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

/// The bodies of the `OpStream` capture and attestation members (Phase 332): determinism capture /
/// replay and the attestation seam's default sink, signer and verifier. Internal: a consumer reaches
/// each one through its forward in `OpStream` (OpStream.fs), which carries the member's contract
/// and documentation.
module internal OpStreamCapture =
    open Fuaran.Core.OpStreamChain
    open Fuaran.Core.OpStreamJsonl

    // ---- determinism capture / replay (Phase 27) ----

    [<Literal>]
    let deterministicTag = "deterministic"

    /// The hash payload binding a capture to its chain — `{capture, seq, eff, det, value}`. The
    /// `value` is embedded as raw JSON (the domain `Codec` output), so it joins the chain hash
    /// byte-for-byte exactly as an op payload does.
    let private capturePayload (seq: int) (eff: string) (det: string) (value: string) : string =
        "{\"capture\":true,\"seq\":"
        + string seq
        + ",\"eff\":"
        + jstr eff
        + ",\"det\":"
        + jstr det
        + ",\"value\":"
        + value
        + "}"

    let captureEffectWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (encode: 'v -> string)
        (det: string)
        (eff: string)
        (effect: unit -> 'v)
        (captures: EffectCapture list)
        : 'v * EffectCapture list =
        let v = effect ()

        if det = deterministicTag then
            v, captures
        else
            // One walk for the length and the tip (Phase 296) where there were two.
            let mutable seq = 0
            let mutable prev = cfg.Genesis
            let mutable rest = captures

            while not rest.IsEmpty do
                prev <- rest.Head.Hash
                seq <- seq + 1
                rest <- rest.Tail

            let value = encode v
            let h = hashFn prev (capturePayload seq eff det value)

            v,
            captures
            @ [ { Seq = seq
                  Eff = eff
                  Determinism = det
                  Value = value
                  PrevHash = prev
                  Hash = h } ]

    let captureEffect
        (hashFn: HashFn)
        (encode: 'v -> string)
        (det: string)
        (eff: string)
        (effect: unit -> 'v)
        (captures: EffectCapture list)
        : 'v * EffectCapture list =
        captureEffectWith canonicalConfig hashFn encode det eff effect captures

    let replayEffect
        (decode: string -> Result<'v, string>)
        (eff: string)
        (det: string)
        (effect: unit -> 'v)
        (captures: EffectCapture list)
        : Result<'v * EffectCapture list, string> =
        if det = deterministicTag then
            Ok(effect (), captures)
        else
            match captures with
            // Guard the head capture's identity (Phase 40). Replay consumes the journal positionally
            // in record order; previously the requesting effect identity was ignored, so a replay in a
            // *different* identity order silently received another effect's captured value. A head
            // whose `Eff` ≠ the requesting `eff` is now a named error, not a wrong value.
            | c :: _ when c.Eff <> eff ->
                Error(
                    "replayEffect: effect-identity mismatch — the next capture is for '"
                    + c.Eff
                    + "' but '"
                    + eff
                    + "' was requested (replay must consume captures in record order)"
                )
            | c :: rest -> decode c.Value |> Result.map (fun v -> v, rest)
            | [] -> Ok(effect (), [])

    let capturedSeed (eff: string) (captures: EffectCapture list) : string option =
        captures |> List.tryPick (fun c -> if c.Eff = eff then Some c.Value else None)

    let firstCaptureBreakWith (cfg: StreamConfig) (hashFn: HashFn) (captures: EffectCapture list) : ChainBreak option =
        walkChain
            hashFn
            cfg.Genesis
            0
            (fun (c: EffectCapture) -> c.Seq)
            (fun c -> c.PrevHash)
            (fun c -> c.Hash)
            (fun c -> capturePayload c.Seq c.Eff c.Determinism c.Value)
            captures

    let firstCaptureBreak (hashFn: HashFn) (captures: EffectCapture list) : ChainBreak option =
        firstCaptureBreakWith canonicalConfig hashFn captures

    let verifyCaptures (hashFn: HashFn) (captures: EffectCapture list) : bool =
        firstCaptureBreak hashFn captures |> Option.isNone

    let captureToJsonl (captures: EffectCapture list) : string =
        captures
        |> List.map (fun c ->
            "{\"capture\":true,\"seq\":"
            + string c.Seq
            + ",\"eff\":"
            + jstr c.Eff
            + ",\"det\":"
            + jstr c.Determinism
            + ",\"value\":"
            + c.Value
            + ",\"prevHash\":"
            + jstr c.PrevHash
            + ",\"hash\":"
            + jstr c.Hash
            + "}")
        |> String.concat "\n"

    let captureFromJsonl (text: string) : Result<EffectCapture list, string> =
        text
        |> Jsonl.scanRecords (fun l ->
            Jsonl.intField "seq" l
            |> bindR (fun seq ->
                Jsonl.stringField "eff" l
                |> bindR (fun eff ->
                    Jsonl.stringField "det" l
                    |> bindR (fun det ->
                        Jsonl.rawField "value" l
                        |> bindR (fun value ->
                            Jsonl.stringField "prevHash" l
                            |> bindR (fun prevHash ->
                                Jsonl.stringField "hash" l
                                |> Result.map (fun hash ->
                                    { Seq = seq
                                      Eff = eff
                                      Determinism = det
                                      Value = value
                                      PrevHash = prevHash
                                      Hash = hash })))))))
        |> Result.mapError JsonlFault.toString

    // ---- cryptographic attestation (Phase 320) ----

    let noAttestation: IAttestationSink =
        { new IAttestationSink with
            member _.Sign _ = None
            member _.Verify _ _ = false }

    let attestHead (sink: IAttestationSink) (records: OpRecord<'Op> list) : Attestation option = sink.Sign(head records)

    let verifyAttestation (sink: IAttestationSink) (attestation: Attestation) (records: OpRecord<'Op> list) : bool =
        sink.Verify attestation (head records)
