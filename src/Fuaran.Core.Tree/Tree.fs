namespace Fuaran.Core

/// Witness over a domain's id representation. Identity is the one genuine
/// cross-domain axis (string ids in Doc/Calc, Guid ids in UI/Music — 2-2): it is
/// resolved here as a parameter, never a fixed type. The generic functions mint no
/// ids of their own (hygiene derives ids deterministically via `ToString`/`OfString`),
/// so this witness carries no effectful `fresh` — id minting stays domain-side.
/// FSharp.Core only, Fable-clean.
type IdWitness<'Id> =
    {
        /// The id's string key — what every map, set and stamp here keys by (`Tree.wellFormed`,
        /// `Tree.Index`, `FreshIds`). Must be injective: two ids that render alike are one id to
        /// every uniqueness check.
        ToString: 'Id -> string
        /// The inverse of `ToString` (`OfString (ToString id)` equals `id`). In this package only
        /// `FreshIds` reads it, to turn a minted candidate key back into an id.
        OfString: string -> 'Id
        /// Id equality for the walking lookups (`tryFind`, `parentOf`, `path`, `updateNode`). Must
        /// hold exactly when the two `ToString` keys are equal, or the walks and the keyed index
        /// answer differently.
        Equals: 'Id -> 'Id -> bool
    }

/// Witness over a domain's node type. The core owns NO base node type — closed
/// exhaustive `NodeKind` DUs are load-bearing (a compiler-checked totality guarantee no
/// open base type can give) and stay sovereign per domain. The core sees a node only
/// through these four accessors:
///   - `Id`              — the node's permanent identity
///   - `KindTag`         — a string tag for the node's kind (for envelopes / addressing)
///   - `Children`        — the ordered child list (a finite walk — totality by construction)
///   - `ReplaceChildren` — rebuild a node with a new child list (the structural-edit seam)
type NodeWitness<'Node, 'Id> =
    {
        /// The node's identity. The engine never changes it; only `Tree.remapIds` rewrites one,
        /// through a setter the caller passes.
        Id: 'Node -> 'Id
        /// The node's kind as a string — read by every shape hash and staleness stamp, and the key
        /// the op layer's container rules look a parent up by.
        KindTag: 'Node -> string
        /// The ordered structural children: the only nodes the unkeyed walks reach, and the list
        /// every structural edit rebuilds. Nodes held in keyed positions belong in `KeyedWitness`.
        Children: 'Node -> 'Node list
        /// The node with exactly this child list and its own `Id` and `KindTag` kept
        /// (`Children (ReplaceChildren n cs) = cs`) — the seam every rebuild and edit goes through.
        ReplaceChildren: 'Node -> 'Node list -> 'Node
    }

/// The domain-supplied KEYED-CHILDREN declaration (Phase 189; moved here from the conformance kit
/// and widened by Phase 286): the nodes a domain holds where `NodeWitness.Children` does not report
/// them — a case table, a fallback slot, a named alternative, an argument position — together with
/// the domain's own full-walk id check over them.
///
/// **Why this is a witness of its own and not a field of `NodeWitness`.** `Children` is what the
/// engine REBUILDS through: a structural edit appends to it, filters it and permutes it. Widening
/// it to reach keyed positions would oblige every domain to re-express a case table as an ordered
/// list the engine may restructure. That reasoning (Phase 189) stands, so the keyed positions stay
/// a separate declaration and the engine never adds to, removes from or reorders them.
///
/// **What the engine does with it (Phase 286).** It READS it. `Tree.traversal` derives the
/// `NodeWitness` whose walk covers both surfaces, so every navigator reaches a node held in a keyed
/// position; `Tree.wellFormedKeyed` / `Tree.graftWellFormedKeyed` hold id uniqueness over that
/// walk; and `Ops.applyContainedKeyed` refuses a `DuplicateId` there and addresses nodes below a
/// keyed position. A domain declares its keyed positions ONCE, here, and stops re-deriving the walk.
///
/// **A domain with no keyed position declares none** — `KeyedChildren = fun _ -> []`,
/// `ReplaceKeyedChildren = fun n _ -> n`, `PlaceKeyedChild = fun _ _ -> None` — and every keyed form
/// then answers exactly what its unkeyed form does.
type KeyedWitness<'Node, 'Id> =
    {
        /// The domain's own id check, named as a reader of a counterexample would look for it —
        /// name the thing an author calls ("the full walk in `Doc.validate`"), not the module it
        /// lives in.
        Surface: string
        /// The nodes this node holds in keyed, non-structural positions — the ones `Children` does
        /// not report — in the domain's own order. `[]` for a node that holds none, and
        /// `fun _ -> []` for a domain that has none at all. The ids these nodes carry are what
        /// Phase 189's `HasKeyedChildren` declared; that list is now DERIVED (`Tree.keyedIds`)
        /// rather than stated a second time, so the declaration the kit certifies and the one the
        /// engine reads cannot disagree.
        KeyedChildren: 'Node -> 'Node list
        /// Rebuild a node with new nodes in its keyed positions, ARITY-PRESERVING: given a list as
        /// long as `KeyedChildren n`, position for position, the result holds exactly that list
        /// there and is otherwise `n` — the same content, the same `Children`. It never adds or
        /// vacates a position; that is a domain edit, not a rebuild. This is the seam
        /// `Tree.traversal` rebuilds through, so a walk that rewrites a node below a keyed position
        /// can put it back. `fun n _ -> n` for a domain that has none.
        ReplaceKeyedChildren: 'Node -> 'Node list -> 'Node
        /// Place a node carrying `id` in a keyed position of this node, or `None` where this node
        /// has no keyed position to place into. The kit BUILDS its collisions through this rather
        /// than drawing them: a generator's contract is a fresh id, so a drawn sample can never
        /// exhibit the defect those laws are about, and a law quantified over the drawn sample
        /// alone would certify a check that checked nothing (`opAlgebra`'s built arm, for the same
        /// reason).
        PlaceKeyedChild: 'Node -> 'Id -> 'Node option
        /// The domain's own full-walk id check: `true` when this tree's ids are unique over the
        /// domain's OWN walk, keyed positions included. It is the obligation the kit certifies, so
        /// it is the domain's function and never derived from the fields above.
        IdsUnique: 'Node -> bool
    }

