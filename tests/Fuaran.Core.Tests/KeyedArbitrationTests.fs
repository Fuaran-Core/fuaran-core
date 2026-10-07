module Fuaran.Core.Tests.KeyedArbitrationTests

// Phase 247 — off-walk identity in the footprint and arbitration. `Ops.footprintKeyed` reads an
// inserted subtree's ids over the keyed walk, so two scripts that each bring in one keyed id are
// dependent; `Arbitration.arbitrateWith` takes the domain's footprint and applicability, and
// `arbitrateContained` is its container-aware common case; `Conformance.keyedArbitrationLawsAt`
// builds the container-illegal proposal and the keyed-id clash the plain family never draws.
//
// The domain is `ConformanceTests`' keyed reference: `KNode` holds a case table (`Cases`) beside
// its structural `Children`, and `ConformanceTests.keyw` declares it. A node with no children at
// construction is a `para`, which the container capability below refuses.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ConformanceTests

let private k (id: string) (children: KNode list) (cases: (string * KNode) list) : KNode =
    { Id = id
      Kind = (if List.isEmpty children then "para" else "section")
      Children = children
      Cases = cases }

let private leaf (id: string) = k id [] []

let private canHold (n: KNode) = n.Kind <> "para"

let private canHoldAll (_: KNode) = true

/// A switch `id` whose case holds a node carrying `keyed` — the hidden keyed collision's shape.
let private switchHolding (id: string) (keyed: string) = k id [] [ "on", leaf keyed ]

/// root [ s1 [ x1 ]; s2 [ x2 ]; note ] — two sections and a leaf.
let private doc () =
    k "root" [ k "s1" [ leaf "x1" ] []; k "s2" [ leaf "x2" ] []; leaf "note" ] []

let private fp ops = Ops.footprint knodew idw ops
let private fpK ops = Ops.footprintKeyed keyw knodew idw ops

let private noKeyedK: KeyedWitness<KNode, string> =
    { keyw with
        KeyedChildren = fun _ -> []
        ReplaceKeyedChildren = fun n _ -> n
        PlaceKeyedChild = fun _ _ -> None }

let private prop id ops : OpScriptProposal<KNode, string> =
    { Id = id
      Holder = sprintf "agent-%d" id
      Ops = ops }

/// The per-node encoder the keyed-walk content hash reads: id, kind and the case labels.
let encK (n: KNode) : string =
    String.concat "|" [ n.Id; n.Kind; n.Cases |> List.map fst |> String.concat "," ]

/// `KeyedApplyTests.deepGen` under the `para`-refusing container capability: drawn trees hold
/// leaves to insert under and keyed positions to place into.
let containedKGen: OpGen<KNode, string> =
    { KeyedApplyTests.deepGen with
        CanHold = Some canHold }

let private failed (results: LawResult list) =
    results |> List.filter (fun r -> not r.Passed)

let private landKeyed (ops: SkeletonOp<KNode, string> list) (root: KNode) =
    ops
    |> List.fold (fun acc op -> acc |> Result.bind (Ops.applyContainedKeyed keyw canHoldAll knodew idw op)) (Ok root)

