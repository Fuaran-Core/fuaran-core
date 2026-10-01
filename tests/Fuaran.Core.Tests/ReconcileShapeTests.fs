module Fuaran.Core.Tests.ReconcileShapeTests

// Phase 300 — the lane DAG's reconcile over the shapes where lanes SHARE history, and the lane set
// that does not apply. Each probe below failed before 0.33.0: a fast-forward, a duplicate head and a
// criss-cross replayed their shared history twice; a lane set with a rejecting lane folded under one
// arrival order and rejected under the other; `merge("", x)` was accepted and a comma-bearing parent
// id minted a merge's content id through `append`.

open Expecto
open Fuaran.Core

/// A counter whose `Dec` refuses to go below zero, and a `Set` that does not commute with anything.
type private COp =
    | Inc of int
    | Dec of int
    | SetTo of int
    | Nop

let private apply (op: COp) (s: int) : Result<int, string> =
    match op with
    | Inc n -> Ok(s + n)
    | Dec n ->
        if s >= n then
            Ok(s - n)
        else
            Error(sprintf "underflow: %d - %d" s n)
    | SetTo v -> Ok v
    | Nop -> Ok s

let private encode (op: COp) : string =
    match op with
    | Inc n -> "I|" + string n
    | Dec n -> "D|" + string n
    | SetTo v -> "S|" + string v
    | Nop -> "N"

let private w: StreamWitness<COp, int, string> =
    { Apply = apply
      Encode = encode
      Decode = fun _ -> Error "unused" }

let private none: Set<string> = Set.empty

/// One cell every counting op reads and writes — the footprint under which shared history used to
/// "conflict" with itself.
let private cell (op: COp) : Footprint =
    match op with
    | Nop ->
        { Reads = none
          StructureWrites = none
          ContentWrites = none
          UnknownParentWrites = none }
    | _ ->
        { Reads = Set.singleton "c"
          StructureWrites = none
          ContentWrites = Set.singleton "c"
          UnknownParentWrites = none }

/// No addresses at all: every pair is independent (true of `Inc`/`Dec`, which commute where both apply).
let private blind (_: COp) : Footprint =
    { Reads = none
      StructureWrites = none
      ContentWrites = none
      UnknownParentWrites = none }

let private h = OpStream.defaultHash

let private chain (actor: string) (ops: COp list) (parent: string) (d: Dag.T<COp>) =
    ops
    |> List.fold (fun (p, dd) op -> Dag.append h w (Human actor) op p dd |> Reference.built) (parent, d)

let private replayFrom (s: int) (script: COp list) =
    script |> List.fold (fun acc op -> acc |> Result.bind (apply op)) (Ok s)

let private stateAt (dag: Dag.T<COp>) (id: string) =
    Dag.replayTo w 0 dag id |> Result.mapError snd

/// `replayTo base` then the script, beside `replayTo` of a merge node over the two heads.
let private againstMerge (dag: Dag.T<COp>) (baseId: string) (a: string) (b: string) (script: COp list) =
    let m, dm = Dag.merge h w (Human "merge") Nop a b dag |> Reference.built
    stateAt dm baseId |> Result.bind (fun s -> replayFrom s script), stateAt dm m

