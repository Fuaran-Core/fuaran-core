module Fuaran.Core.Tests.ProofOracle.CompactedTests

// Phase 301 — the bridge for `proofs/Chain.fst` sections 7b and 7c: the extracted model of the
// compacted stream (`append_to`, `compact_from`, `index_onto`) and of the capture journal
// (`record_session`, `first_capture_break_from`, `replay_session`) run beside production. A file of
// its own rather than more cases in `ProofOracle.ChainTests.fs`, beside the other models' `ProofOracle.*Tests.fs`; the
// ladder reads its case names beside that suite's (`ProofsLadderTests.realCases`). The chain bridges
// (`posOfInt`, `toChainRecords`, `chainApply`, `modelPay`, ...) are that suite's.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.ProofOracle.Bridges
open Fuaran.Core.Tests.ChainOracle
open Fuaran.Core.Tests.ProofOracle.ChainTests
open Fuaran.Core.Tests.Reference.Counter

let private stateEnc (s: int) = string s

/// A compaction's whole outcome — the refusal rendered as the model spells it, or every field of the
/// snapshot and the tail record for record.
type private Verdict =
    | CompactedTo of seq: int * state: int * prevHash: string * hash: string * tail: Chain.record<CounterOp> list
    | RefusedWith of string

let private ofModel (r: Chain.compacted<CounterOp, int>) : Verdict =
    match r with
    | Chain.Compacted(s, tail) -> CompactedTo(intOfPos s.sseq, s.sstate, s.sprev, s.shash, tail)
    | Chain.CompactRefused e -> RefusedWith e

/// Production's typed fault in the model's spelling — `compact`'s two messages, which the model pins,
/// and the seam `compactFrom` checks first.
let private ofProd (r: Result<Compacted<CounterOp, int>, SnapshotFault<string>>) : Verdict =
    match r with
    | Ok c -> CompactedTo(c.Snapshot.Seq, c.Snapshot.State, c.Snapshot.PrevHash, c.Snapshot.Hash, toChainRecords c.Tail)
    | Error(SnapshotFault.SeqOutOfRange _) -> RefusedWith "OpStream.snapshotAt: seq out of range"
    | Error(SnapshotFault.PrefixRejected(i, _)) ->
        RefusedWith(sprintf "OpStream.snapshotAt: prefix replay failed at %d" i)
    | Error(SnapshotFault.TailSeqMismatch _) ->
        RefusedWith "OpStream.snapshot: the tail does not start at the snapshot's boundary"
    | Error(SnapshotFault.TailRejected(i, _)) -> RefusedWith(sprintf "tail rejected at %d" i)

let private keyOf (op: CounterOp) : string option = Some(encode op)

let private modelKeyOf (op: CounterOp) : Chain.found<string> = Chain.Found(encode op)

/// A key index as the set of its entries — the model keeps a first-wins list, production a map.
let private ofModelIndex (idx: Chain.kentry list) : Set<string * int * string> =
    idx |> List.map (fun e -> e.kkey, intOfPos e.kseq, e.khash) |> Set.ofList

let private ofProdIndex (idx: KeyIndex) : Set<string * int * string> =
    idx.Seen
    |> Map.toList
    |> List.map (fun (k, e) -> k, e.Seq, e.Hash)
    |> Set.ofList

/// A seeded pool of counter streams, lengths 0..7, built through production's `append` (a drawn op
/// the domain refuses does not extend the stream).
let private streams (seed: int) (count: int) : (int * OpRecord<CounterOp> list) list =
    let rng = System.Random seed

    [ for _ in 1..count do
          let len = rng.Next 8
          let mutable st = 0
          let mutable rs = OpStream.empty

          for _ in 1..len do
              let op = if rng.Next 3 = 0 then Dec(rng.Next 4) else Inc(rng.Next 5)

              match OpStream.append (OpStream.canonical OpStream.defaultHash witness) (Human "w") op st rs with
              | Ok(st', rs') ->
                  st <- st'
                  rs <- rs'
              | Error _ -> ()

          yield st, rs ]

let private compactProd mode rs n =
    Compacted.compactAt mode (OpStream.canonical OpStream.defaultHash witness) stateEnc keyOf 0 rs n