[<Tests>]
let tests =
    testList
        "Keyed arbitration (Phase 247)"
        [ testList
              "Ops.footprintKeyed"
              [ testCase "a hidden keyed collision (two grafts, one keyed id) is declared dependent, on that id"
                <| fun _ ->
                    let a = [ InsertChild("s1", switchHolding "swA" "shared") ]
                    let b = [ InsertChild("s2", switchHolding "swB" "shared") ]

                    Expect.isTrue (Ops.independent (fp a) (fp b)) "the unkeyed footprint cannot see the case"
                    Expect.isFalse (Ops.independent (fpK a) (fpK b)) "the keyed footprint can"

                    Expect.contains
                        (Ops.interference (fpK a) (fpK b))
                        (Interference.SameTarget(Set.singleton "shared"))
                        "and names the id both scripts author"

                testCase "Dag.conflicts over the keyed footprint halts the two lanes at the keyed id"
                <| fun _ ->
                    let a = [ InsertChild("s1", switchHolding "swA" "shared") ]
                    let b = [ InsertChild("s2", switchHolding "swB" "shared") ]
                    let byOp op = fpK [ op ]

                    Expect.isEmpty (Dag.conflicts (fun op -> fp [ op ]) a b) "unkeyed: the merge is declared clean"

                    let reported = Dag.conflicts byOp a b |> List.map (fun c -> c.Shape, c.Address)

                    Expect.contains
                        reported
                        (MergeConflictShape.ConcurrentUpdate, "shared")
                        "keyed: reported where the lanes collide"

                testCase "the inserted id set is the graft's keyed walk, and nothing else moves"
                <| fun _ ->
                    let graft =
                        k "g" [ leaf "g1" ] [ "one", k "c" [ leaf "c1" ] [ "inner", leaf "ck" ] ]

                    let ops = [ InsertChild("s1", graft) ]
                    let keyed = fpK ops
                    let plain = fp ops
                    let all = Set.ofList [ "g"; "c"; "ck"; "c1"; "g1" ]

                    Expect.equal keyed.ContentWrites all "every id the keyed walk reaches is authored"
                    Expect.equal keyed.Reads (Set.add "s1" all) "and read (the duplicate check)"
                    Expect.equal plain.ContentWrites (Set.ofList [ "g"; "g1" ]) "the unkeyed walk stops at the surface"
                    Expect.equal keyed.StructureWrites plain.StructureWrites "the structural writes are unchanged"
                    Expect.equal keyed.UnknownParentWrites plain.UnknownParentWrites "and so are the relocations"

                testCase "an UpdateNode payload's keyed subtrees are read and authored as new content"
                <| fun _ ->
                    let payload = k "sw" [] [ "one", k "c" [] [ "inner", leaf "ck" ] ]
                    let keyed = fpK [ UpdateNode payload ]

                    Expect.equal keyed.ContentWrites (Set.ofList [ "sw"; "c"; "ck" ]) "target + incoming keyed ids"
                    Expect.equal keyed.UnknownParentWrites (Set.singleton "sw") "the update's unknown-parent write"

                    Expect.equal
                        (fp [ UpdateNode payload ]).ContentWrites
                        (Set.singleton "sw")
                        "unkeyed: the target only"

                testCase "a domain that declares no keyed position gets footprint's answer, op for op"
                <| fun _ ->
                    let ops =
                        [ InsertChild("s1", switchHolding "swA" "shared")
                          RemoveNode "x2"
                          MoveNode("note", "s2")
                          ReorderChildren("root", [ "s2"; "s1"; "note" ])
                          UpdateNode(switchHolding "s1" "z")
                          Batch [ InsertChild("s2", leaf "n"); RemoveNode "x1" ] ]

                    for op in ops do
                        Expect.equal (Ops.footprintKeyed noKeyedK knodew idw [ op ]) (fp [ op ]) (sprintf "%A" op) ]

          testList
              "Ops.canApplyAllKeyed"
              [ testCase "it threads the keyed engine and names the first refusal"
                <| fun _ ->
                    let t = k "root" [ leaf "a"; switchHolding "sw" "held" ] []
                    let script = [ InsertChild("root", leaf "n"); InsertChild("root", leaf "held") ]

                    Expect.equal
                        (Ops.canApplyAllKeyed keyw canHoldAll knodew idw script t)
                        (Error(1, DuplicateId "held"))
                        "the second step collides with an id held in a case"

                    Expect.equal
                        (Ops.canApplyAllWith canHoldAll knodew idw script t)
                        (Ok())
                        "the unkeyed check is blind to it"

                    Expect.equal
                        (Ops.canApplyAllKeyed keyw canHold knodew idw [ InsertChild("a", leaf "n") ] t)
                        (Error(0, NotAContainer("a", "para")))
                        "and it carries the container capability"

                testCase "with no keyed position declared it is canApplyAllWith"
                <| fun _ ->
                    let t = doc ()

                    for script in
                        [ [ InsertChild("s1", leaf "n"); MoveNode("n", "s2") ]
                          [ InsertChild("note", leaf "n") ]
                          [ RemoveNode "ghost" ]
                          [ ReorderChildren("s1", [ "x1" ]); RemoveNode "x1"; RemoveNode "x1" ] ] do
                        Expect.equal
                            (Ops.canApplyAllKeyed noKeyedK canHold knodew idw script t)
                            (Ops.canApplyAllWith canHold knodew idw script t)
                            (sprintf "%A" script) ]

          testList
              "Arbitration.arbitrateWith / arbitrateContained"
              [ testCase "arbitrate is arbitrateWith at the skeleton defaults"
                <| fun _ ->
                    let t = doc ()

                    let proposals =
                        [ prop 3 [ InsertChild("s1", leaf "n3") ]
                          prop 1 [ InsertChild("s1", leaf "n1") ]
                          prop 2 [ RemoveNode "x2" ]
                          prop 4 [ RemoveNode "ghost" ]
                          prop 5 [ ReorderChildren("s2", [ "x2" ]) ] ]

                    Expect.equal
                        (Arbitration.arbitrateWith (Ops.footprint knodew idw) (Ops.canApplyAll knodew idw) t proposals)
                        (Arbitration.arbitrate knodew idw t proposals)
                        "one partition"

                    Expect.equal
                        (Arbitration.arbitrateContained canHoldAll knodew idw t proposals)
                        (Arbitration.arbitrate knodew idw t proposals)
                        "and arbitrateContained with a capability that refuses nothing is arbitrate"

                testCase "an insert under a leaf: arbitrate admits it, arbitrateContained refuses it"
                <| fun _ ->
                    let t = doc ()
                    let underLeaf = prop 1 [ InsertChild("note", leaf "n") ]
                    let elsewhere = prop 2 [ InsertChild("s1", leaf "m") ]

                    let plain = Arbitration.arbitrate knodew idw t [ underLeaf; elsewhere ]
                    Expect.equal (plain.Accepted |> List.map _.Id) [ 1; 2 ] "admitted by the plain check"

                    Expect.isError
                        (Ops.applyAllWith canHold knodew idw plain.MergedScript t)
                        "and the domain's engine then refuses the merged script"

                    let contained =
                        Arbitration.arbitrateContained canHold knodew idw t [ underLeaf; elsewhere ]

                    Expect.equal (contained.Accepted |> List.map _.Id) [ 2 ] "only the legal proposal lands"

                    Expect.equal
                        contained.Rejected
                        [ underLeaf, Inapplicable(0, NotAContainer("note", "para")) ]
                        "refused with the envelope applyContained would give"

                    let viaDomain =
                        Arbitration.arbitrateWith
                            (Ops.footprintKeyed keyw knodew idw)
                            (Ops.canApplyAllKeyed keyw canHold knodew idw)
                            t
                            [ underLeaf; elsewhere ]

                    Expect.equal viaDomain contained "refused at the domain's canApply too"

                testCase "a keyed label clash at N = 32: every clash is refused, half the set lands"
                <| fun _ ->
                    let n = 32

                    let sections =
                        [ for i in 1..n -> k (sprintf "s%d" i) [ leaf (sprintf "p%d" i) ] [] ]

                    let t = k "root" sections []
                    // proposal i grafts a figure under its own section, carrying label L(i mod 16) keyed
                    let proposals =
                        [ for i in 1..n ->
                              prop
                                  i
                                  [ InsertChild(
                                        sprintf "s%d" i,
                                        switchHolding (sprintf "fig%d" i) (sprintf "L%d" (i % 16))
                                    ) ] ]

                    let plain = Arbitration.arbitrate knodew idw t proposals
                    Expect.equal (List.length plain.Accepted) n "the plain footprint admits every clash"

                    match landKeyed plain.MergedScript t with
                    | Error(DuplicateId _) -> ()
                    | other ->
                        failtestf "the merged plain set should be refused DuplicateId by the keyed engine: %A" other

                    let keyed =
                        Arbitration.arbitrateWith
                            (Ops.footprintKeyed keyw knodew idw)
                            (Ops.canApplyAllKeyed keyw canHoldAll knodew idw)
                            t
                            proposals

                    Expect.equal (List.length keyed.Accepted) (n / 2) "one proposal per label lands"
                    Expect.equal (List.length keyed.Rejected) (n / 2) "the other is refused"

                    for p, reason in keyed.Rejected do
                        match reason with
                        | Conflicts(_, explained) ->
                            let label = sprintf "L%d" (p.Id % 16)

                            Expect.isTrue
                                (explained
                                 |> List.exists (fun (_, clauses) ->
                                     List.contains (Interference.SameTarget(Set.singleton label)) clauses))
                                (sprintf "proposal %d is refused on its label %s" p.Id label)
                        | other -> failtestf "proposal %d: expected Conflicts, got %A" p.Id other

                    Expect.isOk (landKeyed keyed.MergedScript t) "and the admitted half lands under the keyed engine" ]

          testList
              "Conformance.keyedArbitrationLawsAt"
              [ testCase "the keyed reference passes, with both built arms reached"
                <| fun _ ->
                    let results =
                        Conformance.keyedArbitrationLawsAt keyw knodew idw containedKGen encK 2470 200

                    match failed results with
                    | [] -> ()
                    | bad -> failtestf "%A" bad

                    Expect.isFalse
                        (results |> List.exists (fun r -> r.Law.Contains "vacuous BY DECLARATION"))
                        "no arm is vacuous at this witness"

                testCase "handed arbitrate's footprint, the clash arm goes red"
                <| fun _ ->
                    let results =
                        Conformance.keyedArbitrationLawsWith
                            keyw
                            knodew
                            idw
                            containedKGen
                            encK
                            (Ops.footprint knodew idw)
                            (Ops.canApplyAllKeyed keyw canHold knodew idw)
                            2471
                            200

                    Expect.isTrue
                        (results
                         |> List.exists (fun r ->
                             not r.Passed && r.Law.StartsWith "two proposals bringing in the same keyed id"))
                        "the off-walk-id clash is admitted, and the law names it"

                testCase "handed arbitrate's applicability, the container arm goes red"
                <| fun _ ->
                    let results =
                        Conformance.keyedArbitrationLawsWith
                            keyw
                            knodew
                            idw
                            containedKGen
                            encK
                            (Ops.footprintKeyed keyw knodew idw)
                            (Ops.canApplyAll knodew idw)
                            2472
                            200

                    Expect.isTrue
                        (results
                         |> List.exists (fun r -> not r.Passed && r.Law.StartsWith "a proposal inserting under a node"))
                        "the insert under a leaf is admitted, and the law names it"

                testCase "a domain with no container capability and no keyed position passes VACUOUSLY and says so"
                <| fun _ ->
                    let declaresNone: KeyedWitness<RNode, string> =
                        { Surface = "Tree.ids over the reference witness"
                          KeyedChildren = fun _ -> []
                          ReplaceKeyedChildren = fun n _ -> n
                          PlaceKeyedChild = fun _ _ -> None
                          IdsUnique = fun t -> Tree.isWellFormed nodew idw t }

                    let results =
                        Conformance.keyedArbitrationLawsAt declaresNone nodew idw opGen encNode 2473 200

                    Expect.isEmpty (failed results) "green"

                    Expect.isTrue
                        (results |> List.exists (fun r -> r.Law.Contains "vacuous BY DECLARATION"))
                        "the adequacy line says which verdict it is" ] ]
