namespace Fuaran.Core

/// The stream families (Phase 297 split): chains, snapshots, DAGs, capture/replay, CAS, idempotency, and the break-reason fixtures.
module internal StreamLaws =

    /// The op-stream laws: `verifyChain` accepts an intact chain and rejects a tampered
    /// op; `replay` re-derives the live state from the base state.
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

        let mutable accepted = 0
        let mutable tampered = 0

        LawKit.run iterations seed (fun rng _ at ->
            // a rejected op just doesn't extend the chain — `buildChain` counts the ones that did
            let state, recs, acceptedHere = LawKit.buildChain hashFn sw gen rng
            accepted <- accepted + acceptedHere

            verify.Check(OpStream.verifyChain hashFn sw recs, fun () -> at "an intact chain failed verifyChain")

            match OpStream.replay sw gen.State0 recs with
            | Ok s when s = state -> replay.Saw()
            | other -> replay.Check(false, fun () -> at (sprintf "replay≠live state (got %A)" other))

            match recs with
            | [] -> ()
            | _ ->
                let tIdx = rng.IntBelow(List.length recs)
                let newOp = rng.Draw gen.Op
                let orig = List.item tIdx recs

                // Only a genuinely-different op is a tamper the chain must detect.
                if sw.Encode orig.Op <> sw.Encode newOp then
                    tampered <- tampered + 1

                    let forged =
                        recs |> List.mapi (fun j r -> if j = tIdx then { r with Op = newOp } else r)

                    tamper.Check(
                        not (OpStream.verifyChain hashFn sw forged),
                        fun () -> at "a tampered op was not detected"
                    ))

        LawKit.results [ verify; replay; tamper ]
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

            // tamper one node's op with a genuinely-different op
            let newOp = rng.Draw gen.Op

            let tid, tnode = dag.Nodes |> Map.toList |> List.head

            if sw.Encode tnode.Op <> sw.Encode newOp then
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

            match Dag.append colliding sw (Human "collider") newOp m dag with
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
    ///  - **tamper-evidence** — a tampered captured value fails `verifyCaptures`.
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
                let newV = rng.Draw draw
                let newValue = encode newV
                let orig = List.item tIdx captures

                if orig.Value <> newValue then
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

        LawKit.results [ exact; deterministic; tamper; multiIdentity ]
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
        // The gated arms — the two fresh-key arms run only when the drawn op's key is not already in
        // the index, and the seen-key arm (duplicate convergence + the stale-head retry) only over a
        // non-empty chain, all three decided by what the generator drew — are held by the runner's
        // evidence count: an arm never reached reports its law "never reached" rather than green
        // (Phase 302). A census-visible guard naming the starved arm is a later widening (see
        // `snapshotLawsWith`).

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
            let opF = rng.Draw gen.Op

            if (KeyIndex.tryFind (keyOf opF) index).IsNone then
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
                let first = recs |> List.find (fun r -> keyOf r.Op = key)
                let opD = rng.Draw gen.Op

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
                            at (sprintf "a seen key under a stale head did not converge on Duplicate (got %A)" other)
                    )

            // ---- fresh key through the CAS: stale head refuses; the true head ≡ append ----
            let opC = rng.Draw gen.Op

            if (KeyIndex.tryFind (keyOf opC) index).IsNone then
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
        @ [ SampleAdequacy.reached "Conformance.idempotencyLaws" "accepted fresh op" seed [ "accepted", accepted ]
            SampleAdequacy.reached "Conformance.idempotencyLaws" "refused fresh op" seed [ "refused", refused ] ]

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
