namespace Fuaran.Core

// ============================================================================
//  The id-scoped write gate (Phase 318) — which nodes an actor may WRITE, as a
//  policy over ids, decided from the op's own footprint and the tree it lands on.
//
//  Three domains wrote this gate by hand, near-verbatim: a set of LOCKED ids that
//  are never writable and an optional ALLOW-LIST outside which nothing is, with a
//  per-op `targets` function naming the ids an op writes. The targets are not a
//  domain fact. They are what `Ops.footprint` already computes from the script —
//  the parents whose child lists change, the nodes authored, destroyed, relocated
//  or rewritten — plus the two things the pure script cannot name and the tree
//  can: the subtree a removal destroys, and the ancestors a lock or an allowance
//  reaches down through. So the gate is Core's, and only the two sets are the
//  domain's.
//
//  SUBTREE SEMANTICS. A lock on a node locks its subtree, and an allowance on a
//  node allows its subtree: a target is locked when it or any ancestor is in
//  `Locked`, and (under an allow-list) writable when it or any ancestor is in
//  `Writable`. A node an op CREATES has the ancestors it is created under.
//  The ancestors are read along the walk the scope locates through (Phase 517):
//  under a keyed scope a node held in a keyed position has its holder, and the
//  holder's ancestors, as its chain, so a lock on a holder covers its keyed
//  subtree as it covers its structural one.
//
//  FSharp.Core + Fuaran.Core.Tree only, as `Ops` is; Fable-clean.
// ============================================================================

/// The mode of a write gate (Phase 391; an `option` before `0.37.0`, `None` for the deny-list and
/// `Some ids` for the allow-list): what is writable besides what the gate locks. The locks are the
/// gate's `Locked` in both modes, so the deny-list case carries no set of its own — a second one
/// would be a second place to say what is locked, and a lock wins inside an allow-list.
[<RequireQualifiedAccess>]
type WriteScope =
    /// Deny-list mode: everything not locked is writable. The deny list is the gate's `Locked`.
    | DenyList
    /// Allow-list mode (default-deny): only these ids and their subtrees are writable, and a lock
    /// still wins inside one. An allowance on a node covers removing it and moving it out: the
    /// removed node's source parent, whose child list the removal rewrites, need not be on the list
    /// (DECISIONS.md D138, which answers D119.7).
    | AllowList of Set<string>

/// An id-scoped write policy (Phase 318): ids keyed by `IdWitness.ToString`, as `Footprint`'s are,
/// so no `comparison` is demanded of the id.
type WriteGate =
    {
        /// Never writable, and neither is anything under them.
        Locked: Set<string>
        /// What is writable besides the locks: everything (`DenyList`), or only the listed ids and
        /// their subtrees (`AllowList`).
        Writable: WriteScope
    }

/// Why the write gate refused an op (Phase 318). Each case names the target it refused and the
/// set it refused against, so a refused actor learns what it may write instead.
[<RequireQualifiedAccess>]
type WriteDenial =
    /// The op writes `target`, and `lockedBy` — the target itself, or the nearest of its ancestors
    /// that is locked — is in `Locked`.
    | Locked of target: string * lockedBy: string
    /// The gate is an allow-list, and neither `target` nor any of its ancestors is on it. `writable`
    /// is the allow-list, in id order.
    | NotWritable of target: string * writable: string list

/// A gated apply's refusal (Phase 318): the gate's denial, before the reducer ran, or the reducer's
/// own rejection of an op the gate allowed.
[<RequireQualifiedAccess>]
type GatedApplyFailure<'Id> =
    /// The gate refused the op; `Ops.apply` was not run.
    | Denied of WriteDenial
    /// The gate allowed the op and `Ops.apply` rejected it.
    | Rejected of Rejection<'Id>

