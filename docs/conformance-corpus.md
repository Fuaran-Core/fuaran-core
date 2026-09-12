# The shared wire-format conformance corpus

Two suites in this repository certify against a corpus that lives in **another repository**:

| Family | What it certifies | Suite |
|---|---|---|
| `laws/transform-laws.json` | the transform-law reference vectors this kit publishes are the vectors the corpus carries, and each one is still true of this evaluator | `tests/Fuaran.Core.Tests/LawVectorTests.fs` |
| `nodes/*.json` | the vendored wire snapshots the IDL inversion gate asserts against have not drifted from the live corpus | `tests/Fuaran.Core.Tests/IdlSpikeTests.fs` |

The corpus is <https://github.com/fuaran-ui/fuaran-ui-specification>, resolved on disk under the
directory name `wire-format-fixtures`. The directory name is the interface; the repository name is
not.

## Getting it

Either of these is found with no further configuration:

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

`FUARAN_CORE_CORPUS_DIR` must point at the corpus **root** — the directory holding `manifest.json`
and the family directories — not at a family. A directory that is not the corpus is refused by name
rather than searched past: acceptance reads the `schema` and `idl` documents the corpus's own
manifest declares, so an index that merely *mentions* those families cannot pass for one.

## An absent corpus FAILS. It does not skip

This is the decision worth stating plainly, because the opposite was true until Phase 130 and it
cost a day.

Both suites used to locate the corpus by climbing upwards from wherever the test binary was running,
and to `skiptest` by name when the climb found nothing. From the repository's main working tree the
climb reached a corpus checked out alongside; from a **linked git worktree** of the same repository
it never did, because a worktree sits somewhere else entirely. So four consecutive green gate runs
had certified against a corpus none of them had read, and the first run that actually compared —
one in the main tree — failed on drift that had been accumulating invisibly.

A skip that means "this check did not run" is indistinguishable, in a green report, from a check
that ran and passed. So:

- the corpus is resolved from the repository's **main working tree**, which git answers identically
  from every worktree of a repository (`git rev-parse --git-common-dir`, whose parent is the main
  tree);
- an absent corpus **fails**, naming every path it tried and the remedy;
- the only way to skip is to ask for it.

## Asking to skip

```powershell
$env:FUARAN_CORE_SKIP_CORPUS = '1'
```

The corpus legs then skip, and each printed skip names the variable and says that nothing was
compared against the shared corpus. Use it when you are working on something unrelated and do not
want a second clone; do not propose a change whose only green run was made with it set.

## What CI does

`.github/workflows/ci.yml` checks the corpus out beside the repository checkout and sets
`FUARAN_CORE_CORPUS_DIR` to it. That is deliberate rather than incidental: it means every push
compares against the corpus at its own `main`, so drift between this kit and the published corpus
surfaces on the change that caused it instead of on somebody else's change days later.

It also means a change to what this kit renders into the corpus is not finished until the corpus
commit is pushed too. The two repositories are one change-set.
