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
