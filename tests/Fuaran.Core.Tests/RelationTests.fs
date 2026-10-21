/// Questions over a finite relation, answered once and totally (Phase 552; the law set is the one
/// `Fuaran.Program.Relation` shipped under its D47, ported rather than re-invented), each property
/// behind a law or a differential against a brute-force reference that follows the DEFINITION rather
/// than the algorithm:
///
///   * **every cycle is found** — every edge that lies on a cycle (its target reaches its source) has
///     both ends in `cyclesAmong`;
///   * **a node is in a non-trivial component exactly when it reaches itself**, by one edge or more —
///     and two nodes share a component exactly when each reaches the other;
///   * **the cost is linear in nodes plus edges** — the pass's counted work is exactly N + E on every
///     relation, whatever the number of paths; the dense diamond a downstream tooling phase measured (200 deep, 4
///     wide, 4^200 paths) is answered fast, with and without a back edge; and a chain and a ring
///     200,000 long are answered without exhausting the stack (Propagation's recursive form died near
///     3,500 deep in Debug and 13,700 in Release — the regression this holds);
///   * **the order is a function of the edge SET** — reordering or repeating a successor list moves no
///     answer — and the components come in reverse topological order, each cycle's members in the
///     pass's order (reversed, a simple cycle's path);
///   * **closure, inverse, sort and chain ends** agree with their references;
///   * **`Propagation` is byte-identical** — `sort`, `dependents`, `dirtyFromChangedIds` and
///     `neededFor` answer, item for item and member order included, what the Phase-298 walker and the
///     frontier closure this file keeps as references answered, on every drawn map.
module Fuaran.Core.Tests.RelationTests

open Expecto
open Fuaran.Core

// ─── the references: the definitions, by brute force ─────────────────────────

