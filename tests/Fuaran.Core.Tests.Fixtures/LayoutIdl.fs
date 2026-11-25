module Fuaran.Core.Tests.LayoutIdl

// ---------------------------------------------------------------------------
// Phase 474 — the INLINE declarations, over a vocabulary built for them and its flat twin.
//
// `layout` declares all three shapes a vocabulary reshape needs without moving a byte:
//   * an inline record (`Paging`): `pageSize` / `pageStateKey` are the grid's own members;
//   * a record-payload union case (`Field.Text of payload: TextPayload`, `TextPayload` inline):
//     its members sit beside the tag exactly as positional fields do;
//   * two presence-keyed unions held inline: `RowIdentity` (optional — `rowKey` or `rowKeyField`)
//     and `GridRows` (required — `source`, or `staticRows` with the `source` placeholder the wire
//     always carried beside it, SYNTHESISED by the encoder).
//
// `flat` is the same wire declared the way it grew — flat members, positional fields, a required
// `source` beside an optional `staticRows`. Every document of the corpus below is read and written
// identically by both, which is the phase's acceptance: the reshape is the host's, never the wire's.
// `LayoutGenerated.fs` is the F# emitted for `layout` with the collecting decoders requested,
// committed and held to the generator by `IdlLayoutTests`.
//
// Phase 722 — in the fixtures library, because `layout` is also a certification vocabulary of the
// F* target: `FStarModels` generates `proofs/LayoutVocabulary.fst` and its proofs from it.
// ---------------------------------------------------------------------------

open Fuaran.Core
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

let private record (name: string) (isInline: bool) (fields: IdlField list) : IdlRecord =
    { Name = name
      Fields = fields
      Annotations = Annotations.Empty
      Inline = isInline }

let private case (tag: string) (fields: IdlField list) : IdlUnionCase =
    { Tag = tag
      Fields = fields
      Annotations = Annotations.Empty
      Hosted = None }

let private union (name: string) (discrimination: UnionDiscrimination) (cases: IdlUnionCase list) : IdlUnion =
    { Name = name
      Params = []
      Cases = cases
      Annotations = Annotations.Empty
      TagRule = UnionTagRule.CaseName
      Discrimination = discrimination }

let private kind (tag: string) (fields: IdlField list) : IdlKind =
    { Tag = tag
      Category = "layout"
      Fields = fields
      Annotations = Annotations.Empty }

/// The tagged feed a grid's `source` holds.
let private feed =
    union
        "Feed"
        UnionDiscrimination.Tagged
        [ case "Static" [ field "value" TJson Required ]
          case "State" [ field "key" TStr Required ] ]

/// The header-and-rows table `staticRows` holds — an ordinary record, its own object.
let private staticTable =
    record
        "StaticTable"
        false
        [ field "headers" (TList TStr) Required
          field "rows" (TList(TList TStr)) Required ]

/// The `source` placeholder the wire carries beside `staticRows`.
let staticSource: IdlValue = VUnion("Static", [ ("value", VJson(JArr [])) ])

let private base' (kinds: IdlKind list) (unions: IdlUnion list) (records: IdlRecord list) : Idl =
    { Kinds = kinds
      Unions = feed :: unions
      Enums = []
      Records = staticTable :: records
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared
      Contracts = [] }

/// The vocabulary declaring the three shapes.
let layout: Idl =
    base'
        [ kind
              "Grid"
              [ field "columns" (TList TStr) Required
                field "paging" (TRecord "Paging") Required
                field "identity" (TUnion("RowIdentity", [])) Optional
                field "rows" (TUnion("GridRows", [])) Required
                field "caption" TStr Optional ]
          kind "Form" [ field "field" (TUnion("Field", [])) Required ] ]
        [ union
              "RowIdentity"
              (UnionDiscrimination.ByPresence
                  [ { Case = "RowKey"
                      Key = "rowKey"
                      Synthesised = [] }
                    { Case = "RowKeyField"
                      Key = "rowKeyField"
                      Synthesised = [] } ])
              [ case "RowKey" [ field "rowKey" TClosure Required ]
                case "RowKeyField" [ field "rowKeyField" TStr Required ] ]
          union
              "GridRows"
              (UnionDiscrimination.ByPresence
                  [ { Case = "Source"
                      Key = "source"
                      Synthesised = [] }
                    { Case = "StaticRows"
                      Key = "staticRows"
                      Synthesised = [ ("source", staticSource) ] } ])
              [ case "Source" [ field "source" (TUnion("Feed", [])) Required ]
                case "StaticRows" [ field "staticRows" (TRecord "StaticTable") Required ] ]
          union
              "Field"
              UnionDiscrimination.Tagged
              [ case "Text" [ field "payload" (TRecord "TextPayload") Required ]
                case "Check" [ field "on" TBool Required; field "label" TStr Optional ] ] ]
        [ record "Paging" true [ field "pageSize" TInt Optional; field "pageStateKey" TStr Optional ]
          record "TextPayload" true [ field "value" TStr Required; field "placeholder" TStr Optional ] ]

