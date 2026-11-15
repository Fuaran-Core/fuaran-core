module Fuaran.Core.Tests.IdlContractTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests.ContractsIdl

// Phase 644 — the IDL gains contracts (DECISIONS.md D260, docs/idl-contracts.md). The model, its
// artifact codec, the named refusals, and the projection of a method onto a capability with its laws.

/// The names of the contract refusals a vocabulary earns, in order.
let private refusals (idl: Idl) : string list =
    Contract.faults idl |> List.map (snd >> ContractFault.name)

/// The one refusal expected of `idl`, by name, and that the artifact reader gives it too, at a path
/// under `contracts`, with the name leading the sentence.
let private refusedAs (name: string) (idl: Idl) =
    Expect.equal (refusals idl) [ name ] "the contract rules name the refusal"

    // Ill-formed text has no JSON spelling, so that one refusal is the in-memory model's alone.
    let read =
        if name = "illFormedDescription" then
            Error []
        else
            Artifact.parseDetailed (Artifact.render idl)

    match read with
    | Ok _ -> failtestf "the artifact reader admitted a vocabulary refused as %s" name
    | Error [] -> ()
    | Error errors ->
        let hit =
            errors
            |> List.filter (fun e -> e.Message.Contains("schema fault: " + name + ": "))

        Expect.isNonEmpty hit (sprintf "the reader refuses it as %s: %A" name (errors |> List.map _.Message))

        for e in hit do
            Expect.equal e.Code DecodeCode.SchemaFault "a schema fault"
            Expect.equal (List.head e.Path) (PathSegment.Key "contracts") "at a path under contracts"

let private server (idl: Idl) : Capability list =
    match ContractProjection.capabilities Server idl with
    | Ok caps -> caps
    | Error faults -> failtestf "the vocabulary did not project: %A" faults

let private capabilityNamed (id: string) : Capability =
    server idl |> List.find (fun c -> c.Id = id)

/// The argument string of an encoded value: a document's canonical text, a string's characters, a
/// number's digits, a boolean's word.
let private argumentString (j: JVal) : string =
    match j with
    | JStr s -> s
    | JInt i -> string i
    | JFloat f -> FloatLayout.canonical f
    | JBool b -> if b then "true" else "false"
    | other -> Canonical.write Rendering.CrossHost other

/// The members of the probe node, encoded by the IDL's own encoder: one value of every type.
let private probeMembers: (string * JVal) list =
    match Encode.encode idl (VNode("p", "Probe", everyValue)) with
    | Error e -> failwithf "the probe did not encode: %s" e
    | Ok text ->
        match Json.parse text with
        | Ok node ->
            match Decoder.field "kind" Ok node with
            | Ok(JObj members) -> members |> List.filter (fun (k, _) -> k <> "$type")
            | other -> failwithf "the probe has no kind object: %A" other
        | Error e -> failwithf "the probe did not parse: %s" e

let private structured =
    [ "paging"
      "filter"
      "items"
      "byName"
      "pair"
      "byDay"
      "tags"
      "anything"
      "tree" ]

