module Fuaran.Core.Tests.OpStreamTests

open Expecto
open Fuaran.Core

// The counter stream domain — the one reference copy, `Reference.Counter` (Phase 296): inc/dec
// ops through the Core.Wire helpers and combinators, `Dec` below zero the domain's rejection.
open Fuaran.Core.Tests.Reference.Counter

let private encodeOp = encode
let private sw = witness

// Phase 27 — value codecs for captured boundary values (clock ticks / RNG draws as int,
// network bodies as string), exercising the real Core.Wire encode/decode surface.
let private encInt (n: int) = Json.render (JInt n)

let private decInt (s: string) : Result<int, string> =
    Json.parse s |> Result.bind (Decoder.describing Decoder.int)

let private encStr (s: string) = Json.render (JStr s)

let private decStr (s: string) : Result<string, string> =
    Json.parse s |> Result.bind (Decoder.describing Decoder.str)

let private build () =
    let step acc op =
        acc
        |> Result.bind (fun (st, recs) ->
            OpStream.append (OpStream.canonical OpStream.defaultHash sw) (Human "tester") op st recs)

    [ Inc 5; Inc 3; Dec 2 ] |> List.fold step (Ok(0, OpStream.empty))

// Phase 255 — a domain's *legacy* chain format (e.g. the Documents-style "%d|%s|%s" payload
// with a "genesis" sentinel), distinct from the canonical {seq,actor,op} + "" binding.
let private legacyCfg: StreamConfig =
    { Payload = fun seq actor opJson -> sprintf "%d|%s|%s" seq (Actor.id actor) opJson
      Genesis = "genesis"
      Quote = StreamConfig.canonical.Quote }

