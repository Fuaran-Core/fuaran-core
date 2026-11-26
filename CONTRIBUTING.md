# Contributing to Fuaran.Core

Thanks for your interest. Fuaran.Core is the cross-domain substrate for the Fuaran family — a
library of **generic functions over domain-witness records**, FSharp.Core-only and Fable-clean on
both the encode and decode paths. It has no base node type and no domain evaluator; that constraint
is the whole design, not an accident.

## Building and testing

Prerequisites. The first three are needed by every contributor; the rest are needed only by the legs
named beside them, and a leg whose prerequisite is absent is **skipped by name** in the Expecto summary
(never silently passed), except where the table says it fails.

| Prerequisite | Needed for | Without it |
|---|---|---|
| The .NET SDK pinned in [`global.json`](global.json) | everything | nothing builds |
| PowerShell 7 (`pwsh`) | `run.ps1`, `verify.ps1`, `proofs/check.ps1` | the launchers do not run |
| A **full clone with tags** (`git clone`, then `git fetch --tags` in a shallow or tag-less one; CI uses `fetch-depth: 0`) | the legs that read the release tags in HEAD's history (the `Release ledger` family's tag clauses, the README's per-release claims, the public and wire since-tag class reports) | those legs **skip by name**, all of them: a clone holding no `vX.Y.Z` tag (a fork, or a clone made without tags) reads as "no released tag", which says nothing about your change. A clone that holds release tags while HEAD's history contains none of them **fails** the `Release ledger` family's release-record and draft-sentence checks, because they would otherwise pass vacuously. No checkout of this repository's history can be in that state, so it means the tag reader is broken |
| `git` on `PATH` | the working-copy line-ending and main-tree anchoring legs | those legs skip |
| `node` on `PATH`, and `FUARAN_CORE_TSC` naming a TypeScript compiler | the executed TypeScript legs of the IDL suites (round trip, byte identity, type-check) | those legs skip locally. CI installs a pinned compiler and runs them on every push, so the type-check evidence is the CI run (STABILITY.md) |
| The shared conformance corpus, **pinned** by [`copies.json`](copies.json) (`corpus.sha`) | the copy-freshness legs and the proof-oracle differentials that read the corpus's fixtures | unasked: the legs report NOT CHECKED or skip. Asked for (`FUARAN_CORE_CORPUS_FRESHNESS`, as CI does): an absent or off-pin corpus **fails** |
| Windows or Linux, or a matching F\* release named by `FSTAR_HOME` (about 200 MB downloaded on the first `-Proofs` run into `proofs/.fstar/`) | `./verify.ps1 -Proofs` and `pwsh ./proofs/check.ps1` | not part of the default gate; macOS has no pinned prover and must set `FSTAR_HOME` |

The skipped legs are counted and named on the gate's last line, so a green run says how much of the
suite it did not read.

An older clone may hold `src/Fuaran.Core.Lease/`, `src/Fuaran.Core.DataFrame/`, `src/Fuaran.Core.Column.Ops/`,
`src/Fuaran.Core.DataFrame.CSharp/`, `src/Fuaran.Core.DataFrame.Conformance/`, `tests/fable-smoke/` and
`tests/Fuaran.Core.DataFrame.CSharp.Proof/` with nothing in them but ignored `bin/` and `obj/` output: the
residue of strands that left this repository. No tracked file lives there, and `git clean -X` removes them
safely (it removes every ignored file, the prover cache under `proofs/.fstar/` included).

Every leg that reads tags reads the release tags **in HEAD's history** (`git tag --list --merged HEAD`),
through one reader. So a baseline-class report on a branch cut before the newest tag says "since the
newest tag in this branch's history", not "since the newest tag anywhere". A tag your branch does not
contain cannot be a baseline for it (DECISIONS.md D123). Once a release slot's heading is flipped to
released, its tag must already exist in the history being gated, so the one green order at a release
is tag, then gate, then push the commit and the tag together.

```powershell
./run.ps1        # restore tools, format, build, test, sample
./verify.ps1     # format-check + build + test + sample (the green gate)
```

A change is ready to propose when `./verify.ps1` is green.

