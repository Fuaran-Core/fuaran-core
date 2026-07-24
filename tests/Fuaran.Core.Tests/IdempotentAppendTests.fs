module Fuaran.Core.Tests.IdempotentAppendTests

// Phase 82 — idempotent append: a re-sent invocation key converges on its earlier entry
// (Duplicate names it) instead of double-applying; a fresh key appends chain-identically to
// plain append; the caller-threaded KeyIndex rebuilds from any stream (ofStream) in agreement
// with incremental maintenance; and the Phase 79 composition (appendIdempotentIf) checks the
// key BEFORE the head, so a lost-ack retry terminates instead of going StaleHead forever.

open Expecto
open Fuaran.Core

// The counter stream domain (mirrors OpStreamTests) — Dec below zero is a domain rejection.
type private CounterOp =
    | Inc of int
    | Dec of int

let private encodeOp =
    function
    | Inc n -> Json.render (Json.kindObj "inc" [ "n", JInt n ])
    | Dec n -> Json.render (Json.kindObj "dec" [ "n", JInt n ])

let private decodeOp (s: string) : Result<CounterOp, string> =
    Decode.parse s
    |> Result.bind (fun el ->
        Decode.kindOf el
        |> Result.bind (fun k -> Decode.intField "n" el |> Result.map (fun n -> k, n)))
    |> Result.bind (function
        | "inc", n -> Ok(Inc n)
        | "dec", n -> Ok(Dec n)
        | k, _ -> Error("unknown op kind: " + k))

let private sw: StreamWitness<CounterOp, int, string> =
    { Apply =
        fun op st ->
            match op with
            | Inc n -> Ok(st + n)
            | Dec n -> if st - n < 0 then Error "would go negative" else Ok(st - n)
      Encode = encodeOp
      Decode = decodeOp }

// The op → invocation-key projection (the Phase 27 invocationKey shape): here the canonical op
// JSON — two identical ops are, by contract, retries of one invocation.
let private keyOf = encodeOp

