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

**Since Phase 175 the kit also ships the theorems.** A leg that can check a model, handed to a
repository with nothing to check, is a leg that repository must re-model against — and fold
confluence at its own witness is exactly what three named adopters want. So the copy set now carries
the models under `../`: the generic theorems (`DagFold`, `Chain`, `WireCanon`, `WireDecode`,
`JsonParse`, `Limits`), the reference instance a domain instantiates against (`TreeOps`, `Skeleton`,
`Preservation`, `TreeDiff`), and `templates/Instance.fst.template`, which makes fold confluence at a
domain's own algebra a fill-in-the-holes act with exactly one hole that is an obligation. See
"Importing a theorem" below.

## What is here

| File | What it is | Copied? |
|---|---|---|
| `check-proof-leg.ps1` | **The engine.** Knows how to run a proof leg; knows nothing about which models a repository has. Takes the module list, the oracle host and the paths as parameters. | copy verbatim |
| `templates/check.ps1` | The thin caller. Three declarations to edit at the top; nothing below them is per-repository. | copy and edit |
| `templates/oracle.fsproj.template` | The never-packed oracle project: `--strict-indentation-`, the FS0058/FS0064/FS1182 `NoWarn`, and the compile order the shims and models need. Named `.template` so no build or glob in a host repository can pick it up. | copy, rename and edit |
| `templates/modules.json` | The cost declaration: the budget rule, the floor rule, and one worked entry. | copy and edit |
| `templates/proofs.json` | The claims ladder: the closed level set, what each level means, and the host family that holds the rows to the tree. Goes at the **repository root**, not in `proofs/`. | copy and edit |
| `templates/ci-proofs-job.yml` | The CI job, with the cache key that hashes the pin file — which is the whole mechanism by which a pin bump reaches CI with no second edit. | copy and edit |
| `templates/Instance.fst.template` | The instantiation template (Phase 175): `../Skeleton.fst` with fourteen named holes. Drop the preamble, fill the holes, and the result is a domain's fold-confluence composite; the one obligation is `{{DIAMOND}}`, a proof of `independence_diamond` at the domain's own footprint and apply. Held to `../Skeleton.fst` byte for byte by the `Proofs.Kit` family, so the template and its first instance cannot drift apart. | copy and instantiate |

The rest of what an adopter needs is **not duplicated here**, deliberately, and lives where it is
actually used:

| File | Where | Why not a copy in the kit |
|---|---|---|
| `../fstar-pin.json` | `proofs/fstar-pin.json` | This is the live pin — the one this repository's leg reads and its CI caches on. A copy of it inside the kit would be a second number in the same repository, and a second number is exactly the problem the kit exists to remove. |
| `../oracle/Prims.fs` | `proofs/oracle/Prims.fs` | The hand-written runtime floor grows as models reach for more of F\*'s library, so the live one is always the current one. A kit copy could only ever be behind it. |
| `../oracle/FStar_Pervasives_Native.fs` | `proofs/oracle/FStar_Pervasives_Native.fs` | The same. |
| `../<Model>.fst` — the ten models `copies.json` `$sources` names | `proofs/*.fst` | The models are the live ones the leg checks and the oracle is extracted from. A kit copy would be a second model beside the first, in the same repository, with nothing holding them equal — the sources ARE `proofs/*.fst`, and the registry names each one. Which theorem a model carries and which obligations it asks a domain for is on its `$sources` entry, one per file. |

So the copy set is: everything in this directory, **plus** those three files and the ten models from
`../`. An adopter copies what it needs and declares what it copies; `copies.json` `$sources` at this
repository's root is that set written out, one entry per file, and an adopter's record is the entry
with its own path filled in.

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

## Importing a theorem (Phase 175)

An adopter that wants a theorem rather than only a leg — fold confluence at its own witness, which is
what the estate's three named adopters want — does three things, and the leg above is what checks
the third. Nothing in it is a package or a reference; it is the same copy-by-declaration, with a
template where the leg had a caller.

1. **Copy by declaration.** From `$sources`, copy the generic model the theorem lives in — for fold
   confluence that is `../DagFold.fst`, which opens nothing and is the one file every instance
   needs — and the reference instance you will instantiate against, `../TreeOps.fst` and
   `../Skeleton.fst`, into `<repo>/proofs/`. Append one `records` entry per file to your
   `copies.json`, `check: fingerprint`, `regen` as the `$sources` entry says. Each model is now a
   named copy the sweep watches, so a theorem that moves here is a finding there before it is a red
   gate you did not cause.
