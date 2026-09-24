module Fuaran.Core.Tests.ConformanceTests

// Phase 243 — the op-algebra conformance kit, self-proven against the in-repo reference
// witness, plus a deliberately-broken witness whose failure is reproduced from a seed.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.Reference2

// ---- a random tree generator over the reference RNode (unique ids) ----
let genTree (rng: ConfRng.T) : RNode * ConfRng.T =
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

let opGen: OpGen<RNode, string> =
    { Tree = genTree
      FreshNode = genFresh
      CanHold = None }

// ---- a counter stream witness (mirrors OpStreamTests) ----
type CounterOp =
    | Inc of int
    | Dec of int

let sw: StreamWitness<CounterOp, int, string> =
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

let streamGen: StreamGen<CounterOp, int> = { State0 = 0; Op = genStreamOp }

// ---- Phase 223: the stratified reference generators for the drawn-refusal families ----

/// A `Dec` no reachable counter state can absorb. Every chain these laws build is a handful of
/// `Inc` draws below five, so an overdraw is refused in EVERY state the run can reach.
let overdraw = 1_000_000

/// `streamGen` with its refusal made a STRATUM rather than a coincidence: one draw in three is
/// `Inc` (applies in every state), one is `Dec n` (applies or refuses by the state, as before), and
/// one is an overdraw (refuses in every state). The kit reference generator for `casLaws` and
/// `idempotencyLaws`, whose agreement laws compare a domain refusal only when one is drawn — so a
/// run of the default size reaches both outcomes by the generator's shape, not by the counter
/// happening to sit low. A StreamGen is not told the iteration index, so the stratum is drawn at a
/// fixed rate: over the two hundred iterations the reference runs, the chance that no draw in the
/// arm lands on it is (2/3)^200.
let private genStratifiedStreamOp (rng: ConfRng.T) : CounterOp * ConfRng.T =
    let stratum, r1 = ConfRng.intBelow 3 rng
    let n, r2 = ConfRng.intBelow 5 r1

    match stratum with
    | 0 -> Inc n, r2
    | 1 -> Dec n, r2
    | _ -> Dec overdraw, r2

let stratifiedStreamGen: StreamGen<CounterOp, int> =
    { State0 = 0
      Op = genStratifiedStreamOp }

/// The refusal-free generator every drawn-refusal suite's must-fail case uses: `Inc` only, so no
/// op is ever refused and the family's refused-op guard is the only line that can say so.
let refusalFreeStreamGen: StreamGen<CounterOp, int> =
    { State0 = 0
      Op =
        fun rng ->
            let n, r = ConfRng.intBelow 5 rng
            Inc n, r }

/// The stratified reference generator for `diffContainedLaws`: "section" holds children and "para"
/// is a leaf, and every drawn tree carries a para directly under its root, so the minted probe
/// always has a non-container to graft under unless the four derived ops removed it. Before this
/// the demanding direction of the refusal iff was reached on whichever draws happened to carry a
/// para (100 of 200 at seed 4242).
let containedGen: OpGen<RNode, string> =
    { Tree =
        fun rng ->
            let t, r = genTree rng

            { t with
                Children = t.Children @ [ RNode.leaf "strat-para" "para" "v" ] },
            r
      FreshNode = opGen.FreshNode
      CanHold = Some(fun (n: RNode) -> n.Kind <> "para") }

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

let keyedSink (key: string) : IAttestationSink =
    let sign (head: string) = fnv1a (key + "|" + head)

    { new IAttestationSink with
        member _.Sign head =
            Some
                { Head = head
                  KeyId = "test-key"
                  Signature = sign head }

        member _.Verify att head =
            att.Head = head && att.Signature = sign head }

