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
open Fuaran.Core.Idl.Codegen.Types
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
let private vocabularies () : (string * GenSupport * Idl) list =
    let decodeIdl, _, _ = DecodeVectorsIdl.current ()

    [ "mini", GenSupport.Empty, MiniIdl.miniIdl
      "doc", GenSupport.Empty, SecondDomainIdl.docIdl
      "score", GenSupport.Empty, ScoreDomainIdl.scoreIdl
      "reference", ReferenceIdl.support.Support, ReferenceIdl.refIdl
      "decode-vectors", GenSupport.Empty, decodeIdl
      "refusal-corners", GenSupport.Empty, RefusalCornersIdl.idl
      "derive", GenSupport.Empty, DeriveIdl.deriveIdl ]

let private tags (idl: Idl) = idl.Kinds |> List.map _.Tag

let private refusedAs (r: Result<string, CodegenError>) : string =
    match r with
    | Error(CodegenError.UnsupportedConstruct(construct, _, _)) -> construct
    | Error e -> failtestf "expected UnsupportedConstruct, got %A" e
    | Ok _ -> failtest "expected a refusal, got a module"

let private derive (ds: Derivation list) (idl: Idl) =
    Gen.fsharpModuleDerived GenSupport.Empty ds "Probe" idl (tags idl)

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
                            @ [ { Hosted = None
                                  Tag = "Echo"
                                  Fields =
                                    [ { Name = "back"
                                        Type =
                                          TFn
                                              { FSharp = "'Msg -> string"
                                                TypeScript = "(m: Msg) => string"
                                                Placeholder = "(fun (_: obj) -> \"\")" }
                                        Opt = Required
                                        Aliases = []
                                        Presence = None
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
                        "regenerate with: dotnet run --project tools/Fuaran.Core.Tools -- render generated")

                testCase "a derived member is appended, never interleaved: the plain module is a prefix" (fun _ ->
                    let plain =
                        Gen.fsharpModuleWith GenSupport.Empty "M" DeriveIdl.deriveIdl (tags DeriveIdl.deriveIdl)

                    let derived =
                        derive [ Derivation.VocabularyConstants; Derivation.DefaultRecords ] DeriveIdl.deriveIdl

                    match plain, derived with
                    | Ok p, Ok d ->
                        // Below the stamp (Phase 505): the emission hash moves with the appended members.
                        Expect.isTrue
                            ((Snapshots.unstamped d)
                                .Replace("module Probe", "module M")
                                .StartsWith(Snapshots.unstamped p))
                            "prefix"
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
                    let c = refusedAs (derive [ Derivation.Fold "Measure" ] DeriveIdl.deriveIdl)
                    Expect.stringContains c "Measure" "names the union")

                testCase "a projection no case carries is refused" (fun _ ->
                    let c =
                        refusedAs (derive [ Derivation.Projections("Trigger", [ "colour" ]) ] DeriveIdl.deriveIdl)

                    Expect.stringContains c "colour" "names the field")

                testCase "a projection whose cases disagree on its type is refused" (fun _ ->
                    let c =
                        refusedAs (derive [ Derivation.Projections("Trigger", [ "fire" ]) ] DeriveIdl.deriveIdl)

                    Expect.stringContains c "different types" "says why")

                testCase "a slot enumerator over an undeclared type is refused" (fun _ ->
                    let c = refusedAs (derive [ Derivation.SlotsOf "Nothing" ] DeriveIdl.deriveIdl)
                    Expect.stringContains c "Nothing" "names the type")

                testCase "a message map over a vocabulary with no message parameter is refused" (fun _ ->
                    refusedAs (derive [ Derivation.MapMsg ] ScoreDomainIdl.scoreIdl) |> ignore)

                testCase "a message map through a contravariant signature is refused" (fun _ ->
                    let c = refusedAs (derive [ Derivation.MapMsg ] contravariantIdl)
                    Expect.stringContains c "'Msg -> string" "names the signature")

                testCase
                    "every derivation but the message map is expressible for the score and document vocabularies"
                    (fun _ ->
                        let common =
                            [ Derivation.StructuralAccess
                              Derivation.KeyedPositions
                              Derivation.VocabularyConstants ]

                        for idl, extra in
                            [ ScoreDomainIdl.scoreIdl, [ Derivation.SlotsOf "Duration"; Derivation.DefaultRecords ]
                              SecondDomainIdl.docIdl, [] ] do
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
        match ChildProcess.run (ChildProcess.command "node" ("\"" + tmp + "\"")) with
        | Error _ -> None
        | Ok p ->
            if p.Exit <> 0 then
                failtestf "node failed running the generated TS module: %s" p.Stderr

            p.Stdout.Replace("\r\n", "\n").Split('\n')
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

let private tsDerive (ds: Derivation list) (idl: Idl) =
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
                  [ for name, _, idl in vocabularies () -> name, [ Derivation.SpecDecoders ], idl ]
                  @ [ "derive (every request)", DeriveIdl.derivations, DeriveIdl.deriveIdl ]

              for name, ds, idl in cases do
                  match Gen.typescriptModule idl (tags idl), tsDerive ds idl with
                  | Ok plain, Ok derived ->
                      // Below the stamp (Phase 505): the emission hash moves with the appended members.
                      let plain, derived = Snapshots.unstamped plain, Snapshots.unstamped derived
                      Expect.isTrue (derived.StartsWith(plain + "\n\n", System.StringComparison.Ordinal)) name
                      Expect.isTrue (derived.Length > plain.Length) (name + ": something was appended")
                  | p, d -> failtestf "%s: plain %A, derived %A" name (Result.isOk p) d)

          testCase "a request is admitted, or refused with the same typed error, exactly as on the F# path" (fun _ ->
              let common =
                  [ Derivation.StructuralAccess
                    Derivation.KeyedPositions
                    Derivation.VocabularyConstants ]

              let cases =
                  [ "fold, not recursive", [ Derivation.Fold "Measure" ], DeriveIdl.deriveIdl
                    "fold, undeclared", [ Derivation.Fold "Nothing" ], DeriveIdl.deriveIdl
                    "projection, no carrier", [ Derivation.Projections("Trigger", [ "colour" ]) ], DeriveIdl.deriveIdl
                    "projection, types differ", [ Derivation.Projections("Trigger", [ "fire" ]) ], DeriveIdl.deriveIdl
                    "fold beside a projection named fold",
                    [ Derivation.Fold "Rule"; Derivation.Projections("Rule", [ "fold" ]) ],
                    DeriveIdl.deriveIdl
                    "slots, undeclared", [ Derivation.SlotsOf "Nothing" ], DeriveIdl.deriveIdl
                    "slots, unheld", [ Derivation.SlotsOf "Guard" ], DeriveIdl.deriveIdl
                    "message map, no parameter", [ Derivation.MapMsg ], ScoreDomainIdl.scoreIdl
                    "message map, contravariant", [ Derivation.MapMsg ], contravariantIdl
                    "every request", DeriveIdl.derivations, DeriveIdl.deriveIdl
                    "score",
                    common @ [ Derivation.SlotsOf "Duration"; Derivation.DefaultRecords ],
                    ScoreDomainIdl.scoreIdl
                    "document", common, SecondDomainIdl.docIdl ]

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
              match tsDerive [] DeriveIdl.deriveIdl, tsDerive [ Derivation.MapMsg ] DeriveIdl.deriveIdl with
              | Ok plain, Ok mapped -> Expect.equal mapped plain "an admitted MapMsg adds no member"
              | p, m -> failtestf "plain %A, mapped %A" p m) ]

// ---------------------------------------------------------------------------
// Phase 381 — the declaration file types the derived members. The derived module and its derived
// declarations are held to EXACTLY the same names, mechanically, over every request each test
// vocabulary admits; with a TypeScript compiler (`FUARAN_CORE_TSC`), the declarations also compile
// and a typed consumer of every derived member checks against them.
// ---------------------------------------------------------------------------

let private tsDeclare (ds: Derivation list) (idl: Idl) =
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
let private candidateRequests (idl: Idl) : Derivation list list =
    let general =
        [ Derivation.StructuralAccess
          Derivation.KeyedPositions
          Derivation.MapMsg
          Derivation.DefaultRecords
          Derivation.VocabularyConstants
          Derivation.SpecDecoders ]

    let typeNames =
        (idl.Enums |> List.map _.Name)
        @ (idl.Records |> List.map _.Name)
        @ (idl.Unions |> List.map _.Name)

    let perUnion =
        idl.Unions
        |> List.collect (fun u ->
            [ yield Derivation.Fold u.Name
              for f in u.Cases |> List.collect _.Fields |> List.map _.Name |> List.distinct do
                  yield Derivation.Projections(u.Name, [ f ]) ])

    (general @ (typeNames |> List.map Derivation.SlotsOf) @ perUnion
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

        ChildProcess.command
            "node"
            (sprintf
                "\"%s\" --noEmit --strict --target es2022 --module nodenext --moduleResolution nodenext consumer.mts"
                tsc)
        |> ChildProcess.inDir dir
        |> ChildProcess.run
        |> Result.toOption
        |> Option.map (fun p -> p.Exit, p.Stdout + p.Stderr)
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
                  // Below the stamp (Phase 505): the emission hash moves with the appended members.
                  let plain = Snapshots.unstamped plain

                  let unheaded =
                      (Snapshots.unstamped derived).Split('\n')
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
              match tsDeclare [ Derivation.MapMsg ] DeriveIdl.deriveIdl with
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

module internal Projected =
    let field name ty opt =
        { Name = name
          Type = ty
          Opt = opt
          Aliases = []
          Presence = None
          Annotations = Annotations.Empty }

    let req name ty = field name ty Required
    let opt name ty = field name ty Optional

    let case tag fields =
        { Tag = tag
          Fields = fields
          Annotations = Annotations.Empty
          Hosted = None }

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
            [ { TagRule = Fuaran.Core.Idl.UnionTagRule.CaseName
                Name = "Rule"
                Params = []
                Cases = [ case "Always" []; case "Equals" [ req "key" TStr; req "value" TStr ] ]
                Annotations = Annotations.Empty }
              { TagRule = Fuaran.Core.Idl.UnionTagRule.CaseName
                Name = "Trigger"
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
                    case "Manual" [ req "label" TStr ] ]
                Annotations = Annotations.Empty } ]
          Records =
            [ { Name = "Step"
                Fields = [ req "label" TStr; req "body" TNode ]
                Annotations = Annotations.Empty } ]
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

    let projection (map: string option) (fields: IdlField list option) : KindProjection =
        { SpecDecl =
            "GateSpec<'Msg> =\n    { Body: Node<'Msg>\n      Steps: Step<'Msg> list\n      Rule: Rule\n      Trigger: Trigger<'Msg> }"
          Encoder =
            "and private encGateSpec<'Msg> (s: GateSpec<'Msg>) : JVal =\n"
            + "    Canonical.typed \"Gate\" ([ Some(\"body\", encNode s.Body); Some(\"steps\", JArr(List.map encStep s.Steps)); (match s.Rule with Rule.Always -> None | r -> Some(\"rule\", encRule r)); Some(\"trigger\", encTrigger s.Trigger) ] |> List.choose id)"
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

    let supportWith (map: string option) (fields: IdlField list option) : GenSupport =
        { GenSupport.Empty with
            KindProjections = Map.ofList [ "Gate", projection map fields ] }

    /// The projection as a host supplies it in full.
    let support = supportWith (Some gateMap) (Some recordFields)

    let tags = [ "Gate"; "Leaf" ]

    let derive (sup: GenSupport) (ds: Derivation list) (idl: Idl) =
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

    /// Run a script under `dotnet fsi` with compiler `flags`: its exit code and both streams, or
    /// None without `dotnet`.
    let runFsiWith (flags: string) (source: string) : (int * string * string) option =
        let path =
            Path.Combine(Path.GetTempPath(), sprintf "fuaran-403-%s.fsx" (System.Guid.NewGuid().ToString("N")))

        File.WriteAllText(path, source)

        try
            ChildProcess.run (ChildProcess.command "dotnet" ("fsi " + flags + "\"" + path + "\""))
            |> Result.toOption
            |> Option.map (fun p -> p.Exit, p.Stdout, p.Stderr)
        finally
            try
                File.Delete path
            with _ ->
                ()

    let runFsi = runFsiWith ""

    let dllRef (name: string) =
        "#r @\"" + Path.Combine(System.AppContext.BaseDirectory, name) + "\"\n"

    /// A script compiling the emitted module `name` ahead of `body`.
    let scriptOf (name: string) (moduleText: string) (body: string) =
        dllRef "Fuaran.Core.Text.dll"
        + dllRef "Fuaran.Core.Wire.dll"
        + dllRef "Fuaran.Core.Tree.dll"
        + dllRef "Fuaran.Core.Validator.dll"
        + moduleText.Replace("module " + name + "\n", "")
        + "\n\n"
        + body

    let script = scriptOf "Projected"

    let derivations =
        [ Derivation.StructuralAccess
          Derivation.KeyedPositions
          Derivation.SlotsOf "Rule"
          Derivation.MapMsg ]

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
              "printfn \"children\\t%s\" (children gate |> List.map _.Id |> String.concat \",\")"
              "printfn \"keyed\\t%s\" (keyedChildren gate |> List.map _.Id |> String.concat \",\")"
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
                  Projected.emitted (Projected.derive Projected.support [ Derivation.MapMsg ] Projected.idl)

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
                          [ Derivation.MapMsg ]
                          Projected.idl
                  )

              Expect.equal construct "the message map over the projected kind 'Gate'" "the construct, by name"

              Expect.equal
                  principle
                  "a projected kind's record is verbatim host source, so the generator cannot construct it"
                  "the principle, unchanged")

          testCase "the slot enumerator reads the RECORD's shape: a required member, not the wire's option" (fun _ ->
              let src =
                  Projected.emitted (Projected.derive Projected.support [ Derivation.SlotsOf "Rule" ] Projected.idl)

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
                  (fst (Projected.refusal (Projected.derive undeclared [ Derivation.StructuralAccess ] Projected.idl)))
                  "the structural children of the projected kind 'Gate'"
                  "the public structural access"

              // Nodes only within a record: the witness has no child to read, the keyed walk does.
              let keyedOnly = without [ "body" ]

              Expect.isOk
                  (Gen.fsharpModuleWith undeclared "Projected" keyedOnly Projected.tags)
                  "no direct node on the wire: the witness reads nothing of the record"

              Expect.equal
                  (fst (Projected.refusal (Projected.derive undeclared [ Derivation.KeyedPositions ] keyedOnly)))
                  "the keyed node positions of the projected kind 'Gate'"
                  "the keyed walk"

              // No node on the wire at all: the node walks admit it; the slot enumerator still
              // refuses, because a record may hold a value its wire spells otherwise.
              let nodeFree = without [ "body"; "steps" ]

              Expect.isOk
                  (Projected.derive undeclared [ Derivation.StructuralAccess; Derivation.KeyedPositions ] nodeFree)
                  "a node-free wire: no node position to read"

              Expect.equal
                  (fst (Projected.refusal (Projected.derive undeclared [ Derivation.SlotsOf "Rule" ] nodeFree)))
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
              let snap (sup: GenSupport) =
                  match
                      Diff.parse
                          (Artifact.render Projected.idl)
                          (Some(SupportArtifact.render { Support = sup; HostPrelude = None }))
                  with
                  | Ok s -> s.Support
                  | Error e -> failtestf "snapshot: %s" e

              let full = snap Projected.support
              Expect.notEqual (snap (Projected.supportWith None (Some Projected.recordFields))) full "the map"
              Expect.notEqual (snap (Projected.supportWith (Some Projected.gateMap) None)) full "the record fields") ]

