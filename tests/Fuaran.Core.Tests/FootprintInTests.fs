module Fuaran.Core.Tests.FootprintInTests

// Phase 500 — the base-aware footprint: `Ops.footprintIn` resolves against the tree the script
// applies to what the pure `Ops.footprint` records as an unknown-parent write, and the three
// entries that hold a base compose it — `Arbitration.arbitrateIn`, `Dag.conflictsIn` and
// `Dag.reconcileIn` / `reconcileManyIn`. The two acceptance cases of the phase are the first two
// tests; the theorems behind them are `FootprintIn.footprint_in_sound` and `footprint_in_refines`,
// and the oracle differential in `ProofOracle.TreeOpsTests` holds production to the model.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

type private Op = SkeletonOp<RNode, string>

let private fpIn (ops: Op list) =
    Ops.footprintIn Policy.none (OpScope.plain nodew idw) (sample ()) ops

let private fpPure (ops: Op list) =
    Ops.footprint Policy.none (OpScope.plain nodew idw) ops

let private upd (id: string) (value: string) : Op = UpdateNode(RNode.leaf id "para" value)

let private independentIn (a: Op list) (b: Op list) = Ops.independent (fpIn a) (fpIn b)

let private independentPure (a: Op list) (b: Op list) = Ops.independent (fpPure a) (fpPure b)

/// Both orders apply from the sample and reach the same content hash.
let private commute (a: Op list) (b: Op list) =
    let hash (t: RNode) = Tree.encodeHash nodew encNode t

    let run (ops: Op list) (t: RNode) =
        Ops.applyAll Policy.none (OpScope.plain nodew idw) ops t

    match run a (sample ()) |> Result.bind (run b), run b (sample ()) |> Result.bind (run a) with
    | Ok x, Ok y -> hash x = hash y
    | _ -> false

/// A state-blind draw over the sample's ids: in-place edits, removals, moves and inserts, small
/// enough that two scripts routinely touch unrelated nodes (the pair the base-aware footprint frees)
/// and routinely collide.
let private genTreeOp (rng: ConfRng.T) : Op * ConfRng.T =
    let ids = [ "root"; "a"; "a1"; "a2"; "b"; "b1"; "n0"; "n1" ]
    let kind, r1 = ConfRng.intBelow 4 rng

    match kind with
    | 0 ->
        let t, r2 = ConfRng.choose ids r1
        let v, r3 = ConfRng.intBelow 3 r2
        upd t ("v" + string v), r3
    | 1 ->
        let t, r2 = ConfRng.choose ids r1
        RemoveNode t, r2
    | 2 ->
        let t, r2 = ConfRng.choose ids r1
        let np, r3 = ConfRng.choose ids r2
        MoveNode(t, np), r3
    | _ ->
        let p, r2 = ConfRng.choose ids r1
        let n, r3 = ConfRng.intBelow 2 r2
        InsertChild(p, RNode.leaf ("n" + string n) "para" ""), r3

