namespace Fuaran.Core

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

/// WHICH integrity check a `ChainBreak` failed (Phase 125) — the closed set of reasons the chain
/// walkers can report, typed where the reason is MINTED rather than re-derived downstream by
/// string-matching this library's spellings.
///
/// **Three named cases for four spellings, deliberately.** `firstChainBreakWith` and
/// `firstCaptureBreak` spell the digest failure differently (`"tampered op/actor/seq"` vs
/// `"tampered capture"`) because they walk different records; both are the SAME check, and which
/// walker ran is the caller's own choice — it called one of them. A case that every consumer
/// immediately collapses is a worse contract than no case, so both map to `ChainBreakReason.HashMismatch`.
///
/// **`Unrecognised` is the honest arm, not a hedge**, even though only this library mints the named
/// cases. A `ChainBreak` also reaches a reader from outside these walkers — a host's own verifier, a
/// reason carried across a wire or a process boundary, a record a consumer constructs itself — and
/// the alternative to naming that case is a reader that claims to know which check failed when it
/// does not. `ChainBreakReason.ofString` is total and lands there; nothing in this module ever does
/// (`Conformance.chainBreakReasonLaws`).
[<RequireQualifiedAccess>]
type ChainBreakReason =
    /// The record's sequence is not the one the walk expected — a gap, a reordering, a truncation.
    | SequenceMismatch
    /// The record's `PrevHash` does not name its predecessor's `Hash`.
    | PrevHashLinkBroken
    /// The record's `Hash` does not recompute from its own fields — a tampered op, actor, sequence
    /// or captured value.
    | HashMismatch
    /// A reason that did not come from this module's walkers. Reported AS unknown: the chain is
    /// genuinely broken, and nothing here will claim to know which check failed.
    | Unrecognised of reason: string

/// Render / parse a `ChainBreakReason` as the wire-and-log string the walkers emitted before the
/// type existed, so a consumer that logged those bytes keeps logging them.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ChainBreakReason =

    /// The canonical string for a reason. `ChainBreakReason.HashMismatch` renders the OP-walk spelling for both
    /// walkers — one case, one rendering; the capture spelling is still accepted by `ofString`.
    let toString (r: ChainBreakReason) : string =
        match r with
        | ChainBreakReason.SequenceMismatch -> "sequence-number mismatch"
        | ChainBreakReason.PrevHashLinkBroken -> "prev-hash link broken"
        | ChainBreakReason.HashMismatch -> "hash mismatch (tampered op/actor/seq)"
        | ChainBreakReason.Unrecognised s -> s

    /// Total: every string the walkers ever emitted classifies, and anything else is `Unrecognised`
    /// verbatim rather than swept into the nearest-looking case. `toString >> ofString` is the
    /// identity on the named cases.
    let ofString (s: string) : ChainBreakReason =
        match s with
        | "sequence-number mismatch" -> ChainBreakReason.SequenceMismatch
        | "prev-hash link broken" -> ChainBreakReason.PrevHashLinkBroken
        | "hash mismatch (tampered op/actor/seq)"
        | "hash mismatch (tampered capture)" -> ChainBreakReason.HashMismatch
        | other -> ChainBreakReason.Unrecognised other

