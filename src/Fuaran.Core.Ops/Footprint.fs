namespace Fuaran.Core

/// The structural read/write **footprint** of an op-script (Phase 78) — the multi-agent coordination
/// invariant computed *from the script*, never separately declared (the `paramsOf` precedent). The
/// addresses are id-keys (`IdWitness.ToString` form — no `comparison` demanded of `'Id`), split by the
/// role the analysis needs to decide disjointness:
///
///   - `Reads` — node ids whose existence / identity an op depends on (a parent lookup, a move target,
///     a reorder's named children, the dup-id check on an inserted subtree). Every structural anchor is
///     also a read (the parent must exist), so a script that *creates or destroys* a node the other
///     script uses as an anchor collides through a content-vs-read overlap.
///   - `StructureWrites` — parent ids whose child-list membership / order an op changes (shifting sibling
///     positions): `InsertChild.parent`, `ReorderChildren.parent`, `MoveNode.newParent`. A **known**
///     structural position.
///   - `ContentWrites` — node ids whose own node is authored / destroyed / relocated (the identity-level
///     write): an `InsertChild`'s whole inserted subtree, a `RemoveNode`/`MoveNode` target, an
///     `UpdateNode`'s target (Phase 250). Distinct from a structure-write: it is *which node*, not *whose
///     child-list*. Two content-writes to one node collide.
///   - `UnknownParentWrites` — the targets of `RemoveNode` / `MoveNode` / `UpdateNode`. Removing or moving
///     a node also rewrites its *source* parent's child-list, and rewriting a node in place rewrites the
///     child-list entry it occupies, but that parent (and every ancestor relationship) is a
///     **tree fact the pure script cannot name** — so such an op is the conservative case: it collides
///     with *every* structural write in a concurrent script. THE pinned over-approximation (STABILITY.md
///     "Op-script footprint + independence").
///   - `SlotReads` / `SlotWrites` (Phase 340) — the SLOTS an op reads or writes, a slot being a
///     `(node id, slot name)` pair: a named field of the node, or the key a keyed child sits under. A
///     write to a slot is a write to PART of the node, so against the other four kinds it is read as a
///     write of the node — a slot write of `n` collides with a content write, a structure write or a
///     read of `n`, and a slot read of `n` with a content write of `n` — but against another slot access
///     it is compared AT THE SLOT: two writes to different slots of one node commute, and only a write
///     and an access of the SAME slot collide (`Interference.SlotClash`). No skeleton op writes a slot —
///     every skeleton op rewrites a node whole, and a pure script cannot say which part of a payload
///     changed — so `Ops.footprint` and `Ops.footprintKeyed` leave both sets empty, and a domain op that
///     writes a field by name declares it with `Footprint.slotEdit` / `Footprint.readingSlot`. A domain
///     that declares no slot access gets the four-set verdict it always got.
///
/// **Conservativity is the contract:** `footprint` over-approximates — it records more collisions than a
/// tree-aware analysis would. `Ops.independent = true` is therefore a *promise* (the scripts provably
/// commute); `independent = false` is always a safe answer, never a defect report.
type Footprint =
    {
        /// Ids whose existence an op depends on. A content write of one of them in the other script
        /// collides.
        Reads: Set<string>
        /// Named parents whose child list an op changes. The same parent written by both scripts
        /// collides.
        StructureWrites: Set<string>
        /// Ids of nodes an op authors, destroys, relocates or rewrites — every id of an inserted
        /// subtree. Collides with the same id written or read by the other script.
        ContentWrites: Set<string>
        /// Ids standing for a parent the script cannot name (removes, moves, in-place rewrites).
        /// Non-empty, it collides with ANY structure or unknown-parent write of the other script.
        UnknownParentWrites: Set<string>
        /// `(node id, slot)` pairs read without the whole node. Always empty for a skeleton op.
        SlotReads: Set<string * string>
        /// `(node id, slot)` pairs written without the whole node; writes to different slots of one
        /// node commute. Always empty for a skeleton op.
        SlotWrites: Set<string * string>
    }