[<Tests>]
let footprintInTests =
    testList
        "Ops.footprintIn (Phase 500)"
        [ testCase
              "two in-place edits of different nodes under different parents are independent (the pure form refuses them)"
          <| fun () ->
              let a = [ upd "a1" "x'" ]
              let b = [ upd "b1" "z'" ]
              Expect.isFalse (independentPure a b) "the pure footprint carries an unknown-parent write on each"
              Expect.isTrue (independentIn a b) "the base resolves both targets, so neither records an unknown parent"
              Expect.isTrue (commute a b) "and the two orders reach one tree"
              Expect.isEmpty (fpIn a).UnknownParentWrites "an in-place edit of a present node names no unknown parent"

          testCase
              "an in-place edit beside the removal of its ancestor is NOT independent — the target is in the removed set"
          <| fun () ->
              let edit = [ upd "a1" "x'" ]
              let remove = [ RemoveNode "a" ]
              Expect.isFalse (independentIn edit remove) "a1 is content-written by the removal of a"

              Expect.equal
                  (fpIn remove).ContentWrites
                  (Set.ofList [ "a"; "a1"; "a2" ])
                  "a removal content-writes the whole subtree it destroys"

              Expect.equal (fpIn remove).StructureWrites (Set.singleton "root") "and names its source parent"
              Expect.isEmpty (fpIn remove).UnknownParentWrites "with no unknown-parent write left"

          testCase
              "a removal and an insert under an unrelated parent are independent; under a node inside the removed subtree they are not"
          <| fun () ->
              let remove = [ RemoveNode "a" ]
              let outside = [ InsertChild("b", RNode.leaf "n" "para" "") ]
              let inside = [ InsertChild("a2", RNode.leaf "n" "para" "") ]

              Expect.isFalse
                  (independentPure remove outside)
                  "the pure form refuses every structural write beside a removal"

              Expect.isTrue (independentIn remove outside) "the base-aware form frees the unrelated parent"
              Expect.isTrue (commute remove outside) "and they commute"
              Expect.isFalse (independentIn remove inside) "an insert under a destroyed node is refused"

          testCase "a move names its source parent, its destination, and reads the destination's ancestors"
          <| fun () ->
              let f = fpIn [ MoveNode("a1", "b1") ]
              Expect.equal f.StructureWrites (Set.ofList [ "a"; "b1" ]) "source and destination parents"
              Expect.equal f.ContentWrites (Set.singleton "a1") "the moved node"

              Expect.equal
                  f.Reads
                  (Set.ofList [ "a1"; "b1"; "a"; "root"; "b" ])
                  "target, destination, source parent, and the destination's ancestors"

              Expect.isEmpty f.UnknownParentWrites "no unknown parent"

          testCase
              "two moves nesting into each other's subtrees are still refused — through the ancestor read, not the unknown-parent clause"
          <| fun () ->
              // a under b1 and b under a1: each applies alone, the pair rejects WouldNestUnderSelf.
              let m1 = [ MoveNode("a", "b1") ]
              let m2 = [ MoveNode("b", "a1") ]
              Expect.isFalse (independentIn m1 m2) "the pair is refused"
              Expect.isFalse (commute m1 m2) "and it does not commute"

              let clauses = Ops.interference (fpIn m1) (fpIn m2)

              Expect.isTrue
                  (clauses
                   |> List.exists (function
                       | Interference.LeftWritesRightReads _
                       | Interference.RightWritesLeftReads _ -> true
                       | _ -> false))
                  "the clause is a read of the moved node on the other side's destination path"

          testCase "the script is threaded: a removal after a move destroys what the move brought under it"
          <| fun () ->
              // move b1 under a2, then remove a: b1 is destroyed, so an edit of b1 must be refused.
              let lane = [ MoveNode("b1", "a2"); RemoveNode "a" ]
              let edit = [ upd "b1" "z'" ]

              Expect.isTrue
                  (Set.contains "b1" (fpIn lane).ContentWrites)
                  "b1 is in the removed set at the state the removal applies to"

              Expect.isFalse (independentIn lane edit) "so the pair is refused"
              Expect.isFalse (commute lane edit) "and indeed it does not commute"

          testCase
              "where the base is silent the pure arm stands: an absent target, and every op after one that does not apply"
          <| fun () ->
              let absent = [ upd "ghost" "v" ]
              Expect.equal (fpIn absent) (fpPure absent) "an absent target takes the pure arm"
              let stuck = [ RemoveNode "ghost"; RemoveNode "a" ]
              Expect.equal (fpIn stuck) (fpPure stuck) "after an op that does not apply, every op is footprinted purely"
              let later = [ InsertChild("b", RNode.leaf "n" "para" ""); upd "n" "v" ]

              Expect.isEmpty
                  (fpIn later).UnknownParentWrites
                  "a node inserted earlier in the script is present when its edit is footprinted"

          testCase
              "insert and reorder are the pure arms verbatim, and the keyed form agrees with the unkeyed one on a domain with no keyed position"
          <| fun () ->
              let ops =
                  [ InsertChild("b", RNode.leaf "n" "para" "")
                    ReorderChildren("a", [ "a2"; "a1" ]) ]

              Expect.equal (fpIn ops) (fpPure ops) "nothing to resolve"

              let keyw: KeyedWitness<RNode, string> =
                  { Surface = "no keyed position"
                    KeyedChildren = fun _ -> []
                    ReplaceKeyedChildren = fun n _ -> n
                    PlaceKeyedChild = fun _ _ -> None
                    IdsUnique = Tree.isWellFormed nodew idw }

              let all = [ upd "a1" "x'"; RemoveNode "b"; MoveNode("a2", "root") ]

              Expect.equal
                  (Ops.footprintIn Policy.none (OpScope.keyed keyw (fun _ -> true) nodew idw) (sample ()) all)
                  (fpIn all)
                  "the keyed form is the unkeyed one"

          testCase
              "refinement, sampled: every pair the pure footprint frees, the base-aware one frees (and it frees more)"
          <| fun () ->
              let mutable pureFree = 0
              let mutable freed = 0
              let mutable rng = ConfRng.ofSeed 500

              for _ in 1..400 do
                  let t = sample ()
                  let a, r2 = genTreeOp rng
                  let b, r3 = genTreeOp r2
                  rng <- r3

                  match
                      Ops.apply Policy.none (OpScope.plain nodew idw) a t,
                      Ops.apply Policy.none (OpScope.plain nodew idw) b t
                  with
                  | Ok _, Ok _ ->
                      let pureInd = Ops.independent (fpPure [ a ]) (fpPure [ b ])

                      let inInd =
                          Ops.independent
                              (Ops.footprintIn Policy.none (OpScope.plain nodew idw) t [ a ])
                              (Ops.footprintIn Policy.none (OpScope.plain nodew idw) t [ b ])

                      if pureInd then
                          pureFree <- pureFree + 1

                          Expect.isTrue
                              inInd
                              (sprintf "a pair the pure footprint frees is refused by the base-aware one: %A / %A" a b)

                      if inInd && not pureInd then
                          freed <- freed + 1
                  | _ -> ()

              Expect.isGreaterThan pureFree 0 "the sample drew a pure-independent pair"
              Expect.isGreaterThan freed 0 "the sample drew a pair only the base-aware footprint frees" ]