let private buildWith (cfg: StreamConfig) =
    let step acc op =
        acc
        |> Result.bind (fun (st, recs) ->
            OpStream.append (OpStream.ofConfig cfg OpStream.defaultHash sw) (Human "tester") op st recs)

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
                  Expect.equal recs[0].PrevHash "" "genesis prev is empty"
                  Expect.equal recs[1].PrevHash recs[0].Hash "chain links"
              | Error e -> failtestf "unexpected %A" e

          testCase "append surfaces a domain rejection unchanged"
          <| fun _ ->
              match
                  OpStream.append (OpStream.canonical OpStream.defaultHash sw) (Human "tester") (Dec 5) 3 OpStream.empty
              with
              | Error "would go negative" -> ()
              | other -> failtestf "expected domain rejection, got %A" other

          testCase "verifyChain accepts an intact chain"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  Expect.isTrue (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash sw) recs) "intact"
              | Error e -> failtestf "unexpected %A" e

          testCase "verifyChain rejects a tampered op"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  let tampered =
                      recs |> List.mapi (fun i r -> if i = 1 then { r with Op = Inc 99 } else r)

                  Expect.isFalse
                      (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash sw) tampered)
                      "tamper detected"
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
              Expect.equal (fst skipped[0]) 1 "the skipped op was at index 1 (the Dec 9)"

          testCase "toJsonl / fromJsonl round-trips the records"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  match Jsonl.write sw recs |> Jsonl.read sw |> Result.mapError StreamLoadFault.toString with
                  | Ok restored ->
                      Expect.equal restored recs "records survive the round-trip"

                      Expect.isTrue
                          (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash sw) restored)
                          "restored chain still verifies"
                  | Error e -> failtestf "fromJsonl failed: %s" e
              | Error e -> failtestf "unexpected %A" e

          testCase "fromJsonl names a line whose op fails to decode (Phase 252)"
          <| fun _ ->
              // a syntactically-fine line whose op kind is unknown ⇒ the witness Decode Errors
              let bad =
                  "{\"seq\":0,\"actor\":{\"kind\":\"human\",\"id\":\"x\"},\"op\":{\"kind\":\"bogus\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"

              match Jsonl.read sw bad |> Result.mapError StreamLoadFault.toString with
              | Error m -> Expect.stringContains m "line 1" "names the failing line"
              | Ok _ -> failtest "expected a decode Error"

          // ---- Phase 255: pluggable payload format + migration shim (finding F4) ----

          testCase "appendWith canonicalConfig is byte-identical to append (back-compat lock)"
          <| fun _ ->
              match build (), buildWith StreamConfig.canonical with
              | Ok(_, a), Ok(_, b) -> Expect.equal b a "the canonical config reproduces the default chain exactly"
              | _ -> failtest "build failed"

          testCase "a legacy-format chain verifies under its own config, not under the default"
          <| fun _ ->
              match buildWith legacyCfg with
              | Ok(_, recs) ->
                  Expect.isTrue
                      (OpStream.verifyChain (OpStream.ofConfig legacyCfg OpStream.defaultHash sw) recs)
                      "intact under legacyCfg"

                  Expect.isFalse
                      (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash sw) recs)
                      "the default config (different payload + genesis) does not verify the legacy chain"
              | Error e -> failtestf "unexpected %A" e

          testCase "rehash migrates a legacy chain to a canonical chain that verifies under the default"
          <| fun _ ->
              match buildWith legacyCfg with
              | Ok(st, legacy) ->
                  match
                      OpStream.rehash
                          (OpStream.ofConfig legacyCfg OpStream.defaultHash sw)
                          (OpStream.canonical OpStream.defaultHash sw)
                          legacy
                  with
                  | Ok canonical ->
                      Expect.isTrue
                          (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash sw) canonical)
                          "the rehashed chain verifies under the default"

                      Expect.equal
                          (canonical |> List.map _.Op)
                          (legacy |> List.map _.Op)
                          "the ops are preserved — only the hash chain changed"

                      Expect.equal (OpStream.replay sw 0 canonical) (Ok st) "replay reproduces the same state"
                      Expect.equal canonical[0].PrevHash "" "genesis is now the canonical empty sentinel"
                  | Error e -> failtestf "rehash failed: %A" e
              | Error e -> failtestf "unexpected %A" e

          testCase "rehash refuses a source chain that does not verify under fromCfg"
          <| fun _ ->
              match buildWith legacyCfg with
              | Ok(_, recs) ->
                  let tampered =
                      recs |> List.mapi (fun i r -> if i = 1 then { r with Op = Inc 99 } else r)

                  match
                      OpStream.rehash
                          (OpStream.ofConfig legacyCfg OpStream.defaultHash sw)
                          (OpStream.canonical OpStream.defaultHash sw)
                          tampered
                  with
                  | Error b ->
                      Expect.equal
                          (b.Index, b.Reason)
                          (1, ChainBreakReason.HashMismatch)
                          "refuses to migrate a broken chain, naming its break"
                  | Ok _ -> failtest "expected rehash to refuse a tampered source chain"
              | Error e -> failtestf "unexpected %A" e

          // Phase 21 — chain-break localisation.
          testCase "firstChainBreak is None for an intact chain"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  Expect.isNone
                      (OpStream.firstChainBreak (OpStream.canonical OpStream.defaultHash sw) recs)
                      "intact ⇒ no break"
              | Error e -> failtestf "unexpected %A" e

          testCase "firstChainBreak localises a tampered op to its record index + reason"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  let tampered =
                      recs |> List.mapi (fun i r -> if i = 1 then { r with Op = Inc 99 } else r)

                  match OpStream.firstChainBreak (OpStream.canonical OpStream.defaultHash sw) tampered with
                  | Some b ->
                      Expect.equal b.Index 1 "break at record 1"
                      Expect.equal b.Reason ChainBreakReason.HashMismatch "names the digest check, typed (0.23.0)"
                  | None -> failtest "expected a break"
              | Error e -> failtestf "unexpected %A" e

          testCase "firstChainBreak catches a broken prev-link"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  let broken =
                      recs
                      |> List.mapi (fun i r -> if i = 2 then { r with PrevHash = "WRONG" } else r)

                  match OpStream.firstChainBreak (OpStream.canonical OpStream.defaultHash sw) broken with
                  | Some b -> Expect.equal b.Index 2 "break at record 2 (prev-link or hash)"
                  | None -> failtest "expected a break"
              | Error e -> failtestf "unexpected %A" e

          testCase "fromJsonlVerified names the break index for a tampered stream"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  let tampered =
                      recs |> List.mapi (fun i r -> if i = 1 then { r with Op = Inc 99 } else r)

                  match
                      Jsonl.readVerified (OpStream.canonical OpStream.defaultHash sw) (Jsonl.write sw tampered)
                      |> Result.mapError StreamLoadFault.toString
                  with
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
              let t, c1 =
                  EffectCapture.captureEffect StreamConfig.canonical h encInt "clock" "clock" readClock []

              let r, c2 =
                  EffectCapture.captureEffect StreamConfig.canonical h encInt "random" "rng" drawRandom c1

              let body, caps =
                  EffectCapture.captureEffect StreamConfig.canonical h encStr "network" "fetch" fetch c2

              Expect.equal (List.length caps) 3 "three non-deterministic captures journalled"

              // replay: the live world has since moved on, but the captured values must come back
              // unchanged (and a divergent live source must NOT be consulted).
              match
                  EffectCapture.replayEffect decInt "clock" "clock" readClock caps
                  |> Result.bind (fun (t', c) ->
                      EffectCapture.replayEffect decInt "rng" "random" drawRandom c
                      |> Result.map (fun (r', c') -> t', r', c'))
                  |> Result.bind (fun (t', r', c) ->
                      EffectCapture.replayEffect decStr "fetch" "network" fetch c
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
                  EffectCapture.captureEffect
                      StreamConfig.canonical
                      h
                      encInt
                      EffectCapture.deterministicTag
                      "pure"
                      (fun () -> 42)
                      []

              Expect.equal v 42 "value flows through"
              Expect.isTrue (List.isEmpty caps) "deterministic ⇒ nothing captured"

              match EffectCapture.replayEffect decInt "pure" EffectCapture.deterministicTag (fun () -> 42) caps with
              | Ok(v', caps') ->
                  Expect.equal v' 42 "deterministic replay re-evaluates the live source"
                  Expect.isTrue (List.isEmpty caps') "journal untouched"
              | Error e -> failtestf "%s" e

          testCase "replay falls back to live evaluation when the journal is exhausted"
          <| fun _ ->
              match EffectCapture.replayEffect decInt "clock" "clock" (fun () -> 99) [] with
              | Ok(v, _) -> Expect.equal v 99 "absent a capture, replay reads the live source"
              | Error e -> failtestf "%s" e

          // ---- Phase 40: replayEffect effect-identity guard ----

          testCase "replayEffect rejects a head capture whose identity differs from the request"
          <| fun _ ->
              let h = OpStream.defaultHash

              let _, c1 =
                  EffectCapture.captureEffect StreamConfig.canonical h encInt "clock" "alpha" (fun () -> 1) []

              let _, caps =
                  EffectCapture.captureEffect StreamConfig.canonical h encInt "random" "beta" (fun () -> 2) c1

              // in record order alpha then beta both replay
              match EffectCapture.replayEffect decInt "alpha" "clock" (fun () -> 0) caps with
              | Ok(1, rest) ->
                  match EffectCapture.replayEffect decInt "beta" "random" (fun () -> 0) rest with
                  | Ok(2, []) -> ()
                  | other -> failtestf "beta replay: %A" other
              | other -> failtestf "alpha replay: %A" other

              // requesting beta while the head is alpha is a named error, not a wrong value
              match EffectCapture.replayEffect decInt "beta" "random" (fun () -> 0) caps with
              | Error msg -> Expect.stringContains msg "identity mismatch" "names the identity mismatch"
              | Ok(v, _) -> failtestf "expected a mismatch error, got value %d" v

          testCase "capturedSeed surfaces the recorded value for an effect identity"
          <| fun _ ->
              let h = OpStream.defaultHash

              let _, caps =
                  EffectCapture.captureEffect StreamConfig.canonical h encInt "random" "rng-seed" (fun () -> 4242) []

              Expect.equal
                  (EffectCapture.capturedSeed "rng-seed" caps)
                  (Some(encInt 4242))
                  "seed surfaced for reseeding"

              Expect.equal (EffectCapture.capturedSeed "absent" caps) None "no capture ⇒ None"

          testCase "verifyCaptures accepts an intact journal and rejects a tampered capture"
          <| fun _ ->
              let h = OpStream.defaultHash

              let _, c1 =
                  EffectCapture.captureEffect StreamConfig.canonical h encInt "clock" "clock" (fun () -> 5) []

              let _, caps =
                  EffectCapture.captureEffect StreamConfig.canonical h encInt "random" "rng" (fun () -> 9) c1

              Expect.isTrue (EffectCapture.verifyCaptures StreamConfig.canonical h caps) "intact journal verifies"

              let tampered =
                  caps
                  |> List.mapi (fun i c -> if i = 0 then { c with Value = encInt 999 } else c)

              Expect.isFalse
                  (EffectCapture.verifyCaptures StreamConfig.canonical h tampered)
                  "tampered capture detected"

              match EffectCapture.firstCaptureBreak StreamConfig.canonical h tampered with
              | Some b ->
                  Expect.equal b.Index 0 "break localised to capture 0"
                  // 0.23.0 — the capture walk's own digest spelling collapses into the one typed
                  // `HashMismatch` case; which walker ran is carried by which function was called.
                  Expect.equal b.Reason ChainBreakReason.HashMismatch "names the digest check, typed (0.23.0)"
              | None -> failtest "expected a capture break"

          testCase "capture journal round-trips through JSONL and still verifies"
          <| fun _ ->
              let h = OpStream.defaultHash

              let _, c1 =
                  EffectCapture.captureEffect StreamConfig.canonical h encInt "clock" "clock" (fun () -> 5) []

              let _, caps =
                  EffectCapture.captureEffect
                      StreamConfig.canonical
                      h
                      encStr
                      "network"
                      "fetch"
                      (fun () -> "hello \"world\"")
                      c1

              match EffectCapture.toJsonl caps |> EffectCapture.ofJsonl with
              | Ok restored ->
                  Expect.equal restored caps "captures survive the round-trip byte-for-byte"

                  Expect.isTrue
                      (EffectCapture.verifyCaptures StreamConfig.canonical h restored)
                      "restored journal still verifies"
              | Error e -> failtestf "EffectCapture.ofJsonl: %s" (JsonlFault.toString e)

          // ---- Phase 45: scanner bounds + duplicate-key consistency ----

          testCase "fromJsonl refuses a duplicate top-level key at its second occurrence"
          <| fun _ ->
              // Phase 45 resolved a repeated key first-wins, with the JVal decoders; Phase 466 refuses
              // it, as the strict grammar does (D157), at the repeated key's opening quote.
              let line =
                  """{"seq":0,"actor":{"kind":"human","id":"first"},"actor":{"kind":"human","id":"second"},"op":{"kind":"inc","n":5},"prevHash":"","hash":""}"""

              match Jsonl.read sw line with
              | Error(StreamLoadFault.Unreadable(None, f)) ->
                  Expect.equal
                      f
                      { Line = 1
                        Position = line.IndexOf("\"actor\"", 20)
                        Reason = JsonlFaultReason.RepeatedKey "$[\"actor\"]" }
                      "the second actor is refused at its quote, by its path"
              | other -> failtestf "expected the repeated key's refusal, got %A" other

          testCase "fromJsonl handles a truncated \\u escape gracefully (no opaque exception)"
          <| fun _ ->
              // a string field whose \u escape is truncated must not throw IndexOutOfRangeException —
              // the scanner degrades gracefully and returns a Result either way
              let line =
                  """{"seq":0,"actor":"a\u","op":{"kind":"inc","n":5},"prevHash":"","hash":""}"""

              match Jsonl.read sw line |> Result.mapError StreamLoadFault.toString with
              | Ok _
              | Error _ -> ()

          // ---- Phase 260: an unknown actor kind refuses instead of reading as Human ----

          testCase "fromJsonl decodes both known actor kinds (Phase 260)"
          <| fun _ ->
              let line actor =
                  sprintf """{"seq":0,"actor":%s,"op":{"kind":"inc","n":5},"prevHash":"","hash":""}""" actor

              match
                  Jsonl.read sw (line (Actor.encode (Human "ann")))
                  |> Result.mapError StreamLoadFault.toString
              with
              | Ok [ r ] -> Expect.equal r.Actor (Human "ann") "a human actor decodes as Human"
              | other -> failtestf "expected one record, got %A" other

              match
                  Jsonl.read sw (line (Actor.encode (Agent("m", "v", "bot"))))
                  |> Result.mapError StreamLoadFault.toString
              with
              | Ok [ r ] -> Expect.equal r.Actor (Agent("m", "v", "bot")) "an agent actor decodes as Agent"
              | other -> failtestf "expected one record, got %A" other

          testCase "fromJsonl refuses an unknown actor kind, naming the kind and the line (Phase 260)"
          <| fun _ ->
              let line =
                  """{"seq":0,"actor":{"kind":"service","id":"svc-1"},"op":{"kind":"inc","n":5},"prevHash":"","hash":""}"""

              match Jsonl.read sw line |> Result.mapError StreamLoadFault.toString with
              | Error m ->
                  Expect.stringContains m "line 1" "names the failing line"
                  Expect.stringContains m "unknown actor kind \"service\"" "names the kind it does not know"
              | Ok recs -> failtestf "an unknown kind must not decode, got %A" recs

          testCase "fromJsonl refuses an actor with no kind rather than reading it as Human (Phase 260)"
          <| fun _ ->
              let line =
                  """{"seq":0,"actor":{"id":"ann"},"op":{"kind":"inc","n":5},"prevHash":"","hash":""}"""

              match Jsonl.read sw line |> Result.mapError StreamLoadFault.toString with
              | Error m -> Expect.stringContains m "no kind" "names the absence"
              | Ok recs -> failtestf "a kind-less actor must not decode, got %A" recs

          testCase "an unknown actor kind refuses every reader built on the scanner (Phase 260)"
          <| fun _ ->
              let line =
                  """{"seq":0,"actor":{"kind":"service","id":"svc-1"},"op":{"kind":"inc","n":5},"prevHash":"","hash":""}"""

              Expect.isError (Jsonl.readWithSnapshots sw line) "the snapshot-aware reader refuses"

              Expect.isError
                  (Jsonl.readVerified (OpStream.canonical OpStream.defaultHash sw) line)
                  "the verified reader refuses" ]

// ---- Phase 81: attributed-stream lift ----

let private liftedSw = Attributed.liftWitness sw

let private attr actor session turn at op : Attributed<CounterOp> =
    { Actor = actor
      Session = session
      Turn = turn
      At = at
      Op = op }

let private buildAttr (entries: Attributed<CounterOp> list) =
    let step acc a =
        acc
        |> Result.bind (fun (st, recs) ->
            OpStream.append (OpStream.canonical OpStream.defaultHash liftedSw) (Human "tester") a st recs)

    entries |> List.fold step (Ok(0, OpStream.empty))

[<Tests>]
let attributedTests =
    testList
        "OpStream.Attributed"
        [ testCase "the envelope encodes to the exact camelCase golden bytes (attributed corpus fixture)"
          <| fun _ ->
              let enc = Attributed.encodeEnvelope encodeOp

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
              let enc = Attributed.encodeEnvelope encodeOp (attr "a" "s" (Some 1) "t" (Inc 7))

              Expect.stringContains enc (encodeOp (Inc 7)) "the inner op is embedded verbatim"

          testCase "encodeEnvelope / decodeEnvelope round-trip (both turn variants)"
          <| fun _ ->
              let enc = Attributed.encodeEnvelope encodeOp
              let dec = Attributed.decodeEnvelope decode

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
                  Expect.isTrue (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash liftedSw) recs) "intact"

                  let reattributed =
                      recs
                      |> List.mapi (fun i r ->
                          if i = 0 then
                              { r with
                                  Op = { r.Op with Actor = "mallory" } }
                          else
                              r)

                  Expect.isFalse
                      (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash liftedSw) reattributed)
                      "re-attribution detected (attribution is inside the hashed op)"
              | Error e -> failtestf "unexpected %A" e

          testCase "an attributed chain round-trips through JSONL and still verifies"
          <| fun _ ->
              match buildAttr [ attr "a" "s1" (Some 1) "t0" (Inc 5); attr "b" "s2" None "t1" (Dec 2) ] with
              | Ok(_, recs) ->
                  match
                      Jsonl.write liftedSw recs
                      |> Jsonl.read liftedSw
                      |> Result.mapError StreamLoadFault.toString
                  with
                  | Ok restored ->
                      Expect.equal restored recs "attributed records survive the round-trip byte-for-byte"

                      Expect.isTrue
                          (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash liftedSw) restored)
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
                  let byA = Attributed.byActor recs
                  Expect.equal (Map.count byA) 2 "two actors"
                  Expect.equal (byA["alice"] |> List.map _.Seq) [ 0; 2 ] "alice's records in stream order"
                  Expect.equal (byA["bob"] |> List.map _.Seq) [ 1 ] "bob's one record"

                  let byS = Attributed.bySession recs
                  Expect.equal (byS["s1"] |> List.map _.Seq) [ 0; 1 ] "session s1 groups two"
                  Expect.equal (byS["s2"] |> List.map _.Seq) [ 2 ] "session s2 one"
              | Error e -> failtestf "unexpected %A" e

          // Phase 79 — compare-and-append (optimistic concurrency).
          testCase "appendIf with the current head appends chain-identically to append"
          <| fun _ ->
              match build () with
              | Ok(st, recs) ->
                  let h = OpStream.head StreamConfig.canonical recs

                  let viaAppend =
                      OpStream.append (OpStream.canonical OpStream.defaultHash sw) (Human "tester") (Inc 4) st recs

                  let viaCas =
                      OpStream.appendIf (OpStream.canonical OpStream.defaultHash sw) h (Human "tester") (Inc 4) st recs

                  match viaAppend, viaCas with
                  | Ok a, Ok b -> Expect.equal b a "CAS with the true head is identical to append"
                  | _ -> failtestf "expected both to append (got %A / %A)" viaAppend viaCas
              | Error e -> failtestf "unexpected %A" e

          testCase "appendIf with a stale head is rejected naming both heads (no mutation)"
          <| fun _ ->
              match build () with
              | Ok(st, recs) ->
                  let actual = OpStream.head StreamConfig.canonical recs

                  match
                      OpStream.appendIf
                          (OpStream.canonical OpStream.defaultHash sw)
                          "not-the-head"
                          (Human "tester")
                          (Inc 1)
                          st
                          recs
                  with
                  | Error(AppendRejection.StaleHead(expected, got)) ->
                      Expect.equal expected "not-the-head" "names the caller's expected head"
                      Expect.equal got actual "names the stream's actual head"
                  | other -> failtestf "expected StaleHead, got %A" other
              | Error e -> failtestf "unexpected %A" e

          testCase "two racing appendIf calls off one base head admit exactly one winner"
          <| fun _ ->
              match build () with
              | Ok(st, recs) ->
                  let baseHead = OpStream.head StreamConfig.canonical recs
                  // Both writers captured baseHead; whoever commits first wins, the other goes stale.
                  match
                      OpStream.appendIf
                          (OpStream.canonical OpStream.defaultHash sw)
                          baseHead
                          (Human "a")
                          (Inc 1)
                          st
                          recs
                  with
                  | Ok(st1, recs1) ->
                      match
                          OpStream.appendIf
                              (OpStream.canonical OpStream.defaultHash sw)
                              baseHead
                              (Human "b")
                              (Inc 2)
                              st1
                              recs1
                      with
                      | Error(AppendRejection.StaleHead(_, actual)) ->
                          Expect.equal
                              actual
                              (OpStream.head StreamConfig.canonical recs1)
                              "the losing writer is told the advanced head"
                      | other -> failtestf "expected the second writer to be stale, got %A" other
                  | other -> failtestf "expected the first writer to win, got %A" other
              | Error e -> failtestf "unexpected %A" e

          testCase "appendIf forwards a domain rejection once the head matches"
          <| fun _ ->
              // Empty stream: head is the genesis sentinel ""; Dec 5 from state 3 would go negative.
              match
                  OpStream.appendIf
                      (OpStream.canonical OpStream.defaultHash sw)
                      (OpStream.head StreamConfig.canonical OpStream.empty)
                      (Human "t")
                      (Dec 5)
                      3
                      OpStream.empty
              with
              | Error(AppendRejection.Domain "would go negative") -> ()
              | other -> failtestf "expected a forwarded domain rejection, got %A" other ]

[<Tests>]
let chainBreakReasonTests =
    testList
        "OpStream.ChainBreakReason"
        [ testCase "chainBreakReasonLaws certify the named-case discipline on both walkers (Phase 125)"
          <| fun _ ->
              let results = Conformance.chainBreakReasonLaws 5125 120
              Expect.equal (List.length results) 5 "five reason laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "chainBreakReasonLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.chainBreakReasonLaws 5125 120) results "same seed ⇒ identical report"

          // The go-red half. The family's first law is only worth its breaking change if it can
          // FAIL, and the only reason it never does is that the walkers stay inside the named
          // cases — so plant a break that came from outside them and check the classifier does not
          // launder it into whichever named case looks nearest. That laundering is exactly the
          // defect the pre-0.23.0 consumer-side form had.
          testCase "an unknown reason is Unrecognised verbatim, never the nearest named case"
          <| fun _ ->
              Expect.equal
                  (ChainBreakReason.ofString "hash mismatch, probably")
                  (ChainBreakReason.Unrecognised "hash mismatch, probably")
                  "a reason that merely LOOKS like a digest failure is not one"

              Expect.equal
                  (ChainBreakReason.ofString "hash mismatch (tampered capture)")
                  ChainBreakReason.HashMismatch
                  "the capture walk's own legacy spelling still classifies"

              Expect.equal
                  (ChainBreakReason.toString (ChainBreakReason.Unrecognised "verbatim"))
                  "verbatim"
                  "an unrecognised reason renders as itself, losing nothing"

          testCase "toString renders the pre-0.23.0 bytes, so a consumer that logged them still does"
          <| fun _ ->
              Expect.equal
                  (ChainBreakReason.toString ChainBreakReason.SequenceMismatch)
                  "sequence-number mismatch"
                  "the sequence spelling is unchanged"

              Expect.equal
                  (ChainBreakReason.toString ChainBreakReason.PrevHashLinkBroken)
                  "prev-hash link broken"
                  "the prev-link spelling is unchanged"

              Expect.equal
                  (ChainBreakReason.toString ChainBreakReason.HashMismatch)
                  "hash mismatch (tampered op/actor/seq)"
                  "the op-walk digest spelling is the one rendering for the single case" ]