/// Generic tree addressing / walking / structural update, generic over the `'Node`
/// and `'Id` witnesses. No domain `NodeKind` is ever in scope here.
module Tree =

    /// Preorder (node-then-children) flattening. A finite walk over a finite child list —
    /// structurally total. Iterative with an explicit work-list (Phase 10): a deep tree cannot
    /// overflow the stack the way the prior body-recursive version could. Tail-recursive; the
    /// emitted order is identical (node, then each child subtree left-to-right).
    let preorder (w: NodeWitness<'Node, 'Id>) (root: 'Node) : 'Node list =
        let rec loop (acc: 'Node list) (stack: 'Node list) =
            match stack with
            | [] -> List.rev acc
            | node :: rest -> loop (node :: acc) (w.Children node @ rest)

        loop [] [ root ]

    /// Every id in the tree, in preorder.
    let ids (w: NodeWitness<'Node, 'Id>) (root: 'Node) : 'Id list = preorder w root |> List.map w.Id

    // ---- structural validity (Phase 139) ----

    /// The verdict of `Tree.wellFormed` — Core's ONE definition of STRUCTURAL validity, named so a
    /// claim can point at the layer it is about.
    ///
    /// **Validity is three layers and this is the first of them.** Structural validity is what the
    /// witness surface can see; a domain's wire boundary decides VOCABULARY validity (is this a kind
    /// I know, with the fields it declares), and a domain's pre-emit lint decides its own RULE
    /// families. Nothing here says anything about the other two, and a reader who wants "is this
    /// tree valid" has to say which question they are asking.
    ///
    /// **Scope: the WITNESS SURFACE.** `Tree.ids` walks `NodeWitness.Children`, so `wellFormed`
    /// quantifies over exactly the nodes Core rebuilds through. A domain that holds nodes in KEYED,
    /// NON-STRUCTURAL positions — a switch case table, a named slot map — keeps them outside
    /// `Children`, so they are invisible to the unkeyed forms. Since Phase 286 that is no longer the
    /// domain's own obligation: it declares those positions once, in a `KeyedWitness`, and
    /// `Tree.wellFormedKeyed` / `Tree.graftWellFormedKeyed` return this same verdict over the walk
    /// that includes them. The unkeyed forms keep their answer, which is the keyed forms' answer
    /// for a domain that declares no keyed position.
    ///
    /// **One clause, not two, and that is a correction to how this was described.** The natural
    /// pairing is "unique ids AND a single root", but a `'Node` value IS its tree here: the walk
    /// starts at exactly one node by construction of the type, there is no forest to exclude and no
    /// second parent to find, so single-rootedness is a TYPE-LEVEL guarantee rather than a
    /// checkable clause. What remains checkable — and what every downstream function actually
    /// depends on — is id uniqueness: `Tree.updateNode` rewrites EVERY node matching a repeated id,
    /// and `Tree.Index.build`'s `Map.ofList` silently keeps the last. So the predicate has one
    /// clause and says why.
    type WellFormed<'Id> =
        /// Every id the walk reaches is distinct — the `Children` preorder for the unkeyed forms,
        /// the `Tree.traversal` preorder for the keyed ones.
        | Structural
        /// The FIRST id, in that preorder, that the tree carries twice.
        | RepeatedId of 'Id

    /// The first id in `candidates` that `seen` already holds or that `candidates` repeats within
    /// itself — the one scan every uniqueness verdict here projects from, so "already in the tree"
    /// and "duplicated inside the graft" cannot drift apart into two different notions of the same
    /// defect.
    let private firstRepeat (idw: IdWitness<'Id>) (seen: Set<string>) (candidates: 'Id list) : 'Id option =
        let rec scan (seen: Set<string>) ids =
            match ids with
            | [] -> None
            | i :: rest ->
                let k = idw.ToString i

                if Set.contains k seen then
                    Some i
                else
                    scan (Set.add k seen) rest

        scan seen candidates

    /// The same scan, over ids a caller has already collected (Phase 286): the first of `incoming`
    /// that `held` carries, or that `incoming` repeats within itself; `None` when admitting
    /// `incoming` beside `held` keeps every id unique. `graftWellFormed` is this with `held` the
    /// tree's ids and `incoming` the graft's, and `Ops.applyContainedKeyed` asks it about an
    /// `UpdateNode` payload's keyed subtrees, whose ids arrive beside a tree the payload's target
    /// keeps.
    let firstRepeatedId (idw: IdWitness<'Id>) (held: 'Id list) (incoming: 'Id list) : 'Id option =
        firstRepeat idw (held |> List.map idw.ToString |> Set.ofList) incoming

    /// Is `root` structurally well-formed, and if not, which id breaks it? One preorder scan.
    let wellFormed (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (root: 'Node) : WellFormed<'Id> =
        match firstRepeat idw Set.empty (ids w root) with
        | Some d -> RepeatedId d
        | None -> Structural

    /// The boolean form, for a caller that does not need the offender named.
    let isWellFormed (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (root: 'Node) : bool =
        match wellFormed w idw root with
        | Structural -> true
        | RepeatedId _ -> false

    /// The verdict for the tree that WOULD result from grafting `node` anywhere into `root` —
    /// computed without building it. Seeded from the root's ids, so one scan decides both halves:
    /// an id the tree already carries, and an id the graft repeats within itself. The offender named
    /// is the first in `Tree.ids node` order, which for a preorder walk means the graft's own id
    /// outranks its descendants'.
    ///
    /// This is the question every insert validator asks; `Ops`'s reads it, so the accept path and
    /// this predicate cannot diverge. `Tree.graftWellFormedKeyed` asks it over the walk that
    /// includes a domain's keyed positions, on both sides of the graft (Phase 286).
    let graftWellFormed
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (node: 'Node)
        (root: 'Node)
        : WellFormed<'Id> =
        match firstRepeatedId idw (ids w root) (ids w node) with
        | Some d -> RepeatedId d
        | None -> Structural

    /// The node carrying `target`, if present.
    let tryFind (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (target: 'Id) (root: 'Node) : 'Node option =
        preorder w root |> List.tryFind (fun n -> idw.Equals (w.Id n) target)

    /// Whether any node the `Children` walk reaches carries `target` — a preorder scan under
    /// `Equals`, O(n). A keyed position is reached only through `Tree.traversal`.
    let exists (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (target: 'Id) (root: 'Node) : bool =
        tryFind w idw target root |> Option.isSome

    /// The parent of `target` (None for the root or an absent id).
    let parentOf (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (target: 'Id) (root: 'Node) : 'Node option =
        preorder w root
        |> List.tryFind (fun n -> w.Children n |> List.exists (fun c -> idw.Equals (w.Id c) target))

    /// The id-path from the root down to `target` (inclusive), if present. This is the
    /// lexical address used for capture-avoiding hole binding (hygiene): an absolute
    /// path, never a bare name. Iterative explicit-stack DFS (Phase 19) — a deep tree cannot
    /// overflow the stack; the first preorder match wins, exactly as the recursive version.
    let path (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (target: 'Id) (root: 'Node) : 'Id list option =
        // each work item carries the node and the REVERSED id-trail to it (prepend is O(1);
        // reverse once at the match, so the whole walk is O(n) not O(n·depth))
        let rec loop (stack: ('Node * 'Id list) list) =
            match stack with
            | [] -> None
            | (node, revTrail) :: rest ->
                let here = w.Id node :: revTrail

                if idw.Equals (w.Id node) target then
                    Some(List.rev here)
                else
                    // descend depth-first, preserving left-to-right preorder
                    loop ((w.Children node |> List.map (fun c -> c, here)) @ rest)

        loop [ (root, []) ]

    /// Bottom-up structural rebuild: map every node through `f` *after* its children are
    /// rebuilt, reassembling via `ReplaceChildren`. Iterative with an explicit frame stack
    /// (Phase 19) so a deep tree cannot overflow — the engine behind `map` (`updateNode` copies
    /// only the root-to-target path since Phase 298). A frame is `(node, children-not-yet-rebuilt, rebuilt-children-reversed)`;
    /// `carry` hands a just-finished child up to its parent frame.
    let private rebuildPostorder (w: NodeWitness<'Node, 'Id>) (f: 'Node -> 'Node) (root: 'Node) : 'Node =
        let rec loop (stack: ('Node * 'Node list * 'Node list) list) (carry: 'Node option) : 'Node =
            match stack with
            | [] -> root // unreachable: the root frame returns directly when `rest` is empty
            | (node, remaining, doneRev) :: rest ->
                match carry with
                | Some child -> loop ((node, remaining, child :: doneRev) :: rest) None
                | None ->
                    match remaining with
                    | [] ->
                        let rebuilt = f (w.ReplaceChildren node (List.rev doneRev))

                        match rest with
                        | [] -> rebuilt
                        | _ -> loop rest (Some rebuilt)
                    | c :: cs -> loop ((c, w.Children c, []) :: (node, cs, doneRev) :: rest) None

        loop [ (root, w.Children root, []) ] None

    /// Rebuild the tree, replacing the node identified by `target` with `f` applied to it.
    /// Returns None if `target` is absent. This backs every `Ops.apply`.
    ///
    /// **Path copying (Phase 298).** The target is located by an iterative depth-first search
    /// (the Phase-19 posture — a deep tree cannot overflow) that records the root-to-target path,
    /// and only that path is rebuilt: `f` sees the target with its own children, each ancestor is
    /// rebuilt through `ReplaceChildren` with its one changed child, and every other subtree is
    /// SHARED with `root`, untouched. Until Phase 298 every node of the tree was rebuilt through
    /// `ReplaceChildren` on every edit, so each `Ops.apply` reallocated the whole tree. For a
    /// conformant witness (`ReplaceChildren` round-trips, per the witness laws) the result is the
    /// same tree. The FIRST preorder node carrying `target` is the one rewritten — on a
    /// well-formed tree (unique ids, `Tree.wellFormed`) it is the only one.
    let updateNode
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (f: 'Node -> 'Node)
        (root: 'Node)
        : 'Node option =
        // each work item: a node, and its ancestors nearest-first with the child index taken at each
        let rec find (stack: ('Node * ('Node * int) list) list) =
            match stack with
            | [] -> None
            | (node, ancestors) :: rest ->
                if idw.Equals (w.Id node) target then
                    Some(node, ancestors)
                else
                    find ((w.Children node |> List.mapi (fun i c -> c, (node, i) :: ancestors)) @ rest)

        find [ root, [] ]
        |> Option.map (fun (node, ancestors) ->
            (f node, ancestors)
            ||> List.fold (fun child (parent, i) ->
                w.ReplaceChildren parent (w.Children parent |> List.mapi (fun j c -> if j = i then child else c))))

    /// The digest pre-image of a tree under a per-node labelling: every preorder node as TWO
    /// fields — its label, then its ARITY (`List.length (w.Children n)`, as a decimal) — through
    /// `Hash.canonicalFields`. Shared by `contentHash` (label = kind tag) and `encodePreimage`
    /// (label = the caller's encoder), so the two folds cannot drift apart.
    ///
    /// **Why the arity is there (Phase 290).** A preorder alone does not determine a tree:
    /// `root(a(a1,a2), b(b1))` and `root(a(a1,a2,b(b1)))` have the same preorder — they are one
    /// `MoveNode` apart — and hashed equal under every encoder, however injective, until this
    /// fold carried the shape. A preorder WITH each node's child count is injective over ordered
    /// trees (`preorder_arity_injective` in `proofs/TreeOps.fst`): the count says where each
    /// node's subtree ends, so the flat list parses back into one tree. The arity is what the
    /// witness exposes at the node itself, so the fold stays one local pass (a per-node depth
    /// would be injective too, but needs the path carried down; a bare descent marker with no
    /// ascent is not — `DECISIONS.md`, Phase 290). Through `canonicalFields` rather than a bare
    /// separator so a label that spells the separator cannot run into the next field either (the
    /// injectivity `Hash.canonicalFields` documents and `proofs/Query.fst` proves).
    let private preimageWith (w: NodeWitness<'Node, 'Id>) (label: 'Node -> string) (node: 'Node) : string =
        preorder w node
        |> List.collect (fun n -> [ label n; string (List.length (w.Children n)) ])
        |> Hash.canonicalFields

    /// Content hash of a node's structural SHAPE: the kind tag and the child count of every node
    /// in preorder, through `Hash.canonicalFields` and the portable FNV-1a. A cheap, deterministic
    /// fingerprint for the bounded-escape `Custom`-region discipline. Two trees with one
    /// `contentHash` pre-image have the same shape and the same kind at every position — it sees
    /// nesting, not just the sequence of kinds (Phase 290; before it two trees one `MoveNode`
    /// apart could hash equal). It does NOT see per-node payload: for that, `encodeHash`.
    let contentHash (w: NodeWitness<'Node, 'Id>) (node: 'Node) : string =
        preimageWith w w.KindTag node |> Hash.fnv1a

    // ---- convenience combinators (Phase 249) ----
    // The everyday traversals every domain otherwise re-derives atop `preorder` + the
    // witness accessors. Purely additive, structurally recursive — no new types.

    /// Preorder fold over the tree (node-then-children).
    let fold (w: NodeWitness<'Node, 'Id>) (f: 'State -> 'Node -> 'State) (state: 'State) (root: 'Node) : 'State =
        preorder w root |> List.fold f state

    /// The number of nodes in the tree.
    let count (w: NodeWitness<'Node, 'Id>) (root: 'Node) : int = preorder w root |> List.length

    /// The proper ancestors of `target`, root-first (root -> ... -> immediate parent).
    /// Empty for the root itself or an absent id.
    let ancestors (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (target: 'Id) (root: 'Node) : 'Node list =
        match path w idw target root with
        | Some ids when not (List.isEmpty ids) ->
            // drop the target (the last id on the path), keep the chain root-first
            ids
            |> List.rev
            |> List.tail
            |> List.rev
            |> List.choose (fun i -> tryFind w idw i root)
        | _ -> []

    /// Every node strictly below `node` (its subtree minus itself), in preorder.
    let descendants (w: NodeWitness<'Node, 'Id>) (node: 'Node) : 'Node list =
        match preorder w node with
        | _ :: rest -> rest
        | [] -> []

    /// The siblings of `target` (the other children of its parent). Empty for the root
    /// or an absent id.
    let siblings (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (target: 'Id) (root: 'Node) : 'Node list =
        match parentOf w idw target root with
        | Some p -> w.Children p |> List.filter (fun c -> not (idw.Equals (w.Id c) target))
        | None -> []

    /// The depth of `target` (root = 0). None if absent.
    let depth (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (target: 'Id) (root: 'Node) : int option =
        path w idw target root |> Option.map (fun ids -> List.length ids - 1)

    /// Extract the subtree rooted at `target` as a standalone node (the cut/copy
    /// primitive). None if absent. A node *is* its subtree, so this is `tryFind`.
    let subtree (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (target: 'Id) (root: 'Node) : 'Node option =
        tryFind w idw target root

    // ---- transform + id-remap (Phase 02) ----
    // The whole-tree rebuild every domain otherwise re-derives, plus the clone/paste
    // id-rewrite primitive. `remapIds` takes its id-setter as a *per-call parameter* rather
    // than adding a `SetId` field to `NodeWitness`: id mutation is rare (clone/paste
    // relocation only) and does not earn a permanent witness seam (the surface is frozen).
    // Iterative via `rebuildPostorder` (Phase 19); nothing mints ids.

    /// Rebuild the tree applying `f` to every node, bottom-up: a node's children are mapped
    /// first, the node is rebuilt around them via `ReplaceChildren`, then `f` is applied to
    /// the node-with-mapped-children. `f` may return a structurally-different node (e.g. a new
    /// id); a leaf (empty child list) passes through `ReplaceChildren n []` cleanly. Iterative
    /// (Phase 19) — a deep tree cannot overflow.
    let map (w: NodeWitness<'Node, 'Id>) (f: 'Node -> 'Node) (node: 'Node) : 'Node = rebuildPostorder w f node

    /// Every node satisfying `pred`, in preorder.
    let filter (w: NodeWitness<'Node, 'Id>) (pred: 'Node -> bool) (root: 'Node) : 'Node list =
        preorder w root |> List.filter pred

    /// The first `Some` of `chooser` over the tree, in preorder.
    let tryPick (w: NodeWitness<'Node, 'Id>) (chooser: 'Node -> 'a option) (root: 'Node) : 'a option =
        preorder w root |> List.tryPick chooser

    /// Rewrite every id in the tree through `rename`, using a caller-supplied `setId` to
    /// rebuild a node with its new id. `setId` is a per-call parameter, NOT a witness field
    /// (the witness exposes no id-setter by design — paste is the only caller). The everyday
    /// use is clone/paste: rewrite a copied subtree's ids to a fresh, collision-free set
    /// before `InsertChild` — without it, re-inserting a copied subtree raises `DuplicateId`.
    /// Deterministic: ids derive from existing ids via `rename` (no minting; consistent with
    /// the no-`fresh` `IdWitness`).
    let remapIds
        (w: NodeWitness<'Node, 'Id>)
        (setId: 'Id -> 'Node -> 'Node)
        (rename: 'Id -> 'Id)
        (root: 'Node)
        : 'Node =
        map w (fun n -> setId (rename (w.Id n)) n) root

    // ---- the keyed walk (Phase 286) ----
    // A domain that holds nodes in keyed positions declares them once, in a `KeyedWitness`, and
    // every walk here reaches them through ONE derived witness rather than through a second copy of
    // each walk. `traversal` is that witness; the named keyed forms below are the walks a caller
    // reaches for most, and every other combinator in this module (`tryFind`, `parentOf`, `path`,
    // `ancestors`, `updateNode`, `map`, `remapIds`, `Index.build`, …) takes `traversal nodew keyw`
    // in place of `nodew` to reach a node held in a keyed position. For a domain that declares no
    // keyed position every keyed form answers exactly what its unkeyed form does.

    /// The ids of the nodes `n` holds in keyed positions, in the domain's order — Phase 189's
    /// `HasKeyedChildren`, derived from `KeyedWitness.KeyedChildren` rather than declared beside it.
    let keyedIds (w: NodeWitness<'Node, 'Id>) (keyw: KeyedWitness<'Node, 'Id>) (n: 'Node) : 'Id list =
        keyw.KeyedChildren n |> List.map w.Id

    /// The `NodeWitness` whose walk covers both surfaces: a node's children are the nodes it holds
    /// in keyed positions, THEN its structural `Children`, and a rebuild splits the list back at
    /// that boundary — the keyed prefix through `ReplaceKeyedChildren`, the rest through
    /// `ReplaceChildren`.
    ///
    /// **Keyed first, and why.** A node's keyed positions are part of its own content (an
    /// `UpdateNode` payload carries them; `ReplaceChildren` does not touch them), so the walk reads
    /// them before it descends the structural list. It also makes the structural surface the TAIL
    /// of the combined list, so appending a structural child — the one way `Ops` grows a node — is
    /// appending to this witness's children too: the keyed engine's insert is literally the
    /// unkeyed engine's insert over this walk, which is how `proofs/Preservation.fst` transfers the
    /// preservation theorem to it, first offender included.
    ///
    /// **A READ-and-REBUILD witness, not an EDIT witness.** Its `ReplaceChildren` is exact on a list
    /// as long as its `Children` — which is every list a rebuild-in-place walk passes (`updateNode`,
    /// `map`, `remapIds`) — and requires the domain's `ReplaceChildren` not to move the keyed
    /// positions (the `keyedApplyLaws` witness laws). Handing it to the structural ops would let
    /// them append to, filter and permute keyed positions, which is exactly what Phase 189 kept them
    /// from; `Ops.applyContainedKeyed` locates through it and edits through `nodew`.
    let traversal (w: NodeWitness<'Node, 'Id>) (keyw: KeyedWitness<'Node, 'Id>) : NodeWitness<'Node, 'Id> =
        { Id = w.Id
          KindTag = w.KindTag
          Children = fun n -> keyw.KeyedChildren n @ w.Children n
          ReplaceChildren =
            fun n cs ->
                let keyedArity = List.length (keyw.KeyedChildren n)
                let keyed, structural = List.splitAt (min keyedArity (List.length cs)) cs
                keyw.ReplaceKeyedChildren (w.ReplaceChildren n structural) keyed }

    /// Preorder over the keyed walk: node, then its keyed children's subtrees, then its structural
    /// children's. Iterative, as `preorder` is (Phase 10) — it IS `preorder`, over `traversal`.
    let preorderKeyed (w: NodeWitness<'Node, 'Id>) (keyw: KeyedWitness<'Node, 'Id>) (root: 'Node) : 'Node list =
        preorder (traversal w keyw) root

    /// Every id the keyed walk reaches, in `preorderKeyed` order.
    let idsKeyed (w: NodeWitness<'Node, 'Id>) (keyw: KeyedWitness<'Node, 'Id>) (root: 'Node) : 'Id list =
        ids (traversal w keyw) root

    /// `fold` over the keyed walk.
    let foldKeyed
        (w: NodeWitness<'Node, 'Id>)
        (keyw: KeyedWitness<'Node, 'Id>)
        (f: 'State -> 'Node -> 'State)
        (state: 'State)
        (root: 'Node)
        : 'State =
        fold (traversal w keyw) f state root

    /// `wellFormed` over the keyed walk: id uniqueness over every id-bearing position the domain
    /// declares, and the first offender in `preorderKeyed` order. For a domain with no keyed
    /// position this is `wellFormed`.
    let wellFormedKeyed
        (w: NodeWitness<'Node, 'Id>)
        (keyw: KeyedWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (root: 'Node)
        : WellFormed<'Id> =
        wellFormed (traversal w keyw) idw root

    /// `graftWellFormed` over the keyed walk, on BOTH sides: an id the tree holds in a keyed
    /// position, and an id the graft carries into one, are each seen. This is the scan
    /// `Ops.applyContainedKeyed`'s `DuplicateId` refusal projects from.
    let graftWellFormedKeyed
        (w: NodeWitness<'Node, 'Id>)
        (keyw: KeyedWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (node: 'Node)
        (root: 'Node)
        : WellFormed<'Id> =
        graftWellFormed (traversal w keyw) idw node root

    // ---- build-once index (Phase 05) ----
    // The everyday navigators (`tryFind` / `parentOf` / `path` / `ancestors` / `depth`) each
    // run an independent `preorder`; across a batch of lookups that is O(n^2) (`ancestors`
    // alone is O(n*depth)). `NodeIndex` is a one-pass snapshot — `id -> node` and `id -> parent`
    // maps keyed by the `IdWitness` string form — over which the same navigators are
    // O(log n)/O(depth). It is a READ-ONLY cache: any structural edit invalidates it, so
    // rebuild after `Ops.apply`. Each `Index.*` lookup returns exactly what the matching
    // `Tree.*` combinator returns — the index is a cache, not a new semantics. The build-once /
    // read-per-call hazard (Phase 17): reusing an index after an edit silently returns stale
    // answers, so `build` stamps a `Fingerprint` over the (id, child-ids) preorder and
    // `Index.isFreshFor` lets a caller detect a stale index instead of trusting it blindly.

    /// A one-pass, read-only snapshot of a tree for O(log n) lookups (see above). Stale after any
    /// edit unless carried through `Index.rebind` / `Ops.Index.afterOp`; `Index.isFreshFor` detects
    /// a stale one.
    type NodeIndex<'Node, 'Id> =
        {
            /// Every node the preorder reaches, keyed by `IdWitness.ToString` of its id. For an id
            /// carried twice the LAST occurrence wins.
            ById: Map<string, 'Node>
            /// Each non-root node's key to its parent's id. The root has no entry, which is how
            /// `Index.path` knows it has reached the top.
            ParentOf: Map<string, 'Id>
            /// The root's id at `build`; `Index.rebind` keeps it, since no skeleton op replaces the
            /// root.
            Root: 'Id
            /// Staleness stamp captured at `build` — the sum, modulo 2^32, of one FNV-1a term per
            /// node over its id, kind, child count and ordered child-ids (Phase 317; until then one
            /// digest over the whole preorder), so any `InsertChild` / `RemoveNode` / `MoveNode` /
            /// `ReorderChildren` (or id-remap or kind change) since `build` changes it. A sum over
            /// nodes rather than a digest over a sequence so an edit RE-STAMPS in the nodes it
            /// touched (`Index.rebind`) instead of in the tree. A *detector*, not a guarantee: a
            /// collision is possible but vanishingly unlikely. Compare with `Index.isFreshFor`.
            Fingerprint: string
        }

    /// Building, checking and maintaining a `NodeIndex`. Each lookup answers exactly what the
    /// matching `Tree` combinator answers over the tree the index was built from — a cache, not a
    /// new semantics.
    module Index =

        // Arithmetic modulo 2^32, masked as `Hash.fnv1a` masks, so every host wraps alike.
        let private add32 (a: uint32) (b: uint32) : uint32 = (a + b) &&& 0xFFFFFFFFu

        let private sub32 (a: uint32) (b: uint32) : uint32 =
            (a + (0xFFFFFFFFu - b) + 1u) &&& 0xFFFFFFFFu

        let private stampOf (h: uint32) : string = h.ToString("x8")

        // A stamp (and an `fnv1a` digest) is x8: lowercase hex, eight digits. Read back digit by
        // digit — portable to every host, where a platform hex parser is not.
        let private unstamp (s: string) : uint32 =
            let mutable h = 0u

            for ch in s do
                let d =
                    if ch >= '0' && ch <= '9' then
                        uint32 ch - uint32 '0'
                    else
                        uint32 ch - uint32 'a' + 10u

                h <- ((h * 16u) + d) &&& 0xFFFFFFFFu

            h

        /// One node's fingerprint — its term in the staleness stamp (Phase 317; until then this name
        /// was the whole tree's digest, and the `canonicalFields` roster in `Hash.fs` lists it by
        /// it): the fields `id`, `kind`, its child COUNT and then
        /// each child id in order (capturing identity, kind, parent-child structure, and child
        /// order), through `Hash.canonicalFields` and the portable FNV-1a, read back as a 32-bit
        /// value. The record of every node, given unique ids, determines the tree — each node names
        /// its children — so a sum over those records sees every skeleton edit plus a kind change or
        /// id-remap, but NOT an opaque per-node payload mutation the witness has no accessor for
        /// (the index would still hand back a payload-stale node; rebuild after a domain value-edit
        /// too). The count keeps the fields unambiguous whatever an id contains (Phase 290).
        let private fingerprintOf (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (n: 'Node) : uint32 =
            let kids = w.Children n

            let digest =
                idw.ToString(w.Id n)
                :: w.KindTag n
                :: string (List.length kids)
                :: (kids |> List.map (fun c -> idw.ToString(w.Id c)))
                |> Hash.canonicalFields
                |> Hash.fnv1a

            unstamp digest

        /// The staleness stamp of a whole tree: the sum of every preorder node's term. Recomputed by
        /// `isFreshFor` and compared against the stamp `build` stored.
        let private treeStampOf (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (root: 'Node) : string =
            preorder w root
            |> List.fold (fun acc n -> add32 acc (fingerprintOf w idw n)) 0u
            |> stampOf

        /// Build both maps + the staleness stamp in a single preorder pass. Over a tree that is not
        /// well-formed (an id carried twice) `ById` and `ParentOf` keep the LAST occurrence, and
        /// the parent links can form a cycle; every read below terminates on such an index
        /// regardless (Phase 298), and `tryBuild` refuses the tree instead.
        let build (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (root: 'Node) : NodeIndex<'Node, 'Id> =
            let nodes = preorder w root

            { ById = nodes |> List.map (fun n -> idw.ToString(w.Id n), n) |> Map.ofList
              ParentOf =
                [ for p in nodes do
                      for c in w.Children p -> idw.ToString(w.Id c), w.Id p ]
                |> Map.ofList
              Root = w.Id root
              Fingerprint = treeStampOf w idw root }

        /// `build`, refusing a tree that is not well-formed (Phase 298): `Error (RepeatedId d)`
        /// naming the first id the preorder carries twice (`Tree.wellFormed`'s verdict), where
        /// `build` would index it with the earlier occurrences silently overwritten.
        let tryBuild
            (w: NodeWitness<'Node, 'Id>)
            (idw: IdWitness<'Id>)
            (root: 'Node)
            : Result<NodeIndex<'Node, 'Id>, WellFormed<'Id>> =
            match wellFormed w idw root with
            | Structural -> Ok(build w idw root)
            | repeated -> Error repeated

        /// The node carrying `target`, if present (O(log n)).
        let tryFind (idw: IdWitness<'Id>) (target: 'Id) (ix: NodeIndex<'Node, 'Id>) : 'Node option =
            Map.tryFind (idw.ToString target) ix.ById

        /// The parent node of `target` (None for the root or an absent id).
        let parentOf (idw: IdWitness<'Id>) (target: 'Id) (ix: NodeIndex<'Node, 'Id>) : 'Node option =
            Map.tryFind (idw.ToString target) ix.ParentOf
            |> Option.bind (fun pid -> Map.tryFind (idw.ToString pid) ix.ById)

        /// The absolute id-path root..target (inclusive), walking parent links (O(depth)).
        ///
        /// **Terminates on any index (Phase 298).** The walk carries the ids it has visited: over
        /// an index `build` made from a tree carrying an id twice, the parent links can form a
        /// cycle, and the walk that followed them looped forever. A cycle has no root to reach, so
        /// such a target's path is `None` — the same answer as an absent id.
        let path (idw: IdWitness<'Id>) (target: 'Id) (ix: NodeIndex<'Node, 'Id>) : 'Id list option =
            if not (Map.containsKey (idw.ToString target) ix.ById) then
                None
            else
                let rec up acc (visited: Set<string>) cur =
                    match Map.tryFind (idw.ToString cur) ix.ParentOf with
                    | Some p when Set.contains (idw.ToString p) visited -> None // a parent cycle
                    | Some p -> up (p :: acc) (Set.add (idw.ToString p) visited) p
                    | None -> Some acc // cur is the root

                up [ target ] (Set.singleton (idw.ToString target)) target

        /// The proper ancestors of `target`, root-first. Empty for the root / an absent id.
        let ancestors (idw: IdWitness<'Id>) (target: 'Id) (ix: NodeIndex<'Node, 'Id>) : 'Node list =
            match path idw target ix with
            | Some ids when not (List.isEmpty ids) ->
                ids
                |> List.rev
                |> List.tail
                |> List.rev
                |> List.choose (fun i -> Map.tryFind (idw.ToString i) ix.ById)
            | _ -> []

        /// The depth of `target` (root = 0). None if absent.
        let depth (idw: IdWitness<'Id>) (target: 'Id) (ix: NodeIndex<'Node, 'Id>) : int option =
            path idw target ix |> Option.map (fun ids -> List.length ids - 1)

        /// Is `ix` still valid for `root` (Phase 17)? Recomputes the staleness stamp over the
        /// current tree and compares it to the one captured at `build`. Returns `false` after any
        /// structural edit since the index was built (so a caller can `build` again instead of
        /// reading stale answers), `true` for an unedited tree. O(n) — an opt-in check, not on the
        /// per-lookup hot path. **A content-only `UpdateNode` is invisible to it** (Phase 305): the
        /// stamp reads id, kind and child ids, so a rewrite that keeps all three — the common
        /// content edit — leaves it `true` while `ById` holds the node as it was. `isFreshForWith
        /// encode`, over an index from `buildWith encode`, is the check that sees it.
        let isFreshFor
            (w: NodeWitness<'Node, 'Id>)
            (idw: IdWitness<'Id>)
            (root: 'Node)
            (ix: NodeIndex<'Node, 'Id>)
            : bool =
            treeStampOf w idw root = ix.Fingerprint

        // ---- the content-aware stamp (Phase 305) ----
        // `fingerprintOf` sees a node's id, kind and child ids and nothing else, so `isFreshFor`
        // reports an index fresh across an `UpdateNode` that changed only the node's content: the
        // index then hands back the node as it was before the rewrite. The witness has no content
        // accessor, so the one way to stamp content is the caller's encoder — the same per-call
        // parameter `Tree.encodeHash` takes, folded into each node's term beside its kind.

        /// One node's term in the content-aware stamp: `fingerprintOf`'s fields plus `encode n`,
        /// between the kind and the child count, so the stamp moves whenever the encoder's reading
        /// of the node moves.
        let private fingerprintOfWith
            (w: NodeWitness<'Node, 'Id>)
            (idw: IdWitness<'Id>)
            (encode: 'Node -> string)
            (n: 'Node)
            : uint32 =
            let kids = w.Children n

            let digest =
                idw.ToString(w.Id n)
                :: w.KindTag n
                :: encode n
                :: string (List.length kids)
                :: (kids |> List.map (fun c -> idw.ToString(w.Id c)))
                |> Hash.canonicalFields
                |> Hash.fnv1a

            unstamp digest

        let private treeStampOfWith
            (w: NodeWitness<'Node, 'Id>)
            (idw: IdWitness<'Id>)
            (encode: 'Node -> string)
            (root: 'Node)
            : string =
            preorder w root
            |> List.fold (fun acc n -> add32 acc (fingerprintOfWith w idw encode n)) 0u
            |> stampOf

        /// `build`, stamped with the caller's content encoder as well (Phase 305): the same two maps
        /// and root, and a `Fingerprint` that `isFreshForWith encode` moves for a content-only
        /// `UpdateNode` as well as for every skeleton edit. The pair is a pair: an index built here
        /// is checked with `isFreshForWith` under the SAME encoder (`isFreshFor` reads it as stale,
        /// since the plain stamp omits the content term), and `Ops.Index.afterOp` maintains the
        /// PLAIN stamp, so an index carried through it is a plain one — rebuild here after an edit
        /// instead. The encoder should be injective over a node's own content, as `encodeHash`'s
        /// must be: a reading two different contents share is a rewrite the stamp cannot see.
        let buildWith
            (w: NodeWitness<'Node, 'Id>)
            (idw: IdWitness<'Id>)
            (encode: 'Node -> string)
            (root: 'Node)
            : NodeIndex<'Node, 'Id> =
            { build w idw root with
                Fingerprint = treeStampOfWith w idw encode root }

        /// `isFreshFor` under the content encoder `buildWith` stamped with (Phase 305): `false` after
        /// any skeleton edit OR any `UpdateNode` whose new content the encoder reads differently,
        /// `true` for an unedited tree. O(n), like `isFreshFor`.
        let isFreshForWith
            (w: NodeWitness<'Node, 'Id>)
            (idw: IdWitness<'Id>)
            (encode: 'Node -> string)
            (root: 'Node)
            (ix: NodeIndex<'Node, 'Id>)
            : bool =
            treeStampOfWith w idw encode root = ix.Fingerprint

        /// Re-index through an edit confined to known nodes (Phase 317) — the primitive an index is
        /// MAINTAINED through rather than rebuilt (`Ops.Index.afterOp` is its caller for the
        /// skeleton ops). `leaving` are nodes of the INDEXED tree: each one's stamp term, its `ById`
        /// entry and the `ParentOf` entry of each of its children are withdrawn. `arriving` are
        /// nodes of the EDITED tree: each one's term, entry and child links are installed.
        /// Withdrawals first, then installs, so a node on both sides is replaced. The root id is
        /// kept (no skeleton op changes it). O(the two lists and their children), never O(tree).
        ///
        /// **The contract.** The result is `build w idw edited` when every node whose own record
        /// (id, kind, ordered child ids) or value differs between the two trees, or which is in only
        /// one of them, is in `leaving` if it was in the indexed tree and in `arriving` if it is in
        /// the edited one — each id at most once per side, ids unique in both trees. A node in
        /// neither keeps the value the index holds. The function cannot check the contract (that
        /// would cost the walk it exists to avoid); `afterOp` derives both lists from the op and
        /// verifies what it can.
        let rebind
            (w: NodeWitness<'Node, 'Id>)
            (idw: IdWitness<'Id>)
            (leaving: 'Node list)
            (arriving: 'Node list)
            (ix: NodeIndex<'Node, 'Id>)
            : NodeIndex<'Node, 'Id> =
            let key (n: 'Node) = idw.ToString(w.Id n)

            let withdrawn =
                leaving
                |> List.fold
                    (fun (byId: Map<string, 'Node>, parentOf: Map<string, 'Id>, stamp: uint32) n ->
                        Map.remove (key n) byId,
                        (parentOf, w.Children n) ||> List.fold (fun m c -> Map.remove (key c) m),
                        sub32 stamp (fingerprintOf w idw n))
                    (ix.ById, ix.ParentOf, unstamp ix.Fingerprint)

            let byId, parentOf, stamp =
                arriving
                |> List.fold
                    (fun (byId: Map<string, 'Node>, parentOf: Map<string, 'Id>, stamp: uint32) n ->
                        Map.add (key n) n byId,
                        (parentOf, w.Children n) ||> List.fold (fun m c -> Map.add (key c) (w.Id n) m),
                        add32 stamp (fingerprintOf w idw n))
                    withdrawn

            { ById = byId
              ParentOf = parentOf
              Root = ix.Root
              Fingerprint = stampOf stamp }

    // ---- content-aware hash (Phase 06) ----

    /// The UNHASHED content pre-image `encodeHash` digests: every preorder node's `encode` paired
    /// with its arity, through `Hash.canonicalFields`. Exposed (Phase 290) for the one caller that
    /// must not settle for a 32-bit digest — `Function.applyMemo` keys its cache on this string
    /// rather than on `encodeHash`, so a key hit is an equality of pre-images and a colliding FNV-1a
    /// can never serve the wrong tree. INJECTIVE over trees whenever `encode` is injective over a
    /// node's own content: the arity fold recovers the shape (`preorder_arity_injective`,
    /// `proofs/TreeOps.fst`) and the field encoding recovers the fields (`proofs/Query.fst`).
    let encodePreimage (w: NodeWitness<'Node, 'Id>) (encode: 'Node -> string) (node: 'Node) : string =
        preimageWith w encode node

    /// Content hash folding a caller-supplied per-node `encode` over the preorder — each node's
    /// encoding and its child count, through `Hash.canonicalFields` (`encodePreimage`) and the
    /// portable FNV-1a. Where `contentHash` fingerprints SHAPE only (kind tags), this
    /// fingerprints CONTENT: two trees that differ only in per-node payload hash differently.
    /// The encoder is a per-call parameter — no `Core.Wire` dependency, no equality seam (GP2).
    /// When `encode` is canonical, `encodeHash w encode a = encodeHash w encode b` is a domain's
    /// structural-equality test (the canonical-wire-equality pattern, generically). The field
    /// encoding keeps two adjacent encodings from running together into a colliding fold
    /// (`["ab";"c"]` and `["a";"bc"]` hash distinctly — pre-Phase-11 this fold used `""`, and until
    /// Phase 290 a bare separator an encoding could spell), and the arity keeps two trees with one
    /// preorder apart (until Phase 290 `root(a(a1,a2), b(b1))` and `root(a(a1,a2,b(b1)))` hashed
    /// equal under every encoder).
    /// **Precondition (memo soundness, Phase 56):** `encode` must be *injective* over a node's own
    /// content — a lossy `encode` makes distinct trees share a pre-image, so any caller that keys
    /// on this (or on `encodePreimage`) could then serve the wrong tree.
    /// `Conformance.encoderInjectivityLaws` certifies a given encoder is collision-free over a
    /// domain generator. That two distinct pre-images hash apart under FNV-1a is not claimed.
    let encodeHash (w: NodeWitness<'Node, 'Id>) (encode: 'Node -> string) (node: 'Node) : string =
        encodePreimage w encode node |> Hash.fnv1a

/// Deterministic fresh ids for a caller that must mint one (Phase 312): a derived or a sequential
/// id that a caller-supplied TAKEN set does not hold, and the repair of a tree that carries an id
/// twice. Every consumer that clones, pastes or repairs re-derived this derive-and-probe loop.
///
/// **Minting stays OFF the witness (D5, kept).** `IdWitness` carries no `fresh` and gains none:
/// these are helpers over `ToString` / `OfString` and a taken set the caller passes, so nothing
/// here reads ambient state, and the same inputs mint the same id on every host and every replay.
/// A taken set is a `Set<string>` of `IdWitness.ToString` keys — the form `Footprint`,
/// `Tree.Index` and `Tree.wellFormed` key ids by — so no `comparison` is demanded of `'Id`.
///
/// **A strategy is a function `'Id -> Set<string> -> 'Id`**: given the id being replaced and the
/// taken keys, an id whose key the set does not hold. `derived` and `sequential` are two such
/// functions over STRING-SHAPED ids; a domain whose ids are not strings with a suffix (a `Guid`,
/// an integer) writes its own of the same shape, hands it to `repairDuplicates` /
/// `TreePlacement.clone`, and certifies it with `Conformance.freshIdLaws`. Both shipped strategies
/// probe pairwise-distinct candidate strings, so each terminates within `Set.count taken + 1`
/// probes; each requires the witness's `OfString` to read a candidate back to an id whose
/// `ToString` is that candidate, which `freshIdLaws` checks rather than assumes.
[<RequireQualifiedAccess>]
module FreshIds =

    /// The first of `candidate 1`, `candidate 2`, … that `taken` does not hold. The candidates are
    /// pairwise distinct, so at most `Set.count taken + 1` probes run.
    let private firstFree (taken: Set<string>) (candidate: int -> string) : string =
        let rec probe (n: int) =
            let c = candidate n
            if Set.contains c taken then probe (n + 1) else c

        probe 1

    /// A deterministic derivation from the id being replaced: `<id>-copy`, then `<id>-copy-2`,
    /// `<id>-copy-3`, … — the first whose key `taken` does not hold. The spelling is the one the UI
    /// host's clone verbs have shipped, so a consumer that adopts this one keeps its ids.
    let derived (idw: IdWitness<'Id>) (id: 'Id) (taken: Set<string>) : 'Id =
        let s = idw.ToString id

        firstFree taken (fun n -> if n = 1 then s + "-copy" else s + "-copy-" + string n)
        |> idw.OfString

    /// Sequential ids under a fixed prefix: the first of `<prefix>-1`, `<prefix>-2`, … whose key
    /// `taken` does not hold. The id being replaced is not read, so the minted sequence depends only
    /// on the prefix and the taken set — the deterministic-replay strategy. There is NO hidden
    /// counter: a caller that adds each minted key to `taken` (as `repairDuplicates` does) gets
    /// `-1`, `-2`, `-3`, … in request order, and the same requests mint the same ids on replay.
    let sequential (idw: IdWitness<'Id>) (prefix: string) (_replaced: 'Id) (taken: Set<string>) : 'Id =
        firstFree taken (fun n -> prefix + "-" + string n) |> idw.OfString

    /// Rename every node of `root` whose id is already TAKEN — held by `taken`, or carried by an
    /// EARLIER node in preorder — to a fresh id from `mint`, and return the renamed tree with the
    /// mapping `(original, fresh)`, one entry per renamed node in preorder. The first occurrence of
    /// an id `taken` does not hold keeps it; every later occurrence is the one renamed.
    ///
    /// Two uses, one function. With `taken = Set.empty` it repairs a tree that carries an id twice
    /// (the later occurrence is renamed). With `taken` the keys of a TARGET tree it prepares a
    /// subtree for insertion there — every id that would collide is renamed, every other id is
    /// kept — which is what a clone or a paste needs before `InsertChild`.
    ///
    /// Each fresh id is minted against `taken`, every id `root` carries (so it cannot collide with
    /// an occurrence not yet visited) and every id minted before it. Given a lawful `mint` the
    /// result is `Tree.wellFormed` and holds no key of `taken` (`Conformance.freshIdLaws`). A tree
    /// with nothing to rename is returned as it was, with an empty mapping.
    ///
    /// `setId` rebuilds a node with a new id — a per-call parameter, as for `Tree.remapIds`, never a
    /// witness field. The rebuild is bottom-up through `ReplaceChildren`, iterative (a deep tree
    /// cannot overflow), and walks `w`'s children: a domain with keyed positions passes
    /// `Tree.traversal nodew keyw` to reach and rename the nodes held there too.
    let repairDuplicates
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (setId: 'Id -> 'Node -> 'Node)
        (mint: 'Id -> Set<string> -> 'Id)
        (taken: Set<string>)
        (root: 'Node)
        : 'Node * ('Id * 'Id) list =
        let nodes = Tree.preorder w root
        let carried = nodes |> List.map (fun n -> idw.ToString(w.Id n)) |> Set.ofList

        // One preorder pass decides, per preorder position, the fresh id (if any).
        let rec decide (i: int) (seen: Set<string>) (avoid: Set<string>) (renames: Map<int, 'Id>) mappingRev ns =
            match ns with
            | [] -> renames, List.rev mappingRev
            | n :: rest ->
                let id = w.Id n
                let k = idw.ToString id

                if Set.contains k taken || Set.contains k seen then
                    let fresh = mint id avoid

                    decide
                        (i + 1)
                        seen
                        (Set.add (idw.ToString fresh) avoid)
                        (Map.add i fresh renames)
                        ((id, fresh) :: mappingRev)
                        rest
                else
                    decide (i + 1) (Set.add k seen) avoid renames mappingRev rest

        let renames, mapping =
            decide 0 Set.empty (Set.union taken carried) Map.empty [] nodes

        if Map.isEmpty renames then
            root, []
        else
            // Bottom-up rebuild whose frames carry their node's PREORDER position: a frame is
            // created when its node is first reached, which is preorder, so `next` numbers the
            // nodes exactly as `decide` did. `(position, node, children-to-rebuild,
            // rebuilt-children-reversed)`; `carry` hands a finished child to its parent frame.
            let rec loop (next: int) (stack: (int * 'Node * 'Node list * 'Node list) list) (carry: 'Node option) =
                match stack with
                | [] -> root // unreachable: the root frame returns directly
                | (ix, node, remaining, doneRev) :: rest ->
                    match carry with
                    | Some child -> loop next ((ix, node, remaining, child :: doneRev) :: rest) None
                    | None ->
                        match remaining with
                        | [] ->
                            // a leaf is not rebuilt: a witness may leave `ReplaceChildren` partial
                            // on nodes that cannot hold children
                            let rebuilt =
                                if List.isEmpty doneRev then
                                    node
                                else
                                    w.ReplaceChildren node (List.rev doneRev)

                            let rebuilt =
                                match Map.tryFind ix renames with
                                | Some fresh -> setId fresh rebuilt
                                | None -> rebuilt

                            match rest with
                            | [] -> rebuilt
                            | _ -> loop next rest (Some rebuilt)
                        | c :: cs ->
                            loop (next + 1) ((next, c, w.Children c, []) :: (ix, node, cs, doneRev) :: rest) None

            loop 1 [ (0, root, w.Children root, []) ] None, mapping
