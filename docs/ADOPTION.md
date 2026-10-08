# Adopting a domain over `Fuaran.Core.*`

The 30-minute on-ramp for re-expressing a domain spine (UI / Calc / Documents / CAD / Office) over
the shared substrate. The runnable template — and the worked example every section below refers
to — lives in this repository at [`samples/adoption`](../samples/adoption/Program.fs): a tiny
outline domain taken through every step, the invocable seams of [step 4](#4-offer-the-domain-as-something-to-invoke)
included. Read it alongside this guide; `dotnet run --project samples/adoption` prints its
conformance report.

The shape is always the same: **map your types to the four witnesses → certify → re-express the
op-stream.** Each step is a few lines.

## 0. Reference the packages

Add `Fuaran.Core.{Tree,Ops,OpStream,Conformance}` (and `.Wire` if you encode ops as JSON;
`.Function` and `.Query` for step 4) from nuget.org. All are Apache-2.0 and depend on FSharp.Core
only at run time. Each package carries its XML documentation file, so the editor shows the doc
comments; [`README.md`](../README.md) stamps each surface it names with the version it arrived in, so
check a stamp against the version you restored.

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

## 2d. Every family, one shape: law families and vector families

The kit ships two kinds of family, and you run both the same way — **every entry answers
`LawResult list`, and green means every result `Passed`**:

- **Law families** are drawn: they take your witnesses, a `seed` and an iteration count, and sample
  your domain. `Conformance.certify` (or `certifyStream`, for a domain with no uniform tree) is the
  base run, already an aggregate — its `ConformanceReport.Results` is the `LawResult list`. Every
  other law family is OPT-IN and called directly beside it: one that needs a witness capability your
  domain has (`keyedChildrenLawsAt`, `referenceLawsAt`, `propagationEvaluatorLawsAt`,
  `projectionLawsAt`, `observerLawsAt`, `sanitizeLawsAt`, `capabilityLawsAt`, …), one for a seam not
  every domain has, or one asking for a stronger promise than the base contract.
- **Vector families** are enumerated: a runner over a fixed table (`WireNullTolerance`,
  `StringEscapeVectors`, `EncodingProfileVectors`, `ParityVectors`) or over YOUR store, walked whole
  (`StoredIdentity.linearLaws` / `dagLaws` / `captureLaws`, `EncodingProfileVectors.storedCodecLaws`).
  Each answers `laws ()` over its committed corpus, and `lawsWith` over a vector set you hand it.
  Their vector-shaped entries (`run`, `check`, `lines ()`) stay for the hosts in other languages that
  diff them; `laws` is how the kit, and you, read them.

```fsharp
let results =
    (Conformance.certify nodew idw opGen sw streamGen OpStream.defaultHash seed iters).Results
    @ Conformance.keyedChildrenLawsAt keyw nodew idw opGen seed iters      // an opt-in that is yours
    @ StoredIdentity.linearLaws "v2" OpStream.defaultHash sw sw myStoredChain // your store, walked whole
    @ WireNullTolerance.laws ()                                              // a fixed corpus

results |> List.filter (fun r -> not r.Passed) |> List.iter (fun r -> printfn "RED %s: %A" r.Law r.Counterexample)
```

**Which families are yours** is data, not reading: `Families.families` enumerates every family the
kit ships, each with the witnesses it takes, whether `certify` runs it, and — for an opt-in — why
(`NeedsWitnessCapability`, `SeamNotEveryDomainHas`, `StrongerPromise`, `NoWitnessToCertify`). The
generated [`conformance-families.md`](conformance-families.md) is the same roster as a table, and its
JSON twin is what a census tool reads.

**A family that reached nothing is red, never green.** A drawn family whose sample missed a verdict
fails its own `sample adequacy (…)` law; a vector family handed no vectors fails `<family>: the corpus
evaluated at least one vector`; a stored family over an empty store fails too. So an empty or
unreachable run cannot pass as an adopted one.

**The names follow one rule.** A bare name is the family at its default (`capabilityLaws` over the
kit's own fixtures); every family that takes a witness capability your domain supplies is spelled
`…At` (`keyedApplyLawsAt`, `memoLawsAt`, `FoldConfluence.laneFoldLawsAt`, …); and `…With` is the
`…At` family — or a bare one — with one more parameter, last before the seed
(`propagationEvaluatorLawsWith`'s prior-aware evaluator, `keyedArbitrationLawsWith`'s footprint and
admission pair, `laneFoldLawsWith`'s `HashFn`, `lawsWith`'s vector set). There is no `…AtWith`. Phase
390 brought every witness-taking family under the rule; the bare spellings it replaced
(`keyedChildrenLaws`, `referenceLaws`, `propagationEvaluatorLaws`, `projectionLaws`, `observerLaws`,
`sanitizeLaws`, `attestationLaws`, `compositionLaws`, `compositionPilot`, `memoLaws`,
`memoSoundnessLaws`, `functionVerifyLaws`, `verifyHonestyLaws`, `encoderInjectivityLaws`,
`keyedApplyLaws`, `keyedArbitrationLaws`, `aiSurfaceLawsUnderKitPolicy` — now
`aiSurfaceKitPolicyLawsAt` — and `FoldConfluence.laneFoldLaws`) were obsolete forwards and left at
`1.0.0`; call the `…At` form ([the 1.0.0 migration](migrations/1.0.0.md)).

## 3. Re-express the op-stream

```fsharp
let streamW : StreamWitness<MyOp, MyState, MyRej> =
    { Apply  = MyOps.apply        // your reducer
      Encode = MyWire.encodeOp    // your op → JSON
      Decode = MyWire.decodeOp }  // string -> Result<MyOp, string>   (Phase 252)

// `actor` is a typed `Actor` (`Human "alice"` / `Agent("model","ver","id")`) — folded into the
// hash since Phase 320 (`0.0.1-alpha.13`), so attribution is tamper-evident. A pre-320 stream migrates via
// `fromJsonlLegacyActor` + `rehash` (see docs/migrations/0.0.1-alpha.13-typed-attested-provenance.md).
OpStream.append OpStream.defaultHash streamW actor op state recs   // hash-chained
OpStream.replay streamW state0 records                              // deterministic
OpStream.verifyChain OpStream.defaultHash streamW records          // tamper-evident
OpStream.fromJsonl streamW jsonl  : Result<_, string>              // portable — runs in-browser
```

## 4. Offer the domain as something to invoke

Steps 1 to 3 make your tree editable through Core. The invocable seams make it *callable*: a model
or a UI invokes a typed capability or runs a typed query, and your host supplies the body. The
shape is the same — **declare → certify at your own seam → dispatch through the path you
certified** — and [`samples/adoption`](../samples/adoption/Program.fs) section 5 is the worked
example, end to end.

**An artifact with holes.** An `ArtifactWitness<'Node,'Id>` adds three things to your node witness:
`Holes` (each hole your tree declares, with its value space and its ADDRESS — the absolute id-path,
which your witness mints), `Effect` (the two-axis effect class) and `Bind` (lower one argument to
your own edit). `Function.signature w name tree` derives the signature a caller sees.

```fsharp
let artifactW : ArtifactWitness<Item,string> =
    { Tree = nodew; IdW = idw; Holes = holesOf; Effect = fun _ -> Effect.pureDeterministic; Bind = bind }

let fill = Capability.create "outline.fill" (Function.signature artifactW "fill-reading" template) Server
let capabilities = CapabilityRegistry.register fill CapabilityRegistry.empty       // Result: a duplicate id is refused
```

Arguments are keyed by the hole's ADDRESS (`"report/temp"`), never its name (`"celsius"`): that is
the hygiene law, and `Function.toJsonSchema`'s properties use the same keys, so a model sees the
address. A capability declares no result type — the body's `'v` is yours.

**A query registry.** A `Query` declares typed parameters (keyed by NAME — a query has no holes), a
result `Schema`, an effect class and a `DataSource`; `QueryRegistry.register` adds it. Your resolver
answers with a `QueryResult` whose `Rows` table has that schema.

**The three outcomes.** A body (or resolver) answers in `Deferred<'T>`: `Ready v`, `Pending` (not
answered yet — the host correlates the later answer, for instance by `invocationKey`) or
`Failed m`. Dispatch returns `Result<Deferred<'v>, InvokeError>` (`QueryError` for a query), so a
call has exactly three outcomes: **settled** `Ok(Ready v)`, **pending** `Ok Pending`, **refused**
`Error e`, typed. A body's `Failed m` arrives as the refusal `BodyFailed m` (`ExecutionFailed` for a
query); `Ok(Failed _)` never escapes. `InvokeError.describe` / `QueryError.describe` turn a refusal
into one sentence a model can act on.

