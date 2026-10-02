namespace Fuaran.Core

/// The stream families (Phase 297 split): chains, snapshots, DAGs, capture/replay, CAS, idempotency, and the break-reason fixtures.
module internal StreamLaws =

    /// The op-stream laws: `verifyChain` accepts an intact chain and rejects a tampered
    /// op; `replay` re-derives the live state from the base state; and (Phase 301) the chain's JSONL,
    /// written by the checked `tryToJsonl`, reads back to records that verify and re-write identically.
    ///
    /// Phase 245 — `Guarded [ "accepted"; "tampered chain" ]`. Every chain is built from ops the
    /// caller's generator DRAWS, and a drawn op the domain refuses does not extend it. A generator
    /// whose every op is refused leaves every chain empty, and all three laws hold over an empty
    /// chain; one whose every tamper encodes like the op it replaces never runs the tamper law. So
    /// the family counts the accepted ops and the chains it actually tampered, and reports the
    /// guard rather than a pass. (A non-empty chain is exactly a chain with an accepted op in it, so
    /// the second side is the tampered chain the third law reads, not merely a non-empty one.)
    let streamLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let verify = LawKit.LawCell "verifyChain accepts an intact chain"
        let replay = LawKit.LawCell "replay re-derives the live state"

        let tamper =
            LawKit.LawCell("verifyChain detects a tampered op", Some "tampered chain")

        // Phase 301 — linear persistence is part of the base contract: the checked writer accepts
        // every chain the domain builds, and what it writes reads back to records that verify and
        // re-write byte-identically. A domain whose `Encode` emits a line break or whitespace around
        // its value cannot persist linearly, and this cell is where that surfaces.
        let jsonl =
            LawKit.LawCell "the JSONL round trip verifies (tryToJsonl, fromJsonl, verifyChain)"

        let mutable accepted = 0
        let mutable tampered = 0

        LawKit.run iterations seed (fun rng _ at ->
            // a rejected op just doesn't extend the chain — `buildChain` counts the ones that did
            let state, recs, acceptedHere = LawKit.buildChain hashFn sw gen rng
            accepted <- accepted + acceptedHere

            verify.Check(OpStream.verifyChain hashFn sw recs, fun () -> at "an intact chain failed verifyChain")

            match OpStream.tryToJsonl sw recs with
            | Error f ->
                jsonl.Check(
                    false,
                    fun () -> at ("the checked writer refused the chain: " + JsonlWriteFault.toString f)
                )
            | Ok text ->
                match OpStream.fromJsonl sw text with
                | Ok back ->
                    jsonl.Check(
                        OpStream.verifyChain hashFn sw back && OpStream.toJsonl sw back = text,
                        fun () -> at "the chain read back from its JSONL does not verify or re-write identically"
                    )
                | Error e -> jsonl.Check(false, fun () -> at ("the written JSONL did not read back: " + e))

            match OpStream.replay sw gen.State0 recs with
            | Ok s when s = state -> replay.Saw()
            | other -> replay.Check(false, fun () -> at (sprintf "replay≠live state (got %A)" other))

            match recs with
            | [] -> ()
            | _ ->
                let tIdx = rng.IntBelow(List.length recs)
                let orig = List.item tIdx recs

                // Only a genuinely-different op is a tamper the chain must detect; the replacement
                // is redrawn until it encodes differently (Phase 302), and a run that never builds
                // one reds the "tampered chain" guard below.
                match LawKit.drawDistinct rng gen.Op (fun o -> sw.Encode orig.Op <> sw.Encode o) with
                | None -> ()
                | Some newOp ->
                    tampered <- tampered + 1

                    let forged =
                        recs |> List.mapi (fun j r -> if j = tIdx then { r with Op = newOp } else r)

                    tamper.Check(
                        not (OpStream.verifyChain hashFn sw forged),
                        fun () -> at "a tampered op was not detected"
                    ))

        LawKit.results [ verify; replay; tamper; jsonl ]
        @ [ SampleAdequacy.reached "Conformance.streamLaws" "accepted op" seed [ "accepted", accepted ]
            SampleAdequacy.reached "Conformance.streamLaws" "tampered chain" seed [ "tampered", tampered ] ]

    /// Domain-reducer laws (Phase 254) — certify a domain's *own* reducer
    /// `apply : 'Op -> 'State -> Result<'State, 'Rej>` (Doc's `DocOp` apply, Calc's, …), not
    /// just the tree witness. Checks **totality** (`apply` never throws — failures are `'Rej`)
    /// and **replay determinism** (re-applying the accepted ops from `State0` reproduces the
    /// threaded state). An optional `namesAlternatives : 'Rej -> bool` samples the envelope
    /// discipline ("every rejection enumerates the valid alternatives"). `'State` needs
    /// equality. The bridge from witness-level to production-apply conformance.
    let reducer
        (apply: 'Op -> 'State -> Result<'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (namesAlternatives: ('Rej -> bool) option)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let totality = LawKit.LawCell "reducer totality (never throws)"
        let determinism = LawKit.LawCell "reducer replay determinism"

        let envelope =
            LawKit.LawCell("rejection enumerates its alternatives", Some "refused op")
        // Phase 220 — the outcome populations the laws branch on, both DRAWN from the domain's
        // own generator: replay determinism reads the accepted ops, the envelope law reads the
        // refused ones, and totality is the claim that a refusal is TYPED rather than thrown —
        // which a run that never reached a refusal has not tested.
        let mutable acceptedN = 0
        let mutable refusedN = 0

        LawKit.run iterations seed (fun rng _ at ->
            let mutable state = gen.State0
            let mutable accepted = []

            for _ in 0..5 do
                let op = rng.Draw gen.Op

                let res =
                    try
                        Some(apply op state)
                    with _ ->
                        None

                totality.Check(res.IsSome, fun () -> at "apply threw (not a typed rejection)")

                match res with
                | None -> ()
                | Some(Ok st') ->
                    acceptedN <- acceptedN + 1
                    state <- st'
                    accepted <- accepted @ [ op ]
                | Some(Error rej) ->
                    refusedN <- refusedN + 1

                    match namesAlternatives with
                    | Some p -> envelope.Check(p rej, fun () -> at "a rejection did not enumerate its alternatives")
                    | None -> ()

            // replay the accepted ops from State0 — must reproduce the live state
            let replayed =
                (Ok gen.State0, accepted)
                ||> List.fold (fun acc op -> acc |> Result.bind (fun s -> apply op s))

            match replayed with
            | Ok st when st = state -> determinism.Saw()
            | other -> determinism.Check(false, fun () -> at (sprintf "replay ≠ live state (got %A)" other)))

        LawKit.results [ totality; determinism ]
        @ (match namesAlternatives with
           | Some _ -> LawKit.results [ envelope ]
           | None -> [])
        // Phase 220 — `Guarded ["accepted"; "refused"]`, after the subject laws so their positions
        // are unchanged for every caller that reads them by index.
        @ [ SampleAdequacy.reached "Conformance.reducer" "accepted op" seed [ "accepted", acceptedN ]
            SampleAdequacy.reached "Conformance.reducer" "refused op" seed [ "refused", refusedN ] ]

    // ---- opt-in surfaces: snapshot / compaction + the op-DAG (Phase 07) ----
    // `streamLaws` exercises only the linear append/verify/replay path. These certify the two
    // op-stream surfaces a domain *opts into*: snapshot/compaction and the branching op-DAG.
    // They are NOT folded into `certify`/`certifyStream` (which would force a `stateEncode`
    // param and the DAG package on every adopter); a domain that uses snapshots or the DAG
    // calls them alongside its base certification.

    /// Snapshot / compaction laws under an explicit `StreamConfig` (Phase 14): **bounded replay**
    /// (`replayFrom` a compacted checkpoint equals `replay` from origin) and **verifyAcrossWith
    /// accepts an intact boundary** — the stream is built with `appendWith cfg` and the boundary
    /// verified with `verifyAcrossWith cfg`, so a domain on a legacy chain format certifies its own
    /// snapshot path. `'State` needs equality. The pinned `cfg` sits last before `seed`, under the
    /// kit's `…With` rule (Phase 330; it was first until then, and no forward keeps that order).
    let snapshotLawsWith
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (stateEncode: 'State -> string)
        (hashFn: HashFn)
        (cfg: StreamConfig)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let bounded =
            LawKit.LawCell "bounded replay (replayFrom snapshot tail = replay from origin)"

        let across = LawKit.LawCell "verifyAcross accepts an intact (snapshot, tail)"
        // The snapshot arm runs only over a non-empty chain, and whether the chain is non-empty is
        // the generator's doing — a generator whose every op is refused never reaches it, and both
        // cells then report "never reached" rather than green (the runner's evidence count, Phase
        // 302). A census-visible guard NAMING the starved arm is a later widening: it adds a result,
        // which every count-pinning reader of this family sees.

        LawKit.run iterations seed (fun rng _ at ->
            let mutable state = gen.State0
            let mutable recs = OpStream.empty

            for _ in 0..5 do
                let op = rng.Draw gen.Op

                match OpStream.appendWith cfg hashFn sw (Human "conf") op state recs with
                | Ok(s', recs') ->
                    state <- s'
                    recs <- recs'
                | Error _ -> ()

            let len = List.length recs

            if len > 0 then
                let atSeq = rng.IntBelow(len + 1)

                match
                    OpStream.Snapshots.compact SnapshotMode.Strict cfg hashFn stateEncode sw gen.State0 recs atSeq
                with
                | Ok(snap, tail) ->
                    match OpStream.Snapshots.replayFrom sw snap tail, OpStream.replay sw gen.State0 recs with
                    | Ok a, Ok b when a = b -> bounded.Saw()
                    | other ->
                        bounded.Check(false, fun () -> at (sprintf "replayFrom ≠ replay-from-origin (%A)" other))

                    across.Check(
                        OpStream.Snapshots.verify cfg hashFn stateEncode sw snap tail,
                        fun () -> at "verifyAcrossWith rejected an intact (snapshot, tail)"
                    )
                | Error e -> bounded.Check(false, fun () -> at (sprintf "compact failed: %A" e)))

        LawKit.results [ bounded; across ]

    /// Snapshot / compaction laws (Phase 07): **bounded replay** (`replayFrom` a compacted
    /// checkpoint equals `replay` from origin) and **verifyAcross accepts an intact boundary**.
    /// A domain that snapshots a large `'State` runs this with its `stateEncode`. `'State` needs
    /// equality. The canonical-config wrapper over `snapshotLawsWith` (Phase 14).
    let snapshotLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (stateEncode: 'State -> string)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        snapshotLawsWith sw gen stateEncode hashFn OpStream.canonicalConfig seed iterations

    /// Op-DAG laws (Phase 07): **verifyDag accepts an intact DAG**, **replayTo is
    /// deterministic** (the total topo order ⇒ the same head replays to the same state), and
    /// **verifyDag detects a tampered node**. A domain that adopts the branching op-DAG runs
    /// this. `'State` needs equality.
    let dagLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let verify = LawKit.LawCell "verifyDag accepts an intact DAG"
        let determinism = LawKit.LawCell "replayTo is deterministic"
        let tamper = LawKit.LawCell "verifyDag detects a tampered node"
        let roundtrip = LawKit.LawCell "DAG JSONL round-trip preserves the DAG"
        // Phase 296 — the three refusals, each asserted every iteration: a head the DAG does not
        // hold, an id it already holds for a different node (a `HashFn` that collides on demand), and
        // an op the witness rejects at the head's state (`appendChecked` agrees with `Apply` on every
        // drawn op, so the refusal arm is taken whenever the generator draws a rejected op).
        let unknownHead = LawKit.LawCell "tryReplayTo refuses a head the DAG does not hold"

        let collision =
            LawKit.LawCell "append refuses an id the DAG holds for a different node"

        let rejectedOp =
            LawKit.LawCell "appendChecked refuses exactly the ops the witness rejects"
        // Phase 329 — the verified forms, on every drawn append (onto genesis and each of the four
        // nodes) and merge (of the two fork heads): handed the parent's replayed state they answer
        // exactly as the checked forms (and a parent whose replay fails surfaces the replay fault);
        // handed ANOTHER node's state — a fork's sibling, an older node holding a later state, the
        // merge holding one side's, a merge handed either head's — they refuse with `StateMismatch`
        // naming both. The refusal arm is reached only where the two states differ, so a generator
        // whose nodes never differ in state starves it, and the strict cell reds as never reached.
        let verifiedAgrees =
            LawKit.LawCell "appendVerified / mergeVerified answer as the checked forms at the parent's replayed state"

        let verifiedRefuses =
            LawKit.LawCell "appendVerified / mergeVerified refuse another node's state with StateMismatch"
        // The tamper arm runs only when the fresh draw differs from the op it replaces — a
        // generator that keeps drawing the same op never tampers, and `tamper` then reports "never
        // reached" rather than green (Phase 302). A census-visible guard naming the starved arm is
        // a later widening (see `snapshotLawsWith`).

        LawKit.run iterations seed (fun rng _ at ->
            // a fork+merge DAG: genesis g; a, b both children of g; merge m of (a, b)
            let op0 = rng.Draw gen.Op
            let opA = rng.Draw gen.Op
            let opB = rng.Draw gen.Op
            let opM = rng.Draw gen.Op

            let g, a, b, m, dag = LawKit.randomDag hashFn sw false op0 opA opB opM

            verify.Check(Dag.verifyDag hashFn sw dag, fun () -> at "verifyDag rejected an intact DAG")

            // Determinism (Phase 18): build the SAME logical history with a permuted append order
            // (B before A) and confirm it converges to the same content-addressed DAG + head and
            // replays to the same state — a genuine convergence check, not the prior `f x <> f x`
            // self-comparison. (Content addressing is append-order-insensitive, so a regression
            // that leaked insertion order into a node id would diverge here.)
            let _, _, _, m', dag' = LawKit.randomDag hashFn sw true op0 opA opB opM

            determinism.Check(
                not (
                    dag'.Nodes <> dag.Nodes
                    || m' <> m
                    || Dag.tryReplayTo sw gen.State0 dag' m' <> Dag.tryReplayTo sw gen.State0 dag m
                ),
                fun () -> at "a permuted-construction history diverged (nodes/head/replay)"
            )

            // tamper one node's op with a genuinely-different op, redrawn until it encodes
            // differently (Phase 302); the tamper cell is strict, so a run that never builds one
            // reds it rather than reporting a green over nothing.
            let tid, tnode = dag.Nodes |> Map.toList |> List.head

            match LawKit.drawDistinct rng gen.Op (fun o -> sw.Encode tnode.Op <> sw.Encode o) with
            | None -> ()
            | Some newOp ->
                let forged = { Dag.T.Nodes = Map.add tid { tnode with Op = newOp } dag.Nodes }

                tamper.Check(not (Dag.verifyDag hashFn sw forged), fun () -> at "a tampered DAG node was not detected")

            // JSONL persistence round-trip (Phase 01 is shipped)
            match Dag.fromJsonl sw (Dag.toJsonl sw.Encode dag) with
            | Ok dag' when dag'.Nodes = dag.Nodes -> roundtrip.Saw()
            | other -> roundtrip.Check(false, fun () -> at (sprintf "DAG JSONL round-trip ≠ original (%A)" other))

            // Phase 296 — an absent head is refused, never replayed as the initial state.
            let absent = m + "#absent-" + string (rng.IntBelow 1000)

            match Dag.tryReplayTo sw gen.State0 dag absent with
            | Error(Dag.ReplayFault.UnknownHead h) when h = absent -> unknownHead.Saw()
            | other -> unknownHead.Check(false, fun () -> at (sprintf "tryReplayTo of an absent head gave %A" other))

            // Phase 296 — a colliding id is refused and the node held first stays: a `HashFn` that
            // answers the merge node's id for everything makes the next append collide with it.
            let colliding: HashFn = fun _ _ -> m

            match Dag.append colliding sw (Human "collider") tnode.Op m dag with
            | Error(DagAppendFault.ContentIdCollision id) when id = m -> collision.Saw()
            | other ->
                collision.Check(false, fun () -> at (sprintf "an append whose id collides with %s gave %A" m other))

            // Phase 296 — appendChecked applies the op at the head's state: refused exactly when the
            // witness rejects it, the DAG untouched either way on a refusal.
            match Dag.tryReplayTo sw gen.State0 dag m with
            | Ok headState ->
                let probe = rng.Draw gen.Op

                match sw.Apply probe headState, Dag.appendChecked hashFn sw (Human "probe") probe headState m dag with
                | Error _, Error(DagAppendRejection.Domain _)
                | Ok _, Ok _ -> rejectedOp.Saw()
                | applied, checkedAppend ->
                    rejectedOp.Check(
                        false,
                        fun () ->
                            at (
                                sprintf
                                    "Apply gave %A but appendChecked gave %A — the check and the witness disagree"
                                    (Result.isOk applied)
                                    checkedAppend
                            )
                    )
            | Error _ -> ()

            // Phase 329 — the verified forms. Drawn after every arm above, so a recorded seed still
            // reproduces the sample those arms saw.
            let s0 = gen.State0
            let verifier = Human "verifier"
            let probe = rng.Draw gen.Op

            let replayOf (p: string) =
                if p = "" then Ok s0 else Dag.tryReplayTo sw s0 dag p

            // The merge's parents' union closure, without the merge op, in the drain order
            // `tryReplayTo` folds a merge node's closure in: `g`, then the fork heads smallest id
            // first (one head, where the two coincide). Computed here rather than read off the
            // implementation, so the law checks the order the verified merge replays in.
            let unionOfHeads =
                g :: (List.distinct [ a; b ] |> List.sort)
                |> List.fold
                    (fun acc id ->
                        acc
                        |> Result.bind (fun st ->
                            sw.Apply dag.Nodes.[id].Op st
                            |> Result.mapError (fun e -> Dag.ReplayFault.Rejected(id, e))))
                    (Ok s0)

            let agrees
                (what: string)
                (replayed: Result<'State, Dag.ReplayFault<'Rej>>)
                (verified: 'State -> Result<'State * string * Dag.T<'Op>, Dag.VerifiedAppendRejection<'State, 'Rej>>)
                (checkedForm: 'State -> Result<'State * string * Dag.T<'Op>, DagAppendRejection<'Rej>>)
                =
                match replayed with
                | Error fault ->
                    // any handed state: the parent's replay fault is surfaced before it is compared
                    match verified s0 with
                    | Error(Dag.VerifiedAppendRejection.ParentReplay f) when f = fault -> verifiedAgrees.Saw()
                    | other ->
                        verifiedAgrees.Check(
                            false,
                            fun () ->
                                at (sprintf "%s: the parent replays to %A, the verified form gave %A" what fault other)
                        )
                | Ok st ->
                    let v = verified st

                    let c = checkedForm st |> Result.mapError Dag.VerifiedAppendRejection.Checked

                    verifiedAgrees.Check(
                        (v = c),
                        fun () -> at (sprintf "%s at the replayed state: verified %A, checked %A" what v c)
                    )

            let refuses
                (what: string)
                (handed: Result<'State, Dag.ReplayFault<'Rej>>)
                (replayed: Result<'State, Dag.ReplayFault<'Rej>>)
                (verified: 'State -> Result<'State * string * Dag.T<'Op>, Dag.VerifiedAppendRejection<'State, 'Rej>>)
                =
                match handed, replayed with
                | Ok sh, Ok sr when sh <> sr ->
                    match verified sh with
                    | Error(Dag.VerifiedAppendRejection.StateMismatch(h', r')) when h' = sh && r' = sr ->
                        verifiedRefuses.Saw()
                    | other ->
                        verifiedRefuses.Check(
                            false,
                            fun () ->
                                at (sprintf "%s (%A, the parent's is %A): the verified form gave %A" what sh sr other)
                        )
                | _ -> ()

            let appendOnto p =
                fun st -> Dag.appendVerified hashFn sw s0 verifier probe st p dag

            for p in [ ""; g; a; b; m ] do
                agrees
                    (sprintf "an append onto %s" (if p = "" then "genesis" else p))
                    (replayOf p)
                    (appendOnto p)
                    (fun st -> Dag.appendChecked hashFn sw verifier probe st p dag)

            agrees
                "a merge of the fork heads"
                unionOfHeads
                (fun st -> Dag.mergeVerified hashFn sw s0 verifier probe st a b dag)
                (fun st -> Dag.mergeChecked hashFn sw verifier probe st a b dag)

            refuses "an append onto a fork head holding its sibling's state" (replayOf b) (replayOf a) (appendOnto a)
            refuses "an append onto a fork head holding its sibling's state" (replayOf a) (replayOf b) (appendOnto b)

            refuses
                "an append onto the genesis node holding the latest head's state"
                (replayOf m)
                (replayOf g)
                (appendOnto g)

            refuses "an append onto the merge holding one side's state" (replayOf a) (replayOf m) (appendOnto m)

            for side in [ a; b ] do
                refuses "a merge handed one head's state" (replayOf side) unionOfHeads (fun st ->
                    Dag.mergeVerified hashFn sw s0 verifier probe st a b dag))

        LawKit.results
            [ verify
              determinism
              tamper
              roundtrip
              unknownHead
              collision
              rejectedOp
              verifiedAgrees
              verifiedRefuses ]

    /// The questions a reachability index answers, asked one way (Phase 289) — the unindexed functions
    /// on a DAG, or `Dag.Reach` on an index — so two ways can be compared question by question.
    type private ReachAsker<'State, 'Rej> =
        { Ancestors: string -> Set<string>
          Reaches: string -> string -> bool
          Order: string -> Result<string list, string>
          MergeBase: string -> string -> string option
          Between: string -> string -> string list
          Replay: string -> Result<'State, Dag.ReplayFault<'Rej>> }

    /// Reachability-index laws (Phase 289): every answer `Dag.Reach` gives equals the unindexed
    /// function's on the same DAG — ancestors, reachability, topological order, merge base and branch
    /// delta, and the two index-taking forms `tryReplayToWith` / `reconcileManyWith` — and an index
    /// EXTENDED node by node through `appendIndexed` / `mergeIndexed` answers every question as the
    /// index `Reach.ofDag` builds from the finished DAG. Each iteration grows one DAG from the caller's
    /// drawn ops on a kit-drawn shape (appends onto heads and older nodes, merges of two heads, merges
    /// of two arbitrary nodes, a second root) and also asks every question of a BUILT cyclic load with
    /// a dangling parent, where the index answers by the unindexed walk. `'State` and `'Rej` need
    /// equality; ops are compared through `Encode`.
    ///
    /// `Guarded [ "DAG shape" ]` (the Phase 245 guard): a merge base and a branch delta are only
    /// tested by a DAG where two incomparable lanes MERGE — on a chain both are trivial — and whether
    /// the drawn shape holds one depends on what the caller supplies: the same op drawn onto the same
    /// parent is one node (a content-addressed DAG holds the second append as the first), and a step
    /// whose id the caller's hash has already given another node is refused and skipped. So the family
    /// counts the samples holding such a merge, counts the ones without beside them, and a run with
    /// none reds the guard: the merge-base, delta and reconcile laws read as never tested rather than
    /// as passed.
    let reachLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let family = "Conformance.reachLaws"
        let dimension = "DAG shape"
        let ancestorsCell = LawKit.LawCell "Reach.ancestors equals Dag.ancestorsOf"

        let reachesCell =
            LawKit.LawCell "Reach.reaches equals membership in Dag.ancestorsOf"

        let orderCell = LawKit.LawCell "Reach.tryTopoOrder equals Dag.tryTopoOrder"

        let mergeBaseCell =
            LawKit.LawCell("Reach.mergeBase equals Dag.mergeBase", Some dimension)

        let betweenCell = LawKit.LawCell("Reach.between equals Dag.between", Some dimension)

        let replayCell = LawKit.LawCell "tryReplayToWith equals tryReplayTo"

        let reconcileCell =
            LawKit.LawCell("reconcileManyWith equals reconcileMany", Some dimension)

        let extensionCell =
            LawKit.LawCell "an index extended by appendIndexed / mergeIndexed answers as Reach.ofDag of the result"

        let cyclicCell =
            LawKit.LawCell "on a cyclic load with a dangling parent every Reach answer equals the unindexed one"

        let mutable merged = 0
        let mutable unmerged = 0
        let mutable folded = 0
        let mutable refused = 0
        let actor = Human "reach"

        let unindexed (dag: Dag.T<'Op>) : ReachAsker<'State, 'Rej> =
            { Ancestors = Dag.ancestorsOf dag
              Reaches = fun a d -> Set.contains a (Dag.ancestorsOf dag d)
              Order = Dag.tryTopoOrder dag
              MergeBase = Dag.mergeBase dag
              Between = fun b h -> Dag.between dag b h |> List.map (fun n -> n.Id)
              Replay = Dag.tryReplayTo sw gen.State0 dag }

        let indexed (reach: Dag.Reach<'Op>) : ReachAsker<'State, 'Rej> =
            { Ancestors = Dag.Reach.ancestors reach
              Reaches = Dag.Reach.reaches reach
              Order = Dag.Reach.tryTopoOrder reach
              MergeBase = Dag.Reach.mergeBase reach
              Between = fun b h -> Dag.Reach.between reach b h |> List.map (fun n -> n.Id)
              Replay = Dag.tryReplayToWith sw gen.State0 reach }

        // The first question of one kind the two askers answer differently, over `ids` and every
        // ordered pair of them.
        let firstMismatch
            (kind: string)
            (x: ReachAsker<'State, 'Rej>)
            (y: ReachAsker<'State, 'Rej>)
            (ids: string list)
            : string option =
            let single (ask: ReachAsker<'State, 'Rej> -> string -> _) =
                ids
                |> List.tryPick (fun a ->
                    let l, r = ask x a, ask y a

                    if l = r then
                        None
                    else
                        Some(sprintf "%s %s: %A vs %A" kind a l r))

            let pair (ask: ReachAsker<'State, 'Rej> -> string -> string -> _) =
                ids
                |> List.tryPick (fun a ->
                    ids
                    |> List.tryPick (fun b ->
                        let l, r = ask x a b, ask y a b

                        if l = r then
                            None
                        else
                            Some(sprintf "%s %s %s: %A vs %A" kind a b l r)))

            match kind with
            | "ancestors" -> single (fun k a -> k.Ancestors a |> Set.toList)
            | "reaches" -> pair (fun k a b -> k.Reaches a b)
            | "order" -> single (fun k a -> k.Order a)
            | "mergeBase" -> pair (fun k a b -> k.MergeBase a b)
            | "between" -> pair (fun k a b -> k.Between a b)
            | _ -> single (fun k a -> k.Replay a)

        let kinds = [ "ancestors"; "reaches"; "order"; "mergeBase"; "between"; "replay" ]

        let anyMismatch x y ids =
            kinds |> List.tryPick (fun k -> firstMismatch k x y ids)

        // Each op content-writes its own encoding, so two lanes holding the same op interfere and the
        // reconcile laws reach the refusal as well as the fold.
        let footprintOf (op: 'Op) : Footprint =
            { Footprint.empty with
                ContentWrites = Set.singleton (sw.Encode op) }

        let renderReconcile (r: Result<'Op list, ReconcileFault<'Op, 'Rej>>) =
            match r with
            | Ok ops -> Ok(ops |> List.map sw.Encode)
            | Error(ReconcileFault.LanesInterfere cs) ->
                Error(
                    Choice1Of3(
                        cs
                        |> List.map (fun c -> sw.Encode c.Left, sw.Encode c.Right, c.Address, c.Shape)
                    )
                )
            | Error(ReconcileFault.SharedHistoryRejected(n, rej)) -> Error(Choice2Of3(n, rej))
            | Error(ReconcileFault.LanesRejected ls) ->
                Error(
                    Choice3Of3(
                        ls
                        |> List.map (fun l -> l.Head, l.Delta |> List.map sw.Encode, l.NodeId, l.Reject)
                    )
                )

        LawKit.run iterations seed (fun rng i at ->
            // ---- grow a DAG through the indexed forms, a step at a time ----
            let mutable reach = Dag.Reach.ofDag Dag.empty
            let mutable nodes: string list = []

            let note (built: Result<string * Dag.T<'Op> * Dag.Reach<'Op>, DagAppendFault>) =
                match built with
                | Ok(id, _, r) -> Some(id, r)
                | Error _ -> None // a content-id collision under the caller's hash: the step is skipped

            match note (Dag.appendIndexed hashFn sw actor (rng.Draw gen.Op) "" reach) with
            | Some(id, r) ->
                reach <- r
                nodes <- [ id ]
            | None -> ()

            let steps = 2 + rng.IntBelow 13

            for _ in 1..steps do
                let dag = Dag.Reach.dag reach
                let heads = Dag.heads dag
                let op = rng.Draw gen.Op
                let kind = rng.IntBelow 10

                let step =
                    if List.isEmpty nodes || kind = 9 then
                        Dag.appendIndexed hashFn sw actor op "" reach
                    elif kind >= 5 && kind <= 7 && List.length heads >= 2 then
                        let l = rng.Choose heads
                        let r = rng.Choose(heads |> List.filter (fun h -> h <> l))
                        Dag.mergeIndexed hashFn sw actor op l r reach
                    elif kind = 8 then
                        Dag.mergeIndexed hashFn sw actor op (rng.Choose nodes) (rng.Choose nodes) reach
                    else
                        let onto =
                            if rng.IntBelow 2 = 0 then
                                rng.Choose heads
                            else
                                rng.Choose nodes

                        Dag.appendIndexed hashFn sw actor op onto reach

                match note step with
                | Some(id, r) ->
                    reach <- r

                    if not (List.contains id nodes) then
                        nodes <- nodes @ [ id ]
                | None -> ()

            let dag = Dag.Reach.dag reach
            let fresh = Dag.Reach.ofDag dag
            let absent = "absent-" + string i
            let ids = nodes @ [ absent ]

            // ---- the guard's count: a merge of two nodes neither of which reaches the other ----
            let laneMerge =
                dag.Nodes
                |> Map.exists (fun _ n ->
                    match n.Parents with
                    | [ p; q ] ->
                        p <> q
                        && not (Set.contains p (Dag.ancestorsOf dag q))
                        && not (Set.contains q (Dag.ancestorsOf dag p))
                    | _ -> false)

            if laneMerge then
                merged <- merged + 1
            else
                unmerged <- unmerged + 1

            // ---- each indexed answer equals the unindexed one ----
            let truth = unindexed dag
            let built = indexed fresh

            let check (cell: LawKit.LawCell) (kind: string) =
                match firstMismatch kind built truth ids with
                | None -> cell.Saw()
                | Some m -> cell.Check(false, fun () -> at ("indexed vs unindexed, " + m))

            check ancestorsCell "ancestors"
            check reachesCell "reaches"
            check orderCell "order"
            check mergeBaseCell "mergeBase"
            check betweenCell "between"
            check replayCell "replay"

            // ---- the extension law: the index grown step by step answers as the one built whole ----
            match anyMismatch (indexed reach) built ids with
            | None -> extensionCell.Saw()
            | Some m -> extensionCell.Check(false, fun () -> at ("extended vs Reach.ofDag, " + m))

            // ---- reconcileManyWith, over drawn bases and head sets ----
            for _ in 1..4 do
                let baseId = rng.Choose ids
                let heads = [ for _ in 0 .. rng.IntBelow 4 -> rng.Choose ids ]

                let baseState =
                    match Dag.tryReplayTo sw gen.State0 dag baseId with
                    | Ok s -> s
                    | Error _ -> gen.State0

                let l =
                    renderReconcile (Dag.reconcileManyWith sw footprintOf fresh baseId baseState heads)

                let r =
                    renderReconcile (Dag.reconcileMany sw footprintOf dag baseId baseState heads)

                if Result.isOk r then
                    folded <- folded + 1
                else
                    refused <- refused + 1

                reconcileCell.Check(
                    (l = r),
                    fun () -> at (sprintf "reconcile base %s heads %A: %A vs %A" baseId heads l r)
                )

            // ---- a built cyclic load: a two-node cycle with a child under it, a node under a head,
            // and a node naming a parent the DAG does not hold ----
            match nodes with
            | [] -> ()
            | first :: _ ->
                let last = List.last nodes
                let template = dag.Nodes.[first]

                let forgedNode id parents =
                    id,
                    { template with
                        Id = id
                        Parents = parents }

                let forged: Dag.T<'Op> =
                    { Nodes =
                        [ forgedNode "cyc-x" [ "cyc-y"; first ]
                          forgedNode "cyc-y" [ "cyc-x" ]
                          forgedNode "cyc-z" [ "cyc-x" ]
                          forgedNode "cyc-w" [ last ]
                          forgedNode "cyc-d" [ "cyc-missing" ] ]
                        |> List.fold (fun m (k, v) -> Map.add k v m) dag.Nodes }

                let forgedIds = ids @ [ "cyc-x"; "cyc-y"; "cyc-z"; "cyc-w"; "cyc-d"; "cyc-missing" ]
                let forgedReach = Dag.Reach.ofDag forged

                match anyMismatch (indexed forgedReach) (unindexed forged) forgedIds with
                | None -> cyclicCell.Saw()
                | Some m -> cyclicCell.Check(false, fun () -> at ("cyclic load, " + m))

                // extending such an index: under an unorderable parent, under an orderable one, and
                // a node minted under the id the dangling parent names (a hash that answers it)
                let minted: HashFn = fun _ _ -> "cyc-missing"

                let extensions =
                    [ Dag.appendIndexed hashFn sw actor (rng.Draw gen.Op) "cyc-z" forgedReach
                      Dag.appendIndexed hashFn sw actor (rng.Draw gen.Op) last forgedReach
                      Dag.appendIndexed minted sw actor (rng.Draw gen.Op) last forgedReach ]

                for e in extensions do
                    match e with
                    | Ok(_, dag', r') ->
                        let ids' =
                            forgedIds
                            @ (Dag.heads dag' |> List.filter (fun h -> not (List.contains h forgedIds)))

                        match anyMismatch (indexed r') (indexed (Dag.Reach.ofDag dag')) ids' with
                        | None -> extensionCell.Saw()
                        | Some m -> extensionCell.Check(false, fun () -> at ("extended cyclic load, " + m))
                    | Error _ -> ()) // a collision under the caller's hash, as in the growth above

        LawKit.results
            [ ancestorsCell
              reachesCell
              orderCell
              mergeBaseCell
              betweenCell
              replayCell
              reconcileCell
              extensionCell
              cyclicCell ]
        @ [ SampleAdequacy.reachedBeside
                family
                dimension
                seed
                [ "lane merge", merged ]
                [ "no lane merge", unmerged
                  "reconcile folded", folded
                  "reconcile refused", refused ] ]

    /// Checkpoint laws on the lane DAG (Phase 288) — the teeth on `Dag.checkpointAt`, `verifyCheckpoint`,
    /// `replayFrom` / `replayFromWith`, `compactAt` / `compactFrom` and `verifyDagFrom`. Each iteration
    /// grows one DAG from the caller's drawn ops on a kit-drawn shape (appends onto heads and older nodes,
    /// merges of two heads, merges of two arbitrary nodes, a second root) and takes a checkpoint at EVERY
    /// node of it:
    ///
    ///  - **taking** — `checkpointAt` seals the state `tryReplayTo` reaches at the node, and refuses
    ///    exactly where that replay does, with its fault;
    ///  - **the seal** — `verifyCheckpoint` accepts the sealed checkpoint, and refuses it with its seal
    ///    changed, its node moved to another node or to an absent id, and (a separate cell) its state
    ///    swapped for another node's state that encodes differently;
    ///  - **replay equivalence** — for every node and an absent id, `replayFrom` gives `tryReplayTo`'s
    ///    answer from the initial state whenever the checkpoint covers the head, and otherwise the named
    ///    refusal an independent reading of the graph predicts (`UnknownHead`, `Unreached`, or `Uncovered`
    ///    naming the first node above the checkpoint, in replay order, that does not descend from it);
    ///    `replayFromWith` answers as `replayFrom`;
    ///  - **truncation** — `compactAt` refuses exactly a DAG holding a node neither behind nor after the
    ///    checkpoint's node (the first, by id); otherwise the checkpoint is `checkpointAt`'s, the compacted
    ///    DAG keeps the node and everything after it, every node it keeps is the full DAG's, it verifies
    ///    from the checkpoint as the full DAG does, and every node it keeps replays as on the full DAG;
    ///  - **tamper** — a kept node after the checkpoint with its op changed, the checkpoint with its seal
    ///    changed, and the checkpoint with its state swapped each fail `verifyDagFrom`;
    ///  - **lives on** — an append onto the checkpoint's node of the compacted DAG is the full DAG's node
    ///    and replays as it does, an empty history above the checkpoint has its node as the only head, and
    ///    `compactFrom` at any later node answers as `compactAt` of the full DAG there;
    ///  - the lane written beside a sidecar is `toJsonl`'s bytes.
    ///
    /// `'State` and `'Rej` need equality; ops are compared through `Encode`. `stateEncode` must be the
    /// canonical encoder the domain seals with (the strict snapshot's).
    ///
    /// `Guarded [ "DAG shape" ]` (the Phase 245 guard): replay equivalence over a chain says nothing a
    /// linear snapshot does not, and whether the drawn shape holds a branch point depends on what the
    /// caller supplies — the same op drawn onto the same parent is one node. So the family DEMANDS a
    /// covered replay whose history above the checkpoint holds a merge of two incomparable lanes, an
    /// uncovered refusal (a branch from below the checkpoint merging above it), and a compaction that
    /// keeps history above its checkpoint; a run without any of them reds the guard rather than reading
    /// green. Samples with no lane merge, rejected replays, refused compactions and compactions that keep
    /// a band behind the checkpoint are counted beside them.
    let checkpointLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (stateEncode: 'State -> string)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let family = "Conformance.checkpointLaws"
        let dimension = "DAG shape"

        let takeCell =
            LawKit.LawCell
                "checkpointAt seals the state tryReplayTo reaches at its node, and refuses exactly where that replay does"

        let sealCell =
            LawKit.LawCell
                "verifyCheckpoint accepts a sealed checkpoint and refuses it with its seal changed or its node moved"

        let stateSealCell =
            LawKit.LawCell
                "verifyCheckpoint refuses a checkpoint whose state is swapped for one that encodes differently"

        let replayCell =
            LawKit.LawCell
                "replayFrom answers as tryReplayTo for every head the checkpoint covers, and refuses an unknown, unreached or uncovered head by name"

        let indexedCell = LawKit.LawCell "replayFromWith answers as replayFrom"

        let compactCell =
            LawKit.LawCell
                "compactAt refuses exactly a DAG holding a node its checkpoint does not cover; otherwise the compacted DAG verifies from the checkpoint and every node it keeps replays as on the full DAG"

        let tamperCell =
            LawKit.LawCell "a changed node kept by a compaction, or a changed checkpoint, fails verifyDagFrom"

        let livesCell =
            LawKit.LawCell
                "a compacted DAG lives on: an append onto its checkpoint's node replays as on the full DAG, and compacting it again answers as compacting the full DAG"

        let laneCell =
            LawKit.LawCell "the lane written beside a checkpoint sidecar is toJsonl's bytes"

        let mutable coveredMerge = 0
        let mutable uncovered = 0
        let mutable compactedAbove = 0
        let mutable opForged = 0
        let mutable noLaneMerge = 0
        let mutable rejected = 0
        let mutable compactRefused = 0
        let mutable banded = 0
        let actor = Human "checkpoint"

        let asFault (r: Result<'State, Dag.ReplayFault<'Rej>>) : Result<'State, Dag.CheckpointFault<'Rej>> =
            r |> Result.mapError Dag.CheckpointFault.Replay

        let rendered (r: Result<Dag.Checkpoint<'State> * Dag.T<'Op>, Dag.CheckpointFault<'Rej>>) =
            r |> Result.map (fun (cp, d) -> cp, d.Nodes |> Map.toList |> List.map fst)

        LawKit.run iterations seed (fun rng i at ->
            // ---- grow a DAG on a kit-drawn shape ----
            let mutable dag: Dag.T<'Op> = Dag.empty
            let mutable nodes: string list = []

            let add (built: Result<string * Dag.T<'Op>, DagAppendFault>) =
                match built with
                | Ok(id, d) ->
                    dag <- d

                    if not (List.contains id nodes) then
                        nodes <- nodes @ [ id ]
                | Error _ -> () // a content-id collision under the caller's hash: the step is skipped

            add (Dag.append hashFn sw actor (rng.Draw gen.Op) "" dag)

            for _ in 1 .. 2 + rng.IntBelow 11 do
                let heads = Dag.heads dag
                let op = rng.Draw gen.Op
                let kind = rng.IntBelow 12

                if List.isEmpty nodes || kind = 11 then
                    add (Dag.append hashFn sw actor op "" dag)
                elif kind >= 6 && kind <= 9 && List.length heads >= 2 then
                    let l = rng.Choose heads
                    let r = rng.Choose(heads |> List.filter (fun h -> h <> l))
                    add (Dag.merge hashFn sw actor op l r dag)
                elif kind = 10 then
                    add (Dag.merge hashFn sw actor op (rng.Choose nodes) (rng.Choose nodes) dag)
                else
                    let onto =
                        if rng.IntBelow 2 = 0 then
                            rng.Choose heads
                        else
                            rng.Choose nodes

                    add (Dag.append hashFn sw actor op onto dag)

            let full = dag
            let reach = Dag.Reach.ofDag full
            let absent = "absent-" + string i

            let anc = nodes |> List.map (fun id -> id, Dag.ancestorsOf full id) |> Map.ofList

            let replayed =
                nodes
                |> List.map (fun id -> id, Dag.tryReplayTo sw gen.State0 full id)
                |> Map.ofList

            // a merge of two nodes neither of which reaches the other
            let laneMergeIn (ids: string list) =
                ids
                |> List.exists (fun id ->
                    match full.Nodes.[id].Parents with
                    | [ p; q ] -> p <> q && not (anc.[q].Contains p) && not (anc.[p].Contains q)
                    | _ -> false)

            if not (laneMergeIn nodes) then
                noLaneMerge <- noLaneMerge + 1

            // a state another node reaches that encodes differently from `st`
            let otherState (st: 'State) =
                nodes
                |> List.tryPick (fun o ->
                    match replayed.[o] with
                    | Ok s when stateEncode s <> stateEncode st -> Some s
                    | _ -> None)

            // what replayFrom must answer, read from the graph without the function under test
            let expected (c: string) (h: string) : Result<'State, Dag.CheckpointFault<'Rej>> =
                if not (full.Nodes.ContainsKey h) then
                    Error(Dag.CheckpointFault.Replay(Dag.ReplayFault.UnknownHead h))
                elif not (anc.[h].Contains c) then
                    Error(Dag.CheckpointFault.Unreached h)
                else
                    let order =
                        match Dag.tryTopoOrder full h with
                        | Ok o -> o
                        | Error _ -> []

                    match
                        order
                        |> List.filter (fun d -> not (anc.[c].Contains d))
                        |> List.tryFind (fun d -> not (anc.[d].Contains c))
                    with
                    | Some u -> Error(Dag.CheckpointFault.Uncovered u)
                    | None -> asFault replayed.[h]

            // the first node (id order) a compaction at `c` would strand
            let stranded (c: string) =
                full.Nodes
                |> Map.toList
                |> List.map fst
                |> List.tryFind (fun id -> not (anc.[c].Contains id) && not (anc.[id].Contains c))

            for c in nodes do
                match Dag.checkpointAt hashFn stateEncode sw gen.State0 full c with
                | Error f ->
                    takeCell.Check(
                        (match replayed.[c] with
                         | Error g -> f = Dag.CheckpointFault.Replay g
                         | Ok _ -> false),
                        fun () -> at (sprintf "checkpointAt %s refused %A where tryReplayTo gave %A" c f replayed.[c])
                    )
                | Ok cp ->
                    takeCell.Check(
                        (cp.Node = c && Ok cp.State = replayed.[c]),
                        fun () -> at (sprintf "checkpointAt %s sealed %A where tryReplayTo gave %A" c cp replayed.[c])
                    )

                    // ---- the seal ----
                    let verdict (k: Dag.Checkpoint<'State>) =
                        Dag.verifyCheckpoint hashFn stateEncode sw k full

                    sealCell.Check((verdict cp = Ok()), fun () -> at (sprintf "the sealed %A does not verify" cp))

                    let isSeal =
                        function
                        | Error(Dag.CheckpointBreak.Seal _) -> true
                        | _ -> false

                    let reHashed = { cp with Hash = cp.Hash + "0" }

                    sealCell.Check(
                        isSeal (verdict reHashed),
                        fun () -> at (sprintf "a changed seal verifies: %A" reHashed)
                    )

                    for other in nodes |> List.filter (fun o -> o <> c) do
                        let moved = { cp with Node = other }

                        sealCell.Check(
                            isSeal (verdict moved),
                            fun () -> at (sprintf "the checkpoint at %s moved to %s verifies" c other)
                        )

                    sealCell.Check(
                        (verdict { cp with Node = absent } = Error(Dag.CheckpointBreak.UnknownNode absent)),
                        fun () ->
                            at (sprintf "the checkpoint at %s moved to an absent node is not refused as unknown" c)
                    )

                    match otherState cp.State with
                    | Some s ->
                        stateSealCell.Check(
                            isSeal (verdict { cp with State = s }),
                            fun () -> at (sprintf "the checkpoint at %s verifies with the state %A" c s)
                        )
                    | None -> ()

                    // ---- replay equivalence ----
                    for h in nodes @ [ absent ] do
                        let r = Dag.replayFrom sw cp full h
                        let e = expected c h

                        replayCell.Check(
                            (r = e),
                            fun () -> at (sprintf "replayFrom %s to %s: %A, expected %A" c h r e)
                        )

                        let ri = Dag.replayFromWith sw cp reach h

                        indexedCell.Check(
                            (ri = r),
                            fun () -> at (sprintf "replayFromWith %s to %s: %A, replayFrom %A" c h ri r)
                        )

                        match r with
                        | Ok _ when laneMergeIn (Dag.between full c h |> List.map (fun n -> n.Id)) ->
                            coveredMerge <- coveredMerge + 1
                        | Error(Dag.CheckpointFault.Uncovered _) -> uncovered <- uncovered + 1
                        | Error(Dag.CheckpointFault.Replay(Dag.ReplayFault.Rejected _)) -> rejected <- rejected + 1
                        | _ -> ()

                    // ---- truncation ----
                    let later = nodes |> List.filter (fun id -> id <> c && anc.[id].Contains c)

                    match Dag.compactAt hashFn stateEncode sw gen.State0 full c with
                    | Error(Dag.CheckpointFault.Uncovered u) ->
                        compactRefused <- compactRefused + 1

                        compactCell.Check(
                            (stranded c = Some u),
                            fun () ->
                                at (sprintf "compactAt %s refused %s; the first stranded node is %A" c u (stranded c))
                        )
                    | Error other ->
                        compactCell.Check(
                            false,
                            fun () -> at (sprintf "compactAt %s refused %A after checkpointAt took it" c other)
                        )
                    | Ok(cp2, small) ->
                        let sameNode (id: string) (n: DagNode<'Op>) =
                            match Map.tryFind id full.Nodes with
                            | Some m -> m.Parents = n.Parents && m.Actor = n.Actor && sw.Encode m.Op = sw.Encode n.Op
                            | None -> false

                        let kept = small.Nodes |> Map.forall sameNode

                        let keptLater =
                            small.Nodes.ContainsKey c && later |> List.forall small.Nodes.ContainsKey

                        let verifies = Dag.verifyDagFrom hashFn stateEncode sw cp2 small
                        let fullVerifies = Dag.verifyDagFrom hashFn stateEncode sw cp full

                        let replaysAgree =
                            small.Nodes
                            |> Map.toList
                            |> List.forall (fun (h, _) -> Dag.replayFrom sw cp2 small h = Dag.replayFrom sw cp full h)

                        compactCell.Check(
                            (stranded c = None
                             && cp2 = cp
                             && kept
                             && keptLater
                             && verifies
                             && fullVerifies
                             && replaysAgree),
                            fun () ->
                                at (
                                    sprintf
                                        "compactAt %s: stranded %A, same checkpoint %b, nodes the full DAG's %b, keeps the node and its descendants %b, verifies %b, the full DAG verifies %b, replays agree %b"
                                        c
                                        (stranded c)
                                        (cp2 = cp)
                                        kept
                                        keptLater
                                        verifies
                                        fullVerifies
                                        replaysAgree
                                )
                        )

                        if not (List.isEmpty later) then
                            compactedAbove <- compactedAbove + 1

                        if small.Nodes.Count > 1 + List.length later then
                            banded <- banded + 1

                        // ---- tamper ----
                        let fails (k: Dag.Checkpoint<'State>) (d: Dag.T<'Op>) =
                            not (Dag.verifyDagFrom hashFn stateEncode sw k d)

                        match later with
                        | [] -> ()
                        | _ ->
                            let victim = rng.Choose later
                            let n = small.Nodes.[victim]

                            // Phase 302 — redrawn until it encodes differently, and COUNTED: this
                            // cell is also asserted unconditionally below, so without the count a
                            // run that never forged an op read green on the seal arms alone.
                            match LawKit.drawDistinct rng gen.Op (fun o -> sw.Encode o <> sw.Encode n.Op) with
                            | None -> ()
                            | Some fresh ->
                                opForged <- opForged + 1

                                let forged =
                                    { small with
                                        Nodes = Map.add victim { n with Op = fresh } small.Nodes }

                                tamperCell.Check(
                                    fails cp2 forged,
                                    fun () ->
                                        at (sprintf "a kept node %s with its op changed verifies from %s" victim c)
                                )

                        tamperCell.Check(
                            fails { cp2 with Hash = cp2.Hash + "0" } small,
                            fun () -> at (sprintf "the compaction at %s verifies with a changed seal" c)
                        )

                        match otherState cp2.State with
                        | Some s ->
                            tamperCell.Check(
                                fails { cp2 with State = s } small,
                                fun () -> at (sprintf "the compaction at %s verifies with the state %A" c s)
                            )
                        | None -> ()

                        // ---- lives on ----
                        if List.isEmpty later then
                            livesCell.Check(
                                (Dag.heads small = [ c ]),
                                fun () -> at (sprintf "the compaction at %s has heads %A" c (Dag.heads small))
                            )

                        let op = rng.Draw gen.Op

                        match Dag.append hashFn sw actor op c small, Dag.append hashFn sw actor op c full with
                        | Ok(id1, small'), Ok(id2, full') ->
                            let onSmall = Dag.replayFrom sw cp2 small' id1
                            let onFull = asFault (Dag.tryReplayTo sw gen.State0 full' id2)

                            livesCell.Check(
                                (id1 = id2
                                 && onSmall = onFull
                                 && Dag.verifyDagFrom hashFn stateEncode sw cp2 small'),
                                fun () ->
                                    at (
                                        sprintf
                                            "an append onto %s: ids %s / %s, replays %A / %A"
                                            c
                                            id1
                                            id2
                                            onSmall
                                            onFull
                                    )
                            )
                        | Error _, Error _ -> () // a collision under the caller's hash refuses on both
                        | a, b ->
                            livesCell.Check(
                                false,
                                fun () ->
                                    at (
                                        sprintf
                                            "an append onto %s: the compacted DAG gave %A, the full one %A"
                                            c
                                            (Result.map fst a)
                                            (Result.map fst b)
                                    )
                            )

                        for n in later do
                            let viaSmall = rendered (Dag.compactFrom hashFn stateEncode sw cp2 small n)
                            let viaFull = rendered (Dag.compactAt hashFn stateEncode sw gen.State0 full n)

                            livesCell.Check(
                                (viaSmall = viaFull),
                                fun () ->
                                    at (sprintf "compacting %s again at %s: %A; the full DAG: %A" c n viaSmall viaFull)
                            )

                        // ---- the sidecar beside the lane ----
                        let lane, _ = Dag.toJsonlWithCheckpoints sw.Encode stateEncode small [ cp2 ]

                        laneCell.Check(
                            (lane = Dag.toJsonl sw.Encode small),
                            fun () -> at (sprintf "the lane beside the compaction at %s is not toJsonl's" c)
                        ))

        LawKit.results
            [ takeCell
              sealCell
              stateSealCell
              replayCell
              indexedCell
              compactCell
              tamperCell
              livesCell
              laneCell ]
        @ [ SampleAdequacy.reachedBeside
                family
                dimension
                seed
                [ "covered replay over a lane merge", coveredMerge
                  "uncovered refusal", uncovered
                  "compaction keeping history above its checkpoint", compactedAbove
                  "kept node with a forged op", opForged ]
                [ "no lane merge", noLaneMerge
                  "replay rejected", rejected
                  "compaction refused", compactRefused
                  "compaction keeping a band behind its checkpoint", banded ] ]

    /// Lane-store laws (Phase 311) — the teeth on `Dag.loadLanes` / `lanesToJsonl`, `verifyLanes`,
    /// `laneCollisions`, `totalOrderBy` / `totalOrder` / `replayAll`, `mergeAll` / `appendOn`,
    /// `commonBase`, `rehashWith` and `prunable`. Each iteration draws a lane store from the caller's ops:
    /// two to four lanes, each lane one writer (its own actor) whose nodes go onto its own last node, onto
    /// every head of the union (a convergence, the shape a writer that folds before it writes takes), or —
    /// for a lane's first node — onto any node already written. Then:
    ///
    ///  - **load ≡ union** — `loadLanes` of `lanesToJsonl` is the store (nodes and attribution), and its
    ///    union is `fromJsonl` of the lane texts concatenated;
    ///  - **order determinism under lane permutation** — the lane files handed in a shuffled order load
    ///    to the same store, order to the same `totalOrder` and replay to the same `replayAll`;
    ///  - **a linear extension at every key** — `totalOrderBy` places every node once, after its parents,
    ///    at the default key and at a key drawn per node; at a constant key it restricts to every head's
    ///    closure as `tryTopoOrder` of that head;
    ///  - **collision refusal** — `laneCollisions` is `[]` on the drawn store and names a lane handed a
    ///    second, incomparable node, with both tips; `loadLanes` refuses a node a second lane file holds
    ///    with different content, naming both lanes, and keeps one it holds identically;
    ///  - **mergeAll ≡ folded binary merges** — over the store's heads, `appendOn` in a shuffled order
    ///    mints `mergeAll`'s id, and the history below the `mergeAll` node is the history below the last
    ///    of a left fold of binary merges, with the fold's merge nodes removed, in the same drain order;
    ///  - **one common base** — `commonBase` is the same under a shuffle of the heads, is `mergeBase` at
    ///    two, and `Reach.commonBase` answers as it does;
    ///  - **rehashWith round trip** — the store re-minted under SHA-256 verifies there and re-minted back
    ///    under the caller's hash is the store, every id mapped back to itself; a tampered store is
    ///    refused as `Unverified`;
    ///  - **verifyLanes names the lane** — `Ok` on the drawn store, and the lane of a node whose op is
    ///    changed under its id;
    ///  - **retention** — the DAG with `prunable` dropped verifies, and every retained root replays and
    ///    orders as it did.
    ///
    /// `'State` and `'Rej` need equality; ops are compared through `Encode`.
    ///
    /// `Guarded [ "lane shape" ]` (the Phase 245 guard): the permutation, merge and collision laws say
    /// nothing a single chain does not unless the store holds a node whose parents lie in two lanes, a
    /// fork handed to one lane, and a content collision across lane files — and whether a draw produces
    /// them depends on the caller's ops (the same op on the same parent is one node, and a step whose id
    /// the caller's hash has already given another node is refused and skipped). So the family demands
    /// all three, and counts the stores without a cross-lane merge and the folds a collision skipped
    /// beside them.
    let laneLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let family = "Conformance.laneLaws"
        let dimension = "lane shape"

        let loadCell =
            LawKit.LawCell
                "loadLanes of lanesToJsonl is the store, and its union is fromJsonl of the lane texts concatenated"

        let permCell =
            LawKit.LawCell "loadLanes, totalOrder and replayAll are the same under every order of the lane files"

        let orderCell =
            LawKit.LawCell
                "totalOrderBy places every node once after its parents at any key, and at a constant key restricts to tryTopoOrder of every head"

        let collisionCell =
            LawKit.LawCell
                "laneCollisions names exactly a lane holding two incomparable nodes, and loadLanes refuses a node two lanes hold with different content"

        let mergeAllCell =
            LawKit.LawCell(
                "appendOn over the heads in any order mints mergeAll's id, and the history below it is the folded binary merges' without their merge nodes",
                Some dimension
            )

        let baseCell =
            LawKit.LawCell
                "commonBase is order-free, is mergeBase at two heads, and Reach.commonBase answers as it does"

        let rehashCell =
            LawKit.LawCell
                "rehashWith to SHA-256 verifies there and rehashes back to the store; an unverified store is refused"

        let verifyCell =
            LawKit.LawCell "verifyLanes accepts the drawn store and names the lane of a node changed under its id"

        let pruneCell =
            LawKit.LawCell
                "the DAG with prunable dropped verifies, and every retained root replays and orders as it did"

        let mutable crossMerged = 0
        let mutable noCrossMerge = 0
        let mutable forked = 0
        let mutable collided = 0
        let mutable foldSkipped = 0
        let mutable manyHeads = 0

        let render (l: Dag.Loaded<'Op>) = Dag.lanesToJsonl sw.Encode l, l.LaneOf

        let renderLoad (r: Result<Dag.Loaded<'Op>, Dag.LaneLoadFault>) =
            r |> Result.map render |> Result.mapError (sprintf "%A")

        LawKit.run iterations seed (fun rng i at ->
            // ---- draw a lane store ----
            let laneIds = [ for k in 0 .. 1 + rng.IntBelow 3 -> "lane-" + string k ]
            let mutable store: Dag.Loaded<'Op> = { Dag = Dag.empty; LaneOf = Map.empty }
            let mutable tips: Map<string, string> = Map.empty
            let actorOf (lane: string) = Human lane

            let write (lane: string) (parents: string list) =
                match Dag.appendOnLane hashFn sw (actorOf lane) (rng.Draw gen.Op) parents lane store with
                | Ok(id, s) ->
                    store <- s
                    tips <- Map.add lane id tips
                | Error _ -> () // a content-id collision under the caller's hash: the step is skipped

            write (List.head laneIds) []

            for _ in 1 .. 3 + rng.IntBelow 12 do
                let lane = rng.Choose laneIds
                let nodes = store.Dag.Nodes |> Map.toList |> List.map fst

                match Map.tryFind lane tips with
                | None when List.isEmpty nodes -> write lane []
                | None -> write lane [ rng.Choose nodes ]
                | Some tip ->
                    if rng.IntBelow 10 < 6 then
                        write lane [ tip ]
                    else
                        write lane (Dag.heads store.Dag)

            let dag = store.Dag
            let ids = dag.Nodes |> Map.toList |> List.map fst
            let heads = Dag.heads dag

            let crossLane =
                dag.Nodes
                |> Map.exists (fun _ n ->
                    n.Parents
                    |> List.choose (fun p -> Map.tryFind p store.LaneOf)
                    |> List.distinct
                    |> List.length
                    >= 2)

            if crossLane then
                crossMerged <- crossMerged + 1
            else
                noCrossMerge <- noCrossMerge + 1

            let texts = Dag.lanesToJsonl sw.Encode store

            // ---- load ≡ union ----
            let reloaded = Dag.loadLanes sw texts

            let concatenated =
                Dag.fromJsonl sw (texts |> List.map snd |> List.filter (fun t -> t <> "") |> String.concat "\n")
                |> Result.map (Dag.toJsonl sw.Encode)

            loadCell.Check(
                renderLoad reloaded = Ok(render store)
                && concatenated = Ok(Dag.toJsonl sw.Encode dag),
                fun () ->
                    at (sprintf "lanes %A: reloaded %A, concatenated %A" texts (renderLoad reloaded) concatenated)
            )

            // ---- order determinism under lane permutation ----
            let state0 = gen.State0
            let order0 = Dag.totalOrder store
            let replay0 = Dag.replayAll sw state0 store

            for _ in 1..3 do
                let shuffled = rng.Shuffle texts

                match Dag.loadLanes sw shuffled with
                | Ok l ->
                    permCell.Check(
                        render l = render store
                        && Dag.totalOrder l = order0
                        && Dag.replayAll sw state0 l = replay0,
                        fun () -> at (sprintf "lane files in the order %A" (shuffled |> List.map fst))
                    )
                | Error f -> permCell.Check(false, fun () -> at (sprintf "a shuffled load refused: %A" f))

            // ---- a linear extension at every key ----
            let drawn = ids |> List.map (fun id -> id, rng.IntBelow 4) |> Map.ofList

            let isExtension (order: Result<string list, Dag.TotalOrderFault>) =
                match order with
                | Error _ -> false
                | Ok o ->
                    let pos = o |> List.mapi (fun k id -> id, k) |> Map.ofList

                    List.length o = Map.count dag.Nodes
                    && Map.count pos = List.length o
                    && o
                       |> List.forall (fun id ->
                           dag.Nodes.[id].Parents
                           |> List.forall (fun p ->
                               match Map.tryFind p pos with
                               | Some pp -> pp < pos.[id]
                               | None -> not (dag.Nodes.ContainsKey p)))

            let constant = Dag.totalOrderBy (fun _ -> 0) dag

            let restricts =
                match constant with
                | Error _ -> false
                | Ok o ->
                    heads
                    |> List.forall (fun h ->
                        let c = Dag.ancestorsOf dag h
                        Dag.tryTopoOrder dag h = Ok(o |> List.filter (fun id -> Set.contains id c)))

            orderCell.Check(
                isExtension order0
                && isExtension (Dag.totalOrderBy (fun (n: DagNode<'Op>) -> drawn.[n.Id]) dag)
                && isExtension constant
                && restricts,
                fun () -> at (sprintf "order %A, constant-key order %A" order0 constant)
            )

            // ---- collision refusal ----
            let clean = Dag.laneCollisions store

            collisionCell.Check(
                List.isEmpty clean,
                fun () -> at (sprintf "a one-head-per-lane store reported %A" clean)
            )

            let forkLane = rng.Choose(tips |> Map.toList)

            match forkLane with
            | lane, tip ->
                let tipNode = dag.Nodes.[tip]

                match LawKit.drawDistinct rng gen.Op (fun op -> sw.Encode op <> sw.Encode tipNode.Op) with
                | Some op ->
                    match Dag.appendOnLane hashFn sw tipNode.Actor op tipNode.Parents lane store with
                    | Ok(sibling, forkedStore) when sibling <> tip && not (dag.Nodes.ContainsKey sibling) ->
                        forked <- forked + 1
                        let reported = Dag.laneCollisions forkedStore

                        let expected =
                            [ lane, List.sortWith (fun a b -> System.String.CompareOrdinal(a, b)) [ tip; sibling ] ]

                        collisionCell.Check(
                            (reported = expected),
                            fun () ->
                                at (sprintf "a fork of %s in lane %s: expected %A, got %A" tip lane expected reported)
                        )
                    | _ -> ()
                | None -> ()

                // a second lane file holding a node of this lane: identically, and with its op changed
                let held = tipNode
                let copy = "zz-copy", Dag.toJsonl sw.Encode { Nodes = Map.ofList [ held.Id, held ] }

                collisionCell.Check(
                    renderLoad (Dag.loadLanes sw (texts @ [ copy ])) = Ok(render store),
                    fun () -> at (sprintf "a node held identically by a second lane file changed the store")
                )

                match LawKit.drawDistinct rng gen.Op (fun op -> sw.Encode op <> sw.Encode held.Op) with
                | Some op ->
                    collided <- collided + 1

                    let forged =
                        "zz-forged", Dag.toJsonl sw.Encode { Nodes = Map.ofList [ held.Id, { held with Op = op } ] }

                    let expected =
                        Error(
                            sprintf
                                "%A"
                                (Dag.LaneLoadFault.Collision(held.Id, [ store.LaneOf.[held.Id]; "zz-forged" ]))
                        )

                    let got = renderLoad (Dag.loadLanes sw (rng.Shuffle(texts @ [ forged ])))

                    collisionCell.Check(
                        (got = expected),
                        fun () ->
                            at (
                                sprintf
                                    "a node %s held with different content: expected %A, got %A"
                                    held.Id
                                    expected
                                    got
                            )
                    )
                | None -> ()

            // ---- mergeAll ≡ folded binary merges ----
            if List.length heads >= 2 then
                manyHeads <- manyHeads + 1
                let op = rng.Draw gen.Op
                let actor = Human "merge"

                match
                    Dag.mergeAll hashFn sw actor op heads dag, Dag.appendOn hashFn sw actor op (rng.Shuffle heads) dag
                with
                | Ok(m, dm), Ok(m', _) ->
                    let hs = rng.Shuffle heads

                    let folded =
                        (Ok(List.head hs, dag, []), List.tail hs)
                        ||> List.fold (fun acc h ->
                            acc
                            |> Result.bind (fun (left, d, merges) ->
                                Dag.merge hashFn sw actor op left h d
                                |> Result.map (fun (id, d') -> id, d', id :: merges)))

                    match folded with
                    | Ok(last, df, merges) ->
                        let below (d: Dag.T<'Op>) (top: string) (drop: string list) =
                            Dag.tryTopoOrder d top
                            |> Result.map (List.filter (fun id -> not (List.contains id drop)))

                        let viaAll = below dm m [ m ]
                        let viaFold = below df last merges

                        mergeAllCell.Check(
                            m = m' && viaAll = viaFold && Result.isOk viaAll,
                            fun () ->
                                at (
                                    sprintf
                                        "heads %A: mergeAll %s, appendOn %s; below %A vs %A"
                                        heads
                                        m
                                        m'
                                        viaAll
                                        viaFold
                                )
                        )
                    | Error _ -> foldSkipped <- foldSkipped + 1
                | _ -> foldSkipped <- foldSkipped + 1

            // ---- one common base ----
            let baseHeads = [ for _ in 0 .. rng.IntBelow 4 -> rng.Choose(ids @ [ "absent" ]) ]
            let cb = Dag.commonBase dag baseHeads
            let reach = Dag.Reach.ofDag dag

            let atTwo =
                match baseHeads |> List.distinct with
                | [ a; b ] -> cb = Dag.mergeBase dag a b
                | _ -> true

            baseCell.Check(
                Dag.commonBase dag (rng.Shuffle baseHeads) = cb
                && Dag.Reach.commonBase reach baseHeads = cb
                && atTwo,
                fun () -> at (sprintf "heads %A: commonBase %A" baseHeads cb)
            )

            // ---- rehashWith round trip ----
            match Dag.rehashWith hashFn OpStream.sha256Hash sw dag with
            | Ok(there, ids1) ->
                let back = Dag.rehashWith OpStream.sha256Hash hashFn sw there

                let roundTrip =
                    match back with
                    | Ok(d, ids2) ->
                        Dag.toJsonl sw.Encode d = Dag.toJsonl sw.Encode dag
                        && ids |> List.forall (fun id -> ids2.[ids1.[id]] = id)
                    | Error _ -> false

                rehashCell.Check(
                    Dag.verifyDag OpStream.sha256Hash sw there && roundTrip,
                    fun () ->
                        at (
                            sprintf
                                "rehash round trip: %A"
                                (back |> Result.map fst |> Result.map (Dag.toJsonl sw.Encode))
                        )
                )
            | Error f -> rehashCell.Check(false, fun () -> at (sprintf "a verified store refused: %A" f))

            // ---- verifyLanes names the lane ----
            verifyCell.Check(
                Dag.verifyLanes hashFn sw store = Ok(),
                fun () -> at (sprintf "the drawn store does not verify: %A" (Dag.verifyLanes hashFn sw store))
            )

            let victim = rng.Choose ids
            let vNode = dag.Nodes.[victim]

            match LawKit.drawDistinct rng gen.Op (fun op -> sw.Encode op <> sw.Encode vNode.Op) with
            | Some op ->
                let tampered =
                    { store with
                        Dag = { Nodes = Map.add victim { vNode with Op = op } dag.Nodes } }

                let lane = store.LaneOf.[victim]

                verifyCell.Check(
                    (match Dag.verifyLanes hashFn sw tampered with
                     | Error b -> b.Lane = lane && b.Break.NodeId = victim
                     | Ok() -> false),
                    fun () ->
                        at (sprintf "node %s of lane %s changed: %A" victim lane (Dag.verifyLanes hashFn sw tampered))
                )

                rehashCell.Check(
                    (match Dag.rehashWith hashFn OpStream.sha256Hash sw tampered.Dag with
                     | Error(Dag.RehashFault.Unverified b) -> b.NodeId = victim
                     | _ -> false),
                    fun () -> at (sprintf "a tampered store was re-minted, node %s" victim)
                )
            | None -> ()

            // ---- retention ----
            let roots =
                match rng.Shuffle heads with
                | [] -> []
                | hs -> List.truncate (1 + rng.IntBelow(List.length hs)) hs

            match Dag.prunable reach roots with
            | Error r -> pruneCell.Check(false, fun () -> at (sprintf "a held root %s was refused" r))
            | Ok dropped ->
                let kept =
                    roots
                    |> List.fold (fun acc r -> Set.union acc (Dag.ancestorsOf dag r)) Set.empty

                let pruned: Dag.T<'Op> =
                    { Nodes = dag.Nodes |> Map.filter (fun id _ -> not (List.contains id dropped)) }

                pruneCell.Check(
                    (dropped = (ids |> List.filter (fun id -> not (Set.contains id kept))))
                    && Dag.verifyDag hashFn sw pruned
                    && roots
                       |> List.forall (fun r ->
                           Dag.tryReplayTo sw state0 pruned r = Dag.tryReplayTo sw state0 dag r
                           && Dag.tryTopoOrder pruned r = Dag.tryTopoOrder dag r)
                    && Dag.prunable reach [ "absent-root" ] = Error "absent-root",
                    fun () -> at (sprintf "roots %A dropped %A" roots dropped)
                ))

        LawKit.results
            [ loadCell
              permCell
              orderCell
              collisionCell
              mergeAllCell
              baseCell
              rehashCell
              verifyCell
              pruneCell ]
        @ [ SampleAdequacy.reachedBeside
                family
                dimension
                seed
                [ "cross-lane merge", crossMerged
                  "two or more heads", manyHeads
                  "fork handed to one lane", forked
                  "content collision across lane files", collided ]
                [ "no cross-lane merge", noCrossMerge
                  "merge fold skipped on a collision", foldSkipped ] ]

    /// The determinism-capture / replay laws (Phase 27) — the teeth on `OpStream.captureEffect` /
    /// `replayEffect`. A domain supplies a value `Codec` (`encode`/`decode`) and a `draw` of a
    /// realized effect value (the stand-in for a live non-deterministic source); the kit certifies:
    ///
    ///  - **exact replay** — recording a non-deterministic session (`captureEffect` over a sequence
    ///    of drawn values), then replaying it (`replayEffect` over the journal, with a live source
    ///    that would now draw *different* values), reproduces the recorded values **byte-identically**
    ///    and fully consumes the journal — proving replay feeds back the captured value rather than
    ///    re-reading the source;
    ///  - **deterministic pass-through** — a `Deterministic` effect emits no capture and replay
    ///    re-evaluates the live source (the journal is untouched);
    ///  - **tamper-evidence** — a tampered captured value fails `verifyCaptures`;
    ///  - **persistence** (Phase 301) — the journal's JSONL, written by the checked
    ///    `tryCaptureToJsonl`, reads back to a journal that `verifyCaptures` and re-writes identically.
    ///
    /// `'v` needs equality (it compares recorded vs replayed values). Opt-in like `snapshotLaws` /
    /// `dagLaws` — it carries a value codec + generator the base `certify` does not, so a domain
    /// that journals impure effects calls it alongside its base certification.
    let captureReplayLaws
        (encode: 'v -> string)
        (decode: string -> Result<'v, string>)
        (draw: ConfRng.T -> 'v * ConfRng.T)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let exact =
            LawKit.LawCell "replay-with-capture is byte-identical to the recorded session"

        let deterministic =
            LawKit.LawCell "a deterministic effect emits no capture (replay re-evaluates live)"

        let tamper =
            LawKit.LawCell("verifyCaptures detects a tampered capture", Some "tampered")

        let multiIdentity =
            LawKit.LawCell "replayEffect enforces effect-identity order (a misordered replay is a named error)"

        // Phase 301 — the journal persists: the checked writer accepts it, and what it writes reads
        // back to a journal that verifies and re-writes byte-identically. A value codec whose output
        // carries a line break or whitespace around the value fails here.
        let jsonl =
            LawKit.LawCell "the capture JSONL round trip verifies (tryCaptureToJsonl, captureFromJsonl, verifyCaptures)"
        // The tamper arm runs only when the fresh draw encodes differently from the capture it
        // replaces — a `draw` that keeps yielding the same value never tampers.
        let mutable tampered = 0

        LawKit.run iterations seed (fun rng _ at ->
            // record a non-deterministic session: 1..5 effects, each a fresh draw, journalled.
            let k = rng.IntBelow 5
            let mutable captures = []
            let mutable recorded = []

            for _ in 0..k do
                let v = rng.Draw draw
                // the recorded "live source" draws v; captureEffect journals it under "random".
                let got, caps' =
                    OpStream.captureEffect hashFn encode "random" "eff" (fun () -> v) captures

                captures <- caps'
                recorded <- recorded @ [ got ]

            // replay with a live source that would draw DIFFERENT values — a capture hit must win.
            let mutable cursor = captures
            let mutable replayed = []
            let mutable replayOk = true

            for _ in recorded do
                // a divergent live fallback: if replay ever re-evaluated, it would diverge from orig.
                let liveDifferent () = rng.Draw draw

                match OpStream.replayEffect decode "eff" "random" liveDifferent cursor with
                | Ok(v, rest) ->
                    cursor <- rest
                    replayed <- replayed @ [ v ]
                | Error _ -> replayOk <- false

            exact.Check(
                not (
                    not replayOk
                    || replayed <> recorded
                    || not (List.isEmpty cursor)
                    // byte-identity: each replayed value re-encodes to the journalled value.
                    || (List.zip replayed captures |> List.exists (fun (v, c) -> encode v <> c.Value))
                ),
                fun () -> at "replay-with-capture ≠ recorded session"
            )

            match OpStream.tryCaptureToJsonl captures with
            | Error f ->
                jsonl.Check(
                    false,
                    fun () -> at ("the checked writer refused the journal: " + JsonlWriteFault.toString f)
                )
            | Ok text ->
                match OpStream.captureFromJsonl text with
                | Ok back ->
                    jsonl.Check(
                        OpStream.verifyCaptures hashFn back && OpStream.captureToJsonl back = text,
                        fun () -> at "the journal read back from its JSONL does not verify or re-write identically"
                    )
                | Error e -> jsonl.Check(false, fun () -> at ("the written journal did not read back: " + e))

            // deterministic pass-through: no capture emitted, replay re-evaluates live, journal intact.
            let dv = rng.Draw draw

            let dGot, dCaps =
                OpStream.captureEffect hashFn encode OpStream.deterministicTag "eff" (fun () -> dv) []

            let dReplay =
                OpStream.replayEffect decode "eff" OpStream.deterministicTag (fun () -> dv) dCaps

            deterministic.Check(
                not (dGot <> dv || not (List.isEmpty dCaps) || dReplay <> Ok(dv, [])),
                fun () -> at "a deterministic effect was captured or altered replay"
            )

            // tamper: replace a captured value with a genuinely-different encoding ⇒ verifyCaptures fails.
            match captures with
            | [] -> ()
            | _ ->
                let tIdx = rng.IntBelow(List.length captures)
                let orig = List.item tIdx captures

                // Phase 302 — redrawn until the replacement encodes differently.
                match LawKit.drawDistinct rng draw (fun v -> encode v <> orig.Value) with
                | None -> ()
                | Some newV ->
                    let newValue = encode newV
                    tampered <- tampered + 1

                    let forged =
                        captures
                        |> List.mapi (fun j c -> if j = tIdx then { c with Value = newValue } else c)

                    tamper.Check(
                        not (OpStream.verifyCaptures hashFn forged),
                        fun () -> at "a tampered capture was not detected"
                    ))

        // multi-identity guard (Phase 40): a journal of two distinct effect identities replays
        // correctly in record order, but a replay that requests the wrong identity at the head
        // surfaces a *named* mismatch rather than silently handing back the other effect's value.
        let fixedDraws = LawKit.Draws(seed + 7)
        let v1 = fixedDraws.Draw draw
        let v2 = fixedDraws.Draw draw
        let _, c1 = OpStream.captureEffect hashFn encode "clock" "alpha" (fun () -> v1) []
        let _, caps = OpStream.captureEffect hashFn encode "random" "beta" (fun () -> v2) c1

        // in record order: alpha then beta — both hit their captures byte-identically.
        let inOrder =
            match OpStream.replayEffect decode "alpha" "clock" (fun () -> v1) caps with
            | Ok(a, rest) ->
                match OpStream.replayEffect decode "beta" "random" (fun () -> v2) rest with
                | Ok(b, []) -> encode a = c1.Head.Value && encode b = (List.item 1 caps).Value
                | _ -> false
            | _ -> false

        // out of order: requesting beta while the head is alpha must be a named error.
        let misordered =
            match OpStream.replayEffect decode "beta" "random" (fun () -> v2) caps with
            | Error msg -> msg.Contains "identity mismatch"
            | Ok _ -> false

        multiIdentity.Check(
            inOrder && misordered,
            fun () -> sprintf "seed=%d: replayEffect did not enforce effect-identity order" seed
        )

        LawKit.results [ exact; deterministic; tamper; multiIdentity; jsonl ]
        @ [ SampleAdequacy.reached "Conformance.captureReplayLaws" "tampered" seed [ "tampered", tampered ] ]

    /// The compare-and-append (CAS) laws (Phase 79) — certify `OpStream.appendIf` is a sound
    /// optimistic-concurrency primitive over a domain's `StreamWitness`. Three properties:
    /// **match ≡ append** (`appendIf` with the stream's *true* head produces exactly what `append`
    /// produces — same state, same chained records — and forwards a domain rejection as
    /// `AppendRejection.Domain`); **stale rejection** (`appendIf` with a head that does NOT match is
    /// `Error (AppendRejection.StaleHead (expected, actual))` naming the actual head, and the stream is
    /// left unchanged — no partial write); **race serialisation** (two `appendIf` calls that both
    /// captured one base head, committed in either order, yield exactly one success and one `StaleHead`
    /// — the CAS admits a single winner under any serialisation). Seed-replayable; a counterexample
    /// carries the seed + iteration. `'State` / `'Op` / `'Rej` need equality.
    let casLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let matchLaw = LawKit.LawCell "appendIf with the true head ≡ append"

        let staleLaw =
            LawKit.LawCell "appendIf with a stale head rejects, naming the actual head (stream unchanged)"

        let raceLaw =
            LawKit.LawCell(
                "two racing appendIf calls off one base admit exactly one winner under any serialisation",
                Some "race arm"
            )

        let actor = Human "conf"
        // Phase 223 — the match arm's two outcome populations, both DRAWN from the caller's
        // StreamGen: `match ≡ append` compares a domain refusal with a CAS `Domain` rejection only
        // when the drawn op is refused, and compares two accepted appends only when it is not.
        let mutable accepted = 0
        let mutable refused = 0
        // The race arm runs only when BOTH drawn writers apply against the base — a generator
        // that refuses often enough leaves the race law with nothing to serialise.
        let mutable races = 0

        LawKit.run iterations seed (fun rng _ at ->
            // Build a random base chain (as streamLaws does) — the CAS is exercised against its head.
            let state, recs, _ = LawKit.buildChain hashFn sw gen rng

            let baseHead = OpStream.head recs

            // ---- match ≡ append ----
            let opM = rng.Draw gen.Op
            let viaAppend = OpStream.append hashFn sw actor opM state recs
            let viaCas = OpStream.appendIf hashFn sw baseHead actor opM state recs

            (match viaAppend with
             | Ok _ -> accepted <- accepted + 1
             | Error _ -> refused <- refused + 1)

            let matchOk =
                match viaAppend, viaCas with
                | Ok a, Ok b -> a = b
                | Error e, Error(AppendRejection.Domain e2) -> e = e2
                | _ -> false

            matchLaw.Check(matchOk, fun () -> at "appendIf(trueHead) ≢ append")

            // ---- stale head rejects, naming the actual head; stream unchanged ----
            let opS = rng.Draw gen.Op
            let staleHead = baseHead + "!" // guaranteed ≠ baseHead

            match OpStream.appendIf hashFn sw staleHead actor opS state recs with
            | Error(AppendRejection.StaleHead(expected, actual)) when expected = staleHead && actual = baseHead ->
                staleLaw.Saw() // recs is an immutable value the caller still holds — there is no partial write
            | other ->
                staleLaw.Check(
                    false,
                    fun () -> at (sprintf "appendIf(staleHead) did not name the actual head (got %A)" other)
                )

            // ---- race: exactly one of two writers off one base head wins, either order ----
            let opA = rng.Draw gen.Op
            let opB = rng.Draw gen.Op

            // A genuine CAS race needs both ops to individually apply against the base — a domain
            // reject is not a CAS outcome, so skip the race check for that iteration.
            match OpStream.append hashFn sw actor opA state recs, OpStream.append hashFn sw actor opB state recs with
            | Ok _, Ok _ ->
                races <- races + 1

                // The first writer commits against the base head → succeeds and advances the chain; the
                // second still holds baseHead as its expectation → StaleHead. Exactly one winner.
                let serialise first second =
                    match OpStream.appendIf hashFn sw baseHead actor first state recs with
                    | Ok(s1, recs1) ->
                        match OpStream.appendIf hashFn sw baseHead actor second s1 recs1 with
                        | Error(AppendRejection.StaleHead _) -> true
                        | _ -> false
                    | _ -> false

                raceLaw.Check(
                    serialise opA opB && serialise opB opA,
                    fun () -> at "two racing appendIf calls did not admit exactly one winner"
                )
            | _ -> ())

        LawKit.results [ matchLaw; staleLaw; raceLaw ]
        // Phase 223 — `Guarded ["accepted"; "refused"]`, after the subject laws so their positions
        // are unchanged. A StreamGen that never draws a refused op leaves `match ≡ append`
        // certified on the accept path alone, and green.
        @ [ SampleAdequacy.reached "Conformance.casLaws" "accepted op" seed [ "accepted", accepted ]
            SampleAdequacy.reached "Conformance.casLaws" "refused op" seed [ "refused", refused ]
            SampleAdequacy.reached "Conformance.casLaws" "race arm" seed [ "both apply", races ] ]

    // ---- idempotent append (Phase 82) ----
    // The at-least-once claim the agent retry loop rests on: a re-sent invocation key converges
    // on its earlier entry instead of double-applying, the idempotency guard adds nothing to the
    // chain (a fresh-key append is byte-identical to plain `append`), and the caller-threaded
    // `KeyIndex` is honest (rebuilding from the stream agrees with incremental maintenance).

    /// The idempotent-append laws (Phase 82) — certify `OpStream.appendIdempotent` (and the
    /// combined Phase 79 composition `appendIdempotentIf`) over a domain's `StreamWitness`. Four
    /// properties: **fresh ≡ append** (a fresh-key `appendIdempotent` produces exactly what
    /// `append` produces — same state, same chained records, so the stream is chain-identical —
    /// and the returned index is the old index plus exactly the new entry; a domain rejection is
    /// forwarded verbatim); **duplicate convergence** (re-sending a seen key is
    /// `AppendOutcome.Duplicate` naming the entry the key *first* produced — `Seq` + `Hash` — and
    /// the caller's stream is untouched); **rebuild parity** (`KeyIndex.ofStream keyOf` over the
    /// resulting stream equals the incrementally-maintained index, at every prefix the loop
    /// builds); and **idempotency-before-CAS** (`appendIdempotentIf` with a seen key converges on
    /// `Duplicate` even under a stale head — the lost-ack retry terminates; a fresh key under a
    /// stale head is `StaleHead`; a fresh key under the true head ≡ `append`). `keyOf` is the
    /// domain's op → invocation-key projection (the Phase 27 `Function.invocationKey` shape) —
    /// the laws only exercise keys `keyOf` yields, so a projection that collides distinct ops
    /// treats them as retries of one invocation (the caller's contract, certified as given).
    /// Seed-replayable; a counterexample carries the seed + iteration. `'State` / `'Op` / `'Rej`
    /// need equality.
    let idempotencyLaws
        (keyOf: 'Op -> string)
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let freshLaw =
            LawKit.LawCell
                "appendIdempotent with a fresh key ≡ append (chain-identical; index gains exactly the new entry)"

        let dupLaw =
            LawKit.LawCell
                "re-appending a seen key is Duplicate naming the entry the key first produced (stream untouched)"

        let parityLaw =
            LawKit.LawCell "KeyIndex.ofStream agrees with the incrementally-maintained index (rebuild parity)"

        let casLaw =
            LawKit.LawCell
                "idempotency precedes the CAS (a seen key converges under any head; a fresh key CASes as appendIf)"

        let actor = Human "conf"
        // Phase 223 — the fresh-key arms' outcome populations, DRAWN from the caller's StreamGen:
        // `fresh ≡ append` and the true-head CAS arm forward a domain refusal verbatim only when the
        // drawn op is refused, which is a property of the run.
        let mutable accepted = 0
        let mutable refused = 0
        let mutable freshAttempts = 0
        let mutable freshBuilt = 0
        // The gated arms — the two fresh-key arms run only when an op whose key is not already in the
        // index can be drawn, and the seen-key arm (duplicate convergence + the stale-head retry) only
        // over a non-empty chain, all three decided by what the generator drew — are held by the
        // runner's evidence count: an arm never reached reports its law "never reached" rather than
        // green. Since Phase 302 the fresh-key arms are also MEASURED: they redraw (bounded) until
        // the key is fresh, and the guard demands they were built on at least half their attempts.

        LawKit.run iterations seed (fun rng _ at ->
            // Build a base chain THROUGH appendIdempotent, threading (state, records, index) — a
            // generated op whose key is already seen (or whose apply rejects) extends nothing,
            // which is itself the primitive under test. Rebuild parity is checked at every step.
            let mutable state = gen.State0
            let mutable recs = OpStream.empty
            let mutable index = KeyIndex.empty

            for _ in 0..5 do
                let op = rng.Draw gen.Op

                match OpStream.appendIdempotent hashFn sw (keyOf op) actor op state index recs with
                | Ok(AppendOutcome.Appended(s', recs', idx')) ->
                    state <- s'
                    recs <- recs'
                    index <- idx'
                | Ok(AppendOutcome.Duplicate _)
                | Error _ -> ()

                parityLaw.Check(
                    KeyIndex.ofStream keyOf recs = index,
                    fun () -> at "ofStream ≠ the incrementally-maintained index"
                )

            let baseHead = OpStream.head recs

            // ---- fresh ≡ append (chain-identity + index extended by exactly the new entry) ----
            // Phase 302 — the fresh op is redrawn until its key is unseen, and the arm's reach is
            // measured against its attempts: a run that built it once in two hundred tries read
            // adequate when the guard asked only for one.
            freshAttempts <- freshAttempts + 1

            match LawKit.drawDistinct rng gen.Op (fun o -> (KeyIndex.tryFind (keyOf o) index).IsNone) with
            | None -> ()
            | Some opF ->
                freshBuilt <- freshBuilt + 1
                let viaAppend = OpStream.append hashFn sw actor opF state recs

                let viaIdem =
                    OpStream.appendIdempotent hashFn sw (keyOf opF) actor opF state index recs

                (match viaAppend with
                 | Ok _ -> accepted <- accepted + 1
                 | Error _ -> refused <- refused + 1)

                let freshOk =
                    match viaAppend, viaIdem with
                    | Ok(sA, recsA), Ok(AppendOutcome.Appended(sI, recsI, idxI)) ->
                        let last = List.last recsA

                        sA = sI
                        && recsA = recsI
                        && idxI = KeyIndex.add (keyOf opF) { Seq = last.Seq; Hash = last.Hash } index
                    | Error e, Error e2 -> e = e2
                    | _ -> false

                freshLaw.Check(freshOk, fun () -> at "appendIdempotent(fresh key) ≢ append")

            // ---- duplicate convergence: a seen key names the entry it FIRST produced ----
            if not (List.isEmpty recs) then
                let pick = rng.IntBelow(List.length recs)
                let key = keyOf (List.item pick recs).Op
                let opD = rng.Draw gen.Op

                // Phase 302 — a `keyOf` that is not a function (two calls on one op disagree) used
                // to throw here; it is a counterexample now.
                match recs |> List.tryFind (fun r -> keyOf r.Op = key) with
                | None ->
                    dupLaw.Check(
                        false,
                        fun () ->
                            at (
                                sprintf
                                    "keyOf is not a function: the key %s of record %d matches no record when recomputed"
                                    key
                                    pick
                            )
                    )
                | Some first ->
                    match OpStream.appendIdempotent hashFn sw key actor opD state index recs with
                    | Ok(AppendOutcome.Duplicate existing) when existing.Seq = first.Seq && existing.Hash = first.Hash ->
                        dupLaw.Saw() // recs/index are immutable values the caller still holds — the stream is byte-identical
                    | other ->
                        dupLaw.Check(
                            false,
                            fun () -> at (sprintf "a seen key did not converge on its first entry (got %A)" other)
                        )

                    // ---- idempotency-before-CAS: the lost-ack retry converges under a stale head ----
                    let staleHead = baseHead + "!" // guaranteed ≠ baseHead

                    match OpStream.appendIdempotentIf hashFn sw key staleHead actor opD state index recs with
                    | Ok(AppendOutcome.Duplicate existing) when existing.Seq = first.Seq && existing.Hash = first.Hash ->
                        casLaw.Saw()
                    | other ->
                        casLaw.Check(
                            false,
                            fun () ->
                                at (
                                    sprintf
                                        "a seen key under a stale head did not converge on Duplicate (got %A)"
                                        other
                                )
                        )

            // ---- fresh key through the CAS: stale head refuses; the true head ≡ append ----
            freshAttempts <- freshAttempts + 1

            match LawKit.drawDistinct rng gen.Op (fun o -> (KeyIndex.tryFind (keyOf o) index).IsNone) with
            | None -> ()
            | Some opC ->
                freshBuilt <- freshBuilt + 1
                let staleHead = baseHead + "!"

                match OpStream.appendIdempotentIf hashFn sw (keyOf opC) staleHead actor opC state index recs with
                | Error(AppendRejection.StaleHead(expected, actual)) when expected = staleHead && actual = baseHead ->
                    casLaw.Saw()
                | other ->
                    casLaw.Check(
                        false,
                        fun () -> at (sprintf "a fresh key under a stale head was not StaleHead (got %A)" other)
                    )

                let viaAppend = OpStream.append hashFn sw actor opC state recs

                let viaBoth =
                    OpStream.appendIdempotentIf hashFn sw (keyOf opC) baseHead actor opC state index recs

                (match viaAppend with
                 | Ok _ -> accepted <- accepted + 1
                 | Error _ -> refused <- refused + 1)

                let matchOk =
                    match viaAppend, viaBoth with
                    | Ok(sA, recsA), Ok(AppendOutcome.Appended(sI, recsI, _)) -> sA = sI && recsA = recsI
                    | Error e, Error(AppendRejection.Domain e2) -> e = e2
                    | _ -> false

                casLaw.Check(matchOk, fun () -> at "appendIdempotentIf(fresh key, true head) ≢ append"))

        LawKit.results [ freshLaw; dupLaw; parityLaw; casLaw ]
        // Phase 223 — `Guarded ["accepted"; "refused"]`, after the subject laws. A StreamGen that
        // never draws a refused fresh op leaves the verbatim-forwarding half of `fresh ≡ append`
        // and of the true-head CAS arm certified by nothing, and green.
        // Phase 302 — the thresholds are fractions: the fresh arms must be BUILT on at least half of
        // their attempts, and each outcome must be at least one in twenty of the arms built.
        @ [ SampleAdequacy.reachedFraction
                "Conformance.idempotencyLaws"
                "accepted fresh op"
                seed
                [ "accepted", accepted, freshBuilt, 20
                  "fresh-key arm built", freshBuilt, freshAttempts, 2 ]
            SampleAdequacy.reachedFraction
                "Conformance.idempotencyLaws"
                "refused fresh op"
                seed
                [ "refused", refused, freshBuilt, 20 ] ]

    /// **Every reason this library MINTS is a named case** (Phase 125) — the law that makes
    /// `ChainBreakReason.Unrecognised` an honest arm rather than a hedge.
    ///
    /// `ChainBreak.Reason` became a closed DU so a consumer stops re-deriving the type by
    /// string-matching this library's spellings. That only helps if the walkers actually stay
    /// inside the named cases: a walker that minted an `Unrecognised` would hand every consumer
    /// back exactly the untyped string the type exists to remove, and nothing would say so. So this
    /// family drives BOTH walkers — the op walk (`firstChainBreakWith`) and the capture walk
    /// (`firstCaptureBreak`) — into EVERY break each can produce, and asserts the reason is named.
    ///
    /// The three breaks are BUILT each iteration rather than drawn, so the sample cannot miss one:
    /// a sequence is renumbered, a prev-link is repointed, and a payload is tampered with its
    /// sequence and prev-link left intact so the cheap checks pass and the digest check is the one
    /// that fires. The final law is the non-vacuity guard — each break kind was actually observed —
    /// because "no unnamed reason was minted" is trivially true of a walk that never broke.
    ///
    /// The `toString` / `ofString` pair is certified here too, in both directions: the round trip is
    /// the identity on the named cases, and an unknown string lands in `Unrecognised` VERBATIM
    /// rather than being swept into the nearest-looking case — which is the defect the consumer's
    /// pre-typed form had, and the reason this type is worth its breaking change.
    let chainBreakReasonLaws (seed: int) (iterations: int) : LawResult list =
        // The kit's own int-op witness: the claim is about THIS library's walkers, not about a
        // host's, so there is no caller witness to take.
        let sw = LawKit.intWitness

        let hashFn = OpStream.defaultHash

        let unnamed =
            LawKit.LawCell "every reason the chain walkers mint is a NAMED ChainBreakReason case"

        let roundTrip =
            LawKit.LawCell "ChainBreakReason.ofString (toString r) = r on every named case"

        let verbatim =
            LawKit.LawCell "ChainBreakReason.ofString carries an unknown reason into Unrecognised verbatim"

        let opWalk =
            LawKit.LawCell "non-vacuity: the op walk produced every break kind it can produce"

        let captureWalk =
            LawKit.LawCell "non-vacuity: the capture walk produced every break kind it can produce"

        let mutable seenOp = Set.empty
        let mutable seenCapture = Set.empty

        // The reason a break carries, or None when the walk found the chain intact — which is
        // itself a defect here, since every input below is deliberately broken.
        let reasonOf (label: string) (at: string -> string) (b: ChainBreak option) : ChainBreakReason option =
            match b with
            | Some br ->
                // Qualified since Phase 147: `DagBreakReason` declares an `Unrecognised` too, and
                // both are in scope here. The .NET compiler resolves this from the scrutinee's type;
                // FABLE does not, and reported it as an error on a tree .NET had built clean.
                (match br.Reason with
                 | ChainBreakReason.Unrecognised s ->
                     unnamed.Check(
                         false,
                         fun () ->
                             at (
                                 sprintf
                                     "the %s walk minted an unnamed reason %s — a walker inside this library must stay inside the named cases, or ChainBreakReason gives a consumer back the untyped string it exists to remove"
                                     label
                                     s
                             )
                     )
                 | _ -> unnamed.Saw())

                Some br.Reason
            | None ->
                unnamed.Check(
                    false,
                    fun () ->
                        at (
                            sprintf
                                "the %s walk reported NO break over a deliberately broken chain, so this family is measuring nothing"
                                label
                        )
                )

                None

        LawKit.run iterations seed (fun rng _ at ->
            // ---- a sound op chain of four records ----
            let mutable state = 0
            let mutable recs = OpStream.empty

            for _ in 0..3 do
                let op = rng.IntBelow 50

                match OpStream.append hashFn sw (Human "conf") (op + 1) state recs with
                | Ok(s', recs') ->
                    state <- s'
                    recs <- recs'
                | Error _ -> ()

            let len = List.length recs

            if len >= 2 then
                // sequence: renumber the last record so the contiguity counter disagrees first.
                let renumbered =
                    recs
                    |> List.mapi (fun j r -> if j = len - 1 then { r with Seq = r.Seq + 7 } else r)

                match
                    reasonOf "op" at (OpStream.firstChainBreakWith OpStream.canonicalConfig hashFn sw renumbered)
                with
                | Some r -> seenOp <- Set.add (ChainBreakReason.toString r) seenOp
                | None -> ()

                // prev-link: repoint the last record's PrevHash, leaving its Seq correct.
                let repointed =
                    recs
                    |> List.mapi (fun j r ->
                        if j = len - 1 then
                            { r with PrevHash = r.PrevHash + "x" }
                        else
                            r)

                match reasonOf "op" at (OpStream.firstChainBreakWith OpStream.canonicalConfig hashFn sw repointed) with
                | Some r -> seenOp <- Set.add (ChainBreakReason.toString r) seenOp
                | None -> ()

                // digest: tamper the OP only. Seq and PrevHash still agree, so the two cheap checks
                // pass and the hash recomputation is what fails — the only way to reach that arm.
                let tampered =
                    recs
                    |> List.mapi (fun j r -> if j = len - 1 then { r with Op = r.Op + 1000 } else r)

                match reasonOf "op" at (OpStream.firstChainBreakWith OpStream.canonicalConfig hashFn sw tampered) with
                | Some r -> seenOp <- Set.add (ChainBreakReason.toString r) seenOp
                | None -> ()

            // ---- a sound capture log of three captures, then the same three breaks ----
            let mutable caps = []

            for k in 0..2 do
                let v, caps' =
                    OpStream.captureEffect hashFn string "nondeterministic" ("eff" + string k) (fun () -> k * 3) caps

                ignore v
                caps <- caps'

            let clen = List.length caps

            if clen >= 2 then
                let capRenumbered =
                    caps
                    |> List.mapi (fun j c -> if j = clen - 1 then { c with Seq = c.Seq + 7 } else c)

                match reasonOf "capture" at (OpStream.firstCaptureBreak hashFn capRenumbered) with
                | Some r -> seenCapture <- Set.add (ChainBreakReason.toString r) seenCapture
                | None -> ()

                let capRepointed =
                    caps
                    |> List.mapi (fun j c ->
                        if j = clen - 1 then
                            { c with PrevHash = c.PrevHash + "x" }
                        else
                            c)

                match reasonOf "capture" at (OpStream.firstCaptureBreak hashFn capRepointed) with
                | Some r -> seenCapture <- Set.add (ChainBreakReason.toString r) seenCapture
                | None -> ()

                let capTampered =
                    caps
                    |> List.mapi (fun j c -> if j = clen - 1 then { c with Value = c.Value + "9" } else c)

                match reasonOf "capture" at (OpStream.firstCaptureBreak hashFn capTampered) with
                | Some r -> seenCapture <- Set.add (ChainBreakReason.toString r) seenCapture
                | None -> ()

            // ---- the string pair, both directions ----
            for named in
                [ ChainBreakReason.SequenceMismatch
                  ChainBreakReason.PrevHashLinkBroken
                  ChainBreakReason.HashMismatch ] do
                roundTrip.Check(
                    ChainBreakReason.ofString (ChainBreakReason.toString named) = named,
                    fun () ->
                        at (
                            sprintf
                                "ofString (toString %A) = %A — the rendering and the parse disagree, so a consumer reading a logged reason back does not recover the case that wrote it"
                                named
                                (ChainBreakReason.ofString (ChainBreakReason.toString named))
                        )
                )

            let alien = rng.IntBelow 1000
            let alienText = "a reason this library does not mint #" + string alien

            match ChainBreakReason.ofString alienText with
            | ChainBreakReason.Unrecognised s when s = alienText -> verbatim.Saw()
            | other ->
                verbatim.Check(
                    false,
                    fun () ->
                        at (
                            sprintf
                                "ofString %s = %A — an unknown reason must land in Unrecognised carrying its own text, never be swept into a named case, which is a claim about which check failed that nothing established"
                                alienText
                                other
                        )
                ))

        // The capture walk spells the digest failure differently; `toString` renders one spelling
        // for the single `HashMismatch` case, so both walks are expected to have observed the same
        // three strings.
        let expected =
            [ ChainBreakReason.toString ChainBreakReason.SequenceMismatch
              ChainBreakReason.toString ChainBreakReason.PrevHashLinkBroken
              ChainBreakReason.toString ChainBreakReason.HashMismatch ]
            |> Set.ofList

        let missing (seen: Set<string>) =
            Set.difference expected seen |> Set.toList |> String.concat ", "

        opWalk.Check(
            Set.isEmpty (Set.difference expected seenOp),
            fun () ->
                "the op walk never reported: "
                + missing seenOp
                + " — the laws above hold vacuously for the break kinds that were never produced"
        )

        captureWalk.Check(
            Set.isEmpty (Set.difference expected seenCapture),
            fun () ->
                "the capture walk never reported: "
                + missing seenCapture
                + " — the laws above hold vacuously for the break kinds that were never produced"
        )

        LawKit.results [ unnamed; roundTrip; verbatim; opWalk; captureWalk ]

    /// **Every reason the DAG walker MINTS is a named case** (Phase 147) — the sibling of
    /// `chainBreakReasonLaws`, and the law that makes `DagBreakReason.Unrecognised` an honest arm
    /// rather than a hedge.
    ///
    /// `DagBreak.Reason` became a closed DU so a consumer stops re-deriving the type by
    /// string-matching this library's spellings. That only helps if `Dag.firstBreak` actually stays
    /// inside the named cases: a walker that minted an `Unrecognised` would hand every consumer back
    /// exactly the untyped string the type exists to remove, and nothing would say so. So this family
    /// drives the walker into EVERY break it can produce and asserts the reason is named.
    ///
    /// The two breaks are BUILT each iteration rather than drawn, so the sample cannot miss one: a
    /// node's op is tampered while its map KEY is left alone (which is what makes the stored id
    /// disagree with the recomputed one), and a node another node NAMES is deleted. The deletion has
    /// to be a deletion rather than a re-pointed parent, because re-pointing changes the pre-image
    /// and the walker reports the content-id mismatch first — so `MissingParent` is unreachable by
    /// tampering a node's own fields. The last two laws are the non-vacuity guards — each break kind
    /// was actually observed — because "no unnamed reason was minted" is trivially true of a walk
    /// that never broke.
    ///
    /// The `toString` / `ofString` pair is certified here too, in both directions: the round trip is
    /// the identity on the named cases, and an unknown string lands in `Unrecognised` VERBATIM rather
    /// than being swept into the nearer-looking case. `toString` is also what keeps
    /// `Dag.fromJsonlVerified`'s error bytes unchanged across this type's introduction, so the round
    /// trip is a compatibility claim and not only a tidiness one.
    let dagBreakReasonLaws (seed: int) (iterations: int) : LawResult list =
        // The kit's own int-op witness: the claim is about THIS library's walker, not about a
        // host's, so there is no caller witness to take.
        let sw = LawKit.intWitness

        let hashFn = OpStream.defaultHash

        let unnamed =
            LawKit.LawCell "every reason the DAG walker mints is a NAMED DagBreakReason case"

        let roundTrip =
            LawKit.LawCell "DagBreakReason.ofString (toString r) = r on every named case"

        let verbatim =
            LawKit.LawCell "DagBreakReason.ofString carries an unknown reason into Unrecognised verbatim"

        let dagWalk =
            LawKit.LawCell "non-vacuity: the DAG walk produced every break kind it can produce"

        let mutable seen = Set.empty

        // The reason a break carries, or None when the walk found the DAG intact — which is itself a
        // defect here, since every input below is deliberately broken.
        let reasonOf (label: string) (at: string -> string) (b: DagBreak option) : DagBreakReason option =
            match b with
            | Some br ->
                // Qualified: `ChainBreakReason` declares an `Unrecognised` too, and both are in
                // scope here — the sibling family below is the reason this file sees both.
                (match br.Reason with
                 | DagBreakReason.Unrecognised s ->
                     unnamed.Check(
                         false,
                         fun () ->
                             at (
                                 sprintf
                                     "the %s walk minted an unnamed reason %s — the walker inside this library must stay inside the named cases, or DagBreakReason gives a consumer back the untyped string it exists to remove"
                                     label
                                     s
                             )
                     )
                 | _ -> unnamed.Saw())

                Some br.Reason
            | None ->
                unnamed.Check(
                    false,
                    fun () ->
                        at (
                            sprintf
                                "the %s walk reported NO break over a deliberately broken DAG, so this family is measuring nothing"
                                label
                        )
                )

                None

        LawKit.run iterations seed (fun rng _ at ->
            // ---- a sound DAG: genesis, two children, a merge — every node shape the walker meets ----
            let op0 = rng.IntBelow 50
            let opA = rng.IntBelow 50
            let opB = rng.IntBelow 50
            let opM = rng.IntBelow 50

            let g, _, _, _, dag =
                LawKit.randomDag hashFn sw false op0 (opA + 1) (opB + 1) (opM + 1)

            // content id: tamper the OP and leave the map KEY exactly as it was. That is the threat
            // the content id exists to catch, and it is precisely not a rewrite.
            let tid, tnode = dag.Nodes |> Map.toList |> List.head

            let tampered =
                { Dag.T.Nodes = Map.add tid { tnode with Op = tnode.Op + 1000 } dag.Nodes }

            match reasonOf "content-id" at (Dag.firstBreak hashFn sw tampered) with
            | Some r -> seen <- Set.add (DagBreakReason.toString r) seen
            | None -> ()

            // missing parent: delete the genesis node that `a` and `b` both name. Every surviving
            // node's id still recomputes from its own fields, so the content-id check passes and the
            // parent check is the one that fires — the only way to reach that arm.
            let orphaned = { Dag.T.Nodes = Map.remove g dag.Nodes }

            match reasonOf "missing-parent" at (Dag.firstBreak hashFn sw orphaned) with
            | Some r -> seen <- Set.add (DagBreakReason.toString r) seen
            | None -> ()

            // ---- the string pair, both directions ----
            for named in [ DagBreakReason.ContentIdMismatch; DagBreakReason.MissingParent ] do
                roundTrip.Check(
                    DagBreakReason.ofString (DagBreakReason.toString named) = named,
                    fun () ->
                        at (
                            sprintf
                                "ofString (toString %A) = %A — the rendering and the parse disagree, so a consumer reading a logged reason back does not recover the case that wrote it"
                                named
                                (DagBreakReason.ofString (DagBreakReason.toString named))
                        )
                )

            let alien = rng.IntBelow 1000
            let alienText = "a reason this library does not mint #" + string alien

            match DagBreakReason.ofString alienText with
            | DagBreakReason.Unrecognised s when s = alienText -> verbatim.Saw()
            | other ->
                verbatim.Check(
                    false,
                    fun () ->
                        at (
                            sprintf
                                "ofString %s = %A — an unknown reason must land in Unrecognised carrying its own text, never be swept into a named case, which is a claim about which check failed that nothing established"
                                alienText
                                other
                        )
                ))

        let expected =
            [ DagBreakReason.toString DagBreakReason.ContentIdMismatch
              DagBreakReason.toString DagBreakReason.MissingParent ]
            |> Set.ofList

        let missing = Set.difference expected seen

        dagWalk.Check(
            Set.isEmpty missing,
            fun () ->
                "the DAG walk never reported: "
                + (missing |> Set.toList |> String.concat ", ")
                + " — the laws above hold vacuously for the break kinds that were never produced"
        )

        LawKit.results [ unnamed; roundTrip; verbatim; dagWalk ]
