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

                  Expect.isNonEmpty swept.Disagreements "the blind bridge loses over the generated sample as well" ]
