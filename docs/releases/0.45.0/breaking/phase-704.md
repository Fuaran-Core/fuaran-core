- **A vocabulary's docs reach its JSON Schema** (Phase 704, `Fuaran.Core.Idl.Codegen`) — class **breaking**
  (behavioural): no `api/` or `api/wire/` row moves, and a vocabulary that documents nothing emits byte for byte as
  before; for one that does, `Gen.jsonSchema` and `Gen.contractArgumentSchemas` (whose `$defs` are the same
  definitions) answer different bytes. A declaration's `doc` annotation is the `description` of its definition (an
  enum, a union and every instantiation of a generic one, a record, a kind, an op), ahead of its keywords; a union
  case's is the `description` of its branch; a field's is the `description` of its property, after the keywords that
  state its type, the node envelope's members included. An enum case, an inline record and a presence-keyed union have
  no object of their own and carry none; their fields' docs still reach the members they lay out. A generic union stays
  defined once per instantiation under its instance names (`Binding_float`, `Binding_list_SelectOption`), the names a
  hand-written schema already publishes for the same definitions, so no second naming rule is added. **What a consumer
  does:** one that holds a documented vocabulary's generated schema to a fresh generation regenerates it; a
  description changes no document's validity.
