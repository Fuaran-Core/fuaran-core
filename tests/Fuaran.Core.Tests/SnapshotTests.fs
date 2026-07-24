module Fuaran.Core.Tests.SnapshotTests

// Phase 244 — op-stream snapshot / compaction: bounded replay from a checkpoint, with the
// chain verifiable across the truncation boundary.

open Expecto
open Fuaran.Core

type private CounterOp =
    | Inc of int
    | Dec of int

let private sw: StreamWitness<CounterOp, int, string> =
    { Apply =
        fun op st ->
            match op with
            | Inc n -> Ok(st + n)
            | Dec n -> if st - n < 0 then Error "negative" else Ok(st - n)
      Encode =
        fun op ->
            match op with
            | Inc n -> Json.render (Json.kindObj "inc" [ "n", JInt n ])
            | Dec n -> Json.render (Json.kindObj "dec" [ "n", JInt n ])
      Decode =
        fun s ->
            Decode.parse s
            |> Result.bind (fun el ->
                Decode.kindOf el
                |> Result.bind (fun k ->
                    Decode.intField "n" el
                    |> Result.map (fun n -> if k = "dec" then Dec n else Inc n))) }

let private h = OpStream.defaultHash
let private stateEnc (s: int) = string s
let private stateDec (s: string) = int s

/// Build the chain Inc5; Inc3; Dec2; Inc4 → live state 10.
let private build () =
    let step acc op =
        acc
        |> Result.bind (fun (st, recs) -> OpStream.append h sw (Human "tester") op st recs)

    [ Inc 5; Inc 3; Dec 2; Inc 4 ] |> List.fold step (Ok(0, OpStream.empty))

