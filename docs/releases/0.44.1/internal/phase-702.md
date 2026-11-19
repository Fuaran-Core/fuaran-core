- **Two changes that each add a release note do not conflict** (Phase 702, the release ledger, DECISIONS.md D272) —
  class **internal**: tests, the ledger's files and documents only; no `api/` or `api/wire/` row, no package, wire
  or conformance byte moves. While a slot is the standing draft each of its entries is a file of its own,
  `docs/releases/<v>/<class>/<name>.md`, holding its bullet exactly as it will print, so two changes add two files
  and merge clean in either order where two appended entries conflicted; the act that closes the slot (the release
  commit with `CORE_APPROVE_DOCS=ledger-index`, or `open-slot`'s advance) prints them into the slot's file, a section
  per class, and removes their files. The `Release ledger` family reads a draft as its close will print it and reds
  an entry in the draft's own file, an entry beside a closed slot, a stray in an entry directory, and an entry whose
  class would advance the draft, each refusal naming the file the entry goes to. This slot's entries moved into
  `0.44.1/internal/`, word for word. The limit: a merge of `main` still conflicts in `DECISIONS.md` when both sides
  add a decision; 3 of this slot's 11 earlier entries came from phases that also added one.