// ---------------------------------------------------------------------------
// Phase 511 — the laws of `Decl`, the ONE resolution every emitter reads. The emitters' bytes
// are held by the drift guards; these hold the resolution itself, over every certification
// vocabulary this suite carries, so a change to the walk that happens to leave today's fixtures
// unmoved is still caught by the property it breaks.
// ---------------------------------------------------------------------------

module private DeclLaws =
    open Fuaran.Core.Idl.Emit

    let field name ty opt =
        { Name = name
          Type = ty
          Opt = opt
          Aliases = []
          Presence = None
          Annotations = Annotations.Empty }

    let case tag fields =
        { Tag = tag
          Fields = fields
          Annotations = Annotations.Empty
          Hosted = None }

    /// A vocabulary whose node ENVELOPE is the only root of a record and an enum — reached from
    /// no kind — so a resolution that forgot the envelope as a root would drop them.
    let enveloped: Idl =
        { MiniIdl.miniIdl with
            Enums = MiniIdl.miniIdl.Enums @ [ Declare.enumOf "Tone" [ "Quiet"; "Loud" ] ]
            Records =
                MiniIdl.miniIdl.Records
                @ [ { Name = "Style"
                      Fields = [ field "tone" (TEnum "Tone") Required ]
                      Annotations = Annotations.Empty } ]
            NodeFields = [ field "style" (TRecord "Style") Optional ] }

    /// Every vocabulary the suite carries ahead of this file, named.
    let vocabularies () : (string * Idl) list =
        let vectors, _, _ = DecodeVectorsIdl.current ()

        [ "mini", MiniIdl.miniIdl
          "derive", DeriveIdl.deriveIdl
          "reference", ReferenceIdl.refIdl
          "value-coverage", ReferenceIdl.valueCoverageIdl
          "unsorted-coverage", ReferenceIdl.unsortedCoverageIdl
          "score", ScoreDomainIdl.scoreIdl
          "second-domain", SecondDomainIdl.docIdl
          "doc-annotated", IdlDocAnnotationTests.docIdl
          "refusal-corners", RefusalCornersIdl.idl
          "decode-vectors", vectors
          "projected", Projected.idl
          "enveloped", enveloped ]

    let tagsOf (idl: Idl) = idl.Kinds |> List.map _.Tag

    let all (idl: Idl) = Decl.resolve idl Map.empty (tagsOf idl)

    /// The names a type mentions STRUCTURALLY (not through signature or hosted text).
    let rec named (t: IdlType) : string list =
        match t with
        | TEnum n
        | TRecord n -> [ n ]
        | TUnion(n, args) -> n :: List.collect named args
        | TList i
        | TMap i -> named i
        | THosted h -> h.Wire |> Option.map named |> Option.defaultValue []
        | _ -> []

    /// An independent reference walk — a breadth-first closure over names, scanning signature and
    /// hosted text for declared names as the reach rule states — so the resolution's reach is
    /// checked against a second spelling of the rule, not against itself.
    let referenceReach (idl: Idl) (kinds: IdlKind list) : Set<string> =
        let declaredNames =
            (idl.Enums |> List.map _.Name)
            @ (idl.Unions |> List.map _.Name)
            @ (idl.Records |> List.map _.Name)

        let textNames (text: string) =
            declaredNames |> List.filter (fun n -> text.Contains n)

        let rec mentions (t: IdlType) : string list =
            match t with
            | TFn sg -> textNames sg.FSharp
            | THosted h ->
                (h.Wire |> Option.map mentions |> Option.defaultValue [])
                @ textNames (h.FSharp + " " + h.Encode + " " + h.Decode)
            | TUnion(n, args) -> n :: List.collect mentions args
            | TList i
            | TMap i -> mentions i
            | TEnum n
            | TRecord n -> [ n ]
            | _ -> []

        let fieldsOfName (n: string) : IdlField list =
            match idl.Unions |> List.tryFind (fun u -> u.Name = n) with
            | Some u -> u.Cases |> List.collect _.Fields
            | None ->
                match idl.Records |> List.tryFind (fun r -> r.Name = n) with
                | Some r -> r.Fields
                | None -> []

        let roots =
            (kinds |> List.collect _.Fields) @ idl.NodeFields
            |> List.collect (fun f -> mentions f.Type)

        let rec close (seen: Set<string>) (frontier: string list) =
            match frontier with
            | [] -> seen
            | n :: rest when seen.Contains n -> close seen rest
            | n :: rest ->
                let next = fieldsOfName n |> List.collect (fun f -> mentions f.Type)
                close (seen.Add n) (rest @ next)

        close Set.empty roots

    let reachedNames (d: Decl) =
        Set.unionMany
            [ d.Enums |> List.map _.Name |> Set.ofList
              d.Unions |> List.map _.Name |> Set.ofList
              d.Records |> List.map _.Name |> Set.ofList ]

    /// A generic union whose recursion GROWS its argument: no finite instantiation set.
    let growing: Idl =
        { MiniIdl.miniIdl with
            Unions =
                [ { TagRule = Fuaran.Core.Idl.UnionTagRule.CaseName
                    Name = "Grow"
                    Params = [ "T" ]
                    Cases =
                      [ case "Leaf" [ field "value" (TVar "T") Required ]
                        case "Deeper" [ field "next" (TUnion("Grow", [ TList(TVar "T") ])) Required ] ]
                    Annotations = Annotations.Empty } ]
            Kinds =
                [ { Tag = "Holder"
                    Category = "x"
                    Fields = [ field "g" (TUnion("Grow", [ TStr ])) Required ]
                    Annotations = Annotations.Empty } ]
            Records = []
            Enums = []
            NodeFields = [] }

    /// Every type application of a generic union inside `t`.
    let rec applications (t: IdlType) : IdlType list =
        match t with
        | TUnion(_, args) when not args.IsEmpty -> t :: List.collect applications args
        | TUnion(_, args) -> List.collect applications args
        | TList x
        | TMap x -> applications x
        | _ -> []

