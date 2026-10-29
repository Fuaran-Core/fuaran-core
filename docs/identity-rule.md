# The identity of a declared record

**The rule.** The identity of a record declared through a vocabulary is the content address, under a
named encoding profile, of its canonical form under that vocabulary:

```
identity(record, vocabulary, profile) = (profile, ContentAddress.ofValue profile v)
    where v = the value the vocabulary's canonical text of the record reads as
```

The canonical form fixes three things, so two hosts or two components holding the same declared values
mint one `sha256:` address and any change to a member changes it:

1. **Members in canonical order.** The order is the vocabulary's declared key order: Ordinal by default
   (`KeyOrder.Sorted`), declaration order under `KeyOrder.Declared`. The order an author wrote the
   members in does not matter, because a host reads the record through the vocabulary before it mints.
2. **Absent optional members are omitted.** An `Optional` member that has no value is not written. Being
   present and being absent are two different records.
3. **Defaults are not materialised.** An `OmitDefault` member holding its default is not written, so a
   record that states the default and one that leaves it out have one identity.

The profile renders `v`, and `ContentAddress` hashes the bytes: SHA-256 over their UTF-8 encoding, written
as `sha256:` and 64 lower-case hex digits. A host without this package computes the same digest from the
same bytes. Under `v2`, for a vocabulary in the default Ordinal key order, those bytes are the
vocabulary's own canonical text, so `ContentAddress.ofCanonicalText v2` of that text gives the same
address.

## What a host does

1. Read the record through its vocabulary (`Decode.decodeOp`, or the host's generated reader). Do not hash
   the text as it was received.
2. Take its canonical form (`Encode.encodeOp`, or the host's generated writer) and read that text as a value.
3. Render the value under the named profile and hash it (`ContentAddress.ofValue`).
4. Keep the profile with the address. The digest does not record which profile it was computed under. A
   value with no line feed, carriage return or tab in any string has the same digest under `v1` and `v2`,
   and a value with one has two different digests. So the identity is the pair of the profile and the
   address.

## The rows it inherits

The rule composes parts Core already ships. It proves nothing new about them:

- **`canonical-digest-preimage-injective`** (proved, `proofs/WireCanon.fst`): over values whose strings are
  well-formed, equal UTF-8 bytes of the cross-host rendering mean equal normal forms. Under `v2`, in the
  default key order, the bytes hashed are that rendering, so two records that mint one address have one
  canonical value, unless SHA-256 has collided.
- **`canonical-render-guard`** (proved): `Canonical.tryWrite` refuses a non-finite float, an ill-formed
  string and a repeated member. This is why `ContentAddress.ofValue` refuses to mint for such a value.
- **`decimal-text-one-number-one-text`** (proved, `proofs/DecimalText.fst`): two decimal texts denote one
  number exactly when their canonical forms are one text. A vocabulary that writes a number as decimal
  text and admits only the canonical text has one text, and so one address, per number.

The rule itself is a `tested` row, `declared-record-identity`, sampled by the `identity` conformance
family (`conformance/identity/`) over a reference vocabulary.

## The family

`conformance/identity/identity.json` is a line table, one vector per line: a name, a profile, the record
as a host receives it (members in the order its author wrote them), its canonical text under the profile,
and its address. A host that certifies the rule reproduces every line. The vectors pin these relations:

| Pair | Addresses |
|---|---|
| members in declaration order / the same members permuted | one |
| a default left out / the same default written | one |
| an optional member absent / the member present with another value | two |
| a value / the same value one digit apart | two |
| a list of sources / the same sources in another order | two (a list is a sequence) |
| one value under `v1` / under `v2`, no control character in any string | one digest, two profiles |
| one value under `v1` / under `v2`, a string carrying a line feed | two |

The reference vocabulary is a **test fixture** (`tests/Fuaran.Core.Tests.Fixtures/IdentityIdl.fs`). It is
a `$type`-tagged record with a method digest, a list of source fingerprints, a value written as canonical
decimal text, and a unit that is omitted at its default `1`. It is a tagged root because the IDL
interpreter's public encoder has two roots, a node and a tagged value, and a node carries an id that an
identity cannot be computed over. The tag is one of the record's members, so two record types declared
with the same members do not share an address.

## What the rule does not claim

- **Nothing about what a record means.** Two records with one address hold the same declared values. That
  says nothing about whether they are the same fact in any domain's sense.
- **Nothing about which members a domain declares.** Choosing the members that make up an identity is the
  domain's decision. A member left out of the vocabulary is left out of the identity.
- **No vocabulary lives here.** Core ships no record type and no record vocabulary for any domain. Each
  domain declares its own vocabulary in its own repository and cites this rule.
- **Two vocabularies are not one vocabulary.** The canonical form is the vocabulary's. Two declarations of
  the same members agree only if they also agree on key order, tag, optionality and defaults.
- **The number form is the vocabulary's to fix.** A string member holding a number has one address per
  spelling, unless the vocabulary admits a single text per number, as the reference vocabulary does through
  `DecimalText`. A float member is written in the canonical float layout, which collapses a negative zero
  to `0` and spells a non-finite value as its quoted token, which the member's float type reads back.
- **SHA-256's collision resistance is assumed, not proved.** It is the same premise every content address
  in Core rests on.
- **`v1` carries no injectivity row.** Its short escapes for line feed, carriage return and tab are frozen
  for stores minted from `0.30.0` to `0.32.0`. A new identity should be minted under `v2`.
