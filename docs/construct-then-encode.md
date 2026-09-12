# Certifying the authoring surface — `Conformance.constructThenEncodeLaws`

**Family name:** `Conformance.constructThenEncodeLaws` (Phase 126, `0.22.0`). That string is what a
consumer's conformance census carries, so it is the name to answer for — adopted, or not used with a
reason.

## The gap this closes

A domain's conformance today is `encode (decode b) = b` over its corpus. That certifies the
**codec** and says nothing about the **authoring surface** a program actually writes against: the
smart constructors, the builders, the fluent factory. They are a different function into the same
type, and a round-trip suite never calls them — it starts from bytes and ends at bytes, and the
constructors are not on that path.

The gap is not hypothetical. In the `@fuaran-ui/ui` 0.26.0 release (2026-09-11, recorded as
fuaran#1661) one field widened in memory to a richer carrier. The decoder-encoder suite stayed green
over thousands of vectors — every widened value encoded and decoded perfectly — and the only
author-direction consumer in the estate broke on the pin bump, because what an author now *built*
was not what the corpus *said*.

So this family runs the corpus **through** the authoring surface: decode a document, rebuild it with
the domain's own constructors, re-encode, and compare.

## Adopting it

Supply a `ConstructWitness<'T>` beside the codec you already have:

```fsharp
open Fuaran.Core

/// Rebuild a decoded value THROUGH the constructors an author calls — never through the decoded
/// record (`{ n with Children = … }` would certify nothing; the point is that the author-facing
/// surface is on the path).
let rec construct (n: Item) : Result<Item, string> =
    if List.isEmpty n.Children then
        Ok(Item.leaf n.Id n.Kind n.Value)
    else
        n.Children |> traverse construct |> Result.map (Item.node n.Id n.Kind)

let witness: ConstructWitness<Item> =
    { Surface = "the Item smart constructors"   // named for the report a human reads
      Construct = construct }

Conformance.constructThenEncodeLaws "my domain" myCodec (Some witness) myCorpus
```

`myCorpus` is the `Corpus.Case` list you already run the codec against — `Reject` cases are not
documents and are skipped, so nothing new has to be written to adopt this.

Three laws come back:

1. **non-vacuity** — the corpus offers at least one round-trip document. A family with no document
   certifies nothing and would otherwise report the same green as one that certified a thousand.
2. **acceptance** — every document decodes, and the authoring surface accepts the decoded value. A
   constructor that *refuses* a value the domain's own codec just produced is a finding about the
   surface, and it is deliberately separate from the next law so a red is never misattributed.
3. **the law** — `encode (construct (decode b)) = encode (decode b)` for every document: what an
   author builds encodes as what the codec decoded. When it reddens, the counterexample also states
   whether the plain codec round-trip passes over that same document — it usually does, and that is
   the whole finding.

### Why the right-hand side is `encode (decode b)` rather than the bytes `b`

The law reads naturally as `encode (construct (decode b)) = b`, and on a corpus whose documents are
written in their codec's own canonical form that is exactly what it computes. But a `Corpus.Case`'s
JSON is not *required* to be canonical — `Corpus.roundTrip` compares values, so a legal corpus may
spell a document with a different key order or spacing. Comparing against the literal bytes would
then redden on the corpus's formatting rather than on the authoring surface. Comparing against the
codec's own encoding of the same decoded value isolates the one subject this family has, on any
corpus.

## Not adopting it

A domain that supplies no witness is reported **by name**:

```
construct-then-encode (the UI tier): NOT ADOPTED — no ConstructWitness supplied
```

and that result is **not passed**. `LawResult` has two states and no third, and widening it would be
a compile-breaking change for every consumer that constructs one, so the honest reading is the one
the kit reports: a family asked to certify an authoring surface it was never given has certified
nothing.

That means running the family with `None` is not a way to stay green — it is a way to say out loud
that the surface is uncertified. A domain that has decided the family does not apply records it as a
reasoned non-use in its own census row instead, exactly as it does for any other family whose
subject it does not have.

## See also

- [`ADOPTION.md`](ADOPTION.md) — the whole adoption path, of which this is step 2b.
- [`../STABILITY.md`](../STABILITY.md) — "Construct-then-encode: the authoring surface certified".
