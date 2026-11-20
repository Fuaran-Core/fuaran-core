- **The profile bump answers major on a decided wire break, whatever else is undecided** (Phase 710,
  `Fuaran.Core.Idl.Codegen`, DECISIONS.md D242 as amended) — class **breaking**, behavioural: no signature and no
  wire byte moves, and an existing caller receives a different answer. For a verdict holding a decided
  `breaking-wire` row beside an undecided one, `Diff.bumpProfile` and `Diff.bumpProfileUnder` (under either decode
  rule) answered `Bump.Undecided rows` and now answer `Bump.Bumped` with the major moved, which is what the
  verdict's own `ProfileAdvice` already said; the undecided rows stay in `Verdict.Undecided`. A verdict whose only
  doubt is an undecided row, or whose undecided row stands beside an emitter break or an addition, still answers
  `Bump.Undecided`. `Diff.verdictClass` (and so the CLI's exit code) and `Verdict.StabilityImpact` are unchanged.
  **What a consumer does:** a caller that read `Bump.Undecided` as "no profile can be named" for such a verdict now
  receives the major to publish; one that still wants to hold it until the undecided rows are checked reads
  `Verdict.Undecided`, or the class.