[<Tests>]
let tests =
    testList
        "OpStream.snapshot"
        [ testCase "replayFrom a compacted checkpoint equals replay-from-origin"
          <| fun _ ->
              match build () with
              | Ok(live, recs) ->
                  Expect.equal live 10 "sanity: 5 + 3 - 2 + 4"

                  for atSeq in 0..4 do
                      match OpStream.compact h stateEnc sw 0 recs atSeq with
                      | Ok(snap, tail) ->
                          Expect.equal
                              (OpStream.replayFrom sw snap tail)
                              (Ok live)
                              (sprintf "bounded replay = live at seq %d" atSeq)

                          Expect.equal (List.length tail) (4 - atSeq) "tail length"
                      | Error e -> failtestf "compact failed at %d: %s" atSeq e
              | Error e -> failtestf "build failed: %A" e

          testCase "verifyAcross confirms the chain across the truncation boundary"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  match OpStream.compact h stateEnc sw 0 recs 2 with
                  | Ok(snap, tail) ->
                      Expect.isTrue (OpStream.verifyAcross h stateEnc sw snap tail) "intact across the boundary"
                  | Error e -> failtestf "compact failed: %s" e
              | Error e -> failtestf "build failed: %A" e

          testCase "verifyAcross rejects a tampered snapshot state"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  match OpStream.compact h stateEnc sw 0 recs 2 with
                  | Ok(snap, tail) ->
                      let tampered = { snap with State = 999 }
                      Expect.isFalse (OpStream.verifyAcross h stateEnc sw tampered tail) "tampered state detected"
                  | Error e -> failtestf "compact failed: %s" e
              | Error e -> failtestf "build failed: %A" e

          testCase "verifyAcross rejects a tampered tail op"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  match OpStream.compact h stateEnc sw 0 recs 1 with
                  | Ok(snap, tail) ->
                      let badTail =
                          tail |> List.mapi (fun i r -> if i = 0 then { r with Op = Inc 99 } else r)

                      Expect.isFalse (OpStream.verifyAcross h stateEnc sw snap badTail) "tampered tail detected"
                  | Error e -> failtestf "compact failed: %s" e
              | Error e -> failtestf "build failed: %A" e

          testCase "snapshot JSONL round-trips and op-record readers skip the snapshot line"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  match OpStream.snapshotAt h stateEnc sw 0 recs 2 with
                  | Ok snap ->
                      let line = OpStream.snapshotToJsonl stateEnc snap

                      Expect.equal
                          (OpStream.snapshotFromJsonl stateDec line)
                          (Ok snap)
                          "snapshot survives the round-trip"
                      // a snapshot line interleaved with op records is ignored by fromJsonl
                      let mixed = line + "\n" + OpStream.toJsonl sw recs
                      Expect.equal (OpStream.fromJsonl sw mixed) (Ok recs) "fromJsonl skips the snapshot line"
                  | Error e -> failtestf "snapshotAt failed: %s" e
              | Error e -> failtestf "build failed: %A" e

          testCase "snapshot seq out of range is a typed error"
          <| fun _ ->
              match build () with
              | Ok(_, recs) -> Expect.isError (OpStream.snapshotAt h stateEnc sw 0 recs 99) "out-of-range rejected"
              | Error e -> failtestf "build failed: %A" e

          testCase "snapshotLaws certify the reference stream witness green (Phase 07)"
          <| fun _ ->
              let genOp (rng: ConfRng.T) =
                  let kind, r1 = ConfRng.intBelow 2 rng
                  let n, r2 = ConfRng.intBelow 5 r1
                  (if kind = 0 then Inc n else Dec n), r2

              let streamGen: StreamGen<CounterOp, int> = { State0 = 0; Op = genOp }
              let results = Conformance.snapshotLaws sw streamGen stateEnc h 321 100
              Expect.equal (List.length results) 2 "bounded replay + verifyAcross"
              Expect.isTrue (results |> List.forall (fun r -> r.Passed)) "snapshot laws pass"

          // Phase 13 — verified linear load.
          testCase "fromJsonlVerified accepts an intact stream and rejects a tampered op"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  let jsonl = OpStream.toJsonl sw recs
                  Expect.equal (OpStream.fromJsonlVerified h sw jsonl) (Ok recs) "intact stream verifies"

                  let tampered =
                      recs |> List.mapi (fun i r -> if i = 1 then { r with Op = Inc 999 } else r)

                  Expect.isError
                      (OpStream.fromJsonlVerified h sw (OpStream.toJsonl sw tampered))
                      "a tampered op (stale hash) fails the chain gate"
              | Error e -> failtestf "build failed: %A" e

          // Phase 16 — snapshot-aware read recovers the base state a compacted file carries.
          testCase "fromJsonlWithSnapshots recovers the snapshot + tail of a compacted file"
          <| fun _ ->
              match build () with
              | Ok(live, recs) ->
                  match OpStream.compact h stateEnc sw 0 recs 2 with
                  | Ok(snap, tail) ->
                      let file = OpStream.snapshotToJsonl stateEnc snap + "\n" + OpStream.toJsonl sw tail

                      match OpStream.fromJsonlWithSnapshots sw file with
                      | Ok(recs', snaps) ->
                          Expect.equal recs' tail "tail records recovered"
                          Expect.equal (List.length snaps) 1 "the snapshot line is surfaced, not dropped"

                          match OpStream.snapshotFromJsonl stateDec (List.head snaps) with
                          | Ok s ->
                              Expect.equal
                                  (OpStream.replayFrom sw s tail)
                                  (Ok live)
                                  "replay from recovered snapshot = live"
                          | Error e -> failtestf "snapshot line decode failed: %s" e
                      | Error e -> failtestf "snapshot-aware read failed: %s" e
                  | Error e -> failtestf "compact failed: %s" e
              | Error e -> failtestf "build failed: %A" e

          // Phase 15 — Result-returning snapshot state decode threads a typed failure.
          testCase "snapshotFromJsonlResult threads a typed state-decode failure (no exception)"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  match OpStream.snapshotAt h stateEnc sw 0 recs 2 with
                  | Ok snap ->
                      let line = OpStream.snapshotToJsonl stateEnc snap
                      let okDec (s: string) : Result<int, string> = Ok(int s)
                      Expect.equal (OpStream.snapshotFromJsonlResult okDec line) (Ok snap) "Result decoder succeeds"

                      let failDec (_: string) : Result<int, string> = Error "bad state"

                      match OpStream.snapshotFromJsonlResult failDec line with
                      | Error m -> Expect.stringContains m "bad state" "typed decode failure is threaded, not thrown"
                      | Ok _ -> failtest "expected the decode failure to surface"
                  | Error e -> failtestf "snapshotAt failed: %s" e
              | Error e -> failtestf "build failed: %A" e

          // Phase 14 — StreamConfig-aware snapshot verification.
          testCase "verifyAcrossWith verifies a custom-StreamConfig boundary the canonical one rejects"
          <| fun _ ->
              let cfg: StreamConfig =
                  { Payload = (fun seq actor opJson -> sprintf "%d|%s|%s" seq (Actor.id actor) opJson)
                    Genesis = "GEN" }

              let step acc op =
                  acc
                  |> Result.bind (fun (st, recs) -> OpStream.appendWith cfg h sw (Human "tester") op st recs)

              match [ Inc 5; Inc 3; Dec 2; Inc 4 ] |> List.fold step (Ok(0, OpStream.empty)) with
              | Ok(_, recs) ->
                  match OpStream.compact h stateEnc sw 0 recs 2 with
                  | Ok(snap, tail) ->
                      Expect.isTrue
                          (OpStream.verifyAcrossWith cfg h stateEnc sw snap tail)
                          "the custom-config boundary verifies under its own cfg"

                      Expect.isFalse
                          (OpStream.verifyAcross h stateEnc sw snap tail)
                          "the canonical verifier rejects a custom-config tail (the seam matters)"
                  | Error e -> failtestf "compact failed: %s" e
              | Error e -> failtestf "custom-config build failed: %A" e

          // ---- Phase 258: optional (chain-only) snapshot state-encoder ----

          testCase "chain-only replayFrom equals replay-from-origin at every boundary"
          <| fun _ ->
              match build () with
              | Ok(live, recs) ->
                  for atSeq in 0..4 do
                      match OpStream.compactChainOnly h sw 0 recs atSeq with
                      | Ok(snap, tail) ->
                          Expect.equal
                              (OpStream.replayFrom sw snap tail)
                              (Ok live)
                              (sprintf "chain-only bounded replay = live at seq %d" atSeq)

                          Expect.equal (List.length tail) (4 - atSeq) "tail length"
                      | Error e -> failtestf "compactChainOnly failed at %d: %s" atSeq e
              | Error e -> failtestf "build failed: %A" e

          testCase "chain-only verifyAcross accepts an intact boundary and catches a tampered prefix-link / tail"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  match OpStream.compactChainOnly h sw 0 recs 2 with
                  | Ok(snap, tail) ->
                      Expect.isTrue (OpStream.verifyAcrossChainOnly h sw snap tail) "intact chain-only boundary"

                      // the snapshot's PrevHash is the link to the truncated prefix — tampering it breaks
                      // the snapshot's own hash (it is folded in via hashFn).
                      let badLink = { snap with PrevHash = "WRONG" }
                      Expect.isFalse (OpStream.verifyAcrossChainOnly h sw badLink tail) "tampered prefix-link caught"

                      // a tampered tail op breaks the tail walk exactly as in strict mode.
                      let badTail =
                          tail |> List.mapi (fun i r -> if i = 0 then { r with Op = Inc 99 } else r)

                      Expect.isFalse (OpStream.verifyAcrossChainOnly h sw snap badTail) "tampered tail caught"
                  | Error e -> failtestf "compactChainOnly failed: %s" e
              | Error e -> failtestf "build failed: %A" e

          testCase "chain-only does NOT catch a swapped state, but strict does"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  match OpStream.compact h stateEnc sw 0 recs 2, OpStream.compactChainOnly h sw 0 recs 2 with
                  | Ok(strictSnap, strictTail), Ok(chainSnap, chainTail) ->
                      // strict folds the state into the snapshot hash — a swap is detected.
                      Expect.isFalse
                          (OpStream.verifyAcross h stateEnc sw { strictSnap with State = 999 } strictTail)
                          "strict catches a swapped state"

                      // chain-only trusts the stored state — a swap is NOT detected (documented trade-off).
                      Expect.isTrue
                          (OpStream.verifyAcrossChainOnly h sw { chainSnap with State = 999 } chainTail)
                          "chain-only does not catch a swapped state (by construction)"
                  | _ -> failtest "compact failed"
              | Error e -> failtestf "build failed: %A" e

          testCase "chain-only and strict snapshots at one boundary have distinct, non-interchangeable hashes"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  match OpStream.snapshotAt h stateEnc sw 0 recs 2, OpStream.snapshotAtChainOnly h sw 0 recs 2 with
                  | Ok strictSnap, Ok chainSnap ->
                      Expect.equal chainSnap.State strictSnap.State "same folded state at the boundary"
                      Expect.equal chainSnap.PrevHash strictSnap.PrevHash "same prefix link"

                      Expect.notEqual
                          chainSnap.Hash
                          strictSnap.Hash
                          "the stateHashed discriminator separates the two pre-images"

                      // the verifiers are not interchangeable — each rejects the other mode's snapshot.
                      let tail = recs |> List.skip 2

                      Expect.isFalse
                          (OpStream.verifyAcross h stateEnc sw chainSnap tail)
                          "the strict verifier rejects a chain-only snapshot"

                      Expect.isFalse
                          (OpStream.verifyAcrossChainOnly h sw strictSnap tail)
                          "the chain-only verifier rejects a strict snapshot"
                  | _ -> failtest "snapshotAt failed"
              | Error e -> failtestf "build failed: %A" e

          testCase "snapshotAt (strict) is byte-identical to snapshotAtOpt (Some encoder) — unchanged"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  Expect.equal
                      (OpStream.snapshotAt h stateEnc sw 0 recs 2)
                      (OpStream.snapshotAtOpt h (Some stateEnc) sw 0 recs 2)
                      "the strict wrapper reproduces the option-threaded form exactly"
              | Error e -> failtestf "build failed: %A" e

          testCase "chain-only snapshot JSONL flags stateHashed:false; the reader picks the mode and round-trips"
          <| fun _ ->
              match build () with
              | Ok(live, recs) ->
                  match OpStream.compactChainOnly h sw 0 recs 2 with
                  | Ok(snap, tail) ->
                      let line = OpStream.snapshotToJsonlChainOnly stateEnc snap

                      // the discriminator distinguishes the two persisted modes; a strict / pre-258 line
                      // (no field) reads as state-hashed via the absent-default.
                      Expect.isFalse
                          (OpStream.snapshotStateHashedFromJsonl line)
                          "chain-only line reads as not state-hashed"

                      Expect.isTrue
                          (OpStream.snapshotStateHashedFromJsonl (OpStream.snapshotToJsonl stateEnc snap))
                          "a strict line reads as state-hashed (absent-default)"

                      // the state still persists, so the line decodes to the snapshot exactly as a strict one.
                      Expect.equal
                          (OpStream.snapshotFromJsonl stateDec line)
                          (Ok snap)
                          "chain-only line round-trips the snapshot"

                      // a full compacted file: the reader recovers snapshot + tail, sees the flag, verifies
                      // in chain-only mode, and replays to the live state.
                      let file = line + "\n" + OpStream.toJsonl sw tail

                      match OpStream.fromJsonlWithSnapshots sw file with
                      | Ok(recs', [ snapLine ]) ->
                          Expect.equal recs' tail "tail recovered"

                          Expect.isFalse
                              (OpStream.snapshotStateHashedFromJsonl snapLine)
                              "the surfaced line still flags chain-only"

                          match OpStream.snapshotFromJsonl stateDec snapLine with
                          | Ok s ->
                              Expect.isTrue
                                  (OpStream.verifyAcrossChainOnly h sw s tail)
                                  "chain-only boundary verifies after reload"

                              Expect.equal
                                  (OpStream.replayFrom sw s tail)
                                  (Ok live)
                                  "replay from the reloaded chain-only snapshot = live"
                          | Error e -> failtestf "snapshot line decode failed: %s" e
                      | other -> failtestf "snapshot-aware read: unexpected %A" other
                  | Error e -> failtestf "compactChainOnly failed: %s" e
              | Error e -> failtestf "build failed: %A" e ]
