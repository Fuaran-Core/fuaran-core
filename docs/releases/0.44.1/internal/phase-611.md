- **A date or an instant renders into one string** (Phase 611, `TemporalText.fs`) — class **internal**: no `api/`
  row, wire byte or refusal moves. `dateText` and `instantText` write a day or an instant in the canonical range into
  one `char[]` sized to the text and build one string from it, over the civil parts the date accessors share; a day,
  a second of the day or a fraction outside the range keeps the padded text it always had, held against that form by
  new tests at every unit. `dateText` allocates 96 bytes where it allocated 180, `instantText` 128 to 171 where 364
  to 468, and a cell read of a 100,000-row date column 12.8 MB where 21.2 MB; measured in
  `benchmarks/results/2026-11-19-ultra9-386h-phase-611.md`.
