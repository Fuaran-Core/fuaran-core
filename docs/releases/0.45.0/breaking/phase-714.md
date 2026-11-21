- **A model's argument object is read by Core, and an emitted op is read through the codec its surface carries**
  (Phase 714, `Fuaran.Core.Function`, `Fuaran.Core.AiSurface`, `Fuaran.Core.Conformance`, DECISIONS.md D282) — class
  **breaking**: `union-widening` on `Families.Takes`, which gains `EmissionSurfaceWitness`, and a changed answer of
  `Function.toJsonSchema`; every other move is additive. `CapabilityCodec.decodeArgs` / `decodeArgsJson` read the object
  `toJsonSchema` describes into the argument list every seam operation takes, in declaration order and each value in
  its space's canonical spelling: an integer hole reads a JSON integer or a number with no fractional part, a number
  hole either, a slot an object, a document any value. They answer every `DecodeError` at once (`UndeclaredMember`,
  `WrongKind`, `OutOfRange`, `MissingField`; a member given twice is `OutOfRange` at its second occurrence), and an
  `Ok` is a list `Capability.validateArgs` admits. The schema and the reader come from one entry per value space.
  `EmissionSurfaceWitness` (`Surface`, `Emission: Codec<'Op>`) embeds the frozen `AiSurfaceWitness` and is frozen at
  birth; `AiSurface.decodeEmission` / `decodeEmissionJson` read one emitted op with the codec's strict reader, and
  `AiSurface.kindSchemas` lists a `kind`-keyed union codec's case schemas for the catalogue. `capabilityLaws` gains a
  law (twelve where it reported eleven): over drawn signatures and argument objects, `toJsonSchema` admits an object
  exactly when `decodeArgs` reads it. `Conformance.emissionLawsAt` is a new family. `capabilityArgumentLaws`
  (`conformance/laws/capability-argument-laws.json`, twenty vectors) is a new vector family of this repository's; it is
  not a copy in the shared corpus. **What breaks, and what a consumer changes:** `toJsonSchema` now states
  `"additionalProperties": false`, and a slot's property requires `kind` (a string, or `{"enum": [k]}` for a slot
  constrained to `k`), so a consumer pinning its bytes re-pins them; an argument object the old schema admitted and the
  seam refused is now refused by the schema too. An exhaustive `match` over `Families.Takes` is incomplete until it
  adds an `EmissionSurfaceWitness` arm. `StringLen`'s bounds are still counted in UTF-16 code units, which JSON
  Schema's `minLength` / `maxLength` count in code points: the two differ on text outside the Basic Multilingual Plane
  (D282.5).