// ---- Phase 296 — one scanner that refuses, the batch append, the snapshot family ----

/// A canonical record line with one member's raw value replaced — the shapes the lenient scanner
/// read as `Ok` with a misread value.
let private lineWith (field: string) (raw: string) =
    let members =
        [ "seq", "0"
          "actor", "{\"kind\":\"human\",\"id\":\"x\"}"
          "op", encodeOp (Inc 1)
          "prevHash", "\"\""
          "hash", "\"h\"" ]
        |> List.map (fun (k, v) -> if k = field then k, raw else k, v)

    "{"
    + (members |> List.map (fun (k, v) -> "\"" + k + "\":" + v) |> String.concat ",")
    + "}"

let private refusedAs (text: string) (lineNo: int) (expect: string) =
    match Jsonl.read sw text |> Result.mapError StreamLoadFault.toString with
    | Error m ->
        Expect.stringContains m (sprintf "line %d:" lineNo) "names the 1-based line"
        Expect.stringContains m expect "names the reason"
    | Ok recs -> failtestf "expected a refusal (%s), read %A" expect recs

let private enc (s: int) = string s

let private dec (s: string) =
    match System.Int32.TryParse s with
    | true, n -> Ok n
    | _ -> Error "not an int"

[<Tests>]
let scannerRefusalTests =
    testList
        "OpStream.Jsonl (Phase 296)"
        [ testCase "an unquoted value where a string is required is refused, never cut to its inner characters"
          <| fun _ ->
              refusedAs (lineWith "prevHash" "null") 1 "field prevHash is not a string"
              refusedAs (lineWith "hash" "12") 1 "field hash is not a string"

          testCase "an integer field is held to the JSON grammar"
          <| fun _ ->
              refusedAs (lineWith "seq" "0x2") 1 "invalid literal '0x2'"
              refusedAs (lineWith "seq" "\"0\"") 1 "field seq is not an integer"
              refusedAs (lineWith "seq" "1.0") 1 "field seq is not an integer"
              refusedAs (lineWith "seq" "01") 1 "invalid literal '01'"

          testCase "a non-hex \\u digit, an unknown escape and a lone surrogate are refused"
          <| fun _ ->
              refusedAs (lineWith "hash" "\"\\u00zz\"") 1 "invalid escape \\u00zz"
              refusedAs (lineWith "hash" "\"\\q\"") 1 "invalid escape \\q"
              refusedAs (lineWith "hash" "\"\\ud800x\"") 1 "invalid escape"

          testCase "a truncated line, a line ending after ':' and a non-object line are refused at a position"
          <| fun _ ->
              refusedAs "{\"seq\":" 1 "the line ends inside the object (position 7)"
              refusedAs "{\"seq\":0" 1 "the line ends inside the object"
              refusedAs "[1,2]" 1 "expected a JSON object (position 0)"
              refusedAs (lineWith "seq" "0" + " trailing") 1 "content after the closing '}'"

          testCase "the line number counts EVERY line, blank lines included"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  let good = Jsonl.write sw recs
                  refusedAs (good + "\n\n\n" + lineWith "prevHash" "null") 6 "field prevHash is not a string"
              | Error e -> failtestf "build failed: %A" e

          testCase "one snapshot line at the head is read; one anywhere else, or a second, is refused"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  match
                      Snapshots.compact SnapshotMode.Strict (OpStream.canonical OpStream.defaultHash sw) enc 0 recs 1
                  with
                  | Ok(snap, tail) ->
                      let snapLine = Snapshots.toJsonl enc snap
                      let body = Jsonl.write sw tail

                      match
                          Jsonl.readWithSnapshots sw (snapLine + "\n" + body)
                          |> Result.mapError StreamLoadFault.toString
                      with
                      | Ok(t, [ s ]) ->
                          Expect.equal s snapLine "the head snapshot is returned verbatim"
                          Expect.equal t tail "the tail records"
                      | other -> failtestf "expected one snapshot and the tail, got %A" other

                      refusedAs (body + "\n" + snapLine) 3 "a snapshot line that is not the first line"

                      refusedAs
                          (snapLine + "\n" + snapLine + "\n" + body)
                          2
                          "a snapshot line that is not the first line"
                  | Error e -> failtestf "compact failed: %A" e
              | Error e -> failtestf "build failed: %A" e

          testCase "a record line carrying a snapshot member is a record, not a dropped snapshot"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  let line = Jsonl.write sw [ List.head recs ]
                  let withMember = line.Substring(0, line.Length - 1) + ",\"snapshot\":true}"

                  match
                      Jsonl.readWithSnapshots sw withMember
                      |> Result.mapError StreamLoadFault.toString
                  with
                  | Ok([ r ], []) -> Expect.equal r (List.head recs) "read as the record it is"
                  | other -> failtestf "expected one record and no snapshot, got %A" other
              | Error e -> failtestf "build failed: %A" e

          testCase "the shared scanner is public: topFields keeps raw spans byte-for-byte, rawSpan reads one"
          <| fun _ ->
              let line = "{\"a\":{\"x\" : [1, 2]},\"b\":\"s\\n\"}"

              Expect.equal
                  (Jsonl.topFields line)
                  (Ok [ "a", "{\"x\" : [1, 2]}"; "b", "\"s\\n\"" ])
                  "raw spans, byte for byte"

              Expect.equal
                  (Jsonl.topFields "{\"a\":1,\"a\":3}")
                  (Error
                      { Line = 1
                        Position = 7
                        Reason = JsonlFaultReason.RepeatedKey "$[\"a\"]" })
                  "a repeated key is refused (Phase 466; until then the first won)"

              Expect.equal (Jsonl.rawSpan "b" line) (Ok(Some "\"s\\n\"")) "one member's raw span"
              Expect.equal (Jsonl.rawSpan "z" line) (Ok None) "an absent member"
              Expect.isError (Jsonl.rawSpan "a" "{\"a\":1") "a malformed line is refused whole"
              Expect.equal (Jsonl.unquote "\"s\\u0041\\n\"") (Ok "sA\n") "unquote decodes"
              Expect.isError (Jsonl.unquote "null") "unquote is total: null is not a string"

          testCase "captureFromJsonl and decodeEnvelope read through the same refusing scanner"
          <| fun _ ->
              Expect.isError
                  (EffectCapture.ofJsonl
                      "{\"capture\":true,\"seq\":0,\"eff\":\"clock\",\"det\":\"clock\",\"value\":1,\"prevHash\":null,\"hash\":\"h\"}")
                  "a capture whose prevHash is null"

              Expect.isError
                  (Attributed.decodeEnvelope
                      decode
                      "{\"actor\":\"a\",\"session\":\"s\",\"turn\":0x1,\"at\":\"t\",\"op\":{}}")
                  "an envelope whose turn is a hex spelling" ]

