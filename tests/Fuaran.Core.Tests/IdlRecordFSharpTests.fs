module Fuaran.Core.Tests.IdlRecordFSharpTests

// ---------------------------------------------------------------------------
// Phase 732 — the F# backend emits a parameterised record.
//
// This file holds the F# backend to the interpreter over a parameterised record: what each entry
// emits for one (a generic record, its encoder and decoders taking one codec per parameter as a
// generic union's do, an inline one's members laid on its holder at the holder's argument), the
// refusals that stay, and the vocabulary-wide predicates the emitted module depends on.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Aliases = []
      Keyed = false
      Presence = None
      Annotations = Annotations.Empty }

let private kind (tag: string) (fields: IdlField list) : IdlKind =
    { Tag = tag
      Category = "probe"
      Fields = fields
      Annotations = Annotations.Empty }

let private vocabulary (kinds: IdlKind list) : Idl =
    { Kinds = kinds
      Unions = []
      Enums = []
      Records = []
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared
      Contracts = [] }

let private emitted (r: Result<string, CodegenError>) : string =
    match r with
    | Ok src -> src
    | Error e -> failtestf "the generator refused: %s" (CodegenError.describe e)

// ---------------------------------------------------------------------------
// The vocabulary-wide predicates: whether the vocabulary declares a hosted slot (its helpers are
// emitted only then) and which declarations carry the message parameter. Each descended a list and
// a map and no tuple, set or keyed map, so a hosted slot or a message-carrying member held only
// inside one of those emitted a module naming a helper it did not define, or a type parameter its
// declaration did not bind.
// ---------------------------------------------------------------------------

let private hosted: IdlType =
    THosted
        { FSharp = "System.Guid"
          Encode = "(fun (g: System.Guid) -> JStr(g.ToString()))"
          Decode = "(fun (j: JVal) -> match j with JStr s -> Ok(System.Guid.Parse s) | _ -> Error \"guid\")"
          Wire = Some TStr
          Format = Some HostedFormat.Uuid
          Refusal = HostedRefusal.Sentence }

let private handler: IdlType =
    TFn
        { FSharp = "unit -> 'Msg"
          TypeScript = "() => unknown"
          Placeholder = "(fun () -> Unchecked.defaultof<_>)" }

/// Every position a type can hold another in, beside the list and the map the predicates always
/// descended: what it is, the type holding `inner` there, and whether the declaration check admits
/// a hosted slot or a function there (a set's element and a keyed map's key must be orderable).
let private holdings (inner: IdlType) : (string * IdlType * bool) list =
    [ "a tuple's component", TTuple [ inner; TInt ], true
      "a keyed map's value", TKeyedMap(TInt, inner), true
      "a set's element", TSet inner, false
      "a keyed map's key", TKeyedMap(inner, TInt), false ]

[<Tests>]
let predicateTests =
    testList
        "Phase 732 — the vocabulary-wide predicates descend every type"
        [ testCase "a hosted slot held in a tuple, a set or a keyed map has its helpers emitted" (fun _ ->
              for where, t, admitted in holdings hosted do
                  let idl = vocabulary [ kind "K" [ f "held" t Required ] ]

                  Expect.equal
                      (List.isEmpty (Declare.errors idl))
                      admitted
                      (sprintf "%s: whether the declaration check admits it" where)

                  let src = emitted (Gen.fsharpModule "Probe" idl [ "K" ])

                  Expect.stringContains src "dHosted " (sprintf "%s: the decoder reads the slot through dHosted" where)

                  Expect.stringContains src "let private dHosted " (sprintf "%s: and the module defines it" where)

                  Expect.stringContains
                      src
                      "let private dFormat "
                      (sprintf "%s: and the format check the slot declares" where))

          testCase
              "a function over the message held in a tuple, a set or a keyed map makes its holder generic"
              (fun _ ->
                  for where, t, admitted in holdings handler do
                      // `Plain` holds a node (a keyed map's value may be one), so it is generic when `K` is.
                      let idl =
                          vocabulary
                              [ kind "K" [ f "held" t Required ]
                                kind "Plain" [ f "children" (TKeyedMap(TInt, TNode)) Required ] ]

                      Expect.equal
                          (List.isEmpty (Declare.errors idl))
                          admitted
                          (sprintf "%s: whether the declaration check admits it" where)

                      let src = emitted (Gen.fsharpModule "Probe" idl [ "K"; "Plain" ])

                      Expect.stringContains src "KSpec<'Msg> =" (sprintf "%s: the holder binds the parameter" where)
                      Expect.stringContains src "Node<'Msg> =" (sprintf "%s: and so does the node" where)

                      Expect.stringContains
                          src
                          "PlainSpec<'Msg> ="
                          (sprintf "%s: and a kind holding a node as a keyed map's value" where)) ]

