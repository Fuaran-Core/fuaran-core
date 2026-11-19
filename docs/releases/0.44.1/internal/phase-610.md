- **A store load finds its lines in place** (Phase 610, `Fuaran.Core.OpStream`) — `Jsonl.scanRecords` finds each line
  by `IndexOf('\n')`, a `\r` before the `\n` taken as the terminator, and copies each line once, where it rewrote the
  text with `\r\n` read as `\n` and split it. A CRLF text is no longer copied whole: over a 30 MB, 9,844-line store
  the CRLF scan allocates 182.7 MB where it allocated 243.7 MB, and gen2 falls from 0.91 to 0.19 per load; an LF text
  was never copied whole and is unchanged. The lines, their numbers and every refusal and its position are the
  split's, held against the old scan by a sweep. No API, wire byte or conformance file moves.
  ([results](../../benchmarks/results/2026-11-19-ultra9-386h-phase-610.md))
