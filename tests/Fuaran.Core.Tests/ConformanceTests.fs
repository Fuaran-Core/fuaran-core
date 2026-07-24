module Fuaran.Core.Tests.ConformanceTests

// Phase 243 — the op-algebra conformance kit, self-proven against the in-repo reference
// witness, plus a deliberately-broken witness whose failure is reproduced from a seed.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.Reference2

// ---- a random tree generator over the reference RNode (unique ids) ----
let private genTree (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable counter = 0
    let mutable r = rng

    let freshId () =
        let id = sprintf "n%d" counter
        counter <- counter + 1
        id

    let rec build depth =
        let id = freshId ()

        if depth <= 0 then
            RNode.leaf id "para" "v"
        else
            let nKids, r' = ConfRng.intBelow 3 r
            r <- r'
            let kids = [ for _ in 1..nKids -> build (depth - 1) ]
            RNode.node id "section" kids

    let t = build 2
    t, r

let private genFresh (existing: Set<string>) (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable r = rng

    let rec pick () =
        let v, r' = ConfRng.next r
        r <- r'
        let id = sprintf "f%d" (v % 100000)
        if existing.Contains id then pick () else id

    let id = pick ()
    RNode.leaf id "para" "x", r

let private opGen: OpGen<RNode, string> =
    { Tree = genTree
      FreshNode = genFresh
      CanHold = None }

// ---- a counter stream witness (mirrors OpStreamTests) ----
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
      // A real decode (Phase 81 — attributedLaws exercises the fromJsonl path); mirrors OpStreamTests.
      Decode =
        fun s ->
            Decode.parse s
            |> Result.bind (fun el ->
                Decode.kindOf el
                |> Result.bind (fun k -> Decode.intField "n" el |> Result.map (fun n -> k, n)))
            |> Result.bind (function
                | "inc", n -> Ok(Inc n)
                | "dec", n -> Ok(Dec n)
                | k, _ -> Error("unknown op kind: " + k)) }

let private genStreamOp (rng: ConfRng.T) : CounterOp * ConfRng.T =
    let kind, r1 = ConfRng.intBelow 2 rng
    let n, r2 = ConfRng.intBelow 5 r1
    (if kind = 0 then Inc n else Dec n), r2

let private streamGen: StreamGen<CounterOp, int> = { State0 = 0; Op = genStreamOp }

// ---- Phase 60/65: an in-repo keyed signing sink + a wide collision-resistant HashFn stand-in ----
// GP3: no cryptographic hash ships in Core; these live test-side. `keyedSink` is a keyed FNV/HMAC-style
// stand-in (head-bound: Verify recomputes the keyed tag AND checks the covered head), enough to prove
// the attestation seam's falsification guarantee without a host crypto dependency. `wideHash` is a
// 128-bit FNV-family stand-in — not cryptographic, but wide enough that a bounded birthday search finds
// no collision, so it models the "collision-resistant HashFn" a host wires (SHA-256) for the adversarial
// branch (contrast the 32-bit default FNV-1a, which does collide in-budget — the documented posture).
let private fnv1a (s: string) : string =
    let mutable h = 2166136261u

    for ch in s do
        h <- h ^^^ uint32 ch
        h <- h * 16777619u

    h.ToString("x8")

let private keyedSink (key: string) : IAttestationSink =
    let sign (head: string) = fnv1a (key + "|" + head)

    { new IAttestationSink with
        member _.Sign head =
            Some
                { Head = head
                  KeyId = "test-key"
                  Signature = sign head }

        member _.Verify att head =
            att.Head = head && att.Signature = sign head }

let private wideHash: HashFn =
    fun prev payload ->
        let s = prev + "|" + payload

        let pass (basis: uint32) (prime: uint32) =
            let mutable h = basis

            for ch in s do
                h <- h ^^^ uint32 ch
                h <- h * prime

            h.ToString("x8")

        pass 2166136261u 16777619u
        + pass 2166136353u 16777639u
        + pass 2166136619u 16777669u
        + pass 2166136721u 16777691u

// ---- a cross-witness composition generator over the reference RNode (Phase 47) ----
// An outer with two independent `para` slots + one value hole, a closed inner, and two open inners
// sharing the hole name "x" at distinct ids (so their re-rooted copies get distinct addresses).
let private genComposition (rng: ConfRng.T) : Conformance.CompositionSample<RNode, RNode> * ConfRng.T =
    let v, r1 = ConfRng.intBelow 11 rng // an in-space value for the count hole (0..10)
    let det, r2 = ConfRng.intBelow 4 r1 // vary the inner effect so the join is non-trivial
    let determinism = [ Deterministic; Clock; Random; Network ] |> List.item det

    let outer =
        RNode.node
            "co"
            "template"
            [ RNode.hole "v1" "field" "count" (ValueHole(IntRange(0, 10)))
              RNode.hole "sa" "region" "a" (SlotHole(Some "para"))
              RNode.hole "sb" "region" "b" (SlotHole(Some "para")) ]

    let openInnerA =
        { RNode.node "ga" "para" [ RNode.hole "xa" "field" "x" (ValueHole AnyString) ] with
            Eff =
                { Host = Pure
                  Determinism = determinism } }

    let openInnerB =
        RNode.node "gb" "para" [ RNode.hole "xb" "field" "x" (ValueHole AnyString) ]

    { Outer = outer
      SlotA = "co/sa"
      SlotB = "co/sb"
      OuterArgs = [ "co/v1", string v ]
      ClosedInner = RNode.leaf "p" "para" "x"
      OpenInnerA = openInnerA
      OpenInnerB = openInnerB
      OpenHoleName = "x"
      OpenHoleArg = "z" },
    r2

// ---- a CROSS-WITNESS composition generator (Phase 51): RNode outer, R2Node inner ----
// The same outer shape as genComposition, but the inners are the second (int-id) reference witness —
// so composeAcross + applyMemo are exercised across a genuinely-distinct witness pair. The R2 inners
// are rooted at Tag "para" so embedToR yields RNode "para" nodes the slots accept.
let private genComposition2 (rng: ConfRng.T) : Conformance.CompositionSample<RNode, R2Node> * ConfRng.T =
    let v, r1 = ConfRng.intBelow 11 rng
    let det, r2 = ConfRng.intBelow 4 r1
    let determinism = [ Deterministic; Clock; Random; Network ] |> List.item det

    let outer =
        RNode.node
            "co"
            "template"
            [ RNode.hole "v1" "field" "count" (ValueHole(IntRange(0, 10)))
              RNode.hole "sa" "region" "a" (SlotHole(Some "para"))
              RNode.hole "sb" "region" "b" (SlotHole(Some "para")) ]

    let openInnerA =
        { R2Node.node 10 "para" [ R2Node.hole 11 "field" "x" (ValueHole AnyString) ] with
            Effect =
                { Host = Pure
                  Determinism = determinism } }

    let openInnerB =
        R2Node.node 20 "para" [ R2Node.hole 21 "field" "x" (ValueHole AnyString) ]

    { Outer = outer
      SlotA = "co/sa"
      SlotB = "co/sb"
      OuterArgs = [ "co/v1", string v ]
      ClosedInner = R2Node.leaf 1 "para" "x"
      OpenInnerA = openInnerA
      OpenInnerB = openInnerB
      OpenHoleName = "x"
      OpenHoleArg = "z" },
    r2

// ---- a memo sample generator over the reference RNode (Phase 49) ----
// A pure template + two distinct full param-sets (count differs), and an effecting (non-deterministic)
// variant — so applyMemo hits, misses, and bypasses are all exercised.
let private genMemo (rng: ConfRng.T) : Conformance.MemoSample<RNode> * ConfRng.T =
    let v1, r1 = ConfRng.intBelow 11 rng // count in [0,10]
    let v2, r2 = ConfRng.intBelow 11 r1
    let alt = if v2 = v1 then (v1 + 1) % 11 else v2 // guarantee ArgsAlt ≠ Args
    let det, r3 = ConfRng.intBelow 3 r2
    let determinism = [ Clock; Random; Network ] |> List.item det // a non-deterministic source

    let fullArgs c =
        Map.ofList
            [ "tpl/t", ValueArg "T"
              "tpl/c", ValueArg(string c)
              "tpl/s", SlotArg(RNode.leaf "p" "para" "x") ]

    let effFn =
        { template () with
            Eff =
                { Host = Pure
                  Determinism = determinism } }

    { PureFn = template ()
      Args = fullArgs v1
      ArgsAlt = fullArgs alt
      EffectingFn = effFn
      EffectingArgs = fullArgs v1 },
    r3

[<Tests>]
let tests =
    testList
        "Conformance"
        [ testCase "the reference witness certifies green across the algebra + stream laws"
          <| fun _ ->
              let report =
                  Conformance.certify nodew idw opGen sw streamGen OpStream.defaultHash 12345 200

              if not report.AllPassed then
                  let fails =
                      report.Results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "reference witness failed conformance:\n%s" (String.concat "\n" fails)

              Expect.equal
                  (report.Results |> List.length)
                  13
                  "witness (4) + algebra (3) + diff (3) + stream (3) laws reported"

          testCase "op-algebra laws run standalone (no stream)"
          <| fun _ ->
              let results = Conformance.opAlgebra nodew idw opGen 999 200
              Expect.isTrue (results |> List.forall (fun r -> r.Passed)) "all algebra laws pass"

          testCase "diff laws certify the reference witness green (Phase 03)"
          <| fun _ ->
              let results = Conformance.diffLaws nodew idw opGen 4242 200
              Expect.equal (List.length results) 3 "reconstruction + applyability + survivor"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "reference witness failed diff laws:\n%s" (String.concat "\n" fails)

              // determinism: the same seed reproduces the identical verdict (seed-replay)
              Expect.equal (Conformance.diffLaws nodew idw opGen 4242 200) results "same seed ⇒ identical report"

          testCase "a deliberately-broken witness fails with a reproducible counterexample"
          <| fun _ ->
              // ReplaceChildren that ignores the new children — structural edits silently
              // no-op, so apply∘invert can no longer be the identity.
              let brokenW =
                  { nodew with
                      ReplaceChildren = fun n _ -> n }

              let results = Conformance.opAlgebra brokenW idw opGen 7 200
              Expect.isFalse (results |> List.forall (fun r -> r.Passed)) "the broken witness must fail a law"

              let failed = results |> List.filter (fun r -> not r.Passed)
              Expect.isNonEmpty failed "at least one law failed"

              Expect.isTrue
                  (failed |> List.forall (fun r -> r.Counterexample.IsSome))
                  "every failure carries a seeded counterexample"

              // determinism: the same seed reproduces the identical verdict
              let again = Conformance.opAlgebra brokenW idw opGen 7 200
              Expect.equal again results "same seed ⇒ identical report"

          // Phase 27 — the determinism-capture / replay laws.
          testCase "captureReplayLaws certify exact replay for a non-deterministic int witness (Phase 27)"
          <| fun _ ->
              let encInt (n: int) = Json.render (JInt n)

              let decInt (s: string) =
                  Decode.parse s |> Result.bind Decode.asInt

              let results =
                  Conformance.captureReplayLaws encInt decInt ConfRng.next OpStream.defaultHash 31337 200

              Expect.equal
                  (List.length results)
                  4
                  "exact-replay + deterministic + tamper + identity-order laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "captureReplayLaws failed:\n%s" (String.concat "\n" fails)

              // determinism: the same seed reproduces the identical verdict (seed-replay)
              Expect.equal
                  (Conformance.captureReplayLaws encInt decInt ConfRng.next OpStream.defaultHash 31337 200)
                  results
                  "same seed ⇒ identical report"

          // Phase 30 — the invocable-capability laws.
          testCase "capabilityLaws certify validation + replay + enumeration + round-trip (Phase 30)"
          <| fun _ ->
              let results = Conformance.capabilityLaws 4242 200
              Expect.equal (List.length results) 4 "validation + replay + enumeration + round-trip laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "capabilityLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal (Conformance.capabilityLaws 4242 200) results "same seed ⇒ identical report"

          // Phase 46 — the data-acquisition Query laws.
          testCase "queryLaws certify param-validation + replay + enumeration + round-trip (Phase 46)"
          <| fun _ ->
              let results = Conformance.queryLaws 4242 200
              Expect.equal (List.length results) 4 "validation + replay + enumeration + round-trip laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "queryLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal (Conformance.queryLaws 4242 200) results "same seed ⇒ identical report"

          // Phase 47 — the cross-witness composition laws.
          testCase "compositionLaws certify nested-application + associativity + hygiene + effect-join (Phase 47)"
          <| fun _ ->
              let results = Conformance.compositionLaws artw artw id genComposition 4242 200

              Expect.equal
                  (List.length results)
                  4
                  "nested-application + associativity + hygiene + effect-join laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "compositionLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal
                  (Conformance.compositionLaws artw artw id genComposition 4242 200)
                  results
                  "same seed ⇒ identical report"

          // Phase 49 — the memoised-application laws.
          testCase "memoLaws certify equals-direct + param-miss + effecting-bypass + replay-parity (Phase 49)"
          <| fun _ ->
              let results =
                  Conformance.memoLaws artw encNode genMemo OpStream.defaultHash 4242 200

              Expect.equal
                  (List.length results)
                  4
                  "equals-direct + param-miss + effecting-bypass + replay-parity laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "memoLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal
                  (Conformance.memoLaws artw encNode genMemo OpStream.defaultHash 4242 200)
                  results
                  "same seed ⇒ identical report"

          // Phase 50 — the signature-typed function registry laws.
          testCase "registryLaws certify findable + non-match + narrowing + default-deny dispatch (Phase 50)"
          <| fun _ ->
              let results = Conformance.registryLaws 4242 200
              Expect.equal (List.length results) 4 "findable + non-match + narrowing + default-deny laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "registryLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal (Conformance.registryLaws 4242 200) results "same seed ⇒ identical report"

          // Phase 57 — the content-pack loading-contract laws.
          testCase
              "packLoadingLaws certify load round-trip + version-mismatch + default-deny + shape-derived version (Phase 57)"
          <| fun _ ->
              let results = Conformance.packLoadingLaws 4242 200

              Expect.equal
                  (List.length results)
                  4
                  "round-trip + version-mismatch + unknown-base + shape-derived laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "packLoadingLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal (Conformance.packLoadingLaws 4242 200) results "same seed ⇒ identical report"

          // Phase 51 — the cross-witness composition pilot (RNode outer + R2Node inner).
          testCase "compositionPilot certifies composeAcross + applyMemo across two distinct witnesses (Phase 51)"
          <| fun _ ->
              let results =
                  Conformance.compositionPilot artw artw2 embedToR encNode encNode2 genComposition2 4242 200

              Expect.equal
                  (List.length results)
                  6
                  "composeAcross (nested + associative + hygiene + effect-join) + applyMemo (sub-fn + composed) reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "compositionPilot failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal
                  (Conformance.compositionPilot artw artw2 embedToR encNode encNode2 genComposition2 4242 200)
                  results
                  "same seed ⇒ identical report"

          // Phase 53 — the memo audited-effect soundness laws.
          testCase "memoSoundnessLaws certify an under-declared-impure function is bypassed (Phase 53)"
          <| fun _ ->
              // root declares Pure/Deterministic, but the count node secretly observes the clock — the
              // under-declared case the pre-Phase-53 declared-root gate would have wrongly memoised.
              let underDeclared =
                  { RNode.node
                        "ud"
                        "template"
                        [ { RNode.hole "c" "field" "count" (ValueHole(IntRange(0, 5))) with
                              Eff = { Host = Pure; Determinism = Clock } } ] with
                      Eff = Effect.pureDeterministic }

              let underDeclaredArgs = Map.ofList [ "ud/c", ValueArg "3" ]

              let results =
                  Conformance.memoSoundnessLaws artw encNode underDeclared underDeclaredArgs 4242 50

              Expect.equal (List.length results) 2 "gate-distinction + bypass laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "memoSoundnessLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal
                  (Conformance.memoSoundnessLaws artw encNode underDeclared underDeclaredArgs 4242 50)
                  results
                  "same seed ⇒ identical report"

          // Phase 55 — the canonical-float encoder laws.
          testCase "canonicalFloatLaws certify determinism + finite round-trip + stable non-finite tokens (Phase 55)"
          <| fun _ ->
              let results = Conformance.canonicalFloatLaws 4242 500
              Expect.equal (List.length results) 3 "determinism + round-trip + non-finite laws reported"

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
              let good = Conformance.encoderInjectivityLaws artw encNode genTree 4242 200
              Expect.equal (List.length good) 1 "one injectivity law reported"

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

              let bad = Conformance.encoderInjectivityLaws artw lossy twoTrees 1 10
              Expect.isFalse (bad |> List.forall (fun r -> r.Passed)) "a lossy encoder must fail injectivity"

              Expect.isTrue
                  (bad |> List.exists (fun r -> r.Counterexample.IsSome))
                  "the lossy failure carries a (tree, tree) counterexample"

          // Phase 36 — the aggregate-parity laws (Column.aggregate as the single source GroupBy calls).
          testCase "aggregateParityLaws certify single-source parity + null-skip (Phase 36)"
          <| fun _ ->
              let results = Conformance.aggregateParityLaws 4242 200
              Expect.equal (List.length results) 2 "parity + null-skip laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "aggregateParityLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal (Conformance.aggregateParityLaws 4242 200) results "same seed ⇒ identical report"

          // Phase 60 — the attestation / replay-as-provenance laws.
          testCase
              "attestationLaws certify checkpoint round-trip + prefix + replay-equivalence + falsification (Phase 60)"
          <| fun _ ->
              let sink = keyedSink "test-key-0"

              let results =
                  Conformance.attestationLaws sw streamGen sink OpStream.defaultHash 4242 200

              Expect.equal
                  (List.length results)
                  5
                  "round-trip + prefix + replay + op-tamper + actor-tamper laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "attestationLaws failed under defaultHash:\n%s" (String.concat "\n" fails)

              // the falsification guarantee holds under a cryptographic (wide) HashFn too — a re-hashed
              // forgery cannot be re-signed without the key, whatever the chain hash's strength.
              let wide = Conformance.attestationLaws sw streamGen sink wideHash 4242 200

              Expect.isTrue
                  (wide |> List.forall (fun r -> r.Passed))
                  (sprintf "attestationLaws green under a wide HashFn: %A" (wide |> List.filter (fun r -> not r.Passed)))

              // seed-replay determinism
              Expect.equal
                  (Conformance.attestationLaws sw streamGen sink OpStream.defaultHash 4242 200)
                  results
                  "same seed ⇒ identical report"

          testCase "attestationLaws pass vacuously under the noAttestation default (Phase 60)"
          <| fun _ ->
              let results =
                  Conformance.attestationLaws sw streamGen OpStream.noAttestation OpStream.defaultHash 4242 200

              Expect.isTrue
                  (results |> List.forall (fun r -> r.Passed))
                  (sprintf
                      "the no-op sink satisfies the laws vacuously: %A"
                      (results |> List.filter (fun r -> not r.Passed)))

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

              Expect.equal (List.length dflt) 3 "determinism + parity + tamper laws reported"

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

              Expect.equal (List.length results) 3 "replay-parity + tamper + round-trip laws reported"

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

// ---- Phase 48: artifact-function property-verification ----

/// The domain validity oracle the verifier drives: any node whose Value parses as an int > 5 is a
/// `Severity.Error` defect. The "rule" a correct-by-construction function must respect for every
/// binding — registered into a real `Validator.Registry` so `verifyFunction` drives the framework.
let private countReg: Validator.Registry<RNode, string> =
    Validator.empty
    |> Validator.register (
        Validator.perNode "count≤5" (fun _ n ->
            match System.Int32.TryParse n.Value with
            | true, v when v > 5 ->
                [ { Code = "CNT001"
                    Severity = Severity.Error
                    Message = sprintf "count %d exceeds 5" v
                    Node = Some n.Id } ]
            | _ -> [])
    )

/// A full template whose `count` hole ranges over [lo, hi]; title + body are fixed-shape holes.
let private tplCount (lo, hi) =
    { RNode.node
          "tpl"
          "template"
          [ RNode.hole "t" "field" "title" (ValueHole(StringLen(1, 20)))
            RNode.hole "c" "field" "count" (ValueHole(IntRange(lo, hi)))
            RNode.hole "s" "region" "body" (SlotHole(Some "para")) ] with
        Eff = Effect.pureDeterministic }

/// A single-hole template: just a `count` over [lo, hi] — a small finite space for the symbolic mode.
let private countOnly (lo, hi) =
    { RNode.node "ct" "template" [ RNode.hole "c" "field" "count" (ValueHole(IntRange(lo, hi))) ] with
        Eff = Effect.pureDeterministic }

/// Draw an in-space value for a value/repeat hole's space (covers the spaces these templates use).
let private sampleInSpace (space: ValueSpace) (rng: ConfRng.T) : string * ConfRng.T =
    match space with
    | IntRange(lo, hi) ->
        let v, r = ConfRng.intBelow (hi - lo + 1) rng
        string (lo + v), r
    | StringLen(lo, _) -> String.replicate (max 1 lo) "a", rng
    | Enum xs -> ConfRng.choose xs rng
    | _ -> "x", rng

/// A valid param-set generator: fill every data hole with an in-space value / a para slot.
let private genParamsFor (fn: RNode) (rng: ConfRng.T) : Map<string, Arg<RNode>> * ConfRng.T =
    let holes =
        artw.Holes fn
        |> List.filter (fun h ->
            match h.Kind with
            | ActionHole _ -> false
            | _ -> true)

    let mutable r = rng

    let args =
        holes
        |> List.map (fun h ->
            match h.Kind with
            | ValueHole space
            | RepeatHole space ->
                let v, r' = sampleInSpace space r
                r <- r'
                h.Addr, ValueArg v
            | SlotHole _ -> h.Addr, SlotArg(RNode.leaf "p" "para" "x")
            | ActionHole _ -> h.Addr, ValueArg "") // unreachable (filtered above)

    Map.ofList args, r

[<Tests>]
let functionVerifyTests =
    testList
        "Conformance.functionVerify"
        [ testCase "a sound function verifies clean; a broken one yields a (param-set, defect) counterexample"
          <| fun _ ->
              let sound =
                  Conformance.verifyFunction artw (tplCount (0, 5)) countReg genParamsFor 4242 200

              Expect.isTrue sound.Verified "count∈[0,5] never violates the ≤5 rule"
              Expect.isNone sound.Counterexample "a sound function has no counterexample"

              let broken =
                  Conformance.verifyFunction artw (tplCount (0, 10)) countReg genParamsFor 4242 200

              Expect.isFalse broken.Verified "count∈[0,10] admits a >5 value — not correct-by-construction"

              match broken.Counterexample with
              | Some cx ->
                  match cx.Defect with
                  | Conformance.ValidatorRejected ds ->
                      Expect.isNonEmpty ds "the validator's defect travels in the counterexample"
                  | other -> failtestf "expected ValidatorRejected, got %A" other

                  Expect.stringContains
                      (Conformance.renderCounterexample artw cx)
                      "tpl/c"
                      "the rendered counterexample cites the count hole"
              | None -> failtest "a broken function must surface a counterexample"

          testCase "verification is deterministic — same seed ⇒ identical report"
          <| fun _ ->
              let a =
                  Conformance.verifyFunction artw (tplCount (0, 10)) countReg genParamsFor 999 200

              let b =
                  Conformance.verifyFunction artw (tplCount (0, 10)) countReg genParamsFor 999 200

              Expect.equal a b "same seed reproduces the verdict + counterexample"

          testCase "symbolic mode exhaustively covers a small space and reports the coverage (Phase 48)"
          <| fun _ ->
              let sound =
                  Conformance.verifyFunctionSymbolic artw (countOnly (0, 5)) countReg Map.empty 100 7

              Expect.equal sound.Coverage (Conformance.Exhaustive 6) "6 ints in [0,5], all enumerated"
              Expect.isTrue sound.Verified "every value in [0,5] respects the ≤5 rule"

              let broken =
                  Conformance.verifyFunctionSymbolic artw (countOnly (0, 10)) countReg Map.empty 100 7

              Expect.equal broken.Coverage (Conformance.Exhaustive 11) "11 ints in [0,10], the whole space"
              Expect.isFalse broken.Verified "exhaustive enumeration finds the >5 values"

          testCase "symbolic mode samples a large space with the coverage reported (coverage honesty)"
          <| fun _ ->
              let report =
                  Conformance.verifyFunctionSymbolic artw (countOnly (0, 100000)) countReg Map.empty 50 7

              match report.Coverage with
              | Conformance.Sampled(50, Some 100001) -> ()
              | other -> failtestf "expected Sampled(50, Some 100001), got %A" other

          testCase "symbolic mode varies value holes while slots are pinned via fixedArgs"
          <| fun _ ->
              let fixedArgs =
                  Map.ofList [ "tpl/t", ValueArg "T"; "tpl/s", SlotArg(RNode.leaf "p" "para" "x") ]

              let report =
                  Conformance.verifyFunctionSymbolic artw (tplCount (0, 5)) countReg fixedArgs 100 7

              Expect.equal report.Coverage (Conformance.Exhaustive 6) "only the count hole varies (6 cases)"
              Expect.isTrue report.Verified "clean across the pinned-slot param space"

          testCase "verifyFunction surfaces an undeclared effect as a defect (Fork-3 cross-check)"
          <| fun _ ->
              // a 'pure'-declared template whose count node secretly observes the clock.
              let leaky =
                  { RNode.node
                        "lk"
                        "template"
                        [ { RNode.hole "c" "field" "count" (ValueHole(IntRange(0, 5))) with
                              Eff = { Host = Pure; Determinism = Clock } } ] with
                      Eff = Effect.pureDeterministic }

              let report = Conformance.verifyFunction artw leaky Validator.empty genParamsFor 1 25

              Expect.isFalse report.Verified "an effect the declaration doesn't cover is a defect"

              match report.Counterexample with
              | Some cx ->
                  match cx.Defect with
                  | Conformance.EffectObserved(_, observed) ->
                      Expect.equal observed.Determinism Clock "the observed clock effect is named"
                  | other -> failtestf "expected EffectObserved, got %A" other
              | None -> failtest "the effect leak must surface a counterexample"

          testCase "functionVerifyLaws certify sound-clean + broken-fails + determinism (Phase 48)"
          <| fun _ ->
              let results =
                  Conformance.functionVerifyLaws artw (tplCount (0, 5)) (tplCount (0, 10)) countReg genParamsFor 777 200

              Expect.equal (List.length results) 3 "sound + broken + determinism laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "functionVerifyLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism of the kit itself
              Expect.equal
                  (Conformance.functionVerifyLaws
                      artw
                      (tplCount (0, 5))
                      (tplCount (0, 10))
                      countReg
                      genParamsFor
                      777
                      200)
                  results
                  "same seed ⇒ identical report"

          // Phase 79 — compare-and-append (optimistic concurrency) over the StreamWitness.
          testCase "casLaws certify match≡append + stale-rejection + race-serialisation (Phase 79)"
          <| fun _ ->
              let results = Conformance.casLaws sw streamGen OpStream.defaultHash 4242 200

              Expect.equal (List.length results) 3 "match + stale + race laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "casLaws failed under defaultHash:\n%s" (String.concat "\n" fails)

              // the CAS is over head identity, so it is HashFn-agnostic — green under a wide HashFn too.
              let wide = Conformance.casLaws sw streamGen wideHash 4242 200

              Expect.isTrue
                  (wide |> List.forall (fun r -> r.Passed))
                  (sprintf "casLaws green under a wide HashFn: %A" (wide |> List.filter (fun r -> not r.Passed)))

              // seed-replay determinism
              Expect.equal
                  (Conformance.casLaws sw streamGen OpStream.defaultHash 4242 200)
                  results
                  "same seed ⇒ identical report"

          // Phase 52 — the verifyFunction contract honesty boundary (effect-class-aware guard).
          testCase
              "verifyHonestyLaws certify stochastic-verifies-on-structure + effect-class-agnostic verdict (Phase 52)"
          <| fun _ ->
              // structurally-identical functions under a chosen effect-determinism axis: sound
              // (count∈[0,5], never violates the ≤5 rule) and broken (count∈[0,10], admits a >5 value).
              let mkSoundDet (d: DeterminismSource) =
                  { tplCount (0, 5) with
                      Eff = { Host = Pure; Determinism = d } }

              let mkBrokenDet (d: DeterminismSource) =
                  { tplCount (0, 10) with
                      Eff = { Host = Pure; Determinism = d } }

              let results =
                  Conformance.verifyHonestyLaws artw mkSoundDet mkBrokenDet countReg genParamsFor 777 200

              Expect.equal (List.length results) 2 "stochastic-verifies + effect-class-agnostic laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "verifyHonestyLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal
                  (Conformance.verifyHonestyLaws artw mkSoundDet mkBrokenDet countReg genParamsFor 777 200)
                  results
                  "same seed ⇒ identical report" ]