// ---------------------------------------------------------------------------
// The committed generated module over the fixture vocabulary ([[ParameterisedRecordsIdl]]): compiled
// by this project, held to the generator, and held to the interpreter's bytes in both directions.
// ---------------------------------------------------------------------------

module G = Fuaran.Core.Tests.ParameterisedRecordsGenerated

[<Tests>]
let generatedTests =
    testList
        "Phase 732 — the generated F# module over the parameterised records"
        [ testCase
              "the committed ParameterisedRecordsGenerated.fs is what the generator emits (byte for byte)"
              (fun _ ->
                  let generated = emitted (ParameterisedRecordsIdl.generate ())
                  let path = Snapshots.repoFile ParameterisedRecordsIdl.generatedFile

                  Approval.write Approval.Regen Approval.Regenerated.ParameterisedRecords path generated
                  |> ignore

                  Expect.equal
                      generated
                      (System.IO.File.ReadAllText path)
                      "regenerate with: FUARAN_REGEN=ParameterisedRecordsGenerated, running IdlRecordFSharpTests")

          testCase "the generated module reads each pinned document and re-writes its bytes" (fun _ ->
              for what, _, bytes in ParameterisedRecordsIdl.documents do
                  match G.decodeNode bytes with
                  | Ok node -> Expect.equal (G.encodeNode node) bytes (what + ": re-written unchanged")
                  | Error e ->
                      failtestf "%s: the generated decoder refused: %s" what (Fuaran.Core.DecodeError.render e)) ]

[<Tests>]
let flatGeneratedTests =
    testList
        "Phase 732 — the generated F# module in the second wire configuration"
        [ testCase
              "the committed ParameterisedRecordsFlatGenerated.fs is what the generator emits (byte for byte)"
              (fun _ ->
                  let generated = emitted (ParameterisedRecordsIdl.generateFlat ())
                  let path = Snapshots.repoFile ParameterisedRecordsIdl.flatGeneratedFile

                  Approval.write Approval.Regen Approval.Regenerated.ParameterisedRecordsFlat path generated
                  |> ignore

                  Expect.equal
                      generated
                      (System.IO.File.ReadAllText path)
                      "regenerate with: FUARAN_REGEN=ParameterisedRecordsFlatGenerated, running IdlRecordFSharpTests") ]

// ---------------------------------------------------------------------------
// The derived members and the codec-only module ([[ParameterisedRecordsIdl.derived]]): each
// committed module is held to the generator, and what it derives over a parameterised record is
// held to the value.
// ---------------------------------------------------------------------------

module D = Fuaran.Core.Tests.ParameterisedRecordsDerivedGenerated
module C = Fuaran.Core.Tests.ParameterisedRecordsCodecGenerated

let private parsed (s: string) : Fuaran.Core.JVal =
    match Fuaran.Core.Decoder.parse Fuaran.Core.ReadOptions.strict s with
    | Ok j -> j
    | Error e -> failtestf "not JSON: %s (%s)" s (Fuaran.Core.DecodeError.render e)

let private written (j: Fuaran.Core.JVal) : string =
    Fuaran.Core.Canonical.write Fuaran.Core.Rendering.CrossHost j

