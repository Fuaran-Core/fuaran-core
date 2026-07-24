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

              Expect.equal (List.length results) 4 "four witness laws reported"

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
              // a witness defect must short-circuit, so the report is just the 4 witness laws.
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
              Expect.equal (List.length report.Results) 4 "algebra/stream laws short-circuited" ]
