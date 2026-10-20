namespace Fuaran.Core

/// One append-only, hash-chained op record. `Hash = hashFn PrevHash payload`, where
/// `payload` is the canonical `{seq, actor, op}` envelope (the `actor` is the typed `Actor`
/// object since Phase 320). The chain makes tampering — including attribution tampering —
/// detectable (`verifyChain`) and replay deterministic.
type OpRecord<'Op> =
    {
        /// Zero-based position in the stream: the first record is `0`, and the verifiers refuse a
        /// record whose `Seq` is not its index (`ChainBreakReason.SequenceMismatch`).
        Seq: int
        /// Who appended the op. Folded into the hashed payload, so rewriting it breaks `Hash`.
        Actor: Actor
        /// The domain op, applied by the witness's `Apply` on replay and hashed as its `Encode`.
        Op: 'Op
        /// The predecessor's `Hash`, or the config's `Genesis` (`""` canonically) on the first record.
        PrevHash: string
        /// `hashFn PrevHash payload` over the `{seq, actor, op}` payload; the next record's `PrevHash`
        /// and, on the last record, the stream's `head`.
        Hash: string
    }

/// WHICH integrity check a `ChainBreak` failed (Phase 125) — the closed set of reasons the chain
/// walkers can report, typed where the reason is MINTED rather than re-derived downstream by
/// string-matching this library's spellings.
///
/// **Three named cases for four spellings, deliberately.** `firstChainBreak` and
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
    {
        /// The failing item's position in the list that was walked — not its `Seq`, which differs for a
        /// tail walked from a snapshot boundary.
        Index: int
        /// Which check failed. The walkers test sequence, then prev-link, then hash, and report the first.
        Reason: ChainBreakReason
        /// The value the check required: the sequence number as decimal text, the predecessor's hash, or
        /// the recomputed digest, per `Reason`.
        Expected: string
        /// The value the item actually carried, spelled as `Expected` is.
        Got: string
    }

/// The two-seam witness the whole module lifts over: `Apply` is the domain reducer,
/// `Encode`/`Decode` the domain op codec. Per the Documents extraction assessment,
/// parameterise over these and the op-stream module is line-for-line shared.
/// `Decode` returns `Result` (Phase 252) — the same recoverable-envelope discipline the
/// rest of the substrate follows, so a malformed op surfaces as a named `Error`, never an
/// exception (the F3 adoption finding: every domain decode is already `Result`-returning).
type StreamWitness<'Op, 'State, 'Rej> =
    {
        /// The domain reducer. An `Error` refuses the op: `append` chains no record for it, and
        /// `replay` stops at it with the record's index.
        Apply: 'Op -> 'State -> Result<'State, 'Rej>
        /// The op's JSON encoding. It is folded into every record's hash, so it must be deterministic —
        /// the same op always renders the same bytes — or a stream stops verifying.
        Encode: 'Op -> string
        /// The inverse of `Encode`, used when a stream is read back; an `Error` names why the text is
        /// not an op and surfaces as a read fault rather than an exception.
        Decode: string -> Result<'Op, string>
    }

/// `prevHash -> payload -> hash`. Pluggable so a host can swap FNV-1a (portable,
/// Fable-clean default) for SHA-256 at its boundary while keeping cross-host parity.
type HashFn = string -> string -> string