let wideHash: HashFn =
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
let genComposition (rng: ConfRng.T) : Conformance.CompositionSample<RNode, RNode> * ConfRng.T =
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
let genComposition2 (rng: ConfRng.T) : Conformance.CompositionSample<RNode, R2Node> * ConfRng.T =
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
let genMemo (rng: ConfRng.T) : Conformance.MemoSample<RNode> * ConfRng.T =
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
                  17
                  // algebra gained the insert-uniqueness law in Phase 137, the
                  // WellFormed-preservation law in Phase 139, and — Phase 220 — its two
                  // accepted/refused adequacy guards.
                  "witness (4) + algebra (5 + 2 guards) + diff (3) + stream (3) laws reported"

          // ---- Phase 145: the op codec's own injectivity, the content-id theorem's fourth premise ----

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

          testCase "contained diff laws certify a container-bearing witness green (Phase 141)"
          <| fun _ ->
              // `diffLaws` above certifies `Diff.toOps`' scripts with the PLAIN sequence pair,
              // which is blind to containment by construction. This family asks the questions of
              // the pair that belong together: `toOpsContained` emitted the script, so
              // `canApplyAllWith` / `applyAllWith` under the SAME predicate are what must accept
              // it. Run against a witness with a real capability — the generator builds "section"
              // internally and "para" at the leaves, so a para is a genuine non-container and the
              // family's minted probe has somewhere to graft.
              // Phase 223 — the stratified `containedGen` (a para under every drawn root), which is
              // the kit reference generator the census row is measured against.
              let containerGen = containedGen

              // The probe was MEASURED rather than trusted, because a refusal law is green whether
              // or not its demanding direction is ever taken: under this predicate, before the
              // Phase 223 stratification, 100 of 200
              // iterations mint the violating probe (the rest draw an `after` with no para at all),
              // all 100 are refused with the offender named by id AND kind, and all 100 are
              // ACCEPTED by the plain `toOps` — so the refusal really is the container check's
              // contribution and not something else's.
              let results = Conformance.diffContainedLaws nodew idw containerGen 4242 200

              Expect.equal
                  (List.length results)
                  5
                  "reconstruction + applyability + refusal exactness + the two Phase 223 guards"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "reference witness failed the contained diff laws:\n%s" (String.concat "\n" fails)

              Expect.equal
                  (Conformance.diffContainedLaws nodew idw containerGen 4242 200)
                  results
                  "same seed ⇒ identical report"

          testCase "the contained diff laws have teeth — a witness whose canHold refuses everything"
          <| fun _ ->
              // The go-red the green run above cannot be: with a predicate that admits nothing, any
              // `after` carrying a child is a container violation, so the refusal law's DEMANDING
              // direction is the only one exercised. If `toOpsContained` ever stopped consulting
              // the predicate, this run would report a script where a refusal is required and the
              // family would go red — which is what makes the green run above evidence rather than
              // an assertion about a branch nothing takes.
              // MEASURED here too: 132 of 200 generated `after` trees violate natively under this
              // predicate, plus 68 minted probes — so the demanding direction is what this run is
              // almost entirely made of.
              let refuseAll =
                  { opGen with
                      CanHold = Some(fun (_: RNode) -> false) }

              let results = Conformance.diffContainedLaws nodew idw refuseAll 4242 200

              Expect.isTrue
                  (results |> List.forall (fun r -> r.Passed))
                  (sprintf
                      "a predicate that refuses everything must still be certified — every diff is a refusal and the refusal must be exact: %A"
                      (results |> List.filter (fun r -> not r.Passed)))

              // and the refusal really was the branch taken: the plain family still certifies the
              // same witness, so the difference between the two reports is the container check and
              // nothing else.
              Expect.isTrue
                  (Conformance.diffLaws nodew idw refuseAll 4242 200
                   |> List.forall (fun r -> r.Passed))
                  "the PLAIN diff laws are unaffected by the predicate — the container check is the only difference"

          testCase
              "Phase 223 — a canHold that refuses nothing turns diffContainedLaws RED, on the refused-pair guard alone"
          <| fun _ ->
              // The must-fail case: `opGen` supplies no `CanHold`, so the refusal iff is only ever
              // asked in its trivial direction. The three subject laws pass; the guard does not.
              let results = Conformance.diffContainedLaws nodew idw opGen 4242 200

              Expect.equal
                  (results |> List.filter (fun r -> not r.Passed) |> List.map (fun r -> r.Law))
                  [ SampleAdequacy.lawPrefix "Conformance.diffContainedLaws"
                    + "the sample reached every refused pair the laws distinguish" ]
                  "exactly the refused-pair guard is red"

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

          // Phase 30 — the invocable-capability laws; Phase 210 added the three envelope laws.
          testCase "capabilityLaws certify validation + replay + enumeration + round-trip + envelope (Phase 30)"
          <| fun _ ->
              let results = Conformance.capabilityLaws 4242 200

              Expect.equal
                  (List.length results)
                  7
                  "validation + replay + enumeration + round-trip + the three envelope laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "capabilityLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal (Conformance.capabilityLaws 4242 200) results "same seed ⇒ identical report"

          // Phase 46 — the data-acquisition Query laws; Phase 198 added the three envelope laws.
          testCase "queryLaws certify param-validation + replay + enumeration + round-trip + envelope (Phase 46)"
          <| fun _ ->
              let results = Conformance.queryLaws 4242 200

              Expect.equal
                  (List.length results)
                  7
                  "validation + replay + enumeration + round-trip + the three envelope laws reported"

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
              let wide = Conformance.attestationLaws sw streamGen sink wideHash 4242 200

              Expect.isTrue
                  (wide |> List.forall (fun r -> r.Passed))
                  (sprintf "attestationLaws green under a wide HashFn: %A" (wide |> List.filter (fun r -> not r.Passed)))

              // seed-replay determinism
              Expect.equal
                  (Conformance.attestationLaws sw streamGen sink OpStream.defaultHash 4242 200)
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
                  Conformance.attestationLaws sw streamGen OpStream.noAttestation OpStream.defaultHash 4242 200

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
let countReg: Validator.Registry<RNode, string> =
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
let tplCount (lo, hi) =
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
let genParamsFor (fn: RNode) (rng: ConfRng.T) : Map<string, Arg<RNode>> * ConfRng.T =
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
              // Phase 223 — the stratified reference generator, so the refused-op guard is reached
              // by the generator's shape.
              let results =
                  Conformance.casLaws sw stratifiedStreamGen OpStream.defaultHash 4242 200

              Expect.equal (List.length results) 5 "match + stale + race laws, and the two Phase 223 guards"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "casLaws failed under defaultHash:\n%s" (String.concat "\n" fails)

              // the CAS is over head identity, so it is HashFn-agnostic — green under a wide HashFn too.
              let wide = Conformance.casLaws sw stratifiedStreamGen wideHash 4242 200

              Expect.isTrue
                  (wide |> List.forall (fun r -> r.Passed))
                  (sprintf "casLaws green under a wide HashFn: %A" (wide |> List.filter (fun r -> not r.Passed)))

              // seed-replay determinism
              Expect.equal
                  (Conformance.casLaws sw stratifiedStreamGen OpStream.defaultHash 4242 200)
                  results
                  "same seed ⇒ identical report"

          testCase "Phase 223 — a StreamGen that draws no refusal turns casLaws RED, on the refused-op guard alone"
          <| fun _ ->
              // The must-fail case: `Inc` only, so `match ≡ append` never compares a refusal. Every
              // subject law still passes — which is exactly the green this guard exists to refuse.
              let results =
                  Conformance.casLaws sw refusalFreeStreamGen OpStream.defaultHash 4242 200

              Expect.equal
                  (results |> List.filter (fun r -> not r.Passed) |> List.map (fun r -> r.Law))
                  [ SampleAdequacy.lawPrefix "Conformance.casLaws"
                    + "the sample reached every refused op the laws distinguish" ]
                  "exactly the refused-op guard is red"

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

