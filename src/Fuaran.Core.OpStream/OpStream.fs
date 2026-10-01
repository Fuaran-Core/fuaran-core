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

/// The spine's JSON string escape as THIS package carries it (Phase 287). A DELIBERATE COPY of
/// `Wire.Json.escape`: `Fuaran.Core.OpStream` is standalone by design and takes no `Wire`
/// dependency (DECISIONS.md D2), so the rule is copied here exactly as `Hash.fnv1a` is copied
/// into `OpStream` below, and for the same reason it is held VALUE-IDENTICAL rather than trusted —
/// `StringEscapeVectors` in the conformance kit compares every byte this module emits for a
/// control character against `Wire.Json.escape`'s, so a copy that drifts is caught rather than
/// discovered as a chain that verifies on one host and not another.
///
/// The rule: exactly three classes are escaped and nothing else — `"` as `\"`, `\` as `\\`, and
/// every control character `U+0000`–`U+001F` as `\u00xx` with LOWER-CASE hex. `\n`, `\r` and `\t`
/// have NO short form; that is what the UI host's `CanonicalJson.appendRawString` and the
/// TypeScript twin already write, and what made their chain hashes disagree with this package's
/// before Phase 287. Fable-clean (no `System.Text.Json` on the encode path).
///
/// `quoteLegacy` is the spelling this package wrote BEFORE Phase 287 — the three short forms, and
/// `\u00xx` only for the other control characters. It exists so a chain hashed under the old bytes
/// can still be verified and rehashed (`OpStream.legacyEscapeConfig`, `OpStream.legacyActorConfig`);
/// nothing writes it.
module internal JsonString =

    /// `"` + the escaped body + `"` — the canonical spelling of `s` as a JSON string literal.
    let quote (s: string) : string =
        let sb = System.Text.StringBuilder()
        sb.Append('"') |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | c when int c < 0x20 -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append(c) |> ignore

        sb.Append('"') |> ignore
        sb.ToString()

    /// The pre-Phase-287 spelling: `\n` / `\r` / `\t` short, every other control character
    /// `\u00xx`. Verification and migration only.
    let quoteLegacy (s: string) : string =
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

/// Companion helpers for `Actor` — the canonical hash pre-image (`encode`), the stable id
/// projection, and the pre-Phase-320 migration lift.
[<RequireQualifiedAccess>]
module Actor =

    /// `encode` under an explicit string quoter — the one shape both the canonical pre-image and
    /// the pre-287 legacy payload are built from, so the two can differ ONLY in how a string is
    /// spelled. Internal: the quoter is not a choice a consumer makes.
    let internal encodeWith (quote: string -> string) (a: Actor) : string =
        match a with
        | Human id -> "{\"kind\":\"human\",\"id\":" + quote id + "}"
        | Agent(model, version, id) ->
            "{\"kind\":\"agent\",\"model\":"
            + quote model
            + ",\"version\":"
            + quote version
            + ",\"id\":"
            + quote id
            + "}"

    /// The canonical JSON object the chain hash folds over. Field order is fixed (`kind` first,
    /// then the case fields in declaration order) so the pre-image is stable across hosts:
    ///   `Human`  → `{"kind":"human","id":<id>}`
    ///   `Agent`  → `{"kind":"agent","model":<model>,"version":<version>,"id":<id>}`
    /// Strings are spelled by `JsonString.quote` — every control character as `\u00xx` (Phase 287),
    /// so `Agent("m\n", "1", "id")` encodes to `{"kind":"agent","model":"m\u000a",…}` on every host.
    let encode (a: Actor) : string = encodeWith JsonString.quote a

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

/// How a snapshot's hash binds its checkpoint (Phase 258, carried as a value since Phase 296).
/// `Strict` folds the canonical encoding of the `'State` into the hash, so a swapped state is caught;
/// `ChainOnly` binds the prefix link and the boundary position only, and the stored `'State` is
/// trusted — the trade-off a domain whose state has no canonical encoder chooses. The mode travels ON
/// the snapshot, so a reader verifies it the way it was taken rather than re-reading the line to
/// guess.
[<RequireQualifiedAccess>]
type SnapshotMode =
    | Strict
    | ChainOnly

/// A checkpoint of the folded `'State` at a sequence boundary (Phase 244). `PrevHash` is
/// the boundary record's hash (the config's genesis at sequence zero); `Hash` chains the snapshot
/// into the stream so the checkpoint and the truncated prefix are tamper-evident. Replay can resume
/// from a snapshot instead of from the origin — bounded replay for unbounded histories (FGP 5).
/// `Mode` (Phase 296) is how `Hash` was computed, and so how the snapshot verifies.
type Snapshot<'State> =
    { Seq: int
      State: 'State
      PrevHash: string
      Hash: string
      Mode: SnapshotMode }

/// Why a snapshot could not be taken, or its tail not replayed (Phase 296) — the typed faults the
/// snapshot family reports where its string-returning forms used to discard them.
[<RequireQualifiedAccess>]
type SnapshotFault<'Rej> =
    /// The boundary is outside `0 .. length`.
    | SeqOutOfRange of atSeq: int * length: int
    /// The prefix below the boundary does not replay: the index of the op the domain rejected, and
    /// the rejection.
    | PrefixRejected of index: int * reject: 'Rej
    /// The tail does not start at the snapshot's boundary: the tail's first record carries `got`
    /// where the snapshot's `Seq` says `expected`.
    | TailSeqMismatch of expected: int * got: int
    /// A tail op the domain rejected, by its index in the tail, and the rejection.
    | TailRejected of index: int * reject: 'Rej

/// Where a snapshot boundary failed to verify (Phase 296): the snapshot's own hash, or the first
/// break in the tail chain it anchors (indexed by position in the tail).
[<RequireQualifiedAccess>]
type SnapshotBreak =
    | SnapshotHash of expected: string * got: string
    | Tail of ChainBreak

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

