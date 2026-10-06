module Fuaran.Core.Tests.IdlDeriveTests

// ---------------------------------------------------------------------------
// Phase 374 — the structural derivations the F# generator emits on request.
//
// Three kinds of evidence, in this order:
//   1. OPT-IN. Requesting nothing is the generator every vocabulary already had, byte for byte,
//      on every vocabulary this repository generates; and the committed derivations module is
//      exactly what the generator emits (the drift guard).
//   2. THE KEYED GO-RED. A case table whose entries carry nodes: the unkeyed witness's walk does
//      not reach them (the defect the generated `nodeWitness` was recorded with), the generated
//      `keyedWitness` does, and a keyed witness that declares nothing is refused by the same
//      predicate — so the predicate can tell the two apart.
//   3. LAWS over the COMPILED module (`DeriveGenerated.fs`): `withChildren (children n) n = n`,
//      the keyed rebuild is arity-preserving, `mapMsg id = id` and `mapMsg` reaches every handler,
//      `Rule.fold` visits exactly what a hand walk does, the slot enumerators, the projections, the
//      default records and the constants say what the vocabulary declares.
// Plus the refusals, and expressibility for the other non-UI vocabularies.
//
// Trees with handlers carry no structural equality (a closure has none), so a tree is compared
// through its canonical encoding, which is what the wire can tell apart.
// ---------------------------------------------------------------------------

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.DeriveGenerated

let private idw: IdWitness<string> =
    { ToString = id
      OfString = id
      Equals = (=) }

let private owner (name: string) : Owner =
    { Name = Some name
      Priority = Priority.High }

let private leaf (id: string) : Node<string> = mkNote id

/// A tree touching every node position: structural children, a case table, an optional node, a
/// map of nodes and the envelope's annotation.
let private sample () : Node<string> =
    let rule =
        Rule.AllOf
            [ Rule.Equals("k", "v")
              Rule.Not(Rule.Maybe(Some Rule.Always))
              Rule.Guarded
                  { Rule = Rule.Named(Map.ofList [ "a", Rule.Always; "b", Rule.Maybe None ])
                    Note = None } ]

    let choice =
        mkChoice
            "choice"
            [ { Label = "first"
                Body = leaf "case-1" }
              { Label = "second"
                Body = leaf "case-2" } ]
            (leaf "otherwise")
            rule

    let task =
        { mkTask
              "task"
              "review"
              Rule.Always
              (Trigger.Timer(owner "ops", 5, (fun i -> sprintf "tick %d" i)))
              (Map.ofList [ "x", leaf "extra-x"; "y", leaf "extra-y" ])
              (owner "lead") with
            Annotation = Some(leaf "task-note") }

    let task =
        match task.Kind with
        | NodeKind.Task s ->
            { task with
                Kind = NodeKind.Task { s with Hint = Some(leaf "hint") } }
        | _ -> task

    mkSection "root" "plan" [ choice; task; leaf "tail" ]

