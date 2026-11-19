- **A column with no absent row allocates no mask while it is built** (Phase 608, `Column.fs`) — no package
  surface, wire byte or refusal moves. `TypeOps.fill`, the walk under `Column.ofCells` and the codec's decode,
  allocates the validity mask at the first absent row, with the rows before it present, and answers `AllValid`
  holding no array when none is absent; a timestamp column's two vectors are filled directly, without the pair
  array copied out of before. Measured in `benchmarks/results/2026-11-19-ultra9-386h-phase-608.md`: a ten-cell
  `Column.ofCells` plus `Column.aggregate` allocates 14 % less, and a null-free 100,000-row timestamp decode
  1.7 MB less, with fewer gen2 collections.
