namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Conformance — the FOLD-CONFLUENCE pack (Phase 100).
//
//  Phase 80 certified confluence for ONE domain's tree ops: interleavings of two
//  independence-declared op-scripts replay to the same tree. Phase 83 certified that a
//  two-head `Dag.reconcile` folds order-independently. Both are pinned to the skeleton-op
//  tree algebra, and both stop at two branches.
//
//  A local-first deployment does not converge two tree scripts; it converges **N lanes**
//  — one op-stream per writer/session, off one shared base, arriving in whatever order the
//  network delivered them. The claim it rests on is that the arrival order is invisible:
//  the same lanes fold to the same state however they arrive, and a lane set that CANNOT
//  fold refuses in the same way however it arrives. This pack is that claim, made
//  runnable against a domain's OWN `'Op` / `'State` — hand it your `StreamWitness`, your
//  footprint projection and a lane generator, and it certifies, or refutes with a shrunk
//  counterexample.
//
//  Until Phase 300 the second half of that claim was FALSE for one kind of lane set, and this
//  header said otherwise: a lane that rejects from the base state (`[[Dec 5]; [Inc 10]]` off 0)
//  folded under the arrival order that happened to run the other lane first and rejected under
//  the other, so the pack reported a divergence and blamed a domain that satisfies the diamond.
//  `Dag.reconcileMany` now replays every lane ON ITS OWN before anything is composed and refuses
//  a set with a rejecting lane with the set of rejecting lanes — a property of the set, so the
//  refusal is the same however the lanes arrive (`DagFold.fold_confluence_total`, proved with no
//  hypothesis about the lane set). What is still a divergence, and still this pack's to find, is
//  a domain whose declared-independent ops do not commute.
//
//  Three outcomes, not two. The pack distinguishes **folding identically** from **halting
//  identically**, and treats a lane set that folds under one arrival order and halts under
//  another as its own, separately-named defect — that is the bug class the pack exists to
//  catch. A conflict visible only from one direction is worse than a conflict, because the
//  deployment that happened to receive the lanes the other way round proceeds, and the two
//  replicas silently disagree about whether they diverged at all.
//
//  FSharp.Core only (the `ConfRng` xorshift32 generator, no FsCheck), Fable-clean — as the rest
//  of the kit.
//
//  ---- Out of scope, deliberately -------------------------------------------
//   - **Resolution.** The pack certifies that a halt is order-invariant; it never says a
//     halt was correct, and never picks a winner. Reconciliation stays domain-side (GP6).
//   - **Necessity.** As with Phase 80: footprint independence is SUFFICIENT for a clean
//     fold, never necessary. A lane set the footprints declare interfering is required to
//     halt *consistently*, not required to be genuinely unmergeable.
//   - **Exhaustive orders.** N lanes admit N! arrival orders. The pack enumerates them all
//     up to 4 lanes and samples deterministically above that (see `permutationBound`), so a
//     green verdict means "certified over the sampled orders", never "proved for all N!".
// ============================================================================

/// The outcome of folding ONE lane set in ONE arrival order, canonicalised so that two
/// orders' outcomes are comparable by structural equality — which is the whole measurement.
type LaneFoldOutcome =
    /// Every lane delta composed and replayed; the domain-supplied hash of the folded state.
    | LaneFolded of stateHash: string
    /// The lanes interfere. The report is rendered ARRIVAL-ORDER-INDEPENDENTLY (each conflict
    /// as shape + address + its unordered op pair, deduplicated and sorted), because
    /// `Dag.conflicts` reports the same interference with `Left`/`Right` swapped when the two
    /// deltas are handed to it the other way round. Comparing raw reports would therefore fail
    /// every trial for a reason that is presentation, not divergence.
    | LaneHalted of report: string
    /// The lane set does not apply. Since Phase 300 this is, first, the canonical rendering of
    /// every lane that does not apply ON ITS OWN from the base (`canonicalRejectionReport`) — a
    /// property of the set, tested before anything is composed; and otherwise a rejection while
    /// replaying the composed script, which a domain satisfying the diamond never produces. A
    /// rejection that is not identical under every arrival order is a divergence like any other.
    | LaneRejected of reason: string

