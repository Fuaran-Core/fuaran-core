namespace Fuaran.Core

/// Where a refused move's new parent sits relative to the node being moved (Phase 315) — the
/// relation `Rejection.WouldNestUnderSelf` carries.
[<RequireQualifiedAccess>]
type NestRelation =
    /// The new parent IS the moved node: `MoveNode(x, x)`.
    | Self
    /// The new parent is a proper descendant of the moved node.
    | Descendant

/// The recoverable error-envelope contract — the single most valuable thing to
/// standardise across domains (it is the AI-feedback protocol). Every rejection
/// *names the failure and enumerates the valid alternatives*, so an orchestrator
/// reads one rejection shape regardless of tier. Domain defect codes that don't fit
/// the skeleton plug in through `Rejected (code, message)`. Total — failures are
/// data, never exceptions.
///
/// Phase 315 gave the envelope its operations — `Rejection.code` (a stable code per class),
/// `Rejection.explain` (the agent-readable `RejectionGuidance`), and the canonical encoder beside the
/// wire (`RejectionCodec` in `Fuaran.Core.AiSurface`) — so a domain stops writing its own explainer.
type Rejection<'Id> =
    /// `target` is not in the tree; `addressable` enumerates the ids that are.
    ///
    /// `addressable` is the WHOLE tree's ids, and that is deliberate (Phase 248): it is a repair aid
    /// for the single-op `canApply` path, where a model repairs one op and needs every id it could
    /// have meant. It therefore grows with the document rather than with the error. A party that
    /// reports a stale op-script to others — a scheduler telling N proposers why arbitration refused
    /// them — reports `Arbitration.stale`'s bounded envelope instead.
    | UnknownNode of target: 'Id * addressable: 'Id list
    /// Inserting / moving a node whose id already exists in the tree.
    | DuplicateId of 'Id
    /// The root cannot be removed or moved.
    | CannotRemoveRoot
    /// A move whose new parent is the target itself or one of its descendants. `relation` says which
    /// (Phase 315): a move under itself and a move into its own subtree are different mistakes, and a
    /// caller that told them apart used to re-run the check to learn which it had made.
    | WouldNestUnderSelf of target: 'Id * relation: NestRelation
    /// A node that holds children while `canHold` refuses it (Phase 251). Two sites raise it, and
    /// `target` names a node in a different place at each: the (new) PARENT of an insert/move, which
    /// is a node of the tree; or — since Phase 161 (DECISIONS D38) — an interior node of the SUBTREE
    /// an `InsertChild` carries, which is a node of the caller's own graft. `kindTag` is always that
    /// node's own. Only the container-aware `applyContained` / `canApplyContained` raise this; the
    /// plain `apply` / `canApply` treat every node as able to hold children.
    ///
    /// The graft-interior site is defined by `Ops.firstUncontained`, the single definition of the
    /// shape; its diff-side sibling is `Diff.DiffError.TargetNotAContainer`, which names the same
    /// offender under the same payload (Phase 228), and `Conformance.diffContainedLaws` holds the two
    /// refusals to the same trees.
    | NotAContainer of target: 'Id * kindTag: string
    /// A reorder whose proposed order is not a permutation of the parent's children.
    | ReorderMismatch of parent: 'Id * expected: 'Id list * got: 'Id list
    /// Domain-side extension point: a per-kind property-edit rejection.
    | Rejected of code: string * message: string
    /// A `RemoveNode` / `MoveNode` whose target is held directly in a KEYED position of `holder`
    /// (Phase 286). The keyed engine (`Ops.applyContainedKeyed`) reaches such a node — it can be
    /// updated in place, grow structural children, be a move's destination — but the skeleton ops
    /// restructure only the structural surface, and a keyed position is arity-fixed: vacating or
    /// relocating it is a domain edit. Only the keyed engine raises this; the unkeyed forms cannot
    /// see the node and answer `UnknownNode`, as they always have. Declared LAST so every existing
    /// case keeps its tag.
    | KeyedPosition of target: 'Id * holder: 'Id
    /// An insert, move or in-place rewrite that would leave `child` (of kind `childKind`) under
    /// `parent` (of kind `parentKind`) where the domain's containment grammar does not let that kind
    /// hold it (Phase 313). `legal` is what the grammar lets `parentKind` hold — the repair the
    /// envelope enumerates, as `UnknownNode` enumerates the addressable ids; `[]` when the grammar
    /// lets it hold nothing. Only the grammar forms (`Ops.applyGrammar`, `Ops.applyReferenced` and
    /// their dry runs) raise this, and always after every check the container-aware engine makes,
    /// so no operation it refuses changes class. Declared after `KeyedPosition` so every existing
    /// case keeps its tag.
    | IllegalChild of child: 'Id * childKind: string * parent: 'Id * parentKind: string * legal: string list
    /// A `RemoveNode` or an `UpdateNode` that would take away a declaration other nodes still
    /// reference (Phase 313): after it, the tree no longer declares an id it declared before, and
    /// `referrers` — in preorder — are the nodes that still refer to one. `target` is the removed
    /// node or the rewritten one. Only the reference-aware forms (`Ops.applyReferenced` and its dry
    /// runs) raise this, after every other check. Declared last.
    | StillReferenced of target: 'Id * referrers: 'Id list

/// The skeleton edit ops shared by every domain — five structural, and since Phase 250 one
/// generic in-place content edit (`UpdateNode`). Finer per-kind property edits (`SetInput`,
/// `SetParameter`, `SetVariable`, `UpdateProp`, …) stay domain-side and compose with these. `Batch` is all-or-nothing.
///
/// **Membership and order are separate concerns.** `InsertChild` and `MoveNode` change
/// which children a parent has, and both APPEND; `ReorderChildren` states the order, by
/// naming ids. Placing a node anywhere but last is therefore
/// `Batch [InsertChild …; ReorderChildren …]`.
///
/// The ordinal these two ops used to carry was removed deliberately (2026-07-26). Every
/// node has an id, every other op addresses by it, and `ReorderChildren` already stated
/// order that way — so the integer was the one place the structural surface departed from
/// the identity model the rest of the tree is built on. It also named something that does
/// not exist in the tree: children are a list, so order is structural and no index is
/// stored anywhere. An index is a projection over that list, derivable from it and only
/// meaningful against one snapshot of it, which makes it silently wrong after any
/// concurrent or preceding edit. An id is checkable; an ordinal is not.
///
/// **Content and structure are separate concerns too (Phase 250).** `UpdateNode node` rewrites
/// one node IN PLACE: the node whose id is `w.Id node` takes `node`'s own content and KEEPS the
/// children it already has — the payload's children are not read, because changing membership or
/// order is what the other four ops are for. The target is the payload's own id rather than a
/// second field, for the reason the ordinal above was removed: an id stated twice can disagree,
/// and an id stated once cannot. It is declared LAST so every existing case keeps its tag. Before
/// it, redefining a node was `Batch [RemoveNode id; InsertChild(parent, node')]`, which moved the
/// node to the end of its parent and dirtied the parent as well as the node.
type SkeletonOp<'Node, 'Id> =
    /// Append `node` — a whole subtree — as `parent`'s LAST child. An id the subtree repeats, or
    /// one the tree already carries, is refused first (`DuplicateId`); then an absent `parent`.
    | InsertChild of parent: 'Id * node: 'Node
    /// Remove `target` and its whole subtree. The root is refused (`CannotRemoveRoot`), as is an
    /// absent id (`UnknownNode`).
    | RemoveNode of target: 'Id
    /// Detach `target` with its subtree and APPEND it under `newParent`. Refused for the root, an
    /// absent id on either side, and a `newParent` inside the moved subtree (`WouldNestUnderSelf`).
    | MoveNode of target: 'Id * newParent: 'Id
    /// Put `parent`'s children in `order`, which must be a permutation of their ids
    /// (`ReorderMismatch`, carrying the current list, otherwise). Nothing is added or dropped.
    | ReorderChildren of parent: 'Id * order: 'Id list
    /// The ops in order, each against the tree the previous one left. All-or-nothing: the first
    /// refusal is the batch's, and no earlier step survives it.
    | Batch of SkeletonOp<'Node, 'Id> list
    /// Rewrite the node whose id is `w.Id node` in place: it takes `node`'s content and keeps its
    /// own children (the payload's are not read). An absent id is `UnknownNode`.
    | UpdateNode of node: 'Node

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

// ---- Phase 315: the envelope's operations and the footprint builders ----

/// Agent-readable rejection guidance (the envelope discipline, GP5): what went wrong plus the
/// enumerated alternatives, so a refused agent can repair its emission instead of guessing.
/// Defined here since Phase 315 (it was declared in `Fuaran.Core.AiSurface`), so `Rejection.explain`
/// can return it; the namespace is unchanged, so every `RejectionGuidance` in source still means it.
type RejectionGuidance =
    {
        /// One sentence naming the failure in the domain's `RejectionNouns`; for a domain `Rejected`,
        /// the domain's own message verbatim.
        Message: string
        /// The repair choices the envelope enumerates — addressable ids, the children a reorder must
        /// permute, a grammar's legal kinds, the referrers to clear — already rendered; `[]` where it
        /// enumerates none.
        Alternatives: string list
    }

/// The words `Rejection.explain` speaks in (Phase 315) — what the domain calls a node and its root,
/// so the same explainer serves a document ("block", "document root") and a sheet ("cell",
/// "workbook"). `RejectionNouns.generic` is "node" / "root".
type RejectionNouns =
    {
        /// The word for one node. The sentences put a fixed `a` / `no` before it and pluralise it by
        /// appending `s`, so pick a word that reads correctly both ways.
        Node: string
        /// The word for the tree's root, spoken after `the`.
        Root: string
    }

/// The stock nouns.
[<RequireQualifiedAccess>]
module RejectionNouns =

    /// "node" / "root".
    let generic: RejectionNouns = { Node = "node"; Root = "root" }

