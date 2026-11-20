- **A per-kind table declared in the support record, emitted as a total lookup** (Phase 689,
  `Fuaran.Core.Idl.Codegen`, DECISIONS.md D278) — class **breaking**: `record-widening` on `GenSupport` and
  `union-widening` on `Derivation`; no wire byte moves, and a vocabulary and support document that declare no table
  emit byte for byte as before. `KindTable` is new, a union whose case is the column (`Text`, `Bool`,
  `OptionalField`, `Texts` or `Enum`), with `GenSupport.KindTables` (table name to `KindTable`) and
  `Derivation.KindTable <name>` beside it. The F# module prints each requested table in a `KindTables` module as one
  exhaustive match over its kinds, with no wildcard arm. Generation refuses a missing, unknown or duplicate row,
  naming the table and the kind; the TypeScript backend refuses the request by name. `support.json` reads and writes
  the tables as `kindTables`, absent when empty, at encoding version 1. **What breaks, and what a consumer adds:**
  `GenSupport` gained a field, so a full-literal construction of it fails with FS0764 until it adds
  `KindTables = Map.empty` (one built as `{ GenSupport.Empty with … }` compiles unchanged); `Derivation` gained a
  case, so an exhaustive `match` over it is incomplete until it adds a `Derivation.KindTable _` arm.
