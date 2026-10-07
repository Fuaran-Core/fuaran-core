# Releasing Fuaran.Core

This page is the operator's half of a release: what the tag sets off, the Trusted Publishing policy
the publish job logs in through, and what to do when a step is red. It deliberately does **not**
restate the release sequence. That sequence (re-stamp the corpus, pack a candidate, run the
receiving gate, write the release record, tag and push atomically) is a gate output, and it lives in
exactly one place: [`STABILITY.md`, "Versioning policy"](STABILITY.md#versioning-policy), held there
by the `Release record` test family (DECISIONS.md D123). Do that sequence first; this page starts at
its last step, the `git push --atomic origin main v<v>`.

The registry is [nuget.org](https://www.nuget.org/packages?q=Fuaran.Core), and every package
publishes from [`.github/workflows/publish-packages.yml`](.github/workflows/publish-packages.yml).

## What the tag sets off

Pushing a `v*` tag starts `publish-packages`, three jobs in a fixed order. Each one has to pass
before the next one starts.

| Job | Runner | What it refuses |
|---|---|---|
| `tag` | Linux | A run that is not on a tag, or a tag that is not `v<Version>` for the `<Version>` in `Directory.Build.props`. It takes seconds, so a mistyped tag or a branch dispatch costs nothing. |
| `proofs` | Windows | A red proof leg on the tagged commit: `./proofs/check.ps1 -Runs 3`, the same leg the `ci` workflow runs. That means a refuted model, a drifted oracle, or a red `Proofs.Ladder`, `Proofs.Coverage` or `Proofs.Oracle` family (Phase 393). |
| `publish` | Linux | A red `./verify.ps1 -Configuration Release`. After that, nothing is refused by judgement. It packs the verified output (`--no-build`, Phase 395), logs in to nuget.org, pushes the packages and then the symbol packages, and runs the **registry probe**. |

Only the `publish` job is granted `id-token: write`, the permission that lets it ask GitHub for the
token nuget.org exchanges for an API key. The ref check and the proof leg run repository code and
never see that token.

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
