namespace Fuaran.Core

/// The propagation families (Phase 297 split): dirty propagation and the incremental evaluator contract.
module internal PropagationLaws =

    // ---- tree-level dirty propagation (Phase 68) ----
    // The teeth on `Propagation.dirtyFromChangedIds`: over random acyclic reference graphs + a toy pull
    // evaluator, the derived dirty set is exactly the reverse-reachability closure (sound + minimal), no
    // node outside it changes value under the edit, and an incremental recompute over the dirty set is
    // byte-identical to a full recompute (the Phase-34/62 discipline at the tree level). Plus a fixed cyclic
    // fixture: a reference cycle enumerates as data (Tarjan SCC), never a divergence (GP4).

    /// The dirty-propagation laws (Phase 68). Self-contained. Each iteration builds a random DAG (node `i`
    /// reads a random subset of nodes `j < i`, so acyclic by construction) + an intrinsic base value per
    /// node; a toy pull evaluator computes `value(n) = base(n) + Σ value(reads)`. It changes one node's base
    /// and certifies:
    ///
    ///  - **sound + minimal dirty set** — `dirtyFromChangedIds` equals an independent per-node
    ///    reads-reachability oracle (the changed node ∪ every node transitively reading it, and nothing
    ///    else);
    ///  - **frontier soundness** — no node *outside* the dirty set changes value under the edit;
    ///  - **byte-identity bridge** — an incremental recompute (reuse clean, recompute dirty) is byte-identical
    ///    to a full recompute over the changed base.
    ///
    /// A final fixed law certifies **cycle-as-data**: a 3-cycle enumerates as a `Cycles` SCC and
    /// `cycleThrough` returns a path — `sort` terminates, never diverges.
    let dirtyPropagationLaws (seed: int) (iterations: int) : LawResult list =
        let soundMinimal =
            LawKit.LawCell "dirtyFromChangedIds equals the independent reads-reachability closure (sound + minimal)"

        let frontier =
            LawKit.LawCell "no node outside the dirty set changes value under the edit (frontier soundness)"

        let byteIdentity =
            LawKit.LawCell "incremental recompute over the dirty set is byte-identical to a full recompute"

        let cycle =
            LawKit.LawCell "a reference cycle enumerates as a Tarjan SCC + cycleThrough returns a path (cycle-as-data)"
        // Phase 121 — frontier soundness quantifies over CLEAN nodes and the reuse half of the
        // byte-identity law over DIRTY ones, so a sample in which every node is dirty (or none is)
        // certifies one of them green having never applied it.
        let mutable dirtyNodes = 0
        let mutable cleanNodes = 0

        // an independent oracle: the read-edges reachable from `start` (inclusive) — a per-node forward walk,
        // computed differently from `dirtyFromChangedIds`' inverted BFS, so it genuinely cross-checks it.
        let readsReachable (deps: Map<string, Set<string>>) (start: string) : Set<string> =
            let rec go (front: Set<string>) (acc: Set<string>) =
                if Set.isEmpty front then
                    acc
                else
                    let next =
                        (Set.empty, front)
                        ||> Set.fold (fun s node ->
                            match Map.tryFind node deps with
                            | Some rs -> Set.union s rs
                            | None -> s)

                    let fresh = Set.difference next acc
                    go fresh (Set.union acc fresh)

            go (Set.singleton start) (Set.singleton start)

        LawKit.run iterations seed (fun rng _ at ->
            let extra = rng.IntBelow 6
            let nNodes = extra + 2
            let ids = [ for k in 0 .. nNodes - 1 -> string k ]

            // node k reads a random subset of {0 .. k-1} — acyclic by construction (edges point to lower ids).
            let deps =
                [ for k in 0 .. nNodes - 1 ->
                      let reads =
                          [ for j in 0 .. k - 1 do
                                let coin = rng.IntBelow 3

                                if coin = 0 then
                                    yield string j ]

                      string k, Set.ofList reads ]
                |> Map.ofList

            // an intrinsic base value per node, and a change to one node's base.
            let base0 =
                [ for k in 0 .. nNodes - 1 ->
                      let v = rng.IntBelow 100
                      string k, v ]
                |> Map.ofList

            let ck = rng.IntBelow nNodes
            let changedId = string ck
            let base1 = Map.add changedId (Map.find changedId base0 + 1000) base0

            // toy pull evaluator over the DAG, index order (reads point to lower ids ⇒ already computed).
            let evalWith (baseOf: Map<string, int>) : Map<string, int> =
                (Map.empty, ids)
                ||> List.fold (fun acc id ->
                    let v =
                        Map.find id baseOf
                        + (Map.find id deps |> Set.fold (fun s rd -> s + Map.find rd acc) 0)

                    Map.add id v acc)

            let oldVals = evalWith base0
            let newVals = evalWith base1

            let changed = Set.singleton changedId
            let dirty = Propagation.dirtyFromChangedIds deps changed

            // (1) sound + minimal: dirtyFromChangedIds == the independent reads-reachability oracle.
            let oracle =
                ids
                |> List.filter (fun n -> Set.contains changedId (readsReachable deps n))
                |> Set.ofList

            soundMinimal.Check(
                (dirty = oracle),
                fun () -> at (sprintf "dirty=%A ≠ oracle=%A (deps=%A)" dirty oracle deps)
            )

            dirtyNodes <- dirtyNodes + (ids |> List.filter (fun n -> Set.contains n dirty) |> List.length)

            cleanNodes <-
                cleanNodes
                + (ids |> List.filter (fun n -> not (Set.contains n dirty)) |> List.length)

            // (2) frontier soundness: no node outside `dirty` changes value under the edit.
            let leaked =
                ids
                |> List.tryFind (fun n -> not (Set.contains n dirty) && Map.find n oldVals <> Map.find n newVals)

            match leaked with
            | Some n ->
                frontier.Check(false, fun () -> at (sprintf "clean node %s changed value (unsound frontier)" n))
            | None -> frontier.Saw()

            // (3) byte-identity: incremental recompute (reuse clean, recompute dirty) == full recompute.
            let incr =
                (Map.empty, ids)
                ||> List.fold (fun acc id ->
                    let v =
                        if Set.contains id dirty then
                            Map.find id base1
                            + (Map.find id deps |> Set.fold (fun s rd -> s + Map.find rd acc) 0)
                        else
                            Map.find id oldVals

                    Map.add id v acc)

            byteIdentity.Check(
                (incr = newVals),
                fun () -> at (sprintf "incremental recompute ≠ full recompute (changed=%s)" changedId)
            ))

        // (4) cycle-as-data: a fixed 3-cycle a→b→c→a enumerates as an SCC; `cycleThrough` returns a path.
        let cyclic =
            Map.ofList
                [ "a", Set.singleton "b"
                  "b", Set.singleton "c"
                  "c", Set.singleton "a"
                  "x", Set.singleton "a" ] // acyclic tail reader

        let cyclesResult = Propagation.sort cyclic
        let throughB = Propagation.cycleThrough "b" cyclic

        let cycleOk =
            (cyclesResult.Cycles
             |> List.exists (fun g -> Set.ofList g = Set.ofList [ "a"; "b"; "c" ]))
            && (match throughB with
                | Some g -> Set.ofList g = Set.ofList [ "a"; "b"; "c" ]
                | None -> false)
            && not (List.contains "a" cyclesResult.Order) // a cyclic node is not in the linear Order

        cycle.Check(cycleOk, fun () -> sprintf "cycles=%A through-b=%A" cyclesResult.Cycles throughB)

        LawKit.results [ soundMinimal; frontier; byteIdentity; cycle ]
        @ [ SampleAdequacy.reached
                "Conformance.dirtyPropagationLaws"
                "dirty frontier"
                seed
                [ "dirty node", dirtyNodes; "clean node", cleanNodes ] ]

    // ---- tree-level incremental recompute driver (Phase 69) ----
    // The teeth on `Propagation.evalFrom`: over random acyclic DAGs + a toy pull evaluator, the incremental
    // recompute (reuse clean, re-evaluate dirty) is byte-identical to a full `eval` over the changed inputs,
    // re-evaluates exactly the dirty set (minimality, via an invoked-node recorder), and an out-of-graph
    // change is a named `EvalUnknownChange` envelope.

    /// The incremental-driver laws (Phase 69). Self-contained — each iteration builds a random DAG + a base
    /// value per node, evaluates with `eval`, changes one node's base, then re-evaluates with `evalFrom` and
    /// certifies: **byte-identity** (`evalFrom (eval old) changed` equals a full `eval` over the new bases);
    /// **minimality** (`evalFrom` invokes `evalNode` on exactly the dirty set); **unknown-change envelope**
    /// (a `changed` id absent from the graph is `EvalUnknownChange`, never a throw).
    let propagationEvalLaws (seed: int) (iterations: int) : LawResult list =
        let byteIdentical =
            LawKit.LawCell "evalFrom is byte-identical to a full eval over the changed inputs (every change)"
        // The three laws below are asserted only once the prior model has evaluated — the arm the
        // "node reuse" and "undeclared read" guards count in — so they read through those guards.
        let minimal =
            LawKit.LawCell("evalFrom re-evaluates exactly the dirty set (minimal reuse)", Some "node reuse")

        let unknownChange =
            LawKit.LawCell(
                "a changed id absent from the dependency map is a named EvalUnknownChange (GP5)",
                Some "node reuse"
            )

        let undeclaredRefused =
            LawKit.LawCell(
                "an evaluator that reads outside its declared set is refused by eval and evalFrom alike, naming the node and the read (GP5)",
                Some "undeclared read"
            )
        // Phase 121 — the minimality law is what says work was AVOIDED, and it says nothing at all
        // over a sample in which every node is dirty. Both classes have to arise.
        let mutable dirtyNodes = 0
        let mutable cleanNodes = 0
        // Phase 209 — an undeclared read is refused whether the read names a REAL node of the graph or
        // an id the map does not hold at all, and the law says nothing about a class it never drew.
        // Both are BUILT every iteration rather than drawn, so the guard cannot go vacuous on a short
        // run: node `"0"` declares no reads at all (node k reads only below it), so a real undeclared
        // read is available on every graph this generator can produce.
        let mutable refusedRealNode = 0
        let mutable refusedAbsentId = 0

        // a toy pull evaluator over the DAG: value(n) = base(n) + Σ value(reads). Records the ids it is
        // invoked on (for the minimality assertion).
        let evalNodeWith (baseOf: Map<string, int>) (deps: Map<string, Set<string>>) (invoked: ResizeArray<string>) =
            fun (resolve: string -> int option) (id: string) ->
                invoked.Add id

                let readSum =
                    Map.find id deps
                    |> Set.fold (fun s r -> s + (resolve r |> Option.defaultValue 0)) 0

                Ok(Map.find id baseOf + readSum)

        // The same evaluator with ONE node reading ONE id it never declared — the contract violation
        // the driver refuses since Phase 209. `leakAt` is the node, `leakRead` the id it reaches for.
        let leakyEvalNode
            (baseOf: Map<string, int>)
            (deps: Map<string, Set<string>>)
            (leakAt: string)
            (leakRead: string)
            =
            fun (resolve: string -> int option) (id: string) ->
                let declaredSum =
                    Map.find id deps
                    |> Set.fold (fun s r -> s + (resolve r |> Option.defaultValue 0)) 0

                let leaked =
                    if id = leakAt then
                        resolve leakRead |> Option.defaultValue 0
                    else
                        0

                Ok(Map.find id baseOf + declaredSum + leaked)

        LawKit.run iterations seed (fun rng _ at ->
            let extra = rng.IntBelow 6
            let nNodes = extra + 2
            let ids = [ for k in 0 .. nNodes - 1 -> string k ]

            let deps =
                [ for k in 0 .. nNodes - 1 ->
                      let reads =
                          [ for j in 0 .. k - 1 do
                                let coin = rng.IntBelow 3

                                if coin = 0 then
                                    yield string j ]

                      string k, Set.ofList reads ]
                |> Map.ofList

            let base0 =
                [ for k in 0 .. nNodes - 1 ->
                      let v = rng.IntBelow 100
                      string k, v ]
                |> Map.ofList

            let ck = rng.IntBelow nNodes
            let changedId = string ck
            let base1 = Map.add changedId (Map.find changedId base0 + 1000) base0

            match Propagation.eval (evalNodeWith base0 deps (ResizeArray())) deps with
            | Error e -> byteIdentical.Check(false, fun () -> at (sprintf "prior eval errored: %A" e))
            | Ok prior ->
                let fullInvoked = ResizeArray()
                let incrInvoked = ResizeArray()
                let viaFull = Propagation.eval (evalNodeWith base1 deps fullInvoked) deps

                let viaIncr =
                    Propagation.evalFrom
                        (evalNodeWith base1 deps incrInvoked)
                        prior.Values
                        (Set.singleton changedId)
                        deps

                // (1) byte-identical to a full eval over the changed inputs
                byteIdentical.Check(
                    (viaIncr = viaFull),
                    fun () -> at (sprintf "evalFrom ≠ eval (changed=%s)" changedId)
                )

                // (2) minimal: evalFrom invokes evalNode on exactly the dirty set (all nodes acyclic here)
                let dirty = Propagation.dirtyFromChangedIds deps (Set.singleton changedId)

                minimal.Check(
                    (Set.ofSeq incrInvoked = dirty),
                    fun () -> at (sprintf "invoked=%A ≠ dirty=%A" (Set.ofSeq incrInvoked) dirty)
                )

                dirtyNodes <- dirtyNodes + (ids |> List.filter (fun n -> Set.contains n dirty) |> List.length)

                cleanNodes <-
                    cleanNodes
                    + (ids |> List.filter (fun n -> not (Set.contains n dirty)) |> List.length)

                // (3) unknown-change envelope: a changed id not in the graph is a named error
                match
                    Propagation.evalFrom
                        (evalNodeWith base1 deps (ResizeArray()))
                        prior.Values
                        (Set.singleton "no-such-id")
                        deps
                with
                | Error(Propagation.EvalUnknownChange [ "no-such-id" ]) -> unknownChange.Saw()
                | other ->
                    unknownChange.Check(false, fun () -> at (sprintf "expected EvalUnknownChange, got %A" other))

                // (4) THE DECLARED-READS REFUSAL (Phase 209): an evaluator that reads outside its
                // declaration is refused by BOTH drivers, naming the same node and the same read.
                // Two leaks per iteration — one reaching a real node of the graph, one reaching an id
                // the map does not hold — because the two are different shapes of the same violation
                // and a law that drew only one would say nothing about the other. `evalFrom` is asked
                // with the violating node in its change set, so it recomputes and therefore invokes
                // it: that is the qualifier `evalFrom`'s doc comment states, not a weaker law.
                let leakVerdict (leakAt: string) (leakRead: string) : string option =
                    let expected = Error(Propagation.EvalUndeclaredRead(leakAt, leakRead))
                    let ev = leakyEvalNode base1 deps leakAt leakRead
                    let viaFullLeak = Propagation.eval ev deps
                    let viaIncrLeak = Propagation.evalFrom ev prior.Values (Set.singleton leakAt) deps

                    if viaFullLeak = expected && viaIncrLeak = expected then
                        None
                    else
                        Some(
                            at (
                                sprintf
                                    "node %s reading undeclared %s — eval=%A evalFrom=%A, expected both %A"
                                    leakAt
                                    leakRead
                                    viaFullLeak
                                    viaIncrLeak
                                    expected
                            )
                        )

                match leakVerdict "0" "1" with
                | None ->
                    refusedRealNode <- refusedRealNode + 1
                    undeclaredRefused.Saw()
                | Some why -> undeclaredRefused.Check(false, fun () -> why)

                match leakVerdict changedId "no-such-node" with
                | None ->
                    refusedAbsentId <- refusedAbsentId + 1
                    undeclaredRefused.Saw()
                | Some why -> undeclaredRefused.Check(false, fun () -> why))

        LawKit.results [ byteIdentical; minimal; unknownChange; undeclaredRefused ]
        @ [ SampleAdequacy.reached
                "Conformance.propagationEvalLaws"
                "node reuse"
                seed
                [ "dirty node", dirtyNodes; "clean node", cleanNodes ]
            SampleAdequacy.reached
                "Conformance.propagationEvalLaws"
                "undeclared read"
                seed
                [ "read of a real node", refusedRealNode
                  "read of an id the map does not hold", refusedAbsentId ] ]

    // ---- the propagation contract at a DOMAIN'S evaluator (Phase 211) ----
    // `propagationEvalLaws` above certifies the DRIVER, over a toy evaluator the kit wrote. What the
    // agreement theorem still assumes after Phase 209 is about the EVALUATOR — row
    // `propagation-change-set-and-prior` of `proofs.json` — and an evaluator is the model's parameter,
    // so the only place that assumption can be checked is at the evaluator a domain actually runs.

    /// The evaluator-contract laws (Phase 211) — what is left of `evalfrom_agrees`' premises after
    /// Phase 209 made the declared-reads clause a property of the driver, run at a DOMAIN'S
    /// evaluator over the domain's own generated models and edits. Its green run is the sampled
    /// discharge of `propagation-change-set-and-prior`.
    ///
    /// Three laws, in the order a defect localises:
    ///
    /// - **Purity and determinism.** Two full evaluations of one model over one map are the same
    ///   `Result`; and each node, handed the same resolver answers, returns the same value and asks
    ///   for the same reads in the same order — asked twice in a row, and asked with every node
    ///   visited in reverse. Checked of the model before the edit and of the model after it. An
    ///   evaluator that consults state it does not read through the resolver — a clock, a counter, a
    ///   cache keyed on something else — is caught here and CANNOT be caught by a resolver
    ///   restriction at all, which is why Phase 209's refusal and this family are complements and
    ///   not alternatives. It is first because the two laws below are about comparing evaluations,
    ///   and an evaluation that disagrees with itself makes every comparison a coin toss.
    /// - **Change-set honesty.** Off the ids the domain names, the edited model DECLARES the same
    ///   reads (`Deps` agrees), and its evaluator returns the same result and asks for the same reads
    ///   as the prior one — probed under the prior model's values, the edited model's values, a
    ///   mixture of the two, and no answers at all. That is the theorem's `agree_off` and
    ///   `touches_off` sampled at four resolvers rather than quantified over all of them; and where the
    ///   edit keeps the map, every named id is one the map holds (`evalFrom` refuses an unknown one as
    ///   `EvalUnknownChange`, so such a change cannot be replayed incrementally at all).
    /// - **Agreement.** Where the edit keeps the dependency map and the prior model evaluates, `evalFrom`
    ///   of the edited evaluator over `eval`'s own prior equals `eval` of the edited evaluator — and
    ///   so does `evalFrom` over that prior with HOLES drawn in it, because a prior holding fewer of
    ///   `eval`'s values is the other thing the theorem admits and production recomputes a hole. This
    ///   is the end-to-end consequence the two laws above exist for, sampled where the domain's real
    ///   evaluator lives.
    ///
    /// **Prior provenance is carried by CONSTRUCTION, and the half the kit cannot see is yours.** The
    /// law builds every prior the one way the theorem admits: `eval`'s output over the same map, or a
    /// sub-map of it. Where a prior came from in a running domain — which is the premise's other
    /// clause — is not something a law can observe, so it stays the domain's to keep: **a domain that
    /// persists a `prior` across an edit that MOVES the dependency map must not hand it to `evalFrom`
    /// over the new map**; it re-primes with `eval` over the new map, or refuses to reuse the prior.
    /// That is why an edit that moves the map is checked for honesty and never for agreement here: the
    /// kit declines to certify a replay the contract does not cover.
    ///
    /// **Vacuity, per law, and the guard that measures it.** Purity reaches every node of every model,
    /// so it is vacuous only over empty models. Honesty says nothing where the change set names every
    /// node, and agreement says little where every node is dirty or no evaluation ever fails. The one
    /// guard counts, over the edits that reached the agreement law: an edit that reached a node it
    /// did not name (a READER of a changed node, so dirtiness propagated), a clean node reused from
    /// `prior` (so work was avoided and the reuse was checked), and an edited evaluator that FAILED (so
    /// the `Error` branch of the whole-`Result` comparison was reached). A domain whose edits all move
    /// the map, or whose models never evaluate, reaches none of them and is told so — widen the
    /// generator, which is the only remedy that does not leave the law certified by one trial.
    ///
    /// **Opt-in, not folded into `certify`** — the `keyedChildrenLaws` shape. `certify` takes a tree
    /// witness, and a domain that does not evaluate incrementally has nothing for this family to say.
    let propagationEvaluatorLaws (evw: EvaluatorWitness<'Model, 'V>) (seed: int) (iterations: int) : LawResult list =
        let purity =
            LawKit.LawCell
                "the domain's evaluator is a function of what it reads: repeated and reordered evaluation agree (purity, determinism)"

        let honesty =
            LawKit.LawCell
                "off the change set the domain names, the edit moves neither the declared reads nor the evaluator's results or asked reads (change-set honesty)"
        // Agreement is asserted only where the edit keeps the map and the prior evaluates — the arm
        // the "evaluator edit" guard counts in — so it reads through that guard.
        let agreement =
            LawKit.LawCell(
                "evalFrom of the edited evaluator over eval's own prior, whole and with holes, equals eval over the same map (agreement)",
                Some "evaluator edit"
            )

        let mutable readerReached = 0
        let mutable cleanReused = 0
        let mutable failed = 0
        let mutable movedMap = 0

        /// One node, evaluated under a FIXED set of answers, with the reads it asked for in the order
        /// it asked. The resolver answers any id it holds: this probes the evaluator as a function, and
        /// the driver's restriction to declared reads is `propagationEvalLaws`' to certify.
        let probe (ev: (string -> 'V option) -> string -> Result<'V, string>) (answers: Map<string, 'V>) (id: string) =
            let asked = ResizeArray<string>()

            let resolve k =
                asked.Add k
                Map.tryFind k answers

            let result = ev resolve id
            result, List.ofSeq asked

        let valuesOf (r: Result<Propagation.EvalOutcome<'V>, Propagation.PropagationError>) =
            match r with
            | Ok o -> o.Values
            | Error _ -> Map.empty

        let keysOf (deps: Map<string, Set<string>>) = deps |> Map.toList |> List.map fst

        let purityDefect (i: int) (which: string) ev (deps: Map<string, Set<string>>) : string option =
            let first = Propagation.eval ev deps
            let second = Propagation.eval ev deps

            if first <> second then
                Some(
                    sprintf
                        "seed=%d iter=%d: %s — two full evaluations of the %s model over one map disagree: %A, then %A"
                        seed
                        i
                        evw.Surface
                        which
                        first
                        second
                )
            else
                let answers = valuesOf first
                let ids = keysOf deps
                let forward = ids |> List.map (fun id -> id, probe ev answers id)
                let again = ids |> List.map (fun id -> id, probe ev answers id)

                let backward =
                    ids |> List.rev |> List.map (fun id -> id, probe ev answers id) |> List.rev

                let differs (label: string) (xs: (string * (Result<'V, string> * string list)) list) =
                    List.zip forward xs
                    |> List.tryFind (fun (a, b) -> a <> b)
                    |> Option.map (fun ((id, a), (_, b)) ->
                        sprintf
                            "seed=%d iter=%d: %s — node %s of the %s model, handed the same answers, gave %A (asking %A) and then %A (asking %A) %s"
                            seed
                            i
                            evw.Surface
                            id
                            which
                            (fst a)
                            (snd a)
                            (fst b)
                            (snd b)
                            label)

                match differs "when asked again" again with
                | Some why -> Some why
                | None -> differs "when every node was visited in reverse" backward

        let honestyDefectCore
            (i: int)
            ev0
            ev1
            (deps0: Map<string, Set<string>>)
            (deps1: Map<string, Set<string>>)
            (changed: Set<string>)
            (answerSets: (string * Map<string, 'V>) list)
            : string option =
            // A node the edit REMOVED from the map is not held to naming (Phase 250): nothing
            // evaluates it after the edit, so it cannot go stale, and `evalFrom` over the edited map
            // refuses its id as `EvalUnknownChange` — which is why `Propagation.changedForOp` leaves
            // it out. Its READERS are still held here: they remain in the map, and a change set that
            // omits one is caught by the probes below. An INSERTED node (absent before, present
            // after) is still held to naming, and `changedForOp` names it.
            let unnamed =
                Set.union (Set.ofList (keysOf deps0)) (Set.ofList (keysOf deps1))
                |> Set.filter (fun id -> not (Set.contains id changed) && Map.containsKey id deps1)
                |> Set.toList

            match unnamed |> List.tryFind (fun id -> Map.tryFind id deps0 <> Map.tryFind id deps1) with
            | Some id ->
                Some(
                    sprintf
                        "seed=%d iter=%d: %s — node %s is not in the change set %A, but the edit moved the reads it DECLARES from %A to %A"
                        seed
                        i
                        evw.Surface
                        id
                        (Set.toList changed)
                        (Map.tryFind id deps0)
                        (Map.tryFind id deps1)
                )
            | None ->
                let unknown =
                    if deps0 = deps1 then
                        changed |> Set.filter (fun c -> not (Map.containsKey c deps1)) |> Set.toList
                    else
                        []

                if not (List.isEmpty unknown) then
                    Some(
                        sprintf
                            "seed=%d iter=%d: %s — the change set names %A, which the dependency map does not hold; evalFrom refuses such a change as EvalUnknownChange, so it cannot be replayed incrementally"
                            seed
                            i
                            evw.Surface
                            unknown
                    )
                else
                    [ for id in unnamed do
                          for label, answers in answerSets -> id, label, answers ]
                    |> List.tryPick (fun (id, label, answers) ->
                        let r0, asked0 = probe ev0 answers id
                        let r1, asked1 = probe ev1 answers id

                        if r0 <> r1 || asked0 <> asked1 then
                            Some(
                                sprintf
                                    "seed=%d iter=%d: %s — node %s is not in the change set %A, but under %s the prior evaluator gave %A (asking %A) and the edited one %A (asking %A); a change set that omits a node the edit moved makes evalFrom reuse a stale value there"
                                    seed
                                    i
                                    evw.Surface
                                    id
                                    (Set.toList changed)
                                    label
                                    r0
                                    asked0
                                    r1
                                    asked1
                            )
                        else
                            None)

        /// Phase 302 — two clauses checked ahead of the ones above.
        ///
        /// Every id a node ASKS for is one it declares. An evaluator that reads past its declaration
        /// makes `Deps` — and every dirty set computed from it — a guess, and the probes compare two
        /// evaluations over reads nobody tracks. Checked of both models, under each model's own
        /// values and under no answers at all.
        ///
        /// A node the edit REMOVED is not held to naming, but every reader of it that stays in the
        /// map is: its input vanished, so its value can move although its own declaration did not.
        let honestyDefect
            (i: int)
            ev0
            ev1
            (deps0: Map<string, Set<string>>)
            (deps1: Map<string, Set<string>>)
            (changed: Set<string>)
            (oldValues: Map<string, 'V>)
            (newValues: Map<string, 'V>)
            (answerSets: (string * Map<string, 'V>) list)
            : string option =
            let undeclared =
                [ for which, ev, deps, own in [ "prior", ev0, deps0, oldValues; "edited", ev1, deps1, newValues ] do
                      for answers in [ own; Map.empty ] do
                          for id in keysOf deps do
                              let _, asked = probe ev answers id
                              let declared = Map.tryFind id deps |> Option.defaultValue Set.empty

                              for a in asked do
                                  if not (Set.contains a declared) then
                                      yield which, id, a, declared ]

            let removed = Set.difference (Set.ofList (keysOf deps0)) (Set.ofList (keysOf deps1))

            let readsRemoved (id: string) =
                [ deps0; deps1 ]
                |> List.exists (fun d ->
                    Map.tryFind id d
                    |> Option.exists (fun reads -> not (Set.isEmpty (Set.intersect reads removed))))

            let unnamedReader =
                keysOf deps1
                |> List.tryFind (fun id -> not (Set.contains id changed) && readsRemoved id)

            match undeclared, unnamedReader with
            | (which, id, a, declared) :: _, _ ->
                Some(
                    sprintf
                        "seed=%d iter=%d: %s — node %s of the %s model asked for %s, which its declared reads %A do not hold"
                        seed
                        i
                        evw.Surface
                        id
                        which
                        a
                        (Set.toList declared)
                )
            | [], Some id ->
                Some(
                    sprintf
                        "seed=%d iter=%d: %s — node %s reads %A, which the edit removed, but the change set %A does not name it"
                        seed
                        i
                        evw.Surface
                        id
                        (Set.toList removed)
                        (Set.toList changed)
                )
            | [], None -> honestyDefectCore i ev0 ev1 deps0 deps1 changed answerSets

        LawKit.run iterations seed (fun rng i at ->
            let m0 = rng.Draw evw.Model
            let m1, changed = rng.Draw(evw.Change m0)
            let deps0 = evw.Deps m0
            let deps1 = evw.Deps m1
            let ev0 = evw.EvalNode m0
            let ev1 = evw.EvalNode m1

            // ---- law 1: purity and determinism, of both evaluators ----
            if not purity.Failed then
                match
                    (match purityDefect i "prior" ev0 deps0 with
                     | Some why -> Some why
                     | None -> purityDefect i "edited" ev1 deps1)
                with
                | Some why -> purity.Check(false, fun () -> why)
                | None -> purity.Saw()

            // ---- law 2: change-set honesty ----
            let old = Propagation.eval ev0 deps0
            let full = Propagation.eval ev1 deps1
            let oldValues = valuesOf old
            let newValues = valuesOf full

            let mixed =
                newValues
                |> Map.toList
                |> List.mapi (fun k kv -> k, kv)
                |> List.fold (fun acc (k, (id, v)) -> if k % 2 = 0 then Map.add id v acc else acc) oldValues

            let defect =
                honestyDefect
                    i
                    ev0
                    ev1
                    deps0
                    deps1
                    changed
                    oldValues
                    newValues
                    [ "the prior model's values", oldValues
                      "the edited model's values", newValues
                      "a mixture of the two", mixed
                      "no answers at all", Map.empty ]

            let honest = defect.IsNone

            if not honesty.Failed then
                match defect with
                | Some why -> honesty.Check(false, fun () -> why)
                | None -> honesty.Saw()

            // ---- law 3: agreement, over the priors the theorem admits ----
            let known = changed |> Set.forall (fun c -> Map.containsKey c deps1)

            match old with
            | Ok out0 when deps0 = deps1 && known ->
                let mutable holed = out0.Values

                for id in keysOf deps0 do
                    let coin = rng.IntBelow 3

                    if coin = 0 then
                        holed <- Map.remove id holed

                let exact = Propagation.evalFrom ev1 out0.Values changed deps1
                let viaHoled = Propagation.evalFrom ev1 holed changed deps1

                if exact <> full then
                    agreement.Check(
                        false,
                        fun () ->
                            at (
                                sprintf
                                    "%s — evalFrom over eval's own prior (changed=%A) returned %A, and eval of the edited evaluator %A"
                                    evw.Surface
                                    (Set.toList changed)
                                    exact
                                    full
                            )
                    )
                else
                    agreement.Check(
                        (viaHoled = full),
                        fun () ->
                            at (
                                sprintf
                                    "%s — evalFrom over eval's prior with holes at %A (changed=%A) returned %A, and eval of the edited evaluator %A"
                                    evw.Surface
                                    (keysOf deps0 |> List.filter (fun id -> not (Map.containsKey id holed)))
                                    (Set.toList changed)
                                    viaHoled
                                    full
                            )
                    )

                let dirty = Propagation.dirtyFromChangedIds deps1 changed

                if dirty |> Set.exists (fun d -> not (Set.contains d changed)) then
                    readerReached <- readerReached + 1

                if
                    (Propagation.sort deps1).Order
                    |> List.exists (fun id -> not (Set.contains id dirty) && Map.containsKey id out0.Values)
                then
                    cleanReused <- cleanReused + 1

                match full with
                | Error _ -> failed <- failed + 1
                | Ok _ -> ()
            // Phase 302 — an edit that MOVES the map: `evalFrom` over the edited map, from a prior
            // restricted to the SURVIVORS — the ids the edited map still holds and the change set does
            // not name — with the named ids the edited map holds as the change. Every value such a
            // prior offers is one an honest change set keeps clean, so the replay must agree with
            // `eval`. Counted beside the guard, not demanded: a domain whose edits keep the map has
            // none to offer.
            | Ok out0 when honest ->
                movedMap <- movedMap + 1

                let survivors =
                    out0.Values
                    |> Map.filter (fun id _ -> Map.containsKey id deps1 && not (Set.contains id changed))

                let changed1 = changed |> Set.filter (fun c -> Map.containsKey c deps1)
                let viaSurvivors = Propagation.evalFrom ev1 survivors changed1 deps1

                agreement.Check(
                    (viaSurvivors = full),
                    fun () ->
                        at (
                            sprintf
                                "%s — over an edit that moved the map, evalFrom from the survivors' prior %A (changed=%A) returned %A, and eval of the edited evaluator %A"
                                evw.Surface
                                (survivors |> Map.toList |> List.map fst)
                                (Set.toList changed1)
                                viaSurvivors
                                full
                        )
                )
            | _ -> ())

        LawKit.results [ purity; honesty; agreement ]
        @ [ SampleAdequacy.reachedBeside
                "Conformance.propagationEvaluatorLaws"
                "evaluator edit"
                seed
                [ "change reaching a reader", readerReached
                  "clean node reused from prior", cleanReused
                  "failing evaluator", failed ]
                [ "moved-map edit replayed from its survivors", movedMap ] ]

    // ---- the prior value, at a DOMAIN'S evaluator (Phase 250) ----
    // `Propagation.evalFromWith` hands a recomputed node its own prior value beside its reads, and the
    // restated agreement theorem (`evalfromwith_agrees`, row `propagation-prior-blind` of `proofs.json`)
    // adds ONE premise to `evalfrom_agrees`' — the evaluator's answer does not depend on the prior it is
    // handed. Like the others it is about the evaluator, a parameter of the model, so it is checked here
    // at the evaluator a domain actually runs.

    /// The evaluator-contract laws for a PRIOR-AWARE evaluator (Phase 250): `propagationEvaluatorLaws`
    /// over the domain's reference evaluator (`evw.EvalNode`), and then three laws about the
    /// prior-aware one (`evalNodeWith`) the domain hands `Propagation.evalWith` / `evalFromWith`.
    ///
    /// - **The prior-blind reading is the reference.** Handed no prior, the prior-aware evaluator
    ///   returns what the reference returns and asks for the same reads, at every node of the prior
    ///   and the edited model; and `evalWith` of it equals `eval` of the reference. This is what lets
    ///   the reference laws above speak for the prior-aware evaluator at all.
    /// - **The answer does not depend on the prior (prior discipline).** At every node of the edited
    ///   model that has a prior value, under the answers the full evaluation of the edited model gives
    ///   — which are the answers the incremental walk hands it, by the agreement theorem — the node
    ///   handed its prior returns what it returns handed none, asking for the same reads. The prior is
    ///   the node's own value from `evalWith` of the PRIOR model: the prior the driver really hands,
    ///   carrying whatever reuse state the domain keeps in it. This is the restated theorem's added
    ///   premise, sampled; an evaluator that trusts a prior out of step with its inputs fails here.
    /// - **Agreement, with the prior.** Where the edit keeps the dependency map, `evalFromWith` of the
    ///   edited evaluator over `evalWith`'s own prior — whole and with holes — equals `evalWith` of the
    ///   edited evaluator.
    ///
    /// The theorem's equality is the VALUE TYPE's: a value that carries a reuse cache defines its
    /// equality over what it means, not over the cache, or these laws compare caches.
    ///
    /// **Vacuity.** The guard counts, over the edits that reached the agreement law, a RECOMPUTED node
    /// that was handed a prior (so the prior path was taken, not only the priming one) and a clean
    /// node reused from the prior. A domain whose every edit moves the map reaches neither.
    let propagationEvaluatorLawsWith
        (evw: EvaluatorWitness<'Model, 'V>)
        (evalNodeWith: 'Model -> (string -> 'V option) -> 'V option -> string -> Result<'V, string>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let reference = propagationEvaluatorLaws evw seed iterations

        let blind =
            LawKit.LawCell
                "handed no prior, the prior-aware evaluator is the reference evaluator, and evalWith of it is eval of the reference (prior-blind reading)"

        let discipline =
            LawKit.LawCell
                "at the answers the incremental walk hands it, a node handed its prior returns what it returns handed none (prior discipline)"
        // Agreement is asserted only where the edit keeps the map and the prior evaluates — the arm
        // the "prior-aware edit" guard counts in — so it reads through that guard.
        let agreement =
            LawKit.LawCell(
                "evalFromWith of the edited evaluator over evalWith's own prior, whole and with holes, equals evalWith over the same map (agreement, with the prior)",
                Some "prior-aware edit"
            )

        let mutable priorHanded = 0
        let mutable cleanReused = 0

        let probe (ev: (string -> 'V option) -> string -> Result<'V, string>) (answers: Map<string, 'V>) (id: string) =
            let asked = ResizeArray<string>()

            let resolve k =
                asked.Add k
                Map.tryFind k answers

            let result = ev resolve id
            result, List.ofSeq asked

        let withPrior (m: 'Model) (prior: 'V option) : (string -> 'V option) -> string -> Result<'V, string> =
            fun resolve id -> evalNodeWith m resolve prior id

        let valuesOf (r: Result<Propagation.EvalOutcome<'V>, Propagation.PropagationError>) =
            match r with
            | Ok o -> o.Values
            | Error _ -> Map.empty

        let keysOf (deps: Map<string, Set<string>>) = deps |> Map.toList |> List.map fst

        let blindDefect (i: int) (which: string) (m: 'Model) (deps: Map<string, Set<string>>) : string option =
            let viaReference = Propagation.eval (evw.EvalNode m) deps
            let viaWith = Propagation.evalWith (evalNodeWith m) deps

            if viaReference <> viaWith then
                Some(
                    sprintf
                        "seed=%d iter=%d: %s — over the %s model, evalWith of the prior-aware evaluator returned %A and eval of the reference %A"
                        seed
                        i
                        evw.Surface
                        which
                        viaWith
                        viaReference
                )
            else
                let answers = valuesOf viaReference

                keysOf deps
                |> List.tryPick (fun id ->
                    let a = probe (evw.EvalNode m) answers id
                    let b = probe (withPrior m None) answers id

                    if a <> b then
                        Some(
                            sprintf
                                "seed=%d iter=%d: %s — node %s of the %s model, handed no prior, gave %A (asking %A) where the reference gave %A (asking %A)"
                                seed
                                i
                                evw.Surface
                                id
                                which
                                (fst b)
                                (snd b)
                                (fst a)
                                (snd a)
                        )
                    else
                        None)

        LawKit.run iterations seed (fun rng i at ->
            let m0 = rng.Draw evw.Model
            let m1, changed = rng.Draw(evw.Change m0)
            let deps0 = evw.Deps m0
            let deps1 = evw.Deps m1

            // ---- law 1: the prior-blind reading is the reference ----
            if not blind.Failed then
                match
                    (match blindDefect i "prior" m0 deps0 with
                     | Some why -> Some why
                     | None -> blindDefect i "edited" m1 deps1)
                with
                | Some why -> blind.Check(false, fun () -> why)
                | None -> blind.Saw()

            let old = Propagation.evalWith (evalNodeWith m0) deps0
            let full = Propagation.evalWith (evalNodeWith m1) deps1
            let priorValues = valuesOf old
            let newValues = valuesOf full

            // ---- law 2: the answer does not depend on the prior ----
            if not discipline.Failed then
                match
                    keysOf deps1
                    |> List.tryPick (fun id ->
                        match Map.tryFind id priorValues with
                        | None -> None
                        | Some p ->
                            let handed = probe (withPrior m1 (Some p)) newValues id
                            let none = probe (withPrior m1 None) newValues id

                            if handed <> none then
                                Some(
                                    at (
                                        sprintf
                                            "%s — node %s (changed=%A), handed its prior %A, gave %A (asking %A); handed none it gave %A (asking %A). The prior is a hint for reusing work and may not change the answer"
                                            evw.Surface
                                            id
                                            (Set.toList changed)
                                            p
                                            (fst handed)
                                            (snd handed)
                                            (fst none)
                                            (snd none)
                                    )
                                )
                            else
                                None)
                with
                | Some why -> discipline.Check(false, fun () -> why)
                | None -> discipline.Saw()

            // ---- law 3: agreement, over the priors the theorem admits ----
            let known = changed |> Set.forall (fun c -> Map.containsKey c deps1)

            match old with
            | Ok out0 when deps0 = deps1 && known ->
                let mutable holed = out0.Values

                for id in keysOf deps0 do
                    let coin = rng.IntBelow 3

                    if coin = 0 then
                        holed <- Map.remove id holed

                let exact = Propagation.evalFromWith (evalNodeWith m1) out0.Values changed deps1
                let viaHoled = Propagation.evalFromWith (evalNodeWith m1) holed changed deps1

                if exact <> full then
                    agreement.Check(
                        false,
                        fun () ->
                            at (
                                sprintf
                                    "%s — evalFromWith over evalWith's own prior (changed=%A) returned %A, and evalWith of the edited evaluator %A"
                                    evw.Surface
                                    (Set.toList changed)
                                    exact
                                    full
                            )
                    )
                else
                    agreement.Check(
                        (viaHoled = full),
                        fun () ->
                            at (
                                sprintf
                                    "%s — evalFromWith over evalWith's prior with holes at %A (changed=%A) returned %A, and evalWith of the edited evaluator %A"
                                    evw.Surface
                                    (keysOf deps0 |> List.filter (fun id -> not (Map.containsKey id holed)))
                                    (Set.toList changed)
                                    viaHoled
                                    full
                            )
                    )

                let dirty = Propagation.dirtyFromChangedIds deps1 changed

                if dirty |> Set.exists (fun d -> Map.containsKey d out0.Values) then
                    priorHanded <- priorHanded + 1

                if
                    (Propagation.sort deps1).Order
                    |> List.exists (fun id -> not (Set.contains id dirty) && Map.containsKey id out0.Values)
                then
                    cleanReused <- cleanReused + 1
            | _ -> ())

        reference
        @ LawKit.results [ blind; discipline; agreement ]
        @ [ SampleAdequacy.reached
                "Conformance.propagationEvaluatorLawsWith"
                "prior-aware edit"
                seed
                [ "recomputed node handed a prior", priorHanded
                  "clean node reused from prior", cleanReused ] ]
