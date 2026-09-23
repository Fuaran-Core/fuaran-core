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

// Phase 176 — the extracted COLUMNAR model, bound BEFORE `open Fuaran.Core` puts the production
// `ColumnOps` module in front of it: the two share a name on purpose, and this is the one file
// where both are in scope. (Bound here rather than as `global.ColumnOps` beside its family:
// Fantomas rewrites a `global.`-qualified module abbreviation into invalid F#, and a `///` doc
// comment on any module abbreviation is FS0535.)
module ModelCol = ColumnOps

// Phase 177 — the extracted FUNCTION-SEAM model, bound the same way and for the same reason:
// production's `Fuaran.Core.Capability` module shares the model's name once `Fuaran.Core` is open.
module ModelCap = Capability

// Phase 186 — the extracted INCREMENTAL-PROMISE model, bound the same way and for the same
// reason: production's `Fuaran.Core.Propagation` module shares the model's name once
// `Fuaran.Core` is open.
module ModelProp = Propagation

// Phase 187 — the extracted DATA-ACQUISITION-SEAM model, bound the same way and for the same
// reason: production's `Fuaran.Core.Query` module shares the model's name once `Fuaran.Core` is
// open.
module ModelQuery = Query

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

// ---------------------------------------------------------------------------
//  Phase 156 — the ID-ORDERED DRAIN over an abstract node set, beside production's own.
//
//  Section 13 of `proofs/DagFold.fst` proves the drain deterministic, a linear extension of the
//  parent relation, and total on an acyclic set — over an ABSTRACT node set and an ABSTRACT total
//  order on ids. What ties that abstract order to production's is here and nowhere else:
//  `Dag.topoCore` keeps its ready frontier sorted with `List.sort`, which on strings is F#'s
//  structural comparison and so `String.CompareOrdinal`, and that is the `lt` the model is
//  instantiated at below.
//
//  Two shapes are compared, and the second is the point. Per LANE HEAD the closure is a spine and
//  the frontier is one element wide at every step, which is the case Phase 142 already proves and
//  which no tie-break can get wrong. Over a MULTI-HEAD UNION — the lane heads folded together with
//  `Dag.merge`, the convergent node a real reconciliation writes — the frontier is N wide once the
//  base is drained, and the tie-break is what decides the sequence. That is the case section 13
//  exists for, and it is why the go-red below runs there.
// ---------------------------------------------------------------------------

/// The id order production's `List.sort` imposes — the model's `lt` parameter, instantiated.
let private ordLt (a: string) (b: string) = System.String.CompareOrdinal(a, b) < 0

/// The go-red instrument: the same drain at the REVERSED order, which is `pick_min` of a flipped
/// total order and so the frontier's MAXIMUM. It satisfies everything the model asks of a
/// selector — it is still a member of the frontier it is handed — so what it breaks is agreement
/// with production and nothing else, which is exactly the claim these cases make. On a spine it
/// cannot lose, because a one-element frontier has one minimum and one maximum; the multi-head
/// union is what gives it room.
let private ordGt (a: string) (b: string) = System.String.CompareOrdinal(a, b) > 0

/// The model's `covers` bound: one walk step per node, plus the one its base case asks for.
let private drainFuel (ns: 'a list) : 'a list =
    match ns with
    | [] -> []
    | n :: _ -> n :: ns

/// The model nodes of a production DAG restricted to one head's ancestor closure — "the union" the
/// drain is over. Nothing is recomputed across the bridge: the ids are the content hashes
/// production minted, so a disagreement can only be about the ORDER.
let private modelClosure (dag: Dag.T<'Op>) (head: string) : DagFold.node<'Op> list =
    let anc = Dag.ancestorsOf dag head

    dag.Nodes
    |> Map.toList
    |> List.filter (fun (id, _) -> Set.contains id anc)
    |> List.map (fun (_, n) ->
        { DagFold.nid = n.Id
          DagFold.nparents = n.Parents
          DagFold.nop = n.Op })

/// One head whose closure is EVERY node: the lane heads folded together with `Dag.merge`. Returns
/// the union head and the DAG carrying the merge nodes.
let private mergedUnion
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (mergeOp: 'Op)
    (baseId: string)
    (heads: string list)
    (dag: Dag.T<'Op>)
    : string * Dag.T<'Op> =
    match heads with
    | [] -> baseId, dag
    | h0 :: rest ->
        rest
        |> List.fold
            (fun (acc, d) h ->
                if h = acc then
                    acc, d
                else
                    Dag.merge OpStream.defaultHash w (Human "merge") mergeOp acc h d)
            (h0, dag)

let private drainedOrder (r: DagFold.drain_result) : string list option =
    match r with
    | DagFold.Drained ord -> Some ord
    | DagFold.Refused _ -> None

/// Every way the extracted drain and production's own topological order disagree over one head's
/// closure, plus how wide the ready frontier ever got — a run whose frontier never passed one
/// compared two spines and measured nothing the spine theorem did not already cover.
let private drainDisagreements (lt: string -> string -> bool) (dag: Dag.T<'Op>) (head: string) : string list * int =
    let ns = modelClosure dag head
    let model = drainedOrder (DagFold.drain DagFold.IgnoreDangling lt (drainFuel ns) ns)

    // The widest ready frontier the closure ever presents: the nodes whose in-closure parents are
    // all drained, at the step that has the most of them. Computed from the model's own `frontier`
    // over its own emitted prefix, so it measures the drain that ran rather than a re-derivation.
    let widest =
        match model with
        | None -> 0
        | Some ord ->
            let closure = ns |> List.map (fun (n: DagFold.node<'Op>) -> n.nid)

            let rec go (rest: DagFold.node<'Op> list) (emitted: string list) (pending: string list) best =
                match pending with
                | [] -> best
                | id :: tl ->
                    let f = DagFold.frontier rest closure emitted
                    let best' = max best (List.length f)
                    go (DagFold.remove_id rest id) (emitted @ [ id ]) tl best'

            go ns [] ord 0

    match Dag.tryTopoOrder dag head, model with
    | Ok production, Some m when production = m -> [], widest
    | Ok production, Some m ->
        [ sprintf
              "head %s: the drain differs\n  production: [%s]\n  model:      [%s]"
              head
              (String.concat "; " production)
              (String.concat "; " m) ],
        widest
    | Ok _, None -> [ sprintf "head %s: the model REFUSED a closure with no dangling parent" head ], widest
    | Error e, _ -> [ sprintf "head %s: production reports a cyclic closure it cannot have: %s" head e ], widest

// ---------------------------------------------------------------------------
//  Phase 158 — delta recovery over a MERGED HEAD, and `Dag.mergeBase`, beside production's own.
//
//  Section 14 of `proofs/DagFold.fst` proves that over a head whose lane hangs off an already
//  MERGED node, `Dag.between` returns that lane's own nodes in append order (`between_merged`),
//  that the fold above it is the deltas-first fold (`reconcile_many_merged_eq`), and that
//  `Dag.mergeBase` of two such heads is the merged node they diverged from
//  (`merge_base_is_divergence`). What runs here is the other half: production's OWN
//  `Dag.mergeBase` / `Dag.betweenOps` / `Dag.reconcileMany` over the DAG a clone actually holds
//  after it has folded, pulled and folded again — round one's lanes off a base, their heads folded
//  into one convergent head with `Dag.merge`, and round two's lanes appended off THAT — against the
//  extracted model on the same nodes.
//
//  Nothing is recomputed across the bridge: the ids are the content hashes production minted. The
//  model's recovery runs under section 13's drain at `String.CompareOrdinal`, which is the order
//  production's `topoCore` takes, and over this shape the drain's frontier genuinely widens — so
//  the order beneath the merged head is a real choice, and the claim measured is that the
//  recovered delta does not depend on it.
// ---------------------------------------------------------------------------

/// The DAG a clone holds after FOLD, PULL, FOLD: round one's lanes off one base, their heads merged
/// into one convergent head, and round two's lanes appended off that head under their own actors.
/// Returns the original base id, the merged head, round one's heads, round two's heads, and the DAG.
let private foldPullFold
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (baseOp: 'Op)
    (round1: 'Op list list)
    (round2: 'Op list list)
    : string * string * string list * string list * Dag.T<'Op> =
    let hashFn = OpStream.defaultHash
    let baseId, heads1, dag1 = productionDag w baseOp round1
    let merged, dagM = mergedUnion w baseOp baseId heads1 dag1

    let heads2, dag2 =
        round2
        |> List.indexed
        |> List.fold
            (fun (hs, d) (i, ops) ->
                let actor = Human("pull-" + string i)

                let head, d' =
                    ops
                    |> List.fold (fun (h, dd) op -> Dag.append hashFn w actor op h dd) (merged, d)

                hs @ [ head ], d')
            ([], dagM)

    baseId, merged, heads1, heads2, dag2

let private foundId (r: DagFold.found<string>) : string option =
    match r with
    | DagFold.Found id -> Some id
    | DagFold.Missing -> None

/// The go-red instrument for `mergeBase`: the model's own function at the REVERSED key — the same
/// intersection, the same extracted `max_by`, with `deeper` flipped — so it returns the SHALLOWEST
/// common ancestor, which over a fold-pull-fold union is the ORIGINAL BASE rather than the
/// divergence point. It is as well-formed a `maxBy` as the model's own; what it breaks is agreement
/// with production, and the delta recovered from the base it names.
let private shallowestCommon (model: DagFold.dag<'Op>) (fuel: DagFold.node<'Op> list) (left: string) (right: string) =
    match DagFold.inter (DagFold.ancestors_of model fuel left) (DagFold.ancestors_of model fuel right) with
    | [] -> None
    | c :: t -> Some(DagFold.max_by (fun x y -> DagFold.deeper ordLt model fuel y x) c t)

type private MergedTally =
    {
        Failures: string list
        /// Ops recovered across every round-two lane — the vacuity guard.
        Recovered: int
        /// Round-two lanes of two or more ops: a one-node lane walks no chain.
        Chains: int
        /// Two-parent nodes beneath the merged heads: with none, the "merged head" was a plain node
        /// and the case re-measured Phase 134's shape under a different name.
        MergeNodes: int
        /// Head pairs whose merge base was located.
        Pairs: int
    }

/// One fold-pull-fold union, compared three ways: each round-two head's recovered delta (production
/// `betweenOps` from the merged head, the model's `between_ops_drained`, and the lane that was
/// appended — the theorem's own right-hand side); every pair of round-two heads' merge base
/// (production, the model, and the merged head itself); and the model's computable premises, so a
/// green run is known to be one the theorems are ABOUT.
let private mergedDisagreements
    (locate: DagFold.dag<'Op> -> DagFold.node<'Op> list -> string -> string -> string option)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (baseOp: 'Op)
    (round1: 'Op list list)
    (round2: 'Op list list)
    : MergedTally =
    let _, merged, _, heads2, dag = foldPullFold w baseOp round1 round2
    let model = toModelDag dag
    let fuel = model.nodes
    let kfuel = drainFuel model.nodes
    let failures = ResizeArray<string>()
    let mutable recovered = 0

    for (i, (head, lane)) in List.indexed (List.zip heads2 round2) do
        let p = Dag.betweenOps dag merged head
        let o = DagFold.between_ops_drained ordLt kfuel model fuel merged head
        recovered <- recovered + List.length p

        if p <> o then
            failures.Add(
                sprintf
                    "round-two lane %d: the recovered delta differs\n  production: [%s]\n  model:      [%s]"
                    i
                    (p |> List.map w.Encode |> String.concat "; ")
                    (o |> List.map w.Encode |> String.concat "; ")
            )

        if o <> lane then
            failures.Add(
                sprintf
                    "round-two lane %d: the model did not recover the lane that was appended (between_ops_merged)\n  lane:  [%s]\n  model: [%s]"
                    i
                    (lane |> List.map w.Encode |> String.concat "; ")
                    (o |> List.map w.Encode |> String.concat "; ")
            )

        // The FUEL premise of `merged_recovers`, evaluated: the walk below the lane completed.
        if not (DagFold.walk_ok model (DagFold.drop_by fuel (List.rev lane)) merged) then
            failures.Add(sprintf "round-two lane %d: the model's walk below the lane ran out of fuel — premise unmet" i)

    let mutable pairs = 0

    for (i, hi) in List.indexed heads2 do
        for (j, hj) in List.indexed heads2 do
            if i < j then
                pairs <- pairs + 1
                let p = Dag.mergeBase dag hi hj
                let o = locate model fuel hi hj

                if p <> o then
                    failures.Add(
                        sprintf "heads %d and %d: the merge base differs\n  production: %A\n  model:      %A" i j p o
                    )

                if p <> Some merged then
                    failures.Add(
                        sprintf "heads %d and %d: production's merge base %A is not the merged head %s" i j p merged
                    )

    let mergeNodes =
        let anc = Dag.ancestorsOf dag merged

        dag.Nodes
        |> Map.toList
        |> List.filter (fun (id, n) -> Set.contains id anc && List.length n.Parents > 1)
        |> List.length

    { Failures = List.ofSeq failures
      Recovered = recovered
      Chains = round2 |> List.filter (fun l -> List.length l > 1) |> List.length
      MergeNodes = mergeNodes
      Pairs = pairs }

/// The faithful locator — the extracted `merge_base` at production's id order.
let private modelMergeBase (model: DagFold.dag<'Op>) (fuel: DagFold.node<'Op> list) (l: string) (r: string) =
    foundId (DagFold.merge_base ordLt model fuel l r)

// ---------------------------------------------------------------------------
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
          | Conflicts ids -> yield sprintf "rejected %d/%s conflicts %A" p.Id p.Holder ids ]

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

let private ofModelProposal (p: Arbitrate.proposal) : ArbProposal =
    { Id = int p.pid
      Holder = p.holder
      Ops = p.script |> List.map ofModelOp }

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

// ---------------------------------------------------------------------------
//  Phase 191 — snapshot and bounded replay (`proofs/Chain.fst`, section 7)
//
//  The model's `compact` / `replay_from` / `verify_across` beside `OpStream.compact`,
//  `compactChainOnly`, `replayFrom`, `verifyAcross` and `verifyAcrossChainOnly`, over generated
//  streams, EVERY boundary of each (the out-of-range one included), and every single-record tamper
//  of each — so the streams compared include ones that do not verify and ones that do not replay,
//  which is where the two theorems say something a green `snapshotLaws` run does not.
//
//  Two comparisons per compaction, and they are different things. The first is the ordinary one:
//  the extracted model against production, value for value. The second holds PRODUCTION to the
//  theorems' own statements — `replay = offset n (replayFrom snap tail)`, and
//  `verifyChain rs = (verifyChain prefix && verifyAcross snap tail)` — using the model only for
//  `offset`. A model that agreed with production while both drifted from the theorem would pass
//  the first and fail the second.
// ---------------------------------------------------------------------------

/// `OpStream.replay`'s result, as one value both sides are compared at.
type private ReplayVerdict<'State, 'Rej> =
    | ReplayedTo of 'State
    | HaltedAt of index: int * rejection: 'Rej

let private prodReplayVerdict (r: Result<'State, int * 'Rej>) : ReplayVerdict<'State, 'Rej> =
    match r with
    | Ok s -> ReplayedTo s
    | Error(i, e) -> HaltedAt(i, e)

let private modelReplayVerdict (r: Chain.replayed<'State, 'Rej>) : ReplayVerdict<'State, 'Rej> =
    match r with
    | Chain.Replayed s -> ReplayedTo s
    | Chain.Halted(i, e) -> HaltedAt(intOfPos i, e)

let private toModelReplayed (v: ReplayVerdict<'State, 'Rej>) : Chain.replayed<'State, 'Rej> =
    match v with
    | ReplayedTo s -> Chain.Replayed s
    | HaltedAt(i, e) -> Chain.Halted(posOfInt i, e)

/// The witness's reducer, as the model takes it — the ONLY thing section 7 asks of a domain.
let private chainApply (w: StreamWitness<'Op, 'State, 'Rej>) (op: 'Op) (st: 'State) : Chain.applied<'State, 'Rej> =
    match w.Apply op st with
    | Ok s -> Chain.Applied s
    | Error e -> Chain.Refused e

/// Production's two compaction entry points. They differ in the snapshot's hash pre-image and in
/// nothing else, which is why the model takes the payload as a parameter.
type private SnapshotMode =
    | StateHashed
    | ChainOnly

let private prodCompact
    (mode: SnapshotMode)
    (hashFn: HashFn)
    (stateEnc: 'State -> string)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (rs: OpRecord<'Op> list)
    (n: int)
    : Result<Snapshot<'State> * OpRecord<'Op> list, string> =
    match mode with
    | StateHashed -> OpStream.compact hashFn stateEnc w state0 rs n
    | ChainOnly -> OpStream.compactChainOnly hashFn w state0 rs n

let private prodVerifyAcross
    (mode: SnapshotMode)
    (hashFn: HashFn)
    (stateEnc: 'State -> string)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (snap: Snapshot<'State>)
    (tail: OpRecord<'Op> list)
    : bool =
    match mode with
    | StateHashed -> OpStream.verifyAcross hashFn stateEnc w snap tail
    | ChainOnly -> OpStream.verifyAcrossChainOnly hashFn w snap tail

let private modelPay (mode: SnapshotMode) (stateEnc: 'State -> string) : Chain.pos -> 'State -> string =
    match mode with
    | StateHashed -> Chain.snap_payload showPos stateEnc
    | ChainOnly -> Chain.snap_payload_chain_only showPos

let private toModelSnapshot (s: Snapshot<'State>) : Chain.snapshot<'State> =
    { Chain.sseq = posOfInt s.Seq
      Chain.sstate = s.State
      Chain.sprev = s.PrevHash
      Chain.shash = s.Hash }

/// A compaction's whole outcome: the refusal's MESSAGE, or every field of the snapshot and the tail
/// record for record. Compared at once, so a model that snapshotted the right state under the wrong
/// boundary hash is as red as one that refused.
type private CompactVerdict<'Op, 'State> =
    | CompactedTo of seq: int * state: 'State * prevHash: string * hash: string * tail: Chain.record<'Op> list
    | CompactRefusedWith of string

let private prodCompactVerdict
    (r: Result<Snapshot<'State> * OpRecord<'Op> list, string>)
    : CompactVerdict<'Op, 'State> =
    match r with
    | Ok(s, tail) -> CompactedTo(s.Seq, s.State, s.PrevHash, s.Hash, toChainRecords tail)
    | Error e -> CompactRefusedWith e

let private modelCompactVerdict (r: Chain.compacted<'Op, 'State>) : CompactVerdict<'Op, 'State> =
    match r with
    | Chain.Compacted(s, tail) -> CompactedTo(intOfPos s.sseq, s.sstate, s.sprev, s.shash, tail)
    | Chain.CompactRefused e -> CompactRefusedWith e

type private SnapshotTally =
    {
        Failure: string option
        /// Compactions compared, and the two refusal classes among them.
        Compactions: int
        OutOfRange: int
        PrefixRefused: int
        /// Bounded replays that reached a state, and ones that HALTED in the tail at a boundary past
        /// zero — the only place the theorem's `offset` is observable.
        Accepted: int
        HaltedPastZero: int
        /// Boundaries production verified across, and ones it rejected.
        VerifiedAcross: int
        RejectedAcross: int
        /// Compactions that verify across although the ORIGINAL does not: a tamper in the discarded
        /// prefix. `compact_verifies_iff_original`'s premise, observed being load-bearing.
        PrefixTamperUnseen: int
        /// Tampers of a compacted TAIL, and how many production's boundary walker found.
        TailTampers: int
        TailDetected: int
    }

let private emptySnapshotTally =
    { Failure = None
      Compactions = 0
      OutOfRange = 0
      PrefixRefused = 0
      Accepted = 0
      HaltedPastZero = 0
      VerifiedAcross = 0
      RejectedAcross = 0
      PrefixTamperUnseen = 0
      TailTampers = 0
      TailDetected = 0 }

let private snapshotDifferential
    (label: string)
    (prodHash: HashFn)
    (modelHash: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (stateEnc: 'State -> string)
    (otherOp: 'Op -> 'Op)
    (gen: LaneGen<'Op, 'State>)
    (seed: int)
    (iterations: int)
    : SnapshotTally =
    let mutable rng = ConfRng.ofSeed seed
    let mutable t = emptySnapshotTally

    let note (why: string) =
        if t.Failure.IsNone then
            t <- { t with Failure = Some why }

    let apply = chainApply w

    for _ in 1..iterations do
        let lanes, r' = gen.Lanes 2 rng
        rng <- r'
        let ops = List.concat lanes
        let intact = chainUnder prodHash w gen.State0 (Human "writer") ops

        let where (what: string) (mode: SnapshotMode) (n: int) =
            sprintf
                "%s: seed=%d stream=%s mode=%A atSeq=%d ops=[%s]"
                label
                seed
                what
                mode
                n
                (ops |> List.map w.Encode |> String.concat "; ")

        let across (mode: SnapshotMode) (snap: Snapshot<'State>) (tail: OpRecord<'Op> list) : bool * bool =
            prodVerifyAcross mode prodHash stateEnc w snap tail,
            Chain.verify_across
                modelHash
                showPos
                w.Encode
                (modelPay mode stateEnc)
                (toModelSnapshot snap)
                (toChainRecords tail)

        let compareAt (what: string) (rs: OpRecord<'Op> list) (mode: SnapshotMode) (n: int) =
            let here = where what mode n
            let p = prodCompact mode prodHash stateEnc w gen.State0 rs n

            let m =
                Chain.compact
                    modelHash
                    showPos
                    (modelPay mode stateEnc)
                    apply
                    gen.State0
                    (toChainRecords rs)
                    (posOfInt n)

            let pv, mv = prodCompactVerdict p, modelCompactVerdict m

            t <-
                { t with
                    Compactions = t.Compactions + 1 }

            if pv <> mv then
                note (sprintf "%s\n  compact, production: %A\n  compact, model:      %A" here pv mv)

            let pOrigin = prodReplayVerdict (OpStream.replay w gen.State0 rs)

            let mOrigin = modelReplayVerdict (Chain.replay apply gen.State0 (toChainRecords rs))

            if pOrigin <> mOrigin then
                note (sprintf "%s\n  replay, production: %A\n  replay, model:      %A" here pOrigin mOrigin)

            match p with
            | Error _ ->
                if n > List.length rs then
                    t <- { t with OutOfRange = t.OutOfRange + 1 }
                else
                    t <-
                        { t with
                            PrefixRefused = t.PrefixRefused + 1 }

                    // `compact_refusal_is_the_origins`, held of production: an in-range refusal is
                    // the origin's own halt, inside the prefix.
                    match pOrigin with
                    | HaltedAt(i, _) when i < n -> ()
                    | other ->
                        note (
                            sprintf "%s\n  compact refused an in-range boundary, but the origin replay is %A" here other
                        )
            | Ok(snap, tail) ->
                let pFrom = prodReplayVerdict (OpStream.replayFrom w snap tail)

                let mFrom =
                    modelReplayVerdict (Chain.replay_from apply (toModelSnapshot snap) (toChainRecords tail))

                if pFrom <> mFrom then
                    note (sprintf "%s\n  replayFrom, production: %A\n  replayFrom, model:      %A" here pFrom mFrom)

                // `replay_from_snapshot_eq`, held of PRODUCTION's two values.
                let shifted = modelReplayVerdict (Chain.offset (posOfInt n) (toModelReplayed pFrom))

                if shifted <> pOrigin then
                    note (
                        sprintf
                            "%s\n  replay from the origin:                %A\n  replayFrom, read at the origin's index: %A"
                            here
                            pOrigin
                            shifted
                    )

                match pFrom with
                | ReplayedTo _ -> t <- { t with Accepted = t.Accepted + 1 }
                | HaltedAt _ when n > 0 ->
                    t <-
                        { t with
                            HaltedPastZero = t.HaltedPastZero + 1 }
                | HaltedAt _ -> ()

                let pAcross, mAcross = across mode snap tail

                if pAcross <> mAcross then
                    note (
                        sprintf "%s\n  verifyAcross, production: %b\n  verify_across, model:    %b" here pAcross mAcross
                    )

                // `compact_preserves_verify`, held of PRODUCTION's three values.
                let whole = OpStream.verifyChain prodHash w rs
                let prefix = OpStream.verifyChain prodHash w (List.truncate n rs)

                if whole <> (prefix && pAcross) then
                    note (
                        sprintf
                            "%s\n  verifyChain whole=%b, but verifyChain prefix=%b and verifyAcross=%b"
                            here
                            whole
                            prefix
                            pAcross
                    )

                if pAcross then
                    t <-
                        { t with
                            VerifiedAcross = t.VerifiedAcross + 1 }

                    if not whole then
                        t <-
                            { t with
                                PrefixTamperUnseen = t.PrefixTamperUnseen + 1 }
                else
                    t <-
                        { t with
                            RejectedAcross = t.RejectedAcross + 1 }

                // Every tamper of the compacted TAIL — of the intact stream only, so a detection
                // here is a detection of THIS tamper and not of one the stream already carried.
                if what = "none" then
                    for (tamper, tail') in chainTampers otherOp tail do
                        let pT, mT = across mode snap tail'

                        t <-
                            { t with
                                TailTampers = t.TailTampers + 1 }

                        if pT <> mT then
                            note (
                                sprintf
                                    "%s tail-tamper=%s\n  verifyAcross, production: %b\n  verify_across, model:    %b"
                                    here
                                    tamper
                                    pT
                                    mT
                            )

                        if not pT then
                            t <-
                                { t with
                                    TailDetected = t.TailDetected + 1 }

        if not (List.isEmpty intact) then
            for (what, rs) in ("none", intact) :: chainTampers otherOp intact do
                for mode in [ StateHashed; ChainOnly ] do
                    for n in 0 .. List.length rs + 1 do
                        compareAt what rs mode n

    t

let private expectSnapshotAgreement (label: string) (t: SnapshotTally) =
    match t.Failure with
    | Some why -> failtest why
    | None ->
        // Every class the two theorems speak about has to have been MET, or the agreement above is
        // about less than it claims.
        Expect.isGreaterThan t.Accepted 0 (sprintf "%s: no bounded replay reached a state" label)

        Expect.isGreaterThan
            t.HaltedPastZero
            0
            (sprintf "%s: no bounded replay halted past boundary zero, so the index offset was never observable" label)

        Expect.isGreaterThan t.OutOfRange 0 (sprintf "%s: no out-of-range boundary was compared" label)
        Expect.isGreaterThan t.PrefixRefused 0 (sprintf "%s: no compaction was refused on its prefix" label)
        Expect.isGreaterThan t.VerifiedAcross 0 (sprintf "%s: no boundary verified" label)
        Expect.isGreaterThan t.RejectedAcross 0 (sprintf "%s: no boundary was rejected" label)

        Expect.isGreaterThan
            t.PrefixTamperUnseen
            0
            (sprintf "%s: no prefix tamper was compacted away, so the corollary's premise was never exercised" label)

        Expect.isGreaterThan t.TailTampers 0 (sprintf "%s: no tail tamper was compared" label)

        Expect.isGreaterThan
            t.TailDetected
            0
            (sprintf "%s: every tail tamper went undetected, so the agreement is vacuous" label)

// ---------------------------------------------------------------------------
//  Phase 193 — the signed head (`proofs/Chain.fst`, section 8)
//
//  `OpStream.head`, `attestHead` and `verifyAttestation` beside the extracted model, over
//  generated chains, generated KEYRINGS, and the three splices the theorem names — each followed
//  by a full RE-MINT, so that `verifyChain` accepts the result and only the signature can refuse
//  it. That re-mint is the point: it is the tamper sections 1-7 of the model cannot see.
//
//  CORE SHIPS NO PRODUCTION SIGNER. `OpStream.noAttestation` signs nothing, and a real sink is
//  host-side. So "beside production signing" means beside the `IAttestationSink` SEAM, driven by
//  the test-local keyring sink below: what is compared is the model against the seam's contract
//  (`head`, `attestHead`, `verifyAttestation`, and the `verifyChain && verifyAttestation`
//  composition `verifyAttestation`'s doc comment describes), never against a signer.
// ---------------------------------------------------------------------------

/// A test-local keyring: named secrets, and the one a signer signs with. NOT cryptography — the
/// "signature" is the default hash keyed by the secret — and deliberately one whose `Verify` does
/// NOT compare the attestation's recorded `Head`, so that `signature_binds` is spent on the keyed
/// digest rather than satisfied by construction.
type private Keyring =
    { Keys: (string * string) list
      Active: string }

let private keyringSink (ring: Keyring) : IAttestationSink =
    let secretOf (keyId: string) =
        ring.Keys |> List.tryFind (fun (k, _) -> k = keyId) |> Option.map snd

    { new IAttestationSink with
        member _.Sign head =
            secretOf ring.Active
            |> Option.map (fun secret ->
                { Head = head
                  KeyId = ring.Active
                  Signature = OpStream.defaultHash secret head })

        member _.Verify att head =
            match secretOf att.KeyId with
            | Some secret -> att.Signature = OpStream.defaultHash secret head
            | None -> false }

/// A sink for which `signature_binds` is FALSE: it verifies every attestation against every head.
let private promiscuousSink: IAttestationSink =
    { new IAttestationSink with
        member _.Sign head =
            Some
                { Head = head
                  KeyId = "any"
                  Signature = "yes" }

        member _.Verify _ _ = true }

let private toModelAtt (a: Attestation) : Chain.attestation =
    { Chain.ahead = a.Head
      Chain.akey = a.KeyId
      Chain.asig = a.Signature }

let private ofModelAtt (a: Chain.attestation) : Attestation =
    { Head = a.ahead
      KeyId = a.akey
      Signature = a.asig }

/// The sink's own `Sign` and `Verify`, handed to the model as the two parameters it takes.
let private modelSign (sink: IAttestationSink) (head: string) : Chain.found<Chain.attestation> =
    match sink.Sign head with
    | Some a -> Chain.Found(toModelAtt a)
    | None -> Chain.Missing

let private modelVerify (sink: IAttestationSink) (att: Chain.attestation) (head: string) : bool =
    sink.Verify (ofModelAtt att) head

/// A full re-mint under production's own canonical payload and genesis: every record's sequence,
/// prev-link and hash recomputed from the steps, so `verifyChain` accepts whatever it is handed.
/// The shape of `Conformance.attestationLaws`'s forgery, spelled here over a step list because a
/// splice changes the chain's LENGTH, which a record-for-record rehash cannot.
let private remint (hashFn: HashFn) (encode: 'Op -> string) (steps: (Actor * 'Op) list) : OpRecord<'Op> list =
    (([], OpStream.canonicalConfig.Genesis, 0), steps)
    ||> List.fold (fun (acc, prev, i) (actor, op) ->
        let h = hashFn prev (OpStream.canonicalConfig.Payload i actor (encode op))

        { Seq = i
          Actor = actor
          Op = op
          PrevHash = prev
          Hash = h }
        :: acc,
        h,
        i + 1)
    |> fun (acc, _, _) -> List.rev acc

/// One splice, spelled twice and INDEPENDENTLY: as the model's `splice`, and as a plain list edit
/// of production's steps. The differential requires the two to mint the same records.
type private SpliceCase<'Op> =
    { Name: string
      Model: Chain.splice<'Op>
      Steps: (Actor * 'Op) list }

let private spliceCases (otherOp: 'Op -> 'Op) (steps: (Actor * 'Op) list) : SpliceCase<'Op> list =
    let n = List.length steps

    let toStep (a: Actor, o: 'Op) : Chain.cstep<'Op> =
        { Chain.cactor = Actor.encode a
          Chain.cop = o }

    let mallory = Human "mallory"

    [ for i in 0 .. n - 1 do
          let a, o = List.item i steps

          let put (s: Actor * 'Op) =
              steps |> List.mapi (fun j x -> if j = i then s else x)

          // An op replaced; a record re-attributed; and a replacement by the SAME step, which is
          // no splice at all and must stay accepted.
          yield
              { Name = sprintf "replace-op@%d" i
                Model = Chain.Replaced(posOfInt i, toStep (a, otherOp o))
                Steps = put (a, otherOp o) }

          yield
              { Name = sprintf "replace-actor@%d" i
                Model = Chain.Replaced(posOfInt i, toStep (mallory, o))
                Steps = put (mallory, o) }

          yield
              { Name = sprintf "replace-same@%d" i
                Model = Chain.Replaced(posOfInt i, toStep (a, o))
                Steps = steps }

          yield
              { Name = sprintf "drop@%d" i
                Model = Chain.Dropped(posOfInt i)
                Steps = List.removeAt i steps }

      // An insertion at every position, the end and one PAST the end included — the model appends
      // there, and so does this.
      for i in 0 .. n + 1 do
          let inserted = mallory, otherOp (snd (List.item (min i (n - 1)) steps))

          yield
              { Name = sprintf "insert@%d" i
                Model = Chain.Inserted(posOfInt i, toStep inserted)
                Steps = List.insertAt (min i n) inserted steps }

      // Out of range, a replacement and a removal change nothing.
      yield
          { Name = "replace-out-of-range"
            Model = Chain.Replaced(posOfInt (n + 2), toStep (mallory, otherOp (snd (List.head steps))))
            Steps = steps }

      yield
          { Name = "drop-out-of-range"
            Model = Chain.Dropped(posOfInt (n + 2))
            Steps = steps } ]

type private SignedTally =
    {
        Failure: string option
        /// Chains signed, and keyrings drawn with more than one key.
        Signed: int
        MultiKey: int
        /// A verifier whose ring lacks the signing key, and an attestation re-labelled to another
        /// key of the same ring: both refused, on the intact chain.
        UnknownKeyRefused: int
        WrongKeyRefused: int
        /// Re-minted splices compared; those the WALKER accepted (all of them, or the re-mint is
        /// not one); those the signature refused; and the no-op ones that stayed accepted.
        Splices: int
        WalkerBlind: int
        Refused: int
        NoOpAccepted: int
        ReplacedRefused: int
        InsertedRefused: int
        DroppedRefused: int
        /// In-place tampers — no re-mint — which the walker finds before the signature is asked.
        InPlace: int
        InPlaceRefused: int
    }

let private emptySignedTally =
    { Failure = None
      Signed = 0
      MultiKey = 0
      UnknownKeyRefused = 0
      WrongKeyRefused = 0
      Splices = 0
      WalkerBlind = 0
      Refused = 0
      NoOpAccepted = 0
      ReplacedRefused = 0
      InsertedRefused = 0
      DroppedRefused = 0
      InPlace = 0
      InPlaceRefused = 0 }

let private drawKeyring (rng: ConfRng.T) : Keyring * ConfRng.T =
    let count, r1 = ConfRng.intBelow 3 rng
    let active, r2 = ConfRng.intBelow (count + 1) r1
    let salt, r3 = ConfRng.intBelow 1000000 r2

    { Keys = [ for k in 0..count -> sprintf "key-%d" k, sprintf "secret-%d-%d" k salt ]
      Active = sprintf "key-%d" active },
    r3

let private signedHeadDifferential
    (label: string)
    (prodHash: HashFn)
    (modelHash: HashFn)
    (sinkOf: Keyring -> IAttestationSink)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (otherOp: 'Op -> 'Op)
    (gen: LaneGen<'Op, 'State>)
    (seed: int)
    (iterations: int)
    : SignedTally =
    let mutable rng = ConfRng.ofSeed seed
    let mutable t = emptySignedTally

    let note (why: string) =
        if t.Failure.IsNone then
            t <- { t with Failure = Some why }

    let writer = Human "writer"

    for _ in 1..iterations do
        let lanes, r' = gen.Lanes 2 rng
        let ring, r'' = drawKeyring r'
        rng <- r''
        let sink = sinkOf ring
        let intact = chainUnder prodHash w gen.State0 writer (List.concat lanes)

        if not (List.isEmpty intact) then
            let steps = intact |> List.map (fun r -> r.Actor, r.Op)

            let cs: Chain.cstep<'Op> list =
                steps
                |> List.map (fun (a, o) ->
                    { Chain.cactor = Actor.encode a
                      Chain.cop = o })

            let here (what: string) =
                sprintf
                    "%s: seed=%d %s keys=%d active=%s ops=[%s]"
                    label
                    seed
                    what
                    (List.length ring.Keys)
                    ring.Active
                    (steps |> List.map (snd >> w.Encode) |> String.concat "; ")

            // `OpStream.head` against `chain_head`, and the premise the theorem carries about it.
            let pHead = OpStream.head intact
            let mHead = Chain.chain_head (toChainRecords intact)

            if pHead <> mHead then
                note (sprintf "%s\n  head, production: %s\n  chain_head, model: %s" (here "intact") pHead mHead)

            if pHead = "" then
                note (sprintf "%s\n  a non-empty chain's head is the empty-chain sentinel" (here "intact"))

            // The model's re-mint is production's `append`, record for record.
            let mBuilt = Chain.build_chain modelHash showPos w.Encode "" Chain.PZero cs

            if mBuilt <> toChainRecords intact then
                note (sprintf "%s\n  build_chain does not mint the records production appended" (here "intact"))

            if remint prodHash w.Encode steps <> intact then
                note (sprintf "%s\n  the test's own re-mint does not reproduce production's append" (here "intact"))

            // `attestHead` against `attest_head`.
            let pAtt = OpStream.attestHead sink intact
            let mAtt = Chain.attest_head (modelSign sink) (toChainRecords intact)

            (match pAtt, mAtt with
             | Some p, Chain.Found m when toModelAtt p = m -> ()
             | None, Chain.Missing -> ()
             | _ ->
                 note (
                     sprintf "%s\n  attestHead, production: %A\n  attest_head, model:    %A" (here "intact") pAtt mAtt
                 ))

            match pAtt with
            | None -> ()
            | Some att ->
                t <-
                    { t with
                        Signed = t.Signed + 1
                        MultiKey = t.MultiKey + (if List.length ring.Keys > 1 then 1 else 0) }

                let accepts (verifier: IAttestationSink) (a: Attestation) (rs: OpRecord<'Op> list) : bool * bool =
                    (OpStream.verifyChain prodHash w rs && OpStream.verifyAttestation verifier a rs),
                    Chain.accepts_signed
                        modelHash
                        showPos
                        w.Encode
                        ""
                        (modelVerify verifier)
                        (toModelAtt a)
                        (toChainRecords rs)

                let agree (what: string) (p: bool, m: bool) =
                    if p <> m then
                        note (sprintf "%s\n  accepted, production: %b\n  accepts_signed, model: %b" (here what) p m)

                    p

                // The round trip: the chain that was signed is accepted.
                if not (agree "intact" (accepts sink att intact)) then
                    note (
                        sprintf "%s\n  the chain that was signed is refused under its own attestation" (here "intact")
                    )

                // KEYRINGS. A verifier that does not hold the signing key refuses; so does the same
                // signature re-labelled to another key of the ring.
                let stranger =
                    sinkOf
                        { ring with
                            Keys = ring.Keys |> List.filter (fun (k, _) -> k <> att.KeyId) }

                if not (agree "unknown-key" (accepts stranger att intact)) then
                    t <-
                        { t with
                            UnknownKeyRefused = t.UnknownKeyRefused + 1 }

                for (other, _) in ring.Keys |> List.filter (fun (k, _) -> k <> att.KeyId) do
                    if not (agree ("relabelled-to-" + other) (accepts sink { att with KeyId = other } intact)) then
                        t <-
                            { t with
                                WrongKeyRefused = t.WrongKeyRefused + 1 }

                // THE SPLICES, each re-minted.
                for sp in spliceCases otherOp steps do
                    let pForged = remint prodHash w.Encode sp.Steps

                    let mForged =
                        Chain.build_chain modelHash showPos w.Encode "" Chain.PZero (Chain.apply_splice cs sp.Model)

                    if toChainRecords pForged <> mForged then
                        note (
                            sprintf
                                "%s\n  apply_splice + build_chain does not mint the production forgery"
                                (here sp.Name)
                        )

                    let changes = Chain.splice_changes cs sp.Model

                    if changes <> (sp.Steps <> steps) then
                        note (
                            sprintf
                                "%s\n  splice_changes says %b, but the production steps %s"
                                (here sp.Name)
                                changes
                                (if sp.Steps <> steps then "moved" else "did not move")
                        )

                    let walker = OpStream.verifyChain prodHash w pForged
                    let accepted = agree sp.Name (accepts sink att pForged)

                    t <-
                        { t with
                            Splices = t.Splices + 1
                            WalkerBlind = t.WalkerBlind + (if walker then 1 else 0) }

                    if not walker then
                        note (
                            sprintf
                                "%s\n  the re-mint does not verify, so it is not the tamper the theorem is about"
                                (here sp.Name)
                        )

                    // `signed_head_rejects_splice`, held of PRODUCTION's own verdict.
                    if changes && accepted then
                        note (
                            sprintf "%s\n  a re-minted splice is ACCEPTED under the original attestation" (here sp.Name)
                        )

                    if not changes && not accepted then
                        note (sprintf "%s\n  a splice that changes nothing is refused" (here sp.Name))

                    if changes && not accepted then
                        t <-
                            { t with
                                Refused = t.Refused + 1
                                ReplacedRefused =
                                    t.ReplacedRefused
                                    + (match sp.Model with
                                       | Chain.Replaced _ -> 1
                                       | _ -> 0)
                                InsertedRefused =
                                    t.InsertedRefused
                                    + (match sp.Model with
                                       | Chain.Inserted _ -> 1
                                       | _ -> 0)
                                DroppedRefused =
                                    t.DroppedRefused
                                    + (match sp.Model with
                                       | Chain.Dropped _ -> 1
                                       | _ -> 0) }

                    if not changes && accepted then
                        t <-
                            { t with
                                NoOpAccepted = t.NoOpAccepted + 1 }

                // In place, with NO re-mint: the walker's own business, and the composition must
                // still agree.
                for (tamper, rs') in chainTampers otherOp intact do
                    t <- { t with InPlace = t.InPlace + 1 }

                    if not (agree ("in-place " + tamper) (accepts sink att rs')) then
                        t <-
                            { t with
                                InPlaceRefused = t.InPlaceRefused + 1 }

    t

let private expectSignedAgreement (label: string) (t: SignedTally) =
    match t.Failure with
    | Some why -> failtest why
    | None ->
        // Every class the two theorems speak about has to have been MET.
        Expect.isGreaterThan t.Signed 0 (sprintf "%s: no chain was signed" label)
        Expect.isGreaterThan t.MultiKey 0 (sprintf "%s: no keyring held more than one key" label)
        Expect.isGreaterThan t.UnknownKeyRefused 0 (sprintf "%s: no verifier lacking the key was compared" label)
        Expect.isGreaterThan t.WrongKeyRefused 0 (sprintf "%s: no re-labelled attestation was compared" label)
        Expect.isGreaterThan t.Splices 0 (sprintf "%s: no splice was compared" label)

        Expect.equal
            t.WalkerBlind
            t.Splices
            (sprintf "%s: a re-minted splice failed verifyChain, so it was not the rewrite the theorem is about" label)

        Expect.isGreaterThan t.ReplacedRefused 0 (sprintf "%s: no replaced op was refused" label)
        Expect.isGreaterThan t.InsertedRefused 0 (sprintf "%s: no inserted op was refused" label)
        Expect.isGreaterThan t.DroppedRefused 0 (sprintf "%s: no dropped op was refused" label)

        Expect.isGreaterThan
            t.NoOpAccepted
            0
            (sprintf "%s: no splice that changes nothing was accepted, so `splice_changes` was never load-bearing" label)

        Expect.isGreaterThan t.InPlace 0 (sprintf "%s: no in-place tamper was compared" label)
        Expect.equal t.InPlaceRefused t.InPlace (sprintf "%s: an in-place tamper was accepted" label)

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
        | SiblingCorpus.NotAsked why -> skiptest why
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

    // ---- the ERASE-THEN-COMPARE probe (Phase 190) ----

    /// A refusal rendering with the index dropped. The bridge relates two documents of DIFFERENT
    /// lengths, and the model carries a position as the input SUFFIX — the same list on both
    /// sides — which this host renders as an index into an input the deletion shortened. So the
    /// kind and the message are compared and the index is not; the index is what every other
    /// probe in this family compares, on documents that are the same bytes on both sides.
    let withoutPosition (answer: string) : string =
        if answer.StartsWith "ok " then
            answer
        else
            System.Text.RegularExpressions.Regex.Replace(answer, " @\\d+ ", " ")

    /// `k` nested objects, each carrying a member null beside a member that survives — the
    /// every-depth half of the theorem, whose model statement quantifies over the remaining
    /// budget rather than over a nesting family.
    let rec nestObjNulls (k: int) : string * string =
        if k <= 0 then
            "0", "0"
        else
            let inner, erased = nestObjNulls (k - 1)
            sprintf "{\"n\":null,\"v\":%s}" inner, sprintf "{\"v\":%s}" erased

    /// Phase 190's bridge as a pool: a document carrying member nulls, beside the document with
    /// exactly those members DELETED. `null_absorption_is_erasure` says the tolerant reading of
    /// the first IS the strict reading of the second, so this is the theorem's own statement put
    /// to production and to the extracted model rather than only to the prover.
    let erasurePairs: (string * string * string) list =
        [ "leading null", "{\"a\":null,\"b\":1}", "{\"b\":1}"
          "trailing null", "{\"b\":1,\"a\":null}", "{\"b\":1}"
          "interior null", "{\"b\":1,\"a\":null,\"c\":2}", "{\"b\":1,\"c\":2}"
          "run of nulls", "{\"a\":null,\"b\":null,\"c\":3}", "{\"c\":3}"
          "sole null", "{\"a\":null}", "{}"
          "all nulls", "{\"a\":null,\"b\":null}", "{}"
          // The whitespace the model's `null_member` does not spell: it is read by `skip_ws`,
          // which is policy-independent, so this is the spelling the theorem's boundary names.
          "whitespace around the fork", "{ \"a\" : null , \"b\" : 1 }", "{ \"b\" : 1 }"
          "whitespace and sole null", "{ \"a\" : null }", "{ }"
          "nested object", "{\"a\":{\"c\":null,\"d\":2}}", "{\"a\":{\"d\":2}}"
          "null inside an array of objects", "[{\"a\":null,\"b\":1},{\"a\":null}]", "[{\"b\":1},{}]"
          "null beside an array member", "{\"a\":null,\"b\":[1,{\"c\":null,\"d\":3}]}", "{\"b\":[1,{\"d\":3}]}"
          // Keys the model's `plain_all` hypothesis does not admit — that hypothesis is what lets
          // the lemma unfold `parse_string` without a fact about escapes, and the absorption does
          // not read the key at all, so production and the oracle are asked the wider question
          // here than the prover was asked there.
          "escaped key", "{\"a\\nb\":null,\"c\":1}", "{\"c\":1}"
          "unicode key", "{\"kéy\":null,\"c\":1}", "{\"c\":1}"
          // The REFUSAL half: a tail the strict reader refuses is refused identically, by kind
          // and message, whether or not the nulls in front of it were absorbed.
          "refused tail — no value", "{\"a\":null,\"b\":}", "{\"b\":}"
          "refused tail — unclosed", "{\"a\":null,\"b\":1", "{\"b\":1"
          "refused tail — bad number", "{\"a\":null,\"b\":1e}", "{\"b\":1e}"
          "deep nesting", fst (nestObjNulls 6), snd (nestObjNulls 6) ]

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
//  TWO SOURCES OF (op, state) PAIRS, since Phase 139. The generator plus the minted grafts is the
//  first; the shared corpus's `apply/` family is the second, decoded through its own envelope and
//  asked of both sides through this same probe. That family is also what four other hosts certify
//  against, so running it here joins "the model agrees with production" to "production agrees with
//  the published contract" instead of leaving them two unconnected claims.
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
/// This is the GENERATED source. Phase 139 landed the second one — the shared corpus's `apply/`
/// family, run by `presCorpusDifferential` below — and the two answer different questions rather
/// than one twice: this pool is wide and nobody chose it, and the corpus pool is narrow and
/// deliberately covers one clause per op, so a shape the generator never draws is still asked.
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

// ---------------------------------------------------------------------------
//  Phase 140 — the CONTAINER CAPABILITY: `Ops.applyContained` / `Ops.canApplyContained` beside
//  the model, over a GENERATED `canHold`.
//
//  WHY A GENERATED PREDICATE RATHER THAN A FIXED ONE. `canHold` is a parameter of the engine, and
//  the model proves its theorems for EVERY predicate. A differential run against one hand-picked
//  predicate (`ContainedOpsTests`' "a para is a leaf") certifies that instance and nothing about
//  the quantifier. Each trial here draws a random SUBSET of the pool's kind vocabulary — including
//  the empty set, where nothing can hold children and every insert and move is refused, and the
//  full set, where the engine is `Ops.apply` again. Both extremes are inside the theorem, so both
//  are inside the sample.
//
//  The predicates on both sides decide on the KIND TAG alone, which is `child_blind` — the model's
//  own premise — instantiated: it is how every domain writes `canHold`, and the model's
//  counterexample `contained_needs_child_blind` is what says the type permits worse.
//
//  FIVE COMPARISONS, per (op, state, predicate):
//    1. `Ops.applyContained` vs the model's `apply_contained` — verdict, accepted result through
//       `Tree.encodeHash`, rejection by CLASS.
//    2. `Ops.canApplyContained` vs the model's `can_apply_contained`, and that the dry run and the
//       mutating call agree on EACH side separately (`can_apply_contained_agrees` instantiated).
//    3. `Ops.applyContained` against `Ops.apply` ON PRODUCTION: the two must agree exactly unless
//       the container-aware one raises `NotAContainer`, which is `not_a_container_exact` asked of
//       the shipped engine rather than of the model.
//    4. where a `NotAContainer` is raised, that the node it names really holds children, that its
//       own kind tag is what was reported, and that the predicate really refuses it
//       (`not_a_container_locates`). Since Phase 161 the node is looked for in the TREE or in the
//       op's own inserted SUBTREE, because there are two capability sites and they name nodes in
//       two different places — a parent, and an interior node of a graft.
//    5. THE INVARIANT: where the STATE satisfies "every node with children can hold children", so
//       must the result (`contained_preserves`). Counted, because a run in which the hypothesis
//       was never met would assert the theorem vacuously. Phase 161 retired the second half of
//       that hypothesis — the operation's own graft — by making the engine refuse a graft that
//       breaks it, so this arm now asserts the conclusion on strictly more probes.
//
//  THE GO-RED IS THE MODEL'S OWN INSTRUMENT. `Preservation.apply_contained_insert_only` is the
//  engine that checks the capability on insert and not on move, and
//  `insert_only_breaks_contained` proves — in F\*, before this file runs — that it admits a move
//  the real one refuses and breaks the invariant doing it. Handing it to the same differential is
//  the measurement, and it must lose on a MOVE.
// ---------------------------------------------------------------------------

/// The kind vocabulary the pool actually uses: the base tree's three, which is also every kind the
/// generator and the probes below mint. A subset of it is a `canHold`.
let private containerKinds = [ "doc"; "para"; "section" ]

let private drawCanHold (r0: ConfRng.T) : Set<string> * ConfRng.T =
    let mutable r = r0
    let mutable acc = Set.empty

    for k in containerKinds do
        let b, r' = ConfRng.intBelow 2 r
        r <- r'

        if b = 1 then
            acc <- Set.add k acc

    acc, r

let private showKinds (kinds: Set<string>) =
    if Set.isEmpty kinds then
        "(nothing)"
    else
        kinds |> Set.toList |> String.concat "+"

/// "every node with children satisfies `canHold`", written directly over the witness — the model's
/// `contained` asked of a production tree. Written out rather than routed through the extracted
/// model, for the reason the two op renderers are written out: one function over both sides could
/// not tell a divergence from its own convention.
let rec private prodContained (canHold: RNode -> bool) (n: RNode) : bool =
    (match nodew.Children n with
     | [] -> true
     | _ -> canHold n)
    && nodew.Children n |> List.forall (prodContained canHold)

/// The invariant's second hypothesis, on the operation's own trees.
let rec private prodContainedOp (canHold: RNode -> bool) (op: SkeletonOp<RNode, string>) : bool =
    match op with
    | InsertChild(_, node) -> prodContained canHold node
    | Batch inner -> inner |> List.forall (prodContainedOp canHold)
    | _ -> true

type private ContTally =
    {
        Diffs: string list
        Accepted: int
        Refused: int
        /// `NotAContainer` refusals, counted per SITE — the go-red is about the move half, so a run
        /// that reached only the insert half could not have caught it.
        InsertRefusals: int
        MoveRefusals: int
        BatchRefusals: int
        /// (Phase 161) refusals raised by the GRAFT walk rather than by either parent check — a
        /// fourth capability site, and the one a pool of leaf-only inserts can never reach.
        GraftRefusals: int
        /// probes where the capability PRE-EMPTED another refusal — the shape the first run of
        /// this differential turned up, and the one an `adds a refusal` reading gets wrong
        Preempted: int
        /// probes where the invariant's hypotheses were MET, and the conclusion therefore asserted
        Preserved: int
        Predicates: Set<string>
        Classes: Set<string>
    }

let private emptyContTally =
    { Diffs = []
      Accepted = 0
      Refused = 0
      InsertRefusals = 0
      MoveRefusals = 0
      BatchRefusals = 0
      GraftRefusals = 0
      Preempted = 0
      Preserved = 0
      Predicates = Set.empty
      Classes = Set.empty }

let private contProbe
    (modelApply:
        (TreeOps.tree -> bool) -> TreeOps.op -> TreeOps.tree -> DagFold.outcome<TreeOps.tree, TreeOps.rejection>)
    (kinds: Set<string>)
    (op: SkeletonOp<RNode, string>)
    (st: RNode)
    (acc: ContTally)
    : ContTally =
    let canHold (n: RNode) = kinds.Contains(nodew.KindTag n)
    let mCanHold (t: TreeOps.tree) = kinds.Contains(mKind t)
    let mop = toModelOpWith toModelTree op
    let mst = toModelTree st

    let where =
        sprintf "op %s at tree %s under canHold={%s}" (renderProdOp op) (prodTreeHash st) (showKinds kinds)

    let prod = Ops.applyContained canHold nodew idw op st
    let model = modelApply mCanHold mop mst
    let prodCan = Ops.canApplyContained canHold nodew idw op st
    let modelCan = Preservation.can_apply_contained mCanHold mop mst
    let plain = Ops.apply nodew idw op st

    // 1. applyContained
    let applyDiff, accepted, refused, cls =
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
        // The refusal is COUNTED even where the two sides disagree about it, unlike the Phase 138
        // family's corresponding arm. That is what lets the go-red's adequacy guard say which
        // capability SITE it reached: in the go-red the model accepts every move, so a move
        // refusal exists only on production's side of a divergence, and a probe that did not
        // count it would report the run as having reached no move at all.
        | Error pe, DagFold.Ok _ ->
            [ sprintf "production REJECTED (%s) but the oracle accepted — %s" (prodRejClass pe) where ],
            0,
            1,
            Some(prodRejClass pe)

    // 2. canApplyContained, and the dry run against the mutating call on each side
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
                [ sprintf "canApplyContained differs — %s\n  production: %s\n  oracle:     %s" where pv mv ]
            else
                []

        let prodSelf =
            if
                Result.isOk prodCan
                <> (match prod with
                    | Ok _ -> true
                    | Error _ -> false)
            then
                [ sprintf "production's canApplyContained and applyContained DISAGREE — %s (check %s)" where pv ]
            else
                []

        differs @ prodSelf

    // 3. the DIFFERENCE, on production: the two engines agree EXCEPT where the container-aware one
    //    raises `NotAContainer` — `apply_contained_diff` asked of the shipped code rather than of
    //    the model. This is the half that says `applyContained` is `apply` plus a guard rather
    //    than a second engine.
    //
    //    Note the shape of the exception, which the first run of this differential is what taught:
    //    the capability check can PRE-EMPT another refusal rather than only adding one. The
    //    `MoveNode` arm tests `canHold` on the new parent (Ops.fs:249) BEFORE the descendant test
    //    (Ops.fs:258), so a self-move into a leaf is `NotAContainer` here and `WouldNestUnderSelf`
    //    under `apply` — two engines refusing the same operation with different classes, which is
    //    correct and is why the theorem is stated as "identical, or `NotAContainer`" rather than
    //    as "the same class unless the capability adds one".
    let diffDiff =
        match plain, prod with
        | Ok a, Ok b ->
            if prodTreeHash a <> prodTreeHash b then
                [ sprintf "apply and applyContained both accepted but produced DIFFERENT trees — %s" where ]
            else
                []
        | Ok _, Error e ->
            if prodRejClass e <> "NotAContainer" then
                [ sprintf
                      "applyContained refused what apply accepted, and NOT as NotAContainer (%s) — %s"
                      (prodRejClass e)
                      where ]
            else
                []
        | Error a, Error b ->
            if prodRejClass b <> "NotAContainer" && prodRejClass a <> prodRejClass b then
                [ sprintf
                      "apply and applyContained refused DIFFERENTLY, and not by the capability — %s\n  apply:           %s\n  applyContained: %s"
                      where
                      (prodRejClass a)
                      (prodRejClass b) ]
            else
                []
        | Error a, Ok _ -> [ sprintf "applyContained ACCEPTED what apply refused (%s) — %s" (prodRejClass a) where ]

    // 4. where the refusal is a NotAContainer, it names a node that really holds children, reports
    //    that node's own kind tag, and the predicate really refuses it. Since Phase 161 there are
    //    TWO places the node can be, and which one is not a detail: a parent refusal names a node
    //    of the TREE, and a graft refusal names a node of the op's own inserted SUBTREE, which the
    //    duplicate-id scan guarantees is not in the tree at all. Looking only in the tree would
    //    report every graft refusal as a defect; looking in either without saying which would let a
    //    refusal that named nothing pass.
    let locateDiff =
        match prod with
        | Error(NotAContainer(target, kindTag)) ->
            let inTree = Tree.tryFind nodew idw target st

            let inGraft =
                match op with
                | InsertChild(_, graft) -> Tree.tryFind nodew idw target graft
                | _ -> None

            match inTree, inGraft with
            | None, None ->
                [ sprintf "NotAContainer named %s, which neither the tree nor the graft holds — %s" target where ]
            | _ ->
                let site, n =
                    match inTree with
                    | Some n -> "the tree", n
                    | None -> "the graft", Option.get inGraft

                (if nodew.KindTag n <> kindTag then
                     [ sprintf
                           "NotAContainer reported kind %s for %s in %s, whose kind is %s — %s"
                           kindTag
                           target
                           site
                           (nodew.KindTag n)
                           where ]
                 else
                     [])
                @ (if canHold n then
                       [ sprintf "NotAContainer named %s in %s, which canHold ADMITS — %s" target site where ]
                   else
                       [])
                @ (match inTree, inGraft with
                   | None, Some g when List.isEmpty (nodew.Children g) ->
                       // the graft walk refuses a node that HOLDS children; a childless one the
                       // predicate merely dislikes is not a violation of the invariant at all
                       [ sprintf "the graft refusal named %s, which holds no children — %s" target where ]
                   | _ -> [])
        | _ -> []

    // 5. the invariant, on production, where its hypothesis is met.
    //
    // Phase 161 RETIRED the second hypothesis: `contained_op` used to gate this arm too, because
    // the engine never inspected a graft and an insert could therefore carry a violation in. The
    // engine refuses such a graft now (`nested_graft_refused`), so the only premise left is that
    // the input tree satisfies the invariant — which is why this arm asserts the conclusion on
    // strictly MORE probes than it did before the phase, including every graft probe below.
    let preservedDiff, preserved =
        if prodContained canHold st then
            match prod with
            | Ok result ->
                (if prodContained canHold result then
                     []
                 else
                     [ sprintf
                           "the container invariant BROKE across an accepted operation — %s\n  a node with children fails canHold in the result"
                           where ]),
                1
            | Error _ -> [], 1
        else
            [], 0

    let preempted =
        match plain, prod with
        | Error a, Error b when prodRejClass b = "NotAContainer" && prodRejClass a <> "NotAContainer" -> 1
        | _ -> 0

    let insertRef, moveRef, batchRef =
        match cls, op with
        | Some "NotAContainer", InsertChild _ -> 1, 0, 0
        | Some "NotAContainer", MoveNode _ -> 0, 1, 0
        | Some "NotAContainer", Batch _ -> 0, 0, 1
        | _ -> 0, 0, 0

    // Phase 161's site, told from the parent site by WHERE the named node is: a graft refusal names
    // a node the tree does not hold (the duplicate-id scan has already refused a graft sharing an
    // id with it), so the two are distinguishable from the envelope alone.
    let graftRef =
        match prod, op with
        | Error(NotAContainer(target, _)), InsertChild(_, graft) ->
            (match Tree.tryFind nodew idw target st, Tree.tryFind nodew idw target graft with
             | None, Some _ -> 1
             | _ -> 0)
        | _ -> 0

    { Diffs = acc.Diffs @ applyDiff @ canDiff @ diffDiff @ locateDiff @ preservedDiff
      Accepted = acc.Accepted + accepted
      Refused = acc.Refused + refused
      InsertRefusals = acc.InsertRefusals + insertRef
      MoveRefusals = acc.MoveRefusals + moveRef
      BatchRefusals = acc.BatchRefusals + batchRef
      GraftRefusals = acc.GraftRefusals + graftRef
      Preempted = acc.Preempted + preempted
      Preserved = acc.Preserved + preserved
      Predicates = Set.add (showKinds kinds) acc.Predicates
      Classes =
        match cls with
        | Some c -> Set.add c acc.Classes
        | None -> acc.Classes }

/// The generated pool, plus probes MINTED PER STATE that address the two capability sites
/// directly: an insert under every id, a move of every leaf under every id, and a batch whose
/// second step is a move into a leaf. The generated pool alone reaches the sites by luck — the
/// probes reach them by construction, which is what the Phase 138 family's own lesson says to do.
let private contDifferential
    (modelApply:
        (TreeOps.tree -> bool) -> TreeOps.op -> TreeOps.tree -> DagFold.outcome<TreeOps.tree, TreeOps.rejection>)
    (seed: int)
    (trials: int)
    : ContTally =
    let mutable r = ConfRng.ofSeed seed
    let mutable tally = emptyContTally
    let mutable n = 0

    for _ in 1..trials do
        let lanes, r1 = treeLaneGen.Lanes 3 r
        let kinds, r2 = drawCanHold r1
        r <- r2
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
            let ids = Tree.ids nodew st |> List.truncate 4

            let leaves =
                Tree.preorder nodew st
                |> List.filter (fun c -> List.isEmpty (nodew.Children c))
                |> List.map nodew.Id
                |> List.truncate 2

            let inserts =
                ids
                |> List.map (fun p -> InsertChild(p, RNode.leaf (sprintf "p140-%s-%d" p n) "para" "v"))

            // Phase 161's site, reached by CONSTRUCTION for the reason the file's header gives: the
            // pool above mints leaf grafts only, and a leaf graft can never carry an interior
            // offender, so the walk would have been certified by a sample that could not reach it.
            // Each graft's root holds a child, so whether it is an offender is exactly whether the
            // drawn predicate admits its kind — and both kinds are minted, so both verdicts arise
            // within one run rather than across lucky seeds.
            let grafts =
                ids
                |> List.collect (fun p ->
                    [ for kind in [ "para"; "section" ] ->
                          InsertChild(
                              p,
                              RNode.node
                                  (sprintf "p161-%s-%s-%d" kind p n)
                                  kind
                                  [ RNode.leaf (sprintf "p161i-%s-%s-%d" kind p n) "para" "v" ]
                          ) ])

            let moves =
                ids |> List.collect (fun p -> leaves |> List.map (fun l -> MoveNode(l, p)))

            let batches =
                match ids, leaves with
                | root :: _, l :: _ ->
                    [ Batch
                          [ InsertChild(root, RNode.leaf (sprintf "p140-b-%d" n) "para" "v")
                            MoveNode(l, root) ]
                      Batch
                          [ InsertChild(root, RNode.leaf (sprintf "p140-c-%d" n) "section" "")
                            InsertChild(l, RNode.leaf (sprintf "p140-d-%d" n) "para" "v") ] ]
                | _ -> []

            for op in generated @ inserts @ grafts @ moves @ batches do
                tally <- contProbe modelApply kinds op st tally

    tally


// ---------------------------------------------------------------------------
//  Phase 160 — the SEQUENCE surface as a third oracle family.
//
//  `Ops.applyAllWith` / `Ops.canApplyAllWith` are the container-aware script pair that closes the
//  gap Phase 140 recorded, and `proofs/Preservation.fst` section 9 models them clause for clause:
//  `apply_all_with` / `can_apply_all_with`, with the failure payload MODELLED rather than dropped
//  (section 8's `can_apply_all` drops the index because nothing there turned on it; here
//  everything does).
//
//  What runs here is the extracted pair beside the shipped pair, over generated scripts and a
//  drawn `canHold` — drawn for the reason the per-op container family draws one, because the
//  theorems quantify over the predicate. Four things are compared, and the last two are the whole
//  of what the sequence surface adds over the per-op one:
//
//    1. the verdict, and the accepted tree through production's own `Tree.encodeHash`
//    2. the rejection CLASS
//    3. the refusal INDEX — which step was refused, not merely that one was
//    4. the PARTIAL TREE the refusal hands back — the state the accepted prefix reached
//
//  A refusal at index 0 returns the caller's own input, so it says nothing about (4). The
//  adequacy guard therefore counts MID-SCRIPT refusals separately and asserts them, exactly as the
//  Phase 140 family counts capability sites separately: a run that met only step-0 refusals would
//  compare the partial tree against the input and certify nothing.
// ---------------------------------------------------------------------------

/// The extracted sequence dry run, as the differential calls it — a seam so the go-red can be
/// substituted for it. The go-red is the model's own `can_apply_all`: section 8's plain sequence
/// check, which is what the shipped `canApplyAll` threaded before this phase.
type private ScriptCan =
    (TreeOps.tree -> bool)
        -> Prims.nat
        -> Prims.list<TreeOps.op>
        -> TreeOps.tree
        -> DagFold.outcome<unit, Prims.nat * TreeOps.rejection>

/// The go-red: the capability-free sequence dry run, lifted into the container-aware signature by
/// discarding the predicate. `can_apply_all_ignores_containment` proves in F\* that it admits a
/// script the container-aware call refuses, so it is a proved weakening before it is measured.
let private scriptGoRedCan: ScriptCan =
    fun _ i os t ->
        match Preservation.can_apply_all os t with
        | DagFold.Ok() -> DagFold.Ok()
        | DagFold.Error e -> DagFold.Error(i, e)

type private ScriptTally =
    {
        Diffs: string list
        Accepted: int
        Refused: int
        /// refusals at an index > 0 — the only probes where the partial tree is something other
        /// than the caller's own input
        MidScript: int
        /// refusals whose class is `NotAContainer` — the class the whole phase exists to make
        /// reachable from a script
        ContainerRefusals: int
        Predicates: Set<string>
    }

let private emptyScriptTally =
    { Diffs = []
      Accepted = 0
      Refused = 0
      MidScript = 0
      ContainerRefusals = 0
      Predicates = Set.empty }

let private renderDiffs (diffs: string list) =
    diffs |> List.truncate 5 |> String.concat "\n"

let private scriptProbe
    (modelCanApply: ScriptCan)
    (kinds: Set<string>)
    (script: SkeletonOp<RNode, string> list)
    (st: RNode)
    (acc: ScriptTally)
    : ScriptTally =
    let canHold (n: RNode) = kinds.Contains(nodew.KindTag n)
    let mCanHold (t: TreeOps.tree) = kinds.Contains(mKind t)
    let mscript = script |> List.map (toModelOpWith toModelTree)
    let mst = toModelTree st
    let zero = Prims.parse_int "0"

    let where =
        sprintf
            "script [%s] at tree %s under canHold={%s}"
            (script |> List.map renderProdOp |> String.concat "; ")
            (prodTreeHash st)
            (showKinds kinds)

    let prod = Ops.applyAllWith canHold nodew idw script st
    let model = Preservation.apply_all_with mCanHold zero mscript mst
    let prodCan = Ops.canApplyAllWith canHold nodew idw script st
    let modelCan = modelCanApply mCanHold zero mscript mst

    // 1-4. applyAllWith: verdict, tree, class, index, partial tree
    let applyDiff, accepted, refused, midScript, containerRef =
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
            0,
            0
        | Error(pi, pe, ppartial), DagFold.Error(mi, me, mpartial) ->
            let pc = prodRejClass pe
            let mc = modelRejClass me

            let classDiff =
                if pc <> mc then
                    [ sprintf "rejection class differs — %s\n  production: %s\n  oracle:     %s" where pc mc ]
                else
                    []

            // The index is the sequence surface's own contribution — which step, not merely that
            // one failed. Compared as a number on both sides; the model's is an unbounded `nat`.
            let indexDiff =
                if System.Numerics.BigInteger(pi) <> mi then
                    [ sprintf "refusal INDEX differs — %s\n  production: %d\n  oracle:     %O" where pi mi ]
                else
                    []

            // And the partial tree: first-refusal-wins keeps the accepted prefix, so this is the
            // state the caller is left holding and the model has to reproduce it exactly.
            let partialDiff =
                let ph = prodTreeHash ppartial
                let mh = modelTreeHash mpartial

                if ph <> mh then
                    [ sprintf "PARTIAL tree differs — %s\n  production: %s\n  oracle:     %s" where ph mh ]
                else
                    []

            (classDiff @ indexDiff @ partialDiff),
            0,
            1,
            (if pi > 0 then 1 else 0),
            (if pc = "NotAContainer" then 1 else 0)
        | Ok _, DagFold.Error(_, me, _) ->
            [ sprintf "production ACCEPTED the script but the oracle rejected (%s) — %s" (modelRejClass me) where ],
            0,
            0,
            0,
            0
        | Error(pi, pe, _), DagFold.Ok _ ->
            [ sprintf
                  "production REJECTED the script at %d (%s) but the oracle accepted — %s"
                  pi
                  (prodRejClass pe)
                  where ],
            0,
            1,
            (if pi > 0 then 1 else 0),
            (if prodRejClass pe = "NotAContainer" then 1 else 0)

    // 5. the dry run, against the model's and against production's own mutating call
    let canDiff =
        let render v =
            match v with
            | Choice1Of2() -> "ok"
            | Choice2Of2(i: string, c: string) -> sprintf "%s@%s" c i

        let pv =
            match prodCan with
            | Ok() -> Choice1Of2()
            | Error(i, e) -> Choice2Of2(string i, prodRejClass e)

        let mv =
            match modelCan with
            | DagFold.Ok() -> Choice1Of2()
            | DagFold.Error(i, e) -> Choice2Of2(string i, modelRejClass e)

        let differs =
            if render pv <> render mv then
                [ sprintf
                      "canApplyAllWith differs — %s\n  production: %s\n  oracle:     %s"
                      where
                      (render pv)
                      (render mv) ]
            else
                []

        // production's own two surfaces, which section 9's `can_apply_all_with_agrees` says agree
        // on the index and the envelope as well as on the verdict
        let prodSelf =
            let fromApply =
                match prod with
                | Ok _ -> Choice1Of2()
                | Error(i, e, _) -> Choice2Of2(string i, prodRejClass e)

            if render pv <> render fromApply then
                [ sprintf
                      "production's canApplyAllWith and applyAllWith DISAGREE — %s\n  dry run: %s\n  apply:   %s"
                      where
                      (render pv)
                      (render fromApply) ]
            else
                []

        differs @ prodSelf

    // 6. and the total instance: `Ops.applyAll` / `Ops.canApplyAll` ARE the `fun _ -> true`
    //    instances, which is the claim that no existing caller moved. Asserted here as well as in
    //    `OpsTests` because this pool is the generated one.
    let instanceDiff =
        let plainApply = Ops.applyAll nodew idw script st
        let totalApply = Ops.applyAllWith (fun _ -> true) nodew idw script st

        let render r =
            match r with
            | Ok t -> "ok:" + prodTreeHash t
            | Error(i, e, t) -> sprintf "%d:%s:%s" i (prodRejClass e) (prodTreeHash t)

        let applySide =
            if render plainApply <> render totalApply then
                [ sprintf
                      "applyAll is NOT applyAllWith (fun _ -> true) — %s\n  applyAll:     %s\n  applyAllWith: %s"
                      where
                      (render plainApply)
                      (render totalApply) ]
            else
                []

        let renderCan r =
            match r with
            | Ok() -> "ok"
            | Error(i, e) -> sprintf "%d:%s" i (prodRejClass e)

        let canSide =
            if
                renderCan (Ops.canApplyAll nodew idw script st)
                <> renderCan (Ops.canApplyAllWith (fun _ -> true) nodew idw script st)
            then
                [ sprintf "canApplyAll is NOT canApplyAllWith (fun _ -> true) — %s" where ]
            else
                []

        applySide @ canSide

    { Diffs = acc.Diffs @ applyDiff @ canDiff @ instanceDiff
      Accepted = acc.Accepted + accepted
      Refused = acc.Refused + refused
      MidScript = acc.MidScript + midScript
      ContainerRefusals = acc.ContainerRefusals + containerRef
      Predicates = Set.add (showKinds kinds) acc.Predicates }

/// The generated pool, plus scripts MINTED to place a container refusal at a chosen POSITION.
/// The generated lanes alone reach a mid-script refusal by luck; a script whose second step is a
/// move into a leaf reaches one by construction, which is what the Phase 138 family's lesson says
/// to do and what the partial-tree comparison needs.
let private scriptDifferential' (modelCanApply: ScriptCan) (seed: int) (trials: int) : ScriptTally =
    let mutable r = ConfRng.ofSeed seed
    let mutable tally = emptyScriptTally
    let mutable n = 0

    for _ in 1..trials do
        let lanes, r1 = treeLaneGen.Lanes 3 r
        let kinds, r2 = drawCanHold r1
        r <- r2
        n <- n + 1
        let generated = List.concat lanes

        let leaves =
            Tree.preorder nodew treeBase
            |> List.filter (fun c -> List.isEmpty (nodew.Children c))
            |> List.map nodew.Id
            |> List.truncate 2

        // a step that lands under a leaf — refused by every predicate that does not admit "para",
        // and accepted (as `apply` accepts it) by one that does
        let intoLeaf =
            match leaves with
            | l :: _ ->
                [ MoveNode("b", l)
                  InsertChild(l, RNode.leaf (sprintf "p160-%d" n) "para" "v") ]
            | [] -> []

        let prefix = generated |> List.truncate 2

        let scripts =
            [ generated ]
            @ (intoLeaf |> List.map (fun probe -> [ probe ])) // refusal at index 0
            @ (intoLeaf |> List.map (fun probe -> prefix @ [ probe ])) // refusal MID-script
            @ (intoLeaf |> List.map (fun probe -> prefix @ [ probe ] @ generated)) // and work after it

        for script in scripts do
            tally <- scriptProbe modelCanApply kinds script treeBase tally

    tally

/// The differential proper — the extracted `can_apply_all_with` in the dry-run slot.
let private scriptDifferential (seed: int) (trials: int) : ScriptTally =
    scriptDifferential' Preservation.can_apply_all_with seed trials

/// The SECOND source (Phase 139): the shared corpus's `apply/` family, decoded through the
/// envelope's own codec and asked of both sides through the same probe.
///
/// Why a committed corpus is worth running beside a generator that already draws thousands of
/// pairs. The generator's pool is whatever its lane draws reach, so it is wide and unchosen —
/// good for finding a disagreement nobody anticipated, and silent about any clause it happens
/// never to draw. The corpus is the opposite: one vector per validator clause per op, authored,
/// and the same bytes four other hosts are asked to certify against. Running it here means the
/// F* model is held to the SAME artefact the hosts are, so "the model agrees with production"
/// and "production agrees with the published contract" stop being two unconnected claims.
let private presCorpusDifferential
    (modelApply: TreeOps.op -> TreeOps.tree -> DagFold.outcome<TreeOps.tree, TreeOps.rejection>)
    (vectors: ApplyVectorExport.ParsedVector list)
    : PresTally =
    vectors
    |> List.fold (fun acc (v: ApplyVectorExport.ParsedVector) -> presProbe modelApply v.Op v.Tree acc) emptyPresTally

// ---------------------------------------------------------------------------
//  Phase 141 — THE DIFF, over PAIRS NOBODY DERIVED FROM ONE ANOTHER.
//
//  `Conformance.diffLaws` has certified `Diff.toOps` since Phase 03, and its sample is narrower
//  than its claim: it builds `before`, then derives `after` by APPLYING random ops to it. So every
//  pair it has ever diffed is one the algebra can already reach, and the interesting half of the
//  claim — that a diff exists between two trees nobody built from one another — was never sampled.
//  This generator draws the two trees INDEPENDENTLY over a shared id space, which is what the
//  theorem in `proofs/TreeDiff.fst` quantifies over.
//
//  ONE CONSTRAINT ON THE PAIR, AND IT IS A PROPERTY OF THE PROBLEM RATHER THAN OF THE GENERATOR.
//  A node's CONTENT is a function of its id, so the two trees agree on every id they share. Skeleton
//  ops relocate and delete nodes; they cannot edit one (`Core.Ops`' remit, stated in `toOps`' own doc
//  comment), so a pair whose shared id carries a different kind in each tree is unreconstructible by
//  ANY skeleton script and asking for one is asking the wrong question. Measured before it was
//  believed: a first generator that redrew each tree's kinds independently reconstructed 1,738 of
//  4,000 pairs; with content keyed to the id, 20,000 of 20,000.
//
//  SIX COMPARISONS, per (before, after, predicate):
//    1. `Diff.toOps` vs the extracted `TreeDiff.to_ops` — verdict, error class, and the SCRIPT
//       operation for operation, not merely its length.
//    2. RECONSTRUCTION through production's own `Tree.encodeHash`: `applyAll (toOps b a) b` is `a`.
//    3. APPLICABILITY: `canApplyAll` accepts every emitted step.
//    4. THE PROVED SHAPE, asserted on the shipped script: the extracted `script_shape` — the
//       conjunction of the four block characterisations `diff_script_shape` proves — is asked of
//       every operation production emitted. That is the theorem held to the engine rather than to
//       the model of it.
//    5. `Diff.toOpsContained` vs `TreeDiff.to_ops_contained` under a DRAWN predicate, and where it
//       produces a script, that script certified through `canApplyAllWith` / `applyAllWith` — the
//       container-aware pair Phase 160 added, which is the executor a contained script belongs to.
//       This is `diff_applicable_contained` asked of the shipped engine.
//    6. where a `TargetNotAContainer` is raised, that the node it names is one `after` really
//       carries, that really has children, and that the predicate really refuses.
//
//  TWO GO-REDS, one per direction the phase claims something.
//    * THE ORDER. `diff_emission_order` proves the script is four homogeneous blocks —
//      inserts, moves, removes, reorders — so a stable partition by operation kind RECOVERS those
//      blocks exactly, and reassembling them with the removes FIRST is "the same four passes in the
//      wrong order". If that permutation reconstructed as often as the real one, the order the
//      source comment argues for would not be load-bearing and the theorem would be about nothing.
//    * THE CONTAINER CHECK. Substituting the model's PLAIN `to_ops` into the contained slot is an
//      engine that "emits an insert under a non-container" because it never looked, and it must
//      disagree with the shipped `toOpsContained` on exactly the pairs whose `after` nests under a
//      leaf.
// ---------------------------------------------------------------------------

/// The shared id space a pair is drawn from. Eight ids plus the root: small enough that two
/// independent draws overlap heavily (so survivors, additions and removals all occur in one pair),
/// large enough that the shapes are not enumerable by accident.
let private diffPool = [ for i in 1..8 -> sprintf "d%d" i ]

/// A node's content, as a function of its id alone — see the constraint above. Deliberately NOT
/// `GetHashCode`: .NET randomises string hashing per process, so a generator keyed off it would
/// draw a different pool on every run and a failure would not reproduce from its seed.
let private diffKindOf (i: string) =
    if i = "root" then
        "doc"
    else
        containerKinds[(int (i.Substring 1)) % List.length containerKinds]

/// One well-formed tree over a random SUBSET of the pool, grown by attaching each drawn id under a
/// uniformly random already-placed node. Nothing about it consults the other tree of the pair.
let private genPairTree (r0: ConfRng.T) : RNode * ConfRng.T =
    let mutable r = r0
    let arr = List.toArray diffPool

    for i in (arr.Length - 1) .. -1 .. 1 do
        let j, r' = ConfRng.intBelow (i + 1) r
        r <- r'
        let t = arr[i]
        arr[i] <- arr[j]
        arr[j] <- t

    let take, r1 = ConfRng.intBelow (arr.Length + 1) r
    r <- r1
    let chosen = arr |> Array.truncate take |> Array.toList

    let mutable placed = [ "root" ]
    let mutable kidsOf = Map.ofList [ "root", ([]: string list) ]

    for c in chosen do
        let pi, r' = ConfRng.intBelow (List.length placed) r
        r <- r'
        let p = placed[pi]
        kidsOf <- kidsOf |> Map.add p (kidsOf[p] @ [ c ]) |> Map.add c []
        placed <- placed @ [ c ]

    let rec build i =
        { RNode.leaf i (diffKindOf i) "v" with
            Children = kidsOf[i] |> List.map build }

    build "root", r

/// The contained-diff seam, so the go-red can be substituted for it.
type private ContainedDiff =
    (TreeOps.tree -> bool) -> TreeOps.tree -> TreeOps.tree -> DagFold.outcome<TreeOps.op list, TreeDiff.diff_error>

/// The container go-red: the model's own PLAIN diff, lifted into the contained signature by
/// discarding the predicate. It is a weakening by construction — `to_ops_contained` at a predicate
/// that refuses nothing IS `to_ops` (`diff_contained_at_total_is_plain`), so this is that instance
/// handed a predicate that refuses something.
let private diffGoRedContained: ContainedDiff = fun _ b a -> TreeDiff.to_ops b a

/// The order go-red: the same four blocks with the REMOVES FIRST. A stable partition by operation
/// kind recovers the blocks because `diff_emission_order` proves there are exactly four of them, in
/// that order — so this is a permutation of the passes and not a different algorithm.
let private removesFirst (ops: SkeletonOp<RNode, string> list) =
    let pick f = ops |> List.filter f

    pick (function
        | RemoveNode _ -> true
        | _ -> false)
    @ pick (function
        | InsertChild _ -> true
        | _ -> false)
    @ pick (function
        | MoveNode _ -> true
        | _ -> false)
    @ pick (function
        | ReorderChildren _ -> true
        | _ -> false)

/// Phase 167 — the go-red for `diff_reconstructs`' HYPOTHESIS. `diffKindOf` keys a node's content
/// to its id, which since Phase 167 is the theorem's own `kinds_agree` precondition and not merely
/// a property of this generator. This is a SECOND function of the same id: a shared id then names a
/// different kind in each tree, which is the one shape no skeleton script can express, because no
/// operation in the alphabet edits a node. `TreeDiff.kinds_agree_is_necessary` pins that
/// counterexample in the model; the test below measures it on the shipped engine. The root is left
/// at "doc" in both trees deliberately — a pair whose ROOTS disagree would fail to reconstruct
/// without ever reaching a shared child, which would measure the wrong thing.
let private diffKindShifted (i: string) =
    if i = "root" then
        "doc"
    else
        containerKinds[((int (i.Substring 1)) + 1) % List.length containerKinds]

/// Re-kind every node of a generated tree through `kindOf`, leaving the shape and the ids exactly
/// as the generator drew them — so the probe differs from the green run in the KINDS and in nothing
/// else.
let rec private reKind (kindOf: string -> string) (n: RNode) : RNode =
    { n with
        Kind = kindOf n.Id
        Children = n.Children |> List.map (reKind kindOf) }

let rec private idsOfTree (n: RNode) : string list =
    n.Id :: (n.Children |> List.collect idsOfTree)

let private prodDiffRender (r: Result<SkeletonOp<RNode, string> list, Diff.DiffError<string>>) =
    match r with
    | Ok ops -> "ok:" + (ops |> List.map renderProdOp |> String.concat ";")
    | Error(Diff.RootIdMismatch(b, a)) -> sprintf "err:RootIdMismatch(%s,%s)" b a
    | Error(Diff.DuplicateIdInTree d) -> sprintf "err:DuplicateIdInTree(%s)" d
    | Error(Diff.TargetNotAContainer(p, k)) -> sprintf "err:TargetNotAContainer(%s,%s)" p k

let private modelDiffRender (r: DagFold.outcome<TreeOps.op list, TreeDiff.diff_error>) =
    match r with
    | DagFold.Ok ops -> "ok:" + (ops |> List.map renderModelOp |> String.concat ";")
    | DagFold.Error(TreeDiff.RootIdMismatch(b, a)) -> sprintf "err:RootIdMismatch(%s,%s)" b a
    | DagFold.Error(TreeDiff.DuplicateIdInTree d) -> sprintf "err:DuplicateIdInTree(%s)" d
    | DagFold.Error(TreeDiff.TargetNotAContainer(p, k)) -> sprintf "err:TargetNotAContainer(%s,%s)" p k

type private DiffTally =
    {
        Diffs: string list
        Pairs: int
        /// pairs whose script carries at least one operation of each kind — counted per kind,
        /// because a pool that never removed anything could not have met the order go-red
        Inserted: int
        Removed: int
        Moved: int
        Reordered: int
        /// pairs the two draws made identical — the empty script, worth reaching at all
        Empty: int
        /// contained diffs that produced a script, and ones refused for containment
        Contained: int
        ContainerRefused: int
        /// pairs where the removes-first permutation of the SAME four blocks fails to reconstruct
        OrderBroken: int
        Predicates: Set<string>
    }

let private emptyDiffTally =
    { Diffs = []
      Pairs = 0
      Inserted = 0
      Removed = 0
      Moved = 0
      Reordered = 0
      Empty = 0
      Contained = 0
      ContainerRefused = 0
      OrderBroken = 0
      Predicates = Set.empty }

let private diffProbe
    (containedDiff: ContainedDiff)
    (kinds: Set<string>)
    (before: RNode)
    (after: RNode)
    (acc: DiffTally)
    : DiffTally =
    let canHold (n: RNode) = kinds.Contains(nodew.KindTag n)
    let mCanHold (t: TreeOps.tree) = kinds.Contains(mKind t)
    let mb = toModelTree before
    let ma = toModelTree after

    let where =
        sprintf "pair %s -> %s under canHold={%s}" (prodTreeHash before) (prodTreeHash after) (showKinds kinds)

    let prod = Diff.toOps nodew idw before after
    let model = TreeDiff.to_ops mb ma

    // 1. the verdict and the script, operation for operation
    let scriptDiff =
        if prodDiffRender prod <> modelDiffRender model then
            [ sprintf
                  "toOps differs — %s\n  production: %s\n  oracle:     %s"
                  where
                  (prodDiffRender prod)
                  (modelDiffRender model) ]
        else
            []

    // 2-4. reconstruction, applyability, and the PROVED shape asked of the shipped script
    let roundTripDiff, ins, rem, mov, reo, emptyScript, orderBroken =
        match prod with
        | Error e ->
            [ sprintf "toOps REFUSED an independently generated well-formed pair (%A) — %s" e where ], 0, 0, 0, 0, 0, 0
        | Ok ops ->
            let rebuilt = Ops.applyAll nodew idw ops before

            let reconstruction =
                match rebuilt with
                | Ok t when prodTreeHash t = prodTreeHash after -> []
                | Ok t -> [ sprintf "applyAll(toOps) did NOT reconstruct — %s\n  got: %s" where (prodTreeHash t) ]
                | Error(i, e, _) ->
                    [ sprintf "applyAll(toOps) was REFUSED at step %d (%s) — %s" i (prodRejClass e) where ]

            let applyability =
                match Ops.canApplyAll nodew idw ops before with
                | Ok() -> []
                | Error(i, e) ->
                    [ sprintf "canApplyAll REFUSED the emitted script at step %d (%s) — %s" i (prodRejClass e) where ]

            // the theorem, asked of the engine: `script_shape` is the conjunction of the four block
            // characterisations `diff_script_shape` proves, evaluated over production's own output
            let shape =
                let mops = ops |> List.map (toModelOpWith toModelTree)

                if TreeDiff.all_ops (TreeDiff.script_shape mb ma) mops then
                    []
                else
                    [ sprintf
                          "the shipped script violates the PROVED block shape — %s\n  script: %s"
                          where
                          (ops |> List.map renderProdOp |> String.concat ";") ]

            let has f = ops |> List.exists f

            let broken =
                match Ops.applyAll nodew idw (removesFirst ops) before with
                | Ok t when prodTreeHash t = prodTreeHash after -> 0
                | _ -> 1

            reconstruction @ applyability @ shape,
            (if
                 has (function
                     | InsertChild _ -> true
                     | _ -> false)
             then
                 1
             else
                 0),
            (if
                 has (function
                     | RemoveNode _ -> true
                     | _ -> false)
             then
                 1
             else
                 0),
            (if
                 has (function
                     | MoveNode _ -> true
                     | _ -> false)
             then
                 1
             else
                 0),
            (if
                 has (function
                     | ReorderChildren _ -> true
                     | _ -> false)
             then
                 1
             else
                 0),
            (if List.isEmpty ops then 1 else 0),
            broken

    // 5-6. the container-aware mirror, and the executor a contained script belongs to
    let prodC = Diff.toOpsContained canHold nodew idw before after
    let modelC = containedDiff mCanHold mb ma

    let containedDiffs, contained, refused =
        let agreement =
            if prodDiffRender prodC <> modelDiffRender modelC then
                [ sprintf
                      "toOpsContained differs — %s\n  production: %s\n  oracle:     %s"
                      where
                      (prodDiffRender prodC)
                      (modelDiffRender modelC) ]
            else
                []

        match prodC with
        | Ok ops ->
            let recon =
                match Ops.applyAllWith canHold nodew idw ops before with
                | Ok t when prodTreeHash t = prodTreeHash after -> []
                | Ok t ->
                    [ sprintf "applyAllWith(toOpsContained) did NOT reconstruct — %s\n  got: %s" where (prodTreeHash t) ]
                | Error(i, e, _) ->
                    [ sprintf "applyAllWith REFUSED a contained script at step %d (%s) — %s" i (prodRejClass e) where ]

            let dry =
                match Ops.canApplyAllWith canHold nodew idw ops before with
                | Ok() -> []
                | Error(i, e) ->
                    [ sprintf
                          "canApplyAllWith REFUSED a contained script at step %d (%s) — the emitted parents were supposed to be containers — %s"
                          i
                          (prodRejClass e)
                          where ]

            agreement @ recon @ dry, 1, 0
        | Error(Diff.TargetNotAContainer(p, k)) ->
            let located =
                match Tree.tryFind nodew idw p after with
                | Some n when not (List.isEmpty (nodew.Children n)) && not (canHold n) && nodew.KindTag n = k -> []
                | _ ->
                    [ sprintf
                          "TargetNotAContainer(%s,%s) does not locate a childful non-container of `after` — %s"
                          p
                          k
                          where ]

            agreement @ located, 0, 1
        | Error e ->
            agreement
            @ [ sprintf "toOpsContained refused with %A on a well-formed pair — %s" e where ],
            0,
            0

    { Diffs = acc.Diffs @ scriptDiff @ roundTripDiff @ containedDiffs
      Pairs = acc.Pairs + 1
      Inserted = acc.Inserted + ins
      Removed = acc.Removed + rem
      Moved = acc.Moved + mov
      Reordered = acc.Reordered + reo
      Empty = acc.Empty + emptyScript
      Contained = acc.Contained + contained
      ContainerRefused = acc.ContainerRefused + refused
      OrderBroken = acc.OrderBroken + orderBroken
      Predicates = Set.add (showKinds kinds) acc.Predicates }

let private diffDifferential' (containedDiff: ContainedDiff) (seed: int) (trials: int) : DiffTally =
    let mutable r = ConfRng.ofSeed seed
    let mutable tally = emptyDiffTally

    for _ in 1..trials do
        let before, r1 = genPairTree r
        let after, r2 = genPairTree r1
        let kinds, r3 = drawCanHold r2
        r <- r3
        tally <- diffProbe containedDiff kinds before after tally

    tally

let private diffDifferential (seed: int) (trials: int) : DiffTally =
    diffDifferential' TreeDiff.to_ops_contained seed trials

// ---------------------------------------------------------------------------
//  Phase 152 — KEY ORDER, differentially.
//
//  WIRE_FORMAT §2 rule 2 obliges a decoder to accept an object's members in any order, and §20
//  ratifies that the same bytes decode to the same tree on every conformant host. Every fixture
//  in the corpus is canonically ordered, so a decoder that silently read member order would pass
//  all of them — which is what makes this family different from the Phase 135 one above. It
//  SHUFFLES: every object of every document, at every depth, seeded and replayable, and then asks
//  three questions of each shuffle.
//
//    1. Is the shuffle an instance of the theorem at all? The extracted `member_perm` — the model's
//       own relation, not a second one written here — must hold of the pair. A shuffle the
//       relation does not relate would be measuring something the lemma never claimed.
//    2. Does PRODUCTION answer the same on the shuffle as on the original? Every combinator whose
//       result carries no members is compared for equality, message included; `getProp`, whose
//       result is a subtree and so is itself reordered, is compared through the model's
//       `outcome_perm`.
//    3. Does the oracle still agree with production on the shuffled document? The Phase 135 probes,
//       on documents no fixture contains.
//
//  Duplicate-keyed documents are FILTERED OUT, using the extracted `keys_unique_deep` rather than
//  a predicate written here, because on a repeated key `List.tryFind` genuinely does depend on
//  order — see `duplicate_keys_break_order_invariance`, which proves the premise necessary rather
//  than assuming it away. Arrays are NOT shuffled: an array's order is content.
// ---------------------------------------------------------------------------

/// Fisher–Yates over the suite's own replayable generator.
let private shuffleList (xs: 'a list) (r0: ConfRng.T) : 'a list * ConfRng.T =
    let arr = List.toArray xs
    let mutable r = r0

    for i in (arr.Length - 1) .. -1 .. 1 do
        let j, r' = ConfRng.intBelow (i + 1) r
        r <- r'
        let tmp = arr.[i]
        arr.[i] <- arr.[j]
        arr.[j] <- tmp

    List.ofArray arr, r

/// Reorder the members of EVERY object in the document, at every depth. Arrays keep their order.
let rec private shuffleDeep (v: JVal) (r0: ConfRng.T) : JVal * ConfRng.T =
    match v with
    | JArr xs ->
        let items, r =
            xs
            |> List.fold
                (fun (acc, rr) x ->
                    let x', rr' = shuffleDeep x rr
                    acc @ [ x' ], rr')
                ([], r0)

        JArr items, r
    | JObj fields ->
        let inner, r1 =
            fields
            |> List.fold
                (fun (acc, rr) (k, x) ->
                    let x', rr' = shuffleDeep x rr
                    acc @ [ k, x' ], rr')
                ([], r0)

        let reordered, r2 = shuffleList inner r1
        JObj reordered, r2
    | _ -> v, r0

/// The model's own deep duplicate-free predicate, asked of a production value.
let private keysUniqueDeep (v: JVal) : bool = WireDecode.keys_unique_deep (toModel v)

/// The model's own relation, asked of a pair of production values.
let private relatedByModel (a: JVal) (b: JVal) : bool =
    WireDecode.member_perm (toModel a) (toModel b)

/// A decoder — the parameter is `getProp`, so the go-red instrument below can be the SAME decoder
/// reading members by position instead of by name. Everything else is `Wire.Decode`.
type private GetProp = string -> JVal -> Result<JVal, string>

let rec private decodeRefWith (getProp: GetProp) (el: JVal) : Result<RefNode, string> =
    let strField name e =
        getProp name e |> Result.bind Decode.asString

    match getProp "kind" el |> Result.bind Decode.asString with
    | Error m -> Error m
    | Ok tag ->
        if tag = "text" then
            strField "value" el |> Result.map RefText
        elif tag = "flag" then
            getProp "on" el |> Result.bind Decode.asBool |> Result.map RefFlag
        elif tag = "tags" then
            getProp "tags" el
            |> Result.bind (Decode.mapList Decode.asString)
            |> Result.map RefTags
        elif tag = "group" then
            match strField "id" el with
            | Error m -> Error m
            | Ok id ->
                match getProp "items" el with
                | Error m -> Error m
                | Ok(JArr ys) ->
                    let rec go acc rest =
                        match rest with
                        | [] -> Ok(List.rev acc)
                        | x :: t ->
                            match decodeRefWith getProp x with
                            | Ok n -> go (n :: acc) t
                            | Error m -> Error m

                    go [] ys |> Result.map (fun ns -> RefGroup(id, ns))
                | Ok other -> Error("expected array, got " + kindWord other)
        else
            Error("unknown kind: " + tag)

/// THE GO-RED INSTRUMENT: a `getProp` that reads the FIRST member of an object rather than the
/// one it was asked for. On the canonically-ordered corpus it is very nearly right — which is the
/// point, and why the corpus alone could not catch it. Under a shuffle it must lose.
let private getPropByPosition (name: string) (el: JVal) : Result<JVal, string> =
    match el with
    | JObj((_, v) :: _) -> Ok v
    | JObj [] -> Error("missing property: " + name)
    | other -> Error("expected object, got " + kindWord other)

/// Production's answers to the questions the theorem says are order-insensitive. Each result
/// carries no members of its own, so these are compared for EQUALITY — message included.
let private orderFreeAnswers (getProp: GetProp) (el: JVal) : (string * string) list =
    [ "kindOf", resR asStr (getProp "kind" el |> Result.bind Decode.asString)
      "strField id", resR asStr (getProp "id" el |> Result.bind Decode.asString)
      "strField value", resR asStr (getProp "value" el |> Result.bind Decode.asString)
      "intField n", resR string (getProp "n" el |> Result.bind Decode.asInt)
      "asString", resR asStr (Decode.asString el)
      "asInt", resR string (Decode.asInt el)
      "asBool", resR string (Decode.asBool el)
      "asFloat", resR asFlt (Decode.asFloat el)
      "mapList asString", resR asStrs (Decode.mapList Decode.asString el)
      "decodeRef", resR renderRef (decodeRefWith getProp el) ]

/// `getProp`'s own result is a SUBTREE, so the two answers are related rather than equal — and
/// the relation they are compared by is the extracted model's, which is the one the lemma is
/// about.
let private getPropRelated (getProp: GetProp) (name: string) (a: JVal) (b: JVal) : bool =
    let toO (r: Result<JVal, string>) : WireDecode.outcome<MJVal> =
        match r with
        | Ok v -> WireDecode.Ok(toModel v)
        | Error m -> WireDecode.Error m

    WireDecode.outcome_perm (toO (getProp name a)) (toO (getProp name b))

type private ShuffleTally =
    {
        Diffs: string list
        /// Documents the filter admitted (duplicate-free at every depth).
        Admitted: int
        /// Shuffles that actually MOVED a member — without these the family re-runs Phase 135's.
        Moved: int
        /// Values the node decoder ACCEPTED, across all shuffles.
        Accepted: int
        /// Values it refused. Both arms must be reached, or the agreement certifies one of them.
        Refused: int
        /// Pairs the extracted relation did NOT relate — a defect in the shuffle, not in the tree.
        Unrelated: int
    }

let private emptyShuffleTally =
    { Diffs = []
      Admitted = 0
      Moved = 0
      Accepted = 0
      Refused = 0
      Unrelated = 0 }

/// One document, `trials` shuffles of it. `getProp` is `Decode.getProp` in every real run and the
/// positional instrument only in the go-red case.
let private shuffleProbe
    (getProp: GetProp)
    (label: string)
    (trials: int)
    (seed: int)
    (el: JVal)
    (tally: ShuffleTally)
    : ShuffleTally =
    if not (keysUniqueDeep el) then
        tally
    else
        let baseline = orderFreeAnswers getProp el
        let mutable r = ConfRng.ofSeed seed

        let mutable t =
            { tally with
                Admitted = tally.Admitted + 1 }

        for i in 1..trials do
            let shuffled, r' = shuffleDeep el r
            r <- r'

            if shuffled <> el then
                t <- { t with Moved = t.Moved + 1 }

            // 1. the shuffle is an instance of the relation the lemma is about
            if not (relatedByModel el shuffled) then
                t <-
                    { t with
                        Unrelated = t.Unrelated + 1
                        Diffs =
                            t.Diffs
                            @ [ sprintf
                                    "%s/%d: the extracted member_perm does NOT relate the document to its shuffle\n  original: %s\n  shuffled: %s"
                                    label
                                    i
                                    (Json.render el)
                                    (Json.render shuffled) ] }

            // 2. production answers the same on the shuffle as on the original
            let answers = orderFreeAnswers getProp shuffled

            for idx in 0 .. baseline.Length - 1 do
                let name, before = baseline.[idx]
                let _, after = answers.[idx]

                if before <> after then
                    t <-
                        { t with
                            Diffs =
                                t.Diffs
                                @ [ sprintf
                                        "%s/%d: %s MOVED under a member reordering\n  original: %s -> %s\n  shuffled: %s -> %s"
                                        label
                                        i
                                        name
                                        (Json.render el)
                                        before
                                        (Json.render shuffled)
                                        after ] }

            for name in [ "kind"; "id"; "value"; "items"; "tags"; "on"; "no-such-member" ] do
                if not (getPropRelated getProp name el shuffled) then
                    t <-
                        { t with
                            Diffs =
                                t.Diffs
                                @ [ sprintf
                                        "%s/%d: getProp %s answers an UNRELATED subtree under a member reordering\n  original: %s\n  shuffled: %s"
                                        label
                                        i
                                        name
                                        (Json.render el)
                                        (Json.render shuffled) ] }

            match decodeRefWith getProp shuffled with
            | Ok _ -> t <- { t with Accepted = t.Accepted + 1 }
            | Error _ -> t <- { t with Refused = t.Refused + 1 }

        t

/// The tally's own vacuity guards: a run that admitted nothing, or that never actually moved a
/// member, proves nothing whatever else it reports. Which arms of the node decoder a pool reaches
/// is a property of that pool, so each case asserts its own.
let private expectShuffleAgreement (label: string) (t: ShuffleTally) =
    match t.Diffs with
    | d :: _ -> failtestf "%s: a member reordering CHANGED a decode answer\n%s" label d
    | [] ->
        Expect.isGreaterThan t.Admitted 0 (sprintf "%s: no document passed the duplicate-free filter" label)

        Expect.isGreaterThan
            t.Moved
            0
            (sprintf "%s: no shuffle actually moved a member — the family re-ran the unshuffled probes" label)

//  Phase 149 — the CANONICAL ENCODER. `proofs/WireCanon.fst`'s extracted `render`
//  beside `Fuaran.Core.Canon.render`, byte for byte.
//
//  The model is named `WireCanon` and not `Canon` for the reason `TreeOps.fst` is
//  not called `Ops` and `TreeDiff.fst` is not called `Diff`: the extracted oracle is
//  a top-level F# module and this host opens `Fuaran.Core`, which already carries a
//  `Canon`. The two would shadow each other exactly where the differential needs both.
// ---------------------------------------------------------------------------

/// The model's hex nibble, by value. Used in both directions of the bridge.
let private canonHexdOf (n: int) : WireCanon.hexd =
    match n with
    | 0 -> WireCanon.HD0
    | 1 -> WireCanon.HD1
    | 2 -> WireCanon.HD2
    | 3 -> WireCanon.HD3
    | 4 -> WireCanon.HD4
    | 5 -> WireCanon.HD5
    | 6 -> WireCanon.HD6
    | 7 -> WireCanon.HD7
    | 8 -> WireCanon.HD8
    | 9 -> WireCanon.HD9
    | 10 -> WireCanon.HDa
    | 11 -> WireCanon.HDb
    | 12 -> WireCanon.HDc
    | 13 -> WireCanon.HDd
    | 14 -> WireCanon.HDe
    | 15 -> WireCanon.HDf
    | _ -> failwithf "not a hex nibble: %d" n

let private canonHexdChar (d: WireCanon.hexd) : char =
    match d with
    | WireCanon.HD0 -> '0'
    | WireCanon.HD1 -> '1'
    | WireCanon.HD2 -> '2'
    | WireCanon.HD3 -> '3'
    | WireCanon.HD4 -> '4'
    | WireCanon.HD5 -> '5'
    | WireCanon.HD6 -> '6'
    | WireCanon.HD7 -> '7'
    | WireCanon.HD8 -> '8'
    | WireCanon.HD9 -> '9'
    | WireCanon.HDa -> 'a'
    | WireCanon.HDb -> 'b'
    | WireCanon.HDc -> 'c'
    | WireCanon.HDd -> 'd'
    | WireCanon.HDe -> 'e'
    | WireCanon.HDf -> 'f'

/// A .NET `char` as one of the model's constructors. TOTAL and CLASSIFYING: every character the
/// canonical encoder distinguishes has its own constructor, and `CPlain` catches the rest
/// VERBATIM — so two different ordinary characters are never identified, and `WireCanon.bridged`
/// (the model's own statement of what this function has to be: a `CPlain` never carries a
/// spelling another constructor already denotes) holds of everything produced here by
/// construction. That is the level-3 assumption, written where it is discharged.
let private canonToCh (c: char) : WireCanon.ch =
    match c with
    | '"' -> WireCanon.CQuote
    | '\\' -> WireCanon.CBackslash
    | '{' -> WireCanon.CLBrace
    | '}' -> WireCanon.CRBrace
    | '[' -> WireCanon.CLBrack
    | ']' -> WireCanon.CRBrack
    | ':' -> WireCanon.CColon
    | ',' -> WireCanon.CComma
    | '-' -> WireCanon.CMinus
    | '+' -> WireCanon.CPlus
    | '.' -> WireCanon.CDot
    | 'E' -> WireCanon.CUpE
    | 'u' -> WireCanon.CLu
    | c when c < ' ' -> WireCanon.CCtrl(int c >= 16, canonHexdOf (int c % 16))
    | c when c >= '0' && c <= '9' -> WireCanon.CHexCh(canonHexdOf (int c - int '0'))
    | c when c >= 'a' && c <= 'f' -> WireCanon.CHexCh(canonHexdOf (int c - int 'a' + 10))
    | c -> WireCanon.CPlain(string c)

let private canonFromCh (c: WireCanon.ch) : string =
    match c with
    | WireCanon.CQuote -> "\""
    | WireCanon.CBackslash -> "\\"
    | WireCanon.CLBrace -> "{"
    | WireCanon.CRBrace -> "}"
    | WireCanon.CLBrack -> "["
    | WireCanon.CRBrack -> "]"
    | WireCanon.CColon -> ":"
    | WireCanon.CComma -> ","
    | WireCanon.CMinus -> "-"
    | WireCanon.CPlus -> "+"
    | WireCanon.CDot -> "."
    | WireCanon.CUpE -> "E"
    | WireCanon.CLu -> "u"
    | WireCanon.CHexCh d -> string (canonHexdChar d)
    | WireCanon.CCtrl(hi, lo) ->
        string (
            char (
                (if hi then 16 else 0)
                + int (
                    canonHexdChar lo
                    |> fun ch ->
                        if ch <= '9' then
                            int ch - int '0'
                        else
                            int ch - int 'a' + 10
                )
            )
        )
    | WireCanon.CPlain s -> s

let private canonToChs (s: string) : WireCanon.ch list = s |> Seq.map canonToCh |> List.ofSeq

let private canonFromChs (l: WireCanon.ch list) : string =
    l |> List.map canonFromCh |> String.concat ""

let rec private canonToModel (v: JVal) : WireCanon.jval<int, float> =
    match v with
    | JStr s -> WireCanon.JStr(canonToChs s)
    | JInt i -> WireCanon.JInt i
    | JBool b -> WireCanon.JBool b
    | JFloat f -> WireCanon.JFloat f
    | JArr xs -> WireCanon.JArr(xs |> List.map canonToModel)
    | JObj fs -> WireCanon.JObj(fs |> List.map (fun (k, v) -> (canonToChs k, canonToModel v)))

let rec private canonOfModel (v: WireCanon.jval<int, float>) : JVal =
    match v with
    | WireCanon.JStr s -> JStr(canonFromChs s)
    | WireCanon.JInt i -> JInt i
    | WireCanon.JBool b -> JBool b
    | WireCanon.JFloat f -> JFloat f
    | WireCanon.JArr xs -> JArr(xs |> List.map canonOfModel)
    | WireCanon.JObj fs -> JObj(fs |> List.map (fun (k, v) -> (canonFromChs k, canonOfModel v)))

/// The `wire` the model is parametric over, instantiated at production's own layouts. Two of the
/// seven fields are worth naming.
///
/// `float_str` is `Double.ToString("R", InvariantCulture)` DIRECTLY and not `Canon.canonicalFloat`,
/// so the `-0` collapse and the three non-finite tokens stay the MODEL's clauses to get right
/// rather than being handed to it — that is the difference between a comparison and a tautology.
/// The digits themselves are .NET's, per the opaque-numeral boundary theorems 1 and 4 also draw.
///
/// `tok_read` is the numeral READ-BACK the model does not compute: production's own parser over
/// the token the model scanned. `tok_read_ok`, the one premise the theorems carry, is exactly the
/// claim that this function inverts the two layouts above — which is what rule 5 means by "the
/// shortest digit sequence that ROUND-TRIPS", so the premise is the layout's definition rather
/// than an extra assumption about it.
let private canonWire: WireCanon.wire<int, float> =
    { int_str = fun i -> canonToChs (string i)
      float_str = fun f -> canonToChs (f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
      fclass =
        fun f ->
            if System.Double.IsNaN f then
                WireCanon.FNaN
            elif System.Double.IsPositiveInfinity f then
                WireCanon.FPosInf
            elif System.Double.IsNegativeInfinity f then
                WireCanon.FNegInf
            else
                WireCanon.FFinite
      is_zero = fun f -> f = 0.0
      pos_zero = 0.0
      key_le = fun a b -> System.String.CompareOrdinal(canonFromChs a, canonFromChs b) <= 0
      tok_read =
        fun t ->
            match Json.parse (canonFromChs t) with
            | Result.Ok v -> WireCanon.Ok(canonToModel v)
            | Result.Error m -> WireCanon.Error m }

/// The GO-RED instrument, and this family's counterpart to the fold family's blind footprint and
/// the decode family's blind integer bridge: rule 2's comparator REVERSED, so the sort the rule
/// mandates still runs but orders keys the other way. Every object carrying two distinct keys must
/// then disagree with production, and a document carrying none still agrees — so the comparison is
/// known to be both one that can lose and one that is narrow to the rule it is about.
let private canonWireGoRed: WireCanon.wire<int, float> =
    { canonWire with
        key_le = fun a b -> System.String.CompareOrdinal(canonFromChs a, canonFromChs b) >= 0 }

/// Rule 5's own slot rule, as a predicate on a value: a token with no `.`, no `e`/`E` and a
/// magnitude inside the int53 window "keeps integer identity", so a float whose canonical token
/// carries neither marker is INDISTINGUISHABLE ON THE WIRE from the integer of that token. That is
/// the model's `canonical`, and the subset every theorem in section 10 is stated over.
let rec private isCanonicalValue (v: JVal) : bool =
    match v with
    | JFloat f ->
        System.Double.IsFinite f
        && (let t = Canon.canonicalFloat f in t.Contains "." || t.Contains "E")
    | JArr xs -> xs |> List.forall isCanonicalValue
    | JObj fs -> fs |> List.forall (snd >> isCanonicalValue)
    | _ -> true

/// The extracted model is a CHARACTER-LIST interpreter, and F*'s F# backend emits plain recursion
/// with no tail calls (README, finding 3's neighbour: the backend is second-class upstream). So
/// rendering a multi-kilobyte corpus fixture walks a stack proportional to the document's BYTES,
/// and the largest fixture in the corpus overflows the default 1 MB one. That is a property of the
/// EXTRACTION and not of the model — the theorem is about a function, not about a runtime's frame
/// budget — so the differential runs on a thread with a stack sized for the corpus rather than
/// shrinking the pool until it fits the default. Shrinking would silently narrow what the corpus
/// leg certifies, and the fixture it would drop first is the deepest one.
let private onBigStack (f: unit -> 'a) : 'a =
    let mutable result = Unchecked.defaultof<'a>
    let mutable failure: exn = null

    let body () =
        try
            result <- f ()
        with e ->
            failure <- e

    let t = System.Threading.Thread(body, 128 * 1024 * 1024)
    t.Start()
    t.Join()

    if not (isNull failure) then
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()

    result

type private CanonTally =
    {
        Docs: int
        Diffs: string list
        /// Objects carrying two or more DISTINCT keys — the shape rule 2's sort is observable on,
        /// and therefore the shape the go-red needs to have met.
        SortableObjects: int
        /// Strings carrying a character rule 6 escapes.
        EscapedStrings: int
        /// Strings carrying a control character — the `\u00xx` arm specifically.
        ControlStrings: int
        /// Floats whose canonical token is in scientific notation — rule 5's other layout.
        ScientificFloats: int
        /// Canonical documents whose model round trip was checked.
        RoundTrips: int
    }

let private emptyCanonTally =
    { Docs = 0
      Diffs = []
      SortableObjects = 0
      EscapedStrings = 0
      ControlStrings = 0
      ScientificFloats = 0
      RoundTrips = 0 }

let rec private canonShape (v: JVal) (t: CanonTally) : CanonTally =
    match v with
    | JStr s ->
        let t =
            if s |> Seq.exists (fun c -> c = '"' || c = '\\' || c < ' ') then
                { t with
                    EscapedStrings = t.EscapedStrings + 1 }
            else
                t

        if s |> Seq.exists (fun c -> c < ' ') then
            { t with
                ControlStrings = t.ControlStrings + 1 }
        else
            t
    | JFloat f when System.Double.IsFinite f && (Canon.canonicalFloat f).Contains "E" ->
        { t with
            ScientificFloats = t.ScientificFloats + 1 }
    | JArr xs -> xs |> List.fold (fun acc x -> canonShape x acc) t
    | JObj fs ->
        let t =
            if (fs |> List.map fst |> List.distinct |> List.length) >= 2 then
                { t with
                    SortableObjects = t.SortableObjects + 1 }
            else
                t

        fs |> List.fold (fun acc (_, x) -> canonShape x acc) t
    | _ -> t

/// One document, asked of production and of the model. The comparison is the BYTES — which is the
/// only comparison that means anything for an encoder whose whole job is to produce a digest input.
let private canonProbe (w: WireCanon.wire<int, float>) (label: string) (v: JVal) (t: CanonTally) : CanonTally =
    let expected = Canon.render v
    let got = canonFromChs (WireCanon.render w (canonToModel v))

    let t = canonShape v { t with Docs = t.Docs + 1 }

    if expected = got then
        t
    else
        { t with
            Diffs =
                sprintf "%s: production rendered\n  %s\nthe model rendered\n  %s" label expected got
                :: t.Diffs }

/// The value-level round trip, on the canonical subset: the model's own reader over the model's
/// own rendering, against production's parser over production's rendering. A disagreement here
/// says the reader and the encoder have drifted apart, which is the one way the injectivity
/// theorem could be true of a model that is not this encoder.
let private canonRoundTrip (label: string) (v: JVal) (t: CanonTally) : CanonTally =
    if not (isCanonicalValue v) then
        t
    else
        let t = { t with RoundTrips = t.RoundTrips + 1 }

        let modelSide =
            match WireCanon.read canonWire (WireCanon.render canonWire (canonToModel v)) with
            | WireCanon.Ok(mv, []) -> Result.Ok(canonOfModel mv)
            | WireCanon.Ok(_, rest) -> Result.Error(sprintf "the reader left %d characters unread" (List.length rest))
            | WireCanon.Error m -> Result.Error m

        let productionSide =
            match Json.parse (Canon.render v) with
            | Result.Ok pv -> Result.Ok pv
            | Result.Error m -> Result.Error m

        if modelSide = productionSide then
            t
        else
            { t with
                Diffs =
                    sprintf "%s: round trip — production read %A, the model read %A" label productionSide modelSide
                    :: t.Diffs }

// ---- the generated pool ----

let private canonKeys =
    [| "$type"
       "a"
       "b"
       "Z"
       "z"
       "0"
       "_x"
       "é"
       "\u00e9x"
       "\U0001D11E"
       "\uE000"
       "kind"
       "A"
       "aa" |]

let private canonChars =
    [| "a"
       "Z"
       "0"
       " "
       "/"
       "\""
       "\\"
       "\n"
       "\r"
       "\t"
       "\u0000"
       "\u0007"
       "\u001f"
       "é"
       "字"
       "\U0001D11E"
       "$"
       "E"
       "." |]

let private canonFloats =
    [| 0.5
       -1.25
       1e21
       1e-7
       1.602e-19
       5e-324
       0.1
       3.141592653589793
       -0.0
       0.0
       2.0
       1e17
       100.0 |]

let private nextCanonSeed (r: int) : int = (r * 1103515245 + 12345) &&& 0x3FFFFFFF

let private genCanonValue (r: int ref) (depth: int) : JVal =
    let draw (n: int) =
        r.Value <- nextCanonSeed r.Value
        r.Value % n

    let rec go (depth: int) : JVal =
        match draw (if depth <= 0 then 5 else 7) with
        | 0 ->
            let n = draw 5
            JStr(String.concat "" [ for _ in 1..n -> canonChars[draw canonChars.Length] ])
        | 1 -> JInt(draw 2000 - 1000)
        | 2 -> JBool(draw 2 = 0)
        | 3 -> JFloat canonFloats[draw canonFloats.Length]
        | 4 -> JStr canonKeys[draw canonKeys.Length]
        | 5 -> JArr [ for _ in 1 .. draw 4 -> go (depth - 1) ]
        | _ -> JObj [ for _ in 1 .. draw 5 -> canonKeys[draw canonKeys.Length], go (depth - 1) ]

    go depth

let private canonGenerated (w: WireCanon.wire<int, float>) (seed: int) (trials: int) (roundTrip: bool) : CanonTally =
    let r = ref seed
    let mutable t = emptyCanonTally

    for i in 1..trials do
        let v = genCanonValue r 3
        t <- canonProbe w (sprintf "generated seed=%d iteration=%d" seed i) v t

        if roundTrip then
            t <- canonRoundTrip (sprintf "generated seed=%d iteration=%d" seed i) v t

    t

let private canonCorpus (w: WireCanon.wire<int, float>) (family: string) (roundTrip: bool) : CanonTally =
    let mutable t = emptyCanonTally

    for name, text in JsonParseDiff.corpusTexts family do
        match Json.parse text with
        | Result.Error m -> failtestf "the corpus fixture %s/%s did not parse: %s" family name m
        | Result.Ok v ->
            t <- canonProbe w (sprintf "%s/%s" family name) v t

            if roundTrip then
                t <- canonRoundTrip (sprintf "%s/%s" family name) v t

    t

let private renderCanonDiffs (diffs: string list) : string =
    diffs |> List.rev |> List.truncate 5 |> String.concat "\n"

// ---- the guard (Phase 165): `WireCanon.try_render` beside `Canon.tryRender` ----

/// The model names a refusal as DATA — a path of steps and the float it found — where production
/// emits one string. This renders the model's refusal in production's spelling so the two can be
/// compared as the `Result` a caller actually receives. The member key goes through the MODEL's
/// own `quoted` (rule 6's escape), not through `Canon`, so the bridge hands production nothing to
/// agree with itself about; the index crosses the `nat` → `int` width boundary here, where
/// `oracle/Prims.fs` says such a conversion belongs.
let private guardPathOfModel (p: WireCanon.pstep list) : string =
    "$"
    + (p
       |> List.map (fun s ->
           match s with
           | WireCanon.PItem i -> "[" + string (int i) + "]"
           | WireCanon.PMember k -> "[" + canonFromChs (WireCanon.quoted k) + "]")
       |> String.concat "")

let private guardModelSide (w: WireCanon.wire<int, float>) (v: JVal) : Result<string, string> =
    match WireCanon.try_render w (canonToModel v) with
    | WireCanon.Rendered bytes -> Result.Ok(canonFromChs bytes)
    | WireCanon.Refused(p, f) ->
        let tok =
            match w.fclass f with
            | WireCanon.FNaN -> "NaN"
            | WireCanon.FPosInf -> "Infinity"
            | WireCanon.FNegInf -> "-Infinity"
            | WireCanon.FFinite -> "<the model refused a float its own wire calls finite>"

        Result.Error(
            "non-finite float has no canonical rendering of its own: "
            + tok
            + " at "
            + guardPathOfModel p
        )

/// The guard's predicate, written a THIRD time and independently of both sides: does the value
/// hold a non-finite float anywhere. "Refuses exactly" is a claim about this set, and asking
/// either side under test to define it would make the claim circular.
let rec private holdsNonFinite (v: JVal) : bool =
    match v with
    | JFloat f -> not (System.Double.IsFinite f)
    | JArr xs -> xs |> List.exists holdsNonFinite
    | JObj fs -> fs |> List.exists (snd >> holdsNonFinite)
    | _ -> false

type private GuardTally =
    {
        Docs: int
        Diffs: string list
        /// Documents production refused.
        Refused: int
        /// Refusals whose path is two or more steps deep — the scan's recursion, not its leaf arm.
        DeepRefusals: int
        /// Refusals naming each of the three tokens.
        NaNs: int
        PosInfs: int
        NegInfs: int
        /// ACCEPTED documents carrying a float outside the canonical subset — an integer-shaped
        /// token or a zero — which is the set the guard must not refuse.
        AcceptedNormalised: int
    }

let private emptyGuardTally =
    { Docs = 0
      Diffs = []
      Refused = 0
      DeepRefusals = 0
      NaNs = 0
      PosInfs = 0
      NegInfs = 0
      AcceptedNormalised = 0 }

/// One document, asked of production's guard and of the model's. Three comparisons, each of which
/// can lose on its own: the two `Result`s agree (message and path included); an `Ok` is exactly
/// `Canon.render`'s bytes; and the verdict is `Error` precisely when the independent predicate
/// says a non-finite float is present.
let private guardProbe (w: WireCanon.wire<int, float>) (label: string) (v: JVal) (t: GuardTally) : GuardTally =
    let production = Canon.tryRender v
    let model = guardModelSide w v
    let t = { t with Docs = t.Docs + 1 }

    let diff (what: string) (t: GuardTally) =
        { t with
            Diffs =
                sprintf "%s: %s\n  production: %A\n  the model:  %A" label what production model
                :: t.Diffs }

    let t =
        if production = model then
            t
        else
            diff "the guards disagree" t

    match production with
    | Result.Ok bytes ->
        let t =
            if bytes = Canon.render v then
                t
            else
                diff "an accepted value did not render to Canon.render's bytes" t

        let t =
            if holdsNonFinite v then
                diff "production ACCEPTED a value holding a non-finite float" t
            else
                t

        if isCanonicalValue v then
            t
        else
            { t with
                AcceptedNormalised = t.AcceptedNormalised + 1 }
    | Result.Error m ->
        let t =
            if holdsNonFinite v then
                t
            else
                diff "production REFUSED a value holding no non-finite float" t

        let steps = m |> Seq.filter (fun c -> c = '[') |> Seq.length

        { t with
            Refused = t.Refused + 1
            DeepRefusals = t.DeepRefusals + (if steps >= 2 then 1 else 0)
            NaNs = t.NaNs + (if m.Contains ": NaN at " then 1 else 0)
            PosInfs = t.PosInfs + (if m.Contains ": Infinity at " then 1 else 0)
            NegInfs = t.NegInfs + (if m.Contains ": -Infinity at " then 1 else 0) }

/// The Phase 149 pool carries no non-finite float — it was built to measure the renderer, which
/// has nothing to say about one. This walks a drawn value and replaces roughly one numeric leaf in
/// three with one of the three, so the refusals land at every depth and position the pool reaches
/// rather than only at the root.
let private poisonCanonValue (r: int ref) (v: JVal) : JVal =
    let draw (n: int) =
        r.Value <- nextCanonSeed r.Value
        r.Value % n

    let rec go (v: JVal) : JVal =
        match v with
        | JFloat _
        | JInt _ when draw 3 = 0 ->
            (match draw 3 with
             | 0 -> JFloat nan
             | 1 -> JFloat infinity
             | _ -> JFloat -infinity)
        | JArr xs -> JArr(xs |> List.map go)
        | JObj fs -> JObj(fs |> List.map (fun (k, x) -> k, go x))
        | other -> other

    go v

let private guardGenerated (w: WireCanon.wire<int, float>) (seed: int) (trials: int) : GuardTally =
    let r = ref seed
    let mutable t = emptyGuardTally

    for i in 1..trials do
        let v = poisonCanonValue r (genCanonValue r 3)
        t <- guardProbe w (sprintf "generated seed=%d iteration=%d" seed i) v t

    t

let private guardCorpus (w: WireCanon.wire<int, float>) (family: string) : GuardTally =
    let mutable t = emptyGuardTally

    for name, text in JsonParseDiff.corpusTexts family do
        match Json.parse text with
        | Result.Error m -> failtestf "the corpus fixture %s/%s did not parse: %s" family name m
        | Result.Ok v -> t <- guardProbe w (sprintf "%s/%s" family name) v t

    t

/// The GO-RED instrument for the guard: a wire that cannot see NaN — it classifies one as finite,
/// so the model's scan walks past it. Every document whose FIRST non-finite float is a NaN must
/// then disagree with production, and every other document — including one refused for an
/// infinity — must still agree, which is what says the instrument is narrow to the predicate.
let private guardWireGoRed: WireCanon.wire<int, float> =
    { canonWire with
        fclass =
            fun f ->
                if System.Double.IsNaN f then
                    WireCanon.FFinite
                else
                    canonWire.fclass f }

// ---------------------------------------------------------------------------
//  Phase 151 — the EVOLUTION POLICY: `WireVersioning` beside `Versioning`.
// ---------------------------------------------------------------------------
//
// `proofs/WireVersioning.fst` models WIRE_FORMAT §15.4's table — `Versioning.classify` / `bump` /
// `negotiate` / `decodeTolerant` / `reencode` — and proves the four soundness statements the
// table's prose asserts. This section runs the extraction beside production over the two inputs
// the shard names: the corpus's `envelope/` family for the DECODE half, and pairs of `idl.json`
// revisions for the CLASSIFY half.
//
// The model's tag type is `WireCanon.ch list`, which is why the bridges above are reused rather
// than a second set written: a tag is a string read out of a `jval`, and Phase 149 already has
// the string bridge that matches the model's own character alphabet.

/// A production `Versioning.Profile` as the model reads it. The two counters cross a width
/// boundary — production holds `int`, the model's `nat` extracts to `BigInteger` per
/// `oracle/Prims.fs` — so the conversion is here and not hidden inside a comparison.
let private toModelProfile (p: Versioning.Profile) : WireVersioning.profile =
    { WireVersioning.name = canonToChs p.Name
      WireVersioning.major = bigint p.Major
      WireVersioning.minor = bigint p.Minor }

/// A vocabulary — production's `Set<string>` as the model's list of tags. `Set.toList` hands back
/// a sorted duplicate-free list, which is the only shape `classify`'s list-valued result can be
/// compared against element for element rather than as a set.
let private toModelVocab (v: Set<string>) : WireCanon.ch list list = v |> Set.toList |> List.map canonToChs

/// The model's `evolution` rendered as production's `Versioning.Evolution`, so the comparison is
/// one equality over the shipped type rather than a pair of shape tests.
let private ofModelEvolution (e: WireVersioning.evolution) : Versioning.Evolution =
    match e with
    | WireVersioning.Additive added -> Versioning.Additive(added |> List.map canonFromChs)
    | WireVersioning.Breaking(removed, added) ->
        Versioning.Breaking(removed |> List.map canonFromChs, added |> List.map canonFromChs)

/// One classification, asked of production and of the model. The comparison is the WHOLE verdict
/// — both lists, not merely the `Additive`/`Breaking` discriminator — because a classifier that
/// named the right class and the wrong tags would leave a migration author with the wrong shim.
let private versionProbe (label: string) (before: Set<string>) (after: Set<string>) (diffs: string list) =
    let expected = Versioning.classify before after

    let got =
        ofModelEvolution (WireVersioning.classify (toModelVocab before) (toModelVocab after))

    if expected = got then
        diffs
    else
        sprintf "%s: production classified\n  %A\nthe model classified\n  %A" label expected got
        :: diffs

/// The GO-RED instrument, and this family's counterpart to the canon family's reversed comparator:
/// the model's OWN `classify_ignoring_removals`, a classifier that computes the additions and
/// never looks for removals. It is not a strawman — it is what an author writes who reads §15.4's
/// additive row and stops there, and it agrees with production on every purely additive change.
/// A comparison that cannot separate it from the real thing is measuring nothing.
let private versionProbeGoRed (label: string) (before: Set<string>) (after: Set<string>) (diffs: string list) =
    let expected = Versioning.classify before after

    let got =
        ofModelEvolution (WireVersioning.classify_ignoring_removals (toModelVocab before) (toModelVocab after))

    if expected = got then
        diffs
    else
        sprintf "%s: production classified\n  %A\nthe broken model classified\n  %A" label expected got
        :: diffs

// ---- the IDL pairs ----------------------------------------------------------------------
//
// The pairs are produced by PERTURBING the pinned `idl.json` and re-reading it through
// `Diff.snapshot`, rather than by writing two tag sets by hand. That matters: the claim the row
// carries is about the classifier applied to an IDL DIFF, and a hand-written pair would prove the
// classifier agrees with itself over two lists somebody chose. Going through the artifact reader
// exercises the path a migration author actually walks.

/// The pinned corpus's `idl.json`, parsed. Resolved through the same sibling-corpus seam every
/// other corpus-reading family here uses.
let private pinnedIdlText () =
    match SiblingCorpus.resolve "nodes" with
    | SiblingCorpus.Found root -> System.IO.File.ReadAllText(System.IO.Path.Combine(root, "idl.json"))
    | SiblingCorpus.NotAsked why -> skiptest why
    | SiblingCorpus.Absent why -> failtest why

/// The kind tags an `idl.json` text declares, read through the production artifact differ rather
/// than by picking the JSON apart here.
let private kindTagsOf (label: string) (text: string) : Set<string> =
    match Fuaran.Core.Idl.Diff.parse text with
    | Result.Error m -> failtestf "the %s idl.json did not snapshot: %s" label m
    | Result.Ok(snap: Fuaran.Core.Idl.Diff.Snapshot) -> snap.Kinds |> Map.toList |> List.map fst |> Set.ofList

/// Rewrite the `kinds` array of an `idl.json` JVal. Every perturbation below is one of these.
let private mapKinds (f: JVal list -> JVal list) (idl: JVal) : JVal =
    match idl with
    | JObj fields ->
        JObj(
            fields
            |> List.map (fun (k, v) ->
                match k, v with
                | "kinds", JArr xs -> k, JArr(f xs)
                | _ -> k, v)
        )
    | other -> other

let private kindTag (k: JVal) : string =
    match k with
    | JObj fields ->
        match fields |> List.tryPick (fun (n, v) -> if n = "tag" then Some v else None) with
        | Some(JStr s) -> s
        | _ -> failtest "a kind in idl.json carries no string `tag`"
    | _ -> failtest "a kind in idl.json is not an object"

let private withTag (tag: string) (k: JVal) : JVal =
    match k with
    | JObj fields -> JObj(fields |> List.map (fun (n, v) -> if n = "tag" then n, JStr tag else n, v))
    | other -> other

/// Give a kind one more field, at a named optionality class. §15.4's field rows are all this
/// perturbation with a different class in the one slot, and the perturbation is deliberately one
/// that changes NO tag: that is what makes it invisible to the kind-tag delta (the model's
/// section 6) and visible to the composition that decides it (section 7).
let private withAnExtraField (optClass: string) (k: JVal) : JVal =
    match k with
    | JObj fields ->
        let extra =
            JObj
                [ "name", JStr "phase151ProbeField"
                  "optionality", JObj [ "$type", JStr optClass ]
                  "type", JObj [ "$type", JStr "prim"; "name", JStr "String" ] ]

        JObj(
            fields
            |> List.map (fun (n, v) ->
                match n, v with
                | "fields", JArr xs -> n, JArr(xs @ [ extra ])
                | _ -> n, v)
        )
    | other -> other

/// The four perturbations §15.4's table distinguishes, each as `(label, before-text, after-text)`.
/// `Canon.render` is what writes them back, so the perturbed artifact is canonical bytes and the
/// snapshot reader meets exactly what it would meet in a repository.
let private idlPairs () : (string * Set<string> * Set<string>) list =
    let text = pinnedIdlText ()

    let idl =
        match Json.parse text with
        | Result.Error m -> failtestf "the pinned idl.json did not parse: %s" m
        | Result.Ok v -> v

    let before = kindTagsOf "pinned" text
    let firstTag = before |> Set.toList |> List.head

    let render (v: JVal) = Canon.render v

    let added =
        idl
        |> mapKinds (fun xs ->
            match xs with
            | first :: _ -> xs @ [ withTag "Phase151ProbeKind" first ]
            | [] -> xs)
        |> render

    let optionalField =
        idl
        |> mapKinds (
            List.map (fun k ->
                if kindTag k = firstTag then
                    withAnExtraField "optional" k
                else
                    k)
        )
        |> render

    let removed =
        idl |> mapKinds (List.filter (fun k -> kindTag k <> firstTag)) |> render

    let renamed =
        idl
        |> mapKinds (
            List.map (fun k ->
                if kindTag k = firstTag then
                    withTag (firstTag + "Renamed") k
                else
                    k)
        )
        |> render

    [ "add a kind", before, kindTagsOf "kind-added" added
      "add an optional field", before, kindTagsOf "optional-field-added" optionalField
      "remove a tag", before, kindTagsOf "tag-removed" removed
      "rename", before, kindTagsOf "renamed" renamed ]

// ---- §15.4's FIELD rows, at the composition that decides them (Phase 200) ----------------
//
// The pairs above are the classify half AT THE KIND-TAG DELTA, and at that delta a field addition
// is invisible: the "add an optional field" pair classifies `Additive []`, the no-op arm. Until
// Phase 200 that was recorded as §15.4's optional-field row being satisfied VACUOUSLY, and
// permanently so, on the argument that widening `classify` to see fields would model a function
// this repository does not ship.
//
// The argument was wrong in one specific, checkable way. `Versioning.classify` does not take kind
// tags — it takes two SUBJECT SETS — and the shipped caller that builds them from an IDL diff is
// `Diff.evolution`, which partitions each row by the SEVERITY `Diff.classifyFieldAdd` gives it
// from the field's optionality class. `Diff.bumpProfile` then carries the verdict through
// `Versioning.bump`. The row is decided by shipped code all the way to a published profile, on a
// path this family did not walk. Walking it is this section, and the model's section 7 is the
// same composition proved.
//
// Three perturbations, one per class the classifier branches on, each adding ONE field to the
// pinned artifact and changing no tag:
//
//   `optional` — §15.4's row itself. A subject is INTRODUCED, the verdict is a NON-EMPTY
//                `Additive`, the MINOR moves, and an old consumer is `Behind` rather than blind.
//   `required` — introduced too, and the minor is still the honest profile answer: every existing
//                document decodes. What moved is the EMITTER's obligation, carried on
//                `BreaksEmitters` beside the profile because a major would tell every consumer to
//                refuse documents that decode perfectly.
//   `hostOnly` — WIRE_FORMAT §9's wire-omitted fields: on no document in either direction, so no
//                profile moves. This is the arm that makes the other two a MEASUREMENT rather
//                than a constant, and it is the one the go-red gets wrong.

module IdlDiff = Fuaran.Core.Idl.Diff

/// The model's `severity` as production's, so the comparison is one equality over the shipped type
/// rather than a pair of shape tests.
let private ofModelSeverity (s: WireVersioning.severity) : IdlDiff.Severity =
    match s with
    | WireVersioning.SAdditive -> IdlDiff.Additive
    | WireVersioning.SBreakingForEmitters -> IdlDiff.BreakingForEmitters
    | WireVersioning.SBreakingWire -> IdlDiff.BreakingWire
    | WireVersioning.SHostSurfaceOnly -> IdlDiff.HostSurfaceOnly
    | WireVersioning.SUnclassifiable -> IdlDiff.Unclassifiable

/// A model profile as the three components a publisher reads. The counters cross a width boundary
/// in the other direction from `toModelProfile` — the model's `nat` extracts to `BigInteger` — so
/// the conversion is here and not hidden inside a comparison.
let private profileTriple (p: WireVersioning.profile) : string * int * int =
    canonFromChs p.name, int p.major, int p.minor

/// The SUBJECT string production renders for one classification row. `Diff.summarise` is private,
/// so it is recovered through the public `Diff.evolution` over the single row — which hands the
/// string back WITHOUT presupposing which partition it lands in, since either list answers. That
/// matters: the partition is exactly what this family compares, so it must not be an input to the
/// comparison. A row that moves neither set contributes to neither, and its subject is read by
/// nothing.
let private subjectOf (c: IdlDiff.Classification) : string =
    match IdlDiff.evolution [ c ] with
    | Versioning.Additive(s :: _) -> s
    | Versioning.Breaking(s :: _, _) -> s
    | Versioning.Breaking([], s :: _) -> s
    | _ -> "<this row moves neither subject set>"

/// What one field-add perturbation measures on both sides.
type private FieldAddMeasurement =
    {
        /// The optionality class the perturbation declared, read back out of the artifact.
        OptClass: string
        ProductionSeverity: IdlDiff.Severity
        ModelSeverity: IdlDiff.Severity
        /// The profile `core@1.0` bumps to, as `Diff.bumpProfile` computes it.
        ProductionProfile: string * int * int
        /// The same, computed end to end by the model: its own `classify_field_add`, its own
        /// `evolution_of`, its own `bump`.
        ModelProfile: string * int * int
        /// And by the model's GO-RED — a classifier that answers additive whatever the class says.
        GoRedProfile: string * int * int
        BreaksEmitters: bool
    }

let private fieldAddProbe (optClass: string) (beforeText: string) (afterText: string) : FieldAddMeasurement =
    match IdlDiff.classifyArtifacts beforeText afterText with
    | Result.Error m -> failtestf "the `%s` field-add perturbation did not classify: %s" optClass m
    | Result.Ok v ->
        let row =
            match v.Changes with
            | [ one ] -> one
            | rows ->
                failtestf
                    "the `%s` field-add perturbation produced %d rows, not the single FieldAdded it adds — the perturbation moved something else: %A"
                    optClass
                    (List.length rows)
                    (rows |> List.map (fun r -> r.Change))

        let declared =
            match row.Change with
            | IdlDiff.FieldAdded(_, f) -> f.OptClass
            | other -> failtestf "the `%s` perturbation's only row is %A, not a FieldAdded" optClass other

        let subject = canonToChs (subjectOf row)
        let baseProfile = toModelProfile Versioning.Profile.coreV1

        let modelSeverity = WireVersioning.classify_field_add (canonToChs declared)

        let goRedSeverity =
            WireVersioning.classify_field_add_ignoring_optionality (canonToChs declared)

        let modelBump (sev: WireVersioning.severity) =
            profileTriple (WireVersioning.bump baseProfile (WireVersioning.evolution_of [ sev, subject ]))

        let productionProfile =
            match IdlDiff.bumpProfile Versioning.Profile.coreV1 v with
            | IdlDiff.Bump.Bumped p -> p.Name, p.Major, p.Minor
            | IdlDiff.Bump.Undecided rows ->
                failtestf "the `%s` field-add perturbation left %d rows undecided" optClass (List.length rows)

        { OptClass = declared
          ProductionSeverity = row.Severity
          ModelSeverity = ofModelSeverity modelSeverity
          ProductionProfile = productionProfile
          ModelProfile = modelBump modelSeverity
          GoRedProfile = modelBump goRedSeverity
          BreaksEmitters = v.BreaksEmitters }

/// The three perturbations, each derived from the PINNED artifact and read back through
/// `Diff.parse`, for the reason `idlPairs` gives: the claim is about the classifier applied to an
/// IDL diff, and a hand-written pair would establish that it agrees with itself.
let private fieldAddMeasurements () : FieldAddMeasurement list =
    let text = pinnedIdlText ()

    let idl =
        match Json.parse text with
        | Result.Error m -> failtestf "the pinned idl.json did not parse: %s" m
        | Result.Ok v -> v

    let firstTag = kindTagsOf "pinned" text |> Set.toList |> List.head
    let before = Canon.render idl

    let perturb (optClass: string) =
        idl
        |> mapKinds (
            List.map (fun k ->
                if kindTag k = firstTag then
                    withAnExtraField optClass k
                else
                    k)
        )
        |> Canon.render

    [ "optional"; "required"; "hostOnly" ]
    |> List.map (fun c -> fieldAddProbe c before (perturb c))

// ---- the envelope family ----------------------------------------------------------------
//
// The corpus's six `envelope/` fixtures are the DECODE half. Each carries a `$profile` and a
// `$payload`; the consumer is `core@1.0` and its vocabulary is the one the fixtures were built
// against — `Markdown` known, `hologram` not. The discriminator is the payload's `kind.$type`,
// and `requiredProfile` sits beside `kind` on the payload object, which is exactly where
// production's own `readRequiredProfile` looks.

/// The consumer's vocabulary for the envelope family, as the fixtures declare it by construction.
let private envelopeVocab: Set<string> = Set.ofList [ "Markdown" ]

let private envelopeTagOf (el: JVal) : Result<string, string> =
    Decode.getProp "kind" el
    |> Result.bind (Decode.getProp "$type")
    |> Result.bind Decode.asString

let private envelopeRequiredProfile (el: JVal) : Versioning.Profile option =
    match Decode.getProp "requiredProfile" el with
    | Result.Ok(JStr s) ->
        match Versioning.Profile.tryParse s with
        | Result.Ok p -> Some p
        | Result.Error _ -> None
    | _ -> None

/// The model's counterparts, over the bridged value. `decode_known` is the identity on the parsed
/// object on BOTH sides: this family is about the tolerance boundary, not about a domain codec,
/// and handing the two sides different known-decoders would compare the decoders instead.
let private envelopeTagOfModel (el: WireCanon.jval<int, float>) : WireCanon.outcome<WireCanon.ch list> =
    match envelopeTagOf (canonOfModel el) with
    | Result.Ok s -> WireCanon.Ok(canonToChs s)
    | Result.Error m -> WireCanon.Error m

let private envelopeRequiredProfileModel (el: WireCanon.jval<int, float>) =
    match envelopeRequiredProfile (canonOfModel el) with
    | Some p -> FStar_Pervasives_Native.Some(toModelProfile p)
    | None -> FStar_Pervasives_Native.None

/// One envelope fixture, asked of production and of the model. Four things are compared, and the
/// last is the one §15.3 is about:
///   1. the NEGOTIATION — `Current` / `Behind` / `Foreign`, and the authored profile each carries;
///   2. whether the tolerant decode found a KNOWN kind or preserved an UNKNOWN one;
///   3. the `requiredProfile` the unknown carries, which is what a degraded placeholder names;
///   4. the BYTES out of `reencode` + `Canon.render` — which for a `Foreign` fixture is not asked,
///      because a `Foreign` artifact is refused before any payload is decoded.
let private envelopeProbe (label: string) (text: string) (diffs: string list) =
    match Versioning.parse text with
    | Result.Error m -> failtestf "the envelope fixture %s did not parse: %s" label m
    | Result.Ok env ->
        let consumer = Versioning.Profile.coreV1
        let expectedNeg = Versioning.negotiate consumer env.Profile

        let gotNeg =
            WireVersioning.negotiate (toModelProfile consumer) (toModelProfile env.Profile)

        let negAgrees =
            match expectedNeg, gotNeg with
            | Versioning.Current, WireVersioning.Current -> true
            | Versioning.Behind a, WireVersioning.Behind b
            | Versioning.Foreign a, WireVersioning.Foreign b -> toModelProfile a = b
            | _ -> false

        let diffs =
            if negAgrees then
                diffs
            else
                sprintf "%s: production negotiated %A, the model negotiated %A" label expectedNeg gotNeg
                :: diffs

        match expectedNeg with
        | Versioning.Foreign _ ->
            // A Foreign profile is refused at the envelope, so there is no tolerant decode to
            // compare. Recording that here rather than skipping the fixture is what keeps the
            // reject half of the family inside the comparison.
            diffs
        | _ ->
            let expected =
                Versioning.decodeTolerant envelopeTagOf (fun t -> envelopeVocab.Contains t) Result.Ok env.Payload

            let got =
                WireVersioning.decode_tolerant
                    envelopeTagOfModel
                    (WireVersioning.known_in (toModelVocab envelopeVocab))
                    WireCanon.Ok
                    envelopeRequiredProfileModel
                    (canonToModel env.Payload)

            match expected, got with
            | Result.Error m, WireCanon.Error m' when m = m' -> diffs
            | Result.Ok d, WireCanon.Ok d' ->
                let shapeAgrees =
                    match d, d' with
                    | Versioning.Known _, WireVersioning.Known _ -> true
                    | Versioning.Unknown u, WireVersioning.Unknown u' ->
                        canonFromChs u'.kind = u.Kind
                        && (match u.RequiredProfile, u'.required_profile with
                            | Some p, FStar_Pervasives_Native.Some p' -> toModelProfile p = p'
                            | None, FStar_Pervasives_Native.None -> true
                            | _ -> false)
                    | _ -> false

                let diffs =
                    if shapeAgrees then
                        diffs
                    else
                        sprintf "%s: production decoded %A, the model decoded a different shape" label d
                        :: diffs

                // §15.3 at the bytes. Production's own `Canon.render` on both sides — the model's
                // renderer is Phase 149's family's subject, and re-testing it here would be
                // measuring that rather than preservation.
                let expectedBytes = Canon.render (Versioning.reencode id d)
                let gotBytes = Canon.render (canonOfModel (WireVersioning.reencode id d'))

                if expectedBytes = gotBytes && expectedBytes = Canon.render env.Payload then
                    diffs
                else
                    sprintf
                        "%s: preservation — production re-rendered\n  %s\nthe model re-rendered\n  %s\nthe payload was\n  %s"
                        label
                        expectedBytes
                        gotBytes
                        (Canon.render env.Payload)
                    :: diffs
            | _ ->
                sprintf "%s: production and the model disagreed on whether the decode succeeded" label
                :: diffs

// ---------------------------------------------------------------------------
//  Phase 176 — the COLUMNAR op algebra: `ColumnOps.apply` / `canApply` / `invert` / `toOps` /
//  `applyAll` beside the extracted model (`proofs/ColumnOps.fst`), over generated tables and op
//  scripts.
//
//  WHAT THE BRIDGE SAYS. The model reads a cell as its type and an OPAQUE CARRIER — `Present ty
//  carrier` — and nothing in the algebra looks inside one. The bridge renders each production
//  value to its carrier (`Int 5` to `"5"`, a float through the round-trip `"R"` format, a bool to
//  its lower-case word, the three string-carried kinds verbatim) and parses it back, so a
//  production table and its model image are the same table under either reading. Where the two
//  DISAGREE by construction is a NaN float — `Float nan <> Float nan` in production, `"NaN" =
//  "NaN"` in the model — and that is the `column-cell-carrier-opaque` row; the generator does not
//  draw one, and says so.
//
//  The pipeline evaluator is a PARAMETER of the model (`ev`), as `canHold` is of the container
//  model. Here it is production's own `DataFrame.evalPipeline`, reached through the bridge: the
//  model hands the evaluator a token, the token indexes a fixed pool of pipelines, and the answer
//  comes back through the same bridge. So the `ApplyTransform` arm compares the model's
//  ENVELOPE — replace wholesale, or `TransformRejected` carrying the rendered error — and nothing
//  about the pipeline, which is exactly what the model claims.
//
//  FIVE COMPARISONS, per (op, state):
//    1. `ColumnOps.apply` vs the model's `apply` — verdict, accepted result through the bridge,
//       rejection by class AND payload (name, row, count, tag).
//    2. `ColumnOps.canApply` vs `can_apply`, and the dry run against the mutating call on each side
//       separately (`canapply_agrees` instantiated).
//    3. `ColumnOps.invert` vs `invert` — verdict and the derived inverse as an operation; and,
//       where the pre-state is WELL-FORMED and the op accepted and invertible, the round trip
//       asserted EXACTLY on production and on the model (`invert_roundtrip` instantiated). Where
//       the pre-state is not well-formed the round trip is only counted, because the theorem
//       does not claim it there — and the count of failures is the evidence the hypothesis earns
//       its place. Since Phase 181 both sides are GUARDED by `canApply`, so this arm now also
//       compares a REFUSED op's rejection coming back out of `invert` rather than an inverse.
//    4. THE INVARIANT: a well-formed pre-state and an accepted structural op give a well-formed
//       result (`apply_preserves_wf` instantiated), judged by the MODEL's `wf` on the bridged
//       production result.
//    5. Every script as a whole: `ColumnOps.applyAll` vs `apply_all` — the short-circuit and the
//       all-or-nothing discipline (`reject_identity`) as one comparison.
//  And a sixth over PAIRS of tables: `ColumnOps.toOps` vs `to_ops` as scripts, and where both
//  tables are well-formed, `applyAll (toOps before after) before = Ok after` asserted on both
//  sides (`diff_applicable` instantiated), with both branches of the diff counted.
//
//  The tables are GENERATED with the invariant deliberately broken some of the time — a repeated
//  name, a schema entry with no column, a column a row short, a cell of the wrong type — because
//  `apply` is total over all of them and the model must agree there too; the theorems that need
//  well-formedness are asserted only where the generator produced it, and the tally says how
//  often that was.
// ---------------------------------------------------------------------------

let private colTypeToModel (t: ColumnType) : ModelCol.coltype =
    match t with
    | IntType -> ModelCol.IntType
    | FloatType -> ModelCol.FloatType
    | BoolType -> ModelCol.BoolType
    | StringType -> ModelCol.StringType
    | DateType -> ModelCol.DateType
    | TimestampType -> ModelCol.TimestampType

let private colTypeOfModel (t: ModelCol.coltype) : ColumnType =
    match t with
    | ModelCol.IntType -> IntType
    | ModelCol.FloatType -> FloatType
    | ModelCol.BoolType -> BoolType
    | ModelCol.StringType -> StringType
    | ModelCol.DateType -> DateType
    | ModelCol.TimestampType -> TimestampType

/// The honest cell bridge: type + carrier, the carrier a rendering that parses back exactly.
let private cellToModel (c: Cell) : ModelCol.cell =
    match c with
    | Null -> ModelCol.Null
    | Int i -> ModelCol.Present(ModelCol.IntType, i.ToString(inv))
    | Float f -> ModelCol.Present(ModelCol.FloatType, f.ToString("R", inv))
    | Bool b -> ModelCol.Present(ModelCol.BoolType, (if b then "true" else "false"))
    | Str s -> ModelCol.Present(ModelCol.StringType, s)
    | Date s -> ModelCol.Present(ModelCol.DateType, s)
    | Timestamp s -> ModelCol.Present(ModelCol.TimestampType, s)

/// The BLIND cell bridge — the go-red's instrument: every present cell is read as a string, so
/// the model's type check sees a `Str` where production sees an `Int`, and the two must part.
let private blindCellToModel (c: Cell) : ModelCol.cell =
    match cellToModel c with
    | ModelCol.Present(_, carrier) -> ModelCol.Present(ModelCol.StringType, carrier)
    | ModelCol.Null -> ModelCol.Null

let private cellOfModel (c: ModelCol.cell) : Cell =
    match c with
    | ModelCol.Null -> Null
    | ModelCol.Present(ModelCol.IntType, s) -> Int(System.Int32.Parse(s, inv))
    | ModelCol.Present(ModelCol.FloatType, s) -> Float(System.Double.Parse(s, inv))
    | ModelCol.Present(ModelCol.BoolType, s) -> Bool(s = "true")
    | ModelCol.Present(ModelCol.StringType, s) -> Str s
    | ModelCol.Present(ModelCol.DateType, s) -> Date s
    | ModelCol.Present(ModelCol.TimestampType, s) -> Timestamp s

let private columnToModelWith (bridge: Cell -> ModelCol.cell) (c: Column) : ModelCol.column =
    { ModelCol.column.name = c.Name
      ModelCol.column.ty = colTypeToModel c.Type
      ModelCol.column.cells = c.Cells |> List.map bridge }

let private columnOfModel (c: ModelCol.column) : Column =
    Column.create c.name (colTypeOfModel c.ty) (c.cells |> List.map cellOfModel)

let private tableToModelWith (bridge: Cell -> ModelCol.cell) (t: Table) : ModelCol.table =
    { ModelCol.table.schema = t.Schema |> List.map (fun (n, ty) -> n, colTypeToModel ty)
      ModelCol.table.columns = t.Columns |> List.map (columnToModelWith bridge) }

let private tableToModel = tableToModelWith cellToModel

let private tableOfModel (t: ModelCol.table) : Table =
    { Schema = t.schema |> List.map (fun (n, ty) -> n, colTypeOfModel ty)
      Columns = t.columns |> List.map columnOfModel }

/// The pipelines `ApplyTransform` draws from; the model sees each as its index. Two accept on
/// most tables, two reject on most, one is the identity, one rejects on an empty one.
let private pipelinePool: Transform list list =
    [ [ Distinct ]
      [ Limit(Slot.Lit 2, Slot.Lit 0) ]
      [ Project [ "a", "a" ] ]
      [ Filter(Col "nope") ]
      [ Derive("d", Lit(Int 1)) ]
      [] ]

let private pipelineToken (p: Transform list) : string =
    match pipelinePool |> List.tryFindIndex (fun q -> q = p) with
    | Some i -> string i
    | None -> "?"

/// The model's evaluator parameter, instantiated at production's own `DataFrame.evalPipeline`.
let private modelEvaluator (token: string) (mt: ModelCol.table) : ModelCol.outcome<ModelCol.table, string> =
    match System.Int32.TryParse token with
    | true, i when i >= 0 && i < List.length pipelinePool ->
        (match DataFrame.evalPipeline (List.item i pipelinePool) (tableOfModel mt) with
         | Ok t -> ModelCol.Ok(tableToModel t)
         | Error e -> ModelCol.Error(DataFrame.errorString e))
    | _ -> ModelCol.Error("no such pipeline token: " + token)

let private colOpToModelWith (bridge: Cell -> ModelCol.cell) (op: ColumnOp) : ModelCol.op =
    match op with
    | SetCell(n, row, v) -> ModelCol.SetCell(n, bigint row, bridge v)
    | SetColumn c -> ModelCol.SetColumn(columnToModelWith bridge c)
    | InsertColumn(i, c) -> ModelCol.InsertColumn(bigint i, columnToModelWith bridge c)
    | RemoveColumn n -> ModelCol.RemoveColumn n
    | AppendRows rows -> ModelCol.AppendRows(rows |> List.map (List.map (fun (n, v) -> n, bridge v)))
    | ApplyTransform p -> ModelCol.ApplyTransform(pipelineToken p)

let private modelCellRender (c: ModelCol.cell) : string =
    match c with
    | ModelCol.Null -> "null"
    | ModelCol.Present(ty, carrier) -> ModelCol.tag ty + ":" + carrier

let private modelColumnRender (c: ModelCol.column) : string =
    sprintf "%s:%s[%s]" c.name (ModelCol.tag c.ty) (c.cells |> List.map modelCellRender |> String.concat ",")

/// The two renderings agree by construction — production renders THROUGH the honest bridge —
/// so a differing rendering is a differing value.
let private modelColOpRender (op: ModelCol.op) : string =
    match op with
    | ModelCol.SetCell(n, row, v) -> sprintf "SetCell(%s;%s;%s)" n (string row) (modelCellRender v)
    | ModelCol.SetColumn c -> sprintf "SetColumn(%s)" (modelColumnRender c)
    | ModelCol.InsertColumn(i, c) -> sprintf "InsertColumn(%s;%s)" (string i) (modelColumnRender c)
    | ModelCol.RemoveColumn n -> sprintf "RemoveColumn(%s)" n
    | ModelCol.AppendRows rows -> sprintf "AppendRows(%d)" (List.length rows)
    | ModelCol.ApplyTransform p -> sprintf "ApplyTransform(%s)" p

let private prodColOpRender (op: ColumnOp) : string =
    modelColOpRender (colOpToModelWith cellToModel op)

let private modelColRejRender (r: ModelCol.rejection) : string =
    match r with
    | ModelCol.NoSuchColumn(n, avail) -> sprintf "NoSuchColumn(%s;%s)" n (String.concat "," avail)
    | ModelCol.DuplicateColumn n -> sprintf "DuplicateColumn(%s)" n
    | ModelCol.RowOutOfRange(row, rc) -> sprintf "RowOutOfRange(%s;%s)" (string row) (string rc)
    | ModelCol.CellTypeMismatch(c, e, g) -> sprintf "CellTypeMismatch(%s;%s;%s)" c e g
    | ModelCol.ColumnLengthMismatch(c, e, g) -> sprintf "ColumnLengthMismatch(%s;%s;%s)" c (string e) (string g)
    | ModelCol.RowShapeUnknownColumn(n, avail) -> sprintf "RowShapeUnknownColumn(%s;%s)" n (String.concat "," avail)
    | ModelCol.TransformRejected d -> sprintf "TransformRejected(%s)" d
    | ModelCol.NotInvertible o -> sprintf "NotInvertible(%s)" o

let private prodColRejRender (r: ColumnRejection) : string =
    match r with
    | NoSuchColumn(n, avail) -> sprintf "NoSuchColumn(%s;%s)" n (String.concat "," avail)
    | DuplicateColumn n -> sprintf "DuplicateColumn(%s)" n
    | RowOutOfRange(row, rc) -> sprintf "RowOutOfRange(%d;%d)" row rc
    | CellTypeMismatch(c, e, g) -> sprintf "CellTypeMismatch(%s;%s;%s)" c e g
    | ColumnLengthMismatch(c, e, g) -> sprintf "ColumnLengthMismatch(%s;%d;%d)" c e g
    | RowShapeUnknownColumn(n, avail) -> sprintf "RowShapeUnknownColumn(%s;%s)" n (String.concat "," avail)
    | TransformRejected d -> sprintf "TransformRejected(%s)" d
    | NotInvertible o -> sprintf "NotInvertible(%s)" o

let private colRejClass (r: ColumnRejection) : string =
    let s = prodColRejRender r
    s.Substring(0, s.IndexOf '(')

// ---- the generator ----

let private colNamePool = [ "a"; "b"; "c"; "d" ]

let private colTypePool =
    [ IntType; FloatType; BoolType; StringType; DateType; TimestampType ]

/// A cell for a column of type `ty`: mostly fitting, sometimes `Null`, sometimes of another type.
let private genColCell (ty: ColumnType) (r: ConfRng.T) : Cell * ConfRng.T =
    let roll, r1 = ConfRng.intBelow 10 r
    let v, r2 = ConfRng.intBelow 100 r1

    let ofType t =
        match t with
        | IntType -> Int v
        | FloatType -> Float(float v / 4.0)
        | BoolType -> Bool(v % 2 = 0)
        | StringType -> Str(sprintf "s%d" v)
        | DateType -> Date(sprintf "2026-01-%02d" (1 + v % 28))
        | TimestampType -> Timestamp(sprintf "2026-01-01T00:00:%02dZ" (v % 60))

    if roll < 7 then
        ofType ty, r2
    elif roll < 9 then
        Null, r2
    else
        let other, r3 = ConfRng.choose colTypePool r2
        ofType other, r3

let private genColCells (ty: ColumnType) (n: int) (r: ConfRng.T) : Cell list * ConfRng.T =
    let mutable rng = r
    let cells = System.Collections.Generic.List<Cell>()

    for _ in 1..n do
        let c, r' = genColCell ty rng
        rng <- r'
        cells.Add c

    List.ofSeq cells, rng

let private genColumn (name: string) (rows: int) (r: ConfRng.T) : Column * ConfRng.T =
    let ty, r1 = ConfRng.choose colTypePool r
    let cells, r2 = genColCells ty rows r1
    Column.create name ty cells, r2

/// A table: up to three columns over a four-name pool, up to three rows — and, one draw in ten
/// each, a repeated name, a column a row long or short, or a schema that is not the columns'
/// projection. `ModelCol.wf` on the bridged table says which it was.
let private genColTable (r: ConfRng.T) : Table * ConfRng.T =
    let ncols, r1 = ConfRng.intBelow 4 r
    let rows, r2 = ConfRng.intBelow 4 r1
    let names, r3 = ConfRng.shuffle colNamePool r2
    let mutable rng = r3
    let cols = System.Collections.Generic.List<Column>()

    for i in 0 .. ncols - 1 do
        let dupRoll, r4 = ConfRng.intBelow 10 rng
        let lenRoll, r5 = ConfRng.intBelow 10 r4
        rng <- r5

        let name =
            if dupRoll = 0 && i > 0 then
                cols.[0].Name
            else
                List.item i names

        let n = if lenRoll = 0 then rows + 1 else rows
        let c, r6 = genColumn name n rng
        rng <- r6
        cols.Add c

    let columns = List.ofSeq cols
    let schemaRoll, r7 = ConfRng.intBelow 10 rng
    rng <- r7

    let schema =
        columns
        |> List.map (fun c -> c.Name, c.Type)
        |> fun s ->
            if schemaRoll = 0 then
                List.truncate (List.length s - 1) s
            else
                s

    { Schema = schema; Columns = columns }, rng

/// An op against `t`, drawn so that every rejection class is reachable and acceptance is common.
let private genColOp (t: Table) (r: ConfRng.T) : ColumnOp * ConfRng.T =
    let rc = Table.rowCount t
    let names = Table.columnNames t
    let nameOrStranger, r1 = ConfRng.choose (names @ [ "zz" ]) r
    let kind, r2 = ConfRng.intBelow 6 r1

    match kind with
    | 0 ->
        let row, r3 = ConfRng.intBelow (rc + 2) r2
        let ty, r4 = ConfRng.choose colTypePool r3
        let v, r5 = genColCell ty r4
        SetCell(nameOrStranger, row - 1, v), r5
    | 1 ->
        let lenRoll, r3 = ConfRng.intBelow 5 r2
        let n = if lenRoll = 0 then rc + 1 else rc
        let c, r4 = genColumn nameOrStranger n r3
        SetColumn c, r4
    | 2 ->
        let idx, r3 = ConfRng.intBelow (List.length t.Columns + 3) r2
        let name, r4 = ConfRng.choose colNamePool r3
        let lenRoll, r5 = ConfRng.intBelow 5 r4

        let n =
            if List.isEmpty t.Columns then 2
            elif lenRoll = 0 then rc + 1
            else rc

        let c, r6 = genColumn name n r5
        InsertColumn(idx - 1, c), r6
    | 3 -> RemoveColumn nameOrStranger, r2
    | 4 ->
        let nrows, r3 = ConfRng.intBelow 2 r2
        let mutable rng = r3
        let rows = System.Collections.Generic.List<(string * Cell) list>()

        for _ in 0..nrows do
            let subset, r4 = ConfRng.shuffle (names @ [ "zz" ]) rng
            let take, r5 = ConfRng.intBelow (List.length subset + 1) r4
            rng <- r5
            let row = System.Collections.Generic.List<string * Cell>()

            for n in List.truncate take subset do
                let ty =
                    match Table.tryColumn n t with
                    | Some c -> c.Type
                    | None -> IntType

                let v, r6 = genColCell ty rng
                rng <- r6
                row.Add((n, v))

            rows.Add(List.ofSeq row)

        AppendRows(List.ofSeq rows), rng
    | _ ->
        let p, r3 = ConfRng.choose pipelinePool r2
        ApplyTransform p, r3

let private colStructural (op: ColumnOp) =
    match op with
    | ApplyTransform _ -> false
    | _ -> true

let private colInvertible (op: ColumnOp) =
    match op with
    | SetCell _
    | SetColumn _
    | InsertColumn _
    | RemoveColumn _ -> true
    | _ -> false

type private ColTally =
    {
        Diffs: string list
        Accepted: int
        Rejected: int
        Classes: Set<string>
        Inverted: int
        RoundTripped: int
        /// round trips ATTEMPTED on a pre-state the theorem does not cover, and how many failed
        RoundTripsOutsideWf: int
        RoundTripFailuresOutsideWf: int
        WfPre: int
        WfPreserved: int
        Scripts: int
    }

let private emptyColTally =
    { Diffs = []
      Accepted = 0
      Rejected = 0
      Classes = Set.empty
      Inverted = 0
      RoundTripped = 0
      RoundTripsOutsideWf = 0
      RoundTripFailuresOutsideWf = 0
      WfPre = 0
      WfPreserved = 0
      Scripts = 0 }

/// One (op, state) asked of both sides, across the first four comparisons.
let private colProbe (bridge: Cell -> ModelCol.cell) (op: ColumnOp) (st: Table) (acc: ColTally) : ColTally =
    let mop = colOpToModelWith bridge op
    let mst = tableToModelWith bridge st

    let where =
        sprintf
            "op %s at table %s"
            (prodColOpRender op)
            (modelColumnRender |> fun f -> mst.columns |> List.map f |> String.concat "|")

    let prod = ColumnOps.apply op st
    let model = ModelCol.apply modelEvaluator mop mst
    let wfPre = ModelCol.wf mst

    // 1. apply
    let applyDiff, accepted, rejected, cls =
        match prod, model with
        | Ok pt, ModelCol.Ok mt ->
            (if tableToModelWith bridge pt <> mt then
                 [ sprintf "accepted result differs — %s" where ]
             else
                 []),
            1,
            0,
            None
        | Error pe, ModelCol.Error me ->
            let pr = prodColRejRender pe
            let mr = modelColRejRender me

            (if pr <> mr then
                 [ sprintf "rejection differs — %s\n  production: %s\n  oracle:     %s" where pr mr ]
             else
                 []),
            0,
            1,
            Some(colRejClass pe)
        | Ok _, ModelCol.Error me ->
            [ sprintf "production ACCEPTED but the oracle rejected (%s) — %s" (modelColRejRender me) where ], 0, 0, None
        | Error pe, ModelCol.Ok _ ->
            [ sprintf "production REJECTED (%s) but the oracle accepted — %s" (prodColRejRender pe) where ], 0, 0, None

    // 2. canApply, each side against the other and against its own apply
    let canDiff =
        let pv =
            match ColumnOps.canApply op st with
            | Ok() -> "ok"
            | Error e -> prodColRejRender e

        let mv =
            match ModelCol.can_apply modelEvaluator mop mst with
            | ModelCol.Ok() -> "ok"
            | ModelCol.Error e -> modelColRejRender e

        let pa =
            match prod with
            | Ok _ -> "ok"
            | Error e -> prodColRejRender e

        let ma =
            match model with
            | ModelCol.Ok _ -> "ok"
            | ModelCol.Error e -> modelColRejRender e

        (if pv <> mv then
             [ sprintf "canApply differs — %s\n  production: %s\n  oracle:     %s" where pv mv ]
         else
             [])
        @ (if pv <> pa then
               [ sprintf "production's canApply and apply DISAGREE — %s" where ]
           else
               [])
        @ (if mv <> ma then
               [ sprintf "the oracle's can_apply and apply DISAGREE — %s" where ]
           else
               [])

    // 3. invert — the derived inverse, and the round trip where the theorem claims it
    let pInv = ColumnOps.invert op st
    let mInv = ModelCol.invert modelEvaluator mop mst

    let invDiff =
        match pInv, mInv with
        | Ok pi, ModelCol.Ok mi ->
            if prodColOpRender pi <> modelColOpRender mi then
                [ sprintf
                      "the derived INVERSE differs — %s\n  production: %s\n  oracle:     %s"
                      where
                      (prodColOpRender pi)
                      (modelColOpRender mi) ]
            else
                []
        | Error pe, ModelCol.Error me ->
            if prodColRejRender pe <> modelColRejRender me then
                [ sprintf "invert's rejection differs — %s" where ]
            else
                []
        | _ -> [ sprintf "invert's verdict differs — %s" where ]

    let inverted, roundTripped, outsideWf, outsideWfFailed, roundTripDiff =
        match prod, pInv with
        | Ok pt, Ok pi when colInvertible op ->
            let back = ColumnOps.apply pi pt

            let mBack =
                match model, mInv with
                | ModelCol.Ok mt, ModelCol.Ok mi -> Some(ModelCol.apply modelEvaluator mi mt)
                | _ -> None

            let restored = (back = Ok st)
            let mRestored = (mBack = Some(ModelCol.Ok mst))

            if wfPre then
                1,
                1,
                0,
                0,
                (if not restored then
                     [ sprintf "the inverse did NOT restore a WELL-FORMED input on production — %s (got %A)" where back ]
                 else
                     [])
                @ (if not mRestored then
                       [ sprintf "the ORACLE's inverse did not restore a well-formed input — %s" where ]
                   else
                       [])
            else
                1, 0, 1, (if restored then 0 else 1), []
        | _ -> 0, 0, 0, 0, []

    // 4. the invariant, on production's result, judged by the model's `wf`
    let wfPreserved, wfDiff =
        match prod with
        | Ok pt when wfPre && colStructural op ->
            if ModelCol.wf (tableToModelWith bridge pt) then
                1, []
            else
                0, [ sprintf "a well-formed table and an accepted structural op gave a MALFORMED result — %s" where ]
        | _ -> 0, []

    { Diffs = acc.Diffs @ applyDiff @ canDiff @ invDiff @ roundTripDiff @ wfDiff
      Accepted = acc.Accepted + accepted
      Rejected = acc.Rejected + rejected
      Classes =
        (match cls with
         | Some c -> Set.add c acc.Classes
         | None -> acc.Classes)
        |> fun s ->
            match pInv with
            | Error(NotInvertible _) -> Set.add "NotInvertible" s
            | _ -> s
      Inverted = acc.Inverted + inverted
      RoundTripped = acc.RoundTripped + roundTripped
      RoundTripsOutsideWf = acc.RoundTripsOutsideWf + outsideWf
      RoundTripFailuresOutsideWf = acc.RoundTripFailuresOutsideWf + outsideWfFailed
      WfPre = acc.WfPre + (if wfPre then 1 else 0)
      WfPreserved = acc.WfPreserved + wfPreserved
      Scripts = acc.Scripts }

/// Every op of every generated script at every state the script reaches, plus the script whole.
let private colDifferential (bridge: Cell -> ModelCol.cell) (seed: int) (trials: int) : ColTally =
    let mutable r = ConfRng.ofSeed seed
    let mutable tally = emptyColTally

    for _ in 1..trials do
        let start, r1 = genColTable r
        r <- r1
        let mutable st = start
        let ops = System.Collections.Generic.List<ColumnOp>()

        for _ in 1..6 do
            let op, r2 = genColOp st r
            r <- r2
            ops.Add op
            tally <- colProbe bridge op st tally

            match ColumnOps.apply op st with
            | Ok t -> st <- t
            | Error _ -> ()

        // 5. the script whole — short-circuit and all-or-nothing as one comparison
        let script = List.ofSeq ops
        let prod = ColumnOps.applyAll script start

        let model =
            ModelCol.apply_all
                modelEvaluator
                (script |> List.map (colOpToModelWith bridge))
                (tableToModelWith bridge start)

        let scriptDiff =
            match prod, model with
            | Ok pt, ModelCol.Ok mt when tableToModelWith bridge pt = mt -> []
            | Error pe, ModelCol.Error me when prodColRejRender pe = modelColRejRender me -> []
            | _ -> [ sprintf "applyAll differs on a %d-op script (seed %d)" (List.length script) seed ]

        tally <-
            { tally with
                Diffs = tally.Diffs @ scriptDiff
                Scripts = tally.Scripts + 1 }

    tally

type private ColDiffTally =
    { DDiffs: string list
      Pairs: int
      BothWf: int
      Granular: int
      Rebuild: int }

/// Pairs of tables: `toOps` as a script compared, and where both are well-formed the
/// reconstruction asserted on both sides. Half the pairs share a schema (the column-granular
/// branch); half are independent draws (the rebuild branch, almost always).
let private colDiffDifferential (seed: int) (trials: int) : ColDiffTally =
    let mutable r = ConfRng.ofSeed seed

    let mutable tally =
        { DDiffs = []
          Pairs = 0
          BothWf = 0
          Granular = 0
          Rebuild = 0 }

    for i in 1..trials do
        let before, r1 = genColTable r
        r <- r1

        let after, r2 =
            if i % 2 = 0 then
                genColTable r
            else
                // same schema, one column's cells redrawn — the granular branch's home
                match before.Columns with
                | [] -> before, r
                | cols ->
                    let idx, r3 = ConfRng.intBelow (List.length cols) r
                    let target = List.item idx cols
                    let cells, r4 = genColCells target.Type (List.length target.Cells) r3

                    { before with
                        Columns = cols |> List.mapi (fun j c -> if j = idx then { c with Cells = cells } else c) },
                    r4

        r <- r2
        let mb = tableToModel before
        let ma = tableToModel after
        let pScript = ColumnOps.toOps before after
        let mScript = ModelCol.to_ops mb ma

        let granular =
            (before.Schema = after.Schema && Table.rowCount before = Table.rowCount after)

        let scriptDiff =
            if (pScript |> List.map prodColOpRender) <> (mScript |> List.map modelColOpRender) then
                [ sprintf "toOps emits a different script (pair %d)" i ]
            else
                []

        let bothWf = ModelCol.wf mb && ModelCol.wf ma

        let reconDiff =
            if bothWf then
                (if ColumnOps.applyAll pScript before <> Ok after then
                     [ sprintf
                           "applyAll (toOps before after) before is NOT after on production (pair %d, %s)"
                           i
                           (if granular then "granular" else "rebuild") ]
                 else
                     [])
                @ (if ModelCol.apply_all modelEvaluator mScript mb <> ModelCol.Ok ma then
                       [ sprintf "the ORACLE's diff does not reconstruct (pair %d)" i ]
                   else
                       [])
            else
                []

        tally <-
            { DDiffs = tally.DDiffs @ scriptDiff @ reconDiff
              Pairs = tally.Pairs + 1
              BothWf = tally.BothWf + (if bothWf then 1 else 0)
              Granular = tally.Granular + (if bothWf && granular then 1 else 0)
              Rebuild = tally.Rebuild + (if bothWf && not granular then 1 else 0) }

    tally


// ---------------------------------------------------------------------------
//  Phase 177 — the FUNCTION SEAM: the extracted model of `Fuaran.Core.Function`'s effect
//  lattice, value spaces, function algebra and capability registry, beside production.
// ---------------------------------------------------------------------------
//
// The model's node type is a PARAMETER, so it is instantiated at the reference domain's `RNode`
// directly: the witness the model reads is `Reference.artw` bridged field for field, and a tree
// crosses without translation. Two records the host supplies close the model's two premises —
// the witness (`modelWitness`), and the three scalar readers (`readers`), which are production's
// own `Int32.TryParse`, `Double.TryParse`-against-a-float-range and `String.Length`. The go-red
// blinds the int reader.

let private toMOpt (o: 'a option) : FStar_Pervasives_Native.option<'a> =
    match o with
    | Some x -> FStar_Pervasives_Native.Some x
    | None -> FStar_Pervasives_Native.None

let private ofMOpt (o: FStar_Pervasives_Native.option<'a>) : 'a option =
    match o with
    | FStar_Pervasives_Native.Some x -> Some x
    | FStar_Pervasives_Native.None -> None

let private hostToModel (h: HostEffect) : ModelCap.host_effect =
    match h with
    | Pure -> ModelCap.Pure
    | ReadsHost -> ModelCap.ReadsHost
    | WritesHost -> ModelCap.WritesHost

let private hostOfModel (h: ModelCap.host_effect) : HostEffect =
    match h with
    | ModelCap.Pure -> Pure
    | ModelCap.ReadsHost -> ReadsHost
    | ModelCap.WritesHost -> WritesHost

let private detToModel (d: DeterminismSource) : ModelCap.determinism_source =
    match d with
    | Deterministic -> ModelCap.Deterministic
    | Clock -> ModelCap.Clock
    | Random -> ModelCap.Random
    | Network -> ModelCap.Network

let private detOfModel (d: ModelCap.determinism_source) : DeterminismSource =
    match d with
    | ModelCap.Deterministic -> Deterministic
    | ModelCap.Clock -> Clock
    | ModelCap.Random -> Random
    | ModelCap.Network -> Network

let private effToModel (e: EffectClass) : ModelCap.effect_class =
    { ModelCap.effect_class.host = hostToModel e.Host
      ModelCap.effect_class.determinism = detToModel e.Determinism }

let private effOfModel (e: ModelCap.effect_class) : EffectClass =
    { Host = hostOfModel e.host
      Determinism = detOfModel e.determinism }

/// A float range's bounds cross as opaque carriers (the readers premise): the round-trip `R`
/// format, which parses back to the same double.
let private spaceToModel (s: ValueSpace) : ModelCap.value_space =
    match s with
    | IntRange(lo, hi) -> ModelCap.IntRange(bigint lo, bigint hi)
    | FloatRange(lo, hi) -> ModelCap.FloatRange(lo.ToString("R", inv), hi.ToString("R", inv))
    | StringLen(lo, hi) -> ModelCap.StringLen(bigint lo, bigint hi)
    | Enum xs -> ModelCap.Enum xs
    | AnyString -> ModelCap.AnyString

let private spaceOfModel (s: ModelCap.value_space) : ValueSpace =
    match s with
    | ModelCap.IntRange(lo, hi) -> IntRange(int lo, int hi)
    | ModelCap.FloatRange(lo, hi) -> FloatRange(System.Double.Parse(lo, inv), System.Double.Parse(hi, inv))
    | ModelCap.StringLen(lo, hi) -> StringLen(int lo, int hi)
    | ModelCap.Enum xs -> Enum xs
    | ModelCap.AnyString -> AnyString

let private modelSpaceRender (s: ModelCap.value_space) : string =
    match s with
    | ModelCap.IntRange(lo, hi) -> sprintf "int[%s..%s]" (string lo) (string hi)
    | ModelCap.FloatRange(lo, hi) -> sprintf "float[%s..%s]" lo hi
    | ModelCap.StringLen(lo, hi) -> sprintf "len[%s..%s]" (string lo) (string hi)
    | ModelCap.Enum xs -> sprintf "enum{%s}" (String.concat "," xs)
    | ModelCap.AnyString -> "any"

/// The readers premise made concrete: the three host functions `Space.validate` reaches for,
/// exactly as production calls them.
let private readers: ModelCap.readers =
    { ModelCap.readers.int_of =
        fun s ->
            match System.Int32.TryParse s with
            | true, v -> FStar_Pervasives_Native.Some(bigint v)
            | _ -> FStar_Pervasives_Native.None
      ModelCap.readers.float_in =
        fun lo hi s -> Space.validate (FloatRange(System.Double.Parse(lo, inv), System.Double.Parse(hi, inv))) s
      ModelCap.readers.str_len = fun s -> bigint s.Length }

/// The go-red: an int reader that reads nothing, so every int-ranged value is out of space to
/// the model and in space to production.
let private blindReaders: ModelCap.readers =
    { readers with
        ModelCap.readers.int_of = fun _ -> FStar_Pervasives_Native.None }

let private holeKindToModel (k: HoleKind) : ModelCap.hole_kind =
    match k with
    | ValueHole s -> ModelCap.ValueHole(spaceToModel s)
    | SlotHole c -> ModelCap.SlotHole(toMOpt c)
    | RepeatHole s -> ModelCap.RepeatHole(spaceToModel s)
    | ActionHole e -> ModelCap.ActionHole(effToModel e)

let private holeDeclToModel (h: HoleDecl) : ModelCap.hole_decl =
    { ModelCap.hole_decl.h_addr = h.Addr
      ModelCap.hole_decl.h_name = h.Name
      ModelCap.hole_decl.h_kind = holeKindToModel h.Kind }

let private sigEntryToModel (e: SigEntry) : ModelCap.sig_entry =
    { ModelCap.sig_entry.s_addr = e.Addr
      ModelCap.sig_entry.s_name = e.Name
      ModelCap.sig_entry.s_kind = e.Kind
      ModelCap.sig_entry.s_space = toMOpt (Option.map spaceToModel e.Space)
      ModelCap.sig_entry.s_slot = toMOpt e.Slot
      ModelCap.sig_entry.s_action = toMOpt (Option.map effToModel e.Action)
      ModelCap.sig_entry.s_required = e.Required }

let private sigToModel (sg: Signature) : ModelCap.signature =
    { ModelCap.signature.sg_name = sg.Name
      ModelCap.signature.sg_holes = sg.Holes |> List.map sigEntryToModel
      ModelCap.signature.sg_effect = effToModel sg.Effect }

let private argToModel (a: Arg<RNode>) : ModelCap.arg<RNode> =
    match a with
    | ValueArg s -> ModelCap.ValueArg s
    | SlotArg n -> ModelCap.SlotArg n

let private argOfModel (a: ModelCap.arg<RNode>) : Arg<RNode> =
    match a with
    | ModelCap.ValueArg s -> ValueArg s
    | ModelCap.SlotArg n -> SlotArg n

/// `Reference.artw`, as the model reads it — the five functions the algebra consults, and no
/// others. The tree is the same `RNode` on both sides.
let private modelWitness: ModelCap.witness<RNode> =
    { ModelCap.witness.holes = fun n -> artw.Holes n |> List.map holeDeclToModel
      ModelCap.witness.eff = fun n -> effToModel (artw.Effect n)
      ModelCap.witness.bind_hole =
        fun addr a n ->
            match artw.Bind addr (argOfModel a) n with
            | Ok r -> ModelCap.Ok r
            | Error m -> ModelCap.Error m
      ModelCap.witness.kind_tag = artw.Tree.KindTag
      ModelCap.witness.preorder = fun n -> Tree.preorder artw.Tree n }

let private prodApplyErrRender (e: ApplyError) : string =
    match e with
    | UnknownHoleAddr(a, d) -> sprintf "UnknownHoleAddr(%s;%s)" a (String.concat "," d)
    | ValueOutOfSpace(a, s, g) -> sprintf "ValueOutOfSpace(%s;%s;%s)" a (modelSpaceRender (spaceToModel s)) g
    | RequiredHolesUnbound xs -> sprintf "RequiredHolesUnbound(%s)" (String.concat "," xs)
    | NotASlot a -> sprintf "NotASlot(%s)" a
    | SlotKindMismatch(a, e, g) -> sprintf "SlotKindMismatch(%s;%s;%s)" a e g
    | NonTotal a -> sprintf "NonTotal(%s)" a
    | BindFailed(a, m) -> sprintf "BindFailed(%s;%s)" a m

let private modelApplyErrRender (e: ModelCap.apply_error) : string =
    match e with
    | ModelCap.UnknownHoleAddr(a, d) -> sprintf "UnknownHoleAddr(%s;%s)" a (String.concat "," d)
    | ModelCap.ValueOutOfSpace(a, s, g) -> sprintf "ValueOutOfSpace(%s;%s;%s)" a (modelSpaceRender s) g
    | ModelCap.RequiredHolesUnbound xs -> sprintf "RequiredHolesUnbound(%s)" (String.concat "," xs)
    | ModelCap.NotASlot a -> sprintf "NotASlot(%s)" a
    | ModelCap.SlotKindMismatch(a, e, g) -> sprintf "SlotKindMismatch(%s;%s;%s)" a e g
    | ModelCap.NonTotal a -> sprintf "NonTotal(%s)" a
    | ModelCap.BindFailed(a, m) -> sprintf "BindFailed(%s;%s)" a m

let private applyErrClass (e: ApplyError) : string =
    (prodApplyErrRender e).Substring(0, (prodApplyErrRender e).IndexOf '(')

/// `NoSuchCapability`'s `known` list is production's sorted map keys and the model's list in
/// registration order — the ONE ordering the model does not carry; both render sorted.
let private prodInvokeErrRender (e: InvokeError) : string =
    match e with
    | NoSuchCapability(id, known) -> sprintf "NoSuchCapability(%s;%s)" id (String.concat "," (List.sort known))
    | DuplicateCapability id -> sprintf "DuplicateCapability(%s)" id
    | UnknownArg(a, d) -> sprintf "UnknownArg(%s;%s)" a (String.concat "," d)
    | ArgOutOfSpace(a, s, g) -> sprintf "ArgOutOfSpace(%s;%s;%s)" a (modelSpaceRender (spaceToModel s)) g
    | RequiredArgsUnbound xs -> sprintf "RequiredArgsUnbound(%s)" (String.concat "," xs)
    | UninvocableArg a -> sprintf "UninvocableArg(%s)" a
    | BodyFailed m -> sprintf "BodyFailed(%s)" m

let private modelInvokeErrRender (e: ModelCap.invoke_error) : string =
    match e with
    | ModelCap.NoSuchCapability(id, known) -> sprintf "NoSuchCapability(%s;%s)" id (String.concat "," (List.sort known))
    | ModelCap.DuplicateCapability id -> sprintf "DuplicateCapability(%s)" id
    | ModelCap.UnknownArg(a, d) -> sprintf "UnknownArg(%s;%s)" a (String.concat "," d)
    | ModelCap.ArgOutOfSpace(a, s, g) -> sprintf "ArgOutOfSpace(%s;%s;%s)" a (modelSpaceRender s) g
    | ModelCap.RequiredArgsUnbound xs -> sprintf "RequiredArgsUnbound(%s)" (String.concat "," xs)
    | ModelCap.UninvocableArg a -> sprintf "UninvocableArg(%s)" a
    | ModelCap.BodyFailed m -> sprintf "BodyFailed(%s)" m

let private invokeErrClass (e: InvokeError) : string =
    (prodInvokeErrRender e).Substring(0, (prodInvokeErrRender e).IndexOf '(')

/// Phase 210 — the body answers in the `Deferred` envelope on both sides, and the two `deferred`
/// types are distinct (production's `Fuaran.Core.Deferred`, the model's extracted `deferred`), so
/// the accepted VALUE is compared by rendering exactly as the refusal already is.
let private prodDeferredRender (d: Deferred<int>) : string =
    match d with
    | Pending -> "Pending"
    | Ready v -> sprintf "Ready(%d)" v
    | Failed m -> sprintf "Failed(%s)" m

let private modelDeferredRender (d: ModelCap.deferred<int>) : string =
    match d with
    | ModelCap.Pending -> "Pending"
    | ModelCap.Ready v -> sprintf "Ready(%d)" v
    | ModelCap.Failed m -> sprintf "Failed(%s)" m

let private placementToModel (p: Placement) : ModelCap.placement =
    match p with
    | BuildTime -> ModelCap.BuildTime
    | Server -> ModelCap.Server
    | ClientDeclarative -> ModelCap.ClientDeclarative
    | ClientIsland Pyodide -> ModelCap.ClientIsland ModelCap.Pyodide
    | ClientIsland Fable -> ModelCap.ClientIsland ModelCap.Fable
    | ClientIsland Js -> ModelCap.ClientIsland ModelCap.Js
    | Precomputed -> ModelCap.Precomputed

let private capToModel (c: Capability) : ModelCap.capability =
    { ModelCap.capability.c_id = c.Id
      ModelCap.capability.c_signature = sigToModel c.Signature
      ModelCap.capability.c_determinism = detToModel c.Determinism
      ModelCap.capability.c_placement = placementToModel c.Placement }

// ---- generators ----

let private effPool =
    [ Effect.pureDeterministic
      { Host = ReadsHost
        Determinism = Deterministic }
      { Host = Pure; Determinism = Clock }
      { Host = WritesHost
        Determinism = Random }
      { Host = ReadsHost
        Determinism = Network } ]

let private genSpace (r: ConfRng.T) : ValueSpace * ConfRng.T =
    let roll, r1 = ConfRng.intBelow 5 r
    let lo, r2 = ConfRng.intBelow 5 r1
    let span, r3 = ConfRng.intBelow 5 r2

    match roll with
    | 0 -> IntRange(lo, lo + span), r3
    | 1 -> FloatRange(float lo / 2.0, float (lo + span) / 2.0 + 0.5), r3
    | 2 -> StringLen(lo, lo + span), r3
    | 3 -> Enum [ "a"; "b" ], r3
    | _ -> AnyString, r3

/// A value for a space — in space more often than not, and out of it (or unparseable) the rest.
let private genValueFor (s: ValueSpace) (r: ConfRng.T) : string * ConfRng.T =
    let roll, r1 = ConfRng.intBelow 10 r
    let k, r2 = ConfRng.intBelow 8 r1

    match s with
    | IntRange(lo, hi) ->
        (if roll < 6 then string (lo + k % (hi - lo + 1))
         elif roll < 8 then string (hi + 1 + k)
         else "x"),
        r2
    | FloatRange(lo, hi) ->
        (if roll < 6 then
             (lo + (hi - lo) * float (k % 4) / 4.0).ToString("R", inv)
         elif roll < 8 then
             (hi + 1.0).ToString("R", inv)
         else
             "nan?"),
        r2
    | StringLen(lo, hi) -> String.replicate (if roll < 6 then lo + k % (hi - lo + 1) else hi + 1 + k) "s", r2
    | Enum xs -> (if roll < 7 then List.item (k % List.length xs) xs else "zz"), r2
    | AnyString -> sprintf "v%d" k, r2

let private genHoleKind (r: ConfRng.T) : HoleKind * ConfRng.T =
    let roll, r1 = ConfRng.intBelow 10 r
    let s, r2 = genSpace r1
    let e, r3 = ConfRng.choose effPool r2

    if roll < 4 then
        ValueHole s, r3
    elif roll < 6 then
        SlotHole(if roll = 4 then Some "para" else None), r3
    elif roll < 8 then
        RepeatHole s, r3 // AnyString one draw in five — the unbounded repeat
    else
        ActionHole e, r3

/// An artifact: a root over up to four children, each a hole (`h1`..`h4`), a leaf, or a group
/// holding a hole that REUSES an earlier hole's name (the hygiene case), every node with its
/// own effect so the observed effect and the audit vary.
let private genArtifact (r: ConfRng.T) : RNode * ConfRng.T =
    let n, r1 = ConfRng.intBelow 5 r
    let mutable rng = r1
    let children = System.Collections.Generic.List<RNode>()

    for i in 1..n do
        let roll, r2 = ConfRng.intBelow 10 rng
        let hk, r3 = genHoleKind r2
        let e, r4 = ConfRng.choose effPool r3
        rng <- r4
        let name = sprintf "x%d" (i % 2 + 1)

        if roll < 6 then
            children.Add
                { RNode.hole (sprintf "h%d" i) "field" name hk with
                    Eff = e }
        elif roll < 8 then
            children.Add
                { RNode.leaf (sprintf "l%d" i) "para" "v" with
                    Eff = e }
        else
            children.Add(
                { RNode.node (sprintf "g%d" i) "group" [ RNode.hole (sprintf "gh%d" i) "field" name hk ] with
                    Eff = e }
            )

    let rootEff, r5 = ConfRng.choose effPool rng

    { RNode.node "root" "doc" (List.ofSeq children) with
        Eff = rootEff },
    r5

/// An argument set for an artifact's holes: three draws in four bind a hole (with a value for
/// its space, or — one draw in eight — an arg of the wrong axis), and one draw in six adds an
/// arg at an address no hole declares.
let private genArgs (t: RNode) (r: ConfRng.T) : Map<string, Arg<RNode>> * ConfRng.T =
    let mutable rng = r
    let mutable args = Map.empty

    for h in artw.Holes t do
        let roll, r1 = ConfRng.intBelow 8 rng
        rng <- r1

        if roll < 6 then
            let a, r2 =
                match h.Kind with
                | ValueHole s
                | RepeatHole s ->
                    if roll = 5 then
                        SlotArg(RNode.leaf "in" "para" "z"), rng
                    else
                        let v, r' = genValueFor s rng
                        ValueArg v, r'
                | SlotHole _ ->
                    if roll = 5 then ValueArg "s", rng
                    elif roll = 4 then SlotArg(RNode.leaf "in" "field" "z"), rng
                    else SlotArg(RNode.leaf "in" "para" "z"), rng
                | ActionHole _ -> ValueArg "act", rng

            rng <- r2
            args <- Map.add h.Addr a args

    let extra, r3 = ConfRng.intBelow 6 rng
    rng <- r3

    if extra = 0 then
        args <- Map.add "root/zz" (ValueArg "1") args

    args, rng

type private FnTally =
    { FDiffs: string list
      Artifacts: int
      Applied: int
      ApplyRefused: int
      Curried: int
      CurryRefused: int
      Composed: int
      ComposeRefused: int
      NonTotal: int
      AuditErrors: int
      FClasses: Set<string> }

let private fnProbe
    (rd: ModelCap.readers)
    (t: RNode)
    (args: Map<string, Arg<RNode>>)
    (inner: RNode)
    (slot: string)
    (acc: FnTally)
    : FnTally =
    let mw = modelWitness
    let margs = args |> Map.toList |> List.map (fun (k, v) -> k, argToModel v)
    let diffs = System.Collections.Generic.List<string>()

    let label =
        sprintf "artifact %A args %A" (artw.Holes t |> List.map (fun h -> h.Addr, h.Kind)) (Map.keys args |> List.ofSeq)

    // signature, isTotal, signatureExcluding
    let psg = Function.signature artw "f" t
    let msg = ModelCap.signature_of mw "f" t

    if sigToModel psg <> msg then
        diffs.Add(sprintf "%s: signature differs\n  prod %A\n  model %A" label (sigToModel psg) msg)

    if Function.isTotal psg <> ModelCap.is_total msg then
        diffs.Add(
            sprintf "%s: isTotal differs (prod %b, model %b)" label (Function.isTotal psg) (ModelCap.is_total msg)
        )

    let bound = args |> Map.keys |> List.ofSeq

    if
        sigToModel (Function.signatureExcluding (Set.ofList bound) psg)
        <> ModelCap.signature_excluding bound msg
    then
        diffs.Add(sprintf "%s: signatureExcluding differs" label)

    // apply / curry — verdict, result tree, rejection class and payload
    let compare (what: string) (p: Result<RNode, ApplyError>) (m: ModelCap.outcome<RNode, ModelCap.apply_error>) =
        match p, m with
        | Ok pn, ModelCap.Ok mn ->
            if pn <> mn then
                diffs.Add(sprintf "%s: %s accepted on both sides with DIFFERENT trees" label what)
        | Error pe, ModelCap.Error me ->
            if prodApplyErrRender pe <> modelApplyErrRender me then
                diffs.Add(
                    sprintf
                        "%s: %s refused differently\n  prod %s\n  model %s"
                        label
                        what
                        (prodApplyErrRender pe)
                        (modelApplyErrRender me)
                )
        | Ok _, ModelCap.Error me ->
            diffs.Add(
                sprintf "%s: %s accepted by production, refused by the model (%s)" label what (modelApplyErrRender me)
            )
        | Error pe, ModelCap.Ok _ ->
            diffs.Add(
                sprintf "%s: %s refused by production (%s), accepted by the model" label what (prodApplyErrRender pe)
            )

    let pApply = Function.apply artw args t
    compare "apply" pApply (ModelCap.apply rd mw margs t)
    let pCurry = Function.curry artw args t
    compare "curry" pCurry (ModelCap.curry rd mw margs t)
    let pComp = Function.compose artw slot inner t
    compare "compose" pComp (ModelCap.compose mw slot inner t)

    // the effect surfaces
    if
        effToModel (Function.composedEffect artw inner t)
        <> ModelCap.composed_effect mw inner t
    then
        diffs.Add(sprintf "%s: composedEffect differs" label)

    if effToModel (Function.observedEffect artw t) <> ModelCap.observed_effect mw t then
        diffs.Add(sprintf "%s: observedEffect differs" label)

    let pAudit = Function.auditEffect artw t
    let mAudit = ModelCap.audit_effect mw t

    (match pAudit, mAudit with
     | Ok(), ModelCap.Ok() -> ()
     | Error(d, a), ModelCap.Error(md, ma) ->
         if effToModel d <> md || effToModel a <> ma then
             diffs.Add(sprintf "%s: auditEffect error payload differs" label)
     | _ -> diffs.Add(sprintf "%s: auditEffect verdict differs" label))

    let cls (r: Result<RNode, ApplyError>) =
        match r with
        | Ok _ -> None
        | Error e -> Some(applyErrClass e)

    { FDiffs = acc.FDiffs @ List.ofSeq diffs
      Artifacts = acc.Artifacts + 1
      Applied = acc.Applied + (if Result.isOk pApply then 1 else 0)
      ApplyRefused = acc.ApplyRefused + (if Result.isError pApply then 1 else 0)
      Curried = acc.Curried + (if Result.isOk pCurry then 1 else 0)
      CurryRefused = acc.CurryRefused + (if Result.isError pCurry then 1 else 0)
      Composed = acc.Composed + (if Result.isOk pComp then 1 else 0)
      ComposeRefused = acc.ComposeRefused + (if Result.isError pComp then 1 else 0)
      NonTotal = acc.NonTotal + (if Function.isTotal psg then 0 else 1)
      AuditErrors = acc.AuditErrors + (if Result.isError pAudit then 1 else 0)
      FClasses =
        [ cls pApply; cls pCurry; cls pComp ]
        |> List.choose id
        |> List.fold (fun s c -> Set.add c s) acc.FClasses }

let private fnDifferential (rd: ModelCap.readers) (seed: int) (trials: int) : FnTally =
    let mutable rng = ConfRng.ofSeed seed

    let mutable tally =
        { FDiffs = []
          Artifacts = 0
          Applied = 0
          ApplyRefused = 0
          Curried = 0
          CurryRefused = 0
          Composed = 0
          ComposeRefused = 0
          NonTotal = 0
          AuditErrors = 0
          FClasses = Set.empty }

    for _ in 1..trials do
        let t, r1 = genArtifact rng
        let args, r2 = genArgs t r1
        let innerKind, r3 = ConfRng.choose [ "para"; "field" ] r2
        let holes = artw.Holes t
        let slotRoll, r4 = ConfRng.intBelow 4 r3

        let slots =
            holes
            |> List.filter (fun h ->
                match h.Kind with
                | SlotHole _ -> true
                | _ -> false)

        let slot =
            if slotRoll = 0 || List.isEmpty holes then
                "root/nope"
            elif slotRoll < 3 && not (List.isEmpty slots) then
                (List.item (slotRoll % List.length slots) slots).Addr
            else
                (List.item (slotRoll % List.length holes) holes).Addr

        rng <- r4
        tally <- fnProbe rd t args (RNode.leaf "in" innerKind "z") slot tally

    tally

type private CapTally =
    { CDiffs: string list
      Registered: int
      DupRefused: int
      Dispatched: int
      NoSuch: int
      Validated: int
      Refused: int
      BodyRan: int
      BodyFailed: int
      RefusedWithoutBody: int
      CClasses: Set<string> }

/// A signature for a capability: up to three entries drawn through `Function.signature` of a
/// generated artifact, so the entry vocabulary is production's own.
let private genCapSignature (name: string) (r: ConfRng.T) : Signature * ConfRng.T =
    let t, r1 = genArtifact r
    let e, r2 = ConfRng.choose effPool r1

    { Function.signature artw name t with
        Effect = e },
    r2

let private capIdPool = [ "cap-a"; "cap-b"; "cap-c" ]

/// A typed invocation: for each entry, three draws in four an arg (a value for its space, or
/// — for a slot entry — a value it cannot take), plus one draw in five an unknown address.
let private genInvocation (sg: Signature) (r: ConfRng.T) : (string * string) list * ConfRng.T =
    let mutable rng = r
    let args = System.Collections.Generic.List<string * string>()

    for e in sg.Holes do
        let roll, r1 = ConfRng.intBelow 4 rng
        rng <- r1

        if roll < 3 then
            match e.Space with
            | Some s ->
                let v, r2 = genValueFor s rng
                rng <- r2
                args.Add(e.Addr, v)
            | None -> args.Add(e.Addr, "s")

    let extra, r3 = ConfRng.intBelow 5 rng
    rng <- r3

    if extra = 0 then
        args.Add("root/zz", "1")

    List.ofSeq args, rng

let private capProbe (rd: ModelCap.readers) (seedTag: int) (acc: CapTally) (r: ConfRng.T) : CapTally * ConfRng.T =
    let diffs = System.Collections.Generic.List<string>()
    let mutable rng = r

    // build a registry on both sides, in the same order, with duplicates possible
    let n, r1 = ConfRng.intBelow 4 rng
    rng <- r1
    let mutable preg = Registry.empty
    let mutable mreg = ModelCap.empty
    let mutable registered = 0
    let mutable dupRefused = 0

    for i in 1..n do
        let id, r2 = ConfRng.choose capIdPool rng
        let sg, r3 = genCapSignature (sprintf "f%d" i) r2
        let placement, r4 = ConfRng.choose [ BuildTime; Server; ClientIsland Pyodide ] r3
        rng <- r4
        let cap = Capability.create id sg placement

        match Registry.register cap preg, ModelCap.register (capToModel cap) mreg with
        | Ok p', ModelCap.Ok m' ->
            preg <- p'
            mreg <- m'
            registered <- registered + 1
        | Error pe, ModelCap.Error me ->
            dupRefused <- dupRefused + 1

            if prodInvokeErrRender pe <> modelInvokeErrRender me then
                diffs.Add(
                    sprintf
                        "seed %d: register refused differently (%s vs %s)"
                        seedTag
                        (prodInvokeErrRender pe)
                        (modelInvokeErrRender me)
                )
        | _ -> diffs.Add(sprintf "seed %d: register verdict differs on %s" seedTag id)

    // enumerate + tryFind agree (membership, and the entries themselves, sorted by id)
    let penum = Registry.enumerate preg |> List.map capToModel
    let menum = ModelCap.enumerate mreg |> List.sortBy (fun c -> c.c_id)

    if penum <> menum then
        diffs.Add(sprintf "seed %d: enumerate differs" seedTag)

    for id in "cap-x" :: capIdPool do
        let p = Registry.tryFind id preg |> Option.map capToModel
        let m = ofMOpt (ModelCap.try_find_cap id mreg)

        if p <> m then
            diffs.Add(sprintf "seed %d: tryFind %s differs" seedTag id)

    // invocations: an id from the pool or an unregistered one, args drawn against the
    // resolved capability's signature when there is one
    let mutable dispatched = 0
    let mutable noSuch = 0
    let mutable validated = 0
    let mutable refused = 0
    let mutable bodyRan = 0
    let mutable bodyFailed = 0
    let mutable refusedWithoutBody = 0
    let mutable classes = acc.CClasses
    let k, r5 = ConfRng.intBelow 4 rng
    rng <- r5

    for _ in 0..k do
        let id, r6 = ConfRng.choose ("cap-x" :: capIdPool) rng
        rng <- r6

        let args, r7 =
            match Registry.tryFind id preg with
            | Some c -> genInvocation c.Signature rng
            | None -> [ "h1", "1" ], rng

        // Phase 210 — the body answers in the envelope, so the draw reaches all three of its cases:
        // 0 fails (the typed `BodyFailed`), 1 is still pending, 2 and 3 settle.
        let failBody, r8 = ConfRng.intBelow 4 r7
        rng <- r8
        let pRan = ref false
        let mRan = ref false

        let pBody (_: Capability) () =
            pRan.Value <- true

            match failBody with
            | 0 -> Failed "boom"
            | 1 -> Pending
            | _ -> Ready 42

        let mBody (_: ModelCap.capability) () =
            mRan.Value <- true

            match failBody with
            | 0 -> ModelCap.Failed "boom"
            | 1 -> ModelCap.Pending
            | _ -> ModelCap.Ready 42

        let p = Registry.dispatch preg id args pBody
        let m = ModelCap.dispatch rd mreg id args mBody

        (match p, m with
         | Ok pv, ModelCap.Ok mv ->
             dispatched <- dispatched + 1

             if prodDeferredRender pv <> modelDeferredRender mv then
                 diffs.Add(
                     sprintf
                         "seed %d: dispatch %s accepted with different envelopes\n  prod %s\n  model %s"
                         seedTag
                         id
                         (prodDeferredRender pv)
                         (modelDeferredRender mv)
                 )

             // and neither side ever hands an `Ok(Failed _)` out of the seam
             // (`invoke_never_ok_failed` / `dispatch_never_ok_failed`, sampled here on the shipped one).
             match pv, mv with
             | Failed _, _
             | _, ModelCap.Failed _ ->
                 diffs.Add(sprintf "seed %d: dispatch %s accepted with an Ok(Failed _) envelope" seedTag id)
             | _ -> ()
         | Error pe, ModelCap.Error me ->
             classes <- Set.add (invokeErrClass pe) classes

             if prodInvokeErrRender pe <> modelInvokeErrRender me then
                 diffs.Add(
                     sprintf
                         "seed %d: dispatch %s refused differently\n  prod %s\n  model %s"
                         seedTag
                         id
                         (prodInvokeErrRender pe)
                         (modelInvokeErrRender me)
                 )

             match pe with
             | NoSuchCapability _ -> noSuch <- noSuch + 1
             | BodyFailed _ -> bodyFailed <- bodyFailed + 1
             | _ -> refused <- refused + 1
         | Ok _, ModelCap.Error me ->
             diffs.Add(
                 sprintf
                     "seed %d: dispatch %s accepted by production, refused by the model (%s)"
                     seedTag
                     id
                     (modelInvokeErrRender me)
             )
         | Error pe, ModelCap.Ok _ ->
             diffs.Add(
                 sprintf
                     "seed %d: dispatch %s refused by production (%s), accepted by the model"
                     seedTag
                     id
                     (prodInvokeErrRender pe)
             ))

        // the body ran on both sides or on neither — and never past a refusal that is not the body's
        if pRan.Value <> mRan.Value then
            diffs.Add(
                sprintf
                    "seed %d: dispatch %s ran the body on one side only (prod %b, model %b)"
                    seedTag
                    id
                    pRan.Value
                    mRan.Value
            )

        if pRan.Value then
            bodyRan <- bodyRan + 1

        (match p with
         | Error(BodyFailed _)
         | Ok _ -> ()
         | Error _ ->
             refusedWithoutBody <- refusedWithoutBody + 1

             if pRan.Value then
                 diffs.Add(sprintf "seed %d: dispatch %s was REFUSED and the body still ran" seedTag id))

        // validateArgs on its own, on the resolved capability
        match Registry.tryFind id preg with
        | Some c ->
            match Capability.validateArgs c args, ModelCap.validate_args rd (capToModel c) args with
            | Ok(), ModelCap.Ok() -> validated <- validated + 1
            | Error pe, ModelCap.Error me ->
                if prodInvokeErrRender pe <> modelInvokeErrRender me then
                    diffs.Add(
                        sprintf
                            "seed %d: validateArgs refused differently\n  prod %s\n  model %s"
                            seedTag
                            (prodInvokeErrRender pe)
                            (modelInvokeErrRender me)
                    )
            | _ -> diffs.Add(sprintf "seed %d: validateArgs verdict differs on %s" seedTag id)
        | None -> ()

    { CDiffs = acc.CDiffs @ List.ofSeq diffs
      Registered = acc.Registered + registered
      DupRefused = acc.DupRefused + dupRefused
      Dispatched = acc.Dispatched + dispatched
      NoSuch = acc.NoSuch + noSuch
      Validated = acc.Validated + validated
      Refused = acc.Refused + refused
      BodyRan = acc.BodyRan + bodyRan
      BodyFailed = acc.BodyFailed + bodyFailed
      RefusedWithoutBody = acc.RefusedWithoutBody + refusedWithoutBody
      CClasses = classes },
    rng

let private capDifferential (rd: ModelCap.readers) (seed: int) (trials: int) : CapTally =
    let mutable rng = ConfRng.ofSeed seed

    let mutable tally =
        { CDiffs = []
          Registered = 0
          DupRefused = 0
          Dispatched = 0
          NoSuch = 0
          Validated = 0
          Refused = 0
          BodyRan = 0
          BodyFailed = 0
          RefusedWithoutBody = 0
          CClasses = Set.empty }

    for i in 1..trials do
        let t, r' = capProbe rd i tally rng
        tally <- t
        rng <- r'

    tally


// ------------------------------------------------------------------------------------------
// Phase 186 — the INCREMENTAL PROMISE. `proofs/Propagation.fst` models
// `Fuaran.Core.Propagation`'s dirty set (`dependents`, `dirtyFromChangedIds`, `staleSet`) and its
// driver (`eval`, `evalFrom`) clause for clause, over an abstract node evaluator and with
// `sort`'s result handed in; `proofs/oracle/Propagation.fs` is that model extracted. This runs
// it BESIDE production over generated dependency maps — acyclic ones, the generator
// `Conformance.propagationEvalLaws` and `dirtyPropagationLaws` use, WIDENED with back edges
// (cycles), dangling reads, multi-id change sets, unknown ids, failing evaluators and priors
// with a hole in them — and holds production's `sort` to the one fact the agreement theorem
// assumes of it: `Order` holds no id twice.
//
// Phase 209 — the model gained the RESTRICTED resolver and its `EvalUndeclaredRead` refusal, so
// every model call now carries a `read_witness`: the ids the evaluator actually reads at a node, as
// production's instrumented resolver observes them. A pure `Tot` function cannot observe a call, so
// the model takes the observation as a parameter and the bridge SUPPLIES it — which makes the
// witness this file passes the model's half of `propagation-read-witness`. Getting it wrong here
// would make the differential green against a model of a different driver, so the witness is derived
// from the evaluator it is paired with and never guessed: the toy evaluator reads exactly
// `reads_of deps id`, and the third case's evaluator reads `a` at `b` whatever the map says.
// ------------------------------------------------------------------------------------------

/// The deps bridge: a `Map<string, Set<string>>` as the model reads it (`sets-are-lists`).
let private depsToModel (deps: Map<string, Set<string>>) : ModelProp.dmap =
    deps |> Map.toList |> List.map (fun (k, rs) -> k, Set.toList rs)

/// The go-red: a bridge that LOSES a read edge — every node's last read is dropped — so the
/// model's dependents map, its dirty set and its reuse are all computed over a smaller graph
/// than production's.
let private blindDepsBridge (deps: Map<string, Set<string>>) : ModelProp.dmap =
    deps
    |> Map.toList
    |> List.map (fun (k, rs) -> k, (Set.toList rs |> List.truncate (max 0 (Set.count rs - 1))))

let private modelDepsToMap (d: ModelProp.dmap) : Map<string, Set<string>> =
    d |> List.map (fun (k, vs) -> k, Set.ofList vs) |> Map.ofList

/// The read witness of an evaluator that reads exactly what its map declares — `propSpec`'s, and
/// the one every generated trial uses. Derived from the same `deps` the evaluator folds over, so the
/// two cannot drift apart.
let private declaredReads (deps: Map<string, Set<string>>) : ModelProp.read_witness =
    fun id ->
        match Map.tryFind id deps with
        | Some rs -> Set.toList rs
        | None -> []

/// The base at which the toy evaluator refuses by name.
let private failBase = -7

/// The toy pull evaluator of the two law families, with a designated failure. It reads ONLY its
/// declared reads, which since Phase 209 the driver ENFORCES rather than assuming.
let private propSpec
    (baseOf: Map<string, int>)
    (deps: Map<string, Set<string>>)
    (resolve: string -> int option)
    (id: string)
    : Result<int, string> =
    let b = Map.find id baseOf

    if b = failBase then
        Error("refused:" + id)
    else
        Ok(
            b
            + (Map.find id deps
               |> Set.fold (fun s r -> s + (resolve r |> Option.defaultValue -1)) 0)
        )

/// Production's evaluator, recording the ids it is invoked on.
let private propProdEvaluator
    (spec: (string -> int option) -> string -> Result<int, string>)
    (invoked: ResizeArray<string>)
    =
    fun (resolve: string -> int option) (id: string) ->
        invoked.Add id
        spec resolve id

/// The SAME evaluator as the model reads it: the resolver and the result cross the option and
/// outcome shims and nothing else changes.
let private propModelEvaluator
    (spec: (string -> int option) -> string -> Result<int, string>)
    : ModelProp.evaluator<int> =
    fun resolve id ->
        match spec (fun k -> ofMOpt (resolve k)) id with
        | Ok v -> ModelProp.Ok v
        | Error m -> ModelProp.Error m

let private topoToModel (t: Propagation.TopoResult) : ModelProp.topo_result =
    { ModelProp.topo_result.order = t.Order
      ModelProp.topo_result.cycles = t.Cycles }

/// Both sides rendered alike: the values as a sorted association list (production's `Map`
/// order; the model holds them newest-first), the cyclic groups as given, a refusal by class
/// and payload.
let private prodOutcomeRender (r: Result<Propagation.EvalOutcome<int>, Propagation.PropagationError>) : string =
    match r with
    | Ok o -> sprintf "Ok values=%A cyclic=%A" (Map.toList o.Values) o.Cyclic
    | Error(Propagation.EvalUnknownChange ids) -> sprintf "EvalUnknownChange %A" ids
    | Error(Propagation.EvalNodeFailed(n, m)) -> sprintf "EvalNodeFailed %s %s" n m
    | Error(Propagation.EvalUndeclaredRead(n, r)) -> sprintf "EvalUndeclaredRead %s %s" n r

let private modelOutcomeRender
    (r: ModelProp.outcome<ModelProp.eval_outcome<int>, ModelProp.propagation_error>)
    : string =
    match r with
    | ModelProp.Ok o -> sprintf "Ok values=%A cyclic=%A" (o.values |> Map.ofList |> Map.toList) o.cyclic
    | ModelProp.Error(ModelProp.EvalUnknownChange ids) -> sprintf "EvalUnknownChange %A" ids
    | ModelProp.Error(ModelProp.EvalNodeFailed(n, m)) -> sprintf "EvalNodeFailed %s %s" n m
    | ModelProp.Error(ModelProp.EvalUndeclaredRead(n, r)) -> sprintf "EvalUndeclaredRead %s %s" n r

type private PropTally =
    { PDiffs: string list
      Graphs: int
      CyclicGraphs: int
      DanglingGraphs: int
      OrdersDistinct: int
      DirtyNodes: int
      CleanNodes: int
      Reused: int
      AbsentRecomputed: int
      Agreed: int
      NodeFailed: int
      UnknownRefused: int }

let private propProbe
    (bridge: Map<string, Set<string>> -> ModelProp.dmap)
    (i: int)
    (acc: PropTally)
    (rng: ConfRng.T)
    : PropTally * ConfRng.T =
    let mutable r = rng
    let mutable diffs = []

    let draw n =
        let v, r' = ConfRng.intBelow n r
        r <- r'
        v

    let nNodes = draw 6 + 2
    let ids = [ for k in 0 .. nNodes - 1 -> string k ]
    // one graph in four carries back edges (a cycle), one in four dangling reads
    let shape = draw 4
    let cyclicWanted = (shape = 1)
    let danglingWanted = (shape = 2)

    let deps =
        [ for k in 0 .. nNodes - 1 ->
              let lower =
                  [ for j in 0 .. k - 1 do
                        if draw 3 = 0 then
                            yield string j ]

              let back =
                  if cyclicWanted && k < nNodes - 1 && draw 2 = 0 then
                      [ string (k + 1 + draw (nNodes - 1 - k)) ]
                  else
                      []

              let dangling = if danglingWanted && draw 4 = 0 then [ "zz" ] else []
              string k, Set.ofList (lower @ back @ dangling) ]
        |> Map.ofList

    let base0 = [ for id in ids -> id, draw 100 ] |> Map.ofList

    // the change: one to three ids, each given a new base — one time in six the FAILING base
    let nChanged = draw 3 + 1

    let changedIds = [ for _ in 1..nChanged -> string (draw nNodes) ] |> Set.ofList

    let base1 =
        (base0, changedIds)
        ||> Set.fold (fun m c -> Map.add c (if draw 6 = 0 then failBase else Map.find c base0 + 1000) m)

    // one trial in eight also names an id the map does not hold
    let changed =
        if draw 8 = 0 then
            Set.add "no-such-id" changedIds
        else
            changedIds

    let mdeps = bridge deps
    let topo = Propagation.sort deps
    let mtopo = topoToModel topo
    // The witness is `propSpec`'s own reads, derived from PRODUCTION's map — never from the bridge.
    // Under the go-red bridge that is the point: the evaluator still reads the edge the bridge
    // dropped, so the model sees an undeclared read there and refuses where production does not.
    let mreads = declaredReads deps

    let where =
        sprintf "trial %d deps=%A changed=%A" i (Map.toList deps) (Set.toList changed)

    // (0) the bridge the agreement theorem assumes: `sort`'s Order holds no id twice
    let orderDistinct = (List.distinct topo.Order = topo.Order)

    if not orderDistinct then
        diffs <- sprintf "%s: sort's Order holds an id TWICE: %A" where topo.Order :: diffs

    // (1) dependents
    let prodDependents = Propagation.dependents deps
    let modelDependents = modelDepsToMap (ModelProp.dependents mdeps)

    if prodDependents <> modelDependents then
        diffs <-
            sprintf
                "%s: dependents production=%A model=%A"
                where
                (Map.toList prodDependents)
                (Map.toList modelDependents)
            :: diffs

    // (2) the dirty set, and its alias
    let prodDirty = Propagation.dirtyFromChangedIds deps changed

    let modelDirty =
        Set.ofList (ModelProp.dirty_from_changed_ids mdeps (Set.toList changed))

    if prodDirty <> modelDirty then
        diffs <-
            sprintf "%s: dirty production=%A model=%A" where (Set.toList prodDirty) (Set.toList modelDirty)
            :: diffs

    if
        Propagation.staleSet deps changed
        <> Set.ofList (ModelProp.stale_set mdeps (Set.toList changed))
    then
        diffs <- sprintf "%s: staleSet disagrees" where :: diffs

    // (3) the reference evaluator, before the change
    let spec0 = propSpec base0 deps
    let spec1 = propSpec base1 deps
    let prodPrior = Propagation.eval (propProdEvaluator spec0 (ResizeArray())) deps
    let modelPrior = ModelProp.eval (propModelEvaluator spec0) mreads mdeps mtopo

    if prodOutcomeRender prodPrior <> modelOutcomeRender modelPrior then
        diffs <-
            sprintf
                "%s: eval production=%s model=%s"
                where
                (prodOutcomeRender prodPrior)
                (modelOutcomeRender modelPrior)
            :: diffs

    let mutable reused = 0
    let mutable absentRecomputed = 0
    let mutable agreed = 0
    let mutable nodeFailed = 0
    let mutable unknownRefused = 0

    match prodPrior with
    | Error _ -> diffs <- sprintf "%s: the prior eval failed, which base0 never asks for" where :: diffs
    | Ok prior ->
        // one trial in five hands `evalFrom` a prior with a HOLE in it: an id absent from
        // `prior` is recomputed whether or not it is dirty.
        let hole =
            if draw 5 = 0 && not topo.Order.IsEmpty then
                Some(List.item (draw topo.Order.Length) topo.Order)
            else
                None

        let priorValues =
            match hole with
            | Some h -> Map.remove h prior.Values
            | None -> prior.Values

        // (4) evalFrom: its result AND the ids it evaluates, in order
        let prodInvoked = ResizeArray()

        let prodIncr =
            Propagation.evalFrom (propProdEvaluator spec1 prodInvoked) priorValues changed deps

        let modelIncr =
            ModelProp.eval_from
                (propModelEvaluator spec1)
                mreads
                (Map.toList priorValues)
                (Set.toList changed)
                mdeps
                mtopo

        if prodOutcomeRender prodIncr <> modelOutcomeRender modelIncr then
            diffs <-
                sprintf
                    "%s: evalFrom production=%s model=%s"
                    where
                    (prodOutcomeRender prodIncr)
                    (modelOutcomeRender modelIncr)
                :: diffs

        let modelInvoked =
            ModelProp.walk_invoked
                (propModelEvaluator spec1)
                mreads
                (Map.toList priorValues)
                (Set.toList changed)
                mdeps
                mtopo

        if List.ofSeq prodInvoked <> modelInvoked then
            diffs <-
                sprintf "%s: evalFrom EVALUATED production=%A model=%A" where (List.ofSeq prodInvoked) modelInvoked
                :: diffs

        // (5) the full evaluator after the change, and the theorem on the shipped driver:
        // under its premises (a local evaluator, a complete change set, the prior eval's own
        // values or fewer, every changed id known) evalFrom IS eval.
        let prodFull = Propagation.eval (propProdEvaluator spec1 (ResizeArray())) deps

        if
            prodOutcomeRender prodFull
            <> modelOutcomeRender (ModelProp.eval (propModelEvaluator spec1) mreads mdeps mtopo)
        then
            diffs <- sprintf "%s: eval after the change disagrees" where :: diffs

        match prodIncr with
        | Error(Propagation.EvalUnknownChange _) ->
            unknownRefused <- unknownRefused + 1

            if prodInvoked.Count <> 0 then
                diffs <-
                    sprintf "%s: an unknown change EVALUATED %A" where (List.ofSeq prodInvoked)
                    :: diffs
        | _ ->
            if prodIncr <> prodFull then
                diffs <-
                    sprintf
                        "%s: evalFrom %s IS NOT eval %s on the shipped driver"
                        where
                        (prodOutcomeRender prodIncr)
                        (prodOutcomeRender prodFull)
                    :: diffs
            else
                agreed <- agreed + 1

            match prodIncr with
            | Error _ -> nodeFailed <- nodeFailed + 1
            | Ok _ ->
                let invokedSet = Set.ofSeq prodInvoked

                reused <-
                    reused
                    + (topo.Order
                       |> List.filter (fun n -> not (Set.contains n invokedSet))
                       |> List.length)

                match hole with
                | Some h when not (Set.contains h prodDirty) && Set.contains h invokedSet ->
                    absentRecomputed <- absentRecomputed + 1
                | _ -> ()

    { PDiffs = acc.PDiffs @ List.rev diffs
      Graphs = acc.Graphs + 1
      CyclicGraphs = acc.CyclicGraphs + (if topo.Cycles.IsEmpty then 0 else 1)
      DanglingGraphs =
        acc.DanglingGraphs
        + (if deps |> Map.exists (fun _ rs -> Set.contains "zz" rs) then
               1
           else
               0)
      OrdersDistinct = acc.OrdersDistinct + (if orderDistinct then 1 else 0)
      DirtyNodes =
        acc.DirtyNodes
        + (ids |> List.filter (fun n -> Set.contains n prodDirty) |> List.length)
      CleanNodes =
        acc.CleanNodes
        + (ids |> List.filter (fun n -> not (Set.contains n prodDirty)) |> List.length)
      Reused = acc.Reused + reused
      AbsentRecomputed = acc.AbsentRecomputed + absentRecomputed
      Agreed = acc.Agreed + agreed
      NodeFailed = acc.NodeFailed + nodeFailed
      UnknownRefused = acc.UnknownRefused + unknownRefused },
    r

let private propDifferential
    (bridge: Map<string, Set<string>> -> ModelProp.dmap)
    (seed: int)
    (trials: int)
    : PropTally =
    let mutable rng = ConfRng.ofSeed seed

    let mutable tally =
        { PDiffs = []
          Graphs = 0
          CyclicGraphs = 0
          DanglingGraphs = 0
          OrdersDistinct = 0
          DirtyNodes = 0
          CleanNodes = 0
          Reused = 0
          AbsentRecomputed = 0
          Agreed = 0
          NodeFailed = 0
          UnknownRefused = 0 }

    for i in 1..trials do
        let t, r' = propProbe bridge i tally rng
        tally <- t
        rng <- r'

    tally


// ------------------------------------------------------------------------------------------
// Phase 187 — the DATA-ACQUISITION SEAM. `proofs/Query.fst` models `Fuaran.Core.Query`'s
// `validateParams` / `invoke` / `invocationKey` and `QueryRegistry.register` / `enumerate` /
// `tryFind` / `dispatch` clause for clause, over an abstract resolver and a `renderers` record;
// `proofs/oracle/Query.fs` is that model extracted. This runs it BESIDE production over the
// generator `Conformance.queryLaws` uses (`ConfRng`), WIDENED from that family's one fixed
// declaration: registries drawn from a three-id pool (so a duplicate registration arises),
// declarations of up to three params over all six column types with names drawn WITH
// replacement (so a repeated param name arises, and first-wins is compared), and argument sets
// that bind, skip, null, mistype, repeat and reverse — with an INSTRUMENTED resolver on each side
// that must have run on both or on neither, and never past a refusal that is not its own.
//
// The model's result payload is abstract, so it is instantiated at production's own
// `QueryResult` and a settled page crosses untranslated. The four renderers are production's own
// calls: `string` on an int, `Canon.canonicalFloat`, `Hash.fnv1a`, and the ordinal order F#'s
// `List.sortBy fst` compares strings by. A float cell crosses as its round-trip `R` text.
// ------------------------------------------------------------------------------------------

let private qColToModel (t: ColumnType) : ModelQuery.column_type =
    match t with
    | IntType -> ModelQuery.IntType
    | FloatType -> ModelQuery.FloatType
    | BoolType -> ModelQuery.BoolType
    | StringType -> ModelQuery.StringType
    | DateType -> ModelQuery.DateType
    | TimestampType -> ModelQuery.TimestampType

let private qColTag (t: ColumnType) : string =
    match t with
    | IntType -> "int"
    | FloatType -> "float"
    | BoolType -> "bool"
    | StringType -> "string"
    | DateType -> "date"
    | TimestampType -> "timestamp"

let private qModelColTag (t: ModelQuery.column_type) : string =
    match t with
    | ModelQuery.IntType -> "int"
    | ModelQuery.FloatType -> "float"
    | ModelQuery.BoolType -> "bool"
    | ModelQuery.StringType -> "string"
    | ModelQuery.DateType -> "date"
    | ModelQuery.TimestampType -> "timestamp"

let private qCellToModel (c: Cell) : ModelQuery.cell =
    match c with
    | Cell.Int v -> ModelQuery.Int(bigint v)
    | Cell.Float v -> ModelQuery.Float(v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
    | Cell.Bool v -> ModelQuery.Bool v
    | Cell.Str v -> ModelQuery.Str v
    | Cell.Date v -> ModelQuery.Date v
    | Cell.Timestamp v -> ModelQuery.Timestamp v
    | Cell.Null -> ModelQuery.Null

let private qArgsToModel (args: (string * Cell) list) : ModelQuery.arguments =
    args |> List.map (fun (n, c) -> n, qCellToModel c)

let private qHostToModel (h: HostEffect) : ModelQuery.host_effect =
    match h with
    | Pure -> ModelQuery.Pure
    | ReadsHost -> ModelQuery.ReadsHost
    | WritesHost -> ModelQuery.WritesHost

let private qDetToModel (d: DeterminismSource) : ModelQuery.determinism_source =
    match d with
    | Deterministic -> ModelQuery.Deterministic
    | Clock -> ModelQuery.Clock
    | Random -> ModelQuery.Random
    | Network -> ModelQuery.Network

/// A declaration as the model reads it. `keepRequired = false` is the FORGETFUL bridge the
/// go-red uses: every param crosses as optional, so the model stops seeing step 2.
let private queryToModelWith (keepRequired: bool) (q: Query) : ModelQuery.query =
    { ModelQuery.query.q_id = q.Id
      ModelQuery.query.q_params =
        q.Params
        |> List.map (fun p ->
            { ModelQuery.query_param.p_name = p.Name
              ModelQuery.query_param.p_type = qColToModel p.Type
              ModelQuery.query_param.p_required = keepRequired && p.Required })
      ModelQuery.query.q_schema = q.ResultSchema |> List.map (fun (n, t) -> n, qColToModel t)
      ModelQuery.query.q_effect =
        { ModelQuery.effect_class.host = qHostToModel q.Effect.Host
          ModelQuery.effect_class.determinism = qDetToModel q.Effect.Determinism }
      ModelQuery.query.q_source = sprintf "%A" q.Source
      ModelQuery.query.q_timeout_ms = toMOpt (Option.map (fun (n: int) -> bigint n) q.TimeoutMs)
      ModelQuery.query.q_page_size = toMOpt (Option.map (fun (n: int) -> bigint n) q.PageSize) }

let private queryToModel (q: Query) : ModelQuery.query = queryToModelWith true q

/// Production's own four calls. `compare` on two F# strings is ordinal, which is what
/// `List.sortBy fst` sorts a name by.
let private queryRenderers: ModelQuery.renderers =
    { ModelQuery.renderers.render_int = fun (n: bigint) -> string (int n)
      ModelQuery.renderers.render_float =
        fun (s: string) ->
            Canon.canonicalFloat (System.Double.Parse(s, System.Globalization.CultureInfo.InvariantCulture))
      ModelQuery.renderers.hash = Hash.fnv1a
      ModelQuery.renderers.name_le = fun (a: string) (b: string) -> System.String.CompareOrdinal(a, b) <= 0
      ModelQuery.renderers.null_key = "∅" }

/// The ORDER-BLIND comparator the second go-red uses: everything is below everything, so the
/// model's insertion sort leaves the caller's order standing.
let private orderBlindRenderers: ModelQuery.renderers =
    { queryRenderers with
        name_le = fun (_: string) (_: string) -> true }

let private prodQueryErrRender (e: QueryError) : string =
    match e with
    | NoSuchQuery(id, known) -> sprintf "NoSuchQuery(%s;%s)" id (String.concat "," (List.sort known))
    | DuplicateQuery id -> sprintf "DuplicateQuery(%s)" id
    | UnknownParam(name, declared) -> sprintf "UnknownParam(%s;%s)" name (String.concat "," declared)
    | ParamTypeMismatch(name, expected, got) ->
        sprintf "ParamTypeMismatch(%s;%s;%s)" name (qColTag expected) (qColTag got)
    | RequiredParamsUnbound names -> sprintf "RequiredParamsUnbound(%s)" (String.concat "," names)
    | SourceNotResolved r -> sprintf "SourceNotResolved(%s)" r
    | ExecutionFailed(detail, recoverable) -> sprintf "ExecutionFailed(%s;%s)" detail (String.concat "," recoverable)
    | Timeout -> "Timeout"

let private modelQueryErrRender (e: ModelQuery.query_error) : string =
    match e with
    | ModelQuery.NoSuchQuery(id, known) -> sprintf "NoSuchQuery(%s;%s)" id (String.concat "," (List.sort known))
    | ModelQuery.DuplicateQuery id -> sprintf "DuplicateQuery(%s)" id
    | ModelQuery.UnknownParam(name, declared) -> sprintf "UnknownParam(%s;%s)" name (String.concat "," declared)
    | ModelQuery.ParamTypeMismatch(name, expected, got) ->
        sprintf "ParamTypeMismatch(%s;%s;%s)" name (qModelColTag expected) (qModelColTag got)
    | ModelQuery.RequiredParamsUnbound names -> sprintf "RequiredParamsUnbound(%s)" (String.concat "," names)
    | ModelQuery.SourceNotResolved r -> sprintf "SourceNotResolved(%s)" r
    | ModelQuery.ExecutionFailed(detail, recoverable) ->
        sprintf "ExecutionFailed(%s;%s)" detail (String.concat "," recoverable)
    | ModelQuery.Timeout -> "Timeout"

let private queryErrClass (e: QueryError) : string =
    match e with
    | NoSuchQuery _ -> "NoSuchQuery"
    | DuplicateQuery _ -> "DuplicateQuery"
    | UnknownParam _ -> "UnknownParam"
    | ParamTypeMismatch _ -> "ParamTypeMismatch"
    | RequiredParamsUnbound _ -> "RequiredParamsUnbound"
    | SourceNotResolved _ -> "SourceNotResolved"
    | ExecutionFailed _ -> "ExecutionFailed"
    | Timeout -> "Timeout"

let private prodQueryDeferredRender (d: Deferred<QueryResult>) : string =
    match d with
    | Pending -> "Pending"
    | Ready r -> sprintf "Ready(page %d)" r.PageNum
    | Failed m -> sprintf "Failed(%s)" m

let private modelQueryDeferredRender (d: ModelQuery.deferred<QueryResult>) : string =
    match d with
    | ModelQuery.Pending -> "Pending"
    | ModelQuery.Ready r -> sprintf "Ready(page %d)" r.PageNum
    | ModelQuery.Failed m -> sprintf "Failed(%s)" m

type private QueryTally =
    { QDiffs: string list
      QRegistered: int
      QDupRefused: int
      QSettled: int
      QStillPending: int
      QNoSuch: int
      QValidated: int
      QRefused: int
      QExecFailed: int
      QResolverRan: int
      QRefusedWithoutResolver: int
      QKeys: int
      QKeysWithNull: int
      QKeysPermuted: int
      QRepeatedParamDecls: int
      QClasses: Set<string> }

let private queryIdPool = [ "q-a"; "q-b"; "q-c" ]
let private queryParamNamePool = [ "p0"; "p1"; "p2"; "when" ]

let private queryTypePool =
    [ IntType; FloatType; BoolType; StringType; DateType; TimestampType ]

/// A present cell of the given type, from a small pool that reaches the key's edges: a negative
/// and an extreme int, a negative zero and an exponent-form float, a string that SPELLS a binding
/// (the shape `key_collision` reads off), an empty string, and the null marker as a string.
let private genCellOf (t: ColumnType) (r: ConfRng.T) : Cell * ConfRng.T =
    match t with
    | IntType ->
        let v, r1 = ConfRng.choose [ -3; 0; 42; System.Int32.MaxValue ] r
        Cell.Int v, r1
    | FloatType ->
        let v, r1 = ConfRng.choose [ 0.0; -0.0; 1.5; 0.1; 1e21; -2.5e-7 ] r
        Cell.Float v, r1
    | BoolType ->
        let v, r1 = ConfRng.choose [ true; false ] r
        Cell.Bool v, r1
    | StringType ->
        let v, r1 = ConfRng.choose [ ""; "x"; "1b=s2"; "p1=i42"; "∅" ] r
        Cell.Str v, r1
    | DateType ->
        let v, r1 = ConfRng.choose [ "2026-09-20"; "1970-01-01" ] r
        Cell.Date v, r1
    | TimestampType ->
        let v, r1 = ConfRng.choose [ "2026-09-20T00:00:00Z"; "1970-01-01T00:00:00Z" ] r
        Cell.Timestamp v, r1

/// A declaration: up to three params, names drawn WITH replacement, any type, either
/// requiredness; every determinism source reached.
let private genQueryDecl (id: string) (r: ConfRng.T) : Query * ConfRng.T =
    let mutable rng = r
    let n, r1 = ConfRng.intBelow 4 rng
    rng <- r1
    let ps = System.Collections.Generic.List<QueryParam>()

    for _ in 1..n do
        let name, r2 = ConfRng.choose queryParamNamePool rng
        let ty, r3 = ConfRng.choose queryTypePool r2
        let req, r4 = ConfRng.intBelow 2 r3
        rng <- r4

        ps.Add(
            { Name = name
              Type = ty
              Required = (req = 0) }
        )

    let det, r5 = ConfRng.choose [ Deterministic; Clock; Random; Network ] rng
    let page, r6 = ConfRng.intBelow 3 r5
    rng <- r6

    { Id = id
      Params = List.ofSeq ps
      ResultSchema = [ "n", IntType ]
      Effect = { Host = ReadsHost; Determinism = det }
      Source = Ref("src-" + id)
      TimeoutMs = (if page = 0 then None else Some(1000 * page))
      PageSize = (if page = 2 then Some 50 else None) },
    rng

/// An argument set against a declaration: per param, three draws in four a binding — one in six
/// of those a `Null`, one in six a cell of ANOTHER type, the rest in type — one set in five with
/// an undeclared name, one in eight with a binding REPEATED under its name, and one in two
/// reversed, so the name sort is exercised against the caller's order.
let private genQueryArgs (q: Query) (r: ConfRng.T) : (string * Cell) list * ConfRng.T =
    let mutable rng = r
    let args = System.Collections.Generic.List<string * Cell>()

    for p in q.Params do
        let roll, r1 = ConfRng.intBelow 4 rng
        rng <- r1

        if roll < 3 then
            let shape, r2 = ConfRng.intBelow 6 rng
            rng <- r2

            if shape = 0 then
                args.Add(p.Name, Cell.Null)
            elif shape = 1 then
                let other, r3 =
                    ConfRng.choose (queryTypePool |> List.filter (fun t -> t <> p.Type)) rng

                let c, r4 = genCellOf other r3
                rng <- r4
                args.Add(p.Name, c)
            else
                let c, r3 = genCellOf p.Type rng
                rng <- r3
                args.Add(p.Name, c)

    let extra, r5 = ConfRng.intBelow 5 rng
    rng <- r5

    if extra = 0 then
        args.Add("zz", Cell.Int 1)

    let repeat, r6 = ConfRng.intBelow 8 rng
    rng <- r6

    if repeat = 0 && args.Count > 0 then
        let n, _ = args[0]
        let c, r7 = genCellOf StringType rng
        rng <- r7
        args.Add(n, c)

    let flip, r8 = ConfRng.intBelow 2 rng
    rng <- r8
    let drawn = List.ofSeq args
    (if flip = 0 then List.rev drawn else drawn), rng

let private queryResultOf (page: int) : QueryResult =
    { Rows =
        { Schema = [ "n", IntType ]
          Columns =
            [ { Name = "n"
                Type = IntType
                Cells = [ Cell.Int page ] } ] }
      PageNum = page
      TotalRowCount = None
      NextPageToken = None }

let private queryProbe
    (bridge: Query -> ModelQuery.query)
    (rn: ModelQuery.renderers)
    (seedTag: int)
    (acc: QueryTally)
    (r: ConfRng.T)
    : QueryTally * ConfRng.T =
    let diffs = System.Collections.Generic.List<string>()
    let mutable rng = r

    // build a registry on both sides, in the same order, with duplicates possible
    let n, r1 = ConfRng.intBelow 4 rng
    rng <- r1
    let mutable preg = QueryRegistry.empty
    let mutable mreg = ModelQuery.empty
    let mutable registered = 0
    let mutable dupRefused = 0
    let mutable repeatedDecls = 0

    for _ in 1..n do
        let id, r2 = ConfRng.choose queryIdPool rng
        let q, r3 = genQueryDecl id r2
        rng <- r3

        if (q.Params |> List.map (fun p -> p.Name) |> List.distinct |> List.length) < List.length q.Params then
            repeatedDecls <- repeatedDecls + 1

        match QueryRegistry.register q preg, ModelQuery.register (bridge q) mreg with
        | Ok p', ModelQuery.Ok m' ->
            preg <- p'
            mreg <- m'
            registered <- registered + 1
        | Error pe, ModelQuery.Error me ->
            dupRefused <- dupRefused + 1

            if prodQueryErrRender pe <> modelQueryErrRender me then
                diffs.Add(
                    sprintf
                        "seed %d: register refused differently (%s vs %s)"
                        seedTag
                        (prodQueryErrRender pe)
                        (modelQueryErrRender me)
                )
        | _ -> diffs.Add(sprintf "seed %d: register verdict differs on %s" seedTag id)

    // enumerate + tryFind agree (membership, and the entries themselves, sorted by id)
    let penum = QueryRegistry.enumerate preg |> List.map bridge
    let menum = ModelQuery.enumerate mreg |> List.sortBy (fun q -> q.q_id)

    if penum <> menum then
        diffs.Add(sprintf "seed %d: enumerate differs" seedTag)

    for id in "q-x" :: queryIdPool do
        let p = QueryRegistry.tryFind id preg |> Option.map bridge
        let m = ofMOpt (ModelQuery.try_find_query id mreg)

        if p <> m then
            diffs.Add(sprintf "seed %d: tryFind %s differs" seedTag id)

    let mutable settled = 0
    let mutable stillPending = 0
    let mutable noSuch = 0
    let mutable validated = 0
    let mutable refused = 0
    let mutable execFailed = 0
    let mutable resolverRan = 0
    let mutable refusedWithoutResolver = 0
    let mutable keys = 0
    let mutable keysWithNull = 0
    let mutable keysPermuted = 0
    let mutable classes = acc.QClasses
    let k, r4 = ConfRng.intBelow 4 rng
    rng <- r4

    for _ in 0..k do
        let id, r5 = ConfRng.choose ("q-x" :: queryIdPool) rng
        rng <- r5

        let args, r6 =
            match QueryRegistry.tryFind id preg with
            | Some q -> genQueryArgs q rng
            | None -> [ "p0", Cell.Int 1 ], rng

        let margs = qArgsToModel args

        // the resolver answers in the envelope, so the draw reaches all three of its cases:
        // 0 fails (the typed `ExecutionFailed`), 1 is still pending, 2 and 3 settle.
        let answer, r7 = ConfRng.intBelow 4 r6
        rng <- r7
        let pRan = ref false
        let mRan = ref false

        let pResolve (_: Query) =
            pRan.Value <- true

            match answer with
            | 0 -> Failed "unreachable source"
            | 1 -> Pending
            | _ -> Ready(queryResultOf answer)

        let mResolve (_: ModelQuery.query) =
            mRan.Value <- true

            match answer with
            | 0 -> ModelQuery.Failed "unreachable source"
            | 1 -> ModelQuery.Pending
            | _ -> ModelQuery.Ready(queryResultOf answer)

        let p = QueryRegistry.dispatch preg id args pResolve
        let m = ModelQuery.dispatch mreg id margs mResolve

        (match p, m with
         | Ok pv, ModelQuery.Ok mv ->
             (match pv with
              | Pending -> stillPending <- stillPending + 1
              | _ -> settled <- settled + 1)

             if prodQueryDeferredRender pv <> modelQueryDeferredRender mv then
                 diffs.Add(
                     sprintf
                         "seed %d: dispatch %s accepted with different envelopes\n  prod %s\n  model %s"
                         seedTag
                         id
                         (prodQueryDeferredRender pv)
                         (modelQueryDeferredRender mv)
                 )

             // and neither side ever hands an `Ok(Failed _)` out of the seam
             // (`invoke_never_ok_failed` / `dispatch_never_ok_failed`, sampled on the shipped one).
             match pv, mv with
             | Failed _, _
             | _, ModelQuery.Failed _ ->
                 diffs.Add(sprintf "seed %d: dispatch %s accepted with an Ok(Failed _) envelope" seedTag id)
             | _ -> ()
         | Error pe, ModelQuery.Error me ->
             classes <- Set.add (queryErrClass pe) classes

             if prodQueryErrRender pe <> modelQueryErrRender me then
                 diffs.Add(
                     sprintf
                         "seed %d: dispatch %s refused differently\n  prod %s\n  model %s"
                         seedTag
                         id
                         (prodQueryErrRender pe)
                         (modelQueryErrRender me)
                 )

             match pe with
             | NoSuchQuery _ -> noSuch <- noSuch + 1
             | ExecutionFailed _ -> execFailed <- execFailed + 1
             | _ -> refused <- refused + 1
         | Ok _, ModelQuery.Error me ->
             diffs.Add(
                 sprintf
                     "seed %d: dispatch %s accepted by production, refused by the model (%s)"
                     seedTag
                     id
                     (modelQueryErrRender me)
             )
         | Error pe, ModelQuery.Ok _ ->
             diffs.Add(
                 sprintf
                     "seed %d: dispatch %s refused by production (%s), accepted by the model"
                     seedTag
                     id
                     (prodQueryErrRender pe)
             ))

        // the resolver ran on both sides or on neither — and never past a refusal that is not its own
        if pRan.Value <> mRan.Value then
            diffs.Add(
                sprintf
                    "seed %d: dispatch %s ran the resolver on one side only (prod %b, model %b)"
                    seedTag
                    id
                    pRan.Value
                    mRan.Value
            )

        if pRan.Value then
            resolverRan <- resolverRan + 1

        (match p with
         | Error(ExecutionFailed _)
         | Ok _ -> ()
         | Error _ ->
             refusedWithoutResolver <- refusedWithoutResolver + 1

             if pRan.Value then
                 diffs.Add(sprintf "seed %d: dispatch %s was REFUSED and the resolver still ran" seedTag id))

        // validateParams, the capture key and the determinism tag on their own, on the resolved
        // declaration — the key for EVERY argument set, accepted or not: `invocationKey` is total
        // and asks nothing of validation.
        match QueryRegistry.tryFind id preg with
        | Some q ->
            let mq = bridge q

            (match Query.validateParams q args, ModelQuery.validate_params mq margs with
             | Ok(), ModelQuery.Ok() -> validated <- validated + 1
             | Error pe, ModelQuery.Error me ->
                 if prodQueryErrRender pe <> modelQueryErrRender me then
                     diffs.Add(
                         sprintf
                             "seed %d: validateParams refused differently\n  prod %s\n  model %s"
                             seedTag
                             (prodQueryErrRender pe)
                             (modelQueryErrRender me)
                     )
             | _ -> diffs.Add(sprintf "seed %d: validateParams verdict differs on %s" seedTag id))

            let pKey = Query.invocationKey q args
            let mKey = ModelQuery.invocation_key rn mq margs
            keys <- keys + 1

            if args |> List.exists (fun (_, c) -> c = Cell.Null) then
                keysWithNull <- keysWithNull + 1

            if pKey <> mKey then
                diffs.Add(
                    sprintf "seed %d: invocationKey differs on %s %A\n  prod %s\n  model %s" seedTag id args pKey mKey
                )

            // `invocation_key_deterministic`, sampled on the shipped seam: with distinct names the
            // caller's order does not reach the key.
            let names = args |> List.map fst

            if List.length names > 1 && List.length (List.distinct names) = List.length names then
                keysPermuted <- keysPermuted + 1

                if Query.invocationKey q (List.rev args) <> pKey then
                    diffs.Add(sprintf "seed %d: the shipped invocationKey moved under a reordering of %A" seedTag args)

            if Query.determinismTag q <> ModelQuery.determinism_tag_of mq then
                diffs.Add(sprintf "seed %d: determinismTag differs on %s" seedTag id)
        | None -> ()

    { QDiffs = acc.QDiffs @ List.ofSeq diffs
      QRegistered = acc.QRegistered + registered
      QDupRefused = acc.QDupRefused + dupRefused
      QSettled = acc.QSettled + settled
      QStillPending = acc.QStillPending + stillPending
      QNoSuch = acc.QNoSuch + noSuch
      QValidated = acc.QValidated + validated
      QRefused = acc.QRefused + refused
      QExecFailed = acc.QExecFailed + execFailed
      QResolverRan = acc.QResolverRan + resolverRan
      QRefusedWithoutResolver = acc.QRefusedWithoutResolver + refusedWithoutResolver
      QKeys = acc.QKeys + keys
      QKeysWithNull = acc.QKeysWithNull + keysWithNull
      QKeysPermuted = acc.QKeysPermuted + keysPermuted
      QRepeatedParamDecls = acc.QRepeatedParamDecls + repeatedDecls
      QClasses = classes },
    rng

let private queryDifferential
    (bridge: Query -> ModelQuery.query)
    (rn: ModelQuery.renderers)
    (seed: int)
    (trials: int)
    : QueryTally =
    let mutable rng = ConfRng.ofSeed seed

    let mutable tally =
        { QDiffs = []
          QRegistered = 0
          QDupRefused = 0
          QSettled = 0
          QStillPending = 0
          QNoSuch = 0
          QValidated = 0
          QRefused = 0
          QExecFailed = 0
          QResolverRan = 0
          QRefusedWithoutResolver = 0
          QKeys = 0
          QKeysWithNull = 0
          QKeysPermuted = 0
          QRepeatedParamDecls = 0
          QClasses = Set.empty }

    for i in 1..trials do
        let t, r' = queryProbe bridge rn i tally rng
        tally <- t
        rng <- r'

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
              | SiblingCorpus.NotAsked why -> skiptest why
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
              | SiblingCorpus.NotAsked why -> skiptest why
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
              | SiblingCorpus.NotAsked why -> skiptest why
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
              | SiblingCorpus.NotAsked why -> skiptest why
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
              | SiblingCorpus.NotAsked why -> skiptest why
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

          // ---- Phase 156: the ID-ORDERED DRAIN beside production's own topological order ----

          testCase "the extracted drain is production's topological order over every lane head"
          <| fun _ ->
              // The SPINE case: one head's closure is a chain, the frontier is one element wide at
              // every step, and Phase 142 already proves the two orders coincide there. It runs
              // because a green multi-head case means little if the easy case is broken.
              let mutable rng = ConfRng.ofSeed 1560
              let mutable heads = 0

              for i in 1..80 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes

                  for h in hs do
                      heads <- heads + 1

                      match drainDisagreements ordLt dag h with
                      | [], _ -> ()
                      | d :: _, _ -> failtestf "iter %d\n%s\n%s" i (renderLanes encPlanOp lanes) d

                  ignore baseId

              Expect.isGreaterThan heads 0 "some lane heads were drained"

          testCase "the extracted drain is production's topological order over a MULTI-HEAD UNION"
          <| fun _ ->
              // The case Phase 142 does not cover and section 13 exists for: the lane heads folded
              // into one convergent head with `Dag.merge`, so the union head's closure is every
              // node and the ready frontier is N wide once the base is drained. The adequacy guard
              // is the frontier WIDTH — a run whose frontier never passed one has re-measured the
              // spine case under a different name.
              let mutable rng = ConfRng.ofSeed 1561
              let mutable widest = 0
              let mutable unions = 0

              for i in 1..80 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag
                  unions <- unions + 1

                  match drainDisagreements ordLt merged unionHead with
                  | [], w -> widest <- max widest w
                  | d :: _, _ -> failtestf "iter %d\n%s\n%s" i (renderLanes encPlanOp lanes) d

              for i in 1..40 do
                  let lanes, r' = treeLaneGen.Lanes 4 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag treeW treeLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion treeW treeLaneGen.BaseOp baseId hs dag
                  unions <- unions + 1

                  match drainDisagreements ordLt merged unionHead with
                  | [], w -> widest <- max widest w
                  | d :: _, _ -> failtestf "reference witness iter %d\n%s\n%s" i (renderLanes treeW.Encode lanes) d

              Expect.isGreaterThan unions 0 "some unions were drained"

              Expect.isGreaterThan
                  widest
                  1
                  (sprintf
                      "the ready frontier never held more than one node (widest=%d), so the tie-break was never exercised and this case re-measured the spine"
                      widest)

          testCase "a model draining LARGEST-id-first loses over the same unions — the measurement can fail"
          <| fun _ ->
              // The teeth. The reversed order is as legitimate a selector as the model's own — it
              // returns a member of the frontier it is handed, which is all `picks_from_frontier`
              // asks — so a green run above certifies nothing unless this one is red. It is
              // deliberately run over the UNIONS and not the lane heads: on a spine the frontier
              // holds one id, whose minimum and maximum are the same id, and no tie-break can lose.
              let mutable rng = ConfRng.ofSeed 1561
              let mutable found = 0
              let mutable example = ""

              for _ in 1..80 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag

                  match drainDisagreements ordGt merged unionHead with
                  | [], _ -> ()
                  | d :: _, _ ->
                      found <- found + 1

                      if example = "" then
                          example <- d

              Expect.isGreaterThan
                  found
                  0
                  "a drain taking the LARGEST ready id must disagree with production — this comparison cannot lose"

              Expect.stringContains example "the drain differs" "the disagreement names what moved"

          testCase "the drain is a function of the node SET — every arrival order gives one sequence"
          <| fun _ ->
              // `drain_deterministic`, measured. `DagFold.frontier` answers in the WORK LIST's
              // order, so permuting the node set hands the selector a permuted frontier; only an
              // order-invariant selector survives that, and this is the arm that would catch a
              // drain that took the head of an unsorted frontier instead of its minimum.
              let mutable rng = ConfRng.ofSeed 1562
              let mutable compared = 0

              for i in 1..60 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag
                  let ns = modelClosure merged unionHead
                  let canonical = DagFold.drain DagFold.IgnoreDangling ordLt (drainFuel ns) ns

                  // Three permutations, two of them fixed so the case cannot go quiet on a seed
                  // that happened to shuffle nothing, and one drawn.
                  let shuffled, r'' = ConfRng.shuffle ns rng
                  rng <- r''

                  for perm in
                      [ List.rev ns
                        (match ns with
                         | [] -> []
                         | h :: t -> t @ [ h ])
                        shuffled ] do
                      compared <- compared + 1
                      let got = DagFold.drain DagFold.IgnoreDangling ordLt (drainFuel perm) perm

                      if got <> canonical then
                          failtestf
                              "iter %d: a permuted node set drained differently\n%s\n  canonical: %A\n  permuted:  %A"
                              i
                              (renderLanes encPlanOp lanes)
                              canonical
                              got

              Expect.isGreaterThan compared 0 "some permutations were drained"

          testCase "the two dangling-parent policies are the two production call sites, and differ only on a dangler"
          <| fun _ ->
              // `drain_policies_agree` and `drain_refusal_characterised`, measured against the two
              // callers they model. On a closure — which is parent-closed, so no parent lies
              // outside — the policies agree and `Dag.verifyDag` is happy. Drop the BASE node from
              // the set and every lane's first node names a parent outside it: `RefuseDangling`
              // refuses, exactly as `Dag.firstBreak` reports `MissingParent`, while
              // `IgnoreDangling` still drains every remaining node, exactly as `Dag.topoCore`'s
              // `parentsIn` filter makes it.
              let mutable rng = ConfRng.ofSeed 1563
              let mutable refusals = 0
              let mutable agreements = 0

              for i in 1..60 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag
                  let ns = modelClosure merged unionHead

                  Expect.isTrue
                      (Dag.verifyDag OpStream.defaultHash planW merged)
                      "the production DAG holds every parent it names"

                  let ignored = DagFold.drain DagFold.IgnoreDangling ordLt (drainFuel ns) ns
                  let refused = DagFold.drain DagFold.RefuseDangling ordLt (drainFuel ns) ns

                  if ignored <> refused then
                      failtestf "iter %d: the policies differ on a closure with no dangling parent" i

                  agreements <- agreements + 1

                  // … and now WITH one: the base node dropped, so every lane's first node names it
                  // from outside the set.
                  let orphaned = ns |> List.filter (fun n -> n.nid <> baseId)

                  match DagFold.drain DagFold.RefuseDangling ordLt (drainFuel orphaned) orphaned with
                  | DagFold.Refused id ->
                      refusals <- refusals + 1

                      Expect.isTrue
                          (orphaned |> List.exists (fun n -> n.nid = id && List.contains baseId n.nparents))
                          "the refusal names a node that really does name the dropped base"
                  | DagFold.Drained _ -> failtestf "iter %d: RefuseDangling drained a set missing the base" i

                  match DagFold.drain DagFold.IgnoreDangling ordLt (drainFuel orphaned) orphaned with
                  | DagFold.Drained ord ->
                      Expect.equal
                          (List.sortWith (fun a b -> System.String.CompareOrdinal(a, b)) ord)
                          (orphaned
                           |> List.map (fun n -> n.nid)
                           |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b)))
                          "IgnoreDangling places every remaining node — the dropped parent constrains nothing"
                  | DagFold.Refused _ -> failtestf "iter %d: IgnoreDangling refused" i

              Expect.isGreaterThan agreements 0 "the policies were compared on a clean closure"
              Expect.isGreaterThan refusals 0 "the refusing policy was made to refuse"

          testCase "a CYCLE is surfaced as a SHORT drain — the model and production read the same signal"
          <| fun _ ->
              // `drain_complete_is_acyclic`'s other side, measured. Production's `topoCore` does
              // not raise on a cycle: a node inside one never reaches in-degree zero, so the
              // emitted list is strictly shorter than the closure, and `Dag.isAcyclic` /
              // `Dag.tryTopoOrder` are the callers that read that comparison. A cyclic DAG cannot
              // be built through `Dag.append` (the parent's id is minted before the child's), so
              // it is built by hand here — which is exactly the hand-crafted or tampered JSONL load
              // `Dag.fromJsonl`'s docstring warns about.
              let node id parents : DagNode<PlanOp> =
                  { Id = id
                    Parents = parents
                    Actor = Human "cyclic"
                    Op = planLaneGen.BaseOp }

              let cyclic: Dag.T<PlanOp> =
                  { Nodes =
                      [ node "a" []
                        node "b" [ "a"; "d" ] // b waits on d …
                        node "c" [ "b" ]
                        node "d" [ "c" ] ] // … and d waits on c waits on b
                      |> List.map (fun n -> n.Id, n)
                      |> Map.ofList }

              // Production: the closure is four nodes and only "a" is ever ready.
              Expect.isFalse (Dag.isAcyclic cyclic "d") "production sees the cycle"

              match Dag.tryTopoOrder cyclic "d" with
              | Ok _ -> failtest "production must refuse a cyclic closure through tryTopoOrder"
              | Error e -> Expect.stringContains e "cyclic history" "production names the cycle"

              // The model: the same short drain, from the same nodes.
              let ns = modelClosure cyclic "d"
              Expect.equal (List.length ns) 4 "the closure is the whole hand-built set"

              match DagFold.drain DagFold.IgnoreDangling ordLt (drainFuel ns) ns with
              | DagFold.Drained ord ->
                  Expect.equal ord [ "a" ] "the model drains the acyclic prefix and stops, as topoCore does"

                  Expect.isFalse
                      (DagFold.is_topo_enum ns ord)
                      "and a short drain is not a topological enumeration — which is the signal isAcyclic reads"
              | DagFold.Refused _ -> failtest "the hand-built set holds every parent it names"

              // … and the same set with the back edge removed drains completely and IS one, so the
              // comparison above is a discrimination rather than a refusal of everything.
              let acyclic: Dag.T<PlanOp> =
                  { Nodes =
                      [ node "a" []; node "b" [ "a" ]; node "c" [ "b" ]; node "d" [ "c" ] ]
                      |> List.map (fun n -> n.Id, n)
                      |> Map.ofList }

              let ns' = modelClosure acyclic "d"

              match DagFold.drain DagFold.IgnoreDangling ordLt (drainFuel ns') ns', Dag.tryTopoOrder acyclic "d" with
              | DagFold.Drained ord, Ok production ->
                  Expect.equal ord production "the model and production agree once the back edge is gone"
                  Expect.isTrue (DagFold.is_topo_enum ns' ord) "and the complete drain IS a topological enumeration"
              | m, p -> failtestf "the acyclic control disagreed: model=%A production=%A" m p
          // ---- Phase 158: delta recovery over a MERGED HEAD, and mergeBase, beside production's ----

          testCase "the extracted recovery and mergeBase are production's over every FOLD-PULL-FOLD union"
          <| fun _ ->
              // The shape section 14 is about: round one's lanes folded into one convergent head
              // with `Dag.merge`, round two's lanes appended off it. Three comparisons per union —
              // production's delta, the model's, and the lane that was appended; production's merge
              // base, the model's, and the merged head — plus the model's fuel premise, evaluated.
              // The adequacy guards are the ones a green run could otherwise hide behind: ops were
              // recovered, a chain was walked, a merge base was located, and the head the lanes
              // hang off really had two-parent nodes beneath it.
              let mutable rng = ConfRng.ofSeed 1580
              let mutable recovered = 0
              let mutable chains = 0
              let mutable mergeNodes = 0
              let mutable pairs = 0

              for i in 1..60 do
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 3 r1
                  rng <- r2
                  let t = mergedDisagreements modelMergeBase planW planLaneGen.BaseOp round1 round2

                  match t.Failures with
                  | [] -> ()
                  | d :: _ ->
                      failtestf
                          "work-plan domain iter %d\nround one:\n%s\nround two:\n%s\n%s"
                          i
                          (renderLanes encPlanOp round1)
                          (renderLanes encPlanOp round2)
                          d

                  recovered <- recovered + t.Recovered
                  chains <- chains + t.Chains
                  mergeNodes <- mergeNodes + t.MergeNodes
                  pairs <- pairs + t.Pairs

              for i in 1..30 do
                  let round1, r1 = treeLaneGen.Lanes 4 rng
                  let round2, r2 = treeLaneGen.Lanes 3 r1
                  rng <- r2
                  let t = mergedDisagreements modelMergeBase treeW treeLaneGen.BaseOp round1 round2

                  match t.Failures with
                  | [] -> ()
                  | d :: _ ->
                      failtestf
                          "reference witness iter %d\nround one:\n%s\nround two:\n%s\n%s"
                          i
                          (renderLanes treeW.Encode round1)
                          (renderLanes treeW.Encode round2)
                          d

                  recovered <- recovered + t.Recovered
                  chains <- chains + t.Chains
                  mergeNodes <- mergeNodes + t.MergeNodes
                  pairs <- pairs + t.Pairs

              Expect.isGreaterThan recovered 0 (sprintf "no ops were recovered at all (recovered=%d)" recovered)
              Expect.isGreaterThan chains 0 "no round-two lane was longer than one op, so no chain was walked"
              Expect.isGreaterThan pairs 0 "no pair of round-two heads had its merge base located"

              Expect.isGreaterThan
                  mergeNodes
                  0
                  "no merged head had a two-parent node beneath it, so this case re-measured Phase 134's base-plus-chains shape"

          testCase "the fold FROM a merged head is the deltas-first fold over the round-two lanes"
          <| fun _ ->
              // `reconcile_many_merged_eq`, measured: production's `Dag.reconcileMany` from the
              // merged head, the extracted `reconcile_many_drained` over the same DAG, and the
              // deltas-first `reconcile_many` handed the round-two lanes directly — the theorem's
              // own right-hand side. Scripts are compared as scripts and halts as canonical
              // reports, exactly as the Phase 131 cases compare them.
              let mutable rng = ConfRng.ofSeed 1581
              let mutable folded = 0
              let mutable halted = 0

              for i in 1..60 do
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 3 r1
                  rng <- r2
                  let _, merged, _, heads2, dag = foldPullFold planW planLaneGen.BaseOp round1 round2
                  let model = toModelDag dag

                  let production =
                      match Dag.reconcileMany planFootprint dag merged heads2 with
                      | Ok script -> Ok script
                      | Error cs -> Error(FoldConfluence.canonicalConflictReport planW.Encode cs)

                  let fromDag =
                      match
                          DagFold.reconcile_many_drained
                              (planFootprint >> toModelFootprint)
                              ordLt
                              (drainFuel model.nodes)
                              model
                              model.nodes
                              merged
                              heads2
                      with
                      | DagFold.Ok script -> Ok script
                      | DagFold.Error cs ->
                          Error(FoldConfluence.canonicalConflictReport planW.Encode (cs |> List.map ofModelConflict))

                  let deltasFirst = oracleScript planW planFootprint round2

                  match production with
                  | Ok _ -> folded <- folded + 1
                  | Error _ -> halted <- halted + 1

                  if production <> fromDag || fromDag <> deltasFirst then
                      failtestf
                          "iter %d\nround one:\n%s\nround two:\n%s\n  production:   %A\n  model (DAG):  %A\n  deltas-first: %A"
                          i
                          (renderLanes encPlanOp round1)
                          (renderLanes encPlanOp round2)
                          production
                          fromDag
                          deltasFirst

              Expect.isGreaterThan (folded + halted) 0 "some unions were folded"
              Expect.isGreaterThan folded 0 "no union folded clean, so no merge script was ever compared"

          testCase "a model whose mergeBase returns the BASE rather than the divergence point loses"
          <| fun _ ->
              // The teeth. The locator is the model's own `max_by` over the model's own
              // intersection at the REVERSED key, so it names the SHALLOWEST common ancestor — over
              // a fold-pull-fold union, the original base. If this comes back clean, the green
              // case above certifies nothing: it would be comparing a locator that cannot tell the
              // divergence point from anything else the two heads share. Two things must lose —
              // the merge base itself, and the delta recovered from the base it names, which is
              // the whole of round one as well as the lane.
              let mutable rng = ConfRng.ofSeed 1580
              let mutable found = 0
              let mutable namedTheBase = 0
              let mutable longerDeltas = 0
              let mutable example = ""

              for _ in 1..60 do
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 3 r1
                  rng <- r2
                  let t = mergedDisagreements shallowestCommon planW planLaneGen.BaseOp round1 round2

                  match t.Failures with
                  | [] -> ()
                  | d :: _ ->
                      found <- found + 1

                      if example = "" then
                          example <- d

                  let baseId, merged, _, heads2, dag =
                      foldPullFold planW planLaneGen.BaseOp round1 round2

                  let model = toModelDag dag

                  match heads2 with
                  | h0 :: h1 :: _ ->
                      match shallowestCommon model model.nodes h0 h1 with
                      | Some wrong when wrong = baseId && baseId <> merged ->
                          namedTheBase <- namedTheBase + 1

                          let fromWrong =
                              DagFold.between_ops_drained ordLt (drainFuel model.nodes) model model.nodes wrong h0

                          if fromWrong <> Dag.betweenOps dag merged h0 then
                              longerDeltas <- longerDeltas + 1
                      | _ -> ()
                  | _ -> ()

              Expect.isGreaterThan
                  found
                  0
                  "a locator naming the SHALLOWEST common ancestor must disagree with production — this comparison cannot lose"

              Expect.stringContains example "the merge base differs" "the disagreement names what moved"

              Expect.isGreaterThan
                  namedTheBase
                  0
                  "the reversed locator named the ORIGINAL BASE, which is the go-red the phase asks for"

              Expect.isGreaterThan
                  longerDeltas
                  0
                  "a delta recovered from the base rather than the divergence point must differ from production's"

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
              // alphabet is the non-`Batch` ops, which is the alphabet THAT theorem is stated over
              // — Phase 133's, and this case is Phase 133's evidence, unchanged. Since Phase 162
              // the model also carries `op_independence_diamond` over the whole alphabet, batches
              // included; its evidence is the case below rather than a widening of this one, so
              // that the two alphabets stay separately measured.
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

          // ---- Phase 162 — the same diamond over the WHOLE alphabet, nested batches included ----

          testCase "the extracted model keeps the diamond on BATCH pairs — the shape Phase 133 left open"
          <| fun _ ->
              // `TreeOps.covered` named three pair shapes the Phase 133 diamond could not reach:
              // either side a `Batch` that neither does nothing nor relocates. Phase 162 lifted the
              // diamond along a batch's script, deleted `covered`, and stated
              // `op_independence_diamond` over the whole `SkeletonOp` alphabet. This is that
              // widening measured on the EXTRACTED code, and it is a case of its own rather than a
              // change to the one above, so Phase 133's evidence stays exactly what it was.
              //
              // The pool is built from the generated ops the case above filters out batches from,
              // wrapped three ways: a singleton batch (same footprint as its member, so it inherits
              // that member's independence and guarantees the premise is met at all), a paired
              // batch, and one doubly-nested batch — because "nested to any depth" is the part of
              // the claim a single wrapping would not exercise. Only inserts and reorders are
              // wrapped: a batch carrying a remove or a move RELOCATES, and such a pair was already
              // closed by Phase 133 without any lift.
              let mutable r = ConfRng.ofSeed 1620
              let mutable breaks = []
              let mutable met = 0
              let mutable batchMet = 0

              for _ in 1..20 do
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
                      |> List.map toModelTree

                  let leaves =
                      ops
                      |> List.filter (fun o ->
                          match o with
                          | Batch _ -> false
                          | _ -> true)
                      |> List.map (toModelOpWith toModelTree)

                  let liftable =
                      leaves
                      |> List.filter (fun o ->
                          match o with
                          | TreeOps.InsertChild _
                          | TreeOps.ReorderChildren _ -> true
                          | _ -> false)

                  let batches =
                      (liftable |> List.map (fun o -> TreeOps.Batch [ o ]))
                      @ (liftable |> List.chunkBySize 2 |> List.map TreeOps.Batch)
                      @ (match liftable with
                         | o :: _ -> [ TreeOps.Batch [ TreeOps.Batch [ o ] ] ]
                         | [] -> [])

                  let b, m = modelDiamondBreaks TreeOps.op_fp (leaves @ batches) states
                  breaks <- breaks @ b
                  met <- met + m

                  // The adequacy this family cannot read off `met`: how many of the met pairs
                  // carried a batch at all. Without it the pool could collapse to leaves and the
                  // widening would be measured by nothing while the case still passed. Measured at
                  // 20 trials, seed 1620: met=156 of which batchMet=91. The thresholds are below
                  // both with room and are there to catch a generator that stops producing
                  // independent pairs, not to pin the numbers.
                  for a in batches do
                      for bb in leaves @ batches do
                          if DagFold.independent (TreeOps.op_fp a) (TreeOps.op_fp bb) then
                              for s in states do
                                  match TreeOps.wapply a s, TreeOps.wapply bb s with
                                  | DagFold.Ok _, DagFold.Ok _ -> batchMet <- batchMet + 1
                                  | _ -> ()

              match breaks with
              | why :: _ -> failtest why
              | [] ->
                  Expect.isGreaterThan met 100 (sprintf "the sample met the diamond's premise in earnest (met=%d)" met)

                  Expect.isGreaterThan
                      batchMet
                      0
                      (sprintf
                          "the pool met the premise on pairs carrying a BATCH — the shape Phase 133 left open (batchMet=%d)"
                          batchMet)

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

          testCase "the arbitration oracle agrees with Arbitration.arbitrate over generated proposal sets"
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

              let holders (r: Arbitration<RNode, string>) =
                  r.Accepted |> List.map (fun p -> p.Holder)

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
              | [ (p, Conflicts [ 1 ]) ] -> Expect.equal p.Holder "b" "the loser cites the winner's id"
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
              let ids (r: Arbitration<RNode, string>) = r.Accepted |> List.map (fun p -> p.Id)

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
                  (first.Rejected |> List.map snd)
                  [ Conflicts [ 1 ]; Conflicts [ 1 ] ]
                  "and both rejections are justified — each cites the proposal standing in its way"

              Expect.equal
                  (renderProdArbitration first)
                  (renderModelArbitration (
                      Arbitrate.arbitrate Arbitrate.mx_base [ Arbitrate.mx_1; Arbitrate.mx_2; Arbitrate.mx_3 ]
                  ))
                  "the model agrees with production on the witness"

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
              | SiblingCorpus.NotAsked why -> skiptest why
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

          testCase "… and the absorption IS the erasure — Phase 190's bridge, erase-then-compare"
          <| fun _ ->
              // The theorem, put to production and to the extracted model rather than only to the
              // prover: reading a document under the TOLERANT policy is reading the document with
              // its member nulls DELETED under the STRICT one. Four answers per pair, so a model
              // that erased the wrong thing and a production that erased the wrong thing are each
              // caught, and a shared mistake is caught by the two of them disagreeing with each
              // other in the probes above.
              let bad =
                  [ for (name, absorbed, erased) in JsonParseDiff.erasurePairs do
                        let pa =
                            JsonParseDiff.withoutPosition (JsonParseDiff.prodAnswer EraseMemberNull 512 absorbed)

                        let pe =
                            JsonParseDiff.withoutPosition (JsonParseDiff.prodAnswer RejectNull 512 erased)

                        let ma =
                            JsonParseDiff.withoutPosition (
                                JsonParseDiff.modelAnswer
                                    JsonParseDiff.toChs
                                    EraseMemberNull
                                    512
                                    (JsonParseDiff.budget 512)
                                    absorbed
                            )

                        let me =
                            JsonParseDiff.withoutPosition (
                                JsonParseDiff.modelAnswer
                                    JsonParseDiff.toChs
                                    RejectNull
                                    512
                                    (JsonParseDiff.budget 512)
                                    erased
                            )

                        if pa <> pe then
                            yield
                                sprintf
                                    "%s: PRODUCTION does not bridge\n    tolerant %s -> %s\n    strict   %s -> %s"
                                    name
                                    absorbed
                                    pa
                                    erased
                                    pe

                        if ma <> me then
                            yield
                                sprintf
                                    "%s: the MODEL does not bridge\n    tolerant %s -> %s\n    strict   %s -> %s"
                                    name
                                    absorbed
                                    ma
                                    erased
                                    me

                        if pa <> ma then
                            yield sprintf "%s: the model and production disagree on %s: %s vs %s" name absorbed pa ma ]

              if not bad.IsEmpty then
                  failtestf
                      "erase-then-compare: %d finding(s). First 5:\n%s"
                      bad.Length
                      (bad |> List.truncate 5 |> String.concat "\n")

              // Both halves of the bridge were actually reached, so a green run above is not one
              // that only ever accepted or only ever refused.
              Expect.isTrue
                  (JsonParseDiff.erasurePairs
                   |> List.exists (fun (_, a, _) -> (JsonParseDiff.prodAnswer EraseMemberNull 512 a).StartsWith "ok "))
                  "no pair was ACCEPTED — the value half of the bridge measured nothing"

              Expect.isTrue
                  (JsonParseDiff.erasurePairs
                   |> List.exists (fun (_, a, _) ->
                       not ((JsonParseDiff.prodAnswer EraseMemberNull 512 a).StartsWith "ok ")))
                  "no pair was REFUSED — the classification half of the bridge measured nothing"

              // THE TWO HYPOTHESES, each shown to be load-bearing on production rather than only
              // needed by the prover. `members_follow`: a null member closed by a COMMA leaves a
              // trailing comma behind, which is not the empty object the deletion would give.
              Expect.notEqual
                  (JsonParseDiff.withoutPosition (JsonParseDiff.prodAnswer EraseMemberNull 512 "{\"a\":null,}"))
                  (JsonParseDiff.withoutPosition (JsonParseDiff.prodAnswer RejectNull 512 "{}"))
                  "a trailing comma left by the absorption is NOT the empty object — the hypothesis the theorem carries"

              // And the null carve-out: a null the tolerant policy declines to erase is refused
              // with the SAME kind and a DIFFERENT message, which is why the refusal half of the
              // theorem excludes `NullNotRepresentable` rather than covering it.
              let arrA =
                  JsonParseDiff.withoutPosition (
                      JsonParseDiff.prodAnswer EraseMemberNull 512 "{\"a\":null,\"b\":[null]}"
                  )

              let arrE =
                  JsonParseDiff.withoutPosition (JsonParseDiff.prodAnswer RejectNull 512 "{\"b\":[null]}")

              Expect.stringContains
                  arrA
                  "NullNotRepresentable"
                  "the array null is still refused under the tolerant policy"

              Expect.stringContains arrE "NullNotRepresentable" "and under the strict one"

              Expect.notEqual
                  arrA
                  arrE
                  "the two policies word that refusal differently — the carve-out the theorem's refusal half names"

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

          // ---- Phase 191 — snapshot and bounded replay: compact, replayFrom, verifyAcross ----

          testCase
              "the snapshot oracle agrees with production over the work-plan stream, every boundary and every tamper"
          <| fun _ ->
              snapshotDifferential
                  "work-plan snapshots"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  planW
                  planHash
                  tamperedPlanOp
                  planLaneGen
                  3700
                  40
              |> expectSnapshotAgreement "work-plan snapshots"

          testCase
              "the snapshot oracle agrees with production over the reference witness's stream, every boundary and every tamper"
          <| fun _ ->
              snapshotDifferential
                  "reference snapshots"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  treeW
                  prodTreeHash
                  (fun _ -> RemoveNode "tampered-node")
                  treeLaneGen
                  3710
                  30
              |> expectSnapshotAgreement "reference snapshots"

          testCase "a snapshot model handed a DIFFERENT hash disagrees with production — the comparison can lose"
          <| fun _ ->
              // The go-red for the whole differential: nothing about the streams moves, only the
              // function the model mints the snapshot's hash and walks the tail with.
              let t =
                  snapshotDifferential
                      "perturbed snapshots"
                      OpStream.defaultHash
                      swappedHash
                      planW
                      planHash
                      tamperedPlanOp
                      planLaneGen
                      3720
                      3

              Expect.isSome t.Failure "a model hashing with a different function must be caught"

              // And it is caught where it should be: on one intact stream, production verifies
              // across its own compaction and the perturbed model does not.
              let rs =
                  chainUnder
                      OpStream.defaultHash
                      planW
                      planLaneGen.State0
                      (Human "writer")
                      [ AddItem("s1", "one"); AddItem("s2", "two"); Retitle("s1", "uno") ]

              match OpStream.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs 1 with
              | Error e -> failtestf "compact refused an intact stream: %s" e
              | Ok(snap, tail) ->
                  Expect.isTrue
                      (OpStream.verifyAcross OpStream.defaultHash planHash planW snap tail)
                      "production verifies across its own boundary"

                  Expect.isFalse
                      (Chain.verify_across
                          swappedHash
                          showPos
                          planW.Encode
                          (Chain.snap_payload showPos planHash)
                          (toModelSnapshot snap)
                          (toChainRecords tail))
                      "and a model recomputing with a different hash does not"

          testCase
              "replayFrom renumbers a halt from ZERO — the offset in replay_from_snapshot_eq is production's, not the model's"
          <| fun _ ->
              // The theorem is `replay = offset n (replayFrom snap tail)`, and the offset is the
              // part a reader would drop. Shown load-bearing on production alone: a stream whose
              // third op rejects, compacted after its first.
              let built =
                  chainUnder
                      OpStream.defaultHash
                      planW
                      planLaneGen.State0
                      (Human "writer")
                      [ AddItem("o1", "one"); AddItem("o2", "two"); Retitle("o1", "uno") ]

              let rs =
                  built
                  |> List.mapi (fun i r ->
                      if i = 2 then
                          { r with
                              Op = Retitle("missing", "uno") }
                      else
                          r)

              match OpStream.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs 1 with
              | Error e -> failtestf "the prefix replays, so compact must not refuse: %s" e
              | Ok(snap, tail) ->
                  let origin = prodReplayVerdict (OpStream.replay planW planLaneGen.State0 rs)
                  let bounded = prodReplayVerdict (OpStream.replayFrom planW snap tail)
                  Expect.equal origin (HaltedAt(2, "no item missing")) "the origin halts at the third record"

                  Expect.equal
                      bounded
                      (HaltedAt(1, "no item missing"))
                      "and replayFrom reports the same halt at ITS index"

                  Expect.notEqual bounded origin "so the unqualified equality is false of production"

                  Expect.equal
                      (modelReplayVerdict (Chain.offset (posOfInt 1) (toModelReplayed bounded)))
                      origin
                      "and the theorem's offset is exactly what separates them"

          testCase "a tamper in the DISCARDED prefix verifies across — compact trusts the boundary hash it reads"
          <| fun _ ->
              // `compact_verifies_iff_original` carries a premise — the prefix verified — and this
              // is what it is for. The op at sequence zero is changed, the stream no longer
              // verifies, and its compaction at two verifies across all the same: to production and
              // to the model alike. Once the prefix is gone nothing can find it. Verify, then compact.
              let built =
                  chainUnder
                      OpStream.defaultHash
                      planW
                      planLaneGen.State0
                      (Human "writer")
                      [ AddItem("k1", "one"); AddItem("k2", "two"); AddItem("k3", "three") ]

              // The probe built the thing it claims to be about: a rejected `AddItem` chains nothing,
              // and a tamper of an empty stream is no tamper.
              Expect.hasLength built 3 "all three appends were accepted"

              let rs =
                  built
                  |> List.mapi (fun i r ->
                      if i = 0 then
                          { r with
                              Op = AddItem("k1", "TAMPERED") }
                      else
                          r)

              Expect.isFalse (OpStream.verifyChain OpStream.defaultHash planW rs) "the original does not verify"

              match OpStream.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs 2 with
              | Error e -> failtestf "the tampered prefix still replays, so compact does not refuse: %s" e
              | Ok(snap, tail) ->
                  Expect.isTrue
                      (OpStream.verifyAcross OpStream.defaultHash planHash planW snap tail)
                      "production verifies across the boundary of a stream that does not verify"

                  Expect.isTrue
                      (Chain.verify_across
                          OpStream.defaultHash
                          showPos
                          planW.Encode
                          (Chain.snap_payload showPos planHash)
                          (toModelSnapshot snap)
                          (toChainRecords tail))
                      "and so does the model — the split theorem says exactly this"

                  Expect.isFalse
                      (OpStream.verifyChain OpStream.defaultHash planW (List.truncate 2 rs))
                      "the prefix is where the break is, which is the other conjunct of the split"

          testCase
              "under a NON-EMPTY genesis a compaction at sequence zero does not verify across — snapshotAt hard-wires the empty one"
          <| fun _ ->
              // `compact_at_zero_needs_the_empty_genesis`, measured on production. Both shipped
              // configs have the empty genesis, so nothing shipped meets this; `StreamConfig` is a
              // public record, so a domain can.
              let cfg =
                  { OpStream.canonicalConfig with
                      Genesis = "g0" }

              let mutable st = planLaneGen.State0
              let mutable rs: OpRecord<PlanOp> list = OpStream.empty

              for op in [ AddItem("g1", "one"); AddItem("g2", "two") ] do
                  match OpStream.appendWith cfg OpStream.defaultHash planW (Human "writer") op st rs with
                  | Ok(st', rs') ->
                      st <- st'
                      rs <- rs'
                  | Error e -> failtestf "append refused: %s" e

              Expect.isTrue
                  (OpStream.verifyChainWith cfg OpStream.defaultHash planW rs)
                  "the stream verifies under its own config"

              let acrossAt (n: int) : bool * bool =
                  match OpStream.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs n with
                  | Error e -> failtestf "compact refused an intact stream: %s" e
                  | Ok(snap, tail) ->
                      OpStream.verifyAcrossWith cfg OpStream.defaultHash planHash planW snap tail,
                      Chain.verify_across
                          OpStream.defaultHash
                          showPos
                          planW.Encode
                          (Chain.snap_payload showPos planHash)
                          (toModelSnapshot snap)
                          (toChainRecords tail)

              Expect.equal
                  (acrossAt 0)
                  (false, false)
                  "at zero the snapshot says \"\" and the first record links to the genesis"

              Expect.equal
                  (acrossAt 1)
                  (true, true)
                  "past zero the boundary hash is a stored one and the genesis never reaches it"

          // ---- Phase 193 — the signed head: head, attestHead, verifyAttestation, and the re-mint ----

          testCase
              "the signed-head oracle agrees with production over the work-plan stream, every keyring and every re-minted splice"
          <| fun _ ->
              signedHeadDifferential
                  "work-plan signed heads"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  keyringSink
                  planW
                  tamperedPlanOp
                  planLaneGen
                  3800
                  40
              |> expectSignedAgreement "work-plan signed heads"

          testCase
              "the signed-head oracle agrees with production over the reference witness's stream, every keyring and every re-minted splice"
          <| fun _ ->
              signedHeadDifferential
                  "reference signed heads"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  keyringSink
                  treeW
                  (fun _ -> RemoveNode "tampered-node")
                  treeLaneGen
                  3810
                  30
              |> expectSignedAgreement "reference signed heads"

          testCase "a signed-head model handed a DIFFERENT hash disagrees with production — the comparison can lose"
          <| fun _ ->
              // The go-red for the whole differential: nothing about the chains, the keyrings or
              // the splices moves, only the function the model re-mints and walks with.
              let t =
                  signedHeadDifferential
                      "perturbed signed heads"
                      OpStream.defaultHash
                      swappedHash
                      keyringSink
                      planW
                      tamperedPlanOp
                      planLaneGen
                      3820
                      3

              Expect.isSome t.Failure "a model hashing with a different function must be caught"

          testCase
              "under a sink that verifies EVERYTHING a re-minted splice is accepted — signature_binds is load-bearing, to production and to the model alike"
          <| fun _ ->
              // `signature_binds` is the section's one new premise, and this is what it buys. The
              // promiscuous sink verifies every attestation against every head, so it does NOT
              // bind — and the theorem's conclusion fails with it, on production's own verdict.
              let t =
                  signedHeadDifferential
                      "promiscuous signed heads"
                      OpStream.defaultHash
                      OpStream.defaultHash
                      (fun _ -> promiscuousSink)
                      planW
                      tamperedPlanOp
                      planLaneGen
                      3830
                      3

              match t.Failure with
              | Some why ->
                  Expect.stringContains
                      why
                      "ACCEPTED under the original attestation"
                      "the differential fails on the theorem's conclusion, not on a model/production disagreement"
              | None -> failtest "a sink that does not bind must let a re-minted splice through"

              // And it is the MODEL's verdict too: one chain, one op replaced and re-minted.
              let steps =
                  [ Human "writer", AddItem("s1", "one"); Human "writer", AddItem("s2", "two") ]

              let signed = remint OpStream.defaultHash planW.Encode steps

              let forged =
                  remint OpStream.defaultHash planW.Encode [ List.head steps; Human "writer", AddItem("s2", "TWO") ]

              match OpStream.attestHead promiscuousSink signed with
              | None -> failtest "the promiscuous sink signs"
              | Some att ->
                  Expect.isTrue
                      (OpStream.verifyChain OpStream.defaultHash planW forged
                       && OpStream.verifyAttestation promiscuousSink att forged)
                      "production accepts the forgery under a sink that does not bind"

                  Expect.isTrue
                      (Chain.accepts_signed
                          OpStream.defaultHash
                          showPos
                          planW.Encode
                          ""
                          (modelVerify promiscuousSink)
                          (toModelAtt att)
                          (toChainRecords forged))
                      "and so does the model"

                  // Under the keyring sink the same forgery is refused by both.
                  let sink = keyringSink { Keys = [ "k", "s" ]; Active = "k" }

                  match OpStream.attestHead sink signed with
                  | None -> failtest "the keyring sink signs"
                  | Some bound ->
                      Expect.isFalse
                          (OpStream.verifyChain OpStream.defaultHash planW forged
                           && OpStream.verifyAttestation sink bound forged)
                          "production refuses it under a sink that binds"

                      Expect.isFalse
                          (Chain.accepts_signed
                              OpStream.defaultHash
                              showPos
                              planW.Encode
                              ""
                              (modelVerify sink)
                              (toModelAtt bound)
                              (toChainRecords forged))
                          "and so does the model"

          testCase
              "a signed empty-chain sentinel accepts the empty chain and no chain with a head — OpStream.head hard-wires the empty string"
          <| fun _ ->
              // `signed_sentinel_covers_the_empty_chain`, and the premise `signed_head_binds_chain`
              // carries because of it, measured on production.
              let sink = keyringSink { Keys = [ "k", "s" ]; Active = "k" }

              let empty: OpRecord<PlanOp> list = OpStream.empty
              Expect.equal (OpStream.head empty) "" "production's head of the empty chain is the literal empty string"
              Expect.equal (Chain.chain_head (toChainRecords empty)) "" "and so is the model's"

              let one =
                  remint OpStream.defaultHash planW.Encode [ Human "writer", AddItem("s1", "one") ]

              match OpStream.attestHead sink empty, OpStream.attestHead sink one with
              | Some overNothing, Some overOne ->
                  let accepts (att: Attestation) (rs: OpRecord<PlanOp> list) =
                      (OpStream.verifyChain OpStream.defaultHash planW rs
                       && OpStream.verifyAttestation sink att rs),
                      Chain.accepts_signed
                          OpStream.defaultHash
                          showPos
                          planW.Encode
                          ""
                          (modelVerify sink)
                          (toModelAtt att)
                          (toChainRecords rs)

                  Expect.equal (accepts overNothing empty) (true, true) "a signed sentinel accepts the empty chain"
                  Expect.equal (accepts overNothing one) (false, false) "and not a chain that has a head"

                  // The DROP arm at its smallest: the one-record chain, its record dropped and the
                  // remainder re-minted, is the empty chain — refused, because the head that was
                  // signed is not the sentinel.
                  Expect.equal (accepts overOne one) (true, true) "the signed one-record chain is accepted"
                  Expect.equal (accepts overOne empty) (false, false) "and the empty chain is refused under it"

                  Expect.isTrue
                      (Chain.splice_changes
                          [ { Chain.cactor = Actor.encode (Human "writer")
                              Chain.cop = AddItem("s1", "one") } ]
                          (Chain.Dropped Chain.PZero))
                      "which is a splice the theorem speaks about"
              | _ -> failtest "the keyring sink signs"

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

          // ---- Phase 139 — the same differential, over the committed `apply/` family ----
          //
          // Phase 172: read from THIS repository's `conformance/apply/`, which is where the
          // family is authored; the corpus carries a declared copy of it. This leg is about the
          // model agreeing with production over Core's own vectors, so it needs no corpus and
          // runs in the default suite.

          testCase "the preservation oracle agrees with Ops.apply over the committed `apply/` fixtures"
          <| fun _ ->
              let root = OwnedConformance.root ()
              let path = ApplyVectorExport.vectorsPath root

              if not (System.IO.File.Exists path) then
                  failtestf
                      "this repository carries no %s at '%s' — the differential's second host is not committed; re-run `--emit-apply` (no argument) and commit conformance/"
                      ApplyVectorExport.vectorsFileName
                      path

              match ApplyVectorExport.parseVectors (System.IO.File.ReadAllText path) with
              | Error m -> failtest ("the committed apply vectors did not read: " + m)
              | Ok vectors ->
                  let t = presCorpusDifferential TreeOps.apply vectors

                  match t.Diffs with
                  | d :: _ -> failtestf "the preservation oracle and production DISAGREE on a corpus fixture\n%s" d
                  | [] ->
                      // Adequacy over the committed sample, so a corpus that quietly stopped
                      // carrying a clause cannot read as agreement about it. The bounds are the
                      // family's own claim about itself, not a count of this run.
                      Expect.isGreaterThan
                          t.Accepted
                          0
                          (sprintf "the corpus sample carries accepted ops (accepted=%d)" t.Accepted)

                      Expect.isGreaterThan
                          t.Rejected
                          0
                          (sprintf "the corpus sample carries refused ops (rejected=%d)" t.Rejected)

                      Expect.isGreaterThan
                          t.Collided
                          0
                          (sprintf
                              "the corpus sample carries a graft whose id the tree already holds (collided=%d)"
                              t.Collided)

                      Expect.isGreaterThan
                          t.Duplicated
                          0
                          (sprintf
                              "the corpus sample carries a graft repeating an id WITHIN itself (duplicated=%d)"
                              t.Duplicated)

                      for cls in
                          [ "UnknownNode"
                            "DuplicateId"
                            "CannotRemoveRoot"
                            "WouldNestUnderSelf"
                            "ReorderMismatch" ] do
                          Expect.isTrue
                              (Set.contains cls t.Classes)
                              (sprintf "the corpus sample reached a %s rejection (reached: %A)" cls t.Classes)

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
                      e

          // ---- Phase 140 — the CONTAINER CAPABILITY: applyContained under a generated canHold ----

          testCase "the container oracle agrees with Ops.applyContained under a generated canHold"
          <| fun _ ->
              let t = contDifferential Preservation.apply_contained 1400 30

              match t.Diffs with
              | d :: _ -> failtestf "the container oracle and production DISAGREE\n%s" d
              | [] ->
                  // Adequacy, per capability SITE rather than in aggregate. A run that never met a
                  // refusal would agree with production perfectly and certify nothing about the
                  // guard; a run that met only insert refusals would not have been able to catch
                  // the go-red below, which is a move. Measured at 30 trials, seed 1400: accepted
                  // 1,245, refused 1,305, insert refusals 649, move refusals 370, batch 112, graft
                  // refusals 124, pre-emptions 89, invariant asserted on 1,028 probes, all eight
                  // predicates drawn. Each threshold sits below its measurement with room — they
                  // are here to catch a pool that stops reaching a shape, not to pin the numbers.
                  //
                  // Re-measured at Phase 161, and TWO of the numbers moved for reasons worth
                  // knowing rather than because the pool grew. The graft probes are new, so the
                  // insert count rose with them (185 → 649, of which 124 are the graft site). And
                  // the invariant is now asserted on half again as many probes (704 → 1,028)
                  // because retiring the `contained_op` premise removed a GATE from arm 5, not
                  // because more probes were drawn: probes whose graft broke the invariant used to
                  // be skipped, and the engine refuses them now.
                  Expect.isGreaterThan t.Accepted 500 (sprintf "operations were accepted (accepted=%d)" t.Accepted)

                  Expect.isGreaterThan
                      t.InsertRefusals
                      100
                      (sprintf "the sample refused an INSERT under a non-container (insert=%d)" t.InsertRefusals)

                  Expect.isGreaterThan
                      t.MoveRefusals
                      200
                      (sprintf "the sample refused a MOVE into a non-container (move=%d)" t.MoveRefusals)

                  Expect.isGreaterThan
                      t.BatchRefusals
                      50
                      (sprintf
                          "a BATCH inherited a member's NotAContainer (batch=%d) — the clause the model's inheritance half is about"
                          t.BatchRefusals)

                  // Phase 161's site. Counted separately from `InsertRefusals` for the reason the
                  // move half is counted separately from the insert half: a run that reached only
                  // the parent check could not have caught a graft walk that never fired, and
                  // before this phase the pool literally could not reach it — every insert it minted
                  // was a leaf.
                  Expect.isGreaterThan
                      t.GraftRefusals
                      50
                      (sprintf
                          "the sample refused a GRAFT for its own interior (graft=%d) — the Phase 161 site"
                          t.GraftRefusals)

                  // The theorem is quantified over `canHold`; a differential that drew one
                  // predicate would say nothing about the quantifier. Five of the eight subsets is
                  // the measured floor, and the two extremes are named because they are the
                  // degenerate readings: nothing can hold children, and the engine is `apply`.
                  Expect.isGreaterThan
                      (Set.count t.Predicates)
                      5
                      (sprintf "several DIFFERENT canHold predicates were drawn (%A)" t.Predicates)

                  Expect.isTrue
                      (Set.contains "(nothing)" t.Predicates)
                      (sprintf "the empty predicate was drawn — nothing can hold children (%A)" t.Predicates)

                  Expect.isTrue
                      (Set.contains "doc+para+section" t.Predicates)
                      (sprintf "the total predicate was drawn — applyContained IS apply there (%A)" t.Predicates)

                  // and the invariant was not asserted vacuously
                  Expect.isGreaterThan
                      t.Preserved
                      400
                      (sprintf
                          "the container invariant's hypotheses were MET on this many probes, and the conclusion therefore asserted (preserved=%d)"
                          t.Preserved)

                  Expect.isTrue
                      (Set.contains "NotAContainer" t.Classes)
                      (sprintf "the sample reached a NotAContainer rejection (reached: %A)" t.Classes)

                  // and the PRE-EMPTION was reached — the capability refusing an operation `apply`
                  // also refuses, but under a different class. It is asserted rather than merely
                  // permitted because it is the shape that made the first draft of comparison 3
                  // wrong, and a sample that stopped reaching it would let that draft back in.
                  Expect.isGreaterThan
                      t.Preempted
                      40
                      (sprintf
                          "the capability PRE-EMPTED another refusal on this many probes (preempted=%d) — a self-move into a leaf is NotAContainer here and WouldNestUnderSelf under apply"
                          t.Preempted)

          testCase "an engine that checks canHold on insert but NOT on move loses — the measurement can fail"
          <| fun _ ->
              // The teeth, and the model built them: `apply_contained_insert_only` is the engine a
              // plausible reading of the doc comment describes, and `insert_only_breaks_contained`
              // proves in F* that it admits a move the real one refuses and breaks the invariant
              // doing it. If this ever passes, the move site has stopped reaching the comparison
              // and the green run above means nothing about half the guard.
              let t = contDifferential Preservation.apply_contained_insert_only 1400 3

              // Measured at 3 trials: 42 moves into a non-container reached, and 42 disagreements.
              // A go-red that met none of the disputed inputs would agree with production and
              // certify nothing, so this is asserted before the disagreement is.
              Expect.isGreaterThan
                  t.MoveRefusals
                  20
                  (sprintf
                      "the go-red run reached a move into a non-container at all (move=%d) — otherwise it proves nothing"
                      t.MoveRefusals)

              Expect.isNonEmpty
                  t.Diffs
                  "an engine that skips the capability check on MoveNode MUST disagree with production"

              Expect.isTrue
                  (t.Diffs
                   |> List.exists (fun d ->
                       d.Contains "production REJECTED (NotAContainer) but the oracle accepted"
                       && d.Contains "op M|"))
                  (sprintf
                      "the disagreement is the one this phase is about — production refuses the MOVE, the insert-only engine admits it. Got:\n%s"
                      (List.head t.Diffs))

          testCase "canHold is consulted on the graft's INTERIOR too — the shipped engine, since Phase 161"
          <| fun _ ->
              // `nested_graft_refused`, on production, and BOTH halves of it — because the half
              // that matters is the difference between them.
              //
              // Until Phase 161 this case asserted the opposite: `validateInsert` applied `canHold`
              // to the parent and to nothing else, so the engine ADMITTED a subtree whose own
              // interior node was a non-container and the invariant broke across an accepted
              // operation. That was not a defect to fix in the test — it was the reason the theorem
              // carried `contained_op` as a hypothesis. The operator's ruling (DECISIONS D38) was to
              // inspect the graft, so the premise is discharged by the code and the case is
              // rewritten rather than deleted: the model still evaluates the old engine
              // (`apply_contained_pre161`), and what is asserted here is that production no longer
              // behaves like it.
              let canHold (n: RNode) = n.Kind = "doc"
              let tree = RNode.node "root" "doc" []

              let graft = InsertChild("root", RNode.node "a" "para" [ RNode.leaf "b" "para" "" ])

              Expect.isTrue (prodContained canHold tree) "the tree satisfies the invariant to begin with"
              Expect.isFalse (prodContainedOp canHold graft) "the GRAFT does not — its own interior node is a leaf kind"

              // (1) the engine as it stood still breaks the invariant, which is what made the
              //     premise necessary. Asked of the MODEL, because production no longer has it.
              match
                  Preservation.apply_contained_pre161
                      (fun t -> TreeOps.kind_of t = "doc")
                      (toModelOpWith toModelTree graft)
                      (toModelTree tree)
              with
              | DagFold.Error e ->
                  failtestf "the PRE-161 engine refused the graft (%A) — then the premise was never necessary" e
              | DagFold.Ok result ->
                  Expect.isFalse
                      (Preservation.contained (fun t -> TreeOps.kind_of t = "doc") result)
                      "the pre-161 engine broke the invariant, which is what `contained_op` was a hypothesis about"

              // (2) and the shipped engine refuses it, naming the offender IN THE GRAFT — "a",
              //     which holds "b" while the predicate refuses it — and not "root", which is the
              //     parent and can hold children perfectly well.
              match Ops.applyContained canHold nodew idw graft tree with
              | Error(NotAContainer("a", "para")) -> ()
              | other -> failtestf "expected NotAContainer naming the interior offender 'a', got %A" other

              Expect.equal
                  (Ops.canApplyContained canHold nodew idw graft tree)
                  (Ops.applyContained canHold nodew idw graft tree |> Result.map ignore)
                  "the dry run sees the interior refusal too"

          testCase "the sequence surface sees containment and the plain pair still does not — both halves"
          <| fun _ ->
              // `can_apply_all_ignores_containment` AND `can_apply_all_with_sees_containment`, on
              // production, at the same tree and the same script. This case was Phase 140's, where
              // it recorded a FINDING — `Ops.canApplyAll` and `Ops.applyAll` both threaded the
              // plain `apply`, so there was no container-aware sequence surface at all and a
              // caller pre-flighting with `canApplyAll` and executing with `applyContained` had a
              // check that could not see the refusal its executor would make.
              //
              // Phase 160 closed that by ADDING `applyAllWith` / `canApplyAllWith` beside the pair,
              // and 140's own note — "this case goes RED if that gap is ever closed" — turned out
              // to be a go-red by intention rather than by construction: every assertion it made
              // was about `canApplyAll` and `applyContained`, and 160 deliberately moved neither.
              // The plain pair is the `fun _ -> true` instance BY CONSTRUCTION, so it is blind to
              // containment now and always will be; a phase that changed that would have moved
              // every existing caller.
              //
              // So the case is restated to carry both halves, which is what gives it teeth in both
              // directions: it goes RED if the new pair ever stops consulting the capability, and
              // RED if the plain pair ever gains one.
              let canHold (n: RNode) = n.Kind = "box"

              let tree =
                  RNode.node "root" "box" [ RNode.leaf "leaf" "para" ""; RNode.leaf "x" "para" "" ]

              let script = [ MoveNode("x", "leaf") ]

              Expect.isTrue (prodContained canHold tree) "the tree satisfies the invariant to begin with"

              // ---- half one: the plain pair has not moved, and is still blind ----
              Expect.isOk
                  (Ops.canApplyAll nodew idw script tree
                   |> Result.mapError (fun (i, e) -> sprintf "%d:%A" i e))
                  "canApplyAll certifies the script — it consults no capability, and must not start"

              match Ops.applyContained canHold nodew idw (Batch script) tree with
              | Error(NotAContainer("leaf", "para")) -> ()
              | other ->
                  failtestf "applyContained was expected to refuse the very script canApplyAll certified, got %A" other

              // ---- half two: the sequence surface that DOES see it (Phase 160) ----
              match Ops.canApplyAllWith canHold nodew idw script tree with
              | Error(0, NotAContainer("leaf", "para")) -> ()
              | other ->
                  failtestf "canApplyAllWith must refuse at step 0 with the envelope applyAllWith raises, got %A" other

              match Ops.applyAllWith canHold nodew idw script tree with
              | Error(0, NotAContainer("leaf", "para"), partial) ->
                  // the refusal is at the first step, so the accepted prefix is empty and the
                  // partial tree is the caller's own — first-refusal-wins, not all-or-nothing
                  Expect.equal
                      (Tree.encodeHash nodew encNode partial)
                      (Tree.encodeHash nodew encNode tree)
                      "a script refused at step 0 hands back the tree it was given"
              | other -> failtestf "applyAllWith must refuse at step 0 and return the partial tree, got %A" other

          testCase "the script oracle agrees with Ops.applyAllWith / canApplyAllWith under a generated canHold"
          <| fun _ ->
              // The Phase 160 differential: the extracted `apply_all_with` / `can_apply_all_with`
              // beside the shipped pair, over generated scripts and a DRAWN predicate — drawn for
              // the same reason the per-op container family draws one, because the theorems
              // quantify over `canHold` and a run against one hand-picked predicate certifies that
              // instance and nothing about the quantifier.
              //
              // What is compared is the whole failure payload and not merely the verdict: the
              // INDEX and the partial TREE are the sequence surface's entire contribution over the
              // per-op one, so a differential that compared accept-vs-refuse alone would certify
              // nothing this phase added.
              let t = scriptDifferential 1600 24

              Expect.isEmpty t.Diffs (sprintf "the script oracle disagreed with production:\n%s" (renderDiffs t.Diffs))

              // adequacy, per the shape this phase is about — measured at 24 trials (seed 1600):
              // 45 scripts accepted, 123 refused, 60 of those refusals MID-script, 103 of them
              // NotAContainer, and all 8 predicates drawn.
              Expect.isGreaterThan t.Accepted 30 (sprintf "the run accepted scripts at all (accepted=%d)" t.Accepted)

              Expect.isGreaterThan t.Refused 30 (sprintf "the run refused scripts at all (refused=%d)" t.Refused)

              // The one that matters most: a refusal at index > 0 is the only probe that exercises
              // the partial tree as something other than the caller's own input, and it is the
              // state the non-atomic surface exists to produce.
              Expect.isGreaterThan
                  t.MidScript
                  20
                  (sprintf
                      "the run refused scripts MID-WAY (midScript=%d) — otherwise the partial tree is never compared against anything but the input"
                      t.MidScript)

              Expect.isGreaterThan
                  t.ContainerRefusals
                  20
                  (sprintf
                      "the run reached NotAContainer refusals on the SEQUENCE surface (container=%d) — the refusal class this phase exists to make visible to a script"
                      t.ContainerRefusals)

              Expect.isGreaterThan
                  (Set.count t.Predicates)
                  5
                  (sprintf "several DIFFERENT canHold predicates were drawn (%A)" t.Predicates)

          testCase "an engine whose sequence dry run drops the capability loses — the measurement can fail"
          <| fun _ ->
              // The teeth. The go-red is the model's own `can_apply_all`, section 8's plain
              // sequence dry run — the very function that was the shipped `canApplyAll` before this
              // phase, and which `can_apply_all_ignores_containment` proves admits a script the
              // container-aware call refuses. Handing it to the same differential in the dry-run
              // slot is the measurement, and it must lose: if this ever passes, the new surface has
              // stopped consulting the capability and the green run above means nothing.
              let t = scriptDifferential' scriptGoRedCan 1600 24

              Expect.isGreaterThan
                  t.ContainerRefusals
                  20
                  (sprintf
                      "the go-red run reached a container refusal at all (container=%d) — otherwise it proves nothing"
                      t.ContainerRefusals)

              Expect.isNonEmpty
                  t.Diffs
                  "a sequence dry run that drops the capability MUST disagree with Ops.canApplyAllWith"

              Expect.isTrue
                  (t.Diffs |> List.exists (fun d -> d.Contains "canApplyAllWith differs"))
                  (sprintf
                      "the disagreement is the one this phase is about — production's dry run sees the container refusal, the capability-free one does not. Got:\n%s"
                      (List.head t.Diffs))

          // ---- the diff, over pairs nobody derived from one another (Phase 141) ----

          testCase "the diff oracle agrees with Diff.toOps over INDEPENDENTLY generated pairs"
          <| fun _ ->
              // The widening this phase exists for. Every pair `Conformance.diffLaws` has ever
              // diffed was built by applying ops to `before`, so the algebra could always reach it;
              // these two trees are drawn independently over a shared id space and share only what
              // the draw happened to give them.
              let t = diffDifferential 1410 240

              Expect.isEmpty t.Diffs (sprintf "the diff oracle disagreed with production:\n%s" (renderDiffs t.Diffs))

              // adequacy — a run that never removed anything, or never reordered, would certify a
              // pass that never fired. MEASURED at 240 pairs, seed 1410: 179 scripts insert, 146
              // remove, 140 move, 168 reorder, 3 pairs came out identical, 80 contained diffs
              // produced a script and 160 were refused for containment, all 8 predicates drawn.
              // Every threshold below is under its measurement with headroom and above zero.
              Expect.isGreaterThan t.Inserted 40 (sprintf "pairs whose script INSERTS (%d)" t.Inserted)
              Expect.isGreaterThan t.Removed 40 (sprintf "pairs whose script REMOVES (%d)" t.Removed)
              Expect.isGreaterThan t.Moved 40 (sprintf "pairs whose script MOVES (%d)" t.Moved)
              Expect.isGreaterThan t.Reordered 20 (sprintf "pairs whose script REORDERS (%d)" t.Reordered)

              Expect.isGreaterThan
                  t.Contained
                  20
                  (sprintf
                      "contained diffs that produced a script and were certified through applyAllWith (%d)"
                      t.Contained)

              Expect.isGreaterThan
                  t.ContainerRefused
                  20
                  (sprintf
                      "contained diffs REFUSED for containment (%d) — otherwise the refusal half of the mirror is never reached"
                      t.ContainerRefused)

              Expect.isGreaterThan
                  (Set.count t.Predicates)
                  5
                  (sprintf "several DIFFERENT canHold predicates were drawn (%A)" t.Predicates)

          testCase "the four-pass ORDER is load-bearing — removes before inserts loses"
          <| fun _ ->
              // The go-red for `diff_emission_order`. The theorem proves the script is four
              // homogeneous blocks in one order, so partitioning by operation kind recovers those
              // blocks and reassembling them removes-first is the SAME four passes, reordered. If
              // this permutation reconstructed as reliably as the real order, the order argument
              // the source comment carries — and the theorem that mechanises it — would be about
              // nothing. It must lose, and it does: a removed region's surviving descendants are
              // still inside it when the remove runs.
              let t = diffDifferential 1410 240

              Expect.isGreaterThan
                  t.Removed
                  40
                  (sprintf "the run reached scripts that remove at all (%d) — otherwise it proves nothing" t.Removed)

              Expect.isGreaterThan
                  t.OrderBroken
                  10
                  (sprintf
                      "an emission order with the REMOVES FIRST must fail to reconstruct (broken=%d of %d pairs) — the same four blocks, permuted"
                      t.OrderBroken
                      t.Pairs)

          // MEASURED: 52 of 240 pairs. Not all of them, and that is the honest shape of the
          // claim — the permutation is harmless on a pair whose removals happen to hold no
          // survivor, which is most of them. What the order buys is correctness on the rest.

          testCase "a contained diff that never checks the after tree loses — the measurement can fail"
          <| fun _ ->
              // The go-red for the container half: the model's own plain `to_ops` in the contained
              // slot, which is exactly "an oracle that emits an insert under a non-container"
              // because it never looked. `diff_contained_at_total_is_plain` proves it is the
              // predicate-refuses-nothing instance, so handing it a predicate that refuses
              // something is a proved weakening before it is a measured one.
              let t = diffDifferential' diffGoRedContained 1410 240

              Expect.isGreaterThan
                  t.ContainerRefused
                  20
                  (sprintf
                      "the go-red run met pairs whose `after` nests under a non-container (%d) — otherwise it proves nothing"
                      t.ContainerRefused)

              Expect.isNonEmpty
                  t.Diffs
                  "a contained diff that never checks the after tree MUST disagree with Diff.toOpsContained"

              Expect.isTrue
                  (t.Diffs |> List.exists (fun d -> d.Contains "toOpsContained differs"))
                  (sprintf
                      "the disagreement is the one this phase is about — production refuses the pair up front, the unchecked one emits a script. Got:\n%s"
                      (List.head t.Diffs))

          testCase "the reconstruction hypothesis is NECESSARY — a pair whose shared id changes KIND loses"
          <| fun _ ->
              // Phase 167's go-red, and the one this phase owes. `diff_reconstructs` is proved
              // under `kinds_agree` — a node id the two trees share names the same kind in both —
              // and a hypothesis nobody can see fail is indistinguishable from one that was never
              // needed. The pool above has ENFORCED that condition since Phase 141 (a node's
              // content is a function of its id), so the green run can never exercise it; this is
              // the same generator, the same seed and the same shapes with the `after` tree's kinds
              // drawn by a second function of the id.
              //
              // PRODUCTION ONLY — no model is in the loop. What is measured is that the SHIPPED
              // engine cannot express the change, not that the model agrees with it about anything;
              // the model's own counterexample is `TreeDiff.kinds_agree_is_necessary`, proved.
              let mutable r = ConfRng.ofSeed 1410
              let mutable shared = 0
              let mutable lost = 0
              let mutable refused = 0

              for _ in 1..240 do
                  let before, r1 = genPairTree r
                  let after0, r2 = genPairTree r1
                  // drawn and discarded, so the RNG stream matches the green run's pair for pair
                  let _, r3 = drawCanHold r2
                  r <- r3
                  let after = reKind diffKindShifted after0

                  let sharedIds =
                      Set.intersect (Set.ofList (idsOfTree before)) (Set.ofList (idsOfTree after))
                      |> Set.remove "root"

                  if not (Set.isEmpty sharedIds) then
                      shared <- shared + 1

                      match Diff.toOps nodew idw before after with
                      | Error e ->
                          // `diff_refusals_exact` says the only refusals are a root-id mismatch and
                          // a repeated id, and this pair has neither — a kind disagreement is not a
                          // refusal, which is half of why it is dangerous.
                          failtestf "toOps REFUSED a well-formed pair over a kind disagreement (%A)" e
                      | Ok ops ->
                          match Ops.canApplyAll nodew idw ops before with
                          | Error _ -> refused <- refused + 1
                          | Ok() -> ()

                          match Ops.applyAll nodew idw ops before with
                          | Ok t when prodTreeHash t = prodTreeHash after -> ()
                          | _ -> lost <- lost + 1

              // MEASURED at 240 pairs, seed 1410: 163 pairs share a non-root id, all 163 fail to
              // reconstruct, 0 fail applicability. Run the other way — `reKind diffKindOf`, which
              // restores the constraint — it is 0 of 163, so the probe distinguishes the two
              // directions rather than failing for any reason at all.
              //
              // Adequacy first, and it is not a formality: "every pair lost" over a pool that never
              // drew a shared id would be a green assertion measuring nothing at all.
              Expect.isGreaterThan
                  shared
                  100
                  (sprintf "pairs that share a non-root id, and so actually break `kinds_agree` (%d of 240)" shared)

              Expect.equal
                  lost
                  shared
                  (sprintf
                      "EVERY pair whose shared id carries a different kind must fail to reconstruct (%d of %d) — no skeleton operation edits a node, so the survivor keeps `before`'s kind whatever the script does"
                      lost
                      shared)

              // The asymmetry the two theorems predict: `diff_applicable` carries NO hypothesis, so
              // applicability survives the disagreement and only reconstruction fails. If this ever
              // counts above zero, one of the two theorems is about a different function.
              Expect.equal
                  refused
                  0
                  (sprintf
                      "APPLICABILITY survives a kind disagreement (%d refusals) — `diff_applicable` holds without `kinds_agree`, and only `diff_reconstructs` needs it"
                      refused)

          // ---- Phase 152: the decode surface under a MEMBER REORDERING ----

          testCase "every combinator answers the same on every shuffle of every nodes/ fixture"
          <| fun _ ->
              // Real documents, in a real vocabulary, with their members reordered at every depth
              // — the case the corpus itself cannot present, because every fixture in it is
              // canonically ordered.
              match SiblingCorpus.resolve "nodes" with
              | SiblingCorpus.NotAsked why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let files = Directory.GetFiles(Path.Combine(root, "nodes"), "*.json") |> Array.sort
                  Expect.isGreaterThan files.Length 50 "the nodes/ family carries a real corpus"

                  let tally =
                      files
                      |> Array.fold
                          (fun acc path ->
                              let name = Path.GetFileNameWithoutExtension path

                              match Json.parse (File.ReadAllText path) with
                              | Error e -> failtestf "%s: not JSON (%s)" name e
                              | Ok v ->
                                  everyValue v
                                  |> List.fold (fun a el -> shuffleProbe Decode.getProp name 4 15201 el a) acc)
                          emptyShuffleTally

                  expectShuffleAgreement "nodes/ fixtures, shuffled" tally

                  Expect.isGreaterThan
                      tally.Refused
                      0
                      "the reference decoder refused — the corpus is in a vocabulary it does not know, so this is its arm"

                  // … and the oracle still agrees with production on the shuffled documents,
                  // which is the Phase 135 comparison over inputs no fixture contains.
                  let probes =
                      files
                      |> Array.fold
                          (fun acc path ->
                              match Json.parse (File.ReadAllText path) with
                              | Error e -> failtestf "%s: not JSON (%s)" (Path.GetFileNameWithoutExtension path) e
                              | Ok v ->
                                  if keysUniqueDeep v then
                                      let shuffled, _ = shuffleDeep v (ConfRng.ofSeed 15202)
                                      runProbes toModel (Path.GetFileNameWithoutExtension path) shuffled acc
                                  else
                                      acc)
                          emptyTally

                  expectProbeAgreement "nodes/ fixtures, shuffled" probes

          testCase "every combinator answers the same on every shuffle of every ops/ fixture"
          <| fun _ ->
              match SiblingCorpus.resolve "ops" with
              | SiblingCorpus.NotAsked why -> skiptest why
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
                          | Ok v ->
                              everyValue v
                              |> List.fold (fun a el -> shuffleProbe Decode.getProp name 4 15203 el a) acc)
                      emptyShuffleTally
                  |> expectShuffleAgreement "ops/ fixtures, shuffled"

          testCase "the node decoder returns the SAME TREE on every shuffle — §20's one answer"
          <| fun _ ->
              // The accept path, which the corpus pools above cannot reach: documents in the
              // reference vocabulary, encoded, then reordered at every depth. `decode_node`'s
              // result carries no members of its own, so the lemma claims literal equality and
              // this is where that is measured.
              let mutable rng = ConfRng.ofSeed 15204
              let mutable tally = emptyShuffleTally
              let mutable exact = 0

              for i in 1..150 do
                  let n, r' = genRef 0 rng
                  rng <- r'
                  let el = encodeRef n
                  tally <- shuffleProbe Decode.getProp (sprintf "ref %d" i) 4 (15300 + i) el tally

                  // … and the tree it returns is the node that was encoded, not merely a stable
                  // answer: a decoder that answered `Error` consistently would pass the above.
                  let shuffled, r'' = shuffleDeep el rng
                  rng <- r''
                  Expect.equal (decodeRef shuffled) (Ok n) "the reordered encoding decodes to the same node"
                  exact <- exact + 1

              expectShuffleAgreement "reference vocabulary, shuffled" tally

              Expect.isGreaterThan tally.Accepted 0 "the node decoder ACCEPTED — this pool is its accept arm"
              Expect.isGreaterThan exact 0 "the round-trip-under-shuffle sample is non-empty"

              // The refusal arm under a shuffle, where the MESSAGE is the whole comparison.
              let mutable rng2 = ConfRng.ofSeed 15205
              let mutable refusals = emptyShuffleTally

              for i in 1..300 do
                  let v, r' = genJ 0 rng2
                  rng2 <- r'

                  for el in everyValue v do
                      refusals <- shuffleProbe Decode.getProp (sprintf "generated %d" i) 3 (15400 + i) el refusals

              expectShuffleAgreement "generated documents, shuffled" refusals
              Expect.isGreaterThan refusals.Refused 0 "the node decoder REFUSED — the message comparison ran"

          testCase "a decoder that reads the FIRST member rather than the named one LOSES on a shuffle"
          <| fun _ ->
              // The teeth. `getPropByPosition` is `Wire.Decode` with one clause changed: it takes
              // the first member of an object instead of the one asked for. On the canonically
              // ordered corpus it is very nearly right, and every Phase 135 pool would pass it —
              // which is precisely why this family exists. Under a reordering it must lose.
              let mutable rng = ConfRng.ofSeed 15206
              let mutable tally = emptyShuffleTally

              for i in 1..120 do
                  let n, r' = genRef 0 rng
                  rng <- r'
                  tally <- shuffleProbe getPropByPosition (sprintf "positional %d" i) 4 (15500 + i) (encodeRef n) tally

              Expect.isGreaterThan
                  tally.Moved
                  0
                  "the go-red run actually moved members — otherwise it proves nothing about order"

              match tally.Diffs with
              | [] ->
                  failtest
                      "a positional getProp reads whichever member came first, so a reordering MUST change its answer — this comparison cannot lose"
              | ds ->
                  Expect.isTrue
                      (ds |> List.exists (fun d -> d.Contains "MOVED under a member reordering"))
                      (sprintf "the disagreement is an answer that moved with the order — got:\n%s" (List.head ds))

                  // … and the SHUFFLE ITSELF is not what broke: every pair it drew is one the
                  // extracted relation relates, so the loss is the decoder's and not the probe's.
                  Expect.equal
                      tally.Unrelated
                      0
                      "every drawn pair is related by the extracted member_perm — the go-red measures the decoder, not a bad shuffle"

          // ---- the canonical encoder, over the corpus and a generated pool (Phase 149) ----

          testCase "the canon oracle agrees with Canon.render over every nodes/ fixture"
          <| fun _ ->
              let t = onBigStack (fun () -> canonCorpus canonWire "nodes" true)

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the canon oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              Expect.isGreaterThan t.Docs 100 (sprintf "the nodes/ family was read at all (%d fixtures)" t.Docs)

              Expect.isGreaterThan
                  t.SortableObjects
                  100
                  (sprintf
                      "fixtures carrying an object with two or more DISTINCT keys (%d) — rule 2's sort is unobservable without one"
                      t.SortableObjects)

              Expect.isGreaterThan
                  t.RoundTrips
                  100
                  (sprintf "fixtures inside the canonical subset, whose round trip was checked (%d)" t.RoundTrips)

          testCase "the canon oracle agrees with Canon.render over every ops/ fixture"
          <| fun _ ->
              let t = onBigStack (fun () -> canonCorpus canonWire "ops" true)

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the canon oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              Expect.isGreaterThan t.Docs 10 (sprintf "the ops/ family was read at all (%d fixtures)" t.Docs)

          testCase "the canon oracle agrees with Canon.render over a generated JVal pool"
          <| fun _ ->
              // The pool is where the corpus is thin: rule 6's three escape classes (a quote, a
              // backslash, a control character), rule 2's comparator above the BMP (an astral key
              // beside a private-use one — the pair WIRE_FORMAT's own rule-2 note says UTF-16
              // order decides differently from code-point order), and rule 5's scientific layout.
              let t = onBigStack (fun () -> canonGenerated canonWire 1490 1200 true)

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the canon oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              // adequacy — a pool that never reached a shape measures nothing about the rule that
              // governs it. MEASURED at 1200 documents, seed 1490, and every threshold below sits
              // under its measurement with headroom and above zero.
              Expect.isGreaterThan
                  t.SortableObjects
                  100
                  (sprintf "documents carrying a sortable object (%d)" t.SortableObjects)

              Expect.isGreaterThan
                  t.EscapedStrings
                  100
                  (sprintf "strings carrying a character rule 6 escapes (%d)" t.EscapedStrings)

              Expect.isGreaterThan
                  t.ControlStrings
                  20
                  (sprintf "strings carrying a CONTROL character — the \\u00xx arm specifically (%d)" t.ControlStrings)

              Expect.isGreaterThan
                  t.ScientificFloats
                  20
                  (sprintf "floats whose canonical token is in scientific notation (%d)" t.ScientificFloats)

              Expect.isGreaterThan
                  t.RoundTrips
                  200
                  (sprintf "documents inside the canonical subset, whose round trip was checked (%d)" t.RoundTrips)

          testCase "a model with rule 2's key order REVERSED loses — the comparison can fail"
          <| fun _ ->
              // The go-red. `render_injective_up_to_key_order` and `render_deterministic` both turn
              // on the sort being a canonical choice, so the instrument that must lose is one whose
              // sort is a DIFFERENT choice: the same comparator, reversed. Every object carrying two
              // distinct keys must then disagree — and a document carrying none must still agree,
              // which is what says the instrument is narrow to the rule rather than broken.
              let t = onBigStack (fun () -> canonGenerated canonWireGoRed 1490 1200 false)

              Expect.isGreaterThan
                  t.SortableObjects
                  100
                  (sprintf
                      "the go-red run reached objects with two distinct keys at all (%d) — otherwise it proves nothing"
                      t.SortableObjects)

              Expect.isNonEmpty t.Diffs "a model that sorts object keys the other way MUST disagree with Canon.render"

              // and it must NOT disagree on everything: a document with no sortable object is
              // outside rule 2 entirely, and an instrument that reddened those too would be
              // measuring something other than the key order.
              Expect.isLessThan
                  (List.length t.Diffs)
                  t.Docs
                  (sprintf
                      "the reversed comparator disagreed on %d of %d documents — it must leave the ones with no sortable object alone"
                      (List.length t.Diffs)
                      t.Docs)

          testCase "the four renderings that ALIAS — `render_injective` is false, and this is why"
          <| fun _ ->
              // The phase's finding, as assertions over PRODUCTION rather than as prose. The shard
              // asked for `render a == render b ==> a == b`; these are the four ways it fails, and
              // `proofs/WireCanon.fst` carries each as a lemma. They go red if the encoder ever
              // changes — which is the point: three of them are documented design choices that a
              // future session must not "fix" by accident, and the first is the one worth knowing.

              // 1. a non-finite float is a STRING on the wire, so a digest over `JFloat nan`
              //    collides with the digest over `JStr "NaN"`. `Canon.render` refuses nothing and
              //    its bytes are pinned, so this stays asserted; the guarded `Canon.tryRender`
              //    beside it (Phase 165) is the entry point that does refuse — see its own cases.
              Expect.equal
                  (Canon.render (JFloat nan))
                  (Canon.render (JStr "NaN"))
                  "rule 5 renders NaN as the quoted string \"NaN\", which is what that STRING renders as"

              Expect.equal (Canon.render (JFloat infinity)) (Canon.render (JStr "Infinity")) "the same, for +infinity"

              Expect.equal (Canon.render (JFloat -infinity)) (Canon.render (JStr "-Infinity")) "the same, for -infinity"

              // 2. an integral float is an INTEGER on the wire — the documented normalisation
              //    `JVal`'s own type doc names.
              Expect.equal (Canon.render (JFloat 2.0)) (Canon.render (JInt 2)) "rule 5: `render (JFloat 2.0)` emits `2`"

              // 3. the two zeroes are one token — rule 5's `-0` collapse.
              Expect.equal (Canon.render (JFloat -0.0)) (Canon.render (JFloat 0.0)) "rule 5 collapses -0 to 0"

              // 4. member order is not observable — rule 2's sort. Not a loss: it is the purpose of
              //    a canonical form, and the reason the theorem is stated up to member order.
              Expect.equal
                  (Canon.render (JObj [ "a", JInt 1; "b", JInt 2 ]))
                  (Canon.render (JObj [ "b", JInt 2; "a", JInt 1 ]))
                  "rule 2 sorts, so the authored member order does not reach the bytes"

              // and the model agrees with production on all four, which is what makes the lemmas
              // statements about THIS encoder rather than about a model of one.
              for a, b in
                  [ JFloat nan, JStr "NaN"
                    JFloat infinity, JStr "Infinity"
                    JFloat -infinity, JStr "-Infinity"
                    JFloat 2.0, JInt 2
                    JFloat -0.0, JFloat 0.0
                    JObj [ "a", JInt 1; "b", JInt 2 ], JObj [ "b", JInt 2; "a", JInt 1 ] ] do
                  Expect.equal
                      (canonFromChs (WireCanon.render canonWire (canonToModel a)))
                      (canonFromChs (WireCanon.render canonWire (canonToModel b)))
                      (sprintf "the model aliases the pair production aliases: %A / %A" a b)

          testCase "the canon oracle handles the non-canonical arms production reaches"
          <| fun _ ->
              // Outside the canonical subset the theorems say nothing, but the DIFFERENTIAL still
              // has to agree — the model is a model of the whole encoder, not only of the part the
              // theorem covers, and a model that diverged here would be a model of something else.
              let mutable t = emptyCanonTally

              for v in
                  [ JFloat nan
                    JFloat infinity
                    JFloat -infinity
                    JFloat -0.0
                    JFloat 0.0
                    JFloat 2.0
                    JFloat 1e17
                    JInt 0
                    JInt -2147483648
                    JInt 2147483647
                    JStr ""
                    JStr "\u0000\u001f\"\\/"
                    JArr []
                    JObj []
                    JObj [ "a", JFloat nan; "$type", JStr "X" ]
                    JArr [ JFloat infinity; JObj [ "b", JArr [ JFloat -0.0 ] ] ] ] do
                  t <- canonProbe canonWire "non-canonical arm" v t

              Expect.isEmpty
                  t.Diffs
                  (sprintf
                      "the canon oracle disagreed with production off the canonical subset:\n%s"
                      (renderCanonDiffs t.Diffs))

          // ---- the guard beside the canonical encoder (Phase 165) ----

          testCase "Canon.tryRender refuses exactly the non-finite alias witnesses, and names them by path"
          <| fun _ ->
              // Refutation 1 of the four above is the one that is not a documented design choice,
              // and it has THREE witnesses, one per non-finite class. Each is refused, at the root.
              Expect.equal
                  (Canon.tryRender (JFloat nan))
                  (Result.Error "non-finite float has no canonical rendering of its own: NaN at $")
                  "a NaN is refused, by token and by path"

              Expect.equal
                  (Canon.tryRender (JFloat infinity))
                  (Result.Error "non-finite float has no canonical rendering of its own: Infinity at $")
                  "+infinity is refused"

              Expect.equal
                  (Canon.tryRender (JFloat -infinity))
                  (Result.Error "non-finite float has no canonical rendering of its own: -Infinity at $")
                  "-infinity is refused"

              // ... and the STRING each one aliases is a perfectly good value, and is not refused.
              // So are refutations 2, 3 and 4 — the integer-shaped float, the two zeroes and the
              // member order — which the format documents and the guard must therefore leave alone.
              for v in
                  [ JStr "NaN"
                    JStr "Infinity"
                    JStr "-Infinity"
                    JFloat 2.0
                    JInt 2
                    JFloat -0.0
                    JFloat 0.0
                    JFloat 1e17
                    JObj [ "a", JInt 1; "b", JInt 2 ]
                    JObj [ "b", JInt 2; "a", JInt 1 ] ] do
                  Expect.equal
                      (Canon.tryRender v)
                      (Result.Ok(Canon.render v))
                      (sprintf "a value holding no non-finite float is exactly `Ok (render v)`: %A" v)

              // The path: an array by index, a member by its canonically escaped key, the FIRST
              // offender in document order — and document order is AUTHORED order, not the sorted
              // order `render` would emit, because the scan runs before any sort.
              Expect.equal
                  (Canon.tryRender (JObj [ "$type", JStr "X"; "a", JArr [ JInt 1; JFloat infinity ] ]))
                  (Result.Error "non-finite float has no canonical rendering of its own: Infinity at $[\"a\"][1]")
                  "a nested offender is named through the member and the index"

              Expect.equal
                  (Canon.tryRender (JObj [ "b", JFloat nan; "a", JFloat infinity ]))
                  (Result.Error "non-finite float has no canonical rendering of its own: NaN at $[\"b\"]")
                  "the first offender in AUTHORED order is the one named, though `a` sorts first"

              Expect.equal
                  (Canon.tryRender (JArr [ JArr []; JObj [ "k\"\u0001", JFloat -infinity ] ]))
                  (Result.Error
                      "non-finite float has no canonical rendering of its own: -Infinity at $[1][\"k\\\"\\u0001\"]")
                  "a key carrying a quote and a control character is escaped as rule 6 escapes it"

              // `render` is untouched: every value refused above still renders, to the aliasing bytes.
              Expect.equal (Canon.render (JFloat nan)) "\"NaN\"" "the unguarded renderer's bytes did not move"

          testCase "the guard oracle agrees with Canon.tryRender over the corpus — and refuses none of it"
          <| fun _ ->
              for family, floor in [ "nodes", 100; "ops", 10 ] do
                  let t = onBigStack (fun () -> guardCorpus canonWire family)

                  Expect.isEmpty
                      t.Diffs
                      (sprintf
                          "the guard oracle disagreed with production on %s/:\n%s"
                          family
                          (renderCanonDiffs t.Diffs))

                  Expect.isGreaterThan
                      t.Docs
                      floor
                      (sprintf "the %s/ family was read at all (%d fixtures)" family t.Docs)

                  // JSON cannot spell a non-finite float, so a parsed fixture cannot hold one: on
                  // the corpus the guard is `Ok (render v)` everywhere, which is the acceptance's
                  // "agrees with render on the corpus" measured rather than assumed.
                  Expect.equal t.Refused 0 (sprintf "no %s/ fixture is refused" family)

          testCase "the guard oracle agrees with Canon.tryRender over a generated pool carrying non-finite floats"
          <| fun _ ->
              let t = onBigStack (fun () -> guardGenerated canonWire 1650 2400)

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the guard oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              // adequacy — MEASURED at 2400 documents, seed 1650: 398 refused (78 of them two or
              // more steps deep; 132 NaN, 146 +infinity, 120 -infinity) and 2002 accepted, 103 of
              // those carrying a float outside the canonical subset. Every threshold sits under
              // its measurement with headroom and above zero.
              Expect.isGreaterThan t.Refused 200 (sprintf "documents the guard refused (%d)" t.Refused)

              Expect.isGreaterThan
                  (t.Docs - t.Refused)
                  200
                  (sprintf "documents the guard accepted (%d)" (t.Docs - t.Refused))

              Expect.isGreaterThan
                  t.DeepRefusals
                  40
                  (sprintf
                      "refusals two or more steps deep — the scan's recursion, not its leaf arm (%d)"
                      t.DeepRefusals)

              Expect.isGreaterThan t.NaNs 60 (sprintf "refusals naming NaN (%d)" t.NaNs)
              Expect.isGreaterThan t.PosInfs 60 (sprintf "refusals naming Infinity (%d)" t.PosInfs)
              Expect.isGreaterThan t.NegInfs 60 (sprintf "refusals naming -Infinity (%d)" t.NegInfs)

              Expect.isGreaterThan
                  t.AcceptedNormalised
                  50
                  (sprintf
                      "ACCEPTED documents carrying an integer-shaped float or a zero (%d) — the set the guard must not refuse"
                      t.AcceptedNormalised)

          testCase "a guard model that cannot see NaN loses — on exactly the documents a NaN decides"
          <| fun _ ->
              // The go-red. `tryrender_refuses_exactly_aliasing` turns on the guard's predicate
              // being `fclass f <> FFinite`, so the instrument that must lose is one whose predicate
              // is narrower by one class. It must disagree on every document whose FIRST non-finite
              // float is a NaN — the model walks past it and either renders or names a later
              // infinity — and on NO other document, a refusal for an infinity included.
              let t = onBigStack (fun () -> guardGenerated guardWireGoRed 1650 2400)

              Expect.isGreaterThan t.NaNs 60 (sprintf "the go-red run reached NaN refusals at all (%d)" t.NaNs)

              Expect.equal
                  (List.length t.Diffs)
                  t.NaNs
                  (sprintf
                      "the NaN-blind model disagreed on %d documents and production named a NaN on %d — they must be the same documents"
                      (List.length t.Diffs)
                      t.NaNs)

          // ---- the evolution policy, over the envelope family and perturbed IDL pairs (Phase 151) ----

          testCase "the versioning oracle agrees with Versioning.classify over perturbed idl.json pairs"
          <| fun _ ->
              let pairs = idlPairs ()

              let diffs =
                  pairs
                  |> List.fold (fun acc (label, before, after) -> versionProbe label before after acc) []

              Expect.isEmpty
                  diffs
                  (sprintf "the versioning oracle disagreed with production:\n%s" (renderCanonDiffs diffs))

              // adequacy — the four rows §15.4 distinguishes must actually have been REACHED, and
              // each must land where the table says. A pass over four pairs that all classified
              // the same way would measure one row four times.
              let verdicts =
                  pairs |> List.map (fun (label, b, a) -> label, Versioning.classify b a)

              let verdictOf name =
                  verdicts |> List.find (fun (l, _) -> l = name) |> snd

              match verdictOf "add a kind" with
              | Versioning.Additive [ one ] -> Expect.equal one "Phase151ProbeKind" "the added tag is the one added"
              | other -> failtestf "adding a kind classified as %A" other

              match verdictOf "add an optional field" with
              | Versioning.Additive [] -> ()
              | other ->
                  failtestf
                      "adding an OPTIONAL FIELD moved the kind-tag set (%A) — a field addition is invisible AT THIS DELTA by construction, and a verdict here means the perturbation changed a tag. §15.4's optional-field row itself is measured by the field-add case below, which walks the composition that does see it"
                      other

              match verdictOf "remove a tag" with
              | Versioning.Breaking(removed, added) ->
                  Expect.equal (List.length removed) 1 "exactly the removed tag"
                  Expect.isEmpty added "removing a tag adds none"
              | other -> failtestf "removing a tag classified as %A" other

              match verdictOf "rename" with
              | Versioning.Breaking(removed, added) ->
                  Expect.equal (List.length removed) 1 "a rename removes exactly the old tag"
                  Expect.equal (List.length added) 1 "and adds exactly the new one"
              | other ->
                  failtestf
                      "a RENAME classified as %A — §15.4 calls it breaking, and it is the row an author gets wrong"
                      other

          testCase "the versioning oracle agrees with production over §15.4's three field-add rows"
          <| fun _ ->
              let ms = fieldAddMeasurements ()

              let byClass c =
                  ms |> List.find (fun m -> m.OptClass = c)

              let baseP = Versioning.Profile.coreV1

              // 1. the SEVERITY, clause for clause against `Diff.classifyFieldAdd`.
              for m in ms do
                  Expect.equal
                      m.ModelSeverity
                      m.ProductionSeverity
                      (sprintf "the severity of a `%s` field add" m.OptClass)

              // 2. the PROFILE the revision mints, end to end: the model's own classification, its
              //    own partition into the two subject sets, its own bump — beside
              //    `Diff.bumpProfile`. This is §15.4's actual subject matter, since the table is a
              //    table of BUMPS and the bump is what a publisher acts on.
              for m in ms do
                  Expect.equal
                      m.ModelProfile
                      m.ProductionProfile
                      (sprintf "the profile `core@1.0` bumps to under a `%s` field add" m.OptClass)

              // adequacy — the row is REACHED, and the three classes are told apart. A pass in
              // which all three minted the same profile would be measuring the arrival of a field
              // rather than its optionality class, which is the whole of what this row is about.
              let optional = byClass "optional"

              Expect.equal
                  optional.ProductionProfile
                  (baseP.Name, baseP.Major, baseP.Minor + 1)
                  "§15.4's optional-field row, reached: the MINOR moves and the major does not, so an old consumer is `Behind` and tolerates rather than being told nothing happened"

              Expect.isFalse optional.BreaksEmitters "an added optional field breaks no emitter"

              let required = byClass "required"

              Expect.equal
                  required.ProductionProfile
                  (baseP.Name, baseP.Major, baseP.Minor + 1)
                  "a REQUIRED field add is still a minor on the wire — every existing document decodes"

              Expect.isTrue
                  required.BreaksEmitters
                  "and the emitter obligation is carried BESIDE the profile, not folded into it: a major would tell every consumer to refuse documents that decode perfectly"

              let hostOnly = byClass "hostOnly"

              Expect.equal
                  hostOnly.ProductionProfile
                  (baseP.Name, baseP.Major, baseP.Minor)
                  "a HOST-ONLY field is on no document in either direction (WIRE_FORMAT §9), so no profile may move"

              // the go-red — the model's own `classify_field_add_ignoring_optionality`, which is
              // what an author writes who reads "an added field is additive" and stops. It agrees
              // on the row this phase is named for, which is exactly why the comparison has to
              // reach the other two.
              Expect.notEqual
                  hostOnly.GoRedProfile
                  hostOnly.ProductionProfile
                  "a classifier that ignores the optionality class publishes a minor for a field that is on no document — the comparison can fail"

              Expect.equal
                  optional.GoRedProfile
                  optional.ProductionProfile
                  "and it agrees on the optional row, which is what makes it a go-red worth having rather than a strawman"

          testCase "the versioning oracle agrees with production over every envelope/ fixture"
          <| fun _ ->
              let fixtures = JsonParseDiff.corpusTexts "envelope"

              let diffs =
                  fixtures
                  |> List.fold (fun acc (name, text) -> envelopeProbe (sprintf "envelope/%s" name) text acc) []

              Expect.isEmpty
                  diffs
                  (sprintf "the versioning oracle disagreed with production:\n%s" (renderCanonDiffs diffs))

              Expect.isGreaterThan
                  (List.length fixtures)
                  4
                  (sprintf "the envelope/ family was read at all (%d fixtures)" (List.length fixtures))

              // adequacy — all three negotiation outcomes must have been reached, and at least one
              // fixture must have carried a tag the consumer does NOT know. A family that only ever
              // produced `Current` would exercise neither tolerance nor preservation, and the run
              // would be green having tested nothing §15.3 is about.
              let outcomes =
                  fixtures
                  |> List.choose (fun (_, text) ->
                      match Versioning.parse text with
                      | Result.Ok env -> Some(Versioning.negotiate Versioning.Profile.coreV1 env.Profile)
                      | Result.Error _ -> None)

              Expect.isTrue
                  (outcomes
                   |> List.exists (function
                       | Versioning.Current -> true
                       | _ -> false))
                  "a Current fixture was reached"

              Expect.isTrue
                  (outcomes
                   |> List.exists (function
                       | Versioning.Behind _ -> true
                       | _ -> false))
                  "a Behind fixture was reached — otherwise tolerance is untested"

              Expect.isTrue
                  (outcomes
                   |> List.exists (function
                       | Versioning.Foreign _ -> true
                       | _ -> false))
                  "a Foreign fixture was reached — otherwise the refusal half is untested"

              let unknowns =
                  fixtures
                  |> List.filter (fun (_, text) ->
                      match Versioning.parse text with
                      | Result.Ok env ->
                          match envelopeTagOf env.Payload with
                          | Result.Ok t -> not (envelopeVocab.Contains t)
                          | Result.Error _ -> false
                      | Result.Error _ -> false)

              Expect.isGreaterThan
                  (List.length unknowns)
                  1
                  (sprintf
                      "fixtures carrying a tag the consumer does NOT know (%d) — preservation is unobservable without one"
                      (List.length unknowns))

          testCase "a model whose classify IGNORES REMOVALS loses — the comparison can fail"
          <| fun _ ->
              // The go-red. `classify_sound` turns entirely on removals being looked for, so the
              // instrument that must lose is one that does not look — the model's own
              // `classify_ignoring_removals`, which F* refutes in the same file. Every pair with a
              // removal must disagree with production; every purely additive pair must still agree,
              // which is what says the instrument is narrow to the rule rather than broken.
              let pairs = idlPairs ()

              let diffs =
                  pairs
                  |> List.fold (fun acc (label, before, after) -> versionProbeGoRed label before after acc) []

              Expect.isNonEmpty
                  diffs
                  "a classifier that never looks for removals MUST disagree with Versioning.classify"

              Expect.isLessThan
                  (List.length diffs)
                  (List.length pairs)
                  (sprintf
                      "the removal-blind classifier disagreed on %d of %d pairs — it must leave the purely additive ones alone"
                      (List.length diffs)
                      (List.length pairs))

              // and precisely which two: the removal and the rename, which is the whole of §15.4's
              // breaking column. Naming them is what separates "it went red" from "it went red for
              // the reason the theorem is about".
              let failedLabels =
                  diffs |> List.map (fun d -> d.Substring(0, d.IndexOf ':')) |> List.sort

              Expect.equal failedLabels [ "remove a tag"; "rename" ] "exactly the two breaking rows are where it loses"

          // ---- Phase 176 — the COLUMNAR op algebra: apply, canApply, invert, applyAll and toOps
          //      against the model ----

          testCase
              "the columnar oracle agrees with ColumnOps.apply, canApply and invert over generated tables and scripts"
          <| fun _ ->
              let t = colDifferential cellToModel 1760 60

              match t.Diffs with
              | d :: _ -> failtestf "the columnar oracle and production DISAGREE\n%s" d
              | [] ->
                  // Adequacy, per shape. Measured at 60 trials (360 probes): accepted 154,
                  // rejected 206, inverted 88, roundTripped 71, wfPre 278, wfPreserved 85, and 8
                  // of 17 round trips outside well-formedness failed. Each threshold sits below
                  // its measurement with room; they catch a generator that stops reaching a
                  // shape, not pin the numbers.
                  Expect.isGreaterThan t.Accepted 100 (sprintf "ops were accepted (accepted=%d)" t.Accepted)
                  Expect.isGreaterThan t.Rejected 100 (sprintf "ops were refused (rejected=%d)" t.Rejected)

                  Expect.isGreaterThan
                      t.Inverted
                      40
                      (sprintf "accepted invertible ops were inverted (inverted=%d)" t.Inverted)

                  Expect.isGreaterThan
                      t.RoundTripped
                      20
                      (sprintf "round trips were ASSERTED on well-formed pre-states (roundTripped=%d)" t.RoundTripped)

                  Expect.isGreaterThan
                      t.WfPre
                      60
                      (sprintf "the generator produced well-formed pre-states (wfPre=%d)" t.WfPre)

                  Expect.isGreaterThan
                      t.WfPreserved
                      20
                      (sprintf "the invariant was asserted on accepted structural ops (wfPreserved=%d)" t.WfPreserved)

                  Expect.equal t.Scripts 60 "every script was compared whole"

                  for cls in
                      [ "NoSuchColumn"
                        "DuplicateColumn"
                        "RowOutOfRange"
                        "CellTypeMismatch"
                        "ColumnLengthMismatch"
                        "RowShapeUnknownColumn"
                        "TransformRejected"
                        "NotInvertible" ] do
                      Expect.isTrue
                          (Set.contains cls t.Classes)
                          (sprintf "the sample reached a %s rejection (reached: %A)" cls t.Classes)

                  // The hypothesis earns its place: outside well-formedness the round trip is
                  // only counted, and the count of failures there must be NON-ZERO — otherwise
                  // `wf` is a hypothesis the theorem does not need and the ladder overstates.
                  Expect.isGreaterThan
                      t.RoundTripsOutsideWf
                      0
                      (sprintf "round trips were attempted outside wf (%d)" t.RoundTripsOutsideWf)

                  Expect.isGreaterThan
                      t.RoundTripFailuresOutsideWf
                      0
                      (sprintf
                          "some round trip FAILED on a malformed pre-state (%d of %d) — the well-formedness hypothesis is load-bearing"
                          t.RoundTripFailuresOutsideWf
                          t.RoundTripsOutsideWf)

                  // seeded, replayable
                  Expect.equal (colDifferential cellToModel 1760 60) t "same seed => identical tally"

          testCase
              "the columnar oracle agrees with ColumnOps.toOps and applyAll, and the diff reconstructs on well-formed pairs"
          <| fun _ ->
              let t = colDiffDifferential 1761 120

              match t.DDiffs with
              | d :: _ -> failtestf "the columnar diff oracle and production DISAGREE\n%s" d
              | [] ->
                  // Measured at 120 pairs: bothWf 67, granular 44, rebuild 23.
                  Expect.equal t.Pairs 120 "every pair was compared"
                  Expect.isGreaterThan t.BothWf 40 (sprintf "well-formed pairs were reached (bothWf=%d)" t.BothWf)

                  Expect.isGreaterThan
                      t.Granular
                      10
                      (sprintf "the column-granular branch was asserted (granular=%d)" t.Granular)

                  Expect.isGreaterThan t.Rebuild 10 (sprintf "the rebuild branch was asserted (rebuild=%d)" t.Rebuild)

          testCase
              "a columnar oracle handed a BLIND cell bridge DISAGREES with ColumnOps.apply — the measurement can fail"
          <| fun _ ->
              // The teeth. Under the blind bridge every present cell reaches the model as a
              // string, so the model's type check refuses what production accepts (an `Int` into
              // an int column) and accepts what production refuses (a `Str` into one). If this
              // ever passes, the type clauses have stopped reaching the comparison and the green
              // run above certifies nothing about them.
              let t = colDifferential blindCellToModel 1760 20

              Expect.isNonEmpty t.Diffs "a blind cell bridge MUST disagree with production"

              Expect.isTrue
                  (t.Diffs |> List.exists (fun d -> d.Contains "CellTypeMismatch"))
                  "and the disagreement is about the TYPE check, which is what the bridge blinded"

          testCase "a REFUSED insert has no inverse — `refused_insert_inverse_is_live` is now about `invert_pre181`"
          <| fun _ ->
              // Phase 176's finding, CLOSED by Phase 181, with the negative kept pinned. The
              // shipped `invert` refuses the refused insert; the pre-181 clause the model still
              // carries as `invert_pre181` answers a remove that SUCCEEDS at the pre-state and
              // takes the column that was already there. Both halves are asserted, so this goes
              // red either if the guard is reverted OR if the finding it closed stops being what
              // the closed finding was.
              let t: Table =
                  { Schema = [ "a", IntType; "b", IntType ]
                    Columns =
                      [ Column.create "a" IntType [ Int 1; Int 2 ]
                        Column.create "b" IntType [ Int 3; Int 4 ] ] }

              let op = InsertColumn(0, Column.create "a" IntType [ Int 9; Int 9 ])
              Expect.equal (ColumnOps.apply op t) (Error(DuplicateColumn "a")) "the insert is refused as a duplicate"

              match ColumnOps.invert op t with
              | Ok inv ->
                  failtestf "invert answered %s for a REFUSED insert — Phase 181's guard is gone" (prodColOpRender inv)
              | Error e ->
                  Expect.equal
                      (prodColRejRender e)
                      (prodColRejRender (DuplicateColumn "a"))
                      "and the refusal is the one `apply` gave, not a blanket NotInvertible"

              // the model agrees, on the shipped clause and on the pinned negative beside it
              let mt = tableToModel t
              let mop = colOpToModelWith cellToModel op

              Expect.equal
                  (ModelCol.apply modelEvaluator mop mt)
                  (ModelCol.Error(ModelCol.DuplicateColumn "a"))
                  "the model refuses the same insert"

              Expect.equal
                  (ModelCol.invert modelEvaluator mop mt)
                  (ModelCol.Error(ModelCol.DuplicateColumn "a"))
                  "and its guarded invert refuses it too"

              Expect.equal
                  (ModelCol.invert_pre181 mop mt)
                  (ModelCol.Ok(ModelCol.RemoveColumn "a"))
                  "while the pre-181 clause still derives the live remove — the finding, kept"

              // and that remove really was live: the column that was already there goes
              match ColumnOps.apply (RemoveColumn "a") t with
              | Error e -> failtestf "the pre-181 inverse's remove was refused (%s)" (prodColRejRender e)
              | Ok after ->
                  Expect.equal
                      (Table.columnNames after)
                      [ "b" ]
                      "the pre-existing column `a` would have GONE — what the guard now prevents"

          // ---- Phase 177 — the FUNCTION SEAM: the effect lattice, the function algebra and the
          //      capability registry against the model ----

          testCase
              "the function oracle agrees with Function.signature, apply, curry, compose and auditEffect over generated artifacts and argument sets"
          <| fun _ ->
              let t = fnDifferential readers 1770 200

              match t.FDiffs with
              | d :: _ -> failtestf "the function oracle and production DISAGREE\n%s" d
              | [] ->
                  // Adequacy, per shape. Measured at 200 artifacts: applied 73, applyRefused 127,
                  // curried 96, curryRefused 104, composed 17, composeRefused 183, nonTotal 11,
                  // auditErrors 92. Each threshold sits below its measurement with room; they
                  // catch a generator that stops reaching a shape, not pin the numbers.
                  Expect.equal t.Artifacts 200 "every artifact was compared"
                  Expect.isGreaterThan t.Applied 40 (sprintf "full applications were accepted (applied=%d)" t.Applied)

                  Expect.isGreaterThan
                      t.ApplyRefused
                      80
                      (sprintf "full applications were refused (applyRefused=%d)" t.ApplyRefused)

                  Expect.isGreaterThan
                      t.Curried
                      60
                      (sprintf "partial applications were accepted (curried=%d)" t.Curried)

                  Expect.isGreaterThan
                      t.CurryRefused
                      60
                      (sprintf "partial applications were refused (curryRefused=%d)" t.CurryRefused)

                  Expect.isGreaterThan t.Composed 8 (sprintf "compositions were accepted (composed=%d)" t.Composed)

                  Expect.isGreaterThan
                      t.ComposeRefused
                      100
                      (sprintf "compositions were refused (composeRefused=%d)" t.ComposeRefused)

                  Expect.isGreaterThan
                      t.NonTotal
                      5
                      (sprintf "non-total artifacts were reached (nonTotal=%d)" t.NonTotal)

                  Expect.isGreaterThan
                      t.AuditErrors
                      50
                      (sprintf "under-declared roots were reached (auditErrors=%d)" t.AuditErrors)

                  for cls in
                      [ "UnknownHoleAddr"
                        "ValueOutOfSpace"
                        "RequiredHolesUnbound"
                        "NotASlot"
                        "SlotKindMismatch"
                        "NonTotal" ] do
                      Expect.isTrue
                          (Set.contains cls t.FClasses)
                          (sprintf "the sample reached a %s refusal (reached: %A)" cls t.FClasses)

                  // seeded, replayable
                  Expect.equal (fnDifferential readers 1770 200) t "same seed => identical tally"

          testCase
              "the capability oracle agrees with Registry.register, enumerate, tryFind and dispatch, and Capability.validateArgs, over generated registries and invocations"
          <| fun _ ->
              let t = capDifferential readers 1771 150

              match t.CDiffs with
              | d :: _ -> failtestf "the capability oracle and production DISAGREE\n%s" d
              | [] ->
                  // Measured at 150 registries: registered 172, dupRefused 51, dispatched 33,
                  // noSuch 270, validated 45, refused 71, bodyFailed 12, refusedWithoutBody 341.
                  Expect.isGreaterThan
                      t.Registered
                      100
                      (sprintf "capabilities were registered (registered=%d)" t.Registered)

                  Expect.isGreaterThan
                      t.DupRefused
                      20
                      (sprintf "duplicate registrations were refused (dupRefused=%d)" t.DupRefused)

                  Expect.isGreaterThan
                      t.Dispatched
                      15
                      (sprintf "invocations were dispatched and ran (dispatched=%d)" t.Dispatched)

                  Expect.isGreaterThan t.NoSuch 150 (sprintf "unregistered ids were refused (noSuch=%d)" t.NoSuch)
                  Expect.isGreaterThan t.Validated 20 (sprintf "argument sets were accepted (validated=%d)" t.Validated)
                  Expect.isGreaterThan t.Refused 40 (sprintf "argument sets were refused (refused=%d)" t.Refused)

                  Expect.isGreaterThan
                      t.BodyFailed
                      5
                      (sprintf "bodies failed and were named (bodyFailed=%d)" t.BodyFailed)

                  Expect.equal
                      t.BodyRan
                      (t.Dispatched + t.BodyFailed)
                      "the body ran exactly on the invocations validation passed"

                  Expect.isGreaterThan
                      t.RefusedWithoutBody
                      200
                      (sprintf
                          "refusals were checked for a body that did not run (refusedWithoutBody=%d)"
                          t.RefusedWithoutBody)

                  for cls in
                      [ "NoSuchCapability"
                        "UnknownArg"
                        "ArgOutOfSpace"
                        "RequiredArgsUnbound"
                        "UninvocableArg"
                        "BodyFailed" ] do
                      Expect.isTrue
                          (Set.contains cls t.CClasses)
                          (sprintf "the sample reached a %s refusal (reached: %A)" cls t.CClasses)

                  Expect.equal (capDifferential readers 1771 150) t "same seed => identical tally"

          testCase
              "a capability oracle handed a BLIND int reader DISAGREES with Capability.validateArgs and Function.apply — the measurement can fail"
          <| fun _ ->
              // The teeth. Under the blind reader every int-ranged value is out of space to the
              // model, so it refuses what production accepts. If this ever passes, the space
              // check has stopped reaching the comparison and the green runs above certify
              // nothing about it.
              let c = capDifferential blindReaders 1771 60
              Expect.isNonEmpty c.CDiffs "a blind int reader MUST disagree with production on the seam"

              Expect.isTrue
                  (c.CDiffs |> List.exists (fun d -> d.Contains "ArgOutOfSpace"))
                  "and the disagreement is about the SPACE check, which is what the reader blinded"

              let f = fnDifferential blindReaders 1770 60
              Expect.isNonEmpty f.FDiffs "a blind int reader MUST disagree with production on the algebra"

              Expect.isTrue
                  (f.FDiffs |> List.exists (fun d -> d.Contains "ValueOutOfSpace"))
                  "and the disagreement is about the value-space check"

          testCase
              "no handler runs on a refused dispatch — `unregistered_refused` and `validate_before_invoke`, on the shipped seam"
          <| fun _ ->
              // The two theorems' statements instantiated on production: an unregistered id and
              // a rejected argument set each return the typed refusal with the body untouched,
              // and the result is the same under a body that would have failed.
              // The template's slot hole is dropped from the invocable signature: a required
              // slot entry refuses every argument list (`slot_hole_uninvocable`, asserted below).
              let full =
                  { Function.signature artw "f" (template ()) with
                      Effect = Effect.pureDeterministic }

              let sg =
                  { full with
                      Holes = full.Holes |> List.filter (fun e -> e.Kind <> "slot") }

              let cap = Capability.create "cap-t" sg Server

              let reg =
                  match Registry.register cap Registry.empty with
                  | Ok r -> r
                  | Error e -> failtestf "register refused: %A" e

              let ran = ref 0

              let body (_: Capability) () =
                  ran.Value <- ran.Value + 1
                  Ready "ran"

              let failing (_: Capability) () =
                  ran.Value <- ran.Value + 1
                  Failed "boom"

              let pending (_: Capability) () =
                  ran.Value <- ran.Value + 1
                  Pending

              // unregistered_refused
              Expect.equal
                  (Registry.dispatch reg "cap-u" [ "tpl/t", "x" ] body)
                  (Error(NoSuchCapability("cap-u", [ "cap-t" ])))
                  "an unregistered id is the typed refusal, naming what IS registered"

              Expect.equal
                  (Registry.dispatch reg "cap-u" [ "tpl/t", "x" ] failing)
                  (Error(NoSuchCapability("cap-u", [ "cap-t" ])))
                  "and the same refusal under a failing body"

              // validate_before_invoke — out of space, unknown, slot-targeted, required-unbound
              for args in
                  [ [ "tpl/t", "x"; "tpl/c", "99" ]
                    [ "tpl/t", "x"; "tpl/c", "1"; "tpl/zz", "1" ]
                    [ "tpl/t", "x"; "tpl/c", "1"; "tpl/s", "para" ]
                    [ "tpl/t", "x" ] ] do
                  let a = Registry.dispatch reg "cap-t" args body
                  let b = Registry.dispatch reg "cap-t" args failing
                  Expect.isTrue (Result.isError a) (sprintf "the set %A is refused" args)
                  Expect.equal a b "and the refusal is the same under a failing body"

                  Expect.equal
                      a
                      (Capability.validateArgs cap args |> Result.map (fun () -> Ready "unreachable"))
                      "and it IS the validation's refusal"

              Expect.equal ran.Value 0 "no body ran on any refusal"

              // slot_hole_uninvocable — the finding, on the shipped seam: the capability declared
              // over the WHOLE template (its slot hole required and spaceless) is registered,
              // enumerated, and refused on every argument list, the body never running.
              let slotCap = Capability.create "cap-slot" full Server

              let reg2 =
                  match Registry.register slotCap reg with
                  | Ok r -> r
                  | Error e -> failtestf "register refused: %A" e

              Expect.equal
                  (Registry.enumerate reg2 |> List.map (fun c -> c.Id))
                  [ "cap-slot"; "cap-t" ]
                  "the slot-bearing capability enumerates like any other"

              for args in
                  [ []
                    [ "tpl/t", "x"; "tpl/c", "3" ]
                    [ "tpl/t", "x"; "tpl/c", "3"; "tpl/s", "para" ]
                    [ "tpl/s", "" ] ] do
                  Expect.isTrue
                      (Result.isError (Registry.dispatch reg2 "cap-slot" args body))
                      (sprintf "the slot-bearing capability refuses %A" args)

              Expect.equal ran.Value 0 "and no body ran on any of those either"

              // and the accepted set runs it, once
              let accepted = [ "tpl/t", "x"; "tpl/c", "3" ]

              Expect.equal
                  (Registry.dispatch reg "cap-t" accepted body)
                  (Ok(Ready "ran"))
                  "an accepted set runs the body"

              Expect.equal ran.Value 1 "exactly once"

              // Phase 210 — invoke_never_ok_failed / dispatch_never_ok_failed on the shipped seam:
              // past an accepted validation the envelope's three cases go to exactly the three
              // outcomes, and `Ok(Failed _)` is not among them.
              Expect.equal
                  (Registry.dispatch reg "cap-t" accepted pending)
                  (Ok Pending: Result<Deferred<string>, InvokeError>)
                  "a pending body stays pending inside the Ok"

              Expect.equal
                  (Registry.dispatch reg "cap-t" accepted failing)
                  (Error(BodyFailed "boom"): Result<Deferred<string>, InvokeError>)
                  "a failing body is the typed BodyFailed, never Ok(Failed _)"

              Expect.equal ran.Value 3 "and each of those ran its body once"

              // and the model says the same through the same theorems' clauses
              let mreg =
                  match ModelCap.register (capToModel cap) ModelCap.empty with
                  | ModelCap.Ok r -> r
                  | ModelCap.Error _ -> failtest "the model refused the registration"

              Expect.equal
                  (ModelCap.dispatch readers mreg "cap-u" [ "tpl/t", "x" ] (fun _ () -> ModelCap.Ready "ran"))
                  (ModelCap.Error(ModelCap.NoSuchCapability("cap-u", [ "cap-t" ])))
                  "the model refuses the unregistered id the same way"

              Expect.equal (ModelCap.ids (ModelCap.enumerate mreg)) [ "cap-t" ] "and enumerates the registered one"

          // ---- Phase 187: the data-acquisition seam (proofs/Query.fst) ----

          testCase
              "the query oracle agrees with QueryRegistry.register, enumerate, tryFind and dispatch, and Query.validateParams, invocationKey and determinismTag, over generated registries, declarations and argument sets"
          <| fun _ ->
              let t = queryDifferential queryToModel queryRenderers 1871 300

              match t.QDiffs with
              | d :: _ -> failtestf "the query oracle and production DISAGREE\n%s" d
              | [] ->
                  // Measured at 300 registries: registered 337, dupRefused 87, repeated-name
                  // declarations 89; settled 64, pending 24, noSuch 532, validated 110, refused 108,
                  // execFailed 22, refusedWithoutResolver 640; keys 218 (40 over a Null binding, 57
                  // held still under a reordering).
                  Expect.isGreaterThan
                      t.QRegistered
                      200
                      (sprintf "queries were registered (registered=%d)" t.QRegistered)

                  Expect.isGreaterThan
                      t.QDupRefused
                      40
                      (sprintf "duplicate registrations were refused (dupRefused=%d)" t.QDupRefused)

                  Expect.isGreaterThan
                      t.QRepeatedParamDecls
                      40
                      (sprintf
                          "declarations repeating a param name arose, so first-wins was compared (repeated=%d)"
                          t.QRepeatedParamDecls)

                  Expect.isGreaterThan t.QSettled 30 (sprintf "dispatches settled (settled=%d)" t.QSettled)

                  Expect.isGreaterThan
                      t.QStillPending
                      10
                      (sprintf "dispatches stayed pending inside the Ok (pending=%d)" t.QStillPending)

                  Expect.isGreaterThan t.QNoSuch 200 (sprintf "unregistered ids were refused (noSuch=%d)" t.QNoSuch)

                  Expect.isGreaterThan
                      t.QValidated
                      50
                      (sprintf "argument sets were accepted (validated=%d)" t.QValidated)

                  Expect.isGreaterThan t.QRefused 50 (sprintf "argument sets were refused (refused=%d)" t.QRefused)

                  Expect.isGreaterThan
                      t.QExecFailed
                      10
                      (sprintf "resolvers failed and were named (execFailed=%d)" t.QExecFailed)

                  Expect.equal
                      t.QResolverRan
                      (t.QSettled + t.QStillPending + t.QExecFailed)
                      "the resolver ran exactly on the invocations validation passed"

                  Expect.isGreaterThan
                      t.QRefusedWithoutResolver
                      300
                      (sprintf
                          "refusals were checked for a resolver that did not run (refusedWithoutResolver=%d)"
                          t.QRefusedWithoutResolver)

                  Expect.isGreaterThan t.QKeys 100 (sprintf "capture keys were compared (keys=%d)" t.QKeys)

                  Expect.isGreaterThan
                      t.QKeysWithNull
                      20
                      (sprintf "capture keys over a Null binding were compared (withNull=%d)" t.QKeysWithNull)

                  Expect.isGreaterThan
                      t.QKeysPermuted
                      25
                      (sprintf
                          "the shipped key was held still under a reordering of distinct names (permuted=%d)"
                          t.QKeysPermuted)

                  for cls in
                      [ "NoSuchQuery"
                        "UnknownParam"
                        "ParamTypeMismatch"
                        "RequiredParamsUnbound"
                        "ExecutionFailed" ] do
                      Expect.isTrue
                          (Set.contains cls t.QClasses)
                          (sprintf "the sample reached a %s refusal (reached: %A)" cls t.QClasses)

                  Expect.equal (queryDifferential queryToModel queryRenderers 1871 300) t "same seed => identical tally"

          testCase
              "a query oracle behind a FORGETFUL bridge or an ORDER-BLIND comparator DISAGREES with Query.validateParams and Query.invocationKey — the measurement can fail"
          <| fun _ ->
              // The teeth, one set per half of the model. Behind the forgetful bridge every param
              // crosses as optional, so the model accepts what production refuses at step 2. Under
              // the order-blind comparator the model's sort leaves the caller's order standing, so
              // its key moves where production's does not. If either ever passes, that half of the
              // comparison has stopped reaching the clause and the green run above certifies
              // nothing about it.
              let forgetful = queryDifferential (queryToModelWith false) queryRenderers 1871 80
              Expect.isNonEmpty forgetful.QDiffs "a bridge that forgets `Required` MUST disagree with production"

              Expect.isTrue
                  (forgetful.QDiffs |> List.exists (fun d -> d.Contains "RequiredParamsUnbound"))
                  "and the disagreement is about the required-params step, which is what the bridge forgot"

              let blind = queryDifferential queryToModel orderBlindRenderers 1871 80
              Expect.isNonEmpty blind.QDiffs "an order-blind comparator MUST disagree with production"

              Expect.isTrue
                  (blind.QDiffs |> List.forall (fun d -> d.Contains "invocationKey differs"))
                  "and every disagreement is about the capture key — the comparator reaches nothing else"

          testCase
              "no resolver runs on a refused dispatch — `unregistered_refused` and `validate_before_resolve`, on the shipped seam"
          <| fun _ ->
              // The two theorems' statements instantiated on production: an unregistered id and a
              // rejected argument set each return the typed refusal with the resolver untouched,
              // and the result is the same under a resolver that would have failed.
              let q: Query =
                  { Id = "q-t"
                    Params =
                      [ { Name = "p0"
                          Type = IntType
                          Required = true } ]
                    ResultSchema = [ "n", IntType ]
                    Effect =
                      { Host = ReadsHost
                        Determinism = Network }
                    Source = Ref "src-t"
                    TimeoutMs = None
                    PageSize = None }

              let reg =
                  match QueryRegistry.register q QueryRegistry.empty with
                  | Ok r -> r
                  | Error e -> failtestf "registration refused: %A" e

              let ran = ref 0

              let settling (_: Query) =
                  ran.Value <- ran.Value + 1
                  Ready(queryResultOf 1)

              let failing (_: Query) =
                  ran.Value <- ran.Value + 1
                  Failed "boom"

              let pending (_: Query) : Deferred<QueryResult> =
                  ran.Value <- ran.Value + 1
                  Pending

              // `unregistered_refused`
              for resolve in [ settling; failing; pending ] do
                  Expect.equal
                      (QueryRegistry.dispatch reg "q-u" [ "p0", Cell.Int 1 ] resolve)
                      (Error(NoSuchQuery("q-u", [ "q-t" ])))
                      "an unregistered id is NoSuchQuery, naming the id and the registry's ids, under every resolver"

              // `validate_before_resolve`, one argument set per refusal class
              for args in
                  [ [ "p0", Cell.Str "nope" ]
                    [ "zz", Cell.Int 1 ]
                    []
                    [ "p0", Cell.Int 1; "zz", Cell.Null ] ] do
                  let refusal = Query.validateParams q args
                  Expect.isError refusal (sprintf "validation rejects %A" args)

                  for resolve in [ settling; failing; pending ] do
                      Expect.equal
                          (QueryRegistry.dispatch reg "q-t" args resolve)
                          (refusal |> Result.map (fun () -> Pending))
                          (sprintf "a rejected set %A IS validation's refusal, under every resolver" args)

              Expect.equal ran.Value 0 "and no resolver ran on any of them"

              // past an accepted validation the resolver's three answers ride out as the three outcomes
              let accepted = [ "p0", Cell.Int 1 ]

              Expect.equal
                  (QueryRegistry.dispatch reg "q-t" accepted settling)
                  (Ok(Ready(queryResultOf 1)))
                  "a settling resolver settles"

              Expect.equal
                  (QueryRegistry.dispatch reg "q-t" accepted pending)
                  (Ok Pending)
                  "a pending one stays pending"

              Expect.equal
                  (QueryRegistry.dispatch reg "q-t" accepted failing)
                  (Error(ExecutionFailed("boom", [])))
                  "a failing resolver is the typed ExecutionFailed, never Ok(Failed _)"

              Expect.equal ran.Value 3 "and each of those ran its resolver once"

              // and the model says the same through the same theorems' clauses
              let mreg =
                  match ModelQuery.register (queryToModel q) ModelQuery.empty with
                  | ModelQuery.Ok r -> r
                  | ModelQuery.Error _ -> failtest "the model refused the registration"

              Expect.equal
                  (ModelQuery.dispatch mreg "q-u" (qArgsToModel accepted) (fun _ -> ModelQuery.Ready(queryResultOf 1)))
                  (ModelQuery.Error(ModelQuery.NoSuchQuery("q-u", [ "q-t" ])))
                  "the model refuses the unregistered id the same way"

              Expect.equal (ModelQuery.ids (ModelQuery.enumerate mreg)) [ "q-t" ] "and enumerates the registered one"

          testCase "the two findings hold on the shipped seam — `all_null_accepted` and `key_collision`"
          <| fun _ ->
              // THE FINDINGS, pinned on production so that a fix turns this case red and sends its
              // author to the two ladder rows (`query-all-null-accepted`, `query-key-collision`)
              // and the README's theorem 12 section, which is where each is argued.
              let q: Query =
                  { Id = "q-f"
                    Params =
                      [ { Name = "a"
                          Type = StringType
                          Required = true }
                        { Name = "b"
                          Type = StringType
                          Required = false } ]
                    ResultSchema = [ "n", IntType ]
                    Effect =
                      { Host = ReadsHost
                        Determinism = Network }
                    Source = Ref "src-f"
                    TimeoutMs = None
                    PageSize = None }

              let mq = queryToModel q

              Expect.equal
                  mq.q_params
                  ModelQuery.collision_params
                  "the declaration IS the one the model exhibits the collision on"

              // `all_null_accepted`: a REQUIRED param bound to Null passes validation, and the
              // resolver runs — `Required` constrains the presence of a name, never of a value.
              let nulls = [ "a", Cell.Null; "b", Cell.Null ]

              Expect.equal
                  (qArgsToModel nulls)
                  (ModelQuery.nulls_of mq.q_params)
                  "the argument set IS the model's `nulls_of`"

              Expect.equal
                  (Query.validateParams q nulls)
                  (Ok())
                  "every param bound to Null is ACCEPTED, the required one included"

              Expect.equal
                  (Query.validateParams q [ "a", Cell.Null ])
                  (Ok())
                  "and so is the required param bound to Null on its own"

              Expect.equal
                  (Query.validateParams q [])
                  (Error(RequiredParamsUnbound [ "a" ]))
                  "where leaving the same name out is refused — the two are told apart by the NAME alone"

              let ran = ref false

              Expect.equal
                  (Query.invoke q [ "a", Cell.Null ] (fun _ ->
                      ran.Value <- true
                      Pending))
                  (Ok Pending)
                  "so the resolver is reached with its required param absent"

              Expect.isTrue ran.Value "and it ran"

              // `key_collision`: two DIFFERENT accepted argument sets, one canonical string.
              let one = [ "a", Cell.Str "1b=s2" ]
              let two = [ "a", Cell.Str "1"; "b", Cell.Str "2" ]
              Expect.equal (qArgsToModel one) ModelQuery.collision_one "the first set IS the model's exhibit"
              Expect.equal (qArgsToModel two) ModelQuery.collision_two "the second set IS the model's exhibit"
              Expect.equal (Query.validateParams q one) (Ok()) "the first set is accepted"
              Expect.equal (Query.validateParams q two) (Ok()) "the second set is accepted"
              Expect.notEqual one two "they are different argument sets"

              Expect.equal
                  (Query.invocationKey q one)
                  (Query.invocationKey q two)
                  "and they share a capture key — the canonical string has no separator between bindings"

              Expect.equal
                  (ModelQuery.canonical queryRenderers ModelQuery.collision_one)
                  (ModelQuery.canonical queryRenderers ModelQuery.collision_two)
                  "because the PRE-IMAGE is already the same string, before any hash is taken"

              Expect.equal
                  (ModelQuery.invocation_key queryRenderers mq ModelQuery.collision_one)
                  (Query.invocationKey q one)
                  "and the model's key for it is production's"

          // ---- Phase 186: the incremental promise (proofs/Propagation.fst) ----

          testCase
              "the propagation oracle agrees with Propagation.dependents, dirtyFromChangedIds, staleSet, eval and evalFrom, over generated dependency maps and change sets"
          <| fun _ ->
              let t = propDifferential depsToModel 1861 400

              match t.PDiffs with
              | d :: _ -> failtestf "the propagation oracle and production DISAGREE\n%s" d
              | [] ->
                  // Measured at 400 graphs: cyclic 54, dangling 67, dirty 1031, clean 718,
                  // reused 461, absentRecomputed 27, agreed 354, nodeFailed 84, unknownRefused 46.
                  Expect.equal t.Graphs 400 "every trial built a graph"

                  Expect.equal
                      t.OrdersDistinct
                      t.Graphs
                      "sort's Order held no id twice on any graph — the one fact the agreement theorem assumes of it"

                  Expect.isGreaterThan t.CyclicGraphs 25 (sprintf "cyclic graphs arose (cyclic=%d)" t.CyclicGraphs)

                  Expect.isGreaterThan
                      t.DanglingGraphs
                      30
                      (sprintf "graphs with a dangling read arose (dangling=%d)" t.DanglingGraphs)

                  Expect.isGreaterThan t.DirtyNodes 500 (sprintf "dirty nodes arose (dirty=%d)" t.DirtyNodes)
                  Expect.isGreaterThan t.CleanNodes 300 (sprintf "clean nodes arose (clean=%d)" t.CleanNodes)

                  Expect.isGreaterThan
                      t.Reused
                      200
                      (sprintf "evalFrom REUSED prior values, so minimality was exercised (reused=%d)" t.Reused)

                  Expect.isGreaterThan
                      t.AbsentRecomputed
                      5
                      (sprintf "a clean id absent from prior was recomputed (absentRecomputed=%d)" t.AbsentRecomputed)

                  Expect.isGreaterThan
                      t.Agreed
                      200
                      (sprintf "evalFrom was eval on the shipped driver (agreed=%d)" t.Agreed)

                  Expect.isGreaterThan
                      t.NodeFailed
                      20
                      (sprintf "a failing evaluator was reached on both paths (nodeFailed=%d)" t.NodeFailed)

                  Expect.isGreaterThan
                      t.UnknownRefused
                      20
                      (sprintf "an unknown change was refused (unknownRefused=%d)" t.UnknownRefused)

                  Expect.equal (propDifferential depsToModel 1861 400) t "same seed => identical tally"

          testCase
              "a propagation oracle handed a BLIND deps bridge DISAGREES with Propagation.dirtyFromChangedIds and evalFrom — the measurement can fail"
          <| fun _ ->
              // The teeth. A bridge that drops each node's last read hands the model a smaller
              // graph, so its dependents map, its dirty set and what it re-evaluates all fall
              // short of production's. If this ever passes, the dependency map has stopped
              // reaching the comparison and the green run above certifies nothing about it.
              let t = propDifferential blindDepsBridge 1861 120
              Expect.isNonEmpty t.PDiffs "a bridge that loses read edges MUST disagree with production"

              Expect.isTrue
                  (t.PDiffs |> List.exists (fun d -> d.Contains ": dirty production="))
                  "and the disagreement reaches the DIRTY SET, which is what the lost edge shrinks"

              Expect.isTrue
                  (t.PDiffs |> List.exists (fun d -> d.Contains ": evalFrom production="))
                  "and it reaches evalFrom's RESULT: since Phase 209 the witness still names the read the bridge dropped, so the model refuses it as undeclared where production (reading the real map) does not"

          testCase
              "evalFrom is eval under the evaluator contract, and an undeclared read is REFUSED by both drivers — `evalfrom_agrees`, `evalfrom_minimal`, `undeclared_refused` and `evalfrom_unknown_refused`, on the shipped driver"
          <| fun _ ->
              // b DECLARES that it reads a; c reads nothing.
              let declared = Map.ofList [ "a", Set.empty; "b", Set.singleton "a"; "c", Set.empty ]

              // The same graph with the declaration MISSING: b reads a and does not say so.
              let undeclared = Map.ofList [ "a", Set.empty; "b", Set.empty; "c", Set.empty ]

              // An evaluator that reads `a` from `b` whatever the map says — conforming over
              // `declared`, a contract violation over `undeclared`.
              let spec (aBase: int) (resolve: string -> int option) (id: string) : Result<int, string> =
                  match id with
                  | "a" -> Ok aBase
                  | "b" -> Ok(10 + (resolve "a" |> Option.defaultValue 0))
                  | _ -> Ok 7

              // What `spec` READS at each node — production observes this off its own resolver; here
              // it is read off the same three lines the evaluator is written in.
              let specReads: ModelProp.read_witness = fun id -> if id = "b" then [ "a" ] else []

              let run (deps: Map<string, Set<string>>) =
                  let ran = ResizeArray()

                  let prior =
                      match Propagation.eval (spec 1) deps with
                      | Ok o -> o.Values
                      | Error e -> failtestf "the prior eval failed: %A" e

                  let incr =
                      Propagation.evalFrom (propProdEvaluator (spec 2) ran) prior (Set.singleton "a") deps

                  incr, Propagation.eval (spec 2) deps, List.ofSeq ran, prior

              // `evalfrom_agrees`: the contract holds, and the incremental result IS the full one
              let incr, full, ran, prior = run declared
              Expect.equal incr full "under the contract evalFrom is eval"
              // `evalfrom_minimal`: exactly the dirty ids ran — c was reused, never evaluated
              Expect.equal ran [ "a"; "b" ] "and only the dirty ids were evaluated"

              // PHASE 186'S FINDING, NOW CLOSED. The same evaluator over a map that does not declare
              // b's read used to run without complaint: the dirty set was {a}, b kept its stale value
              // and `evalFrom` was NOT `eval`. Since Phase 209 the resolver answers only for
              // `deps[b]`, which is empty, so the read is refused as data by BOTH drivers — and
              // `eval` refuses first, which is why there is no `prior` to reuse a stale value from.
              Expect.equal
                  (Propagation.eval (spec 1) undeclared)
                  (Error(Propagation.EvalUndeclaredRead("b", "a")))
                  "the full driver refuses the undeclared read, naming the node and the read"

              let ranU = ResizeArray()

              Expect.equal
                  (Propagation.evalFrom (propProdEvaluator (spec 2) ranU) Map.empty (Set.singleton "a") undeclared)
                  (Error(Propagation.EvalUndeclaredRead("b", "a")))
                  "and the incremental driver refuses it identically wherever it recomputes the node"

              // The walk stops AT the violating node, so `b` is the last id the recorder saw. Not an
              // exact list: `undeclared` has no edges at all, so `sort`'s order over its three nodes
              // is Tarjan's business and asserting a permutation of it would be asserting
              // `propagation-order-distinct`'s neighbour rather than this phase's refusal.
              Expect.equal
                  (List.tryLast (List.ofSeq ranU))
                  (Some "b")
                  "the evaluator ran at the violating node and the walk stopped there"

              // THE ONE QUALIFIER, asserted rather than assumed: `evalFrom` invokes the evaluator
              // only where it recomputes, so a violating node that is CLEAN and present in `prior`
              // is reused and its violation is not seen here. That is `evalfrom_minimal`, not a hole
              // — the two assertions above show `eval` refuses, so a `prior` of this shape cannot
              // have come from `eval` over this map, which is `evalfrom_agrees`' own premise.
              let priorU = Map.ofList [ "a", 1; "b", 11; "c", 7 ]
              let ranClean = ResizeArray()

              Expect.equal
                  (Propagation.evalFrom (propProdEvaluator (spec 2) ranClean) priorU (Set.singleton "a") undeclared)
                  (Ok
                      { Values = Map.ofList [ "a", 2; "b", 11; "c", 7 ]
                        Cyclic = [] })
                  "a violating node that is clean AND in prior is reused, so nothing invokes it and nothing refuses"

              Expect.equal (List.ofSeq ranClean) [ "a" ] "only the dirty id ran"

              // …and the model says the same on all of it, through the same clauses: the refusal is
              // the driver's, and the model carries it rather than modelling a driver production no
              // longer has.
              let modelRun (deps: Map<string, Set<string>>) (p: Map<string, int>) (chg: string list) =
                  let mtopo = topoToModel (Propagation.sort deps)

                  ModelProp.eval_from
                      (propModelEvaluator (spec 2))
                      specReads
                      (Map.toList p)
                      chg
                      (depsToModel deps)
                      mtopo
                  |> modelOutcomeRender

              Expect.equal
                  (modelRun declared prior [ "a" ])
                  (prodOutcomeRender incr)
                  "the model agrees under the contract"

              Expect.equal
                  (modelRun undeclared Map.empty [ "a" ])
                  "EvalUndeclaredRead b a"
                  "and the model refuses the undeclared read in the same words"

              Expect.equal
                  (modelRun undeclared priorU [ "a" ])
                  (prodOutcomeRender (Propagation.evalFrom (spec 2) priorU (Set.singleton "a") undeclared))
                  "and agrees on the reuse qualifier too"

              // the qualifier in `evalfrom_minimal`: a CLEAN id absent from prior is recomputed
              let ranHole = ResizeArray()

              Propagation.evalFrom
                  (propProdEvaluator (spec 2) ranHole)
                  (Map.remove "c" prior)
                  (Set.singleton "a")
                  declared
              |> ignore

              Expect.equal (List.ofSeq ranHole) [ "a"; "b"; "c" ] "a clean id absent from prior IS evaluated"

              // `evalfrom_unknown_refused`: nothing runs, and the refusal names the unknown id
              let ranUnknown = ResizeArray()

              Expect.equal
                  (Propagation.evalFrom
                      (propProdEvaluator (spec 2) ranUnknown)
                      prior
                      (Set.ofList [ "a"; "nope" ])
                      declared)
                  (Error(Propagation.EvalUnknownChange [ "nope" ]))
                  "an unknown change is the typed refusal naming it"

              Expect.isEmpty ranUnknown "and no evaluator ran" ]
