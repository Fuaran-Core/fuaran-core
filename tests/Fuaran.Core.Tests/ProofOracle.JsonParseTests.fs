module Fuaran.Core.Tests.ProofOracle.JsonParseTests

// The `Proofs.Oracle` differential for `proofs/JsonParse.fst`: the extracted model
// (`proofs/oracle/JsonParse.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- the PARSER (Phase 146) — the boundary the `WireDecode` model named ----

          testCase "the parser oracle agrees with production over the near-miss table"
          <| fun _ ->
              JsonParseDiff.sweep RejectNull 512 JsonParseDiff.nearMisses
              |> JsonParseDiff.expectAgreement "near misses, strict"

          // Phase 299 — the integer reader is the invariant one, so the differential answers the
          // same under a culture whose negative sign is not U+002D. Before, `-5` fell to the float
          // path there, and every near miss carrying a negative integer disagreed with the model.
          testCase "… and under fa-IR and he-IL, whose negative sign is not U+002D"
          <| fun _ ->
              for culture in [ "fa-IR"; "he-IL" ] do
                  let saved = System.Globalization.CultureInfo.CurrentCulture
                  System.Globalization.CultureInfo.CurrentCulture <- System.Globalization.CultureInfo culture

                  try
                      JsonParseDiff.sweep RejectNull 512 JsonParseDiff.nearMisses
                      |> JsonParseDiff.expectAgreement ("near misses, strict, under " + culture)
                  finally
                      System.Globalization.CultureInfo.CurrentCulture <- saved

          testCase "… and under the tolerant read policy, where the member-null fork lives"
          <| fun _ ->
              JsonParseDiff.sweep EraseMemberNull 512 JsonParseDiff.nearMisses
              |> JsonParseDiff.expectAgreement "near misses, tolerant"

              // The fork is asserted to have FIRED, because two parsers that never met a member
              // null agree about a policy neither exercised.
              Expect.equal
                  (JsonParseDiff.prodAnswer EraseMemberNull 512 "{\"a\":null,\"b\":1}")
                  "ok {b=i:1}"
                  "the tolerant policy erases a member null"

              Expect.notEqual
                  (JsonParseDiff.prodAnswer RejectNull 512 "{\"a\":null,\"b\":1}")
                  (JsonParseDiff.prodAnswer EraseMemberNull 512 "{\"a\":null,\"b\":1}")
                  "and the strict policy does not — the two policies were actually distinguished"

          testCase "… and the absorption IS the erasure — Phase 190's bridge, erase-then-compare"
          <| fun _ ->
              // The theorem, put to production and to the extracted model rather than only to the
              // prover: reading a document under the TOLERANT policy is reading the document with
              // its member nulls DELETED under the STRICT one. Four answers per pair, so a model
              // that erased the wrong thing and a production that erased the wrong thing are each
              // caught, and a shared mistake is caught by the two of them disagreeing with each
              // other in the probes above.
              Tally.empty []
              |> Tally.classes [ "production-erasure"; "model-erasure" ]
              |> Differential.over JsonParseDiff.erasurePairs (fun (name, absorbed, erased) t ->
                  let pa =
                      JsonParseDiff.withoutPosition (JsonParseDiff.prodAnswer EraseMemberNull 512 absorbed)

                  let pe =
                      JsonParseDiff.withoutPosition (JsonParseDiff.prodAnswer RejectNull 512 erased)

                  let ma =
                      JsonParseDiff.withoutPosition (
                          JsonParseDiff.modelAnswer
                              JsonParseDiff.toChs
                              EraseMemberNull
                              512
                              (JsonParseDiff.budget 512)
                              absorbed
                      )

                  let me =
                      JsonParseDiff.withoutPosition (
                          JsonParseDiff.modelAnswer
                              JsonParseDiff.toChs
                              RejectNull
                              512
                              (JsonParseDiff.budget 512)
                              erased
                      )

                  let bridges (cls: string) (who: string) (a: string) (e: string) (t: Tally) =
                      if a = e then
                          t
                      else
                          Tally.record
                              cls
                              (sprintf
                                  "%s: %s does not bridge\n    tolerant %s -> %s\n    strict   %s -> %s"
                                  name
                                  who
                                  absorbed
                                  a
                                  erased
                                  e)
                              t

                  t
                  |> bridges "production-erasure" "PRODUCTION" pa pe
                  |> bridges "model-erasure" "the MODEL" ma me
                  |> Differential.compareWith
                      Differential.text
                      (fun p m -> sprintf "%s: the model and production disagree on %s: %s vs %s" name absorbed p m)
                      pa
                      ma)
              |> Differential.expectAgrees "erase-then-compare"

              // Both halves of the bridge were actually reached, so a green run above is not one
              // that only ever accepted or only ever refused.
              Expect.isTrue
                  (JsonParseDiff.erasurePairs
                   |> List.exists (fun (_, a, _) -> (JsonParseDiff.prodAnswer EraseMemberNull 512 a).StartsWith "ok "))
                  "no pair was ACCEPTED — the value half of the bridge measured nothing"

              Expect.isTrue
                  (JsonParseDiff.erasurePairs
                   |> List.exists (fun (_, a, _) ->
                       not ((JsonParseDiff.prodAnswer EraseMemberNull 512 a).StartsWith "ok ")))
                  "no pair was REFUSED — the classification half of the bridge measured nothing"

              // THE TWO HYPOTHESES, each shown to be load-bearing on production rather than only
              // needed by the prover. `members_follow`: a null member closed by a COMMA leaves a
              // trailing comma behind, which is not the empty object the deletion would give.
              Expect.notEqual
                  (JsonParseDiff.withoutPosition (JsonParseDiff.prodAnswer EraseMemberNull 512 "{\"a\":null,}"))
                  (JsonParseDiff.withoutPosition (JsonParseDiff.prodAnswer RejectNull 512 "{}"))
                  "a trailing comma left by the absorption is NOT the empty object — the hypothesis the theorem carries"

              // And the null carve-out: a null the tolerant policy declines to erase is refused
              // with the SAME kind and a DIFFERENT message, which is why the refusal half of the
              // theorem excludes `NullNotRepresentable` rather than covering it.
              let arrA =
                  JsonParseDiff.withoutPosition (
                      JsonParseDiff.prodAnswer EraseMemberNull 512 "{\"a\":null,\"b\":[null]}"
                  )

              let arrE =
                  JsonParseDiff.withoutPosition (JsonParseDiff.prodAnswer RejectNull 512 "{\"b\":[null]}")

              Expect.stringContains
                  arrA
                  "NullNotRepresentable"
                  "the array null is still refused under the tolerant policy"

              Expect.stringContains arrE "NullNotRepresentable" "and under the strict one"

              Expect.notEqual
                  arrA
                  arrE
                  "the two policies word that refusal differently — the carve-out the theorem's refusal half names"

          testCase "the parser oracle agrees with production over every corpus nodes/ fixture"
          <| fun _ ->
              JsonParseDiff.corpusTexts "nodes"
              |> JsonParseDiff.sweep RejectNull 512
              |> fun t ->
                  Differential.expectAgrees "corpus nodes/" t

                  // Every fixture is valid wire JSON, so this pool is the ACCEPT path and has no
                  // refusals to assert — saying so here rather than letting the shared guard
                  // demand a refusal that would mean the corpus was broken.
                  Expect.isGreaterThan
                      (Tally.count "Accepted" t)
                      100
                      "the corpus nodes/ pool is far smaller than expected"

          testCase "… and over every corpus ops/ fixture"
          <| fun _ ->
              JsonParseDiff.corpusTexts "ops"
              |> JsonParseDiff.sweep RejectNull 512
              |> fun t ->
                  Differential.expectAgrees "corpus ops/" t
                  Expect.isGreaterThan (Tally.count "Accepted" t) 10 "the corpus ops/ pool is far smaller than expected"

          testCase "the parser oracle agrees with production over generated deep and wide inputs"
          <| fun _ ->
              // Both nesting families at every depth from 0 to 8, under caps that straddle each —
              // so the boundary itself is compared, not merely the interior.
              let deep =
                  [ for cap in 0..8 do
                        for k in 0..8 do
                            yield sprintf "arr k=%d cap=%d" k cap, (cap, JsonParseDiff.nestArr k)
                            yield sprintf "obj k=%d cap=%d" k cap, (cap, JsonParseDiff.nestObj k) ]

              Tally.empty []
              |> Differential.over deep (fun (name, (cap, input)) t ->
                  t
                  |> Differential.compareWith
                      (Differential.pair
                          (JsonParseDiff.prodAnswer RejectNull cap)
                          (JsonParseDiff.modelAnswer JsonParseDiff.toChs RejectNull cap (JsonParseDiff.budget cap)))
                      (fun p m -> sprintf "%s:\n    production %s\n    model      %s" name p m)
                      input
                      input)
              |> Differential.expectAgrees "deep/wide"

              // The boundary was actually crossed in both directions.
              Expect.stringStarts
                  (JsonParseDiff.prodAnswer RejectNull 3 (JsonParseDiff.nestArr 3))
                  "ok "
                  "a document nested to the cap is accepted"

              // Past the cap, production's Depth breach and the model's cap refusal meet at the
              // bracket that opens the fourth array — the lift, at and past the bound, for both
              // families (Phase 519).
              Expect.equal
                  (JsonParseDiff.prodAnswer RejectNull 3 (JsonParseDiff.nestArr 4))
                  (JsonParseDiff.depthLine 3)
                  "and one nested past it is the Depth breach at its bracket"

              Expect.equal
                  (JsonParseDiff.modelAnswer
                      JsonParseDiff.toChs
                      RejectNull
                      3
                      (JsonParseDiff.budget 3)
                      (JsonParseDiff.nestArr 4))
                  (JsonParseDiff.depthLine 3)
                  "the model's cap refusal renders as the same line"

              Expect.equal
                  (JsonParseDiff.prodAnswer RejectNull 0 "{}")
                  (JsonParseDiff.depthLine 0)
                  "a cap of 0 refuses the root object at its brace"

              let wide =
                  [ for n in [ 0; 1; 2; 10; 64 ] do
                        yield sprintf "wide array %d" n, "[" + String.concat "," [ for i in 1..n -> string i ] + "]"

                        yield
                            sprintf "wide object %d" n,
                            "{" + String.concat "," [ for i in 1..n -> sprintf "\"k%d\":%d" i i ] + "}" ]

              JsonParseDiff.sweep RejectNull 512 (wide @ [ "refusal", "[" ])
              |> JsonParseDiff.expectAgreement "wide"

          testCase "the parser oracle agrees with production over 4000 generated inputs"
          <| fun _ ->
              JsonParseDiff.sweep RejectNull 512 (JsonParseDiff.soup 20260914 2000)
              |> JsonParseDiff.expectAgreement "soup, strict"

              JsonParseDiff.sweep EraseMemberNull 512 (JsonParseDiff.soup 20260915 2000)
              |> JsonParseDiff.expectAgreement "soup, tolerant"

          // ---- the LIMITS (Phase 465) — the limited parse beside `parse_limited` ----

          testCase
              "the limited parse agrees with the model over hand-picked documents, the near misses and a soup, under five records"
          <| fun _ ->
              let wide =
                  { MaxDepth = 512
                    MaxStringLength = 1 <<< 20
                    MaxArrayLength = 100000
                    MaxObjectMembers = 100000
                    MaxDocumentBytes = 1 <<< 25 }

              // One record per bound, and one with every bound tight enough for the soup to reach.
              let records =
                  [ "depth 2", { wide with MaxDepth = 2 }
                    "string 3", { wide with MaxStringLength = 3 }
                    "items 2", { wide with MaxArrayLength = 2 }
                    "members 2", { wide with MaxObjectMembers = 2 }
                    "all tight",
                    { MaxDepth = 3
                      MaxStringLength = 2
                      MaxArrayLength = 2
                      MaxObjectMembers = 2
                      MaxDocumentBytes = 12 } ]

              let smile = "\U0001F600"

              // Each bound at and past its value, in both spellings where a spelling matters, a
              // key, a nested position, and a breach beside malformed text on either side of it.
              let handPicked =
                  [ "depth at", "[[1]]"
                    "depth past", "[[[1]]]"
                    "string at", "\"abc\""
                    "string past", "\"abcd\""
                    "string astral at", "\"" + smile + smile + smile + "\""
                    "string astral past", "\"" + smile + smile + smile + smile + "\""
                    "string escaped astral at", "\"\\uD83D\\uDE00\\uD83D\\uDE00\\uD83D\\uDE00\""
                    "string escaped past", "\"\\u0061\\u0062\\u0063\\u0064\""
                    "string in a key", "{\"abcd\":1}"
                    "string nested", "{\"a\":[\"abcd\"]}"
                    "items at", "[1,2]"
                    "items past", "[1,2,3]"
                    "items nested", "{\"a\":[1,2,3]}"
                    "empty array", "[]"
                    "members at", "{\"a\":1,\"b\":2}"
                    "members past", "{\"a\":1,\"b\":2,\"c\":3}"
                    "empty object", "{}"
                    "breach before malformed", "[1,2,3"
                    "malformed before breach", "[1,%,3,4]"
                    "cjk", "\"日本語\"" ]

              let pools =
                  [ for (rn, r) in records do
                        for (name, input) in handPicked @ JsonParseDiff.nearMisses @ JsonParseDiff.soup 20261015 1500 do
                            yield sprintf "%s under %s" name rn, r, input ]

              let bytesOk (r: JsonLimits) (input: string) =
                  System.Text.Encoding.UTF8.GetByteCount input <= r.MaxDocumentBytes

              Tally.empty []
              |> Differential.over pools (fun (name, r, input) t ->
                  t
                  |> Differential.compareWith
                      (Differential.pair
                          (JsonParseDiff.prodAnswerLimited RejectNull r)
                          (JsonParseDiff.modelAnswerLimited JsonParseDiff.toChs RejectNull r (bytesOk r input)))
                      (fun p m -> sprintf "%s: input %s\n    production %s\n    model      %s" name input p m)
                      input
                      input)
              |> Differential.expectAgrees "limits"

              // Non-vacuity: every one of the five limits was breached somewhere, and some document
              // under a bound was accepted — a comparison that only ever refused measured no bound.
              let breached =
                  pools
                  |> List.choose (fun (_, r, input) ->
                      match Json.read { ReadOptions.strict with Limits = r } input with
                      | Error(JsonRefusal.LimitExceeded b) -> Some b.Limit
                      | _ -> None)
                  |> Set.ofList

              for l in
                  [ JsonLimit.Depth
                    JsonLimit.StringLength
                    JsonLimit.ArrayLength
                    JsonLimit.ObjectMembers
                    JsonLimit.DocumentBytes ] do
                  Expect.isTrue (breached.Contains l) (sprintf "the %s bound was breached somewhere" (JsonLimit.name l))

              Expect.isTrue
                  (pools
                   |> List.exists (fun (_, r, input) ->
                       (JsonParseDiff.prodAnswerLimited RejectNull r input).StartsWith "ok "))
                  "some document under a bound was ACCEPTED"

              // The tolerant policy under a members bound: an erased member null is not a member,
              // on both sides.
              let erased = "{\"a\":null,\"b\":1}"
              let one = { wide with MaxObjectMembers = 1 }

              Expect.equal
                  (JsonParseDiff.prodAnswerLimited EraseMemberNull one erased)
                  (JsonParseDiff.modelAnswerLimited JsonParseDiff.toChs EraseMemberNull one true erased)
                  "tolerant, one member admitted"

              Expect.stringStarts
                  (JsonParseDiff.prodAnswerLimited EraseMemberNull one erased)
                  "ok "
                  "the erased member was not counted"

          testCase "a model whose ITEMS CHECK is out of step DISAGREES with production"
          <| fun _ ->
              // The go-red for the limits comparison: the model is handed a wider items bound than
              // production enforces, accepts what production refuses by name, and the comparison
              // above is thereby known to be one that can fail on exactly this bound.
              let wide =
                  { MaxDepth = 512
                    MaxStringLength = 1 <<< 20
                    MaxArrayLength = 2
                    MaxObjectMembers = 100000
                    MaxDocumentBytes = 1 <<< 25 }

              let p = JsonParseDiff.prodAnswerLimited RejectNull wide "[1,2,3]"

              let m =
                  JsonParseDiff.modelAnswerLimited
                      JsonParseDiff.toChs
                      RejectNull
                      { wide with MaxArrayLength = 8 }
                      true
                      "[1,2,3]"

              Expect.stringStarts p "limit arrayLength" "production refuses at the bound it was given"
              Expect.stringStarts m "ok " "and an over-budgeted model does not"

              Tally.empty []
              |> Differential.compare Differential.text "[1,2,3] under an items bound of 2" p m
              |> Differential.expectLoses "so the items comparison can lose"

          // ---- the teeth: both comparisons can lose ----

          testCase "a model whose DEPTH CHECK is out of step DISAGREES with production"
          <| fun _ ->
              // The go-red the phase asks for: the model keeps a cap production has already spent.
              // Production refuses the input by name; the model, still holding budget, accepts it —
              // so a green run above is known to be a comparison that can fail on exactly the
              // guard this phase is about.
              let input = JsonParseDiff.nestArr 4
              let p = JsonParseDiff.prodAnswer RejectNull 3 input

              let m =
                  JsonParseDiff.modelAnswer JsonParseDiff.toChs RejectNull 3 (JsonParseDiff.budget 8) input

              Expect.stringStarts p "limit depth" "production refuses at the cap it was given"
              Expect.stringStarts m "ok " "and an over-budgeted model does not"
              Expect.notEqual p m "so the depth comparison can lose"

              // … and it loses on the generated pool too, not only on one hand-made input.
              let overBudgeted =
                  Differential.pair
                      (JsonParseDiff.prodAnswer RejectNull 3)
                      (JsonParseDiff.modelAnswer JsonParseDiff.toChs RejectNull 3 (JsonParseDiff.budget 8))

              Tally.empty []
              |> Differential.over [ 0..8 ] (fun k t ->
                  let i = JsonParseDiff.nestArr k
                  t |> Differential.compare overBudgeted (sprintf "k=%d" k) i i)
              |> Differential.expectLoses "the over-budgeted model, somewhere in the family"

          // Phase 519: the differential's production subject moved to `Json.read` and a breach
          // renders through the lift, so the go-red is put to a PLANTED PRODUCTION defect too — a
          // production depth guard one level loose (the cap production is handed is one past the
          // model's) must disagree, on the hand-made input and over the family.
          testCase "a PRODUCTION depth guard one level loose DISAGREES with the model"
          <| fun _ ->
              let loose (cap: int) (input: string) =
                  JsonParseDiff.prodAnswer RejectNull (cap + 1) input

              let model (cap: int) (input: string) =
                  JsonParseDiff.modelAnswer JsonParseDiff.toChs RejectNull cap (JsonParseDiff.budget cap) input

              let input = JsonParseDiff.nestArr 4
              Expect.stringStarts (loose 3 input) "ok " "the loose production accepts past the cap"
              Expect.equal (model 3 input) (JsonParseDiff.depthLine 3) "the model refuses at it"

              let t =
                  Tally.empty []
                  |> Differential.over
                      [ for cap in 0..8 do
                            for k in 0..8 -> cap, k ]
                      (fun (cap, k) t ->
                          let i = JsonParseDiff.nestArr k

                          t
                          |> Differential.compareWith
                              (Differential.pair (loose cap) (model cap))
                              (fun _ _ -> sprintf "cap=%d k=%d" cap k)
                              i
                              i)

              Differential.expectLoses "the loose production depth guard" t

              // Exactly the inputs nested past each cap: one past it the loose guard accepts what the
              // model refuses, and further past it both refuse, a bracket apart. Every input within
              // a cap agrees, so the instrument is not red merely for being crude.
              Expect.equal
                  (List.sort (Tally.disagreements t))
                  (List.sort
                      [ for cap in 0..8 do
                            for k in cap + 1 .. 8 -> sprintf "cap=%d k=%d" cap k ])
                  "the loose guard is seen past every cap, and nowhere within one"

          testCase "a model handed a BLIND bridge DISAGREES with production"
          <| fun _ ->
              // The comparison's own go-red, and the parser family's counterpart to the decode
              // family's blind integer bridge: the two container characters are hidden, so every
              // structured document reaches the model as something production never saw.
              let structured = [ "{}"; "[]"; "{\"a\":1}"; "[1,2,3]"; "{\"a\":[1,{\"b\":2}]}" ]

              let blind =
                  Differential.pair
                      (JsonParseDiff.prodAnswer RejectNull 512)
                      (JsonParseDiff.modelAnswer JsonParseDiff.toChsBlind RejectNull 512 (JsonParseDiff.budget 512))

              let t =
                  Tally.empty []
                  |> Differential.over structured (fun input t -> t |> Differential.compare blind input input input)

              Differential.expectLoses "the blind bridge" t

              Expect.equal
                  (List.length (Tally.disagreements t))
                  (List.length structured)
                  "every structured document must disagree under the blind bridge"

              // And it stays narrow: a scalar is untouched by the blinding, so a green run
              // elsewhere is not green merely because this instrument is crude.
              Expect.equal (blind.Production "42") (blind.Model "42") "a scalar still agrees under the blind bridge" ]
