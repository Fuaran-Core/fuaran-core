# Fuaran.Core — API stability

**Status:** the `1.x` line — semantic versioning from `1.0.0` (Phase 386); the `0.x` line ended at
`0.36.0`. The released version is single-sourced from `<Version>` in `Directory.Build.props` — this
document deliberately does not restate the number (restated versions drift; the props file cannot).
Within a major the surface grows additively and the witness contracts are frozen; a breaking move
waits for the next major (the `OneDotZero` family, below).

## Versioning policy

Per-release semver: `0.0.1-alpha` → `0.0.1-alpha.2` → … → `1.0.0`. Published to nuget.org
(`https://api.nuget.org/v3/index.json`, the one source the publish workflow pushes to) by the
tag workflow [`RELEASING.md`](RELEASING.md) describes (its gates, the Trusted Publishing policy, the
post-push registry probe). The publish workflow uses `--skip-duplicate`;
bump `<Version>` in `Directory.Build.props` before tagging.

**Semantic versioning from `1.0.0`; obsolete forwards leave at a major, and `1.0.0` is one (Phase
386, DECISIONS.md D133).** Through the `0.x` line a breaking class (below) advanced the MINOR and an
additive one rode the standing draft slot. From `1.0.0` the version is semver: a breaking class —
`removal`, `retype` or any `*-widening` — advances the MAJOR, an additive one the minor, and a change
that moves no public surface the patch. A member a minor retires stays as a `System.Obsolete` forward
naming its replacement, and every forward leaves at the next major: a major freezes with none
standing. The `OneDotZero` test family makes that a gate output. It reads the major of `<Version>`
and, from major 1, holds four laws — no public member of a shipped assembly carries
`System.ObsoleteAttribute`; `Conformance.unfrozenWitnesses` is empty; `Conformance.frozenWitnessFields`
is byte-equal to the list the major's `vN.0.0` tag carries (through the committed render
`tests/Fuaran.Core.Tests/frozen-witness-fields.txt`); and no `api/` baseline has moved by a breaking
class since the newest tag unless the major advanced past that tag's. At major 0 each law reported
itself vacuous by name.

**Every version cut cites a green run of the Core Fable gate against the candidate (Phase 217,
DECISIONS.md D55).** This repository runs no Fable compiler, so before the release gesture the
candidate packages are packed to a folder and `fuaran-dotnet`'s
`pwsh ./tests/core-fable/core-fable.ps1 -CoreVersion <candidate> -CoreFeed <folder>` is run against
them, and the release record names that run. It is the compile leg and the value leg at the
candidate — the value leg REQUIRED — so a Fable divergence is caught at the cut, before any consumer
can pin it, rather than when `fuaran-dotnet` next raises its pin. A cut whose run is red is not
released.

**What each version changed lives in the release ledger, one file per slot, and the gate refuses a
tag that has none (Phase 397, DECISIONS.md D130).** This document is the CONTRACT every version is
held to; [`docs/releases/<version>.md`](docs/releases/README.md) is the LEDGER — a file per version
slot, the released ones, the never-released ones and the standing draft alike — and
`docs/releases/README.md` is its index, carrying each file's heading word for word. The `Package
roster` family reads `git tag` and holds the ledger to the releases the repository actually made:
every release tag has a file headed `released <date> as v<v>`, every file has its index entry, and
every index entry has its file. Slots tagged before `0.25.0` predate the per-slot entries; their
files carry the tag, the note kept beside `<Version>` when they were cut and any section of this
document that recorded them, and say so rather than writing an entry after the fact. This document
is held to at most 1,500 lines, so a release's entry cannot drift back into it.

**A version's entries go in its ledger file.** Opening a draft slot creates `docs/releases/<v>.md`
headed `## <v> — DRAFT` and its index entry in the same commit; every change that rides the slot
appends a self-contained `###` entry there. A change to the contract itself — a new surface class, a
new stability-critical surface, a moved posture — is edited here, and its slot's entry says so.

**"Released" is a gate output, not a heading's claim (Phase 392, DECISIONS.md D123).** The `Release
record` family reads every tag in the checked-out history and holds four things, each red by name: a
tag's ledger file is headed `## <v> — released <yyyy-mm-dd> as `v<v>`` (a title may sit between
the version and `released`), so a tagged version headed DRAFT fails; an entry headed `released` has its
tag, so a heading turned before the tag fails; the untagged standing `<Version>` is headed
`## <v> — DRAFT`; and every released slot from `0.31.0`, the first cut under D55, carries a
`**Release record` paragraph and a `**Receiving gate run:**` paragraph in one grammar —

    **Receiving gate run:** `fuaran-dotnet` at `<commit>`, `core-fable.ps1 -CoreVersion <v> -CoreFeed
    <folder>`; compile leg <n> packages green; value leg <m>/<m> vectors byte-identical.

— naming the host commit the gate ran at, the invocation and both legs' counts. `0.31.0` and `0.35.1`
were released without a cited run; their records say `none cited before the tag`, and the family admits
that wording only for the versions D123 lists. The publish workflow runs `verify.ps1` before it packs,
so a release gesture without the heading and the record is refused there; `Directory.Build.props`
declares the ledger's index as the version's stability record (`<FuaranStabilityRecord>`) so a
downstream version check reads the same DRAFT marker.

**The release sequence the gate makes mandatory.** In this order, because each step reads the one
before it:

1. If `<Version>` moved since the conformance corpus was last emitted, re-stamp it
   (`--emit-laws` and `--emit-apply`, `docs/conformance-corpus.md`) and commit.