/// The first integrity fault found in a hash-chained stream (Phase 21) — the record `Index`, why
/// (sequence / prev-link / hash), and the expected vs got value. `verifyChain` is `firstChainBreak
/// … |> Option.isNone`; this names *where* a corrupt stream broke (for `fromJsonlVerified` / debug).
/// `Reason` is the closed `ChainBreakReason` as of `0.23.0` — it was a bare `string`, which every
/// consumer that wanted to branch on it had to re-type by matching this module's own spellings.
type ChainBreak =
    { Index: int
      Reason: ChainBreakReason
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
/// **Phase 287 changed the string spelling inside it**: every control character in the actor's
/// strings is `\u00xx` now, where `\n` / `\r` / `\t` were short escapes — a stream persisted
/// between the two, whose actors carry such a character, verifies under `legacyEscapeConfig` and
/// `rehash`es to canonical the same way; one whose actors carry none hashes identically under both.
type StreamConfig =
    { Payload: int -> Actor -> string -> string
      Genesis: string }

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

/// The bodies of the `OpStream` chain members (Phase 332): the chain hashes, the payload configs,
/// append, verification, rehash, replay, the chain head, compare-and-append and idempotent append.
/// Internal: a consumer reaches each one through its forward in `OpStream` (OpStream.fs), which
/// carries the member's contract and documentation.
module internal OpStreamChain =

    // A DELIBERATE COPY of `Hash.fnv1a` (`Fuaran.Core.Tree`), kept because `OpStream` is standalone
    // by design — it takes no `Tree` dependency (DECISIONS D2), and this is the one hash the layer
    // cannot do without. It must stay VALUE-IDENTICAL to the canonical one: `Hash.fnv1a` and this
    // are compared over a shared corpus by `ParityVectors.hashSweep` (Fuaran.Core.Conformance), so a copy that drifts is
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

    let defaultHash: HashFn = fun prev payload -> fnv1a (prev + "|" + payload)

    // ---- Phase 315: the named SHA-256 chain hash ----
    // A DELIBERATE COPY of `Hash.utf8Bytes` + `Hash.sha256HexOfBytes` (`Fuaran.Core.Tree`), for the
    // reason the FNV-1a copy above exists: this package takes no `Tree` dependency (DECISIONS D2),
    // and the chain hash a host swaps in for `defaultHash` has to be callable from it. It is held
    // VALUE-IDENTICAL rather than trusted — the `sha256Hash/*` rows of `ParityVectors` compare it
    // with `Hash.sha256Hex` over the whole hash-sweep corpus on both pipelines, so a copy that
    // drifts is a red row rather than two hosts disagreeing about a chain. The arithmetic notes
    // (`.+.`'s mask, the `uint32`-only bit length) are `Hash.fs`'s; read them there before
    // "simplifying" anything here.
    module private Sha256 =

        let private k: uint32[] =
            [| 0x428a2f98u
               0x71374491u
               0xb5c0fbcfu
               0xe9b5dba5u
               0x3956c25bu
               0x59f111f1u
               0x923f82a4u
               0xab1c5ed5u
               0xd807aa98u
               0x12835b01u
               0x243185beu
               0x550c7dc3u
               0x72be5d74u
               0x80deb1feu
               0x9bdc06a7u
               0xc19bf174u
               0xe49b69c1u
               0xefbe4786u
               0x0fc19dc6u
               0x240ca1ccu
               0x2de92c6fu
               0x4a7484aau
               0x5cb0a9dcu
               0x76f988dau
               0x983e5152u
               0xa831c66du
               0xb00327c8u
               0xbf597fc7u
               0xc6e00bf3u
               0xd5a79147u
               0x06ca6351u
               0x14292967u
               0x27b70a85u
               0x2e1b2138u
               0x4d2c6dfcu
               0x53380d13u
               0x650a7354u
               0x766a0abbu
               0x81c2c92eu
               0x92722c85u
               0xa2bfe8a1u
               0xa81a664bu
               0xc24b8b70u
               0xc76c51a3u
               0xd192e819u
               0xd6990624u
               0xf40e3585u
               0x106aa070u
               0x19a4c116u
               0x1e376c08u
               0x2748774cu
               0x34b0bcb5u
               0x391c0cb3u
               0x4ed8aa4au
               0x5b9cca4fu
               0x682e6ff3u
               0x748f82eeu
               0x78a5636fu
               0x84c87814u
               0x8cc70208u
               0x90befffau
               0xa4506cebu
               0xbef9a3f7u
               0xc67178f2u |]

        let private rotr (x: uint32) (n: int) : uint32 = (x >>> n) ||| (x <<< (32 - n))

        let inline private (.+.) (x: uint32) (y: uint32) : uint32 = (x + y) &&& 0xFFFFFFFFu

        /// `Hash.utf8Bytes`: UTF-8, a lone or ill-ordered surrogate written as `EF BF BD` — the
        /// platform's answer, so the digest is the one `SHA256.HashData(Encoding.UTF8.GetBytes s)`
        /// computes on .NET.
        let private utf8 (s: string) : ResizeArray<byte> =
            let out = ResizeArray<byte>()
            let mutable i = 0

            while i < s.Length do
                let c = int s[i]

                let pairs =
                    c >= 0xD800
                    && c <= 0xDBFF
                    && i + 1 < s.Length
                    && (let lo = int s[i + 1] in lo >= 0xDC00 && lo <= 0xDFFF)

                if c < 0x80 then
                    out.Add(byte c)
                elif c < 0x800 then
                    out.Add(byte (0xC0 ||| (c >>> 6)))
                    out.Add(byte (0x80 ||| (c &&& 0x3F)))
                elif pairs then
                    let lo = int s[i + 1]
                    let cp = 0x10000 + ((c - 0xD800) <<< 10) + (lo - 0xDC00)
                    out.Add(byte (0xF0 ||| (cp >>> 18)))
                    out.Add(byte (0x80 ||| ((cp >>> 12) &&& 0x3F)))
                    out.Add(byte (0x80 ||| ((cp >>> 6) &&& 0x3F)))
                    out.Add(byte (0x80 ||| (cp &&& 0x3F)))
                    i <- i + 1
                elif c >= 0xD800 && c <= 0xDFFF then
                    out.Add(byte 0xEF)
                    out.Add(byte 0xBF)
                    out.Add(byte 0xBD)
                else
                    out.Add(byte (0xE0 ||| (c >>> 12)))
                    out.Add(byte (0x80 ||| ((c >>> 6) &&& 0x3F)))
                    out.Add(byte (0x80 ||| (c &&& 0x3F)))

                i <- i + 1

            out

        let private hexChars = "0123456789abcdef"

        /// Lower-case hex SHA-256 of the UTF-8 bytes of `s` — `Hash.sha256Hex s`.
        let hex (s: string) : string =
            let data = utf8 s
            let byteLen = data.Count
            data.Add 0x80uy

            while data.Count % 64 <> 56 do
                data.Add 0uy

            let lo = uint32 byteLen <<< 3
            let hi = uint32 byteLen >>> 29

            for shift in [ 24; 16; 8; 0 ] do
                data.Add(byte ((hi >>> shift) &&& 0xFFu))

            for shift in [ 24; 16; 8; 0 ] do
                data.Add(byte ((lo >>> shift) &&& 0xFFu))

            let hs =
                [| 0x6a09e667u
                   0xbb67ae85u
                   0x3c6ef372u
                   0xa54ff53au
                   0x510e527fu
                   0x9b05688cu
                   0x1f83d9abu
                   0x5be0cd19u |]

            let w = Array.zeroCreate<uint32> 64

            for b in 0 .. data.Count / 64 - 1 do
                let off = b * 64

                for t in 0..15 do
                    w[t] <-
                        (uint32 data[off + t * 4] <<< 24)
                        ||| (uint32 data[off + t * 4 + 1] <<< 16)
                        ||| (uint32 data[off + t * 4 + 2] <<< 8)
                        ||| (uint32 data[off + t * 4 + 3])

                for t in 16..63 do
                    let s0 = (rotr w[t - 15] 7) ^^^ (rotr w[t - 15] 18) ^^^ (w[t - 15] >>> 3)
                    let s1 = (rotr w[t - 2] 17) ^^^ (rotr w[t - 2] 19) ^^^ (w[t - 2] >>> 10)
                    w[t] <- w[t - 16] .+. s0 .+. w[t - 7] .+. s1

                let mutable a = hs[0]
                let mutable bb = hs[1]
                let mutable c = hs[2]
                let mutable d = hs[3]
                let mutable e = hs[4]
                let mutable f = hs[5]
                let mutable g = hs[6]
                let mutable h = hs[7]

                for t in 0..63 do
                    let s1 = (rotr e 6) ^^^ (rotr e 11) ^^^ (rotr e 25)
                    let ch = (e &&& f) ^^^ ((~~~e) &&& g)
                    let temp1 = h .+. s1 .+. ch .+. k[t] .+. w[t]
                    let s0 = (rotr a 2) ^^^ (rotr a 13) ^^^ (rotr a 22)
                    let maj = (a &&& bb) ^^^ (a &&& c) ^^^ (bb &&& c)
                    let temp2 = s0 .+. maj
                    h <- g
                    g <- f
                    f <- e
                    e <- d .+. temp1
                    d <- c
                    c <- bb
                    bb <- a
                    a <- temp1 .+. temp2

                hs[0] <- hs[0] .+. a
                hs[1] <- hs[1] .+. bb
                hs[2] <- hs[2] .+. c
                hs[3] <- hs[3] .+. d
                hs[4] <- hs[4] .+. e
                hs[5] <- hs[5] .+. f
                hs[6] <- hs[6] .+. g
                hs[7] <- hs[7] .+. h

            let sb = System.Text.StringBuilder()

            for v in hs do
                for shift in [ 28; 24; 20; 16; 12; 8; 4; 0 ] do
                    sb.Append(hexChars[int ((v >>> shift) &&& 0xFu)]) |> ignore

            sb.ToString()

    let sha256Hash: HashFn = fun prev payload -> Sha256.hex (prev + "|" + payload)

    /// The canonical `{seq, actor, op}` payload the chain hash is computed over. Since Phase 320
    /// the `actor` is the typed `Actor` *object* (`Actor.encode`), so altering the attribution
    /// changes the hash — attribution is folded into the integrity chain. Since Phase 287 the
    /// actor's strings carry every control character as `\u00xx`, the spelling the UI host's DAG
    /// chain and the TypeScript twin already fold, so the linear hash of a record agrees with both.
    let private payloadOf (seq: int) (actor: Actor) (opJson: string) : string =
        "{\"seq\":"
        + string seq
        + ",\"actor\":"
        + Actor.encode actor
        + ",\"op\":"
        + opJson
        + "}"

    let canonicalConfig: StreamConfig = { Payload = payloadOf; Genesis = "" }

    /// The **pre-Phase-287** canonical payload — the same `{seq,actor,op}` envelope over the same
    /// typed `Actor` object, but with `\n`, `\r` and `\t` inside the actor's strings spelled as the
    /// short escapes `\n` / `\r` / `\t` rather than `\u000a` / `\u000d` / `\u0009`. The migration
    /// entry point for a stream persisted between Phase 320 and Phase 287: `verifyChainWith
    /// legacyEscapeConfig` to confirm it is intact, then `rehash legacyEscapeConfig canonicalConfig`
    /// to cut over — the Phase-255 shape, beside `legacyActorConfig`.
    ///
    /// A record whose actor holds no control character has the SAME payload under both configs, so
    /// for such a stream the rehash is a no-op that reproduces every hash — which is the case for
    /// every store this package's own tests and consumers have written. The rehash proves that
    /// rather than assuming it: `verifyChainWith canonicalConfig` over an unmigrated control-free
    /// store already passes.
    let private legacyEscapePayload (seq: int) (actor: Actor) (opJson: string) : string =
        "{\"seq\":"
        + string seq
        + ",\"actor\":"
        + Actor.encodeWith JsonString.quoteLegacy actor
        + ",\"op\":"
        + opJson
        + "}"

    let legacyEscapeConfig: StreamConfig =
        { Payload = legacyEscapePayload
          Genesis = "" }

    /// The **pre-Phase-320** canonical payload — it folded the actor as a *bare JSON string*
    /// (`"actor":"alice"`) rather than the typed object. The migration entry point for a stream
    /// persisted before the typed-actor change: read it with `fromJsonlLegacyActor` (which lifts
    /// each bare-string actor to `Human`), `verifyChainWith legacyActorConfig` to confirm it is
    /// intact, then `rehash legacyActorConfig canonicalConfig` to cut over — the standard
    /// Phase-255 migration shape. Pre-320 streams only ever held `Human` actors; an `Agent`
    /// reaching this payload is migration misuse, so it folds in just the id (best effort). The
    /// bare string keeps the pre-287 short escapes (`JsonString.quoteLegacy`): this payload
    /// reproduces the bytes a pre-320 writer produced, and that writer wrote `\n`.
    let private legacyActorPayload (seq: int) (actor: Actor) (opJson: string) : string =
        "{\"seq\":"
        + string seq
        + ",\"actor\":"
        + JsonString.quoteLegacy (Actor.id actor)
        + ",\"op\":"
        + opJson
        + "}"

    let legacyActorConfig: StreamConfig =
        { Payload = legacyActorPayload
          Genesis = "" }

    let empty: OpRecord<'Op> list = []

    /// The stream's length and last hash in ONE walk (Phase 296). `append` used to walk the list three
    /// times per call (`List.length`, `List.tryLast`, then the copy `@` makes) and `appendIf` four.
    let private tip (records: OpRecord<'Op> list) : int * string option =
        // A plain loop: a recursive walk carrying an option per record, or compiled to `.tail`
        // calls, cost more than the three library walks it replaced (measured, Phase 296).
        let mutable n = 0
        let mutable last = ""
        let mutable rest = records

        while not rest.IsEmpty do
            last <- rest.Head.Hash
            n <- n + 1
            rest <- rest.Tail

        if n = 0 then n, None else n, Some last

    let chainHashOf
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (seq: int)
        (actor: Actor)
        (encodedOp: string)
        (prev: string)
        : string =
        hashFn prev (cfg.Payload seq actor encodedOp)

    /// Chain `ops` onto a stream whose length is `seq0` and whose tip hash is `prev0`, applying each
    /// op in turn: the new records in order and the final state, or the first rejection with its index
    /// in `ops`. Nothing is copied; the caller splices the new records on once.
    let private chainOps
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (seq0: int)
        (prev0: string)
        (ops: 'Op list)
        (state: 'State)
        : Result<'State * OpRecord<'Op> list, int * 'Rej> =
        let rec go i (prev: string) st acc =
            function
            | [] -> Ok(st, List.rev acc)
            | op :: rest ->
                match w.Apply op st with
                | Error e -> Error(i, e)
                | Ok st' ->
                    let seq = seq0 + i
                    let h = chainHashOf cfg hashFn seq actor (w.Encode op) prev

                    let r =
                        { Seq = seq
                          Actor = actor
                          Op = op
                          PrevHash = prev
                          Hash = h }

                    go (i + 1) h st' (r :: acc) rest

        go 0 prev0 state [] ops

    let appendWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, 'Rej> =
        let n, last = tip records

        match chainOps cfg hashFn w actor n (defaultArg last cfg.Genesis) [ op ] state with
        | Ok(state', added) -> Ok(state', records @ added)
        | Error(_, e) -> Error e

    let append
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, 'Rej> =
        appendWith canonicalConfig hashFn w actor op state records

    let appendManyWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (ops: 'Op list)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, int * 'Rej> =
        let n, last = tip records

        chainOps cfg hashFn w actor n (defaultArg last cfg.Genesis) ops state
        |> Result.map (fun (state', added) -> state', records @ added)

    let appendMany
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (ops: 'Op list)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, int * 'Rej> =
        appendManyWith canonicalConfig hashFn w actor ops state records

    let appendChainOnly
        (hashFn: HashFn)
        (encode: 'Op -> string)
        (actor: Actor)
        (op: 'Op)
        (records: OpRecord<'Op> list)
        : OpRecord<'Op> list =
        let n, last = tip records
        let prev = defaultArg last canonicalConfig.Genesis

        records
        @ [ { Seq = n
              Actor = actor
              Op = op
              PrevHash = prev
              Hash = chainHashOf canonicalConfig hashFn n actor (encode op) prev } ]

    /// THE chain walker (Phase 296) — the one loop `firstChainBreakWith`, `rehash`, the snapshot
    /// boundary verifier and `firstCaptureBreak` share, where four copies were written. Walks `items`
    /// from sequence `seq0` and prev-link `genesis` and returns the first item whose sequence,
    /// prev-link, or hash fails, `Index` being its position in `items`. The digest is computed only
    /// after the cheap sequence and link checks pass.
    let walkChain
        (hashFn: HashFn)
        (genesis: string)
        (seq0: int)
        (seqOf: 'R -> int)
        (prevOf: 'R -> string)
        (hashOf: 'R -> string)
        (payloadOf: 'R -> string)
        (items: 'R list)
        : ChainBreak option =
        let rec go (prev: string) (i: int) =
            function
            | [] -> None
            | r :: rest ->
                let expectedSeq = seq0 + i

                if seqOf r <> expectedSeq then
                    Some
                        { Index = i
                          Reason = ChainBreakReason.SequenceMismatch
                          Expected = string expectedSeq
                          Got = string (seqOf r) }
                elif prevOf r <> prev then
                    Some
                        { Index = i
                          Reason = ChainBreakReason.PrevHashLinkBroken
                          Expected = prev
                          Got = prevOf r }
                else
                    let expectedHash = hashFn prev (payloadOf r)

                    if hashOf r <> expectedHash then
                        Some
                            { Index = i
                              Reason = ChainBreakReason.HashMismatch
                              Expected = expectedHash
                              Got = hashOf r }
                    else
                        go (hashOf r) (i + 1) rest

        go genesis 0 items

    /// The op-record instance of the walker: from `seq0` and `genesis`, each record's payload under
    /// `cfg`.
    let walkRecords
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (genesis: string)
        (seq0: int)
        (records: OpRecord<'Op> list)
        : ChainBreak option =
        walkChain
            hashFn
            genesis
            seq0
            (fun (r: OpRecord<'Op>) -> r.Seq)
            (fun r -> r.PrevHash)
            (fun r -> r.Hash)
            (fun r -> cfg.Payload r.Seq r.Actor (w.Encode r.Op))
            records

    let firstChainBreakWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : ChainBreak option =
        walkRecords cfg hashFn w cfg.Genesis 0 records

    let firstChainBreak
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : ChainBreak option =
        firstChainBreakWith canonicalConfig hashFn w records

    let verifyChainWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : bool =
        firstChainBreakWith cfg hashFn w records |> Option.isNone

    let verifyChain (hashFn: HashFn) (w: StreamWitness<'Op, 'State, 'Rej>) (records: OpRecord<'Op> list) : bool =
        verifyChainWith canonicalConfig hashFn w records

    let tryRehash
        (fromCfg: StreamConfig)
        (toCfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : Result<OpRecord<'Op> list, ChainBreak> =
        match firstChainBreakWith fromCfg hashFn w records with
        | Some b -> Error b
        | None ->
            let rec go (prev: string) acc =
                function
                | [] -> List.rev acc
                | (r: OpRecord<'Op>) :: rest ->
                    let payload = toCfg.Payload r.Seq r.Actor (w.Encode r.Op)
                    let h = hashFn prev payload

                    let r' = { r with PrevHash = prev; Hash = h }

                    go h (r' :: acc) rest

            Ok(go toCfg.Genesis [] records)

    let rehash
        (fromCfg: StreamConfig)
        (toCfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : Result<OpRecord<'Op> list, string> =
        tryRehash fromCfg toCfg hashFn w records
        |> Result.mapError (fun b ->
            sprintf
                "OpStream.rehash: source chain does not verify under fromCfg (record %d — %s)"
                b.Index
                (ChainBreakReason.toString b.Reason))

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

    // ---- the chain head (Phase 296; what an attestation signs, Phase 320) ----

    let headWith (cfg: StreamConfig) (records: OpRecord<'Op> list) : string =
        defaultArg (snd (tip records)) cfg.Genesis

    let head (records: OpRecord<'Op> list) : string = headWith canonicalConfig records

    // ---- compare-and-append / optimistic concurrency (Phase 79) ----

    /// The compare-and-append core: the extended stream AND the entry it chained, so the idempotent
    /// forms index the new record without walking the stream again.
    let private appendIfCore
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (expectedHead: string option)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list * EntryRef, AppendRejection<'Rej>> =
        let n, last = tip records
        let actualHead = defaultArg last cfg.Genesis

        match expectedHead with
        | Some expected when expected <> actualHead -> Error(AppendRejection.StaleHead(expected, actualHead))
        | _ ->
            match chainOps cfg hashFn w actor n actualHead [ op ] state with
            | Ok(state', ([ r ] as added)) -> Ok(state', records @ added, { Seq = r.Seq; Hash = r.Hash })
            | Ok(state', added) -> Ok(state', records @ added, { Seq = n; Hash = actualHead })
            | Error(_, rej) -> Error(AppendRejection.Domain rej)

    let appendIfWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (expectedHead: string)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, AppendRejection<'Rej>> =
        appendIfCore cfg hashFn w (Some expectedHead) actor op state records
        |> Result.map (fun (state', records', _) -> state', records')

    let appendIf
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (expectedHead: string)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, AppendRejection<'Rej>> =
        appendIfWith canonicalConfig hashFn w expectedHead actor op state records

    // ---- idempotent append (Phase 82) ----

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
            match appendIfCore canonicalConfig hashFn w None actor op state records with
            | Ok(state', records', entry) -> Ok(AppendOutcome.Appended(state', records', KeyIndex.add key entry index))
            | Error(AppendRejection.Domain rej) -> Error rej
            | Error(AppendRejection.StaleHead _) -> failwith "unreachable: no head was expected"

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
            appendIfCore canonicalConfig hashFn w (Some expectedHead) actor op state records
            |> Result.map (fun (state', records', entry) ->
                AppendOutcome.Appended(state', records', KeyIndex.add key entry index))
