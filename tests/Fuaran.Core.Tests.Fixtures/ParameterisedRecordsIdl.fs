module Fuaran.Core.Tests.ParameterisedRecordsIdl

// ---------------------------------------------------------------------------
// Phase 732 — a vocabulary of PARAMETERISED RECORDS, one in every position a record reference
// can stand, for each backend to be held to the interpreter over.
//
//   * a plain field ..................... `Slider.range: Ranged<float>`
//   * a list's item ..................... `Slider.marks: Labelled<float> list`
//   * a union case's payload ............ `Shape.Span.extent: Ranged<float>` (the union declares no
//                                         parameter), `Binding<T>.Bounded.range: Ranged<T>` (the case
//                                         passes its own parameter down)
//   * an inline member of a kind ........ `Grid.paging: Paging<string>`, its members the grid's own
//   * an inline payload of a parameterised case
//                                         `Binding<T>.Local.payload: LocalState<T>`, its members
//                                         beside the case's tag
//   * two instantiations of one record .. `Ranged<float>` and `Ranged<int>`; `Labelled<float>`,
//                                         `Labelled<string>` and `Labelled<Ranged<int>>`
//   * a record of a record .............. `Stepper.window: Labelled<Ranged<int>>` (an argument that
//                                         is itself an applied record), and `Series<T>.points:
//                                         Labelled<T> list` (a record passing its parameter on)
//   * two parameters .................... `Entry<K, V>`, bound by name
//   * a declaration reached only as a record's argument
//                                         the enum `Direction`, at `Grid.sort: Entry<string, Direction>`
//
// No member is host-only, hosted or a function, so every backend that emits a parameterised record
// emits this vocabulary whole.
// ---------------------------------------------------------------------------

open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

let private field (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Aliases = []
      Keyed = false
      Presence = None
      Annotations = Annotations.Empty }

let private record (name: string) (pars: string list) (isInline: bool) (fields: IdlField list) : IdlRecord =
    { Name = name
      Params = pars
      Fields = fields
      Annotations = Annotations.Empty
      Inline = isInline }

let private case (tag: string) (fields: IdlField list) : IdlUnionCase =
    { Tag = tag
      Fields = fields
      Annotations = Annotations.Empty
      Hosted = None }

let private union (name: string) (pars: string list) (cases: IdlUnionCase list) : IdlUnion =
    { Name = name
      Params = pars
      Cases = cases
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = UnionDiscrimination.Tagged }

let private kind (tag: string) (fields: IdlField list) : IdlKind =
    { Tag = tag
      Category = "parameterised"
      Fields = fields
      Annotations = Annotations.Empty }

let private ranged (t: IdlType) : IdlType = TRecord("Ranged", [ t ])
let private labelled (t: IdlType) : IdlType = TRecord("Labelled", [ t ])

/// The parameterised records, and what each holds its parameter in.
let private records: IdlRecord list =
    [ record
          "Ranged"
          [ "T" ]
          false
          [ field "low" (TVar "T") Required
            field "high" (TVar "T") Required
            field "steps" (TList(TVar "T")) Optional ]
      record "Labelled" [ "T" ] false [ field "label" TStr Required; field "value" (TVar "T") Required ]
      record "Series" [ "T" ] false [ field "points" (TList(labelled (TVar "T"))) Required ]
      record "Entry" [ "K"; "V" ] false [ field "key" (TVar "K") Required; field "value" (TVar "V") Optional ]
      record "Paging" [ "T" ] true [ field "pageSize" TInt Required; field "cursor" (TVar "T") Optional ]
      record "LocalState" [ "T" ] true [ field "initial" (TVar "T") Required; field "stateKey" TStr Required ] ]

let private unions: IdlUnion list =
    [ union
          "Binding"
          [ "T" ]
          [ case "Static" [ field "value" (TVar "T") Required ]
            case "Local" [ field "payload" (TRecord("LocalState", [ TVar "T" ])) Required ]
            case "Bounded" [ field "range" (ranged (TVar "T")) Required ] ]
      union "Shape" [] [ case "Span" [ field "extent" (ranged TFloat) Required ]; case "Point" [] ] ]

