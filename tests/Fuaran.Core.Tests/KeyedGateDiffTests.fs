module Fuaran.Core.Tests.KeyedGateDiffTests

// Phase 496 — three questions about the keyed positions, asked of the shipped engine over the keyed
// reference domain (`ConformanceTests.keyw`: `KNode` holds a case table beside its structural
// `Children`). Each test pinned the answer measured then, a stated limit (`WriteGate.fs`'s and
// `Diff.fs`'s headers), as a GO-RED fixture for the keyed entry the gate and the diff did not have:
// when one lands, its answers flip and the tests become its laws. (a) flipped with Phase 517's keyed
// gate; (b) and (c) flipped with Phase 518's keyed diff (`Conformance.keyedDiffLawsAt` is the law).
//
//   (a) A lock on a holder and a write inside its keyed subtree: does `WriteGate.decide` refuse?
//   (b) `Diff.toOpsWith` with a change below a keyed child: is it seen?
//   (c) `Diff.toOpsWith` over the keyed walk: is the arity `KeyedWitness.ReplaceKeyedChildren`
//       promises kept at step 0's content shells and step 1's inserted shells?

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ConformanceTests

let private k (id: string) (kind: string) (children: KNode list) (cases: (string * KNode) list) : KNode =
    { Id = id
      Kind = kind
      Children = children
      Cases = cases }

let private leaf (id: string) = k id "para" [] []

/// root ─ a
///      └ sw (case "one" -> c1 ─ c1a)
let private sample () =
    k "root" "section" [ leaf "a"; k "sw" "switch" [] [ "one", k "c1" "section" [ leaf "c1a" ] [] ] ] []

let private canHoldAll (_: KNode) = true

/// A node's OWN content, as an encoder injective over it reads it: id and kind, nothing below.
let private ownContent (n: KNode) = n.Id + ":" + n.Kind

