# The oracle protocol

`fuaran-core-oracle` runs this repository's extracted proof models as a process. A host's fuzzer
(`cargo-fuzz` or `proptest`, Go's `testing.F`, Hypothesis) can ask the models about inputs nobody
chose, and compare each answer with its own port. That gives the host a differential against the
model, whatever language it is written in.

**What answers.** Every answer comes from a model extracted from the F\* proofs in
[`proofs/`](../proofs/README.md), and from nothing else. The CLI references the oracle assembly
(`proofs/oracle/`) and no production project. The suite's `Oracle CLI` family holds that on the
project as MSBuild evaluates it and on the CLI's sources, and it fails on a planted reference to
production.

This page is the contract. Protocol version: **1.0**.

## Getting the oracle

Each `v<Version>` release of this repository carries one self-contained binary per platform:
`fuaran-core-oracle-<Version>-linux-x64`, `-linux-arm64`, `-osx-arm64` and `-win-x64.exe`. It also
carries the manifest `fuaran-core-oracle-<Version>.sha256`, in `sha256sum` format. A binary needs no
.NET and no ICU on the host.

Pin the binary the way the proof kit is pinned: the asset's url and its sha256, committed in your
repository. Verify the download against the pin before the first request, and refuse a mismatch. On
POSIX, mark the downloaded file executable.

```sh
curl -fsSLo oracle "https://github.com/Fuaran-Core/fuaran-core/releases/download/v<Version>/fuaran-core-oracle-<Version>-linux-x64"
echo "<pinned sha256>  oracle" | sha256sum -c - && chmod +x oracle
```

The oracle belongs in your test and fuzz tooling only. It never belongs on a host's runtime surface.
[RELEASING.md, "The oracle binaries"](../RELEASING.md#the-oracle-binaries) says how the assets are
built and checked. DECISIONS.md D208 records why the oracle ships as a binary and not as a `dotnet` tool.

## Framing

- **Requests and responses.** The oracle reads requests on standard input and writes responses on
  standard output. Each request is one line of UTF-8 holding one JSON value, ended by LF (a CR before
  the LF is ignored). Each request gets exactly one response line, in request order, flushed as it is
  written. A harness can therefore write one request and block on one line.
- **End of input.** When standard input closes, the oracle answers any final unterminated line and
  then exits 0.
- **Process death.** Any other exit, or a missing response, means the oracle failed. It is never an
  answer.
- **Responses are canonical JSON.** There is no whitespace. Members are sorted by ordinal comparison
  of their names. A string escapes only `"`, `\`, the controls below U+0020, and an unpaired
  surrogate, each control and surrogate as a lower-case `\u` escape. Numbers are integers.
- **Requests are read strictly.** The reader follows RFC 8259, with two exceptions:
  - A `\u` escape may name an unpaired surrogate. The string then holds that unit, which is how you
    send an ill-formed string.
  - An object that repeats a member name is refused.

  Nesting deeper than 1024 is refused.

```
{"id":<any JSON value, optional>,"fn":"<function>","input":{...}}
```

A request has three members:

- `id` is echoed in the response, or `null` when it is absent.
- `fn` names the function.
- `input` is the function's input.

Any other member makes the request malformed.

A response has exactly one of two shapes:

```
{"answer":{...},"id":...,"protocol":"1.0"}
{"error":{"code":"...","message":"..."},"id":...,"protocol":"1.0"}
```

## "The model refuses this input" is an answer; "the oracle failed" is an error

**`answer`**: the model ran and this is what it says. When the model *refuses* the input (a
malformed document, an unknown node, a broken chain), that refusal is an answer like any other.
Your port must refuse in the same way. Each answer is a one-member object, and its member names the
model's case, for example `{"parseRefused":{...}}` or `{"applied":{...}}`.

**`error`**: no model answered. There are four codes, and a harness counts each as "no verdict",
never as a disagreement:

| `code` | Meaning |
|---|---|
| `malformed-request` | The line is not UTF-8, is not one JSON value, or is not a request object with a string `fn` and an `input`. |
| `unknown-function` | `fn` names no function in this protocol version. |
| `bad-input` | `input` does not fit the function's declared shape (a missing or undeclared member, a wrong type, or a count above a protocol limit). The message gives the path, for example `input.lanes[1][0].node`. |
| `internal` | The oracle raised an exception. This is a defect in the oracle, and is never a verdict on the input. |

## Versioning

Every response carries `"protocol":"<major>.<minor>"`. `oracle.describe` returns the protocol
version, the oracle's release version, and the functions with the models each one asks.

- **Minor bump.** Adding a function moves the minor. A caller of the old functions is unaffected.
- **Major bump.** Changing anything about an existing function moves the major. That covers its
  input, an answer case added or removed, a member renamed, or a different meaning.
- **Pinning.** A harness pins the major. It checks `oracle.describe` for any function newer than its
  pin.
- **Release version.** The oracle's release version moves with every release. The protocol version
  does not move with it.

## Functions

Each function names the model it asks, and every answer case that model has. **What the CLI
supplies** is listed where a model takes a host parameter. The model leaves those parameters to its
host, and the CLI instantiates each one over the base library alone. Each instantiation is the one
the suite's own differential uses, or an independent implementation of the named algorithm.

### `oracle.describe`

- **Input:** `{}`.
- **Answer:** `{"description":{"protocol":"1.0","oracle":"<Version>","functions":[{"name":...,"models":[...]}]}}`.

### `json.parse`, the limited JSON parser (`JsonParse.parse_limited`)

**Input:**

```
{"text":"<document>","nulls":"reject"|"eraseMember","limits":{"depth":n,"stringLength":n,"arrayLength":n,"objectMembers":n,"documentBytes":n}}
```

`text` is required. Every other member is optional:

- `nulls` defaults to `reject`. Under `eraseMember`, a `null` member value is dropped.
- `limits.depth` defaults to 512.
- Every other limit defaults to unbounded.

**Answers:**

- `{"value":V}` — the parsed value in the tagged form below. Numbers keep the token the model kept.
  An integer that fits 32 bits is `int`. Every other number is `float`, including an integer past
  32 bits that is safe in a double.
- `{"parseRefused":{"kind":K,"message":M,"at":n}}` — `K` is the model's error kind:
  `UnexpectedChar`, `UnexpectedEndOfInput`, `ExpectedToken`, `UnterminatedString`,
  `UnterminatedEscape`, `TruncatedUnicodeEscape`, `BadEscape`, `BadHexDigit`, `MalformedNumber`,
  `NullNotRepresentable`, `MaxDepthExceeded`, `TrailingCharacters`, `DuplicateMember` or
  `LimitExceeded`. `n` is the UTF-16 index the model stopped at.
- `{"parseLimit":{"limit":L,"at":n}}` — `L` is one of `depth`, `stringLength`, `arrayLength`,
  `objectMembers` or `documentBytes`.

**The tagged value form** (shared with `wire.render`):

- `{"string":s}`
- `{"int":"<token>"}`
- `{"float":"<token>"}`
- `{"bool":b}`
- `{"array":[V,...]}`
- `{"object":[{"key":k,"value":V},...]}`, with members in document order

**What the CLI supplies:**

- **The unit classes.** Each UTF-16 unit maps to the model's character class: the twelve JSON
  structural and whitespace characters, the digits and the letters the grammar spells, a raw high
  or low surrogate, a raw control, and any other unit.
- **The float reader.** A finite double reads as `FCanonical` when its round-trip `R` text equals
  the token, and `FFinite` otherwise. A non-finite one reads as `FNonFinite`, and anything else as
  `FUnparsable`.
- **The document-size verdict.** This is the UTF-8 length, with an unpaired surrogate counting as the
  three bytes of its replacement.

### `wire.decode`, a node from a document (`JsonParse.parse_limited`, then `WireDecode.decode_node`)

**Input:** the same as `json.parse`.

**Answers:**

- Any `parseRefused` or `parseLimit` answer, as for `json.parse`.
- `{"decodeRefused":{"message":M}}` — the decoder model's message, for example `unknown kind: zzz` or
  `missing property: kind`.
- `{"node":N}` — the node in the model's own wire encoding:
  - `{"kind":"text","value":s}`
  - `{"kind":"flag","on":b}`
  - `{"kind":"tags","tags":[s,...]}`
  - `{"kind":"group","id":s,"items":[N,...]}`

### `wire.render`, the guarded canonical renderer (`WireCanon.try_render`)

**Input:** `{"value":V}`, in the tagged value form. Here `float` also takes `NaN`, `Infinity` and
`-Infinity`. A string or key may hold an unpaired surrogate, and an object may repeat a key. These
are the inputs a document cannot carry and the renderer must refuse. `int` must fit 32 bits.

**Answers:**

- `{"rendered":"<canonical text>"}`
- `{"refusedNonFinite":{"path":P,"value":"NaN"|"Infinity"|"-Infinity"}}`
- `{"refusedString":{"path":P,"string":s,"isKey":b}}`
- `{"refusedRepeated":{"path":P,"key":k}}`

`P` is a path from the root. A number is an array index and a string is a member key.

**What the CLI supplies:**

- An integer is written as its decimal text, and a finite double as its round-trip `R` text.
- `-0` is written as `0`.
- Keys are ordered by ordinal UTF-16 comparison.
- A string is well-formed exactly when a strict UTF-8 encoder accepts it.

### `tree.apply`, one op on a tree (`TreeOps.apply`)

**Input:** `{"tree":T,"op":O}`.

- **`T`** is `{"id":s,"kind":s,"children":[T,...]}`. `children` is optional and defaults to `[]`.
- **`O`** is one of:
  - `{"op":"insertChild","parent":s,"node":T}`
  - `{"op":"removeNode","node":s}`
  - `{"op":"moveNode","node":s,"newParent":s}`
  - `{"op":"reorderChildren","parent":s,"order":[s,...]}`
  - `{"op":"batch","ops":[O,...]}`
  - `{"op":"updateNode","node":T}`

**Answers:**

- `{"applied":T}`
- `{"rejected":R}`, where `R` is one of:
  - `{"rejection":"unknownNode","target":s,"addressable":[s,...]}`
  - `{"rejection":"duplicateId","id":s}`
  - `{"rejection":"cannotRemoveRoot"}`
  - `{"rejection":"wouldNestUnderSelf","node":s}`
  - `{"rejection":"notAContainer","node":s,"kind":s}`
  - `{"rejection":"reorderMismatch","parent":s,"current":[...],"requested":[...]}`
  - `{"rejection":"rejected","code":s,"message":s}` (only from the checked apply `dag.fold` uses)

### `chain.verify`, the first break in a hash chain (`Chain.first_chain_break`)

**Input:**

```
{"hash":"fnv1a"|"sha256","genesis":s,"records":[{"seq":n,"actor":s,"op":s,"prev":s,"hash":s},...]}
```

- `genesis` is optional and defaults to `""`.
- `actor` and `op` are the record's encoded text, spliced into the pre-image as they stand. The
  pre-image is `{"seq":<n>,"actor":<actor>,"op":<op>}`.

**Answers:**

- `{"intact":{}}`
- `{"broken":{"index":n,"reason":R,"expected":s,"got":s}}`, where `R` is one of
  `sequence-number mismatch`, `prev-hash link broken` or `hash mismatch (tampered op/actor/seq)`.

**What the CLI supplies** is the hash over `prev + "|" + pre-image`. Both options are written from
the algorithm's definition:

- `fnv1a` is FNV-1a 32 over the UTF-16 units, as eight lower-case hex digits.
- `sha256` is SHA-256 over the UTF-8 bytes, as lower-case hex.

### `dag.fold`, lanes folded onto a base (`DagFold.fold_once`, at `TreeOps.wapply` and `TreeOps.op_fp`)

**Input:** `{"base":T,"lanes":[[O,...],...]}`.

**Answers:**

- `{"folded":T}`
- `{"halted":[{"left":O,"right":O,"address":s,"shape":S},...]}` — conflicts in the model's order.
  `S` is one of `concurrent-update`, `insert-position-clash`, `move-vs-remove` or
  `slot-clash:<slot>`. Compare them as a set, because the order follows the lanes.
- `{"rejected":R}` — the merged script was refused.
- `{"lanesRejected":[[O,...],...]}` — the lanes that fail when replayed alone from the base.

The fold model halts every pair of tree lanes whose merged script it would refuse. So `rejected` is
the model's case, and no tree input is known to reach it.

## Limits

- Nesting in a request is limited to 1024 levels.
- Every count the protocol turns into one of a model's unary bounds is limited to 1,048,576: a
  depth, a string, array or member limit, or a sequence number. A larger count is `bad-input`.
- The models recurse structurally. The oracle runs them on a 512 MB stack, which is ample for any
  input inside these limits.

## Checking a harness against this page

[`tests/oracle-smoke.py`](../tests/oracle-smoke.py) is a standard-library Python client of this
protocol. It covers every function, every error code, a line that is not UTF-8, and `--version`.
Run it against a binary to see a working harness:

```sh
python tests/oracle-smoke.py ./fuaran-core-oracle-<Version>-linux-x64
```
