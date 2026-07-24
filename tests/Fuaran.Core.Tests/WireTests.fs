module Fuaran.Core.Tests.WireTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// A reference codec over RNode, built from the Core.Wire encode DSL + decode
// combinators. Per-kind cases are domain-side (here); the core owns the envelope.
let rec private encodeNode (n: RNode) : JVal =
    Json.kindObj
        n.Kind
        [ "id", JStr n.Id
          "value", JStr n.Value
          "children", JArr(n.Children |> List.map encodeNode) ]

let private encode (n: RNode) = Json.render (encodeNode n)

let rec private decodeNode (el) : Result<RNode, string> =
    Decode.kindOf el
    |> Result.bind (fun kind ->
        Decode.strField "id" el
        |> Result.bind (fun id ->
            Decode.strField "value" el
            |> Result.bind (fun value ->
                Decode.getProp "children" el
                |> Result.bind (Decode.mapList decodeNode)
                |> Result.map (fun kids ->
                    { Id = id
                      Kind = kind
                      Value = value
                      Hole = None
                      HoleName = ""
                      Eff = Effect.pureDeterministic
                      Children = kids }))))

let private decode (s: string) =
    Decode.parse s |> Result.bind decodeNode

let private codec: Corpus.Codec<RNode> = { Encode = encode; Decode = decode }

[<Tests>]
let tests =
    testList
        "Wire"
        [ testCase "encode produces a kind-tagged camelCase envelope"
          <| fun _ ->
              let json = encode (RNode.leaf "a1" "para" "x")
              Expect.stringContains json "\"kind\":\"para\"" "kind tag leads"
              Expect.stringContains json "\"id\":\"a1\"" "id field"

          testCase "JSON escaping handles quotes and control chars"
          <| fun _ ->
              let json = Json.render (JStr "a\"b\nc")
              Expect.equal json "\"a\\\"b\\nc\"" "escaped"

          testCase "value round-trips through the codec"
          <| fun _ ->
              match Corpus.roundTrip codec (sample ()) with
              | Ok() -> ()
              | Error m -> failtestf "round-trip failed: %s" m

          testCase "decode rejects malformed JSON"
          <| fun _ -> Expect.isError (decode "{not json") "malformed ⇒ Error"

          testCase "decode rejects a missing required field"
          <| fun _ -> Expect.isError (decode "{\"kind\":\"para\",\"id\":\"a\"}") "missing value/children ⇒ Error"

          testCase "runCorpus passes a round-trip case and a reject case"
          <| fun _ ->
              let cases =
                  [ { Corpus.Name = "leaf"
                      Corpus.Kind = Corpus.RoundTrip
                      Corpus.Json = encode (RNode.leaf "a1" "para" "x")
                      Corpus.Tag = "para" }
                    { Corpus.Name = "no-kind"
                      Corpus.Kind = Corpus.Reject
                      Corpus.Json = "{\"id\":\"x\"}"
                      Corpus.Tag = "reject" } ]

              let outcomes = Corpus.runCorpus codec cases
              Expect.isTrue (outcomes |> List.forall (fun o -> o.Passed)) "all corpus cases pass"

          testCase "coverageGate flags a missing tag"
          <| fun _ ->
              let cases =
                  [ { Corpus.Name = "leaf"
                      Corpus.Kind = Corpus.RoundTrip
                      Corpus.Json = "{}"
                      Corpus.Tag = "para" } ]

              Expect.isError (Corpus.coverageGate [ "para"; "section" ] cases) "section uncovered"
              Expect.isOk (Corpus.coverageGate [ "para" ] cases) "para covered"

          // Phase 18 — generative round-trip fuzzing over a wide random JVal sample.
          testCase "fuzzRoundTrip: render is parse-round-trip-idempotent across a random sample"
          <| fun _ ->
              match Corpus.fuzzRoundTrip 1 2000 6 with
              | Ok() -> ()
              | Error m -> failtest m

          testCase "fuzzRoundTrip is seed-replayable (same seed ⇒ same verdict)"
          <| fun _ ->
              Expect.equal (Corpus.fuzzRoundTrip 42 500 5) (Corpus.fuzzRoundTrip 42 500 5) "deterministic by seed"

          // Phase 20 — domain-Codec generative round-trip law.
          testCase "codecLaws certifies the reference RNode codec over a random sample"
          <| fun _ ->
              // a seeded RNode generator using only RNode.node/leaf (so the decoded defaults —
              // Hole=None, HoleName="", Eff=pureDeterministic — match, keeping round-trip exact)
              let genRNode (seed: int) : RNode =
                  let mutable st = (uint32 seed * 2654435761u) + 1u

                  let next () =
                      st <- (st * 1664525u) + 1013904223u
                      int (st >>> 1)

                  let mutable counter = 0

                  let freshId () =
                      counter <- counter + 1
                      sprintf "n%d" counter

                  let rec gen depth =
                      if depth >= 3 || next () % 2 = 0 then
                          RNode.leaf (freshId ()) "para" (sprintf "v%d" (next () % 100))
                      else
                          RNode.node (freshId ()) "section" [ for _ in 0 .. next () % 3 -> gen (depth + 1) ]

                  gen 0

              match Corpus.codecLaws codec genRNode 1 500 with
              | Ok() -> ()
              | Error m -> failtest m

          testCase "codecLaws catches a broken codec"
          <| fun _ ->
              // a decode that drops the value field ⇒ round-trip differs on any non-empty value
              let broken =
                  { codec with
                      Decode = fun s -> codec.Decode s |> Result.map (fun n -> { n with Value = "MANGLED" }) }

              let genLeaf (seed: int) =
                  RNode.leaf (sprintf "n%d" seed) "para" "real"

              Expect.isError (Corpus.codecLaws broken genLeaf 1 50) "broken decode yields a counterexample" ]