[<Tests>]
let batchAppendTests =
    testList
        "OpStream.appendMany (Phase 296)"
        [ testCase "appendMany chains the same records as a fold of append, from empty and onto a stream"
          <| fun _ ->
              let ops = [ Inc 5; Inc 3; Dec 2; Inc 1 ]

              let foldFrom (start: int * OpRecord<CounterOp> list) =
                  ops
                  |> List.fold
                      (fun acc op ->
                          acc
                          |> Result.bind (fun (st, recs) ->
                              OpStream.append (OpStream.canonical OpStream.defaultHash sw) (Human "tester") op st recs))
                      (Ok start)
                  |> Result.mapError (fun e -> 0, e)

              Expect.equal
                  (OpStream.appendMany
                      (OpStream.canonical OpStream.defaultHash sw)
                      (Human "tester")
                      ops
                      0
                      OpStream.empty)
                  (foldFrom (0, OpStream.empty))
                  "byte-identical to the fold"

              match
                  OpStream.append (OpStream.canonical OpStream.defaultHash sw) (Human "tester") (Inc 2) 0 OpStream.empty
              with
              | Ok(st, recs) ->
                  Expect.equal
                      (OpStream.appendMany (OpStream.canonical OpStream.defaultHash sw) (Human "tester") ops st recs)
                      (foldFrom (st, recs))
                      "onto a non-empty stream too"
              | Error e -> failtestf "append failed: %s" e

          testCase "appendMany is all or nothing: the first rejection is named by its index"
          <| fun _ ->
              Expect.equal
                  (OpStream.appendMany
                      (OpStream.canonical OpStream.defaultHash sw)
                      (Human "tester")
                      [ Inc 1; Dec 5; Inc 1 ]
                      0
                      OpStream.empty)
                  (Error(1, "would go negative"))
                  "the second op rejects; nothing chained"

          testCase "headWith and appendIfWith read the config's genesis on an empty stream"
          <| fun _ ->
              Expect.equal (OpStream.head legacyCfg OpStream.empty) "genesis" "an empty stream's head is the genesis"

              Expect.equal (OpStream.head StreamConfig.canonical OpStream.empty) "" "the canonical head is unchanged"

              match
                  OpStream.appendIf
                      (OpStream.ofConfig legacyCfg OpStream.defaultHash sw)
                      "genesis"
                      (Human "t")
                      (Inc 1)
                      0
                      OpStream.empty
              with
              | Ok(_, [ r ]) ->
                  Expect.equal r.PrevHash "genesis" "chained from the genesis"

                  Expect.isTrue
                      (OpStream.verifyChain (OpStream.ofConfig legacyCfg OpStream.defaultHash sw) [ r ])
                      "and verifies under it"
              | other -> failtestf "expected one record, got %A" other

              Expect.equal
                  (OpStream.appendIf
                      (OpStream.ofConfig legacyCfg OpStream.defaultHash sw)
                      ""
                      (Human "t")
                      (Inc 1)
                      0
                      OpStream.empty
                   |> Result.map ignore)
                  (Error(AppendRejection.StaleHead("", "genesis")))
                  "the canonical empty head is stale under another genesis"

          testCase "tryRehash keeps the typed break rehash used to discard"
          <| fun _ ->
              match buildWith legacyCfg with
              | Ok(_, recs) ->
                  let tampered =
                      recs |> List.mapi (fun i r -> if i = 1 then { r with Hash = "x" } else r)

                  match
                      OpStream.rehash
                          (OpStream.ofConfig legacyCfg OpStream.defaultHash sw)
                          (OpStream.canonical OpStream.defaultHash sw)
                          tampered
                  with
                  | Error b ->
                      Expect.equal b.Index 1 "the record that breaks"
                      Expect.equal b.Reason ChainBreakReason.HashMismatch "and why"
                  | Ok _ -> failtest "a broken source chain must not be rehashed"
              | Error e -> failtestf "build failed: %A" e ]

[<Tests>]
let snapshotFamilyTests =
    let compactAt mode recs atSeq =
        Snapshots.compact mode (OpStream.canonical OpStream.defaultHash sw) enc 0 recs atSeq

    testList
        "OpStream.Snapshots (Phase 296)"
        [ testCase "the mode is carried on the snapshot and through its line"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  for mode in [ SnapshotMode.Strict; SnapshotMode.ChainOnly ] do
                      match compactAt mode recs 2 with
                      | Ok(snap, tail) ->
                          Expect.equal snap.Mode mode "taken in the mode asked for"

                          Expect.isTrue
                              (Snapshots.verify (OpStream.canonical OpStream.defaultHash sw) enc snap tail)
                              "verifies under its own mode"

                          Expect.equal
                              (Snapshots.ofJsonl dec (Snapshots.toJsonl enc snap))
                              (Ok snap)
                              "the line round-trips the snapshot, mode included"
                      | Error e -> failtestf "compact failed: %A" e
              | Error e -> failtestf "build failed: %A" e

          testCase "firstBreak localises: the snapshot's own hash, or the tail record that breaks"
          <| fun _ ->
              let cfg = StreamConfig.canonical

              match
                  build ()
                  |> Result.map snd
                  |> Result.bind (fun recs -> compactAt SnapshotMode.Strict recs 1 |> Result.mapError string)
              with
              | Ok(snap, tail) ->
                  Expect.isNone
                      (Snapshots.firstBreak (OpStream.ofConfig cfg OpStream.defaultHash sw) enc snap tail)
                      "intact"

                  match
                      Snapshots.firstBreak
                          (OpStream.ofConfig cfg OpStream.defaultHash sw)
                          enc
                          { snap with State = 99 }
                          tail
                  with
                  | Some(SnapshotBreak.SnapshotHash _) -> ()
                  | other -> failtestf "a swapped state must break the snapshot's hash, got %A" other

                  let bent = tail |> List.mapi (fun i r -> if i = 1 then { r with Hash = "x" } else r)

                  match Snapshots.firstBreak (OpStream.ofConfig cfg OpStream.defaultHash sw) enc snap bent with
                  | Some(SnapshotBreak.Tail b) -> Expect.equal b.Index 1 "the tail position that breaks"
                  | other -> failtestf "expected a tail break, got %A" other
              | Error e -> failtestf "setup failed: %s" e

          testCase "replayFrom refuses a tail that does not start at the snapshot's boundary"
          <| fun _ ->
              match build () with
              | Ok(st, recs) ->
                  match compactAt SnapshotMode.ChainOnly recs 1 with
                  | Ok(snap, tail) ->
                      Expect.equal (Snapshots.replayFrom sw snap tail) (Ok st) "the seam holds"

                      Expect.equal
                          (Snapshots.replayFrom sw snap (List.tail tail))
                          (Error(SnapshotFault.TailSeqMismatch(1, 2)))
                          "a tail that skips a record"
                  | Error e -> failtestf "compact failed: %A" e
              | Error e -> failtestf "build failed: %A" e

          testCase "take keeps the prefix rejection the string forms discarded"
          <| fun _ ->
              let bad =
                  [ { Seq = 0
                      Actor = Human "t"
                      Op = Dec 3
                      PrevHash = ""
                      Hash = "h" } ]

              let take atSeq =
                  Snapshots.take SnapshotMode.Strict (OpStream.canonical OpStream.defaultHash sw) enc 0 bad atSeq

              Expect.equal
                  (take 1)
                  (Error(SnapshotFault.PrefixRejected(0, "would go negative")))
                  "the index and the domain's own rejection"

              Expect.equal (take 2) (Error(SnapshotFault.SeqOutOfRange(2, 1))) "a boundary past the end" ]

