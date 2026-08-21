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

          testCase "the strict parser is byte-identical on null-free input"
          <| fun _ ->
              for input in strictInvariants do
                  let viaDefault = Json.parseDetailed input
                  let viaPolicy = Json.parseDetailedWithPolicy RejectNull Json.defaultMaxDepth input

                  Expect.equal viaPolicy viaDefault ("strict policy diverged from the default on: " + input)

          testCase "the strict entry points still refuse every null position"
          <| fun _ ->
              for input in [ "null"; """{"a":null}"""; "[null]"; """{"a":[{"b":null}]}""" ] do
                  match Json.parseDetailed input with
                  | Ok _ -> failtestf "strict parse accepted %s" input
                  | Error e ->
                      Expect.equal e.Kind NullNotRepresentable ("classified kind for: " + input)

                      Expect.equal
                          e.Message
                          "null is not representable in the Fuaran wire JVal model"
                          ("pinned strict message for: " + input)

          testCase "the tolerant rejection names the missing absence"
          <| fun _ ->
              match Json.parseDetailedTolerantOfNull "[1,null]" with
              | Ok _ -> failtest "tolerant parse erased an array-element null"
              | Error e ->
                  Expect.equal e.Kind NullNotRepresentable "still classified NullNotRepresentable"
                  Expect.stringContains e.Message "no absence to erase it to" "names why this position differs"

          testCase "the nesting cap still applies under the tolerant policy"
          <| fun _ ->
              let deep = String.replicate 6 "{\"a\":" + "null" + String.replicate 6 "}"

              match Json.parseTolerantOfNullWith 3 deep with
              | Ok _ -> failtest "the tolerant policy escaped the nesting cap"
              | Error m -> Expect.stringContains m "max nesting depth" "capped by name"

          testCase "Decode.parseTolerantOfNull reads an erased member as missing"
          <| fun _ ->
              let doc = """{"a":"x","b":null}"""

              match Decode.parseTolerantOfNull doc with
              | Error m -> failtestf "tolerant decode failed: %s" m
              | Ok el ->
                  Expect.equal (Decode.strField "a" el) (Ok "x") "the present member decodes"

                  Expect.equal
                      (Decode.getProp "b" el)
                      (Error "missing property: b")
                      "the erased member reads exactly as an omitted one"

          testCase "a tolerantly-read document is indistinguishable downstream"
          <| fun _ ->
              // Canon.render sorts keys, so this also pins that erasure leaves nothing
              // behind for the canonical encoder to trip on.
              let tolerant = Json.parseTolerantOfNull WireNullTolerance.foreignDocument
              let strict = Json.parse WireNullTolerance.foreignDocumentNullFree

              match tolerant, strict with
              | Ok a, Ok b ->
                  Expect.equal (Canon.render a) (Canon.render b) "canonical bytes agree"
                  Expect.isFalse ((Canon.render a).Contains "null") "no null survives into the emission"
              | _ -> failtest "one of the two spellings failed to parse" ]