[<Tests>]
let model =
    testList
        "IDL contracts"
        [ testCase "the go-red document reads: a vocabulary with a `contracts` member"
          <| fun () ->
              let text = Artifact.render idl
              Expect.stringContains text "\"contracts\": [" "the member is written"

              match Artifact.parseDetailed text with
              | Ok back -> Expect.equal back (Artifact.canonicalise idl) "and reads back as the canonical vocabulary"
              | Error errors -> failtestf "refused: %A" errors

          testCase "the fixture vocabulary is well-formed"
          <| fun () ->
              Expect.isEmpty (Declare.errors idl) "no fault"
              Expect.isEmpty (Contract.faults idl) "no contract fault"

          testCase "a vocabulary that declares no contract writes no `contracts` member"
          <| fun () ->
              let text = Artifact.render (withContracts [])
              Expect.isFalse (text.Contains "contracts") "the member is omitted at its default"

              Expect.equal
                  (Artifact.parse text |> Result.map _.Contracts)
                  (Ok [])
                  "and its absence reads as no contract"

          testCase "the canonical form sorts contracts and methods, and keeps the arguments in declared order"
          <| fun () ->
              let canon = Artifact.canonicalise idl
              Expect.equal (canon.Contracts |> List.map _.Name) [ "orders"; "probe" ] "contracts by name"

              let ordersC = canon.Contracts |> List.find (fun c -> c.Name = "orders")

              Expect.equal
                  (ordersC.Methods |> List.map _.Id)
                  [ "cancel"; "export"; "get"; "list"; "place"; "watch" ]
                  "methods by id"

              let m (id: string) =
                  ordersC.Methods |> List.find (fun m -> m.Id = id)

              Expect.equal
                  ((m "list").Arguments |> List.map _.Name)
                  [ "paging"; "limit"; "status" ]
                  "arguments as declared"

              Expect.equal
                  (m "place").ReadPolicy
                  (MethodReadPolicy.Invalidates [ "get"; "list" ])
                  "invalidated methods sorted"

              let shuffled =
                  withContracts
                      [ probe
                        { orders with
                            Methods = List.rev orders.Methods } ]

              Expect.equal (Artifact.render shuffled) (Artifact.render idl) "a reshuffle writes the same bytes"

          testCase "a method's members are omitted at their defaults and written away from them"
          <| fun () ->
              let text = Artifact.render (withContracts [ probe ])

              for omitted in
                  [ "shape"
                    "destination"
                    "readPolicy"
                    "idempotent"
                    "record"
                    "auditKind"
                    "rateLimit" ] do
                  Expect.isFalse (text.Contains("\"" + omitted + "\":")) (omitted + " is omitted at its default")

              for written in
                  [ "\"id\""
                    "\"arguments\""
                    "\"result\""
                    "\"effect\""
                    "\"authority\""
                    "\"serves\"" ] do
                  Expect.stringContains text written "a member with no default is written"

              Expect.stringContains
                  text
                  "\"result\": {\n            \"$type\": \"nothing\""
                  "a method that answers nothing says so"

              let full = Artifact.render (withContracts [ orders ])

              for written in
                  [ "\"shape\": \"stream\""
                    "\"shape\": \"job\""
                    "\"destination\": true"
                    "\"idempotent\": true"
                    "\"$type\": \"cached\""
                    "\"$type\": \"invalidates\""
                    "\"auditKind\": \"order.placed\""
                    "\"rateLimit\""
                    "\"$type\": \"intBetween\""
                    "\"$type\": \"omitDefault\"" ] do
                  Expect.stringContains full written "written away from its default"

          testCase "a method document without a member that has no default is refused at that member"
          <| fun () ->
              for name in [ "id"; "arguments"; "result"; "effect"; "authority"; "serves" ] do
                  let strip (j: JVal) : JVal =
                      match j with
                      | JObj members -> JObj(members |> List.filter (fun (k, _) -> k <> name))
                      | other -> other

                  let doc =
                      match
                          Artifact.json (
                              withContracts
                                  [ { probe with
                                        Methods = [ List.last probe.Methods ] } ]
                          )
                      with
                      | JObj members ->
                          members
                          |> List.map (fun (k, v) ->
                              match k, v with
                              | "contracts", JArr [ JObj c ] ->
                                  k,
                                  JArr
                                      [ JObj(
                                            c
                                            |> List.map (fun (ck, cv) ->
                                                match ck, cv with
                                                | "methods", JArr [ m ] -> ck, JArr [ strip m ]
                                                | _ -> ck, cv)
                                        ) ]
                              | _ -> k, v)
                          |> JObj
                      | other -> other

                  match Artifact.ofJsonDetailed doc with
                  | Ok _ -> failtestf "a method without `%s` was admitted" name
                  | Error errors ->
                      Expect.exists
                          errors
                          (fun e -> e.Code = DecodeCode.MissingField && List.last e.Path = PathSegment.Key name)
                          (sprintf "`%s` is MissingField at its own path: %A" name errors)

          testCase "a refusal's sentence leads with its name, and a method writes exactly when its effect does"
          <| fun () ->
              Expect.equal
                  (ContractFault.describe (ContractFault.NoAuthority("orders", "get")))
                  "noAuthority: method 'orders.get' declares no authority; a method open to every caller says so with a value its host defines"
                  "the sentence"

              let byId (id: string) =
                  orders.Methods |> List.find (fun m -> m.Id = id)

              Expect.isTrue (Contract.writes (byId "cancel")) "a method whose effect writes to its host"
              Expect.isFalse (Contract.writes (byId "get")) "a method that reads"

          testCase "a type expression has one canonical text, and a method projects alone as it does in its vocabulary"
          <| fun () ->
              Expect.equal
                  (ContractProjection.typeText (TList TInt))
                  (Some "{\"$type\":\"list\",\"of\":{\"$type\":\"int\"}}")
                  "the artifact's type object, rendered canonically"

              Expect.equal
                  (ContractProjection.typeText (TKeyedMap(TInt, TStr)))
                  (ContractProjection.typeText (TKeyedMap(TInt, TStr)))
                  "one expression, one text"

              Expect.notEqual
                  (ContractProjection.typeText (TList TInt))
                  (ContractProjection.typeText (TSet TInt))
                  "two expressions, two texts"

              let get = orders.Methods |> List.find (fun m -> m.Id = "get")

              Expect.equal
                  (ContractProjection.capabilityOf idl Server orders get)
                  (ContractProjection.capabilities Server idl
                   |> Result.map (List.find (fun c -> c.Id = "orders.get"))
                   |> function
                       | Ok c -> c
                       | Error e -> failtestf "%A" e)
                  "the method's own projection is its entry in the vocabulary's"

          testCase "a method at a version it serves is found, and at one it does not serve is refused by name"
          <| fun () ->
              Expect.equal (Contract.methodAt idl "orders" "cancel" 2 |> Result.map _.Id) (Ok "cancel") "served"

              Expect.equal
                  (Contract.methodAt idl "orders" "cancel" 1)
                  (Error(MethodRefusal.VersionNotServed("orders", "cancel", 1, [ 2 ])))
                  "a version it does not serve, naming what it serves"

              Expect.equal
                  (Contract.methodAt idl "orders" "nope" 1)
                  (Error(MethodRefusal.UnknownMethod("orders", "nope")))
                  "an unknown method"

              Expect.equal
                  (Contract.methodAt idl "nope" "get" 1)
                  (Error(MethodRefusal.UnknownContract "nope"))
                  "an unknown contract" ]

