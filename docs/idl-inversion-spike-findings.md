# IDL inversion spike — findings + go/no-go (Phase 316)

**Status:** spike complete — **GO**. Advisory findings note for the schema-driven codegen capability
(Phase 317) and the IDL-canonical direction (substrate-as-asset brief). Amends no shipped contract.

> **This document describes `Fuaran.Core.Idl.Spike`, not the production engine** (Phase 201). Every gap it
> records is a gap in the SPIKE, measured in 2026 against a five-kind mini IDL and an illustrative,
> uncompiled emitter; the production `Fuaran.Core.Idl` + `Fuaran.Core.Idl.Codegen` have moved a long way
> past it, and a §3 item still reading "not yet built" was reporting the spike's state and nothing else.
> The two items that were genuinely open — defaults-fill and the full node envelope — are **closed below,
> by citing the production code that built them and the tests that hold it**. Read each item's own text
> for what shipped; nothing here is an open capability question. The doc is kept rather than deleted
> because the *reasoning* it records (what the inversion risked, what the corpus-as-oracle migration loop
> costs) is what a second IDL adopter meets, and that has not aged.

**Artefacts:** [`src/Fuaran.Core.Idl/`](../src/Fuaran.Core.Idl/) (the IDL model + a schema-driven encoder +
an illustrative F#-type emitter), [`src/Fuaran.Core.Idl.Spike/`](../src/Fuaran.Core.Idl.Spike/) (the mini UI
IDL + the authored corpus trees), and `tests/Fuaran.Core.Tests/IdlSpikeTests.fs` (the gate).

## 1. Result

A ~150-line IDL covering **five kinds** — `Card` (layout, children + optional field), `Heading` / `Badge` /
`Metric` (display, the last with bindings + formats), and `Button` (input, with an action) — drives a
schema-driven codec that produces canonical wire JSON **byte-identical to the committed corpus** for all
five fixtures (`heading-1`, `badge-1`, `btn-1`, `metric-1`, `card-1`), and proves it **in both directions**:
the authoring leg (authored `IdlValue` → encode → wire) and the round-trip leg (wire → decode → re-encode →
wire). Verified by an Expecto gate (6 spike tests green; full Core suite **264 passed, 0 failed**). The gate
is **self-contained** — the expected bytes are a vendored snapshot, so it is never vacuous — with a **drift
guard** that confirms the snapshot still matches the live `Fuaran-UI/wire-format-fixtures/nodes/` corpus when
checked out alongside (it ran, not skipped). A negative control diverges; the encoder rejects authored fields
absent from the IDL; the type-generation leg emits F# source for every kind/union/enum.

