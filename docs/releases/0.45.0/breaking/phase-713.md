- **An enum or a union that declares no case is refused at its declaration** (Phase 713, `Fuaran.Core.Idl` and
  `Fuaran.Core.Idl.Codegen`, DECISIONS.md D280) — class **breaking**: no public signature moves, and a vocabulary that
  loaded is refused. `Declare.errors` names each such type, `enum '<name>' declares no case, so it has no value` (or
  `union '<name>'`), at `$.enums[i].cases` or `$.unions[i].cases`, beside the other declaration faults and in the
  order they are walked, so the artifact reader and `Proposal.applyDelta` refuse it there; it used to load, emit and
  round-trip, and was refused only where something first sampled it. The contract refusal `emptyEnumeration` is
  unchanged, and a vocabulary that earns it now earns the declaration fault as well. **What breaks, and what a
  consumer does:** a vocabulary declaring a type with no case gives it a case or removes it. **Generated F# moves
  only where a member carries a support doc block and an authored annotation:** under a block that opens with `<`
  the annotation's lines are an explicit `<remarks>`, encoded, where they were text the compiler could not parse
  (FS3390) and a tool reading the comment dropped; under a text block an annotation whose doc opens with `<` is
  printed as authored, with no `<summary>` of its own. A member with only one of the two emits the bytes it did.