// ---------------------------------------------------------------------------
//  Phase 189 — `Conformance.keyedChildrenLaws`, certified against the reference
//  witness EXTENDED with a keyed slot, and against the plain one that has none.
// ---------------------------------------------------------------------------

/// The reference node with one addition: a name-keyed CASE TABLE, which `Children` does not
/// report. It is the shape `README.md` names when it says "a case table, a fallback slot, a named
/// alternative, an argument position" — a node the domain holds and the engine cannot see. It is a
/// type of its own rather than a field on `RNode` because every other family in this suite
/// certifies the surface witness, and giving that witness an invisible position would change what
/// those runs are about.
type KNode =
    { Id: string
      Kind: string
      Children: KNode list
      Cases: (string * KNode) list }

let knodew: NodeWitness<KNode, string> =
    { Id = fun n -> n.Id
      KindTag = fun n -> n.Kind
      Children = fun n -> n.Children
      ReplaceChildren = fun n cs -> { n with Children = cs } }

/// A drawn tree carries unique ids and SOMETIMES a keyed case, so the family's declaration count
/// is exercised by the draw — but never a collision: a generator's contract is a fresh id, which
/// is exactly why the two collision laws build their subjects instead.
let private genKTree (rng: ConfRng.T) : KNode * ConfRng.T =
    let mutable counter = 0
    let mutable r = rng

    let freshId () =
        let id = sprintf "k%d" counter
        counter <- counter + 1
        id

    let rec build depth =
        let id = freshId ()

        let kids =
            if depth <= 0 then
                []
            else
                let nKids, r1 = ConfRng.intBelow 3 r
                r <- r1
                [ for _ in 1..nKids -> build (depth - 1) ]

        let caseRoll, r2 = ConfRng.intBelow 2 r
        r <- r2

        let cases =
            if caseRoll = 0 then
                []
            else
                let cid = freshId ()

                [ "default",
                  { Id = cid
                    Kind = "case"
                    Children = []
                    Cases = [] } ]

        { Id = id
          Kind = (if List.isEmpty kids then "para" else "section")
          Children = kids
          Cases = cases }

    build 2, r

let private genKFresh (existing: Set<string>) (rng: ConfRng.T) : KNode * ConfRng.T =
    let mutable r = rng

    let rec pick () =
        let v, r' = ConfRng.next r
        r <- r'
        let id = sprintf "f%d" (v % 100000)
        if existing.Contains id then pick () else id

    { Id = pick ()
      Kind = "para"
      Children = []
      Cases = [] },
    r

let kGen: OpGen<KNode, string> =
    { Tree = genKTree
      FreshNode = genKFresh
      CanHold = None }

/// The domain's OWN full walk: `Children` AND the case table. This is the check the claims ladder
/// makes the domain's obligation — the one `witness-surface-scope` says no kit law could reach
/// until this family gave the domain a way to declare the positions.
let rec private fullWalk (n: KNode) : string list =
    (n.Id :: (n.Children |> List.collect fullWalk))
    @ (n.Cases |> List.collect (fun (_, c) -> fullWalk c))

let private idsUnique (root: KNode) =
    let ks = fullWalk root
    List.length (List.distinct ks) = List.length ks

