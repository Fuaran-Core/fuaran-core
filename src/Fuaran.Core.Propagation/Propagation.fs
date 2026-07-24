namespace Fuaran.Core

/// Change propagation over a reference DAG + tree-level dirty recomputation (Phase 68) — the third
/// incremental-recomputation surface of the compute strand, after the columnar `DataFrame.evalFrom`
/// (Phase 34) and the capability-DAG `CapabilityPipeline.evalFrom` (Phase 62). A domain models its artefact
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
        { Order: string list
          Cycles: string list list }

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
    // keyed). Recursive like the Calc precedent; a pathologically deep dependency *chain* could stack-overflow
    // (an iterative rewrite mirrors Tree.fs's Phase-19 walkers if a consumer ever needs it — noted, not built).
    let private tarjan (nodes: string list) (succ: string -> string list) : string list list =
        let mutable index = 0
        let idx = System.Collections.Generic.Dictionary<string, int>()
        let low = System.Collections.Generic.Dictionary<string, int>()
        let onStack = System.Collections.Generic.HashSet<string>()
        let stack = System.Collections.Generic.Stack<string>()
        let sccs = ResizeArray<string list>()

        let rec strongConnect v =
            idx[v] <- index
            low[v] <- index
            index <- index + 1
            stack.Push v
            onStack.Add v |> ignore

            for w in succ v do
                if not (idx.ContainsKey w) then
                    strongConnect w
                    low[v] <- min low[v] low[w]
                elif onStack.Contains w then
                    low[v] <- min low[v] idx[w]

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

        for n in nodes do
            if not (idx.ContainsKey n) then
                strongConnect n

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

    /// The **minimal dirty set** for a changed-input set: `changed` ∪ every id transitively downstream of it
    /// (the reverse-reachability closure over the dependents map). Minimal by construction — an id not
    /// reverse-reachable from any change is never included. This is the primary entry point: a *value* edit
    /// (a formula / cell-body change) names the changed ids directly. Pure, total.
    let dirtyFromChangedIds (deps: Map<string, Set<string>>) (changed: Set<string>) : Set<string> =
        let deps' = dependents deps

        let rec grow (frontier: Set<string>) (acc: Set<string>) =
            if Set.isEmpty frontier then
                acc
            else
                let next =
                    (Set.empty, frontier)
                    ||> Set.fold (fun s node ->
                        match Map.tryFind node deps' with
                        | Some ds -> Set.union s ds
                        | None -> s)

                let fresh = Set.difference next acc
                grow fresh (Set.union acc fresh)

        grow changed changed

    /// Staleness as queryable data (A3): the dirty closure the caller has not yet recomputed, returned as a
    /// derived `Set<string>` — no mutable state, no stored flag on any node (GP2). A semantic alias of
    /// `dirtyFromChangedIds` making the "outputs to mark stale" contract explicit at the call site.
    let staleSet (deps: Map<string, Set<string>>) (changed: Set<string>) : Set<string> =
        dirtyFromChangedIds deps changed

    /// The string ids a structural `SkeletonOp` touches — the *container* ids whose structure changed
    /// (over-approximation always safe, the Phase-34 contract). Because bindings are id-addressed, a
    /// move/reorder does not change any *referenced value*, so only the structurally-involved containers are
    /// touched; a `RemoveNode` touches the whole removed subtree (its former dependents dangle — surfaced as
    /// dirty by the closure, named as a defect by the validator, not here). Resolves subtrees against `root`
    /// (an op carries only ids, not nodes).
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

        match op with
        | InsertChild(parent, _, node) -> Set.add (s parent) (idsOf node)
        | RemoveNode target -> subtreeIds target
        | MoveNode(target, newParent, _) -> Set.ofList [ s target; s newParent ]
        | ReorderChildren(parent, _) -> Set.singleton (s parent)
        | Batch ops ->
            (Set.empty, ops)
            ||> List.fold (fun acc o -> Set.union acc (touchedBy w idw root o))

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

    // ---- incremental recompute driver (Phase 69) ----
    // The tree-level member of the incremental-eval trio (DataFrame.evalFrom columnar ∥
    // CapabilityPipeline.evalFrom capability-DAG ∥ this). Core owns the *order + reuse plumbing* — it walks
    // the acyclic nodes in dependency order and threads the value map; the domain injects `evalNode` (the
    // evaluator, GP6 — no compute in Core). `evalFrom` recomputes only the dirty subgraph and reuses each
    // clean node's prior value, byte-identical to a full `eval`. Cyclic SCCs are returned as data (the
    // `#CALC!` posture); the iterative upgrade is Phase 72.

    /// Why incremental evaluation failed — named, enumerated (GP5), never a throw (GP4). A cyclic reference
    /// is **not** a failure — it is data in `EvalOutcome.Cyclic`.
    type PropagationError =
        | EvalUnknownChange of ids: string list
        | EvalNodeFailed of node: string * message: string

    /// The outcome of a (re)evaluation: the acyclic nodes' values, plus the cyclic SCCs that could not be
    /// ordered (the `#CALC!` set — a caller renders them as cycle errors, or re-runs them under an
    /// iteration policy, Phase 72). A node downstream of a cycle evaluates with its cyclic read resolving to
    /// `None` — the domain's `evalNode` decides how to propagate that (Calc's `#CALC!` propagation).
    type EvalOutcome<'v> =
        { Values: Map<string, 'v>
          Cyclic: string list list }

    /// Shared walk: evaluate the acyclic nodes in dependency order, threading the results; recompute a node
    /// when `recompute id` (or it is absent from `prior`), otherwise reuse its `prior` value. `evalNode
    /// resolve id` computes node `id`, reading already-computed upstreams via `resolve` (present for every
    /// acyclic read in dependency order; `None` for a cyclic/dangling read). Cyclic SCCs are surfaced, never
    /// evaluated.
    let private walk
        (evalNode: (string -> 'v option) -> string -> Result<'v, string>)
        (recompute: string -> bool)
        (prior: Map<string, 'v>)
        (deps: Map<string, Set<string>>)
        : Result<EvalOutcome<'v>, PropagationError> =
        let topo = sort deps

        let rec go (results: Map<string, 'v>) =
            function
            | [] ->
                Ok
                    { Values = results
                      Cyclic = topo.Cycles }
            | id :: rest ->
                if recompute id || not (Map.containsKey id prior) then
                    match evalNode (fun k -> Map.tryFind k results) id with
                    | Ok v -> go (Map.add id v results) rest
                    | Error m -> Error(EvalNodeFailed(id, m))
                else
                    go (Map.add id (Map.find id prior) results) rest

        go Map.empty topo.Order

    /// The reference full evaluator (Phase 69): evaluate every acyclic node once, in dependency order,
    /// threading the results; cyclic SCCs are returned in `EvalOutcome.Cyclic`. The evaluator the
    /// incremental `evalFrom` is certified byte-identical to.
    let eval
        (evalNode: (string -> 'v option) -> string -> Result<'v, string>)
        (deps: Map<string, Set<string>>)
        : Result<EvalOutcome<'v>, PropagationError> =
        walk evalNode (fun _ -> true) Map.empty deps

    /// Incrementally re-evaluate (Phase 69) given the PRIOR values and the changed-input set: recompute only
    /// the dirty subgraph (`dirtyFromChangedIds`) in dependency order, reusing each clean node's prior value.
    /// **Byte-identical to a full `eval` over the same inputs** (`Conformance.propagationEvalLaws`) — a clean
    /// node's inputs are unchanged, so its value equals its prior; a dirty node re-evaluates against the new
    /// upstream values. A node absent from `prior` (never evaluated) is always recomputed. A `changed` id
    /// not in the dependency map is a named `EvalUnknownChange` (GP5).
    let evalFrom
        (evalNode: (string -> 'v option) -> string -> Result<'v, string>)
        (prior: Map<string, 'v>)
        (changed: Set<string>)
        (deps: Map<string, Set<string>>)
        : Result<EvalOutcome<'v>, PropagationError> =
        let unknown = changed |> Set.filter (fun c -> not (Map.containsKey c deps))

        if not (Set.isEmpty unknown) then
            Error(EvalUnknownChange(Set.toList unknown))
        else
            let dirty = dirtyFromChangedIds deps changed
            walk evalNode (fun id -> Set.contains id dirty) prior deps