/// The vocabulary: every position of the header, reached from three kinds.
let idl: Idl =
    { Kinds =
        [ kind
              "Slider"
              [ field "range" (ranged TFloat) Required
                field "marks" (TList(labelled TFloat)) Optional
                field "binding" (TUnion("Binding", [ TFloat ])) Required
                field "shape" (TUnion("Shape", [])) Optional ]
          kind
              "Stepper"
              [ field "range" (ranged TInt) Required
                field "window" (labelled (ranged TInt)) Optional
                field "binding" (TUnion("Binding", [ TInt ])) Optional ]
          kind
              "Grid"
              [ field "paging" (TRecord("Paging", [ TStr ])) Required
                field "title" (labelled TStr) Optional
                field "series" (TRecord("Series", [ TFloat ])) Optional
                field "sort" (TRecord("Entry", [ TStr; TEnum "Direction" ])) Optional ] ]
      Unions = unions
      Enums = [ Declare.enumOf "Direction" [ "asc"; "desc" ] ]
      Records = records
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared
      Contracts = [] }

/// The kinds of [[idl]], in declared order — the selection every backend is asked for.
let kindTags: string list = idl.Kinds |> List.map _.Tag

/// One document per kind, between them a value in every position of the header: what it shows, the
/// authored value, and the canonical bytes the interpreter writes for it. Every backend that emits
/// the vocabulary is held to these bytes in both directions.
let documents: (string * IdlValue * string) list =
    [ "a plain field, a list's item, an inline payload of a parameterised case, a case's payload",
      VNode(
          "s",
          "Slider",
          [ "range",
            VRecord
                [ "low", VFloat 0.5
                  "high", VFloat 9.5
                  "steps", VList [ VFloat 1.5; VFloat 2.5 ] ]
            "marks", VList [ VRecord [ "label", VStr "a"; "value", VFloat 1.5 ] ]
            "binding", VUnion("Local", [ "payload", VRecord [ "initial", VFloat 2.5; "stateKey", VStr "k" ] ])
            "shape", VUnion("Span", [ "extent", VRecord [ "low", VFloat 0.5; "high", VFloat 1.5 ] ]) ]
      ),
      "{\"id\":\"s\",\"kind\":{\"$type\":\"Slider\",\"binding\":{\"$type\":\"Local\",\"initial\":2.5,\"stateKey\":\"k\"},\"marks\":[{\"label\":\"a\",\"value\":1.5}],\"range\":{\"high\":9.5,\"low\":0.5,\"steps\":[1.5,2.5]},\"shape\":{\"$type\":\"Span\",\"extent\":{\"high\":1.5,\"low\":0.5}}}}"
      "a second instantiation, a record of a record, a record held by a parameterised case",
      VNode(
          "p",
          "Stepper",
          [ "range", VRecord [ "low", VInt 1; "high", VInt 9 ]
            "window", VRecord [ "label", VStr "w"; "value", VRecord [ "low", VInt 2; "high", VInt 4 ] ]
            "binding", VUnion("Bounded", [ "range", VRecord [ "low", VInt 1; "high", VInt 3 ] ]) ]
      ),
      "{\"id\":\"p\",\"kind\":{\"$type\":\"Stepper\",\"binding\":{\"$type\":\"Bounded\",\"range\":{\"high\":3,\"low\":1}},\"range\":{\"high\":9,\"low\":1},\"window\":{\"label\":\"w\",\"value\":{\"high\":4,\"low\":2}}}}"
      "an inline member of a kind, a record passing its parameter on, two parameters",
      VNode(
          "g",
          "Grid",
          [ "paging", VRecord [ "pageSize", VInt 20; "cursor", VStr "c" ]
            "title", VRecord [ "label", VStr "t"; "value", VStr "v" ]
            "series", VRecord [ "points", VList [ VRecord [ "label", VStr "p"; "value", VFloat 0.5 ] ] ]
            "sort", VRecord [ "key", VStr "name"; "value", VEnum "desc" ] ]
      ),
      "{\"id\":\"g\",\"kind\":{\"$type\":\"Grid\",\"cursor\":\"c\",\"pageSize\":20,\"series\":{\"points\":[{\"label\":\"p\",\"value\":0.5}]},\"sort\":{\"key\":\"name\",\"value\":\"desc\"},\"title\":{\"label\":\"t\",\"value\":\"v\"}}}" ]

// ---------------------------------------------------------------------------
// The F# backend over the vocabulary. `ParameterisedRecordsGenerated.fs` is the module emitted for
// `idl` with the collecting decoders requested, committed, compiled by the suite and held to the
// generator.
// ---------------------------------------------------------------------------

