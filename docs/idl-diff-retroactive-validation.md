# `idl-diff` retroactive validation

Phase 700 asked for the classifier to be run against real vocabulary changes and its verdicts
compared with what was hand-declared at the time — "disagreements are findings to report, not test
failures to suppress". This is that run, and its findings.

Reproduce with:

```
dotnet run --project tests/Fuaran.Core.Tests -- --idl-diff <old.json> <new.json> [<manifest.json>]
```

The two artifacts are `idl.json` revisions extracted from the wire-format specification repository
(`git show <sha>:idl.json`).

## Which revisions exist — the first finding, and it changes the candidate set

**The phase's listed candidates cannot be run.** It named 707's `liveRegion` re-model, 766's
`Toggle`, 765's `Now` and 768's `Switch` widening. Only the first has an artifact pair: `idl.json`
was itself introduced by Phase 696 (`0f37604`, 2026-08-01), and 765 / 766 / 768 all predate it. There
is no committed "before" for them, and reconstructing one means checking out a `UiIdl.fs` revision
whose surrounding test project no longer builds — which would validate the classifier against a
hand-rebuilt input rather than against the published contract, defeating the exercise.

The artifact's whole history is four revisions, so the honest reading of "the last three real
vocabulary changes" is **the three deltas between them**, and each is a genuine vocabulary change
with a hand-declared classification to compare against:

| # | Delta | Change | Hand-declared |
|---|---|---|---|
| 1 | `0f37604` → `854c4cd` | Phase 707 — `accessibility.liveRegion` stops being an opaque hosted slot and becomes a declared closed set | `additive`; STABILITY.md: "**Not a breaking change to any stable surface** … **No wire-format impact — the corpus and the generated-layer pins are byte-identical either side of the change**" |
| 2 | `854c4cd` → `f795330` | Phase 703 — the `TreeOp` vocabulary published into the artifact | `additive` |
| 3 | `f795330` → `89a9379` | Phase 801 — `StaticRows` gains `sortable` + `defaultSort` | `additive` |

This is a finding in its own right rather than an inconvenience: **the artifact is the classifier's
memory, and it is one month old.** Everything before 2026-08-01 is outside what any mechanical
classifier can reach, and nothing will make it reachable retroactively. The corollary is forward-
looking — every future vocabulary change is checkable because 696 landed, and the value of that
compounds with each revision committed.

## Delta 2 — Phase 703, the op vocabulary. AGREES.

11 changes, all additive: the eleven `TreeOp` cases (`Batch`, `EditNode`, `InsertChild`, `MoveNode`,
`RemoveNode`, `ReorderChildren`, `ReplaceBinding`, `ReplaceRoot`, `UpdateProp`, `UpdateState`,
`UpdateStyle`). Verdict `stability_impact: additive`, `core@1.(x+1)` profile minor. Matches the
hand declaration.

Worth noting what the classifier did **not** do: it did not report the artifact's new `ops` key as a
change. `Artifact.json` emits `ops` only when the domain declares any, precisely so an op-free
vocabulary's artifact stays byte-identical, and the reader treats an absent `ops` as an empty one.
A diff that read the raw JSON keys would have reported an encoding change here and been wrong.

## Delta 3 — Phase 801, declarative sort intent. AGREES.

4 changes, all additive: the `SortDirection` closed set, the `DefaultSort` record, and the two
`optional` fields on `StaticRows`. Verdict `additive`, `core@1.(x+1)`. Matches.

The obligation set it emitted is the useful half. Both field additions land on a **record**, not a
kind, so the report marks the C#/VB veneer row `CHECK` rather than `MUST` and cites the tension
directly: Phase 801 itself recorded that a payload-field addition binds neither the C# `Coverage`
reflection nor the VB analyzer's `Vocabulary.cs` (both pin `NodeKind`), while `WIRE_FORMAT.md` §11
step 6 speaks of "attribute rows". The classifier does not pretend to settle that; it names it as the
one row the phase author has to decide.

