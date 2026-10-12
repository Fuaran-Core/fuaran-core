module Fuaran.Core.Tests.FoldFixtures

// The two lane-set domains the fold-confluence pack (FoldConfluenceTests.fs) certifies production
// against, shared with the F*-oracle differential host (ProofOracle.*Tests.fs), which runs the
// extracted model over the SAME lane sets, so the two draw from one generator. The witnesses and
// their lane generators are public since Phase 131.

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
    | UpdateNode node -> "U|" + encNode node

let treeW: StreamWitness<SkeletonOp<RNode, string>, RNode, Rejection<string>> =
    { Apply = fun op st -> Ops.apply nodew idw op st
      Encode = encTreeOp
      Decode = fun _ -> Error "FoldConfluenceTests: the tree witness's decode is unused by the pack" }

let treeBase = sample ()

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

let treeLaneGen: LaneGen<SkeletonOp<RNode, string>, RNode> =
    { State0 = treeBase
      // Never applied — it sits in the base closure, which `betweenOps` excludes from every lane
      // delta. It only seeds the base node's content id.
      BaseOp = RemoveNode "root"
      Lanes =
        fun n r0 ->
            let mutable r = r0
            let mutable lanes = []

            for _ in 1..n do
                // A lane is 0, 1 or 2 ops rather than always 2. Four full lanes of skeleton ops
                // over a six-node tree essentially never have mutually-independent footprints —
                // every address is within two hops of the root — so a fixed length of 2 leaves the
                // FOLD-coverage guard with nothing to certify at 4 lanes (measured: 1 folding set
                // in 300 trials, and the vacuity guard duly fired). Varying the length restores a
                // mixture of both outcome classes at every lane count (measured at 4 lanes,
                // seed 2200: 73 folded / 227 halted in 300), which is what makes the fold law and
                // the halt law both real. An empty lane is explicitly legitimate — see `LaneGen`.
                let len, rl = ConfRng.intBelow 3 r
                let s, r' = treeScript len rl
                r <- r'
                lanes <- lanes @ [ s ]

            lanes, r }

let treeHash = Tree.encodeHash nodew encNode

let treeFootprint (op: SkeletonOp<RNode, string>) = Ops.footprint nodew idw [ op ]

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

let noAddr: Set<string> = Set.empty

/// The domain's own address projection. Every op that requires an item to EXIST reads it — which
/// is the clause it is easiest to get wrong: `AddDep` writing only its own structure would leave a
/// lane creating an item footprint-independent of a lane depending on it, and those two do not
/// commute. The pack's classification law catches exactly that omission.
let planFootprint (op: PlanOp) : Footprint =
    match op with
    | AddItem(id, _) ->
        { Reads = noAddr
          StructureWrites = noAddr
          ContentWrites = Set.singleton id
          UnknownParentWrites = noAddr
          SlotReads = Set.empty
          SlotWrites = Set.empty }
    | Retitle(id, _)
    | SetShipped id ->
        { Reads = Set.singleton id
          StructureWrites = noAddr
          ContentWrites = Set.singleton id
          UnknownParentWrites = noAddr
          SlotReads = Set.empty
          SlotWrites = Set.empty }
    | AddDep(id, dep) ->
        { Reads = Set.ofList [ id; dep ]
          StructureWrites = Set.singleton id
          ContentWrites = noAddr
          UnknownParentWrites = noAddr
          SlotReads = Set.empty
          SlotWrites = Set.empty }

let encPlanOp (op: PlanOp) : string =
    match op with
    | AddItem(i, t) -> "A|" + i + "|" + t
    | Retitle(i, t) -> "T|" + i + "|" + t
    | SetShipped i -> "S|" + i
    | AddDep(i, d) -> "D|" + i + "|" + d

let decPlanOp (s: string) : Result<PlanOp, string> =
    match s.Split('|') |> List.ofArray with
    | [ "A"; i; t ] -> Ok(AddItem(i, t))
    | [ "T"; i; t ] -> Ok(Retitle(i, t))
    | [ "S"; i ] -> Ok(SetShipped i)
    | [ "D"; i; d ] -> Ok(AddDep(i, d))
    | _ -> Error("PlanOp: cannot decode " + s)

let planW: StreamWitness<PlanOp, Plan, string> =
    { Apply = planApply
      Encode = encPlanOp
      Decode = decPlanOp }

/// A canonical rendering of the whole plan, hashed — `Map` enumerates key-sorted, so the
/// rendering is a pure function of the state's content.
let planHash (p: Plan) : string =
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
let basePlan: Plan =
    [ for i in 1..8 ->
          "p" + string i,
          { Title = "item-" + string i
            Shipped = i = 3
            Deps = Set.empty } ]
    |> Map.ofList

let private genPlanOp (p: Plan) (rng: ConfRng.T) : PlanOp * ConfRng.T =
    let ids = p |> Map.toList |> List.map fst

    let choose (r: ConfRng.T) =
        let i, r' = ConfRng.intBelow (List.length ids) r in List.item i ids, r'

    let kind, r1 = ConfRng.intBelow 4 rng

    match kind with
    | 0 ->
        // A deliberately SMALL fresh-id pool, so two lanes routinely add the same id — the
        // halting path has to be exercised or the halt law certifies nothing.
        let v, r2 = ConfRng.intBelow 4 r1
        let t, r3 = ConfRng.intBelow 3 r2
        AddItem("n" + string v, "title-" + string t), r3
    | 1 ->
        let i, r2 = choose r1
        let t, r3 = ConfRng.intBelow 3 r2
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

let planLaneGen: LaneGen<PlanOp, Plan> =
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
