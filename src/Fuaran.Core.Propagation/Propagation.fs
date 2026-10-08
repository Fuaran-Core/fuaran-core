namespace Fuaran.Core

/// Change propagation over a reference DAG + tree-level dirty recomputation (Phase 68) — the tree-level
/// incremental-recomputation surface. (It was written as the third of a trio beside a columnar and a
/// capability-DAG `evalFrom`; those left this repository with the compute strand under DECISIONS D66,
/// and this one stays: it is witness-generic tree machinery, not compute.) A domain models its artefact
/// as a typed tree whose cross-node references are **declared bindings on permanent ids** (a Calc model, a
/// notebook, an Office cross-domain refresh); given the dependency structure this module derives the
/// dependency order, enumerates any reference cycle (as data, GP4 — never a divergence), and computes the
/// **minimal dirty set** from a change — so a consumer re-evaluates only downstream-of-change.
///
/// Core owns NO evaluator (GP6): it computes *what is stale*, the domain recomputes it. The dependency
/// relation is a **per-call function argument** (`readsOf : 'Node -> 'Id seq`), never a witness field (GP2) —
/// the same capability-as-argument posture as `Tree.remapIds`' `setId`. Ids are keyed by their `IdWitness`
/// string form (the `Tree.NodeIndex` precedent), so this stays generic over any `'Id` without a `comparison`
/// constraint. FSharp.Core only, Fable-clean.
///
/// (Named `Propagation` — the headline job — rather than `Topology`, which would collide with the
/// geometric/world topology a geometry or worldbuilding domain would mean by the word. Topological *sort* is one function
/// here, `sort`; it is not what the package is about.)
module Propagation =

    /// The derived evaluation order + any reference cycles — the `Fuaran.Calc` `TopoResult` shape, string-id
    /// keyed. `Order` lists ids dependencies-first (a valid pull-eval order); `Cycles` enumerates each
    /// strongly-connected group of >1 node, or a self-referential node — a circular reference is *data*
    /// (GP4), never a divergence.
    type TopoResult =
        {
            /// The acyclic ids, each once, every one after each acyclic id it reads; ids in a cycle
            /// and dangling reads never appear here.
            Order: string list
            /// One list per cyclic group, the groups in dependencies-first order; the order of ids
            /// within a group is Tarjan's stack order, not sorted.
            Cycles: string list list
        }

    /// Build the dependency map of a tree: each node's string id → the set of string ids it references
    /// (via `readsOf`). Folds `readsOf` over `Tree.preorder`; every node appears (a leaf maps to the empty
    /// set). A referenced id that is absent from the tree is **retained** (it surfaces downstream as a
    /// dangling reference — the validator's concern, not dropped here). Pure, total.
    let dependencyMap
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (readsOf: 'Node -> 'Id seq)
        (root: 'Node)
        : Map<string, Set<string>> =
        Tree.preorder w root
        |> List.map (fun n -> idw.ToString(w.Id n), (readsOf n |> Seq.map idw.ToString |> Set.ofSeq))
        |> Map.ofList

    // Tarjan strongly-connected components over the reference sub-graph — SCCs emit in reverse-topological
    // order (dependencies-first), exactly evaluation order (the Fuaran.Calc `Topology.tarjan`, string-id
    // keyed). ITERATIVE (Phase 298, the Phase-19 walker posture): the recursion is an explicit frame stack,
    // each frame a node and the successors it has not yet visited, so a dependency chain as long as a
    // spreadsheet's row count cannot overflow the thread stack (the recursive form died near 3,500 deep
    // in a Debug test host and 13,700 in Release). The frames replay the recursive visit exactly — the
    // successors in `succ` order, a child's low-link folded into its parent's when the child's frame
    // finishes, a component emitted when its root's frame does — so the SCCs and their order are the
    // recursive version's, item for item.
    let private tarjan (nodes: string list) (succ: string -> string list) : string list list =
        let mutable index = 0
        let idx = System.Collections.Generic.Dictionary<string, int>()
        let low = System.Collections.Generic.Dictionary<string, int>()
        let onStack = System.Collections.Generic.HashSet<string>()
        let stack = System.Collections.Generic.Stack<string>()
        let sccs = ResizeArray<string list>()
        // the call stack: each frame's node, and the successors that node has still to visit
        let frames = System.Collections.Generic.Stack<string>()
        let pending = System.Collections.Generic.Dictionary<string, string list>()

        let enter v =
            idx[v] <- index
            low[v] <- index
            index <- index + 1
            stack.Push v
            onStack.Add v |> ignore
            pending[v] <- succ v
            frames.Push v

        for n in nodes do
            if not (idx.ContainsKey n) then
                enter n

                while frames.Count > 0 do
                    let v = frames.Peek()

                    match pending[v] with
                    | w :: rest ->
                        pending[v] <- rest

                        if not (idx.ContainsKey w) then
                            enter w
                        elif onStack.Contains w then
                            low[v] <- min low[v] idx[w]
                    | [] ->
                        frames.Pop() |> ignore
                        pending.Remove v |> ignore

                        if low[v] = idx[v] then
                            let comp = ResizeArray<string>()
                            let mutable popped = false

                            while not popped do
                                let w = stack.Pop()
                                onStack.Remove w |> ignore
                                comp.Add w

                                if w = v then
                                    popped <- true

                            sccs.Add(List.ofSeq comp)

                        // the return to the caller's frame: fold the child's low-link into it
                        if frames.Count > 0 then
                            let parent = frames.Peek()
                            low[parent] <- min low[parent] low[v]

        List.ofSeq sccs

    /// Derive the evaluation order of a dependency map + report circular groups as data. Edges to ids not in
    /// the map (dangling references) are ignored for ordering (they are validator defects, not cycles). A
    /// singleton SCC with no self-edge is an `Order` entry; a self-referential singleton or any multi-node
    /// SCC is a `Cycle`. Total — never diverges on a cycle (GP4).
    let sort (deps: Map<string, Set<string>>) : TopoResult =
        let nodes = deps |> Map.toList |> List.map fst

        let succ id =
            match Map.tryFind id deps with
            | Some ds -> ds |> Set.toList |> List.filter (fun d -> Map.containsKey d deps)
            | None -> []

        let sccs = tarjan nodes succ

        let order =
            sccs
            |> List.choose (fun comp ->
                match comp with
                | [ single ] when not (List.contains single (succ single)) -> Some single
                | _ -> None)

        let cycles =
            sccs
            |> List.filter (fun comp ->
                match comp with
                | [ single ] -> List.contains single (succ single)
                | _ -> true)

        { Order = order; Cycles = cycles }

    /// The CERTIFICATE `sort`'s result is checked against (Phase 308): `true` exactly when `topo` is a
    /// valid evaluation order of `deps` —
    ///
    ///   1. `Order` holds no id twice;
    ///   2. `Order` and the members of `Cycles` together PARTITION the ids `deps` holds — every id once,
    ///      in one of them, and nothing else;
    ///   3. every read of an `Order` id that `deps` holds appears EARLIER in `Order`, or lies in a cycle.
    ///
    /// The first clause is the one premise the agreement theorems take of the walked order
    /// (`valid_topo_distinct`, `proofs/Propagation.fst`), and the third is what makes the values the walk
    /// computes mean something: an acyclic read is computed before its reader. It is a CHECKER, not a
    /// second sort: it says whether an order is acceptable and never builds one, so it is the half of
    /// `sort` the proofs can state. `sort` itself runs Tarjan's algorithm and is held to this checker
    /// by the propagation laws and the proof differential on every graph they draw; a caller that
    /// builds a `Plan` by hand can hold its own order to it. Linear in ids plus reads, up to the set
    /// lookups. Pure, total.
    let validTopo (deps: Map<string, Set<string>>) (topo: TopoResult) : bool =
        let cyclic = topo.Cycles |> List.concat
        let all = topo.Order @ cyclic
        let distinct = List.length (List.distinct all) = List.length all
        let keys = deps |> Map.toList |> List.map fst |> Set.ofList

        if not distinct || Set.ofList all <> keys then
            false
        else
            let inCycle = Set.ofList cyclic

            let rec ordered (seen: Set<string>) =
                function
                | [] -> true
                | id :: rest ->
                    let reads = Map.tryFind id deps |> Option.defaultValue Set.empty

                    reads
                    |> Set.forall (fun r -> not (Set.contains r keys) || Set.contains r seen || Set.contains r inCycle)
                    && ordered (Set.add id seen) rest

            ordered Set.empty topo.Order

    /// The cycle group through `target`, if any — the enumeration a rejection envelope carries (GP5: a cycle
    /// rejection enumerates the cycle path).
    let cycleThrough (target: string) (deps: Map<string, Set<string>>) : string list option =
        (sort deps).Cycles |> List.tryFind (List.contains target)

    /// Invert a dependency map into its dependents map: `id → the ids that reference it`. The reverse edges
    /// dirty propagation walks.
    let dependents (deps: Map<string, Set<string>>) : Map<string, Set<string>> =
        [ for KeyValue(node, reads) in deps do
              for r in reads -> r, node ]
        |> List.groupBy fst
        |> List.map (fun (k, vs) -> k, vs |> List.map snd |> Set.ofList)
        |> Map.ofList

    /// The reachability closure of `seed` over an edge map: `seed` ∪ every id reachable from it by
    /// following `edges` (an id with no entry has no out-edges). The one frontier loop behind both
    /// directions of propagation (Phase 317): over the DEPENDENTS map it is the dirty set (push), over
    /// the dependency map itself it is the needed set (pull). `grow` in `proofs/Propagation.fst`.
    let private closureOver (edges: Map<string, Set<string>>) (seed: Set<string>) : Set<string> =
        let rec grow (frontier: Set<string>) (acc: Set<string>) =
            if Set.isEmpty frontier then
                acc
            else
                let next =
                    (Set.empty, frontier)
                    ||> Set.fold (fun s node ->
                        match Map.tryFind node edges with
                        | Some ds -> Set.union s ds
                        | None -> s)

                let fresh = Set.difference next acc
                grow fresh (Set.union acc fresh)

        grow seed seed

    /// The **minimal dirty set** for a changed-input set: `changed` ∪ every id transitively downstream of it
    /// (the reverse-reachability closure over the dependents map). Minimal by construction — an id not
    /// reverse-reachable from any change is never included. This is the primary entry point: a *value* edit
    /// (a formula / cell-body change) names the changed ids directly. Pure, total.
    let dirtyFromChangedIds (deps: Map<string, Set<string>>) (changed: Set<string>) : Set<string> =
        closureOver (dependents deps) changed

    /// The **needed set** for a target set (Phase 317) — the pull dual of `dirtyFromChangedIds`:
    /// `targets` ∪ every id transitively UPSTREAM of one (the reachability closure over the dependency
    /// map itself, where the dirty set closes over its inverse). It is the ⊆-least set that holds the
    /// targets and is closed under "is read by a member" — every read of a member is a member — so it
    /// is exactly what evaluating the targets requires (`needed_for_least`, `proofs/Propagation.fst`).
    /// A dangling read, and a target the map does not hold, are members like any other id: the set is
    /// about ids, and whether an id is evaluable is the map's question, not this one. Pure, total.
    let neededFor (deps: Map<string, Set<string>>) (targets: Set<string>) : Set<string> = closureOver deps targets

    /// The string ids a structural `SkeletonOp` touches — the *container* ids whose structure changed
    /// (over-approximation always safe, the Phase-34 contract). Because bindings are id-addressed, a
    /// move/reorder does not change any *referenced value*, so only the structurally-involved containers are
    /// touched; a `RemoveNode` touches the whole removed subtree (its former dependents dangle — surfaced as
    /// dirty by the closure, named as a defect by the validator, not here). Resolves subtrees against `root`
    /// (an op carries only ids, not nodes).
    ///
    /// **Every container whose child list moved (Phase 308).** A `MoveNode` touches the target, the new
    /// parent AND the parent it left; a `RemoveNode` touches the removed subtree AND the parent it was
    /// removed from — the containers whose child list changed, as the `InsertChild` and
    /// `ReorderChildren` arms always named theirs. Before, a node whose value counts its children read a
    /// stale count after a move out of it or a removal from it. And a `Batch` resolves each sub-op
    /// against the tree the sub-ops BEFORE it produced (`Ops.apply`, threaded), because that is the tree
    /// the sub-op is applied to: resolved against `root`, a move followed by the removal of the moved
    /// node's new parent touched neither the moved subtree nor its readers. A sub-op `Ops.apply` refuses
    /// (a domain's own engine may accept what the unconstrained one does not, below a keyed position for
    /// instance) is resolved against the tree in hand and the threading continues from it.
    let rec touchedBy
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (root: 'Node)
        (op: SkeletonOp<'Node, 'Id>)
        : Set<string> =
        let s = idw.ToString

        let idsOf (node: 'Node) =
            Tree.ids w node |> List.map s |> Set.ofList

        let subtreeIds (tid: 'Id) =
            match Tree.subtree w idw tid root with
            | Some node -> idsOf node
            | None -> Set.singleton (s tid)

        let parentIds (tid: 'Id) =
            match Tree.parentOf w idw tid root with
            | Some p -> Set.singleton (s (w.Id p))
            | None -> Set.empty

        match op with
        | InsertChild(parent, node) -> Set.add (s parent) (idsOf node)
        | RemoveNode target -> Set.union (subtreeIds target) (parentIds target)
        | MoveNode(target, newParent) -> Set.union (Set.ofList [ s target; s newParent ]) (parentIds target)
        | ReorderChildren(parent, _) -> Set.singleton (s parent)
        | Batch ops ->
            let step (acc: Set<string>, tree: 'Node) (o: SkeletonOp<'Node, 'Id>) =
                let next =
                    match Ops.apply w idw o tree with
                    | Ok tree' -> tree'
                    | Error _ -> tree

                Set.union acc (touchedBy w idw tree o), next

            ((Set.empty, root), ops) ||> List.fold step |> fst
        // Phase 250 — the node ALONE. An in-place rewrite keeps the node's id and its children, so
        // no container's structure moved: what changed is the one definition, and its readers are
        // reached by the closure. Before `UpdateNode`, a redefinition was a remove plus an insert,
        // which touched the parent as well and moved the node to the end of its siblings.
        | UpdateNode node -> Set.singleton (s (w.Id node))

    /// The dirty set induced by a structural `SkeletonOp`: `touchedBy` ∪ their transitive dependents. The
    /// dependency map is computed over the **pre-edit** `root` so a removed node's now-dangling dependents are
    /// captured (they still reference the removed id in the pre-edit graph). Convenience over the two
    /// primitives.
    let dirtyFromOp
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (root: 'Node)
        (readsOf: 'Node -> 'Id seq)
        (op: SkeletonOp<'Node, 'Id>)
        : Set<string> =
        let deps = dependencyMap w idw readsOf root
        dirtyFromChangedIds deps (touchedBy w idw root op)

    /// The change set to hand `evalFrom` over the POST-edit graph after a structural `SkeletonOp`
    /// (Phase 250): a dirty closure over the PRE-edit tree's dependency graph, restricted to the ids
    /// the post-edit tree still holds — seeded since Phase 308 from the diff of the two trees (below),
    /// where it was seeded from `touchedBy` alone.
    ///
    /// Both halves are load-bearing, and each is the obvious thing to get wrong. The dirty set must
    /// be computed over `pre`, because a removed node's dependents are reachable from it only in the
    /// graph that still holds it — over `post` they no longer read anything that exists, and a change
    /// set built there leaves them stale. And the removed ids must then be dropped, because `evalFrom`
    /// over the post-edit dependency map refuses an id that map does not hold (`EvalUnknownChange`,
    /// the proved `evalfrom_unknown_refused`) — which is the refusal that catches a domain's
    /// mistyped change set, and so is kept rather than relaxed. Restricting `touchedBy` to the
    /// surviving ids instead is accepted by `evalFrom` and is WRONG: it loses exactly the dependents
    /// of a removed node.
    ///
    /// The result names every surviving node whose value the edit can move, so it is an honest
    /// `changed` for `evalFrom`: over-approximating (a closure handed in as a change set closes to
    /// itself), never missing a reader. `post` must be the tree `op` produced from `pre`.
    ///
    /// **Derived from the DIFF of `pre` and `post` (Phase 308, DECISIONS D108).** The seeds are
    ///
    ///   - every id of `post` that `pre` does not hold (inserted);
    ///   - every id of `post` whose CHILD LIST (the ids `w.Children` reports, in order) differs from its
    ///     child list in `pre` — the old parent of a move, the parent of a removal, a reordered or
    ///     grown container;
    ///   - every id of `post` whose READ SET (`readsOf`) differs from its read set in `pre` — a node
    ///     whose reads are derived from its subtree reads differently when something below it moved;
    ///   - every id the op CONTENT-WRITES (`Ops.footprint`'s `ContentWrites`: an inserted subtree, an
    ///     `UpdateNode` target, a removed or moved target), which is how a node's own content can
    ///     differ — the witness has no content accessor, and `Ops.apply` rewrites content nowhere else;
    ///   - every id `pre` holds and `post` does not (removed), so that the closure reaches its readers;
    ///   - and `touchedBy` over `pre`, so the set never shrinks below what it was;
    ///
    /// closed over the PRE-edit dependency graph and restricted to the survivors. A `Batch` needs no
    /// special case: the diff compares the two ends, so every intermediate tree is accounted for by
    /// what it left behind — a node a batch moves under a parent it then removes is simply absent from
    /// `post`, and its readers are reached through it.
    ///
    /// The completeness this buys is a theorem (`changed_for_op_complete`, `proofs/PropagationOps.fst`):
    /// for an op `Ops.apply` accepts, every survivor whose content, child list or reads differ, and every
    /// survivor that reads a removed id, is named. So `agree_off` / `touches_off` hold for any evaluator
    /// that is a function of a node's own content, its child ids and its resolved declared reads — an
    /// evaluator that reads a node's whole SUBTREE without declaring it is outside that class, and is
    /// what declared reads are for.
    let changedForOp
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (readsOf: 'Node -> 'Id seq)
        (pre: 'Node)
        (post: 'Node)
        (op: SkeletonOp<'Node, 'Id>)
        : Set<string> =
        let s = idw.ToString

        let index (root: 'Node) =
            Tree.preorder w root |> List.map (fun n -> s (w.Id n), n) |> Map.ofList

        let before = index pre
        let after = index post

        let kids (n: 'Node) =
            w.Children n |> List.map (fun c -> s (w.Id c))

        let reads (n: 'Node) = readsOf n |> Seq.map s |> Set.ofSeq
        let written = (Ops.footprint w idw [ op ]).ContentWrites

        let local =
            after
            |> Map.toList
            |> List.choose (fun (k, n) ->
                match Map.tryFind k before with
                | None -> Some k
                | Some m when Set.contains k written || kids m <> kids n || reads m <> reads n -> Some k
                | Some _ -> None)
            |> Set.ofList

        let removed =
            before
            |> Map.toList
            |> List.map fst
            |> List.filter (fun k -> not (Map.containsKey k after))

        let seeds = Set.unionMany [ local; Set.ofList removed; touchedBy w idw pre op ]

        let deps = dependencyMap w idw readsOf pre
        let survivors = after |> Map.toList |> List.map fst |> Set.ofList
        Set.intersect (dirtyFromChangedIds deps seeds) survivors

    // ---- column-granular reads (Phase 250) ----
    // A read of a node may name the PARTS of that node's value it depends on — the columns of a table,
    // the fields of a record — so an edit that moves only some parts of a node dirties only the readers
    // of those parts. Core names no part vocabulary: a part is a string the domain chooses, and which
    // parts an edit moved is a function the CALLER supplies (a columnar domain derives it from its op,
    // e.g. `ColumnOps.changedColumns`). The node-granular map the driver walks is a projection of this
    // one, so the two can never disagree about which nodes read which.

    /// One declared read — `Read` is the id read — narrowed to the parts of that node's value the
    /// reader depends on. `Parts = None` reads the whole value — the node-granular read every existing `readsOf` makes.
    type PartRead<'Id> =
        {
            /// The node read; a read of an id absent from the tree is retained as a dangling read.
            Read: 'Id
            /// The parts of the read value the reader depends on — `None` for the whole value. Matched against the moved
            /// parts on the first hop only; when those are unknown (`None`) every declaration meets them.
            Parts: Set<string> option
        }

    /// Build the part-granular dependency map of a tree (Phase 250): each node's string id → each id it
    /// reads → the parts it reads there (`None` = the whole value). Two reads of one node merge: their
    /// parts unite, and a whole-value read absorbs any narrowed one. Like `dependencyMap`, every node
    /// appears and a dangling read is retained. Pure, total.
    let partDependencyMap
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (readsOf: 'Node -> PartRead<'Id> seq)
        (root: 'Node)
        : Map<string, Map<string, Set<string> option>> =
        let merge (a: Set<string> option) (b: Set<string> option) =
            match a, b with
            | Some x, Some y -> Some(Set.union x y)
            | _ -> None

        Tree.preorder w root
        |> List.map (fun n ->
            let reads =
                (Map.empty, readsOf n)
                ||> Seq.fold (fun acc r ->
                    let k = idw.ToString r.Read

                    match Map.tryFind k acc with
                    | Some prev -> Map.add k (merge prev r.Parts) acc
                    | None -> Map.add k r.Parts acc)

            idw.ToString(w.Id n), reads)
        |> Map.ofList

    /// The node-granular projection of a part-granular map — exactly `dependencyMap` of the same
    /// `readsOf` with its parts dropped. This is what `sort`, `eval`, `evalFrom` and their `With`
    /// forms walk.
    let nodeDependencies (partDeps: Map<string, Map<string, Set<string> option>>) : Map<string, Set<string>> =
        partDeps
        |> Map.map (fun _ reads -> reads |> Map.toList |> List.map fst |> Set.ofList)

    /// The dirty set for a change that moved only SOME parts of the changed nodes (Phase 250):
    /// `changed` ∪ every reader of a changed node whose declared parts meet the parts that moved
    /// (`changedParts id`; `None` = unknown or all, which every reader meets) ∪ everything downstream
    /// of those readers.
    ///
    /// **Only the FIRST hop is narrowed, and that is a statement about what is known, not a
    /// shortcut.** Which parts of a changed node moved is the caller's knowledge (a columnar op says
    /// which column it wrote). Which parts of a RECOMPUTED node's value move is not known until it is
    /// recomputed, so from the second hop on every reader is dirty, exactly as in
    /// `dirtyFromChangedIds` — to which this is equal when `changedParts` answers `None` everywhere or
    /// every read is whole-value.
    ///
    /// **Sound under part-faithful reads, which is the domain's obligation.** A reader left clean is
    /// one whose answer the edit cannot move PROVIDED it depends on no part of the read value it did
    /// not declare. Core cannot check that — it does not see inside a value — so a domain that
    /// declares `Parts` makes that promise for each one. The proved driver (`evalFrom`, theorem
    /// `evalfrom_agrees`) still recomputes node-granularly; this set is what an edit CAN reach at
    /// part granularity, for scheduling, reporting and measurement. Pure, total.
    let dirtyFromChangedParts
        (partDeps: Map<string, Map<string, Set<string> option>>)
        (changedParts: string -> Set<string> option)
        (changed: Set<string>)
        : Set<string> =
        let meets (declared: Set<string> option) (moved: Set<string> option) =
            match declared, moved with
            | Some d, Some m -> not (Set.isEmpty (Set.intersect d m))
            | _ -> true

        let firstHop =
            [ for KeyValue(reader, reads) in partDeps do
                  for KeyValue(read, parts) in reads do
                      if Set.contains read changed && meets parts (changedParts read) then
                          reader ]
            |> Set.ofList

        let deps = nodeDependencies partDeps
        Set.union changed (dirtyFromChangedIds deps firstHop)

    // ---- incremental recompute driver (Phase 69) ----
    // The tree-level incremental driver (its columnar and capability-DAG siblings left with the compute
    // strand, DECISIONS D66). Core owns the *order + reuse plumbing* — it walks the acyclic nodes in
    // dependency order and threads the value map; the domain injects `evalNode` (the evaluator, GP6 — no
    // compute in Core). `evalFrom` recomputes only the dirty subgraph and reuses each clean node's prior
    // value, byte-identical to a full `eval`. Cyclic SCCs are returned as data (the `#CALC!` posture);
    // iterating a cyclic group to a fixed point is a domain policy over `EvalOutcome.Cyclic`.

    /// Why incremental evaluation failed — named, enumerated (GP5), never a throw (GP4). A cyclic reference
    /// is **not** a failure — it is data in `EvalOutcome.Cyclic`.
    ///
    /// `EvalUndeclaredRead` (Phase 209) is declared LAST because a case's declaration order IS its tag
    /// number: appending keeps every existing tag where a consumer's serialised or cached form already has
    /// it. It names the node that read and the first id it read outside `deps[node]`.
    ///
    /// `RequireQualifiedAccess` (Phase 298): `EvalNodeFailed` is also a case of `Function`'s pipeline
    /// error, with the same payload, so a consumer opening both met one name for two cases — write
    /// `PropagationError.EvalNodeFailed`.
    [<RequireQualifiedAccess>]
    type PropagationError =
        /// Some `changed` ids are not keys of the dependency map; `ids` lists every such id, in
        /// ascending order. Raised before any node is evaluated.
        | EvalUnknownChange of ids: string list
        /// The domain evaluator returned `Error message` at `node`; the walk stops at the first
        /// failure in dependency order, so later nodes are not evaluated.
        | EvalNodeFailed of node: string * message: string
        /// The evaluator at `node` asked the resolver for `read`, an id outside `deps[node]`; only
        /// the first such read is named, and it outranks an `Error` the evaluator returned.
        | EvalUndeclaredRead of node: string * read: string

    /// The outcome of a (re)evaluation: the acyclic nodes' values, plus the cyclic SCCs that could not be
    /// ordered (the `#CALC!` set — a caller renders them as cycle errors, or re-runs them under an
    /// iteration policy of its own). A node downstream of a cycle evaluates with its cyclic read resolving to
    /// `None` — the domain's `evalNode` decides how to propagate that (Calc's `#CALC!` propagation).
    type EvalOutcome<'v> =
        {
            /// One value per evaluated acyclic node — recomputed or reused from the prior. No cyclic
            /// node and no dangling id has an entry; under `evalFor` only the needed nodes do.
            Values: Map<string, 'v>
            /// The cyclic groups, as `TopoResult.Cycles` reports them, never evaluated; under
            /// `evalFor` only the groups that meet the needed set.
            Cyclic: string list list
        }

    /// Shared walk: evaluate the acyclic nodes in dependency order, threading the results; recompute a node
    /// when `recompute id` (or it is absent from `prior`), otherwise reuse its `prior` value. `evalNode
    /// resolve id` computes node `id`, reading already-computed upstreams via `resolve`.
    ///
    /// **The resolver answers for `deps[id]` and for nothing else (Phase 209).** A declared read resolves
    /// exactly as before — a value once computed, `None` for a cyclic, dangling or not-yet-reached read. A
    /// read OUTSIDE the declaration is not answered and is not `None`-as-data: `None` already means
    /// "declared, and absent or failed upstream", which a domain propagates as a value of its own (Calc's
    /// `#CALC!`), so conflating the two would turn a contract violation into a plausible-looking blank. The
    /// walk records the first such read, discards whatever the evaluator went on to return, and ends the
    /// evaluation with `EvalUndeclaredRead`. The undeclared read wins over an `Error` the evaluator itself
    /// returned: a failure computed from a read that answered nothing is downstream of the violation, and
    /// naming the violation is what a domain can act on.
    ///
    /// Cyclic SCCs are surfaced, never evaluated — so a node in a cycle is never a violator here.
    ///
    /// **The node's prior value rides beside its reads (Phase 250).** A recomputed node is handed
    /// `Map.tryFind id prior` — its own value from the evaluation that produced `prior`, or `None`
    /// when there is none. `eval` / `evalFrom` hand an evaluator that ignores it; `evalWith` /
    /// `evalFromWith` hand the domain's own.
    ///
    /// **The order is handed in (Phase 317)** — `walkWith` hands `sort deps`, and the demand-driven
    /// `evalForWith` hands that same order restricted to the needed set, so the two walk one order.
    let private walkTopo
        (evalNode: (string -> 'v option) -> 'v option -> string -> Result<'v, string>)
        (recompute: string -> bool)
        (prior: Map<string, 'v>)
        (deps: Map<string, Set<string>>)
        (topo: TopoResult)
        : Result<EvalOutcome<'v>, PropagationError> =
        let rec go (results: Map<string, 'v>) =
            function
            | [] ->
                Ok
                    { Values = results
                      Cyclic = topo.Cycles }
            | id :: rest ->
                if recompute id || not (Map.containsKey id prior) then
                    let declared =
                        match Map.tryFind id deps with
                        | Some reads -> reads
                        | None -> Set.empty

                    // The FIRST id read outside `declared`, if any. A cell rather than an accumulated list:
                    // one violation is what a domain fixes, and the read that follows it may only exist
                    // because the first answered nothing.
                    let undeclared = ref None

                    let resolve k =
                        if Set.contains k declared then
                            Map.tryFind k results
                        else
                            if Option.isNone undeclared.Value then
                                undeclared.Value <- Some k

                            None

                    let computed = evalNode resolve (Map.tryFind id prior) id

                    match undeclared.Value with
                    | Some r -> Error(PropagationError.EvalUndeclaredRead(id, r))
                    | None ->
                        match computed with
                        | Ok v -> go (Map.add id v results) rest
                        | Error m -> Error(PropagationError.EvalNodeFailed(id, m))
                else
                    go (Map.add id (Map.find id prior) results) rest

        go Map.empty topo.Order

    /// The walk over `sort deps` — the full evaluators' order.
    let private walkWith
        (evalNode: (string -> 'v option) -> 'v option -> string -> Result<'v, string>)
        (recompute: string -> bool)
        (prior: Map<string, 'v>)
        (deps: Map<string, Set<string>>)
        : Result<EvalOutcome<'v>, PropagationError> =
        walkTopo evalNode recompute prior deps (sort deps)

    /// The adapter every prior-blind entry point runs through (Phase 298): an evaluator that is never
    /// handed a prior, as the prior-aware shape the `With` forms take. `eval` and `evalFrom` ARE
    /// `evalWith` and `evalFromWith` over it, so the two families cannot drift apart.
    let private priorBlind
        (evalNode: (string -> 'v option) -> string -> Result<'v, string>)
        : (string -> 'v option) -> 'v option -> string -> Result<'v, string> =
        fun resolve _ id -> evalNode resolve id

    /// A dependency map PREPARED for repeated incremental evaluation (Phase 298): its order and cycles
    /// (`sort`) and its dependents (`dependents`), derived ONCE. `evalFrom` / `evalFromWith` derive all
    /// three on every call — a Tarjan pass and a map inversion per tick, for a graph that changes far
    /// less often than its values do; a host that ticks hands `evalFromPlan` / `evalFromWithPlan` the
    /// same plan until the GRAPH changes, and rebuilds it then. `Deps` is the map the plan was built
    /// from: a plan is a cache of it, never a second source of truth.
    type Plan =
        {
            /// The dependency map the plan was prepared from; the resolver answers from it and an
            /// unknown changed id is judged against its keys.
            Deps: Map<string, Set<string>>
            /// `sort Deps` — the walk order and the cyclic groups. A hand-built plan can hold it to
            /// `validTopo`.
            Topo: TopoResult
            /// `dependents Deps` — the reverse edges the dirty set is grown over; an id nothing reads
            /// has no key.
            Dependents: Map<string, Set<string>>
        }

    /// Prepare `deps` (Phase 298): one `sort`, one `dependents`.
    let plan (deps: Map<string, Set<string>>) : Plan =
        { Deps = deps
          Topo = sort deps
          Dependents = dependents deps }

    /// The full evaluator over a prior-aware evaluator (Phase 250): `eval` with every node handed
    /// `None` as its prior. It is the reference `evalFromWith` is certified against.
    let evalWith
        (evalNode: (string -> 'v option) -> 'v option -> string -> Result<'v, string>)
        (deps: Map<string, Set<string>>)
        : Result<EvalOutcome<'v>, PropagationError> =
        walkWith evalNode (fun _ -> true) Map.empty deps

    /// The reference full evaluator (Phase 69): evaluate every acyclic node once, in dependency order,
    /// threading the results; cyclic SCCs are returned in `EvalOutcome.Cyclic`. The evaluator the
    /// incremental `evalFrom` is certified byte-identical to. `evalWith` over an evaluator that ignores
    /// its prior (Phase 298).
    ///
    /// Every node is recomputed, so this is where an evaluator that reads outside its declaration is ALWAYS
    /// caught: `EvalUndeclaredRead` at the first such node in dependency order (Phase 209).
    let eval
        (evalNode: (string -> 'v option) -> string -> Result<'v, string>)
        (deps: Map<string, Set<string>>)
        : Result<EvalOutcome<'v>, PropagationError> =
        evalWith (priorBlind evalNode) deps

    /// `evalFromWith` over a prepared plan (Phase 298) — the one incremental walk every incremental
    /// entry point runs. The refusal and the walk are `evalFromWith`'s, word for word; only the
    /// order, the cycles and the dependents are read from `p` instead of derived from `p.Deps`.
    let evalFromWithPlan
        (evalNode: (string -> 'v option) -> 'v option -> string -> Result<'v, string>)
        (prior: Map<string, 'v>)
        (changed: Set<string>)
        (p: Plan)
        : Result<EvalOutcome<'v>, PropagationError> =
        let unknown = changed |> Set.filter (fun c -> not (Map.containsKey c p.Deps))

        if not (Set.isEmpty unknown) then
            Error(PropagationError.EvalUnknownChange(Set.toList unknown))
        else
            let dirty = closureOver p.Dependents changed
            walkTopo evalNode (fun id -> Set.contains id dirty) prior p.Deps p.Topo

    /// `evalFrom` over a prepared plan (Phase 298): `evalFromWithPlan` with an evaluator that ignores
    /// its prior.
    let evalFromPlan
        (evalNode: (string -> 'v option) -> string -> Result<'v, string>)
        (prior: Map<string, 'v>)
        (changed: Set<string>)
        (p: Plan)
        : Result<EvalOutcome<'v>, PropagationError> =
        evalFromWithPlan (priorBlind evalNode) prior changed p

    /// Incrementally re-evaluate with each RECOMPUTED node handed its own prior value (Phase 250):
    /// `evalFrom`, except that `evalNode resolve prior id` receives `Map.tryFind id prior` beside the
    /// resolver. A clean node is reused exactly as `evalFrom` reuses it, and the refusals are
    /// `evalFrom`'s: an unknown changed id is `EvalUnknownChange`, an undeclared read
    /// `EvalUndeclaredRead` — one contract, not two. `evalFromWithPlan` over `plan deps` (Phase 298).
    ///
    /// **The agreement theorem, restated for it** (`evalfromwith_agrees`, `proofs/Propagation.fst`):
    /// `evalFromWith ev prior changed deps = evalWith ev deps` under `evalFrom`'s premises for the
    /// evaluator's prior-blind reading (`fun resolve id -> ev resolve None id`) and ONE more — **the
    /// evaluator's answer does not depend on the prior it is handed**: at every node the walk
    /// recomputes, under the resolver it is handed there, `ev resolve (Some p) id = ev resolve None
    /// id`. The prior is a hint for reusing work, never an input to the answer. A table node that
    /// refreshes from its prior state must return the table a fresh evaluation would; one whose prior
    /// is out of step with its source must recompute rather than trust it.
    ///
    /// That premise is the domain's, and `Conformance.propagationEvaluatorLawsWith` samples it at the
    /// domain's own evaluator and edits. A value that carries a reuse cache (an incremental state
    /// beside a table) defines its equality over what it MEANS, not over the cache: the theorem's
    /// equality is the value type's.
    let evalFromWith
        (evalNode: (string -> 'v option) -> 'v option -> string -> Result<'v, string>)
        (prior: Map<string, 'v>)
        (changed: Set<string>)
        (deps: Map<string, Set<string>>)
        : Result<EvalOutcome<'v>, PropagationError> =
        evalFromWithPlan evalNode prior changed (plan deps)

    /// Incrementally re-evaluate (Phase 69) given the PRIOR values and the changed-input set: recompute only
    /// the dirty subgraph (`dirtyFromChangedIds`) in dependency order, reusing each clean node's prior value.
    /// **Byte-identical to a full `eval` over the same inputs** (`Conformance.propagationEvalLaws`) — a clean
    /// node's inputs are unchanged, so its value equals its prior; a dirty node re-evaluates against the new
    /// upstream values. A node absent from `prior` (never evaluated) is always recomputed. A `changed` id
    /// not in the dependency map is a named `EvalUnknownChange` (GP5). `evalFromWith` over an evaluator
    /// that ignores its prior (Phase 298); a host that ticks over one graph hands `evalFromPlan` a `plan`.
    ///
    /// **The evaluator contract (Phase 186, first clause ENFORCED by Phase 209).** That equality is a
    /// THEOREM — `evalfrom_agrees` in `proofs/Propagation.fst`. Its first premise is now a property of this
    /// driver rather than a promise the caller makes: `resolve` answers for `deps[id]` and for nothing else,
    /// and a read outside it ends the evaluation with `EvalUndeclaredRead` naming the node and the read. An
    /// evaluator that reads an undeclared node no longer silently keeps a stale value here where `eval`
    /// computes a fresh one — it is refused, as data, wherever it is invoked.
    ///
    /// Two premises remain the caller's, and neither can be read off a resolver: `changed` names every node
    /// whose evaluation differs from the one that produced `prior`, and `prior` came from `eval` (or an
    /// earlier `evalFrom`) over the SAME `deps`.
    ///
    /// **Where the refusal is observable.** `evalFrom` invokes `evalNode` only on the nodes it recomputes —
    /// the dirty set, plus any node absent from `prior` — so a violating node that is clean AND present in
    /// `prior` is reused without being re-invoked and its violation is not seen HERE. That is not a hole in
    /// the enforcement: the second premise above says `prior` came from `eval` over the same `deps`, and
    /// `eval` recomputes everything, so such a `prior` cannot exist. Prime with `eval`, and the violation is
    /// found before there is a `prior` to reuse.
    let evalFrom
        (evalNode: (string -> 'v option) -> string -> Result<'v, string>)
        (prior: Map<string, 'v>)
        (changed: Set<string>)
        (deps: Map<string, Set<string>>)
        : Result<EvalOutcome<'v>, PropagationError> =
        evalFromWith (priorBlind evalNode) prior changed deps

    // ---- the prior value, inside the contract (Phase 250) ----
    // `evalFrom` hands an evaluator its declared reads and nothing else, and a node's OWN prior value
    // is not one of them — so a domain that reuses work WITHIN a node (a table node refreshing only the
    // rows a source edit reached) had to keep its caches beside the driver and keep them in step with
    // it by hand, with nothing certifying the bookkeeping. `evalWith` / `evalFromWith` above hand the
    // evaluator its prior value as an argument, so the driver keeps it in step: a node is handed
    // exactly the value it had in the evaluation that produced `prior`.

    // ---- demand-driven evaluation: pull (Phase 317) ----
    // `evalFrom` pushes a change forward over the dirty set; these two pull a TARGET set back over
    // the needed set (`neededFor`) and evaluate that and nothing else — "compute X, and only what X
    // reads". The walk is the one every other entry point runs, over `sort deps`'s order restricted
    // to the needed set: a subsequence of a dependency order is a dependency order, and walking the
    // SAME order is what makes agreement with `eval` a theorem (`eval_for_agrees`,
    // `proofs/Propagation.fst`) rather than an argument about two orders. The order is still derived
    // over the whole map — `sort` is cheap beside evaluation, and it is the evaluator that is pulled.

    /// `sort deps` restricted to the needed set: its order filtered, and the cyclic groups that meet
    /// it (a group meeting a closed set lies inside it, since every member reads every other).
    let private restrictTopo (needed: Set<string>) (topo: TopoResult) : TopoResult =
        { Order = topo.Order |> List.filter (fun id -> Set.contains id needed)
          Cycles = topo.Cycles |> List.filter (List.exists (fun id -> Set.contains id needed)) }

    /// Demand-driven evaluation over a prior-aware evaluator (Phase 317): `evalWith` restricted to
    /// `neededFor deps targets` — every needed node is evaluated once, in dependency order, handed
    /// `None` as its prior, and no node outside the needed set is handed to `evalNode` at all.
    ///
    /// **Agreement** (`eval_for_agrees`): when `evalWith evalNode deps` succeeds, this succeeds with
    /// exactly its values on the needed set and exactly its cyclic groups that meet the needed set.
    /// The converse is the point of pulling: a node that would fail OUTSIDE the needed set is never
    /// reached, so this can succeed where the full evaluation does not. A target the map does not
    /// hold is needed and has no value — as under `eval`, an absent id is never evaluated — so a
    /// caller that must tell the two apart asks `Map.containsKey`. The undeclared-read refusal is
    /// `eval`'s (`EvalUndeclaredRead`), at the first needed node that reads outside its declaration.
    let evalForWith
        (evalNode: (string -> 'v option) -> 'v option -> string -> Result<'v, string>)
        (targets: Set<string>)
        (deps: Map<string, Set<string>>)
        : Result<EvalOutcome<'v>, PropagationError> =
        let needed = neededFor deps targets
        walkTopo evalNode (fun _ -> true) Map.empty deps (restrictTopo needed (sort deps))

    /// Demand-driven evaluation (Phase 317): `eval` restricted to `neededFor deps targets` — the
    /// targets and everything upstream of them, evaluated once each in dependency order, and nothing
    /// else. `evalForWith` over an evaluator that ignores its prior; its agreement with `eval` on the
    /// needed set, and its silence outside it, are `evalForWith`'s.
    let evalFor
        (evalNode: (string -> 'v option) -> string -> Result<'v, string>)
        (targets: Set<string>)
        (deps: Map<string, Set<string>>)
        : Result<EvalOutcome<'v>, PropagationError> =
        evalForWith (priorBlind evalNode) targets deps

/// The dependency-graph algorithms of `Propagation`, under a name a consumer with NO evaluator finds
/// them by (Phase 313). A domain checking reference cycles, ordering declarations or inverting a
/// "refers to" relation needs the topological sort, not change propagation, and looked for a graph
/// module rather than an evaluation one — so each re-implemented the depth-first search, several
/// without a visited set. These are `Propagation`'s functions, not copies: one Tarjan pass, one
/// definition of a cycle (a strongly-connected group of more than one id, or a self-reference), and
/// cycles reported as DATA, never as a divergence (GP4). Linear in ids plus edges. String-keyed, as
/// `Propagation` is: a map from each id to the ids it depends on.
[<RequireQualifiedAccess>]
module Graph =

    /// `Propagation.TopoResult`: `Order` dependencies-first, `Cycles` each circular group.
    type TopoResult = Propagation.TopoResult

    /// `Propagation.sort` — the dependencies-first order of `deps` and its cycles. An edge to an id
    /// the map does not hold is ignored for ordering (a dangling edge is not a cycle).
    let sort (deps: Map<string, Set<string>>) : TopoResult = Propagation.sort deps

    /// `Propagation.cycleThrough` — the cycle group containing `target`, if any.
    let cycleThrough (target: string) (deps: Map<string, Set<string>>) : string list option =
        Propagation.cycleThrough target deps

    /// `Propagation.dependents` — the inverted map: each id to the ids that depend on it.
    let dependents (deps: Map<string, Set<string>>) : Map<string, Set<string>> = Propagation.dependents deps
