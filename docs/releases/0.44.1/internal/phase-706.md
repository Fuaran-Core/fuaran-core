- **A decision number may land above a missing one while the slot is the draft** (Phase 706, the decision record,
  DECISIONS.md D275) — class **internal**: tests and documents only; no `api/` or `api/wire/` row, no package, wire
  or conformance byte moves. The `Decisions` family admits a number missing below the newest while it is above the
  newest number the last release carried, and refuses one at or below it, naming the number and what to do; the
  `Release ledger` family refuses every missing number, by number, once the standing slot is not the draft, which is
  the release commit. A clone with no release tag reads the first clause alone and says so. The live assertion now
  prints its messages, and `CONTRIBUTING.md`, the head of `DECISIONS.md` and step 4 of STABILITY.md's release
  sequence say how a number is chosen and that an abandoned one is recorded `Never-minted` before the release.