/// How this package builds the pre-images it hashes (Phase 255 — finding F4; one value with the
/// encoding profile since Phase 516): the chain-*payload* binding `Payload seq actor opJson ->
/// payload`, the genesis sentinel for the first record's `PrevHash`, and the JSON string spelling
/// (`Quote`) of every string this package spells inside a pre-image it builds itself. Cross-host hash
/// parity is already pluggable via `HashFn`; the payload format is the other half. A domain whose
/// persisted streams use its own legacy chain format (Documents `"%d|%s|%s|%s"` + `"genesis"`, Calc /
/// Geom their own) matches that format with a `StreamConfig`, verifies the existing streams, then
/// `OpStream.rehash`es to the canonical form — no flag-day re-hash of history.
///
/// **The encoding profiles are constructors** (Phase 516): `StreamConfig.canonical` is the
/// `{seq,actor,op}` envelope + `""` genesis with every control character spelled `\u00xx` (the
/// spelling since `0.33.0`, Phase 287 — `Wire`'s `EncodingProfile.V2`); `StreamConfig.legacyEscape` is
/// the same envelope with `\n`, `\r` and `\t` spelled short (`0.30.0` through `0.32.0` — `V1`); and
/// `StreamConfig.legacyActor` is the pre-Phase-320 envelope, which folded the actor as a bare string.
/// A store reads the profile it declares as one of these values, and migrates by `OpStream.rehash`
/// between two streams built over them.
type StreamConfig =
    {
        /// `seq -> actor -> encodedOp -> payload`: the text the `HashFn` digests, after the previous
        /// hash, for each record. Changing it changes every hash in the stream.
        Payload: int -> Actor -> string -> string
        /// The `PrevHash` of the first record, and the `head` of an empty stream.
        Genesis: string
        /// The JSON string spelling of the strings this package embeds in a pre-image it builds itself
        /// (Phase 516): the actor inside a DAG node id (`StreamConfig.spellActor`) and a capture's effect
        /// and determinism tags. `StreamConfig.canonical.Quote` spells every control character as
        /// lower-case `\u00xx`; `StreamConfig.legacyEscape.Quote` spells `\n`, `\r` and `\t` short. A
        /// host's own config takes the canonical one unless its store was written under another.
        Quote: string -> string
    }

/// An op stream's whole configuration as ONE value (Phase 516): how its records are hashed
/// (`Config`), the chain hash (`HashFn`) and the domain witness (`Witness`). Every chain verb takes
/// it once, so a stream cannot be appended under one config and verified, compacted or attested under
/// another — the `…With cfg` twin each verb used to carry is the verb. Built with
/// `OpStream.canonical hashFn w` (the canonical config) or `OpStream.ofConfig cfg hashFn w` (a legacy
/// profile's config, or a host's own).
type Stream<'Op, 'State, 'Rej> =
    {
        /// The payload binding, genesis and string spelling every record is hashed under.
        Config: StreamConfig
        /// The chain hash, folded as `HashFn prevHash payload`.
        HashFn: HashFn
        /// The domain reducer and op codec.
        Witness: StreamWitness<'Op, 'State, 'Rej>
    }

/// The boundary a chained run of records hangs from (Phase 530): the sequence its first record
/// carries and the hash that record links to. A stream's own anchor is its genesis (`Anchor.genesis`:
/// sequence zero, the config's `Genesis`); a tail kept above a snapshot hangs from the snapshot's
/// boundary (`{ Seq = snap.Seq; PrevHash = snap.PrevHash }`); a DAG checkpoint stands at sequence zero
/// with its node's id as the boundary hash. The tip of a run is the anchor the next record hangs from
/// (`ChainShape.tip`).
type Anchor =
    {
        /// The `Seq` the run's first record must carry — the number of records before it.
        Seq: int
        /// The hash the run's first record must name as its `PrevHash`.
        PrevHash: string
    }

/// What makes a list of records a hash chain (Phase 530): where each record says it sits, what it
/// links to, what it hashes to, the payload its hash is computed over, and the record re-linked. The
/// chain verbs are written ONCE over a shape (`ChainShape.walk`, `tip`, `append`, `rehash`), and every
/// chained record kind in this package is an instance of it — the op record (`ChainShape.records`),
/// the positional and keyed capture journals, and a sealed state (`Sealed`), whose one-record chain
/// hangs from its anchor. A host's own record kind joins the same verbs by supplying its shape.
type ChainShape<'R> =
    {
        /// The record's zero-based position in its stream.
        SeqOf: 'R -> int
        /// The hash the record names as its predecessor's.
        PrevOf: 'R -> string
        /// The record's own hash, the next record's `PrevOf`.
        HashOf: 'R -> string
        /// The text the record's hash is computed over, after its predecessor's hash (`hashFn prev
        /// payload`). It must not read the record's link: re-linking a record never changes it.
        PayloadOf: 'R -> string
        /// The record with its predecessor's hash and its own hash set, every other field kept.
        Link: 'R -> string -> string -> 'R
    }

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
    /// The stream's head is not the one the caller expected: `expected` is the caller's, `actual` the
    /// stream's current head (its genesis when empty). Nothing was applied or chained.
    | StaleHead of expected: string * actual: string
    /// The head matched but the witness's `Apply` refused the op; the rejection is forwarded verbatim.
    | Domain of 'Rej

