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

let private get (change: IdlMethod -> IdlMethod) = withMethod "get" change

let private firstArg (change: IdlArgument -> IdlArgument) (m: IdlMethod) =
    { m with
        Arguments = change (List.head m.Arguments) :: List.tail m.Arguments }

/// The refusal cases (`docs/idl-contracts.md`, "The refusals"): what is wrong, the refusal's name, and a
/// vocabulary that earns exactly that refusal. The suite holds one case to every named refusal, and the
/// vectors in `conformance/idl/contracts.json` carry each one the artifact can spell.
let refusalCases: (string * string * Idl) list =
    [ "a method that declares no authority", "noAuthority", get (fun m -> { m with Authority = [] })
      "an empty authority value", "malformedAuthority", get (fun m -> { m with Authority = [ "" ] })
      "an authority value listed twice",
      "malformedAuthority",
      get (fun m ->
          { m with
              Authority = [ "reader"; "reader" ] })
      "a duplicate method id",
      "duplicateMethod",
      withContracts
          [ { orders with
                Methods = orders.Methods @ [ method "get" [] None [ "reader" ] ] } ]
      "a duplicate contract", "duplicateContract", withContracts [ probe; probe ]
      "a contract name that is no identifier",
      "contractNameNotIdentifier",
      withContracts [ { probe with Name = "pro.be" } ]
      "a method id that is no identifier",
      "methodIdNotIdentifier",
      withMethod "export" (fun m -> { m with Id = "ex port" })
      "a method that serves no version", "servesNoVersion", get (fun m -> { m with Serves = [] })
      "a version that is not positive", "malformedVersion", get (fun m -> { m with Serves = [ 0 ] })
      "a stream with a read policy",
      "streamWithReadPolicy",
      withMethod "watch" (fun m ->
          { m with
              ReadPolicy = MethodReadPolicy.Cached 5 })
      "a stream with no result", "streamWithoutResult", withMethod "watch" (fun m -> { m with Result = None })
      "a write with a cache lifetime",
      "writeWithCacheLifetime",
      withMethod "cancel" (fun m ->
          { m with
              ReadPolicy = MethodReadPolicy.Cached 5 })
      "a cache lifetime that is not positive",
      "cacheLifetimeNotPositive",
      get (fun m ->
          { m with
              ReadPolicy = MethodReadPolicy.Cached 0 })
      "an invalidation naming nothing",
      "malformedInvalidation",
      withMethod "place" (fun m ->
          { m with
              ReadPolicy = MethodReadPolicy.Invalidates [] })
      "an invalidation naming an unknown method",
      "invalidatesUnknownMethod",
      withMethod "place" (fun m ->
          { m with
              ReadPolicy = MethodReadPolicy.Invalidates [ "nope" ] })
      "a rate limit that is not positive",
      "rateLimitNotPositive",
      get (fun m ->
          { m with
              RateLimit = Some { Calls = 0; Seconds = 60 } })
      "an empty audit kind", "malformedAuditKind", get (fun m -> { m with AuditKind = Some "" })
      "an ill-formed description",
      "illFormedDescription",
      get (fun m ->
          { m with
              Doc = Some(string (char 0xD800)) })
      "an argument name that is no identifier",
      "argumentNameNotIdentifier",
      get (firstArg (fun a -> { a with Name = "the id" }))
      "a duplicate argument",
      "duplicateArgument",
      get (fun m ->
          { m with
              Arguments = m.Arguments @ m.Arguments })
      "a host-only argument", "hostOnlyArgument", get (firstArg (fun a -> { a with Opt = HostOnly }))
      "an argument of a type with no wire form",
      "typeWithoutWireForm",
      get (firstArg (fun a -> { a with Type = TList TOpaque }))
      "a result of a type with no wire form", "typeWithoutWireForm", get (fun m -> { m with Result = Some TClosure })
      "an argument of an enumeration with no case",
      "emptyEnumeration",
      { get (firstArg (fun a -> { a with Type = TEnum "Never" })) with
          Enums = idl.Enums @ [ Declare.enumOf "Never" [] ] }
      "a constraint that does not fit its type",
      "constraintMismatch",
      get (
          firstArg (fun a ->
              { a with
                  Constraint = Some(ArgumentConstraint.IntBetween(0, 9)) })
      )
      "a constraint that admits no value",
      "emptyConstraint",
      get (
          firstArg (fun a ->
              { a with
                  Constraint = Some(ArgumentConstraint.LengthBetween(5, 1)) })
      )
      "a default that is not of the argument's type",
      "defaultOutOfType",
      get (firstArg (fun a -> { a with Opt = OmitDefault(VInt 3) }))
      "a default outside the argument's constraint",
      "defaultOutOfType",
      withMethod "list" (fun m ->
          { m with
              Arguments =
                  m.Arguments
                  |> List.map (fun a ->
                      if a.Name = "limit" then
                          { a with Opt = OmitDefault(VInt 500) }
                      else
                          a) }) ]

/// `orders` with one argument of one method changed, by name.
let private withArgument (methodId: string) (name: string) (change: IdlArgument -> IdlArgument) : Idl =
    withMethod methodId (fun m ->
        { m with
            Arguments = m.Arguments |> List.map (fun a -> if a.Name = name then change a else a) })

let private ordersOnly: Idl = withContracts [ orders ]

