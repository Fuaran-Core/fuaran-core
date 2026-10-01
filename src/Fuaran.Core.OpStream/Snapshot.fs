namespace Fuaran.Core

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

/// The bodies of the `OpStream` snapshot members (Phase 332): the `OpStream.Snapshots` family and
/// the pre-Phase-296 matrix forwarded over it. Internal: a consumer reaches each one through its
/// forward in `OpStream` (OpStream.fs), which carries the member's contract, documentation and
/// `Obsolete` attribute.
module internal OpStreamSnapshot =
    open Fuaran.Core.OpStreamChain
    open Fuaran.Core.OpStreamJsonl

    // ---- snapshot / compaction (Phase 244; one family since Phase 296) ----

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

        let verify
            (cfg: StreamConfig)
            (hashFn: HashFn)
            (stateEncode: 'State -> string)
            (w: StreamWitness<'Op, 'State, 'Rej>)
            (snap: Snapshot<'State>)
            (tail: OpRecord<'Op> list)
            : bool =
            firstBreak cfg hashFn stateEncode w snap tail |> Option.isNone

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

    // ---- the pre-Phase-296 snapshot matrix over `Snapshots` (its `Obsolete` forwards are in OpStream.fs) ----

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

    let snapshotAtChainOnly
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State>, string> =
        Snapshots.take SnapshotMode.ChainOnly canonicalConfig hashFn (fun _ -> "") w state0 records atSeq
        |> Result.mapError snapshotFaultText

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

    let compactChainOnly
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (records: OpRecord<'Op> list)
        (atSeq: int)
        : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
        Snapshots.compact SnapshotMode.ChainOnly canonicalConfig hashFn (fun _ -> "") w state0 records atSeq
        |> Result.mapError snapshotFaultText

    let replayFrom
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : Result<'State, int * 'Rej> =
        replay w snap.State tail

    let verifyAcrossWithOpt
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (stateEncode: ('State -> string) option)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        Snapshots.verify cfg hashFn (encoderOf stateEncode) w { snap with Mode = modeOf stateEncode } tail

    let verifyAcrossWith
        (cfg: StreamConfig)
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        Snapshots.verify cfg hashFn stateEncode w { snap with Mode = SnapshotMode.Strict } tail

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

    let verifyAcross
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (snap: Snapshot<'State>)
        (tail: OpRecord<'Op> list)
        : bool =
        Snapshots.verify canonicalConfig hashFn stateEncode w { snap with Mode = SnapshotMode.Strict } tail

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

    let snapshotToJsonl (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
        Snapshots.toJsonl stateEncode { snap with Mode = SnapshotMode.Strict }

    let snapshotToJsonlChainOnly (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
        Snapshots.toJsonl
            stateEncode
            { snap with
                Mode = SnapshotMode.ChainOnly }

    let snapshotStateHashedFromJsonl (line: string) : bool =
        match Jsonl.rawSpan "stateHashed" line with
        | Ok(Some v) -> v <> "false"
        | Ok None
        | Error _ -> true

    let snapshotFromJsonlResult
        (stateDecode: string -> Result<'State, string>)
        (line: string)
        : Result<Snapshot<'State>, string> =
        Snapshots.ofJsonl stateDecode line

    let snapshotFromJsonl (stateDecode: string -> 'State) (line: string) : Result<Snapshot<'State>, string> =
        Snapshots.ofJsonl
            (fun s ->
                try
                    Ok(stateDecode s)
                with ex ->
                    Error ex.Message)
            line
