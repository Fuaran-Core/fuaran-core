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
