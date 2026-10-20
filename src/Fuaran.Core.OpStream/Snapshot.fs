namespace Fuaran.Core

/// How a snapshot's hash binds its checkpoint (Phase 258, carried as a value since Phase 296).
/// `Strict` folds the canonical encoding of the `'State` into the hash, so a swapped state is caught;
/// `ChainOnly` binds the prefix link and the boundary position only, and the stored `'State` is
/// trusted — the trade-off a domain whose state has no canonical encoder chooses. The mode travels ON
/// the snapshot, so a reader verifies it the way it was taken rather than re-reading the line to
/// guess.
[<RequireQualifiedAccess>]
type SnapshotMode =
    /// The hash folds in the state's canonical encoding, so a swapped `State` fails verification.
    | Strict
    /// The hash binds only the boundary `Seq` and `PrevHash`; the stored `State` is trusted, not
    /// checked. Serialised as `"stateHashed":false`.
    | ChainOnly

/// A checkpoint of the folded `'State` at a sequence boundary (Phase 244). `PrevHash` is
/// the boundary record's hash (the config's genesis at sequence zero); `Hash` chains the snapshot
/// into the stream so the checkpoint and the truncated prefix are tamper-evident. Replay can resume
/// from a snapshot instead of from the origin — bounded replay for unbounded histories (FGP 5).
/// `Mode` (Phase 296) is how `Hash` was computed, and so how the snapshot verifies.
type Snapshot<'State> =
    {
        /// The boundary: the number of records folded into `State`, and so the `Seq` the tail's first
        /// record must carry.
        Seq: int
        /// The state after replaying records `0 .. Seq - 1` from the initial state.
        State: 'State
        /// The hash of record `Seq - 1`, or the config's genesis when `Seq` is zero.
        PrevHash: string
        /// `hashFn PrevHash` over the snapshot payload for `Mode`; verification recomputes it before
        /// walking the tail.
        Hash: string
        /// Whether `Hash` covers the state; verification uses the mode stored here, not a caller's guess.
        Mode: SnapshotMode
    }

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
    /// The snapshot's own `Hash` does not recompute: `expected` is the recomputed digest, `got` the
    /// stored one. Checked first; the tail is not walked.
    | SnapshotHash of expected: string * got: string
    /// The snapshot verified but its tail did not; the break's `Index` is the position in the tail.
    | Tail of ChainBreak

/// A state sealed at an anchor (Phase 530) — the one shape a linear `Snapshot`, the compacted stream's
/// snapshot and the op-DAG's checkpoint each are: `State` folded up to the boundary `Anchor`, and
/// `Seal` the hash binding the two by `Mode` (`Sealed.seal`). A snapshot is the sealed state at its
/// stream boundary (`Sealed.ofSnapshot`); a DAG checkpoint is the strict sealed state at sequence zero
/// with its node's id as the anchor's hash. The seal bytes are the snapshot hash's, unchanged.
type Sealed<'State> =
    {
        /// The boundary the state stands at — the tail above it hangs from the same anchor.
        Anchor: Anchor
        /// The state folded up to the boundary.
        State: 'State
        /// Whether `Seal` covers the state (`Strict`) or binds the boundary alone (`ChainOnly`).
        Mode: SnapshotMode
        /// `hashFn Anchor.PrevHash` over the seal payload for `Mode`.
        Seal: string
    }

