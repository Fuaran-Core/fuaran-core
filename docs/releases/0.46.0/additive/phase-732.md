- **Every backend emits a parameterised record** (Phase 732, `Fuaran.Core.Idl` and `Fuaran.Core.Idl.Codegen`,
  DECISIONS.md D287). A record declares type parameters, a member names one, and a reference applies arguments;
  `TypeParams.bindRecord` is the binding a reference makes, beside `TypeParams.bind`. `idl.json` writes a record's
  `params` and a record reference's `args` only where there is one, so every existing artifact is byte-identical, and
  `conformance/idl/idl.schema.json` states both; so does `support.json` where a kind projection's member is a record
  reference applying arguments. The two wire baselines move for that reason alone: their reach-everything vocabulary
  now declares a record parameter (every document of `api/wire/Fuaran.Core.Idl.txt` gains it) and applies an
  argument (`supportArtifact / IdlType.TRecord` in `api/wire/Fuaran.Core.Idl.Codegen.txt`). The interpreter
  (`Encode`, `Decode`, `Sample`) reads a record's members at the reference's instantiation, an inline record's from
  its holder's. The JSON schema states one `$defs` entry per instantiation on the wire, named as a union's are
  (`Ranged_float`) by the rule of the breaking entry's EIGHTH, and defines an instantiation written only in a hosted
  slot's declared wire form, whose `$ref` dangled, a generic union's at `0.45.0` too. The F# backend emits a generic
  record whose codecs take one codec per type parameter, as a generic union's do, and tests a default that is an
  applied record's literal by a match, so a generic holder's module compiles. The TypeScript backend emits one
  function per record on the same terms and a generic type in the declaration file, and intersects an inline record
  applied to arguments into its holder as its members, so a holder that reaches itself through one type-checks. The
  F\* target states one model type per instantiation (`r_ranged__flt`), with the round trip proved for a vocabulary
  that holds a parameterised record at a parameterised union case, in both wire shapes (the `parameterised-` rows of
  `proofs.json`). The stability classifier reads a record's parameters and grades them as a union's
  (`RecordParamsChanged`), and `Proposal.applyDelta` reads a record reference's arguments in a delta's field and
  holds the result to the declaration check. What each backend still refuses, by name, is D287.9 as its amendments
  replace it.