let private compactModel (hashFn: HashFn) mode rs n =
    Chain.compact hashFn showPos (modelPay mode stateEnc) (chainApply witness) "" 0 (toChainRecords rs) (posOfInt n)

/// The re-compaction tally: the (n, k) pairs `Compared`, production's refusals among them (`Refused`)
/// and the prefix refusals by their origin index (`PrefixRefused`), and the in-range compositions
/// met (`Composed`).
let private recompactionTally =
    Tally.empty [ "Compared"; "Refused"; "PrefixRefused"; "Composed" ]

/// Every (n, k) pair of every stream in the pool, both modes, plus a STARVED snapshot (its state set
/// back to the initial one) so the prefix refusal is met: production's `compactFrom` against the
/// model's `compact_from`, and production's `compactFrom` against production's own `compactAt` at
/// `n + k` — the theorem held of production's values.
let private recompactionDifferential (modelHash: HashFn) : Tally =
    let boundaries =
        [ for _, rs in streams 301 60 do
              for mode in [ SnapshotMode.Strict; SnapshotMode.ChainOnly ] do
                  for n in 0 .. List.length rs do
                      yield rs, mode, n ]

    let boundary (rs: OpRecord<CounterOp> list, mode: SnapshotMode, n: int) (t: Tally) : Tally =
        let len = List.length rs

        match compactProd mode rs n, compactModel modelHash mode rs n with
        | Ok c, Chain.Compacted(snap, tail) ->
            let probe (starved: bool, k: int) (t: Tally) : Tally =
                let c, snap =
                    if starved then
                        { c with
                            Snapshot = { c.Snapshot with State = 0 } },
                        { snap with Chain.sstate = 0 }
                    else
                        c, snap

                let p =
                    ofProd (
                        Compacted.compactFrom (OpStream.canonical OpStream.defaultHash witness) stateEnc keyOf c (n + k)
                    )

                let m =
                    ofModel (
                        Chain.compact_from
                            modelHash
                            showPos
                            (modelPay mode stateEnc)
                            (chainApply witness)
                            snap
                            tail
                            (posOfInt k)
                    )

                let refusal (t: Tally) : Tally =
                    match p with
                    | RefusedWith e ->
                        t
                        |> Tally.bump "Refused"
                        |> Tally.bumpIf (e.Contains "prefix replay") "PrefixRefused"
                    | CompactedTo _ -> t

                let t =
                    t
                    |> Tally.bump "Compared"
                    |> refusal
                    |> Differential.check (p = m) (fun () ->
                        sprintf "mode=%A n=%d k=%d starved=%b\n  production: %A\n  model:      %A" mode n k starved p m)

                if starved then
                    t
                else
                    let whole = ofProd (compactProd mode rs (n + k))

                    t
                    |> Tally.bumpIf (k <= len - n) "Composed"
                    |> Differential.check (p = whole) (fun () ->
                        sprintf "compact_compose fails of production: mode=%A n=%d k=%d" mode n k)

            Differential.over
                [ for starved in [ false; true ] do
                      for k in 0 .. len - n + 1 do
                          yield starved, k ]
                probe
                t
        | p, m -> Tally.disagree (sprintf "the compactions disagree before composing: n=%d\n  %A\n  %A" n p m) t

    Differential.over boundaries boundary recompactionTally

/// One equality held of production against the model (or of production against itself), under the
/// `Expect.equal` reading: `what`, and both values.
let private agreeOn (what: string) (production: 'T) (model: 'T) (t: Tally) : Tally =
    Differential.check
        (production = model)
        (fun () -> sprintf "%s\n  production: %A\n  the model:  %A" what production model)
        t

