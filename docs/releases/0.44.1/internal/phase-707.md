- **The release sequence gates the release commit under its tag before it is pushed** (Phase 707, the release
  sequence, DECISIONS.md D277) — class **internal**: documents and one remedy sentence only; no `api/` or `api/wire/`
  row, no package, wire or conformance byte moves. Step 5 of STABILITY.md's release sequence now tags the release
  commit locally and runs `verify.ps1` on it, and the new step 6 pushes the commit and the tag together only when that
  run is green; a red run is fixed by amending the release commit and moving the tag, which has not left the clone.
  `RELEASING.md` says what whoever performs the release does before the push, and `CONTRIBUTING.md` and the `Release
  ledger` family's remedy sentence say the same order. The reason is measured in the entry: a release commit over a
  record with one decision number missing is refused by two families, and only under its tag.
