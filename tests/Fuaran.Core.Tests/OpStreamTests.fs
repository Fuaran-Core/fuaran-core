module Fuaran.Core.Tests.OpStreamTests

open Expecto
open Fuaran.Core

// A minimal stream domain: a counter with inc/dec ops, encoded via the Core.Wire
// helpers and decoded via the Core.Wire combinators (so the test exercises the real
// wire surface too). Dec below zero is a domain rejection.
type CounterOp =
    | Inc of int
    | Dec of int

let private encodeOp =
    function
    | Inc n -> Json.render (Json.kindObj "inc" [ "n", JInt n ])
    | Dec n -> Json.render (Json.kindObj "dec" [ "n", JInt n ])

let private decodeOp (s: string) : Result<CounterOp, string> =
    let parsed =
        Decode.parse s
        |> Result.bind (fun el ->
            Decode.kindOf el
            |> Result.bind (fun k -> Decode.intField "n" el |> Result.map (fun n -> k, n)))

    match parsed with
    | Ok("inc", n) -> Ok(Inc n)
    | Ok("dec", n) -> Ok(Dec n)
    | Ok(k, _) -> Error("unknown op kind: " + k)
    | Error e -> Error e

let private applyOp op (st: int) =
    match op with
    | Inc n -> Ok(st + n)
    | Dec n -> if st - n < 0 then Error "would go negative" else Ok(st - n)

let private sw: StreamWitness<CounterOp, int, string> =
    { Apply = applyOp
      Encode = encodeOp
      Decode = decodeOp }

// Phase 27 — value codecs for captured boundary values (clock ticks / RNG draws as int,
// network bodies as string), exercising the real Core.Wire encode/decode surface.
let private encInt (n: int) = Json.render (JInt n)

let private decInt (s: string) : Result<int, string> =
    Decode.parse s |> Result.bind Decode.asInt

let private encStr (s: string) = Json.render (JStr s)

let private decStr (s: string) : Result<string, string> =
    Decode.parse s |> Result.bind Decode.asString

let private build () =
    let step acc op =
        acc
        |> Result.bind (fun (st, recs) -> OpStream.append OpStream.defaultHash sw (Human "tester") op st recs)

    [ Inc 5; Inc 3; Dec 2 ] |> List.fold step (Ok(0, OpStream.empty))

// Phase 255 — a domain's *legacy* chain format (e.g. the Documents-style "%d|%s|%s" payload
// with a "genesis" sentinel), distinct from the canonical {seq,actor,op} + "" binding.
let private legacyCfg: StreamConfig =
    { Payload = fun seq actor opJson -> sprintf "%d|%s|%s" seq (Actor.id actor) opJson
      Genesis = "genesis" }

let private buildWith (cfg: StreamConfig) =
    let step acc op =
        acc
        |> Result.bind (fun (st, recs) -> OpStream.appendWith cfg OpStream.defaultHash sw (Human "tester") op st recs)

    [ Inc 5; Inc 3; Dec 2 ] |> List.fold step (Ok(0, OpStream.empty))