/// Building and consulting a `WriteGate` (Phase 318).
[<RequireQualifiedAccess>]
module WriteGate =

    /// The permissive gate: nothing locked, no allow-list.
    let allowAll: WriteGate =
        { Locked = Set.empty
          Writable = WriteScope.DenyList }

    /// Deny-list mode: everything writable except these ids and their subtrees.
    let lockOnly (ids: string list) : WriteGate =
        { Locked = Set.ofList ids
          Writable = WriteScope.DenyList }

    /// Allow-list mode (default-deny): only these ids and their subtrees are writable.
    let allowOnly (ids: string list) : WriteGate =
        { Locked = Set.empty
          Writable = WriteScope.AllowList(Set.ofList ids) }

    /// The ids ONE non-batch op writes where it lands, read from the op's view of that tree (Phase
    /// 512): `Ops.footprint`'s structure writes (the parents whose child lists change), content writes
    /// (the nodes it authors, destroys, relocates or rewrites) and unknown-parent writes, the nodes of
    /// its slot writes, and — what the pure footprint cannot name, recorded as its pinned
    /// over-approximation (2) — the subtree a `RemoveNode` destroys, the subtree the view located.
    /// Under a keyed scope the footprint and the destroyed subtree are the keyed walks (Phase 517).
    let private targetsOfOne (scope: OpScope<'Node, 'Id>) (v: OpView<'Node, 'Id>) : Set<string> =
        let fp = Ops.footprint Policy.none scope [ v.Op ]

        Set.unionMany
            [ fp.StructureWrites
              fp.ContentWrites
              fp.UnknownParentWrites
              Footprint.slotNodes fp.SlotWrites
              Ops.destroyedBy (OpScope.ids scope) v
              |> List.map scope.Ids.ToString
              |> Set.ofList ]

    /// The chain a target's policy is read along, nearest first: the target, then its ancestors in
    /// `root` along the walk the scope locates through. For a node `op` creates — absent from
    /// `root`, inside an `InsertChild`'s graft — the chain runs through the graft up to the insert's
    /// parent and on through the parent's ancestors.
    let private chainOf
        (scope: OpScope<'Node, 'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        (target: string)
        : string list =
        let idw = scope.Ids
        let walk = OpScope.locate scope

        let inTree (id: 'Id) (node: 'Node) =
            Tree.path walk idw id node |> Option.map (List.rev >> List.map idw.ToString)

        match inTree (idw.OfString target) root with
        | Some chain -> chain
        | None ->
            match op with
            | InsertChild(parent, graft) ->
                let inGraft = inTree (idw.OfString target) graft |> Option.defaultValue [ target ]

                let above = inTree parent root |> Option.defaultValue [ idw.ToString parent ]
                inGraft @ above
            | _ -> [ target ]

    /// The verdict on one non-batch op over `root`: the first target, in id order, that is locked
    /// (through itself or an ancestor), else — under an allow-list — the first that is not covered.
    let private decideOne
        (gate: WriteGate)
        (scope: OpScope<'Node, 'Id>)
        (v: OpView<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, WriteDenial> =
        let targets = targetsOfOne scope v |> Set.toList
        let chains = targets |> List.map (fun t -> t, chainOf scope v.Op root t)

        let locked =
            chains
            |> List.tryPick (fun (t, chain) ->
                chain
                |> List.tryFind gate.Locked.Contains
                |> Option.map (fun by -> WriteDenial.Locked(t, by)))

        match locked, gate.Writable with
        | Some denial, _ -> Error denial
        | None, WriteScope.DenyList -> Ok()
        | None, WriteScope.AllowList writable ->
            match
                chains
                |> List.tryFind (fun (_, chain) -> not (List.exists writable.Contains chain))
            with
            | Some(t, _) -> Error(WriteDenial.NotWritable(t, Set.toList writable))
            | None -> Ok()

    /// The gate's walk over an op (Phase 512): a `Batch` taken op by op, each step's view located
    /// on the tree the steps before it produced, `judge` reading the view, and the engine stepping
    /// through the SAME view under the scope, so the gate and the reducer see one location of each
    /// op. The runner stops at the first denial (`Choice1Of2`) or at the first op the reducer refuses
    /// (`Choice2Of2`, carrying what `judge` said of it) — the reducer refuses there, whatever the
    /// gate would say of the ops after it.
    let private walk
        (scope: OpScope<'Node, 'Id>)
        (judge: OpView<'Node, 'Id> -> 'Node -> Result<'Out, WriteDenial>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        =
        let s = Ops.walksOf scope
        let canHold = Ops.canHoldOf scope

        let rec steps (o: SkeletonOp<'Node, 'Id>) =
            match o with
            | Batch inner -> List.collect steps inner
            | _ -> [ o ]

        Ops.run
            (fun node o ->
                let v = Ops.view s o node

                match judge v node with
                | Error denial -> Error(Choice1Of2 denial)
                | Ok out ->
                    match Ops.applyView canHold s v node with
                    | Ok next -> Ok(next, out)
                    | Error _ -> Error(Choice2Of2 out))
            (steps op)
            root

    /// The ids `op` writes when it lands on `root` under `scope` (Phase 318; the scope, Phase 517) —
    /// the targets the gate decides on: `Ops.footprint`'s structure, content and unknown-parent
    /// writes and its slot writes' nodes, with the subtree a `RemoveNode` destroys added from the
    /// tree. A `Batch` is taken op by op, each over the tree the ops before it produced, and stops at
    /// the first op the scope's engine does not apply (the reducer refuses there, whatever its
    /// targets). Every id an op creates, destroys, rewrites or whose child list it changes is a
    /// target, except the SOURCE parent of a removed or moved node — a tree fact the footprint
    /// records only as the target's unknown-parent write — whose lock reaches the target anyway,
    /// because the target is in that parent's subtree.
    let targetsOf (scope: OpScope<'Node, 'Id>) (op: SkeletonOp<'Node, 'Id>) (root: 'Node) : Set<string> =
        let r = walk scope (fun v _ -> Ok(targetsOfOne scope v)) op root

        let last =
            match r.Refused with
            | Some(Choice2Of2 targets) -> targets
            | _ -> Set.empty

        Set.unionMany (last :: r.Outs)

    /// Decide whether `op` may write what it writes on `root` under `scope` (Phase 318): `Error` with
    /// the first denial, `Ok ()` otherwise. A target is refused `Locked` when it or an ancestor is
    /// locked, and — under an allow-list — `NotWritable` when neither it nor any ancestor is on the
    /// list; the lock wins where both hold. The ancestors are read along the walk the scope locates
    /// through, so under a keyed scope a lock on a holder covers its keyed subtree (Phase 517). A
    /// `Batch` is decided op by op over the trees the earlier ops produce, so an op inside it is
    /// judged where it actually lands; the first op that does not apply ends the decision (`Ok`),
    /// and the reducer refuses it.
    let decide
        (gate: WriteGate)
        (scope: OpScope<'Node, 'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, WriteDenial> =
        match (walk scope (decideOne gate scope) op root).Refused with
        | Some(Choice1Of2 denial) -> Error denial
        | _ -> Ok()

    /// Apply `op` under `policy` and `scope` only if the gate allows it (Phase 318): the gate is
    /// consulted FIRST and a denial returns before `Ops.apply` runs; an allowed op's reducer
    /// rejection is `Rejected`. Default-deny by shape: there is no path from a denied op to an
    /// applied tree.
    let applyGated
        (gate: WriteGate)
        (policy: Policy<'Node, 'Id>)
        (scope: OpScope<'Node, 'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, GatedApplyFailure<'Id>> =
        match decide gate scope op root with
        | Error denial -> Error(GatedApplyFailure.Denied denial)
        | Ok() -> Ops.apply policy scope op root |> Result.mapError GatedApplyFailure.Rejected

    /// A denial as the guidance an agent repairs from: one sentence naming the target and why, and as
    /// alternatives the allow-list (for `NotWritable`) or nothing (for `Locked`).
    let guidance (denial: WriteDenial) : RejectionGuidance =
        match denial with
        | WriteDenial.Locked(target, by) when target = by ->
            { Message = "'" + target + "' is locked and cannot be written"
              Alternatives = [] }
        | WriteDenial.Locked(target, by) ->
            { Message =
                "'"
                + target
                + "' is inside '"
                + by
                + "', which is locked, and cannot be written"
              Alternatives = [] }
        | WriteDenial.NotWritable(target, writable) ->
            { Message = "'" + target + "' is not inside anything this actor may write"
              Alternatives = writable }
