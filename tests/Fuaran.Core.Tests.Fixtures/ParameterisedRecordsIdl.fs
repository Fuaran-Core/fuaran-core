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