let private derivedNode (doc: string) : D.Node<obj> =
    match D.decodeNode doc with
    | Ok n -> n
    | Error e -> failtestf "the derived module refused %s: %s" doc (Fuaran.Core.DecodeError.render e)

let private driftGuard (stem: string) (file: string) (generated: Result<string, CodegenError>) =
    let text = emitted generated
    let path = Snapshots.repoFile file
    Approval.write Approval.Regen stem path text |> ignore

    Expect.equal
        text
        (System.IO.File.ReadAllText path)
        (sprintf "regenerate with: FUARAN_REGEN=%s, running IdlRecordFSharpTests" stem)

let private aFloat (j: Fuaran.Core.JVal) : Result<float, Fuaran.Core.DecodeError> =
    match j with
    | Fuaran.Core.JFloat f -> Ok f
    | Fuaran.Core.JInt i -> Ok(float i)
    | other -> Error(Fuaran.Core.Decoder.wrongKind "number" other)

/// The bytes pinned for the first document of the vocabulary (the `Slider`).
let private sliderBytes: string =
    match ParameterisedRecordsIdl.documents with
    | (_, _, bytes) :: _ -> bytes
    | [] -> failwith "the fixture pins no document"

[<Tests>]
let derivedTests =
    testList
        "Phase 732 — the derived members and the codec-only module over a parameterised record"
        [ testCase
              "the committed ParameterisedRecordsDerivedGenerated.fs is what the generator emits (byte for byte)"
              (fun _ ->
                  driftGuard
                      Approval.Regenerated.ParameterisedRecordsDerived
                      ParameterisedRecordsIdl.derivedGeneratedFile
                      (ParameterisedRecordsIdl.generateDerived ()))

          testCase
              "the committed ParameterisedRecordsCodecGenerated.fs is what the generator emits (byte for byte)"
              (fun _ ->
                  driftGuard
                      Approval.Regenerated.ParameterisedRecordsCodec
                      ParameterisedRecordsIdl.codecGeneratedFile
                      (ParameterisedRecordsIdl.generateCodec ()))

          testCase
              "the derived vocabulary is well-formed, and both hosts read and re-write its documents alike"
              (fun _ ->
                  let idl = ParameterisedRecordsIdl.derived
                  Expect.isEmpty (Declare.errors idl) "well-formed"

                  let documents =
                      (ParameterisedRecordsIdl.documents |> List.map (fun (_, _, bytes) -> bytes))
                      @ ParameterisedRecordsIdl.derivedCorpus

                  Expect.equal
                      (List.length documents)
                      6
                      "the three pinned documents and the three of the further kinds"

                  for doc in documents do
                      match Decode.decode idl doc with
                      | Error m -> failtestf "the interpreter refused %s: %s" doc m
                      | Ok v -> Expect.equal (Encode.encode idl v) (Ok doc) "the interpreter re-writes it"

                      Expect.equal (D.encodeNode (derivedNode doc)) doc "the derived module re-writes it"

                      match D.decodeNodeAll doc with
                      | Ok n -> Expect.equal (D.encodeNode n) doc "and so does its collecting decoder"
                      | Error es -> failtestf "the collecting decoder refused %s: %A" doc es)

          testCase
              "a fold reads a record at the instantiation: a union held at a record's argument is visited"
              (fun _ ->
                  let tree =
                      match (derivedNode ParameterisedRecordsIdl.derivedCorpus[0]).Kind with
                      | D.NodeKind.Outline s -> s.Tree
                      | other -> failtestf "an Outline expected: %A" other

                  // `Tree.n` is the derived projection: a leaf's number, none for a branch.
                  let leaves =
                      D.Tree.fold
                          (fun acc t ->
                              match D.Tree.n t with
                              | Some n -> acc @ [ n ]
                              | None -> acc)
                          []
                          tree

                  // Behind `Kid<Tree>.child` (twice, nested) and behind `Tagged<Tree>.items`.
                  Expect.equal leaves [ 1; 2; 3 ] "every nested leaf, in preorder"
                  Expect.equal (D.Tree.fold (fun n _ -> n + 1) 0 tree) 5 "the value and every nested value")

          testCase "a projection, a slot enumerator and a default record are derived over a generic record" (fun _ ->
              let range: D.Ranged<int> = { Low = 1; High = 3; Steps = None }
              Expect.equal (D.Binding.range (D.Binding.Bounded range)) (Some range) "a case's generic record member"
              Expect.equal (D.Binding.range (D.Binding.Static 1)) None "and none where the case carries none"

              let local: D.LocalState<float> = { Initial = 2.5; StateKey = "k" }
              Expect.equal (D.Binding.payload (D.Binding.Local local)) (Some local) "an inline generic record member"

              Expect.equal
                  (D.slotsOfRanged (derivedNode sliderBytes) |> List.map fst)
                  [ "range" ]
                  "the slot of a generic record, by wire field name"

              let empty: D.Tagged<int> = D.defaultTagged
              Expect.equal empty { Tag = ""; Items = []; Note = None } "a generic record's default, at any argument"

              // A declared default that is a generic record's literal is restored and omitted.
              match (derivedNode ParameterisedRecordsIdl.derivedCorpus[1]).Kind with
              | D.NodeKind.Outline s -> Expect.equal s.Trail D.defaultTagged "restored where the document omits it"
              | other -> failtestf "an Outline expected: %A" other)

          testCase "the keyed walk and the message map rebuild a generic record holding a node" (fun _ ->
              let frame = derivedNode ParameterisedRecordsIdl.derivedCorpus[2]
              Expect.equal (D.keyedChildren frame |> List.map _.Id) [ "b" ] "the node inside `Panel<string>`"
              Expect.equal (D.children frame |> List.map _.Id) [ "r" ] "beside the kind's structural child"

              let renamed =
                  D.withKeyedChildren (D.keyedChildren frame |> List.map (fun n -> { n with Id = "z" })) frame

              Expect.equal (D.keyedChildren renamed |> List.map _.Id) [ "z" ] "re-assigned through the record"

              let mapped: D.Node<int> = D.mapMsg (fun (_: obj) -> 7) frame
              Expect.equal (D.keyedChildren mapped |> List.map _.Id) [ "b" ] "the message map keeps the record's node"

              match mapped.Kind with
              | D.NodeKind.Frame s ->
                  Expect.equal s.Panel.Meta "m" "and the member held at the record's parameter"
                  Expect.equal (s.OnOpen "x") 7 "and maps the handler"
              | other -> failtestf "a Frame expected: %A" other)

          testCase
              "the codec-only module writes and reads a generic record, an inline one included, strictly too"
              (fun _ ->
                  // What the interpreter writes for `Slider.range` and `Slider.binding` in the pinned document.
                  let range = """{"high":9.5,"low":0.5,"steps":[1.5,2.5]}"""
                  let local = """{"$type":"Local","initial":2.5,"stateKey":"k"}"""
                  Expect.stringContains sliderBytes ("\"range\":" + range) "the interpreter's bytes for the record"

                  Expect.stringContains
                      sliderBytes
                      ("\"binding\":" + local)
                      "and for the case holding the inline record"

                  match C.decodeRanged aFloat (parsed range) with
                  | Error e -> failtestf "refused: %s" (Fuaran.Core.DecodeError.render e)
                  | Ok v ->
                      Expect.equal
                          v
                          { Low = 0.5
                            High = 9.5
                            Steps = Some [ 1.5; 2.5 ] }
                          "read at the argument's decoder"

                      Expect.equal
                          (written (C.encodeRanged Fuaran.Core.JFloat v))
                          range
                          "and written back to the bytes"

                  match C.decodeBinding aFloat (parsed local) with
                  | Error e -> failtestf "refused: %s" (Fuaran.Core.DecodeError.render e)
                  | Ok v ->
                      Expect.equal v (D.Binding.Local { Initial = 2.5; StateKey = "k" }) "the inline record's members"
                      Expect.equal (written (C.encodeBinding Fuaran.Core.JFloat v)) local "written beside the tag"

                  Expect.isOk
                      (C.decodeRangedStrict aFloat (parsed range))
                      "the strict reading admits the declared members"

                  for extra in
                      [ """{"high":9.5,"low":0.5,"other":1}"""
                        """{"$type":"Bounded","range":{"high":1,"low":0,"other":1}}"""
                        """{"$type":"Local","initial":2.5,"stateKey":"k","other":1}""" ] do
                      let answer =
                          if extra.Contains "$type" then
                              C.decodeBindingStrict aFloat (parsed extra) |> Result.map ignore
                          else
                              C.decodeRangedStrict aFloat (parsed extra) |> Result.map ignore

                      match answer with
                      | Ok() -> failtestf "the strict reading admitted %s" extra
                      | Error e ->
                          Expect.equal
                              e.Code
                              Fuaran.Core.DecodeCode.UndeclaredMember
                              (extra + ": an undeclared member")) ]

