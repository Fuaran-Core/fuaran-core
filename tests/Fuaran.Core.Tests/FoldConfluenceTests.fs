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

// ---------------------------------------------------------------------------
//  Domain 1 — the reference witness (tree + skeleton ops), lifted to a StreamWitness.
// ---------------------------------------------------------------------------

let private genFresh (existing: Set<string>) (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable r = rng

    let rec pick () =
        let v, r' = ConfRng.next r
        r <- r'
        let id = "f" + string (v % 100000)
        if existing.Contains id then pick () else id

    RNode.leaf (pick ()) "para" "x", r

/// One random (possibly-invalid) skeleton op against `tree` — the shape the kit's own private
/// generator uses; test-local, as every law-test file in this suite carries its own.
let private genOp (tree: RNode) (rng: ConfRng.T) : SkeletonOp<RNode, string> * ConfRng.T =
    let ids = Tree.preorder nodew tree |> List.map nodew.Id
    let kind, r1 = ConfRng.intBelow 4 rng

    match kind with
    | 0 ->
        let parent, r2 = ConfRng.choose ids r1
        let fresh, r3 = genFresh (Set.ofList ids) r2
        InsertChild(parent, fresh), r3
    | 1 ->
        let target, r2 = ConfRng.choose ids r1
        RemoveNode target, r2
    | 2 ->
        let target, r2 = ConfRng.choose ids r1
        let np, r3 = ConfRng.choose ids r2
        MoveNode(target, np), r3
    | _ ->
        let parent, r2 = ConfRng.choose ids r1

        match Tree.tryFind nodew idw parent tree with
        | Some p ->
            let kids = nodew.Children p |> List.map nodew.Id
            let shuffled, r3 = ConfRng.shuffle kids r2
            ReorderChildren(parent, shuffled), r3
        | None -> ReorderChildren(parent, []), r2

/// A structural fingerprint of an op — enough for distinct ops to get distinct DAG content ids,
/// and it is what a counterexample is rendered through.
let rec private encTreeOp (op: SkeletonOp<RNode, string>) : string =
    match op with
    | InsertChild(p, node) ->
        "I|"
        + p
        + "|"
        + (Tree.preorder nodew node |> List.map encNode |> String.concat ",")
    | RemoveNode t -> "R|" + t
    | MoveNode(t, np) -> "M|" + t + "|" + np
    | ReorderChildren(p, order) -> "O|" + p + "|" + String.concat "," order
    | Batch inner -> "B|" + (inner |> List.map encTreeOp |> String.concat ";")

let private treeW: StreamWitness<SkeletonOp<RNode, string>, RNode, Rejection<string>> =
    { Apply = fun op st -> Ops.apply nodew idw op st
      Encode = encTreeOp
      Decode = fun _ -> Error "FoldConfluenceTests: the tree witness's decode is unused by the pack" }

let private treeBase = sample ()

/// An applyable script: `n` random ops threaded from the base tree, keeping the accepted ones.
let private treeScript (n: int) (r0: ConfRng.T) =
    let mutable cur = treeBase
    let mutable accepted = []
    let mutable r = r0

    for _ in 1..n do
        let op, r' = genOp cur r
        r <- r'

        match Ops.apply nodew idw op cur with
        | Ok t' ->
            cur <- t'
            accepted <- accepted @ [ op ]
        | Error _ -> ()

    accepted, r

let private treeLaneGen: LaneGen<SkeletonOp<RNode, string>, RNode> =
    { State0 = treeBase
      // Never applied — it sits in the base closure, which `betweenOps` excludes from every lane
      // delta. It only seeds the base node's content id.
      BaseOp = RemoveNode "root"
      Lanes =
        fun n r0 ->
            let mutable r = r0
            let mutable lanes = []

            for _ in 1..n do
                let s, r' = treeScript 2 r
                r <- r'
                lanes <- lanes @ [ s ]

            lanes, r }

let private treeHash = Tree.encodeHash nodew encNode

let private treeFootprint (op: SkeletonOp<RNode, string>) = Ops.footprint nodew idw [ op ]

// ---------------------------------------------------------------------------
//  Domain 2 — a NON-tree domain with the shape of a real op-stream consumer: a work plan of
//  items, each with a title, a shipped flag and a dependency set. It is here because the pack's
//  claim is witness-generic and the reference witness alone cannot demonstrate that: this
//  domain's state is a Map, not a tree, and its footprint projection is its own rather than
//  `Ops.footprint`. It is the worked example a consuming domain copies.
// ---------------------------------------------------------------------------

type PlanItem =
    { Title: string
      Shipped: bool
      Deps: Set<string> }

type Plan = Map<string, PlanItem>

type PlanOp =
    | AddItem of id: string * title: string
    | Retitle of id: string * title: string
    | SetShipped of id: string
    | AddDep of id: string * dependsOn: string

let private planApply (op: PlanOp) (p: Plan) : Result<Plan, string> =
    match op with
    | AddItem(id, title) ->
        if p.ContainsKey id then
            Error("duplicate item " + id)
        else
            Ok(
                Map.add
                    id
                    { Title = title
                      Shipped = false
                      Deps = Set.empty }
                    p
            )
    | Retitle(id, title) ->
        match Map.tryFind id p with
        | None -> Error("no item " + id)
        | Some it -> Ok(Map.add id { it with Title = title } p)
    | SetShipped id ->
        match Map.tryFind id p with
        | None -> Error("no item " + id)
        | Some it -> Ok(Map.add id { it with Shipped = true } p)
    | AddDep(id, dep) ->
        match Map.tryFind id p with
        | None -> Error("no item " + id)
        | Some it ->
            if not (p.ContainsKey dep) then
                Error("no dependency target " + dep)
            else
                Ok(Map.add id { it with Deps = Set.add dep it.Deps } p)

let private noAddr: Set<string> = Set.empty

/// The domain's own address projection. Every op that requires an item to EXIST reads it — which
/// is the clause it is easiest to get wrong: `AddDep` writing only its own structure would leave a
/// lane creating an item footprint-independent of a lane depending on it, and those two do not
/// commute. The pack's classification law catches exactly that omission.
let private planFootprint (op: PlanOp) : Footprint =
    match op with
    | AddItem(id, _) ->
        { Reads = noAddr
          StructureWrites = noAddr
          ContentWrites = Set.singleton id
          UnknownParentWrites = noAddr }
    | Retitle(id, _)
    | SetShipped id ->
        { Reads = Set.singleton id
          StructureWrites = noAddr
          ContentWrites = Set.singleton id
          UnknownParentWrites = noAddr }
    | AddDep(id, dep) ->
        { Reads = Set.ofList [ id; dep ]
          StructureWrites = Set.singleton id
          ContentWrites = noAddr
          UnknownParentWrites = noAddr }

let private encPlanOp (op: PlanOp) : string =
    match op with
    | AddItem(i, t) -> "A|" + i + "|" + t
    | Retitle(i, t) -> "T|" + i + "|" + t
    | SetShipped i -> "S|" + i
    | AddDep(i, d) -> "D|" + i + "|" + d

let private decPlanOp (s: string) : Result<PlanOp, string> =
    match s.Split('|') |> List.ofArray with
    | [ "A"; i; t ] -> Ok(AddItem(i, t))
    | [ "T"; i; t ] -> Ok(Retitle(i, t))
    | [ "S"; i ] -> Ok(SetShipped i)
    | [ "D"; i; d ] -> Ok(AddDep(i, d))
    | _ -> Error("PlanOp: cannot decode " + s)

let private planW: StreamWitness<PlanOp, Plan, string> =
    { Apply = planApply
      Encode = encPlanOp
      Decode = decPlanOp }

/// A canonical rendering of the whole plan, hashed — `Map` enumerates key-sorted, so the
/// rendering is a pure function of the state's content.
let private planHash (p: Plan) : string =
    let body =
        p
        |> Map.toList
        |> List.map (fun (k, v) ->
            k
            + "="
            + v.Title
            + "/"
            + (if v.Shipped then "1" else "0")
            + "/["
            + (v.Deps |> Set.toList |> String.concat ",")
            + "]")
        |> String.concat ";"

    OpStream.defaultHash "" body

/// Wide enough that lanes CAN be disjoint and narrow enough that they often are not: both
/// coverage guards have to fire, and an address space of three items makes every trial collide.
let private basePlan: Plan =
    [ for i in 1..8 ->
          "p" + string i,
          { Title = "item-" + string i
            Shipped = i = 3
            Deps = Set.empty } ]
    |> Map.ofList

/// A draw from the HIGH bits of the kit's LCG. `ConfRng.intBelow` takes `v % n`, and an LCG modulo
/// 2^32 gives bit k a period of 2^(k+1) — so a small `n` cycles in step across consecutively-drawn
/// lanes. That is invisible on the tree generator (its fresh ids draw a whole word) and decisive
/// here: with `intBelow`, all three lanes picked colliding addresses in lockstep and 150 of 150
/// trials halted — the fold law never ran, which the coverage guard duly reported. Local to this
/// generator; the kit's own sampling is unchanged.
let private pick (n: int) (r: ConfRng.T) : int * ConfRng.T =
    let v, r' = ConfRng.next r
    (v / 4096) % n, r'

let private genPlanOp (p: Plan) (rng: ConfRng.T) : PlanOp * ConfRng.T =
    let ids = p |> Map.toList |> List.map fst

    let choose (r: ConfRng.T) =
        let i, r' = pick (List.length ids) r in List.item i ids, r'

    let kind, r1 = pick 4 rng

    match kind with
    | 0 ->
        // A deliberately SMALL fresh-id pool, so two lanes routinely add the same id — the
        // halting path has to be exercised or the halt law certifies nothing.
        let v, r2 = pick 4 r1
        let t, r3 = pick 3 r2
        AddItem("n" + string v, "title-" + string t), r3
    | 1 ->
        let i, r2 = choose r1
        let t, r3 = pick 3 r2
        Retitle(i, "title-" + string t), r3
    | 2 ->
        let i, r2 = choose r1
        SetShipped i, r2
    | _ ->
        let i, r2 = choose r1
        let d, r3 = choose r2
        AddDep(i, d), r3

let private planScript (n: int) (r0: ConfRng.T) =
    let mutable cur = basePlan
    let mutable accepted = []
    let mutable r = r0

    for _ in 1..n do
        let op, r' = genPlanOp cur r
        r <- r'

        match planApply op cur with
        | Ok p' ->
            cur <- p'
            accepted <- accepted @ [ op ]
        | Error _ -> ()

    accepted, r

let private planLaneGen: LaneGen<PlanOp, Plan> =
    { State0 = basePlan
      BaseOp = SetShipped "p3"
      Lanes =
        fun n r0 ->
            let mutable r = r0
            let mutable lanes = []

            for _ in 1..n do
                let s, r' = planScript 2 r
                r <- r'
                lanes <- lanes @ [ s ]

            lanes, r }

// ---------------------------------------------------------------------------
//  Domain 3 — the teeth. An openly order-sensitive reducer (string append) whose footprint
//  declares EVERY op independent of every other. Nothing can ever halt, so every lane set must
//  fold — and two lanes of distinct appends cannot fold to one state. The pack must bite.
// ---------------------------------------------------------------------------

let private appendW: StreamWitness<string, string, string> =
    { Apply = fun op st -> Ok(st + op)
      Encode = id
      Decode = Ok }

let private blindFootprint (_: string) : Footprint =
    { Reads = noAddr
      StructureWrites = noAddr
      ContentWrites = noAddr
      UnknownParentWrites = noAddr }

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
        | None -> failtestf "no law whose name contains %s (laws: %A)" fragment (results |> List.map (fun r -> r.Law))

[<Tests>]
let foldConfluenceTests =
    testList
        "Conformance.FoldConfluence"
        [

          // ---- the reference witness ----

          testCase "the reference witness certifies fold + halt determinism, classification and both coverage guards"
          <| fun _ ->
              let results =
                  FoldConfluence.laneFoldLaws treeW treeFootprint treeHash treeLaneGen 3 1000 120

              Expect.equal (List.length results) 5 "three invariance laws + two coverage guards reported"
              expectGreen "the reference witness's laneFoldLaws" results

          testCase "the reference certification is seed-replayable"
          <| fun _ ->
              Expect.equal
                  (FoldConfluence.laneFoldLaws treeW treeFootprint treeHash treeLaneGen 3 1000 60)
                  (FoldConfluence.laneFoldLaws treeW treeFootprint treeHash treeLaneGen 3 1000 60)
                  "the same seed reproduces the same report"

          testCase "the reference witness certifies green under a different lane count"
          <| fun _ ->
              // 4 lanes is still exhaustively enumerated (4! = 24 = permutationBound).
              expectGreen
                  "laneFoldLaws at 4 lanes"
                  (FoldConfluence.laneFoldLaws treeW treeFootprint treeHash treeLaneGen 4 2200 60)

          // ---- the second, non-tree domain ----

          testCase "the work-plan domain certifies green — witness-generic, non-tree state, own footprint"
          <| fun _ ->
              let results =
                  FoldConfluence.laneFoldLaws planW planFootprint planHash planLaneGen 3 4100 150

              expectGreen "the work-plan domain's laneFoldLaws" results

          testCase "the work-plan sample exercises BOTH the folding and the halting path"
          <| fun _ ->
              let results =
                  FoldConfluence.laneFoldLaws planW planFootprint planHash planLaneGen 3 4100 150

              Expect.isTrue (lawNamed "fold coverage" results).Passed "a folding lane set was exercised"
              Expect.isTrue (lawNamed "conflict coverage" results).Passed "a halting lane set was exercised"

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
              Expect.equal (List.length report.Results) 5 "the aggregate carries every law"

          // ---- the teeth: the pack can fail, and shrinks what it reports ----

          testCase "an order-sensitive witness with a blind footprint makes the fold law bite"
          <| fun _ ->
              let results =
                  FoldConfluence.laneFoldLaws appendW blindFootprint id appendLaneGen 3 7 20

              let fold = lawNamed "lane-fold determinism" results
              Expect.isFalse fold.Passed "an order-sensitive reducer cannot fold order-independently"

              // The halt law is vacuously green (a blind footprint never conflicts) — and the
              // conflict-coverage guard is what refuses to let that read as a certification.
              Expect.isTrue
                  (lawNamed "lane-halt determinism" results).Passed
                  "nothing halted, so nothing halted wrongly"

              Expect.isFalse
                  (lawNamed "conflict coverage" results).Passed
                  "the vacuity guard reports that the halt law was never tested"

          testCase "the reported counterexample is SHRUNK to two lanes of one op"
          <| fun _ ->
              let results =
                  FoldConfluence.laneFoldLaws appendW blindFootprint id appendLaneGen 3 7 20

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

          testCase "Dag.reconcileMany is Dag.reconcile at N = 2"
          <| fun _ ->
              let hashFn = OpStream.defaultHash

              let chain (ops: PlanOp list) (parent: string) (d0: Dag.T<PlanOp>) =
                  ops
                  |> List.fold (fun (h, d) op -> Dag.append hashFn planW (Human "lane") op h d) (parent, d0)

              let build (a: PlanOp list) (b: PlanOp list) =
                  let baseId, d0 =
                      Dag.append hashFn planW (Human "base") (SetShipped "p3") "" Dag.empty

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
                      (Dag.reconcileMany planFootprint dag baseId [ headA; headB ])
                      (Dag.reconcile planFootprint dag baseId headA headB)
                      "the N-lane fold is the two-head fold at N = 2"

          testCase "Dag.reconcileMany folds three disjoint lanes and halts on any interfering pair"
          <| fun _ ->
              let hashFn = OpStream.defaultHash

              let chain (i: int) (ops: PlanOp list) (parent: string) (d0: Dag.T<PlanOp>) =
                  ops
                  |> List.fold
                      (fun (h, d) op -> Dag.append hashFn planW (Human("lane-" + string i)) op h d)
                      (parent, d0)

              let build (lanes: PlanOp list list) =
                  let baseId, d0 =
                      Dag.append hashFn planW (Human "base") (SetShipped "p3") "" Dag.empty

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

              match Dag.reconcileMany planFootprint dag baseId heads with
              | Ok script -> Expect.equal (List.length script) 3 "every disjoint lane's op is in the merge script"
              | Error cs -> failtestf "three disjoint lanes should fold clean; got %A" cs

              // lane 2 now collides with lane 0 — the pairwise sweep must find it even though the
              // interfering pair is not the first one checked.
              let baseId2, heads2, dag2 =
                  build [ [ AddItem("n1", "t") ]; [ AddItem("n2", "t") ]; [ AddItem("n1", "u") ] ]

              match Dag.reconcileMany planFootprint dag2 baseId2 heads2 with
              | Ok _ -> failtest "a colliding lane pair must halt the whole fold"
              | Error cs -> Expect.isNonEmpty cs "the halt names the interference" ]