/// The NEUTERED check, and it is the realistic defect rather than a strawman: the same uniqueness
/// question asked over `Tree.ids`, which walks `Children` alone. It is what a domain writes when
/// it reaches for the engine's own scan, and it is green on every tree this suite draws.
let private surfaceOnlyUnique (root: KNode) =
    let ks = Tree.ids knodew root
    List.length (List.distinct ks) = List.length ks

let private caseNode (id: string) =
    { Id = id
      Kind = "case"
      Children = []
      Cases = [] }

let keyw: KeyedWitness<KNode, string> =
    { Surface = "the reference domain's full walk (Children + the case table)"
      HasKeyedChildren = fun n -> n.Cases |> List.map (fun (_, c) -> c.Id)
      PlaceKeyedChild =
        fun n id ->
            Some
                { n with
                    Cases = n.Cases @ [ id, caseNode id ] }
      IdsUnique = idsUnique }

/// The other half of the acceptance: the plain reference witness, which holds nothing anywhere
/// `Children` does not report, declares the empty list and says so.
let private noKeyed: KeyedWitness<RNode, string> =
    { Surface = "Tree.ids over the reference witness"
      HasKeyedChildren = fun _ -> []
      PlaceKeyedChild = fun _ _ -> None
      IdsUnique =
        fun t ->
            let ks = Tree.ids nodew t |> List.map idw.ToString
            List.length (List.distinct ks) = List.length ks }

let private lawNamed (prefix: string) (results: LawResult list) =
    results |> List.find (fun r -> r.Law.StartsWith prefix)

[<Tests>]
let keyedChildrenLawTests =
    testList
        "Conformance.keyedChildrenLaws"
        [ testCase "the reference witness with a keyed slot is the first certifier — every law green"
          <| fun _ ->
              let results = Conformance.keyedChildrenLaws keyw knodew idw kGen 1890 200

              let failed = results |> List.filter (fun r -> not r.Passed)

              if not (List.isEmpty failed) then
                  let msg =
                      failed
                      |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                      |> String.concat "\n"

                  failtestf "the keyed reference witness failed keyedChildrenLaws:\n%s" msg

              Expect.equal (List.length results) 4 "three laws + the adequacy guard"

              Expect.equal
                  (Conformance.keyedChildrenLaws keyw knodew idw kGen 1890 200)
                  results
                  "same seed ⇒ identical report"

          testCase "the census calls it Guarded, and the run reports the arms it reached"
          <| fun _ ->
              // The half `SampleAdequacyTests` does not run for a witness-taking family — it leaves
              // those to their own suite, and this is that suite.
              match
                  SampleAdequacy.census
                  |> List.tryFind (fun (n, _) -> n = "Conformance.keyedChildrenLaws")
              with
              | Some(_, Guarded _) -> ()
              | Some(_, Unconditional why) -> failtestf "censused Unconditional (%s) but it emits a guard" why
              | None -> failtest "Conformance.keyedChildrenLaws is missing from SampleAdequacy.census"

              let adequacy =
                  Conformance.keyedChildrenLaws keyw knodew idw kGen 1890 200
                  |> List.filter (fun r -> r.Law.StartsWith "sample adequacy")

              Expect.equal (List.length adequacy) 1 "exactly one adequacy law"
              Expect.isTrue (List.head adequacy).Passed "the reference witness reaches every arm"

              Expect.stringContains
                  (List.head adequacy).Law
                  "the sample reached every built arm"
                  "a witness WITH keyed positions is measured, not declared vacuous"

          testCase "go-red: a check that walks only `Children` loses both collision laws"
          <| fun _ ->
              // The defect the family exists to catch, and the one the ladder row describes: the
              // engine's own scan, adopted as the domain's check. It is green on every drawn tree,
              // which is why the collision subjects are BUILT.
              let results =
                  Conformance.keyedChildrenLaws
                      { keyw with
                          IdsUnique = surfaceOnlyUnique }
                      knodew
                      idw
                      kGen
                      1890
                      200

              Expect.isTrue
                  (lawNamed "the domain's id check accepts" results).Passed
                  "a surface-only check still accepts a clean tree — the acceptance law is not what catches it"

              let clash =
                  lawNamed "the domain's id check refuses an id held in a keyed position and" results

              Expect.isFalse clash.Passed "a surface-only check must lose the keyed-vs-surface law"

              Expect.stringContains
                  (clash.Counterexample |> Option.defaultValue "")
                  "ACCEPTED a tree holding"
                  "the counterexample says what was accepted"

              let twice = lawNamed "the domain's id check refuses an id held in two keyed" results

              Expect.isFalse twice.Passed "a surface-only check must lose the twice-keyed law too"

          testCase "go-red: a check that refuses everything loses the acceptance law"
          <| fun _ ->
              // Without this arm the two collision laws certify `fun _ -> false`, which refuses
              // every tree the domain will ever hold and is not a check at all.
              let results =
                  Conformance.keyedChildrenLaws { keyw with IdsUnique = fun _ -> false } knodew idw kGen 1890 200

              let accepts = lawNamed "the domain's id check accepts" results
              Expect.isFalse accepts.Passed "a check that refuses everything must lose the acceptance law"

              Expect.stringContains
                  (accepts.Counterexample |> Option.defaultValue "")
                  "REFUSED a tree whose full walk"
                  "the counterexample says what was refused"

          testCase "a witness declaring no keyed position passes VACUOUSLY, and the report says so"
          <| fun _ ->
              let results = Conformance.keyedChildrenLaws noKeyed nodew idw opGen 1890 100

              Expect.isTrue (results |> List.forall (fun r -> r.Passed)) "a domain with no keyed position is not failed"

              Expect.equal (List.length results) 4 "the report keeps its shape whatever the witness declares"

              let adequacy = lawNamed "sample adequacy" results

              Expect.stringContains
                  adequacy.Law
                  "vacuous BY DECLARATION"
                  "the adequacy line distinguishes 'nothing to certify' from 'three laws certified'"

          testCase "a witness that declares keyed positions and can build none FAILS the guard"
          <| fun _ ->
              // The case that must not be read as the one above. Declaring keyed positions is a
              // claim this family can measure; giving it no way to build one makes the claim
              // unmeasurable, which is not the same as having nothing to claim.
              let results =
                  Conformance.keyedChildrenLaws
                      { keyw with
                          PlaceKeyedChild = fun _ _ -> None }
                      knodew
                      idw
                      kGen
                      1890
                      200

              let adequacy = lawNamed "sample adequacy" results
              Expect.isFalse adequacy.Passed "an arm nothing could reach must be reported"

              Expect.stringContains
                  (adequacy.Counterexample |> Option.defaultValue "")
                  "keyed id in the witness surface"
                  "and it names the arm that was never built" ]

