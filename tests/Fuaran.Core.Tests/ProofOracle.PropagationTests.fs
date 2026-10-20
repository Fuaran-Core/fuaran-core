module Fuaran.Core.Tests.ProofOracle.PropagationTests

// The `Proofs.Oracle` differential for `proofs/Propagation.fst`: the extracted model
// (`proofs/oracle/Propagation.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

// Phase 186 — the extracted INCREMENTAL-PROMISE model, bound the same way and for the same
// reason: production's `Fuaran.Core.Propagation` module shares the model's name once
// `Fuaran.Core` is open.
module ModelProp = Propagation

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

// ------------------------------------------------------------------------------------------
// Phase 186 — the INCREMENTAL PROMISE. `proofs/Propagation.fst` models
// `Fuaran.Core.Propagation`'s dirty set (`dependents`, `dirtyFromChangedIds`) and its
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
    | Error(Propagation.PropagationError.EvalUnknownChange ids) -> sprintf "EvalUnknownChange %A" ids
    | Error(Propagation.PropagationError.EvalNodeFailed(n, m)) -> sprintf "EvalNodeFailed %s %s" n m
    | Error(Propagation.PropagationError.EvalUndeclaredRead(n, r)) -> sprintf "EvalUndeclaredRead %s %s" n r

let private modelOutcomeRender
    (r: ModelProp.outcome<ModelProp.eval_outcome<int>, ModelProp.propagation_error>)
    : string =
    match r with
    | ModelProp.Ok o -> sprintf "Ok values=%A cyclic=%A" (o.values |> Map.ofList |> Map.toList) o.cyclic
    | ModelProp.Error(ModelProp.EvalUnknownChange ids) -> sprintf "EvalUnknownChange %A" ids
    | ModelProp.Error(ModelProp.EvalNodeFailed(n, m)) -> sprintf "EvalNodeFailed %s %s" n m
    | ModelProp.Error(ModelProp.EvalUndeclaredRead(n, r)) -> sprintf "EvalUndeclaredRead %s %s" n r

