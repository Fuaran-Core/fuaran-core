module Fuaran.Core.Tests.SchemaTests

// Phase 247 — signature → JSON tool-schema projection. The generic
// artifact-function-as-tool descriptor, produced once in the core (over Core.Wire's JVal).

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

[<Tests>]
let tests =
    testList
        "Function.toSchema"
        [ testCase "projects a signature into a canonical, stable kind-tagged object"
          <| fun _ ->
              let sg = Function.signature artw "tpl" (template ())
              let json = Json.render (Function.toSchema sg)

              let expected =
                  "{\"kind\":\"signature\",\"name\":\"tpl\","
                  + "\"effect\":{\"host\":\"pure\",\"determinism\":\"deterministic\"},"
                  + "\"holes\":["
                  + "{\"addr\":\"tpl/t\",\"name\":\"title\",\"kind\":\"value\",\"required\":true,\"space\":{\"kind\":\"stringLen\",\"minLength\":1,\"maxLength\":20}},"
                  + "{\"addr\":\"tpl/c\",\"name\":\"count\",\"kind\":\"value\",\"required\":true,\"space\":{\"kind\":\"intRange\",\"min\":0,\"max\":10}},"
                  + "{\"addr\":\"tpl/s\",\"name\":\"body\",\"kind\":\"slot\",\"required\":true,\"slotKind\":\"para\"}"
                  + "],\"required\":[\"tpl/t\",\"tpl/c\",\"tpl/s\"]}"

              Expect.equal json expected "canonical schema shape"
              Expect.equal (Json.render (Function.toSchema sg)) json "stable for a fixed signature"

          testCase "the projection re-parses as valid JSON (Core.Wire decode)"
          <| fun _ ->
              let json =
                  Json.render (Function.toSchema (Function.signature artw "tpl" (template ())))

              Expect.isOk (Json.parse json) "schema is well-formed JSON"

          testCase "value-space cases map to their constraint fields"
          <| fun _ ->
              let enumSig =
                  { Name = "e"
                    Holes =
                      [ { Addr = "x"
                          Name = "mode"
                          Kind = "value"
                          Space = Some(Enum [ "a"; "b" ])
                          Slot = None
                          Action = None
                          Required = true } ]
                    Effect = Effect.pureDeterministic }

              let json = Json.render (Function.toSchema enumSig)
              Expect.stringContains json "\"kind\":\"enum\"" "enum tagged"
              Expect.stringContains json "\"values\":[\"a\",\"b\"]" "enum values projected"

          testCase "a repeat hole is non-required in the schema"
          <| fun _ ->
              let repeatTree =
                  RNode.node "root" "doc" [ RNode.hole "r" "region" "rep" (RepeatHole(IntRange(0, 5))) ]

              let sg = Function.signature artw "r" repeatTree
              let json = Json.render (Function.toSchema sg)
              Expect.stringContains json "\"kind\":\"repeat\",\"required\":false" "repeat is optional"
              // a non-required hole is absent from the required list
              Expect.stringContains json "\"required\":[]" "no required addresses" ]

// Phase 04 — standard JSON Schema projection: a saved artifact-function as a registerable tool.
[<Tests>]
let jsonSchemaTests =
    testList
        "Function.toJsonSchema"
        [ testCase "projects a signature into a standard JSON-Schema object"
          <| fun _ ->
              let sg = Function.signature artw "tpl" (template ())
              let json = Json.render (Function.toJsonSchema sg)

              let expected =
                  "{\"type\":\"object\",\"title\":\"tpl\","
                  + "\"x-effect\":{\"host\":\"pure\",\"determinism\":\"deterministic\"},"
                  + "\"properties\":{"
                  + "\"tpl/t\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":20},"
                  + "\"tpl/c\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":10},"
                  + "\"tpl/s\":{\"type\":\"object\",\"description\":\"slot of kind: para\"}"
                  + "},\"required\":[\"tpl/t\",\"tpl/c\",\"tpl/s\"]}"

              Expect.equal json expected "standard JSON-Schema shape"
              Expect.equal (Json.render (Function.toJsonSchema sg)) json "stable for a fixed signature"
              Expect.isOk (Json.parse json) "the projection is well-formed JSON"

          testCase "every value-space variant maps to its JSON-Schema keywords"
          <| fun _ ->
              let sg =
                  { Name = "all"
                    Holes =
                      [ { Addr = "i"
                          Name = "i"
                          Kind = "value"
                          Space = Some(IntRange(1, 9))
                          Slot = None
                          Action = None
                          Required = true }
                        { Addr = "f"
                          Name = "f"
                          Kind = "value"
                          Space = Some(FloatRange(0.0, 1.0))
                          Slot = None
                          Action = None
                          Required = true }
                        { Addr = "s"
                          Name = "s"
                          Kind = "value"
                          Space = Some(StringLen(2, 5))
                          Slot = None
                          Action = None
                          Required = true }
                        { Addr = "e"
                          Name = "e"
                          Kind = "value"
                          Space = Some(Enum [ "a"; "b" ])
                          Slot = None
                          Action = None
                          Required = true }
                        { Addr = "a"
                          Name = "a"
                          Kind = "value"
                          Space = Some AnyString
                          Slot = None
                          Action = None
                          Required = true }
                        { Addr = "r"
                          Name = "r"
                          Kind = "repeat"
                          Space = Some(IntRange(0, 3))
                          Slot = None
                          Action = None
                          Required = false } ]
                    Effect = Effect.pureDeterministic }

              let json = Json.render (Function.toJsonSchema sg)
              Expect.stringContains json "\"i\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":9}" "int range"
              Expect.stringContains json "\"f\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1}" "float range"
              Expect.stringContains json "\"s\":{\"type\":\"string\",\"minLength\":2,\"maxLength\":5}" "string length"
              Expect.stringContains json "\"e\":{\"enum\":[\"a\",\"b\"]}" "enum"
              Expect.stringContains json "\"a\":{\"type\":\"string\"}" "anyString"
              // the repeat hole is present as a property but absent from required
              Expect.stringContains json "\"required\":[\"i\",\"f\",\"s\",\"e\",\"a\"]" "repeat excluded from required" ]
