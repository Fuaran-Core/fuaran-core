namespace Fuaran.Core

//  ---- The facade (Phase 332) ------------------------------------------------
//  This module is the op-stream's PUBLIC surface and is compiled LAST. Every
//  member is a forward with the member's full signature, attributes and
//  documentation; its body lives in the file that owns its concern, in an
//  internal module compiled ahead of this one:
//    - Chain.fs      `OpStreamChain`      the chain hashes, the payload configs, append,
//                                         verification, rehash, replay, the head,
//                                         compare-and-append and idempotent append
//    - Jsonl.fs      `OpStreamJsonl`      the one JSONL scanner and the record readers/writer
//    - Snapshot.fs   `OpStreamSnapshot`   the snapshot family and its pre-Phase-296 forwards
//    - Capture.fs    `OpStreamCapture`    determinism capture / replay and attestation
//    - Attributed.fs `OpStreamAttributed` the attributed-stream lift
//  So this file IS the contract a reader can read top to bottom, a change to one
//  concern edits one file, and the public-surface baseline
//  (api/Fuaran.Core.OpStream.txt) is byte-identical across the split.
// ============================================================================

/// Append-only hash-chained op stream + deterministic replay + JSONL persistence,
/// generic over the `StreamWitness`. The highest-genericity core layer.
module OpStream =

    // ---- the chain (bodies in Chain.fs) ----

    /// The default portable hash: FNV-1a over `prevHash | payload`. **Portable is meant literally** —
    /// value-identical on .NET and under Fable, so a browser replaying a chain a server wrote
    /// computes the same hashes. That was not true before `0.6.0`; see the copy note at `fnv1a`
    /// (Chain.fs).
    let defaultHash: HashFn = fun prev payload -> OpStreamChain.defaultHash prev payload

    /// The named SHA-256 chain hash (Phase 315): lower-case hex SHA-256 over the UTF-8 bytes of
    /// `prevHash + "|" + payload` — the same join `defaultHash` folds, under a cryptographic digest.
    /// The `HashFn` a host passes where a chain must resist a forged second pre-image, which FNV-1a
    /// cannot. It is byte-for-byte `fun prev payload -> Hash.sha256Hex (prev + "|" + payload)`, the
    /// function several consumers wrote by hand, so each copy is replaceable by this name without
    /// moving one persisted hash.
    ///
    /// **Over ill-formed text it inherits the platform's replacement**, as `Hash.sha256Hex` does: a
    /// lone surrogate in an actor or an op's encoding is hashed as U+FFFD, so two records differing
    /// only there share a digest. A `HashFn` is total and cannot refuse; a writer that must rule
    /// that out checks its strings before it appends (`Hash.trySha256Hex` names the unit).
    let sha256Hash: HashFn = fun prev payload -> OpStreamChain.sha256Hash prev payload

    /// The canonical payload binding — the `{seq,actor,op}` envelope + `""` genesis. The default
    /// for every `append` / `verifyChain` call.
    let canonicalConfig: StreamConfig = OpStreamChain.canonicalConfig

    /// The pre-Phase-287 canonical config (typed actor with short control escapes + `""` genesis).
    /// The `fromCfg` for a string-escaping migration `rehash`. See `legacyEscapePayload`.
    let legacyEscapeConfig: StreamConfig = OpStreamChain.legacyEscapeConfig

    /// The pre-Phase-320 canonical config (bare-string actor + `""` genesis). The `fromCfg` for a
    /// typed-actor migration `rehash`. See `legacyActorPayload`.
    let legacyActorConfig: StreamConfig = OpStreamChain.legacyActorConfig

    /// The stream with no records. Its `head` is the config's genesis, and the first `append` onto it
    /// chains a record at `Seq` 0.
    let empty: OpRecord<'Op> list = OpStreamChain.empty

    /// THE hash of one chained record (Phase 315): `hashFn prev (cfg.Payload seq actor encodedOp)`,
    /// the value every `append` stores as `Hash` and every verifier recomputes. Public for an adapter
    /// that keeps its own record type, or appends without the domain state (`appendChainOnly`), and
    /// used to rebuild the payload by hand to get it — a copy that silently stops verifying the day
    /// the payload binding moves. `encodedOp` is the witness's `Encode` of the op; `prev` is the
    /// predecessor's `Hash`, or `cfg.Genesis` for the first record.
    let chainHashOf
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (seq: int)
        (actor: Actor)
        (encodedOp: string)
        (prev: string)
        : string =
        OpStreamChain.chainHashOf cfg hashFn seq actor encodedOp prev

    /// `append` under an explicit `StreamConfig` (Phase 255) — the chain payload + genesis come
    /// from `cfg` rather than the canonical binding. Used during a format migration to extend a
    /// stream in its own legacy chain format; ordinary callers use `append`.
    ///
    /// **Cost (Phase 296).** One walk of `records` and one copy — the list's end is where a record
    /// goes, and an immutable list reaches its end only by walking it, so a single `append` is linear
    /// in the stream and a loop of them is quadratic. A caller chaining several ops chains them with
    /// `appendManyWith`, which walks and copies ONCE for the batch.
    let appendWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, 'Rej> =
        OpStreamChain.appendWith cfg hashFn w actor op state records

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
        OpStreamChain.append hashFn w actor op state records

    /// Chain several ops by one actor under an explicit `StreamConfig` in ONE walk of the stream
    /// (Phase 296): the same records, byte for byte, as folding `appendWith` over `ops`, at the cost of
    /// one `append` rather than `List.length ops` of them. All or nothing: the first op the domain
    /// rejects is `Error(index in ops, rejection)` and nothing is chained.
    let appendManyWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (ops: 'Op list)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, int * 'Rej> =
        OpStreamChain.appendManyWith cfg hashFn w actor ops state records

    /// `appendManyWith` under the canonical config (Phase 296) — the batch form of `append`.
    let appendMany
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (ops: 'Op list)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, int * 'Rej> =
        OpStreamChain.appendMany hashFn w actor ops state records

    /// Chain one op onto the stream WITHOUT applying it (Phase 315) — no state in, no state out, no
    /// domain rejection. For an adapter whose apply runs somewhere else (at `replay`, or in its own
    /// reducer before it calls here), which used to recompute the canonical payload and hash by hand
    /// to get the record `append` would have written. The record is exactly that one — `chainHashOf`
    /// under `canonicalConfig`, so a stream built with it `verifyChain`s under a witness with the same
    /// `Encode` — and it carries nothing `append` would have checked: an op the domain would refuse is
    /// chained all the same, and `replay` is where it is refused. `encode` is the witness's `Encode`.
    /// One walk and one copy, as `append`.
    let appendChainOnly
        (hashFn: HashFn)
        (encode: 'Op -> string)
        (actor: Actor)
        (op: 'Op)
        (records: OpRecord<'Op> list)
        : OpRecord<'Op> list =
        OpStreamChain.appendChainOnly hashFn encode actor op records

    /// `firstChainBreak` under an explicit `StreamConfig` (Phase 21 + Phase 255) — the localising
    /// verifier. Walks the chain and returns the first record whose sequence, prev-link, or hash
    /// fails under `cfg`; `None` for an intact chain.
    let firstChainBreakWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : ChainBreak option =
        OpStreamChain.firstChainBreakWith cfg hashFn w records

    /// The first integrity fault in a canonical-config chain (Phase 21), or `None` if intact.
    let firstChainBreak
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : ChainBreak option =
        OpStreamChain.firstChainBreak hashFn w records

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
        OpStreamChain.verifyChainWith cfg hashFn w records

    /// Recompute the chain from the records and confirm every link. Detects reordering,
    /// tampering with an op, and a broken prev-link.
    let verifyChain (hashFn: HashFn) (w: StreamWitness<'Op, 'State, 'Rej>) (records: OpRecord<'Op> list) : bool =
        OpStreamChain.verifyChain hashFn w records

    /// Migrate a chain from one payload format to another (Phase 255), keeping the typed break
    /// (Phase 296). Verifies the source records under `fromCfg` first — a chain that does not verify
    /// under its declared legacy format is a migration the caller must not silently re-bless, so it is
    /// `Error` carrying the first `ChainBreak` — then re-derives every `PrevHash` / `Hash` under `toCfg`
    /// (the ops / actors / seqs are the source of truth; only the hash chain changes). The result
    /// `verifyChain`s under `toCfg`.
    let tryRehash
        (fromCfg: StreamConfig)
        (toCfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : Result<OpRecord<'Op> list, ChainBreak> =
        OpStreamChain.tryRehash fromCfg toCfg hashFn w records

    /// `tryRehash` with the break rendered (Phase 255) — the string form, kept; the message now names
    /// the record and the reason the source chain failed at.
    let rehash
        (fromCfg: StreamConfig)
        (toCfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : Result<OpRecord<'Op> list, string> =
        OpStreamChain.rehash fromCfg toCfg hashFn w records

    /// Re-apply every op over a base state. Replay is a fold of `Apply` — op-stream
    /// replay is a special case of re-derivation.
    let replay
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State, int * 'Rej> =
        OpStreamChain.replay w state0 records

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
        OpStreamChain.replayLenient w state0 records

    // ---- JSONL persistence and the one scanner (bodies in Jsonl.fs) ----

    /// One JSON object per line. The op payload is embedded as raw JSON (the domain's
    /// own `Encode` output), so a round-trip preserves it byte-for-byte.
    let toJsonl (w: StreamWitness<'Op, 'State, 'Rej>) (records: OpRecord<'Op> list) : string =
        OpStreamJsonl.toJsonl w records

    /// `toJsonl` that refuses what the reader cannot read back (Phase 301). Every op's encoding is
    /// checked before a line is built (`Jsonl.checkRaw`): one carrying a line break, whitespace either
    /// side of the value, or anything but one JSON value would read back changed — split across lines,
    /// or trimmed — and the read-back chain would fail `verifyChain`. The first such record is the
    /// `Error`, by its 1-based line and member (`op`). On `Ok` the text is `toJsonl`'s, byte for byte,
    /// and `fromJsonl` reads it back to records that `toJsonl` writes identically.
    let tryToJsonl
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : Result<string, JsonlWriteFault> =
        OpStreamJsonl.tryToJsonl w records

    /// THE JSONL line scanner (Phase 296) — the one scanner in the repository. Every reader in this
    /// package (`fromJsonl`, `captureFromJsonl`, `snapshotFromJsonlResult`,
    /// `Attributed.decodeEnvelope`) and the DAG package's `Dag.fromJsonl` read through it; until
    /// Phase 296 the DAG carried a verbatim copy, and every scanner fix was applied twice.
    ///
    /// It splits one flat top-level object into its members, keeping each value's RAW span
    /// byte-for-byte (so an `op` handed to a witness decoder is exactly what `Encode` produced) —
    /// which `Wire.Json.parse`, yielding a lossy `JVal`, cannot. It stays FSharp.Core-only and
    /// Fable-clean: `OpStream` takes no `Wire` dependency (DECISIONS.md D2), and sharing this module
    /// with the DAG package adds none either, because that package already references this one.
    ///
    /// **It refuses what `Wire.Json.parse` refuses** at the levels it reads: the object's own
    /// structure (a non-object line, a truncated line, a missing `:` / `,` / `}`, trailing content),
    /// every string token's escapes (an unknown escape letter, a non-hex `\u` digit, an unpaired
    /// surrogate), every bare value's literal (`true` / `false` / `null` / a JSON number and nothing
    /// else), and — through the typed accessors — a member whose KIND is wrong for its reader (an
    /// unquoted value where a string is required, a non-integer where an integer is). The interior
    /// of an array or object value is balanced and its strings checked, but its grammar beyond that
    /// is the grammar of whoever decodes the raw span. Every refusal is a typed `JsonlFault` naming
    /// the 1-based line and the scanner's own position; nothing here throws past this module.
    module Jsonl =

        /// Scan one line into a `JsonlLine` carrying its 1-based `number`.
        let parseLine (number: int) (text: string) : Result<JsonlLine, JsonlFault> =
            OpStreamJsonl.Jsonl.parseLine number text

        /// The members of one flat JSON object, each value as its raw span byte-for-byte (the
        /// opaque canonical payload a consumer embeds keeps its bytes), first-wins on a repeated key.
        /// A refusal is numbered line 1.
        let topFields (line: string) : Result<(string * string) list, JsonlFault> = OpStreamJsonl.Jsonl.topFields line

        /// The raw span of ONE top-level member of a flat JSON object — `None` when the object has no
        /// such member — after the whole line has been scanned (a malformed line is refused even when
        /// the member itself is intact).
        let rawSpan (field: string) (line: string) : Result<string option, JsonlFault> =
            OpStreamJsonl.Jsonl.rawSpan field line

        /// Unescape a string token (surrounding quotes included). Total: a raw span that is not a
        /// well-formed JSON string is an `Error`, never a truncated read — `null` is not the string
        /// `"ul"`.
        let unquote (raw: string) : Result<string, JsonlFaultReason> = OpStreamJsonl.Jsonl.unquote raw

        /// The line's 1-based number.
        let lineNumber (line: JsonlLine) : int = OpStreamJsonl.Jsonl.lineNumber line

        /// The line's text, verbatim.
        let lineText (line: JsonlLine) : string = OpStreamJsonl.Jsonl.lineText line

        /// A reader's own refusal of a well-formed line — `JsonlFaultReason.Refused` at the line's
        /// start.
        let refuse (line: JsonlLine) (reason: string) : JsonlFault = OpStreamJsonl.Jsonl.refuse line reason

        /// The raw span of a member, or `None` when the line has none.
        let tryRawField (key: string) (line: JsonlLine) : string option =
            OpStreamJsonl.Jsonl.tryRawField key line

        /// The raw span of a required member.
        let rawField (key: string) (line: JsonlLine) : Result<string, JsonlFault> =
            OpStreamJsonl.Jsonl.rawField key line

        /// A required member that must be a JSON string, unescaped.
        let stringField (key: string) (line: JsonlLine) : Result<string, JsonlFault> =
            OpStreamJsonl.Jsonl.stringField key line

        /// A required member that must be an integer under the JSON grammar
        /// (`-?(0|[1-9][0-9]*)`, within the 32-bit range) — not a string, a fraction, an exponent or
        /// a hex spelling.
        let intField (key: string) (line: JsonlLine) : Result<int, JsonlFault> = OpStreamJsonl.Jsonl.intField key line

        /// A required member that must be an array of JSON strings (`[]` included), unescaped.
        let stringsField (key: string) (line: JsonlLine) : Result<string list, JsonlFault> =
            OpStreamJsonl.Jsonl.stringsField key line

        /// A required member holding the typed `Actor` object (Phase 320) —
        /// `{"kind":"human","id":…}` or `{"kind":"agent","model":…,"version":…,"id":…}`, every member
        /// present and a string. An absent or unknown `kind` is a refusal, never `Human`: a store
        /// written by a newer build may carry a kind this reader does not know, and reading it as a
        /// person would attribute the op to the wrong kind of actor (Phase 260).
        let actorField (key: string) (line: JsonlLine) : Result<Actor, JsonlFault> =
            OpStreamJsonl.Jsonl.actorField key line

        /// Scan a JSONL text line by line — the one record loop every reader shares. Lines are
        /// numbered from 1 over ALL lines (blank lines counted, then skipped), `\r\n` read as `\n`;
        /// each non-blank line is scanned and handed to `decode`, and the first refusal — the
        /// scanner's or the decoder's — is the result.
        let scanRecords (decode: JsonlLine -> Result<'T, JsonlFault>) (text: string) : Result<'T list, JsonlFault> =
            OpStreamJsonl.Jsonl.scanRecords decode text

        /// Will `raw`, embedded verbatim as a member's value, read back as exactly `raw` (Phase 301)?
        /// It must carry no line break (`\n` / `\r`), begin with no whitespace, and be ONE JSON value
        /// this scanner reads to its last character — so the member's span is `raw` byte for byte. A
        /// line or paragraph separator (U+2028 / U+2029) inside a string passes: the canonical escaper
        /// emits it raw, and this reader splits lines on `\n` alone. The check every `try…ToJsonl`
        /// writer applies, public so a writer outside this package (the DAG's) applies the same one.
        let checkRaw (raw: string) : Result<unit, JsonlWriteFaultReason> = OpStreamJsonl.Jsonl.checkRaw raw

    /// Parse JSONL into `(records, rawSnapshotLines)` (Phase 16) — the snapshot-aware reader. The
    /// records are decoded by the witness; the snapshot line, when there is one, is returned verbatim
    /// (its `state` member still embedded raw) so a caller can recover the base state with
    /// `snapshotFromJsonl` / `snapshotFromJsonlResult` and resume via `replayFrom`. A non-empty
    /// snapshot list means the file was compacted — replaying the records from origin would be wrong.
    /// Fully portable (Phase 241).
    ///
    /// **Refusals (Phase 296).** A malformed line, a witness decode `Error`, a member of the wrong
    /// kind (`"prevHash":null`, `"seq":0x2`), or a snapshot line anywhere but the first line is an
    /// `Error` rendering the typed `JsonlFault` — `line N: <reason> (position P)`, `N` 1-based over
    /// every line of the text. At most one snapshot line is returned. The `op` raw span is preserved
    /// byte-for-byte, so a round-trip is identical. Since Phase 320 the `actor` member is the typed
    /// object; use `fromJsonlLegacyActor` for a pre-320 file.
    let fromJsonlWithSnapshots
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list * string list, string> =
        OpStreamJsonl.fromJsonlWithSnapshots w text

    /// Parse JSONL back into records, **dropping the snapshot line** (Phase 244) — correct for a
    /// linear, never-compacted stream. A thin wrapper over `fromJsonlWithSnapshots` (the one scanner);
    /// a `compact` output (snapshot + tail) read this way loses its base state with no signal and
    /// replaying from origin is then wrong, so read a possibly-compacted file with
    /// `fromJsonlWithSnapshots` instead. Refuses exactly what that reader refuses; the `op` raw span
    /// round-trips.
    let fromJsonl (w: StreamWitness<'Op, 'State, 'Rej>) (text: string) : Result<OpRecord<'Op> list, string> =
        OpStreamJsonl.fromJsonl w text

    /// Read a **pre-Phase-320** JSONL file (Phase 320 migration) — the `actor` member is still a bare
    /// JSON string, which this lifts to the typed `Human` case. The returned records carry the file's
    /// stored `PrevHash` / `Hash` (computed under the old bare-string payload), so they
    /// `verifyChainWith legacyActorConfig` and then `rehash legacyActorConfig canonicalConfig` to the
    /// new typed form. The snapshot line is dropped. Refuses what `fromJsonl` refuses.
    let fromJsonlLegacyActor (w: StreamWitness<'Op, 'State, 'Rej>) (text: string) : Result<OpRecord<'Op> list, string> =
        OpStreamJsonl.fromJsonlLegacyActor w text

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
        OpStreamJsonl.fromJsonlVerified hashFn w text

    // ---- snapshot / compaction (Phase 244; one family since Phase 296) (bodies in Snapshot.fs) ----

    /// THE snapshot family (Phase 296) — one set of entry points taking the `SnapshotMode` and the
    /// `StreamConfig`, where seventeen members formed a strict/chain-only × canonical/config matrix.
    /// The mode is carried ON the `Snapshot`, so everything after `take` reads it from the snapshot
    /// rather than from an argument or a re-parse of the line.
    ///
    /// **The state encoder.** It is the `'State`'s JSON, which a snapshot line always stores; under
    /// `SnapshotMode.Strict` it is ALSO the hash pre-image of the state, so it must be canonical and
    /// byte-stable across hosts. Under `SnapshotMode.ChainOnly` it never enters a hash — `take`,
    /// `firstBreak` and `verify` do not call it — so a domain without a canonical encoder passes the
    /// storage serialiser it has.
    ///
    /// Phase 288's checkpoint builds on this family: one pre-image per mode (`payload` in Snapshot.fs), one
    /// verifier, one line format.
    module Snapshots =

        /// Capture a snapshot at boundary `atSeq` — the state after `records[0 .. atSeq-1]` from
        /// `state0` — hashed by `mode`. The boundary hash at sequence zero is `cfg.Genesis`, the seed
        /// every chain walker starts from (Phase 227: `compact_at_zero_verifies_under_any_genesis`,
        /// `proofs/Chain.fst`); past zero it is the stored `records[atSeq-1].Hash`, read and TRUSTED —
        /// verify the stream before snapshotting it. Only `cfg.Genesis` is read: the snapshot's own
        /// pre-image is the checkpoint format, not the per-op chain format.
        let take
            (mode: SnapshotMode)
            (cfg: StreamConfig)
            (hashFn: HashFn)
            (stateEncode: 'State -> string)
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (state0: 'State)
            (records: OpRecord<'Op> list)
            (atSeq: int)
            : Result<Snapshot<'State>, SnapshotFault<'Rej>> =
            OpStreamSnapshot.Snapshots.take mode cfg hashFn stateEncode w state0 records atSeq

        /// Compact a stream at `atSeq` into `(snapshot, tail)` that replays identically to the full
        /// stream from `state0` — the prefix is discarded, the chain stays verifiable.
        ///
        /// **Verify, then compact (Phase 227).** `compact` does not walk the chain: it reads
        /// `records[atSeq-1].Hash` and TRUSTS it. So the compacted stream verifies exactly when the
        /// original does only if the discarded prefix verified first — `compact_preserves_verify` /
        /// `compact_verifies_iff_original` (`proofs/Chain.fst`). A tamper in the prefix of an
        /// UNVERIFIED stream survives compaction, verifies across the boundary, and once the prefix is
        /// discarded nothing can find it again. Run `verifyChainWith cfg` over the stream first.
        let compact
            (mode: SnapshotMode)
            (cfg: StreamConfig)
            (hashFn: HashFn)
            (stateEncode: 'State -> string)
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (state0: 'State)
            (records: OpRecord<'Op> list)
            (atSeq: int)
            : Result<Snapshot<'State> * OpRecord<'Op> list, SnapshotFault<'Rej>> =
            OpStreamSnapshot.Snapshots.compact mode cfg hashFn stateEncode w state0 records atSeq

        /// The first place a snapshot boundary fails to verify, under the snapshot's OWN mode — its
        /// hash (and, `Strict`, the state it folds in), then the tail's chain from the snapshot's
        /// `PrevHash` and `Seq` under `cfg.Payload` through the one chain walker. `None` for an intact
        /// boundary. The localising form of `verify`, public since Phase 296.
        let firstBreak
            (cfg: StreamConfig)
            (hashFn: HashFn)
            (stateEncode: 'State -> string)
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (snap: Snapshot<'State>)
            (tail: OpRecord<'Op> list)
            : SnapshotBreak option =
            OpStreamSnapshot.Snapshots.firstBreak cfg hashFn stateEncode w snap tail

        /// Verify the chain across the truncation boundary under the snapshot's own mode — `firstBreak
        /// … |> Option.isNone`. A `ChainOnly` snapshot does NOT detect a swapped `'State` (its
        /// trade-off); a `Strict` one does.
        let verify
            (cfg: StreamConfig)
            (hashFn: HashFn)
            (stateEncode: 'State -> string)
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (snap: Snapshot<'State>)
            (tail: OpRecord<'Op> list)
            : bool =
            OpStreamSnapshot.Snapshots.verify cfg hashFn stateEncode w snap tail

        /// Bounded replay from a snapshot (Phase 296 checks the seam): the tail must start at the
        /// snapshot's boundary — its first record's `Seq` equal to the snapshot's `Seq` — or the
        /// replay would fold ops from the wrong position onto the checkpoint; then `Apply` is folded
        /// over the tail from `snap.State`. The mode does not reach replay.
        let replayFrom
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (snap: Snapshot<'State>)
            (tail: OpRecord<'Op> list)
            : Result<'State, SnapshotFault<'Rej>> =
            OpStreamSnapshot.Snapshots.replayFrom w snap tail

        /// One snapshot line, by the snapshot's mode: a `Strict` line carries no `stateHashed` member
        /// (byte-identical to the pre-Phase-258 format); a `ChainOnly` line carries
        /// `"stateHashed":false`. The `state` member is `stateEncode`'s output embedded raw.
        let toJsonl (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
            OpStreamSnapshot.Snapshots.toJsonl stateEncode snap

        /// `toJsonl` that refuses a state encoding the reader cannot read back (Phase 301) — the
        /// `Jsonl.checkRaw` check over `stateEncode snap.State`, refused as line 1, member `state`. On
        /// `Ok` the line is `toJsonl`'s, byte for byte.
        let tryToJsonl (stateEncode: 'State -> string) (snap: Snapshot<'State>) : Result<string, JsonlWriteFault> =
            OpStreamSnapshot.Snapshots.tryToJsonl stateEncode snap

        /// Parse a snapshot line through the one scanner, the mode read from the line: `ChainOnly`
        /// when it carries `"stateHashed":false`, `Strict` when the member is absent or `true`
        /// (every pre-Phase-258 line is strict), a refusal for any other value. The `state` span is
        /// handed to `stateDecode`, whose `Error` is threaded through.
        let ofJsonl (stateDecode: string -> Result<'State, string>) (line: string) : Result<Snapshot<'State>, string> =
            OpStreamSnapshot.Snapshots.ofJsonl stateDecode line

    // ---- the compacted stream (Phase 301) (bodies in Compacted.fs) ----

    /// A compacted stream that keeps going (Phase 301): append, compare-and-append, idempotent append,
    /// re-compaction, the head and the key index, each reading the snapshot's BOUNDARY rather than
    /// the tail's length — the next record's sequence is `Snapshot.Seq` plus the tail's length, the
    /// head of an empty tail is `Snapshot.PrevHash`, and the key index is `Keys` plus the tail's. The
    /// plain `append` / `head` / `KeyIndex.ofStream` are unchanged and remain the forms for a stream
    /// that starts at its genesis; handed a tail they number it from zero and link it to nothing.
    ///
    /// **The algebra** (`proofs/Chain.fst` §7, Phase 301): `appendTo` onto `compactAt rs n` mints
    /// exactly the record `append` mints onto `rs`, and the original-plus-record verifies exactly when
    /// its discarded prefix does and the compacted-plus-record verifies across
    /// (`append_after_compact`); `compactFrom (compactAt rs n) m = compactAt rs m` for every
    /// `n <= m` in range (`compact_compose`); and `keyIndex keyOf (compactAt … keyOf rs n)` is
    /// `KeyIndex.ofStream` over the whole stream (`key_index_compact_parity`).
    module Compacted =

        /// Compact `records` at `atSeq` (`Snapshots.compact`, same refusals) into a compacted stream
        /// whose `Keys` is the index `keyOf` builds over the discarded prefix — first-wins, an op
        /// `keyOf` answers `None` for carries no key. A stream that keys nothing passes
        /// `fun _ -> None`. Verify, then compact: the prefix is folded and discarded unchecked.
        let compactAt
            (mode: SnapshotMode)
            (cfg: StreamConfig)
            (hashFn: HashFn)
            (stateEncode: 'State -> string)
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (keyOf: 'Op -> string option)
            (state0: 'State)
            (records: OpRecord<'Op> list)
            (atSeq: int)
            : Result<Compacted<'Op, 'State>, SnapshotFault<'Rej>> =
            OpStreamCompacted.compactAt mode cfg hashFn stateEncode w keyOf state0 records atSeq

        /// Compact AGAIN at `atSeq`, counted in the ORIGIN's numbering (`Snapshot.Seq` up to
        /// `Snapshot.Seq` plus the tail's length) — the snapshot sealed in the stream's own mode, the
        /// cut tail records indexed onto `Keys`. Exactly the compacted stream `compactAt` takes of the
        /// full history at `atSeq` (`compact_compose`), refusals included: `SeqOutOfRange` names
        /// `atSeq` and the origin length, and `PrefixRejected` the ORIGIN index of the op the domain
        /// refused. A tail that does not start at the boundary is `TailSeqMismatch`.
        let compactFrom
            (hashFn: HashFn)
            (stateEncode: 'State -> string)
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (keyOf: 'Op -> string option)
            (c: Compacted<'Op, 'State>)
            (atSeq: int)
            : Result<Compacted<'Op, 'State>, SnapshotFault<'Rej>> =
            OpStreamCompacted.compactFrom hashFn stateEncode w keyOf c atSeq

        /// The head — the tail's last hash, or the snapshot's `PrevHash` (the boundary record's hash,
        /// the configured genesis at sequence zero) on an empty tail. What `appendIfTo` compares and
        /// what an attestation of a compacted stream signs: the same head the full stream has.
        let head (c: Compacted<'Op, 'State>) : string = OpStreamCompacted.head c

        /// Append onto a compacted stream: the record at sequence `Snapshot.Seq` plus the tail's
        /// length, linked to `head c`, hashed under `cfg.Payload`. A domain rejection is the `Error`.
        let appendTo
            (cfg: StreamConfig)
            (hashFn: HashFn)
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (actor: Actor)
            (op: 'Op)
            (state: 'State)
            (c: Compacted<'Op, 'State>)
            : Result<'State * Compacted<'Op, 'State>, 'Rej> =
            OpStreamCompacted.appendTo cfg hashFn w actor op state c

        /// Compare-and-append onto a compacted stream (`appendIf`'s contract): `StaleHead` when
        /// `expectedHead` is not `head c`, nothing written; otherwise `appendTo`, a domain rejection
        /// as `Domain`.
        let appendIfTo
            (cfg: StreamConfig)
            (hashFn: HashFn)
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (expectedHead: string)
            (actor: Actor)
            (op: 'Op)
            (state: 'State)
            (c: Compacted<'Op, 'State>)
            : Result<'State * Compacted<'Op, 'State>, AppendRejection<'Rej>> =
            OpStreamCompacted.appendIfTo cfg hashFn w expectedHead actor op state c

        /// Idempotent append onto a compacted stream (`appendIdempotent`'s contract): a key `index`
        /// already holds is `Duplicate` naming the entry it produced — before or after the boundary,
        /// so a retry of a pre-compaction key converges — and nothing is written; otherwise `appendTo`
        /// and the index extended. Thread `keyIndex keyOf c` as the first `index`.
        let appendIdempotentTo
            (cfg: StreamConfig)
            (hashFn: HashFn)
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (key: string)
            (actor: Actor)
            (op: 'Op)
            (state: 'State)
            (index: KeyIndex)
            (c: Compacted<'Op, 'State>)
            : Result<CompactedOutcome<'Op, 'State>, 'Rej> =
            OpStreamCompacted.appendIdempotentTo cfg hashFn w key actor op state index c

        /// The key index of the whole history: `Keys` (the discarded prefix's) with the tail's keys
        /// folded on, first-wins — `KeyIndex.ofStream` of the uncompacted stream, rebuilt without it.
        let keyIndex (keyOf: 'Op -> string option) (c: Compacted<'Op, 'State>) : KeyIndex =
            OpStreamCompacted.keyIndex keyOf c

        /// `Snapshots.verify` over the snapshot and the tail, under the snapshot's own mode.
        let verify
            (cfg: StreamConfig)
            (hashFn: HashFn)
            (stateEncode: 'State -> string)
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (c: Compacted<'Op, 'State>)
            : bool =
            OpStreamCompacted.verify cfg hashFn stateEncode w c

        /// `Snapshots.replayFrom` over the snapshot and the tail — the seam checked.
        let replayFrom
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (c: Compacted<'Op, 'State>)
            : Result<'State, SnapshotFault<'Rej>> =
            OpStreamCompacted.replayFrom w c

    // ---- the pre-Phase-296 snapshot matrix: forwards over `Snapshots`, kept for the 0.33.0 draft ----
    //
    // Each forward answers exactly as it did: it pins the mode its name says (so a strict verifier
    // handed a chain-only snapshot still refuses it) and renders the typed fault as the string it
    // returned. Removed after the draft.

    [<Literal>]
    let private SnapshotForward =
        "a pre-Phase-296 snapshot entry point; use OpStream.Snapshots (the mode and the config as arguments, the mode carried on the snapshot). Removed after the 0.33.0 draft."

    /// `Snapshots.take` with the mode chosen by an optional encoder (`Some` strict, `None`
    /// chain-only) and the fault rendered. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotAtOptWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (stateEncode: ('State -> string) option)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State>, string> =
        OpStreamSnapshot.snapshotAtOptWith cfg hashFn stateEncode w state0 records atSeq

    /// `snapshotAtOptWith` under the canonical config. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotAtOpt
        (hashFn: HashFn)
        (stateEncode: ('State -> string) option)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State>, string> =
        OpStreamSnapshot.snapshotAtOpt hashFn stateEncode w state0 records atSeq

    /// A strict snapshot under the canonical config. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotAt
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State>, string> =
        OpStreamSnapshot.snapshotAt hashFn stateEncode w state0 records atSeq

    /// A chain-only snapshot under the canonical config. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotAtChainOnly
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State>, string> =
        OpStreamSnapshot.snapshotAtChainOnly hashFn w state0 records atSeq

    /// A strict compaction under `cfg`. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let compactWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
        OpStreamSnapshot.compactWith cfg hashFn stateEncode w state0 records atSeq

    /// A chain-only compaction under `cfg`. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let compactChainOnlyWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
        OpStreamSnapshot.compactChainOnlyWith cfg hashFn w state0 records atSeq

    /// A strict compaction under the canonical config. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let compact
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
        OpStreamSnapshot.compact hashFn stateEncode w state0 records atSeq

    /// A chain-only compaction under the canonical config. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let compactChainOnly
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
        OpStreamSnapshot.compactChainOnly hashFn w state0 records atSeq

    /// Replay the tail from a snapshot's state, UNCHECKED — the tail's first `Seq` is not compared
    /// with the snapshot's, and the fault type cannot say so. A forward for one draft; use
    /// `Snapshots.replayFrom`, which checks the seam.
    [<System.Obsolete(SnapshotForward)>]
    let replayFrom
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : Result<'State, int * 'Rej> =
        OpStreamSnapshot.replayFrom w snap tail

    /// `Snapshots.verify` with the mode an optional encoder chooses, whatever the snapshot carries.
    /// Public since Phase 296 (it was internal): a forward for one draft; `Snapshots.firstBreak` is the
    /// localising form.
    [<System.Obsolete(SnapshotForward)>]
    let verifyAcrossWithOpt
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (stateEncode: ('State -> string) option)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        OpStreamSnapshot.verifyAcrossWithOpt cfg hashFn stateEncode w snap tail

    /// Strict boundary verification under `cfg`. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let verifyAcrossWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        OpStreamSnapshot.verifyAcrossWith cfg hashFn stateEncode w snap tail

    /// Chain-only boundary verification under `cfg`. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let verifyAcrossChainOnlyWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        OpStreamSnapshot.verifyAcrossChainOnlyWith cfg hashFn w snap tail

    /// Strict boundary verification under the canonical config. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let verifyAcross
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        OpStreamSnapshot.verifyAcross hashFn stateEncode w snap tail

    /// Chain-only boundary verification under the canonical config. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let verifyAcrossChainOnly
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        OpStreamSnapshot.verifyAcrossChainOnly hashFn w snap tail

    /// A strict snapshot line, whatever the snapshot carries. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotToJsonl (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
        OpStreamSnapshot.snapshotToJsonl stateEncode snap

    /// A chain-only snapshot line, whatever the snapshot carries. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotToJsonlChainOnly (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
        OpStreamSnapshot.snapshotToJsonlChainOnly stateEncode snap

    /// The line's `stateHashed` discriminator: `false` only for an explicit `"stateHashed":false`,
    /// `true` otherwise (a structural fault degrades to strict, the safe default). A forward for one
    /// draft — `Snapshots.ofJsonl` carries the mode on the snapshot it returns.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotStateHashedFromJsonl (line: string) : bool =
        OpStreamSnapshot.snapshotStateHashedFromJsonl line

    /// `Snapshots.ofJsonl`. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotFromJsonlResult
        (stateDecode: string -> Result<'State, string>)
        (line: string)
        : Result<Snapshot<'State>, string> =
        OpStreamSnapshot.snapshotFromJsonlResult stateDecode line

    /// `Snapshots.ofJsonl` over a state decode that cannot fail (a throw is caught and named). A
    /// forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotFromJsonl (stateDecode: string -> 'State) (line: string) : Result<Snapshot<'State>, string> =
        OpStreamSnapshot.snapshotFromJsonl stateDecode line

    // ---- determinism capture / replay (Phase 27) (bodies in Capture.fs) ----

    /// The determinism label below which an effect needs no capture — the `Deterministic` tag.
    /// `Fuaran.Core.Function`'s `Effect.determinismTag` projects `Deterministic` to exactly this
    /// string; the capture seam keys on the label rather than referencing the DU (this layer sits
    /// below `Function` and stays FSharp.Core-only). A non-deterministic label names one or more of
    /// `clock`, `random`, `network`, in that order, joined by `+` (`"clock"`, `"clock+random"`).
    [<Literal>]
    let deterministicTag = OpStreamCapture.deterministicTag

    /// The record seam (Phase 27). Evaluate `effect` once; for a non-`Deterministic` tag, journal
    /// the realized value (encoded via the domain `Codec`) into the hash-chained capture log and
    /// return `(value, extended-log)`; for `deterministicTag` it is pass-through — the value is
    /// returned and nothing is captured (a deterministic effect is reproducible from its inputs).
    /// `encode` is a per-call parameter (GP2 — no new witness field), so one log can hold captures
    /// of heterogeneous value types side by side. `Eff` identifies the boundary for the
    /// seed-injection helper.
    let captureEffectWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (encode: 'v -> string)
        (det: string)
        (eff: string)
        (effect: unit -> 'v)
        (captures: EffectCapture list)
        : 'v * EffectCapture list =
        OpStreamCapture.captureEffectWith cfg hashFn encode det eff effect captures

    /// `captureEffectWith` from the canonical genesis `""` — the record seam as it always was.
    let captureEffect
        (hashFn: HashFn)
        (encode: 'v -> string)
        (det: string)
        (eff: string)
        (effect: unit -> 'v)
        (captures: EffectCapture list)
        : 'v * EffectCapture list =
        OpStreamCapture.captureEffect hashFn encode det eff effect captures

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
        OpStreamCapture.replayEffect decode eff det effect captures

    /// Is `det` a canonical determinism label (Phase 301)? `deterministicTag`, or one or more of
    /// `clock`, `random`, `network` in that order joined by `+` — compared exactly, case included —
    /// the vocabulary `Fuaran.Core.Function`'s `Effect.determinismTag` renders.
    let isDeterminismLabel (det: string) : bool = OpStreamCapture.isCanonicalLabel det

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
        OpStreamCapture.replayEffectStrict decode eff det effect captures

    /// The head of a capture journal under `cfg` (Phase 301) — the last capture's `Hash`, or
    /// `cfg.Genesis` for an empty journal. The chain binds every capture's sequence, so the head
    /// fixes the journal's LENGTH: a journal with its tail cut off has a different head.
    let captureHeadWith (cfg: StreamConfig) (captures: EffectCapture list) : string =
        OpStreamCapture.captureHeadWith cfg captures

    /// `captureHeadWith` under the canonical genesis `""`.
    let captureHead (captures: EffectCapture list) : string =
        OpStreamCapture.captureHeadWith canonicalConfig captures

    /// `verifyCaptures` against an ANCHORED head (Phase 301), under `cfg`: the chain is intact and its
    /// head is `head` — the value a host recorded when the session closed. A journal truncated at the
    /// tail still verifies on its own; against its anchor it does not.
    let verifyCapturesAtWith (cfg: StreamConfig) (hashFn: HashFn) (head: string) (captures: EffectCapture list) : bool =
        OpStreamCapture.verifyCapturesAtWith cfg hashFn head captures

    /// `verifyCapturesAtWith` under the canonical genesis `""`.
    let verifyCapturesAt (hashFn: HashFn) (head: string) (captures: EffectCapture list) : bool =
        OpStreamCapture.verifyCapturesAtWith canonicalConfig hashFn head captures

    /// The seed-injection helper (Phase 27) — surface the recorded value of the first capture for
    /// an effect identity. An effect that reads non-determinism *internally* (a seeded RNG whose
    /// individual draws are not captured) records its seed as the capture value; a well-behaved
    /// consumer reseeds from this so the internal trajectory replays automatically rather than by
    /// discipline. `None` when the journal holds no capture for `eff`.
    let capturedSeed (eff: string) (captures: EffectCapture list) : string option =
        OpStreamCapture.capturedSeed eff captures

    /// `firstCaptureBreak` from `cfg.Genesis` (Phase 296) — the capture chain's genesis read from the
    /// config rather than hard-wired `""`, through the one chain walker. Only `cfg.Genesis` is read: a
    /// capture's pre-image is the capture format, not the op payload.
    let firstCaptureBreakWith (cfg: StreamConfig) (hashFn: HashFn) (captures: EffectCapture list) : ChainBreak option =
        OpStreamCapture.firstCaptureBreakWith cfg hashFn captures

    /// The first integrity fault in a capture chain (Phase 27), or `None` if intact — the capture
    /// analogue of `firstChainBreak`. Walks the chain and returns the first capture whose
    /// sequence, prev-link, or hash fails; reuses `ChainBreak` so a capture break localises
    /// exactly as an op break does (Phase 21).
    let firstCaptureBreak (hashFn: HashFn) (captures: EffectCapture list) : ChainBreak option =
        OpStreamCapture.firstCaptureBreak hashFn captures

    /// Recompute the capture chain and confirm every link — the capture analogue of `verifyChain`.
    /// A tampered captured value (or a reordered / dropped capture) fails this, so a recorded
    /// effect value is tamper-evident exactly like an op.
    let verifyCaptures (hashFn: HashFn) (captures: EffectCapture list) : bool =
        OpStreamCapture.verifyCaptures hashFn captures

    /// One JSON object per line for a capture log — the `value` field embedded as raw JSON (the
    /// domain `Codec` output), so a round-trip preserves it byte-for-byte. Lets a recorded session
    /// persist its captures alongside its ops, so "replay exactly what happened" holds *from the
    /// file*.
    let captureToJsonl (captures: EffectCapture list) : string = OpStreamCapture.captureToJsonl captures

    /// `captureToJsonl` that refuses a captured value the reader cannot read back (Phase 301) — the
    /// `Jsonl.checkRaw` check over each `Value` (member `value`). A capture's value is hashed raw, so
    /// a value with a line break or whitespace either side read back changed and failed
    /// `verifyCaptures`. On `Ok` the text is `captureToJsonl`'s, byte for byte.
    let tryCaptureToJsonl (captures: EffectCapture list) : Result<string, JsonlWriteFault> =
        OpStreamCapture.tryCaptureToJsonl captures

    /// Parse a capture log back from JSONL (Phase 27) — the `value` raw span is preserved
    /// byte-for-byte (the domain decoder receives exactly what `Codec` produced), so a round-trip
    /// is identical and the chain still `verifyCaptures`. Uses the same self-contained, Fable-clean
    /// line scanner as `fromJsonl`; a malformed line is a named `Error`, never an exception.
    let captureFromJsonl (text: string) : Result<EffectCapture list, string> = OpStreamCapture.captureFromJsonl text

    // ---- cryptographic attestation (Phase 320) (the head in Chain.fs, the rest in Capture.fs) ----

    /// The default no-op attestation sink: never signs (`Sign` ⇒ `None`) and verifies nothing
    /// (`Verify` ⇒ `false`). Signing is opt-in — a stream that does not plug in a real sink behaves
    /// exactly as before. Enterprise hosts supply a KMS / HSM-backed `IAttestationSink`.
    let noAttestation: IAttestationSink = OpStreamCapture.noAttestation

    /// The current head of a chain under an explicit `StreamConfig` (Phase 296) — the last record's
    /// `Hash`, or `cfg.Genesis` for an empty chain: the seed every chain walker starts from, so an
    /// empty stream appended under a non-empty genesis has the head its first record's `PrevHash`
    /// names (the class of defect Phase 227 fixed for `snapshotAtOpt`).
    let headWith (cfg: StreamConfig) (records: OpRecord<'Op> list) : string = OpStreamChain.headWith cfg records

    /// The current head of a chain — the last record's `Hash`, or the canonical genesis `""` for an
    /// empty chain. The thing an attestation signs (the hash-chain attests the whole prefix). A
    /// stream under another genesis reads its head with `headWith`.
    let head (records: OpRecord<'Op> list) : string = OpStreamChain.head records

    /// Attest the current head of a chain at a commit / publish boundary (Phase 320). Signs the head
    /// hash via `sink` — O(commits), not O(ops), since the hash-chain already binds every prior op
    /// into the head. `None` from the no-op sink. The signed `Attestation` plus deterministic replay
    /// is the replay-as-provenance contract: a verifier re-derives the head from the op log, then
    /// `verifyAttestation`s the signature against it.
    let attestHead (sink: IAttestationSink) (records: OpRecord<'Op> list) : Attestation option =
        OpStreamCapture.attestHead sink records

    /// Re-verify an attestation against a chain's *current* head (Phase 320). Independent
    /// re-verification: a third party recomputes the head from the records (`verifyChain` proves the
    /// chain is intact; `head` reads its tip) and checks the signature covers exactly that head.
    let verifyAttestation (sink: IAttestationSink) (attestation: Attestation) (records: OpRecord<'Op> list) : bool =
        OpStreamCapture.verifyAttestation sink attestation records

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
        (hashFn: HashFn)
        (party: Actor)
        (records: OpRecord<'Op> list)
        : Attestation option =
        sink.Sign(attestationSubject hashFn party (head records))

    /// `verifyAttestation` for a party's attestation (Phase 311): `true` only when the attestation
    /// names the subject this party's attestation of the chain's current head signs AND the sink
    /// accepts it there — so an attestation made by another party, or over another head, is refused
    /// before the sink is asked.
    let verifyAttestationAs
        (sink: IAttestationSink)
        (hashFn: HashFn)
        (party: Actor)
        (attestation: Attestation)
        (records: OpRecord<'Op> list)
        : bool =
        let subject = attestationSubject hashFn party (head records)
        attestation.Head = subject && sink.Verify attestation subject

    // ---- compare-and-append / optimistic concurrency (Phase 79) (bodies in Chain.fs) ----

    /// `appendIf` under an explicit `StreamConfig` (Phase 296) — the head an empty stream is compared
    /// against is `cfg.Genesis`, and the record chains under `cfg.Payload`.
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
        OpStreamChain.appendIfWith cfg hashFn w expectedHead actor op state records

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
    ///
    /// **One walk (Phase 296)** for the head check and the append together, where there were four.
    let appendIf
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (expectedHead: string)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, AppendRejection<'Rej>> =
        OpStreamChain.appendIf hashFn w expectedHead actor op state records

    // ---- attributed-stream lift (Phase 81) (bodies in Attributed.fs) ----

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
            OpStreamAttributed.Attributed.encodeEnvelope encodeInner a

        /// Decode an attribution envelope, delegating the inner `op` raw span to `decodeInner`. Reuses
        /// the self-contained flat-object scanner (`Jsonl.topFields`) — FSharp.Core-only + Fable-clean —
        /// so an attributed host decodes / verifies / replays in-browser without a host boundary. A
        /// structural fault or an inner-decode `Error` is a named `Error`, never an exception (the
        /// recoverable-envelope discipline). An absent / `null` `turn` decodes to `None`.
        let decodeEnvelope
            (decodeInner: string -> Result<'Op, string>)
            (line: string)
            : Result<Attributed<'Op>, string> =
            OpStreamAttributed.Attributed.decodeEnvelope decodeInner line

        /// Lift a `StreamWitness<'Op,'State,'Rej>` to `StreamWitness<Attributed<'Op>,'State,'Rej>` — the
        /// derived attributed witness. `Apply` delegates to the inner `Apply` on `.Op`; `Encode`/`Decode`
        /// wrap the inner codec in the attribution envelope. No new witness field (GP2): the lift is a
        /// pure value over the existing three-seam witness, so an attributed stream appends / verifies /
        /// replays through the unchanged `OpStream` surface and the chain hash covers the attribution.
        let liftWitness (w: StreamWitness<'Op, 'State, 'Rej>) : StreamWitness<Attributed<'Op>, 'State, 'Rej> =
            OpStreamAttributed.Attributed.liftWitness w

        /// Project an attributed stream to "who appended what" — records grouped by actor id, each group
        /// in stream order. A pure fold, no host dependency.
        let byActor (records: OpRecord<Attributed<'Op>> list) : Map<string, OpRecord<Attributed<'Op>> list> =
            OpStreamAttributed.Attributed.byActor records

        /// Project an attributed stream by session id — each session's appended records in stream order.
        /// A pure fold, no host dependency.
        let bySession (records: OpRecord<Attributed<'Op>> list) : Map<string, OpRecord<Attributed<'Op>> list> =
            OpStreamAttributed.Attributed.bySession records

    // ---- idempotent append (Phase 82) (bodies in Chain.fs) ----

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
        OpStreamChain.appendIdempotent hashFn w key actor op state index records

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
        OpStreamChain.appendIdempotentIf hashFn w key expectedHead actor op state index records
