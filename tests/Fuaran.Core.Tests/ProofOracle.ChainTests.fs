module Fuaran.Core.Tests.ProofOracle.ChainTests

// The `Proofs.Oracle` differential for `proofs/Chain.fst`: the extracted model
// (`proofs/oracle/Chain.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.FoldFixtures
open Fuaran.Core.Tests.ProofOracle.Bridges

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

/// The order `Dag.firstBreak` walks a DAG since Phase 311: the whole DAG's drain (parents first,
/// the smallest key among the ready nodes), then every node on or below a cycle in key order. Read
/// off production rather than re-derived: `totalOrderBy` at a constant key is that drain, and where
/// a cycle leaves nodes unplaced the drain of the rest — which no unplaced node is a parent of — is
/// the placed prefix.
let private firstBreakWalk (dag: Dag.T<'Op>) : string list =
    match Dag.totalOrderBy (fun _ -> 0) dag with
    | Ok order -> order
    | Error { Unplaced = unplaced } ->
        let rest: Dag.T<'Op> =
            DagOf.nodes (dag.Nodes |> Map.filter (fun k _ -> not (List.contains k unplaced)))

        (match Dag.totalOrderBy (fun _ -> 0) rest with
         | Ok order -> order
         | Error _ -> [])
        @ unplaced

/// A production `Dag.T` as the model reads it — in the order `Dag.firstBreak` walks
/// (`firstBreakWalk`; `Map.toList`'s id order until Phase 311); the map KEY beside the node,
/// because production compares the key and never the node's own `Id` field; and the actor as the
/// string `Actor.encode` produces, which is the only thing the hash pre-image ever sees of it.
let private toChainEntries (dag: Dag.T<'Op>) : Chain.entry<'Op> list =
    firstBreakWalk dag
    |> List.map (fun k -> k, dag.Nodes[k])
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
    prodDagVerdict (Dag.firstBreak (OpStream.canonical prodHash w) dag),
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
    let baseId, d0 =
        Dag.append (OpStream.canonical hashFn w) (Human "base") baseOp "" Dag.empty
        |> Reference.built

    let mutable d = d0

    for (i, ops) in List.indexed lanes do
        let actor = Human("lane-" + string i)
        let mutable h = baseId

        for op in ops do
            let id, d' =
                Dag.append (OpStream.canonical hashFn w) actor op h d |> Reference.built

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
              yield sprintf "op@%s" k, DagOf.nodes (Map.add k { n with Op = o' } dag.Nodes)

          let a' = Human "tamperer"

          if a' <> n.Actor then
              yield sprintf "actor@%s" k, DagOf.nodes (Map.add k { n with Actor = a' } dag.Nodes)

          match
              nodes
              |> List.tryPick (fun (p, _) -> if p <> k && n.Parents <> [ p ] then Some p else None)
          with
          | Some p -> yield sprintf "reparent@%s" k, DagOf.nodes (Map.add k { n with Parents = [ p ] } dag.Nodes)
          | None -> ()

          for p in n.Parents do
              yield sprintf "drop-parent@%s" p, DagOf.nodes (Map.remove p dag.Nodes) ]

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

        let singles = dagTampers otherOp dag

        for (what, tampered) in singles do
            let p = compare what tampered
            t <- { t with Tampers = t.Tampers + 1 }

            if p <> DagIntact then
                t <- { t with Detected = t.Detected + 1 }

        // Phase 311: TWO tampers at once, so the walkers must agree on WHICH break comes first —
        // production names the earliest in its walk, and the model is handed that walk.
        let changedKey (d: Dag.T<'Op>) =
            d.Nodes
            |> Map.toList
            |> List.tryFind (fun (k, n) ->
                match Map.tryFind k dag.Nodes with
                | Some o -> o.Parents <> n.Parents || o.Actor <> n.Actor || w.Encode o.Op <> w.Encode n.Op
                | None -> true)
            |> Option.map fst

        // one in-place tamper per node (a tamper that removes a node changes no node in place)
        let perNode =
            singles
            |> List.choose (fun (what, d) ->
                changedKey d
                |> Option.filter d.Nodes.ContainsKey
                |> Option.map (fun k -> k, what, d.Nodes[k]))
            |> List.distinctBy (fun (k, _, _) -> k)

        for (i, (k1, w1, n1)) in List.indexed perNode do
            for (j, (k2, w2, n2)) in List.indexed perNode do
                if i < j then
                    let both: Dag.T<'Op> = DagOf.nodes (dag.Nodes |> Map.add k1 n1 |> Map.add k2 n2)

                    let p = compare (w1 + " and " + w2) both
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
    prodChainVerdict (OpStream.firstChainBreak (OpStream.canonical prodHash w) rs),
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
        match OpStream.append (OpStream.canonical hashFn w) actor op st rs with
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
//  The model's `compact` / `replay_from` / `verify_across` beside `SnapshotMatrix.compact`,
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
    | StateHashed -> SnapshotMatrix.compact hashFn stateEnc w state0 rs n
    | ChainOnly -> SnapshotMatrix.compactChainOnly hashFn w state0 rs n

let private prodVerifyAcross
    (mode: SnapshotMode)
    (hashFn: HashFn)
    (stateEnc: 'State -> string)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (snap: Snapshot<'State>)
    (tail: OpRecord<'Op> list)
    : bool =
    match mode with
    | StateHashed -> SnapshotMatrix.verifyAcross hashFn stateEnc w snap tail
    | ChainOnly -> SnapshotMatrix.verifyAcrossChainOnly hashFn w snap tail

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
                    StreamConfig.canonical.Genesis
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
                let pFrom = prodReplayVerdict (SnapshotMatrix.replayFrom w snap tail)

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
                let whole = OpStream.verifyChain (OpStream.canonical prodHash w) rs

                let prefix =
                    OpStream.verifyChain (OpStream.canonical prodHash w) (List.truncate n rs)

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
/// The shape of `Conformance.attestationLawsAt`'s forgery, spelled here over a step list because a
/// splice changes the chain's LENGTH, which a record-for-record rehash cannot.
let private remint (hashFn: HashFn) (encode: 'Op -> string) (steps: (Actor * 'Op) list) : OpRecord<'Op> list =
    (([], StreamConfig.canonical.Genesis, 0), steps)
    ||> List.fold (fun (acc, prev, i) (actor, op) ->
        let h = hashFn prev (StreamConfig.canonical.Payload i actor (encode op))

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
            let pHead = OpStream.head StreamConfig.canonical intact
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
            let pAtt = Attestation.attestHead sink StreamConfig.canonical intact
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
                    (OpStream.verifyChain (OpStream.canonical prodHash w) rs
                     && Attestation.verifyAttestation verifier StreamConfig.canonical a rs),
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

                    let walker = OpStream.verifyChain (OpStream.canonical prodHash w) pForged
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
                      OpJson = Canonical.write Rendering.CrossHost op }
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
//  Phase 197 — the linear chain's ENVELOPE PARSE, measured against production's payload
//
//  `Chain.fst` section 6b discharges the linear side's premise: `rec_payload`'s four-way envelope
//  `{"seq":<n>,"actor":<a>,"op":<o>}` is parsed back into its three fields, each split forced by
//  the leading comma of the separator that follows it, under three conditions on the alphabets —
//  the numeral carries no comma and `show` is injective (`seq_numeral_code`), the actor code is
//  prefix-free (`actor_code_prefix_free`, Phase 145's premise again), and the op codec is
//  injective (premise 4, nothing about its alphabet). The model cannot check any of those of
//  production, so this is where they are measured: that production's payload IS the model's
//  envelope, that the parse recovers all three fields over the adversarial population, that each
//  alphabet condition is load-bearing (the parse loses under a perturbed code), and that the
//  theorem the discharge buys — `payload_splice_breaks_chain` — holds beside production's walker.
// ---------------------------------------------------------------------------

/// Op encodings for the ENVELOPE, chosen to break a parse if anything can: the op is the last
/// field, so these carry the envelope's own separators and its closing brace.
let private adversarialEnvelopeOps: string list =
    [ ""
      "}"
      "{}"
      ",\"op\":"
      "\"}"
      "{\"seq\":1,\"actor\":{},\"op\":{}}"
      "A|x|t" ]

/// Sequence numbers for the envelope, including the ones a thousands-separated renderer would
/// print differently. Bounded at four digits because the model's `pos` is a Peano numeral built
/// by a non-tail recursion (`posOfInt`), which a six-digit sequence overflows the stack with.
let private adversarialSeqs: int list = [ 0; 1; 7; 10; 999; 1000; 1001; 2026 ]

/// The model's envelope over a STRING op with the identity codec — the shape the parse is stated
/// over, since `rec_payload` reads nothing of the op but its encoding.
let private modelEnvelope (show: Chain.pos -> string) (seq: int) (actor: string) (opJson: string) : string =
    Chain.rec_payload show id (posOfInt seq) actor opJson

/// Every PAYLOAD SPLICE of one chain: record `i` takes record `j`'s content while keeping its own
/// stored hash and prev-link — the whole content (which the sequence check finds first) and the
/// actor-and-op only (which only the recomputed hash can find). Exactly the tamper
/// `payload_splice_breaks_chain` is about, drawn from the chain's own payloads rather than minted.
let private payloadSplices (rs: OpRecord<'Op> list) : (string * OpRecord<'Op> list) list =
    let n = List.length rs

    [ for i in 0 .. n - 1 do
          for j in 0 .. n - 1 do
              if i <> j then
                  let r = List.item i rs
                  let s = List.item j rs

                  let put (r': OpRecord<'Op>) =
                      rs |> List.mapi (fun k x -> if k = i then r' else x)

                  yield
                      sprintf "content@%d<-%d" i j,
                      put
                          { r with
                              Seq = s.Seq
                              Actor = s.Actor
                              Op = s.Op }

                  if s.Actor <> r.Actor || s.Op <> r.Op then
                      yield sprintf "actor+op@%d<-%d" i j, put { r with Actor = s.Actor; Op = s.Op } ]

let private payloadSpliceDifferential
    (label: string)
    (prodHash: HashFn)
    (modelHash: HashFn)
    (w: StreamWitness<'Op, 'State, 'Rej>)
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
        let lanes, r' = gen.Lanes 2 rng
        rng <- r'
        let ops = List.concat lanes
        // Two writers, so an actor-and-op splice can change the actor as well as the op.
        let rs =
            ops
            |> List.mapi (fun k op ->
                (if k % 2 = 0 then
                     Human "writer"
                 else
                     Agent("m", "v", "writer-2")),
                op)
            |> List.fold
                (fun (st, acc) (actor, op) ->
                    match OpStream.append (OpStream.canonical prodHash w) actor op st acc with
                    | Ok(st', acc') -> st', acc'
                    | Error _ -> st, acc)
                (gen.State0, OpStream.empty)
            |> snd

        let compare (what: string) (records: OpRecord<'Op> list) : ChainVerdict =
            let p, m = chainVerdicts prodHash modelHash w records

            if p <> m then
                note (
                    sprintf
                        "%s: seed=%d splice=%s ops=[%s]\n  production: %s\n  model:      %s"
                        label
                        seed
                        what
                        (ops |> List.map w.Encode |> String.concat "; ")
                        (renderChainVerdict p)
                        (renderChainVerdict m)
                )

            p

        if List.length rs > 1 then
            if compare "none" rs = ChainIntact then
                t <- { t with Intact = t.Intact + 1 }

            for (what, spliced) in payloadSplices rs do
                let p = compare what spliced
                t <- { t with Tampers = t.Tampers + 1 }

                if p <> ChainIntact then
                    t <- { t with Detected = t.Detected + 1 }
                else
                    note (sprintf "%s: seed=%d splice=%s went UNDETECTED by production" label seed what)

    t

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
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
                  (Some DagBreakReason.ContentIdMismatch)
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

              Expect.equal
                  (chainVerdictReason m)
                  (Some ChainBreakReason.HashMismatch)
                  "and it disagrees by naming the check that failed"

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

                  DagOf.nodes (
                      Map.add
                          k
                          { n with
                              Op = AddItem("z1", "TAMPERED") }
                          dag.Nodes
                  )

              let real = dagUnder OpStream.defaultHash planW planLaneGen.BaseOp [ lane ]
              let weak = dagUnder opBlindHash planW planLaneGen.BaseOp [ lane ]

              // The probe did the thing it claims to be about: two different hashes, two different
              // sets of ids. Without this the case could be comparing one DAG with itself.
              Expect.notEqual (Dag.heads real) (Dag.heads weak) "the two hashes mint different ids"

              let rp, rm =
                  dagVerdicts OpStream.defaultHash OpStream.defaultHash planW (tamper real)

              Expect.equal
                  (dagVerdictReason rp)
                  (Some DagBreakReason.ContentIdMismatch)
                  "under an injective hash the tamper is found"

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
                                  | [] ->
                                      Dag.append (OpStream.canonical OpStream.defaultHash rawW) s.Actor s.OpJson "" dag
                                      |> Reference.built
                                  | [ Some p ] ->
                                      Dag.append (OpStream.canonical OpStream.defaultHash rawW) s.Actor s.OpJson p dag
                                      |> Reference.built
                                  | [ Some l; Some r ] ->
                                      merges <- merges + 1

                                      Dag.merge (OpStream.canonical OpStream.defaultHash rawW) s.Actor s.OpJson l r dag
                                      |> Reference.built
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
                  // model's own pre-image measured against the id production minted. The merge is
                  // made INTO the rebuilt DAG, which holds both parents: since Phase 296 a merge whose
                  // parent names no node is refused, and this used to merge into an empty one.
                  match built with
                  | (_, _, a) :: (_, _, b) :: _ ->
                      let lr, _ =
                          Dag.merge (OpStream.canonical OpStream.defaultHash rawW) (Human "m") "{}" a b dag
                          |> Reference.built

                      let rl, _ =
                          Dag.merge (OpStream.canonical OpStream.defaultHash rawW) (Human "m") "{}" b a dag
                          |> Reference.built

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

              // Since Phase 300 `Dag.append` VALIDATES the parent string it is handed: a comma-bearing
              // parent id — whose pre-image splices into the merge of the two ids it names — is
              // refused with a typed fault, so the premise holds of everything production BUILDS.
              let ambiguous = List.head keys + "," + List.item 1 keys

              match
                  Dag.append
                      (OpStream.canonical OpStream.defaultHash planW)
                      (Human "w")
                      planLaneGen.BaseOp
                      ambiguous
                      dag
              with
              | Error(DagAppendFault.CommaInParentId p) -> Expect.equal p ambiguous "the refusal names the offending id"
              | other ->
                  failtestf
                      "a comma-bearing parent id must be refused, got %A"
                      (Result.map (fun (w: Dag.Written<PlanOp>) -> w.Id) other)

              // What such an append WOULD have minted is exactly the merge of the two ids — the
              // splice, measured: a node carrying the merge's content id and the one comma-bearing
              // parent passes the content check. What stops it sitting in a VERIFIED DAG that was not
              // built here (a hand-made or loaded one) is the walk's second clause: it names no node.
              let mergeId, _ =
                  Dag.merge
                      (OpStream.canonical OpStream.defaultHash planW)
                      (Human "w")
                      planLaneGen.BaseOp
                      (List.head keys)
                      (List.item 1 keys)
                      dag
                  |> Reference.built

              let spoiled =
                  dag.Nodes
                  |> Map.add
                      mergeId
                      { Id = mergeId
                        Parents = [ ambiguous ]
                        Actor = Human "w"
                        Op = planLaneGen.BaseOp }
                  |> DagOf.nodes

              match Dag.firstBreak (OpStream.canonical OpStream.defaultHash planW) spoiled with
              | Some b ->
                  // Typed since Phase 147 — `Reason` was the string this case compared when it was
                  // written, and comparing the case is what that phase made possible.
                  Expect.equal
                      b.Reason
                      DagBreakReason.MissingParent
                      "a comma-bearing parent id is caught as a missing parent"

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

                  DagOf.nodes (
                      Map.add
                          k
                          { n with
                              Op = AddItem("z1", "TAMPERED") }
                          weak.Nodes
                  )

              Expect.isTrue
                  (Dag.verifyDag (OpStream.canonical OpStream.defaultHash lossy) tampered)
                  "under a non-injective CODEC — the hash untouched — production's own walker cannot see the tamper"

          // ---- Phase 197 — the envelope parse: the linear side's premise, discharged ----

          testCase "production's chain payload IS the model's envelope, over the adversarial actors, ops and sequences"
          <| fun _ ->
              // `rec_payload` is a hand-spelled copy of `OpStream.canonicalConfig.Payload`, and the
              // parse is a theorem about the copy. This is the byte-for-byte check that the copy is
              // the production envelope, over the population every case below runs on.
              for seq in adversarialSeqs do
                  for a in adversarialActors do
                      for o in adversarialEnvelopeOps do
                          Expect.equal
                              (modelEnvelope showPos seq (Actor.encode a) o)
                              (StreamConfig.canonical.Payload seq a o)
                              (sprintf "the model's envelope is production's payload for seq=%d actor=%A op=%s" seq a o)

          testCase "the envelope parse recovers the sequence, the actor and the op over the adversarial population"
          <| fun _ ->
              // `Chain.rec_payload_parsed`, measured: over every (seq, actor, op-encoding) triple the
              // envelope determines all three — no two distinct triples share an envelope.
              let triples =
                  [ for seq in adversarialSeqs do
                        for a in adversarialActors do
                            for o in adversarialEnvelopeOps -> seq, Actor.encode a, o ]

              let envelopes =
                  triples
                  |> List.map (fun (seq, a, o) -> modelEnvelope showPos seq a o, (seq, a, o))

              let collisions =
                  envelopes
                  |> List.groupBy fst
                  |> List.filter (fun (_, g) -> (g |> List.map snd |> List.distinct |> List.length) > 1)
                  |> List.map fst

              Expect.isEmpty
                  collisions
                  "two distinct (seq, actor, op) triples share one envelope — the parse is ambiguous"

              Expect.equal
                  (envelopes |> List.map fst |> List.distinct |> List.length)
                  (List.length envelopes)
                  "and no two triples collide at all"

          testCase
              "under a FREE actor code the same envelope IS ambiguous — the prefix-free premise is what the parse spends"
          <| fun _ ->
              // The go-red for the actor split. The parse cuts the actor at the comma that opens
              // `,"op":` and needs the actor code prefix-free to do it; over a free actor string the
              // cut is not forced, and the ambiguity the model's section 6b names is REAL: the actor
              // `A,"op":B` with op `C` and the actor `A` with op `B,"op":C` mint one envelope.
              let ambiguous = modelEnvelope showPos 3 "A,\"op\":B" "C"
              Expect.equal ambiguous (modelEnvelope showPos 3 "A" "B,\"op\":C") "one envelope, two (actor, op) readings"

              // What saves production: both actors are `Actor.encode` outputs there, and the code is
              // prefix-free (Phase 145's measurement), so neither reading's actor is an encoding.
              let isEncoding (s: string) =
                  adversarialActors |> List.exists (fun a -> Actor.encode a = s)

              Expect.isFalse (isEncoding "A,\"op\":B") "the free actor is not an actor encoding"
              Expect.isFalse (isEncoding "A") "nor is the other reading's"

              // And the SAME ambiguity cannot be built from encodings: no actor encoding is a proper
              // prefix of another, so no encoding extended by `,"op":<x>` is an encoding.
              let encs = adversarialActors |> List.map Actor.encode

              Expect.isEmpty
                  [ for x in encs do
                        for y in encs do
                            if y.StartsWith(x + ",\"op\":", System.StringComparison.Ordinal) then
                                yield x, y ]
                  "no actor encoding is another encoding followed by the op separator"

          testCase
              "the sequence numeral is comma-free and injective, and a numeral WITH a comma refutes the first-comma split rather than the envelope"
          <| fun _ ->
              // `seq_numeral_code`, measured on production's `show` — `string : int -> string`.
              let rendered =
                  adversarialSeqs @ [ for i in 0..2000 -> i ]
                  |> List.distinct
                  |> List.map (fun i -> i, string i)

              for (i, s) in rendered do
                  Expect.isFalse (s.Contains ",") (sprintf "the numeral for %d carries no comma: %s" i s)

              Expect.equal
                  (rendered |> List.map snd |> List.distinct |> List.length)
                  (List.length rendered)
                  "and `string` is injective on the sequence numbers"

              // Where the premise is SPENT, and the finding beside it. The proof cuts the sequence at
              // the FIRST comma (`app_sep_split`), which a thousands-separated renderer defeats: for
              // seq 1000 it prints `1,000`, and the first-comma cut reads the sequence as `1`. That
              // is a refutation of the PROOF STEP, not of the envelope — the bytes after that comma
              // are `000,"actor":`, which no envelope's skeleton begins with, so the envelope for
              // seq 1000 is still not the envelope for seq 1. Comma-freedom is therefore SUFFICIENT
              // and what production has, not NECESSARY: the weakest premise the digraph `,"actor":`
              // would admit is that a numeral never spells the separator, and it was not taken
              // because the one-symbol premise is true of `string` and is a one-line split. Recorded
              // so the next reader does not mistake the numeral's comma for an ambiguity.
              let grouped (p: Chain.pos) : string =
                  (intOfPos p).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

              Expect.equal (grouped (posOfInt 1000)) "1,000" "the perturbed renderer carries a comma"

              let env = modelEnvelope grouped 1000 "X" "{}"
              let afterSkeleton = env.Substring("{\"seq\":".Length)

              Expect.equal
                  (afterSkeleton.Substring(0, afterSkeleton.IndexOf ','))
                  "1"
                  "the first-comma cut misreads the grouped numeral — `app_sep_split`'s premise is load-bearing"

              Expect.notEqual
                  env
                  (modelEnvelope grouped 1 "X" "{}")
                  "yet the envelopes for 1000 and 1 still differ — the skeleton after the comma is not `000`"

              Expect.isFalse
                  (afterSkeleton.StartsWith("1,\"actor\":", System.StringComparison.Ordinal))
                  "so no second reading of the envelope exists; the comma refutes the split, not the parse"

          testCase "a payload splice breaks the chain — production and the oracle agree, over the work-plan stream"
          <| fun _ ->
              // `payload_splice_breaks_chain`, beside production: one record's content swapped for
              // another record's, its stored hash and prev-link kept, in a chain `verifyChain`
              // accepted — and both walkers refuse it, reporting the same break.
              payloadSpliceDifferential
                  "work-plan payload splices"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  planW
                  planLaneGen
                  3970
                  40
              |> expectWalkerAgreement "work-plan payload splices"

          testCase
              "a payload splice breaks the chain — production and the oracle agree, over the reference witness's stream"
          <| fun _ ->
              payloadSpliceDifferential
                  "reference payload splices"
                  OpStream.defaultHash
                  OpStream.defaultHash
                  treeW
                  treeLaneGen
                  3971
                  30
              |> expectWalkerAgreement "reference payload splices"

          testCase
              "a payload splice is INVISIBLE under a hash that folds the payload — the parse cannot outrun the hash premise"
          <| fun _ ->
              // The differential's own go-red, and the honest boundary of the discharge: the parse
              // makes the ENVELOPE injective, and the record hash is `h prev envelope`, so a hash that
              // is not injective on its second argument hides a splice from production and the model
              // alike. `hash_injective` stays the one premise the linear side assumes, exactly as the
              // DAG's does.
              let payloadBlind: HashFn = fun prev _ -> OpStream.defaultHash prev "payload"
              let lanes, _ = planLaneGen.Lanes 2 (ConfRng.ofSeed 3972)

              let rs =
                  chainUnder payloadBlind planW planLaneGen.State0 (Human "writer") (List.concat lanes)

              Expect.isGreaterThan (List.length rs) 1 "at least two records appended, so a splice exists"

              let splices =
                  payloadSplices rs |> List.filter (fun (what, _) -> what.StartsWith "actor+op@")

              Expect.isNonEmpty splices "there is an actor-and-op splice to try"

              for (what, spliced) in splices do
                  let p, m = chainVerdicts payloadBlind payloadBlind planW spliced

                  Expect.equal
                      p
                      ChainIntact
                      (sprintf "%s: under a payload-blind hash production cannot see the splice" what)

                  Expect.equal m ChainIntact (sprintf "%s: nor can the model" what)

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

              match SnapshotMatrix.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs 1 with
              | Error e -> failtestf "compact refused an intact stream: %s" e
              | Ok(snap, tail) ->
                  Expect.isTrue
                      (SnapshotMatrix.verifyAcross OpStream.defaultHash planHash planW snap tail)
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

              match SnapshotMatrix.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs 1 with
              | Error e -> failtestf "the prefix replays, so compact must not refuse: %s" e
              | Ok(snap, tail) ->
                  let origin = prodReplayVerdict (OpStream.replay planW planLaneGen.State0 rs)
                  let bounded = prodReplayVerdict (SnapshotMatrix.replayFrom planW snap tail)
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

              Expect.isFalse
                  (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash planW) rs)
                  "the original does not verify"

              match SnapshotMatrix.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs 2 with
              | Error e -> failtestf "the tampered prefix still replays, so compact does not refuse: %s" e
              | Ok(snap, tail) ->
                  Expect.isTrue
                      (SnapshotMatrix.verifyAcross OpStream.defaultHash planHash planW snap tail)
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
                      (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash planW) (List.truncate 2 rs))
                      "the prefix is where the break is, which is the other conjunct of the split"

          testCase
              "under a NON-EMPTY genesis a compaction at sequence zero verifies across — compactWith carries the configured genesis"
          <| fun _ ->
              // Phase 227 closed Phase 191's finding. `compact_at_zero_verifies_under_any_genesis`,
              // measured on production: a stream appended under a config whose genesis is not `""`,
              // compacted through `compactWith` / `compactChainOnlyWith` with that config, verifies
              // across at EVERY boundary, zero included — and the model, handed the same genesis,
              // agrees with production value for value. Until 227 the snapshot at zero said `""` and
              // the first tail record linked to the genesis, so zero did not verify.
              let cfg =
                  { StreamConfig.canonical with
                      Genesis = "g0" }

              let mutable st = planLaneGen.State0
              let mutable rs: OpRecord<PlanOp> list = OpStream.empty

              for op in [ AddItem("g1", "one"); AddItem("g2", "two") ] do
                  match
                      OpStream.append (OpStream.ofConfig cfg OpStream.defaultHash planW) (Human "writer") op st rs
                  with
                  | Ok(st', rs') ->
                      st <- st'
                      rs <- rs'
                  | Error e -> failtestf "append refused: %s" e

              Expect.hasLength rs 2 "both appends were accepted"

              Expect.isTrue
                  (OpStream.verifyChain (OpStream.ofConfig cfg OpStream.defaultHash planW) rs)
                  "the stream verifies under its own config"

              let prodWith (mode: SnapshotMode) (n: int) =
                  match mode with
                  | StateHashed ->
                      SnapshotMatrix.compactWith cfg OpStream.defaultHash planHash planW planLaneGen.State0 rs n
                  | ChainOnly ->
                      SnapshotMatrix.compactChainOnlyWith cfg OpStream.defaultHash planW planLaneGen.State0 rs n

              for mode in [ StateHashed; ChainOnly ] do
                  for n in 0 .. List.length rs do
                      let p = prodWith mode n

                      let m =
                          Chain.compact
                              OpStream.defaultHash
                              showPos
                              (modelPay mode planHash)
                              (chainApply planW)
                              cfg.Genesis
                              planLaneGen.State0
                              (toChainRecords rs)
                              (posOfInt n)

                      Expect.equal
                          (prodCompactVerdict p)
                          (modelCompactVerdict m)
                          (sprintf "%A at %d: the model under the same genesis is production" mode n)

                      match p with
                      | Error e -> failtestf "%A at %d: compact refused an intact stream: %s" mode n e
                      | Ok(snap, tail) ->
                          if n = 0 then
                              Expect.equal
                                  snap.PrevHash
                                  cfg.Genesis
                                  "at zero the boundary hash is the configured genesis"

                          // `cfg` differs from the canonical config in its genesis only, and the
                          // boundary walker reads the genesis nowhere (it walks from `snap.PrevHash`),
                          // so the canonical-payload verifiers are the config's own here.
                          let pAcross = prodVerifyAcross mode OpStream.defaultHash planHash planW snap tail

                          let mAcross =
                              Chain.verify_across
                                  OpStream.defaultHash
                                  showPos
                                  planW.Encode
                                  (modelPay mode planHash)
                                  (toModelSnapshot snap)
                                  (toChainRecords tail)

                          Expect.equal
                              (pAcross, mAcross)
                              (true, true)
                              (sprintf "%A at %d: the compaction of an intact stream verifies across" mode n)

                          // `compact_preserves_verify`, held of production under the config, with no
                          // condition on the genesis left in it.
                          Expect.equal
                              (OpStream.verifyChain (OpStream.ofConfig cfg OpStream.defaultHash planW) rs)
                              (OpStream.verifyChain
                                  (OpStream.ofConfig cfg OpStream.defaultHash planW)
                                  (List.truncate n rs)
                               && pAcross)
                              (sprintf "%A at %d: the split holds under the configured genesis" mode n)

              // The canonical entry point is the `""` instantiation and says so: handed this stream it
              // still snapshots `""` at zero, which is why a host on another genesis compacts through
              // `compactWith`. Past zero the two agree — the boundary hash is a stored one.
              match SnapshotMatrix.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs 0 with
              | Error e -> failtestf "compact refused an intact stream: %s" e
              | Ok(snap, tail) ->
                  Expect.equal snap.PrevHash "" "the canonical compact seeds the canonical genesis"

                  Expect.isFalse
                      (SnapshotMatrix.verifyAcrossWith cfg OpStream.defaultHash planHash planW snap tail)
                      "so it is not the compaction of a g0 stream at zero"

              Expect.equal
                  (SnapshotMatrix.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs 1)
                  (prodWith StateHashed 1)
                  "past zero the canonical compact and compactWith cfg are the same value"

          testCase
              "the shipped configurations snapshot byte-identically — compact is compactWith canonicalConfig, pinned by a vector"
          <| fun _ ->
              // Phase 227's additive half: threading the genesis moved no byte any released entry point
              // emits. Both shipped configs carry the empty genesis, so `compact` / `compactChainOnly`
              // / `snapshotAtOpt` are `...With` at either one, value for value, at every boundary — and
              // the snapshots at zero of a fixed stream are pinned by digest, so a later move of either
              // entry point's bytes goes red here rather than in a consumer's cache.
              let rs =
                  chainUnder
                      OpStream.defaultHash
                      planW
                      planLaneGen.State0
                      (Human "writer")
                      [ AddItem("v1", "one"); AddItem("v2", "two") ]

              Expect.hasLength rs 2 "both appends were accepted"

              for cfg in [ StreamConfig.canonical; StreamConfig.legacyActor ] do
                  Expect.equal cfg.Genesis "" "a shipped config carries the empty genesis"

                  for n in 0 .. List.length rs + 1 do
                      Expect.equal
                          (SnapshotMatrix.compactWith cfg OpStream.defaultHash planHash planW planLaneGen.State0 rs n)
                          (SnapshotMatrix.compact OpStream.defaultHash planHash planW planLaneGen.State0 rs n)
                          (sprintf "compact at %d" n)

                      Expect.equal
                          (SnapshotMatrix.compactChainOnlyWith cfg OpStream.defaultHash planW planLaneGen.State0 rs n)
                          (SnapshotMatrix.compactChainOnly OpStream.defaultHash planW planLaneGen.State0 rs n)
                          (sprintf "compactChainOnly at %d" n)

                      Expect.equal
                          (SnapshotMatrix.snapshotAtOptWith
                              cfg
                              OpStream.defaultHash
                              (Some planHash)
                              planW
                              planLaneGen.State0
                              rs
                              n)
                          (SnapshotMatrix.snapshotAtOpt
                              OpStream.defaultHash
                              (Some planHash)
                              planW
                              planLaneGen.State0
                              rs
                              n)
                          (sprintf "snapshotAtOpt at %d" n)

              match
                  SnapshotMatrix.snapshotAt OpStream.defaultHash planHash planW planLaneGen.State0 rs 0,
                  SnapshotMatrix.snapshotAtChainOnly OpStream.defaultHash planW planLaneGen.State0 rs 0
              with
              | Ok strict, Ok chainOnly ->
                  Expect.equal strict.PrevHash "" "the canonical boundary hash at zero"
                  Expect.equal strict.Hash "299b31aa" "the strict snapshot at zero, pinned"
                  Expect.equal chainOnly.Hash "92f95a5f" "the chain-only snapshot at zero, pinned"
              | a, b -> failtestf "snapshot at zero refused: %A / %A" a b

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

              match Attestation.attestHead promiscuousSink StreamConfig.canonical signed with
              | None -> failtest "the promiscuous sink signs"
              | Some att ->
                  Expect.isTrue
                      (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash planW) forged
                       && Attestation.verifyAttestation promiscuousSink StreamConfig.canonical att forged)
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

                  match Attestation.attestHead sink StreamConfig.canonical signed with
                  | None -> failtest "the keyring sink signs"
                  | Some bound ->
                      Expect.isFalse
                          (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash planW) forged
                           && Attestation.verifyAttestation sink StreamConfig.canonical bound forged)
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

              Expect.equal
                  (OpStream.head StreamConfig.canonical empty)
                  ""
                  "production's head of the empty chain is the literal empty string"

              Expect.equal (Chain.chain_head (toChainRecords empty)) "" "and so is the model's"

              let one =
                  remint OpStream.defaultHash planW.Encode [ Human "writer", AddItem("s1", "one") ]

              match
                  Attestation.attestHead sink StreamConfig.canonical empty,
                  Attestation.attestHead sink StreamConfig.canonical one
              with
              | Some overNothing, Some overOne ->
                  let accepts (att: Attestation) (rs: OpRecord<PlanOp> list) =
                      (OpStream.verifyChain (OpStream.canonical OpStream.defaultHash planW) rs
                       && Attestation.verifyAttestation sink StreamConfig.canonical att rs),
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
              | _ -> failtest "the keyring sink signs" ]