/// A stable reference to one chained record (Phase 82) — the entry an idempotency key already
/// produced. `Seq` names its position in the stream; `Hash` its chain identity — together they let
/// a retrying caller locate AND integrity-check the record its earlier attempt landed, without the
/// index holding the record itself (the ref is O(1) per key regardless of op size).
type EntryRef =
    {
        /// The record's zero-based `Seq` in its stream.
        Seq: int
        /// The record's chain `Hash` — compare it with the record at `Seq` to confirm it is the same entry.
        Hash: string
    }

/// A pure index of seen invocation keys → the entry each key first produced (Phase 82). A **value
/// the caller threads** — Core holds no registry state (GP6): rebuild it from any stream with
/// `KeyIndex.ofStream` (a total fold), or maintain it incrementally via the `KeyIndex` returned by
/// `OpStream.appendIdempotent` (the two agree — the rebuild-parity law). Key uniqueness scope is
/// **per-stream**: an index is only meaningful against the stream it was built from / threaded
/// alongside; cross-stream dedup is a host concern, like storage and locking (GP3/GP6).
type KeyIndex =
    {
        /// Invocation key → the entry that key FIRST produced; a later add under the same key never
        /// replaces it.
        Seen: Map<string, EntryRef>
    }

/// The typed outcome of an idempotent append (Phase 82) — enumerated, never a throw (GP4).
/// `Appended` carries the advanced state, the extended stream, and the incrementally-updated
/// `KeyIndex` (so the caller threads all three forward); `Duplicate` names the entry the key
/// already produced (GP5) — the at-least-once retry *converges* on its earlier result instead of
/// double-applying, and the stream/index the caller holds are untouched (immutable values; no
/// partial write). A domain-reducer rejection is not an outcome of the idempotency guard — it is
/// forwarded on the `Result` error channel exactly as `append` forwards it.
[<RequireQualifiedAccess>]
type AppendOutcome<'Op, 'State> =
    /// The key was new and the op applied: the advanced state, the WHOLE extended stream (not just the
    /// new record), and the index with the key bound to that record.
    | Appended of state: 'State * records: OpRecord<'Op> list * index: KeyIndex
    /// The key was already indexed: the entry it first produced. The op was not applied — the reducer
    /// never ran — and nothing was chained.
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

/// Companion helpers for `Anchor` (Phase 530).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Anchor =

    /// A stream's own anchor under `cfg`: sequence zero, linked to the config's genesis.
    let genesis (cfg: StreamConfig) : Anchor = { Seq = 0; PrevHash = cfg.Genesis }