/// WHY a JSONL line was refused (Phase 296) — the closed set of faults the one line scanner
/// (`OpStream.Jsonl`) and the readers built on it report. Before this type the scanner was lenient
/// where `Wire.Json.parse` refuses: an unquoted value read as a string with its first and last
/// characters cut off (`"prevHash":null` read as `"ul"`), a non-hex `\u` digit went through
/// arithmetic, and a line ending after `:` indexed past the end. Each is now a named refusal, so a
/// stream that reads `Ok` is one the scanner actually parsed.
[<RequireQualifiedAccess>]
type JsonlFaultReason =
    /// The line's first non-blank character is not `{` — the line is not a JSON object.
    | NotAnObject
    /// The line ends before its object does — after a `:`, after a `,`, or before the closing `}`.
    | Truncated
    /// A member key is not a string token.
    | ExpectedKey
    /// A member key is not followed by `:`.
    | ExpectedColon
    /// A member has no value (`"a":}` or `"a":,`).
    | MissingValue
    /// A member value is not followed by `,` or `}`.
    | ExpectedCommaOrBrace
    /// Non-blank content follows the object's closing `}`.
    | TrailingContent
    /// A string token is not closed before the line ends.
    | UnterminatedString
    /// An array or object value is not closed before the line ends.
    | UnterminatedContainer
    /// An escape the JSON grammar does not have: an unknown escape letter, a `\u` whose four digits
    /// are not all hex, or a surrogate escape that is not half of a high-low pair. The escape as read.
    | InvalidEscape of escape: string
    /// A bare value that is not `true`, `false`, `null` or a JSON number. The token as read.
    | InvalidLiteral of token: string
    /// A member the reader requires to be a string holds another kind of value (`"prevHash":null`,
    /// `"id":12`).
    | ExpectedString of field: string
    /// A member the reader requires to be an integer holds something else — a string, a fraction, an
    /// exponent, a hex spelling (`"seq":0x2`), or a value outside the 32-bit range.
    | ExpectedInteger of field: string
    /// A member the reader requires to be an array of strings holds something else.
    | ExpectedStringArray of field: string
    /// A member the reader requires is absent.
    | MissingField of field: string
    /// A snapshot line that is not the first line of the stream — one snapshot line, at the head, is
    /// the only compacted shape `compact` writes.
    | SnapshotNotAtHead
    /// The line is well-formed JSON and its reader refused what it says — a witness decode `Error`,
    /// an actor kind this build does not know, a node colliding with one already read. The reader's
    /// reason, verbatim.
    | Refused of reason: string

/// One refused JSONL line (Phase 296): the 1-based `Line` number counted over EVERY line of the
/// text (blank lines included, so it is the number an editor shows), the scanner's own 0-based
/// `Position` within that line, and the `Reason`.
type JsonlFault =
    { Line: int
      Position: int
      Reason: JsonlFaultReason }

/// Render a `JsonlFault` for a log line or an `Error` string (Phase 296).
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module JsonlFault =

    /// The reason's text, without the line and position.
    let reasonText (r: JsonlFaultReason) : string =
        match r with
        | JsonlFaultReason.NotAnObject -> "expected a JSON object"
        | JsonlFaultReason.Truncated -> "the line ends inside the object"
        | JsonlFaultReason.ExpectedKey -> "expected a member key (a string)"
        | JsonlFaultReason.ExpectedColon -> "expected ':'"
        | JsonlFaultReason.MissingValue -> "a member has no value"
        | JsonlFaultReason.ExpectedCommaOrBrace -> "expected ',' or '}'"
        | JsonlFaultReason.TrailingContent -> "content after the closing '}'"
        | JsonlFaultReason.UnterminatedString -> "unterminated string"
        | JsonlFaultReason.UnterminatedContainer -> "unterminated container"
        | JsonlFaultReason.InvalidEscape e -> "invalid escape " + e
        | JsonlFaultReason.InvalidLiteral t -> "invalid literal '" + t + "'"
        | JsonlFaultReason.ExpectedString f -> "field " + f + " is not a string"
        | JsonlFaultReason.ExpectedInteger f -> "field " + f + " is not an integer"
        | JsonlFaultReason.ExpectedStringArray f -> "field " + f + " is not an array of strings"
        | JsonlFaultReason.MissingField f -> "missing field " + f
        | JsonlFaultReason.SnapshotNotAtHead -> "a snapshot line that is not the first line of the stream"
        | JsonlFaultReason.Refused reason -> reason

    /// `line N: <reason> (position P)` — the `line N:` prefix every JSONL reader's `Error` string
    /// has carried since Phase 252, now 1-based over all lines.
    let toString (f: JsonlFault) : string =
        sprintf "line %d: %s (position %d)" f.Line (reasonText f.Reason) f.Position

/// The scanner's internal refusal, raised deep in the scan and caught at the one boundary that turns
/// it into a `JsonlFault` — never seen by a caller.
exception internal JsonlScanFault of position: int * reason: JsonlFaultReason

/// One scanned JSONL line (Phase 296): its 1-based number, its text, and its top-level members as
/// raw spans (first-wins on a repeated key). Read it through the `OpStream.Jsonl` accessors.
type JsonlLine =
    internal
        { Number: int
          Text: string
          Fields: (string * string * int) list }