/// The module name the committed generated file declares.
[<Literal>]
let moduleName = "Fuaran.Core.Tests.ParameterisedRecordsGenerated"

/// The committed generated file, repository-relative.
[<Literal>]
let generatedFile = "tests/Fuaran.Core.Tests/ParameterisedRecordsGenerated.fs"

/// The module text the generator emits for `idl`, with the collecting decoders requested.
let generate () : Result<string, CodegenError> =
    Gen.fsharpModuleDerived GenSupport.Empty [ Derivation.SpecDecoders ] moduleName idl kindTags

// ---------------------------------------------------------------------------
// The second wire configuration the three-host differential runs (the document and score
// vocabularies'): a `kind` discriminator, the kind merged into the node envelope, members in declared
// order. `ParameterisedRecordsFlatGenerated.fs` is the plain structural module emitted for it.
// ---------------------------------------------------------------------------

/// [[idl]] under the second wire configuration.
let flat: Idl =
    { idl with
        Wire =
            { Discriminator = "kind"
              NodeEnvelope = NodeEnvelopeShape.FlatKind
              KeyOrder = KeyOrder.Declared } }

/// The bytes the interpreter writes for each of [[documents]] under [[flat]], in their order.
let flatBytes: string list =
    [ "{\"kind\":\"Slider\",\"id\":\"s\",\"range\":{\"low\":0.5,\"high\":9.5,\"steps\":[1.5,2.5]},\"marks\":[{\"label\":\"a\",\"value\":1.5}],\"binding\":{\"kind\":\"Local\",\"initial\":2.5,\"stateKey\":\"k\"},\"shape\":{\"kind\":\"Span\",\"extent\":{\"low\":0.5,\"high\":1.5}}}"
      "{\"kind\":\"Stepper\",\"id\":\"p\",\"range\":{\"low\":1,\"high\":9},\"window\":{\"label\":\"w\",\"value\":{\"low\":2,\"high\":4}},\"binding\":{\"kind\":\"Bounded\",\"range\":{\"low\":1,\"high\":3}}}"
      "{\"kind\":\"Grid\",\"id\":\"g\",\"pageSize\":20,\"cursor\":\"c\",\"title\":{\"label\":\"t\",\"value\":\"v\"},\"series\":{\"points\":[{\"label\":\"p\",\"value\":0.5}]},\"sort\":{\"key\":\"name\",\"value\":\"desc\"}}" ]

/// Every wire configuration the vocabulary is pinned in: its name, the vocabulary under it, and
/// each document as what it shows, the authored value and the bytes pinned for it.
let configurations: (string * Idl * (string * IdlValue * string) list) list =
    [ "default", idl, documents
      "flat", flat, List.map2 (fun (what, value, _) bytes -> what, value, bytes) documents flatBytes ]

/// The module name the committed generated file of [[flat]] declares.
[<Literal>]
let flatModuleName = "Fuaran.Core.Tests.ParameterisedRecordsFlatGenerated"

/// The committed generated file of [[flat]], repository-relative.
[<Literal>]
let flatGeneratedFile =
    "tests/Fuaran.Core.Tests/ParameterisedRecordsFlatGenerated.fs"

/// The plain structural module the generator emits for [[flat]].
let generateFlat () : Result<string, CodegenError> =
    Gen.fsharpModule flatModuleName flat kindTags

// ---------------------------------------------------------------------------
// The derived members and the codec-only module over a parameterised record. `derived` is `idl` and
// two more kinds, because the derivations read shapes `idl` does not hold: a union that holds itself
// at a record's ARGUMENT (`Tree.Branch.kids: Kid<Tree> list`, `spare: Tagged<Tree>`), so its
// fold must read the record at the instantiation; a generic record every member of which has a
// value without the caller (`Tagged<T>`), so it has a default record; a declared default that is a
// generic record's literal (`Outline.trail`); and a generic record holding a node beside a function
// over the message (`Panel<T>` in `Frame`), so the keyed walk and the message map rebuild one.
// `ParameterisedRecordsDerivedGenerated.fs` is the F# emitted for it with `derivations` requested,
// and `ParameterisedRecordsCodecGenerated.fs` the codec-only module over its node-free declarations;
// both are committed, compiled by the suite and held to the generator.
// ---------------------------------------------------------------------------

