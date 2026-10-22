module Fuaran.Core.Tests.ProvenanceTests

// Phase 320 — typed, attested provenance. The actor is now a typed `Actor` (Human/Agent) folded
// into the chain hash (so altering attribution breaks the integrity chain), a pre-320 bare-string
// stream migrates via `rehash` under `legacyActorConfig`, an optional `IAttestationSink` signs
// chain checkpoints (default no-op), and replay-as-provenance is independently re-verifiable.

open Expecto
open Fuaran.Core

type private CounterOp =
    | Inc of int
    | Dec of int

let private encodeOp =
    function
    | Inc n -> Json.render (Json.kindObj "inc" [ "n", JInt n ])
    | Dec n -> Json.render (Json.kindObj "dec" [ "n", JInt n ])

let private decodeOp (s: string) : Result<CounterOp, string> =
    Json.parse s
    |> Result.bind (fun el ->
        Decoder.describing (Decoder.field "kind" Decoder.str) el
        |> Result.bind (fun k ->
            Decoder.describing (Decoder.field "n" Decoder.int) el
            |> Result.map (fun n -> if k = "dec" then Dec n else Inc n)))

let private sw: StreamWitness<CounterOp, int, string> =
    { Apply =
        fun op st ->
            match op with
            | Inc n -> Ok(st + n)
            | Dec n -> if st - n < 0 then Error "negative" else Ok(st - n)
      Encode = encodeOp
      Decode = decodeOp }

let private h = OpStream.defaultHash

// A human and an AI agent acting on the same stream — the Human/Agent distinction is the
// load-bearing AI-accountability fact this phase makes tamper-evident.
let private alice = Human "alice"
let private bot = Agent("claude-opus-4-8", "2026-06", "session-42")

/// A deterministic stand-in for a host KMS/HSM attestation sink — the signature is `hash(key|head)`
/// (no System.Security.Cryptography, so the *test* stays as Fable-clean as the library). A real
/// host swaps in asymmetric signing behind the same `IAttestationSink` seam.
let private testSink (key: string) : IAttestationSink =
    { new IAttestationSink with
        member _.Sign head =
            Some
                { Head = head
                  KeyId = "test-key"
                  Signature = h key head }

        member _.Verify attestation head =
            attestation.KeyId = "test-key"
            && attestation.Head = head
            && attestation.Signature = h key head }

/// Serialise records in the **pre-Phase-320** legacy form (the `actor` field a bare string), so a
/// migration reader (`fromJsonlLegacyActor`) has a realistic file to ingest.
let private toLegacyJsonl (records: OpRecord<CounterOp> list) : string =
    records
    |> List.map (fun r ->
        sprintf
            "{\"seq\":%d,\"actor\":\"%s\",\"op\":%s,\"prevHash\":\"%s\",\"hash\":\"%s\"}"
            r.Seq
            (Actor.id r.Actor)
            (encodeOp r.Op)
            r.PrevHash
            r.Hash)
    |> String.concat "\n"

let private build () =
    let step acc (actor, op) =
        acc
        |> Result.bind (fun (st, recs) -> OpStream.append (OpStream.canonical h sw) actor op st recs)

    [ alice, Inc 5; bot, Inc 3; alice, Dec 2 ]
    |> List.fold step (Ok(0, OpStream.empty))

