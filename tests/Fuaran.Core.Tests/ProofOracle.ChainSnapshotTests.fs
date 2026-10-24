module Fuaran.Core.Tests.ProofOracle.ChainSnapshotTests

// The Proofs.Oracle differential for proofs/Chain.fst, continued: snapshot and bounded replay (section 7) and the signed head (section 8), split from ProofOracle.ChainTests.fs for length; the shared walkers and renderers are read from there.

open System.IO
open Expecto



open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.FoldFixtures
open Fuaran.Core.Tests.ProofOracle.Bridges
open Fuaran.Core.Tests.ChainOracle
open Fuaran.Core.Tests.ProofOracle.ChainTests

// ---------------------------------------------------------------------------
//  Phase 191 — snapshot and bounded replay (`proofs/Chain.fst`, section 7)
//
//  The model's `compact` / `replay_from` / `verify_across` beside `SnapshotMatrix.compact`,
//  `compactChainOnly`, `replayFrom`, `verifyAcross` and `verifyAcrossChainOnly`, over generated
//  streams, EVERY boundary of each (the out-of-range one included), and every single-record tamper
//  of each — so the streams compared include ones that do not verify and ones that do not replay,
//  which is where the two theorems say something a green `snapshotLaws` run does not.
//
//  Two comparisons per compaction, and they are different things. The first is the ordinary one:
//  the extracted model against production, value for value. The second holds PRODUCTION to the
//  theorems' own statements — `replay = offset n (replayFrom snap tail)`, and
//  `verifyChain rs = (verifyChain prefix && verifyAcross snap tail)` — using the model only for
//  `offset`. A model that agreed with production while both drifted from the theorem would pass
//  the first and fail the second.
// ---------------------------------------------------------------------------

/// `OpStream.replay`'s result, as one value both sides are compared at.
type private ReplayVerdict<'State, 'Rej> =
    | ReplayedTo of 'State
    | HaltedAt of index: int * rejection: 'Rej

let private prodReplayVerdict (r: Result<'State, int * 'Rej>) : ReplayVerdict<'State, 'Rej> =
    match r with
    | Ok s -> ReplayedTo s
    | Error(i, e) -> HaltedAt(i, e)

let private modelReplayVerdict (r: Chain.replayed<'State, 'Rej>) : ReplayVerdict<'State, 'Rej> =
    match r with
    | Chain.Replayed s -> ReplayedTo s
    | Chain.Halted(i, e) -> HaltedAt(intOfPos i, e)

let private toModelReplayed (v: ReplayVerdict<'State, 'Rej>) : Chain.replayed<'State, 'Rej> =
    match v with
    | ReplayedTo s -> Chain.Replayed s
    | HaltedAt(i, e) -> Chain.Halted(posOfInt i, e)