let private enc (n: Node<'Msg>) = encodeNode n

let private sortedIds (xs: string list) = List.sort xs

/// The go-red's predicate: every case-table body id is reached by the walk the witnesses derive.
let private reachesCaseTable (keyw: KeyedWitness<Node<string>, string>) (root: Node<string>) : bool =
    let reached = Tree.idsKeyed nodeWitness keyw root |> Set.ofList
    [ "case-1"; "case-2" ] |> List.forall reached.Contains

/// A keyed witness declaring no keyed position — the state before this phase.
let private noKeyed: KeyedWitness<Node<string>, string> =
    { Surface = "none"
      KeyedChildren = fun _ -> []
      ReplaceKeyedChildren = fun n _ -> n
      PlaceKeyedChild = fun _ _ -> None
      IdsUnique = fun _ -> true }

/// Every Rule a hand walk visits, in preorder.
let rec private handRules (r: Rule) : Rule list =
    r
    :: match r with
       | Rule.AllOf rs -> List.collect handRules rs
       | Rule.Not x -> handRules x
       | Rule.Maybe(Some x) -> handRules x
       | Rule.Maybe None -> []
       | Rule.Guarded g -> handRules g.Rule
       | Rule.Named m -> m |> Map.toList |> List.collect (snd >> handRules)
       | Rule.Always
       | Rule.Equals _ -> []

/// The in-repository vocabularies the generator emits, under the support each is emitted with.
let private vocabularies () : (string * Gen.GenSupport * Idl) list =
    let decodeIdl, _, _ = DecodeVectorsIdl.current ()

    [ "mini", Gen.GenSupport.Empty, MiniIdl.miniIdl
      "doc", Gen.GenSupport.Empty, SecondDomainSpike.docIdl
      "score", Gen.GenSupport.Empty, ScoreDomainSpike.scoreIdl
      "reference", ReferenceIdl.support.Support, ReferenceIdl.refIdl
      "decode-vectors", Gen.GenSupport.Empty, decodeIdl
      "refusal-corners", Gen.GenSupport.Empty, RefusalCornersIdl.idl
      "derive", Gen.GenSupport.Empty, DeriveIdl.deriveIdl ]

let private tags (idl: Idl) = idl.Kinds |> List.map _.Tag

let private refusedAs (r: Result<string, CodegenError>) : string =
    match r with
    | Error(CodegenError.UnsupportedConstruct(construct, _, _)) -> construct
    | Error e -> failtestf "expected UnsupportedConstruct, got %A" e
    | Ok _ -> failtest "expected a refusal, got a module"

let private derive (ds: Gen.Derivation list) (idl: Idl) =
    Gen.fsharpModuleDerived Gen.GenSupport.Empty ds "Probe" idl (tags idl)

[<Tests>]
let tests =
    testList
        "Phase 374 - structural derivations"
        [ testList
              "opt-in"
              [ testCase "requesting nothing emits fsharpModuleWith byte for byte, on every vocabulary here" (fun _ ->
                    for name, sup, idl in vocabularies () do
                        let plain = Gen.fsharpModuleWith sup ("M." + name) idl (tags idl)
                        let derived = Gen.fsharpModuleDerived sup [] ("M." + name) idl (tags idl)
                        Expect.equal derived plain (sprintf "%s: an empty request changed the emission" name))

                testCase "the committed DeriveGenerated.fs is what the generator emits (byte for byte)" (fun _ ->
                    let generated =
                        match DeriveIdl.generate () with
                        | Ok s -> s
                        | Error e -> failtestf "codegen refused the derivations vocabulary: %A" e

                    let path = Snapshots.repoFile DeriveIdl.generatedFile

                    if System.Environment.GetEnvironmentVariable "FUARAN_REGEN" = "1" then
                        File.WriteAllText(path, generated)

                    Expect.equal
                        generated
                        (File.ReadAllText path)
                        "regenerate with: dotnet run --project tests/Fuaran.Core.Tests -- --regen-snapshots")

                testCase "a derived member is appended, never interleaved: the plain module is a prefix" (fun _ ->
                    let plain =
                        Gen.fsharpModuleWith Gen.GenSupport.Empty "M" DeriveIdl.deriveIdl (tags DeriveIdl.deriveIdl)

                    let derived =
                        derive
                            [ Gen.Derivation.VocabularyConstants; Gen.Derivation.DefaultRecords ]
                            DeriveIdl.deriveIdl

                    match plain, derived with
                    | Ok p, Ok d -> Expect.isTrue (d.Replace("module Probe", "module M").StartsWith p) "prefix"
                    | _ -> failtest "both emissions should succeed") ]

          testList
              "keyed positions (go-red)"
              [ testCase "RED: the structural witness's walk does not reach a case table's entries" (fun _ ->
                    let ids = Tree.ids nodeWitness (sample ()) |> Set.ofList
                    Expect.isFalse (ids.Contains "case-1") "the unkeyed walk reaches no case-table body"
                    Expect.isFalse (reachesCaseTable noKeyed (sample ())) "a witness declaring no keyed position fails")

                testCase
                    "GREEN: the generated keyed witness enumerates the case table and every keyed position"
                    (fun _ ->
                        let root = sample ()
                        Expect.isTrue (reachesCaseTable keyedWitness root) "case-table bodies reached"

                        let all = Tree.idsKeyed nodeWitness keyedWitness root |> sortedIds

                        let expected =
                            [ "root"
                              "choice"
                              "case-1"
                              "case-2"
                              "otherwise"
                              "task"
                              "hint"
                              "extra-x"
                              "extra-y"
                              "task-note"
                              "tail" ]
                            |> sortedIds

                        Expect.equal all expected "the keyed walk reaches every declared node position")

                testCase "withKeyedChildren (keyedChildren n) n = n, and the rebuild is arity-preserving" (fun _ ->
                    let root = sample ()

                    for n in Tree.preorderKeyed nodeWitness keyedWitness root do
                        Expect.equal (enc (withKeyedChildren (keyedChildren n) n)) (enc n) "identity rebuild"

                        let fresh = keyedChildren n |> List.mapi (fun i _ -> leaf (sprintf "fresh-%d" i))

                        Expect.equal
                            (keyedChildren (withKeyedChildren fresh n) |> List.map _.Id)
                            (fresh |> List.map _.Id)
                            "the keyed positions hold exactly the new list"

                        Expect.equal
                            (children (withKeyedChildren fresh n) |> List.map _.Id)
                            (children n |> List.map _.Id)
                            "the structural children are untouched")

                testCase "IdsUnique refuses an id placed in a keyed position, and wellFormedKeyed agrees" (fun _ ->
                    let root = sample ()
                    Expect.isTrue (keyedWitness.IdsUnique root) "the sample is unique"

                    let choice =
                        Tree.preorderKeyed nodeWitness keyedWitness root
                        |> List.find (fun n -> n.Id = "choice")

                    match keyedWitness.PlaceKeyedChild choice "tail" with
                    | None -> failtest "a case table with entries has a position to place into"
                    | Some placed ->
                        let tree =
                            withChildren
                                (children root |> List.map (fun c -> if c.Id = "choice" then placed else c))
                                root

                        Expect.isFalse (keyedWitness.IdsUnique tree) "the duplicate in the case table is seen"

                        Expect.equal
                            (Tree.wellFormedKeyed nodeWitness keyedWitness idw tree)
                            (Tree.RepeatedId "tail")
                            "the engine's keyed verdict names it"

                        Expect.equal
                            (Tree.wellFormed nodeWitness idw tree)
                            Tree.Structural
                            "the unkeyed verdict cannot")

                testCase "the generated keyed witness is certified by the kit's keyedChildrenLaws" (fun _ ->
                    // Trees drawn with a case table of 0..3 entries and a sometimes-present hint,
                    // so the laws meet keyed positions both held and absent.
                    let tree (r: ConfRng.T) =
                        let entries, r = ConfRng.intBelow 4 r
                        let withHint, r = ConfRng.intBelow 2 r

                        let choice =
                            mkChoice
                                "c"
                                [ for i in 1..entries ->
                                      { Label = string i
                                        Body = leaf (sprintf "c-%d" i) } ]
                                (leaf "c-else")
                                Rule.Always

                        let task: Node<string> =
                            { mkTask "t" "t" Rule.Always (Trigger.Manual "m") Map.empty (owner "o") with
                                Annotation = (if withHint = 1 then Some(leaf "t-note") else None) }

                        mkSection "r" "r" [ choice; task ], r

                    let gen: OpGen<Node<string>, string> =
                        { Tree = tree
                          FreshNode =
                            fun used r ->
                                let n, r = ConfRng.intBelow 1000000 r

                                let id =
                                    Seq.initInfinite (fun i -> sprintf "fresh-%d-%d" n i)
                                    |> Seq.find (used.Contains >> not)

                                leaf id, r
                          CanHold = None }

                    let results = Conformance.keyedChildrenLaws keyedWitness nodeWitness idw gen 374 200
                    let failed = results |> List.filter (fun r -> not r.Passed)
                    Expect.isEmpty (failed |> List.map (fun r -> r.Law, r.Counterexample)) "every keyed law holds")

                testCase "a node with no keyed position has nowhere to place one" (fun _ ->
                    Expect.isNone (keyedWitness.PlaceKeyedChild (leaf "n") "x") "a note holds no keyed position") ]

          testList
              "structural access"
              [ testCase "withChildren (children n) n = n for every node of the sample" (fun _ ->
                    for n in Tree.preorderKeyed nodeWitness keyedWitness (sample ()) do
                        Expect.equal (enc (withChildren (children n) n)) (enc n) n.Id)

                testCase "wireTag is the encoded discriminator, and allWireTags is the kind set in order" (fun _ ->
                    Expect.equal allWireTags (DeriveIdl.deriveIdl.Kinds |> List.map _.Tag) "declaration order"

                    for n in Tree.preorderKeyed nodeWitness keyedWitness (sample ()) do
                        Expect.stringContains (enc n) (sprintf "\"$type\":\"%s\"" (wireTag n)) n.Id
                        Expect.equal (nodeWitness.KindTag n) (wireTag n) "the witness is built on it") ]

          testList
              "slots, map, fold, projections, defaults, constants"
              [ testCase "slotsOfRule and slotsOfOwner list the fields declared at each type" (fun _ ->
                    let nodes = Tree.preorderKeyed nodeWitness keyedWitness (sample ())
                    let byId i = nodes |> List.find (fun n -> n.Id = i)
                    Expect.equal (slotsOfRule (byId "choice") |> List.map fst) [ "rule" ] "choice"
                    Expect.equal (slotsOfRule (byId "task") |> List.map fst) [ "due" ] "task"
                    Expect.isEmpty (slotsOfRule (byId "tail")) "a note holds no rule"
                    Expect.equal (slotsOfOwner (byId "task")) [ "owner", owner "lead" ] "task owner"
                    Expect.isEmpty (slotsOfOwner (byId "root")) "an absent optional owner is no slot")

                testCase "mapMsg id = id, and mapMsg reaches every handler" (fun _ ->
                    let root = sample ()
                    Expect.equal (enc (mapMsg id root)) (enc root) "map id = id"

                    let mapped: Node<int> = mapMsg String.length root

                    let fired =
                        Tree.preorderKeyed nodeWitness keyedWitness mapped
                        |> List.choose (fun n ->
                            match n.Kind with
                            | NodeKind.Task s ->
                                match s.Trigger with
                                | Trigger.Timer(_, _, fire) -> Some(fire 12)
                                | _ -> None
                            | _ -> None)

                    Expect.equal fired [ String.length "tick 12" ] "the handler's message is rewritten")

                testCase "Rule.fold visits exactly the nested values a hand walk does, in preorder" (fun _ ->
                    let rule =
                        Rule.AllOf
                            [ Rule.Not(Rule.Equals("a", "b"))
                              Rule.Maybe(Some(Rule.AllOf [ Rule.Always ]))
                              Rule.Guarded
                                  { Rule = Rule.Not Rule.Always
                                    Note = Some "n" }
                              Rule.Named(Map.ofList [ "z", Rule.Always; "y", Rule.Maybe None ]) ]

                    let folded = Rule.fold (fun acc r -> r :: acc) [] rule |> List.rev
                    Expect.equal folded (handRules rule) "same values, same order")

                testCase "a projection answers where the case carries the field" (fun _ ->
                    Expect.equal (Trigger.owner (Trigger.Timer(owner "a", 1, ignore))) (Some(owner "a")) "timer"
                    Expect.equal (Trigger.owner (Trigger.Manual "m")) None "manual carries none"
                    Expect.equal (Measure.amount (Measure.Days 2.5)) 2.5 "every case carries amount")

                testCase "the default records are the values the smart constructors fill" (fun _ ->
                    match (mkNote "n": Node<string>).Kind with
                    | NodeKind.Note s -> Expect.equal s defaultNoteSpec "mkNote fills defaultNoteSpec"
                    | _ -> failtest "mkNote builds a note"

                    Expect.equal
                        defaultOwner
                        { Name = None
                          Priority = Priority.Normal }
                        "owner defaults")

                testCase "the constants say what the vocabulary declares" (fun _ ->
                    Expect.equal kindCategories["structure"] (set [ "Section"; "Choice" ]) "categories"
                    Expect.equal kindFieldNames["Note"] (set [ "text"; "priority" ]) "field names"
                    Expect.equal envelopeFieldNames (set [ "annotation" ]) "envelope"
                    Expect.equal opFieldNames["Move"] (set [ "target"; "index" ]) "ops") ]

          testList
              "refusals and reach"
              [ testCase "a fold over a union that does not recurse is refused" (fun _ ->
                    let c = refusedAs (derive [ Gen.Derivation.Fold "Measure" ] DeriveIdl.deriveIdl)
                    Expect.stringContains c "Measure" "names the union")

                testCase "a projection no case carries is refused" (fun _ ->
                    let c =
                        refusedAs (derive [ Gen.Derivation.Projections("Trigger", [ "colour" ]) ] DeriveIdl.deriveIdl)

                    Expect.stringContains c "colour" "names the field")

                testCase "a projection whose cases disagree on its type is refused" (fun _ ->
                    let c =
                        refusedAs (derive [ Gen.Derivation.Projections("Trigger", [ "fire" ]) ] DeriveIdl.deriveIdl)

                    Expect.stringContains c "different types" "says why")

                testCase "a slot enumerator over an undeclared type is refused" (fun _ ->
                    let c = refusedAs (derive [ Gen.Derivation.SlotsOf "Nothing" ] DeriveIdl.deriveIdl)
                    Expect.stringContains c "Nothing" "names the type")

                testCase "a message map over a vocabulary with no message parameter is refused" (fun _ ->
                    refusedAs (derive [ Gen.Derivation.MapMsg ] ScoreDomainSpike.scoreIdl) |> ignore)

                testCase "a message map through a contravariant signature is refused" (fun _ ->
                    let contravariant =
                        { DeriveIdl.deriveIdl with
                            Unions =
                                DeriveIdl.deriveIdl.Unions
                                |> List.map (fun u ->
                                    if u.Name <> "Trigger" then
                                        u
                                    else
                                        { u with
                                            Cases =
                                                u.Cases
                                                @ [ { Tag = "Echo"
                                                      Fields =
                                                        [ { Name = "back"
                                                            Type =
                                                              TFn
                                                                  { FSharp = "'Msg -> string"
                                                                    TypeScript = "(m: Msg) => string"
                                                                    Placeholder = "(fun (_: obj) -> \"\")" }
                                                            Opt = Required
                                                            Annotations = Annotations.Empty } ]
                                                      Annotations = Annotations.Empty } ] }) }

                    let c = refusedAs (derive [ Gen.Derivation.MapMsg ] contravariant)
                    Expect.stringContains c "'Msg -> string" "names the signature")

                testCase
                    "every derivation but the message map is expressible for the score and document vocabularies"
                    (fun _ ->
                        let common =
                            [ Gen.Derivation.StructuralAccess
                              Gen.Derivation.KeyedPositions
                              Gen.Derivation.VocabularyConstants ]

                        for idl, extra in
                            [ ScoreDomainSpike.scoreIdl,
                              [ Gen.Derivation.SlotsOf "Duration"; Gen.Derivation.DefaultRecords ]
                              SecondDomainSpike.docIdl, [] ] do
                            match derive (common @ extra) idl with
                            | Ok _ -> ()
                            | Error e -> failtestf "refused: %s" (CodegenError.describe e))

                testCase "no derived identifier, comment or annotation is a user-interface term" (fun _ ->
                    let text = File.ReadAllText(Snapshots.repoFile DeriveIdl.generatedFile)
                    let derived = text.Substring(text.IndexOf "/// The kind's wire tag")

                    // The sweep reads the text it claims to: the vocabulary's own words are found.
                    Expect.isTrue
                        (System.Text.RegularExpressions.Regex.IsMatch(derived, "\\bRule\\b"))
                        "the sweep's matcher finds a word the derived members do contain"

                    for term in
                        [ "UI"
                          "Widget"
                          "Render"
                          "View"
                          "Component"
                          "Binding"
                          "Handler"
                          "Layout"
                          "Screen"
                          "Element" ] do
                        Expect.isFalse
                            (System.Text.RegularExpressions.Regex.IsMatch(
                                derived,
                                "\\b" + term + "\\b",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                            ))
                            (sprintf "the derived members mention '%s'" term)) ] ]
