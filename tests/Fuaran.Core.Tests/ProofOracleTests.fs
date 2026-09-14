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
open Fuaran.Core.Tests.Reference
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

/// The real DAG `FoldConfluence.foldOnce` builds for a lane set — each lane chained onto one base
/// node under its own actor — with the base id and the lane heads it produced. One definition,
/// because three places here need the same construction and a second copy of it would be a second
/// thing to keep in step with `foldOnce`.
let private productionDag
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (baseOp: 'Op)
    (lanes: 'Op list list)
    : string * string list * Dag.T<'Op> =
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

    baseId, heads, dag

/// Production's merge SCRIPT (or canonical halt report) through that DAG.
let private productionScript
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (baseOp: 'Op)
    (lanes: 'Op list list)
    : Result<'Op list, string> =
    let baseId, heads, dag = productionDag w baseOp lanes

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
//  The theorem's HYPOTHESIS, measured on the domain it names (Phase 132).
//
//  `fold_confluence`'s one domain hypothesis is `independence_diamond`: ops the model's
//  `independent` declares disjoint and which BOTH APPLY at a state each apply after the other
//  and reach the same state. Phase 131 assumed the stronger `independence_sound` — commuting at
//  every state, REJECTIONS INCLUDED — which the reference algebra cannot keep (the last case
//  below is the witness). A hypothesis stated and never measured is a theorem about a domain
//  nobody has, so this pool measures it on the reference tree witness itself.
// ---------------------------------------------------------------------------

/// Every way a witness breaks the diamond over one pool of ops and one pool of states, and the
/// number of (pair, state) triples at which the PREMISE was actually met — both orders asked
/// for. `oracleFp` is the footprint the check runs under; handing it a blind one is what proves
/// this measurement can fail, exactly as the differential's go-red case does.
let private diamondBreaks
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (oracleFp: 'Op -> Footprint)
    (ops: 'Op list)
    (states: 'State list)
    : string list * int =
    let apply = modelApply w
    let mutable met = 0
    let mutable breaks = []

    for a in ops do
        for b in ops do
            if DagFold.independent (toModelFootprint (oracleFp a)) (toModelFootprint (oracleFp b)) then
                for s in states do
                    match apply a s, apply b s with
                    | DagFold.Ok sa, DagFold.Ok sb ->
                        // The premise is met here, and only here: both ops apply at `s`.
                        met <- met + 1

                        let broke (why: string) =
                            breaks <-
                                breaks
                                @ [ sprintf
                                        "independent %s / %s both apply at a state, but %s"
                                        (w.Encode a)
                                        (w.Encode b)
                                        why ]

                        match apply b sa, apply a sb with
                        | DagFold.Ok t1, DagFold.Ok t2 when t1 = t2 -> ()
                        | DagFold.Ok _, DagFold.Ok _ -> broke "the two orders reach different states"
                        | DagFold.Error _, _ -> broke "the second does not apply after the first"
                        | _, DagFold.Error _ -> broke "the first does not apply after the second"
                    | _ -> ()

    breaks, met

/// The op and state pools Phase 80's construction yields for the reference witness: applyable
/// skeleton-op scripts threaded from the base tree (`treeLaneGen` — the public form of that
/// construction, shared with the differential above), pooled into ops, and every state a prefix
/// of that pool reaches. The states matter: the hypothesis quantifies over ALL of them, and a
/// check at the base tree alone would measure one instance of a `forall s`.
let private treeDiamondSample
    (oracleFp: SkeletonOp<RNode, string> -> Footprint)
    (seed: int)
    (trials: int)
    : string list * int =
    let mutable r = ConfRng.ofSeed seed
    let mutable breaks = []
    let mutable met = 0

    for _ in 1..trials do
        let lanes, r' = treeLaneGen.Lanes 3 r
        r <- r'
        let ops = List.concat lanes

        let states =
            ops
            |> List.fold
                (fun (acc, cur) op ->
                    match treeW.Apply op cur with
                    | Ok t -> (acc @ [ t ]), t
                    | Error _ -> acc, cur)
                ([ treeBase ], treeBase)
            |> fst

        let b, m = diamondBreaks treeW oracleFp ops states
        breaks <- breaks @ b
        met <- met + m

    breaks, met

// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
//  Phase 135 — the DECODE model as a second oracle.
//
//  `proofs/WireDecode.fst` models the `Fuaran.Core.Decode` combinators clause for clause, with
//  decoder totality proved (every combinator reaches exactly one outcome on every input, and
//  which one is characterised structurally), the reference vocabulary's round trip proved, and
//  the Phase 102 read policy proved to agree with the strict reader everywhere the policy does
//  not fire. `proofs/oracle/WireDecode.fs` is that model extracted by F*'s own code generator.
//
//  What runs here is the model BESIDE `Wire.Decode`, over the wire corpus's `nodes/` and `ops/`
//  fixtures, over a generated `JVal` sample, and — for the policy — over generated documents
//  that carry the `null` token in every position. Three things are compared at once, because
//  they are rendered into one string: the outcome CLASS, the decoded VALUE, and the error
//  MESSAGE. A model that agreed on accept-vs-refuse alone would not notice a decoder that named
//  the wrong expectation, which is most of what these combinators are for.
//
//  The model is parametric in the two numeric carriers (no combinator in `Decode` looks inside a
//  number, it only moves one), so the bridge instantiates it at `<int, float>` and hands
//  `as_float` the widening `Decode.asFloat` performs as `float i`.
// ---------------------------------------------------------------------------

type private MJVal = WireDecode.jval<int, float>
type private MJValN = WireDecode.jvaln<int, float>

let private inv = System.Globalization.CultureInfo.InvariantCulture

let rec private toModel (v: JVal) : MJVal =
    match v with
    | JStr s -> WireDecode.JStr s
    | JInt i -> WireDecode.JInt i
    | JBool b -> WireDecode.JBool b
    | JFloat f -> WireDecode.JFloat f
    | JArr xs -> WireDecode.JArr(xs |> List.map toModel)
    | JObj fields -> WireDecode.JObj(fields |> List.map (fun (k, v) -> k, toModel v))

let rec private ofModel (v: MJVal) : JVal =
    match v with
    | WireDecode.JStr s -> JStr s
    | WireDecode.JInt i -> JInt i
    | WireDecode.JBool b -> JBool b
    | WireDecode.JFloat f -> JFloat f
    | WireDecode.JArr xs -> JArr(xs |> List.map ofModel)
    | WireDecode.JObj fields -> JObj(fields |> List.map (fun (k, v) -> k, ofModel v))

/// The BLIND bridge — the go-red instrument, and the decode family's counterpart to the blind
/// footprint the fold family hands its oracle. It reads every wire integer as a float, which is
/// exactly the numeric normalisation `JVal`'s doc warns a reader not to assume away, so the
/// oracle is asked about a different value from the one production was asked about and the
/// comparison must lose. A green report elsewhere is therefore known to be a comparison that CAN
/// fail rather than one that cannot.
let rec private toModelBlind (v: JVal) : MJVal =
    match v with
    | JInt i -> WireDecode.JFloat(float i)
    | JStr s -> WireDecode.JStr s
    | JBool b -> WireDecode.JBool b
    | JFloat f -> WireDecode.JFloat f
    | JArr xs -> WireDecode.JArr(xs |> List.map toModelBlind)
    | JObj fields -> WireDecode.JObj(fields |> List.map (fun (k, v) -> k, toModelBlind v))

/// `Decode.kindName`, which is private to that module — mirrored here because the reference
/// decoder below has to name a wrong shape the same way the shipped combinators do, and a
/// differential whose two sides spell a message differently measures the spelling.
let private kindWord (v: JVal) : string =
    match v with
    | JStr _ -> "string"
    | JInt _ -> "int"
    | JBool _ -> "bool"
    | JFloat _ -> "float"
    | JArr _ -> "array"
    | JObj _ -> "object"

// ---- one probe of the decode surface, both sides rendered the same way ----

type private Probe =
    { Name: string
      Production: string
      Oracle: string }

let private resR (render: 'a -> string) (r: Result<'a, string>) : string =
    match r with
    | Ok v -> "Ok " + render v
    | Error m -> "Error " + m

let private resO (render: 'a -> string) (o: WireDecode.outcome<'a>) : string =
    match o with
    | WireDecode.Ok v -> "Ok " + render v
    | WireDecode.Error m -> "Error " + m

let private asStr (s: string) = s
let private asFlt (f: float) = f.ToString("R", inv)
let private asStrs (xs: string list) = String.concat "" xs
let private asJson (v: JVal) = Json.render v
let private asModelJson (v: MJVal) = Json.render (ofModel v)

/// Every combinator in `Decode`, asked the same question of production and of the model.
/// `bridge` is the projection the oracle is handed — the identity in every real run and the
/// blind one only in the go-red case.
let private decodeProbes (bridge: JVal -> MJVal) (el: JVal) : Probe list =
    let m = bridge el

    let p name prod orac =
        { Name = name
          Production = prod
          Oracle = orac }

    [ p "asString" (resR asStr (Decode.asString el)) (resO asStr (WireDecode.as_string m))
      p "asInt" (resR string (Decode.asInt el)) (resO string (WireDecode.as_int m))
      p "asBool" (resR string (Decode.asBool el)) (resO string (WireDecode.as_bool m))
      p "asFloat" (resR asFlt (Decode.asFloat el)) (resO asFlt (WireDecode.as_float (fun (i: int) -> float i) m))
      p "kindOf" (resR asStr (Decode.kindOf el)) (resO asStr (WireDecode.kind_of m))
      p "getProp kind" (resR asJson (Decode.getProp "kind" el)) (resO asModelJson (WireDecode.get_prop "kind" m))
      p "getProp $type" (resR asJson (Decode.getProp "$type" el)) (resO asModelJson (WireDecode.get_prop "$type" m))
      p "getProp id" (resR asJson (Decode.getProp "id" el)) (resO asModelJson (WireDecode.get_prop "id" m))
      p
          "getProp absent"
          (resR asJson (Decode.getProp "no-such-member" el))
          (resO asModelJson (WireDecode.get_prop "no-such-member" m))
      p "strField id" (resR asStr (Decode.strField "id" el)) (resO asStr (WireDecode.str_field "id" m))
      p "intField n" (resR string (Decode.intField "n" el)) (resO string (WireDecode.int_field "n" m))
      p
          "mapList asString"
          (resR asStrs (Decode.mapList Decode.asString el))
          (resO asStrs (WireDecode.map_list (fun (x: MJVal) -> WireDecode.as_string x) m)) ]

/// Every value in a document, root first — the combinators are asked about the scalars and the
/// nested objects too, not only the root, since that is where their refusal arms live.
let rec private everyValue (v: JVal) : JVal list =
    match v with
    | JArr xs -> v :: (xs |> List.collect everyValue)
    | JObj fields -> v :: (fields |> List.collect (fun (_, x) -> everyValue x))
    | _ -> [ v ]

type private Tally =
    { Disagreements: string list
      Accepted: int
      Refused: int }

let private emptyTally =
    { Disagreements = []
      Accepted = 0
      Refused = 0 }

/// Run every probe over every value of `el`, folding the disagreements and the two outcome
/// classes into `tally`.
let private runProbes (bridge: JVal -> MJVal) (label: string) (el: JVal) (tally: Tally) : Tally =
    everyValue el
    |> List.fold
        (fun acc v ->
            decodeProbes bridge v
            |> List.fold
                (fun (a: Tally) probe ->
                    let a =
                        if probe.Production.StartsWith "Ok" then
                            { a with Accepted = a.Accepted + 1 }
                        else
                            { a with Refused = a.Refused + 1 }

                    if probe.Production <> probe.Oracle then
                        { a with
                            Disagreements =
                                a.Disagreements
                                @ [ sprintf
                                        "%s: %s on %s\n  production: %s\n  oracle:     %s"
                                        label
                                        probe.Name
                                        (Json.render v)
                                        probe.Production
                                        probe.Oracle ] }
                    else
                        a)
                acc)
        tally

let private expectProbeAgreement (label: string) (t: Tally) =
    match t.Disagreements with
    | d :: _ -> failtestf "%s: the decode oracle and Wire.Decode DISAGREE\n%s" label d
    | [] ->
        // Both classes must have been reached, or the agreement certifies one arm only — the
        // same vacuity posture the fold family's `expectAgreement` takes.
        Expect.isGreaterThan
            t.Accepted
            0
            (sprintf "%s: no probe was ACCEPTED (accepted=%d refused=%d)" label t.Accepted t.Refused)

        Expect.isGreaterThan
            t.Refused
            0
            (sprintf "%s: no probe was REFUSED (accepted=%d refused=%d)" label t.Accepted t.Refused)

// ---- the reference vocabulary, on the production side ----

/// The vocabulary `WireDecode.rnode` models: one case per combinator the model exercises.
type RefNode =
    | RefText of string
    | RefFlag of bool
    | RefTags of string list
    | RefGroup of string * RefNode list

/// F#: `Json.kindObj` — the model's `encode`, on the production side.
let rec private encodeRef (n: RefNode) : JVal =
    match n with
    | RefText s -> Json.kindObj "text" [ "value", JStr s ]
    | RefFlag b -> Json.kindObj "flag" [ "on", JBool b ]
    | RefTags ts -> Json.kindObj "tags" [ "tags", JArr(ts |> List.map JStr) ]
    | RefGroup(id, items) -> Json.kindObj "group" [ "id", JStr id; "items", JArr(items |> List.map encodeRef) ]

/// The kind-dispatch node decoder a domain writes from the shipped combinators — the production
/// side of the model's `decode_node`, written against `Wire.Decode` and nothing else.
let rec private decodeRef (el: JVal) : Result<RefNode, string> =
    match Decode.kindOf el with
    | Error m -> Error m
    | Ok tag ->
        if tag = "text" then
            Decode.strField "value" el |> Result.map RefText
        elif tag = "flag" then
            Decode.getProp "on" el |> Result.bind Decode.asBool |> Result.map RefFlag
        elif tag = "tags" then
            Decode.getProp "tags" el
            |> Result.bind (Decode.mapList Decode.asString)
            |> Result.map RefTags
        elif tag = "group" then
            match Decode.strField "id" el with
            | Error m -> Error m
            | Ok id ->
                match Decode.getProp "items" el with
                | Error m -> Error m
                | Ok(JArr ys) ->
                    let rec go acc rest =
                        match rest with
                        | [] -> Ok(List.rev acc)
                        | x :: t ->
                            match decodeRef x with
                            | Ok n -> go (n :: acc) t
                            | Error m -> Error m

                    go [] ys |> Result.map (fun ns -> RefGroup(id, ns))
                | Ok other -> Error("expected array, got " + kindWord other)
        else
            Error("unknown kind: " + tag)

let rec private renderRef (n: RefNode) : string =
    match n with
    | RefText s -> "text(" + s + ")"
    | RefFlag b -> "flag(" + string b + ")"
    | RefTags ts -> "tags[" + String.concat ";" ts + "]"
    | RefGroup(id, items) -> "group(" + id + ")[" + (items |> List.map renderRef |> String.concat ";") + "]"

let rec private renderRnode (n: WireDecode.rnode) : string =
    match n with
    | WireDecode.RText s -> "text(" + s + ")"
    | WireDecode.RFlag b -> "flag(" + string b + ")"
    | WireDecode.RTags ts -> "tags[" + String.concat ";" ts + "]"
    | WireDecode.RGroup(id, items) ->
        "group("
        + id
        + ")["
        + (items |> List.map renderRnode |> String.concat ";")
        + "]"

/// Production's node decoder and the model's, on the same document.
let private refDiff (el: JVal) : string option =
    let production = resR renderRef (decodeRef el)
    let oracle = resO renderRnode (WireDecode.decode_node (toModel el))

    if production <> oracle then
        Some(
            sprintf
                "the node decoders disagree on %s\n  production: %s\n  oracle:     %s"
                (Json.render el)
                production
                oracle
        )
    else
        None

// ---- generators ----

let rec private genJ (depth: int) (r: ConfRng.T) : JVal * ConfRng.T =
    let pick, r1 = ConfRng.intBelow (if depth >= 2 then 4 else 6) r

    match pick with
    | 0 ->
        let n, r2 = ConfRng.intBelow 8 r1
        JStr("s" + string n), r2
    | 1 ->
        let n, r2 = ConfRng.intBelow 1000 r1
        JInt(n - 500), r2
    | 2 ->
        let n, r2 = ConfRng.intBelow 2 r1
        JBool(n = 0), r2
    | 3 ->
        let n, r2 = ConfRng.intBelow 1000 r1
        JFloat(float n + 0.25), r2
    | 4 ->
        let count, r2 = ConfRng.intBelow 4 r1

        let items, r3 =
            List.fold
                (fun (acc, rr) _ ->
                    let v, rr' = genJ (depth + 1) rr
                    acc @ [ v ], rr')
                ([], r2)
                [ 1..count ]

        JArr items, r3
    | _ ->
        let count, r2 = ConfRng.intBelow 4 r1

        let fields, r3 =
            List.fold
                (fun (acc, rr) _ ->
                    let k, rr1 = ConfRng.intBelow 6 rr
                    let v, rr2 = genJ (depth + 1) rr1
                    // The key alphabet deliberately includes the names the probes and the
                    // reference decoder ask for, so the hit arms are reached as often as the
                    // miss arms.
                    let name = [| "kind"; "value"; "on"; "tags"; "id"; "items" |].[k]
                    acc @ [ name, v ], rr2)
                ([], r2)
                [ 1..count ]

        JObj fields, r3

let rec private genRef (depth: int) (r: ConfRng.T) : RefNode * ConfRng.T =
    let pick, r1 = ConfRng.intBelow (if depth >= 2 then 3 else 4) r

    match pick with
    | 0 ->
        let n, r2 = ConfRng.intBelow 20 r1
        RefText("t" + string n), r2
    | 1 ->
        let n, r2 = ConfRng.intBelow 2 r1
        RefFlag(n = 0), r2
    | 2 ->
        let count, r2 = ConfRng.intBelow 4 r1

        let ts, r3 =
            List.fold
                (fun (acc, rr) _ ->
                    let n, rr' = ConfRng.intBelow 20 rr
                    acc @ [ "g" + string n ], rr')
                ([], r2)
                [ 1..count ]

        RefTags ts, r3
    | _ ->
        let count, r2 = ConfRng.intBelow 3 r1
        let idn, r3 = ConfRng.intBelow 20 r2

        let items, r4 =
            List.fold
                (fun (acc, rr) _ ->
                    let n, rr' = genRef (depth + 1) rr
                    acc @ [ n ], rr')
                ([], r3)
                [ 1..count ]

        RefGroup("i" + string idn, items), r4

// ---- the Phase 102 read policy ----

/// A document carrying the `null` token, rendered as the wire text production's two policies
/// actually read. Floats keep a fractional part: an integral `JFloat` renders without a point
/// and re-parses as `JInt` (the documented numeric normalisation), which would make the
/// comparison measure that normalisation rather than the policy.
let rec private renderN (d: MJValN) : string =
    match d with
    | WireDecode.NNull -> "null"
    | WireDecode.NStr s -> "\"" + Json.escape s + "\""
    | WireDecode.NInt i -> string i
    | WireDecode.NBool b -> (if b then "true" else "false")
    | WireDecode.NFloat f -> f.ToString("R", inv)
    | WireDecode.NArr xs -> "[" + (xs |> List.map renderN |> String.concat ",") + "]"
    | WireDecode.NObj fields ->
        "{"
        + (fields
           |> List.map (fun (k, v) -> "\"" + Json.escape k + "\":" + renderN v)
           |> String.concat ",")
        + "}"

let rec private genN (depth: int) (r: ConfRng.T) : MJValN * ConfRng.T =
    let pick, r1 = ConfRng.intBelow (if depth >= 2 then 5 else 7) r

    match pick with
    | 0 ->
        let n, r2 = ConfRng.intBelow 8 r1
        WireDecode.NStr("s" + string n), r2
    | 1 ->
        let n, r2 = ConfRng.intBelow 1000 r1
        WireDecode.NInt(n - 500), r2
    | 2 ->
        let n, r2 = ConfRng.intBelow 2 r1
        WireDecode.NBool(n = 0), r2
    | 3 ->
        let n, r2 = ConfRng.intBelow 1000 r1
        WireDecode.NFloat(float n + 0.25), r2
    | 4 -> (WireDecode.NNull: MJValN), r1
    | 5 ->
        let count, r2 = ConfRng.intBelow 4 r1

        let items, r3 =
            List.fold
                (fun (acc, rr) _ ->
                    let v, rr' = genN (depth + 1) rr
                    acc @ [ v ], rr')
                ([], r2)
                [ 1..count ]

        WireDecode.NArr items, r3
    | _ ->
        let count, r2 = ConfRng.intBelow 4 r1

        let fields, r3 =
            List.fold
                (fun (acc, rr) _ ->
                    let k, rr1 = ConfRng.intBelow 4 rr
                    let v, rr2 = genN (depth + 1) rr1
                    acc @ [ "k" + string k, v ], rr2)
                ([], r2)
                [ 1..count ]

        WireDecode.NObj fields, r3

type private PolicyTally =
    {
        Diffs: string list
        /// Documents the strict reader accepted.
        StrictOk: int
        /// Documents the strict reader refused.
        StrictErr: int
        /// Documents the POLICY fired on: strict refuses, tolerant accepts. Without these the
        /// tolerant leg would be a second run of the strict one.
        Fired: int
    }

/// The model's reader beside `Json.parseDetailedWithPolicy`, under both policies, on the same
/// document — compared on the outcome class, the decoded value AND the message, the last of
/// which is the half that distinguishes the tolerant policy's "no absence to erase it to"
/// refusal from the strict policy's blanket one.
let private policyProbe (d: MJValN) (t: PolicyTally) : PolicyTally =
    let text = renderN d

    let side (policy: NullPolicy) (mp: WireDecode.null_policy) =
        let production =
            match Json.parseDetailedWithPolicy policy Json.defaultMaxDepth text with
            | Ok v -> "Ok " + Json.render v
            | Error e -> "Error " + e.Message

        let oracle =
            match WireDecode.read mp d with
            | WireDecode.Ok v -> "Ok " + Json.render (ofModel v)
            | WireDecode.Error m -> "Error " + m

        production, oracle

    let strictP, strictO = side RejectNull WireDecode.RejectNull
    let lenientP, lenientO = side EraseMemberNull WireDecode.EraseMemberNull

    let diffs =
        [ if strictP <> strictO then
              sprintf "strict read of %s\n  production: %s\n  oracle:     %s" text strictP strictO
          if lenientP <> lenientO then
              sprintf "tolerant read of %s\n  production: %s\n  oracle:     %s" text lenientP lenientO ]

    { Diffs = t.Diffs @ diffs
      StrictOk = t.StrictOk + (if strictP.StartsWith "Ok" then 1 else 0)
      StrictErr = t.StrictErr + (if strictP.StartsWith "Ok" then 0 else 1)
      Fired =
        t.Fired
        + (if not (strictP.StartsWith "Ok") && lenientP.StartsWith "Ok" then
               1
           else
               0) }

// ---------------------------------------------------------------------------
//  Phase 134 — the DAG BENEATH the fold, differentially.
//
//  Sections 0–10 of the model start where the lane deltas are known, so until now the oracle was
//  fed them directly while production rebuilt them from a content-addressed DAG, and the
//  difference between those two starting points was the widest unproved gap in the claims ladder.
//  Section 11 closes it on the model side (`between_chain`, `reconcile_many_dag_eq`,
//  `fold_confluence_dag`); what runs here is the other half — production's OWN
//  `Dag.ancestorsOf` / `topoOrder` / `Dag.between` / `Dag.betweenOps`, on the DAG `foldOnce`
//  actually builds, against the model's recovery on the SAME nodes.
//
//  Nothing is recomputed across the bridge: the ids the model walks are the content hashes
//  production minted, so a disagreement can only be about the RECOVERY. The one step the model
//  does not prove — that Kahn's frontier drain and the reverse of the parent walk are the same
//  order on a spine — is exactly what these cases measure.
// ---------------------------------------------------------------------------

/// A production `Dag.T` as the model reads it: the same nodes, the same content ids, the same
/// parent lists.
let private toModelDag (dag: Dag.T<'Op>) : DagFold.dag<'Op> =
    { DagFold.nodes =
        dag.Nodes
        |> Map.toList
        |> List.map (fun (_, n) ->
            { DagFold.nid = n.Id
              DagFold.nparents = n.Parents
              DagFold.nop = n.Op }) }

/// The faithful bridge — what every real run uses.
let private unperturbed (_: string) (_: string list) (dag: Dag.T<'Op>) : DagFold.dag<'Op> = toModelDag dag

/// The go-red instrument, and the fold family's blind-footprint counterpart for this layer: every
/// lane's FIRST node is re-parented from the base onto the PREVIOUS lane's head, so the model
/// walks a spine the DAG does not have. The ids are untouched, so every lookup still succeeds and
/// the only thing that has moved is the shape the recovery walks — which is the thing under test.
let private reparented (baseId: string) (heads: string list) (dag: Dag.T<'Op>) : DagFold.dag<'Op> =
    let previousHead (actor: Actor) =
        match actor with
        | Human name when name.StartsWith "lane-" ->
            match System.Int32.TryParse(name.Substring 5) with
            | true, i when i > 0 && i - 1 < List.length heads -> Some(List.item (i - 1) heads)
            | _ -> None
        | _ -> None

    { DagFold.nodes =
        dag.Nodes
        |> Map.toList
        |> List.map (fun (_, n) ->
            let parents =
                if n.Parents = [ baseId ] then
                    match previousHead n.Actor with
                    | Some p when p <> baseId -> [ p ]
                    | _ -> n.Parents
                else
                    n.Parents

            { DagFold.nid = n.Id
              DagFold.nparents = parents
              DagFold.nop = n.Op }) }

/// Every way production's per-lane delta recovery and the model's disagree on ONE lane set, plus
/// how many ops were actually recovered — a run that recovered nothing has compared two empty
/// lists and measured nothing at all.
let private deltaDisagreements
    (bridge: string -> string list -> Dag.T<'Op> -> DagFold.dag<'Op>)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (baseOp: 'Op)
    (lanes: 'Op list list)
    : string list * int =
    let baseId, heads, dag = productionDag w baseOp lanes
    let model = bridge baseId heads dag
    // One walk step per node in the DAG, which is the bound production's own work-list runs to.
    let fuel = model.nodes

    let diffs =
        heads
        |> List.mapi (fun i head ->
            let p = Dag.betweenOps dag baseId head
            let o = DagFold.between_ops model fuel baseId head

            if p <> o then
                Some(
                    sprintf
                        "lane %d: the recovered delta differs\n  production: [%s]\n  model:      [%s]"
                        i
                        (p |> List.map w.Encode |> String.concat "; ")
                        (o |> List.map w.Encode |> String.concat "; ")
                )
            else
                None)
        |> List.choose id

    diffs, (heads |> List.sumBy (fun h -> List.length (Dag.betweenOps dag baseId h)))

/// The oracle's fold FROM THE DAG — `fold_once_dag`, the Phase 134 entry point beside
/// `fold_once` — rendered exactly as `FoldConfluence.foldOnce` renders production's. Where
/// `oracleFold` above is handed the lane deltas, this one is handed the DAG and recovers them.
let private oracleFoldFromDag
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (hashState: 'State -> string)
    (state0: 'State)
    (baseOp: 'Op)
    (lanes: 'Op list list)
    : LaneFoldOutcome =
    let baseId, heads, dag = productionDag w baseOp lanes
    let model = toModelDag dag

    match DagFold.fold_once_dag (modelApply w) (fp >> toModelFootprint) model model.nodes baseId state0 heads with
    | DagFold.LaneFolded s -> LaneFolded(hashState s)
    | DagFold.LaneHalted cs ->
        LaneHalted(FoldConfluence.canonicalConflictReport w.Encode (cs |> List.map ofModelConflict))
    | DagFold.LaneRejected r -> LaneRejected(sprintf "%A" r)

type private DeltaTally =
    {
        Failure: string option
        /// Ops recovered across every lane of every trial — the vacuity guard.
        Recovered: int
        /// Lane sets carrying a lane of two or more ops: a one-node chain exercises no walk.
        Chains: int
    }

/// Run the delta differential over `iterations` generated lane sets from one pool.
let private deltaDifferential
    (label: string)
    (bridge: string -> string list -> Dag.T<'Op> -> DagFold.dag<'Op>)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (gen: LaneGen<'Op, 'State>)
    (laneCount: int)
    (seed: int)
    (iterations: int)
    : DeltaTally =
    let mutable rng = ConfRng.ofSeed seed
    let mutable failure: string option = None
    let mutable recovered = 0
    let mutable chains = 0

    for i in 0 .. iterations - 1 do
        let lanes, r' = gen.Lanes laneCount rng
        rng <- r'

        if lanes |> List.exists (fun l -> List.length l > 1) then
            chains <- chains + 1

        let diffs, got = deltaDisagreements bridge w gen.BaseOp lanes
        recovered <- recovered + got

        match diffs, failure with
        | d :: _, None ->
            failure <- Some(sprintf "%s: seed=%d iter=%d\n%s\n%s" label seed i (renderLanes w.Encode lanes) d)
        | _ -> ()

    { Failure = failure
      Recovered = recovered
      Chains = chains }

let private expectDeltaAgreement (label: string) (t: DeltaTally) =
    match t.Failure with
    | Some why -> failtest why
    | None ->
        Expect.isGreaterThan t.Recovered 0 (sprintf "%s: no ops were recovered at all (recovered=%d)" label t.Recovered)

        Expect.isGreaterThan
            t.Chains
            0
            (sprintf "%s: no trial carried a lane longer than one op, so no chain was walked" label)
//  Phase 133 — the TREE ALGEBRA as a third oracle.
//
//  `proofs/TreeOps.fst` models `Ops.apply` and `Ops.footprint` over the tree as the
//  `NodeWitness` shows it — an id, a kind tag and an ordered child list, and nothing else — and
//  proves the fold theorem's one domain hypothesis for it: footprint-independent operations that
//  both apply at a well-formed tree each apply after the other and reach the same tree.
//  `proofs/Skeleton.fst` composes that with Phase 131's fold theorem.
//  `proofs/oracle/TreeOps.fs` and `oracle/Skeleton.fs` are those models extracted by F*'s own
//  code generator.
//
//  What runs here is the model BESIDE `Ops.apply` / `Ops.footprint`, over the op pool Phase 80's
//  generator produces AND over every state a prefix of that pool reaches — the generator keeps
//  only ACCEPTED ops, so without the states (and without the hand-written refusals below) the
//  rejection arms would be sampled only by accident. Three things are compared per (op, state):
//    1. the FOOTPRINT, as four address sets — the model reads sets as lists, so both sides are
//       compared deduplicated and sorted, which is the reading `proofs/README.md` records;
//    2. the VERDICT, accepted or rejected;
//    3. an accepted RESULT through `Tree.encodeHash` — production's own function, run on both
//       sides through a `NodeWitness` for each tree type, over the per-node content the witness
//       exposes (id and kind tag). Hashing the reference node's value, hole or effect class would
//       compare fields the model does not model, and agreeing about them would mean nothing.
//    4. a rejection by CLASS, which is what the model claims. The envelope PAYLOADS carry id
//       lists (`UnknownNode`'s `addressable`, `ReorderMismatch`'s two orders) and are outside it.
// ---------------------------------------------------------------------------

/// The reference node as the tree model reads it — the three accessors, and no more.
let rec private toModelTree (n: RNode) : TreeOps.tree =
    TreeOps.TNode(n.Id, n.Kind, n.Children |> List.map toModelTree)

/// The BLIND bridge — the go-red instrument, and the tree family's counterpart to the fold
/// family's blind footprint. It erases every kind tag, so the model is asked about a different
/// tree from the one production was asked about and the result comparison must lose. It leaves
/// every id alone, so the VERDICTS still agree: what fails is the hash, which is the half that
/// would otherwise be the least exercised.
let rec private toModelTreeBlind (n: RNode) : TreeOps.tree =
    TreeOps.TNode(n.Id, "", n.Children |> List.map toModelTreeBlind)

let private mTid (t: TreeOps.tree) =
    match t with
    | TreeOps.TNode(i, _, _) -> i

let private mKind (t: TreeOps.tree) =
    match t with
    | TreeOps.TNode(_, k, _) -> k

let private mKids (t: TreeOps.tree) =
    match t with
    | TreeOps.TNode(_, _, cs) -> cs

/// A `NodeWitness` over the MODEL's tree, so the comparison runs production's own
/// `Tree.encodeHash` on both sides rather than a re-implementation of it beside the model.
let private modelTreeW: NodeWitness<TreeOps.tree, string> =
    { Id = mTid
      KindTag = mKind
      Children = mKids
      ReplaceChildren = fun t cs -> TreeOps.TNode(mTid t, mKind t, cs) }

let rec private toModelOpWith (bridge: RNode -> TreeOps.tree) (op: SkeletonOp<RNode, string>) : TreeOps.op =
    match op with
    | InsertChild(p, node) -> TreeOps.InsertChild(p, bridge node)
    | RemoveNode t -> TreeOps.RemoveNode t
    | MoveNode(t, np) -> TreeOps.MoveNode(t, np)
    | ReorderChildren(p, order) -> TreeOps.ReorderChildren(p, order)
    | Batch inner -> TreeOps.Batch(inner |> List.map (toModelOpWith bridge))

let private encWitnessNode (nodeId: string) (kindTag: string) = nodeId + "|" + kindTag

let private prodTreeHash (t: RNode) =
    Tree.encodeHash nodew (fun n -> encWitnessNode n.Id n.Kind) t

let private modelTreeHash (t: TreeOps.tree) =
    Tree.encodeHash modelTreeW (fun n -> encWitnessNode (mTid n) (mKind n)) t

let private prodRejClass (r: Rejection<string>) =
    match r with
    | UnknownNode _ -> "UnknownNode"
    | DuplicateId _ -> "DuplicateId"
    | CannotRemoveRoot -> "CannotRemoveRoot"
    | WouldNestUnderSelf _ -> "WouldNestUnderSelf"
    | NotAContainer _ -> "NotAContainer"
    | ReorderMismatch _ -> "ReorderMismatch"
    | Rejected _ -> "Rejected"

let private modelRejClass (r: TreeOps.rejection) =
    match r with
    | TreeOps.UnknownNode _ -> "UnknownNode"
    | TreeOps.DuplicateId _ -> "DuplicateId"
    | TreeOps.CannotRemoveRoot -> "CannotRemoveRoot"
    | TreeOps.WouldNestUnderSelf _ -> "WouldNestUnderSelf"
    | TreeOps.NotAContainer _ -> "NotAContainer"
    | TreeOps.ReorderMismatch _ -> "ReorderMismatch"
    | TreeOps.Rejected _ -> "Rejected"

/// Membership is the meaning on both sides: production carries `Set<string>`, the model carries
/// lists read as sets (proofs/README.md, "sets are lists").
let private asSet (xs: string list) = xs |> List.distinct |> List.sort

let private prodFpParts (f: Footprint) =
    [ asSet (Set.toList f.Reads)
      asSet (Set.toList f.StructureWrites)
      asSet (Set.toList f.ContentWrites)
      asSet (Set.toList f.UnknownParentWrites) ]

let private modelFpParts (f: DagFold.footprint) =
    [ asSet f.reads
      asSet f.structure_writes
      asSet f.content_writes
      asSet f.unknown_parent_writes ]

type private TreeTally =
    { Diffs: string list
      Accepted: int
      Rejected: int
      Classes: Set<string> }

let private emptyTreeTally =
    { Diffs = []
      Accepted = 0
      Rejected = 0
      Classes = Set.empty }

/// One (op, state) asked of both sides.
let private treeProbe
    (bridge: RNode -> TreeOps.tree)
    (op: SkeletonOp<RNode, string>)
    (st: RNode)
    (acc: TreeTally)
    : TreeTally =
    let mop = toModelOpWith bridge op
    let mst = bridge st
    let where = sprintf "op %s at tree %s" (treeW.Encode op) (prodTreeHash st)

    let fpDiff =
        let p = prodFpParts (Ops.footprint nodew idw [ op ])
        let m = modelFpParts (TreeOps.op_fp mop)

        if p <> m then
            [ sprintf "footprint differs — %s\n  production: %A\n  oracle:     %A" where p m ]
        else
            []

    let prod = Ops.apply nodew idw op st
    let model = TreeOps.apply mop mst

    let applyDiff, accepted, rejected, cls =
        match prod, model with
        | Ok pt, DagFold.Ok mt ->
            let ph = prodTreeHash pt
            let mh = modelTreeHash mt

            (if ph <> mh then
                 [ sprintf "accepted result differs — %s\n  production: %s\n  oracle:     %s" where ph mh ]
             else
                 []),
            1,
            0,
            None
        | Error pe, DagFold.Error me ->
            let pc = prodRejClass pe
            let mc = modelRejClass me

            (if pc <> mc then
                 [ sprintf "rejection class differs — %s\n  production: %s\n  oracle:     %s" where pc mc ]
             else
                 []),
            0,
            1,
            Some pc
        | Ok _, DagFold.Error me ->
            [ sprintf "production ACCEPTED but the oracle rejected (%s) — %s" (modelRejClass me) where ], 0, 0, None
        | Error pe, DagFold.Ok _ ->
            [ sprintf "production REJECTED (%s) but the oracle accepted — %s" (prodRejClass pe) where ], 0, 0, None

    { Diffs = acc.Diffs @ fpDiff @ applyDiff
      Accepted = acc.Accepted + accepted
      Rejected = acc.Rejected + rejected
      Classes =
        match cls with
        | Some c -> Set.add c acc.Classes
        | None -> acc.Classes }

/// Ops that REACH each rejection class the plain `apply` can raise, against the base tree
/// `root(doc)[a(section)[a1,a2], b(section)[b1]]`. The generator keeps only accepted ops, so the
/// refusal arms are supplied rather than hoped for.
let private treeRefusals: SkeletonOp<RNode, string> list =
    [ InsertChild("no-such-parent", RNode.leaf "fresh-133" "para" "v") // UnknownNode (parent)
      InsertChild("a", RNode.leaf "a1" "para" "v") // DuplicateId
      RemoveNode "root" // CannotRemoveRoot
      RemoveNode "no-such-node" // UnknownNode (target)
      MoveNode("a", "a1") // WouldNestUnderSelf
      MoveNode("a", "no-such-parent") // UnknownNode (new parent)
      ReorderChildren("a", [ "a1" ]) // ReorderMismatch
      ReorderChildren("no-such-parent", []) // UnknownNode (reorder)
      Batch [ InsertChild("b", RNode.leaf "b133" "para" "v"); RemoveNode "root" ] ] // all-or-nothing

/// Every op the generator yields, plus the refusals, asked at every state a prefix of the
/// generated pool reaches — the same construction the Phase 132 diamond family uses, and for the
/// same reason: a check at the base tree alone measures one instance.
let private treeDifferential (bridge: RNode -> TreeOps.tree) (seed: int) (trials: int) : TreeTally =
    let mutable r = ConfRng.ofSeed seed
    let mutable tally = emptyTreeTally

    for _ in 1..trials do
        let lanes, r' = treeLaneGen.Lanes 3 r
        r <- r'
        let generated = List.concat lanes

        let states =
            generated
            |> List.fold
                (fun (acc, cur) op ->
                    match Ops.apply nodew idw op cur with
                    | Ok t -> (acc @ [ t ]), t
                    | Error _ -> acc, cur)
                ([ treeBase ], treeBase)
            |> fst

        for op in generated @ treeRefusals do
            for st in states do
                tally <- treeProbe bridge op st tally

    tally

/// The theorem's own instance on the EXTRACTED code: for every pair the model's `independent`
/// declares disjoint and every state where both halves of the guarded algebra accept, the two
/// orders agree. `oracleFp` is the footprint the check runs under — the real one in the green
/// run, a blind one in the go-red.
let private modelDiamondBreaks
    (oracleFp: TreeOps.op -> DagFold.footprint)
    (ops: TreeOps.op list)
    (states: TreeOps.tree list)
    : string list * int =
    let mutable met = 0
    let mutable breaks = []

    for a in ops do
        for b in ops do
            if DagFold.independent (oracleFp a) (oracleFp b) then
                for s in states do
                    match TreeOps.wapply a s, TreeOps.wapply b s with
                    | DagFold.Ok sa, DagFold.Ok sb ->
                        met <- met + 1

                        match TreeOps.wapply b sa, TreeOps.wapply a sb with
                        | DagFold.Ok t1, DagFold.Ok t2 when t1 = t2 -> ()
                        | _ ->
                            breaks <-
                                breaks
                                @ [ sprintf "the extracted model breaks the diamond at a well-formed tree" ]
                    | _ -> ()

    breaks, met
// ---------------------------------------------------------------------------
//  Phase 136 — the two INTEGRITY WALKERS as a fourth oracle.
//
//  `proofs/Chain.fst` models `Dag.firstBreak` over the content-addressed DAG and
//  `OpStream.firstChainBreak` over the linear chain, clause for clause, and proves that a
//  single-node tamper is found — under ONE named premise: the content id determines the content.
//  This section runs the extracted model beside BOTH production walkers, over DAGs and chains
//  production itself built and over generated single-node tampers, comparing the verdicts as
//  strings so the outcome class, the node or index it is reported at, WHICH check failed, and the
//  expected/got values are all compared at once.
//
//  `Dag.nodeHash` is private, so the only way to reach it is through `Dag.append` — which is what
//  makes the intact runs evidence rather than a formality. A model whose `isort` and `join_comma`
//  were not production's `List.sortWith CompareOrdinal` and `String.concat ","` would recompute a
//  different id for every node and report a break on an INTACT DAG. `mintsProductionIds` below
//  asserts the same thing directly, node by node, so a divergence says which half moved.
//
//  ON THE CORPUS. The wire corpus's `dag/` family is the UI host's DAG-RECORD WIRE FORMAT
//  (`kind: "dag-record-round-trip"`), not a pool of Core content ids: its `hash` members are
//  64 characters, minted by that host's own pre-image under SHA-256 over an envelope carrying
//  members Core's `DagNode` does not have, where Core's default `HashFn` is FNV-1a and emits 8.
//  Handing those four records to `Dag.firstBreak` would report four content-id mismatches — a true
//  answer to the wrong question. What the family DOES supply, and what the generated pools cannot,
//  are the SHAPES: a genesis node, a linear step, a two-parent MERGE, and both actor kinds. Those
//  are rebuilt below through production's own `Dag.append` / `Dag.merge`, and the size fact is
//  asserted so the boundary goes red if it ever moves.
// ---------------------------------------------------------------------------

/// The model's `le`: F#'s `List.sortWith (fun a b -> String.CompareOrdinal(a, b))` as a predicate.
let private ordinalLe (a: string) (b: string) : bool = System.String.CompareOrdinal(a, b) <= 0

/// What a tamper puts in a work-plan op's place: a DIFFERENT op, on every case, so no tamper is
/// silently a no-op. The tampers below skip any that lands back on the op it replaced.
let private tamperedPlanOp (op: PlanOp) : PlanOp =
    match op with
    | AddItem(i, t) -> AddItem(i, t + "!")
    | Retitle(i, t) -> Retitle(i, t + "!")
    | SetShipped i -> Retitle(i, "tampered")
    | AddDep(i, d) -> AddDep(i, d + "!")

/// A production `Dag.T` as the model reads it — `Map.toList`'s own order, which is the order
/// `Dag.firstBreak` walks; the map KEY beside the node, because production compares the key and
/// never the node's own `Id` field; and the actor as the string `Actor.encode` produces, which is
/// the only thing the hash pre-image ever sees of it.
let private toChainEntries (dag: Dag.T<'Op>) : Chain.entry<'Op> list =
    dag.Nodes
    |> Map.toList
    |> List.map (fun (k, n) ->
        { Chain.ekey = k
          Chain.enode =
            { Chain.dparents = n.Parents
              Chain.dactor = Actor.encode n.Actor
              Chain.dop = n.Op } })

/// A DAG walker's verdict as a COMPARABLE VALUE (Phase 147). Production's break carries a typed
/// `DagBreakReason`; the model's carries the string its extraction mints, classified through the
/// same total `ofString`. Comparing these compares the reason as a CLASS rather than as two
/// spellings that happen to match — which is the projection Phase 147 deletes, and it is this
/// differential that asked for it. It also sharpens the failure: a model whose reason string drifted
/// a character used to fail as an unexplained textual mismatch and now fails as an `Unrecognised`
/// carrying that text against a named case, which says what went wrong.
type private DagVerdict =
    | DagIntact
    | DagBroke of node: string * reason: DagBreakReason * expected: string * got: string

let private renderDagVerdict (v: DagVerdict) : string =
    match v with
    | DagIntact -> "intact"
    | DagBroke(node, reason, expected, got) ->
        sprintf "break at %s | %s | expected=%s | got=%s" node (DagBreakReason.toString reason) expected got

/// The reason class a verdict names, for the cases that assert WHICH check fired.
let private dagVerdictReason (v: DagVerdict) : DagBreakReason option =
    match v with
    | DagIntact -> None
    | DagBroke(_, reason, _, _) -> Some reason

let private prodDagVerdict (b: DagBreak option) : DagVerdict =
    match b with
    | None -> DagIntact
    | Some b -> DagBroke(b.NodeId, b.Reason, b.Expected, b.Got)

let private modelDagVerdict (b: Chain.found<Chain.dbreak>) : DagVerdict =
    match b with
    | Chain.Missing -> DagIntact
    | Chain.Found b -> DagBroke(b.bnode, DagBreakReason.ofString b.breason, b.bexpected, b.bgot)

/// Both walkers on the same DAG. The two hash functions are separate arguments only so the go-red
/// case can hand the MODEL one production is not using; every real run passes the same one twice.
let private dagVerdicts
    (prodHash: HashFn)
    (modelHash: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (dag: Dag.T<'Op>)
    : DagVerdict * DagVerdict =
    prodDagVerdict (Dag.firstBreak prodHash w dag),
    modelDagVerdict (Chain.first_break modelHash w.Encode ordinalLe (toChainEntries dag))

/// The DAG `FoldConfluence.foldOnce` builds, under a CHOSEN `HashFn` — one shared base node and
/// one chain per lane. `productionDag` above pins the default hash; the premise case below needs
/// a deliberately non-injective one, and nothing else about the construction may move with it.
let private dagUnder
    (hashFn: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (baseOp: 'Op)
    (lanes: 'Op list list)
    : Dag.T<'Op> =
    let baseId, d0 = Dag.append hashFn w (Human "base") baseOp "" Dag.empty
    let mutable d = d0

    for (i, ops) in List.indexed lanes do
        let actor = Human("lane-" + string i)
        let mutable h = baseId

        for op in ops do
            let id, d' = Dag.append hashFn w actor op h d
            h <- id
            d <- d'

    d

/// Every node's id, recomputed by the MODEL, against the one production minted — the pre-image
/// itself, checked directly rather than inferred from the walker's verdict.
let private mintsProductionIds
    (hashFn: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (dag: Dag.T<'Op>)
    : string option =
    dag.Nodes
    |> Map.toList
    |> List.tryPick (fun (k, n) ->
        let minted =
            Chain.node_hash hashFn w.Encode ordinalLe n.Parents (Actor.encode n.Actor) n.Op

        if minted <> k then
            Some(sprintf "the model mints %s where production minted %s (op %s)" minted k (w.Encode n.Op))
        else
            None)

/// Every single-node tamper of one DAG, as (what was done, the tampered DAG). A TAMPER moves the
/// node's content and leaves its ADDRESS — the map key — exactly as it was; that is the threat the
/// content id exists to catch, and it is precisely not a rewrite.
///
/// The fourth class is the other break: a node another node NAMES, deleted. It has to be a
/// deletion rather than a re-pointed parent, because a re-pointed parent changes the pre-image and
/// production reports the content-id mismatch first — so "missing parent" is unreachable by
/// tampering a node's own fields.
let private dagTampers (otherOp: 'Op -> 'Op) (dag: Dag.T<'Op>) : (string * Dag.T<'Op>) list =
    let nodes = dag.Nodes |> Map.toList

    [ for (k, n) in nodes do
          let o' = otherOp n.Op

          if o' <> n.Op then
              yield
                  sprintf "op@%s" k,
                  { dag with
                      Nodes = Map.add k { n with Op = o' } dag.Nodes }

          let a' = Human "tamperer"

          if a' <> n.Actor then
              yield
                  sprintf "actor@%s" k,
                  { dag with
                      Nodes = Map.add k { n with Actor = a' } dag.Nodes }

          match
              nodes
              |> List.tryPick (fun (p, _) -> if p <> k && n.Parents <> [ p ] then Some p else None)
          with
          | Some p ->
              yield
                  sprintf "reparent@%s" k,
                  { dag with
                      Nodes = Map.add k { n with Parents = [ p ] } dag.Nodes }
          | None -> ()

          for p in n.Parents do
              yield
                  sprintf "drop-parent@%s" p,
                  { dag with
                      Nodes = Map.remove p dag.Nodes } ]

type private WalkerTally =
    {
        Failure: string option
        /// Intact structures compared — a run that never met one has not tested the agreeing case.
        Intact: int
        /// Tampers compared, and how many production actually DETECTED. A run whose tampers were all
        /// invisible has compared two walkers that both said "intact" and measured nothing.
        Tampers: int
        Detected: int
    }

let private emptyWalkerTally =
    { Failure = None
      Intact = 0
      Tampers = 0
      Detected = 0 }

/// Run the DAG walker differential over `iterations` generated lane sets from one pool.
let private dagDifferential
    (label: string)
    (prodHash: HashFn)
    (modelHash: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (otherOp: 'Op -> 'Op)
    (gen: LaneGen<'Op, 'State>)
    (laneCount: int)
    (seed: int)
    (iterations: int)
    : WalkerTally =
    let mutable rng = ConfRng.ofSeed seed
    let mutable t = emptyWalkerTally

    let note (why: string) =
        if t.Failure.IsNone then
            t <- { t with Failure = Some why }

    for _ in 1..iterations do
        let lanes, r' = gen.Lanes laneCount rng
        rng <- r'
        let dag = dagUnder prodHash w gen.BaseOp lanes

        match mintsProductionIds prodHash w dag with
        | Some why -> note (sprintf "%s: seed=%d %s" label seed why)
        | None -> ()

        let compare (what: string) (d: Dag.T<'Op>) : DagVerdict =
            let p, m = dagVerdicts prodHash modelHash w d

            if p <> m then
                note (
                    sprintf
                        "%s: seed=%d tamper=%s\n%s\n  production: %s\n  model:      %s"
                        label
                        seed
                        what
                        (renderLanes w.Encode lanes)
                        (renderDagVerdict p)
                        (renderDagVerdict m)
                )

            p

        if compare "none" dag = DagIntact then
            t <- { t with Intact = t.Intact + 1 }

        for (what, tampered) in dagTampers otherOp dag do
            let p = compare what tampered
            t <- { t with Tampers = t.Tampers + 1 }

            if p <> DagIntact then
                t <- { t with Detected = t.Detected + 1 }

    t

// ---- the linear chain ----

let rec private posOfInt (i: int) : Chain.pos =
    if i <= 0 then
        Chain.PZero
    else
        Chain.PSucc(posOfInt (i - 1))

let rec private intOfPos (p: Chain.pos) : int =
    match p with
    | Chain.PZero -> 0
    | Chain.PSucc m -> 1 + intOfPos m

/// F#'s `string (seq: int)`, which the model takes as a parameter for the same reason it takes the
/// hash: rendering an integer is not something a model of a hash chain owns.
let private showPos (p: Chain.pos) : string = string (intOfPos p)

/// Production's records as the model reads them. `Seq` is a Peano numeral here (the extraction
/// carries no integers — `proofs/README.md`, finding 2), so a tampered NEGATIVE sequence is
/// outside what this bridge can carry and the tampers below stay non-negative.
let private toChainRecords (rs: OpRecord<'Op> list) : Chain.record<'Op> list =
    rs
    |> List.map (fun r ->
        { Chain.rseq = posOfInt r.Seq
          Chain.ractor = Actor.encode r.Actor
          Chain.rop = r.Op
          Chain.rprev = r.PrevHash
          Chain.rhash = r.Hash })

/// The linear walker's verdict, on the same footing as `DagVerdict` (Phase 147). `ChainBreak.Reason`
/// has been a closed DU since Phase 125; this differential was still rendering it to text and
/// comparing the text, so the same reason-as-a-class argument applies unchanged and the section now
/// holds no string match on a break reason at all.
type private ChainVerdict =
    | ChainIntact
    | ChainBroke of index: int * reason: ChainBreakReason * expected: string * got: string

let private renderChainVerdict (v: ChainVerdict) : string =
    match v with
    | ChainIntact -> "intact"
    | ChainBroke(index, reason, expected, got) ->
        sprintf "break at %d | %s | expected=%s | got=%s" index (ChainBreakReason.toString reason) expected got

let private chainVerdictReason (v: ChainVerdict) : ChainBreakReason option =
    match v with
    | ChainIntact -> None
    | ChainBroke(_, reason, _, _) -> Some reason

let private prodChainVerdict (b: ChainBreak option) : ChainVerdict =
    match b with
    | None -> ChainIntact
    | Some b -> ChainBroke(b.Index, b.Reason, b.Expected, b.Got)

let private modelChainVerdict (b: Chain.found<Chain.cbreak>) : ChainVerdict =
    match b with
    | Chain.Missing -> ChainIntact
    | Chain.Found b -> ChainBroke(intOfPos b.cindex, ChainBreakReason.ofString b.creason, b.cexpected, b.cgot)

let private chainVerdicts
    (prodHash: HashFn)
    (modelHash: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (rs: OpRecord<'Op> list)
    : ChainVerdict * ChainVerdict =
    prodChainVerdict (OpStream.firstChainBreak prodHash w rs),
    modelChainVerdict (Chain.first_chain_break modelHash showPos w.Encode "" (toChainRecords rs))

/// A chain production built by `OpStream.append`, skipping the ops the domain reducer rejects —
/// `append` chains nothing on a rejection, so the chain is the shape of the accepted run.
let private chainUnder
    (hashFn: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (actor: Actor)
    (ops: 'Op list)
    : OpRecord<'Op> list =
    let mutable st = state0
    let mutable rs: OpRecord<'Op> list = OpStream.empty

    for op in ops do
        match OpStream.append hashFn w actor op st rs with
        | Ok(st', rs') ->
            st <- st'
            rs <- rs'
        | Error _ -> ()

    rs

/// Every single-record tamper of one chain, in the four classes the walker's three checks cover:
/// the op and the actor (found by the recomputed hash), the sequence, the prev-link, and a
/// DROPPED record — truncation, which the sequence check finds.
let private chainTampers (otherOp: 'Op -> 'Op) (rs: OpRecord<'Op> list) : (string * OpRecord<'Op> list) list =
    let n = List.length rs

    [ for i in 0 .. n - 1 do
          let r = List.item i rs

          let put (r': OpRecord<'Op>) =
              rs |> List.mapi (fun j x -> if j = i then r' else x)

          let o' = otherOp r.Op

          if o' <> r.Op then
              yield sprintf "op@%d" i, put { r with Op = o' }

          let a' = Human "tamperer"

          if a' <> r.Actor then
              yield sprintf "actor@%d" i, put { r with Actor = a' }

          yield sprintf "seq@%d" i, put { r with Seq = r.Seq + 1 }
          yield sprintf "prev@%d" i, put { r with PrevHash = "deadbeef" }

          if n > 1 then
              yield sprintf "drop@%d" i, (rs |> List.indexed |> List.filter (fun (j, _) -> j <> i) |> List.map snd) ]

let private chainDifferential
    (label: string)
    (prodHash: HashFn)
    (modelHash: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (otherOp: 'Op -> 'Op)
    (gen: LaneGen<'Op, 'State>)
    (seed: int)
    (iterations: int)
    : WalkerTally =
    let mutable rng = ConfRng.ofSeed seed
    let mutable t = emptyWalkerTally

    let note (why: string) =
        if t.Failure.IsNone then
            t <- { t with Failure = Some why }

    for _ in 1..iterations do
        // One lane's ops, appended as one writer's linear stream.
        let lanes, r' = gen.Lanes 2 rng
        rng <- r'
        let ops = List.concat lanes
        let rs = chainUnder prodHash w gen.State0 (Human "writer") ops

        let compare (what: string) (records: OpRecord<'Op> list) : ChainVerdict =
            let p, m = chainVerdicts prodHash modelHash w records

            if p <> m then
                note (
                    sprintf
                        "%s: seed=%d tamper=%s ops=[%s]\n  production: %s\n  model:      %s"
                        label
                        seed
                        what
                        (ops |> List.map w.Encode |> String.concat "; ")
                        (renderChainVerdict p)
                        (renderChainVerdict m)
                )

            p

        if not (List.isEmpty rs) then
            if compare "none" rs = ChainIntact then
                t <- { t with Intact = t.Intact + 1 }

            for (what, tampered) in chainTampers otherOp rs do
                let p = compare what tampered
                t <- { t with Tampers = t.Tampers + 1 }

                if p <> ChainIntact then
                    t <- { t with Detected = t.Detected + 1 }

    t

let private expectWalkerAgreement (label: string) (t: WalkerTally) =
    match t.Failure with
    | Some why -> failtest why
    | None ->
        Expect.isGreaterThan t.Intact 0 (sprintf "%s: no intact structure was compared" label)
        Expect.isGreaterThan t.Tampers 0 (sprintf "%s: no tamper was compared" label)

        // The teeth's vacuity guard, and the one that matters most here: two walkers that both say
        // "intact" agree perfectly and certify nothing. Detection has to have HAPPENED.
        Expect.isGreaterThan
            t.Detected
            0
            (sprintf "%s: every tamper went undetected, so the agreement is vacuous" label)

/// A deliberately NON-INJECTIVE `HashFn`: `nodeHash` hands it the joined parents and
/// `actorEncoded + "|" + encodedOp`, and this folds only up to the first `|` — so the actor
/// survives and the OP IS DROPPED, and two nodes differing only in their op mint one id.
/// `Actor.encode` emits a JSON object with no `|` in it, so the first `|` is the separator.
let private opBlindHash: HashFn =
    fun parents rest ->
        let cut = rest.IndexOf '|'
        OpStream.defaultHash parents (if cut < 0 then rest else rest.Substring(0, cut))

/// The go-red instrument for the comparison itself: a hash the MODEL uses and production does not
/// — the same function with its two arguments swapped, so every recomputed id is wrong and an
/// intact DAG must be reported as broken.
let private swappedHash: HashFn = fun a b -> OpStream.defaultHash b a

// ---- the corpus dag/ family, as a source of SHAPES ----

/// One `dag/` fixture, read for what Core can use: the parent shape, the typed actor, and the op
/// payload. Its own `hash` is kept only so the size assertion below can be made about it.
type private DagShape =
    { Fixture: string
      CorpusHash: string
      CorpusParents: string list
      Actor: Actor
      OpJson: string }

let private readDagShapes (root: string) : Result<DagShape list, string> =
    Directory.GetFiles(Path.Combine(root, "dag"), "*.json")
    |> Array.sort
    |> Array.toList
    |> List.filter (fun p -> Path.GetFileName p <> "manifest.json")
    |> List.map (fun path ->
        let name = Path.GetFileNameWithoutExtension path

        match Json.parse (File.ReadAllText path) with
        | Error e -> Error(sprintf "%s: not JSON (%s)" name e)
        | Ok(JObj fields) ->
            let actor =
                match field fields "actor" with
                | Some(JObj af) ->
                    let s k = defaultArg (strField af k) ""

                    match strField af "kind" with
                    | Some "agent" -> Some(Agent(s "model", s "version", s "id"))
                    | _ -> Some(Human(s "id"))
                | _ -> None

            match strField fields "hash", field fields "parents", actor, field fields "op" with
            | Some h, Some(JArr ps), Some a, Some op ->
                Ok
                    { Fixture = name
                      CorpusHash = h
                      CorpusParents =
                        ps
                        |> List.choose (fun v ->
                            match v with
                            | JStr s -> Some s
                            | _ -> None)
                      Actor = a
                      OpJson = Canon.render op }
            | _ -> Error(sprintf "%s: expected hash / parents / actor / op" name)
        | Ok _ -> Error(sprintf "%s: not a JSON object" name))
    |> List.fold
        (fun acc r ->
            match acc, r with
            | Ok xs, Ok x -> Ok(xs @ [ x ])
            | Error e, _ -> Error e
            | _, Error e -> Error e)
        (Ok [])

/// The op payload of a `dag/` fixture is opaque wire JSON, and every walker treats an op that way.
let private rawW: StreamWitness<string, string, string> =
    { Apply = fun op st -> Ok(st + "/" + op)
      Encode = id
      Decode = Ok }

// ---------------------------------------------------------------------------
//  The PARSER differential (Phase 146) — `JsonParse.parse` beside
//  `Json.parseDetailedWithPolicy`.
//
//  Everything above this line begins at a `JVal` that already exists, which is exactly the
//  boundary Phase 135's theorem 1 drew. What runs here is the model of the recursive-descent
//  parser itself, over the same TEXT production reads: the corpus fixtures as raw bytes, a
//  near-miss table that reaches every classified failure, and generated deep, wide and random
//  inputs.
//
//  Each probe renders both answers into ONE string carrying the outcome class, the decoded value,
//  and — on a failure — the KIND, the POSITION and the MESSAGE. A model agreeing on accept-vs-
//  refuse alone would not notice a parser that classified the wrong thing or pointed at the wrong
//  character, and the position is the half most likely to drift silently.
// ---------------------------------------------------------------------------

module private JsonParseDiff =

    /// A production character as the model reads it. Every character the parser DISTINGUISHES has
    /// its own constructor; every other is carried verbatim, so two different characters are never
    /// identified and the bridge loses nothing.
    let toCh (c: char) : JsonParse.ch =
        match c with
        | ' ' -> JsonParse.CSpace
        | '\t' -> JsonParse.CTab
        | '\n' -> JsonParse.CNewline
        | '\r' -> JsonParse.CReturn
        | '"' -> JsonParse.CQuote
        | '\\' -> JsonParse.CBackslash
        | '/' -> JsonParse.CSlash
        | '{' -> JsonParse.CLBrace
        | '}' -> JsonParse.CRBrace
        | '[' -> JsonParse.CLBrack
        | ']' -> JsonParse.CRBrack
        | ':' -> JsonParse.CColon
        | ',' -> JsonParse.CComma
        | '-' -> JsonParse.CMinus
        | '+' -> JsonParse.CPlus
        | '.' -> JsonParse.CDot
        | '0' -> JsonParse.CD0
        | '1' -> JsonParse.CD1
        | '2' -> JsonParse.CD2
        | '3' -> JsonParse.CD3
        | '4' -> JsonParse.CD4
        | '5' -> JsonParse.CD5
        | '6' -> JsonParse.CD6
        | '7' -> JsonParse.CD7
        | '8' -> JsonParse.CD8
        | '9' -> JsonParse.CD9
        | 'a' -> JsonParse.CLa
        | 'b' -> JsonParse.CLb
        | 'c' -> JsonParse.CLc
        | 'd' -> JsonParse.CLd
        | 'e' -> JsonParse.CLe
        | 'f' -> JsonParse.CLf
        | 'l' -> JsonParse.CLl
        | 'n' -> JsonParse.CLn
        | 'r' -> JsonParse.CLr
        | 's' -> JsonParse.CLs
        | 't' -> JsonParse.CLt
        | 'u' -> JsonParse.CLu
        | 'A' -> JsonParse.CUa
        | 'B' -> JsonParse.CUb
        | 'C' -> JsonParse.CUc
        | 'D' -> JsonParse.CUd
        | 'E' -> JsonParse.CUe
        | 'F' -> JsonParse.CUf
        | other -> JsonParse.COther(string other)

    let toChs (s: string) : JsonParse.ch list = s |> Seq.map toCh |> List.ofSeq

    /// The BLIND bridge — the go-red instrument for the comparison itself, and this family's
    /// counterpart to the decode family's blind integer bridge. It hides the two container
    /// characters, so the model is asked about a document production was not asked about and every
    /// structured input must disagree. It touches nothing else, so a scalar still agrees, which is
    /// what keeps the instrument narrow enough to be informative.
    let toChsBlind (s: string) : JsonParse.ch list =
        s
        |> Seq.map (fun c ->
            match c with
            | '{'
            | '[' -> JsonParse.COther(string c)
            | other -> toCh other)
        |> List.ofSeq

    let private inv = System.Globalization.CultureInfo.InvariantCulture

    /// The model's own spelling of a character, so the two sides never disagree merely about how a
    /// character is written.
    let chStr (c: JsonParse.ch) : string = JsonParse.ch_str c

    let tokStr (t: JsonParse.ch list) : string = t |> List.map chStr |> String.concat ""

    /// The four hex digits a `\uXXXX` carried, resolved to the code point HERE. The model declines
    /// to compute it (it carries no integers); the host can, so the differential still compares the
    /// decoded character rather than only the escape's guards.
    let private hexVal (c: JsonParse.ch) : int = System.Convert.ToInt32(chStr c, 16)

    let private ochToString (o: JsonParse.och) : string =
        match o with
        | JsonParse.OLit c -> chStr c
        | JsonParse.OEsc e ->
            match chStr e with
            | "n" -> "\n"
            | "r" -> "\r"
            | "t" -> "\t"
            | "b" -> "\b"
            | "f" -> "\f"
            | other -> other
        | JsonParse.OUni(a, b, c, d) ->
            string (char ((hexVal a <<< 12) + (hexVal b <<< 8) + (hexVal c <<< 4) + hexVal d))

    let private modelStr (os: JsonParse.och list) : string =
        os |> List.map ochToString |> String.concat ""

    let rec private renderProd (v: JVal) : string =
        match v with
        | JStr s -> "s:" + s
        | JInt i -> "i:" + string i
        | JBool b -> "b:" + string b
        | JFloat f -> "f:" + f.ToString("R", inv)
        | JArr xs -> "[" + (xs |> List.map renderProd |> String.concat ",") + "]"
        | JObj fs ->
            "{"
            + (fs |> List.map (fun (k, v) -> k + "=" + renderProd v) |> String.concat ",")
            + "}"

    /// The model's tree in the same rendering. A number carries its TOKEN, so this is where the
    /// token boundaries are checked: reading it back with the same .NET call production made means
    /// a model that scanned one character more or less renders a different value.
    let rec private renderModel (v: JsonParse.jval) : string =
        match v with
        | JsonParse.JStr s -> "s:" + modelStr s
        | JsonParse.JInt tok -> "i:" + string (System.Int32.Parse(tokStr tok, inv))
        | JsonParse.JBool b -> "b:" + string b
        | JsonParse.JFloat tok ->
            let d =
                System.Double.Parse(tokStr tok, System.Globalization.NumberStyles.Float, inv)

            "f:" + d.ToString("R", inv)
        | JsonParse.JArr xs -> "[" + (xs |> List.map renderModel |> String.concat ",") + "]"
        | JsonParse.JObj fs ->
            "{"
            + (fs
               |> List.map (fun (k, v) -> modelStr k + "=" + renderModel v)
               |> String.concat ",")
            + "}"

    /// The model's one opacity parameter, instantiated: `System.Double.TryParse` plus the
    /// finiteness gate, which is exactly what `parseNumber` asks.
    let floatRead (tok: JsonParse.ch list) : JsonParse.freadv =
        match System.Double.TryParse(tokStr tok, System.Globalization.NumberStyles.Float, inv) with
        | true, v when System.Double.IsNaN v || System.Double.IsInfinity v -> JsonParse.FNonFinite
        | true, _ -> JsonParse.FFinite
        | _ -> JsonParse.FUnparsable

    /// The depth cap as the model spends it — one element per descent (`proofs/README.md`: the
    /// extraction carries no integers).
    let budget (n: int) : unit list = List.replicate n ()

    let private modelPolicy (p: NullPolicy) : JsonParse.policy =
        match p with
        | RejectNull -> JsonParse.RejectNull
        | EraseMemberNull -> JsonParse.EraseMemberNull

    let prodAnswer (policy: NullPolicy) (maxDepth: int) (input: string) : string =
        match Json.parseDetailedWithPolicy policy maxDepth input with
        | Ok v -> "ok " + renderProd v
        | Error e -> sprintf "err %A @%d %s" e.Kind e.Position e.Message

    /// The model's answer. `modelBudget` and `cap` are separate arguments only so the go-red case
    /// can hand the model a cap production is not using; every real run passes `budget maxDepth`.
    let modelAnswer
        (bridge: string -> JsonParse.ch list)
        (policy: NullPolicy)
        (cap: int)
        (modelBudget: unit list)
        (input: string)
        : string =
        match JsonParse.parse floatRead (string cap) (modelPolicy policy) modelBudget (bridge input) with
        | JsonParse.ROk v -> "ok " + renderModel v
        | JsonParse.RErr(k, m, at) -> sprintf "err %A @%d %s" k (input.Length - List.length at) m

    /// Every way the two disagree over one pool, plus how many inputs reached each outcome class —
    /// a run that only ever refused has compared twelve error messages and measured no parse at
    /// all, and a run that only ever accepted has measured none of the classification.
    let sweep (policy: NullPolicy) (maxDepth: int) (inputs: (string * string) list) =
        let mutable accepted = 0
        let mutable refused = 0
        let bad = ResizeArray<string>()

        for (name, input) in inputs do
            let p = prodAnswer policy maxDepth input
            let m = modelAnswer toChs policy maxDepth (budget maxDepth) input

            if p.StartsWith "ok " then
                accepted <- accepted + 1
            else
                refused <- refused + 1

            if p <> m then
                bad.Add(sprintf "%s: input %s\n    production %s\n    model      %s" name input p m)

        List.ofSeq bad, accepted, refused

    let expectAgreement (label: string) (bad: string list, accepted: int, refused: int) =
        if not bad.IsEmpty then
            failtestf
                "%s: the model and production disagree on %d input(s). First 5:\n%s"
                label
                bad.Length
                (bad |> List.truncate 5 |> String.concat "\n")

        Expect.isGreaterThan accepted 0 (label + ": no input was ACCEPTED — the accept path measured nothing")

        Expect.isGreaterThan refused 0 (label + ": no input was REFUSED — the classification measured nothing")

    // ---- the pools ----

    /// Every corpus fixture, as the raw TEXT production reads. The differential above this one asks
    /// about the decoded value; this one asks about the bytes.
    let corpusTexts (family: string) : (string * string) list =
        match SiblingCorpus.resolve family with
        | SiblingCorpus.Found root ->
            Directory.GetFiles(Path.Combine(root, family), "*.json")
            |> Array.sort
            |> Array.toList
            |> List.map (fun p -> Path.GetFileNameWithoutExtension p, File.ReadAllText p)
        | SiblingCorpus.SkippedByRequest why -> failtest why
        | SiblingCorpus.Absent why -> failtest why

    /// The near misses, hand-written so every classified failure is REACHED rather than hoped for —
    /// the host's counterpart to the model's own reachability theorem. The two `null` near misses
    /// are the ones Phase 135's ladder names as the reason its policy entry is an assumption:
    /// `nul` falls through to the strict arm, `nullish` is caught by the following expectation.
    let nearMisses: (string * string) list =
        [ "empty", ""
          "whitespace only", "   \t\r\n"
          "bare word", "%"
          "true", "true"
          "truncated true", "tru"
          "truncated false", "fals"
          "bare null", "null"
          "member null", "{\"a\":null}"
          "member nul", "{\"a\":nul}"
          "member nullish", "{\"a\":nullish}"
          "array null", "[null]"
          "unterminated string", "\"abc"
          "unterminated escape", "\"abc\\"
          "bad escape", "\"a\\q\""
          "truncated unicode", "\"\\u12\""
          "bad hex digit", "\"\\uzzzz\""
          "good unicode", "\"\\u0041\\u00e9\""
          "all short escapes", "\"\\\"\\\\\\/\\n\\r\\t\\b\\f\""
          "trailing characters", "0 0"
          "trailing brace", "{} }"
          "object missing colon", "{\"a\" 1}"
          "object trailing comma", "{\"a\":1,}"
          "array trailing comma", "[1,]"
          "array missing comma", "[1 2]"
          "unclosed object", "{\"a\":1"
          "unclosed array", "[1"
          "empty object", "{}"
          "empty array", "[]"
          "nested empties", "{\"a\":[],\"b\":{}}"
          "zero", "0"
          "negative zero", "-0"
          "leading zeros small", "007"
          "leading zeros int53", "0009007199254740992"
          "int53 boundary", "9007199254740992"
          "int53 boundary plus one", "9007199254740993"
          "int32 boundary", "2147483647"
          "int32 boundary plus one", "2147483648"
          "int32 min", "-2147483648"
          "int32 min minus one", "-2147483649"
          "seventeen nines", "99999999999999999"
          "bare minus", "-"
          "float", "1.5"
          "float exponent", "1e3"
          "float exponent plus", "1e+3"
          "float exponent minus", "1.5e-3"
          "exponent no digits", "1e"
          "exponent sign no digits", "1e+"
          "trailing dot", "1."
          "overflowing float", "1e400"
          "negative overflowing float", "-1e400"
          "deep-ish", "[[[[[1]]]]]"
          "wide", "[1,2,3,4,5,6,7,8,9,10]"
          "whitespace everywhere", " { \"a\" : [ 1 , 2 ] , \"b\" : true } "
          "unicode key", "{\"kéy\":1}"
          "colon only", ":"
          "comma only", ","
          "close brace only", "}" ]

    /// A nesting of exactly `k` arrays around a scalar — the family the model's `depth_bound_exact`
    /// is stated over, built here so the theorem's instance and the differential's input are the
    /// same shape.
    let nestArr (k: int) : string =
        String.replicate k "[" + "0" + String.replicate k "]"

    let nestObj (k: int) : string =
        String.replicate k "{\"a\":" + "0" + String.replicate k "}"

    /// Deterministic character soup over the parser's own alphabet — most of it malformed, which is
    /// the point: it is the only pool that reaches failure positions nobody thought to write down.
    let soup (seed: int) (count: int) : (string * string) list =
        let alphabet = "{}[]\",:0123456789-+.eE\\ \ttnrufalse\u00e9%"
        let mutable state = uint64 seed * 6364136223846793005UL + 1442695040888963407UL

        let next () =
            state <- state * 6364136223846793005UL + 1442695040888963407UL
            int ((state >>> 33) &&& 0x7FFFFFFFUL)

        [ for i in 1..count ->
              let len = next () % 24

              let s =
                  System.String(Array.init len (fun _ -> alphabet.[next () % alphabet.Length]))

              sprintf "soup seed=%d #%d" seed i, s ]

// ---------------------------------------------------------------------------
//  Phase 145 — the pre-image's two splices, measured against production's encodings
// ---------------------------------------------------------------------------
//
// `Chain.fst` proves the two splices unambiguous under conditions on the ALPHABETS: a parent id
// carries no comma and is not empty, and no actor encoding is a proper prefix of another. Those
// are claims about `OpStream.defaultHash`'s output and about `Actor.encode`, and the model cannot
// check either — F*'s `string` is primitive. This is where they are checked, and the population is
// chosen adversarially rather than typically, because a typical actor cannot refute anything.

/// `x` is a PROPER prefix of `y` — the model's `Chain.proper_prefix`, spelled over `string`.
let private properPrefix (x: string) (y: string) : bool =
    x.Length < y.Length && y.StartsWith(x, System.StringComparison.Ordinal)

/// Actors chosen to break a splice if anything can: the separator itself, the JSON metacharacters
/// `Actor.encode`'s escaper does and does not handle, an id that spells another actor's encoding,
/// the empty id, and both cases at every field.
let private adversarialActors: Actor list =
    [ Human ""
      Human "a"
      Human "a|b"
      Human "a|b|c"
      Human "a\"}|{\""
      Human "a\\|b"
      Human "a,b"
      Human "{\"kind\":\"human\",\"id\":\"a\"}"
      Human "}|{"
      Human "\n|\t"
      Agent("", "", "")
      Agent("m", "v", "a")
      Agent("m|1", "v|2", "a|3")
      Agent("m", "v", "a\"}|x")
      Agent("m", "v", "{\"kind\":\"agent\"}")
      Agent("m,1", "v,2", "a,3") ]

/// A `StreamGen` over the work-plan domain, drawn through the lane generator this differential
/// already runs on — so the population the kit's codec law samples is the population the model's
/// `op_codec_injective` parameter is about, rather than a second one invented beside it.
let private planStreamGen: StreamGen<PlanOp, Plan> =
    { State0 = planLaneGen.State0
      Op =
        fun rng ->
            let lanes, r = planLaneGen.Lanes 1 rng

            match lanes |> List.collect id with
            | op :: _ -> op, r
            | [] -> planLaneGen.BaseOp, r } // a lane may legitimately be empty

/// Op encodings chosen the same way — including the work-plan codec's own `"A|" + id + "|" + title`
/// shape, which is why the splice condition is not academic.
let private adversarialOpEncodings: string list =
    [ ""; "|"; "A|x|t"; "}|{"; "{\"kind\":\"human\",\"id\":\"a\"}|A|x|t"; "\"}" ]


// ---------------------------------------------------------------------------
//  Phase 138 — the APPLY-ENGINE PRESERVATION model beside `Ops.apply` / `canApply` / `invert`.
//
//  WHY THIS FAMILY EXISTS SEPARATELY FROM THE PHASE 133 ONE, which already runs the tree model
//  beside `Ops.apply`. That family draws its inserts from Phase 80's generator, whose `FreshNode`
//  contract is an id NOT in the tree — so it cannot draw a colliding graft, and for the whole of
//  Phase 137 it was green while the extracted model carried the pre-137 validator and production
//  carried the fixed one. A differential over a pool that cannot reach the disputed inputs is not
//  evidence of agreement about them. This one MINTS the disputed inputs, per state, from the
//  state's own ids: a subtree whose DESCENDANT id the tree already holds, a subtree that repeats an
//  id WITHIN itself, and one that does both — the two halves of `firstDuplicateId`.
//
//  FOUR COMPARISONS, per (op, state):
//    1. `Ops.apply` vs the model's `apply` — verdict, accepted result through `Tree.encodeHash`
//       (production's own function on both sides), rejection by CLASS.
//    2. `Ops.canApply` vs the model's `can_apply` — the dry run, same shape.
//    3. that `canApply` and `apply` agree on EACH side, which is `canapply_preserves` instantiated.
//    4. `Ops.invert` vs the model's `invert_leaf` on an accepted leaf operation, compared as the
//       OPERATION each returns, plus the round trip run on production: applying the inverse to the
//       result must give the input back, which is `invert_applicable` instantiated.
//
//  THE GO-RED IS THE RETIRED COUNTEREXAMPLE. `TreeOps.apply_pre137` is the pre-Phase-137 clause
//  kept under its own name (proofs/TreeOps.fst section 13), so the "a validator that skips the
//  subtree check must LOSE" case needs no instrument built for it: the model of that validator is
//  already extracted, and handing it to the differential is exactly the measurement.
// ---------------------------------------------------------------------------

/// Both renderings are written out rather than shared, deliberately: one function over both sides
/// could not tell a divergence from its own convention.
let rec private renderProdOp (op: SkeletonOp<RNode, string>) : string =
    match op with
    | InsertChild(p, node) ->
        "I|"
        + p
        + "|"
        + (Tree.preorder nodew node
           |> List.map (fun n -> encWitnessNode n.Id n.Kind)
           |> String.concat ",")
    | RemoveNode t -> "R|" + t
    | MoveNode(t, np) -> "M|" + t + "|" + np
    | ReorderChildren(p, order) -> "O|" + p + "|" + String.concat "," order
    | Batch inner -> "B|" + (inner |> List.map renderProdOp |> String.concat ";")

let rec private renderModelOp (op: TreeOps.op) : string =
    match op with
    | TreeOps.InsertChild(p, node) ->
        "I|"
        + p
        + "|"
        + (Tree.preorder modelTreeW node
           |> List.map (fun n -> encWitnessNode (mTid n) (mKind n))
           |> String.concat ",")
    | TreeOps.RemoveNode t -> "R|" + t
    | TreeOps.MoveNode(t, np) -> "M|" + t + "|" + np
    | TreeOps.ReorderChildren(p, order) -> "O|" + p + "|" + String.concat "," order
    | TreeOps.Batch inner -> "B|" + (inner |> List.map renderModelOp |> String.concat ";")

type private PresTally =
    {
        Diffs: string list
        /// Grafts the tree accepted, and the three disputed classes, counted separately so a run
        /// that reached none of them cannot report agreement about them.
        Accepted: int
        Rejected: int
        Collided: int
        Duplicated: int
        Inverted: int
        Classes: Set<string>
    }

let private emptyPresTally =
    { Diffs = []
      Accepted = 0
      Rejected = 0
      Collided = 0
      Duplicated = 0
      Inverted = 0
      Classes = Set.empty }

/// The disputed grafts, minted from the state's OWN ids so the collision is real rather than
/// hoped for. `existing` is the state's id list; `n` seeds the fresh names.
let private disputedInserts (existing: string list) (parent: string) (n: int) : SkeletonOp<RNode, string> list =
    let tag s = sprintf "p138-%s-%d" s n
    let victim = existing |> List.tryLast |> Option.defaultValue parent
    let root = List.head existing

    [
      // a DESCENDANT the tree already holds — invisible to the pre-137 check, which read the
      // graft's own id alone.
      InsertChild(parent, RNode.node (tag "shell") "section" [ RNode.leaf victim "para" "v" ])
      // the same, one level deeper, and naming the ROOT — the shape TreeOps' own counterexample uses
      InsertChild(
          parent,
          RNode.node (tag "outer") "section" [ RNode.node (tag "inner") "section" [ RNode.leaf root "para" "v" ] ]
      )
      // an id repeated WITHIN the graft, with nothing in common with the tree: the half no host
      // outside Core refuses at all (the TS, Go and Rust engines seed their comparison from the
      // root alone), and the half the pre-137 check could not see either.
      InsertChild(
          parent,
          RNode.node (tag "twins") "section" [ RNode.leaf (tag "twin") "para" "a"; RNode.leaf (tag "twin") "para" "b" ]
      )
      // both at once, so precedence is exercised: the FIRST offender in `Tree.ids` order is named
      InsertChild(
          parent,
          RNode.node (tag "both") "section" [ RNode.leaf victim "para" "a"; RNode.leaf (tag "both") "para" "b" ]
      )
      // and a clean multi-node graft, so the ACCEPT path is exercised with a subtree rather than a
      // leaf — otherwise a validator that refused every non-leaf graft would pass this family
      InsertChild(
          parent,
          RNode.node
              (tag "clean")
              "section"
              [ RNode.leaf (tag "clean-a") "para" "a"; RNode.leaf (tag "clean-b") "para" "b" ]
      ) ]

/// One (op, state) asked of both sides, across all four comparisons. `modelApply` is the model's
/// apply arm — the real one in the green run, `apply_pre137` in the go-red.
let private presProbe
    (modelApply: TreeOps.op -> TreeOps.tree -> DagFold.outcome<TreeOps.tree, TreeOps.rejection>)
    (op: SkeletonOp<RNode, string>)
    (st: RNode)
    (acc: PresTally)
    : PresTally =
    let mop = toModelOpWith toModelTree op
    let mst = toModelTree st
    let where = sprintf "op %s at tree %s" (renderProdOp op) (prodTreeHash st)

    let prod = Ops.apply nodew idw op st
    let model = modelApply mop mst
    let prodCan = Ops.canApply nodew idw op st
    let modelCan = Preservation.can_apply mop mst

    // 1. apply
    let applyDiff, accepted, rejected, cls =
        match prod, model with
        | Ok pt, DagFold.Ok mt ->
            let ph = prodTreeHash pt
            let mh = modelTreeHash mt

            (if ph <> mh then
                 [ sprintf "accepted result differs — %s\n  production: %s\n  oracle:     %s" where ph mh ]
             else
                 []),
            1,
            0,
            None
        | Error pe, DagFold.Error me ->
            let pc = prodRejClass pe
            let mc = modelRejClass me

            (if pc <> mc then
                 [ sprintf "rejection class differs — %s\n  production: %s\n  oracle:     %s" where pc mc ]
             else
                 []),
            0,
            1,
            Some pc
        | Ok _, DagFold.Error me ->
            [ sprintf "production ACCEPTED but the oracle rejected (%s) — %s" (modelRejClass me) where ], 0, 0, None
        | Error pe, DagFold.Ok _ ->
            [ sprintf "production REJECTED (%s) but the oracle accepted — %s" (prodRejClass pe) where ], 0, 0, None

    // 2. canApply, and 3. canApply-vs-apply on each side
    let canDiff =
        let pv =
            match prodCan with
            | Ok() -> "ok"
            | Error e -> prodRejClass e

        let mv =
            match modelCan with
            | DagFold.Ok() -> "ok"
            | DagFold.Error e -> modelRejClass e

        let differs =
            if pv <> mv then
                [ sprintf "canApply differs — %s\n  production: %s\n  oracle:     %s" where pv mv ]
            else
                []

        let prodSelf =
            if
                Result.isOk prodCan
                <> (match prod with
                    | Ok _ -> true
                    | Error _ -> false)
            then
                [ sprintf "production's canApply and apply DISAGREE — %s (canApply %s)" where pv ]
            else
                []

        let modelSelf =
            let mcOk =
                match modelCan with
                | DagFold.Ok() -> true
                | _ -> false

            let maOk =
                match model with
                | DagFold.Ok _ -> true
                | _ -> false

            if mcOk <> maOk then
                [ sprintf "the oracle's can_apply and apply DISAGREE — %s (can_apply %s)" where mv ]
            else
                []

        differs @ prodSelf @ modelSelf

    // 4. invert, on an accepted leaf operation
    let isLeaf =
        match op with
        | Batch _ -> false
        | _ -> true

    let invDiff, inverted =
        match prod, model with
        | Ok pt, DagFold.Ok mt when isLeaf ->
            let pInv = Ops.invert nodew idw op st
            let mInv = Preservation.invert_leaf mop mst

            let names =
                match pInv, mInv with
                | Ok pi, DagFold.Ok mi ->
                    let pr = renderProdOp pi
                    let mr = renderModelOp mi

                    if pr <> mr then
                        [ sprintf "the derived INVERSE differs — %s\n  production: %s\n  oracle:     %s" where pr mr ]
                    else
                        []
                | Error pe, DagFold.Error me ->
                    let pc = prodRejClass pe
                    let mc = modelRejClass me

                    if pc <> mc then
                        [ sprintf "invert's rejection class differs — %s\n  production: %s\n  oracle: %s" where pc mc ]
                    else
                        []
                | Ok _, DagFold.Error _
                | Error _, DagFold.Ok _ -> [ sprintf "invert's verdict differs — %s" where ]

            // the theorem instantiated on the shipped code: undoing an accepted step restores the
            // tree it was applied to, compared through the content hash rather than by equality of
            // a record the model does not model.
            let roundTrip =
                match pInv with
                | Ok pi ->
                    (match Ops.apply nodew idw pi pt with
                     | Ok back ->
                         if prodTreeHash back <> prodTreeHash st then
                             [ sprintf
                                   "the inverse did NOT restore the input — %s\n  before: %s\n  after:  %s"
                                   where
                                   (prodTreeHash st)
                                   (prodTreeHash back) ]
                         else
                             []
                     | Error e -> [ sprintf "the inverse was REJECTED at the result (%s) — %s" (prodRejClass e) where ])
                | Error _ -> []

            let mRoundTrip =
                match mInv with
                | DagFold.Ok mi ->
                    (match TreeOps.apply mi mt with
                     | DagFold.Ok back ->
                         if modelTreeHash back <> modelTreeHash mst then
                             [ sprintf "the ORACLE's inverse did not restore the input — %s" where ]
                         else
                             []
                     | DagFold.Error e ->
                         [ sprintf "the oracle's inverse was rejected at the result (%s) — %s" (modelRejClass e) where ])
                | DagFold.Error _ -> []

            names @ roundTrip @ mRoundTrip, 1
        | _ -> [], 0

    // sample adequacy: which DISPUTED shapes this probe actually reached, judged by re-reading the
    // graft through `Tree.ids` rather than by trusting the construction above.
    let collided, duplicated =
        match op with
        | InsertChild(_, node) ->
            let sub = Tree.ids nodew node
            let treeIds = Tree.ids nodew st |> Set.ofList
            let hits = sub |> List.filter treeIds.Contains |> List.length
            let internalDup = List.length sub <> List.length (List.distinct sub)
            (if hits > 0 then 1 else 0), (if internalDup then 1 else 0)
        | _ -> 0, 0

    { Diffs = acc.Diffs @ applyDiff @ canDiff @ invDiff
      Accepted = acc.Accepted + accepted
      Rejected = acc.Rejected + rejected
      Collided = acc.Collided + collided
      Duplicated = acc.Duplicated + duplicated
      Inverted = acc.Inverted + inverted
      Classes =
        match cls with
        | Some c -> Set.add c acc.Classes
        | None -> acc.Classes }

/// Every op the generator yields, the Phase 133 refusals, AND the disputed grafts minted per state,
/// asked at every state a prefix of the generated pool reaches.
///
/// Phase 139's `apply/` fixture family does not exist yet — it lands after this phase — so the pool
/// here is the generator plus the constructed grafts, and that is stated rather than implied. When
/// those fixtures land, this differential gains them as a third source.
let private presDifferential
    (modelApply: TreeOps.op -> TreeOps.tree -> DagFold.outcome<TreeOps.tree, TreeOps.rejection>)
    (seed: int)
    (trials: int)
    : PresTally =
    let mutable r = ConfRng.ofSeed seed
    let mutable tally = emptyPresTally
    let mutable n = 0

    for _ in 1..trials do
        let lanes, r' = treeLaneGen.Lanes 3 r
        r <- r'
        let generated = List.concat lanes

        let states =
            generated
            |> List.fold
                (fun (acc, cur) op ->
                    match Ops.apply nodew idw op cur with
                    | Ok t -> (acc @ [ t ]), t
                    | Error _ -> acc, cur)
                ([ treeBase ], treeBase)
            |> fst

        for st in states do
            n <- n + 1
            let existing = Tree.ids nodew st
            let parents = existing |> List.truncate 3

            let disputed = parents |> List.collect (fun p -> disputedInserts existing p n)

            for op in generated @ treeRefusals @ disputed do
                tally <- presProbe modelApply op st tally

    tally

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
              Expect.equal model production "the model enumerates the same interferences"

          // ---- the theorem's hypothesis, measured on the reference witness (Phase 132) ----

          testCase "the reference witness keeps the theorem's hypothesis: independent ops form a diamond"
          <| fun _ ->
              let breaks, met = treeDiamondSample treeFootprint 1320 250

              match breaks with
              | why :: _ -> failtestf "the reference witness BREAKS independence_diamond: %s" why
              | [] ->
                  // Sample adequacy, the pack's posture: a run that never met the premise
                  // measures nothing at all, and a run that met it a handful of times measures
                  // almost nothing. The threshold is far below what the sample delivers and is
                  // there to catch a generator that stops producing independent pairs.
                  Expect.isGreaterThan met 300 (sprintf "the sample met the diamond's premise in earnest (met=%d)" met)

          testCase "a blind footprint BREAKS the diamond over the same sample — the measurement can fail"
          <| fun _ ->
              // The teeth. A footprint that erases every address makes `independent` declare
              // EVERY pair independent, so genuinely-dependent pairs (an insert racing a reorder
              // of the same parent, an insert under a node another op removes) are measured and
              // must break. If this comes back clean, the green run above certifies nothing.
              let blind (_: SkeletonOp<RNode, string>) : Footprint =
                  { Reads = noAddr
                    StructureWrites = noAddr
                    ContentWrites = noAddr
                    UnknownParentWrites = noAddr }

              let breaks, met = treeDiamondSample blind 1320 250
              Expect.isGreaterThan met 0 "the blind run met the premise, so it had something to measure"

              match breaks with
              | [] ->
                  failtest
                      "a footprint declaring EVERY pair independent must break the diamond — otherwise this comparison cannot lose"
              | why :: _ -> Expect.stringContains why "independent" "the break names the pair it found"

          testCase "the reference algebra cannot keep rejection identity — the clause the diamond drops"
          <| fun _ ->
              // Phase 131's hypothesis demanded that independent ops commute at EVERY state,
              // REJECTIONS INCLUDED. `Rejection.UnknownNode` carries `addressable` — the whole id
              // set of the tree it was raised against — so that clause is unavailable here, and
              // this is the witness that keeps the claims ladder's "not claimed" honest. `a`
              // inserts under a real parent; `b` inserts under an absent one and always rejects.
              let a = InsertChild("a", RNode.leaf "fresh-132" "para" "v")
              let b = InsertChild("ghost-132", RNode.leaf "fresh-132b" "para" "v")

              Expect.isTrue
                  (Ops.independent (treeFootprint a) (treeFootprint b))
                  "the two inserts are footprint-independent — different parents, different fresh ids"

              let ab = treeW.Apply a treeBase |> Result.bind (treeW.Apply b)
              let ba = treeW.Apply b treeBase |> Result.bind (treeW.Apply a)

              match ab, ba with
              | Error(UnknownNode(t1, ids1)), Error(UnknownNode(t2, ids2)) ->
                  Expect.equal t1 t2 "both orders reject on the same absent parent"

                  Expect.notEqual
                      ids1
                      ids2
                      "but NOT with the same envelope — `addressable` is the tree at the moment of rejection"

                  Expect.isTrue
                      (List.contains "fresh-132" ids1)
                      "`a; b` rejects against a tree that already carries a's insert"

                  Expect.isFalse (List.contains "fresh-132" ids2) "`b; a` rejects against the tree before it"
              | _ -> failtestf "expected both orders to reject with UnknownNode; got %A / %A" ab ba

              // The DIAMOND is silent about this pair, and that is the whole of the change: `b`
              // never applies, so the premise is not met and nothing is claimed.
              match treeW.Apply b treeBase with
              | Error _ -> ()
              | Ok _ -> failtest "the witness requires `b` to reject at the base tree"

          // ---- Phase 135: the DECODE model beside Wire.Decode ----

          testCase "the decode oracle agrees with Wire.Decode over every nodes/ fixture"
          <| fun _ ->
              match SiblingCorpus.resolve "nodes" with
              | SiblingCorpus.SkippedByRequest why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let files = Directory.GetFiles(Path.Combine(root, "nodes"), "*.json") |> Array.sort
                  Expect.isGreaterThan files.Length 50 "the nodes/ family carries a real corpus"

                  files
                  |> Array.fold
                      (fun acc path ->
                          let name = Path.GetFileNameWithoutExtension path

                          match Json.parse (File.ReadAllText path) with
                          | Error e -> failtestf "%s: not JSON (%s)" name e
                          | Ok v -> runProbes toModel name v acc)
                      emptyTally
                  |> expectProbeAgreement "nodes/ fixtures"

          testCase "the decode oracle agrees with Wire.Decode over every ops/ fixture"
          <| fun _ ->
              match SiblingCorpus.resolve "ops" with
              | SiblingCorpus.SkippedByRequest why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let files = Directory.GetFiles(Path.Combine(root, "ops"), "*.json") |> Array.sort
                  Expect.isGreaterThan files.Length 8 "the ops/ family carries a real corpus"

                  files
                  |> Array.fold
                      (fun acc path ->
                          let name = Path.GetFileNameWithoutExtension path

                          match Json.parse (File.ReadAllText path) with
                          | Error e -> failtestf "%s: not JSON (%s)" name e
                          | Ok v -> runProbes toModel name v acc)
                      emptyTally
                  |> expectProbeAgreement "ops/ fixtures"

          testCase "the decode oracle agrees with Wire.Decode over a generated JVal sample"
          <| fun _ ->
              let mutable rng = ConfRng.ofSeed 8100
              let mutable tally = emptyTally

              for i in 1..400 do
                  let v, r' = genJ 0 rng
                  rng <- r'
                  tally <- runProbes toModel (sprintf "generated %d" i) v tally

              expectProbeAgreement "generated JVal sample" tally

          testCase "the model's node decoder inverts its encoder, on both sides"
          <| fun _ ->
              // `decode_encode_roundtrip` is proved on the model; this is that theorem's
              // instance on the EXTRACTED code, run beside the same decoder written from the
              // shipped combinators. The two together are what tie the proof to production.
              let mutable rng = ConfRng.ofSeed 6100
              let mutable decoded = 0

              for _ in 1..150 do
                  let n, r' = genRef 0 rng
                  rng <- r'
                  let el = encodeRef n

                  match refDiff el with
                  | Some d -> failtest d
                  | None -> ()

                  Expect.equal (decodeRef el) (Ok n) "production decodes its own encoding"
                  decoded <- decoded + 1

              Expect.isGreaterThan decoded 0 "the round-trip sample is non-empty"

          testCase "the model's node decoder agrees with production on documents it REFUSES"
          <| fun _ ->
              // The accept path is the round trip above; this is the refusal path, where the
              // MESSAGE is the whole content of the comparison. Corpus fixtures first (real
              // documents in a vocabulary this decoder does not know), then a generated sample
              // whose key alphabet is the reference vocabulary's own, so the near-misses — right
              // tag, wrong member type — are reached as well as the obvious rejects.
              let mutable refused = 0

              let check (el: JVal) =
                  match refDiff el with
                  | Some d -> failtest d
                  | None ->
                      match decodeRef el with
                      | Error _ -> refused <- refused + 1
                      | Ok _ -> ()

              match SiblingCorpus.resolve "nodes" with
              | SiblingCorpus.SkippedByRequest why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  for path in Directory.GetFiles(Path.Combine(root, "nodes"), "*.json") |> Array.sort do
                      match Json.parse (File.ReadAllText path) with
                      | Error e -> failtestf "%s: not JSON (%s)" (Path.GetFileNameWithoutExtension path) e
                      | Ok v ->
                          for el in everyValue v do
                              check el

              let mutable rng = ConfRng.ofSeed 6200

              for _ in 1..400 do
                  let v, r' = genJ 0 rng
                  rng <- r'

                  for el in everyValue v do
                      check el

              Expect.isGreaterThan refused 0 "documents were refused, so the message comparison ran"

          testCase "the model's null-policy reader agrees with the parser under BOTH policies"
          <| fun _ ->
              // The Phase 102 promise, tied to production. The model reads a document TREE where
              // the parser reads text, so each generated document is rendered to the wire bytes
              // the parser actually takes — see `renderN` and the boundary note in
              // proofs/README.md for what that comparison does and does not establish.
              let fixtures: MJValN list =
                  [ WireDecode.NNull
                    WireDecode.NArr [ WireDecode.NNull ]
                    WireDecode.NArr [ WireDecode.NInt 1; WireDecode.NNull ]
                    WireDecode.NObj [ "a", WireDecode.NNull ]
                    WireDecode.NObj [ "a", WireDecode.NNull; "b", WireDecode.NInt 1 ]
                    WireDecode.NObj [ "a", WireDecode.NObj [ "b", WireDecode.NNull ] ]
                    WireDecode.NObj [ "a", WireDecode.NArr [ WireDecode.NNull ] ] ]

              let mutable t =
                  { Diffs = []
                    StrictOk = 0
                    StrictErr = 0
                    Fired = 0 }

              for d in fixtures do
                  t <- policyProbe d t

              let mutable rng = ConfRng.ofSeed 7100

              for _ in 1..400 do
                  let d, r' = genN 0 rng
                  rng <- r'
                  t <- policyProbe d t

              match t.Diffs with
              | d :: _ -> failtestf "the read-policy oracle and the parser DISAGREE\n%s" d
              | [] ->
                  Expect.isGreaterThan t.StrictOk 0 "some documents were accepted"
                  Expect.isGreaterThan t.StrictErr 0 "some documents were refused"
                  Expect.isGreaterThan t.Fired 0 "the policy FIRED — strict refused where tolerant accepted"

          testCase "a decode oracle handed a blind bridge DISAGREES with Wire.Decode"
          <| fun _ ->
              // The teeth. The blind bridge reads every wire integer as a float, so the oracle is
              // asked about a different value from the one production was asked about: `asInt`
              // must lose. A green report above is therefore a comparison that CAN fail.
              let tally = runProbes toModelBlind "blind" (JObj [ "n", JInt 7 ]) emptyTally

              match tally.Disagreements with
              | [] ->
                  failtest
                      "a blind bridge reads every integer as a float, so asInt must disagree — this comparison cannot lose"
              | ds ->
                  Expect.isTrue
                      (ds |> List.exists (fun d -> d.Contains "asInt"))
                      (sprintf "the disagreement names the arm that moved — got:\n%s" (List.head ds))

                  // … and it loses over the generated sample too, not only on a hand-made value.
                  let mutable rng = ConfRng.ofSeed 9100
                  let mutable swept = emptyTally

                  for _ in 1..60 do
                      let v, r' = genJ 0 rng
                      rng <- r'
                      swept <- runProbes toModelBlind "blind" v swept

                  Expect.isNonEmpty swept.Disagreements "the blind bridge loses over the generated sample as well"

          // ---- Phase 134: production's DELTA RECOVERY beside the model's ----

          testCase "production's betweenOps recovers the model's delta over the reference witness"
          <| fun _ ->
              deltaDifferential "reference witness" unperturbed treeW treeLaneGen 3 1340 120
              |> expectDeltaAgreement "reference witness, 3 lanes"

          testCase "production's betweenOps recovers the model's delta over the reference witness at 4 lanes"
          <| fun _ ->
              deltaDifferential "reference witness" unperturbed treeW treeLaneGen 4 1341 60
              |> expectDeltaAgreement "reference witness, 4 lanes"

          testCase "production's betweenOps recovers the model's delta over the work-plan domain"
          <| fun _ ->
              deltaDifferential "work-plan domain" unperturbed planW planLaneGen 3 1342 150
              |> expectDeltaAgreement "work-plan domain, 3 lanes"

          testCase "production's betweenOps recovers the model's delta over the work-plan domain at 5 lanes"
          <| fun _ ->
              deltaDifferential "work-plan domain" unperturbed planW planLaneGen 5 1343 40
              |> expectDeltaAgreement "work-plan domain, 5 lanes"

          testCase "production's betweenOps recovers the model's delta over the corpus ops pool"
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
                  let mutable recovered = 0
                  let mutable chains = 0

                  let check (lanes: CorpusOp list list) =
                      if lanes |> List.exists (fun l -> List.length l > 1) then
                          chains <- chains + 1

                      match deltaDisagreements unperturbed corpusW baseOp lanes with
                      | [], got -> recovered <- recovered + got
                      | d :: _, _ -> failtestf "corpus lanes\n%s\n%s" (renderLanes corpusW.Encode lanes) d

                  for i in 0 .. n - 1 do
                      for j in i + 1 .. n - 1 do
                          check [ [ List.item i ops ]; [ List.item j ops ] ]

                          for k in j + 1 .. n - 1 do
                              check [ [ List.item i ops ]; [ List.item j ops ]; [ List.item k ops ] ]

                  // Multi-op lanes are where the WALK is exercised: a one-node chain is recovered
                  // by a single step and would certify almost nothing about the closure.
                  for i in 0 .. n - 2 do
                      check [ [ List.item i ops; List.item (i + 1) ops ]; [ List.item ((i + 2) % n) ops ] ]

                  Expect.isGreaterThan recovered 0 "ops were recovered"
                  Expect.isGreaterThan chains 0 "multi-op lanes were walked"

          testCase "the oracle's DAG-shaped fold agrees with production over the work-plan domain"
          <| fun _ ->
              // `fold_once_dag` is the Phase 134 entry point: the model doing the WHOLE thing from
              // the content-addressed DAG — recovering each lane's delta, sweeping the pairs,
              // composing and replaying — where `fold_once` is handed the deltas. Compared against
              // production end to end, and against the deltas-first oracle beside it, so a
              // disagreement says which of the two halves moved.
              let mutable rng = ConfRng.ofSeed 1344
              let mutable folded = 0
              let mutable halted = 0

              for i in 1..120 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'

                  let p =
                      productionFold planW planFootprint planHash planLaneGen.State0 planLaneGen.BaseOp lanes

                  let o =
                      oracleFoldFromDag planW planFootprint planHash planLaneGen.State0 planLaneGen.BaseOp lanes

                  let deltasFirst = oracleFold planW planFootprint planHash planLaneGen.State0 lanes

                  if p <> o then
                      failtestf
                          "iter %d: the DAG-shaped oracle and production differ\n%s\n  production: %A\n  oracle:     %A"
                          i
                          (renderLanes encPlanOp lanes)
                          p
                          o

                  if o <> deltasFirst then
                      failtestf
                          "iter %d: fold_once_dag and fold_once differ on the same lanes\n%s\n  from the DAG: %A\n  from deltas:  %A"
                          i
                          (renderLanes encPlanOp lanes)
                          o
                          deltasFirst

                  match p with
                  | LaneFolded _ -> folded <- folded + 1
                  | LaneHalted _ -> halted <- halted + 1
                  | LaneRejected _ -> ()

              Expect.isGreaterThan folded 0 "some lane sets folded"
              Expect.isGreaterThan halted 0 "some lane sets halted"

          testCase "a model DAG whose lanes hang off the WRONG parent disagrees with production"
          <| fun _ ->
              // The teeth. Re-parenting each lane's first node onto the previous lane's head makes
              // the model walk a longer spine than the DAG has, so it recovers a longer delta. If
              // this comes back clean, the green cases above certify nothing: they would be
              // comparing a recovery that cannot notice the shape it walks.
              let mutable rng = ConfRng.ofSeed 1342
              let mutable found = 0
              let mutable example = ""

              for _ in 1..150 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'

                  match deltaDisagreements reparented planW planLaneGen.BaseOp lanes with
                  | [], _ -> ()
                  | d :: _, _ ->
                      found <- found + 1

                      if example = "" then
                          example <- d

              Expect.isGreaterThan
                  found
                  0
                  "a DAG whose lanes hang off the wrong parent must recover different deltas — this comparison cannot lose"

              Expect.stringContains example "the recovered delta differs" "the disagreement names what moved"
          // ---- Phase 133: the TREE ALGEBRA model beside Ops.apply / Ops.footprint ----

          testCase "the tree oracle agrees with Ops.apply and Ops.footprint over the generated pool"
          <| fun _ ->
              let t = treeDifferential toModelTree 1330 30

              match t.Diffs with
              | d :: _ -> failtestf "the tree oracle and production DISAGREE\n%s" d
              | [] ->
                  // The pack's sample-adequacy posture: an agreement that never met a rejection
                  // certifies one arm only, and one that never met an acceptance certifies none.
                  Expect.isGreaterThan
                      t.Accepted
                      0
                      (sprintf "(op, state) pairs were ACCEPTED (accepted=%d rejected=%d)" t.Accepted t.Rejected)

                  Expect.isGreaterThan
                      t.Rejected
                      0
                      (sprintf "(op, state) pairs were REJECTED (accepted=%d rejected=%d)" t.Accepted t.Rejected)

                  // Every rejection class the plain `apply` can raise was reached — named rather
                  // than counted, because a count cannot say WHICH arm went unexercised.
                  // `NotAContainer` is `applyContained`'s alone and `Rejected` is the domain-side
                  // extension point Core never raises, so the reachable set is these five.
                  for cls in
                      [ "UnknownNode"
                        "DuplicateId"
                        "CannotRemoveRoot"
                        "WouldNestUnderSelf"
                        "ReorderMismatch" ] do
                      Expect.isTrue
                          (Set.contains cls t.Classes)
                          (sprintf "the sample reached a %s rejection (reached: %A)" cls t.Classes)

          testCase "a tree oracle handed a blind bridge DISAGREES with Ops.apply"
          <| fun _ ->
              // The teeth. The blind bridge erases every kind tag, so the model is asked about a
              // different tree from the one production was asked about. The VERDICTS still agree —
              // no clause in the algebra reads a kind — so what has to lose is the accepted-result
              // hash, which is otherwise the half a coincidence could carry. A green report above
              // is therefore known to be a comparison that can fail.
              let t = treeDifferential toModelTreeBlind 1330 3

              Expect.isNonEmpty t.Diffs "a bridge that erases every kind tag must lose the result comparison"

              Expect.isTrue
                  (t.Diffs |> List.exists (fun d -> d.Contains "accepted result differs"))
                  (sprintf "the disagreement names the arm that moved — got:\n%s" (List.head t.Diffs))

          testCase "the extracted tree model keeps the diamond its own theorem proves"
          <| fun _ ->
              // `TreeOps.leaf_independence_diamond` is proved on the model; this is that theorem's
              // instance on the EXTRACTED code, over the pool Phase 80's generator produces. The
              // alphabet is the non-`Batch` ops, which is the alphabet the theorem is stated over.
              let mutable r = ConfRng.ofSeed 1331
              let mutable breaks = []
              let mutable met = 0

              for _ in 1..60 do
                  let lanes, r' = treeLaneGen.Lanes 3 r
                  r <- r'
                  let ops = List.concat lanes

                  let states =
                      ops
                      |> List.fold
                          (fun (acc, cur) op ->
                              match Ops.apply nodew idw op cur with
                              | Ok t -> (acc @ [ t ]), t
                              | Error _ -> acc, cur)
                          ([ treeBase ], treeBase)
                      |> fst

                  let mops =
                      ops
                      |> List.filter (fun o ->
                          match o with
                          | Batch _ -> false
                          | _ -> true)
                      |> List.map (toModelOpWith toModelTree)

                  let b, m = modelDiamondBreaks TreeOps.op_fp mops (states |> List.map toModelTree)
                  breaks <- breaks @ b
                  met <- met + m

              match breaks with
              | why :: _ -> failtest why
              | [] ->
                  // The premise here is narrower than the Phase 132 family's: `wapply` demands a
                  // well-formed state AND a well-formed result, so a (pair, state) triple counts
                  // only where both halves of the guarded algebra accept. Measured at 60 trials:
                  // 155. The threshold is below that with room and is there to catch a generator
                  // that stops producing independent pairs, not to pin the number.
                  Expect.isGreaterThan met 100 (sprintf "the sample met the diamond's premise in earnest (met=%d)" met)

          testCase "a blind footprint BREAKS the extracted model's diamond — the measurement can fail"
          <| fun _ ->
              // The teeth for the family above, on a pair chosen so the break is not a matter of
              // luck: two inserts under the SAME parent both apply and do not commute (the appended
              // order differs), and `Ops.independent` correctly refuses them. A footprint that
              // declares every pair independent must therefore break the diamond here.
              let blind (_: TreeOps.op) : DagFold.footprint =
                  { DagFold.reads = []
                    DagFold.structure_writes = []
                    DagFold.content_writes = []
                    DagFold.unknown_parent_writes = [] }

              let x = TreeOps.InsertChild("a", TreeOps.TNode("x133", "para", []))
              let y = TreeOps.InsertChild("a", TreeOps.TNode("y133", "para", []))
              let s0 = toModelTree treeBase

              Expect.isFalse
                  (DagFold.independent (TreeOps.op_fp x) (TreeOps.op_fp y))
                  "the two same-parent inserts are NOT independent — the shared structural parent"

              let breaks, met = modelDiamondBreaks blind [ x; y ] [ s0 ]
              Expect.isGreaterThan met 0 "the blind run met the premise, so it had something to measure"

              Expect.isNonEmpty
                  breaks
                  "a footprint declaring EVERY pair independent must break the diamond — otherwise this measurement cannot lose"

          // ---- Phase 143 — the precision ceiling: the pinned unknown-parent clause is NECESSARY ----

          testCase "the pinned unknown-parent clause is necessary — both classes, on the model and on production"
          <| fun _ ->
              // `proofs/TreeOps.fst` section 18 proves this; this is that section's witness run on
              // the EXTRACTED model and, in the same case, on PRODUCTION `Ops.footprint` /
              // `Ops.independent` over the reference witness. Two classes are named:
              //
              //   NOW-INDEPENDENT-IF-THE-CLAUSE-WERE-DROPPED — a `MoveNode` and an `InsertChild`
              //     under a parent inside the moved subtree. They DO commute; only the pinned
              //     clause refuses them.
              //   STILL-REFUSED-AND-RIGHTLY — the same shape with a `RemoveNode` in it. It does NOT
              //     commute: the insert's parent goes with the destroyed subtree.
              //
              // And the reason the first cannot be freed: the two ops carry the SAME footprint, so
              // any predicate over the four address sets gives them one verdict. This case goes RED
              // if `Ops.independent` is ever tightened, which is the point of writing it down.

              // ---- the model half ----
              Expect.equal
                  (TreeOps.op_fp TreeOps.reloc_move)
                  (TreeOps.op_fp TreeOps.reloc_remove)
                  "the move and the remove-shaped batch are footprint-INDISTINGUISHABLE on the model"

              Expect.isFalse
                  (DagFold.independent (TreeOps.op_fp TreeOps.reloc_move) (TreeOps.op_fp TreeOps.reloc_insert))
                  "the model refuses the pair — the pinned clause is the only clause doing so"

              let modelOrder (first: TreeOps.op) (second: TreeOps.op) =
                  match TreeOps.apply first TreeOps.reloc_tree with
                  | DagFold.Ok t -> TreeOps.apply second t
                  | e -> e

              Expect.equal
                  (modelOrder TreeOps.reloc_move TreeOps.reloc_insert)
                  (modelOrder TreeOps.reloc_insert TreeOps.reloc_move)
                  "NOW-INDEPENDENT class: the move and the insert commute on the model"

              Expect.isTrue
                  (match modelOrder TreeOps.reloc_remove TreeOps.reloc_insert with
                   | DagFold.Error _ -> true
                   | _ -> false)
                  "STILL-REFUSED class: the remove-shaped batch destroys the insert's parent"

              // ---- the production half: the same witness over the reference witness ----
              let pTree =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "x" "section" [ RNode.node "p" "section" [] ]
                        RNode.node "q" "section" [] ]

              let pMove: SkeletonOp<RNode, string> = MoveNode("x", "q")

              let pRemove: SkeletonOp<RNode, string> =
                  Batch [ RemoveNode "x"; ReorderChildren("q", []) ]

              let pInsert: SkeletonOp<RNode, string> = InsertChild("p", RNode.node "n" "para" [])

              let fp (o: SkeletonOp<RNode, string>) = Ops.footprint nodew idw [ o ]

              Expect.equal (fp pMove) (fp pRemove) "production agrees: the two ops carry the same four address sets"

              // every clause EXCEPT the pinned pair holds — so the pinned pair is load-bearing here
              let fpM, fpI = fp pMove, fp pInsert
              let disjoint (a: Set<string>) b = Set.isEmpty (Set.intersect a b)

              Expect.isTrue
                  (disjoint fpM.ContentWrites fpI.ContentWrites
                   && disjoint fpM.ContentWrites fpI.Reads
                   && disjoint fpI.ContentWrites fpM.Reads
                   && disjoint fpM.StructureWrites fpI.StructureWrites)
                  "every clause but the pinned relocation pair is satisfied — nothing else is refusing this"

              Expect.isFalse (Ops.independent fpM fpI) "production refuses the pair, by the pinned clause alone"

              let prodOrder first second =
                  match Ops.apply nodew idw first pTree with
                  | Ok t -> Ops.apply nodew idw second t
                  | e -> e

              Expect.equal
                  (prodOrder pMove pInsert)
                  (prodOrder pInsert pMove)
                  "NOW-INDEPENDENT class, in production: the move and the insert commute"

              Expect.isTrue
                  (match prodOrder pRemove pInsert with
                   | Error _ -> true
                   | Ok _ -> false)
                  "STILL-REFUSED class, in production: remove-then-insert cannot find the insert's parent"

              Expect.isTrue
                  (match Ops.apply nodew idw pRemove pTree, Ops.apply nodew idw pInsert pTree with
                   | Ok _, Ok _ -> true
                   | _ -> false)
                  "both halves apply at the tree on their own — so the divergence is the pair's, not one op's"

          testCase "a move pair nesting into each other's subtrees is refused, and no record could free it"
          <| fun _ ->
              // The second, independent reason the refused set is not one homogeneous class waiting
              // on a better footprint. `TreeOps.relocation_move_pair_also_fails` proves it; here it
              // is on the extracted model and on production.
              let modelStep (first: TreeOps.op) (second: TreeOps.op) =
                  match TreeOps.apply first TreeOps.cross_tree with
                  | DagFold.Ok t -> TreeOps.apply second t
                  | e -> e

              Expect.isTrue
                  (match
                      TreeOps.apply TreeOps.cross_a TreeOps.cross_tree, TreeOps.apply TreeOps.cross_b TreeOps.cross_tree
                   with
                   | DagFold.Ok _, DagFold.Ok _ -> true
                   | _ -> false)
                  "each move applies on its own"

              Expect.isTrue
                  (match modelStep TreeOps.cross_a TreeOps.cross_b with
                   | DagFold.Error _ -> true
                   | _ -> false)
                  "after either, the other would nest a node under its own subtree — the cycle check refuses it"

              let cTree =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "x" "section" [ RNode.node "mp" "section" [] ]
                        RNode.node "y" "section" [ RNode.node "np" "section" [] ] ]

              let a: SkeletonOp<RNode, string> = MoveNode("x", "np")
              let b: SkeletonOp<RNode, string> = MoveNode("y", "mp")
              let fp (o: SkeletonOp<RNode, string>) = Ops.footprint nodew idw [ o ]

              Expect.isFalse (Ops.independent (fp a) (fp b)) "production refuses the move pair"

              Expect.isTrue
                  (match Ops.apply nodew idw a cTree with
                   | Ok t ->
                       match Ops.apply nodew idw b t with
                       | Error _ -> true
                       | Ok _ -> false
                   | Error _ -> false)
                  "and it is genuinely dependent — the obstruction is the cycle check, not the addresses"

          // ---- Phase 136 — the two integrity walkers, and the tampers they are for ----

          testCase "the chain oracle agrees with production over the reference witness's DAG and every tamper of it"
          <| fun _ ->
              dagDifferential
                  "reference witness"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  treeW
                  (fun _ -> RemoveNode "tampered-node")
                  treeLaneGen
                  3
                  3600
                  60
              |> expectWalkerAgreement "reference witness DAG"

          testCase "the chain oracle agrees with production over the work-plan DAG and every tamper of it"
          <| fun _ ->
              dagDifferential
                  "work-plan domain"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  planW
                  tamperedPlanOp
                  planLaneGen
                  3
                  3610
                  60
              |> expectWalkerAgreement "work-plan DAG"

          testCase "the chain oracle agrees with production over the linear op-stream and every tamper of it"
          <| fun _ ->
              chainDifferential
                  "work-plan chain"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  planW
                  tamperedPlanOp
                  planLaneGen
                  3620
                  80
              |> expectWalkerAgreement "work-plan chain"

          testCase "the chain oracle agrees with production over the reference witness's linear op-stream"
          <| fun _ ->
              chainDifferential
                  "reference chain"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  treeW
                  (fun _ -> RemoveNode "tampered-node")
                  treeLaneGen
                  3630
                  60
              |> expectWalkerAgreement "reference chain"

          // ---- the teeth: both comparisons can lose ----

          testCase "a model handed a DIFFERENT hash reports a break where production sees none"
          <| fun _ ->
              // The go-red for the DAG comparison. Nothing about the DAG moves; only the function
              // the model recomputes ids with. If this came back clean the green cases above would
              // be comparing a walker that cannot notice the pre-image it hashes.
              let lanes, _ = planLaneGen.Lanes 3 (ConfRng.ofSeed 3640)
              let dag = dagUnder OpStream.defaultHash planW planLaneGen.BaseOp lanes
              let p, m = dagVerdicts OpStream.defaultHash swappedHash planW dag
              Expect.equal p DagIntact "production built this DAG and sees it intact"
              Expect.notEqual m p "a model recomputing ids with a different hash must disagree"

              Expect.equal
                  (dagVerdictReason m)
                  (Some ContentIdMismatch)
                  "and it disagrees by naming the check that failed"

          testCase "a model handed a DIFFERENT hash reports a break on an intact linear chain"
          <| fun _ ->
              let lanes, _ = planLaneGen.Lanes 2 (ConfRng.ofSeed 3650)

              let rs =
                  chainUnder OpStream.defaultHash planW planLaneGen.State0 (Human "writer") (List.concat lanes)

              Expect.isNonEmpty rs "the generated lane produced a chain"
              let p, m = chainVerdicts OpStream.defaultHash swappedHash planW rs
              Expect.equal p ChainIntact "production built this chain and sees it intact"
              Expect.notEqual m p "a model recomputing hashes with a different function must disagree"

              Expect.equal (chainVerdictReason m) (Some HashMismatch) "and it disagrees by naming the check that failed"

          // ---- what the theorem's one premise buys, measured in BOTH directions ----

          testCase "under a NON-INJECTIVE hash the tamper is invisible — to production and to the model alike"
          <| fun _ ->
              // `tamper_detected` rests on exactly one hypothesis: the content id determines the
              // content. `opBlindHash` breaks it in the smallest way that is still a hash — it
              // folds the parents and the actor and drops the op — so two nodes differing only in
              // their op mint one id. Run the SAME tamper both ways: under the real hash it is
              // found, under the collapsing one it is not, and the model tracks production in both
              // directions. That is the premise shown to be load-bearing rather than decorative.
              let lane = [ AddItem("z1", "one"); AddItem("z2", "two") ]

              let tamper (dag: Dag.T<PlanOp>) : Dag.T<PlanOp> =
                  let k, n =
                      dag.Nodes |> Map.toList |> List.find (fun (_, n) -> n.Op = AddItem("z1", "one"))

                  { dag with
                      Nodes =
                          Map.add
                              k
                              { n with
                                  Op = AddItem("z1", "TAMPERED") }
                              dag.Nodes }

              let real = dagUnder OpStream.defaultHash planW planLaneGen.BaseOp [ lane ]
              let weak = dagUnder opBlindHash planW planLaneGen.BaseOp [ lane ]

              // The probe did the thing it claims to be about: two different hashes, two different
              // sets of ids. Without this the case could be comparing one DAG with itself.
              Expect.notEqual (Dag.heads real) (Dag.heads weak) "the two hashes mint different ids"

              let rp, rm =
                  dagVerdicts OpStream.defaultHash OpStream.defaultHash planW (tamper real)

              Expect.equal (dagVerdictReason rp) (Some ContentIdMismatch) "under an injective hash the tamper is found"

              Expect.equal rm rp "and the model finds it identically"

              let wp, wm = dagVerdicts opBlindHash opBlindHash planW (tamper weak)
              Expect.equal wp DagIntact "a hash that drops the op cannot see an op tamper — this is the premise"
              Expect.equal wm wp "and neither can the model, under the same hash"

          // ---- the corpus dag/ family: its SHAPES, not its addresses ----

          testCase "the corpus dag/ family's shapes, rebuilt through production's own append and merge"
          <| fun _ ->
              match SiblingCorpus.resolve "dag" with
              | SiblingCorpus.SkippedByRequest why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let shapes =
                      match readDagShapes root with
                      | Ok s -> s
                      | Error why -> failtestf "a dag/ fixture could not be read: %s" why

                  Expect.isGreaterThan (List.length shapes) 3 "the dag/ family carries its four shapes"

                  // THE BOUNDARY, as an assertion rather than a sentence, so it goes red if it ever
                  // moves. This family's addresses belong to the UI host's DAG-record wire format —
                  // 64 characters, minted by a different pre-image under SHA-256 — where Core's
                  // default content id is 8. They are not Core content ids and cannot be made into
                  // one; what is used below is the SHAPES the fixtures carry.
                  Expect.equal (OpStream.defaultHash "" "").Length 8 "Core's default content id is 8 characters"

                  for s in shapes do
                      Expect.equal
                          s.CorpusHash.Length
                          64
                          (sprintf "%s: a dag/ address is 64 characters, so it is not a Core content id" s.Fixture)

                  // Rebuild each shape through production: no parents (genesis), one (a linear
                  // step), two (a MERGE — the shape the generated pools never build, because
                  // `foldOnce` only ever grows chains off one base).
                  let mutable dag: Dag.T<string> = Dag.empty
                  let mutable built: (string * string * string) list = []
                  let mutable merges = 0

                  let coreIdOf (h: string) =
                      built |> List.tryPick (fun (_, ch, id) -> if ch = h then Some id else None)

                  let isBuilt (f: string) =
                      built |> List.exists (fun (bf, _, _) -> bf = f)

                  for want in [ 0; 1; 2 ] do
                      for s in shapes do
                          if List.length s.CorpusParents = want && not (isBuilt s.Fixture) then
                              let id, d =
                                  match s.CorpusParents |> List.map coreIdOf with
                                  | [] -> Dag.append OpStream.defaultHash rawW s.Actor s.OpJson "" dag
                                  | [ Some p ] -> Dag.append OpStream.defaultHash rawW s.Actor s.OpJson p dag
                                  | [ Some l; Some r ] ->
                                      merges <- merges + 1
                                      Dag.merge OpStream.defaultHash rawW s.Actor s.OpJson l r dag
                                  | _ -> failtestf "%s: it names a parent no earlier fixture built" s.Fixture

                              dag <- d
                              built <- built @ [ s.Fixture, s.CorpusHash, id ]

                  Expect.equal (List.length built) (List.length shapes) "every shape was rebuilt"

                  Expect.isGreaterThan
                      merges
                      0
                      "the two-parent MERGE shape was exercised — the one the generated pools never build"

                  match mintsProductionIds OpStream.defaultHash rawW dag with
                  | Some why -> failtest why
                  | None -> ()

                  let p, m = dagVerdicts OpStream.defaultHash OpStream.defaultHash rawW dag
                  Expect.equal p DagIntact "the rebuilt DAG is intact"
                  Expect.equal m p "and the model agrees"

                  let mutable detected = 0

                  for (what, tampered) in dagTampers (fun (o: string) -> o + " ") dag do
                      let tp, tm = dagVerdicts OpStream.defaultHash OpStream.defaultHash rawW tampered
                      Expect.equal tm tp (sprintf "tamper %s: the two walkers disagree" what)

                      if tp <> DagIntact then
                          detected <- detected + 1

                  Expect.isGreaterThan detected 0 "the tampers were found, so the agreement is not vacuous"

                  // Phase 64.1 on the shape only this family supplies: a merge node's id does not
                  // depend on which head the reconciler called left. This is the production side of
                  // the model's `merge_id_parent_order_independent`, and the last assertion is the
                  // model's own pre-image measured against the id production minted.
                  match built with
                  | (_, _, a) :: (_, _, b) :: _ ->
                      let lr, _ = Dag.merge OpStream.defaultHash rawW (Human "m") "{}" a b Dag.empty
                      let rl, _ = Dag.merge OpStream.defaultHash rawW (Human "m") "{}" b a Dag.empty
                      Expect.equal lr rl "merge(A,B) and merge(B,A) converge to one content id"

                      Expect.equal
                          (Chain.node_hash
                              OpStream.defaultHash
                              rawW.Encode
                              ordinalLe
                              [ b; a ]
                              (Actor.encode (Human "m"))
                              "{}")
                          lr
                          "and the model mints that same id from the reversed parent list"
                  | _ -> failtest "two rebuilt nodes are needed to exercise a merge"

          // ---- the PARSER (Phase 146) — the boundary theorem 1 named ----

          testCase "the parser oracle agrees with production over the near-miss table"
          <| fun _ ->
              JsonParseDiff.sweep RejectNull 512 JsonParseDiff.nearMisses
              |> JsonParseDiff.expectAgreement "near misses, strict"

          testCase "… and under the tolerant read policy, where the member-null fork lives"
          <| fun _ ->
              JsonParseDiff.sweep EraseMemberNull 512 JsonParseDiff.nearMisses
              |> JsonParseDiff.expectAgreement "near misses, tolerant"

              // The fork is asserted to have FIRED, because two parsers that never met a member
              // null agree about a policy neither exercised.
              Expect.equal
                  (JsonParseDiff.prodAnswer EraseMemberNull 512 "{\"a\":null,\"b\":1}")
                  "ok {b=i:1}"
                  "the tolerant policy erases a member null"

              Expect.notEqual
                  (JsonParseDiff.prodAnswer RejectNull 512 "{\"a\":null,\"b\":1}")
                  (JsonParseDiff.prodAnswer EraseMemberNull 512 "{\"a\":null,\"b\":1}")
                  "and the strict policy does not — the two policies were actually distinguished"

          testCase "the parser oracle agrees with production over every corpus nodes/ fixture"
          <| fun _ ->
              JsonParseDiff.corpusTexts "nodes"
              |> JsonParseDiff.sweep RejectNull 512
              |> fun (bad, accepted, _) ->
                  if not bad.IsEmpty then
                      failtestf
                          "corpus nodes/: the model and production disagree on %d fixture(s). First 5:\n%s"
                          bad.Length
                          (bad |> List.truncate 5 |> String.concat "\n")

                  // Every fixture is valid wire JSON, so this pool is the ACCEPT path and has no
                  // refusals to assert — saying so here rather than letting the shared guard
                  // demand a refusal that would mean the corpus was broken.
                  Expect.isGreaterThan accepted 100 "the corpus nodes/ pool is far smaller than expected"

          testCase "… and over every corpus ops/ fixture"
          <| fun _ ->
              JsonParseDiff.corpusTexts "ops"
              |> JsonParseDiff.sweep RejectNull 512
              |> fun (bad, accepted, _) ->
                  if not bad.IsEmpty then
                      failtestf
                          "corpus ops/: the model and production disagree on %d fixture(s). First 5:\n%s"
                          bad.Length
                          (bad |> List.truncate 5 |> String.concat "\n")

                  Expect.isGreaterThan accepted 10 "the corpus ops/ pool is far smaller than expected"

          testCase "the parser oracle agrees with production over generated deep and wide inputs"
          <| fun _ ->
              // Both nesting families at every depth from 0 to 8, under caps that straddle each —
              // so the boundary itself is compared, not merely the interior.
              let deep =
                  [ for cap in 0..8 do
                        for k in 0..8 do
                            yield sprintf "arr k=%d cap=%d" k cap, (cap, JsonParseDiff.nestArr k)
                            yield sprintf "obj k=%d cap=%d" k cap, (cap, JsonParseDiff.nestObj k) ]

              let bad =
                  [ for (name, (cap, input)) in deep do
                        let p = JsonParseDiff.prodAnswer RejectNull cap input

                        let m =
                            JsonParseDiff.modelAnswer
                                JsonParseDiff.toChs
                                RejectNull
                                cap
                                (JsonParseDiff.budget cap)
                                input

                        if p <> m then
                            yield sprintf "%s:\n    production %s\n    model      %s" name p m ]

              if not bad.IsEmpty then
                  failtestf
                      "deep/wide: %d disagreement(s). First 5:\n%s"
                      bad.Length
                      (bad |> List.truncate 5 |> String.concat "\n")

              // The boundary was actually crossed in both directions.
              Expect.stringStarts
                  (JsonParseDiff.prodAnswer RejectNull 3 (JsonParseDiff.nestArr 3))
                  "ok "
                  "a document nested to the cap is accepted"

              Expect.stringContains
                  (JsonParseDiff.prodAnswer RejectNull 3 (JsonParseDiff.nestArr 4))
                  "MaxDepthExceeded"
                  "and one nested past it is refused by name"

              let wide =
                  [ for n in [ 0; 1; 2; 10; 64 ] do
                        yield sprintf "wide array %d" n, "[" + String.concat "," [ for i in 1..n -> string i ] + "]"

                        yield
                            sprintf "wide object %d" n,
                            "{" + String.concat "," [ for i in 1..n -> sprintf "\"k%d\":%d" i i ] + "}" ]

              JsonParseDiff.sweep RejectNull 512 (wide @ [ "refusal", "[" ])
              |> JsonParseDiff.expectAgreement "wide"

          testCase "the parser oracle agrees with production over 4000 generated inputs"
          <| fun _ ->
              JsonParseDiff.sweep RejectNull 512 (JsonParseDiff.soup 20260914 2000)
              |> JsonParseDiff.expectAgreement "soup, strict"

              JsonParseDiff.sweep EraseMemberNull 512 (JsonParseDiff.soup 20260915 2000)
              |> JsonParseDiff.expectAgreement "soup, tolerant"

          // ---- the teeth: both comparisons can lose ----

          testCase "a model whose DEPTH CHECK is out of step DISAGREES with production"
          <| fun _ ->
              // The go-red the phase asks for: the model keeps a cap production has already spent.
              // Production refuses the input by name; the model, still holding budget, accepts it —
              // so a green run above is known to be a comparison that can fail on exactly the
              // guard this phase is about.
              let input = JsonParseDiff.nestArr 4
              let p = JsonParseDiff.prodAnswer RejectNull 3 input

              let m =
                  JsonParseDiff.modelAnswer JsonParseDiff.toChs RejectNull 3 (JsonParseDiff.budget 8) input

              Expect.stringContains p "MaxDepthExceeded" "production refuses at the cap it was given"
              Expect.stringStarts m "ok " "and an over-budgeted model does not"
              Expect.notEqual p m "so the depth comparison can lose"

              // … and it loses on the generated pool too, not only on one hand-made input.
              let disagreements =
                  [ for k in 0..8 do
                        let i = JsonParseDiff.nestArr k

                        if
                            JsonParseDiff.prodAnswer RejectNull 3 i
                            <> JsonParseDiff.modelAnswer JsonParseDiff.toChs RejectNull 3 (JsonParseDiff.budget 8) i
                        then
                            yield k ]

              Expect.isNonEmpty disagreements "the over-budgeted model must disagree somewhere in the family"

          testCase "a model handed a BLIND bridge DISAGREES with production"
          <| fun _ ->
              // The comparison's own go-red, and the parser family's counterpart to the decode
              // family's blind integer bridge: the two container characters are hidden, so every
              // structured document reaches the model as something production never saw.
              let structured = [ "{}"; "[]"; "{\"a\":1}"; "[1,2,3]"; "{\"a\":[1,{\"b\":2}]}" ]

              let disagreements =
                  [ for input in structured do
                        let p = JsonParseDiff.prodAnswer RejectNull 512 input

                        let m =
                            JsonParseDiff.modelAnswer
                                JsonParseDiff.toChsBlind
                                RejectNull
                                512
                                (JsonParseDiff.budget 512)
                                input

                        if p <> m then
                            yield input ]

              Expect.equal
                  (List.length disagreements)
                  (List.length structured)
                  "every structured document must disagree under the blind bridge"

              // And it stays narrow: a scalar is untouched by the blinding, so a green run
              // elsewhere is not green merely because this instrument is crude.
              Expect.equal
                  (JsonParseDiff.prodAnswer RejectNull 512 "42")
                  (JsonParseDiff.modelAnswer JsonParseDiff.toChsBlind RejectNull 512 (JsonParseDiff.budget 512) "42")
                  "a scalar still agrees under the blind bridge"

          // ---- Phase 145 — the two splices, and the premise each of them actually needs ----

          testCase "an actor encoding CAN carry the splice separator — Phase 136's stated reason is REFUTED"
          <| fun _ ->
              // Phase 136's README says the `|` splice is unambiguous because "`Actor.encode` emits
              // a JSON object that never contains one". It can. `jstr` escapes `"`, `\` and the C0
              // controls; `|` is 0x7C and is none of those. Recorded as an assertion rather than a
              // sentence so it goes red the day the escaper changes — at which point the model's
              // separator-freedom reading would become available and this case is the notice.
              let enc = Actor.encode (Human "a|b")
              Expect.equal enc "{\"kind\":\"human\",\"id\":\"a|b\"}" "the id is copied through unescaped"

              Expect.isTrue
                  (enc.Contains "|")
                  "an actor encoding containing the separator is REACHABLE — separator freedom is false of production"

              Expect.isTrue
                  ((Actor.encode (Agent("m|1", "v|2", "a|3"))).Contains "|")
                  "and on the Agent case at every field"

              // The pre-image really does then carry several separators, which is the whole
              // question: it is not that the splice is safe because there is only one.
              let preimage = Actor.encode (Human "a|b") + "|" + "A|x|t"

              Expect.isGreaterThan
                  (preimage.Split('|').Length - 1)
                  1
                  "the composite pre-image carries several separators"

          testCase "Actor.encode is a PREFIX-FREE code — the premise the splice lemma actually takes"
          <| fun _ ->
              // `Chain.actor_op_splice_unambiguous` asks for exactly this and nothing stronger: no
              // encoding is a proper prefix of another. It is what a self-delimiting JSON object
              // buys, and it is what survives the refutation above.
              let encs = adversarialActors |> List.map Actor.encode
              Expect.equal (List.length (List.distinct encs)) (List.length encs) "the population encodes injectively"

              let offenders =
                  [ for x in encs do
                        for y in encs do
                            if properPrefix x y then
                                yield sprintf "%s is a proper prefix of %s" x y ]

              Expect.isEmpty offenders (sprintf "no actor encoding may be a proper prefix of another: %A" offenders)

              // The probe can lose: drop the JSON envelope and the code stops being prefix-free on
              // this very population, so the emptiness above is a measurement rather than a shape.
              let bare = adversarialActors |> List.map Actor.id

              Expect.isNonEmpty
                  [ for x in bare do
                        for y in bare do
                            if properPrefix x y then
                                yield x, y ]
                  "a bare-id encoder IS prefix-comparable here — the check above can fail"

          testCase "the actor/op splice recovers both halves over the adversarial population"
          <| fun _ ->
              // The model's conclusion, measured on production's own encodings: over every pair of
              // (actor, op-encoding) the composite `Actor.encode a + \"|\" + enc` determines both.
              let pairs =
                  [ for a in adversarialActors do
                        for o in adversarialOpEncodings -> a, o ]

              let spliced = pairs |> List.map (fun (a, o) -> Actor.encode a + "|" + o, (a, o))
              let byBytes = spliced |> List.map fst

              let collisions =
                  spliced
                  |> List.groupBy fst
                  |> List.filter (fun (_, g) -> (g |> List.map snd |> List.distinct |> List.length) > 1)

              Expect.isEmpty
                  (collisions |> List.map fst)
                  "two distinct (actor, op) pairs splice to one pre-image — the splice is ambiguous"

              Expect.equal (List.length (List.distinct byBytes)) (List.length byBytes) "and no two pairs collide at all"

              // Both directions. A bare-id actor encoder makes the SAME population collide, so the
              // green above is not a property of the population.
              let bareSpliced = pairs |> List.map (fun (a, o) -> Actor.id a + "|" + o, (a, o))

              Expect.isLessThan
                  (List.length (List.distinct (bareSpliced |> List.map fst)))
                  (List.length bareSpliced)
                  "under a bare-id encoder the same population DOES collide — the comparison can lose"

          testCase "a content id carries no comma and is not empty, and a parent id that would names no node"
          <| fun _ ->
              // `Chain.parent_splice_unambiguous` asks for exactly two things of a parent id. Both
              // are properties of what mints one. The default hash is FNV-1a rendered `x8`.
              let lanes, _ = planLaneGen.Lanes 3 (ConfRng.ofSeed 3660)
              let dag = dagUnder OpStream.defaultHash planW planLaneGen.BaseOp lanes
              let keys = dag.Nodes |> Map.toList |> List.map fst
              Expect.isGreaterThan (List.length keys) 3 "the generated DAG has nodes to measure"

              for k in keys do
                  Expect.equal k.Length 8 (sprintf "a content id is 8 characters: %s" k)
                  Expect.isFalse (k.Contains ",") (sprintf "a content id carries no comma: %s" k)
                  Expect.isFalse (k = "") "a content id is not empty"

              // And the ids production stores as PARENTS are exactly those keys, in an intact DAG.
              for KeyValue(_, n) in dag.Nodes do
                  for p in n.Parents do
                      Expect.isTrue (List.contains p keys) (sprintf "a stored parent is a key: %s" p)

              // `Dag.append` does NOT validate the parent string it is handed — it takes it and
              // stores it — so a comma-bearing parent id is constructible. What stops it sitting in
              // a VERIFIED DAG is the walk's second clause: it names no node. That is the honest
              // form of "refused or escaped", and it is the reason the model may ask for
              // comma-freedom of a parent list whose parents are all present.
              let ambiguous = List.head keys + "," + List.item 1 keys

              let _, spoiled =
                  Dag.append OpStream.defaultHash planW (Human "w") planLaneGen.BaseOp ambiguous dag

              match Dag.firstBreak OpStream.defaultHash planW spoiled with
              | Some b ->
                  // Typed since Phase 147 — `Reason` was the string this case compared when it was
                  // written, and comparing the case is what that phase made possible.
                  Expect.equal b.Reason MissingParent "a comma-bearing parent id is caught as a missing parent"
                  Expect.equal b.Got ambiguous "and the break names the offending id"
              | None -> failtest "a DAG naming a parent that is not a node must not verify"

          testCase "the op codec this differential runs on certifies injective through the kit's own law"
          <| fun _ ->
              // The model's FOURTH premise (`Chain.op_codec_injective`) is the domain's, so it is a
              // parameter there and a sampled law here. This runs that law over the very witness the
              // DAG and chain differentials above use, so the premise is certified for the domain
              // whose ids those cases compare rather than for a domain invented for the purpose.
              let results = Conformance.codecInjectivityLaws planW planStreamGen 3670 200

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "the work-plan op codec is not certified injective:\n%s" (String.concat "\n" fails)

              // The premise shown to be load-bearing and the law shown to be able to lose, in one
              // move: a codec that drops the op's payload aliases two ops onto one encoding, so two
              // DAG nodes differing only in that op mint ONE content id and the tamper between them
              // is invisible to production's own walker. That is what the parameter buys.
              let lossy = { planW with Encode = fun _ -> "op" }

              Expect.isTrue
                  (Conformance.codecInjectivityLaws lossy planStreamGen 3670 200
                   |> List.exists (fun r -> not r.Passed))
                  "a codec that erases its op must fail the law — otherwise the green above cannot lose"

              let lane = [ AddItem("z1", "one"); AddItem("z2", "two") ]
              let weak = dagUnder OpStream.defaultHash lossy planLaneGen.BaseOp [ lane ]

              let tampered =
                  let k, n =
                      weak.Nodes
                      |> Map.toList
                      |> List.find (fun (_, n) -> n.Op = AddItem("z1", "one"))

                  { weak with
                      Nodes =
                          Map.add
                              k
                              { n with
                                  Op = AddItem("z1", "TAMPERED") }
                              weak.Nodes }

              Expect.isTrue
                  (Dag.verifyDag OpStream.defaultHash lossy tampered)
                  "under a non-injective CODEC — the hash untouched — production's own walker cannot see the tamper"

          // ---- Phase 138 — the APPLY ENGINE: apply, canApply and invert against the model ----

          testCase "the preservation oracle agrees with Ops.apply over colliding and duplicated grafts"
          <| fun _ ->
              let t = presDifferential TreeOps.apply 1380 12

              match t.Diffs with
              | d :: _ -> failtestf "the preservation oracle and production DISAGREE\n%s" d
              | [] ->
                  // Adequacy, per disputed shape rather than in aggregate: a count of probes cannot
                  // say WHICH of the two halves of `firstDuplicateId` was exercised, and this family
                  // exists precisely because the Phase 133 pool reaches neither. Measured at 12
                  // trials: accepted 276, rejected 1082, collided 507, duplicated 294, inverted 276.
                  // Each threshold sits below its measurement with room — they are here to catch a
                  // generator that stops reaching a shape, not to pin the numbers.
                  Expect.isGreaterThan t.Accepted 150 (sprintf "grafts were accepted (accepted=%d)" t.Accepted)
                  Expect.isGreaterThan t.Rejected 600 (sprintf "grafts were refused (rejected=%d)" t.Rejected)

                  Expect.isGreaterThan
                      t.Collided
                      250
                      (sprintf
                          "the sample carried grafts whose ids the tree ALREADY HOLDS (collided=%d) — re-read through Tree.ids, not assumed from the construction"
                          t.Collided)

                  Expect.isGreaterThan
                      t.Duplicated
                      150
                      (sprintf
                          "the sample carried grafts that repeat an id WITHIN themselves (duplicated=%d)"
                          t.Duplicated)

                  Expect.isGreaterThan
                      t.Inverted
                      150
                      (sprintf "accepted leaf operations were inverted and undone (inverted=%d)" t.Inverted)

                  for cls in
                      [ "UnknownNode"
                        "DuplicateId"
                        "CannotRemoveRoot"
                        "WouldNestUnderSelf"
                        "ReorderMismatch" ] do
                      Expect.isTrue
                          (Set.contains cls t.Classes)
                          (sprintf "the sample reached a %s rejection (reached: %A)" cls t.Classes)

          testCase "a validator that SKIPS the subtree check loses against production — the measurement can fail"
          <| fun _ ->
              // The teeth, and they cost nothing to build: `TreeOps.apply_pre137` IS the validator
              // that reads the graft's own id alone, kept under its own name when Phase 138 lifted
              // the model. Handing it to the same differential is the go-red — and it is the same
              // comparison that was silently GREEN for the whole of Phase 137, because the Phase 80
              // generator cannot mint a colliding graft. If this ever passes, the disputed inputs
              // have stopped reaching the comparison and the green run above means nothing.
              let t = presDifferential TreeOps.apply_pre137 1380 3

              // Measured at 3 trials: 126 colliding grafts reached. A go-red that met none of the
              // disputed inputs would agree with production perfectly and certify nothing, so this
              // is asserted before the disagreement is.
              Expect.isGreaterThan
                  t.Collided
                  60
                  (sprintf
                      "the go-red run reached the disputed inputs at all (collided=%d) — otherwise it proves nothing"
                      t.Collided)

              Expect.isNonEmpty
                  t.Diffs
                  "a model whose insert validator reads the graft's own id alone MUST disagree with production"

              Expect.isTrue
                  (t.Diffs
                   |> List.exists (fun d -> d.Contains "production REJECTED (DuplicateId) but the oracle accepted"))
                  (sprintf
                      "the disagreement is the one this phase is about — production refuses the graft, the old validator admits it. Got:\n%s"
                      (List.head t.Diffs))

          testCase
              "the retired counterexample is refused by the shipped engine, and admitted by the model of the old validator"
          <| fun _ ->
              // `TreeOps.insert_breaks_wf_pre137` is machine-checked against `apply_pre137`, and
              // `TreeOps.cx_insert_refused_now` against the live model. This is the third leg: the
              // SHIPPED engine, on the same concrete tree, so the refutation and its fix are pinned
              // on all three surfaces rather than two.
              let cxTree = RNode.node "root" "doc" [ RNode.leaf "a" "section" "" ]

              let cxInsert =
                  InsertChild("a", RNode.node "fresh" "section" [ RNode.leaf "root" "para" "v" ])

              match Ops.apply nodew idw cxInsert cxTree with
              | Ok _ -> failtest "the shipped engine ADMITS the Phase 133 counterexample — Phase 137 has regressed"
              | Error(DuplicateId d) ->
                  Expect.equal d "root" "the offender named is the DESCENDANT id, which the pre-137 check could not see"
              | Error e -> failtestf "refused, but not as a duplicate id: %A" e

              // and the old validator's model admits it, which is what made it a counterexample
              match TreeOps.apply_pre137 (toModelOpWith toModelTree cxInsert) (toModelTree cxTree) with
              | DagFold.Ok _ -> ()
              | DagFold.Error e ->
                  failtestf
                      "the model of the PRE-137 validator refused it too — then it was never a counterexample: %A"
                      e ]
