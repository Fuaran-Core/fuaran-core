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
        | InsertChild(parent, node) -> Set.add (s parent) (idsOf node)
        | RemoveNode target -> subtreeIds target
        | MoveNode(target, newParent) -> Set.ofList [ s target; s newParent ]
        | ReorderChildren(parent, _) -> Set.singleton (s parent)
        | Batch ops ->
            (Set.empty, ops)
            ||> List.fold (fun acc o -> Set.union acc (touchedBy w idw root o))
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
    /// (Phase 250): `dirtyFromOp` over the PRE-edit tree, restricted to the ids the post-edit tree
    /// still holds.
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
    let changedForOp
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (readsOf: 'Node -> 'Id seq)
        (pre: 'Node)
        (post: 'Node)
        (op: SkeletonOp<'Node, 'Id>)
        : Set<string> =
        let survivors = Tree.ids w post |> List.map idw.ToString |> Set.ofList
        Set.intersect (dirtyFromOp w idw pre readsOf op) survivors

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
        { Read: 'Id; Parts: Set<string> option }

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
                          yield reader ]
            |> Set.ofList

        let deps = nodeDependencies partDeps
        Set.union changed (dirtyFromChangedIds deps firstHop)

    // ---- incremental recompute driver (Phase 69) ----
    // The tree-level member of the incremental-eval trio (DataFrame.evalFrom columnar ∥
    // CapabilityPipeline.evalFrom capability-DAG ∥ this). Core owns the *order + reuse plumbing* — it walks
    // the acyclic nodes in dependency order and threads the value map; the domain injects `evalNode` (the
    // evaluator, GP6 — no compute in Core). `evalFrom` recomputes only the dirty subgraph and reuses each
    // clean node's prior value, byte-identical to a full `eval`. Cyclic SCCs are returned as data (the
    // `#CALC!` posture); the iterative upgrade is Phase 72.

    /// Why incremental evaluation failed — named, enumerated (GP5), never a throw (GP4). A cyclic reference
    /// is **not** a failure — it is data in `EvalOutcome.Cyclic`.
    ///
    /// `EvalUndeclaredRead` (Phase 209) is declared LAST because a case's declaration order IS its tag
    /// number: appending keeps every existing tag where a consumer's serialised or cached form already has
    /// it. It names the node that read and the first id it read outside `deps[node]`.
    type PropagationError =
        | EvalUnknownChange of ids: string list
        | EvalNodeFailed of node: string * message: string
        | EvalUndeclaredRead of node: string * read: string

    /// The outcome of a (re)evaluation: the acyclic nodes' values, plus the cyclic SCCs that could not be
    /// ordered (the `#CALC!` set — a caller renders them as cycle errors, or re-runs them under an
    /// iteration policy, Phase 72). A node downstream of a cycle evaluates with its cyclic read resolving to
    /// `None` — the domain's `evalNode` decides how to propagate that (Calc's `#CALC!` propagation).
    type EvalOutcome<'v> =
        { Values: Map<string, 'v>
          Cyclic: string list list }

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
    let private walkWith
        (evalNode: (string -> 'v option) -> 'v option -> string -> Result<'v, string>)
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
                    | Some r -> Error(EvalUndeclaredRead(id, r))
                    | None ->
                        match computed with
                        | Ok v -> go (Map.add id v results) rest
                        | Error m -> Error(EvalNodeFailed(id, m))
                else
                    go (Map.add id (Map.find id prior) results) rest

        go Map.empty topo.Order

    /// The prior-blind walk every existing entry point runs: the evaluator is never handed a prior.
    let private walk
        (evalNode: (string -> 'v option) -> string -> Result<'v, string>)
        (recompute: string -> bool)
        (prior: Map<string, 'v>)
        (deps: Map<string, Set<string>>)
        : Result<EvalOutcome<'v>, PropagationError> =
        walkWith (fun resolve _ id -> evalNode resolve id) recompute prior deps

    /// The reference full evaluator (Phase 69): evaluate every acyclic node once, in dependency order,
    /// threading the results; cyclic SCCs are returned in `EvalOutcome.Cyclic`. The evaluator the
    /// incremental `evalFrom` is certified byte-identical to.
    ///
    /// Every node is recomputed, so this is where an evaluator that reads outside its declaration is ALWAYS
    /// caught: `EvalUndeclaredRead` at the first such node in dependency order (Phase 209).
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
        let unknown = changed |> Set.filter (fun c -> not (Map.containsKey c deps))

        if not (Set.isEmpty unknown) then
            Error(EvalUnknownChange(Set.toList unknown))
        else
            let dirty = dirtyFromChangedIds deps changed
            walk evalNode (fun id -> Set.contains id dirty) prior deps

    // ---- the prior value, inside the contract (Phase 250) ----
    // `evalFrom` hands an evaluator its declared reads and nothing else, and a node's OWN prior value
    // is not one of them — so a domain that reuses work WITHIN a node (a table node refreshing only the
    // rows a source edit reached, `DataFrame.Incremental`) had to keep its caches beside the driver and
    // keep them in step with it by hand, with nothing certifying the bookkeeping. These two hand the
    // evaluator its prior value as an argument, so the driver keeps it in step: a node is handed
    // exactly the value it had in the evaluation that produced `prior`.

    /// The full evaluator over a prior-aware evaluator (Phase 250): `eval` with every node handed
    /// `None` as its prior. It is the reference `evalFromWith` is certified against.
    let evalWith
        (evalNode: (string -> 'v option) -> 'v option -> string -> Result<'v, string>)
        (deps: Map<string, Set<string>>)
        : Result<EvalOutcome<'v>, PropagationError> =
        walkWith evalNode (fun _ -> true) Map.empty deps

    /// Incrementally re-evaluate with each RECOMPUTED node handed its own prior value (Phase 250):
    /// `evalFrom`, except that `evalNode resolve prior id` receives `Map.tryFind id prior` beside the
    /// resolver. A clean node is reused exactly as `evalFrom` reuses it, and the refusals are
    /// `evalFrom`'s: an unknown changed id is `EvalUnknownChange`, an undeclared read
    /// `EvalUndeclaredRead` — one contract, not two.
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
        let unknown = changed |> Set.filter (fun c -> not (Map.containsKey c deps))

        if not (Set.isEmpty unknown) then
            Error(EvalUnknownChange(Set.toList unknown))
        else
            let dirty = dirtyFromChangedIds deps changed
            walkWith evalNode (fun id -> Set.contains id dirty) prior deps
