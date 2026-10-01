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
    | InsertChild of parent: 'Id * node: 'Node
    | RemoveNode of target: 'Id
    | MoveNode of target: 'Id * newParent: 'Id
    | ReorderChildren of parent: 'Id * order: 'Id list
    | Batch of SkeletonOp<'Node, 'Id> list
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
///
/// **Conservativity is the contract:** `footprint` over-approximates — it records more collisions than a
/// tree-aware analysis would. `Ops.independent = true` is therefore a *promise* (the scripts provably
/// commute); `independent = false` is always a safe answer, never a defect report.
type Footprint =
    { Reads: Set<string>
      StructureWrites: Set<string>
      ContentWrites: Set<string>
      UnknownParentWrites: Set<string> }

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

// ---- Phase 315: the envelope's operations and the footprint builders ----

/// Agent-readable rejection guidance (the envelope discipline, GP5): what went wrong plus the
/// enumerated alternatives, so a refused agent can repair its emission instead of guessing.
/// Defined here since Phase 315 (it was declared in `Fuaran.Core.AiSurface`), so `Rejection.explain`
/// can return it; the namespace is unchanged, so every `RejectionGuidance` in source still means it.
type RejectionGuidance =
    { Message: string
      Alternatives: string list }

/// The words `Rejection.explain` speaks in (Phase 315) — what the domain calls a node and its root,
/// so the same explainer serves a document ("block", "document root") and a sheet ("cell",
/// "workbook"). `RejectionNouns.generic` is "node" / "root".
type RejectionNouns = { Node: string; Root: string }

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
    /// `wouldNestUnderSelf`, `notAContainer`, `reorderMismatch`, `keyedPosition`. Two answers differ
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
          UnknownParentWrites = Set.empty }

    /// Both footprints' addresses, kind by kind. `independent (union a b) c` holds exactly when
    /// `independent a c` and `independent b c` both do, so growing a footprint never frees a pair.
    let union (a: Footprint) (b: Footprint) : Footprint =
        { Reads = Set.union a.Reads b.Reads
          StructureWrites = Set.union a.StructureWrites b.StructureWrites
          ContentWrites = Set.union a.ContentWrites b.ContentWrites
          UnknownParentWrites = Set.union a.UnknownParentWrites b.UnknownParentWrites }

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
        { Reads = Set.ofList [ parent; id ]
          StructureWrites = Set.singleton parent
          ContentWrites = Set.singleton id
          UnknownParentWrites = Set.empty }

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
        { Reads = Set.ofList [ id; newParent ]
          StructureWrites = Set.singleton newParent
          ContentWrites = Set.singleton id
          UnknownParentWrites = Set.singleton id }

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
            if not (Tree.exists t idw parent root) then
                Error(UnknownNode(parent, Tree.ids t root))
            else
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
                match Tree.tryFind t idw parent root with
                | None -> Error(UnknownNode(parent, allIds ()))
                | Some p ->
                    let byId = w.Children p |> List.map (fun c -> idw.ToString(w.Id c), c) |> Map.ofList
                    let reordered = order |> List.map (fun i -> byId.[idw.ToString i])

                    Tree.updateNode t idw parent (fun p -> w.ReplaceChildren p reordered) root
                    |> Option.map Ok
                    |> Option.defaultValue (Error(UnknownNode(parent, allIds ()))))

        | MoveNode(target, newParent) ->
            if eq (w.Id root) target then
                Error CannotRemoveRoot
            elif not (Tree.exists t idw target root) then
                Error(UnknownNode(target, allIds ()))
            elif not (Tree.exists t idw newParent root) then
                Error(UnknownNode(newParent, allIds ()))
            else
                match Tree.tryFind t idw newParent root with
                | Some np0 when not (canHold np0) -> Error(NotAContainer(newParent, w.KindTag np0))
                | _ ->

                    match Tree.tryFind t idw target root with
                    | None -> Error(UnknownNode(target, allIds ()))
                    | Some sub ->
                        // newParent must not be the target itself nor any of its descendants.
                        let descendantIds = Tree.ids t sub |> Set.ofList |> Set.map idw.ToString

                        if descendantIds.Contains(idw.ToString newParent) then
                            let relation =
                                if idw.ToString newParent = idw.ToString target then
                                    NestRelation.Self
                                else
                                    NestRelation.Descendant

                            Error(WouldNestUnderSelf(target, relation))
                        else
                            // remove then insert: both halves already validated above.
                            match applyWith canHold w t keyed idw (RemoveNode target) root with
                            | Error e -> Error e
                            | Ok removed ->
                                // re-target into the removed tree (newParent still present there).
                                match Tree.tryFind t idw newParent removed with
                                | None -> Error(UnknownNode(newParent, Tree.ids t removed))
                                | Some _ ->
                                    Tree.updateNode
                                        t
                                        idw
                                        newParent
                                        (fun np -> w.ReplaceChildren np (w.Children np @ [ sub ]))
                                        removed
                                    |> Option.map Ok
                                    |> Option.defaultValue (Error(UnknownNode(newParent, Tree.ids t removed)))

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
        | MoveNode _
        | Batch _ -> applyWith canHold w t keyed idw op root |> Result.map ignore

    /// Dry-run validation (Phase 246): would `op` be accepted against `root`? Returns the
    /// exact `Rejection` `apply` would, but builds **no** new tree for the index/structure
    /// ops. `MoveNode` / `Batch` are order-dependent, so their check simulates through
    /// `apply` (and discards the result). The AI pre-flight surface — "is this op legal?"
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
                // The pre-state node restores the content; its children are not read by the
                // inverse either, so the children the tree holds when the undo runs are kept.
                | UpdateNode node -> Ok(UpdateNode(Tree.tryFind w idw (w.Id node) pre |> Option.get))
                | Batch _ -> Ok op // unreachable (handled above) — keeps the match total

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
    ///   - an empty `Batch []` is dropped, and a `Batch` is normalised recursively.
    /// Left-to-right peephole passes iterated to a fixpoint, so it is genuinely **idempotent**
    /// (`normalize ∘ normalize = normalize`) and never lengthens a script. `'Node` needs no equality
    /// (it compares ids only). **Caveat:** preservation is guaranteed only for a script that is
    /// *applyable* to the tree — collapsing an insert/remove pair can turn an `applyAll` that would
    /// have *failed* at that pair into one that succeeds, so normalise after validating, not before.
    let rec normalize
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        : SkeletonOp<'Node, 'Id> list =
        let eq = idw.Equals

        // 1. normalise inside batches; drop empties
        let stripped =
            ops
            |> List.collect (fun op ->
                match op with
                | Batch inner ->
                    match normalize w idw inner with
                    | [] -> []
                    | xs -> [ Batch xs ]
                | _ -> [ op ])

        // 2. adjacency peephole — one pass commits each non-collapsing head before recursing, so a
        //    collapse that newly adjoins two collapsible ops (e.g. a cancelled insert/remove between
        //    two same-target moves) is not caught within the pass.
        let rec peephole xs =
            match xs with
            | [] -> []
            | InsertChild(_, node) :: RemoveNode target :: rest when eq (w.Id node) target -> peephole rest
            | MoveNode(t1, _) :: (MoveNode(t2, _) as second) :: rest when eq t1 t2 -> peephole (second :: rest)
            | ReorderChildren(p1, _) :: (ReorderChildren(p2, _) as second) :: rest when eq p1 p2 ->
                peephole (second :: rest)
            | x :: rest -> x :: peephole rest

        // …so iterate the pass to a fixpoint. A pass only ever drops ops, so the length is
        // monotonically non-increasing; an unchanged length means no collapse fired ⇒ done. This is
        // what makes `normalize` genuinely idempotent (no 'Node equality needed — length suffices).
        let rec toFixpoint xs =
            let xs' = peephole xs

            if List.length xs' = List.length xs then
                xs'
            else
                toFixpoint xs'

        toFixpoint stripped

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
    // (`relocation_diamond_fails_for_a_remove`); and the two ops carry the SAME four address sets
    // (`relocation_footprints_coincide`). A predicate over footprints alone gives one verdict to
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

    /// The read/write footprint of an op-script (Phase 78) — a pure, total derivation over the skeleton
    /// five through the node/id witnesses (the `NodeWitness` reads the ids out of an inserted `'Node`
    /// subtree; the `IdWitness` keys every address by its string form). `Batch` folds its inner ops.
    /// Total: no tree, no failure case — it never throws (GP4) and mints no ids. Over-approximating by
    /// design — see `Footprint` and STABILITY.md for the pinned conservative cases.
    let footprint (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (ops: SkeletonOp<'Node, 'Id> list) : Footprint =
        let key (i: 'Id) = idw.ToString i

        let subtreeKeys (node: 'Node) =
            Tree.ids w node |> List.map key |> Set.ofList

        let rec ofOp (op: SkeletonOp<'Node, 'Id>) : Footprint =
            match op with
            | InsertChild(parent, node) ->
                let inserted = subtreeKeys node
                // parent existence + the inserted ids' dup-check are reads; the parent's child-list is a
                // (known) structure-write; the inserted subtree is authored into being — a content-write.
                //
                // Phase 137: the dup-check named here is now the one `validateInsert` actually performs
                // — `firstDuplicateId` reads exactly this `Tree.ids w node` set against the whole tree,
                // so `Reads` describes a read that happens rather than one the footprint assumed. The
                // set is unchanged: the validator's other half (is the subtree unique WITHIN ITSELF?) is
                // internal to the op and reads no tree state, so it adds nothing to the footprint and
                // creates no new collision between concurrent scripts.
                { Reads = Set.add (key parent) inserted
                  StructureWrites = Set.singleton (key parent)
                  ContentWrites = inserted
                  UnknownParentWrites = Set.empty }
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
                { Reads = Set.ofList [ key target; key newParent ]
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

                { Reads = Set.singleton target
                  StructureWrites = Set.empty
                  ContentWrites = Set.singleton target
                  UnknownParentWrites = Set.singleton target }

        List.fold (fun acc op -> unionFootprint acc (ofOp op)) emptyFootprint ops

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
              Interference.RightUnknownParent(structuralA, b.UnknownParentWrites) ]

    /// Are two footprints **independent** (Phase 78) — do their scripts provably commute under `apply`?
    /// Pairwise disjointness across the write kinds, with the conservative rules pinned:
    ///   - no content write/write overlap, and no content-write vs read overlap either way (a node one
    ///     script authors/destroys must not be read or written by the other);
    ///   - no shared **named** structural parent — two positional inserts (or an insert + a reorder, …)
    ///     under one parent are NOT independent (they shift the same siblings — THE pinned same-parent
    ///     rule);
    ///   - an `UnknownParentWrites` op (a remove/move, whose source parent is a tree fact) conflicts with
    ///     *any* structural write — known or unknown — in the other script (the pinned unknown-parent
    ///     over-approximation): a remove/move is only independent of a structure-free script.
    /// `true` is a promise (they commute); `false` is always a safe answer. Total, no throws (GP4).
    ///
    /// **The last clause is NECESSARY over this record, not merely conservative (Phase 143).** A move
    /// and a batch that removes and reorders carry the SAME four address sets, and one of them
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

    /// Derive a script such that `Ops.applyAll (toOps w idw before after) before`
    /// reproduces `after` structurally. Relocated subtrees diff to `MoveNode` (never
    /// remove+insert), so an unchanged subtree is preserved, not destroyed and rebuilt.
    /// The emitted order is always applyable: added nodes go in as leaf shells (top-down),
    /// every survivor is then reattached/reordered to its `after` position, and removed
    /// regions are deleted **last** (so a surviving child is pulled out before its old
    /// container is removed). Structural only — per-kind property edits are out of scope
    /// (`Core.Ops`' remit); the two roots must share an id.
    let toOps
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =

        let key (i: 'Id) = idw.ToString i

        // First duplicated id in a tree (by key), if any — Core's named structural predicate
        // (Phase 139). This was a `groupBy` of its own until then, and the retirement is the point:
        // a diff refusing a malformed tree and an insert refusing a malformed graft are the same
        // notion of malformed, and they now read the same function.
        //
        // ONE OBSERVABLE CHANGE, and it is which id is NAMED, never whether the tree is refused.
        // The `groupBy` form reported the first id whose GROUP had more than one member, in
        // first-appearance order of the keys; `Tree.wellFormed` reports the first id at its SECOND
        // occurrence in preorder. For `[a; b; b; a]` the old form said `a` and the new says `b`.
        // The new answer is the one `Rejection.DuplicateId` already gave on the accept path, so the
        // two paths now name the same offender for the same tree. Recorded in STABILITY.md.
        let dupId (root: 'Node) =
            match Tree.wellFormed w idw root with
            | Tree.RepeatedId d -> Some d
            | Tree.Structural -> None

        // key -> parent id, for every non-root node.
        let parentMap (nodes: 'Node list) =
            [ for p in nodes do
                  for c in w.Children p -> key (w.Id c), w.Id p ]
            |> Map.ofList

        let childKeysOf (n: 'Node) =
            w.Children n |> List.map (fun c -> key (w.Id c))

        if key (w.Id before) <> key (w.Id after) then
            Error(RootIdMismatch(w.Id before, w.Id after))
        else
            match dupId before with
            | Some d -> Error(DuplicateIdInTree d)
            | None ->
                match dupId after with
                | Some d -> Error(DuplicateIdInTree d)
                | None ->
                    let beforeNodes = Tree.preorder w before
                    let afterNodes = Tree.preorder w after
                    let bIds = beforeNodes |> List.map (fun n -> key (w.Id n)) |> Set.ofList
                    let aIds = afterNodes |> List.map (fun n -> key (w.Id n)) |> Set.ofList
                    let aParent = parentMap afterNodes
                    let bParent = parentMap beforeNodes

                    let bChildKeys =
                        beforeNodes |> List.map (fun n -> key (w.Id n), childKeysOf n) |> Map.ofList

                    let ops = ResizeArray<SkeletonOp<'Node, 'Id>>()
                    // Parents whose order must be restated once membership is final (step 4).
                    let reorderParents = ResizeArray<'Id>()

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

                    // 4. Order, last — every parent now holds exactly its after-children, so
                    //    naming the after-order is a legal permutation. One op per changed
                    //    parent, where the old sweep emitted one MoveNode per child.
                    for pid in reorderParents do
                        match Tree.tryFind w idw pid after with
                        | Some p when List.length (w.Children p) > 1 ->
                            ops.Add(ReorderChildren(pid, w.Children p |> List.map w.Id))
                        | _ -> ()

                    Ok(List.ofSeq ops)

    /// Container-aware diff (Phase 09) — the `canHold`-aware mirror of `toOps`, the diff-path
    /// analogue of `Ops.applyContained`. Every parent the emitted script addresses comes from
    /// the `after` tree, so an `after` that nests children under a node `canHold` rejects makes
    /// a container-legal script impossible: that is surfaced as a typed `TargetNotAContainer`
    /// rather than an `InsertChild`/`MoveNode` under a leaf. When every `after`-parent is a
    /// container the result is exactly `toOps` (so `toOps` is the `(fun _ -> true)` wrapper —
    /// the same relationship `apply`/`applyContained` have). Containment *legality* (which kind
    /// may parent which) stays domain-side; `canHold` answers only "can this node hold children
    /// at all".
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
        | None -> toOps w idw before after