open Fuaran.Core.Idl.Emit

[<Tests>]
let declLaws =
    let each (name: string) (law: Idl -> unit) =
        testCase name (fun () ->
            for (vocabulary, idl) in DeclLaws.vocabularies () do
                try
                    law idl
                with ex ->
                    failtestf "[%s] %s" vocabulary ex.Message)

    testList
        "Phase 511 — the laws of the one resolution (Decl)"
        [ each "reach is exactly the reference walk's closure from the selected kinds and the envelope" (fun idl ->
              let d = DeclLaws.all idl
              Expect.equal (DeclLaws.reachedNames d) (DeclLaws.referenceReach idl d.Kinds) "reach")

          each "reach is closed: every declared name a reached declaration mentions is reached" (fun idl ->
              let d = DeclLaws.all idl
              let reached = DeclLaws.reachedNames d

              let declared =
                  Set.unionMany
                      [ idl.Enums |> List.map _.Name |> Set.ofList
                        idl.Unions |> List.map _.Name |> Set.ofList
                        idl.Records |> List.map _.Name |> Set.ofList ]

              let fieldLists =
                  (d.Kinds |> List.map _.Fields)
                  @ [ idl.NodeFields ]
                  @ (d.Unions |> List.collect (fun u -> u.Cases |> List.map _.Fields))
                  @ (d.Records |> List.map _.Fields)

              for fs in fieldLists do
                  for f in fs do
                      for n in DeclLaws.named f.Type do
                          if declared.Contains n then
                              Expect.isTrue (reached.Contains n) (sprintf "'%s' (field '%s') is reached" n f.Name))

          each "the reached declarations are in DECLARATION order, the kinds in the caller's order" (fun idl ->
              let d = DeclLaws.all idl
              let names = DeclLaws.reachedNames d
              Expect.equal d.Enums (idl.Enums |> List.filter (fun e -> names.Contains e.Name)) "enums"
              Expect.equal d.Unions (idl.Unions |> List.filter (fun u -> names.Contains u.Name)) "unions"
              Expect.equal d.Records (idl.Records |> List.filter (fun r -> names.Contains r.Name)) "records"
              let reversed = DeclLaws.tagsOf idl |> List.rev
              let r = Decl.resolve idl Map.empty (reversed @ [ "NoSuchKind" ])

              Expect.equal
                  (r.Kinds |> List.map _.Tag)
                  reversed
                  "kinds in the caller's order; an unknown tag selects nothing")

          each "reach is monotone in the selection: one kind reaches a subset of all of them" (fun idl ->
              let whole = DeclLaws.reachedNames (DeclLaws.all idl)

              for tag in DeclLaws.tagsOf idl do
                  let one = DeclLaws.reachedNames (Decl.resolve idl Map.empty [ tag ])
                  Expect.isTrue (Set.isSubset one whole) (sprintf "kind '%s'" tag))

          each "the whole-vocabulary resolution holds every declaration, in declaration order" (fun idl ->
              let d = Decl.declared idl
              Expect.equal d.Enums idl.Enums "enums"
              Expect.equal d.Unions idl.Unions "unions"
              Expect.equal d.Records idl.Records "records"
              Expect.equal d.Kinds idl.Kinds "kinds"
              Expect.isTrue d.Projections.IsEmpty "no host projections")

          each "the node holders are the least fixpoint over the reached declarations" (fun idl ->
              let d = DeclLaws.all idl
              let hs = d.NodeHolders
              Expect.isTrue (Set.isSubset hs (DeclLaws.reachedNames d)) "only reached declarations hold"

              for u in d.Unions do
                  let holds =
                      u.Cases
                      |> List.exists (fun c -> c.Fields |> List.exists (fun f -> Decl.holdsIn hs f.Type))

                  Expect.equal (hs.Contains u.Name) holds (sprintf "union '%s'" u.Name)

              for r in d.Records do
                  let holds = r.Fields |> List.exists (fun f -> Decl.holdsIn hs f.Type)
                  Expect.equal (hs.Contains r.Name) holds (sprintf "record '%s'" r.Name))

          each "the message parameter reaches the tree all at once: Node, NodeKind and a dispatching spec" (fun idl ->
              let d = DeclLaws.all idl
              let specs = idl.Kinds |> List.exists (fun k -> d.Msg.Contains(k.Tag + "Spec"))
              Expect.equal (d.Msg.Contains "Node") specs "Node"
              Expect.equal (d.Msg.Contains "NodeKind") specs "NodeKind")

          each "wire equality goes only to a non-generic declaration holding a host-only closure" (fun idl ->
              let d = DeclLaws.all idl

              let closure (f: IdlField) =
                  f.Opt = HostOnly
                  && (match f.Type with
                      | TFn s -> s.FSharp.Contains "->"
                      | _ -> false)

              for key in d.EqualityCapable do
                  let fields, generic =
                      if key = "Node" then
                          idl.NodeFields, d.Msg.Contains "Node"
                      elif key.StartsWith "R:" then
                          (idl.Records |> List.find (fun r -> r.Name = key.Substring 2)).Fields,
                          d.Msg.Contains(key.Substring 2)
                      else
                          (idl.Kinds |> List.find (fun k -> k.Tag = key.Substring 2)).Fields,
                          d.Msg.Contains(key.Substring 2 + "Spec")

                  Expect.isFalse generic (sprintf "'%s' is generic in the message" key)
                  Expect.isTrue (fields |> List.exists closure) (sprintf "'%s' holds a host-only closure" key))

          each "a host projection changes no reach and withdraws only its own kind from wire equality" (fun idl ->
              let plain = DeclLaws.all idl

              let projection: KindProjection =
                  { SpecDecl = ""
                    Encoder = ""
                    Decoder = ""
                    Mk = None
                    MapMsg = None
                    RecordFields = None }

              for k in idl.Kinds do
                  let projected =
                      Decl.resolve idl (Map.ofList [ k.Tag, projection ]) (DeclLaws.tagsOf idl)

                  Expect.equal (DeclLaws.reachedNames projected) (DeclLaws.reachedNames plain) "reach"
                  Expect.equal projected.NodeHolders plain.NodeHolders "node holders"
                  Expect.equal projected.Msg plain.Msg "msg"
                  Expect.isFalse (projected.EqualityCapable.Contains("S:" + k.Tag)) "the projected kind")

          each "the envelope layout is the declared shape: the kind under 'kind', or merged" (fun idl ->
              let d = DeclLaws.all idl
              Expect.equal d.Envelope.Members idl.NodeFields "the envelope members"

              match idl.Wire.NodeEnvelope, d.Envelope.Kind with
              | NodeEnvelopeShape.NestedKind, KindPlacement.NestedUnder "kind"
              | NodeEnvelopeShape.FlatKind, KindPlacement.Merged -> ()
              | shape, placement -> failtestf "%A placed as %A" shape placement)

          each
              "a case is bare exactly when it is the declared transparent case, printable bare only with one field"
              (fun idl ->
                  let d = DeclLaws.all idl

                  for u in d.Unions do
                      for c in u.Cases do
                          match Decl.caseLayout d u c, Decl.transparentTag d u with
                          | CaseLayout.Tagged, Some t -> Expect.notEqual t c.Tag (sprintf "%s.%s" u.Name c.Tag)
                          | CaseLayout.Tagged, None -> ()
                          | CaseLayout.Bare(Ok f), Some t ->
                              Expect.equal t c.Tag "the transparent tag"
                              Expect.equal [ f ] c.Fields "its one field"
                          | CaseLayout.Bare(Error _), Some t ->
                              Expect.equal t c.Tag "the transparent tag"
                              Expect.notEqual c.Fields.Length 1 "refused only off one field"
                          | layout, None -> failtestf "%s.%s laid out %A with no transparent case" u.Name c.Tag layout)

          each "the instantiations are closed, unique and ordered by instance name" (fun idl ->
              match Decl.instantiations idl with
              | Error e -> failtestf "refused: %s" (CodegenError.describe e)
              | Ok insts ->
                  let names = insts |> List.map _.Name
                  Expect.equal names (List.sort names) "ordered"
                  Expect.equal names (List.distinct names) "unique"

                  let generic (n: string) =
                      idl.Unions |> List.exists (fun u -> u.Name = n && not u.Params.IsEmpty)

                  for i in insts do
                      Expect.isNonEmpty i.Union.Params (sprintf "'%s' is of a generic union" i.Name)

                      for c in i.Union.Cases do
                          for f in c.Fields do
                              if f.Opt <> HostOnly then
                                  for a in DeclLaws.applications (TypeParams.substitute i.Subst f.Type) do
                                      match a with
                                      | TUnion(n, _) when generic n ->
                                          Expect.contains names (Decl.instName a) (sprintf "'%s' reaches it" i.Name)
                                      | _ -> ())

          testCase "a recursion that grows its type argument has no finite instantiation set, and says so" (fun () ->
              match Decl.instantiations DeclLaws.growing with
              | Error(CodegenError.UnsupportedConstruct(construct, _, _)) ->
                  Expect.stringContains construct "did not close" "the bound, named"
              | other -> failtestf "expected the bounded refusal, got %A" other)

          testCase "an instance name is a declaration's own name, with its arguments mangled after it" (fun () ->
              Expect.equal (Decl.instName (TUnion("Binding", []))) "Binding" "non-generic"
              Expect.equal (Decl.instName (TUnion("Binding", [ TFloat ]))) "Binding_float" "generic"

              Expect.equal
                  (Decl.instName (TUnion("Binding", [ TList(TRecord "SelectOption") ])))
                  "Binding_list_SelectOption"
                  "nested")

          each "the resolution is a function of its inputs" (fun idl ->
              Expect.equal (DeclLaws.all idl) (DeclLaws.all idl) "two resolutions agree") ]

