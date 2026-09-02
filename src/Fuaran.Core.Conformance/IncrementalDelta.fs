namespace Fuaran.Core

// ============================================================================
//  The incremental-evaluation equivalence family (Phase 99).
//
//  `Incremental` evaluates a `Transform` pipeline against a delta instead of
//  from scratch. That is only ever an OPTIMISATION, so the claim it has to earn
//  is an equality: for every (base, delta) pair, the incremental result is the
//  reference result — the same table, or the same `EvalError`, with no
//  exception for the pairs that are awkward to get right.
//
//  The family is built so a defect cannot hide in a case the generator never
//  reaches. Its edits deliberately include the three that a first
//  implementation gets wrong and that no equality-of-happy-path would catch:
//  a pure REORDERING (which no identity diff reports as a row change, and which
//  moves `First` / `Last` and a float `Sum`), a filter that drops the only
//  typed row of a DERIVED column (which moves that column's inferred type), and
//  a SCHEMA change (which is not a row change at all). Its pipelines include
//  the ones the seam declines, because "falls back correctly" is a claim about
//  the answer as much as "propagates correctly" is.
//
//  Phase 115 admitted `Sort`, and the corpus grew to meet it rather than
//  merely to cover it. A merged order is wrong in exactly one way that an
//  equality over a tie-free corpus cannot see, so the generated tables draw
//  their sort keys from a range of three over up to six rows — ties are the
//  common case, not the edge — and the sort-bearing pipelines put a sort in
//  every position that matters: last (the shape the estate's recompute fixture
//  family carries), before a type-inferring `Derive` and a `Filter`, and
//  feeding an order-sensitive maintained `GroupBy` whose `First` / `Last`
//  aggregates read the order the sort produced. The `reverse` edit is what
//  makes those load-bearing: an identity diff reports it as quiet, so a merge
//  that trusted its cached order without checking arrival order would answer
//  a delta that named nothing with a table in the wrong order.
//
//  Every sample also records its FOOTPRINT — what the prime evaluated and what
//  the refresh evaluated — so the family certifies not only that the answer is
//  right but that the seam did less work to reach it. An incremental evaluator
//  that silently recomputed everything would pass an equality suite perfectly.
//
//  FSharp.Core only, Fable-clean.
// ============================================================================

/// One generated (base, delta) pair and what it cost — the record the family both judges and
/// reports. Public so a consumer can run the generator and print the footprints itself.
type IncrementalSample =
    {
        Seed: int
        Iteration: int
        /// The pipeline evaluated.
        Pipeline: Transform list
        /// Its declared strategy.
        Strategy: IncrementalStrategy
        /// What the initial full evaluation over the BASE cost.
        Prime: RecomputeFootprint
        /// What a full evaluation over the CHANGED source would have cost — the baseline the refresh
        /// has to beat, and the only honest one, since the changed source may be the larger table.
        Full: RecomputeFootprint
        /// What advancing the primed state against the delta cost.
        Refresh: RecomputeFootprint
        /// Did the incremental answer equal the reference answer?
        Equivalent: bool
        /// Did priming equal a full reference evaluation over the base?
        PrimeEquivalent: bool
        /// A short tag naming the edit that produced the delta — what a counterexample cites.
        Edit: string
    }

