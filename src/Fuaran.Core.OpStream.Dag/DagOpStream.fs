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
        | ContentIdMismatch -> "content-id mismatch (tampered node)"
        | MissingParent -> "missing parent"
        | Unrecognised s -> s

    /// Total: both strings the walker ever emitted classify, and anything else is `Unrecognised`
    /// verbatim rather than swept into the nearer-looking case. `toString >> ofString` is the
    /// identity on the named cases.
    let ofString (s: string) : DagBreakReason =
        match s with
        | "content-id mismatch (tampered node)" -> ContentIdMismatch
        | "missing parent" -> MissingParent
        | other -> Unrecognised other

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
/// The three shapes partition the negation of the Phase-78 independence predicate over its four
/// `Ops.Footprint` address kinds, so a pair is tagged iff `Ops.independent` would reject it:
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
type MergeConflictShape =
    | ConcurrentUpdate
    | InsertPositionClash
    | MoveVsRemove

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

/// Why `Dag.tryAppend` / `Dag.tryMerge` refused to build a node (Phase 300) — the typed refusals that
/// make the parent-splice premise a property of everything this module BUILDS rather than a premise
/// about its callers. `nodeHash` joins the sorted parent ids with `,`, so a parent id carrying a comma
/// splices: an `append` whose parent id is the string `"x,y"` minted the id of `merge(x, y)` with no
/// hash weakness at all, and replaced that node silently. And `""` is `append`'s genesis marker, never
/// a node id, so `merge("", x)` built a node with a phantom parent. `parent_splice_unambiguous`
/// (proofs/Chain.fst) proves the comma-join injective exactly for non-empty, comma-free ids; these two
/// refusals are that premise, stated where the ids enter.
[<RequireQualifiedAccess>]
type DagAppendFault =
    /// A merge was handed `""` for a parent — `append`'s genesis marker, not a node id.
    | EmptyParentId
    /// A parent id carries a `,` — the separator the content-hash pre-image joins parent ids with.
    | CommaInParentId of parentId: string

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

    let private addNode
        (hashFn: HashFn)
        (encode: 'Op -> string)
        (actor: Actor)
        (op: 'Op)
        (parents: string list)
        (dag: T<'Op>)
        : string * T<'Op> =
        let id = nodeHash hashFn encode parents actor op

        let node =
            { Id = id
              Parents = parents
              Actor = actor
              Op = op }

        id, { Nodes = Map.add id node dag.Nodes }

    /// `append` with its refusal typed (Phase 300): a comma-bearing `parentId` is
    /// `Error(DagAppendFault.CommaInParentId _)`, because its content id would splice into a merge's.
    /// `""` is genesis, as for `append`.
    let tryAppend
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (parentId: string)
        (dag: T<'Op>)
        : Result<string * T<'Op>, DagAppendFault> =
        match parentFault parentId with
        | Some f -> Error f
        | None -> Ok(addNode hashFn w.Encode actor op (if parentId = "" then [] else [ parentId ]) dag)

    /// Append `op` as a child of `parentId` (`""` for genesis). Appending onto a node that
    /// already has a child *forks* a branch. Returns the new node's content id.
    ///
    /// Since Phase 300 a comma-bearing `parentId` is refused — an `ArgumentException` carrying
    /// `DagAppendFault.toString`; `tryAppend` is the same refusal as a typed `Result`.
    let append
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (parentId: string)
        (dag: T<'Op>)
        : string * T<'Op> =
        match tryAppend hashFn w actor op parentId dag with
        | Ok r -> r
        | Error f -> invalidArg "parentId" (DagAppendFault.toString f)

    /// `merge` with its refusals typed (Phase 300): a parent that is `""` is
    /// `Error DagAppendFault.EmptyParentId`, a comma-bearing one `Error(DagAppendFault.CommaInParentId _)`
    /// — the left parent judged first.
    let tryMerge
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
                parentFault p

        match judge leftId |> Option.orElse (judge rightId) with
        | Some f -> Error f
        | None -> Ok(addNode hashFn w.Encode actor op [ leftId; rightId ] dag)

    /// Merge two heads into a convergent node (`Parents = [leftId; rightId]`). `op` is the
    /// merge commit's own reconciliation op (a domain no-op where the merge adds nothing).
    ///
    /// Since Phase 300 a parent that is `""` or carries a comma is refused — an `ArgumentException`
    /// carrying `DagAppendFault.toString`; `tryMerge` is the same refusal as a typed `Result`. With both
    /// refusals, no merge id can equal an append id: an append's pre-image names ONE comma-free parent,
    /// a merge's two joined by a comma.
    let merge
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (actor: Actor)
        (op: 'Op)
        (leftId: string)
        (rightId: string)
        (dag: T<'Op>)
        : string * T<'Op> =
        match tryMerge hashFn w actor op leftId rightId dag with
        | Ok r -> r
        | Error f -> invalidArg "parentId" (DagAppendFault.toString f)

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
                      Reason = ContentIdMismatch
                      Expected = h
                      Got = id }
            else
                match n.Parents |> List.tryFind (fun p -> not (dag.Nodes.ContainsKey p)) with
                | Some missing ->
                    Some
                        { NodeId = id
                          Reason = MissingParent
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

    /// Replay to `headId`: fold the reducer over the head's ancestor-closure in topological
    /// order (each op applied once). Deterministic — the topo order is total — so two
    /// convergent histories over the same node set replay to the same state. On a domain
    /// rejection, returns the offending node id + the envelope.
    let replayTo
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (dag: T<'Op>)
        (headId: string)
        : Result<'State, string * 'Rej> =
        let rec go st =
            function
            | [] -> Ok st
            | id :: rest ->
                let n = Map.find id dag.Nodes

                match w.Apply n.Op st with
                | Ok st' -> go st' rest
                | Error e -> Error(id, e)

        go state0 (topoOrder dag headId)

    /// A guarded-replay fault (Phase 42): either the head's history is **cyclic** (so a plain
    /// `replayTo` would silently fold only the acyclic prefix to a wrong `Ok`) or a node's op was
    /// **rejected** by the domain witness.
    type ReplayFault<'Rej> =
        | CyclicHistory of headId: string
        | Rejected of nodeId: string * reject: 'Rej

    /// `replayTo` that refuses a cyclic history (Phase 42). Returns `Error(CyclicHistory head)` when
    /// the head's closure is not fully orderable — instead of `replayTo`'s silent partial fold — and
    /// `Error(Rejected …)` on a domain rejection. Prefer this on any DAG that was not loaded through
    /// `fromJsonlVerified` (whose content-hash gate already makes a forged cycle impossible).
    let tryReplayTo
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (state0: 'State)
        (dag: T<'Op>)
        (headId: string)
        : Result<'State, ReplayFault<'Rej>> =
        match tryTopoOrder dag headId with
        | Error _ -> Error(CyclicHistory headId)
        | Ok order ->
            let rec go st =
                function
                | [] -> Ok st
                | id :: rest ->
                    let n = Map.find id dag.Nodes

                    match w.Apply n.Op st with
                    | Ok st' -> go st' rest
                    | Error e -> Error(Rejected(id, e))

            go state0 order

    // ---- JSONL persistence (Phase 01) ----
    // The linear OpStream round-trips to JSONL; the DAG does too, closing the persistence
    // asymmetry. Self-contained scanner — the Dag module takes no Core.Wire dependency (D2)
    // and stays Fable-clean (Phase 241). One JSON object per node; the `op` value is the
    // witness's own Encode output embedded raw (preserved byte-for-byte); nodes are emitted
    // in id-sorted order so output is stable for a fixed DAG.

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
    let toJsonl (encode: 'Op -> string) (dag: T<'Op>) : string =
        dag.Nodes
        |> Map.toList
        |> List.map (fun (_, n) ->
            "{\"node\":true,\"id\":"
            + jstr n.Id
            + ",\"parents\":["
            + (n.Parents |> List.map jstr |> String.concat ",")
            + "],\"actor\":"
            + Actor.encode n.Actor
            + ",\"op\":"
            + encode n.Op
            + "}")
        |> String.concat "\n"

    /// A self-contained FSharp.Core-only line scanner — ported from OpStream's so the Dag
    /// module keeps its no-Core.Wire posture (D2). It splits a flat top-level object into its
    /// fields, capturing the `op` value's raw span byte-for-byte. Structural faults report the
    /// scanner's own fault index (`start` / `i`) through the surrounding `try/with` → `Result`,
    /// never an exception — no `Wire.Json` reparse (D2).
    module private Jsonl =

        /// Unescape a raw JSON string token (surrounding quotes included).
        let unquote (raw: string) : string =
            let inner = raw.Substring(1, raw.Length - 2)
            let sb = System.Text.StringBuilder()
            let n = inner.Length
            let mutable i = 0

            let hex (c: char) =
                if c >= '0' && c <= '9' then int c - int '0'
                elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
                else int c - int 'A' + 10

            while i < n do
                let c = inner.[i]

                if c = '\\' && i + 1 < n then
                    let e = inner.[i + 1]
                    i <- i + 2

                    match e with
                    | '"' -> sb.Append('"') |> ignore
                    | '\\' -> sb.Append('\\') |> ignore
                    | '/' -> sb.Append('/') |> ignore
                    | 'n' -> sb.Append('\n') |> ignore
                    | 'r' -> sb.Append('\r') |> ignore
                    | 't' -> sb.Append('\t') |> ignore
                    | 'b' -> sb.Append('\b') |> ignore
                    | 'f' -> sb.Append('\f') |> ignore
                    | 'u' when i + 3 < n ->
                        let code =
                            (hex inner.[i] <<< 12)
                            + (hex inner.[i + 1] <<< 8)
                            + (hex inner.[i + 2] <<< 4)
                            + hex inner.[i + 3]

                        i <- i + 4
                        sb.Append(char code) |> ignore
                    // A truncated `\u` escape at end of input (Phase 45): emit the `u` literally rather
                    // than reading past the end and throwing an opaque IndexOutOfRangeException.
                    | 'u' -> sb.Append('u') |> ignore
                    | _ -> sb.Append(e) |> ignore
                else
                    sb.Append(c) |> ignore
                    i <- i + 1

            sb.ToString()

        /// Index just past a complete string token starting at the opening quote.
        let skipString (s: string) (start: int) : int =
            let n = s.Length
            let mutable i = start + 1
            let mutable fin = false

            while not fin do
                if i >= n then
                    failwith (sprintf "Dag.fromJsonl: unterminated string (opened at position %d)" start)

                match s.[i] with
                | '\\' -> i <- i + 2
                | '"' ->
                    i <- i + 1
                    fin <- true
                | _ -> i <- i + 1

            i

        /// Index just past a complete JSON value starting at `start` (no leading ws).
        let skipValue (s: string) (start: int) : int =
            let n = s.Length
            let mutable i = start

            match s.[i] with
            | '"' -> skipString s i
            | '{'
            | '[' ->
                i <- i + 1
                let mutable depth = 1

                while depth > 0 do
                    if i >= n then
                        failwith (sprintf "Dag.fromJsonl: unterminated container (opened at position %d)" start)

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
            | _ ->
                let isEnd c =
                    c = ',' || c = '}' || c = ']' || c = ' ' || c = '\t' || c = '\n' || c = '\r'

                while i < n && not (isEnd s.[i]) do
                    i <- i + 1

                i

        /// `(key, raw-value)` pairs of a flat top-level object; values kept verbatim.
        let topFields (line: string) : (string * string) list =
            let s = line.Trim()
            let n = s.Length
            let mutable i = 0

            let skipWs () =
                while i < n && (let c = s.[i] in c = ' ' || c = '\t' || c = '\n' || c = '\r') do
                    i <- i + 1

            skipWs ()

            if i >= n || s.[i] <> '{' then
                failwith (sprintf "Dag.fromJsonl: expected a JSON object at position %d" i)

            i <- i + 1
            let fields = ResizeArray<string * string>()
            skipWs ()

            if i < n && s.[i] = '}' then
                ()
            else
                let mutable go = true

                while go do
                    skipWs ()
                    let ks = skipString s i
                    let key = unquote (s.Substring(i, ks - i))
                    i <- ks
                    skipWs ()

                    if i >= n || s.[i] <> ':' then
                        failwith (sprintf "Dag.fromJsonl: expected ':' at position %d" i)

                    i <- i + 1
                    skipWs ()
                    let vs = skipValue s i
                    fields.Add((key, s.Substring(i, vs - i).Trim()))
                    i <- vs
                    skipWs ()

                    if i < n && s.[i] = ',' then
                        i <- i + 1
                    elif i < n && s.[i] = '}' then
                        go <- false
                    else
                        failwith (sprintf "Dag.fromJsonl: expected ',' or '}' at position %d" i)

            // First-wins on a duplicate key (Phase 45) — agree with the first-wins `JVal` decoders
            // rather than the last-wins `Map.ofList` the consumers apply.
            let seen = System.Collections.Generic.HashSet<string>()

            [ for (k, v) in fields do
                  if seen.Add k then
                      yield (k, v) ]

        /// Parse a raw JSON array-of-strings span (e.g. `["h1","h2"]` / `[]`).
        let parseStringArray (raw: string) : string list =
            let s = raw.Trim()
            let n = s.Length
            let items = ResizeArray<string>()
            let mutable i = 0

            if i < n && s.[i] = '[' then
                i <- i + 1

            let mutable go = true

            while go do
                while i < n
                      && (s.[i] = ' ' || s.[i] = ',' || s.[i] = '\t' || s.[i] = '\n' || s.[i] = '\r') do
                    i <- i + 1

                if i >= n || s.[i] = ']' then
                    go <- false
                elif s.[i] = '"' then
                    let e = skipString s i
                    items.Add(unquote (s.Substring(i, e - i)))
                    i <- e
                else
                    failwith (sprintf "Dag.fromJsonl: expected a string in the parents array at position %d" i)

            List.ofSeq items

    /// Decode the `actor` field's raw span into a typed `Actor` (Phase 320) — the canonical object
    /// form (`{"kind":"human"|"agent", ...}`) emitted by `Actor.encode`, parsed via the flat-object
    /// scanner. An unrecognised or absent `kind` is a named decode `Error`, never `Human` — a store
    /// written by a newer build may carry a kind this reader does not know, and reading it as a
    /// person would misattribute the node. The refusal surfaces as the `line N: <reason>` `Error`.
    let private actorOfRaw (raw: string) : Actor =
        let fields = Jsonl.topFields raw |> Map.ofList

        let get k =
            match Map.tryFind k fields with
            | Some v -> Jsonl.unquote v
            | None -> ""

        match get "kind" with
        | "human" -> Human(get "id")
        | "agent" -> Agent(get "model", get "version", get "id")
        | "" -> failwith "Dag.fromJsonl: the actor carries no kind"
        | kind -> failwith (sprintf "Dag.fromJsonl: unknown actor kind \"%s\"" kind)

    /// Parse JSONL back into a DAG (the `op` raw span is handed to `w.Decode`). Fully portable —
    /// runs under .NET and Fable. A decode Error or a structural fault yields a `line N: <reason>`
    /// Error, never an exception (GP4). The `op` field's raw span is preserved byte-for-byte, so a
    /// round-trip is identical.
    ///
    /// **Structural only — this does NOT verify integrity.** Nodes are keyed by their *stored* id;
    /// a tampered id, a dangling parent, or a cycle decodes to a clean `Ok` here. Run `verifyDag`
    /// afterwards (or use `fromJsonlVerified`, Phase 13) to recompute content ids and confirm every
    /// parent exists before trusting the DAG.
    let fromJsonl (w: StreamWitness<'Op, 'State, 'Rej>) (text: string) : Result<T<'Op>, string> =
        let lines =
            text.Replace("\r\n", "\n").Split('\n')
            |> Array.filter (fun l -> l.Trim() <> "")
            |> Array.toList

        let rec go i acc =
            function
            | [] -> Ok acc
            | (line: string) :: rest ->
                let parsed =
                    try
                        let fields = Jsonl.topFields line |> Map.ofList

                        let get k =
                            match Map.tryFind k fields with
                            | Some v -> v
                            | None -> failwith ("missing field " + k)

                        match w.Decode(get "op") with
                        | Error e -> Error e
                        | Ok op ->
                            Ok
                                { Id = Jsonl.unquote (get "id")
                                  Parents = Jsonl.parseStringArray (get "parents")
                                  Actor = actorOfRaw (get "actor")
                                  Op = op }
                    with ex ->
                        Error ex.Message

                match parsed with
                | Error e -> Error(sprintf "line %d: %s" i e)
                | Ok(node: DagNode<'Op>) -> go (i + 1) (Map.add node.Id node acc) rest

        go 0 Map.empty lines |> Result.map (fun nodes -> { Nodes = nodes })

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

                  // The three overlap classes — each the strict negation of one Ops.independent clause,
                  // so their union is non-empty iff the pair is NOT independent.
                  //  (1) concurrent-update: a content-write overlapping the other's content-write or read.
                  let concurrent =
                      Set.union
                          (Set.intersect fa.ContentWrites fb.ContentWrites)
                          (Set.union (Set.intersect fa.ContentWrites fb.Reads) (Set.intersect fb.ContentWrites fa.Reads))
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
                            Shape = ConcurrentUpdate }

                  for addr in c2 do
                      yield
                          { Left = a
                            Right = b
                            Address = addr
                            Shape = InsertPositionClash }

                  for addr in c3 do
                      yield
                          { Left = a
                            Right = b
                            Address = addr
                            Shape = MoveVsRemove } ]

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
        let r = region dag baseId heads
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
