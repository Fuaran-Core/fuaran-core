module Fuaran.Core.Tests.WireNullToleranceTests

// Phase 102 — the null-tolerant read. The conformance family in
// `Fuaran.Core.Conformance` carries the vectors; this suite runs them and adds the two
// claims a vector set cannot make about itself: that the STRICT parser is unchanged
// (same errors, same positions, same messages, on inputs that have nothing to do with
// null), and that the tolerance is reachable through the `Decode` entry point a consumer
// actually calls.

open Expecto
open Fuaran.Core

/// Inputs with no `null` anywhere: the strict parser must answer identically before and
/// after the policy parameter was threaded through it. Every one of these exercises a
/// different failure classification, since a silently-moved POSITION is the regression
/// this pins and a value-only check would not see it.
let private strictInvariants =
    [ "{}"
      "[]"
      """{"a":1}"""
      """{"a":{"b":[1,2,{"c":"d"}]}}"""
      "  \n {\"a\" : 1 } \t "
      """{"a":}"""
      """{"a" 1}"""
      "{"
      "["
      """{"a":1,}"""
      """{"a":01}"""
      """{"a":1e400}"""
      """{"a":"unterminated"""
      """{"a":"\q"}"""
      """{"a":"\u00zz"}"""
      """{"a":tru}"""
      "{} trailing"
      "" ]

[<Tests>]
let tests =
    testList
        "WireNullTolerance"
        [ testList
              "conformance family"
              [ for v in WireNullTolerance.vectors ->
                    testCase v.Name
                    <| fun _ ->
                        let outcome = WireNullTolerance.runVector v
                        Expect.isTrue outcome.Passed outcome.Detail ]

          testCase "the family reports a single green verdict"
          <| fun _ ->
              match WireNullTolerance.check () with
              | Ok() -> ()
              | Error m -> failtestf "null-tolerance family failed: %s" m

          testCase "the two policies read null-free input identically"
          <| fun _ ->
              for input in strictInvariants do
                  let strict = Json.read ReadOptions.strict input
                  let tolerant = Json.read ReadOptions.tolerantOfNull input

                  Expect.equal tolerant strict ("the tolerant policy diverged from the strict on: " + input)

          testCase "the strict entry points still refuse every null position"
          <| fun _ ->
              for input in [ "null"; """{"a":null}"""; "[null]"; """{"a":[{"b":null}]}""" ] do
                  match Json.read ReadOptions.strict input with
                  | Ok _ -> failtestf "strict parse accepted %s" input
                  | Error(JsonRefusal.LimitExceeded b) -> failtestf "strict parse breached %A on %s" b input
                  | Error(JsonRefusal.Malformed e) ->
                      Expect.equal e.Kind NullNotRepresentable ("classified kind for: " + input)

                      Expect.equal
                          e.Message
                          "null is not representable in the Fuaran wire JVal model"
                          ("pinned strict message for: " + input)

          testCase "the tolerant rejection names the missing absence"
          <| fun _ ->
              match Json.read ReadOptions.tolerantOfNull "[1,null]" with
              | Ok _ -> failtest "tolerant parse erased an array-element null"
              | Error(JsonRefusal.LimitExceeded b) -> failtestf "tolerant parse breached %A" b
              | Error(JsonRefusal.Malformed e) ->
                  Expect.equal e.Kind NullNotRepresentable "still classified NullNotRepresentable"
                  Expect.stringContains e.Message "no absence to erase it to" "names why this position differs"

          testCase "the nesting cap still applies under the tolerant policy"
          <| fun _ ->
              let deep = String.replicate 6 "{\"a\":" + "null" + String.replicate 6 "}"

              let capped =
                  { ReadOptions.tolerantOfNull with
                      Limits = JsonLimits.depthOnly 3 }

              match Json.read capped deep with
              | Error(JsonRefusal.LimitExceeded b) -> Expect.equal b.Limit JsonLimit.Depth "capped by the depth bound"
              | other -> failtestf "the tolerant policy escaped the nesting cap: %A" other

          testCase "Decoder.parse tolerantOfNull reads an erased member as missing"
          <| fun _ ->
              let doc = """{"a":"x","b":null}"""

              match Decoder.parse ReadOptions.tolerantOfNull doc with
              | Error e -> failtestf "tolerant decode failed: %s" (DecodeError.render e)
              | Ok el ->
                  Expect.equal
                      (Decoder.describing (Decoder.field "a" Decoder.str) el)
                      (Ok "x")
                      "the present member decodes"

                  Expect.equal
                      (Decoder.describing (Decoder.field "b" Decoder.json) el)
                      (Error "missing property: b")
                      "the erased member reads exactly as an omitted one"

          testCase "a tolerantly-read document is indistinguishable downstream"
          <| fun _ ->
              // Canonical.write Rendering.CrossHost sorts keys, so this also pins that erasure leaves nothing
              // behind for the canonical encoder to trip on.
              let tolerant =
                  Json.read ReadOptions.tolerantOfNull WireNullTolerance.foreignDocument

              let strict = Json.read ReadOptions.strict WireNullTolerance.foreignDocumentNullFree

              match tolerant, strict with
              | Ok a, Ok b ->
                  Expect.equal
                      (Canonical.write Rendering.CrossHost a)
                      (Canonical.write Rendering.CrossHost b)
                      "canonical bytes agree"

                  Expect.isFalse
                      ((Canonical.write Rendering.CrossHost a).Contains "null")
                      "no null survives into the emission"
              | _ -> failtest "one of the two spellings failed to parse" ]
