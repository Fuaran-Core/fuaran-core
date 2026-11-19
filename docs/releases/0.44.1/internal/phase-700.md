- **A wire baseline's header is read from the tree, and an approval rewrites only what moved** (Phase 700,
  `WireSurfaceTests.fs`, DECISIONS.md D270) — class **internal**: tests and records only; no `api/` or `api/wire/`
  row or header moves. A header states its class since the newest release the tree's own release ledger records,
  never since the clone's newest tag, so one tree approves to the same bytes in every clone; a clone that does not
  hold that release's tag refuses, naming `git fetch --tags`. `CORE_APPROVE_WIRE` rewrites a baseline only when its
  rows moved or its header fails the stated-class check, which now names an absent tag as absent rather than as a
  first snapshot and does not read headers an approval run is rewriting.