2. Pack every packable project from that clean commit into a folder, and run the receiving gate
   against it: `fuaran-dotnet`'s `pwsh ./tests/core-fable/core-fable.ps1 -CoreVersion <v> -CoreFeed
   <folder>`, green on both legs. A package this repository adds since the last release needs the
   host's candidate-only reference first, or the gate's membership check fails it by name.
3. Pack again from the clean tree for publication, so the packed surface is the one the gate measured.
4. In ONE commit: write the `**Release record` and `**Receiving gate run:**` paragraphs in the slot's
   ledger file, turn its heading to `released <date> as `v<v>`` there and in the index, and regenerate the README's version stamps
   (`dotnet run --project tests/Fuaran.Core.Tests -- --filter ReadmeClaims` with `CORE_APPROVE_README=1`
   set in the environment). The
   stamps are derived from the tags, so the regeneration runs under a provisional LOCAL tag on the
   candidate commit (its `api/` is the release's); delete that tag after the commit.
5. Tag THAT commit `v<v>` and push the commit and the tag together
   (`git push --atomic origin main v<v>`), so no CI run sees the turned heading without its tag. A
   push of the commit alone runs `main`'s CI red on the second clause until the tag exists, and a tag
   on any earlier commit is red in the publish workflow on the first: both are the gate working.

**Adding a public operation now carries a coverage line (Phase 335).** The `Proofs.Coverage` family
reads the committed API baselines (`api/*.txt`) as its census of public operations, so the commit that
publishes a new method or function, and regenerates its baseline, reds the suite until the operation
is mapped in the same commit: named by a `proofs.json` row's `evidence.operations`, listed on a law
family's operation roster (`Families.operations`), or entered in `proofs/coverage-exclusions.json`'s
`operations` block with a class (`trivial`, `forward`, `obsolete`, `host-seam`, `measured-elsewhere`)
and a one-line reason. Removing or renaming an operation reds the stale direction until its line goes.
The line is a record of what stands behind the operation, not a gate on its design; see
`proofs/README.md`, "Clause 4".

**A change to rendered bytes names the content-addressed consumers it moves (Phase 360).** A
content-addressed store keys its ids on bytes this package renders — an op encoder over `Json.render`,
the actor inside a chain payload or a DAG node id, the tags inside a capture — so a change to those
bytes is a change to every id such a store holds, whatever class the managed surface reports. The
entry that records such a change therefore lists, by name, every path that hashes the moved bytes
(the linear chain, `Dag.nodeId`, the capture chain, `ContentPack.signatureFingerprint`, ...) and, for
each, the route a store takes across it; and the change ships as a NEW `EncodingProfile` case with
`EncodingProfile.current` moved to it, never as an edit to what an existing case renders. "No known
store holds one" is not a bound this document accepts: Phase 287 wrote it, and a downstream store
whose payload strings carried a newline could not verify its ids on the next release.

## Public-surface baselines — the class of a move is a gate output, not an argument (Phase 183)

Every packable package carries a committed baseline of its public contract at
[`api/<package>.txt`](api/), rendered from the built assembly's IL metadata: one ordinal-sorted
line per externally-visible type, member, record field and union case. The `Public surface` test
family renders each package afresh on every gate run and diffs it against its baseline.

**Field names are part of the surface (Phase 237).** A record field has always rendered by name.
A union case renders each field as `name: Type` in declaration order. A consumer that constructs or
matches a case by field name (`TargetNotAContainer(target = …)`) stops compiling when that name
changes. So a union field rename is a `retype`, which is breaking, and the report names the case
and both names. Before Phase 237 a case rendered its field types only, and a rename moved nothing.

**The attributes that decide what consumer source is legal are part of the surface (Phase 406).**
A type renders one `attribute <type> <Name>` line per such attribute it carries:
`RequireQualifiedAccess`, `AutoOpen`, `NoEquality`, `NoComparison`, `ReferenceEquality`,
`AllowNullLiteral`, `Sealed`, `AbstractClass`, `Measure`, and `Struct` (a value type). Adding or
removing one on a type both sides publish is a `retype`, which is breaking, and the report prints
what the move costs a consumer. The member- and parameter-level attributes that change call syntax
(`ParamArray`, optional arguments, `[<Extension>]`, `CompilerMessage`,
`RequiresExplicitTypeArguments`, `CompilationRepresentation` other than `ModuleSuffix`) are not
drawn: the surface refuses them, so the first one shipped is red until the renderer draws it.
Before Phase 406 qualifying a union moved nothing.

**The gate refuses an UNCLASSIFIED move, never a breaking one.** Additive or breaking, a move whose
baseline moved with it passes; what fails is a surface that moved while its baseline stood still.
The record-widening dispensation stands — widening is permitted, widening *in silence* is
not — and the class below is what a reviewer applies it to.

Regenerate with:

```
CORE_APPROVE_API=1 dotnet run --project tests/Fuaran.Core.Tests
CORE_APPROVE_API=Fuaran.Core.Wire dotnet run --project tests/Fuaran.Core.Tests
```

Every approval switch in the repository (`CORE_APPROVE_API`, `_WIRE`, `_LADDER`, `_README`, `_DOCS`
and `FUARAN_REGEN`) has one reading (`tests/Fuaran.Core.Tests/Approval.fs`, Phase 396): absent, empty,
`0` or `false` is no; `1` or `true` is every file the switch governs; any other value is a filter, a
comma-separated list of file stems (a package id here). A filter that matches nothing is red by name,
and each file a switch rewrites is printed.

**The hazard of the bare form is that it rewrites EVERY drifted
baseline, not the one you were looking at.** An unrelated drift sitting in the tree lands in your
commit silently. Name the package, or stage the baselines you meant to move BY NAME from the printed
list and read the rest back out.

### The classes, and what each costs a pinned consumer

The six are what the diff reports per move; the headline for a whole package is the most
informative of them present (a required record field widens the primary constructor too, so it
shows as `record-widening` beside a `retype`, and the headline names the cause rather than its side
effect).

| class | what moved | what it costs a consumer | ride or advance |
|---|---|---|---|
| `removal` | a baseline token with no counterpart — removed or renamed | call sites stop resolving | **advance `<Version>`** |
| `retype` | the same member rendered differently: a parameter or return type changed, or a record field's POSITION moved | call sites stop type-checking; positional construction binds the wrong slot | **advance** |
| `record-widening` | a field added to a record the baseline published | every full-literal construction fails, `FS0764` | **advance** |
| `union-widening` | a case added to a union the baseline published | every exhaustive `match` becomes incomplete — and against a stale same-version pack, an `InvalidCastException` at run time with no compile signal at all | **advance** |
| `interface-widening` | a member added to an interface the baseline published | every implementer stops compiling | **advance** |
| `additive` | a new type, a new module function, a new member on a class — and a field or case on a type the baseline never published | nothing: a pinned consumer compiles either way | may **ride** the standing draft slot |

The three middle rows are the reason this is not a plain token diff. A union case addition REMOVES
NOTHING — it emits a new factory, a new `IsCase` property and a new `Tags` literal — so a
removal-only differ reports it as ordinary growth. They are read off the
`CompilationMappingAttribute` the F# compiler already emits, which is what makes them computable
rather than arguable.

### What the gate prints, and what it does not claim

The family prints the class of every baseline that has moved **since the newest `vX.Y.Z` tag** —
the "ride or advance" line that entries in this document wrote by hand until now. A package whose
baseline the newest tag does not carry is reported as a first snapshot rather than as unchanged.

**A tag cut before Phase 237 is read without field names, and the report says so.** Its baselines
carry nameless union cases. Compared with today's named ones, they would report every carrying
case as a `retype` that no consumer ever saw. So when the tagged baseline predates the names, the
report strips them from the current surface before it classifies. It prints a line for that
package: a field rename since that tag is not visible to the comparison, just as it was invisible
to the gate then. **This normalisation expires by itself.** The first tag cut after Phase 237
carries named baselines, and nothing is stripped against it. **The live gate never normalises.** A
committed baseline without names reads as moved against today's render. So a stale baseline cannot
hide a rename.

Three boundaries, named rather than assumed:

- **Not semantics.** A function whose signature is unchanged and whose behaviour reversed is
  invisible here and always will be.
- **Not the Fable source half.** Every packable project also ships its `.fs` sources under `fable/`
  for a Fable consumer to compile; what is rendered is the managed assembly, which is the .NET
  consumer's contract.
- **Not internals.** An internal member cannot break a consumer in another assembly and is not in
  the surface to begin with.

## The wire surface — a canonical-encode change states its class in the same commit (Phase 214)

The managed baseline's first boundary is "not semantics", and the canonical BYTES a package emits sit
on the far side of it. Phase 213 moved them: a `project` step's `cols` became `columns`. That is
breaking for every consumer that pins canonical bytes, and the managed gate correctly reported
nothing. So the wire surface has a baseline of its own:
[`api/wire/<package>.txt`](api/wire/).

**What each file holds.** One file per wire-bearing package. It holds the canonical bytes of a
document set that reaches every member name and every discriminator the package emits. Each line
carries the sha256 of the emitted bytes and the document rendered canonically. The set is DERIVED by
reflection from each package's root document types, so a case added to a union is in the set without
anyone listing it (DECISIONS D53).

**What the gate does.** The `Wire surface` test family re-encodes the set on every gate run. A changed
byte fails it, naming the document and the member, until the baseline is regenerated:

```
CORE_APPROVE_WIRE=1 dotnet run --project tests/Fuaran.Core.Tests
```

**THE RULE: a wire-baseline change states its class in the same commit.** The regeneration writes the
class into the baseline's own header (`# class since <tag>: <class>`). A second test recomputes that
class against the tag the header names and fails if the statement is missing, stale or wrong. The
commit that moves a baseline therefore carries its class, and the class is checked rather than
asserted. Put the same word in the commit message and, for a published package, in the draft slot's
ledger entry.

The classes:

| class | what moved | ride or advance |
|---|---|---|
| `additive` | a new member, or a new case (a document added) | may **ride** the standing draft slot |
| `breaking` | a member renamed or removed; a discriminator changed; a document removed; the same structure emitted as different bytes | **advance `<Version>`** |

An F# case renamed with its wire bytes untouched is not a wire move; that one is the managed gate's.
A member reordering cannot occur in `Canon.render` output, which sorts keys. The two emitters that are
not `Canon.render` (the IDL artifact's indented form, and the hand-assembled stream envelopes) are
pinned by the hash, and a layout move there is `breaking`.

**What it does not pin, named rather than assumed:**
- **Decode.** A consumer pins what a package EMITS. A decode alias is outside the baseline, and
  adding one moves nothing; a test holds that for the alias Phase 213 kept.
- **A domain's vocabulary.** A domain's values under its own IDL vocabulary, and the op payload
  embedded in a stream envelope, are the domain's to pin.
- **Semantics.** The same bytes meaning something new is invisible here, as it is to the managed
  gate.

**The hazard is the managed switch's.** The switch rewrites EVERY drifted wire baseline. Stage the
ones you meant to move BY NAME.

## The load-bearing invariant

`Fuaran.Core.*` is **a library of generic functions over domain-witness records, never a
base node type.** This is not a style choice — closed exhaustive `NodeKind` DUs with
total matching are load-bearing in every domain (the compiler-checked totality guarantee
no open base type can give). A shared base node type
in the core would destroy that property. Any change that introduces a concrete node/kind
type into a core package is a breaking architectural regression, not a feature.

## Vocabulary — the failure families, the exception postures, the argument order

These are rules the code already follows; written here so the next type lands in the right place
without a reader opening the code. A new public type or function that cannot be placed by them is a
reason to amend this section in the same commit, not to invent a further family.

**The failure families, by suffix.** Every typed failure in `Fuaran.Core.*` is one of seven, and the
suffix says which question it answers:

- **`…Rejection`** — the algebra or the domain refuses a well-formed request against the state it
  meets: the op algebra's `Rejection`, `AppendRejection`, `VerifiedAppendRejection`,
  `ArbitrationRejection`, `DagAppendRejection`, `DagAppendIfRejection`, `LaneRejection`. Retrying
  the same request against the same state is refused again; a different state may admit it.
- **`…Break`** — integrity: stored or received evidence does not verify (`ChainBreak`, `DagBreak`,
  `SnapshotBreak`, `CheckpointBreak`, `LaneBreak`, each detailed by a `…BreakReason` where it has
  several). A break means the material was altered or corrupted, never that a request was wrong.
- **`…Fault`** — an operation ran over the material it was given and could not complete, and the fault
  says where it stopped: a replay, a capture replay, a reconcile, a rehash, a checkpoint, a lane or
  JSONL load, a DAG append or attestation, a total order, a declaration set, and a host's own fetch
  answered through a seam (`ResolveFault`, the query resolver's account of a fetch that could not
  complete). A fault is typically translated into the calling seam's `…Error` at the boundary.
- **`…Error`** — the call itself is refused: its input does not parse or decode (`JsonError`,
  `DecodeError`, `ReadError`), or it names what the seam does not hold or binds it wrongly — the seam
  unions a caller receives (`InvokeError`, `QueryError`, `PipelineError`, `ColumnError`,
  `SchemaError`, `RegistrationError`, …).
- **`…Failure`** — the outcome of a composite flow, whose cases wrap the other families
  (`GatedApplyFailure` is a denial or a rejection; `ProposeFailure`, `ApprovalFailure`).
- **`…Denial`** — a policy said no (`WriteDenial`, `PolicyDenial`); the request was valid and would
  otherwise have run.
- **`Defect`** (and `…Defect`) — a finding reported in a list beside an answer, never a refusal: the
  validator's `Defect`, `VerifyDefect`, `ObserverDefect`, `ReferenceDefect`.

`ResolveFault` was reviewed against this rule on the `1.0.0` slot (Phase 389, DECISIONS.md D136) and
keeps its name: it is the host's report that its fetch could not complete, which is a fault, and the
caller-facing refusal it becomes is `QueryError`. A `ResolveError` would have put two `…Error` unions
on one dispatch with no rule to tell them apart. `Idl.Sample.SampleRefusal` was the one type outside
the seven suffixes: by this rule it is a fault (the sampler could not complete on the vocabulary it
was given, and names the slot), and the same decision ruled its rename; it is `SampleFault` since the
`1.0.0` slot (Phase 391). A replayed dispatch that does not answer a value is a `ReplayFailure` —
the seam's own refusal (`Refused`) or the journal's inability to answer (`Unanswered`, carrying the
`KeyedCaptureFault`) — the `…Failure` of a composite flow, as the rule above says.

**The exception postures.** A typed failure is the rule; an exception crosses a Core boundary in
exactly three shapes, each in a fixed place:

1. **Host code Core calls on the host's behalf is not wrapped.** A capability body, a query resolver,
   an evaluator, a witness function: Core neither catches nor converts what it throws, because
   catching every exception also catches the ones a host means to escape (cancellation, its own fatal
   faults). The obligation is totality, stated at each seam (`Capability.invoke`: "the body must be
   TOTAL"), and a body reports failure by answering `Failed`.
2. **Host code Core runs in order to REPORT on it converts a throw into a finding.** The validator
   runs each rule family guarded: a family that throws contributes one `RULE-FAULT` defect carrying the
   exception's message, and the families after it still run. The conformance kit does the same where a
   throw is what a law watches for: a dispatch that throws is a red law naming the throw.
3. **Core's own misuse checks raise.** A programming error that no input data can cause raises at
   the call that commits it — `Json.Reader` read twice or left unread (`invalidOp`), the kit's
   `ConfRng.choose` over an empty alphabet (`invalidArg`). Where data can reach the same check, the
   surface is the `try…` form, which answers a typed failure instead.

**The capability-argument order.** A function that takes domain capabilities and witnesses takes
them in one order, outermost first, so that partial application peels them in the same order
everywhere: (1) the companion witness that selects the variant (`KeyedWitness`, `RefWitness`); (2)
the domain capabilities, grammar before containment — `allowedChildren` (which kinds a parent kind
admits) before `canHold` (whether a node holds children at all); (3) the `NodeWitness`, then the
`IdWitness`; (4) the request (the op, the proposals); (5) the subject last (the root, the base tree),
so the call pipes. `applyReferenced refw allowedChildren canHold w idw op root`,
`arbitrateGrammar allowedChildren canHold nodew idw baseTree proposals` and
`applyContainedKeyed keyw canHold nodew idw op root` are the same rule with steps left out. The
attributed verbs follow it too: the AI surface's `Proposals` verbs take the witness, then the actor,
then the instant, then their own arguments — `approve w approver at id q state`,
`reject approver at id reason q`, `proposeWithId author at id intent ops q` (Phase 391).

**The shapes a frozen surface does not carry (Phase 391, DECISIONS.md D138).** A public function
does not answer a positional tuple of three or more parts, a field or case does not carry a
`string` over a closed set its package declares, and an `option` does not stand for a two-case
mode: each is a record, the declared union, or a two-case union, named once (`ScriptRejection`,
`CapturedDispatch`, `ReplayedDispatch`, `CapturedEffect`, `MemoStep`, `WitnessFields`,
`WriteScope`, `HostedFormat`, `ColumnType`, `AggFn`, `Actor`). A frozen witness record names its
seam and carries no per-call input: the body a family hands the seam's host path is the family's
argument (`capabilityLawsAt w body`), not a field.

The first clause is a test output for every package (Phase 410, DECISIONS.md D140): `OneDotZeroTests`
reads each packable package's built assembly and fails on a public method, or a public property of
a function type, whose answer carries a tuple of three or more positions — through generic
arguments, arrays and a returned function's range — so a package added later is held without being
named. `OpStream.Dag`'s writes answer `Dag.CheckedAppend` and `Dag.IndexedAppend`, and its default
order key is a `Dag.LaneKey`.

## Stability-critical surfaces

These thread through multiple packages; changing their shape is a breaking change
requiring a major-version bump and coordinated domain-adopter updates (adopting a domain?
see [`docs/ADOPTION.md`](docs/ADOPTION.md)):

- **`IdWitness<'Id>`** (`{ ToString; OfString; Equals }`) — resolves the string-vs-Guid
  identity axis. Threads through `Core.Tree`, `Core.Ops`, `Core.Wire`, `Core.Function`.
  Deliberately carries **no `fresh`**: the generic functions mint no ids (hygiene derives
  ids deterministically); id minting stays domain-side.
- **`NodeWitness<'Node, 'Id>`** (`{ Id; KindTag; Children; ReplaceChildren }`) — the four
  accessors the whole tree/ops/validator surface operates through.
- **`StreamWitness<'Op, 'State, 'Rej>`** (`{ Apply; Encode; Decode }`) — the two-seam
  op-stream witness. `Decode : string -> Result<'Op, string>` as of `0.0.1-alpha.3` —
  the recoverable-envelope discipline; `fromJsonl`/`Snapshots.ofJsonl` return
  `Result` (migration notes shipped with that release).
- **`Actor`** (`Human of id | Agent of model * version * id`) + the `OpRecord` / `DagNode` /
  `StreamConfig` / `append` / `merge` actor field+parameter (typed since Phase 320, `0.0.1-alpha.13`).
  The typed actor is **folded into the chain hash** — a breaking hash-format bump.
  See "Typed, attested provenance" below (migration notes shipped with that release).
- **`Hash.fnv1a` / `Hash.sha256Hex` / `Hash.sha256Bytes`** (`Core.Tree`) — the two hashing
  regimes. Their **values** are the contract, not merely their signatures: `fnv1a` is folded
  into every stored content hash and `sha256Hex` is pinned to the FIPS 180-4 vectors, so a
  change to what either returns invalidates persisted data rather than breaking a compile.
  Which regime a call site is in is part of its meaning — a cache fingerprint quietly
  substituted for the crypto digest would still typecheck.
- **`ArtifactWitness<'Node, 'Id>`** (`{ Tree; IdW; Holes; Effect; Bind }`) — the
  artifact-function witness.
- **`SkeletonOp` carries no ordinal** (`0.2.0`, 2026-07-26). `InsertChild` and `MoveNode` are
  `(parent, node)` and `(target, newParent)`; both **append**, and `ReorderChildren` states order by
  naming ids. Placing a node anywhere but last is `Batch [InsertChild …; ReorderChildren …]`.
  **Breaking twice over**: the two constructors lost an argument, and `Rejection.IndexOutOfRange` was
  removed because nothing can construct it any more — an unreachable rejection case is dead
  vocabulary, and the envelope discipline above is about naming real failures.
  **The rule this establishes: where a collection's members have identity, they are addressed by it.**
  An ordinal names a projection over a list rather than anything the tree stores — children are a list,
  so order is structural and no index exists in the state. It is therefore derivable, snapshot-bound,
  and silently wrong after any preceding or concurrent edit, where a wrong id fails loudly.
  Reintroducing a positional argument to these ops is a regression, not a convenience. Contained data
  with no identity of its own (a column list, a chart's series) is a different case and may still be
  addressed positionally.
  *Consequence worth knowing:* a remove and an insert on the same parent now **commute**, where the
  index-bearing forms did not. `Ops.footprint` still reports them dependent — it is a deliberate
  over-approximation keyed on the parent id and does not consult the tree — and that conservatism is
  pinned by a test so it reads as a choice rather than a defect.
- **The recoverable envelope discipline** — `Rejection<'Id>` (Ops) and `ApplyError`
  (Function) must always *name the failure and enumerate the valid alternatives*. New
  cases are additive; removing the enumeration from a case is breaking.
- **`EffectClass`** + the `Effect.join` law — the two-axis effect signature is mandatory
  and total; `compose` must always join componentwise. Making the effect optional is
  breaking (it is exactly how impurity leaks in).
- **`Wire.Versioning`** — the wire version/profile + forward-compatibility contract
  (`Profile` `<name>@<major>.<minor>`, `negotiate` → `Current` / `Behind` / `Foreign`, the
  `$profile` / `$payload` `Envelope`, the transport-only `Unknown { Kind; Payload; RequiredProfile }`
  carrier + `decodeTolerant` / `reencode` must-ignore-but-preserve, `classify` / `bump`). This is a
  **cross-host wire contract**: the envelope shape, the negotiation table, the name+major equality
  rule, and the byte-for-byte preservation of an unknown kind are re-implemented byte-identically by
  each language host (F#/TS/Python) and conformance-certified (the `VersioningTests` in-repo, plus the
  `envelope-*` families in the shared UI `wire-format-fixtures/` corpus). The base profile is `core@1.0`;
  the bare (un-enveloped) form is read as the implicit `core@1.0`, so envelopes are opt-in carriage — no
  existing wire changes. Changing the envelope keys, an `Unknown` carrier field, a `negotiate` outcome,
  or dropping preservation is a **major** bump; an additive kind/case is a **minor** bump an older
  consumer tolerates via `decodeTolerant`.

### The Column and Query strand (first-class)

> **Since `0.33.0` (Phase 258, DECISIONS.md D66) `Fuaran.Core.DataFrame` and `Fuaran.Core.Column.Ops`
> are produced by [`Fuaran-Core/fuaran-core-compute`](https://github.com/Fuaran-Core/fuaran-core-compute),
> and every change to them from `0.33.0` on is recorded in that repository's `STABILITY.md`.** The
> promises this repository made about those two packages while it produced them (up to `0.32.0`)
> are in the release ledger, [`docs/releases/0.32.0.md`](docs/releases/0.32.0.md#the-compute-strand-contracts-this-document-carried-until-phase-397),
> which that document points at rather than restates. The promises below are this repository's.

The columnar strand this repository produces (`Fuaran.Core.Column`, `Fuaran.Core.Query`) is
a **first-class, stability-critical member of the substrate**, not an example or a reference sketch —
its public surface carries the same 1.0 commitment as the witness spine, and its cross-host semantics
are **conformance-certified**, not asserted. Stable surfaces:

- **The columnar model + wire codec** — the fixed Arrow scalar set (`int` / `float` / `bool` / `string`
  / `date` / `timestamp`, and from `0.33.0` the exact `decimal`), the validity-mask null model, and `ColumnCodec`'s six-code decode envelope
  are the cross-host contract: two hosts encode a column to byte-identical wire (same null / coercion /
  ordering / **canonical-float** semantics — floats route through `Wire.Canon.canonicalFloat`, certified
  by `Conformance.canonicalFloatLaws`). Changing the scalar set, the null model, the codec envelope, or
  a coercion/widening rule is a **major** bump.
- **The typed column storage (Phase 417, `1.0.0`)** — a `Column` is `{ Name; Data }`, its `Data` one
  case of `ColumnData` per column type, each an opaque immutable `Vector<'T>` of values beside a
  `Validity` mask, and its `Type` the case the storage carries, so a column cannot disagree with its own
  storage and a present cell is always of its column's type (`Column.ofCells` refuses one that is
  not, as the `TypeMismatch` `Table.validate` named for it before). Three promises ride the storage:
  **the wire is untouched** — every column the codec carried as a `Cell list` encodes to the same bytes
  and decodes to the same table, held by the corpus families; **equality is cell equality** — two
  columns of one type are equal exactly when their masks agree and `Cell.compare` answers `Some 0` at
  every present row, every NaN one value and `-0.0` equal to `0.0`, on every host, held by
  `Conformance.columnVectorLaws`; and **a vector never changes after construction** — no public member
  of `Vector` writes, `Vector.ofArray` copies, `Vector.adopt` takes ownership of an array the caller
  promises not to touch again, `Vector.slice` is a view, and `Vector.Unsafe.borrow` is the one route to
  the backing array, under the rule that the borrower does not write (Phase 418 states the contract in
  full and adds its law family). What is NOT promised: the storage of `Dates` and `Timestamps` (canonical
  text in this slot; Phase 422 makes them integers with a unit) and the shape of `Validity` (a
  `Vector<bool>` in this slot; Phase 420 elides it for a null-free column) — both ride the untagged
  `1.0.0` draft and are decided before it is cut, which is why they are named here rather than frozen.
- **`Query`** — the data-acquisition seam + its invocation key (also `canonicalFloat`-routed) are
  certified by `Conformance.queryLaws`.

A domain that ships a `Transform` evaluator / `Query` provider runs `transformLaws` / `queryLaws`
alongside its base witness certification; a host in another language re-implements the codec + these
laws against this section, exactly as it does the witness surface.

**Three incremental-eval surfaces (Phase 34 columnar ∥ Phase 62 capability ∥ Phase 68 tree-level).** The
compute strand carries three dirty-aware re-evaluation paths sharing one discipline — the incremental
result is **byte-identical to a full eval over the same inputs** (certified, not asserted):
`DataFrame.evalFrom` (columnar, change-relevance reuse, `Conformance.incrementalLaws`, Phase 34);
`CapabilityPipeline.evalFrom` (the capability-DAG, re-invoke only the downstream-of-change nodes,
`Conformance.capabilityPipelineIncrementalLaws`, Phase 62); and `Fuaran.Core.Propagation.evalFrom` (a
typed domain tree with declared id-addressed bindings — the minimal dirty set from a value change or a
structural `SkeletonOp` (Phase 68) drives a dirty-subgraph recompute in dependency order, reusing clean
nodes, `Conformance.propagationEvalLaws`, Phase 69). `Propagation.eval` is the reference full evaluator
the tree-level `evalFrom` is certified against — Core walks the acyclic nodes in dependency order and
returns cyclic SCCs as data (`EvalOutcome.Cyclic`, the `#CALC!` posture; the iterative upgrade is the
`office`/Calc convergence work), the host supplies the injected `evalNode` (GP6 — no evaluator in Core).
**On the tree-level path the declared reads are ENFORCED, not assumed (Phase 209):** the resolver
`evalNode` is handed answers for `deps[id]` and for nothing else, and a read outside that set is the
typed `EvalUndeclaredRead` naming the node and the read — so the byte-identity above is a property of
the driver rather than a clause the host has to keep. A host re-implementing this path implements the
restriction too. The first two are keyed on the same content-addressing discipline (the Phase-49 `applyMemo` /
Phase-27 capture keys). `CapabilityPipeline.eval` is the reference evaluator the capability path is
certified against — Core supplies the DAG plumbing (topological walk + `FromNode` edge resolution), the
host supplies the node `body`. A dirty non-deterministic node re-invokes (or replays from its Phase-27
capture); a clean node reuses its prior value, so incrementality never serves a stale effect result (the
Phase-53 effect-honesty gate on the dirty path).

**`Fuaran.Core.Propagation` owns no evaluator (GP6).** The new package (added Phase 68) computes *what is
stale* — the dependency structure, the cycle enumeration, and the dirty `Set` — and returns it as data;
the actual re-evaluation stays domain-side. The dependency relation is a per-call `readsOf` function, not
a witness field (GP2); staleness is a returned `Set`, not a stored flag. Its public surface (`dependencyMap`
/ `sort` / `cycleThrough` / `dirtyFromChangedIds` / `touchedBy` / `dirtyFromOp`; the `staleSet` alias of
`dirtyFromChangedIds` left at `1.0.0`) is
FSharp.Core-only + Fable-clean and carries the same within-a-major additive-growth commitment as the
rest of the substrate.

### Public because a sibling Core package calls it (`0.19.0`)

`Fuaran.Core.*` is nineteen packable assemblies. Between them it grants `InternalsVisibleTo` in
exactly four declarations — `Fuaran.Core.Function` to `Fuaran.Core.Query`, `Fuaran.Core.Tree` to
`Fuaran.Core.ContentAddress`, and `Fuaran.Core.Idl` to `Fuaran.Core.Idl.Codegen` and to
`Fuaran.Core.Conformance` — each read only from a `NoInlining` boundary (DECISIONS.md D129); the two
further declarations, `Fuaran.Core.Idl.Cli` and `Fuaran.Core.Conformance` to the test project, grant
nothing a consumer reaches.
Everywhere else a function one Core package needs from another has to be public — `internal` cannot
express it. Three members are exactly that and nothing else: no caller outside these packages, no test, and until
`0.19.0` no document saying why they were public. A reading that classifies surface by caller count
therefore takes each for dead and reaches for a narrowing that would not compile. They are contracts:

- **`Fuaran.Core.FunctionRegistryModule.narrow`** — the content-pack currying
  `Fuaran.Core.Conformance` builds its samples with.
- **`Fuaran.Core.Memo.isMemoisable`** — the memo-soundness law asks it the same question the memo
  gate asks, which is the whole point of that law.
- **`Fuaran.Core.Idl.ProposalModule.touchedKinds`** — `Fuaran.Core.Idl.Codegen` reads it when
  reporting which kinds a proposal delta reaches.

Public here is a consequence of the package split, not an invitation. They carry the ordinary
breaking-change terms above; nothing in this entry promotes them to a documented extension point.

### Members the other language hosts mirror name for name (`0.19.0`)

Three members are re-implemented under the same name by the TypeScript and Go reference
implementations. Those hosts do not call the F#; the F# is the reference they must agree with. So a
rename, or a change to what one of these returns, is a cross-host divergence to be landed in every
host in the same change-set — whatever the caller count in this repository says.

- **`Fuaran.Core.CapabilityPipelineModule.nodeInvocationKey`** — a pipeline node's invocation key.
- **`Fuaran.Core.Idl.Sanitize.sanitizeUrlOrBlank`** — the URL sanitiser. Its RESULT is the contract:
  a divergence here is a security divergence, not a cosmetic one.
- **`Fuaran.Core.Conformance.capabilityLaws`** — the capability law family a host certifies against.

## Witness-record field freeze (the 1.0 contract)

The sixteen public witness records (`IdWitness`, `NodeWitness`, `StreamWitness`, `ArtifactWitness`,
`AiSurfaceWitness`, `ProjectionWitness`, since Phase 330 the six conformance-kit inputs
`CapabilitySeamWitness`, `QuerySeamWitness`, `CapabilityPipelineWitness`, `ConstructWitness`,
`KeyedWitness`, `EvaluatorWitness`, since Phase 313 `RefWitness`, since Phase 298 `ObserverWitness`, since
Phase 349 `SanitizeWitness` and since Phase 318 `GuardedSurfaceWitness`, each frozen at birth) are
**plain records**: adding a field is a compile-break for *every* adopter's construction site, with no
gradual-migration path. At `1.0` their field sets **freeze**. (The freeze originally named only the
four base records; `AiSurfaceWitness` and `ProjectionWitness` are equally public, equally
adopter-constructed, and carry the same blast radius — an unpinned public witness is exactly the gap
the freeze exists to close, so they are frozen on the same terms. The six conformance-kit inputs
joined at Phase 330 on the same argument: a domain builds each one by name, exactly as it builds a
core witness, so a field added to one breaks every adopter that runs its family — and `KeyedWitness`
is read by the apply path besides, since Phase 286.) This is a safe commitment, not a
gamble: the adoption cycle exercised the surface across eight structurally-distinct domains — a
prose/section tree, a heterogeneous spreadsheet model, a CAD feature tree, a belief-bearing world
graph, the UI tree, a derived legal-drafting domain, a slide deck, and a music score — spanning both
id shapes (string and Guid), every state shape (homogeneous tree / heterogeneous layers / graph),
every op posture (own op-stream / base-domain op-stream via projection / no op layer yet), and the
non-equality-state case. Each of the last five adopters needed **zero** additive witness surface. The
surface has saturated; the freeze records that.

**How a genuinely-new post-1.0 capability evolves — compose, never grow.** The evolution path is
already in the design: `ArtifactWitness` does **not** add fields to `NodeWitness`/`IdWitness`; it
*embeds* them (`{ Tree: NodeWitness<…>; IdW: IdWitness<…>; Holes; Effect; Bind }`) and carries the
extra accessors itself. Any future generic layer that needs more domain accessors follows that
precedent: it introduces a **new composing witness record** that embeds the frozen ones plus its own
fields. Existing adopters that don't use the new layer construct nothing new and never break; adopters
that opt in construct the new record once. This keeps Core's evolution *additive at the type level*
without a flag-day.

**`WitnessV2` + bridge is the last resort, not the default.** A versioned record
(`NodeWitnessV2` + a `NodeWitness -> NodeWitnessV2` bridge, both maintained for a deprecation window)
is reserved for the case where a *frozen* witness is found fundamentally insufficient — not merely
"a new layer wants more," which composition already covers. That is a major-version event and must be
justified against why composition could not express it.

**Enforcement.** New `Fuaran.Core.*` code that adds a field to any of the sixteen frozen records is a
review-blocking regression (the same class as introducing a concrete node type, per *The load-bearing
invariant* above). The mechanical backstop is the law family **`Conformance.witnessSurfaceLaws`**
(Phase 232), which this repository's suite runs on every gate: one law per frozen record, holding
its field set — read by reflection from the compiled record — equal to the pinned list in
`Conformance.frozenWitnessFields` **by name and in declaration order**, and one more law holding
that every public record named `…Witness` in the Fuaran.Core assemblies the kit references is
either frozen there or declared outside the freeze, with its reason, in
`Conformance.unfrozenWitnesses` (empty since Phase 330, which froze the six conformance-kit inputs
Phase 232 had declared there; the list and the law that reads it stay, so a witness added before
`1.0` is still classified by the commit that adds it). A field added,
removed, renamed or reordered on a frozen record turns the gate red **by the record's name**, and
regenerating the `api/` baseline does not turn it green. A domain can run the same family at its
own pin bump to certify that the Core it compiled against carries the frozen shape.

**The opt-out for a deliberate widening before `1.0`.** Until the `1.0` cut, a frozen record may
grow only as a deliberate, named act, never as a regenerated baseline: (1) edit the record's entry
in `Conformance.frozenWitnessFields` to the new field list — the law stays red until you do, and
that edit is the act a reviewer sees in the diff; (2) in the SAME commit, add an entry to the
open draft slot's ledger file naming the record, the field, the class the surface gate reports
(`record-widening`), and why composition — a new witness record that EMBEDS the frozen one — could
not express it; (3) regenerate the `api/` baselines. Extending the freeze to a record now declared
outside it is the same act in reverse: move it from `unfrozenWitnesses` to `frozenWitnessFields`
with an entry in the draft slot's ledger file. From `1.0` there is no widening route: a frozen witness does not grow, and the
`WitnessV2` path above is the only one.

## Digest changes

Content-hash digests are in-memory fingerprints (FNV-1a; not cryptographic), not a wire format —
but a domain that *persists* a digest as a content-address should note one-time changes here.

- **`Tree.encodeHash` (Phase 11, `0.0.1-alpha.7`):** the fold now separates adjacent per-node
  encodings with the `U+0001` byte `contentHash` already used, instead of `""`. This fixes a
  boundary collision (`["ab";"c"]` and `["a";"bc"]` previously hashed equal) and aligns the code
  with its docstring. **`encodeHash` digests therefore change once**; `contentHash` digests are
  unchanged (same separator byte, now via the shared `hashSep` constant).
- **`Validator.canonicalCodes` (Phase 25, `0.0.1-alpha.8`):** the cross-host parity projection now
  joins sorted codes with the `U+0001` byte instead of `,`, so a code containing the separator can
  no longer alias. **The projected string changes once**; defect codes are stable identifiers, so a
  host that persisted the old `,`-joined parity string should re-derive it.
- **FNV-1a everywhere in the spine (`0.6.0`) — a ONE-SIDED change, and the asymmetry is the whole
  of it.** The multiply now goes through a split-half 32-bit form so the function computes true
  32-bit FNV-1a on both pipelines. **On .NET nothing changes**: every digest, content-address,
  chain hash and staleness stamp minted by a .NET process is byte-for-byte what it was, which is
  the constraint the fix was built to satisfy, and the pinned vectors enforce it. **Under Fable
  every value changes**, because the pre-`0.6.0` transpiled multiply overflowed 2^53 and was not
  FNV-1a at all. A host that persisted a JavaScript-minted FNV digest as a content-address must
  re-derive it from its source data. This is stated as a one-time digest change rather than a bug
  fix precisely because a caller cannot tell the two apart from the outside.

  **The surface is wider than `Hash.fnv1a`, and that is the part worth reading.** The spine shipped
  *six* copies of FNV-1a, inlined at various times so a package need not take a `Tree` dependency.
  Fixing only the canonical one would have left **`OpStream.defaultHash` — the op-stream chain
  hash — still divergent**, which is the exact harm the fix existed to prevent: two hosts replaying
  one log computing two different chains. All six are fixed in `0.6.0`. Three are now gone
  entirely (`Capability.invocationKey`, the pipeline node key, and `Query.invocationKey` call
  `Hash.fnv1a` directly — their packages already depended on `Tree`). Three remain by necessity —
  `Hash.fnv1a`, `OpStream`'s copy (standalone by DECISIONS D2) and `Column`'s (references only
  `Wire`) — and the two copies are now **checked against the canonical one by test and by probe**
  rather than kept in step by discipline. Affected values: `Tree.contentHash`, `Tree.encodeHash`,
  `Tree.Index.fingerprintOf`, `Projection.digestOf`, `ContentPack.signatureFingerprint`,
  `Function.Memo` keys, `OpStream.defaultHash`, `Schema.fingerprint`, and both `invocationKey`s.

## Typed, attested provenance (Phase 320, `0.0.1-alpha.13`)

The actor is a typed `Actor` (`Human of id | Agent of model * version * id`) **folded into the chain
hash** — a one-shot breaking hash-format bump (the canonical payload now encodes the actor as a JSON
object, not a bare string). Attribution is therefore part of the integrity chain: re-attributing an
op breaks `verifyChain` / `verifyDag` exactly as op-tampering does. The `Human` / `Agent` distinction
is the load-bearing accountability fact; an `Agent`'s `model` / `version` is a neutral attribution axis
a consumer may use for its own analytics or provenance reporting. A pre-320 bare-string stream migrates via `fromJsonlLegacyActor` + `rehash` under
`legacyActorConfig` (the Phase-255 migration seam) — see the migration doc. **Cross-host break:** the
hash pre-image is a wire contract; the TS / Python / Fuaran.UI hosts must adopt the same typed-actor
object + field ordering in the same release or chains diverge (the Fuaran.UI op-record fold is
coordinated with the Phase-319 wire-versioning work).

An optional **`IAttestationSink`** signs chain checkpoints — the head at a commit / publish boundary
(O(commits), not O(ops); the hash-chain attests the whole prefix). The default `noAttestation` is a
no-op, so signing is opt-in and the un-attested path is unchanged; an enterprise host plugs in
KMS / HSM signing behind the seam (Core stays FSharp.Core-only + Fable-clean — the crypto lives
host-side). **Replay-as-provenance:** a state is provably the deterministic replay of its attested
op log / op-DAG, and a signed head + replay is independently re-verifiable (integrity → attestation →
deterministic replay), falsified by any op- or actor-tamper.

## Attributed-stream lift — "who did what" without a witness seam (Phase 81)

`OpStream.Attributed.liftWitness : StreamWitness<'Op,…> -> StreamWitness<Attributed<'Op>,…>` gives every
appended op actor / session / turn / timestamp provenance for agent-fleet accountability — as a **derived
lift over the existing three-seam `StreamWitness`, not a new witness field.** This is deliberate: the
per-op witness-metadata seam (adoption fork **F8**) was rejected and **stays rejected** — the lift
*wraps*, it does not seam. `Attributed<'Op>` is a plain envelope record (`Actor` / `Session` opaque host
strings, optional `Turn`, `At` timestamp, wrapped `Op`); the lift's `Apply` delegates to the inner reducer
on `.Op` (attribution is provenance, never state), and `Encode`/`Decode` wrap the inner op codec in a
camelCase `{"actor":…,"session":…,"turn":…,"at":…,"op":<inner>}` envelope.

- **The chain covers attribution for free.** The envelope rides *inside* the chained op's wire encoding,
  which the hash chain already folds over — so re-attributing a chained op breaks `verifyChain` exactly as
  op-tampering does, with **no change to the `OpStream` surface** and `verifyChain` unchanged.
- **Timestamp-as-data (Phase 27 discipline).** `At` is a host-supplied string carried as data — **Core
  never reads a clock**; `""` means unstamped.
- **Identity is host-side vocabulary.** `Actor` / `Session` are opaque strings — Core owns no identity
  model and stores exactly what the host supplies. This is a **distinct axis** from the chain-level typed
  `Actor` DU folded into `OpRecord` (Phase 320, `0.0.1-alpha.13`): that names the *appender* in the chain payload; the
  `Attributed` envelope carries per-op session/turn provenance *inside the op* via the lift. Both end up
  hash-covered.
- **Additive + Fable-clean (GP2/GP3).** No new witness field; unattributed streams and their codecs are
  byte-unchanged (the inner op is embedded verbatim). Encode is hand-rolled canonical JSON, decode reuses
  the self-contained JSONL scanner, so encode AND decode are Fable-clean. `Conformance.attributedLaws`
  certifies replay-preservation, chain-covers-attribution (tamper), and envelope round-trip; `byActor` /
  `bySession` are pure projection folds.

**Attested provenance is now a conformance-certified claim (Phase 60).** `Conformance.attestationLawsAt`
is a seed-replayable law kit a domain (or host) runs against its `StreamWitness` + sink to prove the
three-stage guarantee end-to-end: **checkpoint round-trip** (a signed head verifies against its chain),
**prefix attestation** (one signature is bound to the whole prefix its head folds — O(commits)),
**replay-equivalence** (the attested log replays to exactly the state the head was taken over), and
**falsification** — an op-tamper AND an actor-re-attribution, *each rehashed so `verifyChain`
re-accepts the forged chain*, are still caught by `verifyAttestation` (the head moved; the signature
covers only the original). That last property is precisely what attestation adds over a bare hash
chain, and it holds under a cryptographic `HashFn` too (a re-hashed forgery cannot be re-signed without
the host key). The `noAttestation` default satisfies the kit **vacuously**
(`Conformance.noAttestationVacuityLaws`): `Sign ⇒ None`, `Verify ⇒ false`, and the chain is unchanged —
so adopting the seam never forces a sink on a host. The crypto stays host-side (GP3); the kit
self-proves green in-repo against the reference witness with a keyed FNV/HMAC-style stand-in sink.

## Hash-chain integrity posture

The op-stream (`OpRecord`) and op-DAG (`DagNode`) chains are **tamper-evident against accidental
corruption**, not against a motivated adversary, *with the default hash*. Since Phase 320 (`0.0.1-alpha.13`) the
typed actor is inside the hash, so attribution tampering is detected on the same footing as op tampering. `OpStream.defaultHash` is
FNV-1a — fast, portable, Fable-clean, and **not cryptographic**: anyone who edits a record can
recompute the whole chain. `verifyChain` / `verifyDag` detect reordering, a dropped link, or a bit
flip; they do **not** detect a re-hashed forgery under FNV-1a. For adversarial tamper-evidence,
supply a cryptographic `HashFn` (e.g. SHA-256) at the host boundary — the `HashFn` seam is pluggable
for exactly this, and cross-host parity holds as long as both hosts use the same one.

**The supply-your-own-crypto contract is conformance-certified (Phase 65).** `Conformance.hashFnLaws`
certifies, over a seed-replayable sample under the SUPPLIED `HashFn`, that a chain hash is a pure
function of the canonical wire pre-image: **determinism** (the same op sequence hashes identically
across builds), **pre-image parity** (an incremental `append` build and a bulk reforge of the same
`(seq, actor, op)` pre-images agree hash-for-hash — the cross-host-parity foundation: two hosts on the
same `HashFn` + same pre-image get byte-identical chains), and **tamper-detection** (reorder / drop /
bit-flip caught by `verifyChain`). Until Phase 302 this read "under *any* supplied `HashFn`", and a
constant `HashFn` passed: those three tampers are caught before the hash is consulted. Since Phase 302
the family also builds the three arms that need the hash itself — an op replaced with its seq, prev and
stored hash kept, the forgery re-minted end to end (the head must move), and two envelopes differing
only in their op under one prev hash (they must hash apart) — so a `HashFn` that cannot tell ops apart
is RED, and the family is not a certificate for any `HashFn` whatever. The crypto posture itself is pinned by
`Conformance.hashFnAdversarialLaws`: a collision-resistant `HashFn` admits **no** pre-image collision
within the search budget (a re-hashed forgery cannot land a chosen head, so it is caught), whereas the
32-bit default FNV-1a **does** — the documented forgery primitive, asserted so a silent widening of the
default would be flagged as a posture regression. The adversarial branch uses a wide, test-side
stand-in that models collision resistance in-budget, so the law certifies the *contract* rather than
any particular digest.

**A cryptographic digest now ships in Core (`0.5.0`), and the default is deliberately unchanged.**
`Hash.sha256Hex` / `Hash.sha256Bytes` are a pinned pure FIPS 180-4 SHA-256 — FSharp.Core-only and
Fable-clean, so a digest taken by a server verifies in a browser. What moved is availability, not
posture: `OpStream.defaultHash` is still FNV-1a, because changing it would silently invalidate every
persisted chain in every domain, and a host that wants adversarial tamper-evidence still supplies
`Hash.sha256Hex` through the `HashFn` seam as an explicit act. The two are separately named for that
reason — a **cache fingerprint** (`fnv1a`: staleness stamps, bounded-escape content hashes, where
nobody gains by forging) and a **crypto digest** (`sha256*`: anything that becomes a signed head or a
record a dispute is read from), never interchanged.

**BOTH hashes are now certified .NET/Fable value-identical, and both were measured rather than
assumed (`sha256*` at `0.5.0`, `fnv1a` at `0.6.0`).** Compiling the two pipelines against the same
corpus — including the one-million-`a` vector — gives byte-identical SHA-256 on both, which is what
the masked add in its inner loop exists for. **`fnv1a` did not agree until `0.6.0`**, and the way
that was found is the part worth keeping: the `0.5.0` probe measured it as a by-product and reported
`fnv1a "a"` as `e40c292c` on .NET but `e40c2930` under Fable, because `h * 16777619u` transpiled to a
plain JS multiply whose product passes 2^53 — precision lost inside the operation, where a trailing
mask cannot reach it. `0.6.0` routes the multiply through a split-half 32-bit form that never builds
a product above 2^32, and a re-run of the same probe over a 124-entry corpus is byte-identical on
both pipelines.

**The .NET values did not move, and that was the constraint the fix was designed around** — the .NET
side is canonical, and `fnv1a` is folded into every stored content hash, so a change here would
invalidate persisted data rather than break a compile. The pinned vectors hold it to that
byte-for-byte. What moved is the transpiled side, which now agrees with .NET instead of disagreeing
with it. **A `fnv1a` value minted by a JavaScript host before `0.6.0` will not re-verify** — see the
migration note below.

**The Fable-compile gate cannot catch this class, and that has not changed** — it proves a construct
transpiles, not that it computes the same number, so nothing in the repo had reason to report the
divergence for as long as it existed. Two guards replace it, and neither is a compile: the `fnv1a`
vectors and an independent 64-bit reference comparison in `HashTests` pin the .NET half, and the
cross-pipeline half is the value leg ("The value leg" under Fable cleanliness), which runs
`ParityVectors` on both pipelines and byte-compares. Since Phase 217 that table carries the
124-entry corpus of the retired by-hand probe as its `hashSweep/*` rows — one column each for the
canonical `fnv1a`, `sha256Hex`, and the two deliberate copies in `OpStream` and `Column` — so one run
says both that a divergence exists and how far it reaches. Both guards were taken go-red before
being trusted: reintroducing the naive multiply was measured to leave the suite fully green while
120 of the 124 entries diverged. **Anyone touching either multiply-safe helper (`mul32`, `.+.`) runs
the value leg against a candidate pack** — `fuaran-dotnet`'s
`tests/core-fable/core-fable.ps1 -CoreVersion <v> -CoreFeed <folder>` — because a green .NET suite
is not evidence about the other pipeline, and nothing in THIS repository runs the transpiled side.

**Migration (`0.6.0`).** No action is needed for a value minted on .NET: those are unchanged, so
every persisted chain, content hash and staleness stamp written by a .NET process re-verifies
exactly as before. A value minted by a **JavaScript/Fable host** before `0.6.0` was never the true
FNV-1a of its input and will not re-verify under `0.6.0`; such a value must be re-minted from its
source data rather than carried across. Note that the pre-`0.6.0` guidance in this file was that
such a value must not be compared across pipelines at all, so it was never usable as a portable
identity — which is what makes re-minting a correction rather than a loss.

This reverses the earlier "no cryptographic hash ships in Core (GP3)" line, and the reason is worth
stating: GP3 asks that public surfaces stay FSharp.Core-only and Fable-clean, which this
implementation is — it is pure `uint32` arithmetic and compiles under the Fable gate like everything
else. What GP3 rules out is a *host-side* crypto dependency (`System.Security.Cryptography`, a keyed
MAC, a signer), and none of that is here. The previous line conflated "no crypto dependency" with "no
cryptographic algorithm", and the cost of that conflation was two domains each hand-porting the same
primitive, which is a divergence waiting for a patch that reaches one of them. Keys, signing and
attestation remain host-side behind the `IAttestationSink` seam, exactly as before.

Integrity is also **opt-in on load**: `OpStream.fromJsonl` / `Dag.fromJsonl` decode *structurally*
and do not verify — a tampered, dangling-parent, or cyclic input decodes to a clean `Ok`. Use
`fromJsonlVerified` (Phase 13) to gate the load on `verifyChain` / `verifyDag`, or call the verifier
explicitly before trusting a decoded stream/DAG.

**Compaction reads the boundary, it does not check it — verify, then compact (Phase 227).**
`OpStream.Snapshots.compact`, in either mode and under any config, reads the boundary record's hash
and TRUSTS it. The compacted stream verifies exactly when the original does only over a prefix that
was verified BEFORE it was discarded (`compact_preserves_verify` / `compact_verifies_iff_original`,
`proofs/Chain.fst`). A tamper in the prefix of an unverified stream survives compaction, verifies
across, and cannot be found once the prefix is gone. A host that compacts an unverified stream has
compacted whatever it was handed, so run `verifyChain` / `verifyChainWith cfg` first.

**A snapshot at sequence zero carries the configured genesis (Phase 227).** The boundary hash at zero
is the genesis every chain walker starts from. `OpStream.Snapshots.take` and `compact` write
`cfg.Genesis` there for the config they are handed; under `canonicalConfig` that is `""`, which is
`canonicalConfig.Genesis` and `legacyActorConfig.Genesis`, so under both shipped configs every
snapshot byte is unchanged. A stream appended under a config with any other genesis must be
compacted under that config; a snapshot at zero taken under the canonical config does not verify
across such a stream. (The pre-Phase-296 entry points this paragraph named — `snapshotAt`,
`compactWith` and the rest — left at `1.0.0`.)

### Chain pre-image portability

The chain pre-image is pluggable via `StreamConfig.Payload` (`int -> Actor -> string -> string`,
Phase 255) so a domain on a legacy chain format can verify its persisted streams and `rehash` to the
canonical form without a flag-day. That flexibility is a **migration seam, not an interchange
format**: a stream hash-chained under a bespoke `Payload` verifies only for a reader who knows that
config, so it is **not** portable across independently built hosts. **Cross-host conformance is
therefore pinned to `OpStream.canonicalConfig`** (the `{seq, actor, op}` envelope + `""` genesis): a
stream claiming the canonical `core@1.0` chain profile MUST verify under `canonicalConfig`, and a
bespoke `StreamConfig.Payload` is a **host-private profile, non-portable by declaration** — sanctioned
only as the transient input side of the `verifyChainWith <legacy>` → `rehash <legacy> canonicalConfig`
migration. This is a **definitional** boundary: `Conformance.streamLaws` already certifies
append / verify / replay over the default `canonicalConfig` pre-image, so no separate portability law
is added — "the canonical profile is verifiable under `canonicalConfig`" is what `streamLaws` proves,
and the non-portability of a bespoke pre-image is a declared contract, not a testable property. (The
decision is recorded in [`DECISIONS.md`](DECISIONS.md) D11.)

## Deterministic replay posture (Phase 27, `0.0.1-alpha.8`)

The hash chain proves a stream's *shape* was not altered, but plain `replay` re-derives state by
re-applying ops — so a non-deterministic effect evaluated **outside** the op sequence (a clock read,
an RNG draw, a network / tool response) is *not* reproducible from the file: re-running it re-reads
the live source. The **determinism-capture seam** closes that gap. `OpStream.captureEffect` records
the realized value at the boundary (keyed on the `Fuaran.Core.Function` determinism tag —
`Effect.determinismTag`); `replayEffect` feeds the recorded value back instead of re-evaluating; the
capture is hash-chained (`verifyCaptures` / `firstCaptureBreak`), so a tampered captured value is
detected exactly like a tampered op. A `Deterministic` effect emits no capture and replays unchanged.
With captures present, a recorded clock/random/network session **replays byte-identically**, upgrading
the op-stream from *structural* tamper-evidence to *full deterministic behavioral replay*.

**Opaque determinism-label rule.** `EffectCapture.Determinism` is an **open label space**. The core
interprets exactly one value — `"deterministic"`, the label of an effect that emits no capture. Every
**other** label (`"clock"` / `"random"` / `"network"`, the composite `"clock+random"` spellings of a factor set, and any host- or domain-minted label) is
**opaque and reserved to the effect supplier**: the core carries it verbatim through the hash-chained
journal and keys replay ordering on the `Eff` identity, never on interpreting the label. A conformant
host MUST carry a non-`"deterministic"` label without interpretation — inventing or re-meaning one is
a host/domain concern, not a wire change. This is the open-label counterpart of the `IdWitness`
posture: identity/label vocabulary is supplied, not fixed, and the substrate stays FSharp.Core-only by
never depending on the `Fuaran.Core.Function` `Determinism` set.

**The guarantee is *outcome*-faithful, not *trajectory*-faithful.** Replay reproduces the values the
boundary *returned*; it does not reproduce the internal path a stateful effect took to produce them.
An effect that reads non-determinism internally (a seeded RNG whose individual draws are not each
captured) replays its *outcome* from the journal, but its internal trajectory only matches if the
consumer reseeds from the captured seed — `OpStream.capturedSeed` exposes the recorded seed precisely
so that trajectory replay is automatic rather than left to discipline. Capture is also **opt-in**: a
legacy stream with no journal (or an exhausted journal) falls back to live evaluation, so replay is
only as reproducible as the effects a host chose to capture. The capture chain shares `OpStream`'s
non-cryptographic-by-default posture — supply a cryptographic `HashFn` for adversarial tamper-evidence.

**Replay consumes captures in record order (Phase 40, `0.0.1-alpha.13`).** `replayEffect` takes the
requesting effect identity and asserts the head capture's `Eff` matches it; a cross-identity misorder
is a *named error*, not a silently-mispaired value. A driver must therefore replay effects in the same
identity order it recorded them. This is a signature change (`replayEffect decode eff det effect captures`).

## verifyFunction guarantee scope (Phase 52)

`Conformance.verifyFunction` (Phase 48) certifies that an artifact-function emits a
**validator-conformant tree for every binding in the sampled / symbolic param space** — *structural
validity over the param space*, plus the Fork-3 effect cross-check (the result observes no effect the
declaration doesn't cover). It is **scoped deliberately and must not be read more widely:**

- It does **NOT** certify that the output is *semantically good* — only that it is structurally valid
  (validator-clean) for each binding.
- For a function whose effect class is **non-deterministic** (`Clock` / `Random` / `Network` — a
  stochastic spec, e.g. an MMM model or a future `Fuaran.Model` dialect), the verdict asserts
  **structural validity only**, never output determinism or quality. A structurally-valid but
  value-varying output still verifies.
- The verdict is **effect-class-agnostic** — the determinism axis does not change it; verify keys on
  structure, not on the effect class. The `Conformance.verifyHonestyLawsAt` guard law proves this.

Advertising "verified" as a quality or determinism guarantee is an **over-claim** the statistical
domains must not make. This is a contract/scope clarification, not a behaviour change: the shipped
`verifyFunction` is unchanged.

## Memo soundness preconditions (Phase 53 / 56)

`Function.applyMemo` is sound only under two preconditions, now both enforced or certifiable:

- **Effect honesty (Phase 53, enforced).** Memoisation gates on the **observed** effect
  (`Function.observedEffect` — the widest effect walked over the whole subtree), not the declared root.
  A function whose root declares `Pure`/`Deterministic` while a descendant leaks `Clock`/`Random`/
  `ReadsHost` is bypassed (never cached/served), so the cache cannot serve a stale result for an
  actually-impure function even when the root under-declares. `Conformance.memoSoundnessLawsAt` proves it.
- **Encoder injectivity (Phase 56, caller precondition + certifiable).** The cache key is
  `Tree.encodeHash w.Tree encode node`; the caller-supplied `encode` MUST be injective over the node
  space, or two distinct trees collide and the cache serves the wrong one. Core cannot enforce this for
  an arbitrary host encoder, so it is a documented precondition (`applyMemo` / `Tree.encodeHash`
  doc-comments) that a domain certifies with `Conformance.encoderInjectivityLawsAt` (the
  "certify-your-codec" posture, like `Corpus.codecLaws`).

## Op-script footprint + independence (Phase 78)

`Ops.footprint : NodeWitness -> IdWitness -> SkeletonOp list -> Footprint` computes an op-script's
read/write **footprint** — the node ids / structural positions it reads and writes — as a pure, total
derivation *from the script* (the `paramsOf` precedent, Phase 77), and `Ops.independent : Footprint ->
Footprint -> bool` is pairwise footprint disjointness. This is the structural basis for dispatch-time
conflict refusal, computed leases (Phase 84), and proposal arbitration (Phase 85) — the coordination
edge is *computed from the script*, never separately declared. Additive over `Ops.fs`: **no new witness
field** (GP2), FSharp.Core-only + Fable-clean (GP3), no failure case (GP4 — a footprint is always a
value). `Conformance.footprintLaws` certifies soundness (an independent pair commutes under `apply`,
content-hash equal), monotonicity (a sub-script's footprint ⊆ its script's), and determinism.

**Conservativity is the contract — it is over-approximating by design.** `footprint` records *more*
potential collisions than a tree-aware analysis would, so `Ops.independent = true` is a **promise** (the
two scripts provably commute), and `independent = false` is **always a safe answer, never a defect
report**. A host may treat `false` as "serialise these two edits" without it implying either is wrong.

The **pinned over-approximations** (enumerated here and in the `Footprint` / `footprint` / `independent`
doc-comments), each a place where an exact set is a *tree fact the pure script cannot name*:

- **A `RemoveNode` / `MoveNode` has an unknown source parent.** Removing or moving a node also rewrites
  its *source* parent's child-list, but that parent — and every ancestor relationship — is a tree fact
  the op does not carry. So such a target lands in `Footprint.UnknownParentWrites`, and an op that
  removes/moves conservatively conflicts with **every** structural write in a concurrent script.
  Disjoint-subtree independence is therefore **not proven** when either side removes or moves (proving
  it needs the tree); a remove/move is independent only of a structure-free script. This is why two
  agents each editing a *different* subtree are still reported dependent if either deletes or relocates a
  node — safe, deliberately coarse.
- **A `RemoveNode`'s content-write records only the target id, not its (tree-unknown) subtree.** Sound
  for the skeleton five because *every* skeleton op is a structural write, so the unknown-parent rule
  above already serialises a remove/move against any concurrent structural op. A domain that layers a
  **pure in-place content-edit op** (an "update a node's payload" op, which the skeleton five have none
  of) on top must fold the removed subtree into the footprint itself — it has the tree.
- **The same-named-parent rule is pinned:** two ops that write the *same named* parent's child-list
  (two positional inserts, an insert + a reorder, …) are **not** independent — they shift the same
  siblings. Two structural writes on *distinct named* parents (with disjoint content) are independent.

The `Footprint` record's four address sets (`Reads`, `StructureWrites`, `ContentWrites`,
`UnknownParentWrites`) are keyed by the `IdWitness.ToString` string form (no `comparison` demanded of
`'Id`) and grow additively within a major like the rest of the surface.

## Branch merge: conflict enumeration + reconciliation (Phases 64, 83)

The op-DAG (`Fuaran.Core.OpStream.Dag`) exists for branch/merge: [Phase 08](.) gives the merge-base +
branch-delta of two heads, [Phase 26](.) projects a delta as an applyable `'Op` sequence. Two pure,
generic functions close the merge — and the **GP6 line runs straight through them**: Core *detects and
folds what provably commutes*; the domain *resolves* everything else. Neither picks a winner or applies
a policy.

- **`Dag.conflicts : ('Op -> Footprint) -> 'Op list -> 'Op list -> MergeConflict<'Op> list`** — the
  DETECTION half. Given two branch deltas from a common base and a caller-supplied address projection
  (`'Op -> Footprint`; the domain feeds `Ops.footprint` over its own witnesses, since the DAG is
  generic over the opaque `'Op`), it enumerates every op pair across the two deltas that targets the
  same address and would interfere — a typed `MergeConflict` per collision (`Left`, `Right`, the shared
  `Address`, and the `Shape`). It is the **`canApply` of merging**: it decides nothing, applies nothing,
  picks no winner. `MergeConflictShape` is a **closed DU** (GP5) — `ConcurrentUpdate` (both branches
  write one node's content, or one writes what the other reads), `InsertPositionClash` (a shared *named*
  structural parent), `MoveVsRemove` (a remove/move whose unknown source parent races the other side's
  structural write). No witness field (GP2); FSharp.Core-only, Fable-clean (GP3).

- **`Dag.reconcile : ('Op -> Footprint) -> Dag.T<'Op> -> string -> string -> string -> Result<'Op list, MergeConflict<'Op> list>`**
  — the mechanical FOLD half. Non-conflicting deltas ⇒ `Ok` the merge script
  (`betweenOps base headA ++ betweenOps base headB`, one applyable sequence); any conflict ⇒ `Error`
  `Dag.conflicts`' report verbatim, **nothing applied** — no partial merge. It is to merging what
  `apply` is to a `canApply`-clean op. The pinned composition order is **canonical form, not semantics**:
  a conflict-free pair commutes, so `A ++ B` and `B ++ A` replay to content-hash-equal state; pinning
  delta-A-then-delta-B makes the output a pure function of `(base, headA, headB)`.

**Conflict detection inherits #78's conservativity exactly.** `conflicts` fires on a pair *iff*
`Ops.independent` would reject it — the three shapes partition the negation of the independence
predicate over the four `Footprint` address kinds. So `conflicts = []` carries the same **promise**
`Ops.independent = true` does: the two deltas provably commute — never a false "clean merge". A
remove/move is therefore reported against *any* concurrent structural write (its source parent is a
tree fact the pure script cannot name — the pinned `UnknownParentWrites` over-approximation), tagged
`MoveVsRemove` and keyed by the removed/moved id. This makes `Dag.reconcile`'s **footprint
cross-validation** hold by construction: `Ops.independent (footprint deltaA) (footprint deltaB) ⇒
reconcile = Ok` (`Conformance.reconcileLaws`). The converse is *not* claimed — footprints
over-approximate, so `conflicts = []` means footprint-independent (and, via `footprintLaws`' certified
soundness, genuinely commuting), not tree-aware-minimal.

Certified by `Conformance.mergeConflictLaws` (symmetry up to `Left`/`Right` swap; determinism;
agreement with #78 — a pair is reported iff its footprints are not independent) and
`Conformance.reconcileLaws` (clean-fold order-independent replay by content hash; #78 cross-validation;
the conflicted path returns `conflicts`' report with nothing applied; determinism + order pinning).
Both are append-only additions to the conformance kit; a domain that reconciles branches runs them
alongside its base certification. `Dag.conflicts` / `Dag.reconcile` add a build-time dependency from
`Fuaran.Core.OpStream.Dag` onto `Fuaran.Core.Ops` (for the `Footprint` type); additive, no cycle.

## Confluence / interleaving law (Phase 80)

`Conformance.concurrencyLaws` certifies the coordination claim the agent-fleet substrate rests on:
**op-scripts `Ops.independent` declares disjoint replay to the same tree under every interleaving of
their individual ops** — confluence. `footprintLaws` (Phase 78) proves the two whole-script sequential
orders commute; concurrent appenders produce arbitrary op-level interleavings, and this law samples
them: interleavings grow as C(m+n, m), so per independent pair it checks a **bounded, deterministic,
seed-replayable sample** (the two sequential extremes + 8 uniform riffles) for **interleaving
totality** (every sampled interleaving applies cleanly) and **content-hash-equal replay** (the
Phase-06 encoder hash). A **coverage vacuity guard** fails a run whose generator never produced an
independent pair, so a green report always means "certified over real pairs", never "nothing was
checked".

**What it deliberately does not certify: anything about dependent scripts.** Independence is
*sufficient* for confluence, not *necessary* — a pair `Ops.independent` does not declare independent
is **skipped, not asserted** (it may or may not commute; the law makes no claim about it). This is
the same posture as Phase 78's conservativity contract: `independent = false` stays "serialise
these two", never "one of these is wrong". The footprint function is injectable
(`concurrencyLawsWith`) purely as the teeth seam — the in-repo suite proves the law bites under a
falsely-independent footprint; domains run `concurrencyLaws` (pinned to the real `Ops.footprint`).

### Mechanised: the N-lane fold law is a theorem (Phase 131)

The Phase 100 fold-confluence law — the same lanes fold to the same state however they arrive,
a lane set that cannot fold halts with the same canonical report however it arrives, and no set
folds under one order and halts under another — is **proved** in `proofs/DagFold.fst` over an F\*
model of `Ops.independent` / `Dag.conflicts` / `Dag.reconcileMany` / the replay, for every lane
count and every permutation, under the domain's commutation promise for independent ops. The
model is extracted to F# and run by the suite beside production over the Phase 100 generators and
the wire corpus (`Proofs.Oracle`), and the proof leg holds the committed oracle to a fresh
extraction byte for byte. **No shipped surface changes**: the claim is about what the existing
`reconcileMany` does, stated as a theorem on a model and tied to production by a differential
test. The claims ladder — what is proved, what is only tested, what is assumed — is in
`proofs/README.md`, and "formally verified" is spent on the theorem alone.

## Open-core posture

Apache-2.0, abstractions-tier. The contract (protocol + witness records + signature/
effect types + envelopes) lives here; each domain's **evaluator / reduction** (render,
recompute, regenerate geometry, reflow) stays domain-side. No domain evaluator may leak
into a `Fuaran.Core.*` package (FGP 6).

## Fable cleanliness

Public surfaces are FSharp.Core-only and Fable-clean on **both** the encode and the decode
path. Decode is portable as of Phase 241: `Fuaran.Core.Wire.Json.parse` is a hand-rolled
recursive-descent JSON parser (no System.Text.Json), `Fuaran.Core.Wire.Decode`'s combinators
operate over the resulting `JVal`, and `Fuaran.Core.OpStream.fromJsonl` ships its own
self-contained line scanner — so a Fable-compiled host can decode, `verifyChain`, and replay
in-browser without a host-side boundary. `Json.render` and `Json.parse` are inverses over
canonical wire JSON. The Fuaran wire `JVal` model has no `null`; a bare `null` token is
rejected by name on decode (and see ["Null-tolerant read"](docs/releases/0.7.0.md#null-tolerant-read-phase-102-070) for the opt-in, read-side-only
tolerance that lets a foreign document spell an absent member `null` without the model gaining one).

**Enforced, not asserted — in `fuaran-dotnet`, since Phase 217.** This repository runs no Fable
compiler: the compiler belongs where a Fable toolchain already lives, so the gate that enforces this
claim is **`fuaran-dotnet`'s `tests/core-fable/`** — run by that repository's Fable stage
(`tests/fable-laws/fable-check.ps1`) and by its CI's `fable-portability` job. It has two legs. The
**compile leg** references every public package, touches each one's encode/decode surface, and
checks that every reference was actually transpiled; a construct that is not Fable-clean fails
there. The **value leg** is "The value leg" below. Until Phase 217 both ran here, in `./verify.ps1`
(Phase 54 and Phase 118); DECISIONS.md D55 records why they moved and why neither was retired.

**The cost of the move, and the rule that pays it.** That gate sees this repository at the version
`fuaran-dotnet` pins, so on its own it fires when the pin is raised — a release after a divergence
was introduced. So **every version cut of this repository cites a green run of that gate against
the candidate packages** before the release gesture (see "Versioning policy"). The cut-time run
restores every `Fuaran.Core.*` package from the candidate folder alone, into an isolated package
cache so a same-version repack is never served stale, derives the surface from what the candidate
ships, and REQUIRES the value leg.

`Fuaran.Core.Idl` joined the surface at `0.8.0` — it was the one `src/` package absent from it, which
made its portability an unprovable claim rather than a certified one. What had blocked it was not the
model but the emitters sharing its project; splitting them into `Fuaran.Core.Idl.Codegen` (Phase 97)
removed the obstacle rather than working around it.

**Since Phase 185 the surface is CHECKED for every packable package, not asserted over them — and
since Phase 217 that half stays here, because it needs no Fable.**
`Fuaran.Core.Tests.FableSmokeCompletenessTests` derives the packable set from the tree — every
project under `src/` whose own `IsPackable` is not `false`, honouring `Directory.Build.props` — and
fails naming any that neither ships the `fable/` source distribution (the `Directory.Build.props`
convention, which is what a Fable consumer compiles and so is the act of making the claim) nor is
listed in [`fable-exclusions.json`](fable-exclusions.json). The exclusions are
`{ "package", "reason", "phase" }` — what is off the surface, why, and which phase decided — and the
check runs in both directions, so an entry whose package ships the `fable/` sources anyway fails as
loudly as a package covered by neither. The receiving gate holds its own completeness check against
the same entries, over its pins and over a candidate's packages. The same test family checks the
rest of Phase 217 rather than asserting it: no script or workflow here invokes `dotnet fable`, the
tool manifest does not carry the compiler, and the only authored `Fable.Core` reference is
`Fuaran.Core.Wire`'s — which powers Wire's own `#if FABLE_COMPILER` float layout, the fix for the
`Json.render` defect below, and is the one sanctioned library reference (D55). Off the surface today: `Fuaran.Core.Idl.Codegen` and `Fuaran.Core.Idl.Cli` (build-time and
.NET-only — they emit source or are a console tool, and neither ships the `fable/` source
distribution), which are the two entries of `fable-exclusions.json`; the suite holds every name
this paragraph and that file give to a project in the tree. (`Double.ToString`
with the round-trip specifier is *not* Fable-supported — float→string must route through
`Wire.Canon.canonicalFloat`; `Double.TryParse`'s style/provider arguments are ignored under Fable but
parse invariantly, which is a benign warning, not a gate failure.)

**A compile gate is not a VALUE gate, and the difference has cost real defects here.** It proves a
construct transpiles; it cannot notice that the transpiled code computes a different number. That is
exactly how `fnv1a` sat divergent behind a green gate until `0.6.0` (see "Hash-chain integrity
posture"). Where a value must agree across pipelines, the claim is bought by the value leg below and
by an independent in-suite reference implementation on the .NET side. Anything new making a
cross-pipeline value claim adds its vector to `ParityVectors` — see "The value leg".

### The value leg (Phase 118, `0.18.0`)

**The compile gate has a VALUE leg beside it.** A committed vector table — `Hash.fnv1a`,
`Hash.sha256Hex` / `sha256HexOfBytes` / `utf8Bytes`, `Wire.Canon.canonicalFloat`,
`Wire.Json.render`'s own float layout, encode + decode of a reference witness document through both
renderers, `OpStream.defaultHash` over a two-op chain, `ConfRng`'s draw stream, and (since Phase 217)
the 124-row `hashSweep/*` corpus — runs on **both** pipelines and the output is byte-compared.
**The table is PUBLIC since Phase 217: `Fuaran.Core.ParityVectors`, in `Fuaran.Core.Conformance`**
(additive, `0.31.0`). It ships as code rather than data because the transpiled side has to COMPUTE
it, and a consumer sees this repository only as packages. `fuaran-dotnet`'s `tests/core-fable/`
compiles it from the package's `fable/` sources with the rest of the surface and diffs
`ParityVectors.lines ()` between .NET and node. The .NET half is pinned here against committed
expected bytes by `ParityVectorTests` (the sweep by row count and digest). Those are two claims and
neither implies the other: a change that moves both pipelines identically fails the pinned table and
passes the diff, and one that moves only the transpiled side does the reverse.

**It FAILS without a JS runtime; it never skips a leg it can run.** A check that reports success on
a machine where it did not run asserts exactly what was not checked. The one case it cannot run is
STATED rather than skipped: at a `fuaran-dotnet` pin below `0.31.0` the restored Conformance package
has no table, so that gate says so on every run and names the cut-time rule that covers the gap —
and it FAILS if a pin at or above `0.31.0` restores a package without the table. The by-hand probe
that used to sit beside the leg (`tests/hash-parity-probe/`, retired by Phase 217) is absorbed: its
corpus is the `hashSweep/*` rows.

**What the leg found on its first run, and what changed as a result.** `Wire.Json.render` — a public
encode surface — **threw under Fable for any `JFloat`**. Its float case was
`System.String.Format(InvariantCulture, "{0:R}", f)`, and Fable's `String.format` refuses the
round-trip specifier at RUNTIME ("The round-trip format is not supported by Fable"). It compiled
cleanly, so the Fable gate had been green over it since the gate existed, and the smoke it gates had
never been executed. From `0.18.0` the layout is one shared internal helper: the finite re-lay that
`Canon.canonicalFloat` already carried is now `FloatLayout`, used by both, and `Json.render` renders
through it. **The .NET bytes are unchanged and pinned** — `String.Format(inv, "{0:R}", f)` and
`f.ToString("R", inv)` agree across the finite range, on `-0` (`"-0"`, which `Json.render` keeps and
`canonicalFloat` deliberately collapses to `0`), and on the non-finite tokens (`NaN` / `Infinity` /
`-Infinity`, which are not valid JSON and are exactly what `Json.tryRender` exists to refuse). What
moved is the transpiled side, from throwing to agreeing. A Fable host that had been avoiding
`Json.render` for floats can stop.

**The go-red measurement, re-taken.** Removing the masked add `Hash.(.+.)` leaves `HashTests` 12/12
green **and** the committed .NET vector table green — the mask is a no-op on .NET, so neither can see
it — while the parity leg reddens on exactly the two-block SHA-256 vectors (the 56-byte FIPS message
and the byte-form vector over it) and leaves every single-block vector untouched. That is the class
this leg exists for, and it is now measured on the gate's own vectors rather than in a scratch probe.
**Re-taken in the receiving gate (Phase 217.E):** the same perturbation, packed as a `0.31.0`
candidate and run through `fuaran-dotnet`'s cut-time mode, reported 40 of 164 vectors divergent —
`sha256/two-block` first, then the multi-block `hashSweep/*` rows — and the clean candidate 164/164.

### The portability set — what the Core Fable smoke reaches is PROMISED (`0.19.0`)

The gate above compiles its smoke program against every public package, and what it touches it
touches deliberately. (The smoke lived in this repository as `tests/fable-smoke/` until Phase 217;
it is now `fuaran-dotnet`'s `tests/core-fable/Program.fs`, carried over unchanged, touches included.) A member reached from there therefore carries a **portability guarantee to Fable
consumers**, not merely a proof that it compiles — and two such members have no other caller in
this repository, which is exactly the shape a caller-count reading takes for dead code. Narrowing
one to `internal` would withdraw a guarantee **without turning the gate red**: the smoke project
would simply stop seeing it, and stop asserting anything about it. They are promised, and named here
so the next such reading knows it:

- **`Fuaran.Core.Idl.Sanitize.sanitizeAttributes`** and **`Idl.Sanitize.scrubMarkdown`** — the
  sanitisers, which a client host needs precisely because it is the side rendering untrusted content.

**The rule generalises: the Core Fable smoke is a promise surface, not a scratch project.** Adding a
member to it makes a portability promise; removing one withdraws it. Neither is a tidying edit —
and since Phase 217 the edit is made in `fuaran-dotnet`, so a change here that should widen the
promise names the smoke line it needs in its own record.

## Canonical float layout (Phase 55)

`Fuaran.Core.Wire.Canon.canonicalFloat : float -> string` is the **single cross-host float → string
encoder**, and every float→wire / float→key path in the substrate routes through it (the tree/columnar
wire codecs via `Canon.render`, `DataFrame`'s cell-string + group-key, `Query`'s invocation key). The
pinned layout: non-finite floats → the fixed JSON-string tokens `"NaN"` / `"Infinity"` / `"-Infinity"`;
`-0.0` collapses to `0`; a finite float uses `Double.ToString("R", InvariantCulture)` on .NET and the
byte-identical JS shortest-round-trip re-layout under Fable (WIRE_FORMAT §2 rule 5). A conformant TS /
Python host MUST replicate exactly this. `Conformance.canonicalFloatLaws` certifies determinism, finite
round-trip, and the stable non-finite tokens. `Fuaran.Core.Validator` references `Fuaran.Core.Wire` and
routes its uniqueness tokens through `Canon.canonicalFloat` directly — one canonical float layout,
one implementation, no inlined copy to drift (a host comparing Validator tokens cross-host uses the
same canonical form).

## Value-level compare-and-append (Phase 79)

`Fuaran.Core.OpStream.appendIf : HashFn -> StreamWitness -> expectedHead -> Actor -> op -> state ->
records -> Result<state * records, AppendRejection<'Rej>>` is a **compare-and-append** primitive:
it chains the op only when the stream's current head (`OpStream.head`) equals `expectedHead`,
otherwise it returns `AppendRejection.StaleHead (expected, actual)` naming both heads. On a match it
is behaviourally identical to `append` — a domain-reducer rejection is forwarded as
`AppendRejection.Domain rej`. It is **additive** over `OpStream`: `append` is unchanged and there is
no new `StreamWitness` field.

**The CAS is value-level only, by design.** The guard is over the *logical* chain head — a `string`
value. Core owns no filesystem (GP3) and no process model (GP6), so **file-level atomicity for a
persisted JSONL stream is the host's job**: the host serialises the read-check-append (an advisory
lock, a single-writer actor, or rename-into-place) and calls `appendIf` inside that critical section.
`appendIf` is what makes the losing side of a race a *typed outcome* (`StaleHead`) rather than a lost
write — it does not by itself provide the OS-level mutual exclusion. The intended host shape is the
viewer/CLI **single-mutation surface**: one serialised writer per stream. `AppendRejection<'Rej>` is a
`Rejection`-class envelope — a new case is additive; removing a case is breaking.

## Idempotent append (Phase 82)

`Fuaran.Core.OpStream.appendIdempotent : HashFn -> StreamWitness -> key -> Actor -> op -> state ->
KeyIndex -> records -> Result<AppendOutcome, 'Rej>` is the **at-least-once retry** primitive: an
orchestrated session that times out mid-append re-sends its op under the same invocation key (the
Phase 27 `Function.invocationKey` shape), and the re-send *converges* instead of double-applying. A
fresh key appends **chain-identically to `append`** (the idempotency guard adds nothing to the chain
— no new record field, no new witness field, GP2) and returns the incrementally-updated `KeyIndex`; a
seen key returns `AppendOutcome.Duplicate` naming the entry the key already produced (an `EntryRef` —
`Seq` + `Hash`, GP5) with the caller's stream and index untouched. A domain rejection is forwarded
verbatim and indexes nothing — the key stays fresh for a corrected retry. Total, never a throw (GP4).

**The index is caller-threaded pure state — Core holds no seen-key registry (GP6).** `KeyIndex` is a
plain value: `KeyIndex.ofStream keyOf records` rebuilds it from any stream (a total fold, first-wins
on a duplicate-keyed plain-`append` stream), and the `KeyIndex` returned by each `Appended` maintains
it incrementally — the two agree (the rebuild-parity law; `ofStream` is literally a fold of
`KeyIndex.add`, which is itself first-wins). `keyOf : 'Op -> string` is a per-call parameter, not a
witness seam (GP2). **Key uniqueness scope is per-stream**: an index is only meaningful against the
stream it was built from; cross-stream dedup, storage, and locking stay host-side (GP3/GP6), exactly
as for the Phase 79 CAS.

**The full agent retry loop composes idempotency with the CAS — key check first, deliberately.**
`appendIdempotentIf` (Phase 82 ∘ Phase 79) checks the key *before* the head: when a retry's earlier
attempt actually landed (the ack was lost, not the write), the head has advanced and a bare
`appendIf` would return `StaleHead` forever — the key check first lets the retry converge on
`Duplicate` under any head. Only a fresh key reaches the CAS (stale head ⇒
`AppendRejection.StaleHead`: re-read the stream, rebuild the index via `KeyIndex.ofStream` — the
re-read picks up any own-earlier append — and retry; matched head ≡ `append`).
`Conformance.idempotencyLaws` certifies fresh≡append, duplicate convergence, rebuild parity, and the
idempotency-before-CAS ordering — seed-replayable.

## Proposal arbitration (Phase 85)

`Arbitration.arbitrate : NodeWitness -> IdWitness -> 'Node -> OpScriptProposal list -> Arbitration`
decides which subset of N op-script proposals can land together against one base tree: batch
`Ops.canApplyAll` against the base filters the inapplicable (each rejection carries the op-algebra's
own envelope + the failing op index), then a greedy pass in the **pinned order** (ascending proposal
id) accepts each proposal whose footprint (Phase 78) is `Ops.independent` of everything already
accepted. The result is a **deterministic, total partition** (GP4 — analysis only, the base is never
mutated): the accepted set is mutually independent — by footprint soundness its scripts apply
confluently in any order (`MergedScript` is the pinned-order composition) — and every rejection is
typed + actionable (GP5): `Inapplicable (opIndex, rejection)`, or `Conflicts interfering` naming the
accepted ids (computed against the **full** accepted set) the agent must rebase against. Proposal ids
are expected unique — a queue assigns them, and `AiSurface.Proposals` (Phase 59) is one such assigner,
projecting to `OpScriptProposal` with `Proposals.toOpScript`; `arbitrate` is total on duplicate-id
input, but the permutation-invariance guarantee assumes unique ids (only then is the pinned order a
total order). Since Phase 157 that assumption is a total check —
`Arbitration.duplicateIds proposals = []` — and [`proofs/README.md`](proofs/README.md)'s theorem 13
proves the guarantee under it and that it cannot be dropped.

**It lives in `Fuaran.Core.Ops` since Phase 192** (`AiSurface.arbitrate` until then), beside
`Ops.footprint` and `Ops.independent` — the two it is the other end of. `OpScriptProposal<'Node,'Id>`
is the minimal record the partition needs: `Id` (what the pinned order sorts by), `Holder` (whom a
rejection is reported to) and `Ops`. The lifecycle fields a queue carries — author, timestamp,
intent, approval status — stay in `AiSurface.Proposals`, because arbitration reads none of them.

**Coexistence, not quality (GP6).** Arbitration says which proposals *can coexist*, never which is
*better*: no ranking policy, no quality judgement, no evaluator lives in Core. A host that wants
priority or scoring exercises it upstream — the pinned order is proposal id, and id assignment is the
host's lever — or downstream, by choosing what to re-propose; the partition itself is mechanical.

**Greedy-maximal, not maximum (the Phase 52 honesty discipline).** Greedy-in-pinned-order yields *a
maximal* mutually-independent set — nothing rejected could be added without a conflict — not *the
maximum* one; a different order could accept a larger set (and finding the maximum is NP-hard, and
would smuggle a ranking policy into Core besides). The order is pinned, documented, deterministic.
And independence is conservative (Phase 78): `Conflicts` means "not **provably** coexistent", never
"wrong".

**The consumption shape (dispatcher / orchestration).** `arbitrate` → the accepted set lands (the
merged script, or per-proposal in any order); every `Conflicts` reject is re-proposed after rebasing
onto the tree the accepted set produced; every `Inapplicable` reject repairs against its op-algebra
envelope. `Conformance.arbitrationLaws` certifies determinism + input-permutation invariance, the
total partition, pairwise independence, rejection actionability, and any-order confluence
(`concurrencyLaws`, Phase 80, is the stronger op-level-interleaving form of the same claim — a domain
that arbitrates runs both). `ArbitrationRejection<'Id>` is a `Rejection`-class envelope — a new case
is additive; removing a case is breaking.

## Fold-confluence pack: N-lane arrival-order invariance (Phase 100)

Phase 80 certifies confluence for a domain's TREE ops (interleavings of two independence-declared
skeleton-op scripts replay to the same tree); Phase 83 certifies that a two-head `Dag.reconcile`
folds order-independently. Both are pinned to the skeleton-op algebra and both stop at two branches.
A local-first deployment converges neither: it converges **N lanes** — one op-stream per writer,
off one shared base, arriving in whatever order the network delivered them — over its own `'Op` and
`'State`. Phase 100 is that claim, made runnable per domain.

Two surfaces, one in each half of the boundary.

- **`Dag.reconcileMany : ('Op -> Footprint) -> Dag.T<'Op> -> string -> string list -> Result<'Op list, MergeConflict<'Op> list>`**
  — the N-lane generalisation of `reconcile`: every UNORDERED pair of lane deltas is checked with
  `conflicts`, then the whole set is composed at once. `reconcileMany fp dag b [x; y]` is
  `reconcile fp dag b x y`, so #78's conservativity contract is inherited verbatim — `Ok` still
  carries the promise that the deltas provably commute, never a false "clean merge". Pairwise is
  sufficient because set disjointness IS pairwise: there is no N-way interference the sweep can
  miss. The `heads` order is **canonical form, not semantics**, exactly as `reconcile`'s pin is.
  Folding by repeated pairwise `reconcile` is deliberately NOT the same operation: it would mint
  intermediate merge nodes and let the pairing order leak into the result.

- **`FoldConfluence.laneFoldLawsAt`** (in `Fuaran.Core.Conformance`) — the teeth. Given a domain's
  `StreamWitness`, its footprint projection, a state hash and a lane generator, it folds each
  generated lane set under every sampled arrival order and certifies five laws: **lane-fold
  determinism** (a folding set folds to one state hash under every order — a reducer that rejects
  under one order and not another fails here too), **lane-halt determinism** (a halting set halts
  with the same canonical report under every order), **outcome classification invariance** (no lane
  set folds under one order and halts under another), and — since `0.18.0`, where the pack's two
  hand-written coverage guards became one **sample-adequacy** law (see ["Sample adequacy"](docs/releases/0.18.0.md#sample-adequacy-what-a-familys-sample-must-contain-phase-121-0180)) — a guard demanding
  that the sample exercised both a folding and a halting set, reported with the counts it did reach.
  A run that never collided has not tested the halt law at all. Seed-replayable; every divergence is **shrunk** by greedy
  delta-debugging before it is reported, so the counterexample is a minimal reproducer in the
  domain's own op encoding rather than the generated trial that happened to expose it.

**Three outcomes, not two — and the third is the point.** A lane set that folds under one arrival
order and halts under another is named as its own defect rather than folded into "not confluent",
because it is the worse failure: the deployment that received the lanes the other way round
proceeds, and two replicas then disagree about whether they diverged at all. Distinguishing "folds
identically" from "halts identically" is what makes that observable.

**The halt report is canonicalised, and it has to be.** `Dag.conflicts` is symmetric only up to a
`Left`/`Right` swap (`mergeConflictLaws` pins exactly that), so handing it the same two deltas the
other way round yields a report that differs in presentation. `FoldConfluence.canonicalConflictReport`
renders each interference as shape + address + its UNORDERED op pair, deduplicated and sorted — so
"halts identically" is a claim about the interference, not about the printing.

**Sampling bound.** N lanes admit N! arrival orders. `FoldConfluence.arrivalOrders` enumerates all of
them up to `permutationBound` (24 = 4!) and above that samples deterministically — the identity, the
reverse, and shuffles from a fixed seed. The order set is a pure function of the lane count, which is
what lets a shrunk counterexample be re-measured against the same orders. A green verdict therefore
means "certified over the sampled orders", never "proved for all N!".

**What it deliberately does not certify.** Not **resolution** — the pack says a halt is
order-invariant, never that it was correct, and picks no winner (GP6). Not **necessity** — as with
Phase 80, footprint independence is sufficient for a clean fold and never necessary, so a lane set
the footprints declare interfering is required to halt *consistently*, not required to be genuinely
unmergeable. Both surfaces are additive; `laneFoldLawsAt` is opt-in like `footprintLaws` — a domain
that converges concurrent lanes runs it.

**How a domain runs it.** Supply a `StreamWitness<'Op, 'State, 'Rej>`, an address projection
`'Op -> Footprint` over the domain's own vocabulary (a tree domain feeds `Ops.footprint`; a non-tree
domain writes its own), a canonical `'State -> string` hash, and a `LaneGen` naming the base state, a
genesis op and a lane source; then call
`FoldConfluence.laneFoldLawsAt witness footprintOf hashState gen laneCount seed iterations` (or
`certifyFold` for the aggregate verdict). The in-repo suite certifies the reference witness and a
second, non-tree domain whose state is a `Map` and whose footprint is its own, and proves the pack
can FAIL: an openly order-sensitive reducer whose footprint declares everything independent produces
a shrunk two-lane, one-op-per-lane counterexample.

**The clause that is easiest to get wrong is the address projection, and the classification law is
what catches it.** An op that requires an address to EXIST must READ it, not merely write its own:
an op creating an entity and an op depending on that entity are not independent, and a projection
that omits the read declares them so. The two then fold under one arrival order and reject under the
other — which is exactly the classification law, and exactly the reason it is stated separately.

**A one-time behaviour change: the kit's sampler moved to the high-order bits (0.12.0).**
`ConfRng.intBelow` — the small-integer draw every generator in `Fuaran.Core.Conformance` is built
from, and therefore `choose` and `shuffle` with it — reduced by `v % n` until 0.12.0. The state is a
linear congruential generator taken mod 2^32, in which bit `k` has period 2^(k+1), so a modulo by a
small `n` read the weakest bits in the word, and read them *in phase*: generators drawn consecutively
off one stream chose in lockstep rather than independently. The coverage guards above are what
surfaced it — a three-lane generator halted on 150 of 150 trials and the fold law never executed
once. `intBelow` now takes the top `bitWidth (n - 1)` bits and redraws an out-of-range candidate:
full-period bits only, and exactly uniform rather than modulo-biased, from shifts and comparisons
alone so the kit stays value-identical under Fable.

The signatures did not move; **every value did**. A consumer pinning a seed will see different
generated data, so a law that certified green over one sample is now certifying over another — which
is the point, since a weak generator in a conformance kit does not produce failures, it produces
false assurance. Treat a repin as a **read**: check that a coverage or vacuity guard which passes
still passes for a reason, rather than raising the iteration count until it does. In this repo's own
suite the change moved exactly one expectation — a four-lane reference-witness certification whose
lanes were a fixed two ops each, a shape that essentially never has mutually-independent footprints
over a six-node tree; its lane length is now drawn, which restores a mixture of both outcome classes
at every lane count.

## Where sections moved

Until Phase 397 this document was the contract and every release's entry in one file. The entries
now live in the release ledger, [`docs/releases/`](docs/releases/README.md), one file per version
slot, and every heading that moved keeps a working anchor there. This table is permanent: an old
link to `STABILITY.md#<anchor>` is resolved by finding the anchor in the right-hand column and
following the row to its file, where the anchor is the same unless the row writes `old → new`
(a heading GitHub numbered as a duplicate here is unique in its new file). The suite holds every
row: each anchor named must be a heading of the file the row names (the `Package roster` family,
"the moved-anchor table resolves").

| New home | Anchors that moved there |
|---|---|
| this file | `#the-compute-strand--column--dataframe--query-first-class` → `#the-column-and-query-strand-first-class` |
| [`docs/releases/0.36.0.md`](docs/releases/0.36.0.md) | `#0360--released-2026-10-08-as-v0360`, `#seven-defects-on-the-untrusted-and-gated-paths-each-pinned-by-a-go-red-plant-phase-383--breaking-source-capabilitylookup-gains-policy-record-widening-and-pipelineevalerror-gains-evalpolicyrefused-union-widening-the-rest-additive-the-wire-none`, `#the-0352-surface-is-total-codect-refuses-rather-than-raises-declareenumannotate-answers-an-error-list-the-scaffold-emits-a-refusal-spike-proposal-refuses-typed-and-one-spelling-per-twin-pair-phase-384--removal--retype-the-wire-none`, `#the-two-cross-runtime-claims-the-suite-did-not-measure-are-measured-phase-387-decisionsmd-d124--additive-on-the-net-surface-the-fable-sources-lose-two-reflection-laws-removal-every-sampled-set-moves-the-wire-none`, `#one-admission-gate-at-every-reader-on-the-query-side-too-the-typed-resolver-reaches-paging-capture-and-replay-phase-385--breaking-source-queryerror-gains-unreadableargs-union-widening-the-rest-additive-the-wire-additive`, `#one-truthiness-and-a-scope-for-the-approval-switches-phase-396--no-surface-change-test-project-tooling-only-no-packages-surface-or-wire-byte-moves`, `#the-copies-d2-does-not-justify-collapse-into-one-body-each-and-three-of-the-five-long-files-split-along-their-banners-phase-388-decisionsmd-d125--additive-opstreamjsonlquote-decodertagdispatchwith-the-wire-none`, `#one-entry-story-for-the-conformance-kit-every-family-answers-lawresult-list-and-is-rostered-a-vector-family-handed-nothing-is-red-by-name-and-every-witness-taking-family-takes-at-phase-390-decisionsmd-d127--additive-breaking-behavioural-an-empty-store-reds-the-stored-families-eighteen-bare-names-obsolete-until-100-the-wire-none`, `#a-query-declares-what-it-filters-and-how-it-orders-a-resolver-that-cannot-honour-either-refuses-by-name-phase-398-decisionsmd-d128--breaking-source-query-gains-where-and-orderby-record-widening-queryerror-and-resolvefault-gain-cases-union-widening-the-rest-additive-the-wire-additive`, `#a-release-built-consumer-no-longer-fails-reaching-an-internal-member-the-proof-legs-cold-check-holds-on-both-oses-phase-402-decisionsmd-d129--no-surface-change-no-public-member-signature-or-wire-byte-moves-behavioural-for-a-release-built-consumer-which-stops-failing` |
| [`docs/releases/0.35.2.md`](docs/releases/0.35.2.md) | `#0352--released-2026-10-07-as-v0352`, `#the-generator-emits-the-structural-derivations-a-domain-otherwise-writes-by-hand-phase-374--additive-the-wire-none`, `#the-generator-emits-defect-collecting-per-spec-decoders-phase-377--additive-the-wire-none`, `#the-typescript-emitter-gains-the-structural-derivations-and-the-collecting-decoders-phase-380--additive-the-wire-none`, `#the-typescript-declarations-type-the-derived-members-phase-381--additive-the-wire-none`, `#canonical-and-codect-on-the-spine-digest-proposed-and-held-for-a-placement-ruling-phase-379--additive-the-wire-none`, `#digest-joins-the-spine-on-route-b-the-type-in-tree-the-profile-pinned-constructor-in-the-new-fuarancorecontentaddress-phase-382-decisionsmd-d122-as-amended--additive-the-wire-none` |
| [`docs/releases/0.35.1.md`](docs/releases/0.35.1.md) | `#0351--released-2026-10-06-as-v0351`, `#validated-declarations-on-the-invocable-seams-one-admission-gate-a-capped-count-one-binding-per-address-phase-307-decisionsmd-d111--breaking-source-invokeerror-applyerror-and-queryerror-widened-breaking-behavioural-new-refusals-a-stricter-reader-every-nodeinvocationkey-moves-the-wire-additive`, `#merkle-digests-a-change-classification-a-defect-set-gate-and-the-projection-reading-them-phase-314-decisionsmd-d112--additive-two-widenings-of-the-slots-own-class-source-ride-it-projectionsnapshot-gains-subtrees-scope-gains-bysubtreedigest`, `#the-generated-hosts-agree-with-the-interpreter-on-the-readers-corners-and-the-sentinel-slots-phase-347-decisionsmd-d113--breaking-behavioural-documents-both-generated-hosts-accepted-are-refused-the-public-surface-and-the-wire-unchanged`, `#one-place-for-each-rule-coverage-that-states-what-was-evaluated-the-decode-refusal-declared-as-it-is-and-the-inherited-javascript-member-names-refused-phase-348-decisionsmd-d114--breaking-source-for-typescript-consumers-of-the-declaration-file-breaking-behavioural-a-vocabulary-using-one-of-the-refused-names-is-refused-and-an-early-stopped-symbolic-verification-no-longer-reports-exhaustive-the-rest-additive-the-public-surface-and-the-wire-unchanged`, `#the-capability-model-grows-the-handler-table-the-pipeline-and-the-codecs-phase-354-decisionsmd-d115--additive-no-public-surface-moves-no-emitted-byte-moves`, `#the-sanitisation-floor-the-configured-stream-operations-and-the-escape-table-gain-laws-the-kit-draws-nested-batches-the-markdown-scrub-reaches-a-fixed-point-phase-349-decisionsmd-d116--additive-for-the-kit-breaking-behavioural-sanitizescrubmarkdown-removes-two-splices-it-emitted-live-and-every-op-drawing-family-draws-a-different-sample-at-the-same-seed-the-wire-unchanged`, `#query-paging-on-the-input-side-a-registration-that-refuses-a-repeated-parameter-and-the-four-lifecycle-verbs-on-all-four-registries-phase-316-decisionsmd-d117--breaking-behavioural-queryregistryregister-refuses-a-declaration-naming-a-parameter-twice-breaking-source-functionregistry-is-opaque-registrationerror-and-packloaderror-widened-the-rest-additive-the-wire-unchanged`, `#a-policy-gate-on-the-invocable-registries-an-id-scoped-write-gate-a-guarded-ai-surface-and-invocation-keyed-capture-phase-318-decisionsmd-d118--breaking-binary-policydecision-moves-from-fuarancoreaisurface-to-fuarancorefunction-which-the-surface-gate-classes-removal-for-fuarancoreaisurface-source-unchanged-breaking-source-invokeerror-and-queryerror-widened-capabilityregistry-and-queryregistry-gain-a-field-the-rest-additive-the-wire-additive`, `#the-tree-model-gains-a-frame-theorem-and-the-write-gates-cover-and-lock-are-proved-over-it-phase-362-decisionsmd-d119--additive-no-public-surface-moves-no-emitted-byte-moves`, `#content-addressed-stores-pin-a-frozen-canonical-encoding-encodingprofile-the-0300-rendering-as-v1-and-a-two-witness-rehash-phase-360-decisionsmd-d120--additive-no-default-moves-no-existing-signature-changes-no-emitted-byte-moves`, `#a-string-with-nothing-to-escape-is-written-whole-the-escape-scans-before-it-copies-and-the-writer-escapes-into-its-own-builder-phase-365--additive-jsonescapeinto-no-emitted-byte-moves`, `#a-string-is-parsed-by-runs-not-characters-phase-366--no-surface-change-faster-the-same-values-and-the-same-refusals`, `#the-parsed-float-check-skips-the-re-lay-under-fable-phase-367--no-surface-change-faster-under-node-the-same-values-and-the-same-refusals`, `#a-typed-reader-over-the-parsers-own-grammar-jsonreader-phase-368-decisionsmd-d121--additive-jsonreader-jsonreaderror-and-the-jsonreader-functions-parse-keeps-every-value-and-every-refusal`, `#an-op-stream-line-is-decoded-by-runs-not-characters-phase-369--no-surface-change-faster-store-load-and-verify-the-same-values-and-the-same-refusals`, `#generated-typescript-codecs-write-clean-strings-whole-phase-370--no-surface-change-the-emitted-encstr-source-gains-one-line-no-wire-byte-moves`, `#an-integer-past-int32-is-parsed-without-an-exception-under-node-phase-372--no-surface-change-faster-under-node-the-same-values-and-the-same-refusals`, `#columnaggregate-reads-its-aggregate-and-column-type-by-pattern-not-by-union-equality-phase-353--no-surface-change-faster-under-node-the-same-answers-and-the-same-refusals`, `#a-float-renders-without-the-re-lay-where-the-layouts-agree-and-both-fable-only-float-branches-have-parity-vectors-at-their-edges-phase-373--no-surface-change-faster-under-node-the-same-bytes-twenty-parity-vectors-added` |
| [`docs/releases/0.34.0.md`](docs/releases/0.34.0.md) | `#0340--released-2026-10-02-as-v0340`, `#the-f-target-encodes-the-wire-shape-it-declares-the-classifiers-rules-are-one-table-with-supportjson-as-an-input-and-codegenfs-splits-behind-the-gen-facade-with-one-type-emitter-phase-293--breaking-source-in-diff-inside-the-slots-standing-class-gens-baseline-moves-by-zero-lines-genfsharptypes-output-changes`, `#the-opstream-module-becomes-a-forwarding-facade-over-the-concern-files-phase-332--additive-no-public-surface-moves`, `#one-sanitisation-floor-total-on-hostile-input-with-the-url-floor-at-parity-with-the-ui-tiers-phase-291--additive-no-public-surface-moves`, `#rejections-that-explain-themselves-opsinterference-names-the-clause-conflicts-carries-it-and-a-stale-proposal-has-a-bounded-report-phase-248--breaking-source-arbitrationrejectionconflicts-gains-a-field-retype-the-rest-additive`, `#jsonparse-reads-every-float-canonrender-writes-the-canonical-float-family-samples-the-whole-double-range-phase-253--additive-the-read-side-widens-no-emitted-byte-moves`, `#the-idl-serves-a-vocabulary-that-is-not-the-uis-phase-252--breaking-source-one-record-widened-two-emitters-retyped`, `#the-sanitisation-floor-joins-the-cross-pipeline-table-phase-291s-fable-half--additive`, `#a-reachability-index-on-the-lane-dag-dagreach-the-indexed-append-and-merge-and-two-index-taking-overloads-phase-289--additive`, `#off-walk-identity-in-the-footprint-and-arbitration-a-keyed-footprint-a-domains-own-arbitration-pair-and-the-container-rule-at-the-partition-phase-247--additive`, `#an-authored-doc-annotation-emitted-as-the-members--summary-phase-255--breaking-source-annotations-widened-additive-on-the-artifact-wire-and-the-generated-emission`, `#the-capability-and-query-seams-speak-to-a-model-phase-251--additive`, `#a-checkpoint-on-the-lane-dag-a-sealed-state-at-a-node-bounded-replay-from-it-and-a-dag-that-may-begin-at-one-phase-288-decisionsmd-d93--additive`, `#a-placement-algebra-tree-lowering-and-fresh-ids-treeplacement-opslower-freshids-phase-312--additive`, `#footprint-soundness-at-a-domains-own-ops-and-the-lane-dag-foldonce-folds-phase-249--additive`, `#the-compacted-stream-lives-on-checked-jsonl-writers-strict-effect-replay-phase-301-decisionsmd-d97--additive`, `#a-structural-integrity-strand-a-containment-grammar-and-a-reference-witness-phase-313-decisionsmd-d98--breaking-source-union-widening-of-rejection-and-diffdifferror-the-rest-additive`, `#a-decode-layer-with-typed-path-carrying-refusals-phase-310-decisionsmd-d99--additive-a-malformed-optional-member-is-now-refused-where-it-was-read-as-absent`, `#the-kits-non-degeneracy-floor-every-gated-arm-counts-what-it-built-and-reds-at-zero-phase-302-decisionsmd-d100--additive-no-public-member-moves-families-go-red-on-witnesses-they-passed`, `#the-five-smaller-seams-are-total-and-speak-the-spines-conventions-phase-298-decisionsmd-d101--breaking-source-defect-widened-both-registries-register-retyped-four-unions-qualified-deny--submitdenied-retyped-approvalfailure-widened-the-rest-additive`, `#the-ladder-tells-the-truth-about-production-phase-309-decisionsmd-d102--additive-no-public-surface-moves`, `#the-strangers-on-ramp-every-package-carries-its-xml-documentation-file-readme-claims-carry-the-version-they-arrived-in-and-the-invocable-seams-have-an-adoption-path-phase-254--additive`, `#the-invocable-seams-converge-one-space-relation-one-effect-and-value-space-codec-one-registry-shape-a-pipeline-that-type-checks-its-edges-and-totality-that-agrees-with-required-phase-295-decisionsmd-d104--breaking-source-and-breaking-wire-the-toschema--tojsonschema-bytes-of-a-signature-with-a-repeat-hole-the-rest-additive`, `#the-idl-emitter-compiles-what-it-emits-and-the-three-hosts-agree-by-value-phase-303-decisionsmd-d105--additive-two-public-members-added-refusals-where-silence-was`, `#the-classifier-grades-by-what-old-documents-do-phase-304-decisionsmd-d107--breaking-behavioural-classifier-verdicts-move-two-of-them-stricter-no-public-member-added-removed-or-retyped`, `#a-structural-edits-change-set-is-the-diff-of-the-two-trees-and-the-order-is-certified-phase-308-decisionsmd-d108--additive-one-public-member-added-change-sets-grow-a-stale-value-becomes-a-correct-one`, `#the-generated-decoders-refuse-with-the-typed-decode-error-phase-337-decisionsmd-d109--breaking-source-every-generated-f-decoder-is-retyped-to-result_-decodeerror-and-the-generated-typescript-refusal-becomes-an-object-no-api-baseline-moves`, `#lanes-and-a-whole-dag-total-order-with-the-multi-parent-primitives-phase-311-decisionsmd-d110--breaking-behavioural-firstbreak-and-the-message-of-fromjsonlverified-name-the-earliest-of-several-breaks-in-history-order-rather-than-the-smallest-id-the-faulty-set-and-verifydags-verdict-are-unchanged-everything-else-is-additive-new-types-and-functions-beside-unchanged-ones`, `#every-public-member-carries-a-doc-comment-held-by-a-ratchet-that-only-moves-up-phase-339--additive-doc-comments-and-a-test-no-public-member-added-removed-or-retyped`, `#proof-coverage-at-operation-granularity-phase-335--additive-the-kits-law-family-roster-gains-an-operation-roster-and-every-public-operation-maps-to-a-ladder-row-a-law-family-or-a-recorded-exclusion` |
| [`docs/releases/0.33.0.md`](docs/releases/0.33.0.md) | `#0330--released-2026-10-01-as-v0330`, `#the-compute-strand-leaves-moved-not-removed-phase-258--breaking-removal`, `#an-exact-decimal-in-the-column-model-decisionsmd-d72--breaking-union-widening`, `#the-proposal-pricing-harness-leaves-fuarancoreidlcodegen-for-the-cli-phase-230--breaking-removal`, `#the-witness-record-field-freeze-is-held-by-a-law-family-now-not-at-the-10-release-candidate-phase-232--breaking-union-widening`, `#an-unknown-node-actor-kind-refuses-instead-of-reading-as-human-phase-260--breaking-for-a-store-that-carries-one-no-public-surface-moves`, `#the-c-facade-is-removed-phase-231-decisionsmd-d28--breaking-removal`, `#streamlaws-guards-its-sample-lanefoldlaws-counts-its-rejected-lanes-and-an-aggregates-pass-reads-its-counts-phase-245--breaking-for-a-stream-generator-that-never-reaches-an-accepted-op-or-a-tampered-chain-no-public-surface-moves`, `#the-gate-closes-its-blind-spots-and-the-packages-gain-metadata-phase-294--additive-no-public-surface-moves`, `#one-string-escaping-on-the-spine-every-control-character-is-u00xx-phase-287-decisionsmd-the-spine-owns-the-string-escaping-rule--breaking-wire-bytes-the-managed-surface-moves-additive`, `#every-hash-pre-image-is-injective-and-every-tree-digest-sees-the-trees-shape-phase-290--breaking-digest-move-memo-keys-projection-digests-contenthash--encodehash-digests-index-fingerprints-and-canonicalcodes-all-change-value-additive-on-the-public-surface-treeencodepreimage`, `#the-kit-gets-a-runner-splits-by-topic-behind-a-facade-declares-each-family-once-and-gives-with-one-meaning-phase-297--breaking-record-widening-of-familieslawfamily-six-report-types-move-to-namespace-level-and-verdict-changes-obsolete-forwards-for-this-draft`, `#column-trusts-nothing-it-is-handed-and-the-parser-holds-to-the-json-grammar-phase-299-decisionsmd-the-parser-holds-to-the-json-grammar-nan-sorts-last-the-column-codec-carries-only-a-table-it-can-decode-and-rowcodec-is-obsoleted--breaking-union-widening-columnerror-aggregateerror-a-changed-case-payload-columnerrornotjson-a-removal-celldefaultfor-refusals-of-input-that-was-accepted-and-a-value-move-schemafingerprint-additive-beside-them`, `#totality-against-the-machine-in-wire-and-column-phase-306-decisionsmd-d84--breaking-refusals-of-input-that-was-accepted-versioningprofiletryparse-jsontryrender-canontryrender-jsonreadint32-a-refusal-where-an-infinity-was-answered-float-sum-and-value-moves-at-the-edge-of-the-float-range-mean-median-stddev-additive-beside-them`, `#the-lane-dags-reconcile-applies-shared-history-once-and-refuses-a-rejecting-lane-set-the-same-way-under-every-arrival-order-append--merge-refuse-a-splice-bearing-parent-id-phase-300-decisionsmd-the-reconcile-partitions-the-region-above-its-base--breaking-dagreconcilemanys-signature-and-error-type-change-scripts-change-value-where-they-were-wrong-halts-become-folds-and-append--merge-raise-where-they-accepted`, `#the-keyed-witness-reaches-the-apply-path-phase-286-decisionsmd-uniqueness-over-keyed-positions-is-cores-refusal--breaking-record-widening-of-keyedwitness-and-a-field-removed-union-widening-of-rejection-the-rest-additive`, `#the-determinism-axis-is-a-set-of-factors-joined-by-union-phase-319-decisionsmd-d82--breaking-retype--removal-the-determinism-wire-vocabulary-widens`, `#one-jsonl-scanner-that-refuses-the-dags-typed-refusals-the-snapshot-family-with-its-mode-on-the-snapshot-phase-296-decisionsmd-one-jsonl-scanner-shared-rather-than-copied--breaking-dagappend--dagmerge-change-return-type-snapshot-gains-a-field-record-widening-four-unions-become-qualified-access-and-input-that-was-accepted-is-refused-the-rest-additive-with-obsolete-forwards-for-this-draft`, `#the-light-set-the-small-exports-consumers-copied-phase-315-decisionsmd-the-light-set--additive-with-three-breaking-items-beside-it-a-case-payload-rejectionwouldnestunderself-breaking-source-a-type-that-moves-package-rejectionguidance-aisurface--ops-source-compatible-and-refusals-of-input-that-was-accepted-an-empty-actor-id-on-read-columnvalidatorunique-reading-decimals-by-value`, `#the-remaining-algebra-symmetries-schemapatch-propagation-pull-an-index-carried-through-an-edit-phase-317-decisionsmd-the-algebras-remaining-symmetries-are-built--breaking-record-widening-of-schemadelta-and-a-value-move-treeindex-stamps-the-rest-additive`, `#the-dags-checked-append-gains-a-verifying-variant-phase-329-decisionsmd-the-verified-append-is-the-call-site-check-d83-left-to-the-caller--additive-on-the-public-surface-daglaws-gains-two-laws-and-a-stream-generator-whose-dag-nodes-never-differ-in-state-now-reds-the-family-as-never-reached`, `#the-conflict-shape-renderer-distinguishes-the-three-shapes-again-fuarancoreconformance-decisionsmd-d88--a-behaviour-correction-on-the-draft`, `#decimal-vectors-and-the-cut-gate-the-parity-table-carries-decimaltext-the-law-set-carries-decimal-documents-and-the-cell-ranging-families-reach-the-decimal-phase-276--additive-on-every-surface-breaking-as-a-verdict-change-for-two-kit-families-aggregatenullskiplaws-is-now-guarded-columnarvalidatorlaws-gains-a-guard-no-public-surface-moves`, `#the-three-remaining-with-entries-take-the-rules-order-and-the-six-conformance-kit-witness-records-join-the-freeze-phase-330-decisionsmd-the-kits-last-three-with-entries-are-reordered-with-no-forward-and-the-six-kit-witness-records-are-frozen--breaking-removal-of-the-old-parameter-order-the-freeze-is-a-promise-no-surface-moves-for-it` |
| [`docs/releases/0.32.0.md`](docs/releases/0.32.0.md) | `#0320--released-2026-09-26-as-v0320`, `#updatenode--the-in-place-skeleton-op-phase-250--breaking-union-widening`, `#propagationevalwith--evalfromwith--the-prior-value-inside-the-contract-phase-250--additive`, `#propagationchangedforop--the-post-edit-change-set-for-a-structural-op-phase-250--additive`, `#column-granular-reads--propagationpartread-partdependencymap-nodedependencies-dirtyfromchangedparts-columnopschangedcolumns-phase-250--additive`, `#columnopsdeltaof--a-cell-edit-reaches-incremental-as-one-row-phase-250--additive`, `#witness-taking-law-families--the-seam-ai-surface-and-columnar-families-certify-the-domain-phase-246`, `#capabilitylawswith-querylawswith-capabilitypipelinelawswith-and-their-witnesses--additive`, `#aisurfacelaws-runs-the-domains-decide--a-verdict-change-breaking-whatever-the-surface-gate-says`, `#columnaroplawswith-takes-the-domains-generator--breaking-for-that-member-retype`, `#incrementallawswith--additive`, `#the-roster-and-the-counts`, `#the-compute-boundary-prepared-inside-core--two-new-packages-fuarancoredataframeconformance-and-fuarancoredataframecsharp-phase-257`, `#the-f-targets-omit-at-default-wrapper-terminates-for-a-list-map-record-or-union-member-phase-256--none-on-the-api-a-fix-in-what-it-emits` |
| [`docs/releases/0.31.0.md`](docs/releases/0.31.0.md) | `#0310--released-2026-09-26-as-v0310`, `#the-fable-gate-leaves-this-repository-the-parity-table-is-public--fuarancoreparityvectors-phase-217--additive`, `#the-refusable-family-audit--opalgebra-and-reducer-are-guarded-over-accepted--refused-and-certifys-verdict-moves-with-them-phase-220--breaking`, `#the-six-drawn-refusal-families-are-guarded-too-and-the-kits-reference-generators-reach-the-refused-branch-phase-223--breaking`, `#a-snapshot-at-sequence-zero-carries-the-configured-genesis--snapshotatoptwith-compactwith-compactchainonlywith-phase-227--additive`, `#every-capture-keys-pre-image-is-injective-through-one-canonicaliser--hashcanonicalfields-phase-225--breaking-a-keys-value-with-two-additive-members`, `#one-payload-for-the-graft-containment-refusal--differrortargetnotacontainers-field-is-target-phase-228--breaking-one-named-field-renamed-by-name-use-only`, `#required-means-non-null--a-required-query-param-bound-only-to-null-is-refused-as-the-new-queryerrorrequiredparamsnull-phase-226--breaking-behaviour-and-a-case-added-to-a-closed-union`, `#a-tree-typed-slot-has-a-value-space--valuespaceslottree-so-a-capability-over-a-slotted-artifact-is-invocable-phase-229--breaking-a-case-added-to-a-closed-union-and-behaviour`, `#a-chain-only-compaction-under-any-streamconfig-is-verifiable-through-the-public-surface--verifyacrosschainonlywith-phase-236--additive`, `#core-emits-every-law-set-it-is-the-reference-for--capability-lawsjson-moves-out-of-the-ui-tier-phase-235--additive-tests-and-corpus-only`, `#the-public-surface-gate-sees-a-union-field-rename-and-classes-it-retype-phase-237--no-package-moves-the-gates-own-baseline-format` |
| [`docs/releases/0.30.1.md`](docs/releases/0.30.1.md) | `#0301--never-released-its-entries-ship-in-0310`, `#vacuity-is-measured--every-law-family-reports-a-case-count-and-the-roster-export-carries-it-as-a-cases-column-phase-196--additive-with-one-behaviour-change-worth-reading-if-you-run-attestationlaws-without-a-signing-sink`, `#one-model-bridge-row-leaves-the-proof-contract-a-domain-inherits-phase-200--additive-nothing-to-compile`, `#defaults-fill-on-decode-is-a-law-and-the-full-node-envelope-is-declared-phase-201--additive-and-api-is-byte-identical`, `#the-propagation-contract-at-your-evaluator--propagationevaluatorlaws-and-evaluatorwitness-phase-211--additive` |
| [`docs/releases/0.30.0.md`](docs/releases/0.30.0.md) | `#0300--released-2026-09-23-as-v0300`, `#witness-surface-scope-becomes-a-domain-obligation-with-a-law-phase-189--additive-adds-one-row-to-a-consumers-conformance-census`, `#the-hardening-default-is-retired--hardenpolicydefault-is-deleted-and-an-absent-harden-block-means-declared-nothing-phase-180--breaking-on-the-api-and-on-what-a-pre-179-artifact-means`, `#the-lease-strand-leaves--fuarancorelease-and-conformanceleaselaws-are-removed-phase-188--breaking-a-removal-and-the-package-is-no-longer-emitted-at-all` |
| [`docs/releases/0.29.0.md`](docs/releases/0.29.0.md) | `#0290--released-2026-09-21-as-v0290`, `#the-incremental-corpus-reaches-a-row-local-step-reading-a-cross-row-column-phase-212--additive-may-turn-an-adopters-conformance-run-red`, `#a-merged-order-no-longer-reuses-the-position-of-a-row-a-window-moved-phase-215--corrective-wrong-answers-in-01800280`, `#arbitrationduplicateids-and-the-id-uniqueness-hypothesis-as-a-law-phase-157--additive-one-law-list-grows`, `#a-guarded-canontryrender-beside-the-canonical-renderer-phase-165--additive-rides-this-draft`, `#familieslawfamily-gains-reason-and-the-roster-says-why-a-family-is-opt-in-phase-194--record-widening` |
| [`docs/releases/0.28.0.md`](docs/releases/0.28.0.md) | `#0280--released-2026-09-20-as-v0280`, `#the-dataframe-wire-spells-out-a-column-naming-member-phase-213--breaking-on-canonical-encode-additive-on-decode` |
| [`docs/releases/0.27.0.md`](docs/releases/0.27.0.md) | `#0270--released-2026-09-19-as-v0270`, `#linear-time-row-access-phase-206--behaviour-identical-no-public-surface-moves`, `#incremental-evaluation-agrees-with-full-evaluation-as-a-theorem-phase-186--additive-no-public-surface-moves`, `#the-query-seam-carries-the-deferred-envelope-phase-198--breaking-a-public-return-type-moves`, `#arbitration-leaves-aisurface-for-the-op-layer-phase-192--breaking-a-public-function-and-its-types-change-package`, `#the-relocation-kind-footprint-widening-was-measured-and-declined-phase-163--no-surface-moves`, `#an-evaluator-reads-only-what-it-declared-phase-209--breaking-a-union-case-is-added-and-the-driver-refuses-a-non-conforming-evaluator`, `#top-n-is-maintainable-phase-207--breaking-a-case-added-to-stepincrementality`, `#steps-after-a-group-by-phase-202--breaking-a-case-added-to-fallbackreason-a-field-added-to-incrementaleval`, `#the-capability-seam-carries-the-deferred-envelope-phase-210--breaking-a-host-bodys-type-and-three-public-return-types-move`, `#the-restricted-refresh-pays-for-the-delta-phase-208--breaking-incrementalevals-representation-becomes-private` |
| [`docs/releases/0.26.0.md`](docs/releases/0.26.0.md) | `#0260--released-2026-09-17-as-v0260`, `#the-artifact-always-carries-its-harden-block-phase-179--additive-on-the-wire-and-on-the-api`, `#fuarancorefamilies--the-law-family-roster-exported-by-the-kit-phase-184--additive`, `#a-refused-columnar-op-has-no-inverse-phase-181--corrective-and-it-rides-this-slot`, `#the-generated-proof-scripts-presence-split-now-linear-phase-182--additive-on-the-api-and-it-advances-the-slot`, `#the-fable-smokes-membership-is-derived-phase-185--additive-and-two-promises-now-kept`, `#the-package-roster-is-derived-and-this-documents-header-is-held-to-version-phase-199--docs--gate-no-surface-moves`, `#codegen-refusals-are-data--every-remaining-throw-in-fuarancoreidlcodegen-is-now-a-codegenerror-phase-195--breaking` |
| [`docs/releases/0.25.0.md`](docs/releases/0.25.0.md) | `#0250--the-proof-programmes-certification-leg-and-the-hardening-policys-undeclared-half--released-2026-09-15-as-v0250`, `#what-shipped-in-it-and-the-class-of-each`, `#the-two-additive-moves-on-fuarancoreidlcodegen-phases-173-168`, `#two-artefact-facts-that-are-not-api-classes-phases-172-and-the-slots-own-re-emit`, `#the-hardening-policys-undeclared-half-phase-178--additive-and-it-advances-the-slot` |
| [`docs/releases/0.24.0.md`](docs/releases/0.24.0.md) | `#0240--the-apply-engine-correctness-programme-and-the-proof-programmes-contract-changes--released-2026-09-15-as-v0240`, `#an-f-proof-model-target-for-the-idl-generator-0240-phase-150--additive`, `#a-container-aware-sequence-surface--opsapplyallwith--opscanapplyallwith-0240-phase-160--additive`, `#the-unknown-parent-over-approximation-is-necessary-not-merely-conservative-0240-phase-143--no-contract-change`, `#insertchild-refuses-a-subtree-that-breaks-id-uniqueness-0240-phase-137--a-refusal-class-widening`, `#dagdagbreakreason-is-a-closed-du-0240-phase-147--breaking`, `#conformancecodecinjectivitylaws--the-op-codecs-own-contract-certifiable-0240-phase-145--additive`, `#treewellformed--structural-validity-named-once-0240-phase-139--additive`, `#conformanceopalgebra-gains-a-preservation-law-0240-phase-139--additive`, `#the-apply-conformance-family-0240-phase-139--additive-and-it-is-a-corpus-artefact`, `#conformancediffcontainedlaws--a-contained-diff-certified-by-the-engine-that-runs-it-0240-phase-141--additive`, `#applycontained-refuses-a-graft-whose-interior-is-not-a-container-0240-phase-161--a-refusal-class-widening` |
| [`docs/releases/0.23.0.md`](docs/releases/0.23.0.md) | `#0230--the-core-api-asks-routed-here-from-the-ui-tier-phase-125--released-2026-09-13-as-v0230`, `#chainbreakreason-is-a-closed-du-0230--breaking`, `#colexprnow--a-now-literal-at-a-declared-grain-0230--breaking`, `#limit-and-sort-accept-a-param-in-their-scalar-slots-0230--breaking`, `#declared-defaults-the-generative-property-and-one-named-backend-divergence-0230`, `#a-declared-transparent-case-as-a-default-is-refused-by-the-f-backend-too-0230--breaking`, `#idlgenuseshosted-is-removed-0230--breaking` |
| [`docs/releases/0.22.0.md`](docs/releases/0.22.0.md) | `#the-c-facade-an-f-free-authoring-surface-over-the-closed-unions-phase-128-0220`, `#construct-then-encode-the-authoring-surface-certified-phase-126-0220`, `#one-classifier-for-the-wire-profile-bump-and-the-f-consequence-table-phase-127-0220` |
| [`docs/releases/0.20.0.md`](docs/releases/0.20.0.md) | `#the-conformance-kits-draw-stream-xorshift32-fable-identical-0200--breaking` |
| [`docs/releases/0.19.0.md`](docs/releases/0.19.0.md) | `#surface-narrowing-the-uncalled-internals-0190--breaking`, `#the-narrowing-had-a-caller-schemawalknosources-is-public-again-phase-129-0220` |
| [`docs/releases/0.18.0.md`](docs/releases/0.18.0.md) | `#static-output-schema-derivation-phase-112-0180`, `#sample-adequacy-what-a-familys-sample-must-contain-phase-121-0180` |
| [`docs/releases/0.11.0.md`](docs/releases/0.11.0.md) | `#incremental-column-layer-evaluation-phase-99-0110`, `#the-merged-order--sort-admitted-phase-115-0180`, `#one-scale-for-rowsevaluated-and-a-declined-prime-is-primed-phase-117-0180--breaking`, `#the-bounded-frame-and-the-filtering-join--window-and-join-partly-admitted-phase-120-0180`, `#the-row-set-preserving-window-family--every-window-admitted-0190` |
| [`docs/releases/0.9.0.md`](docs/releases/0.9.0.md) | `#column-layer-deltas-phase-98-090` |
| [`docs/releases/0.8.0.md`](docs/releases/0.8.0.md) | `#the-idl-engine--two-packages-two-promises-phase-97-080`, `#declared-defaults-what-the-generator-renders-and-what-it-refuses-phase-124-0210`, `#declared-annotations-on-cases-and-fields-phase-113-0180`, `#kind-and-enum-case-annotations-and-the-declared-retirement-path-phase-119-0180`, `#the-retirement-clause-of-the-vocabulary-growth-charter`, `#the-artifact-reads-back-the-declaration-triple-phase-114-0180`, `#the-emitted-artefacts-line-endings-are-the-generators-phase-129-0220` |
| [`docs/releases/0.7.0.md`](docs/releases/0.7.0.md) | `#null-tolerant-read-phase-102-070` |
| [`docs/releases/0.2.1.md`](docs/releases/0.2.1.md) | `#typed-row-source-codec-fuaran665-021` |