/// The operations on a `Rejection` (Phase 315): its stable code, and its guidance. Its canonical
/// wire encoding is `RejectionCodec` in `Fuaran.Core.AiSurface`, the package that carries the wire.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Rejection =

    /// The stable code of a rejection — one per class, camel-case, and for the skeleton classes the
    /// `$type` the canonical encoder writes: `unknownNode`, `duplicateId`, `cannotRemoveRoot`,
    /// `wouldNestUnderSelf`, `notAContainer`, `reorderMismatch`, `keyedPosition` — and since Phase 313
    /// `illegalChild` and `stillReferenced`. Two answers differ
    /// from the case tag. `reorderOnLeaf` is a `ReorderMismatch` whose parent holds no children
    /// (`expected = []`): nothing to reorder, rather than a wrong permutation, so a caller need not
    /// pre-check the leaf to tell them apart. And a domain's `Rejected` answers its OWN code verbatim
    /// — that is what the case is for. The skeleton words are this envelope's vocabulary: a domain
    /// code should not reuse one.
    let code (r: Rejection<'Id>) : string =
        match r with
        | UnknownNode _ -> "unknownNode"
        | DuplicateId _ -> "duplicateId"
        | CannotRemoveRoot -> "cannotRemoveRoot"
        | WouldNestUnderSelf _ -> "wouldNestUnderSelf"
        | NotAContainer _ -> "notAContainer"
        | ReorderMismatch(_, [], _) -> "reorderOnLeaf"
        | ReorderMismatch _ -> "reorderMismatch"
        | Rejected(code, _) -> code
        | KeyedPosition _ -> "keyedPosition"
        | IllegalChild _ -> "illegalChild"
        | StillReferenced _ -> "stillReferenced"

    /// The rejection as guidance an agent can act on: a message naming the failure in the domain's
    /// `nouns`, and the alternatives the envelope enumerates — the addressable ids of an
    /// `UnknownNode`, the children a reorder must permute. `idText` renders an id. A domain's
    /// `Rejected` is its own message, with no alternatives (it carries none). Total.
    let explain (idText: 'Id -> string) (nouns: RejectionNouns) (r: Rejection<'Id>) : RejectionGuidance =
        let q (i: 'Id) = "'" + idText i + "'"

        match r with
        | UnknownNode(target, addressable) ->
            { Message = "no " + nouns.Node + " " + q target + " exists"
              Alternatives = addressable |> List.map idText }
        | DuplicateId d ->
            { Message = "a " + nouns.Node + " " + q d + " already exists; mint a fresh id"
              Alternatives = [] }
        | CannotRemoveRoot ->
            { Message = "the " + nouns.Root + " cannot be removed or moved"
              Alternatives = [] }
        | WouldNestUnderSelf(target, NestRelation.Self) ->
            { Message = "a " + nouns.Node + " cannot be moved under itself (" + q target + ")"
              Alternatives = [] }
        | WouldNestUnderSelf(target, NestRelation.Descendant) ->
            { Message = "moving " + q target + " there would nest it inside its own subtree"
              Alternatives = [] }
        | NotAContainer(target, kindTag) ->
            { Message = q target + " (" + kindTag + ") cannot hold children"
              Alternatives = [] }
        | ReorderMismatch(parent, [], _) ->
            { Message = q parent + " has no children to reorder"
              Alternatives = [] }
        | ReorderMismatch(parent, expected, _) ->
            { Message = "a reorder of " + q parent + " must be a permutation of its current children"
              Alternatives = expected |> List.map idText }
        | Rejected(_, message) -> { Message = message; Alternatives = [] }
        | KeyedPosition(target, holder) ->
            { Message =
                q target
                + " sits in a keyed position of "
                + q holder
                + "; vacating or relocating it is a domain edit"
              Alternatives = [] }
        | IllegalChild(child, childKind, parent, parentKind, legal) ->
            { Message =
                q child
                + " ("
                + childKind
                + ") cannot sit under "
                + q parent
                + " ("
                + parentKind
                + ")"
              Alternatives = legal }
        | StillReferenced(target, referrers) ->
            { Message =
                q target
                + " declares what other "
                + nouns.Node
                + "s still reference; remove or retarget the references first"
              Alternatives = referrers |> List.map idText }

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

/// A domain's REFERENCES (Phase 313): which ids a node refers to, and which ids it declares for
/// others to refer to — the cross-node links a calculation, a feature tree, a set of defined terms or
/// a cross-referenced document carries beside its containment. A witness of its own for the reason
/// `KeyedWitness` is (Phase 189): the engine never rebuilds through it, it only reads it.
///
/// - `RefsOf` — the ids this node refers to, in the domain's own order. `[]` for a node that refers
///   to nothing.
/// - `DeclsOf` — the ids this node declares as reference targets. A node that is itself the target
///   declares its own id (`fun n -> [ w.Id n ]` for a domain where every node is referable); a
///   domain whose names are not node ids declares the names.
///
/// A reference RESOLVES when some node of the tree declares its id. What Core does with the witness:
/// `Validator.referenceIntegrity` reports the dangling, unused, forward and cyclic references;
/// `Ops.footprintReferenced` reads every id a script writes a reference to; and the reference-aware
/// engine (`Ops.applyReferenced`) refuses a `RemoveNode` that would leave a reference dangling.
type RefWitness<'Node, 'Id> =
    {
        /// The ids this node refers to, in the domain's order; each resolves when some node's
        /// `DeclsOf` carries it.
        RefsOf: 'Node -> 'Id list
        /// The ids this node offers as reference targets — its own id where every node is referable,
        /// the names it defines where names are not node ids.
        DeclsOf: 'Node -> 'Id list
    }

/// The generic apply engine over the skeleton ops. Total: every failure is a typed
/// `Rejection` envelope. Generic over the `NodeWitness` / `IdWitness` — no domain
/// `NodeKind` is ever in scope.
module Ops =

    // ---- validation (Phase 246) ----
    // The structural checks each op must pass, factored out of `apply` so the dry-run
    // `canApply` and the mutating `apply` share one source of validation truth and can
    // never diverge. The three index/structure ops validate without building a tree;
    // `MoveNode` and `Batch` are order-dependent (a later step sees an earlier step's
    // tree), so their validation simulates through `apply` — the rejection returned is
    // exactly the one `apply` would produce.

    // ---- the two witnesses every clause below reads (Phase 286) ----
    // `w` is the STRUCTURAL witness: what a node's children are for an edit — what an insert appends
    // to, a remove filters, a reorder permutes, an update keeps. `t` is the witness the engine
    // LOCATES through — which nodes exist, where they are, which ids a graft collides with — and the
    // one it rebuilds ancestors through. The unkeyed forms pass `t = w`, so every clause reads
    // exactly what it read before Phase 286; `applyContainedKeyed` passes `Tree.traversal w keyw`,
    // and `keyed` carries the domain's `KeyedChildren` for the two questions only the keyed engine
    // asks (which node holds a keyed position, and what an `UpdateNode` payload carries into one).

    /// The first id in `node`'s subtree (preorder) that breaks uniqueness — one already carried by
    /// `root`, or one the subtree repeats within itself. `None` when the graft is clean.
    ///
    /// **Phase 137** widened this from the graft's own id to its whole subtree; **Phase 139** moved
    /// the scan itself into `Tree.graftWellFormed`, so this is now a projection of Core's ONE named
    /// definition of structural validity rather than a second, separately-maintained copy of it.
    /// The behaviour is unchanged — same seed, same preorder, same first offender — and the point of
    /// the move is that it can no longer drift from `Tree.wellFormed`, which is what a caller
    /// checks the RESULT with. **Phase 286**: over `t`, so under `applyContainedKeyed` it is
    /// `Tree.graftWellFormedKeyed` and sees keyed positions on both sides of the graft.
    ///
    /// Scope, damage and precedence are all stated where the predicate is defined
    /// (`Tree.WellFormed`); this comment deliberately does not restate them.
    let private firstDuplicateId
        (t: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (node: 'Node)
        (root: 'Node)
        : 'Id option =
        match Tree.graftWellFormed t idw node root with
        | Tree.RepeatedId d -> Some d
        | Tree.Structural -> None

    /// `firstUncontained`'s walk, over a witness of the caller's choosing: the first node `t`
    /// reaches that holds STRUCTURAL children while `canHold` refuses it. The keyed engine walks
    /// the graft's keyed subtrees too (Phase 286), since a node the keyed walk reaches is a node of
    /// the tree; "holds children" stays `w.Children`, the list `canHold` is about.
    let private firstUncontainedOver
        (t: NodeWitness<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (node: 'Node)
        : 'Node option =
        Tree.preorder t node
        |> List.tryFind (fun n -> not (List.isEmpty (w.Children n)) && not (canHold n))

    /// The first node of a graft (preorder) that HOLDS children while `canHold` refuses it — the
    /// interior offender an inserted subtree carries in. `None` when the graft's own interior
    /// already satisfies the invariant `applyContained` exists to keep ("every node with children
    /// satisfies `canHold`"). A childless node is unconstrained: the predicate answers "can this
    /// node hold children AT ALL", so a leaf that holds none says nothing.
    ///
    /// **Phase 161 (DECISIONS D38).** `canHold` used to be applied to the PARENT of an insert and
    /// to nothing inside the subtree being inserted, so a graft whose own interior node was a
    /// non-container carried the violation in and the invariant broke across an ACCEPTED operation
    /// — machine-checked as `contained_needs_op_hypothesis` in `proofs/Preservation.fst` before it
    /// was a refusal. The operator's ruling was to inspect the graft.
    ///
    /// The predicate is `Diff.toOpsContained`'s, deliberately: the diff path has walked an `after`
    /// tree for exactly this shape since Phase 09, and the accept path was the one place the check
    /// was missing — word for word Phase 137's situation with `DuplicateIdInTree`.
    ///
    /// **The single definition of the graft-containment shape (Phase 228).** Both refusals of that
    /// shape are raised from THIS function — `Rejection.NotAContainer` on the apply path (through
    /// `validateGraftContainment`) and `Diff.DiffError.TargetNotAContainer` on the diff path (through
    /// `Diff.toOpsContained`, which calls it over the whole `after` tree) — and both name the node it
    /// returns under one payload, `target * kindTag`. It is `internal` rather than `private` only so
    /// the `Diff` module can call it instead of re-stating it; it is not public surface.
    let internal firstUncontained (canHold: 'Node -> bool) (w: NodeWitness<'Node, 'Id>) (node: 'Node) : 'Node option =
        firstUncontainedOver w canHold w node

    /// The graft-containment clause of `validateInsert` (Phase 161), factored out so the check has
    /// one name and one home. `NotAContainer` names the offending node in the GRAFT — not the
    /// parent in the tree — reporting that node's own id and kind tag, which is what a caller needs
    /// to repair a subtree it authored.
    ///
    /// **Not called from the `MoveNode` arm, and that is a decision rather than an omission
    /// (D38).** A move relocates a subtree that is already in the tree, so it introduces no interior
    /// structure the tree did not already hold: a violation found inside it was carried in by an
    /// earlier insert, and refusing the move for it would be an invariant-REPAIR gate rather than a
    /// graft check. The machine-checked form of that argument is `contained_preserves`' move clause,
    /// which derives the moved subtree's containment from the tree's own and needs no hypothesis
    /// about the operation at all.
    let private validateGraftContainment
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (node: 'Node)
        : Result<unit, Rejection<'Id>> =
        match firstUncontainedOver t canHold w node with
        | Some offender -> Error(NotAContainer(w.Id offender, w.KindTag offender))
        | None -> Ok()

    let private validateInsert
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (parent: 'Id)
        (node: 'Node)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        // `Tree.ids` is preorder, so its head is the inserted node's OWN id: the widened scan
        // subsumes the pre-137 root-id check and keeps its precedence over `UnknownNode` rather
        // than quietly reordering the envelope a caller already handles.
        match firstDuplicateId t idw node root with
        | Some d -> Error(DuplicateId d)
        | None ->
            // one walk locates the parent (Phase 298; an `exists` walk then a `tryFind` walk, with
            // a `None` arm after the existence check that could not be reached, before)
            match Tree.tryFind t idw parent root with
            | None -> Error(UnknownNode(parent, Tree.ids t root))
            | Some p when not (canHold p) -> Error(NotAContainer(parent, w.KindTag p))
            // Phase 161 — the graft's own interior, checked LAST. The ordering is D38's: no
            // operation that was REFUSED before this phase changes its class, because every
            // earlier clause still fires first. Only operations that were ACCEPTED can now be
            // refused, which is what makes this a widening rather than a re-shuffling — and it
            // is what keeps Phase 137's built-collision conformance arm reaching `DuplicateId`.
            | Some _ -> validateGraftContainment canHold w t node

    /// The node whose STRUCTURAL child list holds `target`, searched over every node `t` reaches.
    /// With `t = w` this is `Tree.parentOf w`, word for word; over the keyed walk it also finds a
    /// structural parent that is itself held below a keyed position.
    let private structuralParentOf
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (root: 'Node)
        : 'Node option =
        Tree.preorder t root
        |> List.tryFind (fun n -> w.Children n |> List.exists (fun c -> idw.Equals (w.Id c) target))

    /// The node holding `target` directly in one of its KEYED positions, when `target` has no
    /// structural parent (Phase 286). Always `None` for the unkeyed forms (`keyed = None`), which
    /// therefore never pay for the search.
    let private keyedHolderOf
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (keyed: ('Node -> 'Node list) option)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (root: 'Node)
        : 'Node option =
        match keyed with
        | None -> None
        | Some keyedOf ->
            match structuralParentOf w t idw target root with
            | Some _ -> None
            | None ->
                Tree.preorder t root
                |> List.tryFind (fun n -> keyedOf n |> List.exists (fun c -> idw.Equals (w.Id c) target))

    let private validateRemove
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (keyed: ('Node -> 'Node list) option)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        if idw.Equals (w.Id root) target then
            Error CannotRemoveRoot
        elif not (Tree.exists t idw target root) then
            Error(UnknownNode(target, Tree.ids t root))
        else
            match keyedHolderOf w t keyed idw target root with
            | Some holder -> Error(KeyedPosition(target, w.Id holder))
            | None -> Ok()

    let private validateReorder
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (parent: 'Id)
        (order: 'Id list)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        match Tree.tryFind t idw parent root with
        | None -> Error(UnknownNode(parent, Tree.ids t root))
        | Some p ->
            let current = w.Children p |> List.map w.Id

            let key xs =
                xs |> List.map idw.ToString |> List.sort

            if key current <> key order then
                Error(ReorderMismatch(parent, current, order))
            else
                Ok()

    /// `MoveNode`'s checks, without building a tree (Phase 298) — what `canApply (MoveNode _)` now
    /// asks instead of simulating the move through `apply`. In the order the apply path has always
    /// refused in: moving the root (`CannotRemoveRoot`); an absent target, then an absent new
    /// parent (`UnknownNode`, enumerating the ids the walk reaches); a new parent that cannot hold
    /// children (`NotAContainer`); a new parent that is the target or below it
    /// (`WouldNestUnderSelf`); and a target held directly in a keyed position (`KeyedPosition`, the
    /// remove half's refusal). On success it hands back the subtree being moved, which the apply
    /// arm grafts.
    let private validateMove
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (keyed: ('Node -> 'Node list) option)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (newParent: 'Id)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        if idw.Equals (w.Id root) target then
            Error CannotRemoveRoot
        else
            match Tree.tryFind t idw target root with
            | None -> Error(UnknownNode(target, Tree.ids t root))
            | Some sub ->
                match Tree.tryFind t idw newParent root with
                | None -> Error(UnknownNode(newParent, Tree.ids t root))
                | Some np when not (canHold np) -> Error(NotAContainer(newParent, w.KindTag np))
                | Some _ ->
                    // newParent must not be the target itself nor any of its descendants.
                    let below = Tree.ids t sub |> List.map idw.ToString |> Set.ofList

                    if below.Contains(idw.ToString newParent) then
                        let relation =
                            if idw.ToString newParent = idw.ToString target then
                                NestRelation.Self
                            else
                                NestRelation.Descendant

                        Error(WouldNestUnderSelf(target, relation))
                    else
                        match keyedHolderOf w t keyed idw target root with
                        | Some holder -> Error(KeyedPosition(target, w.Id holder))
                        | None -> Ok sub

    /// The node an `UpdateNode` leaves behind (Phase 250): the payload's own content over the
    /// children `existing` already holds. The payload's children are never read. Its keyed
    /// positions ARE its content — `ReplaceChildren` does not touch them — so under the keyed engine
    /// they arrive with it, and `validateUpdate` checks what they carry (Phase 286).
    let private updated (w: NodeWitness<'Node, 'Id>) (existing: 'Node) (node: 'Node) : 'Node =
        w.ReplaceChildren node (w.Children existing)

    /// `UpdateNode`'s checks (Phase 250), in order: the target — the payload's own id — must be in
    /// the tree (`UnknownNode`, enumerating the ids that are); and, when the node it rewrites holds
    /// children, the rewritten node must be able to hold them (`NotAContainer`, naming the target
    /// and the NEW kind tag, since that is the kind that refuses). The second check is the
    /// container-aware engine's only: plain `apply` passes a `canHold` that admits everything.
    /// There is no duplicate-id check to make on the structural surface: the rewritten node keeps
    /// its id and its children, so the tree's id set is unchanged by construction.
    ///
    /// **Under the keyed engine (Phase 286) the payload's keyed subtrees are new content**, so two
    /// clauses follow, LAST, in the insert's order: their ids against the tree the target keeps —
    /// every id the keyed walk reaches except those below the target's own outgoing keyed positions
    /// (`DuplicateId`) — and their interior against `canHold` (`NotAContainer`). A payload that
    /// holds no keyed node skips both, which is why a domain with no keyed position is unaffected.
    let private validateUpdate
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (keyed: ('Node -> 'Node list) option)
        (idw: IdWitness<'Id>)
        (node: 'Node)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        let target = w.Id node

        match Tree.tryFind t idw target root with
        | None -> Error(UnknownNode(target, Tree.ids t root))
        | Some existing ->
            let result = updated w existing node

            if not (List.isEmpty (w.Children result)) && not (canHold result) then
                Error(NotAContainer(target, w.KindTag result))
            else
                match keyed with
                | None -> Ok()
                | Some keyedOf ->
                    match keyedOf node with
                    | [] -> Ok()
                    | incoming ->
                        // the tree as the rewrite keeps it: the keyed walk everywhere, except that
                        // the target itself is walked structurally — its outgoing keyed positions
                        // are the ones the payload replaces.
                        let kept =
                            { t with
                                Children =
                                    fun n ->
                                        if idw.Equals (w.Id n) target then
                                            w.Children n
                                        else
                                            t.Children n }

                        match Tree.firstRepeatedId idw (Tree.ids kept root) (incoming |> List.collect (Tree.ids t)) with
                        | Some d -> Error(DuplicateId d)
                        | None ->
                            match incoming |> List.tryPick (firstUncontainedOver t canHold w) with
                            | Some offender -> Error(NotAContainer(w.Id offender, w.KindTag offender))
                            | None -> Ok()

    /// The shared apply engine, parameterised by a container capability `canHold`
    /// (Phase 251). `apply` passes `(fun _ -> true)` — every node can hold children, so the
    /// behaviour is exactly as before; `applyContained` passes the domain predicate so an
    /// `InsertChild`/`MoveNode` under a leaf is a typed `NotAContainer` instead of a silent
    /// no-op (the F1 adoption finding). Since Phase 286 it also takes the witness it LOCATES
    /// through (`t`) and, for the keyed engine, the domain's keyed children (`keyed`); see the
    /// section head above.
    let rec private applyWith
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (keyed: ('Node -> 'Node list) option)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =

        let allIds () = Tree.ids t root
        let eq = idw.Equals

        match op with
        | InsertChild(parent, node) ->
            validateInsert canHold w t idw parent node root
            |> Result.bind (fun () ->
                Tree.updateNode t idw parent (fun p -> w.ReplaceChildren p (w.Children p @ [ node ])) root
                |> Option.map Ok
                |> Option.defaultValue (Error(UnknownNode(parent, allIds ()))))

        | RemoveNode target ->
            validateRemove w t keyed idw target root
            |> Result.bind (fun () ->
                match structuralParentOf w t idw target root with
                | None -> Error(UnknownNode(target, allIds ()))
                | Some p ->
                    Tree.updateNode
                        t
                        idw
                        (w.Id p)
                        (fun p ->
                            w.ReplaceChildren p (w.Children p |> List.filter (fun c -> not (eq (w.Id c) target))))
                        root
                    |> Option.map Ok
                    |> Option.defaultValue (Error(UnknownNode(target, allIds ()))))

        | ReorderChildren(parent, order) ->
            validateReorder w t idw parent order root
            |> Result.bind (fun () ->
                // the permutation is computed from the parent `updateNode` hands over, so the parent
                // is located once (Phase 298; a second `tryFind` walk with an unreachable `None` arm,
                // before)
                let permute (p: 'Node) =
                    let byId = w.Children p |> List.map (fun c -> idw.ToString(w.Id c), c) |> Map.ofList
                    w.ReplaceChildren p (order |> List.map (fun i -> byId.[idw.ToString i]))

                Tree.updateNode t idw parent permute root
                |> Option.map Ok
                |> Option.defaultValue (Error(UnknownNode(parent, allIds ()))))

        | MoveNode(target, newParent) ->
            // every refusal is `validateMove`'s (Phase 298); after it, remove then graft. newParent
            // is not below the target, so the removal leaves it in the tree for the graft.
            validateMove canHold w t keyed idw target newParent root
            |> Result.bind (fun sub ->
                applyWith canHold w t keyed idw (RemoveNode target) root
                |> Result.bind (fun removed ->
                    Tree.updateNode t idw newParent (fun np -> w.ReplaceChildren np (w.Children np @ [ sub ])) removed
                    |> Option.map Ok
                    |> Option.defaultValue (Error(UnknownNode(newParent, Tree.ids t removed)))))

        | Batch ops ->
            // all-or-nothing: thread the tree; abort (leaving the original) on first failure.
            let rec go node =
                function
                | [] -> Ok node
                | o :: rest ->
                    match applyWith canHold w t keyed idw o node with
                    | Ok node' -> go node' rest
                    | Error e -> Error e

            go root ops

        | UpdateNode node ->
            let target = w.Id node

            validateUpdate canHold w t keyed idw node root
            |> Result.bind (fun () ->
                Tree.updateNode t idw target (fun existing -> updated w existing node) root
                |> Option.map Ok
                |> Option.defaultValue (Error(UnknownNode(target, allIds ()))))

    /// Apply one skeleton op. Every node is treated as able to hold children.
    let apply
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        applyWith (fun _ -> true) w w None idw op root

    /// Container-aware apply (Phase 251): an `InsertChild`/`MoveNode` whose (new) parent
    /// `canHold` rejects is a typed `NotAContainer`, not a silent no-op. Domains with closed
    /// kind-sets that have leaves dispatch through this. Containment *legality* (which kinds
    /// may parent which) stays domain-side — `canHold` answers only "can this node hold
    /// children at all".
    ///
    /// **Phase 161 (DECISIONS D38): an `InsertChild` also has its GRAFT inspected.** The subtree is
    /// walked and the first interior node that holds children while `canHold` refuses it earns the
    /// same `NotAContainer`, naming that node. So `applyContained` now keeps the invariant
    /// `contained_preserves` is about — every node with children satisfies `canHold` — against a
    /// graft as well as against a parent. The one premise no engine check can discharge is
    /// `child_blind`: a `canHold` that READS the child list can admit a node at the instant it is
    /// checked and refuse it the instant it gains one. That is the domain's obligation, certified
    /// by `Conformance.containerLaws` rather than assumed.
    ///
    /// **The unkeyed form (Phase 286).** It walks `Children` alone, so a node a domain holds in a
    /// keyed position is invisible to it — to its `DuplicateId` refusal as much as to its
    /// addressing. A domain with keyed positions calls `applyContainedKeyed` with its
    /// `KeyedWitness`; for a domain with none the two answer identically.
    let applyContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        applyWith canHold w w None idw op root

    /// `applyContained` over the keyed walk (Phase 286): the domain declares its keyed positions
    /// once, in `keyw`, and the engine's own refusals see them.
    ///
    /// - **`DuplicateId` sees every id-bearing position.** The insert scan is
    ///   `Tree.graftWellFormedKeyed`, so the refusal names an id the tree holds in a keyed position
    ///   OR one the graft carries into a keyed position, as well as the structural collisions it
    ///   always named; an `UpdateNode` payload's keyed subtrees are checked the same way against the
    ///   tree the target keeps.
    /// - **The engine LOCATES through `Tree.traversal nodew keyw`.** A node held in, or below, a
    ///   keyed position can be an insert's or a reorder's parent, a move's destination, an update's
    ///   target, and a remove's or move's target when it has a structural parent. `UnknownNode`
    ///   enumerates the ids the keyed walk reaches.
    /// - **The engine EDITS through `nodew`.** Structural ops append to, filter and permute
    ///   `Children` and rebuild through `ReplaceChildren`, exactly as `applyContained` does; the
    ///   keyed positions are never added to, vacated or reordered. A `RemoveNode` / `MoveNode` of a
    ///   node held directly in a keyed position is therefore refused as `KeyedPosition`.
    /// - **Containment** (`NotAContainer` over a graft's interior) walks the keyed subtrees too.
    ///
    /// For a domain whose `KeyedChildren` is `fun _ -> []` this returns exactly what
    /// `applyContained` returns, on every op and tree (`Conformance.keyedApplyLaws` runs both).
    let applyContainedKeyed
        (keyw: KeyedWitness<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        applyWith canHold nodew (Tree.traversal nodew keyw) (Some keyw.KeyedChildren) idw op root

    let private canApplyWith
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (keyed: ('Node -> 'Node list) option)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        match op with
        | InsertChild(parent, node) -> validateInsert canHold w t idw parent node root
        | RemoveNode target -> validateRemove w t keyed idw target root
        | ReorderChildren(parent, order) -> validateReorder w t idw parent order root
        | UpdateNode node -> validateUpdate canHold w t keyed idw node root
        // Phase 298 — the move's own checks, no tree built (it simulated through `apply` before)
        | MoveNode(target, newParent) -> validateMove canHold w t keyed idw target newParent root |> Result.map ignore
        | Batch _ -> applyWith canHold w t keyed idw op root |> Result.map ignore

    /// Dry-run validation (Phase 246): would `op` be accepted against `root`? Returns the
    /// exact `Rejection` `apply` would, but builds **no** new tree for the index/structure
    /// ops — `MoveNode` included since Phase 298 (`validateMove`). Only `Batch` is
    /// order-dependent, so its check simulates through `apply` (and discards the result). The
    /// AI pre-flight surface — "is this op legal?"
    /// — without committing the (potentially large) rebuild.
    let canApply
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        canApplyWith (fun _ -> true) w w None idw op root

    /// Container-aware dry-run (Phase 251) — the `canApply` mirror of `applyContained`.
    let canApplyContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        canApplyWith canHold w w None idw op root

    /// Keyed dry-run (Phase 286) — the `canApply` mirror of `applyContainedKeyed`: the same
    /// rejection it would return, without building the tree for the index/structure ops.
    let canApplyContainedKeyed
        (keyw: KeyedWitness<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        canApplyWith canHold nodew (Tree.traversal nodew keyw) (Some keyw.KeyedChildren) idw op root

    /// Apply a sequence non-atomically under a container capability (Phase 160) — the
    /// sequence-level `applyContained`, threading `applyWith canHold` so every step sees the
    /// capability the per-op surface has consulted since Phase 251.
    ///
    /// FIRST REFUSAL WINS, and a script is **not** a `Batch`. `Batch` is all-or-nothing inside
    /// one op: it aborts and the original tree survives. A script stops at the first refusal and
    /// returns the tree built so far, so the accepted prefix is kept. On failure the payload is
    /// `(index, envelope, partial tree)`: the **index** is the 0-based position of the refused op
    /// in `ops` (it counts steps offered, so it is the position of the step that failed, not the
    /// count that succeeded), and the tree is the one the accepted prefix reached — the state the
    /// refused step was offered against. That triple is the shape every domain `applyAll`
    /// returns and the contract consumers already rely on.
    let applyAllWith
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<'Node, int * Rejection<'Id> * 'Node> =
        let rec go i node =
            function
            | [] -> Ok node
            | o :: rest ->
                match applyWith canHold w w None idw o node with
                | Ok node' -> go (i + 1) node' rest
                | Error e -> Error(i, e, node)

        go 0 root ops

    /// Dry-run a sequence under a container capability (Phase 160) — the `canApplyAll` mirror of
    /// `applyAllWith`, and the pre-flight a container-aware executor needs: it reports the same
    /// first-refusal index and the same envelope `applyAllWith` would, `NotAContainer` included.
    ///
    /// The sequence is order-dependent, so this threads through `applyWith canHold` (each step's
    /// check sees the prior step's tree) and discards the materialised tree — the same shape
    /// `canApplyAll` has carried since Phase 246, with the capability now threaded.
    let canApplyAllWith
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<unit, int * Rejection<'Id>> =
        let rec go i node =
            function
            | [] -> Ok()
            | o :: rest ->
                match applyWith canHold w w None idw o node with
                | Ok node' -> go (i + 1) node' rest
                | Error e -> Error(i, e)

        go 0 root ops

    /// Apply a sequence non-atomically, threading the tree. Every node is treated as able to hold
    /// children, so this is `applyAllWith (fun _ -> true)` and its behaviour is exactly what it
    /// was before Phase 160 — the instance relation `apply`/`applyContained` have carried since
    /// Phase 251, now at the sequence level. First refusal wins: on failure the failing index, the
    /// envelope, and the partial tree built so far.
    let applyAll
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<'Node, int * Rejection<'Id> * 'Node> =
        applyAllWith (fun _ -> true) w idw ops root

    /// Dry-run a sequence (Phase 246): report the first failing index + envelope without
    /// returning a tree. `canApplyAllWith (fun _ -> true)` since Phase 160 — the check consults
    /// no capability, so a script it certifies can still be refused by `applyContained` /
    /// `applyAllWith` at a step whose parent cannot hold children. A container-aware caller
    /// pre-flights with `canApplyAllWith` and executes with `applyAllWith`.
    let canApplyAll
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<unit, int * Rejection<'Id>> =
        canApplyAllWith (fun _ -> true) w idw ops root

    /// Dry-run a sequence over the keyed engine (Phase 247) — the `canApplyAllWith` mirror of
    /// `applyContainedKeyed`: each step is checked against the tree the earlier steps leave, under
    /// `canHold` and over the keyed walk, so it reports the first-refusal index and the envelope a
    /// keyed executor would meet — a `DuplicateId` on an id held in a keyed position, a
    /// `KeyedPosition`, a `NotAContainer` — and discards the materialised tree. It is the `canApply` a
    /// keyed domain hands `Arbitration.arbitrateWith`. For a domain whose `KeyedChildren` is
    /// `fun _ -> []` it answers exactly what `canApplyAllWith canHold` answers.
    let canApplyAllKeyed
        (keyw: KeyedWitness<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<unit, int * Rejection<'Id>> =
        let t = Tree.traversal nodew keyw

        let rec go i node =
            function
            | [] -> Ok()
            | o :: rest ->
                match applyWith canHold nodew t (Some keyw.KeyedChildren) idw o node with
                | Ok node' -> go (i + 1) node' rest
                | Error e -> Error(i, e)

        go 0 root ops

    // ---- the index, maintained through an edit (Phase 317) ----
    // `Tree.Index.build` is O(n) and `isFreshFor` reports staleness after every op, so a consumer
    // holding an index across an edit session either rebuilt it per op or read it stale. `afterOp`
    // carries it through the op instead, touching what the op touched. It lives here, not beside
    // `build`, because `SkeletonOp` is declared here and `Tree` precedes this module.

    /// Index maintenance across the skeleton ops (Phase 317).
    [<RequireQualifiedAccess>]
    module Index =

        // What an op (or a batch of them) did to the tree, tracked by id without the intermediate
        // trees: the child-id lists it rewrote, the nodes it grafted, the parent links it moved, the
        // indexed nodes it removed, and every id whose own record or presence may differ.
        type private Track<'Node, 'Id> =
            { Kids: Map<string, 'Id list>
              Grafted: Map<string, 'Node>
              Parent: Map<string, 'Id>
              Gone: Set<string>
              Touched: Set<string> }

        /// The index of `post` — the tree `apply w idw op` returned for the tree `ix` indexes —
        /// carried through `op` rather than rebuilt (Phase 317). Every op kind is handled, a
        /// `Batch` folded through its steps without the intermediate trees: the op names the
        /// containers it rewrote and the subtrees it grafted or removed, and only those, their
        /// ancestors (whose node values every edit rebuilds) and the grafted and removed subtrees
        /// are re-indexed, through `Tree.Index.rebind`.
        ///
        /// **The law:** `afterOp w idw op post (Tree.Index.build w idw pre) ≡ Tree.Index.build w
        /// idw post` — the same `Root`, `ParentOf` and `Fingerprint`, the same `ById` keys, and at
        /// each key a node equal to `post`'s (a node outside the edit keeps the value the index
        /// held, which `apply` rebuilt from the same content). So `isFreshFor post` holds of it.
        ///
        /// **The cost** is the op's, not the tree's: per step, the depth times the fanout along each
        /// rewritten container's path, plus the size of each grafted, removed or rewritten subtree's
        /// root record — and never a walk of the tree. **The guard:** what the tracking predicts is
        /// checked against `post` on the nodes it re-indexes (the root's id, every re-indexed node
        /// found where the tracking puts it, with the children the tracking says it has); a `post`
        /// that disagrees — an op `apply` refused, a tree from elsewhere, an index built under
        /// another witness — falls back to `Tree.Index.build w idw post`, so the law holds whatever
        /// the caller hands it, and only the cost depends on the contract.
        let afterOp
            (w: NodeWitness<'Node, 'Id>)
            (idw: IdWitness<'Id>)
            (op: SkeletonOp<'Node, 'Id>)
            (post: 'Node)
            (ix: Tree.NodeIndex<'Node, 'Id>)
            : Tree.NodeIndex<'Node, 'Id> =
            let key (i: 'Id) = idw.ToString i
            let keyOf (n: 'Node) = key (w.Id n)

            let exists (st: Track<'Node, 'Id>) (k: string) =
                st.Grafted.ContainsKey k || (ix.ById.ContainsKey k && not (st.Gone.Contains k))

            let kidsOf (st: Track<'Node, 'Id>) (k: string) : 'Id list option =
                match Map.tryFind k st.Kids with
                | Some ks -> Some ks
                | None ->
                    match Map.tryFind k st.Grafted with
                    | Some n -> Some(w.Children n |> List.map w.Id)
                    | None ->
                        if st.Gone.Contains k then
                            None
                        else
                            Map.tryFind k ix.ById |> Option.map (fun n -> w.Children n |> List.map w.Id)

            let without (target: 'Id) (ks: 'Id list) =
                ks |> List.filter (fun c -> not (idw.Equals c target))

            let rec step (st: Track<'Node, 'Id>) (op: SkeletonOp<'Node, 'Id>) : Track<'Node, 'Id> option =
                match op with
                | InsertChild(parent, node) ->
                    let pk = key parent

                    match (if exists st pk then kidsOf st pk else None) with
                    | None -> None
                    | Some ks ->
                        let graft = Tree.preorder w node

                        Some
                            { st with
                                Kids = Map.add pk (ks @ [ w.Id node ]) st.Kids
                                Grafted = (st.Grafted, graft) ||> List.fold (fun m n -> Map.add (keyOf n) n m)
                                Parent =
                                    (Map.add (keyOf node) parent st.Parent, graft)
                                    ||> List.fold (fun m n ->
                                        (m, w.Children n) ||> List.fold (fun m c -> Map.add (keyOf c) (w.Id n) m))
                                Touched = (Set.add pk st.Touched, graft) ||> List.fold (fun s n -> Set.add (keyOf n) s) }

                | RemoveNode target ->
                    let tk = key target

                    match Map.tryFind tk st.Parent with
                    | Some parent when exists st tk ->
                        let pk = key parent

                        match kidsOf st pk with
                        | None -> None
                        | Some ks ->
                            // the subtree as the tracking holds it NOW (earlier steps included)
                            let rec collect (acc: string list) (stack: string list) =
                                match stack with
                                | [] -> Some acc
                                | k :: rest ->
                                    match kidsOf st k with
                                    | None -> None
                                    | Some cs -> collect (k :: acc) ((cs |> List.map key) @ rest)

                            match collect [] [ tk ] with
                            | None -> None
                            | Some sub ->
                                Some
                                    { Kids =
                                        (Map.add pk (without target ks) st.Kids, sub)
                                        ||> List.fold (fun m k -> Map.remove k m)
                                      Grafted = (st.Grafted, sub) ||> List.fold (fun m k -> Map.remove k m)
                                      Parent = (st.Parent, sub) ||> List.fold (fun m k -> Map.remove k m)
                                      Gone =
                                        (st.Gone, sub)
                                        ||> List.fold (fun s k -> if ix.ById.ContainsKey k then Set.add k s else s)
                                      Touched = (Set.add pk st.Touched, sub) ||> List.fold (fun s k -> Set.add k s) }
                    | _ -> None

                | MoveNode(target, newParent) ->
                    let tk = key target
                    let nk = key newParent

                    match Map.tryFind tk st.Parent with
                    | Some parent when exists st tk && exists st nk ->
                        let pk = key parent

                        match kidsOf st pk with
                        | None -> None
                        | Some ks ->
                            // remove, then append — `apply`'s order, which matters when the two parents are one
                            let kids1 = Map.add pk (without target ks) st.Kids

                            match kidsOf { st with Kids = kids1 } nk with
                            | None -> None
                            | Some nks ->
                                Some
                                    { st with
                                        Kids = Map.add nk (nks @ [ target ]) kids1
                                        Parent = Map.add tk newParent st.Parent
                                        Touched = st.Touched |> Set.add pk |> Set.add nk }
                    | _ -> None

                | ReorderChildren(parent, order) ->
                    let pk = key parent

                    if exists st pk then
                        Some
                            { st with
                                Kids = Map.add pk order st.Kids
                                Touched = Set.add pk st.Touched }
                    else
                        None

                | UpdateNode node ->
                    let tk = keyOf node

                    if exists st tk then
                        Some
                            { st with
                                Touched = Set.add tk st.Touched }
                    else
                        None

                | Batch ops ->
                    let rec go (s: Track<'Node, 'Id>) =
                        function
                        | [] -> Some s
                        | o :: rest ->
                            match step s o with
                            | Some s' -> go s' rest
                            | None -> None

                    go st ops

            let start =
                { Kids = Map.empty
                  Grafted = Map.empty
                  Parent = ix.ParentOf
                  Gone = Set.empty
                  Touched = Set.empty }

            let incremental (st: Track<'Node, 'Id>) : Tree.NodeIndex<'Node, 'Id> option =
                // every touched id still present, and its ancestors, are re-indexed from `post`
                let affected =
                    (Set.empty, st.Touched)
                    ||> Set.fold (fun acc k ->
                        if not (exists st k) then
                            acc
                        else
                            let rec up (a: Set<string>) (cur: string) =
                                if a.Contains cur then
                                    a
                                else
                                    let a' = Set.add cur a

                                    match Map.tryFind cur st.Parent with
                                    | Some p -> up a' (key p)
                                    | None -> a'

                            up acc k)

                let rootKey = key ix.Root

                if not (idw.Equals (w.Id post) ix.Root) then
                    None
                elif Set.isEmpty affected then
                    Some(Tree.Index.rebind w idw [] [] ix)
                elif not (affected.Contains rootKey) then
                    None
                else
                    // find each affected node in `post` by descending from the root through the
                    // affected set — every affected node's ancestors are affected
                    let rec locate (found: 'Node list) (queue: 'Node list) =
                        match queue with
                        | [] -> found
                        | n :: rest ->
                            let next = w.Children n |> List.filter (fun c -> affected.Contains(keyOf c))
                            locate (n :: found) (next @ rest)

                    let arriving = locate [] [ post ]

                    let agrees (n: 'Node) =
                        match kidsOf st (keyOf n) with
                        | Some ks -> (ks |> List.map key) = (w.Children n |> List.map keyOf)
                        | None -> false

                    if List.length arriving <> Set.count affected || not (List.forall agrees arriving) then
                        None
                    else
                        let leaving =
                            Set.union affected st.Touched
                            |> Set.toList
                            |> List.choose (fun k -> Map.tryFind k ix.ById)

                        Some(Tree.Index.rebind w idw leaving arriving ix)

            match step start op |> Option.bind incremental with
            | Some ix' -> ix'
            | None -> Tree.Index.build w idw post

    /// Derive the inverse of an op from the **pre-state** tree (the tree the op applied to)
    /// — Phase 242. Every skeleton op's inverse is recoverable from the pre-state:
    /// insert↔remove, remove↔insert (capturing the removed subtree + its parent + index),
    /// move↔move-back (prior parent + index), reorder↔reorder (prior order), update↔update (the
    /// pre-state node, whose content the undo restores — Phase 250). `Batch` inverts to its
    /// inverses in reverse order (each derived against the state that op saw). Total: a non-applyable op has no inverse — its `Rejection` is
    /// returned. The defining law: `apply (invert op pre) (apply op pre) = pre`. Undo/redo
    /// becomes a generic capability over the witness, not a per-domain re-implementation.
    let rec invert
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (pre: 'Node)
        : Result<SkeletonOp<'Node, 'Id>, Rejection<'Id>> =

        // The pre-state order of a parent's children, by id. `InsertChild` / `MoveNode`
        // append, so restoring a node to where it was is two steps: put it back, then
        // restate the order it was part of. The order is read from the pre-state, exactly
        // as the index used to be — the same information, named rather than counted.
        let orderIn (parentNode: 'Node) =
            parentNode |> w.Children |> List.map w.Id

        // Restore `target` under `parentNode` at the position it held in `pre`. A single
        // op when it was already last (the append lands it correctly); otherwise the
        // append plus the order it belonged to.
        let restoring (parentNode: 'Node) (target: 'Id) (put: SkeletonOp<'Node, 'Id>) =
            let order = orderIn parentNode

            let wasLast =
                match List.tryLast order with
                | Some lastId -> idw.Equals lastId target
                | None -> false

            if wasLast then
                put
            else
                Batch [ put; ReorderChildren(w.Id parentNode, order) ]

        match op with
        | Batch ops ->
            // Thread `pre` through the forward ops; invert each against the state it saw.
            // Prepending accumulates the inverses in reverse forward order — exactly the
            // order the undo batch must run.
            let rec go acc state =
                function
                | [] -> Ok(Batch acc)
                | o :: rest ->
                    match invert w idw o state with
                    | Error e -> Error e
                    | Ok inv ->
                        match apply w idw o state with
                        | Ok state' -> go (inv :: acc) state' rest
                        | Error e -> Error e

            go [] pre ops

        | _ ->
            // A single op is invertible iff it would apply to `pre`.
            match canApply w idw op pre with
            | Error e -> Error e
            | Ok() ->
                match op with
                | InsertChild(_, node) -> Ok(RemoveNode(w.Id node))
                | RemoveNode target ->
                    let parent = Tree.parentOf w idw target pre |> Option.get
                    let sub = Tree.tryFind w idw target pre |> Option.get
                    Ok(restoring parent target (InsertChild(w.Id parent, sub)))
                | MoveNode(target, _) ->
                    let parent = Tree.parentOf w idw target pre |> Option.get
                    Ok(restoring parent target (MoveNode(target, w.Id parent)))
                | ReorderChildren(parent, _) ->
                    let p = Tree.tryFind w idw parent pre |> Option.get
                    Ok(ReorderChildren(parent, p |> w.Children |> List.map w.Id))
                // The pre-state node's CONTENT restores the content; the inverse carries it as a
                // shell (`ReplaceChildren old []`, Phase 305), because `UpdateNode` never reads its
                // payload's children — the children the tree holds when the undo runs are kept —
                // and an undo stack that stored the whole pre-state subtree per edit held, and
                // never read, a copy of everything below the node.
                | UpdateNode node ->
                    Ok(UpdateNode(w.ReplaceChildren (Tree.tryFind w idw (w.Id node) pre |> Option.get) []))
                | Batch _ -> Ok op // unreachable (handled above) — keeps the match total

    /// The script-level inverse (Phase 305): the inverses of `ops`, each derived against the state
    /// the forward op saw, in REVERSE order — so `applyAll (invertAll w idw ops pre) (applyAll w idw
    /// ops pre) = pre`, the law `invert` states per op lifted to the sequence. It refuses as
    /// `applyAll` does, with the 0-based index of the first op that does not apply (or cannot be
    /// inverted, which is the same op: `invert` refuses exactly what `canApply` refuses) and its
    /// envelope.
    ///
    /// **Why a script and not `invert (Batch ops)`.** That form returns a `Batch`, and a `Batch` is
    /// all-or-nothing INSIDE one operation where a script stops at the first refusal and keeps the
    /// accepted prefix — so an undo stack that recorded the SCRIPT it applied, and inverted it as a
    /// batch, would undo with a different failure shape from the one it did. `invertAll` stores the
    /// shape it applied. The elements are the per-op inverses, so a `Batch` inside `ops` inverts to
    /// one `Batch` as `invert` has always made it.
    let invertAll
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (pre: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, int * Rejection<'Id>> =
        let rec go i acc state =
            function
            | [] -> Ok acc
            | o :: rest ->
                match invert w idw o state with
                | Error e -> Error(i, e)
                | Ok inv ->
                    match apply w idw o state with
                    | Ok state' -> go (i + 1) (inv :: acc) state' rest
                    | Error e -> Error(i, e)

        go 0 [] pre ops

    /// Normalise an op script (Phase 23): a conservative, structural peephole that collapses the
    /// redundancy classes it can prove safe *without the tree*, leaving everything else untouched.
    /// The defining law (certified by `Conformance.normalizeLaws`): for any script applyable to a
    /// tree, `applyAll (normalize ops) = applyAll ops` — normalisation never changes the result.
    /// Collapses, all on adjacent ops so no intervening op observes the discarded state:
    ///   - `InsertChild(p,i,node)` then `RemoveNode(id node)` — insert-then-remove of the same node
    ///     nets to nothing (the insert+remove cancel `p`'s child-shift, so later ops are unaffected);
    ///   - `MoveNode(t,_,_)` then `MoveNode(t,p,i)` — the first relocation is superseded by the
    ///     second (the intermediate parent is restored, since `t` is not in `p`'s subtree in any
    ///     applyable script), so only the net move survives;
    ///   - `ReorderChildren(p,_)` then `ReorderChildren(p,o)` — a reorder sets the full order, so the
    ///     last one on a parent wins;
    ///   - (Phase 305, D65's case) `UpdateNode a` then `UpdateNode a'` of the same id — the last
    ///     rewrite wins, since each keeps the children and replaces the content whole;
    ///     `InsertChild(p, n)` then `UpdateNode n'` of `n`'s id — one insert carrying `n'`'s content
    ///     over `n`'s children (what the pair leaves in the tree); `UpdateNode n` then
    ///     `RemoveNode (id n)` — the remove, since a rewrite of a node about to leave is unobservable;
    ///   - an empty `Batch []` is dropped, and a `Batch` is normalised recursively.
    /// It is **idempotent** (`normalize ∘ normalize = normalize`) and never lengthens a script.
    /// `'Node` needs no equality (it compares ids only). **Caveat:** preservation is guaranteed only
    /// for a script that is *applyable* to the tree — collapsing an insert/remove pair can turn an
    /// `applyAll` that would have *failed* at that pair into one that succeeds, so normalise after
    /// validating, not before.
    ///
    /// **One left fold with an output stack (Phase 305).** Each op is pushed onto the ops already
    /// committed; a push that collapses with the top replaces or drops it and re-examines the new
    /// top, so a collapse that newly adjoins two collapsible ops (a cancelled insert/remove between
    /// two same-target moves) is caught in the same pass. Linear in the script, a loop rather than a
    /// recursion over it — the recursive peephole it replaces, iterated to a fixpoint, overflowed the
    /// stack at about 2,000 flat ops — and the output has no adjacent collapsible pair by
    /// construction, which is what makes a second pass the identity.
    let rec normalize
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        : SkeletonOp<'Node, 'Id> list =
        let eq = idw.Equals

        // What the committed top and the incoming op collapse to, if they collapse: `None` keeps
        // both, `Some []` drops both, `Some [y]` replaces the top with `y`.
        let collapse (top: SkeletonOp<'Node, 'Id>) (x: SkeletonOp<'Node, 'Id>) =
            match top, x with
            | InsertChild(_, node), RemoveNode target when eq (w.Id node) target -> Some []
            | MoveNode(t1, _), MoveNode(t2, _) when eq t1 t2 -> Some [ x ]
            | ReorderChildren(p1, _), ReorderChildren(p2, _) when eq p1 p2 -> Some [ x ]
            | UpdateNode a, UpdateNode a' when eq (w.Id a) (w.Id a') -> Some [ x ]
            | InsertChild(p, n), UpdateNode n' when eq (w.Id n) (w.Id n') ->
                Some [ InsertChild(p, w.ReplaceChildren n' (w.Children n)) ]
            | UpdateNode n, RemoveNode target when eq (w.Id n) target -> Some [ x ]
            | _ -> None

        // Push `x` onto the stack, collapsing against the top for as long as it collapses. The loop
        // is bounded by the stack's depth and pops at every turn, so the whole fold is O(n).
        let push (stack: SkeletonOp<'Node, 'Id> list) (x: SkeletonOp<'Node, 'Id>) =
            let mutable stack = stack
            let mutable pending = Some x

            while Option.isSome pending do
                let x = Option.get pending

                match stack with
                | top :: rest ->
                    match collapse top x with
                    | None ->
                        stack <- x :: stack
                        pending <- None
                    | Some [] ->
                        stack <- rest
                        pending <- None
                    | Some(y :: _) ->
                        stack <- rest
                        pending <- Some y
                | [] ->
                    stack <- [ x ]
                    pending <- None

            stack

        // Batches are normalised inside first and dropped when empty; everything else is pushed.
        (([], ops)
         ||> List.fold (fun stack op ->
             match op with
             | Batch inner ->
                 match normalize w idw inner with
                 | [] -> stack
                 | xs -> push stack (Batch xs)
             | _ -> push stack op))
        |> List.rev

    // ---- footprint + independence (Phase 78) ----
    // The multi-agent coordination invariant, computed structurally from the op-script (never
    // separately declared — the `paramsOf` precedent, Phase 77). `footprint` is a pure, total union-fold
    // over the skeleton ops through the witnesses; `independent` is pairwise footprint disjointness.
    // The structural basis for dispatch-time conflict refusal (a downstream dispatcher's computed
    // leases), lease derivation (Phase 84), and proposal arbitration (Phase 85).
    //
    // Conservativity is the contract (STABILITY.md "Op-script footprint + independence"): where an
    // exact read/write set is a tree fact the script cannot name, over-approximate — report *dependent*.
    // `independent = true` is a promise; `independent = false` is always safe. The pinned
    // over-approximations, enumerated in `Footprint`'s doc-comment and STABILITY.md:
    //   (1) a RemoveNode/MoveNode's SOURCE parent (and any ancestor relationship) is unknown from the
    //       script, so it lands in `UnknownParentWrites` and conflicts with every structural write in a
    //       concurrent script — disjoint-subtree independence is NOT proven when either side removes or
    //       moves (that needs the tree);
    //   (2) a RemoveNode's `ContentWrites` records only the target id, not its (tree-unknown) subtree.
    //       Sound for the skeleton ops because every one of them — `UpdateNode` included, which is why
    //       its footprint carries an unknown-parent write (Phase 250) — is a structural write, so (1)
    //       already serialises a remove/move against any concurrent op; a domain that layers its OWN
    //       in-place content op on top, with no unknown-parent write, must fold the removed subtree in
    //       itself (it has the tree).
    //
    // Phase 143 asked whether (1) could now be TIGHTENED, with Phase 138's preservation theorem in
    // hand: a relocation ought to commute with a structural write under an unrelated parent. It
    // cannot be, over THIS record, and that is now a theorem rather than a suspicion
    // (`proofs/TreeOps.fst` section 18, `relocation_clause_is_necessary`). The witness is one
    // well-formed tree and three ops: a `MoveNode` and a structural write under a parent inside the
    // moved subtree DO commute (`relocation_disjoint_diamond`); the same shape with a `RemoveNode`
    // in it does NOT, because the insert's parent is destroyed with the subtree
    // (`relocation_diamond_fails_for_a_remove`); and the two ops carry the SAME address sets — the
    // four node sets then, and the six since Phase 340, whose two slot sets are empty for every
    // skeleton op (`relocation_footprints_coincide`). A predicate over footprints alone gives one verdict to
    // both, so freeing the safe pair frees the fatal one. That is (2) being load-bearing for (1)
    // and (1) being load-bearing for (2), each proved rather than asserted.
    //
    // So the last two clauses of `independent` below are NECESSARY, not a placeholder. Tightening
    // them needs a footprint that can NAME the difference — a fifth address kind carrying the
    // relocation's kind, or a destroyed-subtree set the pure script cannot compute — which is a
    // change to the record, not to a clause. `relocation_move_pair_also_fails` adds the second
    // reason: two moves nesting into each other's subtrees reject each other with
    // `WouldNestUnderSelf`, and no record could free that pair at all.

    let private emptyFootprint = Footprint.empty

    let private unionFootprint (a: Footprint) (b: Footprint) : Footprint = Footprint.union a b

    // The one fold behind `footprint` and `footprintKeyed` (Phase 247). `carried` reads the ids an
    // inserted subtree carries; `introduced` the ids an `UpdateNode` payload brings in beyond its own
    // target. The unkeyed form passes `Tree.ids w` and nothing, which is exactly the fold `footprint`
    // was before the keyed form existed.
    let private footprintOver
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (carried: 'Node -> 'Id list)
        (introduced: 'Node -> 'Id list)
        (ops: SkeletonOp<'Node, 'Id> list)
        : Footprint =
        let key (i: 'Id) = idw.ToString i

        let subtreeKeys (node: 'Node) =
            carried node |> List.map key |> Set.ofList

        let rec ofOp (op: SkeletonOp<'Node, 'Id>) : Footprint =
            match op with
            | InsertChild(parent, node) ->
                let inserted = subtreeKeys node
                // parent existence + the inserted ids' dup-check are reads; the parent's child-list is a
                // (known) structure-write; the inserted subtree is authored into being — a content-write.
                //
                // Phase 137: the dup-check named here is now the one `validateInsert` actually performs
                // — `firstDuplicateId` reads exactly this `Tree.ids w node` set against the whole tree
                // (the keyed walk of the graft under `footprintKeyed`, as the keyed engine reads it),
                // so `Reads` describes a read that happens rather than one the footprint assumed. The
                // set is unchanged: the validator's other half (is the subtree unique WITHIN ITSELF?) is
                // internal to the op and reads no tree state, so it adds nothing to the footprint and
                // creates no new collision between concurrent scripts.
                { emptyFootprint with
                    Reads = Set.add (key parent) inserted
                    StructureWrites = Set.singleton (key parent)
                    ContentWrites = inserted }
            | RemoveNode target ->
                // the node is destroyed (content-write on the target) and its unknown source parent's
                // child-list is rewritten (the pinned over-approximation).
                { emptyFootprint with
                    Reads = Set.singleton (key target)
                    ContentWrites = Set.singleton (key target)
                    UnknownParentWrites = Set.singleton (key target) }
            | MoveNode(target, newParent) ->
                // relocation: the destination child-list is a known structure-write; the target is
                // content-written (it moves); the source parent's child-list is the unknown over-approx.
                { emptyFootprint with
                    Reads = Set.ofList [ key target; key newParent ]
                    StructureWrites = Set.singleton (key newParent)
                    ContentWrites = Set.singleton (key target)
                    UnknownParentWrites = Set.singleton (key target) }
            | ReorderChildren(parent, order) ->
                // the parent's child-list order is rewritten (known structure-write); the named children
                // are read (their positions are permuted, their content is not touched).
                let named = order |> List.map key |> Set.ofList

                { emptyFootprint with
                    Reads = Set.add (key parent) named
                    StructureWrites = Set.singleton (key parent) }
            | Batch inner -> List.fold (fun acc o -> unionFootprint acc (ofOp o)) emptyFootprint inner
            | UpdateNode node ->
                // Phase 250 — an in-place rewrite. The node is read (it must exist) and its content
                // is written. It is ALSO an unknown-parent write, and that is required rather than
                // cautious: the node is rewritten under a parent — and a chain of ancestors — the
                // script cannot name, so an update of `x` and a concurrent `RemoveNode` of an
                // ancestor of `x` carry disjoint reads and content-writes yet do not commute (the
                // update lands in one order and is refused in the other). Only the unknown-parent
                // clause of `independent` can see that pair, exactly as it sees a remove against a
                // write inside the removed subtree. The cost is the same pinned over-approximation
                // the remove/move pay: an update is independent only of a structure-free script,
                // so two updates of different nodes are reported dependent.
                let target = key (w.Id node)
                // Phase 247 — under the keyed engine the payload's keyed subtrees are new content
                // (`validateUpdate` checks their ids for duplicates), so they are read and written as
                // an insert's subtree is. Empty for the unkeyed form.
                let incoming = introduced node |> List.map key |> Set.ofList

                // Phase 340 — and it is a WHOLE-node write, never a slot write: the payload is the
                // node entire, and a pure script cannot say which part of it changed. A domain that
                // rewrites a field by name lowers its own op with `Footprint.slotEdit`.
                { emptyFootprint with
                    Reads = Set.add target incoming
                    ContentWrites = Set.add target incoming
                    UnknownParentWrites = Set.singleton target }

        List.fold (fun acc op -> unionFootprint acc (ofOp op)) emptyFootprint ops

    /// The read/write footprint of an op-script (Phase 78) — a pure, total derivation over the skeleton
    /// five through the node/id witnesses (the `NodeWitness` reads the ids out of an inserted `'Node`
    /// subtree; the `IdWitness` keys every address by its string form). `Batch` folds its inner ops.
    /// Total: no tree, no failure case — it never throws (GP4) and mints no ids. Over-approximating by
    /// design — see `Footprint` and STABILITY.md for the pinned conservative cases.
    ///
    /// **The unkeyed form (Phase 247).** It reads an inserted subtree's ids over `Children` alone, so
    /// an id a domain holds in a keyed position inside the graft is not in the footprint: two scripts
    /// that each insert a subtree carrying the same keyed id are declared independent and collide only
    /// at replay. A domain with keyed positions calls `footprintKeyed` with its `KeyedWitness`; for a
    /// domain with none the two answer identically.
    let footprint (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (ops: SkeletonOp<'Node, 'Id> list) : Footprint =
        footprintOver w idw (Tree.ids w) (fun _ -> []) ops

    /// `footprint` over the keyed walk (Phase 247) — the footprint of the script
    /// `Ops.applyContainedKeyed` runs. The id set an `InsertChild` authors is the graft's keyed walk,
    /// `Tree.idsKeyed nodew keyw node`, so an id held in a keyed position anywhere inside the inserted
    /// subtree is read and content-written as a structural one is; and an `UpdateNode` payload's keyed
    /// subtrees, which the keyed engine checks as new content, are read and content-written too. Two
    /// scripts that each bring in the same keyed id therefore fail `independent` with
    /// `Interference.SameTarget` on it — the collision `footprint` cannot see.
    ///
    /// Every other address is `footprint`'s: the keyed engine edits through `Children` alone, so the
    /// structural writes are unchanged, and an op that locates a node below a keyed position names it by
    /// the same id key. For a domain whose `KeyedChildren` is `fun _ -> []` this returns exactly what
    /// `footprint` returns. Total, no throws (GP4); it mints no ids.
    let footprintKeyed
        (keyw: KeyedWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        : Footprint =
        footprintOver
            nodew
            idw
            (Tree.idsKeyed nodew keyw)
            (fun n -> keyw.KeyedChildren n |> List.collect (Tree.idsKeyed nodew keyw))
            ops

    /// Every clause of `independent` two footprints fail, with the addresses each fails on (Phase
    /// 248) — the explanation of a `false` verdict, so a refused party can see WHAT it collides on
    /// (the parent it shares, the id the other script reads, the relocation that serialises it)
    /// without re-deriving the clauses itself. Empty exactly when `independent a b`. The clauses are
    /// listed in `Interference`'s declaration order, each at most once; `a` is the left side and `b`
    /// the right, so `interference b a` reports the same clauses with the directional cases mirrored.
    /// Total, no throws (GP4).
    let interference (a: Footprint) (b: Footprint) : Interference list =
        let structural (f: Footprint) =
            Set.union f.StructureWrites f.UnknownParentWrites

        let sameTarget = Set.intersect a.ContentWrites b.ContentWrites
        let leftWrites = Set.intersect a.ContentWrites b.Reads
        let rightWrites = Set.intersect a.Reads b.ContentWrites
        let sameParent = Set.intersect a.StructureWrites b.StructureWrites
        let structuralA = structural a
        let structuralB = structural b
        // Phase 340 — the slot clauses. A slot access is compared at the slot against another slot
        // access, and as an access of the NODE against the other side's whole-node sets. The three
        // sets are `Footprint`'s helpers, which `Dag.conflicts` reads too.
        let slotClash = Footprint.slotClash a b
        let leftSlots = Footprint.slotsAgainstNode a b
        let rightSlots = Footprint.slotsAgainstNode b a

        [ if not (Set.isEmpty sameTarget) then
              Interference.SameTarget sameTarget
          if not (Set.isEmpty leftWrites) then
              Interference.LeftWritesRightReads leftWrites
          if not (Set.isEmpty rightWrites) then
              Interference.RightWritesLeftReads rightWrites
          if not (Set.isEmpty sameParent) then
              Interference.SameParent sameParent
          if not (Set.isEmpty a.UnknownParentWrites) && not (Set.isEmpty structuralB) then
              Interference.LeftUnknownParent(a.UnknownParentWrites, structuralB)
          if not (Set.isEmpty b.UnknownParentWrites) && not (Set.isEmpty structuralA) then
              Interference.RightUnknownParent(structuralA, b.UnknownParentWrites)
          if not (Set.isEmpty slotClash) then
              Interference.SlotClash slotClash
          if not (Set.isEmpty leftSlots) then
              Interference.LeftSlotsRightNode leftSlots
          if not (Set.isEmpty rightSlots) then
              Interference.RightSlotsLeftNode rightSlots ]

    /// Are two footprints **independent** (Phase 78) — do their scripts provably commute under `apply`?
    /// Pairwise disjointness across the write kinds, with the conservative rules pinned:
    ///   - no content write/write overlap, and no content-write vs read overlap either way (a node one
    ///     script authors/destroys must not be read or written by the other);
    ///   - no shared **named** structural parent — two positional inserts (or an insert + a reorder, …)
    ///     under one parent are NOT independent (they shift the same siblings — THE pinned same-parent
    ///     rule);
    ///   - an `UnknownParentWrites` op (a remove/move, whose source parent is a tree fact) conflicts with
    ///     *any* structural write — known or unknown — in the other script (the pinned unknown-parent
    ///     over-approximation): a remove/move is only independent of a structure-free script;
    ///   - (Phase 340) no slot is written by one script and accessed by the other (`SlotClash`), and no
    ///     node one script accesses through a slot is touched whole by the other (`LeftSlotsRightNode`
    ///     / `RightSlotsLeftNode`): two writes to DIFFERENT slots of one node are independent; a
    ///     whole-node write is a write of every slot and serialises against each.
    /// `true` is a promise (they commute); `false` is always a safe answer. Total, no throws (GP4).
    ///
    /// **The unknown-parent clauses are NECESSARY over this record, not merely conservative (Phase
    /// 143).** A move and a batch that removes and reorders carry the SAME address sets (the slot
    /// sets Phase 340 added are empty for every skeleton op, so they tell them apart no better), and one of them
    /// commutes with a structural write under a parent inside the relocated subtree while the other
    /// destroys that parent — so no predicate over footprints alone can free the first without
    /// freeing the second. Proved, with the witness, in `proofs/TreeOps.fst` section 18
    /// (`relocation_clause_is_necessary`); pinned in the test tree by the `Proofs.Oracle` case
    /// "the pinned unknown-parent clause is necessary" and by the `Conformance.concurrencyLaws`
    /// teeth-check that erases `UnknownParentWrites`. Tightening it is a change to the `Footprint`
    /// record and to every consumer that reads it, not a change to this function.
    ///
    /// Since Phase 248 it is DEFINED as `interference a b = []`, so the verdict and its explanation
    /// have one source and cannot drift; the clauses above are `interference`'s cases.
    let independent (a: Footprint) (b: Footprint) : bool = List.isEmpty (interference a b)

    // ---- lowering a tree to a skeleton root plus an insert script (Phase 312) ----
    // A tree that arrives whole — decoded, generated, streamed — reaches the op stream as ops: a
    // skeleton root to start from, and the inserts that rebuild everything below it. The UI host
    // shipped this as its streaming lowering, generic over the witness already; it lives here now.

    /// `node` with its STRUCTURAL children emptied — the shape an insert script grows back. A node
    /// that holds no children is returned as it is, so `ReplaceChildren` is never asked to rebuild
    /// a leaf (a witness may leave it partial on nodes that cannot hold children). Only
    /// `Children` is emptied: anything a domain holds in keyed positions travels WITH the shell,
    /// exactly as an `UpdateNode` payload's keyed positions do.
    let shellOf (w: NodeWitness<'Node, 'Id>) (node: 'Node) : 'Node =
        if List.isEmpty (w.Children node) then
            node
        else
            w.ReplaceChildren node []

    /// The genesis tree `lower`'s script is applied to: the root's shell.
    let skeletonRoot (w: NodeWitness<'Node, 'Id>) (root: 'Node) : 'Node = shellOf w root

    /// Lower `root` to the insert script that rebuilds it from `skeletonRoot w root`: in preorder,
    /// one `InsertChild(parent, shellOf child)` per node below the root, each child inserted as its
    /// own shell and then filled. `InsertChild` appends, and preorder inserts a parent's children
    /// left to right, so sibling order is rebuilt with no `ReorderChildren`. Iterative — a deep tree
    /// cannot overflow.
    ///
    /// **The law** (`Conformance.loweringLaws`): for every `Tree.wellFormed` tree,
    /// `applyAll w idw (lower w t) (skeletonRoot w t) = Ok t`. **The contained form is the same
    /// script under `applyAllWith canHold`**: every node that holds children is an insert's parent,
    /// so for a tree in the containment invariant (every node with children satisfies `canHold`)
    /// the script is accepted and rebuilds the tree; for a tree outside it, the refusal is
    /// `NotAContainer` naming the first offender in preorder — the shell is accepted childless and
    /// its first child is refused. A well-formed tree's script never meets `DuplicateId`; an
    /// ill-formed one's meets it at the second occurrence.
    let lower (w: NodeWitness<'Node, 'Id>) (root: 'Node) : SkeletonOp<'Node, 'Id> list =
        let rec go (acc: SkeletonOp<'Node, 'Id> list) (stack: ('Id * 'Node) list) =
            match stack with
            | [] -> List.rev acc
            | (parent, node) :: rest ->
                let below = w.Children node |> List.map (fun c -> w.Id node, c)
                go (InsertChild(parent, shellOf w node) :: acc) (below @ rest)

        go [] (w.Children root |> List.map (fun c -> w.Id root, c))

    // ---- the containment grammar and the reference witness (Phase 313) ----
    // `canHold` is unary and child-blind by design (Phase 161): it answers whether a node can hold
    // children AT ALL. A grammar answers which: for a parent's kind tag, the kind tags it may hold
    // (`None` = any). The grammar is DATA the domain declares, never a kind Core knows (DECISIONS,
    // Phase 313), and every surface that reads it — the engine below, `Diff.toOpsGrammar`,
    // `Arbitration.arbitrateGrammar`, `Validator.containment`, `Conformance.containmentLaws` — reads
    // it through `isLegalChild`, so they share ONE definition of a legal child.
    //
    // The grammar and reference forms WRAP the container-aware engine rather than widening it: each
    // non-batch step is first decided exactly as `applyContained` decides it, and the new clauses run
    // only on a step it accepted, against the tree before and after it. So no operation the engine
    // refuses changes class (the D38 ordering, at the level of a whole engine), and a `Batch` stays
    // all-or-nothing because it is threaded step by step through the same wrapper.

    /// Is a node of kind `childKind` a legal child of a node of kind `parentKind` under
    /// `allowedChildren` (Phase 313)? `None` for the parent's kind admits every child; `Some legal`
    /// admits exactly the kinds `legal` lists (ordinal comparison of the kind tags). The one
    /// definition every grammar-reading surface uses.
    let isLegalChild (allowedChildren: string -> string list option) (parentKind: string) (childKind: string) : bool =
        match allowedChildren parentKind with
        | None -> true
        | Some legal -> List.contains childKind legal

    /// Every parent→child pair of `node`'s subtree whose child the grammar does not let its parent
    /// hold (Phase 313), parents in preorder and each parent's children in order. Empty exactly when
    /// the subtree keeps the grammar. Walks `Children` — the structural surface the engine edits.
    let illegalChildren
        (allowedChildren: string -> string list option)
        (w: NodeWitness<'Node, 'Id>)
        (node: 'Node)
        : ('Node * 'Node) list =
        Tree.preorder w node
        |> List.collect (fun p ->
            let pk = w.KindTag p

            w.Children p
            |> List.filter (fun c -> not (isLegalChild allowedChildren pk (w.KindTag c)))
            |> List.map (fun c -> p, c))

    /// The `IllegalChild` envelope for `child` under `parent`, enumerating what the grammar lets the
    /// parent's kind hold.
    let private illegalChild
        (allowedChildren: string -> string list option)
        (w: NodeWitness<'Node, 'Id>)
        (parent: 'Node)
        (child: 'Node)
        : Rejection<'Id> =
        let pk = w.KindTag parent
        IllegalChild(w.Id child, w.KindTag child, w.Id parent, pk, allowedChildren pk |> Option.defaultValue [])

    /// The grammar clause of one accepted non-batch step: the parent→child pairs the step CREATES,
    /// checked in a fixed order. An insert creates its node under the parent, then every pair inside
    /// the graft (preorder); a move creates the moved node under its new parent — the pairs inside
    /// the moved subtree already stood in the tree, and refusing the move for them would be an
    /// invariant-repair gate rather than a check of the move (D38's reasoning for `NotAContainer`);
    /// an in-place rewrite can change the node's kind, so it creates the node under its parent and
    /// each of its kept children under it. Removes and reorders create none.
    let private grammarRefusal
        (allowedChildren: string -> string list option)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (before: 'Node)
        (after: 'Node)
        : Rejection<'Id> option =
        let legal (parent: 'Node) (child: 'Node) =
            isLegalChild allowedChildren (w.KindTag parent) (w.KindTag child)

        match op with
        | InsertChild(parent, node) ->
            match Tree.tryFind w idw parent before with
            | Some p when not (legal p node) -> Some(illegalChild allowedChildren w p node)
            | _ ->
                illegalChildren allowedChildren w node
                |> List.tryHead
                |> Option.map (fun (p, c) -> illegalChild allowedChildren w p c)
        | MoveNode(target, newParent) ->
            match Tree.tryFind w idw newParent before, Tree.tryFind w idw target before with
            | Some np, Some t when not (legal np t) -> Some(illegalChild allowedChildren w np t)
            | _ -> None
        | UpdateNode node ->
            let target = w.Id node

            match Tree.tryFind w idw target after with
            | None -> None
            | Some rewritten ->
                match Tree.parentOf w idw target after with
                | Some p when not (legal p rewritten) -> Some(illegalChild allowedChildren w p rewritten)
                | _ ->
                    w.Children rewritten
                    |> List.tryFind (fun c -> not (legal rewritten c))
                    |> Option.map (illegalChild allowedChildren w rewritten)
        | RemoveNode _
        | ReorderChildren _
        | Batch _ -> None

    /// The reference clause of one accepted non-batch step: the ids the tree declared before the step
    /// and no longer declares after it are ORPHANED, and a node of the resulting tree that refers to
    /// one is a referrer — `StillReferenced`, naming the step's target (a remove's target, a
    /// rewrite's node) and every referrer in preorder. Only a `RemoveNode` (its subtree's
    /// declarations leave) and an `UpdateNode` (the rewritten node may declare less) can orphan an id;
    /// an insert, a move or a reorder keeps every declaration. A reference that already dangled before
    /// the step is not the step's doing and is not reported.
    let private referenceRefusal
        (refw: RefWitness<'Node, 'Id>)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (before: 'Node)
        (after: 'Node)
        : Rejection<'Id> option =
        let key (i: 'Id) = idw.ToString i

        let declared (t: 'Node) =
            Tree.preorder w t |> List.collect refw.DeclsOf |> List.map key |> Set.ofList

        let refuse (target: 'Id) =
            let orphaned = Set.difference (declared before) (declared after)

            if Set.isEmpty orphaned then
                None
            else
                match
                    Tree.preorder w after
                    |> List.filter (fun n -> refw.RefsOf n |> List.exists (fun r -> orphaned.Contains(key r)))
                with
                | [] -> None
                | referrers -> Some(StillReferenced(target, referrers |> List.map w.Id))

        match op with
        | RemoveNode target -> refuse target
        | UpdateNode node -> refuse (w.Id node)
        | InsertChild _
        | MoveNode _
        | ReorderChildren _
        | Batch _ -> None

    /// The wrapped engine: `applyContained` decides each non-batch step, and `check` — handed the
    /// step, the tree before it and the tree after it — runs only on a step it accepted. A `Batch` is
    /// threaded through this same function, all-or-nothing, exactly as the engine threads one.
    let rec private applyChecked
        (check: SkeletonOp<'Node, 'Id> -> 'Node -> 'Node -> Rejection<'Id> option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        match op with
        | Batch ops ->
            let rec go node =
                function
                | [] -> Ok node
                | o :: rest ->
                    match applyChecked check canHold w idw o node with
                    | Ok node' -> go node' rest
                    | Error e -> Error e

            go root ops
        | _ ->
            match applyContained canHold w idw op root with
            | Error e -> Error e
            | Ok after ->
                match check op root after with
                | Some r -> Error r
                | None -> Ok after

    /// The sequence form of `applyChecked`: first refusal wins, `applyAllWith`'s triple.
    let private applyAllChecked
        (check: SkeletonOp<'Node, 'Id> -> 'Node -> 'Node -> Rejection<'Id> option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<'Node, int * Rejection<'Id> * 'Node> =
        let rec go i node =
            function
            | [] -> Ok node
            | o :: rest ->
                match applyChecked check canHold w idw o node with
                | Ok node' -> go (i + 1) node' rest
                | Error e -> Error(i, e, node)

        go 0 root ops

    let private grammarCheck allowedChildren (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) =
        fun op before after -> grammarRefusal allowedChildren w idw op before after

    let private referenceCheck
        (refw: RefWitness<'Node, 'Id>)
        allowedChildren
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        =
        fun op before after ->
            match grammarRefusal allowedChildren w idw op before after with
            | Some r -> Some r
            | None -> referenceRefusal refw w idw op before after

    /// Grammar-aware apply (Phase 313) — `applyContained` with the domain's containment grammar
    /// beside `canHold`. Every step the container-aware engine refuses is refused with the same
    /// envelope; a step it accepts is then refused with `IllegalChild` when it would leave a child
    /// under a parent whose kind `allowedChildren` does not let hold it (see the section head for
    /// which pairs each op creates). For a tree that keeps the grammar, every tree this returns keeps
    /// it — `Conformance.containmentLaws`, and `grammar_preserves` in `proofs/Preservation.fst`.
    /// With `allowedChildren = fun _ -> None` it answers exactly what `applyContained` answers.
    let applyGrammar
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        applyChecked (grammarCheck allowedChildren w idw) canHold w idw op root

    /// The dry run of `applyGrammar`: its exact envelope, and no tree returned. The grammar clause
    /// reads the step's result, so unlike `canApplyContained` this builds the edited tree.
    let canApplyGrammar
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        applyGrammar allowedChildren canHold w idw op root |> Result.map ignore

    /// The sequence form of `applyGrammar` — `applyAllWith`'s contract (first refusal wins; the
    /// failing index, the envelope and the tree the accepted prefix reached) under the grammar.
    let applyAllGrammar
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<'Node, int * Rejection<'Id> * 'Node> =
        applyAllChecked (grammarCheck allowedChildren w idw) canHold w idw ops root

    /// The dry run of `applyAllGrammar`, in `canApplyAllWith`'s shape — the `canApply` a domain with
    /// a grammar hands `Arbitration.arbitrateWith` (`Arbitration.arbitrateGrammar` is that
    /// composition).
    let canApplyAllGrammar
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<unit, int * Rejection<'Id>> =
        match applyAllGrammar allowedChildren canHold w idw ops root with
        | Ok _ -> Ok()
        | Error(i, e, _) -> Error(i, e)

    /// Reference-aware apply (Phase 313) — `applyGrammar` under a `RefWitness` as well: a
    /// `RemoveNode` or `UpdateNode` it accepts is then refused with `StillReferenced` when the tree
    /// after it no longer declares an id it declared before and a node of that tree still refers to
    /// it — a removed subtree's declarations, or what a rewrite stops declaring. So an accepted op
    /// never leaves dangling a reference that resolved before it. A reference an INSERT or an
    /// in-place rewrite brings in is not refused here: whether it resolves is
    /// `Validator.referenceIntegrity`'s report, because a document under construction legitimately
    /// refers ahead of what it has declared. Pass `fun _ -> None` for a domain with no grammar.
    let applyReferenced
        (refw: RefWitness<'Node, 'Id>)
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        applyChecked (referenceCheck refw allowedChildren w idw) canHold w idw op root

    /// The dry run of `applyReferenced`.
    let canApplyReferenced
        (refw: RefWitness<'Node, 'Id>)
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        applyReferenced refw allowedChildren canHold w idw op root |> Result.map ignore

    /// The sequence form of `applyReferenced`, `applyAllWith`'s contract.
    let applyAllReferenced
        (refw: RefWitness<'Node, 'Id>)
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<'Node, int * Rejection<'Id> * 'Node> =
        applyAllChecked (referenceCheck refw allowedChildren w idw) canHold w idw ops root

    /// The dry run of `applyAllReferenced`, in `canApplyAllWith`'s shape.
    let canApplyAllReferenced
        (refw: RefWitness<'Node, 'Id>)
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<unit, int * Rejection<'Id>> =
        match applyAllReferenced refw allowedChildren canHold w idw ops root with
        | Ok _ -> Ok()
        | Error(i, e, _) -> Error(i, e)

    /// `footprint` under a `RefWitness` (Phase 313): every id the script writes a reference to is
    /// READ — the references every node of an inserted subtree carries, and those of an `UpdateNode`
    /// payload — so a script that writes a reference to `x` fails `independent` against a script that
    /// destroys `x`, by `Interference.RightWritesLeftReads` / `LeftWritesRightReads` naming `x`.
    /// Every other address is `footprint`'s, so this only ever ADDS collisions: it is as sound as
    /// `footprint` is, and `independent` over it is never more permissive.
    ///
    /// Among the skeleton ops alone the remove-versus-reference race was already serialised, by the
    /// pinned unknown-parent clause (a remove collides with every structural write, an update is one).
    /// What this adds is the collision NAMED for the reference, and the same read for a domain op that
    /// writes a reference and no structure (`Footprint.reading`), where no other clause catches it.
    /// The read meets a removal's content-write when the declared id IS the declaring node's id; a
    /// domain declaring names that are not node ids folds the names a removal destroys into its own
    /// removal footprint.
    let footprintReferenced
        (refw: RefWitness<'Node, 'Id>)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        : Footprint =
        let rec written (op: SkeletonOp<'Node, 'Id>) : 'Id list =
            match op with
            | InsertChild(_, node) -> Tree.preorder w node |> List.collect refw.RefsOf
            | UpdateNode node -> refw.RefsOf node
            | Batch inner -> inner |> List.collect written
            | RemoveNode _
            | MoveNode _
            | ReorderChildren _ -> []

        Footprint.union (footprint w idw ops) (Footprint.reading (ops |> List.collect written |> List.map idw.ToString))

/// Where a node is to sit among its destination's children (Phase 312) — stated by naming a
/// sibling or an end, or by an index. Placement is OVER the existing ops: `InsertChild` and
/// `MoveNode` append and `ReorderChildren` states order by id (Phase 95), and `TreePlacement`
/// lowers an anchor to those ops. Positions count the destination's children OTHER than the node
/// being placed, so `Index 0` is first and `Index n` (with `n` such siblings) is last, for an insert
/// and a move alike — the index a document domain's index-bearing ops have always meant.
[<RequireQualifiedAccess>]
type Anchor<'Id> =
    /// Before every sibling.
    | First
    /// After every sibling — what `InsertChild` / `MoveNode` do on their own.
    | Last
    /// Immediately before the named sibling.
    | Before of sibling: 'Id
    /// Immediately after the named sibling.
    | After of sibling: 'Id
    /// At this position among the siblings, `0` (first) to their count (last) inclusive.
    | Index of int

/// Why `TreePlacement` could not lower a placement (Phase 312). Every case names the failure and
/// enumerates what was valid, as `Rejection` does.
[<RequireQualifiedAccess>]
type PlaceError<'Id> =
    /// The op the placement lowers to would be refused by the engine — an unknown parent or node,
    /// a duplicate id, a move of the root or under itself, a parent `canHold` refuses. The envelope
    /// is the engine's own, exactly as `canApply` would return it.
    | Refused of Rejection<'Id>
    /// The anchor is not among the destination's children other than the node being placed;
    /// `siblings` are the ids that are, in order.
    | UnknownAnchor of parent: 'Id * anchor: 'Id * siblings: 'Id list
    /// The index is outside `0 .. count`, where `count` is the number of the destination's children
    /// other than the node being placed.
    | IndexOutOfRange of parent: 'Id * index: int * count: int

/// The placement algebra (Phase 312): a placed insert, move and clone, each LOWERED to a skeleton
/// script — never a new op. Every consumer that wanted a node anywhere but last derived the full
/// sibling permutation itself; this derives it once, over the witnesses, so it serves every domain.
///
/// **Named for what it is.** `Fuaran.Core.Placement` is already a public type (where a capability's
/// body runs), so this module is `TreePlacement` rather than a second `Placement` whose resolution
/// would depend on which packages a consumer opens (`DECISIONS.md`, Phase 312).
///
/// **The scripts.** A placement is validated by the engine's own dry run first (`PlaceError.Refused`
/// carries its envelope unchanged), then its anchor is resolved, then:
///   - `place`: `[InsertChild]` when the node lands last, else
///     `[Batch [InsertChild; ReorderChildren]]` — one insert plus one reorder, atomic;
///   - `move` to another parent: `[MoveNode]` when it lands last, else
///     `[Batch [MoveNode; ReorderChildren]]`; within its own parent: `[ReorderChildren]`, or `[]`
///     when it is already there — a reorder footprints the parent alone, where a move would also
///     write the node and an unknown parent;
///   - `clone`: the source subtree with every id renamed by `FreshIds.repairDuplicates` against the
///     tree's ids, then `place`d.
/// A reorder leg that would restate the order appending already gives is dropped, so the common
/// case stays one bare op. **The law** (`Conformance.placementLaws`): `applyAll` of the script puts
/// the node exactly at the anchor's position and moves nothing else.
///
/// The `…Contained` forms validate through `canApplyContained canHold`, for a domain that executes
/// through `applyContained` / `applyAllWith canHold`; the plain forms are those with every node able
/// to hold children.
[<RequireQualifiedAccess>]
module TreePlacement =

    /// The position `anchor` names among `others` (the destination's children other than the node
    /// being placed), or the refusal naming why it names none.
    let private positionOf
        (idw: IdWitness<'Id>)
        (parent: 'Id)
        (anchor: Anchor<'Id>)
        (others: 'Id list)
        : Result<int, PlaceError<'Id>> =
        let count = List.length others

        let find a =
            others |> List.tryFindIndex (fun c -> idw.Equals c a)

        match anchor with
        | Anchor.First -> Ok 0
        | Anchor.Last -> Ok count
        | Anchor.Before a ->
            match find a with
            | Some i -> Ok i
            | None -> Error(PlaceError.UnknownAnchor(parent, a, others))
        | Anchor.After a ->
            match find a with
            | Some i -> Ok(i + 1)
            | None -> Error(PlaceError.UnknownAnchor(parent, a, others))
        | Anchor.Index i ->
            if i < 0 || i > count then
                Error(PlaceError.IndexOutOfRange(parent, i, count))
            else
                Ok i

    /// The ids of `parent`'s children, once the engine's dry run has said `parent` exists.
    let private childIds (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (parent: 'Id) (root: 'Node) : 'Id list =
        match Tree.tryFind w idw parent root with
        | Some p -> w.Children p |> List.map w.Id
        | None -> []

    /// `place` under a container capability: the dry run is `canApplyContained canHold`.
    let placeContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (parent: 'Id)
        (anchor: Anchor<'Id>)
        (node: 'Node)
        (root: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, PlaceError<'Id>> =
        let insert = InsertChild(parent, node)

        match Ops.canApplyContained canHold w idw insert root with
        | Error r -> Error(PlaceError.Refused r)
        | Ok() ->
            let others = childIds w idw parent root

            positionOf idw parent anchor others
            |> Result.map (fun k ->
                if k = List.length others then
                    [ insert ]
                else
                    [ Batch [ insert; ReorderChildren(parent, List.insertAt k (w.Id node) others) ] ])

    /// Insert `node` under `parent` at `anchor`, as a script `Ops.applyAll` accepts (see the module
    /// note for its shape). Refused, by name, when the engine would refuse the insert, when the
    /// anchor names no other child of `parent`, or when the index is out of range.
    let place
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (parent: 'Id)
        (anchor: Anchor<'Id>)
        (node: 'Node)
        (root: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, PlaceError<'Id>> =
        placeContained (fun _ -> true) w idw parent anchor node root

    /// `move` under a container capability: the dry run is `canApplyContained canHold`.
    let moveContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (newParent: 'Id)
        (anchor: Anchor<'Id>)
        (root: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, PlaceError<'Id>> =
        let move = MoveNode(target, newParent)

        match Ops.canApplyContained canHold w idw move root with
        | Error r -> Error(PlaceError.Refused r)
        | Ok() ->
            let siblings = childIds w idw newParent root
            let isTarget c = idw.Equals c target
            let alreadyChild = List.exists isTarget siblings
            let others = siblings |> List.filter (isTarget >> not)

            positionOf idw newParent anchor others
            |> Result.map (fun k ->
                let wanted = List.insertAt k target others

                if alreadyChild then
                    if List.forall2 idw.Equals wanted siblings then
                        []
                    else
                        [ ReorderChildren(newParent, wanted) ]
                elif k = List.length others then
                    [ move ]
                else
                    [ Batch [ move; ReorderChildren(newParent, wanted) ] ])

    /// Move `target` under `newParent` at `anchor` — to another parent, or to another position among
    /// its own siblings — as a script `Ops.applyAll` accepts (see the module note for its shape).
    let move
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (newParent: 'Id)
        (anchor: Anchor<'Id>)
        (root: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, PlaceError<'Id>> =
        moveContained (fun _ -> true) w idw target newParent anchor root

    /// `clone` under a container capability.
    let cloneContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (setId: 'Id -> 'Node -> 'Node)
        (mint: 'Id -> Set<string> -> 'Id)
        (source: 'Id)
        (parent: 'Id)
        (anchor: Anchor<'Id>)
        (root: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, PlaceError<'Id>> =
        match Tree.subtree w idw source root with
        | None -> Error(PlaceError.Refused(UnknownNode(source, Tree.ids w root)))
        | Some sub ->
            let taken = Tree.ids w root |> List.map idw.ToString |> Set.ofList
            let copy, _ = FreshIds.repairDuplicates w idw setId mint taken sub
            placeContained canHold w idw parent anchor copy root

    /// Copy the subtree at `source` and place the copy under `parent` at `anchor`. Every id of the
    /// copy is renamed through `mint` (`FreshIds.derived idw`, `FreshIds.sequential idw prefix`, or a
    /// domain's own strategy) against the tree's ids, by `FreshIds.repairDuplicates`; the script is
    /// `place`'s, so the copy's ids are the ones its `InsertChild` carries. A paste from ANOTHER tree
    /// is the same two steps a caller composes: `repairDuplicates` against this tree's keys, then
    /// `place`.
    let clone
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (setId: 'Id -> 'Node -> 'Node)
        (mint: 'Id -> Set<string> -> 'Id)
        (source: 'Id)
        (parent: 'Id)
        (anchor: Anchor<'Id>)
        (root: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, PlaceError<'Id>> =
        cloneContained (fun _ -> true) w idw setId mint source parent anchor root

/// Structural tree-diff → op script (Phase 245): the inverse direction of `Ops.apply`.
/// Given two trees over a shared id space, derive a `SkeletonOp` list that transforms one
/// into the other — so a desired tree lowers to the apply/op-stream path every domain has.
module Diff =

    /// Why a diff could not be produced — a typed result, never an unapplyable script.
    type DiffError<'Id> =
        /// Skeleton ops cannot change the root, so two trees with different root ids are
        /// not a shared id space.
        | RootIdMismatch of before: 'Id * after: 'Id
        /// A well-formed tree carries each id at most once; this id appears twice.
        | DuplicateIdInTree of 'Id
        /// (Container-aware diff, Phase 09) the `after` tree places children under a node that
        /// `canHold` rejects — a container-legal script is impossible, so the diff is refused
        /// rather than emitting an `InsertChild`/`MoveNode` under a leaf (the F1 hazard).
        ///
        /// `target` is the first such node of `after` in preorder, as `Ops.firstUncontained` — the
        /// single definition of the shape — finds it, and `kindTag` is that node's own. The payload
        /// is its apply-side sibling's, `Rejection.NotAContainer`: grafting the same subtree through
        /// `Ops.applyContained` refuses with `NotAContainer` naming the same `target` and `kindTag`,
        /// which `Conformance.diffContainedLaws` asserts. (Phase 228 renamed the field from `parent`,
        /// the operator's ruling (B) of 2026-09-20; positional construction and matching are
        /// unaffected.)
        | TargetNotAContainer of target: 'Id * kindTag: string
        /// (Grammar-aware diff, Phase 313) the `after` tree places `child` (of kind `childKind`)
        /// under `parent` (of kind `parentKind`) where the domain's containment grammar does not let
        /// that kind hold it, so no grammar-legal script reaches it. The pair is the first
        /// `Ops.illegalChildren` reports over `after`, and the payload is `Rejection.IllegalChild`'s,
        /// `legal` enumerating what the grammar lets `parentKind` hold. Declared last.
        | IllegalChildInTree of child: 'Id * childKind: string * parent: 'Id * parentKind: string * legal: string list

    /// How one id changed between `before` and `after` (Phase 314) — the per-id reading of the
    /// content-aware diff, which `Diff.changes` derives from the two trees and `Conformance.changeLaws`
    /// holds to the script `toOpsWith` emits (DECISIONS.md D112: a classification is a PROJECTION of
    /// the diff, never a second diff). One id may carry more than one entry — a survivor that moved
    /// AND whose content changed carries `Moved` and `Changed` — because the two facts are
    /// independent and the script carries both ops; a reader that wants one kind per id takes the
    /// first in declaration order, which is what the consumers' single-kind classifiers reported.
    /// `RequireQualifiedAccess`: `ChangeKind.Added`, so the cases shadow nothing a consumer owns.
    [<RequireQualifiedAccess>]
    type ChangeKind<'Id> =
        /// The id is in `after` and not in `before` — the child an `InsertChild` of the script
        /// grafts.
        | Added
        /// The id is in `before` and not in `after` — a `RemoveNode` target, or a node below one
        /// (the script removes a region at its top; every id in it is `Removed` here).
        | Removed
        /// A survivor whose parent differs between the two trees — the script's `MoveNode`. A change
        /// of position under ONE parent is not a move of the child; it is a `Reordered` parent.
        | Moved of fromParent: 'Id * toParent: 'Id
        /// A survivor whose kind tag differs. Reported INSTEAD of `Changed`, which it subsumes: the
        /// script carries one `UpdateNode` for the node, whichever this reads as.
        | KindChanged of fromKind: string * toKind: string
        /// A survivor of unchanged kind whose own content differs under the caller's encoder over
        /// the two shells — exactly the test `toOpsWith` emits an `UpdateNode` on.
        | Changed
        /// A survivor whose KEPT children — the children both trees place under it — stand in a
        /// different relative order in `after`; a child arriving or leaving alone is not a reorder.
        /// Every `ReorderChildren` the script emits names a parent that is `Reordered`, or one that
        /// gained an `Added` or `Moved` child (the structural passes append, so a child placed before
        /// a kept one is restated by a reorder the trees do not otherwise show). Declared last.
        | Reordered

    /// One entry of `Diff.changes`: the id and how it changed.
    type Change<'Id> =
        {
            /// The id the entry is about.
            Id: 'Id
            /// How it changed.
            Kind: ChangeKind<'Id>
        }

    // ---- the one emitter behind every entry (Phase 305) ----
    // The four structural passes are Phase 245's, and are what `proofs/TreeDiff.fst` models
    // clause for clause; Phase 305 changed one clause of step 4 (the settled-order drop, below).
    // Phase 305 also added the two CONTENT blocks around them — `UpdateNode`
    // for every survivor whose own content differs between the trees, which only a caller's
    // `encode` can see (the witness has no content accessor) — and moved the two maps and the
    // step-4 lookup onto `Tree.Index` (the `parentMap` here was `Tree.Index.build`'s `ParentOf`
    // written a second time, and step 4 walked `after` once per reordered parent).
    //
    // THE PLACEMENT RULE (DECISIONS D103). A content-changing survivor whose NEW node `canHold`
    // accepts is updated FIRST, before any insert or move; every other update goes LAST, after the
    // reorders. First, because a survivor that becomes a container is the parent of the inserts
    // and moves under it, and `validateInsert` / `validateMove` read the kind the tree holds at
    // that step — a leaf that is about to become a section refuses its own new children
    // (`NotAContainer(p, para)`, the shape `TreeDiff.fst` section 12 pins) unless the rewrite
    // lands before them. Last, because a survivor that becomes a LEAF may be rewritten only once
    // its children have left (`validateUpdate` refuses a childful leaf), and they leave in the
    // moves and the removals. An `UpdateNode` keeps the children the tree holds, so it is inert to
    // the four structural blocks wherever it sits; the two sites are where the containment check
    // is satisfied. Measured over 5,444 independent pairs with drawn kinds and a drawn `canHold`:
    // 0 refused under `applyAllWith`, 0 round-trip mismatches, where the structural script had 41%
    // refused and appending every update last still had 41% (`ProofOracleTests`, the content-aware
    // bridge).
    let private emitWith
        (changed: ('Node -> 'Node -> bool) option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =

        let key (i: 'Id) = idw.ToString i

        // First duplicated id in a tree (by key), if any — Core's named structural predicate
        // (Phase 139), read through `Tree.Index.tryBuild` since Phase 305 so the index the passes
        // read is built in the same pass that refuses a malformed tree. This was a `groupBy` of its
        // own until Phase 139, and the retirement is the point: a diff refusing a malformed tree
        // and an insert refusing a malformed graft are the same notion of malformed.
        //
        // ONE OBSERVABLE CHANGE at Phase 139, and it is which id is NAMED, never whether the tree is
        // refused. The `groupBy` form reported the first id whose GROUP had more than one member,
        // in first-appearance order of the keys; `Tree.wellFormed` reports the first id at its
        // SECOND occurrence in preorder. For `[a; b; b; a]` the old form said `a` and the new says
        // `b`. The new answer is the one `Rejection.DuplicateId` already gave on the accept path, so
        // the two paths name the same offender for the same tree. Recorded in STABILITY.md.
        let index (root: 'Node) =
            match Tree.Index.tryBuild w idw root with
            | Ok ix -> Ok ix
            | Error(Tree.RepeatedId d) -> Error(DuplicateIdInTree d)
            | Error Tree.Structural -> Ok(Tree.Index.build w idw root) // unreachable: `tryBuild` refuses only a repeat

        let childKeysOf (n: 'Node) =
            w.Children n |> List.map (fun c -> key (w.Id c))

        if key (w.Id before) <> key (w.Id after) then
            Error(RootIdMismatch(w.Id before, w.Id after))
        else
            match index before with
            | Error e -> Error e
            | Ok bix ->
                match index after with
                | Error e -> Error e
                | Ok aix ->
                    let beforeNodes = Tree.preorder w before
                    let afterNodes = Tree.preorder w after
                    let bIds = bix.ById |> Map.keys |> Set.ofSeq
                    let aIds = aix.ById |> Map.keys |> Set.ofSeq
                    // key -> parent id, for every non-root node — the index's own map.
                    let aParent = aix.ParentOf
                    let bParent = bix.ParentOf

                    let bChildKeys =
                        beforeNodes |> List.map (fun n -> key (w.Id n), childKeysOf n) |> Map.ofList

                    let ops = ResizeArray<SkeletonOp<'Node, 'Id>>()
                    // Parents whose order must be restated once membership is final (step 4).
                    let reorderParents = ResizeArray<'Id>()
                    // Content-changing survivors whose new node cannot hold children (step 5).
                    let trailingUpdates = ResizeArray<SkeletonOp<'Node, 'Id>>()

                    // 0. Content, first and last (Phase 305). A survivor is a node both trees
                    //    carry; `changed` is the caller's encoder over the two SHELLS, so a
                    //    difference in the children alone is never an update. The payload is the
                    //    `after` node (its children are not read by `UpdateNode`, so carrying them
                    //    costs nothing and keeps the op a faithful statement of `after`).
                    match changed with
                    | None -> ()
                    | Some differs ->
                        for n in afterNodes do
                            match Map.tryFind (key (w.Id n)) bix.ById with
                            | Some b when differs b n ->
                                if canHold n then
                                    ops.Add(UpdateNode n)
                                else
                                    trailingUpdates.Add(UpdateNode n)
                            | _ -> ()

                    // 1. Added nodes → leaf shells under their after-parent (top-down via
                    //    preorder, so an added parent exists before an added child). They
                    //    append; step 2 states the order.
                    for n in afterNodes do
                        let k = key (w.Id n)

                        if not (bIds.Contains k) then
                            match Map.tryFind k aParent with
                            | Some pid -> ops.Add(InsertChild(pid, w.ReplaceChildren n []))
                            | None -> () // an added root is impossible (roots match)

                    // 2. Reattach + reorder every survivor to its after-position. A parent
                    //    whose child-id list is unchanged is already correct (its children
                    //    are never disturbed), so skip it.
                    //
                    //    This used to be a positional sweep — one `MoveNode` per child at
                    //    its index, relying on the front prefix building correctly whilst
                    //    stale children trailed. With membership and order separated it is
                    //    a `MoveNode` only for children that actually CHANGED parent, plus
                    //    one `ReorderChildren` naming the after-order. That is strictly
                    //    fewer ops (a pure reorder of n children is now 1 op, not n) and
                    //    carries no ordinals.
                    for p in afterNodes do
                        let pk = key (w.Id p)
                        let aKidKeys = childKeysOf p

                        let unchanged =
                            bIds.Contains pk
                            && (match Map.tryFind pk bChildKeys with
                                | Some bk -> bk = aKidKeys
                                | None -> false)

                        if not unchanged then
                            // Membership: a survivor whose before-parent differs must move.
                            // A newly-added node was already appended here in step 1.
                            for c in w.Children p do
                                let ck = key (w.Id c)

                                let movedParent =
                                    bIds.Contains ck
                                    && (match Map.tryFind ck bParent with
                                        | Some bp -> key bp <> pk
                                        | None -> true) // no before-parent → it was the root's child set

                                if movedParent then
                                    ops.Add(MoveNode(w.Id c, w.Id p))

                            // Order is stated LAST, in step 4 — not here. `ReorderChildren`
                            // demands an exact permutation of the parent's children AT APPLY
                            // TIME, and at this point the parent may still hold children that
                            // are about to leave: a node moving to a parent processed later in
                            // this loop, or a node removed in step 3. The old positional sweep
                            // tolerated that ("the front prefix builds correctly even with
                            // stale children trailing"); naming an order does not, so every
                            // reorder waits until membership is final.
                            reorderParents.Add(w.Id p)

                    // 3. Removals last — a removed region's surviving descendants have been
                    //    moved out in step 2, so removing the region's top node (the removed
                    //    node whose before-parent survives) drops only removed nodes.
                    for n in beforeNodes do
                        let k = key (w.Id n)

                        if not (aIds.Contains k) then
                            match Map.tryFind k bParent with
                            | Some pid when aIds.Contains(key pid) -> ops.Add(RemoveNode(w.Id n))
                            | _ -> () // before-parent also removed → covered by removing it

                    // 4. Order, last of the structure — every parent now holds exactly its
                    //    after-children, so naming the after-order is a legal permutation. One
                    //    op per changed parent, where the old sweep emitted one MoveNode per
                    //    child. The parent is read off the index (Phase 305; a preorder walk of
                    //    `after` per parent before).
                    //
                    //    THE DROP (Phase 305, 305.t1). The order steps 1-3 LEAVE a parent in is
                    //    a function of the two trees alone: its kept survivors in before-order (a
                    //    before-child that is still its child), then the inserted shells, then
                    //    the moved-in survivors, the last two each in after-order — an insert and
                    //    a move both append, step 1 walks `after`'s preorder (a parent's new
                    //    children arrive in its child order), step 2 walks a parent's children in
                    //    order, and a move-out or a removal deletes in place. A parent whose
                    //    after-order IS that order needs no reorder, and the reorder that used to
                    //    trail every append restated an order the tree already held. `settled`
                    //    is `TreeDiff.fst`'s `settled_order` clause for clause; the order-
                    //    prediction lemma behind the drop is section 10's `ord1`/`ord2`/`ord3`,
                    //    and `reorder_settled` is the step that skips. "Moved in" is tested
                    //    against the parent's before-children rather than step 2's `bParent`:
                    //    the two agree on a well-formed `before` (a before-child of `p` has
                    //    before-parent `p`), and this form reads the map step 2 already built.
                    for pid in reorderParents do
                        match Tree.Index.tryFind idw pid aix with
                        | Some p when List.length (w.Children p) > 1 ->
                            let aKidKeys = childKeysOf p
                            let bKidKeys = defaultArg (Map.tryFind (key pid) bChildKeys) []
                            let aKidSet = Set.ofList aKidKeys
                            let bKidSet = Set.ofList bKidKeys

                            let settled =
                                (bKidKeys |> List.filter (fun c -> Set.contains c aKidSet))
                                @ (aKidKeys |> List.filter (fun c -> not (bIds.Contains c)))
                                @ (aKidKeys
                                   |> List.filter (fun c -> bIds.Contains c && not (Set.contains c bKidSet)))

                            if settled <> aKidKeys then
                                ops.Add(ReorderChildren(pid, w.Children p |> List.map w.Id))
                        | _ -> ()

                    // 5. The updates `canHold` refuses, last of all: by now each such node holds
                    //    exactly its `after` children, which `firstUncontained` has already shown
                    //    to be none (a childful node the predicate refuses is refused up front by
                    //    `toOpsContained`, and the plain forms pass a predicate that refuses
                    //    nothing, so this block is empty there).
                    ops.AddRange trailingUpdates

                    Ok(List.ofSeq ops)

    /// Derive a script such that `Ops.applyAll (toOps w idw before after) before`
    /// reproduces `after` structurally. Relocated subtrees diff to `MoveNode` (never
    /// remove+insert), so an unchanged subtree is preserved, not destroyed and rebuilt.
    /// The emitted order is always applyable: added nodes go in as leaf shells (top-down),
    /// every survivor is then reattached/reordered to its `after` position, and removed
    /// regions are deleted **last** (so a surviving child is pulled out before its old
    /// container is removed). Structural only — a survivor keeps `before`'s content, kind
    /// included; `toOpsWith` is the form that diffs content too. The two roots must share an id.
    let toOps
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        emitWith None (fun _ -> true) w idw before after

    /// Container-aware diff (Phase 09) — the `canHold`-aware mirror of `toOps`, the diff-path
    /// analogue of `Ops.applyContained`. Every parent the emitted script addresses comes from
    /// the `after` tree, so an `after` that nests children under a node `canHold` rejects makes
    /// a container-legal script impossible: that is surfaced as a typed `TargetNotAContainer`
    /// rather than an `InsertChild`/`MoveNode` under a leaf. When every `after`-parent is a
    /// container the result is exactly `toOps` (so `toOps` is the `(fun _ -> true)` wrapper —
    /// the same relationship `apply`/`applyContained` have). Containment *legality* (which kind
    /// may parent which) stays domain-side; `canHold` answers only "can this node hold children
    /// at all".
    ///
    /// **What the check buys, exactly (Phase 305).** The script's addresses resolve, in `after`,
    /// to nodes `canHold` accepts (`proofs/TreeDiff.fst`, `diff_applicable_contained`). That is NOT
    /// yet "`applyAllWith canHold` accepts every step": the script runs against `before`'s kinds,
    /// and a structural diff carries no content, so a survivor that is a leaf in `before` and a
    /// container in `after` refuses the inserts under it with `NotAContainer` — over independent
    /// pairs with kinds drawn freely, 41% of the scripts this form returns are refused. The
    /// content-aware `toOpsContainedWith` is the form that applies under the predicate it checked.
    let toOpsContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        // Every (new) parent the script targets is an `after` node that has children. If any
        // such node cannot hold children, no container-legal script exists.
        match Ops.firstUncontained canHold w after with
        | Some p -> Error(TargetNotAContainer(w.Id p, w.KindTag p))
        | None -> emitWith None canHold w idw before after

    /// Content-aware, container-aware diff (Phase 305) — `toOpsContained` that also emits an
    /// `UpdateNode` for every survivor whose own content differs, as the caller's `encode` sees it
    /// over the two nodes' SHELLS (`ReplaceChildren n []`, so a change in the children alone is
    /// never an update). The witness has no content accessor, so the encoder is the one way the
    /// diff can see content — the same per-call parameter `Tree.encodeHash` and `Tree.Index.buildWith`
    /// take, and like theirs it should be injective over a node's own content: a lossy encoder
    /// makes two different nodes read as unchanged and the script lands on a tree that is not
    /// `after`.
    ///
    /// **The guarantee this form adds.** For well-formed `before` and `after` and a child-blind
    /// `canHold`, a script it returns is accepted at every step by `Ops.applyAllWith canHold` and
    /// lands on `after`, content included — the placement rule in `emitWith`'s header (D103) is what
    /// makes it so, and `ProofOracleTests`' content-aware bridge measures it over drawn kinds and a
    /// drawn predicate. The refusals are `toOpsContained`'s, unchanged. With
    /// `canHold = fun _ -> true` it is exactly `toOpsWith`.
    let toOpsContainedWith
        (canHold: 'Node -> bool)
        (encode: 'Node -> string)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        let shell (n: 'Node) = encode (w.ReplaceChildren n [])
        let differs (b: 'Node) (a: 'Node) = shell b <> shell a

        match Ops.firstUncontained canHold w after with
        | Some p -> Error(TargetNotAContainer(w.Id p, w.KindTag p))
        | None -> emitWith (Some differs) canHold w idw before after

    /// Content-aware diff (Phase 305) — `toOps` that also emits an `UpdateNode` for every survivor
    /// whose own content differs under the caller's `encode`; `toOpsContainedWith (fun _ -> true)`,
    /// so every update sits before the structural blocks. `Ops.applyAll (toOpsWith encode w idw
    /// before after) before` reproduces `after` structurally AND in every node's content the
    /// encoder distinguishes.
    let toOpsWith
        (encode: 'Node -> string)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        toOpsContainedWith (fun _ -> true) encode w idw before after

    /// Grammar-aware diff (Phase 313) — `toOpsContained` with the domain's containment grammar
    /// beside `canHold`. Every refusal `toOpsContained` makes is made first and unchanged; then an
    /// `after` that holds a child its parent's kind may not hold is refused with
    /// `IllegalChildInTree`, naming the first such pair (`Ops.illegalChildren`). Otherwise the
    /// script is `toOpsContained`'s, and every parent→child pair it creates is one the final tree
    /// holds — a shell is inserted under its `after` parent and never moved, a survivor is moved
    /// once, to its `after` parent — so for a `before` that keeps the grammar, `Ops.applyAllGrammar`
    /// accepts the script wherever the tree it builds keeps it (`Conformance.containmentLaws`).
    /// Structural only, as `toOps` is: a survivor keeps `before`'s content, kind included;
    /// `toOpsGrammarWith` is the content-aware form.
    let toOpsGrammar
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        match toOpsContained canHold w idw before after with
        | Error e -> Error e
        | Ok ops ->
            match Ops.illegalChildren allowedChildren w after with
            | (p, c) :: _ ->
                let pk = w.KindTag p

                Error(IllegalChildInTree(w.Id c, w.KindTag c, w.Id p, pk, allowedChildren pk |> Option.defaultValue []))
            | [] -> Ok ops

    /// `toOpsGrammar` over the content-aware `toOpsContainedWith` (Phase 305): the same two
    /// refusals in the same order, and a script that carries the survivors' content changes.
    let toOpsGrammarWith
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (encode: 'Node -> string)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        match toOpsContainedWith canHold encode w idw before after with
        | Error e -> Error e
        | Ok ops ->
            match Ops.illegalChildren allowedChildren w after with
            | (p, c) :: _ ->
                let pk = w.KindTag p

                Error(IllegalChildInTree(w.Id c, w.KindTag c, w.Id p, pk, allowedChildren pk |> Option.defaultValue []))
            | [] -> Ok ops

    /// The rank of a change kind in the canonical order: declaration order, so an id's entries read
    /// added, removed, moved, kind, content, reordered.
    let private rankOf (k: ChangeKind<'Id>) : int =
        match k with
        | ChangeKind.Added -> 0
        | ChangeKind.Removed -> 1
        | ChangeKind.Moved _ -> 2
        | ChangeKind.KindChanged _ -> 3
        | ChangeKind.Changed -> 4
        | ChangeKind.Reordered -> 5

    /// The per-id change classification between two trees (Phase 314): every id of either tree that
    /// changed, in canonical order — ascending id key, then `ChangeKind` declaration order — each
    /// entry one of `Added | Removed | Moved | KindChanged | Changed | Reordered` as the cases
    /// document. Read off the two trees' indexes directly, not off a script, and held to the script
    /// by `Conformance.changeLaws`: over `Ok ops = toOpsWith encode w idw before after`, the `Added`
    /// ids are exactly the `InsertChild` grafts' ids, the `Moved` ids exactly the `MoveNode` targets,
    /// the `KindChanged` and `Changed` ids together exactly the `UpdateNode` targets (when `encode`
    /// sees the kind, as an injective encoder does), the `RemoveNode` targets are `Removed` and every
    /// other `Removed` id sits below one in `before`, and every `ReorderChildren` parent is
    /// `Reordered` or holds an `Added` or `Moved` child. The encoder is read over each survivor's
    /// SHELL, exactly as `toOpsWith` reads it. The refusals are `toOps`'s and in its order: a root id
    /// mismatch, then a repeated id in `before`, then one in `after`. An identity pair classifies to
    /// `[]`.
    let changes
        (encode: 'Node -> string)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<Change<'Id> list, DiffError<'Id>> =
        let key (i: 'Id) = idw.ToString i
        let shell (n: 'Node) = encode (w.ReplaceChildren n [])

        let index (root: 'Node) =
            match Tree.Index.tryBuild w idw root with
            | Ok ix -> Ok ix
            | Error(Tree.RepeatedId d) -> Error(DuplicateIdInTree d)
            | Error Tree.Structural -> Ok(Tree.Index.build w idw root)

        let childKeysOf (n: 'Node) =
            w.Children n |> List.map (fun c -> key (w.Id c))

        if key (w.Id before) <> key (w.Id after) then
            Error(RootIdMismatch(w.Id before, w.Id after))
        else
            match index before with
            | Error e -> Error e
            | Ok bix ->
                match index after with
                | Error e -> Error e
                | Ok aix ->
                    let found = ResizeArray<Change<'Id>>()

                    for n in Tree.preorder w after do
                        let k = key (w.Id n)

                        match Map.tryFind k bix.ById with
                        | None -> found.Add { Id = w.Id n; Kind = ChangeKind.Added }
                        | Some b ->
                            match Map.tryFind k aix.ParentOf, Map.tryFind k bix.ParentOf with
                            | Some ap, Some bp when key ap <> key bp ->
                                found.Add
                                    { Id = w.Id n
                                      Kind = ChangeKind.Moved(bp, ap) }
                            | _ -> ()

                            let bKind = w.KindTag b
                            let aKind = w.KindTag n

                            if bKind <> aKind then
                                found.Add
                                    { Id = w.Id n
                                      Kind = ChangeKind.KindChanged(bKind, aKind) }
                            elif shell b <> shell n then
                                found.Add
                                    { Id = w.Id n
                                      Kind = ChangeKind.Changed }

                            let bKids = childKeysOf b
                            let aKids = childKeysOf n
                            let aSet = Set.ofList aKids
                            let bSet = Set.ofList bKids

                            if
                                (bKids |> List.filter (fun c -> Set.contains c aSet))
                                <> (aKids |> List.filter (fun c -> Set.contains c bSet))
                            then
                                found.Add
                                    { Id = w.Id n
                                      Kind = ChangeKind.Reordered }

                    for n in Tree.preorder w before do
                        if not (Map.containsKey (key (w.Id n)) aix.ById) then
                            found.Add
                                { Id = w.Id n
                                  Kind = ChangeKind.Removed }

                    found |> List.ofSeq |> List.sortBy (fun c -> key c.Id, rankOf c.Kind) |> Ok
