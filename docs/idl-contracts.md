# The IDL's contracts

Phase 644 (DECISIONS.md D260) added a top-level member to the IDL: a named **contract** with **methods**. A
method is declared once and carries everything that is true of it wherever it is called from. This page is the
specification every host implements. `Contract` and `ContractProjection` are the reference's code. The document
space, which the projection rests on, is held by the law vectors in
[`conformance/laws/capability-document-laws.json`](../conformance/laws/capability-document-laws.json).

The IDL states what a method IS. A client, a server table, a protocol, a route, an envelope and a tool executor
are a runtime's, built on this declaration and never part of it.

## The document

`idl.json` gains one optional top-level member, `contracts`: an array of contracts, sorted by name. It is
omitted when the vocabulary declares none, so a vocabulary without a contract writes the bytes it wrote before.

A **contract** is an object:

| Member | Value | Written |
|---|---|---|
| `name` | an identifier, unique among the vocabulary's contracts | always |
| `methods` | an array of methods, sorted by `id` | always |
| `doc` | what the contract is for, for a person and for a model | when declared |

A **method** is an object. A member with no default is always written, and a document without it is refused.

| Member | Value | Default (omitted at it) |
|---|---|---|
| `id` | an identifier, unique in the contract | none |
| `arguments` | an array of arguments, in declared order | none |
| `result` | a type object, or `{"$type":"nothing"}` for a method that answers nothing | none |
| `shape` | `"call"` (one request, one answer), `"stream"` (the result any number of times, in order) or `"job"` (an acknowledgement at once, the result when the work ends) | `"call"` |
| `effect` | `{"host": "pure" \| "readsHost" \| "writesHost", "determinism": …}`, the effect class as a capability declaration writes it | none |
| `destination` | `true` when a call hands what it is given to a party beyond the host that runs it | `false` |
| `readPolicy` | `{"$type":"unstated"}`, `{"$type":"cached","seconds":n}` or `{"$type":"invalidates","methods":[…]}` | unstated |
| `idempotent` | `true` when calling it twice with the same arguments has the effect of calling it once | `false` |
| `authority` | a non-empty array of strings, sorted | none |
| `record` | `{"arguments": bool, "result": bool}`: what a host may keep of a call | neither |
| `auditKind` | a string | absent |
| `rateLimit` | `{"calls": n, "seconds": n}`: at most `calls` calls in any window of `seconds` | absent |
| `serves` | a non-empty array of positive integers, ascending: the schema versions the method serves | none |
| `doc` | what the method does | absent |

