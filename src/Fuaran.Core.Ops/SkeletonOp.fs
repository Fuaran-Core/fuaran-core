namespace Fuaran.Core

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
/// `Ops.footprint` under a `Policy` carrying it reads every id a script writes a reference to; and the
/// engine under that policy refuses a `RemoveNode` that would leave a reference dangling.
type RefWitness<'Node, 'Id> =
    {
        /// The ids this node refers to, in the domain's order; each resolves when some node's
        /// `DeclsOf` carries it.
        RefsOf: 'Node -> 'Id list
        /// The ids this node offers as reference targets — its own id where every node is referable,
        /// the names it defines where names are not node ids.
        DeclsOf: 'Node -> 'Id list
    }

/// A script's refusal (Phase 391): the first op of an op-script the engine refused, with the tree
/// the accepted prefix reached. A script is not a `Batch` — it stops at the first refusal and KEEPS
/// the prefix — so the refusal names how far it got. `Ops.applyAll` answers it on `Error`,
/// under every policy and scope.
type ScriptRejection<'Node, 'Id> =
    {
        /// How many ops were applied before the refusal — so also the 0-based position of the
        /// refused op in the script.
        Applied: int
        /// Why that op was refused.
        Rejection: Rejection<'Id>
        /// The tree the accepted prefix reached: the state the refused op was offered against.
        Tree: 'Node
    }

