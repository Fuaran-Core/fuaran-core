- **A decode error's path names the document as written, and every backend applies a member's aliases, an enum
  case's aliases and the four presence conditions** (Phase 723, `Fuaran.Core.Idl` and `Fuaran.Core.Idl.Codegen`,
  DECISIONS.md D288, D289, D290 and D291, amending D190.1, D190.4 and D256.5) — class **breaking**: a
  decode error's `Path` moves for a refusal under an alias, and the generated F# module of a vocabulary declaring
  an alias or a condition is emitted differently. FIRST, what breaks. A refusal inside a member the document writes
  under an ALIAS is located at the alias, where it was located at the member's canonical spelling, which the
  document does not carry: `{"source":"not a list"}` for an `options` member read under `source` is `WrongKind` at
  `[kind, source]`, where it was at `[kind, options]`, and an item under the alias `suggestions` is at
  `[kind, suggestions, 1]`. That holds for `Decode.decodeDetailed` and the strict reading
  (`Decode.decodeStrictDetailed`), and for every generated F# reader: the short-circuiting and the collecting
  decoders and the codec-only module's strict reader. A consumer that reads a decode error's `Path`, or compares it with a
  stored one, sees the alias where it saw the canonical spelling. What does not move: which spelling is read (the
  canonical one when present, then each alias in declared order, so where both are present the path names the
  canonical member), a missing member's path (its canonical key, since no spelling of it is present), a refusal at
  an enum case's alias (at its position), the decoded value, the canonical bytes, every encoder, and the key a
  collecting decoder's `Absent` hook is asked with. SECOND, the generated F# module of a vocabulary declaring an
  alias or a presence condition moves: its members are read under the spelling the object carries (`dSpelt`,
  `dMember`), and its presence tests read a member with `dFound`; the helper `dPick` that answered a member's value
  alone is gone, so a vocabulary that also reaches a presence-keyed union, whose module defined `dPick` twice and
  did not compile, now compiles. A drift guard over such a module fails until it is regenerated. A module whose
  vocabulary declares neither is byte-identical. THIRD, what widens. `Gen.typescriptModule` and
  `typescriptModuleDerived` generate a module for a vocabulary declaring an alias or a sibling condition, which
  they refused by name (`UnsupportedConstruct`), reading and writing it as the interpreter does, a refusal under
  an alias located as written; one declaring `WhenNonEmpty` alone regenerates differently and reads alike. The
  F\* target models such a vocabulary, which it refused whole: `FStarTarget.partition` refuses only the kinds
  meeting a shape the model's type cannot state (an equality over a number, an alias an object already carries as
  a fixed key, a declared default holding a member whose condition fails, a condition over a sibling that is no
  member of the constructor), each by name, and a vocabulary declaring an alias or a condition beside a
  presence-keyed union, on the node envelope or in a record's declared default where its condition holds is
  modelled. Over the UI reference vocabulary with a member alias, an enum case's alias and a presence condition
  declared at once, the TypeScript module is generated and the F\* refused set is the baseline's two kinds,
  `Stepper` and `Tabs`, each for its int default. No public type, member or signature moves: the API baselines
  of both packages are unchanged. The shared corpus's copy of `laws/canonical-form-laws.json`, which gains seven
  refusal vectors carrying `refusal`, `code` and `path`, reads stale until it is copied there.