// ---- Phase 301: the compacted stream lives on, the checked writers, strict effect replay ----

let private keyOf (op: CounterOp) : string option = Some(encodeOp op)

let private fullStream (ops: CounterOp list) =
    (Ok(0, OpStream.empty), ops)
    ||> List.fold (fun acc op ->
        acc
        |> Result.bind (fun (st, recs) ->
            OpStream.append (OpStream.canonical OpStream.defaultHash sw) (Human "tester") op st recs))

let private compactedAt mode recs n =
    Compacted.compactAt mode (OpStream.canonical OpStream.defaultHash sw) enc keyOf 0 recs n

/// A witness whose op IS its encoding, so a test can hand the writers any span it likes.
let private rawWitness: StreamWitness<string, int, string> =
    { Apply = fun _ s -> Ok(s + 1)
      Encode = id
      Decode = Ok }

let private rawChain (encodings: string list) =
    (OpStream.empty, encodings)
    ||> List.fold (fun recs e ->
        OpStream.appendChainOnly (OpStream.canonical OpStream.defaultHash rawWitness) (Human "t") e recs)

[<Tests>]
let compactedTests =
    testList
        "OpStream.Compacted (Phase 301)"
        [ testCase "compact at any boundary, append, re-compact, and a pre-compaction key retries as Duplicate"
          <| fun _ ->
              let ops = [ Inc 5; Inc 3; Dec 2; Inc 1 ]

              match fullStream ops, fullStream (ops @ [ Inc 7 ]) with
              | Ok(st, recs), Ok(st', recs') ->
                  for n in 0 .. List.length recs do
                      match compactedAt SnapshotMode.Strict recs n with
                      | Error e -> failtestf "compact at %d failed: %A" n e
                      | Ok c ->
                          Expect.equal
                              (Compacted.keyIndex keyOf c)
                              (KeyIndex.ofStream encodeOp recs)
                              (sprintf "at %d the key index is the whole stream's" n)

                          Expect.equal
                              (Compacted.head c)
                              (OpStream.head StreamConfig.canonical recs)
                              (sprintf "at %d the head is the full stream's" n)

                          match
                              Compacted.appendTo
                                  (OpStream.canonical OpStream.defaultHash sw)
                                  (Human "tester")
                                  (Inc 7)
                                  st
                                  c
                          with
                          | Error e -> failtestf "appendTo at %d failed: %A" n e
                          | Ok(stA, cA) ->
                              Expect.equal stA st' "the state the full append reaches"
                              Expect.equal cA.Tail (List.skip n recs') "the record the full append mints"

                              Expect.isTrue
                                  (Compacted.verify (OpStream.canonical OpStream.defaultHash sw) enc cA)
                                  (sprintf "at %d the appended compacted stream verifies across" n)

                              for m in n .. List.length recs' do
                                  Expect.equal
                                      (Compacted.compactFrom (OpStream.canonical OpStream.defaultHash sw) enc keyOf cA m)
                                      (compactedAt SnapshotMode.Strict recs' m)
                                      (sprintf "re-compacting %d at %d is compacting the full stream at %d" n m m)

                              let index = Compacted.keyIndex keyOf cA

                              for r in List.truncate n recs do
                                  match
                                      Compacted.appendIdempotentTo
                                          (OpStream.canonical OpStream.defaultHash sw)
                                          (encodeOp r.Op)
                                          (Human "tester")
                                          r.Op
                                          stA
                                          index
                                          cA
                                  with
                                  | Ok(CompactedOutcome.Duplicate e) ->
                                      Expect.equal e { Seq = r.Seq; Hash = r.Hash } "the entry the key produced"
                                  | other -> failtestf "a pre-compaction key at %d must be a Duplicate, got %A" n other
              | other -> failtestf "build failed: %A" other

          testCase "the defect this closes: the plain append onto a compacted tail does not verify across"
          <| fun _ ->
              match fullStream [ Inc 5; Inc 3; Dec 2; Inc 1 ] with
              | Ok(st, recs) ->
                  match compactedAt SnapshotMode.Strict recs 2 with
                  | Ok c ->
                      match
                          OpStream.append
                              (OpStream.canonical OpStream.defaultHash sw)
                              (Human "tester")
                              (Inc 7)
                              st
                              c.Tail
                      with
                      | Ok(_, tail') ->
                          Expect.equal (List.last tail').Seq 2 "numbered from the tail's length"

                          Expect.isFalse
                              (Snapshots.verify (OpStream.canonical OpStream.defaultHash sw) enc c.Snapshot tail')
                              "and no verifier accepts it"
                      | Error e -> failtestf "append failed: %A" e
                  | Error e -> failtestf "compact failed: %A" e
              | Error e -> failtestf "build failed: %A" e

          testCase "an empty tail links to the boundary: appendIfTo compares the boundary hash"
          <| fun _ ->
              match fullStream [ Inc 5; Inc 3 ] with
              | Ok(st, recs) ->
                  match compactedAt SnapshotMode.ChainOnly recs 2 with
                  | Ok c ->
                      Expect.isEmpty c.Tail "compacted at the end"

                      Expect.equal
                          (Compacted.head c)
                          (OpStream.head StreamConfig.canonical recs)
                          "the head is the last record's"

                      let cas expected =
                          Compacted.appendIfTo
                              (OpStream.canonical OpStream.defaultHash sw)
                              expected
                              (Human "tester")
                              (Inc 1)
                              st
                              c

                      match cas "" with
                      | Error(AppendRejection.StaleHead(e, a)) ->
                          Expect.equal
                              (e, a)
                              ("", OpStream.head StreamConfig.canonical recs)
                              "the genesis is not this stream's head"
                      | other -> failtestf "expected StaleHead, got %A" other

                      match cas (OpStream.head StreamConfig.canonical recs) with
                      | Ok(_, c') ->
                          let r = List.exactlyOne c'.Tail

                          Expect.equal
                              (r.Seq, r.PrevHash)
                              (2, OpStream.head StreamConfig.canonical recs)
                              "sequence and link from the boundary"
                      | Error e -> failtestf "the matching head must append: %A" e
                  | Error e -> failtestf "compact failed: %A" e
              | Error e -> failtestf "build failed: %A" e

          testCase "compactFrom refuses by name, in the origin's numbering"
          <| fun _ ->
              match fullStream [ Inc 5; Inc 3; Dec 2; Inc 1 ] with
              | Ok(_, recs) ->
                  match compactedAt SnapshotMode.ChainOnly recs 2 with
                  | Ok c ->
                      let from =
                          Compacted.compactFrom (OpStream.canonical OpStream.defaultHash sw) enc keyOf

                      Expect.equal (from c 1) (Error(SnapshotFault.SeqOutOfRange(1, 4))) "behind the boundary"
                      Expect.equal (from c 5) (Error(SnapshotFault.SeqOutOfRange(5, 4))) "past the end"

                      let starved =
                          { c with
                              Snapshot = { c.Snapshot with State = 0 } }

                      Expect.equal
                          (from starved 3)
                          (Error(SnapshotFault.PrefixRejected(2, "would go negative")))
                          "the ORIGIN index of the refused op"

                      Expect.equal
                          (from { c with Tail = List.tail c.Tail } 3)
                          (Error(SnapshotFault.TailSeqMismatch(2, 3)))
                          "a tail that does not start at the boundary"
                  | Error e -> failtestf "compact failed: %A" e
              | Error e -> failtestf "build failed: %A" e ]

[<Tests>]
let checkedWriterTests =
    let refusedBy (expected: JsonlWriteFaultReason) (raw: string) =
        Expect.equal (Jsonl.checkRaw raw) (Error expected) (sprintf "%A" raw)

    testList
        "OpStream checked JSONL writers (Phase 301)"
        [ testCase "checkRaw names each way an embedded span would read back changed"
          <| fun _ ->
              refusedBy (JsonlWriteFaultReason.MultiLine 1) "1\n2"
              refusedBy (JsonlWriteFaultReason.MultiLine 3) "\"a\"\r\n"
              refusedBy JsonlWriteFaultReason.NotTrimStable "5 "
              refusedBy JsonlWriteFaultReason.NotTrimStable " 5"
              refusedBy JsonlWriteFaultReason.NotTrimStable " "
              refusedBy (JsonlWriteFaultReason.Unreadable JsonlFaultReason.Truncated) ""
              refusedBy (JsonlWriteFaultReason.Unreadable JsonlFaultReason.TrailingContent) "5 6"
              refusedBy (JsonlWriteFaultReason.Unreadable(JsonlFaultReason.InvalidLiteral "abc")) "abc"

              for ok in [ "5"; "\"a b\""; "{\"a\":[1,2]}"; "\"a\u2028b\""; "null" ] do
                  Expect.equal (Jsonl.checkRaw ok) (Ok()) (sprintf "%A embeds faithfully" ok)

          testCase "the op writer refuses what the unchecked round trip breaks, and writes what it does not"
          <| fun _ ->
              for bad in [ "5 "; " 5"; "\"a\"\n" ] do
                  let recs = rawChain [ "1"; bad ]

                  let unchecked =
                      Jsonl.read rawWitness (Jsonl.write rawWitness recs)
                      |> Result.map (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash rawWitness))

                  Expect.notEqual unchecked (Ok true) (sprintf "the unchecked round trip of %A does not verify" bad)

                  match Jsonl.tryWrite rawWitness recs with
                  | Error f -> Expect.equal (f.Line, f.Member) (2, "op") "the record and member refused"
                  | Ok _ -> failtestf "%A must be refused" bad

              let good = rawChain [ "1"; "\"a\u2028b\""; "{\"k\":[true]}" ]

              match Jsonl.tryWrite rawWitness good with
              | Ok text ->
                  Expect.equal text (Jsonl.write rawWitness good) "byte for byte the unchecked writer's"

                  match Jsonl.read rawWitness text |> Result.mapError StreamLoadFault.toString with
                  | Ok back ->
                      Expect.equal back good "reads back to the same records"

                      Expect.isTrue
                          (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash rawWitness) back)
                          "and verifies"
                  | Error e -> failtestf "read-back failed: %s" e
              | Error f -> failtestf "refused a faithful chain: %s" (JsonlWriteFault.toString f)

          testCase "the capture writer refuses a value the round trip would trim"
          <| fun _ ->
              let _, caps =
                  EffectCapture.captureEffect
                      StreamConfig.canonical
                      OpStream.defaultHash
                      id
                      "clock"
                      "t"
                      (fun () -> "5 ")
                      []

              let unchecked =
                  EffectCapture.ofJsonl (EffectCapture.toJsonl caps)
                  |> Result.map (EffectCapture.verifyCaptures StreamConfig.canonical OpStream.defaultHash)

              Expect.equal unchecked (Ok false) "the trimmed value no longer verifies"

              Expect.equal
                  (EffectCapture.tryToJsonl caps
                   |> Result.mapError (fun f -> f.Line, f.Member, f.Reason))
                  (Error(1, "value", JsonlWriteFaultReason.NotTrimStable))
                  "refused at the write"

          testCase "the snapshot and DAG writers refuse the same spans"
          <| fun _ ->
              match fullStream [ Inc 5 ] with
              | Ok(_, recs) ->
                  match compactedAt SnapshotMode.Strict recs 1 with
                  | Ok c ->
                      Expect.equal
                          (Snapshots.tryToJsonl (fun (s: int) -> string s + "\n") c.Snapshot
                           |> Result.mapError (fun f -> f.Line, f.Member))
                          (Error(1, "state"))
                          "a state with a line break"

                      Expect.equal
                          (Snapshots.tryToJsonl enc c.Snapshot)
                          (Ok(Snapshots.toJsonl enc c.Snapshot))
                          "a faithful state writes the unchecked line"
                  | Error e -> failtestf "compact failed: %A" e
              | Error e -> failtestf "build failed: %A" e

              match Dag.append (OpStream.canonical OpStream.defaultHash rawWitness) (Human "t") " 5" "" Dag.empty with
              | Ok { Dag = dag } ->
                  Expect.equal
                      (Dag.tryToJsonl id dag |> Result.mapError (fun f -> f.Line, f.Member, f.Reason))
                      (Error(1, "op", JsonlWriteFaultReason.NotTrimStable))
                      "a node whose encoding the reader would trim"
              | Error e -> failtestf "dag append failed: %A" e

              match Dag.append (OpStream.canonical OpStream.defaultHash rawWitness) (Human "t") "5" "" Dag.empty with
              | Ok { Dag = dag } ->
                  Expect.equal (Dag.tryToJsonl id dag) (Ok(Dag.toJsonl id dag)) "a faithful DAG writes unchanged"
              | Error e -> failtestf "dag append failed: %A" e

          testCase
              "Phase 473: every checked writer refuses an unpaired surrogate in a string it quotes itself, by member"
          <| fun _ ->
              // The text the unchecked writer produces for the same value is refused by the strict
              // reader (Phase 466) — that is what makes the write-side refusal a go-red, not a choice.
              let lone = string (char 0xD83D)

              let readerRefuses (text: string) =
                  text.Split '\n'
                  |> Array.exists (fun line -> Result.isError (Jsonl.parseLine 1 line))

              let refused (what: string) (wanted: string) (checkedResult: Result<string, JsonlWriteFault>) unchecked =
                  match checkedResult with
                  | Error f -> Expect.equal (f.Line, f.Member) (1, wanted) (what + ": refused at the write, by member")
                  | Ok _ -> failtestf "%s: written" what

                  Expect.isTrue (readerRefuses unchecked) (what + ": the unchecked text is refused at the read")

              match rawChain [ "1" ] with
              | [ r ] ->
                  for wanted, bad in
                      [ "actor", { r with Actor = Human("a" + lone) }
                        "prevHash", { r with PrevHash = lone }
                        "hash", { r with Hash = lone + "h" } ] do
                      refused
                          ("record " + wanted)
                          wanted
                          (Jsonl.tryWrite rawWitness [ bad ])
                          (Jsonl.write rawWitness [ bad ])
              | other -> failtestf "expected one record, got %A" other

              let _, caps =
                  EffectCapture.captureEffect
                      StreamConfig.canonical
                      OpStream.defaultHash
                      id
                      "clock"
                      "t"
                      (fun () -> "5")
                      []

              match caps with
              | [ c ] ->
                  for wanted, bad in
                      [ "eff", { c with Eff = lone }
                        "det", { c with Determinism = lone }
                        "prevHash", { c with PrevHash = lone }
                        "hash", { c with Hash = lone } ] do
                      refused
                          ("capture " + wanted)
                          wanted
                          (EffectCapture.tryToJsonl [ bad ])
                          (EffectCapture.toJsonl [ bad ])
              | other -> failtestf "expected one capture, got %A" other

              match fullStream [ Inc 5 ] with
              | Ok(_, recs) ->
                  match compactedAt SnapshotMode.Strict recs 1 with
                  | Ok c ->
                      for wanted, bad in
                          [ "prevHash", { c.Snapshot with PrevHash = lone }
                            "hash", { c.Snapshot with Hash = lone } ] do
                          refused
                              ("snapshot " + wanted)
                              wanted
                              (Snapshots.tryToJsonl enc bad)
                              (Snapshots.toJsonl enc bad)
                  | Error e -> failtestf "compact failed: %A" e
              | Error e -> failtestf "build failed: %A" e

              match Dag.append (OpStream.canonical OpStream.defaultHash rawWitness) (Human "t") "5" "" Dag.empty with
              | Ok { Dag = dag } ->
                  let edit (f: DagNode<string> -> DagNode<string>) =
                      let nodes =
                          dag.Nodes
                          |> Map.toList
                          |> List.map (fun (_, n) -> let e = f n in e.Id, e)
                          |> Map.ofList

                      match Dag.ofNodes nodes with
                      | Ok d -> d
                      | Error e -> failwithf "ofNodes refused the edited map: %A" e

                  for wanted, bad in
                      [ "id", edit (fun n -> { n with Id = lone })
                        "parents", edit (fun n -> { n with Parents = [ lone ] })
                        "actor", edit (fun n -> { n with Actor = Human lone }) ] do
                      refused ("DAG " + wanted) wanted (Dag.tryToJsonl id bad) (Dag.toJsonl id bad)
              | Error e -> failtestf "dag append failed: %A" e

          testCase "streamLaws' JSONL cell is red for a domain whose encoding the reader would trim"
          <| fun _ ->
              let trailing: StreamWitness<int, int, string> =
                  { Apply = fun op s -> Ok(s + op)
                    Encode = fun op -> string op + " "
                    Decode = fun s -> Ok(int (s.Trim())) }

              let gen: StreamGen<int, int> = { State0 = 0; Op = fun rng -> 1, rng }

              let cell =
                  Conformance.streamLaws trailing gen OpStream.defaultHash 7 20
                  |> List.find (fun r -> r.Law.Contains "JSONL round trip")

              Expect.isFalse cell.Passed "the law refuses the domain's persistence" ]

[<Tests>]
let strictReplayTests =
    let record (values: (string * string * int) list) =
        (([]: EffectCapture list), values)
        ||> List.fold (fun caps (eff, det, v) ->
            snd (
                EffectCapture.captureEffect
                    StreamConfig.canonical
                    OpStream.defaultHash
                    encInt
                    det
                    eff
                    (fun () -> v)
                    caps
            ))

    testList
        "OpStream.replayEffectStrict (Phase 301)"
        [ testCase "over a complete journal strict replay is the lenient one"
          <| fun _ ->
              let caps = record [ "a", "clock", 1; "b", "random", 2 ]

              let strict =
                  EffectCapture.replayEffectStrict decInt "a" "clock" (fun () -> 99) caps
                  |> Result.bind (fun (x, rest) ->
                      EffectCapture.replayEffectStrict decInt "b" "random" (fun () -> 99) rest
                      |> Result.map (fun (y, rest') -> x, y, rest'))

              Expect.equal strict (Ok(1, 2, [])) "both recorded values, the journal consumed"

          testCase "a tail-truncated journal is Exhausted, never replayed live"
          <| fun _ ->
              let caps = record [ "a", "clock", 1; "a", "clock", 2 ]
              let truncated = List.truncate 1 caps

              Expect.isTrue
                  (EffectCapture.verifyCaptures StreamConfig.canonical OpStream.defaultHash truncated)
                  "the cut journal still verifies"

              match EffectCapture.replayEffect decInt "a" "clock" (fun () -> 99) [] with
              | Ok(v, _) -> Expect.equal v 99 "the lenient form leaks the live value"
              | Error e -> failtestf "lenient replay: %s" e

              let _, rest =
                  EffectCapture.replayEffectStrict decInt "a" "clock" (fun () -> 99) truncated
                  |> Result.defaultWith (fun f -> failtestf "%A" f)

              Expect.equal
                  (EffectCapture.replayEffectStrict decInt "a" "clock" (fun () -> 99) rest)
                  (Error(CaptureReplayFault.Exhausted "a"))
                  "the strict form refuses"

              let anchor = EffectCapture.captureHead StreamConfig.canonical caps

              Expect.isTrue
                  (EffectCapture.verifyCapturesAt StreamConfig.canonical OpStream.defaultHash anchor caps)
                  "the whole journal at its anchor"

              Expect.isFalse
                  (EffectCapture.verifyCapturesAt StreamConfig.canonical OpStream.defaultHash anchor truncated)
                  "the cut journal against the anchor"

          testCase "the label is checked at replay, exactly"
          <| fun _ ->
              let caps = record [ "a", "clock", 1 ]

              Expect.equal
                  (EffectCapture.replayEffectStrict decInt "a" "random" (fun () -> 99) caps)
                  (Error(CaptureReplayFault.LabelMismatch("random", "clock")))
                  "another label"

              Expect.equal
                  (EffectCapture.replayEffectStrict decInt "b" "clock" (fun () -> 99) caps)
                  (Error(CaptureReplayFault.IdentityMismatch("b", "a")))
                  "another effect"

              for bad in [ "Deterministic"; "Clock"; "random+clock"; "clock+clock"; ""; "clock+" ] do
                  Expect.equal
                      (EffectCapture.replayEffectStrict decInt "a" bad (fun () -> 99) caps)
                      (Error(CaptureReplayFault.LabelNotCanonical bad))
                      (sprintf "%A is not a label" bad)

          testCase "the label vocabulary is Function's, held equal over a corpus"
          <| fun _ ->
              let factors = [ "clock"; "random"; "network" ]

              let corpus =
                  [ "deterministic"
                    "Deterministic"
                    "DETERMINISTIC"
                    ""
                    "+"
                    "clock+"
                    "deterministic+clock" ]
                  @ [ for a in factors do
                          yield a
                          yield a.ToUpperInvariant()

                          for b in factors do
                              yield a + "+" + b

                              for c in factors do
                                  yield a + "+" + b + "+" + c ]

              for label in corpus do
                  Expect.equal
                      (EffectCapture.isDeterminismLabel label)
                      (Effect.tryDeterminismOfTag label |> Option.isSome)
                      (sprintf "%A" label) ]

// ---- Phase 416: a stream load types its faults, and names the ops it could not decode ----

/// The counter's op vocabulary as a NEWER host writes it: every counter op, and a `reset` that this
/// build's witness (`Reference.Counter`) has never heard of. A store this writes is intact, and the
/// older witness refuses exactly its `reset` ops — `unknown op kind: reset`.
module Newer =

    type NewerOp =
        | Known of CounterOp
        | Reset

    let resetText = Json.render (Json.kindObj "reset" [ "n", JInt 0 ])

    let witness: StreamWitness<NewerOp, int, string> =
        { Apply =
            fun op st ->
                match op with
                | Known k -> sw.Apply k st
                | Reset -> Ok 0
          Encode =
            function
            | Known k -> sw.Encode k
            | Reset -> resetText
          Decode =
            fun s ->
                if s = resetText then
                    Ok Reset
                else
                    sw.Decode s |> Result.map Known }

    /// What the older witness says of a `reset`.
    let refusal = "unknown op kind: reset"

/// The public loads of the two stream packages, found by reflection: every public method named
/// `fromJsonl…` or `loadLanes…`.
let private publicLoads () =
    [ typeof<OpRecord<int>>.Assembly; typeof<DagBreak>.Assembly ]
    |> List.collect (fun asm ->
        asm.GetExportedTypes()
        |> Array.collect (fun t ->
            t.GetMethods(
                System.Reflection.BindingFlags.Public
                ||| System.Reflection.BindingFlags.Static
                ||| System.Reflection.BindingFlags.DeclaredOnly
            ))
        |> Array.filter (fun m ->
            m.Name.StartsWith "fromJsonl"
            || m.Name.StartsWith "loadLanes"
            || (m.DeclaringType.Name = "Jsonl" && m.Name.StartsWith "read"))
        |> Array.map (fun m -> m.DeclaringType.Name + "." + m.Name, m.ReturnType)
        |> List.ofArray)

[<Tests>]
let streamLoadFaultTests =
    let h = OpStream.defaultHash

    // seq 0 inc, seq 1 reset, seq 2 inc, seq 3 reset — written by the newer host.
    let newerRecords () =
        [ Newer.Known(Inc 5); Newer.Reset; Newer.Known(Inc 2); Newer.Reset ]
        |> List.fold
            (fun acc op ->
                acc
                |> Result.bind (fun (st, recs) ->
                    OpStream.append (OpStream.canonical h Newer.witness) (Human "newer") op st recs))
            (Ok(0, OpStream.empty))
        |> function
            | Ok(_, recs) -> recs
            | Error e -> failwithf "the newer host could not write: %A" e

    testList
        "a stream load types its faults (Phase 416)"
        [ testCase "an intact stream holding ops this witness does not know is Undecodable, naming every one"
          <| fun _ ->
              let recs = newerRecords ()
              let text = Jsonl.write Newer.witness recs

              let expected =
                  [ 1; 3 ]
                  |> List.map (fun i ->
                      { Lane = None
                        Line = i + 1
                        NodeId = recs[i].Hash
                        Reason = Newer.refusal })

              for name, load in
                  [ "fromJsonl", Jsonl.read sw text
                    "fromJsonlVerified", Jsonl.readVerified (OpStream.canonical h sw) text
                    "fromJsonlLegacyActor",
                    Jsonl.readLegacyActor sw (text.Replace("{\"kind\":\"human\",\"id\":\"newer\"}", "\"newer\""))
                    "fromJsonlWithSnapshots", Jsonl.readWithSnapshots sw text |> Result.map fst ] do
                  match load with
                  | Error(StreamLoadFault.Undecodable sites) -> Expect.equal sites expected (name + " names both sites")
                  | other -> failtestf "%s: expected Undecodable, got %A" name other

              // the newer host reads its own stream, verified
              Expect.equal
                  (Jsonl.readVerified (OpStream.canonical h Newer.witness) text)
                  (Ok recs)
                  "the newer witness reads it"

          testCase "a broken chain answers the break, not the ops it could not decode"
          <| fun _ ->
              let recs = newerRecords ()
              let text = Jsonl.write Newer.witness recs
              // flip one byte of the reset record's stored hash
              let h1 = recs[1].Hash

              let flipped =
                  h1.Substring(0, h1.Length - 1) + (if h1.EndsWith "0" then "1" else "0")

              let tampered =
                  text.Replace("\"hash\":\"" + h1 + "\"", "\"hash\":\"" + flipped + "\"")

              Expect.notEqual tampered text "the probe flipped a byte"

              match Jsonl.readVerified (OpStream.canonical h sw) tampered with
              | Error(StreamLoadFault.Broken b) -> Expect.equal b.Index 1 "the break is at the flipped record"
              | other -> failtestf "expected the chain break, got %A" other

          testCase "toString keeps the pre-416 bytes: a decode failure, a scanner fault and a break"
          <| fun _ ->
              let recs = newerRecords ()
              let one = Jsonl.write Newer.witness (List.truncate 2 recs)

              Expect.equal
                  (Jsonl.read sw one |> Result.mapError StreamLoadFault.toString)
                  (Error("line 2: " + Newer.refusal + " (position 0)"))
                  "one undecodable op renders as the reader's old message"

              let site =
                  { Lane = Some "beta"
                    Line = 4
                    NodeId = "n"
                    Reason = "why" }

              Expect.equal (StreamLoadFault.siteText site) "lane beta: line 4: why (position 0)" "a site with a lane"

              Expect.equal
                  (StreamLoadFault.toStringWith
                      string
                      (StreamLoadFault<int>.Undecodable [ site; { site with Lane = None; Line = 5 } ]))
                  "lane beta: line 4: why (position 0); line 5: why (position 0)"
                  "several sites are joined, each as siteText renders it"

              Expect.equal
                  (StreamLoadFault.toStringWith (sprintf "break %d") (StreamLoadFault.Broken 7))
                  "break 7"
                  "toStringWith renders a break by the renderer it is handed"

              Expect.equal
                  (Jsonl.read sw "{" |> Result.mapError StreamLoadFault.toString)
                  (Error "line 1: the line ends inside the object (position 1)")
                  "a scanner fault renders as JsonlFault.toString"

              match build () with
              | Ok(_, good) ->
                  let tampered =
                      good |> List.mapi (fun i r -> if i = 1 then { r with Op = Inc 999 } else r)

                  match Jsonl.readVerified (OpStream.canonical h sw) (Jsonl.write sw tampered) with
                  | Error(StreamLoadFault.Broken b as f) ->
                      Expect.equal
                          (StreamLoadFault.toString f)
                          (sprintf
                              "OpStream.fromJsonlVerified: chain breaks at record %d — %s"
                              b.Index
                              (ChainBreakReason.toString b.Reason))
                          "a break renders as the verified reader's old message"
                  | other -> failtestf "expected a break, got %A" other
              | Error e -> failtestf "build failed: %A" e

          testCase "no public stream load answers with a string (reflection over both packages)"
          <| fun _ ->
              let loads = publicLoads ()
              let names = loads |> List.map fst |> Set.ofList

              // non-vacuity: the loads the phase names are all found
              for expected in
                  [ "Jsonl.read"
                    "Jsonl.readVerified"
                    "Jsonl.readLegacyActor"
                    "Jsonl.readWithSnapshots"
                    "Dag.fromJsonl"
                    "Dag.fromJsonlVerified"
                    "Dag.fromJsonlWithCheckpoints"
                    "Dag.loadLanes"
                    "Dag.loadLanesVerified" ] do
                  Expect.isTrue (names.Contains expected) (sprintf "%s is found by the probe" expected)

              let stringly =
                  loads
                  |> List.filter (fun (_, rt) ->
                      rt.IsGenericType
                      && rt.GetGenericTypeDefinition() = typedefof<Result<int, int>>
                      && rt.GetGenericArguments()[1] = typeof<string>)

              Expect.isEmpty stringly "every load's fault is typed"

              // the probe goes red on the shape it hunts: a Result<_, string>
              let probe = typeof<Result<OpRecord<int> list, string>>

              Expect.isTrue
                  (probe.IsGenericType
                   && probe.GetGenericTypeDefinition() = typedefof<Result<int, int>>
                   && probe.GetGenericArguments()[1] = typeof<string>)
                  "the predicate recognises a stringly load" ]

// ---- Phase 530: the chain verbs over a shape, an anchor, and the keyed journal's JSONL ----

let private shapeOf = ChainShape.records StreamConfig.canonical encodeOp

let private okOf (r: Result<'a, 'e>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failwithf "expected Ok, got %A" e

let private keyedJournal () =
    let h = OpStream.defaultHash
    let o1, j1 = KeyedCapture.beginEffectKeyed StreamConfig.canonical h "clock" "k1" []

    let j2 =
        KeyedCapture.settleEffectKeyed StreamConfig.canonical h encInt "k1" o1 (Ok 42) j1
        |> okOf

    let ce =
        KeyedCapture.captureEffectKeyed
            StreamConfig.canonical
            h
            encInt
            "random"
            "k\"2"
            (fun () -> Some(Error "why\nnot"))
            j2

    (KeyedCapture.captureEffectKeyed StreamConfig.canonical h encInt "network" "k3" (fun () -> None) ce.Journal).Journal

let private doubleOp (op: CounterOp) =
    match op with
    | Inc n -> Inc(2 * n)
    | Dec n -> Dec(2 * n)

[<Tests>]
let chainShapeTests =
    testList
        "Phase 530 - ChainShape, Anchor and the keyed journal's JSONL"
        [ test "the walk over the op record's shape from the genesis anchor is firstChainBreak" {
              let _, recs = build () |> okOf

              let tampered =
                  recs |> List.mapi (fun i r -> if i = 1 then { r with Op = Inc 9 } else r)

              for rs in [ recs; tampered; List.rev recs ] do
                  Expect.equal
                      (ChainShape.walk shapeOf OpStream.defaultHash (Anchor.genesis StreamConfig.canonical) rs)
                      (OpStream.firstChainBreak (OpStream.canonical OpStream.defaultHash sw) rs)
                      "one walker"
          }

          test "a segment walked from its anchor agrees with the whole stream's walk, at every cut" {
              let _, recs = build () |> okOf

              let tampered =
                  recs |> List.mapi (fun i r -> if i = 2 then { r with Op = Inc 7 } else r)

              for rs in [ recs; tampered ] do
                  for k in 0 .. rs.Length do
                      let at =
                          ChainShape.tip shapeOf (Anchor.genesis StreamConfig.canonical) (List.truncate k rs)

                      Expect.equal at.Seq k "the tip's sequence is the prefix length"

                      let whole = OpStream.firstChainBreak (OpStream.canonical OpStream.defaultHash sw) rs

                      let segment =
                          ChainShape.walk shapeOf OpStream.defaultHash at (List.skip k rs)
                          |> Option.map (fun b -> { b with Index = b.Index + k })

                      // a break in the prefix is the whole walk's alone; one in the tail, both walks'
                      match whole with
                      | Some b when b.Index < k -> ()
                      | _ -> Expect.equal segment whole (sprintf "the segment above %d" k)
          }

          test "rehash with the identity map is OpStream.rehash, and a map re-chains a mapped stream that verifies" {
              let _, recs = buildWith legacyCfg |> okOf
              let from = ChainShape.records legacyCfg encodeOp

              let rehashBy f rs =
                  ChainShape.rehash
                      from
                      OpStream.defaultHash
                      shapeOf
                      OpStream.defaultHash
                      (Anchor.genesis legacyCfg)
                      ""
                      f
                      rs

              Expect.equal
                  (rehashBy id recs)
                  (OpStream.rehash
                      (OpStream.ofConfig legacyCfg OpStream.defaultHash sw)
                      (OpStream.canonical OpStream.defaultHash sw)
                      recs)
                  "id"

              let doubled = rehashBy (fun r -> { r with Op = doubleOp r.Op }) recs |> okOf

              Expect.isTrue
                  (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash sw) doubled)
                  "the mapped stream verifies"

              Expect.equal (doubled |> List.map _.Op) [ Inc 10; Inc 6; Dec 4 ] "each op mapped"

              let broken =
                  recs |> List.mapi (fun i r -> if i = 0 then { r with Op = Inc 1 } else r)

              Expect.isError (rehashBy id broken) "a source that does not verify is refused"
          }

          test "append over the shape mints the record appendChainOnly mints" {
              let _, recs = build () |> okOf

              let viaShape =
                  ChainShape.append
                      shapeOf
                      OpStream.defaultHash
                      (Anchor.genesis StreamConfig.canonical)
                      (fun n ->
                          { Seq = n
                            Actor = Human "z"
                            Op = Inc 1
                            PrevHash = ""
                            Hash = "" })
                      recs

              Expect.equal
                  viaShape
                  (OpStream.appendChainOnly (OpStream.canonical OpStream.defaultHash sw) (Human "z") (Inc 1) recs)
                  "one append"

              let at = ChainShape.tip shapeOf (Anchor.genesis StreamConfig.canonical) recs

              Expect.equal
                  (ChainShape.linkAt
                      shapeOf
                      OpStream.defaultHash
                      at
                      { List.last viaShape with
                          PrevHash = ""
                          Hash = "" })
                  (List.last viaShape)
                  "the record linked at the tip"
          }

          test "the keyed journal writes one chained line per record and reads back the journal it wrote" {
              let j = keyedJournal ()
              let text = KeyedCapture.toJsonl j

              Expect.equal (KeyedCapture.tryToJsonl j) (Ok text) "the checked writer agrees"
              Expect.equal (KeyedCapture.ofJsonl text) (Ok j) "round trip"

              Expect.isTrue
                  (KeyedCapture.verifyKeyedCaptures StreamConfig.canonical OpStream.defaultHash j)
                  "the journal verifies"

              let first = List.head j

              Expect.equal
                  (text.Split('\n')[0])
                  ("{\"capture\":\"keyed\",\"seq\":0,\"key\":\"k1\",\"occ\":0,\"det\":\"clock\",\"phase\":\"attempted\",\"value\":null,\"prevHash\":\"\",\"hash\":"
                   + Jsonl.quote first.Hash
                   + "}")
                  "the line is the hash payload with its link"

              let reread = KeyedCapture.ofJsonl text |> okOf

              Expect.isTrue
                  (KeyedCapture.verifyKeyedCaptures StreamConfig.canonical OpStream.defaultHash reread)
                  "read back, it verifies"
          }

          test "the keyed reader refuses an unknown phase and the checked writer a value it cannot read back" {
              let j = keyedJournal ()

              let text =
                  (KeyedCapture.toJsonl j).Replace("\"phase\":\"attempted\"", "\"phase\":\"maybe\"")

              match KeyedCapture.ofJsonl text with
              | Error f ->
                  Expect.equal f.Line 1 "the first line"
                  Expect.equal f.Reason (JsonlFaultReason.Refused "unknown capture phase \"maybe\"") "named"
              | Ok _ -> failtest "an unknown phase reads"

              let bad =
                  j
                  |> List.mapi (fun i c -> if i = 1 then { c with Value = "{\"a\":\n1}" } else c)

              match KeyedCapture.tryToJsonl bad with
              | Error f -> Expect.equal (f.Line, f.Member) (2, "value") "the value, by its line"
              | Ok _ -> failtest "a multi-line value was written"
          }

          test "Jsonl.settle names EVERY refused read, in read order, and decodes the rest" {
              let reads = [ 1, Ok "a"; 2, Error "x"; 3, Ok "c"; 4, Error "y" ]

              let site (line: int, _: Result<string, string>) (why: string) : UndecodableOp =
                  { Lane = None
                    Line = line
                    NodeId = string line
                    Reason = why }

              Expect.equal
                  (Jsonl.settle snd site reads
                   |> Result.mapError (List.map (fun s -> s.Line, s.Reason)))
                  (Error [ 2, "x"; 4, "y" ])
                  "every site"

              Expect.equal (Jsonl.settle snd site [ 1, Ok "a"; 2, Ok "b" ]) (Ok [ "a"; "b" ]) "all decoded"
          }

          test "Jsonl.carry never refuses, and re-encodes an undecoded op as its stored text" {
              let cw = Jsonl.carry sw
              let undecoded = cw.Decode "{\"kind\":\"nope\"}" |> okOf
              Expect.isError (snd undecoded) "the real witness refuses it"
              Expect.equal (cw.Encode undecoded) "{\"kind\":\"nope\"}" "its stored text"
              let decoded = cw.Decode(encodeOp (Inc 2)) |> okOf
              Expect.equal (cw.Encode decoded) (encodeOp (Inc 2)) "a decoded op encodes by the witness"
          }

          test "Jsonl.checkedLines refuses the first unreadable member by its line and member" {
              let lines =
                  Jsonl.checkedLines "v" (fun (v: string) -> [ "v", v ]) (fun _ v -> "{\"v\":" + v + "}") [ "1"; "[2]" ]

              Expect.equal lines (Ok "{\"v\":1}\n{\"v\":[2]}") "written"

              match Jsonl.checkedLines "v" (fun (v: string) -> [ "v", v ]) (fun _ v -> v) [ "1"; " 2" ] with
              | Error f ->
                  Expect.equal (f.Line, f.Member, f.Reason) (2, "v", JsonlWriteFaultReason.NotTrimStable) "refused"
              | Ok _ -> failtest "a value with leading whitespace was written"
          }

          test "a keyed refusal's reason is read by the scanner's unquote: one JSON string, to its last character" {
              let h = OpStream.defaultHash

              let j =
                  (KeyedCapture.captureEffectKeyed
                      StreamConfig.canonical
                      h
                      encInt
                      "random"
                      "k"
                      (fun () -> Some(Error "no"))
                      [])
                      .Journal

              let replay (j: KeyedCapture list) =
                  KeyedCapture.replayEffectKeyed decInt "random" "k" (fun () -> None) Map.empty j
                  |> Result.map fst

              Expect.equal (replay j) (Ok(Some(Error "no"))) "the reason"

              // A value that is a string FOLLOWED by more members no longer reads as that string: the
              // private copy this replaced wrapped it in an object and parsed a line, reading the tail
              // as members of that object.
              let spliced =
                  j
                  |> List.map (fun c ->
                      if c.Phase = CapturePhase.Refused then
                          { c with Value = "\"no\",\"x\":1" }
                      else
                          c)

              match replay spliced with
              | Error(KeyedCaptureFault.Undecodable _) -> ()
              | other -> failtestf "a spliced reason read as %A" other
          } ]
