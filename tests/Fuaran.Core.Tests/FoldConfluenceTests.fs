module Fuaran.Core.Tests.FoldConfluenceTests

// Phase 100 — the fold-confluence pack: N-lane arrival-order invariance certified against the
// REFERENCE witness (the tree/skeleton-op algebra) and against a SECOND, non-tree domain shaped
// like a real op-stream consumer; the canonical conflict report's order-independence; the N-lane
// `Dag.reconcileMany` fold surface agreeing with the two-head `Dag.reconcile`; and the teeth —
// a deliberately order-sensitive witness whose footprint declares everything independent must
// make the pack bite and report a SHRUNK counterexample.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.FoldFixtures

// ---------------------------------------------------------------------------
//  Domain 3 — the teeth. An openly order-sensitive reducer (string append) whose footprint
//  declares EVERY op independent of every other. Nothing can ever halt, so every lane set must
//  fold — and two lanes of distinct appends cannot fold to one state. The pack must bite.
// ---------------------------------------------------------------------------

let private appendW: StreamWitness<string, string, string> =
    { Apply = fun op st -> Ok(st + op)
      Encode = id
      Decode = Ok }

let blindFootprint (_: string) : Footprint =
    { Reads = noAddr
      StructureWrites = noAddr
      ContentWrites = noAddr
      UnknownParentWrites = noAddr
      SlotReads = Set.empty
      SlotWrites = Set.empty }

/// Three lanes of three distinct ops each — deliberately larger than the defect, so the shrinker
/// has something to reduce and the assertion below can prove it did.
let private appendLaneGen: LaneGen<string, string> =
    { State0 = ""
      BaseOp = "base"
      Lanes =
        fun n r0 ->
            let mutable r = r0
            let mutable lanes = []

            for i in 0 .. n - 1 do
                let mutable lane = []

                for j in 0..2 do
                    let v, r' = ConfRng.intBelow 26 r
                    r <- r'
                    lane <- lane @ [ string (char (int 'a' + v)) + string i + string j ]

                lanes <- lanes @ [ lane ]

            lanes, r }

// ---------------------------------------------------------------------------

let private expectGreen (label: string) (results: LawResult list) =
    if results |> List.exists (fun r -> not r.Passed) then
        let fails =
            results
            |> List.filter (fun r -> not r.Passed)
            |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

        failtestf "%s failed:\n%s" label (String.concat "\n" fails)

let private lawNamed (fragment: string) (results: LawResult list) =
    results
    |> List.tryFind (fun r -> r.Law.Contains fragment)
    |> function
        | Some r -> r
        | None -> failtestf "no law whose name contains %s (laws: %A)" fragment (results |> List.map _.Law)