let private handler: IdlType =
    TFn
        { FSharp = "string -> 'Msg"
          TypeScript = "(x: string) => Msg"
          Placeholder = "(fun (_: string) -> box \"<closure>\")" }

/// [[idl]] with the `Outline` and `Frame` kinds and what they reach.
let derived: Idl =
    { idl with
        Records =
            idl.Records
            @ [ record
                    "Tagged"
                    [ "T" ]
                    false
                    [ field "tag" TStr (OmitDefault(VStr ""))
                      field "items" (TList(TVar "T")) (OmitDefault(VList []))
                      field "note" TStr Optional ]
                record "Kid" [ "T" ] false [ field "name" TStr Required; field "child" (TVar "T") Required ]
                record "Panel" [ "T" ] false [ field "body" TNode Required; field "meta" (TVar "T") Required ] ]
        Unions =
            idl.Unions
            @ [ union
                    "Tree"
                    []
                    [ case "Leaf" [ field "n" TInt Required ]
                      case
                          "Branch"
                          [ field "kids" (TList(TRecord("Kid", [ TUnion("Tree", []) ]))) Required
                            field "spare" (TRecord("Tagged", [ TUnion("Tree", []) ])) Optional ] ] ]
        Kinds =
            idl.Kinds
            @ [ kind
                    "Outline"
                    [ field "tree" (TUnion("Tree", [])) Required
                      field
                          "trail"
                          (TRecord("Tagged", [ TStr ]))
                          (OmitDefault(VRecord [ "tag", VStr ""; "items", VList [] ])) ]
                kind
                    "Frame"
                    [ field "panel" (TRecord("Panel", [ TStr ])) Required
                      field "rest" (TList TNode) Required
                      field "onOpen" handler Required ] ] }

/// Every derivation, over the declarations of [[derived]] that hold a parameterised record.
let derivations: Derivation list =
    [ Derivation.StructuralAccess
      Derivation.KeyedPositions
      Derivation.SlotsOf "Ranged"
      Derivation.SlotsOf "Panel"
      Derivation.SlotsOf "Tree"
      Derivation.MapMsg
      Derivation.Fold "Tree"
      Derivation.Projections("Tree", [ "kids"; "n" ])
      Derivation.Projections("Binding", [ "range"; "payload" ])
      Derivation.DefaultRecords
      Derivation.VocabularyConstants
      Derivation.SpecDecoders ]

/// Documents of the two further kinds, each in canonical (sorted) form: a tree three deep whose
/// nested values sit behind a record's argument, with a trail away from its default; a leaf at the
/// default trail; and a frame holding a node inside a generic record.
let derivedCorpus: string list =
    [ """{"id":"o1","kind":{"$type":"Outline","trail":{"items":["a","b"],"tag":"t"},"tree":{"$type":"Branch","kids":[{"child":{"$type":"Leaf","n":1},"name":"l"},{"child":{"$type":"Branch","kids":[{"child":{"$type":"Leaf","n":2},"name":"n"}]},"name":"m"}],"spare":{"items":[{"$type":"Leaf","n":3}],"note":"x"}}}}"""
      """{"id":"o2","kind":{"$type":"Outline","tree":{"$type":"Leaf","n":7}}}"""
      """{"id":"f1","kind":{"$type":"Frame","onOpen":"<closure>","panel":{"body":{"id":"b","kind":{"$type":"Outline","tree":{"$type":"Leaf","n":9}}},"meta":"m"},"rest":[{"id":"r","kind":{"$type":"Outline","tree":{"$type":"Leaf","n":8}}}]}}""" ]

/// The module name the committed derived file declares.
[<Literal>]
let derivedModuleName = "Fuaran.Core.Tests.ParameterisedRecordsDerivedGenerated"

/// The committed derived file, repository-relative.
[<Literal>]
let derivedGeneratedFile =
    "tests/Fuaran.Core.Tests/ParameterisedRecordsDerivedGenerated.fs"

/// The module text the generator emits for [[derived]], with [[derivations]] requested.
let generateDerived () : Result<string, CodegenError> =
    Gen.fsharpModuleDerived GenSupport.Empty derivations derivedModuleName derived (derived.Kinds |> List.map _.Tag)

