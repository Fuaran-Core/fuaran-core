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
    Decode.parse s
    |> Result.bind (fun el ->
        Decode.kindOf el
        |> Result.bind (fun k ->
            Decode.intField "n" el
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
        acc |> Result.bind (fun (st, recs) -> OpStream.append h sw actor op st recs)

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
                  Expect.equal recs.[0].Actor (Human "alice") "first op is a human"

                  match recs.[1].Actor with
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
                  Expect.isTrue (OpStream.verifyChain h sw recs) "intact chain verifies"

                  // re-attribute op 1 from the agent to a human, leaving op + hashes untouched
                  let reattributed =
                      recs
                      |> List.mapi (fun i r -> if i = 1 then { r with Actor = Human "mallory" } else r)

                  Expect.isFalse
                      (OpStream.verifyChain h sw reattributed)
                      "a re-attributed op no longer hashes to its stored chain link"

                  match OpStream.firstChainBreak h sw reattributed with
                  | Some b ->
                      Expect.equal b.Index 1 "break localised to the re-attributed record"
                      Expect.stringContains b.Reason "actor" "the break reason names actor tampering"
                  | None -> failtest "expected a chain break"
              | Error e -> failtestf "build failed: %A" e

          testCase "the typed actor round-trips through JSONL"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  match OpStream.toJsonl sw recs |> OpStream.fromJsonl sw with
                  | Ok restored ->
                      Expect.equal restored recs "records (typed actor included) survive the round-trip"
                      Expect.isTrue (OpStream.verifyChain h sw restored) "restored chain still verifies"
                  | Error e -> failtestf "fromJsonl failed: %s" e
              | Error e -> failtestf "build failed: %A" e

          // ---- migration from a pre-320 bare-string-actor stream ----

          testCase "a pre-320 legacy stream verifies under legacyActorConfig and not under canonical"
          <| fun _ ->
              // build under the legacy bare-string payload, as a pre-320 host would have
              let step acc (actor, op) =
                  acc
                  |> Result.bind (fun (st, recs) ->
                      OpStream.appendWith OpStream.legacyActorConfig h sw actor op st recs)

              match
                  [ Human "alice", Inc 5; Human "bob", Inc 3 ]
                  |> List.fold step (Ok(0, OpStream.empty))
              with
              | Ok(_, legacy) ->
                  Expect.isTrue
                      (OpStream.verifyChainWith OpStream.legacyActorConfig h sw legacy)
                      "intact under the legacy bare-string config"

                  Expect.isFalse
                      (OpStream.verifyChain h sw legacy)
                      "the canonical typed-actor config does not verify the legacy chain"
              | Error e -> failtestf "legacy build failed: %A" e

          testCase "fromJsonlLegacyActor + rehash migrates a pre-320 file into the typed-actor hash format"
          <| fun _ ->
              let step acc (actor, op) =
                  acc
                  |> Result.bind (fun (st, recs) ->
                      OpStream.appendWith OpStream.legacyActorConfig h sw actor op st recs)

              match
                  [ Human "alice", Inc 5; Human "bob", Inc 3; Human "alice", Dec 2 ]
                  |> List.fold step (Ok(0, OpStream.empty))
              with
              | Ok(live, legacy) ->
                  // a pre-320 file on disk: bare-string actors
                  let file = toLegacyJsonl legacy

                  match OpStream.fromJsonlLegacyActor sw file with
                  | Ok read ->
                      Expect.equal read legacy "the legacy reader lifts each bare-string actor to Human"

                      match OpStream.rehash OpStream.legacyActorConfig OpStream.canonicalConfig h sw read with
                      | Ok migrated ->
                          Expect.isTrue
                              (OpStream.verifyChain h sw migrated)
                              "the migrated chain verifies under the canonical typed-actor config"

                          Expect.equal
                              (migrated |> List.map (fun r -> r.Actor))
                              (legacy |> List.map (fun r -> r.Actor))
                              "actors are preserved (still Human) — only the hash chain changed"

                          Expect.equal (OpStream.replay sw 0 migrated) (Ok live) "replay reproduces the same state"
                      | Error e -> failtestf "rehash failed: %s" e
                  | Error e -> failtestf "fromJsonlLegacyActor failed: %s" e
              | Error e -> failtestf "legacy build failed: %A" e

          // ---- optional attestation sink (default no-op) ----

          testCase "the default no-op sink signs nothing — default behaviour is unchanged"
          <| fun _ ->
              match build () with
              | Ok(_, recs) ->
                  Expect.equal (OpStream.attestHead OpStream.noAttestation recs) None "no-op sink ⇒ no attestation"

                  let bogus =
                      { Head = OpStream.head recs
                        KeyId = "k"
                        Signature = "s" }

                  Expect.isFalse
                      (OpStream.verifyAttestation OpStream.noAttestation bogus recs)
                      "no-op sink verifies nothing"
              | Error e -> failtestf "build failed: %A" e

          testCase "an attestation sink signs the chain head and the signature re-verifies"
          <| fun _ ->
              let sink = testSink "kms-secret"

              match build () with
              | Ok(_, recs) ->
                  match OpStream.attestHead sink recs with
                  | Some att ->
                      Expect.equal att.Head (OpStream.head recs) "the attestation covers the current head"

                      Expect.isTrue
                          (OpStream.verifyAttestation sink att recs)
                          "the signed checkpoint verifies against the head"
                  | None -> failtest "expected the sink to sign"
              | Error e -> failtestf "build failed: %A" e

          // ---- replay-as-provenance: independently re-verifiable ----

          testCase "linear: a state is provably the deterministic replay of its attested op log"
          <| fun _ ->
              let sink = testSink "kms-secret"

              match build () with
              | Ok(live, recs) ->
                  let att = OpStream.attestHead sink recs |> Option.get

                  // an independent verifier, given only the records + the attestation:
                  Expect.isTrue (OpStream.verifyChain h sw recs) "(1) the hash chain is intact"
                  Expect.isTrue (OpStream.verifyAttestation sink att recs) "(2) the signed head checks out"
                  Expect.equal (OpStream.replay sw 0 recs) (Ok live) "(3) replay re-derives exactly the state"
                  Expect.equal (OpStream.replay sw 0 recs) (OpStream.replay sw 0 recs) "replay is deterministic"

                  // tampering any op or actor breaks (1), so the attested head is no longer the
                  // legitimate product of the log — provenance is independently falsifiable
                  let tampered =
                      recs
                      |> List.mapi (fun i r -> if i = 1 then { r with Actor = Human "mallory" } else r)

                  Expect.isFalse
                      (OpStream.verifyChain h sw tampered)
                      "re-attribution breaks the chain ⇒ the attested head no longer follows from the log"
              | Error e -> failtestf "build failed: %A" e

          testCase "DAG: a signed head id + deterministic replay is independently re-verifiable"
          <| fun _ ->
              let sink = testSink "kms-secret"
              // a branch+merge op-DAG with mixed human/agent attribution
              let g, d1 = Dag.append h sw alice (Inc 5) "" Dag.empty
              let b, d2 = Dag.append h sw bot (Inc 3) g d1
              let c, d3 = Dag.append h sw alice (Inc 4) g d2
              let m, dag = Dag.merge h sw bot (Inc 0) b c d3

              // the head is a content hash; attest it
              let att = sink.Sign m |> Option.get
              Expect.equal (Dag.heads dag) [ m ] "the merge is the sole head"

              // an independent verifier:
              Expect.isTrue (Dag.verifyDag h sw dag) "(1) every node id is the content hash of its (parents, actor, op)"
              Expect.isTrue (sink.Verify att m) "(2) the signed head id checks out"

              match Dag.replayTo sw 0 dag m with
              | Ok state ->
                  Expect.equal state 12 "(3) replay folds genesis(5)+b(3)+c(4)+merge(0) once"
                  Expect.equal (Dag.replayTo sw 0 dag m) (Dag.replayTo sw 0 dag m) "replay is deterministic"
              | Error e -> failtestf "replayTo failed: %A" e

              // re-attributing a node changes its content id, so the attested head id no longer
              // names a node the recomputed DAG contains — provenance broken
              let tampered =
                  { Dag.T.Nodes = dag.Nodes |> Map.map (fun _ n -> { n with Actor = Human "mallory" }) }

              Expect.isFalse (Dag.verifyDag h sw tampered) "re-attribution breaks every node's content id" ]