// ---------------------------------------------------------------------------
// Phase 511 (S12) — the TypeScript backend's derivation admission is the F# path's admission over
// a resolution with NO host projections: a projection is F# source, and the TypeScript host
// declares none. So a projection that makes the F# path refuse a derivation (a projected kind
// whose record declares no fields, under a wire that holds a node) leaves the TypeScript backend
// admitting it, by rule rather than by accident; and without the projection the two agree.
// ---------------------------------------------------------------------------

[<Tests>]
let typescriptAdmission =
    testList
        "Phase 511 — TypeScript derivation admission (S12)"
        [ testCase "a projection that refuses the F# keyed walk does not reach the TypeScript backend" (fun () ->
              let request = [ Derivation.KeyedPositions ]

              let construct, _ =
                  Projected.refusal (Projected.derive (Projected.supportWith None None) request Projected.idl)

              Expect.stringContains construct "projected kind 'Gate'" "the F# path refuses over the projection"

              match Gen.typescriptModuleDerived request Projected.idl Projected.tags with
              | Ok text -> Expect.stringContains text "keyedChildren" "the TypeScript backend admits it"
              | Error e -> failtestf "the TypeScript backend refused: %s" (CodegenError.describe e))

          testCase "without the projection, the two backends admit and refuse alike" (fun () ->
              for request in
                  [ [ Derivation.KeyedPositions ]
                    [ Derivation.Fold "Trigger" ]
                    [ Derivation.StructuralAccess ]
                    [ Derivation.DefaultRecords ] ] do
                  let fs = Projected.derive GenSupport.Empty request Projected.idl
                  let ts = Gen.typescriptModuleDerived request Projected.idl Projected.tags

                  match fs, ts with
                  | Ok _, Ok _ -> ()
                  | Error a, Error b -> Expect.equal b a (sprintf "%A: one refusal" request)
                  | _ -> failtestf "%A: F# admits %b, TypeScript admits %b" request (Result.isOk fs) (Result.isOk ts)) ]

