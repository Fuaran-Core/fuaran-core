module Fuaran.Core.Tests.WitnessLawTests

// Phase 253 — witness-law conformance. The runner that makes the F1 bug class
// self-diagnosing: a leaf-no-op `ReplaceChildren` fails a *witness* law with a localised
// message, instead of surfacing as a downstream apply∘invert failure.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// A small random tree generator over the reference RNode (all containers — RNode's
// ReplaceChildren is total, so the witness laws hold).
let private genTree (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable counter = 0
    let mutable r = rng

    let freshId () =
        let s = sprintf "n%d" counter
        counter <- counter + 1
        s

    let rec build depth =
        let id = freshId ()
        let nKids, r' = if depth <= 0 then 0, r else ConfRng.intBelow 3 r
        r <- r'
        RNode.node id "section" [ for _ in 1..nKids -> build (depth - 1) ]

    let t = build 2
    t, r

let private genFresh (existing: Set<string>) (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable r = rng

    let rec pick () =
        let v, r' = ConfRng.next r
        r <- r'
        let id = sprintf "f%d" (v % 100000)
        if existing.Contains id then pick () else id

    RNode.leaf (pick ()) "para" "x", r

let private opGen: OpGen<RNode, string> =
    { Tree = genTree
      FreshNode = genFresh
      CanHold = None }

[<Tests>]
let tests =
    testList
        "Conformance.witnessLaws"
        [ testCase "the reference witness passes every witness law"
          <| fun _ ->
              let results = Conformance.witnessLaws nodew idw opGen 7 100
              let failed = results |> List.filter (fun r -> not r.Passed)

              if not (List.isEmpty failed) then
                  let msg =
                      failed
                      |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                      |> String.concat "\n"

                  failtestf "reference witness failed a witness law:\n%s" msg

              Expect.equal (List.length results) 5 "five witness laws reported (Phase 290 added the identities law)"

          testCase "a leaf-no-op ReplaceChildren fails the round-trip law with a localised message (F1)"
          <| fun _ ->
              let broken =
                  { nodew with
                      ReplaceChildren = fun n _ -> n } // ignores the new children

              let results = Conformance.witnessLaws broken idw opGen 7 100

              let rcLaw =
                  results |> List.find (fun r -> r.Law.StartsWith "ReplaceChildren round-trip")

              Expect.isFalse rcLaw.Passed "the round-trip law fails"

              match rcLaw.Counterexample with
              | Some msg -> Expect.stringContains msg "ReplaceChildren is not total" "the message localises the defect"
              | None -> failtest "expected a counterexample"

          testCase "certify runs witness laws first and short-circuits on a witness defect"
          <| fun _ ->
              // reuse the counter stream witness from ConformanceTests is out of scope here;
              // a witness defect must short-circuit, so the report is just the 5 witness laws.
              let broken =
                  { nodew with
                      ReplaceChildren = fun n _ -> n }

              let sw: StreamWitness<int, int, string> =
                  { Apply = fun op st -> Ok(st + op)
                    Encode = string
                    Decode = fun _ -> Ok 0 }

              let report =
                  Conformance.certify broken idw opGen sw { State0 = 0; Op = fun r -> 1, r } OpStream.defaultHash 7 100

              Expect.isFalse report.AllPassed "a witness defect fails certification"
              Expect.equal (List.length report.Results) 5 "algebra/stream laws short-circuited"

          // Phase 290 — the two identities are one relation. A case-insensitive `Equals` over a
          // case-preserving `ToString` passes reflexivity and the round trip, and every other law
          // here; it must fail THIS one, on a BUILT pair, because the reference generator never
          // draws two ids differing only in case.
          testCase "a witness whose Equals is coarser than its ToString is refused (Phase 290)"
          <| fun _ ->
              let caseBlind =
                  { idw with
                      Equals = fun a b -> System.String.Equals(a, b, System.StringComparison.OrdinalIgnoreCase) }

              let results = Conformance.witnessLaws nodew caseBlind opGen 7 100

              let law =
                  results |> List.find (fun r -> r.Law.StartsWith "IdWitness identities agree")

              Expect.isFalse law.Passed "Equals a b ⇔ ToString a = ToString b fails for a case-blind Equals"

              match law.Counterexample with
              | Some msg ->
                  Expect.stringContains msg "(built from" "the pair that bites is the BUILT one, not a drawn one"
                  Expect.stringContains msg "two identities" "the message names the defect"
              | None -> failtest "expected a counterexample"

              // Every OTHER witness law is still green for it — which is exactly why the law exists.
              for r in results do
                  if not (r.Law.StartsWith "IdWitness identities agree") then
                      Expect.isTrue r.Passed (sprintf "%s still passes for the case-blind witness" r.Law)

              // And `certify` short-circuits on it, so the kit refuses the witness by name rather
              // than the apply engine refusing the first duplicate id.
              let sw: StreamWitness<int, int, string> =
                  { Apply = fun op st -> Ok(st + op)
                    Encode = string
                    Decode = fun _ -> Ok 0 }

              let report =
                  Conformance.certify
                      nodew
                      caseBlind
                      opGen
                      sw
                      { State0 = 0; Op = fun r -> 1, r }
                      OpStream.defaultHash
                      7
                      100

              Expect.isFalse report.AllPassed "certification refuses the case-blind witness"
              Expect.equal (List.length report.Results) 5 "and short-circuits at the witness laws"

          testCase "a witness whose ToString is coarser than its Equals is refused too (Phase 290)"
          <| fun _ ->
              // The other direction: two ids `Equals` keeps apart that `ToString` renders alike.
              let lossy =
                  { idw with
                      ToString = fun s -> s.ToUpperInvariant() }

              let results = Conformance.witnessLaws nodew lossy opGen 7 100

              let law =
                  results |> List.find (fun r -> r.Law.StartsWith "IdWitness identities agree")

              Expect.isFalse law.Passed "ToString a = ToString b with Equals a b false is refused" ]