/// The codec-only request: every generic record that has a JSON value of its own and the unions
/// holding one as roots (an inline record is no root, and `Panel` reaches a node, which a codec-only
/// module does not print), each reached declaration at the host type the derived module declares.
let codecRequest: CodecOnly =
    { Roots =
        [ "Ranged"
          "Labelled"
          "Series"
          "Entry"
          "Tagged"
          "Kid"
          "Binding"
          "Shape"
          "Tree" ]
      HostTypes =
        (derived.Unions |> List.map _.Name)
        @ (derived.Records |> List.map _.Name)
        @ (derived.Enums |> List.map _.Name)
        |> List.map (fun n -> n, derivedModuleName + "." + n)
        |> Map.ofList }

/// The module name the committed codec-only file declares.
[<Literal>]
let codecModuleName = "Fuaran.Core.Tests.ParameterisedRecordsCodecGenerated"

/// The committed codec-only file, repository-relative.
[<Literal>]
let codecGeneratedFile =
    "tests/Fuaran.Core.Tests/ParameterisedRecordsCodecGenerated.fs"

/// The codec-only module text the generator emits for [[codecRequest]].
let generateCodec () : Result<string, CodegenError> =
    Gen.fsharpCodecModule GenSupport.Empty codecRequest codecModuleName derived

// ---------------------------------------------------------------------------
// The F* proof model over a parameterised record. Two generated models are committed and checked by
// the proof leg, one per wire configuration: `proofs/ParameterisedVocabulary.fst` over [[derived]]
// (every position of the header, and what `derived` adds: a union that holds itself at a record's
// argument, a declared default that is a generic record's literal, a generic record holding a node)
// and `proofs/ParameterisedFlatVocabulary.fst` over [[derivedFlat]], the same declarations where an
// inline record's members share the node's own object with the envelope's. The decode vectors below
// hold each model's READING to the interpreter's: documents the interpreter wrote, and documents it
// refuses.
// ---------------------------------------------------------------------------

/// [[derived]] under the second wire configuration ([[flat]]'s).
let derivedFlat: Idl = { derived with Wire = flat.Wire }

/// The documents the model of [[derived]] reads: one per kind of [[idl]], then [[derivedCorpus]].
let modelDocuments: string list =
    (documents |> List.map (fun (_, _, bytes) -> bytes)) @ derivedCorpus

/// [[modelDocuments]] as the interpreter writes each under [[derivedFlat]]: the documents that
/// model reads. The first three are [[flatBytes]].
let flatModelDocuments () : string list =
    modelDocuments
    |> List.map (fun doc ->
        match Decode.decode derived doc |> Result.bind (Encode.encode derivedFlat) with
        | Ok bytes -> bytes
        | Error m -> failwithf "the interpreter did not rewrite %s under the flat configuration: %s" doc m)

/// Documents the interpreter REFUSES under [[derived]], each with what it shows. No document holds
/// a number that is not an integer, so each is carried into the model verbatim.
let refused: (string * string) list =
    [ "a member of `Ranged<int>` holding what `Ranged<string>` would",
      """{"id":"p","kind":{"$type":"Stepper","range":{"high":"9","low":"1"}}}"""
      "`Labelled<string>` holding the value of `Labelled<Ranged<int>>`",
      """{"id":"g","kind":{"$type":"Grid","pageSize":20,"title":{"label":"t","value":{"high":4,"low":2}}}}"""
      "`Labelled<Ranged<int>>` holding the value of `Labelled<string>`",
      """{"id":"p","kind":{"$type":"Stepper","range":{"high":9,"low":1},"window":{"label":"w","value":"v"}}}"""
      "a required member of an instantiation missing", """{"id":"p","kind":{"$type":"Stepper","range":{"low":1}}}"""
      "a required member of an inline record missing from the kind's own object",
      """{"id":"g","kind":{"$type":"Grid","cursor":"c"}}"""
      "an inline record's member holding what another instantiation would",
      """{"id":"g","kind":{"$type":"Grid","cursor":5,"pageSize":20}}"""
      "a required member of an inline payload missing beside the case's tag",
      """{"id":"p","kind":{"$type":"Stepper","binding":{"$type":"Local","initial":2},"range":{"high":9,"low":1}}}"""
      "an inline payload's member holding what the union's other instantiation would",
      """{"id":"p","kind":{"$type":"Stepper","binding":{"$type":"Local","initial":"x","stateKey":"k"},"range":{"high":9,"low":1}}}"""
      "a record held by a parameterised case, a required member missing",
      """{"id":"p","kind":{"$type":"Stepper","binding":{"$type":"Bounded","range":{"high":3}},"range":{"high":9,"low":1}}}"""
      "two parameters: the second argument's enum at a spelling it does not declare",
      """{"id":"g","kind":{"$type":"Grid","pageSize":20,"sort":{"key":"name","value":"sideways"}}}"""
      "two parameters: the member at the first argument missing",
      """{"id":"g","kind":{"$type":"Grid","pageSize":20,"sort":{"value":"desc"}}}"""
      "a record applied to a union, its member holding no value of that union",
      """{"id":"o","kind":{"$type":"Outline","tree":{"$type":"Branch","kids":[{"child":"x","name":"l"}]}}}"""
      "a generic record at a declared default, a member of the authored value at another argument's type",
      """{"id":"o","kind":{"$type":"Outline","trail":{"items":[1],"tag":"t"},"tree":{"$type":"Leaf","n":7}}}""" ]

