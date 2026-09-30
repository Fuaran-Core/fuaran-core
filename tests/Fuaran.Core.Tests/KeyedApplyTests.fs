module Fuaran.Core.Tests.KeyedApplyTests

// Phase 286 — the keyed witness reaches the apply path. `KeyedWitness.KeyedChildren` hands the
// engine the nodes a domain holds in keyed positions; `Tree.traversal` walks them, the keyed
// `wellFormed` / `graftWellFormed` see them, and `Ops.applyContainedKeyed` refuses a `DuplicateId`
// over them and addresses nodes below them — while the structural ops still edit `Children` alone.
//
// The domain is `ConformanceTests`' keyed reference: `KNode` holds a case table (`Cases`) beside
// its structural `Children`, and `ConformanceTests.keyw` declares it.

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

/// The M3 shape: a switch holding `c1` in a case, whose own subtree holds `c1a` structurally and a
/// nested case `c1k`.
///
///   root
///   ├─ a
///   └─ sw  (case "one" -> c1 [ c1a ]  (case "inner" -> c1k))
let private sample () =
    k "root" [ leaf "a"; k "sw" [] [ "one", k "c1" [ leaf "c1a" ] [ "inner", leaf "c1k" ] ] ] []

let private ids (xs: string list) = xs

let private canHoldAll (_: KNode) = true

let private applyK op root =
    Ops.applyContainedKeyed keyw canHoldAll knodew idw op root

let private applyU op root =
    Ops.applyContained canHoldAll knodew idw op root

/// The keyed reference with no keyed position declared — the `fun _ -> []` domain.
let private noKeyedK: KeyedWitness<KNode, string> =
    { keyw with
        KeyedChildren = fun _ -> []
        ReplaceKeyedChildren = fun n _ -> n }

/// A deeper keyed generator than `ConformanceTests.kGen`: a case node carries its own children and
/// cases, so the keyed walk descends BELOW a keyed position, and the traversal's rebuild is
/// exercised there.
let private genDeep (rng: ConfRng.T) : KNode * ConfRng.T =
    let mutable counter = 0
    let mutable r = rng

    let draw n =
        let v, r' = ConfRng.intBelow n r
        r <- r'
        v

    let fresh () =
        let id = sprintf "d%d" counter
        counter <- counter + 1
        id

    let rec build depth =
        let id = fresh ()

        let kids =
            if depth <= 0 then
                []
            else
                [ for _ in 1 .. draw 3 -> build (depth - 1) ]

        let cases =
            if depth <= 0 then
                []
            else
                [ for i in 1 .. draw 3 -> sprintf "case%d" i, build (depth - 1) ]

        k id kids cases

    build 3, r

let deepGen: OpGen<KNode, string> =
    { Tree = genDeep
      FreshNode = (kGen: OpGen<KNode, string>).FreshNode
      CanHold = None }

let private failed (results: LawResult list) =
    results |> List.filter (fun r -> not r.Passed)

