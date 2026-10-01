# IDL stability classes — one table, two axes

_Phase 127. The classifier is `Fuaran.Core.Idl.Diff` in `Fuaran.Core.Idl.Codegen`; the command is
`fuaran-core-idl` (`Fuaran.Core.Idl.Cli`). This document states what the code decides, for a reader
with no build. External surface guards and corpus gates cite this table rather than each
re-deriving the mapping from the compiler's behaviour._

A change between two `idl.json` revisions has **two independent consequences**, and reading either
one as the other is the mistake this table exists to stop:

- the **wire** consequence — what happens to a document, and to an emitter that produces one;
- the **F#** consequence — what happens to a consumer's source compiled against the generated
  structural layer (`Gen.fsharpTypes` / `Gen.fsharpModuleWith`).

They diverge routinely. An **optional field added** is additive on the wire — every existing
document is byte-unchanged and stays valid — and it still stops every full record literal
compiling, because an F# record literal must name every field whether or not its type is an
`option`. A **`hostSurface` edit** is the same divergence in the other direction: invisible on the
wire, and it moves a generated field's type. A classifier that reported only the first axis would
tell a consumer it repins without source changes, in exactly the cases where it does not.

## Axis 1 — the wire severity

Reported per changed member with the rule it cites (`STABILITY.md`, `VOCABULARY.md` §4).

| Severity | Meaning |
|---|---|
| `additive` | every previously-valid document stays valid and every previously-conformant emitter stays conformant |
| `breaking-for-emitters` | old documents still decode, and an emitter written against the old contract now produces one that does not — a minor on paper, a break in practice |
| `breaking-wire` | a `/v2/` major event: a document that was valid is not, or its bytes moved |
| `host-surface-only` | not observable on the wire at all — a generated-declaration change |
| `undecided` | the change crosses an **erased** slot (`hosted` / `json` / `opaque`) whose admitted values the artifact deliberately does not state, so the artifact cannot decide it |

## Axis 2 — the F# consequence classes

Each class is named for the **site that stops working**, because that is what a consumer reads in
the compiler output.

| Class | Site | What the consumer sees |
|---|---|---|
| `full-literal-construction` | every full record literal that builds the owner | `FS0764` ("No assignment given for field") on an added field, `FS1129` on a removed one, `FS0001` on a moved type. **Independent of optionality** — `string option` is still a field the literal must name. |
| `exhaustive-match` | every exhaustive `match` over the generated DU | `FS0025` on an added case — a **warning** by default, an error wherever a consumer sets `TreatWarningsAsErrors`, and a `MatchFailureException` at run time on the first value carrying the new case — and `FS0039` on a removed one. |
| `type-name-reference` | every reference to a generated type name | `FS0039`, or the wrong arity, when a whole type leaves or its type parameters move. A type **arriving** is not this class: nothing could have referenced it. |
| `stale-package-slot` | nothing, until the package slot is reused | No compile event at all if the version was **repacked** rather than advanced: a consumer restoring the same version from a warm cache compiles against the new shape and runs against the old one, and the mismatch arrives as an `InvalidCastException` thrown from code that type-checked. It **accompanies** the three classes above — they are what a clean rebuild reports, this is what a stale restore reports instead of them — which is why a shape change wants a fresh version rather than a repack of a slot consumers already hold. |
| `no-generated-shape-change` | — | the generated declarations are unchanged, so no construction, match or reference site moves. |
| `generated-shape-unreadable` | — | the change crosses an erased slot, so whether the generated shape moved cannot be read off the artifact. Reported, never guessed. |

## The mapping

What each change class carries on each axis. Where the two columns differ, the difference is the
point.

<!-- BEGIN GENERATED (Phase 293): rendered by `Diff.mappingTable` from the classifier's descriptor table.
     Do not hand-edit — the test suite holds this section byte-equal to that string; change the rule
     in `Diff.fs` and paste the rendered table. -->