/// The differ's cases (DECISIONS.md D260.16 and D260.18): a name, the revision before, the revision
/// after, and the class the pair is. Every revision is a valid vocabulary. The vectors in
/// `conformance/idl/contracts.json` are these pairs with the classifier's own answer, and the suite
/// holds each answer to the class written here.
let diffCases: (string * Idl * Idl * string) list =
    let withoutExport =
        withContracts
            [ { orders with
                  Methods = orders.Methods |> List.filter (fun m -> m.Id <> "export") } ]

    let extra (opt: Optionality) =
        withMethod "get" (fun m ->
            { m with
                Arguments = m.Arguments @ [ { arg "verbose" TBool with Opt = opt } ] })

    let limit (lo: int) (hi: int) =
        withArgument "list" "limit" (fun a ->
            { a with
                Constraint = Some(ArgumentConstraint.IntBetween(lo, hi)) })

    let rate (calls: int) (seconds: int) =
        withMethod "place" (fun m ->
            { m with
                RateLimit = Some { Calls = calls; Seconds = seconds } })

    [ "unchanged", ordersOnly, ordersOnly, "unchanged"
      "contract-added", ordersOnly, idl, "additive"
      "contract-removed", idl, ordersOnly, "breaking"
      "method-added", withoutExport, ordersOnly, "additive"
      "method-removed", ordersOnly, withoutExport, "breaking"
      "argument-added-optional", ordersOnly, extra Optional, "additive"
      "argument-added-with-default", ordersOnly, extra (OmitDefault(VBool false)), "additive"
      "argument-added-required", ordersOnly, extra Required, "breaking"
      "argument-removed",
      ordersOnly,
      withMethod "place" (fun m ->
          { m with
              Arguments = m.Arguments |> List.filter (fun a -> a.Name <> "note") }),
      "breaking"
      "arguments-reordered",
      ordersOnly,
      withMethod "list" (fun m ->
          { m with
              Arguments = List.rev m.Arguments }),
      "breaking"
      "argument-type-changed", ordersOnly, withArgument "get" "id" (fun a -> { a with Type = TInt }), "breaking"
      "argument-type-across-erased-slot",
      ordersOnly,
      withArgument "get" "id" (fun a -> { a with Type = TJson }),
      "undecided"
      "argument-loosened", ordersOnly, withArgument "get" "id" (fun a -> { a with Opt = Optional }), "additive"
      "argument-newly-required",
      ordersOnly,
      withArgument "list" "status" (fun a -> { a with Opt = Required }),
      "breaking"
      "argument-default-changed",
      ordersOnly,
      withArgument "list" "limit" (fun a -> { a with Opt = OmitDefault(VInt 50) }),
      "breaking"
      "constraint-widened", ordersOnly, limit 1 200, "additive"
      "constraint-narrowed", ordersOnly, limit 1 50, "breaking"
      "constraint-withdrawn",
      ordersOnly,
      withArgument "list" "limit" (fun a -> { a with Constraint = None }),
      "additive"
      "constraint-declared",
      ordersOnly,
      withArgument "get" "id" (fun a ->
          { a with
              Constraint = Some(ArgumentConstraint.LengthBetween(1, 64)) }),
      "breaking"
      "result-changed", ordersOnly, withMethod "get" (fun m -> { m with Result = Some TStr }), "breaking"
      "shape-changed", ordersOnly, withMethod "export" (fun m -> { m with Shape = MethodShape.Call }), "breaking"
      "effect-changed",
      ordersOnly,
      withMethod "get" (fun m ->
          { m with
              Effect =
                  { Host = ReadsHost
                    Determinism = Effect.clock } }),
      "breaking"
      "authority-removed", ordersOnly, withMethod "place" (fun m -> { m with Authority = [ "writer" ] }), "additive"
      "authority-added",
      ordersOnly,
      withMethod "get" (fun m ->
          { m with
              Authority = [ "reader"; "writer" ] }),
      "breaking"
      "served-version-added", ordersOnly, withMethod "get" (fun m -> { m with Serves = [ 1; 2 ] }), "additive"
      "served-version-withdrawn", ordersOnly, withMethod "place" (fun m -> { m with Serves = [ 2 ] }), "breaking"
      "destination-gained", ordersOnly, withMethod "get" (fun m -> { m with Destination = true }), "breaking"
      "destination-lost", ordersOnly, withMethod "place" (fun m -> { m with Destination = false }), "additive"
      "idempotent-lost", ordersOnly, withMethod "get" (fun m -> { m with Idempotent = false }), "breaking"
      "idempotent-gained", ordersOnly, withMethod "place" (fun m -> { m with Idempotent = true }), "additive"
      "read-policy-changed",
      ordersOnly,
      withMethod "get" (fun m ->
          { m with
              ReadPolicy = MethodReadPolicy.Cached 60 }),
      "breaking"
      "rate-limit-declared",
      ordersOnly,
      withMethod "get" (fun m ->
          { m with
              RateLimit = Some { Calls = 5; Seconds = 60 } }),
      "breaking"
      "rate-limit-loosened", ordersOnly, rate 20 60, "additive"
      "rate-limit-tightened", ordersOnly, rate 5 60, "breaking"
      "rate-limit-withdrawn", ordersOnly, withMethod "place" (fun m -> { m with RateLimit = None }), "additive"
      "description-changed",
      ordersOnly,
      withMethod "get" (fun m ->
          { m with
              Doc = Some "One order, by id." }),
      "host-surface"
      "audit-kind-and-record-changed",
      ordersOnly,
      withMethod "place" (fun m ->
          { m with
              AuditKind = Some "order.created"
              Record = { Arguments = true; Result = true } }),
      "host-surface" ]
