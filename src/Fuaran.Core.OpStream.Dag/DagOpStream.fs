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
/// `[parent]` for a linear/fork step, and two or more heads for a merge (`merge` writes
/// `[left; right]`; `appendOn` / `mergeAll` write any number, Phase 311).
type DagNode<'Op> =
    {
        /// The content id: `Dag.nodeId` of this node's parents, actor and op. `firstBreak` recomputes it,
        /// so a node whose fields were edited is caught as `ContentIdMismatch`.
        Id: string
        /// Parent ids in the order the author gave them — the id is hashed over them sorted, so the order
        /// changes no id. A repeated parent is kept as given.
        Parents: string list
        /// Who wrote the node; part of the content id, so re-attribution changes `Id`.
        Actor: Actor
        /// The domain op the node records. The DAG never applies it on write; replay does.
        Op: 'Op
    }

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
    {
        /// The id the faulty node is stored under — the earliest faulty node in topological order.
        NodeId: string
        /// Which check failed; the content id is checked before the parents.
        Reason: DagBreakReason
        /// For `ContentIdMismatch`, the recomputed content id; for `MissingParent`, `""`.
        Expected: string
        /// For `ContentIdMismatch`, the stored id; for `MissingParent`, the first parent id the DAG does
        /// not hold.
        Got: string
    }

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
    /// Both ops touch the same node's content, at least one writing it. Takes priority: an address
    /// tagged here is not also tagged `InsertPositionClash` or `MoveVsRemove`.
    | ConcurrentUpdate
    /// Both ops write the child list of the same named parent; `Address` is that parent.
    | InsertPositionClash
    /// One op removes or moves a node from a parent the script cannot name while the other writes any
    /// structure; `Address` is the removed or moved id. Conservative — it may fire on ops that commute.
    | MoveVsRemove
    /// Both ops access `slot` of the node at `Address`, at least one writing it. Reported beside the
    /// node-keyed shapes, never deduplicated against them.
    | SlotClash of slot: string

/// One enumerated merge interference (Phase 64): the two ops (`Left` from delta A, `Right` from
/// delta B) that both target `Address`, and the `Shape` of their collision. Detection only —
/// `Dag.conflicts` decides nothing, applies nothing, and picks no winner (GP6); the domain's own
/// reconciliation consumes this report. Generic over the opaque `'Op` — no `comparison` and no
/// witness field are demanded (GP2).
type MergeConflict<'Op> =
    {
        /// The op from the first delta passed to `conflicts`.
        Left: 'Op
        /// The op from the second delta passed to `conflicts`.
        Right: 'Op
        /// The footprint address the two collide on — a node id, or a parent id for
        /// `InsertPositionClash`. An op pair yields one conflict per address it collides on, plus one per
        /// clashing slot.
        Address: string
        /// How the two collide at `Address`.
        Shape: MergeConflictShape
    }

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

    /// One English sentence naming the fault and, where it has one, the offending id in double quotes.
    /// For a log or an exception message; there is no parser back.
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
    /// A graph refusal, judged before the op is applied; the DAG is unchanged.
    | Fault of DagAppendFault
    /// The graph accepted the node but `Apply` refused the op at the caller's state; the node is not
    /// added.
    | Domain of 'Rej

