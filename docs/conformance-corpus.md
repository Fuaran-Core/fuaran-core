# Core's conformance vectors, and the shared corpus that carries a copy

Three questions are asked about the conformance vectors this repository publishes, and they have
three different answerers. Keeping them apart is the whole of Phase 172; until then all three were
answered by one mechanism, which is why "Core is generic" was true of the packages and false of the
gate.

| Question | Who answers it | Where |
|---|---|---|
| **Is the artefact the suite runs Core's own?** | the default suite, over the committed `conformance/` directory in THIS checkout — no other repository is read | `tests/Fuaran.Core.Tests/LawVectorTests.fs`, `ApplyVectorTests.fs`, the `apply/` preservation differential in `ProofOracleTests.fs` |
| **Are the corpus copies fresh?** | the workspace copy registry, from `copies.json` at this repository's root (`roadmapctl copies <workspace-root>`, warn-first, on every estate sweep); and the in-suite legs — the `laws/` one runs whenever a corpus is present and REPORTS, failing where it is asked for (CI, on every push); the `apply/` one is still opt-in | `copies.json`; the two `… is fresh …` legs in `LawVectorTests.fs` / `ApplyVectorTests.fs` |
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
the in-suite leg where the corpus is present — until the copy is refreshed; that is the copy being
stale, named, with its remedy, rather than an unrelated phase's gate going red days later (the
Phase 139 finding). The `apply/` family carries no stamp at all, for the reason its exporter's
header gives: a specification whose every expectation is recomputed on each run cannot go stale for
a reason unrelated to its content.

#### Moving `<Version>`: the sequence, in one sitting

The stamp is DERIVED, so **every** move of `<Version>` — a cut, a draft advance, anything — restales
the corpus copy, in a repository this gate cannot write to. Three times on 2026-09-21 a session
moved the version, got a green local gate, and reddened `main` on the next push from a run belonging
to somebody else. Phase 216 moved the discovery; the sequence it discovers is this:

```powershell
# 1. move <Version> in Directory.Build.props, then re-emit the source of truth
dotnet run --project tests/Fuaran.Core.Tests -- --emit-laws

# 2. re-stamp the corpus copy with the same exporter, pointed at the corpus checkout
dotnet run --project tests/Fuaran.Core.Tests -- --emit-laws ../Fuaran-UI/wire-format-fixtures

# 3. commit BOTH, and push BOTH — they are two repositories, and the corpus is a public one
```

Step 1 is not optional and never was: the default suite holds the committed file to a fresh render,
so a version move without it is red in this repository immediately. Step 2 is the one a session
forgets, because until Phase 216 nothing here said anything about it. Now the ordinary `verify.ps1`
run prints the copy's stamp, this kit's stamp and both commands, whenever a corpus is checked out
beside this one — a report locally, a failure in CI. The interval between the two pushes is real and
cannot be closed (two repositories, two permission sets), but it now starts at a moment the session
knows about.

**Two readings, and they are never merged into one sentence.** A copy whose **vectors** differ
records answers this kit's reference evaluator no longer gives — a content divergence, on the file
five hosts certify against. A copy whose vectors are byte-identical and whose **stamp alone** differs
is the derived-lockstep case, which is what a version move causes and all it causes. Both are
reported, both are fatal where the leg is asked for, and the report names which one it is — because
a stamp move must never be able to hide a content divergence behind it, and the remedies read
differently even though the commands are the same.

#### How this differs from the cut-to-raise window

The 2026-09-15 bundle ruled (C) on a different window, and the two are easy to conflate. That one is
the gap between a Core **cut** and a **host's raise**: a host still pinning the previous Core is
correct to certify against what it pins, its copy of a law file is right for that pin, and the gap
is an honest signal to be documented rather than closed. It is a property of adoption, and a window
there is always legitimate until the host raises.

The stamp has **no such window**. It is not a consumer's lag; it is a value derived from `<Version>`
inside a file this repository emits, and there is no state of the world in which the two
repositories carrying different stamps for the same content is correct. So ruling (C) is untouched
by anything above: it governs when a host adopts, this governs when a producer emits. What Phase
216 also settled is that the two cannot both hold while the leg merely FAILS with one sentence —
"accept the stale reading" and "CI is red" cannot both be true of the same report — which is why
the readings are separated whatever else is decided.

