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
/// against a head. Signing is **opt-in, never mandatory** — the default `Attestation.noAttestation`
/// signs nothing, so the un-attested path behaves exactly as before.
type IAttestationSink =
    /// Attest a chain head. Returns `Some` signed `Attestation`, or `None` for the no-op sink.
    abstract member Sign: head: string -> Attestation option
    /// Re-verify an attestation against the head it claims to cover. `false` if the signature does
    /// not check out (or for the no-op sink, which never issued one).
    abstract member Verify: attestation: Attestation -> head: string -> bool

/// Why STRICT replay refused a request (Phase 301, `EffectCapture.replayEffectStrict`). The lenient
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

/// Where a keyed capture stands in its two phases (Phase 318). An invocation is ATTEMPTED before its
/// body runs, and SETTLED — `Completed` with the value or `Refused` with the reason — when the body
/// answers, which for an asynchronous body is later than the attempt and may be never.
[<RequireQualifiedAccess>]
type CapturePhase =
    /// The invocation was about to run its body. Its value is `null`.
    | Attempted
    /// The body answered a value; the capture's value is the domain codec's encoding of it.
    | Completed
    /// The body answered a failure; the capture's value is the reason, as a JSON string.
    | Refused

/// One record of the KEYED capture journal (Phase 318) — the journal a seam writes when it captures
/// an invocation under its invocation key rather than at its position. `Key` is the invocation key
/// (`Capability.invocationKey`, `Query.invocationKey`, `Query.invocationKeyPage`); `Occurrence` is
/// which invocation under that key this is, counting from 0 in the order the attempts were journalled,
/// so a session that makes the same call twice has two attempts and replays both. Chained exactly as
/// `EffectCapture` is: `Hash` is the hash of `PrevHash` and the record's pre-image.
type KeyedCapture =
    {
        /// Zero-based position in this journal's chain.
        Seq: int
        /// The invocation key the record is about. Replay finds an invocation by it, never by position.
        Key: string
        /// Which invocation under `Key`: 0 for the first attempt journalled under it, 1 for the second.
        Occurrence: int
        /// The determinism label the invocation was journalled under — never `deterministic`.
        Determinism: string
        /// The phase this record moves the invocation into.
        Phase: CapturePhase
        /// Raw JSON: `null` for an attempt, the encoded value for a completion, the reason as a JSON
        /// string for a refusal.
        Value: string
        /// The previous record's `Hash`, or the genesis on the first record.
        PrevHash: string
        /// `hashFn PrevHash` over the `{capture:"keyed", seq, key, occ, det, phase, value}` pre-image.
        Hash: string
    }

/// What capturing one invocation answered (Phase 391; a positional triple before `0.37.0`):
/// `KeyedCapture.captureEffectKeyed`'s result.
type CapturedEffect<'v> =
    {
        /// What the effect answered: `None` while it has not settled — the attempt then stays open.
        Answer: Result<'v, string> option
        /// The invocation's occurrence under its key — what `settleEffectKeyed` settles an open
        /// attempt with later. `0`, and nothing journalled, for a `deterministic` label.
        Occurrence: int
        /// The journal with the attempt, and its settlement when the effect answered.
        Journal: KeyedCapture list
    }

/// Why the keyed capture journal refused a request (Phase 318). Each verb names the cases it can
/// raise: `settleEffectKeyed` the last two, `replayEffectKeyed` the first five.
[<RequireQualifiedAccess>]
type KeyedCaptureFault =
    /// Replay asked for a key the journal holds no record of at all — the invocation was never
    /// captured, so there is nothing to answer it with and the live source is not consulted.
    | NoCapture of key: string
    /// Replay asked for more invocations under `key` than were attempted: `recorded` attempts were
    /// journalled under it and every one has been replayed.
    | Exhausted of key: string * recorded: int
    /// The attempt was journalled under another determinism label: the label requested, and the one
    /// recorded. Compared exactly.
    | LabelMismatch of key: string * requested: string * recorded: string
    /// The requested label is not a canonical determinism label (`EffectCapture.isDeterminismLabel`).
    | LabelNotCanonical of label: string
    /// The completed value did not decode: the domain codec's reason.
    | Undecodable of reason: string
    /// Settling named an invocation the journal holds no attempt for.
    | NotAttempted of key: string * occurrence: int
    /// Settling named an invocation that was already settled; an invocation settles once.
    | AlreadySettled of key: string * occurrence: int

