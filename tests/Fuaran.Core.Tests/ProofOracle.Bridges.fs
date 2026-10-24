module Fuaran.Core.Tests.ProofOracle.Bridges

// The bridges, generators, tallies and renderers more than one `Proofs.Oracle` differential reads:
// Core's types as an extracted model (`proofs/oracle/*.fs`) reads them, and the model's back. A bridge
// that one model alone reads lives beside that model's differential, in `ProofOracle.<Model>Tests.fs`.

open System.IO
open Expecto

// Phase 177 — the extracted FUNCTION-SEAM model, bound the same way and for the same reason:
// production's `Fuaran.Core.Capability` module shares the model's name once `Fuaran.Core` is open.
module ModelCap = Capability

// Phase 428 — the unit algebra's model. Aliased for the same reason: production's `Unit` module
// (`Fuaran.Core.Unit`) is opened below, and the oracle's `Unit` would be shadowed by it.
module ModelUnit = Unit

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.CapabilityOracle
open Fuaran.Core.Tests.CanonOracle
open Fuaran.Core.Tests.ColumnOracle
open Fuaran.Core.Tests.DecodeOracle

// ---------------------------------------------------------------------------
//  Phase 535 — THE DIFFERENTIAL HARNESS. Every `Proofs.Oracle` differential is the same loop:
//  draw a case from a seeded pool, ask production and the extracted model, render both answers
//  through the DU's render pair, record a disagreement when the renders differ, and count what the
//  pool reached so an agreement that met nothing cannot read green. The October split left that
//  loop copied once per model, each copy with its own tally record. It is written once here.
//
//  The tally is DECLARED: a differential names its counters, its reached-sets and its finding
//  classes up front, and reading or writing an undeclared name fails the test. Without that a
//  misspelt counter would read as zero, and an assertion `count a = count b` over two misspellings
//  would hold vacuously.
//
//  The go-red arm is `Differential.expectLoses`: the same differential, handed a perturbed model or
//  bridge, must disagree. A model whose differential cannot lose certifies nothing.
// ---------------------------------------------------------------------------

/// The finding class a comparison through a render pair records under.
[<Literal>]
let Disagreement = "disagreement"

/// One differential's evidence: every finding under its class (newest first, as recorded), the
/// named counters, and the named sets of classes the pool reached. Built and read through `Tally`.
type Tally =
    { Findings: Map<string, string list>
      Counts: Map<string, int>
      Reached: Map<string, Set<string>> }

module Tally =
    let private undeclared (what: string) (name: string) (declared: seq<string>) =
        failtestf "the tally declares no %s %A (declared: %s)" what name (String.concat ", " declared)

    /// A tally counting `counters`, recording `Disagreement` findings, reaching no set yet.
    let empty (counters: string list) : Tally =
        { Findings = Map.ofList [ Disagreement, [] ]
          Counts = counters |> List.map (fun c -> c, 0) |> Map.ofList
          Reached = Map.empty }

    /// Declares the named reached-sets.
    let sets (names: string list) (t: Tally) : Tally =
        { t with
            Reached = names |> List.fold (fun m n -> Map.add n Set.empty m) t.Reached }

    /// Declares further finding classes beside `Disagreement`.
    let classes (names: string list) (t: Tally) : Tally =
        { t with
            Findings = names |> List.fold (fun m n -> Map.add n [] m) t.Findings }

    let add (name: string) (n: int) (t: Tally) : Tally =
        match Map.tryFind name t.Counts with
        | Some c ->
            { t with
                Counts = Map.add name (c + n) t.Counts }
        | None -> undeclared "counter" name t.Counts.Keys

    let bump (name: string) (t: Tally) : Tally = add name 1 t

    /// `bump` when `cond` holds.
    let bumpIf (cond: bool) (name: string) (t: Tally) : Tally = if cond then bump name t else t

    let reach (set: string) (value: string) (t: Tally) : Tally =
        match Map.tryFind set t.Reached with
        | Some s ->
            { t with
                Reached = Map.add set (Set.add value s) t.Reached }
        | None -> undeclared "reached-set" set t.Reached.Keys

    let record (cls: string) (message: string) (t: Tally) : Tally =
        match Map.tryFind cls t.Findings with
        | Some ms ->
            { t with
                Findings = Map.add cls (message :: ms) t.Findings }
        | None -> undeclared "finding class" cls t.Findings.Keys

    let disagree (message: string) (t: Tally) : Tally = record Disagreement message t

    let count (name: string) (t: Tally) : int =
        match Map.tryFind name t.Counts with
        | Some c -> c
        | None -> undeclared "counter" name t.Counts.Keys

    let reached (set: string) (t: Tally) : Set<string> =
        match Map.tryFind set t.Reached with
        | Some s -> s
        | None -> undeclared "reached-set" set t.Reached.Keys

    /// The findings of one class, in the order they were found.
    let findings (cls: string) (t: Tally) : string list =
        match Map.tryFind cls t.Findings with
        | Some ms -> List.rev ms
        | None -> undeclared "finding class" cls t.Findings.Keys

    /// The disagreements, in the order they were found.
    let disagreements (t: Tally) : string list = findings Disagreement t

    let private goRedClass (instrument: string) = "go-red:" + instrument

    /// Declares the named GO-RED instruments a differential runs IN THE SAME PASS as its agreement:
    /// a perturbed bridge or model asked beside the real one. Its losses are findings the go-red
    /// arm reads and the agreement arm does not.
    let goRed (instruments: string list) (t: Tally) : Tally =
        classes (List.map goRedClass instruments) t

    /// Records one loss of the in-pass `instrument`.
    let lose (instrument: string) (message: string) (t: Tally) : Tally =
        record (goRedClass instrument) message t

    /// The losses of the in-pass `instrument`, in the order they were found.
    let losses (instrument: string) (t: Tally) : string list = findings (goRedClass instrument) t

    /// Two tallies of the same declaration, summed.
    let merge (a: Tally) (b: Tally) : Tally =
        { Findings = Map.map (fun k ms -> (Map.tryFind k b.Findings |> Option.defaultValue []) @ ms) a.Findings
          Counts = Map.map (fun k c -> c + (Map.tryFind k b.Counts |> Option.defaultValue 0)) a.Counts
          Reached =
            Map.map (fun k s -> Set.union s (Map.tryFind k b.Reached |> Option.defaultValue Set.empty)) a.Reached }

