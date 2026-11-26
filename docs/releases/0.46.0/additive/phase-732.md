- **Every backend emits a parameterised record** (Phase 732, `Fuaran.Core.Idl` and `Fuaran.Core.Idl.Codegen`,
  DECISIONS.md D287). A record declares type parameters, a member names one, and a reference applies arguments;
  `TypeParams.bindRecord` is the binding a reference makes, beside `TypeParams.bind`. `idl.json` writes a record's
  `params` and a record reference's `args` only where there is one, so every existing artifact is byte-identical, and
  `conformance/idl/idl.schema.json` states both. The interpreter (`Encode`, `Decode`, `Sample`) reads a record's
  members at the reference's instantiation, an inline record's from its holder's. The JSON schema states one `$defs`
  entry per instantiation, named as a union's are (`Ranged_float`); the F# backend emits a generic record whose codecs
  take one codec per type parameter, as a generic union's do; the TypeScript backend emits one function per record on
  the same terms and a generic type in the declaration file; and the F\* target states one model type per
  instantiation (`r_ranged__flt`), with the round trip proved for a vocabulary that holds a parameterised record at a
  parameterised union case, in both wire shapes (the `parameterised-` rows of `proofs.json`). The stability classifier
  reads a record's parameters and grades them as a union's (`RecordParamsChanged`), and `Proposal.applyDelta` reads a
  record reference's arguments in a delta's field and holds the result to the declaration check. What each backend
  still refuses, by name, is D287.9.
