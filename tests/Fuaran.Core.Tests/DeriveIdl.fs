module Fuaran.Core.Tests.DeriveIdl

open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 374 — the derivations vocabulary. A small WORK-PLAN domain (sections, choices, tasks,
// notes) that is deliberately not a user-interface vocabulary, so every structural derivation
// the generator emits is shown generated, compiled and law-checked for a domain other than the
// one whose hand-written copies motivated it.
//
// It carries one instance of every construct a derivation is stated over:
//   - structural children: `Section.children` (a node list), `Choice.otherwise` (a node);
//   - keyed positions: `Choice.branches` (a CASE TABLE — a list of records each holding a node),
//     `Task.hint` (an optional node), `Task.extras` (a map of nodes), and the envelope's optional
//     `annotation` node;
//   - a self-recursive union `Rule`, recursing through a list, an option, a map, a direct field
//     and a record (`Guard`);
//   - a union generic in the message parameter, `Trigger<'Msg>`, through two covariant handler
//     signatures, which makes `TaskSpec`, `NodeKind` and `Node` generic in it;
//   - a field carried by SOME cases (`Trigger.owner`) and one carried by EVERY case
//     (`Measure.amount`);
//   - a fully defaulted kind (`Note`) and record (`Owner`), and kinds that are not.
//
// `DeriveGenerated.fs` is the F# emitted from it with every derivation requested, committed and
// checked for drift by `IdlDeriveTests`.
// ---------------------------------------------------------------------------

let private field name ty opt =
    { Name = name
      Type = ty
      Opt = opt
      Annotations = Annotations.Empty }

let private req name ty = field name ty Required
let private opt name ty = field name ty Optional

let private case tag fields =
    { Tag = tag
      Fields = fields
      Annotations = Annotations.Empty }

let private kind tag category fields =
    { Tag = tag
      Category = category
      Fields = fields
      Annotations = Annotations.Empty }

let private handler (arg: string) =
    TFn
        { FSharp = arg + " -> 'Msg"
          TypeScript = "(x: " + arg + ") => Msg"
          Placeholder = "(fun (_: " + arg + ") -> box \"<closure>\")" }

let private rule = TUnion("Rule", [])

/// The derivations vocabulary.
let deriveIdl: Idl =
    { Enums = [ Declare.enumOf "Priority" [ "Low"; "Normal"; "High" ] ]
      Unions =
        [ { Name = "Rule"
            Params = []
            Cases =
              [ case "Always" []
                case "Equals" [ req "key" TStr; req "value" TStr ]
                case "AllOf" [ req "rules" (TList rule) ]
                case "Not" [ req "rule" rule ]
                case "Maybe" [ opt "inner" rule ]
                case "Guarded" [ req "guard" (TRecord "Guard") ]
                case "Named" [ req "rules" (TMap rule) ] ] }
          { Name = "Trigger"
            Params = []
            Cases =
              [ case "Timer" [ req "owner" (TRecord "Owner"); req "every" TInt; req "fire" (handler "int") ]
                case
                    "Signal"
                    [ req "owner" (TRecord "Owner")
                      req "name" TStr
                      req "fire" (handler "string") ]
                case "Manual" [ req "label" TStr ] ] }
          { Name = "Measure"
            Params = []
            Cases = [ case "Hours" [ req "amount" TFloat ]; case "Days" [ req "amount" TFloat ] ] } ]
      Records =
        [ { Name = "Owner"
            Fields =
              [ opt "name" TStr
                field "priority" (TEnum "Priority") (OmitDefault(VEnum "Normal")) ] }
          { Name = "Branch"
            Fields = [ req "label" TStr; req "body" TNode ] }
          { Name = "Guard"
            Fields = [ req "rule" rule; opt "note" TStr ] } ]
      Kinds =
        [ kind
              "Section"
              "structure"
              [ req "title" TStr
                req "children" (TList TNode)
                opt "owner" (TRecord "Owner")
                opt "estimate" (TUnion("Measure", [])) ]
          kind
              "Choice"
              "structure"
              [ req "branches" (TList(TRecord "Branch"))
                req "otherwise" TNode
                req "rule" rule ]
          kind
              "Task"
              "work"
              [ req "name" TStr
                req "due" rule
                req "trigger" (TUnion("Trigger", []))
                opt "hint" TNode
                req "extras" (TMap TNode)
                req "owner" (TRecord "Owner") ]
          kind
              "Note"
              "work"
              [ req "text" TStr
                field "priority" (TEnum "Priority") (OmitDefault(VEnum "Normal")) ] ]
      Defaults =
        [ { Kind = "Note"
            Field = "text"
            Value = VStr "" } ]
      NodeFields = [ opt "annotation" TNode ]
      Ops = [ kind "Move" "op" [ req "target" TStr; req "index" TInt ] ]
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared }

/// The module name the committed generated file declares.
[<Literal>]
let moduleName = "Fuaran.Core.Tests.DeriveGenerated"

/// The committed generated file, repository-relative.
[<Literal>]
let generatedFile = "tests/Fuaran.Core.Tests/DeriveGenerated.fs"

/// Every derivation, each stated over this vocabulary.
let derivations: Gen.Derivation list =
    [ Gen.Derivation.StructuralAccess
      Gen.Derivation.KeyedPositions
      Gen.Derivation.SlotsOf "Rule"
      Gen.Derivation.SlotsOf "Owner"
      Gen.Derivation.MapMsg
      Gen.Derivation.Fold "Rule"
      Gen.Derivation.Projections("Trigger", [ "owner" ])
      Gen.Derivation.Projections("Measure", [ "amount" ])
      Gen.Derivation.DefaultRecords
      Gen.Derivation.VocabularyConstants ]

/// The module text the generator emits for this vocabulary with every derivation requested.
let generate () : Result<string, CodegenError> =
    Gen.fsharpModuleDerived Gen.GenSupport.Empty derivations moduleName deriveIdl (deriveIdl.Kinds |> List.map _.Tag)
