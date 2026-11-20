- **`RuleFamily` is opaque behind constructors, and per-node families share one walk** (Phase 709,
  `Fuaran.Core.Validator`, DECISIONS.md D279) — class **breaking**: the surface guard reads `removal` (the record's
  constructor and its `Id` and `Run` fields) beside two additions, `Validator.wholeTree` and the `Id` property. No
  finding moves: `Validator.runAll` and `runAllTagged` give the same findings in the same order (family order, then
  preorder) and the same one `RULE-FAULT` per throwing family, and they now run every per-node family of a registry
  over one preorder of the tree (at 5,021 nodes and 30 families, 4.8 MB a call where it was 18.9 MB;
  `benchmarks/results/2026-11-20-ultra9-386h-phase-709.md`). The stock `containment`, `referenceIntegrity` and
  `referenceOrder` families are built with `wholeTree` and find what they found. **What breaks, and what a consumer
  writes instead:** a record literal `{ Id = i; Run = f }` becomes `Validator.wholeTree i f`, and a read
  `family.Run w root` becomes `(Validator.asCheck w family).Run root`; `Validator.perNode` and `family.Id` are
  unchanged. `docs/ADOPTION.md` (section 3b) shows the two forms side by side. A later family shape is a new
  constructor, so it will not break a consumer's source again.