Both launchers take `-Configuration Debug|Release` (default `Debug`, so the commands above are unchanged).
The publish workflow verifies in `Release` and packs from that same output, so the surface and
documentation checks read the assemblies that ship; the release sequence is stated once, as the contract,
in [`STABILITY.md`, "Versioning policy"](STABILITY.md#versioning-policy). CI runs both configurations; run
`./verify.ps1 -Configuration Release` locally when a change could behave differently under the optimiser.

Two of the suites certify against a conformance corpus that lives in a separate repository, at the
commit [`copies.json`](copies.json) pins (Phase 394; `docs/conformance-corpus.md`, "Bumping the pin").
Where the corpus is not asked for and not present, the legs say NOT CHECKED or skip by name; once it is
asked for, an absent corpus **fails** the gate rather than skipping it, because a skipped comparison is
indistinguishable from a passing one in a green report.
[`docs/conformance-corpus.md`](docs/conformance-corpus.md) has the one-line clone command and the
variable that names a clone kept elsewhere.

## Regenerating a committed artefact

Several files are projections the suite holds to what the code renders today, and
[`tests/Fuaran.Core.Tests/projections.json`](tests/Fuaran.Core.Tests/projections.json) lists every one:
the tool's family that writes it (`dotnet run --project tools/Fuaran.Core.Tools -- render <family>`;
the test runner has no verb), the environment switch that rewrites it from inside the suite,
the family that holds it, and the copies other repositories carry. A new generated artefact is one row
there. The switches are read by ONE rule (`tests/Fuaran.Core.Tests.Fixtures/Approval.fs`), and the table below is
rendered from the registry (`CORE_APPROVE_DOCS=CONTRIBUTING` rewrites it):

| Switch | Governs | A filter value is |
|---|---|---|
| `CORE_APPROVE_API` | `api/<package>.txt` | a package id |
| `CORE_APPROVE_WIRE` | `api/wire/<package>.txt` | a package id |
| `CORE_APPROVE_README` | `README.md` | `README` |
| `CORE_APPROVE_DOCS` | `DECISIONS.md`, `docs/releases/README.md`, `.fantomasignore`, `version-derives.json`, `copies.json`, `CONTRIBUTING.md`, `conformance/idl/idl.schema.json` | `DECISIONS`, `ledger-index`, `fantomasignore`, `version-derives`, `copies`, `CONTRIBUTING` or `idl-schema` |
| `FUARAN_REGEN` | `tests/Fuaran.Core.Tests/MiniGenerated.fs`, `tests/Fuaran.Core.Tests/DocGenerated.fs`, `tests/Fuaran.Core.Tests/DocAnnotatedGenerated.fs`, `tests/Fuaran.Core.Tests/DeriveGenerated.fs`, `tests/Fuaran.Core.Tests/SpecDecodersGenerated.fs`, `tests/Fuaran.Core.Tests/LayoutGenerated.fs`, `tests/Fuaran.Core.Tests/LayoutDerivedGenerated.fs`, `tests/Fuaran.Core.Tests/LayoutCodecGenerated.fs`, `tests/Fuaran.Core.Tests/ParameterisedRecordsGenerated.fs`, `tests/Fuaran.Core.Tests/ParameterisedRecordsFlatGenerated.fs`, `tests/Fuaran.Core.Tests/ParameterisedRecordsDerivedGenerated.fs`, `tests/Fuaran.Core.Tests/ParameterisedRecordsCodecGenerated.fs`, `tests/Fuaran.Core.Tests/ParameterisedRecordsDefaultsGenerated.fs`, `src/Fuaran.Core.Function/CapabilitySeamCodec.fs`, `src/Fuaran.Core.Function/PipelineSeamCodec.fs`, `src/Fuaran.Core.Query/QuerySeamCodec.fs`, `src/Fuaran.Core.Column/FieldChangeSeamCodec.fs`, `src/Fuaran.Core.AiSurface/RejectionSeamCodec.fs`, `tests/Fuaran.Core.Tests/snapshots/spike.json` | `MiniGenerated`, `DocGenerated`, `DocAnnotatedGenerated`, `DeriveGenerated`, `SpecDecodersGenerated`, `LayoutGenerated`, `LayoutDerivedGenerated`, `LayoutCodecGenerated`, `ParameterisedRecordsGenerated`, `ParameterisedRecordsFlatGenerated`, `ParameterisedRecordsDerivedGenerated`, `ParameterisedRecordsCodecGenerated`, `ParameterisedRecordsDefaultsGenerated`, `CapabilitySeamCodec`, `PipelineSeamCodec`, `QuerySeamCodec`, `FieldChangeSeamCodec`, `RejectionSeamCodec` or `spike` |

Absent, empty, `0` or `false` is no. `1` or `true` rewrites every file the switch governs. Any other
value is a filter, a comma-separated list: `CORE_APPROVE_API=Fuaran.Core.Wire` rewrites that one
baseline. A filter that matches nothing fails by name. Each file the run rewrites is printed
(`CORE_APPROVE_API: wrote <path>`), so stage exactly that list, by name.

```powershell
$env:CORE_APPROVE_WIRE = 'Fuaran.Core.Query'
dotnet run --project tests/Fuaran.Core.Tests --no-build -- --filter 'wire'
Remove-Item Env:CORE_APPROVE_WIRE
```

## Recording a decision

A ruling that changes what the code promises, or declines a change someone will propose again, is an
entry in [`DECISIONS.md`](DECISIONS.md). An entry is a heading, `## <yyyy-mm-dd> — D<n>: <title>`,
placed newest first; directly under it, after one blank line, a status line, which is mandatory:
`**Status:** Live` for a new ruling. The clauses (`Live`, `Amended-by D<n>`, `Superseded-by D<n>`,
`Moved-to-compute`, `Never-minted`) and how they combine are stated at the head of the file. An entry that
changes an earlier one also changes that entry's status line, in the same commit: the earlier ruling reads
`Amended-by D<n>` or `Superseded-by D<n>` at its own heading, so nobody reads it as current.

