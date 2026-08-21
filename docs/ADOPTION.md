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

## Verify

`./verify.ps1` builds the sample and runs its conformance report as part of the green gate. A clean
adoption prints `conformance: GREEN`.

## See also

- [`samples/adoption/Program.fs`](../samples/adoption/Program.fs) — the runnable template.
- [`CORE-ADOPTION-PILOT.md`](../../Fuaran-Documents/docs/CORE-ADOPTION-PILOT.md) — the Documents pilot (the worked example + the findings).
- [`STABILITY.md`](../STABILITY.md) — which witness surfaces are stability-critical.
- [`incremental-evaluation.md`](incremental-evaluation.md) — adopting incremental `Transform`
  evaluation (a refresh that costs the rows that changed).
