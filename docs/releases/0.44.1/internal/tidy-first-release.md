- **A repository's first release is gated before its tag** (the `Release ledger` family's tag clauses,
  `LedgerTests.fs`, STABILITY.md's release sequence) — class **internal**: tests and documents only; no `api/` or
  `api/wire/` row, no package, wire or conformance byte moves. A clone holding no `vX.Y.Z` tag used to skip the tag
  clauses whatever its record said. It now reads the record: while every slot is headed DRAFT the clauses run over
  the empty tag set with a printed note, and while the one released slot is the standing version and carries a
  paragraph starting `**First release.**` they run over the tag the release commit is about to receive, so the record
  and the receiving-gate run are checked before the tag exists. A tagless clone whose record names any other release
  still skips by name (DECISIONS.md D168). Step 5 of the release sequence says when the marker is written.
