- **The TypeScript backend lays out inline declarations, and the F\* target models them** (Phase 722,
  `Fuaran.Core.Idl.Codegen`, DECISIONS.md D283 and D284, narrowing D220.5). `Gen.typescriptModule`,
  `typescriptModuleDerived`, `typescriptDeclarations`, `typescriptDeclarationsDerived` and `typescriptValue` emit a
  vocabulary that reaches an inline record, a presence-keyed union or a record-payload case; each refused it by name
  before. The TypeScript host holds the wire shape, so an inline declaration's members are the holding object's own
  keys: a holder's type is its members intersected with the declaration's, the codec reads a presence-keyed union's
  case from the keys present, a member a case synthesises is written at its declared value, checked on decode and not
  returned, and `kindFieldNames` and `opFieldNames` name those members. `FStarTarget` models an inline record's
  members as its holder's and a presence-keyed union as one constructor of its holder per choice of case, the decoder
  asking the D220.3 reading of the holder's own object; the generated proofs carry the round trip of each constructor
  and a second theorem, that a decoder answers a constructor only where the reading picked its case.
  `FStarTarget.partition` no longer refuses a kind for reaching an inline declaration. A vocabulary that declares no
  inline declaration emits the bytes it emitted. Still refused, each by name: in the TypeScript host a slot
  enumerator, a fold or a projection over an inline declaration; in the codec-only module an inline declaration; in
  the F\* target a presence-keyed union whose case declares another case's key as its own member, whose round trip
  is false, and a synthesised value holding a number.