/// The encoding profiles as constructors (Phase 516) — the configs a store declares its chain by, and
/// the spelling a profile gives the actor. Each is the whole `StreamConfig`: a stream built over one
/// hashes, verifies and re-mints under it, and `OpStream.rehash` moves a stream between two of them.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module StreamConfig =

    /// The canonical `{seq, actor, op}` payload the chain hash is computed over. Since Phase 320 the
    /// `actor` is the typed `Actor` *object* (`Actor.encode`), so altering the attribution changes the
    /// hash — attribution is folded into the integrity chain. Since Phase 287 the actor's strings carry
    /// every control character as `\u00xx`, the spelling the UI host's DAG chain and the TypeScript twin
    /// already fold, so the linear hash of a record agrees with both.
    let private payloadAs (quote: string -> string) (seq: int) (actor: Actor) (opJson: string) : string =
        "{\"seq\":"
        + string seq
        + ",\"actor\":"
        + Actor.encodeWith quote actor
        + ",\"op\":"
        + opJson
        + "}"

    /// The canonical config — the `{seq,actor,op}` envelope over the typed actor, the `""` genesis, and
    /// every control character in a pre-image's strings spelled `\u00xx` (the spelling since `0.33.0`,
    /// `Wire`'s `EncodingProfile.V2`). What `OpStream.canonical` builds a stream over.
    let canonical: StreamConfig =
        { Payload = payloadAs JsonString.quote
          Genesis = ""
          Quote = JsonString.quote }

    /// The **pre-Phase-287** config (`0.30.0` through `0.32.0`; `Wire`'s `EncodingProfile.V1`) — the
    /// same `{seq,actor,op}` envelope over the same typed `Actor` object, but with `\n`, `\r` and `\t`
    /// inside the strings it spells written as the short escapes rather than `\u000a` / `\u000d` /
    /// `\u0009`, in the actor of the chain payload, the actor of a DAG node id and a capture's tags
    /// alike. The `from` stream of a migration for a store persisted between Phase 320 and Phase 287:
    /// verify under it, then `OpStream.rehash` to canonical.
    ///
    /// A record whose actor holds no control character has the SAME payload under both configs, so for
    /// such a stream the rehash is a no-op that reproduces every hash — which is the case for every
    /// store this package's own tests and consumers have written. The rehash proves that rather than
    /// assuming it: verifying an unmigrated control-free store under `canonical` already passes.
    let legacyEscape: StreamConfig =
        { Payload = payloadAs JsonString.quoteLegacy
          Genesis = ""
          Quote = JsonString.quoteLegacy }

    /// The **pre-Phase-320** config — it folded the actor as a *bare JSON string* (`"actor":"alice"`)
    /// rather than the typed object. The `from` stream of a migration for a store persisted before the
    /// typed-actor change: read it with `Jsonl.readLegacyActor` (which lifts each bare-string actor to
    /// `Human`), verify under it, then `OpStream.rehash` to canonical — the standard Phase-255
    /// migration shape. Pre-320 streams only ever held `Human` actors; an `Agent` reaching this payload
    /// is migration misuse, so it folds in just the id (best effort). The bare string keeps the pre-287
    /// short escapes: this payload reproduces the bytes a pre-320 writer produced, and that writer
    /// wrote `\n`.
    let legacyActor: StreamConfig =
        { Payload =
            fun seq actor opJson ->
                "{\"seq\":"
                + string seq
                + ",\"actor\":"
                + JsonString.quoteLegacy (Actor.id actor)
                + ",\"op\":"
                + opJson
                + "}"
          Genesis = ""
          Quote = JsonString.quoteLegacy }

    /// The actor's pre-image under `cfg`'s spelling: `Actor.encode` under `canonical`, and under
    /// `legacyEscape` the same object with `\n`, `\r` and `\t` spelled short — the bytes `0.30.0`
    /// folded into every chain hash and DAG node id.
    let spellActor (cfg: StreamConfig) (actor: Actor) : string = Actor.encodeWith cfg.Quote actor

