- **A parse copies no number or literal token on .NET** (Phase 616, `Json.fs`, with the grammar's range form in
  `JsonSyntax.fs`) — class **internal**: no `api/` row, wire byte, refusal kind, sentence or position moves. The
  scanner holds a number token to the grammar where it stands in the input and reads it there, `TryParse` over a
  span and the int53 bound and float layout compared ordinally in place; `true`, `false`, `null` and the tolerant
  read's member-`null` look-ahead are compared in place too; the integer read answers a `ValueOption`. The Fable
  build keeps its one `Substring` per token. A parse of the 10,000-float corpus allocates 0.97 MB where it allocated
  1.50 MB, 10,000 integers 0.83 MB where 1.41 MB, and 10,000 literals 0.82 MB where 1.14 MB; measured in
  `benchmarks/results/2026-11-19-ultra9-386h-phase-616.md`.
