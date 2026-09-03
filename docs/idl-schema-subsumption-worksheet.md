# IDL JSON-schema leg — subsumption worksheet

Phase 697. `Gen.jsonSchema` had only ever been smoke-tested on the 8-kind `miniIdl`. This records
what it does now that it has met the full corpus, and answers the one question the phase exists to
answer: **can it subsume the hand-written `Fuaran.UI.Ops.SchemaGen`?**

**Verdict: not yet.** Three gaps were named at Phase 697; Phase 703 closed the first, so two
remain, and neither is in the schema leg's core. The leg is corpus-certified for what it covers,
which is the precondition for the decision, not the decision.

Certification lived in `tests/Fuaran.Core.Tests/IdlSchemaTests.fs`, evaluated by JsonSchema.Net —
the same Draft 2020-12 implementation the consuming tier uses for the hand-written schema, so a
disagreement between the two is about the schemas, not the validators.

> **Where this leg runs now (Phase 123, 2026-09-03).** That suite generated its schema from a
> DOMAIN's vocabulary and evaluated it against that domain's committed wire corpus, and both left
> this repo when the vocabulary went home (DECISIONS.md D14). Everything below is the record of
> what the leg established while it ran here, and the verdict it reached is unchanged — but the
> corpus-scale certification it rests on is a domain-side gate now, not a Core one. What this repo
> still certifies about `Gen.jsonSchema` is the generator's own behaviour, over the mini-IDL,
> wire-shape and enum-wire families.

## What the certification established

- **Every node accept fixture in the corpus validates.** All 92, against the schema generated from
  the real `uiIdl`.
- **No dangling `$ref`.** Checked by walking the emitted document and resolving every reference it
  contains, not by inspecting the generator.
- **The certification is not vacuous.** Two structurally-invalid payloads (unknown kind tag; a node
  with no `id`) are pinned as rejected, and the fixture sweep asserts it actually read the corpus
  rather than skipping into a silent pass.

## Three defects fixed on the way in

| Defect | Resolution |
|---|---|
| `idl.Records` absent from `$defs` while every `TRecord` slot emitted `$ref: #/$defs/<name>` | Records join the assembly via `recordSchema` — **no `$type` const**, which is exactly what distinguishes a record from a union case on the wire. A strict validator treats an unresolvable `$ref` as an error, so the leg could not have certified at all until this was fixed. |
| No transparent-union reflection | `TextSource.Literal` is on the wire BARE (`"x"`, not `{"$type":"Literal","text":"x"}`). Without it the schema rejected the canonical form of every literal string in the corpus. `unionDef` now emits the bare form beside the tagged branches; the tagged branch stays, because §16 lenient-accept admits the envelope on input. |
| `additionalProperties: false` everywhere | **Aligned with the format, not exempted.** The decoder tolerates unknown keys (`WIRE_FORMAT.md` §2.1 rule 2, field-lookup-by-name) and the published `schema.json` says so in its own header. A schema stricter than the format rejects payloads the format accepts — and breaks the forward compatibility that tolerance exists for, since an older host validating a newer producer's output would fail on a key it has not learned yet. Pinned by a test that walks the document for any `additionalProperties: false`. |

A fourth, found while fixing the third: **`HostOnly` fields were being emitted as properties.** They
are never on the wire in any state (Phase 691), so advertising them describes a key no encoder
writes. Now filtered out of both `properties` and `required`.

## Reject family — 35 of 42 caught structurally

The remaining 7 are **not** schema defects. They fall into three classes, and only one of them is
a permanent exemption:

| Fixture | Class |
|---|---|
| `reject-daterange-unordered` | **Provably inexpressible in Draft 2020-12** — cross-field ordering (`from` ≤ `to`). The decoder is its only possible enforcer. |
| `reject-emptynodeid` | **IDL expressiveness gap, not a schema gap.** Draft can state this (`minLength: 1`); the IDL cannot, because `TStr` carries no constraint vocabulary. The hand-written schema states it because a human wrote it directly. |
| `reject-null-action-aitool-args`, `reject-null-action-notify-payload`, `reject-null-action-setstate-value`, `reject-null-custom-prop`, `reject-null-i18n-arg` | **Deliberate abstention.** Rule 12 forbids `null` in structured-payload positions; the IDL renders `TJson` / `THosted` as `true` (any JSON) because it does not decompose that content. Expressible in principle, but stating it means the IDL modelling "any JSON except null", which it currently has no way to say. |