| Change | Wire severity | F# consequence | Classifier case |
|---|---|---|---|
| the artifact's own encoding version | `host-surface-only` | `no-generated-shape-change` | `ArtifactVersionChanged` |
| the declared wire shape | `breaking-wire` | `no-generated-shape-change` | `WireShapeChanged` |
| the hardening vocabulary | `host-surface-only` | `no-generated-shape-change` | `HardenPolicyChanged` |
| a node kind added | `additive` | `exhaustive-match` (a kind is a case of the generated node-kind DU) | `KindAdded` |
| a node kind removed | `breaking-wire` | `exhaustive-match` | `KindRemoved` |
| a node kind renamed (inferred, reported beside the add and the remove it explains) | `breaking-wire` | `no-generated-shape-change` — the add and the remove carry the consequence | `KindRenamed` |
| a kind's category | `host-surface-only` | `no-generated-shape-change` | `KindCategoryChanged` |
| a kind's or a tree-op's annotation set | `additive` on a first marking, `host-surface-only` otherwise | `no-generated-shape-change` — an `Obsolete` attribute moves, which changes which **warnings** a consumer sees, not a shape | `KindAnnotationsChanged` |
| a tree-op added | `additive` | `no-generated-shape-change` — the F# type emitter leaves the op vocabulary unshipped (Phase 703). **This row changes the day that leg lands.** | `OpAdded` |
| a tree-op removed | `breaking-wire` | `no-generated-shape-change` — as for a tree-op added | `OpRemoved` |
| a union added | `additive` | `no-generated-shape-change` — nothing could have referenced it | `UnionAdded` |
| a union removed | `breaking-wire` | `type-name-reference` | `UnionRemoved` |
| a union case added | `additive` | `exhaustive-match` | `UnionCaseAdded` |
| a union case removed | `breaking-wire` | `exhaustive-match` | `UnionCaseRemoved` |
| a union's type parameters moved | `host-surface-only` — the wire carries no type arguments | `type-name-reference` (the wrong arity) | `UnionParamsChanged` |
| a union's transparent case | `breaking-wire` | `no-generated-shape-change` | `UnionTransparencyChanged` |
| an enum added | `additive` | `no-generated-shape-change` | `EnumAdded` |
| an enum removed | `breaking-wire` | `type-name-reference` | `EnumRemoved` |
| an enum case added | `additive` | `exhaustive-match` | `EnumCaseAdded` |
| an enum case removed | `breaking-wire` | `exhaustive-match` | `EnumCaseRemoved` |
| an enum's **host** case names moved | `host-surface-only` | `exhaustive-match` | `EnumHostMappingChanged` |
| an enum case's annotation set | `additive` on a first marking, `host-surface-only` otherwise | `no-generated-shape-change` — an `Obsolete` attribute moves, which changes which **warnings** a consumer sees, not a shape | `EnumCaseAnnotationsChanged` |
| a record added | `additive` | `no-generated-shape-change` | `RecordAdded` |
| a record removed | `breaking-wire` | `type-name-reference` | `RecordRemoved` |
| a **required** field added | `breaking-for-emitters` | `full-literal-construction` | `FieldAdded` |
| an **optional** field added | `additive` | `full-literal-construction` | `FieldAdded` |
| a **host-only** field added | `host-surface-only` | `full-literal-construction` | `FieldAdded` |
| a field removed | `breaking-wire`; `host-surface-only` for a host-only field | `full-literal-construction` | `FieldRemoved` |
| a field's type moved | `breaking-wire` | `full-literal-construction` | `FieldTypeChanged` |
| a field's type **widened from `int` to `float`** (anywhere in it — a list element, a map value, a union argument) | `additive` | `full-literal-construction` | `FieldTypeChanged` |
| a field's type moved across an **erased** slot (`hosted` / `json` / `opaque`), at any depth — a list element, a map value, a union argument | `undecided` | `generated-shape-unreadable` | `FieldTypeChanged` |
| a field's optionality moved **into or out of** `optional` | `breaking-for-emitters` when it became required, else `breaking-wire` | `full-literal-construction` | `FieldOptionalityChanged` |
| a field's optionality moved **between** `required` and `omitDefault`, or its identity default moved | `breaking-for-emitters` when it became required (an emitter that omitted it now produces an invalid document); `breaking-wire` otherwise (omit-at-default is wire-visible) | `full-literal-construction` on a kind field with no authoring default — `mk<Kind>` takes a parameter for every required field, so the parameter leaves or arrives — else `no-generated-shape-change` | `FieldOptionalityChanged` |
| a field crossed the **host-only** boundary | `breaking-wire` | `full-literal-construction` when it crossed `optional` too, else `no-generated-shape-change` | `FieldOptionalityChanged` |
| a `fn` slot's `hostSurface` block moved | `host-surface-only` | `full-literal-construction` when its `fsharp` signature moved, else `no-generated-shape-change` | `FieldHostSurfaceChanged` |
| a **hosted** slot's `hostSurface` block moved — its host type, its `encode` or its `decode` | `undecided` | `full-literal-construction` when its `fsharp` type moved, else `no-generated-shape-change` | `FieldHostSurfaceChanged` |
| a field's annotation set | `additive` on a first marking, `host-surface-only` otherwise | `no-generated-shape-change` — an `Obsolete` attribute moves, which changes which **warnings** a consumer sees, not a shape | `FieldAnnotationsChanged` |
| a union case's annotation set | `additive` on a first marking, `host-surface-only` otherwise | `no-generated-shape-change` — an `Obsolete` attribute moves, which changes which **warnings** a consumer sees, not a shape | `UnionCaseAnnotationsChanged` |
| an authoring default added | `additive` | `full-literal-construction` when the field is required — `mk<Kind>` loses the parameter, so every call site moves — else `no-generated-shape-change` | `DefaultAdded` |
| an authoring default removed | `breaking-for-emitters` | `full-literal-construction` when the field is required — `mk<Kind>` gains the parameter — else `no-generated-shape-change` | `DefaultRemoved` |
| an authoring default changed | `breaking-for-emitters` | `no-generated-shape-change` — the parameter list is unchanged, the constructor's body is not | `DefaultChanged` |
| a declared support entry (`support.json`) added, removed or changed — a doc block, a splice, a case refine, a kind projection, the host prelude | `host-surface-only` — host-language source the generator splices, never on the wire | `full-literal-construction` for a kind projection or the type splice (generated declarations move), else `no-generated-shape-change` | `SupportChanged` |
<!-- END GENERATED -->