[<Tests>]
let tests =
    testList
        "Keyed write gate and keyed diff (Phase 496)"
        [ testList
              "(a) a lock on a holder covers its keyed subtree (Phase 517)"
              [ testCase "decide under a keyed scope refuses a write inside the locked holder's keyed subtree"
                <| fun _ ->
                    // Phase 496 pinned the OTHER answer here as the limit (the gate walked the plain
                    // witness, so `c1a`'s chain was `[c1a]` and the lock on `sw` was never read).
                    // Phase 517 gives the gate a scope: the chain is read along the keyed walk.
                    let t = sample ()
                    let gate = WriteGate.lockOnly [ "sw" ]
                    let op = UpdateNode(k "c1a" "heading" [] [])
                    let scope = OpScope.keyed keyw canHoldAll knodew idw

                    Expect.equal
                        (WriteGate.decide gate scope op t)
                        (Error(WriteDenial.Locked("c1a", "sw")))
                        "the lock on the holder reaches the node held below its keyed position"

                    Expect.equal (WriteGate.targetsOf scope op t) (Set.singleton "c1a") "the target is named"

                    match WriteGate.applyGated gate Policy.none scope op t with
                    | Error(GatedApplyFailure.Denied(WriteDenial.Locked("c1a", "sw"))) -> ()
                    | other -> failtestf "expected the gate to deny before the keyed engine ran, got %A" other

                testCase "the keyed engine would land the write the gate now refuses"
                <| fun _ ->
                    // the go-red half: without the gate, the keyed engine applies the write, so the
                    // denial above is the gate's doing and not the engine's refusal
                    let t = sample ()
                    let op = UpdateNode(k "c1a" "heading" [] [])

                    match Ops.apply Policy.none (OpScope.keyed keyw canHoldAll knodew idw) op t with
                    | Ok t' ->
                        Expect.equal
                            (Tree.tryFind (Tree.traversal knodew keyw) idw "c1a" t' |> Option.map _.Kind)
                            (Some "heading")
                            "the keyed engine writes the node"
                    | Error e -> failtestf "the keyed engine refused the write: %A" e

                testCase "a plain scope cannot see the keyed node: its gate admits and its reducer refuses"
                <| fun _ ->
                    let t = sample ()
                    let op = UpdateNode(k "c1a" "heading" [] [])
                    let plain = OpScope.plain knodew idw

                    Expect.equal (WriteGate.decide (WriteGate.lockOnly [ "sw" ]) plain op t) (Ok()) "admitted"

                    match WriteGate.applyGated (WriteGate.lockOnly [ "sw" ]) Policy.none plain op t with
                    | Error(GatedApplyFailure.Rejected _) -> ()
                    | other -> failtestf "expected the plain reducer to refuse an unreachable target, got %A" other

                testCase "a lock on the keyed node itself is read under the keyed scope"
                <| fun _ ->
                    let t = sample ()
                    let op = UpdateNode(k "c1a" "heading" [] [])

                    Expect.equal
                        (WriteGate.decide (WriteGate.lockOnly [ "c1a" ]) (OpScope.keyed keyw canHoldAll knodew idw) op t)
                        (Error(WriteDenial.Locked("c1a", "c1a")))
                        "locked by itself" ]

          testList
              "(b) the keyed diff sees below a keyed child (Phase 518)"
              [ testCase "a change inside a keyed subtree is the holder's update, whatever the encoder reads"
                <| fun _ ->
                    // Phase 496 pinned the OTHER answer here as the limit: over the plain witness an
                    // own-content encoder saw nothing, `[]`, and the script landed on `before`.
                    let before = sample ()

                    let after =
                        k
                            "root"
                            "section"
                            [ leaf "a"
                              k "sw" "switch" [] [ "one", k "c1" "section" [ k "c1a" "heading" [] [] ] [] ] ]
                            []

                    let keyed = OpScope.keyed keyw canHoldAll knodew idw

                    match Diff.toOpsWith ownContent Policy.none keyed before after with
                    | Ok ops ->
                        Expect.equal
                            (ops
                             |> List.map (function
                                 | UpdateNode n -> "UpdateNode " + n.Id
                                 | other -> sprintf "%A" other))
                            [ "UpdateNode sw" ]
                            "the keyed subtree is the holder's content, so it rides one UpdateNode of sw"

                        Expect.equal (Ops.applyAll Policy.none keyed ops before) (Ok after) "which reaches after"
                    | Error e -> failtestf "the keyed diff refused: %A" e

                    Expect.equal
                        (Diff.changes ownContent Policy.none keyed before after)
                        (Ok
                            [ { Id = "sw"
                                Kind = Diff.ChangeKind.Changed } ])
                        "and changes reads it off the same pass: the holder Changed"

                testCase "the plain scope still sees no keyed position: its own-content diff is empty"
                <| fun _ ->
                    // the plain scope declares no keyed position, so the case table is invisible to it
                    // by contract; the keyed scope is the form that sees it
                    let before = sample ()

                    let after =
                        k
                            "root"
                            "section"
                            [ leaf "a"
                              k "sw" "switch" [] [ "one", k "c1" "section" [ k "c1a" "heading" [] [] ] [] ] ]
                            []

                    Expect.equal
                        (Diff.toOpsWith ownContent Policy.none (OpScope.plain knodew idw) before after)
                        (Ok [])
                        "nothing the plain walk reaches changed its own content"

                testCase "a node relocated across a keyed boundary is refused as KeyedPosition"
                <| fun _ ->
                    let before = sample ()
                    // `a` leaves the structural surface for a new case of `sw`
                    let after =
                        k
                            "root"
                            "section"
                            [ k "sw" "switch" [] [ "one", k "c1" "section" [ leaf "c1a" ] []; "two", leaf "a" ] ]
                            []

                    Expect.equal
                        (Diff.toOpsWith ownContent Policy.none (OpScope.keyed keyw canHoldAll knodew idw) before after)
                        (Error(KeyedPosition("a", "sw", 1)))
                        "the node, the holder that carries it, the slot" ]

          testList
              "(c) the keyed diff keeps the keyed arity (Phase 518)"
              [ testCase "no shell or rebuild hands ReplaceKeyedChildren a list shorter than its arity"
                <| fun _ ->
                    let mutable violations = []

                    let strict: KeyedWitness<KNode, string> =
                        { keyw with
                            ReplaceKeyedChildren =
                                fun n ks ->
                                    if List.length ks <> List.length n.Cases then
                                        violations <- (n.Id, List.length n.Cases, List.length ks) :: violations

                                    keyw.ReplaceKeyedChildren n ks }

                    let before = sample ()
                    // `fresh` is ADDED and holds a case, so step 1 inserts its shell; `sw` survives,
                    // so step 0 reads its content shell. Phase 496 measured both under-filled
                    // (`[ "fresh", 1, 0; "sw", 1, 0 ]`) when the diff ran over `Tree.traversal`.
                    let after =
                        k
                            "root"
                            "section"
                            [ leaf "a"
                              k "sw" "switch" [] [ "one", k "c1" "section" [ leaf "c1a" ] [] ]
                              k "fresh" "switch" [] [ "x", leaf "fx" ] ]
                            []

                    let keyed = OpScope.keyed strict canHoldAll knodew idw

                    match Diff.toOpsWith ownContent Policy.none keyed before after with
                    | Ok ops ->
                        match ops with
                        | [ InsertChild("root", shell) ] ->
                            Expect.equal
                                (shell.Cases |> List.map (snd >> _.Id))
                                [ "fx" ]
                                "the shell keeps its keyed case"

                            Expect.isEmpty shell.Children "and no structural child"
                        | other -> failtestf "expected the added holder's shell alone, got %A" other

                        Expect.equal (Ops.applyAll Policy.none keyed ops before) (Ok after) "the script lands on after"
                        Expect.isEmpty violations "no rebuild under-filled a keyed position"
                    | Error e -> failtestf "the diff refused: %A" e ] ]