// ---------------------------------------------------------------------------
//  Phase 211 — `Conformance.propagationEvaluatorLaws`, certified against an
//  in-repo FORMULA SHEET. It is the family's adequacy witness because no adopter
//  evaluator exists yet: measured 2026-09-24, nothing outside this repository
//  calls `Propagation.eval` / `evalFrom`, and inside it every caller is a test's
//  toy. The sheet is not one, in the ways the family distinguishes: it FAILS (a
//  division by zero, a read that answers nothing), and `IfPos` reads only the
//  branch it takes, so what a node asks for is a subset of what it declares and
//  moves with its inputs.
// ---------------------------------------------------------------------------

/// A formula, as a spreadsheet cell holds one.
type SheetFormula =
    | Lit of int
    | Ref of string
    | Add of SheetFormula * SheetFormula
    | Mul of SheetFormula * SheetFormula
    | Div of SheetFormula * SheetFormula
    | IfPos of SheetFormula * SheetFormula * SheetFormula

/// Cells `c0` … `cN`, each holding a formula over earlier cells — and, now and then, over a cell
/// nobody holds (`zz`), which reads as `#REF!`.
type RefSheet = Map<string, SheetFormula>

let rec private refsOf (f: SheetFormula) : Set<string> =
    match f with
    | Lit _ -> Set.empty
    | Ref r -> Set.singleton r
    | Add(a, b)
    | Mul(a, b)
    | Div(a, b) -> Set.union (refsOf a) (refsOf b)
    | IfPos(c, t, e) -> Set.unionMany [ refsOf c; refsOf t; refsOf e ]

let rec private evalFormula (resolve: string -> int option) (f: SheetFormula) : Result<int, string> =
    let both a b k =
        match evalFormula resolve a with
        | Error e -> Error e
        | Ok x ->
            match evalFormula resolve b with
            | Error e -> Error e
            | Ok y -> k x y

    match f with
    | Lit n -> Ok n
    | Ref r ->
        match resolve r with
        | Some v -> Ok v
        | None -> Error("#REF! " + r)
    | Add(a, b) -> both a b (fun x y -> Ok(x + y))
    | Mul(a, b) -> both a b (fun x y -> Ok(x * y))
    | Div(a, b) -> both a b (fun x y -> if y = 0 then Error "#DIV/0!" else Ok(x / y))
    | IfPos(c, t, e) ->
        match evalFormula resolve c with
        | Error m -> Error m
        | Ok x ->
            if x > 0 then
                evalFormula resolve t
            else
                evalFormula resolve e

/// The sheet's cell evaluator — what it hands `Propagation.eval` / `evalFrom`.
let sheetEvalNode (s: RefSheet) (resolve: string -> int option) (id: string) : Result<int, string> =
    match Map.tryFind id s with
    | Some f -> evalFormula resolve f
    | None -> Error("no cell " + id)