// ---------------------------------------------------------------------------
// Phase 540 — the slot enumerator over a PARAMETERISED union compiles under nullness checking.
// Its slots are listed at `obj`, and `box` answers `obj | null` once nullness is on, so a consumer
// building with `<Nullable>enable</Nullable>` refused the emission (FS3261). The compiled leg runs
// the emission under `dotnet fsi --checknulls+ --warnaserror+`, and runs the pre-540 spelling
// through the same probe, which must FAIL there, so the probe can tell the fix from the defect.
// ---------------------------------------------------------------------------

module private NullChecked =
    let field name ty opt =
        { Name = name
          Type = ty
          Opt = opt
          Aliases = []
          Presence = None
          Annotations = Annotations.Empty }

    let case tag fields =
        { Tag = tag
          Fields = fields
          Annotations = Annotations.Empty
          Hosted = None }

    let kind tag fields =
        { Tag = tag
          Category = "c"
          Fields = fields
          Annotations = Annotations.Empty }

    let binding t = TUnion("Binding", [ t ])

    /// A binding union generic in its value, held directly, optionally, in a list, and on the envelope.
    let idl: Idl =
        { Enums = []
          Unions =
            [ { TagRule = Fuaran.Core.Idl.UnionTagRule.CaseName
                Name = "Binding"
                Params = [ "T" ]
                Cases =
                  [ case "Lit" [ field "value" (TVar "T") Required ]
                    case "Path" [ field "path" TStr Required ] ]
                Annotations = Annotations.Empty } ]
          Records = []
          Kinds =
            [ kind
                  "Text"
                  [ field "text" (binding TStr) Required
                    field "size" (binding TInt) Optional
                    field "all" (TList(binding TBool)) Required ]
              kind "Leaf" [ field "x" TStr Required ] ]
          Defaults = []
          NodeFields = [ field "hidden" (binding TBool) Optional ]
          Ops = []
          Wire = WireShape.Default
          Harden = HardenPolicy.Undeclared }

    let emitted () =
        Projected.emitted (
            Gen.fsharpModuleDerived
                GenSupport.Empty
                [ Derivation.KeyedPositions; Derivation.SlotsOf "Binding" ]
                "NullChecked"
                idl
                [ "Text"; "Leaf" ]
        )

    let probe =
        String.concat
            "\n"
            [ "let n: Node ="
              "    { Id = \"t\""
              "      Kind = NodeKind.Text { Text = Binding.Lit \"a\"; Size = Some(Binding.Path \"p\"); All = [ Binding.Lit true ] }"
              "      Hidden = Some(Binding.Lit false) }"
              "printfn \"slots\t%s\" (slotsOfBinding n |> List.map fst |> String.concat \",\")" ]

    /// Compiled under nullness checking with every warning an error, as a strict consumer builds.
    let compile (moduleText: string) =
        Projected.runFsiWith "--checknulls+ --warnaserror+ " (Projected.scriptOf "NullChecked" moduleText probe)

