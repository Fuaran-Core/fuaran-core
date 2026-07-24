# IDL inversion spike — findings + go/no-go (Phase 316)

**Status:** spike complete — **GO**. Advisory findings note for the schema-driven codegen capability
(Phase 317) and the IDL-canonical direction (substrate-as-asset brief). Amends no shipped contract.

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
2. **Defaults-fill.** The spike exercises omit-on-absence (wire-visible) but not IDL-declared *default values*
   that are emitted on absence. Same mechanism (the field carries a default; absence → emit it) — feasible,
   not yet built.
3. **The full node envelope.** The spike emits `id` + `kind` only (matching the minimal-node fixtures). The
   real node adds `state` / `style` / `accessibility` / `motion` / `extraAttributes` as omit-on-default
   slots — same mechanism, more fields. **This is where ARIA defaults become IDL-declared** (the durable
   answer to the Phase 307 / 313 accessibility-defaults question).
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
   source-string), scaling to the **full ~40-kind real tier** + the migration (generate → diff → switch), and
   defaults-fill / the full node envelope (items 2–3 above).

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
3. **Defaults-fill + the full node envelope** (items 2–3 of §3), and the **syntax-tree-API** emission form (vs
   source-string) the Phase 321 trust boundary may want.

These are the right shape for follow-on increments; the capability they instantiate is built and verified.
