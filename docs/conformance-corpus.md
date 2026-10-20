# Core's conformance vectors, and the shared corpus that carries a copy

Three questions are asked about the conformance vectors this repository publishes, and they have
three different answerers. Keeping them apart is the whole of Phase 172; until then all three were
answered by one mechanism, which is why "Core is generic" was true of the packages and false of the
gate.

| Question | Who answers it | Where |
|---|---|---|
| **Is the artefact the suite runs Core's own?** | the default suite, over the committed `conformance/` directory in THIS checkout — no other repository is read; every committed file is held to its exporter row's render by one reading (Phase 508) | `tests/Fuaran.Core.Tests/ExportersTests.fs` (freshness); `LawVectorTests.fs`, `ApplyVectorTests.fs`, the `apply/` preservation differential in `ProofOracle.PreservationTests.fs` (the oracle) |
| **Are the corpus copies fresh?** | the workspace copy registry, from `copies.json` at this repository's root (warn-first, on every workspace sweep); and the in-suite legs — the `laws/` one runs whenever a corpus is present and REPORTS, failing where it is asked for (CI, on every push); the `apply/` one is still opt-in | `copies.json`; the two `… is fresh …` legs in `LawVectorTests.fs` / `ApplyVectorTests.fs` |
| **Are the hosts certified?** | each host's own certification kit, run in that host's repository against the corpus at the paths it has always read; `apply/manifest.json` records per-host adoption | not this repository's question — see [Adoption](#adoption) |

## What this repository owns

```
conformance/
  laws/capability-laws.json    the capability-law vectors (`render laws`, since Phase 235)
  laws/decimal-laws.json       the exact decimal's documents (`render laws`, since Phase 276)
  laws/field-laws.json         the schema field's documents (`render laws`, since Phase 427)
  laws/propagation-laws.json   the propagation vectors (`render laws`, since Phase 538)
  apply/skeleton-apply.json    the skeleton-op apply contract (`render apply`)
  apply/manifest.json          the apply family's own index + per-host adoption (`render apply`)
  refusals/codec-refusals.json the codec refusal vectors: JSON grammar, surrogates, the decode-determinism rows (Phase 462), column cells, the wire-profile grammar (`render refusals`, since Phase 299; profiles since Phase 306)
  refusals/manifest.json       the refusals family's own index + per-host adoption (`render refusals`)
  decode/decode-rejects.json   the decode error contract: every decode code at the path it names, and the syntax rows at the shape reader (`render decode`, since Phase 310)
  decode/manifest.json         the decode family's own index + per-host adoption (`render decode`)
  escape/string-escape.json   the string-escape table as the lines a host diffs (`render escape`, since Phase 349; no corpus copy yet)
```

### Which law sets Core emits — all of them, and the UI tier emits none

Core is the reference for every law family it ships, so it emits every law set a host certifies a
reimplementation against. Since Phase 538 that is **four** families under `laws/`, all written by
the tool's `render laws` (`LawVectorExport.emitted` in `tests/Fuaran.Core.Tests.Fixtures/`):

| Family | File | Shape |
|---|---|---|
| `capabilityLaws` | `laws/capability-laws.json` | a SELF-CONTAINED family — the `(input, expected verdict)` pairs the law draws from its seed |
| `decimal` | `laws/decimal-laws.json` | AUTHORED documents — inputs chosen to reach every behaviour DECISIONS.md D72 pins, each answer computed by the kit; no seed |
| `fieldLaws` | `laws/field-laws.json` | AUTHORED documents — the schema field's canonical bytes, decode and refusals (Phase 427); no seed |
| `propagationLaws` | `laws/propagation-laws.json` | a DRAWN family — the cases `propagationEvalLaws` draws from its seed, by the families' own generator, each answer computed by calling `Fuaran.Core.Propagation` (Phase 538) |

#### The `propagationLaws` family: what a host must reproduce

A host that reimplements `Fuaran.Core.Propagation` certifies against `laws/propagation-laws.json`.
Every input carries its graph as `deps` (each id to the ids it reads), so a vector replays without the
draw; the file's description also states the draw, for a host that re-runs the families over the same
sample. The evaluator is `value(n) = base(n) + Σ value(r)` over `n`'s declared reads, a read that
resolves to nothing counting `0`. The four cases:

