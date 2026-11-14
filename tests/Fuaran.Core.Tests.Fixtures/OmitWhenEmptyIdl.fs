namespace Fuaran.Core.Tests

// ============================================================================
//  Phase 660 (DECISIONS.md D255) — the fixture vocabulary for an optional list
//  declared omit-when-empty (`PresenceCondition.WhenNonEmpty`). It is shaped like
//  the consumer that asked for it: a generic `Binding<'T>` whose `Query`,
//  `Transform` and `Expr` arms each carry an optional `dependsOn` or `params`
//  list, held by a kind beside an optional list of its own. `plain` is the same
//  vocabulary declaring nothing, so the two generate the same F# TYPES and differ
//  only in what an empty list writes.
// ============================================================================

module OmitWhenEmptyIdl =

    open Fuaran.Core.Idl

    let private field (name: string) (t: IdlType) (opt: Optionality) : IdlField =
        { Name = name
          Type = t
          Opt = opt
          Aliases = []
          Keyed = false
          Presence = None
          Annotations = Annotations.Empty }

    /// An optional list declared omit-when-empty.
    let private nonEmpty (name: string) (item: IdlType) : IdlField =
        { field name (TList item) Optional with
            Presence = Some WhenNonEmpty }

    let private case (tag: string) (fields: IdlField list) : IdlUnionCase =
        { Tag = tag
          Fields = fields
          Annotations = Annotations.Empty
          Hosted = None }

    let private param = TRecord "TransformParam"

    /// The declaring vocabulary: `Binding.Query.dependsOn`, `Binding.Transform.params`,
    /// `Binding.Expr.params` and `Panel.tags` are each an optional list declared non-empty.
    let idl: Idl =
        { Kinds =
            [ { Tag = "Panel"
                Category = "Display"
                Fields =
                  [ field "title" TStr Required
                    field "binding" (TUnion("Binding", [ TStr ])) Required
                    nonEmpty "tags" TStr ]
                Annotations = Annotations.Empty } ]
          Unions =
            [ { Name = "Binding"
                Params = [ "T" ]
                Cases =
                  [ case "Static" [ field "value" (TVar "T") Optional ]
                    case "Query" [ field "name" TStr Required; nonEmpty "dependsOn" TStr ]
                    case "Transform" [ field "source" TStr Required; nonEmpty "params" param ]
                    case "Expr" [ field "expr" TStr Required; nonEmpty "params" param ] ]
                Annotations = Annotations.Empty
                TagRule = UnionTagRule.CaseName
                Discrimination = UnionDiscrimination.Tagged } ]
          Enums = []
          Records =
            [ { Name = "TransformParam"
                Fields = [ field "name" TStr Required; field "value" TStr Required ]
                Annotations = Annotations.Empty
                Inline = false } ]
          Defaults = []
          NodeFields = []
          Ops = []
          Wire = WireShape.Default
          Harden = HardenPolicy.Undeclared }

    /// The same vocabulary declaring nothing: every member an ordinary `Optional` list.
    let plain: Idl =
        let strip (fs: IdlField list) =
            fs |> List.map (fun f -> { f with Presence = None })

        { idl with
            Kinds = idl.Kinds |> List.map (fun k -> { k with Fields = strip k.Fields })
            Unions =
                idl.Unions
                |> List.map (fun u ->
                    { u with
                        Cases = u.Cases |> List.map (fun c -> { c with Fields = strip c.Fields }) }) }

    /// The kinds the tests generate.
    let kinds = [ "Panel" ]
