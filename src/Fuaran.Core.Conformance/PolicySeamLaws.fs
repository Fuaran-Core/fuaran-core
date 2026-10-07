namespace Fuaran.Core

/// The Phase 318 gates: the policy gate, the write gate and the keyed capture journal — `SeamLaws`
/// until the Phase 388 split along its banners.
module internal PolicySeamLaws =
    // ---- the policy gate (Phase 318) ----

    /// The policy-gate laws (Phase 318). Self-contained: it builds its own capabilities, queries and
    /// gates from the seed, and over a seed-replayable sample certifies:
    ///
    ///  - **the join is a lattice on the decisions' rank** — `join` is the maximum of `Allow <
    ///    NeedsApproval < Deny`, commutative up to which denial is kept, associative, idempotent,
    ///    with `Allow` its identity and `Deny` absorbing, and MONOTONE: raising either argument never
    ///    lowers the result; `all` is the join's fold and `any` the meet's, the empty `any` a denial;
    ///  - **the gate runs after validation and before the body** — on all three registries, an
    ///    invocation a gate denies is `PolicyRefused` naming that gate (`QueryPolicyRefused` on the
    ///    query seam), one it parks is `ApprovalRequired`, and in neither case does the body run; an
    ///    invocation whose arguments do not validate is refused by validation before any gate;
    ///  - **every policy refusal reaches every observer, once** — and nothing else does;
    ///  - **a gate only tightens** — `decide` after `withGate` is never less restrictive than before,
    ///    a union refuses whatever either side's policy refused, and `restrict` keeps the policy;
    ///  - **`decide` agrees with `dispatch`** — the body runs exactly when `decide` answers `Allow`.
    let policyLaws (seed: int) (iterations: int) : LawResult list =
        let lattice =
            LawKit.LawCell
                "join is the rank maximum, commutative up to the kept denial, associative, idempotent, with Allow its identity, Deny absorbing, and monotone; all and any fold join and meet"

        let beforeBody =
            LawKit.LawCell
                "on all three registries a denied invocation is PolicyRefused naming its gate and a parked one ApprovalRequired, after validation and before the body, which never runs"

        let observed =
            LawKit.LawCell "every policy refusal reaches every observer exactly once, and no other outcome reaches any"

        let tightens =
            LawKit.LawCell
                "withGate never lowers decide's rank, a union refuses what either side refused, and restrict, register and unregister carry the policy through equality"

        let agrees = LawKit.LawCell "the body runs exactly when decide answers Allow"

        let pipelineAdmits =
            LawKit.LawCell
                "a pipeline evaluated over either registry's lookup admits each Invoke through the registry's policy: a denied one is EvalPolicyRefused naming its gate, a parked one EvalPolicyRefused with ApprovalRequired, observers told, and its body never runs"

        let decisions =
            [ PolicyDecision.Allow
              PolicyDecision.NeedsApproval
              PolicyDecision.deny "first"
              PolicyDecision.denyWith "second" [ "x" ] ]

        let rank = PolicyDecision.rank

        let capOf (id: string) : Capability =
            Capability.create
                id
                { Name = id
                  Holes =
                    [ { Addr = "n"
                        Name = "n"
                        Kind = "value"
                        Space = Some(IntRange(0, 9))
                        Slot = None
                        Action = None
                        Required = true } ]
                  Effect = Effect.pureDeterministic }
                BuildTime

        let queryOf (id: string) : Query =
            { Id = id
              Params =
                [ { Name = "n"
                    Type = IntType
                    Required = true } ]
              ResultSchema = [ "n", IntType ]
              Effect = Effect.pureDeterministic
              Source = Ref id
              TimeoutMs = None
              PageSize = None }

        let ids = [ "a"; "b"; "c" ]

        let build (add: 'x -> 'r -> Result<'r, 'e>) (empty: 'r) (xs: 'x list) : 'r =
            xs |> List.fold (fun r x -> add x r |> Result.defaultValue r) empty

        let emptyResult: QueryResult =
            { Rows = { Schema = []; Columns = [] }
              PageNum = 0
              TotalRowCount = None
              NextPageToken = None }

        LawKit.run iterations (seed + 318) (fun rng _ at ->
            // ---- the lattice, over a drawn triple ----
            let a = rng.Choose decisions
            let b = rng.Choose decisions
            let c = rng.Choose decisions
            let j = PolicyDecision.join

            // monotone: a <= a' (by rank) implies join a c <= join a' c, both arguments
            let a' = rng.Choose decisions

            let monotoneHolds =
                rank a > rank a'
                || (rank (j a c) <= rank (j a' c) && rank (j c a) <= rank (j c a'))

            lattice.Check(
                rank (j a b) = max (rank a) (rank b)
                && rank (j a b) = rank (j b a)
                && j (j a b) c = j a (j b c)
                && j a a = a
                && j PolicyDecision.Allow a = a
                && j a PolicyDecision.Allow = a
                && rank (j a (PolicyDecision.deny "z")) = 2
                && monotoneHolds
                && PolicyDecision.all [ a; b; c ] = j (j a b) c
                && rank (PolicyDecision.any [ a; b; c ]) = min (rank a) (min (rank b) (rank c))
                && rank (PolicyDecision.any []) = 2
                && PolicyDecision.all [] = PolicyDecision.Allow,
                fun () -> at (sprintf "the lattice failed over %A %A %A %A" a a' b c)
            )

            // ---- the gate over the three registries ----
            for verdict in decisions do
                let target = rng.Choose ids
                let gateName = "g" + string (rng.IntBelow 5)

                let decideFor (id: string) =
                    if id = target then verdict else PolicyDecision.Allow

                let told = ResizeArray<PolicyDenial<(string * string) list>>()
                let qTold = ResizeArray<PolicyDenial<(string * Cell) list>>()

                let capReg =
                    build CapabilityRegistry.register CapabilityRegistry.empty (ids |> List.map capOf)
                    |> CapabilityRegistry.withGate
                        { Policy = gateName
                          Decide = fun c _ -> decideFor c.Id }
                    |> CapabilityRegistry.onDenied told.Add

                let fnReg =
                    build
                        FunctionRegistry.register
                        FunctionRegistry.empty
                        (ids |> List.map (fun id -> FunctionRegistry.entry "doc" (capOf id)))
                    |> FunctionRegistry.withGate
                        { Policy = gateName
                          Decide = fun c _ -> decideFor c.Id }

                let qReg =
                    build QueryRegistry.register QueryRegistry.empty (ids |> List.map queryOf)
                    |> QueryRegistry.withGate
                        { Policy = gateName
                          Decide = fun q _ -> decideFor q.Id }
                    |> QueryRegistry.onDenied qTold.Add

                let id = target
                let n = rng.IntBelow 10
                let ran = ResizeArray<string>()

                let capOut =
                    CapabilityRegistry.dispatch capReg id [ "n", string n ] (fun _ () ->
                        ran.Add "capability"
                        Ready n)

                let fnOut =
                    FunctionRegistry.dispatch fnReg id [ "n", string n ] (fun _ () ->
                        ran.Add "function"
                        Ready n)

                let qOut =
                    QueryRegistry.dispatch qReg id [ "n", Int n ] (fun _ ->
                        ran.Add "query"
                        Ready emptyResult)

                let expected = decideFor id

                let capRight, fnRight, qRight =
                    match expected with
                    | PolicyDecision.Allow -> capOut = Ok(Ready n), fnOut = Ok(Ready n), qOut = Ok(Ready emptyResult)
                    | PolicyDecision.NeedsApproval ->
                        capOut = Error(ApprovalRequired gateName),
                        fnOut = Error(ApprovalRequired gateName),
                        qOut = Error(QueryApprovalRequired gateName)
                    | PolicyDecision.Deny g ->
                        capOut = Error(PolicyRefused(gateName, g.Message, g.Alternatives)),
                        fnOut = Error(PolicyRefused(gateName, g.Message, g.Alternatives)),
                        qOut = Error(QueryPolicyRefused(gateName, g.Message, g.Alternatives))

                let bodyRuns = if expected = PolicyDecision.Allow then 3 else 0

                // an invalid argument is refused by validation, before any gate is consulted
                let consulted = ResizeArray<string>()

                let watching =
                    CapabilityRegistry.withGate
                        { Policy = "watch"
                          Decide =
                            fun c _ ->
                                consulted.Add c.Id
                                PolicyDecision.Allow }
                        capReg

                let bad =
                    CapabilityRegistry.dispatch watching target [ "n", "99" ] (fun _ () ->
                        ran.Add "bad"
                        Ready 0)

                beforeBody.Check(
                    capRight
                    && fnRight
                    && qRight
                    && ran.Count = bodyRuns
                    && consulted.Count = 0
                    && (match bad with
                        | Error(ArgOutOfSpace _) -> true
                        | _ -> false),
                    fun () ->
                        at (
                            sprintf
                                "gate %s on %s deciding %A: %A / %A / %A (bodies run %A)"
                                gateName
                                id
                                expected
                                capOut
                                fnOut
                                qOut
                                (List.ofSeq ran)
                        )
                )

                let capTold = told.Count
                let refusedHere = expected <> PolicyDecision.Allow

                observed.Check(
                    (if refusedHere then
                         capTold = 1
                         && told.[0] = { Policy = gateName
                                         Id = id
                                         Args = [ "n", string n ]
                                         Decision = expected }
                         && qTold.Count = 1
                     else
                         capTold = 0 && qTold.Count = 0),
                    fun () -> at (sprintf "observers saw %A / %A for %A" (List.ofSeq told) (List.ofSeq qTold) expected)
                )

                // ---- the pipeline evaluator admits through the gate (Phase 383) ----
                let pipeline: CapabilityPipeline =
                    { Nodes =
                        [ Source("s", "ref", IntRange(0, 9))
                          Invoke("i", id, IntRange(0, 9), [ "n", FromNode "s" ]) ] }

                let invoked = ResizeArray<string>()

                let pipelineBody (node: PipelineNode) (_: (string * PipelineArg<int>) list) : Result<int, string> =
                    match node with
                    | Source _ -> Ok n
                    | Invoke(nid, _, _, _) ->
                        invoked.Add nid
                        Ok n

                let toldBefore = told.Count

                let viaCap =
                    CapabilityPipeline.eval (CapabilityLookup.ofRegistry capReg) string pipelineBody pipeline

                let toldByEval = told.Count - toldBefore

                let viaFn =
                    CapabilityPipeline.eval (CapabilityLookup.ofFunctionRegistry fnReg) string pipelineBody pipeline

                let viaFrom =
                    CapabilityPipeline.evalFrom
                        (CapabilityLookup.ofRegistry capReg)
                        string
                        pipelineBody
                        Map.empty
                        (Set.ofList [ "s" ])
                        pipeline

                let wanted: Result<Map<string, int>, PipelineEvalError> =
                    match expected with
                    | PolicyDecision.Allow -> Ok(Map.ofList [ "s", n; "i", n ])
                    | PolicyDecision.NeedsApproval -> Error(EvalPolicyRefused("i", ApprovalRequired gateName))
                    | PolicyDecision.Deny g ->
                        Error(EvalPolicyRefused("i", PolicyRefused(gateName, g.Message, g.Alternatives)))

                pipelineAdmits.Check(
                    viaCap = wanted
                    && viaFn = wanted
                    && viaFrom = wanted
                    && invoked.Count = (if refusedHere then 0 else 3)
                    && toldByEval = (if refusedHere then 1 else 0),
                    fun () ->
                        at (
                            sprintf
                                "gate %s on %s deciding %A: the pipeline gave %A / %A / %A (bodies run %A, observers told %d)"
                                gateName
                                id
                                expected
                                viaCap
                                viaFn
                                viaFrom
                                (List.ofSeq invoked)
                                toldByEval
                        )
                )

                // ---- a gate only tightens ----
                let args = [ "n", string n ]

                let baseReg =
                    build CapabilityRegistry.register CapabilityRegistry.empty (ids |> List.map capOf)

                let secondVerdict = rng.Choose decisions

                let second: PolicyGate<Capability, (string * string) list> =
                    { Policy = "second"
                      Decide = fun _ _ -> secondVerdict }

                let gated = CapabilityRegistry.withGate second capReg

                let other =
                    build CapabilityRegistry.register CapabilityRegistry.empty [ capOf "z" ]
                    |> CapabilityRegistry.withGate second

                let unionTightens =
                    match CapabilityRegistry.union capReg other with
                    | Ok u ->
                        rank (CapabilityRegistry.decide u id args)
                        >= rank (CapabilityRegistry.decide capReg id args)
                        && rank (CapabilityRegistry.decide u id args) >= rank secondVerdict
                    | Error _ -> false

                let restricted = CapabilityRegistry.restrict (Set.ofList ids) capReg

                tightens.Check(
                    rank (CapabilityRegistry.decide gated id args)
                    >= rank (CapabilityRegistry.decide capReg id args)
                    && rank (CapabilityRegistry.decide capReg id args)
                       >= rank (CapabilityRegistry.decide baseReg id args)
                    && unionTightens
                    && CapabilityRegistry.decide restricted id args = CapabilityRegistry.decide capReg id args
                    && restricted = capReg
                    && hash restricted = hash capReg
                    && (CapabilityRegistry.register (capOf "w") capReg
                        |> Result.bind (CapabilityRegistry.unregister "w")) = Ok capReg,
                    fun () -> at (sprintf "a gate loosened the registry for %s" id)
                )

                agrees.Check(
                    (CapabilityRegistry.decide capReg id args = PolicyDecision.Allow) = (capOut = Ok(Ready n))
                    && (FunctionRegistry.decide fnReg id args = PolicyDecision.Allow) = (fnOut = Ok(Ready n))
                    && (QueryRegistry.decide qReg id [ "n", Int n ] = PolicyDecision.Allow) = (qOut = Ok(
                        Ready emptyResult
                    ))
                    && rank (CapabilityRegistry.decide capReg "missing" []) = 2,
                    fun () -> at (sprintf "decide and dispatch disagree on %s" id)
                ))

        LawKit.results [ lattice; beforeBody; observed; tightens; agrees; pipelineAdmits ]

    /// The no-unapproved-write law and the dry run's agreement with `Apply`, at a DOMAIN's guarded
    /// AI surface (Phase 318). Runs the domain's OWN `Decide` (never a kit policy), joined with the
    /// registry's decision for every capability its `EffectsOf` names (`Proposals.decideGuarded`),
    /// and certifies over drawn ops and actors:
    ///
    ///  - **no unapproved write** — for every actor `privileged` refuses, no op that invokes a
    ///    registered capability whose effect writes to the host (`WritesHost`) is `Allow`ed;
    ///  - **the dry run agrees with `Apply`** — `DryRun op s` and `Apply op s` accept the same ops,
    ///    and agree on the state (compared with `Unchecked.equals`);
    ///  - **an inapplicable sequence never applies** — `submitGuarded` of a sequence whose dry run
    ///    refuses is a refusal (`SubmitDenied` or `SubmitOpRejected`), never applied or parked.
    let policyLawsAt
        (family: string)
        (gw: GuardedSurfaceWitness<'State, 'Op, 'Rej>)
        (registry: CapabilityRegistry)
        (state0: 'State)
        (genOp: ConfRng.T -> 'Op * ConfRng.T)
        (actors: string list)
        (privileged: string -> bool)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let noWrite =
            LawKit.LawCell "no op that invokes a host-writing capability is Allowed for an unprivileged actor"

        let dryRun =
            LawKit.LawCell "DryRun and Apply accept the same ops and agree on the state they produce"

        let inapplicable =
            LawKit.LawCell "a sequence whose dry run refuses is never applied or parked by submitGuarded"

        let mutable writes = 0
        let mutable unprivileged = 0
        let mutable refusedRuns = 0

        let writesHost (op: 'Op) =
            gw.EffectsOf op
            |> List.exists (fun (id, _) ->
                match CapabilityRegistry.tryFind id registry with
                | Some c -> c.Signature.Effect.Host = WritesHost
                | None -> false)

        LawKit.run iterations (seed + 3180) (fun rng _ at ->
            let op = rng.Draw genOp
            let op2 = rng.Draw genOp
            let actor = rng.Choose actors

            if not (privileged actor) then
                unprivileged <- unprivileged + 1

                if writesHost op then
                    writes <- writes + 1

                    noWrite.Check(
                        Proposals.decideGuarded gw registry actor op <> PolicyDecision.Allow,
                        fun () ->
                            at (sprintf "%A was allowed for unprivileged %s although it writes to the host" op actor)
                    )

            let agree =
                match gw.DryRun op state0, gw.Surface.Apply op state0 with
                | Ok a, Ok b -> Unchecked.equals a b
                | Error _, Error _ -> true
                | _ -> false

            dryRun.Check(agree, fun () -> at (sprintf "DryRun and Apply disagree on %A" op))

            let seqOps = [ op; op2 ]

            let dry =
                seqOps |> List.fold (fun acc o -> acc |> Result.bind (gw.DryRun o)) (Ok state0)

            match dry with
            | Ok _ -> ()
            | Error _ ->
                refusedRuns <- refusedRuns + 1

                match Proposals.submitGuarded gw registry actor "t" None seqOps Proposals.Queue.empty state0 with
                | Proposals.SubmitDenied _
                | Proposals.SubmitOpRejected _ -> inapplicable.Saw()
                | other -> inapplicable.Check(false, fun () -> at (sprintf "an inapplicable sequence was %A" other)))

        LawKit.results [ noWrite; dryRun; inapplicable ]
        @ [ SampleAdequacy.reached
                family
                "op and actor"
                seed
                [ "unprivileged actor", unprivileged
                  "host-writing op", writes
                  "refused dry run", refusedRuns ] ]

    // ---- the write gate (Phase 318) ----

    /// The write-gate laws (Phase 318), at a domain's tree witnesses and op generator:
    ///
    ///  - **targets cover the writes** — every id an applied op creates, destroys or whose child list
    ///    it changes is in `WriteGate.targetsOf`, or is the source parent of a target (a removal's or
    ///    a move's), whose lock reaches that target because the target is in its subtree;
    ///  - **the gate runs before the reducer** — `applyGated` is `Denied` exactly when `decide`
    ///    refuses, and is `Ops.apply` otherwise;
    ///  - **a lock covers its subtree** — locking a node refuses every non-batch op one of whose
    ///    targets lies in that node's subtree, and `allowAll` refuses nothing;
    ///  - **locking more never admits more** — a gate whose `Locked` is a superset refuses every op
    ///    the smaller one refused.
    let writeGateLaws
        (family: string)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let cover =
            LawKit.LawCell
                "every id an applied op creates, destroys or whose child list it changes is a target, or the source parent of one"

        let first =
            LawKit.LawCell "applyGated is Denied exactly when decide refuses, and Ops.apply otherwise"

        let subtree =
            LawKit.LawCell
                "a lock refuses every non-batch op with a target in the locked node's subtree, and allowAll refuses nothing"

        let monotone =
            LawKit.LawCell "a gate locking a superset refuses every op the smaller gate refused"

        let mutable applied = 0
        let mutable denied = 0

        let key = idw.ToString

        let childLists (t: 'Node) : Map<string, string list> =
            Tree.preorder nodew t
            |> List.map (fun n -> key (nodew.Id n), nodew.Children n |> List.map (nodew.Id >> key))
            |> Map.ofList

        let parentOf (t: 'Node) : Map<string, string> =
            Tree.preorder nodew t
            |> List.collect (fun n -> nodew.Children n |> List.map (fun c -> key (nodew.Id c), key (nodew.Id n)))
            |> Map.ofList

        LawKit.run iterations (seed + 3181) (fun rng _ at ->
            let tree = rng.Draw gen.Tree
            let op = rng.Draw(LawKit.genOp nodew idw gen tree)
            let targets = WriteGate.targetsOf nodew idw op tree

            match Ops.apply nodew idw op tree with
            | Ok post ->
                applied <- applied + 1
                let before = childLists tree
                let after = childLists post

                let written =
                    Set.union (Set.ofSeq before.Keys) (Set.ofSeq after.Keys)
                    |> Set.filter (fun id -> Map.tryFind id before <> Map.tryFind id after)

                let parents = parentOf tree

                let uncovered =
                    written
                    |> Set.filter (fun id ->
                        not (targets.Contains id)
                        && not (targets |> Set.exists (fun t -> Map.tryFind t parents = Some id)))

                cover.Check(
                    uncovered.IsEmpty,
                    fun () ->
                        at (sprintf "%A wrote %A beyond its targets %A" op (Set.toList uncovered) (Set.toList targets))
                )
            | Error _ -> ()

            let ids = Tree.preorder nodew tree |> List.map (nodew.Id >> key)
            let lockedNode = rng.Choose ids
            let gate = WriteGate.lockOnly [ lockedNode ]
            let verdict = WriteGate.decide gate nodew idw op tree

            if Result.isError verdict then
                denied <- denied + 1

            first.Check(
                (match verdict, WriteGate.applyGated gate nodew idw op tree with
                 | Error d, Error(GatedApplyFailure.Denied d') -> d = d'
                 | Ok(), Ok t -> Unchecked.equals (Ops.apply nodew idw op tree) (Ok t: Result<'Node, Rejection<'Id>>)
                 | Ok(), Error(GatedApplyFailure.Rejected r) ->
                     Unchecked.equals (Ops.apply nodew idw op tree) (Error r: Result<'Node, Rejection<'Id>>)
                 | _ -> false),
                fun () -> at (sprintf "applyGated disagrees with decide on %A" op)
            )

            // a non-batch op with an existing target under the lock is refused
            let underLock (t: string) =
                match Tree.path nodew idw (idw.OfString t) tree with
                | Some path -> path |> List.map key |> List.contains lockedNode
                | None -> false

            let directHit =
                match op with
                | Batch _ -> false
                | _ -> targets |> Set.exists underLock

            subtree.Check(
                (not directHit || Result.isError verdict)
                && WriteGate.decide WriteGate.allowAll nodew idw op tree = Ok(),
                fun () -> at (sprintf "locking %s did not refuse %A" lockedNode op)
            )

            let wider = WriteGate.lockOnly [ lockedNode; rng.Choose ids ]

            monotone.Check(
                not (Result.isError verdict)
                || Result.isError (WriteGate.decide wider nodew idw op tree),
                fun () -> at (sprintf "a wider lock admitted %A" op)
            ))

        LawKit.results [ cover; first; subtree; monotone ]
        @ [ SampleAdequacy.reached family "gated op" seed [ "applied op", applied; "denied op", denied ] ]

    // ---- the keyed capture journal (Phase 318) ----

    /// The keyed-capture laws (Phase 318). Self-contained: it records sessions of invocations under
    /// drawn keys and outcomes — some settled, some failed, some left pending — and certifies:
    ///
    ///  - **a recorded journal verifies** — chain and pairing (`verifyKeyedCaptures`);
    ///  - **replay is by key** — the invocations replayed in a SHUFFLED order across keys (each key's
    ///    own calls kept in order) answer exactly what each settled to: a value, the same failure, or
    ///    `None` for one left pending; the live effect is never consulted;
    ///  - **a miss refuses** — a key with no record is `NoCapture`, a call past the recorded ones is
    ///    `Exhausted`, an invocation settles once (`AlreadySettled`) and only after its attempt
    ///    (`NotAttempted`);
    ///  - **tampering is detected** — a changed value, or a record dropped from before the tip, fails
    ///    verification (a journal cut at its tip verifies on its own, as `verifyCaptures`' does);
    ///  - **the seam replays exactly** — a `network` capability dispatched through
    ///    `CapabilityRegistry.dispatchCaptured` replays through `dispatchReplayed` with the recorded
    ///    answer even when the live body would now answer otherwise, and an invocation the policy
    ///    refused journals nothing.
    let keyedCaptureLaws (seed: int) (iterations: int) : LawResult list =
        let verifies =
            LawKit.LawCell "a recorded keyed journal verifies: chain intact and phases paired"

        let byKey =
            LawKit.LawCell "replay in any order across keys answers each invocation exactly what it settled to"

        let refuses =
            LawKit.LawCell
                "a missing key is NoCapture, a call past the recorded is Exhausted, and settling is once, after an attempt"

        let tamper =
            LawKit.LawCell "a changed value or a record dropped from before the tip fails verification"

        let seam =
            LawKit.LawCell
                "a network capability replays exactly through dispatchReplayed, and a policy refusal journals nothing"

        let hashFn = OpStream.defaultHash
        let enc (v: int) = string v

        let dec (s: string) =
            match System.Int32.TryParse s with
            | true, v -> Ok v
            | _ -> Error("not an int: " + s)

        let netCap =
            Capability.create
                "fetch"
                { Name = "fetch"
                  Holes =
                    [ { Addr = "n"
                        Name = "n"
                        Kind = "value"
                        Space = Some(IntRange(0, 9))
                        Slot = None
                        Action = None
                        Required = true } ]
                  Effect =
                    { Effect.pureDeterministic with
                        Determinism = Effect.network } }
                Server

        LawKit.run iterations (seed + 3182) (fun rng _ at ->
            let n = 1 + rng.IntBelow 6

            let calls =
                [ for i in 0 .. n - 1 ->
                      let key = "k" + string (rng.IntBelow 3)

                      let outcome =
                          match rng.IntBelow 4 with
                          | 0 -> Some(Error("failed " + string i))
                          | 1 -> None
                          | _ -> Some(Ok(rng.IntBelow 100))

                      key, outcome ]

            let journal, tickets =
                calls
                |> List.fold
                    (fun (j, ts) (key, outcome) ->
                        let _, occ, j' =
                            OpStream.captureEffectKeyed hashFn enc "network" key (fun () -> outcome) j

                        j', ts @ [ key, occ, outcome ])
                    ([], [])

            verifies.Check(
                OpStream.verifyKeyedCaptures hashFn journal,
                fun () -> at (sprintf "a recorded journal of %d calls does not verify" n)
            )

            // shuffle across keys, keeping each key's own order
            let order =
                tickets |> List.groupBy (fun (k, _, _) -> k) |> rng.Shuffle |> List.collect snd

            let replayedRight =
                order
                |> List.fold
                    (fun (cursor, ok) (key, _, outcome) ->
                        match OpStream.replayEffectKeyed dec "network" key (fun () -> Some(Ok -1)) cursor journal with
                        | Ok(answer, cursor') -> cursor', ok && answer = outcome
                        | Error _ -> cursor, false)
                    (Map.empty, true)
                |> snd

            byKey.Check(replayedRight, fun () -> at (sprintf "keyed replay of %A disagreed with the record" calls))

            // misses
            let missing =
                OpStream.replayEffectKeyed dec "network" "absent" (fun () -> None) Map.empty journal

            let firstKey, _, _ = List.head tickets

            let recorded = tickets |> List.filter (fun (k, _, _) -> k = firstKey) |> List.length

            let past =
                OpStream.replayEffectKeyed
                    dec
                    "network"
                    firstKey
                    (fun () -> None)
                    (Map.ofList [ firstKey, recorded ])
                    journal

            let twice =
                match tickets |> List.tryFind (fun (_, _, o) -> Option.isSome o) with
                | Some(k, occ, _) ->
                    OpStream.settleEffectKeyed hashFn enc k occ (Ok 1) journal = Error(
                        KeyedCaptureFault.AlreadySettled(k, occ)
                    )
                | None -> true

            refuses.Check(
                missing = Error(KeyedCaptureFault.NoCapture "absent")
                && past = Error(KeyedCaptureFault.Exhausted(firstKey, recorded))
                && twice
                && OpStream.settleEffectKeyed hashFn enc "absent" 0 (Ok 1) journal = Error(
                    KeyedCaptureFault.NotAttempted("absent", 0)
                ),
                fun () -> at (sprintf "a miss was not refused over %A" calls)
            )

            // tamper: change every completed value, or drop the first record
            let changed =
                journal
                |> List.map (fun c ->
                    if c.Phase = CapturePhase.Completed then
                        { c with Value = c.Value + "0" }
                    else
                        c)

            let anyCompleted =
                journal |> List.exists (fun c -> c.Phase = CapturePhase.Completed)

            tamper.Check(
                (not anyCompleted || not (OpStream.verifyKeyedCaptures hashFn changed))
                && (List.length journal < 2
                    || not (OpStream.verifyKeyedCaptures hashFn (List.tail journal))),
                fun () -> at "a tampered keyed journal verified"
            )

            // the seam
            let recordedN = rng.IntBelow 10
            let arg = rng.IntBelow 10

            let reg =
                CapabilityRegistry.register netCap CapabilityRegistry.empty
                |> Result.defaultValue CapabilityRegistry.empty

            let live, _, j =
                CapabilityRegistry.dispatchCaptured
                    hashFn
                    enc
                    reg
                    "fetch"
                    [ "n", string arg ]
                    (fun _ () -> Ready recordedN)
                    []

            let replay =
                CapabilityRegistry.dispatchReplayed
                    dec
                    reg
                    "fetch"
                    [ "n", string arg ]
                    (fun _ () -> Ready(recordedN + 1))
                    Map.empty
                    j

            let denying =
                CapabilityRegistry.withGate
                    { Policy = "no"
                      Decide = fun _ _ -> PolicyDecision.deny "never" }
                    reg

            let _, _, refusedJournal =
                CapabilityRegistry.dispatchCaptured
                    hashFn
                    enc
                    denying
                    "fetch"
                    [ "n", string arg ]
                    (fun _ () -> Ready 0)
                    []

            seam.Check(
                live = Ok(Ready recordedN)
                && (match replay with
                    | Ok(outcome, _) -> outcome = Ok(Ready recordedN)
                    | Error _ -> false)
                && List.isEmpty refusedJournal,
                fun () -> at (sprintf "the seam did not replay exactly: %A" replay)
            ))

        LawKit.results [ verifies; byKey; refuses; tamper; seam ]