An **argument** is an object: `name` (an identifier, unique in the method), `type` (a type object, as a field's),
`optionality` (as a field's, and always written, as a field's is), `constraint` (optional) and `doc` (optional). An argument
declared `optional` may be left out and its absence is information; one declared `omitDefault` may be left out
and the declared default is its value then.

A **constraint** bounds a scalar argument: `{"$type":"intBetween","min":a,"max":b}` on an `int`,
`{"$type":"floatBetween","min":a,"max":b}` on a `float` (both bounds finite), and
`{"$type":"lengthBetween","min":a,"max":b}` on a `string` (UTF-16 code units). Every bound is inclusive.

## Closed vocabularies, and the host's values

`shape`, `readPolicy`, `idempotent`, `constraint` and `record` are closed vocabularies of the IDL: every host
reads the same cases, and a document carrying another is refused.

`authority` and `auditKind` are the HOST's. The IDL carries the strings and reads none of them: it names no
role and no claim. The one reading it gives `authority` is structural: the values are a set, and a caller needs
every one of them. A method open to every caller says so with a value its host defines. **A method that declares
no authority is refused**: open access is a declared value, never an absence. Whose calls a `rateLimit` counts
together, and which party a `destination` is, are also the host's.

## The refusals

A document that is not well-formed is refused at the declaration's path with a NAMED refusal, the name leading
the sentence (`noAuthority: method 'orders.get' declares no authority; …`). `ContractFault` is the list:

| Name | The rule it breaks |
|---|---|
| `contractNameNotIdentifier`, `methodIdNotIdentifier`, `argumentNameNotIdentifier` | a name is an ASCII letter or `_`, then letters, digits and `_` |
| `duplicateContract`, `duplicateMethod`, `duplicateArgument` | a name is declared once in its scope |
| `noAuthority` | a method declares its authority |
| `malformedAuthority` | an authority value is non-empty, well-formed text, listed once |
| `servesNoVersion`, `malformedVersion` | a method serves at least one version; each is positive and listed once |
| `streamWithReadPolicy` | a stream's answers are not one answer to reuse |
| `streamWithoutResult` | a stream declares what it yields |
| `writeWithCacheLifetime` | only a read's answer may be reused: a method whose effect writes to its host declares no cache lifetime |
| `cacheLifetimeNotPositive`, `rateLimitNotPositive` | a lifetime, a count and a window are positive |
| `malformedInvalidation`, `invalidatesUnknownMethod` | an invalidation names methods of the same contract, each once |
| `malformedAuditKind`, `illFormedDescription` | text is non-empty where required, and well-formed |
| `hostOnlyArgument` | an argument crosses |
| `typeWithoutWireForm` | an argument's or a result's type mentions no closure, function, opaque host value or type variable |
| `emptyEnumeration` | an argument or a result is of an enumeration that has a case |
| `constraintMismatch`, `emptyConstraint` | a constraint fits its argument's type and admits a value |
| `defaultOutOfType` | a declared default is a value the argument admits |

A type that names an enum, a union or a record the vocabulary does not declare is refused as it is in a field.

Asking for a method at a schema version (`Contract.methodAt`) answers the method, or `unknownContract`,
`unknownMethod`, or `versionNotServed` naming the versions it does serve.

## Identity

A contract is identified by its name, and a method by `<contract>.<method>`. Both halves are identifiers, which
hold no `.`, so two methods have one name exactly when they are one method.

## The projection onto a capability

Every method projects to one `Capability` (`ContractProjection.capabilityOf`), which is what a registry gates:

| Capability | From the method |
|---|---|
| `id` | `<contract>.<method>` |
| signature `name` | the same |
| `holes` | one per argument, in declared order: `addr` and `name` are the argument's name, `kind` is `value`, `space` is the space of its type and constraint, `required` exactly when the argument is |
| `effect` | the method's effect class |
| `result` | the space of the result type; `{"$type":"nothing"}` for a method that answers nothing. For a stream it is the type of one item, for a job of the final answer |
| `placement` | the projecting host's: where a method runs is not the contract's to say |

Distinct methods project to distinct ids. Registering a projected vocabulary registers one capability per
method, and a method that was not registered is refused at dispatch as any unknown id is. The shape, read
policy, authority, recording, audit kind, rate limit, versions and descriptions are not in the capability: a
host that enforces them reads the contract.

### The space of a type

| IDL type | Value space | The argument string |
|---|---|---|
| `string` | `anyString`; `stringLen` under a `lengthBetween` | the string's characters |
| `int` | `intRange` over the 32-bit integers; the constraint's bounds under `intBetween` | the integer |
| `float` | `floatRange` over the finite doubles; the constraint's bounds under `floatBetween` | the number |
| `bool` | `enum` of `false`, `true` | `false` or `true` |
| an enumeration | `enum` of its canonical wire cases | the wire case |
| a 64-bit integer, a decimal, bytes, a time of day, a duration, a hosted format | `anyString` | the value's one canonical spelling |
| a hosted type that declares a wire type | the space of that type | as that type |
| a record, a union, a node, a kind, an op, a list, a map, a tuple, a keyed map, a set, a JSON value | `document` | the value's JSON text |

**A float at the registry is finite.** The IDL's float admits three non-finite tokens (`NaN`, `Infinity`,
`-Infinity`) in a document. In an argument they are refused, as `argOutOfSpace` naming the float range: a number
at this seam is finite, and a hole that took any text would validate nothing.

**A string-borne scalar is `anyString`.** The exact type stays in the contract, and the host's decoder checks
the grammar.

## Changing a contract

`fuaran-core-idl classify` reads `contracts` and grades every change by one rule: a change that can refuse a
caller the contract admitted, or change what an admitted caller receives, is **breaking**; every other change is
**additive**. The rows are generated into [`idl-stability-classes.md`](idl-stability-classes.md); in outline:

