module Fuaran.Core.Tests.ProofOracleTests

// Phase 131 — the F* oracle as a corpus host.
//
// `proofs/DagFold.fst` is an F* model of the N-lane DAG fold with fold confluence proved as a
// theorem; `proofs/oracle/DagFold.fs` is that model extracted to F# by F*'s own code generator.
// This file runs the extracted model BESIDE the production fold — `Dag.reconcileMany` through a
// real DAG, rendered exactly as `FoldConfluence.foldOnce` renders it — over the same generated lane
// sets the Phase 100 pack certifies production against, and over the wire corpus's `ops/` fixtures.
// A disagreement is red and is shrunk to a minimal lane set before it is reported.
//
// Three things are compared per lane set, per arrival order:
//   1. the OUTCOME — folded to the same state hash / halted with the same canonical report /
//      rejected with the same reason (`LaneFoldOutcome` equality);
//   2. the MERGE SCRIPT — the op sequence `reconcileMany` composes, or its canonical halt report;
//   3. the oracle's own outcomes across the sampled arrival orders — one distinct outcome, which is
//      the theorem's statement instantiated on the extracted code.
//
// What this does and does not establish is stated in proofs/README.md: the model starts where the
// lane deltas are known, so agreement here is a claim about production's `betweenOps` recovery and
// pairwise sweep together against a model whose fold is proved order-invariant.

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.FoldConfluenceTests

// ---------------------------------------------------------------------------
//  The bridge — Core's types as the model reads them, and the model's back.
// ---------------------------------------------------------------------------

/// A Core `Footprint` as the model reads it: sets become sorted lists (membership is the meaning).
let private toModelFootprint (f: Footprint) : DagFold.footprint =
    { DagFold.reads = Set.toList f.Reads
      DagFold.structure_writes = Set.toList f.StructureWrites
      DagFold.content_writes = Set.toList f.ContentWrites
      DagFold.unknown_parent_writes = Set.toList f.UnknownParentWrites }

let private ofModelShape (s: DagFold.shape) : MergeConflictShape =
    match s with
    | DagFold.ConcurrentUpdate -> ConcurrentUpdate
    | DagFold.InsertPositionClash -> InsertPositionClash
    | DagFold.MoveVsRemove -> MoveVsRemove

