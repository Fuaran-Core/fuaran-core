- **The TypeScript backend lays out inline declarations, the F\* target models them, and the codec-only module
  prints them** (Phase 722, `Fuaran.Core.Idl.Codegen`, DECISIONS.md D283 to D286, narrowing D220.5).
  `Gen.typescriptModule`, `typescriptModuleDerived`, `typescriptDeclarations`, `typescriptDeclarationsDerived` and
  `typescriptValue` emit a vocabulary that reaches an inline record, a presence-keyed union or a record-payload case;
  each refused it by name before. The TypeScript host holds the wire shape, so an inline declaration's members are
  the holding object's own keys: a holder's type is its members intersected with the declaration's, the codec reads a
  presence-keyed union's case from the keys present, a member a case synthesises is written at its declared value
  in the vocabulary's key order, compared by value on decode and not returned, a required presence-keyed union
  holding no case is refused on encode in the interpreter's words, a default record lays an inline member's
  defaults on the holder, and `kindFieldNames` and `opFieldNames` name those members, a host-only one wherever it
  is declared. A slot enumerator,
  a fold and a projection read an inline declaration off its holder and answer what the F# derivation answers: a slot
  or a projection of an inline declaration is its own members rebuilt alone, a projection of a presence-keyed union
  switches on the case its keys pick, and a fold descends through a record payload and through a presence-keyed
  union that holds itself, its folder taking the union's own members.
  `FStarTarget` models an inline record's members as its holder's and a presence-keyed union as one constructor of
  its holder per choice of case, the decoder asking the D220.3 reading of the holder's own object; the generated
  proofs carry the round trip of each constructor. `FStarTarget.vectorsModuleWith` and the `DecodeVectors` record
  are new: beside `vectorsModule`'s encode facts it emits decode facts, the model's decoder evaluated by the
  normaliser on documents the interpreter reads and on documents it refuses, which is what holds the model's
  reading of a document to the interpreter's. `FStarTarget.partition` no longer refuses a kind for reaching an
  inline declaration, and `FStarTarget.proofsModule` over an empty kind selection answers `UnmodellableInFStar`
  where it threw. `Gen.fsharpCodecModule` and `fsharpCodecModuleWith` print an inline declaration the roots reach with
  the structural module's emitter, and their strict readers walk its members on the object holding it. A vocabulary
  that declares no inline declaration emits the bytes it emitted. Still refused, each by name and each pinned by a
  test: in the codec-only module an inline declaration as a root, because a root's entries write and read one JSON
  value and it has none of its own; in the F\* target a synthesised value holding a number, because the model's
  numeric carriers are opaque and no literal spells one. A derived member is otherwise refused only where the F#
  derivation refuses it, in its words.
