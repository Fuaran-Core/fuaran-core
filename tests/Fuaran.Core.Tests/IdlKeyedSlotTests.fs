module Fuaran.Core.Tests.IdlKeyedSlotTests

// ---------------------------------------------------------------------------
// Phase 494 — a DECLARED keyed position. A node a kind always carries is a structural child by the
// derived rule; a vocabulary marks a named arm (a boundary's fallback, a switch's default) keyed,
// and the generated witness then has ONE child enumeration, Phase 286's: `children` lists only the
// ordered positions and the keyed walk holds the arm.
//
// The evidence, in order:
//   1. THE GENERATED LAYER. Over the derivations vocabulary's `Attempt` (`body` structural,
//      `recovery` declared keyed): `Tree.ids` does not reach the keyed node and `Tree.idsKeyed`
//      does, on the compiled F# module and on the generated TypeScript run under node.
//   2. THE CONFORMANCE LAW. `Conformance.witnessPairLawsAt` holds the generated pair to a hand
//      pair that agrees, reds — at the holding node — against a hand pair that lists the arm as an
//      ordered child, and says VACUOUS by declaration when no sampled node holds a keyed position.
//   3. THE DECLARATION. `Declare.errors` admits the marker only on a node or node list a kind
//      always carries; the artifact writes `keyed` only when set and reads it back; the classifier
//      grades a marking `host-surface-only` with its own row.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Tests.DeriveGenerated

let private idw: IdWitness<string> =
    { ToString = id
      OfString = id
      Equals = (=) }

let private leaf (id: string) : Node<string> = mkNote id

/// A section holding one `Attempt` whose recovery arm holds a further node.
let private sample () : Node<string> =
    mkSection "root" "plan" [ mkAttempt "attempt" (leaf "body") (mkSection "recovery" "r" [ leaf "recovery-step" ]) ]

let private find (id: string) (root: Node<string>) =
    Tree.preorderKeyed nodeWitness keyedWitness root
    |> List.find (fun n -> n.Id = id)

/// The hand pair a domain would write, agreeing with Phase 286: `recovery` is a keyed arm.
let private handNodes: NodeWitness<Node<string>, string> =
    { nodeWitness with
        Children =
            fun n ->
                match n.Kind with
                | NodeKind.Section s -> s.Children
                | NodeKind.Choice s -> [ s.Otherwise ]
                | NodeKind.Attempt s -> [ s.Body ]
                | _ -> [] }

let private handKeyed: KeyedWitness<Node<string>, string> =
    { keyedWitness with
        Surface = "the hand keyed walk"
        KeyedChildren =
            fun n ->
                (match n.Kind with
                 | NodeKind.Choice s -> s.Branches |> List.map _.Body
                 | NodeKind.Task s -> Option.toList s.Hint @ (s.Extras |> Map.toList |> List.map snd)
                 | NodeKind.Attempt s -> [ s.Recovery ]
                 | _ -> [])
                @ Option.toList n.Annotation }

/// The enumeration this phase retires: the arm listed as an ORDERED child, and so not keyed.
let private staleNodes: NodeWitness<Node<string>, string> =
    { handNodes with
        Children =
            fun n ->
                match n.Kind with
                | NodeKind.Attempt s -> [ s.Body; s.Recovery ]
                | _ -> handNodes.Children n }

let private staleKeyed: KeyedWitness<Node<string>, string> =
    { handKeyed with
        KeyedChildren =
            fun n ->
                match n.Kind with
                | NodeKind.Attempt _ -> Option.toList n.Annotation
                | _ -> handKeyed.KeyedChildren n }

/// Trees with 0..2 attempts, each recovery sometimes a section holding a further node.
let private gen (withAttempts: bool) : OpGen<Node<string>, string> =
    let tree (r: ConfRng.T) =
        let count, r = ConfRng.intBelow 3 r
        let deep, r = ConfRng.intBelow 2 r

        let attempts =
            if withAttempts then
                [ for i in 1..count ->
                      mkAttempt
                          (sprintf "a-%d" i)
                          (leaf (sprintf "a-%d-body" i))
                          (if deep = 1 then
                               mkSection (sprintf "a-%d-rec" i) "r" [ leaf (sprintf "a-%d-step" i) ]
                           else
                               leaf (sprintf "a-%d-rec" i)) ]
            else
                []

        mkSection "r" "r" (leaf "n" :: attempts), r

    { Tree = tree
      FreshNode =
        fun used r ->
            let n, r = ConfRng.intBelow 1000000 r

            let id =
                Seq.initInfinite (fun i -> sprintf "fresh-%d-%d" n i)
                |> Seq.find (used.Contains >> not)

            leaf id, r
      CanHold = None }