2. **Write the instance.** `templates/Instance.fst.template` → `<repo>/proofs/<Instance>.fst`. The
   contract is the preamble's three rules: drop everything through the
   `(* ==== END OF PREAMBLE ==== *)` line, replace every `{{HOLE}}` — the same hole takes the same
   value everywhere it occurs — and leave no `{{` behind; an unfilled hole is a refusal, not a
   default. Fourteen holes: twelve identifiers, two prose blocks, and exactly **one obligation** —
   `{{DIAMOND}}`, a lemma in your domain module of type
   `unit -> Lemma (independence_diamond #op #state #rej footprint apply)`. That is the fold
   theorem's one hypothesis about the domain, and it is yours to prove: the template fixes
   everything else, and a fixed line you find you must change is a template defect to send back,
   not a fork to keep. The values that make `../Skeleton.fst`, which the `Proofs.Kit` family holds
   the template to:

   | Hole | `Skeleton.fst` | Yours |
   |---|---|---|
   | `MODULE` | `Skeleton` | the instance's module name |
   | `DOMAIN` | `TreeOps` | the module carrying your algebra, opened beside `DagFold` |
   | `OP` / `STATE` / `REJ` | `op` / `tree` / `rejection` | your operation, state and rejection types, as `DOMAIN` names them |
   | `APPLY` | `wapply` | `OP -> STATE -> outcome STATE REJ` — the GUARDED apply where your apply is only lawful on well-formed states; the guard is where an input invariant is stated rather than assumed |
   | `FOOTPRINT` | `op_fp` | `OP -> footprint` |
   | `FOLD` | `skeleton_fold` | the instance's fold; the theorems are `<FOLD>_confluence` and `<FOLD>_confluence_halt` |
   | `DIAMOND` | `op_independence_diamond` | **the obligation** — your proof of `independence_diamond` at `FOOTPRINT` and `APPLY` |
   | `PROD_APPLY` / `PROD_FOOTPRINT` / `PROD_FOLD` | `Ops.apply` / `Ops.footprint` / `FoldConfluence.foldOnce` | the production names your comments cite; prose only |
   | `HEADER` | the opening comment's interior | what your composite says, which boundaries remain and where each is stated, the licence line |
   | `TRAILER` | `batch_lanes_fold` and its comment | your own evidence after the theorem — keep a non-vacuity witness here, or nothing goes red when your alphabet is quietly narrowed |

   **What the template does not give.** The apply-engine preservation theorems (`../Preservation.fst`),
   the diff's (`../TreeDiff.fst`) and the tree algebra's own diamond (`../TreeOps.fst`) are stated
   ABOUT the skeleton-op tree algebra, not generically over a domain. A domain whose state IS that
   tree under those operations inherits all of them by copying the files and declaring the copies —
   it instantiates nothing. A domain with its own algebra models its own apply, and `{{DIAMOND}}` is
   the theorem it proves about it before the template applies; `../Preservation.fst` is the shape to
   model preservation against, not a template for it.
3. **Check.** Add `DagFold`, your domain module and the instance to `$modules` in
   `<repo>/proofs/check.ps1`, in dependency order (a model follows what it opens), one budget entry
   each in `modules.json`, `pwsh ./proofs/check.ps1`, and commit the oracle it extracts. The leg
   checks the instance from a cold cache like any other model and diffs its extraction like any
   other; nothing about it is special, which is the point.

**What your `proofs.json` rows then say.** One `proved` row per theorem the instance yields —
`<FOLD>_confluence` and `<FOLD>_confluence_halt` — with `evidence.theorem` naming it and
`evidence.model` naming your instance file. The obligation you discharged is a `proved` row at YOUR
domain, `evidence.theorem` your `DIAMOND` lemma in your domain module: this repository's ladder
carries `independence-diamond` as `assumed` / `domain-obligation` because the fold theorem is
generic, and at an instance it is what `tree-independence-diamond` is here. `lanes-apply` stays an
`assumed` / `domain-obligation` row, `dischargedBy: Conformance.concurrencyLaws`, because it is a
statement about a lane set in hand and no instance proves it. A guarded `APPLY` carries an
`assumed` / `domain-obligation` row for its input invariant, `dischargedBy: Conformance.opAlgebra`,
as `tree-algebra-well-formed-states` does here. And every `model-bridge` and `premise` row this
ladder carries about a model you copied is a row you inherit — a copy of `../Chain.fst` is a copy of
`content-id-determines-content`. The kit ships the leg that runs your ladder family; what each row is
held to is a property of your tree, and the family is yours to write.

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
byte. An unrecognised value is refused rather than defaulted. A model's record is the same shape with
`source: proofs/DagFold.fst` and `check: fingerprint`; the `$sources` entry is the record with the
`consumers` path left for you to fill.

The sweep is warn-first and offline: it names a drifted copy and the command that regenerates it,
and it never edits anything.

## What this kit does not solve

A copy is still a copy. The registry tells you a copy has drifted; it does not pull the change
through, and it cannot tell you whether the drift matters. That is the trade this shape was chosen
for: it buys an adopting repository a proof leg for the cost of a `cp`, with no package, no
reference and no coupled release — and it pays for that by making the sync a named, visible act
instead of an automatic one. A repository that wants the automatic version wants a package, and a
package is a different decision with a different cost.