/// Append-only hash-chained op stream + deterministic replay + JSONL persistence,
/// generic over the `StreamWitness`. The highest-genericity core layer.
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

    /// The default portable hash: FNV-1a over `prevHash | payload`. **Portable is meant literally** —
    /// value-identical on .NET and under Fable, so a browser replaying a chain a server wrote
    /// computes the same hashes. That was not true before `0.6.0`; see the copy note above.
    let defaultHash: HashFn = fun prev payload -> fnv1a (prev + "|" + payload)

    /// JSON string spelling for every line, snapshot, capture and envelope this module emits —
    /// the ONE escaper this package carries (`JsonString.quote`, the D2 copy of `Wire.Json.escape`,
    /// every control character as `\u00xx` since Phase 287). Fable-clean.
    let private jstr (s: string) : string = JsonString.quote s

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

    /// The canonical payload binding — the `{seq,actor,op}` envelope + `""` genesis. The default
    /// for every `append` / `verifyChain` call.
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

    /// The pre-Phase-287 canonical config (typed actor with short control escapes + `""` genesis).
    /// The `fromCfg` for a string-escaping migration `rehash`. See `legacyEscapePayload`.
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

    /// The pre-Phase-320 canonical config (bare-string actor + `""` genesis). The `fromCfg` for a
    /// typed-actor migration `rehash`. See `legacyActorPayload`.
    let legacyActorConfig: StreamConfig =
        { Payload = legacyActorPayload
          Genesis = "" }

    let empty: OpRecord<'Op> list = []

    /// The stream's length and last hash in ONE walk (Phase 296). `append` used to walk the list three
    /// times per call (`List.length`, `List.tryLast`, then the copy `@` makes) and `appendIf` four.
    let private tip (records: OpRecord<'Op> list) : int * string option =
        let rec go n (last: string option) =
            function
            | [] -> n, last
            | (r: OpRecord<'Op>) :: rest -> go (n + 1) (Some r.Hash) rest

        go 0 None records

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
                    let h = hashFn prev (cfg.Payload seq actor (w.Encode op))

                    let r =
                        { Seq = seq
                          Actor = actor
                          Op = op
                          PrevHash = prev
                          Hash = h }

                    go (i + 1) h st' (r :: acc) rest

        go 0 prev0 state [] ops

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
        let n, last = tip records

        match chainOps cfg hashFn w actor n (defaultArg last cfg.Genesis) [ op ] state with
        | Ok(state', added) -> Ok(state', records @ added)
        | Error(_, e) -> Error e

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
        let n, last = tip records

        chainOps cfg hashFn w actor n (defaultArg last cfg.Genesis) ops state
        |> Result.map (fun (state', added) -> state', records @ added)

    /// `appendManyWith` under the canonical config (Phase 296) — the batch form of `append`.
    let appendMany
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (ops: 'Op list)
        (state: 'State)
        (records: OpRecord<'Op> list)
        : Result<'State * OpRecord<'Op> list, int * 'Rej> =
        appendManyWith canonicalConfig hashFn w actor ops state records

    /// THE chain walker (Phase 296) — the one loop `firstChainBreakWith`, `rehash`, the snapshot
    /// boundary verifier and `firstCaptureBreak` share, where four copies were written. Walks `items`
    /// from sequence `seq0` and prev-link `genesis` and returns the first item whose sequence,
    /// prev-link, or hash fails, `Index` being its position in `items`. The digest is computed only
    /// after the cheap sequence and link checks pass.
    let private walkChain
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
    let private walkRecords
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

    /// `firstChainBreak` under an explicit `StreamConfig` (Phase 21 + Phase 255) — the localising
    /// verifier. Walks the chain and returns the first record whose sequence, prev-link, or hash
    /// fails under `cfg`; `None` for an intact chain.
    let firstChainBreakWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : ChainBreak option =
        walkRecords cfg hashFn w cfg.Genesis 0 records

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

    /// `tryRehash` with the break rendered (Phase 255) — the string form, kept; the message now names
    /// the record and the reason the source chain failed at.
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

        let inline private isWs (c: char) =
            c = ' ' || c = '\t' || c = '\n' || c = '\r'

        let private fail (pos: int) (reason: JsonlFaultReason) : 'a = raise (JsonlScanFault(pos, reason))

        let inline private isHex (c: char) =
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')

        let inline private hexVal (c: char) =
            if c <= '9' then int c - int '0'
            elif c >= 'a' then int c - int 'a' + 10
            else int c - int 'A' + 10

        /// The code unit of the `\uXXXX` escape whose backslash is at `i`, or `-1` when fewer than
        /// four hex digits follow the `u`.
        let private unicodeAt (s: string) (i: int) : int =
            if
                i + 5 < s.Length
                && s.[i + 1] = 'u'
                && isHex s.[i + 2]
                && isHex s.[i + 3]
                && isHex s.[i + 4]
                && isHex s.[i + 5]
            then
                (hexVal s.[i + 2] <<< 12)
                + (hexVal s.[i + 3] <<< 8)
                + (hexVal s.[i + 4] <<< 4)
                + hexVal s.[i + 5]
            else
                -1

        let private escapeText (s: string) (i: int) (len: int) = s.Substring(i, min len (s.Length - i))

        /// Index just past the string token whose opening quote is at `start`, every escape held to
        /// the JSON grammar: the eight single-letter escapes, and `\uXXXX` with four hex digits, a
        /// high surrogate followed at once by an escaped low one.
        let internal skipString (s: string) (start: int) : int =
            let n = s.Length
            let mutable i = start + 1
            let mutable fin = false

            while not fin do
                if i >= n then
                    fail start JsonlFaultReason.UnterminatedString

                match s.[i] with
                | '"' ->
                    i <- i + 1
                    fin <- true
                | '\\' ->
                    if i + 1 >= n then
                        fail start JsonlFaultReason.UnterminatedString

                    match s.[i + 1] with
                    | '"'
                    | '\\'
                    | '/'
                    | 'b'
                    | 'f'
                    | 'n'
                    | 'r'
                    | 't' -> i <- i + 2
                    | 'u' ->
                        let code = unicodeAt s i

                        if code < 0 then
                            fail i (JsonlFaultReason.InvalidEscape(escapeText s i 6))
                        elif code >= 0xD800 && code <= 0xDBFF then
                            let low =
                                if i + 6 < n && s.[i + 6] = '\\' then
                                    unicodeAt s (i + 6)
                                else
                                    -1

                            if low >= 0xDC00 && low <= 0xDFFF then
                                i <- i + 12
                            else
                                fail i (JsonlFaultReason.InvalidEscape(escapeText s i 12))
                        elif code >= 0xDC00 && code <= 0xDFFF then
                            fail i (JsonlFaultReason.InvalidEscape(escapeText s i 6))
                        else
                            i <- i + 6
                    | c -> fail i (JsonlFaultReason.InvalidEscape("\\" + string c))
                | _ -> i <- i + 1

            i

        /// Decode a string token `skipString` has already accepted (quotes included).
        let private decodeString (token: string) : string =
            let sb = System.Text.StringBuilder()
            let last = token.Length - 1
            let mutable i = 1

            while i < last do
                let c = token.[i]

                if c = '\\' then
                    match token.[i + 1] with
                    | 'u' ->
                        sb.Append(char (unicodeAt token i)) |> ignore
                        i <- i + 6
                    | e ->
                        (match e with
                         | 'b' -> sb.Append('\b')
                         | 'f' -> sb.Append('\f')
                         | 'n' -> sb.Append('\n')
                         | 'r' -> sb.Append('\r')
                         | 't' -> sb.Append('\t')
                         | other -> sb.Append(other))
                        |> ignore

                        i <- i + 2
                else
                    sb.Append(c) |> ignore
                    i <- i + 1

            sb.ToString()

        /// A bare value's token held to the literal grammar: `true`, `false`, `null`, or a JSON number
        /// (`-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?`).
        let private isLiteral (t: string) : bool =
            if t = "true" || t = "false" || t = "null" then
                true
            else
                let n = t.Length
                let mutable i = 0
                let digit k = k < n && t.[k] >= '0' && t.[k] <= '9'

                if i < n && t.[i] = '-' then
                    i <- i + 1

                let intStart = i

                if i < n && t.[i] = '0' then
                    i <- i + 1
                else
                    while digit i do
                        i <- i + 1

                let mutable ok = i > intStart

                if ok && i < n && t.[i] = '.' then
                    i <- i + 1
                    let fracStart = i

                    while digit i do
                        i <- i + 1

                    ok <- i > fracStart

                if ok && i < n && (t.[i] = 'e' || t.[i] = 'E') then
                    i <- i + 1

                    if i < n && (t.[i] = '+' || t.[i] = '-') then
                        i <- i + 1

                    let expStart = i

                    while digit i do
                        i <- i + 1

                    ok <- i > expStart

                ok && i = n

        /// Index just past the JSON value starting at `start` (no leading whitespace).
        let internal skipValue (s: string) (start: int) : int =
            let n = s.Length

            if start >= n then
                fail start JsonlFaultReason.Truncated

            match s.[start] with
            | '"' -> skipString s start
            | '{'
            | '[' ->
                let mutable i = start + 1
                let mutable depth = 1

                while depth > 0 do
                    if i >= n then
                        fail start JsonlFaultReason.UnterminatedContainer

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
            | ','
            | '}'
            | ']' -> fail start JsonlFaultReason.MissingValue
            | _ ->
                let mutable i = start

                while i < n && not (let c = s.[i] in c = ',' || c = '}' || c = ']' || isWs c) do
                    i <- i + 1

                let token = s.Substring(start, i - start)

                if not (isLiteral token) then
                    fail start (JsonlFaultReason.InvalidLiteral token)

                i

        /// The members of the flat object `s` holds — `(key, raw value, value position)`, first-wins
        /// on a repeated key (Phase 45: the first-wins `JVal` decoders and this scanner agree on which
        /// value a repeated key resolves to). Positions are offsets into `s` plus `offset`.
        let private membersOf (offset: int) (s: string) : (string * string * int) list =
            let n = s.Length
            let mutable i = 0

            let skipWs () =
                while i < n && isWs s.[i] do
                    i <- i + 1

            let at k = offset + k
            skipWs ()

            if i >= n || s.[i] <> '{' then
                fail (at i) JsonlFaultReason.NotAnObject

            i <- i + 1
            skipWs ()
            let fields = ResizeArray<string * string * int>()

            if i >= n then
                fail (at i) JsonlFaultReason.Truncated

            if s.[i] = '}' then
                i <- i + 1
            else
                let mutable go = true

                while go do
                    skipWs ()

                    if i >= n then
                        fail (at i) JsonlFaultReason.Truncated

                    if s.[i] <> '"' then
                        fail (at i) JsonlFaultReason.ExpectedKey

                    let ks =
                        try
                            skipString s i
                        with JsonlScanFault(p, r) ->
                            fail (at p) r

                    let key = decodeString (s.Substring(i, ks - i))
                    i <- ks
                    skipWs ()

                    if i >= n then
                        fail (at i) JsonlFaultReason.Truncated

                    if s.[i] <> ':' then
                        fail (at i) JsonlFaultReason.ExpectedColon

                    i <- i + 1
                    skipWs ()

                    if i >= n then
                        fail (at i) JsonlFaultReason.Truncated

                    let vs =
                        try
                            skipValue s i
                        with JsonlScanFault(p, r) ->
                            fail (at p) r

                    fields.Add((key, s.Substring(i, vs - i), at i))
                    i <- vs
                    skipWs ()

                    if i >= n then
                        fail (at i) JsonlFaultReason.Truncated

                    if s.[i] = ',' then
                        i <- i + 1
                    elif s.[i] = '}' then
                        i <- i + 1
                        go <- false
                    else
                        fail (at i) JsonlFaultReason.ExpectedCommaOrBrace

            skipWs ()

            if i < n then
                fail (at i) JsonlFaultReason.TrailingContent

            let seen = System.Collections.Generic.HashSet<string>()

            [ for (k, v, p) in fields do
                  if seen.Add k then
                      yield (k, v, p) ]

        let private faultAt (line: int) (pos: int) (reason: JsonlFaultReason) : JsonlFault =
            { Line = line
              Position = pos
              Reason = reason }

        /// Scan one line into a `JsonlLine` carrying its 1-based `number`.
        let parseLine (number: int) (text: string) : Result<JsonlLine, JsonlFault> =
            try
                Ok
                    { Number = number
                      Text = text
                      Fields = membersOf 0 text }
            with JsonlScanFault(p, r) ->
                Error(faultAt number p r)

        /// The members of one flat JSON object, each value as its raw span byte-for-byte (the
        /// opaque canonical payload a consumer embeds keeps its bytes), first-wins on a repeated key.
        /// A refusal is numbered line 1.
        let topFields (line: string) : Result<(string * string) list, JsonlFault> =
            parseLine 1 line
            |> Result.map (fun l -> l.Fields |> List.map (fun (k, v, _) -> k, v))

        /// The raw span of ONE top-level member of a flat JSON object — `None` when the object has no
        /// such member — after the whole line has been scanned (a malformed line is refused even when
        /// the member itself is intact).
        let rawSpan (field: string) (line: string) : Result<string option, JsonlFault> =
            parseLine 1 line
            |> Result.map (fun l -> l.Fields |> List.tryPick (fun (k, v, _) -> if k = field then Some v else None))

        /// Unescape a string token (surrounding quotes included). Total: a raw span that is not a
        /// well-formed JSON string is an `Error`, never a truncated read — `null` is not the string
        /// `"ul"`.
        let unquote (raw: string) : Result<string, JsonlFaultReason> =
            if raw.Length < 2 || raw.[0] <> '"' then
                Error(JsonlFaultReason.ExpectedString "")
            else
                try
                    if skipString raw 0 = raw.Length then
                        Ok(decodeString raw)
                    else
                        Error(JsonlFaultReason.ExpectedString "")
                with JsonlScanFault(_, r) ->
                    Error r

        /// The line's 1-based number.
        let lineNumber (line: JsonlLine) : int = line.Number

        /// The line's text, verbatim.
        let lineText (line: JsonlLine) : string = line.Text

        /// A reader's own refusal of a well-formed line — `JsonlFaultReason.Refused` at the line's
        /// start.
        let refuse (line: JsonlLine) (reason: string) : JsonlFault =
            faultAt line.Number 0 (JsonlFaultReason.Refused reason)

        let private memberOf (key: string) (line: JsonlLine) : (string * int) option =
            line.Fields
            |> List.tryPick (fun (k, v, p) -> if k = key then Some(v, p) else None)

        /// The raw span of a member, or `None` when the line has none.
        let tryRawField (key: string) (line: JsonlLine) : string option = memberOf key line |> Option.map fst

        /// The raw span of a required member.
        let rawField (key: string) (line: JsonlLine) : Result<string, JsonlFault> =
            match memberOf key line with
            | Some(v, _) -> Ok v
            | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))

        /// A required member that must be a JSON string, unescaped.
        let stringField (key: string) (line: JsonlLine) : Result<string, JsonlFault> =
            match memberOf key line with
            | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))
            | Some(v, p) ->
                match unquote v with
                | Ok s -> Ok s
                | Error(JsonlFaultReason.ExpectedString _) ->
                    Error(faultAt line.Number p (JsonlFaultReason.ExpectedString key))
                | Error r -> Error(faultAt line.Number p r)

        /// A required member that must be an integer under the JSON grammar
        /// (`-?(0|[1-9][0-9]*)`, within the 32-bit range) — not a string, a fraction, an exponent or
        /// a hex spelling.
        let intField (key: string) (line: JsonlLine) : Result<int, JsonlFault> =
            match memberOf key line with
            | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))
            | Some(v, p) ->
                let digits = if v.StartsWith "-" then v.Substring 1 else v

                let grammatical =
                    digits.Length > 0
                    && digits.Length <= 10
                    && Seq.forall (fun c -> c >= '0' && c <= '9') digits
                    && (digits = "0" || digits.[0] <> '0')

                let value = if grammatical then System.Int64.Parse v else 0L

                if
                    grammatical
                    && value >= int64 System.Int32.MinValue
                    && value <= int64 System.Int32.MaxValue
                then
                    Ok(int value)
                else
                    Error(faultAt line.Number p (JsonlFaultReason.ExpectedInteger key))

        /// A required member that must be an array of JSON strings (`[]` included), unescaped.
        let stringsField (key: string) (line: JsonlLine) : Result<string list, JsonlFault> =
            match memberOf key line with
            | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))
            | Some(v, p) ->
                let bad () =
                    Error(faultAt line.Number p (JsonlFaultReason.ExpectedStringArray key))

                let n = v.Length

                if n < 2 || v.[0] <> '[' || v.[n - 1] <> ']' then
                    bad ()
                else
                    let items = ResizeArray<string>()
                    let mutable i = 1
                    let mutable ok = true
                    let mutable expectItem = true

                    let skipWs () =
                        while i < n - 1 && isWs v.[i] do
                            i <- i + 1

                    skipWs ()

                    if i = n - 1 then
                        Ok []
                    else
                        while ok && i < n - 1 do
                            skipWs ()

                            if expectItem then
                                if i < n - 1 && v.[i] = '"' then
                                    let e =
                                        try
                                            skipString v i
                                        with JsonlScanFault _ ->
                                            n

                                    items.Add(decodeString (v.Substring(i, e - i)))
                                    i <- e
                                    expectItem <- false
                                else
                                    ok <- false
                            elif v.[i] = ',' then
                                i <- i + 1
                                expectItem <- true
                            else
                                ok <- false

                            skipWs ()

                        if ok && not expectItem then
                            Ok(List.ofSeq items)
                        else
                            bad ()

        /// A required member holding the typed `Actor` object (Phase 320) —
        /// `{"kind":"human","id":…}` or `{"kind":"agent","model":…,"version":…,"id":…}`, every member
        /// present and a string. An absent or unknown `kind` is a refusal, never `Human`: a store
        /// written by a newer build may carry a kind this reader does not know, and reading it as a
        /// person would attribute the op to the wrong kind of actor (Phase 260).
        let actorField (key: string) (line: JsonlLine) : Result<Actor, JsonlFault> =
            match memberOf key line with
            | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))
            | Some(v, p) ->
                try
                    let inner =
                        { Number = line.Number
                          Text = v
                          Fields = membersOf p v }

                    let str k =
                        match stringField k inner with
                        | Ok s -> s
                        | Error f ->
                            let reason =
                                match f.Reason with
                                | JsonlFaultReason.MissingField m -> JsonlFaultReason.MissingField(key + "." + m)
                                | JsonlFaultReason.ExpectedString m -> JsonlFaultReason.ExpectedString(key + "." + m)
                                | r -> r

                            fail f.Position reason

                    match tryRawField "kind" inner with
                    | None -> Error(refuse line "the actor carries no kind")
                    | Some _ ->
                        match str "kind" with
                        | "human" -> Ok(Human(str "id"))
                        | "agent" -> Ok(Agent(str "model", str "version", str "id"))
                        | kind -> Error(refuse line (sprintf "unknown actor kind \"%s\"" kind))
                with JsonlScanFault(pos, r) ->
                    Error(faultAt line.Number pos r)

        /// Scan a JSONL text line by line — the one record loop every reader shares. Lines are
        /// numbered from 1 over ALL lines (blank lines counted, then skipped), `\r\n` read as `\n`;
        /// each non-blank line is scanned and handed to `decode`, and the first refusal — the
        /// scanner's or the decoder's — is the result.
        let scanRecords (decode: JsonlLine -> Result<'T, JsonlFault>) (text: string) : Result<'T list, JsonlFault> =
            let lines = text.Replace("\r\n", "\n").Split('\n')
            let acc = ResizeArray<'T>()
            let mutable fault = None
            let mutable k = 0

            while fault.IsNone && k < lines.Length do
                let text = lines.[k]

                if text.Trim() <> "" then
                    match parseLine (k + 1) text |> Result.bind decode with
                    | Ok v -> acc.Add v
                    | Error f -> fault <- Some f

                k <- k + 1

            match fault with
            | Some f -> Error f
            | None -> Ok(List.ofSeq acc)

    let inline private bindR ([<InlineIfLambda>] f: 'a -> Result<'b, 'e>) (r: Result<'a, 'e>) = Result.bind f r

    /// A single-object reader's refusal (a snapshot line, an attribution envelope) — the reason and
    /// the position, without a line number the caller never had.
    let private spanFault (f: JsonlFault) : string =
        sprintf "%s (position %d)" (JsonlFault.reasonText f.Reason) f.Position

    /// One record line, decoded — the members in line order, the `op` span handed to the witness.
    let private recordOf
        (actorOf: JsonlLine -> Result<Actor, JsonlFault>)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (line: JsonlLine)
        : Result<OpRecord<'Op>, JsonlFault> =
        Jsonl.intField "seq" line
        |> bindR (fun seq ->
            actorOf line
            |> bindR (fun actor ->
                Jsonl.rawField "op" line
                |> bindR (fun raw -> w.Decode raw |> Result.mapError (Jsonl.refuse line))
                |> bindR (fun op ->
                    Jsonl.stringField "prevHash" line
                    |> bindR (fun prevHash ->
                        Jsonl.stringField "hash" line
                        |> Result.map (fun hash ->
                            { Seq = seq
                              Actor = actor
                              Op = op
                              PrevHash = prevHash
                              Hash = hash })))))

    /// Is this line a snapshot line? One carrying `"snapshot":true` and NO `op` — a record line that
    /// happens to carry a `snapshot` member is a record (Phase 296), not a snapshot dropped on the
    /// floor.
    let private isSnapshotLine (line: JsonlLine) : bool =
        Option.isNone (Jsonl.tryRawField "op" line)
        && Jsonl.tryRawField "snapshot" line = Some "true"

    /// The records-and-snapshot reader, parameterised on how the `actor` member decodes — the
    /// canonical typed object, or the legacy bare string. One snapshot line is admitted, and only as
    /// the first line of the stream (Phase 296); a second, or one after a record, is refused.
    let private scanJsonlWithSnapshots
        (actorOf: JsonlLine -> Result<Actor, JsonlFault>)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list * string list, JsonlFault> =
        text
        |> Jsonl.scanRecords (fun line ->
            if isSnapshotLine line then
                Ok(Choice2Of2 line)
            else
                recordOf actorOf w line |> Result.map Choice1Of2)
        |> bindR (fun items ->
            let rec go (first: bool) recs snaps =
                function
                | [] -> Ok(List.rev recs, List.rev snaps)
                | Choice1Of2 r :: rest -> go false (r :: recs) snaps rest
                | Choice2Of2(l: JsonlLine) :: rest ->
                    if first then
                        go false recs [ Jsonl.lineText l ] rest
                    else
                        Error
                            { Line = Jsonl.lineNumber l
                              Position = 0
                              Reason = JsonlFaultReason.SnapshotNotAtHead }

            go true [] [] items)

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
        scanJsonlWithSnapshots (Jsonl.actorField "actor") w text
        |> Result.mapError JsonlFault.toString

    /// Parse JSONL back into records, **dropping the snapshot line** (Phase 244) — correct for a
    /// linear, never-compacted stream. A thin wrapper over `fromJsonlWithSnapshots` (the one scanner);
    /// a `compact` output (snapshot + tail) read this way loses its base state with no signal and
    /// replaying from origin is then wrong, so read a possibly-compacted file with
    /// `fromJsonlWithSnapshots` instead. Refuses exactly what that reader refuses; the `op` raw span
    /// round-trips.
    let fromJsonl (w: StreamWitness<'Op, 'State, 'Rej>) (text: string) : Result<OpRecord<'Op> list, string> =
        fromJsonlWithSnapshots w text |> Result.map fst

    /// Read a **pre-Phase-320** JSONL file (Phase 320 migration) — the `actor` member is still a bare
    /// JSON string, which this lifts to the typed `Human` case. The returned records carry the file's
    /// stored `PrevHash` / `Hash` (computed under the old bare-string payload), so they
    /// `verifyChainWith legacyActorConfig` and then `rehash legacyActorConfig canonicalConfig` to the
    /// new typed form. The snapshot line is dropped. Refuses what `fromJsonl` refuses.
    let fromJsonlLegacyActor (w: StreamWitness<'Op, 'State, 'Rej>) (text: string) : Result<OpRecord<'Op> list, string> =
        scanJsonlWithSnapshots (fun line -> Jsonl.stringField "actor" line |> Result.map Actor.ofLegacyString) w text
        |> Result.map fst
        |> Result.mapError JsonlFault.toString

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
            | Some b ->
                Error(
                    sprintf
                        "OpStream.fromJsonlVerified: chain breaks at record %d — %s"
                        b.Index
                        (ChainBreakReason.toString b.Reason)
                ))

    // ---- snapshot / compaction (Phase 244; one family since Phase 296) ----

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
    /// Phase 288's checkpoint builds on this family: one pre-image per mode (`payload` below), one
    /// verifier, one line format.
    module Snapshots =

        /// The hash pre-image binding a snapshot to its boundary, by its mode. `Strict` folds the
        /// state in; `ChainOnly` carries the `stateHashed` discriminator instead, so a chain-only
        /// snapshot can never collide with a strict one at the same boundary. Byte-identical to the
        /// two pre-images the matrix computed.
        let private payload (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
            match snap.Mode with
            | SnapshotMode.Strict ->
                "{\"snapshot\":true,\"seq\":"
                + string snap.Seq
                + ",\"state\":"
                + stateEncode snap.State
                + "}"
            | SnapshotMode.ChainOnly -> "{\"snapshot\":true,\"seq\":" + string snap.Seq + ",\"stateHashed\":false}"

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
            let n = List.length records

            if atSeq < 0 || atSeq > n then
                Error(SnapshotFault.SeqOutOfRange(atSeq, n))
            else
                match replay w state0 (List.truncate atSeq records) with
                | Error(i, e) -> Error(SnapshotFault.PrefixRejected(i, e))
                | Ok state ->
                    let prevHash =
                        if atSeq = 0 then
                            cfg.Genesis
                        else
                            (List.item (atSeq - 1) records).Hash

                    let snap0 =
                        { Seq = atSeq
                          State = state
                          PrevHash = prevHash
                          Hash = ""
                          Mode = mode }

                    Ok
                        { snap0 with
                            Hash = hashFn prevHash (payload stateEncode snap0) }

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
            take mode cfg hashFn stateEncode w state0 records atSeq
            |> Result.map (fun snap -> snap, List.skip atSeq records)

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
            let expected = hashFn snap.PrevHash (payload stateEncode snap)

            if snap.Hash <> expected then
                Some(SnapshotBreak.SnapshotHash(expected, snap.Hash))
            else
                walkRecords cfg hashFn w snap.PrevHash snap.Seq tail
                |> Option.map SnapshotBreak.Tail

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
            firstBreak cfg hashFn stateEncode w snap tail |> Option.isNone

        /// Bounded replay from a snapshot (Phase 296 checks the seam): the tail must start at the
        /// snapshot's boundary — its first record's `Seq` equal to the snapshot's `Seq` — or the
        /// replay would fold ops from the wrong position onto the checkpoint; then `Apply` is folded
        /// over the tail from `snap.State`. The mode does not reach replay.
        let replayFrom
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (snap: Snapshot<'State>)
            (tail: OpRecord<'Op> list)
            : Result<'State, SnapshotFault<'Rej>> =
            match tail with
            | (r: OpRecord<'Op>) :: _ when r.Seq <> snap.Seq -> Error(SnapshotFault.TailSeqMismatch(snap.Seq, r.Seq))
            | _ ->
                match replay w snap.State tail with
                | Ok st -> Ok st
                | Error(i, e) -> Error(SnapshotFault.TailRejected(i, e))

        /// One snapshot line, by the snapshot's mode: a `Strict` line carries no `stateHashed` member
        /// (byte-identical to the pre-Phase-258 format); a `ChainOnly` line carries
        /// `"stateHashed":false`. The `state` member is `stateEncode`'s output embedded raw.
        let toJsonl (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
            "{\"snapshot\":true,\"seq\":"
            + string snap.Seq
            + ",\"state\":"
            + stateEncode snap.State
            + (match snap.Mode with
               | SnapshotMode.Strict -> ""
               | SnapshotMode.ChainOnly -> ",\"stateHashed\":false")
            + ",\"prevHash\":"
            + jstr snap.PrevHash
            + ",\"hash\":"
            + jstr snap.Hash
            + "}"

        /// Parse a snapshot line through the one scanner, the mode read from the line: `ChainOnly`
        /// when it carries `"stateHashed":false`, `Strict` when the member is absent or `true`
        /// (every pre-Phase-258 line is strict), a refusal for any other value. The `state` span is
        /// handed to `stateDecode`, whose `Error` is threaded through.
        let ofJsonl (stateDecode: string -> Result<'State, string>) (line: string) : Result<Snapshot<'State>, string> =
            Jsonl.parseLine 1 line
            |> bindR (fun l ->
                let mode =
                    match Jsonl.tryRawField "stateHashed" l with
                    | None
                    | Some "true" -> Ok SnapshotMode.Strict
                    | Some "false" -> Ok SnapshotMode.ChainOnly
                    | Some other -> Error(Jsonl.refuse l ("stateHashed is " + other + ", not a boolean"))

                mode
                |> bindR (fun mode ->
                    Jsonl.intField "seq" l
                    |> bindR (fun seq ->
                        Jsonl.rawField "state" l
                        |> bindR (fun stateRaw ->
                            Jsonl.stringField "prevHash" l
                            |> bindR (fun prevHash ->
                                Jsonl.stringField "hash" l
                                |> Result.map (fun hash -> mode, seq, stateRaw, prevHash, hash))))))
            |> Result.mapError spanFault
            |> Result.bind (fun (mode, seq, stateRaw, prevHash, hash) ->
                stateDecode stateRaw
                |> Result.map (fun state ->
                    { Seq = seq
                      State = state
                      PrevHash = prevHash
                      Hash = hash
                      Mode = mode }))

    // ---- the pre-Phase-296 snapshot matrix: forwards over `Snapshots`, kept for the 0.33.0 draft ----
    //
    // Each forward answers exactly as it did: it pins the mode its name says (so a strict verifier
    // handed a chain-only snapshot still refuses it) and renders the typed fault as the string it
    // returned. Removed after the draft.

    /// Render a `SnapshotFault` as the forwards' `Error` string — BYTE-IDENTICAL to what the matrix
    /// returned, `snapshotAt:` prefix included whichever member reported, because the proof model of
    /// compaction (`proofs/Chain.fst`, and the oracle extracted from it) pins these exact strings and
    /// the oracle suite compares them. The family's typed `SnapshotFault` is the corrected surface.
    let private snapshotFaultText (f: SnapshotFault<'Rej>) : string =
        match f with
        | SnapshotFault.SeqOutOfRange _ -> "OpStream.snapshotAt: seq out of range"
        | SnapshotFault.PrefixRejected(i, _) -> sprintf "OpStream.snapshotAt: prefix replay failed at %d" i
        | SnapshotFault.TailSeqMismatch(e, g) ->
            sprintf "OpStream.snapshot: the tail starts at seq %d, the snapshot's boundary is %d" g e
        | SnapshotFault.TailRejected(i, _) -> sprintf "OpStream.snapshot: tail replay failed at %d" i

    let private modeOf (stateEncode: ('State -> string) option) =
        match stateEncode with
        | Some _ -> SnapshotMode.Strict
        | None -> SnapshotMode.ChainOnly

    let private encoderOf (stateEncode: ('State -> string) option) : 'State -> string =
        defaultArg stateEncode (fun _ -> "")

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
        Snapshots.take (modeOf stateEncode) cfg hashFn (encoderOf stateEncode) w state0 records atSeq
        |> Result.mapError snapshotFaultText

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
        Snapshots.take (modeOf stateEncode) canonicalConfig hashFn (encoderOf stateEncode) w state0 records atSeq
        |> Result.mapError snapshotFaultText

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
        Snapshots.take SnapshotMode.Strict canonicalConfig hashFn stateEncode w state0 records atSeq
        |> Result.mapError snapshotFaultText

    /// A chain-only snapshot under the canonical config. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotAtChainOnly
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State>, string> =
        Snapshots.take SnapshotMode.ChainOnly canonicalConfig hashFn (fun _ -> "") w state0 records atSeq
        |> Result.mapError snapshotFaultText

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
        Snapshots.compact SnapshotMode.Strict cfg hashFn stateEncode w state0 records atSeq
        |> Result.mapError snapshotFaultText

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
        Snapshots.compact SnapshotMode.ChainOnly cfg hashFn (fun _ -> "") w state0 records atSeq
        |> Result.mapError snapshotFaultText

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
        Snapshots.compact SnapshotMode.Strict canonicalConfig hashFn stateEncode w state0 records atSeq
        |> Result.mapError snapshotFaultText

    /// A chain-only compaction under the canonical config. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let compactChainOnly
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
        Snapshots.compact SnapshotMode.ChainOnly canonicalConfig hashFn (fun _ -> "") w state0 records atSeq
        |> Result.mapError snapshotFaultText

    /// Replay the tail from a snapshot's state, UNCHECKED — the tail's first `Seq` is not compared
    /// with the snapshot's, and the fault type cannot say so. A forward for one draft; use
    /// `Snapshots.replayFrom`, which checks the seam.
    [<System.Obsolete(SnapshotForward)>]
    let replayFrom
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : Result<'State, int * 'Rej> =
        replay w snap.State tail

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
        Snapshots.verify cfg hashFn (encoderOf stateEncode) w { snap with Mode = modeOf stateEncode } tail

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
        Snapshots.verify cfg hashFn stateEncode w { snap with Mode = SnapshotMode.Strict } tail

    /// Chain-only boundary verification under `cfg`. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let verifyAcrossChainOnlyWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        Snapshots.verify
            cfg
            hashFn
            (fun _ -> "")
            w
            { snap with
                Mode = SnapshotMode.ChainOnly }
            tail

    /// Strict boundary verification under the canonical config. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let verifyAcross
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        Snapshots.verify canonicalConfig hashFn stateEncode w { snap with Mode = SnapshotMode.Strict } tail

    /// Chain-only boundary verification under the canonical config. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let verifyAcrossChainOnly
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        Snapshots.verify
            canonicalConfig
            hashFn
            (fun _ -> "")
            w
            { snap with
                Mode = SnapshotMode.ChainOnly }
            tail

    /// A strict snapshot line, whatever the snapshot carries. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotToJsonl (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
        Snapshots.toJsonl stateEncode { snap with Mode = SnapshotMode.Strict }

    /// A chain-only snapshot line, whatever the snapshot carries. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotToJsonlChainOnly (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
        Snapshots.toJsonl
            stateEncode
            { snap with
                Mode = SnapshotMode.ChainOnly }

    /// The line's `stateHashed` discriminator: `false` only for an explicit `"stateHashed":false`,
    /// `true` otherwise (a structural fault degrades to strict, the safe default). A forward for one
    /// draft — `Snapshots.ofJsonl` carries the mode on the snapshot it returns.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotStateHashedFromJsonl (line: string) : bool =
        match Jsonl.rawSpan "stateHashed" line with
        | Ok(Some v) -> v <> "false"
        | Ok None
        | Error _ -> true

    /// `Snapshots.ofJsonl`. A forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotFromJsonlResult
        (stateDecode: string -> Result<'State, string>)
        (line: string)
        : Result<Snapshot<'State>, string> =
        Snapshots.ofJsonl stateDecode line

    /// `Snapshots.ofJsonl` over a state decode that cannot fail (a throw is caught and named). A
    /// forward for one draft.
    [<System.Obsolete(SnapshotForward)>]
    let snapshotFromJsonl (stateDecode: string -> 'State) (line: string) : Result<Snapshot<'State>, string> =
        Snapshots.ofJsonl
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
    /// below `Function` and stays FSharp.Core-only). A non-deterministic label names one or more of
    /// `clock`, `random`, `network`, in that order, joined by `+` (`"clock"`, `"clock+random"`).
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
            let rec tipOf n (last: string) =
                function
                | [] -> n, last
                | (c: EffectCapture) :: rest -> tipOf (n + 1) c.Hash rest

            let seq, prev = tipOf 0 cfg.Genesis captures

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

    /// `captureEffectWith` from the canonical genesis `""` — the record seam as it always was.
    let captureEffect
        (hashFn: HashFn)
        (encode: 'v -> string)
        (det: string)
        (eff: string)
        (effect: unit -> 'v)
        (captures: EffectCapture list)
        : 'v * EffectCapture list =
        captureEffectWith canonicalConfig hashFn encode det eff effect captures

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

    /// `firstCaptureBreak` from `cfg.Genesis` (Phase 296) — the capture chain's genesis read from the
    /// config rather than hard-wired `""`, through the one chain walker. Only `cfg.Genesis` is read: a
    /// capture's pre-image is the capture format, not the op payload.
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

    /// The first integrity fault in a capture chain (Phase 27), or `None` if intact — the capture
    /// analogue of `firstChainBreak`. Walks the chain and returns the first capture whose
    /// sequence, prev-link, or hash fails; reuses `ChainBreak` so a capture break localises
    /// exactly as an op break does (Phase 21).
    let firstCaptureBreak (hashFn: HashFn) (captures: EffectCapture list) : ChainBreak option =
        firstCaptureBreakWith canonicalConfig hashFn captures

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

    /// The default no-op attestation sink: never signs (`Sign` ⇒ `None`) and verifies nothing
    /// (`Verify` ⇒ `false`). Signing is opt-in — a stream that does not plug in a real sink behaves
    /// exactly as before. Enterprise hosts supply a KMS / HSM-backed `IAttestationSink`.
    let noAttestation: IAttestationSink =
        { new IAttestationSink with
            member _.Sign _ = None
            member _.Verify _ _ = false }

    /// The current head of a chain under an explicit `StreamConfig` (Phase 296) — the last record's
    /// `Hash`, or `cfg.Genesis` for an empty chain: the seed every chain walker starts from, so an
    /// empty stream appended under a non-empty genesis has the head its first record's `PrevHash`
    /// names (the class of defect Phase 227 fixed for `snapshotAtOpt`).
    let headWith (cfg: StreamConfig) (records: OpRecord<'Op> list) : string =
        defaultArg (snd (tip records)) cfg.Genesis

    /// The current head of a chain — the last record's `Hash`, or the canonical genesis `""` for an
    /// empty chain. The thing an attestation signs (the hash-chain attests the whole prefix). A
    /// stream under another genesis reads its head with `headWith`.
    let head (records: OpRecord<'Op> list) : string = headWith canonicalConfig records

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
    ///
    /// **One walk (Phase 296)** for the head check and the append together, where there were four.
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
            Jsonl.parseLine 1 line
            |> bindR (fun l ->
                let turn =
                    match Jsonl.tryRawField "turn" l with
                    | None
                    | Some "null" -> Ok None
                    | Some _ -> Jsonl.intField "turn" l |> Result.map Some

                Jsonl.stringField "actor" l
                |> bindR (fun actor ->
                    Jsonl.stringField "session" l
                    |> bindR (fun session ->
                        turn
                        |> bindR (fun turn ->
                            Jsonl.stringField "at" l
                            |> bindR (fun at ->
                                Jsonl.rawField "op" l
                                |> bindR (fun raw -> decodeInner raw |> Result.mapError (Jsonl.refuse l))
                                |> Result.map (fun op ->
                                    { Actor = actor
                                      Session = session
                                      Turn = turn
                                      At = at
                                      Op = op }))))))
            |> Result.mapError spanFault

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
            // Linear (Phase 296): each group is built reversed with a cons, then reversed once — the
            // old `group @ [ r ]` copied the group on every record, quadratic in a busy actor's share.
            (Map.empty, records)
            ||> List.fold (fun acc r ->
                let k = keyOf r.Op
                Map.add k (r :: (Map.tryFind k acc |> Option.defaultValue [])) acc)
            |> Map.map (fun _ group -> List.rev group)

        /// Project an attributed stream to "who appended what" — records grouped by actor id, each group
        /// in stream order. A pure fold, no host dependency.
        let byActor (records: OpRecord<Attributed<'Op>> list) : Map<string, OpRecord<Attributed<'Op>> list> =
            groupBy _.Actor records

        /// Project an attributed stream by session id — each session's appended records in stream order.
        /// A pure fold, no host dependency.
        let bySession (records: OpRecord<Attributed<'Op>> list) : Map<string, OpRecord<Attributed<'Op>> list> =
            groupBy _.Session records

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
            appendIfCore canonicalConfig hashFn w (Some expectedHead) actor op state records
            |> Result.map (fun (state', records', entry) ->
                AppendOutcome.Appended(state', records', KeyIndex.add key entry index))