/// One lane an N-lane reconcile refused because the lane's own delta does not apply (Phase 300): the
/// lane's head, its delta (the exclusive region `Dag.reconcileMany` would have folded), and the first
/// node of that delta the domain rejected, with the rejection. A property of the lane alone — it is
/// replayed on its own, never after another lane — so the set of these is arrival-order-invariant.
type LaneRejection<'Op, 'Rej> =
    {
        /// The lane's head id, as named to `reconcileMany`; rejections are sorted ordinally by it.
        Head: string
        /// Every op of the lane's exclusive delta, in replay order — not only those before the rejection.
        Delta: 'Op list
        /// The first node of the delta whose op the domain rejected, replayed from the shared state.
        NodeId: string
        /// The domain's rejection of that node's op.
        Reject: 'Rej
    }

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
    ///
    /// OPAQUE since `1.0.0` (Phase 386): the constructor is private, so a DAG is built through this
    /// module — `empty`, `append` and its kin, the readers, or `ofNodes`, which refuses a map whose
    /// key and node id disagree. While the record was public, `{ Nodes = … }` could file a node under
    /// a key that was not its id, and every walk that looks a parent up by id then read a different
    /// node from the one the map enumerated. `Nodes` is still read freely.
    type T<'Op> =
        private
            { NodeMap: Map<string, DagNode<'Op>> }

        /// Every node, keyed by its own `Id`. The id is the CLAIM `firstBreak` checks against the
        /// node's content; that every key equals its node's id is what `ofNodes` and every builder
        /// here guarantee.
        member this.Nodes: Map<string, DagNode<'Op>> = this.NodeMap

    /// A map key whose node carries a different `Id` — what `ofNodes` refuses.
    type KeyMismatch =
        {
            /// The key the node was filed under.
            Key: string
            /// The id the node itself carries.
            NodeId: string
        }

    /// The DAG with no nodes: no heads, and `append` with parent `""` adds its first (genesis) node.
    let empty: T<'Op> = { NodeMap = Map.empty }

    /// A DAG over `nodes`. Refused with the first key (in ordinal key order) whose node carries a
    /// different `Id`; a node whose id does not match its CONTENT is admitted, because that is the
    /// integrity question `firstBreak` and `verifyDag` answer, and a loaded or hand-built DAG is
    /// exactly what they are asked about.
    let ofNodes (nodes: Map<string, DagNode<'Op>>) : Result<T<'Op>, KeyMismatch> =
        nodes
        |> Map.toSeq
        |> Seq.tryFind (fun (key, node) -> key <> node.Id)
        |> function
            | Some(key, node) -> Error { Key = key; NodeId = node.Id }
            | None -> Ok { NodeMap = nodes }

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
    ///
    /// `nodeHashAs` is the same pre-image with the actor spelled by `actorText` (Phase 360), so the
    /// profiled forms (`nodeIdWith`, `firstBreakWith`, `rehashEncoding`) share this one definition.
    let private nodeHashAs
        (actorText: Actor -> string)
        (hashFn: HashFn)
        (encode: 'Op -> string)
        (parents: string list)
        (actor: Actor)
        (op: 'Op)
        : string =
        let sorted =
            parents |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))

        hashFn (String.concat "," sorted) (actorText actor + "|" + encode op)

    let private nodeHash
        (hashFn: HashFn)
        (encode: 'Op -> string)
        (parents: string list)
        (actor: Actor)
        (op: 'Op)
        : string =
        nodeHashAs Actor.encode hashFn encode parents actor op

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
    let private addNodeAs
        (id: string)
        (encode: 'Op -> string)
        (actor: Actor)
        (op: 'Op)
        (parents: string list)
        (dag: T<'Op>)
        : Result<string * T<'Op>, DagAppendFault> =
        match Map.tryFind id dag.Nodes with
        | Some existing when sameNode encode existing parents actor op -> Ok(id, dag)
        | Some _ -> Error(DagAppendFault.ContentIdCollision id)
        | None ->
            let node =
                { Id = id
                  Parents = parents
                  Actor = actor
                  Op = op }

            Ok(id, { NodeMap = Map.add id node dag.Nodes })

    /// `addNodeAs` at the id `nodeHash` mints.
    let private addNode
        (hashFn: HashFn)
        (encode: 'Op -> string)
        (actor: Actor)
        (op: 'Op)
        (parents: string list)
        (dag: T<'Op>)
        : Result<string * T<'Op>, DagAppendFault> =
        addNodeAs (nodeHash hashFn encode parents actor op) encode actor op parents dag

    /// The content id a node of (`parents`, `actor`, `op`) has (Phase 311) — `nodeHash`, public, so a
    /// consumer that names a node before (or without) building it computes the id this module mints
    /// instead of re-deriving the pre-image: `hashFn (sorted parents joined by ",") (Actor.encode
    /// actor + "|" + encode op)`, the parents sorted ordinally, so the id is a function of the parent
    /// SET's order-free spelling. `encode` is the witness's `Encode`. `firstBreak` and `verifyDag`
    /// recompute ids through this same function.
    let nodeId (hashFn: HashFn) (encode: 'Op -> string) (parents: string list) (actor: Actor) (op: 'Op) : string =
        nodeHash hashFn encode parents actor op

    /// `nodeId` under a named encoding profile (Phase 360): the same pre-image with the actor spelled
    /// as `profile` spells it (`OpStream.encodeActorWith`). `nodeIdWith OpStream.EncodingProfile.V2` is
    /// `nodeId`; `V1` is the id `0.30.0` minted. The op's bytes are `encode`'s, so a store pinned to
    /// `V1` passes an encoder that renders through `Json.renderWith EncodingProfile.V1` — one
    /// declaration, both halves of the pre-image.
    let nodeIdWith
        (profile: OpStream.EncodingProfile)
        (hashFn: HashFn)
        (encode: 'Op -> string)
        (parents: string list)
        (actor: Actor)
        (op: 'Op)
        : string =
        nodeHashAs (OpStream.encodeActorWith profile) hashFn encode parents actor op

    /// Append `op` as a child of EVERY id in `parents` (Phase 311) — the N-parent constructor `append`
    /// (one parent) and `merge` (two) are the cases of. `[]` is a genesis node. The parents are stored
    /// in the order given (the content id sorts them, so the order changes no id) and are judged in
    /// that order, the first refusal returned: `EmptyParentId` for `""` (here a parent id, never the
    /// genesis marker — genesis is the empty list), `CommaInParentId`, `UnknownParent`, and
    /// `ContentIdCollision` exactly as for `append`. A parent named twice is kept twice, as `merge x x`
    /// keeps it; `mergeAll` is the form that deduplicates. `appendOn [ p ]` is `append p` for a
    /// non-empty `p`, and `appendOn [ l; r ]` is `merge l r`, byte for byte — both are written over it.
    let appendOn
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (parents: string list)
        (dag: T<'Op>)
        : Result<string * T<'Op>, DagAppendFault> =
        let judge (p: string) =
            if p = "" then
                Some DagAppendFault.EmptyParentId
            else
                knownParent dag p

        match parents |> List.tryPick judge with
        | Some f -> Error f
        | None -> addNode hashFn w.Encode actor op parents dag

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
            appendOn hashFn w actor op [ parentId ] dag

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
        appendOn hashFn w actor op [ leftId; rightId ] dag

    /// Converge a set of heads in ONE node (Phase 311): `appendOn` over `heads` deduplicated and
    /// sorted ordinally, so the node a set of heads converges in — its id AND its stored parents — is
    /// a function of the head SET, never of the order the heads arrived in. Two replicas converging
    /// the same heads mint the same node, and an `appendOn` over the same heads in any order mints the
    /// same id (`proofs/Chain.fst`, `append_on_is_merge_all`). `[]` is a genesis node and one distinct
    /// head an append onto it; the refusals are `appendOn`'s, judged in the sorted order. Where a
    /// consumer folded N heads into N-1 binary merges, this is the one node those merges stood for:
    /// the history below it is the same, in the same drain order (`Conformance.laneLaws`).
    let mergeAll
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (heads: string list)
        (dag: T<'Op>)
        : Result<string * T<'Op>, DagAppendFault> =
        appendOn hashFn w actor op (heads |> List.distinct |> ordinalSort) dag

    /// Record a merge of `leftId` and `rightId` whose reconciliation is a SCRIPT rather than one op
    /// (Phase 311): the merge node carries the script's first op, and each further op is appended in
    /// order onto the node before it, so the history a replay of the last node folds is both parents'
    /// closures and then the script, op by op — what a merge whose op were a `Batch` of the script
    /// would apply, recorded without the domain needing a `Batch` case. Returns the ids recorded, in
    /// order (the last is the new head). The parents are judged as `merge` judges them whatever the
    /// script; an EMPTY script then records nothing and returns `Ok([], dag)` — a merge node needs an op,
    /// and a convergence with nothing to record is `merge` with the domain's own no-op. Each recorded
    /// node is an ordinary node: an identical one already held deduplicates, a colliding one is refused
    /// (`ContentIdCollision`) and nothing is recorded.
    let mergeWith
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (script: 'Op list)
        (leftId: string)
        (rightId: string)
        (dag: T<'Op>)
        : Result<string list * T<'Op>, DagAppendFault> =
        let judge (p: string) =
            if p = "" then
                Some DagAppendFault.EmptyParentId
            else
                knownParent dag p

        match [ leftId; rightId ] |> List.tryPick judge, script with
        | Some f, _ -> Error f
        | None, [] -> Ok([], dag)
        | None, first :: rest ->
            match merge hashFn w actor first leftId rightId dag with
            | Error f -> Error f
            | Ok(m, d) ->
                let rec go (acc: string list) (tip: string) (d: T<'Op>) =
                    function
                    | [] -> Ok(List.rev acc, d)
                    | (op: 'Op) :: more ->
                        match append hashFn w actor op tip d with
                        | Error f -> Error f
                        | Ok(id, d') -> go (id :: acc) id d' more

                go [ m ] m d rest

    /// What a write that APPLIES its op answers (Phase 410; a positional triple before): the state
    /// after the op, the node that recorded it, and the DAG holding that node. The answer of
    /// `appendChecked`, `mergeChecked`, `appendIf` and the four verified forms.
    type CheckedAppend<'State, 'Op> =
        {
            /// The state after the op: the caller's state with the op applied.
            State: 'State
            /// The id of the node that recorded the op — an identical node already held, when the
            /// write deduplicated.
            Id: string
            /// The DAG holding that node.
            Dag: T<'Op>
        }

    /// `append` that also APPLIES the op (Phase 296): `state` is the state `parentId`'s closure
    /// replays to — the caller's, exactly as `OpStream.append` takes the stream's current state — and
    /// an op the domain rejects there is refused before it enters the DAG, instead of surfacing at
    /// replay. Answers the state after the op beside the node. The graph refusals are judged first.
    let appendChecked
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (parentId: string)
        (dag: T<'Op>)
        : Result<CheckedAppend<'State, 'Op>, DagAppendRejection<'Rej>> =
        match append hashFn w actor op parentId dag with
        | Error f -> Error(DagAppendRejection.Fault f)
        | Ok(id, dag') ->
            match w.Apply op state with
            | Ok state' -> Ok { State = state'; Id = id; Dag = dag' }
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
        : Result<CheckedAppend<'State, 'Op>, DagAppendRejection<'Rej>> =
        match merge hashFn w actor op leftId rightId dag with
        | Error f -> Error(DagAppendRejection.Fault f)
        | Ok(id, dag') ->
            match w.Apply op state with
            | Ok state' -> Ok { State = state'; Id = id; Dag = dag' }
            | Error rej -> Error(DagAppendRejection.Domain rej)

    /// Why `Dag.appendIf` refused (Phase 311) — `AppendRejection<'Rej>`'s shape on the DAG: the head
    /// set moved (`StaleHeads`, both sets named, each sorted ordinally), a structural `Fault`, or the
    /// domain's rejection of the op at the state the caller holds for the expected heads.
    [<RequireQualifiedAccess>]
    type DagAppendIfRejection<'Rej> =
        /// The DAG's head set is not the caller's: `expected` deduplicated, both sorted ordinally.
        /// Checked first; nothing is built or applied.
        | StaleHeads of expected: string list * actual: string list
        /// The heads matched but the node was refused structurally, before the op was applied.
        | Fault of DagAppendFault
        /// The node was sound but `Apply` refused the op at the caller's state; nothing is added.
        | Domain of 'Rej

    /// Compare-and-append on the DAG (Phase 311) — `OpStream.appendIf`'s guard, over the HEAD SET: append
    /// `op` onto every one of `expectedHeads` (`mergeAll`'s node: one head is an append, several a
    /// convergence, none a genesis) ONLY if they are exactly the DAG's current heads, compared as sets.
    /// Otherwise `StaleHeads(expected, actual)` and nothing changes — another writer added a node since
    /// the caller read the heads. On a match the op is applied at `state`, the state the caller holds
    /// for those heads (`appendChecked`'s discipline), so the positional check is the head set and the
    /// domain check one `Apply`: nothing is replayed, where the alternative — re-deriving the state by
    /// replaying the whole union before each write — pays the history on every append. A caller that
    /// cannot vouch for `state` replays first (`replayAllBy`, `tryReplayTo`), once, outside the loop.
    /// Graph refusals are judged before the op is applied.
    let appendIf
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (expectedHeads: string list)
        (actor: Actor)
        (op: 'Op)
        (state: 'State)
        (dag: T<'Op>)
        : Result<CheckedAppend<'State, 'Op>, DagAppendIfRejection<'Rej>> =
        let expected = expectedHeads |> List.distinct |> ordinalSort
        let actual = heads dag |> ordinalSort

        if expected <> actual then
            Error(DagAppendIfRejection.StaleHeads(expected, actual))
        else
            match mergeAll hashFn w actor op expected dag with
            | Error f -> Error(DagAppendIfRejection.Fault f)
            | Ok(id, dag') ->
                match w.Apply op state with
                | Ok state' -> Ok { State = state'; Id = id; Dag = dag' }
                | Error rej -> Error(DagAppendIfRejection.Domain rej)

    /// The ancestor closure of `roots` — each root plus all its transitive parents the DAG holds; a
    /// root or parent it does not hold contributes nothing. The one closure walk (Phase 388):
    /// `ancestorsOf` is its one-root case and `topoCoreMany` drains it. An explicit work-list (Phase
    /// 10), so a long ancestor chain cannot overflow the stack. Tail-recursive.
    let private closureOf (dag: T<'Op>) (roots: string list) : Set<string> =
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

        collect Set.empty roots

    /// The Kahn drain over a node set (Phase 311; any set since Phase 388, the whole DAG's by `drainBy`): `(placed, unplaced)`. The ready frontier is
    /// ordered by `(key node, id)` — the caller's key first, the ordinal id breaking every tie, so the
    /// order is total whatever the key — and drained smallest first; a parent the DAG does not hold
    /// constrains nothing (the fold path's policy, `topoCore`'s). In-degrees count a parent named twice
    /// twice, as `Reach.ofDag` does. Nodes on or below a cycle never become ready: they are `unplaced`,
    /// in id order. Iterative — no recursion over the graph's depth. With a constant key the placed
    /// order is the drain `Reach.ofDag` numbers its slots in, and on a down-closed node set it is every
    /// per-head drain restricted (DagFold section 13).
    /// Over a DOWN-CLOSED set (an ancestor closure) a held parent is a parent in the set, so the drain
    /// of the set is the drain of the sub-DAG it names (`topoCoreMany`).
    let private drainNodes (key: DagNode<'Op> -> 'K) (nodes: Map<string, DagNode<'Op>>) : string list * string list =
        let mutable indeg: Map<string, int> = Map.empty
        let mutable children: Map<string, string list> = Map.empty

        for KeyValue(id, n) in nodes do
            let held = n.Parents |> List.filter nodes.ContainsKey
            indeg <- Map.add id (List.length held) indeg

            for p in held do
                children <- Map.add p (id :: (Map.tryFind p children |> Option.defaultValue [])) children

        let mutable ready: Set<'K * string> =
            indeg
            |> Map.toSeq
            |> Seq.filter (fun (_, d) -> d = 0)
            |> Seq.map (fun (id, _) -> key nodes[id], id)
            |> Set.ofSeq

        let placed = ResizeArray<string>()

        while not (Set.isEmpty ready) do
            let top = Set.minElement ready
            ready <- Set.remove top ready
            let id = snd top
            placed.Add id

            match Map.tryFind id children with
            | Some kids ->
                for k in kids do
                    let d = indeg[k] - 1
                    indeg <- Map.add k d indeg

                    if d = 0 then
                        ready <- Set.add (key nodes[k], k) ready
            | None -> ()

        let placedSet = Set.ofSeq placed

        List.ofSeq placed,
        nodes
        |> Map.toList
        |> List.map fst
        |> List.filter (fun id -> not (Set.contains id placedSet))

    /// `drainNodes` over the whole DAG.
    let private drainBy (key: DagNode<'Op> -> 'K) (dag: T<'Op>) : string list * string list = drainNodes key dag.Nodes

    /// `firstBreak` with the actor spelled by `actorText` in each recomputed id (Phase 360).
    let private firstBreakAs
        (actorText: Actor -> string)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (dag: T<'Op>)
        : DagBreak option =
        let placed, unplaced = drainBy (fun _ -> 0) dag

        placed @ unplaced
        |> List.map (fun id -> id, dag.Nodes[id])
        |> List.tryPick (fun (id, n) ->
            let h = nodeHashAs actorText hashFn w.Encode n.Parents n.Actor n.Op

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

    /// The first integrity fault in the DAG (Phase 21): a node whose stored id is not the content hash
    /// of its (parents, actor, op) — a tampered node — or a node naming a parent the DAG does not
    /// contain. `None` for an intact DAG. Localises what `verifyDag` only reports as a boolean, so
    /// `fromJsonlVerified` / an operator can say *where*.
    ///
    /// **The scan is TOPOLOGICAL since Phase 311** — the whole DAG's drain (`drainBy`, smallest id first
    /// among the ready nodes), then any node on or below a cycle in id order — so where a DAG holds
    /// several faults the one named is the EARLIEST in its history, never whichever id sorts first. It
    /// was the id order until `0.34.0`; which nodes are faulty, and `verifyDag`'s verdict, are unchanged.
    let firstBreak (hashFn: HashFn) (w: StreamWitness<'Op, 'State, 'Rej>) (dag: T<'Op>) : DagBreak option =
        firstBreakAs Actor.encode hashFn w dag

    /// `firstBreak` under a named encoding profile (Phase 360): every id recomputed through `nodeIdWith
    /// profile`, so a DAG whose ids were minted under `profile` — `0.30.0`'s, for `V1` — verifies for
    /// what it is. `w.Encode` must render the op as the store declares too (`Json.renderWith`).
    /// `firstBreakWith OpStream.EncodingProfile.V2` is `firstBreak`.
    let firstBreakWith
        (profile: OpStream.EncodingProfile)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (dag: T<'Op>)
        : DagBreak option =
        firstBreakAs (OpStream.encodeActorWith profile) hashFn w dag

    /// Verify the DAG: every node's id is the content hash of its (parents, actor, op), and
    /// every parent exists — generalises the linear `verifyChain` to a DAG. Re-expressed over
    /// `firstBreak` (Phase 21) so the two share one definition of integrity.
    let verifyDag (hashFn: HashFn) (w: StreamWitness<'Op, 'State, 'Rej>) (dag: T<'Op>) : bool =
        firstBreak hashFn w dag |> Option.isNone

    /// `verifyDag` under a named encoding profile (Phase 360) — `firstBreakWith profile` finds nothing.
    let verifyDagWith
        (profile: OpStream.EncodingProfile)
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (dag: T<'Op>)
        : bool =
        firstBreakWith profile hashFn w dag |> Option.isNone

    /// The Kahn topological-sort core: returns the emitted order **and** the head's ancestor-closure.
    /// On an acyclic closure `List.length order = Set.count anc`; a cycle leaves the cyclic nodes
    /// unreachable so `order` is strictly shorter — the signal `tryTopoOrder` / `isAcyclic` use.
    ///
    /// Since Phase 300 the drain runs over the UNION of several heads' closures (`topoCoreMany`); one
    /// head is the one-root case, byte-for-byte the drain it always was. The drain is a function of the
    /// node SET it covers, so the order the roots are named in cannot reach the output.
    let private topoCoreMany (dag: T<'Op>) (roots: string list) : string list * Set<string> =
        // The closure's drain is `drainNodes` restricted to the closure (Phase 388): one Kahn body,
        // smallest id first among the ready nodes — the order this function always emitted, now off
        // an ordered set rather than a list re-sorted on every insert. Cyclic nodes stay unplaced, so
        // `order` is strictly shorter than the closure exactly when the closure is cyclic.
        let anc = closureOf dag roots

        let placed, _ =
            drainNodes (fun _ -> 0) (dag.Nodes |> Map.filter (fun id _ -> Set.contains id anc))

        placed, anc

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
        /// The head's closure does not drain to a total order — a hand-built or tampered DAG. Where
        /// several roots were replayed, the first root whose own closure is cyclic. Nothing is applied.
        | CyclicHistory of headId: string
        /// The domain refused the op of `nodeId`, the first rejecting node in replay order; the ops
        /// before it were applied, but no partial state is returned.
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

    // ---- the whole-DAG total order (Phase 311) ----
    // Every function above orders ONE head's closure (`tryTopoOrder`, `tryReplayTo`), or a union of
    // heads' closures above a base (`reconcileMany`). A store of several writers' lanes needs the order
    // of the WHOLE union — what a clone folds on load — and consumers drew it three ways: the drain at
    // the smallest id, a sort by (Lamport rank, lane, id), and a drain at (domain rank, id). Each is the
    // same drain at a different key, so Core exposes the drain with the key as a parameter: the order is
    // a linear extension of the parent relation whatever the key (`DagFold.total_order_by_is_a_linear_
    // extension`), a function of the node SET (the drain's determinism, section 13), and a consumer with
    // its own key passes it. The lane store's default key is `laneKey` (a `LaneKey`: lane, seq, id), below.

    /// Why a whole-DAG order could not be drawn (Phase 311): nodes on or below a cycle, which no drain
    /// places — every one of them, in id order. Only a hand-built or unverified load can hold one.
    [<RequireQualifiedAccess>]
    type TotalOrderFault =
        /// Every node the drain could not place — on or below a cycle — in id order.
        | Cyclic of unplaced: string list

    /// Why `replayAllBy` / `replayAll` refused (Phase 311): the union has no total order (`Cyclic`, the
    /// unplaced nodes in id order), or the domain rejected a node's op (`Rejected`, the first in the
    /// order, with the rejection).
    [<RequireQualifiedAccess>]
    type ReplayAllFault<'Rej> =
        /// The whole DAG has no total order: `TotalOrderFault.Cyclic`'s unplaced nodes. Nothing is applied.
        | Cyclic of unplaced: string list
        /// The domain refused the op of `nodeId`, the first rejecting node in the key's order.
        | Rejected of nodeId: string * reject: 'Rej

    /// The whole DAG in one total order (Phase 311): the Kahn drain over EVERY node, the ready frontier
    /// taken smallest `(key node, id)` first — the key the caller's, the ordinal id breaking every tie,
    /// so the order is total for any key, including one that is constant. Parents first, always: the
    /// key chooses only among nodes that are ready. A parent the DAG does not hold constrains nothing (a
    /// compacted or partial load still orders). `Error(TotalOrderFault.Cyclic unplaced)` when a cycle
    /// leaves nodes the drain cannot place. Iterative, O(N log N) comparisons of keys.
    ///
    /// A constant key is the smallest-id drain — the order `Reach.ofDag` numbers its slots in, and on
    /// one head's closure exactly `tryTopoOrder`'s. A consumer with its own order passes its own key:
    /// a Lamport-rank order is `totalOrderBy (fun n -> ranks[n.Id], lane n)`, a domain-rank order
    /// `totalOrderBy (fun n -> rankOf n.Op)`. The lane store's default is `totalOrder` (`laneKey`).
    let totalOrderBy (key: DagNode<'Op> -> 'K) (dag: T<'Op>) : Result<string list, TotalOrderFault> =
        match drainBy key dag with
        | placed, [] -> Ok placed
        | _, unplaced -> Error(TotalOrderFault.Cyclic unplaced)

    /// The Lamport depth of every node (Phase 311): `0` for a node with no parent the DAG holds, and
    /// otherwise one more than its deepest held parent — the longest path from a root. Computed along the
    /// whole-DAG drain, iteratively, so a history of any depth costs no stack. `Error Cyclic` exactly where
    /// `totalOrderBy` refuses.
    let ranks (dag: T<'Op>) : Result<Map<string, int>, TotalOrderFault> =
        totalOrderBy (fun _ -> 0) dag
        |> Result.map (fun order ->
            order
            |> List.fold
                (fun (acc: Map<string, int>) id ->
                    let r =
                        dag.Nodes[id].Parents
                        |> List.fold
                            (fun m p ->
                                match Map.tryFind p acc with
                                | Some rp -> max m (rp + 1)
                                | None -> m)
                            0

                    Map.add id r acc)
                Map.empty)

    /// Replay the WHOLE DAG from `state0` in `totalOrderBy key`'s order (Phase 311): every node once, its
    /// op applied after every parent's. `Error(Cyclic unplaced)` where there is no total order, and
    /// `Error(Rejected(node, rejection))` at the first node the domain refuses. Where a history's
    /// concurrent ops do not commute, the KEY decides which runs first — so a domain whose ops carry an
    /// order of their own (a kill after the fork it kills) passes a key that encodes it, and the replay
    /// is the same on every replica however its node ids happen to sort.
    let replayAllBy
        (key: DagNode<'Op> -> 'K)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (dag: T<'Op>)
        : Result<'State, ReplayAllFault<'Rej>> =
        match totalOrderBy key dag with
        | Error(TotalOrderFault.Cyclic unplaced) -> Error(ReplayAllFault.Cyclic unplaced)
        | Ok order ->
            let rec go st =
                function
                | [] -> Ok st
                | (id: string) :: rest ->
                    match w.Apply dag.Nodes[id].Op st with
                    | Ok st' -> go st' rest
                    | Error e -> Error(ReplayAllFault.Rejected(id, e))

            go state0 order

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
        (checkedForm: unit -> Result<CheckedAppend<'State, 'Op>, DagAppendRejection<'Rej>>)
        : Result<CheckedAppend<'State, 'Op>, VerifiedAppendRejection<'State, 'Rej>> =
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
        : Result<CheckedAppend<'State, 'Op>, VerifiedAppendRejection<'State, 'Rej>> =
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
        : Result<CheckedAppend<'State, 'Op>, VerifiedAppendRejection<'State, 'Rej>> =
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
        : Result<CheckedAppend<'State, 'Op>, VerifiedAppendRejection<'State, 'Rej>> =
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
        : Result<CheckedAppend<'State, 'Op>, VerifiedAppendRejection<'State, 'Rej>> =
        mergeVerifiedWith (fun a b -> a = b) hashFn w state0 actor op state leftId rightId dag

    // ---- JSONL persistence (Phase 01) ----
    // The linear OpStream round-trips to JSONL; the DAG does too, closing the persistence
    // asymmetry. Read through the linear package's one scanner (`OpStream.Jsonl`, Phase 296) and
    // spelled by its one escaper (`OpStream.Jsonl.quote`, Phase 388): this package references
    // `OpStream`, so neither is copied here, and it stays Fable-clean (Phase 241). One JSON
    // object per node; the `op` value is the witness's own Encode output embedded raw (preserved
    // byte-for-byte); nodes are emitted in id-sorted order so output is stable for a fixed DAG.

    /// One JSON object per node, in deterministic id-sorted order. The `op` is embedded as raw
    /// JSON (the witness's own Encode output), so a round-trip preserves it byte-for-byte.
    let private nodeLine (n: DagNode<'Op>) (opJson: string) : string =
        "{\"node\":true,\"id\":"
        + OpStream.Jsonl.quote n.Id
        + ",\"parents\":["
        + (n.Parents |> List.map OpStream.Jsonl.quote |> String.concat ",")
        + "],\"actor\":"
        + Actor.encode n.Actor
        + ",\"op\":"
        + opJson
        + "}"

    /// The DAG as JSONL: one `{"node":true,…}` line per node, in id order, joined by `\n` with no
    /// trailing newline — the same DAG always writes the same bytes. Unchecked: an `encode` output with
    /// a line break or surrounding whitespace is embedded as-is and will not read back; `tryToJsonl`
    /// refuses it.
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

    /// The witness a load reads through (Phase 416) — the linear reader's, which that package keeps
    /// internal: it never refuses, carrying each op's stored text beside the real witness's answer, so
    /// a decode failure stops neither the read nor the verification. A decoded op encodes by the real
    /// witness, as every check always has; an undecoded one encodes as its stored text, the bytes the
    /// writer embedded verbatim, so its content id still recomputes over exactly what was hashed.
    let private carry (w: StreamWitness<'Op, 'State, 'Rej>) : StreamWitness<string * Result<'Op, string>, unit, unit> =
        { Apply = fun _ s -> Ok s
          Encode =
            fun (raw, decoded) ->
                match decoded with
                | Ok op -> w.Encode op
                | Error _ -> raw
          Decode = fun raw -> Ok(raw, w.Decode raw) }

    /// One text's nodes, read through `carry` (Phase 296's reader, Phase 416's witness): the DAG and,
    /// for each node, the 1-based line first holding it. A repeated id with the same content is one
    /// node; with different content it is refused at its line as a content-id collision.
    let private readCarried
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<T<string * Result<'Op, string>> * Map<string, int>, JsonlFault> =
        let cw = carry w
        let bind f r = Result.bind f r

        let nodeOf (line: JsonlLine) =
            OpStream.Jsonl.stringField "id" line
            |> bind (fun id ->
                OpStream.Jsonl.stringsField "parents" line
                |> bind (fun parents ->
                    OpStream.Jsonl.actorField "actor" line
                    |> bind (fun actor ->
                        OpStream.Jsonl.rawField "op" line
                        |> bind (fun raw -> cw.Decode raw |> Result.mapError (OpStream.Jsonl.refuse line))
                        |> Result.map (fun op ->
                            line,
                            { Id = id
                              Parents = parents
                              Actor = actor
                              Op = op }))))

        text
        |> OpStream.Jsonl.scanRecords nodeOf
        |> bind (fun lines ->
            let rec go (acc: Map<string, DagNode<string * Result<'Op, string>>>) (at: Map<string, int>) =
                function
                | [] -> Ok({ NodeMap = acc }, at)
                | (line, node: DagNode<string * Result<'Op, string>>) :: rest ->
                    match Map.tryFind node.Id acc with
                    | None -> go (Map.add node.Id node acc) (Map.add node.Id (OpStream.Jsonl.lineNumber line) at) rest
                    | Some held when sameNode cw.Encode held node.Parents node.Actor node.Op -> go acc at rest
                    | Some _ ->
                        Error(
                            OpStream.Jsonl.refuse
                                line
                                ("node "
                                 + node.Id
                                 + " is already held with different content (a content-id collision)")
                        )

            go Map.empty Map.empty lines)

    /// A carried DAG decoded — or EVERY node the witness refused, in lane and then line order, each by
    /// the lane `laneOf` names (`None` for a load of one text) and the line `lineOf` names.
    let private settle
        (laneOf: string -> string option)
        (lineOf: Map<string, int>)
        (dag: T<string * Result<'Op, string>>)
        : Result<T<'Op>, StreamLoadFault<'Break>> =
        let sites =
            dag.Nodes
            |> Map.toList
            |> List.choose (fun (id, n) ->
                match snd n.Op with
                | Ok _ -> None
                | Error why ->
                    Some
                        { Lane = laneOf id
                          Line = Map.tryFind id lineOf |> Option.defaultValue 0
                          NodeId = id
                          Reason = why })
            |> List.sortWith (fun a b ->
                match System.String.CompareOrdinal(Option.defaultValue "" a.Lane, Option.defaultValue "" b.Lane) with
                | 0 -> compare a.Line b.Line
                | c -> c)

        match sites with
        | [] ->
            Ok
                { NodeMap =
                    dag.Nodes
                    |> Map.map (fun _ n ->
                        { Id = n.Id
                          Parents = n.Parents
                          Actor = n.Actor
                          Op =
                            match snd n.Op with
                            | Ok op -> op
                            | Error why -> invalidOp why }) }
        | _ -> Error(StreamLoadFault.Undecodable sites)

    /// Parse JSONL back into a DAG (the `op` raw span is handed to `w.Decode`) through the ONE
    /// JSONL scanner, `OpStream.Jsonl` (Phase 296; until then this module carried a verbatim copy).
    /// Fully portable — runs under .NET and Fable. A malformed line, a member of the wrong kind
    /// (`"id":12`, a non-string parent) or an unknown actor kind is `StreamLoadFault.Unreadable`
    /// carrying the `JsonlFault`, its line 1-based over every line of the text — never an exception
    /// (GP4). The `op` raw span is preserved byte-for-byte, so a round-trip is identical.
    ///
    /// **An op the witness refuses (Phase 416)** does not stop the read: when every line parses and
    /// only the witness refused, the answer is `StreamLoadFault.Undecodable`, naming EVERY such node by
    /// line and id — the signature of a store written by a newer host. `loadFaultToString` renders the
    /// pre-416 message.
    ///
    /// **A repeated id (Phase 296).** A line repeating a node already read deduplicates, as `append`
    /// does; a line naming an id already held for DIFFERENT content — parents modulo order, actor, or
    /// op — is refused as a content-id collision rather than replacing the node read first.
    ///
    /// **Structural only — this does NOT verify integrity, and never answers `Broken`.** Nodes are
    /// keyed by their *stored* id; a tampered id, a dangling parent, or a cycle decodes to a clean `Ok`
    /// here. Run `verifyDag` afterwards (or use `fromJsonlVerified`, Phase 13) to recompute content ids
    /// and confirm every parent exists before trusting the DAG.
    let fromJsonl (w: StreamWitness<'Op, 'State, 'Rej>) (text: string) : Result<T<'Op>, StreamLoadFault<DagBreak>> =
        match readCarried w text with
        | Error f -> Error(StreamLoadFault.Unreadable(None, f))
        | Ok(carried, lineOf) -> settle (fun _ -> None) lineOf carried

    /// `fromJsonl` + the integrity gate (Phase 13): parses structurally, then runs `firstBreak` so a
    /// tampered id, a dangling parent, or a (hash-impossible-to-forge, so id-mismatching) cycle is
    /// `StreamLoadFault.Broken` with the first `DagBreak`, instead of a silently-corrupt `Ok`.
    ///
    /// **The DAG is verified before any op must decode (Phase 416).** A node the witness cannot decode
    /// is hashed over its stored op text, so every content id and parent is checked without it; only
    /// then are the ops decoded. A break answers `Broken` whatever else is wrong; an intact DAG with
    /// ops the witness refused answers `Undecodable`, naming every one. `loadFaultToString` renders the
    /// pre-416 message, a break's included.
    let fromJsonlVerified
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<T<'Op>, StreamLoadFault<DagBreak>> =
        match readCarried w text with
        | Error f -> Error(StreamLoadFault.Unreadable(None, f))
        | Ok(carried, lineOf) ->
            match firstBreak hashFn (carry w) carried with
            | Some b -> Error(StreamLoadFault.Broken b)
            | None -> settle (fun _ -> None) lineOf carried

    /// A DAG load's fault as text (Phase 416): a break as `Dag.fromJsonlVerified: <reason> at node <id>`
    /// — the pre-0.24.0 spelling `fromJsonlVerified` answered as its `Error` string until Phase 416,
    /// byte for byte — and every other case as `StreamLoadFault.toStringWith` renders it.
    let loadFaultToString (f: StreamLoadFault<DagBreak>) : string =
        f
        |> StreamLoadFault.toStringWith (fun b ->
            sprintf "Dag.fromJsonlVerified: %s at node %s" (DagBreakReason.toString b.Reason) b.NodeId)

    // ---- merge-base / branch-delta (Phase 08) ----
    // The *generic* half of a merge: locate the divergence point of two heads and enumerate
    // one branch's delta. The reconciliation itself stays domain-side (GP6 — Core owns no
    // merge semantics); these pure graph queries inform a `merge`, they do not perform it.

    /// The ancestor-closure of `id` — `id` itself plus all its transitive parents present in
    /// the DAG. Empty for an id not in the DAG. Total.
    let ancestorsOf (dag: T<'Op>) (id: string) : Set<string> = closureOf dag [ id ]

    /// Of a non-empty DOWN-CLOSED node set (an intersection of ancestor closures), the node with the
    /// largest ancestor closure, tie-broken by id (Phase 388). Over an ACYCLIC set only a MAXIMAL
    /// member can win — a member with a descendant in the set has a strictly smaller closure than
    /// that descendant — so the maximal members are found in one closure walk (the set minus every
    /// member's strict ancestors) and only they are sized. It was a closure per member: quadratic in
    /// the shared history. A set holding a CYCLE (a hand-built or tampered DAG; the drain leaves it
    /// unplaced) is sized member by member as before, because there a closure need not grow along an
    /// edge and a cycle has no maximal member at all — so the answer is the same on every input.
    let private deepest (dag: T<'Op>) (common: Set<string>) : string =
        let candidates =
            match drainNodes (fun _ -> 0) (dag.Nodes |> Map.filter (fun id _ -> Set.contains id common)) with
            | _, [] ->
                let below =
                    closureOf dag (common |> Set.toList |> List.collect (fun id -> dag.Nodes[id].Parents))

                Set.difference common below
            | _ -> common

        candidates
        |> Set.toList
        |> List.maxBy (fun id -> Set.count (ancestorsOf dag id), id)

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
            Some(deepest dag common)

    /// The common base of N heads (Phase 311): `mergeBase`'s rule over all of them at once — of the
    /// nodes in EVERY head's ancestor closure, the one with the largest closure, tie-broken by id.
    /// `None` for no heads, or when no node is common to all of them. At two heads it is `mergeBase`,
    /// and at one head it is that head. A function of the head SET: unlike a left fold of pairwise
    /// `mergeBase` calls, whose answer on a criss-cross can depend on which pair is folded first, no
    /// intermediate base is chosen, so the order the heads are named in cannot reach the answer. A head
    /// the DAG does not hold contributes an empty closure, so the answer is `None`. Total.
    let commonBase (dag: T<'Op>) (heads: string list) : string option =
        match heads |> List.distinct with
        | [] -> None
        | h :: rest ->
            let common =
                rest
                |> List.fold (fun acc x -> Set.intersect acc (ancestorsOf dag x)) (ancestorsOf dag h)

            if Set.isEmpty common then
                None
            else
                Some(deepest dag common)

    /// The branch delta: the nodes on the region from (exclusive) `baseId` to (inclusive)
    /// `head`, in topological order — the ops a reconciler replays. Equals `head`'s
    /// ancestor-closure minus `baseId`'s. `base = head` ⇒ `[]`; an unrelated base ⇒ the whole
    /// head closure. Total — a missing id yields `[]`.
    let between (dag: T<'Op>) (baseId: string) (head: string) : DagNode<'Op> list =
        let baseClosure = ancestorsOf dag baseId

        topoOrder dag head
        |> List.filter (fun id -> not (Set.contains id baseClosure))
        |> List.map (fun id -> dag.Nodes[id])

    /// The branch delta's *ops*, in the same topological order as `between` (Phase 26) — the op
    /// sequence a domain replays through its own reducer / `OpStream` to fast-forward `baseId` to
    /// `head`. A thin projection of `between` (the reconciliation itself stays domain-side, GP6):
    /// `base = head` ⇒ `[]`; an unrelated base ⇒ the whole head closure's ops.
    let betweenOps (dag: T<'Op>) (baseId: string) (head: string) : 'Op list =
        between dag baseId head |> List.map _.Op

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
                      { Left = a
                        Right = b
                        Address = addr
                        Shape = MergeConflictShape.ConcurrentUpdate }

                  for addr in c2 do
                      { Left = a
                        Right = b
                        Address = addr
                        Shape = MergeConflictShape.InsertPositionClash }

                  for addr in c3 do
                      { Left = a
                        Right = b
                        Address = addr
                        Shape = MergeConflictShape.MoveVsRemove }

                  for (node, slot) in slotClash do
                      { Left = a
                        Right = b
                        Address = node
                        Shape = MergeConflictShape.SlotClash slot } ]

    /// `conflicts` over NODES rather than ops (Phase 311): the same report — every shape, the Phase 340
    /// slot clash included, at the same addresses, in the same order — with `Left` and `Right` the
    /// nodes whose ops collide, so a consumer reads the node ids off the report instead of building an
    /// index from op back to node. `footprintOf` is the caller's projection of the op, as for
    /// `conflicts`; it is `conflicts` at `fun n -> footprintOf n.Op`, so the two can never disagree.
    let conflictsOfNodes
        (footprintOf: 'Op -> Footprint)
        (deltaA: DagNode<'Op> list)
        (deltaB: DagNode<'Op> list)
        : MergeConflict<DagNode<'Op>> list =
        conflicts (fun (n: DagNode<'Op>) -> footprintOf n.Op) deltaA deltaB

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
        ids |> List.map (fun id -> dag.Nodes[id].Op)

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

    /// The interference of N heads over a base, naming NODES (Phase 311): the region above the base
    /// partitioned exactly as `reconcileMany` partitions it (heads deduplicated, shared history set
    /// aside, one exclusive delta per head), and `conflictsOfNodes` over every unordered pair of
    /// exclusive deltas, in the order `heads` names them. Empty exactly when `reconcileMany` would not
    /// refuse with `LanesInterfere`, and otherwise that report with each op replaced by its node — so
    /// `List.map (fun c -> c.Left.Op, …)` of it IS the `LanesInterfere` report. Nothing is applied.
    let conflictsOfHeads
        (footprintOf: 'Op -> Footprint)
        (dag: T<'Op>)
        (baseId: string)
        (heads: string list)
        : MergeConflict<DagNode<'Op>> list =
        let r = region dag baseId heads

        let deltas =
            r.Exclusive
            |> List.map (fun (_, ids) -> ids |> List.map (fun id -> dag.Nodes[id]))

        let indexed = List.indexed deltas

        [ for (i, a) in indexed do
              for (j, b) in indexed do
                  if i < j then
                      yield! conflictsOfNodes footprintOf a b ]

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
                match w.Apply dag.Nodes[id].Op st with
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
    // two closures and one more per MAXIMAL common ancestor (Phase 388), and `reconcileMany` one per head. Each is
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
            w < bits.Length && ((bits[w] >>> (j &&& 31)) &&& 1) = 1

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
                    own[k] <- own[k] ||| pb[k]

            own[s >>> 5] <- own[s >>> 5] ||| (1 <<< (s &&& 31))
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
                    dag.Nodes[id].Parents
                    |> List.choose (fun p -> Map.tryFind p slotNow |> Option.map (fun ps -> bits[ps]))

                ids.Add id
                bits.Add(closureBits parentBits s)
                slot <- Map.add id s slot

                match Map.tryFind id children with
                | Some kids ->
                    for k in kids do
                        let d = indeg[k] - 1
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
                let b = reach.Bits[s]
                let acc = ResizeArray<string>()

                for j in 0..s do
                    if hasBit b j then
                        acc.Add reach.Ids[j]

                Set.ofSeq acc
            | None -> ancestorsOf reach.Graph id

        /// Is `ancestor` in `descendant`'s ancestor closure — `Set.contains ancestor (Dag.ancestorsOf
        /// dag descendant)`, so a node reaches itself and nothing reaches an id the DAG does not hold.
        /// One bit test where both are orderable.
        let reaches (reach: Reach<'Op>) (ancestor: string) (descendant: string) : bool =
            match Map.tryFind descendant reach.Slot with
            | Some sd ->
                match Map.tryFind ancestor reach.Slot with
                | Some sa -> hasBit reach.Bits[sd] sa
                | None -> false
            | None -> Set.contains ancestor (ancestorsOf reach.Graph descendant)

        /// The drain restricted to one slot's closure: every orderable id in it, in order.
        let private closureInOrder (reach: Reach<'Op>) (s: int) (keep: int -> bool) : string list =
            let b = reach.Bits[s]

            [ for t in 0 .. reach.Pos[s] do
                  let j = reach.Order[t]

                  if hasBit b j && keep j then
                      reach.Ids[j] ]

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
                    let bl = reach.Bits[sl]
                    let br = reach.Bits[sr]
                    let mutable best = -1

                    for j in 0 .. min sl sr do
                        if hasBit bl j && hasBit br j then
                            if
                                best < 0
                                || reach.Count[j] > reach.Count[best]
                                || (reach.Count[j] = reach.Count[best]
                                    && System.String.CompareOrdinal(reach.Ids[j], reach.Ids[best]) > 0)
                            then
                                best <- j

                    if best < 0 then None else Some reach.Ids[best]
                | _ -> mergeBase reach.Graph left right

        /// `Dag.commonBase`, from the index (Phase 311): the node common to every head's closure with
        /// the largest closure, tie-broken by id; `None` for no heads, a head the DAG does not hold, or
        /// histories with nothing in common to all.
        let commonBase (reach: Reach<'Op>) (heads: string list) : string option =
            let hs = heads |> List.distinct

            if List.isEmpty hs then
                None
            elif hs |> List.exists (fun h -> not (reach.Graph.Nodes.ContainsKey h)) then
                None
            else
                let slots = hs |> List.map (fun h -> Map.tryFind h reach.Slot)

                if slots |> List.exists Option.isNone then
                    commonBase reach.Graph heads
                else
                    let ss = slots |> List.choose id
                    let top = ss |> List.min
                    let mutable best = -1

                    for j in 0..top do
                        if ss |> List.forall (fun s -> hasBit reach.Bits[s] j) then
                            if
                                best < 0
                                || reach.Count[j] > reach.Count[best]
                                || (reach.Count[j] = reach.Count[best]
                                    && System.String.CompareOrdinal(reach.Ids[j], reach.Ids[best]) > 0)
                            then
                                best <- j

                    if best < 0 then None else Some reach.Ids[best]

        /// `Dag.between`, from the index: the nodes in `head`'s closure and not in `baseId`'s, in
        /// topological order; `[]` for a head the DAG does not hold.
        let between (reach: Reach<'Op>) (baseId: string) (head: string) : DagNode<'Op> list =
            let headSlot = Map.tryFind head reach.Slot
            let baseSlot = Map.tryFind baseId reach.Slot

            match headSlot with
            | Some sh when baseSlot.IsSome || not (reach.Graph.Nodes.ContainsKey baseId) ->
                let notInBase =
                    match baseSlot with
                    | Some sb -> fun j -> not (hasBit reach.Bits[sb] j)
                    | None -> fun _ -> true

                closureInOrder reach sh notInBase |> List.map (fun id -> reach.Graph.Nodes[id])
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
                let node = dag'.Nodes[id]
                let parentSlots = node.Parents |> List.map (fun p -> Map.tryFind p reach.Slot)

                if parentSlots |> List.exists Option.isNone then
                    { reach with Graph = dag' }
                else
                    let ps = parentSlots |> List.choose (fun p -> p)
                    let n = reach.Ids.Length
                    let s = n
                    let after = ps |> List.fold (fun acc p -> max acc reach.Pos[p]) -1

                    let mutable q = after + 1

                    while q < n && System.String.CompareOrdinal(id, reach.Ids[reach.Order[q]]) > 0 do
                        q <- q + 1

                    let own = closureBits (ps |> List.map (fun p -> reach.Bits[p])) s

                    { Graph = dag'
                      Slot = Map.add id s reach.Slot
                      Ids = Array.append reach.Ids [| id |]
                      Bits = Array.append reach.Bits [| own |]
                      Count = Array.append reach.Count [| popCount own |]
                      Order =
                        Array.init (n + 1) (fun t ->
                            if t < q then reach.Order[t]
                            elif t = q then s
                            else reach.Order[t - 1])
                      Pos =
                        Array.init (n + 1) (fun j ->
                            if j = s then q
                            elif reach.Pos[j] >= q then reach.Pos[j] + 1
                            else reach.Pos[j])
                      Dangling = reach.Dangling }

    /// What a write that EXTENDS an index answers (Phase 410; a positional triple before): the node,
    /// the DAG holding it, and the index extended to that DAG. The answer of `appendIndexed` and
    /// `mergeIndexed`. No equality, as `Reach` has none.
    [<NoEquality; NoComparison>]
    type IndexedAppend<'Op> =
        {
            /// The id of the node the write recorded — an identical node already held, when the write
            /// deduplicated.
            Id: string
            /// The DAG holding that node: `append`'s (or `merge`'s) DAG, and `Reach.dag Reach`.
            Dag: T<'Op>
            /// The index extended with the node: it answers every question as `Reach.ofDag Dag` does.
            Reach: Reach<'Op>
        }

    /// `append`, extending an index with the new node (Phase 289): `Id` and `Dag` are exactly
    /// `append`'s answer on `Reach.dag reach`, and `Reach` answers every question as
    /// `Reach.ofDag Dag` does, at O(N) instead of a rebuild. Refusals are `append`'s.
    let appendIndexed
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (parentId: string)
        (reach: Reach<'Op>)
        : Result<IndexedAppend<'Op>, DagAppendFault> =
        match append hashFn w actor op parentId reach.Graph with
        | Error f -> Error f
        | Ok(id, dag') ->
            Ok
                { Id = id
                  Dag = dag'
                  Reach = Reach.extend reach dag' id }

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
        : Result<IndexedAppend<'Op>, DagAppendFault> =
        match merge hashFn w actor op leftId rightId reach.Graph with
        | Error f -> Error f
        | Ok(id, dag') ->
            Ok
                { Id = id
                  Dag = dag'
                  Reach = Reach.extend reach dag' id }

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
                        match w.Apply reach.Graph.Nodes[id].Op st with
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
                | Some s -> j <= s && ((reach.Bits[s][j >>> 5] >>> (j &&& 31)) &&& 1) = 1
                | None -> false

            let hs = List.distinct heads
            let baseSlot = Map.tryFind baseId reach.Slot
            let closures = hs |> List.map (fun h -> h, Map.tryFind h reach.Slot)

            let owners (j: int) =
                closures |> List.sumBy (fun (_, c) -> if has c j then 1 else 0)

            let last =
                closures
                |> List.fold
                    (fun acc (_, c) -> max acc (c |> Option.map (fun s -> reach.Pos[s]) |> Option.defaultValue -1))
                    -1

            let above =
                [ for t in 0..last do
                      let j = reach.Order[t]

                      if not (has baseSlot j) then
                          let o = owners j

                          if o > 0 then
                              j, o ]

            let r =
                { Shared =
                    above
                    |> List.filter (fun (_, o) -> o >= 2)
                    |> List.map (fun (j, _) -> reach.Ids[j])
                  Exclusive =
                    closures
                    |> List.map (fun (h, c) ->
                        h,
                        above
                        |> List.filter (fun (j, o) -> o = 1 && has c j)
                        |> List.map (fun (j, _) -> reach.Ids[j])) }

            reconcileRegion w footprintOf dag r baseState

    // ---- retention (Phase 311) ----
    // The linear stream retains by compaction behind a snapshot. A DAG has a second kind of history a
    // store may want to stop carrying: whole branches no retained head needs — an abandoned lane, a
    // superseded fork. Content addressing means such a node can be TOMBSTONED without excising
    // anything that remains: every retained node's id is a hash over its own closure, and a node outside
    // every retained closure is in none of them. Core names the set; what a host does with it (drop it
    // from a lane file, archive it, keep it) is the host's call (GP6), and nothing here removes a node.

    /// The nodes no retained root needs (Phase 311): every node of the index's DAG outside the ancestor
    /// closure of every id in `roots`, sorted ordinally. The closures are read from the index's bitsets
    /// (a node the drain cannot order falls back to the unindexed walk). Dropping exactly these leaves
    /// every root's closure whole — the retained set is down-closed, so nothing kept names a parent that
    /// went — and each root replays, verifies and orders as before (`Conformance.laneLaws`).
    /// `Error root` for the first root, in the order given, the DAG does not hold: a mistyped root
    /// would otherwise retain nothing and mark everything prunable.
    let prunable (reach: Reach<'Op>) (roots: string list) : Result<string list, string> =
        let dag = reach.Graph

        match roots |> List.tryFind (fun r -> not (dag.Nodes.ContainsKey r)) with
        | Some r -> Error r
        | None ->
            let kept =
                roots
                |> List.fold (fun acc r -> Set.union acc (Reach.ancestors reach r)) Set.empty

            dag.Nodes
            |> Map.toList
            |> List.map fst
            |> List.filter (fun id -> not (Set.contains id kept))
            |> Ok

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
        {
            /// The id of the node the checkpoint stands at; history above it replays from `State`.
            Node: string
            /// The state after applying every op in `Node`'s ancestor closure, `Node`'s own op included.
            State: 'State
            /// The seal over `Node` and `State`; `verifyCheckpoint` recomputes it and refuses a mismatch.
            Hash: string
        }

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
                if dag.Nodes[id].Parents |> List.exists (fun p -> p = node || Set.contains p desc) then
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
                            match w.Apply dag.Nodes[id].Op st with
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
                |> List.collect (fun id -> dag.Nodes[id].Parents)
                |> List.filter (fun p -> p <> node && Set.contains p behind)
                |> List.distinct

            let band = forwardFrom dag (fun id -> Set.contains id behind) side
            let keep = Set.unionMany [ band; later; Set.singleton node ]

            Ok { NodeMap = dag.Nodes |> Map.filter (fun id _ -> Set.contains id keep) }

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
    /// refuses, is `StreamLoadFault.UnreadableCheckpoint` carrying the `JsonlFault` by its sidecar line
    /// (`loadFaultToString` renders `checkpoint sidecar: line N: <reason> (position P)`, as before Phase
    /// 416). The lane's own faults are `fromJsonl`'s, and come first. Structural only, as `fromJsonl` is:
    /// verify with `verifyCheckpoint` / `verifyDagFrom` before trusting either.
    let fromJsonlWithCheckpoints
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (stateDecode: string -> Result<'State, string>)
        (text: string)
        (sidecar: string option)
        : Result<T<'Op> * Checkpoint<'State> list, StreamLoadFault<DagBreak>> =
        fromJsonl w text
        |> Result.bind (fun dag ->
            match sidecar with
            | None -> Ok(dag, [])
            | Some side ->
                OpStream.Jsonl.scanRecords (checkpointOf stateDecode) side
                |> Result.map (fun cps -> dag, cps)
                |> Result.mapError StreamLoadFault.UnreadableCheckpoint)

    // ---- lanes (Phase 311) ----
    // A multi-writer store keeps one file per writer — a LANE (`<store>/ops/<lane>.jsonl` is the shape
    // consumers use) — and folds their union. Lanes partition WHO wrote a node, never a resource: every
    // node belongs to the lane whose file holds it, a lane is one writer's history, and nothing about a
    // lane changes what a node means or how it replays. So the lane identity rides BESIDE the node map
    // (`Loaded.LaneOf`), not in the node: content ids, files written before this phase and every
    // function above are untouched. Core loads the union one way, verifies it naming the lane at fault,
    // checks the two lane properties a store relies on — one head per lane, and heads that share a
    // history — and orders the union by a default key a consumer with another key replaces.

    /// A lane store, loaded (Phase 311): the union `Dag`, and for every node of it the lane whose file
    /// holds it (`LaneOf`).
    type Loaded<'Op> =
        {
            /// The union of every lane's nodes, each once. Structurally read only — `verifyLanes` it
            /// before trusting it.
            Dag: T<'Op>
            /// Node id → the lane whose file holds it; a node held by several lanes with the same content
            /// is attributed to the ordinally smallest. An id absent here counts as lane `""`.
            LaneOf: Map<string, string>
        }

    /// A lane store's first integrity fault, and the lane whose file holds the node at fault (Phase 311).
    type LaneBreak =
        {
            /// The lane `LaneOf` attributes the faulty node to, or `""` when it names none.
            Lane: string
            /// The union's first break, exactly as `firstBreak` reports it.
            Break: DagBreak
        }

    /// The lanes of a store and the nodes each holds, in ordinal lane order; a node `LaneOf` does not
    /// name is under the lane `""`.
    let private lanePartition (loaded: Loaded<'Op>) : (string * T<'Op>) list =
        loaded.Dag.Nodes
        |> Map.toList
        |> List.groupBy (fun (id, _) -> Map.tryFind id loaded.LaneOf |> Option.defaultValue "")
        |> List.map (fun (lane, ns) -> lane, { NodeMap = Map.ofList ns })
        |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b))

    /// The union of a lane store, read through `carry` (Phase 416): every lane text structurally, in
    /// ordinal lane order, with each node's lane and the line of that lane's text holding it. The faults
    /// a lane SET has — a lane id given twice, a lane text that does not parse (the first in lane
    /// order), a node two lanes hold with different content — are refused here; an op the witness
    /// refused is not.
    let private loadCarried
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (lanes: (string * string) list)
        : Result<Loaded<string * Result<'Op, string>> * Map<string, int>, StreamLoadFault<LaneBreak>> =
        let cw = carry w

        let sorted =
            lanes |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b))

        match sorted |> List.pairwise |> List.tryFind (fun ((a, _), (b, _)) -> a = b) with
        | Some((l, _), _) -> Error(StreamLoadFault.DuplicateLane l)
        | None ->
            let rec go (acc: Loaded<string * Result<'Op, string>>) (at: Map<string, int>) =
                function
                | [] -> Ok(acc, at)
                | (lane: string, text: string) :: rest ->
                    match readCarried w text with
                    | Error f -> Error(StreamLoadFault.Unreadable(Some lane, f))
                    | Ok(d, lineOf) ->
                        let step st (id: string) (n: DagNode<string * Result<'Op, string>>) =
                            st
                            |> Result.bind (fun (l: Loaded<string * Result<'Op, string>>, at: Map<string, int>) ->
                                match Map.tryFind id l.Dag.Nodes with
                                | None ->
                                    Ok(
                                        { Dag = { NodeMap = Map.add id n l.Dag.Nodes }
                                          LaneOf = Map.add id lane l.LaneOf },
                                        Map.add id lineOf[id] at
                                    )
                                | Some held when sameNode cw.Encode held n.Parents n.Actor n.Op -> Ok(l, at)
                                | Some _ -> Error(StreamLoadFault.Collision(id, ordinalSort [ l.LaneOf[id]; lane ])))

                        match Map.fold step (Ok(acc, at)) d.Nodes with
                        | Ok(acc', at') -> go acc' at' rest
                        | Error f -> Error f

            go { Dag = empty; LaneOf = Map.empty } Map.empty sorted

    /// A carried lane store decoded — or every op the witness refused, by lane and line.
    let private settleLanes
        (loaded: Loaded<string * Result<'Op, string>>, lineOf: Map<string, int>)
        : Result<Loaded<'Op>, StreamLoadFault<LaneBreak>> =
        settle (fun id -> Some(Map.tryFind id loaded.LaneOf |> Option.defaultValue "")) lineOf loaded.Dag
        |> Result.map (fun dag -> { Dag = dag; LaneOf = loaded.LaneOf })

    /// Load a lane store (Phase 311): one `(lane id, JSONL text)` per lane file, in any order. Each text
    /// is read as `fromJsonl` reads it — structural, so run `verifyLanes` before trusting the union, or
    /// load with `loadLanesVerified` — and the union holds every node once. A node two lanes hold with
    /// the SAME content is one node, attributed to the ordinally smallest of those lanes; with different
    /// content it is refused (`StreamLoadFault.Collision`). The lanes are read in ordinal lane-id order
    /// whatever order they are handed in, so the store is a function of the lane SET. Refused by name: a
    /// lane id given twice (`DuplicateLane`), a text that does not parse (`Unreadable` with its lane and
    /// the typed `JsonlFault`, the first such lane in lane-id order), and a collision across lanes. Then,
    /// since Phase 416, the ops: when only the witness refused, `Undecodable` names every such op by lane,
    /// line and node id. Never `Broken`. Equal, as a DAG, to `fromJsonl` of the lanes' texts concatenated
    /// — the union is the union whichever way it is read — with the attribution beside it.
    let loadLanes
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (lanes: (string * string) list)
        : Result<Loaded<'Op>, StreamLoadFault<LaneBreak>> =
        loadCarried w lanes |> Result.bind settleLanes

    /// `loadLanes` + the integrity gate (Phase 416): the union is verified as `verifyLanes` verifies it
    /// BEFORE any op must decode — a node the witness cannot decode is hashed over its stored op text — so
    /// a lane store answers exactly one of three things about its history. `Broken` with the first
    /// `LaneBreak`: a content id that does not recompute or a parent that does not resolve, damage,
    /// whatever else is wrong. `Undecodable`, every site named by lane, line and node id: the store is
    /// intact and the witness predates some of its ops — the store was written by a newer host. `Ok`: the
    /// store, verified. The lane-set faults of `loadLanes` come first.
    let loadLanesVerified
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (lanes: (string * string) list)
        : Result<Loaded<'Op>, StreamLoadFault<LaneBreak>> =
        loadCarried w lanes
        |> Result.bind (fun (loaded, lineOf) ->
            match firstBreak hashFn (carry w) loaded.Dag with
            | Some b ->
                Error(
                    StreamLoadFault.Broken
                        { Lane = Map.tryFind b.NodeId loaded.LaneOf |> Option.defaultValue ""
                          Break = b }
                )
            | None -> settleLanes (loaded, lineOf))

    /// A lane load's fault as text (Phase 416): a break as `lane <lane>: ` and then `loadFaultToString`'s
    /// spelling of the union's break, and every other case as `StreamLoadFault.toStringWith` renders it.
    let laneLoadFaultToString (f: StreamLoadFault<LaneBreak>) : string =
        f
        |> StreamLoadFault.toStringWith (fun lb ->
            "lane " + lb.Lane + ": " + loadFaultToString (StreamLoadFault.Broken lb.Break))

    /// The lane files of a store (Phase 311): one `(lane id, text)` per lane, in ordinal lane order, each
    /// text `toJsonl` of exactly the nodes `LaneOf` attributes to that lane — so `loadLanes` of the result
    /// is the store again (`Conformance.laneLaws`). A node `LaneOf` does not name is written under `""`.
    let lanesToJsonl (encode: 'Op -> string) (loaded: Loaded<'Op>) : (string * string) list =
        lanePartition loaded |> List.map (fun (lane, d) -> lane, toJsonl encode d)

    /// `lanesToJsonl` through the checked writer (Phase 311): each lane by `tryToJsonl`, the first lane
    /// (in lane order) whose op encoding the reader could not read back refused with its lane and
    /// `tryToJsonl`'s fault. On `Ok`, `lanesToJsonl`'s texts byte for byte.
    let tryLanesToJsonl
        (encode: 'Op -> string)
        (loaded: Loaded<'Op>)
        : Result<(string * string) list, string * JsonlWriteFault> =
        let rec go (acc: (string * string) list) =
            function
            | [] -> Ok(List.rev acc)
            | (lane: string, d: T<'Op>) :: rest ->
                match tryToJsonl encode d with
                | Error f -> Error(lane, f)
                | Ok text -> go ((lane, text) :: acc) rest

        go [] (lanePartition loaded)

    /// Verify a lane store (Phase 311): `firstBreak` over the UNION — a parent link crosses lane files, so
    /// no lane verifies alone — with the lane whose file holds the faulty node. `Ok()` for an intact
    /// store. The break is the earliest in the union's history (`firstBreak`'s topological scan), so the
    /// lane named is the one where the history first goes wrong; a node `LaneOf` does not name reports
    /// the lane `""`.
    let verifyLanes
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (loaded: Loaded<'Op>)
        : Result<unit, LaneBreak> =
        match firstBreak hashFn w loaded.Dag with
        | None -> Ok()
        | Some b ->
            Error
                { Lane = Map.tryFind b.NodeId loaded.LaneOf |> Option.defaultValue ""
                  Break = b }

    /// For each lane, its nodes as `(id, seq, under)`: `seq` the number of nodes of the SAME lane the
    /// node descends from in the union, `under` whether some other node of the lane descends from it.
    /// Read from the reachability index's bitsets — a per-lane mask over slots, one pass per node — with
    /// the unindexed walk for a lane holding a node the drain cannot order.
    let private laneShape (loaded: Loaded<'Op>) : (string * (string * int * bool) list) list =
        let reach = Reach.ofDag loaded.Dag

        let bitOf (bits: int array) (j: int) =
            let w = j >>> 5
            w < bits.Length && ((bits[w] >>> (j &&& 31)) &&& 1) = 1

        lanePartition loaded
        |> List.map (fun (lane, d) ->
            let ids = d.Nodes |> Map.toList |> List.map fst
            let slots = ids |> List.map (fun i -> Map.tryFind i reach.Slot)

            if slots |> List.forall Option.isSome then
                let ss = slots |> List.choose id
                let width = (reach.Ids.Length >>> 5) + 1
                let mask = Array.zeroCreate<int> width
                let below = Array.zeroCreate<int> width

                for s in ss do
                    mask[s >>> 5] <- mask[s >>> 5] ||| (1 <<< (s &&& 31))
                    let b = reach.Bits[s]

                    for k in 0 .. b.Length - 1 do
                        let v =
                            if k = (s >>> 5) then
                                b[k] &&& ~~~(1 <<< (s &&& 31))
                            else
                                b[k]

                        below[k] <- below[k] ||| v

                lane,
                ss
                |> List.map (fun s ->
                    let b = reach.Bits[s]
                    let mutable c = 0

                    for j in 0 .. s - 1 do
                        if bitOf b j && bitOf mask j then
                            c <- c + 1

                    reach.Ids[s], c, bitOf below s)
            else
                lane,
                ids
                |> List.map (fun a ->
                    let seq =
                        ids |> List.sumBy (fun b -> if b <> a && Reach.reaches reach b a then 1 else 0)

                    a, seq, ids |> List.exists (fun b -> b <> a && Reach.reaches reach a b)))

    /// The lanes that hold more than one writer's history (Phase 311): every lane holding two nodes
    /// NEITHER of which reaches the other in the union — one lane file must mean one head, and two
    /// incomparable nodes in one lane are two heads, which is what two writers sharing a lane id produce.
    /// Each with its TIPS (its nodes no other node of the same lane descends from), sorted; lanes in
    /// ordinal order; `[]` for a sound store. Comparability is read in the UNION, so a writer whose next
    /// node descends from its last only through another lane's merge still has one head, and a lane is
    /// judged whether or not it verifies on its own.
    let laneCollisions (loaded: Loaded<'Op>) : (string * string list) list =
        laneShape loaded
        |> List.choose (fun (lane, ns) ->
            match ns |> List.filter (fun (_, _, under) -> not under) with
            | _ :: _ :: _ as tips -> Some(lane, tips |> List.map (fun (id, _, _) -> id) |> ordinalSort)
            | _ -> None)

    /// Heads that share no history (Phase 311): when the DAG has two or more heads and `commonBase` of
    /// all of them is `None`, every head with its ROOT — the ordinally smallest node of its closure that
    /// has no parent the DAG holds (the head itself when there is none); `[]` otherwise. A store whose
    /// writers began from different genesis nodes and never merged: a structural fault no fold can
    /// repair, since no base exists to fold over. Heads in ordinal order.
    let disjointRoots (dag: T<'Op>) : (string * string) list =
        match heads dag with
        | []
        | [ _ ] -> []
        | hs when (commonBase dag hs).IsSome -> []
        | hs ->
            hs
            |> List.map (fun h ->
                let root =
                    ancestorsOf dag h
                    |> Set.toList
                    |> List.filter (fun id ->
                        dag.Nodes[id].Parents |> List.forall (fun p -> not (dag.Nodes.ContainsKey p)))
                    |> List.tryHead
                    |> Option.defaultValue h

                h, root)

    /// `appendOn` into a lane (Phase 311): the node is added to the union and attributed to `lane`. An
    /// identical node the store already holds is one node, and keeps the lane it already has — the
    /// writer that wrote it first. Refusals are `appendOn`'s.
    let appendOnLane
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (parents: string list)
        (lane: string)
        (loaded: Loaded<'Op>)
        : Result<string * Loaded<'Op>, DagAppendFault> =
        appendOn hashFn w actor op parents loaded.Dag
        |> Result.map (fun (id, d) ->
            id,
            { Dag = d
              LaneOf =
                if loaded.LaneOf.ContainsKey id then
                    loaded.LaneOf
                else
                    Map.add id lane loaded.LaneOf })

    /// The lane store's default order key for one node (Phase 410; a positional `(lane, seq, id)`
    /// triple before). It compares field by field in declaration order — `Lane`, then `Seq`, then
    /// `Id`, each string ordinally — which is the order the triple compared in, so every total order
    /// and replay it drives is unchanged.
    type LaneKey =
        {
            /// The node's lane; `""` for a node `LaneOf` does not name.
            Lane: string
            /// The node's position in its lane's own history: the number of nodes of the same lane it
            /// descends from, so a one-head lane's nodes are 0, 1, 2, … in the order its writer wrote
            /// them. `0` for a node `LaneOf` does not name.
            Seq: int
            /// The node's id: breaks the ties a lane with two heads leaves.
            Id: string
        }

    /// The lane store's DEFAULT order key (Phase 311): a `LaneKey` — the node's lane, its position
    /// in that lane's own history, and its id. Under it the drain takes, among the nodes that are
    /// ready, the smallest lane's next node: each writer's history is kept together as far as the
    /// parent relation allows, lanes in lane-id order, and the id breaks the ties a lane with two
    /// heads leaves. Computed once per store (the returned function answers per node). A consumer
    /// whose history carries an order of its own passes its own key to `totalOrderBy` /
    /// `replayAllBy` instead.
    let laneKey (loaded: Loaded<'Op>) : DagNode<'Op> -> LaneKey =
        let seqs =
            laneShape loaded
            |> List.collect (fun (_, ns) -> ns |> List.map (fun (id, s, _) -> id, s))
            |> Map.ofList

        fun n ->
            { Lane = Map.tryFind n.Id loaded.LaneOf |> Option.defaultValue ""
              Seq = Map.tryFind n.Id seqs |> Option.defaultValue 0
              Id = n.Id }

    /// The lane store in its default total order (Phase 311): `totalOrderBy (laneKey loaded)`.
    let totalOrder (loaded: Loaded<'Op>) : Result<string list, TotalOrderFault> =
        totalOrderBy (laneKey loaded) loaded.Dag

    /// Replay a lane store in its default total order (Phase 311): `replayAllBy (laneKey loaded)`.
    let replayAll
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (loaded: Loaded<'Op>)
        : Result<'State, ReplayAllFault<'Rej>> =
        replayAllBy (laneKey loaded) w state0 loaded.Dag

    // ---- rehash between hash functions (Phase 311) ----
    // The linear stream migrates its chain between formats with `OpStream.rehash`, verifying the source
    // first. A DAG's ids ARE its hashes, so a change of hash function — the 32-bit FNV-1a default to a
    // host's SHA-256 — re-mints every node, and each child's parent ids with it.

    /// Why `rehashWith` (Phase 311) or `rehashEncoding` (Phase 360) refused.
    [<RequireQualifiedAccess>]
    type RehashFault =
        /// The source does not verify under `fromHash` (or, for `rehashEncoding`, under its profile and
        /// witness): its first break. A history that does not verify is not re-blessed under a new hash
        /// or a new encoding.
        | Unverified of DagBreak
        /// Nodes no drain places — a cycle — every one, in id order.
        | Cyclic of unplaced: string list
        /// Two nodes mint one id under `toHash`: the target hash collides, at that id.
        | Collision of nodeId: string

    /// The re-mint both rehashes share (Phase 360 factored it out of Phase 311's `rehashWith`): refuse
    /// a `sourceBreak`, then rebuild every node in topological order at the id `idOf` mints, each
    /// parent id replaced by its new one, keyed by `encode` for the collision check.
    let private remint
        (sourceBreak: DagBreak option)
        (idOf: string list -> Actor -> 'Op -> string)
        (encode: 'Op -> string)
        (dag: T<'Op>)
        : Result<T<'Op> * Map<string, string>, RehashFault> =
        match sourceBreak with
        | Some b -> Error(RehashFault.Unverified b)
        | None ->
            match drainBy (fun _ -> 0) dag with
            | _, (_ :: _ as unplaced) -> Error(RehashFault.Cyclic unplaced)
            | order, [] ->
                let rec go (d: T<'Op>) (ids: Map<string, string>) =
                    function
                    | [] -> Ok(d, ids)
                    | (id: string) :: rest ->
                        let n = dag.Nodes[id]
                        let parents = n.Parents |> List.map (fun p -> ids[p])
                        let id' = idOf parents n.Actor n.Op

                        match addNodeAs id' encode n.Actor n.Op parents d with
                        | Ok(_, d') when Map.count d'.Nodes = Map.count d.Nodes -> Error(RehashFault.Collision id')
                        | Ok(_, d') -> go d' (Map.add id id' ids) rest
                        | Error _ -> Error(RehashFault.Collision id')

                go empty Map.empty order

    /// Re-mint a DAG under another hash function (Phase 311): verify it under `fromHash` (`firstBreak`;
    /// a break is `Unverified`), then rebuild every node in topological order under `toHash`, each
    /// parent id replaced by its new id — the ops, actors and parent ORDER are the source of truth, only
    /// the ids change. `Ok(dag', ids)`, with `ids` mapping every old id to its new one (for heads,
    /// lanes, checkpoints and attestations a host keys by id). The result verifies under `toHash`, and
    /// `rehashWith toHash fromHash` of it is the source again (`Conformance.laneLaws`).
    let rehashWith
        (fromHash: HashFn)
        (toHash: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (dag: T<'Op>)
        : Result<T<'Op> * Map<string, string>, RehashFault> =
        remint (firstBreak fromHash w dag) (fun ps a op -> nodeHash toHash w.Encode ps a op) w.Encode dag

    /// `rehashWith` over a lane store (Phase 311): the union re-minted, and every node's lane carried to
    /// its new id.
    let rehashLanes
        (fromHash: HashFn)
        (toHash: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (loaded: Loaded<'Op>)
        : Result<Loaded<'Op> * Map<string, string>, RehashFault> =
        rehashWith fromHash toHash w loaded.Dag
        |> Result.map (fun (d, ids) ->
            { Dag = d
              LaneOf =
                loaded.LaneOf
                |> Map.toList
                |> List.choose (fun (k, l) -> Map.tryFind k ids |> Option.map (fun k' -> k', l))
                |> Map.ofList },
            ids)

    /// The TWO-WITNESS rehash across encoding profiles (Phase 360): verify the DAG under `fromProfile`
    /// with `fromW`'s encoder (`firstBreakWith`; a break is `Unverified`), then re-mint every node under
    /// `toProfile` with `toW`'s — `rehashWith` changes the hash function and keeps one encoder; this
    /// changes the encoding and keeps one hash function. A store whose op encoder rendered through
    /// `Json.render` before `0.33.0` passes `fromW` encoding with `Json.renderWith EncodingProfile.V1`
    /// and `toW` with `V2`. Only the witnesses' `Encode` is read. `Ok(dag', ids)` as for `rehashWith`;
    /// the result verifies under `toProfile` and `toW`, and the rehash back is the source again. A lane
    /// store carries its lanes across through `ids`, as `rehashLanes` does for the hash axis.
    let rehashEncoding
        (fromProfile: OpStream.EncodingProfile)
        (fromW: StreamWitness<'Op, 'State, 'Rej>)
        (toProfile: OpStream.EncodingProfile)
        (toW: StreamWitness<'Op, 'State, 'Rej>)
        (hashFn: HashFn)
        (dag: T<'Op>)
        : Result<T<'Op> * Map<string, string>, RehashFault> =
        remint
            (firstBreakWith fromProfile hashFn fromW dag)
            (fun ps a op -> nodeIdWith toProfile hashFn toW.Encode ps a op)
            toW.Encode
            dag

    // ---- attestation (Phase 311) ----
    // The linear stream signs its chain head through `IAttestationSink` (Phase 320). A DAG node's id is a
    // hash over its whole closure, so signing a node signs its history; on a store several parties write,
    // the signature also says WHO vouched (`OpStream.attestationSubject`, a party-bound subject).

    /// Why `attestHead` refused (Phase 311).
    [<RequireQualifiedAccess>]
    type DagAttestFault =
        /// The DAG holds no node with this id.
        | UnknownNode of nodeId: string

    /// Attest a node of the DAG — usually a head — as `party` (Phase 311): the sink signs
    /// `OpStream.attestationSubject hashFn party headId`, and since the node id is a hash over its whole
    /// ancestor closure one signature covers that history. `Ok None` from the no-op sink; `Error
    /// UnknownNode` for an id the DAG does not hold. Integrity is a separate act, as for the linear
    /// stream: verify the DAG (`verifyDag`, `verifyLanes`) before attesting it.
    let attestHead
        (sink: IAttestationSink)
        (hashFn: HashFn)
        (party: Actor)
        (dag: T<'Op>)
        (headId: string)
        : Result<Attestation option, DagAttestFault> =
        if not (dag.Nodes.ContainsKey headId) then
            Error(DagAttestFault.UnknownNode headId)
        else
            Ok(sink.Sign(OpStream.attestationSubject hashFn party headId))

    /// Re-verify a party's attestation of a node (Phase 311): `true` only when the DAG holds `headId`, the
    /// attestation names the subject `attestHead` would sign for this party and node, and the sink accepts
    /// it there — an attestation by another party, or of another node, is refused before the sink is asked.
    let verifyAttestation
        (sink: IAttestationSink)
        (hashFn: HashFn)
        (party: Actor)
        (attestation: Attestation)
        (dag: T<'Op>)
        (headId: string)
        : bool =
        let subject = OpStream.attestationSubject hashFn party headId

        dag.Nodes.ContainsKey headId
        && attestation.Head = subject
        && sink.Verify attestation subject