/// One clause of `Ops.independent` that two footprints fail, with the addresses it fails on
/// (Phase 248) — what `Ops.interference` reports, so a refused party learns HOW its script
/// collides and not only with whom. `left` and `right` are the first and second footprint handed
/// to `Ops.interference`; a clause that is an overlap carries the one shared set, which is the
/// address set on each side. The cases are the clauses of `independent`, in its order, and nothing
/// else: a footprint pair that fails no clause is independent, and one that fails any is not.
[<RequireQualifiedAccess>]
type Interference =
    /// Both scripts content-write the same nodes — author, destroy, relocate or rewrite them:
    /// `left.ContentWrites ∩ right.ContentWrites`.
    | SameTarget of targets: Set<string>
    /// The left script content-writes nodes the right script reads (as a parent, a move target, a
    /// reorder's child, an inserted id's duplicate check): `left.ContentWrites ∩ right.Reads`.
    | LeftWritesRightReads of addresses: Set<string>
    /// The right script content-writes nodes the left script reads: `left.Reads ∩ right.ContentWrites`.
    | RightWritesLeftReads of addresses: Set<string>
    /// Both scripts write the child list of the same named parent, so they shift the same siblings
    /// (the pinned same-parent rule): `left.StructureWrites ∩ right.StructureWrites`.
    | SameParent of parents: Set<string>
    /// The left script removes, moves or rewrites in place nodes whose parent it cannot name, and the
    /// right script writes structure somewhere (the pinned unknown-parent over-approximation):
    /// `relocated` is the left's `UnknownParentWrites`, `structural` the right's `StructureWrites ∪
    /// UnknownParentWrites`. No address is shared — that is the clause's point.
    | LeftUnknownParent of relocated: Set<string> * structural: Set<string>
    /// The mirror of `LeftUnknownParent`: the right script relocates, the left writes structure.
    /// `structural` is the left's `StructureWrites ∪ UnknownParentWrites`, `relocated` the right's
    /// `UnknownParentWrites`.
    | RightUnknownParent of structural: Set<string> * relocated: Set<string>
    /// Both scripts access the same SLOT of a node and at least one writes it (Phase 340):
    /// `left.SlotWrites ∩ right.SlotWrites`, `left.SlotWrites ∩ right.SlotReads` and
    /// `left.SlotReads ∩ right.SlotWrites`, as one set of `(node, slot)` pairs — the exact slot a
    /// repair has to look at, not the node. Two slot writes to DIFFERENT slots of one node fail no
    /// clause.
    | SlotClash of slots: Set<string * string>
    /// The left script accesses, through a slot, nodes the right script touches WHOLE (Phase 340): the
    /// nodes of `left.SlotWrites` the right content-writes, reads or structure-writes, and the nodes
    /// of `left.SlotReads` the right content-writes. A whole-node write is a write of every slot, so
    /// it serialises against each of them — the conservative default every four-set footprint keeps.
    | LeftSlotsRightNode of nodes: Set<string>
    /// The mirror of `LeftSlotsRightNode`: the right script accesses slots of nodes the left touches
    /// whole.
    | RightSlotsLeftNode of nodes: Set<string>