let private succOf (edges: Map<'N, 'N list>) (n: 'N) =
    Map.tryFind n edges |> Option.defaultValue []

let private nodesOf (edges: Map<'N, 'N list>) : Set<'N> =
    edges
    |> Map.fold (fun acc k vs -> Set.union (Set.add k acc) (Set.ofList vs)) Set.empty

/// Every node reachable from `n` by ONE edge or more.
let private reachPlus (edges: Map<'N, 'N list>) (n: 'N) : Set<'N> =
    let rec go (seen: Set<'N>) (todo: 'N list) =
        match todo with
        | [] -> seen
        | x :: rest ->
            let fresh =
                succOf edges x |> List.distinct |> List.filter (fun y -> not (seen.Contains y))

            go (Set.union seen (Set.ofList fresh)) (fresh @ rest)

    let first = succOf edges n |> List.distinct
    go (Set.ofList first) first

let private reachesSelf edges n = (reachPlus edges n).Contains n

/// Reachable by ZERO edges or more.
let private reachStar edges n = Set.add n (reachPlus edges n)

let private refCycles (edges: Map<'N, 'N list>) : Set<'N> =
    nodesOf edges |> Set.filter (reachesSelf edges)

let private refTerminals (edges: Map<'N, 'N list>) (n: 'N) : Set<'N> option =
    let reached = reachStar edges n

    if reached |> Set.exists (reachesSelf edges) then
        None
    else
        Some(reached |> Set.filter (fun m -> List.isEmpty (succOf edges m)))

/// The inverse relation by its definition: every node a key, `b` under `a` exactly when `a -> b`.
let private refInverse (edges: Map<'N, 'N list>) : Map<'N, Set<'N>> =
    let nodes = nodesOf edges

    nodes
    |> Set.toList
    |> List.map (fun b -> b, nodes |> Set.filter (fun a -> List.contains b (succOf edges a)))
    |> Map.ofList

/// A seeded random relation over ints: `size` keyed nodes, some successors outside the keys, repeated
/// successors, self-edges — every shape the definitions distinguish.
let private randomRelation (seed: int) : Map<int, int list> =
    let rng = System.Random seed
    let size = 1 + rng.Next 14
    let density = rng.NextDouble() * 0.35

    [ for k in 0 .. size - 1 ->
          k,
          [ for t in 0 .. size + 2 do
                if rng.NextDouble() < density then
                    yield t

                    if rng.NextDouble() < 0.1 then
                        yield t ] ]
    |> Map.ofList

let private seeds = [ 1..400 ]

let private prepared seed = randomRelation seed |> Relation.ofMap

/// The dense diamond a downstream tooling phase measured: every node depends on every node of the layer above, so
/// the relation has width^2 x (layers - 1) edges and width ^ layers paths. `backEdge` closes a cycle
/// L0W0 -> L(last)W0.
let private diamond (layers: int) (width: int) (backEdge: bool) : Map<string, string list> =
    [ for l in 0 .. layers - 1 do
          for w in 0 .. width - 1 ->
              sprintf "L%dW%d" l w,
              [ if l > 0 then
                    for v in 0 .. width - 1 -> sprintf "L%dW%d" (l - 1) v
                if backEdge && l = 0 && w = 0 then
                    yield sprintf "L%dW0" (layers - 1) ] ]
    |> Map.ofList

// ─── the laws ────────────────────────────────────────────────────────────────

let private cycles =
    testList
        "cycle membership"
        [ test "every cycle is found: each edge on a cycle has both ends in cyclesAmong" {
              for seed in seeds do
                  let r = randomRelation seed
                  let found = Relation.cyclesAmong (Relation.ofMap r)

                  for KeyValue(u, vs) in r do
                      for v in vs do
                          if (reachStar r v).Contains u then
                              Expect.isTrue
                                  (found.Contains u && found.Contains v)
                                  (sprintf "seed %d: the edge %d -> %d lies on a cycle" seed u v)
          }

          test "a node is on a cycle exactly when it reaches itself (differential against the definition)" {
              for seed in seeds do
                  let r = randomRelation seed
                  Expect.equal (Relation.cyclesAmong (Relation.ofMap r)) (refCycles r) (sprintf "seed %d" seed)
          }

          test "two nodes share a component exactly when each reaches the other; the components partition the nodes" {
              for seed in seeds do
                  let r = randomRelation seed
                  let components = Relation.components (Relation.ofMap r)

                  Expect.equal
                      (components |> List.concat |> List.sort)
                      (nodesOf r |> Set.toList)
                      (sprintf "seed %d: every node once" seed)

                  let componentOf =
                      components
                      |> List.indexed
                      |> List.collect (fun (i, members) -> members |> List.map (fun m -> m, i))
                      |> Map.ofList

                  for a in nodesOf r do
                      for b in nodesOf r do
                          let mutual = (reachStar r a).Contains b && (reachStar r b).Contains a

                          Expect.equal (componentOf[a] = componentOf[b]) mutual (sprintf "seed %d: %d and %d" seed a b)
          }

          test "cycles are exactly the components that are non-trivial, in the components' order" {
              for seed in seeds do
                  let r = randomRelation seed
                  let relation = Relation.ofMap r

                  let expected =
                      Relation.components relation
                      |> List.filter (fun members ->
                          match members with
                          | [ only ] -> reachesSelf r only
                          | _ -> true)

                  Expect.equal (Relation.cycles relation) expected (sprintf "seed %d" seed)
          }

          test "a self-edge is a cycle of one; a node that is only a target is a node" {
              let r = Map.ofList [ "a", [ "a"; "b" ]; "b", [ "c" ] ] |> Relation.ofMap
              Expect.equal (Relation.cyclesAmong r) (Set.ofList [ "a" ]) "the self-loop, nothing else"
              Expect.equal (Relation.components r) [ [ "c" ]; [ "b" ]; [ "a" ] ] "c is a node; dependencies first"
              Expect.equal (Relation.nodes r) [ "a"; "b"; "c" ] "every key and every target, ascending"
              Expect.equal (Relation.successors r "a") (Set.ofList [ "a"; "b" ]) "a's successors"
              Expect.equal (Relation.successors r "c") Set.empty "a node with none"
              Expect.equal (Relation.successors r "z") Set.empty "a node the relation does not hold"
          } ]

let private order =
    testList
        "the order is canonical"
        [ test "reordering and repeating successor lists moves no answer" {
              for seed in seeds do
                  let r = randomRelation seed
                  let rng = System.Random(seed * 7919)

                  let shuffled =
                      r
                      |> Map.map (fun _ vs ->
                          let doubled = vs @ (vs |> List.filter (fun _ -> rng.Next 2 = 0))
                          doubled |> List.sortBy (fun _ -> rng.Next()))

                  let terminalsOf rel =
                      let t = Relation.terminals rel
                      nodesOf r |> Set.toList |> List.map t

                  let a = Relation.ofMap r
                  let b = Relation.ofMap shuffled
                  Expect.equal (Relation.components b) (Relation.components a) (sprintf "seed %d" seed)
                  Expect.equal (Relation.cycles b) (Relation.cycles a) (sprintf "seed %d" seed)
                  Expect.equal (Relation.sort b) (Relation.sort a) (sprintf "seed %d" seed)
                  Expect.equal (terminalsOf b) (terminalsOf a) (sprintf "seed %d" seed)
                  Expect.equal (Relation.toMap b) (Relation.toMap a) (sprintf "seed %d" seed)
          }

          test "components come in reverse topological order: each after every component it reaches" {
              for seed in seeds do
                  let r = randomRelation seed
                  let components = Relation.components (Relation.ofMap r)

                  let position =
                      components
                      |> List.indexed
                      |> List.collect (fun (i, members) -> members |> List.map (fun m -> m, i))
                      |> Map.ofList

                  for KeyValue(u, vs) in r do
                      for v in vs do
                          Expect.isTrue
                              (position[v] <= position[u])
                              (sprintf "seed %d: %d -> %d, the target's component first" seed u v)
          }

          test
              "a simple cycle's members, reversed, are its path: each consecutive pair an edge, the last closing to the first" {
              // a -> c -> b -> d -> a, with a distractor edge out of the cycle
              let r =
                  Map.ofList [ "a", [ "c" ]; "c", [ "b"; "x" ]; "b", [ "d" ]; "d", [ "a" ] ]
                  |> Relation.ofMap

              match Relation.cycles r with
              | [ members ] ->
                  let path = List.rev members
                  Expect.equal (List.head path) "a" "the pass reaches the cycle at its least node"

                  let closed = path @ [ List.head path ]

                  for u, v in List.pairwise closed do
                      Expect.isTrue ((Relation.successors r u).Contains v) (sprintf "%s -> %s is an edge" u v)
              | other -> failtestf "one cycle expected, found %A" other
          } ]

let private linear =
    testList
        "the cost is linear in nodes plus edges"
        [ test "the pass's counted work is exactly N + E on every relation" {
              for seed in seeds do
                  let r = randomRelation seed
                  let n = (nodesOf r).Count

                  let e =
                      r |> Map.toList |> List.sumBy (fun (_, vs) -> List.distinct vs |> List.length)

                  Expect.equal
                      (Relation.work (Relation.ofMap r))
                      (n + e)
                      (sprintf "seed %d: one finish per node, one look per edge" seed)
          }

          test "the dense diamond 200 deep - 4^200 paths - is answered in linear work, with and without a cycle" {
              let clock = System.Diagnostics.Stopwatch.StartNew()
              let acyclic = diamond 200 4 false |> Relation.ofMap
              let cyclic = diamond 200 4 true |> Relation.ofMap

              Expect.equal (Relation.work acyclic) (800 + 199 * 16) "N + E, not paths"
              Expect.isEmpty (Relation.cyclesAmong acyclic) "no cycle"

              // The back edge L0W0 -> L199W0 closes a cycle: L0W0, L199W0 and every node of layers 1..198 lie on
              // it; L199W1..L199W3 are not reachable from L0W0 and L0W1..L0W3 reach nothing.
              let onCycle = Relation.cyclesAmong cyclic
              Expect.equal onCycle.Count (2 + 198 * 4) "every node on the back edge's cycle, and no other"
              Expect.isFalse (onCycle.Contains "L0W1") "a node that reaches nothing is on no cycle"
              Expect.equal (Relation.work cyclic) (800 + 199 * 16 + 1) "N + E"

              let ends = Relation.terminals acyclic

              Expect.equal
                  (ends "L199W2")
                  (Some(Set.ofList [ "L0W0"; "L0W1"; "L0W2"; "L0W3" ]))
                  "every chain ends at layer 0"

              Expect.equal (Relation.terminals cyclic "L199W1") None "a chain into the cycle ends nowhere"

              Expect.equal
                  ((Relation.reachable acyclic [ "L199W0" ]).Count)
                  797
                  "the closure: itself and the 199 layers below"

              Expect.isLessThan clock.Elapsed.TotalSeconds 5.0 "a path enumeration would not finish"
          }

          test "a chain 200,000 long is answered without exhausting the stack, as is the ring that closes it" {
              let chain = Map.ofList [ for i in 0..199_999 -> i, [ i + 1 ] ] |> Relation.ofMap
              Expect.isEmpty (Relation.cyclesAmong chain) "a chain has no cycle"
              Expect.equal (Relation.terminals chain 0) (Some(Set.singleton 200_000)) "it ends at its last node"
              Expect.equal (Relation.sort chain).Order [ 200_000..-1..0 ] "the chain's sort, dependencies first"
              Expect.equal (Relation.reachable (Relation.inverse chain) [ 200_000 ]).Count 200_001 "the inverse closure"

              let ring =
                  Map.ofList [ for i in 0..199_999 -> i, [ i + 1 ] ]
                  |> Map.add 199_999 [ 0 ]
                  |> Relation.ofMap

              Expect.equal (Relation.cyclesAmong ring).Count 200_000 "closing it puts every node on the cycle"
              Expect.equal (Relation.terminals ring 5) None "a chain on the ring ends nowhere"
          } ]

let private closure =
    testList
        "closure, inverse, sort and chain ends"
        [ test "reachable is the reflexive-transitive closure from the sources (differential)" {
              for seed in seeds do
                  let r = randomRelation seed
                  let rng = System.Random(seed + 31)
                  let sources = nodesOf r |> Set.filter (fun _ -> rng.Next 3 = 0) |> Set.toList

                  let expected =
                      sources |> List.fold (fun acc s -> Set.union acc (reachStar r s)) Set.empty

                  Expect.equal (Relation.reachable (Relation.ofMap r) sources) expected (sprintf "seed %d" seed)
          }

          test "a source outside the relation is reached as itself" {
              Expect.equal
                  (Relation.reachable (Relation.ofMap (Map.ofList [ 1, [ 2 ] ])) [ 9 ])
                  (Set.singleton 9)
                  "a node with no successors"
          }

          test
              "the inverse is the relation turned round (differential), over the same nodes; inverting twice is the identity" {
              for seed in seeds do
                  let r = randomRelation seed
                  let relation = Relation.ofMap r
                  let inverse = Relation.inverse relation
                  Expect.equal (Relation.toMap inverse) (refInverse r) (sprintf "seed %d" seed)
                  Expect.equal (Relation.nodes inverse) (Relation.nodes relation) (sprintf "seed %d: the nodes" seed)

                  Expect.equal
                      (Relation.toMap (Relation.inverse inverse))
                      (Relation.toMap relation)
                      (sprintf "seed %d: an involution" seed)

                  // The closure over the inverse is the set a change dirties: every node that reaches a seed.
                  let rng = System.Random(seed + 53)
                  let changed = nodesOf r |> Set.filter (fun _ -> rng.Next 3 = 0)

                  let expected =
                      nodesOf r
                      |> Set.filter (fun n -> (reachStar r n) |> Set.exists changed.Contains)

                  Expect.equal (Relation.reachable inverse changed) expected (sprintf "seed %d: the dirty set" seed)
          }

          test
              "toMap holds every node as a key, each to its distinct successors, and prepares back to the same relation" {
              for seed in seeds do
                  let r = randomRelation seed
                  let relation = Relation.ofMap r
                  let asMap = Relation.toMap relation
                  Expect.equal (asMap |> Map.toList |> List.map fst) (Relation.nodes relation) (sprintf "seed %d" seed)

                  for KeyValue(node, successors) in asMap do
                      Expect.equal successors (succOf r node |> Set.ofList) (sprintf "seed %d: node %d" seed node)

                  Expect.equal (Relation.components (Relation.ofMap asMap)) (Relation.components relation) "round trip"
          }

          test "sort partitions the nodes into the acyclic order and the cycles, dependencies first (differential)" {
              for seed in seeds do
                  let r = randomRelation seed
                  let relation = Relation.ofMap r
                  let topo = Relation.sort relation
                  let cyclic = topo.Cycles |> List.concat

                  Expect.equal
                      (topo.Order @ cyclic |> List.sort)
                      (nodesOf r |> Set.toList)
                      (sprintf "seed %d: every node once, in one of them" seed)

                  Expect.equal
                      (Set.ofList cyclic)
                      (refCycles r)
                      (sprintf "seed %d: the cycles are the self-reachers" seed)

                  Expect.equal
                      topo.Cycles
                      (Relation.cycles relation)
                      (sprintf "seed %d: the cycles, in their order" seed)

                  let before =
                      topo.Order |> List.indexed |> List.map (fun (i, n) -> n, i) |> Map.ofList

                  for n in topo.Order do
                      for v in succOf r n |> List.distinct do
                          if before.ContainsKey v then
                              Expect.isTrue
                                  (before[v] < before[n])
                                  (sprintf "seed %d: %d reads %d, so %d comes first" seed n v v)
          }

          test "terminals are the successor-less nodes reached, or None when a cycle is reached (differential)" {
              for seed in seeds do
                  let r = randomRelation seed
                  let ends = Relation.terminals (Relation.ofMap r)

                  for n in nodesOf r do
                      Expect.equal (ends n) (refTerminals r n) (sprintf "seed %d: node %d" seed n)

                  Expect.equal (ends 10_000) (Some(Set.singleton 10_000)) "an unknown node ends at itself"
          } ]

// ─── Propagation is byte-identical: the Phase-298 walker, kept as the reference ───────────────────

/// `Propagation`'s own graph code as it stood before Phase 552 — the iterative Tarjan Phase 298 wrote,
/// the `sort` over it, the `dependents` inversion and the frontier closure — kept here whole as the
/// reference the differential below holds the delegating `Propagation` to. Every `TopoResult` a
/// consumer journalled or goldened was produced by this code; a member order that moved here would
/// move a law vector.
module private Phase298 =

    let tarjan (nodes: string list) (succ: string -> string list) : string list list =
        let mutable index = 0
        let idx = System.Collections.Generic.Dictionary<string, int>()
        let low = System.Collections.Generic.Dictionary<string, int>()
        let onStack = System.Collections.Generic.HashSet<string>()
        let stack = System.Collections.Generic.Stack<string>()
        let sccs = ResizeArray<string list>()
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

                        if frames.Count > 0 then
                            let parent = frames.Peek()
                            low[parent] <- min low[parent] low[v]

        List.ofSeq sccs

    let sort (deps: Map<string, Set<string>>) : Propagation.TopoResult =
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

    let dependents (deps: Map<string, Set<string>>) : Map<string, Set<string>> =
        [ for KeyValue(node, reads) in deps do
              for r in reads -> r, node ]
        |> List.groupBy fst
        |> List.map (fun (k, vs) -> k, vs |> List.map snd |> Set.ofList)
        |> Map.ofList

    let closureOver (edges: Map<string, Set<string>>) (seed: Set<string>) : Set<string> =
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

/// A seeded random dependency map in `Propagation`'s shape: string ids, dangling reads (ids no key
/// declares), self-reads, and ids whose order under `compare` is not their numeric order.
let private randomDeps (seed: int) : Map<string, Set<string>> =
    randomRelation seed
    |> Map.toList
    |> List.map (fun (k, vs) -> sprintf "n%d" k, vs |> List.map (sprintf "n%d") |> Set.ofList)
    |> Map.ofList

let private propagation =
    testList
        "Propagation delegates and is byte-identical to the Phase-298 walker"
        [ test "sort: Order, Cycles and the order of each cycle's members, on every drawn map" {
              for seed in seeds do
                  let deps = randomDeps seed
                  Expect.equal (Propagation.sort deps) (Phase298.sort deps) (sprintf "seed %d" seed)

                  Expect.isTrue
                      (Propagation.validTopo deps (Propagation.sort deps))
                      (sprintf "seed %d: the certificate" seed)
          }

          test "dependents, dirtyFromChangedIds and neededFor, on every drawn map" {
              for seed in seeds do
                  let deps = randomDeps seed
                  let rng = System.Random(seed + 17)

                  let ids =
                      deps
                      |> Map.fold (fun acc k vs -> Set.union (Set.add k acc) vs) Set.empty
                      |> Set.filter (fun _ -> rng.Next 3 = 0)

                  Expect.equal (Propagation.dependents deps) (Phase298.dependents deps) (sprintf "seed %d" seed)

                  Expect.equal
                      (Propagation.dirtyFromChangedIds deps ids)
                      (Phase298.closureOver (Phase298.dependents deps) ids)
                      (sprintf "seed %d: the dirty set" seed)

                  Expect.equal
                      (Propagation.neededFor deps ids)
                      (Phase298.closureOver deps ids)
                      (sprintf "seed %d: the needed set" seed)
          }

          test "the documented shapes: dangling reads are not cycles, a self-read is, and member order is the pass's" {
              let deps =
                  Map.ofList
                      [ "a", Set.ofList [ "b"; "ghost" ]
                        "b", Set.ofList [ "c" ]
                        "c", Set.ofList [ "a" ]
                        "d", Set.ofList [ "d" ]
                        "e", Set.empty ]

              let topo = Propagation.sort deps
              Expect.equal topo.Order [ "e" ] "the one acyclic id; the dangling read is ignored"

              Expect.equal
                  topo.Cycles
                  [ [ "c"; "b"; "a" ]; [ "d" ] ]
                  "the pop order of the Phase-298 walker, then the self-read"

              Expect.equal (Propagation.cycleThrough "b" deps) (Some [ "c"; "b"; "a" ]) "the group through b"

              Expect.equal
                  (Propagation.dependents deps)
                  (Map.ofList
                      [ "a", Set.ofList [ "c" ]
                        "b", Set.ofList [ "a" ]
                        "c", Set.ofList [ "b" ]
                        "d", Set.ofList [ "d" ]
                        "ghost", Set.ofList [ "a" ] ])
                  "an id nothing reads has no key; a dangling read has one"
          } ]

[<Tests>]
let tests =
    testList
        "Phase 552 - questions over a finite relation, answered once in Core"
        [ cycles; order; linear; closure; propagation ]
