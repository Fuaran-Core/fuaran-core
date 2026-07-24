module Fuaran.Core.Tests.ReducerTests

// Phase 254 — domain-reducer conformance. Certify a domain's own `apply` reducer
// (totality + replay determinism + optional envelope shape), not just the tree witness.

open Expecto
open Fuaran.Core

// A counter reducer: Inc/Dec; Dec below zero is a typed rejection that names the floor.
type private Op =
    | Inc of int
    | Dec of int

type private Rej = WouldGoNegative of by: int * floor: int

let private apply op st =
    match op with
    | Inc n -> Ok(st + n)
    | Dec n ->
        if st - n < 0 then
            Error(WouldGoNegative(n, 0))
        else
            Ok(st - n)

let private genOp (rng: ConfRng.T) : Op * ConfRng.T =
    let kind, r1 = ConfRng.intBelow 2 rng
    let n, r2 = ConfRng.intBelow 5 r1
    (if kind = 0 then Inc n else Dec n), r2

let private gen: StreamGen<Op, int> = { State0 = 0; Op = genOp }

// A StreamWitness over the counter Op — wires the same reducer into the op-stream seam so
// `certifyStream` (the F7 reducer-only / heterogeneous-tree entry point) can be exercised.
let private encode =
    function
    | Inc n -> "i" + string n
    | Dec n -> "d" + string n

let private decode (s: string) : Result<Op, string> =
    if s.Length < 2 then
        Error("short: " + s)
    else
        match System.Int32.TryParse(s.Substring 1) with
        | true, n when s.[0] = 'i' -> Ok(Inc n)
        | true, n when s.[0] = 'd' -> Ok(Dec n)
        | _ -> Error("bad op: " + s)

let private sw: StreamWitness<Op, int, Rej> =
    { Apply = apply
      Encode = encode
      Decode = decode }

[<Tests>]
let tests =
    testList
        "Conformance.reducer"
        [ testCase "a total, deterministic reducer passes"
          <| fun _ ->
              let results = Conformance.reducer apply gen None 314 200
              Expect.isTrue (results |> List.forall (fun r -> r.Passed)) "totality + determinism pass"
              Expect.equal (List.length results) 2 "two laws when no envelope predicate"

          testCase "a throwing reducer fails the totality law"
          <| fun _ ->
              let throwing op st =
                  match op with
                  | Dec 3 -> failwith "boom" // a real exception, not a rejection
                  | _ -> apply op st

              let results = Conformance.reducer throwing gen None 1 200

              let totality = results |> List.find (fun r -> r.Law.StartsWith "reducer totality")

              Expect.isFalse totality.Passed "totality fails on a throwing reducer"
              Expect.isSome totality.Counterexample "with a seeded counterexample"

          testCase "the optional envelope-shape law is reported and passes for well-formed rejections"
          <| fun _ ->
              // every WouldGoNegative carries its floor → it 'names the alternative'
              let namesAlternatives =
                  function
                  | WouldGoNegative(_, _) -> true

              let results = Conformance.reducer apply gen (Some namesAlternatives) 99 200
              Expect.equal (List.length results) 3 "envelope law added when a predicate is supplied"
              Expect.isTrue (results |> List.forall (fun r -> r.Passed)) "all three pass"

          testCase "the envelope-shape law fails when a rejection is judged empty"
          <| fun _ ->
              let alwaysEmpty = fun (_: Rej) -> false // pretend no rejection names its alternatives

              let results = Conformance.reducer apply gen (Some alwaysEmpty) 99 200

              let envelope =
                  results |> List.find (fun r -> r.Law.StartsWith "rejection enumerates")

              Expect.isFalse envelope.Passed "envelope law fails when predicate rejects"

          testCase "certifyStream certifies a reducer-only domain (op-stream laws + reducer laws, no tree)"
          <| fun _ ->
              // F7: a domain with no uniform node tree certifies via the op-stream + reducer
              // seams alone. The report bundles streamLaws (3) + reducer laws (2) = 5.
              let report = Conformance.certifyStream sw gen OpStream.defaultHash 271 200
              Expect.isTrue report.AllPassed "a well-formed reducer-only domain certifies green"
              Expect.equal (List.length report.Results) 5 "3 op-stream laws + 2 reducer laws"

          testCase "certifyStream surfaces a throwing reducer through the bundled report"
          <| fun _ ->
              let throwing op st =
                  match op with
                  | Dec 3 -> failwith "boom"
                  | _ -> apply op st

              let badSw = { sw with Apply = throwing }
              let report = Conformance.certifyStream badSw gen OpStream.defaultHash 1 200
              Expect.isFalse report.AllPassed "a throwing reducer fails the bundled certification" ]