[<Tests>]
let tests =
    testList
        "Provenance"
        [
          // ---- typed actor folded into the hash ----

          testCase "the op-record carries a typed Human/Agent actor"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  Expect.equal recs[0].Actor (Human "alice") "first op is a human"

                  match recs[1].Actor with
                  | Agent(model, version, id) ->
                      Expect.equal model "claude-opus-4-8" "model recorded"
                      Expect.equal version "2026-06" "version recorded"
                      Expect.equal id "session-42" "agent id recorded"
                  | other -> failtestf "expected an Agent, got %A" other
              | Error e -> failtestf "build failed: %A" e

          testCase "altering the actor breaks the chain (attribution is now tamper-evident)"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  Expect.isTrue (OpStream.verifyChain (OpStream.canonical h sw) recs) "intact chain verifies"

                  // re-attribute op 1 from the agent to a human, leaving op + hashes untouched
                  let reattributed =
                      recs
                      |> List.mapi (fun i r -> if i = 1 then { r with Actor = Human "mallory" } else r)

                  Expect.isFalse
                      (OpStream.verifyChain (OpStream.canonical h sw) reattributed)
                      "a re-attributed op no longer hashes to its stored chain link"

                  match OpStream.firstChainBreak (OpStream.canonical h sw) reattributed with
                  | Some b ->
                      Expect.equal b.Index 1 "break localised to the re-attributed record"
                      // 0.23.0 — the reason is typed. `HashMismatch` is the digest check, which is
                      // the one a re-attribution fails: the record keeps its sequence and its
                      // prev-link, and only the pre-image the hash was taken over has moved.
                      Expect.equal b.Reason ChainBreakReason.HashMismatch "the break names the digest check"
                  | None -> failtest "expected a chain break"
              | Error e -> failtestf "build failed: %A" e

          testCase "the typed actor round-trips through JSONL"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  match Jsonl.write sw recs |> Jsonl.read sw |> Result.mapError StreamLoadFault.toString with
                  | Ok restored ->
                      Expect.equal restored recs "records (typed actor included) survive the round-trip"

                      Expect.isTrue
                          (OpStream.verifyChain (OpStream.canonical h sw) restored)
                          "restored chain still verifies"
                  | Error e -> failtestf "fromJsonl failed: %s" e
              | Error e -> failtestf "build failed: %A" e

          // ---- migration from a pre-320 bare-string-actor stream ----

          testCase "a pre-320 legacy stream verifies under legacyActorConfig and not under canonical"
          <| fun _ ->
              // build under the legacy bare-string payload, as a pre-320 host would have
              let step acc (actor, op) =
                  acc
                  |> Result.bind (fun (st, recs) ->
                      OpStream.append (OpStream.ofConfig StreamConfig.legacyActor h sw) actor op st recs)

              match
                  [ Human "alice", Inc 5; Human "bob", Inc 3 ]
                  |> List.fold step (Ok(0, OpStream.empty))
              with
              | Ok(_, legacy) ->
                  Expect.isTrue
                      (OpStream.verifyChain (OpStream.ofConfig StreamConfig.legacyActor h sw) legacy)
                      "intact under the legacy bare-string config"

                  Expect.isFalse
                      (OpStream.verifyChain (OpStream.canonical h sw) legacy)
                      "the canonical typed-actor config does not verify the legacy chain"
              | Error e -> failtestf "legacy build failed: %A" e

          testCase "fromJsonlLegacyActor + rehash migrates a pre-320 file into the typed-actor hash format"
          <| fun _ ->
              let step acc (actor, op) =
                  acc
                  |> Result.bind (fun (st, recs) ->
                      OpStream.append (OpStream.ofConfig StreamConfig.legacyActor h sw) actor op st recs)

              match
                  [ Human "alice", Inc 5; Human "bob", Inc 3; Human "alice", Dec 2 ]
                  |> List.fold step (Ok(0, OpStream.empty))
              with
              | Ok(live, legacy) ->
                  // a pre-320 file on disk: bare-string actors
                  let file = toLegacyJsonl legacy

                  match Jsonl.readLegacyActor sw file |> Result.mapError StreamLoadFault.toString with
                  | Ok read ->
                      Expect.equal read legacy "the legacy reader lifts each bare-string actor to Human"

                      match
                          OpStream.rehash
                              (OpStream.ofConfig StreamConfig.legacyActor h sw)
                              (OpStream.canonical h sw)
                              read
                      with
                      | Ok migrated ->
                          Expect.isTrue
                              (OpStream.verifyChain (OpStream.canonical h sw) migrated)
                              "the migrated chain verifies under the canonical typed-actor config"

                          Expect.equal
                              (migrated |> List.map _.Actor)
                              (legacy |> List.map _.Actor)
                              "actors are preserved (still Human) — only the hash chain changed"

                          Expect.equal (OpStream.replay sw 0 migrated) (Ok live) "replay reproduces the same state"
                      | Error e -> failtestf "rehash failed: %A" e
                  | Error e -> failtestf "fromJsonlLegacyActor failed: %s" e
              | Error e -> failtestf "legacy build failed: %A" e

          // ---- optional attestation sink (default no-op) ----

          testCase "the default no-op sink signs nothing — default behaviour is unchanged"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  Expect.equal
                      (Attestation.attestHead Attestation.noAttestation StreamConfig.canonical recs)
                      None
                      "no-op sink ⇒ no attestation"

                  let bogus =
                      { Head = OpStream.head StreamConfig.canonical recs
                        KeyId = "k"
                        Signature = "s" }

                  Expect.isFalse
                      (Attestation.verifyAttestation Attestation.noAttestation StreamConfig.canonical bogus recs)
                      "no-op sink verifies nothing"
              | Error e -> failtestf "build failed: %A" e

          testCase "an attestation sink signs the chain head and the signature re-verifies"
          <| fun _ ->
              let sink = testSink "kms-secret"

              match build () with
              | Ok(_, recs) ->
                  match Attestation.attestHead sink StreamConfig.canonical recs with
                  | Some att ->
                      Expect.equal
                          att.Head
                          (OpStream.head StreamConfig.canonical recs)
                          "the attestation covers the current head"

                      Expect.isTrue
                          (Attestation.verifyAttestation sink StreamConfig.canonical att recs)
                          "the signed checkpoint verifies against the head"
                  | None -> failtest "expected the sink to sign"
              | Error e -> failtestf "build failed: %A" e

          // ---- replay-as-provenance: independently re-verifiable ----

          testCase "linear: a state is provably the deterministic replay of its attested op log"
          <| fun _ ->
              let sink = testSink "kms-secret"

              match build () with
              | Ok(live, recs) ->
                  let att = Attestation.attestHead sink StreamConfig.canonical recs |> Option.get

                  // an independent verifier, given only the records + the attestation:
                  Expect.isTrue (OpStream.verifyChain (OpStream.canonical h sw) recs) "(1) the hash chain is intact"

                  Expect.isTrue
                      (Attestation.verifyAttestation sink StreamConfig.canonical att recs)
                      "(2) the signed head checks out"

                  Expect.equal (OpStream.replay sw 0 recs) (Ok live) "(3) replay re-derives exactly the state"
                  Expect.equal (OpStream.replay sw 0 recs) (OpStream.replay sw 0 recs) "replay is deterministic"

                  // tampering any op or actor breaks (1), so the attested head is no longer the
                  // legitimate product of the log — provenance is independently falsifiable
                  let tampered =
                      recs
                      |> List.mapi (fun i r -> if i = 1 then { r with Actor = Human "mallory" } else r)

                  Expect.isFalse
                      (OpStream.verifyChain (OpStream.canonical h sw) tampered)
                      "re-attribution breaks the chain ⇒ the attested head no longer follows from the log"
              | Error e -> failtestf "build failed: %A" e

          testCase "DAG: a signed head id + deterministic replay is independently re-verifiable"
          <| fun _ ->
              let sink = testSink "kms-secret"
              // a branch+merge op-DAG with mixed human/agent attribution
              let g, d1 =
                  Dag.append (OpStream.canonical h sw) alice (Inc 5) "" Dag.empty
                  |> Reference.built

              let b, d2 = Dag.append (OpStream.canonical h sw) bot (Inc 3) g d1 |> Reference.built

              let c, d3 =
                  Dag.append (OpStream.canonical h sw) alice (Inc 4) g d2 |> Reference.built

              let m, dag =
                  Dag.merge (OpStream.canonical h sw) bot (Inc 0) b c d3 |> Reference.built

              // the head is a content hash; attest it
              let att = sink.Sign m |> Option.get
              Expect.equal (Dag.heads dag) [ m ] "the merge is the sole head"

              // an independent verifier:
              Expect.isTrue
                  (Dag.verifyDag (OpStream.canonical h sw) dag)
                  "(1) every node id is the content hash of its (parents, actor, op)"

              Expect.isTrue (sink.Verify att m) "(2) the signed head id checks out"

              match Dag.tryReplayTo sw 0 dag m with
              | Ok state ->
                  Expect.equal state 12 "(3) replay folds genesis(5)+b(3)+c(4)+merge(0) once"
                  Expect.equal (Dag.tryReplayTo sw 0 dag m) (Dag.tryReplayTo sw 0 dag m) "replay is deterministic"
              | Error e -> failtestf "tryReplayTo failed: %A" e

              // re-attributing a node changes its content id, so the attested head id no longer
              // names a node the recomputed DAG contains — provenance broken
              let tampered =
                  DagOf.nodes (dag.Nodes |> Map.map (fun _ n -> { n with Actor = Human "mallory" }))

              Expect.isFalse
                  (Dag.verifyDag (OpStream.canonical h sw) tampered)
                  "re-attribution breaks every node's content id" ]

