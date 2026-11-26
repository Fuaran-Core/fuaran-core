- **A record declares type parameters and a record reference applies arguments, in the one existing case** (Phase 732,
  `Fuaran.Core.Idl` and `Fuaran.Core.Idl.Codegen`, DECISIONS.md D287, amending D283.6) — class **breaking**: two
  public shapes move, the classifier gains two cases and regrades how a type parameter and a type argument move, a
  vocabulary that loaded is refused, and one that emitted unchecked is refused. FIRST, the shapes. `IdlType.TRecord`
  is `TRecord of recordName: string * args: IdlType list`, as `TUnion` is: `TRecord n` becomes `TRecord(n, [])` where
  a value is built and `TRecord(n, _)` where one is matched. `IdlRecord` gains `Params: string list`, declared second,
  after `Name`: a full record literal adds `Params = []`. There is no second case beside `TRecord`, so one reference
  has one spelling. SECOND, the stability classifier's types (`Fuaran.Core.Idl.Codegen.Types`). `Snapshot` gains
  `RecordParams`, declared last, so a full literal adds it; `Change` gains `RecordParamsChanged` and
  `FieldTypeRestated`, appended last so no existing case's tag moves, and an exhaustive match over `Change` names
  both. THIRD, the classifier grades a type parameter and a type argument by what an old document does, so the class
  of some revision pairs moves, for a union as well as a record. A field whose type is only written differently (a
  parameter no member names added or removed, a declaration made generic and applied at the type it held, an argument
  changed where no member holds the parameter) was `breaking-wire` as `FieldTypeChanged` and is `host-surface-only` as
  `FieldTypeRestated`; a parameter renamed was `breaking-wire` at every member naming it and is the parameter list's
  row alone. In the other direction, two parameters exchanged under references written as before read
  `host-surface-only` and every stored document broke: the member is `FieldTypeChanged`, `breaking-wire`. A consumer
  that gates a release on the class sees the new class for such a pair, and reads the rule in
  `docs/idl-stability-classes.md`, whose table gains the two rows. A record reference's label in a report carries its
  arguments (`record Box<int>`), as a union's does. FOURTH, a reference that applies another number of type arguments
  than its declaration declares is refused by every generator entry, a union's in the sentence a record's is: `a
  reference applying union '<U>' to <n> type argument(s) where it declares <m>` (`UnsupportedConstruct`), from
  `Gen.fsharpTypes`, `fsharpModule`, `fsharpModuleDerived`, `fsharpCodecModule`, `fsharpValue`, `typescriptModule`,
  `typescriptModuleDerived`, `typescriptDeclarations`, `typescriptDeclarationsDerived`, `typescriptValue`,
  `jsonSchema`, `contractArgumentSchemas` and `FStarTarget`. `Declare.errors` refused such a vocabulary already, so
  this reaches only an `Idl` value built in memory and not checked: the F# and TypeScript entries emitted a module for
  a misapplied union reference, the JSON schema wrote a `$ref` to a definition that does not exist, and the F\* target
  refused it as `a union arity mismatch on '<U>'`, a sentence a consumer no longer meets. A consumer runs
  `Declare.errors` over a vocabulary it builds and corrects what it names. FIFTH, `Declare.errors` refuses a
  vocabulary that loaded: an omit-at-default member whose type names a type parameter, where its literal is no value
  of the member's type at an instantiation the vocabulary reaches: `record '<R>', field '<f>': the omit-at-default
  value does not fit the field's type at the instantiation <R><int> (…)` (`union '<U>' case '<C>', field '<f>'` for a
  union's case), at the literal's own path, `$.records[i].fields[j].optionality.default`. Such a declaration was
  admitted for a union's case before this release; at the named instantiation a document that leaves the member out
  read to a value no encoder writes back. A consumer changes the literal to a value of every argument the declaration
  is applied to, or makes the member `optional`, or declares the member at a fixed type. A declaration no reference
  instantiates, and one instantiated only where the literal fits, load as before. SIXTH, sentences a consumer may have
  matched. A type variable where no parameter is declared is refused as `type variable '<T>' names a type parameter,
  and a kind declares none` (an op, the node envelope, a contract's method), where it said `is not a parameter of the
  union declaring it`; and the JSON schema's refusal of a recursion that grows its type argument reads `a generic
  union or record whose instantiation walk did not close after 1000 expansions`. SEVENTH, a declaration whose
  recursion grows its type argument (`Grow<T>` holding `Grow<T list>`). At `0.45.0` none of `FStarTarget.partition`,
  `FStarTarget.vocabularyModule`, `Gen.fsharpTypes`, `fsharpModule`, `typescriptModule`, `typescriptDeclarations` and
  `jsonSchema` returned over a union with that recursion while a probe waited: the resolution each runs walked an
  unbounded reach. Every entry returns now, for a union and a record: the F# and TypeScript entries emit it, the F\*
  target refuses it by name (`the generic union '<U>', whose recursion grows its type argument`) and the JSON schema
  refuses it at its bound. Nothing a consumer had working moves. Every artifact, schema-checked document and generated
  module of a vocabulary that declares no parameterised record and misapplies no reference is byte-identical.