[<Tests>]
let tests =
    testList
        "OpStream"
        [ testCase "append threads state and chains records"
          <| fun _ ->
              match build () with
              | Ok(st, recs) ->
                  Expect.equal st 6 "5 + 3 - 2"
                  Expect.equal (List.length recs) 3 "three records"
                  Expect.equal recs.[0].PrevHash "" "genesis prev is empty"
                  Expect.equal recs.[1].PrevHash recs.[0].Hash "chain links"
              | Error e -> failtestf "unexpected %A" e

          testCase "append surfaces a domain rejection unchanged"
          <| fun _ ->
              match OpStream.append OpStream.defaultHash sw (Human "tester") (Dec 5) 3 OpStream.empty with
              | Error "would go negative" -> ()
              | other -> failtestf "expected domain rejection, got %A" other

          testCase "verifyChain accepts an intact chain"
          <| fun _ ->
              match build () with
              | Ok(_, recs) -> Expect.isTrue (OpStream.verifyChain OpStream.defaultHash sw recs) "intact"
              | Error e -> failtestf "unexpected %A" e

          testCase "verifyChain rejects a tampered op"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  let tampered =
                      recs |> List.mapi (fun i r -> if i = 1 then { r with Op = Inc 99 } else r)

                  Expect.isFalse (OpStream.verifyChain OpStream.defaultHash sw tampered) "tamper detected"
              | Error e -> failtestf "unexpected %A" e

          testCase "replay re-derives the same state"
          <| fun _ ->
              match build () with
              | Ok(_, recs) -> Expect.equal (OpStream.replay sw 0 recs) (Ok 6) "replay = fold"
              | Error e -> failtestf "unexpected %A" e

          testCase "replayLenient skips rejecting ops and reports what it dropped"
          <| fun _ ->
              // Hand-built records (the hashes are irrelevant — replayLenient only folds Apply over
              // .Op). From state 0 the Dec 9 would go negative: fail-fast `replay` halts there;
              // fail-soft `replayLenient` skips it and the survivors (Inc 5, Inc 3) fold to 8.
              let rec_ seq op : OpRecord<CounterOp> =
                  { Seq = seq
                    Actor = Human "t"
                    Op = op
                    PrevHash = ""
                    Hash = "" }

              let recs = [ rec_ 0 (Inc 5); rec_ 1 (Dec 9); rec_ 2 (Inc 3) ]
              let state, skipped = OpStream.replayLenient sw 0 recs
              Expect.equal state 8 "the surviving ops (Inc 5, Inc 3) fold to 8"
              Expect.equal (List.length skipped) 1 "one op was skipped"
              Expect.equal (fst skipped.[0]) 1 "the skipped op was at index 1 (the Dec 9)"

          testCase "toJsonl / fromJsonl round-trips the records"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  match OpStream.toJsonl sw recs |> OpStream.fromJsonl sw with
                  | Ok restored ->
                      Expect.equal restored recs "records survive the round-trip"

                      Expect.isTrue
                          (OpStream.verifyChain OpStream.defaultHash sw restored)
                          "restored chain still verifies"
                  | Error e -> failtestf "fromJsonl failed: %s" e
              | Error e -> failtestf "unexpected %A" e

          testCase "fromJsonl names a line whose op fails to decode (Phase 252)"
          <| fun _ ->
              // a syntactically-fine line whose op kind is unknown ⇒ the witness Decode Errors
              let bad =
                  "{\"seq\":0,\"actor\":\"x\",\"op\":{\"kind\":\"bogus\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"

              match OpStream.fromJsonl sw bad with
              | Error m -> Expect.stringContains m "line 0" "names the failing line"
              | Ok _ -> failtest "expected a decode Error"

          // ---- Phase 255: pluggable payload format + migration shim (finding F4) ----

          testCase "appendWith canonicalConfig is byte-identical to append (back-compat lock)"
          <| fun _ ->
              match build (), buildWith OpStream.canonicalConfig with
              | Ok(_, a), Ok(_, b) -> Expect.equal b a "the canonical config reproduces the default chain exactly"
              | _ -> failtest "build failed"

          testCase "a legacy-format chain verifies under its own config, not under the default"
          <| fun _ ->
              match buildWith legacyCfg with
              | Ok(_, recs) ->
                  Expect.isTrue
                      (OpStream.verifyChainWith legacyCfg OpStream.defaultHash sw recs)
                      "intact under legacyCfg"

                  Expect.isFalse
                      (OpStream.verifyChain OpStream.defaultHash sw recs)
                      "the default config (different payload + genesis) does not verify the legacy chain"
              | Error e -> failtestf "unexpected %A" e

          testCase "rehash migrates a legacy chain to a canonical chain that verifies under the default"
          <| fun _ ->
              match buildWith legacyCfg with
              | Ok(st, legacy) ->
                  match OpStream.rehash legacyCfg OpStream.canonicalConfig OpStream.defaultHash sw legacy with
                  | Ok canonical ->
                      Expect.isTrue
                          (OpStream.verifyChain OpStream.defaultHash sw canonical)
                          "the rehashed chain verifies under the default"

                      Expect.equal
                          (canonical |> List.map (fun r -> r.Op))
                          (legacy |> List.map (fun r -> r.Op))
                          "the ops are preserved — only the hash chain changed"

                      Expect.equal (OpStream.replay sw 0 canonical) (Ok st) "replay reproduces the same state"
                      Expect.equal canonical.[0].PrevHash "" "genesis is now the canonical empty sentinel"
                  | Error e -> failtestf "rehash failed: %s" e
              | Error e -> failtestf "unexpected %A" e

          testCase "rehash refuses a source chain that does not verify under fromCfg"
          <| fun _ ->
              match buildWith legacyCfg with
              | Ok(_, recs) ->
                  let tampered =
                      recs |> List.mapi (fun i r -> if i = 1 then { r with Op = Inc 99 } else r)

                  match OpStream.rehash legacyCfg OpStream.canonicalConfig OpStream.defaultHash sw tampered with
                  | Error m -> Expect.stringContains m "does not verify" "refuses to migrate a broken chain"
                  | Ok _ -> failtest "expected rehash to refuse a tampered source chain"
              | Error e -> failtestf "unexpected %A" e

          // Phase 21 — chain-break localisation.
          testCase "firstChainBreak is None for an intact chain"
          <| fun _ ->
              match build () with
              | Ok(_, recs) -> Expect.isNone (OpStream.firstChainBreak OpStream.defaultHash sw recs) "intact ⇒ no break"
              | Error e -> failtestf "unexpected %A" e

          testCase "firstChainBreak localises a tampered op to its record index + reason"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  let tampered =
                      recs |> List.mapi (fun i r -> if i = 1 then { r with Op = Inc 99 } else r)

                  match OpStream.firstChainBreak OpStream.defaultHash sw tampered with
                  | Some b ->
                      Expect.equal b.Index 1 "break at record 1"
                      Expect.stringContains b.Reason "hash" "names a hash mismatch"
                  | None -> failtest "expected a break"
              | Error e -> failtestf "unexpected %A" e

          testCase "firstChainBreak catches a broken prev-link"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  let broken =
                      recs
                      |> List.mapi (fun i r -> if i = 2 then { r with PrevHash = "WRONG" } else r)

                  match OpStream.firstChainBreak OpStream.defaultHash sw broken with
                  | Some b -> Expect.equal b.Index 2 "break at record 2 (prev-link or hash)"
                  | None -> failtest "expected a break"
              | Error e -> failtestf "unexpected %A" e

          testCase "fromJsonlVerified names the break index for a tampered stream"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  let tampered =
                      recs |> List.mapi (fun i r -> if i = 1 then { r with Op = Inc 99 } else r)

                  match OpStream.fromJsonlVerified OpStream.defaultHash sw (OpStream.toJsonl sw tampered) with
                  | Error m -> Expect.stringContains m "record 1" "the load error names where it broke"
                  | Ok _ -> failtest "expected the tampered stream to be refused"
              | Error e -> failtestf "unexpected %A" e

          // ---- Phase 27: determinism capture / replay ----
          // value codecs for the captured boundary values (via the real Core.Wire surface).

          testCase "a clock / random / network session replays exactly via captured values"
          <| fun _ ->
              let h = OpStream.defaultHash
              // a live world whose readings advance on every call — the non-determinism we pin down.
              let mutable clock = 1000

              let readClock () =
                  clock <- clock + 1
                  clock

              let mutable rngState = 7

              let drawRandom () =
                  rngState <- (rngState * 1103515245 + 12345) &&& 0x7fffffff
                  rngState

              let mutable hit = 0

              let fetch () =
                  hit <- hit + 1
                  sprintf "body-%d" hit

              // record: each non-deterministic reading is journalled.
              let t, c1 = OpStream.captureEffect h encInt "clock" "clock" readClock []
              let r, c2 = OpStream.captureEffect h encInt "random" "rng" drawRandom c1
              let body, caps = OpStream.captureEffect h encStr "network" "fetch" fetch c2
              Expect.equal (List.length caps) 3 "three non-deterministic captures journalled"

              // replay: the live world has since moved on, but the captured values must come back
              // unchanged (and a divergent live source must NOT be consulted).
              match
                  OpStream.replayEffect decInt "clock" "clock" readClock caps
                  |> Result.bind (fun (t', c) ->
                      OpStream.replayEffect decInt "rng" "random" drawRandom c
                      |> Result.map (fun (r', c') -> t', r', c'))
                  |> Result.bind (fun (t', r', c) ->
                      OpStream.replayEffect decStr "fetch" "network" fetch c
                      |> Result.map (fun (body', c') -> t', r', body', c'))
              with
              | Ok(t', r', body', rest) ->
                  Expect.equal t' t "clock replays to the recorded tick"
                  Expect.equal r' r "random replays to the recorded draw"
                  Expect.equal body' body "network replays to the recorded body"
                  Expect.isTrue (List.isEmpty rest) "journal fully consumed"
              | Error e -> failtestf "replay failed: %s" e

          testCase "a deterministic effect emits no capture and replay re-evaluates live"
          <| fun _ ->
              let h = OpStream.defaultHash

              let v, caps =
                  OpStream.captureEffect h encInt OpStream.deterministicTag "pure" (fun () -> 42) []

              Expect.equal v 42 "value flows through"
              Expect.isTrue (List.isEmpty caps) "deterministic ⇒ nothing captured"

              match OpStream.replayEffect decInt "pure" OpStream.deterministicTag (fun () -> 42) caps with
              | Ok(v', caps') ->
                  Expect.equal v' 42 "deterministic replay re-evaluates the live source"
                  Expect.isTrue (List.isEmpty caps') "journal untouched"
              | Error e -> failtestf "%s" e

          testCase "replay falls back to live evaluation when the journal is exhausted"
          <| fun _ ->
              match OpStream.replayEffect decInt "clock" "clock" (fun () -> 99) [] with
              | Ok(v, _) -> Expect.equal v 99 "absent a capture, replay reads the live source"
              | Error e -> failtestf "%s" e

          // ---- Phase 40: replayEffect effect-identity guard ----

          testCase "replayEffect rejects a head capture whose identity differs from the request"
          <| fun _ ->
              let h = OpStream.defaultHash
              let _, c1 = OpStream.captureEffect h encInt "clock" "alpha" (fun () -> 1) []
              let _, caps = OpStream.captureEffect h encInt "random" "beta" (fun () -> 2) c1

              // in record order alpha then beta both replay
              match OpStream.replayEffect decInt "alpha" "clock" (fun () -> 0) caps with
              | Ok(1, rest) ->
                  match OpStream.replayEffect decInt "beta" "random" (fun () -> 0) rest with
                  | Ok(2, []) -> ()
                  | other -> failtestf "beta replay: %A" other
              | other -> failtestf "alpha replay: %A" other

              // requesting beta while the head is alpha is a named error, not a wrong value
              match OpStream.replayEffect decInt "beta" "random" (fun () -> 0) caps with
              | Error msg -> Expect.stringContains msg "identity mismatch" "names the identity mismatch"
              | Ok(v, _) -> failtestf "expected a mismatch error, got value %d" v

          testCase "capturedSeed surfaces the recorded value for an effect identity"
          <| fun _ ->
              let h = OpStream.defaultHash

              let _, caps =
                  OpStream.captureEffect h encInt "random" "rng-seed" (fun () -> 4242) []

              Expect.equal (OpStream.capturedSeed "rng-seed" caps) (Some(encInt 4242)) "seed surfaced for reseeding"
              Expect.equal (OpStream.capturedSeed "absent" caps) None "no capture ⇒ None"

          testCase "verifyCaptures accepts an intact journal and rejects a tampered capture"
          <| fun _ ->
              let h = OpStream.defaultHash
              let _, c1 = OpStream.captureEffect h encInt "clock" "clock" (fun () -> 5) []
              let _, caps = OpStream.captureEffect h encInt "random" "rng" (fun () -> 9) c1
              Expect.isTrue (OpStream.verifyCaptures h caps) "intact journal verifies"

              let tampered =
                  caps
                  |> List.mapi (fun i c -> if i = 0 then { c with Value = encInt 999 } else c)

              Expect.isFalse (OpStream.verifyCaptures h tampered) "tampered capture detected"

              match OpStream.firstCaptureBreak h tampered with
              | Some b ->
                  Expect.equal b.Index 0 "break localised to capture 0"
                  Expect.stringContains b.Reason "hash" "names a hash mismatch"
              | None -> failtest "expected a capture break"

          testCase "capture journal round-trips through JSONL and still verifies"
          <| fun _ ->
              let h = OpStream.defaultHash
              let _, c1 = OpStream.captureEffect h encInt "clock" "clock" (fun () -> 5) []

              let _, caps =
                  OpStream.captureEffect h encStr "network" "fetch" (fun () -> "hello \"world\"") c1

              match OpStream.captureToJsonl caps |> OpStream.captureFromJsonl with
              | Ok restored ->
                  Expect.equal restored caps "captures survive the round-trip byte-for-byte"
                  Expect.isTrue (OpStream.verifyCaptures h restored) "restored journal still verifies"
              | Error e -> failtestf "captureFromJsonl: %s" e

          // ---- Phase 45: scanner bounds + duplicate-key consistency ----

          testCase "fromJsonl resolves a duplicate top-level key first-wins"
          <| fun _ ->
              // first-wins matches the JVal decoders (Decode.getProp), not the last-wins Map.ofList.
              // The actor is the typed object since Phase 320.
              let line =
                  """{"seq":0,"actor":{"kind":"human","id":"first"},"actor":{"kind":"human","id":"second"},"op":{"kind":"inc","n":5},"prevHash":"","hash":""}"""

              match OpStream.fromJsonl sw line with
              | Ok [ r ] -> Expect.equal r.Actor (Human "first") "the first occurrence of a duplicate key wins"
              | other -> failtestf "expected one record, got %A" other

          testCase "fromJsonl handles a truncated \\u escape gracefully (no opaque exception)"
          <| fun _ ->
              // a string field whose \u escape is truncated must not throw IndexOutOfRangeException —
              // the scanner degrades gracefully and returns a Result either way
              let line =
                  """{"seq":0,"actor":"a\u","op":{"kind":"inc","n":5},"prevHash":"","hash":""}"""

              match OpStream.fromJsonl sw line with
              | Ok _
              | Error _ -> () ]

// ---- Phase 81: attributed-stream lift ----

let private liftedSw = OpStream.Attributed.liftWitness sw

let private attr actor session turn at op : Attributed<CounterOp> =
    { Actor = actor
      Session = session
      Turn = turn
      At = at
      Op = op }

let private buildAttr (entries: Attributed<CounterOp> list) =
    let step acc a =
        acc
        |> Result.bind (fun (st, recs) -> OpStream.append OpStream.defaultHash liftedSw (Human "tester") a st recs)

    entries |> List.fold step (Ok(0, OpStream.empty))

[<Tests>]
let attributedTests =
    testList
        "OpStream.Attributed"
        [ testCase "the envelope encodes to the exact camelCase golden bytes (attributed corpus fixture)"
          <| fun _ ->
              let enc = OpStream.Attributed.encodeEnvelope encodeOp

              Expect.equal
                  (enc (attr "alice" "s1" (Some 2) "2026-01-01" (Inc 5)))
                  """{"actor":"alice","session":"s1","turn":2,"at":"2026-01-01","op":{"kind":"inc","n":5}}"""
                  "attributed envelope golden (turn present)"

              Expect.equal
                  (enc (attr "bob" "s2" None "" (Dec 3)))
                  """{"actor":"bob","session":"s2","turn":null,"at":"","op":{"kind":"dec","n":3}}"""
                  "attributed envelope golden (turn absent ⇒ null, unstamped ⇒ empty)"

          testCase "the envelope embeds the inner op bytes verbatim (unattributed encoding unchanged — additive)"
          <| fun _ ->
              // the op sub-span inside the envelope is byte-identical to the bare witness encoding, so a
              // lift changes nothing about how the inner (unattributed) op serialises — the #77 additive proof.
              let enc =
                  OpStream.Attributed.encodeEnvelope encodeOp (attr "a" "s" (Some 1) "t" (Inc 7))

              Expect.stringContains enc (encodeOp (Inc 7)) "the inner op is embedded verbatim"

          testCase "encodeEnvelope / decodeEnvelope round-trip (both turn variants)"
          <| fun _ ->
              let enc = OpStream.Attributed.encodeEnvelope encodeOp
              let dec = OpStream.Attributed.decodeEnvelope decodeOp

              for a in [ attr "alice" "s1" (Some 2) "at-1" (Inc 5); attr "bob" "s2" None "" (Dec 3) ] do
                  match dec (enc a) with
                  | Ok a' -> Expect.equal a' a "envelope round-trips"
                  | Error e -> failtestf "decodeEnvelope failed: %s" e

          testCase "a lifted stream replays identically to its inner ops"
          <| fun _ ->
              match
                  buildAttr
                      [ attr "a" "s" (Some 1) "t0" (Inc 5)
                        attr "b" "s" (Some 2) "t1" (Inc 3)
                        attr "a" "s" None "t2" (Dec 2) ]
              with
              | Ok(st, recs) ->
                  Expect.equal st 6 "5 + 3 - 2 (attribution is provenance, never state)"
                  Expect.equal (OpStream.replay liftedSw 0 recs) (Ok 6) "replay = inner fold"
              | Error e -> failtestf "unexpected %A" e

          testCase "the hash chain covers attribution — re-attributing a chained op breaks verifyChain"
          <| fun _ ->
              match buildAttr [ attr "alice" "s" (Some 1) "t0" (Inc 5); attr "bob" "s" (Some 2) "t1" (Inc 3) ] with
              | Ok(_, recs) ->
                  Expect.isTrue (OpStream.verifyChain OpStream.defaultHash liftedSw recs) "intact"

                  let reattributed =
                      recs
                      |> List.mapi (fun i r ->
                          if i = 0 then
                              { r with
                                  Op = { r.Op with Actor = "mallory" } }
                          else
                              r)

                  Expect.isFalse
                      (OpStream.verifyChain OpStream.defaultHash liftedSw reattributed)
                      "re-attribution detected (attribution is inside the hashed op)"
              | Error e -> failtestf "unexpected %A" e

          testCase "an attributed chain round-trips through JSONL and still verifies"
          <| fun _ ->
              match buildAttr [ attr "a" "s1" (Some 1) "t0" (Inc 5); attr "b" "s2" None "t1" (Dec 2) ] with
              | Ok(_, recs) ->
                  match OpStream.toJsonl liftedSw recs |> OpStream.fromJsonl liftedSw with
                  | Ok restored ->
                      Expect.equal restored recs "attributed records survive the round-trip byte-for-byte"

                      Expect.isTrue
                          (OpStream.verifyChain OpStream.defaultHash liftedSw restored)
                          "restored chain verifies"
                  | Error e -> failtestf "fromJsonl failed: %s" e
              | Error e -> failtestf "unexpected %A" e

          testCase "byActor / bySession project the stream in append order"
          <| fun _ ->
              match
                  buildAttr
                      [ attr "alice" "s1" None "t0" (Inc 5)
                        attr "bob" "s1" None "t1" (Inc 1)
                        attr "alice" "s2" None "t2" (Inc 2) ]
              with
              | Ok(_, recs) ->
                  let byA = OpStream.Attributed.byActor recs
                  Expect.equal (Map.count byA) 2 "two actors"
                  Expect.equal (byA.["alice"] |> List.map (fun r -> r.Seq)) [ 0; 2 ] "alice's records in stream order"
                  Expect.equal (byA.["bob"] |> List.map (fun r -> r.Seq)) [ 1 ] "bob's one record"

                  let byS = OpStream.Attributed.bySession recs
                  Expect.equal (byS.["s1"] |> List.map (fun r -> r.Seq)) [ 0; 1 ] "session s1 groups two"
                  Expect.equal (byS.["s2"] |> List.map (fun r -> r.Seq)) [ 2 ] "session s2 one"
              | Error e -> failtestf "unexpected %A" e

          // Phase 79 — compare-and-append (optimistic concurrency).
          testCase "appendIf with the current head appends chain-identically to append"
          <| fun _ ->
              match build () with
              | Ok(st, recs) ->
                  let h = OpStream.head recs

                  let viaAppend =
                      OpStream.append OpStream.defaultHash sw (Human "tester") (Inc 4) st recs

                  let viaCas =
                      OpStream.appendIf OpStream.defaultHash sw h (Human "tester") (Inc 4) st recs

                  match viaAppend, viaCas with
                  | Ok a, Ok b -> Expect.equal b a "CAS with the true head is identical to append"
                  | _ -> failtestf "expected both to append (got %A / %A)" viaAppend viaCas
              | Error e -> failtestf "unexpected %A" e

          testCase "appendIf with a stale head is rejected naming both heads (no mutation)"
          <| fun _ ->
              match build () with
              | Ok(st, recs) ->
                  let actual = OpStream.head recs

                  match OpStream.appendIf OpStream.defaultHash sw "not-the-head" (Human "tester") (Inc 1) st recs with
                  | Error(AppendRejection.StaleHead(expected, got)) ->
                      Expect.equal expected "not-the-head" "names the caller's expected head"
                      Expect.equal got actual "names the stream's actual head"
                  | other -> failtestf "expected StaleHead, got %A" other
              | Error e -> failtestf "unexpected %A" e

          testCase "two racing appendIf calls off one base head admit exactly one winner"
          <| fun _ ->
              match build () with
              | Ok(st, recs) ->
                  let baseHead = OpStream.head recs
                  // Both writers captured baseHead; whoever commits first wins, the other goes stale.
                  match OpStream.appendIf OpStream.defaultHash sw baseHead (Human "a") (Inc 1) st recs with
                  | Ok(st1, recs1) ->
                      match OpStream.appendIf OpStream.defaultHash sw baseHead (Human "b") (Inc 2) st1 recs1 with
                      | Error(AppendRejection.StaleHead(_, actual)) ->
                          Expect.equal actual (OpStream.head recs1) "the losing writer is told the advanced head"
                      | other -> failtestf "expected the second writer to be stale, got %A" other
                  | other -> failtestf "expected the first writer to win, got %A" other
              | Error e -> failtestf "unexpected %A" e

          testCase "appendIf forwards a domain rejection once the head matches"
          <| fun _ ->
              // Empty stream: head is the genesis sentinel ""; Dec 5 from state 3 would go negative.
              match
                  OpStream.appendIf
                      OpStream.defaultHash
                      sw
                      (OpStream.head OpStream.empty)
                      (Human "t")
                      (Dec 5)
                      3
                      OpStream.empty
              with
              | Error(AppendRejection.Domain "would go negative") -> ()
              | other -> failtestf "expected a forwarded domain rejection, got %A" other ]