// ---- Phase 491 — the attestation descriptor (DECISIONS.md D187.7) ----

/// The canonical stream the descriptor tests seal and verify under.
let private stream = OpStream.canonical h sw

/// The anchor a segment starting at `at` hangs from: the genesis anchor at zero, else the boundary
/// the record before it leaves.
let private anchorAt (recs: OpRecord<CounterOp> list) (at: int) : Anchor =
    if at = 0 then
        Anchor.genesis StreamConfig.canonical
    else
        { Seq = at
          PrevHash = recs[at - 1].Hash }

/// A claim over `segment` hung from `anchor`, by `party`, naming the test sink's key.
let private claimOver (anchor: Anchor) (segment: OpRecord<CounterOp> list) (party: Actor) : AttestationClaim =
    { StreamId = "orders"
      Head = ChainShape.tip (ChainShape.records StreamConfig.canonical sw.Encode) anchor segment
      Party = party
      Algorithm = "test-alg-v1"
      SignedAt = 1767225600L
      KeyId = "test-key"
      Evidence = [ "chain-intact", "pass"; "replay-equal", "pass" ] }

let private recordsOf () =
    match build () with
    | Ok(_, recs) -> recs
    | Error e -> failtestf "build failed: %A" e

[<Tests>]
let descriptorTests =
    testList
        "Provenance.AttestationDescriptor"
        [ testCase "a descriptor over a mid-stream segment describes it, signs, and verifies"
          <| fun _ ->
              let recs = recordsOf ()
              let anchor = anchorAt recs 1
              let segment = recs |> List.skip 1
              let d = AttestationDescriptor.seal stream anchor (claimOver anchor segment bot)

              Expect.equal d.Mode SnapshotMode.Strict "a descriptor is sealed strict"
              Expect.equal d.State.Head.Seq 3 "the head is one past the segment's last record"
              Expect.equal d.State.Head.PrevHash recs[2].Hash "and names its hash"
              Expect.isNone (AttestationDescriptor.firstFault stream d segment) "it describes its segment"

              let sink = testSink "kms-secret"
              let att = Attestation.ofDescriptor sink d |> Option.get
              Expect.equal att.Head d.Seal "the sink signs the seal"

              Expect.equal
                  (Attestation.verifyDescriptor sink stream att d segment)
                  (Ok())
                  "the attestation verifies over the segment"

          testCase "the claim's encoding and the seal's pre-image are pinned"
          <| fun _ ->
              let claim =
                  { StreamId = "s\"1"
                    Head = { Seq = 2; PrevHash = "abc" }
                    Party = Human "alice"
                    Algorithm = "alg"
                    SignedAt = 1767225600L
                    KeyId = "k"
                    Evidence = [ "c1", "pass" ] }

              let encoded =
                  "{\"stream\":\"s\\\"1\",\"head\":{\"seq\":2,\"prevHash\":\"abc\"},\"party\":"
                  + Actor.encode (Human "alice")
                  + ",\"algorithm\":\"alg\",\"signedAt\":1767225600,\"keyId\":\"k\",\"evidence\":[[\"c1\",\"pass\"]]}"

              Expect.equal (AttestationDescriptor.encode claim) encoded "the canonical encoding"

              let anchor = { Seq = 1; PrevHash = "prev" }
              let d = AttestationDescriptor.seal stream anchor claim

              Expect.equal
                  d.Seal
                  (h "prev" ("{\"attestation\":1,\"seq\":1,\"claim\":" + encoded + "}"))
                  "the seal is the chain hash of the pre-image at the anchor"

              Expect.equal (AttestationDescriptor.seal stream d.Anchor d.State) d "re-sealing the claim reproduces it"

          testCase "the encoding is injective where a naive join is not, and its pre-image is apart from a snapshot's"
          <| fun _ ->
              let recs = recordsOf ()
              let anchor = anchorAt recs 0
              let claim = claimOver anchor recs alice

              let a =
                  { claim with
                      Evidence = [ "a\",\"b", "c" ] }

              let b =
                  { claim with
                      Evidence = [ "a", "b\",\"c" ] }

              Expect.notEqual
                  (AttestationDescriptor.encode a)
                  (AttestationDescriptor.encode b)
                  "evidence spelling a separator is still its own value"

              let snapshotSeal =
                  (Sealed.seal SnapshotMode.Strict h AttestationDescriptor.encode anchor claim).Seal

              Expect.notEqual
                  (AttestationDescriptor.seal stream anchor claim).Seal
                  snapshotSeal
                  "a descriptor's seal is not the seal of a snapshot holding the same claim"

          testCase "an empty segment is described by a descriptor whose head is its anchor"
          <| fun _ ->
              let recs = recordsOf ()
              let anchor = anchorAt recs 3
              let d = AttestationDescriptor.seal stream anchor (claimOver anchor [] alice)
              Expect.equal d.State.Head anchor "the tip of nothing is the anchor"
              Expect.isNone (AttestationDescriptor.firstFault stream d []) "it describes the empty segment"

              Expect.equal
                  (AttestationDescriptor.firstFault stream d [ recs[2] ])
                  (Some(
                      DescriptorFault.Broken(
                          SnapshotBreak.Tail
                              { Index = 0
                                Reason = ChainBreakReason.SequenceMismatch
                                Expected = "3"
                                Got = "2" }
                      )
                  ))
                  "a record from below the anchor is refused by the walker"

          testCase "each refusal is named, in the order the checks run"
          <| fun _ ->
              let recs = recordsOf ()
              let anchor = anchorAt recs 1
              let segment = recs |> List.skip 1
              let d = AttestationDescriptor.seal stream anchor (claimOver anchor segment bot)
              let fault = AttestationDescriptor.firstFault stream

              Expect.equal
                  (fault { d with Mode = SnapshotMode.ChainOnly } segment)
                  (Some DescriptorFault.NotStrict)
                  "a chain-only descriptor would not bind its claim"

              match
                  fault
                      { d with
                          State = { d.State with Party = alice } }
                      segment
              with
              | Some(DescriptorFault.Broken(SnapshotBreak.SnapshotHash(expected, got))) ->
                  Expect.equal got d.Seal "the carried seal is reported"
                  Expect.notEqual expected d.Seal "beside the one the edited claim recomputes to"
              | other -> failtestf "a re-attributed claim must break the seal, got %A" other

              let tampered =
                  segment |> List.mapi (fun i r -> if i = 1 then { r with Op = Inc 99 } else r)

              match fault d tampered with
              | Some(DescriptorFault.Broken(SnapshotBreak.Tail b)) ->
                  Expect.equal b.Index 1 "the walker names the tampered record by its place in the segment"
                  Expect.equal b.Reason ChainBreakReason.HashMismatch "by its hash"
              | other -> failtestf "a segment the walker refuses must be refused, got %A" other

              Expect.equal
                  (fault d (List.take 1 segment))
                  (Some(DescriptorFault.RangeMismatch(3, 2)))
                  "a shortened segment moves the range"

              // the forgery a bare hash chain re-accepts: the last op replaced and the chain re-minted
              let reminted =
                  (Ok(0, OpStream.empty), [ alice, Inc 5; bot, Inc 3; alice, Inc 99 ])
                  ||> List.fold (fun acc (actor, op) ->
                      acc |> Result.bind (fun (st, rs) -> OpStream.append stream actor op st rs))
                  |> function
                      | Ok(_, rs) -> rs |> List.skip 1
                      | Error e -> failtestf "re-mint failed: %A" e

              Expect.equal
                  (fault d reminted)
                  (Some(DescriptorFault.HeadMismatch(recs[2].Hash, reminted[1].Hash)))
                  "a re-minted forgery walks cleanly and is refused by its head"

          testCase "an attestation is refused for another seal, another key, or a signature the sink refuses"
          <| fun _ ->
              let recs = recordsOf ()
              let anchor = anchorAt recs 0
              let d = AttestationDescriptor.seal stream anchor (claimOver anchor recs alice)
              let sink = testSink "kms-secret"
              let att = Attestation.ofDescriptor sink d |> Option.get
              let verify = Attestation.verifyDescriptor sink stream

              let byBot = AttestationDescriptor.seal stream anchor { d.State with Party = bot }

              Expect.equal
                  (verify att byBot recs)
                  (Error(AttestationFault.NotThisSeal(d.Seal, byBot.Seal)))
                  "one party's attestation is not another's"

              let otherKey =
                  AttestationDescriptor.seal stream anchor { d.State with KeyId = "other-key" }

              let attOther = Attestation.ofDescriptor sink otherKey |> Option.get

              Expect.equal
                  (verify attOther otherKey recs)
                  (Error(AttestationFault.KeyMismatch("other-key", "test-key")))
                  "a signature under another key than the claim names is refused"

              Expect.equal
                  (Attestation.verifyDescriptor (testSink "another-secret") stream att d recs)
                  (Error AttestationFault.SignatureRefused)
                  "the sink is asked last, and its refusal is named"

              Expect.equal
                  (verify att d (List.take 2 recs))
                  (Error(AttestationFault.Descriptor(DescriptorFault.RangeMismatch(3, 2))))
                  "a descriptor that does not describe the records is refused before the sink is asked"

              Expect.isNone (Attestation.ofDescriptor Attestation.noAttestation d) "the no-op sink signs nothing" ]