| Change | Class |
|---|---|
| a contract or a method added | additive |
| a contract or a method removed | breaking |
| an argument added, optional or declared with a default | additive |
| an argument added required, removed, or reordered among those both revisions carry | breaking |
| an argument's type, or the result, changed | breaking; undecided across a `hosted` / `json` / `opaque` slot |
| an argument loosened from required | additive |
| any other move of an argument's optionality, a changed default included | breaking |
| a bound withdrawn, or widened within its kind | additive |
| a bound declared, narrowed, or changed in kind | breaking |
| the shape or the effect class changed | breaking |
| an authority value removed; a served version added | additive |
| an authority value added; a served version withdrawn | breaking |
| a destination lost; `idempotent` gained; a rate limit withdrawn or loosened | additive |
| a destination gained; `idempotent` lost; a rate limit declared or tightened; any move of the read policy | breaking |
| a permission to record withdrawn, of the arguments or of the result, and none gained | additive |
| a permission to record gained on either half, whatever the other half does | breaking |
| a description, or the audit kind | host surface |

`record` is a permission, never a demand: it says what a host MAY keep of a call. A method that begins to
permit recording of its arguments or of its result withdraws what a caller was told, that what it sent or what
it was answered is not kept, so a permission gained is breaking. A host that records less than it may is
conformant, so a permission withdrawn is additive. A move that gains one half and withdraws the other is
breaking.

A contract that arrives or leaves is one row, not a row for each method. A rename is a removal and an addition.
No F# is generated for a contract, so no row moves a generated shape, and the one obligation a row raises is on
the hosts that serve or call the contract.

## What is generated from a contract

- **The JSON Schema of a method's arguments** (`Gen.methodArgumentsSchema`, `Gen.contractArgumentSchemas`), in
  the shape a tool definition takes: one object schema, a property an argument, `required` naming the arguments
  a caller must supply, `additionalProperties` false (an argument the method does not declare is refused),
  `title` the method's name and `description` its description. An argument's property is its type's schema with
  its description, its declared default and its bound beside it. `$defs` holds the declared types the arguments
  reach, and no other. A float argument is a number. A length bound is stated as the tightest bound that
  refuses no admitted value: the IDL counts UTF-16 code units and JSON Schema counts characters, so `maxLength`
  is the declared maximum and `minLength` half the declared minimum, rounded up.
- **The F\* model of a contract's types** (`FStarTarget.contractTypes`): the vocabulary the proof-model
  backend already models, holding for each method a kind of its arguments and, when it answers, a kind of its
  result, so that the round-trip theorems the backend generates are theorems about a call's arguments and its
  answer.

Nothing else is generated. A client, a server table, a protocol, a route, an envelope and a tool executor are a
runtime's. No F# and no TypeScript is generated from a contract, so there is no generated host to answer a
contract vector: the reference answers them, and a host in another language that reads contracts certifies
against `conformance/idl/contracts.json`.

A contract moves nothing that is generated from the vocabulary's data. Each generated module is headed by a
stamp naming the vocabulary, the hash of its `idl.json`, and a contract is a member of `idl.json`: that one line
tells a vocabulary that declares a contract from the same vocabulary without one, and every other line is the
same.

## The document space

A structured value at a hole or a result is a **document**: `{"$type":"document"}`, or
`{"$type":"document","documentType":t}` when it carries a type.

- **Membership is by shape.** A string is a member exactly when it is well-formed JSON with a canonical
  rendering: it parses under the strict reader (no `null`), and holds no repeated member, no ill-formed string
  and no number a finite float cannot spell. The type is carried and never read, so every document space has
  the same members and a host needs no resolver to decide one.
- **`documentType`** is the declared name of a record, or of a union applied to no type argument. For every
  other structured type it is the canonical text of the type expression: the type object `idl.json` writes,
  rendered with sorted members and no white space (`{"$type":"list","of":{"$type":"int"}}`). Two holes carry the
  same text exactly when the IDL gives them one type.
- **A family of its own.** An unconstrained document space admits every document space; a constrained one
  admits only the same `documentType`. No scalar space and no tree space admits a document, and no document
  space admits either.
- **One value, one identity.** A document argument is keyed by its canonical text (members sorted, no white
  space, the pinned float layout and string escape), so two spellings of one document are one invocation: one
  capture key, one pre-image, one grant. The space canonicalises; a caller need not.
- **A refusal names the space.** An argument that is no document is `argOutOfSpace` carrying the space.
- **A body is handed the parsed document**, not its text; decoding it into its type is the type's owner's.
