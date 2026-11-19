- **Exact-decimal arithmetic reads canonical text in place** (Phase 613, `DecimalText.fs`, `Column.fs`) — class
  **internal**: no `api/` row, answer, wire byte or refusal moves. `tryCanonical` answers a text already canonical as
  itself after a scan that allocates nothing, and `isCanonical` is that scan; the sum of two canonical texts is
  written into one `char[]` laid out as its text, with no parse, pad or split and no string per digit, and `compare`
  reads two canonical texts in place. `Column.aggregate Sum` adds the admitted texts through that path directly. `Sum`
  over a 100,000-row decimal column allocates 94.6 bytes a row where it allocated 1,247.8, `DecimalText.add` 160
  bytes a call where 863, and `Mean` and `Max` over the same column 24.0 and 104.0 bytes a row where 590.9 and
  1,330.0; measured in `benchmarks/results/2026-11-19-ultra9-386h-phase-613.md`.