**What this proves (and what it doesn't).** The encoder and decoder are schema-DRIVEN **interpreters** — they
walk the IDL at runtime — so the spike proves the IDL carries enough to *round-trip the canonical wire*
(schema-sufficiency). It does **not** yet prove *compiled code emission* from the IDL (the actual generator,
with the structural-emission-not-string-templating discipline Phase 314 depends on); `Gen` here emits an
illustrative, uncompiled source string only. That is Phase 317 work.

**The decisive de-risk:** the codec **reuses `Fuaran.Core.Wire.Canon.render` + `Json.parse`** (the renderer
documented byte-identical to the UI host's `CanonicalJson` — Ordinal-sorted keys, pinned float layout,
canonical escaping). So the inversion's hardest-looking risk — reproducing the exact canonical bytes — **is
already solved by the shared Core renderer**; the spike only had to prove *structural* faithfulness, and it
does, both ways.

## 2. IDL expressiveness vs the wire contract

The wire contract is a strict subset of F#: flat `$type`-discriminated kinds, `$type` value-unions
(`TextSource` / `Binding` / `Format` / `Action`), bare-string enums, omit-on-absence optionals, recursive
nodes. Eight type constructors (`TStr` / `TInt` / `TBool` / `TFloat` / `TEnum` / `TUnion` / `TNode` /
`TList`) plus `Required`/`Optional` express everything these five kinds need. **No F# expressiveness was lost
for the structural layer** — the layer the inversion generates. (Behavioural code — render / evaluate /
ergonomic sugar — stays hand-written per host and is out of the IDL's scope by design.)

## 3. Known gaps the spike bounded (the full inversion must close)

1. **Generic element types — resolved (Phase 317 increment 1, `fuaran-core@c625e78`…).** `Binding<'T>` is now
   a single **parameterised union** (`TUnion of name * args`, `TVar`), instantiated at `float`
   (Metric.source/trend) and `bool` (Button.disabled) — both round-trip byte-identical, an element-type
   mismatch (`Binding<bool>` given a float) is rejected, and the type-gen leg emits a generic `type
   Binding<'T>`. Multi-parameter / higher-kinded shapes (`GridSpec<'row,'Msg>`, `Binding.Format` over a
   numeric source) remain for the full-vocabulary pass — but the hardest expressiveness question is answered.
2. **Defaults-fill — done (Phase 124, held to a law by Phase 201).** `Optionality.OmitDefault d` is the
   declaration: the encoder emits the field only when it differs from `d`, and the decoder restores `d` on
   absence, in every leg the generator emits — the kind spec, a record, a union case and the node envelope,
   across the interpreter (`Idl.Decode`), the F# backend (`dDef`), the TypeScript backend and the F* target.
   The omit test and the restore render from ONE literal (`Gen.fsDefaultLit`), which is why they cannot come
   apart. `tests/Fuaran.Core.Tests/IdlEnvelopeTests.fs` states it as a law rather than a code path: a member
   absent from a document decodes to its declared default and **re-encodes ABSENT**, so the bytes are stable
   across the round trip, while a present non-default value is carried unchanged.

   Note the boundary, because the two are easily confused: `Idl.IdlDefault` (the root `Defaults` list) is an
   **authoring** default — what a smart constructor fills so a caller need not pass it — and deliberately does
   **not** fill on decode. A `Required` member is always emitted, so filling one on decode would re-encode it
   PRESENT and `decode >> encode` would stop being the identity on the wire. The type's own doc comment carries
   the argument.
3. **The full node envelope — done (Phases 690 / 691 / 698, held to the five-slot shape by Phase 201).**
   `Idl.NodeFields` is the declaration — what a node carries beside `id` and `kind` — and every emitter derives
   from it rather than hard-coding it, because "what a node carries" is a property of the DOMAIN's tree and
   another domain has a different envelope or none. All five slots are expressible: `state` / `style` /
   `accessibility` as wire-visible members (omit-on-absence or omit-at-default — **this is where an ARIA
   default becomes IDL-declared**, the durable answer to the Phase 307 / 313 question), and `motion` /
   `extraAttributes` as `Optionality.HostOnly` over a `TFn` slot. `TFn` is named for its commonest use rather
   than its meaning: it is a slot whose HOST type is declared and whose WIRE form is fixed — for a host-only
   member, fixed at *absence* — so a map-valued or closure-valued host-only member needs no widening of the
   type model. `IdlEnvelopeTests` declares an envelope carrying all five, generates it on both backends and in
   the schema, and holds a host-only member to being on neither side of the wire.
4. **`schema.json` emission — done (Phase 317 increment 4, `fuaran-core@7a93718`).** `Gen.jsonSchema` emits a
   Draft 2020-12 JSON Schema from the IDL (`$defs` per enum/union/kind, `oneOf` by `$type`, required +
   `additionalProperties:false`). So **one IDL now drives all three §11 mirrors** — encoder + decoder +
   schema. (JSON Schema has no type parameters; a generic union's `'T` fields emit as permissive `{}`.)
5. **Generated F# compiles — done, feature-complete (Phase 317 increments 2–3, `fuaran-core@6ffbfa6`).**
   `Gen.fsharpModule` emits a self-contained F# module (`Generated.fs`) for **all 8 kinds**, handling every
   feature class — **optionals** (omit-on-absence via `List.choose`), **generics** (`Binding<'T>` by
   codec-passing), **lists**, and **node nesting** (recursive `encNode`; specs + `NodeKind` + `Node` emitted
   as one `type … and …` group so the cycle resolves). It **compiles as part of the build**, and its generated
   encoder round-trips heading/badge/button/stack byte-identical. A drift guard re-runs the generator vs the
   committed file; `.fantomasignore` keeps it pristine. Still open: the **syntax-tree-API** emission form (vs
   source-string), and scaling to the **full ~40-kind real tier** + the migration (generate → diff → switch).
   (Defaults-fill and the full node envelope were still open when this was written; both are closed above.)

## 4. Meta-schema cost + migration path

The IDL model is ~60 lines (eight types). For the full UI vocabulary (~40 kinds + the unions + enums) the IDL
is a **data file, not code** — bounded, and it *replaces* the hand-written encoder + decoder + `SchemaGen`
triple-mirror that the §11 forward-coupling rule currently keeps in lockstep by discipline.

The **incremental migration is validated as viable**: the corpus is the equivalence oracle. Generate a host's
structural layer from the IDL → byte-compare against the hand-written one via the corpus → switch when
byte-equal. The spike is exactly this loop at five-kind scale, green. Scaling to the full vocabulary is
mechanical, not novel.

## 5. Status — capability proven; instantiation remains

**Phase 317's codegen *capability* is proven end-to-end at slice scale (increments 1–4).** From one IDL:
typed F# (incl. a generic `Binding<'T>`), a runtime codec (encoder + decoder, both directions), a
**compiled, feature-complete generated encoder** (optionals + generics + lists + nesting, all 8 kinds,
byte-correct), and a **JSON Schema** — all green under `run.ps1`. The central uncertainties are retired: a
schema *can* round-trip the canonical wire byte-for-byte (incl. generics), and the generator *can* emit
compiling, byte-correct F#.

**What remains is instantiation/scaling, not contract questions** — genuinely larger, multi-session work, and
NOT claimed complete:

1. **Full ~40-kind breadth + the real-tier migration.** Author the full UI IDL (matching `Fuaran.UI` `Types.fs`)
   and run the migration loop — generate → byte-diff vs the hand-written tier → switch. The migration target
   lives in the **Fuaran-UI** repo, so this is downstream consumer work, not Fuaran-Core spike work.
2. **A second, independent backend** (C# or TS) — proves host-independence and **cross-host byte-identical
   hashing** (the precondition for Phase 320 attestation). A whole second emitter.
3. ~~**Defaults-fill + the full node envelope**~~ — both closed; see items 2–3 of §3. What remains of this
   entry is the **syntax-tree-API** emission form (vs source-string) the Phase 321 trust boundary may want.

These are the right shape for follow-on increments; the capability they instantiate is built and verified.
