# Releasing Fuaran.Core

This page is the operator's half of a release: what the tag sets off, the Trusted Publishing policy
the publish job logs in through, and what to do when a step is red. The release SEQUENCE is stated in
exactly one place, [`STABILITY.md`, "Versioning policy"](STABILITY.md#versioning-policy), held there by
the `Release ledger` test family (DECISIONS.md D123), and it is the CONTRACT a release honours, not a
procedure this page repeats. The release act itself is performed by tooling outside this repository,
whose preflight re-reads the packed public surfaces before it records the release. This page starts where the contract ends, at the pushed `v<v>` tag.

The registry is [nuget.org](https://www.nuget.org/packages?q=Fuaran.Core), and every package
publishes from [`.github/workflows/publish-packages.yml`](.github/workflows/publish-packages.yml).

## What the tag sets off

Pushing a `v*` tag starts `publish-packages`. The jobs run in the order below, and each one starts
only when the jobs it needs have passed: `proofs` needs `tag`; `publish`, `proofs-kit` and
`oracle-build` each need `proofs`; `oracle-smoke` needs `oracle-build`; `oracle-release` needs
`proofs-kit` and `oracle-smoke`.

| Job | Runner | What it refuses |
|---|---|---|
| `tag` | Linux | A run that is not on a tag, or a tag that is not `v<Version>` for the `<Version>` in `Directory.Build.props`. It takes seconds, so a mistyped tag or a branch dispatch costs nothing. |
| `proofs` | Windows | A red proof leg on the tagged commit: `./proofs/check.ps1 -Runs 3`, the same leg the `ci` workflow runs. That means a refuted model, a drifted oracle, or a red `Proofs.Ladder`, `Proofs.Coverage` or `Proofs.Oracle` family (Phase 393). |
| `publish` | Linux | A red `./verify.ps1 -Configuration Release`. After that, nothing is refused by judgement. It packs the verified output (`--no-build`, Phase 395), logs in to nuget.org, pushes the packages and then the symbol packages, and runs the **registry probe**. |
| `proofs-kit` | Linux | A kit asset whose sha256 is not `copies.json`'s committed `proofsKit.sha256`, an asset already attached with different bytes, and a public url serving bytes other than the committed sha (Phase 536, DECISIONS.md D206). |
| `oracle-build` | Linux | A runtime identifier the publish script does not release. It builds the four oracle binaries and their manifest ([The oracle binaries](#the-oracle-binaries)). |
| `oracle-smoke` | each binary's own OS | A binary whose sha256 is not the manifest's, a manifest with no single line for it, or a red `tests/oracle-smoke.py` run against it. |
| `oracle-release` | Linux | A build set missing any binary or the manifest; a release carrying oracle assets but no manifest; and a public url serving bytes other than the manifest's. |

Only the `publish` job is granted `id-token: write`, the permission that lets it ask GitHub for the
token nuget.org exchanges for an API key. The ref check and the proof leg run repository code and
never see that token.
Only `proofs-kit` and `oracle-release` are granted `contents: write`, the permission that attaches
release assets.

**Re-running is safe.** Both pushes use `--skip-duplicate`, so a re-run of the same tag publishes
whatever is missing and skips whatever is already there. A green push step on its own does not
tell you which ids were new, so the probe settles it.

**The registry probe** ([`.github/scripts/probe-registry.ps1`](.github/scripts/probe-registry.ps1))
works out the packable roster from the project files. It uses the same derivation the suite holds
the README package table to, and `PackageRosterTests` holds the two derivations to one set. The
probe first refuses a packed set that differs from the roster. Then it reads nuget.org's v3 flat
container (`https://api.nuget.org/v3-flatcontainer/<id>/index.json`) for every id until `<Version>`
is listed. nuget.org indexes a push asynchronously, so the probe retries for up to fifteen minutes
before it fails. When it fails it lists every id under one of two answers, and the two are never
merged:

- **NOT SERVED.** The registry answered, and that version is not in its list. Either the push did
  not land, or indexing is slower than the budget. Re-run the failed jobs. The pushes skip what is
  already there and the probe asks again.
- **COULD NOT ASK.** The request itself failed: the host was unreachable, the server returned a 5xx,
  or the body could not be read. The probe learned nothing about those ids. Re-run once the registry
  answers.

You can run the same probe from any clone, with no workspace and no credential:

```powershell
pwsh ./.github/scripts/probe-registry.ps1 -Version 0.36.0   # exit 0 only when every id is served
pwsh ./.github/scripts/probe-registry.ps1 -ListRoster       # the ids it would ask about
```

## When a job is red

- **`tag` is red.** Nothing was built. If the tag is wrong, delete it locally and on the remote,
  then tag the right commit.
- **`proofs` or `publish` is red before the push step.** Nothing reached the registry. Fix the cause
  on `main`, then move the tag to the fixed commit: delete the tag locally and on the remote, and tag
  and push again. You may move a tag only while no package carrying its version has been pushed.
  `v0.35.2` was moved this way after its first publish failed on a README stamp.
- **The push or the probe is red after packages reached the registry.** Never move the tag. nuget.org
  does not let a version be re-uploaded with different contents, so the published packages *are* that
  version. Re-run to complete a partial push. If the published contents are wrong, the fix ships as
  the next patch version.

## Exercising the release gates

A gate that has never been seen to refuse is a claim, not evidence. So each gate on the release path
is run once where it guards, before the first release that depends on it, and the runs are recorded
here. Repeat the exercise after any change to `publish-packages.yml`, to the `verify` or `proofs`
jobs of `ci.yml`, or to `proofs/kit/check-proof-leg.ps1`.

**1. The `main` run of `ci.yml`.** After the change lands, read the first `ci` run on `main`. All
four legs must be green: `verify (Debug)`, `verify (Release)`, `proofs (windows-latest)` and
`proofs (ubuntu-latest)`. If a proof leg dies with no F\* error line, re-run it before you diagnose
it. A leg that lost its prover has refuted nothing.

**2. A refused publish.** Push a disposable tag whose publish run cannot publish, and whose proof
leg must refuse it. The tag's commit sits on a throwaway branch and makes exactly these changes:

- `proofs/DagFold.fst` gains one lemma the prover must refuse, for example
  `let gate_probe_refuted (n: nat) : Lemma (n + 1 = n) = ()`. `DagFold` is the first module the leg
  checks, so the refusal comes in its first minute.
- `.github/workflows/publish-packages.yml` loses every step that could reach the registry: the
  NuGet login, both pushes and the registry probe. The `publish` job also loses `id-token: write`.
  So no outcome of the run can publish anything, whatever else goes wrong.
- `Directory.Build.props` sets `<Version>` to the tag's version, `0.0.0-gate-probe-<yyyymmdd>`.
  The `tag` job refuses any tag that is not `v<Version>`, and it has to be green for the run to
  reach the proof leg.

The run must show `tag` green, `proofs` red at the planted lemma, and `publish` skipped. Then delete
the tag and the branch on the remote. No tag-reading test family can read the probe tag while it
exists: each parses tags as `vX.Y.Z` exactly, and `GateProbeTagTests` holds that for every reader.

```powershell
$date = Get-Date -Format yyyyMMdd
git switch -c "gate-probe-$date" origin/main
# make the three changes above, then:
git commit -am "test: a disposable gate probe, never merged"
git tag "v0.0.0-gate-probe-$date"
git push origin "gate-probe-$date" "v0.0.0-gate-probe-$date"
# read the publish-packages run the tag started, record it below, then:
git push origin --delete "v0.0.0-gate-probe-$date" "gate-probe-$date"
git tag -d "v0.0.0-gate-probe-$date"
git switch main
git branch -D "gate-probe-$date"
```

**The record.** Newest first. Each exercise adds its runs here.

| When | Run | Commit | Gate | Outcome |
|---|---|---|---|---|
| 2026-10-22 | [publish 39740267949](https://github.com/Fuaran-Core/fuaran-core/actions/runs/39740267949) | `0e8943a` (tag `v0.0.0-gate-probe-20261022`, never merged) | the publish workflow, refusing, repeated after Phase 555 made the oracle manifest one line per binary | **Refused as designed.** `tag` succeeded; `proofs` failed at the planted lemma (`DagFold.gate_probe_refuted`, Error 19 at `DagFold.fst(999)`); `publish`, `proofs-kit`, `oracle-build`, `oracle-smoke` and `oracle-release` were all skipped. The probe commit's workflow carried no login, no push, no registry probe and no release write, so no outcome could publish. The tag and its branch were deleted from the remote afterwards. |
| 2026-10-21 | [publish 39620407088](https://github.com/Fuaran-Core/fuaran-core/actions/runs/39620407088) | `ac76402` (tag `v0.0.0-gate-probe-20261021-b`, never merged) | the publish workflow, refusing, repeated after Phase 482 added the `oracle-build`, `oracle-smoke` and `oracle-release` jobs | **Refused as designed.** `tag` succeeded; `proofs` failed at the planted lemma (`DagFold.gate_probe_refuted`); `proofs-kit`, `oracle-build`, `oracle-smoke`, `oracle-release` and `publish` were all skipped. The probe commit's workflow carried no login, no push, no registry probe and no release write (`proofs-kit` and `oracle-release` held `contents: read` with their attach and fetch steps removed), so no outcome could publish. The tag and its branch were deleted from the remote afterwards. |
| 2026-10-21 | [publish 39589175718](https://github.com/Fuaran-Core/fuaran-core/actions/runs/39589175718) | `a3cd5a7` (tag `v0.0.0-gate-probe-20261021`, never merged) | the publish workflow, refusing, repeated after Phase 536 added the `proofs-kit` job | **Refused as designed.** `tag` succeeded; `proofs` failed at the planted lemma (`DagFold.gate_probe_refuted`, Error 19); `publish` and `proofs-kit` were both skipped. The probe commit's workflow carried no login, no push, no registry probe and no release write (`proofs-kit` held `contents: read` with its attach steps removed), so no outcome could publish. The tag and its branch were deleted from the remote afterwards. |
| 2026-10-07 | [ci 37688313255](https://github.com/Fuaran-Core/fuaran-core/actions/runs/37688313255) | `785f9db` (`main`) | `verify (Release)`, `proofs (ubuntu-latest)`: the first run of each | **Both red, and both findings were real.** `verify (Release)`: nine families failed with `MethodAccessException`, because the F# optimiser had copied bodies that name another package's internal member into the test assembly. A Release-built consumer failed the same way. Fixed by Phase 402 (DECISIONS.md D129). `proofs (ubuntu-latest)`: the Linux leg itself worked (the archive, the prover and every model checked green), but it was refused at `WireColumn`, 16s against a 17s floor seeded on Windows. The Linux runner is faster, and there was no second writer. Since Phase 402, a floor is enforced only on the OS it was seeded on, and the kit's cache provenance check refuses a second writer directly, on every OS. |
| 2026-10-07 | [ci 37701357094](https://github.com/Fuaran-Core/fuaran-core/actions/runs/37701357094) | `fce5fca` (pull request) | all four jobs, after the Phase 402 fix | **Green.** `verify (Release)` passed, so the cross-assembly inlining guard holds in the configuration that ships. `proofs (ubuntu-latest)` passed. `proofs (windows-latest)` first died with Z3 "Killed" (a runner resource kill, not an F\* error line), and passed on re-run. |
| 2026-10-08 | [ci 37704948477](https://github.com/Fuaran-Core/fuaran-core/actions/runs/37704948477) | `fce5fca` (`main`) | `proofs (ubuntu-latest)` | **Red, and a real finding.** 3 of the 36 leg-test assertions failed in arm P (second writer). The planted cache file came from a concurrent runspace and could land after the leg had already checked the module, so the arm was sometimes green. Fixed in `6392076`: the plant runs synchronously through the leg's `-AfterInvocation` hook, and the cache state is a content hash, not a timestamp. |
| 2026-10-08 | [publish 37705005066](https://github.com/Fuaran-Core/fuaran-core/actions/runs/37705005066) | `14661e6` (tag `v0.0.0-gate-probe-20261008`, never merged) | the publish workflow, refusing | **Refused as designed.** `tag` succeeded. `proofs` failed at the one planted lemma (`DagFold.gate_probe_refuted`, Error 19 at `DagFold.fst(4821)`). `publish` was skipped. The probe commit's workflow carried no login and no push step, so no outcome could publish. The tag and its branch were deleted from the remote afterwards. |
| 2026-10-08 | [ci 37711676406](https://github.com/Fuaran-Core/fuaran-core/actions/runs/37711676406) | `6392076` (pull request) | all four jobs; `proofs (ubuntu-latest)` run three times | **Green on every run**, including three consecutive Linux leg runs over the reworked second-writer arm. |
| 2026-10-08 | [publish 37768029130](https://github.com/Fuaran-Core/fuaran-core/actions/runs/37768029130) | `f528500` (tag `v0.0.0-gate-probe-20261008-b`, never merged) | the publish workflow, refusing, repeated after Phase 399 changed `check-proof-leg.ps1` | **Refused as designed.** `tag` succeeded; `proofs` failed at the planted lemma (`DagFold.gate_probe_refuted`); `publish` was skipped. The tag and its branch were deleted from the remote afterwards. |

## The oracle binaries

`fuaran-core-oracle` is the extracted proof models behind a JSON-lines protocol
([`docs/oracle-protocol.md`](docs/oracle-protocol.md)). A host's fuzzer runs a differential against it,
whatever language the host is written in. It ships as **one self-contained, single-file binary per
runtime identifier**, attached to the `v<Version>` GitHub release beside a sha256 manifest. It is not
a package, so it adds no nuget.org id and needs no Trusted Publishing policy. The decision and its
alternatives are in DECISIONS.md D208.

| Asset | Runs on |
|---|---|
| `fuaran-core-oracle-<Version>-linux-x64` | Linux, x64 |
| `fuaran-core-oracle-<Version>-linux-arm64` | Linux, Arm64 |
| `fuaran-core-oracle-<Version>-osx-arm64` | macOS, Apple silicon |
| `fuaran-core-oracle-<Version>-win-x64.exe` | Windows, x64 |
| `fuaran-core-oracle-<Version>.sha256` | the manifest: one `<sha256>  <file>` line per binary, in `sha256sum` format |

**How it is built.** `oracle-build` runs
[`tools/Fuaran.Core.Oracle.Cli/publish-oracle.ps1`](tools/Fuaran.Core.Oracle.Cli/publish-oracle.ps1)
once per identifier, all on one Linux runner, so each asset has exactly one producer. The script is
also the way to rebuild an asset by hand:

```powershell
pwsh ./tools/Fuaran.Core.Oracle.Cli/publish-oracle.ps1 -Rid linux-x64 -OutDir ./out
```

The build is deterministic on one machine with one SDK: two publishes of the same commit give the
same bytes. It is not reproducible across SDK patch levels, because the SDK's runtime pack is part of
the bytes. So the trust anchor is the manifest the release carries, not a sha committed in this
repository the way the proof kit's is. A consumer pins the sha of the binary it adopted and verifies
it before every run (`docs/oracle-protocol.md`, "Getting the oracle").

**What refuses.** `oracle-smoke` checks the exact bytes that will be attached, each on its own OS.
It refuses a binary that does not match its manifest line, then runs `tests/oracle-smoke.py` against
it. `oracle-release` attaches the four binaries, then the manifest **last**, so a manifest on a
release means the set is complete. It ends by fetching every asset from its public url and checking
each one against the manifest.

**Re-running is safe.**
- If the manifest is already attached, the run replaces nothing. It checks the attached assets
  against the attached manifest.
- If the release carries oracle assets but no manifest, an earlier attach stopped part-way. The run
  refuses rather than mix two builds. Delete those assets, then re-run:

  ```powershell
  gh release delete-asset v<Version> fuaran-core-oracle-<Version>-linux-x64 --yes   # one per asset named in the refusal
  ```

**The first release, `v0.40.1` (publish run [39640035761](https://github.com/Fuaran-Core/fuaran-core/actions/runs/39640035761)).**
`oracle-build` and all four `oracle-smoke` legs were green, and `oracle-release` attached the four binaries and the
manifest. Its final check then refused, correctly: the attached manifest had 36 lines, not 4, because the build loop had
collected `publish-oracle.ps1`'s build output into the manifest lines. The four `sha256  name` lines it carried were
right, and each was checked against its released binary. On 2026-10-21 the manifest asset was replaced by hand with
exactly those four lines (sha256 `1e4363e08b210e40b72fe67c7de010b415ed8555bb1c8e164817268faac60f68`), and the public url
was confirmed to serve it. The build now sends that output to the log and refuses a manifest that is not four
well-formed lines before anything is attached (the `0.40.2` ledger). This was the first time the release jobs' refusals
ran against a real release, and the one that fired caught a real defect.
**The protocol version is not the package version.** A response names the protocol (`1.0`). The
oracle's own version is the release's `<Version>`, which `--version` and `oracle.describe` report.
A release whose models moved, but whose protocol did not, keeps the protocol version.

## The Trusted Publishing policy

The publish job has no long-lived API key. `NuGet/login@v1` exchanges the job's GitHub OIDC token
for a key that lasts one hour, and nuget.org grants that key only when a **Trusted Publishing
policy** matches the token's claims. As registered for this repository:

| Policy field | Value | Note |
|---|---|---|
| Policy creator (the login step's `user:` input) | `ajwillshire` | The nuget.org profile that created the policy. It names the creator, not the package owner, and it is public, not a secret. |
| Package owner | `ajwillshire` | The owner nuget.org lists for every `Fuaran.Core.*` id (checked against the registry's search index on 2026-10-07). A package id that this owner does not own cannot be pushed through this policy. |
| Repository owner | `Fuaran-Core` | The GitHub organisation in the token's `repository` claim. It is not the user, and it is not where the repository used to live. |
| Repository | `fuaran-core` | |
| Workflow file | `publish-packages.yml` | The file name only. Renaming the workflow file breaks the match. |
| Environment | *(blank)* | The job declares no environment, and a policy that names one never matches. |

A newly registered policy is **pending** until a run uses it, and nuget.org expires a pending policy
that goes unused for seven days. Register it, then publish within that window.

### The second policy: when the ids change owner

Package ownership and the policy are separate records on nuget.org. Ownership can move to a
nuget.org organisation, for example one created to mirror the `Fuaran-Core` GitHub organisation.
When it does, the policy above stops covering any id the new owner holds, and a second policy has to
be registered **under that organisation**. The second policy is the same as the first except for the
package owner: repository owner `Fuaran-Core`, repository `fuaran-core`, workflow file
`publish-packages.yml`, environment blank, and the organisation as the package owner. Register it in
the same act that adds the organisation as an owner, and before the next tag. Otherwise the next
release learns about the change from a red push.

### What a 401 at `NuGet/login` means

`NuGet/login` fails with HTTP 401, usually worded *No matching trust policy owned by user
'ajwillshire'*, when **no policy matches the token's claims**. The packages were not touched. The
causes, most likely first:

1. **The repository moved.** A transfer to another owner or organisation changes the token's
   `repository` claim immediately. GitHub's git-URL redirects do not apply to the token, so the
   policy's repository owner no longer matches. This has happened once already, when the repository
   moved to the `Fuaran-Core` organisation. Fix the policy's repository owner, or register a new
   policy.
2. **The workflow file was renamed**, or a job gained an `environment:` the policy does not name.
3. **The policy was never activated** and its seven-day window has passed.
4. **The login `user:` is not the policy's creator.**

A login that succeeds and is then **refused at the push with 403** is a different failure. The policy
matched, but its package owner does not own the id being pushed. See the second policy above.
