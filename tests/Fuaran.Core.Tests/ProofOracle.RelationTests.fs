module Fuaran.Core.Tests.ProofOracle.RelationTests

// The `Proofs.Oracle` differential for `proofs/Relation.fst`: the extracted model
// (`proofs/oracle/Relation.fs`) run beside production, THREE ways. Production's `Relation`
// (Phase 552) is held to the extracted PASS — `components`, `cycles`, `cyclesAmong`, `sort`,
// `reachable`, `inverse`, `terminals`, `nodes` and `successors`, item for item — and both are held
// to the extracted SPECIFICATION the model states concretely over the edge list and does not prove
// of the pass (`mutual`, `deps_first`, `cycles_among_spec`, `terminals_spec`; see the
// `relation-components-are-sccs` row): that is the sampled half of Tarjan's correctness, and it is
// measured here on every drawn relation rather than assumed.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open Expecto

// Phase 553 — the extracted RELATION model, bound before `Fuaran.Core` is open: production's
// `Fuaran.Core.Relation` shares the model's name once it is (as `ModelProp` does for Propagation).
module ModelRel = Relation

open Fuaran.Core
open Fuaran.Core.Tests.ProofOracle.Bridges

/// The bridge: a PREPARED relation as the model reads it — `toMap`'s keys ascending, each node's
/// successors ascending (`sets-are-lists`). The model visits the list in this order, which is the
/// order production's pass visits the dense form, so the two orders are one order.
let private toModel (r: Relation.Relation<string>) : ModelRel.relation =
    Relation.toMap r |> Map.toList |> List.map (fun (k, s) -> k, Set.toList s)

/// The go-red bridge: a bridge that LOSES an edge — every node's last successor is dropped — so the
/// model answers every question over a smaller relation than production's.
let private blindBridge (r: Relation.Relation<string>) : ModelRel.relation =
    Relation.toMap r
    |> Map.toList
    |> List.map (fun (k, s) -> k, (Set.toList s |> List.truncate (max 0 (Set.count s - 1))))

/// A seeded random relation over string ids: `size` keyed nodes, successors outside the keys (a
/// node with no successors, added by `ofMap`), repeated successors and self-edges — every shape the
/// definitions distinguish; `RelationTests`' generator, over ids whose text order is their number.
let private randomRelation (rng: System.Random) : Map<string, string list> =
    let size = 1 + rng.Next 14
    let density = rng.NextDouble() * 0.35
    let id (n: int) = sprintf "n%02d" n

    [ for k in 0 .. size - 1 ->
          id k,
          [ for t in 0 .. size + 2 do
                if rng.NextDouble() < density then
                    yield id t

                    if rng.NextDouble() < 0.1 then
                        yield id t ] ]
    |> Map.ofList

/// The dense diamond `RelationTests` measures: every node of a layer depends on every node of the
/// layer above (width^2 x (layers - 1) edges, width ^ layers paths); `backEdge` closes a cycle.
let private diamond (layers: int) (width: int) (backEdge: bool) : Map<string, string list> =
    [ for l in 0 .. layers - 1 do
          for w in 0 .. width - 1 ->
              sprintf "L%03dW%d" l w,
              [ if l > 0 then
                    for v in 0 .. width - 1 -> sprintf "L%03dW%d" (l - 1) v
                if backEdge && l = 0 && w = 0 then
                    yield sprintf "L%03dW0" (layers - 1) ] ]
    |> Map.ofList

/// A `Topo` either side, rendered alike: the order, then the cycles.
let private topoRender: Differential<Relation.Topo<string>, ModelRel.topo> =
    Differential.pair (fun t -> sprintf "%A" (t.Order, t.Cycles)) (fun t -> sprintf "%A" (t.torder, t.tcycles))

/// A chain end either side: `None`, or the set.
let private endsRender: Differential<Set<string> option, string list option> =
    Differential.pair (fun e -> sprintf "%A" (e |> Option.map Set.toList)) (fun e ->
        sprintf "%A" (e |> Option.map (Set.ofList >> Set.toList)))