**The table is the classifier's own (Phase 293).** Every row above is rendered from the one
descriptor table in `Fuaran.Core.Idl.Diff` that `classify`, `consequences`, the report's sort order,
its one-line summaries and the three §11 family predicates are all projections of — so a rule
corrected once is corrected everywhere it is read, and a row this document shows cannot say
something the code does not decide. Three rows read differently from the hand-copied table that
preceded it, and in each the code was right and the copy had drifted: a union's type parameters
moving is `host-surface-only` (the wire carries no type arguments), an `omitDefault` field becoming
`required` is `breaking-for-emitters` (the emitter that omitted it is the one that breaks), and a
type change across an erased slot is `undecided` at ANY depth — the rule used to test the
top-level tag alone, so a `list` of `hosted` values reported `breaking-wire` where this document
said undecided. Two consequence rows are new rather than corrected: an authoring default added to
or removed from a **required** field moves `mk<Kind>`'s parameter list — the generated smart
constructor takes a parameter for every required field with no default — so it is a construction
break at every call site, where the axis used to read `no-generated-shape-change` for every
default change — and a kind field moving into or out of `required` with no default to stand in is the
same parameter arriving or leaving, which the property found the moment it read the constructors. The property test that holds the consequence axis to the generator's output
reads the emitter that emits the constructors (`Gen.fsharpModule`), not the type emitter alone,
which emits none.

**The two corrected rules (Phase 252).**

- **Int to float is a widening, and a widening is `additive`.** A float slot admits every integer
  literal, and a whole float renders as the same digits, so every document the old vocabulary
  admitted decodes and re-encodes byte-identically under the new one, and every emitter that
  conformed (writing integers) still conforms. That is this table's definition of `additive`, not of
  `breaking-for-emitters`, whose defining case is an old emitter's output becoming invalid. The cost
  is the one a new enum case has: host lag — a decoder that predates the widening refuses `2.5` —
  and the report says so. The rule used to read "a value that decoded no longer does", which is
  false for this one change. The narrowing (`float` to `int`) and every other retype stay
  `breaking-wire`.
- **A hosted slot's codec is its wire form, so moving it is `undecided`.** The artifact states what a
  hosted slot's codec is called, never what it writes, so a different `encode` or `decode` — or a
  different host type under the same codec names, which is a different function — can move every
  document's bytes with nothing in the artifact to show it. The rule used to call that
  `host-surface-only` (exit 0), a false absorbable; it is now `undecided` (exit 4) with the checks a
  human runs. A `fn` slot's block stays `host-surface-only`: a closure is the fixed sentinel on the
  wire whatever its signature says. On the F# axis both are decided rather than reported unreadable —
  the block states the host type outright, so the generated field moves exactly when `fsharp` does.

