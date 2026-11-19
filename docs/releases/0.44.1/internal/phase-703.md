- **A host's adoption record moves when the family or the host's answers change** (Phase 703, DECISIONS.md D272;
  `docs/conformance-corpus.md`, the adoption-record section; a comment in `ApplyVectorExport.fs`) — class
  **internal**: records and documents only; no `api/` or `api/wire/` row, conformance byte, manifest, corpus file or
  corpus pin moves. A record moves at first adoption, when the family's vectors change and the host measures them,
  and when what the host's leg answers changes; never when the host's pin moves over unchanged vectors or a leg
  changes how it reports. The Rust port's eleven records naming `0.41.0` or `0.43.0` stay: each family's vectors are
  the same blob at the tag its record names and at `v0.44.0`, and a `kitVersion` older than the host's pin reads
  that the family has not changed since. `skeletonMerge` and `skeletonDiff`, restamped four times over unchanged
  vectors and an unchanged leg, are recorded as the exception and are not moved back.
