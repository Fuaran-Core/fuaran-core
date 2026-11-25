- **The TypeScript backend lays out inline declarations, the F\* target models them, and the codec-only module
  prints them** (Phase 722, `Fuaran.Core.Idl.Codegen`, DECISIONS.md D283, D284 and D285, narrowing D220.5).
  `Gen.typescriptModule`, `typescriptModuleDerived`, `typescriptDeclarations`, `typescriptDeclarationsDerived` and
  `typescriptValue` emit a vocabulary that reaches an inline record, a presence-keyed union or a record-payload case;
  each refused it by name before. The TypeScript host holds the wire shape, so an inline declaration's members are
  the holding object's own keys: a holder's type is its members intersected with the declaration's, the codec reads a
  presence-keyed union's case from the keys present, a member a case synthesises is written at its declared value,
  checked on decode and not returned, and `kindFieldNames` and `opFieldNames` name those members. A slot enumerator,
  a fold and a projection read an inline declaration off its holder and answer what the F# derivation answers: a slot
  or a projection of an inline declaration is its own members rebuilt alone, a projection of a presence-keyed union
  switches on the case its keys pick, and a fold descends through a record payload and through a presence-keyed
  union that holds itself, its folder taking the union's own members. A fold through a record payload and a
  projection of one were emitted before and read a key the host object does not carry; both are corrected.
  `FStarTarget` models an inline record's members as its holder's and a presence-keyed union as one constructor of
  its holder per choice of case, the decoder asking the D220.3 reading of the holder's own object; the generated
  proofs carry the round trip of each constructor and a second theorem, that a decoder answers a constructor only
  where the reading picked its case. `FStarTarget.partition` no longer refuses a kind for reaching an inline
  declaration. `Gen.fsharpCodecModule` and `fsharpCodecModuleWith` print an inline declaration the roots reach with
  the structural module's emitter, and their strict readers walk its members on the object holding it. A vocabulary
  that declares no inline declaration emits the bytes it emitted. Still refused, each by name and each pinned by a
  test: in the codec-only module an inline declaration as a root, because a root's entries write and read one JSON
  value and it has none of its own; in the F\* target a synthesised value holding a number, because the model's
  numeric carriers are opaque and no literal spells one. A derived member is otherwise refused only where the F#
  derivation refuses it, in its words.