**Refused by default.** There is no deny flag: default deny is the registry's shape. A call reaches
your body only when its id is registered AND its arguments validate against the signature (in
space, no stray key, every required hole bound); every other call is refused before the body runs,
and an unregistered id is `NoSuchCapability` naming the registered ids.

**Certify, then re-express.** `capabilityLaws` / `queryLaws` certify Core's own fixtures and cannot
see yours. Hand the kit your registry, your body, your HOST path and a generator of the calls a
model could make:

```fsharp
let capabilitySeam : CapabilitySeamWitness<string> =
    { Registry = capabilities
      Body = fun args _ () -> fillBody args
      Dispatch = CapabilityRegistry.dispatch capabilities   // the path your surface really calls
      GenCall = genCall }                         // settled, pending and refused calls

Conformance.capabilityLawsAt capabilitySeam seed iters   // and queryLawsAt for a QuerySeamWitness
```

The family certifies the three outcomes, that a refusal runs no body, and that your host refuses
exactly what the registry refuses — and it reports itself starved, not green, if your generator never
reaches one of settled, pending or refused. Then dispatch through the very path you certified.

## The caveats the first adoption surfaced (read these before you start)

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
  the hashes; a domain with persisted streams migrates by sealing its converted state and continuing from
  it — a linear stream through `OpStream.Snapshots`, a lane DAG through `Dag.sealAt` (Phase 288), which
  seals an imported state without replaying the history that produced it.