[<Tests>]
let nullCheckedSlotTests =
    testList
        "Phase 540 - the slot enumerator compiles under nullness checking"
        [ testCase "a parameterised union's slots are upcast to obj, never boxed" (fun _ ->
              let src = NullChecked.emitted ()
              Expect.stringContains src "[ \"text\", (s.Text :> obj) ]" "a direct slot"
              Expect.stringContains src "Some __v -> [ \"size\", (__v :> obj) ]" "an optional slot"
              Expect.stringContains src "List.map (fun __v -> \"all\", (__v :> obj))" "a list of slots"
              Expect.isFalse (src.Contains "box ") "no box in the module")

          testCase
              "compiled with nullness checking and warnings as errors, it compiles and lists every slot; the boxed spelling does not"
              (fun _ ->
                  let src = NullChecked.emitted ()

                  match NullChecked.compile src with
                  | None -> skiptest "dotnet not on PATH - the nullness-checked compile is skipped"
                  | Some(code, out, err) ->
                      Expect.equal code 0 (sprintf "the emission compiles: %s" err)
                      Expect.stringContains out "slots\ttext,size,all,hidden" "kind fields first, then the envelope's"

                  // The pre-540 spelling, through the same probe: it emits, and it does not compile.
                  let boxed =
                      System.Text.RegularExpressions.Regex.Replace(src, @"\(([A-Za-z_.]+) :> obj\)", "box $1")

                  Expect.isTrue (boxed.Contains "box s.Text") "the substitution reached the slots"

                  match NullChecked.compile boxed with
                  | None -> skiptest "dotnet not on PATH"
                  | Some(code, _, err) ->
                      Expect.notEqual code 0 "boxed slots under nullness checking"
                      Expect.stringContains err "FS3261" "the nullness refusal, as recorded") ]

// ---------------------------------------------------------------------------
// Phase 541 — the overridable collecting decoders. A host hands `decode*AllWith` a
// `CollectingDecoders` record copied from `collectingDecoders` with one type overridden, and its
// decoder is the one applied at that type at EVERY depth — through a node list, a record list, a
// node field, a union's own recursion. Before 541 every nested position read the generator's
// private decoder, so an override could reach the root and nothing beneath it.
// ---------------------------------------------------------------------------

module private Overrides =
    let note (id: string) : Node<obj> =
        { Id = id
          Kind =
            NodeKind.Note
                { Text = "n"
                  Priority = Priority.Normal }
          Annotation = None }

    let section (id: string) (children: Node<obj> list) : Node<obj> =
        { Id = id
          Kind =
            NodeKind.Section
                { Title = "t"
                  Children = children
                  Owner = None
                  Estimate = None }
          Annotation = None }

    /// root (Section) > children[0] (Choice) > branches[0].body (Section) > children[0] (Note
    /// `x-deep`); the choice's rule nests `Equals` under `AllOf` and `Not`.
    let tree: Node<obj> =
        section
            "root"
            [ { Id = "choice"
                Kind =
                  NodeKind.Choice
                      { Branches =
                          [ { Label = "b"
                              Body = section "inner" [ note "x-deep" ] } ]
                        Otherwise = note "other"
                        Rule = Rule.AllOf [ Rule.Not(Rule.Equals("k", "v")) ] }
                Annotation = None } ]

    let json () : JVal =
        match Decoder.parse ReadOptions.strict (encodeNode tree) with
        | Ok j -> j
        | Error e -> failwithf "the fixture parses: %A" e

    /// A policy over the generated structure: every `Equals` value upper-cased.
    let upper: CollectingDecoders =
        { collectingDecoders with
            Rule =
                fun self j ->
                    collectingDecoders.Rule self j
                    |> Result.map (function
                        | Rule.Equals(k, v) -> Rule.Equals(k, v.ToUpperInvariant())
                        | r -> r) }

    let refusal: DecodeError =
        { Code = DecodeCode.OutOfRange
          Path = []
          Expected = "an id the host admits"
          Message = "an x- id is the host's" }

    /// A policy over the generated structure: a node whose id starts `x-` is refused.
    let refuseX: CollectingDecoders =
        { collectingDecoders with
            Node =
                fun self j ->
                    match collectingDecoders.Node self j with
                    | Ok n when n.Id.StartsWith("x-", System.StringComparison.Ordinal) -> Error [ refusal ]
                    | r -> r }

    /// A policy over a kind spec: every note's text replaced.
    let renamed: CollectingDecoders =
        { collectingDecoders with
            NoteSpec =
                fun self j ->
                    collectingDecoders.NoteSpec self j
                    |> Result.map (fun s -> { s with Text = "host" }) }

    let choiceRule (n: Node<obj>) =
        match n.Kind with
        | NodeKind.Section s ->
            match s.Children with
            | [ { Kind = NodeKind.Choice c } ] -> Some c.Rule
            | _ -> None
        | _ -> None

    let rec noteTexts (n: Node<obj>) : string list =
        [ match n.Kind with
          | NodeKind.Note s -> yield s.Text
          | _ -> ()
          for c in children n do
              yield! noteTexts c ]

    /// The emitted module of a vocabulary with the structural access and the collecting decoders
    /// requested. A `TFn` placeholder is the vocabulary author's text, spliced verbatim (Phase 460),
    /// and the derivations vocabulary's boxes its closure's result, which nullness checking refuses;
    /// it is upcast here so the compile below judges the emitter's text and nothing else.
    let emitted (name: string) (idl: Idl) =
        Projected
            .emitted(
                Gen.fsharpModuleDerived
                    GenSupport.Empty
                    [ Derivation.StructuralAccess; Derivation.SpecDecoders ]
                    name
                    idl
                    (idl.Kinds |> List.map _.Tag)
            )
            .Replace("box \"<closure>\"", "(\"<closure>\" :> obj)")

    let strict = "--checknulls+ --warnaserror+ "

    /// A decode result as comparable data: a node holds closures, so it compares by its encoding.
    let render (r: Result<Node<obj>, DecodeError list>) = r |> Result.map encodeNode

[<Tests>]
let collectingOverrideTests =
    testList
        "Phase 541 - the collecting decoders take host decoders at the types a host overrides"
        [ testCase "an override at a union is applied at depth, and the no-override entry is unchanged" (fun _ ->
              let j = Overrides.json ()
              let expected = Rule.AllOf [ Rule.Not(Rule.Equals("k", "V")) ]

              match decodeNodeJsonAllWith Overrides.upper j, decodeNodeJsonAll j with
              | Ok overridden, Ok plain ->
                  Expect.equal (Overrides.choiceRule overridden) (Some expected) "Equals under AllOf > Not"

                  Expect.equal
                      (encodeNode plain)
                      (encodeNode Overrides.tree)
                      "decodeNodeJsonAll is the no-override case"
              | r -> failtestf "both decode: %A" r)

          testCase "an override at the node is applied to a node four positions down, at its path" (fun _ ->
              match decodeNodeJsonAllWith Overrides.refuseX (Overrides.json ()) with
              | Error [ e ] ->
                  Expect.equal e.Message Overrides.refusal.Message "the host's defect"

                  Expect.equal
                      e.Path
                      [ PathSegment.Key "kind"
                        PathSegment.Key "children"
                        PathSegment.Index 0
                        PathSegment.Key "kind"
                        PathSegment.Key "branches"
                        PathSegment.Index 0
                        PathSegment.Key "body"
                        PathSegment.Key "kind"
                        PathSegment.Key "children"
                        PathSegment.Index 0 ]
                      "the defect sits at the nested node"
              | r -> failtestf "one defect, at depth: %A" r)

          testCase "an override at a kind spec is applied wherever a node holds that kind" (fun _ ->
              match decodeNodeJsonAllWith Overrides.renamed (Overrides.json ()) with
              | Ok n -> Expect.equal (Overrides.noteTexts n |> List.distinct) [ "host" ] "every note, at every depth"
              | Error e -> failtestf "decodes: %A" e)

          testCase "the generated record reproduces every no-override entry, clean and defective" (fun _ ->
              let clean = encodeNode Overrides.tree

              let broken =
                  clean.Replace("\"title\":\"t\"", "\"title\":1").Replace("\"label\":\"b\"", "\"label\":2")

              Expect.notEqual broken clean "the fixture is broken"

              for text in [ clean; broken ] do
                  Expect.equal
                      (Overrides.render (decodeNodeAllWith collectingDecoders text))
                      (Overrides.render (decodeNodeAll text))
                      "decodeNodeAll"

              match Decoder.parse ReadOptions.strict broken with
              | Ok j ->
                  Expect.equal
                      (Overrides.render (decodeNodeJsonAllWith collectingDecoders j))
                      (Overrides.render (decodeNodeJsonAll j))
                      "decodeNodeJsonAll"

                  match decodeNodeJsonAll j with
                  | Error es -> Expect.isGreaterThan es.Length 1 "every defect of the broken text"
                  | Ok _ -> failtest "the broken text is refused"
              | Error e -> failtestf "parses: %A" e)

          testCase
              "the emission compiles under nullness checking with warnings as errors, generic unions included"
              (fun _ ->
                  for name, idl in [ "Overridable", DeriveIdl.deriveIdl; "NullChecked", NullChecked.idl ] do
                      let src = Overrides.emitted name idl
                      Expect.stringContains src "type CollectingDecoders =" (name + ": the record")

                      Expect.stringContains
                          src
                          "let decodeNodeJsonAllWith (decoders: CollectingDecoders)"
                          (name + ": the entry")

                      match Projected.runFsiWith Overrides.strict (Projected.scriptOf name src "printfn \"ok\"") with
                      | None -> skiptest "dotnet not on PATH - the nullness-checked compile is skipped"
                      | Some(code, out, err) ->
                          Expect.equal code 0 (sprintf "%s compiles: %s" name err)
                          Expect.stringContains out "ok" (name + " ran")) ]

