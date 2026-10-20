module Fuaran.Core.Tests.ProofOracle.UnitTests

// The `Proofs.Oracle` differential for `proofs/Unit.fst`: the extracted model
// (`proofs/oracle/Unit.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 428: the unit algebra beside Unit.fst, the field codec beside WireColumn.fst §10 ----

          testCase "the unit oracle agrees with Unit.parse and Unit.render over the drawn pool and the refusal corpus"
          <| fun _ ->
              // Every admitted atom bare and under every prefix it takes, the corpus (non-canonical
              // spellings the parser reads, and a text per refusal class), and 600 drawn products;
              // then 599 drawn pairs through the algebra. Every unit either side answers must be
              // CANONICAL in the model's sense — the premise `parse_render` is stated over.
              let t = onBigStack (fun () -> UnitDiff.generated 428 600)

              Differential.expectAgrees "the unit oracle" t

              Expect.isGreaterThan
                  (Tally.count "Texts" t)
                  700
                  (sprintf "texts asked of both parsers (%d)" (Tally.count "Texts" t))

              Expect.isGreaterThan
                  (Tally.count "Parsed" t)
                  650
                  (sprintf "texts both parsed (%d)" (Tally.count "Parsed" t))

              Expect.equal
                  (Tally.count "NotCanonical" t)
                  0
                  "every unit the shipped parser answers is canonical in the model"

              for cls in
                  [ "Empty"
                    "Malformed"
                    "UnknownAtom"
                    "Annotation"
                    "ArbitraryUnit"
                    "NonRatioUnit"
                    "PrefixNotAllowed"
                    "NumericFactor" ] do
                  Expect.isTrue
                      ((Tally.reached "Refusals" t).Contains cls)
                      (sprintf "the corpus reached a %s refusal (reached: %A)" cls (Tally.reached "Refusals" t))

          testCase "the unit oracle agrees with mul, div, pow, compatible and conversionFactor over drawn pairs"
          <| fun _ ->
              // The same run: `mul`, `div`, `pow` at a drawn exponent and at one past the bound
              // (production's ArgumentException is the model's None), `compatible`, and the factor
              // both ways — production's Ratio in lowest terms against the model's reduced pair,
              // which is where uniqueness of the reduced form, not proved, is held.
              let t = onBigStack (fun () -> UnitDiff.generated 428 600)

              Differential.expectAgrees "the unit oracle" t

              Expect.isGreaterThan
                  (Tally.count "Pairs" t)
                  1100
                  (sprintf "pairs asked of the algebra (%d)" (Tally.count "Pairs" t))

              Expect.isGreaterThan
                  (Tally.count "Compatible" t)
                  600
                  (sprintf "compatible pairs, with a factor on both sides (%d)" (Tally.count "Compatible" t))

              Expect.isGreaterThan
                  (Tally.count "Overflowed" t)
                  400
                  (sprintf "pairs where the big power left the bound on both sides (%d)" (Tally.count "Overflowed" t))

          testCase "a canonicaliser that skips the merge loses - on exactly the reordered draws"
          <| fun _ ->
              // The go-red: the model's `render` handed the CONCATENATION of two factor lists where
              // `mul` merges them. It loses on exactly the pairs whose concatenation is not already
              // the merge — a shared atom, or an atom of the second sorting before one of the first
              // among the numerators or the denominators — and on no other.
              let t = onBigStack (fun () -> UnitDiff.concatenated 428 600)

              Expect.isGreaterThan
                  (Tally.count "Reordered" t)
                  150
                  (sprintf "pairs whose concatenation is not the merge (%d)" (Tally.count "Reordered" t))

              Expect.isGreaterThan
                  ((Tally.count "Pairs" t) - (Tally.count "Reordered" t))
                  50
                  (sprintf
                      "pairs whose concatenation is the merge (%d)"
                      ((Tally.count "Pairs" t) - (Tally.count "Reordered" t)))

              Expect.equal
                  (Tally.count "LiteralLost" t)
                  (Tally.count "Reordered" t)
                  "the concatenation lost on the reordered pairs, and on no other" ]