/// The render pair of one answer DU: production's answer and the model's, each as the text the
/// comparison reads. Two answers agree exactly when their renders are equal.
type Differential<'P, 'M> =
    { Production: 'P -> string
      Model: 'M -> string }

module Differential =
    let pair (production: 'P -> string) (model: 'M -> string) : Differential<'P, 'M> =
        { Production = production
          Model = model }

    /// Both sides already answer in text.
    let text: Differential<string, string> = pair id id

    /// Both sides answer in one F# type, read through `%A`.
    let structural<'T> : Differential<'T, 'T> = pair (sprintf "%A") (sprintf "%A")

    /// Records `message ()` as a disagreement unless `agree` — the primitive every check rides.
    let check (agree: bool) (message: unit -> string) (t: Tally) : Tally =
        if agree then t else Tally.disagree (message ()) t

    /// Records `message ()` as a loss of the in-pass go-red `instrument` unless `agree`.
    let checkOn (instrument: string) (agree: bool) (message: unit -> string) (t: Tally) : Tally =
        if agree then t else Tally.lose instrument (message ()) t

    /// Compares one answer pair through the render pair. `message` receives the two renders.
    let compareWith
        (d: Differential<'P, 'M>)
        (message: string -> string -> string)
        (production: 'P)
        (model: 'M)
        (t: Tally)
        : Tally =
        let p = d.Production production
        let m = d.Model model
        check (p = m) (fun () -> message p m) t

    /// Compares one answer pair through the render pair, under the standard message.
    let compare (d: Differential<'P, 'M>) (where: string) (production: 'P) (model: 'M) (t: Tally) : Tally =
        compareWith d (fun p m -> sprintf "%s\n  production: %s\n  the model:  %s" where p m) production model t

    /// The seeded loop: `trials` steps, each drawing from the generator state it is handed and
    /// returning the state the next step draws from. The generator is the caller's (ConfRng, a
    /// `System.Random`, an LCG) — the harness fixes the loop, not the pool.
    let seededFrom (trials: int) (state0: 'R) (step: int -> 'R -> Tally -> 'R * Tally) (t0: Tally) : 'R * Tally =
        let mutable r = state0
        let mutable t = t0

        for i in 1..trials do
            let r', t' = step i r t
            r <- r'
            t <- t'

        r, t

    /// `seededFrom`, when no later loop draws from the state it leaves.
    let seeded (trials: int) (state0: 'R) (step: int -> 'R -> Tally -> 'R * Tally) (t0: Tally) : Tally =
        seededFrom trials state0 step t0 |> snd

    /// The fixed-pool loop: every case, in order.
    let over (cases: seq<'C>) (probe: 'C -> Tally -> Tally) (t0: Tally) : Tally =
        Seq.fold (fun t c -> probe c t) t0 cases

    /// The shrink hook: a case that `diverges` is reduced by `shrink` before it is reported, so the
    /// finding names the smallest failing case the shrinker can reach.
    let shrunk (diverges: 'C -> bool) (shrink: ('C -> bool) -> 'C -> 'C) (case: 'C) : 'C =
        if diverges case then shrink diverges case else case

    /// The agreement arm: no finding of any class, or the first five of each are printed.
    let expectAgrees (label: string) (t: Tally) =
        for KeyValue(cls, ms) in t.Findings do
            if not (List.isEmpty ms) && not (cls.StartsWith "go-red:") then
                failtestf
                    "%s: the oracle and production DISAGREE — %d %s finding(s):\n%s"
                    label
                    (List.length ms)
                    cls
                    (List.rev ms |> List.truncate 5 |> String.concat "\n")

    /// The GO-RED arm: the same differential, handed a perturbed model or bridge, must disagree.
    let expectLoses (label: string) (t: Tally) =
        Expect.isNonEmpty (Tally.disagreements t) (sprintf "%s: the perturbed differential must DISAGREE" label)

    /// The GO-RED arm for an instrument run in the agreement's own pass (`Tally.goRed`).
    let expectLosesOn (label: string) (instrument: string) (t: Tally) =
        Expect.isNonEmpty (Tally.losses instrument t) (sprintf "%s: the perturbed %s must DISAGREE" label instrument)

// ---------------------------------------------------------------------------
//  The bridge — Core's types as the model reads them, and the model's back.
// ---------------------------------------------------------------------------

/// A Core `Footprint` as the model reads it: sets become sorted lists (membership is the meaning).
let toModelFootprint (f: Footprint) : DagFold.footprint =
    { DagFold.reads = Set.toList f.Reads
      DagFold.structure_writes = Set.toList f.StructureWrites
      DagFold.content_writes = Set.toList f.ContentWrites
      DagFold.unknown_parent_writes = Set.toList f.UnknownParentWrites
      DagFold.slot_reads = Set.toList f.SlotReads
      DagFold.slot_writes = Set.toList f.SlotWrites }

let ofModelShape (s: DagFold.shape) : MergeConflictShape =
    match s with
    | DagFold.ConcurrentUpdate -> MergeConflictShape.ConcurrentUpdate
    | DagFold.InsertPositionClash -> MergeConflictShape.InsertPositionClash
    | DagFold.MoveVsRemove -> MergeConflictShape.MoveVsRemove
    | DagFold.SlotClash slot -> MergeConflictShape.SlotClash slot

let ofModelConflict (c: DagFold.conflict<'Op>) : MergeConflict<'Op> =
    { Left = c.left
      Right = c.right
      Address = c.address
      Shape = ofModelShape c.shape }

let modelApply (w: StreamWitness<'Op, 'State, 'Rej>) (op: 'Op) (st: 'State) : DagFold.outcome<'State, 'Rej> =
    match w.Apply op st with
    | Ok s -> DagFold.Ok s
    | Error e -> DagFold.Error e

/// A model outcome rendered exactly as `FoldConfluence.foldOnce` renders production's. The model's
/// `LanesRejected` (Phase 300) carries the rejecting lanes; production's rendering names each with
/// the rejection it met replaying ON ITS OWN from the base, so the renderer replays each lane
/// through the model's own `replay` to recover it.
let renderModelOutcome
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (hashState: 'State -> string)
    (state0: 'State)
    (o: DagFold.lane_outcome<'Op, 'State, 'Rej>)
    : LaneFoldOutcome =
    match o with
    | DagFold.LaneFolded s -> LaneFolded(hashState s)
    | DagFold.LaneHalted cs ->
        LaneHalted(FoldConfluence.canonicalConflictReport w.Encode (cs |> List.map ofModelConflict))
    | DagFold.LaneRejected r -> LaneRejected(sprintf "%A" r)
    | DagFold.LanesRejected rs ->
        let rejection (lane: 'Op list) : LaneRejection<'Op, 'Rej> =
            { Head = ""
              Delta = lane
              NodeId = ""
              Reject =
                match DagFold.replay (modelApply w) lane state0 with
                | DagFold.Error r -> r
                | DagFold.Ok _ -> failwith "the model named a lane that applies" }

        LaneRejected(FoldConfluence.canonicalRejectionReport w.Encode (rs |> List.map rejection))

/// The oracle's fold, rendered exactly as `FoldConfluence.foldOnce` renders production's.
let oracleFold
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (hashState: 'State -> string)
    (state0: 'State)
    (lanes: 'Op list list)
    : LaneFoldOutcome =
    DagFold.fold_once (modelApply w) (fp >> toModelFootprint) state0 lanes
    |> renderModelOutcome w hashState state0

let productionFold
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
let productionDag
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (baseOp: 'Op)
    (lanes: 'Op list list)
    : string * string list * Dag.T<'Op> =
    let hashFn = OpStream.defaultHash

    let baseId, d0 =
        Dag.append (OpStream.canonical hashFn w) (Human "base") baseOp "" Dag.empty
        |> Reference.built

    let heads, dag =
        lanes
        |> List.indexed
        |> List.fold
            (fun (hs, d) (i, ops) ->
                let actor = Human("lane-" + string i)

                let head, d' =
                    ops
                    |> List.fold
                        (fun (h, dd) op -> Dag.append (OpStream.canonical hashFn w) actor op h dd |> Reference.built)
                        (baseId, d)

                hs @ [ head ], d')
            ([], d0)

    baseId, heads, dag

/// Production's N-lane partition and interference sweep, with the lanes-apply test (Phase 300)
/// switched off by a reducer that accepts every op — the merge SCRIPT the differentials here compare,
/// isolated from what the domain's reducer does with it.
let reconcileScript
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (dag: Dag.T<'Op>)
    (baseId: string)
    (heads: string list)
    : Result<'Op list, MergeConflict<'Op> list> =
    let accepting: StreamWitness<'Op, unit, 'Rej> =
        { Apply = fun _ () -> Ok()
          Encode = w.Encode
          Decode = w.Decode }

    match Dag.reconcileMany accepting fp dag baseId () heads with
    | Ok script -> Ok script
    | Error(ReconcileFault.LanesInterfere cs) -> Error cs
    | Error other -> failwithf "an accepting reducer cannot refuse: %A" other

/// Production's merge SCRIPT (or canonical halt report) through that DAG.
let productionScript
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (baseOp: 'Op)
    (lanes: 'Op list list)
    : Result<'Op list, string> =
    let baseId, heads, dag = productionDag w baseOp lanes

    match reconcileScript w fp dag baseId heads with
    | Ok script -> Ok script
    | Error cs -> Error(FoldConfluence.canonicalConflictReport w.Encode cs)

/// The oracle's merge script (or canonical halt report), from the lane deltas directly.
let oracleScript
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (lanes: 'Op list list)
    : Result<'Op list, string> =
    match DagFold.reconcile_many (fp >> toModelFootprint) lanes with
    | DagFold.Ok script -> Ok script
    | DagFold.Error cs -> Error(FoldConfluence.canonicalConflictReport w.Encode (cs |> List.map ofModelConflict))

let renderLanes (encodeOp: 'Op -> string) (lanes: 'Op list list) : string =
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
let disagreements
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

/// The fold differential's tally: the lane sets that folded clean and those that halted.
let foldTally = Tally.empty [ "Folded"; "Halted" ]

let expectAgreement (label: string) (t: Tally) =
    Differential.expectAgrees label t
    let folded = Tally.count "Folded" t
    let halted = Tally.count "Halted" t
    // Both classes must have been exercised, or the agreement certifies one path only —
    // the same vacuity posture as the pack's sample-adequacy guard.
    Expect.isGreaterThan folded 0 (sprintf "%s: no lane set folded (folded=%d halted=%d)" label folded halted)
    Expect.isGreaterThan halted 0 (sprintf "%s: no lane set halted (folded=%d halted=%d)" label folded halted)

let strField (fields: (string * JVal) list) (k: string) : string option =
    fields
    |> List.tryPick (fun (n, v) ->
        match v with
        | JStr s when n = k -> Some s
        | _ -> None)

let field (fields: (string * JVal) list) (k: string) : JVal option =
    fields |> List.tryPick (fun (n, v) -> if n = k then Some v else None)

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
//  `as_float` the widening `(Decoder.describing Decoder.float)` performs as `float i`.
// ---------------------------------------------------------------------------

let inv = System.Globalization.CultureInfo.InvariantCulture

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
//    1. the FOOTPRINT, as six address sets (four of nodes, two of slots since Phase 340) — the
//       model reads sets as lists, so both sides are compared deduplicated and sorted, which is
//       the reading `proofs/README.md` records;
//    2. the VERDICT, accepted or rejected;
//    3. an accepted RESULT through `Tree.encodeHash` — production's own function, run on both
//       sides through a `NodeWitness` for each tree type, over the per-node content the witness
//       exposes (id and kind tag). Hashing the reference node's value, hole or effect class would
//       compare fields the model does not model, and agreeing about them would mean nothing.
//    4. a rejection by CLASS, which is what the model claims. The envelope PAYLOADS carry id
//       lists (`UnknownNode`'s `addressable`, `ReorderMismatch`'s two orders) and are outside it.
// ---------------------------------------------------------------------------

/// The reference node as the tree model reads it — the three accessors, and no more. The bridge is
/// the exporters' (`VectorOracle.TreeModel`, Phase 478): the apply vectors are stamped through it.
let toModelTree (n: RNode) : TreeOps.tree = VectorOracle.TreeModel.toTree n

/// The BLIND bridge — the go-red instrument, and the tree family's counterpart to the fold
/// family's blind footprint. It erases every kind tag, so the model is asked about a different
/// tree from the one production was asked about and the result comparison must lose. It leaves
/// every id alone, so the VERDICTS still agree: what fails is the hash, which is the half that
/// would otherwise be the least exercised.
let rec toModelTreeBlind (n: RNode) : TreeOps.tree =
    TreeOps.TNode(n.Id, "", n.Children |> List.map toModelTreeBlind)

let mTid (t: TreeOps.tree) =
    match t with
    | TreeOps.TNode(i, _, _) -> i

let mKind (t: TreeOps.tree) =
    match t with
    | TreeOps.TNode(_, k, _) -> k

let mKids (t: TreeOps.tree) =
    match t with
    | TreeOps.TNode(_, _, cs) -> cs

/// A `NodeWitness` over the MODEL's tree, so the comparison runs production's own
/// `Tree.encodeHash` on both sides rather than a re-implementation of it beside the model.
let modelTreeW: NodeWitness<TreeOps.tree, string> =
    { Id = mTid
      KindTag = mKind
      Children = mKids
      ReplaceChildren = fun t cs -> TreeOps.TNode(mTid t, mKind t, cs) }

let toModelOpWith (bridge: RNode -> TreeOps.tree) (op: SkeletonOp<RNode, string>) : TreeOps.op =
    VectorOracle.TreeModel.toOpWith bridge op

let encWitnessNode (nodeId: string) (kindTag: string) = nodeId + "|" + kindTag

let prodTreeHash (t: RNode) =
    Tree.encodeHash nodew (fun n -> encWitnessNode n.Id n.Kind) t

let modelTreeHash (t: TreeOps.tree) =
    Tree.encodeHash modelTreeW (fun n -> encWitnessNode (mTid n) (mKind n)) t

/// A tree as the differentials compare it (Phase 302): id, kind tag and children, recursively — the
/// part of a tree the model carries. The accepted-result comparison read `Tree.encodeHash` digests,
/// so its verdict rested on a hash: Phase 290 made the digest see the shape, and this makes the
/// comparison not depend on the digest at all. The mis-nesting mutant still loses it.
type TreeShape = TreeShape of id: string * kind: string * children: TreeShape list

let rec prodShape (t: RNode) : TreeShape =
    TreeShape(t.Id, t.Kind, t.Children |> List.map prodShape)

let rec modelShape (t: TreeOps.tree) : TreeShape =
    TreeShape(mTid t, mKind t, modelTreeW.Children t |> List.map modelShape)

let prodRejClass (r: Rejection<string>) =
    match r with
    | UnknownNode _ -> "UnknownNode"
    | DuplicateId _ -> "DuplicateId"
    | CannotRemoveRoot -> "CannotRemoveRoot"
    | WouldNestUnderSelf _ -> "WouldNestUnderSelf"
    | NotAContainer _ -> "NotAContainer"
    | ReorderMismatch _ -> "ReorderMismatch"
    | Rejected _ -> "Rejected"
    // Phase 286: raised only by the keyed engine, which the oracle does not run — so the class
    // never reaches a comparison, and it has no model counterpart to meet there.
    | KeyedPosition _ -> "KeyedPosition"
    // Phase 313: raised only by the grammar and reference engines, which the oracle does not run.
    | IllegalChild _ -> "IllegalChild"
    | StillReferenced _ -> "StillReferenced"
    // Phase 518: raised only by `Diff`, never by an apply the oracle runs.
    | RootIdMismatch _ -> "RootIdMismatch"

let modelRejClass (r: TreeOps.rejection) =
    match r with
    | TreeOps.UnknownNode _ -> "UnknownNode"
    | TreeOps.DuplicateId _ -> "DuplicateId"
    | TreeOps.CannotRemoveRoot -> "CannotRemoveRoot"
    | TreeOps.WouldNestUnderSelf _ -> "WouldNestUnderSelf"
    | TreeOps.NotAContainer _ -> "NotAContainer"
    | TreeOps.ReorderMismatch _ -> "ReorderMismatch"
    | TreeOps.Rejected _ -> "Rejected"

/// Ops that REACH each rejection class the plain `apply` can raise, against the base tree
/// `root(doc)[a(section)[a1,a2], b(section)[b1]]`. The generator keeps only accepted ops, so the
/// refusal arms are supplied rather than hoped for.
let treeRefusals: SkeletonOp<RNode, string> list =
    [ InsertChild("no-such-parent", RNode.leaf "fresh-133" "para" "v") // UnknownNode (parent)
      InsertChild("a", RNode.leaf "a1" "para" "v") // DuplicateId
      RemoveNode "root" // CannotRemoveRoot
      RemoveNode "no-such-node" // UnknownNode (target)
      MoveNode("a", "a1") // WouldNestUnderSelf
      MoveNode("a", "no-such-parent") // UnknownNode (new parent)
      ReorderChildren("a", [ "a1" ]) // ReorderMismatch
      ReorderChildren("no-such-parent", []) // UnknownNode (reorder)
      Batch [ InsertChild("b", RNode.leaf "b133" "para" "v"); RemoveNode "root" ] ] // all-or-nothing
// Phase 309 retired the two hand-written `UpdateNode` cases Phase 250 supplied here (an accepted
// rewrite of a container and an absent target): the pool below now DRAWS updates and batches
// itself, so both arms are reached by generation rather than by a list.

/// The update and batch draws the generated pool carries (Phase 309) — the two shapes Phase 297's
/// `genOp` draws for a law family and the lane generator does not. Every node any reached state
/// holds is rewritten in place (same id, a kind the witness can see moved), and every adjacent pair
/// of generated ops is bundled as a `Batch`. Each is then asked at EVERY state, like the rest of
/// the pool: an update is accepted where its target is held and refused (`UnknownNode`) where a
/// state has removed or never had it, and a batch exercises all-or-nothing over real members.
let drawnUpdatesAndBatches
    (states: RNode list)
    (generated: SkeletonOp<RNode, string> list)
    : SkeletonOp<RNode, string> list =
    let updates =
        states
        |> List.collect (Tree.preorder nodew)
        |> List.distinctBy _.Id
        |> List.map (fun n -> UpdateNode(RNode.node n.Id ("edited-" + n.Kind) []))

    let batches = generated |> List.pairwise |> List.map (fun (a, b) -> Batch [ a; b ])

    updates @ batches

// ---------------------------------------------------------------------------
//  The PARSER differential (Phase 146) — `JsonParse.parse` beside `Json.read` at a cap and no
//  other bound (`JsonLimits.depthOnly`; until Phase 519 the unlimited `parseDetailedWithPolicy`,
//  which every reader now runs through the limited scanner instead). The model's own theorem
//  `limited_at_no_bound_is_the_parser` is the relation between the two: at no bound but the cap they
//  answer the same, the cap's `MaxDepthExceeded` lifted to the `Depth` breach at the same position —
//  so a breach and the model's cap refusal render as ONE line, and every other answer as before.
//
//  Everything above this line begins at a `JVal` that already exists, which is exactly the
//  boundary the `WireDecode` model (Phase 135) drew. What runs here is the model of the recursive-descent
//  parser itself, over the same TEXT production reads: the corpus fixtures as raw bytes, a
//  near-miss table that reaches every classified failure, and generated deep, wide and random
//  inputs.
//
//  Each probe renders both answers into ONE string carrying the outcome class, the decoded value,
//  and — on a failure — the KIND, the POSITION and the MESSAGE. A model agreeing on accept-vs-
//  refuse alone would not notice a parser that classified the wrong thing or pointed at the wrong
//  character, and the position is the half most likely to drift silently.
// ---------------------------------------------------------------------------

module JsonParseDiff =

    /// The character bridge is the exporters' (`VectorOracle.JsonModel`, Phase 478).
    let toCh (c: char) : JsonParse.ch = VectorOracle.JsonModel.toCh c

    let toChs (s: string) : JsonParse.ch list = VectorOracle.JsonModel.toChs s

    /// A string from its UTF-16 units. An unpaired surrogate is built this way and never written
    /// as a `\u` escape in an F# literal, which the compiler replaces with U+FFFD.
    let units (codes: int list) : string =
        System.String(codes |> List.map char |> Array.ofList)

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

    let chStr (c: JsonParse.ch) : string = VectorOracle.JsonModel.chStr c

    let tokStr (t: JsonParse.ch list) : string = VectorOracle.JsonModel.tokStr t

    let private modelStr (os: JsonParse.och list) : string = VectorOracle.JsonModel.modelStr os

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

    let floatRead (tok: JsonParse.ch list) : JsonParse.freadv = VectorOracle.JsonModel.floatRead tok

    let budget (n: int) : unit list = VectorOracle.JsonModel.budget n

    let private modelPolicy (p: NullPolicy) : JsonParse.policy = VectorOracle.JsonModel.policy p

    /// The depth cap's one rendering, on both sides (Phase 519): production's `Depth` breach and the
    /// model's `MaxDepthExceeded` at the same position are the same answer by the lift.
    let depthLine (position: int) : string =
        sprintf "limit %s @%d" (JsonLimit.name JsonLimit.Depth) position

    let prodAnswer (policy: NullPolicy) (maxDepth: int) (input: string) : string =
        match
            Json.read
                { Null = policy
                  Limits = JsonLimits.depthOnly maxDepth }
                input
        with
        | Ok v -> "ok " + renderProd v
        | Error(JsonRefusal.Malformed e) -> sprintf "err %A @%d %s" e.Kind e.Position e.Message
        | Error(JsonRefusal.LimitExceeded b) when b.Limit = JsonLimit.Depth -> depthLine b.Position
        // No other bound is set, so none can be passed; rendered apart so a defect shows.
        | Error(JsonRefusal.LimitExceeded b) -> sprintf "limit %s @%d (not the cap)" (JsonLimit.name b.Limit) b.Position

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
        // The lift (`limited_at_no_bound_is_the_parser`): the cap's refusal is the Depth breach.
        | JsonParse.RErr(JsonParse.MaxDepthExceeded, _, at) -> depthLine (input.Length - List.length at)
        | JsonParse.RErr(k, m, at) -> sprintf "err %A @%d %s" k (input.Length - List.length at) m
        // `parse` runs at no bound and never answers this (`limited_at_no_bound_is_the_parser`);
        // the arm is for the match's totality.
        | JsonParse.RLimit(l, at) -> sprintf "limit %A @%d" l (input.Length - List.length at)

    // ---- the LIMITS (Phase 465) ----

    /// Production's answer under a limits record. A breach renders as `limit <name> @<position>`:
    /// the model does not carry the path, which the decode vectors compare instead.
    let prodAnswerLimited (policy: NullPolicy) (limits: JsonLimits) (input: string) : string =
        match Json.read { Null = policy; Limits = limits } input with
        | Ok v -> "ok " + renderProd v
        | Error(JsonRefusal.Malformed e) -> sprintf "err %A @%d %s" e.Kind e.Position e.Message
        | Error(JsonRefusal.LimitExceeded b) -> sprintf "limit %s @%d" (JsonLimit.name b.Limit) b.Position

    /// The model's answer under a record. `bytesOk` is the byte verdict the model takes as a
    /// parameter (its alphabet cannot count bytes); the differential computes it with the
    /// platform's UTF-8 encoder, an oracle independent of production's own counter.
    let modelAnswerLimited
        (bridge: string -> JsonParse.ch list)
        (policy: NullPolicy)
        (limits: JsonLimits)
        (bytesOk: bool)
        (input: string)
        : string =
        match
            JsonParse.parse_limited
                floatRead
                (string limits.MaxDepth)
                (modelPolicy policy)
                (budget limits.MaxDepth)
                (VectorOracle.JsonModel.modelLims limits)
                bytesOk
                (bridge input)
        with
        | JsonParse.ROk v -> "ok " + renderModel v
        | JsonParse.RErr(k, m, at) -> sprintf "err %A @%d %s" k (input.Length - List.length at) m
        | JsonParse.RLimit(l, at) ->
            sprintf "limit %s @%d" (VectorOracle.JsonModel.limitName l) (input.Length - List.length at)

    /// Every way the two disagree over one pool, plus how many inputs reached each outcome class —
    /// a run that only ever refused has compared twelve error messages and measured no parse at
    /// all, and a run that only ever accepted has measured none of the classification. EVERY
    /// disagreement counts: Phase 299's grammar and surrogate refusals were carved out of this
    /// comparison until Phase 306 restated the model to make them, and the carve-out is gone.
    let sweep (policy: NullPolicy) (maxDepth: int) (inputs: (string * string) list) : Tally =
        Tally.empty [ "Accepted"; "Refused" ]
        |> Differential.over inputs (fun (name, input) t ->
            let p = prodAnswer policy maxDepth input
            let m = modelAnswer toChs policy maxDepth (budget maxDepth) input

            t
            |> Tally.bump (if p.StartsWith "ok " then "Accepted" else "Refused")
            |> Differential.compareWith
                Differential.text
                (fun p m -> sprintf "%s: input %s\n    production %s\n    model      %s" name input p m)
                p
                m
            // The sweep compares in the regime the differential was written for — an unbounded-
            // equivalent parser — so its cap sits above every input it is handed, and an input
            // that reaches it is a defect of the pool, not an agreement (Phase 519). The cap's own
            // boundary is the deep/wide family's, which straddles it on purpose.
            |> Differential.check (not (p.StartsWith "limit ")) (fun () ->
                sprintf "%s: input %s reached the sweep's cap %d — the pool left its regime" name input maxDepth))

    let expectAgreement (label: string) (t: Tally) =
        Differential.expectAgrees label t

        Expect.isGreaterThan
            (Tally.count "Accepted" t)
            0
            (label + ": no input was ACCEPTED — the accept path measured nothing")

        Expect.isGreaterThan
            (Tally.count "Refused" t)
            0
            (label + ": no input was REFUSED — the classification measured nothing")

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
          // Phase 462 — the decode-determinism rows the model refuses itself: a repeated member
          // name (checked when the member is accumulated; an erased null is not an occurrence) and
          // a raw control inside a string, in a value, in a key, and beside a pending surrogate.
          "repeated member", "{\"a\":1,\"a\":2}"
          "repeated member apart", "{\"a\":1,\"b\":2,\"a\":3}"
          "repeated member escaped spelling", "{\"a\":1,\"\u0061\":2}"
          "repeated member after an erased null", "{\"a\":null,\"a\":1}"
          "repeated member then null", "{\"a\":1,\"a\":null}"
          "repeated member in a nested object", "[{\"a\":1},{\"b\":1,\"b\":2}]"
          "repeated member with a malformed value", "{\"a\":1,\"a\":[}"
          "one name in sibling objects", "[{\"a\":1},{\"a\":2}]"
          "one name at two depths", "{\"a\":{\"a\":1}}"
          "raw tab in a string", "\"a	b\""
          "raw control in a string", "\"a" + string (char 1) + "b\""
          "raw control in a key", "{\"a" + string (char 1) + "\":1}"
          "raw newline after a high surrogate",
          "\""
          + string (char 0xD83D)
          + "
\""
          "escaped controls", "\"\u0000\u001f\t\""
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
          // Phase 253 — an integer token past 2^53 that IS a canonical float layout is read; one
          // that spells the same double's exact value instead is not.
          "canonical layout past int53", "10000000000000000"
          "canonical layout 2^53 plus two", "9007199254740994"
          "canonical layout negative", "-9007199254740994"
          "exact value, not the layout", "18205257897171752"
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
          "close brace only", "}"
          // Phase 306 — well-formed UTF-16 or refused, in every spelling a unit can arrive in. The
          // raw units are built, never written as escapes in this file (`units`).
          "raw lone high", "\"" + units [ 0xD83D ] + "\""
          "raw lone low", "\"" + units [ 0xDE00 ] + "\""
          "raw high then ascii", "\"" + units [ 0xD83D ] + "z\""
          "raw low then high", "\"" + units [ 0xDE00; 0xD83D ] + "\""
          "raw high then high", "\"" + units [ 0xD801; 0xD800 ] + "\""
          "raw pair", "\"" + units [ 0xD83D; 0xDE00 ] + "\""
          "raw pair then stray low", "\"" + units [ 0xD83D; 0xDE00; 0xDE00 ] + "\""
          "raw high at end of input", "\"" + units [ 0xD83D ]
          "escaped lone high", "\"\\uD800\""
          "escaped lone low", "\"\\uDFFF\""
          "escaped lone low lower-case", "\"\\udfff\""
          "escaped high then high", "\"\\uD801\\uD800\""
          "escaped pair", "\"\\uD83D\\uDE00\""
          "escaped pair lower-case", "\"\\ud83d\\ude00\""
          "raw high then escaped low", "\"" + units [ 0xD83D ] + "\\uDE00\""
          "escaped high then raw low", "\"\\uD83D" + units [ 0xDE00 ] + "\""
          "escaped high then short escape", "\"\\uD83D\\n\""
          "escaped high then bad escape", "\"\\uD83D\\q\""
          "escaped high then bad hex", "\"\\uD83D\\uzzzz\""
          "lone high in a key", "{\"" + units [ 0xD800 ] + "\":1}"
          "escaped boundary below the surrogates", "\"\\uD7FF\""
          "escaped boundary above the surrogates", "\"\\uE000\""
          // The number grammar's boundary, beside the entries above that already reach it.
          "leading zero then fraction", "01.5"
          "point without integer", "-.5"
          "point before exponent", "1.e5"
          "negative leading zero", "-01"
          "zero fraction", "0.5"
          "zero exponent", "0e0" ]

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
        // The two surrogate halves are in the alphabet since Phase 306 (built, not written): a
        // soup string holds them lone, paired and reversed, which nothing hand-written enumerates.
        let alphabet =
            "{}[]\",:0123456789-+.eE\\ \ttnrufalse\u00e9%dD8" + units [ 0xD83D; 0xDE00 ]

        let mutable state = uint64 seed * 6364136223846793005UL + 1442695040888963407UL

        let next () =
            state <- state * 6364136223846793005UL + 1442695040888963407UL
            int ((state >>> 33) &&& 0x7FFFFFFFUL)

        [ for i in 1..count ->
              let len = next () % 24

              let s = System.String(Array.init len (fun _ -> alphabet[next () % alphabet.Length]))

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
let rec renderProdOp (op: SkeletonOp<RNode, string>) : string =
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
    | UpdateNode node -> "U|" + encWitnessNode node.Id node.Kind

let rec renderModelOp (op: TreeOps.op) : string =
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
    | TreeOps.UpdateNode node -> "U|" + encWitnessNode (mTid node) (mKind node)

// ---------------------------------------------------------------------------
//  Phase 140 — the CONTAINER CAPABILITY: `Ops.apply` under `OpScope.contained` / `Ops.canApply` under `OpScope.contained` beside
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
//    1. `Ops.apply` under `OpScope.contained` vs the model's `apply_contained` — verdict, accepted result through
//       `Tree.encodeHash`, rejection by CLASS.
//    2. `Ops.canApply` under `OpScope.contained` vs the model's `can_apply_contained`, and that the dry run and the
//       mutating call agree on EACH side separately (`can_apply_contained_agrees` instantiated).
//    3. `Ops.apply` under `OpScope.contained` against `Ops.apply` ON PRODUCTION: the two must agree exactly unless
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
let containerKinds = [ "doc"; "para"; "section" ]

let drawCanHold (r0: ConfRng.T) : Set<string> * ConfRng.T =
    let mutable r = r0
    let mutable acc = Set.empty

    for k in containerKinds do
        let b, r' = ConfRng.intBelow 2 r
        r <- r'

        if b = 1 then
            acc <- Set.add k acc

    acc, r

let showKinds (kinds: Set<string>) =
    if Set.isEmpty kinds then
        "(nothing)"
    else
        kinds |> Set.toList |> String.concat "+"

//  Phase 149 — the CANONICAL ENCODER. `proofs/WireCanon.fst`'s extracted `render`
//  beside `Fuaran.Core.Canonical.write Fuaran.Core.Rendering.CrossHost`, byte for byte.
//
//  The model is named `WireCanon` and not `Canon` for the reason `TreeOps.fst` is
//  not called `Ops` and `TreeDiff.fst` is not called `Diff`: the extracted oracle is
//  a top-level F# module and this host opens `Fuaran.Core`, which already carries a
//  `Canon`. The two would shadow each other exactly where the differential needs both.
// ---------------------------------------------------------------------------

/// The `wire` the model is parametric over, instantiated at production's own layouts. Two of the
/// seven fields are worth naming.
///
/// `float_str` is `Double.ToString("R", InvariantCulture)` DIRECTLY and not `FloatLayout.canonical`,
/// so the `-0` collapse and the three non-finite tokens stay the MODEL's clauses to get right
/// rather than being handed to it — that is the difference between a comparison and a tautology.
/// The digits themselves are .NET's, per the opaque-numeral boundary the `WireDecode` and `JsonParse` models also draw.
///
/// `tok_read` is the numeral READ-BACK the model does not compute: production's own parser over
/// the token the model scanned. `tok_read_ok`, the one premise the theorems carry, is exactly the
/// claim that this function inverts the two layouts above — which is what rule 5 means by "the
/// shortest digit sequence that ROUND-TRIPS", so the premise is the layout's definition rather
/// than an extra assumption about it.
/// Is `s` well-formed UTF-16 — by the PLATFORM's own strict encoder, which throws on a string it
/// cannot encode. An independent oracle for the model's `str_ok` parameter (Phase 306): handing
/// the model production's own scan would compare `Json.isWellFormedUtf16` with itself.
let strictUtf8 = System.Text.UTF8Encoding(false, true)

let strictlyEncodable (s: string) : bool =
    try
        strictUtf8.GetBytes s |> ignore
        true
    with :? System.Text.EncoderFallbackException ->
        false

/// A string from its UTF-16 units — the way an unpaired surrogate is built in this file. A `\u`
/// escape of one in an F# string literal is replaced by the compiler with U+FFFD.
let units16 (codes: int list) : string =
    System.String(codes |> List.map char |> Array.ofList)

/// The extracted model is a CHARACTER-LIST interpreter, and F*'s F# backend emits plain recursion
/// with no tail calls (README, finding 3's neighbour: the backend is second-class upstream). So
/// rendering a multi-kilobyte corpus fixture walks a stack proportional to the document's BYTES,
/// and the largest fixture in the corpus overflows the default 1 MB one. That is a property of the
/// EXTRACTION and not of the model — the theorem is about a function, not about a runtime's frame
/// budget — so the differential runs on a thread with a stack sized for the corpus rather than
/// shrinking the pool until it fits the default. Shrinking would silently narrow what the corpus
/// leg certifies, and the fixture it would drop first is the deepest one.
let onBigStack (f: unit -> 'a) : 'a =
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

let nextCanonSeed (r: int) : int = (r * 1103515245 + 12345) &&& 0x3FFFFFFF

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

/// The option shims, once: the exporters' model bridges (`VectorOracle`, Phase 478) own them.
let toMOpt (o: 'a option) : FStar_Pervasives_Native.option<'a> = VectorOracle.toMOpt o

let ofMOpt (o: FStar_Pervasives_Native.option<'a>) : 'a option = VectorOracle.ofMOpt o

/// Every determinism set over the three factors (Phase 319), the empty set first — the closed
/// alphabet the model's characteristic vector ranges over.
let allDeterminismSets: DeterminismSource list =
    [ for mask in 0..7 ->
          [ ClockFactor; RandomFactor; NetworkFactor ]
          |> List.indexed
          |> List.filter (fun (k, _) -> (mask >>> k) &&& 1 = 1)
          |> List.map snd
          |> Set.ofList ]

// The document mutations the codec differentials read (Phase 354; shared with the query codec's since
// Phase 455).

/// Every path to a member or an item below a document, as child indexes.
let rec jsites (v: JVal) : int list list =
    let kids =
        match v with
        | JArr xs -> xs
        | JObj fs -> List.map snd fs
        | _ -> []

    kids
    |> List.mapi (fun i k -> [ i ] :: (jsites k |> List.map (fun p -> i :: p)))
    |> List.concat

/// Rewrite the child at `path`: `f` is handed its key (for a member) and value, and answers the
/// replacement, or `None` to drop it.
let rec jedit (path: int list) (f: string option * JVal -> (string option * JVal) option) (v: JVal) : JVal =
    match path, v with
    | [ i ], JArr xs ->
        JArr(
            xs
            |> List.indexed
            |> List.choose (fun (k, x) -> if k = i then f (None, x) |> Option.map snd else Some x)
        )
    | [ i ], JObj fs ->
        JObj(
            fs
            |> List.indexed
            |> List.choose (fun (k, (n, x)) ->
                if k = i then
                    f (Some n, x) |> Option.map (fun (n', x') -> defaultArg n' n, x')
                else
                    Some(n, x))
        )
    | i :: rest, JArr xs -> JArr(xs |> List.mapi (fun k x -> if k = i then jedit rest f x else x))
    | i :: rest, JObj fs -> JObj(fs |> List.mapi (fun k (n, x) -> if k = i then n, jedit rest f x else n, x))
    | _ -> v

let mutantStrings =
    [ "capability"
      "value"
      "slot"
      "clock"
      "random+clock"
      "intRange"
      "stringLen"
      "pure"
      "fromNode"
      "source" ]

/// One mutation of a document: a member or item dropped, replaced by a value of another kind or by
/// another string of the codec's vocabulary, a `"$type"` re-keyed `"kind"` (the descriptor
/// spelling's discriminator), the root's members reversed, or a member the reader does not read
/// added. The last two the lenient readers must read THROUGH.
let jmutate (v: JVal) (r: ConfRng.T) : JVal * ConfRng.T =
    let sites = jsites v
    let op, r1 = ConfRng.intBelow 9 r

    match op, v with
    | 7, JObj fs -> JObj(List.rev fs), r1
    | 8, JObj fs -> JObj(fs @ [ "unread", JInt 1 ]), r1
    | _ when List.isEmpty sites -> v, r1
    | _ ->
        let path, r2 = ConfRng.choose sites r1
        let s, r3 = ConfRng.choose mutantStrings r2

        let f (key: string option, x: JVal) : (string option * JVal) option =
            match op with
            | 0 -> None
            | 1 -> Some(key, JInt 7)
            | 2 -> Some(key, JStr "zz")
            | 3 -> Some(key, JObj [])
            | 4 -> Some((if key = Some "$type" then Some "kind" else key), x)
            | 5 -> Some(key, JStr s)
            | 6 -> Some(key, JBool true)
            | _ -> Some(key, JArr [])

        jedit path f v, r3

// ---- generators ----

let effPool =
    [ Effect.pureDeterministic
      { Host = ReadsHost
        Determinism = Effect.deterministic }
      { Host = Pure
        Determinism = Effect.clock }
      { Host = WritesHost
        Determinism = Effect.random }
      { Host = ReadsHost
        Determinism = Effect.network }
      { Host = Pure
        Determinism = Set.ofList [ ClockFactor; RandomFactor ] }
      { Host = ReadsHost
        Determinism = Set.ofList [ ClockFactor; NetworkFactor ] }
      { Host = WritesHost
        Determinism = Set.ofList [ ClockFactor; RandomFactor; NetworkFactor ] } ]

let genSpace (r: ConfRng.T) : ValueSpace * ConfRng.T =
    // One draw in ten is one of the Phase 307 declarations below; the rest keep the original
    // five-way split, so the well-formed shapes stay the bulk of the sample and the registries the
    // capability differential builds stay populated.
    let draw, r1 = ConfRng.intBelow 50 r
    let roll = if draw < 45 then draw % 5 else draw - 40
    let lo, r2 = ConfRng.intBelow 5 r1
    let span, r3 = ConfRng.intBelow 5 r2

    match roll with
    | 0 -> IntRange(lo, lo + span), r3
    | 1 -> FloatRange(float lo / 2.0, float (lo + span) / 2.0 + 0.5), r3
    | 2 -> StringLen(lo, lo + span), r3
    | 3 -> Enum [ "a"; "b" ], r3
    | 4 -> AnyString, r3
    // Phase 307 — the declarations the admission gate refuses or ranks: a negative range, an
    // empty one, a NaN or infinite bound, bounds past 2^53, an empty enum, a count past the cap.
    | 5 -> IntRange(-(lo + 1), span), r3
    | 6 ->
        (match span with
         | 0 -> IntRange(lo + 1, lo)
         | 1 -> StringLen(lo + 1, lo)
         | 2 -> Enum []
         | _ -> FloatRange(float (lo + 1), float lo)),
        r3
    | 7 ->
        (match span with
         | 0 -> FloatRange(nan, 1.0)
         | 1 -> FloatRange(0.0, infinity)
         | _ -> FloatRange(-infinity, float lo)),
        r3
    | 8 -> FloatRange(-9007199254740993.0 * 4.0, 1e300), r3
    | _ -> IntRange(lo, Space.maxRepeatCount + span), r3

let genHoleKind (r: ConfRng.T) : HoleKind * ConfRng.T =
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
let genArtifact (r: ConfRng.T) : RNode * ConfRng.T =
    let n, r1 = ConfRng.intBelow 5 r
    let mutable rng = r1
    let children = System.Collections.Generic.List<RNode>()

    for i in 1..n do
        let roll, r2 = ConfRng.intBelow 10 rng
        let hk, r3 = genHoleKind r2
        let e, r4 = ConfRng.choose effPool r3
        rng <- r4
        let name = sprintf "x%d" (i % 2 + 1)

        if roll < 5 then
            children.Add
                { RNode.hole (sprintf "h%d" i) "field" name hk with
                    Eff = e }
        // Phase 307 — a hole whose id an earlier child already uses (two holes at one address),
        // and a slot with a hole beneath it.
        elif roll = 5 then
            if i % 2 = 0 then
                children.Add
                    { RNode.hole "h1" "field" name hk with
                        Eff = e }
            else
                children.Add
                    { RNode.hole (sprintf "s%d" i) "region" name (SlotHole None) with
                        Eff = e
                        Children = [ RNode.hole (sprintf "u%d" i) "field" "under" (ValueHole AnyString) ] }
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

/// A signature for a capability: up to three entries drawn through `Function.signature` of a
/// generated artifact, so the entry vocabulary is production's own.
let genCapSignature (name: string) (r: ConfRng.T) : Signature * ConfRng.T =
    let t, r1 = genArtifact r
    let e, r2 = ConfRng.choose effPool r1

    { Function.signature artw name t with
        Effect = e },
    r2

// ---------------------------------------------------------------------------
//  Phase 306 — the COLUMNAR CODEC: `WireColumn` beside `Table.validate` and `ColumnCodec`.
// ---------------------------------------------------------------------------
//
// `proofs/WireColumn.fst` models the codec at the `JVal`: `validate`, `encode_json`,
// `decode_json`, and the NORMAL FORM its round-trip theorem is stated up to (columns in schema
// order, an `Int` widened into its float or decimal column). What runs here is each of those
// beside the shipped function, over tables drawn INSIDE and OUTSIDE the normal form — which is the
// half the suite's own generative codec law never sampled: its generator builds tables already in
// normal form, so it could assert the literal round trip and pass.
//
// The model declines the decoder's lenient-ingest arms (it answers `OutOfModel`), so a decode
// probe skips a document the model answers that for, and counts it.

module ColumnDiff =

    /// The comparison every differential of the column family (and the unit and temporal ones
    /// beside it) makes: two answers already rendered as text, under the standard message, the
    /// finding located as `<label>: <what>`.
    let compareText (label: string) (what: string) (prod: string) (model: string) (t: Tally) : Tally =
        Differential.compare Differential.text (label + ": " + what) prod model t

    /// A canonical instant for a column at `u`: a whole second, or a fraction of the unit's own
    /// digits, or of a coarser unit's — every unit at least as coarse as the column's, which is
    /// every text `ofCells` and decode admit there.
    let instantFor (draw: int -> int) (u: TimeUnit) : string =
        let second = sprintf "2026-01-01T00:00:%02d" (draw 60)

        let digits =
            match draw 4 with
            | 0 -> 0
            | 1 -> min 3 (TimeUnit.digits u)
            | 2 -> min 6 (TimeUnit.digits u)
            | _ -> TimeUnit.digits u

        if digits = 0 then
            second + "Z"
        else
            // the last digit non-zero, as the canonical form requires
            let body = String.init (digits - 1) (fun _ -> string (draw 10))
            second + "." + body + string (1 + draw 9) + "Z"

    /// A canonical instant FINER than a column at `u` holds — `None` for the finest unit.
    let finerThan (u: TimeUnit) : string option =
        match u with
        | TimeUnit.Seconds -> Some "2026-01-01T00:00:00.5Z"
        | TimeUnit.Milliseconds -> Some "2026-01-01T00:00:00.000125Z"
        | TimeUnit.Microseconds -> Some "2026-01-01T00:00:00.000000007Z"
        | TimeUnit.Nanoseconds -> None

    /// A decoded source as text, a float by its canonical layout — so two sources are compared as
    /// what they hold and a `Float -0.0` beside a `Float 0.0` is not a disagreement the wire could
    /// carry (both are the token `0`).
    let showSource (src: DataSource) : string =
        match src with
        | Ref r -> "ref " + r
        | Embedded t ->
            let cell (c: Cell) =
                match c with
                | Float f -> "Float " + FloatLayout.canonical f
                | other -> sprintf "%A" other

            let column (c: Column) =
                c.Name
                + ":"
                + ColumnType.tag c.Type
                + "["
                + (Column.toCells c |> List.map cell |> String.concat ";")
                + "]"

            (t.Schema
             |> List.map (fun f -> f.Name + ":" + ColumnType.tag f.Type)
             |> String.concat ",")
            + " | "
            + (t.Columns |> List.map column |> String.concat " ")

    let showProd (r: Result<DataSource, ColumnError>) : string =
        match r with
        | Result.Ok src -> "ok " + showSource src
        | Result.Error e -> "refused " + prodClass e

    let showModel (r: WireColumn.res<WireColumn.data_source<int, float>>) : string =
        match r with
        | WireColumn.Good src -> "ok " + showSource (ofModelSource src)
        | WireColumn.Bad e -> "refused " + modelClass e

    // ---- the pool ----

    let private names = [| "a"; "b"; "c"; "k\"q"; "é" |]

    let private texts =
        [| "1.5"
           "1.50"
           "-0"
           "007"
           "x"
           ""
           "12"
           "2026-02-28"
           "2026-02-30"
           "2026-06-22T17:00:00Z"
           "nope" |]

    /// One drawn table. `wild` is how far outside what `validate` accepts the draw may wander:
    /// at 0 the table is valid but NOT necessarily in normal form (its columns may be in another
    /// order than its schema); above 0 it may also carry a duplicate name, a missing or an extra
    /// column, a column whose type is not its schema entry's, a ragged length, a non-finite float
    /// and non-canonical text.
    ///
    /// Every column is built through `Column.ofCells`, so every cell it draws is one the COLUMN'S
    /// type can hold (Phase 417): a cell of another type is unrepresentable in the typed storage,
    /// and `ofCells` refuses it at construction. So the wild cells are drawn per type — a `Float nan`
    /// only into a float column, a `Decimal "1.50"` only into a decimal column, a `Date "2026-02-30"`
    /// only into a date column, an `Int` into an int, a float or a decimal column (the last two
    /// widened, and normalised by `ofCells` to the `Float` / `Decimal` it reads back as). With the
    /// cell-of-another-type wildness gone, the model's clause-(e) `TypeMismatch` arm (a present cell
    /// whose type does not widen into its column's) is no longer reached FROM PRODUCTION: production
    /// cannot hand it such a column. `TypeMismatch` is still reached, through clause (c) — a column
    /// whose type differs from its schema entry (`ty'` below). Phase 419's refinement theorem is
    /// where the typed column and the model's cell-list column are shown to be one value.
    let genTable (r: int ref) (wild: int) : Table =
        let draw (n: int) =
            r.Value <- nextCanonSeed r.Value
            r.Value % n

        let chance (n: int) = wild > 0 && draw n = 0

        let ncols = draw 4
        let rows = draw 4

        let picked =
            [ for i in 0 .. ncols - 1 -> (if chance 8 then names[0] else names[i]), modelTypes[draw modelTypes.Length] ]

        /// The wild cells a column of `ty` can hold — empty for a type with none of its own.
        let wildFor (ty: ColumnType) : Cell[] =
            match ty with
            | IntType -> [| Int 3 |]
            | FloatType -> [| Float nan; Int 3 |]
            | DecimalType -> [| Decimal "1.50"; Int 3 |]
            // Phase 422: a date column holds days, so a `Date` whose text names no day cannot be
            // built (`ofCells` refuses it as the model's validate does); the date column has no
            // wild cell of its own any more.
            | DateType
            | BoolType
            | StringType
            | TimestampType _ -> [||]

        let cellOf (ty: ColumnType) : Cell =
            let pool = wildFor ty

            if draw 5 = 0 then
                Null
            elif pool.Length > 0 && chance 9 then
                pool[draw pool.Length]
            else
                match ty with
                | IntType -> Int(draw 2001 - 1000)
                | FloatType ->
                    match draw 4 with
                    | 0 -> Int(draw 50 - 25)
                    | 1 -> Float(float (draw 40 - 20))
                    | 2 -> Float -0.0
                    | _ -> Float(float (draw 4000 - 2000) / 8.0)
                | BoolType -> Bool(draw 2 = 0)
                | StringType -> Str texts[draw texts.Length]
                | DateType -> Date(sprintf "2026-01-%02d" (1 + draw 28))
                | TimestampType u -> Timestamp(instantFor draw u)
                | DecimalType ->
                    match draw 3 with
                    | 0 -> Int(draw 50 - 25)
                    | 1 -> Decimal(sprintf "%d.%d" (draw 90 - 45) (1 + draw 9))
                    | _ -> Decimal(string (draw 2000 - 1000))

        let build (n: string) (ty: ColumnType) (cells: Cell list) : Column =
            match Column.ofCells n ty cells with
            | Result.Ok c -> c
            | Result.Error e -> failtestf "the generator drew a column %s it cannot build (seed now %d): %A" n r.Value e

        let columns =
            picked
            |> List.map (fun (n, ty) ->
                // A column whose type is not its schema entry's: its cells are drawn for ITS type,
                // so it builds, and the schema still says `ty` — clause (c)'s `TypeMismatch`.
                let ty' = if chance 10 then modelTypes[draw modelTypes.Length] else ty

                let len = if chance 10 then rows + 1 else rows
                build n ty' [ for _ in 1..len -> cellOf ty' ])

        let columns =
            if chance 10 then
                columns @ [ Column.ofInts "zz" (Vector.init rows (fun _ -> 1)) AllValid ]
            elif chance 10 && not columns.IsEmpty then
                List.tail columns
            else
                columns

        // Out of schema order one draw in three — valid, and outside the normal form.
        let columns = if draw 3 = 0 then List.rev columns else columns

        { Schema = picked |> List.map (fun (n, ty) -> Field.create n ty)
          Columns = columns }

    /// The column tally: the tables drawn; those `validate` accepted, and of those the ones NOT in
    /// normal form; the decode probes the model declined as a lenient-ingest arm, and those
    /// compared; and the refusals reached, by class name (the first word of the class).
    let tally: Tally =
        Tally.empty [ "Tables"; "Valid"; "OutsideNormalForm"; "OutOfModel"; "Decoded" ]
        |> Tally.sets [ "Refusals" ]

    /// One table, asked of production and of the model: `validate`; `encodeJson`; `decodeJson` of
    /// that encoding; and — for a table `validate` accepts — the round trip against the model's
    /// NORMAL FORM, at the `JVal` and through the canonical string and the parser. `expected`
    /// chooses what the round trip is held to: the normal form (the theorem), or the table itself
    /// (the literal form the theorem refutes — the go-red).
    let probe (literal: bool) (label: string) (table: Table) (t: Tally) : Tally =
        let m = toModelTable table
        let t = Tally.bump "Tables" t

        // validate
        let prodValid = Table.validate table
        let modelValid = WireColumn.validate host m

        let prodV =
            match prodValid with
            | Result.Ok() -> "ok"
            | Result.Error e -> "refused " + prodClass e

        let modelV =
            match modelValid with
            | WireColumn.Good _ -> "ok"
            | WireColumn.Bad e -> "refused " + modelClass e

        let t = compareText label "validate" prodV modelV t

        let t =
            match prodValid with
            | Result.Error e -> Tally.reach "Refusals" ((prodClass e).Split(' ')[0]) t
            | Result.Ok() -> Tally.bump "Valid" t

        // encode — of ANY table, valid or not: `encodeJson` is total and papers over a malformed one
        let prodJson = ColumnCodec.encodeJson (Embedded table)
        let modelJson = WireColumn.encode_json host (WireColumn.Embedded m)

        let t =
            compareText label "encodeJson" (Json.render prodJson) (Json.render (canonOfModel modelJson)) t

        // decode of the encoding. A NaN the encoder wrote is a `JFloat nan`, which the model's
        // decoder is handed as it is.
        let prodBack = ColumnCodec.decodeJson prodJson
        let modelBack = WireColumn.decode_json host modelJson

        // (An INVALID table can encode to a document the model declines — a float in a timestamp
        // or a decimal column is one of its lenient-ingest arms — and that one is skipped.)
        let t =
            match modelBack with
            | WireColumn.Bad WireColumn.OutOfModel -> Tally.bump "OutOfModel" t
            | _ ->
                t
                |> Tally.bumpIf (showProd prodBack = showModel modelBack) "Decoded"
                |> compareText label "decodeJson (encodeJson t)" (showProd prodBack) (showModel modelBack)

        // the round trip, for a table validate accepts
        match prodValid with
        | Result.Error _ -> t
        | Result.Ok() ->
            let normal = ofModelTable (WireColumn.normal_table host m)

            let t =
                Tally.bumpIf (showSource (Embedded normal) <> showSource (Embedded table)) "OutsideNormalForm" t

            let expected = "ok " + showSource (Embedded(if literal then table else normal))

            let t =
                compareText label "the round trip at the JVal" (showProd prodBack) expected t

            // … and the round trip a consumer performs: the canonical string, then the parser.
            let viaString = ColumnCodec.decode (ColumnCodec.encode (Embedded table))
            compareText label "the round trip through the canonical string" (showProd viaString) expected t

    let generated (literal: bool) (seed: int) (trials: int) (wild: int) : Tally =
        tally
        |> Differential.seeded trials (ref seed) (fun i r t ->
            r, probe literal (sprintf "generated seed=%d iteration=%d" seed i) (genTable r wild) t)

    /// Documents no encoder writes, each decoded by production and by the model: the decode-only
    /// acceptances the model's own lemmas name, and the lenient-ingest arms it declines.
    let handWritten: (string * string) list =
        [ "surplus root member",
          """{"$type":"x","schema":[{"name":"a","type":"int"}],"columns":{"a":{"values":[1],"validity":[true]}}}"""
          "surplus column",
          """{"schema":[{"name":"a","type":"int"}],"columns":{"a":{"values":[1],"validity":[true]},"zz":{"values":[true],"validity":[true]}}}"""
          "ref beside columns", """{"ref":"orders","schema":[],"columns":{"a":{"values":[1],"validity":[true]}}}"""
          "ref alone", """{"ref":"orders"}"""
          "ref not a string", """{"ref":3}"""
          "masked slot holds anything",
          """{"schema":[{"name":"a","type":"date"}],"columns":{"a":{"values":["","not a date"],"validity":[false,false]}}}"""
          "empty schema, columns not an object", """{"schema":[],"columns":true}"""
          "no columns member", """{"schema":[]}"""
          "missing column", """{"schema":[{"name":"a","type":"int"}],"columns":{}}"""
          // (Phase 462: a document with a repeated column KEY no longer parses — the parser refuses
          // the text before the codec sees it — so that arm is reached only from a value.)
          "duplicate schema name",
          """{"schema":[{"name":"a","type":"int"},{"name":"a","type":"int"}],"columns":{"a":{"values":[1],"validity":[true]}}}"""
          "unknown type",
          """{"schema":[{"name":"a","type":"money"}],"columns":{"a":{"values":[1],"validity":[true]}}}"""
          "schema not an array", """{"schema":{},"columns":{}}"""
          "schema entry without a type", """{"schema":[{"name":"a"}],"columns":{}}"""
          "values and validity disagree",
          """{"schema":[{"name":"a","type":"int"}],"columns":{"a":{"values":[1,2],"validity":[true]}}}"""
          "validity not a bool",
          """{"schema":[{"name":"a","type":"int"}],"columns":{"a":{"values":[1],"validity":[1]}}}"""
          "bool in an int column",
          """{"schema":[{"name":"a","type":"int"}],"columns":{"a":{"values":[true],"validity":[true]}}}"""
          "int token in a float column",
          """{"schema":[{"name":"a","type":"float"}],"columns":{"a":{"values":[2],"validity":[true]}}}"""
          "int token in a decimal column",
          """{"schema":[{"name":"a","type":"decimal"}],"columns":{"a":{"values":[12],"validity":[true]}}}"""
          "decimal text normalised",
          """{"schema":[{"name":"a","type":"decimal"}],"columns":{"a":{"values":["12.50"],"validity":[true]}}}"""
          "decimal text not decimal",
          """{"schema":[{"name":"a","type":"decimal"}],"columns":{"a":{"values":["1e3"],"validity":[true]}}}"""
          "impossible date",
          """{"schema":[{"name":"a","type":"date"}],"columns":{"a":{"values":["2026-02-30"],"validity":[true]}}}"""
          "ragged columns",
          """{"schema":[{"name":"a","type":"int"},{"name":"b","type":"int"}],"columns":{"a":{"values":[1,2],"validity":[true,true]},"b":{"values":[3],"validity":[true]}}}"""
          // the lenient-ingest arms — the model answers OutOfModel, and the probe skips them
          "lenient: no schema", """{"columns":{"a":[1,2]}}"""
          "lenient: bare array column", """{"schema":[{"name":"a","type":"int"}],"columns":{"a":[1,2]}}"""
          "lenient: no validity mask", """{"schema":[{"name":"a","type":"int"}],"columns":{"a":{"values":[1,2]}}}"""
          "lenient: epoch in a timestamp column",
          """{"schema":[{"name":"a","type":"timestamp"}],"columns":{"a":{"values":[1752000000],"validity":[true]}}}"""
          "lenient: whole float in a decimal column",
          """{"schema":[{"name":"a","type":"decimal"}],"columns":{"a":{"values":[3000000000],"validity":[true]}}}""" ]

    let decodeProbe (label: string) (text: string) (t: Tally) : Tally =
        match Json.parse text with
        | Result.Error m -> failtestf "%s did not parse: %s" label m
        | Result.Ok el ->
            let model = WireColumn.decode_json host (canonToModel el)

            match model with
            | WireColumn.Bad WireColumn.OutOfModel -> Tally.bump "OutOfModel" t
            | _ ->
                let prod = ColumnCodec.decodeJson el

                let t =
                    match prod with
                    | Result.Error e -> Tally.reach "Refusals" ((prodClass e).Split(' ')[0]) t
                    | Result.Ok _ -> t

                t
                |> Tally.bumpIf (showProd prod = showModel model) "Decoded"
                |> compareText label "decodeJson" (showProd prod) (showModel model)

module UnitDiff =

    open Fuaran.Core.Tests.ColumnOracle.Units


    /// Production's `expected` prose as the model's enumeration — the one place the two spell a
    /// refusal differently, mapped once.
    let private expectOf (prose: string) : string =
        match prose with
        | "a unit" -> "ExpUnit"
        | "'}' closing the annotation" -> "ExpCloseAnnotation"
        | "']' closing the symbol" -> "ExpCloseSymbol"
        | "the digits of an exponent" -> "ExpExponentDigits"
        | "an exponent within ±2147483647" -> "ExpExponentRange"
        | "'.', '/' or the end" -> "ExpSepOrEnd"
        | "'.', '/' or ')'" -> "ExpSepOrClose"
        | p when p.StartsWith "')' closing the '(' at " ->
            "ExpCloseParen " + p.Substring("')' closing the '(' at ".Length)
        | p -> "<unmapped: " + p + ">"

    let private modelExpect (e: ModelUnit.expect) : string =
        match e with
        | ModelUnit.ExpUnit -> "ExpUnit"
        | ModelUnit.ExpCloseAnnotation -> "ExpCloseAnnotation"
        | ModelUnit.ExpCloseSymbol -> "ExpCloseSymbol"
        | ModelUnit.ExpExponentDigits -> "ExpExponentDigits"
        | ModelUnit.ExpExponentRange -> "ExpExponentRange"
        | ModelUnit.ExpCloseParen opened -> "ExpCloseParen " + opened.ToString()
        | ModelUnit.ExpSepOrEnd -> "ExpSepOrEnd"
        | ModelUnit.ExpSepOrClose -> "ExpSepOrClose"

    /// A refusal as the class, the token, the position and (for `Malformed`) what was expected.
    let prodRefusal (r: UnitRefusal) : string =
        let at (case: string) (token: string) (position: int) =
            case + " '" + token + "' @" + string position

        match r with
        | UnitRefusal.Empty -> "Empty"
        | UnitRefusal.Malformed(found, position, expected) ->
            at "Malformed" found position + " expecting " + expectOf expected
        | UnitRefusal.UnknownAtom(token, position) -> at "UnknownAtom" token position
        | UnitRefusal.Annotation(token, position) -> at "Annotation" token position
        | UnitRefusal.ArbitraryUnit(token, position) -> at "ArbitraryUnit" token position
        | UnitRefusal.NonRatioUnit(token, position) -> at "NonRatioUnit" token position
        | UnitRefusal.PrefixNotAllowed(token, position) -> at "PrefixNotAllowed" token position
        | UnitRefusal.NumericFactor(token, position) -> at "NumericFactor" token position

    /// The model's refusal, its token read back out of the TEXT at the position it names: the
    /// alphabet carries every character outside it as one `Other`, so the token's spelling is the
    /// input's, and its position and length are the model's.
    let modelRefusal (text: string) (r: ModelUnit.refusal) : string =
        let at (case: string) (token: ModelUnit.text) (position: bigint) =
            let start = int position
            let length = min (List.length token) (max 0 (text.Length - start))
            case + " '" + text.Substring(start, length) + "' @" + position.ToString()

        match r with
        | ModelUnit.Empty -> "Empty"
        | ModelUnit.Malformed(found, position, expected) ->
            at "Malformed" found position + " expecting " + modelExpect expected
        | ModelUnit.UnknownAtom(token, position) -> at "UnknownAtom" token position
        | ModelUnit.Annotation(token, position) -> at "Annotation" token position
        | ModelUnit.ArbitraryUnit(token, position) -> at "ArbitraryUnit" token position
        | ModelUnit.NonRatioUnit(token, position) -> at "NonRatioUnit" token position
        | ModelUnit.PrefixNotAllowed(token, position) -> at "PrefixNotAllowed" token position
        | ModelUnit.NumericFactor(token, position) -> at "NumericFactor" token position

    /// The unit tally: the texts asked of both parsers, those both parsed, and the units the model
    /// found not canonical; the pairs asked of the algebra, the compatible ones, and those whose big
    /// power overflowed; for the go-red, the reordered pairs and those the concatenation lost; and
    /// the refusal classes reached.
    let tally: Tally =
        Tally.empty
            [ "Texts"
              "Parsed"
              "NotCanonical"
              "Pairs"
              "Compatible"
              "Overflowed"
              "Reordered" ]
        |> Tally.sets [ "Refusals" ]

    let private compareText = ColumnDiff.compareText

    /// One text, asked of `Unit.parse` and of the model's: a unit on both sides renders to one text
    /// and is canonical in the model's sense; a refusal on both sides is one class, token, position
    /// and expectation.
    let probeText (label: string) (text: string) (t: Tally) : Tally =
        let t = Tally.bump "Texts" t

        match Unit.parse text, ModelUnit.parse (toText text) with
        | Ok pu, ModelUnit.Ok mu ->
            t
            |> Tally.bump "Parsed"
            |> compareText label ("render of " + text) (Unit.render pu) (modelRender mu)
            |> Tally.bumpIf (not (ModelUnit.canonical mu)) "NotCanonical"
        | Error pr, ModelUnit.Refused mr ->
            t
            |> Tally.reach "Refusals" ((prodRefusal pr).Split(' ')[0])
            |> compareText label ("refusal of " + text) (prodRefusal pr) (modelRefusal text mr)
        | Ok pu, ModelUnit.Refused mr ->
            compareText label ("parse of " + text) ("ok " + Unit.render pu) ("refused " + modelRefusal text mr) t
        | Error pr, ModelUnit.Ok mu ->
            compareText label ("parse of " + text) ("refused " + prodRefusal pr) ("ok " + modelRender mu) t

    let private prodUnit (f: unit -> UnitOfMeasure) : string =
        try
            Unit.render (f ())
        with :? System.ArgumentException ->
            "overflow"

    let private modelUnit (r: FStar_Pervasives_Native.option<ModelUnit.uom>) : string =
        match r with
        | FStar_Pervasives_Native.Some m -> modelRender m
        | FStar_Pervasives_Native.None -> "overflow"

    let private prodFactor (a: UnitOfMeasure) (b: UnitOfMeasure) : string =
        try
            match Unit.conversionFactor a b with
            | Ok r -> r.Numerator.ToString() + "/" + r.Denominator.ToString()
            | Error(UnitConversionRefusal.Incompatible _) -> "incompatible"
        with :? System.ArgumentException ->
            "overflow"

    let private modelFactor
        (r: FStar_Pervasives_Native.option<ModelUnit.result<ModelUnit.rat, ModelUnit.conv_refusal>>)
        : string =
        match r with
        | FStar_Pervasives_Native.Some(ModelUnit.Ok q) -> q.rn.ToString() + "/" + q.rd.ToString()
        | FStar_Pervasives_Native.Some(ModelUnit.Refused _) -> "incompatible"
        | FStar_Pervasives_Native.None -> "overflow"

    /// One pair, asked of the algebra on both sides: `mul`, `div`, `pow` (at a drawn exponent, and
    /// at one that leaves the bound), `compatible` and `conversionFactor`.
    let probePair (label: string) (a: UnitOfMeasure) (b: UnitOfMeasure) (n: int) (t: Tally) : Tally =
        let am = toModel a
        let bm = toModel b
        let check = compareText label
        let big = 1000000000
        let powBig = prodUnit (fun () -> Unit.pow a big)

        t
        |> Tally.bump "Pairs"
        |> check "mul" (prodUnit (fun () -> Unit.mul a b)) (modelUnit (ModelUnit.mul am bm))
        |> check "div" (prodUnit (fun () -> Unit.div a b)) (modelUnit (ModelUnit.div am bm))
        |> check (sprintf "pow %d" n) (prodUnit (fun () -> Unit.pow a n)) (modelUnit (ModelUnit.pow am (bigint n)))
        |> check (sprintf "pow %d" big) powBig (modelUnit (ModelUnit.pow am (bigint big)))
        |> Tally.bumpIf (powBig = "overflow") "Overflowed"
        |> check "compatible" (string (Unit.compatible a b)) (string (ModelUnit.compatible am bm))
        |> Tally.bumpIf (Unit.compatible a b) "Compatible"
        |> check "conversionFactor" (prodFactor a b) (modelFactor (ModelUnit.conversion_factor am bm))
        |> check "conversionFactor reversed" (prodFactor b a) (modelFactor (ModelUnit.conversion_factor bm am))

    /// The GO-RED: the canonicaliser that SKIPS THE MERGE. The model's `render` is handed the
    /// CONCATENATION of the two factor lists where `mul` merges them, and compared with production's
    /// `render (mul a b)`. It loses on exactly the draws whose concatenation is not already the merge:
    /// a shared atom (merged into one factor, or cancelled), or an atom of `b` that sorts before one
    /// of `a` among the numerators or among the denominators. `Reordered` counts those by that
    /// description, computed here and not by rendering, so the equality below is a claim.
    let probeConcat (label: string) (a: UnitOfMeasure) (b: UnitOfMeasure) (t: Tally) : Tally =
        let am = toModel a
        let bm = toModel b
        let concat: ModelUnit.uom = { factors = am.factors @ bm.factors }

        let shared =
            am.factors
            |> List.exists (fun (x, _) -> bm.factors |> List.exists (fun (y, _) -> x = y))

        let outOfOrder (keep: bigint -> bool) =
            let ap = am.factors |> List.filter (fun (_, e) -> keep e)
            let bp = bm.factors |> List.filter (fun (_, e) -> keep e)

            ap
            |> List.exists (fun (x, _) -> bp |> List.exists (fun (y, _) -> not (ModelUnit.atom_lt x y)))

        let reordered =
            shared || outOfOrder (fun e -> e > 0I) || outOfOrder (fun e -> e < 0I)

        let t = t |> Tally.bump "Pairs" |> Tally.bumpIf reordered "Reordered"

        match
            (try
                Some(Unit.mul a b)
             with :? System.ArgumentException ->
                 None)
        with
        | None -> t
        | Some p ->
            t
            |> Differential.compare
                (Differential.pair Unit.render modelRender)
                (sprintf "%s: render (mul a b) against the model's render of the concatenation" label)
                p
                concat

    // ---- the pools ----

    /// The atoms the SOURCE admits, read from `Unit.fs`'s vocabulary table as `(symbol, metric)` —
    /// as `UnitTests` reads them — so the pool reaches every admitted atom whatever the table holds.
    let sourceAtoms: (string * bool) list =
        let row =
            System.Text.RegularExpressions.Regex(@"^\s+""(?<sym>[^""]+)"", def (?<metric>true|false) ")

        File.ReadAllLines(Snapshots.repoFile "src/Fuaran.Core.Unit/Unit.fs")
        |> Array.choose (fun l ->
            let m = row.Match l

            if m.Success then
                Some(m.Groups["sym"].Value, m.Groups["metric"].Value = "true")
            else
                None)
        |> List.ofArray

    let private prefixes =
        [| "Y"
           "Z"
           "E"
           "P"
           "T"
           "G"
           "M"
           "k"
           "h"
           "da"
           "d"
           "c"
           "m"
           "u"
           "n"
           "p"
           "f"
           "a"
           "z"
           "y" |]

    /// Every admitted atom bare, and every metric atom under every prefix (the alias `l` too), as the
    /// texts production renders them: the pool's floor, before the draws.
    let atomTexts: string list =
        let atoms = sourceAtoms @ [ "[GBP]", true; "[USD]", true; "[JPY]", true; "l", true ]

        [ for sym, metric in atoms do
              yield sym

              if metric then
                  for p in prefixes do
                      yield p + sym ]

    /// Drawn units: products of one to three prefixed atoms under exponents in -3..3, as texts the
    /// shipped parser reads (a prefixed text the parser reads as another atom — `Pa`, `cd` — is kept
    /// as it reads it). Seeded, so a red names a unit a rerun reproduces.
    let drawn (seed: int) (count: int) : UnitOfMeasure list =
        let rng = System.Random(seed)

        let atoms =
            (sourceAtoms @ [ "[GBP]", true; "[USD]", true; "[JPY]", true ]) |> Array.ofList

        let one () =
            let sym, metric = atoms[rng.Next atoms.Length]

            let p =
                if metric && rng.Next 3 = 0 then
                    prefixes[rng.Next prefixes.Length]
                else
                    ""

            let u =
                match Unit.parse (p + sym) with
                | Ok u -> u
                | Error _ ->
                    match Unit.parse sym with
                    | Ok u -> u
                    | Error e -> failtestf "the pool's atom %s does not parse: %A" sym e

            Unit.pow u (rng.Next 7 - 3)

        [ for _ in 1..count ->
              let k = 1 + rng.Next 3
              List.init k (fun _ -> one ()) |> List.fold Unit.mul Unit.dimensionless ]

    /// Texts outside the canonical form the parser still reads, and texts it refuses — one per
    /// refusal class at least, each with the token and position the class names.
    let corpus: string list =
        [ "s-1.m"
          "m.s-1"
          "ml"
          "L/l"
          "kg/(m.s2)"
          "(m)"
          "((m/s)/s)"
          "/min"
          "m/s/s"
          "m2.m-2"
          "1"
          "1/s"
          "m.1"
          "k[GBP]"
          "[GBP]/h"
          "m+2"
          "m-0"
          "m2147483647"
          "m-2147483647"
          ""
          "m2147483648"
          "m-"
          "m+"
          "{dry}"
          "m{dry}"
          "m{dry"
          "[GBP"
          "[IU]"
          "k[IU]"
          "Cel"
          "kCel"
          "B[SPL]"
          "kmin"
          "k%"
          "10*3"
          "10^-3"
          "2.m"
          "xyz"
          "m)"
          "(m"
          "m."
          ".m"
          "m//s"
          "m s"
          "m,s"
          "(m.s"
          "m)s"
          "[AAA]"
          "da[GBP]"
          "mol/L"
          "[in_i]2/[ft_i]" ]

    /// The whole differential over a seed: every atom text, the corpus, and `count` drawn units, as
    /// texts and in pairs.
    let generated (seed: int) (count: int) : Tally =
        let units = drawn seed count
        let rng = System.Random(seed + 1)

        // Consecutive draws are rarely compatible, so every unit is also paired with itself scaled
        // by a per cent — one dimension, factor 100 — which is where the factor's exactness is asked.
        let percent =
            match Unit.parse "%" with
            | Ok u -> u
            | Error e -> failtestf "the per cent does not parse: %A" e

        tally
        |> Differential.over atomTexts (probeText "atom")
        |> Differential.over corpus (probeText "corpus")
        |> Differential.over units (fun u -> probeText "drawn" (Unit.render u))
        |> Differential.over (List.pairwise units) (fun (a, b) -> probePair "pair" a b (rng.Next 9 - 4))
        |> Differential.over units (fun u -> probePair "scaled" u (Unit.mul u percent) (rng.Next 9 - 4))

    let concatenated (seed: int) (count: int) : Tally =
        tally
        |> Differential.over (drawn seed count |> List.pairwise) (fun (a, b) -> probeConcat "pair" a b)

// ---------------------------------------------------------------------------
//  Phase 478 — THE MODEL STAMP on the emitted vectors.
//
//  Each exporter evaluates the extracted model that asks a vector's question, through the bridges in
//  the fixtures library (`VectorOracle`), and stamps the vector `["reference", "model:<Model>"]` — or
//  fails the export on a disagreement. The committed files are held to a fresh render by the
//  `Exporters` family, so a stamp that is in a file was earned by the render that wrote it. These
//  cases hold the rest: every vector carries a well-formed stamp; the model stamps are where the
//  models answer (a census, so an exporter that silently stops asking its model is red); and a
//  disagreement refuses, naming the vector and the ladder row.
// ---------------------------------------------------------------------------

module VectorStamps =

    /// Every `answeredBy` stamp in one committed file: one per vector of a `vectors` file, and the
    /// file's own for a `lines` file, whose lines carry no member.
    let internal stampsOf (text: string) : Result<AnsweredBy list, string> =
        let memberOf k fields =
            fields |> List.tryFind (fun (n, _) -> n = k) |> Option.map snd

        let readAll entries =
            entries
            |> List.fold
                (fun acc (at, entry) ->
                    acc
                    |> Result.bind (fun read ->
                        match entry with
                        | Some e ->
                            AnsweredBy.read e
                            |> Result.map (fun a -> read @ [ a ])
                            |> Result.mapError (fun m -> at + ": " + m)
                        | None -> Error(at + " carries no `answeredBy`")))
                (Ok [])

        match Json.parse text with
        | Ok(JObj fields) ->
            match memberOf "vectors" fields, memberOf "lines" fields with
            | Some(JArr vectors), _ ->
                vectors
                |> List.mapi (fun i v ->
                    match v with
                    | JObj vf -> sprintf "vector %d" i, memberOf "answeredBy" vf
                    | _ -> sprintf "vector %d" i, None)
                |> readAll
            | None, Some(JArr _) -> readAll [ "the file", memberOf "answeredBy" fields ]
            | _ -> Error "neither a `vectors` nor a `lines` file"
        | Ok _ -> Error "not an object"
        | Error e -> Error("does not parse: " + e)

    /// The committed vector files: every emitted file under `conformance/` but the manifests and the
    /// IDL schema, which index and declare rather than answer.
    let vectorFiles () : string list =
        System.IO.Directory.GetFiles(OwnedConformance.root (), "*.json", System.IO.SearchOption.AllDirectories)
        |> Array.filter (fun p ->
            let name = System.IO.Path.GetFileName p
            name <> "manifest.json" && not (name.EndsWith ".schema.json"))
        |> Array.sort
        |> List.ofArray

    let relative (p: string) =
        System.IO.Path.GetRelativePath(OwnedConformance.root (), p).Replace(System.IO.Path.DirectorySeparatorChar, '/')

    /// The census: per file, how many vectors each extracted model answers. Every file not named here
    /// is answered by the reference alone. A model wired into an exporter is one row here.
    let modelStamps: (string * (string * int)) list =
        [ "apply/skeleton-apply.json", ("TreeOps", 25)
          "chain/skeleton-chain.json", ("Chain", 16)
          "dag/skeleton-dag.json", ("Chain", 4)
          "decode/decode-rejects.json", ("JsonParse", 8)
          "decode/decode-rejects.json", ("DecodeLayer", 28)
          "escape/string-escape.json", ("WireCanon", 1)
          "laws/capability-laws.json", ("Capability", 48)
          "laws/decimal-laws.json", ("DecimalText", 49)
          "laws/decimal-laws.json", ("WireColumn", 7)
          "laws/field-laws.json", ("WireColumn", 24)
          "laws/propagation-laws.json", ("Propagation", 73)
          "premises/int53.json", ("JsonParse", 29)
          "premises/recursion-depth.json", ("JsonParse", 13)
          "premises/string-model.json", ("JsonParse", 10)
          "refusals/codec-refusals.json", ("JsonParse", 59)
          "refusals/codec-refusals.json", ("WireColumn", 13)
          "refusals/seam-refusals.json", ("Capability", 48)
          "refusals/seam-refusals.json", ("Query", 45) ]

    [<Tests>]
    let tests =
        testList
            "Proofs.Oracle"
            [ testCase "every committed vector carries an answeredBy stamp a host can read"
              <| fun _ ->
                  let files = vectorFiles ()
                  Expect.isGreaterThanOrEqual (List.length files) 15 "the vector files were found"

                  for p in files do
                      match stampsOf (System.IO.File.ReadAllText p) with
                      | Ok stamps -> Expect.isNonEmpty stamps (relative p + " carries stamps")
                      | Error e -> failtestf "%s: %s" (relative p) e

              testCase "the model stamps are where the extracted models answer, and nowhere else"
              <| fun _ ->
                  for p in vectorFiles () do
                      let stamps =
                          match stampsOf (System.IO.File.ReadAllText p) with
                          | Ok s -> s
                          | Error e -> failtestf "%s: %s" (relative p) e

                      let models =
                          stamps
                          |> List.choose (function
                              | AnsweredBy.Model m -> Some m
                              | AnsweredBy.Reference -> None)
                          |> List.countBy id

                      let expected =
                          modelStamps |> List.filter (fun (f, _) -> f = relative p) |> List.map snd

                      Expect.equal models expected (relative p + "'s model stamps")

              testCase "a disagreement refuses the stamp, naming the vector, the model and the ladder row"
              <| fun _ ->
                  let answer text : ModelAnswer =
                      { Model = "Propagation"
                        LadderRow = "propagation-differential"
                        Answer = text }

                  Expect.equal
                      (AnsweredBy.agree "v-1" "{\"dirty\":[\"1\"]}" (Some(answer "{\"dirty\":[\"1\"]}")))
                      (Ok(AnsweredBy.Model "Propagation"))
                      "an agreeing model answer earns the model stamp"

                  Expect.equal
                      (AnsweredBy.agree "v-1" "x" None)
                      (Ok AnsweredBy.Reference)
                      "no model answer is the reference's"

                  match AnsweredBy.agree "v-1" "{\"dirty\":[\"1\"]}" (Some(answer "{\"dirty\":[]}")) with
                  | Ok a -> failtestf "a perturbed model answer was stamped %A" a
                  | Error e ->
                      for part in [ "v-1"; "Propagation"; "propagation-differential" ] do
                          Expect.stringContains e part "the refusal names the vector, the model and the row"

              testCase "go-red: an exporter handed a disagreeing model answer refuses to render the vector"
              <| fun _ ->
                  let reference = CorpusCell.Row [ "dirty", CorpusFile.str "1" ]

                  let perturbed: VectorOracle.Answer =
                      { Model = VectorOracle.PropagationModel.Model
                        LadderRow = VectorOracle.PropagationModel.LadderRow
                        Cell = CorpusCell.Row [ "dirty", CorpusFile.str "2" ] }

                  Expect.throwsC
                      (fun () ->
                          VectorOracle.stamp "propagation-0-dirty-set" reference (Some perturbed)
                          |> ignore)
                      (fun e -> Expect.stringContains e.Message "propagation-0-dirty-set" "names the vector")

              testCase "a stamp outside the two shapes is refused on read"
              <| fun _ ->
                  for bad in
                      [ JArr []
                        JArr [ JStr "model:Propagation" ]
                        JArr [ JStr "reference"; JStr "model:" ]
                        JArr [ JStr "reference"; JStr "oracle:Propagation" ]
                        JStr "reference" ] do
                      Expect.isError (AnsweredBy.read bad) (sprintf "%A is refused" bad) ]