/// The chain verbs, written once over a record kind's shape (Phase 530). `OpStream`'s op-stream
/// verbs, the capture journals', the snapshot and the compacted stream's are each one of these over
/// its own instance, so a break is found the same way in an op stream, a journal, a snapshot's tail or
/// a host's own records.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ChainShape =

    /// The op record's shape (Phase 530): the payload `cfg` binds over `{seq, actor, encode op}`.
    /// `ChainShape.records StreamConfig.canonical w.Encode` is the shape `OpStream.verifyChain` walks under
    /// the canonical stream.
    let records (cfg: StreamConfig) (encode: 'Op -> string) : ChainShape<OpRecord<'Op>> =
        { SeqOf = fun r -> r.Seq
          PrevOf = fun r -> r.PrevHash
          HashOf = fun r -> r.Hash
          PayloadOf = fun r -> cfg.Payload r.Seq r.Actor (encode r.Op)
          Link = fun r prev hash -> { r with PrevHash = prev; Hash = hash } }

    /// THE chain walker (Phase 296; over a shape since Phase 530) — the first record of `items`, hung
    /// from `anchor`, whose sequence, prev-link or hash fails, `Index` being its position in `items`.
    /// The checks run in that order and the digest is computed only after the two cheap ones pass. A
    /// whole stream is walked from its genesis anchor; a segment from the anchor it hangs from.
    let walk (shape: ChainShape<'R>) (hashFn: HashFn) (anchor: Anchor) (items: 'R list) : ChainBreak option =
        let rec go (prev: string) (i: int) =
            function
            | [] -> None
            | r :: rest ->
                let expectedSeq = anchor.Seq + i

                if shape.SeqOf r <> expectedSeq then
                    Some
                        { Index = i
                          Reason = ChainBreakReason.SequenceMismatch
                          Expected = string expectedSeq
                          Got = string (shape.SeqOf r) }
                elif shape.PrevOf r <> prev then
                    Some
                        { Index = i
                          Reason = ChainBreakReason.PrevHashLinkBroken
                          Expected = prev
                          Got = shape.PrevOf r }
                else
                    let expectedHash = hashFn prev (shape.PayloadOf r)

                    if shape.HashOf r <> expectedHash then
                        Some
                            { Index = i
                              Reason = ChainBreakReason.HashMismatch
                              Expected = expectedHash
                              Got = shape.HashOf r }
                    else
                        go (shape.HashOf r) (i + 1) rest

        go anchor.PrevHash 0 items

    /// `tip` by a hash projection — the ONE tip body (Phase 296's `tipOf`, Phase 388's one body every
    /// log reads its tip through). Inline, so a projection that is a field read stays one rather than a
    /// closure call per record: a recursive walk, or one through a closure, cost more than the three
    /// library walks it replaced (measured, Phase 296).
    let inline internal tipBy ([<InlineIfLambda>] hashOf: 'R -> string) (anchor: Anchor) (items: 'R list) : Anchor =
        let mutable n = 0
        let mutable last = anchor.PrevHash
        let mutable rest = items

        while not rest.IsEmpty do
            last <- hashOf rest.Head
            n <- n + 1
            rest <- rest.Tail

        { Seq = anchor.Seq + n
          PrevHash = last }

    /// The anchor the next record hangs from: the run's anchor advanced past `items` — its sequence
    /// plus their count, and the last one's hash (the anchor's own `PrevHash` when there are none). One
    /// walk of the list.
    let tip (shape: ChainShape<'R>) (anchor: Anchor) (items: 'R list) : Anchor = tipBy shape.HashOf anchor items

    /// `record` linked at `at`: its predecessor's hash is `at.PrevHash` and its own hash is computed
    /// over its payload. The record must already carry `at.Seq` — the payload binds it.
    let linkAt (shape: ChainShape<'R>) (hashFn: HashFn) (at: Anchor) (record: 'R) : 'R =
        shape.Link record at.PrevHash (hashFn at.PrevHash (shape.PayloadOf record))

    /// Chain one record onto `items` (hung from `anchor`): `make seq` builds it at the tip's sequence,
    /// and it is linked to the tip. The whole extended list.
    let append (shape: ChainShape<'R>) (hashFn: HashFn) (anchor: Anchor) (make: int -> 'R) (items: 'R list) : 'R list =
        let at = tip shape anchor items
        items @ [ linkAt shape hashFn at (make at.Seq) ]

    /// Re-chain a run (Phase 255's migration, over a shape since Phase 530; a hash per side since Phase
    /// 516): `items` verified under `from` and `fromHash`, hung from `anchor` — the first break is the
    /// `Error`, and nothing is re-chained — then each record mapped by `map` and linked under `into` and
    /// `intoHash`, hung from `{ anchor with PrevHash = intoPrev }`. Each record keeps its sequence; only
    /// its link and hash change. `OpStream.rehash` is this with `map = id` from one stream's genesis to
    /// another's; a stream whose ops are re-scoped is this with the re-scoping as `map`.
    let rehash
        (from: ChainShape<'R>)
        (fromHash: HashFn)
        (into: ChainShape<'S>)
        (intoHash: HashFn)
        (anchor: Anchor)
        (intoPrev: string)
        (map: 'R -> 'S)
        (items: 'R list)
        : Result<'S list, ChainBreak> =
        match walk from fromHash anchor items with
        | Some b -> Error b
        | None ->
            let rec go (prev: string) acc =
                function
                | [] -> List.rev acc
                | r :: rest ->
                    let m = map r
                    let r' = into.Link m prev (intoHash prev (into.PayloadOf m))
                    go (into.HashOf r') (r' :: acc) rest

            Ok(go intoPrev [] items)

/// Append-only hash-chained op stream + deterministic replay, generic over the `StreamWitness` — the
/// highest-genericity core layer. Since Phase 516 the chain verbs live here with their bodies, and each
/// takes the stream's whole configuration as one `Stream` value; the JSONL readers and writer are
/// `Jsonl`'s, the snapshot family `Snapshots`', the compacted stream `Compacted`'s, determinism
/// capture `Capture`'s and `KeyedCapture`'s, attestation `Attestation`'s and the attributed lift
/// `Attributed`'s.
module OpStream =

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

    /// The default portable hash: FNV-1a over `prevHash | payload`. **Portable is meant literally** —
    /// value-identical on .NET and under Fable, so a browser replaying a chain a server wrote
    /// computes the same hashes. That was not true before `0.6.0`; see the copy note at `fnv1a`.
    let defaultHash: HashFn = fun prev payload -> fnv1a (prev + "|" + payload)

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
    let sha256Hash: HashFn = fun prev payload -> Sha256.hex (prev + "|" + payload)

    /// A stream over an explicit config (Phase 516): a legacy profile's (`StreamConfig.legacyEscape`,
    /// `StreamConfig.legacyActor`) or a host's own. The `Stream` record spelled as a function, so the
    /// type is never left to inference over its field labels.
    let ofConfig
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        : Stream<'Op, 'State, 'Rej> =
        { Config = cfg
          HashFn = hashFn
          Witness = w }

    /// The canonical stream (Phase 516): `w` hashed by `hashFn` under `StreamConfig.canonical` — what
    /// every stream a host writes today is.
    let canonical (hashFn: HashFn) (w: StreamWitness<'Op, 'State, 'Rej>) : Stream<'Op, 'State, 'Rej> =
        ofConfig StreamConfig.canonical hashFn w

    /// The stream with no records. Its `head` is the config's genesis, and the first `append` onto it
    /// chains a record at `Seq` 0.
    let empty: OpRecord<'Op> list = []

    /// The tip of an op stream chained from `cfg`'s genesis: its length and its last hash.
    let private tip (cfg: StreamConfig) (records: OpRecord<'Op> list) : int * string =
        let at =
            ChainShape.tipBy (fun (r: OpRecord<'Op>) -> r.Hash) (Anchor.genesis cfg) records

        at.Seq, at.PrevHash

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
        hashFn prev (cfg.Payload seq actor encodedOp)

    /// Chain ONE op at sequence number `seq` onto tip hash `prev` (`ChainShape.linkAt` over the op
    /// record's shape, Phase 530): the state after it and the record it chained, or the witness's
    /// rejection. The single-op appends call this directly, so the record
    /// they return is the one chained here, never one read back out of a list (Phase 496).
    let internal chainOne
        (s: Stream<'Op, 'State, 'Rej>)
        (actor: Actor)
        (seq: int)
        (prev: string)
        (op: 'Op)
        (state: 'State)
        : Result<'State * OpRecord<'Op>, 'Rej> =
        s.Witness.Apply op state
        |> Result.map (fun state' ->
            state',
            ChainShape.linkAt
                (ChainShape.records s.Config s.Witness.Encode)
                s.HashFn
                { Seq = seq; PrevHash = prev }
                { Seq = seq
                  Actor = actor
                  Op = op
                  PrevHash = prev
                  Hash = "" })

    /// Chain `ops` onto a stream whose length is `seq0` and whose tip hash is `prev0`, applying each
    /// op in turn: the new records in order and the final state, or the first rejection with its index
    /// in `ops`. Nothing is copied; the caller splices the new records on once.
    let private chainOps
        (s: Stream<'Op, 'State, 'Rej>)
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
                match chainOne s actor (seq0 + i) prev op st with
                | Error e -> Error(i, e)
                | Ok(st', r) -> go (i + 1) r.Hash st' (r :: acc) rest

        go 0 prev0 state [] ops

    /// Apply an op to the state; on success, chain a record onto the stream under `s`'s config and
    /// hash. Returns the new state and the extended record list, or the domain rejection unchanged.
    /// A stream in a legacy chain format is extended in its own format by a `Stream` built over that
    /// format's config (`OpStream.ofConfig`).
    ///
    /// **Cost (Phase 296).** One walk of `records` and one copy — the list's end is where a record
    /// goes, and an immutable list reaches its end only by walking it, so a single `append` is linear
    /// in the stream and a loop of them is quadratic. A caller chaining several ops chains them with
    /// `appendMany`, which walks and copies ONCE for the batch.
    let append
        (s: Stream<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, 'Rej> =
        let n, last = tip s.Config records

        chainOne s actor n last op state
        |> Result.map (fun (state', r) -> state', records @ [ r ])

    /// Chain several ops by one actor in ONE walk of the stream (Phase 296): the same records, byte for
    /// byte, as folding `append` over `ops`, at the cost of one `append` rather than `List.length ops`
    /// of them. All or nothing: the first op the domain rejects is `Error(index in ops, rejection)` and
    /// nothing is chained.
    let appendMany
        (s: Stream<'Op, 'State, 'Rej>)
        (actor: Actor)
        (ops: 'Op list)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, int * 'Rej> =
        let n, last = tip s.Config records

        chainOps s actor n last ops state
        |> Result.map (fun (state', added) -> state', records @ added)

    /// Chain one op onto the stream WITHOUT applying it (Phase 315) — no state in, no state out, no
    /// domain rejection. For an adapter whose apply runs somewhere else (at `replay`, or in its own
    /// reducer before it calls here), which used to recompute the payload and hash by hand to get the
    /// record `append` would have written. The record is exactly that one — `chainHashOf` under `s`'s
    /// config and hash, so a stream built with it verifies under `s` — and it carries nothing `append`
    /// would have checked: an op the domain would refuse is chained all the same, and `replay` is where
    /// it is refused. Only the witness's `Encode` is read. One walk and one copy, as `append`.
    let appendChainOnly
        (s: Stream<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (records: OpRecord<'Op> list)
        : OpRecord<'Op> list =
        ChainShape.append
            (ChainShape.records s.Config s.Witness.Encode)
            s.HashFn
            (Anchor.genesis s.Config)
            (fun n ->
                { Seq = n
                  Actor = actor
                  Op = op
                  PrevHash = ""
                  Hash = "" })
            records

    /// The first integrity fault in a chain under `s` (Phase 21; under the stream's own config since
    /// Phase 516) — the localising verifier. Walks the chain from `s`'s genesis and returns the first
    /// record whose sequence, prev-link, or hash fails; `None` for an intact chain.
    let firstChainBreak (s: Stream<'Op, 'State, 'Rej>) (records: OpRecord<'Op> list) : ChainBreak option =
        ChainShape.walk (ChainShape.records s.Config s.Witness.Encode) s.HashFn (Anchor.genesis s.Config) records

    /// Recompute the chain from the records under `s` and confirm every link. Detects reordering,
    /// tampering with an op or its attribution, and a broken prev-link. The migration entry point too:
    /// a domain verifies its persisted legacy streams under a stream over their own config before
    /// `rehash`ing them. One definition of integrity, localising (`firstChainBreak`) or boolean.
    let verifyChain (s: Stream<'Op, 'State, 'Rej>) (records: OpRecord<'Op> list) : bool =
        firstChainBreak s records |> Option.isNone

    /// Migrate a chain from one stream to another (Phase 255; typed break, Phase 296; one verb over two
    /// streams since Phase 516). Verifies `records` under `from` first — a chain that does not verify
    /// under its declared format is a migration the caller must not silently re-bless, so it is `Error`
    /// carrying the first `ChainBreak` — then re-derives every `PrevHash` / `Hash` under `into`, from
    /// `into`'s genesis. The ops, actors and sequence numbers are the source of truth; only the hash
    /// chain changes. The result verifies under `into`, and rehashing it back is `records` again.
    ///
    /// The two streams may differ in any part: the config (a payload envelope or string spelling, the
    /// legacy-actor and legacy-escape migrations), the hash (FNV-1a to SHA-256), or the witness's
    /// `Encode` (a store whose op encoder rendered through `Json.renderWith EncodingProfile.V1` before
    /// `0.33.0` verifies with its OLD encoder and re-mints with its new one). Only the witnesses'
    /// `Encode` is read. A host that keys heads, snapshots or attestations by hash maps each old hash
    /// to the new one by position: the result has one record per source record, in order.
    let rehash
        (from: Stream<'Op, 'State, 'Rej>)
        (into: Stream<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : Result<OpRecord<'Op> list, ChainBreak> =
        ChainShape.rehash
            (ChainShape.records from.Config from.Witness.Encode)
            from.HashFn
            (ChainShape.records into.Config into.Witness.Encode)
            into.HashFn
            (Anchor.genesis from.Config)
            into.Config.Genesis
            id
            records

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

    // ---- the chain head (Phase 296; what an attestation signs, Phase 320) ----

    /// The current head of a chain — the last record's `Hash`, or `cfg`'s genesis for an empty chain:
    /// the seed every chain walker starts from, so an empty stream appended under a non-empty genesis
    /// has the head its first record's `PrevHash` names (the class of defect Phase 227 fixed in the
    /// snapshot entry points). The thing an attestation signs (the hash chain attests the whole
    /// prefix).
    let head (cfg: StreamConfig) (records: OpRecord<'Op> list) : string = snd (tip cfg records)

    // ---- compare-and-append / optimistic concurrency (Phase 79) ----

    /// Append at the tip `(n, head)` of `records`: the extended stream AND the entry it chained, so the
    /// idempotent forms index the new record without walking the stream again.
    let private appendAt
        (s: Stream<'Op, 'State, 'Rej>)
        (n: int, head: string)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list * EntryRef, 'Rej> =
        chainOne s actor n head op state
        |> Result.map (fun (state', r) -> state', records @ [ r ], { Seq = r.Seq; Hash = r.Hash })

    /// The compare-and-append core: `appendAt` behind the head check.
    let private appendIfCore
        (s: Stream<'Op, 'State, 'Rej>)
        (expectedHead: string)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list * EntryRef, AppendRejection<'Rej>> =
        let n, actualHead = tip s.Config records

        if expectedHead <> actualHead then
            Error(AppendRejection.StaleHead(expectedHead, actualHead))
        else
            appendAt s (n, actualHead) actor op state records
            |> Result.mapError AppendRejection.Domain

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
        (s: Stream<'Op, 'State, 'Rej>)
        (expectedHead: string)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, AppendRejection<'Rej>> =
        appendIfCore s expectedHead actor op state records
        |> Result.map (fun (state', records', _) -> state', records')

    // ---- idempotent append (Phase 82) ----

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
        (s: Stream<'Op, 'State, 'Rej>)
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
            appendAt s (tip s.Config records) actor op state records
            |> Result.map (fun (state', records', entry) ->
                AppendOutcome.Appended(state', records', KeyIndex.add key entry index))

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
        (s: Stream<'Op, 'State, 'Rej>)
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
            appendIfCore s expectedHead actor op state records
            |> Result.map (fun (state', records', entry) ->
                AppendOutcome.Appended(state', records', KeyIndex.add key entry index))
