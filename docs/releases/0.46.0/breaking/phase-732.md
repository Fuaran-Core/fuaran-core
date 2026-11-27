- **A record declares type parameters and a record reference applies arguments, in the one existing case** (Phase 732,
  `Fuaran.Core.Idl` and `Fuaran.Core.Idl.Codegen`, DECISIONS.md D287, amending D283.6) — class **breaking**: two
  public shapes move, the classifier gains two cases and regrades how a type parameter and a type argument move,
  vocabularies that loaded are refused, one that emitted unchecked is refused, and some JSON schema definition names
  are spelled differently. FIRST, the shapes. `IdlType.TRecord` is `TRecord of recordName: string * args: IdlType
  list`, as `TUnion` is: `TRecord n` becomes `TRecord(n, [])` where a value is built and `TRecord(n, _)` where one is
  matched. `IdlRecord` gains `Params: string list`, declared second, after `Name`: a full record literal adds
  `Params = []`. There is no second case beside `TRecord`, so one reference has one spelling. `TypeParams.substitute`
  now substitutes inside a tuple, a set and a keyed map, which it passed over unchanged. SECOND, the stability
  classifier's types (`Fuaran.Core.Idl.Codegen.Types`). `Snapshot` gains `RecordParams`, declared last, so a full
  literal adds it; `Change` gains `RecordParamsChanged` and `FieldTypeRestated`, appended last so no existing case's
  tag moves, and an exhaustive match over `Change` names both. THIRD, the classifier grades a type parameter and a
  type argument by what an old document does, so the class of some revision pairs moves, for a union as well as a
  record. A field whose type is only written differently (a parameter no member names added or removed, a
  declaration made generic and applied at the type it held, an argument changed where no member holds the parameter)
  was `breaking-wire` as `FieldTypeChanged` and is `host-surface-only` as `FieldTypeRestated`; a parameter renamed
  was `breaking-wire` at every member naming it and is the parameter list's row alone. In the other direction, two
  parameters exchanged under references written as before read `host-surface-only` and every stored document broke:
  the member is `FieldTypeChanged`, `breaking-wire`. Where either revision reaches infinitely many instantiations of
  its generic declarations, or more than 4096, no member of a generic declaration is graded `FieldTypeRestated`,
  nor is a type that itself reaches such a set: each reads `FieldTypeChanged`, as at `0.45.0`. A consumer that gates a release on the class sees the
  new class for such a pair, and reads the rule in `docs/idl-stability-classes.md`, whose table gains the two rows. A
  record reference's label in a report carries its arguments (`record Box<int>`), as a union's does. FOURTH, a
  reference that applies another number of type arguments than its declaration declares is refused by every
  generator entry, a union's in the sentence a record's is: `a reference applying union '<U>' to <n> type
  argument(s) where it declares <m>` (`UnsupportedConstruct`), from `Gen.fsharpTypes`, `fsharpModule`,
  `fsharpModuleDerived`, `fsharpCodecModule`, `fsharpCodecModuleWith`, `fsharpValue`, `typescriptModule`,
  `typescriptModuleDerived`, `typescriptDeclarations`, `typescriptDeclarationsDerived`, `typescriptValue`,
  `jsonSchema`, `contractArgumentSchemas` (so `methodArgumentsSchema`) and `FStarTarget`. `Declare.errors` refused
  such a vocabulary already, so this reaches only an `Idl` value built in memory and not checked: the F# and
  TypeScript entries emitted a module for a misapplied union reference, the JSON schema wrote a `$ref` to a
  definition that does not exist, and the F\* target refused it as `a union arity mismatch on '<U>'`, a sentence a
  consumer no longer meets. A consumer runs `Declare.errors` over a vocabulary it builds and corrects what it names.
  FIFTH, `Declare.errors` refuses vocabularies that loaded, in two rules over the instantiations of a generic
  declaration a document can hold a value of (an instantiation named only as the argument of a declaration that
  holds nothing at that parameter is not one). (a) An omit-at-default member whose type names a type parameter, where
  its literal is no value of the member's type at such an instantiation: `record '<R>', field '<f>': the
  omit-at-default value does not fit the field's type at the instantiation <R><int> and at least 0 more (…)` (`union
  '<U>' case '<C>', field '<f>'` for a union's case), at the literal's own path,
  `$.records[i].fields[j].optionality.default`. Such a declaration was admitted for a union's case before this
  release; at the named instantiation a document that leaves the member out read to a value no encoder writes back. A
  consumer changes the literal to a value of every argument the declaration is applied to, or makes the member
  `optional`, or declares the member at a fixed type. (b) A union reference written inside a generic holder at the
  holder's parameter, where an instantiation of the holder makes its transparent case's field encode to an object:
  `record '<H>', field '<f>': at the instantiation <H><Box<int>> and at least 0 more, instantiates union '<U>' so that
  its transparent case's field '<v>' can encode to an object — a bare object is indistinguishable from a tagged case`.
  Inside a generic union holder this loaded at `0.45.0`, and the encoder wrote documents the decoder refused. A
  consumer applies the holder at an argument that is no object, or tags the case.
  Each rule refuses a member once, naming the first instantiation found that it refuses at and how many more of those
  found it refuses at, always as a lower bound and always given, `0` included (`… at the instantiation <R><int> and at
  least 3 more (…)`): the check does not find every instantiation it reaches, so the count never says it is every one.
  Where the instantiations are infinitely many it judges them in classes. Each judges an
  instantiation without printing it, so how large one is decides nothing; a refusal names the instantiation to its
  first 256 nodes, `...`
  standing for the rest. Rule (b) asks only a reference written where a value of the member holds it: in `Pair<T1<T>,
  Q>`, where `Pair` holds nothing at its first parameter, `T1<T>` is asked nothing. Two counts bound the walk that
  reads each argument whole, and past either a member is judged in classes instead, never refused for the count alone.
  One is of the types ONE type parameter takes: past 16384 at a parameter a member reads, or at a parameter its
  arguments are placed at, and no other parameter's types count against it. The other is of the instantiations those
  walks read in the whole check: past 1048576, each member whose declaration the walk had read whole is judged as
  before, and the rest in classes. The walks in classes are bounded the same way, and only there is a member refused
  unjudged: past 16384 classes at a parameter it reads, at the depth it is decided at, `more than 16384 instantiations
  of record '<D>' reach record '<D>', field '<f>'; its default cannot be checked at each` (`the transparent case it
  holds cannot be checked at each` likewise), and past 1048576 instantiations read in classes with one unread that
  leads to its declaration, `record '<D>', field '<f>': the declaration check reads its budget of 1048576
  instantiations in classes and stops before it has read every one that leads to record '<D>'; its default cannot be
  checked at each`. A declaration no document can hold a value of, and one each of whose members fits its literal,
  meets no object and is decided in classes under both counts wherever the walk that reads each argument whole passes
  one, load as before, whatever the rest of the vocabulary reaches and however large its instantiations are. SIXTH,
  sentences a consumer may have matched. The FIFTH's two rules report one fault per member, naming one instantiation
  and counting the rest in one form, `and at least <n> more`, `<n>` a lower bound and `0` where no other was found,
  where they reported one per instantiation; a consumer matching a fault's instantiation reads it before `and at
  least`, and no fault of these rules reads `and at <n> more` or ends its instantiation without a count. Rule (b)
  reports none at a member that holds no value of the union it writes; a consumer that counted those faults reads the
  count. A type variable where no parameter is declared is refused as `type variable '<T>' names a type parameter,
  and a kind declares none` (an op, the node envelope, a contract's method), where it said `is not a parameter of the
  union declaring it`; a type variable in a record's field that is not the record's parameter is refused as `is not
  a parameter of the record declaring it`, where it said `of the union declaring it`; the sampler's fault for a type
  variable no instantiation binds reads `it is not bound by an enclosing union or record instantiation`, where it
  said `union instantiation`; the encoder's sentence for an authored value its type does not match, `authored value
  does not match IDL type …`, spells a type of more than 256 nodes as a refusal spells an instantiation, its first 256
  nodes with `...` for the rest (`list<list<...>>`), where it printed the type's F# form in full, as it still does at
  256 nodes or fewer; and the JSON schema's `a generic union whose instantiation walk did not close after
  1000 expansions` is gone, its refusals being the two sentences of the SEVENTH. SEVENTH, a declaration whose
  recursion grows its type argument (`Grow<T>` holding `Grow<T list>`), along one member or several. At `0.45.0` none
  of `FStarTarget.partition`, `FStarTarget.vocabularyModule`, `Gen.fsharpTypes`, `fsharpModule`, `typescriptModule`,
  `typescriptDeclarations` and `jsonSchema` returned over a union with that recursion while a probe waited: the
  resolution each runs walked an unbounded reach. Whether the instantiations are finitely many is now decided from
  the declarations, and every entry returns, for a union and a record: the F# and TypeScript entries emit it; the
  F\* target, for the kinds that reach it, and the JSON schema refuse it as `the generic union '<U>', which has no
  finite set of instantiations: <U><'T> reaches <U><list<'T>>`; a fold through it is refused as `a fold over
  '<root>' through ` and that sentence; and the declaration check admits it, asking only the FIFTH's rules. A
  vocabulary whose instantiations are finitely many and more than 4096, or that hold more than 65536 nodes between
  them each counted at its printed size, is refused by the F\* target, the JSON schema and a fold as `more than 4096
  instantiations of the vocabulary's generic declarations` or `more than 65536 nodes across the instantiations of the
  vocabulary's generic declarations`, and emitted by the F# and TypeScript entries; a vocabulary whose instantiations
  print that large (a chain of declarations each naming the next at two arguments or at a tuple of its parameter, and
  one wrapping its argument in a list, whose n declarations hold n (n + 3) / 2 nodes, from 361) was emitted by
  `0.45.0`'s schema and is refused now. Every analysis over instantiations interns their types, so how
  large an instantiation prints decides nothing of what it costs. EIGHTH, the JSON schema's definition names (`$defs` keys and the `$ref`s to them) are injective
  over the type on the wire, so four spellings move. A tuple inside an argument writes its arity (`Box_tuple_int_str`
  is `Box_tuple2_int_str`); a hosted slot that declares a wire form inside an argument writes it and its format
  (`Box_hosted` is `Box_hostedAs_str`, `Box_hostedDate_str`, `Box_hostedDateTime_str` or `Box_hostedUuid_str`; with
  no wire form it stays `hosted`); a declared name's own `_` is written `_0`, wherever that name is a key, an enum, a
  record, a union, a kind or an op of a vocabulary declaring no generic included (`my_kind` is `my_0kind`); and a
  declared name spelled as a fixed word of the rule is written after `_1` (a record `list` is `_1list`, a record
  `Node` is `_1Node`). So two instantiations that shared one definition under the old spelling (a date and a uuid
  slot in one `Box_hosted`, which stated the date's format for both; two tuples of other arities; `Pair<a_b, c>` and
  `Pair<a, b_c>`) each have their own, and an instantiation named as a plain declaration (`Ranged<float>` beside a
  record `Ranged_float`, a repeated member the schema's own parser refused) no longer is. A name holding no `_` and
  spelled as no fixed word, and every other instance name (`Ranged_float`, `0.45.0`'s `Binding_float` and
  `Binding_list_SelectOption`), is unchanged; this repository commits no generated vocabulary schema, and the three
  it pins by digest (`IdlSchemaValidatorTests`) do not move. A consumer that holds a generated schema regenerates it;
  one that writes a `$ref` by hand to a definition of a tuple, of a hosted slot with a wire form, or of a declaration
  whose name holds `_` or is a fixed word re-spells it by the rule (DECISIONS.md D287). NINTH, a seeded sample stream
  moves for a vocabulary where a reference's argument reaches a node through a declaration of the same name
  (`Box<Box<node>>`, or `Alt<node>` inside `Alt<int>`), a generic union's included: the sampler's depth floor now
  reads such an argument, and a draw that ended in `it has no finite value — every draw at the depth floor recurses`
  draws. TENTH, the F\* model's type names are injective over the declarations' names: a declared name writes each `_` of
  its own as `_0` (a record `Ranged_Int` is `r_ranged_0_int`, where it shared `Ranged<int>`'s `r_ranged__int`), and an
  instantiation joins its arguments' names with `__` (`r_entry__str_e_direction` is `r_entry__str__e_direction`); a
  name with no `_` of its own and an instantiation of one argument are unchanged (`u_slot__str`). Every top-level name the
  model and its proofs emit (each type of every kind, each constructor, each definition) passes through one table, so a
  vocabulary in which two still meet in one name (declarations of any kind whose names differ only in the case of their
  first letter, two tuples of enums, a case tag holding `__` beside an instantiation's constructor) is refused by the
  F\* target by name: two unions `FooBar` and `fooBar`, which `0.45.0` modelled as one type, are refused. A consumer that holds a generated model regenerates it.
  ELEVENTH, `TypeParams.substitute` substituting inside a tuple, a set and a keyed map (the FIRST) is a defect fix that
  moves emitted output of a vocabulary declaring no parameterised record: one that applies a generic union at one of
  those holding its parameter. `U1<T> { x: U2<tuple<T, T>> }` and on, held at `U1<str>`, stated the schema's ``
  `U2_tuple_T_T`, `U3_tuple_T_T` and on, each component the unconstrained `{}`, and was refused by the F\* target as
  `an unresolved type parameter 'T`; it now states `U2_tuple2_str_str`, `U3_tuple2_tuple2_str_str_tuple2_str_str` and on,
  each component typed, and is modelled. Its F# and TypeScript modules do not move. Every other stream, artifact,
  schema-checked document and generated module is byte-identical: that of a vocabulary which declares no
  parameterised record, misapplies no reference, applies no generic union at a tuple, a set or a keyed map of its
  parameter nor at two or more arguments, declares no name holding `_`, holds no two names the TENTH refuses, and is
  under both caps of the SEVENTH.
