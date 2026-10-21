namespace Fuaran.Core

/// **Questions over a FINITE relation, answered once, totally** (Phase 552) — the one home, in the substrate, for
/// "which nodes lie on a cycle", "which components, dependencies first", "which nodes does this set
/// reach" and "where does a chain of redirects end". Each is one traversal of the relation that
/// finishes every node once and examines every edge once, so it terminates on every finite relation
/// and its cost is linear in nodes plus edges, whatever the number of PATHS (a dense diamond 200
/// layers deep has 4^200 of them and is answered in milliseconds). No function here takes a bound, a
/// depth or a fuel, and none recurses on the call stack — the traversal is an explicit frame stack —
/// so a chain two hundred thousand long is answered as a chain of ten is. A cycle comes back as DATA
/// (GP4), never a divergence; Core owns no evaluator here (GP6).
///
/// A relation is **prepared once** (`ofMap`) into a dense form every question reads: the nodes in
/// ascending order, and each node's distinct successors ascending. A successor that is not a key is a
/// node with no successors; a self-edge is an edge; a repeated successor is one edge. **Every answer is
/// a function of the relation's edge SET** — never of the order a successor list was written in or
/// how often an edge is repeated — because the dense form is sorted and the pass visits nodes and
/// successors in that order; so a component's members, the order of the components and every set
/// below are canonical, and a host can journal or golden an answer.
///
/// Generic over any `'N : comparison` (a string id, an integer index, a roadmap id): the comparison is
/// what sorts the dense form, and nothing here hashes, so the module is FSharp.Core-only and
/// Fable-clean. Pure, total functions of a value: they cross no wire and touch no effect.
[<RequireQualifiedAccess>]
module Relation =

    /// A finite relation over `'N`, prepared once: its nodes ascending, and each node's distinct
    /// successors ascending, as indices into the node array. Built by `ofMap`; read by every question.
    type Relation<'N when 'N: comparison> =
        private
            { Nodes: 'N[]
              Successors: int[][] }

    /// The topological reading of a relation: `Order` is every node in no cycle, each once,
    /// dependencies-first (over a "depends on" relation, every node after each node it reaches);
    /// `Cycles` is each cyclic component, the components dependencies-first, the members of each in
    /// the pass's order (see `components`).
    type Topo<'N when 'N: comparison> =
        {
            /// The nodes in no cycle, each once, every one after each node it reaches that is in no
            /// cycle; a node on a cycle never appears here.
            Order: 'N list
            /// One list per cyclic component, the components dependencies-first, the members of each
            /// in the pass's order — the reverse of a simple cycle's path.
            Cycles: 'N list list
        }

    /// The index of `node` in the ascending `nodes`, or -1: a binary search, so no node is hashed.
    let private indexOf (nodes: 'N[]) (node: 'N) : int =
        let mutable lo = 0
        let mutable hi = nodes.Length - 1
        let mutable found = -1

        while found < 0 && lo <= hi do
            let mid = lo + (hi - lo) / 2
            let c = compare nodes[mid] node

            if c = 0 then found <- mid
            elif c < 0 then lo <- mid + 1
            else hi <- mid - 1

        found

    /// **Prepare a relation** from a map of each node to its successors (a set, a list, any
    /// sequence): the keys and every successor are its nodes; a repeated successor is one edge.
    /// `O((N + E) log N)` — the one place anything is sorted; every question below is then
    /// `O(N + E)` over the prepared form.
    let ofMap<'N, 'S when 'N: comparison and 'S :> seq<'N>> (edges: Map<'N, 'S>) : Relation<'N> =
        let nodes =
            edges
            |> Map.fold
                (fun acc node successors -> Seq.fold (fun s n -> Set.add n s) (Set.add node acc) successors)
                Set.empty
            |> Set.toArray

        let successors =
            nodes
            |> Array.map (fun node ->
                match Map.tryFind node edges with
                | None -> [||]
                | Some targets -> targets |> Seq.map (indexOf nodes) |> Set.ofSeq |> Set.toArray)

        { Nodes = nodes
          Successors = successors }

    /// The nodes of the relation, ascending: every key and every successor, each once.
    let nodes (relation: Relation<'N>) : 'N list = List.ofArray relation.Nodes

    let private lift (relation: Relation<'N>) (indices: int[]) : 'N list =
        indices |> Array.map (fun i -> relation.Nodes[i]) |> List.ofArray

    /// The successors of `node`: empty for a node with none, and for a node the relation does not hold.
    let successors (relation: Relation<'N>) (node: 'N) : Set<'N> =
        match indexOf relation.Nodes node with
        | -1 -> Set.empty
        | i -> lift relation relation.Successors[i] |> Set.ofList

    /// The relation as a map with EVERY node a key — a node with no successors maps to the empty set.
    let toMap (relation: Relation<'N>) : Map<'N, Set<'N>> =
        relation.Nodes
        |> Array.mapi (fun i node -> node, lift relation relation.Successors[i] |> Set.ofList)
        |> Map.ofArray

    /// **The inverse relation**: `b` succeeds `a` here exactly when `a` succeeds `b` in `relation`. Over
    /// a "depends on" relation it is "is depended on by" — the dependents — and `reachable` over it is
    /// the set a change dirties. The nodes are the same; `O(N + E)`.
    let inverse (relation: Relation<'N>) : Relation<'N> =
        let n = relation.Nodes.Length
        let counts = Array.zeroCreate<int> n

        for successors in relation.Successors do
            for w in successors do
                counts[w] <- counts[w] + 1

        let inverted = Array.init n (fun w -> Array.zeroCreate<int> counts[w])
        let filled = Array.zeroCreate<int> n

        // `v` ascends, so each inverted list is ascending and distinct by construction.
        for v in 0 .. n - 1 do
            for w in relation.Successors[v] do
                inverted[w][filled[w]] <- v
                filled[w] <- filled[w] + 1

        { Nodes = relation.Nodes
          Successors = inverted }

    /// What one strongly-connected-components pass found: the components as index arrays, in the order
    /// the pass finished them (every component after each component it reaches), and the pass's whole
    /// work — nodes finished plus edges examined — which the linearity law reads.
    type private Pass = { Components: int[] list; Work: int }

    /// Tarjan's pass, iterative: an explicit frame stack in place of the call stack, so depth costs
    /// heap, not stack. Each node is finished once and each edge examined once. The frames replay the
    /// recursive visit exactly — successors in ascending order, a child's low-link folded into its
    /// parent's when the child's frame finishes, a component emitted when its root's frame does — so
    /// the components, their order and the order of each one's members are the recursive form's, item
    /// for item, and a function of the edge set alone.
    let private pass (relation: Relation<'N>) : Pass =
        let n = relation.Nodes.Length
        let order = Array.create n -1
        let low = Array.zeroCreate<int> n
        let onStack = Array.zeroCreate<bool> n
        let stack = System.Collections.Generic.Stack<int>()
        // the call stack: each frame's node, and the index of the successor it examines next
        let frameNode = System.Collections.Generic.Stack<int>()
        let frameNext = Array.zeroCreate<int> n
        let mutable next = 0
        let mutable work = 0
        let mutable components = []

        let enter v =
            order[v] <- next
            low[v] <- next
            next <- next + 1
            stack.Push v
            onStack[v] <- true
            frameNode.Push v

        for root in 0 .. n - 1 do
            if order[root] < 0 then
                enter root

                while frameNode.Count > 0 do
                    let v = frameNode.Peek()
                    let successors = relation.Successors[v]
                    let i = frameNext[v]

                    if i < successors.Length then
                        frameNext[v] <- i + 1
                        work <- work + 1
                        let w = successors[i]

                        if order[w] < 0 then
                            enter w
                        elif onStack[w] then
                            low[v] <- min low[v] order[w]
                    else
                        frameNode.Pop() |> ignore
                        work <- work + 1

                        if low[v] = order[v] then
                            let members = ResizeArray<int>()
                            let mutable w = -1

                            while w <> v do
                                w <- stack.Pop()
                                onStack[w] <- false
                                members.Add w

                            components <- members.ToArray() :: components

                        // the return to the caller's frame: fold the child's low-link into it
                        if frameNode.Count > 0 then
                            let u = frameNode.Peek()
                            low[u] <- min low[u] low[v]

        { Components = List.rev components
          Work = work }

    /// Whether a component is a CYCLE: more than one member, or one member with an edge to itself.
    let private isCycle (relation: Relation<'N>) (members: int[]) : bool =
        members.Length > 1 || Array.contains members[0] relation.Successors[members[0]]

    /// The pass's work over `relation` — nodes finished plus edges examined. Exposed to this
    /// repository's laws only: it is the counted evidence that the cost is linear.
    let internal work (relation: Relation<'N>) : int = (pass relation).Work

    /// **Every strongly-connected component of the relation**, singletons included, in REVERSE
    /// TOPOLOGICAL order — a component comes after every component it reaches, so over a "depends
    /// on" relation dependencies come first. Among components neither of which reaches the other,
    /// the order is the pass's over ascending nodes, a function of the edge set alone.
    ///
    /// The members of a component are in the pass's order — the order Tarjan's stack pops them,
    /// which is the reverse of the order the pass first reached them — not sorted: for a simple
    /// cycle, the members reversed are the cycle's path, each consecutive pair an edge and the last
    /// closing to the first, which is what a rejection envelope enumerates (GP5). That order is a
    /// function of the edge set too, because the pass is; a caller that wants members ascending
    /// sorts each list.
    let components (relation: Relation<'N>) : 'N list list =
        (pass relation).Components |> List.map (lift relation)

    /// The components that are CYCLES — more than one member, or a single member with a self-edge —
    /// in `components`' order. Every cycle of the relation lies inside exactly one of them.
    let cycles (relation: Relation<'N>) : 'N list list =
        (pass relation).Components
        |> List.filter (isCycle relation)
        |> List.map (lift relation)

    /// **Every node on a cycle** — the nodes that reach themselves by one edge or more.
    let cyclesAmong (relation: Relation<'N>) : Set<'N> =
        cycles relation |> List.concat |> Set.ofList

    /// **The topological sort**, as the pass's reading of `components`: each singleton component with
    /// no self-edge is an `Order` entry and every other component is a `Cycles` entry, both in the
    /// components' dependencies-first order. Total — a cycle is reported, never diverged on.
    let sort (relation: Relation<'N>) : Topo<'N> =
        let found = pass relation

        { Order =
            found.Components
            |> List.choose (fun members ->
                if isCycle relation members then
                    None
                else
                    Some relation.Nodes[members[0]])
          Cycles = found.Components |> List.filter (isCycle relation) |> List.map (lift relation) }

    /// **The closure of the relation from `sources`**: every node reachable from one of them by zero
    /// edges or more — the sources themselves included, a source that is no node of the relation
    /// included as a node with no successors. `O(N + E)`, whatever the number of paths.
    let reachable (relation: Relation<'N>) (sources: 'N seq) : Set<'N> =
        let seen = Array.zeroCreate<bool> relation.Nodes.Length
        let frontier = System.Collections.Generic.Stack<int>()
        let mutable outside = Set.empty

        for source in sources do
            match indexOf relation.Nodes source with
            | -1 -> outside <- Set.add source outside
            | i when not seen[i] ->
                seen[i] <- true
                frontier.Push i
            | _ -> ()

        while frontier.Count > 0 do
            for w in relation.Successors[frontier.Pop()] do
                if not seen[w] then
                    seen[w] <- true
                    frontier.Push w

        relation.Nodes
        |> Array.indexed
        |> Array.fold (fun acc (i, node) -> if seen[i] then Set.add node acc else acc) outside

    /// **Where a node's chains END**: the terminal nodes — nodes with no successor — it reaches,
    /// itself when it has none, or `None` when it reaches a CYCLE, because a chain that loops ends
    /// nowhere. A node that is no node of the relation ends at itself. Partially applied, one pass
    /// answers every node.
    ///
    /// Over a "carried into" relation (a superseded item's successors) this is the set of items that
    /// finally carry a node's work, and `None` is the loop that leaves it carried by nothing: readiness
    /// over redirects is "every terminal shipped" with no further machinery.
    let terminals (relation: Relation<'N>) : 'N -> Set<'N> option =
        let found = pass relation
        let ends = Array.create<Set<'N> option> relation.Nodes.Length None

        // Reverse topological order: every component a component reaches was answered before it.
        for members in found.Components do
            let answer =
                if isCycle relation members then
                    None
                else
                    let v = members[0]

                    match relation.Successors[v] with
                    | [||] -> Some(Set.singleton relation.Nodes[v])
                    | successors ->
                        successors
                        |> Array.fold
                            (fun acc w ->
                                match acc, ends[w] with
                                | Some sofar, Some theirs -> Some(Set.union sofar theirs)
                                | _ -> None)
                            (Some Set.empty)

            for v in members do
                ends[v] <- answer

        fun node ->
            match indexOf relation.Nodes node with
            | -1 -> Some(Set.singleton node)
            | i -> ends[i]
