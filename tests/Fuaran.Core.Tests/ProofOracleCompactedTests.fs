module Fuaran.Core.Tests.ProofOracleCompactedTests

// Phase 301 — the bridge for `proofs/Chain.fst` sections 7b and 7c: the extracted model of the
// compacted stream (`append_to`, `compact_from`, `index_onto`) and of the capture journal
// (`record_session`, `first_capture_break_from`, `replay_session`) run beside production. A file of
// its own rather than more cases in `ProofOracleTests.fs`, which holds the other models' bridges; the
// ladder reads its case names beside that suite's (`ProofsLadderTests.realCases`).

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference.Counter

let rec private posOfInt (i: int) : Chain.pos =
    if i <= 0 then
        Chain.PZero
    else
        Chain.PSucc(posOfInt (i - 1))

let rec private intOfPos (p: Chain.pos) : int =
    match p with
    | Chain.PZero -> 0
    | Chain.PSucc m -> 1 + intOfPos m

let private showPos (p: Chain.pos) : string = string (intOfPos p)

let private toChainRecords (rs: OpRecord<'Op> list) : Chain.record<'Op> list =
    rs
    |> List.map (fun r ->
        { Chain.rseq = posOfInt r.Seq
          Chain.ractor = Actor.encode r.Actor
          Chain.rop = r.Op
          Chain.rprev = r.PrevHash
          Chain.rhash = r.Hash })

let private toModelSnapshot (s: Snapshot<'State>) : Chain.snapshot<'State> =
    { Chain.sseq = posOfInt s.Seq
      Chain.sstate = s.State
      Chain.sprev = s.PrevHash
      Chain.shash = s.Hash }

let private chainApply (w: StreamWitness<'Op, 'State, 'Rej>) (op: 'Op) (st: 'State) : Chain.applied<'State, 'Rej> =
    match w.Apply op st with
    | Ok s -> Chain.Applied s
    | Error e -> Chain.Refused e

let private stateEnc (s: int) = string s

let private modelPay (mode: SnapshotMode) : Chain.pos -> int -> string =
    match mode with
    | SnapshotMode.Strict -> Chain.snap_payload showPos stateEnc
    | SnapshotMode.ChainOnly -> Chain.snap_payload_chain_only showPos

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

              match OpStream.append OpStream.defaultHash witness (Human "w") op st rs with
              | Ok(st', rs') ->
                  st <- st'
                  rs <- rs'
              | Error _ -> ()

          yield st, rs ]

let private compactProd mode rs n =
    OpStream.Compacted.compactAt mode OpStream.canonicalConfig OpStream.defaultHash stateEnc witness keyOf 0 rs n

let private compactModel (hashFn: HashFn) mode rs n =
    Chain.compact hashFn showPos (modelPay mode) (chainApply witness) "" 0 (toChainRecords rs) (posOfInt n)

/// Every (n, k) pair of every stream in the pool, both modes, plus a STARVED snapshot (its state set
/// back to the initial one) so the prefix refusal is met: production's `compactFrom` against the
/// model's `compact_from`, and production's `compactFrom` against production's own `compactAt` at
/// `n + k` — the theorem held of production's values. Returns the first disagreement and the tally.
let private recompactionDifferential (modelHash: HashFn) =
    let mutable failure = None
    let mutable compared, refused, prefixRefused, composed = 0, 0, 0, 0

    let note why =
        if failure.IsNone then
            failure <- Some why

    for _, rs in streams 301 60 do
        let len = List.length rs

        for mode in [ SnapshotMode.Strict; SnapshotMode.ChainOnly ] do
            for n in 0..len do
                match compactProd mode rs n, compactModel modelHash mode rs n with
                | Ok c, Chain.Compacted(snap, tail) ->
                    for starved in [ false; true ] do
                        let c, snap =
                            if starved then
                                { c with
                                    Snapshot = { c.Snapshot with State = 0 } },
                                { snap with Chain.sstate = 0 }
                            else
                                c, snap

                        for k in 0 .. len - n + 1 do
                            let p =
                                ofProd (
                                    OpStream.Compacted.compactFrom OpStream.defaultHash stateEnc witness keyOf c (n + k)
                                )

                            let m =
                                ofModel (
                                    Chain.compact_from
                                        modelHash
                                        showPos
                                        (modelPay mode)
                                        (chainApply witness)
                                        snap
                                        tail
                                        (posOfInt k)
                                )

                            compared <- compared + 1

                            match p with
                            | RefusedWith e ->
                                refused <- refused + 1

                                if e.Contains "prefix replay" then
                                    prefixRefused <- prefixRefused + 1
                            | CompactedTo _ -> ()

                            if p <> m then
                                note (
                                    sprintf
                                        "mode=%A n=%d k=%d starved=%b\n  production: %A\n  model:      %A"
                                        mode
                                        n
                                        k
                                        starved
                                        p
                                        m
                                )

                            if not starved then
                                let whole = ofProd (compactProd mode rs (n + k))

                                if k <= len - n then
                                    composed <- composed + 1

                                if p <> whole then
                                    note (sprintf "compact_compose fails of production: mode=%A n=%d k=%d" mode n k)
                | p, m -> note (sprintf "the compactions disagree before composing: n=%d\n  %A\n  %A" n p m)

    failure, compared, refused, prefixRefused, composed

[<Tests>]
let compactedOracleTests =
    testList
        "Proofs.Oracle"
        [ testCase "the re-compaction oracle agrees with production at every (n, k), and production composes"
          <| fun _ ->
              let failure, compared, refused, prefixRefused, composed =
                  recompactionDifferential OpStream.defaultHash

              match failure with
              | Some why -> failtest why
              | None -> ()

              Expect.isGreaterThan compared 500 "every (n, k) pair over the pool was compared"
              Expect.isGreaterThan composed 200 "in-range compositions met"
              Expect.isGreaterThan refused 0 "out-of-range refusals met"
              Expect.isGreaterThan prefixRefused 0 "a prefix refusal met, by its origin index"

          testCase "a re-compaction model handed a DIFFERENT hash disagrees with production — the comparison can lose"
          <| fun _ ->
              let other: HashFn = fun prev payload -> OpStream.defaultHash ("x" + prev) payload
              let failure, _, _, _, _ = recompactionDifferential other
              Expect.isSome failure "a wrong hash must be caught"

          testCase "append after compaction: the oracle's appendTo mints production's record, at every boundary"
          <| fun _ ->
              let mutable compared = 0
              let mutable emptyTail = 0

              for st, rs in streams 3011 60 do
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

                  match OpStream.append OpStream.defaultHash witness (Human "w") (Inc 2) st rs with
                  | Error e -> failtestf "append refused: %A" e
                  | Ok(_, full) ->
                      Expect.equal (toChainRecords full) fullModel "the model's append is production's"

                      for n in 0..len do
                          match
                              compactProd SnapshotMode.Strict rs n,
                              compactModel OpStream.defaultHash SnapshotMode.Strict rs n
                          with
                          | Ok c, Chain.Compacted(snap, tail) ->
                              if List.isEmpty c.Tail then
                                  emptyTail <- emptyTail + 1

                              match
                                  OpStream.Compacted.appendTo
                                      OpStream.canonicalConfig
                                      OpStream.defaultHash
                                      witness
                                      (Human "w")
                                      (Inc 2)
                                      st
                                      c
                              with
                              | Ok(_, c') ->
                                  compared <- compared + 1

                                  Expect.equal
                                      (toChainRecords c'.Tail)
                                      (Chain.append_to
                                          OpStream.defaultHash
                                          showPos
                                          encode
                                          snap
                                          tail
                                          (Actor.encode (Human "w"))
                                          (Inc 2))
                                      (sprintf "appendTo at %d" n)

                                  Expect.equal
                                      (OpStream.Compacted.verify
                                          OpStream.canonicalConfig
                                          OpStream.defaultHash
                                          stateEnc
                                          witness
                                          c')
                                      (OpStream.verifyChain OpStream.defaultHash witness full)
                                      (sprintf "append_after_compact_verifies_iff, of production, at %d" n)
                              | Error e -> failtestf "appendTo refused: %A" e
                          | p, m -> failtestf "compactions disagree at %d: %A / %A" n p m

              Expect.isGreaterThan compared 200 "boundaries compared"
              Expect.isGreaterThan emptyTail 30 "the empty tail, where the defect lived, was met"

          testCase "the key-index oracle agrees with production, and the fold splits at every boundary"
          <| fun _ ->
              let mutable compared = 0

              for _, rs in streams 30111 60 do
                  let whole = Chain.index_onto modelKeyOf [] (toChainRecords rs)
                  Expect.equal (ofModelIndex whole) (ofProdIndex (KeyIndex.ofStream encode rs)) "ofStream"

                  for n in 0 .. List.length rs do
                      match compactProd SnapshotMode.ChainOnly rs n with
                      | Ok c ->
                          compared <- compared + 1

                          let modelSplit =
                              Chain.index_onto
                                  modelKeyOf
                                  (Chain.index_onto modelKeyOf [] (toChainRecords (List.truncate n rs)))
                                  (toChainRecords c.Tail)

                          Expect.equal (ofModelIndex modelSplit) (ofModelIndex whole) "key_index_rebuild_parity"

                          Expect.equal
                              (ofProdIndex (OpStream.Compacted.keyIndex keyOf c))
                              (ofModelIndex modelSplit)
                              (sprintf "Compacted.keyIndex at %d" n)
                      | Error e -> failtestf "compact failed: %A" e

              Expect.isGreaterThan compared 200 "boundaries compared" ]

// ---- the capture journal (section 7c) ----

let private esc (s: string) : string = Json.render (JStr s)

let private encInt (n: int) = Json.render (JInt n)

let private decInt (s: string) : Result<int, string> =
    Decode.parse s |> Result.bind (Decoder.describing Decoder.int)

let private labels =
    [ "clock"; "random"; "clock+random"; "network"; OpStream.deterministicTag ]

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
        snd (OpStream.captureEffect OpStream.defaultHash encInt det eff (fun () -> v) caps))

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
            match OpStream.replayEffectStrict decInt eff det (fun () -> v) caps with
            | Ok(x, caps') -> go (encInt x :: acc) caps' rest
            | Error(CaptureReplayFault.Exhausted e) -> Chain.RStopped(Chain.RExhausted e)
            | Error(CaptureReplayFault.IdentityMismatch(r, c)) -> Chain.RStopped(Chain.RIdentity(r, c))
            | Error(CaptureReplayFault.LabelMismatch(r, c)) -> Chain.RStopped(Chain.RLabel(r, c))
            | Error(CaptureReplayFault.LabelNotCanonical l) -> Chain.RStopped(Chain.RNotCanonical l)
            | Error(CaptureReplayFault.Undecodable e) -> failwithf "a recorded value did not decode: %s" e

    go [] caps session

let private captureDifferential (modelHash: HashFn) =
    let mutable failure = None
    let mutable journals, tampers, detected, truncated, exhausted = 0, 0, 0, 0, 0

    let note why =
        if failure.IsNone then
            failure <- Some why

    for session in sessions 3017 120 do
        let caps = recordProd session

        let modelCaps =
            Chain.record_session modelHash showPos esc "" Chain.PZero (toReqs session)

        journals <- journals + 1

        if toModelCaptures caps <> modelCaps then
            note (sprintf "record: %A\n  production: %A\n  model:      %A" session caps modelCaps)

        let walk (cs: EffectCapture list) =
            OpStream.firstCaptureBreak OpStream.defaultHash cs
            |> Option.map (fun b -> b.Index, b.Reason),
            ofModelBreak (Chain.first_capture_break_from modelHash showPos esc "" Chain.PZero (toModelCaptures cs))

        let p, m = walk caps

        if p <> m || p.IsSome then
            note (sprintf "an intact journal: production %A, model %A" p m)

        for i in 0 .. List.length caps - 1 do
            for tamper in
                [ (fun (c: EffectCapture) -> { c with Value = c.Value + "0" })
                  (fun c -> { c with Seq = c.Seq + 1 })
                  (fun c -> { c with PrevHash = c.PrevHash + "x" }) ] do
                let forged = caps |> List.mapi (fun j c -> if j = i then tamper c else c)
                let p, m = walk forged
                tampers <- tampers + 1

                if p.IsSome then
                    detected <- detected + 1

                if p <> m then
                    note (sprintf "a tamper at %d: production %A, model %A" i p m)

        let canonical = OpStream.isDeterminismLabel
        let full = replayProd session caps
        let model = Chain.replay_session canonical (toReqs session) modelCaps

        if full <> model then
            note (sprintf "replay: production %A, model %A" full model)

        match caps with
        | [] -> ()
        | _ ->
            let cut = List.truncate (List.length caps - 1) caps
            truncated <- truncated + 1
            let p = replayProd session cut
            let m = Chain.replay_session canonical (toReqs session) (toModelCaptures cut)

            match p with
            | Chain.RStopped(Chain.RExhausted _) -> exhausted <- exhausted + 1
            | _ -> ()

            if p <> m then
                note (sprintf "replay of a truncated journal: production %A, model %A" p m)

    failure, journals, tampers, detected, truncated, exhausted

[<Tests>]
let captureOracleTests =
    testList
        "Proofs.Oracle"
        [ testCase "the capture oracle agrees with production — record, walk, tamper and strict replay"
          <| fun _ ->
              let failure, journals, tampers, detected, truncated, exhausted =
                  captureDifferential OpStream.defaultHash

              match failure with
              | Some why -> failtest why
              | None -> ()

              Expect.isGreaterThan journals 100 "sessions recorded"
              Expect.equal detected tampers "every single-capture tamper is detected, by both"
              Expect.isGreaterThan tampers 100 "tampers compared"
              Expect.equal exhausted truncated "every journal missing its last capture stops exhausted"
              Expect.isGreaterThan truncated 50 "truncated journals compared"

          testCase "a capture model handed a DIFFERENT hash disagrees with production — the comparison can lose"
          <| fun _ ->
              let other: HashFn = fun prev payload -> OpStream.defaultHash ("x" + prev) payload
              let failure, _, _, _, _, _ = captureDifferential other
              Expect.isSome failure "a wrong hash must be caught" ]
