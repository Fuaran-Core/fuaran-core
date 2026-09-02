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
//  Three outcomes, not two. The pack distinguishes **folding identically** from **halting
//  identically**, and treats a lane set that folds under one arrival order and halts under
//  another as its own, separately-named defect — that is the bug class the pack exists to
//  catch. A conflict visible only from one direction is worse than a conflict, because the
//  deployment that happened to receive the lanes the other way round proceeds, and the two
//  replicas silently disagree about whether they diverged at all.
//
//  FSharp.Core only (the `ConfRng` LCG, no FsCheck), Fable-clean — as the rest of the kit.
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
    /// The domain reducer rejected an op while replaying the composed script. A rejection that
    /// is not identical under every arrival order is a divergence like any other.
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

/// The fold-confluence law family (Phase 100). A domain runs `laneFoldLaws` against its own
/// witness; `foldOnce` and `shrinkLanes` are exposed because a domain investigating a
/// counterexample wants to drive them directly.
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
        | ConcurrentUpdate -> "concurrent-update"
        | InsertPositionClash -> "insert-position-clash"
        | MoveVsRemove -> "move-vs-remove"

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

    /// Fold ONE lane set in the order given, through the real DAG surface rather than a
    /// re-implementation of it: each lane is chained onto a shared base node **under its own
    /// actor**, the lane deltas are recovered with `Dag.betweenOps`, `Dag.reconcileMany` checks
    /// every unordered lane pair, and the composed script is replayed through the domain reducer
    /// from `state0`.
    ///
    /// The per-lane actor is load-bearing, not decoration: node ids are content hashes of
    /// (parents, actor, op), so two lanes carrying the SAME op sequence off the same base would
    /// otherwise converge to one chain — correct content-addressing, but it would silently
    /// collapse the trial to a single lane and certify nothing.
    let foldOnce
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (footprintOf: 'Op -> Footprint)
        (hashFn: HashFn)
        (hashState: 'State -> string)
        (state0: 'State)
        (baseOp: 'Op)
        (lanes: 'Op list list)
        : LaneFoldOutcome =
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

        match Dag.reconcileMany footprintOf dag baseId heads with
        | Error cs -> LaneHalted(canonicalConflictReport w.Encode cs)
        | Ok script ->
            match script |> List.fold (fun acc op -> acc |> Result.bind (w.Apply op)) (Ok state0) with
            | Ok st -> LaneFolded(hashState st)
            | Error rej -> LaneRejected(sprintf "%A" rej)

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
                  yield dropLane i ls
              for li in 0 .. List.length ls - 1 do
                  for oi in 0 .. List.length (List.item li ls) - 1 do
                      yield dropOp li oi ls ]

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
    let renderLanes (encodeOp: 'Op -> string) (lanes: 'Op list list) : string =
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
    ///    generator until lanes collide, not conclude its ops cannot conflict.
    ///
    /// Every divergence is `shrinkLanes`-reduced before it is reported, and the counterexample
    /// carries the seed, the iteration, the shrunk lanes in the domain's own encoding, and each
    /// distinct outcome — a reproducer, not a symptom.
    let laneFoldLawsWith
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (footprintOf: 'Op -> Footprint)
        (hashFn: HashFn)
        (hashState: 'State -> string)
        (gen: LaneGen<'Op, 'State>)
        (laneCount: int)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable foldCx = None
        let mutable haltCx = None
        let mutable classCx = None
        let mutable folded = 0
        let mutable halted = 0

        let outcomesOf (ls: 'Op list list) =
            arrivalOrders (List.length ls)
            |> List.map (fun p -> foldOnce w footprintOf hashFn hashState gen.State0 gen.BaseOp (permuteBy p ls))
            |> List.distinct

        for i in 0 .. iterations - 1 do
            let lanes, r' = gen.Lanes laneCount rng
            rng <- r'

            match outcomesOf lanes with
            | [ single ] ->
                match single with
                | LaneFolded _ -> folded <- folded + 1
                | LaneHalted _ -> halted <- halted + 1
                | LaneRejected _ -> ()
            | _ ->
                let small = shrinkLanes (fun ls -> List.length (outcomesOf ls) > 1) lanes
                let smallOutcomes = outcomesOf small

                let msg =
                    "seed="
                    + string seed
                    + " iter="
                    + string i
                    + ": "
                    + string (List.length smallOutcomes)
                    + " distinct outcomes over "
                    + string (List.length (arrivalOrders (List.length small)))
                    + " sampled arrival order(s) of "
                    + string (List.length small)
                    + " lane(s); shrunk to\n"
                    + renderLanes w.Encode small
                    + "\noutcomes:\n"
                    + (smallOutcomes |> List.map renderOutcome |> String.concat "\n")

                if (smallOutcomes |> List.map kindTag |> List.distinct |> List.length) > 1 then
                    if classCx.IsNone then
                        classCx <- Some msg
                else
                    match List.head smallOutcomes with
                    | LaneHalted _ ->
                        if haltCx.IsNone then
                            haltCx <- Some msg
                    | _ ->
                        if foldCx.IsNone then
                            foldCx <- Some msg

        [ { Law = "lane-fold determinism (every arrival order of a folding lane set folds to one state hash)"
            Passed = foldCx.IsNone
            Counterexample = foldCx }
          { Law =
              "lane-halt determinism (a halting lane set halts with the same canonical report under every arrival order)"
            Passed = haltCx.IsNone
            Counterexample = haltCx }
          { Law =
              "outcome classification is arrival-order-invariant (no lane set folds under one order and halts under another)"
            Passed = classCx.IsNone
            Counterexample = classCx }
          // The two coverage guards this pack shipped by hand in Phase 100 — the ones that caught
          // 150 halting trials out of 150 — expressed through the kit's shared adequacy guard
          // (Phase 121), so the remedy sentence and the counts read the same here as everywhere.
          SampleAdequacy.reached "FoldConfluence" "lane-fold outcome" seed [ "folded", folded; "halted", halted ] ]

    /// The fold-confluence laws (Phase 100) pinned to `OpStream.defaultHash` — the shape a domain
    /// runs. See `laneFoldLawsWith` for the law text, the sampling bound, and the coverage guards.
    let laneFoldLaws
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (footprintOf: 'Op -> Footprint)
        (hashState: 'State -> string)
        (gen: LaneGen<'Op, 'State>)
        (laneCount: int)
        (seed: int)
        (iterations: int)
        : LawResult list =
        laneFoldLawsWith w footprintOf OpStream.defaultHash hashState gen laneCount seed iterations

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
        let results = laneFoldLaws w footprintOf hashState gen laneCount seed iterations

        { Results = results
          AllPassed = results |> List.forall (fun r -> r.Passed) }