The number is the one held for the change before its work started, or else the next one after the newest. A held
number may land above a number that is still missing, while the standing release slot is the draft and the
missing number is above the newest the last release carried (DECISIONS.md D275). A held number below an entry the
change would amend or supersede cannot be used for that: the change takes a number above that entry, and the held
one is recorded `Never-minted`. A number that is skipped, or held for a change that is given up, is recorded
`Never-minted`, never reused: an entry `## <date> — D<n>: never minted` with `**Status:** Never-minted` and one
sentence saying why, dated the day it is recorded, as D6 is. Record it before the release: the commit that closes
the slot refuses every missing number. Then regenerate the index and stage the file:

```powershell
$env:CORE_APPROVE_DOCS = 'DECISIONS'
dotnet run --project tests/Fuaran.Core.Tests --no-build -- --filter 'Decisions'
Remove-Item Env:CORE_APPROVE_DOCS
```

`DecisionsTests` fails by name on a heading with no status line, a clause outside the grammar, a status
naming an entry that is not newer, a repeated number, a missing number at or below the newest the last
release carried, and an index that is not the render of the headings. The `Release ledger` family fails
by name on every missing number once the standing slot is not the draft. Each refusal names the number
and what to do: record it `Never-minted`, or land the change that holds it.

## Coding standards

- **F# formatting is Fantomas.** Run `./run.ps1` (or `dotnet fantomas src tests tools samples`) before every
  commit; `./verify.ps1` fails on unformatted code.
- **Totality — no exceptions in the public surface.** Failures are typed values (`Result`, a
  `Rejection`, or a named `*Error` envelope), never a thrown exception. A recoverable envelope must
  *name the failure and enumerate the valid alternatives*.
- **FSharp.Core only, Fable-clean.** No `System.Text.Json`, no host or native dependency. Every
  public surface must compile under both .NET and Fable — the Fable consumer's compile gate enforces it, and
  `fable-exclusions.json` records any package deliberately off that surface (STABILITY.md
  "Fable cleanliness").
- **A bare phase number names this repository's own roadmap.** `Phase N` in a comment, `STABILITY.md` or a document
  cites this repository's phase N. A phase from another roadmap carries that roadmap's prefix: `fuaran#N` for the UI
  language's roadmap, where this repository's first phases were tracked and whose numbers now name different work
  here. Decision records and release notes keep the citations they were written with.

## The design invariants — please read before a non-trivial change

[`STABILITY.md`](STABILITY.md) is the contract. In particular:

- **No base node type.** Core never sees a concrete `NodeKind`; it operates through witness records.
  Introducing a concrete node/kind type into a Core package is a breaking architectural regression,
  not a feature.
- **No new witness field.** The public witness records are frozen in shape. A generic function that
  needs a capability the witnesses don't expose takes it as a **per-call function parameter**, never
  a new witness field.
- **No domain evaluator in Core.** Render / recompute / regenerate / reflow stay on the domain side.

A change that adds a public surface should add or extend a `Conformance` law that certifies it, and
update `STABILITY.md` when it touches a stability-critical surface. What each version changed is
recorded in the release ledger, `docs/releases/<version>.md` (`STABILITY.md`, "Versioning policy"). A change
that ships adds its entry as a file of its own beside the standing draft's file, never inside it, so two changes in
flight never edit one file (DECISIONS.md D274):

- the path is `docs/releases/<version>/<class>/<name>.md`, where `<version>` is the standing `<Version>`;
- `<class>` is `breaking`, `additive` or `internal`, the change's class;
- `<name>` is `phase-<n>` for a phase's entry (`phase-<n>-<word>` for its second), or `pr-<n>` for a pull request;
- the file holds the entry's bullet and its wrapped lines exactly as the release will print them, and nothing else.

The draft's own file says the same in one sentence. The act that closes the slot (the release commit, or
`open-slot`'s advance) prints the entries into it, a section per class, and removes their files. The `Release
ledger` family reds an entry written into the draft's own file, an entry beside a closed slot, anything else in an
entry directory, and an entry whose class would advance the draft; each refusal names the file the entry goes to.

## Licensing of contributions

There is no sign-off requirement. The licence basis is Apache-2.0's own inbound=outbound rule: under
section 5 of the [licence](LICENSE), a contribution you intentionally submit for inclusion is licensed
under the Apache License, Version 2.0, without any additional terms or conditions, unless you state
otherwise. Nothing beyond that is asked of a commit (DECISIONS.md D207).

## Pull requests

Keep PRs focused and describe what changed and why. Make sure `./verify.ps1` passes.