- **F5 — typed actors.** Core's op-stream actor is the typed `Actor` (`Human` / `Agent`) since Phase
  320 (`0.0.1-alpha.13`), folded into the hash as step 3 shows; map your own actor type onto it at the seam.
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

## What the declarative surfaces express

Before writing a host-side convention for an intent, read whether Core already states it, or has
decided not to. [`demand-census.md`](demand-census.md) carries the full grid with the file:line or
the decision behind each verdict (Phase 398, DECISIONS.md D128). The short form:

- **A query filters and orders, declared.** `Query.Where` is a closed conjunction of typed column
  predicates — `EqualTo`, the four range bounds (`GreaterThan`, `AtLeast`, `LessThan`, `AtMost`),
  `Contains` on a string column, `IsNull` / `IsNotNull` — and `Query.OrderBy` a column list with a
  direction each. Both are empty by default and then absent from the wire, so a declaration without
  them encodes exactly as before. A predicate's literal is a cell of its column's own type, and the
  registry and the declaration reader refuse anything else by name. Your resolver reads both from the
  `Query` it is handed; if it cannot apply one, it answers `ResolveFault.PredicateUnsupported` /
  `OrderUnsupported` and the caller receives `PredicateNotHonoured` / `OrderNotHonoured` — never
  answer rows the filter was not applied to. The capture key sees both, so a replay never answers
  one filter's rows for another's.
- **Pagination** is expressible: `PageSize`, the page token on input (`Query.invokePage` and the
  registry's paged dispatchers) and `NextPageToken` on the result.
- **Host-side by design on a query:** membership in a set (a disjunction — take the set as a
  parameter), string building, date arithmetic, and aggregation over the result. A query declares what
  to fetch, not what to compute.
- **The pattern bank** matches `contains` (an anchor's literal segments, in order, case-insensitively)
  and alternation between whole anchors (a pattern carries several; any one selects it). It does not
  capture: a `{…}` wildcard reads nothing into `Emit`, so a value in the sentence reaches your `Emit`
  only through the `Args` your host parses. Membership, null tests, date deltas and string building
  are your `Emit`'s.
- **The column layer** answers null tests (`Cell.isNull`), aggregation over a column
  (`Column.aggregate`) and THE cell order (`Cell.compare`). Scalar functions and transforms — substring,
  concatenation, date deltas, sorting and filtering as operations — are the compute repository's (see
  "The dataframe path" above).

## Verify

`./verify.ps1` builds the sample and runs its conformance report as part of the green gate. A clean
adoption prints `conformance: GREEN`.

## See also

- [`samples/adoption/Program.fs`](../samples/adoption/Program.fs) — the runnable template and the
  worked example, the invocable seams included.
- [`STABILITY.md`](../STABILITY.md) — which witness surfaces are stability-critical.
- [The dataframe path](#the-dataframe-path) — where `DataFrame`, `Column.Ops`, their law families,
  the dataframe facade and the incremental-evaluation guide live since Phase 258.
- [`construct-then-encode.md`](construct-then-encode.md) — certifying the authoring surface (step 2b
  above): why a codec round trip cannot see a widened builder, and how to answer for the family.
- [`../DECISIONS.md`](../DECISIONS.md) D28 — why the C# facade of step 2c shipped on one consumer,
  the Phase 231 re-measurement that removed it, and what would bring a C# veneer back.