let private failedLaws (results: LawResult list) =
    results
    |> List.filter (fun r -> not r.Passed)
    |> List.map (fun r -> r.Law, r.Counterexample)

let private field name ty opt =
    { Name = name
      Type = ty
      Opt = opt
      Aliases = []
      Keyed = false
      Presence = None
      Annotations = Annotations.Empty }

let private keyed (f: IdlField) = { f with Keyed = true }

/// The derivations vocabulary with one field of one kind replaced.
let private withKindField (tag: string) (name: string) (f: IdlField) (idl: Idl) : Idl =
    { idl with
        Kinds =
            idl.Kinds
            |> List.map (fun k ->
                if k.Tag <> tag then
                    k
                else
                    { k with
                        Fields = k.Fields |> List.map (fun g -> if g.Name = name then f else g) }) }

let private faults (idl: Idl) =
    Declare.errors idl |> List.map VocabularyFault.describe

let private keyedFaults (idl: Idl) =
    faults idl |> List.filter (fun d -> d.Contains "keyed position")

let private recoveryOf (idl: Idl) =
    idl.Kinds
    |> List.find (fun k -> k.Tag = "Attempt")
    |> _.Fields
    |> List.find (fun f -> f.Name = "recovery")

[<Tests>]
let tests =
    testList
        "Phase 494 - a declared keyed position"
        [ testList
              "the generated layer"
              [ testCase "Tree.ids does not reach a declared keyed node, and Tree.idsKeyed does" (fun _ ->
                    let root = sample ()
                    let structural = Tree.ids nodeWitness root
                    let walk = Tree.idsKeyed nodeWitness keyedWitness root

                    Expect.contains structural "body" "the structural child is on the witness surface"
                    Expect.isFalse (List.contains "recovery" structural) "the keyed arm is not an ordered child"
                    Expect.isFalse (List.contains "recovery-step" structural) "nor is anything below it"
                    Expect.contains walk "recovery" "the keyed walk reaches the arm"
                    Expect.contains walk "recovery-step" "and every node below it")

                testCase "children lists the structural position alone; keyedChildren holds the arm" (fun _ ->
                    let attempt = find "attempt" (sample ())
                    Expect.equal (children attempt |> List.map _.Id) [ "body" ] "children"
                    Expect.equal (keyedChildren attempt |> List.map _.Id) [ "recovery" ] "keyedChildren"

                    Expect.equal
                        (encodeNode (withChildren (children attempt) attempt))
                        (encodeNode attempt)
                        "withChildren (children n) n = n over the reduced list"

                    let rebuilt = withChildren [ leaf "other" ] attempt

                    Expect.equal
                        (keyedChildren rebuilt |> List.map _.Id)
                        [ "recovery" ]
                        "a structural edit leaves the arm")

                testCase "the generated TypeScript enumerates the same two lists, under node" (fun _ ->
                    let tsModule =
                        match
                            Gen.typescriptModuleDerived
                                DeriveIdl.derivations
                                DeriveIdl.deriveIdl
                                (DeriveIdl.deriveIdl.Kinds |> List.map _.Tag)
                        with
                        | Ok s -> s
                        | Error e -> failtestf "TypeScript codegen refused the derivations vocabulary: %A" e

                    let harness =
                        """const __r = decodeNode(__WIRE__);
if (!__r.ok) throw new Error('the F# wire did not decode');
const __a = __r.value.kind.children[0];
console.log('children\u0001' + children(__a).map((n) => n.id).join(','));
console.log('keyed\u0001' + keyedChildren(__a).map((n) => n.id).join(','));
"""
                            .Replace("__WIRE__", System.Text.Json.JsonSerializer.Serialize(encodeNode (sample ())))

                    match IdlDeriveTests.runTsModule tsModule harness with
                    | None -> skiptest "node not on PATH — the executed TypeScript check is skipped"
                    | Some lines ->
                        let answers = IdlDeriveTests.lineMap lines
                        Expect.equal answers["children"] "body" "TypeScript children"
                        Expect.equal answers["keyed"] "recovery" "TypeScript keyedChildren") ]

          testList
              "the conformance law"
              [ testCase "the generated pair and an agreeing hand pair enumerate the same tree" (fun _ ->
                    let results =
                        Conformance.witnessPairLawsAt
                            nodeWitness
                            keyedWitness
                            handNodes
                            handKeyed
                            idw
                            (gen true)
                            494
                            200

                    Expect.isEmpty (failedLaws results) "every pair law holds")

                testCase "RED: a hand pair listing the arm as an ordered child fails at the holding node" (fun _ ->
                    let results =
                        Conformance.witnessPairLawsAt
                            nodeWitness
                            keyedWitness
                            staleNodes
                            staleKeyed
                            idw
                            (gen true)
                            494
                            200

                    let failed = failedLaws results
                    Expect.hasLength failed 1 "only the structural law reds — both pairs reach the same ids"

                    let law, counterexample = List.head failed
                    Expect.stringContains law "structural children" "the structural law"

                    Expect.stringContains
                        (Option.defaultValue "" counterexample)
                        "kind Attempt"
                        "localised to the node that holds the arm")

                testCase "a sample holding no keyed position is vacuous BY DECLARATION, and says so" (fun _ ->
                    let noKeyed: KeyedWitness<Node<string>, string> =
                        { handKeyed with
                            KeyedChildren = fun _ -> [] }

                    let results =
                        Conformance.witnessPairLawsAt nodeWitness noKeyed handNodes noKeyed idw (gen false) 494 50

                    Expect.isEmpty (failedLaws results) "nothing fails"

                    Expect.exists
                        results
                        (fun r -> r.Law.Contains "vacuous BY DECLARATION")
                        "the adequacy line names the vacuity") ]

          testList
              "the declaration"
              [ testCase "the derivations vocabulary, with its keyed arm, is a valid declaration" (fun _ ->
                    Expect.isEmpty (faults DeriveIdl.deriveIdl) "Declare.errors")

                testCase "the marker is refused where it declares nothing" (fun _ ->
                    let cases =
                        [ "an optional node",
                          withKindField "Task" "hint" (keyed (field "hint" TNode Optional)) DeriveIdl.deriveIdl
                          "a slot holding no node",
                          withKindField "Task" "name" (keyed (field "name" TStr Required)) DeriveIdl.deriveIdl
                          "a record's field",
                          { DeriveIdl.deriveIdl with
                              Records =
                                  DeriveIdl.deriveIdl.Records
                                  |> List.map (fun r ->
                                      if r.Name <> "Branch" then
                                          r
                                      else
                                          { r with
                                              Fields =
                                                  r.Fields
                                                  |> List.map (fun f -> if f.Name = "body" then keyed f else f) }) }
                          "the node envelope",
                          { DeriveIdl.deriveIdl with
                              NodeFields = DeriveIdl.deriveIdl.NodeFields |> List.map keyed }
                          "an op's field",
                          { DeriveIdl.deriveIdl with
                              Ops =
                                  DeriveIdl.deriveIdl.Ops
                                  |> List.map (fun o ->
                                      { o with
                                          Fields = o.Fields |> List.map keyed }) } ]

                    for name, idl in cases do
                        Expect.isNonEmpty (keyedFaults idl) name)

                testCase "a node list a kind always carries may be declared keyed" (fun _ ->
                    let idl =
                        withKindField
                            "Section"
                            "children"
                            (keyed (field "children" (TList TNode) Required))
                            DeriveIdl.deriveIdl

                    Expect.isEmpty (faults idl) "admitted")

                testCase "the artifact writes keyed only when set, and reads it back" (fun _ ->
                    let text = Artifact.render DeriveIdl.deriveIdl
                    Expect.stringContains text "\"keyed\": true" "the marked field carries the member"

                    let count = text.Split("\"keyed\"").Length - 1

                    Expect.equal count 1 "exactly one field is marked, and no unmarked field writes the member"

                    match Artifact.parse text with
                    | Ok back ->
                        Expect.isTrue (recoveryOf back).Keyed "the marker survives the round trip"
                        Expect.equal (Artifact.render back) text "byte-stable"
                    | Error e -> failtestf "the artifact did not read back: %s" e)

                testCase "the classifier grades a marking host-surface-only, under its own row" (fun _ ->
                    let unmarked =
                        withKindField
                            "Attempt"
                            "recovery"
                            { recoveryOf DeriveIdl.deriveIdl with
                                Keyed = false }
                            DeriveIdl.deriveIdl

                    match
                        Diff.parse (Artifact.render unmarked) None,
                        Diff.parse (Artifact.render DeriveIdl.deriveIdl) None
                    with
                    | Ok b, Ok a ->
                        let rows = Diff.changes b a |> List.map Diff.classify
                        Expect.hasLength rows 1 "one change"

                        match rows with
                        | [ row ] ->
                            Expect.equal
                                row.Change
                                (FieldKeyedChanged(OKind "Attempt", "recovery", false, true))
                                "the change"

                            Expect.equal row.Severity HostSurfaceOnly "never a wire event"
                        | _ -> ()
                    | Error e, _
                    | _, Error e -> failtestf "artifact did not read back: %s" e) ] ]
