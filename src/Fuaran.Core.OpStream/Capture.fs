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
    {
        /// Zero-based position in the capture chain, independent of any op stream's `Seq`.
        Seq: int
        /// The effect-identity key. Replay consumes captures in order and refuses one whose `Eff` is not
        /// the effect requested.
        Eff: string
        /// The determinism label the value was captured under — never `deterministic`, which captures
        /// nothing. Strict replay compares it exactly with the label requested.
        Determinism: string
        /// The realized value as the domain codec encoded it: raw JSON, embedded and hashed verbatim.
        Value: string
        /// The previous capture's `Hash`, or the config's `Genesis` on the first capture.
        PrevHash: string
        /// `hashFn PrevHash` over the `{capture, seq, eff, det, value}` payload; a capture whose fields
        /// were edited no longer recomputes to it (`verifyCaptures`).
        Hash: string
    }

/// A signed checkpoint over a chain head (Phase 320). `Head` is the hash being attested — a chain
/// `Hash` at a commit / publish boundary. The hash-chain already attests the *whole prefix* (each
/// `Hash` folds in its `PrevHash`), so signing the head is O(commits), not O(ops): one signature
/// covers every op up to that point. `KeyId` names the signing key; `Signature` is the host's
/// opaque attestation token (hex / base64). Verification re-checks the signature against the head —
/// Core owns the *seam*, the host owns the crypto.
type Attestation =
    {
        /// The chain head that was signed — `OpStream.head` of the stream at the time — which covers
        /// every record up to it.
        Head: string
        /// The signing key's name. Opaque to Core: only the `IAttestationSink` interprets it.
        KeyId: string
        /// The host's signature token. Core never inspects it; only the sink's `Verify` does.
        Signature: string
    }

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

/// Why STRICT replay refused a request (Phase 301, `OpStream.replayEffectStrict`). The lenient
/// `replayEffect` falls back to the live source when the journal is exhausted, so a journal whose
/// tail was cut off still replays — with live values leaking in and no signal. The strict form names
/// every way the journal can fail to answer, and evaluates the live source only for the
/// `deterministic` label, which never journals.
[<RequireQualifiedAccess>]
type CaptureReplayFault =
    /// The journal holds nothing more and a non-deterministic effect asked: replay ran past the end of
    /// what was recorded — a truncated journal, or a session doing more than the recorded one did.
    | Exhausted of eff: string
    /// The next capture is for another effect identity (the Phase 40 guard, typed): the effect
    /// requested, and the one the capture records.
    | IdentityMismatch of requested: string * recorded: string
    /// The next capture was journalled under another determinism label: the label requested, and the
    /// one recorded. Compared exactly, case included.
    | LabelMismatch of requested: string * recorded: string
    /// The requested label is not a canonical determinism label — `deterministic`, or one or more of
    /// `clock`, `random`, `network` in that order joined by `+` — compared exactly, case included, so
    /// `Deterministic` or `Clock` is refused rather than read as some other label.
    | LabelNotCanonical of label: string
    /// The captured value did not decode: the domain codec's reason.
    | Undecodable of reason: string

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

    /// The factors of a non-deterministic label, in canonical order. A DELIBERATE COPY of the
    /// vocabulary `Fuaran.Core.Function`'s `Effect.determinismTag` renders (this package sits below
    /// `Function` and cannot reference it); `OpStreamTests` holds the two equal over a label corpus.
    let private labelFactors = [ "clock"; "random"; "network" ]

    let isCanonicalLabel (det: string) : bool =
        if det = deterministicTag then
            true
        else
            let parts = det.Split('+') |> Array.toList

            let rec ordered (from: int) =
                function
                | [] -> true
                | (p: string) :: rest ->
                    match List.tryFindIndex (fun f -> f = p) labelFactors with
                    | Some i when i >= from -> ordered (i + 1) rest
                    | _ -> false

            ordered 0 parts

    let replayEffectStrict
        (decode: string -> Result<'v, string>)
        (eff: string)
        (det: string)
        (effect: unit -> 'v)
        (captures: EffectCapture list)
        : Result<'v * EffectCapture list, CaptureReplayFault> =
        if not (isCanonicalLabel det) then
            Error(CaptureReplayFault.LabelNotCanonical det)
        elif det = deterministicTag then
            Ok(effect (), captures)
        else
            match captures with
            | [] -> Error(CaptureReplayFault.Exhausted eff)
            | c :: _ when c.Eff <> eff -> Error(CaptureReplayFault.IdentityMismatch(eff, c.Eff))
            | c :: _ when c.Determinism <> det -> Error(CaptureReplayFault.LabelMismatch(det, c.Determinism))
            | c :: rest ->
                match decode c.Value with
                | Ok v -> Ok(v, rest)
                | Error reason -> Error(CaptureReplayFault.Undecodable reason)

    let captureHeadWith (cfg: StreamConfig) (captures: EffectCapture list) : string =
        match List.tryLast captures with
        | Some c -> c.Hash
        | None -> cfg.Genesis

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

    let verifyCapturesAtWith (cfg: StreamConfig) (hashFn: HashFn) (head: string) (captures: EffectCapture list) : bool =
        firstCaptureBreakWith cfg hashFn captures |> Option.isNone
        && captureHeadWith cfg captures = head

    let private captureLine (c: EffectCapture) (value: string) : string =
        "{\"capture\":true,\"seq\":"
        + string c.Seq
        + ",\"eff\":"
        + jstr c.Eff
        + ",\"det\":"
        + jstr c.Determinism
        + ",\"value\":"
        + value
        + ",\"prevHash\":"
        + jstr c.PrevHash
        + ",\"hash\":"
        + jstr c.Hash
        + "}"

    let captureToJsonl (captures: EffectCapture list) : string =
        captures |> List.map (fun c -> captureLine c c.Value) |> String.concat "\n"

    let tryCaptureToJsonl (captures: EffectCapture list) : Result<string, JsonlWriteFault> =
        checkedLines "value" (fun (c: EffectCapture) -> c.Value) captureLine captures

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
