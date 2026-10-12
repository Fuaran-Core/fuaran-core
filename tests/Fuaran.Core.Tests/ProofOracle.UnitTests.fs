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

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the unit oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              Expect.isGreaterThan t.Texts 700 (sprintf "texts asked of both parsers (%d)" t.Texts)
              Expect.isGreaterThan t.Parsed 650 (sprintf "texts both parsed (%d)" t.Parsed)
              Expect.equal t.NotCanonical 0 "every unit the shipped parser answers is canonical in the model"

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
                      (t.Refusals.Contains cls)
                      (sprintf "the corpus reached a %s refusal (reached: %A)" cls t.Refusals)

          testCase "the unit oracle agrees with mul, div, pow, compatible and conversionFactor over drawn pairs"
          <| fun _ ->
              // The same run: `mul`, `div`, `pow` at a drawn exponent and at one past the bound
              // (production's ArgumentException is the model's None), `compatible`, and the factor
              // both ways — production's Ratio in lowest terms against the model's reduced pair,
              // which is where uniqueness of the reduced form, not proved, is held.
              let t = onBigStack (fun () -> UnitDiff.generated 428 600)

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the unit oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              Expect.isGreaterThan t.Pairs 1100 (sprintf "pairs asked of the algebra (%d)" t.Pairs)

              Expect.isGreaterThan
                  t.Compatible
                  600
                  (sprintf "compatible pairs, with a factor on both sides (%d)" t.Compatible)

              Expect.isGreaterThan
                  t.Overflowed
                  400
                  (sprintf "pairs where the big power left the bound on both sides (%d)" t.Overflowed)

          testCase "a canonicaliser that skips the merge loses - on exactly the reordered draws"
          <| fun _ ->
              // The go-red: the model's `render` handed the CONCATENATION of two factor lists where
              // `mul` merges them. It loses on exactly the pairs whose concatenation is not already
              // the merge — a shared atom, or an atom of the second sorting before one of the first
              // among the numerators or the denominators — and on no other.
              let t = onBigStack (fun () -> UnitDiff.concatenated 428 600)

              Expect.isGreaterThan
                  t.Reordered
                  150
                  (sprintf "pairs whose concatenation is not the merge (%d)" t.Reordered)

              Expect.isGreaterThan
                  (t.Pairs - t.Reordered)
                  50
                  (sprintf "pairs whose concatenation is the merge (%d)" (t.Pairs - t.Reordered))

              Expect.equal t.LiteralLost t.Reordered "the concatenation lost on the reordered pairs, and on no other" ]
