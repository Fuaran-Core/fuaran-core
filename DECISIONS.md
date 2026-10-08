# Fuaran.Core — decisions (newest first)

## 2026-10-08 — D134: a value space has ONE reader spelling at `1.0.0` — the descriptor read D133 kept leaves, model and oracle with it (amends D133)

**Recorded by Phase 405. `Fuaran.Core.Function` (`SpaceCodec.decoder`), `proofs/Capability.fst` and
its extracted oracle, the suite; the `1.0.0` slot (`docs/releases/1.0.0.md`,
`docs/migrations/1.0.0.md`). A wire `removal` on a reader. No writer, digest, baseline or
canonical document moves.**

*Amended: D133's "Kept: the lenient descriptor read of a value space, to `2.0.0`."* D133 kept the
read because removing it was "a model edit, a re-verification and a re-extraction", not a deletion.
That is a cost, and the major is where it is paid. Keeping it would have frozen two reader spellings
of one wire type for the whole 1.x line, and every other `1.0.0` move took the opposite rule: one
spelling, no forwards. So it leaves on the same slot. D133's paragraph stands as the record of why
it was deferred by one phase, and the `1.0.0` ledger records the removal.

*Decided: `SpaceCodec.decoder` dispatches on `"$type"` only.* A document with a `"kind"` and no
`"$type"` is refused with the codec's existing fault, `MissingField` at `$type`, the same refusal
any document without its tag gets. No new fault and no special sentence is added for the old
spelling. A sentence that names the descriptor would keep the second spelling in the reader's
vocabulary, which is what this decision removes. The internal case table lost its parameters for
the string-length member names. They existed only to serve the second arm.

*Decided: the model states the one spelling.* `space_of_j` in `proofs/Capability.fst` is now the
`"$type"` dispatch itself. The two-arm wrapper and the parameterised `space_cases` are gone, so no
lemma reasons over an arm production no longer has. `space_roundtrip` and the decoded-value lemmas
are unchanged in statement and re-verified: `check.ps1 -Modules Capability -Extract -Runs 3`, three
cold runs (102s, 97s and 97s against the 290s budget), every query 3/3 under `--quake`, at the
pinned rlimit, on the first iteration. The pinning vector
`the-descriptor-spelling-of-a-space-is-read-leniently` became
`the-descriptor-spelling-of-a-space-is-refused-at-type`. It is a twin the prover normalises and the
oracle host evaluates, and its suite counterpart holds production to the same refusal for every
space.

*Unchanged, and why: the descriptor WRITER.* `SpaceCodec.descriptorJson` still writes the `"kind"`
spelling for `Function.toSchema`, because `ContentPack.signatureFingerprint` hashes those bytes
(D104). It is a descriptor, written and never decoded. Every decode site in the repository
(`CapabilityCodec`, the typed refusals and `CapabilityPipelineCodec`) already read documents
`toJson` wrote, and no consumer reads a value space from `toSchema` output. That was checked
before the reader was cut.

## 2026-10-08 — D133: `1.0.0` freezes with no obsolete forward, three shapes close, D101 becomes a gate, and the `OneDotZero` family makes "1.0" a test output

**Recorded by Phase 386. Every package with a forward, `Fuaran.Core.Function`, `Fuaran.Core.Query`,
`Fuaran.Core.OpStream.Dag`, the conformance kit and the suite; opens the `1.0.0` slot
(`docs/releases/1.0.0.md`, `docs/migrations/1.0.0.md`). No wire byte moves except the retired
`RowCodec` document.**

*Decided: a forward leaves at a major, and `1.0.0` is one.* Through the `0.x` line every forward was
written "for one draft" or "removed at 1.0.0" and nothing held the promise. This slot removes every
`System.Obsolete` member the shipped assemblies carry — 44, found by reflection — and the one-draft
items that carried no attribute: the string-error `Decode` helpers, the `Validator.Registry` alias
and the `Fuaran.Core.Observer` adapter namespace. The adapter is not "zero uses", as the shard read
it: the observer family certified it and its own suite drove it. Both went with it, because D101
already decided the adapter was leaving and subscription is host state. The observer family keeps
the witness half of its first law — every emission and snapshot is the derivation of its input —
and its cycle law.

*Decided: the version is semver from `1.0.0`.* A breaking surface class advances the major, an
additive one the minor. A member a minor retires stays as a `System.Obsolete` forward to the next
major (STABILITY.md, "Versioning policy").

*Decided: `OneDotZero` is the gate, and it is vacuous by name at major 0.* Four laws over the live
tree: no public member carries `ObsoleteAttribute` (reflection over the packable roster — the API
baselines do not render attributes, so `grep Obsolete api/*.txt` would have certified nothing);
`unfrozenWitnesses` is empty; `frozenWitnessFields` is byte-equal to the major's `vN.0.0` tag,
through a committed render the suite holds to the live list at every major (so the tag carries the
list as it stood); and no baseline moved by a breaking class since the newest tag unless the major
advanced. While the `vN.0.0` tag does not exist yet the third law says so by name rather than
passing. Each law has a go-red plant, and the first was watched red at `<Version>1.0.0` before the
sweep.

*Decided: the registries and the DAG close.* `CapabilityRegistry` and `QueryRegistry` take
`FunctionRegistry`'s opaque shape (Phase 316): `register` is the only way in, so no capability
reaches dispatch without the admission gate. `Dag.T`'s constructor is private and `Dag.ofNodes`
refuses a key that is not its node's id; a node whose id does not match its content is admitted,
because that is what `firstBreak` exists to find. `Nodes` stays readable as a member.

*Decided: D101 is a gate, and its rule is the shadowing one.* No case name may be carried
UNQUALIFIED by two public unions of the shipped assemblies or FSharp.Core's option and result (a
qualified union may share any name). Qualifying `PipelineError`, `PipelineEvalError` and
`SchemaCompat` — the shard's three — left three more collisions the rule names. `Versioning.Evolution`
(`Additive`, `Breaking`), `WireNullTolerance.Claim` (`Rejected`) and `Diff.Strength` (`Required`) are
qualified too. `Strength` and not `Idl.Optionality`, because every IDL declaration spells
`Required`. The case was live: `Diff.fs` read `Required` as `Strength.Required` only because its
union was declared later.

*Found: the surface gate cannot see a qualification.* `[<RequireQualifiedAccess>]` changes no IL
token the renderer draws, so six source-breaking moves read as `unchanged` against `v0.36.0`. The
release ledger names them by hand. The renderer drawing the attribute is the successor's to build;
every RQA union's baseline line would move once, in the same commit.

*Kept: the lenient descriptor read of a value space, to `2.0.0`.* The shard listed it as a one-draft
item. The verified model of that reader (`proofs/Capability.fst`) pins the descriptor spelling as an
accepted input, with a named vector, and the extracted oracle is held to production. Removing it is
a model edit, a re-verification and a re-extraction. It is not a deletion, so the read stays,
documented as leaving at `2.0.0` with its vector.

*Kept, not built: `witnessFieldsLaw` and `witnessCoverageLaw`.* They left the facade, so the
conformance facade no longer publishes a `System.Type`, but they stay in the kit (`SurfaceLaws`):
`witnessSurfaceLaws ()` runs both, and the suite reaches them through a test-only
`InternalsVisibleTo` to drive its decoy records.

*Not built: a test-only copy of the retired snapshot matrix as a forward.* The snapshot suite and
the compaction oracle's differentials still speak the matrix's shape, and the model pins its fault
strings, so the suite keeps a test-local translation onto `OpStream.Snapshots`
(`tests/Fuaran.Core.Tests/SnapshotMatrix.fs`). Nothing ships from it.

## 2026-10-08 — D132: a kind projection carries its message map and declares its record's fields, in one widening; an emission that cannot read a projected record refuses by name

**Recorded by Phase 403. `Gen.KindProjection`, `Emit/FSharpDerive.fs`, `Emit/FSharpCodec.fs`
(`witnessDecl`), `SupportArtifact` (`mapMsg`, `recordFields`), `Diff`; held by `IdlDeriveTests`, "Phase
403 - a projected kind under the derivations". `Fuaran.Core.Idl.Codegen` moves `record-widening`; the
`supportArtifact` wire baseline moves `additive`.**

*The defect.* A kind projection (Phase 945) replaces a kind's record, encoder and decoder with host
source. Two things followed that the generator could not do. `Derivation.MapMsg` refused a vocabulary
with a projected kind, and the projection had no member through which the host could supply the map,
so a host kept a hand-written message map beside its generated layer. And `SlotsOf` read the kind's
WIRE fields as if they were the record's: where a wire key is optional and the record's member is
required, it emitted `match s.On with Some …` against a required `On`, which does not compile.

*Why the defect was wider than its shard said.* The shard named `SlotsOf` and asked that every
derivation over a projected kind read the record "or refuse by name". Read at HEAD, the node witness
(emitted in every module, privately or as `StructuralAccess`) and `KeyedPositions` read the same wire
fields through the same `k.Fields`. They had compiled for the one projection in use only because its
node-bearing wire keys and record members happen to share names and shapes. All three are the one
defect and are fixed under one rule.

*The decision: BOTH members, in ONE widening.*

- `MapMsg: string option` — the `and private mapMsg<Tag>Spec …` member, verbatim, joined to the derived
  message map's recursion group. It is host source for the reason `Mk` is: the record is host source,
  and a map the generator built from a guessed shape would be a guess.
- `RecordFields: IdlField list option` — the record's fields at their host shapes, read by the witness,
  the keyed walk and the slot enumerators in place of the wire fields.

Refusal alone was considered and rejected. It would have taken `StructuralAccess` away from the one
host that uses a projection, whose projected kind's wire holds a node, with no way back short of a
second widening. Adding a field to `KindProjection` breaks every full literal (`FS0764`) whenever it
happens, so taking both in the slot that freezes the 1.0 surface costs a host one edit instead of two
across a frozen surface. `RecordFields` was not made required: a projection whose wire holds no node,
in a module requesting no slot enumerator, needs no declaration, and forcing one would add ceremony
with nothing to check.

*The refusal rules when a member is `None`.*

- `MapMsg = None`: `Derivation.MapMsg` over that kind refuses with the message it always had ("the
  message map over the projected kind …"). The generator does not guess a map.
- `RecordFields = None`:
  - the node witness and `StructuralAccess` refuse when the kind's wire holds a node DIRECTLY;
  - `KeyedPositions` refuses when it holds one ANYWHERE;
  - `SlotsOf` refuses ALWAYS.

  The node rules read the wire because a projection's encoder writes the wire: a node on no wire key is
  a node no host reads back, so a node-free wire says the record holds none. No such argument exists
  for a value of a declared type, because a record can hold one its wire spells otherwise (two wire keys
  merged into one member). So the slot enumerator admits no undeclared projected kind.
- A declared `RecordFields` naming a type the module does not declare is refused by name, whatever is
  requested.

*Rejected alternatives.* Deriving the map from `RecordFields` was rejected: it would put a second route
to the same member beside the one the host writes, and the refusal would no longer mean "no map was
supplied". Parsing `SpecDecl` for field shapes was rejected: it is verbatim host source, and reading
types out of it would be a second F# parser in the generator. A `support.json` encoding-version bump was
rejected: both keys are optional and absent when undeclared, so every existing document reads and
renders byte for byte. That is the posture `annotations` took in `idl.json`.

## 2026-10-08 — D131: a strict proof run records cost and refuses a cached read; one cached-read threshold replaces the per-module floors

**Recorded by Phase 399, on an operator ruling of 2026-10-08. `proofs/kit/check-proof-leg.ps1`,
`proofs/modules.json` (`cachedRead`), `proofs/check.ps1`, `.github/workflows/proofs-strict.yml`; held
by the `C` arms of `proofs/kit/check-proof-leg.tests.ps1`. No package or wire byte moves.**

*The ruling (the operator's).* `-Strict` had been answering two questions with one switch. **A cost
overrun is not a strict failure.** A module at 110% of its budget is a slow cold check, which is the
opposite of a warm one. Under `-Strict` an overrun is a recorded `COST` finding: it is printed, it is
written to the strict baseline with its percentage, and the scheduled strict run trends it. It never
turns the leg red. Budgets are neither raised to make a run pass nor deleted; they stay the trend's
reference. **The hard gate is "this was not a real cold verification"**, and it is detected by a
cached-read threshold measured on the machine, not by a percentage of a time. The Phase 402 cache
provenance check stays the primary signal. The threshold is its backstop for a writer active during
one invocation. The scheduled strict workflow follows the same rules: it reports cost overruns as
findings and fails only on a cached read or a model that does not verify.

*The measurement.* On the pinned prover, on the local Windows dev machine that recorded the first
strict baseline (other sessions active), ten modules were cold-checked in roster order into one
fresh cache, and each was then re-run three times against the populated cache with the leg's own
flags. The cached reads took 0.18s to 0.51s. Cold checks of the same modules took 0.38s (`Limits`),
1.61s (`Skeleton`), and 29.8s to 184.0s for the rest. The kit tests' one-line models check cold in
about 0.2s. Every cold check printed at least three `Quake:` query lines, and every cached read
printed none. The figures are in `proofs/modules.json` `cachedRead.measured` and in the
`proofs/README.md` section "Cached read or cold check".

*The decision.* The threshold is `cachedRead.thresholdSeconds` = **1.0s**, about twice the slowest
cached read. It is checked before a module's green line is printed.

*Tiny modules, checked first.* Some models genuinely check faster than a second cold, so a flat
threshold would refuse real cold checks as cached reads. The evidence: on the strict baseline run
(`fe50742`) `Limits` checked cold in 1s, 0s and 0s and `Skeleton` in 1s, 1s and 1s; in the
measurement above `Limits` took 0.38s cold, which is inside the cached-read band; the kit tests'
one-line models check cold in about 0.2s. Two fixes were considered. **A threshold relative to each
module's own recorded cold time** was rejected: it is the floor again, one number per module per
machine, and it broke on the first faster runner. **An exemption for the modules whose genuine cold
check can fall into the cached-read band** was adopted, because that is the same cut the floors'
`zeroBelowSeconds` made, now stated against the measured band. The threshold applies to a module
whose recorded `fastestSeconds` is at least `cachedRead.appliesFromFastestSeconds` = **3s**, three
times the threshold. Below that, a module rests on the provenance check alone, and the leg names
those modules at start-up: today `Skeleton`, `Limits` and `WireVersioning`. The threefold margin
covers a machine about twice as fast as this one, which is what the Linux runner measured
(`WireColumn`, 16s against a 17s floor seeded on Windows). The smallest module the threshold judges
is `VocabularyVectors`, which checked cold in 3s on all three strict-baseline runs.

*Held by* the kit tests' `C` arms. At the real numbers (1.0s from 3s), a sub-second module's genuine
cold check stays green, and the same slow module that checks green cold is red when re-run warm. The
warm re-run puts its genuine checked file in front of the check through a new `-BeforeInvocation`
test seam, which runs after the provenance check has passed. That is the writer-during-an-invocation
case that only the threshold can see.

*The strict baseline under these rules.* Re-derived from the recorded timings of the `fe50742` run,
the run `proofs/last-strict.json` names. Every model verified with every query 3/3 under `--quake` on
all three runs. No cost overrun is red. The four modules over budget on some run were all on passes
the contention factor labels contended (x1.03, x0.82, x0.89): `WireDecode` 78s/70s, `VocabularyProofs`
33s/30s, `ScoreVocabulary` 41s/40s and `ScoreVocabularyProofs` 103s/90s. Every module the threshold
judges checked cold in 3s or more, well above 1.0s. So the run is green under these rules, as it was
under the old ones.

*The floors are retired, on that evidence.* The per-module `floorSeconds` (Phase 164) and
`floorSeeding.os` (Phase 402) answered the same question with one number per module per machine.
The measurement shows nothing between a cached read and a cold check for such a number to see: a
module is either read back in half a second or checked. A faster machine reads a cache faster still
and stays under the threshold, so no OS gate is needed. A much slower machine could take longer than
the threshold to read a cache, so the gate would miss; that is the safe direction, and the
provenance check still runs. Keeping both mechanisms would mean two gates for one question, one of
which is already known to break on a faster runner. A budget file that still carries `floorSeeding`
or a `floorSeconds` is refused by name. `-NoFloor` is gone. `fastestSeconds` stays, as the
threshold's applicability input, and `floorSeeding.zeroBelowSeconds` moved to
`contentionSeeding.minimumSeconds`, the one place it is still read.

*What `-Strict` still turns red.* A coverage or shape finding: a module with no budget, a module
with no `fastestSeconds` when a threshold is declared, a budget for a module the leg does not check,
or a budget file with no `cachedRead` block. These are gaps in a declaration, not measurements of a
machine. The contention factor and its labels are unchanged; they now tell a reader of the strict
record which overruns measure the machine.

*Observed, and left for the operator.* The absence of any `Quake:` line separates a cached read from
a cold check without a clock, and it also covers the sub-second modules the threshold cannot judge.
It was not adopted, because the ruling asked for a threshold and a second gate is a decision to make
on its own. A module with no SMT query at all would print no `Quake:` line when checked cold, so
adopting it would need a declared exemption for such modules.

*The first strict baseline.* `proofs/last-strict.json` records the green `check.ps1 -Runs 3
-Strict` of `fe50742` and stays as it is, by the operator's ruling. A pass of the new code was
started and stopped on the operator's instruction: the new rules change how timings are judged, not
whether the proofs hold, and the re-derivation above settles the judgement. The record predates the
per-module `costs` the new code writes, so the over-budget modules are named here rather than in the
file. The run before it, on the same machine, was red under the old rules on one unlabelled finding:
`DocVocabularyProofs` at 27s against a 20s budget, on a pass at x0.72. That module had grown with
Phase 293 without a re-seed, and Phase 399 re-seeded it from its slowest ordinary observation
(70s/32s) before this ruling. Under this decision that overrun would not have been red either. The
code this decision changes is a leg script, so `-Since` reads the baseline as predating every module.
The next strict run of the new code, scheduled or local, records one that does not.

## 2026-10-08 — D130: `STABILITY.md` is the contract and `docs/releases/<version>.md` the ledger, one file per slot; the contract is held under 1,500 lines, its moved anchors are mapped in a permanent table, and the prose claims are held to the tree

**Recorded by Phase 397. `STABILITY.md`, the new `docs/releases/`, `Directory.Build.props`,
`CONTRIBUTING.md`, `README.md`, `docs/ADOPTION.md`; the tests are `PackageRosterTests`,
`ReleaseRecordTests`, `ReadmeClaimsTests` and `GateProbeTagTests`. No package or wire byte moves.**

*The defect.* `STABILITY.md` had grown to 10,552 lines: a contract and every release's changelog in
one file, sections in no reader's order, and about 540 lines of contracts for packages that left
this repository with Phase 258 still reading as current. `Directory.Build.props` carried a third,
partial copy of the version history — 330 lines of comment ahead of `<Version>`, missing ten
released slots — edited by hand in a file every project imports. And the documents a reader judges
this repository by had drifted from the tree because nothing read them: CONTRIBUTING and the README
promised a "Fable-compile gate" `verify.ps1` has not run since Phase 217; CONTRIBUTING's format
command omitted `samples`, which the check covers; the contract said nineteen assemblies and no
`InternalsVisibleTo` anywhere, against eighteen packable projects and five declarations; it named
`Fuaran.Core.CSharp` (removed by Phase 231) and `Idl.Spike` (gone) as off the Fable surface; several
members it listed as public promises no longer existed here; the README's Fable compiler version was
compared only where the receiving gate's checkout happened to be present, which no CI run has; and
one event — the typed actor folded into the chain hash — was spelt "since Phase 320" in one document
and "as of `0.0.1-alpha.13`" in the other.

*Decided: the split.* The contract stays in `STABILITY.md`: the versioning policy (the release
sequence's home since Phase 392), the surface classes, the load-bearing invariant, the
stability-critical surfaces, the witness freeze, the hash-chain and determinism postures, the
Fable-cleanliness promise, the behavioural postures every version is held to, and the vocabulary
paragraphs. The ledger is `docs/releases/<version>.md`, one file per version slot — every tagged
slot, every slot that was opened and never released, and the standing draft — with
`docs/releases/README.md` as its index. A section whose heading is stamped with the version that
shipped it, or that records a change event, is that version's entry and moved to its slot's file; a
section stating a standing promise about a surface that ships today stayed. The compute-strand
promises (`RowIdentity`, the `Transform` algebra and its closed sets, `ColExpr.Param`,
`Column.aggregate`'s parity with `GroupBy`, and the compute members of the three member lists) moved
to `docs/releases/0.32.0.md`, the last slot this repository produced those packages in — moved, not
removed. The version-history comment left `Directory.Build.props` for the slot files, verbatim, each
under the slot it described; the props file keeps `<Version>`, one sentence pointing at the ledger,
and the stability-record declaration.

*Decided: the ledger is held, not trusted.* The index carries each slot file's heading word for word,
newest first, with a link; a slot without a file, a file without its index entry, a heading that
disagrees with its file, a DRAFT heading on anything but the standing `<Version>`, and a stray file
in the directory are each red by name (`Package roster`). `<FuaranStabilityRecord>` now names the
index, so a downstream version check reading the declared record sees every slot's heading. The
`Release record` family reads the slot files and names the file and line of a fault. Every release
tag now has a file: a slot tagged before `0.25.0` is headed with what its tag proves (the version,
the tagged commit's date, `released`) and says in its body that no entry was written when it was
cut, so the per-tag floor (`entryHeaderFloor`) drops to cover every tag without inventing a record
for any.

*Decided: the contract has a ceiling, and its old anchors a permanent map.* `STABILITY.md` is held to
at most 1,500 lines and to carrying no `## <version>` entry, so a release's entry cannot drift back
into it. Every heading that moved keeps a working anchor in its new file, and the table at the end of
`STABILITY.md`, "Where sections moved", maps each old anchor to its new home — permanently, not for a
quarter, because an external link does not expire on a schedule. The suite resolves every row
against the file it names.

*Decided: the prose claims held to the tree.* `ReadmeClaimsTests` reads: the stages CONTRIBUTING's
and the README's `./verify.ps1` line names against the commands `verify.ps1` runs (a command it does
not recognise is red, so a new stage cannot pass undocumented); CONTRIBUTING's format command
against the check's directories; the feed STABILITY names against the publish workflow's
`--source`; the friend-grant paragraph's packable count, grant counts and parties against the
project files; the Fable section's "off the surface" names against `fable-exclusions.json`, and that
file's names against the projects under `src/`; the typed actor's arrival spelt with both its phase
and its version wherever STABILITY or the adoption guide names it; and every member a contract
bullet names against the public-surface baselines. The README's Fable compiler version is marked
"unverified in this repository's CI" in the README itself, held there, and reported on every run
that cannot compare it, instead of being skipped.

*Not decided here.* Phase 389 writes the vocabulary paragraphs into the contract half; the ceiling
leaves room for them. Opening the `1.0.0` slot is Phase 386's, and it opens a ledger file.

## 2026-10-07 — D129: a Core assembly reaches another's internal member only from a `NoInlining` function; the friend grants stay, and no internal is made public to escape the rule

**Recorded by Phase 402. `Fuaran.Core.Conformance` (`ConfRng`), `Fuaran.Core.Idl.Codegen`
(`CodegenLookup`), `Fuaran.Core.Query` (`FunctionInternals`), `Fuaran.Core.ContentAddress`; the test
is `CrossAssemblyInliningTests`. Rides the `0.36.0` draft with no surface change (STABILITY.md, "A
Release-built consumer no longer fails reaching an internal member").**

*The defect.* The first `main` run of the Release verify leg (ci run 37688313255, Core `785f9db`)
failed nine test families with `MethodAccessException`: a test method was calling `Fuaran.Core.Idl`'s
internal `Xorshift32.seeded`. The tests never named it. In Release, the F# optimiser inlines a public
function's body into the assembly that CALLS it, and `ConfRng.ofSeed` was one call to `seeded`, which
`Fuaran.Core.Conformance` reads through `Fuaran.Core.Idl`'s `InternalsVisibleTo`. The optimiser hides
a function whose body names an internal member of its OWN assembly, so that body is never copied out.
It does not do this for an internal member of ANOTHER assembly, because from the friend's side that
member is visible. So the body was copied into a caller that cannot see the member. That is a
shipped defect, not a test artefact: every Release-built consumer of the conformance kit failed the
same way. A census of the built IL found the same shape at every friend grant between shipped
packages. `QueryRegistry.register` and four of its siblings were single calls into
`Fuaran.Core.Function`'s internal `KeyedRegistry`, and the code generator called `IdlLookup` from
thirty-seven places.

*Decided: the rule is `NoInlining`, uniformly.* A reference from one Core assembly to another's
non-public member sits only in a function marked `[<MethodImpl(MethodImplOptions.NoInlining)>]`. A
body with that mark is never copied into another assembly, so its reference stays in the assembly the
grant was made to. Each friend reads through one small boundary. `ConfRng.ofSeed`, `next` and
`intBelow` carry the mark themselves. The code generator reads `IdlLookup` only through the internal
`CodegenLookup`. `Fuaran.Core.Query` reads `SeamCodec`, `KeyedRegistry` and `RegistryPolicy.admit`
only through the internal `FunctionInternals`. `ContentAddress` mints through one private `mint`.
Each forwarder takes its target's FULL argument list, so the reference is in its own body and not in
a closure it returns. A closure has no attribute to carry.

*Rejected: making the kernel and the lookups public.* That was the other way out, and it would have
frozen at 1.0 a second public generator beside `ConfRng`, and four lookups nobody asked for. It also
cannot be the uniform rule. `Digest.ofSha256Bytes` is internal by the D122 ruling, so that no package
but `Fuaran.Core.Tree` and `Fuaran.Core.ContentAddress` can hand it bytes. A rule that applied to
three grants and not the fourth would need a second rule for the fourth. `NoInlining` adds no surface,
and dropping it later is a non-breaking change, which is the cheaper way round to be wrong before a
freeze.

*Held by:* `CrossAssemblyInliningTests` decodes every shipped assembly's IL in the configuration under
test. It fails on any operand that resolves to another Core assembly's non-public member from a
method without the mark, which includes a closure. The rule is deliberately stricter than "public
functions only". A private helper with no mark can be inlined, inside its own assembly, into a public
function that then carries the reference out. That happens in Release and not in Debug, so a check
keyed on visibility would pass in one configuration and fail in the other. Keyed on the mark, it reads
the same in both. The same family requires every friend grant between shipped assemblies to have at
least one reader. A grant nothing reads is removed, and a scan that has stopped seeing references
goes red rather than passing over nothing. Verified both ways: the suite built in Release before the
fix reproduced CI's `MethodAccessException` locally, and after it the Release test assembly's IL
reaches no Core internal it is not granted. Removing the mark from `mint` reddens the family, naming
the site.

## 2026-10-07 — D128: a query declares a closed conjunction of typed column predicates and a column order; the resolver honours both or refuses by name; the pattern bank does not capture, and the column layer's scalar functions stay where D66 put them

**Recorded by Phase 398 (operator ruling: the recommended shape ships). `Fuaran.Core.Query`,
`Fuaran.Core.Conformance` (`queryLaws`); rides the `0.36.0` draft (STABILITY.md, "A query declares
what it filters and how it orders"). The census behind it is `docs/demand-census.md`.**

*Decided: `Query` gains `Where` and `OrderBy`, both empty by default.* `Where` is a CONJUNCTION of
`ColumnPredicate`s over the fixed scalar set — `EqualTo`, the four range bounds (`GreaterThan` and
`LessThan` exclusive, `AtLeast` and `AtMost` inclusive), `Contains` on a string column, `IsNull` and
`IsNotNull`. `OrderBy` is a list of `SortKey`s, a column and a direction each. An empty member is
absent from the wire, so every declaration written before the members existed encodes byte for byte
as it did, and its capture key is unchanged. The union is closed: a predicate a host needs that is
not here is a ruling, not a host-side extension.

*Decided: a literal is a cell of its column's OWN type, exactly — no widening.* A parameter accepts
an `int` for a `float` (`ColumnType.widens`, Phase 295) because an argument is a value supplied at
call time. A predicate's literal is part of the declaration, and one filter should have one
spelling: with widening, `EqualTo("price", Int 3)` and `EqualTo("price", Float 3.0)` are one filter
under two encodings and two capture keys. A `Null` literal is refused (`IsNull` is the test), and so
is a literal the column codec cannot carry (`Table.validate`'s refusal: a non-finite float, decimal,
date or timestamp text that is not canonical). The wire carries a comparison's literal with its
`type`, so a predicate document reads alone — the resolver's refusal carries one without a schema.

*Decided: the meaning is Core's, and stated where the type is.* A comparison orders by `Cell.compare`
(Phase 315: THE cell order — numbers numerically, NaN last, decimals exactly, strings, dates and
timestamps ordinally, `false` before `true`), so every column type takes the range bounds. `Contains`
is ordinal and case-sensitive: case folding is a culture's, and the pattern bank's case-insensitivity
is a property of matching a request, not of filtering data. A `Null` cell satisfies only `IsNull`.
`Ascending` puts a `Null` first, `Descending` last. A host that cannot give one of these meanings
refuses the predicate; it does not approximate it.

*Decided: one admission gate.* `QueryRegistry.admissionFault` (D111, Phase 385) holds the filter and
the order to the declaration's `ResultSchema`: an undeclared column (`UnknownColumn`), `Contains` on a
column that is not a string (`PredicateNotApplicable`), a literal of another type
(`PredicateTypeMismatch`) or one its column cannot carry (`IllFormedLiteral`), and an order naming a
column twice (`DuplicateSortColumn`). `register`, `replace` and the declaration reader run it, and the
reader reports the refusal at the predicate's or key's path.

*Decided: the resolver receives both, and one that cannot honour them refuses by name.* No new
resolver signature: the resolver is already handed the `Query`, so it reads `Where` and `OrderBy`
there. `ResolveFault` gains `PredicateUnsupported` and `OrderUnsupported`, surfacing as
`QueryError.PredicateNotHonoured` / `OrderNotHonoured`; the capture journals them as their wire
documents and replay answers them back (Phase 385's path). The untyped resolvers (`Query.invoke`,
`QueryRegistry.dispatch` and their paged and captured forms) are handed the same declaration, and
their only refusal stays `Failed`, which is `ExecutionFailed`: a host that must refuse a filter by
name uses the typed forms. Core cannot see whether a resolver applied a filter; the seam makes the
honest answer typed and cheap, and `queryLaws` certifies that a refusal reaches the caller.

*Decided: the capture key sees both (Phase 316's paging precedent).* A non-empty `Where` adds three
fields in front of the bindings — an empty name, the tag `w`, and the canonical text of its
predicates — and a non-empty `OrderBy` the same with `o`. Neither tag is a cell's, nor the page tag
`p`, so the pre-image stays a sequence of self-delimiting triples read by their tags. Without this,
`QueryRegistry.replace` swapping one filter for another under the same id would replay the first
filter's rows for the second.

*Decided (driver ruling, closed in this phase): the F\* model states the new members, concretely.*
As first written the phase left `proofs/Query.fst` modelling `invocationKey` over the id and the
arguments, with a fourth theorem reading the declaration "through its id alone". That is false of
production for a declaration that filters or orders, so the proof row overclaimed. The gap is CLOSED
here rather than handed to a successor. The model carries `predicate`, `sort_direction` and `sort_key`
as closed types (every F# case, no type parameter) and `q_where` / `q_order_by` on the declaration. It
models the admission (`where_fault`, `order_fault`, `admission_fault`, with `register_refuses_shape`)
and the shape fields (`shape_fields`, `key_fields`). Theorem four is restated over the id AND the shape
(`invocation_key_deterministic`, `invocation_key_reads_id_and_shape`). The compatibility claim is its
own lemma (`invocation_key_unshaped`: an empty filter and order key exactly as the pre-398 key). The
injectivity theorems cover (filter and order, page token, arguments) (`invocation_key_injective`,
`invocation_key_page_injective`, `distinct_shapes_distinct_preimages`). What the shape spends is two
renderer premises in `key_premises`, beside the int and float renderers': the filter's and the order's
canonical text are injective. The oracle measures both on the shipped codec through the declaration
round trip. Two things are stated as outside the model rather than modelled: the column layer's
literal-carriability clause (`Table.validate`'s, which `DecimalText.fst` and `WireColumn.fst` own), and
the typed resolver's two refusals (the model carries the untyped resolver, as before). The oracle is
re-extracted, and the differential draws filtered and ordered declarations, comparing the admission's
five refusals and the shaped key byte for byte.

*Decided: operands are literals, not parameter references.* A declaration states its own fixed
filter; a value that varies per call is a parameter, which the resolver already receives typed. A
parameter-bound predicate would make the filter a function of the arguments and move admission from
registration to every call. It is not declined forever: it is the first thing the census's fifth stage
watches for.

*Declined: membership and disjunction.* A value in a set is a disjunction of equalities, and `Where`
is closed under conjunction only. A host takes the set as a parameter. Admitting `Or` makes the
predicate language a tree with a normal form to choose, for a need no consumer has yet shown.

*Declined: captures and alternation inside an anchor, on the pattern bank.* A `{…}` wildcard does not
capture; a value reaches a pattern's `Emit` through the intent's `Args`, which the host parses. The
emission body is the domain's (`PatternCard`, "the pattern content stays domain-side; the core owns
only matching and resolution discipline"), and a typed capture would put the domain's parsing in the
core. Alternation between whole anchors is already expressible: a pattern carries several, and any one
selects it.

*Declined: the column layer's scalar functions.* Substring, concatenation, date deltas, sorting and
filtering as OPERATIONS over a table are the compute repository's (D66, executed by D71). What the
column layer keeps is what a `Where` and an `OrderBy` need to have a meaning: the cell order and the
null test.

## 2026-10-07 — D127: the kit has one entry story — every family answers `LawResult list` and is rostered, a vector family handed no vectors is red by name, and the `…At` rule covers EVERY witness-taking family (two rulings), with the bare names forwarded until `1.0.0`

**Recorded by Phase 390. `Fuaran.Core.Conformance`; rides the `0.36.0` draft (STABILITY.md, "One
entry story for the conformance kit"). Amends D78's naming rule by applying it, not by restating it.
(Filed as D126 on the phase branch; Phase 394 took D126 first.)**

*The rule, as it now stands with no exception.* D78 wrote it: a bare name is the family at its default;
`…At` is the domain-witness form; `…With` is the same laws with a pinned parameter injected, last
before the seed. Phase 390 makes it total and checkable:

- **Every family that takes a witness capability the base contract does not** (roster reason
  `NeedsWitnessCapability` — an `ArtifactWitness`, a `KeyedWitness`, an attestation sink, a lane
  generator, an evaluator, observer, projection, sanitiser or AI-surface witness, a seam witness) **is
  spelled `…At`.** The base-contract families over `NodeWitness` / `StreamWitness` alone keep bare
  names: they are the kit's default run, which is what a bare name means.
- **A configured form is `…With`, never `…AtWith`.** The `…With` of a witness-taking family is the
  `…At` family taking its configuration as a further parameter, last before the seed, and an `…At`
  sibling always stands beside it. That is what the three existing precedents already were
  (`propagationEvaluatorLawsWith`, `keyedArbitrationLawsWith`, `FoldConfluence.laneFoldLawsWith`), so no
  `…With` name moves; the rule names what they already did.
- **A family whose stem is not `…Laws` keeps its stem** (`compositionPilot` → `compositionPilotAt`), and
  a family that is a different law SET over the same witness says so in its stem, not in a suffix the
  rule does not have: `aiSurfaceLawsUnderKitPolicy` (the proposal plumbing under the kit's policy, at the
  domain's witness) is `aiSurfaceKitPolicyLawsAt`.
- **Aggregates are not families.** `certify`, `certifyStream` and `FoldConfluence.certifyFold` answer a
  `ConformanceReport`, are not rostered, and are outside the rule.

`ConformanceVacuityTests`' "naming rule" list holds the roster to it — every live witness-taking family
`…At` or `…With`, every `…With` with its `…At`, no `…AtWith`, no `…At` without a witness — and plants a
violating roster to show it goes red.

*The rulings, recorded.* The phase offered two closures for the six bare witness-taking families the
evaluate-design pass named: apply the rule, or restate D78 as "the `At` suffix marks the seam families
whose bare name is the kit-default form". **Ruled 2026-10-07 by the operator: APPLY it** — the
restatement would freeze an exception an adopter has to learn at `1.0`, and a forward costs one draft.
Checking the roster found eleven more (`attestationLaws`, `compositionLaws`, `compositionPilot`,
`memoLaws`, `memoSoundnessLaws`, `functionVerifyLaws`, `verifyHonestyLaws`, `encoderInjectivityLaws`,
`keyedApplyLaws`, `keyedArbitrationLaws`, `FoldConfluence.laneFoldLaws`), and the rule's test found a
twelfth (`aiSurfaceLawsUnderKitPolicy`). **Ruled the same day, as a second ruling: bring all of them
under the rule in this phase**, choosing the `…With` reading above from D78's text and the existing
precedents. Each gains its `…At` spelling with the same parameters in the same order, witness first; the
bare name is an `[<Obsolete>]` forward naming its replacement and `1.0.0`, kept as a roster row under
its own id and its own guard label, exactly as D78's forwards were, so nothing that pins it moves on the
day the rule lands. **All eighteen forwards are on the record for Phase 386's `1.0.0` removal sweep.**
The claims-ladder rows move their `dischargedBy` to the `…At` ids (`propagation-change-set-and-prior` to
`propagationEvaluatorLawsAt`, `witness-surface-scope` to `keyedApplyLawsAt`); the operation roster and
the coverage exclusions name the `…At` ids (the `Fuaran.Core.AiSurface` exclusion also leaves Phase 297's
own obsolete `aiSurfaceLaws` for `aiSurfaceLawsAt`), so the sweep removes the forwards without moving a
discharge.

*Every family answers `LawResult list` and is rostered — the vector families included.* The pass found
five vector families answering `Corpus.Outcome list`, `Result<unit, string>` or `string list` and said the
roster could not see them. Checked against the tree: four already carried a rostered `laws` (Phases 297,
349, 360, 379); `ParityVectors` alone had none. It gains `laws ()`, stating what makes a row comparable on
whichever pipeline runs it (printable ASCII, one space-free label per row, every sanitiser row `ok`, the
`VEC` rendering); the values stay pinned by the suite and diffed by a consumer, so nothing a host reads
moves. Each fixed-corpus family gains `lawsWith` over a vector set the caller hands it (`laws ()` is
`lawsWith` over the committed corpus), and carries one more law — `<family>: the corpus evaluated at
least one vector` — whose evidence is one assertion per vector, counted where each verdict is built
(D100's rule). A run handed no vectors is therefore red by name; before, it was an empty list, which
every reader reads as green. The stored families (`StoredIdentity.*`, `storedCodecLaws`) were
green over an EMPTY store for the same reason — each law asserted once whatever the store held — and
each now carries `the store holds at least one <record | node | capture | text>`, one assertion per
item: a behavioural change, deliberately, since a store that holds nothing certifies nothing. The
suite plants a zero-vector run of every vector family the roster declares and holds that set to the roster.

*A tautological cell is evidence, not a law.* `Check(true, …)` takes the evidence and asserts nothing,
so the census counted it as a law that held. The two sites (`PlacementTreeLaws`, `KeyedApplyLaws`) are
`Saw()` — the match guard around each is the assertion — and a source scan keeps the shape out of the kit.

*The one entry shape.* `docs/ADOPTION.md` §2d: every family is run the same way — call it, concatenate
the `LawResult list`s, green when every result passed — `Conformance.certify` (or `certifyStream`) for the
base run, each opt-in the roster says is yours, and the vector families' `laws` / `lawsWith` or stored
laws. The internal `LawKit` runner is how a family is WRITTEN, not how one is run, so it stays internal.

## 2026-10-07 — D126: the conformance corpus is a pinned build input — `main` is red only for a change in this repository or a deliberate bump, and `--emit-laws` restamps the corpus manifest rows beside the files

**Recorded by Phase 394. Test project and workflows only (`copies.json`, `.github/workflows/ci.yml`,
`.github/workflows/publish-packages.yml`, `.github/scripts/corpus-pin.ps1`, `SiblingCorpus`,
`LawVectorExport`); no package or wire byte moves. Builds on D31 (an asked-for corpus that is absent
fails) and D50 (a stamp-only mismatch is fatal where asked); restates the 2026-09-15 ruling (C)
against the pin.**

*Decided: the corpus Core certifies against is a SHA Core chose.* Every workflow checkout of
`fuaran-ui/fuaran-ui-specification` — `ci.yml`'s `verify` and `proofs`, `publish-packages.yml`'s
`proofs` and `publish`, four in all — named no `ref:` until now, so it read whatever the corpus's
default branch held at that moment. A corpus push could red this repository's `main` with no commit
here, and refuse a tag's publish; a corpus regression could be certified against by accident. The
`corpus` record of `copies.json` (repository, full SHA, date, reason) is now the one tracked pin;
each checkout reads it through `.github/scripts/corpus-pin.ps1`, which refuses an absent or
malformed record by name before the checkout runs. `FUARAN_CORE_CORPUS_DIR` and
`FUARAN_CORE_CORPUS_FRESHNESS` are unchanged.

*Decided: the pin moves only with the change that needs it.* A bump is one commit carrying the
record's edit and the change that made it necessary, its message naming both SHAs, after the corpus
change is pushed and the corpus legs re-run at the new pin (docs/conformance-corpus.md, "Bumping the
pin"). A `<Version>` move is such a change: the cut, the re-emitted `conformance/` and the bump to
the re-stamped corpus commit travel together, so ruling (C)'s window on Core's side — which used to
run from a cut to whatever the corpus branch held — now runs from a cut to a pin bump, both commits
here, and the procedure makes it empty. The host side of (C) is adoption and is unchanged.

*Decided: the suite reports drift by DIRECTION.* `SiblingCorpus` reads the same record (the suite
runs the workflow step over valid and malformed records to keep the two readers one) and, wherever a
corpus is present, names both SHAs when the checkout is not at the pin: AHEAD is a warning everywhere
(the normal state while a corpus change is in flight); BEHIND, DIVERGED, not carrying the pin, or not
a git checkout of its own warn locally and FAIL where the live leg is asked for. A directory inside
another repository is refused as unreadable rather than read, because git would answer with the
enclosing repository's HEAD. In CI the checkout is the pin, so the reading holds by construction.

*Decided (operator ruling, folded in): the corpus `laws/manifest.json` rows are restamped with the
files.* `LawVectorExport.write` deliberately left the shared index alone, so every `<Version>` move
left Core's rows naming the previous kit beside files stamped with the new one, and the rows were
hand-edited at each cut (most recently at the `0.36.0` slot opening, corpus `8725ce4`). The reason the
index is not rendered still holds — it lists families this repository does not own — so the restamp
is surgical: exactly the derived members (`kitVersion`, `vectors`, and `seed` / `iterations` for the
drawn family) of exactly Core's rows, refusing an absent, repeated or incomplete row before anything
is written, and proved by parsing the result back to the original with only those members replaced.
A corpus-present leg holds each row to the file beside it. Measured on the pinned corpus:
`--emit-laws` into a copy of it whose `decimal` row was set back to `0.35.2` / 74 restored the
manifest byte for byte.

*Rejected: pinning by branch or tag.* A branch is the defect; a corpus tag is a second name the corpus
repository controls and can move. *Rejected: a workflow-level `env:` literal per file.* Four copies of
one value must all move at every bump, and a missed one is a job certifying against a different
corpus with nothing to say so; the pin lives once, in the file the suite already reads. *Rejected: rendering the whole manifest.* It would drop the compute
repository's `transformLaws` row, which this repository does not own.

## 2026-10-07 — D125: a copy D2 does not demand is collapsed into one body, a kernel two packages need lives in the lower one behind `InternalsVisibleTo`, and a public module whose public types are nested in it is not split across files

**Recorded by Phase 388. `Fuaran.Core.OpStream`, `.OpStream.Dag`, `.Ops`, `.Wire`, `.Column`,
`.Function`, `.Query`, `.Idl`, `.Idl.Codegen`, `.Conformance`; rides the `0.36.0` draft (STABILITY.md,
"The copies D2 does not justify collapse into one body each"). Builds on D2; amends D124's "a copy
rather than a call".**

*Decided: D2 justifies exactly the copies it names, and nothing that only visibility produced.* D2
keeps `OpStream` and `Wire` standalone, so the escaper in `Actor.fs` (a copy of `Wire.Json.escape`) and
`Column`'s `fnv1a` stay, each held to its original. The DAG's third escaper did not: the DAG package
references `OpStream`, and only `internal` on the one body forced the copy, so the body is public
(`OpStream.Jsonl.quote`) and the copy is deleted. The same test retired every same-package or
dependent-package duplicate the 2026-10-07 design review listed: one tip walk, one Kahn drain, one
closure walk, one seam-codec helper set, one `Deferred` projection, one re-worded tag dispatch
(`Decoder.tagDispatchWith`, public — two packages above `Wire` need it, and a domain codec with a
sentence of its own needs it too), one exact-decimal reading, one vector-verdict kit, one declaration
lookup.

*Decided: the xorshift32 kernel lives in `Fuaran.Core.Idl`, internal, and `Fuaran.Core.Conformance`
reads it through `InternalsVisibleTo` — one implementation rather than a copy held equal by a test.*
Phase 387 (D124) re-based the IDL sampler on `ConfRng`'s generator as a COPY, because the kit depends
on `Idl` and not the reverse, and pinned the copy to `ConfRng.intBelow` in `ParityVectorTests`. The
dependency direction is exactly the reason the kernel belongs in the lower package: `Xorshift32` (the
step, the seed's warm-up, the top-31-bit draw and the high-bit rejection, generic over how a caller
threads its position) is the one body, and `ConfRng` and `Sample` are each a few lines of threading over
it. The alternative — a small PUBLIC generator in `Idl` that `ConfRng` builds on — was declined: it adds
a second public RNG surface beside `ConfRng`, whose surface is the one a domain is told to use. The
`InternalsVisibleTo` grant follows the `Function` → `Query` and `Tree` → `ContentAddress` precedents
(D122): the packages are cut and versioned together from this repository, and the grant names the
consumer. `Idl` grants `Fuaran.Core.Idl.Codegen` too, for `IdlLookup`. The `ParityVectorTests` pin
stays and now holds the two threadings, not a duplicated arithmetic.

*Found, and not built: two of the five long-file splits cannot keep the public surface.* `SeamLaws`,
`StreamLaws` and `TreeLaws` are internal modules and split cleanly (twelve internal modules; the public
`Conformance` facade's forwards did not move). `Diff` and `FStarTarget` are PUBLIC modules whose public
types are nested in them — `Diff+Snapshot`, `Diff+Change`, `Diff+Verdict`, `FStarTarget+Slot`,
`FStarTarget+VectorModel` and the rest — and F# compiles one module from one file. The descriptor table
and the snapshot reader are written over those types, so a file ahead of `Diff.fs` cannot see them and
a file after it cannot be called by it; the same holds for every `FStar.fs` section over `Slot`.
Splitting either means moving its types out, which renames each in the `api/` baseline — a retype every
consumer pays for a layout. Both files say so at their heads. The same constraint holds for `Dag`
(`DagOpStream.fs`), whose public types are nested in the `Dag` module.

*Recorded for the next reader: the file-length rule needs a surface reason before it can bind.* The
phase's acceptance asked that no `src/` file exceed two thousand lines. The internal TypeScript backend
was split too (`TypeScriptCodec`, `TypeScriptDeclarations`, `TypeScriptDerived`), beyond the five the
phase named, because it is internal and nothing queued names it. After the phase seven files still
exceed the line (`Wire.fs`, `DagOpStream.fs`, `Diff.fs`, `FStar.fs`, `Ops.fs`, `Idl.fs`, `Column.fs`).
Three cannot be split without a retype (above). The other four hold several top-level modules and
could be divided at module boundaries with no surface move, but they are the declared key files of the
open phases queued behind this one, and moving them would leave those phases' footprints naming files
that no longer hold their work — the 401 class, created deliberately. That division is a phase of its
own, sequenced after the phases that edit those files.

## 2026-10-07 — D124: the two cross-runtime claims the suite did not measure are measured — the IDL sampler runs on `ConfRng`'s stream and is pinned in `ParityVectors`, the witness freeze reads no reflection under Fable, and D95's escaper guarantee is scoped to IDL-authored text

**Recorded by Phase 387. `Fuaran.Core.Idl` (`Sample`, the `SourceLit` module doc) and
`Fuaran.Core.Conformance` (`ParityVectors`, `SurfaceLaws`); rides the `0.36.0` draft (STABILITY.md,
"The two cross-runtime claims the suite did not measure are measured"). Amends D95.**

The repository's rule since Phase 217 (D55) is that a cross-pipeline VALUE claim is bought by a
`ParityVectors` row and never asserted. The 2026-10-07 design review found two claims that were
asserted, and one reflection path compiled into the Fable distribution and certified by nothing.

*Decided: the sampler is re-based on the `ConfRng` shape (route (b)), not pinned as it was (route
(a)) — operator ruling.* `Sample` was a `uint64` LCG with a 64-bit multiply, inside a Fable-shipped
package — the arithmetic shape `ConfRng` was moved off at `0.20.0` because Fable cannot carry it — and
it chose by `% n`, the low-bit modulo draw `ConfRng.intBelow` was fixed for at `0.12.0`. Route (a)
would have pinned that generator's draws and kept a second RNG discipline alive beside the kit's
because a vector said it agreed today; route (b) gives every Fable-shipped package one discipline.
The sampler is now `ConfRng`'s xorshift32 (shifts and XOR), seeded by `ConfRng.ofSeed`'s warm-up, and
every bounded choice — a pool pick, a list or map length, a presence coin, a JSON shape — is
`intBelow` by rejection from the high bits. It is a private COPY, not a call, because
`Fuaran.Core.Conformance` references `Fuaran.Core.Idl` and so cannot be referenced back; the copy is
held to the original by `ParityVectorTests` ("the sampler draws ConfRng's stream from the same seed,
by rejection"), which predicts every sampled choice over forty-three seeds from `ConfRng.intBelow`.
The cross-pipeline half is seven `sample/*` rows at the tail of `ParityVectors.vectors`: four
`sample/draws/*` rows over a vocabulary whose every choice is visible in its encoding (a seven-case
enum, where rejection and modulo choose differently), two `sample/nodes/*` digests over a vocabulary
reaching every slot shape the sampler draws, and the typed refusal's text.

*The cost, stated in the `0.12.0` form: every sampled set moved.* The signatures did not; the values
did. A consumer that pinned a sampled vector, or a corpus generated from a seed, sees different nodes.
In this repository that moved the generated F* normaliser facts (`proofs/VocabularyVectors.fst`,
regenerated with `--emit-fstar` and verified by the pinned prover, a perturbed fact refused), and one
non-vacuity demand that had been met by the seed rather than by design: the Phase 347 `__proto__`
corner in `IdlRefusalHostTests` fired only when a mutation's randomly drawn position happened to be
an object. That demand was READ, not re-seeded: the corner is now drawn among the document's object
positions, which every document has.

*Decided: the reflection path is FENCED, not certified by a cited run — operator ruling.*
`witnessFieldsLaw` (`FSharpType.IsRecord` / `GetRecordFields`) and `witnessCoverageLaw` are .NET-only,
with the assembly enumeration that was already fenced. The Fable pipeline's `witnessSurfaceLaws ()`
runs `witnessDeclaredFieldsLaw` per frozen record over `declaredWitnessFields`, a committed list of
each record's fields as reflection reads them — the fact beside `frozenWitnessFields`' policy, same
law name and counterexample on both pipelines. The list is derived from the same reflection and held
to it by `SurfaceLawsTests` ("the committed declared-field list is what reflection reads"). A cited
node run would have certified one compile on one day; a fence is structural, so no future change to
Fable's reflection support can make the family say something different on the two pipelines.

*Decided: D95's guarantee is scoped to IDL-authored text, and the trust boundary is stated where it
lives.* D95 and the `SourceLit` doc said a vocabulary built in code "still cannot put a byte of source
into a generated module". It can, by design: a `THosted` slot's `FSharp` / `Encode` / `Decode`, a
`TFn` slot's `ClosureSig`, and every `support.json` entry are HOST SOURCE, spliced verbatim, because
escaping them would turn code into a string. `Declare.errors` checks a hosted slot only for its
declared wire form and format, and `SupportArtifact.ofJson` checks shape. So the escaper's guarantee
covers what the IDL authors — identifiers, wire spellings, the discriminator, categories, docs,
deprecation prose — and host source is trusted exactly as far as the project that compiles it trusts
its own code: a vocabulary or support file from an untrusted party must not carry it.
`IdlCertificationTests` ("Phase 387 — hosted source is trusted host code, outside the escaper")
plants a hosted body that would be unsafe as data and asserts it lands verbatim and live, with the
same text as a category as the control that reaches no source line.

*Not decided here.* Whether host source should become validatable — a declared allow-list of host
expressions, or a separate signed support channel — is a change of trust boundary, not a correction of
its description; nothing in the demand census asks for it.

## 2026-10-07 — D123: "released" is a gate output — a tag's heading and its receiving-gate record are read by `verify.ps1`, which publish runs; `0.31.0` and `0.35.1` were released without a cited run, and the record says so

**Recorded by Phase 392. Test-only and documentation: no package, surface, wire byte or default moves.**

*The defect.* STABILITY.md's policy (D55) says every version cut cites a green run of the receiving Fable
gate against the candidate, and that a cut whose run is red is not released. Nothing read it. `v0.35.1`
was tagged and published while its entry was headed `## 0.35.1 — DRAFT` and cited no run; the per-tag
property (Phase 205) passed, because it asks only that some heading CONTAIN the version. The `0.35.2`
release then hit both halves of the gesture by hand: a commit that turned the heading before the tag
(`285751b`, red on `main` only because the README's unreleased stamps happened to need a DRAFT heading)
and a tag placed on a commit still headed DRAFT (`c441e76`, red in the publish workflow for the same
incidental reason). A `1.0.0` could have shipped headed DRAFT with no run cited and every test green.

*The ruling.* The heading and the record are gate outputs. The `Release record` family
(`tests/Fuaran.Core.Tests/ReleaseRecordTests.fs`) reads the tags in the checked-out history
(`git tag --merged HEAD`: a tag outside HEAD's history is not this tree's release) and holds:

1. every tag at or above `0.25.0` is headed `## <v> — [title — ]released <yyyy-mm-dd> as `v<v>``; a
   tagged version headed DRAFT is red by name;
2. every entry headed `released … as v<v>` has its tag;
3. the untagged standing `<Version>` is headed `## <v> — DRAFT`, the marker a downstream version check
   reads off `<FuaranStabilityRecord>`, now declared in `Directory.Build.props`;
4. every released slot from `0.31.0` carries a `**Release record` paragraph and one
   `**Receiving gate run:**` paragraph in a fixed grammar: the `fuaran-dotnet` commit, the
   `core-fable.ps1 -CoreVersion <v> -CoreFeed <folder>` invocation, the compile leg's package count and
   the value leg's `<m>/<m>`. The grammar is matched per paragraph, so the document's hard wrap cannot
   hide a record.

`verify.ps1` runs the suite, and the publish workflow runs `verify.ps1` before it packs, so a release
gesture without the heading and the record is refused before anything ships. Each clause is proved red
by a plant over synthetic input, and the live case is planted over the real document with the two
`0.35.2` shapes (the newest tag's heading put back to DRAFT; the same tag withheld). On the tree before
this phase the live case reported seven faults, the `0.35.1` DRAFT among them by name.

*Why `0.31.0` is the record floor, not `0.25.0`.* The entry floor stays `0.25.0` (Phase 205). The
receiving gate exists only from D55: `0.25.0` to `0.30.0` were gated by this repository's own Fable leg
at their tags, so there is no external run to cite, and demanding one would demand invention. `0.31.0`
is the first cut under D55.

*The two releases without a cited run.* Both records were written from evidence, not invented, and both
say `none cited before the tag`; the family admits that wording only for the versions this decision lists
(`releasedWithoutCitedRun`), and reds a listed version whose record does cite a run, so the list cannot
outlive its reason. Growing it is a decision recorded here, never an edit made to let a release pass.

- **`0.31.0`.** The only run against a `0.31.0` candidate is D55's 217.E (`fuaran-dotnet` `2b3a92c`,
  2026-09-24: value leg 164/164, no compile count recorded), two days before the tag and before Phase 220
  and the rest of the slot landed. It certifies an earlier candidate.
- **`0.35.1`.** Tagged at `9601020` (observed 05:38 UTC, 2026-10-06). No cut-time run is recorded, and
  none against the committed host gate could have been green: its smoke program built `QueryRegistry`
  from a full record literal, which does not compile once the record gains `Policy` in that slot, and
  the host adapted it only in `18541c2`, four hours after the tag. The released bytes were later shown
  Fable-clean by the host's pinned run at `0.35.1` (2026-10-07: 19 packages, 419/419 vectors), but that
  is post-release evidence, not the cut-time certification the policy requires. **Whether that evidence
  is accepted as `0.35.1`'s certification, or consumers are pointed past it to `0.35.2` (whose cut-time
  run was green), is an operator decision this phase does not take.** The record states the facts either
  way, and the heading now says what the tag says.

*The release sequence the gate makes mandatory* (STABILITY.md "Versioning policy" carries it as the
procedure): (1) re-stamp the conformance corpus if `<Version>` moved since its last emit; (2) run the
receiving gate green, both legs, against the packed candidate; (3) pack the clean tree; (4) in ONE commit,
write the record and the `Receiving gate run` paragraph, turn the heading to `released`, and regenerate
the README stamps (`CORE_APPROVE_README=1`, `--filter ReadmeClaims`, under a provisional local tag on the
candidate commit, since the stamps are derived from tags); (5) tag THAT commit and push the commit and
the tag together. A tag on an earlier commit is red in publish (clause 1); a pushed flip without its tag
is red on `main` (clause 2).

*Rejected.* Keeping the check inside the README stamp family (`stampFaults`), which is what caught both
`0.35.2` misfires: it fires only while some README stamp names the draft, so a release whose slot adds no
README-named surface would pass a DRAFT tag silently. Reading every tag rather than the merged ones: a
branch cut before a release would then red on a heading it cannot yet contain. Matching the record as
free prose: the existing records spell the run several ways, and a fixed grammar is what lets a reader,
and the gate, find the commit and the counts without interpretation.

## 2026-10-06 — D122: `Canonical` and `Codec<'T>` join the spine in `Wire`; `Digest` is proposed and its placement is an open question, because the renderer and the hash live in two packages D2 keeps apart

**Recorded by Phase 379. `Fuaran.Core.Wire` and the conformance kit; additive (STABILITY.md, `0.35.2`);
no default, signature or rendered byte moves.**

*The proposal.* Three shared kits were asked of the substrate, so that consumers stop carrying their own:
a canonical JSON writer and reader (`Canonical`), a typed digest (`Digest`), and codec combinators that
derive an encoder, a strict decoder and a schema from one declaration (`Codec<'T>`). Each was to enter
only if it meets the membership rule (D51: genericity over the witness and plausible cross-domain use,
never a present consumer count) and to have its placement recorded — the spine, a hub, or declined. A
kit placed outside the spine stops at the record.

*The premises, checked against the tree the phase was cut from (`e04551e`).* What the spine already
provides: the versioned profile and its renderer (`EncodingProfile` `V1` / `V2`, `Json.renderWith`,
`Json.escapeWith`, D120); `Canon.render` (sorted keys, the canonical float, not profiled) and
`Canon.renderOrdered`; the typed decode layer (`Decoder<'T>`, `DecodeError`, D99) beside its string twin
`Decode`; `Corpus.Codec` (an encode and decode PAIR, no schema, string errors); the obsolete `RowCodec`;
SHA-256 in `Fuaran.Core.Tree`'s `Hash` with a value-identical copy in `Fuaran.Core.OpStream` (D2); the
`canonicalFields` key roster and the Phase 314 digest maps, all lowercase hex STRINGS with no algorithm
named; the two rehash verbs (Phases 311, 360) and the `StoredIdentity` families. What consumers carry
instead, counted across the downstream repositories that pin this spine (a coordination plane and its
three companion libraries, an application-composition library, and the UI host): about forty-five private
SHA-256 helper definitions, thirteen of them prefixing `sha256:` by hand and several truncating the hex
to an ad-hoc width; a frozen copy of the `0.30.0` renderer that one plane renders every store through,
and two ledger twins of its escape in sibling libraries; one application-composition codec whose
specification REQUIRES the short escapes; nine further JSON string escapers; two hash chains outside
`OpStream`'s. The typed decode layer is used by exactly one consumer family, through generated code; the
string `Decode` layer by about ten; `Corpus.Codec` by four.

One premise was sharper than written. The proposal asked that key order, number layout and whitespace
be "the profile's, never the caller's". Under both profiles that exist, the member order IS the order the
value was built in: `Json.renderWith` does not sort, and every content-addressed pre-image a consumer
holds (the op payload `{seq, actor, op}` among them) is author-ordered. A `Canonical` that sorted would
move every such id. So the rule as built is that `Canonical` offers NO order, layout or whitespace
parameter — those are fixed by the named profile — and the order the profile fixes is "as built", which
a `Codec<'T>` makes declaration order. A sorted canonical form would be a new profile, by D120's rule.

*`Canonical` — admitted, placed on the spine in `Fuaran.Core.Wire`.* Shape: `write profile`,
`tryWrite profile`, `read`, `isCanonical profile`. Membership: it is generic over every value and every
host — it is the profile's renderer under the name a store reasons with, plus the two things every
store re-derives: the guarded write under a NAMED profile (`Json.tryRender` guards only the current
one) and the canonicity check. It closes the frozen-renderer copies and their twins (a store that writes
under `V1` gets `0.30.0`'s bytes from `Canonical.write V1`; the envelope each wraps them in is the
consumer's and stays there) and every escaper that reproduces one of the two rules. It does NOT close
the application-composition codec's escaper unless its specification's spelling is `V1`'s exactly, nor
any envelope, chain or digest helper.

*`Codec<'T>` — admitted, placed on the spine in `Fuaran.Core.Wire`.* Shape: a record of `Write`,
`ReadAll` (collecting) and `Schema`, built by `Codec` combinators — leaves, `enum`, `list`, `map`,
`refine`, the object builder `record` / `field` / `optField` / `build`, the union builder `case` /
`union` — with `decoder` (the strict reader on the `Decoder<'T>` layer), `write` / `read` over text and
`corpus` (the bridge to `Corpus.Codec`). Membership: generic over `'T`, with no vocabulary of its own; it
is the hand-composed counterpart of the generator's per-vocabulary codecs (Phases 374, 377) for a type
that has no IDL declaration, and it SHARES their decode vocabulary rather than adding one: the codes and
paths are `DecodeError`'s and the collecting order is Phase 377's — undeclared members first (the strict
reader checks them first, so the first defect agrees), then members in declaration order, depth-first.
It closes the hand-written encoder-and-decoder pairs on the string layer and the schema nobody writes. It
does NOT replace the generator for a type that has a declaration, and its schema is a structural subset
(`type`, `items`, `properties`, `required`, `additionalProperties`, `enum`, `const`, `oneOf`) held to an
independent validator, not a second acceptance authority. `RowCodec`'s obsolescence message names it as
the typed-row route.

*`Digest` — proposed, placement OPEN; nothing ships.* Shape proposed: a typed digest (algorithm plus
bytes, `sha256:<hex>` on the wire) with one constructor over canonical bytes under a named profile, so a
consumer cannot hash a rendering it did not pin, and the Phase 314 digests and `canonicalFields` keys
expressed through it where their bytes are unchanged. Membership is not in doubt — it is generic, and it
is the largest single source of copies counted above. Its PLACEMENT is a ruling this phase does not have:
the constructor needs the renderer (`Wire`) and the hash (`Tree`) together, and D2 makes `Wire`
standalone so a wire-only consumer pays for nothing else (D76 and D120 both declined the analogous edge
from `OpStream`). The routes, each with what it costs:

- **A — `Wire` references `Tree`.** The type and its hashing constructors in `Tree`'s `Hash`, so the
  Phase 314 maps are expressed through it in place; the profile-pinned constructor in `Wire`. Cheapest:
  `Tree` is FSharp.Core-only and Fable-clean, and of the first-party packages only `Column` and the IDL
  engine's three reach `Wire` without already reaching `Tree`. It amends D2 for `Wire`, and a published package edge is hard to take back.
- **B — a new package referencing both** (`Wire` and `Tree`), holding the profile-pinned constructor; the
  type in `Tree`, its bytes-level constructor not public, so only the new package and `Tree`'s own digests
  can mint one. D2 stands; one more packable package, its baseline and its roster rows are the cost.
- **C — a SHA-256 copy in `Wire`**, on the copy precedent D2 already uses for `OpStream`. Rejected as a
  route: the type would live in `Wire`, which `Tree` cannot see, so the Phase 314 digests could never be
  expressed through it — the proposal's own acceptance fails.

**The rule this adds.** A kit proposed to the spine records its placement before it ships, and a
placement that needs a package edge D2 forbids is an operator's ruling, not a phase's. The byte-stability
gate for an admitted kit is D120's: committed vectors under every profile, a positive control that
re-renders under the other profile and requires a byte to move, and a family a content-addressed
consumer runs against its own stored corpus (`EncodingProfileVectors.storedCodecLaws`).

**Amended 2026-10-07 (Phase 382) — the operator ruled ROUTE B, and `Digest` ships on it. D2 stands
unamended.** The ruling was the operator's, given 2026-10-07; this phase records it and builds on it, and
chose nothing about the route. What shipped, riding the `0.35.2` draft slot (additive):

- **The type is in `Fuaran.Core.Tree`** (`Hash.fs`): `DigestAlgorithm` (one case, `Sha256`) and `Digest`,
  an algorithm and its lowercase hex with structural equality. Its representation is `internal`, and so is
  its one bytes-level constructor (`Digest.ofSha256Bytes`), so no consumer can digest bytes it did not pin.
  `Tree` grants that constructor to exactly one other assembly, the new package, through
  `InternalsVisibleTo` (the `Function` → `Query` precedent).
- **`Tree`'s own digests mint one in place.** `Digest.tryOfFields` is SHA-256 over `Hash.canonicalFields`'
  injective pre-image — a canonical rendering, not raw bytes — so every key on the `canonicalFields` roster
  that is a SHA-256 (the Phase 314 `Own`, `Frame` and `Subtree` digests, `Tree.ownDigest`,
  `Tree.frameDigest`) is that function of its own fields, byte for byte. It is GUARDED, on D84's rule: a
  field carrying an unpaired surrogate is refused rather than digested as its replacement bytes. No
  existing signature moved: the maps still store strings, and their bytes are the digest's `Hex`.
- **Reading a stored digest is not minting one.** `Digest.tryParse` reads the tagged text
  `sha256:<64 lowercase hex>` (the spelling the apply corpus and the wire baselines already store) and
  `Digest.print` writes it back; `Digest.tryOfHex` reads a bare stored hex under an algorithm the caller
  names (the form `Hash.sha256Hex` and the Phase 314 maps hold, which carry no algorithm). Both refuse
  anything but lowercase hex of the algorithm's length, because an accepted uppercase spelling would make
  `Hex` differ from the stored bytes.
- **The profile-pinned constructor is in a NEW package, `Fuaran.Core.ContentAddress`**, which references
  `Wire` and `Tree` so that neither references the other: `ContentAddress.ofValue profile v` is SHA-256
  over `Canonical.tryWrite profile v`, refused where that refuses, and `ContentAddress.ofCanonicalText
  profile text` digests stored text only when `Canonical.isCanonical profile text` holds.

*Why the package is named `ContentAddress`.* By its function, as the ruling asks: it is where a value's
content address — the digest a content-addressed store keys it by — is taken under a named profile. Not
`Digest`, because the type it returns lives in `Tree`, and a package named for a type it does not hold
sends a reader to the wrong assembly; not a name for the edge it carries (`Wire` + `Tree`), because a
package named for its references says nothing about what a consumer reaches it for.

*The byte-stability gate is D120's, as this entry's rule says.* Committed vectors under BOTH profiles
(`CodecTests`, "Phase 382"): a value carrying a line feed digests to a pinned `sha256:` under `V1` and
another under `V2`, and the positive control requires the two to differ; a reference tree whose shell
encoder is `Canonical.write profile` has every Phase 314 map entry reproduced by `tryOfFields` under
each profile, its root Merkle digest pinned per profile and required to move between them. And every
digest the repository already stores reproduces byte-identically through the type: the ten result
hashes in `conformance/apply/skeleton-apply.json` read and write back, and each IS
`ContentAddress.ofCanonicalText` of its stored tree under `V1` and under `V2` (that corpus spells no
character the profiles render differently, so both reproduce it); every `sha256:` token in the
`api/wire/` baselines reads and writes back; the Phase 104 known answers read as bare hex.

*What this does not do.* It does not retype the Phase 314 maps or any key on the roster to `Digest` — that
would be a `retype`, a breaking move of a published surface, and the phase asked for expressibility
without a stored byte moving, which is what it delivers. Moving those signatures is a later, versioned act
if a consumer asks for it. Nor does it close the consumer copies D122 counted; it is the type they can
now adopt.

## 2026-10-05 — D121: the value tree is a third of a large-array decode, so a typed reader ships; it is the parser's own scanner, a grammar refusal is always `parse`'s, and a shape mismatch is reported only for a document `parse` accepts

**Recorded by Phase 368. `Fuaran.Core.Wire`; additive (STABILITY.md, `0.35.0`); no value, refusal or
emitted byte moves.**

*The question, and the bar set before measuring.* After 365 to 367 removed the per-character costs, is
building the `JVal` tree a material share of decoding a large homogeneous array, the shape of the compute
repository's incremental state? The threshold was written down before any timing. MATERIAL meant: on the
`state` corpus (20,000 short strings in one array), the tree's share is at least 20 per cent on at least
one host in both runs, beyond an A/A control of the same method, with the typed outputs checked equal
first. 20 per cent, not Phase 367's 5, because a reader is new public surface with a permanent obligation:
its refusals must equal `parse`'s on two hosts, inside the `JsonParse` proof cone. A reader that buys
less than 365 to 367 each bought (16 to 20 per cent on their cells) does not pay for that.

*How the tree's share was measured.* A scratch copy of `Wire.fs` gained a probe that reads the same text
with the parser's own string and number routines straight into a `string[]` / `float[]`: no `JVal`, no
element list, no typing pass. That is the upper bound of any reader, because it keeps every cost a
reader must still pay. It was timed interleaved against `Json.parse` plus the best-case typing pass a
consumer makes today (one tail-recursive walk into a pre-sized array), two runs on each host. The share
on `state` was 37.8 / 24.7 per cent on .NET and 33.3 / 32.3 under node (A/A within ±16 and ±2 per cent).
On 10,000 floats it was 37 to 47 per cent on .NET and 17 to 18 under node; on 20,000 small ints, 53 to
54 and 11 to 19. On .NET the tree is 1.28 MB of the 3.40 MB a `state` decode allocates. MATERIAL on both
hosts, so the reader ships.

*One grammar, by construction.* `parseDetailedWithPolicy`'s closures became an internal class,
`Json.Scanner`, whose members are the same routines: the value-start rule, the string reader by runs, the
number reader (which now returns the double and records whether the token is a `JInt`, so the reader
fills a `float[]` without building a case), and the array and object loops with the depth cap, the
`,` / closing expectations and the null-erasure fork. `parse` builds a `JVal` through them, and
`Json.Reader` reads typed values through the same members. There is no second copy of any rule. A
refusal the reader meets while scanning is therefore raised by the same code, at the same position, with
the same kind and message.

*A grammar refusal outranks a shape mismatch.* A typed reading can meet a value of the wrong shape
before it meets a syntax error later in the document. Reporting the mismatch would make the reader's
refusals depend on what it was asked, so a mismatch is only provisional: the reader asks `parseDetailed`
about the whole document and reports `parse`'s refusal if there is one. This costs a second parse on
the failure path only. The contract is short. A document `parse` refuses is refused with exactly
`parse`'s error, whatever the reading. A document it accepts reads to what typing its tree gives, or
is refused with `Mismatch` exactly when that typing fails. Mismatch is a new case on a new union, not a
`JsonErrorKind` case, because widening that closed union would break every exhaustive match on it.

*The checks.* `JsonReaderTests` runs eight readings over the codec refusal corpus's json vectors, the
five benchmark corpora and a generated pool of 4,000 documents, each also padded, truncated, cut and
spliced with a grammar character: 28,031 inputs. That is 224,248 (reading, input) outcomes, compared with `parseDetailed` plus typing
as the value or the refusal's kind, message and position, and none disagree. The same program compiled
by Fable gave the identical outcome listing under node. The comparison is shown able to fail. In the
suite, the outcomes with every refusal position moved by one disagree on every refusal. A reader built
without the precedence rule disagreed on 17,919 inputs of the `strings` reading in the suite, and on
109,521 outcomes under node; it was then reverted. Of the API, only the reader's lines moved. The `JsonParse` F\* leg was re-run green;
neither the model nor the oracle moved.

*What it buys* (`benchmarks/results/2026-10-05-i7-9700-phase-368.md`). Reading `state` with `tokens` as
a `string[]`: 33 to 38 per cent faster than parse plus typing on .NET, 20 to 22 per cent under node,
2.12 MB allocated instead of 3.40 MB. The node figure is below the probe's upper bound because the
scanner refactor made `parse` itself 4 to 9 per cent faster under node on the same corpora. `parse` is
nowhere slower: interleaved in one process, after/before is 0.88 to 1.02 under node and 0.58 to 1.00 on
.NET, against A/A controls of 0.98 to 1.21.

*Not done here.* The compute repository's state decode is the first consumer, adopted in its own phase
on that repository after the cohort raise. No tolerant-null or custom-depth reading was added: no
consumer asks for one, and `read` takes the strict policy and the default cap, as `parse` does.

## 2026-10-04 — D120: a content-addressed store names its encoding; the profile is closed and versioned, `V1` is `0.30.0`'s rendering measured against the published binaries, the profile is carried twice because D2 forbids once, and the migration is a two-witness rehash

**Recorded by Phase 360. `Fuaran.Core.Wire`, `Fuaran.Core.OpStream`, `Fuaran.Core.OpStream.Dag` and the
kit; additive (STABILITY.md, `0.35.0`); no default, signature or emitted byte moves.**

*The finding, reproduced before anything was built.* Phase 287 (D76) moved the spelling of line feed,
carriage return and tab in every string Core renders, and bounded the consequence: "no known store needs
it", "no known store holds one". A downstream content-addressed store whose payload strings carried a
newline could not verify its stored ids on the next release. The suite now carries the counter-example
as its regression: a linear store, a four-node DAG and a capture log WRITTEN BY the published `0.30.0`
binaries (a probe drove the `0.30.0` packages with an op encoder over `Json.render`), each of which fails
to verify under the current renderer — run red first, on all three — and verifies under `V1`.

*The premises, checked against the tree the phase was cut from.* (1) Every byte change to a rendering
between `0.30.0` and now is Phase 287's: `Json.render`, `Actor.encode`, `Dag.toJsonl` and the capture
payload all moved for exactly those three characters. Phase 306 added refusals of ill-formed UTF-16 at the
guarded renderers and moved no byte; the float layout is byte-identical to `0.30.0`'s (Phase 253 widened
only the parser); member order and whitespace never moved; `Canon.render` already spelled every control
character `\u00xx` and did not move. Phase 290's `Hash.utf8Bytes` change is to a hash's INPUT, not to a
rendering, and stays on the hash axis. (2) The paths that hash moved bytes are the linear chain, the DAG
node id, the capture chain — which D76 did not name and no config reached — and
`ContentPack.signatureFingerprint`. No `Hash.canonicalFields` key on the roster hashes a renderer
(Phase 316's `invocationKeyPage` included); `attestationSubject` and Phase 318's keyed capture were born
after `0.33.0` and have no `V1` history.

*The ruling.* A store names the encoding its ids were computed under, by a canonical string (`v1`, `v2`),
and keeps computing them that way. The profile is CLOSED and VERSIONED: a later byte change to `render`
adds a case and moves `current`, and never edits what an existing case renders. `V1` carries its own
FROZEN copy of `0.30.0`'s escape rather than calling the live one, because two phases queued behind this
one rewrite the live escape and parse for speed. `V2` IS the live path, held to its bytes by the
committed `V2` column rather than by a second copy: a copy of the current rule would be a second place
for the same rule to live, and the vectors already catch a drift. Neither column is derived from this
package's code: the `V1` column was MEASURED — the published `0.30.0` binaries rendered every value and
pre-image of the table, the new `V1` path matched them on all 55 probe outputs and the table on all 47 of
its rows, and the same probe pointed at `V2` reported the six rows the profiles differ on, so the probe
can fail.

*One type, or two? D2 again, as in D76.* The shard asked for one `EncodingProfile` on the spine, taken by
the renderer and by the hashing entry points. `Wire` owns the renderer, `OpStream` and `Dag` own the
pre-images, and D2 gives `OpStream` no reference to `Wire`, so no single type can be named by both.
Adding the edge was declined in D76 for reasons that still hold. So the profile is carried twice —
`EncodingProfile` in `Wire`, `OpStream.EncodingProfile` beside the chain — with the same cases and the
same canonical names, and `EncodingProfileVectors` holds them equal: the names, the current profile, and
every byte each folds. One declaration string names both halves of a store's encoding.

*The migration shape.* D76 said the day a store needed it, the shape to build was a two-witness rehash
and not a third config. It is that: `OpStream.rehashEncoding` takes a config and a witness for each side
(only `Encode` is read), `Dag.rehashEncoding` a profile and a witness for each side, and
`OpStream.rehashCapturesEncoding` a profile for each side, each refusing a source that does not verify
under what it names and returning the old-to-new id map. The DAG's re-mint is shared with Phase 311's
`rehashWith`, which moves the hash with one encoder; the two axes are separate verbs, composable by
running both. A lane-store variant was written and withdrawn: the id map carries the lanes, and a verb
whose only content is that remap is surface without a law of its own.

*The family a consumer runs on its own data.* `StoredIdentity` is the one family in the kit whose
sample is the CONSUMER'S persisted store rather than one it draws or builds: the declaration names a
known profile, every stored id recomputes under it, and migrating to the current profile and back
reproduces every id. A store that runs it in its gate meets the next rendering change at the raise,
rather than as a chain break on its live data.

*The rule this adds.* STABILITY.md's versioning policy now requires a change to rendered bytes to name
the content-addressed paths it moves and the route across each, and to ship as a new profile case.

## 2026-10-04 — D119: the frame is read against the tree; "written" is the entry at an id; the footprint alone bounds only a script that relocates nothing; a lock is proved over its subtree, and the allow-list is not

**Context.** The footprint is what arbitration, the independence diamond and the write gate all read,
and the model proved one thing about it: two ops with disjoint footprints commute. Nothing proved that
the footprint is where the writes are. Phase 318 shipped the write gate's reading of it as a sampled
law (D118.5) and left the theorem to Phase 362. The phase checked its premises against the tree first.

**D119.1 — the premises, each with its verdict.**

- *`proofs/TreeOps.fst` holds no frame theorem under another name* — HELD, and SHARPENED: its raw
  material already existed elsewhere. `Preservation.fst` section 11 has the per-node view of every edit
  (`ins_view`, `rem_view`, `move_view`, `reorder_view`, `upd_view`), and `PropagationOps.kind_kept` is
  the content half of a frame for survivors. Neither is tied to the footprint's write sets.
- *The model's footprint is the one production's `Ops.footprint` is held to* — HELD. `op_fp` is `ofOp`
  clause for clause, six sets since Phase 340, and the two slot sets are empty for every skeleton op.
- *`WriteGate.targetsOf` and the destroyed-subtree clause are as Phase 318 landed them* — HELD.
- *The cover law states the property to prove* — SHARPENED. The law compares each id's CHILD LIST
  before and after, and accepts a written id that is the source parent of a target. It does not sample
  content, and it has no clause for an id's own position.
- *"Every written id is in the footprint's write sets, including the subtree a removal destroys"* —
  REFUTED as written. See D119.3.
- *A ladder row cites the sampled cover law and is to be re-pointed* — REFUTED: no row of `proofs.json`
  named the write gate. The rows are new, and the law is cited by the bridge row.

**D119.2 — "written" is what the witness shows AT an id.** `written x t t'`: the id's presence, its
content or its ordered child ids differ. The shard's wording gave an id's parent and its position among
its siblings as clauses of their own. They are not separate facts: both are the PARENT's child list,
which is how the footprint (`StructureWrites` names parents) and the cover law already read them, and
`placement_is_the_parents_entry` proves nothing is lost — an id whose parent or sibling order differs
has a written parent. Under the per-id reading the gate's theorems would be false for a reason that is
not a defect: reordering an unlocked parent moves a locked child's index. That boundary is kept as a
witness (`lock_does_not_pin_sibling_place`) rather than hidden by the definition.

**D119.3 — the frame is read against the tree, and the pure-footprint form is false for a relocating
op.** A removal destroys descendants its footprint does not name and rewrites a parent it does not
name; a move rewrites its source parent. Both are `Ops.footprint`'s pinned over-approximations (1) and
(2), documented since Phase 78 and proved necessary by Phase 143, so this is the shard's premise that
is wrong and not the footprint: `Ops.independent` serialises every relocating op for exactly this
reason, and `WriteGate.targetsOf` adds the destroyed subtree from the tree. No production code moves.
The theorem is therefore stated in two halves, and neither is a weakening of the other. With the tree:
every write is named, or is the source parent or destroyed subtree of an unknown-parent write at the
tree the op lands on (`apply_frame`, `batch_frame`). Without it: a script whose footprint carries no
unknown-parent write writes only what the footprint names (`named_frame`). The refutation of the pure
form for relocating ops is kept as two evaluated witnesses, as section 18 of `TreeOps.fst` keeps its
own.

**D119.4 — a new module, because the dependency runs one way.** The view lemmas live in
`Preservation.fst`, which opens `TreeOps.fst`; a frame inside `TreeOps.fst` would have to re-prove them
or invert that. `TreeFrame.fst` opens both, adds no algebra, and keeps a 3,000-line module's query
context out of forty new lemmas.

**D119.5 — checked, not extracted; the law is the bridge.** The frame is about `apply` and `op_fp`,
which the oracle already holds to production. The gate model is new, and `Conformance.writeGateLaws`
already holds production's gate to the statements proved here at a domain's witnesses, so a second
differential would test the same sentence twice. The model-to-code step is an `assumed` row
(`write-gate-model-bridge`, `unscheduled`), closable by extracting the gate model.

**D119.6 — the lock theorem is about the subtree.** "No locked id was written" is true and is the weak
form: `WriteGate` promises that a lock on a node locks its subtree. `gated_apply_respects_lock` proves
that `find_in l` answers the same before and after for every locked `l` — the whole subtree is the
subtree it was — and `nothing_under_a_lock_is_written` reads it at each id.

**D119.7 — no allow-list theorem, and why.** The source parent of a removed or moved node is the one
written id the targets do not name. For a lock that costs nothing: the parent is on the removed node's
chain. For an allow-list it is a real gap in what could be claimed: an actor allowed to write `a` may
remove `a`, which rewrites the child list of a parent the list does not cover
(`allow_list_does_not_cover_the_source_parent`). Whether removing a node should need its parent to be
writable is a question about `WriteGate`'s contract. It is recorded here and not decided; the model
states only what the gate does.

**Consequences.** The footprint is a proved upper bound on an op's writes when read with the tree, and
by itself for a script that relocates nothing; the write gate's cover and its lock stand on a theorem.
`Ops.footprint`, `Dag` conflict detection, the diamond and `Ops.WriteGate` do not move.

## 2026-10-03 — D118: the gate's SHAPE is Core's and its CONTENT is the domain's; a gate only tightens; the write gate's targets are the footprint's; a capture is keyed by what was invoked, in two phases; three premises were sharper than written

**Context.** Being registered was being permitted: a registry refused only an id it did not hold, the one
three-way decision Core had (`PolicyDecision`) lived inside the AI surface's witness with no way to
combine two of them, and every consumer that needed a gate, an id-scoped write policy or an
invocation journal wrote its own — the same join, the same `{Locked; Writable}` record, the same
attempted-then-settled journal. Phase 318 checked the shard's premises against the tree first; three
were sharper than written, and each changed what was built.

**D118.1 — policy content is the domain's; the gate's shape is Core's.** Core owns the decision type and
its lattice (`PolicyDecision.join` is the maximum of `Allow < NeedsApproval < Deny`, the left denial kept
on a tie; `all` folds it, `any` folds the meet and denies when empty), the hook (`PolicyGate`: a named
decision over the resolved declaration and its validated arguments), the order (resolve, validate, gate,
body), the typed refusals (`PolicyRefused` naming the gate, its reason and what it allows;
`ApprovalRequired`) and the denial observer. It owns no rule. Who may invoke what, under which
approval, is written by the domain into `Decide`; nothing in Core inspects an actor.

**D118.2 — a gate only tightens.** A registry carries a LIST of gates and decides by their join, so
`withGate` can only refuse more, a `union` runs both sides' gates, and the lifecycle verbs carry the
policy through. There is no verb that removes a gate: an actor-scoped registry is built from the host's
ungated one, which keeps "this session may do less" a property of construction rather than of a
discipline. The policy is a FIELD of the registry because a gate that lived beside it would be bypassed
by whichever dispatch path forgot it. That field makes registry equality identity of the functions —
the only sound equality a function has — and costs the registries their ordering.

**D118.3 — PREMISE SHARPENED: the move of `PolicyDecision` is source-compatible and binary-breaking, and
the AI surface now depends on `Function`.** The shard asked for the type to move with "an alias kept for
one draft". Both packages declare it in namespace `Fuaran.Core`, so no alias is needed or possible —
a second declaration of the name would collide — and a source that names it compiles unchanged through
the new `Fuaran.Core.AiSurface → Fuaran.Core.Function` reference. F# emits no type forwarder, so a binary
compiled against the old assembly must be rebuilt; the surface gate classes the AI surface `removal`,
and STABILITY says so rather than calling the move additive.

**D118.4 — PREMISE SHARPENED: the frozen witness is composed, not grown.** The shard put an `EffectsOf`
accessor and a dry run on the AI surface. `AiSurfaceWitness` is frozen; STABILITY prescribes a composing
record for exactly this, so `GuardedSurfaceWitness` embeds it, adds `DryRun` and `EffectsOf`, and is
frozen at birth. `submitGuarded` / `approveGuarded` join the domain's decision with the registry's for
every capability an op invokes — so "not registered" is stated once, by the registry — and dry-run the
whole sequence before the first `Apply`, so an `Apply` that performs effects never runs for a sequence a
later op refuses. `submit` and `approve` are unchanged in behaviour.

**D118.5 — the write gate's targets are the footprint's, read against the tree.** The ids an op writes
are `Ops.footprint`'s structure, content and unknown-parent writes and its slot writes' nodes, plus the
subtree a removal destroys — the one thing the pure footprint records only by its target, and the tree
can name. Locks and allowances reach down a subtree through `Tree` ancestry, and a node an op creates has
the ancestors it is created under. The guarantee is a LAW (`writeGateLaws`: every id an applied op
creates, destroys or whose child list it changes is a target, or the source parent of one) and not a
theorem: **PREMISE REFUTED as stated** — the shard called `targets ⊇ written ids` "a corollary of
footprint soundness", and no footprint-soundness (frame) theorem exists in `proofs/TreeOps.fst`. The
footprint's proved property is the independence diamond, which is about commuting, not about what an op
leaves untouched. The frame theorem is its own piece of work over the tree model.

**D118.6 — PREMISE SHARPENED: a capture is keyed by what was invoked, and asynchrony is two phases, not
`Deferred`.** `Fuaran.Core.OpStream` sits below `Fuaran.Core.Function` and cannot name `Deferred`. The
keyed journal is two-phase instead — `Attempted` before the body, `Completed` or `Refused` when it
answers, the settlement free to come later — and its effect answers `Result<'v, string> option`, which is
`Deferred.settled`'s shape. Replay finds an invocation by key and occurrence, refuses a miss
(`NoCapture`, `Exhausted`) rather than calling the live source, and replays a refusal as the same
refusal. The seams' capturing dispatch needed `Fuaran.Core.Function → Fuaran.Core.OpStream`; the positional
`captureEffect` is unchanged beside it.

**D118.7 — the shared keyed-registry helper stays internal** (D117.2's open question is not settled
here): the gate lives in `RegistryPolicy`, a public type the three registries carry, and does not need
the helper published.

## 2026-10-03 — D117: a registry is a lattice, not an append log; the function registry is opaque so its index is a projection; the page token is an input the replay key reads; registration discharges the distinct-names premise

**Context.** The four registries — `CapabilityRegistry`, `FunctionRegistry`, `QueryRegistry` and
`Validator.RuleRegistry` — were add-only: `empty`, `register` (refusing a duplicate), `tryFind`,
`enumerate`, `dispatch`. Removing, swapping, narrowing or combining meant editing a public record by hand,
and the query seam declared paging on its output but not its input. Phase 316 checked the shard's
premises against the tree before building; two were sharper than written.

**D117.1 — a registry is a lattice.** Each registry gains `unregister`, `replace`, `restrict` and `union`.
`restrict` is the meet with a set of ids and never widens; `union` is the join of two registries whose
ids are disjoint and is REFUSED on a shared id with the seam's own duplicate error, never a silent
overwrite, so it is associative in the sense that matters: both associations of three registries are
refused together or agree. `unregister` is `register`'s inverse on a fresh id and refuses an id not held
with the seam's own unknown-id error, naming the held ids; `replace` is held to exactly the admission
gate `register` runs, so a hot reload can admit nothing a registration would refuse. The refusals stay
typed per seam: `NoSuchCapability`/`DuplicateCapability` on the capability and function registries,
`NoSuchQuery`/`DuplicateQuery` on the query registry, and `RegistrationError.UnknownRule`/`DuplicateRule`
on the validator's, the one new case. A session- or actor-scoped capability set is now a `restrict` of the
host's registry rather than a hand-edited record. `ContentPack.unload` is `load`'s inverse, all or
nothing, refused `PackNotLoaded` by name.

**D117.2 — one internal helper behind the three id-keyed registries, not four.** `KeyedRegistry`
(internal to `Fuaran.Core.Function`, visible to `Fuaran.Core.Query`) implements the lifecycle over an
id-keyed map once, parameterised by the seam's refusal constructors and admission gate. The validator's
registry does not sit behind it, for two reasons that are facts about the tree rather than preferences:
its package does not reference `Fuaran.Core.Function`, and adding that dependency would change a
published package's graph to share forty lines; and its order is REGISTRATION order — the order `runAll`
runs families and reports their findings in — where the helper's map is id-ordered, so a shared `union`
or `restrict` would lose the order its runs are defined by. Its four verbs are written beside it with the
same refusal shape. The helper is internal: publishing it so a registry outside this repository can adopt
it is a surface commitment this phase does not make. Making it public later is additive; retracting a
published one is not.

**D117.3 — the function registry is opaque, so its index cannot drift.** The shard offered two routes —
make the records opaque, or rebuild the index on every edit — and the second alone is not a law: with a
public record, `{ r with Entries = … }` still bypasses every verb. Read at the base tree, the defect
was sharper than "phantoms": `findBySignature` already dropped an id the index named and the entries did
not (it reads each candidate back through the entries), so a hand-removed entry was never returned; but
an entry added or re-kinded by hand was never FOUND by result type, a silent miss. `FunctionRegistry`'s
representation is now private; every verb keeps the index the exact projection of the entries (`register`
incrementally, the lifecycle verbs by rebuilding it), and `registryLaws` holds `findBySignature` by result
type equal to the enumerated entries of that type the query matches after a drawn sequence of edits. The
other three records stay public: they carry no derived index, so a lattice law over them holds of every
registry the verbs build, and opacity would cost their consumers an edit for no law.

**D117.4 — the page token is an input, and the replay key reads it.** `Query.invokePage` and
`QueryRegistry.dispatchPage` hand the resolver the token, behind default-deny and `validateParams`, so a
host no longer declares the token as a parameter (which leaked it into the enumerated contract) or calls
its resolver directly (which bypassed both). `Query.invocationKeyPage` puts a page triple — an empty name,
the tag `p` no cell's tag is, the token — in front of `invocationKey`'s fields, through the same
`Hash.canonicalFields`. Two properties decided the shape. The first page adds nothing, so its key IS
`invocationKey`'s and every journal keyed before paging still replays. And the pre-image stays injective
over the pair (`invocation_key_page_injective`, extending Phase 225's theorem): a page triple cannot read
as a binding, because its second field is a tag no cell carries, so no argument list can spell it.
`invocationKey` keeps its signature; the paged key is a sibling, not a widening of it, because widening
the published function would have broken every caller to add a parameter most never pass.

**D117.5 — registration refuses a repeated parameter name, and the premise is discharged there.**
Phase 307 restated `all_null_refusal_exact` over declarations with distinct parameter names, but
`register` checked the id alone, so the theorem assumed what production did not enforce.
`QueryRegistry.register` (and `replace`) now refuse such a declaration `DuplicateParam`, naming the first
repeated name — the query seam's admission gate, as `IllFormedCapability` is the capability seam's — and
`proofs/Query.fst` proves the refusal (`register_refuses_duplicate_params`), that a registry built by
`register` holds only declarations with distinct names, and the exact all-`Null` refusal of every query
such a registry resolves (`registered_all_null_refusal_exact`). `DuplicateParam` is reused rather than a
new case minted: it already names the fault, and the caller knows which operation it called.

**Consequences.** Behavioural break: a query declared with a repeated parameter name is refused at
registration. Source break: `FunctionRegistry`'s fields are private, and `RegistrationError` and
`PackLoadError` gain a case each. Everything else is additive, and the wire does not move. Both breaks
ride the `0.35.0` draft, whose class is already `breaking (source)`.

## 2026-10-03 — D116: the sanitisation floor is held by a law family at a witness a host builds, and the markdown scrub repeats to a fixed point; the `…With` stream operations are held at the caller's own config and hash; the kit nests batches; the obsolete snapshot forwards are classed obsolete

**Context.** Phase 335 mapped every public operation and left the security-relevant and
hash-parameterised ones `measured-elsewhere`: example tests, no law a host could certify against.
Phase 349 gives them laws. Its premises were checked against the tree first, and two did not hold
as written.

**D116.1 — the sanitisation floor is a law family over `SanitizeWitness`.** `Conformance.sanitizeLaws`
takes a record of the six functions rather than calling `Fuaran.Core.Idl.Sanitize` directly, with
`SanitizeWitness.core` the reference. Two reasons. The floor has copies — `Sanitize.fs` itself says a
change not mirrored in the renderer's copy is a defect until they are consolidated — and a law over a
witness is the mechanism that holds a copy to it, where the comment could not. And a family whose
subject is a parameter can be shown failing permanently, in the vacuity suite, at a witness with an
open floor, rather than once by editing the production code. The record is frozen at birth on the
Phase 313 precedent.

Every predicate the laws apply is written from the claim in `Sanitize.fs`'s doc comments, not by
calling into it, so a scrubber and its law cannot share a defect through a helper. The generator draws
two shapes per iteration: a uniform splice of adversarial fragments, and a forbidden name cut at a
drawn point with a removable piece interposed. The second exists because the first reaches a
resurrection about once in a hundred thousand draws; measured, three seeds of the cut-and-interpose
draw each found the defect below within their first two iterations with the pinned vectors masked.

**D116.2 — `scrubMarkdown` repeats its sweep to a fixed point (a behavioural fix).** The family's first
run was red on Core's own floor. A removal splices its neighbours together, and the splice can form
something an earlier step of the same pass already swept: `<scr<iframe>ipt>alert(1)</script>` came out
as a live `<script>alert(1)</script>`, and the handler strip turned `<scri onx=""pt>` into `<script>`.
Phase 96 found the same class in the scheme sweep and fixed it there alone. The fix is the smallest one
that closes the class rather than the instance: the whole pass repeats until it changes nothing. At the
fixed point no step finds anything, which is the floor the function claims, and idempotence holds by
construction. It terminates: after the first pass no scheme survives, so a later pass changes the text
only by a removal of at least four units that can form at most one scheme at its splice, grown two units
by replacement — every later pass that changes anything shortens the text. The same count gives the
length bound the family checks: at most the input plus two units per nine.

**D116.3 — the `…With` operations are config-parameterised, and the family perturbs the caller's
parameters.** The shard asked for the `…With` variants' laws "at a second, non-default hash". The bare
forms already take a `HashFn`; what the `…With` forms add is the `StreamConfig` — the payload pre-image
and the genesis — and the exclusions' reasons ("the hash-parameterised capture … is it at the default
hash") described the wrong parameter. `Conformance.streamConfigLaws` takes both, states the chain they
describe (sequence, link from `cfg.Genesis`, `Hash = hashFn PrevHash (cfg.Payload …)`), and requires a
non-empty chain or journal to FAIL under the caller's own genesis, payload and hash each perturbed by a
suffix — so the perturbation differs from the caller's parameter on every input by construction, at
any config the caller passes, including the canonical one. The reference run sits at a second config
and SHA-256 anyway, and `snapshotLawsWith`'s reference run moves there too. Measured: a
`verifyChainWith` that ignored its config and a `captureEffectWith` that ignored its hash each turned
the family red.

**D116.4 — the obsolete snapshot forwards are classed `obsolete`, not given laws.** Five of the
`…With` set (`compactWith`, `compactChainOnlyWith`, `snapshotAtOptWith`, `verifyAcrossWith`,
`verifyAcrossChainOnlyWith`) carry `System.Obsolete` and forward to `OpStream.Snapshots`; Phase 335
classed them `measured-elsewhere`. A law over an entry point scheduled for removal certifies nothing a
consumer should call, so they are re-classed `obsolete` with `forwardsTo` the replacement, whose laws
now run at the second config and hash. Their bare-form twins (`compact`, `snapshotAt`, `verifyAcross`
and the rest) carry the same attribute and the same misclassification; they are outside this phase's
set and are left for the coverage owner to re-class in one pass. That pass was made the same day: the fourteen
(`compact`, `compactChainOnly`, `replayFrom`, `snapshotAt`, `snapshotAtChainOnly`, `snapshotAtOpt`,
`snapshotFromJsonl`, `snapshotFromJsonlResult`, `snapshotStateHashedFromJsonl`, `snapshotToJsonl`,
`snapshotToJsonlChainOnly`, `verifyAcross`, `verifyAcrossChainOnly`, `verifyAcrossWithOpt`) are classed
`obsolete`, each naming its `OpStream.Snapshots` replacement, so the census reads 24 obsolete and 514
measured-elsewhere.

**D116.5 — the kit's op generator nests batches, and demands it.** `LawKit.genBatch` draws a batch of
one to three ops, each a structural op or, one time in three while under `batchDepthBound` (two levels
below the outermost), a batch drawn the same way. The op-kind tally counts `nested batch` beside the
kinds and every op-drawing family demands it, so a sample that never nested is starved by name rather
than green over flat batches; measured, setting the bound to zero reds every such family's guard at the
reference witness. The draw sequence moves for every seeded run, which `STABILITY.md` records as
behavioural.

**D116.6 — the escape table's format is a committed file and a law.** `StringEscapeVectors.lines` was
`host-seam`: a rendering for another language's host to diff, pinned nowhere. It now has
`conformance/escape/string-escape.json` (written by `--emit-escape`, held to a fresh render), and
`StringEscapeVectors.laws` — rostered on the `WireNullTolerance.laws` precedent — carries one law on the
format, stated from the table rather than from the escapers, so a renderer that drifted from the table
reds even where the escapers still agree with it. The file is new, so no corpus copy of it exists yet.

## 2026-10-03 — D115: the Capability model gains the handler table, the pipeline and the codecs; a theorem is stated about what the code does where the request described something else; the codec lemmas are by hand because each reader does more than a structural vocabulary can say

**Recorded by Phase 354. `proofs/Capability.fst` (sections 14 to 16), `proofs/oracle/Capability.fs`,
`tests/Fuaran.Core.Tests/ProofOracleTests.fs`, `proofs.json`, `proofs/coverage-exclusions.json`,
`proofs/modules.json`, `proofs/README.md`; rides the `0.35.0` draft (STABILITY.md, "Phase 354"). No
file under `src/` moves: the phase found no defect in production.**

**D115.1 — the premises, checked on the tree the phase started from (`a289cbc`).** The model as Phase
307 left it held the effect lattice, the value spaces with their count and well-formedness checks, the
hole and signature vocabulary, the function algebra over the abstract witness, `validateArgs`, `invoke`,
the registry, the capture key and the admission gate — and none of `bindHandlers`, the pipeline or a
codec, as the deferral said. `Function.bindHandlers`, `CapabilityPipeline.typeCheck` / `eval` /
`dirtySet` / `evalFrom` and the codecs were as Phase 307 left them. Four things the request said did
NOT hold of the code, and each theorem is stated about the code:

1. *"Every declared capability is bound to exactly its handler."* `bindHandlers` binds ACTION HOLES,
   not capabilities, and it runs a check the request did not name: a bound handler's declared effect
   must be covered by its hole's ceiling. The characterisation carries all three checks.
2. *"The check visits nodes in a topological order of the pipeline's edges."* It visits DECLARATION
   order and REFUSES a declaration that is not topological (`PipelineCycle`, `PipelineForwardEdge`); it
   searches for no order. The theorem says an accepted declaration IS in topological order, and that
   the verdict is the same across topological orders of one node set.
3. *"`evalFrom` at any node yields the value the whole evaluation assigns that node."* `evalFrom` is
   not addressed at a node: it re-evaluates a whole pipeline from a prior result and a change set. The
   theorem is that its result map equals `eval`'s, which gives the per-node reading as a corollary —
   under a hypothesis the request omitted and the claim is false without (D115.3).
4. *"Decode refuses what encode never produces."* False of a lenient reader, which accepts an extra
   member, any member order and the descriptor spelling of a space. What is proved is the statement
   about values: everything a reader accepts is a well-formed value that reads back as itself.

**D115.2 — the model's clauses, and what each parameter assumes.**

- *Section 14.* `action_holes`, `check_keys`, `check_effects`, the unbound filter and `bind_handlers`
  are production's clauses in production's order, over the handlers read as a `Map`'s key-ordered list.
  `Map.ofList` is modelled too (`map_add` replaces an equal key; `map_of_list` folds it from the left),
  because "the order the handlers arrive in" exists only before it. **Parameter:** `le`, the order the
  map keeps its keys in. `bind_handlers_exact` and `bind_handlers_complete` assume nothing of it;
  `bind_handlers_order_independent` assumes it is a total order (`total_order`, the premise the capture
  key's determinism already takes) and that the arriving addresses are distinct — with a repeated
  address the later binding wins, so order then matters and the hypothesis is not removable.
- *Section 15.* `typeCheck` with its duplicate scan (`first_dup`, the first id in order of first
  occurrence that occurs again, which is what `List.countBy` then `tryPick` computes), its position
  test, `edgeFault`, `argFault` and the required-hole check; the cycle search `pathTo` with its visited
  set, terminating by a checked measure (the declared ids not yet visited), the growth of the set
  carried as a refinement on the result; `runNode`, `eval`, `dirtySet` (the inversion and the frontier
  loop, its termination a checked measure) and `evalFrom`. Two readings are the model's: `nodeById`
  and `position` are `Map.ofList`s that keep the LAST of a repeated id, and the model looks up the
  FIRST — the two agree over distinct ids, and both are consulted only after the duplicate scan has
  passed; and the result map is a list read latest-first, `Map.add` its cons. **Parameters:** the host
  body and `spell` (any total functions); `feed_readers`, the two float comparisons `Space.subsumes`
  makes, of which NOTHING is assumed — every pipeline theorem holds for any relation; the capability
  lookup, a function and a list as in production, with no assumption that the two agree.
- *Section 16.* The writers and the lenient readers of `SpaceCodec`, `EffectCodec`, the signature and
  capability codec and the pipeline's node codec, at the `JVal`, through `Decoder.field` / `optField` /
  `list` / the tag dispatch as each is written; a refusal is its `DecodeCode` and path. `Decoder.list`
  is the direct recursion rather than production's reversed accumulator (the same first refusal, the
  same list). **Parameter:** `codec_readers.float_of_int`, `float i` as `Decoder.float` reads a `JInt`,
  of which nothing is assumed. **Not modelled:** the bytes (`Canon.render`, `Json.parse` — other
  models' theorems; the differential runs the round trip through them), the `Strict` policy, a
  refusal's sentence, and the invocation, `Deferred` and `InvokeError` codecs.

**D115.3 — every theorem conditional on a parameter or a hypothesis, named.**
`bind_handlers_order_independent`: `total_order le`, and distinct arriving addresses.
`typecheck_topological`'s second half: both declarations topological (a declaration that is not is
refused whatever its nodes are, which is the first half). `pipeline_evalfrom_agrees`: `prior` is what
`eval` answered under some body, and the new body agrees with that one at every node outside `changed`
(`agree_off`) — the differential counts 137 probes where a body moved at an unnamed node and `evalFrom`
parted from `eval`, so the hypothesis is the contract, not a convenience. The three
`…_roundtrip_identity` lemmas: the value is well-formed (`wf_signature`, `wf_capability`, a
well-formed output space), which the exact forms beside them make an equivalence. Every theorem about
an evaluator: the body is total and a function of its arguments (`capability-pipeline-body-total`).

**D115.4 — the codec lemmas are stated by hand, and why the IDL route fits none of the three.**
Declaring `Capability`, `Signature` and `PipelineNode` as an IDL vocabulary would have the F\* target
generate `rt_<T>` over a STRUCTURAL codec: one member per field, a tag per case. Each of the three
readers does something that vocabulary cannot say, so a generated lemma would be about a different
function from the one shipped:

- a signature entry OMITS its space on write when it is the slot's derived one and RESTORES it from the
  constraint on read (Phase 229), and its kind is a string from a closed set rather than a tag;
- the signature reader runs `Signature.validate` and refuses what it refuses (Phase 307);
- the capability reader cross-checks the `determinism` label against the signature it has just read
  (Phase 44), a constraint between two members;
- the node reader runs `Space.wellFormed` on the output space it has just read (Phase 307), and the
  space reader accepts a second, descriptor spelling.

The hand lemmas are stated EXACTLY — decode after encode for every value, with the refusal's code and
path where there is one — and the exact form is what found the one value that does not read back as
itself: a hand-built slot entry with no space reads back with the `SlotTree` of its constraint
(`normal_entry`). The generated route stays right for a vocabulary whose codec IS structural.

**D115.5 — the spec-strength check, and the falsification probes.** Each theorem was read against the
seam before it was discharged, and three were strengthened from the request: the handler theorem
carries the effect ceiling and names WHICH offender each refusal reports; the evaluator theorem is an
equivalence (`EvalIllTyped` exactly when the check refuses) rather than its refused half; the codec
theorems are exact for every value rather than an identity over a domain. Twelve single perturbations
were then run, one per prover invocation, each weakening one hypothesis or falsifying one conclusion
(the closure of the dirty set, `agree_off`, the acceptance premise, the topological premise, an
accepted declaration claimed to be in REVERSE order, an undeclared key claimed for `NotAnActionHole`,
distinct addresses, the ceiling, an entry claimed to read back as itself, a capability's determinism,
a node with an ill-formed output space claimed to read back as itself, a refused signature claimed
accepted): the prover refused all twelve.

**D115.6 — the budget moves, by the seeding rule.** The module roughly doubled. The re-seed and the
measurements it came from are in `proofs/modules.json`'s `Capability` note; the pinned flags are
untouched and no query needed a scoped option.

## 2026-10-03 — D114: a coverage report states what was evaluated; the declaration file types the decode refusal as the object it is; a field name every JavaScript object already carries is refused by the IDL, `prototype` is not

**Recorded by Phase 348. `src/Fuaran.Core.Conformance/FunctionLaws.fs` (`verifyFunctionSymbolic`),
`ConformanceWitnesses.fs` (`VerifyCoverage`), `src/Fuaran.Core.Idl.Codegen/Emit/TypeScript.fs`
(`typescriptDeclarations`), `src/Fuaran.Core.Idl/Idl.fs` (`Declare.errors`), and the tests that pin
each; rides the `0.35.0` draft (STABILITY.md, "Phase 348").**

**D114.1 — the premises, checked on the tree the phase started from (`a5426b9`).** All six held. (1)
`Idl.Diff.run` carried a copy of `runWith`'s roster resolution while `runVerdict`'s doc said the roster
lived in one place. (2) `FStarTarget.proofKinds` filtered on `Set.isSubset` over the same two sets
`beyondEnvelope` subtracts. (3) `verifyFunctionSymbolic` reported `Exhaustive spaceSize` after a
counterexample stopped the enumeration, and `Sampled(maxCases, _)` after one stopped the sampling, where
`verifyFunction` reported `Sampled(i, None)`, the count drawn. Every reader of `VerifyCoverage`: in this
repository only `FunctionLawsTests` (both cases); downstream, the UI host's recipe certification, which
maps both cases into its own verdict and renders `exhaustive over N` / `sampled K of N` — after this
phase an early-stopped finite certification renders `sampled K of N`, which is what happened. (4) The
ladder claims of `capability-unregistered-refused`, `capability-enumerate-is-registry` and
`capability-differential` named the obsolete `Registry.*` alias; three `AiSurface.fs` comments cited the
obsolete `aiSurfaceLaws` forward; `Encode.valueJson`'s doc opened with the private renderer's
paragraph. The same `Registry.*` citations stood in `proofs/Capability.fst`'s comments,
`proofs/README.md`, the module's `proofs/modules.json` note, `proofs/check.ps1`'s header and the
oracle project's comment, and one more `aiSurfaceLaws` in `SurfaceLaws.fs` and `Families.fs`; all
name the current members now. (5) The declaration file emitted `{ ok: false; error: string }` while
the module has returned `{ code, path, expected, message }` since Phase 337. (6) A field named
`__proto__` passed `Declare.errors`.

**D114.2 — a coverage report states what was evaluated.** The ruling: `Exhaustive` only when every case
of a finite space was evaluated — which includes a counterexample at the enumeration's LAST case — and
`Sampled(evaluated, Some size)` when an enumeration stopped early; the sampled branch reports the count
drawn rather than the count planned. No case is added: `Sampled`'s meaning widens from "a sample was
drawn" to "fewer than the whole space was evaluated", and its doc says that an early-stopped
enumeration's cases are its first `drawn` in order. Rejected: a third case (`Stopped of evaluated *
size`) — a union widening every exhaustive `match` downstream pays for, to name a distinction
(enumerated prefix vs random draw) no reader branches on; the counterexample's `Iteration` already says
where the run stopped.

**D114.3 — the refusal is declared as the object it is, under one exported name.** The declaration file
exports `DecodeRefusal = { code: <the nine codes>; path: Array<string | number>; expected: string;
message: string }`, the code union read off `DecodeError.codes`, and `decodeNode`'s signature names it.
One exported type, so a consumer writes `DecodeRefusal` rather than restating the shape. A vocabulary
type spelled like a name the file declares for itself (`Node`, `NodeKind`, a `<Kind>Spec`, now
`DecodeRefusal`) is refused as `UnsupportedConstruct` — it would be declared twice, which a consumer's
compiler reports far from its cause; before this the clash was silent at generation. The consumer
type-check needs a TypeScript compiler this repository does not carry: the leg runs when
`FUARAN_CORE_TSC` names one (`typescript`'s `lib/tsc.js`) and skips otherwise, and two legs that need
none hold the declaration's text and the runtime refusal's members and types under node.

**D114.4 — the inherited-name rule, settled by measurement.** Through the generated TypeScript host,
for an optional member of each name: a document carrying it decoded and re-encoded, one without it,
and a value built without it (what `?:` permits), each compared with the canonical bytes.

| name | the TypeScript host | F# host, interpreter, F* model, JSON Schema |
|---|---|---|
| `__proto__` | every path fails: the decoder's object literal SETS the prototype, so the member is never held, and the encoder reads `Object.prototype` back and throws | none |
| `constructor`, `toString`, `valueOf`, `hasOwnProperty` (and the other `Object.prototype` members) | a decoded value round-trips (the literal writes an own member); a value built without the member fails — `s.constructor` reads the inherited function, so the absent member reads as present and the encoder throws. A consumer reading the member of such a value gets the function where its type says `string \| undefined` | none |
| `prototype` | clean on every path — a plain object inherits no such member | none |

So the rule refuses `__proto__` and every other name `Object.prototype` carries (`__defineGetter__`,
`__defineSetter__`, `__lookupGetter__`, `__lookupSetter__`, `constructor`, `hasOwnProperty`,
`isPrototypeOf`, `propertyIsEnumerable`, `toLocaleString`, `toString`, `valueOf`) and admits
`prototype`. It is ONE rule in `Declare.errors`, over every field a vocabulary declares (kind, op,
record, union case, envelope), for every host: a vocabulary is one document, and a name one of its
hosts cannot carry is not a name the vocabulary has. The other hosts have no hazard, and that does not
narrow the rule. Rejected: reading only own members in the generated encoder — it would make the
generated encoder safe and leave every consumer that reads `node.kind.constructor` with a function its
type denies; and refusing `__proto__` alone, which leaves a measured hazard standing. The measurement
is a test (`IdlRefusalHostTests`, "the evidence the rule rests on"): if a host change ever makes a
refused name safe, it goes red and the rule is re-examined.

## 2026-10-03 — D113: the TypeScript host reads JSON with the F# reader's twin, not `JSON.parse`; a map is a null-prototype object read in document order; a sentinel slot is read by value; a repeated key keeps its first value everywhere

**Recorded by Phase 347. `src/Fuaran.Core.Idl.Codegen/Emit/TypeScript.fs` (the decode prelude's `dRead`,
`dInt`, `dMap`, `dSentinel`, and the parse leg `dParse`), `Emit/FSharpCodec.fs` (`dSentinel`, `dMap`, the
sentinel arms of `decFn` and `decField`), every committed generated module under `tests/`,
`tests/Fuaran.Core.Tests/RefusalCornersIdl.fs` and `IdlRefusalHostTests.fs`; rides the `0.35.0` draft
(STABILITY.md, "Phase 347"). Supersedes D109.4's scan and D109.5's sentinel boundary.**

**D113.1 — the premises, checked before anything was built.** Phase 337's worker named four places the
generated hosts answered differently from the interpreter. All four reproduced on the tree Phase 347
started from, each as a three-way case: (a) the TypeScript `dMap` assigned `out[k] = …`, so a map key
`__proto__` replaced the decoded map's prototype — the entry vanished and every later read of the map
went through an object the document chose; (b) a required closure / opaque slot was not read by either
generated host — absent, another string, another kind, all accepted — where the interpreter refuses
them as `MissingField`, `OutOfRange` and `WrongKind`; (c) a repeated member kept its LAST value in the
TypeScript host and its first in the interpreter and the F# host; (d) `1.0` and `1e0` at an int slot
were accepted by the TypeScript host and refused (`WrongKind`) by the other two. One premise was
WRONG: the shard asked that an out-of-range literal (`1e400`) be refused "with `OutOfRange`". The F#
reader refuses it before any slot is read (`MalformedNumber`, so `InvalidJson` at the root), and so
does an integer token past 2^53 that is not the canonical layout of a double; the interpreter answers
exactly that, so `InvalidJson` at the root is the one answer, and the TypeScript host — which read
`1e400` as `Infinity` and accepted it at a float slot — moved to it. The same check found two more
reader corners of the same class: `JSON.parse` refuses a raw control character in a string, which the
F# reader admits, and admits a lone surrogate, which the F# reader refuses (Phase 299).

**D113.2 — the TypeScript host's reader is the F# reader's twin.** Every one of those corners is a
question `JSON.parse` answers before the decoder runs, and three of them (a float token's spelling, a
repeated key, the document order of an index-like key) are facts a JS value cannot carry afterwards.
So `dParse` no longer calls `JSON.parse`: it reads the text itself, to the F# reader's grammar
(`Json.parseDetailedWithPolicy` under `RejectNull`) and in its order — whitespace, strings and their
surrogate rule, the number grammar and its three classes (an Int32 integer token, a float, and past
2^53 only a double's canonical layout, which writes a 16- or 17-digit whole double in full and every
longer one with an exponent), the nesting cap checked as a container opens, trailing characters last.
The first fault met is the one reported, which is what D109.4's prefix scan approximated; that scan,
`dDepthBreach` and `dHasNull` are gone. Beside the value the reader keeps what the value cannot carry,
in two `WeakMap`s keyed by the parsed container: the members and items whose whole number was written
as a float token, and the members as written of an object whose JS form loses them (a repeated key, an
index-like key). `dRead` hands a member or an item to its decoder and tells `dInt` whether it was a
float token. An object is built with every key as data: `__proto__` becomes an own member through
`Object.defineProperty`, never the object's prototype. Rejected: `JSON.parse` with a reviver — it sees
neither the token nor the repeated key (the source-text proposal does, on some runtimes, which would
make the host's answer depend on where it runs); and the documented-gap route the shard allowed — four
of the five corners are refusal differences on documents a model can emit, which is the property the
law exists to close. The cost is a reader in JS rather than the engine's native one; the emitted
module stays dependency-free.

**D113.3 — a map is a null-prototype object, read in document order, first value kept.** The decoded
map is `Object.create(null)`: a key is data whatever it spells, and no key reads one the prototype
has (`constructor`, `toString`), which a plain object answered for an absent key. Its entries are
checked in document order — the interpreter's and the F# host's order; an object lists an index-like
key first, so `{"b":"x","1":"y"}` was refused at `1` by this host and at `b` by the other two — and a
repeated key keeps its first value after every entry is checked. A consumer that called a decoded
map's inherited methods (`m.hasOwnProperty(k)`) calls `Object.prototype.hasOwnProperty.call(m, k)` or
`Object.hasOwn(m, k)`. The F# host's `dMap` took `Map.ofList`, which keeps the LAST of a repeated key;
it keeps the first now, as every member read and the interpreter's map lookup do. A repeated key in a
verbatim `json` slot is the one residue: the interpreter carries both members in its `JObj`, and a JS
object holds one per key, so the TypeScript host keeps the first — the value every member read of
that object answers. No refusal depends on it.

**D113.4 — a sentinel slot is read by value; D109.5's exclusion is removed.** Both generated hosts read
a closure / opaque slot as the interpreter does (`dSentinel`): present, it must be its sentinel —
another string is `OutOfRange`, another kind `WrongKind`, expected `string` — and required, it must be
present (`MissingField`). It still decodes to nothing (`()`, the declared `TFn` placeholder, or JS
`null`), and an optional slot's presence still decides `Some` from `None`. The mutation law now
mutates sentinel positions, draws the reader's corners (a whole number written as a float token, a
literal past the double range or past 2^53, a member repeated with a value of another kind before or
after it, a member renamed `__proto__`) and runs over a fourth vocabulary, `RefusalCornersIdl`, which
holds a slot of every type a corner lives at; each corner is also pinned one by one, three ways. The
one boundary D109.5 named that stands is a hosted slot declaring no wire form, unchanged.

## 2026-10-03 — D112: the digest maps are SHA-256 and three — own, frame, Merkle — with the id and the kind as fields of the pre-image; a change classification is a PROJECTION of the diff, held to its script by law; a gate verdict is a set difference keyed `(code, node)` under one of three policies

**Recorded by Phase 314. `Tree.digests` / `Tree.ownDigest` / `Tree.frameDigest` / `Tree.Digests.diff`,
`Diff.changes` and `Diff.ChangeKind`, `Validator.introduced` / `verdict` / `gate` / `encodeVerdict`,
`Projection.snapshot` / `snapshotDigestOf` / `Scope.ChangedSince` / `Scope.BySubtreeDigest`,
`Conformance.digestLaws` / `changeLaws` / `introducedLaws`, `proofs/TreeDiff.fst` section 14; rides the
`0.35.0` draft (STABILITY.md, "Phase 314").**

**D112.1 — the width is SHA-256, and the pre-image carries the id key and the kind as fields.** Four
consumers kept per-node self and subtree digests of their own, one at 32 bits. Phase 290 named the two
hash regimes and this is the crypto one: a digest map is compared across snapshots, across hosts and
across a reconcile that keeps a subtree BECAUSE its digest agrees, so an unseen difference must cost a
SHA-256 collision and not a 1-in-2^32 chance (Phase 298 measured the content digest letting an edit
read as unchanged). Three maps, because three readers want three things: `Own` (the node's content —
what a four-way delta classifies as changed), `Frame` (the node as a PARENT — own content, child count
and ordered child id keys: what a changed-since read wants, and exactly Phase 298's snapshot digest, so
`Projection.snapshotDigestOf` is now this function and no second definition exists) and `Subtree` (the
Merkle rollup — own digest then each child's subtree digest, in order: what a reconcile keys a kept
subtree by, and the fast path every walk takes). The own pre-image is `[id key; kind tag; encode shell]`
rather than the encoder's output alone, for two reasons that are one: the subtree digest then recovers
ids, kinds and shape with NO hypothesis on the encoder (`subtree_digest_injective` takes none), so the
subtree-equal fast path — skip a subtree whose digest agrees, read none of it — is sound by
construction and not by the domain's diligence, and `Digests.diff`'s `Changed` sees a kind change
whatever the encoder encodes. The encoder is read over the SHELL (`ReplaceChildren n []`), Phase 305's
posture for the content-aware diff, so a change in the children alone never moves an own digest.
Rejected: a 64-bit form (the shard's alternative) — cheaper, but a second width beside `sha256Hex` with
nothing on the spine to spend it on; and one map with the three derivable — `Frame` and `Subtree` are
not derivable from `Own` without the tree, and the reader who has only the maps (a snapshot) is the one
the fast path is for.

**D112.2 — a change classification is a projection of the diff, defined on the trees and HELD to the
script.** Three consumers classify every id as `Added | Removed | Moved | KindChanged | Changed`, each
by hand and each slightly differently (one carries positions, one picks a single kind per id by
priority). `Diff.changes` reads the two indexes directly — a survivor's parents, kind tags and encoded
shells, and its kept children's relative order — and `Conformance.changeLaws` holds the result to the
script `Diff.toOpsWith` emits, kind by kind: `Added` is exactly the `InsertChild` grafts, `Moved` the
`MoveNode` targets, `KindChanged ∪ Changed` the `UpdateNode` targets, `Removed` is `before`'s ids minus
`after`'s with every `RemoveNode` target among them and every other below one, and every
`ReorderChildren` parent is `Reordered` or holds an `Added` or `Moved` child. Three choices follow
from making the script the referee. (a) **`Reordered` is a sixth kind and names the PARENT**, because a
position is a fact about a parent's child list and that is where the op algebra puts it
(`ReorderChildren` names the parent); it is defined semantically — the KEPT children stand in a new
relative order — so a sibling shifting index because a neighbour left is not a reorder, and a consumer
that reported per-child positions reads them off the parent's two child lists. (b) **One id may carry
two entries** — `Moved` and `Changed` for a survivor that was reparented and edited — because the two
facts are independent and the script carries both ops; `KindChanged` subsumes `Changed` (one
`UpdateNode` either way), and the canonical order is `(id key, kind rank)` so a reader wanting one kind
per id takes the first. (c) **`Moved` is a change of PARENT only.** Rejected: deriving the
classification FROM the script — a front insert emits a `ReorderChildren` although no kept order moved,
because the structural passes append (section 10's settled order), so a reorder read off the script
would be an artefact of the emission strategy and not a fact about the trees; and the UI shape's
position-carrying `Moved`, which marks every later sibling moved when one leaves. The law's one
hypothesis is the encoder seeing the kind, which an injective encoder does; a red `changeLaws` over a
domain's encoder is the encoder losing the kind, which is the finding.

**D112.3 — a gate verdict is a set difference keyed `(code, node)`, read under one of three policies.**
Two consumers computed "the defects the candidate has that no parent has" and each named the same
three policies. The identity is the code and the node key — what two hosts agree on, as
`canonicalCodes` agrees on codes — and never the message, family or related nodes, so rewording a
message cannot introduce a defect; `introducedDefects` is pure over defect lists, for a host that has
already run its validators, and `introduced` runs a registry over the candidate and every baseline (one
baseline is a before/after gate, two are a merge's parents, none introduces everything). `Lenient`
consults no validator (`gate` short-circuits, as the presentation tier's did), `Diagnostic` reports and
never blocks, `Gated` blocks on an introduced `Severity.Error` and never on a warning. `encodeVerdict`
is the cross-host surface a refused fold's verdict hash is taken over — through `Hash.canonicalFields`,
the policy, the block, then each defect's code, location and severity in canonical order — and
excludes the message for the parity reason above; the UI tier's JSON verdict carried the message, and
a host adopting this encoding drops it from the hash.

**D112.4 — the projection READS the digests; the fast path is new, the bytes are not.** `snapshot` is
one `Tree.digests` pass re-keyed by `idKey` (escaping is injective), `ProjectionSnapshot` gains the
Merkle map, and `ChangedSince` skips a subtree whose Merkle digest the snapshot holds — nothing below it
can differ up to a collision — while reporting a node by exactly the Phase 298 test (`snapshotDigestOf`,
now `Tree.frameDigest`). A snapshot with an empty `Subtrees` reads as before. `Scope.BySubtreeDigest`
is the `Subtree` slice addressed by content, for the consumer that holds a digest from a snapshot or a
reconcile rather than an id. The shard's premise that Phase 298 left the projection structurally blind
was checked and found FALSE: 298's digest already carried the child count and ordered child ids, so
what this phase closes is the second definition of that digest, not a blindness.

## 2026-10-02 — D111: the invocable seams have ONE admission gate — totality, then well-formedness — run at registration, at every reader and by `compose`; a repeat counts over a capped integer range; an argument names its hole once

**Recorded by Phase 307. `Space.wellFormed` / `Space.isCount` / `Space.maxRepeatCount`, `Signature.validate`,
`Function.validate` / `isClosed`, `DeclarationFault`, `CapabilityRegistry.register` and
`FunctionRegistry.register` through `Capability.admissionFault`, `CapabilityCodec`'s readers,
`Capability.validateArgs` / `validateArgsAll` and `Query.validateParams` / `validateParamsAll` /
`QueryCodec.decodeArgs` through `Capability.repeatedAddrs`, `CapabilityPipeline.typeCheck` and
`nodeInvocationKey`, `Function.applyMemo`; `proofs/Capability.fst` section 13 and `proofs/Query.fst`
section 8; opens the `0.35.0` draft (STABILITY.md, "Phase 307").**

**D111.1 — one gate, at every door.** Everything outside the seams' proved core trusted its declaration:
a `RepeatHole(FloatRange(0, infinity))` was total, `IntRange(5, 1)`, a NaN bound and `Enum []` were
"bounded", two holes at one address registered, and a declaration with an infinite bound was written by
the codec as bytes its own reader refused. The fix is not a check per surface but ONE check run wherever a
declaration enters: totality (`Function.isTotal`, refused `NonTotalCapability`) and then well-formedness
(`Signature.validate`, refused `IllFormedCapability` with a typed `DeclarationFault`) at both registries;
the same `Signature.validate` in the capability reader, so a document that decodes is one a registry could
admit on well-formedness; and `Function.validate` — the signature check plus the one only the witness can
make, no hole beneath a slot — over every tree `compose` builds (`IllFormedResult`). A host deriving a
capability from an artifact runs `Function.validate` first; the gate does not run inside `signature`,
which stays a total projection, because making it refuse would retype every caller for a check the
registries already make. Rejected: refusing at `apply` too (it would be a second gate with a second
vocabulary, and `apply`'s own guard is totality, which it keeps).

**D111.2 — a count space is a capped, non-empty, non-negative `IntRange`; the cap is one million.** The
totality criterion was "neither `AnyString` nor `SlotTree`", which admitted every float range, every
string length and every enum as a repeat's count. A count is how many times a host expands a subtree, so
the criterion is now `Space.isCount`: `IntRange(lo, hi)` with `0 <= lo <= hi <= Space.maxRepeatCount`, and
`maxRepeatCount` is 1,000,000. The number is a stated policy, not a measurement: it bounds the work one
application can demand at a size no current domain approaches, and it is a literal so a host can read
it. A domain that needs more raises it in a decision of its own; one that is refused at registration
learns at registration, not at the expansion. `Required` follows totality (D104), so a repeat over a
space that is no count is no longer required, and the `toSchema` / `toJsonSchema` bytes of such a
signature move with it.

**D111.3 — the fault vocabulary names what a reader can act on, and carries what can travel.**
`EmptySpace` carries the space (the reader sees why no value fits); `NonFiniteBound` carries the address
ALONE, because the space it would carry has no JSON spelling and the fault is itself a wire document;
`DuplicateHoleAddr` names the second occurrence; `HoleUnderSlot` names the NODE that declares the slot, by
its id, because the witness addresses a hole relative to the subtree it is handed and the check walks
subtrees — an address would mean different things at different nodes, and a hole's name is inert by the
hygiene law and must stay so (the check counts kinds and never reads a name, which is what keeps
`compose_rename` a theorem).

**D111.4 — an argument names its hole once, at every seam.** `validateArgs` accepted `[a = x; a = y]`;
the `Map.ofList` reading took the last, `Query`'s `Required` reading took any, and the capture key moved
with the order of the list, so the proved determinism row carried `distinct (keys a)` as a hypothesis
nothing enforced. Now the first repeated address is refused before any value is read — `DuplicateArg` at
the capability seam, its reader and the pipeline's `typeCheck`, `DuplicateParam` at the query seam and
its argument reader (a separate name, because the two unions share a namespace and the seams' vocabularies
are already parallel: `UnknownArg` / `UnknownParam`) — and the hypothesis is a theorem about every list the
seam accepts (`validate_args_distinct`, `validate_params_distinct`).

**D111.5 — the smaller soundness fixes.** A strict application binds closed trees only (`SlotArgOpen`):
an `Ok` used to carry open holes nobody bound. The memo gate joins the observed effect of every
`SlotArg` with the function's, so `applyMemo` and `applyMemoComposed` now agree on a Clock-carrying slot
argument (both bypass). A memo cache is PER WITNESS and documented so, rather than keyed by a witness
tag — every caller threads one cache through one witness, and a tag would be a string a caller could get
wrong. `nodeInvocationKey` leads its pre-image with the node kind, closing the `Source("source",
"source", _)` / `Invoke("source", "source", _, [])` collision; every journalled pipeline key moves.
`Capability.invoke` keeps an unwrapped body and documents the obligation (`capability-body-total`):
catching every exception would also swallow the ones a host means to escape. A pre-229 slot entry with
no space is invocable again, through `Function.slotSpaceOf`. The codecs gain guarded `tryEncode`s over
`Canon.tryRender`; `encode` stays total and is exact over every declaration the gate admits.

**What the shard asked for and this decision did not do.** It asked for the optional-field decoder,
`slotKind` / `nextPageToken`, the `IntRange` culture fix and `evalFrom`'s type-check: Phases 310 and 295
had shipped all four, and this phase pins them rather than redoing them. It classed the phase additive
and said the key change would ride Phase 290's breaking draft: `0.34.0` was released the morning this
phase was taken, and three unions widen, so it opens `0.35.0` as a breaking (source) draft.

## 2026-10-02 — D110: lanes are a writer partition carried beside the node map; the whole union orders by one drain with the key as a parameter, `(lane, seq, id)` by default; a convergence is a function of the head set

**Recorded by Phase 311. `src/Fuaran.Core.OpStream.Dag/DagOpStream.fs` (the lane store, `totalOrderBy`,
the multi-parent primitives, `rehashWith`, attestation, `prunable`, `appendIf`), `OpStream.attestationSubject`,
`Conformance.laneLaws`, `proofs/Chain.fst` section 5b, `proofs/DagFold.fst` section 13.13; rides the `0.34.0`
draft (STABILITY.md, "Phase 311").**

**D110.1 — lanes partition WHO wrote a node, never a resource, so D51 admits them.** A lane is one
writer's file; nothing about a lane changes what a node means, how it hashes or how it replays. That is
why the lane identity rides BESIDE the node map (`Loaded.LaneOf`) rather than in `DagNode` — content ids,
every file written before this phase and every existing function are untouched — and why the Lease
precedent (a resource partition, declined from this substrate) does not apply. `loadLanes` reads the
lanes in ordinal lane-id order whatever order they are handed in, so the store is a function of the lane
SET; a node two lane files hold identically is one node, attributed to the smallest lane, and with
different content it is refused naming both lanes. Of the three ways downstream consumers attributed a
shared node — the last lane wins, the first lane wins, refuse — only the order-free one was admissible,
and refusing an identical copy would turn a sync artefact into a load failure.

**D110.2 — one drain, the key a parameter; the default key is `(lane, seq, id)`.** Consumers ordered the
union three ways: the smallest id, a sort by (Lamport rank, lane, id), and a drain at (domain rank, id).
Each is the Kahn drain over the whole node set with the frontier ordered by `(key node, id)` at a
different key — the sort included, because a rank strictly increases along every edge, so the smallest
remaining key is always ready. So Core exposes `totalOrderBy key`, proves it a linear extension, total on
an acyclic set and set-determined at EVERY key (DagFold 13.13, by proving the key comparison a total
order and instantiating section 13), and leaves the key to the consumer. The lane store's default,
`laneKey`, is `(lane, seq, id)`: a node's lane, its position in that lane's own history (how many nodes of
its lane it descends from), and its id — each writer's history kept together as far as the parent
relation allows, lanes in lane-id order. It is a default, not a recommendation: a domain whose concurrent
ops do not commute passes a key that encodes its own order (one downstream consumer's "a kill folds after
the fork it kills" is `(rank op, id)`), and two replicas sharing a key agree by construction whatever
their ids happen to sort as. Rejected: making the smallest-id drain the only order (the defect class a
downstream replay recorded — which of two concurrent nodes folds first decided by hash luck — is a
property of that key, not of the drain).

**D110.3 — a convergence is a function of the head SET; `appendOn` stores what it is given.** `mergeAll`
deduplicates and sorts the heads before storing them, so two replicas converging the same heads mint the
same node, bytes and id (`merge_all_set_determined`); `appendOn` stores its parents as given (the id sorts
them anyway), so `appendOn [p]` IS `append p` and `appendOn [l; r]` IS `merge l r` — both are now written
over it. Over distinct parents the two mint one id (`append_on_is_merge_all`). A repeated parent under
`appendOn` is kept, as `merge x x` keeps it: refusing it would need a new `DagAppendFault` case, a
breaking widening for a shape nothing builds by accident.

**D110.4 — a merge script is recorded as a merge node and a chain, not as a new op case.** `mergeWith`
records `[o1; …; on]` over `left` and `right` as a merge node carrying `o1` and a chain of the rest, so a
replay of the last node folds both parents' closures and then the script — what a merge whose op were a
`Batch` would apply — with no `Batch` case demanded of the domain. An empty script records NOTHING:
a merge node needs an op, and a convergence with nothing to record is `merge` with the domain's own
no-op.

**D110.5 — one head per lane is judged in the UNION.** `laneCollisions` names a lane holding two nodes
neither of which reaches the other in the union DAG. A downstream verifier judged each lane file ALONE,
and a lane whose nodes name parents in other lanes did not verify alone, so it was skipped — exactly the
lanes a converging writer produces. Comparability in the union holds for those lanes too: a writer whose
next node descends from its last only through another lane's merge still has one head.

**D110.6 — `firstBreak` names the earliest fault.** It scans the whole DAG's drain and then any node on
or below a cycle, where it scanned `Map.toList`. Which nodes are faulty and `verifyDag`'s verdict do not
move; where several faults exist, the one named is the first in the history rather than the smallest id,
so `verifyLanes` names the lane where the history first goes wrong. The model's refusal names the
smallest dangling id, another order-invariant choice; which node production names among several is not
modelled, and `proofs.json`'s `dangling-parent-policy` says so.

**D110.7 — the rest of the linear stream's parity, each the smallest shape.** `rehashWith` verifies the
source under `fromHash` first and returns the old-to-new id map, because heads, lanes, checkpoints and
attestations are keyed by id and only the caller knows which it holds. An attestation is bound to the
PARTY that makes it — `OpStream.attestationSubject hashFn party head`, `"attest|"` before the party's
encoding so the pre-image is never a node's — because on a multi-writer store the signature must say who
vouched, and `IAttestationSink` signs that subject unchanged (no interface member added). `prunable`
NAMES the nodes no retained root needs and removes nothing: dropping them leaves every retained closure
whole by content addressing, and what to do with them is the host's (GP6). `appendIf` guards on the head
set and applies the op at the caller's state — the positional check is the head set, nothing is
replayed — and appends onto every head, the convergence a writer that folds before it writes takes.
`commonBase` is `mergeBase`'s rule over all heads at once; a left fold of pairwise merge bases, which a
downstream verifier used, can depend on the order of the heads on a criss-cross.

**Premise verdicts.** The shard's premises hold at the base commit: Core ordered the DAG per head only
and carried no lane identity; `append` and `merge` were the only constructors and `nodeHash` was private;
`reconcile` returned an `'Op list` and `MergeConflict` carried ops; `firstBreak` scanned in map order;
`rehash`, attestation and retention existed for the linear stream only. One is narrowed: the linear
stream's "retention" is compaction behind a snapshot, which the DAG has had since Phase 288; what the DAG
lacked was branch retention, which is what `prunable` is.

## 2026-10-02 — D109: the generated decoders refuse with Core's `DecodeError`, held to the interpreter's code and path; a verbatim decode expression keeps answering a sentence

**Recorded by Phase 337. `src/Fuaran.Core.Idl.Codegen/Emit/FSharpCodec.fs` (the decode helpers, the
union, enum, node and parse legs), `Emit/TypeScript.fs` (the decode prelude, `dParse`, `decodeNode`),
`Emit/Core.fs` (`oneOf`, `declaresHosted`), `Gen.fs` (the support channel's contract), every committed
generated module under `tests/`, and `tests/Fuaran.Core.Tests/IdlRefusalHostTests.fs`; rides the
`0.34.0` draft (STABILITY.md, "Phase 337").**

**D109.1 — Core's type, not a generated mirror.** The generated F# module already opens `Fuaran.Core`
(it encodes through `Canon`, registers a `NodeWitness`, runs `Validator.runAll`), so it may name
`DecodeError`, `DecodeCode` and `PathSegment` directly. A mirror would be a second closed code set to
keep in step with the one D99 made the wire contract, and a consumer would convert between two types
that mean one thing. The TypeScript host has no Core, so its refusal is `DecodeError.toJson`'s members
— `code`, `path` (keys and indices), `expected`, `message` — which is the shape a conformance vector
already carries.

**D109.2 — the code and the path are the interpreter's; the sentence is the layer's own, and unchanged.**
`Idl.Decode.decodeDetailed` is the oracle: the helpers raise the code and grow the path exactly where
its walk does (a member, an item, a map key, the discriminator; a transparent union's bare payload at
the same position). Where the generated layer and the interpreter drew a refusal differently, the
generated one moved: an object without the discriminator at a union slot is `MissingField` at the
discriminator, never the transparent case's payload refusal, and an unknown enum string is
`UnknownTag`. The SENTENCE is kept word for word, so `Result.mapError DecodeError.describe` over the
new `decodeNode` is the old function — a consumer whose own policy layer pins those sentences (a reject
corpus, a diagnostic) migrates by a type change, not by a corpus regeneration. The law asserts code and
path; `expected` matches too except where a module covers a subset of the vocabulary's kinds (it lists
the kinds it decodes).

**D109.3 — a verbatim decode EXPRESSION keeps its contract; a verbatim decode MEMBER takes the new one.**
The support channel splices three kinds of decode source. A hosted slot's `Decode` and a case refine
are expressions whose natural refusal is a sentence — a foreign codec (`DataFrameCodec`, a date parser)
or a policy over decoded members — and neither composes with the generated helpers, so both keep
answering `Result<_, string>`: the helper the generator wraps them in (`dHosted`, `dRefine`) lifts the
sentence to `OutOfRange` at the slot or at the case's object. That code is the honest one: a hosted
slot's declared wire form has already been checked, and a refine runs after every member decoded, so
in both the value is of the right kind and outside what the position admits. A kind projection's
decoder and a decode splice are MEMBERS of the decoder group that call the generated decoders, so they
cannot keep a string error without discarding every path below them; they answer `DecodeError`.
Rejected: an overloaded lift admitting either error type from any verbatim piece — it compiles only
when the piece's error type is already fixed, and a codec answering `Ok` alone leaves it ambiguous, so
it would trade one explicit edit for an inference failure at a distance.

**D109.4 — the TypeScript host refuses what the F# reader refuses at parse.** `JSON.parse` reads `null`
and any depth; the F# reader (`Json.parse`, the spine's only reader) refuses `null` and caps nesting at
512, so a document the other two refused was read by this one, and the refusal surfaced later (a
`null` at a string slot) or never (a `null` in a `json` slot). `dParse` restores the reader's answer
and its ORDER: the text is scanned for the first container opened past the cap, and the prefix before
it — closed with a value that cannot join an open token — is what decides between `InvalidJson` (a
malformed or `null`-carrying prefix, which the reader meets first) and `LimitExceeded`.

**D109.5 — the law, and its two stated boundaries.** The `conformance/decode/` vectors are a shape
grammar, not a vocabulary, and every generated host decodes from a node root; so each vector the IDL
can declare becomes one kind of a vocabulary over them (`DecodeVectorsIdl`), its input the member `v` of
a flat node, its pinned refusal expected under `["v"]`. Eight cannot be declared — an item bound, an int
range, an admit list, a closed object a refusal depends on, unresolved references, a member name that
is not an identifier — and are named with their reasons rather than dropped. Sampled mutations of the
certification vocabularies' documents complete the law. Two positions are not mutated, each a known
disagreement this phase does not move: a closure / opaque sentinel (the generated hosts read only a
sentinel slot's presence; the interpreter reads the sentinel), and a hosted slot declaring no wire
form (only the compiled host runs its codec, Phase 252).


## 2026-10-02 — D108: a structural edit's change set is the diff of the two trees; the order is certified by a checker the walk does not run; the evaluator contract is about evaluators that return

**Recorded by Phase 308. `src/Fuaran.Core.Propagation/Propagation.fs` (`touchedBy`, `changedForOp`,
`validTopo`), `src/Fuaran.Core.Conformance/PropagationLaws.fs` (`propagationEvaluatorLawsWith`),
`proofs/Propagation.fst` section 8, `proofs/PropagationOps.fst`, `proofs.json`; rides the `0.34.0` draft
(STABILITY.md, "Phase 308").**

**D107.1 — the change set is defined by what the edit did to the two trees, not by what the op names.**
`changedForOp pre post op` seeds its closure with every post-edit id that is NEW, every survivor whose
CHILD IDS differ, every survivor whose DECLARED READS differ, every id the op CONTENT-WRITES
(`Ops.footprint`'s `ContentWrites`), every REMOVED id, and `touchedBy pre op`; it closes them over the
PRE-edit dependency graph and keeps the survivors. Each seed answers one way a value can move under an
evaluator that is a function of a node's own content, its child ids and its resolved declared reads:
content (only an op's content writes move it — `kind_kept` proves `Ops.apply` rewrites content nowhere
else, which is what lets a witness with no content accessor see it), children, reads, and a read whose
target vanished (reached through the removed id in the graph that still holds it). A read whose
target's VALUE moved needs no seed: `evalFrom` closes the change set over the post-edit graph itself.
The op-directed form it replaces named what the op's arms named, resolved against the pre-edit tree, and
was wrong three ways the diff cannot be: a move's old parent and a removal's parent were not touched (a
count of children went stale), and a `Batch` was resolved against the tree its first sub-op saw (a node
moved under a parent the batch then removed took its readers' staleness with it). `touchedBy`'s two arms
and its batch threading are corrected too, and it stays a seed, so the set never shrinks below the
pre-edit closure it was. The completeness is a theorem (`changed_for_op_complete`), stated for a
well-formed tree and an op `Ops.apply` accepts; an evaluator that reads a node's subtree without
declaring the reads is outside its class, and declared reads are the remedy.

**D107.2 — `sort` is certified by a checker, and the walk does not run it.** `Propagation.validTopo` is
the certificate: `Order` and the cycles' members distinct and holding exactly the map's ids, and every
read of an `Order` id the map holds earlier or in a cycle. Its meaning is proved (`valid_topo_distinct`:
acceptance implies the one order premise the agreement theorems take), and Tarjan's output is held to it
on every graph the differential draws. `sort` itself does not call it. Running it there would make the
order premise hold of every run rather than of every sampled one — but a failed check would then have to
be reported as SOMETHING, and the only total answer `TopoResult` can carry is a cycle verdict the graph
does not have: an internal defect delivered as a plausible wrong answer, the failure this phase exists to
remove. Declined on that ground (the operator's ruling for the phase); the order bridge stays an assumed
row, narrowed from "Tarjan's output holds no id twice" to "Tarjan's output passes the checker". A runtime
check that fails LOUDLY — a typed internal fault — would need a new `PropagationError` case and is a
separate, breaking decision, not taken here.

**D107.3 — the evaluator returns.** Every agreement theorem quantifies over a total evaluator, and the
driver does not catch. Wrapping the call so a throw becomes `EvalNodeFailed` was considered and
declined: it would catch what no total function raises, report a host fault (an out-of-memory, a
bug in the caller's own code) as a domain failure of one node, and give a domain that throws a value
where it should get a stack trace. The claims read "any TOTAL evaluator", and the premise is a row of
its own (`propagation-evaluator-total`).

**D107.4 — what 302 had already done, and what this phase added to the laws.** The honesty law's
reader-of-a-removed-node clause, the asked-within-declared clause and the survivor-restricted agreement
arm shipped with Phase 302 in `propagationEvaluatorLaws`; this phase pins the first with a go-red
(a removal-blind change set loses honesty, naming the reader) and gives `propagationEvaluatorLawsWith`
the survivor arm it lacked, counted beside its guard.

## 2026-10-02 — D107: a stability class is a fact about what an OLD document does under the NEW vocabulary, never about the emitter alone

**Recorded by Phase 304. `src/Fuaran.Core.Idl.Codegen/Diff.fs` (`classifyFieldAdd`, the
`FieldOptionalityChanged` rule and its descriptor rows), `Emit/TypeScript.fs` (`dInt`),
`docs/idl-stability-classes.md`, `proofs/WireVersioning.fst` (section 8), `proofs.json`
(`evolution-field-additive-monotone`, `evolution-old-document-differential`),
`tests/Fuaran.Core.Tests/IdlStabilityClassTests.fs`, `IdlThreeHostTests.fs`; rides the `0.34.0` draft
(STABILITY.md, "Phase 304").**

**D107.1 — the grading rule.** The table defines its classes by what happens to documents: `additive`
keeps every previously-valid document valid; `breaking-wire` is a document that was valid and is not,
or whose bytes moved. The classifier nevertheless graded two rows by what an EMITTER does, and got
both backwards: a required field added, and a field tightened to `required`, were `breaking-for-emitters`
(a minor) "because old documents still decode"; `required` loosened to anything was `breaking-wire` (a
major) "because a consumer that relied on presence now faces absence". Decoded, the first claim is
false — every old document lacks the member and the decoder refuses it, with or without an authoring
default, which fills on construction and never on decode — and the second describes NEW documents in
OLD consumers, which is host lag. The rule from here: **a row's class is read off what an old document
does under the new vocabulary — refused, decoded to a different value, re-encoded to different bytes,
or unchanged.** What emitters must do is reported beside it (`BreaksEmitters`), and what consumers'
source must do on the F# axis; neither decides the class.

**D107.2 — the rows it moves.** A required field added, `optional` → `required` and `omitDefault` →
`required` are `breaking-wire`: the major moves and an old consumer is `Foreign`, where the minor let a
`Behind` consumer tolerate a profile under which every stored document of the kind is refused.
`required` → `optional` is `additive`: every old document carries the member, decodes to the same
value and re-encodes byte-identically, and every old emitter writes it. **The consumer-presence
argument is recorded, not dismissed:** a consumer that relied on presence meets absence only in a
document a NEW emitter writes, and a decoder that predates the change refuses that document — host
lag, exactly as for a new enum case, which is what the minor's `Behind` already means; on the F# axis
the member becomes an `option`, which `full-literal-construction` carries.

**D107.3 — `required` → `omitDefault` stays `breaking-wire`, and the precedent is the moved identity
default.** The decoder reads a member sitting on the default and the encoder then omits it, so every
stored document carrying the default value re-encodes to different bytes. That is the argument the
`omitDefault` → `omitDefault` (default moved) row already rests on — omit-at-default is wire-visible —
and a hash-chained store re-encoding such a document would not reproduce it. The phase
expected every loosening to be "not a major"; for this one the table's own definition says otherwise,
and the table wins.

**D107.4 — int → float is `additive` by the same test**, which Phase 252 ruled and 293 carried into
the descriptor table: old documents decode and re-encode byte-identically under the widened
vocabulary. Phase 304 moves nothing there; the evolution differential holds it.

**D107.5 — held three ways.** (i) `field_additive_monotone` (`proofs/WireVersioning.fst` §8): every
field-add row kept off the major leaves every document that does not carry the added member decoding
identically through the tolerant seam. Its hypothesis is "not retired" rather than "`Additive`"
because `breaking-for-emitters` and `host-surface-only` also promise old documents untouched — a
statement exempting them would have been true of the old rule by construction. Stated so, it failed on
the pre-304 rule (a prover run with that rule restored fails at `c <> required_chars`;
`pre_304_rule_breaks_field_additive_monotone` is the witness in the model). (ii) The evolution
differential decodes a corpus of old documents under seven perturbed vocabularies through the
interpreter and the generated TypeScript decoder and asserts the class predicts both. (iii) The
section-7 row is restated over the corrected class (`required_field_addition_is_foreign_not_behind`).

**D107.6 — the int range is the interpreter's on every host.** The narrowing case found the generated
TypeScript `dInt` reading any integral number, where the interpreter's and the compiled F# host's int
is 32-bit: an old document carrying 2^31 at a slot narrowed to `int` decoded on one host only. The
three-way differential never reached it, because an authored `VInt` cannot exceed the range. `dInt` now
refuses outside [-2^31, 2^31 - 1], and a block of the three-way differential plants 2^31, -2^31 - 1 and
2^53 at the first int slot of drawn nodes in every certification vocabulary and requires all three
hosts to refuse, beside an in-range control all three accept.


## 2026-10-02 — D106: a footprint names the slot it writes; a whole-node write stays the conservative default; the keyed witness is not widened for a key no skeleton op can use

**Context.** Phase 340. A `Footprint` had four sets, all node-granular, so any write to part of a node
was a write to the node: two ops touching different fields of one node interfered, and the fold halted
on a pair that commutes. The imprecision had a measured cost in a consumer that maps every op of a
roadmap onto this record — two lanes writing different fields of one phase halted a whole side as an
operator-owned conflict no verb could resolve, twice, and each new field-versus-field pair would have
needed another hand-written safe class — and in keyed domains, where two lanes editing different keys
under one holder halted at the holder and the report named only the holder. Phase 334 had set out to
add a keyed-slot set and was retired on a refuted premise (two lanes placing different nodes into one
keyed slot do NOT fold clean; they halt at the holder), leaving the label as the residue.

**Decision.**

1. **A footprint carries two slot sets, not one.** `SlotWrites` and `SlotReads`, each a set of
   `(node id, slot name)` pairs. The shard asked for a fifth set and for `Footprint.readingSlot` — a
   reader of one field that does not depend on a write to another — and a slot read cannot be
   expressed in a write set or in the whole-node `Reads` (a whole-node read collides with every slot
   write of the node, which is exactly the dependence the reader is declining). Two sets mirror
   `Reads` / `ContentWrites` at slot granularity; a read set that was not there would have been added
   by the first consumer that needed it, as a second breaking widening.
2. **A slot access is compared at the slot against another slot access, and as an access of the node
   against everything else.** Two writes to different slots of one node commute; a write and an access
   of ONE slot are `Interference.SlotClash`, carrying the slots. A slot write of `n` collides with a
   content write, a read or a structure write of `n`, and a slot read of `n` with a content write of
   `n` (`LeftSlotsRightNode` / `RightSlotsLeftNode`). **A whole-node write stays the conservative
   default**: it is a write of every slot, so it serialises against each. The rule is what keeps every
   existing footprint sound without edit — a domain that declares no slot access gets the four-set
   verdict it always got, byte for byte — and it is where the precision comes from: a consumer narrows
   a write to a slot ONLY where it knows the op writes that slot and nothing else, and every write it
   has not narrowed keeps refusing every slot. Proved sound at a slot store (`proofs/DagFold.fst`
   section 17): different slots commute at every slot, one slot does not, a whole-node write is
   refused against each slot.
3. **One helper behind both readers.** `Footprint.slotClash` and `Footprint.slotsAgainstNode` compute
   the slot clauses' sets, and `Ops.interference` and `Dag.conflicts` both read them, so arbitration
   and the fold name the same slot by construction (the Phase 248 shape). `Dag.conflicts` reports
   `MergeConflictShape.SlotClash slot` at the node — the slot rides the shape because the conflict's
   address is the node — and the node-level slot collisions as `ConcurrentUpdate` at the node. A slot
   clash is its own address space and is not deduplicated against the node-keyed shapes: a pair that
   both touches a node whole and clashes on one of its slots reports both, because the slot is the
   thing a repair has to look at.
4. **Two writes of the same payload to one slot are still a `SlotClash`.** The footprint sees no
   payload. The consumer's own classifier decides whether two identical writes are one intent
   recorded twice, as it does today for identical whole-node writes; Core reports the clash and
   decides nothing (GP6).
5. **`KeyedWitness` is NOT widened with the key a keyed child sits under, and `Ops.footprintKeyed`
   declares no slot write.** The shard asked for both so a keyed placement could be declared a write to
   `(holder, key)` "where the op only places the keyed child". Measured against the tree, no skeleton
   op does that: the only ways to write a keyed slot are `UpdateNode` of the holder and `InsertChild`
   of a subtree carrying it, both of which rewrite the holder whole, and a pure script cannot say which
   keyed position — or which field — a payload changed (`KeyedSlotFoldTests`, Phase 334's pin, now
   with the slot cases beside it). A key in the witness would have had no op to serve, and a
   `footprintKeyed` that narrowed an `UpdateNode` to its keyed slots would have declared two rewrites
   of one holder independent — which they are not. A domain that places by key lowers its OWN op with
   `Footprint.slotEdit holder key`, which needs no witness; the engine's keyed walk is unchanged. This
   is the phase's premise finding, reported rather than built around.
6. **The skeleton law families do not draw slot pairs; the domain-op family counts them.** No skeleton
   op writes a slot, so `concurrencyLaws` and `keyedArbitrationLaws` have no slot pair to draw and
   are not pretended to. `Conformance.footprintLawsAt` — the family at a domain's own ops, where slot
   footprints live — gains a cell (a slot clash is named at the slot by the fold and by arbitration
   alike) and two demands (the pairs independent ONLY because slots are compared at the slot, and the
   pairs that clash on a slot), both vacuous BY DECLARATION for a footprint that declares no slot
   access, so the family's verdict on such a domain is unchanged.

**Consequences.** Breaking (source) on the open `0.34.0` draft, which already is: every full
`Footprint` literal gains two fields, every exhaustive match on `Interference` three arms and on
`MergeConflictShape` one. The record is on no wire. The consumer that motivated the phase can retire
its hand-written field classes once it lowers its field edits to `slotEdit` and raises its pin; until
it does, it sees exactly what it saw. Keyed domains stop halting on edits to different keys only for
ops they lower by key themselves — the skeleton `UpdateNode` of a holder keeps halting at the holder,
by construction, and the label on that halt is unchanged.


## 2026-10-02 — D105: a transparent case never carries what can be an object; at a float slot the §7 tokens are read back, not refused; a map's value is its key set; a field-less declaration is a marker type

**Recorded by Phase 303. `src/Fuaran.Core.Idl/Idl.fs` (`Decode`, `FloatToken`), `Artifact.fs`,
`src/Fuaran.Core.Idl.Codegen/` (the one type emitter, `Trust.checkHardenPolicy`, the TypeScript
runtime's verbatim encoder, `FStarTarget.vectorsModule`), `tests/Fuaran.Core.Tests/IdlThreeHostTests.fs`,
`IdlSchemaValidatorTests.fs`, `proofs/VocabularyVectors.fst`, `proofs.json`
(`vocabulary-vectors-agree`); rides the `0.34.0` draft (STABILITY.md, "Phase 303").**

**D104.1 — the transparent-case rule, stated once.** A declared transparent union case goes on the wire
BARE — its one field's value with no discriminator — and a reader tells it from the union's tagged
cases by the absence of the discriminator. That test is sound only when the bare value can never be an
object: the reference interpreter sends every object to the tagged arm, the generated F# and TypeScript
send anything without the discriminator to the bare arm, so a bare object either changes case on one
host or, carrying the discriminator, decodes on every host as a DIFFERENT case under identical re-encoded
bytes (`Lit({"$type":"Ref",…})` reading as `Ref`). The rule is therefore a property of the
DECLARATION, not of a value: a transparent case whose field type is object-capable — `json`, a record, a
map, a union, a node, or a type variable instantiated at one — is refused by `Declare.errors` at load
(`Artifact.ofJson`, `Proposal.applyDelta`), which Phase 292 shipped and this phase pins at the loading
boundary. It is the premise the F* target always stated in its own refusal; now there is one statement of
it, at the one place every vocabulary passes.

**D104.2 — the §7 direction.** WIRE_FORMAT §7 spells a non-finite float at a FLOAT slot as the quoted
token `"NaN"`, `"Infinity"` or `"-Infinity"`. The interpreter's encoder writes it, the generated F#
`dFloat`, the generated TypeScript decoder and the emitted schema read it, and the interpreter's own
decoder refused it — the reference host was the odd one out, refusing its own output. The direction is
to ACCEPT: the decoder's float arm reads exactly those three strings (nothing else, and at no other
slot — an `int` slot and a `json` slot do not widen), and the artifact reader reads a non-finite declared
default back. The opposite direction — routing the encoder through the guarded renderer so a non-finite
float is refused — was the second pass's first reading of Phase 292's task and is declined: it would
move the reference interpreter further from every host, and the guarded renderer stays where §7 does not
reach (a non-finite float inside a verbatim `json` or hosted value, which has no token of its own and is
refused by path).

**D104.3 — a map's value is its key set (the second pass's P1).** A decoded map is an entry list on the
interpreter (wire order), a `Map` on the generated F# (key order) and an object on the TypeScript host
(insertion order, integer-like keys first). The bytes agree — every encoder sorts a map's entries
Ordinal, under either key order — and the VALUES differ only in an order no reader may depend on. Settled by
statement rather than by sorting in any one host: the three-way differential's value normal form compares
a map as its key set, and so does its TypeScript leg's deep equality.

**D104.4 — a field-less declaration is a marker type.** F# has no empty record (`{ }` is FS3863), so a
field-less kind or record emits as the single-case union `R = | R`, its value spelled `R.R` through one
record-literal helper every F# site shares. Chosen over a nullary `NodeKind` case or a `unit` payload
because it keeps every `NodeKind` case carrying its spec, so the codec, the projection seam and the smart
constructors stay one shape. Before this phase the score vocabulary's generated module did not compile
and the suite, which only regenerated it, did not notice; every certification vocabulary's generated F#
is now compiled.

**D104.5 — a harden entry names what the floor can reach.** `Trust.checkHardenPolicy` refuses a
`UrlFields` / `MarkdownFields` entry naming no kind, no field, a host-only field, or a field whose type
the floor does not rewrite (anything but a `str` or a union carrying the declared literal case), through
the existing `UnsupportedConstruct` case, and a `str`-typed entry is sanitised directly. An entry used to
be matched by name and silently passed when it did not fit: a record-typed `href` holding
`javascript:alert(1)` survived `harden` verbatim.

**What the differential found, and was fixed in the same phase.** The TypeScript runtime's verbatim
JSON encoder wrote a whole number at or past 2^53 in `String()` layout (`1e+21`, or digits) rather than
the canonical float layout (`1E+21`); and the reference vocabulary's support document's projection
encoder omitted the kind discriminator — wrong since it was written, and visible only once the module was
compiled and run against the interpreter.

**What is not done here.** The F* facts see structure, presence and key order, not the float layout (the
model's float carrier is opaque) and not the §7 tokens; the three-way differential, a test, covers both.
The generated F# and TypeScript layers carry no op root, so a root op is certified on the interpreter and
against the schema only.

## 2026-10-02 — D104: one space relation answers every "does this value fit" question; a wire document's value space is `$type`-tagged and the descriptor's spelling is frozen; a signature and strict application agree on what a repeat requires; a registry holds only total capabilities

**Context (Phase 295).** The invocable seams had drifted around the parts that were designed
together. There were three incompatible answers to "does this space admit every value of that one":

- The pipeline's edge check ignored bounds, so an `IntRange(0, 1000)` output fed an `IntRange(0, 10)`
  argument.
- The function registry's hole match respected bounds but refused int into float, which validation
  accepted.
- The query seam asked type equality, while `ColumnType.widens` called itself the single source of
  truth.

The other drift:

- The effect was written three times and read twice, with two sentences for one refusal. The value
  space was written in two spellings.
- `Function.signature` marked a bounded repeat optional while strict `apply` refused it unbound.
- A capability over an unbounded repeat registered and dispatched.
- A pipeline with a cycle, a self-edge or a forward edge type-checked and failed only at evaluation.
- Evaluation ran bodies without type-checking.

**Decision.**

1. **THE space relation is `Space.subsumes required available`**, and every call site asks it.
   - The lattice: ranges compare by BOUNDS, and the one widening is int into float (a `FloatRange`
     admits an `IntRange` its bounds contain).
   - `StringLen` admits a `StringLen` it bounds and an `Enum` whose members it bounds. An `Enum`
     admits a subset `Enum`.
   - `AnyString` is the top of the SCALAR spaces, because every argument at the seam is a string.
   - The tree spaces are their own family: an unconstrained `SlotTree` admits every `SlotTree`, and a
     constrained one only its own kind. No scalar space admits a tree, and no tree space a scalar.
   - The relation is sound (true only where every value of `available` validates in `required`),
     and incomplete where the answer would need enumerating values (an `IntRange` against an `Enum`
     of digits, say). Incomplete is the safe direction for a type check.
   - The query seam asks `ColumnType.widens`, the same lattice over typed cells. It agrees with
     `subsumes` on the numeric types. It does not agree on strings, where it should not: a typed
     `string` parameter takes no `int` cell, while an `AnyString` hole admits the text `5`.
2. **A value space in a wire DOCUMENT is `"$type"`-tagged, with `min` / `max` bounds** (`SpaceCodec`),
   which is Phase 251's convention for anything a codec decodes back.
   - `toSchema` is a DESCRIPTOR and keeps its `"kind"` / `minLength` spelling, through
     `SpaceCodec.descriptorJson`.
   - That spelling is FROZEN, not merely kept: `ContentPack.signatureFingerprint` hashes `toSchema`'s
     bytes, so moving the descriptor onto the document spelling would re-pin every published pack for
     no change in meaning.
   - The reader takes the descriptor spelling too, leniently, for the 0.34.0 draft. Nothing writes it
     into a document.
   - The effect is untagged in both, because it is never a document of its own; `EffectCodec` is its
     one reader and writer.
3. **A bounded repeat is `Required`**, because strict `apply` demands it.
   - This is the direction that changes the signature, not the law: full application binds every
     data hole (Phase 181), and the signature is the projection of that law that had disagreed.
   - The cost, accepted by operator ruling: a signature with a repeat hole lists it as required, so
     its descriptor bytes and its pack fingerprint move.
4. **A registry holds only total capabilities.**
   - `register` refuses a non-total one, naming its entries (`NonTotalCapability`). Non-total means
     a repeat over an unbounded count, or an entry that projects to no hole kind.
   - `compose` checks totality on both parts, as `composeAcross` does.
5. **A pipeline is checked before it runs.**
   - `typeCheck` refuses a self-edge or a cycle as `PipelineCycle`, naming the cycle, and an acyclic
     forward edge as `PipelineForwardEdge`.
   - An argument refusal wraps the capability's own `InvokeError`.
   - `eval` / `evalFrom` take the lookup and type-check first, so they are no longer a second
     dispatch path beside the registry's.
   - Both registries project the lookup, so a host with content packs keeps one registry.

**Rejected.**

- Retyping `SigEntry.Kind` to `HoleKind`, as the phase first proposed. `HoleKind` carries the space
  and the effect ceiling the entry already carries in `Space` and `Action`, so the retype would make
  a second copy of each that could disagree: the defect class this phase removes from `Capability`.
  The typed reading is a member (`SigEntry.HoleKind`). The tag is spelled once (`HoleKind.tag`), and
  the codec refuses any other.
- Keeping the old `CapabilityPipeline.eval` / `evalFrom` beside type-checked twins. The old forms
  were the unchecked path, and keeping them keeps the path.
- Canonicalising inside `Space.validate`'s answer instead of the capture key. `validate` answers a
  `bool`. The value is handed on typed by `typeArgs` / `invokeWithArgs`, and `invocationKey` keys
  each argument by `Space.canonical`, so one value has one key wherever the key is built.

**Consequences.**

- The class is breaking (source) and, for the repeat's `required`, breaking (wire); both ride
  0.34.0's operator ruling.
- `proofs/Capability.fst` and `proofs/Query.fst` restate required-ness, totality, registration, the
  widening and the keyed list, and both oracles are re-extracted.
- `capabilityPipelineLaws`, `registryLaws` and `queryLaws` each pin their call site to the relation.


## 2026-10-02 — D103: a content-changing survivor is rewritten before the children it gains and after the children it loses; a contained undo is closed only from a contained pre-state; arbitration refuses a malformed base instead of arbitrating it

**Recorded by Phase 305. `src/Fuaran.Core.Ops/Ops.fs` (`Diff`, `normalize`, `invert`, `invertAll`),
`src/Fuaran.Core.Ops/Arbitration.fs`, `src/Fuaran.Core.Tree/Tree.fs` (`Index.buildWith` /
`isFreshForWith`), `proofs/TreeDiff.fst` section 12, `proofs/Preservation.fst`, `proofs.json`
(`diff-applicable-contained`); rides the `0.34.0` draft (STABILITY.md, "Phase 305").**

**The finding.** `Diff.toOpsContained` checks `after` — no node `canHold` refuses may hold children —
and emits a script that runs against `before`. The structural diff carries no content, so a survivor
keeps `before`'s kind at every step: on `before = root(p:para)`, `after = root(p:section(q:para))`,
`canHold = kind <> "para"` it returns `Ok [InsertChild(p, q)]` and `Ops.applyAllWith` refuses the one
step with `NotAContainer(p, para)`. Over independent pairs with kinds drawn freely the second-pass review
measured 41% of accepted scripts refused; `ContentDiffTests`' bridge, over its own generator, measures
469 of 1,540. The proved row `diff-applicable-contained` said such a script "cannot be refused for
containment at any step": the theorem is about the ADDRESSES the script carries, resolved in `after`,
and the sentence was a gloss the theorem does not support. The row is reworded; the pair is pinned in
the model as `TreeDiff.contained_script_refused_at_before_kinds`, so the false claim cannot be re-proved
by accident.

**D103.1 — the placement rule.** The content-aware forms (`toOpsWith`, `toOpsContainedWith`,
`toOpsGrammarWith`) emit an `UpdateNode` for every survivor whose own content the caller's encoder
reads differently over the two SHELLS (`ReplaceChildren n []`, so a change in the children alone is
never a rewrite), and place them in two blocks around the four structural passes: **a rewrite whose
new node `canHold` accepts goes FIRST**, before any insert or move, because that node is the parent of
the inserts and moves under it and `validateInsert` / `validateMove` read the kind the tree holds at
that step; **every other rewrite goes LAST**, after the reorders, because a node becoming a leaf may be
rewritten only once its children have left, and they leave in the moves and the removals (a childful
leaf in `after` is refused up front). An `UpdateNode` keeps the children the tree holds, so it is inert
to the structural blocks wherever it sits; the two sites are where the containment check is satisfied.
Measured: the content-aware script is refused on 0 of 1,540 pairs and lands on `after` content
included on every one; appending every rewrite LAST instead is refused on the same 469 pairs the
structural script is (the bridge's falsifier), because the leaf-turned-container is what refuses.

**Why `With encode` and not `'Node : equality`.** The witness has no content accessor and demands no
equality of `'Node`; a diff that compared nodes would add a constraint every caller with a function in
its node type cannot meet. The encoder is the parameter `Tree.encodeHash`, `Tree.Index.buildWith` and
every content-reading seam already take, with the same injectivity precondition. The structural forms
are unchanged in output: `TreeDiff.fst` models them clause for clause and its 2,000 lines of
reconstruction proof are about THAT emission, which the structural-part bridge holds the new forms to.

**D103.2 — the contained undo is closed only from a contained pre-state, and that is a theorem's
shape, not a defect.** The phase asked that "a contained remove on a violating pre-state undoes".
It cannot: the inverse of `RemoveNode q` under `p:para` is `InsertChild(p, q)`, and `validateInsert`'s
`canHold p` is the containment guarantee itself (`contained_preserves`) — any engine that accepted it
would admit a leaf gaining a child. The same holds of an update that REPAIRS a violation (`p:para(q)`
rewritten to `p:section(q)`): its inverse re-creates the violation and is refused under any rule that
keeps the invariant. So there is no `invertContained`; the round-trip law for the contained engine is
stated **conditional on a contained pre-state** (every node with children satisfies `canHold`), which
is `Preservation.fst`'s standing shape for every contained theorem, and `Ops.invert` / `invertAll`
stay the plain engine's inverses. A domain that needs to undo on a legacy violating tree undoes
through `Ops.applyAll`, which is what its forward edit on that tree used.

**D103.3 — `validateUpdate`'s containment check is KEPT as it is, and the ruling says why.** The
phase offered relaxing it to "only when the kind changes". That rule assumes `canHold` is a function of
the kind, which the predicate's contract does not say; the honest relaxation is "refuse only a rewrite
that takes a childful node from `canHold`-accepted to refused", which keeps `contained_preserves` and
stops blaming an already-violating node for a rewrite that does not worsen it. It is not taken here
because it moves the modelled `apply_contained` arm, its refusal characterisation and
`contained_preserves` in `Preservation.fst` together, and a rule whose proof is a successor's is not
shipped ahead of the proof under `debt: forbidden`. What it would buy is bounded: a no-op or
content-only rewrite of a node that already violates. The successor that takes D103.2's conditional
theorem takes this with it.

**D103.4 — arbitration refuses a malformed base.** `arbitrate`, `arbitrateContained`,
`arbitrateGrammar` and `arbitrateReferenced` check `Tree.wellFormed` on the base first; a repeated id
`d` makes every proposal `Inapplicable(0, DuplicateId d)` in pinned order, with nothing accepted and
nothing merged. The shape is the existing envelope rather than a new `ArbitrationRejection` case (a
case addition breaks every exhaustive match, which is the class this draft does not take for a
refusal the base owns); index 0 says no op was offered. `arbitrateWith` takes no witness and is
unchanged — a keyed domain checks `Tree.wellFormedKeyed` before composing it. The confluence the
accepted set promises (`TreeOps.tree_independence_diamond`) is stated at `wf`; below it the promise was
empty and the function ran anyway.

**D103.5 — what stayed open across the re-open, and how each closed.** (a) The redundant trailing
`ReorderChildren` after appends is DROPPED (the last item of the re-open to land). Dropping it
changes the modelled pass 4, so it needed the order-prediction lemma — after passes 1–3 a parent
holds its kept survivors in `before` order, then the inserted shells, then the moved-in survivors,
each in `after` order — and dropping it in production alone would have broken the extraction
differential against the model that proves reconstruction. The lemma is `TreeDiff.diff_settles_order`
(`diff-settled-order` in `proofs.json`), and HOW it was proved is the decision worth recording:
section 10's four invariants were written about MEMBERSHIP, and they were left exactly as they were;
three exact-order invariants (`ord1`/`ord2`/`ord3`, one per pass, each a `keep` over one of the two
trees' child lists under a predicate naming the pass's worklist) ride BESIDE them through the same
`_run` lemmas, with one order lemma per step (`ins_ord_core`, `move_ord_core`, `rem_ord_core`) that
reads the per-node view and the list algebra and nothing else. Two choices inside that: the order
invariants are `opaque_to_smt` and revealed only where a body is used, because a transparent `ord1`
in `inserts_run`'s context sent Z3 past the leg's memory on the first attempt (the run lemmas only
pass the invariant along); and production's "moved in" test in step 4 reads the parent's
before-children (`bChildKeys`) rather than step 2's `bParent`, the two agreeing on a well-formed
`before`, so the model's `settled_order` is `keep` over child lists and the kid map — the same three
lists the invariants are stated over — with no parent-map bridge to carry. `inv4` gained one
disjunct (a listed parent holds the predicted order OR `after`'s) rather than the predicted order
alone, so nothing depends on the worklist being duplicate-free. (b) The
`Batch` and script lifts of `invert_applicable` are PROVED (`Preservation.fst` section 15,
`invert_batch_round_trip` / `invert_all_round_trip`, added when the phase was re-opened to finish).
`Normalize.fst` is PROVED too (the continuation of the same re-open): `normalize_preserves` for an
applyable script at a well-formed tree, `normalize_idempotent` and `normalize_never_longer`
unconditionally, with the three uniqueness-dependent rows (move/move, reorder/reorder,
rewrite/remove) proved extensionally through `Preservation`'s per-node views and `TreeDiff.tree_ext`,
and the reason the law is conditional pinned as `cancellation_can_admit_a_refused_script`.
`merged_applies_and_order_free` is PROVED too (`Arbitrate.fst` section 11): at a well-formed base the
merged script applies and every order of the accepted set reaches the same tree, read off
`DagFold.replay_perm` at `Skeleton`'s instantiation with the accepted scripts as lanes — and the `wf
base` hypothesis is exactly what D103.4's refusal makes true of every base production arbitrates. And
the content-aware run theorem is PROVED (`TreeDiff.fst` section 13, `diff_applicable_contained_run`):
section 10's induction was not restated over six blocks but REUSED — the first update block takes
`before` to the same tree recoloured from `after`, the four passes emit the same script for it (they
read no kind), the contained engine agrees with the plain one at every structural step because each
addressed parent already carries `after`'s kind, and the last block rewrites the leaves `canHold`
refuses once their children have left. So of (b)'s four theorems none is open; the bridges that
shipped first (`ContentDiffTests`) stay as the shipped engine's sample of each.

## 2026-10-01 — D102: a ladder row states what it is true OF, and a premise production violates is either a refusal or a row that says so

**Recorded by Phase 309. `proofs/`, `proofs.json`, the proof leg's kit (`proofs/kit/check-proof-leg.ps1`)
and the `Proofs.*` families; rides the `0.34.0` draft (STABILITY.md, "The ladder tells the truth about
production").**

*Decided: the honesty rule.* A row in `proofs.json` names the object it is true of, and no wider one.
Where production violates a premise a row leans on, exactly one of two things is true: production
REFUSES the violating input (so the premise is a property of every value the code lets through), or
the row SAYS that it does not hold there. A premise that production quietly violates while the row
reads as unconditional is the defect this decision exists to prevent. Applied by this phase:

- `node-ids-distinct` hid a REPLACEMENT: until Phase 296, `Dag.append` / `Dag.merge` overwrote a held
  node on a content-id collision (`Map.add`) while `verifyDag` still passed, and under the 32-bit
  FNV-1a default such a collision is expected near 77,000 nodes. The refusal side is now a theorem
  (`append_refuses_differing_node`, `append_never_replaces`), and the row narrows to what is left:
  the hash is injective on the nodes a store HOLDS.
- `content-id-determines-content` is false for `OpStream.defaultHash` at scale (Phase 302 already says
  so); `chain_tamper_evident_iff_hash_distinguishes` now states, with no premise, what that costs —
  an op tamper is hidden exactly where the hash collides on that record's envelope, and nowhere else.
- `Dag.replayTo` carried no row while every store is replayed through it; it is a theorem over the
  proved drain now (`replay_to_is_fold_over_drain`, `replay_to_unknown_head_is_refused`,
  `replay_to_deterministic`).
- `witness-surface-scope` is marked `"discharge": "domain-declared"`: its law checks a domain's
  DECLARATION and cannot check past it, so a domain that declares nothing passes. A green run of such
  a law is not a discharge, and the contract table and the coverage line now say which rows are which.
- the id-witness equality every model consumes (`Equals a b <==> ToString a = ToString b`) is named,
  as `id_key_faithful` in `TreeOps.fst`, and carried on `lawful-abstract-witness`, whose law samples
  exactly it.
- stale counts (`invert-round-trip`'s "four", `apply-preserves-wf`'s and `contained-preserves`' "five")
  and the diff's stated reason for `kinds_agree` ("no skeleton operation edits a node", false since
  Phase 250's `UpdateNode`) are corrected; the diff is a SHAPE diff until Phase 305.

*Decided: the extractor premise is discharged on sampled inputs, by twins that live in the model they
sample.* Each extracted model ends with a `twins` list asserted by `assert_norm`, so F*'s normaliser
evaluates every fixture; the list is extracted with the model and run by the host against the
extracted oracle. One twin module beside all the models was rejected: it would reference every model,
so the Phase 328 cone selector would put every model into every cone and the saving it exists for
would be gone. The kit's step 2c (`-Twins`) refuses an extracted model that declares none.

*Decided: the README's numbers are a projection.* The header count, the contract table and the
contract prose's counts are generated from `proofs.json` (`CORE_APPROVE_LADDER=1`, the
`Proofs.Ladder` family), because two hand-kept copies of one number disagree eventually — the header
said "eight in all" over a ladder of twenty-four models.

*Declined, with the evidence: a "grammar conformance not claimed" row beside `parse_total`.* Its
premise — that `JsonParse.fst` proves a number grammar wider than RFC 8259 — stopped holding when
Phase 299 held `parseNumber` to the JSON number grammar and Phase 306 restated the model
(`is_json_number`, `number_grammar_is_checked_first`); the twins sample it. A row disclaiming a
width the model no longer has would itself break the honesty rule above.

*Not done here, and why:* `tree-algebra-well-formed-states` keeps its discharge until
`Tree.Index.build` refuses duplicates and `arbitrate` checks its base — neither had shipped when this
phase was cut.

## 2026-10-01 — D101: the observer is a witness and its state a value; a defect names its family and its supporting nodes; a union whose case names collide anywhere on the spine is qualified — D1 made general

**Recorded by Phase 298. `Fuaran.Core.Observer`, `Validator`, `Propagation`, `Projection`, `AiSurface`,
`Tree`, `Ops` and the conformance kit; rides the `0.34.0` draft (STABILITY.md, "The five smaller seams
are total").**

*Decided: the observer's shape is the spine's.* `ObserverWitness<'Input,'Flag>` is the domain's pure
derivation and its emit policy; the registry is `ObserverState`, an immutable value of entries by id
and the ids in registration order; `ObserverWitness.register` / `update` / `unregister` / `snapshot` /
`observeTree` are functions over it, each returning the emission it produced. Registration order is
part of the state because the walk's determinism must not depend on a container's enumeration (a
`Dictionary` reuses freed slots on .NET and not under Fable). **Subscription is host state, not a
witness function**: a pure function cannot hold a subscriber, so the functions return emissions and
the `InMemoryObserver` adapter keeps the subscriber list and delivers to a snapshot of it. The OO
surface stays one draft as that adapter; `IObserver.Register` still declares no parent and is not
widened (widening an interface breaks every implementer for a surface that is leaving). *Not built:*
an optional `NodeWitness` on the observer witness — observing a domain tree directly is
`Tree.preorder` then `ObserverWitness.derive`, a line in the consumer, and a `Tree` dependency would
have cost this package its FSharp.Core-only footprint for it (reuse over a new seam).

*Decided: `Defect` carries `Family` and `Related`, in this draft.* The shard deferred both to "the next
breaking draft"; `0.34.0` already is one (Phases 252 and 248), so the deferral would only have
scheduled a second break for every domain's literals. `Family` is stamped by the walker, never trusted
from the rule — the `runPack` precedent, where a rule cannot mis-cite itself — so the provenance the
`PackRule` convention promised is on every finding. `Related` is the supporting enumeration several artefact domains each
added to their own copy of the record, and is what
`Rejection.UnknownNode(target, addressable)` and `RejectionGuidance.Alternatives` already carry.
`runAllTagged` / `validateTagged` keep the pair form beside it for a caller that groups by family.
*Not built:* retyping `RuleFamily<'Node,'Id>` into a subject-generic `RuleFamily<'Subject,'Loc>`.
Phase 315's `Validator.PackCheck<'Subject,'Id>` already IS that rule; `Validator.asCheck` and
`ColumnValidator.asCheck` make a tree family and a column rule instances of it, so retyping
`RuleFamily` would break every tree family for no capability.

*Decided: a registry refuses a repeated id.* A total append made a duplicate a silent doubling of
findings and gave two families one provenance id. Both `register`s return
`Result<_, RegistrationError>`; `ofFamilies` / `ofRules` are the pipeline form. Stock column rule ids
are `Hash.canonicalFields` over the kind and every parameter, because once ids are keys an ambiguous
id (`unique ["a,b"]` against `unique ["a"; "b"]`, two `inRange`s over one column) is a false refusal.

*Decided: D1 is general.* D1 qualified `Severity` because `Error` shadowed `Result.Error`. The rule it
stated is about any union: **a union whose case names collide with a case anywhere else on the spine
— or with a name a consumer plausibly owns — is `[<RequireQualifiedAccess>]` when it ships.** This
draft applies it to `PolicyDecision`, Projection's `Scope`, `Proposals.ProposalStatus` and
`Propagation.PropagationError`; a new public union is checked against it at review.

*Decided: a denial is guidance, and approval is gated by the approver's policy.* `PolicyDecision.Deny`
carries a `RejectionGuidance` and `SubmitDenied` hands it on, because the witness's `Explain` is typed
over the reducer's rejection and the frozen witness cannot gain a field: the guidance has to ride the
decision. `approve` refuses the proposal's author and refuses when `Decide` DENIES the approver one of
the ops; a `NeedsApproval` for the approver is not a refusal, since approving is that approval (a
policy that does not distinguish actors would otherwise make every parked proposal unapprovable).

*Decided: the walks are iterative and the updates path-copying.* The Tarjan pass replays the recursion
with an explicit frame stack and is tested item-for-item against the recursive one; `Propagation.Plan`
holds what a tick used to re-derive. `Tree.updateNode` rebuilds only the root-to-target path.
`canApply (MoveNode _)` validates through `validateMove`, the move's checks in the order `apply` has
always refused in, without building the tree.

## 2026-10-01 — D100: every gated arm is counted where its evidence is built and reds at zero; a query result is bound by a law, not by a refusal; a premise names the family that carries it and is discharged by nothing

**Recorded by Phase 302. `Fuaran.Core.Conformance` (the kit's runner `LawKit`, `SampleAdequacy`, the
family roster, and the families named below); rides the `0.34.0` draft (STABILITY.md, "The kit's
non-degeneracy floor").**

*Decided: the floor is a property of the runner, not of each family's discipline.* An audit ran every
family against broken witnesses — a constant encoder, a constant `HashFn`, a case-blind `Equals`, a
refusal-free stream generator, a node `encode` that ignores its input — and every cell that stayed
green had one shape: evidence drawn, then gated on a difference the defective witness cannot produce,
with the skip uncounted. Phase 297's `LawCell` already makes an UNCOVERED law red at zero evidence. What
this decision adds is that a COVERED law — one that reads green at zero because a guard counts its arm —
is only covered where (1) the guard's counter is taken INSIDE the gate the law is asserted under, and
(2) the guard the cell names is one the family emits. The suite holds (2) to the sources: every covered
dimension a topic file names must open a guard dimension some reference run emits.

*Decided: a replacement is redrawn, bounded, before an arm gives up.* A single draw that happened to
encode like the op it replaces skipped the arm; sixteen draws make the arm reached on every iteration
the generator CAN distinguish, and a run that never could is reported by the arm's guard or strict
cell. Not unbounded — a generator with one op must terminate — and not raising the iteration count,
which is the remedy Phase 106 recorded as the trap.

*Decided: where a law is about a pair the generator rarely draws, the family draws one.* Measured at
this repository's reference witness, `footprintLaws` drew three hundred pairs of four-op scripts and
not one independent pair of NON-EMPTY scripts: a remove, move or update carries an unknown-parent write
that interferes with every structure write. Every independent pair it had counted held an empty
script, so the soundness law behind `independence-diamond` had commuted nothing. The confluence
families now draw a short independent pair (bounded) where the drawn pair interferes, and count only
non-empty ones. This BIASES the sample toward the law's subject, which is the point of a law about
independent pairs; the monotonicity and determinism laws still read the drawn pair.

*Decided: the query result is bound by a law; `Query.invoke` does not refuse.* A resolver that answers
a table of another schema, or rows `Table.validate` refuses, was green everywhere. Two routes: a new
`QueryError` case that `invoke` raises Core-side, or a law in `queryLawsWith` / `queryLawsAt`. A new case
is breaking for every exhaustive match over `QueryError`, and it would make Core judge a host's answer
on every call to catch what a conformance run catches once. The law carries it; a host that wants the
refusal at run time calls `Table.validate` and compares the schema itself.

*Decided: a premise names the family that carries it and is discharged by nothing.*
`content-id-determines-content` is the cryptographic premise of the theorems that take it, and the
ladder's rule is that a premise is discharged by nothing — sampling cannot prove injectivity. Its claim
now says it holds for a host's collision-resistant `HashFn`, is false for the default FNV-1a at scale,
and is CARRIED, sampled, by `hashFnLaws`' three new arms; the row names no `dischargedBy`. The
discharge that was wrong moved instead: `independence-diamond` is about a domain's own ops, so it is
discharged by `footprintLawsAt` (Phase 249), not by `footprintLaws`, which samples the skeleton algebra
that `tree-independence-diamond` already proves.

*Not done here, and why.* The proof-coverage clause that maps every public operation in `api/*.txt`
to a ladder row, a family or a recorded exclusion is NOT in this phase: it is an inventory of the whole
public surface, curated row by row, and the coverage checker and its exclusion file belong to a
concurrent phase's region. The tree differentials' structural comparison — the other half of that
task — is here. Two smaller audit findings are recorded rather than fixed: `keyedArbitrationLawsWith`
waives its keyed-clash demand when the DRAWN trees never carry a keyed child, which reads as "declares
no keyed position" although a witness can declare positions its generator never fills (the witness has
no declaration to read instead); and a covered cell's dimension is checked against guard dimensions by
source scan, not by the type system.

## 2026-10-01 — D99: a decode refusal is a code from a closed set and a path from the root; the code set is the wire-level decode contract every host mirrors

**Recorded by Phase 310. `Fuaran.Core.Wire` (`DecodeCode`, `PathSegment`, `DecodeError`, `Decoder<'T>`,
the `Decoder`, `DecodePath` and `DecodeError` modules, `Corpus.RejectVector` / `runRejects` /
`mutations` / `refusalLaws`), the `…Detailed` entry points of `Idl.Decode`, `Artifact`, `Proposal`,
`CapabilityCodec` and `CapabilityPipeline`, and the `conformance/decode/` family; rides the `0.34.0`
draft (STABILITY.md, "A decode layer with typed, path-carrying refusals").**

*Decided: the code set is closed, and it is the CONTRACT.* `InvalidJson`, `MissingField`, `WrongKind`,
`UnknownTag`, `OutOfRange`, `UndeclaredMember`, `LimitExceeded`, `NotAdmitted`, `SchemaFault` — named on
the wire by their case names, as `JsonErrorKind` and `ColumnError` already are. A host mirrors the set
and adds no code of its own; where a host draws a distinction finer than the set (the UI host tells an
unknown node kind from an unknown case, and an empty node id from any other value outside its domain),
its code is an INSTANCE of one of these, and a test holds the UI host's eight §4d codes to the set with
no residue. `NotAdmitted` and `SchemaFault` are in the set because two different remedies hid behind
one sentence without them: a known spelling this reader's policy refuses (the UI host's
`KIND_NOT_ADMITTED`; a proposal minting a host-surface type) is not an unknown one, and a vocabulary
that names a type it never declares is not the document's fault.

*Decided: the path is steps from the root, and a `MissingField` path names the member that is
absent.* Keys and indices, root first; carried on the wire as an array of strings and integers so no
host parses a rendered path back; rendered `$["a"][0]`, the spelling `Json.firstNonFinite` already
used. The law is that a refusal's path RESOLVES in the document it was raised over — every step for
every code but `MissingField`, whose last step names a member the object at the rest does not carry,
and `InvalidJson`, which names the root. `Corpus.refusalLaws` holds every codec with a typed entry
point to it over structural mutations of real encodings, and it found two refusals that named nothing
on its first run (a dispatch that reworded an inner unknown tag with its own tag's sentence; an
artifact reader that answered a non-object root with a member under it).

*Decided: the sentence stays, beside the code.* `DecodeError.Message` is the sentence the string
forms always returned, byte for byte, so a codec moved onto the layer reads the same to every existing
caller: its string entry point is the typed one with `DecodeError.describe`. The string combinators in
`Decode` are forwards onto `Decoder` for one draft and leave at the next breaking one.

*Decided: an optional member is three-valued.* Absent is `Ok None`; present and refused is the
refusal, never absence. Every hand-rolled optional reader this replaced read an ill-typed member as
absent and substituted its default, so a malformed member went silently unread. The codecs moved onto
the layer now refuse it (STABILITY.md lists where). The one reader that keeps the old reading is
`Diff`'s snapshot, a tolerant CLASSIFIER by design (a revision written before a key existed must still
compare), and it discards a refusal deliberately at three named adapters over the layer, not through a
private copy of it.

*Decided: the strict member policy is the layer's.* Phase 251's `ReadPolicy.Strict` was two private
copies of one members check (the capability and query codecs); `Decoder.members` / `closed` /
`undeclared` are that check once, with the member's path, and both codecs read through it.

*Decided: the reject family is vectors over a SHAPE grammar, and it pins the code and the path, not
the sentence.* A host certifies by reading each input through its own combinators for the shape and
reproducing the code and the path; the sentence beside a refusal is the host's own. The grammar is
small on purpose — six shape kinds, enough to reach every code — and is described in the family file
itself, so a host needs nothing but the file.

*Not done here:* the generated F# decoder still reports a `string` (its error contract mirroring this
one moves every generated decoder's public type and the committed generated fixtures, and lives in the
generator); `QueryCodec` keeps `QueryError` as its published refusal and reads through the layer
underneath; the columnar codec keeps `ColumnError`, which already spelled the layer's faults through
Phase 299's `…With` forms; the versioning envelope reads through the layer and keeps its `string`
refusal; and the op-stream, DAG and projection readers, which publish `string` refusals of their own,
are not moved.

## 2026-10-01 — D98: a containment grammar is data the domain declares, never a kind Core knows; references are a witness the engine reads and never rebuilds through

**Recorded by Phase 313. `Fuaran.Core.Ops` (`Ops.applyGrammar` / `applyReferenced` and their dry
runs and sequence forms, `Ops.isLegalChild`, `Ops.illegalChildren`, `Ops.footprintReferenced`,
`Footprint.reading`, `RefWitness`, `Rejection.IllegalChild` / `StillReferenced`,
`Diff.toOpsGrammar`, `Arbitration.arbitrateGrammar` / `arbitrateReferenced`), `Fuaran.Core.Validator`
(`containment`, `referenceIntegrity`, `referenceOrder`), `Fuaran.Core.Propagation` (`Graph`); rides the
`0.34.0` draft (STABILITY.md, "A structural-integrity strand").**

*Decided: the grammar is a function from a parent's kind tag to the kind tags it may hold, `None`
meaning any, handed to each call beside `canHold`.* Every domain with a grammar kept the same table
and its own refusal, and a domain whose view dropped illegal children silently had nothing to refuse
with. Core still knows no kind: the grammar is a value, read through ONE predicate
(`Ops.isLegalChild`), so the engine, the diff, arbitration, the validator family and the kit agree
about what a legal child is without any of them naming one. It is an argument, not a witness field,
for the reason `canHold` is (Phase 251): a frozen witness does not grow, and a domain without a grammar
pays nothing.

*Decided: the grammar and reference forms WRAP the container-aware engine rather than widening it.*
A step is first decided exactly as `applyContained` decides it; the new clauses run only on a step it
accepted, against the trees before and after it. So no operation the engine refuses changes class —
D38's ordering, at the level of a whole engine — and the existing engine, its proofs and its oracle
are untouched. The grammar clause reads only the pairs the step creates (from a grammatical tree, the
only ones that can be illegal afterwards); the model in `proofs/Preservation.fst` section 13 checks
the whole result and proves preservation, refinement and the no-grammar identity, and the equivalence
of the two readings is `Conformance.containmentLaws`' agreement law at a domain's own grammar.

*Decided: references are a witness of their own, `RefWitness {RefsOf; DeclsOf}`, frozen at birth.* A
reference resolves when some node declares its id. The engine READS it — `StillReferenced` refuses a
remove, or a rewrite that declares less, that would leave a resolved reference dangling, and the footprint reads every id a script writes
a reference to — and never rebuilds through it. An insert or rewrite that brings in an unresolved
reference is NOT refused: a document under construction refers ahead of what it declares, so whether
it resolves is the validator family's report, not the engine's verdict.

*Found, and recorded rather than re-filed: among the skeleton ops the remove-versus-reference race was
already serialised.* A `RemoveNode` writes an unknown parent, so it collides with every structural
write and every in-place rewrite; the race is live for a DOMAIN op footprinted with
`Footprint.contentEdit`, which carries no unknown-parent write. `Footprint.reading` closes it there, and
`Ops.footprintReferenced` names the collision for the skeleton ops instead of leaving it to the
over-approximation. The read meets a removal's content-write when the declared id is the declaring
node's id; a domain declaring names that are not node ids folds the destroyed names into its own
removal footprint.

*Decided: the four reference defects are a typed list first and a rule family second.*
`Validator.referenceDefects` and `forwardReferences` answer `ReferenceDefect` values a caller can act
on; `referenceIntegrity` and the opt-in `referenceOrder` render them as coded defects. Cycles are
`Propagation.sort`'s strongly-connected groups — one linear Tarjan pass, cycles as data — and the graph
functions are re-exported as `Graph` so a consumer with no evaluator finds them. A forward reference is
one whose declarer is a LATER BRANCH below the two nodes' lowest common ancestor; an ancestor's or a
descendant's declaration is never forward.

*Not done here:* a keyed grammar form (the grammar reads `Children`, the surface the engine edits); a
refusal of dangling references on insert (above); and moving any domain onto the forms, which is each
consumer's own change.

## 2026-10-01 — D97: a compacted stream is a VALUE that reads its boundary, the writers refuse what the reader cannot read back, and strict replay never answers live

**Recorded by Phase 301. `Fuaran.Core.OpStream` (`Compacted`, `OpStream.Compacted`, the `try…`
writers, `replayEffectStrict`), `Fuaran.Core.OpStream.Dag` (`Dag.tryToJsonl`); rides the `0.34.0`
draft (STABILITY.md, "The compacted stream lives on").**

*Decided: the compacted stream is a value, `Compacted<'Op,'State>` — snapshot, tail and the
discarded history's key index — and the operations on it are new members beside `append`, not a
change to `append`.* The shard offered two shapes. The other was to make `append` / `head` /
`KeyIndex.ofStream` read `last.Seq + 1` and the last hash and accept a boundary. Rejected: `append`
numbering from the list's length is what `proofs/Chain.fst` §6 models (`append_rec` at the record
count), what every verified stream agrees with, and what an unverified stream's next record is
measured against; changing it would move a proved function's meaning on exactly the inputs where it
matters (a stream whose stored sequences are not its positions) to fix inputs where it does not
apply (a tail). A tail is not a stream that starts at its genesis, and a value that carries its
boundary says so in its type. `append` stays the form for a stream from its genesis; `Compacted` is
the form for one that begins at a snapshot, and §7b proves the two mint the same record
(`append_after_compact`), so nothing is lost by having both.

*Decided: `Keys` is the PREFIX index, fixed at compaction, and no hash binds it.* A key index is a
value the caller threads (Phase 82); after compaction the prefix cannot be re-folded, so the index of
what was discarded has to be carried, and `Compacted.keyIndex` continues it over the tail
(`key_index_rebuild_parity`). Binding it into the snapshot's hash would change the snapshot's
pre-image and with it every snapshot line's meaning, which Phase 301's "bytes on disk are unchanged"
excludes; so it is trusted exactly as a `ChainOnly` snapshot's state is — as far as the act that
compacted. **No on-disk format for it is minted here.** A host that persists a compacted stream
persists `Keys` beside it (it is a map of key to `{Seq; Hash}`); a line format for it, and whether
that line belongs in the snapshot line as an extra member old readers would ignore, is a format
decision left to the first consumer that asks, because it is irreversible once written and nothing
yet reads it.

*Decided: the writers CHECK; they do not escape.* The shard offered a scanner that keeps the exact
raw span and writers that escape. Rejected: the span IS the bytes the chain hashed, so re-spelling
an encoding at the writer would write a line whose op the reader hands the decoder in bytes the hash
never saw — the failure moved, not removed. The checked writers (`tryToJsonl` and its siblings) take
the encoding as it is and refuse one that would not come back as itself: a line break, whitespace
around the value, or anything but one JSON value (`OpStream.Jsonl.checkRaw`). The unchecked writers
are unchanged, for every caller that already holds well-formed encodings and wants no `Result`; a
line or paragraph separator inside a string passes, because the canonical escaper emits it raw and
the reader splits on `\n` alone.

*Decided: strict replay is a second member, and its label check is exact.* `replayEffect`'s live
fallback is a contract callers rely on (a legacy journal), so `replayEffectStrict` sits beside it
and refuses instead: exhausted, another identity, another label, a label that is not canonical.
"Canonical" is `Effect.determinismTag`'s vocabulary, copied here because this package sits below
`Function` (`isDeterminismLabel`, held equal to `Effect.tryDeterminismOfTag` over a label corpus by a
test); case-exact, so `Deterministic` is refused rather than journalled as some other label. A
truncation the session never reaches cannot be seen by replay at all, so the journal's head is the
anchor (`captureHead`, `verifyCapturesAt`): the chain binds every sequence, so the head fixes the
length, and a host records it in its op stream or has an `IAttestationSink` sign it.

*Decided: the base run certifies linear persistence.* `streamLaws` gains the JSONL round-trip cell,
so `certify` / `certifyStream` go red for a domain whose `Encode` is not one single-line JSON value.
That is a run that can turn red without a line of the domain changing — named in STABILITY.md as
the one adoption cost — and it is taken deliberately: a stream the domain cannot write and read back
is a stream it cannot keep, and a base certification that is silent about it is the gap Phase 301
was filed for.

## 2026-10-01 — D96: the F\* model refuses a shape its literal cannot carry; the classifier's rules are one descriptor table and read `support.json`; `Codegen.fs` splits behind the `Gen` facade with one type emitter

**Recorded by Phase 293. `Fuaran.Core.Idl.Codegen` (`FStarTarget`, `Diff`, `Gen`); rides the `0.34.0`
draft (STABILITY.md, "The F\* target encodes the wire shape it declares").** Three decisions, each
the kind a later reader reaches for again.

*The F\* target's refusal is for a COLLIDING KEY, and the four shape combinations are all modelled.*
The phase asked that "any combination the model cannot express" be refused by name. Laid out as
entries of one literal in the declared key order — fixed keys merged among the members under
`Sorted`, leading them under `Declared`; the flat node as one constructor per kind — every
combination of `NodeEnvelopeShape` and `KeyOrder` is expressible, so the refusal set is exactly the
documents the literal cannot carry: two entries under one key, which `find_field` reads as one. That
is a flat kind member named `id` or the discriminator or sharing an envelope member's key, a nested
envelope member named `kind`, a kind member named the discriminator under the nested shape, and a
union case member named the discriminator. `Declare.wireShapeErrors` refuses most of these at
declaration; the target refuses them again because it is handed an `Idl` value, not a declaration
that was checked, and a theorem over a silently de-duplicated object would be the "document nobody
sends" the target's header names as the thing its typed refusal exists to prevent. What is NOT
modelled is unchanged and stated in every header: a `TMap` is an association list in authored order,
where the interpreter Ordinal-sorts map entries at encode under both key orders.

*The classifier's rules are ONE table, and the document is rendered from it.* Seven parallel
matches over a 34-case union — `classify`, `consequences`, `sortKey`, `summarise` and three
predicates — plus a hand-copied document table had drifted in three rows, and Phase 252's two
corrections had nowhere to land once. `Diff.rules` is the single source: a `Change` case is joined
to its row by reflection on its case name at first use, a case with no row fails there rather than
defaulting, and `docs/idl-stability-classes.md`'s mapping table is a generated section a test holds
byte-equal to `Diff.mappingTable`. The alternative — a doc test that merely checks each label is
mentioned — is what allowed the drift; equality on the rendered rows is the only form that fails on
the next one. One consequence of reading the table against the EMITTER that emits the constructors
(not the type emitter, which emits none): a required-ness move and a default add/remove on a required
kind field are construction breaks, because `mk<Kind>`'s parameter list is exactly the required
fields without defaults. The context-free `consequences` cannot see a field's optionality (the
`Change` payloads are published shapes), so it reads a default as a required field's — the answer
that costs a rebuild rather than a surprise — and `classifyDiff` / `classifyArtifacts`, which hold
the snapshots, decide exactly.

*The `Codegen.fs` split ships WHOLE, under the facade rule — and the rule is what made it safe.*
The phase closed once with the split deferred and the operator re-opened it to finish, with no
successor: on a side whose posture forbids debt, a phase closes complete. The rule the split ships
under: every published type and entry point stays declared in the facade (`Gen.fs` — `KindProjection`,
`GenSupport`, and every function as a one-line forward), the emitters are `internal` modules under
`Emit/` in dependency order (`Core`, `Reach`, `Annotations`, `FSharpTypes`, `FSharpDefaults`,
`FSharpCodec`, `JsonSchema`, `TypeScript`, `Scaffold`), the baseline moves by ZERO lines for the split
(it did — the only lines that moved are the classifier's widening), `FStar.fs` consumes `Emit/Core.fs`,
and there is ONE type emitter: `FSharpTypes.typeGroup`, which the module renders and `Gen.fsharpTypes`
projects with no declared support. The one consequence a consumer sees is `fsharpTypes`' text, recorded
in STABILITY.md with its class. Two mechanics are worth writing down because the obvious alternatives
fail: the published `GenSupport` / `KindProjection` cannot be declared in a module the emitters compile
BEFORE (a module is declared once, and `Gen+GenSupport` is an IL name consumers construct), so the
emitters take mirror records (`Core.Support` / `Core.Projection`) and the facade converts field for
field; and the emitters' `private` markers were dropped wholesale because an `internal` module leaks
nothing — except where the marker sat inside GENERATED source (`let private encFloat …` in a
triple-quoted literal), which the generated-module drift guards caught on the first run and which is
why those guards exist.

*`support.json` is a classifier INPUT, and the widening is recorded as what it is.* A `Change` case
and a `Snapshot` member are published shapes; the operator accepted both and the CLI flag outside the
phase's declared files as recorded widenings, and STABILITY.md classes each honestly (a closed union
gaining a case is breaking for an exhaustive matcher; a record gaining a field is breaking for a full
literal). The alternative — folding support into an existing case such as `FieldHostSurfaceChanged` —
was rejected as a lie about what moved: a projection is not a field. Every support move is
host-surface on the wire; a projection or the type splice is a construction break on the F# axis
because they ARE generated declarations; a refine is the final expression of one decoder arm built
from binders already read, so it moves no declaration.

## 2026-10-01 — D95: `idl.json` is untrusted input at every loading path, and the generator splices IDL-authored text only through one escaper with a policy per target

**Recorded by Phase 292. `Fuaran.Core.Idl` (`Declare.errors`, `SourceLit`, `TypeParams`,
`Sample.trySampleNodes`) and `Fuaran.Core.Idl.Codegen`; rides the `0.34.0` draft (STABILITY.md, "A
vocabulary is validated data").**

*Decided: a vocabulary read from outside is DATA, and data is validated where it enters.* The IDL
tier's premise is that a vocabulary is a value a domain supplies, and `idl.json` is how it travels —
hand-edited, generated elsewhere, carried in a pull request. The engine checked nothing about it: the
three well-formedness checks it had (`wireShapeErrors`, `enumWireErrors`, `hostedWireErrors`) ran only
in tests, there was no referential check at all, and the source comment claiming the encoder modules
had "no injection surface at all" held only for a vocabulary somebody trusted. So every loading path
now refuses what `Declare.errors` names, naming every error rather than the first: `Artifact.ofJson`
(and so `Artifact.parse`), `Proposal.applyDelta` (over the vocabulary a delta produces), and the
`fuaran-core-idl classify` command, which reads both artifacts as vocabularies before it classifies.
The rules are the three old checks plus references (every enum, record and union name resolves, a
union is applied at its arity, a type variable is a parameter of the union declaring it), duplicates
(kind tags, op tags, one namespace for type names, case tags, type parameters, field names), defaults
of their slot's type (checked by the encoder, so "fits" means "encodes"), `HostOnly` is `TFn`, the two
second-pass rules (a transparent case cannot encode to an object, at its declaration or at an
instantiation; `id` and `kind` are reserved beside the nested kind body), and text: every name the
generators spell as an identifier is one (`[A-Za-z_][A-Za-z0-9_]*`), a category and a deprecation's
replacement, message and version are single-line, and nothing declared is ill-formed UTF-16.

*Decided: one escaper, `SourceLit`, with a policy per TARGET rather than per call site.* What "safe" means
is a property of where the text lands, not of the text: an F# string (and an F# attribute argument,
which is one), a TypeScript string (double- or single-quoted), an F* string, an F# `///` / `//` comment
line and a TypeScript `//` comment line, and a TypeScript object key. A literal escapes the delimiter
and the backslash, names `\n` `\r` `\t`, and writes every other C0 control, U+0085, U+2028 and U+2029 as
`\uXXXX`; its source text holds no line break, and its value is the authored string. A comment cannot
escape, so it is split at every line break into one comment line per authored line, with what no
comment can carry replaced by U+FFFD. An unpaired surrogate has no spelling in an F# or an F* literal —
measured, not assumed: the F# compiler reads `"\uD800"` as U+FFFD, and the pinned F* prover refuses it
as a syntax error — so those policies write U+FFFD and the backends refuse such a VALUE before it
reaches them; TypeScript spells it. Every policy is the identity on the text the generator always
emitted, which is why every committed generated fixture regenerates byte-identically.

*The two halves are deliberately both kept.* Validation alone leaves a vocabulary built in code — which
no loader sees — free to splice source; escaping alone leaves an identifier, which cannot be escaped,
free to be source text. So a name is held to the identifier grammar at declaration, and every other
splice of authored text goes through `SourceLit`; a source-reading test refuses the hand-rolled shapes
the removed splices had, and a vocabulary of hostile text built in code is emitted by every backend
with its payload never reaching source.

*Amended by Phase 387 (D124): the guarantee is scoped to IDL-AUTHORED text.* "A vocabulary built in
code cannot splice source" holds for every name, spelling and prose field the IDL authors. It does not
hold, and never did, for host source: a `THosted` slot's `FSharp` / `Encode` / `Decode`, a `TFn`
slot's `ClosureSig` and every `support.json` entry are spliced verbatim by design, validated for wire
form and shape only, and trusted as the compiling project's own code. D124 records the boundary and
the test that pins it.

*Not decided here.* Whether a declared name is a legal identifier IN EACH LANGUAGE beyond the lexical
grammar — an F# keyword as a case tag, a lower-case union case — is the emitter's compile question, not
an injection one; a field-less kind's `{ }` is the same class. Both are Phase 303's, which owns "the
emitter compiles what it emits".

## 2026-10-01 — D94: placement lowers to the skeleton ops it always needed, a tree lowers to its shell plus an insert script, and minting stays off the witness

**Recorded by Phase 312. `Fuaran.Core.Ops` (`Anchor`, `PlaceError`, `TreePlacement`, `Ops.lower` /
`skeletonRoot` / `shellOf`) and `Fuaran.Core.Tree` (`FreshIds`); rides the `0.34.0` draft
(STABILITY.md, "A placement algebra, tree lowering and fresh ids").** The shard asked for the
placement, lowering and minting helpers consumers had each re-derived, and for this record: that
placement lowers to skeleton ops, and that minting stays off the witness.

*Placement is a LOWERING, never an op.* Phase 95 removed the ordinal from `InsertChild` / `MoveNode`
because an index is a projection over one snapshot of a child list and an id is checkable; nothing
here reverses that. An `Anchor` is resolved against the tree the placement is computed over and
lowered to the ops that already exist — an append, plus a `ReorderChildren` naming every sibling id
when the append alone does not give the anchored order — so the op stream, the apply engine, the
footprint, inversion and arbitration see nothing new, and a script computed against a stale tree is
refused by `ReorderChildren`'s permutation check rather than silently landing somewhere else. The
two legs ride one `Batch` so the placement is atomic. `Anchor.Index` exists because a document
domain's ops are index-bearing; it means a position among the destination's children OTHER than the
node placed (`0 .. count`), which is exactly what those ops meant for an insert and for a move after
its removal, so they re-express as `TreePlacement.place` / `move` at `Anchor.Index` with the same
trees and the same `(parent, index, count)` refusal (`PlacementTests`).

*Within its own parent, a move is a reorder.* The obvious lowering — `MoveNode` then
`ReorderChildren`, which one host shipped — reaches the same tree, but `MoveNode` writes the node and
an UNKNOWN parent in its footprint (the pinned over-approximation), so it collides with every
concurrent structural write; `ReorderChildren` writes the one parent. A move to where the node already
is lowers to `[]`. The dry run is the engine's own (`canApplyContained`), first, so a placement the
engine would refuse carries the engine's envelope unchanged (`PlaceError.Refused`) and the anchor is
only resolved for an op that would be accepted.

*A tree lowers to its shell plus an insert script.* `Ops.lower` is the UI host's streaming lowering,
already generic over the witness: each child inserted as its own shell in preorder, so sibling order
is rebuilt by appends alone and a streamed and a batched emission are one script. No separate
container-aware lowering ships: every node that holds children is an insert's parent, so the same
script under `applyAllWith canHold` rebuilds every tree in the containment invariant and refuses one
outside it at its first offender. A leaf is never shelled (`shellOf` returns a childless node as it
is), because a witness may leave `ReplaceChildren` partial on nodes that cannot hold children.

*Minting stays off the witness (D5 kept).* `IdWitness` still carries no `fresh`. `FreshIds` mints over
`ToString` / `OfString` and a caller-supplied taken set of keys, deterministically, with no hidden
state — `sequential` re-derives its next number from the taken set rather than carrying the counter
one host's version kept, so a replay mints the same ids. A strategy is any `'Id -> Set<string> -> 'Id`;
the two shipped ones suit string-shaped ids, and a `Guid` or integer domain supplies its own and
certifies it with `Conformance.freshIdLaws`. `repairDuplicates` is one function for two uses: with an
empty taken set it renames a tree's later occurrences, with a target tree's keys it prepares a clone
or a paste.

*Named `TreePlacement`, not `Placement`.* The shard spelled the module `Placement`, lifted from a
host's module name. `Fuaran.Core.Placement` is already a public type in `Fuaran.Core.Function` (where a
capability's body runs); a module of the same name in another assembly would make `Placement.x`
resolve differently depending on which packages a consumer references and opens — a permanent
ambiguity on a public surface that a `ModuleSuffix` would only hide at the CLR level. The error type is
`PlaceError` for the same reason.

## 2026-10-01 — D93: the lane DAG's checkpoint is the linear snapshot's counterpart, a DAG may begin at one, and when to take one stays the domain's call

**Recorded by Phase 288. `Fuaran.Core.OpStream.Dag` (`Dag.Checkpoint`, `sealAt` / `checkpointAt` /
`checkpointFrom`, `verifyCheckpoint`, `replayFrom` / `replayFromWith`, `compactAt` / `compactFrom`,
`firstBreakFrom` / `verifyDagFrom`, `toJsonlWithCheckpoints` / `fromJsonlWithCheckpoints`) and
`Conformance.checkpointLaws`; rides the `0.34.0` draft (STABILITY.md, "A checkpoint on the lane DAG").**

*The counterpart, not a second design.* A checkpoint at node N is `{ Node; State; Hash }`, where `State`
is the fold of N's ancestor closure and `Hash` is the linear `Snapshot`'s strict seal of that state at
sequence zero, chained from N's content id the way a linear snapshot chains from its boundary record's
hash — read the other way, the strict snapshot at the start of the history that begins at N. The seal is
computed by `OpStream.Snapshots`' own verifier, so there is one pre-image, one verifier and one line
format: a checkpoint's sidecar line IS that snapshot's line, `{"snapshot":true,"seq":0,"state":<state>,
"prevHash":<node id>,"hash":<seal>}`. A sidecar reader refuses any other sequence, a chain-only line
(a checkpoint's seal always binds its state), and an empty `prevHash`. No linear-stream file changed.

*A DAG may begin at one — and the checkpoint travels BESIDE the DAG, not inside `Dag.T`.* The shard asked
for an optional origin field on `Dag.T<'Op>` "with a default". F# records have no field defaults: a
field added to `T` is a `record-widening` that stops every full record literal compiling, and downstream
consumers build `{ Nodes = … }` literals in their own suites; the checkpoint's state is also a `'State`,
which `T<'Op>` has no parameter for. So the origin rides beside the history, as a linear snapshot rides
beside its tail, and every function that reads a truncated history takes it as an argument:
`replayFrom w cp dag head` replays from it and `verifyDagFrom` verifies through it. `compactAt` KEEPS the
checkpoint's own node, which is what makes the compacted history live on with no new machinery: an
append onto the node is an ordinary `append`, an empty history above the checkpoint has the node as its
only head, the reachability index builds over the compacted DAG unchanged, and `checkpointFrom` /
`compactFrom` take the next checkpoint from the last. That answers the second-pass amendment (the
linear `compact` was terminal) without sharing a `Compacted` abstraction, which the DAG does not need:
its nodes carry no sequence numbers to continue, only parents, and the parent is still there.

*Verification through a checkpoint.* `firstBreakFrom` is `firstBreak` with the checkpoint's node as the
root whose content id the checkpoint carries: the seal recomputes, every node's content id recomputes,
nothing after the checkpoint's node names a parent the DAG does not hold, and every node is at, behind or
after the checkpoint's node. Behind it a missing parent IS the truncation. To keep "nothing after the
checkpoint is missing" checkable, `compactAt` keeps the BAND — every node behind the checkpoint on a path
from a "side parent" (a node behind the checkpoint that a node after it names as a parent, other than the
checkpoint's own node) to it; on the usual shape the band is empty. A truncated lane read WITHOUT its
sidecar is a `MissingParent` break under plain `verifyDag`, as it should be.

*The coverage condition — a premise of the shard that was false, and the refusal that replaces it.* The
shard's law said `replayFrom cp` agrees with the full replay "for every head reachable from the
checkpoint's node". On a DAG with a branch point BELOW the checkpoint that is not so: the drain folds a
head's closure smallest id first, so a branch that left before the checkpoint's node and merges after it
is folded BEFORE part of the checkpoint's own closure. A log witness under node ids it chooses shows it —
`0g`, `2a`, `3b` on one lane, `1c` off `0g`, merged by `4m`: the full replay is `0g1c2a3b4m`, while
resuming at `3b` and folding the rest gives `0g2a3b1c4m`. No state at the checkpoint can stand for that
prefix. So `replayFrom` refuses, by name, the first node above the checkpoint (in replay order) that does
not descend from the checkpoint's node (`CheckpointFault.Uncovered`), and `compactAt` refuses a DAG
holding a node neither behind nor after it. The condition is graph-only — a descent, not an id order — so
the same history is admitted or refused alike under every hash function. The law is restated to match:
for every head the checkpoint covers the answers agree, and every other head is refused with the reason
the graph predicts (`Conformance.checkpointLaws`, which a replay with the refusal removed reds).

*When to take a checkpoint stays the domain's call (GP6).* Core says what a checkpoint is, how it
chains, how a replay resumes from one and how a truncated history verifies through it; the domain
supplies the state codec and picks the node. `sealAt` is the genesis-import shape: a converted history
begins at an ordinary genesis node whose state was translated rather than folded, and the domain vouches
for that state once.

*What it does not claim.* The seal binds the state to the node; it does not re-fold the discarded
history, so a checkpoint is exactly as trustworthy as the act that sealed it — verify, then compact,
the linear compaction's rule. The replay is bounded in the ops it APPLIES; on a full history the graph
walks that find the delta are still linear unless the reachability index answers them
(`replayFromWith`). And whether `proofs/DagFold.fst` extends to a checkpointed origin is open: the
`proofs.json` rows are `tested`, and the question sits in the README's "not claimed" list.

## 2026-10-01 — D92: the lane DAG's reachability index is a drain order plus per-node ancestor bitsets — an additional way to ask, never a change to what the unindexed functions answer

**Recorded by Phase 289. `Fuaran.Core.OpStream.Dag` (`Dag.Reach`, `appendIndexed` / `mergeIndexed`,
`tryReplayToWith`, `reconcileManyWith`); rides the `0.34.0` draft (STABILITY.md, "A reachability index
on the lane DAG").** The shard left the representation to Core and asked for it, its measured cost on
the shapes lane stores actually hold (a few long lanes, a shallow fan of merges), and the boundary to
be recorded.

*Chosen: a slot per orderable node, the whole DAG's drain order, and a per-slot ancestor bitset over
slots.* The per-head functions drain a head's closure smallest ordinal id first. The drain of a
down-closed node set is the whole DAG's drain restricted to it — a node's readiness depends only on its
ancestors, and a node outside the set never unlocks one inside it — so one array of the whole drain
answers a head's order, a union of heads' order (`reconcileMany`'s region) and a branch delta's order by
filtering. A node's ancestors always hold smaller slots, so slot `s` needs `s + 1` bits: N²/64 32-bit
words over the index. Reachability is one bit test; a merge base is a scan of two bitsets' common bits
ranked by the stored closure size, the unindexed rule's key. Words are 32-bit `int`s so the arithmetic is
the same under Fable. *Rejected:* a closure `Set` per node (the shard's "simplest"), which holds the
same N² information as tree nodes of tens of bytes each rather than bits, and interval or chain labels,
which are smaller on a few long lanes but need a second structure to give the ORDER back, and order is
what replay, `between` and `reconcileMany` ask for.

*Measured* (`FUARAN_CORE_MEASURE_REACH`, the opt-in leg in `ReachTests.fs`; 5,000 nodes, 25 lanes, 200
merge points, built through `appendIndexed` / `mergeIndexed` under SHA-256; each loop asks up to 5,000
times and stops at a 20-second budget, and the count it reached is stated; the reconcile row uses a
footprint that writes nothing, so every call reaches the lane replays):

| question | through the index (calls, µs per call) | unindexed (calls in budget, µs per call) | fraction |
|---|---|---|---|
| `reaches` (a keyring walk's question) | 5,000 · 1.1 | 5,000 · 867.7 | 0.0013 |
| `ancestors` | 5,000 · 421.3 | 5,000 · 836.0 | 0.50 |
| `tryTopoOrder` | 5,000 · 78.3 | 3,415 · 5,857 | 0.013 |
| `mergeBase` of two lane heads | 5,000 · 97.7 | 42 · 476,746 | 0.0002 |
| `between` (a node, a lane head) | 5,000 · 493.5 | 967 · 20,686 | 0.024 |
| `tryReplayToWith` / `tryReplayTo` to a lane head | 5,000 · 716.6 | 973 · 20,559 | 0.035 |
| `reconcileManyWith` / `reconcileMany`, two lane heads over their base | 308 · 65,313 | 180 · 111,478 | 0.59 |
| `appendIndexed` / `append` then `Reach.ofDag` | 100 · 175 | 100 · 69,380 | 0.0025 |

Two rows are read, not just quoted. `ancestors` halves and no more: the answer is a `Set` of up to 5,000
strings and building it is most of the cost either way — the index removes the walk, not the answer.
`reconcileManyWith` saves the region partition (N+1 closure walks and a drain) and nothing else: the
pairwise interference check over the two exclusive deltas and the per-lane replays are the fold's own
cost, unchanged by design, and on two lanes of a few hundred ops each they are most of the call.

The index over 5,000 nodes held 1,572,512 bytes (393,128 words) of bitsets and `Reach.ofDag` took 80 ms
(18.8 MB allocated in passing). The bound is quadratic — about 156 MB at 50,000 nodes — and is stated
for the shapes measured, not as a general
claim; a store that grows past it is the trigger to revisit the representation (chain labels for the
membership half, the drain array kept for order), not to drop the index.

*The boundary, decided.* The index is an ADDITIONAL way to ask. Every unindexed function keeps its
signature and its answer — `reconcileMany` was re-expressed over a shared private fold with the same
partition — and `Conformance.reachLaws` pins every indexed answer equal to the unindexed one, the
extension law included. A node the drain cannot order (on or below a cycle, which only an unverified
load can hold) gets no slot, and any question touching one is answered by the unindexed function itself;
a node minted under an id some held node names as a dangling parent is not a leaf, so extending onto it
rebuilds. The index carries the DAG it was built for, so the overloads take it IN PLACE of the DAG and
there is no pair of arguments to mismatch. The extension is O(N) (arrays copied, bitsets shared) and its
law is observational: an extended index numbers its nodes in arrival order and a rebuilt one in drain
order, and they answer every question alike, so `Reach` carries no equality.

*Two names moved from the shard, both to match the function each equals.* `tryReplayToWith`, not
`replayToWith`: `replayTo` is obsolete and leaves after this draft. `Reach.tryTopoOrder`, not
`Reach.topoOrder`: the public unindexed function is `tryTopoOrder` and answers with a `Result`.
`mergeIndexed` was added beside the shard's `appendIndexed` because a session that merges would
otherwise rebuild at its first merge.

## 2026-10-01 — D91: a generated record holding a host-only closure takes WIRE equality; the conformance families keep their equality constraint

**Recorded by Phase 252. `Fuaran.Core.Idl.Codegen` (`Gen.fsharpModule`'s output); rides the `0.34.0`
draft (STABILITY.md, "The IDL serves a vocabulary that is not the UI's").** A field declared
`HostOnly` is a closure, so the generated record holding it has no structural equality, and neither
does anything that holds that record — the generated `Node` included. Every tree-algebra conformance
family (`witnessLaws`, `opAlgebra`, `diffLaws`) asks `'Node: equality`, so a vocabulary with one
host-only field could not certify its own generated layer without wrapping it in a type whose
equality is the equality of its encodings. The shard offered two remedies; this records the choice.

*Chosen: custom equality on the generated declaration, over its wire fields.* A host-only field is
never on the wire, so the record's wire-observable identity is exactly its other fields, and equality
over them is what a decode can tell apart — the wrapper a consumer otherwise writes by hand, generated
once. Giving the families an equality function instead would leave every generated layer without
`=` for every other use, would add a parameter to four families a domain with ordinary equality never
needs, and would put the burden on each caller to know which equality the laws mean.

*Its boundary, decided rather than left open.* A declaration qualifies when it holds a host-only field,
is not generic in `'Msg` (an `Equals` over a `'Msg`-carrying field needs a constraint the generated
layer cannot state, and the vocabularies that are `'Msg`-generic keep their shape untouched), and every
wire field is equality-capable — so a wire-visible closure (`TFn`) or a hosted host type, whose equality
the IDL cannot see, disqualifies it rather than emitting source that does not compile. Records, kind
specs and the node envelope are covered; a host-only field on a UNION case is not, because no
vocabulary declares one outside a `'Msg`-generic union, and a union's `Equals` must enumerate its cases
— built when the first such vocabulary exists, not before. The qualifying set is computed as a
fixpoint across the recursive type group, since one declaration's equality depends on another's.

## 2026-10-01 — D90: an integer token past 2^53 is read when it is a canonical float — the read side moves, not the layout

**Recorded by Phase 253. `Fuaran.Core.Wire`; ADDITIVE, riding the `0.34.0` draft (STABILITY.md, "`Json.parse`
reads every float `Canon.render` writes").** `Canon.canonicalFloat` wrote `1e16` as `10000000000000000`
and `9007199254740994.0` as `9007199254740994`, and `Json.parse` refused both: its int53 guard refuses
every integer token past 2^53. So a document Core wrote could not be read back by Core. The
canonical-float family stayed green because its sample never left |f| < 1e6. Measured: about a third of
the fixed-point layouts past 2^53 are not even the double's exact value — 1.8205257897171752e16 is
written `18205257897171750` — because the layout is the shortest digit string that round-trips, padded
with zeros to the decimal point.

Two repairs were open, one on each side of the wire.

*Render a float past 2^53 with an exponent* (`1E+16`). Rejected. WIRE_FORMAT §2 rule 5 makes the
fixed-point window (base-10 exponent -4 to 16) mandatory for every finite double across the whole range,
every conformant host emits it, and the cross-pipeline vector `canonicalFloat/e16` pins
`10000000000000000`. Changing it changes the bytes of every value holding such a float: a digest over
one moves, and Core's bytes part from every other host's until all of them follow. That is a breaking
wire change in this repository's own classification ("the same structure emitted as different bytes"),
made against the specification the encoder is held to.

*Read the integer token past 2^53 at parse.* Chosen, narrowly: **the token is read exactly when it is
the canonical layout (`FloatLayout.finite`) of the finite double it parses to**, as that `JFloat`. That
admits every float the encoder can write and nothing else. The guard's purpose was never the number
2^53; its message names it — the token "cannot round-trip without precision loss". A canonical layout
re-renders to itself, byte for byte, so no reading of it moves a digit of the wire. Every token that
does not — 2^53 + 1, a 19-digit identifier, `18205257897171752` (the exact value of a double whose
layout is `…750`) — is still refused, with the same message. The admission is by SPELLING, not by value,
because spelling is what survives a round trip and what a digest is taken over.

*What it costs, said plainly.* The guard used to promise that an accepted integer token's value is
int53-safe. It now promises that, or that the token is the canonical layout of a double — and that
double's exact value can differ from the token's digits. A producer that writes an identifier past 2^53
as a bare number, and whose digits happen to be a canonical float layout, has it read as a float.
An identifier in that range travels as a string; the guard keeps refusing every other spelling.

*Cross-host byte identity is untouched*: no emitted byte moves, so no host has anything to follow. The
read side was already stricter here than the UI wire format's own reader, which treats any number token
past ±(2^53−1) in an untyped position as a double; Core now reads the subset of those that a canonical
writer can produce. The proof models move with the parser (`JsonParse.fst`'s float reader gains the
`FCanonical` verdict and the int53 theorems are restated; `WireCanon.fst` gains the premise that the
floats outside its canonical subset are read back), and the canonical-float family samples the whole
double range with a parse-and-re-render law, so the class cannot return unseen.

## 2026-10-01 — D89: the kit's last three `…With` entries are reordered with no forward, and the six kit witness records are frozen

**Recorded by Phase 330. `Fuaran.Core.Conformance`; BREAKING, riding the `0.33.0` draft (STABILITY.md,
"The three remaining `…With` entries take the rule's order").** Two pieces of this draft were left
half-done by design, each with its decision deferred to a ruling. Both were ruled on 2026-10-01, before
the release, so the draft carries them rather than a later breaking cut.

*The naming rule, completed.* D78 stated the rule — `…With` is the same laws with a pinned parameter
injected, last before `seed` — and recorded three entries it could not yet bring under it:
`snapshotLawsWith` (its `StreamConfig` first), `concurrencyLawsWith` (its footprint projection first)
and `FoldConfluence.laneFoldLawsWith` (its `hashFn` third). An obsolete forward cannot keep an old
parameter order under the name the rule assigns, so the choices were to carry three exceptions into
`0.34.0` and beyond, or to reorder in place with no forward. **Ruled: the old positions are removed
outright.** The three are reordered in their modules and their facade forwards, and every call site in
this repository moved with them. A breaking change on a draft that is already breaking costs an adopter
one raise; three standing exceptions would cost every adopter, at every raise, the knowledge of which
entries break the rule. Every pinned parameter's type differs from each parameter it changed places
with, so no call in the old order compiles — the compiler names each one, which is what makes a move
without a forward safe to take. This closes D78's "reordered together, in one breaking change".

*The witness freeze, completed.* Phase 232 put the `1.0` field freeze behind `witnessSurfaceLaws` and
declared six public records outside it, as conformance-kit inputs each versioned with the family that
takes it: `CapabilitySeamWitness`, `QuerySeamWitness`, `CapabilityPipelineWitness`, `ConstructWitness`,
`KeyedWitness`, `EvaluatorWitness`. **Ruled: they are in.** Each is built by name at an adopter's
construction site exactly as a core witness is, so a field added to one breaks every adopter that runs
its family — the blast radius the freeze exists for. They are frozen with their fields as they stand,
in declaration order. `KeyedWitness` was held back for a second reason, recorded in D81: it was still
being widened inside this draft. Phase 286, which widened it, has shipped, and the open phase planned
to read it next (Phase 247, keyed ids in the footprint) adds to `Footprint`, not to the witness, so
the release that settles its shape is this one. This closes D81's "Not decided here".

`Conformance.unfrozenWitnesses` is left EMPTY rather than removed. The coverage law reads it unchanged,
so the classification stays two-way before `1.0`: a witness added later is frozen or declared outside
the freeze, with why, by the commit that adds it. Removing the list would have made "frozen" the only
classification and turned a future kit input into a forced freeze — a decision this ruling did not
take.

*Shown failing first.* Frozen with two of `KeyedWitness`'s fields swapped in its pin, the suite went red
naming the record and the reorder; the suite keeps a decoy with the same swap as a standing go-red.

## 2026-10-01 — D88: a pattern identifier that is not a case, and a rule that is never matched, are build errors

**`Directory.Build.props`; `Fuaran.Core.Conformance`. A behaviour correction riding the `0.33.0` draft
(STABILITY.md, "The conflict-shape renderer distinguishes the three shapes again").** Phase 296 made
`MergeConflictShape` `[<RequireQualifiedAccess>]`, and `FoldConfluence.shapeTag` went on matching
`ConcurrentUpdate`, `InsertPositionClash` and `MoveVsRemove` unqualified. With the cases out of scope
those names are VARIABLE patterns: the first arm bound every shape and rendered it as
`concurrent-update`, the other two arms could never be reached, and the canonical conflict report — the
rendering "halts identically" is judged by — could not tell an insert-position clash from a concurrent
update. The compiler said so on every build, as FS0049 on the first arm and FS0026 on the other two,
and the build was green. So the two codes join FS0025 in `WarningsAsErrors`, by number for D75's
reason: an uppercase identifier in a pattern that is not a case in scope is a silent catch-all, and a
rule never matched is dead code — almost always the arm such a catch-all swallowed. Neither is a style
finding; each is a wrong answer the type checker can already see. At the escalation the whole solution,
the extracted proof oracle included, raised neither code anywhere else, so no site needed rewriting and
the oracle needs no exception beside its FS0025 one. A deliberate catch-all is written `_` (or a
lowercase binder), which neither code flags.

## 2026-10-01 — D87: the verified append is the call-site check D83 left to the caller — an ordinary function a caller chooses, not a debug build, and it pays one replay per call

**Recorded by Phase 329. `Fuaran.Core.OpStream.Dag`, `Fuaran.Core.Conformance`; additive, riding the
`0.33.0` draft (STABILITY.md, "The DAG's checked append gains a verifying variant").**

*What D83 left to the caller.* D83 declined to make `Dag.append` apply the op, because a DAG holds no
state, and shipped `appendChecked` / `mergeChecked` taking the parent's state from the caller — exactly
as `OpStream.append` takes the stream's. The one assumption that leaves unverified is the PAIRING of
that state with the parent named. On a linear stream there is one place to append, so the caller's
state can only be stale; on a DAG the parent is chosen per call, so a caller can hand over the state of
a DIFFERENT node — fork from an older node while holding the latest head's state, append to one head
while holding another's, pass one side's state after a merge. The op is then judged at a state that
never existed at that point in the graph: a rejected op is admitted, or a valid one refused. The
reconcile and replay refusals recompute state from history and catch a wrongly admitted op later, on
whatever machine reconciles; nothing caught it where it was made.

*The ruling.* `Dag.appendVerified` and `Dag.mergeVerified` replay the named parent's ancestor closure
from a caller-supplied initial state — the genesis parent `""` replays to that state itself; a merge
replays the union of both parents' closures, without the merge op — compare the result with the
handed-in state, and refuse a difference with `StateMismatch`, carrying both states. The refusals are
ordered: the graph refusals first (nothing is replayed for a parent the DAG does not hold), then a
parent whose replay fails (`ParentReplay`, the fault `tryReplayTo` names), then the mismatch;
otherwise the answer is exactly the checked form's. The merge's union is drained in the order
`tryReplayTo` folds the merge node's own closure in, so an accepted merge returns that node's replay.
It claims nothing order-free: D80's refuted premise stands, and the verified merge agrees with
`tryReplayTo` by construction rather than with every interleaving of the branches. `tryReplayTo` is
now that union replay at one root — the same fold, shared rather than written twice.

*An ordinary function, not conditional compilation.* The variant was asked for as a debug aid, and it
ships as a public function a caller chooses in its tests and its own debug builds. Core packages ship
one build; a `#if DEBUG` path would exist for no consumer.

*Equality is supplied, not derived.* `StreamWitness` carries `Apply`, `Encode` and `Decode` — an
encoder of OPS, none of states — so the comparison comes from the state's type (`appendVerified`,
`mergeVerified`, constrained `'State : equality`) or from the caller (`appendVerifiedWith`,
`mergeVerifiedWith`), for a state with no structural equality or one whose structural equality is
finer than the domain's. D78's rule names the form: `…With` is the same function with a parameter
injected. The comparison comes FIRST, as the stream's `…With` members take their `StreamConfig`; D78's
"last before `seed`" is the position for a law family, and these have no seed.

*A new union, not a case.* `Dag.VerifiedAppendRejection<'State, 'Rej>` wraps `DagAppendRejection`
(`Checked`) beside `ParentReplay` and `StateMismatch`, so no existing union gains a case and every
exhaustive match over the checked forms' refusal still compiles. It is nested in `Dag` because it
carries `Dag.ReplayFault`.

*The cost, which is why it is a variant.* One replay of the parent's closure per call — what
`tryReplayTo` costs at that parent, linear in the closure plus the drain's ordering. A DAG of n nodes
built through the verified forms costs a replay per node, quadratic overall, which is the cost D83
declined to put on every append. The checked forms stay the production path.

*The law.* `dagLaws` gains two cells. On every drawn append (onto genesis and each of the four built
nodes) and the drawn merge (of the two fork heads), the verified form answers exactly as the checked
form when handed the parent's replayed state; handed another node's state — a fork's sibling, an older
node holding a later state, the merge holding one side's, the merge handed either head's — it refuses
with `StateMismatch` naming both. The refusal arm is reached only where two nodes' states differ, so it
is a strict cell: a generator whose nodes never differ in state starves it and the family reds as
never reached, which is the kit's rule (D78) rather than a new one. Run against a verifier whose
comparison was skipped, the family went red on exactly that cell.

## 2026-10-01 — D86: the algebra's remaining symmetries are built (`Schema.patch`, the pull, an index carried through an edit), and five omissions are recorded as deliberate so they are not filed again

**Recorded by Phase 317. Riding the `0.33.0` draft (STABILITY.md `0.33.0 — DRAFT`, "The remaining
algebra symmetries").** A closure pass over Core's public surface found the spine complete under its
advertised laws apart from three asymmetries, and five places where an operation is ABSENT on
purpose but nothing said so — which is how an omission gets re-proposed by the next pass that walks
the same surface. The three are built; the five are ruled here.

*`Schema.diff` gets its transform, and the delta records where the columns go.* A `SchemaDelta` was a
report: `Reordered : bool` said THAT the common columns moved and not where, and nothing recorded an
added column's position, so a host that keeps deltas beside `Schema.fingerprint` as provenance could
not replay one, and `patch old (diff old target) = target` could not be stated. The delta gains
`Order` — the target's column order, EMPTY exactly when the target's order is the one `patch` derives
without it (the survivors in old order, then the additions in listed order) — so `diff a a` is the
identity delta and an append-only change records nothing extra. `Reordered` is kept with its meaning
(it is now implied by `Order`) rather than replaced, because it is the one bit a compatibility check
reads. `Schema.patch` refuses, by name, a delta computed against another schema (`SchemaError`); it
never repairs one. The order is a sibling FIELD rather than a second delta type because the delta is
what `diff` returns: a consumer that wanted the transform would otherwise have to call two functions
that can disagree. The cost is honest and recorded: a record gaining a field breaks every full-literal
construction, so the class is `record-widening`, not the `additive` the shard proposed. `merge`,
`project` and `rename` over schemas stay the compute repository's (D66); only the inverse of `diff`
is the spine's.

*Propagation pulls as well as pushes.* `dirtyFromChangedIds` is the forward closure over the
dependents map; its dual, the backward closure over the dependency map itself, was expressible only
as the unnamed trick `dirtyFromChangedIds (dependents deps) targets`. `neededFor` names it, and both
now run ONE frontier loop handed the map in the direction wanted — the loop the model always had
(`grow`). `evalFor` / `evalForWith` evaluate the needed set and nothing else. GP6 ("owns no
evaluator") is not crossed: `evalFrom` is already a driver, and this is the same driver over a
smaller order. **The order is `sort` over the WHOLE map, restricted to the needed set** — not `sort`
over the restricted map — because walking one order is what makes agreement with `eval` a theorem
(`eval_for_agrees`, `proofs/Propagation.fst`) over a model that takes the order as a parameter and
assumes nothing about `sort`; deriving the order is cheap beside evaluation, and evaluation is what is
pulled. A target the map does not hold is needed and has no value, exactly as an absent id has none
under `eval`; it is not a new refusal, so `PropagationError` does not widen.

*An index is carried through an edit, and its stamp becomes a sum so it can be.* `Tree.Index.build` is
O(n), and so was the staleness stamp beside it: one FNV-1a digest over the whole preorder, which no
edit can update without re-walking the tree. So a carried index could never be cheaper than a rebuilt
one while the stamp was a sequence digest, whatever the rest of the index did. **The stamp is now the
sum, modulo 2^32, of one FNV-1a term per node** over the node's id, kind, child count and ordered
child ids. Given unique ids those records determine the tree (each node names its children), so the
sum still sees every skeleton edit, a kind change and an id-remap, and an edit re-stamps by
subtracting the terms of the nodes it changed and adding their successors'. Every stamp value moves;
a stamp persisted before this draft reads stale once and the index is rebuilt, which is the safe
direction. `Tree.Index.rebind` is the primitive (withdraw these nodes' entries, install those), and
`Ops.Index.afterOp` derives both lists from the op — every kind, a `Batch` folded through its steps
WITHOUT the intermediate trees, by tracking ids, parent links and child lists. It lives in `Ops`
because `SkeletonOp` does. **It verifies what it predicts** (the root's id, every re-indexed node found
in `post` where the tracking puts it, with the children the tracking says it has) and falls back to a
rebuild on any disagreement — an op `apply` refused, a tree from elsewhere, an index built under a
keyed traversal whose `UpdateNode` brings keyed children with it — so the law
`afterOp op post (build pre) ≡ build post` holds whatever it is handed and only the cost depends on
the contract. The cost, measured by the witness's child reads rather than a clock: the same edit
session costs the same over a tree forty times larger, and twice the session costs twice as much. A
law family in the kit for a domain's own witness was considered and not built: the guard already
falls back on any record the witness reports differently, and the one thing it trusts unchecked — that
a node the op did not touch comes back from `apply` with the content it had — is `ReplaceChildren`
round-tripping, which the witness laws `certify` runs already hold a domain to.

**The five omissions, ruled.**

1. **No `uncurry` / `decompose` on `Function`.** A bound function IS its closure: `Bind` substitutes an
   argument into the artifact and the result is an artifact, with no record of the binding kept
   apart from it, so there is nothing to take apart. `Function.signatureExcluding` is the projection
   a caller actually wants (the signature less the bound addresses); an inverse of `curry` would have
   to invent the binding history `Bind` deliberately does not keep.
2. **No seventh skeleton op.** `ReplaceSubtree`, `SetChildren` and `Swap` are each `Diff.toOps` over
   the subtree concerned — a computed, minimal script of the six ops — and an op that bundled one would
   add a case to every exhaustive match in every domain, a footprint rule, an inverse and a proof
   section, for a script a function already computes. Membership, order and content are separate ops
   on purpose (`SkeletonOp`'s own comment); a seventh would recombine them.
3. **`Ops.normalize` is not a canonical form.** It is a peephole that preserves the result of an
   applyable script and is idempotent — the two laws it is certified for — and nothing more: two
   scripts with the same effect need not normalise to the same script, because deciding that needs
   the tree, and `normalize` reads none. A canonical form, if one is ever wanted, is `Diff.toOps` over
   the before and after trees, which does read them.
4. **No ordinal on `InsertChild` / `MoveNode`.** Removed deliberately on 2026-07-26 and recorded only
   on the type: an index is a projection of a child list against one snapshot of it, silently wrong
   after any preceding or concurrent edit, where an id is checkable. Placement is
   `Batch [InsertChild …; ReorderChildren …]`.
5. **No `fresh` on the id witness.** The generic functions mint no ids — hygiene derives addresses
   deterministically, and replay and caching depend on that — so id minting stays the domain's
   (STABILITY.md "Stability-critical surfaces"; the identity axis is a parameter). The conformance
   generator takes `FreshNode` FROM the domain for the same reason.

## 2026-10-01 — D85: the light set — a dozen copied pieces become names; `appendAll` is `appendMany`, the capture key keeps its own cell encoding, and the nesting relation is a field

**Recorded by Phase 315, riding the `0.33.0` draft (STABILITY.md `0.33.0 — DRAFT`, "The light set").**
Downstream consumers had each re-implemented a handful of one-screen pieces because Core kept them
private, or exported the type without the operation. Each export below names the copies that evidence
it; each is held to the copy it replaces on a shared fixture (`LightSetTests`, and the `ParityVectors`
rows for the ones a browser computes too).

| Export | What it replaces |
|---|---|
| `OpStream.sha256Hash : HashFn` | `fun prev payload -> sha256Hex (prev + "\|" + payload)`, hand-written by four consumers (the UI host's stream entries among them) |
| `Hash.fnv1a32 : string -> uint32` | the raw FNV value, recovered by copying `mul32` in seven consumers (the UI renderer among them) |
| `OpStream.chainHashOf`, `OpStream.appendChainOnly` | four domain adapters that append without the state and rebuild the canonical payload and hash by hand |
| `Footprint.empty` / `union` / `contentEdit` / `insertUnder` / `removeNode` / `moveTo` | the private builders (`Ops.fs`), rebuilt by three consumers whose op vocabulary is not `SkeletonOp` |
| `Rejection.code`, `Rejection.explain`, `RejectionCodec` | the six-case explainer the UI host and two domains each wrote |
| `FloatLayout` (public) | the UI host's "keep in sync" copy of the layout Core ported from it, kept for SVG |
| `Cell.token`, `Cell.compare` | four spellings of a cell's token (the `CountDistinct` key, the `unique` key, the compute layer's `cellToken`, the capture key's cell fields), one of which had drifted at `Decimal` |
| `Validator.Pack` / `runPack` / `PackFinding` | the versioned pack container two domains hand-roll over `PackRule`, a third planned |
| `Actor.validate` / `human` / `agent`, `ActorInvalid` | a consumer's workaround for an actor with an empty id |

*`sha256Hash` is a copy, by D2.* `OpStream` references nothing, so the digest it names is its own copy
of `Hash.utf8Bytes` + `Hash.sha256HexOfBytes`, held value-identical by the `sha256Hash/*` parity rows
(both pipelines) and by the suite against the platform digest. Over an ill-formed string it answers the
platform's replacement, as `Hash.sha256Hex` does: a `HashFn` is total and cannot refuse, so this is
D84's unguarded posture — platform parity, not injectivity — stated on the function.

*`appendAll` is not added.* The shard asked for a batch append that stops at the first refusal and
names its index; Phase 296 shipped exactly that as `appendMany` / `appendManyWith`, with the signature
the consumer copies have. A second name for one function is the drift this phase exists to remove, so
the acceptance law — the batch refuses at the index `Ops.applyAll` refuses at — is stated over
`appendMany`.

*The capture key does not adopt `Cell.token`.* `Query.invocationKey`'s pre-image is INJECTIVE on cells
(`cell_fields_injective` in `proofs/Query.fst`): a decimal is its text as it stands, a float its
canonical layout. `Cell.token` is an EQUALITY-CLASS key — NaN one value, `1.50` and `1.5` one value —
because that is what a `Distinct` or a `unique` key asks. They answer different questions; folding one
into the other would either break the proved property or move every persisted capture key. So "one
spelling" holds for the three identity keys (`CountDistinct`, `ColumnValidator.unique`, the compute
layer's `cellToken`, which matches `Cell.token` on every non-decimal cell and adopts it at its next
raise) and the capture key keeps its own. `Cell.compare` is the family-wise ORDER the aggregates use
(NaN last, `-0 = 0`, decimals exact), `None` across families: an order, not an identity.

*The nesting relation is a field, and that is the one breaking item.* `WouldNestUnderSelf` conflated a
move under itself with a move into its own subtree. The payload cannot say which (it carries the
target, not the new parent), so telling them apart needs either a field or a second case. A second case
for the descendant move would change which case existing code receives for a move it already handles,
and split one class that the apply vectors and the proof model pin as one. The field keeps the class
and changes the payload: `WouldNestUnderSelf of target * relation: NestRelation`, breaking-source for
a one-field pattern, in a draft that is already breaking. The reorder on a leaf needs no new case — a
`ReorderMismatch` whose `expected` is empty already says the parent has no children — so it is a code
(`reorderOnLeaf`), derived from the payload.

*`RejectionGuidance` moves down without an alias.* `Rejection.explain` returns it, so it is declared in
`Fuaran.Core.Ops` now. The namespace is unchanged, so every source that names it still compiles; an
alias of the same full name in `Fuaran.Core.AiSurface` would be a cyclic abbreviation, not a
forward. The move is binary-breaking for the AiSurface assembly and source-compatible. The canonical
encoder is `RejectionCodec` in AiSurface, because `Fuaran.Core.Ops` carries no wire; it writes the
whole envelope, and the apply vectors keep their narrower class-and-address projection.

*An actor names somebody — at the constructors and on read.* A union case cannot refuse its arguments
and closing the cases would break every construction site, so the refusal is `Actor.validate` and the
two validating constructors, and the JSONL actor decoder refuses an empty id
(`JsonlFaultReason.ActorInvalid`). The legacy bare-string reader stays lenient: it is a migration
path, and refusing there would strand a stream it is the only way to read.

*The pack is generic over its subject.* A tree family is one instance (`fun root -> family.Run w
root`); a document, a model or a voicing sequence is another. A finding carries the `PackRule` and the
citation `<pack>@<version>/<ruleId>` beside an unchanged `Defect` — the `Family` field on `Defect`
itself is Phase 298's, for the next breaking draft. `PackRule` is kept: this is its use.

*The laws are suite tests.* Each is a property of Core's own functions over no domain witness — the
union law (`independent (a ∪ c) b ⇔ independent a b ∧ independent c b`, so growing a footprint never
frees a pair), the builders against `Ops.footprint`'s clauses, the stateless chain against `append`'s
— so they are `LightSetTests` rather than kit families a domain runs.

*What the acceptance could not have.* "Each named consumer copy is byte-equal to the Core export"
holds for the chain hash, the FNV value, the float layout and the cell token, each tested against a
reproduction of the copy. It cannot hold for the rejection explainer: the copies disagree with each
other in wording, each speaking its own nouns. `Rejection.explain` takes the nouns as a parameter and
is pinned, case by case, on its own fixture; a consumer adopts it by choosing its nouns, not by
matching its old sentences.

## 2026-10-01 — D84: the renderers do not recurse, an ill-formed string is refused wherever a digest depends on it, a float aggregate names its overflow, the profile grammar is its canonical strings, and the §21 limits are an open question

**Recorded by Phase 306. BREAKING, riding the `0.33.0` draft (STABILITY.md `0.33.0 — DRAFT`,
"Totality against the machine in Wire and Column").** Each ruling below is about the machine the
code runs on rather than the model it is proved over: a thread's stack, the float range, the UTF-16
units that are not characters, the strings a reader took that its writer never wrote. A proof over
the model cannot see any of them, which is how each survived a directory of proofs.

*One iterative writer, and the renderers RENDER rather than refuse.* `Json.render`, `Canon.render`,
`Canon.renderOrdered` and both `tryRender`s were each a recursive function mapping itself over a
list, so their stack depth was the value's nesting depth: a constructed value about 1,400 deep killed
the process on a 1 MB thread, and a stack overflow is uncatchable on .NET. The parse cap protected
only the read side, and `render_total` in `proofs/WireCanon.fst` rests on F\*'s `Tot`, which says the
function is defined everywhere and says nothing about the stack. One writer (`Json.writeWith`) now
stands behind all of them, with its pending work on an explicit stack in the heap, and the two scans
the guarded renderers refuse on are iterative too. The phase offered a choice — render at any depth,
or have `tryRender` refuse past `defaultMaxDepth` — and the first is taken: a value built in memory is
not untrusted wire data, depth is not a fault in it, and the text of one nested past the cap is
refused BY NAME on the read side (`MaxDepthExceeded`), which is the cap doing its job. The bytes of
every renderer are unchanged.

*Refusal over replacement, wherever a digest depends on the string.* A UTF-16 string may hold a
surrogate with no partner; such a unit has no code point and UTF-8 has no encoding for it, so every
encoder substitutes. Until Phase 290 `Hash.utf8Bytes` read the next unit as the low half unchecked
(over all 66,060,288 (high surrogate, non-low unit) pairs, 97.6% encoded byte-identically to a
well-formed astral character); since Phase 290 it writes the replacement character, as the platform
does, and then `"\uD800"`, `"\uDFFF"` and `"�"` are one byte string. Replacement only changes
WHICH strings collide. Injectivity needs the refusal, and it is now made at each of the three places a
digest pre-image is built: the parser (Phase 299, on read), the guarded renderers (`Canon.tryRender`
and `Json.tryRender`, which name the first such string by path, a member's key before its value), and
the guarded encoder (`Hash.tryUtf8Bytes` / `Hash.trySha256Hex`, which name the unit and its index).
The unguarded forms are unchanged and say what they are: platform parity, not injectivity.
`Json.tryRender` is included though the phase names only the canonical one, because its contract is
that an `Ok` is text `parse` reads back, and a lone surrogate broke that the same way a NaN did. A
non-finite float is looked for first in both, so every refusal either already made keeps its message.

*What that buys is a theorem, and the bridge to the shipped encoder is an independent oracle.*
`proofs/Utf8.fst` models `Hash.utf8Bytes` and its guard and proves the encoding injective on
well-formed strings; `proofs/WireCanon.fst` section 16 carries canonical injectivity through it — two
canonical values `Canon.tryRender` accepted, whose renderings have the same UTF-8 bytes, have the same
normal form — and `proofs/JsonParse.fst` section 12 proves every string and member key the parser
returns is well-formed, so the theorem's hypothesis holds of everything the parser admits. `Utf8.fst`
is checked and NOT extracted (its units are integers, which do not survive the extraction), so what
stands beside `Hash.utf8Bytes` in the suite is `System.Text.Encoding.UTF8` — every code unit, every
high surrogate against the units that bound the low range, and a seeded sample — the first
independent-oracle differential in the proof leg. SHA-256's collision resistance stays a premise.

*A lone surrogate written as a `\u` escape in an F# string literal is not a lone surrogate.* The F#
compiler replaces an unpaired surrogate escape in a literal with U+FFFD. So Phase 290's ill-formed
rows — in `HashTests` and in `ParityVectors`, the `utf8Bytes/ill-formed-*` family — were, on .NET,
well-formed strings of replacement characters: they encoded to the same `EF BF BD` a correct encoder
gives a surrogate, and passed without ever handing the encoder an ill-formed unit. Both are rebuilt
from `char` values; the expected bytes did not move, which is how it went unseen. The rule for this
repository: an unpaired surrogate in a test or a vector is BUILT, never written.

*The float aggregates: the plain formula first, and a form that cannot overflow where it did.*
`Median [1e308; 1e308]`, `Mean [1.7e308; 1.7e308; -1.7e308]` and `StdDev [1e200; -1e200]` each
overflowed an intermediate though the answer is representable. The phase named Welford's recurrence
with scaling; its later amendment required that the one-pass rewrite answer every aggregate to the
bit. Both are kept, in this order: each aggregate is computed by the formula it always used, in the
fold order it always used, and ONLY where that leaves the float range over finite input is it
recomputed — `Median` as `a/2 + b/2`, `Mean` and `StdDev` by a Welford recurrence over values scaled
by a power of two until the largest magnitude is at most one. Welford throughout was DECLINED: it
moves every `StdDev` in the last place, against the amendment, to repair cases the fallback repairs
without moving anything. A float `Sum` has no second form — the running total is the answer — so a
total that leaves the range over finite input is `AggregateOverflow`, as an int `Sum` past int32
always was. A column that itself holds a NaN or an infinity is not an overflow. `StdDev` is the
POPULATION form (it divides by `n`) and says so; a sample form is NOT added, because a new `AggFn`
case is a `union-widening` for every exhaustive match downstream and nothing has asked for one.
Noted for the streaming accumulators the compute repository is to hold to this function: `Mean`
streams as a sum and a count and is this function to the bit; the plain `StdDev` needs the mean
before the deviations, so a one-pass streamed `StdDev` is not, and that phase chooses between keeping
the numbers and a tolerance.

*One pass, with the refusals in the order they had.* `aggregate` built three intermediate lists
(admitted cells, present cells, numbers). It folds once now, into the one accumulator its aggregate
needs. The refusal precedence is preserved exactly: a cell outside its column's type anywhere in the
column, then a non-numeric column, then the first decimal past the float range. The suite carries the
pre-change function verbatim and compares the two over a generated pool, by the bits of each float.

*The profile grammar is its canonical strings.* `name "@" number "." number`, where a name is an ASCII
letter followed by ASCII letters, digits, `.`, `_` and `-`, and a number is `0` or a non-zero digit
followed by digits, at most `Int32.MaxValue`. `Profile.tryParse` accepts exactly what
`Profile.render` writes over a valid profile; it read `core@01.0`, `core@+1.0`, a version followed by
NUL characters and any name at all before. The name alphabet is a choice, made narrow on purpose: it
covers `core` and every profile this repository's suite and corpus carry, and a name with a space, an
`@` or a control character in it had no reader that agreed with its writer. The record is public, so a profile outside the grammar can
still be built by hand: `Profile.tryRender` (and `Versioning.tryRender` for an envelope) is the
guarded form, and `render` documents that it assumes a valid one — the `encode` / `tryEncode`
convention the codecs already follow. `Json.readInt32` holds its token to a sign and digits before
the host reader sees it, because that reader has always tolerated trailing NUL characters.

*`bump` saturates and `tryBump` refuses.* A counter at `Int32.MaxValue` has no successor;
`baseProfile.Minor + 1` there wrapped NEGATIVE. `tryBump` names the counter; `bump` returns the
profile unchanged for a caller with no error channel. `proofs/WireVersioning.fst` modelled the
counters as naturals, where `+ 1` is total, so every theorem about a bump was true of the model and
false of the code at one value; the limit is in the model now, each theorem that needs the bump to
have happened says so, and `saturated_breaking_bump_is_not_foreign` exhibits what saturation costs. The one
caller in this repository, `Diff.bumpProfile`, keeps the saturating form: a vocabulary 2^31 additive
revisions old is not a case its report needs an arm for, and a caller that must know has `tryBump`.

*Surplus members of a column source are must-ignore — stated, not changed.* A member of the source
other than `schema`, `columns` and `ref`, and under an explicit schema a member of `columns` the
schema does not name, is read past. Refusing was the alternative and is DECLINED: it is the wire's
forward-compatibility rule, a host wraps the source in its own discriminator, and the table that
results is the schema's and is checked by `validate`. The asymmetry is recorded rather than hidden —
`validate` refuses the TABLE a surplus column would have meant, and the codec's model proves both
halves (`decode_drops_a_column_outside_the_schema`, `validate_refuses_a_column_outside_the_schema`).

*`Json.render (JFloat -0.0)` stays `-0`.* It re-parses as `JInt 0`, so the first author-ordered
render of a negative zero is not a fixed point of parse-then-render and every later one is. Making it
`0` would move bytes that chain pre-images are built from; it is documented as the one divergence from
`Canon.render` that a parse collapses, and pinned by a test.

*`Hash.fnv1a`'s unit is the UTF-16 code unit.* It always was; nothing said so. A twin that folds
UTF-8 bytes or code points agrees on ASCII and on nothing else. The `fnv1a/astral-code-units` parity
vector pins the shortest input on which the three readings differ.

*The codec's first model, and what it found.* `proofs/WireColumn.fst` models `Table.validate` and
`ColumnCodec` at the `JVal` and retires `Fuaran.Core.Column`'s coverage exclusion. Its theorem is the
round trip UP TO A NORMAL FORM — columns in schema order, an `Int` widened into its float or decimal
column — because the literal `decode (encode t) = Ok t` is false of the shipped codec three ways, each
proved as a refutation. Two of its findings were about the code and are settled here. First,
`Table.validate` checks that the schema's names and the columns' names are the same SET and never that
they are in the same order, where the type's doc said column order follows the schema. The code is
kept and the doc is corrected: the schema is the order authority, the encoder looks each column up by
name, the decoder returns schema order, and a table built in another order encodes correctly. Making
`validate` refuse one is DECLINED — it would refuse tables that encode and decode correctly today, to
make a doc sentence true that the round-trip theorem states more exactly. Second, the suite's
generative codec law asserts the literal round trip and passes only because its generator builds
tables already in normal form; the model's differential now samples OUTSIDE the normal form — an
`Int` in a float and in a decimal column, columns out of schema order — and holds production's
`decode (encode t)` to the model's `normal_table`, with the literal comparison as its go-red.

*QUESTION (open, for a ruling): the WIRE_FORMAT §21 limits.* The parser admits documents §21 says a
conformant host refuses: nesting past 256 (`Json.defaultMaxDepth` is 512), a string past 2^20 code
points, an array past 100,000 items. Two answers are available and this phase takes neither.
(a) ENFORCE IN CORE: lower the default cap to 256 and add the two size refusals — breaking for a
consumer whose documents sit between the limits, and it makes Core's parser a §21 host. (b) STATE
THAT CORE IS NOT A §21 HOST: the spine's parser is a substrate with a nesting cap of its own for the
stack's sake, the limits are the conforming host's to enforce, and `proofs/Limits.fst` keeps them as
named premises. What argues for (a) is that a limit two hosts enforce and a third does not is a
document one of them cannot read; what argues for (b) is that Core's parser reads op streams and
store files that are not §21 documents at all. Until it is ruled, nothing here claims §21 conformance
for `Json.parse`.

**RULED (operator, 2026-10-01): (b). Core is not a §21 host.** Read against the section itself, the
question was framed too narrowly. §21 is the UI wire format's: it sets EIGHT limits, five of them
over things Core's parser has no notion of (node depth, total nodes, document bytes, expression
nodes, skeleton rows); it requires the refusal to be `LIMIT_EXCEEDED` in that format's own error
envelope and forbids reporting it as malformed JSON; and its conformance section names the five
wire-format hosts, each of which enforces every limit at its own decoder. Core is the substrate
beneath them and was never the layer the obligation is about. Its nesting cap (512) is its own stack
guard, deliberately looser than §21's 256 — so Core never refuses a document §21 says a host MUST
accept — and its refusal there is its own `MaxDepthExceeded`. `proofs/Limits.fst` keeps the limits as
named premises for the models that reason "within the format's limits"; nothing in Core enforces them,
and nothing in Core claims §21 conformance. No shared limits-taking entry point is added: the hosts
already carry the enforcement, in the units and with the error the section requires.

*The JSONL scanner reads its integers from the digits.* Phase 296 landed its one scanner while this
phase was in flight, and its integer reader called `System.Int64.Parse` under the current culture —
which THROWS on `-5` under a culture whose negative sign is not U+002D, out of a function that returns
a `Result`. `OpStream` may not reference `Wire`, so it cannot call `Json.readInt32`; it folds the
digits its own grammar test has already accepted, which is culture-free by construction.

*Not done here, and where it goes.* The shared wire corpus takes its copy of `conformance/refusals/` — which gains the profile grammar's
vectors — at the hosts' next pin raise. The Fable half of the new parity rows is measured at the cut.

## 2026-10-01 — D83: one JSONL scanner, shared rather than copied; a colliding node is refused under the default hash; the snapshot mode is a value on the snapshot; and `append` does not apply the op

**Recorded by Phase 296. `Fuaran.Core.OpStream`, `Fuaran.Core.OpStream.Dag`, `Fuaran.Core.Conformance`;
rides the `0.33.0` draft (STABILITY.md "One JSONL scanner that refuses…").**

*D2 does not forbid the shared scanner.* The DAG package carried a verbatim copy of the linear
package's JSONL scanner and actor decoder, under a comment that the copy kept the DAG's
"no-`Core.Wire` posture (D2)". D2 is a rule about the WIRE dependency: the op-stream packages stay
FSharp.Core-only and Fable-clean, so they take no reference on `Fuaran.Core.Wire`. The scanner is not
`Wire` — it is the linear package's own code — and the DAG package has referenced the linear package
by project since it was cut. Sharing it adds no dependency edge, and the copy's cost was already being
paid: every scanner fix since Phase 45 was applied twice, and Phase 260 had to change the actor
decoder in two places. The scanner is PUBLIC (`OpStream.Jsonl`) rather than `internal` behind an
`InternalsVisibleTo`, because the reason to expose it outlives the DAG: a consumer that embeds an
opaque canonical payload in a line — the UI tier's envelope readers are the measured case — hand-scans
for exactly the raw member span `topFields` / `rawSpan` return, and a third copy is the defect this
entry closes. The scanner refuses at the levels it reads (structure, string escapes, bare literals,
and the KIND of a member through the typed accessors); the interior grammar of an array or object
member stays the business of whoever decodes the raw span, which is what a raw span is for.

*The collision refusal, under the default hash.* `Dag.append`, `Dag.merge` and `Dag.fromJsonl` did
`Map.add id node`: a second node minting an id the DAG held REPLACED the first, silently, and
`verifyDag` still passed because the survivor's id was its own content hash. Under the 32-bit FNV-1a
default that is not a theoretical event — about 0.3% at 5,000 nodes and even odds near 77,000 — so it
is refused (`DagAppendFault.ContentIdCollision`) and the node held first stays. The comparison is over
the node, not the id: parents modulo order (the content id is parent-order-independent since Phase
64.1), the actor, and the op through its encoding (no equality is demanded of `'Op`). Comparing the
parents is what makes the refusal catch the splice the second-pass review named — a `merge(x, y)` id
equals the id of a node whose one parent is the comma-spliced `"x,y"` — which no colliding `HashFn`
test alone would pin, because that collision needs no hash weakness at all. An IDENTICAL node is one
node: content addressing converges by design, so the same op by the same actor on the same parent
deduplicates and returns `Ok`, and a caller that must count appends keys them itself. A host that
cannot accept FNV-1a's collision rate swaps the `HashFn`; the refusal makes the rate visible rather
than silent, it does not lower it.

*The snapshot mode is a value on the snapshot.* Seventeen members formed a strict / chain-only ×
canonical / config matrix, and the mode lived in which member a caller had called — so a reader
re-parsed the line to pick a verifier. `SnapshotMode` (`Strict` | `ChainOnly`) is now a field of
`Snapshot<'State>`, and `OpStream.Snapshots` is one family taking the mode once (`take`, `compact`) and
reading it from the snapshot thereafter (`firstBreak`, `verify`, `toJsonl`, `ofJsonl`). The state
encoder is always the state's JSON and, under `Strict` only, also its hash pre-image; the family never
calls it for a chain-only hash. This is the shape Phase 288's checkpoint builds on: one pre-image per
mode, one verifier, one line, and a typed `SnapshotFault` / `SnapshotBreak` rather than strings. The
matrix stays as obsolete forwards for this draft, each pinning the mode its name says, so a strict
verifier handed a chain-only snapshot still refuses it — "answers as before" is held, not
approximated. The forwards keep the `OpStream.snapshotAt:` error prefix the shard asked to correct,
because the proof model of compaction (`proofs/Chain.fst` and the oracle extracted from it) pins those
exact strings and the oracle suite compares them; the typed fault is the correction, and the strings
retire with the forwards.

*Declined: `Dag.append` applying the op.* The shard asked `append` and `merge` to apply the op through
the witness, so a rejected op could not enter the DAG. A DAG holds no state: whether an op applies
depends on the state its parent's closure replays to, which the DAG can only supply by replaying it
(linear per append, quadratic per built DAG) or by trusting a state the caller hands in. And the
refusal it would buy is already the reconcile's: Phase 300's `reconcileMany` refuses a lane that does
not apply on its own, because a DAG also arrives from a file or another writer, where no append ran.
Making the plain forms apply would have turned every lane generator in the conformance kit that draws
a rejecting op into a build failure, changing what `laneFoldLaws` measures. So the plain forms stay
graph operations with typed graph refusals, and `appendChecked` / `mergeChecked` take the parent's
state — exactly as `OpStream.append` takes the stream's — and refuse a rejected op before it enters
(`DagAppendRejection.Domain`). The acceptance's "append of an op the witness rejects is a typed
refusal" is met there; `dagLaws` asserts that the check agrees with `Apply` on every drawn op.

*Refuted: an O(1) `append` behind the unchanged list.* The shard asked `append`, `appendIf`,
`appendIdempotent` and `captureEffect` to be O(1) per call with the public `OpRecord list` unchanged.
An immutable list reaches its end only by walking it, and putting a record there costs a copy, so no
representation behind that projection makes one call constant. Measured on a 5,000-record loop
(Release, median of five, identical chain bytes in every arm): the pre-296 body 185–280 ms, the
one-walk `append` 296 ms run first and ~600 ms run after the other arms — the copy and its garbage
dominate, and the walks saved are inside the noise — and `appendMany` over the same ops 12.3 ms, about
5% of the loop. The shard's alternative, a batch form, is what ships; a caller appending in a loop
collects and calls `appendMany`.

*The file split.* `OpStream.fs` is split by concern — `Actor.fs`, `Chain.fs`, `Snapshot.fs`,
`Capture.fs`, `Attributed.fs`, `Jsonl.fs` carry the namespace-level types and their companion modules,
and `OpStream.fs` the `OpStream` module — with the API baseline byte-identical across the split (the
public-surface family is the check). The module's own members stay in one file: splitting them behind
a forwarding facade would restate some seventy signatures and their documentation a second time for
no change a consumer can see, and is not done here.

## 2026-09-30 — D79: the parser holds to the JSON grammar, NaN sorts last, the column codec carries only a table it can decode, and `RowCodec` is obsoleted

**Recorded by Phase 299. BREAKING, riding the `0.33.0` draft (STABILITY.md `0.33.0 — DRAFT`, "Column
trusts nothing it is handed").** The column strand took what it was handed at its word — a cell by its
shape, a table by its column list, a decimal by its text, a date by nothing at all — and the parser
under it read tokens the JSON grammar does not have. Each ruling below is the refusal a host twin
must make too; `conformance/refusals/` carries them as vectors.

*The JSON number grammar, exactly — scanned as before, then held to the grammar.* `Json.isJsonNumber`
is `-? (0 | [1-9][0-9]*) (\.[0-9]+)? ([eE][+-]?[0-9]+)?` and nothing else, and `parseNumber` checks
every token against it before reading one: `01`, `1.`, `-.5`, `1.e5`, `1e` are `MalformedNumber`.
The token is still SCANNED the way it always was (an optional sign, digits, point, digits,
exponent), and the refusal is `malformed number: <tok>` at the token's end. That is deliberate:
an input the old parser already refused as `malformed number: <tok>` keeps its kind, its position
and its message, so the behaviour change is the set of tokens now refused, and the parser differential against the
extracted model (`proofs/JsonParse.fst`) disagrees on exactly those. A scanner that stopped at the
first grammar fault would have moved the position and message of inputs nobody meant to touch.

*One integer reader, the invariant one.* `Json.readInt32` is `Int32.TryParse(tok,
NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)`, and `parseNumber` and
`Versioning.Profile.tryParse` both read through it. The bare `Int32.TryParse tok` read under the
current culture: under fa-IR and he-IL `-5` failed it, fell to the float path and parsed as
`JFloat -5.0`, so one document decoded differently by server locale while its bytes and digests
agreed. `NumberStyles.None`, as the phase first stated it, would have refused the sign itself and sent
every negative integer to the float path on every host — the amendment's correction is what shipped.

*A lone or ill-ordered surrogate is refused, as `BadEscape`, raw or escaped.* A string is well-formed
UTF-16 or it is not a string of characters: a surrogate with no partner has no code point, UTF-8 has
no encoding for it, and the platform encoders each substitute their own replacement, so a digest over
it cannot mean one thing on every host (Phase 290's injectivity needs the refusal; Phase 306 states the
UTF-8 theorem over it). The kind is the existing `BadEscape` — "this string's content is not
well-formed" — rather than a new `JsonErrorKind` every exhaustive match would have to learn; the
message says which half is missing. `Corpus.fuzzRoundTrip` now draws every control character and
every surrogate class through BOTH renderers, and its law for an ill-formed draw is the refusal.

*NaN sorts last, and `-0` equals `0` — spelled out, never delegated.* F# generic comparison put NaN
below every value on .NET and above every value under Fable, so `Min`, `Max` and `Median` over a
column holding a NaN answered by host. The column layer's order (`compareFloat`) is IEEE order on the
non-NaN values, `-0 = 0`, and NaN one value above +∞; its token (`floatToken`, the distinct token's
float half) is equal exactly where the order ties, and the non-finite spelling is
`JVal.nonFiniteToken`'s, the one spelling every surface now reads. Last rather than first because a
NaN in a measurement is an absence of a comparable value, and `Min` answering it would hide every real
value. The `aggregate/nan-order` parity vector pins the answers on both pipelines.

*`Table.validate` is the table the codec can carry.* It now refuses a duplicate schema or column name,
a present cell whose type does not widen into its column's (`ColumnType.widens` — the rule the schema
lattice already pinned, so the codec and the lattice agree by construction), a non-finite float, and
decimal, date or timestamp text that is not canonical (`TemporalText`: `YYYY-MM-DD` and
`YYYY-MM-DDThh:mm:ssZ`, calendar-checked), and names a ragged table `RaggedColumns`, apart from the
wire's values/validity `LengthMismatch`. `tryEncode` is then exactly `validate` and `encode`, a law in
the suite pins it over generated and deliberately broken tables, and `decode` ENDS in `validate`, so
a table that decodes is one `tryEncode` accepts. A NaN is refused by `validate` rather than only by
`tryEncode` because the law is stated over what `validate` accepts; a computation that produces a NaN
still aggregates it — `aggregate` reads columns, not tables. `aggregate` admits every cell first and
refuses one outside its column's type by name (`AggregateError.CellOutsideType`) rather than
truncating it into an int `Sum` or dropping it from a decimal `Sum` while counting it in `Mean`.

*The decimal's edges.* A decimal column reads a whole-valued number token within the int53 guard
(`3000000000` arrives as a `JFloat`, and within 2^53 its value is its digits — the Timestamp arm's
rule), and refuses one past it. `DecimalText.tryToFloat` refuses past the float range rather than
answering ∞; the aggregates that are floats name it `AggregateOverflow`, and the columnar range rule
reads it as out of every finite range.

*The absent slot is a wire placeholder, not a cell.* `Cell.defaultFor` built `Date ""` — a cell no date
column accepts — to fill a null's slot in the values array. It is removed; the codec writes the same
bytes from a private placeholder a reader never decodes (the validity mask marks the slot). A `ref`
source no longer has to carry a schema: the decoder read it and discarded it, so the rule asked for a
statement nothing kept. The encoder still writes `"schema":[]` beside a `ref`, which the host twins'
readers expect.

*Column reads through `Wire.Decode`, generic over its error.* `Decode.Fault` names the two structural
faults (`MissingProperty`, `WrongKind`) before any codec spells them, and `propWith` / `stringWith` /
`arrayWith` / `tryProp` take the caller's spelling; the string combinators are those at `describe`,
byte-identical to before. A `mapError` adapter over the string form was the alternative and is
DECLINED: it would have had to parse the string back into `MissingField` versus `MalformedShape`.
`JVal.kindName`, `JVal.nonFiniteToken` and `Json.firstNonFinite` are public once, where three private
copies of each stood. `Schema.fingerprint` builds its pre-image through a value-identical copy of the
canonical field encoding (`Column` references only `Wire`, so it cannot call `Hash.canonicalFields`);
a name spelling the separator made two schemas collide under the bare `U+0001` join, and every
fingerprint moved once.

*`RowCodec` is obsoleted — and its removal waits on the UI tier, not on a count of callers.* Its bytes
carry two hazards no fix can remove without changing them: an `Unspecified`-kind `DateTime` goes
through `ToUniversalTime()`, which reads the machine's time zone, and an `int64` is widened to a
double. The phase's premise that "only tests consume it" is TRUE OF THIS REPOSITORY AND FALSE OF ITS
CONSUMERS: a downstream UI host calls it from its generated codec, its chart and binding code and its
IDL vocabulary, and
that host builds with warnings as errors, so the `[<Obsolete>]` reaches it as a build error at its
next Core raise. That is the notice doing its job, and it is the UI host's call how to answer it
(suppress FS0044 at the call sites for a release, or host its own row codec); what it settles here is
that removal at "the next breaking draft" means the next breaking draft AFTER the UI tier owns a row
codec — removing it sooner would break that host's generated code with no replacement to move to.

*Not done here, and where it goes.* The parser differential's model still reads the pre-299 grammar:
`proofs/JsonParse.fst` and its extraction are restated by Phase 306, which owns the model; until then
the differential carves out EXACTLY the refusals above (a `MalformedNumber` over a token
`isJsonNumber` refuses, a `BadEscape` carrying the surrogate message), recognised on production's own
answer, and a test pins the carve-out's extent over the near-miss table so it can be neither vacuous
nor wider. The shared wire corpus takes its copy of `conformance/refusals/` at the hosts' next pin
raise, when their codec twins re-certify; its manifest records every host as `proposed`.

## 2026-09-30 — D80: the reconcile partitions the region above its base, a rejecting lane set is refused as a set, and the "replays to the merge" premise is refuted rather than proved

**Recorded by Phase 300. `Fuaran.Core.OpStream.Dag`, `Fuaran.Core.Conformance`; rides the `0.33.0`
draft (STABILITY.md, "The lane DAG's reconcile applies shared history once…").**

*The subtraction rule.* A lane's delta is no longer `between base head`. The region above the base
is partitioned by node id: nodes held by two or more heads' closures are the SHARED region, applied
once and first, in the drain order of the union of the closures; each head's EXCLUSIVE delta is its
closure minus the base's minus every other head's, in the same drain order. Conflicts are checked
between exclusive deltas only, because shared history is not a concurrent edit — and two exclusive
deltas are incomparable node for node (an ancestor of a node in one head's delta is in that head's
closure, so it cannot be exclusive to another). The rule is what a downstream consumer had been doing
by hand at its own call site; it belongs in the primitive, so every consumer inherits the fix rather
than each re-deriving it. Consequences, each chosen: a head that is an ancestor of another
contributes nothing; a head named twice is DEDUPLICATED rather than refused (a duplicate head is a
harmless restatement, and refusing it would make an idempotent re-delivery an error); and `mergeBase`
stays a policy — "a maximal common ancestor", never "the right one" — because the partition no longer
depends on which maximal common ancestor the tie-break returned.

*The order-free rejection.* `reconcileMany` checks, in this order: interference between exclusive
deltas; the shared region replaying from the base state; every exclusive delta replaying ON ITS OWN
from the state the shared region reached. The third is the test the fold theorem used to assume
(`lanes_apply`). Each check reads a property of the lane SET — the pairwise report is symmetric up
to a swap, the shared region is a function of the set, and a lane replayed alone is a property of
the lane — so the refusal is the same under every arrival order, and the rejecting lanes are
reported sorted by head id. Interference is checked first so that every lane set that halted before
still halts with the same report; the halt half of the theorem is untouched. `reconcileMany` takes
the witness and the base state to do this, and `reconcile` does not: the two-head form stays the
pure graph query it was, and a caller wanting the N-lane partition without the test hands
`reconcileMany` an accepting witness.

*The refuted premise.* The phase asked for a theorem that the script, replayed from `replayTo base`,
equals `replayTo` of a merge of the heads "under the diamond". That is false, and the reason is not
the reconcile's: a merged history whose branches do not commute has no order-free replay of its own —
`replayTo` drains the merged closure by id, a tie-break — so no script can equal it. The witness is a
fast-forward whose later head merged a side branch writing the cell the shared history writes; a
pinned test finds one by varying actors until the id order puts the side branch first. What IS true
is proved: each node once, exactly the region above the base, for any closures (`reconcile_sound`),
and the whole checked fold arrival-order-invariant from the DAG (`reconcile_fold_order_free`). The
replay equality is measured by `reconcileLaws` over histories whose merged branches commute, which
is the only kind for which it is a question.

*The splice refusals, staged.* `append` / `merge` refuse a comma-bearing parent id and `merge` an
empty one, typed as `DagAppendFault` through `tryAppend` / `tryMerge`, with the plain forms raising.
Turning the plain forms into `Result` is Phase 296's, later on the same draft, together with its
unknown-parent and content-id-collision refusals; doing it here would churn every call site twice in
one draft for no reader's benefit. The criss-cross is drawn by `reconcileLaws` and not by
`laneFoldLaws`, because a criss-cross needs a merge op and a domain's lane generator supplies none.

## 2026-09-30 — D81: uniqueness over keyed positions is Core's refusal given the domain's declaration; the declaration stays a witness of its own, and `Children` stays what the engine rebuilds

**Recorded by Phase 286. Amends Phase 137's scoping (D34), confirms Phase 189's; breaking, on the
`0.33.0` draft (STABILITY.md).** Phase 137 made the insert validator walk the whole graft and scoped the
guarantee to the witness surface, leaving uniqueness over keyed positions — a case table, a fallback, a
named alternative — as "the domain's own obligation". Phase 189 gave the domain a way to DECLARE those
positions (`KeyedWitness`) and certified the domain's own check against the declaration, but only the kit
read it, so every consumer of a tree re-derived "all id-bearing positions" for itself, and a UI-domain
analysis counted about fifty such walks with at least five different answers. The ruling was that the
enumeration belongs in Core.

*Uniqueness over keyed positions is now Core's refusal, given the declaration.* `KeyedWitness` carries
the keyed NODES (`KeyedChildren`) and an arity-preserving rebuild of them (`ReplaceKeyedChildren`);
`Tree.traversal` derives the witness whose walk covers both surfaces; `Ops.applyContainedKeyed` refuses a
`DuplicateId` over that walk — an id held in a keyed position of the tree, or carried into one by the
graft or by an `UpdateNode` payload. This AMENDS D34's scoping, not its mechanism: the scan is still
`Tree.graftWellFormed`, over a different witness. Proved for the insert (`Preservation.fst` section 12,
`keyed_apply_preserves_wf`) by showing the keyed insert IS the unkeyed insert over the traversal, so
`apply_preserves_wf` transfers rather than being re-proved.

*The declaration remains a separate witness — Phase 189's reason CONFIRMED.* Widening `NodeWitness` would
oblige every domain to re-express a case table as an ordered list; the keyed positions stay a
declaration beside it. The record moved from the kit to `Fuaran.Core.Tree` (same namespace) because the
engine now reads it. `HasKeyedChildren` is DERIVED (`Tree.keyedIds`), not kept as a second field: two
declarations of one fact can disagree, and the kit certifying one while the engine reads the other would
certify something the engine never sees. The shard's wording was "kept for the kit"; the kit keeps the
reading, through the derivation.

*The structural surface remains what the engine rebuilds.* The keyed engine LOCATES through the traversal
and EDITS through `Children`: inserts append to it, removes filter it, reorders permute it, and a keyed
position is never added to, vacated or reordered by a skeleton op. So a `RemoveNode` / `MoveNode` of a
node held DIRECTLY in a keyed position has no structural edit to make, and is refused as a new
`Rejection.KeyedPosition (target, holder)` rather than as `UnknownNode` (the node is in the tree, so
"unknown" would be false) or as `Rejected` (the domain extension point, which `Preservation.fst` proves
Core never raises). A closed union gaining a case is breaking-source for an exhaustive match; the draft is
already breaking, and a host that must now say what this refusal means is the point.

*Keyed children come FIRST in the traversal.* A node's keyed positions are part of its own content, and
putting the structural list at the tail makes appending to it appending to the combined list — which is
what lets the proof transfer, first offender included.

*Not decided here, and why.* Whether `KeyedWitness` joins the witness-record freeze (Phase 232): it is
still being widened inside this draft, so the freeze decision belongs to the release that settles its
shape; `SurfaceLaws.unfrozenWitnesses` says so. (Closed on this draft: frozen — see "the six kit witness records are frozen", newest first.) The footprint (`Ops.footprint`) still reads the
structural surface; carrying keyed ids into it is Phase 247's.

## 2026-09-30 — D82: determinism is a SET of factors joined by union — the chain is declined, the journal is why, and a breaking change on the open draft is not a reason to keep the wrong answer

**Recorded by Phase 319. BREAKING; rides the `0.33.0` draft (STABILITY.md "The determinism axis is a SET of factors").**
`EffectClass.Determinism` was a total order — `Deterministic < Clock < Random < Network` — joined by
maximum. Two readings of one axis were both defensible, and the phase existed to rule between them
before a capture journal keys on the axis and before a host adopts the effect algebra as a contract:

- **(A) the chain stands.** "The least deterministic factor names the class." `join` is the maximum,
  `covers` is the order, and the documentation says a declared class names the least deterministic
  factor and a journal captures the whole class.
- **(B) a factor set.** "Every factor is a fact." `Determinism` is `Set<DeterminismFactor>`, the empty
  set is `Deterministic`, `join` is union, `covers` is superset, and the label renders the set
  canonically.

**Ruled: (B). (A) is DECLINED.** The reason is the capture journal. Under the chain, `join Clock Random`
is `Random`: the clock factor is dropped, so a journal entry for a body that read both carries one
label, and a replay that re-seeds the random source and not the clock reads as exact. The same
chain says a `Network` declaration `covers` a clock read, so a declaration can be honest without
naming a factor its body reads. Under the set, the label a capture records IS the whole class, and
`covers` is exactly "the declaration names every factor the body reads". The set is strictly more
informative than the chain — the chain is its projection by maximum rank, and the reverse loses the
clock beside a random read — so nothing the chain could say is lost and what it could not say is now
stateable. A downstream platform already models this axis as a factor set with union as the join,
and a downstream application-composition layer mirrors that shape; both are precedent only, cited
for the shape and taking no dependency in either direction (the pillars stay decoupled; the shared
form is a set of three named factors, nothing more). That `0.33.0` is an open, already-breaking draft
slot made the cost of the change low, but it is not what decided it: a breaking change is not good
enough reason to keep the wrong answer.

**What the decision fixes.**

- **The type.** `DeterminismFactor = ClockFactor | RandomFactor | NetworkFactor`;
  `DeterminismSource` is an alias for `Set<DeterminismFactor>`, kept so the conformance kit's
  witness builders keep their signature. The factor alphabet is closed; adding a factor is a
  `union-widening` of `DeterminismFactor` and a wire-vocabulary widening.
- **The algebra.** `Effect.join` is union on this axis (the host axis stays a chain), associative,
  commutative, idempotent, with `Set.empty` its identity; `Effect.covers` is `Set.isSubset actual
  declared` beside the host rank. The proof models restate these (`Capability.fst` section 1) and
  the effect law and the audit law hold unchanged.
- **The label.** Canonical, in a FIXED factor order (clock, random, network — held by an explicit
  list, not by the derived comparison, so reordering the union cases cannot reorder a wire label),
  joined by `+`; a single factor keeps the label it always had. The inverse accepts only the
  canonical spelling, so a set has one wire spelling and the capability codec's cross-check of the
  wire tag against the signature stays meaningful.
- **The mapping for a reader of the old shape.** A set maps to the chain by MAXIMUM RANK: the empty set
  is `Deterministic`, otherwise the highest-ranked member (`Clock` 1, `Random` 2, `Network` 3) names
  the class. The map is many-to-one: `{Random}` and `{Clock, Random}` both read as `Random`, which
  is the information the chain lost.
  A chain value lifts to the set by the one-member set (`Deterministic` to the empty set); the lift
  is a section of the projection, not its inverse.
- **The law.** `capabilityLaws` certifies that the effect a capture records covers every factor the
  body exercised, and that a read outside the recorded class is not covered and is named. The
  journal is unchanged in shape: it keys on the label string and the label now names a set.

**What is not decided here, and whose it is.** A host that mirrors the old four-token vocabulary as
a closed enum — a generated wire chain in a UI host, a name-mapping function in an orchestration
layer — keeps reading single-factor labels unchanged and must accept or refuse the `+` labels at its
own next raise; that work is on those hosts' own sides and is not reached from here. A capture
keyed per invocation (Phase 318) inherits the set: its key already carries the label.

**Declined alternatives.** (A) above. A *bitmask* representation would make the canonical order
implicit and the wire label a number; it was rejected because the label is a readable contract
other hosts certify against and the set is the shape the downstream precedents already use. A
*tagged union of `Deterministic | Nondeterministic of Set`* (the shape one downstream layer uses to
keep the empty set out of the second case) was not adopted: the empty set is already `Deterministic`
here, so the wrapper would add a case and an invariant to maintain and buy nothing.

## 2026-09-30 — D75: an incomplete match is a build error, the publication sweep is a standing arm of the suite, and the pack's reproducibility is re-measured — path-length-independent content, still no byte-identity claim

**Recorded by Phase 294. Gate and packaging only; no package surface moves (STABILITY.md `0.33.0 — DRAFT`).**
The repository's promise rests on closed unions matched exhaustively, and its gate did not enforce
it. Four blind spots, each closed where it was found, and one earlier measurement re-run.

*`FS0025` is an error, by number.* `Directory.Build.props` adds `FS0025` to `WarningsAsErrors` and
leaves `TreatWarningsAsErrors` false. The escalation is deliberately ONE diagnostic: turning every
warning into a gate would make a toolchain upgrade that adds a warning a red build nobody chose.
The recorded cases are the argument — Phase 135's `Incremental.reasonString`, "a
`MatchFailureException` waiting for the first fall-back that reaches it", and Phase 266's `(_, Anti)`
both shipped as a warning. **The escalation revealed no incomplete match in the spine, the suites or
the sample** (a no-incremental build of the whole solution with the code forced on, and again with
it passed on the command line), so landing it cost no rewrite, and a deliberately incomplete match added to a spine project fails the build with the error.

*The one exception, and how it is held.* The extracted proof oracle is F\* output. The extractor emits
one projector per field of a union case as a `match` over that single case, partial by construction,
because the model's own proof discharges the other cases and F# cannot see the proof; there are many
such sites. Its project file takes the code out of `NoWarn` AND out of `WarningsAsErrors`, with the reason
beside it. **Both edits are needed, and the second needs a third:** the compiler lets a
warning-as-error win over a `NoWarn`, and a `-p:WarningsAsErrors=...` on the command line is a global
property no project file overrides, so the project names `TreatAsLocalProperty="WarningsAsErrors"`
on its root element. Without that, the very command a session runs to prove its tree survives the
escalation would fail in the one project that is allowed to carry it.

*The publication sweep is a standing arm of the suite.* `PublicationBoundaryTests` sweeps every tracked
file (plus untracked-and-not-ignored files: what a commit would publish) — source comments, docs,
READMEs, ledgers, workflow files — one line at a time against `publication-boundary.json`. It is a
suite family and not a launcher stage, so it runs in `verify.ps1`, in CI and in a contributor's
`dotnet run` alike, with no environment. Four properties are what keep it from going quietly green,
each a go-red in the suite: every rule carries an `example` it must flag, and the file is refused
when a pattern does not match its own example (a rule cannot be edited into silence); a carried
exception is exempt only where its file AND its text match, and one that matches nothing fails (an
exception cannot outlive its line); the swept set must contain the files whose absence would make a
green meaningless; and the carried set is printed on every run. The rules are a committed list, so a
change to what is banned is reviewed as a change to a list. Phase citations are not vocabulary and
stay allowed everywhere; the operator-command rule is case-sensitive because the plain English
phrase is not a command.

*Two shard premises were wrong, and the sweep records the corrected model.* The shard asked for the
private workspace path in the two ledgers to become "a relative path". It already IS the relative
form the registry defines: a copy registry record names each consumer copy relative to the workspace
root, because that is the only root that can address a file in a different repository, and the
registry's own source calls the parent-relative alternative the worse one (it bakes the producer's
depth into every record); the version ledger requires the same `<repo>:<path>` shape. Rewriting it
breaks or degrades the freshness sweep the ledgers exist for. The two lines are therefore CARRIED,
file-and-text exact, and printed on every run; the `regen` commands beside them, which were free
text naming the same path, are rewritten generically. The sweep also found what the shard did not
list: 101 lines across 32 files, beyond the named doc comments — the ledgers themselves
(`DECISIONS.md`, `STABILITY.md`), `proofs/**` prose including four `.fst` comments, and test comments —
all rewritten in generic terms in the same change.

*The scheduling clause reads data, not an environment variable.* The proof-coverage clause that
checks a `model-bridge` row's `closes` phase is open read `FUARAN_CORE_ROADMAP`, a path to another
repository's rendered index, which public CI never set: the first real scheduling claim in
`proofs.json` would have reddened it permanently, and a public test was tied to the layout of a
private projection. It now reads the committed `tests/Fuaran.Core.Tests/open-phases.json`: either `{ "open": [ids] }`
or `{ "inert": "<reason>" }`, exactly one, both refused when malformed. **The file is declared inert
today**, because nothing writes the list and a hand-kept one goes stale silently, which would let a
stale scheduling claim pass; under the inert form a row that makes a scheduling claim FAILS, as it
did without an oracle. No row carries a phase-form `closes`, so the clause is vacuous on today's data
and the suite says so. Replacing the inert form with a list is the act of whoever owns the phases,
when a writer for it exists; this decision does not schedule one.

*The publish workflow.* It now refuses a ref that is not a tag (a dispatch on a branch has no
version to check and is refused rather than waved through), refuses a tag that is not `v<Version>`
for the `<Version>` in `Directory.Build.props`, and runs `verify.ps1` before it packs, on the full
history and tags the roster checks read. It does not run the proof leg, which needs the pinned prover
on Windows and stays in CI; publishing a commit whose CI run is red is a choice the workflow does not
make for the operator.

*D29 re-measured.* D29 (Phase 129) measured that a locally packed assembly was not byte-identical to
the published one and named three causes: the compiler feature band (`rollForward:
latestFeature`), the operating system, and no property pinning embedded source paths. This phase sets
`Deterministic` and, on a CI runner, `ContinuousIntegrationBuild`, which is the third cause, so the
measurement is re-run on `Fuaran.Core.Idl.Codegen`, the package D29 measured, packing the same commit
from two checkouts on the same machine and SDK:

| Checkout paths | `ContinuousIntegrationBuild` | Assembly |
|---|---|---|
| same path, packed twice | on | byte-identical |
| different paths, different LENGTH | off | different; the embedded PDB path is each absolute path |
| different paths, different LENGTH | on | different by 67 bytes, every one in the PE debug-directory layout |
| different paths, EQUAL length | on | byte-identical |

The embedded PDB path is `/_/src/Fuaran.Core.Idl.Codegen/obj/...` in both CI-on builds, so the
property does what it is for; the 67 bytes are the compiler's PE writer placing the debug-directory
blobs at offsets that depend on the LENGTH of the original absolute path (the section's virtual size
moves by 24 bytes, the PDB checksum entry moves with it, and the content hash, the module id and the
timestamp derived from it are equal). **Verdict: the third cause is narrowed from "embedded paths" to
"the length of the checkout path", and it is not closed; the other two are untouched.** On a hosted
runner the workspace path is fixed by the runner's own convention, so two publishes from one runner
image with the same SDK should produce the same assembly (an inference from the table, not a
measurement on a runner), and that is the strongest claim it supports. **Byte-identity of a local
pack against the published package is still NOT claimed**, for D29's reasons, which stand. Not
measured, deliberately: a comparison against the published `0.32.0` package, which needs a download,
and the operating-system cause, which needs a second operating system. The falsifier for the equal-
length row is the different-length row beside it, run both ways: equal lengths agreeing while unequal
lengths disagree is what makes length, and not anything else about the path, the cause.

*Consequences the pack-all tooling should expect.* A pack now writes a `snupkg` beside each `nupkg`,
so a packing script that globs `*.nupkg` leaves the symbols behind and one that copies the output
folder carries both. Nothing in this repository relies on either.

*Not done, and why.* `verify.ps1` is changed only by a patch the driver lands (`#Requires`, the
global `LASTEXITCODE` seeds, and `fantomas --check` over `samples` as well), because that file is the
gate's own and is not a worker's to commit; `run.ps1` carries the same changes directly.

## 2026-09-30 — D76: the spine owns the string-escaping rule, the rule is `\u00xx` for every control character, and the standalone packages carry it as pinned copies rather than as a reference

**Recorded by Phase 287. `Fuaran.Core.Wire`, `Fuaran.Core.OpStream`, `Fuaran.Core.OpStream.Dag`
and the conformance kit; the wire bytes move (STABILITY.md, 0.33.0), the managed surface `additive`.**

*The finding.* Two dialects of one canonical JSON met in the chain hash. `Wire.Json.escape` and
`Actor.encode` spelled `\n`, `\r` and `\t` short and every other control character `\u00xx`; the UI
host's canonical encoder, its own actor mirror and the TypeScript twin spelled all thirty-two
`\u00xx` — and the twin's comment claimed byte-identity with this package. Because the UI's linear
chain folds `OpStream.canonicalConfig.Payload`, an actor carrying CR, LF or TAB had one .NET linear
hash, another TypeScript linear hash, and a third .NET DAG hash. Every host was green because no
shared corpus carried such an actor. `Canon.render` already held the hosts' spelling, so this
package had both dialects INSIDE it, one private copy apart.

*The ruling.* The spine owns the rule, and the rule is the hosts': `"` → `\"`, `\` → `\\`, every
`U+0000`–`U+001F` → lower-case `\u00xx`, nothing else — no short forms, no `/` escape, no
`\u` above the control range. `Wire.Json.escape` is the rule's home; `Canon.render` escapes through
it and its private copy is deleted. This answers, for escaping, the open question recorded
beside the spine's convergence plan — whether Core's op-stream payload adopts the unified
discipline when streams cross hosts: yes, and Core is where the answer lives. Key ORDER of the payload (author-ordered
`{seq,actor,op}`) is deterministic as it stands and is not moved here.

*One function, or one rule? The phase asked for one function, and D2 says no.* The shard's task was
that `OpStream`'s escaper "takes the shared one". It cannot without a `Fuaran.Core.OpStream` →
`Fuaran.Core.Wire` project reference, and D2 (2026-06-17) is exactly the decision that there is none:
`OpStream` and `Wire` are standalone so an op-stream-only consumer pays for nothing else, and both
`OpStream.fs` (its `fnv1a` copy of `Hash.fnv1a`) and `DagOpStream.fs` (its scanner) already cite D2
as the reason they carry copies. Adding the edge would also put `Fable.Core` on every op-stream
consumer's restore graph through `Wire`'s package reference. So the shape taken is D2's own: **one
rule, three value-identical copies, held equal by a vector family** — `StringEscapeVectors`
enumerates the whole escape alphabet (every control character, the quote, the backslash, a
plain-text control) and pins, for each, the bytes of `Json.escape`, `Canon.render`, `Json.render`,
`Actor.encode` (both cases), both chain configs' payloads and `Dag.toJsonl`'s node line against one
table, plus the two named actors the phase asks the chain laws to carry. Inside `OpStream` the two
copies it had (`Actor`'s and the module's) ARE one function now, `JsonString.quote`, internal. The
`fnv1a` precedent is the reason this is a decision and not debt: a copy the kit compares is
the standalone packages' sanctioned way to share a rule, and the alternative — a reference — was
declined once already for a stronger reason than convenience.

*The migration shape.* On the Phase-255 seam: `canonicalConfig` is the `\u00xx` payload;
`legacyEscapeConfig` is the outgoing one, beside `legacyActorConfig`, and a store written under it
verifies with `verifyChainWith legacyEscapeConfig` and cuts over with `rehash legacyEscapeConfig
canonicalConfig`. `legacyActorConfig` keeps the SHORT escapes on its bare string, because it exists
to reproduce a pre-320 writer's bytes and that writer wrote `\n`. A control-free store has identical
payloads under both configs — verified unmigrated, and the rehash reproduces every hash — and the
suite proves it on a three-record store rather than asserting it.

*Two boundaries, stated rather than left.* (1) The chain config governs the actor's spelling. A
stored OP whose own `Codec` output carried a short escape re-encodes differently, and `rehash`
re-encodes through the one witness it is handed, so no config verifies such a chain; migrating it
needs the domain's old encoder on the verify leg and its new one on the rehash leg. No two-witness
rehash ships, because no known store needs one; the day one does, that is the shape to build, not a
third config. (2) A content-addressed DAG has no rehash — its ids are its parent links — so a DAG
node with a control-character actor takes a new id and a host re-appends. Neither is residue: each
is a design property of a seam this phase did not change, written where its next reader will look.

*Not done here, and why.* The shared chain corpus's control-character record is emitted by the UI
host's `HashChain.computeHash`, which folds this package's `canonicalConfig.Payload` at the version
the host PINS. Emitting it from a tree that pins the old bytes would golden the wrong hash; emitting
it by hand would break the resident-emitter rule the corpus is certified under. It lands with the
consumer's pin raise, and the consumer-side phase that depends on this one carries it. `SampleAdequacy`'s
census deliberately does not enrol `StringEscapeVectors`, on the `WireNullTolerance` precedent: a
fixed vector table has no sample that could miss anything.

## 2026-09-30 — D77: a tree digest folds each node's ARITY beside its label, the memo keys on the pre-image rather than its hash, and a depth marker is DECLINED — with the shard's reason for declining it corrected

**Recorded by Phase 290. `Fuaran.Core.Tree`, `Function`, `Projection`, `Validator`, the kit and
the proof leg; breaking on the `0.33.0` draft (every digest of five kinds changes value);
`Tree.encodePreimage` is the one public-surface addition.**

*The finding.* `Tree.contentHash` and `Tree.encodeHash` folded the preorder alone. A preorder does
not determine an ordered tree: `root(a(a1,a2), b(b1))` and `root(a(a1,a2,b(b1)))` visit the same
nodes in the same order and are one accepted `MoveNode(b, a)` apart, so they hashed equal under
every per-node encoder — the "encode must be injective over the node space" precondition was never
enough, because a per-node encoder sees one node and the alias is between two nestings of the same
nodes. `Function.applyMemo` keyed on `encodeHash` and served the cached tree on a key hit alone, so
one `MoveNode` reached a memo serving the wrong function's result. Three more keys joined their
fields on the bare `U+0001` separator (`Function.memoKey`, `Projection.digestOf`,
`Validator.canonicalCodes`; `Tree.Index.fingerprintOf` on `>` and `,`), which a value can spell.

*What is taken.* Every preorder node contributes TWO fields to the digest pre-image — its label and
its child count — through `Hash.canonicalFields`. A (label, arity) preorder is injective over
ordered trees, and that is proved rather than argued (`preorder_arity_injective`,
`proofs/TreeOps.fst` section 21): the arity says how many of the following entries belong under the
node, so the flat list parses back into one tree. The premise is evaluated beside it
(`preorder_alone_aliases`): the label preorder alone identifies the shard's pair. Every key the
spine mints that is not a chain hash now builds through `canonicalFields`, and the doc comment
above it is a ROSTER a source-reading family holds to `src/` both ways, so a new key cannot be
minted through a bare join without the suite naming it.

*The memo key is the pre-image, not a hash of it.* The shard offered two forms: compare the stored
`(fnHash, args)` on a key hit, or record the collision bound of the two 32-bit halves. Neither was
taken as written. Comparing `fnHash` after a hit compares eight hex characters the key already
holds — it closes nothing on the function side, where the `MoveNode` alias lives; and a recorded
bound is a documented hole, which is the debt posture this repository refuses. The key is instead
`Hash.canonicalFields` over the function's `encodePreimage` (the unhashed string) and its
address-sorted bindings, each binding three fields — address, case tag, payload, a slot payload
being the slot subtree's own `encodePreimage`. A `Map` keyed on that string compares the whole
pre-image on every lookup, so a hit IS an equality of `(function, param-set)` under the caller's
`encode`: nothing to compare after the hit, no bound to record, and the memo's soundness rests on
exactly two things — `encode` injective on a node's own content (the domain's obligation,
`Conformance.encoderInjectivityLaws`) and the two theorems above. The cost is the key's length: a
function's whole encoding beside the result tree the entry already holds. That is paid knowingly;
a memo whose entries are cheap to key and wrong to serve is the worse trade.

*A depth marker is DECLINED — and the shard's reason is corrected here rather than repeated.* The
shard recorded the alternative as "a depth marker, which is not injective either without arity".
That is false as stated: a preorder that carries EACH node's depth is injective over ordered trees
(each node's parent is the nearest preceding node one level shallower), so a per-node depth would
have closed the alias too. What is not injective is a bare DESCENT marker with no ascent — the
shape a fold writes when it emits one symbol on entering a child list and nothing on leaving it —
because `root(a(a1), a2)` and `root(a(a1, a2))` then emit the same marker sequence. The arity is
taken over a per-node depth for three reasons, none of them injectivity: it is what the witness
exposes AT the node (`w.Children n`), so the fold stays one local pass with no path carried down
and no state between nodes; it is what the theorem is stated over, and the proof's induction
(`shape_prefix`) reads the child list's length straight off the head; and it is the encoding a host
twin re-implements from the field list alone, where a depth has to be reconstructed from the walk.
A depth per node would also cost the same two fields per node, so nothing is saved by it. Recorded
so the next reader does not reject the depth form for a reason that does not hold, and does not
adopt it either.

*What is deliberately NOT done here.* `Hash.utf8Bytes` gets the platform's replacement bytes for a
lone or ill-ordered surrogate (byte-for-byte `Encoding.UTF8`, the parity claim the doc made and the
corpus did not test), and NOT a refusal: replacement trades one collision class for another (the
platform maps `"\uD800"`, `"\uDFFF"` and `"\uFFFD"` to one byte string), injectivity over ill-formed
input needs a typed refusal at the parser and at the guarded digest, and that layer is Phase 306's,
which depends on this phase and keeps the replacement form for the unguarded path. The wire
encoder's string escaping is Phase 287's and is not touched. `Schema.fingerprint` in
`Fuaran.Core.Column` still joins on the raw `U+0001` byte — `Column` takes no dependency on `Tree`,
by its own recorded reason — and the roster family names it as the one known bare join, by file
and count, so it is residue with a name rather than an exemption.

## 2026-09-30 — D78: the law kit gets a runner and a facade, each family is declared once, and `…With` means one thing

**Recorded by Phase 297. `Fuaran.Core.Conformance`; rides the `0.33.0` draft (STABILITY.md, "The kit
gets a runner…").** Three decisions, taken together because each is what makes the next one cheap.

*The runner.* Every law family is written over one internal module, `LawKit`: one loop, one cursor
over `ConfRng`, one first-counterexample cell (`LawCell`), one result tail. A kit-wide guarantee is
threaded through that module once instead of through sixty copies — which is how the drift the
phase was filed for happened (guards labelled three different ways, a coverage law the census could
not see, a census reason nobody could check). The cell counts its evidence, and the rule it applies
is the one Phase 302 asked for: a law asserted only in an arm the family's own guard counts reads
through that guard; a law whose arm no guard counts reds itself at zero evidence. **Every gated arm
is counted by a named guard or held by the cell — never neither.** The runner is internal on
purpose: how a family keeps its evidence is the kit's business, which is what lets it change under
every family at once. It preserves draw order, so every recorded seed still reproduces its sample.

*The facade.* `Conformance.fs` is the public surface and is compiled last; the families live in
internal topic modules ahead of it. A forward is one line with the family's full signature, so the
facade is the contract a reader reads top to bottom, and the roster ids and the aggregates the suite
reads off this file's source stay where they were. The six report types nested in the module moved
to namespace level because the families that build them now compile before it; abbreviations keep
the type names, and the one thing an abbreviation cannot carry — a qualified union case — is the
source change the split costs a consumer.

*One declaration per family.* A family's adequacy class and refusal verdict are fields of its
`LawFamily` record; `SampleAdequacy.census` and `Families.refusalAudit` are projections. Until now
they were three lists held equal only by tests, and each test could catch a family missing from one
list but none could make the lists one. Making the census a projection moved the roster ahead of the
guard module in compile order, and the shared verdict types into a file of their own ahead of both.

*The naming rule.* A bare name is the family at its default. `…With` is the same laws with a pinned
parameter injected, last before `seed`; `…At` is the domain-witness form; a family that chains the
domain's own ops takes `hashFn`, and one over the kit's fixtures defaults it. The renamed entries
keep obsolete forwards through this draft under their own roster ids, so nothing that pins them
moves on the day the rule lands. The bare `aiSurfaceLaws` is RETIRED, not reassigned to the
kit-policy form its name would now suggest: a name that changes meaning under a caller is worse than
one that disappears. Three `…With` entries whose pinned parameter is not last before `seed`
(`snapshotLawsWith`, `concurrencyLawsWith`, `laneFoldLawsWith`) cannot take a forward under the name
the rule gives them; they are reordered together, in one breaking change, rather than one at a time.
(Closed on this draft: reordered with no forward — see "the kit's last three `…With` entries are reordered with no forward".)
(Amended by D127, Phase 390, `0.36.0` draft: the rule now covers EVERY family that takes a witness capability the base contract does not — each is spelled `…At`, its configured form is `…With` (the `…At` family with one more parameter, last before the seed, never `…AtWith`), and the eighteen bare spellings it replaced are obsolete forwards removed at `1.0.0`. A roster test holds it.)

**Declined here, and why.** Widening `LawResult` to carry a passing guard's reached counts is a
recorded open decision and is not taken by this phase: the runner makes it a one-module change
whenever it is ruled. Splitting `ConformanceTests.fs` along the new seams waits for a change that no
sibling phase is appending cases to.

## 2026-09-30 — D74: the proof leg's module-cone selector is Phase 164's sanctioned form of a cheaper leg; a shared checked-module cache stays DECLINED

**Recorded by Phase 328. Tooling under `proofs/` and the test project; no package surface moves.**
The proof leg checks every registered model or none, and a phase that edits one model paid for all
twenty-one. There are two ways to stop paying for the ones that did not change, and they are not
equally safe.

*The one declined, again.* A checked-module cache shared between invocations — or between runs,
machines, or a baseline and its descendants — would make a pass nearly free. Phase 164 made the cache
per invocation because that is exactly what let an orphaned run's `.checked` files pass as a cold
verification on 2026-09-14 (`TreeOps 0s`, then `==== proofs: green`), and nothing about that has
changed: a `.checked` file is evidence some *other* run produced, about bytes this run did not look
at. The per-module floor catches the loud case and not the quiet one — eight of the twenty-one
modules (`Skeleton`, `Limits` and `WireVersioning` among them) have floors of 0, and a warm read of
them is indistinguishable from a fast one.
**Declined; the per-invocation cache stands.**

*The one taken.* `check.ps1 -Since <tree>` — the **module cone**. It recomputes from the tree every
time, verifies what it selects cold, under every gate the full leg applies, and reuses only the
knowledge of what did not change. That is the property Phase 164 protects, kept: nothing is taken on
trust from a previous run except in one place, stated next. The selector is this decision's
sanctioned form of a cheaper leg, and a later phase that wants more speed extends the selector
rather than reopening the cache.

*The one place a previous run is trusted, and its guard.* An **empty** cone verifies nothing, so its
green must lean on something: it is green only when `proofs/last-strict.json` records a green
`-Strict` full run whose tree is an ancestor of HEAD, **and** the cone against that tree is empty too.
Without a record the leg says it has no strict baseline and exits 1. A green `-Strict` full run
writes the record, and refuses to — without changing its verdict — over a working tree that differs
from HEAD in anything a module reads.

*Soundness is the selector's whole correctness, and three choices follow from it.*

1. **The edges are wider than `open`.** A module is in when it references a changed model however
   transitively, and "references" is every `open` / `include` / `friend`, every module abbreviation and
   every qualified name in the model's code, comments and strings blanked. The phase that proposed the
   selector named `open` lines as the edges; measured, `WireCanon` reaches `JsonParse` only through
   `module JP = JsonParse`, so an `open`-only reading would have left it stale.
2. **The subject map is `modules.json`'s `packages`, not `proofs.json`.** The proposal named the
   ladder's rows as the map from production source to model; the rows name models and theorems and
   no source path. Phase 203's `packages` is the only place a package is joined to a model, so it is
   the map, read rather than restated.
3. **An unknown input defaults to "it matters".** Any path under `proofs/` the selector does not
   classify puts every module in, and so does any change to the code (not the comments) of
   `check.ps1` or the kit's engine. A missed input would pass stale evidence; an extra one costs time.

*Where it lives.* The cone is computed in the test project beside `parseModules`, so the ladder and
the selector read the roster with ONE function; `check.ps1` asks it (`--proof-cone`) and hands the
kit the cone. The kit — copied into other repositories by declaration — is not touched. With neither
switch `check.ps1` hands the kit the arguments it always did, so the full leg's verdict cannot move;
the `Proofs.Cone` family holds that, the three perturbations the phase names, and the git half end
to end over a scratch history.

*A figure corrected on the way.* The proposal sized the problem at 2,742 s of measured cold checks
(about 46 minutes a pass). The twenty-one `measuredSeconds` sum to 783 s — each the slowest cold run
ever observed, most under contention — and 1,710 s of budget; a quiet pass is about ×0.29 of the
first figure (Phase 171). The selector is still worth having, since a one-model cone measured 46 s
of prover time under contention against that 783 s, but the saving is a quarter of what was claimed.

## 2026-09-30 — D73: the IDL spike scaffolding leaves `src/` — the operator tool moves to the CLI, the mini vocabulary stays as a test fixture, and relocating the spike under `tests/` is DECLINED

**Recorded on the `0.33.0` draft (Phase 230). Executes the maintainer's ruling of 2026-09-20: DELETE.**
Two things in `src/` were the IDL inversion spike's scaffolding, kept after the inversion shipped:
`Fuaran.Core.Idl.Spike` (never packable) and `ProposalSpike.fs`, compiled into the packable
`Fuaran.Core.Idl.Codegen`. The question was whether the operator tool the second one backs
(`--spike-proposal`, generalised by Phase 123) moves to `Fuaran.Core.Idl.Cli` and the project is
deleted, or the spike is kept deliberately as a second-domain certification fixture and moves under
`tests/`. The ruling is the first. **Relocating the spike under `tests/` is declined**, and a
generic spine does not carry its own inversion-era archaeology in `src/`.

*What moved where.*

| Was | Is |
|---|---|
| `ProposalSpike.fs` in `Fuaran.Core.Idl.Codegen` (public: `ProposalSpike`, `SpikeInput`, `SpikeLeg`, `SpikeReport`, `ExternalLeg`) | `src/Fuaran.Core.Idl.Cli/ProposalSpike.fs`, internal, behind the `spike-proposal` verb; flags, report and exit codes unchanged. A `removal` from Codegen's public surface, riding the `0.33.0` draft (STABILITY.md) |
| the `--spike-proposal` flag of the test runner | gone there; `IdlProposalTests` drives the harness in-process (internals visible to the suite) and the verb end to end as a process |
| `src/Fuaran.Core.Idl.Spike/` (`Spike.fs`, `Generated.fs`) | deleted from `src/` and the solution; the vocabulary and the F# emitted from it are `tests/Fuaran.Core.Tests/MiniIdl.fs` and `MiniGenerated.fs` |
| Phase 185's vacuity guard (`packable < total` over `src/`) | proved on a fixture tree built for the test; see below |

*Why the mini vocabulary is kept, and why that is not the relocation the ruling declined.* The
ruling declined keeping the SPIKE — the project, as a named second-domain fixture — under `tests/`.
What the suite still certifies against is narrower and measured. `miniIdl` is the only vocabulary
declaring a transparent union (`TextSource.Literal`, a bare-string arm), which `ReferenceIdl.fs` does
not, so it is the only source of the bare-value-arm, `transparentCase` and D33 coverage, of the
snapshot surface (`snapshots/spike.json`), of one of the four neutral vocabularies
`IdlCertificationTests` walks, and of the engine-level legs `IdlSpikeTests` holds (the 500-vector F#
and TypeScript sweep, the TypeScript decoder round-trip, the witness and schema legs). Deleting it
with the project would have removed that coverage to satisfy a tidiness ruling, which is debt, not
tidiness. So the fixtures stay in the test project under names that say what they are, and the
project, the harness and the `src/` footprint go. Whether to rename the remaining `Spike`-named test
files and `miniIdl`'s role beside `ReferenceIdl.fs` is a separate, open question and is not decided
here.

*What was lost, stated.* `Fuaran.Core.Idl.Spike` compiled the generated F# against the model half
alone — it deliberately did not reference `Fuaran.Core.Idl.Codegen` (Phase 97), so that the output
could not quietly come to need the generator. `MiniGenerated.fs` compiles in the test project, which
references everything, exactly as `DocGenerated.fs` already did; that one property — "the emitted
source builds against the model assembly alone" — is no longer checked by a compile. It is still
checked by what the emitted source names: the byte-for-byte drift guard pins the file to the
generator's output, and that output opens only `Fuaran.Core` and uses the wire, tree and validator
seams — it names no codegen type. A compile-level check on a model-only project would be a project
under `tests/` again, which is the relocation declined.

*The vacuity guard.* `FableSmokeCompletenessTests` asserted that at least one project under `src/`
declares `IsPackable=false`, so the derivation was seen to filter. The spike was the only such
project, so deleting it leaves an assertion that cannot pass; leaving the assertion, and adding an
unpackable project to `src/` to satisfy it, would be the guard dictating the tree. The guard is
re-seated: the derivation takes the directory it reads, the test builds a src-shaped tree in its
own output directory (one project that ships `fable/`, one that opts out of it, one
`IsPackable=false`), and asserts the filter drops exactly that one. The go-red is a second tree with
no unpackable project, over which the same comparison reads `false` — the guard is able to fail.
`fable-exclusions.json` and the roster tests are unchanged in verdict.

## 2026-09-27 — D72: the column model gains an EXACT decimal, carried as canonical text

**Recorded on the `0.33.0` draft. RATIFIED by the maintainer on 2026-09-27, as written.** The scalar
set was `int` / `float` / `bool` / `string` / `date` / `timestamp`. A sum of money has no honest home
in it: a `float` rounds, and an `int` of minor units moves the scale into every consumer's head. The
maintainer asked for a decimal in the column model, and ruled on its representation after the change
was written: it was put as a proposal, with a scaled integer under a declared scale as the
alternative, because a scalar's representation is a wire commitment every host language then
mirrors. The table below is what was ratified, and keeps beside each choice the evidence that would
reopen it.

**What was added.** `ColumnType.DecimalType`, wire tag `decimal`. `Cell.Decimal of string`. The
`DecimalText` module: `tryCanonical`, `isCanonical`, `compare`, `add`, `tryToFloat`, `zero`.
`Cell.decimal`, the constructor that canonicalises.

**The representation, and each choice with the evidence that would overturn it.**

| # | Choice | Why | What would overturn it |
|---|---|---|---|
| K1 | **The carrier is text**, not a host decimal. | The reason `Date` is text: the model takes no host type, and stays Fable-clean and byte-identical. `System.Decimal` is 96 bits with a scale of at most 28, other hosts' decimals are other widths, and a database's `NUMERIC(38, 10)` fits none of them everywhere. The digits fit all of them. | A measured cost, in a consumer, of parsing text on a hot path that a typed vector behind the `Table` boundary cannot absorb. |
| K2 | **The type is unparameterised**: no precision, no scale. | The text has exactly the digits it has, so a declared scale would be a second statement of the same fact, and the two could disagree. `ColumnType` stays a closed set of nullary cases, which is what `ColumnType.all`, `ofTag` and every host's enum rely on. | A host that must allocate a fixed-scale vector before it sees the data and cannot scan for the scale. |
| K3 | **The canonical form strips zeros**: no leading zero, no trailing fraction zero, no point on a whole number, no sign on zero. | Two texts then denote one number exactly when they are one string, so equality, grouping and the distinct token need no arithmetic. A scale kept for display (`12.50`) is presentation, which the render tier owns. | A consumer for which the written scale is data, not presentation. It would carry the scale in a column of its own. |
| K4 | **The grammar read is `-?[0-9]+(\.[0-9]+)?`**, and leading and trailing zeros are read and normalised. A leading `+`, a bare point, an exponent, a separator and white space are refused. | It is what a database renders a fixed-scale value as. Each refused form has more than one reading in some locale or tool. | A source in wide use that emits one of the refused forms and cannot be configured not to. |
| K5 | **On the wire a decimal is a JSON string.** An integer token is read, because it is exact. **A fractional number token is refused** as a `TypeMismatch`. | A number token has been through a float in most parsers before any codec sees it. A type whose purpose is exactness cannot accept a value it cannot vouch for. This is stricter than the lenient-ingest rules the other types follow, deliberately. | Evidence from an emitter census that the refusal costs more emissions than the silent inexactness would cost correctness. The remedy would be a named, opt-in lenient reader, not a change to this one. |
| K6 | **`Decimal` is declared after `Null`**, and `DecimalType` last. | Every case already published keeps its tag, so the move is `union-widening` and nothing else: no existing case is retyped. | Nothing; the order is read by no clause. |
| K7 | **The column layer's arithmetic is an order and a sum, both exact.** `Sum` over a decimal column is a `Decimal`. `Min` and `Max` compare exactly. `Mean`, `Median` and `StdDev` are `float`, as `aggType` already declares for every source type, and read each value at its nearest float. | They are what `Column.aggregate` needs. Division is not closed over finite decimals, so an exact `Mean` would need a rounding rule this layer has no ground to choose. Multiplication, division and rounding belong to the evaluator, in the compute repository. | A consumer that needs an exact mean to a stated scale. That is an evaluator function with the scale as its argument. |
| K8 | **`Int → Decimal` is a safe widening; `Float → Decimal` is not, in either direction.** | An int is exactly a decimal, and the codec agrees by reading an integer token into a decimal column. A float is an approximation and a decimal is a statement of digits, so a retype between them changes what the column claims. | Nothing foreseen. |
| K9 | **Schema inference never infers `decimal`.** | The rule for temporal types: a column of digit strings is a string column until a schema says otherwise. | Nothing foreseen. |
| K10 | **The capture key's tag for the case is `m`**, and the payload is the text as it stands. | `d` is a date's. Taking the text as found is what keeps the pre-image injective on cells, which is `cell_fields_injective` in `proofs/Query.fst`; canonicalising there would key two distinct cells alike. | Nothing; the tag is pinned once released. |

**A `Decimal` cell is canonical by construction, and the boundaries hold that.** `Cell.decimal`
canonicalises, the codec canonicalises on decode, and `ColumnCodec.tryEncode` refuses a cell whose
text is not canonical. Inside those boundaries every reader takes the text as it finds it. A cell
built directly with non-canonical text is the caller's defect, as a `Date` holding text that is not a
date is; `encode` emits it as it stands and the round-trip law catches it.

**The class and the version.** A case added to a published union is `union-widening`, breaking: every
exhaustive `match` over `Cell` or `ColumnType` stops compiling, and the C# facade's `CellValue.Match`
and `Switch` gain a parameter. `0.33.0` is an untagged draft that already carries a breaking move
(D71), so this rides it and moves no number. STABILITY.md "0.33.0 — DRAFT" is the consumer-facing
record, and its earlier sentence that a consumer of the spine "changes nothing else" is amended
there.

**What this change does not do, and who does.**

- **The evaluator.** Decimal arithmetic in expressions, the typer's rules for a decimal operand, a
  typed vector for the dense frame and the transform law vectors are the compute repository's. It
  pins this repository at `0.32.0` and is unaffected until it raises; the raise is where its
  exhaustive matches meet the new case.
- **The other hosts.** Each host language's twin of the column codec gains the type when it raises.
  The shared wire corpus gains decimal documents with the first host that emits them.
- **The proof leg.** `proofs/Query.fst` carries the new case in its `column_type` and `cell`, and
  `cell_type`, `cell_tag` and `cell_payload` each gain an arm. **The model was checked with the
  pinned prover over the new case: `Query.fst` verified on three cold runs, every query 3/3 under
  `--quake`, and a fresh extraction of `proofs/oracle/Query.fs` is byte-identical to the committed
  one.** No theorem's statement changed; `cell_fields_injective` holds with the seventh tag because
  the tags are pairwise distinct and the payload is the carrier. What was run is the one model this
  change touches. The other twenty are untouched, and the full leg is the continuous-integration
  job's on the push.

**K1 measured, and it HOLDS (2026-10-02).** K1's reopening evidence was a measured cost of parsing text
on a hot path that a typed vector behind the `Table` boundary cannot absorb. The compute repository's
Phase 280 measured exactly that. With the text path as the baseline, the premium of a decimal column
over a float column cleared its committed bar of 2.0 (sort 19.6x and 10.9x, group-and-sum 8.5x and 6.2x
on .NET at 10,000 and 100,000 rows; up to 20.6x under node; joins about 1.0x). It then built the typed
vector: a decimal column whose values fit 15 significant digits at one scale holds each value as an
exact integer in a float64, with the cells kept beside it, and every other column keeps the text path.
Every premium fell under 2.0 on both hosts, with every cell read byte-identical to the text path. So
the cost K1 named is absorbed behind the boundary, and the carrier stays text. Nothing in this
repository changes.

## 2026-09-26 — D71: D66 is EXECUTED — the compute strand has left this repository, on the `0.33.0` draft, moved rather than removed

**Recorded (Phase 258).** D66 ruled that `Fuaran.Core.DataFrame` and `Fuaran.Core.Column.Ops` are
produced by a repository of their own under the same ids, and listed four steps. The first was
Phase 257 (D68: the line drawn inside this tree). The second and third are done outside it:
[`Fuaran-Core/fuaran-core-compute`](https://github.com/Fuaran-Core/fuaran-core-compute) exists,
carries the four packages with their history, and released them at `0.33.0` — the next minor above
this repository's last emission of them, `0.32.0` — on 2026-09-26. This phase is the fourth: the
strand is removed from here.

**What left.** The four projects (`DataFrame`, `Column.Ops`, and the two Phase 257 cut beside them,
`DataFrame.Conformance` and `DataFrame.CSharp`), the dataframe facade proof, the test suites over
them, the two proof models (`ColumnOps.fst`, `Pipeline.fst`) with their claims-ladder rows, cost
entries, oracle extractions and coverage exclusions, their public- and wire-surface baselines, the
transform law vectors with their exporter and their entries in `version-derives.json` and
`copies.json` (the compute repository declares both), and `docs/incremental-evaluation.md`. The Phase
257 forwarding module went with `DataFrame.Conformance`, the package it lived in; the compute
repository retires its own copy in its own change-set.

**What changed in what stayed.** The kit's roster is one share again (`KitRoster`), and its generated
census renders this package's families only. The boundary test is now an assertion of ABSENCE: no
project, directory, package reference or built spine assembly names one of the four ids, so a helper
cannot bring them back — including by a package reference to the compute repository's release, which
would invert the direction D66 set. The Phase 250 composition sheet, which certified
`propagationEvaluatorLawsWith` over `Column.Ops` edits and a `DataFrame.Incremental` refresh, now
states its source edits and its table node's cache over `Column` alone, so the family keeps an
adequacy witness here; its `ColumnOps.deltaOf` section left with that function.

**The version.** `0.32.0` is tagged, and removing four packages is the surface gate's `removal`, so
the slot advances to `0.33.0` rather than riding. The number coincides with the compute repository's
first release by construction, not by coupling: from here the two repositories version
independently, and a consumer pins each with its own property. STABILITY.md "0.33.0 — DRAFT" is the
consumer-facing record ("moved, not removed").

## 2026-09-26 — D70: D62 is AMENDED — the compute repository is the specification owner for the compute subsystems the other hosts twin

**Amends D62 (Phase 258, as D66 step 4 directs).** D62 names this repository "the reference
implementation and specification owner for every other host language", and lists among the
subsystems the other hosts twin the `Transform` / DataFrame evaluator. From `0.33.0` that evaluator
is not in this repository. So the ownership D62 states is split along D66's line:

- **The compute subsystems** — the `Transform` / `ColExpr` evaluator, the columnar op algebra, the
  incremental seam and the transform law vectors (`laws/transform-laws.json`) the other hosts certify
  against — are specified by
  [`Fuaran-Core/fuaran-core-compute`](https://github.com/Fuaran-Core/fuaran-core-compute), which
  emits and stamps those vectors.
- **Everything else D62 names** — `FunctionRegistry.findBySignature`, `Capability.invocationKey`,
  list-parameter substitution, the lenient-ingest rules, and the law sets this repository emits
  (`laws/capability-laws.json`, the `apply/` family) — stays this repository's to specify.

D62's trigger (a per-language Core package is cut when a SECOND domain needs Core semantics in that
language by a route other than Fable) and its preparation rule (each host keeps its twins behind a
boundary; the specification owner emits every law set it is the reference for) apply to each owner
for its own subsystems: a future per-language compute package certifies against the compute
repository's vectors. D62's text stands; this entry is how it reads from `0.33.0`.

## 2026-09-26 — D69: D49 is AMENDED — the dataframe algebra belongs "not in a consumer tier"; its reasons stand and its conclusion changes

**Amends D49 (Phase 258, recording what D66 ruled).** D49 decided that "`Fuaran.Core.DataFrame`
stays in this repository", on three reasons: the algebra has several consuming domains and none is
above the others; it sits on this spine and on nothing else; in the consuming tiers it is a binding,
never a node. All three still hold, and this entry does not re-argue them.

What changes is the conclusion they support. Each reason rules out homing the algebra IN A CONSUMER
TIER — a user-interface, presentation or application-composition tier — and none of them rules on a
sibling repository beside this one, which satisfies all three equally: its consumers stay peers, it
depends on this spine and nothing else, and it stays a binding in every consumer. D66 answered that
second question, for a reason D49 never weighed (one `<Version>` over a spine that should break
almost never and a compute layer that will break repeatedly). So D49's conclusion reads, from
Phase 258: **the dataframe algebra belongs not in a consumer tier** — today in
[`Fuaran-Core/fuaran-core-compute`](https://github.com/Fuaran-Core/fuaran-core-compute), under the
same ids. D49's honest reading of the reference evaluator against the no-evaluator principle is
unchanged and travels with the code. D49's text is left as written; this entry is the amendment.

## 2026-09-26 — D68: the compute boundary is prepared inside Core — two assemblies cut beside the leaving ones, the families keep their spelling through a same-named forwarding module, and the facade half keeps its namespace

**Decided (Phase 257).** D66 moves `Fuaran.Core.DataFrame` and `Fuaran.Core.Column.Ops` to a
repository of their own, and asks for the line to be drawn here first so the move is a copy of
whole assemblies. Two packages crossed it: `Fuaran.Core.Conformance` referenced both, and
`Fuaran.Core.CSharp` referenced `DataFrame`. After this phase neither does, and
`ComputeBoundaryTests` refuses any spine assembly that reaches the compute side again. The work
needed five calls.

**1. What moves is decided by a mechanical rule.** A family that reads `DataFrame` or
`Column.Ops` ships from `Fuaran.Core.DataFrame.Conformance`. Everything else stays in the kit. The
phase named seven families; the rule moves eleven, plus `IncrementalDelta`:

- `transformLaws`, `aggregateParityLaws` (the `GroupBy` half; see 4), `columnarOpLaws`,
  `columnarOpLawsWith`, `columnarOpStreamGen`, `incrementalLaws` and `incrementalLawsWith`;
- `paramLaws`, `schemaWalkLaws`, `nowLaws` and `slotParamLaws`. The phase did not name these four,
  but each evaluates a pipeline through the `DataFrame` reference evaluator;
- `IncrementalDelta` (`laws`, `lawsWith`), moved whole under its own module name.

`columnarValidatorLaws` stays because it is over `Column`, which stays. `ParityVectors` stays
whole. The phase expected compute rows in it, but it has none: every vector is a hash, a canonical
float, a JSON rendering, a chain hash, a `ConfRng` draw or a `Schema` fingerprint.

**2. The families keep their spelling through a second module named `Fuaran.Core.Conformance`.**
The home of the moved families is the module `DataFrameConformance`. The same assembly also
declares a module `Fuaran.Core.Conformance` whose every member is a one-line call to its home.
F# resolves a qualified name against every module of that name in the referenced assemblies, so a
consumer that references both packages keeps compiling unchanged. This was measured, not assumed,
for three consumer shapes: `open Fuaran.Core` then `Conformance.x`; a module abbreviation
`module K = Fuaran.Core.Conformance` then `K.x`, which is the shape fuaran-dotnet uses; and fully
qualified `Fuaran.Core.Conformance.x`. It holds on .NET, and under Fable 5 in a two-package probe
whose output ran under node. Two modules of one name in ONE compilation are refused (FS0248). Across two packages they
are not, on either side; the Fable probe is the evidence for the transpiled one. This
repository's own suite now compiles through the forwards, so they are exercised on every run. A
test holds them to the home module member for member, and runs each seed-and-iterations forward
beside its home.

Four alternatives were declined:

- **An `[<AutoOpen>]` module wrapping a nested `Conformance`.** This was the obvious extension
  shape. It resolves only through `open`: the abbreviation and the fully-qualified forms both fail
  (FS0039, measured), and the abbreviation is exactly the form the largest consumer uses.
- **Type-forwarders or forwards inside the kit.** Either would make the kit reference the new
  assembly, which is the upward reference the boundary test exists to refuse.
- **No forwards, with every consumer renaming in its raise.** It is honest but costs every consumer
  the rename now, when it can be taken once, with the removal, in Phase 258.
- **Making the same-named module the permanent home.** It would spare consumers any rename. But it
  leaves one module name split across two independently versioned repositories for good. A family
  added on either side with a name the other side already uses would then resolve by reference
  order. That is the collision this repository's 0.11.0 entry named for types.

**What a consumer pays.** It adds one PackageReference, `Fuaran.Core.DataFrame.Conformance`, and
changes no source. The surface gate classes `Fuaran.Core.Conformance` and `Fuaran.Core.CSharp` as
`removal`, because a binary compiled against 0.31.0 that calls a moved member does not find it.
Both ride the standing 0.32.0 draft, which is breaking already (D65, D67). The roster keeps the
spellings consumers call today (`Conformance.<family>`, `IncrementalDelta.<entry>`), so a census
has no renamed rows. There is one exception, and it is the consumer's one-line change in its raise:
a census that reflects over the kit's assembly alone must reflect over both. fuaran-dotnet's
census reads `typeof<LawResult>.Assembly` for four module names, so until it widens it will report
`IncrementalDelta` missing.

**What Phase 258 removes.** It removes `Forwards.fs`. It re-keys the moved rows from
`Conformance.<family>` to `DataFrameConformance.<family>`; that re-keying is the census rename, and
it is taken once. The families fuaran-dotnet's census adopts at 0.31.0 that it touches are
`aggregateParityLaws`, `columnarOpLaws`, `columnarOpLawsWith`, `incrementalLaws`, `paramLaws`,
`schemaWalkLaws`, `nowLaws`, `slotParamLaws`, `IncrementalDelta.laws` and
`IncrementalDelta.lawsWith`. `IncrementalDelta` keeps its name. `transformLaws` is a `Not used`
row there. `incrementalLawsWith` is new in 0.32.0 and has no row yet.

**3. The roster is declared per package and composed by its reader.** `Families` cannot name
families in a package it does not reference. So each package declares its own share as a
`Families.Roster` (`Families.roster`, `DataFrameFamilies.roster`), and each share carries the same
record, refusal-audit and census rows it carried before. The renderings take a list of rosters
(`toJsonOf` / `toMarkdownOf`). The generated `docs/conformance-families.*` are rendered from both
shares, with the package each family ships from. The JSON's `package` became `packages` plus a
per-family `package`, and `schema` reads 5. The suite composes the two shares in one place
(`tests/Fuaran.Core.Tests/KitRoster.fs`). Every completeness check (reflection over return type,
census, refusal audit, reference runs) now quantifies over both assemblies. A roster key declared
by two shipped modules is refused rather than overwritten.

**4. Aggregate parity keeps one name on each side.** Parity is the comparison with a single-group
`GroupBy`, so that half is the dataframe layer's. It keeps the name `aggregateParityLaws` and
moves. The null-skip law reads `Column` alone and stays in the kit as `aggregateNullSkipLaws`. A
consumer calling `aggregateParityLaws` now runs one law where it ran two, and adopts
`aggregateNullSkipLaws` to keep the other. The proof-coverage exclusion that cited
`aggregateParityLaws` for `Fuaran.Core.Column` now cites `aggregateNullSkipLaws`, the half that
stays with `Column`.

**5. The facade half keeps its namespace, and each facade proves itself.** `Expr`, `Step`,
`Pipeline`, the slot types and the five dataframe vocabularies moved to
`Fuaran.Core.DataFrame.CSharp`, still in `Fuaran.Core.CSharp`. A C# consumer adds one reference and
changes no source. The half that stays declares a public `Vocabulary` bridge (`ToCore` /
`FromCore` for `ColumnKind` and `AggregateFunction`). An enum cannot carry the bridge pair itself,
and the dataframe half needs those two mappings. A bridge keeps one mapping; a copy would be a
second place for a new column type to be missed. The internal F#-interop helpers are copied, not
shared. Sharing them through `InternalsVisibleTo` would bind the leaving assembly to the staying
one's internals across a repository boundary, and making them public would put F# types on
non-bridge members. The facade proof is split in two, and each project scans its own assembly's
surface and its own unions' coverage. The partial generator and rebuild walk, the check ledger,
the coverage walk and the surface rule are compiled into the second project by link, so each has
one copy. Phase 258 copies them rather than untangling them.

## 2026-09-26 — D67: the witness-taking families extend the teeth seam rather than mint a second name; `aiSurfaceLaws` runs the domain's `Decide`, and the kit's policy is the named variant

**Decided (Phase 246; the member extension confirmed by the operator the same day).** Downstream
consumers' measurements (Phase 246) found that the families a domain most needs could not see the
domain: the seam families take a seed and certify this repository's own fixtures, and
`aiSurfaceLaws` substituted the kit's `Decide` for the domain's. Each planted defect passed: a host
that ran the body before the registry refused, and a policy that allowed every write. Phase 246 adds
the witness-taking forms beside the fixture-bound ones. Three calls in it needed recording.

**1. `columnarOpLawsWith` gains the generator; no second name is minted.** The name the phase asked
for was already taken by Phase 181's injectable-`invert` seam. Two readings were available: mint a
new name for the witness-taking form, or extend the member that holds the name. The member is
extended, on the precedent the shard itself cited: `concurrencyLawsWith` takes the teeth seam
(`footprintOf`) AND the domain's witness in one entry point, and a domain passes the shipped
function for the seam. So:

```fsharp
// 0.31.0
columnarOpLawsWith (invertUnderTest) (seed) (iterations)
// 0.32.0
columnarOpLawsWith (invertUnderTest) (gen: StreamGen<ColumnOp, Table>) (seed) (iterations)
```

The surface gate classes it `retype`, a breaking move for that member. It rides the `0.32.0` draft
because that draft is already breaking (D65). A second name would have left two entry points that
differ only in which half of the same parameter list they expose, and a reader choosing between them
would have to learn that from the documentation rather than from the signature. `columnarOpLaws`
keeps its signature and its sample exactly. `Conformance.columnarOpStreamGen` is the kit's reference
generator in the shape the new parameter takes. It is not `columnarOpLaws`' own sample, which reads
the evolving table and so cannot be a `StreamGen`.

**2. The seam witnesses are new composing records, not fields on frozen ones.**
`CapabilitySeamWitness`, `QuerySeamWitness` and `CapabilityPipelineWitness` compose the seam's own
types, as STABILITY's witness-freeze section prescribes ("compose, never grow"). Each carries the
host's `Dispatch` beside the registry and the body. The planted defect lives in the host's wiring,
and a family that called `Registry.dispatch` itself would certify Core's dispatch a second time and
never see it. A host that delegates to Core passes `Registry.dispatch registry`.

**3. `aiSurfaceLaws` runs the domain's `Decide`, and the old behaviour is a variant named for what it
does.** The family submits each drawn op as the actor `"author"` through the domain's policy. Its
adequacy guard counts the decisions that policy reached (allowed, parked, denied), so a policy that
never parks or never denies anything drawn is starved: RED, because the gate was never exercised at
that domain. Core cannot know which of a domain's ops write, so this is the claim the family can
make honestly. It does not claim the policy is right. It claims that a green run exercised the
policy's gate. `aiSurfaceLawsUnderKitPolicy` is the pre-`0.32.0` family unchanged. It is never the
default, because its green says nothing about the policy, and its name says so. This is a VERDICT
change on an existing family, which Phase 220 classed breaking whatever the member's surface class.
A domain that was green only because the kit rolled its decisions can turn red. The other direction
exists too, and is why the variant is kept rather than deleted: a proposal arm the kit reached with
an op the domain's policy never routes there is no longer exercised on that op, so a plumbing fault
found only that way is the variant's to find.

**Declined: guarding the cross product** (every decision against both reducer outcomes). A domain
whose policy denies only ops the reducer would refuse anyway is legitimate. Demanding every cell
would starve it for a property of its policy rather than a gap in its generator.

## 2026-09-26 — D66: the compute layer becomes its own repository — `Column` stays, `DataFrame` and `Column.Ops` leave under the same ids; D51 is the rule that draws the line, and D49 is amended to the question it answered

**Decided (operator, 2026-09-26).** `Fuaran.Core.DataFrame` and `Fuaran.Core.Column.Ops` will be
produced by a repository of their own in the same public organisation, under the SAME package ids
and namespaces, together with the columnar law families (a compute-side conformance package), the
dataframe half of the C# facade, the two proof models that cover them (`ColumnOps.fst`,
`Pipeline.fst`) and ownership of the transform law vectors the other hosts certify against.
`Fuaran.Core.Column` stays here. So do `Query`, `Validator`, `Propagation` and every other spine
package. The operator's words were that this is the correct long-term shape and that its cost is
accepted now because it will never be cheaper. This entry records the ruling and the reasons; the
work is a sequence of phases, and nothing moves before Phase 250 has landed (it has: `64d0b5a`).

**The rule that draws the line is D51, not a new one.** Membership here is genericity over the
witness. `Column` passes: a fixed scalar set with a validity mask and a canonical wire is what every
seam needs and no domain owns — `Query` returns a `Table`, `Validator` carries a columnar rule family
over one, the facade reads one. The compute layer does not pass. A transform algebra with a pure
reference evaluator, an incremental engine with a measured cost model and an op algebra over tables
are a concrete engine with pinned semantics, witness-free by construction (the README has said "no
witness" of the strand since the roster was written). It sat here as a standing exception to the
rule, and this entry ends the exception rather than restating it.

**D49 is amended, not reversed.** Its three reasons — several consuming tiers with none above the
others, a dependency set that is this spine and nothing else, a binding rather than a node in every
consumer — all still hold, and all are satisfied equally by a sibling repository in the same
organisation. What they rule out is homing the algebra in a consumer tier. They never ruled on
"beside this repository", and that is the question this entry answers. D49's honest reading of the
reference evaluator against the no-evaluator principle is unchanged and travels with the code.

**Why the answer is "beside", and the decisive reason is the version number.** This spine should
break almost never; its programme is stability, laws and proofs. The compute layer will break
repeatedly, because closing the O(n) floor a downstream consumer measured (Phase 250's source: the
row-preserving incremental refresh lost to full evaluation at every size above a thousand rows) and
making the three-strand composition a contract is a performance programme, and performance
programmes change shapes. One `<Version>` over both forces either false breaks on the spine or
held-back compute. Phase 250 riding a breaking draft of this repository to add a skeleton-op case
for a spreadsheet's benefit is the first instance. Two audiences follow from the same fact: the
substrate's reader wants the witness pattern, the laws and the proofs; the compute layer's reader
wants a typed dataframe for F# and Fable with a certified incremental evaluator, and compares it
against the dataframe libraries they would otherwise pick.

**What follows, in order, each its own phase.** (1) The boundary is prepared HERE first, on D62's
pattern: the columnar families and the dataframe facade split into their own assemblies inside this
repository, and a test refuses any spine assembly that references `DataFrame` or `Column.Ops` — so
the cut is a copy, not an untangling. (2) The repository is created and filled, adopts the proof-leg
kit by copy and declares the corpus copy of the transform laws as ITS derived file. (3) Its first
release opens at the next minor above this repository's last emission of the two ids, so every
consumer's floor stays monotone; consumers pin a second producer property. (4) The strand is
removed from here on a BREAKING draft whose `STABILITY.md` entry says "moved, not removed" and names
the producer and version the ids continue at; D62 is amended to name that repository as the
specification owner for the compute subsystems the other hosts twin.

**What this entry does NOT decide.** Whether `Query` follows later (it returns a `Table` over
`Column` and sits over `Function`; today it stays, and a future query planner needing the transform
algebra is the trigger to revisit). The repository's name. Whether the proof leg runs in its CI from
the first commit. Each is an open question on the plan that sequences this work, and each is
answered at the stage that needs it.

**Note (2026-10-02, the compute repository's Phase 322 and its DECISIONS.md D4).** The SAME-ids
ruling above held for the cut and no longer holds for what the compute repository produces: from
its `0.36.0` the packages carry their own ids and the `Fuaran.Compute` namespace —
`Fuaran.Core.DataFrame` → `Fuaran.Compute.DataFrame`, `Fuaran.Core.Column.Ops` →
`Fuaran.Compute.ColumnOps`, `Fuaran.Core.DataFrame.Conformance` → `Fuaran.Compute.Conformance`, and
its later `Fuaran.Core.DataFrame.PipelineQuery` → `Fuaran.Compute.PipelineQuery` (`Fuaran.Core.DataFrame.CSharp`
was deleted, not renamed). The `Fuaran.Core.*` ids stop at `0.34.0`, their last published version.
The reason is this entry's own line, drawn by D51: a name that says the dataframe is a Core property
says what D51 rules it is not. Nothing in this repository moves but the boundary: the compute
boundary test refuses BOTH id sets inside the spine — the new ones because they are what crosses the
line now, the old ones because they stay restorable and a stale pin could bring one back.

## 2026-09-26 — D65: `UpdateNode of node: 'Node` — one field, content not structure, and an unknown-parent write in its footprint; classed `union-widening`, so it opens the `0.32.0` slot

**Decided (operator, 2026-09-26; executed by Phase 250).** `SkeletonOp` gains an in-place update,
declared last so every existing tag keeps its number. The source was a downstream
spreadsheet-shaped consumer's measurement (Phase 250). A redefined formula was
`Batch [RemoveNode id; InsertChild(parent, node')]`. That moved the node to the end of its
siblings and dirtied the parent as well, and the consumer measured 2.82 nodes dirtied per
redefinition where 1.00 moved.

**One field, not two.** The phase text proposed `UpdateNode(id, node)`. The shipped case is
`UpdateNode of node: 'Node`, and the target is `w.Id node`. With two fields the op can carry an id
that disagrees with its payload's, and Core has no honest refusal for that. Every existing
`Rejection` case names something else. A new trailing case would be a second break, on a second
closed union, and it would widen the proof model's rejection type and `Preservation`'s op/rejection
characterisation with it. One field makes the mismatch unrepresentable. It is the argument `Ops.fs`
already records for removing the ordinal: an id stated twice can disagree, and an id stated once
cannot.

**Content, not structure.** The node takes the payload's content and keeps the children it already
has, and the payload's children are not read. Membership and order stay the other four ops' job,
exactly as `Ops.fs` separates membership from order. So `Propagation.touchedBy` is the node alone,
the id set never moves (no duplicate-id clause), and the root may be rewritten. Under
`applyContained`, a rewrite that would leave children under a node the predicate refuses is
`NotAContainer(target, newKindTag)`. It names the NEW kind, because that is the kind refused.

**The footprint carries an unknown-parent write, and that is required.** `Ops.footprint (UpdateNode
n)` is `Reads {id}`, `ContentWrites {id}`, `UnknownParentWrites {id}`. Without the unknown-parent
write, an update of `x` and a concurrent `RemoveNode` of an ancestor of `x` share no address the
script can name, so `Ops.independent` would call them independent. They do not commute: the update
lands in one order and is refused in the other. Only the unknown-parent clause can see that pair,
exactly as it sees a remove against a write inside the removed subtree. **So UpdateNode is
independent only of structure-free scripts**, the same pinned over-approximation `RemoveNode` and
`MoveNode` pay, and two updates of different nodes are reported dependent. Tightening that is a
change to the `Footprint` record, not to a clause — `TreeOps` section 18's argument applies
unchanged.

**The proof model carries it.** `TreeOps.op` gained the case, with its apply and footprint clauses.
Without it, `Skeleton.skeleton_fold_confluence` would quietly be about a sub-alphabet of the
shipped union again, which is the boundary Phase 162 removed. Because the footprint relocates, the
diamond's six new pairs close by `relocating_forces_inert` (`update_is_relocating`), with no new
commutation lemma. `Preservation` carries the rejection characterisation, `apply_preserves_wf`,
`invert_applicable` and `contained_preserves` for it (`upd_found_self`, `upd_contained`).
`Skeleton.update_lane_folds` and `update_pair_halts` are the evaluated rows: an update lane folds,
and two update lanes halt.

**Class.** The surface gate prints `union-widening` for `Fuaran.Core.Ops`. `0.31.0` is tagged, so
the change advances the slot to `0.32.0` (STABILITY.md "0.32.0 — DRAFT"). What adopting it costs a
consumer with an exhaustive `match` on `SkeletonOp` is one arm, and that entry names it.

## 2026-09-26 — D64: the prior value is an argument of the propagation driver, and the agreement theorem gains exactly one premise; `evalFrom` keeps refusing an unknown change

**Decided (operator, 2026-09-26; executed by Phase 250).** `Propagation.evalWith` and
`evalFromWith` take an evaluator `resolve -> prior -> id -> Result`, and hand a recomputed node its
own value from the evaluation that produced `prior`. The source was a downstream
spreadsheet-shaped consumer's measurement (Phase 250). Its table nodes refreshed only the rows an
edit reached (`DataFrame.Incremental`), and that needs the node's previous incremental state, which
`evalFrom` never passed. The consumer kept one state per node beside the driver and kept each in
step with its source by hand. Nothing certified that bookkeeping, and an out-of-step state is a
silently wrong refresh.

**How theorem 11's hypotheses read for `evalFromWith`.** `evalfromwith_agrees`
(`proofs/Propagation.fst` section 7) states `evalFromWith ev1 prior changed deps = evalWith ev1
deps`. Its premises are `evalfrom_agrees`' own premises, taken at the evaluators' PRIOR-BLIND
readings (`blind ev = fun resolve id -> ev resolve None id`), with nothing weakened: `agree_off`
and `touches_off` (the change set is complete), `prior_of` (`prior` is `evalWith`'s own output over
the same map, or fewer of its values), and a duplicate-free order. It adds exactly ONE premise,
`prior_blind_along`: **the evaluator's answer does not depend on the prior it is handed**, at every
node the incremental walk recomputes, under the resolver the walk hands it there. The premise is
stated along the walk and not for every resolver, deliberately. An evaluator whose prior carries
reuse state keyed to the inputs it was built from is entitled to trust that state beside the
resolver the walk builds, and nowhere else. A "for every resolver" premise would be false of
exactly the evaluators the feature exists for.

The premise is about the evaluator, which is the model's parameter, so it is a DOMAIN obligation.
It is the `propagation-prior-blind` row of `proofs.json`, discharged by a domain's green run of
`Conformance.propagationEvaluatorLawsWith`. That family samples the prior discipline at the answers
the full evaluation gives, which are the answers the incremental walk hands a node by the theorem
itself. Equality in the theorem is the VALUE TYPE's. A value that carries a reuse cache defines its
equality over what it means, not over the cache, and the family's own reference sheet does exactly
that.

The shared walk is a lemma too: `eval_is_walk_with` shows that `eval` and `evalFrom` are the
prior-aware walk at an evaluator that ignores its prior. So every theorem already proved about them
stands unchanged.

**The refusal stays.** The phase text also asked that "`evalFrom` reports rather than refuses an id
absent from the graph". That half is DECLINED. `Propagation.changedForOp`, shipped by the same
phase, returns `dirtyFromOp` over the pre-edit tree restricted to the post-edit ids, so it never
names an absent id. The consumer's finding offered the two remedies as alternatives, and the defect
was that Core offered nothing that computed the post-edit set, not that it refused an absent id.
The refusal is a proved clause (`evalfrom_unknown_refused`) and a certified law arm (the
unknown-change arm of `propagationEvalLaws`, guarded in `SampleAdequacy`). It is what catches a
domain's mistyped change set, and a mistyped id is indistinguishable from a removed one. A report
field would have been a second closed-record break (`EvalOutcome`) for a case `changedForOp`
removes. `evalFromWith` refuses identically, so there is one contract, not two.

One law moves with it, in the permissive direction only. `propagationEvaluatorLaws`' change-set
honesty law no longer holds a node the edit REMOVED from the map to being named. Nothing evaluates
that node after the edit, and `changedForOp` leaves it out. Its readers are still held.

## 2026-09-25 — D63: a tree-typed slot gets a value space — ruling (A), carried as a new `ValueSpace.SlotTree` case; (B), refusing at `register`, is declined

**Decided (operator, 2026-09-20; executed by Phase 229, shape confirmed 2026-09-25).** Ruling
**(A)** on the Tidy-Up bundle from Phase 177, "a SlotHole makes a capability un-invocable". A slot
is a legitimate capability parameter, and it gets a value space. The space is a wire document whose
`"kind"` matches the slot's constraint, or any kind when there is no constraint. Core owns no node
type, so the space is stated over the WIRE and checked by shape. The argument is the document's
JSON string, and decoding it into the domain's node is the host's job, per the witness pattern.
`Function.signature` enters every `SlotHole c` as `Space = Some(SlotTree c)`. `validateArgs`
accepts a conforming tree. It refuses a tree of the wrong kind as
`ArgOutOfSpace(addr, SlotTree c, got)`, which names the address and the constraint, and refuses
anything that is no tree as `UninvocableArg addr`.

**The finding it closes.** Phase 177's `slot_hole_uninvocable`: `signature` entered a slot as a
required entry with no space, and such an entry refuses every argument list. A capability declared
over an artifact with a slot registered, enumerated and never dispatched. The model now proves the
positive statement, `slot_hole_invocable_in_space`, over a completeness lemma
(`validate_args_complete`, the converse of `validate_args_sound`). The old statement survives as
`spaceless_required_uninvocable`, and after this change it describes only a hand-built entry.

**The shape: a new `ValueSpace` case, and why it is breaking.** The shard asked for two things
that meet at one point. `InvokeError` must stay unchanged, and the refusal must name the
constraint. The one existing refusal that carries a space is
`ArgOutOfSpace of addr * space: ValueSpace * got`, so the constraint has to be expressible as a
`ValueSpace`. Adding a case to a closed union is a union widening, which is BREAKING. It rides the
open `0.31.0` breaking draft, which was already advanced by Phase 220, as the shard allowed. The
operator confirmed that on 2026-09-25, and `STABILITY.md` states the migration: add a `SlotTree` arm
to every exhaustive match.

**The wire does not move, deliberately.** A slot entry's space is derived from its `Slot`, so the
tool schema and the capability codec omit it, and decoding restores it. So the bytes and the
`ContentPack.signatureFingerprint` of every slotted signature are unchanged. That matters because a
packed function records its base's fingerprint as `BaseSignatureVersion` and refuses to load on a
mismatch. Writing the derived space would have silently orphaned every pack authored against a
slotted base. A test pins the literal pre-229 bytes.

**Alternatives not taken, recorded so they are not re-proposed.**
- **A new `InvokeError` case** (say, `SlotKindMismatch`), with slots left spaceless. The shard
  ruled it out (`InvokeError` unchanged), and it would have been a union widening anyway, so it buys
  no compatibility. It would also have left `signature` entering a slot with no space, which is the
  state the finding named.
- **Refusing with an existing space as a stand-in** (`ArgOutOfSpace(addr, Enum [k], got)`). It adds
  no case, but the refusal would then name a space that is not the slot's space: an `Enum` of kind
  strings, which the argument (a document) never lies in. A refusal that is false about its own
  payload violates `refusal_is_truthful`'s promise, and was rejected for that reason.
- **Emitting the slot's space on the wire.** Rejected for the fingerprint reason above.

**Declined: (B), refusing at `register`.** `Registry.register` (or `Capability.create`) could have
refused a capability whose signature has a required spaceless entry. That is non-breaking on the
type surface, and it would make the defect loud. But it would make a capability over a slotted
artifact impossible rather than invocable, so every host with a slotted template would lose the
capability instead of gaining the call. The ruling is that the slot is a parameter, not a defect.
Recorded so that (B) is not re-proposed as the "non-breaking" alternative: it is non-breaking only
because it forbids the case outright.

## 2026-09-25 — D62: per-language Core packages are cut on a named trigger — a SECOND domain needing Core semantics in that language — and not before

**Decided (operator, 2026-09-25).** This repository is the implementation of `Fuaran.Core` for .NET,
and the reference implementation and specification owner for every other host language. The
UI domain's TypeScript, Python, Go and Rust hosts each reimplement the Core subsystems they need
(the `Transform` / DataFrame evaluator, `FunctionRegistry.findBySignature`,
`Capability.invocationKey`, list-parameter substitution and the lenient-ingest rules) as twins inside
their own packages. They certify against the law vectors and wire corpora this repository is the
reference for.

**A per-language Core package (`fuaran-core-<lang>`) is NOT cut now.** Every non-.NET port of a Core
subsystem today serves one domain, so each package would open with a single consumer: the host it
was carved out of. That is structure ahead of demand. The second domain to reach a browser consumes
this repository's own code compiled to JavaScript with Fable, not a TypeScript reimplementation, so
for JavaScript the Fable route is a live alternative to a port.

**The trigger.** Cut `fuaran-core-<lang>` when a SECOND domain needs Core semantics in that language
by a route other than Fable. That is the first point at which two consumers would otherwise each
carry their own twins of the same subsystems. Until then the extraction is PREPARED, so that it will
be a copy and not an untangling:

- Each host keeps its Core twins behind an internal boundary that imports nothing from its domain's
  packages, held there by a test rather than by convention.
- This repository emits EVERY law set it is the reference for, so a future per-language Core
  package has its certification suite on its first day.

**Declined:**
- **(1) Cut all four packages now.** Each would have one consumer, and add a release rhythm, version
  pins and a publication path per language.
- **(2) Leave the twins where they are, with no boundary.** A later cut would then be an untangling
  across domain packages, and a second domain in the same language would reimplement the twins a
  second time.

## 2026-09-25 — D61: `Required` means non-null — ruling (A), refused as a DISTINCT `RequiredParamsNull`; (B), a doc-only correction, is declined

**Decided (operator, 2026-09-25; Phase 226).** Ruling **(A)**. A `Required` query parameter that is
present but bound only to `Null` is refused before any resolver runs. On the error's shape the
operator took the STRICTER of the shard's two readings. The refusal is a new
`QueryError.RequiredParamsNull of names: string list`, so a caller can tell "present but null" from
"missing" (`RequiredParamsUnbound`, whose meaning does not change). The change is BREAKING twice
over in the draft `0.31.0`: it changes what a public function accepts, and it adds a case to a
closed union, which breaks every exhaustive `match` on `QueryError`. `STABILITY.md` states both
breaks and the migration: declare the parameter optional, and add the match arm.

**The finding it closes.** Phase 187's `all_null_accepted`: the only required-params step asked
whether the NAME was a key of the argument map. So every declaration accepted the set binding each
param to `Null`, and a resolver was reached with its required param absent, against `QueryParam`'s
own doc comment. The model now proves the positive statement instead. `required_is_non_null` says
validation accepts EXACTLY the well-typed sets that bind every required name to a value.
`all_null_refusal_exact` says the all-`Null` set is refused as `RequiredParamsNull`, naming every
required param in declaration order, and is accepted only where nothing is required.
`null_required_truthful` says the refusal names only declared names that are bound, and bound to no
value.

**Why (A).** A default-deny seam (FGP 3) should refuse what its declaration says it requires. Under
(B), every resolver in every domain would re-implement the same null check, and removing that
repetition is what this substrate is for.

**The shape, and the alternative not taken.** The worker proposed widening `RequiredParamsUnbound`'s
MEANING to cover null-bound names, on the grounds that the seam already treats `Null` as absence and
that it would add no case. That was declined. It would have made one refusal answer two different
questions, and a caller that distinguishes a missing argument from an explicitly nulled one (a form
that sends every field, for example) could not. The step order is fixed: unknown or mistyped
binding, then missing, then null-bound. The new case is appended after `Timeout`, so no existing tag
moves.

**Duplicates.** A name bound more than once is bound to a value when ANY of its bindings carries
one (`has_value`). The model states that rather than inheriting `Map.ofList`'s last-wins rule.

**The capability seam is unchanged, deliberately.** The shard asked for the same rule "where its
value space admits an absent marker". `Capability.validateArgs` takes strings checked against a
`ValueSpace`, and no value space has an absent marker: `StringLen (0, _)` admits `""` as a value,
not as absence. So a bound required hole already carries an in-space value, and there is nothing to
refuse. Its doc comments change, and nothing else. `capabilityLaws` gains no law, because it would
restate the space check it already certifies.

**Declined: (B).** Correcting only the doc comments to call `Required` a presence check was cheaper.
It would have left the resolver owning null-handling in every domain. Recorded so that it is not
re-proposed as the "non-breaking" alternative: it is non-breaking only because it moves the defect
out of this repository.

## 2026-09-25 — D60: the capture key's pre-image becomes injective by an OUTRIGHT change — no versioned key scheme, because no host journals these keys

**Decided (operator, 2026-09-25; Phase 225).** Ruling **(B)**. `Query.invocationKey` and
`Capability.invocationKey` build their pre-image through ONE canonicaliser, `Hash.canonicalFields`,
and the new key replaces the old one. There is no versioned prefix and no read-both window.
`CapabilityPipeline.nodeInvocationKey` goes through the same canonicaliser: it had the identical
defect in the same file, and leaving it open would have been knowingly shipped debt. The change is
BREAKING in the draft `0.31.0` (a key's value is a contract). `STABILITY.md` states what a host with
a persisted journal does at the pin bump.

**The finding it closes.** Phase 187's `key_collision`: the pre-image spliced `name=value` pairs
together, so a string value could spell the next binding, and `[a = "1b=s2"]` and
`[a = "1"; b = "2"]` shared a key. The finding said both seams joined "on the empty string". That
was true of `Query` and of the pipeline's node key, but NOT of `Capability`, which joined on
`U+0001`. It collided anyway, because a value can carry that byte.

**The census the ruling rested on.** The shard assumed a persisted consumer: that the UI host
journals effects under this key in its SQLite sink, and that a key change would orphan them. That
premise is false. Every caller of either `invocationKey` outside this repository was searched for,
along with every call that passes a key as `OpStream.captureEffect`'s `eff` argument.

| Consumer | Threads the key into a capture? | Journal outlives a process? |
|---|---|---|
| The UI host's SQLite op-stream sink | No | Yes — its `op_invocation` table persists, but it holds a caller-chosen idempotency key (a command or request id), never this key |
| The UI host's in-memory sink | No | No |
| The UI host's AI-tools capability invoker | No — a doc comment says a host "should" journal under this key, and nothing does | n/a |
| The domain libraries and the coordination plane | No — doc-comment mentions of the capture seam only | n/a |
| Other language hosts (TypeScript, Go) | They reimplement the key, and test against a law corpus pinning ten literal capability keys | No |
| This repository's own laws (`capabilityLaws`, `queryLaws`, the pipeline family) | Yes, in-process, capture then replay | No |

**No persisted journal holds either key, so an outright change orphans nothing.** The cost the shard
did not price is the cross-host one in the last-but-one row. The law corpus is derived from the
pinned Core release and the ports must match it. That is follow-through at the consumer's pin raise,
in its own change-set, and is tracked there. It is not a reason to keep a colliding key here.

**The options declined.**

- **(A) A versioned key scheme** (a `#2#` prefix, the old form read and never written, one
  deprecation window). Declined because it protects nothing: no journal exists to read the old form
  from. It would also add permanent surface, a second key form every host must recognise, to serve
  a population the census measured at zero. If a persisted consumer appears before `0.31.0` is
  tagged, this is the decision to revisit.
- **(C) A refused precondition.** Keep the join, refuse at `validateParams` / `validateArgs` any
  value containing the join's characters, and keep `key_collision` as a pinned negative. Declined:
  it rejects legitimate values (a filter string with an `=` in it) to protect an internal encoding,
  which bends the wrong side. It would also have had to refuse `U+0001` on the capability seam and
  `=`, `L:` and `N:` shapes on the pipeline's.

**The encoding, and why a bare separator was not enough.** The README's "Next" item proposed "a
separator no `cellKey` can emit". A bare `U+0001` is not that: a `Str` cell can emit anything, and
the capability seam already demonstrated it. So every field is ESCAPED and TERMINATED:
- `U+0010` (DLE) is written before each `U+0010` and each `U+0001` the field carries;
- `U+0001` (`Hash.foldSep`) then ends the field.

A cell becomes two fields (a one-letter tag and its rendering), so no claim about distinct literal
prefixes is needed. The literal `∅` a `Null` used to render as is gone; a `Null` is tag `n` with an
empty payload.

**What is proved, and what it spends.** `invocation_key_injective` is proved in `proofs/Query.fst` and in
`proofs/Capability.fst` (where the key is new to the model): two argument lists with one sorted
canonical string are one sorted list and hold the same bindings. It needs no comparator premise.
Its premises are named once, in `key_premises`:
- the reading of a string as its symbols that `Chain.fst` already takes;
- the escaper premise, measured on the shipped function by a round trip through an independently
  written decoder over adversarial fields, with a bare separator as the go-red;
- on the query seam, injectivity of the int and float renderers on the model's carriers.

That last premise is FALSE at one pair, and the `query-renderers-abstract` row says so.
`canonicalFloat` renders `-0.0` and `0.0` alike, so those two keys agree. Production's own `Cell`
equality calls them equal too, and the closure test pins both facts together.

**A measurement lesson worth keeping.** The query differential compares the capture key byte for byte and
stayed GREEN under a `field` that only appended the terminator: its generator draws no character of
the encoding. The escaper case exists because of that measurement, and it goes red on that `field`.
## 2026-09-25 — D59: one payload for the graft-containment refusal — `DiffError.TargetNotAContainer` names its `target`, ruling (B); option (A) declined

**Decided (operator, 2026-09-20; executed by Phase 228).** Core refused the graft-containment
shape (a node holding children while `canHold` refuses it) under two cases:
`Rejection.NotAContainer of target: 'Id * kindTag` on the apply path and
`Diff.DiffError.TargetNotAContainer of parent: 'Id * kindTag` on the diff path. Both named the same
offender, but under two field names. The Tidy-Up bundle Phase 161 filed ("one refusal name for the
graft-containment shape") put two options to the operator:

- **(A) One CASE:** retire `TargetNotAContainer` and have the diff return the apply envelope's
  `NotAContainer`. **Declined.** `DiffError` and `Rejection` answer different questions (why no
  script exists, and why an op was refused). A diff error that borrowed the apply envelope would
  have to carry `Rejection`'s other cases into a result type that can never raise them. The
  difference is the ENVELOPE, and the envelope is correctly two types.
- **(B) One PAYLOAD:** keep both cases and give them one field name, `target`, with one definition
  behind them and a law holding them to the same trees. **Taken.**

**What landed.** The field is renamed `parent` → `target` (source-breaking for construction and
matching by name, so it rides the `0.31.0` draft, and STABILITY.md has the one-line migration).
The shard expected the surface gate to print `retype` for this. It does not, because
`api/Fuaran.Core.Ops.txt` records a union case by its field TYPES and not by its field names. So
the class is carried by the STABILITY entry and not by the baseline.
`Ops.firstUncontained` becomes `internal`, and `Diff.toOpsContained` calls it instead of re-stating
its lambda. The shard said the two already shared that predicate. They shared its TEXT, not its
definition, and after this phase they share the definition. `Conformance.diffContainedLaws` gains
the **refusal correspondence** law. Wherever the diff refuses with `TargetNotAContainer(t, k)`, it
builds the offending graft: the subtree at `t`, cut out of `after` and re-inserted under its own
parent. `Ops.applyContained` must then refuse that graft with `NotAContainer(t, k)`. The law skips
two cases rather than counting them: a ROOT offender, which has no parent to graft under, and a host
parent the predicate refuses once the graft is cut out of it. The second arises because a `canHold`
may read the child list (Phase 140's `child_blind`), and that insert would be refused at the parent
clause instead. At the reference witness the law is asked on 180 of 200 iterations. A predicate
that is not a function of the node turns it red (`OpsTests`, Phase 228). `docs/conformance-corpus.md`
records that the two Core classes map to one host class.

**The proof model follows the rename (operator, 2026-09-25).** `proofs/TreeDiff.fst`'s
`diff_error` spells the field `target` too, and `diff_contained_locates` projects it under that
name. This is one perturbation. TreeDiff verified on its first run, the oracle was re-extracted and
not hand-edited, and `proofs/check.ps1 -Runs 3` is green. The extracted oracle and the
differentials over it match POSITIONALLY, so no statement and no comparison moved. The rename keeps
the model's vocabulary equal to the package's.

## 2026-09-25 — D58: a snapshot at sequence zero carries the CONFIGURED genesis — ruling (A), the `...With` family; (B), refusing the boundary, is declined

**Decided (operator, 2026-09-25; Phase 227).** Ruling **(A) — thread the genesis.** Phase 191
found, and `compact_at_zero_needs_the_empty_genesis` proved, that `OpStream.snapshotAtOpt` wrote the
literal `""` as the boundary hash at sequence zero where every chain walker (`verifyChainWith`,
`firstChainBreakWith`, `appendWith`) starts from `cfg.Genesis`. Under a non-empty genesis, the
compaction at zero of an intact stream therefore failed `verifyAcross`. Neither shipped config
(`canonicalConfig`, `legacyActorConfig`) is affected, because both carry the empty genesis.
`StreamConfig` is a public record, though, so a domain could configure its own genesis and would meet
the defect on its first compaction at zero.

**What shipped.** Three public members were ADDED: `snapshotAtOptWith cfg`, `compactWith cfg` and
`compactChainOnlyWith cfg`. Each seeds the boundary at zero with `cfg.Genesis`. `snapshotAtOpt`
became `snapshotAtOptWith canonicalConfig`. `snapshotAt`, `snapshotAtChainOnly`, `compact` and
`compactChainOnly` keep their signatures and their bytes; each is the canonical config's
instantiation. No published member was retyped. A new parameter on an existing function would have
been a `retype`, and the class had to be `additive` to ride the 0.31.0 draft. The surface gate
reported the move as additions only. A digest vector in `Proofs.Oracle` pins the strict and
chain-only snapshots at zero of a fixed stream. The same case holds every canonical entry point equal
to its `...With` form, at both shipped configs and at every boundary.

**The proof side moved with it.** The model's `compact` in `proofs/Chain.fst` takes the genesis. The
split theorem `compact_preserves_verify` and its corollary `compact_verifies_iff_original` LOSE the
condition `n == PZero ==> genesis == ""`, which makes them strictly stronger. The negative theorem is
restated as the positive `compact_at_zero_verifies_under_any_genesis`, and the oracle is re-extracted.
The pinned production case moves from "the finding holds" to "the finding is closed". Under genesis
`g0`, production's `...With` family agrees with the model at every boundary in both modes and
verifies across at zero.

**Declined: (B), refusing `atSeq = 0` under a non-empty genesis with a typed error.** It would have
removed a legitimate operation, compacting at zero, to protect an internal constant. It would also
have added an error case to an existing `Result` for no gain in what a host can do. Nothing about
(A) needs a refusal, because the value a host needs at zero is already in the config it holds.

**What stays an OBLIGATION rather than a theorem: verify, then compact.** `compact` reads the
boundary record's hash and trusts it. So `compact_preserves_verify` gives the compacted stream's
verdict only over a prefix verified before it was discarded. (A) does not change that, and no
signature can. It is now stated where a host reads it: on `compact`'s and `compactChainOnly`'s doc
comments, in the README, and in `STABILITY.md`'s hash-chain integrity posture.

## 2026-09-25 — D57: a theorem over the CONCRETE pipeline driver is taken — Phase 154 supersedes, for `Fuaran.Core.DataFrame`, D14's "declined as a domain's cost" and Phase 203's `law-tested-by-design` exclusion

**Decided (operator, 2026-09-25; Phase 154).** `proofs/Pipeline.fst` models `Fuaran.Core.DataFrame`'s
counted pipeline driver — the closed `ColExpr` and `Transform` algebra with every payload type,
`evalPipelineWithInEnvCounted`'s `costOf` and `go` clause for clause, and `evalPipelineWithInEnv` as
the projection it is — over ONE parameter, the step evaluator, and proves it total
(`eval_total`), budget-monotone in the pipeline prefix (`budget_monotone`), bounded in expression
work under the §21.8 node limit taken as a hypothesis (`work_bounded`), and identical to the
uncounted entry point (`uncounted_is_projection`). The package therefore HAS a model, attributed to it
in `proofs/modules.json`, and the `Fuaran.Core.DataFrame` entry in `proofs/coverage-exclusions.json`
— Phase 203's `law-tested-by-design`, which read "a theorem over the concrete pipeline is declined
as a domain's cost, per D14" — is RETIRED, because clause 1 of the coverage predicate fails an
exclusion for a package that has since gained a model, and because the sentence it carried is no
longer the decision.

**What this supersedes, exactly.** D14 (and D41, which carried it into `proofs/`) says a
VOCABULARY is the domain's contract and a proof over a domain's kinds is that domain's cost imposed
on every other commit. The exclusion read that rule onto the dataframe algebra. D49 has since
settled that the algebra belongs in Core and that the reference evaluator is its MEANING rather
than a runtime — so the evaluator's driver is substrate, not a domain's vocabulary, and a theorem
about it is a property of the engine that the substrate can honestly cite. The Program tier's
budget-monotonicity theorem (`fuaran#1716`) shipped with no Core-evaluator row, neither proving nor
axiomatising this driver; this entry is what lets a later consumer cite `pipeline-eval-total` and
`pipeline-budget-monotone` as stated rather than as an axiom about them. D14 and D41 stand
unchanged for what they are about: no vocabulary moves here, and the generated `proofs/` modules
still prove the F\* backend over the certification set and nothing about any domain's kinds.

**What is NOT taken, and stays a recorded decision.** The fourteen verbs' SEMANTICS —
`evalStep resolve env`'s dispatch to three-valued predicates, group aggregation, windows, pivots,
joins, the set operations and the pinned float layout — are the model's parameter, and every theorem
holds for every step evaluator. What the retired exclusion declined is therefore still declined,
only now as the model's own ladder row (`pipeline-step-evaluator-abstract`, a `model-bridge`,
`unscheduled`) rather than as an exclusion of the whole package: the laws the entry named
(`incrementalLaws`, `schemaWalkLaws`, `aggregateParityLaws`, and `transformLaws` at every host)
remain where the verbs are held. `unscheduled` rather than `permanent`, because a verb can be
modelled verb by verb on the Phase 176 precedent and nobody has asked for one.

**Two findings the theorem read off the tree, recorded rather than fixed.** (1) `Limits.max_expr_nodes`
(§21.8, 512) is enforced NOWHERE under `src/` — `over_limit_not_refused` says so as a theorem, and
the differential asserts it on the shipped evaluator with a 513-node expression. Whether a
conformant host must refuse such a pipeline before evaluation is §21.2's question for a later
operator act; enforcing it changes what a public function accepts and touches the hot path the phase
was chartered to leave alone. (2) The `counted_agrees` the phase was chartered with relates one path
to itself: `evalPipelineWithInEnv` is `evalPipelineWithInEnvCounted |> Result.map fst`, so the ladder
carries the identity and no agreement lemma. Zero-impact held: nothing under `src/` changed.

**How this was decided.** The phase ran as an operator-requested same-task trial — two workers on
two models from one base commit, and the operator chose which arm lands — and the retirement of
the exclusion is the one act in it that outlives the phase, which is why it is an entry here and not
a line in a note. Theorem 14 in `proofs/README.md` carries the statements, the differential (the
sixteen `transform-laws.json` vectors and four hundred generated pipelines reaching every verb and
every expression kind) and the cost; `proofs/coverage-exclusions.json` keeps the retirement under
`$retired` and points here.

## 2026-09-24 — D56: a member's READ is a named, opaque reader with one value lemma — the k=16 round trip discharges under `--z3rlimit 40` (continues D54)

**Decided (Phase 224).** A conditional member of a suffixed constructor (D52) whose read calls
nothing in the decoder's mutual family is decoded through a named, top-level, opaque READER,
`rd_<T>__<Ctor>__<member>`. Such a member is a scalar, a verbatim value, a sentinel, or a closed
string set, whose decoder is top-level. The decoder applies the reader where Phase 222 inlined the
read. The proof script emits one VALUE lemma per reader, `rv_<T>__<Ctor>__<member>`, beside the
lookups: the reader on the encoded object is `Ok` the member. It is proved by revealing the reader
and citing the member's two lookups. The round-trip arm cites that lemma in place of its two-way
presence case. This is D52's move for the encoder, applied to the decoder, which is the candidate
D54 named. Only suffixed constructors get readers, so a vocabulary with none emits byte-identical
text (`DocVocabulary` does).

**What it is measured to buy.** Same synthetic probe as D52 and D54, pinned prover, one
perturbation per run, `--query_stats`:

| k | round-trip arm, 222 → 224 | slowest lookup, 224 (222) |
|---|---|---|
| 5 | 1.12 → 0.179 | 0.172 (0.19) |
| 8 | 2.92 → 0.220 | 0.244 (0.24) |
| 12 | 24.4 → 0.273 | 0.344 (0.34) |
| 16 | **345 → 0.328** | 0.446 (0.446) |

The mechanism D54 named is CONFIRMED, not merely consistent with the data. The first measurement
was the one change alone, hand-applied to 222's k=16 output, beside an unchanged copy in the same
pass. The copy reproduced 345.36, and the change gave 0.328. The emitter's version was then
measured at `--z3rlimit 40` at all four widths and agrees. The k=16 round-trip clause D54 carried
forward is met: nothing admitted, no statement weakened, the rlimit still 40. The clause is
therefore NOT retired, and no operator decision about retiring it is needed.

**Why the body-side remedies could not work, now that this one has.** D54's two perturbations
changed what the arm's query was TOLD about each read (a presence split, then the read's value). The
read itself stayed in the query's term, because the decoder's body is what the query unfolds. A
fact about a term does not stop the solver from working through the term. Making the term opaque
does.

**The boundary, stated.** A member whose read CALLS the family (a record, union, node, list or map)
cannot be defined above the family, so it is read inline and keeps its two-way citation, as in 222.
No vocabulary here carries a wide constructor of such members, so its cost at width is unmeasured,
not known to be cheap. The obvious extension, if one is ever wanted, is a reader of the member's
PRESENCE alone, `get_prop` refined by `jsize`, opaque and non-recursive, with the family decoder
applied to its result. That is named here and not built: nothing measured asks for it.

**Not re-proposed.** D52's seven remedies and D54's two round-trip perturbations stay refuted.
## 2026-09-24 — D55: the Fable compiler leaves this repository; both legs of the Fable gate RELOCATE to `fuaran-dotnet`, neither is retired, and every version cut cites the relocated run

**Decided (operator ruling on Phase 217.A, 2026-09-24).** The operator rule of 2026-09-21: the Fable
compiler belongs in applications and in the language's .NET implementation, not in the substrate.
This repository gated its "Fable-clean on encode AND decode" claim with two in-repo legs, and they
prove different things, so each was decided on its own:

| leg | proves | ruling |
|---|---|---|
| `dotnet fable` over the smoke | every public package transpiles | **RELOCATE** to `fuaran-dotnet` `tests/core-fable/` |
| node value parity | the transpiled code computes the same BYTES as .NET | **RELOCATE** beside it, **never retire** |

**Why the parity leg is not retired, with the two recorded defects answered by name.** A compile
cannot disagree about a number, and neither can a .NET suite. `Hash.fnv1a` was value-divergent
between the pipelines behind a fully green suite until `0.6.0`. `Wire.Json.render` THREW under Fable
for any float — a public encode surface, unusable in a browser — from before `0.5.0` until Phase 118,
with the compile green throughout. A third instance is recorded in the table itself: the `ConfRng`
LCG before `0.20.0` drew zeros under Fable after its first draw. Retiring the leg would leave nothing
able to catch any of the three: every consumer's compile was green through all of them.

**The census the ruling rests on (217.B, measured 2026-09-24).** Of the smoke's 17 members (18 when
the phase was filed; Phase 188 took `Lease` out first):

- 9 are Fable-compiled by shipping browser code in `fuaran-dotnet` and `fuaran-live`;
- 7 only inside `fuaran-dotnet`'s law harness, through this repository's Conformance kit;
- `Fuaran.Core.Idl` by no Fable consumer at all.

**Idl keeps its claim**, and the relocated smoke references it for no reason but to gate it. The
Phase 97 split exists to make exactly that claim (STABILITY.md, the IDL engine's section). It is not
the lease case (D51): the lease algebra had a measured server-only consumer set and no design intent
to reach a browser. **No published guarantee changes** — the claim moves where it is checked, not
whether it holds.

**The vector table ships as CODE, in the conformance kit — `Fuaran.Core.ParityVectors`, ADDITIVE,
riding the `0.31.0` draft.** The shard proposed shipping the vectors "as data". That premise is
false: the transpiled side has to COMPUTE them, and the consumer sees this repository only as
packages at its pin. Inside Conformance, the table reaches the consumer in a package it already
Fable-compiles, and is version-coherent with the surfaces it measures by construction. A tracked
copy in the consumer was the alternative, and was declined: it could run ahead of the consumer's pin
and stop compiling against it.

**The retired `tests/hash-parity-probe` is ABSORBED, not dropped.** The vectors did not cover it.
The probe's 124-input corpus runs four implementations — the canonical `Hash.fnv1a`, `sha256Hex`, and
the two deliberate FNV copies in `OpStream` (the chain hash) and `Column` (`Schema.fingerprint`) —
while the table had five `fnv1a` inputs and no `Column` copy. So the corpus joined the table as
`hashSweep/*`, pinned here by row count and digest, and the probe is gone because it is covered, not
because it was inconvenient.

**§M — the latency cost, and the rule that narrows it.** A relocated gate fires when the consumer
raises its Core pin, so a divergence would surface in another repository a release later. So:
**every Fuaran.Core version cut cites a green run of `fuaran-dotnet`'s
`tests/core-fable/core-fable.ps1 -CoreVersion <candidate> -CoreFeed <folder>` against the candidate
packages before the release gesture.** That run:

- restores every Core package from the candidate folder alone, into an isolated cache, so a
  same-version repack can never be served stale;
- derives the surface from what the candidate actually ships;
- REQUIRES the parity leg.

The cut is where the divergence is caught, before any consumer can pin it. The rule is recorded here
and in STABILITY.md's release section, and not in `verify.ps1`, because it is a property of the
release gesture rather than of a commit.

**Until the consumer's pin reaches `0.31.0`, its default run cannot compile the table** — `0.30.0`
predates it, and the pin must stay publicly restorable. Its gate therefore:

- says so on every run, naming this rule;
- reads whether the table is present off the RESTORE, not off a version number;
- FAILS if a pin at or above `0.31.0` restores a Conformance package without the table (the
  tripwire). The decision table is proven at the start of every run.

**Membership splits (§C).** The half that needs no Fable stays here:

- `fable-exclusions.json` at the repository root (moved from the smoke, whose PROJECT is gone; it is
  the scope of THIS repository's claim);
- the retargeted `FableSmokeCompletenessTests`: every packable project ships the `fable/` source
  distribution or carries an entry, in both directions.

The consumer holds its own completeness check over its pins, and over the candidate's packages in a
cut-time run. The residual gap is a new package the consumer does not pin yet. It is not silent: the
cut-time run derives the surface from the candidate, so the new package fails by name at the first
cut that ships it.

**217.F, redrawn, and checked rather than asserted.** Taken literally, "no authored `Fable.Core`
reference" would have removed `Fuaran.Core.Wire`'s. That reference powers Wire's own
`#if FABLE_COMPILER` float-layout emit, which is the FIX for the `Json.render` defect above, and a
Fable consumer of Wire needs it to compile that arm at all. **Wire's is the one sanctioned authored
`Fable.Core` reference** — a library's reference powering its own Fable arm, not tooling. The suite
now fails on:

- any script or workflow here that invokes `dotnet fable`;
- a `fable` entry in the tool manifest;
- any authored `Fable.Core` reference other than Wire's.

The line is the compiler and an authored tooling reference; a transitive `Fable.Core` is a restore
artefact.

**Proved red in the receiver (217.E).** Against a candidate `0.31.0` with `Hash.(.+.)`'s mask
removed, the relocated leg reported 40 of 164 vectors divergent, `sha256/two-block` first — the
vector chosen in Phase 118 as the leg's go-red anchor. Against the clean candidate it was green,
164/164. With no `node` on PATH it failed by name.

## 2026-09-24 — D54: a suffix chain's argument is an APPLICATION, not a `match` — the k=16 lookups discharge; the round trip is the next wall, and it is not the same mechanism (continues D52)

**Decided (Phase 222).** Every link of a suffixed member list (D52) now passes its member to its
suffix through a per-slot option encoder in the encoder's mutual family: `enc_opt_<slot> v` for an
optional member, `enc_dflt_<slot> (d) v` for an omit-at-default one (the default is an ARGUMENT, so
one encoder serves every default of a slot). Phase 204 wrote the `match` / `if` inline in that
position. The lookup lemmas keep their statements and their shape: they re-bind the same chain and
cite the same steps, now at the application. Only the slots a suffixed constructor's conditional
members carry get an encoder, so a vocabulary with no suffixes emits byte-identical text.

**What it is measured to buy.** D52's synthetic probe (one kind, k optional string members, one
always-emitted list member after them), pinned prover, one perturbation per run, `--query_stats`:

| k | first lookup, 204 → 222 | every lookup under `--z3rlimit 40` (222) | the round-trip arm (222) |
|---|---|---|---|
| 5 | — | green, max 0.19 | 1.12 |
| 8 | — | green, max 0.24 | 2.92 |
| 12 | 3.7 → — | green, max 0.34 | 24.4 (204's emitter: 27.6) |
| 16 | **58.6 → 0.150** | **green, max 0.446** (114 queries) | **345** (204's emitter: red at 400) |

The mechanism D52 named is CONFIRMED rather than merely consistent: the first measurement was the
one change alone, hand-applied to 204's k=16 output (a top-level transparent `enc_opt_s`), and it
moved the postcondition from canceled-at-40 to 0.150. The emitter's version (the encoder inside the
family) was then measured separately and matches it. The k=16 LOOKUP clause of Phase 204's task 2
is met: nothing was admitted, no statement weakened, the rlimit is still 40.

**What it does NOT buy: the round trip at k=16, which is a SECOND wall and not this one.** 204
stopped at the first red lookup and so never reached the round-trip arm. With the lookups green,
the k=16 probe script is red on one query, the Grid arm of `rt_vkind`, at every fuel/ifuel retry.
It is pre-existing: 204's emitter fails it at rlimit 400. The growth is ~1.7x per member (1.12 /
2.92 / 24.4 / 345 at k = 5 / 8 / 12 / 16). Two perturbations were measured against it, and
neither is the fix:

1. the arm's sixteen two-way `match fN` replaced by applications of a per-member lemma whose
   ENSURES carries the presence match: the query goes from 50 goals to 2, and it is still
   canceled at 40;
2. the same, with each per-member lemma stating the DECODER's own read of that member equals
   `Ok fN` (no presence case in the statement): 271 at k=16, against 345.

So this wall is NOT the body's VC split, which is the mechanism this entry removes from the
lookups. The cost is in the solver working through the decoder's 17-deep `outcome` nest over the
inlined member reads, even when it is handed each read's value. **The next candidate, unmeasured:**
the move D52 made for the encoder, applied to the DECODER. Each member read becomes a named,
top-level, opaque reader, with one lemma per reader stating its value off the encoded object. The
round trip then threads 17 opaque applications instead of 17 inlined reads. Whether the k=16
round-trip clause is worth that generator work, or should be retired as an acceptance criterion
at this width, was the operator's call. The operator ruled that it gets a successor:
**Phase 224 carries the k=16 round trip under `--z3rlimit 40`, with this candidate
named.** This phase does not retire the clause.

**Not re-proposed.** The seven remedies in D52's list stay refuted; none of them is used here.
The round-trip arm's two perturbations above join that list.

## 2026-09-24 — D53: the wire surface gets a committed baseline of its own, over a DERIVED document set — not a view over the law corpus

**Decided (Phase 214).** Each wire-bearing package commits `api/wire/<package>.txt`, beside the
managed baseline in `api/`. The file holds the canonical bytes of a document set that reaches every
member name and every discriminator the package emits. Each document line carries the sha256 of the
bytes the package emitted and the document re-rendered canonically. The `Wire surface` test family
re-encodes the set on every gate run and fails when a byte moves without its baseline. The
regeneration step, `CORE_APPROVE_WIRE=1 dotnet run --project tests/Fuaran.Core.Tests`, writes the
CLASS of the move into the baseline's own header. A second test holds that stated class to a
recomputation. So a canonical-encode change cannot land without the baseline moving, and the baseline
cannot move without a class that is checked rather than asserted.

**Why not a view over the law corpus.** `conformance/laws/transform-laws.json` was read first, as the
phase asked, and it cannot serve.
- It pins BEHAVIOUR: a host evaluates the vectors and compares result tables, not document framing.
- Its sixteen vectors reach six step kinds (derive, filter, sort, distinct, groupBy, limit) and
  three expression kinds (col, binary, lit). They carry no `project` step, so the very move this
  phase was filed for (Phase 213's `cols` → `columns`) is outside it.
- Widening it into a framing corpus would put two purposes behind one emission. Every framing change
  would then re-emit a file that a declared byte copy in the shared corpus must follow: the coupling
  D50 and the 0.30.0 entry exist to contain, bought for no gain.
- It covers one package of the eleven that emit.

So the baseline is a second artefact, and says so.

**Why the document set is derived and not authored.** A hand-written set is exactly as complete as
its author's attention on the day it was written, and the next case added to a union sits outside it
with the gate green. That is the silent failure a baseline exists to prevent.

The set is therefore built by reflection from each package's ROOT document types, in the test file's
`roots`. For every union reachable from a root, and every case of it, one document is built:
- each enclosing union routes toward that case, choosing a case that reaches it without re-entering a
  union already on the path;
- every record field is populated;
- every option is `Some`, so every optional member is spelled.

A suite test reads the builder's construction log and requires that every per-case document really
built the case it is named for, since a route that silently fell back to a default would name a
document after a case it does not contain.

A case added tomorrow is in tomorrow's set without anyone listing it, and so it MOVES the baseline:
additive, and stated. Two roots are specimens written by hand, because reflection cannot build their
values: the row codec (a `Row` cell is `obj`, and its RUNTIME type is the discriminator) and the
AI-surface catalogue (its witness carries functions). The code says so where they are declared. As
cut: 11 packages and 420 documents.

**The classes, and why they are these.** They follow the managed baseline's pattern and the phase's
statement:
- **Additive:** a new member or a new case.
- **Breaking:** a renamed or removed member, a changed discriminator, or the same structure rendered
  to different bytes.
- **Not a wire move at all:** an F# case renamed with its bytes unchanged. That is the managed gate's
  business.
- **No class of its own for a reordered member.** It cannot occur in `Canon.render` output, which
  sorts keys. In the two emitters that are not `Canon.render` (the IDL artifact's indented form, and
  the hand-assembled stream envelopes), the hash is what sees layout, and a layout move is breaking.

A member is named by its PATH through every `$type` it meets, so `{project}.cols` and
`{project}.columns` are different members, and a rename reads as one member removed and one added.

**What it deliberately does not pin.**
- **Decode.** A consumer pins what a package EMITS; what it ACCEPTS is a superset by design. A decode
  alias is therefore outside the baseline, and adding one moves nothing, which a test holds.
- **A domain's vocabulary.** `Idl.Encode` renders a domain's values under the domain's own
  vocabulary, and an op stream embeds the domain's op encoding verbatim. Those names are the domain's
  to pin.
- **Packages that emit no document of their own.** Each is named with its reason in `notWire`, and a
  roster test refuses a packable package that is in neither list.

**Why the stated class is anchored to the tag it NAMES.** The header reads
`# class since <tag>: <class>`, and the check recomputes the class against that tag's own copy of the
file. If it were anchored to the newest tag instead, cutting a release would stale every header at
once. That would be one more re-stamp a version move must carry, the coupling D50 was written about.
The check adds one condition so an old statement cannot hide a new move: a baseline that has moved
since the newest tag must state its class since THAT tag.

**Measured, and proved able to fail.**
- **Phase 213's rename, replayed.** On a scratch copy, the pre-213 encoder spelling was restored and
  a baseline cut from it. With the 213 encoder back, the gate fails `breaking`, naming
  `$[]{project}.cols` and the window sort key's `orderBy[].col` as removed-or-renamed in every
  document that carries them, in `Fuaran.Core.DataFrame` and again in `Fuaran.Core.Column.Ops`, whose
  `applyTransform` op embeds a pipeline.
- **A wrong stated class.** A header stating `additive` where the recomputation says
  `first snapshot` fails, naming both.
- **In-suite classifier tests** hold that an added optional member reads additive, and that a new
  case reads additive. They also hold that a changed discriminator, a re-rendered value, and bytes
  that moved under an unchanged structure each read breaking. And they hold that the decode alias
  `cols` re-encodes to the very bytes the baseline pins.

## 2026-09-23 — D52: the model's member list is bound as named, opaque SUFFIXES — the MODEL's exponential D42 named, removed; the mutual-family split is not the successor; k=16 lookups spill over

**Decided (Phase 204).** A constructor with `FStarTarget.presenceSplitAt` (two) or more conditional
members no longer encodes its member list as an inline test per conditional member with the tail
written into BOTH arms. Each conditional member gets one top-level SUFFIX, `sfx_<T>__<Ctor>__<member>`,
which takes that member's encoding as an option (`None` exactly when the encoder omits it) and the
REST of the list, and conses the member onto the rest or passes the rest through. The encoder binds
the suffixes as a `let` chain, one `let` per conditional member, innermost first, each naming the next
— so no tail is written twice and the emitted model text is LINEAR in k. Each suffix is
`[@@"opaque_to_smt"]`: nothing sees inside one except its three lemmas in the proof script (`__skip`: a
different key passes through; `__hit`: a present member is found; `__none`: an absent member leaves the
rest), each proved by revealing it once. Every `lk_*` lookup keeps its Phase 182 statement exactly — the
round-trip family that cites them is unchanged — and is now PROVED as a chain over the named suffixes:
a negative lookup is k steps instead of one query over the 2^(k-1) shapes of the tail behind it — each
step cheap, and the one query that remains measured below.
Below the threshold a constructor still encodes inline (one conditional member writes its tail twice:
a constant factor, not an exponent) and is still proved in one query, which D42's k=5 measurement shows
is the cheaper shape while it holds.

**What it is measured to buy.** The same synthetic vocabulary as D42 (one kind carrying k optional
string members and one always-emitted list member after them), pinned prover, `--z3rlimit 40 --quake 3`,
this dev machine:

| k | model text, Phase 182 → 204 | model check | lookups under `--z3rlimit 40` |
|---|---|---|---|
| 5 | 11,666 → **10,509** chars | green | all green |
| 8 | 34,831 → **12,279** | green | all green |
| 12 | 427,037 → **14,664** | green | all green — including the `__absent` D42 recorded as failing at fuel 30 and 60 |
| 16 | 6,694,957 → **17,056** | **green, 19 s** (D42: `allocation failure` after 719 s) | the forty-eight per-suffix steps green; the FIRST lookup RED |

**What it does NOT buy, and why that is a spillover rather than a quiet weakening.** The k=16 lookups
do not discharge at the leg's rlimit: the first one's postcondition costs 58.6 units (3.7 at k=12,
green at 400). The exponent MOVED — out of the model's text and into the lookup's verification
condition: the lookup body re-binds the chain with `let`s whose arguments `match` on each member, a
lemma body is a computation, and F* splits the VC on both arms of every such `match`. The same chain
in a specification is a term and costs nothing (an unfold lemma stating it proves at 0.175). Seven
remedies were measured one at a time and none discharged it — fuel, ifuel, the unfold lemma, both
together, per-suffix quantified facts with scoped patterns (with and without the unfold lemma; the
trigger never met the unfolded term), and `x`-indexed steps with the chain in their `ensures` — each
recorded in the README. The lookups keep their statements and nothing is admitted; the k=16 clause is
filed as a spillover, and the lead the diagnosis points at (an application in the argument position
instead of a `match`: a per-slot option encoder in the family) is named there unmeasured.

**Why suffixes and not the mutual-family split.** The README had named the family split as Phase 168's
successor since Phase 168. D42 measured that it addresses the wrong quantity: the family split bounds how
much of the mutual family each query carries, and what failed at k=12 and k=16 was the WIDTH of one
constructor — the model's own text and one lookup's walk — inside a family of two types. Splitting a
family of two changes nothing there. It remains the lever for a proof vocabulary widened past the node
envelope's closure (the SCC argument in the README still holds), and is named there as that and no
longer as this phase's successor. Nor is this Phase 150's refuted `opt_cons`: that was ONE shared helper
with an SMT-patterned lookup law, and the pattern fired throughout the encoder. A suffix carries no SMT
pattern and is opaque; its lemmas enter a query only where a lookup cites them by name.

**What it costs, said plainly.** The proof script now grows as k^2 per wide constructor, because a
lookup for the t-th conditional member cites t steps and re-binds the chain (147,376 characters at k=16,
where Phase 182's shape emitted 24,829 over the same probe — and 78 MB before it). That is the price of
making each step a separate cheap query, and at the widths an adopter has measured it is small. The
committed certification models regenerate with suffixes at the three constructors that reach the
threshold (the reference envelope and `Embed`; the score sample's `Measure` and `Score`); the
second-domain sample has none, so its model is byte-identical.

## 2026-09-23 — D51: membership in this substrate is genericity over the witness, never present consumer count — and D9 keeps the question it was actually answering

**Decided (operator ruling, 2026-09-16; recorded by Phase 188).** The rule, verbatim:

> Core membership is decided by genericity over the witness and plausible cross-domain use, never by
> present consumer count. Generic tree functions stay with one consumer (`arbitrate` included —
> `fuaran-dotnet` certifies `arbitrationLaws` over its own witness). `Projection` and `Propagation`
> stay because their shape is cross-domain. A domain-specific strand goes to its domain library.

**It supersedes D9's framing as the MEMBERSHIP test and leaves D9 standing as the SHIPPING test.**
Two different questions were being answered by one sentence. *Does this shape belong in a
witness-generic substrate* is a question about the shape; *is there enough evidence to ship it now*
is a question about demand. A consumer count can answer the second — that is exactly what D9's
single-unblocking-consumer exception is for — and nothing about a count can answer the first. Read as
a membership criterion, a count says a strand earns its place here by being wanted, which would admit
any sufficiently-wanted domain type and is the opposite of guiding principle 1.

**The reading runs in both directions, which is why the rule is worth recording rather than applying
once.** A count-based membership test is wrong when it says *out* and equally wrong when it says *in*:

- It would evict a generic tree function that happens to have one consumer today. `arbitrate` is the
  live case — one caller, and a tree-generic partition of op-script proposals that any tree host can
  instantiate, certified over `fuaran-dotnet`'s own witness. It stays. (It changed PACKAGE at
  `0.28.0` — `AiSurface` to the op algebra, D46 — which is a question about where a generic function
  sits, not about whether it belongs.)
- It would admit a strand that is over the wrong axis because enough people want it. That is the
  case this entry pays for.

**What it costs, and the cost is this entry's evidence.** `Fuaran.Core.Lease` (Phase 84) shipped under
D9 on one concrete consumer — the coordination library that hand-rolls the claim/coordination the
strand replaces, named in D9 in exactly those terms. On 2026-09-16 the consumer census showed the
same single family and nothing else: the corroborating adopters D9 called "corroboration, not
prerequisites" never arrived. Under a count test that census is the whole argument and it points at
removal for the wrong reason — few consumers. Under this rule the census is not the argument at all.
The argument is the axis: `LeaseOp<'Res>` is generic in its type parameter and its only instantiation
is a claim over a **resource** axis, which is not the tree and is not a witness this substrate
defines. It sat here because a lease PRIMITIVE is publishable where lease AUTHORITY — the fold, who
may claim — is not, and a licensing convenience is not a genericity argument. So it leaves
(Phase 188, `0.30.0`, BREAKING), and it would leave on this rule even if the census had shown ten
consumers.

**What deliberately does NOT move with it.** `Ops`' footprint and independence, and `Arbitration`:
tree-generic by shape, and the whole reason this rule had to be stated separately from the count that
was standing in for it. Lease authority was never here to move.

## 2026-09-21 — D50: a stamp-only corpus mismatch stays FATAL, and the discovery moves to where the version moves

**Decided (operator ruling, 2026-09-21; Phase 216).** `conformance/laws/transform-laws.json` carries
a `kitVersion` DERIVED from `<Version>`, and the shared corpus holds a declared byte copy of it, so
every move of `<Version>` restales that copy in a different repository. The question put was whether
a mismatch in that stamp ALONE should stay fatal in CI, or become a warning with the vectors'
equality being what fails. **It stays FATAL. `.github/workflows/ci.yml` is therefore unchanged** —
fatal is the status quo, and editing the workflow would have been a move away from the ruling rather
than toward it. It was read and confirmed to match rather than assumed to.

**Why the warning was declined.** That leg is the only thing that notices the corpus has drifted at
all. Demote it and the first mismatch nobody re-stamps becomes indistinguishable from the first
mismatch that MATTERS — a genuine content divergence between Core's laws and the copy the conformant
hosts certify against. That trades a noisy true signal for a silent false negative, on the one file
whose whole job is to be the shared oracle. The residual cost is accepted and named: `main` can red
for the interval between the Core push and the corpus push, because two repositories with two
permission sets cannot be pushed simultaneously.

**And option (A) remains declined, from earlier.** The 2026-09-15 bundle offered a stamp-INSENSITIVE
fingerprint — teach the workspace copy registry to ignore `kitVersion` for every producer — and the
operator declined it. It would buy the same quiet by making the registry structurally unable to see a
stamp move on any copy of any file, which is a wider blast radius than the problem and removes the
evidence rather than the noise. Phase 216 leaves the registry alone and works on the Core side of the
coupling only; `kitVersion` is not removed either, because two hosts read it.

**What the three incidents actually recorded, which is what the phase fixes.** Not that the check is
fatal — that the re-stamp is a separate manual act, in a separate repository, discovered in CI
minutes after the version move that caused it. On 2026-09-21 that ran three times in one day (the
`0.28.1` draft, six consecutive red runs on `main` before anyone looked; a hand re-stamp; then the
advance to `0.29.0`, red again on the very next commit — by a session that had predicted the coupling
in its own deviations and still could not act on it, because nothing failed where the change was
made). So the DISCOVERY moved:

- **The `laws/` copy-freshness leg is decided by the corpus's PRESENCE, not by the ask.** A corpus
  checked out beside this one is compared on every ordinary run and the finding is REPORTED, naming
  the two stamps and both re-emit commands. `FUARAN_CORE_CORPUS_FRESHNESS` keeps exactly one job:
  deciding whether a finding is fatal. With no corpus the leg says NOT CHECKED, by name, and a
  machine holding only this repository is still green. D31 is untouched on the branch it governs —
  asked for and absent still FAILS.
- **The two readings are separated.** Vectors-differ and stamp-only are distinct in the classifier
  and in the report, and a copy whose stamp AND vectors have both moved reads as the divergence — a
  version move must never be able to hide a content divergence behind it. Both go-reds are proved in
  the suite rather than asserted.
- **It is reported locally and fatal only where asked.** Considered and declined: making a finding
  fatal locally too. A local hard failure over a SECOND repository's checkout state is the "red gate
  you did not cause" class — a contributor whose corpus clone is merely behind would be blocked on a
  repository they may not own — and the ordinary reason a copy is behind on a developer's machine is
  that they have not pulled it. CI is the one place that can reasonably demand both repositories be
  in step, and it does.

**The leg's equality is unchanged and is still the registry's.** The reading refines
the copy registry's `fingerprint` rather than inventing a neighbouring notion of freshness: a
reading is taken only once the fingerprints have already disagreed, and it is taken from the
registry's own normalisation. Doing that surfaced a defect in the normalisation itself, fixed here —
`StartsWith(string)` compares by the current culture, under which U+FEFF is an IGNORABLE character,
so the BOM test answered true for every text and quietly removed the first character of any document
that had no BOM (and threw on an empty one). Both sides of a comparison lost the same character, so
no freshness leg was ever wrong; what they had was a hole exactly one character wide in the one
equality this repository's published copies are held to, and a comparison that could not tell an
empty document from a crash.

**Not in scope, deliberately.** The `apply/` copy-freshness leg stays opt-in: the class this ruling
closes is a DERIVED stamp restaling a copy on every version move, and `apply/` carries no stamp at
all. Making the engine's `version cut` emit the re-stamp itself — which is what would close the
interval rather than merely announce it — is engine-side work, outside this repository, and filed
separately.

## 2026-09-20 — D49: the dataframe algebra belongs in Core, and the reference evaluator is its MEANING rather than a runtime

**Decided (Phase 213; the operator's decision of 2026-09-19, recorded here for the first time.)**
`Fuaran.Core.DataFrame` stays in this repository. It arrived in the initial public release with no
entry arguing its placement, and the question has been asked often enough — a declarative-compute
layer looks like an application concern — that the silence has become the problem.

**Three reasons, in the order they bind.**

1. **It has several consuming domains, and none of them is above the others.** A user-interface
   tier binds a transform pipeline to a view; a presentation tier charts the result and carries the
   pipeline in its own wire; an application-composition tier runs one as a data-flow leg. Homing the
   algebra in any one of them would force the other two to reference that tier's package in order to
   describe a data pipeline — a dependency on a vocabulary they do not otherwise use, taken for a
   type they do.
2. **It sits on this repository's own spine and on nothing else.** `Fuaran.Core.DataFrame`
   project-references exactly `Fuaran.Core.Column` and `Fuaran.Core.Wire`, and package-references
   exactly `FSharp.Core`. `Column` over `Wire`, `DataFrame` over `Column`, with `Column.Ops` and the
   incremental seam built on top: the layering is already here, and the algebra is its top course.
3. **In the consuming tiers it is a BINDING, never a node.** Nothing in those domains' own trees has
   a dataframe shape, so relocating the algebra into one of them would not consolidate a vocabulary —
   it would put a vocabulary those trees do not contain inside the package that defines them.

**The honest reading of the reference evaluator against the no-evaluator principle, checked rather
than asserted.** This repository's stated rule is that the core carries no evaluator, no clock and no
scheduler, and `DataFrame.fs` plainly contains something called an evaluator, so the entry would be
worthless without confronting that. The reading HOLDS, and it is stronger than "it happens to be
pure":

- **`evalPipelineWithInEnv` is total in its result type** — `Result<Table, EvalError>` — and the
  module raises nothing: there is no `failwith` and no `raise` anywhere in the file. A pipeline the
  algebra cannot answer produces a typed refusal, which is a VALUE of the algebra.
- **It performs no I/O and holds no host.** The three references above are the whole dependency set;
  nothing in the module touches the filesystem, the network, the environment or a process.
- **It does not read a clock — and it REFUSES to.** A `ColExpr.Now` is resolved by substituting a
  caller-supplied `ClockWitness` into the pipeline BEFORE evaluation; a `Now` that reaches the
  evaluator with no clock pinned is a named error case, not a read of the ambient time. The clock is
  an input to a rewrite, never an effect of the fold.
- **The one seam that could carry an effect is a PARAMETER, and its default refuses.** `resolve:
  string -> Result<Table, EvalError>` is how a caller supplies a source a `Join` or `Union` names by
  reference; the library's own default is `noResolve`, which errors. A caller may of course pass a
  function that reads a database — but that is the caller's effect, at the caller's boundary, and it
  is why the seam is a parameter rather than a capability the module holds.

So what the module contains is not a runtime. A serializable algebra whose steps have no pinned
answers is a vocabulary rather than a language: the reference fold is what `Transform` MEANS, and the
conformance family `transformLaws` exists precisely to hold an independently-written host evaluator to
that meaning byte for byte. Removing it would not remove an evaluator from the core; it would leave a
wire format with no definition. The no-evaluator principle bars a core that RUNS a domain's programs
against the world, and this runs nothing against anything.

## 2026-09-20 — D48: a column-naming wire member of the dataframe algebra is spelled out, never abbreviated

**Decided (Phase 213; operator, 2026-09-19.)** The naming rule the algebra follows, in one sentence:

> A wire member whose only honest name is "the column" or "the columns" is spelled out in full —
> `column` for one, `columns` for a list — and never abbreviated; every other member is named for the
> ROLE its columns play in the step, and an abbreviation survives only as a decode alias.

**What it moves, in `0.28.0`:**

| object | canonical was | canonical is | decode alias |
|---|---|---|---|
| `project` step — the list of column renames | `cols` | **`columns`** | `cols` |
| a `sort` key's column, and a `window`'s frame-ordering entry | `col` | **`column`** | `col` |

**What it leaves, and why that is the rule rather than an exception.** The survey covers every wire
member of `DataFrame.fs` that holds a column name or a list of them. All of the following name a
ROLE, so the rule does not reach them and none of their aliases change: `groupBy.keys` (alias `by`),
`sort.by` (alias `keys`), `window.partitionBy`, `window.of`, `window.as`, an aggregate entry's `of`
(alias `column`) and `name` (alias `as`), `derive.name`, `join.on`, `pivot.index` / `on` / `values`,
`unpivot.idVars` / `valueVars`, and the `col` expression's `name`. The flat filter shorthand's
decode-only `column` already spells out and is consistent as it stands.

**Why the singular flips too, when it would have been cheaper not to.** `column` was already admitted
as an alias of `col` on a sort key, in the other direction to this change. Leaving that as it stood
would have produced "singular abbreviated, plural spelled out" — a distinction no reader would guess
and no author could apply to the next member. The choice was therefore between two rules, not between
a rule and a smaller change, and the rule that generalises is the one that costs the same version
bump either way.

**Two things the rule deliberately does NOT reach**, recorded so they are not read as oversights:

- **A `$type` tag.** The `col` EXPRESSION is `{"$type":"col","name":…}`, and `col` there names a KIND
  of expression, not a column; the rule is about MEMBER names. Renaming the tag would move the bytes
  of every predicate in every document — a far wider break than the one decided — for a vocabulary
  (`groupBy`, `isNull`, `rowNumber`, `ntile`) that is not the subject of this rule. If it is ever
  wanted it is its own decision.
- **A pair's `a` / `b`.** `pairJson` renders both a `project` rename and a `join` key, and `a` / `b`
  name a POSITION in a pair rather than a column. Giving them meaningful names is attractive, but one
  naming cannot serve both uses — source/target in a rename, left/right in a join key — so it is a
  separate design question and not part of this rule.

**The alias's lifetime.** Each alias is kept until a major version says otherwise, is accepted on
decode, and is NEVER emitted; a document carrying both spellings is refused as ambiguous rather than
silently resolved, which is the behaviour every other aliased member of this algebra already has. A
document written before `0.28.0` therefore keeps decoding, to the same tree, and normalises to the
canonical spelling the first time it is re-encoded.

**The cost, and why now.** The canonical encode's BYTES change, which breaks a consumer that compares
them — a conformant host's round-trip corpus does — while breaking no decoder and moving no managed
type, member, record field or union case. A correct name gets cheaper never: the algebra's consuming
domains, published versions and stored documents all grow monthly, and every month of delay adds
bytes that say the old thing. The argument is about timing, not about demand.

## 2026-09-19 — D47: the relocation-kind footprint widening is DECLINED — measured, and the one relocation kind anybody records is the kind no record can free

**Decided (Phase 163; the operator's note at accept was "investigate further before building", and
the measurement is what decides it.)** `Footprint` keeps its four fields. `Ops.footprint` and
`Ops.independent` are unchanged, byte for byte. `proofs/TreeOps.fst` section 18 stands as the
ceiling, not as a placeholder for work now due.

**What was being proposed.** Phase 143 proved that `Ops.independent`'s pinned unknown-parent clause —
a non-empty `UnknownParentWrites` refuses independence against any structural write — cannot be
tightened over this record. `UnknownParentWrites` carries only the relocated node's id, so a
`MoveNode` and a remove-shaped `Batch` present byte-identical footprints, while only the move commutes
with a structural write inside the relocated subtree (`relocation_disjoint_diamond` versus
`relocation_diamond_fails_for_a_remove`). Any predicate over these four sets frees both or neither.
The named remedy was a FIFTH address kind: relocations carrying their kind, and a move its target
parent. That is a change to a public record and to every projection onto it.

### The measurement

**Method.** For a pair to be refused by the pinned clause at all, one side's footprint must carry a
non-empty `UnknownParentWrites`, which `Ops.footprint` populates for exactly two op shapes —
`RemoveNode` and `MoveNode`. So over each recorded ledger, count:

- **T** — cross-lane op pairs examined;
- **C** — pairs the pinned clause could possibly refuse (a relocation-shaped op on one side, a
  structure-writing op on the other). Halts attributable to the clause are a subset of C;
- **M** — of those, the ones a kind-aware record could possibly free: every relocation involved is a
  MOVE. A remove is section 18's fact 2, which proves it does not commute, so no width of record frees
  it.

M is the ceiling on the widening's value. Both counts are UPPER BOUNDS, by two deliberate
over-approximations pulling the same way — any two ops in different lanes are treated as concurrent
(real concurrency is narrower), and C asks only whether the pinned clause COULD fire, not whether the
other four clauses already refuse the pair. Bounds that can only inflate are what make a zero
conclusive. The instrument is `proofs/kit/measure-relocation-halts.ps1`; it takes ledger paths and a
projection map as arguments and reads committed bytes only, so it runs offline against anything.

**Population.** 72 recorded op-stream ledger files from the maintainers' own working repositories — eight
independent stores, their concurrent write lanes, their single-chain bases and their frozen
predecessor archives. 55 carried ops and every op in all 55 was classified; the other 17 are signing
and approval artefacts holding no ops. No line failed to parse. Each op was classified through the
projection its own host computes footprints with, transcribed arm for arm from that host's single
exhaustive `'Op -> Footprint` match.

**Result. T = 20,649,689 · C = 0 · M = 0.** Of 16,780 classified ops: 230 structural writes, **zero
move-shaped relocations, and 22 remove-shaped ones — all 22 in frozen predecessor archives that are
single linear chains with no concurrency at all.** So the pinned clause is not merely rarely decisive
in these repositories; it is VACUOUS. Every op on every live lane has an empty `UnknownParentWrites`, which
makes `independent`'s last two clauses vacuously true, so no recorded fold was ever refused by them.

**The falsifier, stated before the run, and the guards that answer it.** A zero is worthless if the
instrument cannot find a halting pair that is there. Three guards, all exercised by
`proofs/kit/measure-relocation-halts.tests.ps1` (15 assertions):

1. A **planted** move-versus-structural-write pair in a synthetic two-lane ledger must register as
   C ≥ 1 **and** M ≥ 1. If the planted pair does not register, nothing else in this entry means
   anything.
2. The **same probe run the other way**: a planted remove-versus-structural-write pair must register
   as C ≥ 1 and **M = 0**. A script reporting M ≥ 1 there would be measuring "relocations" rather than
   "relocations a wider record could free", and would have inflated this result.
3. **An op kind the projection map does not classify is counted as UNCLASSIFIED, reported BY NAME, and
   its ledger downgraded to PARTIAL or SKIPPED — never rendered as zero halts.** This guard is the one
   that earned its keep: the first run over the real ledgers, using the built-in skeleton vocabulary,
   correctly reported every ledger SKIPPED rather than printing the reassuring `C = 0` it had computed.
   The result above is from a run in which the PARTIAL and SKIPPED counts are zero and nil-with-ops
   respectively, which is why its zero is a measurement.

A fourth check, not planned and worth more than the three that were: the instrument's 22 remove-shaped
ops reconcile exactly with an independent text search for that op's wire kind across the same trees —
two unrelated methods, the same 22, in the same two files.

### Why the answer is structural, and will not drift as the set of consumers grows

A count of zero invites "not yet". This one is a property of the projection rather than of the sample.
The consuming host's footprint projection has **exactly one arm** producing a non-empty
`UnknownParentWrites`, and it is a REMOVE. **It has no move-shaped arm at all** — not an unused one, not
a rare one. Its predecessor model did have a relocation op; that model was retired on the principle that
completion is a status and not a location, and its ops translate to no current op whatever, so they
carry no footprint and cannot reach a fold. So the widening frees moves, and nothing anybody records is
a move. Were the remove op to return to live use tomorrow, section 18's fact 2 proves a kind-aware
record still refuses it.

Two further bounds worth recording, because each independently caps what the widening could ever buy.
Section 18's `relocation_move_pair_also_fails` exhibits a refused pair that **no** record could free —
two moves nesting into each other's subtrees, where the obstruction is the validation and not the
addresses — so the refused set was never one homogeneous class awaiting a better footprint. And Phase
133's `relocating_forces_inert` shows a relocation is independent only of an op that does nothing
whatever, because every skeleton op but a structure-free `Batch` writes structure: the clause's reach is
total by construction, which is exactly why so little rides on sharpening it.

### The cost, which the proposal had mis-sized in BOTH directions

Checked rather than assumed, and the check moved the answer twice.

**Cheaper than proposed in one respect: `Footprint` is not a wire shape.** Nothing serialises it —
no codec, no schema, no fixture. It appears in no conformance corpus and in no wire specification. So
the widening carries no wire migration and no host-by-host adoption, which is what the proposal had
sized the breaking change against.

**More coupled than proposed in another, and this is the finding that matters.** The record IS
mirrored — by hand, in F\*: `proofs/DagFold.fst` declares its own four-field `footprint` and
re-implements `independent` clause for clause, and a byte-identical copy of that model is maintained
in a second repository's proof tree. Both extract to generated F# oracles. The four fields are
additionally pinned **with their ordinals** in `api/Fuaran.Core.Ops.txt`, so a field added anywhere but
last moves the baseline; and the record is reproduced verbatim, field names and all, in transpiled
JavaScript that reaches shipped production bundles. Beyond that, five independent `'Op -> Footprint`
projections construct it. So the true cost is a hand-maintained formal model that must move in lockstep
across a repository boundary, plus five projections, plus an ordinal-bearing surface baseline — to free
a set measured at zero and argued above to be structurally zero.

**Rejected: widen it anyway, because section 18 names the remedy.** Section 18 proves a ceiling exists;
it never claimed the ceiling binds. Building the remedy to a proof of impossibility, with the benefit
unmeasured, is how a correct theorem funds work nothing needs — and this repository's own debt posture
forbids shipping a mechanism whose value nobody can state.

**Rejected: widen it speculatively, for a future domain whose writers relocate nodes.** A domain like
that is exactly what would make this worth doing, and the measurement would then say so — the
instrument is committed and takes its ledgers as arguments, so re-running it is the cheap act. Widening
a published record for a consumer who does not exist trades a real cost now against a hypothetical
benefit later, and leaves the wider record to be maintained in the F\* mirror meanwhile.

**Rejected: record this only in a phase outcome.** The next reader to meet section 18 will have the
same idea, and a ceiling with a named remedy and no recorded verdict is one they will spend their
budget re-deriving. That is what section 18 itself says about ceilings nobody proved.

**Not added: a new assertion to catch the world changing back.** One already exists — the Phase 143
teeth-check in `tests/Fuaran.Core.Tests/ConcurrencyTests.fs` erases exactly `UnknownParentWrites` and
requires the run to go RED, with a comment saying that a green there means either the generator stopped
producing relocations or the algebra moved under the theorem. That is the assertion this decision would
otherwise have had to invent, and it is already in the gate. A second one asserting the same fact would
be two things to keep true.

## 2026-09-19 — D46: `arbitrate` belongs to the op algebra, not to the AI surface — and a package name that names four things does not get a fifth

**Decided (Phase 192; operator decision 2026-09-16 on the `AiSurface` placement question.)**
`arbitrate`, `ArbitrationRejection` and `Arbitration` move from `Fuaran.Core.AiSurface` to
`Fuaran.Core.Ops`, as `Arbitration.arbitrate` over a new minimal record
`OpScriptProposal<'Node,'Id>` = `{ Id; Holder; Ops }`. `AiSurface.Proposals` keeps its queue and
gains `toOpScript`, the one-line projection. Nothing about what the function decides changes. The
adoption cost and the ride-not-advance argument are in [`STABILITY.md`](STABILITY.md) under the
`0.27.0` draft.

**The argument is about what the package NAME promises.** `Fuaran.Core.AiSurface` answers one
question — what does a model need in order to read a domain artifact and propose changes to it — and
its four parts are four halves of that answer. Six per-domain `*.AiTools` layers adopt it under that
reading, and `AiSurfaceWitness` is frozen over exactly those four. Arbitration answers a different
question: given N op scripts and one base tree, which subset can land together. Its inputs are
footprints, its output is a partition, its callers are schedulers, and no model is involved at any
point. It is the other end of `Ops.footprint` and `Ops.independent` — the concurrency half of the
tree algebra whose first half already lives in `Ops`.

**Why it landed in the wrong package, which is the part worth recording.** It landed beside the
proposal RECORD. `arbitrate` needed a list of things with an id and an op list; `Proposals.Proposal`
was a thing with an id and an op list; so the function was declared where that type was. That is a
reason about where a record sat, not about what the function is — and it is a very easy reason to
act on, because it presents as the absence of friction rather than as a choice. The record was doing
two jobs (a human-approval lifecycle, and an identity for the partition to sort by), and splitting
it is what let the function go where it belonged. **The generalisable form: when a function seems to
belong beside a type, check whether it needs the whole type or three fields of it. A function that
needs three fields of a six-field record is telling you there are two records.**

**Rejected: renaming `AiSurface`.** The name is correct for what remains, six domains' `*.AiTools`
layers are named after it, and `AiSurfaceWitness` is in the 1.0 field freeze. A rename would have
broken every adopter to fix a problem one function had.

**Rejected: leaving it and documenting the oddity.** The cost of the move is two call-site edits in
two coordination-layer consumers, both of which repin within this release anyway. The cost of not
moving it is paid repeatedly and by people who did not choose it: a new adopter looking for
concurrency semantics reads the package that does not have them, and a reader of the arbitration
theorems is told they are about an AI surface. A one-time cost falling on two known callers beats a
permanent cost falling on every future reader.

**Rejected: `Ops.Arbitration.arbitrate`, which is what the phase was written as.** `Ops` is a
module, and F# does not let a second file re-open one; the only way to spell it that way was to put
arbitration inside `Ops.fs`. The module is therefore a PEER of `Ops` in the same package and the same
namespace — `Fuaran.Core.Arbitration`, exactly as `Fuaran.Core.Diff` already is — which reads
`Arbitration.arbitrate` under the `open Fuaran.Core` every consumer already has. It carries
`[<CompilationRepresentation(ModuleSuffix)>]` so the module coexists with the `Arbitration` type,
the pattern `Column`, `DataFrame`, `Function` and `OpStream` all use.

**Verbatim was measured, not asserted.** Both implementations ran over the same 300 generated
proposal sets from the law kit's own generators before the old one was deleted — zero disagreements,
and the same differential against an inverted pinned order disagreed on 282 of the 300, so the
comparison was shown able to fail before its passing was believed.

## 2026-09-19 — D45: the class of a surface move is COMPUTED at the gate, and the gate refuses an unclassified move rather than a breaking one

**Decided (Phase 183).** Every packable package carries a committed baseline of its public contract
at `api/<package>.txt`, rendered from the built assembly's IL metadata, and the `Public surface`
test family diffs each package's freshly-rendered surface against it on every gate run. A move is
classified — `removal` / `retype` / `record-widening` / `union-widening` / `interface-widening` /
`additive` — and the gate fails when a surface moved and its baseline did not. See
[`STABILITY.md`](STABILITY.md) "Public-surface baselines" for the vocabulary and what each class
costs a pinned consumer.

**Why a gate at all.** The draft-slot rule asks one question of every commit that touches a
package: does its public contract move, and in which class. On 2026-09-15 that question was
answered twice by hand, in two workers' deviations records — "additive, and it advances because the
slot is tagged"; "additive, rides the draft". Both were right. Neither was checked, and neither
could be: this repository had no surface baseline, so the only available instrument was a reading
of the diff by the person who wrote it.

**The gate is on the CLASSIFICATION, not on the class.** Additive or breaking, a classified move
passes. This is the 2026-08-04 record-widening dispensation implemented rather than restated: the
repository's own policy permits widening, so a gate that refused a breaking class would be enforcing a rule nobody
made. What it refuses is a surface that moved while its baseline stood still — the state in which
no reviewer can apply the dispensation, because nothing says what there is to permit.

**Rejected: a removal-only differ, which is the obvious reuse.** The shape already in use elsewhere
renders a flat token list and calls a removed token breaking and an added one additive. Half of
what motivates this phase is invisible to it. Adding a case to a closed union REMOVES NOTHING — it
emits a factory, an `IsCase` property and a `Tags` literal — so the reading is "ordinary growth",
which is exactly the reading that lets a union widening occupy an unchanged feed slot and reach a
consumer as an `InvalidCastException` with no compile signal anywhere. So the renderer marks the
three F#-specific shapes that are source-breaking while looking additive, off the
`CompilationMappingAttribute` the compiler already emits, and the classifier checks each addition's
OWNER against the baseline — a field or case on a type the baseline never published is additive,
because nobody could have constructed or matched it.

**Rejected: rendering through `MetadataLoadContext` or ordinary reflection.** Several packable
projects are not referenced by the test project, and both alternatives need the whole dependency
closure resolvable — or the assembly loadable — before they can name a parameter's type.
`MetadataReader` needs no resolution at all: a type from another assembly is named from its
TypeReference row. That is the stronger property here and it is what lets one family cover every
package rather than the subset the suite happens to link.

**Rejected: renaming the whole thing a "no breaking changes" gate.** The surface is not the
semantics, and saying so once here is cheaper than having it inferred. A function whose signature
is unchanged and whose behaviour reversed passes this gate and always will.

## 2026-09-17 — D44: the generator's LAST two string channels carry their refusals — `fsharpTypes` and `jsonSchema` return `Result`, because the alternative was a throw wearing a different name

**Decided (Phase 195).** Guiding principle 3 admits no exception as a rejection, and
`Fuaran.Core.Idl.Codegen` held thirteen of them while every other refusal in the same file was
already a `CodegenError`. Removing the eleven that sat in `Result`-returning functions is
bookkeeping. The other two are a decision, because they are what forced the two published emitters
`Gen.fsharpTypes : Idl -> string` and `Gen.jsonSchema : Idl -> string` to become
`Idl -> Result<string, CodegenError>` — a BREAKING change to a second public surface, on a phase
whose declared impact was a DU growth.

**The shape of the problem.** Five recursive emitters — `fsTypeIn`, `encFn`, `decFn`, `tsEncFn`,
`tsDecFn` — each carry a `TKind | TOp` arm for a slot no backend emits. F# requires an expression in
every arm, so an arm that cannot produce a value has exactly three endings: throw, return a
falsehood, or change the function's type. The first is what GP3 forbids. The second is worse than
what it replaces — a generated module referencing a type that does not exist compiles nowhere, and a
JSON Schema missing a `$def` leaves a dangling `$ref`, which a strict validator treats as an error
and not a permissive skip, so a partial emission is a green build that certifies nothing. So the
type changes, and it changes all the way up: `fsTypeIn` feeds `fsharpTypes`, and the schema leg's
instantiation walk feeds `jsonSchema`.

**The alternative that was considered and rejected: a private exception carrying the typed
`CodegenError`, raised in the deep arms and caught at each entry point.** It is small, it leaves
both signatures alone, and every published `Result`-returning entry point would still hand its
caller a value. It was rejected because it relocates the exception rather than removing it: the two
string-channel emitters still throw, so the property "no path out of this package raises" would be
false while reading as true, and the next reader has to discover a second refusal mechanism to learn
that. A guiding principle that holds only where the return type already allowed it is not a
principle the code obeys; it is one the code happens not to have been asked about.

**What the breakage is worth.** In-repo the change costs four test call sites. Outside, a caller
adapts with `|> Result.defaultWith (CodegenError.describe >> failwith)`, which is the behaviour it
had, with a typed value behind it. It rides the open `0.26.0` draft slot, whose class was already
breaking — the DU growth — so it advances no number of its own (the draft-slot rule).

**A second decision inside the first: a Required node-envelope member is EMITTED when a default is
declared for it.** The refusal narrowed rather than moved, because the full node envelope needs
exactly that shape: a required member that always carries a value the smart constructor can fill.
A declared envelope default is addressed by the EMPTY `IdlDefault.Kind` — the envelope has no kind
tag, and a kind's tag is its `$type` discriminator on the wire, so the empty address is free and can
name nothing else. This was preferred to widening the published `IdlDefault` record with an
envelope flag, or minting a second defaults list on `Idl`: both are breaking changes to the MODEL
package to express something the existing address already distinguishes. `IdlCodegenRefusalTests`
pins the discrimination in both directions — a default declared against the KIND does not satisfy
the envelope member.

**What is deliberately NOT done.** The op-emission leg stays unshipped; a `TKind` / `TOp` slot is
refused by name in each backend that meets it, which is what tells the phase that wires ops in
exactly where to look. Making them emittable is that phase's work, not this one's.

## 2026-09-17 — D43: a refused columnar op has NO inverse, and the refusal is the refusing rejection

**Decided (Phase 181).** `ColumnOps.invert` is guarded by `canApply` on the pre-state. An operation
the table would refuse yields that refusal — `Error (DuplicateColumn "a")` for a duplicate insert,
not an operation — where until now the `InsertColumn` clause answered `Ok (RemoveColumn col.Name)`
unconditionally, having read nothing. This closes the gap [Phase 176](proofs/README.md) found and
deliberately reported rather than fixed: the "inverse" of a refused insert was a remove that
SUCCEEDED at the pre-state and took the column that was already there, so an undo stack recording
`invert op pre` beside every op it attempts lost a column the refused operation never touched.

**Three things about it were decisions rather than consequences, and each had a plausible
alternative.**

1. **The refusal is the REFUSING rejection, not `NotInvertible`.** The shard's prose could be read
   either way. It is the refusing rejection because that is the tree engine's shape — `Ops.invert`
   returns `canApply`'s `Rejection` — and because the alternative would make
   `invert_refuses_as_apply` false in the other direction, replacing an inverse that lied about the
   operation with a refusal that lies about the reason. `NotInvertible` keeps one meaning: *this
   operation has no inverse at any table*, which is `AppendRows`' and `ApplyTransform`'s alone. The
   proof README's own statement of the fix shape, written by 176, names `DuplicateColumn` as the
   refusal, and this follows it.
2. **The two never-invertible operations answer BEFORE the guard, so the guard is not literally on
   every clause.** `canApply (ApplyTransform p)` runs the pipeline, and `invert` must not evaluate a
   user pipeline to report what it already knows; the answer for those two does not depend on the
   table at all. The alternative — one guard at the top, no exceptions — is more uniform and was
   rejected for that cost, and because it would have made `invert_not_invertible` conditional for
   nothing: the defect is only ever in a clause that can answer `Ok`. The model carries
   `invert_ignores_evaluator` so the claim is checked rather than asserted.
3. **The guard strengthens all FOUR invertible clauses, not just the insert.** `SetCell` and
   `SetColumn` read the pre-state for the column and the row but never for the VALUE, so a
   wrong-typed cell or a wrong-length column — both refused by `apply` — had an inverse too. Those
   were harmless rather than destructive (a `SetCell` restoring a cell to what it already held),
   which is why 176's finding named only the insert. Narrowing the fix to the named instance would
   have left the class open.

**The finding is KEPT, not deleted.** `invert_insert_reads_nothing` and
`refused_insert_inverse_is_live` are now theorems about `invert_pre181`, the model's copy of the
clause as it stood, and the second states the shipped refusal in the SAME lemma as the old live
remove. A finding deleted at the moment it is fixed leaves nothing that goes red if the fix is ever
reverted, and two lemmas that can be re-proved independently let one half rot while the other
passes. The differential's fourth case asserts both halves on the shipped engine for the same
reason.

**The class is corrective, and it rides the `0.26.0` draft.** No signature moves, no wire byte
moves, no record gains a field and no DU gains a case — what changes is that a function refuses
where it wrongly answered. A consumer depending on the old answer was depending on the defect, and
the only shape that can notice is a caller that inverts an op it has not checked, which is precisely
the caller the change protects. It is not one of the classes the draft-slot rule says outranks a
draft (a required record field, a DU case reorder, a wire-shape break), so it appends to the
standing draft rather than advancing it.
## 2026-09-17 — D42: the presence split is LINEAR in the conditional members — and the exponential that remains is the MODEL emitter's, measured

**Decided (Phase 182).** A constructor with k conditional members is no longer proved one lemma per
PRESENCE PATTERN (Phase 168, 2^k lemmas) but one LOOKUP per MEMBER: `lk_<T>__<Ctor>__<member>` reads
one key off the encoded object with every OTHER conditional member left free — one lemma for a member
that is always emitted, two (`__present` / `__absent`) for a conditional one — and the constructor's
round-trip lemma cites them a member at a time. That is `2k + r'` lemmas, where `r'` counts the
always-emitted members that sort after the first conditional one; everything before it is reached by
`find_field` without meeting a branch and needs nothing. `FStarTarget.presenceSplitAt` is RE-PURPOSED
rather than retired: it was the count at which the per-pattern split began, and it is now the count at
which the linear split is used instead of proving the whole constructor in one query. It stays at two,
so the certification set exercises the shape rather than an adopter meeting it first.

**What bought it.** `fuaran#1754`, the kit's first adopter, measured Phase 168's shape at the UI
vocabulary: 71,722 lemmas in a 114 MB, 713,272-line proof script, one kind with sixteen conditional
members contributing 65,536 of them. Re-emitting the same synthetic scale here, the script goes from
78,192,374 characters and 655,507 lines to 25,847 characters and 333 lines — the same theorem, three
thousand times less of it.

**What it is measured to buy, and where it stops.** Pinned prover, `--z3rlimit 40`, a synthetic
vocabulary whose widest kind carries k optional string members, this dev machine:

| k | one query per constructor | per-pattern (Phase 168) | per-member (Phase 182) |
|---|---|---|---|
| 5 | 29 s green | 96 s green (64 lemmas) | **17 s green** (14 lemmas) |
| 8 | FAILS at fuel 32 | did not finish in 62 min; 33.7 GB resident when stopped | **64 s green** |
| 12 | not attempted | not attempted (3.99 MB script) | the first member's `__absent` lookup FAILS, at fuel 30 and at 60 |
| 16 | — | — | the MODEL alone cannot be checked |

**So the exhaustive-coverage ambition stays REFUTED at sixteen conditional members, and Phase 182
names the reason precisely where `fuaran#1754` could only name the symptom.** Two exponentials were
in play and only one of them was the proof shape's.

1. **The lemma count** — Phase 168's, 2^k, and this phase removes it.
2. **The encoder's own emitted TEXT** — `encMembers` writes the tail of the member list into BOTH
   arms of every conditional member's match, so a constructor with k of them emits an expression 2^k
   long: 5,318,686 characters for one kind at k=16, which is `fuaran#1754`'s 5,072,945-character model
   line reproduced. At k=16 the prover dies loading it — `Fatal error: allocation failure during
   minor GC`, after 719 s — with no proof script involved at all. A negative lookup has the same root:
   showing a key is ABSENT means walking the whole conditional tail, which is 2^(k-1) object shapes,
   and that is what fails at k=12.

Both belong to the MODEL emitter, which this phase deliberately does not touch (`Vocabulary.fst`,
`DocVocabulary.fst` and `ScoreVocabulary.fst` are byte-identical across it). The successor is
therefore NOT the mutual-family split the README has named as 168's successor since Phase 168 — that
addresses query breadth, and query breadth is no longer what binds — but a non-duplicating member-list
emission in the model (named suffixes, so the text is linear and a suffix is a term a lemma can be
stated about). `proofs/README.md`'s theorem 1 section carries the measurements.

**The lookup lemmas sit OUTSIDE the mutual family, and that is load-bearing rather than tidy.** They
recurse on nothing, so they need not be in it — and F* admits one option set per top-level
declaration, of which a mutual family is one. `find_field` pushes through a key it is not looking
for, but the default two unfoldings do not reach past the second key, so each lookup needs FUEL sized
to its constructor. Inside the family that fuel would be paid by every other query in the file;
outside it, each lemma is pushed under its own `--fuel` and the family keeps the leg's defaults.

## 2026-09-15 — D41: D14 governs `proofs/` as it governs `tests/` — the proof leg certifies the backend over the certification set, and a domain proves its own vocabulary

**Decided (Phase 173).** The generated F\* files under `proofs/` are emitted from the vocabularies
the engine is CERTIFIED on — `ReferenceIdl.refIdl` and the two vendored non-UI samples, the same set
`IdlCertificationTests` holds the F# and TypeScript backends to — and never from a domain's
vocabulary. What the proof leg proves is therefore the F\* BACKEND (`FStarTarget`): that the
decoders it emits are total and that the round trip it emits discharges, over both declared wire
shapes and every type case the reference vocabulary was authored to reach. A domain runs the same
generator over its own `Idl` in its own repository, where the cost is charged to the commits that
change its kinds; the UI vocabulary's model, proofs, cost and exhaustive-coverage decision are
`fuaran#1754`'s, the proof kit's first adopter. `--emit-fstar` takes its vocabularies from the test
project, the `Proofs.Vocabulary` generation diff reads no corpus, and nothing under `proofs/` names
a UI kind.

**Why this is D14 and not a new rule.** D14 said a vocabulary is the domain's contract, moves at the
domain's cadence, and does not live in the substrate; Phase 114 completed it for `tests/` by cutting
a neutral reference vocabulary so the engine's certification no longer rested on the UI one. Phase
150 then brought the UI vocabulary back through `proofs/` — `Vocabulary.fst` regenerated from the
corpus's `idl.json`, a 322–398s budget row in Core's cost ledger, and the round-trip theorems held
out of the leg because at that vocabulary's scale they did not discharge. Every one of those facts
was a fact about the UI vocabulary presented as a fact about the leg. The placement was a default —
the leg existed nowhere else — and this entry makes it a decision the other way: the rule that
governs which vocabulary `tests/` certifies against governs which vocabulary `proofs/` proves over,
for the same reason. A proof running in the substrate's CI over a domain's kinds is that domain's
cost imposed on every other commit, and its result is a theorem about one adopter that the substrate
cannot honestly cite as a property of the engine.

**Two premises the shard carried were checked against the tree and one was false.** (1) "The
reference vocabulary reaches every backend refusal class by construction, because it was authored
to reach every `IdlType`." Reaching every type case is not reaching every refusal class: most of the
backend's refusals are about an ILL-FORMED IDL — an undeclared record, a union arity mismatch, an
unresolved type parameter, a transparent case that is not one scalar, a default omitting a required
member — which no well-formed certification vocabulary carries. The one refusal a well-formed
vocabulary CAN reach at kind level is the numeric default, and the reference vocabulary reaches it
(`Measure.value`'s `Fixed { value = 0.0 }`; the score sample's `Note.voice` / `Chord.voice` reach it
too). `IdlFStarTargetTests` pins the corrected form: the three boundary kinds as exactly that set,
and every other refusal class to a hand-written case. (2) "`FStarTarget.proofKinds` is the selection
rule." It is a cost control sized against a vocabulary whose node envelope carries dozens of declared
types; over the reference vocabulary, whose envelope is two scalars, it keeps ONE kind of five and
drops the four the vocabulary exists for. The committed models cover every expressible kind, the
rule is left unchanged as the adopter's instrument, and the one-of-five result is pinned so nobody
re-takes the measurement.

**What the measurement changed about Phase 150's conclusion.** Phase 150 committed the model alone:
the emitted round trip did not discharge at twenty UI kinds — a 65-goal node query failing a
`--quake` seed, a 2^k blow-up in the widest kind's arm — and a `.fst` that does not verify is worse
than none. Phase 150 also recorded WHY: the cost is the node envelope's closure, not the kinds. Over
the certification set that envelope is two scalars and k = 2, and all six generated modules check
cold under the leg's own flags in seconds (`proofs/modules.json`; the reference pair in ten or under).
So the proof scripts are committed, registered in `$modules`, budgeted, and carried as `proved` rows
— the ladder's every-module-has-a-row clause requires the claim to be written down once the leg
checks the module, which is what decided that those rows are this phase's rather than Phase 168's.
Phase 168 keeps what it was cut for: the per-kind lemma shape that reaches UI scale, and the `wf`
characterisation over a generated acceptance predicate. The UI measurement stays in
`proofs/README.md`'s theorem 1 section as history under a heading that says whose problem it is now.

**The generator learned to say where a model came from, and that is the one surface change.**
`FStarTarget.vocabularyModuleFrom` / `proofsModuleFrom` take a `Provenance` — pre-wrapped header
lines naming the source, the regeneration command and what the theorem is a property of — because
the same emitter now serves this repository's certification set and a domain's vocabulary in the
domain's repository, and one hard-coded header cannot tell both stories. The un-suffixed
`vocabularyModule` / `proofsModule` keep their signatures and emit a provenance honest about the one
thing the generator knows (an IDL was supplied); until this phase they named this repository's
corpus and its check script in a package a domain generates from. Additive, riding the 0.25.0 draft
slot.

**Rejected: one merged `Idl` for the whole certification set.** The three vocabularies declare
different wire shapes (`WireShape.Default` against the samples' bare-string discriminator and flat
envelope) and different node envelopes, and the emitted model is parameterised by both — a union
would have to pick one shape and would certify the backend's other branches by nothing. Three pairs,
each from its own walk, is the faithful form and is what `IdlCertificationTests` already means by
"the union of the three": a set, not a merge.

**Rejected: keeping the UI model beside the certification set "for coverage".** It would keep every
one of the costs above for a claim the substrate cannot cite, and it would keep the corpus read in
the leg. The adopter runs the same emitter over the same `idl.json` and gets the same model; nothing
is lost but the misplacement.

## 2026-09-15 — D40: the hardening default is NOT flipped — it is what two published artifacts MEAN, and the refusal ships opt-in beside it

**Decided (Phase 178).** `HardenPolicy.Default` keeps the four tokens the engine used to
hard-code, and keeps being what an artifact with no `harden` block reads back as. The refusal the
phase was written to install ships anyway, **reachable and opt-in**: `HardenPolicy.Undeclared`
beside `Default`, and `Trust.checkHardenPolicy` / `Trust.hardenOrRefuse` beside `Trust.harden`. A
vocabulary that wants "declared nothing, so say so" declares `Undeclared` (or leaves any member
empty) and calls the checked entry point; every existing caller is untouched, and `Trust.harden`'s
signature and behaviour are unchanged.

**Why the flip was stopped: the measurement refuted the premise that licensed it.** The phase's
shard stated it plainly — "every vocabulary author in [the consuming repositories] that calls the hardener declares
`Harden` explicitly — the UI tier does since 116 — so the change is breaking on paper and lands on
no consumer". Measured first, as the shard's own first task required, that is false in the two
places that decide it.

**The consumer measurement** (`grep -rn "Trust.harden\|Harden = \|HardenPolicy"` over `Fuaran/`,
`*.fs`, excluding `bin/`, `obj/` and worktrees; 2026-09-15, `Fuaran.Core` at `<Version>` 0.24.0):

| Repo | Site | Policy declared |
|---|---|---|
| `fuaran-dotnet` | `src/Fuaran.UI.Idl/Vocabulary.fs:3482` | **`HardenPolicy.Default`** — the field, not the tokens |
| `Fuaran-Core` | `src/Fuaran.Core.Idl.Spike/Spike.fs:385` | **`HardenPolicy.Default`** — and it is in `src/` |
| `Fuaran-Core` | `tests/fable-smoke/Program.fs:324` | **`HardenPolicy.Default`** |
| `Fuaran-Core` | 9 test vocabularies — `IdlAnnotationTests:71`, `IdlCertificationTests:253`, `IdlDiffTests:45`, `IdlEnumWireTests:56`, `IdlKindAnnotationTests:86`, `IdlStabilityClassTests:82`, `IdlWireShapeTests:44`, `ScoreDomainSpike:304`, `SecondDomainSpike:199` | **`HardenPolicy.Default`** |
| `Fuaran-Core` | `tests/Fuaran.Core.Tests/IdlFStarTargetTests.fs:58` | `{ HardenPolicy.Default with TransparentUnions = [] }` — seven of eight members from the default |
| `Fuaran-Core` | `tests/Fuaran.Core.Tests/ReferenceIdl.fs:202` | **its own, every member** — the only site among the measured repositories that declares the tokens |

Thirteen declaration sites take their tokens from the default; one declares its own. `Trust.harden`
itself has exactly one caller among the measured repositories — `IdlCertificationTests`, over `refIdl`, the one
vocabulary that would have survived the flip — which is precisely why a caller census alone reads
as "lands on no consumer" and is the wrong census to take.

**And the finding the shard's grep could not reach: the default is a WIRE fact, not only a source
one.** `Artifact.render` omits the `harden` block exactly when `idl.Harden = HardenPolicy.Default`
(`Artifact.fs:497`), and `Artifact.readHarden` resolves an absent block through `Default`
(`Artifact.fs:971`, under a doc comment promising exactly that to every artifact written before the
tokens were declarable). Both published `idl.json` artifacts —
`fuaran-dotnet/src/Fuaran.UI.Idl/idl.json` and the **shared cross-host corpus**
`wire-format-fixtures/idl.json` — carry no `harden` key. Emptying or removing `Default` therefore
does not merely break a compile that could be fixed: it changes what already-published bytes MEAN,
for every host that reads them, silently and with a green build. The phase's own acceptance
("`fuaran-dotnet` regenerates byte-identically … with no change on its side") cannot hold across
the flip, because the flip is the change on its side.

**What is deliberately NOT done, and what it would cost.** The four tokens stay in `src/` as
`HardenPolicy.Default`'s literals, so the acceptance criterion "a search of `src/` for `Custom`,
`Markdown`, `Static` and `TextSource` returns only doc comments" is not met — it is not meetable
without the flip. Widening the members to `string option` is likewise not done: a retype of a
published record is met by every consumer whether or not it wants the refusal, which is the
opposite of an opt-in, and it would have to be justified by the same false premise. `Undeclared`
uses empty strings instead, which is the absence of a name in a record whose members are names.

**What makes the refusal per-member and the finding durable.** `checkHardenPolicy` is static in
`(idl, policy)` rather than in the value: the gate runs over every harden, so the gated kind and the
four members its inert placeholder is built from are always needed, and the value-literal pair is
needed exactly when the caller declared a URL field. A value-dependent answer would pass today on a
tree with no gated node and refuse tomorrow on a document nobody changed.
`HardenPolicy.TransparentUnions` is never refused — an empty list is the honest declaration of a
vocabulary no case of which encodes bare, and `ReferenceIdl` says exactly that on purpose.
`IdlTrustTests` pins each member's refusal as its own case, and both directions of the conditional
pair; each was confirmed to go red with the refusal removed. Its last two cases are the guard over
the compat promise above — an artifact carrying no block reads back as `Default`, never as
`Undeclared` — and that pair was confirmed to go red, alone, when `readHarden`'s absent-block
answer was flipped. _(Phase 179 amended the first of the two: the writer now emits the block
unconditionally, so the case asserts the block's PRESENCE on a fresh render and holds the
absent-block promise over a fixture with the block dropped. The promise it guards is unchanged —
see the amendment below.)_ A later session that reaches for the flip anyway meets a red test naming
this entry rather than a silent change to what published bytes mean.

**The route if the flip is still wanted.** It is a migration, not a default change: `fuaran-dotnet`
spells its own four tokens at `Vocabulary.fs:3482`, its `idl.json` and the shared corpus are
regenerated to carry an explicit `harden` block, every host reading that corpus is confirmed to
tolerate the new key, and only then does `Default` empty. Each step is separately shippable and
none of them is this phase.

**Amended 2026-09-17 (Phase 179) — the route's first step is taken, and it is a WRITER change
only.** `Artifact.render` now emits the `harden` block for every policy value, `Default` included;
the omission branch is gone. A freshly rendered artifact therefore declares its hardening
vocabulary outright and no reader has to infer it, which is what makes the step above ("regenerated
to carry an explicit `harden` block") a re-render rather than a hand edit. **`readHarden` is
deliberately untouched**: an absent block still resolves through `Default`, because the artifacts
written before this phase still exist and step one must not change what they mean — the whole point
of sequencing the flip rather than collapsing it. The class is additive on both axes and measured
rather than asserted: `IdlArtifactTests` runs the diff classifier over pre-179 and post-179 bytes
in both directions and requires no `HardenPolicyChanged` row, with a falsifier requiring one for a
policy that genuinely moved. Shipped on `0.26.0`; `STABILITY.md` carries the entry.

**What remains of the route, and what gates it.** Two published artifacts must be RE-RENDERED under
this phase — `fuaran-dotnet/src/Fuaran.UI.Idl/idl.json` and the shared cross-host corpus
`wire-format-fixtures/idl.json` — and every host reading that corpus confirmed to tolerate the new
key. Only then does step two run: an absent block means "declared nothing", `Default` empties or
retires, and the tokens leave `src/`. That step is **Phase 180**, and collapsing it back into this
one recreates exactly the hazard the measurement above refused. This entry, not the code, is what a
session reaching for the flip should meet first.

**CLOSED 2026-09-23 (Phase 180) — the route was walked and step two is taken.** The gate this entry
set is met and was checked rather than assumed: `fuaran#1755` shipped at `bb10065`, both published
`idl.json` artifacts carry an explicit `harden` block (line 437 of each, byte-identical to one
another), and the workspace copy registry reports the shared corpus and both bundled host snapshots of it
`ok`. So `HardenPolicy.Default` is deleted, `Artifact.readHarden` resolves an absent block as
`Undeclared`, and `Trust.harden` is the checked entry point — with `hardenOrRefuse` kept as its
alias, because it is the name every caller written between 178 and 180 uses and the two now mean the
same thing. Shipped on the `0.30.0` slot, class BREAKING, `STABILITY.md` carries the entry.

**What the walk found that this entry's own measurement did not.** D40's consumer grep listed thirteen
`HardenPolicy.Default` DECLARATION sites, and they were all thirteen. What it could not list is a
CONSUMER of the default's meaning that never names the member: `Diff`'s artifact snapshot carried a
literal second copy of the five tokens for its absent-block case, so retiring the reader answer
without it would have left the classifier and the reader disagreeing about what an artifact MEANS —
a disagreement with no compile error and no failing test until an artifact with no block was
classified. It is written as a walk over the same members applied to an empty object now, so the two
cannot drift apart again. The lesson generalises past this record: a grep for a member finds every
site that SPELLS it, and a default's meaning can be copied without being named.

**What is deliberately still true after the close.** The `src/` token criterion is met in the sense
the flip was ever about — no engine source supplies those five names to a vocabulary that has not
declared them — and `Fuaran.Core.Idl.Spike` still spells `Markdown`, `Static` and `TextSource`,
because they are ITS kinds and union cases. That is a vocabulary declaring its own names, which is
what Phase 116 exists to make possible; reading the criterion as a ban on the spike naming its own
tokens would have required deleting the spike to satisfy it. The spike declares
`{ HardenPolicy.Undeclared with TransparentUnions = [ "TextSource", "Literal" ] }`: the one member
it genuinely has, and no gated kind, because it has none.

## 2026-09-15 — D39: the producer owns its conformance vectors; the shared corpus is the distribution point

**Decided (Phase 172).** The two conformance families this repository EMITS — `laws/transform-laws.json`
and the `apply/` pair — are committed HERE, under `conformance/` at the repository root, and the
default suite certifies the committed files: the oracle question (is every vector still true of this
evaluator / engine?) and the freshness question (is the file what this kit renders?) are both asked
of this checkout and no other. The shared wire-format corpus carries a **declared copy** of each,
named in this repository's `copies.json` on the workspace copy registry (
`check: fingerprint`, `regen:` the exporter pointed at the corpus). The exporters default to
`conformance/` and take a directory only to refresh the copy. Nothing moved OUT of the corpus: the
hosts read `laws/` and `apply/` at the same paths with the same bytes — a copy is what they were
always reading, and this names it.

**Why.** Since D31 an absent corpus fails, so `pwsh ./verify.ps1` on a machine holding only this
repository was red: "Core is generic" was true of the packages and false of the gate. Eighteen
`SiblingCorpus.resolve` sites across five test files read that corpus, and three of the reads were
Core's own generic contracts stored in a domain's specification — emitted here, committed there, and
read back from there by the suite that emitted them. The producer of a contract is the one party that
cannot need a copy of it to know what it says.

**Every corpus read is now one of two things, and the classification is the decision.** A read is
either of a file Core AUTHORS — moved to `conformance/`, default suite — or of the DOMAIN's own
fixtures used as input (`nodes/` 230 files, `ops/` 24, `dag/` 5, `envelope/`, the pinned `idl.json`),
which are not Core's to vendor and are behind the opt-in **live-corpus leg**,
`FUARAN_CORE_CORPUS_FRESHNESS=1`: the two copy-freshness legs, the proof-oracle differentials over
the domain's pools (beside their generative legs, which run regardless), the IDL spike's drift guard,
and the F\* target's partition and `Vocabulary.fst` generation diff. Unset, each of those legs reports
itself NOT ASKED FOR, by name, and says nothing was compared; the gate reads the ask before it
consults anything, so a checkout with no corpus anywhere is green by construction rather than by
every candidate path missing. Set, an absent corpus FAILS exactly as D31 decided — D31 is amended in
scope, not reversed: it now binds the leg it was written for rather than a suite that no longer
needs the corpus to certify Core's own contracts. CI sets the variable in both jobs, so every push
still compares against the corpus at its `main`; the variable is what stops CI from silently going
self-contained, which would be the Phase 130 defect in a new coat. `--emit-fstar` is a command, not a
leg — an invocation is its own ask — and reads the locator ungated. `FUARAN_CORE_SKIP_CORPUS` is
retired: with nothing left to skip by default, there was nothing for it to say.

**The `kitVersion` stamp stays in the file, and what moves is where a version cut goes red.** Two
hosts read the stamp, so the bytes are fixed. The stamp now lives in Core's own committed file, so a
`<Version>` move re-emits it in the same commit and Core's gate never crosses a repository boundary
to fail; the corpus copy is then reported **stale by fingerprint** — warn-first on the sweep from
every checkout, and as a failure of the opt-in leg where the corpus is present (CI) — until the copy
is refreshed. That narrows Phase 139's finding rather than dissolving it: the redness is confined to
the copy's own freshness question, named with its one-line remedy, instead of reaching an unrelated
phase's gate days later. A leg that compared the copy modulo the stamp would be a fuzzy match, which
the registry's own definition refuses; the honest report is "the copy is stale", because it is.

**Why the registry rather than a bespoke freshness test.** The workspace already has one answer to
"has a generated cross-repo copy drifted from its source" — `copies.json` and the `copies` sweep,
warn-first, offline, quoting the regenerating command verbatim — and the corpus's own `copies.json`
already declares the ts/py bundled snapshots on it. A second mechanism would be the reinvention the
rule of three exists to refuse. The in-suite opt-in leg reuses the registry's `fingerprint` equality
(restated in `OwnedConformance.fingerprint`) so the two never disagree about what "fresh" means.

**Proved.** The gate both ways as unit tests in `SiblingCorpusTests.fs` (not asked ⇒ `NotAsked`
naming the variable, the override never consulted; asked and pointed at a non-existent directory ⇒
`Absent` naming the path); the default suite green with `FUARAN_CORE_CORPUS_DIR` pointed at a
directory that does not exist; the three emitted files byte-identical to the corpus copies; the
registry reporting the three records `ok`, and `stale` when the source is perturbed.

## 2026-09-14 — D38: a graft's INTERIOR is inspected — `applyContained` walks the inserted subtree

**Decided (Phase 161), by the operator, between two options the phase was chartered to put rather
than to choose.** Phase 140 proved that `Ops.applyContained` does not preserve the invariant it
exists to keep — "every node with children satisfies `canHold`" — and named two hypotheses the
theorem has to carry to be true of the function that ships. One of them, `contained_op`, is about the
graft: `canHold` is applied to the PARENT of an insert and to nothing inside the subtree being
inserted, so a graft whose own interior node holds children while `canHold` refuses it is accepted
whole and carries the violation in. The question was what to do about it.

**(A) Inspect the graft — CHOSEN.** `validateInsert` walks the inserted subtree and refuses, with
`NotAContainer` naming the first interior parent that holds children while `canHold` refuses it. A
refusal-class widening on the Phase 137 pattern: an operation previously accepted is now refused, so
a minor under the draft-slot rule, and the `contained_op` premise is discharged by the code rather
than carried by the theorem.

**(B) Interior containment is the domain's obligation — DECLINED.** `applyContained` keeps its
shape; the README states that a domain grafting a subtree is responsible for its interior, exactly as
137 scoped keyed positions; `contained_op` stays a named premise at level 3 of the claims ladder, and
the new `Conformance.containerLaws` family gains a sampled interior check the domain runs over its
own generator.

**Why (A).** Four reasons, in the order they weighed.

1. **(A) is the only option that discharges the premise in code.** Under (B) `contained_op` remains a
   level-3 assumption — a sentence a reader has to believe about every caller — while the survey
   Phase 137 ran found twenty consumers applying through Core with no pre-check of their own. An
   obligation that is documented and unenforced, on a surface with that many callers, is an
   obligation that is not met; the counterexample is already machine-checked, so the only open
   question was who pays for it.
2. **The traversal is already paid for.** Phase 137 made `validateInsert` walk the inserted subtree
   for id collisions (`firstDuplicateId` reads `Tree.ids w node` over the whole graft). Checking
   `canHold` on each interior node that has children is that same walk with one more predicate call
   per node — not a new cost, and not a new traversal for a reader to reason about.
3. **The only operations (A) refuses are bugs that succeed silently today**, and no host mirrors this
   surface. A graft refused by the new clause is one that places children under a node the domain's
   own `canHold` says cannot hold them; accepting it leaves a tree the domain's own predicate calls
   invalid, and nothing downstream reports it. The container capability is Core's — the TypeScript,
   Go, Rust and Python hosts model no `canHold` at all — so nothing outside this repository moves.
4. **(B)'s one advantage is not real.** (B) avoids a contract change, but `containerLaws` names the
   domain obligation either way, so the documentation half of (B) lands under (A) as well. What (B)
   buys is the absence of a refusal — which is the thing being asked for.

**What (A) does NOT cover, and this is the half that stays.** `child_blind` — the other hypothesis —
is untouched by any amount of graft inspection. `canHold : 'Node -> bool` may read the node's child
list, and a predicate that does can admit a node at the instant it is checked and refuse it the
instant it gains a child; no check placed anywhere in the engine can repair that, because the
predicate's answer changes under the very edit the check licensed. It stays a named premise of
`contained_preserves`, and it becomes the **domain's** obligation, certified rather than asserted:
`Conformance.containerLaws`' first law perturbs a node's children and requires `canHold` to be
unchanged. A domain whose predicate reads the child list fails that law and learns it from its own
conformance run.

**Scope of the walk: `InsertChild` only, and the omission at `MoveNode` is argued rather than
inherited.** A move relocates a subtree that is ALREADY IN THE TREE, so it introduces no interior
structure the tree did not already hold: whatever the moved subtree's interior says about
containment, it said before the operation, and a violation found there was carried in by some earlier
insert. `contained_preserves`' move clause is the machine-checked form of that argument — it derives
the moved subtree's containment from the tree's own (`find_in_contained`), and it needed no premise
about the operation to do so. Walking the graft at `MoveNode` would refuse an operation that carries
in nothing new, on the strength of a pre-existing violation elsewhere in the tree; that is an
invariant-REPAIR gate, a different feature, and one nobody asked for. The new clause therefore sits
in `validateInsert`, where new structure actually enters.

**Precedence within `validateInsert`: the new check goes LAST.** After the duplicate-id scan, after
the parent's existence, after the parent's own capability. The consequence is that **no operation
refused before Phase 161 changes its class** — only operations that were ACCEPTED can now be refused
— which is the smallest correct change and the one a consumer's existing envelope handling survives
unaltered. It also keeps Phase 137's built-collision conformance arm reaching the check it is about:
a deliberately colliding graft earns `DuplicateId` first, exactly as it did.

**Rejected: refusing under a new rejection case.** A `GraftNotAContainer`, or a `NotAContainer`
variant carrying the graft-relative path, would let a consumer distinguish "your parent is a leaf"
from "your subtree's interior is wrong". It was declined for 137's reason, in 137's words: the class
is the same failure the envelope already names, reached through more of the subtree, and a new case
breaks every consumer matching on the envelope to buy a distinction they can already make — the
named node is in their own graft, and they have it in hand.

**Rejected: gating the walk behind a flag.** An `applyContainedDeep`, or a parameter on
`applyContained`, would let a caller opt in. It multiplies the container-aware surface by two at the
exact moment Phase 140 found that surface already had one gap too many — there is still no
container-aware SEQUENCE surface (`container-sequence-gap`) — and it leaves the default answering
wrongly, which is what this decision is about.
## 2026-09-14 — D37: structural validity is ONE clause and ONE definition, and the apply corpus family carries no version stamp

**Decided (Phase 139).** Three calls, each of which had a plausible alternative.

**(1) `Tree.WellFormed` has one clause, not two.** The phase was specified as "`uniqueIds`,
`singleRoot`", which is the natural pairing and is how the invariant reads in most tree libraries.
It is not a pairing that means anything here. A tree in this core is a `'Node` value reached through
`NodeWitness.Children`: the walk starts at exactly one node by construction of the type, there is no
forest to exclude, and no node can be reached as the child of two parents without also appearing
twice in the preorder — which is the uniqueness clause. So single-rootedness is a TYPE-LEVEL
guarantee, and a second clause would have been either redundant (a restatement of uniqueness) or
false (a claim about a shape the type cannot hold). The predicate carries the one clause that can
actually be violated, and says so where it is defined rather than leaving a reader to infer it.

The alternative — ship the second clause anyway, because the specification named it — would have put
a permanently-true field in a public verdict type, which is worse than it looks: a reader who sees
two clauses reasonably concludes that two things are being checked, and writes code that branches on
an arm nothing can produce.

**(2) One definition, and the OFFENDER moved.** `Ops`'s insert validator and `Diff.toOps` each
carried their own id-uniqueness scan. Both now read `Tree.wellFormed` / `Tree.graftWellFormed`. The
cost is one observable change: `Diff.DiffError.DuplicateIdInTree` used to name the first id whose
duplicate GROUP appeared earliest and now names the first id reached twice in preorder, which
differs for `[a; b; b; a]`. That was accepted rather than preserved, because preserving it would
have meant keeping the second scan — and the answer the accept path already gave is the one worth
converging on. Recorded in `STABILITY.md` as additive: the error case and the refusal are unchanged,
and nothing ever pinned which of several duplicates was named.

**(3) The `apply/` corpus family carries NO `kitVersion` stamp.** `laws/transform-laws.json` carries
one and is then byte-compared whole, so any `<Version>` move reddens its freshness leg until the
corpus is re-emitted — including a draft-slot cut, which is made once and ridden by several phases
landing days apart, so every one of them inherits a red gate for a file none of them touched. Phase
137 hit exactly that and recorded it. Copying the stamp into a second file would have doubled the
class for no gain, because the two families are not the same kind of artefact: the law vectors are a
SAMPLE of what one pinned kit answered, and the apply vectors are a SPECIFICATION of apply semantics
whose every expectation is recomputed by the suite on each run. An artefact whose answers are
re-derived does not need a stamp saying who derived them, and one that cannot go stale for a reason
unrelated to its content should not be able to.

The family is also SELF-ENUMERATED — its own `apply/manifest.json`, not an entry in the corpus root
manifest — which is the `laws/` and `merge-conformance/` precedent and, separately, the only shape
that does not redden a host on arrival: at least one host's certification kit reads the root
manifest's fixture list as a whole and asserts that its per-kind leg tallies account for exactly the
manifest's fixture count, so a new `kind` there fails a repository that has adopted nothing.

## 2026-09-14 — D36: the footprint's unknown-parent over-approximation is not tightened, because it cannot be tightened over this record

**Decided (Phase 143).** `Ops.independent`'s last two clauses — a `RemoveNode`/`MoveNode` conflicts
with any structural write in a concurrent script — stay exactly as Phase 78 pinned them. The phase
was chartered to tighten them under a proof, and shipped the proof that they cannot be.

**Why the tightening was worth attempting.** Phase 133 measured what the clause costs: nine of the
fifteen unordered operation pairs are closed by it alone and never look at a tree, because every
skeleton operation except a do-nothing batch writes structure, so a relocation is independent only of
an operation that does nothing (`relocating_forces_inert`). Every multi-writer fold with a relocation
on one lane and any structural edit on another therefore halts. Phase 78 pinned the clause as
conservative-not-tight on the grounds that *nothing could then say what a tighter clause would be
sound against*; Phase 138's preservation theorem removed that obstacle, and 143 was the phase that
cashed it in.

**What was found instead.** The tightening is sound for a `MoveNode` and unsound for a `RemoveNode`,
and **the `Footprint` record cannot tell them apart.** Three facts, proved at one well-formed tree in
`proofs/TreeOps.fst` section 18 with no admits under the pinned prover:

1. `relocation_disjoint_diamond` — a `MoveNode` and an `InsertChild` under a parent inside the moved
   subtree commute. The subtree travels intact, so an edit within it lands in the same place
   whichever order the two are made in. Every clause of `independent` except the pinned pair already
   holds of them.
2. `relocation_diamond_fails_for_a_remove` — the same shape with a `RemoveNode` in it does not. Both
   operations apply at the tree, but remove-then-insert is `UnknownNode` (the insert's parent was
   destroyed with the subtree) while insert-then-remove succeeds.
3. `relocation_footprints_coincide` — a `MoveNode`, and a batch that removes and then reorders, fold
   to **byte-identical** footprints across all four address sets.

A predicate over the four sets assigns one verdict to both, so freeing the safe pair frees the fatal
one. That is `relocation_clause_is_necessary`, and a single witness is enough because the claim being
refuted is universal.

**Read this as the second pinned over-approximation being load-bearing for the first.** STABILITY.md
has said since Phase 78 that a `RemoveNode`'s content-write records the target id and not its
tree-unknown subtree, and that this is *sound because* the unknown-parent rule already serialises the
pair. Dropping the unknown-parent rule re-opens the content-write gap, in exactly the shape the
entry predicted. What was prose is now a theorem.

**Rejected: widening `Footprint` to carry the discriminator.** A fifth address kind naming the
relocation's kind, or a destroyed-subtree set, would let the clause split — and both are breaking
changes to a record every consumer reads, taken on speculation about how much fold availability the
move half actually buys. There is no measurement of that, and this phase's scope excluded the
record by charter. It stays available, priced, and unchosen; the price is recorded in STABILITY.md's
0.24.0 entry so the next attempt starts from it rather than from the beginning.

**Rejected: proving the general move-versus-structural-write theorem anyway.** It is true, and fact 1
is an instance of it, but `Ops.independent` could not consume it — a theorem whose conclusion no
clause can read buys nothing and costs prover budget on every run for as long as it stands. If the
record ever gains the discriminator, that is the phase that should prove it, against the clause it
will actually license.

**Also found, and recorded because two later claims rest on it.** No other language host mirrors
`Ops.footprint`, `Ops.independent` or the `Footprint` record at all — checked across the TypeScript,
Go, Rust and Python hosts. The footprint surface is not on STABILITY.md's "members the other language
hosts mirror name for name" list either, and that list's six members are all elsewhere. And no
committed conformance-corpus `ops/` vector carries an expected independence verdict: those fixtures
are operation-wire documents, and the footprint they are read under is computed at test time by
`corpusFootprint` in `ProofOracleTests.fs`. So a change to `independent` would have had no host to
adopt it and no vector to re-emit — which is worth knowing before the next phase sizes one.

**How the decision is held.** The `Proofs.Oracle` case "the pinned unknown-parent clause is
necessary" runs the witness on the extracted model and on production side by side, and the
`Conformance.concurrencyLaws` teeth-check erases `UnknownParentWrites` and nothing else — precisely
`independent` with its last two clauses removed — and requires the confluence or totality law to go
red over a generated pool. Both are red the moment someone tightens the clause, which is the only
form in which a decision not to do something survives.

## 2026-09-14 — D35: a union-case name shared by two types in one namespace is a Fable defect, so every reason family qualifies its cases

**Decided (Phase 147).** `Fuaran.Core` now carries two namespace-level closed unions for a
walker's break reason — `ChainBreakReason` (Phase 125) and `DagBreakReason` (Phase 147) — and both
declare an `Unrecognised of string` arm, deliberately, because the two shapes are meant to read
alike. The day the second one landed, the pre-existing `chainBreakReasonLaws` in
`Fuaran.Core.Conformance` stopped compiling **on the Fable pipeline only**: an unqualified pattern
`| Unrecognised s -> …` matched against a `ChainBreakReason` scrutinee was reported as
`expected 'ChainBreakReason' but here has type 'DagBreakReason'`, on a tree the .NET compiler had
just built with zero errors and zero warnings and whose full Expecto suite was green.

**The general fact, stated so it is not rediscovered.** The .NET F# compiler resolves an unqualified
union-case PATTERN from the scrutinee's already-known type; Fable does not, and resolves it by
plain name lookup in scope, taking whichever type declared that case last. So two types in one
namespace sharing a case name compile clean on .NET and fail under Fable — and only in the file
that happens to have both types in scope, which is typically the conformance kit rather than the
file that added the second type. The build is not the gate for this class; the Fable-clean leg of
`verify.ps1` is, and this is the first phase on which that leg was load-bearing rather than a
formality.

**The rule.** A pattern over a case that ANY other union in the same namespace also declares is
written type-qualified (`ChainBreakReason.Unrecognised s`, `DagBreakReason.Unrecognised s`), in
library code and in the conformance kit alike, and a comment at the site says why. Renaming the
arms apart (`UnrecognisedChain` / `UnrecognisedDag`) was rejected: the whole point of the sibling
shape is that a reader of one family already knows the other, and a name that differs only to
placate a compiler makes the two families read as different contracts. `[<RequireQualifiedAccess>]`
on the unions was rejected too, for now: both types are public since 0.23.0 and 0.24.0
respectively and consumers construct and match their named cases unqualified; forcing
qualification is a source-breaking change on every consumer, whereas the hazard exists only where
both types are in scope at once — which, inside this repository, is one file. It becomes worth
revisiting if a third reason family lands.

**The falsifier, and where it lives.** The fix is commit `910a3ee`: two pattern sites in
`Conformance.fs` qualified, nothing else changed, and the Fable leg went from two errors to clean
with the .NET build unchanged. No new test pins the class — the Fable-clean leg already does, on
every gate — so the obligation this entry adds is to the AUTHOR of the next sibling type: run the
Fable leg before claiming the tree is green, and expect the failure to surface in a file you did
not edit.
## 2026-09-14 — D34: the apply engine's id-uniqueness gap is FIXED, not assumed away by the theorem

**Decided (Phase 137).** The skeleton-op tree algebra has exactly one structural invariant that is not
free from the inductive `'Node` type. Parent/child consistency, acyclicity and child order all come with
the type; **"a well-formed tree carries each id at most once" does not** — it has to be written down and
enforced. It was written down on the diff path (`Diff.toOps` refuses a duplicated tree with
`DiffError.DuplicateIdInTree`) and it was NOT enforced on the accept path: `validateInsert` rejected only
when the inserted node's OWN id already existed, so a subtree whose descendant id was present, or which
repeated an id within itself, was accepted.

**The choice was between a hypothesis and a fix, and it is a real choice.** The preservation lemma the
proof programme is building — *apply takes a well-formed tree to a well-formed tree* — can be discharged
two ways. It can carry the gap as an ANTECEDENT ("…provided the inserted subtree's ids are fresh"), which
is sound, cheap, and true. Or the engine can establish the antecedent itself. We took the second, and the
machine-checked counterexample is already in the tree: `TreeOps.insert_breaks_wf` exhibits the accepted
insert that duplicates an id, and `ins_wf` / `ins_wf_conv` state that guarding on the RESULT is exactly
the validation this decision adds. The lemma would have handed that counterexample back on its first
attempt; fixing it first is the cheaper order.

**Why the hypothesis is the wrong answer here, in one sentence: a theorem that assumes fresh ids is sound
about a tree nobody can guarantee they hold.** The freshness would have to be established by every caller,
of which there are many and which mostly do not know they are callers — a domain applies through
`Ops.apply` and gets no signal that an unstated precondition exists. An unenforced precondition on a
generic library's accept path is not a weaker guarantee, it is a guarantee that reads as one and is not.
And what it costs is not a clean failure: `Tree.updateNode` rewrites *every* node matching a repeated id
and `Tree.Index.build`'s `Map.ofList` keeps the last, so the tree keeps working and answers wrongly.

**Remapping was considered and rejected.** The engine could have accepted the insert and renamed the
colliding ids. That would make the accept path emit bytes the op did not carry, which breaks the one
property an op-stream is for: replaying a recorded script must reproduce the recorded tree. Refusal keeps
apply faithful to its input; remapping is a caller's decision, made with a caller's knowledge, and
`Tree.remapIds` already exists for it.

**Parity was the deciding external evidence, and it is only parity on one of the two halves.** A survey of
the sibling engines found that the TypeScript, Go and Rust reference implementations all refuse an insert
whose subtree carries an already-present id (`DuplicateNodeId`), and the UI tier runs its own pre-check
ahead of this engine — so on that half Core was the outlier and this is a correction, not a new opinion.
On the other half — an id repeated *within* the inserted subtree, none of them present in the tree — all
three of those implementations ACCEPT, because each seeds its comparison set from the destination tree
alone. Core refuses it. That is deliberate and it is deliberately recorded as stricter rather than
described as parity: the invariant is a property of the tree that results, and a reader porting between
engines needs to be told which of the two claims they are relying on. `STABILITY.md` states the two
separately for the same reason.

**The rejection class stays `DuplicateId`.** It is the same failure the envelope already names, reached
through more of the subtree. The per-engine code correspondence (`DuplicateNodeId` on the sibling hosts)
is pinned by the fixture family rather than by renaming a case every consumer matches on.

**Scope is the WITNESS surface, and naming that limit is part of the decision.** `Tree.ids` walks
`NodeWitness.Children`, so a node held in a keyed, non-structural position is invisible here. The
invariant therefore reads *each id occurs at most once over the witness's `Children` traversal*, the
domain owns uniqueness over its keyed positions, and the pre-check a domain runs ahead of this engine
must stay. Widening the witness was rejected: `Children` is also what the engine rebuilds through, so a
wider witness would oblige a domain to restructure keyed cases as an ordered list — a large change to the
adoption contract to buy a check the domain is better placed to make.

**Precedence was preserved rather than tidied.** The old validator checked the inserted node's own id
before parent existence. `Tree.ids` is preorder, so the widened scan reaches that id first and the
envelope ordering a consumer already handles does not move. Reordering it would have been a second,
unrequested behaviour change hidden inside the first.

**What certifies it.** Ten unit cases that were each red before the change (descendant collision and
internal duplication across `apply`, `canApply` and `applyContained`; first-offender order; batch
all-or-nothing; the partially-remapped and non-injective-remap copies), plus a fourth `opAlgebra` law,
`"an accepted insert introduces no id already present"`. That law needs a BUILT arm and the reason is
worth recording: the conformance generator's `FreshNode` contract is "an id not in the tree", so a DRAWN
insert can never collide, and a law quantified over the drawn sample alone would have certified a
validator that checked nothing. Each iteration therefore constructs two colliding subtrees rather than
hoping to draw one.

## 2026-09-13 — D33: four API asks cut as one minor — and the fourth was already shipped, so what it gets is the property

**Decided (Phase 125).** The UI tier routed four Core API asks here. They are cut as
ONE minor because a consumer's cost is per RAISE and not per ask: four minors would have cost the
same consumer three raises it gains nothing from, and the cohort rule moves the substrate as a
system anyway.

**The fourth ask was already answered, and saying so is the deliverable.** The ask was that
`Fuaran.Core.Idl.Codegen` render a payload-carrying `OmitDefault (VUnion (tag, payload))` in all
three emitters, and that the `None`-literal fallbacks — always-emit on the encoder, `dReq` on the
decoder — stop shipping an artefact that contradicts its own IDL. **D26 / Phase 124 (`0.21.0`) is
that work**, landed three days after the ask was written and citing the same example. Reading
`fsDefaultLit`, `defaultExpr`, `tsIsDefault`, `tsDefaultLit` and `tsDecField` in the tree confirms
every clause of it, so implementing the ask as stated would have been a second renderer over the
same set — the exact defect D26 records the first one causing.

What the ask asked for that 124 did NOT ship is the GENERATIVE property. 124's certification is
case-based: a payload-carrying fixture plus go-red probes on each formerly-silent path. A case
proves a case. `Conformance.declaredDefaultLaws` draws a declared default over a drawn IDL and
asserts the two emitters AGREE — both render, or both refuse — which is the claim that survives a
shape nobody wrote a case for, and the one that goes red if 124 is ever undone. A refuted premise
whose finding lands as an assertion is worth more than a mechanism built on top of it.

**And the property found a divergence on its first run, which was escalated rather than guessed at —
and RULED the same day.** A declared TRANSPARENT union case as a default was refused by the
TypeScript backend — bare on the wire, so a tagged predicate would be about a value the JS encoder
never sees — and rendered by the F# backend, whose omit test is a pattern match on the HOST value,
where the case is not transparent. Both were locally correct, and the consequence was a vocabulary
that generates in F# and refuses in TypeScript. Phase 124 added the TS refusal knowing it was newly
reachable and did not add an F# counterpart, and nothing recorded whether the asymmetry was
intended. Under the bound doctrine's escalate-don't-guess axis the release itself did not pick a
side: the property admitted exactly that class by name, failed on any other disagreement, and failed
if the class became empty — so the divergence could not widen, could not spread, and could not be
closed silently either.

**The ruling (operator, 2026-09-13): the asymmetry was NOT intended, and the backends must agree.**
The F# backend now REFUSES a default whose case is a declared transparent case, with the same
`UnsupportedDefault` the TypeScript backend gives. The narrower side wins because the question is
about the WIRE and not about either host language: a transparent case encodes bare, so the omit
predicate the TypeScript encoder would need is not expressible, and a default that only one of the
two generated hosts honours is not a default — it is two hosts disagreeing about what the vocabulary
means, emitted by one generator, on a green build.

**The rule is one predicate, not two matching checks** (`Gen.isDeclaredTransparentCase`, called by
both `fsDefaultLit` and `tsIsDefault`). The rest of each backend's admissibility genuinely differs
and must — F# has literals and patterns where JS has only `===` — but this clause is about the wire,
and a rule about the wire spelled once per backend is a rule that drifts. This one already had: that
is the whole content of the finding above, and writing the F# arm as a second copy of the TypeScript
arm would have fixed the instance while leaving the mechanism in place.

**The property is tightened in the same act**, which is what keeps the ruling from being a comment.
Its named-class admission is gone and so is the guard requiring the class to stay non-empty; it now
demands FULL agreement, so a transparent-case default lands in the both-refused arm with every other
refusal and any `Ok`/`Error` split at all fails. A case-based test beside it plants one such default
and asserts BOTH backends refuse it with `UnsupportedDefault`, then plants the SAME value at the SAME
field of the SAME vocabulary with the case no longer declared transparent and asserts both RENDER —
one bit changed, so the refusal is certified to be about transparency and not about the
payload-carrying shape Phase 124 exists to render. Both halves go red when the new F# arm is removed,
which was checked rather than assumed.

**Where the property lives is itself a decision.** Not `Fuaran.Core.Conformance`: a law family
certifies a HOST against a contract, and nothing outside this repository implements this generator,
so it would be a family no adopter could run — and the kit is Fable-clean while the generator is
.NET-only and build-time by declaration, so the reference would break the portability gate the kit's
own claim rests on. It sits beside Phase 124's case-based certification, which is where the question
it answers was already being asked.

**`ChainBreakReason` collapses the two hash spellings, and that is a decision.** Core's walkers mint
four distinct reason strings; the type has three named cases. `Reason` answers *which check failed*,
and `"tampered op/actor/seq"` and `"tampered capture"` are the same check run by two walkers — which
walker ran is the caller's own choice and carried by which function it called. The one measured
consumer collapses them already. A case every consumer immediately discards is a worse contract than
no case; `toString` / `ofString` keep the old bytes available for anyone who was logging them.

**`Unrecognised` is kept on a type this library alone mints**, for the reason the consumer's own
comment gives: the alternative is a reader that claims to know which check failed when it does not.
A break that arrives from a host's own walker, from a wire, or from a consumer-constructed record
lands there honestly. `Dag.DagBreak.Reason` is deliberately NOT widened in the same act — different
type, different spellings, no measured consumer, and a breaking change taken on a symmetry argument
is a breaking change nobody asked for.

**`Now` resolves by substitution against a pinned clock, not by a platform call and not by a new
env.** GP3 keeps Core FSharp.Core-only and Fable-clean, so there is no clock to call; GP5 makes
"read the host's real clock at evaluation" the wrong default, because a pipeline that silently picks
up wall-clock time is not reproducible and its cross-host parity claim is not falsifiable.
`ClockWitness = NowGrain -> Cell` plus `substituteNow` reuses D12's seam — the one list-valued params
already resolve through — rather than adding a second resolution mechanism, and an unpinned `Now`
reaching evaluation is `EvalError.UnpinnedClock`, the strict analogue of `UnboundParam`. The grain
set is the two cells that can hold a clock reading and no more: an `hour` or `quarter` grain has no
cell to land in, so admitting one would be vocabulary with no semantics.

**A scalar slot takes `Slot<'T>`, not a `ColExpr`.** Reusing `ColExpr` would have cost nothing to
write and would have admitted `Col "x"` at a `Limit` count, where there is no row to read — an
expression the type permits and no evaluator can mean. D13's default-deny-by-shape reading applies:
the closed two-case `Slot<'T>` says literal-or-param and says nothing else. Resolution is the
EXISTING param seam — the same `Map<string, Cell>` env, the same `UnboundParam`, reported by the same
`Transform.paramsOf` — so a host wires nothing new. The sort DIRECTION stays a literal: no demand
named it, and a `Cell`-valued param would have to spell a direction as a string, which is the shape
this vocabulary exists to avoid.

**`Idl.Gen.usesHosted` is removed rather than kept as a declared boundary.** The `0.19.0` narrowing
kept it on the argument that it states a boundary a cross-host generative comparison must state, and
recorded that it had no caller here because that leg was not wired. The operator's 2026-09-10 ruling
is that the leg is not Core's: the vocabulary-scale sweep belongs to the UI tier. Its sibling
`encodeNodeEnv` was already narrowed on the same evidence, and a surface that retires one of a pair
and keeps the other is a surface nobody can ever finish retiring.

## 2026-09-12 — D32: F\* is the prover; the extracted model is the oracle; the pin, not hints, is what makes the leg reproducible

**Decided (Phase 131, the prover spike).** The mechanised half of the correctness story is written
in F\*, on the operator's familiarity argument — the kernel is pure, FSharp.Core-only, DU-shaped F#,
and an ML-family model of an ML-family kernel keeps the gap between what is proved and what ships
small enough to read. `proofs/DagFold.fst` models `Ops.independent`, `Dag.conflicts`,
`Dag.reconcileMany`, the replay and `FoldConfluence.foldOnce` over an abstract `op`/`state`/`rej`
and proves the Phase 100 fold-confluence law as a theorem under one hypothesis, the domain's
commutation promise. All three of the phase's exit criteria were met — reproducible, agrees,
readable — so the verdict is GO, and the decoder-totality theorem follows in F\*.

**The oracle IS the extraction, and the leg holds it there.** `proofs/oracle/DagFold.fs` is the
model as F\*'s F# backend emits it, compiled into a never-packed assembly the suite runs beside
production over the Phase 100 lane generators and the wire corpus's `ops/` pool. The proof leg
re-extracts on every run and fails on any byte of difference from the committed file, so "the
suite tests the model the theorem is about" is a checked claim rather than a discipline. Nothing
extracted enters `src/`: GP3's FSharp.Core-only, Fable-clean surface is untouched, and no package
moves — `0.22.0` stands.

**Hints are gone, so the pin carries reproducibility.** The phase asked for the proof to check
from committed hints; F\* 2026.09.06 has removed proof hints entirely (`--record_hints`,
`--use_hints`, `.hints` files, unsat cores). The leg instead pins the release — which bundles its
Z3 — by version and hash, refuses any other version, proves every query three times over varying
seeds (`--quake 3`), runs three cold checks in CI, and reports every escape hatch as an error.
F\* releases weekly; an unpinned prover is a gate that changes under you, and the pin is the
single most load-bearing line in the leg.

**The backend's cost landed on its edges and was small.** The F# backend ships no runtime (a
sixteen-line `Prims` shim), emits pre-F#-8 layout (strict indentation off for the oracle project
only), and extracts ghost inductives unless told not to (`noextract_to`). None of it touched the
modelling. The findings are itemised in `proofs/README.md` beside the claims ladder that says what
"formally verified" is spent on — the theorem, on the model, under its hypothesis — and what is
only differentially tested (the DAG's delta recovery) or assumed (the domain promise, the
extractor).

## 2026-09-12 — D31: an absent conformance corpus FAILS, and CI supplies the corpus rather than the suite lowering its claim

**Decided (Phase 130, `0.22.0`).** The two suites that certify against the shared wire-format
conformance corpus resolve it through one locator anchored at the repository's MAIN working tree,
and an absent corpus **fails** the gate, naming every path tried and the remedy. The only way to
skip is `FUARAN_CORE_SKIP_CORPUS`, and the skip then prints that variable by name and says nothing
was compared. `.github/workflows/ci.yml` checks the corpus out and names it in
`FUARAN_CORE_CORPUS_DIR`, so the default is fail-loud in CI as well as locally.

**What it replaces, and why the two halves had to move together.** Each suite found the corpus by
climbing upwards from `Directory.GetCurrentDirectory()` and `AppContext.BaseDirectory`, and
`skiptest`ed by name when the climb found nothing. A climb rooted in the running binary reaches a
sibling corpus from the main working tree and can never reach it from a LINKED WORKTREE of the same
repository, because a worktree sits somewhere else entirely. So the same commit either certified
against the corpus or did not, decided by which checkout ran it — and the skip made that invisible:
four consecutive gate runs in worktrees reported green over a corpus none of them had read, and the
first run that actually compared was one in the main tree, where it failed on drift attached to a
change that had not caused it. Fixing only the anchor would have left the skip available to hide the
next such gap; removing only the skip would have reddened every worktree.

**The decision that needed deciding was the DEFAULT, and it turned on what CI does.** A fail-loud
default is worth nothing if the repository's own CI cannot satisfy it — the gate would be red on
arrival, and a gate that is red on arrival is one people learn to step over. CI cloned no corpus,
so there were two honest options: keep skipping when the corpus is absent (cheap, and preserves
exactly the hole just found), or make CI supply it. The second was taken, because the corpus is a
BUILD INPUT to those two suites in the same sense the vendored fixture corpora are: a conformance
check that goes green without its oracle is worse than no check, and this repository already treats
that as settled for every fixture store it vendors. The cost is one checkout step; the return is
that drift between this kit and the published corpus surfaces on the push that caused it.

**Why the skip survives at all.** The corpus is a separate repository, and a contributor on a bare
clone who is changing something unrelated should not be blocked on a second clone. What is not
acceptable is a skip nobody asked for: an opt-out that must be set by name, and that says on every
skipped leg that nothing was compared, cannot be mistaken in a green report for a comparison that
ran. `docs/conformance-corpus.md` states the same thing where a contributor will meet it first.

**The general point.** "The check could not run" and "the check ran and passed" are different facts,
and a skip renders them identically. Where a check depends on something outside the repository, the
durable arrangement is to make the dependency obtainable and the absence loud — not to make the
check optional and hope the machine that skips it is not the only machine that runs.

_(Numbered D30 in the commit that landed it, and renumbered here on integration: the D30 below
reached `main` the same day. Commit messages are not rewritten, so that one still says D30.)_

**Amended 2026-09-15 (D39, Phase 172) — in scope, not reversed.** The families Core itself emits are
committed in this repository and certified from there, so the default suite no longer reads the
corpus at all. This decision now binds the **opt-in live-corpus leg** (`FUARAN_CORE_CORPUS_FRESHNESS=1`,
set by CI): once that leg is asked for, an absent corpus still FAILS, naming every path tried and the
remedy, for exactly the reason above. The opt-out `FUARAN_CORE_SKIP_CORPUS` named here is retired —
the default suite skips nothing, because it needs nothing; a leg that is not asked for says so by
name, and that is a different fact from "the check ran and passed", rendered differently.

## 2026-09-12 — D30: a committed generated artefact is pinned BYTE-FOR-BYTE, not modulo whitespace

**Decided (operator ruling, drained from the Phase 129 finding).** The two committed-generated-artefact
drift guards in this suite — `IdlSpikeTests`'s "real code emission: the generator still reproduces the
committed `Generated.fs`" and `SecondDomainSpike`'s "drift guard: the generator still reproduces the
committed `DocGenerated.fs`" — compare the generator's emission to the committed file **exactly**. The
question they answer is "is the committed file exactly what the generator emits?", not "did the
generator's meaning change?".

**The two options, because the choice was real.** **(A)** keep the guards whitespace-insensitive: they
ask about meaning, and line endings and formatting are covered directly by the dedicated
`IdlCodegenEolTests` and `WorkingCopyEolTests` that Phase 129 landed, so nothing is uncovered and no
regeneration is ever forced by a formatting-only generator change. **(B)** pin the bytes, which makes a
formatting-only generator change red until the artefact is regenerated and committed. **(B) is
adopted.**

**Why.** Two reasons, and the first is an incident rather than an argument. Both guards compared through
a normaliser that stripped *every* whitespace character before comparing, so an emission differing only
in line endings was **equal** to them — and that insensitivity is exactly why the generator's
carriage-return bake reached a downstream consumer before anything in this repository noticed. The
dedicated tests close that particular hole, but they close it by naming the property; the guards are the
check that quantifies over the artefact, and a check standing beside the artefact it governs while
unable to see a whole class of change to it is a check that will be trusted for more than it does. The
second reason is consistency: `LawVectorTests` already holds the committed law-vector corpus to exactly
this standard — it compares the committed file to what the kit renders now, and names the re-emit
command when they differ — and a repository that pins one kind of generated artefact byte-for-byte and
another modulo whitespace has two rules where one will do.

**What (B) costs, stated plainly, because it is what (A) was protecting.** A formatting-only change to
the generator is now a red suite until the artefacts are regenerated. That is the intended price: the
cost is one command, it lands in the same commit as the change that caused it, and the failure message
names the command. What it buys is that "the committed artefact is current" stops being a claim about
meaning and becomes a claim about bytes — the only form of the claim a consumer regenerating from the
packaged generator can actually check.

**One finding came out of adopting it, and it is the reason the remedy is now trustworthy.** Running the
regeneration the new failure messages name turned the suite **red**: `Snapshots.regen` wrote
`snapshots/spike.json` through `JsonSerializerOptions`, which resolves the newline it indents with from
`Environment.NewLine`, so on Windows the regeneration put CRLF into a committed artefact this repository
pins LF — `git status` dirty, `git diff` clean, and the working-copy line-ending check failing by name.
A remedy that breaks the gate is not a remedy, so that writer now normalises at its own boundary, which
is D29's rule applied one artefact further out: **a writer of a committed generated artefact is an
emitter, and the artefact's line endings are its own rather than the writing machine's.** Adopting (B)
is what surfaced it — under the old normaliser the guards would have stayed green either way, which is
the whole of what D30 is about.

**The ruling covers all THREE artefacts `--regen-snapshots` writes, `snapshots/spike.json` included.**
That third one was the artefact the finding above was about, and it was the one nothing byte-compared:
the legs that read it go through `loadPaired`, which parses it and checks only the wire STRINGS it
holds, so its key order, indentation and line endings were unpinned — the CRLF regeneration left every
one of those legs green. It now carries a guard of the same shape, against a `Snapshots.render` split
out of `regen` so the check can ask "is the committed file current?" without writing. Leaving it out
would have made D30 a rule about two artefacts and a habit about the third, which is the shape this
decision exists to remove.

**No committed artefact moved.** All three regenerate byte-identically to what is committed, which is
D29's measured claim holding: the LF emission this repository ships is the LF emission `0.21.0`
published.

## 2026-09-12 — D29: the generator's OUTPUT is reproducible; the packed ASSEMBLY is not, and will not be claimed to be

**Decided (Phase 129, `0.22.0`).** Every emitter in `Fuaran.Core.Idl.Codegen` normalises its output
to LF at its own boundary, so a regeneration is reproducible across machines. The stronger property
the phase was asked to establish — that a locally packed assembly is byte-identical to the published
package for the same version — is **refuted**, and is not adopted as a goal.

**The refutation is measured, not argued.** The published `Fuaran.Core.Idl.Codegen 0.21.0` carries
`lib/net10.0/Fuaran.Core.Idl.Codegen.dll` at 468,992 bytes; `dotnet pack -c Release` of the SAME
tagged commit, from an all-LF export, produces 469,504 bytes — a different size, so not a difference
in build identifiers that a deterministic-build switch would erase. Three causes act independently
and none of them is a line ending: `global.json` pins the SDK with `rollForward: latestFeature`, so
the compiler is whichever feature band the machine has; the released packages are built on a
different operating system; and this repository sets none of the properties (`Deterministic`,
`PathMap`, `ContinuousIntegrationBuild`) that would pin embedded source paths. Reaching assembly
byte-identity would mean pinning all three, which is a decision about how this repository is BUILT
and released, not a fix for the defect that prompted the phase.

**So the claim is narrowed to the one that was actually load-bearing.** The symptom that reached a
consumer was never "two assemblies differ" — nobody diffs assemblies. It was that a consumer's IDL
regeneration test failed on every machine that had packed locally, because the generated TEXT
differed: the generator's multi-line templates baked the line endings of whichever checkout compiled
them. That is what the emitters now foreclose, and it is verifiable by anyone in a way assembly
byte-identity is not: build the generator from an LF tree and from a CRLF tree and compare what it
emits. Measured both ways — before the change the two builds emit different bytes, after it they
emit the same bytes, and those bytes equal `0.21.0`'s LF emission, so no committed generated
artefact moves.

**The general point, which outlives this instance.** "Byte-identical packages" and "reproducible
generated output" look like the same promise and are not. One is a property of a build system and
costs pinned toolchains, pinned runners and source-path mapping; the other is a property of a
LIBRARY and costs one normalisation at each output boundary. The second is what a consumer of a code
generator depends on, and it is available without the first. A finding phrased as the first should
be checked against which of the two the reported symptom actually needed.

## 2026-09-12 — D28: the D9 exception is taken for a C# facade, and its deletion criterion is written down beside it

**Decided (Phase 128, `0.22.0`).** `Fuaran.Core.CSharp` ships a C#-shaped facade over the closed
unions a non-F# authoring surface has to construct and read — the dataframe algebra (`ColExpr`,
`Transform` and the vocabularies they range over), the artifact-function declaration family
(`ValueSpace`, `HoleKind`, `HoleDecl`, `EffectClass`, `SigEntry`, `Signature`) and the wire JSON
model (`JVal`) — with construction through factory methods, reading through `Match` / `Switch`, and
no F# option, list, function, tuple or union type on any public member outside a declared
`ToCore` / `FromCore` bridge.

**The rule of three is NOT met, and this rides D9 rather than pretending otherwise.** There is one
consumer today: the UI host's C# fluent factory (`fuaran-dotnet`), plus the VB dialect that
translates through that same factory — which is one consumer with two front ends, not two
consumers. D9 says a single concrete, unblocking consumer is sufficient evidence when the Core
feature clearly unblocks a desirable feature downstream, and the unblock here is specific: that
veneer keeps a rule that **every authored value passes through the declared structural layer**, and
it cannot keep that rule strictly for its transform and expression bindings, because those reach
these F# unions and C# can construct one only by reflection or by a hand-rolled mirror. Both
alternatives are worse than a facade for the same reason — they put a second, unversioned copy of a
closed vocabulary in a repo that does not own it, where a case added here is a silent divergence
there rather than a compile error.

**The deletion criterion, which is the half D9 asks for and does not always get.** Two different
events, with two different consequences, and conflating them is how an exception becomes permanent:

- **A SECOND independent C# consumer retires the EXCEPTION, not the package.** At that point the
  surface is ordinary rule-of-three-justified surface and this entry is superseded rather than
  acted on.
- **A source generator emitting this surface from the IDL deletes the PACKAGE.** `Fuaran.Core.Idl`
  already generates a structural layer per target language from a declared vocabulary; the day it
  can emit a C# authoring veneer over a closed union, a hand-written facade over the same union is
  a second answer to one question, and the hand-written one goes. Nothing in this package is
  designed to resist that: it holds no logic, only construction and reading.

**One premise the phase was authored on is false, and the scope reflects the tree rather than the
sentence.** `Fuaran.Core.Function` publishes no union named `Function` — it publishes a *module* of
that name over the declaration family listed above. What the facade covers is that family: the part
a veneer actually constructs (declare a typed hole) and reads (a derived signature). `Arg<'Node>` is
deliberately absent, because it is generic over the DOMAIN's node type, which no Core package names
— a facade over it belongs to whichever host supplies that type, not here.

**What certifies it, and why the checks are shaped the way they are.** A C# console proof
(`tests/Fuaran.Core.CSharp.Proof`, run by `./verify.ps1` and `./run.ps1`) carries four legs, and each
answers something the others cannot. A hand-written consumer builds and reads the four families and
sends what it built out through Core's own codec and back, which is the acceptance criterion
literally. A **round-trip law** asserts `ToCore(Rebuild(FromCore(x)))` equals `x` over a sample drawn
off `ConfRng` — the useful form, since the trivial `FromCore ∘ ToCore` is identity by construction
and proves nothing; what this form says is that the readers and the factories are mutually inverse
over the whole algebra. A **coverage guard** beside it reports any case the sample never reached,
with the expected case list read off the F# type through `FSharpType.GetUnionCases` rather than from
a number written down — so a case added to `ColExpr`, `Transform`, `Cell`, `JVal`, `ValueSpace` or
`HoleKind` in a later release reddens the gate, by name, which is the one thing a hand-written facade
over a closed union cannot otherwise be told. And a **surface check** applies the no-F#-types rule by
reflection over the built assembly, with the `ToCore` / `FromCore` exemption and a printed census of
every member using it, because a facade with no bridge is a re-implementation and the honest thing is
to make the bridge's width visible rather than merely permitted. The surface check is written here
rather than imported: the available analyzers answer nullability and public-API-diff questions, and
this one is about which assembly a type on the surface came from.

**The go-red proof is part of the phase.** Swapping the two operands in the rebuild of `Binary`
reddens the round-trip law across the sample; narrowing the window-function rotation reddens the
coverage guard and names the nine cases it stopped reaching; and four decoy types — a non-bridge F#
leak, the same leak on the bridge, a positional tuple, and a clean type — pin the surface rule's
behaviour in both directions.

**Amended 2026-09-30 (Phase 231) — the premise is re-measured and the package is DELETED at `0.33.0`,
both halves of the facade; the first ruling was suspended on a consumer it had not counted.**

*The consumer this entry named, measured at `fuaran-dotnet@5768b1e` (2026-09-29).* Nothing there
references this package. The C# veneer (`src/Fuaran.UI.CSharp/`) references the host's own F#
projects and `FSharp.Core` only (`Fuaran.UI.CSharp.csproj:41-47`), and it keeps its rule without the
facade, three ways: `JVal` is wrapped in the veneer's own `Payload` struct, which constructs the F#
cases directly (`Facade/Actions.cs:50-80`); `Transform` and `ColExpr` are carried on its public
surface as the F# types, deliberately, so that one algebra has one spelling
(`Facade/Bindings.cs:253-283`); and the hole-declaration family is constructed only as an empty list
(`Factories/Structural.cs:52`). The VB dialect names no Core type. The unblock this entry priced was
met another way.

*The first ruling, and why it was suspended.* The maintainer first ruled (B), delete the package, on a
measurement that found no consumer of it anywhere. There was one, and this repository's own record
named it: `Fuaran.Core.DataFrame.CSharp`, the dataframe half Phase 257 split out (D68, call 5) and
Phase 258 moved to the compute repository, is built over this package. At
`Fuaran-Core/fuaran-core-compute@8a39a42` its project takes `Fuaran.Core.CSharp` as a
`PackageReference` (`src/Fuaran.Core.DataFrame.CSharp/Fuaran.Core.DataFrame.CSharp.csproj:12`), and
its public members take this package's types: `Expr.Literal(CellValue)` (`Expr.cs:56`),
`Expr.Cast(ColumnKind, Expr)` (`Expr.cs:94`), `AggregateSpec.Of(string, AggregateFunction, string)`
(`Steps.cs:116`), and `Step.Join` / `Union` / `Intersect` / `Except` over a `SourceValue`
(`Steps.cs:418-496`); the public `Vocabulary` bridge here existed for exactly that reader. That is
neither of this entry's criteria — the dataframe half is the other half of the same facade, not a
second independent consumer, and no generator emits the surface — but it removed the ground the
ruling stated, and a delete would have left the compute repository unable to raise its pin past
`0.32.0` without a breaking change of its own. So the delete was held and the ruling returned to the
maintainer with the corrected facts.

*The ruling taken on them: delete both halves.* `Fuaran.Core.CSharp` and its proof project
(`tests/Fuaran.Core.CSharp.Proof`) leave this repository on the `0.33.0` draft, with the package's
`api/` baseline, its exclusion entries (`fable-exclusions.json`, `proofs/coverage-exclusions.json`)
and the gate stage that ran the proof. `Fuaran.Core.DataFrame.CSharp` is removed by the compute
repository in the release that raises its pin to `0.33.0` — in that same change-set, because its
reference to this package cannot resolve after it. Neither half of the facade continues, and the
versions already published (`0.22.0` to `0.32.0` here) stay on nuget.org. BREAKING, `removal`; see
STABILITY.md "0.33.0 — DRAFT".

*The options declined.* **(A), the consumer adopts** — it met its rule another way, so there is
nothing to adopt. **(C), kept with a dated re-measurement** — the path by which an exception becomes
permanent, which this entry was written to prevent. **(B′), moved rather than removed** — the package
continuing from the compute repository under the same id, beside its one reader: that reader is the
other half of the same facade and is removed with it, so there is nothing left to move it beside.

*What brings a C# veneer back.* This entry's second criterion stands: when the IDL can emit a C#
authoring veneer over a closed union, a generated facade is the route, and it arrives with a consumer
named rather than on the strength of one.

## 2026-09-12 — D27: a conformance kit that certifies only the codec certifies the wrong half — and "not adopted" is NOT PASSED

**Decided (Phase 126, `0.22.0`).** `Fuaran.Core.Conformance` gains `ConstructWitness<'T>` and
`Conformance.constructThenEncodeLaws`: a domain's corpus is rebuilt through its own smart
constructors and re-encoded, so the AUTHORING surface is certified beside the codec.

**Why a round-trip law structurally cannot see this.** Every codec family in the kit certifies
`decode` and `encode` against each other. The constructors an author actually writes against are a
different function into the same type, and they are not on that path — the law starts at bytes and
ends at bytes. So a field that widens in memory to a richer carrier keeps the suite green over
thousands of vectors while breaking every program that BUILDS a value. That is a measured event
rather than a hazard: the `@fuaran-ui/ui` 0.26.0 release of 2026-09-11 (fuaran#1661), where the only
author-direction consumer broke on the pin bump against a fully green corpus. Rule of
three is met three times over — the TypeScript builders, the F# smart constructors and the Documents
builders are three authoring surfaces over three witnesses, and none of them was certified this way.

**The family lands in `Conformance`, not in a module of its own.** The other standalone families
(`FoldConfluence`, `IncrementalDelta`, `WireNullTolerance`) each drive a different subsystem, so a
fourth module would have read as the natural home. It is the wrong home for one reason that outweighs
tidiness: a consumer's conformance census enumerates law entry points by reflection over a
hard-coded module list that mirrors this kit's own. A new module contributes nothing to any
consumer's census until every consumer edits that list first — which is exactly the silent skip this
phase exists to abolish, arriving through the back door. In `Conformance` the family appears in every
consumer's census on the pin raise, with no consumer edit at all.

**"Not adopted" is reported as NOT PASSED, and the alternative was considered and rejected.**
`witness` is an option, and `None` yields one result naming the domain and the family, with
`Passed = false`. `LawResult` carries two states and no third; widening it to carry "skipped" is a
compile-breaking change for every consumer that constructs one, for a distinction that has an honest
encoding already — a family asked to certify a surface it was never given has certified nothing.
The consequence is deliberate: running the family with `None` is not a route to green. A domain
whose subject this is not records a reasoned non-use in its census row, which is the mechanism every
other unused family already goes through, and which leaves a human's reason on the record instead of
a machine's shrug.

**The right-hand side is `encode (decode b)`, not the bytes `b`.** The law is naturally written
`encode (construct (decode b)) = b` and that is what it computes on a canonical corpus. But a
`Corpus.Case`'s JSON is not required to be canonical — `Corpus.roundTrip` compares values, so a
legal corpus may spell a document with a different key order — and a literal byte comparison would
redden on the corpus's formatting rather than on the authoring surface, which is the one subject
this family has. Checked against the tree before implementing, not assumed.

**Refusal and divergence are separate laws.** A constructor that rejects a value the domain's own
codec just decoded is a finding about the surface; a constructor that builds a differently-encoding
value is a different finding with a different remedy. Folding them into one law would make the red
ambiguous at exactly the moment a reader needs it not to be.

**The go-red is the phase, not an addendum.** The widened constructor is planted over the reference
witness and the plain `Corpus.runCorpus` is asserted GREEN over the same corpus in the same test —
the positive control without which the red proves only that something is wrong somewhere.

## 2026-09-07 — D26: a default the generator cannot render is a REFUSAL, not a fallback — and the fallback was the whole defect

**Decided (Phase 124, `0.21.0`).** `Fuaran.Core.Idl.Codegen` renders a value-carrying union
default in both target languages, and — the half that mattered more — an unrenderable default
refuses generation with `CodegenError.UnsupportedDefault` on every path rather than being absorbed.
`Gen.typescriptModule` returns a `Result` for the first time, which is what makes "every path"
literally true.

**The two halves are one change because the fallback is what made the gap invisible.** The missing
literal was cheap to describe — `fsDefaultLit`, `tsDefaultLit` and `tsIsDefault` admitted a
NULLARY union case and nothing more, so `Binding.Static(Some 0)` was unrenderable — but the
consequence of that `None` was not "no default". It was a documented fallback to always-emit on the
encode side and `dReq` on the decode side: two halves that agreed with each other perfectly and with
the vocabulary not at all. The IDL said the slot is omitted at its identity default; the emitted
code said the key is always present. Nothing was red, and nothing could be, because the artefact was
internally consistent. A generator that can silently disagree with its own input is a worse defect
than one that cannot express something.

**Where it was, and was not, already caught.** A KIND-SPEC field was refused, because
`defaultsDecl` runs `defaultExpr` unconditionally from `fsharpModule` — which is why a
downstream consumer's attempt to declare exactly this default in a UI vocabulary failed loudly
rather than shipping. That loud failure is the reason the gap was ever named. Four paths had no such
leg: a **projected** kind (Phase 945 suppresses the generated constructor, and the skip took the
only default check with it), a **union-case** field, a **record** field, and the **entire TypeScript
backend**, which had no error case at all. The node envelope refused, but through a `failwithf`
inside a function that already returned `Result`.

**Refusal by propagation, not by a pre-flight gate.** A single walk over the declaration, run at
each entry point before any emission, would have been a much smaller change and would have covered
these paths too. It was rejected for one reason: it leaves the fallback arms standing. F# offers no
total way to consume a `Result` at an emission site, so a gated design keeps code that still READS
as "fall back to always-emit", correct only by an invariant asserted in a comment — and the next
emission site added is not obliged to notice. Propagating the `Result` deletes the arms. The cost
was a wide, mechanical refactor through the encoder and decoder emitters of both backends, paid
once, and measured against byte-identity guards the whole way.

**The admissible set is decided by the PATTERN position, not by what renders.** The encoder's omit
test for a union field is a `match` rather than an equality (Phase 691: a union whose fields reach a
closure supports no equality), while the decoder's restore and the smart constructor use the same
string as an expression. So the literal has to be legal in both, which is why a non-empty list and a
non-finite float stay refused although both render perfectly well as expressions. `defaultExpr` was
folded into the same renderer for the same reason: two renderers over overlapping sets meant the
constructor could fill a default the encoder could not test for.

**The go-red proof is the phase, not an addendum to it.** No test asserted `UnsupportedDefault`
before this — the type had existed since the first codegen leg and nothing measured that it ever
arrived. Each formerly-silent path is now planted with an unrenderable default and asserted to
refuse BY NAME, with a positive control proving the same vocabulary generates once the default is
removed (a refusal arriving for an unrelated reason is a probe measuring the wrong question), and
the TypeScript round trip is EXECUTED under node over both sides of the default — the key emitted
when the value differs, absent and restored when it does not.

## 2026-09-03 — D25: the discriminator is ROW-SET PRESERVATION, not frame boundedness — and `rowsEvaluated` does not measure ordering work

**Decided (operator, 2026-09-02).** D24's finding is resolved in the direction it pointed. From
`0.19.0`, `Incremental.plan` admits **every** `Window` as `StepIncrementality.RecomputeFrame`, at any
position — the ranking family, `NTile` and the three cumulative aggregates alongside the four bounded
frames. D19's contract is untouched: every result still equals `DataFrame.evalPipelineWithInEnv` over
the same source; this is the additive reclassification `STABILITY.md` has declared since Phase 99, and
no answer moves.

**The discriminator D24 used describes a distinction this evaluator does not make.** The admitted
window column is recomputed wholesale over the walked frame, through the reference's own
`DataFrame.windowStep` — for a `lag` exactly as for a `rank`. Nothing in the walk consults the frame's
width, and nothing was going to: the reuse being claimed is of the steps BEFORE the window, not of the
window's own column. What the walk actually requires of a step is that it PRESERVES THE ROW SET — one
row in, one row out, in input order, plus an appended column — because the walk's token-to-row
invariant is what the merged order, the maintained group and every cache rest on. Every window
function has that. So frame boundedness was a proxy that happened to be conservative, and a
conservative proxy for a property nothing tests is a declined class with no compensating claim.

**Correctness is by construction, not by the widening being small.** The admitted column is the full
evaluation of that column over the same rows in the same order, computed by the reference. That is the
same sentence Phase 120 relied on, unchanged; it never mentioned the frame. It is covered where D19's
contract is covered — the equivalence family, now generating a bare `cumulSum` and a `rank` behind a
filter, with the Phase 121 adequacy declaration extended so a sample that reaches no
**partition-global** window FAILS the family. That last part is the load-bearing half: "a window was
restricted" is satisfied by the `lag` alone and would have gone on passing had this relaxation been
reverted, so the demand names the narrower class.

**And the consumer is the declined family.** The waiver D24 recorded named "ranked and running-total
columns in live grids" as the consumer justifying the widening, and both of those were in the family
it declined. Widening to what the waiver was granted for is closing that gap rather than opening a new
one.

**`FallBackReason.WindowFrameUnbounded` is RETAINED and no longer produced — the smallest reversible
choice.** Removing a case from a published DU breaks every consumer that matches on it, so the case
stays, `Incremental.reasonString` still renders it, and `Incremental.windowFnName` stays with it so a
reason stored under `0.18.0` still reads. It is **not** repurposed for a hypothetical
non-row-preserving step: a future step of that shape would decline for a different reason, and naming
it with a case whose text says "reads the whole partition, not a bounded frame" would be wrong twice
over. Nothing in `plan` branches on a window function now — a predicate that is constantly true is a
branch that cannot be taken, so there is no predicate rather than one with a dead else.

**`DataFrame.windowFrameBounded` stays public, and it now has one honest job.** The distinction it
draws is real: it is the line a later phase restricting the recompute to the rows a delta names or
DISPLACES would have to draw, and it is what the equivalence family uses to name the partition-global
class its adequacy demand requires be reached. It is simply not what admits a step to the walk.

**The instrument's unit, stated because the saving will be misread otherwise.**
`Incremental.rowsEvaluated` counts **expression evaluations at steps** — one evaluation of one step's
expression against one row — and **ordering and windowing work is not on that scale**.
`DataFrame.evalPipelineWithInEnvCounted` charges `Filter` and `Derive` and nothing else, so a `rank`
refresh reports the same six-to-one saving the bounded frames report while still sorting each
partition on every refresh. That is consistent with `Sort` (D20, charged nothing) and with the bounded
frames (D24, charged nothing), and it is exactly what D21 means by one scale: the scale measures
expression evaluations, in every case, so a refresh and the full evaluation it replaced are
comparable. **The saving is real and it is not the window getting cheaper** — it is the steps before
the window no longer running over every row.

**The unit is deliberately NOT changed here.** Charging ordering work would move every recorded value
in every law, fixture and consumer assertion — the breaking change D21 was — and it would do so for no
consumer that has asked. A consumer that needs to compare two ORDERING strategies needs a second
instrument, not a redefinition of this one; that is its own decision, taken when such a consumer
exists.

## 2026-09-02 — D24: the frame, not the verb — `Window` and `Join` widened one class each, and the gate waived once

**Decided.** From `0.18.0`, `Incremental.plan` admits a `Window` whose frame is BOUNDED
(`RecomputeFrame`) and a `Join` whose kind is FILTERING (`FilterByRelation`), at any position, and
declines the rest by type with reasons that name the window function and the join kind rather than
the verb. D19's contract is untouched: every result still equals `DataFrame.evalPipelineWithInEnv`
over the same source; the widening is the additive reclassification `STABILITY.md` already declared.

**The gate D20 set was WAIVED for these two classes by operator decision, not met.** D20's rule is
that a class is widened only after a fixture records what it costs, and the shared corpus still
records no window or join footprint. The waiver's reasoning is that the consumer is not
hypothetical — the UI tier's live-transform grids are ranked and running-total columns and a live
table joined to a static lookup — and that the corpus vectors will be recorded from that consumer
after the fact. So this repository measured each class against its OWN vectors: the Phase 99/115
equivalence family, extended to generate both classes and both of their declines, and two vendored
before/after vectors written exactly as Phase 115's sort vector was. **The rule is unchanged and the
waiver is a one-off**: a future class still measures before it widens, and what a waiver buys is the
order of two acts, never the absence of one.

**Why a bounded frame is the line, and what it is NOT.** A bounded frame is the class whose output
for a row is a function of a fixed neighbourhood of it in its partition's order, so it is the class a
later phase can restrict to the rows a delta names or displaces. A partition-global one — a rank, a
bucket, a cumulative aggregate — never can: every row's output reads every preceding row. Declaring
the two alike would say the seam knows something about a cumulative aggregate that it does not.

**A FINDING that outlives the phase, and it should be read before the boundary is treated as
settled.** In THIS evaluator the admitted window column is recomputed wholesale over the walked
frame, and that is correct for every window function, not only the bounded ones — a window evaluates
no expression, so it costs nothing on the seam's instrument either way, and what actually admits a
`Window` to the restricted walk is that it PRESERVES THE ROW SET (one row in, one row out, in input
order, plus a column), which every member has. So the partition-global family could be admitted on
identical terms, with the identical saving, by relaxing one predicate. It was not, because the phase's
declared scope is the bounded frame and because the honest declaration of a step that reads the whole
partition is not "this step answers a delta". Whether the seam should trade that honesty for the
prefix saving on `rank` and `cumulSum` — which are precisely the two shapes the waiver's named
consumer wants — is an OPERATOR decision, recorded here rather than taken.

_(TAKEN, 2026-09-02: relax. See **D25** — `0.19.0` admits every window function on row-set
preservation, and the paragraph above is the reasoning it acted on. What survives of "why a bounded
frame is the line" two paragraphs up is the frame distinction itself, which is still true and still
the line a later per-row restriction would draw; it is no longer what admits a `Window` to the walk.)_

**Why a filtering join is admitted and a combining one is not, in the walk's own terms.** `Semi` and
`Anti` emit each left row at most once and unchanged, with the left schema only: that is a `Filter`
whose predicate reads a relation, and the walk's per-row model holds it with no new machinery — the
verdict is one cell in the per-row cache a filter already writes. `Inner` and `Left` fan one left row
out across every right row it matches, so one source row stops corresponding to one output row, and
the walk's token-to-row invariant — which the merged order, the maintained group and the whole cache
all rest on — no longer holds; admitting them means the in-flight row carrying a MULTISET, which is a
change to the seam's load-bearing shape rather than an extra case. `Right` and `Outer` additionally
emit rows for right rows no left row matched, which no left-row-local rule can produce at all.

**The cached verdict is conditioned on the RELATION, not only on the delta, and this is the part that
was nearly wrong.** A delta describes the source. A row it did not name can still have a different
verdict, because the relation may have gained or lost the key that row matched on — so
`IncrementalEval` carries the relation's key index per join step, and reuse is taken only while that
index is unchanged. The same reasoning forced a correctness fix one level up: the wholesale reuse of
a prior result (a quiet delta over a byte-identical source) now also requires that no step names a
**`Ref`** relation, because such a relation is whatever `resolve` returns at the moment it is called
and none of the state's three comparisons — pipeline, env, source — can see it move. That path could
hand back the previous relation's answer, and could do so for a DECLINED join too; no shipped
pipeline could reach it (an `Embedded` relation is pinned by `PipelineChanged`, and the convenience
entry points refuse a `Ref` outright), which is why it went unnoticed rather than unreported.

## 2026-09-02 — D23: D12 reaffirmed — no list-valued cell, and a demand for list-valued PARAMS is not a demand for one

Phase 122 asked whether the list-valued cell (`Explode` / `Split`, declined by Phase 101 under D12 and D13)
should be admitted now that the capability catalog records demand for `ListValuedParams`. It should not, and
the phase is retired without building. The recorded demand is for list-valued *parameters* — a multi-select
chip driving an `in` membership test — which D12 resolves by substitution with no change to the cell model.
A list-valued *cell* is a different thing: it reopens the closed flat scalar `Cell` set that the canonical wire
layout, `SchemaWalk` and every conformance law are total over, for two verbs no consumer has asked for. A
domain that needs to split a delimited string into rows does so as a source-side transform in that domain.
D12 and D13 stand; this entry exists so the next suggestion pass does not re-derive the question from the
same mislabelled signal.

## 2026-09-02 — D22: whether a sample was adequate is a law, not a review step

**Decided.** From `0.18.0` the conformance kit carries `SampleAdequacy`: a family DECLARES what its
sample must contain — every verdict its laws branch on, and the width its order-sensitive laws read
— and a sample that does not contain it FAILS the family, with the counts, rather than certifying
it. Every law family the kit ships is classified in `SampleAdequacy.census` as either `Guarded` or
`Unconditional` with its reason, and the suite reflects over the kit's public law entry points and
refuses any family the census does not name.

**Why a law rather than a convention.** A `LawResult` records that a law HELD. It cannot record how
many samples the law was reached BY, so a law gated on a generated condition reports exactly the
same green whether the condition arose two hundred times or never. This kit has had that defect
twice, and both times the sample was wrong rather than the law: the fold-confluence pack producing
150 halting trials out of 150, so the folding branch never executed; and the incremental-equivalence
family drawing tables that mostly held ONE row, so no tie between a named and an unnamed row ever
arose and a merge with no stability tiebreak passed every seed. Each was found by someone who
happened to look, in a kit that certifies eight domains. "Look each time" is not a property; a
declared demand that goes red is.

**Two demand shapes, because the two findings are different questions.** A verdict the laws
distinguish must be REACHED — the first finding's shape, and the one the earlier hand-written
coverage guards already had. A per-sample measure must SPAN the width the law needs — the second
finding's shape, and one nothing in the kit was watching at all, because table width is not a
verdict and no branch names it. The minimum is per-family and named by the family, since only the
family knows what its own laws read.

**Why the census is a declaration WITH a reflection check, rather than either alone.** A declaration
quantifies over what it names, so a family nobody enrolled produces no finding at any grade — the
blind spot every manifest-shaped check has, and the reason a store can hold nine files while the
class it governs holds twelve. Reflection alone would be the opposite error: it can tell that a
family exists, never that its evidence is built rather than drawn, which is a judgement about the
code that a reason string has to carry. So the census states the judgement and the suite refuses any
family missing from it — in both directions, since a row naming a family that was renamed away reads
as coverage while covering nothing.

**A guard reached once is not a guard passed.** The counterexamples say to WIDEN THE GENERATOR, in
those words, because the tempting remedies are both wrong: raising the iteration count and hunting a
seed until the count turns positive each leave the law certified by a single trial. Phase 106
measured that trap — across seeds 2200-2260 only three produced any folding lane set, and the best
produced one in three hundred.

**And the seeds did NOT move.** The phase that raised this anticipated re-baselining every pinned
seed under a re-drawn `ConfRng`. That work was already done: `ConfRng.intBelow` has drawn from the
high-order bits by rejection since `0.12.0`, kit-wide, with the single expectation it moved
inspected rather than re-pinned, and the only remaining `%` reductions in the kit's generators
consume a whole word to mint a fresh id, which is the case that argument explicitly exempts. So the
answer here is a confirmation, not a second re-seed — and re-drawing values a second time to
discharge a task would have moved every consumer's generated data for no reason at all.

## 2026-09-02 — D21: the footprint is one scale, and a prime reports what a prime did

**Decided.** From `0.18.0`, `Incremental.rowsEvaluated` counts row evaluations at steps in **every**
case — `Recompute.FullRecompute` carries the reference evaluator's own count rather than being
projected onto `RecomputeFootprint.SourceRows` — and a pipeline the plan DECLINES primes to
`Primed n` rather than to `FullRecompute`. An amendment to D19's instrument, not to its contract:
every result still equals `DataFrame.evalPipelineWithInEnv` over the same source, and no evaluation
result moves. What moves is the recorded values, which is why `STABILITY.md` records it as breaking.

**Why the projection was wrong rather than merely coarse.** `SourceRows` and "row evaluations at
steps" are different units. A pipeline with three evaluating steps over six rows performs eighteen
row evaluations and was charged six, so a declined refresh compared against its own full baseline
read as having done **less** work than the thing it fell back to. Every D20 measurement — and every
measurement a future widening will be gated on, by D20's own rule — is taken through this
instrument, so an instrument that reads backwards does not merely under-report: it inverts the
comparison the rule depends on. That it read *correctly* on the vectors that motivated D20 is
coincidence, not evidence — those pipelines have one evaluating step, where the two units agree.

**Why the count is taken in the reference DRIVER.** D19's first property is that the reference
evaluator stays the oracle, so deriving a full evaluation's cost anywhere else would be a second
semantics — a second thing that could disagree with the evaluator, in exactly the class of way that
is silent. `DataFrame.evalPipelineWithInEnvCounted` counts where the steps are actually taken, and
the existing entry points delegate to it.

**Why a declined pipeline's prime is `Primed`.** A prime evaluates everything whatever the plan
says, and has no prior state to fall back from, so `FullRecompute` there described a fall-back that
did not happen. The decline belongs to the REFRESH, which is where degrading is real, and the
question "will refreshes be restricted" already has a better answer than a footprint: `plan`
answers it before anything is evaluated, which is what D19 means by declaring the boundary as data.

**And why that is scoped to the plan-level decline alone.** A prime whose identity witness cannot
key the source still reports `FullRecompute (n, RowIdentityUnusable …)`. That defect depends on the
source DATA, so `plan` cannot see it and the footprint is its only channel — the discriminator is
not "is this a prime" but "could the consumer have asked beforehand". The unreachable `plan`/`split`
disagreement keeps `FullRecompute` for the same reason a defensive branch exists at all.

**The corpus consequence, and where it stops.** The two `incremental-recompute` vectors vendored
here were re-pinned to the corrected readings — only the sort vector moved; the control vector was
already on this scale, which is the control doing its job. Re-recording them in the specification
that owns the family is that specification's act, not this repository's, so the vendored bytes
deliberately lead the corpus until it happens, and the local `README.md` says so with the before and
after side by side. The conformance family's work law, which could only be stated over the
restricted classes while the two scales existed, now runs over every sample it generates.

## 2026-09-02 — D20: a `Sort` is merged, not declined — and a class is widened only after it is measured

**Decided.** From `0.18.0`, `Incremental.plan` classifies a `Sort` as `MergeOrder by` rather than
`FallBack (StepNotRowLocal "sort")`, and admits it at **any** position in a pipeline. D19's contract
is untouched: every result still equals `DataFrame.evalPipelineWithInEnv` over the same source, and
the widening is the additive reclassification `STABILITY.md` already declared — the answers do not
move, only the cost.

**Why a sort is not a fall-back, and not row-local either.** A sort computes nothing. What a delta
moves is a row's POSITION, and a position is recoverable: the rows the delta did not name are still
in the order the previous evaluation put them in, so the new order is that order with the named rows
lifted out and merged back under the reference's own comparator. So `PropagateRows` would be a wrong
answer to "does this step's output for a row depend only on that row", and `FallBack` would be a
wrong answer to "can this step answer a delta". It gets its own case, which is what "the boundary is
declared as data" means when the boundary moves.

**Why any position, when a `GroupBy` is admitted only as the last step.** The `GroupBy` condition is
not about position, it is about what the step EMITS: a group table, over which no delta was supplied,
so the steps after it would be reasoning about a change nobody described. A sort emits the rows it
was handed. Every step admitted after it reads the order it produced exactly as it would have read
the reference's, including the two order-sensitive readers the seam already has — a derived column's
whole-column type inference, and a group's ordered member list — both of which are computed from the
walked frame rather than from a cache.

**The saving is not in the sorting, and saying so is the point.** A sort evaluates no expression, so
it contributes nothing to `rowsEvaluated`, exactly as a `GroupBy` contributes none. What the widening
buys is that the steps BEFORE the sort stop re-evaluating every row — which is the whole cost a
declined pipeline was paying. Measured on the shared recompute fixture family: a filter-then-sort
pipeline over six rows with one cell edited falls from six row-evaluations to one. The footprint
vocabulary therefore gains no case; a sort-bearing row-local pipeline reports `RowsRecomputed`.

**The third order-sensitivity, and why it is a stored ARRIVAL order rather than a cleverness.** A
stable sort is a sort by (key, arrival position). Reusing a cached order for the unnamed rows is
therefore sound only while those rows ARRIVE in the same relative order as they did when it was
recorded — so `IncrementalEval` stores, per sort step, both the order the step's rows arrived in and
the order it produced, and the merge is taken only when the unnamed subsequences agree. This is D19's
ordered-member condition one verb along, and it is live for exactly the reason D19 gives: `Delta.diff`
reports a pure reordering as *quiet*, so "the delta named nothing" must never be read as "nothing
moved". A merge that skipped the check answers a delta that named no row with a table in the wrong
order, and nothing in the delta would have said so.

**A class is widened only after a fixture records what it costs, and `Window` is the standing case.**
The shared `incremental-recompute` fixture family records a footprint for the declined sort and none
for a window, so `Window` stays `StepNotRowLocal "window"` — not because a bounded frame is
unanswerable, but because widening it would be an unmeasured claim. The rule is the phase's own gate
and it is kept as a decision: measure, then widen. Two vectors of that family are vendored under
`tests/Fuaran.Core.Tests/fixtures/incremental-recompute/` so the claim is checked in this repository
rather than asserted about another one — one being the control whose recorded footprints must not
move, the other the sort vector whose result must not move and whose class must.

**One finding that outlives the phase: the equivalence family's tables were too small to test an
ORDER law.** Its generated tables held one to five rows and most held one, which is ample for the
row-local and group-local laws D19 wrote it for and cannot exercise a tie between a named row and an
unnamed one — so a merge with no stability tiebreak passed the whole family. The corpus now generates
one to nine rows and sorts on the deliberately tie-heavy column, and it catches that defect. A
generative family is only as strong as the shapes its generator can reach, and a law about ORDER needs
rows to have an order worth getting wrong.

## 2026-08-21 — D19: incremental evaluation is a RESTRICTION of the reference evaluator, and the boundary is data

**Decided.** From `0.11.0`, `Fuaran.Core.DataFrame` carries `Incremental` — a `Transform` pipeline
evaluated against a `TableDelta` (D18) instead of from scratch. `plan` classifies every step as
`PropagateRows`, `MaintainGroups`, or `FallBack` with a typed reason; `prime` builds a state over a
source; `refresh` advances that state against a delta. Every result is equal to
`DataFrame.evalPipelineWithInEnv` over the same source, for every delta, whichever internal path ran.

**Why it computes through the reference evaluator rather than beside it.** An incremental evaluator
recomputes a subset of what a full evaluation recomputes, so the two agree only if the subset is
computed by the same code. Three private primitives were therefore exposed as one-line wrappers —
`evalExprInRow`, `aggregateCells`, `inferCellType` — rather than reimplemented behind the seam. This
is D16's lesson applied one layer up: a hand-copied semantics diverges silently, and the copy nobody
is thinking about is the one that breaks. The alternative on offer was a second evaluator kept in
step by a test suite, which is a maintenance promise rather than a property.

**Why the fall-back is a first-class typed outcome and not an internal detail.** A sort, a window, a
join and a whole-relation set op each produce a row whose value depends on rows the delta does not
name. There is no honest incremental answer for them, and an approximation would be a wrong one. So
the seam declines, in the type, before evaluating anything: a consumer can ASK
`Incremental.plan` whether a pipeline will be restricted and read the reason if not. That makes
adoption per pipeline rather than per application — a declined pipeline costs exactly what it costs
today and sits beside an adopted one. Every runtime condition the path cannot honour (a changed
pipeline or env, a moved schema, a `FullRefresh` delta, an ordinal-addressed delta, an unusable
identity witness) takes the same route: full evaluation, reason recorded in the footprint. Degrading
is always available and always correct, which is what makes the seam safe to adopt gradually.

**Why the footprint is part of the contract and not diagnostics.** An incremental evaluator that
quietly recomputed everything would pass an equality suite perfectly — the answers would all be
right. So the work done is recorded as data (`Recompute`: `Primed` / `ReusedPrior` /
`RowsRecomputed` / `GroupsRecomputed` / `FullRecompute reason`) and the conformance family asserts on
it alongside the equality. Counts only, no clock, so the numbers are deterministic and identical on
every host — which is what lets a consumer assert on them in its own tests, and lets the regression
"this refresh started recomputing everything" be a failing assertion rather than a stopwatch reading.

**The two order-sensitivities that are handled rather than assumed.** Both are silent when got
wrong, and both are exactly what a first implementation misses. (1) A `Derive`d column's TYPE is
inferred from the whole column, so it is recomputed from the rows alive AT THAT STEP even when no
cell moved — a filter later in the pipeline that drops the only typed row moves the type, and no
per-row cache can know that. (2) A group's aggregate depends on its members' ORDER — `First` / `Last`
read position outright and a float `Sum` is order-sensitive in its last bits — so a cached aggregate
is reused only when the group's ordered member list is byte-identical, never merely because no row in
it was named. This is sharper than it looks: `Delta.diff` reports a pure row REORDERING as *quiet*,
because every key is present at both ends with identical content, so "the delta named nothing"
cannot be allowed to mean "nothing moved". The wholesale reuse of a prior result is therefore
conditioned on the source itself being unchanged, not on the delta being quiet.

**Ordinal-addressed deltas are declined, not supported at reduced quality.** D18 reserves the
`ordinal` scheme for a source with no identity. A cache keyed by position is invalidated wholesale by
any insert, so the seam refuses rather than offering a saving that evaporates on the first
non-append. Identity is what the SEAM needs; it is not what the ANSWER needs, which is why a witness
that cannot key the source degrades to a correct full evaluation rather than failing the call.

**The footprint type is `RecomputeFootprint`, not `Footprint`.** `Fuaran.Core.Ops` already publishes
`Fuaran.Core.Footprint` (the op-script address set from the arbitration work). Two same-named types
in one namespace across two packages is a collision for any consumer that opens both, and the
compiler found it the moment the conformance project referenced both — which is the argument for
keeping the law kit referencing every package rather than only the ones it tests.

## 2026-08-21 — D18: the column layer's delta is a monoid with four row states, and identity is a per-call witness

**Decided.** From `0.9.0`, `Fuaran.Core.DataFrame` carries `TableDelta` — what changed in a columnar
table — as a two-case type: `FullRefresh`, and a `RowSet` of identity-addressed row changes plus
column invalidation. `compose` is total and associative for every pair of inputs, `FullRefresh`
absorbs, and `empty scheme` is a two-sided identity within one identity scheme. Identity arrives as
`RowIdentity<'Id>`, a per-call argument.

**Why there are FOUR row states rather than three.** `RowAdded` / `RowChanged` / `RowRemoved` is the
obvious set and it is not closed under composition. A row change is exactly a
`(existed-before, exists-after)` pair, composition takes the first's *before* and the second's
*after*, and `Added ∘ Removed` is therefore `(absent, absent)` — a shape none of the three has.
Collapsing it to "no change" is what a first draft does, and it costs associativity on precisely the
inputs a re-add-then-drop sequence produces: `(Added ∘ Removed) ∘ Changed` is `Changed` while
`Added ∘ (Removed ∘ Changed)` is nothing. So `RowTransient` is not a fourth case bolted on for the
algebra's convenience; it is the missing element, and naming it is what makes the associativity claim
true for *all* inputs instead of true for the consistent ones. It also earns its keep on its own
terms: a consumer holding a per-row cache keyed by identity must evict a transient key, which the
three-case reading would have told it nothing about.

**Why a scheme mismatch degrades to `FullRefresh` rather than throwing.** Two deltas minted under
different identity schemes describe incomparable key spaces, so no precise composite exists. The top
element is not a failure value here — it is the accurate one, and it says exactly what is true
("everything may have changed"). A caller that would rather be stopped than be given a coarse answer
calls `composeChecked`, which refuses with `SchemeMismatch`. Both are available because the two
postures are genuinely different jobs: a folding pipeline wants the total function, an operator-facing
path wants the refusal.

**Why identity is a per-call witness and not a field.** The columnar strand is deliberately
witness-free — `Column` / `DataFrame` introduce no base type and no permanent witness field — and
that property is worth more than the convenience of a `RowId` column baked into `Table`. So the
delta is told *how* to key a row (`RowIdentity<'Id>`: a scheme name, a reader, a string rendering)
and knows nothing about what a key means, the same posture `Propagation` takes with its dependency
relation. `RowIdentity.byColumn` / `byColumns` are reference witnesses, sufficient to exercise the
whole surface with no domain dependency of any kind.

**Ordinals are a checked exception, not a fallback.** A delta declares its scheme, and the reserved
scheme `"ordinal"` is the only one whose refs may be positional; every other scheme must address by
key, and the two may not be mixed inside one delta. `validate` enforces both directions and the wire
decoder runs the same check, so "ordinals only where no identity exists" is a property of the type
rather than a note in a doc comment. This is the same rule `SkeletonOp` established for the tree
strand at `0.2.0` — where a collection's members have identity, they are addressed by it.

**One canonicalisation, called twice.** Deciding whether a row's content changed uses the pinned
`cellToken` the columnar strand already partitions by, exposed rather than re-implemented. D16's
lesson was that a hand-copied canonicalisation diverges silently and the copy nobody was thinking
about is the one that breaks; a delta layer that answered "are these two rows the same" by a second
rule would be exactly that, with the added cost that its answer would disagree with `Distinct`'s.

## 2026-08-21 — D17: the IDL splits into a model half and a codegen half, and both ship

**Decided.** From `0.8.0` the IDL engine is two packages. `Fuaran.Core.Idl` holds the model, the
codec, the sampler, the `idl.json` artifact projection and the sanitisation floor; a new
`Fuaran.Core.Idl.Codegen` holds the source emitters, `CodegenError`, the codegen trust boundary and
the stability diff classifier. Both are packable. The namespace does not split — `Gen`, `Trust` and
`Diff` stay `Fuaran.Core.Idl`, so an existing call site changes its package reference and nothing
else.

**The reason is portability, and it was measurable rather than aesthetic.** `Fuaran.Core.Idl` was the
one project under `src/` absent from `tests/fable-smoke`, so "Core is Fable-clean on encode and
decode" had an exception nobody had proven either way — and the downstream browser hosts are Fable. The
obstruction was entirely emitter-side: one `CultureInfo.InvariantCulture` and two `StringBuilder`s,
all three serving the TypeScript source backend. The model, the codec, the sampler and `Sanitize`
touch none of it. Splitting therefore turned an unprovable claim into a gated one by moving the
obstruction rather than by working around it, and `tests/fable-smoke` now compiles the model half like
every other public package.

**The split boundary was already there; it did not have to be invented.** `Encode.encode` returns
`Result<string, string>` and never mentions `CodegenError`, so the error type genuinely belongs with
the emitters that raise it. The sampler's `Rng`, `nextInt`, `pick` and three pools are private to it
and used by no emitter — it sat inside `Gen` by where it was written, not by what it depends on, and
extracting it into `Sample` was a lift.

**Rejected: making the codegen half `IsPackable=false`.** The phase that specified this split was
authored when the whole project was unpackable, and proposed keeping the emitters that way on the
grounds that publishing a generator creates a second, implicit contract — consumers depend on its
OUTPUT, which is harder to version than an API. That argument is sound and is now written down (see
STABILITY.md's "two packages, two promises"), but the conclusion no longer follows: **D14 published
the generator one day earlier, deliberately and with reasons, and named `Gen.fsharpModuleWith` as the
call a second domain makes.** Un-publishing it as a side effect of a portability fix would have
withdrawn that the day after it was granted, and would have left the second domain back where D14
found it. What the split changes is that a consumer now **chooses** the generator instead of
inheriting it with the model — which is the part of the original argument that was actually about
coupling.

**Two smaller consequences, both deliberate.** `Fuaran.Core.Idl.Codegen` ships **no** `fable/` source,
unlike every other packable project here: the dual-pack convention promises a Fable consumer can
compile from source, and this is the one package that cannot keep that promise. And
`TransparentUnion` became public: the split made a genuine cross-package dependency out of what
`internal` had been hiding, since an independent emitter must agree with this codec about which union
cases encode bare or it generates a host that disagrees on the wire. It remains a wart — the rule is
keyed on a hard-coded vocabulary name in a domain-generic engine — and publishing the accessor makes
that visible rather than fixing it.

**Not decided here.** Whether the two halves should ever run on separate version lines. They move
together at `0.8.0` because they were one package a commit ago; a divergence needs a reason, and none
exists yet.

## 2026-08-21 — D16: `fnv1a` is made cross-pipeline exact, and the .NET side is the canonical one

**Decided.** From `0.6.0`, `Hash.fnv1a`'s multiply goes through a private split-half 32-bit form
(`mul32`), so the function computes true 32-bit FNV-1a on .NET **and** under Fable. The .NET values
are unchanged; the transpiled values move to meet them. Released as a MINOR bump, not a patch.

**Why there was anything to fix.** Fable emits `uint32` `*` as a plain JavaScript multiply on
doubles. `h * 16777619u` reaches roughly 3.6e16 — past the 2^53 exact-integer ceiling — so precision
is lost *inside* the operation, and a trailing `&&& 0xFFFFFFFFu` cannot recover what is already gone.
Measured: `fnv1a "a"` was `e40c292c` on .NET and `e40c2930` under Fable, and 120 of a 124-entry
corpus diverged. This is the same hazard the `.+.` masked add solves for addition, one order of
magnitude worse, and it went unnoticed for the same reason: nothing in the repo compared the two
pipelines' *numbers*.

**Why .NET is canonical rather than "whichever is cheaper to change".** `fnv1a` is folded into every
stored content hash, and essentially all of them were minted by .NET processes. Moving the .NET
values would invalidate persisted data across every consuming domain; moving the transpiled values
invalidates only digests minted by a JavaScript host, which the pre-`0.6.0` documentation already
declared non-portable and unusable as a cross-pipeline identity. So the direction was not a
preference — one side had data behind it and the other had a warning label. The fix was designed
around keeping .NET fixed, and the pinned vectors hold it to that.

**Why MINOR and not PATCH.** By the .NET-only reading this is invisible: same signature, same values,
a private helper added. But STABILITY.md declares that these functions' *values* are the contract,
not merely their signatures — and on a supported pipeline the values change. A patch bump would tell
a Fable consumer that nothing observable moved, which is false. Pre-1.0, MINOR is the lane that says
"look before you repin", and this is exactly that.

**The fix had to reach six implementations, not one — and that is the substantive part.** The spine
carried six copies of FNV-1a, each inlined at some point so a package need not take a `Tree`
dependency, and each commented as "the same arithmetic class as the rest of the substrate's portable
hashing" — a claim that was false in every copy. Fixing only `Hash.fnv1a` would have left
**`OpStream.defaultHash` divergent**, which is precisely the harm being fixed: it is the op-stream
chain hash, so two hosts replaying one log would still compute two different chains, while the
release notes said the hash was now portable. That is a worse outcome than not fixing it, because it
converts a known limit into a false assurance. Three copies are deleted outright — `Function` and
`Query` already depended on `Tree`, and `Function.fs` was calling `Hash.fnv1a` two lines from its own
duplicate. Three remain because the dependency genuinely forbids consolidation, and they are now
**verified rather than trusted**: `HashTests` compares each against the canonical function over a
corpus, and the parity probe carries a column per implementation.

**Why the probe covers every implementation rather than the canonical one.** This is the general
lesson, not a detail of this change. A probe that samples the function you were thinking about will
report green for the reason you expected while the copy nobody was thinking about stays broken —
which is what an audit of the downstream consumers found here, hours after the canonical fix was written and
believed complete. Going red was verified per implementation, not once: perturbing `OpStream`'s copy
alone turns exactly its column red and leaves the other three untouched.

**Why the guard is a probe and not a test.** A compile gate cannot disagree about a number, and
neither can a .NET suite: reintroducing the naive multiply was measured to leave all 720 tests green
while the transpiled side was wrong on 120 of 124 inputs. So the certification is bought twice over —
an independent 64-bit reference implementation inside `HashTests` pins the .NET half against a
mistake in the split multiply itself, and a scratch Fable build of a corpus run under node and
byte-compared pins the cross-pipeline half. Both were taken go-red before being trusted. The standing
obligation, recorded in `Hash.fs` and STABILITY.md: re-run the probe when either multiply-safe helper
is touched.

## 2026-08-21 — D15: the cryptographic digest is homed here; the default chain hash is not changed

**Decided.** `Hash` ships a pinned pure SHA-256 (`sha256Hex`, `sha256Bytes`, `sha256HexOfBytes`, and
the `utf8Bytes` encoder they hash through) from `0.5.0`. It is the spine's one cryptographic digest,
and there will not be a second. `OpStream.defaultHash` stays FNV-1a.

**Why it is here and not in each consumer.** It already existed twice — hand-ported, verbatim, into
two separate tiers, the second port two days before this decision — because both needed a digest that
was FSharp.Core-only and Fable-clean, and the spine offered only a 32-bit checksum. Two copies of one
crypto primitive is not redundancy, it is a divergence waiting for the patch that reaches one of them:
they were identical on the day of the second port and nothing structural kept them so. The FIPS
vectors have to travel with the implementation for the same reason — a copy that is not itself pinned
is a claim rather than a digest, and each porting tier had to re-derive that suite to know what it
had.

**Why the default chain hash does not move with it.** The tempting follow-on — now that a real digest
is available, make `OpStream.defaultHash` use it — is refused. Every persisted chain in every domain
was written under FNV-1a, so changing the default silently invalidates all of them: a store would
verify before the upgrade and fail after, with nothing in the data saying why. A host that wants
adversarial tamper-evidence supplies `Hash.sha256Hex` through the `HashFn` seam, which is what the
seam is for, and a domain that migrates does so as a recorded event rather than a rebuild. Making it
available and making it default are separate decisions, and only the first is taken here.

**Why this reverses "no cryptographic hash ships in Core (GP3)".** That line conflated two things.
GP3 asks that public surfaces be FSharp.Core-only and Fable-clean; this implementation is both — pure
`uint32` arithmetic, no `System.Security.Cryptography`, compiled by the same Fable gate as every
other public package. What GP3 actually rules out is a host-side crypto *dependency*: keys, keyed
MACs, signers, certified modules. None of those are here and none are proposed; they stay behind the
host's attestation seam. The cost of the conflation was paid entirely by consumers, in ports.

**The two regimes are named, and that naming is the contract.** `fnv1a` is a cache fingerprint — a
staleness stamp over data the same process just produced, where forging it gains nobody anything.
`sha256*` is the crypto digest — anything that becomes a signed head or a record a dispute is read
from. A 32-bit second pre-image is seconds of search, so the two must never be interchanged; they are
separately named so that a call site says which regime it is in, and they differ in output length so
that a silent fallback is caught by shape rather than by review.

**`fnv1a` moved file and did not change.** The `Hash` module now lives in its own `Hash.fs` rather
than at the head of `Tree.fs`, because the digest is consumed across the spine and by domains that
never touch a tree. `fnv1a` and `foldSep` are byte-identical through the move — pinned by their own
vectors in the suite, since every stored content hash downstream folds through them.

## 2026-08-20 — D14: the IDL engine ships; a vocabulary does not

**Decided.** `Fuaran.Core.Idl` is packable from 0.4.0 and is distributed like the rest of the spine.
A **vocabulary** — the `Idl` value describing one domain's kinds, unions, enums and records — is
**not** distributed from here. It lives as data in the repo of the domain whose contract it is,
which takes a `PackageReference` on the engine and declares against it. There will be no
`Fuaran.Core.Idl.Vocabularies.*` package, and no vocabulary-shaped module in any existing one.

**Rejected: a shared vocabularies package.** It reads as the tidier option — one place to look, one
version to pin — and it is wrong on three counts.

1. **A vocabulary is the domain's contract, so it must move at the domain's cadence.** Housed here,
   every kind a domain admits becomes a release of *this* repo, and every domain's wire inherits one
   release cadence and one reviewer. The admission gates that govern whether a kind may exist are
   written per domain and enforced per domain; the artefact they govern should not sit somewhere the
   gate does not run.
2. **A domain-named type in the `Fuaran.Core.*` namespace outlives every later correction.** The
   spine is domain-agnostic by charter — D7 already refuses a domain dependency even in tests,
   holding the reference witness locally instead. A `Fuaran.Core.Idl.Vocabularies.Ui` would put a
   single domain's vocabulary in the substrate's identity permanently, which is the one kind of
   mistake a rename cannot undo cheaply.
3. **It concedes the point the engine exists to prove.** The engine is generic because a vocabulary
   is an ordinary value a caller supplies. Shipping vocabularies *with* it would make the generic
   surface indistinguishable from a plugin registry, and the second domain's declaration would be
   evidence of nothing.

**What this unblocks, and it was genuinely blocked.** Until 0.4.0 the engine was `IsPackable=false`,
so a domain workspace could not consume it at all. The one domain using it — the F# UI tier — got
its generated structural layer by reaching across a sibling checkout and byte-copying the artefact
this repo's tests happen to commit. That is not a distribution mechanism; it is the absence of one,
and it is why no second vocabulary had been declared. With the engine packaged, a domain declares an
`Idl`, calls `Gen.fsharpModuleWith`, and owns both the declaration and the output.

**The one exception, and why it is an exception rather than a counter-example.**
`tests/Fuaran.Core.Tests/UiIdl.fs` is a UI vocabulary living in this repo. Under the rule above its
home is the UI tier's own repo, and it is not moved here. It is worth being exact about what it
currently *is*: not the UI domain's vocabulary home, but **this repo's engine-certification
fixture** — the only full-scale vocabulary the engine has ever been proven against, and the input to
seven suites that between them certify corpus byte-parity, the compiled-codegen drift guard, the
schema leg, the op leg, the diff classifier and the cross-host fuzz.

It cannot follow the rule yet because neither available route is sound:

- **A package dependency the other way closes a cycle.** The tier depends on `Fuaran.Core.Idl`; this
  repo's tests depending on a tier package would make the two unbuildable from cold in either order.
- **A compile-link across a sibling checkout is worse than the byte-copy it replaces.** It would
  make this repo's build fail whenever a checkout it does not control is absent or moved — trading a
  drift hazard for an availability one.

So the migration is **staged, with a stated completion criterion**: the vocabulary moves to the UI
tier's repo, taking its regeneration guard with it, once the engine's certification no longer rests
on it — either because a Core-owned fixture is grown to comparable scale, or because a second domain
certifies the engine in its own repo. Until then the duplication is bounded to one artefact and
pinned rather than trusted: the tier's committed `Generated.fs` is byte-compared against the
emission on every CI run, so a divergence fails a gate instead of accumulating silently.

**Amended 2026-09-02 (Phase 114) — the ENGINE side of that criterion is met; the move itself is
not yet made.** Two things changed, and neither is the deletion.

*The vocabulary is now LOADABLE.* `Artifact.parse` inverts the `idl.json` projection and
`SupportArtifact` carries the declared-support record and the host-prelude declaration as a second
document, so the three files a regeneration needs are files a domain holds. The route this entry
called unsound — a compile-link across a sibling checkout — is no longer the only alternative to
the byte copy, because there is now a data route that needs neither.

*The certification no longer rests on the UI vocabulary.* Neither of the two routes above is the one
taken. A Core-owned fixture was NOT grown to comparable scale — a 40-kind vocabulary in this repo
would be a domain in all but name, and would re-make the mistake this entry refuses. Instead the
load is split across three vocabularies no domain owns: the two vendored foreign ones, whose corpora
were written outside this repo, and a small `refIdl` covering the part of the type model neither of
them happens to use. The claim is **enforced, not asserted** — a test walks their union and fails if
any `IdlType` or `Optionality` case is unreached, so "the certification rests on a domain's
vocabulary" is a condition the suite detects rather than a judgement someone re-makes. The codegen
trust boundary moved onto the neutral vocabulary in the same pass, because it is engine behaviour
and would otherwise have left with a domain's contract.

*What remained, deliberately — and no longer does.* Phase 114 left `UiIdl.fs` and `UiGenerated.fs`
in place, with the byte-pin, until the vocabulary landed in the domain's repo: deleting them first
would have left that domain with no vocabulary at all for the interval. It has landed, and they are
gone — see the amendment below.

**Amended 2026-09-03 (Phase 123) — D14 is CLOSED: the fixture is deleted.** The first domain homed
its vocabulary, its declared-support record and its host prelude in its own repository and now
regenerates its structural layer in-process against the packaged engine, with nothing read from
outside it. The interval this entry protected is over, so the exception this entry granted ends
with it.

*What left.* The four fixture files — `UiIdl.fs`, `UiIdlSupport.fs`, `UiHostPrelude.fs`,
`UiGenerated.fs` — and the vendored `snapshots/ui.json` beside them; and, because a fixture is only
ever as removable as the suites it feeds, **all seven of the suites named above**: `IdlUiTests`
(corpus byte-parity), `IdlUiGenTests` (the compiled-codegen drift guard), `IdlArtifactTests` (the
artifact leg), `IdlSchemaTests` (the schema leg), `IdlOpTests` (the op leg),
`IdlFullVocabularyFuzzTests` (the cross-host fuzz), and the three scale cases of `IdlDiffTests`
(the diff classifier), which are re-pointed at the reference vocabulary rather than deleted because
the other twenty-two certify the classifier over vocabularies authored for the purpose. The
`--emit-idl` entry point went with the artifact it rendered, and `--spike-proposal` — which named
the vocabulary because the vocabulary happened to live here — now takes it as an argument, read
through the `Artifact.parse` that Phase 114 added.

*What the engine's certification rests on now, in full.* The union of three vocabularies no domain
owns: the two vendored foreign ones and `refIdl`. That is not an assertion — `IdlCertificationTests`
walks their union and fails if any `IdlType` or `Optionality` case is unreached, and the
sanitisation floor and the codegen trust boundary run over `refIdl` against a policy that shares no
token with the default. Every engine MECHANISM the seven suites exercised is still certified here:
`Gen.jsonSchema` by the mini-IDL, wire-shape and enum-wire families; the op root's round-trip by
`IdlCertificationTests` over `refIdl`, which declares ops where both foreign vocabularies declare
none; and the three-way interpreter / generated-F# / generated-TypeScript comparison by the
second-vocabulary spike and by the mini-IDL's generative sweep.

*What left with them, stated rather than implied.* What those suites had that nothing here replaces
is not a mechanism but a SCALE and a CORPUS: a generated schema evaluated by an off-the-shelf
Draft 2020-12 validator against a real domain's committed wire corpus; an op codec round-tripped
byte-for-byte against that corpus's op family; and a generative cross-host sweep over forty-odd
kinds rather than eight. Those are certifications OF A DOMAIN'S CONTRACT, and this entry's whole
argument is that a domain's contract is gated where the domain's gate runs. Re-standing them there
is the domain's work and is recorded as such; a Core-side substitute would mean growing a
full-scale vocabulary here, which is the route this entry rejected on its own terms.

## 2026-08-18 — D13: the compute vocabulary's closed sets, and what is deliberately absent (Phase 101)

A demand-side census of the transform algebra (enumerate the intents a declarative pipeline must
express, then check each is EXPRESSIBLE — rather than waiting for a failure to harvest) found the
verb set close to complete, with the remaining gaps concentrated in **asymmetries of otherwise-closed
sets**. Those are the cheapest gaps to close and the most expensive to leave: a reader who finds one
member of a familiar pair reasonably assumes the other.

**Closed (all additive — every pre-existing pipeline's wire is byte-unchanged).** `Transform` gains
`Intersect` / `Except` beside `Union`, as **multiset** ops keyed on the full row (`· Distinct`
recovers the SQL set forms, exactly as it does for `Union`); `JoinKind` gains `Semi` / `Anti`;
`AggFn` gains `CountDistinct`; `WindowFn` gains `DenseRank`, `CompetitionRank`, `NTile n`,
`CumulMax`, `CumulMin`, `RollingSum`; `ScalarFn` gains `Sqrt`, `Least`, `Greatest`, `IndexOf`.
Row identity for the set ops and for `CountDistinct` is the **same canonical token `Distinct` dedups
on** — so `NaN` is one value, `-0.0`/`0.0` coincide, `Null` matches `Null`, and an `Int 1` never
matches a `Float 1.0`. That is a different rule from a `Join` key (`cellEq`, where a null matches
nothing), and the difference is intentional: it is what SQL's set operations do and what makes the
result host-identical rather than host-comparison-dependent.

**Two corrections to the census the closure produced, worth more than the additions.**

1. **`Rank` was already DENSE.** It computes `1, 1, 2` — SQL's `DENSE_RANK()`, not `RANK()`. So the
   missing member of the ranking family was never "dense rank"; it was the **gapped** rank, which had
   no spelling at all. `DenseRank` is therefore the explicit (byte-identical) name for what `Rank`
   already does, and `CompetitionRank` is the genuinely new capability. `Rank` is **not** re-pointed
   at the gapped semantics: that would silently change every existing pipeline's output — a major
   bump, not an additive one.
2. **`Semi` is not expressible; `Anti` is.** The prior reading was that both reduce to `Left` + an
   `IsNull` filter. `Anti` does (`cellEq` never matches a null, so a matched row's right key is
   always present, and each unmatched left row yields exactly one output row). `Semi` does **not**: a
   `Left` join has already fanned a left row out once per right match, and no downstream filter undoes
   that — a `Distinct` would also collapse duplicates the input legitimately carried. That is the
   argument for the case, and it is asserted as a test rather than left as prose.

**Declined, with reasons, so the next census does not re-file them.**

- **No clock (`Now` / `Today`).** The evaluator is a pure function of `(table, env, pipeline)`. A
  clock makes the same pipeline over the same data produce different answers — which is precisely
  what `Conformance.transformLaws` byte-identity and deterministic replay exist to forbid. The
  intended route is a host-injected `Param` (D12's binding mechanism): bind `"today"` once at the
  edge and the pipeline stays total; `DateDiffDays` does the arithmetic.
- **No regex.** A pattern is not a cross-host value: .NET, JS, Go and Rust differ on syntax, escapes
  and Unicode classes, and the backtracking engines differ on worst-case *time*, which a total
  algebra cannot absorb. `Contains` / `StartsWith` / `EndsWith` / `IndexOf` / `Substr` / `Replace`
  cover the common intents; anything else is a host-side derived column.
- **No `Pow` / `Log`.** IEEE-754 does not require transcendental functions to be correctly rounded,
  so two conformant hosts may differ in the last ulp — and one ulp is a different byte in the
  canonical float layout, i.e. a parity failure. `Sqrt` **is** IEEE-754-exact, which is why it is
  present and they are not; integer powers compose from `Mul`.
- **No explode/flatten and no `Split`.** Both need a list-valued cell, and D12 rejected exactly that
  (`CellList`) for blast radius and model coherence. `Unpivot` is wide→long **across columns** and
  genuinely does not cover it. So the gap is in the *type model*, not the verb set: closing it is a
  major model change, not an additive verb, and until then a host flattens its JSON before handing
  Core a table — the same materialisation it already performs for every other source.
- **No `PadLeft` / number formatting.** The algebra pins **values**; presentation belongs to the
  render tier, which knows the locale and the column width and this does not.

Naming note: the two-argument extremes are `Least` / `Greatest` (the SQL spelling) rather than
`Min` / `Max`, because `AggFn` already owns those two names in this namespace and a second binding
would shadow it for every unqualified use.

## 2026-07-19 — D12: list-valued params resolve by substitution, not an env change (Phase 91)

The multi-select-chip binding is `ColExpr.InParam of ColExpr * name`, riding the existing `in`
wire tag with `param` in place of `items` (exactly one of the two — the same duality as the flat
filter step's `param`/`value`), resolved by **substitution** (`substituteListParams :
Map<string, Cell list> -> …`, rewriting `InParam(x, n)` to `InList(x, <literals>)`) — mirroring
how scalar `Param`s resolve via `substitute` under the certified `paramLaws`. Rejected: (a) a
`CellList` `Cell` case — the widest blast radius (every codec, aggregate, comparator, all nine
hosts) and list cells are meaningless in the columnar model; (b) an `EvalEnv` shape change —
breaks the public eval API for one feature when substitution already models binding; (c)
host-side expansion — forks the declarative pipeline artefact per host. Consequences: an
`InParam` reaching evaluation unbound is a strict `UnboundParam` (scalar-param parity); "empty
selection ⇒ no constraint" stays host-side pruning policy; `paramsOf` surfaces list params in
the scalar namespace, so reactivity/lease derivation is unchanged. Demand evidence: the
2026-07-19 capability sweep + a downstream consumer's capability-demand log.

## 2026-07-09 — D11: public conformance is pinned to `canonicalConfig`; bespoke chain pre-images are non-portable

`StreamConfig.Payload` (the pluggable chain pre-image binding, Phase 255) has two legitimate uses
that pull in opposite directions: a **migration seam** — a domain whose persisted streams use a
legacy chain format (Documents `"%d|%s|%s|%s"` + `"genesis"`, Calc / Geom their own) verifies the
existing streams under a bespoke `Payload`, then `rehash`es to the canonical form, with no flag-day
re-hash of history — and an **interop hazard**: a stream hash-chained under a bespoke pre-image
verifies only for a reader who already knows that config, so it is not portable across independently
built hosts. Decision: **public / cross-host conformance is pinned to `OpStream.canonicalConfig`**
(the `{seq, actor, op}` envelope + `""` genesis). A stream claiming the canonical `core@1.0`
profile MUST verify under `canonicalConfig`; a bespoke `StreamConfig.Payload` is a **host-private
profile, non-portable by declaration** — legitimate only as the transient input side of the
`verifyChainWith <legacy>` → `rehash <legacy> canonicalConfig` migration, never as a shipped
interchange format. No new conformance law is required: `Conformance.streamLaws` already exercises
append / verify / replay over the default `canonicalConfig` pre-image, so "the canonical profile is
verifiable under `canonicalConfig`" is **definitional**, not a testable property — and the
non-portability of a bespoke pre-image is a **declared boundary**, not something a property test can
assert. Recorded in STABILITY.md ("Chain pre-image portability"). Design decision (2026-07-09).

## 2026-07-09 — D10: content-addressed side-tables are the sanctioned host-private-metadata mechanism

Attaching host-private semantics to the public content-addressed identifiers Core already computes
for integrity reasons — node ids, chain heads, `Tree.contentHash` / `encodeHash`,
`Validator.canonicalCodes` — via **host-side lookup tables keyed by those ids** is the **sanctioned**
mechanism for private per-artifact metadata. It needs no public metadata slot: a content address is
an unforgeable foreign key (tampering changes the key), so a host joins its own off-repo tables
without the public tier ever carrying, hashing, or interpreting the attached data. This closes the
door on future public-metadata-slot pressure and is **consistent with the rejected per-op
witness-metadata seam (adoption fork F8, see STABILITY.md "Attributed-stream lift")**: the same
"metadata / provenance rides *beside* the public record, never *inside* it" posture, applied to
content addresses instead of op records. Recorded so a future request to add a public metadata field
is answered by this convention rather than by growing the surface. Design decision (2026-07-09).

## 2026-07-09 — D9: the rule-of-three bends for a clear downstream unblock

The rule-of-three evidence gate (a new Core strand / surface ships only behind three real consumers)
is a guard against *speculative* surface — **not a hard count.** When a Core feature *clearly unblocks
a desirable feature in a real downstream consumer*, a **single concrete, unblocking consumer is
sufficient evidence to ship**: the feature is not speculative, its consumer already exists, and waiting
for two more hypothetical adopters is gatekeeping that delays clearly-valuable work. Design decision
(2026-07-09). Concretely: the `Fuaran.Core.Lease` strand (Phase 84) shipped on the strength of a single
concrete consumer that hand-rolls the claim/coordination the strand replaces — the further candidate
consumers are corroboration, not prerequisites. The gate still means *evidence of real use* — one real
unblock is real use — so record the driving consumer in the shipping phase's outcome.

## 2026-06-18 — D8: Decode is fully portable — one parser, both pipelines (Phase 241)

The original extraction shipped decode as two `#if !FABLE_COMPILER` System.Text.Json paths
(`Wire.Decode`, `OpStream.fromJsonl`), so a Fable-only host could not `verifyChain`/replay/
round-trip in-browser. Phase 241 retires the asymmetry: `Wire.Json.parse` is a hand-rolled
recursive-descent parser → the `JVal` model (FSharp.Core only), `Wire.Decode`'s combinators
run over `JVal` with no `#if`, and `OpStream.fromJsonl` carries its own self-contained line
scanner (so `OpStream` keeps its no-Wire-dependency posture, D2). The parser is *always*
compiled, so the same code .NET runs is what Fable emits — the existing Wire/OpStream
conformance tests now exercise it as regression coverage. `render`/`parse` are inverses over
canonical wire JSON; the wire model has no `null` (rejected by name on decode).

## 2026-06-17 — D7: Reference witness in tests, no domain dependency

Conformance is proven against an in-repo **reference witness** (`tests/.../Reference.fs`):
a tiny string-id domain (`RNode`) that instantiates every witness. This lets the generic
functions be exercised end-to-end **without depending on any domain repo** — build Core
end-to-end first, rather than substituting it into a domain to prove it. Domain adoption
lives outside this repo.

## 2026-06-17 — D5: `IdWitness` carries no `fresh`

The roadmap phase sketched `toString`/`ofString`/`fresh`/`equals`. We dropped `fresh`:
the generic functions never mint ids — hygiene (Fork 2) derives addresses deterministically
from existing ids via `ToString`/`OfString`, and id minting is a domain concern. Keeping
`fresh` out keeps every generic function pure and deterministic (no hidden id source),
which matters for replay/caching soundness. Domains mint ids their own way.

## 2026-06-17 — D4: Identity is a witness parameter, not a fixed type

The one genuine cross-domain divergence (surfaced during the extraction assessment):
string ids (Doc/Calc) vs Guid ids (UI/Music), 2-2. Resolved as `IdWitness<'Id>` so each
domain keeps its representation over one `Core.Tree`. This is the concrete payoff of the
rule of three — with two data points we'd have guessed; with four/five the axis is visible.

## 2026-06-17 — D3: Extraction order — lowest-risk layers first

Built in the lowest-risk order: `OpStream` (highest genericity, cleanest two-seam witness)
→ `Ops` error-envelope (highest value — the AI-feedback protocol) → `Tree` (where the
identity decision lives) → `Wire` + `Validator` frameworks → `Function` (composes on Tree).

## 2026-06-17 — D2: `OpStream` and `Wire` are standalone; the rest depend on `Tree`

`Core.OpStream` is generic over `'Op`/`'State`/`'Rej` and needs no tree — it is the
cleanest seam, so it depends on nothing. `Core.Wire`'s combinators + corpus tooling are
likewise tree-free. `Core.Ops`/`Core.Validator`/`Core.Function` operate over the tree
witness and reference `Core.Tree`. This keeps the highest-genericity layers maximally
reusable (an op-stream-only or wire-only consumer pays for nothing else).

## 2026-06-17 — D1: `Severity` is `[<RequireQualifiedAccess>]`

`Severity.Error` would otherwise shadow `Result.Error` in any consumer that `open`s
`Fuaran.Core` — a real footgun (it bit the tests immediately). `RequireQualifiedAccess`
forces `Severity.Error` and keeps the bare `Error`/`Ok` as `Result`. Domains adopting the
validator framework inherit the safe spelling.

## Forward-looking notes

- ~~**Decode is .NET-guarded.**~~ _Resolved 2026-06-18 (D8, Phase 241): decode is now fully
  portable — `Wire.Json.parse` + `Wire.Decode` + `OpStream.fromJsonl` are FSharp.Core-only and
  run under both pipelines. No host-side decode boundary remains._
- ~~**DAG op-stream** (Wave 27) is a future `Core.OpStream.Dag.*` follow-on over the linear
  spine extracted here.~~ _Resolved 2026-09-30 (D32, Phase 131): `Fuaran.Core.OpStream.Dag` ships in the
  published roster, and the fold over its lanes is a machine-checked theorem with the extracted model run
  as a differential oracle. Nothing about the DAG is forward-looking any longer._
- **Domain adoption** (re-expressing UI/Calc/Doc/CAD/Office over `Fuaran.Core.*`) is
  intentionally not in this repo; it lands per-domain.
