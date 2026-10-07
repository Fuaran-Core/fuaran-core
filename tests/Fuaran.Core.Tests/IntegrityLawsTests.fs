module Fuaran.Core.Tests.IntegrityLawsTests

// Phase 331 — the tests of the kit's integrity laws (`IntegrityLaws.fs`): the op codec and encoder
// injectivity laws, canonical floats, attestation, the hash function laws and attribution, moved
// verbatim from `ConformanceTests.fs`.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.Reference2
open Fuaran.Core.Tests.Reference.Counter
open Fuaran.Core.Tests.ConformanceTests

[<Tests>]
let integrityLawTests =
    testList
        "Conformance"
        [ // ---- Phase 145: the op codec's own injectivity, the content-id theorem's fourth premise ----

          testCase "the reference stream witness certifies its op codec injective (Phase 145)"
          <| fun _ ->
              let results = Conformance.codecInjectivityLaws sw streamGen 1450 200
              Expect.equal (List.length results) 3 "collision-free + left-inverse + the draw was wide enough"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "the reference stream witness failed codec injectivity:\n%s" (String.concat "\n" fails)

              // determinism: the same seed reproduces the identical verdict (seed-replay)
              Expect.equal
                  (Conformance.codecInjectivityLaws sw streamGen 1450 200)
                  results
                  "same seed ⇒ identical report"

          testCase "a codec that drops a field is caught, in both of the two ways it can be"
          <| fun _ ->
              // The teeth, and they are two DIFFERENT teeth. `Inc n` and `Dec n` encode alike here,
              // so a colliding pair is drawn within a couple of hundred iterations — that is the
              // first law. The left-inverse law does not have to wait for the pair: the very first
              // op it draws either comes back as itself or does not.
              let lossy =
                  { sw with
                      Encode =
                          fun op ->
                              match op with
                              | Inc n
                              | Dec n -> Json.render (Json.kindObj "op" [ "n", JInt n ]) }

              let results = Conformance.codecInjectivityLaws lossy streamGen 1451 200
              let failed = results |> List.filter (fun r -> not r.Passed)

              Expect.isGreaterThan
                  (List.length failed)
                  1
                  "a codec that erases the case fails BOTH the collision law and the left-inverse law"

              Expect.isTrue
                  (failed |> List.forall (fun r -> r.Counterexample.IsSome))
                  "every failure carries a seeded counterexample"

              Expect.isTrue
                  (failed
                   |> List.exists (fun r -> r.Law.StartsWith "the op codec is collision-free"))
                  "the collision law is one of them"

              Expect.isTrue
                  (failed
                   |> List.exists (fun r -> r.Law.StartsWith "the op codec has a left inverse"))
                  "and so is the left-inverse law"

          testCase "a generator that mints one op fails the family's own narrowness law"
          <| fun _ ->
              // Without this the collision law passes over a draw that compared nothing — the
              // vacuity a sampled injectivity search is exactly prone to.
              let oneOp: StreamGen<CounterOp, int> = { State0 = 0; Op = fun r -> Inc 1, r }
              let results = Conformance.codecInjectivityLaws sw oneOp 1452 200

              Expect.isTrue
                  (results
                   |> List.exists (fun r -> not r.Passed && r.Law.StartsWith "the draw searched more than one"))
                  "one drawn encoding is reported as a search that compared nothing"

          // Phase 55 — the canonical-float encoder laws.
          // Phase 253 — the round trip is through the PARSER over the whole double range, and the
          // parsed value re-renders to the same bytes (the fourth law).
          testCase
              "canonicalFloatLaws certify determinism + finite round-trip + fixed point + stable non-finite tokens (Phase 55, 253)"
          <| fun _ ->
              let results = Conformance.canonicalFloatLaws 4242 500

              Expect.equal (List.length results) 4 "determinism + round-trip + fixed-point + non-finite laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "canonicalFloatLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal (Conformance.canonicalFloatLaws 4242 500) results "same seed ⇒ identical report"

          // Phase 56 — the memo encoder-injectivity law.
          testCase "encoderInjectivityLaws pass for a sound encoder, fail for a lossy one (Phase 56)"
          <| fun _ ->
              // sound encoder (encNode) over varied trees — collision-free.
              let good = Conformance.encoderInjectivityLawsAt artw encNode genTree 4242 200
              Expect.equal (List.length good) 2 "one injectivity law and its searched-size guard reported (Phase 297)"

              Expect.isTrue
                  (good |> List.forall (fun r -> r.Passed))
                  (sprintf "sound encoder is collision-free: %A" good)

              // a lossy encoder that drops the node value — two trees differing only in a leaf value collide.
              let lossy (n: RNode) = n.Id + "|" + n.Kind

              let a = RNode.node "r" "doc" [ RNode.leaf "x" "para" "alpha" ]
              let b = RNode.node "r" "doc" [ RNode.leaf "x" "para" "beta" ]
              let mutable flip = false

              let twoTrees (rng: ConfRng.T) =
                  flip <- not flip
                  (if flip then a else b), rng

              let bad = Conformance.encoderInjectivityLawsAt artw lossy twoTrees 1 10
              Expect.isFalse (bad |> List.forall (fun r -> r.Passed)) "a lossy encoder must fail injectivity"

              Expect.isTrue
                  (bad |> List.exists (fun r -> r.Counterexample.IsSome))
                  "the lossy failure carries a (tree, tree) counterexample"

          // Phase 60 — the attestation / replay-as-provenance laws.
          testCase
              "attestationLaws certify checkpoint round-trip + prefix + replay-equivalence + falsification (Phase 60)"
          <| fun _ ->
              let sink = keyedSink "test-key-0"

              let results =
                  Conformance.attestationLawsAt sw streamGen sink OpStream.defaultHash 4242 200

              Expect.equal
                  (List.length results)
                  6
                  "round-trip + prefix + replay + op-tamper + actor-tamper laws, and the Phase 196 signing-outcome guard"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "attestationLaws failed under defaultHash:\n%s" (String.concat "\n" fails)

              // the falsification guarantee holds under a cryptographic (wide) HashFn too — a re-hashed
              // forgery cannot be re-signed without the key, whatever the chain hash's strength.
              let wide = Conformance.attestationLawsAt sw streamGen sink wideHash 4242 200

              Expect.isTrue
                  (wide |> List.forall (fun r -> r.Passed))
                  (sprintf "attestationLaws green under a wide HashFn: %A" (wide |> List.filter (fun r -> not r.Passed)))

              // seed-replay determinism
              Expect.equal
                  (Conformance.attestationLawsAt sw streamGen sink OpStream.defaultHash 4242 200)
                  results
                  "same seed ⇒ identical report"

          // Phase 196 inverted this case's VERDICT while keeping its subject. The observation is
          // unchanged and was always the point — under the no-op sink the five subject laws hold
          // over nothing — but "passes vacuously" is precisely the reading a consumer's census
          // rendered as `adopted`, so the family now reports the vacuity instead of absorbing it.
          // A host with no sink runs `noAttestationVacuityLaws`, which certifies the unsigned path
          // on purpose; running THIS family there is the mistake the guard now names.
          testCase "attestationLaws report the noAttestation default as VACUOUS, not as green (Phase 196)"
          <| fun _ ->
              let results =
                  Conformance.attestationLawsAt sw streamGen OpStream.noAttestation OpStream.defaultHash 4242 200

              let guardOf (r: LawResult) =
                  r.Law.StartsWith SampleAdequacy.guardOpening

              Expect.isTrue
                  (results |> List.filter (guardOf >> not) |> List.forall (fun r -> r.Passed))
                  (sprintf
                      "the five subject laws still hold over nothing: %A"
                      (results |> List.filter (fun r -> not r.Passed)))

              let guard = results |> List.filter guardOf

              Expect.equal (List.length guard) 1 "one adequacy guard is reported"

              Expect.isFalse
                  (guard |> List.forall (fun r -> r.Passed))
                  "and it is RED — a sink that never signs leaves four of the five laws asserting nothing"

              Expect.isTrue
                  (guard
                   |> List.forall (fun r ->
                       match r.Counterexample with
                       | Some cx -> cx.Contains "signed=0" && cx.Contains "falsified=0"
                       | None -> false))
                  "naming the counts it did reach, which is what tells a reader which way to widen"

          testCase "noAttestationVacuityLaws certify Sign⇒None + Verify⇒false + chain-unchanged (Phase 60)"
          <| fun _ ->
              let results =
                  Conformance.noAttestationVacuityLaws sw streamGen OpStream.defaultHash 4242 200

              Expect.equal (List.length results) 3 "no-sign + no-verify + unchanged laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "noAttestationVacuityLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal
                  (Conformance.noAttestationVacuityLaws sw streamGen OpStream.defaultHash 4242 200)
                  results
                  "same seed ⇒ identical report"

          // Phase 65 — the pluggable-HashFn parity + crypto-posture laws.
          testCase
              "hashFnLaws certify determinism + pre-image parity + tamper-detection over both hash postures (Phase 65)"
          <| fun _ ->
              let dflt = Conformance.hashFnLaws sw streamGen OpStream.defaultHash 4242 200

              Expect.equal
                  (List.length dflt)
                  7
                  "determinism + parity + tamper laws, the op-tamper / re-mint / distinguishing laws (Phase 302) and the tamper-arm guard reported"

              if dflt |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      dflt
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "hashFnLaws failed under defaultHash:\n%s" (String.concat "\n" fails)

              // the same contract holds under a supplied (wide) HashFn — cross-host parity keys on the
              // canonical pre-image only, whichever HashFn the host supplies.
              let wide = Conformance.hashFnLaws sw streamGen wideHash 4242 200

              Expect.isTrue
                  (wide |> List.forall (fun r -> r.Passed))
                  (sprintf "hashFnLaws green under a wide HashFn: %A" (wide |> List.filter (fun r -> not r.Passed)))

              // seed-replay determinism
              Expect.equal
                  (Conformance.hashFnLaws sw streamGen OpStream.defaultHash 4242 200)
                  dflt
                  "same seed ⇒ identical report"

          testCase
              "hashFnAdversarialLaws pin the crypto posture: wide stand-in resists, FNV-1a admits a forgery (Phase 65)"
          <| fun _ ->
              let results = Conformance.hashFnAdversarialLaws wideHash 500000 4242
              Expect.equal (List.length results) 2 "resist + admit laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "hashFnAdversarialLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal
                  (Conformance.hashFnAdversarialLaws wideHash 500000 4242)
                  results
                  "same seed ⇒ identical report"

          // Phase 81 — the attributed-stream lift laws.
          testCase "attributedLaws certify replay-parity + chain-covers-attribution + envelope round-trip (Phase 81)"
          <| fun _ ->
              let results = Conformance.attributedLaws sw streamGen OpStream.defaultHash 4242 200

              Expect.equal
                  (List.length results)
                  4
                  "replay-parity + tamper + round-trip laws and the re-attribution guard reported (Phase 297)"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "attributedLaws failed under defaultHash:\n%s" (String.concat "\n" fails)

              // the same contract holds under a supplied (wide) HashFn — attribution is inside the pre-image.
              let wide = Conformance.attributedLaws sw streamGen wideHash 4242 200

              Expect.isTrue
                  (wide |> List.forall (fun r -> r.Passed))
                  (sprintf "attributedLaws green under a wide HashFn: %A" (wide |> List.filter (fun r -> not r.Passed)))

              // seed-replay determinism
              Expect.equal
                  (Conformance.attributedLaws sw streamGen OpStream.defaultHash 4242 200)
                  results
                  "same seed ⇒ identical report" ]