/// The domain-supplied lane generator: the base state every lane forks from, the shared genesis
/// op that gives them a common ancestor node, and a source of N lanes of ops.
type LaneGen<'Op, 'State> =
    {
        /// The state the folded script is replayed from.
        State0: 'State
        /// The genesis op of each trial's DAG. It sits in the base closure, so it appears in no
        /// lane delta and is never applied — it only has to be encodable. A domain typically
        /// passes any op at all; its content merely seeds the base node's content id.
        BaseOp: 'Op
        /// Generate `n` lanes of ops off the base. A lane may legitimately be empty. Deterministic
        /// in the rng, so a whole trial is reproducible from its seed.
        Lanes: int -> ConfRng.T -> 'Op list list * ConfRng.T
    }

/// The lane trial `FoldConfluence.laneDag` builds (Phase 391; a positional triple before `1.0.0`):
/// the DAG, its base node, and the lane heads — exactly the three things `Dag.reconcileMany`
/// takes beside the domain's witness and state.
type LaneDag<'Op> =
    {
        /// The base node and every lane chained onto it.
        Dag: Dag.T<'Op>
        /// The base node's id.
        BaseId: string
        /// Each lane's head, in lane order; an empty lane's head is `BaseId`.
        Heads: string list
    }

/// The fold-confluence law family (Phase 100). A domain runs `laneFoldLaws` against its own
/// witness; `foldOnce` and `shrinkLanes` are exposed because a domain investigating a
/// counterexample wants to drive them directly, and `laneDag` (Phase 249) because a domain that
/// wants a halt's typed conflicts reads them off the DAG `foldOnce` folds.
[<RequireQualifiedAccess>]
module FoldConfluence =

    /// Arrival orders are enumerated exhaustively at or below this many lanes (4! = 24) and
    /// sampled to this many above it. The one number the sampling bound is stated in.
    let permutationBound = 24

    /// All permutations of a list of DISTINCT ints — used only on `[0 .. n-1]`, and only under
    /// `permutationBound`. Typed to `int` rather than left generic: the element-removal step
    /// needs equality, and a generic constraint here would leak into every caller's signature.
    let rec private permutations (xs: int list) : int list list =
        match xs with
        | [] -> [ [] ]
        | _ ->
            xs
            |> List.collect (fun x -> permutations (xs |> List.filter (fun y -> y <> x)) |> List.map (fun p -> x :: p))

    /// The arrival orders sampled for `n` lanes: every permutation when `n! <= permutationBound`,
    /// otherwise a deterministic sample — the identity and the reverse (the two extremes a
    /// hand-written test would pick) plus shuffles from a FIXED seed. Fixed rather than threaded
    /// so that the order set is a pure function of `n`, which is what makes shrinking reproducible:
    /// a shrunk lane set must be re-measured against the same orders, not against fresh ones.
    let arrivalOrders (n: int) : int list list =
        let idx = [ 0 .. n - 1 ]

        if n <= 4 then
            permutations idx
        else
            let rec draw acc k r =
                if k <= 0 then
                    acc
                else
                    let s, r' = ConfRng.shuffle idx r
                    draw (s :: acc) (k - 1) r'

            draw [ idx; List.rev idx ] (permutationBound - 2) (ConfRng.ofSeed 104729)
            |> List.distinct

    let private permuteBy (order: int list) (xs: 'a list) : 'a list =
        order |> List.map (fun i -> List.item i xs)

    let private shapeTag (s: MergeConflictShape) : string =
        match s with
        | MergeConflictShape.ConcurrentUpdate -> "concurrent-update"
        | MergeConflictShape.InsertPositionClash -> "insert-position-clash"
        | MergeConflictShape.MoveVsRemove -> "move-vs-remove"
        // Phase 340 — the slot is part of the shape's identity: two clashes at one node on different
        // slots are two lines, not one.
        | MergeConflictShape.SlotClash slot -> "slot-clash:" + slot

    /// The canonical, arrival-order-independent rendering of a conflict report: one line per
    /// distinct (shape, address, unordered op pair), sorted ordinally. `Dag.conflicts` is
    /// symmetric only up to a `Left`/`Right` swap (`Conformance.mergeConflictLaws` pins exactly
    /// that), so the unordered pair is the honest identity of an interference — and normalising
    /// it here is what lets "halts identically" be a real claim rather than a claim about
    /// presentation. Public: a domain that reports a halt to an operator wants this rendering.
    let canonicalConflictReport (encodeOp: 'Op -> string) (cs: MergeConflict<'Op> list) : string =
        cs
        |> List.map (fun c ->
            let l = encodeOp c.Left
            let r = encodeOp c.Right

            let lo, hi =
                if System.String.CompareOrdinal(l, r) <= 0 then
                    l, r
                else
                    r, l

            shapeTag c.Shape + "|" + c.Address + "|" + lo + "|" + hi)
        |> List.distinct
        |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
        |> String.concat "\n"

    /// The canonical, arrival-order-independent rendering of a lane-set refusal (Phase 300): one
    /// line per distinct rejecting lane — its delta in the domain's own encoding and the rejection
    /// it met replaying on its own — sorted ordinally. A lane's own replay is a property of the
    /// lane, so this rendering is a property of the lane SET, which is what lets "refuses
    /// identically" be a real claim. Public for the same reason `canonicalConflictReport` is.
    let canonicalRejectionReport (encodeOp: 'Op -> string) (rs: LaneRejection<'Op, 'Rej> list) : string =
        rs
        |> List.map (fun r ->
            "["
            + (r.Delta |> List.map encodeOp |> String.concat "; ")
            + "] rejected: "
            + sprintf "%A" r.Reject)
        |> List.distinct
        |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
        |> String.concat "\n"

    /// Fold the lane set whose heads are `heads`, in the order named, over a DAG already built —
    /// `Dag.reconcileMany` from `state0`, the composed script replayed through the domain reducer,
    /// and the outcome rendered canonically. `foldOnce` is this over the chains it builds; the
    /// shaped draws of `laneFoldLawsWith` (a duplicated head, a fast-forward) are this over theirs.
    let internal foldHeads
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (footprintOf: 'Op -> Footprint)
        (hashState: 'State -> string)
        (state0: 'State)
        (dag: Dag.T<'Op>)
        (baseId: string)
        (heads: string list)
        : LaneFoldOutcome =
        match Dag.reconcileMany w footprintOf dag baseId state0 heads with
        | Error(ReconcileFault.LanesInterfere cs) -> LaneHalted(canonicalConflictReport w.Encode cs)
        | Error(ReconcileFault.LanesRejected rs) -> LaneRejected(canonicalRejectionReport w.Encode rs)
        | Error(ReconcileFault.SharedHistoryRejected(nodeId, rej)) ->
            LaneRejected(
                "shared history rejected at "
                + w.Encode dag.Nodes[nodeId].Op
                + ": "
                + sprintf "%A" rej
            )
        | Ok script ->
            match script |> List.fold (fun acc op -> acc |> Result.bind (w.Apply op)) (Ok state0) with
            | Ok st -> LaneFolded(hashState st)
            | Error rej -> LaneRejected(sprintf "%A" rej)

    /// The base node every trial forks from: `baseOp` appended to the empty DAG under the
    /// `Human "base"` actor. One definition, so `laneDag` and the shaped draws of
    /// `laneFoldLawsWith` cannot build different bases.
    let private baseNode (hashFn: HashFn) (w: StreamWitness<'Op, 'State, 'Rej>) (baseOp: 'Op) : string * Dag.T<'Op> =
        Dag.append hashFn w (Human "base") baseOp "" Dag.empty |> LawKit.dagBuilt

    /// Chain each lane onto `parentOf i` under its own actor, returning the heads in lane order.
    let private chainLanes
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (hashFn: HashFn)
        (parentOf: int -> string list -> string)
        (d0: Dag.T<'Op>)
        (lanes: 'Op list list)
        : string list * Dag.T<'Op> =
        lanes
        |> List.indexed
        |> List.fold
            (fun (hs, d) (i, ops) ->
                let actor = Human("lane-" + string i)

                let head, d' =
                    ops
                    |> List.fold
                        (fun (h, dd) op -> Dag.append hashFn w actor op h dd |> LawKit.dagBuilt)
                        (parentOf i hs, d)

                hs @ [ head ], d')
            ([], d0)

    /// The lane DAG of one trial (Phase 249): `baseOp` appended to the empty DAG under the
    /// `Human "base"` actor, then each lane chained onto that base node **under its own actor**
    /// (`Human "lane-<i>"`, `i` the lane's index). Returns the DAG, the base node's id and the
    /// lane heads in lane order — exactly what `Dag.reconcileMany w footprintOf dag baseId state0
    /// heads` takes, so a domain that wants the TYPED `MergeConflict` list of a halt (to report it in
    /// its own words, or to resolve it) reads it off the very DAG `foldOnce` folds, rather than
    /// rebuilding one beside it that can drift.
    ///
    /// The per-lane actor is load-bearing, not decoration: node ids are content hashes of
    /// (parents, actor, op), so two lanes carrying the SAME op sequence off the same base would
    /// otherwise converge to one chain — correct content-addressing, but it would silently
    /// collapse the trial to a single lane and certify nothing. An empty lane's head is the base
    /// node itself.
    let laneDag
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (baseOp: 'Op)
        (lanes: 'Op list list)
        : LaneDag<'Op> =
        let baseId, d0 = baseNode hashFn w baseOp
        let heads, dag = chainLanes w hashFn (fun _ _ -> baseId) d0 lanes

        { Dag = dag
          BaseId = baseId
          Heads = heads }

    /// Fold ONE lane set in the order given, through the real DAG surface rather than a
    /// re-implementation of it: the trial is `laneDag` (each lane chained onto a shared base node
    /// under its own actor), the lane deltas are recovered by `Dag.reconcileMany`'s partition,
    /// which checks every unordered lane pair and (Phase 300) that every lane applies on its own
    /// from `state0`, and the composed script is replayed through the domain reducer from `state0`.
    ///
    /// Defined over `laneDag` (Phase 249), so the DAG a domain builds to read a halt's typed
    /// conflicts and the DAG this folds cannot diverge.
    let foldOnce
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (footprintOf: 'Op -> Footprint)
        (hashFn: HashFn)
        (hashState: 'State -> string)
        (state0: 'State)
        (baseOp: 'Op)
        (lanes: 'Op list list)
        : LaneFoldOutcome =
        let trial = laneDag hashFn w baseOp lanes
        foldHeads w footprintOf hashState state0 trial.Dag trial.BaseId trial.Heads

    /// Greedy delta-debugging over a failing lane set: repeatedly take the first single-element
    /// removal — a whole lane, or one op from one lane — that still `diverges`, to a fixpoint or
    /// the step bound. Every accepted step strictly shrinks the input, so it terminates.
    ///
    /// This is the difference between a usable refutation and a raw dump. The generated trial that
    /// first diverges is typically several lanes of several ops; the defect is almost always two
    /// ops. Shrinking is bounded and greedy rather than optimal on purpose — a locally-minimal
    /// counterexample found in milliseconds beats a globally-minimal one nobody waits for.
    let shrinkLanes (diverges: 'Op list list -> bool) (lanes: 'Op list list) : 'Op list list =
        let dropLane i (ls: 'Op list list) =
            ls |> List.indexed |> List.filter (fun (j, _) -> j <> i) |> List.map snd

        let dropOp li oi (ls: 'Op list list) =
            ls
            |> List.mapi (fun j l ->
                if j = li then
                    l |> List.indexed |> List.filter (fun (k, _) -> k <> oi) |> List.map snd
                else
                    l)

        let candidates (ls: 'Op list list) =
            [ for i in 0 .. List.length ls - 1 do
                  dropLane i ls
              for li in 0 .. List.length ls - 1 do
                  for oi in 0 .. List.length (List.item li ls) - 1 do
                      dropOp li oi ls ]

        let rec go steps ls =
            if steps <= 0 then
                ls
            else
                match candidates ls |> List.tryFind diverges with
                | Some smaller -> go (steps - 1) smaller
                | None -> ls

        go 64 lanes

    /// Render a lane set for a counterexample, one line per lane, through the witness's own
    /// encoder — so the reproducer is in the domain's vocabulary, not `%A` of its internals.
    let internal renderLanes (encodeOp: 'Op -> string) (lanes: 'Op list list) : string =
        lanes
        |> List.mapi (fun i ops ->
            "  lane "
            + string i
            + ": ["
            + (ops |> List.map encodeOp |> String.concat "; ")
            + "]")
        |> String.concat "\n"

    let private kindTag (o: LaneFoldOutcome) : string =
        match o with
        | LaneFolded _ -> "folded"
        | LaneHalted _ -> "halted"
        | LaneRejected _ -> "rejected"

    let private renderOutcome (o: LaneFoldOutcome) : string =
        match o with
        | LaneFolded h -> "  folded → " + h
        | LaneHalted r -> "  halted → " + r.Replace("\n", " / ")
        | LaneRejected r -> "  rejected → " + r

    /// The fold-confluence laws (Phase 100) with an explicit `HashFn` — the host-swap seam the
    /// rest of the kit carries (FNV-1a by default, a host's SHA at its boundary). Domains call
    /// `laneFoldLaws`, which pins `OpStream.defaultHash`.
    ///
    /// Over a seed-replayable sample it generates `laneCount` lanes, folds them under every
    /// sampled arrival order (`arrivalOrders`), and certifies:
    ///
    ///  - **lane-fold determinism** — a lane set that folds folds to ONE state hash, whichever
    ///    order it arrived in (a rejection during replay counts here too: a reducer that rejects
    ///    under one order and not another has not folded deterministically);
    ///  - **lane-halt determinism** — a lane set that halts halts with the SAME canonical conflict
    ///    report under every order;
    ///  - **outcome classification invariance** — no lane set folds under one order and halts
    ///    under another. This is the headline: the other two laws compare values within a class,
    ///    this one denies that the class itself can move;
    ///  - **fold coverage** / **conflict coverage** — vacuity guards. A run whose generator never
    ///    produced a folding set has not tested law 1, and a run that never produced a HALTING set
    ///    has not tested law 2 at all — which is precisely how a pack claiming to cover conflict
    ///    semantics ends up covering none. Both are reported as failures rather than silently
    ///    passing: a domain that sees the conflict-coverage law red should widen its lane
    ///    generator until lanes collide, not conclude its ops cannot conflict. Since Phase 245 the
    ///    guard also counts the lane sets the reducer REJECTED, beside the two it demands, so a
    ///    sample made of rejections is named as one rather than read as a thin but honest draw.
    ///
    /// Every divergence is `shrinkLanes`-reduced before it is reported, and the counterexample
    /// carries the seed, the iteration, the shrunk lanes in the domain's own encoding, and each
    /// distinct outcome — a reproducer, not a symptom.
    ///
    /// The pinned `hashFn` sits last before `seed`, under the kit's `…With` rule (Phase 330; it sat
    /// third until then, and no forward keeps that order).
    let laneFoldLawsWith
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (footprintOf: 'Op -> Footprint)
        (hashState: 'State -> string)
        (gen: LaneGen<'Op, 'State>)
        (laneCount: int)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let foldLaw =
            LawKit.LawCell(
                "lane-fold determinism (every arrival order of a folding lane set folds to one state hash)",
                Some "lane-fold outcome"
            )

        let haltLaw =
            LawKit.LawCell(
                "lane-halt determinism (a halting lane set halts with the same canonical report under every arrival order)",
                Some "lane-fold outcome"
            )

        let classLaw =
            LawKit.LawCell
                "outcome classification is arrival-order-invariant (no lane set folds under one order and halts under another)"

        let mutable folded = 0
        let mutable halted = 0
        let mutable rejected = 0
        let mutable duplicateHeads = 0
        let mutable fastForwards = 0

        // Phase 300 — the SHAPES a lane set arrives in. `Disjoint` is the pack's original draw: every
        // lane chained off the base, the lanes themselves permuted. The two shared-history shapes
        // are built ONCE and their HEADS permuted, because what arrives in a different order there is
        // the head list, not the history: `DuplicateHead` names the first lane's head twice, and
        // `FastForward` chains the second lane onto the first lane's head, so one head descends from
        // the other. Both used to replay the shared history twice; both must fold, halt or refuse
        // identically under every order. The criss-cross needs a merge op, which a lane generator does
        // not supply, so it is drawn by `reconcileLaws`, over the tree algebra's no-op batch.
        let outcomesOf (shape: int) (ls: 'Op list list) =
            let baseId, d0 = baseNode hashFn w gen.BaseOp

            let overHeads (heads: string list) (dag: Dag.T<'Op>) =
                arrivalOrders (List.length heads)
                |> List.map (fun p -> foldHeads w footprintOf hashState gen.State0 dag baseId (permuteBy p heads))
                |> List.distinct

            match shape, ls with
            | 1, _ :: _ ->
                let heads, dag = chainLanes w hashFn (fun _ _ -> baseId) d0 ls
                overHeads (heads @ [ List.head heads ]) dag
            | 2, _ :: _ :: _ ->
                let parentOf i (hs: string list) = if i = 1 then List.head hs else baseId
                let heads, dag = chainLanes w hashFn parentOf d0 ls
                overHeads heads dag
            | _ ->
                arrivalOrders (List.length ls)
                |> List.map (fun p -> foldOnce w footprintOf hashFn hashState gen.State0 gen.BaseOp (permuteBy p ls))
                |> List.distinct

        let shapeName (shape: int) =
            match shape with
            | 1 -> "duplicate-head"
            | 2 -> "fast-forward"
            | _ -> "disjoint"

        // Every lane set is evidence for the classification law; a folding set for law 1 and a
        // halting set for law 2, which is why those two read through the guard that counts both
        // (Phase 297's covered cells) — a sample that never folds, or never halts, is reported once,
        // by the guard, with the remedy. Each drawn lane set is judged in all three shapes (Phase
        // 300); the draw itself is unchanged.
        let judge (at: string -> string) (shape: int) (lanes: 'Op list list) =
            match outcomesOf shape lanes with
            | [ single ] ->
                classLaw.Saw()

                match single with
                | LaneFolded _ ->
                    // Phase 302 — a fold is counted only over two or more non-empty lanes: an empty
                    // set, or one lane, folds the same under every order by construction, and a sample
                    // made of those met the guard while testing order-invariance never.
                    if (lanes |> List.filter (List.isEmpty >> not) |> List.length) >= 2 then
                        folded <- folded + 1

                    foldLaw.Saw()
                | LaneHalted _ ->
                    halted <- halted + 1
                    haltLaw.Saw()
                | LaneRejected _ -> rejected <- rejected + 1
            | _ ->
                let small = shrinkLanes (fun ls -> List.length (outcomesOf shape ls) > 1) lanes
                let smallOutcomes = outcomesOf shape small

                let msg () =
                    at (
                        string (List.length smallOutcomes)
                        + " distinct outcomes over the sampled arrival orders of "
                        + string (List.length small)
                        + " lane(s) in the "
                        + shapeName shape
                        + " shape; shrunk to
"
                        + renderLanes w.Encode small
                        + "
outcomes:
"
                        + (smallOutcomes
                           |> List.map renderOutcome
                           |> String.concat
                               "
")
                    )

                if (smallOutcomes |> List.map kindTag |> List.distinct |> List.length) > 1 then
                    classLaw.Check(false, msg)
                else
                    classLaw.Saw()

                    match List.head smallOutcomes with
                    | LaneHalted _ -> haltLaw.Check(false, msg)
                    | _ -> foldLaw.Check(false, msg)

        LawKit.run iterations seed (fun rng _ at ->
            let lanes = rng.Draw(gen.Lanes laneCount)

            if not (List.isEmpty lanes) then
                duplicateHeads <- duplicateHeads + 1

            if List.length lanes >= 2 then
                fastForwards <- fastForwards + 1

            for shape in [ 0; 1; 2 ] do
                judge at shape lanes)

        LawKit.results [ foldLaw; haltLaw; classLaw ]
        // The two coverage guards this pack shipped by hand in Phase 100 — the ones that caught
        // 150 halting trials out of 150 — expressed through the kit's shared adequacy guard
        // (Phase 121), so the remedy sentence and the counts read the same here as everywhere.
        //
        // Phase 245 — a lane set the reducer rejects under every order is COUNTED beside the two
        // demanded outcomes and not demanded itself: law 1 holds over it, but it tests neither a
        // clean fold nor a halt, so a sample made mostly of rejections must say so, and a domain
        // whose reducer never rejects must not be starved for it.
        //
        // Phase 297 — the guard carries the family's roster id, as every guard in the kit does.
        @ [ SampleAdequacy.reachedBeside
                "FoldConfluence.laneFoldLawsWith"
                "lane-fold outcome"
                seed
                [ "folded", folded; "halted", halted ]
                // Phase 300 — the two shared-history shapes are counted beside the outcomes: they are
                // drawn by construction from every lane set with enough lanes, so reported, not demanded.
                [ "rejected", rejected
                  "duplicate-head", duplicateHeads
                  "fast-forward", fastForwards ] ]

    /// The fold-confluence laws (Phase 100) at a DOMAIN'S witness and lane generator, chained with
    /// `OpStream.defaultHash` — the shape a domain runs. `laneFoldLawsWith` is this family with the
    /// `HashFn` as a further parameter, last before the seed; see it for the law text, the sampling
    /// bound, and the coverage guards. (Phase 390's spelling of the naming rule.)
    let laneFoldLawsAt
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (footprintOf: 'Op -> Footprint)
        (hashState: 'State -> string)
        (gen: LaneGen<'Op, 'State>)
        (laneCount: int)
        (seed: int)
        (iterations: int)
        : LawResult list =
        laneFoldLawsWith w footprintOf hashState gen laneCount OpStream.defaultHash seed iterations

    /// The aggregate verdict, matching `Conformance.certify`'s shape: run the laws and report
    /// whether every one passed.
    let certifyFold
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (footprintOf: 'Op -> Footprint)
        (hashState: 'State -> string)
        (gen: LaneGen<'Op, 'State>)
        (laneCount: int)
        (seed: int)
        (iterations: int)
        : ConformanceReport =
        let results = laneFoldLawsAt w footprintOf hashState gen laneCount seed iterations

        { Results = results
          AllPassed = results |> List.forall _.Passed }
