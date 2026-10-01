namespace Fuaran.Core

/// A recorded non-deterministic effect value at a session boundary (Phase 27). The op-stream's
/// hash chain proves the *shape* of a stream was not altered, but replay of an impure effect
/// re-reads the live source — so a clock read / RNG draw / network or tool response evaluated
/// during a session is not reproducible from the file. An `EffectCapture` closes that gap: it
/// records the realized value at the boundary so replay feeds it back instead of re-evaluating,
/// hash-chained exactly like an `OpRecord` so a tampered capture fails `verifyCaptures`.
///
/// `Seq` is the capture's index in its own append-only chain; `Eff` is a stable effect-identity
/// key (which boundary — so the seed-injection helper can find a capture); `Determinism` is the
/// `Fuaran.Core.Function` determinism tag *label* (`"clock"`, `"clock+random"`, … — the member
/// factors in canonical order joined by `+`) — this layer sits below `Function` and stays
/// FSharp.Core-only, so it keys on the label, not the set (a consumer threads
/// `Effect.determinismTag` in). `Value` is the realized value through the domain `Codec` (raw wire
/// JSON, embedded verbatim like an op payload), so the journal round-trips byte-for-byte. A
/// `Deterministic` effect emits no capture, so the determinism label is always a non-deterministic
/// label: one or more factors.
type EffectCapture =
    { Seq: int
      Eff: string
      Determinism: string
      Value: string
      PrevHash: string
      Hash: string }

/// A signed checkpoint over a chain head (Phase 320). `Head` is the hash being attested — a chain
/// `Hash` at a commit / publish boundary. The hash-chain already attests the *whole prefix* (each
/// `Hash` folds in its `PrevHash`), so signing the head is O(commits), not O(ops): one signature
/// covers every op up to that point. `KeyId` names the signing key; `Signature` is the host's
/// opaque attestation token (hex / base64). Verification re-checks the signature against the head —
/// Core owns the *seam*, the host owns the crypto.
type Attestation =
    { Head: string
      KeyId: string
      Signature: string }

/// The cryptographic-attestation seam (Phase 320), following the default-no-op portability-interface
/// pattern (mirrors `IFuaranTelemetrySink` et al.). Core stays FSharp.Core-only + Fable-clean: the
/// interface compiles under Fable, while the real KMS / HSM signing lives host-side behind it.
/// `Sign` attests a chain head at a commit / publish boundary; `Verify` re-checks an attestation
/// against a head. Signing is **opt-in, never mandatory** — the default `OpStream.noAttestation`
/// signs nothing, so the un-attested path behaves exactly as before.
type IAttestationSink =
    /// Attest a chain head. Returns `Some` signed `Attestation`, or `None` for the no-op sink.
    abstract member Sign: head: string -> Attestation option
    /// Re-verify an attestation against the head it claims to cover. `false` if the signature does
    /// not check out (or for the no-op sink, which never issued one).
    abstract member Verify: attestation: Attestation -> head: string -> bool
