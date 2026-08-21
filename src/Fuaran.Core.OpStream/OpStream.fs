namespace Fuaran.Core

/// The typed, hashed actor that produced an op (Phase 320). The `Human` / `Agent` distinction is
/// the load-bearing accountability fact: a `Human` is a person / account id; an `Agent`
/// additionally carries the `model` + `version` that emitted the op (a neutral attribution axis a
/// consumer may use for its own analytics or provenance reporting). The actor is folded into the chain
/// hash (`StreamConfig.Payload`), so altering it breaks the integrity chain — attribution is now
/// tamper-evident, not merely recorded. FSharp.Core-only + Fable-clean (the encoder is hand-rolled
/// canonical JSON, no `System.Text.Json`), so it hashes byte-identically on every host.
type Actor =
    | Human of id: string
    | Agent of model: string * version: string * id: string

/// Companion helpers for `Actor` — the canonical hash pre-image (`encode`), the stable id
/// projection, and the pre-Phase-320 migration lift.
[<RequireQualifiedAccess>]
module Actor =

    /// Minimal JSON string escaping (Fable-clean — mirrors `OpStream`'s private `jstr`). Kept
    /// local so `Actor.encode` is byte-identical to the rest of the canonical-JSON surface.
    let private jstr (s: string) : string =
        let sb = System.Text.StringBuilder()
        sb.Append('"') |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when int c < 0x20 -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append(c) |> ignore

        sb.Append('"') |> ignore
        sb.ToString()

    /// The canonical JSON object the chain hash folds over. Field order is fixed (`kind` first,
    /// then the case fields in declaration order) so the pre-image is stable across hosts:
    ///   `Human`  → `{"kind":"human","id":<id>}`
    ///   `Agent`  → `{"kind":"agent","model":<model>,"version":<version>,"id":<id>}`
    let encode (a: Actor) : string =
        match a with
        | Human id -> "{\"kind\":\"human\",\"id\":" + jstr id + "}"
        | Agent(model, version, id) ->
            "{\"kind\":\"agent\",\"model\":"
            + jstr model
            + ",\"version\":"
            + jstr version
            + ",\"id\":"
            + jstr id
            + "}"

    /// The stable attribution id of either case.
    let id (a: Actor) : string =
        match a with
        | Human id -> id
        | Agent(_, _, id) -> id

    /// Lift a pre-Phase-320 bare actor *string* (the old op-stream format recorded the actor as an
    /// unstructured string outside any Human/Agent distinction) to the typed `Human` case — the
    /// migration default. See `OpStream.legacyActorConfig` / `OpStream.fromJsonlLegacyActor`.
    let ofLegacyString (s: string) : Actor = Human s

/// One append-only, hash-chained op record. `Hash = hashFn PrevHash payload`, where
/// `payload` is the canonical `{seq, actor, op}` envelope (the `actor` is the typed `Actor`
/// object since Phase 320). The chain makes tampering — including attribution tampering —
/// detectable (`verifyChain`) and replay deterministic.
type OpRecord<'Op> =
    { Seq: int
      Actor: Actor
      Op: 'Op
      PrevHash: string
      Hash: string }

/// The first integrity fault found in a hash-chained stream (Phase 21) — the record `Index`, why
/// (sequence / prev-link / hash), and the expected vs got value. `verifyChain` is `firstChainBreak
/// … |> Option.isNone`; this names *where* a corrupt stream broke (for `fromJsonlVerified` / debug).
type ChainBreak =
    { Index: int
      Reason: string
      Expected: string
      Got: string }