[<Tests>]
let refused =
    let cases = ContractsIdl.refusalCases

    testList
        "IDL contracts: every refusal is named"
        [ for label, name, bad in cases do
              testCase (label + " is refused as " + name) (fun () -> refusedAs name bad)

          testCase "every refusal the vocabulary defines has a case here"
          <| fun () ->
              let covered = cases |> List.map (fun (_, n, _) -> n) |> Set.ofList

              let all =
                  Reflection.FSharpType.GetUnionCases typeof<ContractFault>
                  |> Array.map (fun c -> string (System.Char.ToLowerInvariant c.Name[0]) + c.Name.Substring 1)
                  |> Set.ofArray

              Expect.equal covered all "one test per named refusal"

          testCase "a type naming an undeclared record is refused where a field's would be"
          <| fun () ->
              let bad =
                  withMethod "get" (fun m ->
                      { m with
                          Result = Some(TRecord "Missing") })

              let faults = Declare.errors bad |> List.map VocabularyFault.describe

              Expect.exists
                  faults
                  (fun f ->
                      f.Contains "names record 'Missing'"
                      && f.Contains "method 'orders.get': its result")
                  (sprintf "the dangling name is reported: %A" faults) ]

[<Tests>]
let projection =
    testList
        "IDL contracts: the projection onto a capability"
        [ testCase "a method projects field by field"
          <| fun () ->
              let c = capabilityNamed "orders.list"
              Expect.equal c.Signature.Name "orders.list" "the signature is named as the capability"
              Expect.equal c.Placement Server "at the placement asked for"

              Expect.equal
                  c.Signature.Effect
                  (orders.Methods |> List.find (fun m -> m.Id = "list")).Effect
                  "the method's effect"

              Expect.equal
                  c.Signature.Holes
                  [ { Addr = "paging"
                      Name = "paging"
                      Kind = ValueHole(Document(Some "Paging"))
                      Required = true }
                    { Addr = "limit"
                      Name = "limit"
                      Kind = ValueHole(IntRange(1, 100))
                      Required = false }
                    { Addr = "status"
                      Name = "status"
                      Kind = ValueHole(Enum [ "Open"; "Closed" ])
                      Required = false } ]
                  "the arguments in declared order, each in its space"

              Expect.equal
                  c.Result
                  (Returns.Value(Document(Some "{\"$type\":\"list\",\"of\":{\"$type\":\"record\",\"name\":\"Order\"}}")))
                  "an unnamed result type carries its canonical type text"

          testCase
              "a method that answers nothing projects to Returns.Nothing, a stream to its item, a job to its answer"
          <| fun () ->
              Expect.equal (capabilityNamed "orders.cancel").Result Returns.Nothing "nothing"
              Expect.equal (capabilityNamed "orders.watch").Result (Returns.Value(Document(Some "Order"))) "one item"
              Expect.equal (capabilityNamed "orders.export").Result (Returns.Value AnyString) "the final answer"

          testCase "each scalar projects to the space D260 gives it, and a float is finite"
          <| fun () ->
              let space (name: string) =
                  (capabilityNamed "probe.every").Signature.Holes
                  |> List.find (fun h -> h.Addr = name)
                  |> _.Kind

              Expect.equal (space "text") (ValueHole AnyString) "a string"
              Expect.equal (space "count") (ValueHole(IntRange(System.Int32.MinValue, System.Int32.MaxValue))) "an int"
              Expect.equal (space "flag") (ValueHole(Enum [ "false"; "true" ])) "a boolean"
              Expect.equal (space "status") (ValueHole(Enum [ "Open"; "Closed" ])) "an enumeration"

              for name in [ "big"; "amount"; "blob"; "at"; "wait" ] do
                  Expect.equal (space name) (ValueHole AnyString) (name + ": a string-borne scalar")

              let ratio = FloatRange(-System.Double.MaxValue, System.Double.MaxValue)

              Expect.equal (space "ratio") (ValueHole ratio) "a float is a range over the finite doubles"
              Expect.equal (Space.wellFormed ratio) (Ok()) "which is a well-formed space"
              Expect.isTrue (Space.validate ratio "2.5") "a finite number is a member"

              for token in [ "NaN"; "Infinity"; "-Infinity" ] do
                  Expect.isFalse (Space.validate ratio token) (token + " is refused in an argument")

                  match Capability.validateArgs (capabilityNamed "probe.every") [ "ratio", token ] with
                  | Error(ArgOutOfSpace("ratio", s, got)) ->
                      Expect.equal s ratio "the refusal names the float range"
                      Expect.equal got token "and the token"
                  | other -> failtestf "expected ArgOutOfSpace for %s, got %A" token other

          testCase "every structured type carries a document space: a name where it has one, its type text where not"
          <| fun () ->
              let constraintOf (name: string) =
                  (capabilityNamed "probe.every").Signature.Holes
                  |> List.find (fun h -> h.Addr = name)
                  |> _.Kind
                  |> function
                      | ValueHole(Document c) -> c
                      | other -> failtestf "%s is no document hole: %A" name other

              Expect.equal (constraintOf "paging") (Some "Paging") "a record by name"
              Expect.equal (constraintOf "filter") (Some "Filter") "a union by name"

              Expect.equal
                  (constraintOf "items")
                  (Some "{\"$type\":\"list\",\"of\":{\"$type\":\"int\"}}")
                  "a list by text"

              Expect.equal (constraintOf "tree") (Some "{\"$type\":\"node\"}") "a node by text"

              let texts = structured |> List.map constraintOf

              Expect.equal
                  (List.distinct texts |> List.length)
                  (List.length texts)
                  "distinct types, distinct constraints"

              for _, t in everyType do
                  Expect.equal
                      (ContractProjection.spaceOf idl t None)
                      (ContractProjection.spaceOf idl t None)
                      "one type, one space"

          testCase "LAW: a canonical value of every type is a member of the space its method declares"
          <| fun () ->
              let cap = capabilityNamed "probe.every"

              Expect.equal
                  (probeMembers |> List.map fst |> List.sort)
                  (everyType |> List.map fst |> List.sort)
                  "every type is probed"

              for name, value in probeMembers do
                  let hole = cap.Signature.Holes |> List.find (fun h -> h.Addr = name)

                  let space =
                      match hole.Kind with
                      | ValueHole s -> s
                      | other -> failtestf "%s: %A" name other

                  let text = argumentString value
                  Expect.isTrue (Space.validate space text) (sprintf "%s: %s is a member of %A" name text space)

                  if List.contains name structured then
                      Expect.isFalse (Space.validate (SlotTree None) text) (name + " is no tree: the fault D260 amends")

                      Expect.equal
                          (Space.canonical space text)
                          (Some text)
                          (name + ": the encoder's text is the canonical one")

              let args =
                  probeMembers |> List.map (fun (name, value) -> name, argumentString value)

              Expect.equal (Capability.validateArgs cap args) (Ok()) "and the whole call validates"

          testCase "LAW: distinct methods project to distinct capability ids, one capability per method"
          <| fun () ->
              let caps = server idl

              let methods =
                  idl.Contracts
                  |> List.collect (fun c -> c.Methods |> List.map (fun m -> Contract.methodName c.Name m.Id))

              Expect.equal (caps |> List.map _.Id) methods "one capability per method, in declared order"
              Expect.equal (caps |> List.map _.Id |> List.distinct |> List.length) (List.length caps) "ids are distinct"

          testCase "LAW: the holes are the arguments in order, the result and the effect class are the method's"
          <| fun () ->
              for c in idl.Contracts do
                  for m in c.Methods do
                      let cap = capabilityNamed (Contract.methodName c.Name m.Id)

                      Expect.equal
                          (cap.Signature.Holes |> List.map _.Addr)
                          (m.Arguments |> List.map _.Name)
                          "addresses in order"

                      Expect.equal
                          (cap.Signature.Holes |> List.map _.Required)
                          (m.Arguments |> List.map (fun a -> a.Opt = Required))
                          "required exactly when the argument is"

                      Expect.equal cap.Signature.Effect m.Effect "the effect class"

                      Expect.equal
                          (cap.Result = Returns.Nothing)
                          m.Result.IsNone
                          "nothing exactly when the method answers nothing"

                      Expect.equal
                          (Signature.validate cap.Signature)
                          (Ok())
                          "and the signature is one a registry admits"

          testCase "LAW: registering a projected vocabulary registers one capability per method"
          <| fun () ->
              let registry =
                  server idl
                  |> List.fold
                      (fun acc c -> acc |> Result.bind (CapabilityRegistry.register c))
                      (Ok CapabilityRegistry.empty)

              match registry with
              | Error e -> failtestf "a projected capability was refused: %A" e
              | Ok r ->
                  Expect.equal
                      (CapabilityRegistry.enumerate r |> List.map _.Id)
                      (server idl |> List.map _.Id |> List.sort)
                      "every method, once"

          testCase "LAW: an unregistered method is refused at dispatch like any other id"
          <| fun () ->
              let registry =
                  server (withContracts [ probe ])
                  |> List.fold
                      (fun acc c -> acc |> Result.bind (CapabilityRegistry.register c))
                      (Ok CapabilityRegistry.empty)
                  |> function
                      | Ok r -> r
                      | Error e -> failtestf "register: %A" e

              let dispatch (id: string) =
                  CapabilityRegistry.dispatch registry () None id [] (fun _ () -> Ready "ran")

              match dispatch "orders.get" with
              | Error(NoSuchCapability(id, known)) ->
                  Expect.equal id "orders.get" "the refusal names the id"
                  Expect.equal known [ "probe.every"; "probe.ping" ] "and what is registered"
              | other -> failtestf "expected NoSuchCapability, got %A" other

              Expect.isOk (dispatch "probe.ping") "a registered method dispatches"

          testCase "a vocabulary that is not well-formed projects nothing"
          <| fun () ->
              match ContractProjection.capabilities Server (withMethod "get" (fun m -> { m with Authority = [] })) with
              | Error faults ->
                  Expect.exists faults (fun f -> f.Detail.StartsWith "noAuthority: ") "its faults are answered"
              | Ok caps -> failtestf "projected %d capabilities from a refused vocabulary" (List.length caps)

          testCase "the function package takes no reference to the IDL"
          <| fun () ->
              let referenced =
                  typeof<Capability>.Assembly.GetReferencedAssemblies() |> Array.map _.Name

              Expect.isFalse
                  (Array.contains "Fuaran.Core.Idl" referenced)
                  "Fuaran.Core.Function does not reference Fuaran.Core.Idl"

              Expect.contains
                  (typeof<Idl>.Assembly.GetReferencedAssemblies() |> Array.map _.Name)
                  "Fuaran.Core.Function"
                  "and the IDL references the function package, one way" ]
