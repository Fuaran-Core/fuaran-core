- **An aggregate over a date, a timestamp or a bool compares the carrier** (Phase 612, `Column.fs`) — class
  **internal**: no `api/` row, answer, wire byte or refusal moves. `Min` and `Max` compare a bool column's bools, and
  a date or timestamp column's day counts, or seconds and then fractions, when every present value is in the
  canonical range, rendering only the answer; `CountDistinct` keys the same values. A column holding a day or an
  instant outside the range keeps the rendered cells, where text order and day order disagree. `Max` over a
  100,000-row millisecond timestamp column allocates 208 bytes where it allocated 59.8 MB, and `CountDistinct` over
  it 8.5 MB where 31.4 MB; measured in `benchmarks/results/2026-11-19-ultra9-386h-phase-612.md`.