let rec private genFormula (k: int) (depth: int) (r: ConfRng.T) : SheetFormula * ConfRng.T =
    let pick, r = ConfRng.intBelow (if depth = 0 then 3 else 7) r

    let lit r =
        let n, r = ConfRng.intBelow 10 r
        Lit n, r

    let binary mk r =
        let a, r = genFormula k (depth - 1) r
        let b, r = genFormula k (depth - 1) r
        mk (a, b), r

    match pick with
    | 0
    | 1 ->
        if k = 0 then
            lit r
        else
            let d, r = ConfRng.intBelow 20 r

            if d = 0 then
                Ref "zz", r
            else
                let j, r = ConfRng.intBelow k r
                Ref(sprintf "c%d" j), r
    | 2 -> lit r
    | 3 -> binary Add r
    | 4 -> binary Mul r
    | 5 -> binary Div r
    | _ ->
        let c, r = genFormula k (depth - 1) r
        let t, r = genFormula k (depth - 1) r
        let e, r = genFormula k (depth - 1) r
        IfPos(c, t, e), r

let private genSheet (r: ConfRng.T) : RefSheet * ConfRng.T =
    let extra, r = ConfRng.intBelow 6 r

    [ 0 .. extra + 1 ]
    |> List.fold
        (fun (s, r) k ->
            let f, r = genFormula k 2 r
            Map.add (sprintf "c%d" k) f s, r)
        (Map.empty, r)

/// An edit that keeps the formula's references: the first literal bumped.
let rec private bumpLit (f: SheetFormula) : SheetFormula option =
    let pair mk a b =
        match bumpLit a with
        | Some a' -> Some(mk (a', b))
        | None -> bumpLit b |> Option.map (fun b' -> mk (a, b'))

    match f with
    | Lit n -> Some(Lit((n + 1) % 10))
    | Ref _ -> None
    | Add(a, b) -> pair Add a b
    | Mul(a, b) -> pair Mul a b
    | Div(a, b) -> pair Div a b
    | IfPos(c, t, e) ->
        match bumpLit c with
        | Some c' -> Some(IfPos(c', t, e))
        | None -> pair (fun (t', e') -> IfPos(c, t', e')) t e

/// An edit that keeps the formula's references: the root operator turned, or an `IfPos`'s branches
/// swapped — which moves the reads the cell ASKS for without moving the ones it declares.
let private swapOp (f: SheetFormula) : SheetFormula =
    match f with
    | Add(a, b) -> Mul(a, b)
    | Mul(a, b) -> Div(a, b)
    | Div(a, b) -> Add(a, b)
    | IfPos(c, t, e) -> IfPos(c, e, t)
    | other -> other

/// One cell edited, and the id of the cell edited. Seven edits in eight keep the cell's references:
/// a literal bumped, an operator turned, or the formula divided by zero. The division is there so
/// that an evaluation which SUCCEEDED before the edit fails after it, which is the whole-`Result`
/// arm the agreement law must meet (a failure already present before the edit leaves no prior to
/// replay from, and measured without it the arm was reached twice in two hundred). One edit in
/// eight rewrites the formula outright and so may MOVE the dependency map, which the family checks
/// for honesty and deliberately does not replay.
let private sheetEdit (s: RefSheet) (r: ConfRng.T) : RefSheet * string * ConfRng.T =
    let id, r = ConfRng.choose (s |> Map.toList |> List.map fst) r
    let f = Map.find id s
    let kind, r = ConfRng.intBelow 8 r

    let f', r =
        match kind with
        | 0
        | 1
        | 2 -> (bumpLit f |> Option.defaultWith (fun () -> swapOp f)), r
        | 3
        | 4 ->
            let g = swapOp f
            (if g = f then bumpLit f |> Option.defaultValue f else g), r
        | 5
        | 6 -> Div(f, Lit 0), r
        | _ -> genFormula (int (id.Substring 1)) 2 r

    Map.add id f' s, id, r