## The opt-in live-corpus leg

```powershell
$env:FUARAN_CORE_CORPUS_FRESHNESS = '1'
```

Set, the suite runs every leg that needs the LIVE shared corpus; unset (the default), each of those
legs reports itself **not asked for**, by that name, and says that nothing was compared against the
shared corpus. What the leg covers — everything in this suite that reads `wire-format-fixtures/`,
which is exactly the set of reads that is NOT about Core's own vectors:

- the `apply/` copy-freshness leg, the registry's `fingerprint` equality;
- the proof-oracle differentials that use the domain's fixture pools as real-document input —
  `nodes/`, `ops/`, `dag/`, `envelope/` and the pinned `idl.json` — beside their generative legs,
  which run regardless;
- the IDL spike's drift guard (its vendored `snapshots/` against the live `nodes/`);
- the F\* target's partition over the pinned vocabulary and the generation diff that holds the
  committed `proofs/Vocabulary.fst` to a fresh generation from the pinned `idl.json` (the
  `Proofs.Vocabulary` step of `proofs/check.ps1`).

**The `laws/` copy-freshness leg is no longer in that set (Phase 216).** It is decided by the
corpus's PRESENCE: a corpus checked out beside this one is compared whether or not the leg was asked
for, and `FUARAN_CORE_CORPUS_FRESHNESS` decides only whether a finding FAILS the run. With no corpus
anywhere the leg says **NOT CHECKED**, by name — never a quiet pass, which is the same rule that
governs an unreachable sibling copy elsewhere in the estate: a single-repository checkout has no
siblings at all, and "nothing to check" must not read as "everything checked". A machine holding only
this repository is still green.

The asymmetry with `apply/` is deliberate rather than an oversight. The class Phase 216 closes is
the DERIVED stamp restaling a copy on every version move, and `apply/` carries no stamp: its copy
goes stale only when the family's own content changes, which is a thing the emitting session is
already doing on purpose. The argument for moving it too is the general one — discover a coupling
where it is caused — and it is a separate, smaller change than this one.

**Once asked for, an absent corpus FAILS. It does not skip.** That is Phase 130's decision (D31),
kept on the leg it was written for: a skip nobody asked for is indistinguishable in a green report
from a comparison that ran, and four consecutive worktree gates once certified against a corpus
none of them had read. The failure names every path it tried and the remedy. The old blanket
opt-out `FUARAN_CORE_SKIP_CORPUS` is retired — with nothing left to skip by default, there was
nothing for it to say.

`--emit-fstar` no longer reads the corpus at all (Phase 173): the generated F\* models and proof
scripts under `proofs/` are emitted from the certification vocabularies in the test project, and
the `Proofs.Vocabulary` generation diff is not a corpus leg and is not gated on the ask.

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

**A stamp-only mismatch is FATAL there, by operator ruling (2026-09-21, DECISIONS D50), and the
workflow is deliberately unchanged by Phase 216** — fatal is the status quo, so an edit to it would
have been a change away from the ruling rather than toward it.

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

One correspondence about that refusal is pinned all the same, because it holds inside Core rather
than across hosts. Core raises the graft-containment shape under two names: `Rejection.NotAContainer`
when an op is applied, and `Diff.DiffError.TargetNotAContainer` when a diff is derived. **The two map
to ONE host class**, the one a host raises for its container refusal (`ChildlessKind` on the hosts
named above).

| Core class | Raised by | Payload | Host class |
|---|---|---|---|
| `NotAContainer` | `Ops.applyContained` / `canApplyContained`, at the insert's parent or the graft's interior | `target`, `kindTag` | the host's one container refusal |
| `TargetNotAContainer` | `Diff.toOpsContained`, at the first offending node of `after` | `target`, `kindTag` | the same class |

Both are defined by `Ops.firstUncontained` and carry one payload, the offender's id and its own kind
tag (Phase 228 renamed the diff case's field from `parent`). So a host that maps one of them has
mapped both. `Conformance.diffContainedLaws` holds the two to the same trees: wherever the diff
refuses with `TargetNotAContainer(t, k)`, it builds the same graft, and `applyContained` must refuse
it with `NotAContainer(t, k)`.

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
