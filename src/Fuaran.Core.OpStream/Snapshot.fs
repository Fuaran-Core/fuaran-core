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
