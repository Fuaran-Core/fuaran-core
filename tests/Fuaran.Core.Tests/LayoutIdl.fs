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
      Harden = HardenPolicy.Undeclared }

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