/// The answers of one prepared relation, every way the model is held: production against the
/// extracted pass, item for item; both against the extracted specification. `bridge` is the deps
/// bridge the pass runs over; the go-red instrument runs the BLIND one in the same pass.
let private probe (where: string) (rng: System.Random) (r: Relation.Relation<string>) (t: Tally) : Tally =
    let m = toModel r
    let nodes = Relation.nodes r
    let prodComps = Relation.components r
    let modelComps = ModelRel.components m

    let t =
        t
        |> Tally.bump "Relations"
        |> Tally.add "Nodes" (List.length nodes)
        |> Tally.bumpIf (not (Relation.cycles r).IsEmpty) "Cyclic"
        |> Tally.bumpIf (nodes |> List.exists (fun n -> (Relation.successors r n).Contains n)) "SelfEdges"
        |> Differential.compare Differential.structural (where + ": nodes") nodes (ModelRel.nodes m)
        |> Differential.compare Differential.structural (where + ": components") prodComps modelComps
        |> Differential.compare Differential.structural (where + ": cycles") (Relation.cycles r) (ModelRel.cycles m)
        |> Differential.compare
            Differential.structural
            (where + ": cyclesAmong")
            (Relation.cyclesAmong r |> Set.toList)
            (ModelRel.cycles_among m |> Set.ofList |> Set.toList)
        |> Differential.compare topoRender (where + ": sort") (Relation.sort r) (ModelRel.sort m)
        |> Differential.compare
            Differential.structural
            (where + ": inverse")
            (Relation.inverse r
             |> Relation.toMap
             |> Map.toList
             |> List.map (fun (k, s) -> k, Set.toList s))
            (ModelRel.inverse m |> List.map (fun (k, s) -> k, (Set.ofList s |> Set.toList)))

    // every node's successors, and every node's chain end — the pass's and the specification's
    let t =
        nodes
        |> List.fold
            (fun t n ->
                let prodEnd = Relation.terminals r n

                t
                |> Differential.compare
                    Differential.structural
                    (sprintf "%s: successors of %s" where n)
                    (Relation.successors r n |> Set.toList)
                    (ModelRel.succ_of m n |> Set.ofList |> Set.toList)
                |> Differential.compare
                    endsRender
                    (sprintf "%s: terminals of %s" where n)
                    prodEnd
                    (ModelRel.terminals m n |> ofMOpt)
                |> Differential.compare
                    endsRender
                    (sprintf "%s: terminals of %s against the chain definition" where n)
                    prodEnd
                    (ModelRel.terminals_spec m n |> ofMOpt)
                |> Tally.bumpIf prodEnd.IsNone "NoneEnds")
            t

    // a name that is no node ends at itself, both sides and the specification
    let outside = "zz-outside"

    let t =
        t
        |> Differential.compare
            endsRender
            (where + ": terminals of an outside name")
            (Relation.terminals r outside)
            (ModelRel.terminals m outside |> ofMOpt)
        |> Differential.compare
            endsRender
            (where + ": terminals of an outside name, the chain definition")
            (Relation.terminals r outside)
            (ModelRel.terminals_spec m outside |> ofMOpt)

    // the closure from a drawn source set — one to three nodes, one draw in eight an outside name
    let sources =
        [ for _ in 1 .. 1 + rng.Next 3 -> List.item (rng.Next(List.length nodes)) nodes ]
        |> fun s -> if rng.Next 8 = 0 then outside :: s else s

    let t =
        t
        |> Tally.bumpIf (List.contains outside sources) "OutsideSources"
        |> Differential.compare
            Differential.structural
            (sprintf "%s: reachable from %A" where sources)
            (Relation.reachable r sources |> Set.toList)
            (ModelRel.reachable m sources |> Set.ofList |> Set.toList)

    // THE SPECIFICATION, against production's components: two nodes share a component exactly when
    // each reaches the other; the components come dependencies-first; the cyclic nodes are exactly
    // the nodes that reach themselves. (`terminals` was held to its chain definition above.)
    let componentOf =
        prodComps
        |> List.indexed
        |> List.collect (fun (i, c) -> c |> List.map (fun n -> n, i))
        |> Map.ofList

    // (The pair check is quadratic in nodes, each pair two closures: it runs on the drawn relations,
    // not on the diamond, whose 160 nodes are a cost regression for the pass and nothing for this.)
    let t =
        [ if List.length nodes <= 24 then
              for x in nodes do
                  for y in nodes -> x, y ]
        |> List.fold
            (fun t (x, y) ->
                let same = componentOf[x] = componentOf[y]

                t
                |> Tally.bump "Pairs"
                |> Tally.bumpIf (same && x <> y) "SharedPairs"
                |> Differential.check (same = ModelRel.mutual m x y) (fun () ->
                    sprintf
                        "%s: %s and %s %s a component, but mutual reachability says %b"
                        where
                        x
                        y
                        (if same then "share" else "do not share")
                        (ModelRel.mutual m x y)))
            t
        |> Differential.check (ModelRel.deps_first m [] prodComps) (fun () ->
            sprintf "%s: the components are NOT dependencies-first: %A" where prodComps)
        |> Differential.compare
            Differential.structural
            (where + ": cyclesAmong against the self-reaching nodes")
            (Relation.cyclesAmong r |> Set.toList)
            (ModelRel.cycles_among_spec m |> Set.ofList |> Set.toList)

    // THE GO-RED INSTRUMENT, in the same pass: over the blind bridge the pass must lose somewhere
    t
    |> Differential.checkOn "blind-bridge" (prodComps = ModelRel.components (blindBridge r)) (fun () ->
        sprintf "%s: components under the blind bridge" where)