/// The sealing verbs, written once over `Sealed` (Phase 530): `Snapshots`, the compacted
/// stream and the op-DAG's checkpoints seal, verify and replay through these.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Sealed =

    /// The hash pre-image binding a sealed state to its anchor, by its mode. `Strict` folds the state
    /// in; `ChainOnly` carries the `stateHashed` discriminator instead, so a chain-only seal can never
    /// collide with a strict one at the same boundary. Byte-identical to the snapshot pre-images.
    let private payload (stateEncode: 'State -> string) (s: Sealed<'State>) : string =
        match s.Mode with
        | SnapshotMode.Strict ->
            "{\"snapshot\":true,\"seq\":"
            + string s.Anchor.Seq
            + ",\"state\":"
            + stateEncode s.State
            + "}"
        | SnapshotMode.ChainOnly -> "{\"snapshot\":true,\"seq\":" + string s.Anchor.Seq + ",\"stateHashed\":false}"

    /// A sealed state as a one-record chain hung from its own anchor — so its seal is verified by the
    /// one chain walker, as every other chained record is.
    let private shape (stateEncode: 'State -> string) : ChainShape<Sealed<'State>> =
        { SeqOf = fun s -> s.Anchor.Seq
          PrevOf = fun s -> s.Anchor.PrevHash
          HashOf = fun s -> s.Seal
          PayloadOf = payload stateEncode
          Link = fun s _ seal -> { s with Seal = seal } }

    /// `state` sealed at `anchor` by `mode` — the one sealing step a snapshot, a re-compaction and a
    /// DAG checkpoint share, so each mints the bytes the others do.
    let seal
        (mode: SnapshotMode)
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (anchor: Anchor)
        (state: 'State)
        : Sealed<'State> =
        ChainShape.linkAt
            (shape stateEncode)
            hashFn
            anchor
            { Anchor = anchor
              State = state
              Mode = mode
              Seal = "" }

    /// Where a sealed state and the tail above it fail to verify: the seal first
    /// (`SnapshotBreak.SnapshotHash`, the recomputed seal and the carried one; the tail is not walked),
    /// then the first break in `tail` walked by `tailShape` from the same anchor (`SnapshotBreak.Tail`,
    /// indexed by position in the tail). An empty tail checks the seal alone.
    let firstBreak
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (tailShape: ChainShape<'R>)
        (sv: Sealed<'State>)
        (tail: 'R list)
        : SnapshotBreak option =
        match ChainShape.walk (shape stateEncode) hashFn sv.Anchor [ sv ] with
        | Some b -> Some(SnapshotBreak.SnapshotHash(b.Expected, b.Got))
        | None -> ChainShape.walk tailShape hashFn sv.Anchor tail |> Option.map SnapshotBreak.Tail

    /// Resume replay from a sealed state: fold `tail` from `State`, refusing a tail whose first record
    /// is not at the anchor's sequence. The seal is not checked here — `firstBreak` is that act.
    let replayFrom
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (sv: Sealed<'State>)
        (tail: OpRecord<'Op> list)
        : Result<'State, SnapshotFault<'Rej>> =
        match tail with
        | r :: _ when r.Seq <> sv.Anchor.Seq -> Error(SnapshotFault.TailSeqMismatch(sv.Anchor.Seq, r.Seq))
        | _ ->
            match OpStream.replay w sv.State tail with
            | Ok st -> Ok st
            | Error(i, e) -> Error(SnapshotFault.TailRejected(i, e))

    /// A snapshot as the state it seals at its boundary.
    let ofSnapshot (snap: Snapshot<'State>) : Sealed<'State> =
        { Anchor =
            { Seq = snap.Seq
              PrevHash = snap.PrevHash }
          State = snap.State
          Mode = snap.Mode
          Seal = snap.Hash }

    /// The snapshot a sealed state is — the record `Snapshots` and its line carry.
    let toSnapshot (sv: Sealed<'State>) : Snapshot<'State> =
        { Seq = sv.Anchor.Seq
          State = sv.State
          PrevHash = sv.Anchor.PrevHash
          Hash = sv.Seal
          Mode = sv.Mode }

/// The snapshot family (Phase 244; one family since Phase 296; over `Sealed` since Phase 530; the public
/// module itself since Phase 516): take a snapshot of a stream's state at a boundary, compact a stream to
/// it, verify the boundary and the tail above it, replay from it, and its one JSONL line. Every verb that
/// reads the chain takes the stream's one `Stream` value, so the boundary is sealed and verified under
/// the config the stream was written under.
[<RequireQualifiedAccess>]
module Snapshots =

    /// The snapshot of `state` at boundary `seq` whose boundary hash is `prevHash`, hashed by
    /// `mode` — `Sealed.seal` at that anchor.
    let internal seal
        (mode: SnapshotMode)
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (seq: int)
        (state: 'State)
        (prevHash: string)
        : Snapshot<'State> =
        Sealed.seal mode hashFn stateEncode { Seq = seq; PrevHash = prevHash } state
        |> Sealed.toSnapshot

    /// Capture a snapshot at boundary `atSeq` — the state after `records[0 .. atSeq-1]` from
    /// `state0` — hashed by `mode`. The boundary hash at sequence zero is `s`'s genesis, the seed
    /// every chain walker starts from (Phase 227: `compact_at_zero_verifies_under_any_genesis`,
    /// `proofs/Chain.fst`); past zero it is the stored `records[atSeq-1].Hash`, read and TRUSTED —
    /// verify the stream before snapshotting it. Only the config's genesis is read: the snapshot's own
    /// pre-image is the checkpoint format, not the per-op chain format.
    let take
        (mode: SnapshotMode)
        (s: Stream<'Op, 'State, 'Rej>)
        (stateEncode: 'State -> string)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State>, SnapshotFault<'Rej>> =
        let n = List.length records

        if atSeq < 0 || atSeq > n then
            Error(SnapshotFault.SeqOutOfRange(atSeq, n))
        else
            let prefix = List.truncate atSeq records

            match OpStream.replay s.Witness state0 prefix with
            | Error(i, e) -> Error(SnapshotFault.PrefixRejected(i, e))
            | Ok state ->
                // The boundary is the tip of the prefix just replayed, read from it rather than
                // from the record list by index.
                let at =
                    ChainShape.tip (ChainShape.records s.Config s.Witness.Encode) (Anchor.genesis s.Config) prefix

                Ok(seal mode s.HashFn stateEncode atSeq state at.PrevHash)

    /// Compact a stream at `atSeq` into `(snapshot, tail)` that replays identically to the full
    /// stream from `state0` — the prefix is discarded, the chain stays verifiable.
    ///
    /// **Verify, then compact (Phase 227).** `compact` does not walk the chain: it reads
    /// `records[atSeq-1].Hash` and TRUSTS it. So the compacted stream verifies exactly when the
    /// original does only if the discarded prefix verified first — `compact_preserves_verify` /
    /// `compact_verifies_iff_original` (`proofs/Chain.fst`). A tamper in the prefix of an
    /// UNVERIFIED stream survives compaction, verifies across the boundary, and once the prefix is
    /// discarded nothing can find it again. Run `OpStream.verifyChain s` over the stream first.
    let compact
        (mode: SnapshotMode)
        (s: Stream<'Op, 'State, 'Rej>)
        (stateEncode: 'State -> string)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State> * OpRecord<'Op> list, SnapshotFault<'Rej>> =
        take mode s stateEncode state0 records atSeq
        |> Result.map (fun snap -> snap, List.skip atSeq records)

    /// The first place a snapshot boundary fails to verify, under the snapshot's OWN mode — its
    /// hash (and, `Strict`, the state it folds in), then the tail's chain from the snapshot's
    /// `PrevHash` and `Seq` under `s`'s config through the one chain walker. `None` for an intact
    /// boundary. The localising form of `verify`, public since Phase 296.
    let firstBreak
        (s: Stream<'Op, 'State, 'Rej>)
        (stateEncode: 'State -> string)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : SnapshotBreak option =
        Sealed.firstBreak
            s.HashFn
            stateEncode
            (ChainShape.records s.Config s.Witness.Encode)
            (Sealed.ofSnapshot snap)
            tail

    /// Verify the chain across the truncation boundary under the snapshot's own mode — `firstBreak
    /// … |> Option.isNone`. A `ChainOnly` snapshot does NOT detect a swapped `'State` (its
    /// trade-off); a `Strict` one does.
    let verify
        (s: Stream<'Op, 'State, 'Rej>)
        (stateEncode: 'State -> string)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        firstBreak s stateEncode snap tail |> Option.isNone

    /// Bounded replay from a snapshot (Phase 296 checks the seam): the tail must start at the
    /// snapshot's boundary — its first record's `Seq` equal to the snapshot's `Seq` — or the
    /// replay would fold ops from the wrong position onto the checkpoint; then `Apply` is folded
    /// over the tail from `snap.State`. The mode does not reach replay.
    let replayFrom
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : Result<'State, SnapshotFault<'Rej>> =
        Sealed.replayFrom w (Sealed.ofSnapshot snap) tail

    /// The snapshot line: the chained line (`linked`) over the snapshot's members — its strict seal
    /// payload, or for a chain-only snapshot the same members with the state carried and
    /// `stateHashed` false, since a chain-only seal does not hold the state the line must.
    let private lineOf (snap: Snapshot<'State>) (stateJson: string) : string =
        Jsonl.linked
            ("{\"snapshot\":true,\"seq\":"
             + string snap.Seq
             + ",\"state\":"
             + stateJson
             + (match snap.Mode with
                | SnapshotMode.Strict -> ""
                | SnapshotMode.ChainOnly -> ",\"stateHashed\":false")
             + "}")
            snap.PrevHash
            snap.Hash

    /// One snapshot line, by the snapshot's mode: a `Strict` line carries no `stateHashed` member
    /// (byte-identical to the pre-Phase-258 format); a `ChainOnly` line carries
    /// `"stateHashed":false`. The `state` member is `stateEncode`'s output embedded raw.
    let toJsonl (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string = lineOf snap (stateEncode snap.State)

    /// `toJsonl` that refuses a state encoding the reader cannot read back (Phase 301) — the
    /// `Jsonl.checkRaw` check over `stateEncode snap.State`, and since Phase 473 over `prevHash` and
    /// `hash`, refused as line 1 by member. On `Ok` the line is `toJsonl`'s, byte for byte.
    let tryToJsonl (stateEncode: 'State -> string) (snap: Snapshot<'State>) : Result<string, JsonlWriteFault> =
        Jsonl.checkedLines
            "state"
            (fun (s: Snapshot<'State>) ->
                [ "state", stateEncode s.State
                  "prevHash", Jsonl.jstr s.PrevHash
                  "hash", Jsonl.jstr s.Hash ])
            lineOf
            [ snap ]

    /// Parse a snapshot line through the one scanner, the mode read from the line: `ChainOnly`
    /// when it carries `"stateHashed":false`, `Strict` when the member is absent or `true`
    /// (every pre-Phase-258 line is strict), a refusal for any other value. The `state` span is
    /// handed to `stateDecode`, whose `Error` is threaded through.
    let ofJsonl (stateDecode: string -> Result<'State, string>) (line: string) : Result<Snapshot<'State>, string> =
        Jsonl.parseLine 1 line
        |> Jsonl.bindR (fun l ->
            let mode =
                match Jsonl.tryRawField "stateHashed" l with
                | None
                | Some "true" -> Ok SnapshotMode.Strict
                | Some "false" -> Ok SnapshotMode.ChainOnly
                | Some other -> Error(Jsonl.refuse l ("stateHashed is " + other + ", not a boolean"))

            mode
            |> Jsonl.bindR (fun mode ->
                Jsonl.linkedOf l (fun _ -> Jsonl.rawField "state" l)
                |> Result.map (fun (seq, stateRaw, prevHash, hash) -> mode, seq, stateRaw, prevHash, hash)))
        |> Result.mapError Jsonl.spanFault
        |> Result.bind (fun (mode, seq, stateRaw, prevHash, hash) ->
            stateDecode stateRaw
            |> Result.map (fun state ->
                { Seq = seq
                  State = state
                  PrevHash = prevHash
                  Hash = hash
                  Mode = mode }))