/// The same wire, declared as it grew: flat members, positional fields, every key a sibling.
let flat: Idl =
    base'
        [ kind
              "Grid"
              [ field "columns" (TList TStr) Required
                field "pageSize" TInt Optional
                field "pageStateKey" TStr Optional
                field "rowKey" TClosure Optional
                field "rowKeyField" TStr Optional
                field "source" (TUnion("Feed", [])) Required
                field "staticRows" (TRecord "StaticTable") Optional
                field "caption" TStr Optional ]
          kind "Form" [ field "field" (TUnion("Field", [])) Required ] ]
        [ union
              "Field"
              UnionDiscrimination.Tagged
              [ case "Text" [ field "value" TStr Required; field "placeholder" TStr Optional ]
                case "Check" [ field "on" TBool Required; field "label" TStr Optional ] ] ]
        []

/// `layout` with its keys in DECLARED order, so the inline members' position is on the wire.
let layoutDeclared: Idl =
    { layout with
        Wire =
            { layout.Wire with
                KeyOrder = KeyOrder.Declared } }

/// `flat` with its keys in DECLARED order.
let flatDeclared: Idl =
    { flat with
        Wire =
            { flat.Wire with
                KeyOrder = KeyOrder.Declared } }

/// Documents every reading of the two vocabularies accepts, each in canonical (sorted) form.
let corpus: string list =
    [ """{"id":"g1","kind":{"$type":"Grid","columns":["a"],"source":{"$type":"State","key":"rows"}}}"""
      """{"id":"g2","kind":{"$type":"Grid","caption":"c","columns":[],"pageSize":10,"pageStateKey":"p","rowKeyField":"id","source":{"$type":"Static","value":[1,2]}}}"""
      """{"id":"g3","kind":{"$type":"Grid","columns":["x","y"],"rowKey":"<closure>","source":{"$type":"Static","value":[]},"staticRows":{"headers":["h"],"rows":[["1"]]}}}"""
      """{"id":"g4","kind":{"$type":"Grid","columns":[],"pageSize":3,"source":{"$type":"Static","value":[]},"staticRows":{"headers":[],"rows":[]}}}"""
      """{"id":"f1","kind":{"$type":"Form","field":{"$type":"Text","placeholder":"type here","value":"v"}}}"""
      """{"id":"f2","kind":{"$type":"Form","field":{"$type":"Text","value":""}}}"""
      """{"id":"f3","kind":{"$type":"Form","field":{"$type":"Check","label":"ok","on":true}}}""" ]

/// Documents the layout REFUSES, one per rule of the D220.3 reading and one inside each kind of
/// inline member: both keys of one union present; a synthesised member at another value; the
/// synthesised member absent; a required union with no key present; a defect in an inline record's
/// member; a defect in a record payload's member.
let refused: string list =
    let grid (members: string) : string =
        sprintf """{"id":"g","kind":{"$type":"Grid","columns":[]%s}}""" members

    [ grid ""","rowKey":"<closure>","rowKeyField":"id","source":{"$type":"State","key":"k"}"""
      grid ""","source":{"$type":"State","key":"k"},"staticRows":{"headers":[],"rows":[]}"""
      grid ""","staticRows":{"headers":[],"rows":[]}"""
      grid ""
      grid ""","pageSize":"ten","source":{"$type":"State","key":"k"}"""
      """{"id":"f","kind":{"$type":"Form","field":{"$type":"Text","placeholder":3,"value":"v"}}}""" ]

