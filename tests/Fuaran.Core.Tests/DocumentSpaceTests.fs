module Fuaran.Core.Tests.DocumentSpaceTests

open Expecto
open Fuaran.Core

// Phase 644 — the document space (DECISIONS.md D260). A structured value at a hole or a result is a
// well-formed JSON document, with the type it is a value of carried as a string this package never
// reads. D253.3 had stated such a value as `SlotTree(Some name)`, and no canonical document of a
// structured type is a member of that space (it wants a top-level string `"kind"`): the first test
// below is that finding, kept as a test.

let private order = Document(Some "Order")

let private canonical = "{\"a\":1,\"b\":[true,\"x\\u000ay\",2.5]}"

let private respelled = "{ \"b\" : [ true, \"x\\ny\", 2.50 ],\n  \"a\" : 1 }"

/// Documents in the shapes a structured type writes: a record, a tagged union value, a node in each
/// envelope shape, a list, a map, a tuple, a set, a keyed map, a JSON scalar.
let private shapes: (string * string) list =
    [ "a record", "{\"pageSize\":10,\"pageStateKey\":\"k\"}"
      "a union value", "{\"$type\":\"Literal\",\"text\":\"x\"}"
      "a node, kind nested", "{\"id\":\"t\",\"kind\":{\"$type\":\"Tally\",\"amount\":\"12.25\"}}"
      "a node, kind flat", "{\"$type\":\"Tally\",\"amount\":\"12.25\",\"id\":\"t\"}"
      "a list", "[1,2,3]"
      "a map", "{\"a\":1}"
      "a tuple", "[\"x\",2.5]"
      "a keyed map", "[[1,\"a\"],[2,\"b\"]]"
      "a JSON string", "\"text\""
      "a JSON number", "5" ]

let private others: ValueSpace list =
    [ IntRange(0, 9)
      FloatRange(0.0, 9.5)
      StringLen(0, 99)
      Enum [ "a"; "b" ]
      AnyString
      SlotTree None
      SlotTree(Some "Order") ]

let private capOver (space: ValueSpace) (result: Returns) : Capability =
    Capability.create
        "place"
        { Name = "place"
          Holes =
            [ { Addr = "order"
                Name = "order"
                Kind = ValueHole space
                Required = true } ]
          Effect = Effect.pureDeterministic }
        Server
        result