[<Tests>]
let foldConfluenceTests =
    testList
        "Conformance.FoldConfluence"
        [

          // ---- the reference witness ----

          testCase "the reference witness certifies fold + halt determinism, classification and sample adequacy"
          <| fun _ ->
              let results =
                  FoldConfluence.laneFoldLawsAt treeW treeFootprint treeHash treeLaneGen 3 1000 120

              Expect.equal (List.length results) 4 "three invariance laws + the sample-adequacy guard reported"
              expectGreen "the reference witness's laneFoldLaws" results

          testCase "the reference certification is seed-replayable"
          <| fun _ ->
              Expect.equal
                  (FoldConfluence.laneFoldLawsAt treeW treeFootprint treeHash treeLaneGen 3 1000 60)
                  (FoldConfluence.laneFoldLawsAt treeW treeFootprint treeHash treeLaneGen 3 1000 60)
                  "the same seed reproduces the same report"

          testCase "the reference witness certifies green under a different lane count"
          <| fun _ ->
              // 4 lanes is still exhaustively enumerated (4! = 24 = permutationBound).
              expectGreen
                  "laneFoldLaws at 4 lanes"
                  (FoldConfluence.laneFoldLawsAt treeW treeFootprint treeHash treeLaneGen 4 2200 60)

          // ---- the second, non-tree domain ----

          testCase "the work-plan domain certifies green — witness-generic, non-tree state, own footprint"
          <| fun _ ->
              let results =
                  FoldConfluence.laneFoldLawsAt planW planFootprint planHash planLaneGen 3 4100 150

              expectGreen "the work-plan domain's laneFoldLaws" results

          testCase "the work-plan sample exercises BOTH the folding and the halting path"
          <| fun _ ->
              let results =
                  FoldConfluence.laneFoldLawsAt planW planFootprint planHash planLaneGen 3 4100 150

              // One guard, both counts: Phase 121 moved this pack's two hand-written coverage laws
              // onto the kit's shared adequacy guard, which reports every verdict it demanded and
              // names the ones it never reached.
              Expect.isTrue
                  (lawNamed "sample adequacy" results).Passed
                  "both the folding and the halting lane set were exercised"

          testCase "the work-plan op codec round-trips (the witness a consumer copies is complete)"
          <| fun _ ->
              let ops =
                  [ AddItem("n1", "title-0")
                    Retitle("p1", "title-2")
                    SetShipped "p2"
                    AddDep("p1", "p3") ]

              for op in ops do
                  Expect.equal (decPlanOp (encPlanOp op)) (Ok op) "encode ∘ decode = identity"

          testCase "certifyFold aggregates the work-plan verdict"
          <| fun _ ->
              let report =
                  FoldConfluence.certifyFold planW planFootprint planHash planLaneGen 3 4100 80

              Expect.isTrue report.AllPassed "the aggregate verdict is green"
              Expect.equal (List.length report.Results) 4 "the aggregate carries every law"

          // ---- the teeth: the pack can fail, and shrinks what it reports ----

          testCase "an order-sensitive witness with a blind footprint makes the fold law bite"
          <| fun _ ->
              let results =
                  FoldConfluence.laneFoldLawsAt appendW blindFootprint id appendLaneGen 3 7 20

              let fold = lawNamed "lane-fold determinism" results
              Expect.isFalse fold.Passed "an order-sensitive reducer cannot fold order-independently"

              // The halt law is vacuously green (a blind footprint never conflicts) — and the
              // sample-adequacy guard is what refuses to let that read as a certification.
              Expect.isTrue
                  (lawNamed "lane-halt determinism" results).Passed
                  "nothing halted, so nothing halted wrongly"

              let adequacy = lawNamed "sample adequacy" results
              Expect.isFalse adequacy.Passed "the vacuity guard reports that the halt law was never tested"

              match adequacy.Counterexample with
              | Some c ->
                  Expect.stringContains c "halted=0" "the guard reports the count it reached, not merely that it failed"
              | None -> failtest "the failing adequacy guard carried no counts"

          testCase "the reported counterexample is SHRUNK to two lanes of one op"
          <| fun _ ->
              let results =
                  FoldConfluence.laneFoldLawsAt appendW blindFootprint id appendLaneGen 3 7 20

              let cx =
                  match (lawNamed "lane-fold determinism" results).Counterexample with
                  | Some c -> c
                  | None -> failtest "the failing law carried no counterexample"

              Expect.stringContains cx "shrunk to" "the counterexample says it was shrunk"
              Expect.stringContains cx "lane 0: [" "lane 0 survived"
              Expect.stringContains cx "lane 1: [" "lane 1 survived"
              Expect.isFalse (cx.Contains "lane 2: [") (sprintf "the third lane was shrunk away — got:\n%s" cx)

              // one op per surviving lane: `renderLanes` joins a lane's ops with "; "
              let laneLines =
                  cx.Split('\n') |> Array.filter (fun l -> l.StartsWith "  lane ") |> List.ofArray

              Expect.equal (List.length laneLines) 2 (sprintf "two lanes survived — got:\n%s" cx)

              Expect.isTrue
                  (laneLines |> List.forall (fun l -> not (l.Contains "; ")))
                  (sprintf "each surviving lane was shrunk to one op — got:\n%s" cx)

              Expect.stringContains cx "folded → " "the divergent outcomes are shown"

          testCase "shrinkLanes reduces to a locally-minimal witness of the divergence"
          <| fun _ ->
              let diverges (ls: string list list) =
                  FoldConfluence.arrivalOrders (List.length ls)
                  |> List.map (fun p ->
                      FoldConfluence.foldOnce
                          appendW
                          blindFootprint
                          OpStream.defaultHash
                          id
                          ""
                          "base"
                          (p |> List.map (fun i -> List.item i ls)))
                  |> List.distinct
                  |> List.length > 1

              let shrunk =
                  FoldConfluence.shrinkLanes diverges [ [ "a"; "b"; "c" ]; [ "d"; "e" ]; [ "f" ] ]

              Expect.equal (List.length shrunk) 2 "shrunk to two lanes"
              Expect.isTrue (shrunk |> List.forall (fun l -> List.length l = 1)) "one op per lane"
              Expect.isTrue (diverges shrunk) "the shrunk set still diverges"

          // ---- the pieces the laws rest on ----

          testCase "arrivalOrders enumerates exhaustively to the bound and samples above it"
          <| fun _ ->
              Expect.equal (List.length (FoldConfluence.arrivalOrders 1)) 1 "1! = 1"
              Expect.equal (List.length (FoldConfluence.arrivalOrders 3)) 6 "3! = 6"
              Expect.equal (List.length (FoldConfluence.arrivalOrders 4)) 24 "4! = 24 — the bound"

              Expect.isTrue
                  (List.length (FoldConfluence.arrivalOrders 6) <= FoldConfluence.permutationBound)
                  "6! is sampled, not enumerated"

              Expect.equal
                  (FoldConfluence.arrivalOrders 6)
                  (FoldConfluence.arrivalOrders 6)
                  "the sampled order set is a pure function of the lane count — shrinking re-measures against it"

              for orders in [ FoldConfluence.arrivalOrders 3; FoldConfluence.arrivalOrders 6 ] do
                  for o in orders do
                      Expect.equal (List.sort o) [ 0 .. List.length o - 1 ] "every order is a permutation"

          testCase "the canonical conflict report is independent of which delta is handed over first"
          <| fun _ ->
              let a = [ AddItem("n1", "title-0") ]
              let b = [ AddItem("n1", "title-1") ]

              let ab =
                  FoldConfluence.canonicalConflictReport encPlanOp (Dag.conflicts planFootprint a b)

              let ba =
                  FoldConfluence.canonicalConflictReport encPlanOp (Dag.conflicts planFootprint b a)

              Expect.isFalse (ab = "") "the two lanes genuinely conflict"
              Expect.equal ab ba "the canonical rendering is symmetric — 'halts identically' is a real claim"

              // …while the RAW reports are not, which is why the canonicalisation exists.
              Expect.notEqual
                  (Dag.conflicts planFootprint a b)
                  (Dag.conflicts planFootprint b a)
                  "the raw report swaps Left/Right — comparing raw reports would fail for presentation, not divergence"

          testCase "the canonical conflict report distinguishes the three shapes"
          <| fun _ ->
              // Three conflicts that differ ONLY in their shape. The renderer once matched the cases
              // unqualified after `MergeConflictShape` became `[<RequireQualifiedAccess>]`, so its
              // first arm was a variable pattern that caught every shape: all three rendered as
              // "concurrent-update", collapsed to one line, and two reports differing in shape
              // compared equal.
              let conflictOf shape =
                  { Left = "op-left"
                    Right = "op-right"
                    Address = "n1"
                    Shape = shape }

              let shapes =
                  [ MergeConflictShape.ConcurrentUpdate
                    MergeConflictShape.InsertPositionClash
                    MergeConflictShape.MoveVsRemove
                    // Phase 340 — the slot is part of the shape's identity.
                    MergeConflictShape.SlotClash "tier"
                    MergeConflictShape.SlotClash "thinking" ]

              let report =
                  FoldConfluence.canonicalConflictReport id (shapes |> List.map conflictOf)

              Expect.equal
                  (report.Split '\n' |> List.ofArray)
                  [ "concurrent-update|n1|op-left|op-right"
                    "insert-position-clash|n1|op-left|op-right"
                    "move-vs-remove|n1|op-left|op-right"
                    "slot-clash:thinking|n1|op-left|op-right"
                    "slot-clash:tier|n1|op-left|op-right" ]
                  "each shape renders as its own line — a slot clash per slot"

              for s in shapes do
                  for t in shapes do
                      if s <> t then
                          Expect.notEqual
                              (FoldConfluence.canonicalConflictReport id [ conflictOf s ])
                              (FoldConfluence.canonicalConflictReport id [ conflictOf t ])
                              (sprintf "a %A report and a %A report must not compare equal" s t)

          testCase "Dag.reconcileMany is Dag.reconcile at N = 2"
          <| fun _ ->
              let hashFn = OpStream.defaultHash

              let chain (ops: PlanOp list) (parent: string) (d0: Dag.T<PlanOp>) =
                  ops
                  |> List.fold
                      (fun (h, d) op -> Dag.append hashFn planW (Human "lane") op h d |> Reference.built)
                      (parent, d0)

              let build (a: PlanOp list) (b: PlanOp list) =
                  let baseId, d0 =
                      Dag.append hashFn planW (Human "base") (SetShipped "p3") "" Dag.empty
                      |> Reference.built

                  let headA, d1 = chain a baseId d0
                  let headB, d2 = chain b baseId d1
                  baseId, headA, headB, d2

              // a clean pair (disjoint items) and a conflicting pair (same fresh id)
              for a, b in
                  [ [ AddItem("n1", "t") ], [ AddItem("n2", "t") ]
                    [ AddItem("n1", "t") ], [ AddItem("n1", "u") ]
                    [ Retitle("p1", "t") ], [ SetShipped "p2" ] ] do
                  let baseId, headA, headB, dag = build a b

                  Expect.equal
                      (Dag.reconcileMany planW planFootprint dag baseId basePlan [ headA; headB ]
                       |> Result.mapError (function
                           | ReconcileFault.LanesInterfere cs -> cs
                           | other -> failwithf "unexpected refusal %A" other))
                      (Dag.reconcile planFootprint dag baseId headA headB)
                      "the N-lane fold is the two-head fold at N = 2, where both lanes apply"

          testCase "Dag.reconcileMany folds three disjoint lanes and halts on any interfering pair"
          <| fun _ ->
              let hashFn = OpStream.defaultHash

              let chain (i: int) (ops: PlanOp list) (parent: string) (d0: Dag.T<PlanOp>) =
                  ops
                  |> List.fold
                      (fun (h, d) op -> Dag.append hashFn planW (Human("lane-" + string i)) op h d |> Reference.built)
                      (parent, d0)

              let build (lanes: PlanOp list list) =
                  let baseId, d0 =
                      Dag.append hashFn planW (Human "base") (SetShipped "p3") "" Dag.empty
                      |> Reference.built

                  let heads, dag =
                      lanes
                      |> List.indexed
                      |> List.fold
                          (fun (hs, d) (i, ops) ->
                              let h, d' = chain i ops baseId d
                              hs @ [ h ], d')
                          ([], d0)

                  baseId, heads, dag

              let baseId, heads, dag =
                  build [ [ AddItem("n1", "t") ]; [ AddItem("n2", "t") ]; [ Retitle("p1", "t") ] ]

              match Dag.reconcileMany planW planFootprint dag baseId basePlan heads with
              | Ok script -> Expect.equal (List.length script) 3 "every disjoint lane's op is in the merge script"
              | Error cs -> failtestf "three disjoint lanes should fold clean; got %A" cs

              // lane 2 now collides with lane 0 — the pairwise sweep must find it even though the
              // interfering pair is not the first one checked.
              let baseId2, heads2, dag2 =
                  build [ [ AddItem("n1", "t") ]; [ AddItem("n2", "t") ]; [ AddItem("n1", "u") ] ]

              match Dag.reconcileMany planW planFootprint dag2 baseId2 basePlan heads2 with
              | Ok _ -> failtest "a colliding lane pair must halt the whole fold"
              | Error(ReconcileFault.LanesInterfere cs) -> Expect.isNonEmpty cs "the halt names the interference"
              | Error other -> failtestf "a colliding lane pair halts on interference, not %A" other ]

// ---------------------------------------------------------------------------
//  Phase 245 — the adequacy line counts rejected lanes beside folded and halted ones
// ---------------------------------------------------------------------------

/// Every op this reducer is handed is refused with ONE reason, and the blind footprint never halts,
/// so every lane set replays into a rejection — the same rejection under every arrival order (a
/// reason naming the op would differ by which lane arrived first, and law 1 would rightly see a
/// divergence), so the three subject laws hold over it and certify nothing about folding or
/// halting.
let private rejectW: StreamWitness<string, string, string> =
    { Apply = fun _ _ -> Error "refused"
      Encode = id
      Decode = Ok }

[<Tests>]
let rejectedLaneTests =
    testList
        "FoldConfluence.rejectedLanes"
        [ testCase "go-red: a sample of only rejected lanes is starved, and the adequacy line names the rejections"
          <| fun _ ->
              let results =
                  FoldConfluence.laneFoldLawsAt rejectW blindFootprint id appendLaneGen 3 7 20

              Expect.isTrue
                  (results
                   |> List.filter (fun r -> not (r.Law.StartsWith SampleAdequacy.guardOpening))
                   |> List.forall _.Passed)
                  "the subject laws hold over lane sets that all reject — which is the problem"

              let adequacy = lawNamed "sample adequacy" results
              Expect.isFalse adequacy.Passed "a sample of only rejected lanes is starved"

              let cx = defaultArg adequacy.Counterexample ""
              Expect.stringContains cx "folded=0 halted=0" "the demanded outcomes read zero"
              // Phase 300 — every drawn lane set is judged in three shapes (disjoint, duplicate head,
              // fast-forward), so twenty draws are sixty judged lane sets, each counted.
              Expect.stringContains cx "rejected=60" "and the line names the lanes that were rejected instead"
              Expect.stringContains cx "duplicate-head=20 fast-forward=20" "and the two shared-history shapes it drew"

          testCase "a rejected lane is counted, not demanded — the work-plan reference stays green"
          <| fun _ ->
              // The work-plan reference reaches both demanded outcomes; whether it also reaches a
              // rejection is the domain's business, and a guard that DEMANDED one would starve every
              // domain whose reducer accepts what its footprint lets through.
              let results =
                  FoldConfluence.laneFoldLawsAt planW planFootprint planHash planLaneGen 3 4100 150

              Expect.isTrue (lawNamed "sample adequacy" results).Passed "the guard does not demand a rejection" ]
