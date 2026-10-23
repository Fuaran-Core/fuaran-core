# Host premises

The F\* models in [`proofs/`](../proofs/) are proved about values this implementation's runtime gives
it for free. A .NET string is a sequence of UTF-16 code units; a .NET double prints with the shortest
digits that read back to it; a 64-bit integer and a double both hold every integer up to 2^53; the
runtime's SHA-256 is FIPS 180-4; and its stack holds the parser's bounded recursion. A host that ports
this kernel to another runtime inherits the theorems only where its runtime gives it the same things,
and Rust, Go, Python and an embedded target each differ on at least one.

Each of those things is a **`host-premise`** row of the claims ladder ([`proofs.json`](../proofs.json);
the class is specified in [`proofs/kit/LADDER.md`](../proofs/kit/LADDER.md)), and each row names a
**probe**: a vector family under [`conformance/premises/`](../conformance/premises/). A host discharges a
premise on its own runtime by reproducing every vector of its probe. Nothing this repository runs
discharges a premise for another host, and a domain discharges none of them.

This page states each premise in plain language and what a Rust, Go or Python host does to answer it.
The rows are the authority: where this page and a row disagree, the row wins, and the `Proofs.Ladder`
family holds this page to name every `host-premise` row and its probe.

## Reading a probe

Every probe file has the same shape: a header naming the `family`, the `premise` (the ladder row) and a
`description` of what a vector is, then `vectors`, one compact JSON object per line. Every vector carries
an `id`, its input members, `expected` (the answer) and `answeredBy`. `["reference"]` means this
implementation computed the answer. `["reference", "model:JsonParse"]` means the extracted parser model
(`proofs/JsonParse.fst`) computed it too and agreed; a host that reproduces such a vector agrees with the
proved model, not only with this implementation.

[`conformance/premises/manifest.json`](../conformance/premises/manifest.json) indexes the families with
their vector counts and carries each host's `adoption` record. A host whose entry reads `proposed` has a
premise it has not answered, and reports it by name. A host records its answer in its own change, as it
does for every other family: `adopted` with the cases its leg asserted.

Every file is generated: `dotnet run --project tools/Fuaran.Core.Tools -- render premises` writes them
from the reference implementation, and the `Exporters` family holds the committed bytes to that render.

### `host-string-model`

**The premise.** A string is a sequence of UTF-16 code units, and only well-formed UTF-16 is text. A
high surrogate (`D800`–`DBFF`) followed at once by a low one (`DC00`–`DFFF`) is one code point; a high
surrogate with no low one after it, or a low one with no high one before it, is UNPAIRED, and a string
holding one has no code points and no UTF-8 encoding. This implementation's encoder refuses such a
string at the first unpaired unit, and its parser refuses a JSON string or member key that would hold
one, whether the surrogate is raw or spelled as a `\u` escape.

**The probe.** `premiseStringModel`, [`conformance/premises/string-model.json`](../conformance/premises/string-model.json).
`encode/*` vectors give the code `units` and the encoder's answer: the UTF-8 bytes as hex, or `refused`
with the `index` and `unit` of the first unpaired surrogate. `parse/*` vectors give a JSON `text` and the
strict parser's answer: the string's UTF-8 bytes, or the refusal's class (`BadEscape`).

**What a host does.**

- **Rust.** A `str` is always valid UTF-8, so an unpaired surrogate cannot be stored in one, and the
  encode vectors are answered at the boundary where code units become a `String`: `char::decode_utf16`
  yields an error carrying the unpaired unit, and the index is the number of units consumed before it.
  The JSON reader decodes an escaped pair into one `char` and refuses an escape that leaves a surrogate
  unpaired. A reader that substitutes U+FFFD fails the probe.
- **Go.** A `string` is arbitrary bytes, and both `unicode/utf16.Decode` and the standard
  `encoding/json` replace an unpaired surrogate with U+FFFD rather than refusing it, so a Go host checks
  the pairing itself and refuses. Because this package's readers take text, not bytes, a Go host also
  refuses a document whose bytes are not valid UTF-8 (`utf8.Valid`) before it parses; no vector carries
  ill-formed bytes, and the premise holds only if the host does.
- **Python.** A `str` is a sequence of code points and admits a lone surrogate (`"\ud800"` is a valid
  `str`), and a surrogate pair written as two code points is two lone surrogates, not one character.
  A Python host maps code units to text by decoding them as UTF-16 with the `strict` handler, which
  refuses at the unpaired unit. `json.loads` returns a lone surrogate rather than refusing it, so a
  Python host checks every decoded string and member key and refuses one holding a code point in
  `D800`–`DFFF`.

### `host-float-digits`

**The premise.** A finite double is written with the SHORTEST decimal digits that read back to the same
double, and that text reads back to the same double. `proofs/WireCanon.fst` takes the digit generator as
a premise (`float_round_trips`), so the canonical form's theorems hold for a host exactly when its
generator meets it. The digits are then laid out by the canonical float rule (WIRE_FORMAT section 2
rule 5): fixed point when the magnitude is at least 1e-4 and below 1e17, otherwise exponent form with a
capital `E`, a sign and at least two exponent digits (`1E-05`, `1E+17`), and `-0` written as `0`.