[<Tests>]
let tests =
    testList
        "Document space"
        [ testCase "no document of a structured type is a tree, and every one is a document"
          <| fun () ->
              for label, text in shapes do
                  Expect.isFalse (Space.validate (SlotTree None) text) (label + " is no tree")
                  Expect.isTrue (Space.validate (Document None) text) (label + " is a document")
                  Expect.isTrue (Space.validate order text) (label + " is a document of a named type")

          testCase "membership is by shape: the type name is carried and never read"
          <| fun () ->
              for text in [ canonical; respelled; "{\"a\":1"; "null"; "order"; "{\"a\":1,\"a\":2}"; "" ] do
                  Expect.equal
                      (Space.validate order text)
                      (Space.validate (Document(Some "Receipt")) text)
                      "two named types agree"

                  Expect.equal (Space.validate order text) (Space.validate (Document None) text) "and so does no name"

          testCase "text that is no document is outside the space"
          <| fun () ->
              for text in
                  [ "{\"a\":1"
                    "null"
                    "[1,null]"
                    "order"
                    "{\"a\":1,\"a\":2}"
                    ""
                    "{\"a\":1} x" ] do
                  Expect.isFalse (Space.validate order text) (sprintf "%s is refused" text)
                  Expect.isNone (Space.canonical order text) "and has no spelling"

          testCase "one document has one spelling, whichever way it was written"
          <| fun () ->
              Expect.equal (Space.canonical order canonical) (Some canonical) "the canonical text is a fixed point"
              Expect.equal (Space.canonical order respelled) (Some canonical) "a second spelling is the first"
              Expect.equal (Space.documentOf respelled) (Some canonical) "which is what the reader answers"

              Expect.equal
                  (Space.canonical order "{\"n\":2.0,\"m\":-0.0}")
                  (Some "{\"m\":0,\"n\":2}")
                  "the format's normalisations are the rendering's"

          testCase "two spellings of one document are one invocation, one key and one grant"
          <| fun () ->
              let cap = capOver order Returns.Nothing
              let a = [ "order", canonical ]
              let b = [ "order", respelled ]
              Expect.equal (Capability.invocationKey cap a) (Capability.invocationKey cap b) "one key"
              Expect.equal (Capability.invocationPreimage cap a) (Capability.invocationPreimage cap b) "one pre-image"

              Expect.notEqual
                  (Capability.invocationKey cap a)
                  (Capability.invocationKey cap [ "order", "{\"a\":2}" ])
                  "and another document is another key"

          testCase "the document spaces are a family of their own in the relation"
          <| fun () ->
              Expect.isTrue (Space.subsumes (Document None) order) "no type admits a named type"
              Expect.isTrue (Space.subsumes (Document None) (Document None)) "and itself"
              Expect.isTrue (Space.subsumes order order) "a named type admits itself"
              Expect.isFalse (Space.subsumes order (Document None)) "and not an unnamed document"
              Expect.isFalse (Space.subsumes order (Document(Some "Receipt"))) "nor another type"

              for other in others do
                  for doc in [ Document None; order ] do
                      Expect.isFalse (Space.subsumes other doc) (sprintf "%A admits no document" other)
                      Expect.isFalse (Space.subsumes doc other) (sprintf "a document space does not admit %A" other)

          testCase "a document space is well-formed, counts nothing and reads back"
          <| fun () ->
              for space in [ Document None; order ] do
                  Expect.equal (Space.wellFormed space) (Ok()) "well-formed"
                  Expect.isFalse (Space.isCount space) "no repeat counts over it"

                  Expect.equal
                      (SpaceCodec.decoder (SpaceCodec.toJson space))
                      (Ok space)
                      "the document spelling reads back"

              Expect.equal
                  (Json.render (SpaceCodec.toJson order))
                  "{\"$type\":\"document\",\"documentType\":\"Order\"}"
                  "the wire document"

              Expect.equal
                  (Json.render (SpaceCodec.toJson (Document None)))
                  "{\"$type\":\"document\"}"
                  "and with no type"

              Expect.equal
                  (Json.render (SpaceCodec.descriptorJson order))
                  "{\"kind\":\"document\",\"documentType\":\"Order\"}"
                  "the descriptor spelling"

          testCase "the space says in words what it accepts"
          <| fun () ->
              Expect.equal (Space.describe (Document None)) "a document: well-formed JSON" "unnamed"
              Expect.equal (Space.describe order) "a document of type 'Order': well-formed JSON" "named"

          testCase "a capability declaration over a document space round-trips, and its strict read knows the member"
          <| fun () ->
              let cap = capOver order (Returns.Value(Document(Some "Receipt")))
              let wire = CapabilityCodec.encode cap
              Expect.equal (CapabilityCodec.decodeDetailedWith ReadPolicy.Strict wire) (Ok cap) "strict"
              Expect.equal (CapabilityCodec.decodeDetailedWith ReadPolicy.Lenient wire) (Ok cap) "lenient"

          testCase "an argument that is no document is out of space, naming the space"
          <| fun () ->
              let cap = capOver order Returns.Nothing

              match Capability.validateArgs cap [ "order", "{\"a\":1" ] with
              | Error(ArgOutOfSpace("order", space, "{\"a\":1") as e) ->
                  Expect.equal space order "the refusal carries the space"

                  Expect.stringContains
                      (InvokeError.describe e)
                      "a document of type 'Order': well-formed JSON"
                      "and says what would be accepted"
              | other -> failtestf "expected ArgOutOfSpace, got %A" other

          testCase "a body is handed the parsed document"
          <| fun () ->
              let cap = capOver order Returns.Nothing

              match Capability.typeArgs cap [ "order", respelled ] with
              | Ok [ "order", DocumentValue(JObj members) ] ->
                  Expect.equal (members |> List.map fst) [ "b"; "a" ] "parsed, in the order it was written"
              | other -> failtestf "expected one DocumentValue, got %A" other

          testCase "the registry files a document result under its type name"
          <| fun () ->
              let entry result =
                  FunctionRegistry.entry (capOver order result)

              Expect.equal
                  (entry (Returns.Value(Document(Some "Receipt")))).ResultType
                  (Some "Receipt")
                  "a named document"

              Expect.equal (entry (Returns.Value(Document None))).ResultType None "an unnamed one has no name"

              let registry =
                  match
                      FunctionRegistry.register (entry (Returns.Value(Document(Some "Receipt")))) FunctionRegistry.empty
                  with
                  | Ok r -> r
                  | Error e -> failtestf "register: %A" e

              let found (name: string) =
                  FunctionRegistry.findBySignature
                      Subsumes
                      { ResultType = Some name
                        Available = (capOver order Returns.Nothing).Signature.Holes }
                      registry
                  |> List.map _.Capability.Id

              Expect.equal (found "Receipt") [ "place" ] "found by the type it answers"
              Expect.equal (found "Order") [] "and by no other"

          testCase "a document hole projects no JSON Schema type, since a document is any JSON value"
          <| fun () ->
              let schema =
                  Json.render (Function.toJsonSchema (capOver order Returns.Nothing).Signature)

              Expect.stringContains schema "\"order\":{\"description\":\"document of type: Order\"}" "its property" ]