type private PropTally =
    {
        PDiffs: string list
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
        UnknownRefused: int
        /// Phase 317 — trials where `eval` succeeded and `evalFor` was held to it on the needed set.
        PullAgreed: int
        /// Phase 317 — trials whose needed set left at least one node of the map out.
        PullPartial: int
    }

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
        | Error(Propagation.PropagationError.EvalUnknownChange _) ->
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

    // (6) Phase 317 — the PULL: `neededFor`, `evalFor` and `evalForWith` against `needed_for`,
    // `eval_for`, `eval_for_with` and `walk_for_invoked`, over a drawn TARGET set — one or two ids,
    // one trial in eight also naming an id the map does not hold. The targets are drawn from a
    // stream of their own, seeded by the trial, so the draws every leg above makes are unchanged.
    let mutable targetRng = ConfRng.ofSeed (31700 + i)

    let tdraw n =
        let v, r' = ConfRng.intBelow n targetRng
        targetRng <- r'
        v

    let targets =
        [ for _ in 0 .. tdraw 2 -> string (tdraw nNodes) ]
        @ (if tdraw 8 = 0 then [ "no-such-id" ] else [])
        |> Set.ofList

    let prodNeeded = Propagation.neededFor deps targets
    let modelNeeded = Set.ofList (ModelProp.needed_for mdeps (Set.toList targets))

    if prodNeeded <> modelNeeded then
        diffs <-
            sprintf
                "%s targets=%A: needed production=%A model=%A"
                where
                (Set.toList targets)
                (Set.toList prodNeeded)
                (Set.toList modelNeeded)
            :: diffs

    let pullInvoked = ResizeArray()

    let prodPull =
        Propagation.evalFor (propProdEvaluator spec1 pullInvoked) targets deps

    let modelPull =
        ModelProp.eval_for (propModelEvaluator spec1) mreads (Set.toList targets) mdeps mtopo

    if prodOutcomeRender prodPull <> modelOutcomeRender modelPull then
        diffs <-
            sprintf
                "%s targets=%A: evalFor production=%s model=%s"
                where
                (Set.toList targets)
                (prodOutcomeRender prodPull)
                (modelOutcomeRender modelPull)
            :: diffs

    let modelPullInvoked =
        ModelProp.walk_for_invoked (propModelEvaluator spec1) mreads (Set.toList targets) mdeps mtopo

    if List.ofSeq pullInvoked <> modelPullInvoked then
        diffs <-
            sprintf
                "%s targets=%A: evalFor EVALUATED production=%A model=%A"
                where
                (Set.toList targets)
                (List.ofSeq pullInvoked)
                modelPullInvoked
            :: diffs

    let prodPullWith =
        Propagation.evalForWith (fun resolve _ id -> spec1 resolve id) targets deps

    let modelPullWith =
        ModelProp.eval_for_with (ModelProp.lift (propModelEvaluator spec1)) mreads (Set.toList targets) mdeps mtopo

    if prodOutcomeRender prodPullWith <> modelOutcomeRender modelPullWith then
        diffs <-
            sprintf "%s targets=%A: evalForWith disagrees" where (Set.toList targets)
            :: diffs

    // the theorem on the shipped driver: where `eval` succeeds, `evalFor` is it on the needed set
    let pulledAgreed =
        match Propagation.eval (propProdEvaluator spec1 (ResizeArray())) deps, prodPull with
        | Ok full, Ok pulled ->
            let expected: Propagation.EvalOutcome<int> =
                { Values = full.Values |> Map.filter (fun k _ -> Set.contains k prodNeeded)
                  Cyclic = full.Cyclic |> List.filter (List.exists (fun n -> Set.contains n prodNeeded)) }

            if pulled <> expected then
                diffs <-
                    sprintf "%s targets=%A: evalFor IS NOT eval on the needed set" where (Set.toList targets)
                    :: diffs

            1
        | _ -> 0

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
      UnknownRefused = acc.UnknownRefused + unknownRefused
      PullAgreed = acc.PullAgreed + pulledAgreed
      PullPartial = acc.PullPartial + (if Set.count prodNeeded < nNodes then 1 else 0) },
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
          UnknownRefused = 0
          PullAgreed = 0
          PullPartial = 0 }

    for i in 1..trials do
        let t, r' = propProbe bridge i tally rng
        tally <- t
        rng <- r'

    tally

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 186: the incremental promise (proofs/Propagation.fst) ----

          testCase
              "the propagation oracle agrees with Propagation.dependents, dirtyFromChangedIds, eval, evalFrom, neededFor, evalFor and evalForWith, over generated dependency maps, change sets and target sets"
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

                  // Phase 317 — the pull leg: every trial draws a target set beside its change set.
                  Expect.isGreaterThan
                      t.PullAgreed
                      200
                      (sprintf "evalFor was eval on the needed set on the shipped driver (pullAgreed=%d)" t.PullAgreed)

                  Expect.isGreaterThan
                      t.PullPartial
                      100
                      (sprintf "target sets that need less than the whole map arose (pullPartial=%d)" t.PullPartial)

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
                  (t.PDiffs |> List.exists (fun d -> d.Contains ": needed production="))
                  "and it reaches the NEEDED SET (Phase 317), which the lost edge shrinks from the other side"

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
                  (Error(Propagation.PropagationError.EvalUndeclaredRead("b", "a")))
                  "the full driver refuses the undeclared read, naming the node and the read"

              let ranU = ResizeArray()

              Expect.equal
                  (Propagation.evalFrom (propProdEvaluator (spec 2) ranU) Map.empty (Set.singleton "a") undeclared)
                  (Error(Propagation.PropagationError.EvalUndeclaredRead("b", "a")))
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
                  (Error(Propagation.PropagationError.EvalUnknownChange [ "nope" ]))
                  "an unknown change is the typed refusal naming it"

              Expect.isEmpty ranUnknown "and no evaluator ran"

          // ---- Phase 308: the order by certificate, and the prior-aware drivers, beside production ----

          testCase
              "the extracted certificate is production's validTopo, and production's sort passes it on every generated graph — `valid_topo_distinct`"
          <| fun _ ->
              let mutable rng = ConfRng.ofSeed 308
              let mutable sorted = 0
              let mutable cyclic = 0
              let mutable refused = 0
              let mutable diffs = []

              let draw n =
                  let v, r' = ConfRng.intBelow n rng
                  rng <- r'
                  v

              for i in 1..300 do
                  let nNodes = draw 7 + 1
                  let shape = draw 4

                  let deps =
                      [ for k in 0 .. nNodes - 1 ->
                            let lower =
                                [ for j in 0 .. k - 1 do
                                      if draw 3 = 0 then
                                          yield string j ]

                            let back =
                                if shape = 1 && draw 2 = 0 then
                                    [ string (k + draw (nNodes - k)) ]
                                else
                                    []

                            let dangling = if shape = 2 && draw 4 = 0 then [ "zz" ] else []
                            string k, Set.ofList (lower @ back @ dangling) ]
                      |> Map.ofList

                  let topo = Propagation.sort deps

                  if not (List.isEmpty topo.Cycles) then
                      cyclic <- cyclic + 1

                  // production's sort, held to production's checker and to the extracted one, and
                  // four perturbations of it the checker must refuse wherever they differ from it
                  let candidates: (string * Propagation.TopoResult) list =
                      [ "sort", topo
                        "Order reversed",
                        { topo with
                            Order = List.rev topo.Order }
                        "an id twice",
                        { topo with
                            Order = topo.Order @ List.truncate 1 topo.Order }
                        "an id dropped",
                        { topo with
                            Order = List.skip (min 1 topo.Order.Length) topo.Order }
                        "a cycle walked as acyclic",
                        { Order = topo.Order @ List.concat topo.Cycles
                          Cycles = [] } ]

                  for label, cand in candidates do
                      let prod = Propagation.validTopo deps cand
                      let model = ModelProp.valid_topo (depsToModel deps) (topoToModel cand)

                      if prod <> model then
                          diffs <-
                              sprintf
                                  "trial %d %s: production validTopo=%b, model valid_topo=%b, deps=%A topo=%A"
                                  i
                                  label
                                  prod
                                  model
                                  (Map.toList deps)
                                  cand
                              :: diffs

                      if label = "sort" then
                          if prod then
                              sorted <- sorted + 1
                          else
                              diffs <-
                                  sprintf
                                      "trial %d: sort's own result FAILS the certificate: %A over %A"
                                      i
                                      cand
                                      (Map.toList deps)
                                  :: diffs
                      elif not prod then
                          refused <- refused + 1

              match diffs with
              | d :: _ -> failtestf "the certificate and production DISAGREE\n%s" d
              | [] -> ()

              Expect.equal sorted 300 "every generated graph's sort passed the certificate"
              Expect.isGreaterThan cyclic 20 "cyclic graphs were drawn, so the Cycles half was exercised"
              Expect.isGreaterThan refused 300 "the perturbed orders were refused, so the checker can lose"

          testCase
              "evalWith and evalFromWith are the extracted eval_with and eval_from_with, and evalFromWith agrees with evalWith — `evalfromwith_agrees`"
          <| fun _ ->
              let mutable rng = ConfRng.ofSeed 3080
              let mutable diffs = []
              let mutable agreed = 0
              let mutable handedPrior = 0

              let draw n =
                  let v, r' = ConfRng.intBelow n rng
                  rng <- r'
                  v

              for i in 1..200 do
                  let nNodes = draw 6 + 2
                  let ids = [ for k in 0 .. nNodes - 1 -> string k ]

                  let deps =
                      [ for k in 0 .. nNodes - 1 ->
                            string k,
                            Set.ofList
                                [ for j in 0 .. k - 1 do
                                      if draw 3 = 0 then
                                          yield string j ] ]
                      |> Map.ofList

                  let base0 = [ for id in ids -> id, draw 100 ] |> Map.ofList
                  let changed = [ for _ in 1 .. draw 3 + 1 -> string (draw nNodes) ] |> Set.ofList

                  let base1 =
                      (base0, changed)
                      ||> Set.fold (fun m c -> Map.add c (if draw 6 = 0 then failBase else Map.find c base0 + 1000) m)

                  // A prior-aware evaluator that consults its prior and answers as it would with none
                  // (the prior is reused only when it IS the value it computes): the class
                  // `prior_blind_along` admits, with the prior path taken.
                  let prodWith (baseOf: Map<string, int>) (seen: ResizeArray<string>) =
                      fun (resolve: string -> int option) (prior: int option) (id: string) ->
                          if Option.isSome prior then
                              seen.Add id

                          match propSpec baseOf deps resolve id, prior with
                          | Ok v, Some p when p = v -> Ok p
                          | r, _ -> r

                  let modelWith (baseOf: Map<string, int>) : ModelProp.evaluator_with<int> =
                      fun resolve prior id ->
                          match propSpec baseOf deps (fun k -> ofMOpt (resolve k)) id, ofMOpt prior with
                          | Ok v, Some p when p = v -> ModelProp.Ok p
                          | Ok v, _ -> ModelProp.Ok v
                          | Error m, _ -> ModelProp.Error m

                  let mdeps = depsToModel deps
                  let mtopo = topoToModel (Propagation.sort deps)
                  let mreads = declaredReads deps

                  let where =
                      sprintf "trial %d deps=%A changed=%A" i (Map.toList deps) (Set.toList changed)

                  let full0 = Propagation.evalWith (prodWith base0 (ResizeArray())) deps
                  let mfull0 = ModelProp.eval_with (modelWith base0) mreads mdeps mtopo

                  if prodOutcomeRender full0 <> modelOutcomeRender mfull0 then
                      diffs <-
                          sprintf
                              "%s: evalWith production=%s model=%s"
                              where
                              (prodOutcomeRender full0)
                              (modelOutcomeRender mfull0)
                          :: diffs

                  match full0 with
                  | Ok out0 ->
                      let seen = ResizeArray()
                      let incr = Propagation.evalFromWith (prodWith base1 seen) out0.Values changed deps

                      let mincr =
                          ModelProp.eval_from_with
                              (modelWith base1)
                              mreads
                              (Map.toList out0.Values)
                              (Set.toList changed)
                              mdeps
                              mtopo

                      let full1 = Propagation.evalWith (prodWith base1 (ResizeArray())) deps

                      if prodOutcomeRender incr <> modelOutcomeRender mincr then
                          diffs <-
                              sprintf
                                  "%s: evalFromWith production=%s model=%s"
                                  where
                                  (prodOutcomeRender incr)
                                  (modelOutcomeRender mincr)
                              :: diffs
                      elif incr <> full1 then
                          diffs <-
                              sprintf
                                  "%s: evalFromWith=%s but evalWith=%s"
                                  where
                                  (prodOutcomeRender incr)
                                  (prodOutcomeRender full1)
                              :: diffs
                      else
                          agreed <- agreed + 1

                      if seen.Count > 0 then
                          handedPrior <- handedPrior + 1
                  | Error _ -> ()

              match diffs with
              | d :: _ -> failtestf "the prior-aware drivers and the model DISAGREE\n%s" d
              | [] -> ()

              Expect.isGreaterThan agreed 100 "most trials primed and replayed"
              Expect.isGreaterThan handedPrior 50 "and a recomputed node was handed its prior, so the prior path ran"

          testCase
              "dirtyFromOp names a node whose declared reads the edit moved two levels below it — the node touchedBy does not reach (Phase 456)"
          <| fun _ ->
              // The counter-example Phase 456 fixed. A `sum` node reads every LEAF below it; an insert
              // two levels down moves its declared reads although no container of it is touched and
              // it reads nothing the op touched (the grown container is not a leaf). Seeded from
              // `touchedBy` alone, `dirtyFromOp` left it clean; seeded from the diff it names it, as
              // `changedForOp` has since Phase 308. Red with the seeds cut back to `touchedBy`.
              let subtreeReads (n: RNode) : string seq =
                  if n.Kind = "sum" then
                      Tree.preorder nodew n
                      |> List.tail
                      |> List.filter (fun d -> List.isEmpty d.Children)
                      |> List.map _.Id
                      |> Seq.ofList
                  else
                      Seq.empty

              let pre =
                  RNode.node
                      "root"
                      "section"
                      [ RNode.node "s" "sum" [ RNode.node "c" "section" [ RNode.leaf "a" "para" "" ] ] ]

              let op = InsertChild("c", RNode.leaf "z" "para" "")

              let touched = Propagation.touchedBy nodew idw pre op
              Expect.isFalse (Set.contains "s" touched) "touchedBy does not reach s"

              Expect.isFalse
                  (Set.contains
                      "s"
                      (Propagation.dirtyFromChangedIds (Propagation.dependencyMap nodew idw subtreeReads pre) touched))
                  "nor does its closure: s reads the leaf a, and the op touches c and z"

              let dirty = Propagation.dirtyFromOp nodew idw pre subtreeReads op
              Expect.isTrue (Set.contains "s" dirty) "dirtyFromOp names s, whose declared reads moved"

              match Ops.apply Policy.none (OpScope.plain nodew idw) op pre with
              | Error e -> failtestf "%A" e
              | Ok post ->
                  Expect.equal
                      (Propagation.changedForOp nodew idw subtreeReads pre post op)
                      dirty
                      "nothing was removed, so the change set is the whole dirty set"

              // An op `Ops.apply` refuses has no post-edit tree to diff: the closure is over `touchedBy`.
              let refused = MoveNode("root", "c")

              Expect.isError
                  (Ops.apply Policy.none (OpScope.plain nodew idw) refused pre)
                  "moving the root under its own descendant is refused"

              Expect.equal
                  (Propagation.dirtyFromOp nodew idw pre subtreeReads refused)
                  (Propagation.dirtyFromChangedIds
                      (Propagation.dependencyMap nodew idw subtreeReads pre)
                      (Propagation.touchedBy nodew idw pre refused))
                  "a refused op closes over touchedBy alone"

          testCase
              "changedForOp and dirtyFromOp name every survivor the edit moved and every reader of a removed id — dirtyFromOp the removed ids too, and changedForOp is its restriction to the survivors — over every op kind with nested Batch — `changed_for_op_complete`, `dirty_from_op_complete`"
          <| fun _ ->
              // The theorem's statement, checked of PRODUCTION over generated trees and ops: the set
              // `changed_for_op_complete` says the change set contains is computed here directly from
              // the two trees, and `evalFrom` from the survivors' prior is held to `eval` under an
              // evaluator that is a function of a node's own content, its child ids and its resolved
              // declared reads — the class the theorem's corollary is about.
              let mutable rng = ConfRng.ofSeed 30800
              let mutable diffs = []
              let mutable accepted = 0
              let mutable readerOfRemoved = 0
              let mutable readsMovedUnreached = 0
              let mutable nestedBatch = 0
              let kinds = System.Collections.Generic.HashSet<string>()

              let draw n =
                  let v, r' = ConfRng.intBelow n rng
                  rng <- r'
                  v

              // A `sum` node reads every leaf below it as well as its own value: the class of node
              // whose declared reads an edit below it moves, which `touchedBy` cannot reach (Phase 456).
              let reads (n: RNode) : string seq =
                  let own = if n.Value = "" then [] else [ n.Value ]

                  let below =
                      if n.Kind = "sum" then
                          Tree.preorder nodew n
                          |> List.tail
                          |> List.filter (fun d -> List.isEmpty d.Children)
                          |> List.map _.Id
                      else
                          []

                  Seq.ofList (own @ below)

              let readSet (n: RNode) = reads n |> Set.ofSeq

              let readValue () =
                  match draw 3 with
                  | 0 -> ""
                  | 1 -> "zz"
                  | _ -> sprintf "n%d" (draw 9)

              let genTree () =
                  let size = draw 8 + 2

                  let rec build (i: int) : RNode =
                      let kids =
                          [ for j in i + 1 .. size - 1 do
                                if (j - 1) / 2 = i then
                                    yield build j ]

                      let kind =
                          match draw 3 with
                          | 0 -> "section"
                          | 1 -> "para"
                          | _ -> "sum"

                      { RNode.node (sprintf "n%d" i) kind kids with
                          Value = readValue () }

                  build 0

              let rec genOp (t: RNode) (depth: int) : SkeletonOp<RNode, string> =
                  let ids = Tree.ids nodew t
                  let pick () = List.item (draw ids.Length) ids

                  match draw (if depth = 0 then 6 else 7) with
                  | 0 ->
                      InsertChild(
                          pick (),
                          { RNode.leaf (sprintf "f%d" (draw 1000)) "para" "" with
                              Value = readValue () }
                      )
                  | 1 -> RemoveNode(pick ())
                  | 2 -> MoveNode(pick (), pick ())
                  | 3 ->
                      let p = pick ()

                      match Tree.tryFind nodew idw p t with
                      | Some n ->
                          let kidIds = n.Children |> List.map _.Id
                          let order, r' = ConfRng.shuffle kidIds rng
                          rng <- r'
                          ReorderChildren(p, order)
                      | None -> ReorderChildren(p, [])
                  | 4 ->
                      match Tree.tryFind nodew idw (pick ()) t with
                      | Some n ->
                          UpdateNode
                              { n with
                                  Kind = (if draw 2 = 0 then n.Kind else "rewritten")
                                  Value = readValue () }
                      | None -> Batch []
                  | _ ->
                      // a Batch drawn against the same pre-state, one level of it NESTED
                      Batch [ for _ in 0 .. draw 3 -> genOp t (depth + 1) ]

              let rec kindOf (op: SkeletonOp<RNode, string>) =
                  match op with
                  | InsertChild _ -> "insert"
                  | RemoveNode _ -> "remove"
                  | MoveNode _ -> "move"
                  | ReorderChildren _ -> "reorder"
                  | UpdateNode _ -> "update"
                  | Batch ops when ops |> List.exists _.IsBatch -> "nested batch"
                  | Batch _ -> "batch"

              let rec noteKinds (op: SkeletonOp<RNode, string>) =
                  kinds.Add(kindOf op) |> ignore

                  match op with
                  | Batch ops -> List.iter noteKinds ops
                  | _ -> ()

              let eval (t: RNode) (resolve: string -> int option) (id: string) : Result<int, string> =
                  match Tree.tryFind nodew idw id t with
                  | None -> Error("no node " + id)
                  | Some n ->
                      let own = n.Kind.Length + 10 * List.length n.Children

                      let read = reads n |> Seq.sumBy (fun r -> resolve r |> Option.defaultValue 7)

                      Ok(own + 100 * read % 9973)

              for i in 1..1500 do
                  let pre = genTree ()
                  let op = genOp pre 0

                  match Ops.apply Policy.none (OpScope.plain nodew idw) op pre with
                  | Error _ -> ()
                  | Ok post ->
                      accepted <- accepted + 1
                      noteKinds op

                      if kindOf op = "nested batch" then
                          nestedBatch <- nestedBatch + 1

                      let changed = Propagation.changedForOp nodew idw reads pre post op
                      let dirty = Propagation.dirtyFromOp nodew idw pre reads op
                      let survivors = Tree.ids nodew post |> Set.ofList

                      // Phase 456: the change set is the dirty set restricted to the survivors
                      // (`changed_for_op_is_dirty_from_op`).
                      if changed <> Set.intersect dirty survivors then
                          diffs <-
                              sprintf
                                  "trial %d: changedForOp=%A but dirtyFromOp=%A over survivors %A for %A over %A"
                                  i
                                  (Set.toList changed)
                                  (Set.toList dirty)
                                  (Set.toList survivors)
                                  op
                                  pre
                              :: diffs

                      let index t =
                          Tree.preorder nodew t |> List.map (fun n -> n.Id, n) |> Map.ofList

                      let before = index pre
                      let after = index post
                      let shell (n: RNode) = { n with Children = [] }
                      let kidIds (n: RNode) = n.Children |> List.map _.Id
                      let removedIds = before |> Map.filter (fun k _ -> not (after.ContainsKey k))

                      // What `touchedBy` alone closes to — the pre-Phase-456 `dirtyFromOp`, kept here
                      // so the case can say it REACHED the gap the diff seeds close.
                      let touchedClosure =
                          Propagation.dirtyFromChangedIds
                              (Propagation.dependencyMap nodew idw reads pre)
                              (Propagation.touchedBy nodew idw pre op)

                      // `dirty_from_op_complete`'s other two clauses: every removed id, and every
                      // pre-edit node that read one (surviving or not), is in the dirty set.
                      for KeyValue(x, m) in before do
                          let removed = removedIds.ContainsKey x
                          let readsRemoved = reads m |> Seq.exists removedIds.ContainsKey

                          if (removed || readsRemoved) && not (Set.contains x dirty) then
                              diffs <-
                                  sprintf
                                      "trial %d: %s %s but dirtyFromOp gave %A for %A over %A"
                                      i
                                      x
                                      (if removed then "was removed" else "read a removed id")
                                      (Set.toList dirty)
                                      op
                                      pre
                                  :: diffs

                      for KeyValue(x, n) in after do
                          let moved =
                              match before.TryFind x with
                              | None -> true
                              | Some m -> shell m <> shell n || kidIds m <> kidIds n || readSet m <> readSet n

                          let readsRemoved =
                              match before.TryFind x with
                              | Some m -> reads m |> Seq.exists removedIds.ContainsKey
                              | None -> false

                          if moved && not (Set.contains x touchedClosure) then
                              readsMovedUnreached <- readsMovedUnreached + 1

                          if readsRemoved then
                              readerOfRemoved <- readerOfRemoved + 1

                          if (moved || readsRemoved) && not (Set.contains x changed) then
                              diffs <-
                                  sprintf
                                      "trial %d: %s %s but changedForOp gave %A for %A over %A"
                                      i
                                      x
                                      (if moved then "moved" else "read a removed id")
                                      (Set.toList changed)
                                      op
                                      pre
                                  :: diffs

                      let deps0 = Propagation.dependencyMap nodew idw reads pre
                      let deps1 = Propagation.dependencyMap nodew idw reads post

                      match Propagation.eval (eval pre) deps0 with
                      | Ok out0 ->
                          let prior = out0.Values |> Map.filter (fun k _ -> deps1.ContainsKey k)
                          let incr = Propagation.evalFrom (eval post) prior changed deps1
                          let full = Propagation.eval (eval post) deps1

                          if incr <> full then
                              diffs <-
                                  sprintf "trial %d: evalFrom=%A but eval=%A for %A over %A" i incr full op pre
                                  :: diffs
                      | Error e -> diffs <- sprintf "trial %d: the pre-edit tree did not evaluate: %A" i e :: diffs

              match diffs with
              | d :: _ -> failtestf "changedForOp or dirtyFromOp missed what the theorems say they name\n%s" d
              | [] -> ()

              Expect.equal
                  (Set.ofSeq kinds)
                  (Set.ofList [ "insert"; "remove"; "move"; "reorder"; "update"; "batch"; "nested batch" ])
                  "every op kind, a nested Batch included, was drawn and accepted"

              Expect.isGreaterThan accepted 500 "most trials applied"
              Expect.isGreaterThan readerOfRemoved 20 "a survivor read a removed id, so that clause was reached"

              Expect.isGreaterThan
                  readsMovedUnreached
                  10
                  "a survivor's declared reads moved with no container of it touched, so the gap the diff seeds close was reached"

              Expect.isGreaterThan nestedBatch 5 "nested batches were accepted" ]