/// The witness's reducer, as the model takes it — the ONLY thing section 7 asks of a domain.
let chainApply (w: StreamWitness<'Op, 'State, 'Rej>) (op: 'Op) (st: 'State) : Chain.applied<'State, 'Rej> =
    match w.Apply op st with
    | Ok s -> Chain.Applied s
    | Error e -> Chain.Refused e

/// Production's two compaction entry points. They differ in the snapshot's hash pre-image and in
/// nothing else, which is why the model takes the payload as a parameter.
let private prodCompact
    (mode: SnapshotMode)
    (hashFn: HashFn)
    (stateEnc: 'State -> string)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (rs: OpRecord<'Op> list)
    (n: int)
    : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
    match mode with
    | SnapshotMode.Strict -> SnapshotMatrix.compact hashFn stateEnc w state0 rs n
    | SnapshotMode.ChainOnly -> SnapshotMatrix.compactChainOnly hashFn w state0 rs n

let private prodVerifyAcross
    (mode: SnapshotMode)
    (hashFn: HashFn)
    (stateEnc: 'State -> string)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (snap: Snapshot<'State>)
    (tail: OpRecord<'Op> list)
    : bool =
    match mode with
    | SnapshotMode.Strict -> SnapshotMatrix.verifyAcross hashFn stateEnc w snap tail
    | SnapshotMode.ChainOnly -> SnapshotMatrix.verifyAcrossChainOnly hashFn w snap tail

let modelPay (mode: SnapshotMode) (stateEnc: 'State -> string) : Chain.pos -> 'State -> string =
    match mode with
    | SnapshotMode.Strict -> Chain.snap_payload showPos stateEnc
    | SnapshotMode.ChainOnly -> Chain.snap_payload_chain_only showPos

let toModelSnapshot (s: Snapshot<'State>) : Chain.snapshot<'State> =
    { Chain.sseq = posOfInt s.Seq
      Chain.sstate = s.State
      Chain.sprev = s.PrevHash
      Chain.shash = s.Hash }

/// A compaction's whole outcome: the refusal's MESSAGE, or every field of the snapshot and the tail
/// record for record. Compared at once, so a model that snapshotted the right state under the wrong
/// boundary hash is as red as one that refused.
type private CompactVerdict<'Op, 'State> =
    | CompactedTo of seq: int * state: 'State * prevHash: string * hash: string * tail: Chain.record<'Op> list
    | CompactRefusedWith of string

let private prodCompactVerdict
    (r: Result<Snapshot<'State> * OpRecord<'Op> list, string>)
    : CompactVerdict<'Op, 'State> =
    match r with
    | Ok(s, tail) -> CompactedTo(s.Seq, s.State, s.PrevHash, s.Hash, toChainRecords tail)
    | Error e -> CompactRefusedWith e

let private modelCompactVerdict (r: Chain.compacted<'Op, 'State>) : CompactVerdict<'Op, 'State> =
    match r with
    | Chain.Compacted(s, tail) -> CompactedTo(intOfPos s.sseq, s.sstate, s.sprev, s.shash, tail)
    | Chain.CompactRefused e -> CompactRefusedWith e

/// The snapshot tally. `Compactions` compared, and the two refusal classes among them
/// (`OutOfRange`, `PrefixRefused`). Bounded replays that reached a state (`Accepted`), and ones that
/// HALTED in the tail at a boundary past zero (`HaltedPastZero`) — the only place the theorem's
/// `offset` is observable. Boundaries production verified across (`VerifiedAcross`), and ones it
/// rejected (`RejectedAcross`). `PrefixTamperUnseen`: compactions that verify across although the
/// ORIGINAL does not — a tamper in the discarded prefix, `compact_verifies_iff_original`'s premise
/// observed being load-bearing. `TailTampers` of a compacted tail, and how many production's
/// boundary walker found (`TailDetected`).
let private snapshotTally =
    Tally.empty
        [ "Compactions"
          "OutOfRange"
          "PrefixRefused"
          "Accepted"
          "HaltedPastZero"
          "VerifiedAcross"
          "RejectedAcross"
          "PrefixTamperUnseen"
          "TailTampers"
          "TailDetected" ]

let private snapshotDifferential
    (label: string)
    (prodHash: HashFn)
    (modelHash: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (stateEnc: 'State -> string)
    (otherOp: 'Op -> 'Op)
    (gen: LaneGen<'Op, 'State>)
    (seed: int)
    (iterations: int)
    : Tally =
    let apply = chainApply w

    let step (_: int) (rng: ConfRng.T) (t: Tally) =
        let lanes, r' = gen.Lanes 2 rng
        let ops = List.concat lanes
        let intact = chainUnder prodHash w gen.State0 (Human "writer") ops

        let where (what: string) (mode: SnapshotMode) (n: int) =
            sprintf
                "%s: seed=%d stream=%s mode=%A atSeq=%d ops=[%s]"
                label
                seed
                what
                mode
                n
                (ops |> List.map w.Encode |> String.concat "; ")

        let across (mode: SnapshotMode) (snap: Snapshot<'State>) (tail: OpRecord<'Op> list) : bool * bool =
            prodVerifyAcross mode prodHash stateEnc w snap tail,
            Chain.verify_across
                modelHash
                showPos
                w.Encode
                (modelPay mode stateEnc)
                (toModelSnapshot snap)
                (toChainRecords tail)

        let compareAt (what: string, rs: OpRecord<'Op> list, mode: SnapshotMode, n: int) (t: Tally) : Tally =
            let here = where what mode n
            let p = prodCompact mode prodHash stateEnc w gen.State0 rs n

            let m =
                Chain.compact
                    modelHash
                    showPos
                    (modelPay mode stateEnc)
                    apply
                    StreamConfig.canonical.Genesis
                    gen.State0
                    (toChainRecords rs)
                    (posOfInt n)

            let pv, mv = prodCompactVerdict p, modelCompactVerdict m
            let pOrigin = prodReplayVerdict (OpStream.replay w gen.State0 rs)
            let mOrigin = modelReplayVerdict (Chain.replay apply gen.State0 (toChainRecords rs))

            let t =
                t
                |> Tally.bump "Compactions"
                |> Differential.check (pv = mv) (fun () ->
                    sprintf "%s\n  compact, production: %A\n  compact, model:      %A" here pv mv)
                |> Differential.check (pOrigin = mOrigin) (fun () ->
                    sprintf "%s\n  replay, production: %A\n  replay, model:      %A" here pOrigin mOrigin)

            match p with
            | Error _ when n > List.length rs -> Tally.bump "OutOfRange" t
            | Error _ ->
                // `compact_refusal_is_the_origins`, held of production: an in-range refusal is the
                // origin's own halt, inside the prefix.
                let theOrigins =
                    match pOrigin with
                    | HaltedAt(i, _) -> i < n
                    | ReplayedTo _ -> false

                t
                |> Tally.bump "PrefixRefused"
                |> Differential.check theOrigins (fun () ->
                    sprintf "%s\n  compact refused an in-range boundary, but the origin replay is %A" here pOrigin)
            | Ok(snap, tail) ->
                let pFrom = prodReplayVerdict (SnapshotMatrix.replayFrom w snap tail)

                let mFrom =
                    modelReplayVerdict (Chain.replay_from apply (toModelSnapshot snap) (toChainRecords tail))

                // `replay_from_snapshot_eq`, held of PRODUCTION's two values.
                let shifted = modelReplayVerdict (Chain.offset (posOfInt n) (toModelReplayed pFrom))
                let pAcross, mAcross = across mode snap tail

                // `compact_preserves_verify`, held of PRODUCTION's three values.
                let whole = OpStream.verifyChain (OpStream.canonical prodHash w) rs

                let prefix =
                    OpStream.verifyChain (OpStream.canonical prodHash w) (List.truncate n rs)

                let tailTamper (tamper: string, tail': OpRecord<'Op> list) (t: Tally) : Tally =
                    let pT, mT = across mode snap tail'

                    t
                    |> Tally.bump "TailTampers"
                    |> Differential.check (pT = mT) (fun () ->
                        sprintf
                            "%s tail-tamper=%s\n  verifyAcross, production: %b\n  verify_across, model:    %b"
                            here
                            tamper
                            pT
                            mT)
                    |> Tally.bumpIf (not pT) "TailDetected"

                let replayed (t: Tally) : Tally =
                    match pFrom with
                    | ReplayedTo _ -> Tally.bump "Accepted" t
                    | HaltedAt _ when n > 0 -> Tally.bump "HaltedPastZero" t
                    | HaltedAt _ -> t

                let t =
                    t
                    |> Differential.check (pFrom = mFrom) (fun () ->
                        sprintf "%s\n  replayFrom, production: %A\n  replayFrom, model:      %A" here pFrom mFrom)
                    |> Differential.check (shifted = pOrigin) (fun () ->
                        sprintf
                            "%s\n  replay from the origin:                %A\n  replayFrom, read at the origin's index: %A"
                            here
                            pOrigin
                            shifted)
                    |> replayed
                    |> Differential.check (pAcross = mAcross) (fun () ->
                        sprintf
                            "%s\n  verifyAcross, production: %b\n  verify_across, model:    %b"
                            here
                            pAcross
                            mAcross)
                    |> Differential.check (whole = (prefix && pAcross)) (fun () ->
                        sprintf
                            "%s\n  verifyChain whole=%b, but verifyChain prefix=%b and verifyAcross=%b"
                            here
                            whole
                            prefix
                            pAcross)
                    |> Tally.bumpIf pAcross "VerifiedAcross"
                    |> Tally.bumpIf (pAcross && not whole) "PrefixTamperUnseen"
                    |> Tally.bumpIf (not pAcross) "RejectedAcross"

                // Every tamper of the compacted TAIL — of the intact stream only, so a detection
                // here is a detection of THIS tamper and not of one the stream already carried.
                if what = "none" then
                    Differential.over (chainTampers otherOp tail) tailTamper t
                else
                    t

        let cases =
            [ if not (List.isEmpty intact) then
                  for (what, rs) in ("none", intact) :: chainTampers otherOp intact do
                      for mode in [ SnapshotMode.Strict; SnapshotMode.ChainOnly ] do
                          for n in 0 .. List.length rs + 1 do
                              yield what, rs, mode, n ]

        r', Differential.over cases compareAt t

    Differential.seeded iterations (ConfRng.ofSeed seed) step snapshotTally

let private expectSnapshotAgreement (label: string) (t: Tally) =
    Differential.expectAgrees label t
    // Every class the two theorems speak about has to have been MET, or the agreement above is
    // about less than it claims.
    Expect.isGreaterThan (Tally.count "Accepted" t) 0 (sprintf "%s: no bounded replay reached a state" label)

    Expect.isGreaterThan
        (Tally.count "HaltedPastZero" t)
        0
        (sprintf "%s: no bounded replay halted past boundary zero, so the index offset was never observable" label)

    Expect.isGreaterThan (Tally.count "OutOfRange" t) 0 (sprintf "%s: no out-of-range boundary was compared" label)

    Expect.isGreaterThan (Tally.count "PrefixRefused" t) 0 (sprintf "%s: no compaction was refused on its prefix" label)

    Expect.isGreaterThan (Tally.count "VerifiedAcross" t) 0 (sprintf "%s: no boundary verified" label)
    Expect.isGreaterThan (Tally.count "RejectedAcross" t) 0 (sprintf "%s: no boundary was rejected" label)

    Expect.isGreaterThan
        (Tally.count "PrefixTamperUnseen" t)
        0
        (sprintf "%s: no prefix tamper was compacted away, so the corollary's premise was never exercised" label)

    Expect.isGreaterThan (Tally.count "TailTampers" t) 0 (sprintf "%s: no tail tamper was compared" label)

    Expect.isGreaterThan
        (Tally.count "TailDetected" t)
        0
        (sprintf "%s: every tail tamper went undetected, so the agreement is vacuous" label)

// ---------------------------------------------------------------------------
//  Phase 193 — the signed head (`proofs/Chain.fst`, section 8)
//
//  `OpStream.head`, `attestHead` and `verifyAttestation` beside the extracted model, over
//  generated chains, generated KEYRINGS, and the three splices the theorem names — each followed
//  by a full RE-MINT, so that `verifyChain` accepts the result and only the signature can refuse
//  it. That re-mint is the point: it is the tamper sections 1-7 of the model cannot see.
//
//  CORE SHIPS NO PRODUCTION SIGNER. `OpStream.noAttestation` signs nothing, and a real sink is
//  host-side. So "beside production signing" means beside the `IAttestationSink` SEAM, driven by
//  the test-local keyring sink below: what is compared is the model against the seam's contract
//  (`head`, `attestHead`, `verifyAttestation`, and the `verifyChain && verifyAttestation`
//  composition `verifyAttestation`'s doc comment describes), never against a signer.
// ---------------------------------------------------------------------------

/// A test-local keyring: named secrets, and the one a signer signs with. NOT cryptography — the
/// "signature" is the default hash keyed by the secret — and deliberately one whose `Verify` does
/// NOT compare the attestation's recorded `Head`, so that `signature_binds` is spent on the keyed
/// digest rather than satisfied by construction.
type private Keyring =
    { Keys: (string * string) list
      Active: string }

let private keyringSink (ring: Keyring) : IAttestationSink =
    let secretOf (keyId: string) =
        ring.Keys |> List.tryFind (fun (k, _) -> k = keyId) |> Option.map snd

    { new IAttestationSink with
        member _.Sign head =
            secretOf ring.Active
            |> Option.map (fun secret ->
                { Head = head
                  KeyId = ring.Active
                  Signature = OpStream.defaultHash secret head })

        member _.Verify att head =
            match secretOf att.KeyId with
            | Some secret -> att.Signature = OpStream.defaultHash secret head
            | None -> false }

/// A sink for which `signature_binds` is FALSE: it verifies every attestation against every head.
let private promiscuousSink: IAttestationSink =
    { new IAttestationSink with
        member _.Sign head =
            Some
                { Head = head
                  KeyId = "any"
                  Signature = "yes" }

        member _.Verify _ _ = true }

let private toModelAtt (a: Attestation) : Chain.attestation =
    { Chain.ahead = a.Head
      Chain.akey = a.KeyId
      Chain.asig = a.Signature }

let private ofModelAtt (a: Chain.attestation) : Attestation =
    { Head = a.ahead
      KeyId = a.akey
      Signature = a.asig }

/// The sink's own `Sign` and `Verify`, handed to the model as the two parameters it takes.
let private modelSign (sink: IAttestationSink) (head: string) : Chain.found<Chain.attestation> =
    match sink.Sign head with
    | Some a -> Chain.Found(toModelAtt a)
    | None -> Chain.Missing

let private modelVerify (sink: IAttestationSink) (att: Chain.attestation) (head: string) : bool =
    sink.Verify (ofModelAtt att) head

/// A full re-mint under production's own canonical payload and genesis: every record's sequence,
/// prev-link and hash recomputed from the steps, so `verifyChain` accepts whatever it is handed.
/// The shape of `Conformance.attestationLawsAt`'s forgery, spelled here over a step list because a
/// splice changes the chain's LENGTH, which a record-for-record rehash cannot.
let private remint (hashFn: HashFn) (encode: 'Op -> string) (steps: (Actor * 'Op) list) : OpRecord<'Op> list =
    (([], StreamConfig.canonical.Genesis, 0), steps)
    ||> List.fold (fun (acc, prev, i) (actor, op) ->
        let h = hashFn prev (StreamConfig.canonical.Payload i actor (encode op))

        { Seq = i
          Actor = actor
          Op = op
          PrevHash = prev
          Hash = h }
        :: acc,
        h,
        i + 1)
    |> fun (acc, _, _) -> List.rev acc

/// One splice, spelled twice and INDEPENDENTLY: as the model's `splice`, and as a plain list edit
/// of production's steps. The differential requires the two to mint the same records.
type private SpliceCase<'Op> =
    { Name: string
      Model: Chain.splice<'Op>
      Steps: (Actor * 'Op) list }

let private spliceCases (otherOp: 'Op -> 'Op) (steps: (Actor * 'Op) list) : SpliceCase<'Op> list =
    let n = List.length steps

    let toStep (a: Actor, o: 'Op) : Chain.cstep<'Op> =
        { Chain.cactor = Actor.encode a
          Chain.cop = o }

    let mallory = Human "mallory"

    [ for i in 0 .. n - 1 do
          let a, o = List.item i steps

          let put (s: Actor * 'Op) =
              steps |> List.mapi (fun j x -> if j = i then s else x)

          // An op replaced; a record re-attributed; and a replacement by the SAME step, which is
          // no splice at all and must stay accepted.
          yield
              { Name = sprintf "replace-op@%d" i
                Model = Chain.Replaced(posOfInt i, toStep (a, otherOp o))
                Steps = put (a, otherOp o) }

          yield
              { Name = sprintf "replace-actor@%d" i
                Model = Chain.Replaced(posOfInt i, toStep (mallory, o))
                Steps = put (mallory, o) }

          yield
              { Name = sprintf "replace-same@%d" i
                Model = Chain.Replaced(posOfInt i, toStep (a, o))
                Steps = steps }

          yield
              { Name = sprintf "drop@%d" i
                Model = Chain.Dropped(posOfInt i)
                Steps = List.removeAt i steps }

      // An insertion at every position, the end and one PAST the end included — the model appends
      // there, and so does this.
      for i in 0 .. n + 1 do
          let inserted = mallory, otherOp (snd (List.item (min i (n - 1)) steps))

          yield
              { Name = sprintf "insert@%d" i
                Model = Chain.Inserted(posOfInt i, toStep inserted)
                Steps = List.insertAt (min i n) inserted steps }

      // Out of range, a replacement and a removal change nothing.
      yield
          { Name = "replace-out-of-range"
            Model = Chain.Replaced(posOfInt (n + 2), toStep (mallory, otherOp (snd (List.head steps))))
            Steps = steps }

      yield
          { Name = "drop-out-of-range"
            Model = Chain.Dropped(posOfInt (n + 2))
            Steps = steps } ]

/// The signed-head tally. Chains `Signed`, and keyrings drawn with more than one key (`MultiKey`).
/// A verifier whose ring lacks the signing key (`UnknownKeyRefused`), and an attestation re-labelled
/// to another key of the same ring (`WrongKeyRefused`): both refused, on the intact chain. Re-minted
/// `Splices` compared; those the WALKER accepted (`WalkerBlind` — all of them, or the re-mint is not
/// one); those the signature `Refused`, by class (`ReplacedRefused`, `InsertedRefused`,
/// `DroppedRefused`); and the no-op ones that stayed accepted (`NoOpAccepted`). `InPlace` tampers —
/// no re-mint — which the walker finds before the signature is asked (`InPlaceRefused`).
let private signedTally =
    Tally.empty
        [ "Signed"
          "MultiKey"
          "UnknownKeyRefused"
          "WrongKeyRefused"
          "Splices"
          "WalkerBlind"
          "Refused"
          "NoOpAccepted"
          "ReplacedRefused"
          "InsertedRefused"
          "DroppedRefused"
          "InPlace"
          "InPlaceRefused" ]

let private drawKeyring (rng: ConfRng.T) : Keyring * ConfRng.T =
    let count, r1 = ConfRng.intBelow 3 rng
    let active, r2 = ConfRng.intBelow (count + 1) r1
    let salt, r3 = ConfRng.intBelow 1000000 r2

    { Keys = [ for k in 0..count -> sprintf "key-%d" k, sprintf "secret-%d-%d" k salt ]
      Active = sprintf "key-%d" active },
    r3

let private signedHeadDifferential
    (label: string)
    (prodHash: HashFn)
    (modelHash: HashFn)
    (sinkOf: Keyring -> IAttestationSink)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (otherOp: 'Op -> 'Op)
    (gen: LaneGen<'Op, 'State>)
    (seed: int)
    (iterations: int)
    : Tally =
    let writer = Human "writer"

    /// One intact, non-empty chain under one keyring: its head, its signature, and every re-minted
    /// splice and in-place tamper of it.
    let probeChain (ring: Keyring) (sink: IAttestationSink) (intact: OpRecord<'Op> list) (t: Tally) : Tally =
        let steps = intact |> List.map (fun r -> r.Actor, r.Op)

        let cs: Chain.cstep<'Op> list =
            steps
            |> List.map (fun (a, o) ->
                { Chain.cactor = Actor.encode a
                  Chain.cop = o })

        let here (what: string) =
            sprintf
                "%s: seed=%d %s keys=%d active=%s ops=[%s]"
                label
                seed
                what
                (List.length ring.Keys)
                ring.Active
                (steps |> List.map (snd >> w.Encode) |> String.concat "; ")

        // `OpStream.head` against `chain_head`, and the premise the theorem carries about it.
        let pHead = OpStream.head StreamConfig.canonical intact
        let mHead = Chain.chain_head (toChainRecords intact)

        // The model's re-mint is production's `append`, record for record.
        let mBuilt = Chain.build_chain modelHash showPos w.Encode "" Chain.PZero cs

        // `attestHead` against `attest_head`.
        let pAtt = Attestation.attestHead sink StreamConfig.canonical intact
        let mAtt = Chain.attest_head (modelSign sink) (toChainRecords intact)

        let attested =
            match pAtt, mAtt with
            | Some p, Chain.Found m -> toModelAtt p = m
            | None, Chain.Missing -> true
            | _ -> false

        let t =
            t
            |> Differential.check (pHead = mHead) (fun () ->
                sprintf "%s\n  head, production: %s\n  chain_head, model: %s" (here "intact") pHead mHead)
            |> Differential.check (pHead <> "") (fun () ->
                sprintf "%s\n  a non-empty chain's head is the empty-chain sentinel" (here "intact"))
            |> Differential.check (mBuilt = toChainRecords intact) (fun () ->
                sprintf "%s\n  build_chain does not mint the records production appended" (here "intact"))
            |> Differential.check (remint prodHash w.Encode steps = intact) (fun () ->
                sprintf "%s\n  the test's own re-mint does not reproduce production's append" (here "intact"))
            |> Differential.check attested (fun () ->
                sprintf "%s\n  attestHead, production: %A\n  attest_head, model:    %A" (here "intact") pAtt mAtt)

        match pAtt with
        | None -> t
        | Some att ->
            let accepts (verifier: IAttestationSink) (a: Attestation) (rs: OpRecord<'Op> list) : bool * bool =
                (OpStream.verifyChain (OpStream.canonical prodHash w) rs
                 && Attestation.verifyAttestation verifier StreamConfig.canonical a rs),
                Chain.accepts_signed
                    modelHash
                    showPos
                    w.Encode
                    ""
                    (modelVerify verifier)
                    (toModelAtt a)
                    (toChainRecords rs)

            /// Production's verdict, after recording any disagreement with the model's.
            let agree (what: string) (p: bool, m: bool) (t: Tally) : bool * Tally =
                p,
                t
                |> Differential.check (p = m) (fun () ->
                    sprintf "%s\n  accepted, production: %b\n  accepts_signed, model: %b" (here what) p m)

            let t =
                t |> Tally.bump "Signed" |> Tally.bumpIf (List.length ring.Keys > 1) "MultiKey"

            // The round trip: the chain that was signed is accepted.
            let accepted, t = agree "intact" (accepts sink att intact) t

            let t =
                Differential.check
                    accepted
                    (fun () ->
                        sprintf "%s\n  the chain that was signed is refused under its own attestation" (here "intact"))
                    t

            // KEYRINGS. A verifier that does not hold the signing key refuses; so does the same
            // signature re-labelled to another key of the ring.
            let stranger =
                sinkOf
                    { ring with
                        Keys = ring.Keys |> List.filter (fun (k, _) -> k <> att.KeyId) }

            let accepted, t = agree "unknown-key" (accepts stranger att intact) t
            let t = Tally.bumpIf (not accepted) "UnknownKeyRefused" t

            let relabelled (other: string, _: string) (t: Tally) : Tally =
                let accepted, t =
                    agree ("relabelled-to-" + other) (accepts sink { att with KeyId = other } intact) t

                Tally.bumpIf (not accepted) "WrongKeyRefused" t

            let t =
                Differential.over (ring.Keys |> List.filter (fun (k, _) -> k <> att.KeyId)) relabelled t

            // THE SPLICES, each re-minted.
            let splice (sp: SpliceCase<'Op>) (t: Tally) : Tally =
                let pForged = remint prodHash w.Encode sp.Steps

                let mForged =
                    Chain.build_chain modelHash showPos w.Encode "" Chain.PZero (Chain.apply_splice cs sp.Model)

                let changes = Chain.splice_changes cs sp.Model

                let t =
                    t
                    |> Differential.check (toChainRecords pForged = mForged) (fun () ->
                        sprintf "%s\n  apply_splice + build_chain does not mint the production forgery" (here sp.Name))
                    |> Differential.check (changes = (sp.Steps <> steps)) (fun () ->
                        sprintf
                            "%s\n  splice_changes says %b, but the production steps %s"
                            (here sp.Name)
                            changes
                            (if sp.Steps <> steps then "moved" else "did not move"))

                let walker = OpStream.verifyChain (OpStream.canonical prodHash w) pForged
                let accepted, t = agree sp.Name (accepts sink att pForged) t
                let refused = changes && not accepted

                t
                |> Tally.bump "Splices"
                |> Tally.bumpIf walker "WalkerBlind"
                |> Differential.check walker (fun () ->
                    sprintf
                        "%s\n  the re-mint does not verify, so it is not the tamper the theorem is about"
                        (here sp.Name))
                // `signed_head_rejects_splice`, held of PRODUCTION's own verdict.
                |> Differential.check (not (changes && accepted)) (fun () ->
                    sprintf "%s\n  a re-minted splice is ACCEPTED under the original attestation" (here sp.Name))
                |> Differential.check (changes || accepted) (fun () ->
                    sprintf "%s\n  a splice that changes nothing is refused" (here sp.Name))
                |> Tally.bumpIf refused "Refused"
                |> Tally.bumpIf
                    (refused
                     && (match sp.Model with
                         | Chain.Replaced _ -> true
                         | _ -> false))
                    "ReplacedRefused"
                |> Tally.bumpIf
                    (refused
                     && (match sp.Model with
                         | Chain.Inserted _ -> true
                         | _ -> false))
                    "InsertedRefused"
                |> Tally.bumpIf
                    (refused
                     && (match sp.Model with
                         | Chain.Dropped _ -> true
                         | _ -> false))
                    "DroppedRefused"
                |> Tally.bumpIf (not changes && accepted) "NoOpAccepted"

            let t = Differential.over (spliceCases otherOp steps) splice t

            // In place, with NO re-mint: the walker's own business, and the composition must
            // still agree.
            let inPlace (tamper: string, rs': OpRecord<'Op> list) (t: Tally) : Tally =
                let t = Tally.bump "InPlace" t
                let accepted, t = agree ("in-place " + tamper) (accepts sink att rs') t
                Tally.bumpIf (not accepted) "InPlaceRefused" t

            Differential.over (chainTampers otherOp intact) inPlace t

    let step (_: int) (rng: ConfRng.T) (t: Tally) =
        let lanes, r' = gen.Lanes 2 rng
        let ring, r'' = drawKeyring r'
        let sink = sinkOf ring
        let intact = chainUnder prodHash w gen.State0 writer (List.concat lanes)

        r'',
        (if List.isEmpty intact then
             t
         else
             probeChain ring sink intact t)

    Differential.seeded iterations (ConfRng.ofSeed seed) step signedTally

let private expectSignedAgreement (label: string) (t: Tally) =
    Differential.expectAgrees label t
    let count name = Tally.count name t
    // Every class the two theorems speak about has to have been MET.
    Expect.isGreaterThan (count "Signed") 0 (sprintf "%s: no chain was signed" label)
    Expect.isGreaterThan (count "MultiKey") 0 (sprintf "%s: no keyring held more than one key" label)

    Expect.isGreaterThan (count "UnknownKeyRefused") 0 (sprintf "%s: no verifier lacking the key was compared" label)

    Expect.isGreaterThan (count "WrongKeyRefused") 0 (sprintf "%s: no re-labelled attestation was compared" label)
    Expect.isGreaterThan (count "Splices") 0 (sprintf "%s: no splice was compared" label)

    Expect.equal
        (count "WalkerBlind")
        (count "Splices")
        (sprintf "%s: a re-minted splice failed verifyChain, so it was not the rewrite the theorem is about" label)

    Expect.isGreaterThan (count "ReplacedRefused") 0 (sprintf "%s: no replaced op was refused" label)
    Expect.isGreaterThan (count "InsertedRefused") 0 (sprintf "%s: no inserted op was refused" label)
    Expect.isGreaterThan (count "DroppedRefused") 0 (sprintf "%s: no dropped op was refused" label)

    Expect.isGreaterThan
        (count "NoOpAccepted")
        0
        (sprintf "%s: no splice that changes nothing was accepted, so `splice_changes` was never load-bearing" label)

    Expect.isGreaterThan (count "InPlace") 0 (sprintf "%s: no in-place tamper was compared" label)
    Expect.equal (count "InPlaceRefused") (count "InPlace") (sprintf "%s: an in-place tamper was accepted" label)

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [ testCase
              "the snapshot oracle agrees with production over the work-plan stream, every boundary and every tamper"
          <| fun _ ->
              snapshotDifferential
                  "work-plan snapshots"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  planW
                  planHash
                  tamperedPlanOp
                  planLaneGen
                  3700
                  40
              |> expectSnapshotAgreement "work-plan snapshots"

          testCase
              "the snapshot oracle agrees with production over the reference witness's stream, every boundary and every tamper"
          <| fun _ ->
              snapshotDifferential
                  "reference snapshots"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  treeW
                  prodTreeHash
                  (fun _ -> RemoveNode "tampered-node")
                  treeLaneGen
                  3710
                  30
              |> expectSnapshotAgreement "reference snapshots"

          testCase "a snapshot model handed a DIFFERENT hash disagrees with production — the comparison can lose"
          <| fun _ ->
              // The go-red for the whole differential: nothing about the streams moves, only the
              // function the model mints the snapshot's hash and walks the tail with.
              let t =
                  snapshotDifferential
                      "perturbed snapshots"
                      OpStream.defaultHash
                      swappedHash
                      planW
                      planHash
                      tamperedPlanOp
                      planLaneGen
                      3720
                      3

              Differential.expectLoses "a snapshot model hashing with a different function" t

              // And it is caught where it should be: on one intact stream, production verifies
              // across its own compaction and the perturbed model does not.
              let rs =
                  chainUnder
                      OpStream.defaultHash
                      planW
                      planLaneGen.State0
                      (Human "writer")
                      [ AddItem("s1", "one"); AddItem("s2", "two"); Retitle("s1", "uno") ]

              match SnapshotMatrix.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs 1 with
              | Error e -> failtestf "compact refused an intact stream: %s" e
              | Ok(snap, tail) ->
                  Expect.isTrue
                      (SnapshotMatrix.verifyAcross OpStream.defaultHash planHash planW snap tail)
                      "production verifies across its own boundary"

                  Expect.isFalse
                      (Chain.verify_across
                          swappedHash
                          showPos
                          planW.Encode
                          (Chain.snap_payload showPos planHash)
                          (toModelSnapshot snap)
                          (toChainRecords tail))
                      "and a model recomputing with a different hash does not"

          testCase
              "replayFrom renumbers a halt from ZERO — the offset in replay_from_snapshot_eq is production's, not the model's"
          <| fun _ ->
              // The theorem is `replay = offset n (replayFrom snap tail)`, and the offset is the
              // part a reader would drop. Shown load-bearing on production alone: a stream whose
              // third op rejects, compacted after its first.
              let built =
                  chainUnder
                      OpStream.defaultHash
                      planW
                      planLaneGen.State0
                      (Human "writer")
                      [ AddItem("o1", "one"); AddItem("o2", "two"); Retitle("o1", "uno") ]

              let rs =
                  built
                  |> List.mapi (fun i r ->
                      if i = 2 then
                          { r with
                              Op = Retitle("missing", "uno") }
                      else
                          r)

              match SnapshotMatrix.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs 1 with
              | Error e -> failtestf "the prefix replays, so compact must not refuse: %s" e
              | Ok(snap, tail) ->
                  let origin = prodReplayVerdict (OpStream.replay planW planLaneGen.State0 rs)
                  let bounded = prodReplayVerdict (SnapshotMatrix.replayFrom planW snap tail)
                  Expect.equal origin (HaltedAt(2, "no item missing")) "the origin halts at the third record"

                  Expect.equal
                      bounded
                      (HaltedAt(1, "no item missing"))
                      "and replayFrom reports the same halt at ITS index"

                  Expect.notEqual bounded origin "so the unqualified equality is false of production"

                  Expect.equal
                      (modelReplayVerdict (Chain.offset (posOfInt 1) (toModelReplayed bounded)))
                      origin
                      "and the theorem's offset is exactly what separates them"

          testCase "a tamper in the DISCARDED prefix verifies across — compact trusts the boundary hash it reads"
          <| fun _ ->
              // `compact_verifies_iff_original` carries a premise — the prefix verified — and this
              // is what it is for. The op at sequence zero is changed, the stream no longer
              // verifies, and its compaction at two verifies across all the same: to production and
              // to the model alike. Once the prefix is gone nothing can find it. Verify, then compact.
              let built =
                  chainUnder
                      OpStream.defaultHash
                      planW
                      planLaneGen.State0
                      (Human "writer")
                      [ AddItem("k1", "one"); AddItem("k2", "two"); AddItem("k3", "three") ]

              // The probe built the thing it claims to be about: a rejected `AddItem` chains nothing,
              // and a tamper of an empty stream is no tamper.
              Expect.hasLength built 3 "all three appends were accepted"

              let rs =
                  built
                  |> List.mapi (fun i r ->
                      if i = 0 then
                          { r with
                              Op = AddItem("k1", "TAMPERED") }
                      else
                          r)

              Expect.isFalse
                  (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash planW) rs)
                  "the original does not verify"

              match SnapshotMatrix.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs 2 with
              | Error e -> failtestf "the tampered prefix still replays, so compact does not refuse: %s" e
              | Ok(snap, tail) ->
                  Expect.isTrue
                      (SnapshotMatrix.verifyAcross OpStream.defaultHash planHash planW snap tail)
                      "production verifies across the boundary of a stream that does not verify"

                  Expect.isTrue
                      (Chain.verify_across
                          OpStream.defaultHash
                          showPos
                          planW.Encode
                          (Chain.snap_payload showPos planHash)
                          (toModelSnapshot snap)
                          (toChainRecords tail))
                      "and so does the model — the split theorem says exactly this"

                  Expect.isFalse
                      (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash planW) (List.truncate 2 rs))
                      "the prefix is where the break is, which is the other conjunct of the split"

          testCase
              "under a NON-EMPTY genesis a compaction at sequence zero verifies across — compactWith carries the configured genesis"
          <| fun _ ->
              // Phase 227 closed Phase 191's finding. `compact_at_zero_verifies_under_any_genesis`,
              // measured on production: a stream appended under a config whose genesis is not `""`,
              // compacted through `compactWith` / `compactChainOnlyWith` with that config, verifies
              // across at EVERY boundary, zero included — and the model, handed the same genesis,
              // agrees with production value for value. Until 227 the snapshot at zero said `""` and
              // the first tail record linked to the genesis, so zero did not verify.
              let cfg =
                  { StreamConfig.canonical with
                      Genesis = "g0" }

              let mutable st = planLaneGen.State0
              let mutable rs: OpRecord<PlanOp> list = OpStream.empty

              for op in [ AddItem("g1", "one"); AddItem("g2", "two") ] do
                  match
                      OpStream.append (OpStream.ofConfig cfg OpStream.defaultHash planW) (Human "writer") op st rs
                  with
                  | Ok(st', rs') ->
                      st <- st'
                      rs <- rs'
                  | Error e -> failtestf "append refused: %s" e

              Expect.hasLength rs 2 "both appends were accepted"

              Expect.isTrue
                  (OpStream.verifyChain (OpStream.ofConfig cfg OpStream.defaultHash planW) rs)
                  "the stream verifies under its own config"

              let prodWith (mode: SnapshotMode) (n: int) =
                  match mode with
                  | SnapshotMode.Strict ->
                      SnapshotMatrix.compactWith cfg OpStream.defaultHash planHash planW planLaneGen.State0 rs n
                  | SnapshotMode.ChainOnly ->
                      SnapshotMatrix.compactChainOnlyWith cfg OpStream.defaultHash planW planLaneGen.State0 rs n

              for mode in [ SnapshotMode.Strict; SnapshotMode.ChainOnly ] do
                  for n in 0 .. List.length rs do
                      let p = prodWith mode n

                      let m =
                          Chain.compact
                              OpStream.defaultHash
                              showPos
                              (modelPay mode planHash)
                              (chainApply planW)
                              cfg.Genesis
                              planLaneGen.State0
                              (toChainRecords rs)
                              (posOfInt n)

                      Expect.equal
                          (prodCompactVerdict p)
                          (modelCompactVerdict m)
                          (sprintf "%A at %d: the model under the same genesis is production" mode n)

                      match p with
                      | Error e -> failtestf "%A at %d: compact refused an intact stream: %s" mode n e
                      | Ok(snap, tail) ->
                          if n = 0 then
                              Expect.equal
                                  snap.PrevHash
                                  cfg.Genesis
                                  "at zero the boundary hash is the configured genesis"

                          // `cfg` differs from the canonical config in its genesis only, and the
                          // boundary walker reads the genesis nowhere (it walks from `snap.PrevHash`),
                          // so the canonical-payload verifiers are the config's own here.
                          let pAcross = prodVerifyAcross mode OpStream.defaultHash planHash planW snap tail

                          let mAcross =
                              Chain.verify_across
                                  OpStream.defaultHash
                                  showPos
                                  planW.Encode
                                  (modelPay mode planHash)
                                  (toModelSnapshot snap)
                                  (toChainRecords tail)

                          Expect.equal
                              (pAcross, mAcross)
                              (true, true)
                              (sprintf "%A at %d: the compaction of an intact stream verifies across" mode n)

                          // `compact_preserves_verify`, held of production under the config, with no
                          // condition on the genesis left in it.
                          Expect.equal
                              (OpStream.verifyChain (OpStream.ofConfig cfg OpStream.defaultHash planW) rs)
                              (OpStream.verifyChain
                                  (OpStream.ofConfig cfg OpStream.defaultHash planW)
                                  (List.truncate n rs)
                               && pAcross)
                              (sprintf "%A at %d: the split holds under the configured genesis" mode n)

              // The canonical entry point is the `""` instantiation and says so: handed this stream it
              // still snapshots `""` at zero, which is why a host on another genesis compacts through
              // `compactWith`. Past zero the two agree — the boundary hash is a stored one.
              match SnapshotMatrix.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs 0 with
              | Error e -> failtestf "compact refused an intact stream: %s" e
              | Ok(snap, tail) ->
                  Expect.equal snap.PrevHash "" "the canonical compact seeds the canonical genesis"

                  Expect.isFalse
                      (SnapshotMatrix.verifyAcrossWith cfg OpStream.defaultHash planHash planW snap tail)
                      "so it is not the compaction of a g0 stream at zero"

              Expect.equal
                  (SnapshotMatrix.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs 1)
                  (prodWith SnapshotMode.Strict 1)
                  "past zero the canonical compact and compactWith cfg are the same value"

          testCase
              "the shipped configurations snapshot byte-identically — compact is compactWith canonicalConfig, pinned by a vector"
          <| fun _ ->
              // Phase 227's additive half: threading the genesis moved no byte any released entry point
              // emits. Both shipped configs carry the empty genesis, so `compact` / `compactChainOnly`
              // / `snapshotAtOpt` are `...With` at either one, value for value, at every boundary — and
              // the snapshots at zero of a fixed stream are pinned by digest, so a later move of either
              // entry point's bytes goes red here rather than in a consumer's cache.
              let rs =
                  chainUnder
                      OpStream.defaultHash
                      planW
                      planLaneGen.State0
                      (Human "writer")
                      [ AddItem("v1", "one"); AddItem("v2", "two") ]

              Expect.hasLength rs 2 "both appends were accepted"

              for cfg in [ StreamConfig.canonical; StreamConfig.legacyActor ] do
                  Expect.equal cfg.Genesis "" "a shipped config carries the empty genesis"

                  for n in 0 .. List.length rs + 1 do
                      Expect.equal
                          (SnapshotMatrix.compactWith cfg OpStream.defaultHash planHash planW planLaneGen.State0 rs n)
                          (SnapshotMatrix.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs n)
                          (sprintf "compact at %d" n)

                      Expect.equal
                          (SnapshotMatrix.compactChainOnlyWith cfg OpStream.defaultHash planW planLaneGen.State0 rs n)
                          (SnapshotMatrix.compactChainOnly OpStream.defaultHash planW planLaneGen.State0 rs n)
                          (sprintf "compactChainOnly at %d" n)

                      Expect.equal
                          (SnapshotMatrix.snapshotAtOptWith
                              cfg
                              OpStream.defaultHash
                              (Some planHash)
                              planW
                              planLaneGen.State0
                              rs
                              n)
                          (SnapshotMatrix.snapshotAtOpt
                              OpStream.defaultHash
                              (Some planHash)
                              planW
                              planLaneGen.State0
                              rs
                              n)
                          (sprintf "snapshotAtOpt at %d" n)

              match
                  SnapshotMatrix.snapshotAt OpStream.defaultHash planHash planW planLaneGen.State0 rs 0,
                  SnapshotMatrix.snapshotAtChainOnly OpStream.defaultHash planW planLaneGen.State0 rs 0
              with
              | Ok strict, Ok chainOnly ->
                  Expect.equal strict.PrevHash "" "the canonical boundary hash at zero"
                  Expect.equal strict.Hash "299b31aa" "the strict snapshot at zero, pinned"
                  Expect.equal chainOnly.Hash "92f95a5f" "the chain-only snapshot at zero, pinned"
              | a, b -> failtestf "snapshot at zero refused: %A / %A" a b

          // ---- Phase 193 — the signed head: head, attestHead, verifyAttestation, and the re-mint ----

          testCase
              "the signed-head oracle agrees with production over the work-plan stream, every keyring and every re-minted splice"
          <| fun _ ->
              signedHeadDifferential
                  "work-plan signed heads"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  keyringSink
                  planW
                  tamperedPlanOp
                  planLaneGen
                  3800
                  40
              |> expectSignedAgreement "work-plan signed heads"

          testCase
              "the signed-head oracle agrees with production over the reference witness's stream, every keyring and every re-minted splice"
          <| fun _ ->
              signedHeadDifferential
                  "reference signed heads"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  keyringSink
                  treeW
                  (fun _ -> RemoveNode "tampered-node")
                  treeLaneGen
                  3810
                  30
              |> expectSignedAgreement "reference signed heads"

          testCase "a signed-head model handed a DIFFERENT hash disagrees with production — the comparison can lose"
          <| fun _ ->
              // The go-red for the whole differential: nothing about the chains, the keyrings or
              // the splices moves, only the function the model re-mints and walks with.
              let t =
                  signedHeadDifferential
                      "perturbed signed heads"
                      OpStream.defaultHash
                      swappedHash
                      keyringSink
                      planW
                      tamperedPlanOp
                      planLaneGen
                      3820
                      3

              Differential.expectLoses "a signed-head model hashing with a different function" t

          testCase
              "under a sink that verifies EVERYTHING a re-minted splice is accepted — signature_binds is load-bearing, to production and to the model alike"
          <| fun _ ->
              // `signature_binds` is the section's one new premise, and this is what it buys. The
              // promiscuous sink verifies every attestation against every head, so it does NOT
              // bind — and the theorem's conclusion fails with it, on production's own verdict.
              let t =
                  signedHeadDifferential
                      "promiscuous signed heads"
                      OpStream.defaultHash
                      OpStream.defaultHash
                      (fun _ -> promiscuousSink)
                      planW
                      tamperedPlanOp
                      planLaneGen
                      3830
                      3

              Differential.expectLoses "a sink that does not bind must let a re-minted splice through" t

              Expect.stringContains
                  (List.head (Tally.disagreements t))
                  "ACCEPTED under the original attestation"
                  "the differential fails on the theorem's conclusion, not on a model/production disagreement"

              // And it is the MODEL's verdict too: one chain, one op replaced and re-minted.
              let steps =
                  [ Human "writer", AddItem("s1", "one"); Human "writer", AddItem("s2", "two") ]

              let signed = remint OpStream.defaultHash planW.Encode steps

              let forged =
                  remint OpStream.defaultHash planW.Encode [ List.head steps; Human "writer", AddItem("s2", "TWO") ]

              match Attestation.attestHead promiscuousSink StreamConfig.canonical signed with
              | None -> failtest "the promiscuous sink signs"
              | Some att ->
                  Expect.isTrue
                      (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash planW) forged
                       && Attestation.verifyAttestation promiscuousSink StreamConfig.canonical att forged)
                      "production accepts the forgery under a sink that does not bind"

                  Expect.isTrue
                      (Chain.accepts_signed
                          OpStream.defaultHash
                          showPos
                          planW.Encode
                          ""
                          (modelVerify promiscuousSink)
                          (toModelAtt att)
                          (toChainRecords forged))
                      "and so does the model"

                  // Under the keyring sink the same forgery is refused by both.
                  let sink = keyringSink { Keys = [ "k", "s" ]; Active = "k" }

                  match Attestation.attestHead sink StreamConfig.canonical signed with
                  | None -> failtest "the keyring sink signs"
                  | Some bound ->
                      Expect.isFalse
                          (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash planW) forged
                           && Attestation.verifyAttestation sink StreamConfig.canonical bound forged)
                          "production refuses it under a sink that binds"

                      Expect.isFalse
                          (Chain.accepts_signed
                              OpStream.defaultHash
                              showPos
                              planW.Encode
                              ""
                              (modelVerify sink)
                              (toModelAtt bound)
                              (toChainRecords forged))
                          "and so does the model"

          testCase
              "a signed empty-chain sentinel accepts the empty chain and no chain with a head — OpStream.head hard-wires the empty string"
          <| fun _ ->
              // `signed_sentinel_covers_the_empty_chain`, and the premise `signed_head_binds_chain`
              // carries because of it, measured on production.
              let sink = keyringSink { Keys = [ "k", "s" ]; Active = "k" }

              let empty: OpRecord<PlanOp> list = OpStream.empty

              Expect.equal
                  (OpStream.head StreamConfig.canonical empty)
                  ""
                  "production's head of the empty chain is the literal empty string"

              Expect.equal (Chain.chain_head (toChainRecords empty)) "" "and so is the model's"

              let one =
                  remint OpStream.defaultHash planW.Encode [ Human "writer", AddItem("s1", "one") ]

              match
                  Attestation.attestHead sink StreamConfig.canonical empty,
                  Attestation.attestHead sink StreamConfig.canonical one
              with
              | Some overNothing, Some overOne ->
                  let accepts (att: Attestation) (rs: OpRecord<PlanOp> list) =
                      (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash planW) rs
                       && Attestation.verifyAttestation sink StreamConfig.canonical att rs),
                      Chain.accepts_signed
                          OpStream.defaultHash
                          showPos
                          planW.Encode
                          ""
                          (modelVerify sink)
                          (toModelAtt att)
                          (toChainRecords rs)

                  Expect.equal (accepts overNothing empty) (true, true) "a signed sentinel accepts the empty chain"
                  Expect.equal (accepts overNothing one) (false, false) "and not a chain that has a head"

                  // The DROP arm at its smallest: the one-record chain, its record dropped and the
                  // remainder re-minted, is the empty chain — refused, because the head that was
                  // signed is not the sentinel.
                  Expect.equal (accepts overOne one) (true, true) "the signed one-record chain is accepted"
                  Expect.equal (accepts overOne empty) (false, false) "and the empty chain is refused under it"

                  Expect.isTrue
                      (Chain.splice_changes
                          [ { Chain.cactor = Actor.encode (Human "writer")
                              Chain.cop = AddItem("s1", "one") } ]
                          (Chain.Dropped Chain.PZero))
                      "which is a splice the theorem speaks about"
              | _ -> failtest "the keyring sink signs" ]
