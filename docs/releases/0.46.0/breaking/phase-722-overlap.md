- **A declaration that puts one key on an object twice once its inline declarations are laid out is refused at its
  declaration** (Phase 722, `Fuaran.Core.Idl`, DECISIONS.md D285.6 and D286) — class **breaking**: no public signature
  moves, and a vocabulary that loaded is refused. An inline record's members and a presence-keyed union's case keys
  are keys of the object holding them, so a vocabulary can spell one key twice on that object, and then it has a
  value its own interpreter writes and does not read back, or cannot write at all. `Declare.errors` refuses each
  such declaration and names both bearers of the key, so the artifact reader and `Proposal.applyDelta` refuse it
  there. Two forms loaded before and are refused now. FIRST, a presence-keyed case that declares another case's key
  as a member of its own:
  `union '<U>', case '<A>': the member '<m>' is the key of case '<B>', so a value carrying it writes both keys and is read as neither case`,
  at `$.unions[i].cases[c].fields[j]`. The interpreter encoded a value of the case carrying the member to a document
  its own reading refused, `OutOfRange` at the second key. A consumer renames the member, or moves it to the case
  whose key it is: the two cases cannot both be told from one key. SECOND, in the flat node envelope, a member an
  inline declaration lays on a kind, or a presence-keyed case's key, spelled as the node id or as a node-envelope
  field, which are keys of the same object there:
  `kind '<K>': the member '<m>' is on its object twice once inline declarations are laid out: the node id and field '<f>' (inline record '<R>')`
  (or `the node-envelope field '<e>'`, or `(presence-keyed union '<U>')`), at `$.kinds[i]`. An inline record's
  member of that name had no value the encoder could write (`repeated member`), and a case key of that name was
  always present, so every node holding another case was refused on decode. A consumer renames the member or the
  key, or declares the vocabulary in the nested envelope, where the kind's object carries neither. A host-only
  member is never on the wire and is not refused in either form. A kind's own field spelled as the node id or as an
  envelope field was refused already and is refused in the same words. The fault for a member spelled twice among
  siblings, or as the discriminator, was raised already; its text now ends by naming both bearers
  (`…: field 'paging' (inline record 'Paging') and field 'pageSize'`), so a consumer that matched the whole
  sentence matches its opening words instead. Every entry of `Fuaran.Core.Idl.Codegen` that lays an inline
  declaration out asks the same check first, where its selection reaches one: `Gen.fsharpTypes`, `fsharpModule`,
  `fsharpModuleWith`, `fsharpModuleDerived`, `fsharpCodecModule`, `fsharpCodecModuleWith`, `jsonSchema`,
  `contractArgumentSchemas`, `methodArgumentsSchema`, `typescriptModule`, `typescriptModuleDerived`,
  `typescriptDeclarations`, `typescriptDeclarationsDerived`, and `FStarTarget`, which already did. So an `Idl` value built in memory that the check refuses is refused by each,
  `UnsupportedConstruct` naming the fault (`UnmodellableInFStar` from the F\* target), where the F# and schema
  entries emitted a module for it. A consumer runs `Declare.errors` over a vocabulary it builds in memory and
  corrects what it names; a vocabulary reaching no inline declaration is asked nothing and emits the bytes it
  emitted.
