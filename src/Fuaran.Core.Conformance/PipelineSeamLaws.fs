namespace Fuaran.Core

/// The `Deferred` envelope (Phase 32) and the serializable capability pipeline, whole (Phase 35) and
/// incremental (Phase 62) — `SeamLaws` until the Phase 388 split along its banners.
module internal PipelineSeamLaws =
    open RegistrySeamLaws

    // ---- Deferred async-result envelope (Phase 32) ----
    // The teeth on `Deferred<'T>` + its wire codec + its Phase-27 replay interplay.

    /// The `Deferred` laws (Phase 32). Self-contained — over a seed-replayable sample it certifies:
    ///
    ///  - **wire round-trip** — `Pending` / `Ready v` / `Failed m` each `encodeDeferred`→`decodeDeferred`
    ///    back to themselves (an `int` payload);
    ///  - **combinators** — `map` lifts over `Ready` and propagates `Pending`/`Failed`; `toResult`
    ///    projects `Ready`→`Ok`, `Failed`→`Error`;
    ///  - **replay interplay** — a `Ready` value (the realized result) journals through the Phase 27
    ///    capture seam and replays **byte-identically** even when the live source would now differ.
    let deferredLaws (seed: int) (iterations: int) : LawResult list =
        let roundtrip =
            LawKit.LawCell "Deferred round-trips the wire for Pending / Ready / Failed"

        let combinators =
            LawKit.LawCell "Deferred map / toResult behave (Ready lifts; Pending/Failed propagate)"

        let replay =
            LawKit.LawCell "a Ready value replays byte-identically via the Phase 27 capture seam"

        let encInt (n: int) : JVal = JInt n

        let decInt =
            function
            | JInt i -> Ok i
            | _ -> Error "not int"

        let encV (n: int) : string = string n

        let decV (s: string) : Result<int, string> =
            match System.Int32.TryParse s with
            | true, v -> Ok v
            | _ -> Error "nan"

        let hashFn = OpStream.defaultHash

        LawKit.run iterations seed (fun rng i at ->
            let v = rng.IntBelow 1000

            let cases = [ Pending; Ready v; Failed("err" + string v) ]

            for d in cases do
                match CapabilityCodec.decodeDeferred decInt (CapabilityCodec.encodeDeferred encInt d) with
                | Ok d2 -> roundtrip.Check((d2 = d), fun () -> at (sprintf "Deferred ≠ round-trip (%A)" d))
                | Error m -> roundtrip.Check(false, fun () -> at (sprintf "Deferred decode failed: %s" m))

            let mapped = Deferred.map ((+) 1) (Ready v)
            let pendingMapped = Deferred.map ((+) 1) Pending

            combinators.Check(
                (mapped = Ready(v + 1)
                 && pendingMapped = Pending
                 && Deferred.toResult (Ready v) = Ok v
                 && Deferred.toResult (Failed "x") = Error "x"),
                fun () -> at "Deferred combinators disagree"
            )

            // a Ready value replays byte-identically through the Phase 27 seam.
            let key = "deferred#" + string i
            let _, caps = OpStream.captureEffect hashFn encV "network" key (fun () -> v) []
            let liveDifferent () = v + 1

            match OpStream.replayEffect decV key "network" liveDifferent caps with
            | Ok(rv, rest) ->
                replay.Check(
                    (rv = v && List.isEmpty rest),
                    fun () -> at (sprintf "Ready replay ≠ recorded (%d vs %d)" rv v)
                )
            | Error m -> replay.Check(false, fun () -> at (sprintf "Ready replay errored: %s" m)))

        LawKit.results [ roundtrip; combinators; replay ]

    // ---- serializable capability pipeline (Phase 35) ----
    // The teeth on `CapabilityPipeline`: type-checked composition (ill-typed edge ⇒ named error), a
    // canonical wire round-trip, and per-node byte-identical replay through the Phase-27 capture seam.

    /// The capability-pipeline laws (Phase 35). Self-contained (builds a `prod → cons` 2-node pipeline
    /// from a fixed registry); over a seed-replayable sample it certifies: **type-checked composition**
    /// (a well-typed pipeline passes; an `int`-arg fed a `string` producer is a named `EdgeTypeMismatch`);
    /// **wire round-trip** (`encode`→`decode` is identity); and **per-node replay** (a node's realized
    /// value, journalled under `nodeInvocationKey`, replays byte-identically via the Phase-27 seam).
    let capabilityPipelineLaws (seed: int) (iterations: int) : LawResult list =
        let typecheck =
            LawKit.LawCell
                "pipeline type-check accepts a well-typed DAG + names an ill-typed edge (PipelineError.EdgeTypeMismatch)"

        let roundtrip = LawKit.LawCell "a capability pipeline round-trips the wire"

        let replay =
            LawKit.LawCell "a pipeline node replays byte-identically via the Phase 27 capture seam"

        let relation =
            LawKit.LawCell
                "an edge type-checks exactly when the space relation says the producer's output fits the argument (Space.subsumes)"

        let sound =
            LawKit.LawCell "the space relation is sound: a value of the sub-space validates in the super-space"

        let ordered =
            LawKit.LawCell
                "a self-edge and a cycle are refused PipelineError.PipelineCycle and a forward edge PipelineError.PipelineForwardEdge, by name"

        let checkedFirst =
            LawKit.LawCell "an ill-typed pipeline is refused PipelineEvalError.EvalIllTyped before any body runs"

        let encV (n: int) : string = string n

        let decV (s: string) : Result<int, string> =
            match System.Int32.TryParse s with
            | true, v -> Ok v
            | _ -> Error "nan"

        let hashFn = OpStream.defaultHash

        let prodSig: Signature =
            { Name = "prod"
              Holes = []
              Effect =
                { Host = ReadsHost
                  Determinism = Effect.random } }

        let consHole: SigEntry =
            { Addr = "x"
              Name = "x"
              Kind = "value"
              Space = Some(IntRange(0, 100))
              Slot = None
              Action = None
              Required = true }

        let consSig: Signature =
            { Name = "cons"
              Holes = [ consHole ]
              Effect = Effect.pureDeterministic }

        let regResult =
            CapabilityRegistry.empty
            |> CapabilityRegistry.register (Capability.create "prod" prodSig (ClientIsland Pyodide))
            |> Result.bind (CapabilityRegistry.register (Capability.create "cons" consSig Server))

        match regResult with
        | Error e ->
            let built = LawKit.LawCell "capability pipeline registry built"
            built.Check(false, fun () -> sprintf "%A" e)
            LawKit.results [ built ]
        | Ok registry ->
            let reg = CapabilityLookup.ofRegistry registry

            let good =
                { Nodes =
                    [ Invoke("n1", "prod", IntRange(0, 100), [])
                      Invoke("n2", "cons", IntRange(0, 100), [ "x", FromNode "n1" ]) ] }

            // ill-typed: n1 declares a string output feeding cons's int arg "x"
            let bad =
                { Nodes =
                    [ Invoke("n1", "prod", AnyString, [])
                      Invoke("n2", "cons", IntRange(0, 100), [ "x", FromNode "n1" ]) ] }

            LawKit.run iterations seed (fun rng _ at ->
                let v = rng.IntBelow 100

                (match CapabilityPipeline.typeCheck reg good, CapabilityPipeline.typeCheck reg bad with
                 | Ok(), Error(PipelineError.EdgeTypeMismatch _) -> typecheck.Saw()
                 | g, b -> typecheck.Check(false, fun () -> at (sprintf "type-check disagreed (good=%A bad=%A)" g b)))

                (match CapabilityPipeline.decode (CapabilityPipeline.encode good) with
                 | Ok p2 -> roundtrip.Check((p2 = good), fun () -> at "pipeline ≠ round-trip")
                 | Error m -> roundtrip.Check(false, fun () -> at (sprintf "pipeline decode failed: %s" m)))

                // ---- THE space relation at the edge, and its soundness (Phase 295) ----
                let output = drawSpace rng
                let argSpace = drawSpace rng

                let argCap =
                    Capability.create
                        "arg"
                        { Name = "arg"
                          Holes =
                            [ { consHole with
                                  Space = Some argSpace
                                  Kind =
                                      (match argSpace with
                                       | SlotTree _ -> "slot"
                                       | _ -> "value")
                                  Slot =
                                      (match argSpace with
                                       | SlotTree c -> c
                                       | _ -> None) } ]
                          Effect = Effect.pureDeterministic }
                        Server

                let edge =
                    { Nodes =
                        [ Source("s", "ref", output)
                          Invoke("n", "arg", AnyString, [ "x", FromNode "s" ]) ] }

                let lookup =
                    { reg with
                        TryFind = fun id -> if id = "arg" then Some argCap else reg.TryFind id }

                (match CapabilityPipeline.typeCheck lookup edge with
                 | Ok() when Space.subsumes argSpace output -> relation.Saw()
                 | Error(PipelineError.EdgeTypeMismatch _) when not (Space.subsumes argSpace output) -> relation.Saw()
                 | other ->
                     relation.Check(
                         false,
                         fun () ->
                             at (
                                 sprintf
                                     "the edge check (%A) is not Space.subsumes (%A ⊇ %A = %b)"
                                     other
                                     argSpace
                                     output
                                     (Space.subsumes argSpace output)
                             )
                     ))

                if Space.subsumes argSpace output then
                    for v in candidates output do
                        if Space.validate output v then
                            sound.Check(
                                Space.validate argSpace v,
                                fun () ->
                                    at (sprintf "'%s' is in %A but not in %A, which subsumes it" v output argSpace)
                            )

                // ---- declaration order, built (Phase 295) ----
                let selfEdge =
                    { Nodes = [ Invoke("n1", "cons", IntRange(0, 100), [ "x", FromNode "n1" ]) ] }

                let cycle =
                    { Nodes =
                        [ Invoke("n1", "cons", IntRange(0, 100), [ "x", FromNode "n2" ])
                          Invoke("n2", "cons", IntRange(0, 100), [ "x", FromNode "n1" ]) ] }

                let forward =
                    { Nodes =
                        [ Invoke("n2", "cons", IntRange(0, 100), [ "x", FromNode "n1" ])
                          Invoke("n1", "prod", IntRange(0, 100), []) ] }

                (match
                    CapabilityPipeline.typeCheck reg selfEdge,
                    CapabilityPipeline.typeCheck reg cycle,
                    CapabilityPipeline.typeCheck reg forward
                 with
                 | Error(PipelineError.PipelineCycle("n1", [ "n1" ])),
                   Error(PipelineError.PipelineCycle("n1", [ "n1"; "n2" ])),
                   Error(PipelineError.PipelineForwardEdge("n2", "x", "n1")) -> ordered.Saw()
                 | a, b, c ->
                     ordered.Check(false, fun () -> at (sprintf "order refusals: self=%A cycle=%A forward=%A" a b c)))

                let ran = ref false

                (match
                    CapabilityPipeline.eval
                        reg
                        string
                        (fun _ _ ->
                            ran.Value <- true
                            Ok v)
                        bad
                 with
                 | Error(PipelineEvalError.EvalIllTyped(PipelineError.EdgeTypeMismatch _)) when not ran.Value ->
                     checkedFirst.Saw()
                 | other ->
                     checkedFirst.Check(
                         false,
                         fun () -> at (sprintf "an ill-typed pipeline evaluated (%A; a body ran: %b)" other ran.Value)
                     ))

                // per-node replay byte-identity through the Phase 27 seam
                let key = CapabilityPipeline.nodeInvocationKey (List.head good.Nodes)
                let _, caps = OpStream.captureEffect hashFn encV "random" key (fun () -> v) []

                match OpStream.replayEffect decV key "random" (fun () -> v + 1) caps with
                | Ok(rv, rest) ->
                    replay.Check(
                        (rv = v && List.isEmpty rest),
                        fun () -> at (sprintf "node replay ≠ recorded (%d vs %d)" rv v)
                    )
                | Error m -> replay.Check(false, fun () -> at (sprintf "node replay errored: %s" m)))

            LawKit.results [ typecheck; roundtrip; replay; relation; sound; ordered; checkedFirst ]

    /// The capability-pipeline laws at a DOMAIN'S pipelines and registry (Phase 246).
    /// `capabilityPipelineLaws` beside it builds a fixed two-node pipeline over a fixed registry and
    /// certifies Core's type-checker and codec there; this form runs the domain's own
    /// `CapabilityPipelineWitness` and certifies, for every pipeline its generator draws:
    ///
    ///  - **composition** — `CapabilityPipeline.typeCheck` accepts it against the domain's registry
    ///    (a pipeline the domain builds that does not compose at its own registry is the defect);
    ///  - **wire round-trip** — `decode (encode p) = Ok p`;
    ///  - **node keys** — no two of its nodes share a `nodeInvocationKey`, so the per-node capture
    ///    journal the Phase-27 seam keeps cannot hand one node another's recorded value;
    ///  - **default deny, built** — for every `Invoke` node, the pipeline with that node naming a
    ///    capability the registry does not hold is refused `PipelineNoSuchCapability`, and the
    ///    pipeline with that node binding an argument no hole declares is refused
    ///    `PipelineArgRefused` wrapping `UnknownArg` (Phase 295). Both are BUILT from the drawn
    ///    pipeline, never drawn.
    ///
    /// `deferredLaws` has no witness-taking form and needs none: it is over the `Deferred` envelope
    /// alone, which no domain supplies.
    ///
    /// **Vacuity.** The default-deny arms are built per `Invoke` node, so a generator whose pipelines
    /// carry none builds nothing; the guard counts the `Invoke` nodes reached.
    ///
    /// `family` labels the guard — `Conformance.capabilityPipelineLawsAt`, or the obsolete `…With`
    /// forward's own id for one draft.
    let capabilityPipelineLawsAt
        (family: string)
        (w: CapabilityPipelineWitness)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let composes =
            LawKit.LawCell "every pipeline the domain builds type-checks against its own registry"

        let roundtrip = LawKit.LawCell "a domain pipeline round-trips the wire"

        let keys =
            LawKit.LawCell "no two nodes of a domain pipeline share an invocation key"

        let deny =
            LawKit.LawCell(
                "default deny at the domain's pipelines (an unregistered capability and an undeclared argument are each refused by name)",
                Some "built default-deny arm"
            )

        let mutable invokeNodes = 0

        let absentCapability =
            let rec fresh (s: string) =
                if Option.isSome (CapabilityRegistry.tryFind s w.PipelineRegistry) then
                    fresh (s + "_")
                else
                    s

            fresh "__no_such_capability__"

        let strayArg = "__no_such_hole__"

        LawKit.run iterations seed (fun rng _ at ->
            let p = rng.Draw w.GenPipeline

            (match CapabilityPipeline.decode (CapabilityPipeline.encode p) with
             | Ok p2 when p2 = p -> roundtrip.Saw()
             | other ->
                 roundtrip.Check(false, fun () -> at (sprintf "the pipeline did not round-trip the wire (%A)" other)))

            let nodeKeys = p.Nodes |> List.map CapabilityPipeline.nodeInvocationKey

            keys.Check(
                List.length (List.distinct nodeKeys) = List.length nodeKeys,
                fun () -> at (sprintf "two nodes share an invocation key: %A" nodeKeys)
            )

            let lookup = CapabilityLookup.ofRegistry w.PipelineRegistry

            match CapabilityPipeline.typeCheck lookup p with
            | Error e ->
                composes.Check(false, fun () -> at (sprintf "the pipeline does not compose at its registry: %A" e))
            | Ok() ->
                composes.Saw()

                let replaceAt (k: int) (n: PipelineNode) =
                    { Nodes = p.Nodes |> List.mapi (fun j m -> if j = k then n else m) }

                p.Nodes
                |> List.iteri (fun k n ->
                    match n with
                    | Source _ -> ()
                    | Invoke(nid, capId, outT, args) ->
                        invokeNodes <- invokeNodes + 1

                        match
                            CapabilityPipeline.typeCheck
                                lookup
                                (replaceAt k (Invoke(nid, absentCapability, outT, args)))
                        with
                        | Error(PipelineError.PipelineNoSuchCapability(c, _)) when c = absentCapability -> deny.Saw()
                        | other ->
                            deny.Check(
                                false,
                                fun () ->
                                    at (
                                        sprintf
                                            "node %s naming an unregistered capability was not refused: %A"
                                            nid
                                            other
                                    )
                            )

                        match
                            CapabilityPipeline.typeCheck
                                lookup
                                (replaceAt k (Invoke(nid, capId, outT, args @ [ strayArg, Literal "0" ])))
                        with
                        | Error(PipelineError.PipelineArgRefused(m, UnknownArg(a, _))) when m = nid && a = strayArg ->
                            deny.Saw()
                        | other ->
                            deny.Check(
                                false,
                                fun () ->
                                    at (
                                        sprintf "node %s binding an undeclared argument was not refused: %A" nid other
                                    )
                            )))

        LawKit.results [ composes; roundtrip; keys; deny ]
        @ [ SampleAdequacy.reached family "built default-deny arm" seed [ "invoke node", invokeNodes ] ]

    // ---- incremental capability-pipeline evaluation (Phase 62) ----
    // The teeth on `CapabilityPipeline.evalFrom`: the incremental path re-invokes only the
    // downstream-of-change nodes and is byte-identical to a full `eval` over the same inputs (the Phase-34
    // discipline on the capability-DAG). Fixture: a two-source DAG (s1→a, s2→b) so a change to one source
    // leaves the other branch clean — exercising reuse (minimality) alongside re-invocation.

    /// The incremental capability-pipeline laws (Phase 62). Self-contained — over a seed-replayable sample
    /// it evaluates a two-branch pipeline with a deterministic host `body` (a source emits its supplied
    /// value; an invoke sums its resolved args + 1), then re-evaluates from a changed-source set and
    /// certifies:
    ///
    ///  - **byte-identical to full eval** — `evalFrom (eval old) changed p` equals `eval new p` for every
    ///    changed-source set (the reuse is a sound optimisation, never a different answer);
    ///  - **minimal re-invocation** — `evalFrom` re-invokes exactly `dirtySet changed` (a clean branch is
    ///    reused, never re-run);
    ///  - **effect-honesty on the dirty path** — a clean node takes its prior value and is not re-invoked;
    ///    a dirty node is re-invoked and takes the fresh value (never a stale prior on the dirty path).
    let capabilityPipelineIncrementalLaws (seed: int) (iterations: int) : LawResult list =
        let byteIdentical =
            LawKit.LawCell "evalFrom is byte-identical to a full eval over the changed inputs (every change-set)"

        let minimal =
            LawKit.LawCell(
                "evalFrom re-invokes exactly the downstream-of-change nodes (minimal reuse set)",
                Some "node reuse"
            )

        let honesty =
            LawKit.LawCell(
                "a clean node reuses its prior value (not re-invoked); a dirty node re-invokes (effect-honesty)",
                Some "node reuse"
            )

        let mutable dirtyNodes = 0
        let mutable cleanNodes = 0

        // `inc` takes one integer and is total; evaluation type-checks against it (Phase 295).
        let lookup: CapabilityLookup =
            let inc =
                Capability.create
                    "inc"
                    { Name = "inc"
                      Holes =
                        [ { Addr = "x"
                            Name = "x"
                            Kind = "value"
                            Space = Some(IntRange(0, 1000))
                            Slot = None
                            Action = None
                            Required = true } ]
                      Effect = Effect.pureDeterministic }
                    Server

            { TryFind = fun id -> if id = "inc" then Some inc else None
              Known = [ "inc" ]
              Policy = RegistryPolicy.none }

        // s1 → a, s2 → b : two independent branches.
        let pipeline: CapabilityPipeline =
            { Nodes =
                [ Source("s1", "r1", IntRange(0, 1000))
                  Source("s2", "r2", IntRange(0, 1000))
                  Invoke("a", "inc", IntRange(0, 1000), [ "x", FromNode "s1" ])
                  Invoke("b", "inc", IntRange(0, 1000), [ "x", FromNode "s2" ]) ] }

        // a deterministic host body parameterised by the source values; records which nodes it re-invokes.
        let bodyWith (sourceVals: Map<string, int>) (invoked: ResizeArray<string>) =
            fun (node: PipelineNode) (args: (string * PipelineArg<int>) list) ->
                invoked.Add(CapabilityPipeline.nodeId node)

                match node with
                | Source(id, _, _) -> Ok(Map.find id sourceVals)
                | Invoke _ ->
                    let sum =
                        args
                        |> List.sumBy (fun (_, a) ->
                            match a with
                            | FromUpstream v -> v
                            | LiteralArg s -> int s)

                    Ok(sum + 1)

        LawKit.run iterations seed (fun rng _ at ->
            let s1v0 = rng.IntBelow 1000
            let s2v0 = rng.IntBelow 1000
            let s1v1 = rng.IntBelow 1000
            let s2v1 = rng.IntBelow 1000
            let ck = rng.IntBelow 3

            let sv0 = Map.ofList [ "s1", s1v0; "s2", s2v0 ]

            // which source(s) changed → the new source values + the changed-input set
            let sv1, changed =
                match ck with
                | 0 -> Map.ofList [ "s1", s1v1; "s2", s2v0 ], Set.ofList [ "s1" ]
                | 1 -> Map.ofList [ "s1", s1v0; "s2", s2v1 ], Set.ofList [ "s2" ]
                | _ -> Map.ofList [ "s1", s1v1; "s2", s2v1 ], Set.ofList [ "s1"; "s2" ]

            match CapabilityPipeline.eval lookup string (bodyWith sv0 (ResizeArray())) pipeline with
            | Error e -> byteIdentical.Check(false, fun () -> at (sprintf "prior eval errored: %A" e))
            | Ok prior ->
                let fullInvoked = ResizeArray()
                let incrInvoked = ResizeArray()

                let viaFull =
                    CapabilityPipeline.eval lookup string (bodyWith sv1 fullInvoked) pipeline

                let viaIncr =
                    CapabilityPipeline.evalFrom lookup string (bodyWith sv1 incrInvoked) prior changed pipeline

                // byte-identical to a full eval over the changed inputs
                byteIdentical.Check(
                    (viaIncr = viaFull),
                    fun () -> at (sprintf "evalFrom ≠ eval (changed=%A)\n  incr=%A\n  full=%A" changed viaIncr viaFull)
                )

                // minimal re-invocation: evalFrom re-invokes exactly the dirty set
                let dirty = CapabilityPipeline.dirtySet changed pipeline
                let reInvoked = Set.ofSeq incrInvoked

                minimal.Check(
                    (reInvoked = dirty),
                    fun () -> at (sprintf "re-invoked=%A ≠ dirtySet=%A" reInvoked dirty)
                )

                // effect-honesty: clean nodes take their prior value & are not re-invoked; dirty nodes are.
                match viaIncr with
                | Ok result ->
                    let allIds = pipeline.Nodes |> List.map CapabilityPipeline.nodeId

                    // Phase 121 — the honesty law has two halves and each needs its own class of
                    // node to exist. A sample in which every node is dirty says nothing about reuse;
                    // one in which none is says nothing about re-invocation.
                    dirtyNodes <-
                        dirtyNodes
                        + (allIds |> List.filter (fun id -> Set.contains id dirty) |> List.length)

                    cleanNodes <-
                        cleanNodes
                        + (allIds |> List.filter (fun id -> not (Set.contains id dirty)) |> List.length)

                    let fault =
                        allIds
                        |> List.tryPick (fun id ->
                            if Set.contains id dirty then
                                if not (Set.contains id reInvoked) then
                                    Some(sprintf "dirty node %s not re-invoked" id)
                                else
                                    None
                            elif Set.contains id reInvoked then
                                Some(sprintf "clean node %s was re-invoked" id)
                            elif Map.tryFind id result <> Map.tryFind id prior then
                                Some(sprintf "clean node %s did not reuse its prior value" id)
                            else
                                None)

                    match fault with
                    | Some f -> honesty.Check(false, fun () -> at f)
                    | None -> honesty.Saw()
                | Error e -> honesty.Check(false, fun () -> at (sprintf "evalFrom errored: %A" e)))

        LawKit.results [ byteIdentical; minimal; honesty ]
        @ [ SampleAdequacy.reached
                "Conformance.capabilityPipelineIncrementalLaws"
                "node reuse"
                seed
                [ "dirty node", dirtyNodes; "clean node", cleanNodes ] ]