[<Tests>]
let reconcileShapeTests =
    testList
        "Dag.reconcile shapes (Phase 300)"
        [ testCase "a fast-forward applies the shared history once, even under a single-cell footprint"
          <| fun _ ->
              let g, d0 = Dag.append h w (Human "base") Nop "" Dag.empty |> Reference.built
              let a, d1 = chain "lane-a" [ Inc 7 ] g d0
              let b, dag = chain "lane-b" [ Inc 1 ] a d1

              for fp in [ blind; cell ] do
                  match Dag.reconcile fp dag g a b with
                  | Ok script ->
                      Expect.equal script [ Inc 7; Inc 1 ] "the shared Inc 7 once (it was [Inc 7; Inc 7; Inc 1])"
                      let viaScript, viaMerge = againstMerge dag g a b script
                      Expect.equal viaScript viaMerge "the script replays to the merge node"
                      Expect.equal viaScript (Ok 8) "and that state is 8"
                  | Error cs -> failtestf "a fast-forward cannot conflict with itself: %A" cs

              // and named the other way round: the descendant first
              Expect.equal (Dag.reconcile cell dag g b a) (Ok [ Inc 7; Inc 1 ]) "a head order is canonical form"

          testCase "a duplicate head is deduplicated"
          <| fun _ ->
              let g, d0 = Dag.append h w (Human "base") Nop "" Dag.empty |> Reference.built
              let a, dag = chain "lane-a" [ Inc 7; Inc 1 ] g d0

              Expect.equal
                  (Dag.reconcile cell dag g a a)
                  (Ok [ Inc 7; Inc 1 ])
                  "one head named twice is one lane (it was [Inc 7; Inc 1; Inc 7; Inc 1])"

              Expect.equal (Dag.reconcileMany w cell dag g 0 [ a; a; a ]) (Ok [ Inc 7; Inc 1 ]) "at any multiplicity"

          testCase "a criss-cross over mergeBase's tie-break applies the other branch once"
          <| fun _ ->
              // two lanes off one base, two merges of them under two actors, then a lane off each
              let g, d0 = Dag.append h w (Human "base") Nop "" Dag.empty |> Reference.built
              let x, d1 = chain "lane-x" [ Inc 1000 ] g d0
              let y, d2 = chain "lane-y" [ Inc 100 ] g d1
              let m1, d3 = Dag.merge h w (Human "merge-1") Nop x y d2 |> Reference.built
              let m2, d4 = Dag.merge h w (Human "merge-2") Nop x y d3 |> Reference.built
              let h1, d5 = chain "lane-x" [ Inc 10 ] m1 d4
              let h2, dag = chain "lane-y" [ Inc 1 ] m2 d5

              let mb =
                  Dag.mergeBase dag h1 h2
                  |> Option.defaultWith (fun () -> failtest "the heads share history")

              Expect.contains [ x; y ] mb "mergeBase is ONE of the two maximal common ancestors, on its tie-break"

              match Dag.reconcile blind dag mb h1 h2 with
              | Ok script ->
                  let viaScript, viaMerge = againstMerge dag mb h1 h2 script
                  Expect.equal viaMerge (Ok 1111) "the merge node replays to 1111"
                  Expect.equal viaScript viaMerge "and so does the script (it replayed the other branch twice)"
                  Expect.equal (List.length script) 5 "the other branch, two merge ops and two lane ops — once each"
              | Error cs -> failtestf "commuting increments cannot conflict: %A" cs

          testCase "a lane set with a rejecting lane refuses identically under every arrival order"
          <| fun _ ->
              let lanes = [ [ Dec 5 ]; [ Inc 10 ] ]

              let outcomes =
                  FoldConfluence.arrivalOrders 2
                  |> List.map (fun p ->
                      FoldConfluence.foldOnce w blind h string 0 Nop (p |> List.map (fun i -> List.item i lanes)))

              Expect.equal (List.length (List.distinct outcomes)) 1 "one outcome under both orders"

              match List.head outcomes with
              | LaneRejected r -> Expect.stringContains r "[D|5] rejected" "naming the lane that does not apply"
              | other -> failtestf "the rejecting lane is refused, not folded: %A" other

              // and the laws over it: the invariance law is green at the reference witness
              let gen: LaneGen<COp, int> =
                  { State0 = 0
                    BaseOp = Nop
                    Lanes = fun _ r -> lanes, r }

              let results = FoldConfluence.laneFoldLaws w blind string gen 2 11 5

              for r in
                  results
                  |> List.filter (fun r -> not (r.Law.StartsWith SampleAdequacy.guardOpening)) do
                  Expect.isTrue r.Passed (sprintf "%s — %A" r.Law r.Counterexample)

          testCase "reconcileMany names the rejecting lane set, sorted by head, whatever the head order"
          <| fun _ ->
              let g, d0 = Dag.append h w (Human "base") Nop "" Dag.empty |> Reference.built
              let a, d1 = chain "lane-a" [ Dec 5 ] g d0
              let b, d2 = chain "lane-b" [ Inc 10 ] g d1
              let c, dag = chain "lane-c" [ Dec 7 ] g d2
              let heads = [ a; b; c ]

              let refusals =
                  FoldConfluence.arrivalOrders 3
                  |> List.map (fun p -> Dag.reconcileMany w blind dag g 0 (p |> List.map (fun i -> List.item i heads)))
                  |> List.distinct

              match refusals with
              | [ Error(ReconcileFault.LanesRejected rs) ] ->
                  Expect.equal
                      (rs |> List.map (fun r -> r.Head))
                      (List.sort [ a; c ])
                      "both rejecting lanes, by head id"

                  Expect.isTrue (rs |> List.forall (fun r -> r.NodeId = r.Head)) "each at its own node"
              | other -> failtestf "one order-free refusal expected, got %A" other

          testCase "shared history that does not replay is refused without blaming a lane"
          <| fun _ ->
              let g, d0 = Dag.append h w (Human "base") Nop "" Dag.empty |> Reference.built
              let a, d1 = chain "lane-a" [ Dec 3 ] g d0
              let b, dag = chain "lane-b" [ Inc 1 ] a d1

              match Dag.reconcileMany w blind dag g 0 [ b; a ] with
              | Error(ReconcileFault.SharedHistoryRejected(nodeId, _)) -> Expect.equal nodeId a "at the shared node"
              | other -> failtestf "expected a shared-history refusal, got %A" other

          testCase "merge refuses an empty parent and a comma-bearing one; append refuses a comma-bearing one"
          <| fun _ ->
              let g, dag = Dag.append h w (Human "base") Nop "" Dag.empty |> Reference.built

              Expect.equal
                  (Dag.merge h w (Human "m") Nop "" g dag |> Result.map fst)
                  (Error DagAppendFault.EmptyParentId)
                  "merge(\"\", x) is refused"

              Expect.equal
                  (Dag.merge h w (Human "m") Nop g "" dag |> Result.map fst)
                  (Error DagAppendFault.EmptyParentId)
                  "merge(x, \"\") is refused"

              Expect.equal
                  (Dag.merge h w (Human "m") Nop g "p,q" dag |> Result.map fst)
                  (Error(DagAppendFault.CommaInParentId "p,q"))
                  "a comma-bearing merge parent is refused"

              Expect.isOk (Dag.append h w (Human "a") (Inc 1) "" dag) "\"\" is still append's genesis"

          testCase "a merge id can no longer be minted through append"
          <| fun _ ->
              let g, d0 = Dag.append h w (Human "base") Nop "" Dag.empty |> Reference.built
              let x, d1 = chain "lane-x" [ Inc 1 ] g d0
              let y, d2 = chain "lane-y" [ Inc 2 ] g d1

              let lo, hi =
                  if System.String.CompareOrdinal(x, y) <= 0 then
                      x, y
                  else
                      y, x

              let mergeId, _ = Dag.merge h w (Human "m") Nop x y d2 |> Reference.built

              // the splice: an append naming the one parent "lo,hi" hashes the merge's pre-image
              match Dag.append h w (Human "m") Nop (lo + "," + hi) d2 with
              | Error(DagAppendFault.CommaInParentId _) -> ()
              | Ok(id, _) -> failtestf "the splice is buildable again: %s (merge %s)" id mergeId
              | Error f -> failtestf "refused for the wrong reason: %A" f

          // The shard's replay premise, REFUTED: "replaying the script from replayTo base equals
          // replayTo (merge heads), under the diamond" is false of a history whose merged branches do
          // not commute, because that history has no order-free replay of its own — `replayTo` drains
          // it by id, a tie-break. Reconcile applies each node once and after its parents, and that is
          // all it can promise there. The proof states the replay half under the premise that makes
          // it true; this case pins why the premise is needed.
          testCase "a merge of two NON-commuting branches has no order-free replay, and reconcile cannot supply one"
          <| fun _ ->
              let found =
                  [ 0..63 ]
                  |> List.tryPick (fun k ->
                      let g, d0 = Dag.append h w (Human "base") Nop "" Dag.empty |> Reference.built
                      let a, d1 = chain ("lane-a-" + string k) [ SetTo 1 ] g d0
                      let z, d2 = chain ("lane-z-" + string k) [ SetTo 2 ] g d1
                      let m, dag = Dag.merge h w (Human "merge") Nop a z d2 |> Reference.built

                      match Dag.reconcile blind dag g a m with
                      | Ok script ->
                          let viaScript, viaMerge = againstMerge dag g a m script

                          if viaScript <> viaMerge then
                              Some(script, viaScript, viaMerge)
                          else
                              None
                      | Error _ -> None)

              match found with
              | Some(script, viaScript, viaMerge) ->
                  Expect.equal script [ SetTo 1; SetTo 2; Nop ] "shared history first, then the exclusive delta"
                  Expect.notEqual viaScript viaMerge "and the id-ordered drain of that history can disagree"
              | None -> failtest "no id order put the side branch first — the pin measures nothing" ]