/// The incremental-evaluation equivalence family: generate (base, delta) pairs, run both
/// evaluators, and certify the answers identical while recording the work each did.
module IncrementalDelta =

    // ---- the corpus ----

    let private idw = RowIdentity.byColumn "id"

    let private agg name fn ofCol : Agg = { Name = name; Fn = fn; Of = ofCol }

    /// The generated table: a string identity column and two int columns, `b` drawn from a small
    /// range so groups have several members (a one-row-per-group corpus would never exercise a
    /// maintained aggregate at all).
    let private mkTable (rows: (string * Cell * Cell) list) : Table =
        { Schema = [ "id", StringType; "a", IntType; "b", IntType ]
          Columns =
            [ Column.create "id" StringType (rows |> List.map (fun (i, _, _) -> Str i))
              Column.create "a" IntType (rows |> List.map (fun (_, a, _) -> a))
              Column.create "b" IntType (rows |> List.map (fun (_, _, b) -> b)) ] }

    /// The same rows with a fourth column — a SCHEMA change, which is not a row change and which
    /// the seam must recognise as such rather than diff its way through.
    let private mkWideTable (rows: (string * Cell * Cell) list) : Table =
        { Schema = [ "id", StringType; "a", IntType; "b", IntType; "c", IntType ]
          Columns =
            [ Column.create "id" StringType (rows |> List.map (fun (i, _, _) -> Str i))
              Column.create "a" IntType (rows |> List.map (fun (_, a, _) -> a))
              Column.create "b" IntType (rows |> List.map (fun (_, _, b) -> b))
              Column.create "c" IntType (rows |> List.map (fun _ -> Int 1)) ] }

    /// The pipelines. `0`–`5` and `9`–`13` are incrementalisable; `6`–`8` are the declined ones,
    /// present because a fall-back that returns the wrong answer is the worse failure. `11`–`13`
    /// are the sort-bearing shapes (Phase 115): a sort last, a sort before the steps that read the
    /// order it produced, and a sort feeding an order-sensitive maintained group.
    let private pipelineOf (k: int) : Transform list =
        match k with
        | 0 -> [ Filter(Binary(Gt, Col "a", Lit(Int 0))) ]
        | 1 -> [ Derive("d", Binary(Add, Col "a", Col "b")); Project [ "id", "id"; "d", "d" ] ]
        | 2 ->
            [ Filter(Binary(Gt, Col "a", Lit(Int 0)))
              Derive("d", Binary(Mul, Col "a", Lit(Int 2))) ]
        | 3 -> [ GroupBy([ "b" ], [ agg "n" Count "a"; agg "s" Sum "a" ]) ]
        | 4 ->
            [ Filter(Binary(Gt, Col "a", Lit(Int -5)))
              GroupBy([ "b" ], [ agg "mx" Max "a"; agg "f" First "id"; agg "l" Last "id" ]) ]
        | 5 -> [ Sort [ "b", Asc ] ] // merged order over the TIE-HEAVY key (Phase 115)
        | 6 -> [ Project [ "b", "b" ]; Distinct ] // declined: whole-relation
        | 7 ->
            // declined: a maintainable step that is not last
            [ GroupBy([ "b" ], [ agg "n" Count "a" ])
              Filter(Binary(Gt, Col "n", Lit(Int 0))) ]
        | 8 -> [ Limit(2, 0) ] // declined: order-dependent
        | 9 ->
            // a derived column whose TYPE depends on which rows survive, followed by a filter that
            // can drop the only typed row — the inferred-type trap.
            [ Derive("d", Case([ Binary(Gt, Col "a", Lit(Int 0)), Lit(Str "pos") ], Lit Null))
              Filter(Binary(Lt, Col "a", Lit(Int 0))) ]
        | 10 -> [ Derive("a", Binary(Add, Col "a", Lit(Int 1))) ] // Derive OVERWRITING a column
        | 11 ->
            // the shape the estate's recompute fixture family carries: a filter, then a sort.
            [ Filter(Binary(Gt, Col "a", Lit(Int 0))); Sort [ "a", Asc ] ]
        | 12 ->
            // a sort that is NOT last, followed by the two steps that read the order it produced —
            // a derive whose column TYPE is inferred over the frame, and a filter.
            [ Sort [ "b", Asc ]
              Derive("d", Case([ Binary(Gt, Col "a", Lit(Int 0)), Lit(Str "pos") ], Lit Null))
              Filter(Binary(Lt, Col "a", Lit(Int 3))) ]
        | _ ->
            // a sort feeding an order-sensitive maintained group: `First` and `Last` read the
            // position the sort put each row in, and `b` ties heavily, so the sort's STABILITY is
            // what decides the answer rather than its comparator alone.
            [ Sort [ "b", Asc; "a", Desc ]
              GroupBy([ "b" ], [ agg "f" First "id"; agg "l" Last "id"; agg "n" Count "a" ]) ]

    let private pipelineCount = 14

    /// Apply one edit to the base rows, returning the new table and a tag naming the edit.
    let private editOf (k: int) (rows: (string * Cell * Cell) list) (n: int) : Table * string =
        match k with
        | 0 -> mkTable rows, "none"
        | 1 -> mkTable (rows @ [ "z1", Int 4, Int 1; "z2", Int -4, Int 2 ]), "append"
        | 2 ->
            (match rows with
             | [] -> mkTable rows, "remove(empty)"
             | _ :: rest -> mkTable rest, "removeFirst")
        | 3 ->
            (match rows with
             | [] -> mkTable rows, "change(empty)"
             | (i, _, b) :: rest -> mkTable ((i, Int 42, b) :: rest), "changeFirstA")
        | 4 -> mkTable (List.rev rows), "reverse"
        | 5 ->
            (match rows with
             | [] -> mkTable rows, "null(empty)"
             | (i, _, b) :: rest -> mkTable ((i, Null, b) :: rest), "nullFirstA")
        | 6 ->
            (match List.rev rows with
             | [] -> mkTable rows, "changeLast(empty)"
             | (i, a, _) :: revRest -> mkTable (List.rev ((i, a, Int((n + 1) % 3)) :: revRest)), "regroupLast")
        | 7 -> mkWideTable rows, "schemaWiden"
        | 8 ->
            // A MIDDLE row's non-key column. Under a sort keyed on `b` this is the tie scenario a
            // merge gets wrong silently: the changed row is lifted out of the cached order and put
            // back among rows it ties with, and only the arrival-position tiebreak decides where.
            // Changing the FIRST row (edit 3) reaches it far less often — the earliest arrival wins
            // its ties under a merge that has no tiebreak at all.
            (match rows with
             | [] -> mkTable rows, "changeMiddle(empty)"
             | _ ->
                 let m = List.length rows / 2

                 mkTable (rows |> List.mapi (fun j (i, a, b) -> if j = m then i, Int 77, b else i, a, b)),
                 "changeMiddleA")
        | _ -> mkTable (rows @ [ "z3", Int 7, Int 0 ]), "appendOne"

    let private editCount = 10

    /// The delta the refresh is driven by. The identity diff is the normal producer; the other
    /// three are the honest coarse answers a source may give instead, and each must still yield the
    /// reference result.
    let private deltaOf (k: int) (before: Table) (after: Table) : TableDelta =
        match k with
        | 0
        | 1
        | 2 ->
            match Delta.diff idw before after with
            | Ok d -> d
            | Error _ -> FullRefresh
        | 3 -> FullRefresh
        | 4 -> Delta.diffByOrdinal before after
        | _ -> Delta.ofColumns idw.Scheme [ "a" ]

    let private deltaCount = 6

    // ---- generation ----

    /// Generate the samples for a seed — each one a base table, a pipeline, an edit, and a delta,
    /// with both evaluators run and both footprints recorded.
    let samples (seed: int) (iterations: int) : IncrementalSample list =
        let mutable rng = ConfRng.ofSeed seed

        [ for i in 0 .. iterations - 1 do
              let nRows, r0 = ConfRng.intBelow 9 rng
              let mutable r = r0

              let rows =
                  [ for j in 0..nRows do
                        let a, r1 = ConfRng.intBelow 12 r
                        r <- r1
                        let b, r2 = ConfRng.intBelow 3 r
                        r <- r2
                        "r" + string j, Int(a - 5), Int b ]

              let pk, r3 = ConfRng.intBelow pipelineCount r
              r <- r3
              let ek, r4 = ConfRng.intBelow editCount r
              r <- r4
              let dk, r5 = ConfRng.intBelow deltaCount r
              r <- r5
              rng <- r

              let before = mkTable rows
              let after, edit = editOf ek rows i
              let pipeline = pipelineOf pk
              let delta = deltaOf dk before after
              let p = Incremental.plan pipeline

              let reference = DataFrame.evalPipeline pipeline after

              match Incremental.primeOn idw pipeline before with
              | Error _ ->
                  // The base itself does not evaluate; there is no incremental claim to make, and
                  // the sample is dropped rather than counted as a pass.
                  ()
              | Ok primed ->
                  let refreshed = Incremental.refreshOn idw pipeline primed delta after

                  let full =
                      match Incremental.primeOn idw pipeline after with
                      | Ok s -> s.Footprint
                      | Error _ -> primed.Footprint

                  yield
                      { Seed = seed
                        Iteration = i
                        Pipeline = pipeline
                        Strategy = p.Strategy
                        Prime = primed.Footprint
                        Full = full
                        Refresh =
                          (match refreshed with
                           | Ok s -> s.Footprint
                           | Error _ -> primed.Footprint)
                        Equivalent = (refreshed |> Result.map Incremental.result) = reference
                        PrimeEquivalent = (Ok primed.Output = DataFrame.evalPipeline pipeline before)
                        Edit = edit } ]

    /// The recorded footprint lines — one per sample, counts only, so two hosts print the same
    /// report. This is the instrument: it is what shows the seam did less work, which an equality
    /// suite alone cannot say.
    let report (xs: IncrementalSample list) : string list =
        xs
        |> List.map (fun s ->
            let strategy =
                match s.Strategy with
                | RowLocal -> "rowLocal"
                | RowLocalThenGroups -> "rowLocal+groups"
                | ReferenceOnly r -> "reference(" + Incremental.reasonString r + ")"

            "seed="
            + string s.Seed
            + " iter="
            + string s.Iteration
            + " "
            + strategy
            + " edit="
            + s.Edit
            + " | prime: "
            + Incremental.footprintString s.Prime
            + " | full: "
            + Incremental.footprintString s.Full
            + " | refresh: "
            + Incremental.footprintString s.Refresh)

    // ---- the laws ----

    /// The incremental-evaluation equivalence laws (Phase 99):
    ///
    ///  - **oracle equivalence** — the incremental result equals the reference result (table for
    ///    table, error for error) for every generated (base, delta) pair;
    ///  - **priming is a full evaluation** — a primed state's result equals `evalPipeline` over the
    ///    base, so the seam's entry point is the reference answer and nothing else;
    ///  - **the declared boundary is honoured** — a pipeline `plan` declares `ReferenceOnly`
    ///    reports a `FullRecompute` footprint carrying that same reason (or `ReusedPrior`, which is
    ///    sound for any strategy when the source did not move), and an incrementalisable pipeline
    ///    given a well-formed identity delta does NOT report one;
    ///  - **the refresh does no more work than a full evaluation** — the measured rows-evaluated
    ///    of a restricted refresh never exceeds what priming over the SAME (changed) source costs.
    ///    The baseline is the changed source deliberately: measuring against the BASE is wrong,
    ///    because an append makes the new source larger and the appended row legitimately passes
    ///    through more steps than the rows it joined. This is the claim an equality suite cannot
    ///    make, and the one an evaluator that quietly recomputed everything would fail;
    ///  - **plan is pure and total** — every step is classified, recomputing agrees, and
    ///    `isIncremental` agrees with the strategy;
    ///  - **the declined verbs are actually reached** — the run observes both fall-back classes the
    ///    corpus contains, so the boundary is exercised rather than merely asserted;
    ///  - **a sort is merged, not declined and not re-primed** — the run observes a pipeline whose
    ///    plan carries a `MergeOrder` step answering a delta with a RESTRICTED refresh. Without this
    ///    the equivalence laws would stay green over a seam that had quietly gone back to evaluating
    ///    every sort-bearing pipeline in full, since a full evaluation is always the right answer.
    let laws (seed: int) (iterations: int) : LawResult list =
        let xs = samples seed iterations

        let cite (s: IncrementalSample) (what: string) =
            "seed="
            + string s.Seed
            + " iter="
            + string s.Iteration
            + " edit="
            + s.Edit
            + ": "
            + what

        let equivalence =
            xs
            |> List.tryFind (fun s -> not s.Equivalent)
            |> Option.map (fun s -> cite s "incremental result <> reference result")

        let priming =
            xs
            |> List.tryFind (fun s -> not s.PrimeEquivalent)
            |> Option.map (fun s -> cite s "priming <> a full reference evaluation over the base")

        let mutable boundary = None
        let mutable work = None
        let mutable planPurity = None

        for s in xs do
            let p = Incremental.plan s.Pipeline

            if p <> Incremental.plan s.Pipeline && planPurity.IsNone then
                planPurity <- Some(cite s "plan is not a pure function of the pipeline")

            if List.length p.Steps <> List.length s.Pipeline && planPurity.IsNone then
                planPurity <- Some(cite s "plan did not classify every step")

            let agrees =
                match p.Strategy with
                | ReferenceOnly _ -> not (Incremental.isIncremental p)
                | RowLocal
                | RowLocalThenGroups -> Incremental.isIncremental p

            if not agrees && planPurity.IsNone then
                planPurity <- Some(cite s "isIncremental disagrees with the strategy")

            match p.Strategy, s.Refresh.Recompute with
            | ReferenceOnly r, FullRecompute r2 ->
                if r <> r2 && boundary.IsNone then
                    boundary <- Some(cite s "a declined pipeline reported a different reason")
            | ReferenceOnly _, ReusedPrior ->
                // Sound for ANY strategy: the source is byte-identical and the pipeline and env
                // have not moved, so the prior result still stands. A declined verb is declined
                // because it cannot answer a CHANGE, not because it must be re-run when nothing
                // changed.
                ()
            | ReferenceOnly _, _ ->
                if boundary.IsNone then
                    boundary <- Some(cite s "a declined pipeline did not report a full evaluation")
            | _, FullRecompute _ -> ()
            | _ ->
                if
                    Incremental.rowsEvaluated s.Refresh > Incremental.rowsEvaluated s.Full
                    && work.IsNone
                then
                    work <- Some(cite s "a restricted refresh evaluated more rows than a full evaluation would")

        let sawDeclined =
            xs
            |> List.exists (fun s ->
                match s.Strategy with
                | ReferenceOnly _ -> true
                | _ -> false)

        let sawRestricted =
            xs
            |> List.exists (fun s ->
                match s.Refresh.Recompute with
                | RowsRecomputed _
                | GroupsRecomputed _
                | ReusedPrior -> true
                | _ -> false)

        let sawMergedOrder =
            xs
            |> List.exists (fun s ->
                (Incremental.plan s.Pipeline).Steps
                |> List.exists (function
                    | MergeOrder _ -> true
                    | _ -> false)
                && (match s.Refresh.Recompute with
                    | RowsRecomputed _
                    | GroupsRecomputed _ -> true
                    | _ -> false))

        [ { Law = "every generated pair produced a sample (no base failed to evaluate)"
            Passed = List.length xs = iterations
            Counterexample =
              if List.length xs = iterations then
                  None
              else
                  Some(
                      "seed="
                      + string seed
                      + ": "
                      + string (List.length xs)
                      + " of "
                      + string iterations
                      + " samples"
                  ) }
          { Law = "incremental evaluation equals the reference evaluator for every (base, delta) pair"
            Passed = equivalence.IsNone
            Counterexample = equivalence }
          { Law = "priming a state is a full reference evaluation"
            Passed = priming.IsNone
            Counterexample = priming }
          { Law = "a declined pipeline reports a full evaluation carrying its declared reason"
            Passed = boundary.IsNone
            Counterexample = boundary }
          { Law = "a restricted refresh evaluates no more rows than a full evaluation would"
            Passed = work.IsNone
            Counterexample = work }
          { Law = "plan is pure, total, and agrees with isIncremental"
            Passed = planPurity.IsNone
            Counterexample = planPurity }
          { Law = "a sort-bearing pipeline answered a delta with a restricted refresh"
            Passed = sawMergedOrder
            Counterexample =
              if sawMergedOrder then
                  None
              else
                  Some("seed=" + string seed + ": no MergeOrder step reached a restricted refresh") }
          { Law = "the corpus reaches both sides of the boundary (a declined pipeline and a restricted refresh)"
            Passed = sawDeclined && sawRestricted
            Counterexample =
              if sawDeclined && sawRestricted then
                  None
              else
                  Some(
                      "seed="
                      + string seed
                      + ": declined="
                      + string sawDeclined
                      + " restricted="
                      + string sawRestricted
                  ) } ]
