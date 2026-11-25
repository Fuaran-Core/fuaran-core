- **The TypeScript backend lays out inline declarations, and the F\* target lays out an inline record** (Phase 722,
  `Fuaran.Core.Idl.Codegen`, DECISIONS.md D283, narrowing D220.5). `Gen.typescriptModule`, `typescriptModuleDerived`,
  `typescriptDeclarations`, `typescriptDeclarationsDerived` and `typescriptValue` emit a vocabulary that reaches an
  inline record, a presence-keyed union or a record-payload case; each refused it by name before. The TypeScript host
  holds the wire shape, so an inline declaration's members are the holding object's own keys: a holder's type is its
  members intersected with the declaration's, the codec reads a presence-keyed union's case from the keys present, and
  a member a case synthesises is written at its declared value, checked on decode and not returned. `FStarTarget`
  models an inline record's members as its holder's, so a kind reaching an inline record or a record-payload case is
  modelled, and `FStarTarget.partition` no longer refuses it. A vocabulary that declares no inline declaration emits
  the bytes it emitted. Still refused, each by name: a slot enumerator, a fold or a projection over an inline
  declaration in the TypeScript host; a presence-keyed union in the F\* target; an inline declaration in the
  codec-only module. A record does not take type parameters in this release.
