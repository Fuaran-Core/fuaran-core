# Adopting a domain over `Fuaran.Core.*`

The 30-minute on-ramp for re-expressing a domain spine (UI / Calc / Documents / CAD / Office) over
the shared substrate. The runnable template lives at
[`samples/adoption`](../samples/adoption/Program.fs) — read it alongside this guide; the worked
real-world example is the [Documents adoption pilot](../../Fuaran-Documents/docs/CORE-ADOPTION-PILOT.md).

The shape is always the same: **map your types to the four witnesses → certify → re-express the
op-stream.** Each step is a few lines.

## 0. Reference the packages

Add `Fuaran.Core.{Tree,Ops,OpStream,Conformance}` (and `.Wire` if you encode ops as JSON) from the
local feed / GitHub Packages. All are Apache-2.0, FSharp.Core-only.

## 1. Map your types to the witnesses

| Your type | Core witness | Construction |
|---|---|---|
| `'Id` (string or Guid) | `IdWitness<'Id>` | `{ ToString; OfString; Equals }` |
| `'Node` (your closed-`NodeKind` tree node) | `NodeWitness<'Node,'Id>` | `{ Id; KindTag; Children; ReplaceChildren }` |
| `'Op` + `'State` | `StreamWitness<'Op,'State,'Rej>` | `{ Apply; Encode; Decode }` |
| a saved tree as a function (optional) | `ArtifactWitness<'Node,'Id>` | `{ Tree; IdW; Holes; Effect; Bind }` |

```fsharp
let idw : IdWitness<string> = { ToString = id; OfString = id; Equals = (=) }
let nodew : NodeWitness<Item,string> =
    { Id = fun i -> i.Id
      KindTag = fun i -> kindTag i.Kind
      Children = fun i -> i.Children
      ReplaceChildren = fun i cs -> withChildren cs i }   // see the leaf caveat below
```

## 2. Certify the witness + your reducer

```fsharp
let canHold (i: Item) = isContainer i.Kind                 // F1: see below
let opGen = { Tree = genTree; FreshNode = genFresh; CanHold = Some canHold }

Conformance.witnessLaws nodew idw opGen seed iters         // the witness is well-formed (Phase 253)
Conformance.opAlgebra   nodew idw opGen seed iters         // skeleton ops over your witness (251)
Conformance.reducer     myApply myStreamGen None seed iters // your OWN reducer (Phase 254)
```

`witnessLaws` runs first — if your `ReplaceChildren` isn't total it tells you exactly that, instead
of surfacing later as a confusing `apply ∘ invert` failure.

## 2b. Certify the authoring surface too, not only the codec

```fsharp
let constructW : ConstructWitness<Item> = { Surface = "the Item smart constructors"; Construct = construct }

Conformance.constructThenEncodeLaws "my domain" myCodec (Some constructW) myCorpus   // Phase 126
```

Your codec laws certify that bytes survive `decode` and `encode`. They never call the smart
constructors an author writes against, so a field that widens in memory keeps a round-trip suite
green while breaking every program that BUILDS a value. This family rebuilds each corpus document
through your constructors and re-encodes it. A domain supplying no witness is reported by name as
not adopted, never as passed. See [`construct-then-encode.md`](construct-then-encode.md).

## 2c. If your authoring tier is C# or VB

There is no C# facade from `0.33.0`. `Fuaran.Core.CSharp` (Phase 128) — a C#-shaped surface over the
column layer, the artifact-function declaration family and the wire JSON model — was published up to
`0.32.0` and removed by Phase 231, because the consumer it was shipped for kept its rule another way
and never adopted it; its dataframe half, `Fuaran.Core.DataFrame.CSharp`, is removed by the compute
repository in the release that raises its pin to `0.33.0`. The versions already published stay on
nuget.org. A C# or VB tier constructs Core's values through the F# surface, wrapping only what it
authors, and a generated C# veneer over Core's closed unions is the route by which a facade returns
(DECISIONS.md D28).

## 3. Re-express the op-stream

