namespace Fuaran.Core.Tests

// ============================================================================
//  The propagation law set (Phase 538): `laws/propagation-laws.json`, the
//  vectors a host replays to certify a reimplementation of `Fuaran.Core.Propagation`
//  — the dirty set a write produces, the evaluation order, the incremental
//  re-evaluation, and the refusal of an undeclared read — without running F#.
//
//  Phase 235 settled that every law set Core is the reference for is emitted by
//  Core's own `render laws`; this is the propagation one, written beside the
//  other three by `LawVectorExport.emitted`. The sample is DRAWN by the very
//  generator the two driver families run (`PropagationLaws.drawDag`, over a
//  `LawKit.Draws` at the declared seed), so the vectors are the cases
//  `propagationEvalLaws seed iterations` checks, and every `expected` is computed
//  by CALLING the kit, never restated. The tests at the foot hold each vector to
//  the law it exports.
// ============================================================================

module PropagationVectorExport =

    open Fuaran.Core

    let fileName = "propagation-laws.json"

    /// Declared rather than reused from a law invocation, because a host re-running the family must
    /// reproduce the sample from the file alone.
    let seed = 20261019

    /// Twelve graphs: enough to reach dirty and clean nodes, readers and leaves, at every graph size
    /// the generator draws, and few enough to read.
    let iterations = 12

    /// The two ids an undeclared read reaches for that the drawn graphs do not hold, named once: the
    /// law's own.
    let absentRead = "no-such-node"
    let unknownChange = "no-such-id"

    /// The sample: the `iterations` cases `propagationEvalLaws` draws from `seed`.
    let internal draws () : PropagationLaws.DagDraw list =
        let rng = LawKit.Draws seed
        [ for _ in 1..iterations -> PropagationLaws.drawDag rng ]

    /// The toy pull evaluator the families run, `value(n) = base(n) + Σ value(reads)`, a read that
    /// resolves to nothing counting `0`; `leak` makes node `fst leak` also read `snd leak`, which it
    /// never declared. Records every node it is invoked on.
    let evaluator
        (baseOf: Map<string, int>)
        (deps: Map<string, Set<string>>)
        (leak: (string * string) option)
        (invoked: ResizeArray<string>)
        =
        fun (resolve: string -> int option) (id: string) ->
            invoked.Add id
            let read r = resolve r |> Option.defaultValue 0
            let declared = Map.find id deps |> Set.fold (fun s r -> s + read r) 0

            let leaked =
                match leak with
                | Some(at, r) when at = id -> read r
                | _ -> 0

            Ok(Map.find id baseOf + declared + leaked)

    /// The prior every incremental case starts from: `eval` of the unedited bases.
    let internal prior (d: PropagationLaws.DagDraw) : Map<string, int> =
        match Propagation.eval (evaluator d.Base0 d.Deps None (ResizeArray())) d.Deps with
        | Ok o -> o.Values
        | Error e -> failwithf "the prior evaluation of a drawn DAG failed: %A" e

    // -----------------------------------------------------------------------
    //  cells
    // -----------------------------------------------------------------------

    let private str = CorpusFile.str

    let private ids (xs: string seq) =
        xs |> Seq.map str |> List.ofSeq |> CorpusCell.List

    let private depsCell (deps: Map<string, Set<string>>) =
        deps |> Map.toList |> List.map (fun (k, rs) -> k, ids rs) |> CorpusCell.Row

    let private valuesCell (m: Map<string, int>) =
        m
        |> Map.toList
        |> List.map (fun (k, v) -> k, CorpusFile.num v)
        |> CorpusCell.Row

    /// A driver's answer in host-neutral words. `expected` members, in order.
    let private outcome
        (r: Result<Propagation.EvalOutcome<int>, Propagation.PropagationError>)
        : (string * CorpusCell) list =
        match r with
        | Ok o ->
            [ "verdict", str "accept"
              "values", valuesCell o.Values
              "cycles", o.Cyclic |> List.map (List.sort >> ids) |> CorpusCell.List ]
        | Error(Propagation.PropagationError.EvalUndeclaredRead(node, read)) ->
            [ "verdict", str "reject"
              "error", str "evalUndeclaredRead"
              "node", str node
              "read", str read ]
        | Error(Propagation.PropagationError.EvalUnknownChange unknown) ->
            [ "verdict", str "reject"
              "error", str "evalUnknownChange"
              "ids", ids unknown ]
        | Error(Propagation.PropagationError.EvalNodeFailed(node, _)) ->
            [ "verdict", str "reject"; "error", str "evalNodeFailed"; "node", str node ]

    /// One vector: the reference's `expected` members and the extracted model's answer to the same
    /// input (Phase 478), rendered by the same cell builders; the stamp is `model:Propagation` when they
    /// agree, and the export fails when they do not.
    let private vector (id: string) (case: string) input expected (model: (string * CorpusCell) list) : CorpusCell =
        let answer: VectorOracle.Answer =
            { Model = VectorOracle.PropagationModel.Model
              LadderRow = VectorOracle.PropagationModel.LadderRow
              Cell = CorpusCell.Row model }

        CorpusCell.Row
            [ "id", str id
              "case", str case
              "input", CorpusCell.Row input
              "expected", CorpusCell.Row expected
              VectorOracle.stamp id (CorpusCell.Row expected) (Some answer) ]

    /// Two drivers' shared answer, or `driversDisagree` — which the tests below fail on, so it can
    /// never reach the corpus.
    let private agreed viaEval viaEvalFrom : (string * CorpusCell) list =
        if viaEval = viaEvalFrom then
            outcome viaEval
        else
            [ "verdict", str "driversDisagree" ]

    /// Both drivers' answer to one evaluator.
    let private bothDrivers
        (ev: (string -> int option) -> string -> Result<int, string>)
        (d: PropagationLaws.DagDraw)
        (changed: string)
        : (string * CorpusCell) list =
        agreed (Propagation.eval ev d.Deps) (Propagation.evalFrom ev (prior d) (Set.singleton changed) d.Deps)

    /// The read witness the model takes for `evaluator`: what each node declares, and the leaked read
    /// at the leaking node — derived from the evaluator's own inputs, never guessed.
    let private touches (deps: Map<string, Set<string>>) (leak: (string * string) option) (id: string) =
        let declared =
            Map.tryFind id deps |> Option.map Set.toList |> Option.defaultValue []

        match leak with
        | Some(at, r) when at = id -> declared @ [ r ]
        | _ -> declared

    /// A fresh evaluator per model call: the model invokes it on its own walk.
    let private modelEv baseOf deps leak =
        evaluator baseOf deps leak (ResizeArray())

    /// The model's prior: its own `eval` of the unedited bases.
    let private modelPrior (d: PropagationLaws.DagDraw) : Map<string, int> =
        match VectorOracle.PropagationModel.eval (modelEv d.Base0 d.Deps None) (touches d.Deps None) d.Deps with
        | Ok o -> o.Values
        | Error e -> failwithf "the model's prior evaluation of a drawn DAG failed: %A" e

    let private topoCells (topo: Propagation.TopoResult) =
        [ "order", ids topo.Order
          "cycles", topo.Cycles |> List.map (List.sort >> ids) |> CorpusCell.List ]

    /// Every vector for one drawn graph.
    let internal vectorsFor (i: int) (d: PropagationLaws.DagDraw) : CorpusCell list =
        let topo = Propagation.sort d.Deps
        let invoked = ResizeArray()
        let changedSet = Set.singleton d.ChangedId

        let incremental =
            Propagation.evalFrom (evaluator d.Base1 d.Deps None invoked) (prior d) changedSet d.Deps

        let modelFrom leak baseOf changed =
            VectorOracle.PropagationModel.evalFrom
                (modelEv baseOf d.Deps leak)
                (touches d.Deps leak)
                (modelPrior d)
                changed
                d.Deps

        let modelInvoked =
            VectorOracle.PropagationModel.invoked
                (modelEv d.Base1 d.Deps None)
                (touches d.Deps None)
                (modelPrior d)
                changedSet
                d.Deps

        let name suffix = sprintf "propagation-%d-%s" i suffix
        let changed = [ "changed", ids [ d.ChangedId ] ]

        [ vector
              (name "dirty-set")
              "dirtySet"
              ([ "deps", depsCell d.Deps ] @ changed)
              [ "dirty", ids (Propagation.dirtyFromChangedIds d.Deps changedSet) ]
              [ "dirty", ids (VectorOracle.PropagationModel.dirty d.Deps changedSet) ]
          vector
              (name "evaluation-order")
              "evaluationOrder"
              [ "deps", depsCell d.Deps ]
              (topoCells topo)
              (topoCells (VectorOracle.PropagationModel.sort d.Deps))
          vector
              (name "incremental")
              "evalFrom"
              ([ "deps", depsCell d.Deps; "base", valuesCell d.Base0 ]
               @ changed
               @ [ "changedBase", valuesCell d.Base1 ])
              (outcome incremental @ [ "recomputed", ids (Set.ofSeq invoked) ])
              (outcome (modelFrom None d.Base1 changedSet) @ [ "recomputed", ids modelInvoked ])
          vector
              (name "unknown-change")
              "evalFrom"
              [ "deps", depsCell d.Deps
                "base", valuesCell d.Base0
                "changed", ids [ unknownChange ]
                "changedBase", valuesCell d.Base1 ]
              (outcome (
                  Propagation.evalFrom
                      (evaluator d.Base1 d.Deps None (ResizeArray()))
                      (prior d)
                      (Set.singleton unknownChange)
                      d.Deps
              ))
              (outcome (modelFrom None d.Base1 (Set.singleton unknownChange)))
          // Node "0" declares no reads on every graph the generator draws, so a read of node "1" is
          // a real node read undeclared; the second reaches an id the map does not hold.
          for leakAt, read in [ "0", "1"; d.ChangedId, absentRead ] do
              let leak = Some(leakAt, read)

              vector
                  (name (
                      if read = absentRead then
                          "undeclared-absent-id"
                      else
                          "undeclared-real-node"
                  ))
                  "undeclaredRead"
                  [ "deps", depsCell d.Deps
                    "base", valuesCell d.Base1
                    "node", str leakAt
                    "read", str read ]
                  (bothDrivers (evaluator d.Base1 d.Deps leak (ResizeArray())) d leakAt)
                  (agreed
                      (VectorOracle.PropagationModel.eval (modelEv d.Base1 d.Deps leak) (touches d.Deps leak) d.Deps)
                      (modelFrom leak d.Base1 (Set.singleton leakAt))) ]

    /// The fixed cyclic graph of `dirtyPropagationLaws`: a 3-cycle and a reader of it. A cycle is data.
    let cyclic: Map<string, Set<string>> =
        Map.ofList
            [ "a", Set.singleton "b"
              "b", Set.singleton "c"
              "c", Set.singleton "a"
              "x", Set.singleton "a" ]

    let private cycleVector () : CorpusCell =
        let topo = Propagation.sort cyclic

        vector
            "propagation-cycle-evaluation-order"
            "evaluationOrder"
            [ "deps", depsCell cyclic ]
            (topoCells topo)
            (topoCells (VectorOracle.PropagationModel.sort cyclic))

    let internal allVectors () : CorpusCell list =
        (draws () |> List.mapi vectorsFor |> List.concat) @ [ cycleVector () ]

    let private description =
        "The (input, expected) pairs Fuaran.Core.Conformance.propagationEvalLaws draws from `seed` over `iterations` "
        + "iterations (and dirtyPropagationLaws' fixed cyclic graph), computed by calling the pinned kit. A host "
        + "reproduces the sample with its own ConfRng, one cursor across all iterations: per iteration draw "
        + "intBelow(6) + 2 = n, the ids `0` .. `n-1`; for each node k ascending, intBelow(3) for each j < k ascending, "
        + "k reading j where it is 0; intBelow(100) per node ascending, its base; intBelow(n), the changed node, whose "
        + "base grows by 1000. Every input carries the graph as `deps` (each id to the ids it reads), so the vectors "
        + "also replay without the draw. The evaluator is value(n) = base(n) + the sum of value(r) over n's declared "
        + "reads r, a read that resolves to nothing counting 0. `dirtySet` expects the changed ids and every node "
        + "that transitively reads one, sorted. `evaluationOrder` expects the order the reference walks — Tarjan "
        + "over the ids in ascending ordinal order, each node's reads ascending, dependencies first — and the "
        + "cyclic groups, each sorted; a host whose sort breaks ties differently instead holds its own order to "
        + "the certificate: no id twice, order and cycles partitioning the ids, every acyclic read earlier. "
        + "`evalFrom` re-evaluates `changedBase` incrementally over the prior `eval` of `base`: it expects the "
        + "values, which equal a full evaluation of `changedBase`, and `recomputed`, the nodes the evaluator was "
        + "invoked on, which is exactly the dirty set — or, for a changed id the graph does not hold, the "
        + "evalUnknownChange refusal naming it. `undeclaredRead` makes `node` also read `read`, which it never "
        + "declared: both eval and evalFrom (with `node` changed) refuse with evalUndeclaredRead naming the node "
        + "and the read, whether the read names a node of the graph or an id it does not hold. Every vector's "
        + "`answeredBy` is [\"reference\", \"model:Propagation\"]: the export also ran the extracted F* model "
        + "(proofs/Propagation.fst) on the input and found the same answer."

    /// Rendered with an explicit `kitVersion`, as its siblings in `LawVectorExport` are.
    let renderAt (stamp: string) : string =
        CorpusFile.render
            { Head =
                [ "family", str "propagationLaws"
                  "kitVersion", str stamp
                  "seed", CorpusFile.num seed
                  "iterations", CorpusFile.num iterations
                  "description", str description ]
              Items = "vectors"
              Rows = allVectors () }

    // -----------------------------------------------------------------------
    //  the vectors are the law's
    // -----------------------------------------------------------------------

    /// The dirty set by an independent oracle, computed differently from `dirtyFromChangedIds`' inverted
    /// walk: a node is dirty when the changed node is reachable along its own reads.
    let private readsReach (deps: Map<string, Set<string>>) (start: string) (target: string) : bool =
        let rec go (seen: Set<string>) (front: string list) =
            match front with
            | [] -> false
            | n :: _ when n = target -> true
            | n :: rest ->
                let next = Map.find n deps |> Set.filter (fun r -> not (Set.contains r seen))
                go (Set.union seen next) (List.ofSeq next @ rest)

        go (Set.singleton start) [ start ]

    /// One finding per way a drawn case's computed answers disagree with the laws they export.
    let internal findings (i: int) (d: PropagationLaws.DagDraw) : string list =
        let at fmt =
            Printf.ksprintf (sprintf "iteration %d: %s" i) fmt

        let dirty = Propagation.dirtyFromChangedIds d.Deps (Set.singleton d.ChangedId)

        let oracle =
            d.Ids |> List.filter (fun n -> readsReach d.Deps n d.ChangedId) |> Set.ofList

        let invoked = ResizeArray()

        let incremental =
            Propagation.evalFrom (evaluator d.Base1 d.Deps None invoked) (prior d) (Set.singleton d.ChangedId) d.Deps

        let full = Propagation.eval (evaluator d.Base1 d.Deps None (ResizeArray())) d.Deps

        let refusal leakAt read =
            let ev = evaluator d.Base1 d.Deps (Some(leakAt, read)) (ResizeArray())
            let expected = Error(Propagation.PropagationError.EvalUndeclaredRead(leakAt, read))

            [ if Propagation.eval ev d.Deps <> expected then
                  yield at "eval did not refuse %s reading undeclared %s" leakAt read
              if Propagation.evalFrom ev (prior d) (Set.singleton leakAt) d.Deps <> expected then
                  yield at "evalFrom did not refuse %s reading undeclared %s" leakAt read ]

        [ if dirty <> oracle then
              yield at "dirty %A is not the reads-reachability closure %A" dirty oracle
          if incremental <> full then
              yield at "evalFrom is not a full eval over the changed bases"
          if Set.ofSeq invoked <> dirty then
              yield at "evalFrom recomputed %A, not the dirty set %A" (Set.ofSeq invoked) dirty
          if not (Propagation.validTopo d.Deps (Propagation.sort d.Deps)) then
              yield at "the order is not a valid evaluation order"
          match
              Propagation.evalFrom
                  (evaluator d.Base1 d.Deps None (ResizeArray()))
                  (prior d)
                  (Set.singleton unknownChange)
                  d.Deps
          with
          | Error(Propagation.PropagationError.EvalUnknownChange [ u ]) when u = unknownChange -> ()
          | other -> yield at "an unknown change was answered %A" other
          yield! refusal "0" "1"
          yield! refusal d.ChangedId absentRead ]
