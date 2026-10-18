module Fuaran.Core.Tests.KeyedGateDiffTests

// Phase 496 — three questions about the keyed positions, asked of the shipped engine over the keyed
// reference domain (`ConformanceTests.keyw`: `KNode` holds a case table beside its structural
// `Children`). Each test pins the answer measured today, and each is a stated limit
// (`WriteGate.fs`'s and `Diff.fs`'s headers). They are GO-RED fixtures for the keyed entry the gate
// and the diff do not have yet: when one lands, these answers flip and the tests become its laws.
//
//   (a) A lock on a holder and a write inside its keyed subtree: does `WriteGate.decide` refuse?
//   (b) `Diff.toOpsWith` over the PLAIN witness, with a change below a keyed child: is it seen?
//   (c) `Diff.toOpsWith` over `Tree.traversal`: does step 1's `ReplaceChildren n []` keep the
//       arity `KeyedWitness.ReplaceKeyedChildren` promises?

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
              "(a) a lock on a holder does not reach its keyed subtree"
              [ testCase "decide over the plain witness admits a write inside the locked holder's keyed subtree"
                <| fun _ ->
                    let t = sample ()
                    let gate = WriteGate.lockOnly [ "sw" ]
                    let op = UpdateNode(k "c1a" "heading" [] [])
                    // `chainOf` walks `Tree.path` over the plain witness, which does not reach `c1a`,
                    // so the target's chain is `[c1a]` alone and the lock on `sw` is never read.
                    Expect.equal (WriteGate.decide gate knodew idw op t) (Ok()) "the gate admits the write"

                    Expect.equal
                        (WriteGate.targetsOf knodew idw op t)
                        (Set.singleton "c1a")
                        "the target is named; only its ancestor chain is missing"

                    // ...and the keyed engine then lands it: gate-then-keyed-apply writes under a lock.
                    match Ops.applyContainedKeyed keyw canHoldAll knodew idw op t with
                    | Ok t' ->
                        Expect.equal
                            (Tree.tryFind (Tree.traversal knodew keyw) idw "c1a" t' |> Option.map _.Kind)
                            (Some "heading")
                            "the locked holder's keyed subtree was written"
                    | Error e -> failtestf "the keyed engine refused the write: %A" e

                testCase "the gate's own apply path stays safe: applyGated cannot reach the keyed node"
                <| fun _ ->
                    let t = sample ()
                    let op = UpdateNode(k "c1a" "heading" [] [])

                    match WriteGate.applyGated (WriteGate.lockOnly [ "sw" ]) knodew idw op t with
                    | Error(GatedApplyFailure.Rejected _) -> ()
                    | other -> failtestf "expected the plain reducer to refuse an unreachable target, got %A" other

                testCase "a lock on the keyed node itself is read: the limit is the ancestor chain, not the target"
                <| fun _ ->
                    let t = sample ()
                    let op = UpdateNode(k "c1a" "heading" [] [])

                    Expect.equal
                        (WriteGate.decide (WriteGate.lockOnly [ "c1a" ]) knodew idw op t)
                        (Error(WriteDenial.Locked("c1a", "c1a")))
                        "locked by itself" ]

          testList
              "(b) the plain diff does not see below a keyed child"
              [ testCase "a change inside a keyed subtree emits no op, and the script does not reach after"
                <| fun _ ->
                    let before = sample ()

                    let after =
                        k
                            "root"
                            "section"
                            [ leaf "a"
                              k "sw" "switch" [] [ "one", k "c1" "section" [ k "c1a" "heading" [] [] ] [] ] ]
                            []

                    match Diff.toOpsWith ownContent knodew idw before after with
                    | Ok ops ->
                        Expect.isEmpty ops "no node the plain walk reaches changed its own content"

                        Expect.notEqual
                            (Ops.applyAll knodew idw ops before)
                            (Ok after)
                            "so the script lands on before, not after"
                    | Error e -> failtestf "the diff refused: %A" e

                testCase "an encoder that reads the keyed positions sees the change only as the holder's update"
                <| fun _ ->
                    let before = sample ()

                    let after =
                        k
                            "root"
                            "section"
                            [ leaf "a"
                              k "sw" "switch" [] [ "one", k "c1" "section" [ k "c1a" "heading" [] [] ] [] ] ]
                            []
                    // `sprintf "%A"` over the shell reads the case table, so the holder's shell differs.
                    match Diff.toOpsWith (sprintf "%A") knodew idw before after with
                    | Ok ops ->
                        Expect.equal
                            (ops
                             |> List.map (function
                                 | UpdateNode n -> "UpdateNode " + n.Id
                                 | other -> sprintf "%A" other))
                            [ "UpdateNode sw" ]
                            "the whole keyed subtree rides one UpdateNode of its holder"

                        Expect.equal (Ops.applyAll knodew idw ops before) (Ok after) "which does reach after"
                    | Error e -> failtestf "the diff refused: %A" e ]

          testList
              "(c) the diff over Tree.traversal breaks the keyed arity"
              [ testCase "step 1's empty ReplaceChildren hands ReplaceKeyedChildren a list shorter than its arity"
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
                    // `fresh` is ADDED and holds a case, so step 1 inserts its shell through the
                    // traversal's `ReplaceChildren fresh []`.
                    let after =
                        k
                            "root"
                            "section"
                            [ leaf "a"
                              k "sw" "switch" [] [ "one", k "c1" "section" [ leaf "c1a" ] [] ]
                              k "fresh" "switch" [] [ "x", leaf "fx" ] ]
                            []

                    let tw = Tree.traversal knodew strict

                    match Diff.toOpsWith ownContent tw idw before after with
                    | Ok ops ->
                        // Step 1's shell of the added holder, AND step 0's content shells of the
                        // survivor holder `sw` (`shellOf` is the same `ReplaceChildren n []`).
                        Expect.equal
                            (List.distinct violations |> List.sort)
                            [ "fresh", 1, 0; "sw", 1, 0 ]
                            "every holder was rebuilt with 0 keyed children against an arity of 1"

                        // This domain answers the short list by leaving the cases in place, so the
                        // "shell" still carries `fx` and the script inserts `fx` a second time.
                        match ops with
                        | InsertChild(_, shell) :: _ ->
                            Expect.equal (shell.Cases |> List.map (snd >> _.Id)) [ "fx" ] "the shell is not a shell"
                        | other -> failtestf "expected the added holder's shell first, got %A" other

                        let landed =
                            (Ok before, ops)
                            ||> List.fold (fun acc op ->
                                acc |> Result.bind (Ops.applyContainedKeyed keyw canHoldAll knodew idw op))

                        Expect.notEqual
                            landed
                            (Ok after)
                            "and the script does not land on after through the keyed engine"
                    | Error e -> failtestf "the diff refused: %A" e ] ]