// ---------------------------------------------------------------------------
// Phase 722 — the HOLDERS vocabulary: every place a presence-keyed union can stand that `layout`
// does not reach, and unions of more than two cases. `layout` holds its two unions on a KIND, and
// each has two cases. The F* target also emits a model and a proof script for the forms below, and
// each is in the certification set so that a prover has checked what the generator emits:
//   * a plain (not inline) RECORD holding one (`R2.k`), beside an inline record;
//   * a TAGGED CASE holding one (`T.Two.k3`), beside a record-payload case;
//   * a TYPE-PARAMETERISED union holding one (`Box<T>.Full.pick`), instantiated at a string;
//   * a union of THREE cases one of which synthesises another's key and a member (`K`), with a
//     member name two cases share;
//   * a union of FOUR cases with one key (`p`) that TWO cases synthesise, at different values (`K3`).
// The names are terse on purpose: they are the generated lemma names' stems, and the proof scripts
// were measured over exactly these.
// ---------------------------------------------------------------------------

/// The holders vocabulary.
let holders: Idl =
    let keyed (name: string) (rules: (string * string * (string * IdlValue) list) list) (cases: IdlUnionCase list) =
        union
            name
            (UnionDiscrimination.ByPresence(rules |> List.map (fun (c, k, s) -> { Case = c; Key = k; Synthesised = s })))
            cases

    let full =
        base'
            [ kind
                  "G"
                  [ field "r" (TRecord "R1") Required
                    field "u" (TUnion("K", [])) Required
                    field "o" (TUnion("K3", [])) Optional ]
              kind "H" [ field "t" (TUnion("T", [])) Required; field "rec" (TRecord "R2") Optional ]
              kind "I" [ field "b" (TUnion("Box", [ TStr ])) Required ] ]
            [ keyed
                  "K"
                  [ ("A", "ka", [])
                    ("B", "kb", [])
                    ("C", "kc", [ ("kb", VStr "seven"); ("x", VStr "sx") ]) ]
                  [ case "A" [ field "shared" TStr Optional; field "ka" TStr Required ]
                    case
                        "B"
                        [ field "x" TStr Required
                          field "kb" TStr Required
                          field "shared" TStr Optional ]
                    case "C" [ field "kc" TBool Required ] ]
              keyed
                  "K3"
                  [ ("P", "p", [])
                    ("Q", "q", [ ("p", VStr "1") ])
                    ("R", "r", [ ("p", VStr "2") ])
                    ("S", "sk", []) ]
                  [ case "P" [ field "p" TStr Required ]
                    case "Q" [ field "q" TStr Required ]
                    case "R" [ field "r" TStr Required ]
                    case "S" [ field "sk" TStr Required ] ]
              union
                  "T"
                  UnionDiscrimination.Tagged
                  [ case "One" [ field "payload" (TRecord "R1") Required ]
                    case "Two" [ field "n" TStr Required; field "k3" (TUnion("K3", [])) Optional ] ]
              { union
                    "Box"
                    UnionDiscrimination.Tagged
                    [ case "Full" [ field "item" (TVar "T") Required; field "pick" (TUnion("K3", [])) Optional ]
                      case "Empty" [] ] with
                  Params = [ "T" ] } ]
            [ record
                  "R1"
                  true
                  [ field "zi" TStr (OmitDefault(VStr "five"))
                    field "s" TStr Optional
                    field "ab" TBool Required ]
              record
                  "R2"
                  false
                  [ field "inner" (TRecord "R1") Required
                    field "k" (TUnion("K", [])) Required
                    field "tail" TStr Optional ] ]

    // `base'` adds the layout's feed and table, which this vocabulary does not use.
    { full with
        Unions = full.Unions |> List.filter (fun u -> u.Name <> "Feed")
        Records = full.Records |> List.filter (fun r -> r.Name <> "StaticTable") }