/// The positional determinism-capture journal (Phase 27; the public module itself since Phase 516):
/// journal an effect's value at its POSITION, replay it back in record order, and verify, write and read
/// the journal. Each verb that hashes takes the config and hash the journal is chained under — the
/// config's genesis seeds it and its `Quote` spells the tags.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module EffectCapture =

    /// The determinism label below which an effect needs no capture — the `Deterministic` tag.
    /// `Fuaran.Core.Function`'s `Effect.determinismTag` projects `Deterministic` to exactly this
    /// string; the capture seam keys on the label rather than referencing the DU (this layer sits
    /// below `Function` and stays FSharp.Core-only). A non-deterministic label names one or more of
    /// `clock`, `random`, `network`, in that order, joined by `+` (`"clock"`, `"clock+random"`).
    [<Literal>]
    let deterministicTag = "deterministic"

    /// The hash payload binding a capture to its chain — `{capture, seq, eff, det, value}`. The
    /// `value` is embedded as raw JSON (the domain `Codec` output), so it joins the chain hash
    /// byte-for-byte exactly as an op payload does.
    let private capturePayload
        (quote: string -> string)
        (seq: int)
        (eff: string)
        (det: string)
        (value: string)
        : string =
        "{\"capture\":true,\"seq\":"
        + string seq
        + ",\"eff\":"
        + quote eff
        + ",\"det\":"
        + quote det
        + ",\"value\":"
        + value
        + "}"

    /// The positional capture's shape under `cfg`'s spelling (Phase 530; the spelling the config's since
    /// Phase 516): the chain verbs' instance for the journal.
    let private captureShape (cfg: StreamConfig) : ChainShape<EffectCapture> =
        { SeqOf = fun c -> c.Seq
          PrevOf = fun c -> c.PrevHash
          HashOf = fun c -> c.Hash
          PayloadOf = fun c -> capturePayload cfg.Quote c.Seq c.Eff c.Determinism c.Value
          Link = fun c prev hash -> { c with PrevHash = prev; Hash = hash } }

    /// The record seam (Phase 27). Evaluate `effect` once; for a non-`Deterministic` tag, journal
    /// the realized value (encoded via the domain `Codec`) into the hash-chained capture log and
    /// return `(value, extended-log)`; for `deterministicTag` it is pass-through — the value is
    /// returned and nothing is captured (a deterministic effect is reproducible from its inputs).
    /// `encode` is a per-call parameter (GP2 — no new witness field), so one log can hold captures
    /// of heterogeneous value types side by side. `Eff` identifies the boundary for the
    /// seed-injection helper.
    let captureEffect
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
            let value = encode v

            v,
            ChainShape.append
                (captureShape cfg)
                hashFn
                (Anchor.genesis cfg)
                (fun seq ->
                    { Seq = seq
                      Eff = eff
                      Determinism = det
                      Value = value
                      PrevHash = ""
                      Hash = "" })
                captures

    /// The replay seam (Phase 27). For a non-`Deterministic` tag, return the next recorded value
    /// (decoded via the domain `Codec`) instead of re-evaluating the live source, and advance the
    /// remaining capture log — so `replay(record(session)) == session` for clock / random /
    /// network effects. Absent a capture (legacy / exhausted journal), fall back to live
    /// evaluation. A `deterministicTag` effect re-evaluates and consumes nothing (it is already
    /// reproducible). A driver folds this over its effect sequence, threading the remaining log.
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

    /// Is `det` a canonical determinism label (Phase 301)? `deterministicTag`, or one or more of
    /// `clock`, `random`, `network` in that order joined by `+` — compared exactly, case included —
    /// the vocabulary `Fuaran.Core.Function`'s `Effect.determinismTag` renders.
    let isDeterminismLabel (det: string) : bool =
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

    /// STRICT replay (Phase 301): `replayEffect` with every silent fallback a typed refusal. A label
    /// that is not canonical is `LabelNotCanonical`; the `deterministicTag` label re-evaluates `effect`
    /// and consumes nothing, as `replayEffect` does; any other label is answered from the journal ONLY
    /// — an exhausted journal is `Exhausted` (never the live source), the next capture for another
    /// effect `IdentityMismatch`, one journalled under another label `LabelMismatch`, a value the codec
    /// refuses `Undecodable`. Over a complete journal it answers exactly what `replayEffect` answers.
    /// A journal cut short where the session stops early is not reached by replay at all: anchor its
    /// head (`captureHead`, recorded in the op stream or signed by an `IAttestationSink`) and check it
    /// with `verifyCapturesAt`.
    let replayEffectStrict
        (decode: string -> Result<'v, string>)
        (eff: string)
        (det: string)
        (effect: unit -> 'v)
        (captures: EffectCapture list)
        : Result<'v * EffectCapture list, CaptureReplayFault> =
        if not (isDeterminismLabel det) then
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

    /// The head of a capture journal under `cfg` (Phase 301) — the last capture's `Hash`, or
    /// `cfg.Genesis` for an empty journal. The chain binds every capture's sequence, so the head
    /// fixes the journal's LENGTH: a journal with its tail cut off has a different head.
    let captureHead (cfg: StreamConfig) (captures: EffectCapture list) : string =
        match List.tryLast captures with
        | Some c -> c.Hash
        | None -> cfg.Genesis

    /// The seed-injection helper (Phase 27) — surface the recorded value of the first capture for
    /// an effect identity. An effect that reads non-determinism *internally* (a seeded RNG whose
    /// individual draws are not captured) records its seed as the capture value; a well-behaved
    /// consumer reseeds from this so the internal trajectory replays automatically rather than by
    /// discipline. `None` when the journal holds no capture for `eff`.
    let capturedSeed (eff: string) (captures: EffectCapture list) : string option =
        captures |> List.tryPick (fun c -> if c.Eff = eff then Some c.Value else None)

    /// `firstCaptureBreak` from `cfg.Genesis` (Phase 296) — the capture chain's genesis read from the
    /// config rather than hard-wired `""`, through the one chain walker. Only `cfg.Genesis` is read: a
    /// capture's pre-image is the capture format, not the op payload.
    let firstCaptureBreak (cfg: StreamConfig) (hashFn: HashFn) (captures: EffectCapture list) : ChainBreak option =
        ChainShape.walk (captureShape cfg) hashFn (Anchor.genesis cfg) captures

    /// Recompute the capture chain and confirm every link — the capture analogue of `verifyChain`.
    /// A tampered captured value (or a reordered / dropped capture) fails this, so a recorded
    /// effect value is tamper-evident exactly like an op.
    let verifyCaptures (cfg: StreamConfig) (hashFn: HashFn) (captures: EffectCapture list) : bool =
        firstCaptureBreak cfg hashFn captures |> Option.isNone

    /// `verifyCaptures` against an ANCHORED head (Phase 301), under `cfg`: the chain is intact and its
    /// head is `head` — the value a host recorded when the session closed. A journal truncated at the
    /// tail still verifies on its own; against its anchor it does not.
    let verifyCapturesAt (cfg: StreamConfig) (hashFn: HashFn) (head: string) (captures: EffectCapture list) : bool =
        verifyCaptures cfg hashFn captures && captureHead cfg captures = head

    /// Re-mint a capture log from one profile to another (Phase 360): verify it under `fromProfile`
    /// (`firstCaptureBreak`; a break is `Error`), then re-derive every `PrevHash` / `Hash` with
    /// the tags spelled as `toProfile` spells them, from `cfg.Genesis`. `Ok(captures', ids)`, with `ids`
    /// mapping every old capture hash to its new one. The values are carried byte for byte; the result
    /// verifies under `toProfile`.
    let rehashCaptures
        (fromCfg: StreamConfig)
        (toCfg: StreamConfig)
        (hashFn: HashFn)
        (captures: EffectCapture list)
        : Result<EffectCapture list, ChainBreak> =
        ChainShape.rehash
            (captureShape fromCfg)
            hashFn
            (captureShape toCfg)
            hashFn
            (Anchor.genesis fromCfg)
            toCfg.Genesis
            id
            captures

    /// The capture line: the chained line (`linked`) over the capture's payload with `value` embedded.
    let private captureLine (c: EffectCapture) (value: string) : string =
        Jsonl.linked (capturePayload Jsonl.quote c.Seq c.Eff c.Determinism value) c.PrevHash c.Hash

    /// One JSON object per line for a capture log — the `value` field embedded as raw JSON (the
    /// domain `Codec` output), so a round-trip preserves it byte-for-byte. Lets a recorded session
    /// persist its captures alongside its ops, so "replay exactly what happened" holds *from the
    /// file*.
    let toJsonl (captures: EffectCapture list) : string =
        captures |> List.map (fun c -> captureLine c c.Value) |> String.concat "\n"

    /// `EffectCapture.toJsonl` that refuses a captured value the reader cannot read back (Phase 301) — the
    /// `Jsonl.checkRaw` check over each `Value` (member `value`), and since Phase 473 over the `eff`,
    /// `det`, `prevHash` and `hash` strings it quotes, by member. A capture's value is hashed raw, so
    /// a value with a line break or whitespace either side read back changed and failed
    /// `verifyCaptures`. On `Ok` the text is `EffectCapture.toJsonl`'s, byte for byte.
    let tryToJsonl (captures: EffectCapture list) : Result<string, JsonlWriteFault> =
        Jsonl.checkedLines
            "value"
            (fun (c: EffectCapture) ->
                [ "eff", Jsonl.jstr c.Eff
                  "det", Jsonl.jstr c.Determinism
                  "value", c.Value
                  "prevHash", Jsonl.jstr c.PrevHash
                  "hash", Jsonl.jstr c.Hash ])
            captureLine
            captures

    /// Parse a capture log back from JSONL (Phase 27) — the `value` raw span is preserved
    /// byte-for-byte (the domain decoder receives exactly what `Codec` produced), so a round-trip
    /// is identical and the chain still `verifyCaptures`. Uses the same self-contained, Fable-clean
    /// line scanner as `fromJsonl`; a malformed line is a named `Error`, never an exception.
    let ofJsonl (text: string) : Result<EffectCapture list, JsonlFault> =
        text
        |> Jsonl.scanRecords (fun l ->
            Jsonl.linkedOf l (fun _ ->
                Jsonl.stringField "eff" l
                |> Jsonl.bindR (fun eff ->
                    Jsonl.stringField "det" l
                    |> Jsonl.bindR (fun det -> Jsonl.rawField "value" l |> Result.map (fun value -> eff, det, value))))
            |> Result.map (fun (seq, (eff, det, value), prevHash, hash) ->
                { Seq = seq
                  Eff = eff
                  Determinism = det
                  Value = value
                  PrevHash = prevHash
                  Hash = hash }))

/// The keyed capture journal (Phase 318; its JSONL since Phase 530; the public module itself since Phase
/// 516): journal an invocation under its INVOCATION KEY in two phases, settle it, replay it by key, and
/// verify, write and read the journal. Each verb that hashes takes the config and hash the journal is
/// chained under. One line per record, the chained line over the record's hash payload —
/// `{"capture":"keyed","seq":…,"key":…,"occ":…,"det":…,"phase":…,"value":…,"prevHash":…,"hash":…}` — so
/// the bytes a line carries are the bytes its canonical hash covers, and a journal read back verifies
/// (`KeyedCapture.verifyKeyedCaptures`) exactly as the one written.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module KeyedCapture =

    let private phaseTag (p: CapturePhase) : string =
        match p with
        | CapturePhase.Attempted -> "attempted"
        | CapturePhase.Completed -> "completed"
        | CapturePhase.Refused -> "refused"

    /// The keyed record's hash pre-image. The tag `"keyed"` keeps it disjoint from a positional
    /// capture's (`"capture":true`), so a record of one journal never verifies in the other.
    let private keyedPayload
        (quote: string -> string)
        (seq: int)
        (key: string)
        (occ: int)
        (det: string)
        (phase: CapturePhase)
        (value: string)
        : string =
        "{\"capture\":\"keyed\",\"seq\":"
        + string seq
        + ",\"key\":"
        + quote key
        + ",\"occ\":"
        + string occ
        + ",\"det\":"
        + quote det
        + ",\"phase\":"
        + quote (phaseTag phase)
        + ",\"value\":"
        + value
        + "}"

    /// The keyed record's shape under `cfg`'s spelling (Phase 530; the config's since Phase 516): the
    /// chain verbs' instance for the keyed journal.
    let private keyedShape (cfg: StreamConfig) : ChainShape<KeyedCapture> =
        { SeqOf = fun c -> c.Seq
          PrevOf = fun c -> c.PrevHash
          HashOf = fun c -> c.Hash
          PayloadOf = fun c -> keyedPayload cfg.Quote c.Seq c.Key c.Occurrence c.Determinism c.Phase c.Value
          Link = fun c prev hash -> { c with PrevHash = prev; Hash = hash } }

    /// Append one keyed record at the journal's tip.
    let private appendKeyed
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (key: string)
        (occ: int)
        (det: string)
        (phase: CapturePhase)
        (value: string)
        (captures: KeyedCapture list)
        : KeyedCapture list =
        ChainShape.append
            (keyedShape cfg)
            hashFn
            (Anchor.genesis cfg)
            (fun seq ->
                { Seq = seq
                  Key = key
                  Occurrence = occ
                  Determinism = det
                  Phase = phase
                  Value = value
                  PrevHash = ""
                  Hash = "" })
            captures

    /// How many attempts the journal holds under `key`.
    let private attemptsOf (key: string) (captures: KeyedCapture list) : int =
        captures
        |> List.filter (fun c -> c.Key = key && c.Phase = CapturePhase.Attempted)
        |> List.length

    /// `beginEffectKeyed` from `cfg.Genesis`.
    let beginEffectKeyed
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (det: string)
        (key: string)
        (captures: KeyedCapture list)
        : int * KeyedCapture list =
        let occ = attemptsOf key captures
        occ, appendKeyed cfg hashFn key occ det CapturePhase.Attempted "null" captures

    /// A settlement's phase and value: `Ok v` is `Completed` with `encode v`, `Error reason` is
    /// `Refused` with the reason as a JSON string.
    let private settlementOf (encode: 'v -> string) (outcome: Result<'v, string>) : CapturePhase * string =
        match outcome with
        | Ok v -> CapturePhase.Completed, encode v
        | Error reason -> CapturePhase.Refused, Jsonl.jstr reason

    /// `settleEffectKeyed` from `cfg.Genesis`.
    let settleEffectKeyed
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (encode: 'v -> string)
        (key: string)
        (occurrence: int)
        (outcome: Result<'v, string>)
        (captures: KeyedCapture list)
        : Result<KeyedCapture list, KeyedCaptureFault> =
        let mine (c: KeyedCapture) =
            c.Key = key && c.Occurrence = occurrence

        match captures |> List.tryFind (fun c -> mine c && c.Phase = CapturePhase.Attempted) with
        | None -> Error(KeyedCaptureFault.NotAttempted(key, occurrence))
        | Some attempt ->
            if captures |> List.exists (fun c -> mine c && c.Phase <> CapturePhase.Attempted) then
                Error(KeyedCaptureFault.AlreadySettled(key, occurrence))
            else
                let phase, value = settlementOf encode outcome
                Ok(appendKeyed cfg hashFn key occurrence attempt.Determinism phase value captures)

    /// `captureEffectKeyed` from `cfg.Genesis`.
    let captureEffectKeyed
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (encode: 'v -> string)
        (det: string)
        (key: string)
        (effect: unit -> Result<'v, string> option)
        (captures: KeyedCapture list)
        : CapturedEffect<'v> =
        if det = EffectCapture.deterministicTag then
            { Answer = effect ()
              Occurrence = 0
              Journal = captures }
        else
            let occ, attempted = beginEffectKeyed cfg hashFn det key captures

            match effect () with
            | None ->
                { Answer = None
                  Occurrence = occ
                  Journal = attempted }
            | Some outcome ->
                // The attempt just appended is the one this settles, so its settlement is appended
                // directly rather than looked up again (Phase 496). Over a journal whose pairing holds
                // this is exactly what `settleEffectKeyed` writes. Over one already failing
                // `keyedPairingHolds` (a settlement at this occurrence with no attempt before it) the
                // settlement is still written and the journal fails that check as it did before:
                // total, and the malformation stays where `verifyKeyedCaptures` reports it.
                let phase, value = settlementOf encode outcome

                { Answer = Some outcome
                  Occurrence = occ
                  Journal = appendKeyed cfg hashFn key occ det phase value attempted }

    /// Replay one invocation from the keyed journal (Phase 318) — by its KEY, never its position.
    /// `cursor` counts the invocations already replayed under each key (start from `Map.empty`), so
    /// the n-th call under a key replays its n-th attempt. The answer is what the invocation settled
    /// to: `Some(Ok v)` for a completion, `Some(Error reason)` for a refusal — a failure replays as
    /// the same failure — and `None` for an attempt that never settled. Strict, as
    /// `replayEffectStrict` is: a key with no record is `NoCapture`, a key whose attempts are all
    /// replayed is `Exhausted`, an attempt under another label `LabelMismatch`, a non-canonical label
    /// `LabelNotCanonical`, a value the codec refuses `Undecodable`; the live `effect` runs for the
    /// `deterministic` label only, which never journals. Replaying calls under DIFFERENT keys in any
    /// order answers each the same.
    let replayEffectKeyed
        (decode: string -> Result<'v, string>)
        (det: string)
        (key: string)
        (effect: unit -> Result<'v, string> option)
        (cursor: Map<string, int>)
        (captures: KeyedCapture list)
        : Result<Result<'v, string> option * Map<string, int>, KeyedCaptureFault> =
        if not (EffectCapture.isDeterminismLabel det) then
            Error(KeyedCaptureFault.LabelNotCanonical det)
        elif det = EffectCapture.deterministicTag then
            Ok(effect (), cursor)
        else
            let occ = cursor |> Map.tryFind key |> Option.defaultValue 0
            let mine = captures |> List.filter (fun c -> c.Key = key)

            match mine with
            | [] -> Error(KeyedCaptureFault.NoCapture key)
            | _ ->
                match
                    mine
                    |> List.tryFind (fun c -> c.Occurrence = occ && c.Phase = CapturePhase.Attempted)
                with
                | None -> Error(KeyedCaptureFault.Exhausted(key, attemptsOf key captures))
                | Some attempt when attempt.Determinism <> det ->
                    Error(KeyedCaptureFault.LabelMismatch(key, det, attempt.Determinism))
                | Some _ ->
                    let next = Map.add key (occ + 1) cursor

                    match
                        mine
                        |> List.tryFind (fun c -> c.Occurrence = occ && c.Phase <> CapturePhase.Attempted)
                    with
                    | None -> Ok(None, next)
                    | Some c when c.Phase = CapturePhase.Completed ->
                        match decode c.Value with
                        | Ok v -> Ok(Some(Ok v), next)
                        | Error reason -> Error(KeyedCaptureFault.Undecodable reason)
                    | Some c ->
                        // The scanner's own `unquote` (Phase 530): the value is ONE JSON string, read to its
                        // last character, where a private copy wrapped it in an object and parsed a line.
                        match Jsonl.unquote c.Value with
                        | Ok reason -> Ok(Some(Error reason), next)
                        | Error _ ->
                            Error(KeyedCaptureFault.Undecodable("a refusal's reason is not a JSON string: " + c.Value))

    /// The first chain fault in a keyed journal, or `None` — through the one chain walker, from
    /// `cfg.Genesis`, so a keyed break localises exactly as an op break does.
    let firstKeyedCaptureBreak (cfg: StreamConfig) (hashFn: HashFn) (captures: KeyedCapture list) : ChainBreak option =
        ChainShape.walk (keyedShape cfg) hashFn (Anchor.genesis cfg) captures

    /// The journal's PAIRING is well formed: under each key the attempts carry occurrences 0, 1, …
    /// in journal order, every settlement follows the attempt it settles, carries its label, and no
    /// attempt is settled twice.
    let private keyedPairingHolds (captures: KeyedCapture list) : bool =
        let step
            (ok: bool, attempts: Map<string, int>, settled: Set<string * int>, labels: Map<string * int, string>)
            (c: KeyedCapture)
            =
            if not ok then
                ok, attempts, settled, labels
            else
                let held = attempts |> Map.tryFind c.Key |> Option.defaultValue 0

                match c.Phase with
                | CapturePhase.Attempted ->
                    (c.Occurrence = held && c.Determinism <> EffectCapture.deterministicTag),
                    Map.add c.Key (held + 1) attempts,
                    settled,
                    Map.add (c.Key, c.Occurrence) c.Determinism labels
                | _ ->
                    (c.Occurrence < held
                     && not (settled.Contains(c.Key, c.Occurrence))
                     && Map.tryFind (c.Key, c.Occurrence) labels = Some c.Determinism),
                    attempts,
                    settled.Add(c.Key, c.Occurrence),
                    labels

        let ok, _, _, _ = captures |> List.fold step (true, Map.empty, Set.empty, Map.empty)

        ok

    /// `verifyKeyedCaptures` from `cfg.Genesis`.
    let verifyKeyedCaptures (cfg: StreamConfig) (hashFn: HashFn) (captures: KeyedCapture list) : bool =
        (firstKeyedCaptureBreak cfg hashFn captures).IsNone
        && keyedPairingHolds captures

    let private lineOf (c: KeyedCapture) (value: string) : string =
        Jsonl.linked (keyedPayload Jsonl.quote c.Seq c.Key c.Occurrence c.Determinism c.Phase value) c.PrevHash c.Hash

    /// The journal as JSONL, one line per record, joined by `\n`. Unchecked: a `Value` that is not one
    /// JSON value on one line is embedded as-is and will not read back; `tryToJsonl` refuses it.
    let toJsonl (captures: KeyedCapture list) : string =
        captures |> List.map (fun c -> lineOf c c.Value) |> String.concat "\n"

    /// `toJsonl` through the checked writer (`Jsonl.checkedLines`): every member a line carries is
    /// checked before any line is built, and the first the reader could not read back is the `Error`,
    /// by its 1-based line and member. On `Ok`, `toJsonl`'s text byte for byte.
    let tryToJsonl (captures: KeyedCapture list) : Result<string, JsonlWriteFault> =
        Jsonl.checkedLines
            "value"
            (fun (c: KeyedCapture) ->
                [ "key", Jsonl.jstr c.Key
                  "det", Jsonl.jstr c.Determinism
                  "phase", Jsonl.jstr (phaseTag c.Phase)
                  "value", c.Value
                  "prevHash", Jsonl.jstr c.PrevHash
                  "hash", Jsonl.jstr c.Hash ])
            lineOf
            captures

    /// Read a keyed journal back through the one scanner: structural only — verify it
    /// (`KeyedCapture.verifyKeyedCaptures`) before trusting it. A malformed line, a member of the wrong kind
    /// or a phase other than `attempted` / `completed` / `refused` is the `JsonlFault`, by its 1-based
    /// line; the `value` span is kept byte for byte.
    let ofJsonl (text: string) : Result<KeyedCapture list, JsonlFault> =
        text
        |> Jsonl.scanRecords (fun l ->
            Jsonl.linkedOf l (fun _ ->
                Jsonl.stringField "key" l
                |> Jsonl.bindR (fun key ->
                    Jsonl.intField "occ" l
                    |> Jsonl.bindR (fun occ ->
                        Jsonl.stringField "det" l
                        |> Jsonl.bindR (fun det ->
                            Jsonl.stringField "phase" l
                            |> Jsonl.bindR (fun tag ->
                                match tag with
                                | "attempted" -> Ok CapturePhase.Attempted
                                | "completed" -> Ok CapturePhase.Completed
                                | "refused" -> Ok CapturePhase.Refused
                                | other -> Error(Jsonl.refuse l (sprintf "unknown capture phase \"%s\"" other)))
                            |> Jsonl.bindR (fun phase ->
                                Jsonl.rawField "value" l
                                |> Result.map (fun value -> key, occ, det, phase, value))))))
            |> Result.map (fun (seq, (key, occ, det, phase, value), prevHash, hash) ->
                { Seq = seq
                  Key = key
                  Occurrence = occ
                  Determinism = det
                  Phase = phase
                  Value = value
                  PrevHash = prevHash
                  Hash = hash }))

/// Attestation of a chain's head (Phase 320; the party-bound forms Phase 311; the public module since
/// Phase 516): sign the head through a host's `IAttestationSink`, and re-verify a signature against the
/// head a chain has now. Every verb that reads a head reads it under the config the chain was written
/// under, whose genesis is the head of an empty chain.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Attestation =

    /// The default no-op attestation sink: never signs (`Sign` ⇒ `None`) and verifies nothing
    /// (`Verify` ⇒ `false`). Signing is opt-in — a stream that does not plug in a real sink behaves
    /// exactly as before. Enterprise hosts supply a KMS / HSM-backed `IAttestationSink`.
    let noAttestation: IAttestationSink =
        { new IAttestationSink with
            member _.Sign _ = None
            member _.Verify _ _ = false }

    /// Attest the current head of a chain at a commit / publish boundary (Phase 320). Signs the head
    /// hash via `sink` — O(commits), not O(ops), since the hash-chain already binds every prior op
    /// into the head. `None` from the no-op sink. The signed `Attestation` plus deterministic replay
    /// is the replay-as-provenance contract: a verifier re-derives the head from the op log, then
    /// `verifyAttestation`s the signature against it.
    let attestHead (sink: IAttestationSink) (cfg: StreamConfig) (records: OpRecord<'Op> list) : Attestation option =
        sink.Sign(OpStream.head cfg records)

    /// Re-verify an attestation against a chain's *current* head (Phase 320). Independent
    /// re-verification: a third party recomputes the head from the records (`verifyChain` proves the
    /// chain is intact; `head` reads its tip) and checks the signature covers exactly that head.
    let verifyAttestation
        (sink: IAttestationSink)
        (cfg: StreamConfig)
        (attestation: Attestation)
        (records: OpRecord<'Op> list)
        : bool =
        sink.Verify attestation (OpStream.head cfg records)

    /// What an attestation BY A PARTY signs (Phase 311): `hashFn head ("attest|" + Actor.encode
    /// party)` — the head bound to the identity of the party vouching for it, so one party's signature
    /// over a head is not another's, and a store several writers attest (one lane each) says WHO
    /// attested which head. The `attest|` prefix keeps the pre-image apart from a lane DAG node's
    /// (`Actor.encode actor + "|" + …`, which opens with `{`), so a subject is never a node id by the
    /// spelling of its pre-image. The sink signs and verifies this string exactly as it signs a bare
    /// head; `KeyId` still names the key, the party names who vouches. The bodies are here, not in
    /// `Capture.fs`: they are compositions of `head` and the sink, and carry no state of their own.
    let attestationSubject (hashFn: HashFn) (party: Actor) (head: string) : string =
        hashFn head ("attest|" + Actor.encode party)

    /// `attestHead` by a named party (Phase 311): signs `attestationSubject hashFn party (head
    /// records)`. `None` from the no-op sink.
    let attestHeadAs
        (sink: IAttestationSink)
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (party: Actor)
        (records: OpRecord<'Op> list)
        : Attestation option =
        sink.Sign(attestationSubject hashFn party (OpStream.head cfg records))

    /// `verifyAttestation` for a party's attestation (Phase 311): `true` only when the attestation
    /// names the subject this party's attestation of the chain's current head signs AND the sink
    /// accepts it there — so an attestation made by another party, or over another head, is refused
    /// before the sink is asked.
    let verifyAttestationAs
        (sink: IAttestationSink)
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (party: Actor)
        (attestation: Attestation)
        (records: OpRecord<'Op> list)
        : bool =
        let subject = attestationSubject hashFn party (OpStream.head cfg records)
        attestation.Head = subject && sink.Verify attestation subject