// ---------------------------------------------------------------------------
//  The entries that hold the base
// ---------------------------------------------------------------------------

let private prop id ops : OpScriptProposal<RNode, string> =
    { Id = id
      Holder = sprintf "agent-%d" id
      Ops = ops }

[<Tests>]
let arbitrateInTests =
    testList
        "Arbitration.arbitrateIn (Phase 500)"
        [ testCase "two in-place edits of different nodes coexist under arbitrateIn where arbitrate refuses the second"
          <| fun () ->
              let proposals = [ prop 1 [ upd "a1" "x'" ]; prop 2 [ upd "b1" "z'" ] ]

              let plain =
                  Arbitration.arbitrate Policy.none (OpScope.plain nodew idw) (sample ()) proposals

              let inBase =
                  Arbitration.arbitrateIn Policy.none (OpScope.plain nodew idw) (sample ()) proposals

              Expect.equal (plain.Accepted |> List.map _.Id) [ 1 ] "the pure partition accepts one"
              Expect.equal (inBase.Accepted |> List.map _.Id) [ 1; 2 ] "the base-aware partition accepts both"

          testCase "an edit under a removed ancestor is still refused, citing the removal"
          <| fun () ->
              let proposals = [ prop 1 [ RemoveNode "a" ]; prop 2 [ upd "a1" "x'" ] ]

              let r =
                  Arbitration.arbitrateIn Policy.none (OpScope.plain nodew idw) (sample ()) proposals

              Expect.equal (r.Accepted |> List.map _.Id) [ 1 ] "the removal wins the pinned order"

              match r.Rejected with
              | [ (p, Conflicts(cited, _)) ] ->
                  Expect.equal p.Id 2 "the edit is the rejected one"
                  Expect.equal cited [ 1 ] "and it cites the removal"
              | other -> failtestf "expected one Conflicts rejection, got %A" other

          testCase "a malformed base is refused as arbitrate refuses it"
          <| fun () ->
              let dup =
                  RNode.node "root" "doc" [ RNode.leaf "a" "para" ""; RNode.leaf "a" "para" "" ]

              let r =
                  Arbitration.arbitrateIn Policy.none (OpScope.plain nodew idw) dup [ prop 1 [ upd "a" "v" ] ]

              Expect.isEmpty r.Accepted "nothing is accepted"

              match r.Rejected with
              | [ (_, Inapplicable(0, DuplicateId "a")) ] -> ()
              | other -> failtestf "expected the base's refusal, got %A" other ]