// ---------------------------------------------------------------------------
// The value printers, and the refusals the F# backend keeps over a parameterised record. Each kept
// refusal is one a generic union already meets in the same words, or the declaration's own: none
// is the refusal of an entry that does not emit a parameterised record.
// ---------------------------------------------------------------------------

let private record (name: string) (pars: string list) (fields: IdlField list) : IdlRecord =
    { Name = name
      Params = pars
      Fields = fields
      Annotations = Annotations.Empty
      Inline = false }

let private union (name: string) (pars: string list) (cases: (string * IdlField list) list) : IdlUnion =
    { Name = name
      Params = pars
      Cases =
        cases
        |> List.map (fun (tag, fields) ->
            { Tag = tag
              Fields = fields
              Annotations = Annotations.Empty
              Hosted = None })
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

let private refusal (r: Result<string, CodegenError>) : string =
    match r with
    | Ok _ -> failtest "the generator emitted where a refusal was expected"
    | Error e -> CodegenError.describe e

[<Tests>]
let printerAndRefusalTests =
    testList
        "Phase 732 — the value printers and the kept refusals over a parameterised record"
        [ testCase "an authored value of a parameterised record prints each member at its argument" (fun _ ->
              let idl = ParameterisedRecordsIdl.idl

              // `value` is the record's parameter: an enum at one reference, a record at another.
              Expect.equal
                  (Gen.fsharpValue
                      idl
                      (TRecord("Entry", [ TStr; TEnum "Direction" ]))
                      (VRecord [ "key", VStr "name"; "value", VEnum "desc" ]))
                  (Ok "(Ok({ Key = \"name\"; Value = Some(Direction.desc) }) : Result<_, Fuaran.Core.DecodeError>)")
                  "an enum's host case at the parameter"

              Expect.equal
                  (Gen.fsharpValue
                      idl
                      (TRecord("Labelled", [ TRecord("Ranged", [ TInt ]) ]))
                      (VRecord [ "label", VStr "w"; "value", VRecord [ "low", VInt 2; "high", VInt 4 ] ]))
                  (Ok
                      "(Ok({ Label = \"w\"; Value = { Low = 2; High = 4; Steps = None } }) : Result<_, Fuaran.Core.DecodeError>)")
                  "a record applied to a record"

              // The value does not fit the argument: refused, not printed at the declared variable.
              Expect.isError
                  (Gen.fsharpValue
                      idl
                      (TRecord("Labelled", [ TInt ]))
                      (VRecord [ "label", VStr "w"; "value", VStr "x" ]))
                  "a string where the argument is an integer")

          testCase "a declared default that is a parameterised record's literal is printed at its argument" (fun _ ->
              let src = emitted (ParameterisedRecordsIdl.generateDerived ())

              Expect.stringContains
                  src
                  "(if s.Trail = { Tag = \"\"; Items = []; Note = None } then None else Some(\"trail\", (encTagged JStr) s.Trail))"
                  "the encoder omits the member at the literal"

              Expect.stringContains
                  src
                  "dDef \"trail\" __fs (decTagged dStr) ({ Tag = \"\"; Items = []; Note = None })"
                  "and the decoder restores it"

              // A member of the literal held AT the parameter is printed at the argument: read as
              // declared it is a value at a type variable, which no literal spells.
              let atParameter =
                  { vocabulary
                        [ kind
                              "K"
                              [ f
                                    "pin"
                                    (TRecord("Pinned", [ TInt ]))
                                    (OmitDefault(VRecord [ "label", VStr "a"; "value", VInt 3 ])) ] ] with
                      Records = [ record "Pinned" [ "T" ] [ f "label" TStr Required; f "value" (TVar "T") Required ] ] }

              Expect.isEmpty (Declare.errors atParameter) "the declaration is admitted"

              Expect.stringContains
                  (emitted (Gen.fsharpModule "Probe" atParameter [ "K" ]))
                  "(if s.Pin = { Label = \"a\"; Value = 3 } then None else Some(\"pin\", (encPinned JInt) s.Pin))"
                  "the member at the parameter is an integer literal")

          testCase "an omit-at-default member at a type parameter is refused, for a union and a record alike" (fun _ ->
              // The default's type is the parameter's, known only at an instantiation, and the generic
              // encoder is one function for every instantiation: F# has no literal to test against.
              let holder =
                  kind
                      "K"
                      [ f "u" (TUnion("U", [ TInt ])) Required
                        f "r" (TRecord("R", [ TInt ])) Required ]

              let member' = f "at" (TVar "T") (OmitDefault(VInt 0))

              let asUnion =
                  { vocabulary [ holder ] with
                      Unions = [ union "U" [ "T" ] [ "Only", [ member' ] ] ]
                      Records = [ record "R" [ "T" ] [ f "plain" (TVar "T") Required ] ] }

              let asRecord =
                  { vocabulary [ holder ] with
                      Unions = [ union "U" [ "T" ] [ "Only", [ f "plain" (TVar "T") Required ] ] ]
                      Records = [ record "R" [ "T" ] [ member' ] ] }

              for what, idl in [ "a union case's member", asUnion; "a record's member", asRecord ] do
                  Expect.isEmpty (Declare.errors idl) (what + ": the declaration is admitted")

                  Expect.equal
                      (Gen.fsharpModule "Probe" idl [ "K" ])
                      (Error(CodegenError.UnsupportedDefault(TVar "T", VInt 0)))
                      (what + ": refused as a default no literal spells"))

          testCase
              "a node held through a generic record's argument is refused by the keyed walk, as a union's is"
              (fun _ ->
                  let idl =
                      { vocabulary
                            [ kind "K" [ f "boxed" (TRecord("Box", [ TNode ])) Required ]
                              kind "Leaf" [ f "n" TInt Required ] ] with
                          Records = [ record "Box" [ "T" ] [ f "held" (TVar "T") Required ] ] }

                  // The structural module emits the record; only the per-declaration walk refuses.
                  Expect.isOk
                      (Gen.fsharpModule "Probe" idl [ "K"; "Leaf" ])
                      "the codec reads the node through the argument"

                  Expect.stringContains
                      (refusal (
                          Gen.fsharpModuleDerived
                              GenSupport.Empty
                              [ Derivation.KeyedPositions ]
                              "Probe"
                              idl
                              [ "K"; "Leaf" ]
                      ))
                      "a node held through a type argument of the generic record 'Box'"
                      "by the record's name"

                  let asUnion =
                      { idl with
                          Records = []
                          Unions = [ union "Box" [ "T" ] [ "Only", [ f "held" (TVar "T") Required ] ] ]
                          Kinds =
                              [ kind "K" [ f "boxed" (TUnion("Box", [ TNode ])) Required ]
                                kind "Leaf" [ f "n" TInt Required ] ] }

                  Expect.stringContains
                      (refusal (
                          Gen.fsharpModuleDerived
                              GenSupport.Empty
                              [ Derivation.KeyedPositions ]
                              "Probe"
                              asUnion
                              [ "K"; "Leaf" ]
                      ))
                      "a node held through a type argument of the generic union 'Box'"
                      "the union's sentence, word for word but the one")

          testCase
              "the message parameter carried through a generic record's argument is refused by the message map"
              (fun _ ->
                  let handler: IdlType =
                      TFn
                          { FSharp = "string -> 'Msg"
                            TypeScript = "(x: string) => Msg"
                            Placeholder = "(fun (_: string) -> box \"<closure>\")" }

                  let idl =
                      { vocabulary [ kind "K" [ f "boxed" (TRecord("Box", [ handler ])) Required ] ] with
                          Records = [ record "Box" [ "T" ] [ f "held" (TVar "T") Required ] ] }

                  let src = emitted (Gen.fsharpModule "Probe" idl [ "K" ])

                  Expect.stringContains
                      src
                      "KSpec<'Msg> ="
                      "the holder is generic in the message, through the argument"

                  Expect.stringContains src "Box<'T> =" "and the record is generic in its own parameter alone"

                  Expect.stringContains
                      (refusal (Gen.fsharpModuleDerived GenSupport.Empty [ Derivation.MapMsg ] "Probe" idl [ "K" ]))
                      "the message map through a type argument of 'Box' at K.boxed"
                      "by the record's name and the member")

          testCase "a fold through a record whose recursion grows its type argument is refused by name" (fun _ ->
              // `Grow<T>` names `Grow<T list>` at every step: the fold has no last instantiation to read.
              let idl =
                  { vocabulary [ kind "K" [ f "tree" (TUnion("Tree", [])) Required ] ] with
                      Unions =
                          [ union
                                "Tree"
                                []
                                [ "Leaf", []
                                  "Branch", [ f "grown" (TRecord("Grow", [ TUnion("Tree", []) ])) Required ] ] ]
                      Records =
                          [ record
                                "Grow"
                                [ "T" ]
                                [ f "here" (TVar "T") Required
                                  f "next" (TRecord("Grow", [ TList(TVar "T") ])) Optional ] ] }

              Expect.isOk
                  (Gen.fsharpModule "Probe" idl [ "K" ])
                  "the codec emits the growing record (F# recurses polymorphically)"

              Expect.stringContains
                  (refusal (Gen.fsharpModuleDerived GenSupport.Empty [ Derivation.Fold "Tree" ] "Probe" idl [ "K" ]))
                  "a fold over 'Tree' through the generic record 'Grow', which has no finite set of instantiations: Grow<'T> reaches Grow<list<'T>>"
                  "by the union's and the record's names")

          testCase
              "a record holding a host-only function takes wire equality only where it declares no parameter"
              (fun _ ->
                  let hostOnly: IdlType =
                      TFn
                          { FSharp = "string -> unit"
                            TypeScript = "(x: string) => void"
                            Placeholder = "ignore" }

                  let idl (pars: string list) =
                      { vocabulary
                            [ kind "K" [ f "plain" (TRecord("Plain", pars |> List.map (fun _ -> TInt))) Required ] ] with
                          Records = [ record "Plain" pars [ f "n" TInt Required; f "onTick" hostOnly HostOnly ] ] }

                  let plain = emitted (Gen.fsharpModule "Probe" (idl []) [ "K" ])

                  Expect.stringContains
                      plain
                      "[<CustomEquality; NoComparison>]\ntype Plain ="
                      "the record compares its wire members"

                  let generic = emitted (Gen.fsharpModule "Probe" (idl [ "T" ]) [ "K" ])
                  Expect.stringContains generic "type Plain<'T> =" "the generic record is declared"
                  Expect.isFalse (generic.Contains "CustomEquality") "and takes no custom equality") ]
