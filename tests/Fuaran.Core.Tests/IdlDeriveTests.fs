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

/// The derivations vocabulary with a handler whose signature takes the message — contravariant.
let private contravariantIdl =
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

                    Approval.write Approval.Regen Approval.Regenerated.Derive path generated
                    |> ignore

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

                testCase "the generated keyed witness is certified by the kit's keyedChildrenLawsAt" (fun _ ->
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

                    let results =
                        Conformance.keyedChildrenLawsAt keyedWitness nodeWitness idw gen 374 200

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
                    let c = refusedAs (derive [ Gen.Derivation.MapMsg ] contravariantIdl)
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

// ---------------------------------------------------------------------------
// Phase 380 — the same derivations in the TypeScript host (`Gen.typescriptModuleDerived`).
//
//   1. OPT-IN. Requesting nothing is `typescriptModule`, byte for byte, on every vocabulary here,
//      and a derived member is appended after the module's members, never interleaved. (The
//      repository commits no generated TypeScript module, so the generator's own plain emission is
//      the oracle the empty request is held to.)
//   2. ADMISSIBILITY IS SHARED. A request the F# path refuses is refused here with the same typed
//      error, and a request it admits is admitted.
//   3. HOST AGREEMENT, under node: on the derivations vocabulary the generated TypeScript and the
//      generated F# answer the same wire tags, children, keyed children, slots, folds,
//      projections, keyed placement, default records and constants for every node of one tree,
//      compared as data.
// ---------------------------------------------------------------------------

/// Run a generated TypeScript module with `harness` appended, under node: its standard output
/// lines, or `None` where node is not on PATH. A failed run fails the test.
let runTsModule (moduleText: string) (harness: string) : string list option =
    let tmp =
        Path.Combine(Path.GetTempPath(), sprintf "fuaran-phase380-ts-%s.mjs" (System.Guid.NewGuid().ToString("N")))

    File.WriteAllText(tmp, moduleText + "\n\n" + harness)

    try
        let psi = ChildProcess.redirected "node" ("\"" + tmp + "\"")

        let proc =
            try
                Some(System.Diagnostics.Process.Start psi)
            with _ ->
                None

        match proc with
        | None -> None
        | Some p ->
            let stdout = p.StandardOutput.ReadToEndAsync()
            let stderr = p.StandardError.ReadToEnd()
            p.WaitForExit()

            if p.ExitCode <> 0 then
                failtestf "node failed running the generated TS module: %s" stderr

            stdout.Result.Replace("\r\n", "\n").Split('\n')
            |> Array.filter (fun l -> l <> "")
            |> List.ofArray
            |> Some
    finally
        try
            File.Delete tmp
        with _ ->
            ()

/// `key\u0001value` lines as a map.
let lineMap (lines: string list) : Map<string, string> =
    lines
    |> List.map (fun l ->
        let i = l.IndexOf '\u0001'
        l.Substring(0, i), l.Substring(i + 1))
    |> Map.ofList

let private tsDerive (ds: Gen.Derivation list) (idl: Idl) =
    Gen.typescriptModuleDerived ds idl (tags idl)

/// A union value's case name.
let private caseName<'T> (v: 'T) =
    let case, _ =
        Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(box v, typeof<'T>)

    case.Name

let private flag (b: bool) = if b then "true" else "false"

/// The sample tree with the root's optional owner and estimate present, so every projection and
/// slot answers something.
let private agreementTree () : Node<string> =
    let root = sample ()

    match root.Kind with
    | NodeKind.Section s ->
        { root with
            Kind =
                NodeKind.Section
                    { s with
                        Owner = Some(owner "boss")
                        Estimate = Some(Measure.Hours 2.5) } }
    | _ -> root

/// What the compiled F# module answers, as `key\u0001value` data.
let private fsharpReport (root: Node<string>) : Map<string, string> =
    let ruleTags (r: Rule) =
        Rule.fold (fun acc x -> caseName x :: acc) [] r |> List.rev |> String.concat ","

    let ownerDesc (o: Owner) =
        defaultArg o.Name "-" + "/" + caseName o.Priority

    let ids (ns: Node<string> list) =
        ns |> List.map _.Id |> String.concat ","

    let mapDesc (m: Map<string, Set<string>>) =
        m
        |> Map.toList
        |> List.map (fun (k, s) -> k + "=" + (s |> Set.toList |> String.concat ","))
        |> String.concat ";"

    let nodeLine (n: Node<string>) =
        let projection =
            match n.Kind with
            | NodeKind.Task s ->
                match Trigger.owner s.Trigger with
                | Some o -> ownerDesc o
                | None -> "-"
            | NodeKind.Section s ->
                match s.Estimate with
                | Some m -> string (Measure.amount m)
                | None -> "-"
            | _ -> ""

        let placed =
            match keyedWitness.PlaceKeyedChild n n.Id with
            | Some m -> flag (keyedWitness.IdsUnique m)
            | None -> "none"

        let laws =
            enc (withChildren (children n) n) = enc n
            && enc (withKeyedChildren (keyedChildren n) n) = enc n

        "node " + n.Id,
        String.concat
            "|"
            [ wireTag n
              ids (children n)
              ids (keyedChildren n)
              slotsOfRule n
              |> List.map (fun (k, r) -> k + ":" + ruleTags r)
              |> String.concat ";"
              slotsOfOwner n
              |> List.map (fun (k, o) -> k + ":" + ownerDesc o)
              |> String.concat ";"
              projection
              placed
              flag laws ]

    let defaultsHost: Node<string> =
        { Id = "s"
          Kind =
            NodeKind.Section
                { Title = "t"
                  Children = []
                  Owner = Some defaultOwner
                  Estimate = None }
          Annotation = None }

    let noteHost: Node<string> =
        { Id = "d"
          Kind = NodeKind.Note defaultNoteSpec
          Annotation = None }

    (Tree.preorderKeyed nodeWitness keyedWitness root |> List.map nodeLine)
    @ [ "allWireTags", String.concat "," allWireTags
        "kindCategories", mapDesc kindCategories
        "kindFieldNames", mapDesc kindFieldNames
        "opFieldNames", mapDesc opFieldNames
        "envelopeFieldNames", envelopeFieldNames |> Set.toList |> String.concat ","
        "defaultNoteSpec", enc noteHost
        "defaultOwner", enc defaultsHost
        "idsUnique", flag (keyedWitness.IdsUnique root) ]
    |> Map.ofList

/// The same questions asked of the generated TypeScript, over the F# tree's wire.
let private tsHarness (wire: string) =
    """const __r = decodeNode(__WIRE__);
if (!__r.ok) throw new Error('the F# wire did not decode: ' + JSON.stringify(__r.error));
const __root = __r.value;
const __out = (k, v) => console.log(k + '\u0001' + v);
const __ruleTags = (r) => Rule$.fold((acc, x) => { acc.push(x.$type); return acc; }, [], r).join(',');
const __owner = (o) => (o.name === undefined ? '-' : o.name) + '/' + o.priority;
const __ids = (ns) => ns.map((n) => n.id).join(',');
const __map = (m) => [...m].map(([k, s]) => k + '=' + [...s].join(',')).join(';');
const __all = [];
const __walk = (n) => { __all.push(n); for (const c of [...children(n), ...keyedChildren(n)]) __walk(c); };
__walk(__root);
for (const n of __all) {
  const k = n.kind;
  let proj = '';
  if (k.$type === 'Task') { const o = Trigger$.owner(k.trigger); proj = o === undefined ? '-' : __owner(o); }
  else if (k.$type === 'Section') proj = k.estimate === undefined ? '-' : String(Measure$.amount(k.estimate));
  const placedNode = keyedWitness.placeKeyedChild(n, n.id);
  const placed = placedNode === undefined ? 'none' : String(keyedWitness.idsUnique(placedNode));
  const laws = encodeNode(withChildren(children(n), n)) === encodeNode(n)
    && encodeNode(withKeyedChildren(keyedChildren(n), n)) === encodeNode(n);
  __out('node ' + n.id, [wireTag(n), __ids(children(n)), __ids(keyedChildren(n)),
    slotsOfRule(n).map(([f, r]) => f + ':' + __ruleTags(r)).join(';'),
    slotsOfOwner(n).map(([f, o]) => f + ':' + __owner(o)).join(';'),
    proj, placed, String(laws)].join('|'));
}
__out('allWireTags', allWireTags.join(','));
__out('kindCategories', __map(kindCategories));
__out('kindFieldNames', __map(kindFieldNames));
__out('opFieldNames', __map(opFieldNames));
__out('envelopeFieldNames', [...envelopeFieldNames].join(','));
__out('defaultNoteSpec', encodeNode({ id: 'd', kind: defaultNoteSpec }));
__out('defaultOwner', encodeNode({ id: 's', kind: { $type: 'Section', title: 't', children: [], owner: defaultOwner } }));
__out('idsUnique', String(keyedWitness.idsUnique(__root)));
"""
        .Replace("__WIRE__", System.Text.Json.JsonSerializer.Serialize wire)

[<Tests>]
let typescriptTests =
    testList
        "Phase 380 - structural derivations in the TypeScript host"
        [ testCase "requesting nothing emits typescriptModule byte for byte, on every vocabulary here" (fun _ ->
              for name, _, idl in vocabularies () do
                  Expect.equal (tsDerive [] idl) (Gen.typescriptModule idl (tags idl)) name)

          testCase "a derived member is appended, never interleaved: the plain module is a prefix" (fun _ ->
              let cases =
                  [ for name, _, idl in vocabularies () -> name, [ Gen.Derivation.SpecDecoders ], idl ]
                  @ [ "derive (every request)", DeriveIdl.derivations, DeriveIdl.deriveIdl ]

              for name, ds, idl in cases do
                  match Gen.typescriptModule idl (tags idl), tsDerive ds idl with
                  | Ok plain, Ok derived ->
                      Expect.isTrue (derived.StartsWith(plain + "\n\n", System.StringComparison.Ordinal)) name
                      Expect.isTrue (derived.Length > plain.Length) (name + ": something was appended")
                  | p, d -> failtestf "%s: plain %A, derived %A" name (Result.isOk p) d)

          testCase "a request is admitted, or refused with the same typed error, exactly as on the F# path" (fun _ ->
              let common =
                  [ Gen.Derivation.StructuralAccess
                    Gen.Derivation.KeyedPositions
                    Gen.Derivation.VocabularyConstants ]

              let cases =
                  [ "fold, not recursive", [ Gen.Derivation.Fold "Measure" ], DeriveIdl.deriveIdl
                    "fold, undeclared", [ Gen.Derivation.Fold "Nothing" ], DeriveIdl.deriveIdl
                    "projection, no carrier",
                    [ Gen.Derivation.Projections("Trigger", [ "colour" ]) ],
                    DeriveIdl.deriveIdl
                    "projection, types differ",
                    [ Gen.Derivation.Projections("Trigger", [ "fire" ]) ],
                    DeriveIdl.deriveIdl
                    "fold beside a projection named fold",
                    [ Gen.Derivation.Fold "Rule"; Gen.Derivation.Projections("Rule", [ "fold" ]) ],
                    DeriveIdl.deriveIdl
                    "slots, undeclared", [ Gen.Derivation.SlotsOf "Nothing" ], DeriveIdl.deriveIdl
                    "slots, unheld", [ Gen.Derivation.SlotsOf "Guard" ], DeriveIdl.deriveIdl
                    "message map, no parameter", [ Gen.Derivation.MapMsg ], ScoreDomainSpike.scoreIdl
                    "message map, contravariant", [ Gen.Derivation.MapMsg ], contravariantIdl
                    "every request", DeriveIdl.derivations, DeriveIdl.deriveIdl
                    "score",
                    common @ [ Gen.Derivation.SlotsOf "Duration"; Gen.Derivation.DefaultRecords ],
                    ScoreDomainSpike.scoreIdl
                    "document", common, SecondDomainSpike.docIdl ]

              let refusals =
                  [ for name, ds, idl in cases do
                        match derive ds idl, tsDerive ds idl with
                        | Ok _, Ok _ -> ()
                        | Error f, Error t ->
                            Expect.equal t f (name + ": the same typed refusal")
                            yield name
                        | f, t -> failtestf "%s: F# %A, TypeScript %A" name (Result.isOk f) t ]

              Expect.isGreaterThanOrEqual refusals.Length 8 "the refusal cases refuse")

          testCase "the generated TypeScript agrees with the generated F#, under node, as data" (fun _ ->
              let root = agreementTree ()
              let expected = fsharpReport root

              let tsModule =
                  match tsDerive DeriveIdl.derivations DeriveIdl.deriveIdl with
                  | Ok s -> s
                  | Error e -> failtestf "TypeScript codegen refused the derivations vocabulary: %A" e

              match runTsModule tsModule (tsHarness (enc root)) with
              | None -> skiptest "node not on PATH — the executed TypeScript agreement is skipped"
              | Some lines ->
                  let actual = lineMap lines

                  Expect.isGreaterThanOrEqual
                      (expected |> Map.filter (fun k _ -> k.StartsWith "node ") |> Map.count)
                      11
                      "the tree reaches every node position"

                  for KeyValue(k, v) in expected do
                      match actual.TryFind k with
                      | Some a -> Expect.equal a v (sprintf "TypeScript and F# disagree on '%s'" k)
                      | None -> failtestf "the TypeScript run said nothing about '%s'" k

                  Expect.equal actual.Count expected.Count "no extra answers")

          testCase "the message map has no TypeScript counterpart, and emits nothing" (fun _ ->
              match tsDerive [] DeriveIdl.deriveIdl, tsDerive [ Gen.Derivation.MapMsg ] DeriveIdl.deriveIdl with
              | Ok plain, Ok mapped -> Expect.equal mapped plain "an admitted MapMsg adds no member"
              | p, m -> failtestf "plain %A, mapped %A" p m) ]

// ---------------------------------------------------------------------------
// Phase 381 — the declaration file types the derived members. The derived module and its derived
// declarations are held to EXACTLY the same names, mechanically, over every request each test
// vocabulary admits; with a TypeScript compiler (`FUARAN_CORE_TSC`), the declarations also compile
// and a typed consumer of every derived member checks against them.
// ---------------------------------------------------------------------------

let private tsDeclare (ds: Gen.Derivation list) (idl: Idl) =
    Gen.typescriptDeclarationsDerived ds idl (tags idl)

/// Every name the module's `export { … }` statements name, as a consumer imports it (`U$ as U`
/// is `U`), in order.
let private exportedNames (moduleText: string) : string list =
    moduleText.Split('\n')
    |> Array.filter (fun l -> l.StartsWith("export { ", System.StringComparison.Ordinal))
    |> Array.collect (fun l ->
        l.Substring(9, l.Length - 9 - 3).Split(", ")
        |> Array.map (fun n ->
            match n.IndexOf " as " with
            | -1 -> n
            | i -> n.Substring(i + 4)))
    |> List.ofArray

/// Every VALUE name the declaration file declares (`export declare function|const <name>`), in
/// order. Types are not values: a module exports no type, so a type declares no export.
let private declaredNames (declarations: string) : string list =
    let m =
        System.Text.RegularExpressions.Regex.Matches(
            declarations,
            @"^export declare (?:function|const) ([A-Za-z_$][A-Za-z0-9_$]*)",
            System.Text.RegularExpressions.RegexOptions.Multiline
        )

    [ for x in m -> x.Groups[1].Value ]

/// Every request a vocabulary could be asked for: each general request, a `SlotsOf` per declared
/// type, a `Fold` and a projection of every field per union, singly — the admitted ones are what
/// the agreement test covers, and refusal parity covers the rest.
let private candidateRequests (idl: Idl) : Gen.Derivation list list =
    let general =
        [ Gen.Derivation.StructuralAccess
          Gen.Derivation.KeyedPositions
          Gen.Derivation.MapMsg
          Gen.Derivation.DefaultRecords
          Gen.Derivation.VocabularyConstants
          Gen.Derivation.SpecDecoders ]

    let typeNames =
        (idl.Enums |> List.map _.Name)
        @ (idl.Records |> List.map _.Name)
        @ (idl.Unions |> List.map _.Name)

    let perUnion =
        idl.Unions
        |> List.collect (fun u ->
            [ yield Gen.Derivation.Fold u.Name
              for f in u.Cases |> List.collect _.Fields |> List.map _.Name |> List.distinct do
                  yield Gen.Derivation.Projections(u.Name, [ f ]) ])

    (general @ (typeNames |> List.map Gen.Derivation.SlotsOf) @ perUnion
     |> List.map List.singleton)
    @ [ general ]

/// The vocabularies the agreement runs over here: every one this file generates, 374's derivations
/// vocabulary among them. 377's spec-decoder vocabulary is held by `IdlSpecDecoderTests`, which
/// compiles after it, through the same two functions below.
let private declarationVocabularies () : (string * Idl) list =
    [ for name, _, idl in vocabularies () -> name, idl ]

/// Phase 381 — hold one vocabulary's derived module and derived declarations to EXACTLY the same
/// names over every candidate request: an admitted request declares every name the module exports
/// and exports every name it declares, none twice; a refused one is refused by both with the same
/// typed error. Answers how many requests were admitted.
let declarationsAgreeOver (vocab: string) (idl: Idl) : int =
    let mutable admitted = 0

    for ds in candidateRequests idl do
        let label = sprintf "%s %A" vocab ds

        match tsDerive ds idl, tsDeclare ds idl with
        | Ok m, Ok d ->
            admitted <- admitted + 1
            let exported = exportedNames m
            let declared = declaredNames d

            Expect.equal (List.distinct declared) declared (label + ": no name is declared twice")

            Expect.equal
                (Set.ofList declared)
                (Set.ofList exported)
                (label + ": every exported name is declared, every declared name exported")
        | Error me, Error de -> Expect.equal de me (label + ": the same typed refusal")
        | m, d -> failtestf "%s: module %A, declarations %A" label (Result.isOk m) d

    admitted

/// The TypeScript compiler `FUARAN_CORE_TSC` names (`tsc.js`), as Phase 348's leg reads it.
let tscJs () : string option =
    match System.Environment.GetEnvironmentVariable "FUARAN_CORE_TSC" with
    | null
    | "" -> None
    | p when File.Exists p -> Some p
    | p -> failtestf "FUARAN_CORE_TSC names %s, which does not exist" p

/// `tsc --noEmit --strict` over `consumer.mts` beside `generated.mjs` / `generated.d.mts`, in a
/// fresh directory: the exit code and the compiler's output, or `None` without node.
let tscCheck (tsc: string) (files: (string * string) list) : (int * string) option =
    let dir =
        Path.Combine(Path.GetTempPath(), sprintf "fuaran-381-%s" (System.Guid.NewGuid().ToString "N"))

    Directory.CreateDirectory dir |> ignore

    try
        for name, text in files do
            File.WriteAllText(Path.Combine(dir, name), text)

        let psi =
            ChildProcess.redirected
                "node"
                (sprintf
                    "\"%s\" --noEmit --strict --target es2022 --module nodenext --moduleResolution nodenext consumer.mts"
                    tsc)

        psi.WorkingDirectory <- dir

        match
            (try
                Some(System.Diagnostics.Process.Start psi)
             with _ ->
                 None)
        with
        | None -> None
        | Some p ->
            let stdout = p.StandardOutput.ReadToEndAsync()
            let stderr = p.StandardError.ReadToEnd()
            p.WaitForExit()
            Some(p.ExitCode, stdout.Result + stderr)
    finally
        try
            Directory.Delete(dir, true)
        with _ ->
            ()

/// Phase 381 — under `tsc`, the derived module and declarations of one vocabulary, every request
/// it admits at once, beside a consumer importing the whole module: the declarations compile.
let derivedDeclarationsCompile (tsc: string) (vocab: string) (idl: Idl) : unit =
    let admitted =
        candidateRequests idl
        |> List.concat
        |> List.distinct
        |> List.filter (fun d -> Result.isOk (tsDerive [ d ] idl))

    match tsDerive admitted idl, tsDeclare admitted idl with
    | Ok m, Ok d ->
        let consumer =
            "import * as G from './generated.mjs';\nconsole.log(Object.keys(G));\n"

        match tscCheck tsc [ "generated.mjs", m; "generated.d.mts", d; "consumer.mts", consumer ] with
        | None -> skiptest "node not on PATH"
        | Some(code, out) -> Expect.equal code 0 (sprintf "%s: tsc accepts the declarations: %s" vocab out)
    | m, d -> failtestf "%s: module %A, declarations %A" vocab (Result.isOk m) d

/// A consumer of every derived member of the derivations vocabulary, typed: each binding states
/// the type the declaration must give, and each `@ts-expect-error` line must NOT compile.
let private typedConsumer =
    String.concat
        "\n"
        [ "import { decodeNode, wireTag, allWireTags, children, withChildren, nodeWitness, keyedChildren, withKeyedChildren, keyedWitness, slotsOfRule, slotsOfOwner, Rule, Trigger, Measure, defaultNoteSpec, defaultOwner, kindCategories, kindFieldNames, envelopeFieldNames, opFieldNames, decodeNoteSpec, decodeNoteSpecAll, decodeNodeJson, decodeNodeJsonAll, decodeNodeAll, type Node, type NoteSpec, type Owner, type DecodeRefusal } from './generated.mjs';"
          "const r = decodeNode('{}');"
          "if (r.ok) {"
          "  const n: Node = r.value;"
          "  const tag: 'Section' | 'Choice' | 'Task' | 'Note' = wireTag(n);"
          "  const tags: ReadonlyArray<string> = allWireTags;"
          "  const kids: Node[] = children(n);"
          "  const same: Node = withChildren(kids, n);"
          "  const viaWitness: Node[] = nodeWitness.children(nodeWitness.replaceChildren(n, kids));"
          "  const id: string = nodeWitness.id(n);"
          "  const keyed: Node[] = keyedChildren(withKeyedChildren(keyedChildren(n), n));"
          "  const placed: Node | undefined = keyedWitness.placeKeyedChild(n, 'x');"
          "  const unique: boolean = keyedWitness.idsUnique(n);"
          "  const surface: string = keyedWitness.surface;"
          "  const rules: Array<[string, Rule]> = slotsOfRule(n);"
          "  const owners: Array<[string, Owner]> = slotsOfOwner(n);"
          "  const depth: number = rules.length === 0 ? 0 : Rule.fold((s: number, _v: Rule) => s + 1, 0, rules[0][1]);"
          "  const owner: Owner | undefined = n.kind.$type === 'Task' ? Trigger.owner(n.kind.trigger) : undefined;"
          "  const hours: number = n.kind.$type === 'Section' && n.kind.estimate !== undefined ? Measure.amount(n.kind.estimate) : 0;"
          "  // @ts-expect-error - a projection some case does not carry may be undefined"
          "  const ownerAlways: Owner = n.kind.$type === 'Task' ? Trigger.owner(n.kind.trigger) : defaultOwner;"
          "  // @ts-expect-error - children are nodes, not strings"
          "  const wrong: string[] = children(n);"
          "  console.log(tag, tags, same, viaWitness, id, keyed, placed, unique, surface, owners, depth, owner, hours, ownerAlways, wrong);"
          "}"
          "const note: NoteSpec = defaultNoteSpec;"
          "const owner0: Owner = defaultOwner;"
          "const cats: ReadonlyMap<string, ReadonlySet<string>> = kindCategories;"
          "const fields: ReadonlySet<string> | undefined = kindFieldNames.get('Note');"
          "const env: ReadonlySet<string> = envelopeFieldNames;"
          "const ops: ReadonlyMap<string, ReadonlySet<string>> = opFieldNames;"
          "const one = decodeNoteSpec({});"
          "const every = decodeNoteSpecAll({});"
          "if (!one.ok) { const e: DecodeRefusal = one.error; console.log(e.code); }"
          "if (!every.ok) { const es: DecodeRefusal[] = every.errors; console.log(es.length); }"
          "else { const v: NoteSpec = every.value; console.log(v.text); }"
          "const j = decodeNodeJson(JSON.parse('{}'));"
          "const ja = decodeNodeJsonAll(JSON.parse('{}'));"
          "const t = decodeNodeAll('{}');"
          "if (!t.ok) { const es: DecodeRefusal[] = t.errors; console.log(es); }"
          "// @ts-expect-error - a collecting decoder answers every defect, never one"
          "if (!ja.ok) { const e: DecodeRefusal = ja.errors; console.log(e); }"
          "console.log(note, owner0, cats, fields, env, ops, j);"
          "" ]

[<Tests>]
let declarationTests =
    testList
        "Phase 381 - the TypeScript declarations type the derived members"
        [ testCase "requesting nothing emits typescriptDeclarations byte for byte, on every vocabulary here" (fun _ ->
              for name, idl in declarationVocabularies () do
                  Expect.equal (tsDeclare [] idl) (Gen.typescriptDeclarations idl (tags idl)) name)

          testCase
              "the derived module and its declarations name exactly the same members, for every admitted request"
              (fun _ ->
                  let admitted =
                      declarationVocabularies ()
                      |> List.sumBy (fun (vocab, idl) -> declarationsAgreeOver vocab idl)

                  Expect.isGreaterThanOrEqual admitted 60 "the agreement covers the admitted requests")

          testCase "the derivations vocabulary with every request agrees, and declares a member of every kind" (fun _ ->
              match
                  tsDerive DeriveIdl.derivations DeriveIdl.deriveIdl,
                  tsDeclare DeriveIdl.derivations DeriveIdl.deriveIdl
              with
              | Ok m, Ok d ->
                  Expect.equal (Set.ofList (declaredNames d)) (Set.ofList (exportedNames m)) "the same names"

                  for name in
                      [ "wireTag"
                        "allWireTags"
                        "children"
                        "withChildren"
                        "nodeWitness"
                        "keyedChildren"
                        "withKeyedChildren"
                        "keyedWitness"
                        "slotsOfRule"
                        "slotsOfOwner"
                        "Rule"
                        "Trigger"
                        "Measure"
                        "defaultNoteSpec"
                        "defaultOwner"
                        "kindCategories"
                        "kindFieldNames"
                        "envelopeFieldNames"
                        "opFieldNames"
                        "decodeNoteSpec"
                        "decodeNoteSpecAll"
                        "decodeNodeJson"
                        "decodeNodeJsonAll"
                        "decodeNodeAll" ] do
                      Expect.contains (declaredNames d) name (name + " is declared")

                  Expect.stringContains
                      d
                      "export declare function decodeNodeAll(s: string): { ok: true; value: Node } | { ok: false; errors: Array<DecodeRefusal> };"
                      "a collecting decoder answers DecodeRefusal[]"

                  Expect.stringContains
                      d
                      "  owner(v: Trigger): Owner | undefined;"
                      "a projection some case lacks is undefined there"

                  Expect.stringContains d "  amount(v: Measure): number;" "a projection every case carries is total"
              | m, d -> failtestf "module %A, declarations %A" m d)

          testCase "the declarations are the plain declarations plus a header note and appended members" (fun _ ->
              match
                  Gen.typescriptDeclarations DeriveIdl.deriveIdl (tags DeriveIdl.deriveIdl),
                  tsDeclare DeriveIdl.derivations DeriveIdl.deriveIdl
              with
              | Ok plain, Ok derived ->
                  let unheaded =
                      derived.Split('\n')
                      |> Array.filter (fun l ->
                          not (
                              l.StartsWith("// Phase 381", System.StringComparison.Ordinal)
                              || l.StartsWith("// `mapMsg`", System.StringComparison.Ordinal)
                          ))
                      |> String.concat "\n"

                  Expect.isTrue
                      (unheaded.StartsWith(plain.TrimEnd('\n') + "\n\n", System.StringComparison.Ordinal))
                      "the plain declarations are a prefix once the header note is set aside"
              | p, d -> failtestf "plain %A, derived %A" p d)

          testCase "the message map is not declared, and the header comment says why" (fun _ ->
              match tsDeclare [ Gen.Derivation.MapMsg ] DeriveIdl.deriveIdl with
              | Ok d ->
                  Expect.isFalse (declaredNames d |> List.contains "mapMsg") "mapMsg is not declared"
                  let header = d.Substring(0, d.IndexOf "\n\n")
                  Expect.stringContains header "`mapMsg` is not declared" "the header states it"
                  Expect.stringContains header "carries no message type" "and why"
              | Error e -> failtestf "refused: %A" e)

          testCase "with a TypeScript compiler, every vocabulary's derived declarations compile" (fun _ ->
              match tscJs () with
              | None -> skiptest "FUARAN_CORE_TSC names no TypeScript compiler — the type-check leg cannot run"
              | Some tsc ->
                  for vocab, idl in declarationVocabularies () do
                      derivedDeclarationsCompile tsc vocab idl)

          testCase
              "with a TypeScript compiler, a typed consumer of every derived member checks, and a mistyped one does not"
              (fun _ ->
                  match tscJs () with
                  | None -> skiptest "FUARAN_CORE_TSC names no TypeScript compiler — the type-check leg cannot run"
                  | Some tsc ->
                      match
                          tsDerive DeriveIdl.derivations DeriveIdl.deriveIdl,
                          tsDeclare DeriveIdl.derivations DeriveIdl.deriveIdl
                      with
                      | Ok m, Ok d ->
                          let run (decls: string) =
                              tscCheck
                                  tsc
                                  [ "generated.mjs", m; "generated.d.mts", decls; "consumer.mts", typedConsumer ]

                          match run d with
                          | None -> skiptest "node not on PATH"
                          | Some(code, out) ->
                              Expect.equal code 0 (sprintf "tsc accepts the typed consumer: %s" out)

                              // The falsifier: the plain declarations (Phase 380's state) fail the same consumer.
                              match Gen.typescriptDeclarations DeriveIdl.deriveIdl (tags DeriveIdl.deriveIdl) with
                              | Ok plain ->
                                  match run plain with
                                  | Some(code, _) ->
                                      Expect.notEqual code 0 "the undeclared members fail the same consumer"
                                  | None -> ()
                              | Error e -> failtestf "plain refused: %A" e
                      | m, d -> failtestf "module %A, declarations %A" (Result.isOk m) d) ]

// ---------------------------------------------------------------------------
// Phase 403 — a PROJECTED kind under the derivations. A kind projection (Phase 945) replaces a
// kind's record, encoder and decoder with host source, so the generator can neither construct
// the record (the message map) nor read its field shapes (the witness, the keyed walk, the slot
// enumerators) from the kind's wire fields — the two differ exactly where a projection earns its
// keep. The vocabulary below projects `Gate`, whose wire `rule` is OPTIONAL (absent meaning
// `Always`) while the record's `Rule` is REQUIRED: the optionality the SlotsOf defect was
// recorded with. The projection supplies its map (`MapMsg`) and declares its record's fields
// (`RecordFields`); without them each emission that needs them refuses by name. The compiled
// leg runs the emission under `dotnet fsi` — and runs the pre-403 reading (the wire fields read
// as the record's) through the same probe, which must FAIL to compile, so the probe can tell
// the fixed emission from the defect.
// ---------------------------------------------------------------------------

module private Projected =
    let field name ty opt =
        { Name = name
          Type = ty
          Opt = opt
          Annotations = Annotations.Empty }

    let req name ty = field name ty Required
    let opt name ty = field name ty Optional

    let case tag fields =
        { Tag = tag
          Fields = fields
          Annotations = Annotations.Empty }

    let rule = TUnion("Rule", [])
    let trigger = TUnion("Trigger", [])

    /// The gate's wire fields: a structural child, a case table of keyed nodes, an OPTIONAL rule
    /// and a message-carrying trigger.
    let gateWire =
        [ req "body" TNode
          req "steps" (TList(TRecord "Step"))
          opt "rule" rule
          req "trigger" trigger ]

    let idlWith (gateFields: IdlField list) : Idl =
        { Enums = []
          Unions =
            [ { Name = "Rule"
                Params = []
                Cases = [ case "Always" []; case "Equals" [ req "key" TStr; req "value" TStr ] ] }
              { Name = "Trigger"
                Params = []
                Cases =
                  [ case
                        "Timer"
                        [ req "every" TInt
                          req
                              "fire"
                              (TFn
                                  { FSharp = "int -> 'Msg"
                                    TypeScript = "(x: int) => Msg"
                                    Placeholder = "(fun (_: int) -> box \"<closure>\")" }) ]
                    case "Manual" [ req "label" TStr ] ] } ]
          Records =
            [ { Name = "Step"
                Fields = [ req "label" TStr; req "body" TNode ] } ]
          Kinds =
            [ { Tag = "Gate"
                Category = "flow"
                Fields = gateFields
                Annotations = Annotations.Empty }
              { Tag = "Leaf"
                Category = "flow"
                Fields = [ req "text" TStr ]
                Annotations = Annotations.Empty } ]
          Defaults = []
          NodeFields = []
          Ops = []
          Wire = WireShape.Default
          Harden = HardenPolicy.Undeclared }

    let idl = idlWith gateWire

    /// The record's fields at their host shapes: `rule` REQUIRED where the wire's is optional.
    let recordFields =
        [ req "body" TNode
          req "steps" (TList(TRecord "Step"))
          req "rule" rule
          req "trigger" trigger ]

    /// The map, written once in host source beside the record — as `Mk` is.
    let gateMap =
        "and private mapMsgGateSpec<'Msg, 'Msg2> (f: 'Msg -> 'Msg2) (v: GateSpec<'Msg>) : GateSpec<'Msg2> =\n"
        + "    ({ Body = mapMsg f v.Body; Steps = List.map (mapMsgStep f) v.Steps; Rule = v.Rule; Trigger = mapMsgTrigger f v.Trigger }: GateSpec<'Msg2>)"

    let projection (map: string option) (fields: IdlField list option) : Gen.KindProjection =
        { SpecDecl =
            "GateSpec<'Msg> =\n    { Body: Node<'Msg>\n      Steps: Step<'Msg> list\n      Rule: Rule\n      Trigger: Trigger<'Msg> }"
          Encoder =
            "and private encGateSpec<'Msg> (s: GateSpec<'Msg>) : JVal =\n"
            + "    Canon.typed \"Gate\" ([ Some(\"body\", encNode s.Body); Some(\"steps\", JArr(List.map encStep s.Steps)); (match s.Rule with Rule.Always -> None | r -> Some(\"rule\", encRule r)); Some(\"trigger\", encTrigger s.Trigger) ] |> List.choose id)"
          Decoder =
            "and private decGateSpec (j: JVal) : Result<GateSpec<obj>, DecodeError> =\n"
            + "    dObj j |> Result.bind (fun __fs ->\n"
            + "    dReq \"body\" __fs decNode |> Result.bind (fun body ->\n"
            + "    dReq \"steps\" __fs (dList decStep) |> Result.bind (fun steps ->\n"
            + "    dOpt \"rule\" __fs decRule |> Result.bind (fun rule ->\n"
            + "    dReq \"trigger\" __fs decTrigger |> Result.bind (fun trigger ->\n"
            + "    Ok { Body = body; Steps = steps; Rule = Option.defaultValue Rule.Always rule; Trigger = trigger })))))"
          Mk = None
          MapMsg = map
          RecordFields = fields }

    let supportWith (map: string option) (fields: IdlField list option) : Gen.GenSupport =
        { Gen.GenSupport.Empty with
            KindProjections = Map.ofList [ "Gate", projection map fields ] }

    /// The projection as a host supplies it in full.
    let support = supportWith (Some gateMap) (Some recordFields)

    let tags = [ "Gate"; "Leaf" ]

    let derive (sup: Gen.GenSupport) (ds: Gen.Derivation list) (idl: Idl) =
        Gen.fsharpModuleDerived sup ds "Projected" idl tags

    let refusal (r: Result<string, CodegenError>) : string * string =
        match r with
        | Error(CodegenError.UnsupportedConstruct(construct, principle, _)) -> construct, principle
        | Error e -> failtestf "expected UnsupportedConstruct, got %A" e
        | Ok _ -> failtest "expected a refusal, got a module"

    let emitted (r: Result<string, CodegenError>) : string =
        match r with
        | Ok s -> s
        | Error e -> failtestf "codegen refused: %s" (CodegenError.describe e)

    /// Run a script under `dotnet fsi`: its exit code and both streams, or None without `dotnet`.
    let runFsi (source: string) : (int * string * string) option =
        let path =
            Path.Combine(Path.GetTempPath(), sprintf "fuaran-403-%s.fsx" (System.Guid.NewGuid().ToString("N")))

        File.WriteAllText(path, source)

        try
            match
                (try
                    Some(System.Diagnostics.Process.Start(ChildProcess.redirected "dotnet" ("fsi \"" + path + "\"")))
                 with _ ->
                     None)
            with
            | None -> None
            | Some p ->
                let err = p.StandardError.ReadToEndAsync()
                let out = p.StandardOutput.ReadToEnd()
                p.WaitForExit()
                Some(p.ExitCode, out, err.Result)
        finally
            try
                File.Delete path
            with _ ->
                ()

    let dllRef (name: string) =
        "#r @\"" + Path.Combine(System.AppContext.BaseDirectory, name) + "\"\n"

    /// A script compiling the emitted module ahead of `body`.
    let script (moduleText: string) (body: string) =
        dllRef "Fuaran.Core.Wire.dll"
        + dllRef "Fuaran.Core.Tree.dll"
        + dllRef "Fuaran.Core.Validator.dll"
        + moduleText.Replace("module Projected\n", "")
        + "\n\n"
        + body

    let derivations =
        [ Gen.Derivation.StructuralAccess
          Gen.Derivation.KeyedPositions
          Gen.Derivation.SlotsOf "Rule"
          Gen.Derivation.MapMsg ]

    /// What the compiled module is asked: the walks, the rule slot, and the mapped handler.
    let probe =
        String.concat
            "\n"
            [ "let gate: Node<string> ="
              "    { Id = \"g\""
              "      Kind ="
              "        NodeKind.Gate"
              "            { Body = { Id = \"body\"; Kind = NodeKind.Leaf { Text = \"b\" } }"
              "              Steps = [ { Label = \"one\"; Body = { Id = \"step\"; Kind = NodeKind.Leaf { Text = \"s\" } } } ]"
              "              Rule = Rule.Equals(\"k\", \"v\")"
              "              Trigger = Trigger.Timer(5, fun i -> sprintf \"tick %d\" i) } }"
              "printfn \"children\\t%s\" (children gate |> List.map (fun n -> n.Id) |> String.concat \",\")"
              "printfn \"keyed\\t%s\" (keyedChildren gate |> List.map (fun n -> n.Id) |> String.concat \",\")"
              "printfn \"slots\\t%s\" (slotsOfRule gate |> List.map (fun (k, r) -> sprintf \"%s=%A\" k r) |> String.concat \",\")"
              "match (mapMsg (fun (s: string) -> s.Length) gate).Kind with"
              "| NodeKind.Gate g ->"
              "    match g.Trigger with"
              "    | Trigger.Timer(_, fire) -> printfn \"mapped\\t%d\" (fire 7)"
              "    | Trigger.Manual _ -> printfn \"mapped\\tmanual\""
              "| NodeKind.Leaf _ -> printfn \"mapped\\tleaf\""
              "printfn \"encoded\\t%s\" (encodeNode gate)" ]

[<Tests>]
let projectedKindTests =
    testList
        "Phase 403 - a projected kind under the derivations"
        [ testCase "with its map supplied, MapMsg over a projected kind composes it" (fun _ ->
              let src =
                  Projected.emitted (Projected.derive Projected.support [ Gen.Derivation.MapMsg ] Projected.idl)

              Expect.stringContains src Projected.gateMap "the projection's map, verbatim, in the message map's group"

              Expect.stringContains
                  src
                  "    | NodeKind.Gate s -> NodeKind.Gate(mapMsgGateSpec f s)"
                  "the kind arm calls it")

          testCase "with no map, MapMsg over a projected kind is refused, with the same message" (fun _ ->
              let construct, principle =
                  Projected.refusal (
                      Projected.derive
                          (Projected.supportWith None (Some Projected.recordFields))
                          [ Gen.Derivation.MapMsg ]
                          Projected.idl
                  )

              Expect.equal construct "the message map over the projected kind 'Gate'" "the construct, by name"

              Expect.equal
                  principle
                  "a projected kind's record is verbatim host source, so the generator cannot construct it"
                  "the principle, unchanged")

          testCase "the slot enumerator reads the RECORD's shape: a required member, not the wire's option" (fun _ ->
              let src =
                  Projected.emitted (
                      Projected.derive Projected.support [ Gen.Derivation.SlotsOf "Rule" ] Projected.idl
                  )

              Expect.stringContains
                  src
                  "        | NodeKind.Gate s -> [ \"rule\", s.Rule ]"
                  "the record's required Rule"

              Expect.isFalse (src.Contains "match s.Rule with Some") "not the wire's optional rule")

          testCase "with no declared record, each emission that reads one refuses by name" (fun _ ->
              let undeclared = Projected.supportWith (Some Projected.gateMap) None

              let without (names: string list) =
                  Projected.idlWith (Projected.gateWire |> List.filter (fun f -> not (List.contains f.Name names)))

              // A wire holding a node directly: even the plain module's witness cannot say which
              // member of the record holds it.
              Expect.equal
                  (fst (Projected.refusal (Gen.fsharpModuleWith undeclared "Projected" Projected.idl Projected.tags)))
                  "the node witness's children of the projected kind 'Gate'"
                  "the witness, in the plain module"

              Expect.equal
                  (fst (
                      Projected.refusal (Projected.derive undeclared [ Gen.Derivation.StructuralAccess ] Projected.idl)
                  ))
                  "the structural children of the projected kind 'Gate'"
                  "the public structural access"

              // Nodes only within a record: the witness has no child to read, the keyed walk does.
              let keyedOnly = without [ "body" ]

              Expect.isOk
                  (Gen.fsharpModuleWith undeclared "Projected" keyedOnly Projected.tags)
                  "no direct node on the wire: the witness reads nothing of the record"

              Expect.equal
                  (fst (Projected.refusal (Projected.derive undeclared [ Gen.Derivation.KeyedPositions ] keyedOnly)))
                  "the keyed node positions of the projected kind 'Gate'"
                  "the keyed walk"

              // No node on the wire at all: the node walks admit it; the slot enumerator still
              // refuses, because a record may hold a value its wire spells otherwise.
              let nodeFree = without [ "body"; "steps" ]

              Expect.isOk
                  (Projected.derive
                      undeclared
                      [ Gen.Derivation.StructuralAccess; Gen.Derivation.KeyedPositions ]
                      nodeFree)
                  "a node-free wire: no node position to read"

              Expect.equal
                  (fst (Projected.refusal (Projected.derive undeclared [ Gen.Derivation.SlotsOf "Rule" ] nodeFree)))
                  "a slot enumerator over 'Rule' through the projected kind 'Gate'"
                  "the slot enumerator, always")

          testCase "a declared record field at a type the module does not declare is refused" (fun _ ->
              let sup =
                  Projected.supportWith
                      (Some Projected.gateMap)
                      (Some(Projected.recordFields @ [ Projected.req "owner" (TRecord "Owner") ]))

              Expect.equal
                  (fst (Projected.refusal (Gen.fsharpModuleWith sup "Projected" Projected.idl Projected.tags)))
                  "the projected record field 'Gate.owner' at the undeclared type 'Owner'"
                  "the field and the type, by name")

          testCase
              "compiled: the walks, the slots and the map read the record; the wire's reading does not compile"
              (fun _ ->
                  let fixedSrc =
                      Projected.emitted (Projected.derive Projected.support Projected.derivations Projected.idl)

                  match Projected.runFsi (Projected.script fixedSrc Projected.probe) with
                  | None -> skiptest "dotnet not on PATH - the compiled projection check is skipped"
                  | Some(code, out, err) ->
                      Expect.equal code 0 (sprintf "the emission compiles and runs: %s" err)

                      let lines =
                          out.Replace("\r\n", "\n").Split('\n')
                          |> Array.filter (fun l -> l <> "")
                          |> List.ofArray

                      Expect.equal
                          lines
                          [ "children\tbody"
                            "keyed\tstep"
                            "slots\trule=Equals (\"k\", \"v\")"
                            "mapped\t6"
                            "encoded\t{\"id\":\"g\",\"kind\":{\"$type\":\"Gate\",\"body\":{\"id\":\"body\",\"kind\":{\"$type\":\"Leaf\",\"text\":\"b\"}},\"rule\":{\"$type\":\"Equals\",\"key\":\"k\",\"value\":\"v\"},\"steps\":[{\"body\":{\"id\":\"step\",\"kind\":{\"$type\":\"Leaf\",\"text\":\"s\"}},\"label\":\"one\"}],\"trigger\":{\"$type\":\"Timer\",\"every\":5,\"fire\":\"<closure>\"}}}" ]
                          "the record's members, walked, enumerated and mapped"

                      // The go-red: the pre-403 reading — the wire's fields taken as the record's —
                      // through the same probe. It emits, and it does not compile.
                      let wireReading =
                          Projected.emitted (
                              Projected.derive
                                  (Projected.supportWith (Some Projected.gateMap) (Some Projected.gateWire))
                                  Projected.derivations
                                  Projected.idl
                          )

                      match Projected.runFsi (Projected.script wireReading Projected.probe) with
                      | None -> skiptest "dotnet not on PATH"
                      | Some(code, _, err) ->
                          Expect.notEqual code 0 "the wire's optional rule against the record's required one"
                          Expect.stringContains err "FS0001" "a type mismatch, as recorded")

          testCase "the support document carries the map and the record fields, and reads them back" (fun _ ->
              let doc: SupportDocument =
                  { Support = Projected.support
                    HostPrelude = None }

              let text = SupportArtifact.render doc
              Expect.stringContains text "\"mapMsg\"" "the map is rendered"
              Expect.stringContains text "\"recordFields\"" "the record fields are rendered"

              match SupportArtifact.parse text with
              | Error e -> failtestf "support.json did not parse: %s" e
              | Ok back ->
                  Expect.equal back.Support Projected.support "every member round-trips"
                  Expect.equal (SupportArtifact.render back) text "and the bytes are stable"

              let bare =
                  SupportArtifact.render
                      { Support = Projected.supportWith None None
                        HostPrelude = None }

              Expect.isFalse (bare.Contains "mapMsg" || bare.Contains "recordFields") "neither key when undeclared")

          testCase "the diff sees a change to either member as a support change" (fun _ ->
              let snap (sup: Gen.GenSupport) =
                  match
                      Diff.snapshotWith
                          (Artifact.json Projected.idl)
                          (Some(SupportArtifact.json { Support = sup; HostPrelude = None }))
                  with
                  | Ok s -> s.Support
                  | Error e -> failtestf "snapshot: %s" e

              let full = snap Projected.support
              Expect.notEqual (snap (Projected.supportWith None (Some Projected.recordFields))) full "the map"
              Expect.notEqual (snap (Projected.supportWith (Some Projected.gateMap) None)) full "the record fields") ]