## Delta 1 — Phase 707, `liveRegion`. DISAGREED, and the classifier was wrong.

The classifier's first verdict was **BREAKING (wire)** on:

```
field type changed: record Accessibility.liveRegion : hosted(Fuaran.UI.HostPrelude.LiveRegionKind) → enum LiveRegionKind
```

with the rationale "a value that decoded no longer does". The hand declaration says the opposite, and
the hand declaration is right: the wire strings were `"polite"` / `"assertive"` / `"off"` before the
change and after it, `WIRE_FORMAT.md` §3 already declared the closed set, and the corpus is
byte-identical either side. Nothing on the wire moved. What moved was the IDL's *model* of the slot —
from "opaque, the host codec handles it" to "a declared closed set" — which is the change Phase 707
existed to make.

**Why the classifier got it wrong, which is the part worth keeping.** A `THosted` slot's content is,
by explicit design, not described by the artifact: "everywhere else the JSON is carried verbatim,
because its content is the host codec's business, not the schema's" (`Idl.fs`). So the artifact
records that the slot *was* erased and *is now* a three-string enum, and contains nothing whatever
about what the erased side admitted. The set may have narrowed, widened, or stayed identical, and
`idl.json` cannot say which. The original verdict was not conservative — it was an assertion the
input does not support.

**The fix.** A fifth severity, `Unclassifiable`, for a field type change that crosses an erased slot
(`hosted` / `json` / `opaque`) on either side. It reports the change, names the erasure as the reason
it cannot decide, and states the two checks that settle it — *does every value the old side accepted
still decode, and does the corpus come back byte-identical?* The verdict line becomes `UNDECIDED`
rather than a bump recommendation, and the draft front-matter says `additive` **only if** the checks
pass. Re-run against the same delta, that is now what it prints, and applying the two checks to
Phase 707 gives `additive` — agreeing with the hand declaration by the route that actually justifies
it.

This is the finding the phase was fishing for, and it is a finding about the *classifier*, not about
707. Reporting `BREAKING` on the commonest kind of IDL improvement — replacing an erased slot with a
declared one — would have made the report noise within about three uses, and the correct reading of a
noisy classifier is that nobody reads it.

## What the three deltas say about the classifier's coverage

- **It is not vacuous.** Over three real deltas it produced 16 change rows and got 15 right first
  time, and the one it got wrong it got wrong in a specific, characterisable way rather than by
  accident.
- **The severities that matter never fired here.** None of the three deltas added a required field,
  moved an `omitDefault` value, or removed anything — the classes where a hand declaration is most
  likely to be wrong are exactly the ones with no real precedent to validate against yet. Those are
  pinned by the unit tests (`IdlDiffTests.fs`) and remain unvalidated against history until the
  history contains one. Stated rather than glossed: the retroactive validation is evidence the
  classifier is calibrated on *additive* changes, and only that.
- **The host-strand report has no retroactive check at all.** Its obligation rows are derived from
  §11 and the §11.0 roster; nothing in the artifact history records which surfaces each change
  actually touched, so "did the report name every surface 801 really had to update?" is answerable
  only by reading 801's commits. Not done here, and it is the obvious next validation if one is
  wanted.

## Standing caveat — the roster is still hand-declared

Every report above says so in its header:

```
Host roster source: declared (WIRE_FORMAT.md §11.0 — manifest.json carries no `hosts` key yet)
```

`WIRE_FORMAT.md` §11.0 names `wire-format-fixtures/manifest.json` as the intended machine-readable
mirror of the roster, "until that lands this table is authoritative". `Diff.rosterFrom` reads a
`hosts` array the moment one appears and the reports will stop saying `declared` without any caller
changing; until then the roster in `Diff.declaredRoster` is a copy of a table, with a copy's usual
half-life.