// ---------------------------------------------------------------------------
// Phase 542 — a union, record or enum annotated as a TYPE: the artifact carries it as an optional
// member, the F# and TypeScript declarations print it above the type, and a type that says
// nothing emits exactly what it always did.
// ---------------------------------------------------------------------------

module TypeAnnotated =

    let doc (text: string) =
        { Annotations.Empty with
            Doc = Some text }

    let unionDoc = "What a rule decides: whether a node is shown."
    let recordDoc = "Who owns a section.\nSecond line, kept verbatim."

    let enumDoc =
        "How urgent a section is. A */ in prose cannot end the TypeScript block."

    let enumAnnotations =
        { doc enumDoc with
            Deprecated =
                Some
                    { Replacement = Some "Measure"
                      Message = None } }

    /// The derivations vocabulary with one union, one record and one enum annotated as a type.
    let idl: Idl =
        let base' = DeriveIdl.deriveIdl

        { base' with
            Unions =
                base'.Unions
                |> List.map (fun u ->
                    if u.Name = "Rule" then
                        { u with Annotations = doc unionDoc }
                    else
                        u)
            Records =
                base'.Records
                |> List.map (fun r ->
                    if r.Name = "Owner" then
                        { r with Annotations = doc recordDoc }
                    else
                        r)
            Enums = base'.Enums |> List.map (fun e -> { e with Annotations = enumAnnotations }) }

    let fsharp (idl: Idl) =
        Projected.emitted (Gen.fsharpModuleWith GenSupport.Empty "TypeAnnotated" idl (tags idl))

    let typescript (idl: Idl) =
        Projected.emitted (Gen.typescriptDeclarations idl (tags idl))

    let lines (s: string) =
        s.Replace("\r\n", "\n").Split('\n') |> Array.toList

    /// The lines of `longer` that are not in `shorter`, when `shorter` is `longer` with only those
    /// lines deleted; `None` when anything else differs.
    let added (shorter: string) (longer: string) : string list option =
        let rec go (s: string list) (l: string list) (acc: string list) =
            match s, l with
            | [], rest -> Some(List.rev acc @ rest)
            | _, [] -> None
            | x :: xs, y :: ys when x = y -> go xs ys acc
            | _, y :: ys -> go s ys (y :: acc)

        go (lines shorter) (lines longer) []

    /// The `annotations` members the artifact writes on its unions, records and enums.
    let typeAnnotationKeys (idl: Idl) : int =
        match Artifact.json idl with
        | JObj root ->
            [ "unions"; "records"; "enums" ]
            |> List.sumBy (fun key ->
                match List.tryFind (fun (k, _) -> k = key) root with
                | Some(_, JArr items) ->
                    items
                    |> List.filter (function
                        | JObj ms -> ms |> List.exists (fun (k, _) -> k = "annotations")
                        | _ -> false)
                    |> List.length
                | _ -> 0)
        | _ -> failtest "the artifact is an object"

[<Tests>]
let typeAnnotationTests =
    testList
        "Phase 542 - unions, records and enums annotated as types"
        [ testCase "an annotated artifact round-trips, each type's annotations verbatim" (fun _ ->
              match Artifact.parse (Artifact.render TypeAnnotated.idl) with
              | Error e -> failtestf "the artifact did not read back: %s" e
              | Ok back ->
                  Expect.equal
                      back
                      (Artifact.canonicalise TypeAnnotated.idl)
                      "the read-back vocabulary is the declared one"

                  let union = back.Unions |> List.find (fun u -> u.Name = "Rule")
                  let record = back.Records |> List.find (fun r -> r.Name = "Owner")
                  Expect.equal union.Annotations.Doc (Some TypeAnnotated.unionDoc) "the union's doc"

                  Expect.equal
                      record.Annotations.Doc
                      (Some TypeAnnotated.recordDoc)
                      "the record's doc, line break kept"

                  Expect.equal back.Enums.Head.Annotations TypeAnnotated.enumAnnotations "the enum's whole set"
                  Expect.equal back.Enums.Head.CaseAnnotations [] "no case annotation was implied")

          testCase "an unannotated type writes no annotations member; each annotated type writes exactly one" (fun _ ->
              Expect.equal
                  (TypeAnnotated.typeAnnotationKeys DeriveIdl.deriveIdl)
                  0
                  "nothing for a type that says nothing"

              Expect.equal (TypeAnnotated.typeAnnotationKeys TypeAnnotated.idl) 3 "one per annotated type"

              match TypeAnnotated.added (Artifact.render DeriveIdl.deriveIdl) (Artifact.render TypeAnnotated.idl) with
              | None -> failtest "the annotated artifact is the unannotated one plus inserted lines"
              | Some extra -> Expect.isNonEmpty extra "the annotations were written")

          testCase "the F# declarations print each type's doc above it, and gain nothing else" (fun _ ->
              let plain = TypeAnnotated.fsharp DeriveIdl.deriveIdl
              let annotated = TypeAnnotated.fsharp TypeAnnotated.idl

              Expect.stringContains
                  annotated
                  ("/// " + TypeAnnotated.unionDoc + "\n[<RequireQualifiedAccess>]\ntype Rule =")
                  "the union's doc sits directly above it"

              Expect.stringContains
                  annotated
                  "/// Who owns a section.\n/// Second line, kept verbatim.\nand Owner ="
                  "the record's doc, one /// line per authored line"

              Expect.stringContains
                  annotated
                  ("/// "
                   + TypeAnnotated.enumDoc
                   + "\n/// **Deprecated.** Use `Measure` instead.\n[<System.Obsolete(")
                  "the enum's doc, its deprecation note, then its attribute"

              Expect.stringContains
                  annotated
                  "#nowarn \"44\""
                  "a deprecated type suppresses the warning in its own layer"

              match TypeAnnotated.added (Snapshots.unstamped plain) (Snapshots.unstamped annotated) with
              | None -> failtest "the annotated module is the unannotated one plus inserted lines"
              | Some extra ->
                  for l in extra do
                      Expect.isTrue
                          (l.StartsWith "///"
                           || l.StartsWith "[<System.Obsolete("
                           || l.StartsWith "#nowarn")
                          (sprintf "an added line is a doc, attribute or suppression line: %s" l))

          testCase "the TypeScript declarations carry each type's doc as a JSDoc block above it" (fun _ ->
              let plain = TypeAnnotated.typescript DeriveIdl.deriveIdl
              let annotated = TypeAnnotated.typescript TypeAnnotated.idl

              Expect.stringContains
                  annotated
                  ("/**\n * " + TypeAnnotated.unionDoc + "\n */\nexport type Rule = ")
                  "the union's block"

              Expect.stringContains
                  annotated
                  "/**\n * Who owns a section.\n * Second line, kept verbatim.\n */\nexport type Owner = "
                  "the record's block"

              Expect.stringContains
                  annotated
                  "/**\n * How urgent a section is. A *\\/ in prose cannot end the TypeScript block.\n * @deprecated Use `Measure` instead.\n */\nexport type Priority = "
                  "the enum's block, its */ broken, its deprecation a JSDoc tag"

              match TypeAnnotated.added (Snapshots.unstamped plain) (Snapshots.unstamped annotated) with
              | None -> failtest "the annotated declarations are the unannotated ones plus inserted lines"
              | Some extra ->
                  for l in extra do
                      Expect.isTrue
                          (l = "/**" || l = " */" || l.StartsWith " * ")
                          (sprintf "an added line is a JSDoc line: %s" l))

          testCase "the annotated F# compiles under nullness checking with warnings as errors" (fun _ ->
              // Through Phase 541's compiled emission (structural access + the collecting decoders,
              // the vocabulary's boxing placeholder upcast), so this judges the emitter's text alone.
              let src = Overrides.emitted "TypeAnnotated" TypeAnnotated.idl

              match
                  Projected.runFsiWith
                      "--checknulls+ --warnaserror+ "
                      (Projected.scriptOf "TypeAnnotated" src "printfn \"ok\"")
              with
              | None -> skiptest "dotnet not on PATH - the nullness-checked compile is skipped"
              | Some(code, out, err) ->
                  Expect.equal code 0 (sprintf "the annotated module compiles: %s" err)
                  Expect.stringContains out "ok" "it ran"

                  // The go-red: without the suppression the deprecated enum's own layer fails
                  // the strict build (FS0044), so the attribute is real and the `#nowarn` is why it passes.
                  let unsuppressed =
                      src.Split('\n')
                      |> Array.filter (fun l -> not (l.StartsWith "#nowarn \"44\""))
                      |> String.concat "\n"

                  match
                      Projected.runFsiWith
                          "--checknulls+ --warnaserror+ "
                          (Projected.scriptOf "TypeAnnotated" unsuppressed "printfn \"ok\"")
                  with
                  | None -> ()
                  | Some(code, _, err) ->
                      Expect.notEqual code 0 "without the suppression the strict build refuses it"
                      Expect.stringContains err "FS0044" "and refuses it for the deprecated type") ]

// ---------------------------------------------------------------------------
// Phase 545 — an OPTIONAL node field is a keyed position, not a child, in the node witness
// whether or not the module requests StructuralAccess. The private witness used to read node
// fields by type alone, so `Hint: Node option` was emitted as `[ s.Hint ]` in the children arm
// and the module refused to compile (FS0001) unless StructuralAccess was requested.
// ---------------------------------------------------------------------------

module private OptionalNode =
    let field name ty opt =
        { Name = name
          Type = ty
          Opt = opt
          Aliases = []
          Presence = None
          Annotations = Annotations.Empty }

    let kind tag fields =
        { Tag = tag
          Category = "c"
          Fields = fields
          Annotations = Annotations.Empty }

    /// A kind holding a node and an optional node, one holding only an optional node, and a leaf.
    let idl: Idl =
        { Enums = []
          Unions = []
          Records = []
          Kinds =
            [ kind "Box" [ field "child" TNode Required; field "hint" TNode Optional ]
              kind "Tip" [ field "hint" TNode Optional ]
              kind "Leaf" [ field "label" TStr Required ] ]
          Defaults = []
          NodeFields = []
          Ops = []
          Wire = WireShape.Default
          Harden = HardenPolicy.Undeclared }

    let emitted (ds: Derivation list) =
        Projected.emitted (Gen.fsharpModuleDerived GenSupport.Empty ds "OptionalNode" idl [ "Box"; "Tip"; "Leaf" ])

    let probe =
        String.concat
            "\n"
            [ "let leaf (id: string) : Node = { Id = id; Kind = NodeKind.Leaf { Label = id } }"
              "let box: Node = { Id = \"b\"; Kind = NodeKind.Box { Child = leaf \"c\"; Hint = Some(leaf \"h\") } }"
              "let tip: Node = { Id = \"t\"; Kind = NodeKind.Tip { Hint = Some(leaf \"h\") } }"
              "let ids (ns: Node list) = ns |> List.map (fun n -> n.Id) |> String.concat \",\""
              "printfn \"box\t%s\" (ids (nodeWitness.Children box))"
              "printfn \"tip\t%s\" (ids (nodeWitness.Children tip))"
              "let swapped = nodeWitness.ReplaceChildren box [ leaf \"d\" ]"
              "match swapped.Kind with"
              "| NodeKind.Box s -> printfn \"swapped\t%s\t%s\" s.Child.Id (s.Hint |> Option.map (fun h -> h.Id) |> Option.defaultValue \"-\")"
              "| _ -> printfn \"swapped\tlost\"" ]

    let compile (moduleText: string) =
        Projected.runFsiWith "--checknulls+ --warnaserror+ " (Projected.scriptOf "OptionalNode" moduleText probe)

[<Tests>]
let optionalNodeWitnessTests =
    testList
        "Phase 545 - the node witness treats an optional node field as a keyed position"
        [ for label, ds in
              [ "without StructuralAccess", []
                "with StructuralAccess", [ Derivation.StructuralAccess ] ] do
              testCase (sprintf "%s, the children arm holds only the required node" label) (fun _ ->
                  let src = OptionalNode.emitted ds
                  Expect.stringContains src "| NodeKind.Box s -> [ s.Child ]" "the box's child, and not its hint"
                  Expect.isFalse (src.Contains "[ s.Hint ]") "an optional node is never a child")

              testCase
                  (sprintf "%s, it compiles under nullness checking with warnings as errors and keeps the hint" label)
                  (fun _ ->
                      match OptionalNode.compile (OptionalNode.emitted ds) with
                      | None -> skiptest "dotnet not on PATH - the nullness-checked compile is skipped"
                      | Some(code, out, err) ->
                          Expect.equal code 0 (sprintf "the emission compiles: %s" err)
                          let out = out.Replace("\r\n", "\n")
                          Expect.stringContains out "box\tc\n" "the box's one child"
                          Expect.stringContains out "tip\t\n" "a kind holding only an optional node has no children"
                          Expect.stringContains out "swapped\td\th" "a replacement keeps the keyed hint") ]