/// The formula sheet as an evaluator witness — the family's in-repo adopter. It names, as its
/// change set, exactly the cell it edited: the honest answer.
let sheetw: EvaluatorWitness<RefSheet, int> =
    { Surface = "the reference formula sheet's cell evaluator"
      Model = genSheet
      Deps = fun s -> s |> Map.map (fun _ f -> refsOf f)
      EvalNode = sheetEvalNode
      Change =
        fun s r ->
            let s', id, r = sheetEdit s r
            (s', Set.singleton id), r }

let private evaluatorLaw (prefix: string) (results: LawResult list) =
    results |> List.find (fun r -> r.Law.StartsWith prefix)

[<Tests>]
let propagationEvaluatorLawTests =
    testList
        "Conformance.propagationEvaluatorLaws"
        [ testCase "the formula sheet is the first certifier — every law green, every arm reached"
          <| fun _ ->
              let results = Conformance.propagationEvaluatorLaws sheetw 2110 200
              let failed = results |> List.filter (fun r -> not r.Passed)

              if not (List.isEmpty failed) then
                  let msg =
                      failed
                      |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                      |> String.concat "\n"

                  failtestf "the formula sheet failed propagationEvaluatorLaws:\n%s" msg

              Expect.equal (List.length results) 4 "three laws + the adequacy guard"

              Expect.equal (Conformance.propagationEvaluatorLaws sheetw 2110 200) results "same seed ⇒ identical report"

          testCase "the census calls it Guarded, and the run reports the arms it reached"
          <| fun _ ->
              match
                  SampleAdequacy.census
                  |> List.tryFind (fun (n, _) -> n = "Conformance.propagationEvaluatorLaws")
              with
              | Some(_, Guarded _) -> ()
              | Some(_, Unconditional why) -> failtestf "censused Unconditional (%s) but it emits a guard" why
              | None -> failtest "Conformance.propagationEvaluatorLaws is missing from SampleAdequacy.census"

              let adequacy =
                  Conformance.propagationEvaluatorLaws sheetw 2110 200
                  |> List.filter (fun r -> r.Law.StartsWith "sample adequacy")

              Expect.equal (List.length adequacy) 1 "exactly one adequacy law"
              Expect.isTrue (List.head adequacy).Passed "the formula sheet reaches every arm"

          testCase "go-red: an IMPURE evaluator — one that reads a mutable cell — loses the purity law"
          <| fun _ ->
              // A clock the evaluator advances and consults on every call. Nothing it reads through
              // the resolver moved, so Phase 209's restriction cannot see it; this law can.
              let clock = ref 0

              let impure =
                  { sheetw with
                      EvalNode =
                          fun s resolve id ->
                              clock.Value <- clock.Value + 1
                              sheetEvalNode s resolve id |> Result.map (fun v -> v + clock.Value % 2) }

              let law =
                  Conformance.propagationEvaluatorLaws impure 2110 200
                  |> evaluatorLaw "the domain's evaluator is a function of what it reads"

              Expect.isFalse law.Passed "an evaluator consulting ambient state must be refused"

          testCase
              "go-red: a DISHONEST change set — the edit moved a cell the set does not name — loses honesty and agreement"
          <| fun _ ->
              // The sheet edits one cell and names a DIFFERENT one: the evaluator differs off the
              // named ids, which is the clause `agree_off` states and nothing else checks.
              let dishonest =
                  { sheetw with
                      Change =
                          fun s r ->
                              let s', edited, r = sheetEdit s r

                              let other = s |> Map.toList |> List.map fst |> List.find (fun id -> id <> edited)

                              (s', Set.singleton other), r }

              let results = Conformance.propagationEvaluatorLaws dishonest 2110 200

              Expect.isFalse
                  (evaluatorLaw "off the change set the domain names" results).Passed
                  "a change set that omits the edited cell must be refused"

              Expect.isFalse
                  (evaluatorLaw "evalFrom of the edited evaluator" results).Passed
                  "and the replay it licenses disagrees with a full evaluation"

              Expect.isTrue
                  (evaluatorLaw "the domain's evaluator is a function of what it reads" results).Passed
                  "the evaluator itself is still pure — the defect is the change set's"

          testCase "go-red: an edit that moves only what an unnamed cell ASKS for loses honesty and nothing else"
          <| fun _ ->
              // The premise's other half (`touches_off`): the edit leaves every value where it was
              // and makes one cell it does not name ask for every read it declares before it
              // evaluates. The replay still agrees — no value moved — so only the reads half of the
              // honesty law can see it, which is what shows that half has teeth.
              let spy: EvaluatorWitness<RefSheet * string option, int> =
                  { Surface = "the reference sheet, one unnamed cell asking reads it ignores"
                    Model =
                      fun r ->
                          let s, r = genSheet r
                          (s, None), r
                    Deps = fun (s, _) -> sheetw.Deps s
                    EvalNode =
                      fun (s, spyAt) resolve id ->
                          if spyAt = Some id then
                              for d in refsOf (Map.find id s) do
                                  resolve d |> ignore

                          sheetEvalNode s resolve id
                    Change =
                      fun (s, _) r ->
                          match s |> Map.filter (fun _ f -> not (Set.isEmpty (refsOf f))) |> Map.toList with
                          | [] -> ((s, None), Set.empty), r
                          | withReads ->
                              let id, r = ConfRng.choose (List.map fst withReads) r
                              ((s, Some id), Set.empty), r }

              let results = Conformance.propagationEvaluatorLaws spy 2110 200

              Expect.isFalse
                  (evaluatorLaw "off the change set the domain names" results).Passed
                  "a change set that omits a cell whose asked reads moved must be refused"

              Expect.isTrue (evaluatorLaw "evalFrom of the edited evaluator" results).Passed "no value moved"

              Expect.isTrue
                  (evaluatorLaw "the domain's evaluator is a function of what it reads" results).Passed
                  "and the evaluator is pure"

          testCase "go-red: an evaluator that never fails starves the guard, naming the arm"
          <| fun _ ->
              // Every failure mapped to a value: pure, honest, and green on all three laws — and
              // the whole-`Result` comparison never met an `Error`, which is what the guard is for.
              let neverFails =
                  { sheetw with
                      EvalNode =
                          fun s resolve id ->
                              match sheetEvalNode s resolve id with
                              | Error _ -> Ok 0
                              | ok -> ok }

              let results = Conformance.propagationEvaluatorLaws neverFails 2110 200
              let adequacy = evaluatorLaw "sample adequacy" results
              Expect.isFalse adequacy.Passed "an arm nothing reached must be reported"

              let why = adequacy.Counterexample |> Option.defaultValue ""
              // The counterexample renders every count and then the arms it never reached; the arm
              // list must be exactly the one this witness starves.
              Expect.stringContains why "never reached failing evaluator —" "it names that arm, and only that arm" ]

// ---------------------------------------------------------------------------
//  Phase 220 — the refusable base-run families are `Guarded` over accepted / refused
// ---------------------------------------------------------------------------

/// The counter reducer driven by a generator that only ever increments, so it never reaches a
/// refusal. Every subject law is green over it, and until Phase 220 `certifyStream` certified it —
/// totality included, although no op it drew could have exercised a typed refusal.
let private incOnlyGen: StreamGen<CounterOp, int> =
    { State0 = 0
      Op =
        fun rng ->
            let n, r = ConfRng.intBelow 5 rng
            Inc n, r }

/// A lone leaf that holds nothing. The ops are the KIT's (`genOp`), not the domain's, and over any
/// tree `genOp` draws a reorder (always accepted) and a remove of the root (always refused), so
/// across a real run opAlgebra reaches both sides whatever the domain generates — measured in
/// Phase 220, and the reason the guard's teeth are shown at ONE iteration: a run too short to have
/// reached both sides is exactly the run the guard exists to refuse. No holder, so no built arm.
let private loneLeafGen: OpGen<RNode, string> =
    { Tree = fun rng -> RNode.leaf "only" "para" "v", rng
      FreshNode = genFresh
      CanHold = Some(fun _ -> false) }

let private guardNamed (family: string) (side: string) (results: LawResult list) =
    results
    |> List.find (fun r ->
        r.Law = SampleAdequacy.lawPrefix family
                + "the sample reached every "
                + side
                + " the laws distinguish")

let private subjectOf (results: LawResult list) =
    results
    |> List.filter (fun r -> not (r.Law.StartsWith SampleAdequacy.guardOpening))

[<Tests>]
let refusableFamilyTests =
    testList
        "Conformance.refusableFamilies"
        [ testCase "the reference witness reaches both sides of opAlgebra and reducer"
          <| fun _ ->
              for family, results in
                  [ "Conformance.opAlgebra", Conformance.opAlgebra nodew idw opGen 999 200
                    "Conformance.reducer", Conformance.reducer sw.Apply streamGen None 314 200 ] do
                  for side in [ "accepted op"; "refused op" ] do
                      let g = guardNamed family side results
                      Expect.isTrue g.Passed (sprintf "%s: %s — %A" family side g.Counterexample)

          testCase "go-red: a reducer whose generator draws no refusal starves the guard, and certifyStream goes RED"
          <| fun _ ->
              let results = Conformance.reducer sw.Apply incOnlyGen None 314 200

              Expect.isTrue
                  (subjectOf results |> List.forall (fun r -> r.Passed))
                  "every subject law is green — which is the problem the guard exists for"

              Expect.isFalse
                  (guardNamed "Conformance.reducer" "refused op" results).Passed
                  "the refused side is starved"

              Expect.isTrue (guardNamed "Conformance.reducer" "accepted op" results).Passed "the accepted side is not"

              let measured =
                  SampleAdequacy.cases "Conformance.reducer" (Guarded [ "accepted"; "refused" ]) 200 results

              Expect.isTrue (SampleAdequacy.isVacuous measured) "the census reads it as starved, not as a count"

              let report = Conformance.certifyStream sw incOnlyGen OpStream.defaultHash 271 200

              Expect.isFalse
                  report.AllPassed
                  "the aggregate verdict MOVES: this domain certified green before Phase 220"

              Expect.equal
                  (List.length report.Results)
                  7
                  "a starved guard does not short-circuit the stream laws — reducer (2 + 2 guards) + stream (3)"

          testCase "go-red: an opAlgebra run too short to reach both sides reports the guard, not a pass"
          <| fun _ ->
              let results = Conformance.opAlgebra nodew idw loneLeafGen 999 1

              Expect.isTrue
                  (subjectOf results |> List.forall (fun r -> r.Passed))
                  "every subject law is green over one drawn op"

              let starved =
                  [ "accepted op"; "refused op" ]
                  |> List.filter (fun side -> not (guardNamed "Conformance.opAlgebra" side results).Passed)

              Expect.equal (List.length starved) 1 "one op reaches one side, and the other is reported starved"

          testCase "go-red: certify's verdict moves with opAlgebra's guard"
          <| fun _ ->
              let report =
                  Conformance.certify nodew idw loneLeafGen sw streamGen OpStream.defaultHash 12345 1

              let redGuards =
                  report.Results
                  |> List.filter (fun r ->
                      not r.Passed
                      && r.Law.StartsWith(SampleAdequacy.lawPrefix "Conformance.opAlgebra"))

              Expect.isFalse report.AllPassed "a run that reached one side of apply no longer certifies"
              Expect.isNonEmpty redGuards "and the red line is opAlgebra's guard, naming the starved side" ]