/// The seeded loop: `trials` random relations from one `System.Random`, the probe over each.
let private generated (seed: int) (trials: int) : Tally =
    let rng = System.Random seed

    Tally.empty
        [ "Relations"
          "Nodes"
          "Cyclic"
          "SelfEdges"
          "NoneEnds"
          "OutsideSources"
          "Pairs"
          "SharedPairs" ]
    |> Tally.goRed [ "blind-bridge" ]
    |> Differential.seeded trials rng (fun i rng t ->
        let r = randomRelation rng |> Relation.ofMap
        rng, probe (sprintf "trial %d" i) rng r t)

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [ testCase
              "the relation oracle agrees with Relation.components, cycles, cyclesAmong, sort, reachable, inverse, terminals, nodes and successors over seeded random relations, and production's pass meets the extracted specification — mutual reachability, dependencies-first, the self-reaching nodes and the chain ends"
          <| fun _ ->
              let t = onBigStack (fun () -> generated 553 400)

              Differential.expectAgrees "the relation oracle" t

              Expect.equal (Tally.count "Relations" t) 400 "every drawn relation was probed"

              Expect.isGreaterThan
                  (Tally.count "Cyclic" t)
                  60
                  (sprintf "cyclic relations arose (cyclic=%d)" (Tally.count "Cyclic" t))

              Expect.isGreaterThan
                  (Tally.count "SelfEdges" t)
                  20
                  (sprintf "self-edges arose (selfEdges=%d)" (Tally.count "SelfEdges" t))

              Expect.isGreaterThan
                  (Tally.count "NoneEnds" t)
                  60
                  (sprintf "chains that loop arose (noneEnds=%d)" (Tally.count "NoneEnds" t))

              Expect.isGreaterThan
                  (Tally.count "SharedPairs" t)
                  60
                  (sprintf "pairs sharing a non-trivial component arose (sharedPairs=%d)" (Tally.count "SharedPairs" t))

              Expect.isGreaterThan
                  (Tally.count "OutsideSources" t)
                  20
                  (sprintf "closures from an outside name arose (outsideSources=%d)" (Tally.count "OutsideSources" t))

          testCase
              "a relation oracle handed a BLIND bridge DISAGREES with Relation.components — the measurement can fail"
          <| fun _ ->
              let t = onBigStack (fun () -> generated 553 400)
              Differential.expectLosesOn "the relation oracle" "blind-bridge" t

          testCase "the relation oracle answers the dense diamond as production does, with and without its back edge"
          <| fun _ ->
              let t =
                  onBigStack (fun () ->
                      let rng = System.Random 553

                      Tally.empty
                          [ "Relations"
                            "Nodes"
                            "Cyclic"
                            "SelfEdges"
                            "NoneEnds"
                            "OutsideSources"
                            "Pairs"
                            "SharedPairs" ]
                      |> Tally.goRed [ "blind-bridge" ]
                      |> probe "the diamond" rng (diamond 40 4 false |> Relation.ofMap)
                      |> probe "the diamond with its back edge" rng (diamond 40 4 true |> Relation.ofMap))

              Differential.expectAgrees "the relation oracle on the diamond" t
              Expect.equal (Tally.count "Cyclic" t) 1 "the back edge closes one cycle"
              Expect.equal (Tally.count "Nodes" t) 320 "two diamonds of 160 nodes" ]
