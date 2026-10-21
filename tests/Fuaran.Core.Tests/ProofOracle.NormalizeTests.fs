module Fuaran.Core.Tests.ProofOracle.NormalizeTests

// The `Proofs.Oracle` differential for `proofs/Normalize.fst`: the extracted model
// (`proofs/oracle/Normalize.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

/// The normalised script's render pair: production's script carried across the faithful bridge and
/// the model's, each rendered op by op through the model's own rendering.
let private normalized: Differential<SkeletonOp<RNode, string> list, TreeOps.op list> =
    let render ops =
        ops |> List.map renderModelOp |> String.concat " ; "

    Differential.pair (List.map (toModelOpWith toModelTree) >> render) render

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 305: the normalize oracle — `Ops.normalize` beside the extracted peephole ----
          //
          // The model's theorems (`Normalize.fst`: `normalize_preserves`, `normalize_idempotent`,
          // `normalize_never_longer`) are about the extracted function; this is what holds that
          // function to the shipped one. Scripts come from `ContentDiffTests.drawScript`, the
          // generator with ADJACENT-COLLAPSE BIAS (an accepted op is often followed by the op that
          // collapses with it), so every row of the table is reached; a differential over scripts
          // that never collapse would measure nothing. Compared per script: the normalised script,
          // op for op, through the model's own rendering — the bridge is `toModelOpWith toModelTree`,
          // under which production's `ReplaceChildren n' (Children n)` and the model's
          // `TNode (tid n') (kind n') (kids n)` are the same value — and, on the model side, the
          // three theorems instantiated: the normalised script reaches the same tree, is a fixed
          // point, and is no longer. Agreement is over the pool drawn, never over all inputs.

          testCase "the normalize oracle agrees with Ops.normalize over scripts with adjacent-collapse bias"
          <| fun _ ->
              let step (i: int) (rng: System.Random) (t: Tally) =
                  let tree = ContentDiffTests.drawTree rng ContentDiffTests.pool
                  let script, reached = ContentDiffTests.drawScript rng tree 8
                  let mscript = script |> List.map (toModelOpWith toModelTree)
                  let model = Normalize.normalize mscript

                  let t =
                      t
                      |> Tally.bumpIf (List.length model < List.length mscript) "Collapsed"
                      |> Differential.compareWith
                          normalized
                          (fun p m -> sprintf "#%d\n  production: %s\n  oracle:     %s" i p m)
                          (Ops.normalize nodew idw script)
                          model

                  // the theorems, instantiated on the extracted model over this sample
                  let mtree = toModelTree tree

                  Expect.equal
                      (TreeOps.apply_all model mtree)
                      (TreeOps.apply_all mscript mtree)
                      (sprintf "#%d: the oracle's normalised script reaches the tree the script reached" i)

                  Expect.equal
                      (TreeOps.apply_all mscript mtree)
                      (DagFold.Ok(toModelTree reached))
                      (sprintf "#%d: the bridge agrees with production on the reached tree" i)

                  Expect.equal (Normalize.normalize model) model (sprintf "#%d: the oracle's normalize is idempotent" i)
                  Expect.isLessThanOrEqual (List.length model) (List.length mscript) (sprintf "#%d: never longer" i)

                  rng, t

              let t =
                  Differential.seeded 600 (System.Random 30305) step (Tally.empty [ "Collapsed" ])

              Differential.expectAgrees "Ops.normalize and the extracted model" t

              Expect.isGreaterThan
                  (Tally.count "Collapsed" t)
                  150
                  "the bias reached the collapses (a differential over scripts that never collapse measures nothing)"

          testCase
              "a bridge that renames the remove of a cancelling pair loses against Ops.normalize — the measurement can fail"
          <| fun _ ->
              // The teeth. A bridge that carries `RemoveNode t` as `RemoveNode (t + "?")` exactly
              // when the op before it inserted or rewrote `t` — and every other op faithfully —
              // hands the model a script in which the two cancelling rows never fire, so the model
              // keeps the pair production collapses. On every script carrying such a pair the
              // comparison must lose, and on every script without one it must still agree: the
              // bridge differs from the faithful one at the two rows under test and nowhere else.
              let blind (script: SkeletonOp<RNode, string> list) : TreeOps.op list =
                  let rec go (prev: SkeletonOp<RNode, string> option) (ops: SkeletonOp<RNode, string> list) =
                      match ops with
                      | [] -> []
                      | op :: rest ->
                          let op' =
                              match prev, op with
                              | Some(InsertChild(_, n)), RemoveNode t
                              | Some(UpdateNode n), RemoveNode t when n.Id = t -> TreeOps.RemoveNode(t + "?")
                              | _ -> toModelOpWith toModelTree op

                          op' :: go (Some op) rest

                  go None script

              let step _ (rng: System.Random) (t: Tally) =
                  let tree = ContentDiffTests.drawTree rng ContentDiffTests.pool
                  let script, _ = ContentDiffTests.drawScript rng tree 8

                  let hasPair =
                      script
                      |> List.pairwise
                      |> List.exists (fun (a, b) ->
                          match a, b with
                          | InsertChild(_, n), RemoveNode t
                          | UpdateNode n, RemoveNode t -> n.Id = t
                          | _ -> false)

                  let prod = Ops.normalize nodew idw script
                  let model = Normalize.normalize (blind script)

                  rng,
                  if hasPair then
                      t
                      |> Tally.bump "WithPair"
                      |> Differential.compare
                          normalized
                          (sprintf "a script with a cancelling pair, blind bridge")
                          prod
                          model
                  else
                      t
                      |> Tally.bump "WithoutPair"
                      |> Tally.bumpIf (normalized.Production prod = normalized.Model model) "Agreed"

              let t =
                  Differential.seeded
                      400
                      (System.Random 30306)
                      step
                      (Tally.empty [ "WithPair"; "WithoutPair"; "Agreed" ])

              Expect.isGreaterThan
                  (Tally.count "WithPair" t)
                  50
                  "the sample reached an insert/remove or rewrite/remove pair"

              Differential.expectLoses "a bridge that renames the remove of a cancelling pair" t

              Expect.equal
                  (List.length (Tally.disagreements t))
                  (Tally.count "WithPair" t)
                  "every script carrying a cancelling pair lost under the blind bridge"

              Expect.isGreaterThan (Tally.count "WithoutPair" t) 50 "the sample also drew scripts with no such pair"

              Expect.equal
                  (Tally.count "Agreed" t)
                  (Tally.count "WithoutPair" t)
                  "and on those the blind bridge is the faithful one" ]
