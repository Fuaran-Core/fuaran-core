# Security Policy

## Supported versions

Fuaran.Core follows semantic versioning from `1.0.0`. Security fixes are applied to the latest
minor of the current major, on the `main` branch. Earlier minors, and the `0.x` line, are not
maintained.

## Reporting a vulnerability

Please report suspected vulnerabilities privately — do **not** open a public issue.

- **Preferred:** GitHub's private vulnerability reporting (the repository's **Security** tab →
  **Report a vulnerability**).
- **Or email:** andrew@fuaran.com — include a description, the affected version, and steps
  to reproduce.

We aim to acknowledge a report within five business days and to agree a disclosure timeline with
you. Please allow a reasonable window to ship a fix before any public disclosure.

## Scope — what is and isn't a vulnerability

Fuaran.Core is a **library of pure, generic functions** with no I/O, no network, no process model,
and no ambient authority (FSharp.Core-only; see [`STABILITY.md`](STABILITY.md)). Its threat surface
is correspondingly narrow. Two documented, by-design properties are **not** vulnerabilities:

- **The default hash chain is not cryptographic.** `OpStream.defaultHash` is FNV-1a — fast,
  portable, Fable-clean, and *tamper-evident against accidental corruption only*. Anyone who edits a
  record can recompute the chain, so a re-hashed forgery under the default hash is **expected**, not
  a defect. For adversarial tamper-evidence, supply a cryptographic `HashFn` (e.g. SHA-256) at the
  host boundary — the seam exists for exactly this. See "Hash-chain integrity posture" in
  [`STABILITY.md`](STABILITY.md).
- **Encoder injectivity is a caller precondition.** `Function.applyMemo`'s cache-key soundness
  depends on the caller supplying an injective node encoder; a colliding encoder that serves a wrong
  cached value is a **caller** defect. Certify yours with `Conformance.encoderInjectivityLawsAt`.

## What is bounded, and what is the host's to bound

- **Nesting is bounded.** `Json.parse` refuses text nested past `Json.defaultMaxDepth` (512) by name
  (`MaxDepthExceeded`), and `Json.parseWith` / `parseDetailedWith` take the cap as an argument, so a host
  that wants a tighter one passes it. The renderers cap nothing: a value built in memory is not untrusted
  wire data, and they use an explicit stack rather than the thread's.
- **Size is not bounded by Core.** String length, array length, node count and document bytes are the
  host's to limit before the text reaches the parser (DECISIONS.md D84: Core is not a host of the UI wire
  format's section 21 limits, which is why the proofs model keeps them only as named premises in
  `proofs/Limits.fst`). Unbounded input size is therefore not a vulnerability in Core; a host that accepts
  untrusted documents must cap their size itself.

Genuine issues we want to hear about include: a totality violation (a public function throwing
instead of returning a typed error), a decode path that admits malformed wire as valid, parser
resource-exhaustion of the nesting bound (a document the parser reads that exhausts the stack or the
heap in spite of the depth cap), or any `Conformance` law that is itself unsound.
