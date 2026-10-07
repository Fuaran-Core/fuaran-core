namespace Fuaran.Core

/// A compacted linear stream that LIVES ON (Phase 301): the snapshot a compaction took, the tail of
/// records above its boundary, and the invocation keys the discarded history produced. Until this
/// value compaction was terminal — `append` numbers from the length of the list it is handed and
/// links to its last record, falling back to the genesis, so an append onto a tail continued at the
/// wrong sequence (and onto an empty tail at sequence zero, linked to nothing), a re-compaction of
/// the tail renumbered from zero, and a key index rebuilt from the tail forgot every key the prefix
/// had produced. Every operation on this value reads the BOUNDARY instead: the next sequence is the
/// snapshot's `Seq` plus the tail's length, the head of an empty tail is the snapshot's `PrevHash`
/// (the boundary record's hash), and the key index is `Keys` plus the tail's.
///
/// `Keys` is the index of the discarded history only, fixed at compaction; it is NOT in any hash —
/// like a `ChainOnly` snapshot's state, it is exactly as trustworthy as the act that compacted. A
/// host that persists a compacted stream persists `Keys` beside it (DECISIONS.md, the Phase 301
/// entry); the snapshot line and the record lines are unchanged.
type Compacted<'Op, 'State> =
    {
        /// The checkpoint at the boundary. Its `Seq` is where the tail's numbering starts and its
        /// `PrevHash` is the head while the tail is empty.
        Snapshot: Snapshot<'State>
        /// The records above the boundary, carrying their ORIGINAL sequence numbers and hashes — not a
        /// stream that starts at zero, so never hand it to the plain `append` / `verifyChain`.
        Tail: OpRecord<'Op> list
        /// The invocation keys the discarded prefix produced, fixed at compaction and not hashed; the
        /// tail's own keys are added on top by `keyIndex`.
        Keys: KeyIndex
    }

/// The typed outcome of an idempotent append onto a compacted stream (Phase 301) — `AppendOutcome`
/// with the compacted stream in place of the record list, so a caller is never handed a tail it
/// could mistake for the whole history.
[<RequireQualifiedAccess>]
type CompactedOutcome<'Op, 'State> =
    /// The key was new and the op applied: the advanced state, the compacted stream with the record
    /// appended to its tail, and the caller's index with the key bound to that record.
    | Appended of state: 'State * stream: Compacted<'Op, 'State> * index: KeyIndex
    /// The key was already in the caller's index: the entry it first produced. The reducer never ran
    /// and nothing was appended.
    | Duplicate of existing: EntryRef

/// The bodies of the `OpStream.Compacted` members (Phase 301). Internal: a consumer reaches each one
/// through its forward in `OpStream` (OpStream.fs), which carries the member's contract.
module internal OpStreamCompacted =
    open Fuaran.Core.OpStreamChain
    open Fuaran.Core.OpStreamSnapshot

    /// Fold `keyOf` over `records` onto `index`, first-wins — `KeyIndex.ofStream` continued from an
    /// index rather than started from the empty one, skipping an op that carries no key.
    let private indexOnto (keyOf: 'Op -> string option) (index: KeyIndex) (records: OpRecord<'Op> list) : KeyIndex =
        (index, records)
        ||> List.fold (fun idx r ->
            match keyOf r.Op with
            | Some k -> KeyIndex.add k { Seq = r.Seq; Hash = r.Hash } idx
            | None -> idx)

    /// The next sequence and the head, in one walk of the tail (`tipOf`, from the snapshot's hash).
    let private tip (c: Compacted<'Op, 'State>) : int * string =
        let n, last = tipOf (fun (r: OpRecord<'Op>) -> r.Hash) c.Snapshot.PrevHash c.Tail
        c.Snapshot.Seq + n, last

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
        Snapshots.compact mode cfg hashFn stateEncode w state0 records atSeq
        |> Result.map (fun (snap, tail) ->
            { Snapshot = snap
              Tail = tail
              Keys = indexOnto keyOf KeyIndex.empty (List.truncate atSeq records) })

    let compactFrom
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (keyOf: 'Op -> string option)
        (c: Compacted<'Op, 'State>)
        (atSeq: int)
        : Result<Compacted<'Op, 'State>, SnapshotFault<'Rej>> =
        let snap = c.Snapshot
        let n = List.length c.Tail
        let k = atSeq - snap.Seq

        match c.Tail with
        | (r: OpRecord<'Op>) :: _ when r.Seq <> snap.Seq -> Error(SnapshotFault.TailSeqMismatch(snap.Seq, r.Seq))
        | _ when k < 0 || k > n -> Error(SnapshotFault.SeqOutOfRange(atSeq, snap.Seq + n))
        | _ ->
            let cut = List.truncate k c.Tail

            match replay w snap.State cut with
            | Error(i, e) -> Error(SnapshotFault.PrefixRejected(snap.Seq + i, e))
            | Ok state ->
                let prevHash =
                    if k = 0 then
                        snap.PrevHash
                    else
                        (List.item (k - 1) c.Tail).Hash

                Ok
                    { Snapshot = Snapshots.seal snap.Mode hashFn stateEncode atSeq state prevHash
                      Tail = List.skip k c.Tail
                      Keys = indexOnto keyOf c.Keys cut }

    let head (c: Compacted<'Op, 'State>) : string = snd (tip c)

    let appendTo
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (c: Compacted<'Op, 'State>)
        : Result<'State * Compacted<'Op, 'State>, 'Rej> =
        let seq, prev = tip c

        match w.Apply op state with
        | Error e -> Error e
        | Ok state' ->
            let r =
                { Seq = seq
                  Actor = actor
                  Op = op
                  PrevHash = prev
                  Hash = chainHashOf cfg hashFn seq actor (w.Encode op) prev }

            Ok(state', { c with Tail = c.Tail @ [ r ] })

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
        let actual = head c

        if expectedHead <> actual then
            Error(AppendRejection.StaleHead(expectedHead, actual))
        else
            appendTo cfg hashFn w actor op state c |> Result.mapError AppendRejection.Domain

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
        match KeyIndex.tryFind key index with
        | Some existing -> Ok(CompactedOutcome.Duplicate existing)
        | None ->
            appendTo cfg hashFn w actor op state c
            |> Result.map (fun (state', c') ->
                let r = List.last c'.Tail
                CompactedOutcome.Appended(state', c', KeyIndex.add key { Seq = r.Seq; Hash = r.Hash } index))

    let keyIndex (keyOf: 'Op -> string option) (c: Compacted<'Op, 'State>) : KeyIndex = indexOnto keyOf c.Keys c.Tail

    let verify
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (c: Compacted<'Op, 'State>)
        : bool =
        Snapshots.verify cfg hashFn stateEncode w c.Snapshot c.Tail

    let replayFrom
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (c: Compacted<'Op, 'State>)
        : Result<'State, SnapshotFault<'Rej>> =
        Snapshots.replayFrom w c.Snapshot c.Tail
