module Fuaran.Core.Tests.ProofOracle.ArbitrateTests

// The `Proofs.Oracle` differential for `proofs/Arbitrate.fst`: the extracted model
// (`proofs/oracle/Arbitrate.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.FoldFixtures
open Fuaran.Core.Tests.ProofOracle.Bridges

// ---------------------------------------------------------------------------
//  Phase 157 — PROPOSAL ARBITRATION as an oracle over the tree oracle.
//
//  `proofs/Arbitrate.fst` models `Arbitration.arbitrate` clause for clause over Phase 133's tree
//  model — the stable pinned sort, the `canApplyAll` dry run, the greedy independence pass and the
//  re-citation — and proves the three promises the function's doc comment makes: the accepted set
//  is pairwise independent, every rejection is justified (maximal, NOT maximum), and the whole
//  result is invariant under arrival order for id-distinct input. `proofs/oracle/Arbitrate.fs` is
//  that model extracted.
//
//  What runs here is the model BESIDE production over generated proposal sets against the base
//  tree: scripts from Phase 80's lane generator (each applies at the base on its own, and they
//  frequently share a parent, so conflicts arise without being arranged), about one in four
//  corrupted into a provably inapplicable script, under ids that are a SHUFFLE of 1..n — so the
//  pinned order is not the arrival order — and, in a second mode, under ids drawn from {1, 2}, so
//  that most sets carry a repeated id and the STABLE sort's tie-break is compared too. Compared
//  per set: the accepted proposals (id, holder, script) in order; the merged script; and every
//  rejection in order with its reason — an `Inapplicable`'s index exactly and its envelope by
//  CLASS (which is what the tree model claims; see Phase 133's section above), a `Conflicts`'
//  citation exactly.
// ---------------------------------------------------------------------------

type private ArbProposal = OpScriptProposal<RNode, string>

let private toModelProposal (p: ArbProposal) : Arbitrate.proposal =
    { Arbitrate.proposal.pid = bigint p.Id
      Arbitrate.proposal.holder = p.Holder
      Arbitrate.proposal.script = p.Ops |> List.map (toModelOpWith toModelTree) }

/// Production's result in the vocabulary the comparison is made in.
let private renderProdArbitration (a: Arbitration<RNode, string>) : string list =
    [ for p in a.Accepted do
          yield sprintf "accepted %d/%s %A" p.Id p.Holder (p.Ops |> List.map (toModelOpWith toModelTree))
      yield sprintf "merged %A" (a.MergedScript |> List.map (toModelOpWith toModelTree))
      for p, why in a.Rejected do
          match why with
          | Inapplicable(i, rej) ->
              yield sprintf "rejected %d/%s inapplicable at %d (%s)" p.Id p.Holder i (prodRejClass rej)
          | Conflicts(ids, _) -> yield sprintf "rejected %d/%s conflicts %A" p.Id p.Holder ids ]

/// The model's result in the same vocabulary.
let private renderModelArbitration (a: Arbitrate.arbitration) : string list =
    [ for p in a.accepted do
          yield sprintf "accepted %d/%s %A" (int p.pid) p.holder p.script
      yield sprintf "merged %A" a.merged
      for p, why in a.rejected do
          match why with
          | Arbitrate.Inapplicable(i, rej) ->
              yield sprintf "rejected %d/%s inapplicable at %d (%s)" (int p.pid) p.holder (int i) (modelRejClass rej)
          | Arbitrate.Conflicts ids ->
              yield sprintf "rejected %d/%s conflicts %A" (int p.pid) p.holder (ids |> List.map int) ]

/// THE GO-RED INSTRUMENT — a model that accepts a conflicting pair. It is the extracted model
/// with exactly one clause removed: the greedy pass's `all_independent` test. Everything else —
/// the pinned sort, the dry run, the re-citation, the merged script — is the oracle's own code,
/// so what the comparison loses on is the independence check and nothing else.
let private arbitrateAcceptingConflicts (baseTree: TreeOps.tree) (ps: Arbitrate.proposal list) : Arbitrate.arbitration =
    let step (acc, rej) (p: Arbitrate.proposal) =
        match Arbitrate.can_script System.Numerics.BigInteger.Zero p.script baseTree with
        | DagFold.Error(i, e) -> acc, (p, Arbitrate.Inapplicable(i, e)) :: rej
        | DagFold.Ok() -> p :: acc, rej

    let accRev, rejRev = Arbitrate.pin ps |> List.fold step ([], [])
    let accepted = DagFold.rev accRev

    { Arbitrate.arbitration.accepted = accepted
      Arbitrate.arbitration.merged = Arbitrate.collect_scripts accepted
      Arbitrate.arbitration.rejected = Arbitrate.recite_all accepted (DagFold.rev rejRev) }