**The probe.** `premiseFloatDigits`, [`conformance/premises/float-digits.json`](../conformance/premises/float-digits.json).
Each vector gives `bits`, the binary64 bit pattern as 16 hex digits, and `canonical`, the text. The edges
are named; `sample/*` vectors are drawn by SplitMix64 from four strata.

**What a host does.** Write `bits` as exactly `canonical`, and read `canonical` back to `bits`, for
every vector. Each of the three runtimes has a shortest round-trip generator and a correctly rounded
reader, and none lays the digits out by this rule, so each host takes the digits and the decimal
exponent from its generator and re-lays them.

- **Rust.** `format!("{:e}", x)` gives the shortest digits in exponent form; `Display` never switches
  to exponent form. Read back with `str::parse::<f64>`.
- **Go.** `strconv.FormatFloat(x, 'e', -1, 64)` gives the shortest digits; the `'g'` layout switches
  at a different exponent and writes a lower-case `e`. Read back with `strconv.ParseFloat`.
- **Python.** `repr(x)` gives the shortest digits, but switches to exponent form at 1e16 and writes
  `1e-05`. Read back with `float()`.

### `host-int53`

**The premise.** An integer token is judged by its TEXT against 2^53 = 9007199254740992, never by the
double it would round to. A token in the 32-bit signed range is an integer; one beyond it and at most
2^53 in magnitude is the double it denotes, exactly; one past 2^53 is read only when it is that double's
canonical float layout, and is otherwise refused (`MalformedNumber`). The host's number type must hold
every integer up to 2^53 exactly, and its reader must tell 2^53 + 1 from 2^53.

**The probe.** `premiseInt53`, [`conformance/premises/int53.json`](../conformance/premises/int53.json).
Each vector gives a `token`, read as a whole document, and the answer: `int`, `float` (the bit pattern),
or `refused`. The answers are the reference's and the parser model's.

**What a host does.** Classify each token from its text, and never through a wider integer type or a
double first.

- **Rust.** A reader that parses into `u64` / `i64` reads a 19-digit id exactly, and the wire model
  has no such value: the host refuses it, by the token's digits, unless it is a canonical float layout.
- **Go.** `encoding/json` decodes a number into a `float64` by default, which reads 2^53 + 1 as 2^53;
  read the token's text (`json.Number`, or the host's own scanner) and classify it.
- **Python.** `int` is unbounded and `json.loads` reads 9007199254740993 exactly; the host refuses it
  all the same, and answers an integer beyond the 32-bit range and within 2^53 as a `float`.

### `host-sha256`

**The premise.** SHA-256 is FIPS 180-4 SHA-256 on every input length. Wherever this package names
SHA-256 (the crypto digest a chain or a DAG is minted under, and every digest a conformance vector
records as SHA-256), it is SHA-256 over bytes, and a host whose digest differs on an input reproduces none
of those there.

**The probe.** `premiseSha256`, [`conformance/premises/sha256.json`](../conformance/premises/sha256.json).
Each vector gives `input`, a hex byte string and how many times it repeats, and the digest. The lengths
sit on the padding boundaries (55, 56, 57, 63, 64, 65, 119 and 120 bytes), beside the FIPS 180-4
messages, every byte value and a million `a`. This implementation computes them with its own code, and
the export has checked every one against the runtime's SHA-256.

**What a host does.** Run its SHA-256 over the vector's bytes. The standard libraries (`sha2` in Rust,
`crypto/sha256` in Go, `hashlib` in Python) are FIPS 180-4; the probe is what shows that the host wired
its bytes the same way, and it is what an embedded host with its own or a hardware SHA-256 runs before
anything else.

### `host-recursion-depth`

**The premise.** The parser descends once per nesting level, and spends a depth budget before each
descent: at the default cap of 512, a document nested 512 deep is read, and one nested deeper is refused
by name (`LimitExceeded/depth`), however deep it goes. `proofs/JsonParse.fst` proves the bound exact.
What it cannot prove is that the runtime has the stack for 512 levels: that is the host's.

**The probe.** `premiseRecursionDepth`, [`conformance/premises/recursion-depth.json`](../conformance/premises/recursion-depth.json).
Each vector's text is `open` repeated `depth` times, then `inner`, then `close` repeated `depth` times;
an accepted document's answer is the SHA-256 of its canonical rendering, which is the text itself. The
depths are 511, 512 and 513 levels of arrays and of objects, and 100,000 levels, which must be refused
rather than overflow the stack.

**What a host does.** Run the probe on the SMALLEST stack it supports — an embedded target's main stack,
a worker thread's — because a run on a larger stack says nothing about a smaller one. A host whose stack
cannot hold 512 levels does not lower the cap to pass; it reports the premise unanswered.

- **Rust.** A spawned thread's default stack is smaller than the main thread's, and an embedded
  target's is smaller still. A reader with a lower built-in limit (`serde_json`'s default is 128)
  refuses the 512-deep vectors and fails the probe.
- **Go.** Goroutine stacks grow, so the stack is rarely the limit; the cap is. `encoding/json` admits
  nesting far past 512, so a Go host enforces the 512 cap itself.
- **Python.** The interpreter's recursion limit (1,000 frames by default) is the stack, and a parser
  spending more than one frame per level can reach it before 512 levels. A `RecursionError` is not the
  refusal: the host checks the depth before it descends.