Neither of the two gap classes is an argument against subsumption on its own — both are IDL type-model
gaps that would be fixed once, in the IDL, and benefit every backend rather than only the schema.

## `$defs` diff against the committed `schema.json`

Generated: 105 definitions. Hand-written: 96. The difference is overwhelmingly **naming and
structural convention**, not semantics.

**Only in the generated schema (52).** 39 kind definitions named `Heading` / `Badge` / … against the
hand-written `HeadingSpec` / `BadgeSpec` — a naming convention difference, not a contract one. Seven
records the hand-written schema inlines rather than naming (`ButtonGroupItem`, `DateRangePair`,
`InvokeArg`, `RangePair`, `StaticRows`, `SwitchCase`, `TransformParam`). Five enums plus one union
the hand-written schema does not define at all (`BoxRole`, `ChannelDirection`, `DeterminismSource`,
`HostEffect`, `Motion`, `LayoutMode`).

> `Motion` is a **host-only** enum — never on the wire. The generated `$defs` defines it even though
> nothing references it, because the assembly emits every declared enum rather than only the reached
> ones. Harmless (an unreferenced definition is inert) but untidy, and worth closing when the
> assembly next changes: emit the reachable set, the way `Gen.referenced` already computes it for
> the F# emitter.

**Only in the hand-written schema (43).** The `*Spec` naming half of the same convention difference
(21). The four category definitions `DisplayKind` / `LayoutKind` / `InputKind` / `VisKind` — the
generated schema has no equivalent, and does not need one: Phase 692 flattened `NodeKind`, and the
categories are a host-side classification recovered on decode (`WIRE_FORMAT.md` §3.2). The layout
family `BoxLayout` / `FlexLayout` / `GridSpec` / `GridTemplate`, and `CellValue`. `AriaRole`, which
the IDL models as `THosted` and therefore renders as unconstrained JSON.

> Counted before Phase 703. `NodeKind` and `TreeOp` were in this column at the time and are now in
> both, which is the shape of the diff shrinking as the IDL gains expressiveness rather than as the
> schemas are talked into agreeing.

## The three gaps that actually block subsumption

1. ~~**No `TreeOp` root.**~~ **CLOSED by Phase 703.** The IDL now carries an op vocabulary
   (`Idl.Ops`), the schema root is `oneOf [Node; TreeOp]` when a domain declares ops, and all 22 op
   fixtures round-trip through the interpreter byte-identically. `NodeKind` also became a named
   definition rather than an alternation inlined into `Node.kind`, matching the published schema —
   `TKind` (`EditNode.newKind`) needed something to reference.

   Phase 703 left ONE leg of its own unshipped, which is now the honest remainder here: the F# and
   TypeScript **emitters** do not generate an op family. Nothing walks `Idl.Ops` in either backend,
   so no generated artefact changed; the arms are explicit and fail loudly rather than falling
   through a match. Emitting a `TreeOp` DU is a genuinely larger piece of work than the modelling
   was — it is msg-carrying through `TKind`/`TNode`, so it lands as a generic type group — and
   nothing consumes it yet.
2. **`AriaRole` is unconstrained.** The hand-written schema enumerates the ARIA roles plus a
   free-string escape. The IDL renders it `true`, because its set is genuinely OPEN
   (`AriaRole.Custom of string` emits its payload verbatim) and no `TEnum` can model an open set —
   see Phase 707, which fixed the *closed*-set half of the same problem for `liveRegion`. Closing
   this needs an IDL type for "closed set OR any string", which does not exist yet.
3. **No constraint vocabulary on `TStr`.** `reject-emptynodeid` is the visible cost; `minLength`,
   and any future bound, are unstateable.

## Recommendation

_(Updated after Phase 703 closed blocker 1.)_ Keep both, and revisit when blockers 2 and 3 are
addressed. Subsuming now would trade a schema that states two things the
generated one cannot for one that is merely generated — a regression in what the published artefact
promises, in exchange for removing a mirror. The mirror is worth removing, but only once the
generated leg is at least as expressive, and each remaining gap is a one-time fix in the IDL that
pays off across every backend rather than only here. Phase 703 is the worked example: closing the
op gap in the IDL gave the schema its second root, `idl.json` its op family, and the 700 classifier
an op taxonomy, from one piece of modelling.

The naming convention (`Heading` vs `HeadingSpec`) should be settled deliberately at that point, not
drifted into: whichever wins becomes the `$ref` vocabulary a third-party validator quotes.
