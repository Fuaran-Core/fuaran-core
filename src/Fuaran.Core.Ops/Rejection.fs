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