[<Tests>]
let compactedOracleTests =
    testList
        "Proofs.Oracle"
        [ testCase "the re-compaction oracle agrees with production at every (n, k), and production composes"
          <| fun _ ->
              let t = recompactionDifferential OpStream.defaultHash

              Differential.expectAgrees "re-compaction" t
              Expect.isGreaterThan (Tally.count "Compared" t) 500 "every (n, k) pair over the pool was compared"
              Expect.isGreaterThan (Tally.count "Composed" t) 200 "in-range compositions met"
              Expect.isGreaterThan (Tally.count "Refused" t) 0 "out-of-range refusals met"
              Expect.isGreaterThan (Tally.count "PrefixRefused" t) 0 "a prefix refusal met, by its origin index"

          testCase "a re-compaction model handed a DIFFERENT hash disagrees with production — the comparison can lose"
          <| fun _ ->
              let other: HashFn = fun prev payload -> OpStream.defaultHash ("x" + prev) payload
              Differential.expectLoses "a re-compaction model under a wrong hash" (recompactionDifferential other)

          testCase "append after compaction: the oracle's appendTo mints production's record, at every boundary"
          <| fun _ ->
              let stream (st: int, rs: OpRecord<CounterOp> list) (t: Tally) : Tally =
                  let len = List.length rs

                  let fullModel =
                      Chain.append_full
                          OpStream.defaultHash
                          showPos
                          encode
                          ""
                          (toChainRecords rs)
                          (Actor.encode (Human "w"))
                          (Inc 2)

                  match OpStream.append (OpStream.canonical OpStream.defaultHash witness) (Human "w") (Inc 2) st rs with
                  | Error e -> Tally.disagree (sprintf "append refused: %A" e) t
                  | Ok(_, full) ->
                      let boundary (n: int) (t: Tally) : Tally =
                          match
                              compactProd SnapshotMode.Strict rs n,
                              compactModel OpStream.defaultHash SnapshotMode.Strict rs n
                          with
                          | Ok c, Chain.Compacted(snap, tail) ->
                              let t = Tally.bumpIf (List.isEmpty c.Tail) "EmptyTail" t

                              match
                                  Compacted.appendTo
                                      (OpStream.canonical OpStream.defaultHash witness)
                                      (Human "w")
                                      (Inc 2)
                                      st
                                      c
                              with
                              | Ok(_, c') ->
                                  t
                                  |> Tally.bump "Compared"
                                  |> agreeOn
                                      (sprintf "appendTo at %d" n)
                                      (toChainRecords c'.Tail)
                                      (Chain.append_to
                                          OpStream.defaultHash
                                          showPos
                                          encode
                                          snap
                                          tail
                                          (Actor.encode (Human "w"))
                                          (Inc 2))
                                  |> agreeOn
                                      (sprintf "append_after_compact_verifies_iff, of production, at %d" n)
                                      (Compacted.verify (OpStream.canonical OpStream.defaultHash witness) stateEnc c')
                                      (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash witness) full)
                              | Error e -> Tally.disagree (sprintf "appendTo refused: %A" e) t
                          | p, m -> Tally.disagree (sprintf "compactions disagree at %d: %A / %A" n p m) t

                      t
                      |> agreeOn "the model's append is production's" (toChainRecords full) fullModel
                      |> Differential.over [ 0..len ] boundary

              let t =
                  Tally.empty [ "Compared"; "EmptyTail" ]
                  |> Differential.over (streams 3011 60) stream

              Differential.expectAgrees "append after compaction" t
              Expect.isGreaterThan (Tally.count "Compared" t) 200 "boundaries compared"
              Expect.isGreaterThan (Tally.count "EmptyTail" t) 30 "the empty tail, where the defect lived, was met"

          testCase "the key-index oracle agrees with production, and the fold splits at every boundary"
          <| fun _ ->
              let stream (_: int, rs: OpRecord<CounterOp> list) (t: Tally) : Tally =
                  let whole = Chain.index_onto modelKeyOf [] (toChainRecords rs)

                  let boundary (n: int) (t: Tally) : Tally =
                      match compactProd SnapshotMode.ChainOnly rs n with
                      | Ok c ->
                          let modelSplit =
                              Chain.index_onto
                                  modelKeyOf
                                  (Chain.index_onto modelKeyOf [] (toChainRecords (List.truncate n rs)))
                                  (toChainRecords c.Tail)

                          t
                          |> Tally.bump "Compared"
                          |> agreeOn "key_index_rebuild_parity" (ofModelIndex modelSplit) (ofModelIndex whole)
                          |> agreeOn
                              (sprintf "Compacted.keyIndex at %d" n)
                              (ofProdIndex (Compacted.keyIndex keyOf c))
                              (ofModelIndex modelSplit)
                      | Error e -> Tally.disagree (sprintf "compact failed: %A" e) t

                  t
                  |> agreeOn "ofStream" (ofProdIndex (KeyIndex.ofStream encode rs)) (ofModelIndex whole)
                  |> Differential.over [ 0 .. List.length rs ] boundary

              let t = Tally.empty [ "Compared" ] |> Differential.over (streams 30111 60) stream

              Differential.expectAgrees "the key index" t
              Expect.isGreaterThan (Tally.count "Compared" t) 200 "boundaries compared" ]

// ---- the capture journal (section 7c) ----

let private esc (s: string) : string = Json.render (JStr s)

let private encInt (n: int) = Json.render (JInt n)

let private decInt (s: string) : Result<int, string> =
    Json.parse s |> Result.bind (Decoder.describing Decoder.int)

let private labels =
    [ "clock"; "random"; "clock+random"; "network"; EffectCapture.deterministicTag ]

/// A seeded pool of sessions: effect identity, label and realised value per evaluation.
let private sessions (seed: int) (count: int) : (string * string * int) list list =
    let rng = System.Random seed

    [ for _ in 1..count do
          yield
              [ for _ in 0 .. rng.Next 6 do
                    yield (if rng.Next 2 = 0 then "a" else "b"), labels[rng.Next labels.Length], rng.Next 100 ] ]

let private recordProd (session: (string * string * int) list) : EffectCapture list =
    (([]: EffectCapture list), session)
    ||> List.fold (fun caps (eff, det, v) ->
        snd (EffectCapture.captureEffect StreamConfig.canonical OpStream.defaultHash encInt det eff (fun () -> v) caps))

let private toReqs (session: (string * string * int) list) : Chain.creq list =
    session
    |> List.map (fun (eff, det, v) ->
        { Chain.qeff = eff
          Chain.qdet = det
          Chain.qval = encInt v })

let private toModelCaptures (caps: EffectCapture list) : Chain.capture list =
    caps
    |> List.map (fun c ->
        { Chain.pseq = posOfInt c.Seq
          Chain.peff = c.Eff
          Chain.pdet = c.Determinism
          Chain.pval = c.Value
          Chain.pprev = c.PrevHash
          Chain.phash = c.Hash })

let private ofModelBreak (b: Chain.found<Chain.cbreak>) : (int * ChainBreakReason) option =
    match b with
    | Chain.Missing -> None
    | Chain.Found b -> Some(intOfPos b.cindex, ChainBreakReason.ofString b.creason)

/// Production's strict replay folded over a session — the values as the codec encodes them, or the
/// fault in the model's vocabulary.
let private replayProd (session: (string * string * int) list) (caps: EffectCapture list) : Chain.rsession =
    let rec go acc caps =
        function
        | [] -> Chain.RDone(List.rev acc, toModelCaptures caps)
        | (eff, det, v) :: rest ->
            match EffectCapture.replayEffectStrict decInt eff det (fun () -> v) caps with
            | Ok(x, caps') -> go (encInt x :: acc) caps' rest
            | Error(CaptureReplayFault.Exhausted e) -> Chain.RStopped(Chain.RExhausted e)
            | Error(CaptureReplayFault.IdentityMismatch(r, c)) -> Chain.RStopped(Chain.RIdentity(r, c))
            | Error(CaptureReplayFault.LabelMismatch(r, c)) -> Chain.RStopped(Chain.RLabel(r, c))
            | Error(CaptureReplayFault.LabelNotCanonical l) -> Chain.RStopped(Chain.RNotCanonical l)
            | Error(CaptureReplayFault.Undecodable e) -> failwithf "a recorded value did not decode: %s" e

    go [] caps session

/// The capture tally: the sessions recorded (`Journals`), the single-capture `Tampers` compared and
/// those production `Detected`, and the journals cut short of their last capture (`Truncated`) and
/// those whose strict replay stopped exhausted (`Exhausted`).
let private captureTally =
    Tally.empty [ "Journals"; "Tampers"; "Detected"; "Truncated"; "Exhausted" ]

let private captureDifferential (modelHash: HashFn) : Tally =
    let journal (session: (string * string * int) list) (t: Tally) : Tally =
        let caps = recordProd session

        let modelCaps =
            Chain.record_session modelHash showPos esc "" Chain.PZero (toReqs session)

        let walk (cs: EffectCapture list) =
            EffectCapture.firstCaptureBreak StreamConfig.canonical OpStream.defaultHash cs
            |> Option.map (fun b -> b.Index, b.Reason),
            ofModelBreak (Chain.first_capture_break_from modelHash showPos esc "" Chain.PZero (toModelCaptures cs))

        let p, m = walk caps

        let tamper (i: int, forge: EffectCapture -> EffectCapture) (t: Tally) : Tally =
            let forged = caps |> List.mapi (fun j c -> if j = i then forge c else c)
            let p, m = walk forged

            t
            |> Tally.bump "Tampers"
            |> Tally.bumpIf p.IsSome "Detected"
            |> Differential.check (p = m) (fun () -> sprintf "a tamper at %d: production %A, model %A" i p m)

        let tampers =
            [ for i in 0 .. List.length caps - 1 do
                  for forge in
                      [ (fun (c: EffectCapture) -> { c with Value = c.Value + "0" })
                        (fun c -> { c with Seq = c.Seq + 1 })
                        (fun c -> { c with PrevHash = c.PrevHash + "x" }) ] do
                      yield i, forge ]

        let canonical = EffectCapture.isDeterminismLabel
        let full = replayProd session caps
        let model = Chain.replay_session canonical (toReqs session) modelCaps

        let truncated (t: Tally) : Tally =
            match caps with
            | [] -> t
            | _ ->
                let cut = List.truncate (List.length caps - 1) caps
                let p = replayProd session cut
                let m = Chain.replay_session canonical (toReqs session) (toModelCaptures cut)

                let exhausted =
                    match p with
                    | Chain.RStopped(Chain.RExhausted _) -> true
                    | _ -> false

                t
                |> Tally.bump "Truncated"
                |> Tally.bumpIf exhausted "Exhausted"
                |> Differential.check (p = m) (fun () ->
                    sprintf "replay of a truncated journal: production %A, model %A" p m)

        t
        |> Tally.bump "Journals"
        |> Differential.check (toModelCaptures caps = modelCaps) (fun () ->
            sprintf "record: %A\n  production: %A\n  model:      %A" session caps modelCaps)
        |> Differential.check (p = m && p.IsNone) (fun () -> sprintf "an intact journal: production %A, model %A" p m)
        |> Differential.over tampers tamper
        |> Differential.check (full = model) (fun () -> sprintf "replay: production %A, model %A" full model)
        |> truncated

    Differential.over (sessions 3017 120) journal captureTally

[<Tests>]
let captureOracleTests =
    testList
        "Proofs.Oracle"
        [ testCase "the capture oracle agrees with production — record, walk, tamper and strict replay"
          <| fun _ ->
              let t = captureDifferential OpStream.defaultHash

              Differential.expectAgrees "the capture journal" t
              Expect.isGreaterThan (Tally.count "Journals" t) 100 "sessions recorded"

              Expect.equal
                  (Tally.count "Detected" t)
                  (Tally.count "Tampers" t)
                  "every single-capture tamper is detected, by both"

              Expect.isGreaterThan (Tally.count "Tampers" t) 100 "tampers compared"

              Expect.equal
                  (Tally.count "Exhausted" t)
                  (Tally.count "Truncated" t)
                  "every journal missing its last capture stops exhausted"

              Expect.isGreaterThan (Tally.count "Truncated" t) 50 "truncated journals compared"

          testCase "a capture model handed a DIFFERENT hash disagrees with production — the comparison can lose"
          <| fun _ ->
              let other: HashFn = fun prev payload -> OpStream.defaultHash ("x" + prev) payload
              Differential.expectLoses "a capture model under a wrong hash" (captureDifferential other) ]