// A lane DAG off one base: `(baseId, heads, dag)`, every lane a chain of appends.
let private lanes (lanes: Op list list) =
    let hashFn = OpStream.defaultHash
    let w = FoldFixtures.treeW

    let baseId, d0 = Dag.append hashFn w (Human "base") (Batch []) "" Dag.empty |> built

    let heads, dag =
        lanes
        |> List.indexed
        |> List.fold
            (fun (hs, d) (i, ops) ->
                let actor = Human("lane-" + string i)

                let head, d' =
                    ops
                    |> List.fold (fun (h, dd) op -> Dag.append hashFn w actor op h dd |> built) (baseId, d)

                hs @ [ head ], d')
            ([], d0)

    baseId, heads, dag

let private fpAt (t: RNode) (op: Op) =
    Ops.footprintIn Policy.none (OpScope.plain nodew idw) t [ op ]

let private fpOf (op: Op) =
    Ops.footprint Policy.none (OpScope.plain nodew idw) [ op ]

[<Tests>]
let dagInTests =
    testList
        "Dag.conflictsIn / reconcileIn / reconcileManyIn (Phase 500)"
        [ testCase "conflictsIn reports clean for two in-place edits the pure conflicts reports as MoveVsRemove"
          <| fun () ->
              let a = [ upd "a1" "x'" ]
              let b = [ upd "b1" "z'" ]
              Expect.isNonEmpty (Dag.conflicts fpOf a b) "the pure report fires on the unknown-parent clause"

              Expect.isEmpty
                  (Dag.conflictsIn fpAt FoldFixtures.treeW.Apply (sample ()) a b)
                  "the base-aware report is clean"

          testCase "conflictsIn is conflicts over the threaded footprints, address for address"
          <| fun () ->
              let a = [ MoveNode("b1", "a2"); RemoveNode "a" ]
              let b = [ upd "b1" "z'" ]
              let report = Dag.conflictsIn fpAt FoldFixtures.treeW.Apply (sample ()) a b
              Expect.isNonEmpty report "the removal destroys the edited node"

              Expect.isTrue
                  (report |> List.forall (fun c -> c.Right = upd "b1" "z'" && c.Address = "b1"))
                  "every conflict is at b1 against the edit"

              Expect.isTrue
                  (report |> List.exists (fun c -> c.Left = RemoveNode "a"))
                  "and names the removal on the left"

          testCase
              "reconcileManyIn folds two lanes of in-place edits that reconcileMany refuses, and the script applies"
          <| fun () ->
              let baseId, heads, dag = lanes [ [ upd "a1" "x'" ]; [ upd "b1" "z'" ] ]
              let w = FoldFixtures.treeW

              match Dag.reconcileMany w fpOf dag baseId (sample ()) heads with
              | Error(ReconcileFault.LanesInterfere _) -> ()
              | other -> failtestf "the pure fold should refuse, got %A" other

              match Dag.reconcileManyIn w fpAt dag baseId (sample ()) heads with
              | Ok script ->
                  Expect.equal script [ upd "a1" "x'"; upd "b1" "z'" ] "the merge script in head order"
                  Expect.isOk (Ops.applyAll Policy.none (OpScope.plain nodew idw) script (sample ())) "and it applies"
              | Error f -> failtestf "the base-aware fold refused: %A" f

          testCase "reconcileIn is reconcileManyIn at two heads, and a lane that interferes is still refused"
          <| fun () ->
              let baseId, heads, dag = lanes [ [ RemoveNode "a" ]; [ upd "a1" "x'" ] ]
              let w = FoldFixtures.treeW

              match heads with
              | [ h1; h2 ] ->
                  let two = Dag.reconcileIn w fpAt dag baseId (sample ()) h1 h2
                  Expect.equal two (Dag.reconcileManyIn w fpAt dag baseId (sample ()) heads) "the same verdict"

                  match two with
                  | Error(ReconcileFault.LanesInterfere cs) ->
                      Expect.isTrue (cs |> List.forall (fun c -> c.Address = "a1")) "the edited node is the address"
                  | other -> failtestf "expected LanesInterfere, got %A" other
              | _ -> failtest "two heads"

          testCase
              "the footprint is read at the state the SHARED history reaches, and a rejecting shared region is reported first"
          <| fun () ->
              // Both lanes descend from a node that inserts `n` under b; each then edits a node — one
              // edits `n`, which is absent from the base and present after the shared history.
              let hashFn = OpStream.defaultHash
              let w = FoldFixtures.treeW
              let baseId, d0 = Dag.append hashFn w (Human "base") (Batch []) "" Dag.empty |> built

              let sharedId, d1 =
                  Dag.append hashFn w (Human "shared") (InsertChild("b", RNode.leaf "n" "para" "")) baseId d0
                  |> built

              let h1, d2 = Dag.append hashFn w (Human "l1") (upd "n" "v") sharedId d1 |> built
              let h2, dag = Dag.append hashFn w (Human "l2") (upd "a1" "x'") sharedId d2 |> built

              match Dag.reconcileManyIn w fpAt dag baseId (sample ()) [ h1; h2 ] with
              | Ok script ->
                  Expect.equal
                      script
                      [ InsertChild("b", RNode.leaf "n" "para" ""); upd "n" "v"; upd "a1" "x'" ]
                      "shared first, then the lanes"
              | Error f -> failtestf "refused: %A" f

              // The same DAG from a base state where the shared insert rejects (b is absent).
              let noB =
                  RNode.node "root" "doc" [ RNode.node "a" "section" [ RNode.leaf "a1" "para" "x" ] ]

              match Dag.reconcileManyIn w fpAt dag baseId noB [ h1; h2 ] with
              | Error(ReconcileFault.SharedHistoryRejected(id, _)) ->
                  Expect.equal id sharedId "the shared node is named"
              | other -> failtestf "expected SharedHistoryRejected, got %A" other ]

// ---------------------------------------------------------------------------
//  The law family at the reference domain
// ---------------------------------------------------------------------------

let treeGen: StreamGen<Op, RNode> = { State0 = sample (); Op = genTreeOp }

let treeHash: RNode -> string = Tree.encodeHash nodew encNode

let footprintOf (op: Op) : Footprint =
    Ops.footprint Policy.none (OpScope.plain nodew idw) [ op ]

let footprintIn (t: RNode) (op: Op) : Footprint =
    Ops.footprintIn Policy.none (OpScope.plain nodew idw) t [ op ]

/// A footprint that forgets the removed subtree: the in-place edit of a node inside it is freed, and
/// the law goes red.
let private forgetful (t: RNode) (op: Op) : Footprint =
    match op with
    | RemoveNode x ->
        { footprintIn t op with
            ContentWrites = Set.singleton x }
    | _ -> footprintIn t op

[<Tests>]
let lawTests =
    testList
        "Conformance.footprintInLawsAt"
        [ testCase "the reference domain certifies the base-aware footprint green, with pairs only it frees"
          <| fun () ->
              let results =
                  Conformance.footprintInLawsAt FoldFixtures.treeW footprintOf footprintIn treeHash treeGen 5000 300

              for r in results do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "a footprint that forgets the removed subtree is red on soundness"
          <| fun () ->
              let results =
                  Conformance.footprintInLawsAt FoldFixtures.treeW footprintOf forgetful treeHash treeGen 5000 300

              Expect.isTrue
                  (results |> List.exists (fun r -> not r.Passed && r.Law.Contains "soundness"))
                  "the soundness law names the freed pair that does not commute"

          testCase "a footprint coarser than the pure one is red on refinement"
          <| fun () ->
              let coarse (_: RNode) (op: Op) =
                  { footprintOf op with
                      UnknownParentWrites = Set.add "root" (footprintOf op).UnknownParentWrites }

              let results =
                  Conformance.footprintInLawsAt FoldFixtures.treeW footprintOf coarse treeHash treeGen 5000 300

              Expect.isTrue
                  (results |> List.exists (fun r -> not r.Passed && r.Law.Contains "refinement"))
                  "the refinement law names the pair the pure footprint frees and the coarse one refuses" ]
