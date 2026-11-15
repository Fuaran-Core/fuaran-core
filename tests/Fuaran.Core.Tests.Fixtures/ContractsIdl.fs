module Fuaran.Core.Tests.ContractsIdl

// The contract fixture (Phase 644, `docs/idl-contracts.md`): a small vocabulary that declares data and
// two contracts over it, used by the contract tests and to emit the `conformance/idl/contracts.json`
// vectors. Its names are a FIXTURE's: Core ships no such contract, and names no role. The authority
// values (`reader`, `writer`, `anyone`) are what a host might define; the IDL carries them and reads none.

open Fuaran.Core
open Fuaran.Core.Idl

let field (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Aliases = []
      Keyed = false
      Presence = None
      Annotations = Annotations.Empty }

let arg (name: string) (t: IdlType) : IdlArgument =
    { Name = name
      Type = t
      Opt = Required
      Constraint = None
      Doc = None }

/// A method with every member at its default, reading its host deterministically, open to `authority`.
let method (id: string) (args: IdlArgument list) (result: IdlType option) (authority: string list) : IdlMethod =
    { Id = id
      Arguments = args
      Result = result
      Shape = MethodShape.Call
      Effect =
        { Host = ReadsHost
          Determinism = Effect.deterministic }
      Destination = false
      ReadPolicy = MethodReadPolicy.Unstated
      Idempotent = false
      Authority = authority
      Record = { Arguments = false; Result = false }
      AuditKind = None
      RateLimit = None
      Serves = [ 1 ]
      Doc = None }

let private writing: EffectClass =
    { Host = WritesHost
      Determinism = Effect.clock }

/// Every type an argument may have, by the name the probe gives it: the scalars the spaces state, the
/// string-borne scalars, and every structured type.
let everyType: (string * IdlType) list =
    [ "text", TStr
      "count", TInt
      "ratio", TFloat
      "flag", TBool
      "status", TEnum "Status"
      "big", TInt64
      "amount", TDecimal
      "blob", TBytes
      "at", TTimeOfDay
      "wait", TDuration
      "paging", TRecord "Paging"
      "filter", TUnion("Filter", [])
      "items", TList TInt
      "byName", TMap TStr
      "pair", TTuple [ TStr; TFloat ]
      "byDay", TKeyedMap(TInt, TStr)
      "tags", TSet TStr
      "anything", TJson
      "tree", TNode ]

/// One value of each type in `everyType`, by the same name.
let everyValue: (string * IdlValue) list =
    [ "text", VStr "hello"
      "count", VInt 7
      "ratio", VFloat 2.5
      "flag", VBool true
      "status", VEnum "Open"
      "big", VStr "9007199254740993"
      "amount", VStr "12.25"
      "blob", VStr "AQID"
      "at", VStr "09:30:00"
      "wait", VStr "90s"
      "paging", VRecord [ "pageSize", VInt 10; "pageStateKey", VStr "k" ]
      "filter", VUnion("ByStatus", [ "status", VEnum "Closed" ])
      "items", VList [ VInt 1; VInt 2; VInt 3 ]
      "byName", VMap [ "a", VStr "x"; "b", VStr "y" ]
      "pair", VList [ VStr "x"; VFloat 2.5 ]
      "byDay", VList [ VList [ VInt 1; VStr "a" ]; VList [ VInt 2; VStr "b" ] ]
      "tags", VList [ VStr "a"; VStr "b" ]
      "anything", VJson(JObj [ "z", JInt 1; "a", JArr [ JBool true ] ])
      "tree", VNode("n1", "Leaf", [ "label", VStr "x" ]) ]