type private ArbTally =
    {
        Diffs: string list
        Sets: int
        Accepted: int
        Inapplicable: int
        Conflicting: int
        /// Sets carrying a repeated id — the stable sort's tie-break was compared on these.
        Duplicated: int
        /// Sets where the shipped `duplicateIds` and the model's `distinct_ids` disagreed.
        HypothesisDiffs: string list
        /// Sets where an extracted theorem predicate was FALSE of production's own result.
        TheoremBreaks: string list
    }

let private emptyArbTally =
    { Diffs = []
      Sets = 0
      Accepted = 0
      Inapplicable = 0
      Conflicting = 0
      Duplicated = 0
      HypothesisDiffs = []
      TheoremBreaks = [] }

/// One generated proposal set: `count` scripts off the base, ~1 in 4 corrupted, under `ids`.
let private genProposalSet (uniqueIds: bool) (r0: ConfRng.T) : ArbProposal list * ConfRng.T =
    let extra, r1 = ConfRng.intBelow 4 r0
    let count = extra + 2
    let scripts, r2 = treeLaneGen.Lanes count r1
    let mutable r = r2

    let ids =
        if uniqueIds then
            let shuffled, r' = ConfRng.shuffle [ 1..count ] r
            r <- r'
            shuffled
        else
            [ for _ in 1..count do
                  let v, r' = ConfRng.intBelow 2 r
                  r <- r'
                  yield v + 1 ]

    let proposals =
        [ for k, (id, script) in List.indexed (List.zip ids scripts) do
              let corrupt, r' = ConfRng.intBelow 4 r
              r <- r'

              let ops =
                  if corrupt = 0 then
                      script @ [ RemoveNode(sprintf "ghost-157-%d" k) ]
                  else
                      script

              yield
                  { Id = id
                    Holder = sprintf "agent-%d" k
                    Ops = ops } ]

    proposals, r

/// Production beside `modelArbitrate` over `trials` generated sets in each id mode.
let private arbitrationDifferential
    (modelArbitrate: TreeOps.tree -> Arbitrate.proposal list -> Arbitrate.arbitration)
    (seed: int)
    (trials: int)
    : ArbTally =
    let mutable r = ConfRng.ofSeed seed
    let mutable tally = emptyArbTally
    let mbase = toModelTree treeBase

    for uniqueIds in [ true; false ] do
        for t in 1..trials do
            let proposals, r' = genProposalSet uniqueIds r
            r <- r'
            let prod = Arbitration.arbitrate nodew idw treeBase proposals
            let mps = proposals |> List.map toModelProposal
            let p = renderProdArbitration prod
            let m = renderModelArbitration (modelArbitrate mbase mps)

            let where =
                sprintf "seed=%d mode=%s trial=%d" seed (if uniqueIds then "unique" else "duplicated") t

            let diffs =
                if p <> m then
                    [ sprintf
                          "arbitration differs — %s\n  production:\n    %s\n  oracle:\n    %s"
                          where
                          (String.concat "\n    " p)
                          (String.concat "\n    " m) ]
                else
                    []

            // the shipped check IS the theorem's hypothesis, read off the extracted predicate
            let dups = Arbitration.duplicateIds proposals

            let hypothesisDiffs =
                if List.isEmpty dups <> Arbitrate.distinct_ids mps then
                    [ sprintf
                          "duplicateIds = %A but the model's distinct_ids = %b — %s"
                          dups
                          (Arbitrate.distinct_ids mps)
                          where ]
                else
                    []

            // the extracted theorem predicates, asked of PRODUCTION's result bridged across
            let prodAccepted = prod.Accepted |> List.map toModelProposal

            let theoremBreaks =
                [ if not (Arbitrate.pairwise_independent prodAccepted) then
                      yield sprintf "pairwise_independent is FALSE of production's accepted set — %s" where
                  if not (Arbitrate.all_applicable mbase prodAccepted) then
                      yield sprintf "all_applicable is FALSE of production's accepted set — %s" where
                  if List.length prod.Accepted + List.length prod.Rejected <> List.length proposals then
                      yield sprintf "the partition is not total — %s" where ]

            tally <-
                { Diffs = tally.Diffs @ diffs
                  Sets = tally.Sets + 1
                  Accepted = tally.Accepted + List.length prod.Accepted
                  Inapplicable =
                    tally.Inapplicable
                    + (prod.Rejected
                       |> List.filter (fun (_, w) ->
                           match w with
                           | Inapplicable _ -> true
                           | Conflicts _ -> false)
                       |> List.length)
                  Conflicting =
                    tally.Conflicting
                    + (prod.Rejected
                       |> List.filter (fun (_, w) ->
                           match w with
                           | Conflicts _ -> true
                           | Inapplicable _ -> false)
                       |> List.length)
                  Duplicated = tally.Duplicated + (if List.isEmpty dups then 0 else 1)
                  HypothesisDiffs = tally.HypothesisDiffs @ hypothesisDiffs
                  TheoremBreaks = tally.TheoremBreaks @ theoremBreaks }

    tally