// Build a chain through appendIdempotent, threading (state, records, index) and keying on keyOf.
let private buildIdem ops =
    let step acc op =
        acc
        |> Result.bind (fun (st, recs, idx) ->
            OpStream.appendIdempotent OpStream.defaultHash sw (keyOf op) (Human "tester") op st idx recs
            |> Result.map (function
                | AppendOutcome.Appended(st', recs', idx') -> st', recs', idx'
                | AppendOutcome.Duplicate _ -> st, recs, idx))

    ops |> List.fold step (Ok(0, OpStream.empty, KeyIndex.empty))

let private genStreamOp (rng: ConfRng.T) : CounterOp * ConfRng.T =
    let kind, r1 = ConfRng.intBelow 2 rng
    let n, r2 = ConfRng.intBelow 5 r1
    (if kind = 0 then Inc n else Dec n), r2

let private streamGen: StreamGen<CounterOp, int> = { State0 = 0; Op = genStreamOp }

[<Tests>]
let tests =
    testList
        "OpStream.appendIdempotent (Phase 82)"
        [ testCase "a fresh key appends chain-identically to append and extends the index by exactly the new entry"
          <| fun _ ->
              match buildIdem [ Inc 5; Inc 3 ] with
              | Ok(st, recs, idx) ->
                  let op = Dec 2

                  let viaAppend = OpStream.append OpStream.defaultHash sw (Human "tester") op st recs

                  match
                      OpStream.appendIdempotent OpStream.defaultHash sw (keyOf op) (Human "tester") op st idx recs
                  with
                  | Ok(AppendOutcome.Appended(st', recs', idx')) ->
                      Expect.equal (Ok(st', recs')) viaAppend "chain-identical to plain append"
                      let last = List.last recs'

                      Expect.equal
                          (KeyIndex.tryFind (keyOf op) idx')
                          (Some { Seq = last.Seq; Hash = last.Hash })
                          "the new entry is indexed under its key"

                      Expect.equal
                          idx'
                          (KeyIndex.add (keyOf op) { Seq = last.Seq; Hash = last.Hash } idx)
                          "the index gained exactly the new entry"
                  | other -> failtestf "expected Appended, got %A" other
              | Error e -> failtestf "unexpected %A" e

          testCase "a re-sent key is Duplicate naming the existing entry; the stream and index are untouched"
          <| fun _ ->
              match buildIdem [ Inc 5; Inc 3; Dec 2 ] with
              | Ok(st, recs, idx) ->
                  // Re-send the second op (a lost-ack retry): same key, same op.
                  let existing = List.item 1 recs

                  match
                      OpStream.appendIdempotent
                          OpStream.defaultHash
                          sw
                          (keyOf existing.Op)
                          (Human "tester")
                          existing.Op
                          st
                          idx
                          recs
                  with
                  | Ok(AppendOutcome.Duplicate got) ->
                      Expect.equal got.Seq existing.Seq "names the existing entry's position"
                      Expect.equal got.Hash existing.Hash "names the existing entry's chain identity"
                  // recs / idx are immutable values the caller still holds — byte-identical by construction.
                  | other -> failtestf "expected Duplicate, got %A" other
              | Error e -> failtestf "unexpected %A" e

          testCase "distinct keys append — the guard only converges genuine retries"
          <| fun _ ->
              match buildIdem [ Inc 5; Inc 3; Dec 2; Inc 1 ] with
              | Ok(_, recs, idx) ->
                  Expect.equal (List.length recs) 4 "four distinct keys chained four records"
                  Expect.equal (Map.count idx.Seen) 4 "and indexed four entries"
              | Error e -> failtestf "unexpected %A" e

          testCase "a domain rejection is forwarded verbatim and indexes nothing (the key stays fresh)"
          <| fun _ ->
              match
                  OpStream.appendIdempotent
                      OpStream.defaultHash
                      sw
                      (keyOf (Dec 5))
                      (Human "tester")
                      (Dec 5)
                      3
                      KeyIndex.empty
                      OpStream.empty
              with
              | Error "would go negative" -> ()
              | other -> failtestf "expected the forwarded domain rejection, got %A" other

          testCase "KeyIndex.ofStream rebuilds the incrementally-maintained index from the stream"
          <| fun _ ->
              match buildIdem [ Inc 5; Inc 3; Dec 2; Inc 1 ] with
              | Ok(_, recs, idx) -> Expect.equal (KeyIndex.ofStream keyOf recs) idx "rebuild parity"
              | Error e -> failtestf "unexpected %A" e

          testCase "KeyIndex.ofStream is first-wins over a duplicate-keyed stream built with plain append"
          <| fun _ ->
              // Plain append enforces no idempotency, so the same op (same key) can chain twice;
              // the rebuilt index must name the FIRST entry the key produced.
              let step acc op =
                  acc
                  |> Result.bind (fun (st, recs) -> OpStream.append OpStream.defaultHash sw (Human "tester") op st recs)

              match [ Inc 5; Inc 5; Dec 2 ] |> List.fold step (Ok(0, OpStream.empty)) with
              | Ok(_, recs) ->
                  let idx = KeyIndex.ofStream keyOf recs
                  let first = List.head recs

                  Expect.equal
                      (KeyIndex.tryFind (keyOf (Inc 5)) idx)
                      (Some { Seq = first.Seq; Hash = first.Hash })
                      "the duplicate key names its first entry"
              | Error e -> failtestf "unexpected %A" e

          // Phase 82 ∘ Phase 79 — the combined idempotency-then-CAS retry loop.
          testCase "appendIdempotentIf converges a seen key on Duplicate even under a stale head"
          <| fun _ ->
              match buildIdem [ Inc 5; Inc 3 ] with
              | Ok(st, recs, idx) ->
                  // The lost-ack retry: the earlier attempt landed (head advanced), the caller's
                  // expected head is stale — the key check must run first so the retry terminates.
                  let existing = List.head recs

                  match
                      OpStream.appendIdempotentIf
                          OpStream.defaultHash
                          sw
                          (keyOf existing.Op)
                          "stale-head"
                          (Human "tester")
                          existing.Op
                          st
                          idx
                          recs
                  with
                  | Ok(AppendOutcome.Duplicate got) ->
                      Expect.equal got.Hash existing.Hash "converged on the entry the key first produced"
                  | other -> failtestf "expected Duplicate to precede the head check, got %A" other
              | Error e -> failtestf "unexpected %A" e

          testCase "appendIdempotentIf with a fresh key refuses a stale head naming both heads"
          <| fun _ ->
              match buildIdem [ Inc 5; Inc 3 ] with
              | Ok(st, recs, idx) ->
                  match
                      OpStream.appendIdempotentIf
                          OpStream.defaultHash
                          sw
                          (keyOf (Dec 1))
                          "not-the-head"
                          (Human "tester")
                          (Dec 1)
                          st
                          idx
                          recs
                  with
                  | Error(AppendRejection.StaleHead(expected, actual)) ->
                      Expect.equal expected "not-the-head" "names the caller's expected head"
                      Expect.equal actual (OpStream.head recs) "names the stream's actual head"
                  | other -> failtestf "expected StaleHead, got %A" other
              | Error e -> failtestf "unexpected %A" e

          testCase "appendIdempotentIf with a fresh key and the true head appends chain-identically to append"
          <| fun _ ->
              match buildIdem [ Inc 5; Inc 3 ] with
              | Ok(st, recs, idx) ->
                  let op = Dec 2

                  let viaAppend = OpStream.append OpStream.defaultHash sw (Human "tester") op st recs

                  match
                      OpStream.appendIdempotentIf
                          OpStream.defaultHash
                          sw
                          (keyOf op)
                          (OpStream.head recs)
                          (Human "tester")
                          op
                          st
                          idx
                          recs
                  with
                  | Ok(AppendOutcome.Appended(st', recs', _)) ->
                      Expect.equal (Ok(st', recs')) viaAppend "the matched-head fresh-key path is append"
                  | other -> failtestf "expected Appended, got %A" other
              | Error e -> failtestf "unexpected %A" e

          testCase "appendIdempotentIf forwards a domain rejection once the key is fresh and the head matches"
          <| fun _ ->
              match
                  OpStream.appendIdempotentIf
                      OpStream.defaultHash
                      sw
                      (keyOf (Dec 5))
                      (OpStream.head OpStream.empty)
                      (Human "t")
                      (Dec 5)
                      3
                      KeyIndex.empty
                      OpStream.empty
              with
              | Error(AppendRejection.Domain "would go negative") -> ()
              | other -> failtestf "expected a forwarded domain rejection, got %A" other

          // Phase 82 — the conformance laws over the same witness (seed-replayable).
          testCase "idempotencyLaws certify fresh≡append + duplicate-convergence + rebuild-parity + key-before-CAS"
          <| fun _ ->
              let results =
                  Conformance.idempotencyLaws keyOf sw streamGen OpStream.defaultHash 8282 200

              Expect.equal (List.length results) 4 "fresh + duplicate + parity + composition laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "idempotencyLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism of the kit itself
              Expect.equal
                  (Conformance.idempotencyLaws keyOf sw streamGen OpStream.defaultHash 8282 200)
                  results
                  "same seed ⇒ identical report" ]