let orders: IdlContract =
    { Name = "orders"
      Doc = Some "Read and place orders."
      Methods =
        [ { method "get" [ arg "id" TStr ] (Some(TRecord "Order")) [ "reader" ] with
              ReadPolicy = MethodReadPolicy.Cached 30
              Idempotent = true
              Doc = Some "One order by its id." }
          { method
                "list"
                [ { arg "paging" (TRecord "Paging") with
                      Doc = Some "Which page." }
                  { arg "limit" TInt with
                      Opt = OmitDefault(VInt 20)
                      Constraint = Some(ArgumentConstraint.IntBetween(1, 100)) }
                  { arg "status" (TEnum "Status") with
                      Opt = Optional } ]
                (Some(TList(TRecord "Order")))
                [ "reader" ] with
              Record = { Arguments = true; Result = false } }
          { method
                "place"
                [ arg "order" (TRecord "Order")
                  { arg "note" TStr with
                      Opt = Optional
                      Constraint = Some(ArgumentConstraint.LengthBetween(0, 200)) } ]
                (Some TStr)
                [ "reader"; "writer" ] with
              Effect = writing
              Destination = true
              ReadPolicy = MethodReadPolicy.Invalidates [ "list"; "get" ]
              AuditKind = Some "order.placed"
              RateLimit = Some { Calls = 10; Seconds = 60 }
              Serves = [ 1; 2 ] }
          { method "cancel" [ arg "id" TStr ] None [ "writer" ] with
              Effect = writing
              Idempotent = true
              Serves = [ 2 ] }
          { method "watch" [ arg "filter" (TUnion("Filter", [])) ] (Some(TRecord "Order")) [ "reader" ] with
              Shape = MethodShape.Stream
              Effect =
                  { Host = ReadsHost
                    Determinism = Effect.network } }
          { method "export" [] (Some TBytes) [ "reader" ] with
              Shape = MethodShape.Job } ] }

/// A second contract: one method taking a value of EVERY type, and one open to every caller.
let probe: IdlContract =
    { Name = "probe"
      Doc = None
      Methods =
        [ method "every" (everyType |> List.map (fun (name, t) -> arg name t)) (Some TJson) [ "anyone" ]
          { method "ping" [] None [ "anyone" ] with
              Effect = Effect.pureDeterministic } ] }

/// The vocabulary: the data the contracts name, a kind `Probe` holding one member of every type (what
/// the membership law encodes its documents through), and the two contracts.
let idl: Idl =
    { Kinds =
        [ { Tag = "Leaf"
            Category = "probe"
            Annotations = Annotations.Empty
            Fields = [ field "label" TStr Required ] }
          { Tag = "Probe"
            Category = "probe"
            Annotations = Annotations.Empty
            Fields = everyType |> List.map (fun (name, t) -> field name t Required) } ]
      Unions =
        [ { TagRule = UnionTagRule.CaseName
            Name = "Filter"
            Params = []
            Cases =
              [ { Hosted = None
                  Tag = "ByStatus"
                  Fields = [ field "status" (TEnum "Status") Required ]
                  Annotations = Annotations.Empty }
                { Hosted = None
                  Tag = "All"
                  Fields = []
                  Annotations = Annotations.Empty } ]
            Annotations = Annotations.Empty
            Discrimination = UnionDiscrimination.Tagged } ]
      Enums = [ Declare.enumOf "Status" [ "Open"; "Closed" ] ]
      Records =
        [ { Name = "Order"
            Fields = [ field "id" TStr Required; field "total" TDecimal Required ]
            Annotations = Annotations.Empty
            Inline = false }
          { Name = "Paging"
            Fields = [ field "pageSize" TInt Required; field "pageStateKey" TStr Required ]
            Annotations = Annotations.Empty
            Inline = false } ]
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      Harden = HardenPolicy.Undeclared
      Contracts = [ orders; probe ] }

/// The vocabulary with its contracts replaced by `contracts`.
let withContracts (contracts: IdlContract list) : Idl = { idl with Contracts = contracts }

/// The vocabulary holding `orders` alone with one method changed, by id.
let withMethod (id: string) (change: IdlMethod -> IdlMethod) : Idl =
    withContracts
        [ { orders with
              Methods = orders.Methods |> List.map (fun m -> if m.Id = id then change m else m) } ]
