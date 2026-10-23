module Fuaran.Core.Tests.SecondDomainIdl

// The SECOND-DOMAIN certification vocabulary (Phases 108/109): a document vocabulary with a corpus written
// outside this repository. Declared here, in the fixtures library, because the tool generates from it
// (`render generated`, `render fstar`); `SecondDomainSpike` is its family.

open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// The declared slice
// ---------------------------------------------------------------------------

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Aliases = []
      Keyed = false
      Presence = None
      Annotations = Annotations.Empty }

let private req name t = f name t Required
let private opt name t = f name t Optional

/// The inline-run union: a recursive, tag-discriminated value union carried
/// inside run-bearing leaves rather than as tree nodes with identity.
let private runType = TUnion("Run", [])
let private runList = TList runType

let private kind tag category fields : IdlKind =
    { Tag = tag
      Category = category
      Annotations = Annotations.Empty
      Fields = fields }

/// A slice of a second domain's node vocabulary — eleven kinds chosen to span
/// the structural variety rather than to be complete: a root carrying an
/// optional string and two closed sets, containers over child nodes, a
/// run-bearing leaf, a boolean-bearing row, an optional list, and a
/// string-bearing leaf.
let docIdl: Idl =
    { Kinds =
        [ kind
              "Document"
              "structure"
              [ opt "title" TStr
                req "locale" (TEnum "Locale")
                req "numbering" (TEnum "Numbering")
                req "children" (TList TNode) ]
          kind
              "Section"
              "structure"
              [ req "heading" runList
                req "depth" (TEnum "HeadingDepth")
                req "children" (TList TNode) ]
          kind "Paragraph" "leaf" [ req "runs" runList ]
          kind "ListBlock" "structure" [ req "style" (TEnum "ListStyle"); req "children" (TList TNode) ]
          kind "ListItem" "structure" [ req "children" (TList TNode) ]
          kind "Table" "structure" [ opt "caption" runList; req "children" (TList TNode) ]
          kind "Row" "structure" [ req "isHeader" TBool; req "children" (TList TNode) ]
          kind "Cell" "leaf" [ req "runs" runList ]
          kind "Figure" "structure" [ req "source" TStr; req "children" (TList TNode) ]
          kind "Caption" "leaf" [ req "runs" runList ]
          kind "Footnote" "structure" [ req "children" (TList TNode) ] ]
      Unions =
        [ { TagRule = Fuaran.Core.Idl.UnionTagRule.CaseName
            Name = "Run"
            Params = []
            Cases =
              [ { Hosted = None
                  Tag = "Text"
                  Fields = [ req "value" TStr ]
                  Annotations = Annotations.Empty }
                { Hosted = None
                  Tag = "Emphasis"
                  Fields = [ req "runs" runList ]
                  Annotations = Annotations.Empty }
                { Hosted = None
                  Tag = "Strong"
                  Fields = [ req "runs" runList ]
                  Annotations = Annotations.Empty }
                { Hosted = None
                  Tag = "InlineRef"
                  Fields = [ req "target" TStr ]
                  Annotations = Annotations.Empty }
                { Hosted = None
                  Tag = "InlineVariable"
                  Fields = [ req "field" TStr ]
                  Annotations = Annotations.Empty }
                { Hosted = None
                  Tag = "Link"
                  Fields = [ req "text" TStr; req "url" TStr ]
                  Annotations = Annotations.Empty }
                { Hosted = None
                  Tag = "Code"
                  Fields = [ req "value" TStr ]
                  Annotations = Annotations.Empty } ]
            Annotations = Annotations.Empty
            Discrimination = UnionDiscrimination.Tagged } ]
      Enums =
        [ Declare.enumOf "Locale" [ "EnGB"; "EnUS" ]
          Declare.enumOf "Numbering" [ "NoNumbering"; "DecimalNumbering"; "LegalNumbering" ]
          Declare.enumOf "HeadingDepth" [ "H1"; "H2"; "H3"; "H4"; "H5"; "H6" ]
          Declare.enumOf "ListStyle" [ "Bulleted"; "Numbered"; "Lettered"; "Roman" ] ]
      Records = []
      Defaults = []
      // The second vocabulary's node carries nothing beside its identity and its
      // kind — an empty envelope, which is the `Idl` default.
      NodeFields = []
      Ops = []
      // Findings (1)/(2)/(3), closed: the wire shape is DECLARED (Phases
      // 108/109/111) — a bare-string `kind` discriminator, the flat node
      // envelope, and declaration-order canonical keys.
      Wire =
        { Discriminator = "kind"
          NodeEnvelope = NodeEnvelopeShape.FlatKind
          KeyOrder = KeyOrder.Declared }
      Harden = HardenPolicy.Undeclared }
