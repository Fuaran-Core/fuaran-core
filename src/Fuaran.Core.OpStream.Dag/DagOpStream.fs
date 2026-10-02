namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.OpStream.Dag (Phase 250) — a content-addressed branching/merging
//  op-DAG over the SAME `StreamWitness<'Op,'State,'Rej>` the linear op-stream uses.
//
//  Where the linear spine assumes one totally-ordered actor, the DAG carries
//  concurrent branches that converge through a merge node. Each node is identified
//  by the content hash of (parents, actor, encoded op), so identical histories
//  converge to identical ids and tampering is detectable. Replay to a head folds
//  the reducer over the head's ancestor-closure in a deterministic topological
//  order. The linear `Fuaran.Core.OpStream` is untouched — linear consumers pay
//  nothing. FSharp.Core only, Fable-clean (encode path; decode per Phase 241).
// ============================================================================

/// One node of the op-DAG. `Id` is the content hash; `Parents` is `[]` at genesis,
/// `[parent]` for a linear/fork step, and `[left; right]` for a merge.
type DagNode<'Op> =
    { Id: string
      Parents: string list
      Actor: Actor
      Op: 'Op }

/// WHICH integrity check a `DagBreak` failed (Phase 147) — the closed set of reasons the DAG
/// walker can report, typed where the reason is MINTED rather than re-derived downstream by
/// string-matching this library's spellings. The sibling of `ChainBreakReason`, and deliberately
/// NOT a merge with it: the chain and the DAG fail differently, so a shared type would have to
/// carry cases each walker never mints.
///
/// **Two named cases for two spellings**, unlike the chain's three-for-four — the DAG walker makes
/// exactly two checks, so there is no collapse to justify here.
///
/// **`Unrecognised` is the honest arm, not a hedge**, on the same argument the chain's carries: a
/// `DagBreak` also reaches a reader from outside this walker — a host's own verifier, a reason
/// carried across a wire or a process boundary, a record a consumer constructs itself — and the
/// alternative to naming that case is a reader that claims to know which check failed when it does
/// not. `DagBreakReason.ofString` is total and lands there; `firstBreak` never does
/// (`Conformance.dagBreakReasonLaws`).
[<RequireQualifiedAccess>]
type DagBreakReason =
    /// The node's stored id is not the content hash of its (parents, actor, op) — a tampered node.
    | ContentIdMismatch
    /// The node names a parent the DAG does not contain.
    | MissingParent
    /// A reason that did not come from this module's walker. Reported AS unknown: the DAG is
    /// genuinely broken, and nothing here will claim to know which check failed.
    | Unrecognised of reason: string

/// Render / parse a `DagBreakReason` as the wire-and-log string the walker emitted before the type
/// existed, so a consumer that logged those bytes keeps logging them — including the one that
/// matches `fromJsonlVerified`'s error text, which this pair is what keeps byte-identical.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module DagBreakReason =

    /// The canonical string for a reason — the exact spelling `firstBreak` minted before this type.
    let toString (r: DagBreakReason) : string =
        match r with
        | DagBreakReason.ContentIdMismatch -> "content-id mismatch (tampered node)"
        | DagBreakReason.MissingParent -> "missing parent"
        | DagBreakReason.Unrecognised s -> s

    /// Total: both strings the walker ever emitted classify, and anything else is `Unrecognised`
    /// verbatim rather than swept into the nearer-looking case. `toString >> ofString` is the
    /// identity on the named cases.
    let ofString (s: string) : DagBreakReason =
        match s with
        | "content-id mismatch (tampered node)" -> DagBreakReason.ContentIdMismatch
        | "missing parent" -> DagBreakReason.MissingParent
        | other -> DagBreakReason.Unrecognised other

/// The first integrity fault found in a DAG (Phase 21) — the node it occurs at, why, and the
/// expected vs got value. `verifyDag` is `firstBreak … |> Option.isNone`; this names *where*.
/// `Reason` is the closed `DagBreakReason` as of `0.24.0` — it was a bare `string`, which the
/// measured consumer (this repo's own proof differential) had to compare by spelling.
type DagBreak =
    { NodeId: string
      Reason: DagBreakReason
      Expected: string
      Got: string }

/// The *shape* of a merge interference (Phase 64) — the closed enumeration (GP5) of how two ops,
/// one from each of two branch deltas, target the same address and would collide under `apply`.
/// The shapes partition the negation of the Phase-78 independence predicate over the
/// `Ops.Footprint` address kinds (four of nodes; two of slots since Phase 340), so a pair is tagged
/// iff `Ops.independent` would reject it:
///
///   - `ConcurrentUpdate` — both branches touch the *same node's content*: a content-write on one
///     side overlapping the other's content-write or read (two inserts of one id, an insert + a
///     remove of one id, a remove of a node the other reads as its anchor).
///   - `InsertPositionClash` — both branches write the *same named parent's* child-list (two
///     positional inserts, an insert + a reorder, …): they shift the same siblings.
///   - `MoveVsRemove` — one branch removes/moves a node whose *source parent* is a tree fact the
///     pure script cannot name (`Footprint.UnknownParentWrites`) while the other makes any structural
///     write: conservatively a collision, keyed by the removed/moved id. THE pinned over-approximation
///     inherited from `Ops.independent` — see STABILITY.md "Op-script footprint + independence".
///   - `SlotClash of slot` (Phase 340) — both branches access the *same slot* of the node at `Address`
///     and at least one writes it: `Footprint.slotClash`, the set `Ops.interference` reports as
///     `Interference.SlotClash`, so the fold and arbitration name the slot in one vocabulary. The
///     shape carries the slot name because `Address` is the node. A branch that touches the node
///     WHOLE while the other writes one of its slots is a `ConcurrentUpdate` at the node — a
///     whole-node write is a write of every slot — and two writes to DIFFERENT slots of one node are
///     no conflict at all.
[<RequireQualifiedAccess>]
type MergeConflictShape =
    | ConcurrentUpdate
    | InsertPositionClash
    | MoveVsRemove
    | SlotClash of slot: string

/// One enumerated merge interference (Phase 64): the two ops (`Left` from delta A, `Right` from
/// delta B) that both target `Address`, and the `Shape` of their collision. Detection only —
/// `Dag.conflicts` decides nothing, applies nothing, and picks no winner (GP6); the domain's own
/// reconciliation consumes this report. Generic over the opaque `'Op` — no `comparison` and no
/// witness field are demanded (GP2).
type MergeConflict<'Op> =
    { Left: 'Op
      Right: 'Op
      Address: string
      Shape: MergeConflictShape }

/// Why `Dag.append` / `Dag.merge` refused to build a node (Phase 300, Phase 296) — the typed refusals
/// that make the DAG's structural premises properties of everything this module BUILDS rather than
/// premises about its callers. `nodeHash` joins the sorted parent ids with `,`, so a parent id carrying
/// a comma splices: an `append` whose parent id is the string `"x,y"` minted the id of `merge(x, y)`
/// with no hash weakness at all, and replaced that node silently. And `""` is `append`'s genesis
/// marker, never a node id, so `merge("", x)` built a node with a phantom parent.
/// `parent_splice_unambiguous` (proofs/Chain.fst) proves the comma-join injective exactly for
/// non-empty, comma-free ids; those two refusals are that premise, stated where the ids enter. Phase
/// 296 adds the two the DAG's own content decides: a parent it does not hold, and an id it already
/// holds for a different node.
[<RequireQualifiedAccess>]
type DagAppendFault =
    /// A merge was handed `""` for a parent — `append`'s genesis marker, not a node id.
    | EmptyParentId
    /// A parent id carries a `,` — the separator the content-hash pre-image joins parent ids with.
    | CommaInParentId of parentId: string
    /// A parent id the DAG does not hold (Phase 296).
    | UnknownParent of parentId: string
    /// The DAG already holds this content id for a node whose parents (modulo order), actor or op
    /// differ (Phase 296) — a hash collision, which under the 32-bit FNV-1a default is a real event,
    /// or a node loaded from a file that was not built by this module. The node held first stays.
    | ContentIdCollision of nodeId: string

/// Render a `DagAppendFault` for a log line or an exception message (Phase 300).
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module DagAppendFault =

    let toString (f: DagAppendFault) : string =
        match f with
        | DagAppendFault.EmptyParentId -> "a merge parent is \"\", the genesis marker, not a node id"
        | DagAppendFault.CommaInParentId p ->
            "the parent id \""
            + p
            + "\" carries a comma, the separator of the content-hash pre-image"
        | DagAppendFault.UnknownParent p -> "the parent id \"" + p + "\" names no node of the DAG"
        | DagAppendFault.ContentIdCollision id ->
            "the DAG already holds node \""
            + id
            + "\" with different content (a content-id collision)"

/// Why `Dag.appendChecked` / `Dag.mergeChecked` refused (Phase 296) — the shape of
/// `AppendRejection<'Rej>` on the linear stream: a structural `Fault` the DAG decided, or the domain's
/// own rejection of the op at the state the caller named.
[<RequireQualifiedAccess>]
type DagAppendRejection<'Rej> =
    | Fault of DagAppendFault
    | Domain of 'Rej

/// One lane an N-lane reconcile refused because the lane's own delta does not apply (Phase 300): the
/// lane's head, its delta (the exclusive region `Dag.reconcileMany` would have folded), and the first
/// node of that delta the domain rejected, with the rejection. A property of the lane alone — it is
/// replayed on its own, never after another lane — so the set of these is arrival-order-invariant.
type LaneRejection<'Op, 'Rej> =
    { Head: string
      Delta: 'Op list
      NodeId: string
      Reject: 'Rej }

/// Why `Dag.reconcileMany` refused to fold a lane set (Phase 300). Every case is a property of the
/// lane SET, never of the order the heads were named in: the interference report is Phase 64's,
/// symmetric up to a `Left`/`Right` swap; the rejections are sorted by head id.
[<RequireQualifiedAccess>]
type ReconcileFault<'Op, 'Rej> =
    /// Two lanes' exclusive deltas interfere — `Dag.conflicts`' report over every unordered lane pair,
    /// nothing applied (GP6).
    | LanesInterfere of conflicts: MergeConflict<'Op> list
    /// The SHARED region — history two or more heads both hold above the base — does not replay from
    /// the base state: the node that rejected and the rejection. No lane is to blame, so none is named.
    | SharedHistoryRejected of nodeId: string * reject: 'Rej
    /// One or more lanes do not apply from the state the shared region reaches; every such lane, sorted
    /// by head id. Tested BEFORE anything is folded, one lane at a time, so it cannot depend on arrival.
    | LanesRejected of lanes: LaneRejection<'Op, 'Rej> list

/// A content-addressed branching/merging op-DAG over the `StreamWitness`.
module Dag =

    /// The DAG: nodes keyed by content hash.
    type T<'Op> = { Nodes: Map<string, DagNode<'Op>> }

    let empty: T<'Op> = { Nodes = Map.empty }

    /// `nodeHash = hashFn (sorted parents joined) (actor | encoded-op)` — content addressing,
    /// reusing the pluggable `HashFn` (FNV-1a default; host SHA swap) for cross-host parity. Since
    /// Phase 320 the actor is the typed `Actor` (`Actor.encode`), folded into the content id so a
    /// tampered attribution changes the node id exactly as a tampered op does.
    ///
    /// Parents are sorted (Ordinal) BEFORE hashing (Phase 64.1), so a merge node's identity is
    /// **parent-order-independent** — `merge(A,B)` and `merge(B,A)` converge to the same content
    /// id. Without this, two hosts reconciling the same pair minted different ids, defeating the
    /// content-address convergence that is the DAG's whole point. The stored `Parents` list keeps
    /// author order (the head is the primary replay spine); only the hash pre-image is sorted —
    /// exactly the invariant `firstBreak`/`verifyDag` re-derive through this one function.
    let private nodeHash
        (hashFn: HashFn)
        (encode: 'Op -> string)
        (parents: string list)
        (actor: Actor)
        (op: 'Op)
        : string =
        let sorted =
            parents |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))

        hashFn (String.concat "," sorted) (Actor.encode actor + "|" + encode op)

    /// The heads — nodes that are no node's parent.
    let heads (dag: T<'Op>) : string list =
        let parents =
            dag.Nodes |> Map.toList |> List.collect (fun (_, n) -> n.Parents) |> Set.ofList

        dag.Nodes
        |> Map.toList
        |> List.map fst
        |> List.filter (fun id -> not (parents.Contains id))
        |> List.sort

    /// The splice premise on one parent id (Phase 300): not a comma-bearing id. `""` is judged by the
    /// caller, because it means genesis to `append` and nothing at all to `merge`.
    let private parentFault (p: string) : DagAppendFault option =
        if p.Contains "," then
            Some(DagAppendFault.CommaInParentId p)
        else
            None

    /// A parent id the DAG must already hold (Phase 296) — after the splice premise, since a
    /// comma-bearing id is refused for what it IS, not for being absent.
    let private knownParent (dag: T<'Op>) (p: string) : DagAppendFault option =
        match parentFault p with
        | Some f -> Some f
        | None when not (dag.Nodes.ContainsKey p) -> Some(DagAppendFault.UnknownParent p)
        | None -> None

    let private ordinalSort (ids: string list) =
        ids |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))

    /// Is `n` the node (`parents`, `actor`, `op`) would build? Parents compared modulo order — the
    /// content id is parent-order-independent (Phase 64.1), so `merge(a,b)` and `merge(b,a)` are
    /// one node — and the op through its encoding, so no equality is demanded of `'Op` (GP2).
    let private sameNode
        (encode: 'Op -> string)
        (n: DagNode<'Op>)
        (parents: string list)
        (actor: Actor)
        (op: 'Op)
        : bool =
        ordinalSort n.Parents = ordinalSort parents
        && n.Actor = actor
        && encode n.Op = encode op

    /// Add the node, refusing an id the DAG already holds for DIFFERENT content (Phase 296). The
    /// same node added twice is one node — content addressing deduplicates by design, and a retry
    /// that re-sends an append converges on the node it already wrote.
    let private addNode
        (hashFn: HashFn)
        (encode: 'Op -> string)
        (actor: Actor)
        (op: 'Op)
        (parents: string list)
        (dag: T<'Op>)
        : Result<string * T<'Op>, DagAppendFault> =
        let id = nodeHash hashFn encode parents actor op

        match Map.tryFind id dag.Nodes with
        | Some existing when sameNode encode existing parents actor op -> Ok(id, dag)
        | Some _ -> Error(DagAppendFault.ContentIdCollision id)
        | None ->
            let node =
                { Id = id
                  Parents = parents
                  Actor = actor
                  Op = op }

            Ok(id, { Nodes = Map.add id node dag.Nodes })

    /// Append `op` as a child of `parentId` (`""` for genesis). Appending onto a node that already has
    /// a child *forks* a branch. Returns the new node's content id and the extended DAG.
    ///
    /// **Refusals, typed (Phase 300, Phase 296)** — the DAG is returned only when the node is sound:
    ///   - `CommaInParentId` — the parent id carries the separator the content-hash pre-image joins
    ///     parent ids with, so its id would splice into a merge's;
    ///   - `UnknownParent` — a non-empty `parentId` the DAG does not hold (a typo'd parent used to
    ///     build a node `verifyDag` then rejected as `MissingParent`);
    ///   - `ContentIdCollision` — the DAG already holds this id for a node whose parents (modulo
    ///     order), actor or op differ. Under the 32-bit FNV-1a default a collision is a real event
    ///     (about 0.3% at 5,000 nodes, even odds near 77,000), and the old `Map.add` replaced the
    ///     held node silently while `verifyDag` still passed.
    ///
    /// **An identical node deduplicates, deliberately.** The same op by the same actor on the same
    /// parent IS the same node — content addressing converges by design — so it returns `Ok` with the
    /// DAG unchanged. A caller that must count appends keys them itself (`OpStream.appendIdempotent`'s
    /// discipline).
    ///
    /// The op is NOT applied here: the DAG holds no state, and whether an op applies depends on the
    /// state its parent's closure replays to. `appendChecked` applies it against a state the caller
    /// holds; `tryReplayTo` and `reconcileMany` refuse a rejecting node wherever it came from.
    let append
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (parentId: string)
        (dag: T<'Op>)
        : Result<string * T<'Op>, DagAppendFault> =
        if parentId = "" then
            addNode hashFn w.Encode actor op [] dag
        else
            match knownParent dag parentId with
            | Some f -> Error f
            | None -> addNode hashFn w.Encode actor op [ parentId ] dag

    /// Merge two heads into a convergent node (`Parents = [leftId; rightId]`). `op` is the merge
    /// commit's own reconciliation op (a domain no-op where the merge adds nothing).
    ///
    /// Refusals, typed, the left parent judged first: `EmptyParentId` (`""` is `append`'s genesis
    /// marker, not a node id), `CommaInParentId`, `UnknownParent`, and `ContentIdCollision` exactly as
    /// for `append`. With the first two, no merge id can equal an append id built through this module:
    /// an append's pre-image names ONE comma-free parent, a merge's two joined by a comma — and a node
    /// loaded from a file that tries it anyway is refused as a collision, because the comparison is
    /// over the parents themselves, not the id.
    let merge
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (leftId: string)
        (rightId: string)
        (dag: T<'Op>)
        : Result<string * T<'Op>, DagAppendFault> =
        let judge (p: string) =
            if p = "" then
                Some DagAppendFault.EmptyParentId
            else
                knownParent dag p

        match judge leftId |> Option.orElse (judge rightId) with
        | Some f -> Error f
        | None -> addNode hashFn w.Encode actor op [ leftId; rightId ] dag

    /// `append` that also APPLIES the op (Phase 296): `state` is the state `parentId`'s closure
    /// replays to — the caller's, exactly as `OpStream.append` takes the stream's current state — and
    /// an op the domain rejects there is refused before it enters the DAG, instead of surfacing at
    /// replay. Returns the state after the op beside the node. The graph refusals are judged first.
    let appendChecked
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (parentId: string)
        (dag: T<'Op>)
        : Result<'State * string * T<'Op>, DagAppendRejection<'Rej>> =
        match append hashFn w actor op parentId dag with
        | Error f -> Error(DagAppendRejection.Fault f)
        | Ok(id, dag') ->
            match w.Apply op state with
            | Ok state' -> Ok(state', id, dag')
            | Error rej -> Error(DagAppendRejection.Domain rej)

    /// `merge` that also APPLIES the merge op (Phase 296): `state` is the state the merged closure
    /// replays to (`tryReplayTo`, or `reconcileMany`'s result), and a rejected merge op is refused
    /// before it enters the DAG. The graph refusals are judged first. `mergeVerified` (Phase 329)
    /// replays both parents and refuses a `state` that is not theirs; `appendVerified` likewise.
    let mergeChecked
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (leftId: string)
        (rightId: string)
        (dag: T<'Op>)
        : Result<'State * string * T<'Op>, DagAppendRejection<'Rej>> =
        match merge hashFn w actor op leftId rightId dag with
        | Error f -> Error(DagAppendRejection.Fault f)
        | Ok(id, dag') ->
            match w.Apply op state with
            | Ok state' -> Ok(state', id, dag')
            | Error rej -> Error(DagAppendRejection.Domain rej)

    /// The first integrity fault in the DAG, scanned in deterministic id order (Phase 21): a node
    /// whose stored id is not the content hash of its (parents, actor, op) — a tampered node — or a
    /// node naming a parent the DAG does not contain. `None` for an intact DAG. Localises what
    /// `verifyDag` only reports as a boolean, so `fromJsonlVerified` / an operator can say *where*.
    let firstBreak (hashFn: HashFn) (w: StreamWitness<'Op, 'State, 'Rej>) (dag: T<'Op>) : DagBreak option =
        dag.Nodes
        |> Map.toList
        |> List.tryPick (fun (id, n) ->
            let h = nodeHash hashFn w.Encode n.Parents n.Actor n.Op

            if id <> h then
                Some
                    { NodeId = id
                      Reason = DagBreakReason.ContentIdMismatch
                      Expected = h
                      Got = id }
            else
                match n.Parents |> List.tryFind (fun p -> not (dag.Nodes.ContainsKey p)) with
                | Some missing ->
                    Some
                        { NodeId = id
                          Reason = DagBreakReason.MissingParent
                          Expected = ""
                          Got = missing }
                | None -> None)

    /// Verify the DAG: every node's id is the content hash of its (parents, actor, op), and
    /// every parent exists — generalises the linear `verifyChain` to a DAG. Re-expressed over
    /// `firstBreak` (Phase 21) so the two share one definition of integrity.
    let verifyDag (hashFn: HashFn) (w: StreamWitness<'Op, 'State, 'Rej>) (dag: T<'Op>) : bool =
        firstBreak hashFn w dag |> Option.isNone

    /// The Kahn topological-sort core: returns the emitted order **and** the head's ancestor-closure.
    /// On an acyclic closure `List.length order = Set.count anc`; a cycle leaves the cyclic nodes
    /// unreachable so `order` is strictly shorter — the signal `tryTopoOrder` / `isAcyclic` use.
    ///
    /// Since Phase 300 the drain runs over the UNION of several heads' closures (`topoCoreMany`); one
    /// head is the one-root case, byte-for-byte the drain it always was. The drain is a function of the
    /// node SET it covers, so the order the roots are named in cannot reach the output.
    let private topoCoreMany (dag: T<'Op>) (roots: string list) : string list * Set<string> =
        // Ancestor-closure via an explicit work-list (Phase 10) — a deep/long DAG cannot
        // overflow the stack the way the prior fold-recursive `collect` could. Tail-recursive.
        let rec collect (acc: Set<string>) (stack: string list) =
            match stack with
            | [] -> acc
            | id :: rest ->
                if Set.contains id acc then
                    collect acc rest
                else
                    match Map.tryFind id dag.Nodes with
                    | Some n -> collect (Set.add id acc) (n.Parents @ rest)
                    | None -> collect acc rest

        let anc = collect Set.empty roots

        let parentsIn id =
            (Map.find id dag.Nodes).Parents |> List.filter (fun p -> Set.contains p anc)

        let children =
            anc
            |> Set.toList
            |> List.collect (fun id -> parentsIn id |> List.map (fun p -> p, id))
            |> List.groupBy fst
            |> List.map (fun (p, ps) -> p, ps |> List.map snd)
            |> Map.ofList

        let mutable indeg =
            anc
            |> Set.toList
            |> List.map (fun id -> id, List.length (parentsIn id))
            |> Map.ofList

        let mutable ready =
            anc |> Set.toList |> List.filter (fun id -> indeg.[id] = 0) |> List.sort

        let result = ResizeArray<string>()

        while not (List.isEmpty ready) do
            let id = List.head ready
            ready <- List.tail ready
            result.Add id

            match Map.tryFind id children with
            | Some kids ->
                for k in kids do
                    indeg <- Map.add k (indeg.[k] - 1) indeg

                    if indeg.[k] = 0 then
                        ready <- (k :: ready) |> List.sort
            | None -> ()

        List.ofSeq result, anc

    let private topoCore (dag: T<'Op>) (headId: string) : string list * Set<string> = topoCoreMany dag [ headId ]

    /// The ancestor-closure of `headId` in deterministic topological order (parents before
    /// children; the ready frontier is drained smallest-id-first, so the order is total).
    let private topoOrder (dag: T<'Op>) (headId: string) : string list = topoCore dag headId |> fst

    /// Is `headId`'s ancestor-closure acyclic? `false` when a cycle (e.g. in a hand-crafted or
    /// tampered JSONL load, where `fromJsonl` trusts the stored ids) leaves nodes unreachable by the
    /// topological sort. The acyclicity a silent partial replay would otherwise hide (Phase 42).
    let isAcyclic (dag: T<'Op>) (headId: string) : bool =
        let order, anc = topoCore dag headId
        List.length order = Set.count anc

    /// `topoOrder` as a `Result` (Phase 42): `Ok order` for an acyclic closure, else
    /// `Error "Dag.tryTopoOrder: cyclic history at <head> (<k> node(s) unreachable)"` — so a caller
    /// never silently folds over a truncated, cycle-broken prefix.
    let tryTopoOrder (dag: T<'Op>) (headId: string) : Result<string list, string> =
        let order, anc = topoCore dag headId

        if List.length order = Set.count anc then
            Ok order
        else
            Error(
                sprintf
                    "Dag.tryTopoOrder: cyclic history at %s (%d node(s) unreachable)"
                    headId
                    (Set.count anc - List.length order)
            )

    /// A guarded-replay fault (Phase 42, Phase 296): the head is not a node of the DAG, the head's
    /// history is **cyclic** (a plain fold would cover only the acyclic prefix), or a node's op was
    /// **rejected** by the domain witness.
    [<RequireQualifiedAccess>]
    type ReplayFault<'Rej> =
        /// The DAG holds no node with this id (Phase 296) — a typo'd head used to replay to the
        /// initial state as if it named an empty history.
        | UnknownHead of headId: string
        | CyclicHistory of headId: string
        | Rejected of nodeId: string * reject: 'Rej

    /// Replay the UNION of `roots`' ancestor closures from `state0` (Phase 329): every node once, in
    /// the drain order `tryReplayTo` folds a merge node's closure in — so for the two parents of a
    /// merge it is the state the merge node's own replay applies the merge op to. A root the DAG does
    /// not hold is `UnknownHead` (the first such, in the order given); a union that does not drain is
    /// `CyclicHistory` of the first root whose own closure is cyclic (a cycle reached from the union
    /// is reached from some root).
    let private replayClosure
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (dag: T<'Op>)
        (roots: string list)
        : Result<'State, ReplayFault<'Rej>> =
        match roots |> List.tryFind (fun r -> not (dag.Nodes.ContainsKey r)) with
        | Some r -> Error(ReplayFault.UnknownHead r)
        | None ->
            let order, anc = topoCoreMany dag roots

            if List.length order <> Set.count anc then
                let cyclic =
                    roots
                    |> List.tryFind (fun r -> not (isAcyclic dag r))
                    |> Option.defaultValue (List.head roots)

                Error(ReplayFault.CyclicHistory cyclic)
            else
                let rec go st =
                    function
                    | [] -> Ok st
                    | id :: rest ->
                        let n = Map.find id dag.Nodes

                        match w.Apply n.Op st with
                        | Ok st' -> go st' rest
                        | Error e -> Error(ReplayFault.Rejected(id, e))

                go state0 order

    /// Replay to `headId`, refusing what cannot be replayed (Phase 42, Phase 296): fold the reducer
    /// over the head's ancestor-closure in topological order, each op applied once. Deterministic —
    /// the topo order is total — so two convergent histories over the same node set replay to the same
    /// state. `Error(ReplayFault.UnknownHead h)` for a head the DAG does not hold,
    /// `Error(ReplayFault.CyclicHistory h)` when the closure is not fully orderable (a hand-crafted or
    /// tampered load — `fromJsonlVerified`'s content-hash gate makes a forged cycle impossible), and
    /// `Error(ReplayFault.Rejected …)` on a domain rejection.
    ///
    /// Written over `replayClosure` at one root since Phase 329, which is exactly the fold it always
    /// was: the drain of one head's closure is `topoCore`'s, and its only cyclic root is the head.
    let tryReplayTo
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (dag: T<'Op>)
        (headId: string)
        : Result<'State, ReplayFault<'Rej>> =
        replayClosure w state0 dag [ headId ]

    /// Replay to `headId` — a bridge for one draft over `tryReplayTo` (Phase 296). A domain rejection
    /// is `Error(nodeId, reject)` as before; an unknown head or a cyclic history, which this signature
    /// cannot carry, RAISES (`ArgumentException`) where it used to return a silent partial fold or the
    /// initial state as `Ok`. Use `tryReplayTo`.
    [<System.Obsolete("Dag.replayTo cannot report an unknown head or a cyclic history and raises on both; use Dag.tryReplayTo. Removed after the 0.33.0 draft.")>]
    let replayTo
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (dag: T<'Op>)
        (headId: string)
        : Result<'State, string * 'Rej> =
        match tryReplayTo w state0 dag headId with
        | Ok st -> Ok st
        | Error(ReplayFault.Rejected(id, e)) -> Error(id, e)
        | Error(ReplayFault.UnknownHead h) -> invalidArg "headId" ("Dag.replayTo: the DAG holds no node " + h)
        | Error(ReplayFault.CyclicHistory h) -> invalidArg "headId" ("Dag.replayTo: cyclic history at " + h)

    // ---- the verified append (Phase 329) ----
    // `appendChecked` / `mergeChecked` apply the op at a state the CALLER hands in, and do not replay
    // (D83: a DAG holds no state). A node's own state never changes, but on a DAG the parent is chosen
    // per call, so a caller can hand over the state of a DIFFERENT node — fork from an older node while
    // holding the latest head's state, append to one head while holding another's, pass one side's
    // state after a merge — and the op is then judged at a state that never existed at that point in
    // the graph. The verified forms replay the named parent first and refuse a handed-in state that is
    // not its state, so the mis-pairing is caught at the call that made it rather than at a later
    // reconcile. They are ordinary functions a caller chooses (in its tests, in a debug build), never
    // conditional compilation: a package ships one build, and a `DEBUG`-only path reaches no consumer.
    // The price is one replay per call, linear in the parent's closure.

    /// Why `Dag.appendVerified` / `Dag.mergeVerified` refused (Phase 329). A NEW union beside
    /// `DagAppendRejection` rather than a case added to it, so every exhaustive match over the checked
    /// forms' refusal still compiles.
    [<RequireQualifiedAccess>]
    type VerifiedAppendRejection<'State, 'Rej> =
        /// The checked form's own refusal, verbatim: a graph `Fault` (judged before anything is
        /// replayed), or the domain's rejection of the op at a state that WAS the parent's.
        | Checked of DagAppendRejection<'Rej>
        /// The named parent's closure — both parents' for a merge — does not replay from the initial
        /// state: the fault `tryReplayTo` names.
        | ParentReplay of ReplayFault<'Rej>
        /// The handed-in state is not the state the parent's closure replays to; both are carried.
        | StateMismatch of handed: 'State * replayed: 'State

    /// The verification step both forms share: a replay fault, else a mismatch, else the checked form.
    let private verifiedThen
        (stateEquals: 'State -> 'State -> bool)
        (handed: 'State)
        (replayed: Result<'State, ReplayFault<'Rej>>)
        (checkedForm: unit -> Result<'State * string * T<'Op>, DagAppendRejection<'Rej>>)
        : Result<'State * string * T<'Op>, VerifiedAppendRejection<'State, 'Rej>> =
        match replayed with
        | Error fault -> Error(VerifiedAppendRejection.ParentReplay fault)
        | Ok r when not (stateEquals handed r) -> Error(VerifiedAppendRejection.StateMismatch(handed, r))
        | Ok _ -> checkedForm () |> Result.mapError VerifiedAppendRejection.Checked

    /// `appendChecked`, verifying the PAIRING of `state` with `parentId` (Phase 329), under the
    /// caller's state equality — for a `'State` without structural equality, or one whose structural
    /// equality is finer than the domain's. `parentId`'s ancestor closure is replayed from `state0`
    /// (the genesis parent `""` replays to `state0` itself) and compared with `state` as
    /// `stateEquals state replayed`. In order: the graph refusals are judged first
    /// (`Checked(Fault …)`), then a parent whose replay fails surfaces the replay fault
    /// (`ParentReplay`), then a difference is refused (`StateMismatch(state, replayed)`); otherwise
    /// the result is exactly `appendChecked hashFn w actor op state parentId dag`'s. One replay per
    /// call, linear in the parent's closure.
    let appendVerifiedWith
        (stateEquals: 'State -> 'State -> bool)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (parentId: string)
        (dag: T<'Op>)
        : Result<'State * string * T<'Op>, VerifiedAppendRejection<'State, 'Rej>> =
        match append hashFn w actor op parentId dag with
        | Error f -> Error(VerifiedAppendRejection.Checked(DagAppendRejection.Fault f))
        | Ok _ ->
            let replayed =
                if parentId = "" then
                    Ok state0
                else
                    tryReplayTo w state0 dag parentId

            verifiedThen stateEquals state replayed (fun () -> appendChecked hashFn w actor op state parentId dag)

    /// `appendVerifiedWith` under the state's own equality (Phase 329): `appendChecked` that refuses
    /// a handed-in `state` which is not the state `parentId`'s closure replays to from `state0`.
    let appendVerified
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (parentId: string)
        (dag: T<'Op>)
        : Result<'State * string * T<'Op>, VerifiedAppendRejection<'State, 'Rej>> =
        appendVerifiedWith (fun a b -> a = b) hashFn w state0 actor op state parentId dag

    /// `mergeChecked`, verifying the PAIRING of `state` with the two parents (Phase 329), under the
    /// caller's state equality. The union of both parents' closures — WITHOUT the merge op — is
    /// replayed from `state0`, in the drain order `tryReplayTo` folds the merge node's closure in, so
    /// an accepted merge's returned state is the merge node's own replay. Refusals in the order of
    /// `appendVerifiedWith`; otherwise exactly `mergeChecked`'s result.
    let mergeVerifiedWith
        (stateEquals: 'State -> 'State -> bool)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (leftId: string)
        (rightId: string)
        (dag: T<'Op>)
        : Result<'State * string * T<'Op>, VerifiedAppendRejection<'State, 'Rej>> =
        match merge hashFn w actor op leftId rightId dag with
        | Error f -> Error(VerifiedAppendRejection.Checked(DagAppendRejection.Fault f))
        | Ok _ ->
            verifiedThen stateEquals state (replayClosure w state0 dag [ leftId; rightId ]) (fun () ->
                mergeChecked hashFn w actor op state leftId rightId dag)

    /// `mergeVerifiedWith` under the state's own equality (Phase 329): `mergeChecked` that refuses a
    /// handed-in `state` which is not the state both parents' closures replay to from `state0`.
    let mergeVerified
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (leftId: string)
        (rightId: string)
        (dag: T<'Op>)
        : Result<'State * string * T<'Op>, VerifiedAppendRejection<'State, 'Rej>> =
        mergeVerifiedWith (fun a b -> a = b) hashFn w state0 actor op state leftId rightId dag

    // ---- JSONL persistence (Phase 01) ----
    // The linear OpStream round-trips to JSONL; the DAG does too, closing the persistence
    // asymmetry. Read through the linear package's one scanner (`OpStream.Jsonl`, Phase 296) — the
    // Dag module takes no Core.Wire dependency (D2) and stays Fable-clean (Phase 241). One JSON
    // object per node; the `op` value is the witness's own Encode output embedded raw (preserved
    // byte-for-byte); nodes are emitted in id-sorted order so output is stable for a fixed DAG.

    /// JSON string spelling for the node line — the spine's one escaping rule (Phase 287): `"`,
    /// `\`, and every control character `U+0000`–`U+001F` as lower-case `\u00xx`, with NO short
    /// form for `\n` / `\r` / `\t`. A DELIBERATE COPY of `Wire.Json.escape`, for the reason the
    /// scanner below is one: this module takes no `Core.Wire` dependency (D2). It is held
    /// VALUE-IDENTICAL to the original by `StringEscapeVectors` in the conformance kit — which
    /// pins `toJsonl`'s bytes for a node id carrying a control character against
    /// `Wire.Json.escape`'s — so the copy cannot drift quietly. (The `actor` member is spelled by
    /// `Actor.encode`, the linear package's copy of the same rule.) Fable-clean.
    let private jstr (s: string) : string =
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

    /// One JSON object per node, in deterministic id-sorted order. The `op` is embedded as raw
    /// JSON (the witness's own Encode output), so a round-trip preserves it byte-for-byte.
    let private nodeLine (n: DagNode<'Op>) (opJson: string) : string =
        "{\"node\":true,\"id\":"
        + jstr n.Id
        + ",\"parents\":["
        + (n.Parents |> List.map jstr |> String.concat ",")
        + "],\"actor\":"
        + Actor.encode n.Actor
        + ",\"op\":"
        + opJson
        + "}"

    let toJsonl (encode: 'Op -> string) (dag: T<'Op>) : string =
        dag.Nodes
        |> Map.toList
        |> List.map (fun (_, n) -> nodeLine n (encode n.Op))
        |> String.concat "\n"

    /// `toJsonl` that refuses an op encoding the reader cannot read back (Phase 301) — the linear
    /// writers' check (`OpStream.Jsonl.checkRaw`) over every node's `encode n.Op`, in the id order the
    /// lines are written. A node's content id is hashed over its encoding, so an encoding carrying a
    /// line break, or whitespace either side of the value, read back changed and failed `verifyDag`.
    /// The first such node is the `Error`, by its 1-based line and member (`op`); on `Ok` the text is
    /// `toJsonl`'s, byte for byte.
    let tryToJsonl (encode: 'Op -> string) (dag: T<'Op>) : Result<string, JsonlWriteFault> =
        let rec go (i: int) (acc: string list) =
            function
            | [] -> Ok(acc |> List.rev |> String.concat "\n")
            | (n: DagNode<'Op>) :: rest ->
                let opJson = encode n.Op

                match OpStream.Jsonl.checkRaw opJson with
                | Error reason ->
                    Error
                        { Line = i + 1
                          Member = "op"
                          Reason = reason }
                | Ok() -> go (i + 1) (nodeLine n opJson :: acc) rest

        go 0 [] (dag.Nodes |> Map.toList |> List.map snd)

    /// Parse JSONL back into a DAG (the `op` raw span is handed to `w.Decode`) through the ONE
    /// JSONL scanner, `OpStream.Jsonl` (Phase 296; until then this module carried a verbatim copy).
    /// Fully portable — runs under .NET and Fable. A malformed line, a member of the wrong kind
    /// (`"id":12`, a non-string parent), an unknown actor kind, or a witness decode `Error` is an
    /// `Error` rendering the typed `JsonlFault` — `line N: <reason> (position P)`, `N` 1-based over
    /// every line of the text — never an exception (GP4). The `op` raw span is preserved
    /// byte-for-byte, so a round-trip is identical.
    ///
    /// **A repeated id (Phase 296).** A line repeating a node already read deduplicates, as `append`
    /// does; a line naming an id already held for DIFFERENT content — parents modulo order, actor, or
    /// op — is refused as a content-id collision rather than replacing the node read first.
    ///
    /// **Structural only — this does NOT verify integrity.** Nodes are keyed by their *stored* id;
    /// a tampered id, a dangling parent, or a cycle decodes to a clean `Ok` here. Run `verifyDag`
    /// afterwards (or use `fromJsonlVerified`, Phase 13) to recompute content ids and confirm every
    /// parent exists before trusting the DAG.
    let fromJsonl (w: StreamWitness<'Op, 'State, 'Rej>) (text: string) : Result<T<'Op>, string> =
        let bind f r = Result.bind f r

        let nodeOf (line: JsonlLine) : Result<JsonlLine * DagNode<'Op>, JsonlFault> =
            OpStream.Jsonl.stringField "id" line
            |> bind (fun id ->
                OpStream.Jsonl.stringsField "parents" line
                |> bind (fun parents ->
                    OpStream.Jsonl.actorField "actor" line
                    |> bind (fun actor ->
                        OpStream.Jsonl.rawField "op" line
                        |> bind (fun raw -> w.Decode raw |> Result.mapError (OpStream.Jsonl.refuse line))
                        |> Result.map (fun op ->
                            line,
                            { Id = id
                              Parents = parents
                              Actor = actor
                              Op = op }))))

        text
        |> OpStream.Jsonl.scanRecords nodeOf
        |> bind (fun lines ->
            let rec go (acc: Map<string, DagNode<'Op>>) =
                function
                | [] -> Ok { Nodes = acc }
                | (line, node: DagNode<'Op>) :: rest ->
                    match Map.tryFind node.Id acc with
                    | None -> go (Map.add node.Id node acc) rest
                    | Some held when sameNode w.Encode held node.Parents node.Actor node.Op -> go acc rest
                    | Some _ ->
                        Error(
                            OpStream.Jsonl.refuse
                                line
                                ("node "
                                 + node.Id
                                 + " is already held with different content (a content-id collision)")
                        )

            go Map.empty lines)
        |> Result.mapError JsonlFault.toString

    /// `fromJsonl` + the integrity gate (Phase 13): parses structurally, then runs `verifyDag` so a
    /// tampered id, a dangling parent, or a (hash-impossible-to-forge, so id-mismatching) cycle is a
    /// named `Error` instead of a silently-corrupt `Ok`. The load-time analogue of the discipline
    /// `Dag.fromJsonl`'s docstring used to imply but did not enforce.
    let fromJsonlVerified
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<T<'Op>, string> =
        fromJsonl w text
        |> Result.bind (fun dag ->
            match firstBreak hashFn w dag with
            | None -> Ok dag
            | Some b ->
                // `toString` renders the pre-0.24.0 spelling, so this error's bytes are unchanged by
                // Phase 147 — a consumer matching this text keeps matching it.
                Error(sprintf "Dag.fromJsonlVerified: %s at node %s" (DagBreakReason.toString b.Reason) b.NodeId))

    // ---- merge-base / branch-delta (Phase 08) ----
    // The *generic* half of a merge: locate the divergence point of two heads and enumerate
    // one branch's delta. The reconciliation itself stays domain-side (GP6 — Core owns no
    // merge semantics); these pure graph queries inform a `merge`, they do not perform it.

    /// The ancestor-closure of `id` — `id` itself plus all its transitive parents present in
    /// the DAG. Empty for an id not in the DAG. Total.
    let ancestorsOf (dag: T<'Op>) (id: string) : Set<string> =
        // Explicit work-list (Phase 10) so a long ancestor chain cannot overflow the stack.
        let rec collect (acc: Set<string>) (stack: string list) =
            match stack with
            | [] -> acc
            | cur :: rest ->
                if Set.contains cur acc then
                    collect acc rest
                elif not (dag.Nodes.ContainsKey cur) then
                    collect acc rest
                else
                    collect (Set.add cur acc) ((dag.Nodes.[cur]).Parents @ rest)

        collect Set.empty [ id ]

    /// The merge base of two heads: A MAXIMAL common ancestor — the common ancestor with the largest
    /// ancestor-closure (a node strictly deeper than any of its own ancestors has a strictly larger
    /// closure, so no common ancestor descends from the one returned), tie-broken by id for
    /// determinism. `None` when the histories are disjoint. Total — a missing id contributes an empty
    /// closure.
    ///
    /// A POLICY, not "the" base (Phase 300): where two heads share several maximal common ancestors — a
    /// criss-cross, two merges of the same two lanes — the tie-break picks one, and the history the
    /// other one carries is still shared by both heads. `reconcile` / `reconcileMany` no longer lean on
    /// the choice: whatever base they are handed, history both heads hold above it is applied once.
    let mergeBase (dag: T<'Op>) (left: string) (right: string) : string option =
        let common = Set.intersect (ancestorsOf dag left) (ancestorsOf dag right)

        if Set.isEmpty common then
            None
        else
            common
            |> Set.toList
            |> List.maxBy (fun id -> Set.count (ancestorsOf dag id), id)
            |> Some

    /// The branch delta: the nodes on the region from (exclusive) `baseId` to (inclusive)
    /// `head`, in topological order — the ops a reconciler replays. Equals `head`'s
    /// ancestor-closure minus `baseId`'s. `base = head` ⇒ `[]`; an unrelated base ⇒ the whole
    /// head closure. Total — a missing id yields `[]`.
    let between (dag: T<'Op>) (baseId: string) (head: string) : DagNode<'Op> list =
        let baseClosure = ancestorsOf dag baseId

        topoOrder dag head
        |> List.filter (fun id -> not (Set.contains id baseClosure))
        |> List.map (fun id -> dag.Nodes.[id])

    /// The branch delta's *ops*, in the same topological order as `between` (Phase 26) — the op
    /// sequence a domain replays through its own reducer / `OpStream` to fast-forward `baseId` to
    /// `head`. A thin projection of `between` (the reconciliation itself stays domain-side, GP6):
    /// `base = head` ⇒ `[]`; an unrelated base ⇒ the whole head closure's ops.
    let betweenOps (dag: T<'Op>) (baseId: string) (head: string) : 'Op list =
        between dag baseId head |> List.map (fun n -> n.Op)

    // ---- merge-conflict enumeration (Phase 64) ----
    // The generic DETECTION half of a merge. #08 gives the two branch deltas that diverge from a
    // common base; #64 names the ops across those deltas that target the same address and would
    // interfere — a typed, enumerated `MergeConflict list`. Detection, not resolution (GP6):
    // `conflicts` decides nothing, applies nothing, picks no winner. The domain's own reconciliation
    // consumes the report — the merge analogue of `canApply` naming applicability.
    //
    // The address model is INJECTED by the caller as `'Op -> Footprint` (the domain feeds
    // `Ops.footprint` over its own witnesses), because `Dag` is generic over the opaque `'Op`. Firing
    // is EXACTLY `not (Ops.independent (fp a) (fp b))` per pair — each shape is a strict negation of
    // one of #78's independence clauses, so their union fires iff the pair is not independent. This is
    // why `Dag.reconcile`'s footprint cross-validation (`independent ⇒ conflicts = []`, Phase 83)
    // holds by construction, and why conservativity is inherited verbatim from #78: a remove/move is
    // reported against ANY concurrent structural write (its source parent is a tree fact the pure
    // script cannot name), so `conflicts = []` is the promise that the two deltas commute — never a
    // false "clean merge". See STABILITY.md "Op-script footprint + independence".

    /// Does `f` write structure — a NAMED parent's child-list (`StructureWrites`) or an UNKNOWN source
    /// parent (a remove/move, `UnknownParentWrites`)? Mirrors `Ops.independent`'s internal predicate so
    /// `conflicts` fires exactly when `Ops.independent` returns false.
    let private writesStructure (f: Footprint) : bool =
        not (Set.isEmpty f.StructureWrites) || not (Set.isEmpty f.UnknownParentWrites)

    /// Enumerate the merge conflicts between two branch deltas from a common base (Phase 64): every op
    /// pair `(a ∈ deltaA, b ∈ deltaB)` whose footprints are not independent, tagged by `Shape` and the
    /// shared `Address`. `footprintOf` is the caller's address projection (the domain feeds
    /// `Ops.footprint`). Footprint-independent (disjoint) deltas ⇒ `[]` — no false "conflict-free"
    /// either (firing ≡ `not (Ops.independent …)`). Deterministic: deltaA order × deltaB order ×
    /// id-sorted addresses (`Set` enumerates ordered). Detection only (GP6) — nothing is applied.
    let conflicts (footprintOf: 'Op -> Footprint) (deltaA: 'Op list) (deltaB: 'Op list) : MergeConflict<'Op> list =
        [ for a in deltaA do
              let fa = footprintOf a

              for b in deltaB do
                  let fb = footprintOf b

                  // The four overlap classes — each the strict negation of one Ops.independent clause,
                  // so their union is non-empty iff the pair is NOT independent.
                  //  (1) concurrent-update: a content-write overlapping the other's content-write or read
                  //      — and, since Phase 340, a slot access of a node the other side touches whole
                  //      (`Footprint.slotsAgainstNode`, both ways: a whole-node write is a write of every
                  //      slot, so the pair collides at the NODE).
                  let concurrent =
                      Set.unionMany
                          [ Set.intersect fa.ContentWrites fb.ContentWrites
                            Set.intersect fa.ContentWrites fb.Reads
                            Set.intersect fb.ContentWrites fa.Reads
                            Footprint.slotsAgainstNode fa fb
                            Footprint.slotsAgainstNode fb fa ]
                  //  (2) insert-position clash: a shared NAMED structural parent.
                  let insertClash = Set.intersect fa.StructureWrites fb.StructureWrites
                  //  (3) move-vs-remove: a remove/move (unknown source parent) racing the other side's
                  //      structural write — the pinned #78 over-approximation, keyed by the removed/moved id.
                  let moveRemove =
                      Set.union
                          (if not (Set.isEmpty fa.UnknownParentWrites) && writesStructure fb then
                               fa.UnknownParentWrites
                           else
                               Set.empty)
                          (if not (Set.isEmpty fb.UnknownParentWrites) && writesStructure fa then
                               fb.UnknownParentWrites
                           else
                               Set.empty)

                  //  (4) slot clash (Phase 340): a slot one side writes and the other accesses — the
                  //      SLOT, not the node, is the address, so it is reported beside the node-keyed
                  //      shapes rather than deduplicated against them.
                  let slotClash = Footprint.slotClash fa fb

                  // One shape per shared address by priority (content > position > move/remove), so an
                  // address the pair collides on is reported once, tagged with its most specific shape.
                  let c1 = concurrent
                  let c2 = Set.difference insertClash c1
                  let c3 = Set.difference (Set.difference moveRemove c1) c2

                  for addr in c1 do
                      yield
                          { Left = a
                            Right = b
                            Address = addr
                            Shape = MergeConflictShape.ConcurrentUpdate }

                  for addr in c2 do
                      yield
                          { Left = a
                            Right = b
                            Address = addr
                            Shape = MergeConflictShape.InsertPositionClash }

                  for addr in c3 do
                      yield
                          { Left = a
                            Right = b
                            Address = addr
                            Shape = MergeConflictShape.MoveVsRemove }

                  for (node, slot) in slotClash do
                      yield
                          { Left = a
                            Right = b
                            Address = node
                            Shape = MergeConflictShape.SlotClash slot } ]

    // ---- branch reconciliation (Phase 83; the delta rule Phase 300) ----
    // The mechanical FOLD half of a merge. Given the DAG, a base, and the heads: when the lanes'
    // deltas do NOT conflict (Phase 64), emit the deterministic merge script that folds them all;
    // when they DO, return Phase 64's typed report untouched, nothing applied. GP6 holds: the clean
    // path is pure structural composition, the conflicted path decides nothing.
    //
    // THE DELTA RULE (Phase 300). Until 0.33.0 each lane's delta was `between base head` and the
    // script their concatenation — so whenever two lanes' deltas overlapped, the shared history was
    // applied twice: a fast-forward (one head descends from the other), the same head named twice, and
    // a criss-cross (two merges of the same two lanes, `mergeBase` picking one of the two maximal
    // common ancestors on its tie-break) all produced a script that replayed shared ops twice, and
    // under a real footprint halted with those ops "conflicting" with themselves. Now the region above
    // the base is PARTITIONED by node id:
    //
    //   - the SHARED region: every node above the base held by two or more heads' closures. It is
    //     history the lanes agree on, so it is applied ONCE, first, in the drain order of the union;
    //   - each head's EXCLUSIVE delta: `closure(head) − closure(base) − every other head's closure`.
    //     A head that is an ancestor of another has an empty one; a head named twice is deduplicated
    //     (first occurrence kept) before anything is computed.
    //
    // Conflicts are checked between the EXCLUSIVE deltas, pairwise: shared history is not a
    // concurrent edit, and two exclusive deltas are always incomparable node for node (an ancestor of
    // a node in one head's delta is in that head's closure, so it cannot be exclusive to another).
    // The script is `shared ++ exclusive_1 ++ … ++ exclusive_n`, every node at most once, every node
    // after its parents. With a single chain per lane off one base — what `FoldConfluence.foldOnce`
    // builds — the shared region is empty and each exclusive delta is `between base head`, so the
    // script is exactly the one the old rule produced there.

    /// The partition of the region above a base (Phase 300): the SHARED ids (drain order of the
    /// union) and each deduplicated head's EXCLUSIVE ids (the same drain order, restricted).
    type private Region =
        { Shared: string list
          Exclusive: (string * string list) list }

    let private region (dag: T<'Op>) (baseId: string) (heads: string list) : Region =
        let hs = List.distinct heads
        let baseClosure = ancestorsOf dag baseId
        let closures = hs |> List.map (fun h -> h, ancestorsOf dag h)

        let owners (id: string) =
            closures |> List.sumBy (fun (_, c) -> if Set.contains id c then 1 else 0)

        let above =
            topoCoreMany dag hs
            |> fst
            |> List.filter (fun id -> not (Set.contains id baseClosure))

        { Shared = above |> List.filter (fun id -> owners id >= 2)
          Exclusive =
            closures
            |> List.map (fun (h, c) -> h, above |> List.filter (fun id -> Set.contains id c && owners id = 1)) }

    let private opsOf (dag: T<'Op>) (ids: string list) : 'Op list =
        ids |> List.map (fun id -> dag.Nodes.[id].Op)

    /// Every UNORDERED pair of deltas, i < j, checked with `conflicts` — the report both reconcilers
    /// return.
    let private interference (footprintOf: 'Op -> Footprint) (deltas: 'Op list list) : MergeConflict<'Op> list =
        let indexed = List.indexed deltas

        [ for (i, a) in indexed do
              for (j, b) in indexed do
                  if i < j then
                      yield! conflicts footprintOf a b ]

    /// Reconcile two branch heads over a base (Phase 83): non-conflicting exclusive deltas ⇒ `Ok` the
    /// merge script — the shared region above the base once, then head A's exclusive delta, then head
    /// B's (the Phase 300 delta rule, see the section comment) — one applyable sequence; any conflict ⇒
    /// `Error` the `Dag.conflicts` report of the two exclusive deltas verbatim — no partial merge (GP6).
    /// `footprintOf` is the caller's address projection, as for `conflicts`. Pure function of
    /// `(baseId, headA, headB)`; picks no winner and applies no policy — resolution stays domain-side.
    ///
    /// Where the heads share nothing above the base (two lanes forked off it) the script is
    /// `betweenOps base headA ++ betweenOps base headB`, as it always was. Where they do — `headB`
    /// descending from `headA`, `headA = headB`, a criss-cross whose `baseId` is one of two maximal
    /// common ancestors — the shared history is applied once and nothing conflicts with itself.
    let reconcile
        (footprintOf: 'Op -> Footprint)
        (dag: T<'Op>)
        (baseId: string)
        (headA: string)
        (headB: string)
        : Result<'Op list, MergeConflict<'Op> list> =
        let r = region dag baseId [ headA; headB ]
        let deltas = r.Exclusive |> List.map (snd >> opsOf dag)

        match interference footprintOf deltas with
        | [] -> Ok(opsOf dag r.Shared @ List.concat deltas)
        | cs -> Error cs

    // ---- N-lane reconciliation (Phase 100; the lanes-apply test Phase 300) ----
    // `reconcile` folds TWO heads. A local-first deployment routinely converges N concurrent lanes
    // (one per writer/session) off one shared base, and folding them by repeated pairwise reconcile
    // is not the same operation: it would have to mint intermediate merge nodes, and the *order* in
    // which those pairings happen would leak into the result. `reconcileMany` states the N-lane fold
    // directly — every unordered lane pair is checked, then the whole set is composed at once — so
    // the arrival order of the lanes is canonical form and never semantics.
    //
    // Since Phase 300 it also tests, BEFORE composing anything, that every lane applies on its own
    // from the state the shared region reaches. Without that test a lane set with a rejecting lane was
    // outside the fold theorem and production did not refuse it either: lanes `[[Dec 5]; [Inc 10]]`
    // off 0 folded under one arrival order and rejected under the other, because whether `Dec 5`
    // rejects depends on whether `Inc 10` ran first. A lane's own replay is a property of the lane, so
    // the set of rejecting lanes is a property of the lane set — and the refusal is order-free.

    /// The fold `reconcileMany` and `reconcileManyWith` share (Phase 289): steps 2 to 5 below, over a
    /// region already partitioned — from the node map, or from a reachability index.
    let private reconcileRegion
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (footprintOf: 'Op -> Footprint)
        (dag: T<'Op>)
        (r: Region)
        (baseState: 'State)
        : Result<'Op list, ReconcileFault<'Op, 'Rej>> =
        let deltas = r.Exclusive |> List.map (fun (h, ids) -> h, ids, opsOf dag ids)

        let rec replayIds (st: 'State) (ids: string list) : Result<'State, string * 'Rej> =
            match ids with
            | [] -> Ok st
            | id :: rest ->
                match w.Apply dag.Nodes.[id].Op st with
                | Ok st' -> replayIds st' rest
                | Error e -> Error(id, e)

        match interference footprintOf (deltas |> List.map (fun (_, _, ops) -> ops)) with
        | _ :: _ as cs -> Error(ReconcileFault.LanesInterfere cs)
        | [] ->
            match replayIds baseState r.Shared with
            | Error(nodeId, rej) -> Error(ReconcileFault.SharedHistoryRejected(nodeId, rej))
            | Ok sharedState ->
                let rejected =
                    deltas
                    |> List.choose (fun (h, ids, ops) ->
                        match replayIds sharedState ids with
                        | Ok _ -> None
                        | Error(nodeId, rej) ->
                            Some
                                { Head = h
                                  Delta = ops
                                  NodeId = nodeId
                                  Reject = rej })
                    |> List.sortWith (fun a b -> System.String.CompareOrdinal(a.Head, b.Head))

                match rejected with
                | [] -> Ok(opsOf dag r.Shared @ (deltas |> List.collect (fun (_, _, ops) -> ops)))
                | rs -> Error(ReconcileFault.LanesRejected rs)

    /// The N-lane generalisation of `reconcile` (Phase 100), made total over rejecting lanes (Phase 300):
    /// fold `heads` — N branch heads over `baseId`, whose state is `baseState` — into a single merge
    /// script, or refuse with a `ReconcileFault` that names the same thing under every arrival order:
    ///
    ///  1. the region above the base is partitioned exactly as `reconcile` partitions it (heads
    ///     deduplicated, shared history once, one exclusive delta per head);
    ///  2. every UNORDERED pair of exclusive deltas is checked with `conflicts`; any interference ⇒
    ///     `Error(LanesInterfere report)`, **nothing applied** (GP6);
    ///  3. the shared region is replayed from `baseState` through `w.Apply`; a rejection ⇒
    ///     `Error(SharedHistoryRejected …)`;
    ///  4. every exclusive delta is replayed ON ITS OWN from the state step 3 reached; any that rejects
    ///     ⇒ `Error(LanesRejected …)`, every rejecting lane, sorted by head id;
    ///  5. otherwise `Ok(shared ++ exclusive_1 ++ … ++ exclusive_n)`, in the order `heads` names them.
    ///
    /// At N = 2, when both lanes apply, the script is `reconcile`'s. `Ok` still carries #78's promise
    /// that the exclusive deltas provably commute; with step 4 it also carries the premise the fold
    /// theorem used to ASSUME (`lanes_apply`): every lane applies from the base. The `heads` order
    /// therefore pins **canonical form only** — `DagFold.fold_confluence_total` proves the outcome
    /// equivalent under every permutation, and `Conformance.FoldConfluence.laneFoldLaws` certifies it
    /// for a domain's own witness. Pairwise, not joint: set disjointness IS pairwise, so N
    /// mutually-independent lanes are jointly independent.
    let reconcileMany
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (footprintOf: 'Op -> Footprint)
        (dag: T<'Op>)
        (baseId: string)
        (baseState: 'State)
        (heads: string list)
        : Result<'Op list, ReconcileFault<'Op, 'Rej>> =
        reconcileRegion w footprintOf dag (region dag baseId heads) baseState

    // ---- the reachability index (Phase 289) ----
    // Every graph query above recomputes from the node map: `ancestorsOf` walks the parents on each
    // call, `tryTopoOrder` / `tryReplayTo` / `between` re-drain a head's closure, `mergeBase` takes
    // two closures and one more per common ancestor, and `reconcileMany` one per head. Each is
    // O(closure) per call, which is fine for one call and quadratic for a loop of them. `Reach`
    // builds the answers once per load and answers the same questions from what it holds. It is an
    // ADDITIONAL way to ask: every function above keeps its signature and its answer, and the laws
    // (`Conformance.reachLaws`) pin each indexed answer equal to the unindexed one.
    //
    // The representation, and why (DECISIONS.md, the Phase 289 entry):
    //   - a SLOT per orderable node, and the whole DAG's drain ORDER — the same Kahn drain the
    //     per-head functions run, smallest ordinal id first, over the whole node set. The drain of a
    //     down-closed node set is the whole drain restricted to it (a node's readiness depends only on
    //     its ancestors, and a node outside the set never unlocks one inside it), so one head's order,
    //     a union of heads' order and a branch delta's order are each a filter of this one array;
    //   - per slot, the ANCESTOR BITSET over slots, self included. A node's ancestors always hold
    //     smaller slots, so the set for slot s needs only s+1 bits: N²/64 words over the index, about
    //     1.6 MB at 5,000 nodes and 156 MB at 50,000 — a bound stated for the shapes measured, not a
    //     general claim;
    //   - per slot, the closure SIZE, which is `mergeBase`'s ranking key.
    //
    // Words are 32-bit `int`s, so the arithmetic is the same under Fable as under .NET.
    //
    // A node the drain cannot order — on a cycle, or below one, which only a hand-built or
    // unverified load can hold — gets no slot, and every question that touches one is answered by the
    // unindexed function itself, so the index is exact on every DAG `fromJsonl` can produce. An
    // orderable node's ancestors are all orderable, so the fallback never reaches a question about
    // orderable nodes alone.

    /// A reachability index over one DAG (Phase 289): built by `Reach.ofDag`, extended by
    /// `appendIndexed` / `mergeIndexed`, and asked through the `Reach` module. Immutable — an
    /// extension copies what it changes and shares the rest. It carries the DAG it indexes, so an
    /// index cannot be asked about a different DAG than the one it was built for. No equality: two
    /// indexes over one DAG may number its nodes differently and answer every question alike, and
    /// THAT is the equality the laws state.
    [<NoEquality; NoComparison>]
    type Reach<'Op> =
        private
            {
                /// The DAG this index answers for.
                Graph: T<'Op>
                /// Orderable node id -> slot.
                Slot: Map<string, int>
                /// Slot -> node id.
                Ids: string array
                /// Slot -> ancestor bitset over slots, self included; `s / 32 + 1` words for slot `s`.
                Bits: int array array
                /// Slot -> closure size.
                Count: int array
                /// Drain position -> slot: the whole DAG's drain, smallest ordinal id first.
                Order: int array
                /// Slot -> drain position.
                Pos: int array
                /// Ids a node names as a parent that the DAG does not hold. A node minted under one of
                /// them is not a leaf of the old drain, so extending onto it rebuilds.
                Dangling: Set<string>
            }

    /// Asking the reachability index (Phase 289). Each answer equals the unindexed function's, on
    /// every DAG — `Conformance.reachLaws` holds them equal.
    [<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
    module Reach =

        let private hasBit (bits: int array) (j: int) : bool =
            let w = j >>> 5
            w < bits.Length && ((bits.[w] >>> (j &&& 31)) &&& 1) = 1

        let private popCount (bits: int array) : int =
            let mutable c = 0

            for w in bits do
                let mutable v = w

                while v <> 0 do
                    v <- v &&& (v - 1)
                    c <- c + 1

            c

        /// The ancestor bitset of a node at slot `s` whose parents hold the bitsets `parentBits`.
        let private closureBits (parentBits: int array list) (s: int) : int array =
            let own = Array.zeroCreate<int> ((s >>> 5) + 1)

            for pb in parentBits do
                for k in 0 .. pb.Length - 1 do
                    own.[k] <- own.[k] ||| pb.[k]

            own.[s >>> 5] <- own.[s >>> 5] ||| (1 <<< (s &&& 31))
            own

        /// Build the index over `dag` in one traversal: one drain of the whole node set, each node's
        /// bitset the union of its parents'. O(N log N) for the drain and O(N²/32) word operations for
        /// the bitsets.
        let ofDag (dag: T<'Op>) : Reach<'Op> =
            let present (p: string) = dag.Nodes.ContainsKey p

            // in-degree over HELD parents, with multiplicity — the per-head drain's own count
            let mutable indeg: Map<string, int> = Map.empty
            let mutable children: Map<string, string list> = Map.empty
            let mutable dangling: Set<string> = Set.empty

            for KeyValue(id, n) in dag.Nodes do
                let held = n.Parents |> List.filter present
                indeg <- Map.add id (List.length held) indeg

                for p in n.Parents do
                    if present p then
                        children <- Map.add p (id :: (Map.tryFind p children |> Option.defaultValue [])) children
                    else
                        dangling <- Set.add p dangling

            let mutable ready =
                indeg
                |> Map.toSeq
                |> Seq.filter (fun (_, d) -> d = 0)
                |> Seq.map fst
                |> Set.ofSeq

            let ids = ResizeArray<string>()
            let bits = ResizeArray<int array>()
            let mutable slot: Map<string, int> = Map.empty

            while not (Set.isEmpty ready) do
                let id = Set.minElement ready
                ready <- Set.remove id ready
                let s = ids.Count
                let slotNow = slot

                let parentBits =
                    dag.Nodes.[id].Parents
                    |> List.choose (fun p -> Map.tryFind p slotNow |> Option.map (fun ps -> bits.[ps]))

                ids.Add id
                bits.Add(closureBits parentBits s)
                slot <- Map.add id s slot

                match Map.tryFind id children with
                | Some kids ->
                    for k in kids do
                        let d = indeg.[k] - 1
                        indeg <- Map.add k d indeg

                        if d = 0 then
                            ready <- Set.add k ready
                | None -> ()

            let bitsArr = bits.ToArray()
            let n = ids.Count

            { Graph = dag
              Slot = slot
              Ids = ids.ToArray()
              Bits = bitsArr
              Count = bitsArr |> Array.map popCount
              Order = Array.init n (fun t -> t)
              Pos = Array.init n (fun j -> j)
              Dangling = dangling }

        /// The DAG the index answers for.
        let dag (reach: Reach<'Op>) : T<'Op> = reach.Graph

        /// `Dag.ancestorsOf`, from the index: `id` itself and all its transitive parents the DAG
        /// holds; empty for an id the DAG does not hold.
        let ancestors (reach: Reach<'Op>) (id: string) : Set<string> =
            match Map.tryFind id reach.Slot with
            | Some s ->
                let b = reach.Bits.[s]
                let acc = ResizeArray<string>()

                for j in 0..s do
                    if hasBit b j then
                        acc.Add reach.Ids.[j]

                Set.ofSeq acc
            | None -> ancestorsOf reach.Graph id

        /// Is `ancestor` in `descendant`'s ancestor closure — `Set.contains ancestor (Dag.ancestorsOf
        /// dag descendant)`, so a node reaches itself and nothing reaches an id the DAG does not hold.
        /// One bit test where both are orderable.
        let reaches (reach: Reach<'Op>) (ancestor: string) (descendant: string) : bool =
            match Map.tryFind descendant reach.Slot with
            | Some sd ->
                match Map.tryFind ancestor reach.Slot with
                | Some sa -> hasBit reach.Bits.[sd] sa
                | None -> false
            | None -> Set.contains ancestor (ancestorsOf reach.Graph descendant)

        /// The drain restricted to one slot's closure: every orderable id in it, in order.
        let private closureInOrder (reach: Reach<'Op>) (s: int) (keep: int -> bool) : string list =
            let b = reach.Bits.[s]

            [ for t in 0 .. reach.Pos.[s] do
                  let j = reach.Order.[t]

                  if hasBit b j && keep j then
                      yield reach.Ids.[j] ]

        /// `Dag.tryTopoOrder`, from the index: `Ok` the head's closure in the deterministic
        /// topological order (`Ok []` for a head the DAG does not hold), or the cyclic-history error.
        let tryTopoOrder (reach: Reach<'Op>) (headId: string) : Result<string list, string> =
            match Map.tryFind headId reach.Slot with
            | Some s -> Ok(closureInOrder reach s (fun _ -> true))
            | None -> tryTopoOrder reach.Graph headId

        /// `Dag.mergeBase`, from the index: the common ancestor with the largest closure, tie-broken by
        /// id; `None` when the histories are disjoint or either id is not in the DAG.
        let mergeBase (reach: Reach<'Op>) (left: string) (right: string) : string option =
            if not (reach.Graph.Nodes.ContainsKey left && reach.Graph.Nodes.ContainsKey right) then
                None
            else
                match Map.tryFind left reach.Slot, Map.tryFind right reach.Slot with
                | Some sl, Some sr ->
                    let bl = reach.Bits.[sl]
                    let br = reach.Bits.[sr]
                    let mutable best = -1

                    for j in 0 .. min sl sr do
                        if hasBit bl j && hasBit br j then
                            if
                                best < 0
                                || reach.Count.[j] > reach.Count.[best]
                                || (reach.Count.[j] = reach.Count.[best]
                                    && System.String.CompareOrdinal(reach.Ids.[j], reach.Ids.[best]) > 0)
                            then
                                best <- j

                    if best < 0 then None else Some reach.Ids.[best]
                | _ -> mergeBase reach.Graph left right

        /// `Dag.between`, from the index: the nodes in `head`'s closure and not in `baseId`'s, in
        /// topological order; `[]` for a head the DAG does not hold.
        let between (reach: Reach<'Op>) (baseId: string) (head: string) : DagNode<'Op> list =
            let headSlot = Map.tryFind head reach.Slot
            let baseSlot = Map.tryFind baseId reach.Slot

            match headSlot with
            | Some sh when baseSlot.IsSome || not (reach.Graph.Nodes.ContainsKey baseId) ->
                let notInBase =
                    match baseSlot with
                    | Some sb -> fun j -> not (hasBit reach.Bits.[sb] j)
                    | None -> fun _ -> true

                closureInOrder reach sh notInBase |> List.map (fun id -> reach.Graph.Nodes.[id])
            | _ -> between reach.Graph baseId head

        /// The index for `dag'`, which is `reach`'s DAG with node `id` added (or already held) — the
        /// step `appendIndexed` / `mergeIndexed` take. The new node is a leaf, so the drain of the
        /// other nodes is unchanged: it enters at the first position after its last parent whose
        /// node's id is ordinally larger than its own (the drain takes the smallest ready id, and a
        /// leaf unlocks nothing), and its bitset is its parents' union. O(N) — the arrays are copied,
        /// the bitsets shared. A node under an unorderable parent is unorderable; a node minted under
        /// an id some held node names as a dangling parent is not a leaf, and the index is rebuilt.
        let internal extend (reach: Reach<'Op>) (dag': T<'Op>) (id: string) : Reach<'Op> =
            if reach.Graph.Nodes.ContainsKey id then
                { reach with Graph = dag' }
            elif reach.Dangling.Contains id then
                ofDag dag'
            else
                let node = dag'.Nodes.[id]
                let parentSlots = node.Parents |> List.map (fun p -> Map.tryFind p reach.Slot)

                if parentSlots |> List.exists Option.isNone then
                    { reach with Graph = dag' }
                else
                    let ps = parentSlots |> List.choose (fun p -> p)
                    let n = reach.Ids.Length
                    let s = n
                    let after = ps |> List.fold (fun acc p -> max acc reach.Pos.[p]) -1

                    let mutable q = after + 1

                    while q < n && System.String.CompareOrdinal(id, reach.Ids.[reach.Order.[q]]) > 0 do
                        q <- q + 1

                    let own = closureBits (ps |> List.map (fun p -> reach.Bits.[p])) s

                    { Graph = dag'
                      Slot = Map.add id s reach.Slot
                      Ids = Array.append reach.Ids [| id |]
                      Bits = Array.append reach.Bits [| own |]
                      Count = Array.append reach.Count [| popCount own |]
                      Order =
                        Array.init (n + 1) (fun t ->
                            if t < q then reach.Order.[t]
                            elif t = q then s
                            else reach.Order.[t - 1])
                      Pos =
                        Array.init (n + 1) (fun j ->
                            if j = s then q
                            elif reach.Pos.[j] >= q then reach.Pos.[j] + 1
                            else reach.Pos.[j])
                      Dangling = reach.Dangling }

    /// `append`, extending an index with the new node (Phase 289): `Ok(id, dag', reach')` where
    /// `(id, dag')` is exactly `append`'s answer on `Reach.dag reach` and `reach'` answers every
    /// question as `Reach.ofDag dag'` does, at O(N) instead of a rebuild. Refusals are `append`'s.
    let appendIndexed
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (parentId: string)
        (reach: Reach<'Op>)
        : Result<string * T<'Op> * Reach<'Op>, DagAppendFault> =
        match append hashFn w actor op parentId reach.Graph with
        | Error f -> Error f
        | Ok(id, dag') -> Ok(id, dag', Reach.extend reach dag' id)

    /// `merge`, extending an index with the merge node (Phase 289) — `appendIndexed`'s contract for
    /// the other way a node enters the DAG, so a session that appends and merges in a loop never
    /// rebuilds. Refusals are `merge`'s.
    let mergeIndexed
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (leftId: string)
        (rightId: string)
        (reach: Reach<'Op>)
        : Result<string * T<'Op> * Reach<'Op>, DagAppendFault> =
        match merge hashFn w actor op leftId rightId reach.Graph with
        | Error f -> Error f
        | Ok(id, dag') -> Ok(id, dag', Reach.extend reach dag' id)

    /// `tryReplayTo` over an index (Phase 289): the same answer on `Reach.dag reach`, with the head's
    /// order read from the index instead of drained from the node map. Takes the index where
    /// `tryReplayTo` takes the DAG, so the DAG replayed is the one the index was built for.
    let tryReplayToWith
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (reach: Reach<'Op>)
        (headId: string)
        : Result<'State, ReplayFault<'Rej>> =
        if not (reach.Graph.Nodes.ContainsKey headId) then
            Error(ReplayFault.UnknownHead headId)
        elif not (reach.Slot.ContainsKey headId) then
            // on or below a cycle: the unindexed replay names the cyclic history
            tryReplayTo w state0 reach.Graph headId
        else
            match Reach.tryTopoOrder reach headId with
            | Ok order ->
                let rec go st =
                    function
                    | [] -> Ok st
                    | id :: rest ->
                        match w.Apply reach.Graph.Nodes.[id].Op st with
                        | Ok st' -> go st' rest
                        | Error e -> Error(ReplayFault.Rejected(id, e))

                go state0 order
            | Error _ -> tryReplayTo w state0 reach.Graph headId

    /// `reconcileMany` over an index (Phase 289): the same answer on `Reach.dag reach`, with the
    /// region above the base partitioned from the index — the base's and each head's closure a bitset,
    /// the union's order a filter of the index's drain — instead of N+1 closure walks and a drain.
    /// Takes the index where `reconcileMany` takes the DAG.
    let reconcileManyWith
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (footprintOf: 'Op -> Footprint)
        (reach: Reach<'Op>)
        (baseId: string)
        (baseState: 'State)
        (heads: string list)
        : Result<'Op list, ReconcileFault<'Op, 'Rej>> =
        let dag = reach.Graph

        let unorderable (id: string) =
            dag.Nodes.ContainsKey id && not (reach.Slot.ContainsKey id)

        if unorderable baseId || List.exists unorderable heads then
            reconcileMany w footprintOf dag baseId baseState heads
        else
            let has (s: int option) (j: int) =
                match s with
                | Some s -> j <= s && ((reach.Bits.[s].[j >>> 5] >>> (j &&& 31)) &&& 1) = 1
                | None -> false

            let hs = List.distinct heads
            let baseSlot = Map.tryFind baseId reach.Slot
            let closures = hs |> List.map (fun h -> h, Map.tryFind h reach.Slot)

            let owners (j: int) =
                closures |> List.sumBy (fun (_, c) -> if has c j then 1 else 0)

            let last =
                closures
                |> List.fold
                    (fun acc (_, c) -> max acc (c |> Option.map (fun s -> reach.Pos.[s]) |> Option.defaultValue -1))
                    -1

            let above =
                [ for t in 0..last do
                      let j = reach.Order.[t]

                      if not (has baseSlot j) then
                          let o = owners j

                          if o > 0 then
                              yield j, o ]

            let r =
                { Shared =
                    above
                    |> List.filter (fun (_, o) -> o >= 2)
                    |> List.map (fun (j, _) -> reach.Ids.[j])
                  Exclusive =
                    closures
                    |> List.map (fun (h, c) ->
                        h,
                        above
                        |> List.filter (fun (j, o) -> o = 1 && has c j)
                        |> List.map (fun (j, _) -> reach.Ids.[j])) }

            reconcileRegion w footprintOf dag r baseState

    // ---- checkpoints (Phase 288) ----
    // The linear stream has had a checkpoint since Phase 244 — a `Snapshot` sealed into the chain, a
    // replay that resumes from it, a compaction that truncates behind it. This is the DAG's: a state at
    // a node, sealed with the SAME pre-image (a checkpoint at node N is the strict snapshot at sequence
    // zero of the history that begins at N, chained from N's content id the way a linear snapshot chains
    // from its boundary record's hash, reached through `OpStream.Snapshots` rather than copied), a replay
    // that folds only the history above it, and a DAG that BEGINS at one.
    //
    // The checkpoint travels BESIDE the DAG, as a snapshot travels beside its tail: `T<'Op>` gains no
    // field, so no record literal a consumer wrote stops compiling and no existing answer moves. A DAG
    // compacted at a checkpoint keeps the checkpoint's own node, so it lives on — an append onto that node
    // is an ordinary append, the node is the head of an empty history above it, and the reachability
    // index builds over the compacted DAG unchanged. Core says what a checkpoint is and how a history
    // verifies and replays through one; when to take one stays the domain's call (GP6).
    //
    // ONE condition is new, and it is a property of the replay order rather than of this module: the
    // drain folds a head's closure smallest-id-first, so a branch that left BEFORE the checkpoint's node
    // and merges after it is interleaved with the checkpoint's own closure. No state at the checkpoint can
    // stand for that prefix, so such a node is refused by name (`Uncovered`) wherever it would be folded
    // after the checkpoint — never folded in an order the full replay would not use.

    /// A checkpoint on the lane DAG (Phase 288): `State` is the fold of `Node`'s ancestor closure — the
    /// node's own op included, the state `tryReplayTo` reaches at `Node` — and `Hash` seals the two
    /// together. The seal is the linear `Snapshot`'s strict hash of `State` at sequence zero with `Node`
    /// as its boundary hash, so a changed state, a changed node id or a changed seal each fail
    /// `verifyCheckpoint`. The linear `Snapshot<'State>`'s counterpart on the DAG.
    type Checkpoint<'State> =
        { Node: string
          State: 'State
          Hash: string }

    /// Why a checkpoint could not be taken, a DAG not compacted at one, or a replay from one not run
    /// (Phase 288).
    [<RequireQualifiedAccess>]
    type CheckpointFault<'Rej> =
        /// The node named is not a node of the DAG — the checkpoint's own, or the one a checkpoint was
        /// asked to be taken at.
        | UnknownNode of nodeId: string
        /// The replay itself failed: the node's closure when a checkpoint is taken from the initial state,
        /// the history above the checkpoint when one is replayed from. `tryReplayTo`'s fault, verbatim.
        | Replay of ReplayFault<'Rej>
        /// The head's history does not hold the checkpoint's node, so the checkpoint says nothing about it.
        | Unreached of headId: string
        /// A node folded after the checkpoint that does not descend from the checkpoint's node — a branch
        /// that left before it and merges after it — or, for a compaction, a node of the DAG that is
        /// neither at or behind the checkpoint's node nor after it. The first such node, in replay order
        /// for a replay and in id order for a compaction.
        | Uncovered of nodeId: string

    /// Where a checkpoint, or a DAG that begins at one, fails to verify (Phase 288) — the
    /// `SnapshotBreak` of the DAG.
    [<RequireQualifiedAccess>]
    type CheckpointBreak =
        /// The checkpoint's node is not a node of the DAG.
        | UnknownNode of nodeId: string
        /// The seal does not recompute from the carried node and state: the recomputed seal, the carried one.
        | Seal of expected: string * got: string
        /// A node's own integrity fault, exactly as `firstBreak` reports it: a tampered node, or a parent
        /// the DAG does not hold where the history may not be truncated — after the checkpoint's node.
        | Node of DagBreak
        /// A node that is neither at or behind the checkpoint's node nor descends from it: history the
        /// checkpoint does not cover (a second root, a branch from before the checkpoint).
        | Uncovered of nodeId: string

    /// The seal of `state` at `node`, computed by `OpStream.Snapshots`' own verifier: handed the strict
    /// snapshot at sequence zero whose boundary hash is `node` and whose hash is still empty, it names the
    /// hash that snapshot should carry. An empty tail has no chain to break, so the snapshot's own hash is
    /// the only break it can report, and `None` means that hash IS the empty string.
    let private sealOf
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (node: string)
        (state: 'State)
        : string =
        let unsealed: Snapshot<'State> =
            { Seq = 0
              State = state
              PrevHash = node
              Hash = ""
              Mode = SnapshotMode.Strict }

        match OpStream.Snapshots.firstBreak OpStream.canonicalConfig hashFn stateEncode w unsealed [] with
        | Some(SnapshotBreak.SnapshotHash(expected, _)) -> expected
        | Some(SnapshotBreak.Tail _)
        | None -> ""

    /// The checkpoint as the linear snapshot it is sealed as — its sidecar line is that snapshot's line.
    let private asSnapshot (cp: Checkpoint<'State>) : Snapshot<'State> =
        { Seq = 0
          State = cp.State
          PrevHash = cp.Node
          Hash = cp.Hash
          Mode = SnapshotMode.Strict }

    /// Seal `state` at `nodeId` WITHOUT replaying anything (Phase 288) — the genesis-import shape, where a
    /// converted history begins at a node whose state was translated from elsewhere rather than folded
    /// from its op. The domain vouches for the state; the seal makes it tamper-evident from here on. Use
    /// `checkpointAt` / `checkpointFrom` wherever the state CAN be folded.
    let sealAt
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (nodeId: string)
        (state: 'State)
        : Checkpoint<'State> =
        { Node = nodeId
          State = state
          Hash = sealOf hashFn stateEncode w nodeId state }

    /// Take a checkpoint at `nodeId` (Phase 288): fold the node's ancestor closure from `state0` exactly
    /// as `tryReplayTo` does, and seal the state it reaches. Refused by name: a node the DAG does not hold
    /// (`UnknownNode`), and a closure that does not replay (`Replay`, the replay's own fault). The state is
    /// folded, so a checkpoint taken here is the replay's answer by construction; it is the SEAL that a
    /// later reader checks, so verify the DAG (`verifyDag`) before checkpointing it.
    let checkpointAt
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (dag: T<'Op>)
        (nodeId: string)
        : Result<Checkpoint<'State>, CheckpointFault<'Rej>> =
        if not (dag.Nodes.ContainsKey nodeId) then
            Error(CheckpointFault.UnknownNode nodeId)
        else
            match tryReplayTo w state0 dag nodeId with
            | Error f -> Error(CheckpointFault.Replay f)
            | Ok st -> Ok(sealAt hashFn stateEncode w nodeId st)

    /// Recompute a checkpoint's seal from the state it carries (Phase 288). `Error UnknownNode` for a
    /// checkpoint whose node the DAG does not hold, `Error Seal(expected, got)` for one whose seal does
    /// not recompute — a changed state, node id or seal. Like the linear snapshot's hash this binds the
    /// state to the node; it does not re-fold the history (which a compacted DAG no longer holds), so a
    /// checkpoint is exactly as trustworthy as the act that sealed it.
    let verifyCheckpoint
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (cp: Checkpoint<'State>)
        (dag: T<'Op>)
        : Result<unit, CheckpointBreak> =
        if not (dag.Nodes.ContainsKey cp.Node) then
            Error(CheckpointBreak.UnknownNode cp.Node)
        else
            let expected = sealOf hashFn stateEncode w cp.Node cp.State

            if expected <> cp.Hash then
                Error(CheckpointBreak.Seal(expected, cp.Hash))
            else
                Ok()

    /// The ids in `order` outside `behind` — the history above a checkpoint, in replay order — or the
    /// first of them that does not descend from `node`. `order` is topological, so a node's parents are
    /// judged before it: it descends from `node` exactly when one of its parents IS `node` or did.
    let private above
        (dag: T<'Op>)
        (node: string)
        (order: string list)
        (behind: Set<string>)
        : Result<string list, string> =
        let delta = order |> List.filter (fun id -> not (Set.contains id behind))

        let rec go (desc: Set<string>) =
            function
            | [] -> Ok delta
            | (id: string) :: rest ->
                if dag.Nodes.[id].Parents |> List.exists (fun p -> p = node || Set.contains p desc) then
                    go (Set.add id desc) rest
                else
                    Error id

        go Set.empty delta

    /// The replay both `replayFrom` forms share, over the head's order and the checkpoint's closure as
    /// one of them computes them.
    let private replayAbove
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (cp: Checkpoint<'State>)
        (dag: T<'Op>)
        (headId: string)
        (order: unit -> Result<string list, string>)
        (behind: unit -> Set<string>)
        : Result<'State, CheckpointFault<'Rej>> =
        if not (dag.Nodes.ContainsKey cp.Node) then
            Error(CheckpointFault.UnknownNode cp.Node)
        elif not (dag.Nodes.ContainsKey headId) then
            Error(CheckpointFault.Replay(ReplayFault.UnknownHead headId))
        else
            match order () with
            | Error _ -> Error(CheckpointFault.Replay(ReplayFault.CyclicHistory headId))
            | Ok ids when not (List.contains cp.Node ids) -> Error(CheckpointFault.Unreached headId)
            | Ok ids ->
                match above dag cp.Node ids (behind ()) with
                | Error id -> Error(CheckpointFault.Uncovered id)
                | Ok delta ->
                    let rec go st =
                        function
                        | [] -> Ok st
                        | (id: string) :: rest ->
                            match w.Apply dag.Nodes.[id].Op st with
                            | Ok st' -> go st' rest
                            | Error e -> Error(CheckpointFault.Replay(ReplayFault.Rejected(id, e)))

                    go cp.State delta

    /// Bounded replay from a checkpoint (Phase 288): fold only the history above the checkpoint's node —
    /// `betweenOps dag cp.Node headId`, in that order — over `cp.State`. On the full DAG and on one
    /// compacted at the checkpoint alike, for every head it does not refuse, the answer is
    /// `tryReplayTo w state0 dag headId`'s (`Conformance.checkpointLaws`). Refused by name, in order: the
    /// checkpoint's node absent (`UnknownNode`), the head absent (`Replay UnknownHead`), a cyclic history
    /// (`Replay CyclicHistory`), a head whose history does not hold the checkpoint's node (`Unreached`), a
    /// node above the checkpoint that does not descend from its node (`Uncovered` — the full replay folds
    /// it BEFORE part of the checkpoint's closure, so resuming from the checkpoint would fold it in an order
    /// the full replay does not use), and a domain rejection (`Replay Rejected`, the node the full replay
    /// would name). The seal is not checked here — `verifyCheckpoint` is that act.
    let replayFrom
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (cp: Checkpoint<'State>)
        (dag: T<'Op>)
        (headId: string)
        : Result<'State, CheckpointFault<'Rej>> =
        replayAbove w cp dag headId (fun () -> tryTopoOrder dag headId) (fun () -> ancestorsOf dag cp.Node)

    /// `replayFrom` over a reachability index (Phase 288, on Phase 289's `Reach`): the same answer on
    /// `Reach.dag reach`, with the head's order and the checkpoint's closure read from the index — so on a
    /// full history the walk the replay saves is not paid back in closure walks.
    let replayFromWith
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (cp: Checkpoint<'State>)
        (reach: Reach<'Op>)
        (headId: string)
        : Result<'State, CheckpointFault<'Rej>> =
        replayAbove w cp reach.Graph headId (fun () -> Reach.tryTopoOrder reach headId) (fun () ->
            Reach.ancestors reach cp.Node)

    /// Take a checkpoint at `nodeId` from an EARLIER checkpoint (Phase 288): `replayFrom w origin dag
    /// nodeId`, sealed — how a DAG that begins at a checkpoint takes its next one, since its history
    /// behind `origin` is gone and `checkpointAt` would fold from the wrong start. Refusals are
    /// `replayFrom`'s, with an absent `nodeId` named as `UnknownNode` as `checkpointAt` names it.
    let checkpointFrom
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (origin: Checkpoint<'State>)
        (dag: T<'Op>)
        (nodeId: string)
        : Result<Checkpoint<'State>, CheckpointFault<'Rej>> =
        if dag.Nodes.ContainsKey origin.Node && not (dag.Nodes.ContainsKey nodeId) then
            Error(CheckpointFault.UnknownNode nodeId)
        else
            replayFrom w origin dag nodeId
            |> Result.map (fun st -> sealAt hashFn stateEncode w nodeId st)

    /// Every node reachable from `seeds` along child edges, the seeds included, keeping to `within`.
    let private forwardFrom (dag: T<'Op>) (within: string -> bool) (seeds: string list) : Set<string> =
        let children =
            dag.Nodes
            |> Map.fold
                (fun (m: Map<string, string list>) id n ->
                    n.Parents
                    |> List.fold (fun m p -> Map.add p (id :: (Map.tryFind p m |> Option.defaultValue [])) m) m)
                Map.empty

        let rec go (acc: Set<string>) (stack: string list) =
            match stack with
            | [] -> acc
            | id :: rest ->
                if Set.contains id acc || not (within id) then
                    go acc rest
                else
                    go (Set.add id acc) ((Map.tryFind id children |> Option.defaultValue []) @ rest)

        go Set.empty seeds

    /// The strict descendants of `node` the DAG holds.
    let private after (dag: T<'Op>) (node: string) : Set<string> =
        forwardFrom dag (fun _ -> true) [ node ] |> Set.remove node

    /// The DAG a compaction at `node` keeps, or the first node (id order) it would strand. Kept: the node
    /// itself, everything after it, and the BAND — every node behind it on a path from a "side parent" (a
    /// node behind the checkpoint that a node after it names as a parent, other than the checkpoint's own
    /// node) to it. Keeping the band means no node after the checkpoint ever names a parent the compacted
    /// DAG does not hold, so `firstBreakFrom` can refuse every missing parent after the checkpoint, and the
    /// checkpoint's closure in the compacted DAG still holds every side parent, so the replay above it is
    /// the full DAG's. On the usual shape — nothing after the checkpoint reaches behind it except through
    /// it — the band is empty.
    let private cut (dag: T<'Op>) (node: string) : Result<T<'Op>, string> =
        let behind = ancestorsOf dag node
        let later = after dag node

        match
            dag.Nodes
            |> Map.toList
            |> List.tryFind (fun (id, _) -> not (Set.contains id behind) && not (Set.contains id later))
        with
        | Some(id, _) -> Error id
        | None ->
            let side =
                later
                |> Set.toList
                |> List.collect (fun id -> dag.Nodes.[id].Parents)
                |> List.filter (fun p -> p <> node && Set.contains p behind)
                |> List.distinct

            let band = forwardFrom dag (fun id -> Set.contains id behind) side
            let keep = Set.unionMany [ band; later; Set.singleton node ]

            Ok
                { dag with
                    Nodes = dag.Nodes |> Map.filter (fun id _ -> Set.contains id keep) }

    /// Compact the DAG at `nodeId` (Phase 288): `(checkpoint, compacted)`, the checkpoint `checkpointAt`
    /// takes and the DAG truncated behind its node — which it KEEPS, so the compacted DAG lives on: append
    /// onto the node as onto any node, and an empty history above it has the node as its head. Every
    /// replay `replayFrom` answers on the compacted DAG is the full DAG's, and `verifyDagFrom` verifies it
    /// (`Conformance.checkpointLaws`). Refused by name: `checkpointAt`'s refusals, and `Uncovered` for a
    /// node that is neither at or behind `nodeId` nor after it — history the checkpoint cannot stand for.
    ///
    /// **Verify, then compact** — the linear `compact`'s rule, for the same reason: the closure behind the
    /// node is folded and discarded, and once it is gone nothing can find a tamper in it again.
    let compactAt
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (dag: T<'Op>)
        (nodeId: string)
        : Result<Checkpoint<'State> * T<'Op>, CheckpointFault<'Rej>> =
        checkpointAt hashFn stateEncode w state0 dag nodeId
        |> Result.bind (fun cp ->
            cut dag nodeId
            |> Result.map (fun d -> cp, d)
            |> Result.mapError CheckpointFault.Uncovered)

    /// Compact again, from the checkpoint a DAG already begins at (Phase 288): `checkpointFrom`, then the
    /// same cut — so a compacted DAG is compacted later exactly as the full DAG would be at that node.
    let compactFrom
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (origin: Checkpoint<'State>)
        (dag: T<'Op>)
        (nodeId: string)
        : Result<Checkpoint<'State> * T<'Op>, CheckpointFault<'Rej>> =
        checkpointFrom hashFn stateEncode w origin dag nodeId
        |> Result.bind (fun cp ->
            cut dag nodeId
            |> Result.map (fun d -> cp, d)
            |> Result.mapError CheckpointFault.Uncovered)

    /// The first fault in a DAG that BEGINS at a checkpoint (Phase 288) — `firstBreak` with the
    /// checkpoint's node as the root whose content id the checkpoint carries. In order: the checkpoint
    /// (`verifyCheckpoint`'s breaks), then each node in id order — a content id that does not recompute
    /// (`Node`, `ContentIdMismatch`); after the checkpoint's node, a parent the DAG does not hold (`Node`,
    /// `MissingParent`), since nothing after the checkpoint may be truncated; and a node neither at or
    /// behind the checkpoint's node nor after it (`Uncovered`). Behind the checkpoint's node a missing
    /// parent is the truncation, and is not a fault. `None` for an intact history: a DAG `compactAt`
    /// produced, every append onto it, and the full DAG itself when every node is at, behind or after the
    /// checkpoint's node.
    let firstBreakFrom
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (cp: Checkpoint<'State>)
        (dag: T<'Op>)
        : CheckpointBreak option =
        match verifyCheckpoint hashFn stateEncode w cp dag with
        | Error b -> Some b
        | Ok() ->
            let behind = ancestorsOf dag cp.Node
            let later = after dag cp.Node

            dag.Nodes
            |> Map.toList
            |> List.tryPick (fun (id, n) ->
                let h = nodeHash hashFn w.Encode n.Parents n.Actor n.Op

                if id <> h then
                    Some(
                        CheckpointBreak.Node
                            { NodeId = id
                              Reason = DagBreakReason.ContentIdMismatch
                              Expected = h
                              Got = id }
                    )
                elif Set.contains id later then
                    n.Parents
                    |> List.tryFind (fun p -> not (dag.Nodes.ContainsKey p))
                    |> Option.map (fun missing ->
                        CheckpointBreak.Node
                            { NodeId = id
                              Reason = DagBreakReason.MissingParent
                              Expected = ""
                              Got = missing })
                elif Set.contains id behind then
                    None
                else
                    Some(CheckpointBreak.Uncovered id))

    /// Verify a DAG that begins at a checkpoint (Phase 288): `firstBreakFrom … |> Option.isNone`. The
    /// counterpart of `verifyDag` for a truncated history — a changed byte in a node it keeps, or in the
    /// checkpoint, fails it.
    let verifyDagFrom
        (hashFn: HashFn)
        (stateEncode: 'State -> string)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (cp: Checkpoint<'State>)
        (dag: T<'Op>)
        : bool =
        firstBreakFrom hashFn stateEncode w cp dag |> Option.isNone

    // ---- the checkpoint sidecar (Phase 288) ----
    // Checkpoints never enter a lane file: the lane is `toJsonl`'s bytes, so every content hash and every
    // file written before this phase reads exactly as it did. They ride a SIDECAR beside it, one line per
    // checkpoint, and the line is the linear snapshot line of the snapshot the checkpoint is sealed as:
    // `{"snapshot":true,"seq":0,"state":<state>,"prevHash":<node id>,"hash":<seal>}` (DECISIONS.md, the
    // Phase 288 entry). A reader refuses any other sequence and a chain-only line, since a checkpoint's
    // seal always binds its state.

    /// One sidecar line, read through the one scanner.
    let private checkpointOf
        (stateDecode: string -> Result<'State, string>)
        (line: JsonlLine)
        : Result<Checkpoint<'State>, JsonlFault> =
        let refuse = OpStream.Jsonl.refuse line

        match OpStream.Jsonl.tryRawField "snapshot" line, OpStream.Jsonl.tryRawField "stateHashed" line with
        | Some "true", (None | Some "true") ->
            OpStream.Jsonl.intField "seq" line
            |> Result.bind (fun seq ->
                if seq <> 0 then
                    Error(refuse (sprintf "a checkpoint is the snapshot at seq 0; this line is at seq %d" seq))
                else
                    OpStream.Jsonl.rawField "state" line)
            |> Result.bind (fun raw ->
                OpStream.Jsonl.stringField "prevHash" line
                |> Result.bind (fun node ->
                    OpStream.Jsonl.stringField "hash" line
                    |> Result.bind (fun hash ->
                        if node = "" then
                            Error(refuse "a checkpoint names its node in prevHash, and this one is empty")
                        else
                            stateDecode raw
                            |> Result.mapError refuse
                            |> Result.map (fun st -> { Node = node; State = st; Hash = hash }))))
        | Some "true", Some _ -> Error(refuse "a checkpoint's seal binds its state; a chain-only line is not one")
        | _ -> Error(refuse "not a checkpoint line: a checkpoint is a snapshot line (\"snapshot\":true)")

    /// Write a DAG and its checkpoints (Phase 288): `(lane, sidecar)`. The lane is `toJsonl encode dag`,
    /// byte for byte; the sidecar is one line per checkpoint, in the order given, each the linear snapshot
    /// line (`OpStream.Snapshots.toJsonl`) of the snapshot the checkpoint is sealed as. No checkpoints, an
    /// empty sidecar.
    let toJsonlWithCheckpoints
        (encode: 'Op -> string)
        (stateEncode: 'State -> string)
        (dag: T<'Op>)
        (checkpoints: Checkpoint<'State> list)
        : string * string =
        toJsonl encode dag,
        checkpoints
        |> List.map (fun cp -> OpStream.Snapshots.toJsonl stateEncode (asSnapshot cp))
        |> String.concat "\n"

    /// `toJsonlWithCheckpoints` through the checked writers (Phase 301): the lane by `tryToJsonl`, each
    /// sidecar line by `OpStream.Snapshots.tryToJsonl` — a refused state is the `Error` by its 1-based
    /// SIDECAR line, member `state`. On `Ok` both texts are `toJsonlWithCheckpoints`'s, byte for byte.
    let tryToJsonlWithCheckpoints
        (encode: 'Op -> string)
        (stateEncode: 'State -> string)
        (dag: T<'Op>)
        (checkpoints: Checkpoint<'State> list)
        : Result<string * string, JsonlWriteFault> =
        tryToJsonl encode dag
        |> Result.bind (fun lane ->
            let rec go (i: int) (acc: string list) =
                function
                | [] -> Ok(lane, acc |> List.rev |> String.concat "\n")
                | (cp: Checkpoint<'State>) :: rest ->
                    match OpStream.Snapshots.tryToJsonl stateEncode (asSnapshot cp) with
                    | Error f -> Error { f with Line = i + 1 }
                    | Ok line -> go (i + 1) (line :: acc) rest

            go 0 [] checkpoints)

    /// Read a DAG and its optional checkpoint sidecar (Phase 288). The lane is read by `fromJsonl`, so a
    /// DAG file with NO sidecar (`None`) reads exactly as `fromJsonl` reads it, with no checkpoints. A
    /// sidecar is read through the one scanner, a line per checkpoint, in file order; a line that is not a
    /// strict snapshot line at sequence zero with a node in `prevHash`, or whose state `stateDecode`
    /// refuses, is an `Error` reading `checkpoint sidecar: line N: <reason> (position P)`. Structural only,
    /// as `fromJsonl` is: verify with `verifyCheckpoint` / `verifyDagFrom` before trusting either.
    let fromJsonlWithCheckpoints
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (stateDecode: string -> Result<'State, string>)
        (text: string)
        (sidecar: string option)
        : Result<T<'Op> * Checkpoint<'State> list, string> =
        fromJsonl w text
        |> Result.bind (fun dag ->
            match sidecar with
            | None -> Ok(dag, [])
            | Some side ->
                OpStream.Jsonl.scanRecords (checkpointOf stateDecode) side
                |> Result.map (fun cps -> dag, cps)
                |> Result.mapError (fun f -> "checkpoint sidecar: " + JsonlFault.toString f))