/// The two-seam witness the whole module lifts over: `Apply` is the domain reducer,
/// `Encode`/`Decode` the domain op codec. Per the Documents extraction assessment,
/// parameterise over these and the op-stream module is line-for-line shared.
/// `Decode` returns `Result` (Phase 252) — the same recoverable-envelope discipline the
/// rest of the substrate follows, so a malformed op surfaces as a named `Error`, never an
/// exception (the F3 adoption finding: every domain decode is already `Result`-returning).
type StreamWitness<'Op, 'State, 'Rej> =
    { Apply: 'Op -> 'State -> Result<'State, 'Rej>
      Encode: 'Op -> string
      Decode: string -> Result<'Op, string> }

/// `prevHash -> payload -> hash`. Pluggable so a host can swap FNV-1a (portable,
/// Fable-clean default) for SHA-256 at its boundary while keeping cross-host parity.
type HashFn = string -> string -> string

/// The chain-*payload* binding (Phase 255 — finding F4): `Payload seq actor opJson -> payload`
/// plus the genesis sentinel for the first record's `PrevHash`. Cross-host hash parity is
/// already pluggable via `HashFn`; the payload format is the other half. A domain whose
/// persisted streams use its own legacy chain format (Documents `"%d|%s|%s|%s"` + `"genesis"`,
/// Calc / Geom their own) matches that format with a `StreamConfig`, verifies the existing
/// streams, then `rehash`es to the canonical form — no flag-day re-hash of history. The default
/// (`OpStream.canonicalConfig`) is the `{seq,actor,op}` envelope + `""` genesis. **Phase 320
/// bumped the hash format**: `actor` is now the typed `Actor` *object* rather than a bare string,
/// so the canonical payload is no longer byte-identical to the pre-320 chain — a stream persisted
/// before Phase 320 verifies under `legacyActorConfig` and `rehash`es to the new canonical form.
type StreamConfig =
    { Payload: int -> Actor -> string -> string
      Genesis: string }

/// A checkpoint of the folded `'State` at a sequence boundary (Phase 244). `PrevHash` is
/// the boundary record's hash ("" at genesis); `Hash` chains the snapshot into the stream
/// so the checkpoint and the truncated prefix are tamper-evident. Replay can resume from a
/// snapshot instead of from the origin — bounded replay for unbounded histories (FGP 5).
type Snapshot<'State> =
    { Seq: int
      State: 'State
      PrevHash: string
      Hash: string }

/// A recorded non-deterministic effect value at a session boundary (Phase 27). The op-stream's
/// hash chain proves the *shape* of a stream was not altered, but replay of an impure effect
/// re-reads the live source — so a clock read / RNG draw / network or tool response evaluated
/// during a session is not reproducible from the file. An `EffectCapture` closes that gap: it
/// records the realized value at the boundary so replay feeds it back instead of re-evaluating,
/// hash-chained exactly like an `OpRecord` so a tampered capture fails `verifyCaptures`.
///
/// `Seq` is the capture's index in its own append-only chain; `Eff` is a stable effect-identity
/// key (which boundary — so the seed-injection helper can find a capture); `Determinism` is the
/// `Fuaran.Core.Function` determinism tag *label* (`"clock"`/`"random"`/`"network"`) — this layer
/// sits below `Function` and stays FSharp.Core-only, so it keys on the label, not the DU (a
/// consumer threads `Effect.determinismTag` in). `Value` is the realized value through the domain
/// `Codec` (raw wire JSON, embedded verbatim like an op payload), so the journal round-trips
/// byte-for-byte. A `Deterministic` effect emits no capture, so the determinism label is always
/// one of the non-deterministic three.
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

/// An attribution envelope wrapping a domain op with "who did what" provenance (Phase 81): the actor
/// and session ids, an optional turn/sequence within the session, and a host-supplied timestamp — all
/// carried INSIDE the chained op via `OpStream.Attributed.liftWitness`, not via a new witness field
/// (GP2 — the per-op witness-metadata seam F8 was rejected and stays rejected; this *wraps*, it does
/// not seam). Because the envelope rides inside the op's wire encoding, the existing hash chain covers
/// it: re-attributing a chained op breaks `verifyChain` exactly as op-tampering does — provenance is
/// tamper-evident for free, with no change to the `OpStream` surface.
///
/// Identity is **host-side vocabulary**: `Actor` / `Session` are opaque strings — Core owns no identity
/// model (a distinct axis from the chain-level typed `Actor` DU folded into `OpRecord`). `Turn` is an
/// optional ordinal within a session. `At` is a timestamp carried **as data** — Core never reads a
/// clock (the Phase 27 effect discipline); the host supplies it, `""` meaning unstamped.
type Attributed<'Op> =
    { Actor: string
      Session: string
      Turn: int option
      At: string
      Op: 'Op }

/// The recoverable outcome of a compare-and-append (`OpStream.appendIf`, Phase 79) — the CAS envelope
/// that turns the dispatcher's single-writer *process* convention into a *library* guarantee.
/// `StaleHead` means the caller's `expectedHead` no longer matched the stream's actual head: another
/// writer advanced the chain first, so the CAS refuses rather than silently clobbering it. It names
/// BOTH heads and, by that, the valid alternative — re-read the head, rebase (or re-derive
/// independence via `Ops.footprint`), and retry (GP5). `Domain` carries a domain-reducer rejection
/// (`'Rej`) surfaced from the underlying `append` once the head *did* match: `appendIf` on a matched
/// head is behaviourally identical to `append`, so a domain reject is forwarded verbatim, just
/// re-homed into this envelope. Both are typed values, never exceptions (GP4).
///
/// **Value-level CAS only.** The guard is over the *logical* chain head (`head`). File-level atomicity
/// for a persisted JSONL stream — the lock that serialises read-check-append against a file, or the
/// rename-into-place — stays host-side, since Core has no filesystem (GP3) and no process model (GP6).
/// The intended host shape is the viewer/CLI single-mutation surface: one serialised writer per stream
/// calls `appendIf`, and a losing racer receives `StaleHead` instead of a lost write.
///
/// A `Rejection`-class envelope: adding a case is additive; removing a case (or narrowing its
/// enumeration) is breaking.
[<RequireQualifiedAccess>]
type AppendRejection<'Rej> =
    | StaleHead of expected: string * actual: string
    | Domain of 'Rej

/// A stable reference to one chained record (Phase 82) — the entry an idempotency key already
/// produced. `Seq` names its position in the stream; `Hash` its chain identity — together they let
/// a retrying caller locate AND integrity-check the record its earlier attempt landed, without the
/// index holding the record itself (the ref is O(1) per key regardless of op size).
type EntryRef = { Seq: int; Hash: string }

/// A pure index of seen invocation keys → the entry each key first produced (Phase 82). A **value
/// the caller threads** — Core holds no registry state (GP6): rebuild it from any stream with
/// `KeyIndex.ofStream` (a total fold), or maintain it incrementally via the `KeyIndex` returned by
/// `OpStream.appendIdempotent` (the two agree — the rebuild-parity law). Key uniqueness scope is
/// **per-stream**: an index is only meaningful against the stream it was built from / threaded
/// alongside; cross-stream dedup is a host concern, like storage and locking (GP3/GP6).
type KeyIndex = { Seen: Map<string, EntryRef> }

/// The typed outcome of an idempotent append (Phase 82) — enumerated, never a throw (GP4).
/// `Appended` carries the advanced state, the extended stream, and the incrementally-updated
/// `KeyIndex` (so the caller threads all three forward); `Duplicate` names the entry the key
/// already produced (GP5) — the at-least-once retry *converges* on its earlier result instead of
/// double-applying, and the stream/index the caller holds are untouched (immutable values; no
/// partial write). A domain-reducer rejection is not an outcome of the idempotency guard — it is
/// forwarded on the `Result` error channel exactly as `append` forwards it.
[<RequireQualifiedAccess>]
type AppendOutcome<'Op, 'State> =
    | Appended of state: 'State * records: OpRecord<'Op> list * index: KeyIndex
    | Duplicate of existing: EntryRef

/// Companion helpers for `KeyIndex` (Phase 82) — the empty index, first-wins incremental `add`,
/// lookup, and the total rebuild fold. All pure; no mutable module state.
[<RequireQualifiedAccess>]
module KeyIndex =

    /// The empty index — the starting value for a fresh stream.
    let empty: KeyIndex = { Seen = Map.empty }

    /// Record that `key` produced `entry` — **first-wins**: a key already indexed keeps its
    /// original entry (the entry a retry must converge on is the one that landed first), so
    /// `ofStream` is literally a fold of `add` and rebuild parity holds by construction.
    let add (key: string) (entry: EntryRef) (index: KeyIndex) : KeyIndex =
        if Map.containsKey key index.Seen then
            index
        else
            { Seen = Map.add key entry index.Seen }

    /// The entry `key` already produced, or `None` for a fresh key.
    let tryFind (key: string) (index: KeyIndex) : EntryRef option = Map.tryFind key index.Seen

    /// Rebuild the index from any stream — a total fold of `add` over the records, keying each on
    /// `keyOf` (the caller's projection of an op to its invocation key — the Phase 27
    /// `Function.invocationKey` shape; a per-call parameter, no new witness field, GP2). First-wins
    /// on a duplicate-keyed stream (one built with plain `append`): the entry a key names is the
    /// first it produced.
    let ofStream (keyOf: 'Op -> string) (records: OpRecord<'Op> list) : KeyIndex =
        (empty, records)
        ||> List.fold (fun idx r -> add (keyOf r.Op) { Seq = r.Seq; Hash = r.Hash } idx)

/// Append-only hash-chained op stream + deterministic replay + JSONL persistence,
/// generic over the `StreamWitness`. The highest-genericity core layer.
module OpStream =

    // A DELIBERATE COPY of `Hash.fnv1a` (`Fuaran.Core.Tree`), kept because `OpStream` is standalone
    // by design — it takes no `Tree` dependency (DECISIONS D2), and this is the one hash the layer
    // cannot do without. It must stay VALUE-IDENTICAL to the canonical one: `Hash.fnv1a` and this
    // are compared over a shared corpus by `tests\hash-parity-probe`, so a copy that drifts is
    // caught rather than discovered in a forked chain.
    //
    // The multiply is split into 16-bit halves for the reason spelled out at `Hash.mul32`: a plain
    // `h * 16777619u` transpiles to a JavaScript multiply whose product passes 2^53, so precision is
    // lost INSIDE the operation and no trailing mask can recover it. Here that mattered more than
    // anywhere else in the substrate — this function IS the op-stream chain hash, so a divergence
    // means two hosts replaying the same log compute two different chains. No partial product below
    // exceeds 2^32. The .NET values are unchanged by the split, which is what keeps every persisted
    // chain verifying. Do not "simplify" it back.
    let private fnv1a (s: string) : string =
        let mutable h = 2166136261u

        for ch in s do
            h <- h ^^^ uint32 ch
            // 16777619 = 0x01000193 = 256 * 65536 + 403, so the prime's halves are 256 and 403.
            let lo = h &&& 0xFFFFu
            let hi = h >>> 16
            let cross = ((lo * 256u) + (hi * 403u)) &&& 0xFFFFu
            h <- ((lo * 403u) + (cross * 65536u)) &&& 0xFFFFFFFFu

        h.ToString("x8")

    /// The default portable hash: FNV-1a over `prevHash | payload`. **Portable is meant literally** —
    /// value-identical on .NET and under Fable, so a browser replaying a chain a server wrote
    /// computes the same hashes. That was not true before `0.6.0`; see the copy note above.
    let defaultHash: HashFn = fun prev payload -> fnv1a (prev + "|" + payload)

    /// Minimal JSON string escaping (Fable-clean — no System.Text.Json on the encode path).
    let private jstr (s: string) : string =
        let sb = System.Text.StringBuilder()
        sb.Append('"') |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when int c < 0x20 -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append(c) |> ignore

        sb.Append('"') |> ignore
        sb.ToString()

    /// The canonical `{seq, actor, op}` payload the chain hash is computed over. Since Phase 320
    /// the `actor` is the typed `Actor` *object* (`Actor.encode`), so altering the attribution
    /// changes the hash — attribution is folded into the integrity chain.
    let private payloadOf (seq: int) (actor: Actor) (opJson: string) : string =
        "{\"seq\":"
        + string seq
        + ",\"actor\":"
        + Actor.encode actor
        + ",\"op\":"
        + opJson
        + "}"

    /// The canonical payload binding — the `{seq,actor,op}` envelope + `""` genesis. The default
    /// for every `append` / `verifyChain` call.
    let canonicalConfig: StreamConfig = { Payload = payloadOf; Genesis = "" }

    /// The **pre-Phase-320** canonical payload — it folded the actor as a *bare JSON string*
    /// (`"actor":"alice"`) rather than the typed object. The migration entry point for a stream
    /// persisted before the typed-actor change: read it with `fromJsonlLegacyActor` (which lifts
    /// each bare-string actor to `Human`), `verifyChainWith legacyActorConfig` to confirm it is
    /// intact, then `rehash legacyActorConfig canonicalConfig` to cut over — the standard
    /// Phase-255 migration shape. Pre-320 streams only ever held `Human` actors; an `Agent`
    /// reaching this payload is migration misuse, so it folds in just the id (best effort).
    let private legacyActorPayload (seq: int) (actor: Actor) (opJson: string) : string =
        "{\"seq\":"
        + string seq
        + ",\"actor\":"
        + jstr (Actor.id actor)
        + ",\"op\":"
        + opJson
        + "}"

    /// The pre-Phase-320 canonical config (bare-string actor + `""` genesis). The `fromCfg` for a
    /// typed-actor migration `rehash`. See `legacyActorPayload`.
    let legacyActorConfig: StreamConfig =
        { Payload = legacyActorPayload
          Genesis = "" }

    let empty: OpRecord<'Op> list = []

    /// `append` under an explicit `StreamConfig` (Phase 255) — the chain payload + genesis come
    /// from `cfg` rather than the canonical binding. Used during a format migration to extend a
    /// stream in its own legacy chain format; ordinary callers use `append`.
    let appendWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, 'Rej> =
        match w.Apply op state with
        | Error e -> Error e
        | Ok state' ->
            let seq = List.length records

            let prev =
                match List.tryLast records with
                | Some r -> r.Hash
                | None -> cfg.Genesis

            let payload = cfg.Payload seq actor (w.Encode op)
            let h = hashFn prev payload

            Ok(
                state',
                records
                @ [ { Seq = seq
                      Actor = actor
                      Op = op
                      PrevHash = prev
                      Hash = h } ]
            )

    /// Apply an op to the state; on success, chain a record onto the stream. Returns
    /// the new state and the extended record list, or the domain rejection unchanged.
    let append
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, 'Rej> =
        appendWith canonicalConfig hashFn w actor op state records

    /// `firstChainBreak` under an explicit `StreamConfig` (Phase 21 + Phase 255) — the localising
    /// verifier. Walks the chain and returns the first record whose sequence, prev-link, or hash
    /// fails under `cfg`; `None` for an intact chain.
    let firstChainBreakWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : ChainBreak option =
        let rec go (prev: string) (i: int) =
            function
            | [] -> None
            | (r: OpRecord<'Op>) :: rest ->
                if r.Seq <> i then
                    Some
                        { Index = i
                          Reason = "sequence-number mismatch"
                          Expected = string i
                          Got = string r.Seq }
                elif r.PrevHash <> prev then
                    Some
                        { Index = i
                          Reason = "prev-hash link broken"
                          Expected = prev
                          Got = r.PrevHash }
                else
                    // compute the chain hash only after the cheap seq/prev checks pass
                    let expectedHash = hashFn prev (cfg.Payload r.Seq r.Actor (w.Encode r.Op))

                    if r.Hash <> expectedHash then
                        Some
                            { Index = i
                              Reason = "hash mismatch (tampered op/actor/seq)"
                              Expected = expectedHash
                              Got = r.Hash }
                    else
                        go r.Hash (i + 1) rest

        go cfg.Genesis 0 records

    /// The first integrity fault in a canonical-config chain (Phase 21), or `None` if intact.
    let firstChainBreak
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : ChainBreak option =
        firstChainBreakWith canonicalConfig hashFn w records

    /// `verifyChain` under an explicit `StreamConfig` (Phase 255) — confirm a stream in a given
    /// chain format is intact. The migration entry point: a domain verifies its persisted legacy
    /// streams under their own config before `rehash`ing them to canonical. Re-expressed over
    /// `firstChainBreakWith` (Phase 21) — one definition of integrity, localising or boolean.
    let verifyChainWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : bool =
        firstChainBreakWith cfg hashFn w records |> Option.isNone

    /// Recompute the chain from the records and confirm every link. Detects reordering,
    /// tampering with an op, and a broken prev-link.
    let verifyChain (hashFn: HashFn) (w: StreamWitness<'Op, 'State, 'Rej>) (records: OpRecord<'Op> list) : bool =
        verifyChainWith canonicalConfig hashFn w records

    /// Migrate a chain from one payload format to another (Phase 255). Verifies the source
    /// records under `fromCfg` first — a chain that does not verify under its declared legacy
    /// format is a migration the caller must not silently re-bless, so it is a named `Error` —
    /// then re-derives every `PrevHash` / `Hash` under `toCfg` (the ops / actors / seqs are the
    /// source of truth; only the hash chain changes). The result `verifyChain`s under `toCfg`.
    let rehash
        (fromCfg: StreamConfig)
        (toCfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : Result<OpRecord<'Op> list, string> =
        if not (verifyChainWith fromCfg hashFn w records) then
            Error "OpStream.rehash: source chain does not verify under fromCfg"
        else
            let rec go (prev: string) acc =
                function
                | [] -> List.rev acc
                | (r: OpRecord<'Op>) :: rest ->
                    let payload = toCfg.Payload r.Seq r.Actor (w.Encode r.Op)
                    let h = hashFn prev payload

                    let r' = { r with PrevHash = prev; Hash = h }

                    go h (r' :: acc) rest

            Ok(go toCfg.Genesis [] records)

    /// Re-apply every op over a base state. Replay is a fold of `Apply` — op-stream
    /// replay is a special case of re-derivation.
    let replay
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State, int * 'Rej> =
        let rec go i st =
            function
            | [] -> Ok st
            | (r: OpRecord<'Op>) :: rest ->
                match w.Apply r.Op st with
                | Ok st' -> go (i + 1) st' rest
                | Error e -> Error(i, e)

        go 0 state0 records

    /// Best-effort replay: fold `Apply` over the records, **skipping** any op that rejects rather
    /// than halting at the first failure. The projection semantics a *partial* / *filtered* stream
    /// needs — e.g. a subjective view (Worldbuilder's `extract`) whose surviving ops reference a
    /// node the projection never admitted: that op is dropped, not fatal. `replay` is fail-fast;
    /// this is fail-soft. Returns the folded state **and** the `(index, rejection)` pairs that were
    /// skipped, so a caller can inspect what was dropped (a domain that discards them — as
    /// Worldbuilder's own lenient replay does — just ignores the list).
    let replayLenient
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        : 'State * (int * 'Rej) list =
        let rec go i st skipped =
            function
            | [] -> st, List.rev skipped
            | (r: OpRecord<'Op>) :: rest ->
                match w.Apply r.Op st with
                | Ok st' -> go (i + 1) st' skipped rest
                | Error e -> go (i + 1) st ((i, e) :: skipped) rest

        go 0 state0 [] records

    /// One JSON object per line. The op payload is embedded as raw JSON (the domain's
    /// own `Encode` output), so a round-trip preserves it byte-for-byte.
    let toJsonl (w: StreamWitness<'Op, 'State, 'Rej>) (records: OpRecord<'Op> list) : string =
        records
        |> List.map (fun r ->
            "{\"seq\":"
            + string r.Seq
            + ",\"actor\":"
            + Actor.encode r.Actor
            + ",\"op\":"
            + w.Encode r.Op
            + ",\"prevHash\":"
            + jstr r.PrevHash
            + ",\"hash\":"
            + jstr r.Hash
            + "}")
        |> String.concat "\n"

    /// A self-contained, FSharp.Core-only line scanner for JSONL records. It splits the
    /// flat top-level object into its five fields, capturing the `op` value's *raw* span
    /// byte-for-byte (so the domain decoder receives exactly what `Encode` produced).
    /// Standalone — `OpStream` takes no `Core.Wire` dependency (decision D2) and stays
    /// Fable-clean, so `fromJsonl` now runs under both pipelines (Phase 241).
    // Self-contained JSONL line scanner. It keeps each field's raw value span byte-for-byte (so an
    // `op` round-trips identically), which `Wire.Json.parse` — yielding a lossy `JVal` — cannot, and
    // it stays FSharp.Core-only: `OpStream` takes no `Wire` dependency (DECISIONS.md D2). Structural
    // faults report the scanner's own fault index (`start` / `i`) — a more precise position than a
    // from-scratch reparse — through the surrounding `try/with` → `Result`, never an exception.
    module private Jsonl =

        /// Unescape a raw JSON string token (surrounding quotes included).
        let unquote (raw: string) : string =
            let inner = raw.Substring(1, raw.Length - 2)
            let sb = System.Text.StringBuilder()
            let n = inner.Length
            let mutable i = 0

            let hex (c: char) =
                if c >= '0' && c <= '9' then int c - int '0'
                elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
                else int c - int 'A' + 10

            while i < n do
                let c = inner.[i]

                if c = '\\' && i + 1 < n then
                    let e = inner.[i + 1]
                    i <- i + 2

                    match e with
                    | '"' -> sb.Append('"') |> ignore
                    | '\\' -> sb.Append('\\') |> ignore
                    | '/' -> sb.Append('/') |> ignore
                    | 'n' -> sb.Append('\n') |> ignore
                    | 'r' -> sb.Append('\r') |> ignore
                    | 't' -> sb.Append('\t') |> ignore
                    | 'b' -> sb.Append('\b') |> ignore
                    | 'f' -> sb.Append('\f') |> ignore
                    | 'u' when i + 3 < n ->
                        let code =
                            (hex inner.[i] <<< 12)
                            + (hex inner.[i + 1] <<< 8)
                            + (hex inner.[i + 2] <<< 4)
                            + hex inner.[i + 3]

                        i <- i + 4
                        sb.Append(char code) |> ignore
                    // A truncated `\u` escape at end of input (Phase 45): emit the `u` literally and let
                    // the loop consume any remaining hex digits, rather than reading past the end and
                    // throwing an opaque IndexOutOfRangeException through the surrounding try/with.
                    | 'u' -> sb.Append('u') |> ignore
                    | _ -> sb.Append(e) |> ignore
                else
                    sb.Append(c) |> ignore
                    i <- i + 1

            sb.ToString()

        /// Index just past a complete string token starting at the opening quote.
        let skipString (s: string) (start: int) : int =
            let n = s.Length
            let mutable i = start + 1
            let mutable fin = false

            while not fin do
                if i >= n then
                    failwith (sprintf "OpStream.fromJsonl: unterminated string (opened at position %d)" start)

                match s.[i] with
                | '\\' -> i <- i + 2
                | '"' ->
                    i <- i + 1
                    fin <- true
                | _ -> i <- i + 1

            i

        /// Index just past a complete JSON value starting at `start` (no leading ws).
        let skipValue (s: string) (start: int) : int =
            let n = s.Length
            let mutable i = start

            match s.[i] with
            | '"' -> skipString s i
            | '{'
            | '[' ->
                i <- i + 1
                let mutable depth = 1

                while depth > 0 do
                    if i >= n then
                        failwith (sprintf "OpStream.fromJsonl: unterminated container (opened at position %d)" start)

                    match s.[i] with
                    | '"' -> i <- skipString s i
                    | '{'
                    | '[' ->
                        depth <- depth + 1
                        i <- i + 1
                    | '}'
                    | ']' ->
                        depth <- depth - 1
                        i <- i + 1
                    | _ -> i <- i + 1

                i
            | _ ->
                let isEnd c =
                    c = ',' || c = '}' || c = ']' || c = ' ' || c = '\t' || c = '\n' || c = '\r'

                while i < n && not (isEnd s.[i]) do
                    i <- i + 1

                i

        /// `(key, raw-value)` pairs of a flat top-level object; values kept verbatim.
        let topFields (line: string) : (string * string) list =
            let s = line.Trim()
            let n = s.Length
            let mutable i = 0

            let skipWs () =
                while i < n && (let c = s.[i] in c = ' ' || c = '\t' || c = '\n' || c = '\r') do
                    i <- i + 1

            skipWs ()

            if i >= n || s.[i] <> '{' then
                failwith (sprintf "OpStream.fromJsonl: expected a JSON object at position %d" i)

            i <- i + 1
            let fields = ResizeArray<string * string>()
            skipWs ()

            if i < n && s.[i] = '}' then
                ()
            else
                let mutable go = true

                while go do
                    skipWs ()
                    let ks = skipString s i
                    let key = unquote (s.Substring(i, ks - i))
                    i <- ks
                    skipWs ()

                    if i >= n || s.[i] <> ':' then
                        failwith (sprintf "OpStream.fromJsonl: expected ':' at position %d" i)

                    i <- i + 1
                    skipWs ()
                    let vs = skipValue s i
                    fields.Add((key, s.Substring(i, vs - i).Trim()))
                    i <- vs
                    skipWs ()

                    if i < n && s.[i] = ',' then
                        i <- i + 1
                    elif i < n && s.[i] = '}' then
                        go <- false
                    else
                        failwith (sprintf "OpStream.fromJsonl: expected ',' or '}' at position %d" i)

            // First-wins on a duplicate key (Phase 45) — the consumers `Map.ofList` this, which is
            // last-wins, while every `JVal` decoder (`Decode.getProp`) is first-wins. Dedup here so the
            // scanner and the decoders agree on which value a repeated key resolves to.
            let seen = System.Collections.Generic.HashSet<string>()

            [ for (k, v) in fields do
                  if seen.Add k then
                      yield (k, v) ]

    /// Decode the `actor` field's raw span into a typed `Actor` (Phase 320). The new canonical form
    /// is the object `{"kind":"human"|"agent", ...}` emitted by `Actor.encode`; this re-uses the
    /// flat-object scanner (`Jsonl.topFields`) over that span. An unrecognised / absent `kind` falls
    /// back to `Human` over whatever `id` is present, so a hand-edited record degrades rather than
    /// throwing.
    let private actorOfRaw (raw: string) : Actor =
        let fields = Jsonl.topFields raw |> Map.ofList

        let get k =
            match Map.tryFind k fields with
            | Some v -> Jsonl.unquote v
            | None -> ""

        match get "kind" with
        | "agent" -> Agent(get "model", get "version", get "id")
        | _ -> Human(get "id")

    /// The single JSONL scanner, parameterised on how the `actor` raw span decodes to a typed
    /// `Actor` (`actorOfRaw` for the canonical object form; the bare-string lift for the legacy
    /// reader). Returns `(records, rawSnapshotLines)`.
    let private scanJsonlWithSnapshots
        (actorOf: string -> Actor)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list * string list, string> =
        let lines =
            text.Replace("\r\n", "\n").Split('\n')
            |> Array.filter (fun l -> l.Trim() <> "")
            |> Array.toList

        let rec go i recs snaps =
            function
            | [] -> Ok(List.rev recs, List.rev snaps)
            | (line: string) :: rest ->
                let parsed =
                    try
                        let fields = Jsonl.topFields line |> Map.ofList

                        if Map.containsKey "snapshot" fields then
                            Ok(Choice2Of2 line)
                        else
                            let get k =
                                match Map.tryFind k fields with
                                | Some v -> v
                                | None -> failwith ("missing field " + k)

                            match w.Decode(get "op") with
                            | Error e -> Error e
                            | Ok op ->
                                Ok(
                                    Choice1Of2
                                        { Seq = int (get "seq")
                                          Actor = actorOf (get "actor")
                                          Op = op
                                          PrevHash = Jsonl.unquote (get "prevHash")
                                          Hash = Jsonl.unquote (get "hash") }
                                )
                    with ex ->
                        Error ex.Message

                match parsed with
                | Error e -> Error(sprintf "line %d: %s" i e)
                | Ok(Choice1Of2 r) -> go (i + 1) (r :: recs) snaps rest
                | Ok(Choice2Of2 s) -> go (i + 1) recs (s :: snaps) rest

        go 0 [] [] lines

    /// Parse JSONL into `(records, rawSnapshotLines)` (Phase 16) — the snapshot-aware reader and the
    /// single JSONL scanner (`fromJsonl` is the records-only wrapper over it). The records are decoded
    /// by the witness; each snapshot line is returned verbatim (its `state` field still embedded raw)
    /// so a caller can recover the base state with `snapshotFromJsonl` / `snapshotFromJsonlResult` and
    /// resume via `replayFrom`. A non-empty snapshot list means the file was compacted — replaying the
    /// records from origin would be wrong. Fully portable (Phase 241). A malformed line — a witness
    /// decode `Error` or a structural fault — yields a `line N: <reason>` `Error`, never an exception
    /// (Phase 252). The `op` raw span is preserved byte-for-byte, so a round-trip is identical. Since
    /// Phase 320 the `actor` field is the typed object; use `fromJsonlLegacyActor` for a pre-320 file.
    let fromJsonlWithSnapshots
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list * string list, string> =
        scanJsonlWithSnapshots actorOfRaw w text

    /// Parse JSONL back into records, **silently dropping snapshot lines** (Phase 244) — correct for
    /// a linear, never-compacted stream. A thin wrapper over `fromJsonlWithSnapshots` (the single
    /// scanner); a `compact` output (snapshot + tail) read this way loses its base state with no
    /// signal and replaying from origin is then wrong, so read a possibly-compacted file with
    /// `fromJsonlWithSnapshots` instead. Returns `Result` (Phase 252); the `op` raw span round-trips.
    let fromJsonl (w: StreamWitness<'Op, 'State, 'Rej>) (text: string) : Result<OpRecord<'Op> list, string> =
        fromJsonlWithSnapshots w text |> Result.map fst

    /// Read a **pre-Phase-320** JSONL file (Phase 320 migration) — the `actor` field is still a bare
    /// JSON string, which this lifts to the typed `Human` case. The returned records carry the file's
    /// stored `PrevHash` / `Hash` (computed under the old bare-string payload), so they
    /// `verifyChainWith legacyActorConfig` and then `rehash legacyActorConfig canonicalConfig` to the
    /// new typed form. Snapshot lines are dropped (a compacted pre-320 file is read with the scanner
    /// directly). The migration path for an existing Core stream into the typed-actor hash format.
    let fromJsonlLegacyActor (w: StreamWitness<'Op, 'State, 'Rej>) (text: string) : Result<OpRecord<'Op> list, string> =
        scanJsonlWithSnapshots (Jsonl.unquote >> Actor.ofLegacyString) w text
        |> Result.map fst

    /// `fromJsonl` + a chain-integrity gate (Phase 13). Parses the records, then `verifyChain`s
    /// them — a broken prev-link / reordered / tampered record is a named `Error`, not a silent
    /// `Ok` of a corrupt stream. For a linear, uncompacted stream; a compacted file (snapshot +
    /// tail) does not start its chain at genesis, so read it with `fromJsonlWithSnapshots` and
    /// verify the boundary with `verifyAcross` instead.
    let fromJsonlVerified
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list, string> =
        fromJsonl w text
        |> Result.bind (fun recs ->
            match firstChainBreak hashFn w recs with
            | None -> Ok recs
            | Some b -> Error(sprintf "OpStream.fromJsonlVerified: chain breaks at record %d — %s" b.Index b.Reason))

    // ---- snapshot / compaction (Phase 244) ----

    /// The hash payload binding a snapshot to its boundary (state + seq). The **strict** binding —
    /// the `'State` is folded into the hash via `stateEncode`, so a swapped state fails `verifyAcross`.
    let private snapPayload (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
        "{\"snapshot\":true,\"seq\":"
        + string snap.Seq
        + ",\"state\":"
        + stateEncode snap.State
        + "}"

    /// The **chain-only** hash payload (Phase 258) — the `'State` is *not* folded in (no `stateEncode`
    /// required), so the snapshot hash binds `PrevHash` (via `hashFn`) + `Seq` only. The `stateHashed`
    /// discriminator keeps this pre-image distinct from the strict one, so a chain-only snapshot can
    /// never collide with a strict snapshot at the same boundary. Integrity: the prefix link
    /// (`PrevHash`), the boundary position (`Seq`), and the tail chain stay tamper-evident, but the
    /// stored `'State` is trusted — a swapped `'State` is NOT detected (the domain's chosen trade-off).
    let private snapPayloadChainOnly (snap: Snapshot<'State>) : string =
        "{\"snapshot\":true,\"seq\":" + string snap.Seq + ",\"stateHashed\":false}"

    /// The snapshot hash pre-image threaded on an *optional* state encoder (Phase 258): `Some enc` →
    /// the strict, state-hashed binding (`snapPayload`); `None` → the chain-only binding
    /// (`snapPayloadChainOnly`). The one place the strict-vs-chain-only mode is decided, so every
    /// snapshot entry point (`snapshotAtOpt`, `verifyAcrossWithOpt`) stays a thin wrapper over it.
    let private snapPayloadWith (stateEncode: ('State -> string) option) (snap: Snapshot<'State>) : string =
        match stateEncode with
        | Some enc -> snapPayload enc snap
        | None -> snapPayloadChainOnly snap

    /// Capture a snapshot at boundary `atSeq` under an *optional* state encoder (Phase 258) — the
    /// generic form `snapshotAt` (strict) and `snapshotAtChainOnly` both delegate to. `Some enc` hashes
    /// the `'State` into the checkpoint (strict — a swapped state is caught); `None` binds chain-only
    /// (the hash covers `PrevHash` + `Seq`, the stored `'State` is trusted). A domain whose `'State` is
    /// a whole tree can pass `None` to adopt bounded replay without a canonical state encoder, and add
    /// state-hashing (`Some`) later — the trade-off is the domain's, not the substrate's.
    let snapshotAtOpt
        (hashFn: HashFn)
        (stateEncode: ('State -> string) option)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State>, string> =
        if atSeq < 0 || atSeq > List.length records then
            Error "OpStream.snapshotAt: seq out of range"
        else
            match replay w state0 (records |> List.truncate atSeq) with
            | Error(i, _) -> Error(sprintf "OpStream.snapshotAt: prefix replay failed at %d" i)
            | Ok state ->
                let prevHash =
                    if atSeq = 0 then
                        ""
                    else
                        (List.item (atSeq - 1) records).Hash

                let snap0 =
                    { Seq = atSeq
                      State = state
                      PrevHash = prevHash
                      Hash = "" }

                Ok
                    { snap0 with
                        Hash = hashFn prevHash (snapPayloadWith stateEncode snap0) }

    /// Capture a snapshot at boundary `atSeq` — after applying `records[0 .. atSeq-1]` from
    /// `state0`. The `'State` is hashed via `stateEncode` so the checkpoint is tamper-evident.
    /// The strict convenience wrapper over `snapshotAtOpt (Some stateEncode)` — byte-identical to the
    /// pre-Phase-258 behaviour.
    let snapshotAt
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State>, string> =
        snapshotAtOpt hashFn (Some stateEncode) w state0 records atSeq

    /// Capture a **chain-only** snapshot at boundary `atSeq` (Phase 258) — no `stateEncode` required.
    /// The checkpoint's hash binds only `PrevHash` + `Seq`, so `verifyAcrossChainOnly` confirms the
    /// prefix link + tail chain but NOT the stored `'State`. Bounded replay (`replayFrom`) still
    /// reproduces the origin state exactly (it folds `Apply` over the tail from `snap.State`). The
    /// chain-only convenience wrapper over `snapshotAtOpt None`.
    let snapshotAtChainOnly
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State>, string> =
        snapshotAtOpt hashFn None w state0 records atSeq

    /// Compact a stream at `atSeq` into `(snapshot, tail)` that replays identically to the
    /// full stream from `state0` — the prefix is discarded, the chain stays verifiable.
    let compact
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
        snapshotAt hashFn stateEncode w state0 records atSeq
        |> Result.map (fun snap -> snap, records |> List.skip atSeq)

    /// Compact a stream at `atSeq` into `(chain-only snapshot, tail)` (Phase 258) — the chain-only
    /// analogue of `compact`, requiring no `stateEncode`. The tail replays identically to the full
    /// stream from `state0` (`replayFrom` is unaffected by the snapshot's hash mode); the boundary is
    /// verified with `verifyAcrossChainOnly` rather than `verifyAcross`.
    let compactChainOnly
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
        snapshotAtChainOnly hashFn w state0 records atSeq
        |> Result.map (fun snap -> snap, records |> List.skip atSeq)

    /// Replay the tail ops from a snapshot's state — bounded replay (a plain fold of Apply,
    /// no origin re-derivation). Reproduces the state `replay`-from-origin would produce.
    let replayFrom
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : Result<'State, int * 'Rej> =
        replay w snap.State tail

    /// `verifyAcross` under an explicit `StreamConfig` (Phase 14) — the tail-record chain payload
    /// comes from `cfg.Payload` rather than the hard-wired canonical `payloadOf`, completing the
    /// Phase-255 `StreamConfig` seam over the snapshot surface. A domain whose streams use a legacy
    /// chain format (and so `verifyChainWith`/`appendWith` under its own `cfg`) can now verify a
    /// snapshot/compaction boundary too. The snapshot's own hash payload (`snapPayload`) is fixed —
    /// it is the checkpoint format, not the per-op chain format — so it is unaffected by `cfg`.
    let verifyAcrossWithOpt
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (stateEncode: ('State -> string) option)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        let snapOk = snap.Hash = hashFn snap.PrevHash (snapPayloadWith stateEncode snap)

        let rec go (prev: string) (i: int) =
            function
            | [] -> true
            | (r: OpRecord<'Op>) :: rest ->
                let payload = cfg.Payload r.Seq r.Actor (w.Encode r.Op)

                r.Seq = i
                && r.PrevHash = prev
                && r.Hash = hashFn prev payload
                && go r.Hash (i + 1) rest

        snapOk && go snap.PrevHash snap.Seq tail

    /// `verifyAcross` under an explicit `StreamConfig`, **strict** state-hashed mode (Phase 14). The
    /// convenience wrapper over `verifyAcrossWithOpt (Some stateEncode)` — byte-identical to its
    /// pre-Phase-258 behaviour. Detects a tampered prefix-link, tail, seq, snapshot hash, **and** a
    /// swapped `'State` (the state is folded into the snapshot hash).
    let verifyAcrossWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        verifyAcrossWithOpt cfg hashFn (Some stateEncode) w snap tail

    /// `verifyAcross` under an explicit `StreamConfig`, **chain-only** mode (Phase 258) — no
    /// `stateEncode`. Confirms the snapshot's own hash (over `PrevHash` + `Seq`), the prefix link, and
    /// the tail chain. **Does NOT detect a swapped `'State`** — that is the chain-only trade-off; use
    /// `verifyAcrossWith` (strict) for independent state-tamper detection. A domain on a legacy chain
    /// format (its own `cfg`) that snapshots a large/awkward `'State` verifies its boundary here.
    let verifyAcrossChainOnlyWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        verifyAcrossWithOpt cfg hashFn None w snap tail

    /// Verify the chain across the truncation boundary: the snapshot's own hash is intact, and the
    /// tail records continue the chain from the snapshot (prev-link + sequence). The canonical-config
    /// wrapper over `verifyAcrossWith` — byte-identical to the pre-Phase-14 behaviour.
    let verifyAcross
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        verifyAcrossWith canonicalConfig hashFn stateEncode w snap tail

    /// Verify a **chain-only** snapshot boundary under the canonical config (Phase 258) — the
    /// canonical-config wrapper over `verifyAcrossChainOnlyWith`. Confirms the prefix link + tail
    /// chain; a swapped `'State` is (by construction) NOT detected.
    let verifyAcrossChainOnly
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        verifyAcrossChainOnlyWith canonicalConfig hashFn w snap tail

    /// One snapshot line (the `state` field embedded as raw JSON via `stateEncode`). The **strict**
    /// (state-hashed) line — no `stateHashed` field, so it round-trips byte-identically to the
    /// pre-Phase-258 format and `snapshotStateHashedFromJsonl` reads it as strict (the absent-default).
    let snapshotToJsonl (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
        "{\"snapshot\":true,\"seq\":"
        + string snap.Seq
        + ",\"state\":"
        + stateEncode snap.State
        + ",\"prevHash\":"
        + jstr snap.PrevHash
        + ",\"hash\":"
        + jstr snap.Hash
        + "}"

    /// One **chain-only** snapshot line (Phase 258) — carries `"stateHashed":false`, so a reader
    /// (`snapshotStateHashedFromJsonl`) verifies it with `verifyAcrossChainOnly` rather than the strict
    /// `verifyAcross`. The `state` is still persisted (a reload needs it for `replayFrom`), but here
    /// `stateEncode` is a *storage* serialiser, not the canonical hash pre-image — it never enters the
    /// hash, so it need not be byte-stable across hosts. Decode the line back with `snapshotFromJsonl`
    /// exactly as a strict line (the extra flag is ignored by the state decoder).
    let snapshotToJsonlChainOnly (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
        "{\"snapshot\":true,\"seq\":"
        + string snap.Seq
        + ",\"state\":"
        + stateEncode snap.State
        + ",\"stateHashed\":false,\"prevHash\":"
        + jstr snap.PrevHash
        + ",\"hash\":"
        + jstr snap.Hash
        + "}"

    /// Read the `stateHashed` discriminator of a snapshot line (Phase 258): `false` only when the line
    /// explicitly carries `"stateHashed":false` (a chain-only line), `true` otherwise — so a strict
    /// line and any pre-Phase-258 line (no such field) both read as strict. A reader picks
    /// `verifyAcross` (when `true`) vs `verifyAcrossChainOnly` (when `false`) after decoding the
    /// snapshot with `snapshotFromJsonl`. Structural faults degrade to `true` (strict), the safe default.
    let snapshotStateHashedFromJsonl (line: string) : bool =
        try
            match Jsonl.topFields line |> Map.ofList |> Map.tryFind "stateHashed" with
            | Some v -> v.Trim() <> "false"
            | None -> true
        with _ ->
            true

    /// Parse a snapshot line with a `Result`-returning state decoder (Phase 15) — the snapshot
    /// analogue of `StreamWitness.Decode`, so a failing state decode is a typed `Error` threaded
    /// into the line envelope rather than an exception routed through `try/with`. Structural faults
    /// (missing field, bad scanner state) are still named `Error`s. This is the totality-discipline
    /// seam (GP4); `snapshotFromJsonl` is the convenience wrapper for a state decode that cannot fail.
    let snapshotFromJsonlResult
        (stateDecode: string -> Result<'State, string>)
        (line: string)
        : Result<Snapshot<'State>, string> =
        let parsed =
            try
                let fields = Jsonl.topFields line |> Map.ofList

                let get k =
                    match Map.tryFind k fields with
                    | Some v -> v
                    | None -> failwith ("missing field " + k)

                Ok(int (get "seq"), get "state", Jsonl.unquote (get "prevHash"), Jsonl.unquote (get "hash"))
            with ex ->
                Error ex.Message

        parsed
        |> Result.bind (fun (seq, stateRaw, prevHash, hash) ->
            stateDecode stateRaw
            |> Result.map (fun state ->
                { Seq = seq
                  State = state
                  PrevHash = prevHash
                  Hash = hash }))

    /// Parse a snapshot line (the `state` field handed to a total `stateDecode`). Portable —
    /// runs under both pipelines (Phase 241 scanner). Returns `Result` (Phase 252) — a malformed
    /// line is a named `Error`, never an exception. The convenience wrapper over
    /// `snapshotFromJsonlResult` for a state decode that cannot fail; if `stateDecode` throws, the
    /// exception is still caught and named (use `snapshotFromJsonlResult` to surface a typed
    /// decode failure instead).
    let snapshotFromJsonl (stateDecode: string -> 'State) (line: string) : Result<Snapshot<'State>, string> =
        snapshotFromJsonlResult
            (fun s ->
                try
                    Ok(stateDecode s)
                with ex ->
                    Error ex.Message)
            line

    // ---- determinism capture / replay (Phase 27) ----

    /// The determinism label below which an effect needs no capture — the `Deterministic` tag.
    /// `Fuaran.Core.Function`'s `Effect.determinismTag` projects `Deterministic` to exactly this
    /// string; the capture seam keys on the label rather than referencing the DU (this layer sits
    /// below `Function` and stays FSharp.Core-only). The non-deterministic labels are `"clock"`,
    /// `"random"`, `"network"`.
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

    /// The record seam (Phase 27). Evaluate `effect` once; for a non-`Deterministic` tag, journal
    /// the realized value (encoded via the domain `Codec`) into the hash-chained capture log and
    /// return `(value, extended-log)`; for `deterministicTag` it is pass-through — the value is
    /// returned and nothing is captured (a deterministic effect is reproducible from its inputs).
    /// `encode` is a per-call parameter (GP2 — no new witness field), so one log can hold captures
    /// of heterogeneous value types side by side. `Eff` identifies the boundary for the
    /// seed-injection helper.
    let captureEffect
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
            let seq = List.length captures

            let prev =
                match List.tryLast captures with
                | Some c -> c.Hash
                | None -> ""

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

    /// The seed-injection helper (Phase 27) — surface the recorded value of the first capture for
    /// an effect identity. An effect that reads non-determinism *internally* (a seeded RNG whose
    /// individual draws are not captured) records its seed as the capture value; a well-behaved
    /// consumer reseeds from this so the internal trajectory replays automatically rather than by
    /// discipline. `None` when the journal holds no capture for `eff`.
    let capturedSeed (eff: string) (captures: EffectCapture list) : string option =
        captures |> List.tryPick (fun c -> if c.Eff = eff then Some c.Value else None)

    /// The first integrity fault in a capture chain (Phase 27), or `None` if intact — the capture
    /// analogue of `firstChainBreak`. Walks the chain and returns the first capture whose
    /// sequence, prev-link, or hash fails; reuses `ChainBreak` so a capture break localises
    /// exactly as an op break does (Phase 21).
    let firstCaptureBreak (hashFn: HashFn) (captures: EffectCapture list) : ChainBreak option =
        let rec go (prev: string) (i: int) =
            function
            | [] -> None
            | (c: EffectCapture) :: rest ->
                if c.Seq <> i then
                    Some
                        { Index = i
                          Reason = "sequence-number mismatch"
                          Expected = string i
                          Got = string c.Seq }
                elif c.PrevHash <> prev then
                    Some
                        { Index = i
                          Reason = "prev-hash link broken"
                          Expected = prev
                          Got = c.PrevHash }
                else
                    let expectedHash = hashFn prev (capturePayload c.Seq c.Eff c.Determinism c.Value)

                    if c.Hash <> expectedHash then
                        Some
                            { Index = i
                              Reason = "hash mismatch (tampered capture)"
                              Expected = expectedHash
                              Got = c.Hash }
                    else
                        go c.Hash (i + 1) rest

        go "" 0 captures

    /// Recompute the capture chain and confirm every link — the capture analogue of `verifyChain`.
    /// A tampered captured value (or a reordered / dropped capture) fails this, so a recorded
    /// effect value is tamper-evident exactly like an op.
    let verifyCaptures (hashFn: HashFn) (captures: EffectCapture list) : bool =
        firstCaptureBreak hashFn captures |> Option.isNone

    /// One JSON object per line for a capture log — the `value` field embedded as raw JSON (the
    /// domain `Codec` output), so a round-trip preserves it byte-for-byte. Lets a recorded session
    /// persist its captures alongside its ops, so "replay exactly what happened" holds *from the
    /// file*.
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

    /// Parse a capture log back from JSONL (Phase 27) — the `value` raw span is preserved
    /// byte-for-byte (the domain decoder receives exactly what `Codec` produced), so a round-trip
    /// is identical and the chain still `verifyCaptures`. Uses the same self-contained, Fable-clean
    /// line scanner as `fromJsonl`; a malformed line is a named `Error`, never an exception.
    let captureFromJsonl (text: string) : Result<EffectCapture list, string> =
        let lines =
            text.Replace("\r\n", "\n").Split('\n')
            |> Array.filter (fun l -> l.Trim() <> "")
            |> Array.toList

        let rec go i acc =
            function
            | [] -> Ok(List.rev acc)
            | (line: string) :: rest ->
                let parsed =
                    try
                        let fields = Jsonl.topFields line |> Map.ofList

                        let get k =
                            match Map.tryFind k fields with
                            | Some v -> v
                            | None -> failwith ("missing field " + k)

                        Ok
                            { Seq = int (get "seq")
                              Eff = Jsonl.unquote (get "eff")
                              Determinism = Jsonl.unquote (get "det")
                              Value = get "value"
                              PrevHash = Jsonl.unquote (get "prevHash")
                              Hash = Jsonl.unquote (get "hash") }
                    with ex ->
                        Error ex.Message

                match parsed with
                | Error e -> Error(sprintf "line %d: %s" i e)
                | Ok c -> go (i + 1) (c :: acc) rest

        go 0 [] lines

    // ---- cryptographic attestation (Phase 320) ----

    /// The default no-op attestation sink: never signs (`Sign` ⇒ `None`) and verifies nothing
    /// (`Verify` ⇒ `false`). Signing is opt-in — a stream that does not plug in a real sink behaves
    /// exactly as before. Enterprise hosts supply a KMS / HSM-backed `IAttestationSink`.
    let noAttestation: IAttestationSink =
        { new IAttestationSink with
            member _.Sign _ = None
            member _.Verify _ _ = false }

    /// The current head of a chain — the last record's `Hash`, or the genesis sentinel `""` for an
    /// empty chain. The thing an attestation signs (the hash-chain attests the whole prefix).
    let head (records: OpRecord<'Op> list) : string =
        match List.tryLast records with
        | Some r -> r.Hash
        | None -> ""

    /// Attest the current head of a chain at a commit / publish boundary (Phase 320). Signs the head
    /// hash via `sink` — O(commits), not O(ops), since the hash-chain already binds every prior op
    /// into the head. `None` from the no-op sink. The signed `Attestation` plus deterministic replay
    /// is the replay-as-provenance contract: a verifier re-derives the head from the op log, then
    /// `verifyAttestation`s the signature against it.
    let attestHead (sink: IAttestationSink) (records: OpRecord<'Op> list) : Attestation option = sink.Sign(head records)

    /// Re-verify an attestation against a chain's *current* head (Phase 320). Independent
    /// re-verification: a third party recomputes the head from the records (`verifyChain` proves the
    /// chain is intact; `head` reads its tip) and checks the signature covers exactly that head.
    let verifyAttestation (sink: IAttestationSink) (attestation: Attestation) (records: OpRecord<'Op> list) : bool =
        sink.Verify attestation (head records)

    // ---- compare-and-append / optimistic concurrency (Phase 79) ----

    /// Compare-and-append: chain `op` **only if** the stream's current head matches `expectedHead`
    /// (Phase 79). On a match, behaviourally identical to `append` — the op is applied, the record is
    /// chained, and `Ok (state', records')` is returned; a domain-reducer rejection is forwarded as
    /// `Error (AppendRejection.Domain rej)`. On a mismatch (another writer advanced the chain since the
    /// caller read the head), NO mutation happens — `records` is an immutable value the caller still
    /// holds, so there is no partial write — and the result is
    /// `Error (AppendRejection.StaleHead (expectedHead, actualHead))`, naming both heads so the caller
    /// can re-read, rebase (or re-derive independence via `Ops.footprint`), and retry (GP5). Total —
    /// never throws (GP4).
    ///
    /// **Value-level CAS only.** The guard is over the logical chain head (`head`); file-level atomicity
    /// for a persisted stream (the lock that serialises read-check-append against a JSONL file) stays
    /// host-side (GP3/GP6) — see `AppendRejection`. `appendIf` is the primitive a single-writer host (the
    /// viewer/CLI single-mutation surface) builds that serialisation on: it makes "I expected the chain
    /// to be here" a typed library outcome instead of a lost write. Composes with the idempotent append
    /// (Phase 82) — CAS + idempotency key in one retry loop.
    let appendIf
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (expectedHead: string)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, AppendRejection<'Rej>> =
        let actualHead = head records

        if actualHead <> expectedHead then
            Error(AppendRejection.StaleHead(expectedHead, actualHead))
        else
            match append hashFn w actor op state records with
            | Ok result -> Ok result
            | Error rej -> Error(AppendRejection.Domain rej)

    // ---- attributed-stream lift (Phase 81) ----

    /// "Who did what" for agent fleets, as a **derived lift** over the existing `StreamWitness` — no new
    /// witness field (GP2; the F8 metadata seam stays rejected). `liftWitness` turns any
    /// `StreamWitness<'Op,…>` into a `StreamWitness<Attributed<'Op>,…>`: `Apply` delegates to the inner
    /// reducer on `.Op` (attribution is provenance, never state), and `Encode`/`Decode` wrap the inner op
    /// codec in a camelCase attribution envelope. The envelope rides inside the chained op encoding, so
    /// the existing hash chain covers the attribution — provenance is tamper-evident for free
    /// (`verifyChain` unchanged). `byActor` / `bySession` are pure projection folds over an attributed
    /// stream. FSharp.Core-only + Fable-clean on encode AND decode (GP3): encode is hand-rolled canonical
    /// JSON, decode reuses the self-contained JSONL scanner.
    module Attributed =

        /// Encode an attribution envelope around the inner op's raw wire JSON:
        /// `{"actor":…,"session":…,"turn":<int>|null,"at":…,"op":<inner>}` (camelCase, the `Wire`
        /// kind-tag discipline). The inner op is embedded verbatim (whatever `encodeInner` produced), so
        /// it round-trips byte-for-byte and joins the chain hash exactly as a bare op does. `turn` is the
        /// one nullable slot — an absent optional renders as the bare `null` token, read back to `None`.
        let encodeEnvelope (encodeInner: 'Op -> string) (a: Attributed<'Op>) : string =
            "{\"actor\":"
            + jstr a.Actor
            + ",\"session\":"
            + jstr a.Session
            + ",\"turn\":"
            + (match a.Turn with
               | Some t -> string t
               | None -> "null")
            + ",\"at\":"
            + jstr a.At
            + ",\"op\":"
            + encodeInner a.Op
            + "}"

        /// Decode an attribution envelope, delegating the inner `op` raw span to `decodeInner`. Reuses
        /// the self-contained flat-object scanner (`Jsonl.topFields`) — FSharp.Core-only + Fable-clean —
        /// so an attributed host decodes / verifies / replays in-browser without a host boundary. A
        /// structural fault or an inner-decode `Error` is a named `Error`, never an exception (the
        /// recoverable-envelope discipline). An absent / `null` `turn` decodes to `None`.
        let decodeEnvelope
            (decodeInner: string -> Result<'Op, string>)
            (line: string)
            : Result<Attributed<'Op>, string> =
            try
                let fields = Jsonl.topFields line |> Map.ofList

                let get k =
                    match Map.tryFind k fields with
                    | Some v -> v
                    | None -> failwith ("missing field " + k)

                let turn =
                    match Map.tryFind "turn" fields with
                    | Some "null" -> None
                    | Some v -> Some(int v)
                    | None -> None

                decodeInner (get "op")
                |> Result.map (fun op ->
                    { Actor = Jsonl.unquote (get "actor")
                      Session = Jsonl.unquote (get "session")
                      Turn = turn
                      At = Jsonl.unquote (get "at")
                      Op = op })
            with ex ->
                Error ex.Message

        /// Lift a `StreamWitness<'Op,'State,'Rej>` to `StreamWitness<Attributed<'Op>,'State,'Rej>` — the
        /// derived attributed witness. `Apply` delegates to the inner `Apply` on `.Op`; `Encode`/`Decode`
        /// wrap the inner codec in the attribution envelope. No new witness field (GP2): the lift is a
        /// pure value over the existing three-seam witness, so an attributed stream appends / verifies /
        /// replays through the unchanged `OpStream` surface and the chain hash covers the attribution.
        let liftWitness (w: StreamWitness<'Op, 'State, 'Rej>) : StreamWitness<Attributed<'Op>, 'State, 'Rej> =
            { Apply = fun a state -> w.Apply a.Op state
              Encode = encodeEnvelope w.Encode
              Decode = decodeEnvelope w.Decode }

        /// Group an attributed stream's records by a projected key, preserving per-key append order
        /// (records for one key stay in stream order). The shared engine for `byActor` / `bySession`.
        let private groupBy
            (keyOf: Attributed<'Op> -> string)
            (records: OpRecord<Attributed<'Op>> list)
            : Map<string, OpRecord<Attributed<'Op>> list> =
            (Map.empty, records)
            ||> List.fold (fun acc r ->
                let k = keyOf r.Op
                Map.add k ((Map.tryFind k acc |> Option.defaultValue []) @ [ r ]) acc)

        /// Project an attributed stream to "who appended what" — records grouped by actor id, each group
        /// in stream order. A pure fold, no host dependency.
        let byActor (records: OpRecord<Attributed<'Op>> list) : Map<string, OpRecord<Attributed<'Op>> list> =
            groupBy _.Actor records

        /// Project an attributed stream by session id — each session's appended records in stream order.
        /// A pure fold, no host dependency.
        let bySession (records: OpRecord<Attributed<'Op>> list) : Map<string, OpRecord<Attributed<'Op>> list> =
            groupBy _.Session records

    // ---- idempotent append (Phase 82) ----

    /// The `EntryRef` of the record a fresh-key append just chained — the last record of the
    /// extended stream (an `append` success always appends exactly one).
    let private refOfAppended (records: OpRecord<'Op> list) : EntryRef =
        let r = List.last records
        { Seq = r.Seq; Hash = r.Hash }

    /// Idempotent append (Phase 82): chain `op` **only if** `key` has not already produced an entry
    /// in this stream — the at-least-once retry primitive. Agents retry: a session that times out
    /// mid-append re-sends its op under the same invocation key (the Phase 27
    /// `Function.invocationKey` shape), and this makes the re-send *converge* instead of
    /// double-applying. A fresh key appends **chain-identically to `append`** (same state, same
    /// records — the idempotency guard adds nothing to the chain, GP2) and returns the
    /// incrementally-updated `KeyIndex`; a seen key returns `AppendOutcome.Duplicate` naming the
    /// entry the key already produced (GP5), with the caller's stream and index untouched. A
    /// domain-reducer rejection is forwarded verbatim on the error channel, exactly as `append`
    /// forwards it (a rejected op indexes nothing — the key stays fresh for a corrected retry).
    /// Total — never throws (GP4).
    ///
    /// **The index is caller-threaded pure state.** Core holds no seen-key registry (GP6): the
    /// caller threads the `KeyIndex` alongside the stream (`KeyIndex.ofStream` rebuilds it from any
    /// stream; the returned index maintains it incrementally — the two agree). Key uniqueness scope
    /// is per-stream; storage and locking stay host-side (GP3/GP6). For the full agent retry loop —
    /// idempotency **and** lost-update protection — compose with the Phase 79 CAS via
    /// `appendIdempotentIf`.
    let appendIdempotent
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (key: string)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (index: KeyIndex)
        (records: OpRecord<'Op> list)
        : Result<AppendOutcome<'Op, 'State>, 'Rej> =
        match KeyIndex.tryFind key index with
        | Some existing -> Ok(AppendOutcome.Duplicate existing)
        | None ->
            append hashFn w actor op state records
            |> Result.map (fun (state', records') ->
                AppendOutcome.Appended(state', records', KeyIndex.add key (refOfAppended records') index))

    /// The combined idempotency-then-CAS call shape (Phase 82 ∘ Phase 79) — the full agent retry
    /// loop in one primitive. **The idempotency check runs first, deliberately**: when a retry's
    /// earlier attempt actually landed (the ack was lost, not the write), the head has advanced, so
    /// a bare `appendIf` would return `StaleHead` forever — checking the key first lets the retry
    /// converge on `AppendOutcome.Duplicate` regardless of head staleness. Only a *fresh* key
    /// reaches the CAS: a stale head is `AppendRejection.StaleHead` (re-read the stream, rebuild
    /// the index via `KeyIndex.ofStream` — the re-read picks up any own-earlier append — and
    /// retry); a matched head appends chain-identically to `append` and returns the updated index;
    /// a domain rejection is `AppendRejection.Domain`, forwarded as `appendIf` forwards it. Total
    /// (GP4); every non-success names its valid alternative (GP5). Value-level like `appendIf` —
    /// the host still owns the critical section that serialises read-check-append (GP3/GP6).
    let appendIdempotentIf
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (key: string)
        (expectedHead: string)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (index: KeyIndex)
        (records: OpRecord<'Op> list)
        : Result<AppendOutcome<'Op, 'State>, AppendRejection<'Rej>> =
        match KeyIndex.tryFind key index with
        | Some existing -> Ok(AppendOutcome.Duplicate existing)
        | None ->
            appendIf hashFn w expectedHead actor op state records
            |> Result.map (fun (state', records') ->
                AppendOutcome.Appended(state', records', KeyIndex.add key (refOfAppended records') index))
