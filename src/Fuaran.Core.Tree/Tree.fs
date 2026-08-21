namespace Fuaran.Core

/// Witness over a domain's id representation. Identity is the one genuine
/// cross-domain axis (string ids in Doc/Calc, Guid ids in UI/Music — 2-2): it is
/// resolved here as a parameter, never a fixed type. The generic functions mint no
/// ids of their own (hygiene derives ids deterministically via `ToString`/`OfString`),
/// so this witness carries no effectful `fresh` — id minting stays domain-side.
/// FSharp.Core only, Fable-clean.
type IdWitness<'Id> =
    { ToString: 'Id -> string
      OfString: string -> 'Id
      Equals: 'Id -> 'Id -> bool }

/// Witness over a domain's node type. The core owns NO base node type — closed
/// exhaustive `NodeKind` DUs are load-bearing (a compiler-checked totality guarantee no
/// open base type can give) and stay sovereign per domain. The core sees a node only
/// through these four accessors:
///   - `Id`              — the node's permanent identity
///   - `KindTag`         — a string tag for the node's kind (for envelopes / addressing)
///   - `Children`        — the ordered child list (a finite walk — totality by construction)
///   - `ReplaceChildren` — rebuild a node with a new child list (the structural-edit seam)
type NodeWitness<'Node, 'Id> =
    { Id: 'Node -> 'Id
      KindTag: 'Node -> string
      Children: 'Node -> 'Node list
      ReplaceChildren: 'Node -> 'Node list -> 'Node }

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

    /// The node carrying `target`, if present.
    let tryFind (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (target: 'Id) (root: 'Node) : 'Node option =
        preorder w root |> List.tryFind (fun n -> idw.Equals (w.Id n) target)

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
    /// (Phase 19) so a deep tree cannot overflow — the shared engine behind `map` and
    /// `updateNode`. A frame is `(node, children-not-yet-rebuilt, rebuilt-children-reversed)`;
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

    /// Rebuild the tree, replacing the single node identified by `target` with `f` applied to
    /// it. Returns None if `target` is absent. Iterative (Phase 19, via `rebuildPostorder`) so a
    /// deep tree cannot overflow — this backs every `Ops.apply`. For a conformant witness
    /// (`ReplaceChildren` round-trips, per the witness laws) the result is identical to the prior
    /// recursive version: `f` sees the target node with its own children, every ancestor is
    /// rebuilt, every other subtree is structurally unchanged.
    let updateNode
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (f: 'Node -> 'Node)
        (root: 'Node)
        : 'Node option =
        let mutable hit = false

        let g (node: 'Node) =
            if idw.Equals (w.Id node) target then
                hit <- true
                f node
            else
                node

        let result = rebuildPostorder w g root
        if hit then Some result else None

    /// The content-hash fold separator — the shared `Hash.foldSep` (U+0001). `contentHash` always
    /// used it; Phase 11 applied it to `encodeHash` too (which previously folded with `""`, aliasing
    /// boundary-distinct trees). See `STABILITY.md`: `encodeHash` digests changed once; `contentHash`
    /// digests are unchanged.
    let private hashSep = Hash.foldSep

    /// Content hash of a node's structural shape (kind tags in preorder). A cheap,
    /// deterministic fingerprint for the bounded-escape `Custom`-region discipline.
    let contentHash (w: NodeWitness<'Node, 'Id>) (node: 'Node) : string =
        preorder w node |> List.map w.KindTag |> String.concat hashSep |> Hash.fnv1a

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

    type NodeIndex<'Node, 'Id> =
        {
            ById: Map<string, 'Node>
            ParentOf: Map<string, 'Id>
            Root: 'Id
            /// Staleness stamp captured at `build` — an FNV-1a digest over each node's id paired
            /// with its ordered child-ids, so any `InsertChild` / `RemoveNode` / `MoveNode` /
            /// `ReorderChildren` (or id-remap) since `build` changes it. A *detector*, not a
            /// guarantee: a digest collision is possible but vanishingly unlikely. Compare with
            /// `Index.isFreshFor`.
            Fingerprint: string
        }

    module Index =

        /// The staleness digest: each node as `id>kind>child,child,…` (capturing identity, kind,
        /// parent-child structure, and child order), folded through the portable FNV-1a. Covers
        /// everything the witness exposes — so it detects every skeleton edit plus a kind change or
        /// id-remap, but NOT an opaque per-node payload mutation the witness has no accessor for
        /// (the index would still hand back a payload-stale node; rebuild after a domain value-edit
        /// too). Recomputed by `isFreshFor` and compared against the stamp `build` stored.
        let private fingerprintOf (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (root: 'Node) : string =
            preorder w root
            |> List.map (fun n ->
                idw.ToString(w.Id n)
                + ">"
                + w.KindTag n
                + ">"
                + (w.Children n |> List.map (fun c -> idw.ToString(w.Id c)) |> String.concat ","))
            |> String.concat hashSep
            |> Hash.fnv1a

        /// Build both maps + the staleness stamp in a single preorder pass.
        let build (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (root: 'Node) : NodeIndex<'Node, 'Id> =
            let nodes = preorder w root

            { ById = nodes |> List.map (fun n -> idw.ToString(w.Id n), n) |> Map.ofList
              ParentOf =
                [ for p in nodes do
                      for c in w.Children p -> idw.ToString(w.Id c), w.Id p ]
                |> Map.ofList
              Root = w.Id root
              Fingerprint = fingerprintOf w idw root }

        /// The node carrying `target`, if present (O(log n)).
        let tryFind (idw: IdWitness<'Id>) (target: 'Id) (ix: NodeIndex<'Node, 'Id>) : 'Node option =
            Map.tryFind (idw.ToString target) ix.ById

        /// The parent node of `target` (None for the root or an absent id).
        let parentOf (idw: IdWitness<'Id>) (target: 'Id) (ix: NodeIndex<'Node, 'Id>) : 'Node option =
            Map.tryFind (idw.ToString target) ix.ParentOf
            |> Option.bind (fun pid -> Map.tryFind (idw.ToString pid) ix.ById)

        /// The absolute id-path root..target (inclusive), walking parent links (O(depth)).
        let path (idw: IdWitness<'Id>) (target: 'Id) (ix: NodeIndex<'Node, 'Id>) : 'Id list option =
            if not (Map.containsKey (idw.ToString target) ix.ById) then
                None
            else
                let rec up acc cur =
                    match Map.tryFind (idw.ToString cur) ix.ParentOf with
                    | Some p -> up (p :: acc) p
                    | None -> acc // cur is the root

                Some(up [ target ] target)

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
        /// per-lookup hot path.
        let isFreshFor
            (w: NodeWitness<'Node, 'Id>)
            (idw: IdWitness<'Id>)
            (root: 'Node)
            (ix: NodeIndex<'Node, 'Id>)
            : bool =
            fingerprintOf w idw root = ix.Fingerprint

    // ---- content-aware hash (Phase 06) ----

    /// Content hash folding a caller-supplied per-node `encode` over the preorder, through the
    /// portable FNV-1a. Where `contentHash` fingerprints SHAPE only (kind tags), this
    /// fingerprints CONTENT: two trees that differ only in per-node payload hash differently.
    /// The encoder is a per-call parameter — no `Core.Wire` dependency, no equality seam (GP2).
    /// When `encode` is canonical, `encodeHash w encode a = encodeHash w encode b` is a domain's
    /// structural-equality test (the canonical-wire-equality pattern, generically). The `hashSep`
    /// (U+0001) separator keeps two adjacent encodings from running together into a colliding fold
    /// (`["ab";"c"]` and `["a";"bc"]` hash distinctly) — pre-Phase-11 this fold used `""`.
    /// **Precondition (memo soundness, Phase 56):** `encode` must be *injective* over the node space —
    /// a lossy `encode` makes distinct trees share a hash, so any caller that keys on `encodeHash`
    /// (e.g. `Function.applyMemo`) could then serve the wrong tree. `Conformance.encoderInjectivityLaws`
    /// certifies a given encoder is collision-free over a domain generator.
    let encodeHash (w: NodeWitness<'Node, 'Id>) (encode: 'Node -> string) (node: 'Node) : string =
        preorder w node |> List.map encode |> String.concat hashSep |> Hash.fnv1a
