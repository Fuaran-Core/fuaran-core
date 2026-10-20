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

/// The compacted stream's verbs (Phase 301; the public module itself since Phase 516): compact a stream
/// to a snapshot and the tail above it, compact again from a compacted stream, append onto the tail —
/// plain, compare-and-append and idempotent — and verify and replay across the boundary. Every verb
/// that reads or extends the chain takes the stream's one `Stream` value.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Compacted =

    let private indexOnto (keyOf: 'Op -> string option) (index: KeyIndex) (records: OpRecord<'Op> list) : KeyIndex =
        (index, records)
        ||> List.fold (fun idx r ->
            match keyOf r.Op with
            | Some k -> KeyIndex.add k { Seq = r.Seq; Hash = r.Hash } idx
            | None -> idx)

    /// The anchor the tail hangs from: the snapshot's boundary (Phase 530).
    let private anchorOf (c: Compacted<'Op, 'State>) : Anchor =
        { Seq = c.Snapshot.Seq
          PrevHash = c.Snapshot.PrevHash }

    /// The anchor the next record hangs from: the tail's tip, from the snapshot's boundary, in one walk.
    let private tip (c: Compacted<'Op, 'State>) : Anchor =
        ChainShape.tipBy (fun (r: OpRecord<'Op>) -> r.Hash) (anchorOf c) c.Tail

    /// Compact `records` at `atSeq` (`Snapshots.compact`, same refusals) into a compacted stream
    /// whose `Keys` is the index `keyOf` builds over the discarded prefix — first-wins, an op
    /// `keyOf` answers `None` for carries no key. A stream that keys nothing passes
    /// `fun _ -> None`. Verify, then compact: the prefix is folded and discarded unchecked.
    let compactAt
        (mode: SnapshotMode)
        (s: Stream<'Op, 'State, 'Rej>)
        (stateEncode: 'State -> string)
        (keyOf: 'Op -> string option)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Compacted<'Op, 'State>, SnapshotFault<'Rej>> =
        Snapshots.compact mode s stateEncode state0 records atSeq
        |> Result.map (fun (snap, tail) ->
            { Snapshot = snap
              Tail = tail
              Keys = indexOnto keyOf KeyIndex.empty (List.truncate atSeq records) })

    /// Compact AGAIN at `atSeq`, counted in the ORIGIN's numbering (`Snapshot.Seq` up to
    /// `Snapshot.Seq` plus the tail's length) — the snapshot sealed in the stream's own mode, the
    /// cut tail records indexed onto `Keys`. Exactly the compacted stream `compactAt` takes of the
    /// full history at `atSeq` (`compact_compose`), refusals included: `SeqOutOfRange` names
    /// `atSeq` and the origin length, and `PrefixRejected` the ORIGIN index of the op the domain
    /// refused. A tail that does not start at the boundary is `TailSeqMismatch`.
    let compactFrom
        (s: Stream<'Op, 'State, 'Rej>)
        (stateEncode: 'State -> string)
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

            match OpStream.replay s.Witness snap.State cut with
            | Error(i, e) -> Error(SnapshotFault.PrefixRejected(snap.Seq + i, e))
            | Ok state ->
                // The new boundary is the tip of the part of the tail just replayed (Phase 530).
                let at = ChainShape.tipBy (fun (r: OpRecord<'Op>) -> r.Hash) (anchorOf c) cut

                Ok
                    { Snapshot = Sealed.seal snap.Mode s.HashFn stateEncode at state |> Sealed.toSnapshot
                      Tail = List.skip k c.Tail
                      Keys = indexOnto keyOf c.Keys cut }

    /// The head — the tail's last hash, or the snapshot's `PrevHash` (the boundary record's hash,
    /// the configured genesis at sequence zero) on an empty tail. What `appendIfTo` compares and
    /// what an attestation of a compacted stream signs: the same head the full stream has.
    let head (c: Compacted<'Op, 'State>) : string = (tip c).PrevHash

    /// Append onto a compacted stream: the record at sequence `Snapshot.Seq` plus the tail's
    /// length, linked to `head c`, hashed under `s`'s config. A domain rejection is the `Error`.
    let appendTo
        (s: Stream<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (c: Compacted<'Op, 'State>)
        : Result<'State * Compacted<'Op, 'State>, 'Rej> =
        let at = tip c

        OpStream.chainOne s actor at.Seq at.PrevHash op state
        |> Result.map (fun (state', r) -> state', { c with Tail = c.Tail @ [ r ] })

    /// Compare-and-append onto a compacted stream (`appendIf`'s contract): `StaleHead` when
    /// `expectedHead` is not `head c`, nothing written; otherwise `appendTo`, a domain rejection
    /// as `Domain`.
    let appendIfTo
        (s: Stream<'Op, 'State, 'Rej>)
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
            appendTo s actor op state c |> Result.mapError AppendRejection.Domain

    /// Idempotent append onto a compacted stream (`appendIdempotent`'s contract): a key `index`
    /// already holds is `Duplicate` naming the entry it produced — before or after the boundary,
    /// so a retry of a pre-compaction key converges — and nothing is written; otherwise `appendTo`
    /// and the index extended. Thread `keyIndex keyOf c` as the first `index`.
    let appendIdempotentTo
        (s: Stream<'Op, 'State, 'Rej>)
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
            appendTo s actor op state c
            |> Result.map (fun (state', c') ->
                let r = List.last c'.Tail
                CompactedOutcome.Appended(state', c', KeyIndex.add key { Seq = r.Seq; Hash = r.Hash } index))

    /// The key index of the whole history: `Keys` (the discarded prefix's) with the tail's keys
    /// folded on, first-wins — `KeyIndex.ofStream` of the uncompacted stream, rebuilt without it.
    let keyIndex (keyOf: 'Op -> string option) (c: Compacted<'Op, 'State>) : KeyIndex = indexOnto keyOf c.Keys c.Tail

    /// `Snapshots.verify` over the snapshot and the tail, under the snapshot's own mode.
    let verify (s: Stream<'Op, 'State, 'Rej>) (stateEncode: 'State -> string) (c: Compacted<'Op, 'State>) : bool =
        Snapshots.verify s stateEncode c.Snapshot c.Tail

    /// `Snapshots.replayFrom` over the snapshot and the tail — the seam checked.
    let replayFrom
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (c: Compacted<'Op, 'State>)
        : Result<'State, SnapshotFault<'Rej>> =
        Snapshots.replayFrom w c.Snapshot c.Tail