/// Documents the interpreter REFUSES under [[derivedFlat]], each with what it shows.
let flatRefused: (string * string) list =
    [ "a member of `Ranged<int>` holding what `Ranged<string>` would",
      """{"kind":"Stepper","id":"p","range":{"low":"1","high":"9"}}"""
      "a required member of an instantiation missing", """{"kind":"Stepper","id":"p","range":{"low":1}}"""
      "a required member of an inline record missing from the node's own object",
      """{"kind":"Grid","id":"g","cursor":"c"}"""
      "an inline record's member holding what another instantiation would",
      """{"kind":"Grid","id":"g","pageSize":20,"cursor":5}"""
      "a required member of an inline payload missing beside the case's tag",
      """{"kind":"Stepper","id":"p","range":{"low":1,"high":9},"binding":{"kind":"Local","initial":2}}"""
      "`Labelled<string>` holding the value of `Labelled<Ranged<int>>`",
      """{"kind":"Grid","id":"g","pageSize":20,"title":{"label":"t","value":{"low":2,"high":4}}}"""
      "a record applied to a union, its member holding no value of that union",
      """{"kind":"Outline","id":"o","tree":{"kind":"Branch","kids":[{"name":"l","child":"x"}]}}"""
      "a generic record at a declared default, a member of the authored value at another argument's type",
      """{"kind":"Outline","id":"o","tree":{"kind":"Leaf","n":7},"trail":{"tag":"t","items":[1]}}""" ]

// ---------------------------------------------------------------------------
// A declared default that is a generic record's literal, where the member's type applies the record
// to an argument the encoder cannot ask equality of. `Tagged<T>` is the record; its literal is the
// default of
//
//   * a member of a generic record ............ `Outer<T>.inner: Tagged<T>`
//   * a member of a generic union's case ...... `Pick<T>.Held.inner: Tagged<T>`
//   * a member of an inline generic record .... `Laid<T>.inner: Tagged<T>`, laid on a kind
//                                               (`Defaults.laid: Laid<string>`) and on a generic case
//                                               (`Pick<T>.Carried.payload: Laid<T>`)
//   * a member at a closed argument that carries no equality in F#
//                                               `Defaults.format: Tagged<Format>`, `Format` holding a
//                                               function
//
// In the first three the argument is the holder's own parameter, so the generated encoder is generic
// in it. `Twin<T>` is declared after `Tagged<T>` with members of the same names and other types, so
// the literal must be read at the member's type and not at the last record that declares the names.
// `ParameterisedRecordsDefaultsGenerated.fs` is the F# emitted for the vocabulary, committed and
// compiled by the suite.
// ---------------------------------------------------------------------------

let private tagged (t: IdlType) : IdlType = TRecord("Tagged", [ t ])

let private atDefault: Optionality =
    OmitDefault(VRecord [ "tag", VStr ""; "items", VList [] ])

