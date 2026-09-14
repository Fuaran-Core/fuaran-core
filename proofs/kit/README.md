# `proofs/kit/` — the proof leg, shipped once

A repository that wants an F\* proof leg needs the same nine things every time: a pinned prover, a
script that locates or downloads it, checks each model from a cold cache, extracts each model to F\#
and diffs the result against a committed oracle, runs the host families, and measures each module
against a declared budget and a declared floor; two hand-written runtime shims the extractor's
output compiles against; a never-packed oracle project with the two settings that generated F\#
needs; a CI job; a claims ladder; and a cost declaration. Written out by hand each time, that is
four copies of one design and — the part that actually bites — **four prover pins, three of which
will be behind the day the fourth moves**, because F\* releases weekly.

This directory is that design, written once. This repository consumes it **in place**: its
`proofs/check.ps1` is a thin caller that declares what this repository has and hands it to
`check-proof-leg.ps1`. Another repository adopts it by **copying** the files listed below and
declaring each copy in its own `copies.json`, so the estate's copy registry names a copy that has
drifted from this one before anybody meets a red gate they did not cause.

**What it deliberately is not:** a package, a module, or a runtime dependency. An adopting
repository builds nothing new, references nothing new, and restores nothing new. It is files copied
by declaration and checked for drift — which is the only shape that works across repositories whose
languages, solutions and release cadences have nothing in common.

## What is here

| File | What it is | Copied? |
|---|---|---|
| `check-proof-leg.ps1` | **The engine.** Knows how to run a proof leg; knows nothing about which models a repository has. Takes the module list, the oracle host and the paths as parameters. | copy verbatim |
| `templates/check.ps1` | The thin caller. Three declarations to edit at the top; nothing below them is per-repository. | copy and edit |
| `templates/oracle.fsproj.template` | The never-packed oracle project: `--strict-indentation-`, the FS0058/FS0064/FS1182 `NoWarn`, and the compile order the shims and models need. Named `.template` so no build or glob in a host repository can pick it up. | copy, rename and edit |
| `templates/modules.json` | The cost declaration: the budget rule, the floor rule, and one worked entry. | copy and edit |
| `templates/proofs.json` | The claims ladder: the closed level set, what each level means, and the host family that holds the rows to the tree. Goes at the **repository root**, not in `proofs/`. | copy and edit |
| `templates/ci-proofs-job.yml` | The CI job, with the cache key that hashes the pin file — which is the whole mechanism by which a pin bump reaches CI with no second edit. | copy and edit |

Three more files an adopter needs are **not duplicated here**, deliberately, and live where they are
actually used:

| File | Where | Why not a copy in the kit |
|---|---|---|
| `../fstar-pin.json` | `proofs/fstar-pin.json` | This is the live pin — the one this repository's leg reads and its CI caches on. A copy of it inside the kit would be a second number in the same repository, and a second number is exactly the problem the kit exists to remove. |
| `../oracle/Prims.fs` | `proofs/oracle/Prims.fs` | The hand-written runtime floor grows as models reach for more of F\*'s library, so the live one is always the current one. A kit copy could only ever be behind it. |
| `../oracle/FStar_Pervasives_Native.fs` | `proofs/oracle/FStar_Pervasives_Native.fs` | The same. |

So the copy set is: everything in this directory, **plus** those three files from `../`. An adopter
copies all of them and declares all of them; the table above is what a `copies.json` record set
looks like written out.

## Adopting it

1. **Copy `proofs/kit/` wholesale** into `<repo>/proofs/kit/`, and the three files above into
   `<repo>/proofs/fstar-pin.json` and `<repo>/proofs/oracle/`.
2. **`templates/check.ps1` → `<repo>/proofs/check.ps1`.** Edit the three declarations: the models,
   the host families, and where the host project lives. Delete the `templates/` copy from your
   repository if you prefer — it is a starting point, not a dependency.
3. **`templates/oracle.fsproj.template` → `<repo>/proofs/oracle/<Your>.Proofs.Oracle.fsproj`**, and
   add it to your solution. Keep `IsPackable` false: nothing extracted from a model belongs in a
   shipped assembly.
4. **`templates/modules.json` → `<repo>/proofs/modules.json`.** One entry per model. Seed the
   numbers from real runs, per the rules the file carries; do not invent them, and do not seed from
   a convenient half of the data.
5. **`templates/proofs.json` → `<repo>/proofs.json`** (the repository root). Write the ladder, and
   then write the host family that holds it to your tree — the kit ships the leg that runs that
   family, not the family itself, because what a row must be held to is a property of your tree.
6. **`templates/ci-proofs-job.yml` →** your workflow. Keep the cache key hashing the pin file.
7. **Gitignore `proofs/.fstar/` and `proofs/obj/`.** The prover install and the checked-module cache
   are not source.
8. **Write your first model**, run `pwsh ./proofs/check.ps1`, and commit the oracle it extracts.
9. **Declare your copies** (below). Until you do, nothing says when this kit has moved under you.

## The pin moves in one place

The pinned release is `proofs/fstar-pin.json` in **this** repository. An adopting repository's copy
is a copy, and the copy registry is what says when it is behind:

```json
{
  "kind": "copies",
  "producer": "<your-repo>",
  "records": [
    {
      "source": "proofs/fstar-pin.json",
      "consumers": ["<workspace-relative path to YOUR copy>"],
      "check": "bytes",
      "regen": "copy it from the kit's repository"
    }
  ]
}
```

Two details of that shape are easy to get wrong, and both are the registry's, not the kit's:
`source` resolves relative to **the declaring file's own directory** (so a repository stays clonable
standalone), and each `consumers` entry resolves relative to **the workspace root** (because it
names a file in a different repository). `check` is `bytes` or `fingerprint` — `fingerprint`
normalises line endings and trailing whitespace, which is what you want for a text file two
platforms will check out differently, and `bytes` is what you want for anything where a byte is a
byte. An unrecognised value is refused rather than defaulted.

The sweep is warn-first and offline: it names a drifted copy and the command that regenerates it,
and it never edits anything.

## What this kit does not solve

A copy is still a copy. The registry tells you a copy has drifted; it does not pull the change
through, and it cannot tell you whether the drift matters. That is the trade this shape was chosen
for: it buys an adopting repository a proof leg for the cost of a `cp`, with no package, no
reference and no coupled release — and it pays for that by making the sync a named, visible act
instead of an automatic one. A repository that wants the automatic version wants a package, and a
package is a different decision with a different cost.
