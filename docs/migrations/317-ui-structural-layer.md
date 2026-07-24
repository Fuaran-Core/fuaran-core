# Migration: generate the `Fuaran.UI` structural layer from the IDL (Phase 317)

**What this is.** The path to replace the hand-written `Fuaran.UI` *structural* wire layer — the
`NodeKind` DU + its per-kind spec records in `Types.fs`, and the `encodeNode` in
`Fuaran.UI.OpStream.Abstractions.CanonicalJson` — with F# **generated from one IDL**
(`Fuaran.Core.Idl`). The IDL is now the canonical source; `Types.fs` becomes a projection of it,
byte-diffed against the same wire corpus that already gates the hand-written host.

**Status.** The IDL *models* the full ~40-kind real vocabulary and the schema-driven interpreter
(`Encode.encode`) reproduces the 84-fixture corpus byte-for-byte (`IdlUiTests`). The **compiled**
generator (`Gen.fsharpModule`) now also emits a *compiling*, corpus-byte-identical encoder for the
whole vocabulary (`tests/Fuaran.Core.Tests/UiGenerated.fs` + `IdlUiGenTests`). What remains is the
consumer-side switch inside the `Fuaran.UI` (`fuaran`) tier — downstream work, tracked as
`fuaran#317`, **not** a Core change.

## The three artefacts the generator emits (per host)

From `Gen.fsharpModule "<module>" uiIdl <kindTags>`:

- **types** — `[<RequireQualifiedAccess>]` enums + value-unions + non-discriminated records + one
  per-kind `<Kind>Spec` record + `NodeKind` + `Node`, all in one mutually-recursive `type … and …`
  group (a union can hold a record or a `Node`, a record holds unions, a spec holds `Node list` —
  one cycle).
- **encoder** — `encodeNode : Node -> string`, rendering through the shared `Fuaran.Core.Canon`
  (Ordinal-sorted keys, `\u00xx` control escapes, pinned `ToString("R")` floats) — byte-for-byte the
  rules `CanonicalJson.appendObject` / `appendRawString` / `appendFloat` apply, inherited not
  re-implemented.
- **plumbing** — a `NodeWitness<Node, string>` (so the generated layer plugs into
  `Fuaran.Core.Tree` / `.Validator`), a `runValidator` scaffold, and `mk<Kind>` smart constructors.

## The `'Msg`-erasure boundary (the one genuine design call)

The real `Fuaran.UI` `Types.fs` is `'Msg`-generic: closure-typed fields (`Binding.Query`'s accessor,
every `onChange` / `onClick`, a column projection) and `obj`-erased fields (`Sparkline.source`'s
`float seq`, `Select.value`) carry host behaviour and CLR values that are **invisible on the wire** —
they serialise to the fixed sentinel strings `"<closure>"` / `"<opaque>"`.

**Decision: the generated structural layer is encoder-only and `'Msg`-free.** `TClosure` and
`TOpaque` fields are erased to `unit`; the generated encoder emits the sentinel unconditionally,
ignoring the (`unit`) value. So the generated `Node` is a pure *structural* value — enough to
reproduce the wire exactly, but carrying no behaviour. This is deliberate and load-bearing:

- **Wire-faithful.** The sentinel is the whole of what the wire observes, so the generated encoder is
  byte-identical to `CanonicalJson.encodeNode` over every corpus fixture regardless of the erasure.
- **Trivial to author + attest.** A `unit` field is `()`; there is no `'Msg` to thread, so the
  structural layer is the same shape across the .NET / Fable / TS hosts — the precondition for
  cross-host byte-identity (already proven for the TS backend on the mini vocabulary).
- **Behaviour re-attaches domain-side.** The switch-over does **not** delete the `'Msg`-generic
  authoring surface. `Fuaran.UI` keeps its typed author facades (`GridSpecOf<'row,'Msg>`, the
  closure-bearing `Binding` / `Action` builders); the generated structural layer is the **wire
  projection** those facades reduce to. Named holes (Phase 318) are how behaviour is re-bound on top
  of inert generated structure.

## Migration recipe (the `fuaran` tier — `fuaran#317`)

1. **Generate the structural module** from `uiIdl` into the `Fuaran.UI` tree (a committed
   `Generated.fs` + a drift guard, exactly as `UiGenerated.fs` is committed + guarded in Core's
   tests). `open Fuaran.Core` gives it `JVal` / `Canon` (Wire), `NodeWitness` (Tree),
   `Validator` / `Defect` (Validator).
2. **Byte-diff the generated encoder vs the hand-written host** over the live
   `wire-format-fixtures/nodes/` corpus. The byte target is
   `Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode`: for every fixture,
   `Generated.encodeNode node` must equal `CanonicalJson.encodeNode` of the equivalent hand-written
   `Node`. (Core already proves the generated encoder equals the *corpus bytes* — the corpus is the
   hand-written host's own gate, so this is transitive; the tier-side test makes it direct.)
3. **Switch the structural type + encoder.** Replace the hand-written `NodeKind` + spec records in
   `Types.fs` and `CanonicalJson.encodeNode` with the generated module (re-export under the existing
   names so downstream call sites are untouched). Keep the `'Msg`-generic author facades as a layer
   that constructs generated structural values (the erasure boundary above).
4. **Re-attach behaviour** via the named-hole surface (Phase 318) — the generated structure is inert;
   holes bind the closures the erasure dropped.

### Out of scope for this migration (deferred, annotated on the phase)

- The **node envelope** (`state` / `style` / `accessibility`) — no corpus `Node` fixture carries it.
- **Multi-param generic author facades** (`GridSpecOf<'row,'Msg>`) — a typed author surface, not
  wire-visible; they sit *above* the generated structural layer, unchanged by the switch.
- **`grid-transform`** — a `Binding.Transform` embeds a `Fuaran.Core.DataFrame` pipeline rendered by
  Core's own `DataFrameCodec` / `ColumnCodec`, a separate wire surface from the UI structural layer.
- The two `null`-bearing fixtures (`multiselect-1` + `form-segmented`) — a `Binding.Static None`
  renders JSON `null`, which the FSharp.Core `JVal` model (`Fuaran.Core.Wire`) has no case for. A
  wire-`null`-representation decision for the owning team, **not** something to bolt on unilaterally.

## Verification

- `Fuaran.Core.Tests.IdlUiTests` — the schema-driven interpreter reproduces the whole corpus
  byte-for-byte (all five families, ~40 kinds).
- `Fuaran.Core.Tests.IdlUiGenTests` — the **compiled** generated encoder (`UiGenerated.encodeNode`)
  is byte-identical to the corpus across representatives of every emission class (records, maps,
  closure/opaque sentinels, optional union-case fields, multi-`Node` kinds, the meta family, and the
  `Binding.Format` polymorphic-recursion path), plus a drift guard that the generator still
  reproduces the committed `UiGenerated.fs`.

## Rollback

Nothing in Core is switched — `Gen` is additive tooling and `UiGenerated.fs` is test-only. The
`fuaran`-tier switch (step 3) is the reversible step: re-export the generated names, and rolling back
is reverting the re-export to the hand-written `Types.fs` / `CanonicalJson.fs`, which stay
regression-locked by the same corpus until the switch is trusted.