/// The vocabulary of the header: one kind reaching every holder.
let defaulted: Idl =
    { Kinds =
        [ kind
              "Defaults"
              [ field "outer" (TRecord("Outer", [ TFloat ])) Required
                field "choice" (TUnion("Pick", [ TInt ])) Optional
                field "laid" (TRecord("Laid", [ TStr ])) Required
                field "format" (tagged (TUnion("Format", []))) atDefault
                field "twin" (TRecord("Twin", [ TStr ])) Optional ] ]
      Unions =
        [ union
              "Pick"
              [ "T" ]
              [ case "Held" [ field "inner" (tagged (TVar "T")) atDefault; field "x" TInt Required ]
                case "Carried" [ field "payload" (TRecord("Laid", [ TVar "T" ])) Required ]
                case "Bare" [] ]
          union
              "Format"
              []
              [ case "Plain" []
                case
                    "Custom"
                    [ field
                          "render"
                          (TFn
                              { FSharp = "obj -> string"
                                TypeScript = "(x: unknown) => string"
                                Placeholder = "(fun (_: obj) -> \"\")" })
                          Required ] ] ]
      Enums = []
      Records =
        [ record
              "Tagged"
              [ "T" ]
              false
              [ field "tag" TStr (OmitDefault(VStr ""))
                field "items" (TList(TVar "T")) (OmitDefault(VList [])) ]
          record
              "Twin"
              [ "T" ]
              false
              [ field "tag" TInt Required
                field "items" (TList TInt) Required
                field "extra" (TVar "T") Required ]
          record "Outer" [ "T" ] false [ field "inner" (tagged (TVar "T")) atDefault; field "x" TInt Required ]
          record "Laid" [ "T" ] true [ field "inner" (tagged (TVar "T")) atDefault; field "y" TInt Required ] ]
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared
      Contracts = [] }

/// Documents of [[defaulted]] in canonical form, each with what it shows: every literal at its
/// default (so every member left out), and each literal away from it in every holder.
let defaultedDocuments: (string * string) list =
    [ "every default left out, the case holding the member",
      """{"id":"a","kind":{"$type":"Defaults","choice":{"$type":"Held","x":2},"outer":{"x":1},"y":3}}"""
      "every default left out, the case laying the inline record out",
      """{"id":"b","kind":{"$type":"Defaults","choice":{"$type":"Carried","y":4},"outer":{"x":1},"y":3}}"""
      "each literal away from its default: in the record, the case, the inline record on the kind, and at the closed argument",
      """{"id":"c","kind":{"$type":"Defaults","choice":{"$type":"Held","inner":{"items":[7]},"x":2},"format":{"items":[{"$type":"Plain"}]},"inner":{"tag":"k"},"outer":{"inner":{"items":[1.5],"tag":"t"},"x":1},"twin":{"extra":"e","items":[1],"tag":5},"y":3}}"""
      "the literal away from its default in the inline record laid on a case",
      """{"id":"d","kind":{"$type":"Defaults","choice":{"$type":"Carried","inner":{"items":[9]},"y":4},"outer":{"x":1},"y":3}}""" ]

/// Documents that WRITE a default the encoder leaves out, each with the canonical bytes it re-writes
/// to: the test that a value is at its default, asked in every holder.
let defaultedWritten: (string * string) list =
    [ """{"id":"a","kind":{"$type":"Defaults","choice":{"$type":"Held","inner":{"items":[],"tag":""},"x":2},"format":{"items":[],"tag":""},"inner":{"items":[],"tag":""},"outer":{"inner":{"items":[],"tag":""},"x":1},"y":3}}""",
      """{"id":"a","kind":{"$type":"Defaults","choice":{"$type":"Held","x":2},"outer":{"x":1},"y":3}}"""
      """{"id":"b","kind":{"$type":"Defaults","choice":{"$type":"Carried","inner":{},"y":4},"outer":{"inner":{},"x":1},"y":3}}""",
      """{"id":"b","kind":{"$type":"Defaults","choice":{"$type":"Carried","y":4},"outer":{"x":1},"y":3}}""" ]

/// The module name the committed file of [[defaulted]] declares.
[<Literal>]
let defaultedModuleName = "Fuaran.Core.Tests.ParameterisedRecordsDefaultsGenerated"

/// The committed generated file of [[defaulted]], repository-relative.
[<Literal>]
let defaultedGeneratedFile =
    "tests/Fuaran.Core.Tests/ParameterisedRecordsDefaultsGenerated.fs"

/// The module text the generator emits for [[defaulted]], with the collecting decoders and the
/// default records requested.
let generateDefaulted () : Result<string, CodegenError> =
    Gen.fsharpModuleDerived
        GenSupport.Empty
        [ Derivation.SpecDecoders; Derivation.DefaultRecords ]
        defaultedModuleName
        defaulted
        (defaulted.Kinds |> List.map _.Tag)