## The verdict class, and what a gate does with it

One word per revision pair, which is what a gate branches on.

| Class | When | Exit code |
|---|---|---|
| `unchanged` | no rows — the two revisions describe the same contract | 0 |
| `host-surface` | rows, none of them observable on the wire | 0 |
| `additive` | every wire-observable row is additive | 0 |
| `breaking` | at least one row breaks the wire, or breaks emitters | 3 |
| `undecided` | at least one row the artifact cannot decide | 4 |

`undecided` takes precedence over every other class. Reporting the decidable remainder as the
answer is how a `/v2/` event gets published as a minor.

Three codes rather than a boolean, because "I cannot tell" and "this breaks" want different
handling: collapsing them is how the erased-slot case gets treated as a break, and how the report
stops being read.

## The wire-profile bump

`Diff.bumpProfile` applies `Versioning.bump` to the classification, so the profile grammar
(`<name>@<major>.<minor>`) and the additive-vs-breaking rule stay in one place with one definition:

- a `breaking-wire` row **retires** a member, so the major moves and the minor resets — an older
  consumer negotiating the profile becomes `Foreign` and must refuse rather than mis-decode;
- `additive` and `breaking-for-emitters` rows **introduce** members, so the minor moves. The second
  reads oddly until you ask whose profile it is: every existing document still decodes, so a
  consumer is `Behind` and tolerates by the must-ignore-but-preserve rule, and the minor is the
  honest answer to *that* question. What it cannot carry is that emitters need a coordinated bump,
  which is why the verdict reports that separately instead of calling the format broken;
- `host-surface-only` rows move no profile at all;
- an **undecided** verdict yields **no profile**. Not the base profile unchanged, and not a minor:
  either would hand a caller a number to publish for a revision whose class nobody has established.

## Using the command

```
fuaran-core-idl classify <before.json> <after.json> [--manifest <manifest.json>]
                                                    [--expect <class>]
fuaran-core-idl table
```

Two calling shapes, because a gate is in one of two positions. Without `--expect` it classifies
whatever the pull brought and the caller branches on the exit code above. With `--expect <class>`
it asserts a class the author declared, exiting 0 on a match and 1 on a mismatch, printing both
sides — that is the form that belongs in a `run.ps1`, because it is the form that can go red for
the right reason.

`--manifest` names the vocabulary's own manifest, read only for its host roster (`hosts`). The roster
is that and nothing else (Phase 252): without a manifest, or with one that declares no `hosts`, no
host is obliged by name and the report says so, rather than falling back to one vocabulary's hosts
for every vocabulary. The UI tier's own §11 rows (its corpus, its veneers, its spec document) follow
its reference host `fuaran` when a roster declares it.

Everything is written to stdout, refusals included, so a gate that captures the output gets the
whole record in one stream. A refusal — bad usage, an unreadable file, an artifact that did not
parse — exits **2**, which is deliberately not one of the verdict codes: the tool reached no
verdict, and a gate must not read that as one.

The classification is **advisory**, as the classifier has been since Phase 700. Nothing here writes
a file, bumps a version or gates a build on its own account; a gate that wants to stop is the one
that reads the exit code. A classifier that applied itself would make the hand-declared
classification unfalsifiable, which is what the retroactive validation
(`docs/idl-diff-retroactive-validation.md`) depends on not being true.

## How the claims are held

The **command** is exercised as a command, over three committed fixture artifacts, by the
repository's ordinary test suite — both calling shapes, both directions of the `--expect`
assertion, and every refusal path. It shells the built assembly rather than calling its entry point
in-process, because an exit code returned by a function is not evidence about a process. The
fixtures are generated from the same declarations the in-process legs use, with a guard that fails
naming the regeneration command when a committed file and its declaration disagree — so the command
can never be certified against bytes nothing produces.

The compile claims are **compiled**, not asserted: two legs of
`tests/Fuaran.Core.Tests/IdlStabilityClassTests.fs` spawn the real compiler over the real generator
output and read its verdict, each in both directions — the perturbed consumer must fail *and* the
adapted consumer must pass — so a leg that has stopped measuring anything is visible rather than
green. Beside them, a property over every declared perturbation asserts that the consequence
reported tracks what the generator's **output** structurally exhibits (a record member moved, a DU
case moved), read off the emitted text rather than re-derived from the IDL; restating the mapping in
the assertion would certify the test against itself.