[<Tests>]
let tests =
    testList
        "Keyed apply (Phase 286)"
        [ testList
              "the keyed walk"
              [ testCase "idsKeyed reaches every keyed position and below, keyed children first"
                <| fun _ ->
                    Expect.equal
                        (Tree.idsKeyed knodew keyw (sample ()))
                        (ids [ "root"; "a"; "sw"; "c1"; "c1k"; "c1a" ])
                        "node, its keyed children's subtrees, then its structural children's"

                    Expect.equal
                        (Tree.ids knodew (sample ()))
                        (ids [ "root"; "a"; "sw" ])
                        "the unkeyed walk still stops at the structural surface"

                testCase "preorderKeyed, foldKeyed and keyedIds agree with idsKeyed"
                <| fun _ ->
                    let t = sample ()

                    Expect.equal
                        (Tree.preorderKeyed knodew keyw t |> List.map _.Id)
                        (Tree.idsKeyed knodew keyw t)
                        "preorderKeyed"

                    Expect.equal
                        (Tree.foldKeyed knodew keyw (fun acc n -> acc + 1) 0 t)
                        6
                        "foldKeyed visits every node the keyed walk reaches"

                    let sw = (Tree.tryFind (Tree.traversal knodew keyw) idw "sw" t).Value
                    Expect.equal (Tree.keyedIds knodew keyw sw) [ "c1" ] "keyedIds is the derived HasKeyedChildren"

                testCase "wellFormedKeyed names an id held in a keyed position AND the surface; wellFormed cannot"
                <| fun _ ->
                    let t = k "root" [ leaf "x"; k "sw" [] [ "one", leaf "x" ] ] []
                    Expect.equal (Tree.wellFormed knodew idw t) Tree.Structural "blind to the case"

                    Expect.equal
                        (Tree.wellFormedKeyed knodew keyw idw t)
                        (Tree.RepeatedId "x")
                        "the keyed walk sees both"

                testCase "graftWellFormedKeyed sees keyed positions on both sides of the graft"
                <| fun _ ->
                    let t = sample ()
                    // the graft carries c1a (held below a keyed position of the tree) in a case of its own
                    let graft = k "g" [] [ "one", leaf "c1a" ]
                    Expect.equal (Tree.graftWellFormed knodew idw graft t) Tree.Structural "blind"

                    Expect.equal (Tree.graftWellFormedKeyed knodew keyw idw graft t) (Tree.RepeatedId "c1a") "seen"

                testCase "a domain that declares no keyed position gets the unkeyed answers"
                <| fun _ ->
                    let t = sample ()
                    Expect.equal (Tree.idsKeyed knodew noKeyedK t) (Tree.ids knodew t) "ids"

                    Expect.equal
                        (Tree.wellFormedKeyed knodew noKeyedK idw t)
                        (Tree.wellFormed knodew idw t)
                        "wellFormed"

                    Expect.equal
                        (Tree.graftWellFormedKeyed knodew noKeyedK idw (leaf "a") t)
                        (Tree.graftWellFormed knodew idw (leaf "a") t)
                        "graftWellFormed"

                testCase "every navigator reaches a node below a keyed position through the traversal"
                <| fun _ ->
                    let t = sample ()
                    let tw = Tree.traversal knodew keyw
                    Expect.isSome (Tree.tryFind tw idw "c1a" t) "tryFind"
                    Expect.isNone (Tree.tryFind knodew idw "c1a" t) "the structural walk cannot"
                    Expect.equal (Tree.parentOf tw idw "c1k" t |> Option.map _.Id) (Some "c1") "parentOf"
                    Expect.equal (Tree.path tw idw "c1a" t) (Some [ "root"; "sw"; "c1"; "c1a" ]) "path"

                    Expect.equal (Tree.ancestors tw idw "c1a" t |> List.map _.Id) [ "root"; "sw"; "c1" ] "ancestors"

                    let ix = Tree.Index.build tw idw t
                    Expect.equal (Tree.Index.path idw "c1k" ix) (Some [ "root"; "sw"; "c1"; "c1k" ]) "Index"

                testCase "updateNode and remapIds rebuild through a keyed position and keep the case table"
                <| fun _ ->
                    let t = sample ()
                    let tw = Tree.traversal knodew keyw

                    let edited =
                        Tree.updateNode tw idw "c1a" (fun n -> { n with Kind = "edited" }) t
                        |> Option.get

                    let c1 = (Tree.tryFind tw idw "c1" edited).Value
                    Expect.equal (c1.Children |> List.map _.Kind) [ "edited" ] "the node below the case was rewritten"
                    Expect.equal (c1.Cases |> List.map fst) [ "inner" ] "the case labels survived the rebuild"

                    let sw = (Tree.tryFind tw idw "sw" edited).Value
                    Expect.equal (sw.Cases |> List.map fst) [ "one" ] "and so did the holder's"

                    let renamed =
                        Tree.remapIds tw (fun id n -> { n with Id = id }) (fun id -> id + "'") t

                    Expect.equal
                        (Tree.idsKeyed knodew keyw renamed)
                        (Tree.idsKeyed knodew keyw t |> List.map (fun i -> i + "'"))
                        "remapIds renames every node the keyed walk reaches" ]

          testList
              "applyContainedKeyed refuses what the keyed walk sees (the acceptance)"
              [ testCase "an id held keyed in the tree, grafted structurally"
                <| fun _ ->
                    let t = sample ()
                    let op = InsertChild("a", leaf "c1k")
                    Expect.isOk (applyU op t) "the unkeyed engine is blind to it"
                    Expect.equal (applyK op t) (Error(DuplicateId "c1k")) "refused"

                testCase "an id held structurally in the tree, grafted keyed"
                <| fun _ ->
                    let t = sample ()
                    let graft = (keyw.PlaceKeyedChild (leaf "g") "a").Value
                    let op = InsertChild("root", graft)
                    Expect.isOk (applyU op t) "the unkeyed engine is blind to it"
                    Expect.equal (applyK op t) (Error(DuplicateId "a")) "refused"

                testCase "an id held keyed on both sides"
                <| fun _ ->
                    let t = sample ()
                    let graft = (keyw.PlaceKeyedChild (leaf "g") "c1").Value
                    let op = InsertChild("a", graft)
                    Expect.isOk (applyU op t) "the unkeyed engine is blind to it"
                    Expect.equal (applyK op t) (Error(DuplicateId "c1")) "refused"

                testCase "the M3 case: an InsertChild whose subtree holds a case with an existing id"
                <| fun _ ->
                    let t = sample ()
                    let incoming = k "sw2" [ leaf "new" ] [ "one", k "caseNode" [ leaf "c1a" ] [] ]
                    let op = InsertChild("root", incoming)
                    Expect.isOk (applyU op t) "admitted by the structural walk"
                    Expect.equal (applyK op t) (Error(DuplicateId "c1a")) "refused by Core"

                    Expect.equal
                        (Ops.canApplyContainedKeyed keyw canHoldAll knodew idw op t)
                        (Error(DuplicateId "c1a"))
                        "and by the dry run"

                testCase "a clean graft is accepted and lands where applyContained puts it"
                <| fun _ ->
                    let t = sample ()
                    let op = InsertChild("root", k "g" [] [ "one", leaf "fresh" ])
                    Expect.equal (applyK op t) (applyU op t) "same tree" ]

          testList
              "applyContainedKeyed addresses below a keyed position and edits the structural surface only"
              [ testCase "insert, reorder, move and update reach a node held in a case"
                <| fun _ ->
                    let t = sample ()

                    let inserted =
                        applyK (InsertChild("c1", leaf "n")) t |> Result.defaultWith (failwithf "%A")

                    let c1 = (Tree.tryFind (Tree.traversal knodew keyw) idw "c1" inserted).Value
                    Expect.equal (c1.Children |> List.map _.Id) [ "c1a"; "n" ] "appended to the structural list"
                    Expect.equal (c1.Cases |> List.map (snd >> _.Id)) [ "c1k" ] "the case table is untouched"

                    Expect.equal
                        (applyU (InsertChild("c1", leaf "n")) t)
                        (Error(UnknownNode("c1", [ "root"; "a"; "sw" ])))
                        "unkeyed: unknown"

                    let reordered =
                        applyK (ReorderChildren("c1", [ "n"; "c1a" ])) inserted
                        |> Result.defaultWith (failwithf "%A")

                    Expect.equal
                        ((Tree.tryFind (Tree.traversal knodew keyw) idw "c1" reordered).Value.Children
                         |> List.map _.Id)
                        [ "n"; "c1a" ]
                        "reorder"

                    let moved = applyK (MoveNode("a", "c1")) t |> Result.defaultWith (failwithf "%A")

                    Expect.equal
                        ((Tree.tryFind (Tree.traversal knodew keyw) idw "c1" moved).Value.Children
                         |> List.map _.Id)
                        [ "c1a"; "a" ]
                        "move into a node held in a case"

                    let updated =
                        applyK (UpdateNode { (leaf "c1") with Kind = "renamed" }) t
                        |> Result.defaultWith (failwithf "%A")

                    let c1' = (Tree.tryFind (Tree.traversal knodew keyw) idw "c1" updated).Value
                    Expect.equal c1'.Kind "renamed" "content rewritten in place"
                    Expect.equal (c1'.Children |> List.map _.Id) [ "c1a" ] "children kept"

                testCase "a node held DIRECTLY in a keyed position cannot be removed or moved by a skeleton op"
                <| fun _ ->
                    let t = sample ()
                    Expect.equal (applyK (RemoveNode "c1") t) (Error(KeyedPosition("c1", "sw"))) "remove"
                    Expect.equal (applyK (MoveNode("c1", "a")) t) (Error(KeyedPosition("c1", "sw"))) "move"

                    Expect.equal
                        (Ops.canApplyContainedKeyed keyw canHoldAll knodew idw (RemoveNode "c1") t)
                        (Error(KeyedPosition("c1", "sw")))
                        "the dry run agrees"

                    Expect.equal
                        (applyU (RemoveNode "c1") t)
                        (Error(UnknownNode("c1", [ "root"; "a"; "sw" ])))
                        "unkeyed: unknown, as always"

                testCase "a node with a structural parent below a keyed position can be removed"
                <| fun _ ->
                    let t = sample ()
                    let removed = applyK (RemoveNode "c1a") t |> Result.defaultWith (failwithf "%A")
                    Expect.equal (Tree.idsKeyed knodew keyw removed) [ "root"; "a"; "sw"; "c1"; "c1k" ] "gone"

                testCase "an UpdateNode payload that carries an id into a keyed position is checked"
                <| fun _ ->
                    let t = sample ()
                    // `sw`'s case table replaced with one whose case holds `a`, which the tree holds
                    let clash = k "sw" [] [ "one", leaf "a" ]
                    Expect.equal (applyK (UpdateNode clash) t) (Error(DuplicateId "a")) "refused"
                    Expect.isOk (applyU (UpdateNode clash) t) "the unkeyed engine is blind to it"
                    // replacing a case with one carrying the SAME ids it replaces is not a collision
                    let same = k "sw" [] [ "renamed", k "c1" [] [ "inner", leaf "c1k" ] ]
                    Expect.isOk (applyK (UpdateNode same) t) "the outgoing keyed subtrees are not held against it"

                testCase "a graft whose keyed subtree carries a non-container holding children is NotAContainer"
                <| fun _ ->
                    let t = sample ()
                    let canHold (n: KNode) = n.Kind <> "leafy"

                    let bad =
                        { (leaf "bad") with
                            Kind = "leafy"
                            Children = [ leaf "under" ] }

                    let op = InsertChild("root", k "g" [] [ "one", bad ])
                    Expect.isOk (Ops.applyContained canHold knodew idw op t) "the structural walk cannot see it"

                    Expect.equal
                        (Ops.applyContainedKeyed keyw canHold knodew idw op t)
                        (Error(NotAContainer("bad", "leafy")))
                        "the keyed walk can" ]

          testList
              "Conformance.keyedApplyLaws"
              [ testCase "the keyed reference witness passes, with every built arm reached"
                <| fun _ ->
                    let results = Conformance.keyedApplyLaws keyw knodew idw deepGen 2860 300

                    match failed results with
                    | [] -> ()
                    | bad -> failtestf "%A" bad

                testCase "the shallow reference generator passes too"
                <| fun _ -> Expect.isEmpty (failed (Conformance.keyedApplyLaws keyw knodew idw kGen 2861 300)) "green"

                testCase "a declaration that hides a position the domain walks FAILS the agreement law"
                <| fun _ ->
                    // declares only the FIRST case of a node: a second case is invisible to the engine
                    let partial =
                        { keyw with
                            KeyedChildren = fun n -> n.Cases |> List.truncate 1 |> List.map snd
                            ReplaceKeyedChildren =
                                fun n ks ->
                                    match n.Cases, ks with
                                    | (label, _) :: rest, [ c ] -> { n with Cases = (label, c) :: rest }
                                    | _ -> n
                            PlaceKeyedChild =
                                fun n id ->
                                    Some
                                        { n with
                                            Cases = n.Cases @ [ id, leaf id ] } }

                    let results = Conformance.keyedApplyLaws partial knodew idw deepGen 2862 300

                    Expect.isTrue
                        (results
                         |> List.exists (fun r -> not r.Passed && r.Law.StartsWith "applyContainedKeyed refuses"))
                        "the agreement law names the gap"

                testCase "a witness that hides its keyed positions and still places into them FAILS the agreement law"
                <| fun _ ->
                    let results = Conformance.keyedApplyLaws noKeyedK knodew idw deepGen 2866 300

                    Expect.isTrue
                        (results
                         |> List.exists (fun r -> not r.Passed && r.Law.StartsWith "applyContainedKeyed refuses"))
                        "named"

                testCase "a ReplaceKeyedChildren that ignores its list FAILS the placement law"
                <| fun _ ->
                    let ignoring =
                        { keyw with
                            ReplaceKeyedChildren = fun n _ -> n }

                    let results = Conformance.keyedApplyLaws ignoring knodew idw deepGen 2863 300

                    Expect.isTrue
                        (results
                         |> List.exists (fun r -> not r.Passed && r.Law.StartsWith "ReplaceKeyedChildren places"))
                        "named"

                testCase "a domain with no keyed position passes VACUOUSLY and says so, and the identity law still runs"
                <| fun _ ->
                    // declaring none means placing none too: a witness that hides the case table
                    // and still places into it is the defect the agreement law finds
                    let declaresNone =
                        { noKeyedK with
                            PlaceKeyedChild = fun _ _ -> None }

                    let results = Conformance.keyedApplyLaws declaresNone knodew idw kGen 2864 300
                    Expect.isEmpty (failed results) "green"

                    Expect.isTrue
                        (results |> List.exists (fun r -> r.Law.Contains "vacuous BY DECLARATION"))
                        "the adequacy line says which verdict it is"

                testCase
                    "the reference RNode domain, which has no keyed position, gets identical answers from both engines"
                <| fun _ ->
                    let none: KeyedWitness<RNode, string> =
                        { Surface = "Tree.ids over the reference witness"
                          KeyedChildren = fun _ -> []
                          ReplaceKeyedChildren = fun n _ -> n
                          PlaceKeyedChild = fun _ _ -> None
                          IdsUnique = fun t -> Tree.isWellFormed nodew idw t }

                    Expect.isEmpty (failed (Conformance.keyedApplyLaws none nodew idw opGen 2865 400)) "green" ] ]