/// How the engine sees a domain's tree (Phase 517): the structural witness it walks and rebuilds
/// through, the id witness, which nodes can hold children, and the keyed positions when the domain
/// declares them. The engine's entries take one where they used to take a name per combination
/// (`Ops.apply` under `OpScope.contained`, `Ops.apply` under `OpScope.keyed`, `Ops.canApplyAll` under `OpScope.keyed`, ...): a contained or a keyed write
/// is the same entry under another scope, so no check the plain path makes can be bypassed by
/// choosing a spelling. Build one with `OpScope.plain`, `OpScope.contained` or `OpScope.keyed`.
///
/// It is the domain's DECLARATION; the walks the engine derives from it (the keyed traversal) are
/// computed by the engine, so a scope cannot name a locate walk that disagrees with its keyed
/// positions. Named `OpScope` because `Fuaran.Core.Scope<'Id>` (a projection's scope) already
/// shares the namespace, and two types of one name are ambiguous to a caller opening both.
[<NoEquality; NoComparison>]
type OpScope<'Node, 'Id> =
    {
        /// The structural witness: what the engine edits through (`Children` / `ReplaceChildren`),
        /// and what it walks when no keyed position is declared.
        Nodes: NodeWitness<'Node, 'Id>
        /// The id witness every address is keyed by.
        Ids: IdWitness<'Id>
        /// Which nodes can hold children at all (fuaran#251). `None` admits every node, which is
        /// what the plain scope means. Asked of every parent an op writes: an insert's parent, a
        /// move's destination, a reorder's parent (Phase 517), an update's rewritten node, and every
        /// interior node of an inserted graft (Phase 161, D38).
        CanHold: ('Node -> bool) option
        /// The keyed positions (Phase 286). `Some` makes the engine locate through
        /// `Tree.traversal Nodes keyw` and see the keyed ids in its duplicate check; it still edits
        /// through `Nodes` alone, so a keyed position is never added to, vacated or reordered.
        Keyed: KeyedWitness<'Node, 'Id> option
    }

/// Building an `OpScope` (Phase 517).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module OpScope =

    /// Every node can hold children; no keyed position.
    let plain (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) : OpScope<'Node, 'Id> =
        { Nodes = w
          Ids = idw
          CanHold = None
          Keyed = None }

    /// Only the nodes `canHold` accepts can hold children (fuaran#251); no keyed position.
    let contained (canHold: 'Node -> bool) (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) : OpScope<'Node, 'Id> =
        { plain w idw with
            CanHold = Some canHold }

    /// The keyed scope (Phase 286): `contained` with the domain's keyed positions declared.
    let keyed
        (keyw: KeyedWitness<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        : OpScope<'Node, 'Id> =
        { contained canHold w idw with
            Keyed = Some keyw }

    /// The walk the engine locates nodes through: `Tree.traversal` over the keyed positions when
    /// they are declared, the structural witness otherwise.
    let locate (s: OpScope<'Node, 'Id>) : NodeWitness<'Node, 'Id> =
        match s.Keyed with
        | Some keyw -> Tree.traversal s.Nodes keyw
        | None -> s.Nodes

    /// The ids of `node`'s subtree in the walk the scope locates through, preorder.
    let ids (s: OpScope<'Node, 'Id>) (node: 'Node) : 'Id list = Tree.ids (locate s) node

/// Which checks the engine runs beyond its own (Phase 517): the domain's containment grammar
/// (Phase 313) and its references (Phase 313). The refusal order inside one step is the engine's,
/// whichever fields are set: the engine's checks (D38's order), then the grammar (`IllegalChild`),
/// then the references (`StillReferenced`). `Policy.none` is the engine alone.
[<NoEquality; NoComparison>]
type Policy<'Node, 'Id> =
    {
        /// What a parent of each kind may hold (`None` from the function = unconstrained). A step
        /// that would leave a child under a parent whose kind may not hold it is `IllegalChild`.
        Grammar: (string -> string list option) option
        /// The domain's references. A step that would take away a declaration a remaining node still
        /// refers to is `StillReferenced`; the footprint reads every id a script writes a reference to.
        Refs: RefWitness<'Node, 'Id> option
    }

/// Building a `Policy` (Phase 517).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Policy =

    /// The engine's own checks and nothing more.
    let none<'Node, 'Id> : Policy<'Node, 'Id> = { Grammar = None; Refs = None }

    /// The engine's checks, then the containment grammar.
    let grammar (allowedChildren: string -> string list option) : Policy<'Node, 'Id> =
        { Grammar = Some allowedChildren
          Refs = None }

    /// The engine's checks, then the references (no grammar).
    let referenced (refw: RefWitness<'Node, 'Id>) : Policy<'Node, 'Id> = { Grammar = None; Refs = Some refw }

/// The walks the engine derives from an `OpScope` for one entry (Phase 512; named `OpScope` until
/// Phase 517 made that the public declaration): the witness it EDITS through, the one it LOCATES
/// through, the keyed positions, and the id witness. Internal.
[<NoEquality; NoComparison>]
type internal Walks<'Node, 'Id> =
    { Edit: NodeWitness<'Node, 'Id>
      Locate: NodeWitness<'Node, 'Id>
      Keyed: ('Node -> 'Node list) option
      Ids: IdWitness<'Id> }

/// What one non-batch op finds in one tree (Phase 512), located ONCE: the engine checks and edits
/// from it, and the base-aware footprint, the write gate, the grammar and the inverse read it rather
/// than walk the tree again. A field is `None` where the tree does not hold what it names. What an op
/// NAMES, tree-blind, is the pure footprint (`Ops.footprint`, pinned by the `ops/` vectors).
[<NoEquality; NoComparison>]
type internal OpView<'Node, 'Id> =
    {
        Op: SkeletonOp<'Node, 'Id>
        /// An insert's or a reorder's parent; a remove's, a move's or an update's target (subtree).
        Located: 'Node option
        /// A move's new parent.
        Destination: 'Node option
        /// The node whose STRUCTURAL child list holds a remove's or a move's target.
        SourceParent: 'Node option
        /// The keyed position holding a remove's or a move's target when it has no structural
        /// parent: the holder and the target's index in its `KeyedChildren` (keyed scope only).
        KeyedPosition: ('Node * int) option
    }

/// The one script runner's account of a run (Phase 512): the steps taken, what each produced, the
/// tree reached, and the refusal that stopped it.
[<NoEquality; NoComparison>]
type internal ScriptRun<'Node, 'Out, 'E> =
    { Applied: int
      Outs: 'Out list
      Tree: 'Node
      Refused: 'E option }

/// How the engine finds, in ONE tree, what an op names (Phase 659): by walking the tree, or by
/// reading an index that describes it. Every lookup answers what the walk answers over an id-unique
/// tree, so the engine's checks, edits and refusals are the same whichever form it is handed. A
/// function takes the tree when the engine asks it about a tree an earlier half of the same step
/// produced (a move's graft after its detach); the paths it follows do not move under that edit.
[<NoEquality; NoComparison>]
type internal Lookups<'Node, 'Id> =
    {
        /// The node carrying an id (`Tree.tryFind` over the locate walk).
        Find: 'Id -> 'Node option
        /// The node whose STRUCTURAL child list holds an id.
        StructuralParent: 'Id -> 'Node option
        /// The keyed holder of an id with no structural parent, and its slot (keyed scope only).
        KeyedHolder: 'Id -> ('Node * int) option
        /// The first id of a graft that the tree carries or the graft repeats (`Tree.graftWellFormed`).
        GraftRepeat: 'Node -> 'Id option
        /// An `UpdateNode` payload's keyed ids against the tree its target keeps (keyed scope only).
        KeptRepeat: 'Id -> 'Id list -> 'Id option
        /// Is the id the subtree's root or below it?
        Encloses: 'Node -> 'Id -> bool
        /// Rewrite the node carrying an id, rebuilding only its path (`Tree.updateNode`).
        At: 'Id -> ('Node -> 'Node) -> 'Node -> 'Node option
    }