let private ofModelConflict (c: DagFold.conflict<'Op>) : MergeConflict<'Op> =
    { Left = c.left
      Right = c.right
      Address = c.address
      Shape = ofModelShape c.shape }

let private modelApply (w: StreamWitness<'Op, 'State, 'Rej>) (op: 'Op) (st: 'State) : DagFold.outcome<'State, 'Rej> =
    match w.Apply op st with
    | Ok s -> DagFold.Ok s
    | Error e -> DagFold.Error e

/// The oracle's fold, rendered exactly as `FoldConfluence.foldOnce` renders production's.
let private oracleFold
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (hashState: 'State -> string)
    (state0: 'State)
    (lanes: 'Op list list)
    : LaneFoldOutcome =
    match DagFold.fold_once (modelApply w) (fp >> toModelFootprint) state0 lanes with
    | DagFold.LaneFolded s -> LaneFolded(hashState s)
    | DagFold.LaneHalted cs ->
        LaneHalted(FoldConfluence.canonicalConflictReport w.Encode (cs |> List.map ofModelConflict))
    | DagFold.LaneRejected r -> LaneRejected(sprintf "%A" r)

let private productionFold
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (hashState: 'State -> string)
    (state0: 'State)
    (baseOp: 'Op)
    (lanes: 'Op list list)
    : LaneFoldOutcome =
    FoldConfluence.foldOnce w fp OpStream.defaultHash hashState state0 baseOp lanes

/// Production's merge SCRIPT (or canonical halt report) through a real DAG — the same
/// construction `foldOnce` uses: each lane chained onto one base node under its own actor.
let private productionScript
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (baseOp: 'Op)
    (lanes: 'Op list list)
    : Result<'Op list, string> =
    let hashFn = OpStream.defaultHash
    let baseId, d0 = Dag.append hashFn w (Human "base") baseOp "" Dag.empty

    let heads, dag =
        lanes
        |> List.indexed
        |> List.fold
            (fun (hs, d) (i, ops) ->
                let actor = Human("lane-" + string i)

                let head, d' =
                    ops
                    |> List.fold (fun (h, dd) op -> Dag.append hashFn w actor op h dd) (baseId, d)

                hs @ [ head ], d')
            ([], d0)

    match Dag.reconcileMany fp dag baseId heads with
    | Ok script -> Ok script
    | Error cs -> Error(FoldConfluence.canonicalConflictReport w.Encode cs)

/// The oracle's merge script (or canonical halt report), from the lane deltas directly.
let private oracleScript
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (lanes: 'Op list list)
    : Result<'Op list, string> =
    match DagFold.reconcile_many (fp >> toModelFootprint) lanes with
    | DagFold.Ok script -> Ok script
    | DagFold.Error cs -> Error(FoldConfluence.canonicalConflictReport w.Encode (cs |> List.map ofModelConflict))

let private renderLanes (encodeOp: 'Op -> string) (lanes: 'Op list list) : string =
    lanes
    |> List.mapi (fun i ops ->
        "  lane "
        + string i
        + ": ["
        + (ops |> List.map encodeOp |> String.concat "; ")
        + "]")
    |> String.concat "\n"

// ---------------------------------------------------------------------------
//  The differential law.
// ---------------------------------------------------------------------------

/// Every way the oracle and production disagree on ONE lane set, across the sampled arrival
/// orders — empty when they agree everywhere. `oracleFp` is the footprint the oracle is handed;
/// it is the same projection as `fp` in every real run and a different one only in the go-red
/// case, which is what proves this comparison can fail. `theoremInstance` also asserts the
/// oracle's own arrival-order invariance — sound only for a witness whose footprint keeps the
/// domain's promise (the theorem's hypothesis), so the corpus pool, whose stand-in reducer is
/// an order-sensitive log, passes `false`.
let private disagreements
    (theoremInstance: bool)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (oracleFp: 'Op -> Footprint)
    (hashState: 'State -> string)
    (state0: 'State)
    (baseOp: 'Op)
    (lanes: 'Op list list)
    : string list =
    let orders = FoldConfluence.arrivalOrders (List.length lanes)

    let perOrder =
        orders
        |> List.choose (fun order ->
            let arranged = order |> List.map (fun i -> List.item i lanes)
            let p = productionFold w fp hashState state0 baseOp arranged
            let o = oracleFold w oracleFp hashState state0 arranged

            if p <> o then
                Some(sprintf "outcome differs under order %A:\n  production: %A\n  oracle:     %A" order p o)
            else
                let ps = productionScript w fp baseOp arranged
                let os = oracleScript w oracleFp arranged

                if ps <> os then
                    Some(
                        sprintf "merge script differs under order %A:\n  production: %A\n  oracle:     %A" order ps os
                    )
                else
                    None)

    // The theorem's instance: the oracle's outcomes over every sampled order are ONE outcome.
    let oracleOutcomes =
        orders
        |> List.map (fun order ->
            oracleFold w oracleFp hashState state0 (order |> List.map (fun i -> List.item i lanes)))
        |> List.distinct

    let invariance =
        if theoremInstance && List.length oracleOutcomes > 1 then
            [ sprintf "the oracle itself is not arrival-order-invariant here: %A" oracleOutcomes ]
        else
            []

    perOrder @ invariance

type private Verdict =
    { Failure: string option
      Folded: int
      Halted: int }

/// Run the differential over `iterations` generated lane sets; the first disagreement is shrunk
/// and reported with its seed, iteration and the lanes in the domain's own encoding.
let private differential
    (label: string)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (oracleFp: 'Op -> Footprint)
    (hashState: 'State -> string)
    (gen: LaneGen<'Op, 'State>)
    (laneCount: int)
    (seed: int)
    (iterations: int)
    : Verdict =
    let mutable rng = ConfRng.ofSeed seed
    let mutable folded = 0
    let mutable halted = 0
    let mutable failure: string option = None

    let diverges (ls: 'Op list list) =
        not (List.isEmpty (disagreements true w fp oracleFp hashState gen.State0 gen.BaseOp ls))

    for i in 0 .. iterations - 1 do
        if failure.IsNone then
            let lanes, r' = gen.Lanes laneCount rng
            rng <- r'

            match disagreements true w fp oracleFp hashState gen.State0 gen.BaseOp lanes with
            | [] ->
                match productionFold w fp hashState gen.State0 gen.BaseOp lanes with
                | LaneFolded _ -> folded <- folded + 1
                | LaneHalted _ -> halted <- halted + 1
                | LaneRejected _ -> ()
            | _ ->
                let small = FoldConfluence.shrinkLanes diverges lanes

                failure <-
                    Some(
                        sprintf
                            "%s: seed=%d iter=%d — the oracle and production DISAGREE; shrunk to\n%s\n%s"
                            label
                            seed
                            i
                            (renderLanes w.Encode small)
                            (disagreements true w fp oracleFp hashState gen.State0 gen.BaseOp small
                             |> List.head)
                    )

    { Failure = failure
      Folded = folded
      Halted = halted }

let private expectAgreement (label: string) (v: Verdict) =
    match v.Failure with
    | Some why -> failtest why
    | None ->
        // Both classes must have been exercised, or the agreement certifies one path only —
        // the same vacuity posture as the pack's sample-adequacy guard.
        Expect.isGreaterThan v.Folded 0 (sprintf "%s: no lane set folded (folded=%d halted=%d)" label v.Folded v.Halted)
        Expect.isGreaterThan v.Halted 0 (sprintf "%s: no lane set halted (folded=%d halted=%d)" label v.Folded v.Halted)

// ---------------------------------------------------------------------------
//  The wire corpus's ops/ fixtures as a third lane pool.
//
//  The corpus ops are UI-domain ops (InsertChild / RemoveNode / MoveNode / ReorderChildren /
//  EditNode / UpdateProp / UpdateState / UpdateStyle / ReplaceBinding / ReplaceRoot / Batch), and
//  Core has no reducer for them. What the fold needs of an op is its FOOTPRINT, so each fixture is
//  projected to one here — test-local, mirroring `Ops.footprint`'s clauses for the structural ops
//  (an insert writes its parent's structure and its subtree's content; a remove/move is the
//  unknown-source-parent case; a reorder reads its named children) and treating every
//  target-addressed property edit as a content write on its target. The reducer is a log — order
//  sensitive, deliberately: this pool compares oracle against production under ONE order at a
//  time, and an order-sensitive state makes the folded outcome a real comparison rather than a
//  constant.
// ---------------------------------------------------------------------------

type CorpusOp = { Name: string; Fp: Footprint }

let private corpusW: StreamWitness<CorpusOp, string, string> =
    { Apply = fun op st -> Ok(st + "/" + op.Name)
      Encode = fun op -> op.Name
      Decode = fun _ -> Error "ProofOracleTests: the corpus witness's decode is unused" }

let private corpusFp (op: CorpusOp) = op.Fp

let private unionFp (a: Footprint) (b: Footprint) : Footprint =
    { Reads = Set.union a.Reads b.Reads
      StructureWrites = Set.union a.StructureWrites b.StructureWrites
      ContentWrites = Set.union a.ContentWrites b.ContentWrites
      UnknownParentWrites = Set.union a.UnknownParentWrites b.UnknownParentWrites }

let private noAddr: Set<string> = Set.empty

let rec private idsIn (v: JVal) : string list =
    match v with
    | JObj fields ->
        fields
        |> List.collect (fun (k, v) ->
            match k, v with
            | "id", JStr s -> [ s ]
            | _ -> idsIn v)
    | JArr xs -> List.collect idsIn xs
    | _ -> []

let private strField (fields: (string * JVal) list) (k: string) : string option =
    fields
    |> List.tryPick (fun (n, v) ->
        match v with
        | JStr s when n = k -> Some s
        | _ -> None)

let private field (fields: (string * JVal) list) (k: string) : JVal option =
    fields |> List.tryPick (fun (n, v) -> if n = k then Some v else None)

/// The footprint of one corpus op, or the reason the projection could not read it.
let rec private corpusFootprint (v: JVal) : Result<Footprint, string> =
    match v with
    | JObj fields ->
        let one x = Set.singleton x

        match strField fields "$type" with
        | Some "InsertChild" ->
            match strField fields "parentId", field fields "child" with
            | Some p, Some child ->
                Ok
                    { Reads = one p
                      StructureWrites = one p
                      ContentWrites = Set.ofList (idsIn child)
                      UnknownParentWrites = noAddr }
            | _ -> Error "InsertChild without parentId/child"
        | Some "RemoveNode" ->
            match strField fields "target" with
            | Some t ->
                Ok
                    { Reads = one t
                      StructureWrites = noAddr
                      ContentWrites = one t
                      UnknownParentWrites = one t }
            | None -> Error "RemoveNode without target"
        | Some "MoveNode" ->
            match strField fields "target", strField fields "newParentId" with
            | Some t, Some np ->
                Ok
                    { Reads = Set.ofList [ t; np ]
                      StructureWrites = one np
                      ContentWrites = one t
                      UnknownParentWrites = one t }
            | _ -> Error "MoveNode without target/newParentId"
        | Some "ReorderChildren" ->
            match strField fields "parentId", field fields "newOrder" with
            | Some p, Some(JArr order) ->
                let named =
                    order
                    |> List.choose (function
                        | JStr s -> Some s
                        | _ -> None)

                Ok
                    { Reads = Set.ofList (p :: named)
                      StructureWrites = one p
                      ContentWrites = noAddr
                      UnknownParentWrites = noAddr }
            | _ -> Error "ReorderChildren without parentId/newOrder"
        | Some "ReplaceRoot" ->
            match field fields "node" with
            | Some node ->
                Ok
                    { Reads = noAddr
                      StructureWrites = noAddr
                      ContentWrites = Set.ofList (idsIn node)
                      UnknownParentWrites = noAddr }
            | None -> Error "ReplaceRoot without node"
        | Some "Batch" ->
            match field fields "ops" with
            | Some(JArr ops) ->
                ops
                |> List.map corpusFootprint
                |> List.fold
                    (fun acc r ->
                        match acc, r with
                        | Ok a, Ok b -> Ok(unionFp a b)
                        | Error e, _
                        | _, Error e -> Error e)
                    (Ok
                        { Reads = noAddr
                          StructureWrites = noAddr
                          ContentWrites = noAddr
                          UnknownParentWrites = noAddr })
            | _ -> Error "Batch without ops"
        | Some other ->
            // EditNode / UpdateProp / UpdateState / UpdateStyle / ReplaceBinding — a property
            // edit on the target node.
            match strField fields "target" with
            | Some t ->
                Ok
                    { Reads = one t
                      StructureWrites = noAddr
                      ContentWrites = one t
                      UnknownParentWrites = noAddr }
            | None -> Error(other + " without target")
        | None -> Error "no $type"
    | _ -> Error "not a JSON object"

/// Every ops/ fixture as a `CorpusOp`, or the reason one could not be read — an unreadable
/// fixture FAILS rather than being dropped, because a lane pool with silent holes certifies
/// less than it says.
let private loadCorpusOps (root: string) : Result<CorpusOp list, string> =
    Directory.GetFiles(Path.Combine(root, "ops"), "*.json")
    |> Array.sort
    |> Array.toList
    |> List.map (fun path ->
        let name = Path.GetFileNameWithoutExtension path

        match Json.parse (File.ReadAllText path) with
        | Error e -> Error(sprintf "%s: not JSON (%s)" name e)
        | Ok v ->
            match corpusFootprint v with
            | Ok fp -> Ok { Name = name; Fp = fp }
            | Error why -> Error(sprintf "%s: %s" name why))
    |> List.fold
        (fun acc r ->
            match acc, r with
            | Ok ops, Ok op -> Ok(ops @ [ op ])
            | Error e, _ -> Error e
            | _, Error e -> Error e)
        (Ok [])

// ---------------------------------------------------------------------------

[<Tests>]
let proofOracleTests =
    testList
        "Proofs.Oracle"
        [

          // ---- the reference witness (tree + skeleton ops) ----

          testCase "the oracle agrees with production over the reference witness at 3 lanes"
          <| fun _ ->
              differential "reference witness" treeW treeFootprint treeFootprint treeHash treeLaneGen 3 1000 120
              |> expectAgreement "reference witness, 3 lanes"

          testCase "the oracle agrees with production over the reference witness at 4 lanes"
          <| fun _ ->
              differential "reference witness" treeW treeFootprint treeFootprint treeHash treeLaneGen 4 2200 60
              |> expectAgreement "reference witness, 4 lanes"

          // ---- the second, non-tree domain (a Map state, its own footprint) ----

          testCase "the oracle agrees with production over the work-plan domain"
          <| fun _ ->
              differential "work-plan domain" planW planFootprint planFootprint planHash planLaneGen 3 4100 150
              |> expectAgreement "work-plan domain, 3 lanes"

          testCase "the oracle agrees with production over the work-plan domain at 5 lanes (sampled orders)"
          <| fun _ ->
              differential "work-plan domain" planW planFootprint planFootprint planHash planLaneGen 5 5100 40
              |> expectAgreement "work-plan domain, 5 lanes"

          // ---- the teeth: the comparison can fail, and shrinks what it reports ----

          testCase "an oracle handed a blind footprint DISAGREES with production, shrunk to two lanes"
          <| fun _ ->
              let blind (_: PlanOp) : Footprint =
                  { Reads = noAddr
                    StructureWrites = noAddr
                    ContentWrites = noAddr
                    UnknownParentWrites = noAddr }

              let v =
                  differential "blind oracle" planW planFootprint blind planHash planLaneGen 3 4100 150

              match v.Failure with
              | None -> failtest "a blind oracle never halts, so it must disagree with a production fold that does"
              | Some why ->
                  Expect.stringContains why "shrunk to" "the disagreement was shrunk"
                  Expect.stringContains why "outcome differs" "the disagreement names what differed"

                  let laneLines =
                      why.Split('\n')
                      |> Array.filter (fun l -> l.StartsWith "  lane ")
                      |> List.ofArray

                  Expect.equal (List.length laneLines) 2 (sprintf "two lanes survive the shrink — got:\n%s" why)

          // ---- the wire corpus's ops/ fixtures ----

          testCase "the oracle agrees with production over every pair and triple of corpus ops"
          <| fun _ ->
              match SiblingCorpus.resolve "ops" with
              | SiblingCorpus.SkippedByRequest why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let ops =
                      match loadCorpusOps root with
                      | Ok ops -> ops
                      | Error why -> failtestf "a corpus op could not be projected to a footprint: %s" why

                  Expect.isGreaterThan (List.length ops) 8 "the ops/ family carries a real pool"

                  let baseOp =
                      { Name = "base"
                        Fp = (List.head ops).Fp }

                  let n = List.length ops
                  let mutable folded = 0
                  let mutable halted = 0

                  let check (lanes: CorpusOp list list) =
                      match disagreements false corpusW corpusFp corpusFp id "" baseOp lanes with
                      | [] ->
                          match productionFold corpusW corpusFp id "" baseOp lanes with
                          | LaneFolded _ -> folded <- folded + 1
                          | LaneHalted _ -> halted <- halted + 1
                          | LaneRejected _ -> ()
                      | d :: _ -> failtestf "corpus lanes\n%s\n%s" (renderLanes corpusW.Encode lanes) d

                  for i in 0 .. n - 1 do
                      for j in i + 1 .. n - 1 do
                          check [ [ List.item i ops ]; [ List.item j ops ] ]

                          for k in j + 1 .. n - 1 do
                              check [ [ List.item i ops ]; [ List.item j ops ]; [ List.item k ops ] ]

                  // Two-op lanes as well: a lane whose ops interfere with each other is legal (a
                  // lane is one writer's sequence) and must not be reported as a cross-lane halt.
                  for i in 0 .. n - 2 do
                      check [ [ List.item i ops; List.item (i + 1) ops ]; [ List.item ((i + 2) % n) ops ] ]

                  Expect.isGreaterThan folded 0 "some corpus lane sets fold"
                  Expect.isGreaterThan halted 0 "some corpus lane sets halt"

          // ---- the pieces the comparison rests on ----

          testCase "the model's independence agrees with Ops.independent over the reference footprints"
          <| fun _ ->
              // The model's `independent` is `Ops.independent` clause for clause; over every pair
              // of ops the reference generator produces, the two booleans agree.
              let mutable r = ConfRng.ofSeed 77
              let mutable pairs = 0

              for _ in 1..40 do
                  let lanes, r' = treeLaneGen.Lanes 2 r
                  r <- r'

                  for a in List.concat lanes do
                      for b in List.concat lanes do
                          pairs <- pairs + 1
                          let fa = treeFootprint a
                          let fb = treeFootprint b

                          Expect.equal
                              (DagFold.independent (toModelFootprint fa) (toModelFootprint fb))
                              (Ops.independent fa fb)
                              (sprintf "independence of %s / %s" (treeW.Encode a) (treeW.Encode b))

              Expect.isGreaterThan pairs 0 "pairs were compared"

          testCase "the model's conflicts are Dag.conflicts up to the canonical rendering"
          <| fun _ ->
              let a = [ AddItem("n1", "title-0"); Retitle("p1", "t") ]
              let b = [ AddItem("n1", "title-1"); AddDep("p2", "p1") ]

              let production =
                  FoldConfluence.canonicalConflictReport encPlanOp (Dag.conflicts planFootprint a b)

              let model =
                  FoldConfluence.canonicalConflictReport
                      encPlanOp
                      (DagFold.conflicts (planFootprint >> toModelFootprint) a b
                       |> List.map ofModelConflict)

              Expect.isFalse (production = "") "the two deltas genuinely conflict"
              Expect.equal model production "the model enumerates the same interferences" ]