```fsharp
let streamW : StreamWitness<MyOp, MyState, MyRej> =
    { Apply  = MyOps.apply        // your reducer
      Encode = MyWire.encodeOp    // your op → JSON
      Decode = MyWire.decodeOp }  // string -> Result<MyOp, string>   (Phase 252)

// `actor` is a typed `Actor` (`Human "alice"` / `Agent("model","ver","id")`) — folded into the
// hash since Phase 320, so attribution is tamper-evident. A pre-320 stream migrates via
// `fromJsonlLegacyActor` + `rehash` (see docs/migrations/0.0.1-alpha.13-typed-attested-provenance.md).
OpStream.append OpStream.defaultHash streamW actor op state recs   // hash-chained
OpStream.replay streamW state0 records                              // deterministic
OpStream.verifyChain OpStream.defaultHash streamW records          // tamper-evident
OpStream.fromJsonl streamW jsonl  : Result<_, string>              // portable — runs in-browser
```

## The caveats the pilot surfaced (read these before you start)

- **F1 — `ReplaceChildren` is partial on leaves.** Your leaf kinds (`Paragraph`, `Cell`, …) can't
  hold children, so `withChildren` is a no-op on them. Supply `CanHold = Some isContainer` and
  dispatch real edits through `Ops.applyContained` / `canApplyContained` — they reject an insert/move
  under a leaf with `NotAContainer` instead of silently no-op'ing. Containment *legality* (which kind
  may parent which) stays yours; `canHold` answers only "can this node hold children at all".
- **F2 — conformance is witness-level *and* reducer-level.** `opAlgebra` certifies the tree witness
  (skeleton ops); your production reducer (`DocOp` apply, etc.) is certified separately by
  `Conformance.reducer`. Run both.
- **F3 — `Decode` returns `Result`.** `StreamWitness.Decode : string -> Result<'Op,string>` — most
  domains already have a `Result`-returning decoder, so just plug it in (no exception adapter).
- **F4 — hash format.** Core's chain payload differs from a hand-rolled one, so re-expressing changes
  the hashes; a domain with persisted streams needs a migration (Phase 255, when it lands).
- **F5 — typed actors.** Core's op-stream actor is a `string`; keep your typed `Actor` and tag at the
  seam (`"human:" + u`).
- **F6 — the win.** Core's `fromJsonl` is portable (FSharp.Core only), so your Fable host can
  rehydrate and `verifyChain` a stream in-browser — which a `System.Text.Json` decoder can't.

## The dataframe path

`Fuaran.Core.DataFrame` (the `Transform` pipeline and its reference evaluator, the typed delta, the
incremental seam), `Fuaran.Core.Column.Ops` (the columnar op algebra), `Fuaran.Core.DataFrame.Conformance`
(the law families over them, `transformLaws` among them) and, until that repository's release that
raises its pin to `0.33.0`, `Fuaran.Core.DataFrame.CSharp` (the dataframe half of the removed C#
facade, step 2c) are produced by their own repository,
[`Fuaran-Core/fuaran-core-compute`](https://github.com/Fuaran-Core/fuaran-core-compute), from `0.33.0`,
under the same package ids and the same namespaces (DECISIONS.md D66; Phase 258 removed them here).
Adopting the dataframe path is adopting that repository's packages: its `docs/` carry the
incremental-evaluation guide that used to be `docs/incremental-evaluation.md` here, and its
`conformance/laws/transform-laws.json` is the transform law corpus. Everything above this section —
the tree, the op-stream, the witnesses, `Fuaran.Core.Column` and the kit — is still this
repository's, and the compute packages are built over it.

A consumer that pinned this repository's `<Version>` for those four ids pins the compute repository's
for them instead: a second `PackageVersion` property, one per producing repository. The versions this
repository published of them (up to `0.32.0`) stay on nuget.org and keep restoring.

## Verify

`./verify.ps1` builds the sample and runs its conformance report as part of the green gate. A clean
adoption prints `conformance: GREEN`.

## See also

- [`samples/adoption/Program.fs`](../samples/adoption/Program.fs) — the runnable template.
- [`CORE-ADOPTION-PILOT.md`](../../Fuaran-Documents/docs/CORE-ADOPTION-PILOT.md) — the Documents pilot (the worked example + the findings).
- [`STABILITY.md`](../STABILITY.md) — which witness surfaces are stability-critical.
- [The dataframe path](#the-dataframe-path) — where `DataFrame`, `Column.Ops`, their law families,
  the dataframe facade and the incremental-evaluation guide live since Phase 258.
- [`construct-then-encode.md`](construct-then-encode.md) — certifying the authoring surface (step 2b
  above): why a codec round trip cannot see a widened builder, and how to answer for the family.
- [`../DECISIONS.md`](../DECISIONS.md) D28 — why the C# facade of step 2c shipped on one consumer,
  the Phase 231 re-measurement that removed it, and what would bring a C# veneer back.