/// Documents the holders vocabulary REFUSES, one per reading rule the layout's two-case unions do
/// not reach: two unrelated keys of the three-case union; the four-case union's `p` beside a key
/// that does not synthesise it; the two cases that both synthesise `p`, present together; a
/// synthesising case whose `p` is the OTHER synthesising case's value; and a required union held
/// by a plain record with no key present.
let holdersRefused: string list =
    let g (members: (string * string) list) : string =
        members
        |> List.map (fun (k, v) -> sprintf ",\"%s\":\"%s\"" k v)
        |> String.concat ""
        |> sprintf "{\"id\":\"g\",\"kind\":{\"$type\":\"G\",\"ab\":true%s}}"

    [ g [ "ka", "a"; "kb", "b"; "x", "x" ]
      g [ "ka", "a"; "p", "1"; "sk", "s" ]
      g [ "ka", "a"; "p", "1"; "q", "q"; "r", "r" ]
      g [ "ka", "a"; "p", "2"; "q", "q" ]
      """{"id":"h","kind":{"$type":"H","rec":{"ab":true},"t":{"$type":"Two","n":"n"}}}""" ]

/// The module name the committed generated file declares.
[<Literal>]
let moduleName = "Fuaran.Core.Tests.LayoutGenerated"

/// The committed generated file, repository-relative.
[<Literal>]
let generatedFile = "tests/Fuaran.Core.Tests/LayoutGenerated.fs"

/// The module text the generator emits for `layout`, with the collecting decoders requested.
let generate () : Result<string, CodegenError> =
    Gen.fsharpModuleDerived
        GenSupport.Empty
        [ Derivation.SpecDecoders ]
        moduleName
        layout
        (layout.Kinds |> List.map _.Tag)

// ---------------------------------------------------------------------------
// Phase 722 (DECISIONS.md D285) — the derived members over an inline declaration, on both hosts.
//
// `derived` is `layout` and one more kind, `Outline`, whose two unions hold themselves, because a
// fold is derived for a self-recursive union and `layout` declares none: `Chain` is presence-keyed
// and holds itself through an ordinary record (`LinkBox`, on whose object the nested union's
// members sit), and `Tree` is tagged and holds itself through a record payload (`BranchPayload`).
// `GridBox` is an ordinary record holding the grid's three inline declarations, so each of them
// is reached from a declaration a codec-only module can name as a root.
// `LayoutDerivedGenerated.fs` is the F# emitted for it with every slot enumerator, fold and
// projection of `derivations` requested, committed and held to the generator;
// `IdlLayoutTypeScriptTests` runs it beside the TypeScript module the same requests emit.
// ---------------------------------------------------------------------------

/// `layout` with the `Outline` kind and its two self-recursive unions, and a declared default for
/// each member of `Grid` a caller must otherwise pass: the two inline ones among them (`paging`, an
/// inline record, and `rows`, a required presence-keyed union whose case synthesises a member), so
/// the default record of a holder is emitted and held to the F# host's.
let derived: Idl =
    { layout with
        Defaults =
            [ { Kind = "Grid"
                Field = "columns"
                Value = VList [] }
              { Kind = "Grid"
                Field = "paging"
                Value = VRecord [ ("pageSize", VInt 10) ] }
              { Kind = "Grid"
                Field = "rows"
                Value = VUnion("StaticRows", [ ("staticRows", VRecord [ ("headers", VList []); ("rows", VList []) ]) ]) } ]
        Kinds =
            layout.Kinds
            @ [ kind
                    "Outline"
                    [ field "chain" (TUnion("Chain", [])) Required
                      field "tree" (TUnion("Tree", [])) Required
                      field "box" (TRecord "GridBox") Optional ] ]
        Unions =
            layout.Unions
            @ [ union
                    "Chain"
                    (UnionDiscrimination.ByPresence
                        [ { Case = "Link"
                            Key = "link"
                            Synthesised = [] }
                          { Case = "Stop"
                            Key = "stop"
                            Synthesised = [] } ])
                    [ case "Link" [ field "link" (TRecord "LinkBox") Required ]
                      case "Stop" [ field "stop" TStr Required ] ]
                union
                    "Tree"
                    UnionDiscrimination.Tagged
                    [ case "Leaf" [ field "n" TInt Required ]
                      case "Branch" [ field "payload" (TRecord "BranchPayload") Required ] ] ]
        Records =
            layout.Records
            @ [ record "LinkBox" false [ field "label" TStr Required; field "next" (TUnion("Chain", [])) Optional ]
                record
                    "BranchPayload"
                    true
                    [ field "kids" (TList(TUnion("Tree", []))) Required
                      field "note" TStr Optional ]
                record
                    "GridBox"
                    false
                    [ field "paging" (TRecord "Paging") Required
                      field "identity" (TUnion("RowIdentity", [])) Optional
                      field "rows" (TUnion("GridRows", [])) Required
                      field "title" TStr Optional ] ] }