/// The footprint builders (Phase 315) — the address shapes `Ops.footprint` folds a skeleton op into,
/// public so a domain whose op vocabulary is NOT `SkeletonOp` lowers its own ops into the same
/// `Footprint` without rebuilding the record by hand. Every address is an id key (the
/// `IdWitness.ToString` form). The builders are the skeleton clauses' shapes: `insertUnder`,
/// `removeNode` and `moveTo` are exactly `Ops.footprint` of a leaf `InsertChild`, a `RemoveNode` and a
/// `MoveNode` (held to it by the suite), and a subtree insert is the `union` of one `insertUnder` per
/// id it carries.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Footprint =

    /// No reads, no writes — independent of everything.
    let empty: Footprint =
        { Reads = Set.empty
          StructureWrites = Set.empty
          ContentWrites = Set.empty
          UnknownParentWrites = Set.empty
          SlotReads = Set.empty
          SlotWrites = Set.empty }

    /// Both footprints' addresses, kind by kind. `independent (union a b) c` holds exactly when
    /// `independent a c` and `independent b c` both do, so growing a footprint never frees a pair.
    let union (a: Footprint) (b: Footprint) : Footprint =
        { Reads = Set.union a.Reads b.Reads
          StructureWrites = Set.union a.StructureWrites b.StructureWrites
          ContentWrites = Set.union a.ContentWrites b.ContentWrites
          UnknownParentWrites = Set.union a.UnknownParentWrites b.UnknownParentWrites
          SlotReads = Set.union a.SlotReads b.SlotReads
          SlotWrites = Set.union a.SlotWrites b.SlotWrites }

    /// An in-place edit of one node's own content: a read and a content-write of `id`. Two edits of
    /// one node collide; edits of two nodes do not. NOT `Ops.footprint (UpdateNode …)`, which also
    /// records an unknown-parent write — read that clause before using this for an op that can land
    /// under a parent a concurrent op removes: a domain op shaped like this one must fold in what its
    /// removal partners destroy itself (`Footprint`'s pinned over-approximation (2)).
    let contentEdit (id: string) : Footprint =
        { empty with
            Reads = Set.singleton id
            ContentWrites = Set.singleton id }

    /// A node `id` authored under `parent`: both are read, the parent's child-list is a known
    /// structure-write, and `id` is content-written. `Ops.footprint [ InsertChild(parent, leaf) ]`.
    let insertUnder (parent: string) (id: string) : Footprint =
        { empty with
            Reads = Set.ofList [ parent; id ]
            StructureWrites = Set.singleton parent
            ContentWrites = Set.singleton id }

    /// The node `id` destroyed: read and content-written, and its source parent — which the script
    /// cannot name — recorded as the pinned unknown-parent write. `Ops.footprint [ RemoveNode id ]`.
    let removeNode (id: string) : Footprint =
        { empty with
            Reads = Set.singleton id
            ContentWrites = Set.singleton id
            UnknownParentWrites = Set.singleton id }

    /// The node `id` relocated under `newParent`: both read, the destination's child-list a known
    /// structure-write, `id` content-written, and its source parent the unknown-parent write.
    /// `Ops.footprint [ MoveNode(id, newParent) ]`.
    let moveTo (id: string) (newParent: string) : Footprint =
        { empty with
            Reads = Set.ofList [ id; newParent ]
            StructureWrites = Set.singleton newParent
            ContentWrites = Set.singleton id
            UnknownParentWrites = Set.singleton id }

    /// Ids a script READS and writes nothing at (Phase 313) — the references an op writes into a
    /// node. Writing a reference to `x` depends on `x` existing, so the reference is a read of `x`,
    /// and a concurrent script that destroys `x` then fails `independent` with it
    /// (`Interference.RightWritesLeftReads`). A domain op that edits a node's references is
    /// `union (contentEdit id) (reading referenced)`: `contentEdit` alone carries no unknown-parent
    /// write, so without the read such an op is independent of the removal of the node it now
    /// references, and the two merge into a dangling reference. `Ops.footprintReferenced` adds the
    /// same reads to the skeleton ops' footprint.
    let reading (ids: string list) : Footprint = { empty with Reads = Set.ofList ids }

    /// An in-place edit of ONE SLOT of a node (Phase 340) — a named field, or the key a keyed child
    /// sits under: a read and a write of the slot `(id, slot)`, and nothing at the node. Two edits of
    /// different slots of one node commute; two edits of one slot collide (`Interference.SlotClash`,
    /// naming the slot); and an edit of any slot of `id` collides with a whole-node access of `id` —
    /// a `contentEdit id`, a `removeNode id`, an `insertUnder id _`, a `reading [ id ]` — because a
    /// whole-node write is a write of every slot (`Interference.LeftSlotsRightNode` and its mirror).
    /// A domain op that rewrites a node's field by name lowers to this where it used to lower to
    /// `contentEdit id`; `contentEdit` keeps meaning the whole node, and every other builder keeps
    /// recording the whole node, so a domain that never calls this folds exactly as before. NOT what
    /// any skeleton op does: an `UpdateNode` rewrites its target whole, and a pure script cannot say
    /// which part of the payload changed, so `Ops.footprint` never records a slot.
    ///
    /// Like `contentEdit`, it carries NO unknown-parent write, and the same obligation follows. The
    /// removal of an ANCESTOR of `id` names neither `id` nor any slot of it, so the two footprints fail
    /// no clause of `independent`, yet the scripts do not commute: the edit lands in one order and is
    /// refused in the other. A domain op shaped like this one, which can land under a node a concurrent
    /// op removes, must fold in what its removal partners destroy itself (`Footprint`'s pinned
    /// over-approximation (2)) — read `Ops.footprint`'s `UpdateNode` clause, which records the
    /// unknown-parent write for exactly this pair.
    let slotEdit (id: string) (slot: string) : Footprint =
        { empty with
            SlotReads = Set.singleton (id, slot)
            SlotWrites = Set.singleton (id, slot) }

    /// A read of ONE SLOT of a node and no write (Phase 340): a reader of one field, which does not
    /// depend on a write to another. It collides with a write of that slot (`SlotClash`) and with a
    /// whole-node content write of `id` (`LeftSlotsRightNode` when the reader is the left side), and
    /// with nothing else — where `reading [ id ]` collides with every slot write of `id` too.
    let readingSlot (id: string) (slot: string) : Footprint =
        { empty with
            SlotReads = Set.singleton (id, slot) }

    /// The nodes a set of slots belongs to (Phase 340) — `(node, slot)` pairs read as nodes.
    let slotNodes (slots: Set<string * string>) : Set<string> = Set.map fst slots

    /// The slots two footprints CLASH on (Phase 340) — `Interference.SlotClash`'s set: the slots both
    /// write, and the slots one writes and the other reads, either way round. Symmetric. THE one
    /// computation behind `Ops.interference`'s clause and `Dag.conflicts`' `SlotClash` shape, so
    /// arbitration and the fold cannot disagree about a slot.
    let slotClash (a: Footprint) (b: Footprint) : Set<string * string> =
        Set.unionMany
            [ Set.intersect a.SlotWrites b.SlotWrites
              Set.intersect a.SlotWrites b.SlotReads
              Set.intersect a.SlotReads b.SlotWrites ]

    /// The nodes `a` accesses through a slot that `b` touches WHOLE (Phase 340) —
    /// `Interference.LeftSlotsRightNode`'s set, with `a` on the left: the nodes of `a.SlotWrites` that
    /// `b` content-writes, reads or structure-writes, and the nodes of `a.SlotReads` that `b`
    /// content-writes. `slotsAgainstNode b a` is the mirror. Shared by `Ops.interference` and
    /// `Dag.conflicts` for the reason `slotClash` is.
    let slotsAgainstNode (a: Footprint) (b: Footprint) : Set<string> =
        Set.union
            (Set.intersect (slotNodes a.SlotWrites) (Set.unionMany [ b.ContentWrites; b.Reads; b.StructureWrites ]))
            (Set.intersect (slotNodes a.SlotReads) b.ContentWrites)
