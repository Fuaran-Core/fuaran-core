# Core's conformance vectors, and the shared corpus that carries a copy

Three questions are asked about the conformance vectors this repository publishes, and they have
three different answerers. Keeping them apart is the whole of Phase 172; until then all three were
answered by one mechanism, which is why "Core is generic" was true of the packages and false of the
gate.

| Question | Who answers it | Where |
|---|---|---|
| **Is the artefact the suite runs Core's own?** | the default suite, over the committed `conformance/` directory in THIS checkout — no other repository is read | `tests/Fuaran.Core.Tests/LawVectorTests.fs`, `ApplyVectorTests.fs`, the `apply/` preservation differential in `ProofOracleTests.fs` |
| **Are the corpus copies fresh?** | the workspace copy registry, from `copies.json` at this repository's root (`roadmapctl copies <workspace-root>`, warn-first, on every estate sweep); and an opt-in in-suite leg that FAILS on it where the corpus is present and asked for — CI, on every push | `copies.json`; the two `… is fresh (opt-in: FUARAN_CORE_CORPUS_FRESHNESS)` legs |
| **Are the hosts certified?** | each host's own certification kit, run in that host's repository against the corpus at the paths it has always read; `apply/manifest.json` records per-host adoption | not this repository's question — see [Adoption](#adoption) |

## What this repository owns

```
conformance/
  laws/transform-laws.json     the transform-law reference vectors (`--emit-laws`)
  apply/skeleton-apply.json    the skeleton-op apply contract (`--emit-apply`)
  apply/manifest.json          the apply family's own index + per-host adoption (`--emit-apply`)
```

Every file is EMITTED, never hand-edited: each expectation is computed by calling the reference
evaluator or `Ops.apply`, and the suite holds the committed bytes to a fresh render on every run.
The corpus — <https://github.com/fuaran-ui/fuaran-ui-specification>, resolved on disk under the
directory name `wire-format-fixtures` — carries the same three files at `laws/` and `apply/`, and
those are **declared copies**: `copies.json` names each source, its copy's workspace path, the
`fingerprint` equality it is held to, and the command that refreshes it. The hosts keep reading the
corpus at the same paths with the same bytes; a copy is what they were always reading, and this
names it. Nothing moved OUT of the corpus.

### Emitting

```powershell
# the source of truth — this repository's conformance/ (what the default suite certifies)
dotnet run --project tests/Fuaran.Core.Tests -- --emit-laws
dotnet run --project tests/Fuaran.Core.Tests -- --emit-apply

# the corpus copy — the same exporter, pointed at a corpus checkout
dotnet run --project tests/Fuaran.Core.Tests -- --emit-laws <corpus dir>
dotnet run --project tests/Fuaran.Core.Tests -- --emit-apply <corpus dir>
```

A flag rather than a test side-effect: the corpus is a separate repository, and a suite that wrote
into it on every run would dirty a shared clone. Note the name — the UI language repository's
`--emit-corpus` renders THAT domain's node vectors and knows nothing about this engine; the apply
semantics and the transform laws are Core's, so Core emits them.

A change to what this kit renders is not finished until BOTH have been written and committed: the
in-repo file in the same commit as the change (the default suite is red otherwise), and the corpus
copy in the corpus repository. The two repositories are one change-set; the registry names the copy
as stale, with that exact command, for as long as the second half is outstanding.

### The `kitVersion` stamp

`transform-laws.json` carries the producing kit's version, and two hosts read it, so the stamp
stays in the file and the file's bytes are what the copy must match. What Phase 172 changes is
WHERE a `<Version>` move goes red: the stamp lives in this repository's committed file, so a version
cut re-emits it in the same commit and Core's own gate never crosses a repository boundary to fail.
The corpus copy is then reported **stale by fingerprint** — on the sweep from every checkout, and by
the opt-in leg where the corpus is present — until the copy is refreshed; that is the copy being
stale, named, with its remedy, rather than an unrelated phase's gate going red days later (the
Phase 139 finding). The `apply/` family carries no stamp at all, for the reason its exporter's
header gives: a specification whose every expectation is recomputed on each run cannot go stale for
a reason unrelated to its content.

## The opt-in live-corpus leg

```powershell
$env:FUARAN_CORE_CORPUS_FRESHNESS = '1'
```

Set, the suite runs every leg that needs the LIVE shared corpus; unset (the default), each of those
legs reports itself **not asked for**, by that name, and says that nothing was compared against the
shared corpus. What the leg covers — everything in this suite that reads `wire-format-fixtures/`,
which is exactly the set of reads that is NOT about Core's own vectors:

- the two copy-freshness legs above (`laws/`, `apply/`), the registry's `fingerprint` equality;
- the proof-oracle differentials that use the domain's fixture pools as real-document input —
  `nodes/`, `ops/`, `dag/`, `envelope/` and the pinned `idl.json` — beside their generative legs,
  which run regardless;
- the IDL spike's drift guard (its vendored `snapshots/` against the live `nodes/`);
- the F\* target's partition over the pinned vocabulary and the generation diff that holds the
  committed `proofs/Vocabulary.fst` to a fresh generation from the pinned `idl.json` (the
  `Proofs.Vocabulary` step of `proofs/check.ps1`).

**Once asked for, an absent corpus FAILS. It does not skip.** That is Phase 130's decision (D31),
kept on the leg it was written for: a skip nobody asked for is indistinguishable in a green report
from a comparison that ran, and four consecutive worktree gates once certified against a corpus
none of them had read. The failure names every path it tried and the remedy. The old blanket
opt-out `FUARAN_CORE_SKIP_CORPUS` is retired — with nothing left to skip by default, there was
nothing for it to say.

`--emit-fstar` is a command, not a leg: it reads the pinned `idl.json` because you ran it, and an
invocation is its own ask. It uses the corpus locator directly and is not gated.

### Getting the corpus

Either of these is found with no further configuration once the leg is asked for:

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

`FUARAN_CORE_CORPUS_DIR` is a LOCATOR, not an ask: it says where the corpus is, and only
`FUARAN_CORE_CORPUS_FRESHNESS` says whether this run reads it. It must point at the corpus **root** —
the directory holding `manifest.json` and the family directories — not at a family. A directory that
is not the corpus is refused by name rather than searched past: acceptance reads the `schema` and
`idl` documents the corpus's own manifest declares, so an index that merely *mentions* those
families cannot pass for one.

The lookup is anchored at the repository's **main working tree**, which git answers identically
from every worktree of a repository (`git rev-parse --git-common-dir`, whose parent is the main
tree) — the Phase 130 correction to a climb that started from wherever the binary was running and
therefore never reached the corpus from a linked worktree.

### What CI does

`.github/workflows/ci.yml` checks the corpus out beside the repository checkout, names it in
`FUARAN_CORE_CORPUS_DIR`, and sets `FUARAN_CORE_CORPUS_FRESHNESS`, in both the `verify` and the
`proofs` jobs. So every push still compares against the corpus at its own `main`, and drift between
this kit and the published corpus — a stale copy, a model that no longer agrees with production over
the domain's documents, a vocabulary that moved under the committed F\* model — surfaces on the
change that caused it instead of on somebody else's change days later. A machine holding only this
repository runs the default suite and is green; that is the acceptance the phase was cut for.

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
