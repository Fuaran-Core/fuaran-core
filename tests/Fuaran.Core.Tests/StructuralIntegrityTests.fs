module Fuaran.Core.Tests.StructuralIntegrityTests

// Phase 313 — the structural-integrity strand: a parent-to-child containment grammar the engine,
// the diff, arbitration, the kit and a stock validator family all read through one definition, and
// a reference witness whose defects the validator reports, whose removals the engine refuses while
// still referenced, and whose references the footprint reads.

open System
open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// ---- the grammar fixture: a document of sections, paragraphs and notes ----

/// `doc` holds sections and notes, a `section` holds sections and paragraphs, and paragraphs and
/// notes hold nothing. A kind the grammar does not name holds anything.
let grammar (kind: string) : string list option =
    match kind with
    | "doc" -> Some [ "section"; "note" ]
    | "section" -> Some [ "section"; "para" ]
    | "para"
    | "note" -> Some []
    | _ -> None

let private grammarKinds = [ "section"; "para"; "note" ]

let private leafKind (n: RNode) = n.Kind = "para" || n.Kind = "note"

/// Trees whose children are drawn from every kind, so a drawn tree breaks the grammar about as
/// often as it keeps it, and fresh nodes of every kind — the refusal is DRAWN on both sides.
let genGrammarTree (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable counter = 0
    let mutable r = rng

    let draw n =
        let v, r' = ConfRng.intBelow n r
        r <- r'
        v

    let rec build depth (kind: string) =
        let id = sprintf "g%d" counter
        counter <- counter + 1

        if kind = "section" && depth > 0 then
            let n = draw 3
            RNode.node id kind [ for _ in 1..n -> build (depth - 1) (List.item (draw 3) grammarKinds) ]
        else
            RNode.leaf id kind "v"

    let n = 1 + draw 3

    let root =
        RNode.node "root" "doc" [ for _ in 1..n -> build 2 (List.item (draw 3) grammarKinds) ]

    root, r

let private genGrammarFresh (existing: Set<string>) (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable r = rng

    let rec pick () =
        let v, r' = ConfRng.next r
        r <- r'
        let id = sprintf "f%d" (v % 100000)
        if existing.Contains id then pick () else id

    let id = pick ()
    let k, r' = ConfRng.intBelow 3 r
    RNode.leaf id (List.item k grammarKinds) "x", r'

let grammarGen: OpGen<RNode, string> =
    { Tree = genGrammarTree
      FreshNode = genGrammarFresh
      CanHold = Some(leafKind >> not) }

let private canHold (n: RNode) = not (leafKind n)

/// root(doc) ─ s1(section) ─ p1(para)
///           └ n1(note)
let private doc () =
    RNode.node
        "root"
        "doc"
        [ RNode.node "s1" "section" [ RNode.leaf "p1" "para" "v" ]
          RNode.leaf "n1" "note" "v" ]

// ---- the reference fixture: references written in a node's value ----

/// A node refers to the ids its value lists after `ref:`, and every node declares its own id.
let refw: RefWitness<RNode, string> =
    { RefsOf =
        fun n ->
            if n.Value.StartsWith "ref:" then
                n.Value.Substring(4).Split(',', StringSplitOptions.RemoveEmptyEntries)
                |> List.ofArray
            else
                []
      DeclsOf = fun n -> [ n.Id ] }

/// The reference generator's trees: the op kit's shapes, every node given up to two references to
/// ids of the same tree, one in six to an id nobody declares.
let genRefTree (rng: ConfRng.T) : RNode * ConfRng.T =
    let t, r1 = ConformanceTests.genTree rng
    let ids = Tree.ids nodew t |> Array.ofList
    let mutable r = r1

    let draw n =
        let v, r' = ConfRng.intBelow n r
        r <- r'
        v

    let rec assign (n: RNode) =
        let count = draw 3

        let refs =
            [ for _ in 1..count -> if draw 6 = 0 then "ghost" else ids.[draw ids.Length] ]

        let value =
            if List.isEmpty refs then
                n.Value
            else
                "ref:" + String.concat "," refs

        let kids = n.Children |> List.map assign

        { n with
            Value = value
            Children = kids }

    let assigned = assign t
    assigned, r

let refGen: OpGen<RNode, string> =
    { Tree = genRefTree
      FreshNode = ConformanceTests.opGen.FreshNode
      CanHold = None }

let private withRefs (refs: string list) (n: RNode) =
    { n with
        Value = "ref:" + String.concat "," refs }

/// root ─ a ─ a1 (refers to b)
///      └ b ─ b1 (refers to a1)
let private linked () =
    RNode.node
        "root"
        "section"
        [ RNode.node "a" "section" [ RNode.leaf "a1" "para" "" |> withRefs [ "b" ] ]
          RNode.node "b" "section" [ RNode.leaf "b1" "para" "" |> withRefs [ "a1" ] ] ]

[<Tests>]
let tests =
    testList
        "Structural integrity (Phase 313)"
        [ testList
              "the containment grammar"
              [ testCase "isLegalChild reads the grammar; None admits anything"
                <| fun _ ->
                    Expect.isTrue (Ops.isLegalChild grammar "doc" "section") "listed"
                    Expect.isFalse (Ops.isLegalChild grammar "doc" "para") "not listed"
                    Expect.isFalse (Ops.isLegalChild grammar "para" "para") "a kind that holds nothing"
                    Expect.isTrue (Ops.isLegalChild grammar "unknown" "anything") "an unnamed kind holds anything"

                testCase "an illegal insert is IllegalChild naming the child, the parent and the legal children"
                <| fun _ ->
                    Expect.equal
                        (Ops.applyGrammar
                            grammar
                            canHold
                            nodew
                            idw
                            (InsertChild("root", RNode.leaf "x" "para" ""))
                            (doc ()))
                        (Error(IllegalChild("x", "para", "root", "doc", [ "section"; "note" ])))
                        "a paragraph under the document"

                    Expect.isOk
                        (Ops.applyGrammar
                            grammar
                            canHold
                            nodew
                            idw
                            (InsertChild("s1", RNode.leaf "x" "para" ""))
                            (doc ()))
                        "a paragraph under a section"

                testCase "the graft's interior is checked after its own placement"
                <| fun _ ->
                    let graft = RNode.node "s2" "section" [ RNode.leaf "x" "note" "" ]

                    Expect.equal
                        (Ops.applyGrammar grammar canHold nodew idw (InsertChild("root", graft)) (doc ()))
                        (Error(IllegalChild("x", "note", "s2", "section", [ "section"; "para" ])))
                        "a note inside the grafted section"

                testCase "every engine refusal keeps its class: the container check fires first"
                <| fun _ ->
                    Expect.equal
                        (Ops.applyGrammar
                            grammar
                            canHold
                            nodew
                            idw
                            (InsertChild("p1", RNode.leaf "x" "note" ""))
                            (doc ()))
                        (Error(NotAContainer("p1", "para")))
                        "a paragraph cannot hold children at all"

                    Expect.equal
                        (Ops.applyGrammar
                            grammar
                            canHold
                            nodew
                            idw
                            (InsertChild("nope", RNode.leaf "x" "note" ""))
                            (doc ()))
                        (Ops.applyContained canHold nodew idw (InsertChild("nope", RNode.leaf "x" "note" "")) (doc ()))
                        "an unknown parent is the engine's envelope"

                testCase "a move creates one pair: the moved node under its new parent"
                <| fun _ ->
                    Expect.equal
                        (Ops.applyGrammar grammar canHold nodew idw (MoveNode("p1", "root")) (doc ()))
                        (Error(IllegalChild("p1", "para", "root", "doc", [ "section"; "note" ])))
                        "a paragraph moved under the document"

                testCase "an in-place rewrite that changes a kind is checked under its parent and over its children"
                <| fun _ ->
                    Expect.equal
                        (Ops.applyGrammar grammar canHold nodew idw (UpdateNode(RNode.node "s1" "chapter" [])) (doc ()))
                        (Error(IllegalChild("s1", "chapter", "root", "doc", [ "section"; "note" ])))
                        "a chapter the document may not hold"

                    let g2 k =
                        match k with
                        | "doc" -> None
                        | other -> grammar other

                    Expect.equal
                        (Ops.applyGrammar g2 canHold nodew idw (UpdateNode(RNode.node "s1" "box" [])) (doc ()))
                        (Ok(
                            RNode.node
                                "root"
                                "doc"
                                [ RNode.node "s1" "box" [ RNode.leaf "p1" "para" "v" ]
                                  RNode.leaf "n1" "note" "v" ]
                        ))
                        "a kind the grammar does not name holds the kept children"

                testCase "a batch is all-or-nothing and names the first illegal step"
                <| fun _ ->
                    let script =
                        Batch [ InsertChild("s1", RNode.leaf "x" "para" ""); MoveNode("x", "root") ]

                    Expect.equal
                        (Ops.applyGrammar grammar canHold nodew idw script (doc ()))
                        (Error(IllegalChild("x", "para", "root", "doc", [ "section"; "note" ])))
                        "the move half"

                testCase "the sequence and dry-run forms answer the engine"
                <| fun _ ->
                    let ops =
                        [ InsertChild("s1", RNode.leaf "x" "para" "")
                          InsertChild("root", RNode.leaf "y" "para" "") ]

                    match Ops.applyAllGrammar grammar canHold nodew idw ops (doc ()) with
                    | Error(1, IllegalChild("y", _, "root", _, _), partial) ->
                        Expect.isSome (Tree.tryFind nodew idw "x" partial) "the accepted prefix is kept"
                    | other -> failtestf "expected the second op refused, got %A" other

                    Expect.equal
                        (Ops.canApplyAllGrammar grammar canHold nodew idw ops (doc ()))
                        (Error(1, IllegalChild("y", "para", "root", "doc", [ "section"; "note" ])))
                        "the dry run"

                    Expect.equal
                        (Ops.canApplyGrammar grammar canHold nodew idw ops.Head (doc ()))
                        (Ok())
                        "the single-op dry run"

                testCase "no grammar is applyContained"
                <| fun _ ->
                    let op = MoveNode("p1", "root")

                    Expect.equal
                        (Ops.applyGrammar (fun _ -> None) canHold nodew idw op (doc ()))
                        (Ops.applyContained canHold nodew idw op (doc ()))
                        "identical"

                testCase "the diff refuses an after holding an illegal pair, after every contained refusal"
                <| fun _ ->
                    let after =
                        RNode.node
                            "root"
                            "doc"
                            [ RNode.node "s1" "section" []
                              RNode.leaf "p1" "para" "v"
                              RNode.leaf "n1" "note" "v" ]

                    Expect.equal
                        (Diff.toOpsGrammar grammar canHold nodew idw (doc ()) after)
                        (Error(Diff.IllegalChildInTree("p1", "para", "root", "doc", [ "section"; "note" ])))
                        "the first illegal pair"

                    let legalAfter =
                        RNode.node
                            "root"
                            "doc"
                            [ RNode.node "s1" "section" [ RNode.leaf "p1" "para" "v"; RNode.leaf "p2" "para" "w" ]
                              RNode.leaf "n1" "note" "v" ]

                    match Diff.toOpsGrammar grammar canHold nodew idw (doc ()) legalAfter with
                    | Ok ops ->
                        Expect.equal
                            (Ops.applyAllGrammar grammar canHold nodew idw ops (doc ()))
                            (Ok legalAfter)
                            "the script rebuilds the after tree under the grammar"
                    | Error e -> failtestf "refused a legal pair: %A" e

                testCase "arbitration refuses an illegal proposal as Inapplicable, naming the legal children"
                <| fun _ ->
                    let proposals =
                        [ { Id = 1
                            Holder = "a"
                            Ops = [ InsertChild("root", RNode.leaf "x" "para" "") ] }
                          { Id = 2
                            Holder = "b"
                            Ops = [ InsertChild("s1", RNode.leaf "y" "para" "") ] } ]

                    let verdict =
                        Arbitration.arbitrateGrammar grammar canHold nodew idw (doc ()) proposals

                    Expect.equal (verdict.Accepted |> List.map (fun p -> p.Id)) [ 2 ] "the legal one lands"

                    match verdict.Rejected with
                    | [ (p, Inapplicable(0, IllegalChild("x", "para", "root", "doc", legal))) ] ->
                        Expect.equal p.Id 1 "the illegal one"
                        Expect.equal legal [ "section"; "note" ] "with the repair"
                    | other -> failtestf "expected one Inapplicable, got %A" other

                testCase "Validator.containment reports every illegal pair at the child, naming what the parent holds"
                <| fun _ ->
                    let tree =
                        RNode.node
                            "root"
                            "doc"
                            [ RNode.leaf "p0" "para" ""
                              RNode.node "s1" "section" [ RNode.leaf "n2" "note" "" ] ]

                    let defects = (Validator.containment grammar).Run nodew tree

                    Expect.equal (defects |> List.map (fun d -> d.Node)) [ Some "p0"; Some "n2" ] "both, in preorder"

                    Expect.equal
                        (defects |> List.map (fun d -> d.Code))
                        [ "TREE-ILLEGALCHILD"; "TREE-ILLEGALCHILD" ]
                        "coded"

                    Expect.equal
                        defects.Head.Message
                        "a para cannot sit under a doc; a doc holds: section, note"
                        "the message names the legal children"

                    Expect.equal
                        ((Validator.containment grammar).Run
                            nodew
                            (RNode.node "root" "para" [ RNode.leaf "x" "para" "" ]))
                            .Head.Message
                        "a para cannot sit under a para; a para holds no children"
                        "a kind that holds nothing"

                testCase "the containment family is green at the reference grammar"
                <| fun _ ->
                    let results = Conformance.containmentLaws grammar nodew idw grammarGen 313 200

                    for r in results do
                        Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

                testCase "a grammar the generator never breaks is reported by the guard, not passed"
                <| fun _ ->
                    let results =
                        Conformance.containmentLaws (fun _ -> None) nodew idw grammarGen 313 50

                    Expect.isFalse (results |> List.forall (fun r -> r.Passed)) "the grammar-refusal guard reds" ]

          testList
              "the reference witness"
              [ testCase "referenceDefects reports dangling references, unused declarations and cycles"
                <| fun _ ->
                    let tree =
                        RNode.node
                            "root"
                            "section"
                            [ RNode.leaf "a" "para" "" |> withRefs [ "b"; "ghost" ]
                              RNode.leaf "b" "para" "" |> withRefs [ "a" ]
                              RNode.leaf "c" "para" "" ]

                    let defects = Validator.referenceDefects refw idw nodew tree

                    Expect.equal
                        defects
                        [ Validator.ReferenceDefect.DanglingReference("a", "ghost")
                          Validator.ReferenceDefect.UnusedDeclaration("root", "root")
                          Validator.ReferenceDefect.UnusedDeclaration("c", "c")
                          Validator.ReferenceDefect.ReferenceCycle [ "b"; "a" ] ]
                        "in the documented order"

                    let family = (Validator.referenceIntegrity refw idw).Run nodew tree

                    Expect.equal
                        (family |> List.map (fun d -> d.Code, d.Severity))
                        [ "REF-DANGLING", Severity.Error
                          "REF-UNUSED", Severity.Warning
                          "REF-UNUSED", Severity.Warning
                          "REF-CYCLE", Severity.Error ]
                        "coded and graded"

                testCase "a self-reference is a cycle"
                <| fun _ ->
                    let tree =
                        RNode.node "root" "section" [ RNode.leaf "a" "para" "" |> withRefs [ "a" ] ]

                    Expect.contains
                        (Validator.referenceDefects refw idw nodew tree)
                        (Validator.ReferenceDefect.ReferenceCycle [ "a" ])
                        "the self-referential node"

                testCase
                    "a forward reference is a later branch below the common ancestor, never an ancestor or descendant"
                <| fun _ ->
                    let tree =
                        RNode.node
                            "root"
                            "section"
                            [ RNode.node
                                  "a"
                                  "section"
                                  [ RNode.leaf "a1" "para" "" |> withRefs [ "b1"; "a"; "a2" ]
                                    RNode.leaf "a2" "para" "" ]
                              RNode.node "b" "section" [ RNode.leaf "b1" "para" "" |> withRefs [ "a1" ] ] ]
                        |> fun t -> { t with Value = "ref:b" }

                    Expect.equal
                        (Validator.forwardReferences refw idw nodew tree)
                        [ Validator.ReferenceDefect.ForwardReference("a1", "b1", "b1")
                          Validator.ReferenceDefect.ForwardReference("a1", "a2", "a2") ]
                        "b1 and the later sibling a2 are forward; the ancestor a and the root's descendant b are not"

                    Expect.equal
                        ((Validator.referenceOrder refw idw).Run nodew tree
                         |> List.map (fun d -> d.Code, d.Node))
                        [ "REF-FORWARD", Some "a1"; "REF-FORWARD", Some "a1" ]
                        "the opt-in family"

                testCase "a remove that orphans a reference is StillReferenced, naming every referrer"
                <| fun _ ->
                    Expect.equal
                        (Ops.applyReferenced refw (fun _ -> None) (fun _ -> true) nodew idw (RemoveNode "b") (linked ()))
                        (Error(StillReferenced("b", [ "a1" ])))
                        "a1 refers to b"

                    Expect.equal
                        (Ops.applyReferenced refw (fun _ -> None) (fun _ -> true) nodew idw (RemoveNode "a") (linked ()))
                        (Error(StillReferenced("a", [ "b1" ])))
                        "b1 refers to a1, below a — the subtree's declarations count"

                    let unlinked =
                        match Ops.apply nodew idw (UpdateNode(RNode.leaf "b1" "para" "")) (linked ()) with
                        | Ok t -> t
                        | Error e -> failwithf "the unlinking update was refused: %A" e

                    Expect.isOk
                        (Ops.applyReferenced refw (fun _ -> None) (fun _ -> true) nodew idw (RemoveNode "a") unlinked)
                        "nothing outside a refers into it any more (a1's own reference leaves with it)"

                    Expect.equal
                        (Ops.applyReferenced
                            refw
                            (fun _ -> None)
                            (fun _ -> true)
                            nodew
                            idw
                            (RemoveNode "zz")
                            (linked ()))
                        (Ops.apply nodew idw (RemoveNode "zz") (linked ()))
                        "an engine refusal keeps its class"

                testCase "the reference sequence and dry-run forms answer the engine"
                <| fun _ ->
                    let unlinkB1 = UpdateNode(RNode.leaf "b1" "para" "")

                    Expect.equal
                        (Ops.canApplyAllReferenced
                            refw
                            (fun _ -> None)
                            (fun _ -> true)
                            nodew
                            idw
                            [ unlinkB1; RemoveNode "b" ]
                            (linked ()))
                        (Error(1, StillReferenced("b", [ "a1" ])))
                        "the second step: a1 still refers to b"

                    match
                        Ops.applyAllReferenced
                            refw
                            (fun _ -> None)
                            (fun _ -> true)
                            nodew
                            idw
                            [ unlinkB1; RemoveNode "a" ]
                            (linked ())
                    with
                    | Ok t -> Expect.isNone (Tree.tryFind nodew idw "a" t) "once b1 lets go, a's subtree goes"
                    | Error e -> failtestf "refused: %A" e

                    Expect.equal
                        (Ops.canApplyAllReferenced
                            refw
                            (fun _ -> None)
                            (fun _ -> true)
                            nodew
                            idw
                            [ RemoveNode "a1"; RemoveNode "b" ]
                            (linked ()))
                        (Error(0, StillReferenced("a1", [ "b1" ])))
                        "a1 is itself referenced by b1"

                    Expect.equal
                        (Ops.canApplyReferenced
                            refw
                            (fun _ -> None)
                            (fun _ -> true)
                            nodew
                            idw
                            (RemoveNode "b")
                            (linked ()))
                        (Error(StillReferenced("b", [ "a1" ])))
                        "the single-op dry run"

                testCase "footprintReferenced reads the references a script writes, so the race is refused by name"
                <| fun _ ->
                    let a1 = Tree.tryFind nodew idw "a1" (linked ()) |> Option.get
                    let writer = Ops.footprintReferenced refw nodew idw [ UpdateNode a1 ]
                    let remover = Ops.footprintReferenced refw nodew idw [ RemoveNode "b" ]

                    Expect.isTrue (writer.Reads.Contains "b") "the referenced id is read"
                    Expect.isFalse (Ops.independent writer remover) "dependent"

                    Expect.contains
                        (Ops.interference writer remover)
                        (Interference.RightWritesLeftReads(Set.singleton "b"))
                        "named for the reference"

                testCase "a domain op that writes a reference reads it through Footprint.reading"
                <| fun _ ->
                    let remove = Ops.footprint nodew idw [ RemoveNode "b" ]

                    Expect.isTrue
                        (Ops.independent (Footprint.contentEdit "a1") remove)
                        "the race the plain builder admits"

                    Expect.isFalse
                        (Ops.independent
                            (Footprint.union (Footprint.contentEdit "a1") (Footprint.reading [ "b" ]))
                            remove)
                        "closed by the read"

                testCase "arbitrateReferenced refuses a remove that would orphan the base's references"
                <| fun _ ->
                    let verdict =
                        Arbitration.arbitrateReferenced
                            refw
                            (fun _ -> None)
                            (fun _ -> true)
                            nodew
                            idw
                            (linked ())
                            [ { Id = 1
                                Holder = "a"
                                Ops = [ RemoveNode "b" ] } ]

                    match verdict.Rejected with
                    | [ (_, Inapplicable(0, StillReferenced("b", [ "a1" ]))) ] -> ()
                    | other -> failtestf "expected StillReferenced, got %A" other

                testCase "the reference family is green at the reference witness"
                <| fun _ ->
                    let results = Conformance.referenceLaws refw nodew idw refGen 313 200

                    for r in results do
                        Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

                testCase "a witness with no references is reported by the guard, not passed"
                <| fun _ ->
                    let none: RefWitness<RNode, string> =
                        { RefsOf = fun _ -> []
                          DeclsOf = fun n -> [ n.Id ] }

                    let results = Conformance.referenceLaws none nodew idw refGen 313 50
                    Expect.isFalse (results |> List.forall (fun r -> r.Passed)) "the reference-arm guard reds" ]

          testList
              "the envelopes and the graph"
              [ testCase "the two new rejections' code, guidance and canonical encoding"
                <| fun _ ->
                    let nouns =
                        { Node = "block"
                          Root = "document root" }

                    let cases: (Rejection<string> * string * string * string list * string) list =
                        [ IllegalChild("x", "para", "d", "doc", [ "section"; "note" ]),
                          "illegalChild",
                          "'x' (para) cannot sit under 'd' (doc)",
                          [ "section"; "note" ],
                          """{"$type":"illegalChild","child":"x","childKind":"para","legal":["section","note"],"parent":"d","parentKind":"doc"}"""
                          StillReferenced("b", [ "a1"; "c" ]),
                          "stillReferenced",
                          "'b' declares what other blocks still reference; remove or retarget the references first",
                          [ "a1"; "c" ],
                          """{"$type":"stillReferenced","referrers":["a1","c"],"target":"b"}""" ]

                    for r, code, message, alternatives, wire in cases do
                        Expect.equal (Rejection.code r) code (sprintf "code of %A" r)
                        let g = Rejection.explain id nouns r
                        Expect.equal g.Message message (sprintf "message of %A" r)
                        Expect.equal g.Alternatives alternatives (sprintf "alternatives of %A" r)
                        Expect.equal (RejectionCodec.render id r) wire (sprintf "encoding of %A" r)

                testCase "Graph re-exports Propagation's sort, cycle and dependents"
                <| fun _ ->
                    let deps =
                        Map.ofList
                            [ "a", Set.ofList [ "b" ]
                              "b", Set.ofList [ "a" ]
                              "c", Set.ofList [ "a"; "ghost" ] ]

                    Expect.equal (Graph.sort deps) (Propagation.sort deps) "sort"
                    Expect.equal (Graph.cycleThrough "a" deps) (Propagation.cycleThrough "a" deps) "cycleThrough"
                    Expect.isSome (Graph.cycleThrough "a" deps) "a is on the cycle"
                    Expect.equal (Graph.dependents deps) (Propagation.dependents deps) "dependents" ] ]