/// The model's witness trees and proposals, as production values — so a finding proved about the
/// model is pinned on the shipped function over the SAME inputs.
let rec private ofModelTree (t: TreeOps.tree) : RNode =
    match t with
    | TreeOps.TNode(i, k, []) -> RNode.leaf i k "v"
    | TreeOps.TNode(i, k, cs) -> RNode.node i k (cs |> List.map ofModelTree)

let rec private ofModelOp (o: TreeOps.op) : SkeletonOp<RNode, string> =
    match o with
    | TreeOps.InsertChild(p, n) -> InsertChild(p, ofModelTree n)
    | TreeOps.RemoveNode x -> RemoveNode x
    | TreeOps.MoveNode(x, np) -> MoveNode(x, np)
    | TreeOps.ReorderChildren(p, order) -> ReorderChildren(p, order)
    | TreeOps.Batch inner -> Batch(inner |> List.map ofModelOp)
    | TreeOps.UpdateNode n -> UpdateNode(ofModelTree n)

let private ofModelProposal (p: Arbitrate.proposal) : ArbProposal =
    { Id = int p.pid
      Holder = p.holder
      Ops = p.script |> List.map ofModelOp }

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [ testCase "the arbitration oracle agrees with Arbitration.arbitrate over generated proposal sets"
          <| fun _ ->
              // Phase 157. Accepted set, merged script and every rejection with its reason, over
              // sets whose pinned order is NOT their arrival order and over sets carrying a
              // repeated id. Measured at 150 trials per mode, seed 1570: 300 sets, every bucket
              // reached and a repeated id in most of the second mode's — asserted below, because
              // a differential that met no conflict would agree about nothing worth agreeing on.
              let t = arbitrationDifferential Arbitrate.arbitrate 1570 150

              if not (List.isEmpty t.Diffs) then
                  failtestf
                      "the arbitration oracle DISAGREES with production on %d of %d sets:\n%s"
                      (List.length t.Diffs)
                      t.Sets
                      (t.Diffs |> List.truncate 3 |> String.concat "\n")

              Expect.equal t.Sets 300 "both id modes ran"
              Expect.isGreaterThan t.Accepted 0 "the sample accepted something"
              Expect.isGreaterThan t.Inapplicable 0 "the sample reached an Inapplicable rejection"
              Expect.isGreaterThan t.Conflicting 0 "the sample reached a Conflicts rejection"

              Expect.isGreaterThan
                  t.Duplicated
                  0
                  "the sample reached a repeated id — the stable sort's tie-break was compared"

              Expect.isEmpty
                  t.HypothesisDiffs
                  "Arbitration.duplicateIds is empty EXACTLY when the model's distinct_ids holds — the shipped check is the theorem's hypothesis"

              Expect.isEmpty
                  t.TheoremBreaks
                  "the extracted theorem predicates hold of production's own result, on every set, repeated ids included"

          testCase "a model that ACCEPTS A CONFLICTING PAIR loses — the measurement can fail"
          <| fun _ ->
              // The go-red for `accepted_pairwise_independent`. The instrument is the extracted
              // model with the greedy pass's independence test removed and nothing else touched,
              // so every conflict production refuses is a set the two sides must disagree on.
              let t = arbitrationDifferential arbitrateAcceptingConflicts 1570 150

              Expect.isGreaterThan
                  t.Conflicting
                  0
                  "the go-red run reached a conflicting pair at all — otherwise it proves nothing"

              Expect.isNonEmpty t.Diffs "a model that accepts a conflicting pair DISAGREES with production"

              // and it disagrees on exactly the sets that held a conflict — never on one that did not
              let clean = arbitrationDifferential Arbitrate.arbitrate 1570 150
              Expect.isEmpty clean.Diffs "the same sample under the real model agrees, so the loss is the instrument's"

          testCase "the id-uniqueness hypothesis is NEEDED, on the shipped function — `duplicate_ids_break_invariance`"
          <| fun _ ->
              // THE FINDING, pinned on production over the model's own witness. Two proposals
              // sharing an id and interfering with each other: the stable sort leaves them in
              // arrival order, so WHICH is accepted is the arrival order. If `arbitrate` ever
              // breaks the tie some other way this case goes red and sends its reader to
              // `proofs/Arbitrate.fst` section 8 and to `Arbitration.duplicateIds`' doc comment.
              let baseTree = ofModelTree Arbitrate.dup_base
              let a = ofModelProposal Arbitrate.dup_a
              let b = ofModelProposal Arbitrate.dup_b

              let holders (r: Arbitration<RNode, string>) = r.Accepted |> List.map _.Holder

              Expect.equal (Arbitration.duplicateIds [ a; b ]) [ 1 ] "the shipped check names the repeated id"

              Expect.isFalse
                  (Arbitrate.distinct_ids [ Arbitrate.dup_a; Arbitrate.dup_b ])
                  "and the model's hypothesis is false of it"

              Expect.equal
                  (holders (Arbitration.arbitrate nodew idw baseTree [ a; b ]))
                  [ "a" ]
                  "a arrives first, a is accepted"

              Expect.equal
                  (holders (Arbitration.arbitrate nodew idw baseTree [ b; a ]))
                  [ "b" ]
                  "b arrives first, b is accepted"

              Expect.equal
                  (renderProdArbitration (Arbitration.arbitrate nodew idw baseTree [ a; b ]))
                  (renderModelArbitration (Arbitrate.arbitrate Arbitrate.dup_base [ Arbitrate.dup_a; Arbitrate.dup_b ]))
                  "and the model agrees with production on the witness, in this order"

              Expect.equal
                  (renderProdArbitration (Arbitration.arbitrate nodew idw baseTree [ b; a ]))
                  (renderModelArbitration (Arbitrate.arbitrate Arbitrate.dup_base [ Arbitrate.dup_b; Arbitrate.dup_a ]))
                  "and in the other"

              // what a repeated id does NOT cost: the partition is still total and still justified
              let r = Arbitration.arbitrate nodew idw baseTree [ a; b ]
              Expect.equal (List.length r.Accepted + List.length r.Rejected) 2 "nothing dropped"

              match r.Rejected with
              | [ (p, Conflicts([ 1 ], _)) ] -> Expect.equal p.Holder "b" "the loser cites the winner's id"
              | other -> failtestf "expected one Conflicts [1] rejection, got %A" other

              // the check itself: total, ascending, each repeated id once, empty on unique input
              let prop id : ArbProposal = { Id = id; Holder = "h"; Ops = [] }
              Expect.equal (Arbitration.duplicateIds ([]: ArbProposal list)) [] "empty input"
              Expect.equal (Arbitration.duplicateIds [ prop 3; prop 1; prop 2 ]) [] "unique ids"

              Expect.equal
                  (Arbitration.duplicateIds [ prop 5; prop 2; prop 5; prop 2; prop 5; prop 9 ])
                  [ 2; 5 ]
                  "ascending, each repeated id once however often it repeats"

          testCase "maximal is NOT maximum, on the shipped function — `maximal_is_not_maximum`"
          <| fun _ ->
              // NOT CLAIMED, and the witness that it is not. Proposal 1 writes under both `a` and
              // `b`; 2 and 3 write under one each. The pinned order accepts 1 alone; renumbered to
              // come last, the same three proposals accept 2 and 3. Both results are maximal. The
              // pinned order is a policy choice, and this is what it decides.
              let baseTree = ofModelTree Arbitrate.mx_base
              let ids (r: Arbitration<RNode, string>) = r.Accepted |> List.map _.Id

              let first =
                  Arbitration.arbitrate
                      nodew
                      idw
                      baseTree
                      ([ Arbitrate.mx_1; Arbitrate.mx_2; Arbitrate.mx_3 ] |> List.map ofModelProposal)

              let last =
                  Arbitration.arbitrate
                      nodew
                      idw
                      baseTree
                      ([ Arbitrate.mx_1_last; Arbitrate.mx_2; Arbitrate.mx_3 ]
                       |> List.map ofModelProposal)

              Expect.equal
                  (ids first)
                  [ 1 ]
                  "in the pinned order the two-parent proposal wins alone — an accepted set of ONE"

              Expect.equal (ids last) [ 2; 3 ] "numbered last, the same proposal loses to an accepted set of TWO"

              Expect.equal
                  (first.Rejected
                   |> List.map (fun (_, r) ->
                       match r with
                       | Conflicts(ids, _) -> Some ids
                       | _ -> None))
                  [ Some [ 1 ]; Some [ 1 ] ]
                  "and both rejections are justified — each cites the proposal standing in its way"

              Expect.equal
                  (renderProdArbitration first)
                  (renderModelArbitration (
                      Arbitrate.arbitrate Arbitrate.mx_base [ Arbitrate.mx_1; Arbitrate.mx_2; Arbitrate.mx_3 ]
                  ))
                  "the model agrees with production on the witness" ]