| `case` | Input | A host must answer |
|---|---|---|
| `dirtySet` | `deps`, `changed` | the changed ids and every node that transitively reads one, sorted |
| `evaluationOrder` | `deps` | the dependencies-first `order` and the cyclic groups (`cycles`, each sorted) — a cycle is data, never a divergence; a host whose sort breaks ties differently holds its own order to `validTopo`'s three clauses instead |
| `evalFrom` | `deps`, `base`, `changed`, `changedBase` | the incremental re-evaluation over the prior `eval` of `base`: `values` (equal to a full evaluation of `changedBase`) and `recomputed`, exactly the dirty set — or `evalUnknownChange` naming a changed id the graph does not hold |
| `undeclaredRead` | `deps`, `base`, `node`, `read` | `evalUndeclaredRead` naming the node and the read, from `eval` and from `evalFrom` alike, whether the read names a node of the graph or an id it does not hold |

The corpus's first copy, its `laws/manifest.json` row and the producer record are the UI tier's corpus
commit (fuaran#2328); `LawVectorExport.manifestRows` lists the row in that change-set, because a row
named before the index holds it would refuse every `render laws <corpus dir>` until then.

#### The `decimal` family: what a host must reproduce

A host that mirrors the exact decimal (`ColumnType.DecimalType`, `Cell.Decimal`, `DecimalText`)
certifies against `laws/decimal-laws.json`. Every vector carries `id`, `case`, `input` and
`expected`; `expected.verdict` is `accept` or `reject`, and a `reject` names its class in
`expected.error`. The refusals are vectors like any other: a host that accepts what the kit refuses
disagrees with it. The seven cases:

| `case` | Input | A host must answer |
|---|---|---|
| `canonical` | `text` | the canonical form (K3) — or `notDecimal` for any text outside `-?[0-9]+(\.[0-9]+)?` with ASCII digits (K4: a leading `+`, a bare point, an exponent, a separator, white space, a non-ASCII digit) |
| `compare` | `a`, `b` | the numeric `order`, `-1` / `0` / `1`, including pairs one double cannot tell apart — or `notDecimal` |
| `add` | `a`, `b` | the exact canonical `sum`: carries through the point, widening carries, narrowing borrows, cancellation to an unsigned `0`, mixed signs in both orders, scales and magnitudes past any host decimal |
| `toFloat` | `text` | the nearest double in the canonical float layout — or `pastFloatRange` where the magnitude is past the float range, never an infinity (K7) |
| `codecDecode` | `document` | the canonical re-encoding of a one-column decimal document — a decimal is a JSON string, an integer token is read exactly, and a fractional token or a whole token past 2^53 is refused (K5), with the codec's error class |
| `codecEncode` | `cells` | the guarded encode's canonical bytes for column `c` of type `decimal` — or the refusal of a cell whose text is not canonical (`MalformedShape`) |
| `aggregate` | `fn`, `cells` | the result cell's token (`m:` decimal, `i:` int, `f:` float): `sum` exact, `min` / `max` the winning cell as it stands, `mean` / `median` / `stddev` (population) at each value's nearest double, `countDistinct` over canonical values — or `AggregateOverflow` for a value past the float range, `CellOutsideType` for text that is not decimal |

A `cells` entry is `{ "kind", "text" }` — `decimal` (the text as written, canonical or not), `int`,
or `null` (no text) — rather than a `Cell.token`, because the token canonicalises and the encode's
refusal of a non-canonical cell is one of the behaviours to reproduce. The file is stamped with
`kitVersion` like `capability-laws.json` and re-stamped with it on a version move. The same inputs
are rows of the cross-pipeline value table (`ParityVectors`, `decimal/`, `decimalCodec/`,
`decimalAggregate/`), so the receiving gate's value leg compares them under Fable as well.

**`transformLaws` (`laws/transform-laws.json`) was the second until Phase 258.** It is a PARITY
family — the `Fuaran.Core.DataFrame` reference evaluator's ANSWERS over a declared sample — and it
left with that evaluator (DECISIONS.md D66, D70): [`Fuaran-Core/fuaran-core-compute`](https://github.com/Fuaran-Core/fuaran-core-compute) emits it, stamps it
with its own `<Version>` and declares the corpus copy as its derived file. The corpus path is
unchanged, so no host reads a different place; only its producer moved.

**No UI-tier repository emits a law set.** Until Phase 235 `capability-laws.json` was exported by
the UI tier's test project, against whatever Core version that repository pinned — so the reference
for Core behaviour was produced one repository and one pin away from Core, and a Core change that
moved a capability vector (Phase 225 did) could not re-emit it in the same change-set. That exporter
is retired; the UI tier now READS the corpus copy and certifies its pinned kit against it, as every
other host does. A future law set Core is the reference for is added here, beside these — as the
propagation set fuaran#1764 planned was (Phase 538) — never as a second exporter in a host repository.

`laws/manifest.json` in the corpus, the index over every family in `laws/`, stays hand-curated as an
INDEX: no exporter renders it, because it lists families this repository does not own and a
wholesale renderer would drop them. But its rows for Core's families repeat members DERIVED from
the files they name — `kitVersion`, `vectors`, and `seed` / `iterations` for the drawn family — and
since Phase 394 `render laws <corpus dir>` restamps exactly those members of exactly those rows in
the same act that writes the files, so a `<Version>` move can no longer leave a row naming the
previous kit beside a file stamped with the new one (the lag that was hand-edited at every cut until
then). The edit is surgical and self-checking: a row that is absent, repeated, or missing a derived
member is REFUSED before anything is written (adding a family to the index, or a member to a row, is
an edit made once, by hand), and the result must parse to the original with only those members
replaced. The in-suite leg "the corpus laws/manifest.json rows describe the files beside them" holds
each row to the file beside it, wherever a corpus is present, under the same ask as the copy legs.

**The capability copy lags by ruling, until fuaran#1860.** Phase 225 changed every capture key's
value, and `capability-laws.json` pins literal keys. The committed file here carries this kit's keys;
the corpus copy still carries the `0.30.0` ones the TS and Go ports and the UI tier's pinned kit
certify against, and it is re-synced with both ports at the UI tier's Core pin raise (fuaran#1860).
Until then the registry names the copy stale, and the in-suite leg reads it as that recorded lag
rather than failing: every line must be byte-identical to this renderer's output except the stamp and
the `key` value of the `invocationKey` vectors, so the move itself stays pinned byte for byte, and
any other difference is reported as a divergence.

Every file is EMITTED, never hand-edited: each expectation is computed by calling the reference
evaluator or `Ops.apply`, and the suite holds the committed bytes to a fresh render on every run.
The corpus — <https://github.com/fuaran-ui/fuaran-ui-specification>, resolved on disk under the
directory name `wire-format-fixtures` — carries the same files at `laws/` and `apply/`, and
those are **declared copies**: `copies.json` names each source, its copy's workspace path, the
`fingerprint` equality it is held to, and the command that refreshes it. The hosts keep reading the
corpus at the same paths with the same bytes; a copy is what they were always reading, and this
names it. Nothing moved OUT of the corpus.

### Emitting

```powershell
# the source of truth — this repository's conformance/ (what the default suite certifies)
dotnet run --project tools/Fuaran.Core.Tools -- render laws
dotnet run --project tools/Fuaran.Core.Tools -- render apply

# the corpus copy — the same exporter, pointed at a corpus checkout
dotnet run --project tools/Fuaran.Core.Tools -- render laws <corpus dir>
dotnet run --project tools/Fuaran.Core.Tools -- render apply <corpus dir>
```

Since Phase 508 every family is one row of ONE exporter table (`tests/Fuaran.Core.Tests.Fixtures/Exporters.fs`),
and since Phase 532 the one writer is the tool (`tools/Fuaran.Core.Tools`), not the test runner: the
verb is `render <family>` — `laws`, `apply`, `refusals`, `decode`, `escape`, `encoding` or `families`.
`copies.json`, `version-derives.json` and the projection registry quote that form, rendered from one
spelling (`Tool.fs`); the suite holds the committed files and writes none of them.

### The exporter table (Phase 508)

One table, one writer, one manifest schema, one freshness reading:

- **A row** names the family, the directory its files live in when no target is named
  (`conformance/`, or `docs/` for the roster's two artefacts), every file it writes with the text it
  renders, and — only where writing is more than writing those texts — its own writer: `laws` restamps
  the corpus's shared `laws/manifest.json` rows (Phase 394), and `apply` merges its row into the corpus's
  shared `apply/manifest.json` beside other producers' rows.
- **The writer** is `CorpusFile.render` (internal to `Fuaran.Core.Conformance` until its first outside adopter publishes it, D171): a header member per line, then one
  array member with an item per line, LF endings and a trailing newline. Every key, string and number,
  and every compact item, goes through `Canonical.write Rendering.CrossHost`, so a file escapes by the spine's one string rule.
  An item is either a spaced object or array in author order (`CorpusCell.Row`, `CorpusCell.List`: the
  vectors of `laws/` and `apply/`, every manifest row) or a value in `Canonical.write Rendering.CrossHost`'s compact form
  (`CorpusCell.Value`: the vectors of `refusals/` and `decode/`, the lines of `escape/` and
  `encoding/`). No exporter writes JSON of its own. The hand writer the `laws/` and `apply/` files were
  written with until then spelled `\n`, `\t`, `\b`, `\f` and `\r` short where the spine writes `\u00xx`;
  no committed vector carries a control character, so moving onto the one writer moved no byte of any
  file the corpus copies.
- **The manifest schema** is `CorpusManifest` — a version, a description, and one `CorpusManifestRow` per
  family (`id`, `kind`, `file`, `vectors`, `adoption`, `description`, in that order, the adoption a spaced
  object in roster order). `apply/`, `refusals/` and `decode/` all write it; the latter two wrote their
  adoption map in `Canon.render`'s compact sorted form until Phase 508, and moved onto the apply
  manifest's shape (neither file is copied into the corpus). The corpus's `laws/manifest.json` is not a
  self-enumerated manifest: it indexes families other producers own and is restamped, never rendered.
- **The freshness reading** is `CorpusFile.firstDifference`: a committed file is fresh when it equals its
  row's render once CRLF is read as LF, and a stale one is named by its first differing line, with the
  command that rewrites it. The `families` row reads both texts through the copy registry's fingerprint
  first, as its two files always were. `Exporters.tests` holds every row's committed files to it; each
  family's own tests keep the ORACLE question — is what the file records still true of the code? — and
  no longer compare bytes.

A new family is one row. The four host-compliance phases that were parked behind this table land on it:
fuaran-core#478 (every answer the oracle can check stamped as agreed by the model) is a member on the
rows it stamps, written by the same writer; #479 (the chain, DAG, merge and diff vector families) is
rows; #481 (the limits vector family) is one row; and #484 (each host declares its compliance and Core
validates it) is a validator over the one manifest schema. fuaran-core#477's adoption record replaces
the `adoption` member's value in `CorpusManifestRow`, in one place, for every manifest at once.

A tool verb rather than a test side-effect: the corpus is a separate repository, and a suite that wrote
into it on every run would dirty a shared clone. Note the name — the UI language repository's
`--emit-corpus` renders THAT domain's node vectors and knows nothing about this engine; the apply
semantics and the capability laws are Core's, so Core emits them.

A change to what this kit renders is not finished until BOTH have been written and committed: the
in-repo file in the same commit as the change (the default suite is red otherwise), and the corpus
copy in the corpus repository. The two repositories are one change-set; the registry names the copy
as stale, with that exact command, for as long as the second half is outstanding.

### The `kitVersion` stamp

The `laws/` file carries the producing kit's version, and hosts read it, so the stamp
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
dotnet run --project tools/Fuaran.Core.Tools -- render laws

# 2. re-stamp the corpus copy (and, since Phase 394, its laws/manifest.json rows) with the same
#    exporter, pointed at the corpus checkout
dotnet run --project tools/Fuaran.Core.Tools -- render laws ../Fuaran-UI/wire-format-fixtures

# 3. commit and push the CORPUS first — it is a separate, public repository
# 4. bump the pin to that corpus commit (see "Bumping the pin") and commit it IN THE SAME COMMIT as
#    the <Version> move and the re-emitted conformance/ — then push this repository
```

Since Phase 394 CI certifies against the pinned corpus, not the corpus's `main`, so step 4 is what
makes CI see step 2 at all: a version move whose commit does not also move the pin is red on its own
push (the pinned copy carries the old stamp, and a stamp-only mismatch is fatal there, D50) — which
is the point. The red is attributed to the commit that caused it, never to a later one.

Step 1 is not optional and never was: the default suite holds the committed file to a fresh render,
so a version move without it is red in this repository immediately. Step 2 is the one a session
forgets, because until Phase 216 nothing here said anything about it. Now the ordinary `verify.ps1`
run prints the copy's stamp, this kit's stamp and both commands, whenever a corpus is checked out
beside this one — a report locally, a failure in CI. The interval between the two pushes is real and
cannot be closed (two repositories, two permission sets), but it now starts at a moment the session
knows about — and since Phase 394 it cannot red this repository's `main` at all: until the pin
moves, CI reads the corpus the pin names, and the pin moves in the commit that also moves
`<Version>`.

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

**Ruling (C), restated against the pin (Phase 394).** Before the pin, the stale reading this
repository could observe ran from a Core cut to whatever the corpus branch happened to hold when a
run checked it out — a window nobody opened or closed on purpose, which could also be closed or
reopened by a corpus push with no Core commit at all. Now the corpus Core certifies against moves
only when the pin does, so the window (C) governs on Core's side runs **from a Core cut to a pin
bump**, and both ends are commits in this repository. The procedure above puts them in ONE commit,
so on `main` the window is empty by construction; a cut committed without its bump is red on its
own push, and attributed to it. The host side of (C) — a host certifying against the corpus at its
own pin of Core, until it raises — is unchanged: that is adoption, and it was never this
repository's window to close.

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
governs an unreachable sibling copy elsewhere in the workspace: a single-repository checkout has no
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

`render fstar` (the former `--emit-fstar`) no longer reads the corpus at all (Phase 173): the generated F\* models and proof
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

`.github/workflows/ci.yml` checks the corpus out beside the repository checkout **at the pinned
commit**, names it in `FUARAN_CORE_CORPUS_DIR`, and sets `FUARAN_CORE_CORPUS_FRESHNESS`, in its
`verify` job, which runs the whole suite. Its `proofs` job reads no corpus: it passes
`-SkipOracleHost`, so the corpus-reading host families run only in `verify` (Phase 499).
`publish-packages.yml` checks the corpus out in its `proofs` and `publish` jobs, reading the pin from
the TAGGED commit, because both run the families that read it. So every push compares against the corpus this
repository chose, and drift between this kit and that corpus — a stale copy, a model that no longer
agrees with production over the domain's documents, a vocabulary that moved under the committed F\*
model — surfaces on the change that caused it instead of on somebody else's change days later. A
machine holding only this repository runs the default suite and is green; that is the acceptance
the phase was cut for.

**A stamp-only mismatch is FATAL there, by operator ruling (2026-09-21, DECISIONS D50), and the
workflow is deliberately unchanged by Phase 216** — fatal is the status quo, so an edit to it would
have been a change away from the ruling rather than toward it.

## The pin

Until Phase 394 every one of those checkouts named no `ref:`, so it took whatever the corpus's
default branch held at that moment. A corpus push — a re-stamp, a vector re-sync — could red this
repository's `main` with no commit here, and could refuse a tag's publish; and a corpus regression
could be certified against by accident. The corpus is now a **pinned build input**: `main` goes red
only for a change in this repository or for a deliberate bump (DECISIONS.md D126).

The pin is the `corpus` record of `copies.json`, the file that already declares the corpus copies:

```json
"corpus": {
  "repository": "fuaran-ui/fuaran-ui-specification",
  "sha": "<the corpus commit, 40 lowercase hex>",
  "date": "<yyyy-MM-dd of the last bump>",
  "reason": "<the change that needed it>"
}
```

Two readers hold it to the same four rules — the repository is exactly the corpus, the SHA is a full
lowercase commit SHA (never a branch, never an abbreviation), the date is `yyyy-MM-dd`, the reason
is not blank — and the suite runs the first against the second over valid and malformed records so
they cannot drift apart:

- **every workflow checkout** runs `.github/scripts/corpus-pin.ps1` first and passes its outputs as
  the checkout's `repository:` and `ref:`; an absent or malformed record fails that step by name, and
  the checkout never runs unpinned. `FUARAN_CORE_CORPUS_DIR` and `FUARAN_CORE_CORPUS_FRESHNESS` are
  unchanged;
- **the suite** (`SiblingCorpus.pin` / `driftAt` / `grade`) reads the record and, wherever a corpus
  is present, says where that checkout stands against it with BOTH SHAs, so a contributor sees which
  side moved before a red corpus leg tells them nothing:

| The checkout is… | Unasked (local) | Asked (`FUARAN_CORE_CORPUS_FRESHNESS`, CI) |
|---|---|---|
| at the pin | holds | holds |
| AHEAD of the pin (descends from it) | WARN — the normal state while a corpus change is in flight | WARN |
| BEHIND the pin, DIVERGED from it, or NOT CARRYING it | WARN, saying it would fail where asked | **FAIL** |
| not a git checkout of its own | WARN | **FAIL** — the position cannot be proved |

The copy-freshness legs' reports carry the same one-line reading, so a `laws/` finding says which
side moved too. In CI the checkout IS the pin, so the reading holds by construction; it earns its
keep on a contributor's clone.

### Bumping the pin

Moving the pin is a deliberate act, and it travels with the change that needs it:

1. **Land the corpus change first** — in the corpus repository, pushed, so the commit is reachable on
   its `origin`. A pin naming a commit only one clone holds fails every CI checkout.
2. **Edit the record**: `sha` to that commit (`git -C <corpus> rev-parse HEAD`, the full 40 hex), `date`
   to today, `reason` to the change that needs it.
3. **Re-run the corpus legs** against a checkout AT the new pin, asked for by name:

   ```powershell
   git -C <corpus> checkout <new sha>
   $env:FUARAN_CORE_CORPUS_FRESHNESS = '1'
   $env:FUARAN_CORE_CORPUS_DIR = '<corpus>'
   pwsh ./verify.ps1
   ```

4. **Commit the pin with the change that needs it, in ONE commit whose message names both SHAs** —
   `corpus pin <old sha> -> <new sha>: <reason>`. A version cut (above), a re-emitted copy, a law
   that changed what the corpus must carry: whatever made the bump necessary is in that commit, so
   the commit that moves what Core certifies against is the commit that explains why.

A bump whose only reason is "the corpus moved" is not a reason. Adopting new corpus content on
purpose — fixtures the proof-oracle pools should read, a re-synced copy — is a change that needs the
bump, and the `reason` names it; a corpus commit nobody here has a use for yet is one Core does not
need to certify against, and the AHEAD warning on a local clone is the honest report of that state.

## The JSON-syntax layer every wire cites (normative, Phase 462) {#json-syntax-layer}

Every Fuaran wire — the UI wire, the program wire, the model-execution wire, the column and query
documents, the op-stream records — is JSON below its own vocabulary, and this section is the ONE
statement of what that JSON is. A wire specification cites it by this heading and restates nothing;
Core's reference reader answers it at every decode entry point, the `refusals/` family carries one
vector per row and per boundary acceptance, and the `decode/` family carries each row at the shape
reader. (The UI wire's section 20 was the first statement of the rows; its 20.1 records why a row
binds an ENTRY POINT and not a host, and this section inherits that framing.)

### The rows

A conformant document is one RFC 8259 text, read strictly. Each row below is a refusal of the TEXT
— `InvalidJson` at the root in the decode error contract, with a `JsonErrorKind` naming the class —
and none can be reached by a conformant encoder: every input class is one no host can emit, which is
why the rows cost no legitimate document and is the admission test a further row must meet.

| # | Input class | Core's answer | `JsonErrorKind` |
|---|---|---|---|
| 1 | A **repeated member name** in one object (`{"id":"a","id":"b"}`), names compared after unescaping | refused at the repeated name's opening quote, when the member is accumulated (after its value is read) | `DuplicateMember` |
| 2 | **Content after the root value** (`{}x`, `1 2`, `{} }`) | refused at the first non-whitespace character after the value | `TrailingCharacters` |
| 3 | A number token outside the **RFC 8259 grammar**: leading `+`, leading zero, no integer part, no fractional digits after the point, no digits after the exponent | refused | `MalformedNumber` (`UnexpectedChar` where the token cannot start a value: `+1`, `.5`) |
| 4 | A **bare `NaN` / `Infinity` / `-Infinity`** literal | refused: not a JSON token | `UnexpectedChar` (`MalformedNumber` for `-Infinity`, whose sign starts a number) |
| 5 | A **raw C0 control character** (U+0000 to U+001F) unescaped inside a string | refused at the character, before any other string test | `BadEscape` |
| 6 | An **unpaired surrogate**, raw or escaped — a high not followed at once by a low, a low with no high before it | refused | `BadEscape` |
| 7 | An **overflowing exponent** (`1e999`) | refused: Core's value model has no non-finite float, so this is the VALUE domain's refusal, not the syntax's | `MalformedNumber` |
| 8 | A **quoted sentinel** (`"NaN"`, `"Infinity"`, `"-Infinity"`) | a string; what a typed float slot makes of it is the citing wire's | — |

Three rules sit beside the rows:

- **Every entry point answers alike.** There are four readers over one scanner (Phase 368), each
  taking the read's `ReadOptions` (its null policy and its limits record; Phase 519): `Json.read` (the
  parser's own refusal), `Json.parse` (its one string form, under `ReadOptions.strict`), `Reader.read`
  (typed values, a shape mismatch `WrongKind` at the value's path) and `Decoder.parse` (the decode
  error contract). A test holds each of them, under both null policies, to one answer per `json`
  vector in `refusals/`. A reader a wire host carries beside Core's (the generated TypeScript reader is one)
  answers every row identically or declares the row it diverges on as a conformance gap, never as a
  second dialect.
- **An erased member null is not an occurrence of its name.** Under `EraseMemberNull`
  (`ReadOptions.tolerantOfNull`), `{"a":null,"a":1}` and `{"a":1,"a":null}` both read as `{"a":1}`: the
  tolerant read is the strict read of the document with its member nulls deleted (a proved bridge,
  Phase 190), and the deletion leaves no name behind. Under the strict policy the same documents are
  the null's refusal. Decision record: DECISIONS.md D157.
- **Row 7 is where Core is stricter than a wire may be.** A wire that admits a non-finite number at
  its syntax layer (the UI wire's section 20.2 row 7 does) states that one divergence beside its
  citation; a wire that inherits Core's answer states nothing.

### The limits

Core states no limit VALUES and is not a host of any wire's limits section (DECISIONS.md D84, ruled
2026-10-01; refined by D159, 2026-10-15): its 512 nesting cap is its own stack guard, deliberately
looser than any wire's bound, so Core never refuses a document a wire says a host must accept, and its
refusal there is the `Depth` breach at the container opened past it, exactly as under a host's record
(`LimitExceeded` at that path in the decode error contract; Phase 519). What Core offers is the
MECHANISM (Phase 465): every reader takes a `JsonLimits` record in its `ReadOptions` (`Json.read`, and
`Decoder.parse` and `Reader.read` over it) and enforces each bound ON THE WAY DOWN, before the
container, unit, item or member it guards is read, so nothing past a bound is ever read or built. Each
wire keeps its own values, units and error envelope and cites Core for the mechanism alone; a host
passes its specification's numbers and maps the breach to its own envelope.

| Bound | Counts | Breach raised |
|---|---|---|
| `MaxDepth` (`depth`) | nested objects and arrays admitted, the root counting as one | at the bracket of the container opened past the bound; path: that container's |
| `MaxStringLength` (`stringLength`) | Unicode code points in ONE decoded string or member key — a surrogate pair is one, whichever spelling it arrived in | just past the code point that passes the bound, before it is appended; path: the string value's own, or the object's for a key |
| `MaxArrayLength` (`arrayLength`) | items in one array | where the `max + 1`-th item would start, before a character of it is read; path: the array's |
| `MaxObjectMembers` (`objectMembers`) | members in one object (an erased member null is not a member) | at the `max + 1`-th key's opening quote; path: the object's |
| `MaxDocumentBytes` (`documentBytes`) | UTF-8 bytes of the whole input | before parsing, at position 0; path: the root |

A breach is `JsonRefusal.LimitExceeded` — a `JsonLimitBreach` naming the limit, its declared value, the
path and the position, never the input — and `LimitExceeded` at that path in the decode error contract,
with a sentence naming the limit and the value (`limit arrayLength of 2 exceeded`). It is NEVER
`InvalidJson`, and malformed text is never a breach: `JsonRefusal.Malformed` carries the parser's
classified refusal of the text. The first fault in document order wins, whichever kind it is.
`ReadOptions.strict` carries `JsonLimits.depthOnly 512`, under which only the depth cap can be
passed. The `decode/` family carries one vector per bound at the bound (accepted) and one past it, each
declaring its `limits` record, with the astral and CJK vectors that fail a host whose unit is wrong by
name. The model (`proofs/JsonParse.fst`, section 13) proves the limited entry point at no bound is the
parser, that a breach is never reported as malformed, and the string and items bounds exact on their
canonical families. **A stricter host-operational ceiling is deployment policy, not conformance:** a
host that applies one documents it and never describes a document it refuses under it as malformed.

### The refusal classes

The decode error contract (`decode/`) carries a row's refusal as `InvalidJson` at the root; the
parser's `JsonErrorKind` names the class inside it, closed at thirteen cases (`UnexpectedChar`,
`UnexpectedEndOfInput`, `ExpectedToken`, `UnterminatedString`, `UnterminatedEscape`,
`TruncatedUnicodeEscape`, `BadEscape`, `BadHexDigit`, `MalformedNumber`, `NullNotRepresentable`,
`MaxDepthExceeded`, `TrailingCharacters`, `DuplicateMember`). A host reproduces the class per vector;
the sentence beside it is the host's own. The vectors are emitted, never hand-edited
(`render refusals`, `render decode`), and the manifests are authoritative for the counts.

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
| `NotAContainer` | `Ops.apply` / `canApply` under a contained scope, at the insert's parent or the graft's interior | `target`, `kindTag` | the host's one container refusal |
| `TargetNotAContainer` | `Diff.toOps` under a contained scope, at the first offending node of `after` | `target`, `kindTag` | the same class |

Both are defined by `Ops.firstUncontained` and carry one payload, the offender's id and its own kind
tag (Phase 228 renamed the diff case's field from `parent`). So a host that maps one of them has
mapped both. `Conformance.diffContainedLaws` holds the two to the same trees: wherever the diff
refuses with `TargetNotAContainer(t, k)`, it builds the same graft, and the contained `Ops.apply` must refuse
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

## The structural families the corpus carries and Core does not emit (Phase 479)

The shared corpus's `chain/`, `dag/`, `merge-conformance/` and `diff/` families exercise mechanisms
this package owns: the chain payload and hash, the DAG content address, the three-way merge and its
conflict shapes, and the structural diff. **They are not Core's to emit**, and no exporter row
renders them. Every vector in them carries bytes only a UI-tier codec writes. The chain records hash
the UI's stream-entry envelope over a UI `TreeOp`. The DAG records nest a UI `TreeOp` and result
envelope inside the address pre-image. The merge fixtures are UI trees with a `SemanticStyle` blend
and a sample domain validator. The diff goldens use UI node kinds and ops `SkeletonOp` does not have.
A Core render would restate those codecs in this repository's tests. The inventory and the reasoning
are DECISIONS.md D191.

What a port with no UI tier certifies against is the Core-owned share, which is already here:

| Mechanism | Core-owned vectors |
|---|---|
| the linear chain pre-image, per actor | `escape/string-escape.json` |
| an actor as folded into a chain payload and a DAG node id, per encoding profile | `encoding/encoding-profiles.json` |
| skeleton ops over a witness with no domain payload | `apply/skeleton-apply.json` |

A family that certifies a chain walk, a DAG fold, a merge or a skeleton diff end to end over the
skeleton witness does not exist yet. It would be a new family with its own row in the exporter table,
not a copy of the UI corpus's four.
