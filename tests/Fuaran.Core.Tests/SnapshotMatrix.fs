/// The pre-Phase-296 snapshot matrix, kept as a TEST-LOCAL translation onto `OpStream.Snapshots`.
///
/// Its shipped forwards (`OpStream.snapshotAt`, `compact`, `verifyAcross`, …) left at `1.0.0`
/// (Phase 386). Two kinds of test still speak its shape: the snapshot suite, whose cases were
/// written against these entry points and certify the family's behaviour through them, and the
/// proof-oracle differentials, because the compaction model (`proofs/Chain.fst`, and the oracle
/// extracted from it) pins its refusals as these exact strings. So each member here is the retired
/// body in behaviour — the mode its name pins, the canonical config where it took none, and the
/// typed `SnapshotFault` rendered as the string the model states — over the public family. Nothing
/// ships from this file; a consumer's migration is `docs/migrations/1.0.0.md`.
module Fuaran.Core.Tests.SnapshotMatrix

open Fuaran.Core

let private canonical = OpStream.canonicalConfig

/// A `SnapshotFault` as the matrix's `Error` string — the proof model's spelling, `snapshotAt:`
/// prefix included whichever member reported.
let faultText (f: SnapshotFault<'Rej>) : string =
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

let private encoderOf (stateEncode: ('State -> string) option) : 'State -> string = defaultArg stateEncode (fun _ -> "")

let snapshotAtOptWith
    (cfg: StreamConfig)
    (hashFn: HashFn)
    (stateEncode: ('State -> string) option)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (records: OpRecord<'Op> list)
    (atSeq: int)
    : Result<Snapshot<'State>, string> =
    OpStream.Snapshots.take (modeOf stateEncode) cfg hashFn (encoderOf stateEncode) w state0 records atSeq
    |> Result.mapError faultText

let snapshotAtOpt
    (hashFn: HashFn)
    (stateEncode: ('State -> string) option)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (records: OpRecord<'Op> list)
    (atSeq: int)
    : Result<Snapshot<'State>, string> =
    snapshotAtOptWith canonical hashFn stateEncode w state0 records atSeq

let snapshotAt
    (hashFn: HashFn)
    (stateEncode: 'State -> string)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (records: OpRecord<'Op> list)
    (atSeq: int)
    : Result<Snapshot<'State>, string> =
    OpStream.Snapshots.take SnapshotMode.Strict canonical hashFn stateEncode w state0 records atSeq
    |> Result.mapError faultText

let snapshotAtChainOnly
    (hashFn: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (records: OpRecord<'Op> list)
    (atSeq: int)
    : Result<Snapshot<'State>, string> =
    OpStream.Snapshots.take SnapshotMode.ChainOnly canonical hashFn (fun _ -> "") w state0 records atSeq
    |> Result.mapError faultText

let compactWith
    (cfg: StreamConfig)
    (hashFn: HashFn)
    (stateEncode: 'State -> string)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (records: OpRecord<'Op> list)
    (atSeq: int)
    : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
    OpStream.Snapshots.compact SnapshotMode.Strict cfg hashFn stateEncode w state0 records atSeq
    |> Result.mapError faultText

let compactChainOnlyWith
    (cfg: StreamConfig)
    (hashFn: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (records: OpRecord<'Op> list)
    (atSeq: int)
    : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
    OpStream.Snapshots.compact SnapshotMode.ChainOnly cfg hashFn (fun _ -> "") w state0 records atSeq
    |> Result.mapError faultText

let compact
    (hashFn: HashFn)
    (stateEncode: 'State -> string)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (records: OpRecord<'Op> list)
    (atSeq: int)
    : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
    compactWith canonical hashFn stateEncode w state0 records atSeq

let compactChainOnly
    (hashFn: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (records: OpRecord<'Op> list)
    (atSeq: int)
    : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
    compactChainOnlyWith canonical hashFn w state0 records atSeq

/// The retired `replayFrom`: the tail folded from the snapshot's state, with no boundary check —
/// what it always was (`OpStream.Snapshots.replayFrom` checks the boundary and answers typed).
let replayFrom
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (snap: Snapshot<'State>)
    (tail: OpRecord<'Op> list)
    : Result<'State, int * 'Rej> =
    OpStream.replay w snap.State tail

let verifyAcrossWithOpt
    (cfg: StreamConfig)
    (hashFn: HashFn)
    (stateEncode: ('State -> string) option)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (snap: Snapshot<'State>)
    (tail: OpRecord<'Op> list)
    : bool =
    OpStream.Snapshots.verify cfg hashFn (encoderOf stateEncode) w { snap with Mode = modeOf stateEncode } tail

let verifyAcrossWith
    (cfg: StreamConfig)
    (hashFn: HashFn)
    (stateEncode: 'State -> string)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (snap: Snapshot<'State>)
    (tail: OpRecord<'Op> list)
    : bool =
    OpStream.Snapshots.verify cfg hashFn stateEncode w { snap with Mode = SnapshotMode.Strict } tail

let verifyAcrossChainOnlyWith
    (cfg: StreamConfig)
    (hashFn: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (snap: Snapshot<'State>)
    (tail: OpRecord<'Op> list)
    : bool =
    OpStream.Snapshots.verify
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
    verifyAcrossWith canonical hashFn stateEncode w snap tail

let verifyAcrossChainOnly
    (hashFn: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (snap: Snapshot<'State>)
    (tail: OpRecord<'Op> list)
    : bool =
    verifyAcrossChainOnlyWith canonical hashFn w snap tail

let snapshotToJsonl (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
    OpStream.Snapshots.toJsonl stateEncode { snap with Mode = SnapshotMode.Strict }

let snapshotToJsonlChainOnly (stateEncode: 'State -> string) (snap: Snapshot<'State>) : string =
    OpStream.Snapshots.toJsonl
        stateEncode
        { snap with
            Mode = SnapshotMode.ChainOnly }

/// Whether a snapshot line claims a state-hashed (strict) snapshot: anything but an explicit
/// `"stateHashed":false` reads as strict, an unreadable line included, as the retired reader did.
let snapshotStateHashedFromJsonl (line: string) : bool =
    match OpStream.Jsonl.rawSpan "stateHashed" line with
    | Ok(Some v) -> v <> "false"
    | Ok None
    | Error _ -> true

let snapshotFromJsonlResult
    (stateDecode: string -> Result<'State, string>)
    (line: string)
    : Result<Snapshot<'State>, string> =
    OpStream.Snapshots.ofJsonl stateDecode line

let snapshotFromJsonl (stateDecode: string -> 'State) (line: string) : Result<Snapshot<'State>, string> =
    OpStream.Snapshots.ofJsonl
        (fun s ->
            try
                Ok(stateDecode s)
            with ex ->
                Error ex.Message)
        line
