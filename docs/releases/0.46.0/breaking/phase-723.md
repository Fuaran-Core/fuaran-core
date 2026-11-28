- **A decode error's path names the document as written, and every backend applies a member's aliases, an enum
  case's aliases and the four presence conditions** (Phase 723, `Fuaran.Core.Idl` and `Fuaran.Core.Idl.Codegen`,
  DECISIONS.md D288 to D293, amending D190.1, D190.2, D190.4 and D256.5) — class **breaking**: a
  decode error's `Path` moves for a refusal under an alias, the generated F# module of a vocabulary declaring
  an alias or a condition is emitted differently, and three declarations `0.45.0` admitted are refused. FIRST, what breaks. A refusal inside a member the document writes
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
  F\* target models such a vocabulary, which it refused whole. It asks the declaration check whole before it
  models anything, and refuses a vocabulary the check refuses at every entry, emitting no model and no proof
  script, as `the F* proof model cannot express a vocabulary the declaration check refuses (<path>: <the check's
  first fault>) (at the vocabulary)` (D293); so the refusal text of an ill-formed vocabulary moves, and a default
  spelt other than canonically, answered `UnsupportedDefault`, is answered `UnmodellableInFStar`. Of what the
  check admits, `FStarTarget.partition` refuses only the kinds
  meeting a shape the model's type cannot state (an equality over a number, a declared default holding a member
  at its own default in another spelling, a condition over a sibling that is no member of the constructor), each
  by name, and a vocabulary declaring an alias or a condition beside a
  presence-keyed union, on the node envelope or in a record's declared default where its condition holds is
  modelled. Over the UI reference vocabulary with a member alias, an enum case's alias and a presence condition
  declared at once, the TypeScript module is generated and the F\* refused set is the baseline's two kinds,
  `Stepper` and `Tabs`, each for its int default. No public type, member or signature moves: the API baselines
  of both packages are unchanged. The shared corpus's copy of `laws/canonical-form-laws.json`, which gains seven
  refusal vectors carrying `refusal`, `code` and `path` and the vector `alias-declared-order` (two aliases, the
  canonical spelling absent, read under the first alias in declared order), reads stale until it is copied there.
  A generated TypeScript module reading a member under an alias calls `dKey` (the key the object carries the
  member under), and either host's encoder refuses an equality condition naming no sibling as
  `UnsupportedConstruct`, `the presence condition of field '<f>', which names '<s>', no member of its owner`.
  FOURTH, three declarations no document of which reads canonically, which `Declare.errors` and so
  `Artifact.ofJson` admitted at `0.45.0`, are refused at the declaration, in every backend at once. An alias spelt
  as a key its object already carries from the node envelope: under the flat envelope a kind member's alias spelt
  as the node id or as a spelling of an envelope member, and an envelope member's alias spelt as the node id, the
  discriminator or a key a kind lays beside it; under the nested envelope an envelope member's alias spelt `id`
  or `kind`. It is refused at the alias, `kind 'Gate', field 'count': the alias 'id' is a key its object already
  carries (the node id), so a document holding that key would read it as this member` (the discriminator and a
  key a presence-keyed case lays were refused before, as a key on its object twice). And a declared default
  holding, at any depth, a member whose presence condition fails over the object holding it and which is not at
  its absence: `kind 'Place', field 'spot': the omit-at-default value is not canonical: it holds member 't' of
  record 'Spot', which is meaningful only while 's' is present, and there it is not, so the encoder drops it`;
  an authoring default reads `the value is not canonical`, and one at a type parameter `is not canonical at the
  instantiation <i> and at least <n> more`. And a value a presence-keyed case synthesises holding the same, refused
  at the case and by every backend laying the union out: `union 'Source', case 'ByUrl': the synthesised value is
  not canonical: it holds member 't' of record 'Spot', which is meaningful only while 's' is present, and there it
  is not, so the encoder drops it`. A consumer whose vocabulary declares one renames the alias, or writes the
  default or the synthesised value as the value its bytes read back to (the member left out). No fixture
  vocabulary the backends are certified over, committed artifact or the UI reference vocabulary declares one.