/// Every slot enumerator, fold, projection and default record the two hosts are held to each other
/// over.
let derivations: Derivation list =
    [ Derivation.SlotsOf "Paging"
      Derivation.SlotsOf "RowIdentity"
      Derivation.SlotsOf "GridRows"
      Derivation.SlotsOf "Field"
      Derivation.SlotsOf "Chain"
      Derivation.SlotsOf "Tree"
      Derivation.Fold "Chain"
      Derivation.Fold "Tree"
      Derivation.Projections("RowIdentity", [ "rowKey"; "rowKeyField" ])
      Derivation.Projections("GridRows", [ "source"; "staticRows" ])
      Derivation.Projections("Field", [ "payload"; "on" ])
      Derivation.Projections("Chain", [ "link"; "stop" ])
      Derivation.Projections("Tree", [ "payload"; "n" ])
      Derivation.DefaultRecords ]

/// Documents of the `Outline` kind, each in canonical (sorted) form: a chain of two links and a
/// tree three deep; neither union nested; a link whose box holds no further case.
let derivedCorpus: string list =
    [ """{"id":"o1","kind":{"$type":"Outline","link":{"label":"a","link":{"label":"b","stop":"z"}},"tree":{"$type":"Branch","kids":[{"$type":"Leaf","n":1},{"$type":"Branch","kids":[{"$type":"Leaf","n":2}],"note":"inner"},{"$type":"Leaf","n":3}]}}}"""
      """{"id":"o2","kind":{"$type":"Outline","stop":"now","tree":{"$type":"Leaf","n":7}}}"""
      """{"id":"o3","kind":{"$type":"Outline","link":{"label":"open"},"tree":{"$type":"Branch","kids":[]}}}""" ]

/// The module name the committed derived file declares.
[<Literal>]
let derivedModuleName = "Fuaran.Core.Tests.LayoutDerivedGenerated"

/// The committed derived file, repository-relative.
[<Literal>]
let derivedGeneratedFile = "tests/Fuaran.Core.Tests/LayoutDerivedGenerated.fs"

/// The module text the generator emits for `derived`, with `derivations` requested.
let generateDerived () : Result<string, CodegenError> =
    Gen.fsharpModuleDerived GenSupport.Empty derivations derivedModuleName derived (derived.Kinds |> List.map _.Tag)

// ---------------------------------------------------------------------------
// Phase 722 (DECISIONS.md D285) — the CODEC-ONLY module over inline declarations. Its roots are the
// declarations of `derived` that HOLD one (an inline declaration has no JSON value of its own, so
// it is no root), and every reached declaration's host type is the one `LayoutDerivedGenerated`
// declares. `LayoutCodecGenerated.fs` is the module emitted, committed and held to the generator.
// ---------------------------------------------------------------------------

/// The codec-only request: the holders as roots, each declaration at its generated host type.
let codecRequest: CodecOnly =
    { Roots = [ "GridBox"; "LinkBox"; "Tree"; "Field" ]
      HostTypes =
        (derived.Unions |> List.map _.Name) @ (derived.Records |> List.map _.Name)
        |> List.map (fun n -> n, derivedModuleName + "." + n)
        |> Map.ofList }

/// The module name the committed codec-only file declares.
[<Literal>]
let codecModuleName = "Fuaran.Core.Tests.LayoutCodecGenerated"

/// The committed codec-only file, repository-relative.
[<Literal>]
let codecGeneratedFile = "tests/Fuaran.Core.Tests/LayoutCodecGenerated.fs"

/// The codec-only module text the generator emits for `codecRequest`.
let generateCodec () : Result<string, CodegenError> =
    Gen.fsharpCodecModule GenSupport.Empty codecRequest codecModuleName derived
