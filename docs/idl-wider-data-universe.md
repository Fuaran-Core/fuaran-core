# The IDL's wider data universe

Phase 639 (DECISIONS.md D251) added eight general value types to the IDL. Each has ONE canonical JSON
spelling: a reader refuses every other spelling, and the encoder refuses an authored value that is not
canonical rather than repairing it. This page is the specification every host implements; the conformance
vectors in [`conformance/idl/wider-universe.json`](../conformance/idl/wider-universe.json) hold the hosts to
it, and `ScalarText` is the reference's code for the five string-borne grammars and the order.

| Type | `idl.json` | JSON form | The one spelling | F# | TypeScript |
|---|---|---|---|---|---|
| 64-bit integer | `{"$type":"int64"}` | string | `-?(0\|[1-9][0-9]*)` in [-2^63, 2^63-1], no `+`, no `-0` | `int64` | `bigint` |
| decimal | `{"$type":"decimal"}` | string | `DecimalText`'s canonical form: no leading zero, no trailing fraction zero, no point where the fraction is zero, no sign on zero, no exponent; any precision | `string` | `string` |
| bytes | `{"$type":"bytes"}` | string | RFC 4648 §4 base64 with padding, every unused bit zero | `byte[]` | `Uint8Array` |
| time of day | `{"$type":"timeOfDay"}` | string | `HH:MM:SS` (24-hour), then `.F` only when the fraction is not zero: one to seven digits, the last not zero | `string` | `string` |
| duration | `{"$type":"duration"}` | string | seconds, `-?S(.F)?s`; `S` without a leading zero; `F` as for a time of day; no `-0s`; the magnitude in 100 ns ticks fits a signed 64-bit integer | `string` | `string` |
| tuple | `{"$type":"tuple","of":[…]}` | array | one item per component, in order; at least two components | a tuple | a tuple |
| set | `{"$type":"set","of":…}` | array | the elements in canonical order, none repeated | `Set<_>` | an array |
| keyed map | `{"$type":"keyedMap","keys":…,"values":…}` | array of `[key, value]` | the entries in canonical order of their keys, none repeated | `Map<_, _>` | an array of pairs |

**Why a 64-bit integer is a string.** A JSON number read by a JavaScript host is a double, which holds an
integer exactly only to 2^53, and the reference's `JVal` holds a 32-bit integer; a numeric spelling would round
in one host and overflow in another.

**Why the exact text-borne types bind to text.** No host decimal type holds every decimal, and under Fable a
time or a duration is a millisecond float. The canonical text is exact in every host; a host value that is not
canonical text is written as it is and refused by every reader.

**The duration at each precision.** The fraction is the shortest that states the value — `1s`, `1.5s`,
`0.0000001s`, never `1.0s` or `1.50s` — and stops at seven digits, the 100 ns tick, which is the finest a .NET
host holds; an eighth digit is refused everywhere, so no host reads a value another cannot hold. A duration is
signed and has no calendar unit.

**Canonical order.** A set's elements and a keyed map's keys stand in ascending order of their canonical JSON
texts compared by Unicode code point, which is the order of the texts' UTF-8 bytes. A UTF-16 host compares
code units except where one side holds a surrogate and the other a unit at or above U+E000, which it remaps.
A reader refuses an item whose text is not above the one before it: an equal text is `OutOfRange` ("a distinct
element") and a lower one `OutOfRange` ("an element ordered after …"), at the later item (for a keyed map, at
the entry's key). The element (key) type must be ORDERABLE: a string, an `int` or `int64`, a boolean, an enum,
a decimal, bytes, a time of day, a duration, or a tuple of those. A keyed map's key is never a string (that map
is `map`). On an orderable type the canonical text is injective, so the order is a strict total order on
values (DECISIONS.md D251.8).

**What a reader refuses.** A string-borne scalar given another string is `OutOfRange`, and given another kind
`WrongKind`; a tuple of the wrong length is `OutOfRange`; a keyed map entry that is not a two-item array is
`OutOfRange` (`WrongKind` when it is not an array); a set or keyed map out of order or repeated is
`OutOfRange`.

**Not admitted: a date and time without an offset.** A reader cannot know which instant it means. A time in a
contract is an instant, which a `hosted` slot with format `date-time` (offset required) already is; a
wall-clock date and time is a record of a `date` and a `timeOfDay`; a day of the week is an enum declared by
its case names. `idl.json` refuses `{"$type":"localDateTime"}` as `NotAdmitted`, with that reason.

**Where a target stops.** JSON Schema states each scalar by an exact pattern, a tuple by `prefixItems` and a
set's distinctness by `uniqueItems`; it has no vocabulary for an order over items, so the canonical order is
each reader's to check. The F\* model refines each string-borne scalar by its grammar, models a tuple of leaf
types as an F\* tuple (and refuses one holding a declared type by name), and models a set and a keyed map as
their wire arrays, the order outside a model whose numbers are opaque. A declared default at one of these types
is refused by every generator.
