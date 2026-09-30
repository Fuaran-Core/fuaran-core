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
        let mutable rng = ConfRng.ofSeed seed
        let mutable verify = None
        let mutable replay = None
        let mutable tamper = None
        let mutable accepted = 0
        let mutable tampered = 0

        for i in 0 .. iterations - 1 do
            let mutable state = gen.State0
            let mutable recs = OpStream.empty

            for _ in 0..5 do
                let op, r' = gen.Op rng
                rng <- r'

                match OpStream.append hashFn sw (Human "conf") op state recs with
                | Ok(s', recs') ->
                    accepted <- accepted + 1
                    state <- s'
                    recs <- recs'
                | Error _ -> () // a rejected op just doesn't extend the chain

            if not (OpStream.verifyChain hashFn sw recs) && verify.IsNone then
                verify <- Some(sprintf "seed=%d iter=%d: an intact chain failed verifyChain" seed i)

            match OpStream.replay sw gen.State0 recs with
            | Ok s when s = state -> ()
            | other ->
                if replay.IsNone then
                    replay <- Some(sprintf "seed=%d iter=%d: replay≠live state (got %A)" seed i other)

            match recs with
            | [] -> ()
            | _ ->
                let tIdx, r2 = ConfRng.intBelow (List.length recs) rng
                let newOp, r3 = gen.Op r2
                rng <- r3
                let orig = List.item tIdx recs

                // Only a genuinely-different op is a tamper the chain must detect.
                if sw.Encode orig.Op <> sw.Encode newOp then
                    tampered <- tampered + 1

                    let forged =
                        recs |> List.mapi (fun j r -> if j = tIdx then { r with Op = newOp } else r)

                    if OpStream.verifyChain hashFn sw forged && tamper.IsNone then
                        tamper <- Some(sprintf "seed=%d iter=%d: a tampered op was not detected" seed i)

        [ { Law = "verifyChain accepts an intact chain"
            Passed = verify.IsNone
            Counterexample = verify }
          { Law = "replay re-derives the live state"
            Passed = replay.IsNone
            Counterexample = replay }
          { Law = "verifyChain detects a tampered op"
            Passed = tamper.IsNone
            Counterexample = tamper }
          SampleAdequacy.reached "Conformance.streamLaws" "accepted op" seed [ "accepted", accepted ]
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
        let mutable rng = ConfRng.ofSeed seed
        let mutable totality = None
        let mutable determinism = None
        let mutable envelope = None
        // Phase 220 — the outcome populations the laws branch on, both DRAWN from the domain's
        // own generator: replay determinism reads the accepted ops, the envelope law reads the
        // refused ones, and totality is the claim that a refusal is TYPED rather than thrown —
        // which a run that never reached a refusal has not tested.
        let mutable acceptedN = 0
        let mutable refusedN = 0

        for i in 0 .. iterations - 1 do
            let mutable state = gen.State0
            let mutable accepted = []

            for _ in 0..5 do
                let op, r' = gen.Op rng
                rng <- r'

                let res =
                    try
                        Some(apply op state)
                    with _ ->
                        None

                match res with
                | None ->
                    if totality.IsNone then
                        totality <- Some(sprintf "seed=%d iter=%d: apply threw (not a typed rejection)" seed i)
                | Some(Ok st') ->
                    acceptedN <- acceptedN + 1
                    state <- st'
                    accepted <- accepted @ [ op ]
                | Some(Error rej) ->
                    refusedN <- refusedN + 1

                    match namesAlternatives with
                    | Some p when not (p rej) && envelope.IsNone ->
                        envelope <-
                            Some(sprintf "seed=%d iter=%d: a rejection did not enumerate its alternatives" seed i)
                    | _ -> ()

            // replay the accepted ops from State0 — must reproduce the live state
            let replayed =
                (Ok gen.State0, accepted)
                ||> List.fold (fun acc op -> acc |> Result.bind (fun s -> apply op s))

            match replayed with
            | Ok st when st = state -> ()
            | other ->
                if determinism.IsNone then
                    determinism <- Some(sprintf "seed=%d iter=%d: replay ≠ live state (got %A)" seed i other)

        [ { Law = "reducer totality (never throws)"
            Passed = totality.IsNone
            Counterexample = totality }
          { Law = "reducer replay determinism"
            Passed = determinism.IsNone
            Counterexample = determinism } ]
        @ (match namesAlternatives with
           | Some _ ->
               [ { Law = "rejection enumerates its alternatives"
                   Passed = envelope.IsNone
                   Counterexample = envelope } ]
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
    /// snapshot path. `'State` needs equality.
    let snapshotLawsWith
        (cfg: StreamConfig)
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (stateEncode: 'State -> string)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable bounded = None
        let mutable across = None

        for i in 0 .. iterations - 1 do
            let mutable state = gen.State0
            let mutable recs = OpStream.empty

            for _ in 0..5 do
                let op, r' = gen.Op rng
                rng <- r'

                match OpStream.appendWith cfg hashFn sw (Human "conf") op state recs with
                | Ok(s', recs') ->
                    state <- s'
                    recs <- recs'
                | Error _ -> ()

            let len = List.length recs

            if len > 0 then
                let atSeq, r2 = ConfRng.intBelow (len + 1) rng
                rng <- r2

                match OpStream.compact hashFn stateEncode sw gen.State0 recs atSeq with
                | Ok(snap, tail) ->
                    match OpStream.replayFrom sw snap tail, OpStream.replay sw gen.State0 recs with
                    | Ok a, Ok b when a = b -> ()
                    | other ->
                        if bounded.IsNone then
                            bounded <-
                                Some(sprintf "seed=%d iter=%d: replayFrom ≠ replay-from-origin (%A)" seed i other)

                    if
                        not (OpStream.verifyAcrossWith cfg hashFn stateEncode sw snap tail)
                        && across.IsNone
                    then
                        across <-
                            Some(sprintf "seed=%d iter=%d: verifyAcrossWith rejected an intact (snapshot, tail)" seed i)
                | Error e ->
                    if bounded.IsNone then
                        bounded <- Some(sprintf "seed=%d iter=%d: compact failed: %s" seed i e)

        [ { Law = "bounded replay (replayFrom snapshot tail = replay from origin)"
            Passed = bounded.IsNone
            Counterexample = bounded }
          { Law = "verifyAcross accepts an intact (snapshot, tail)"
            Passed = across.IsNone
            Counterexample = across } ]

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
        snapshotLawsWith OpStream.canonicalConfig sw gen stateEncode hashFn seed iterations

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
        let mutable rng = ConfRng.ofSeed seed
        let mutable verify = None
        let mutable determinism = None
        let mutable tamper = None
        let mutable roundtrip = None

        for i in 0 .. iterations - 1 do
            // a fork+merge DAG: genesis g; a, b both children of g; merge m of (a, b)
            let op0, r0 = gen.Op rng
            let opA, r1 = gen.Op r0
            let opB, r2 = gen.Op r1
            let opM, r3 = gen.Op r2
            rng <- r3

            let g, d1 = Dag.append hashFn sw (Human "conf") op0 "" Dag.empty
            let a, d2 = Dag.append hashFn sw (Human "conf") opA g d1
            let b, d3 = Dag.append hashFn sw (Human "conf") opB g d2
            let m, dag = Dag.merge hashFn sw (Human "conf") opM a b d3

            if not (Dag.verifyDag hashFn sw dag) && verify.IsNone then
                verify <- Some(sprintf "seed=%d iter=%d: verifyDag rejected an intact DAG" seed i)

            // Determinism (Phase 18): build the SAME logical history with a permuted append order
            // (B before A) and confirm it converges to the same content-addressed DAG + head and
            // replays to the same state — a genuine convergence check, not the prior `f x <> f x`
            // self-comparison. (Content addressing is append-order-insensitive, so a regression
            // that leaked insertion order into a node id would diverge here.)
            let g', e1 = Dag.append hashFn sw (Human "conf") op0 "" Dag.empty
            let b', e2 = Dag.append hashFn sw (Human "conf") opB g' e1
            let a', e3 = Dag.append hashFn sw (Human "conf") opA g' e2
            let m', dag' = Dag.merge hashFn sw (Human "conf") opM a' b' e3

            if
                (dag'.Nodes <> dag.Nodes
                 || m' <> m
                 || Dag.replayTo sw gen.State0 dag' m' <> Dag.replayTo sw gen.State0 dag m)
                && determinism.IsNone
            then
                determinism <-
                    Some(sprintf "seed=%d iter=%d: a permuted-construction history diverged (nodes/head/replay)" seed i)

            // tamper one node's op with a genuinely-different op
            let newOp, r4 = gen.Op rng
            rng <- r4

            let tid, tnode = dag.Nodes |> Map.toList |> List.head

            if sw.Encode tnode.Op <> sw.Encode newOp then
                let tampered = { Dag.T.Nodes = Map.add tid { tnode with Op = newOp } dag.Nodes }

                if Dag.verifyDag hashFn sw tampered && tamper.IsNone then
                    tamper <- Some(sprintf "seed=%d iter=%d: a tampered DAG node was not detected" seed i)

            // JSONL persistence round-trip (Phase 01 is shipped)
            match Dag.fromJsonl sw (Dag.toJsonl sw.Encode dag) with
            | Ok dag' when dag'.Nodes = dag.Nodes -> ()
            | other ->
                if roundtrip.IsNone then
                    roundtrip <- Some(sprintf "seed=%d iter=%d: DAG JSONL round-trip ≠ original (%A)" seed i other)

        [ { Law = "verifyDag accepts an intact DAG"
            Passed = verify.IsNone
            Counterexample = verify }
          { Law = "replayTo is deterministic"
            Passed = determinism.IsNone
            Counterexample = determinism }
          { Law = "verifyDag detects a tampered node"
            Passed = tamper.IsNone
            Counterexample = tamper }
          { Law = "DAG JSONL round-trip preserves the DAG"
            Passed = roundtrip.IsNone
            Counterexample = roundtrip } ]

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
        let mutable rng = ConfRng.ofSeed seed
        let mutable exact = None
        let mutable deterministic = None
        let mutable tamper = None

        for i in 0 .. iterations - 1 do
            // record a non-deterministic session: 1..5 effects, each a fresh draw, journalled.
            let k, r0 = ConfRng.intBelow 5 rng
            rng <- r0
            let mutable captures = []
            let mutable recorded = []

            for _ in 0..k do
                let v, r' = draw rng
                rng <- r'
                // the recorded "live source" draws v; captureEffect journals it under "random".
                let got, caps' =
                    OpStream.captureEffect hashFn encode "random" "eff" (fun () -> v) captures

                captures <- caps'
                recorded <- recorded @ [ got ]

            // replay with a live source that would draw DIFFERENT values — a capture hit must win.
            let mutable cursor = captures
            let mutable replayed = []
            let mutable replayOk = true

            for orig in recorded do
                // a divergent live fallback: if replay ever re-evaluated, it would diverge from orig.
                let liveDifferent () =
                    let v, r' = draw rng
                    rng <- r'
                    v

                match OpStream.replayEffect decode "eff" "random" liveDifferent cursor with
                | Ok(v, rest) ->
                    cursor <- rest
                    replayed <- replayed @ [ v ]
                | Error _ -> replayOk <- false

            if
                exact.IsNone
                && (not replayOk
                    || replayed <> recorded
                    || not (List.isEmpty cursor)
                    // byte-identity: each replayed value re-encodes to the journalled value.
                    || (List.zip replayed captures |> List.exists (fun (v, c) -> encode v <> c.Value)))
            then
                exact <- Some(sprintf "seed=%d iter=%d: replay-with-capture ≠ recorded session" seed i)

            // deterministic pass-through: no capture emitted, replay re-evaluates live, journal intact.
            let dv, r1 = draw rng
            rng <- r1

            let dGot, dCaps =
                OpStream.captureEffect hashFn encode OpStream.deterministicTag "eff" (fun () -> dv) []

            let dReplay =
                OpStream.replayEffect decode "eff" OpStream.deterministicTag (fun () -> dv) dCaps

            if
                deterministic.IsNone
                && (dGot <> dv || not (List.isEmpty dCaps) || dReplay <> Ok(dv, []))
            then
                deterministic <-
                    Some(sprintf "seed=%d iter=%d: a deterministic effect was captured or altered replay" seed i)

            // tamper: replace a captured value with a genuinely-different encoding ⇒ verifyCaptures fails.
            match captures with
            | [] -> ()
            | _ ->
                let tIdx, r2 = ConfRng.intBelow (List.length captures) rng
                let newV, r3 = draw r2
                rng <- r3
                let newValue = encode newV
                let orig = List.item tIdx captures

                if orig.Value <> newValue then
                    let tampered =
                        captures
                        |> List.mapi (fun j c -> if j = tIdx then { c with Value = newValue } else c)

                    if OpStream.verifyCaptures hashFn tampered && tamper.IsNone then
                        tamper <- Some(sprintf "seed=%d iter=%d: a tampered capture was not detected" seed i)

        // multi-identity guard (Phase 40): a journal of two distinct effect identities replays
        // correctly in record order, but a replay that requests the wrong identity at the head
        // surfaces a *named* mismatch rather than silently handing back the other effect's value.
        let multiIdentity =
            let v1, rA = draw (ConfRng.ofSeed (seed + 7))
            let v2, _ = draw rA
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

            if inOrder && misordered then
                None
            else
                Some(sprintf "seed=%d: replayEffect did not enforce effect-identity order" seed)

        [ { Law = "replay-with-capture is byte-identical to the recorded session"
            Passed = exact.IsNone
            Counterexample = exact }
          { Law = "a deterministic effect emits no capture (replay re-evaluates live)"
            Passed = deterministic.IsNone
            Counterexample = deterministic }
          { Law = "verifyCaptures detects a tampered capture"
            Passed = tamper.IsNone
            Counterexample = tamper }
          { Law = "replayEffect enforces effect-identity order (a misordered replay is a named error)"
            Passed = multiIdentity.IsNone
            Counterexample = multiIdentity } ]

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
        let mutable rng = ConfRng.ofSeed seed
        let mutable matchLaw = None
        let mutable staleLaw = None
        let mutable raceLaw = None
        let actor = Human "conf"
        // Phase 223 — the match arm's two outcome populations, both DRAWN from the caller's
        // StreamGen: `match ≡ append` compares a domain refusal with a CAS `Domain` rejection only
        // when the drawn op is refused, and compares two accepted appends only when it is not.
        let mutable accepted = 0
        let mutable refused = 0

        for i in 0 .. iterations - 1 do
            // Build a random base chain (as streamLaws does) — the CAS is exercised against its head.
            let mutable state = gen.State0
            let mutable recs = OpStream.empty

            for _ in 0..5 do
                let op, r' = gen.Op rng
                rng <- r'

                match OpStream.append hashFn sw actor op state recs with
                | Ok(s', recs') ->
                    state <- s'
                    recs <- recs'
                | Error _ -> ()

            let baseHead = OpStream.head recs

            // ---- match ≡ append ----
            let opM, rM = gen.Op rng
            rng <- rM
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

            if not matchOk && matchLaw.IsNone then
                matchLaw <- Some(sprintf "seed=%d iter=%d: appendIf(trueHead) ≢ append" seed i)

            // ---- stale head rejects, naming the actual head; stream unchanged ----
            let opS, rS = gen.Op rng
            rng <- rS
            let staleHead = baseHead + "!" // guaranteed ≠ baseHead

            match OpStream.appendIf hashFn sw staleHead actor opS state recs with
            | Error(AppendRejection.StaleHead(expected, actual)) when expected = staleHead && actual = baseHead -> () // recs is an immutable value the caller still holds — there is no partial write
            | other ->
                if staleLaw.IsNone then
                    staleLaw <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: appendIf(staleHead) did not name the actual head (got %A)"
                                seed
                                i
                                other
                        )

            // ---- race: exactly one of two writers off one base head wins, either order ----
            let opA, rA = gen.Op rng
            let opB, rB = gen.Op rA
            rng <- rB

            // A genuine CAS race needs both ops to individually apply against the base — a domain
            // reject is not a CAS outcome, so skip the race check for that iteration.
            match OpStream.append hashFn sw actor opA state recs, OpStream.append hashFn sw actor opB state recs with
            | Ok _, Ok _ ->
                // The first writer commits against the base head → succeeds and advances the chain; the
                // second still holds baseHead as its expectation → StaleHead. Exactly one winner.
                let serialise first second =
                    match OpStream.appendIf hashFn sw baseHead actor first state recs with
                    | Ok(s1, recs1) ->
                        match OpStream.appendIf hashFn sw baseHead actor second s1 recs1 with
                        | Error(AppendRejection.StaleHead _) -> true
                        | _ -> false
                    | _ -> false

                if not (serialise opA opB && serialise opB opA) && raceLaw.IsNone then
                    raceLaw <-
                        Some(
                            sprintf "seed=%d iter=%d: two racing appendIf calls did not admit exactly one winner" seed i
                        )
            | _ -> ()

        [ { Law = "appendIf with the true head ≡ append"
            Passed = matchLaw.IsNone
            Counterexample = matchLaw }
          { Law = "appendIf with a stale head rejects, naming the actual head (stream unchanged)"
            Passed = staleLaw.IsNone
            Counterexample = staleLaw }
          { Law = "two racing appendIf calls off one base admit exactly one winner under any serialisation"
            Passed = raceLaw.IsNone
            Counterexample = raceLaw }
          // Phase 223 — `Guarded ["accepted"; "refused"]`, after the subject laws so their positions
          // are unchanged. A StreamGen that never draws a refused op leaves `match ≡ append`
          // certified on the accept path alone, and green.
          SampleAdequacy.reached "Conformance.casLaws" "accepted op" seed [ "accepted", accepted ]
          SampleAdequacy.reached "Conformance.casLaws" "refused op" seed [ "refused", refused ] ]

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
        let mutable rng = ConfRng.ofSeed seed
        let mutable freshLaw = None
        let mutable dupLaw = None
        let mutable parityLaw = None
        let mutable casLaw = None
        let actor = Human "conf"
        // Phase 223 — the fresh-key arms' outcome populations, DRAWN from the caller's StreamGen:
        // `fresh ≡ append` and the true-head CAS arm forward a domain refusal verbatim only when the
        // drawn op is refused, which is a property of the run.
        let mutable accepted = 0
        let mutable refused = 0

        for i in 0 .. iterations - 1 do
            // Build a base chain THROUGH appendIdempotent, threading (state, records, index) — a
            // generated op whose key is already seen (or whose apply rejects) extends nothing,
            // which is itself the primitive under test. Rebuild parity is checked at every step.
            let mutable state = gen.State0
            let mutable recs = OpStream.empty
            let mutable index = KeyIndex.empty

            for _ in 0..5 do
                let op, r' = gen.Op rng
                rng <- r'

                match OpStream.appendIdempotent hashFn sw (keyOf op) actor op state index recs with
                | Ok(AppendOutcome.Appended(s', recs', idx')) ->
                    state <- s'
                    recs <- recs'
                    index <- idx'
                | Ok(AppendOutcome.Duplicate _)
                | Error _ -> ()

                if KeyIndex.ofStream keyOf recs <> index && parityLaw.IsNone then
                    parityLaw <- Some(sprintf "seed=%d iter=%d: ofStream ≠ the incrementally-maintained index" seed i)

            let baseHead = OpStream.head recs

            // ---- fresh ≡ append (chain-identity + index extended by exactly the new entry) ----
            let opF, rF = gen.Op rng
            rng <- rF

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

                if not freshOk && freshLaw.IsNone then
                    freshLaw <- Some(sprintf "seed=%d iter=%d: appendIdempotent(fresh key) ≢ append" seed i)

            // ---- duplicate convergence: a seen key names the entry it FIRST produced ----
            if not (List.isEmpty recs) then
                let pick, rP = ConfRng.intBelow (List.length recs) rng
                rng <- rP
                let key = keyOf (List.item pick recs).Op
                let first = recs |> List.find (fun r -> keyOf r.Op = key)
                let opD, rD = gen.Op rng
                rng <- rD

                match OpStream.appendIdempotent hashFn sw key actor opD state index recs with
                | Ok(AppendOutcome.Duplicate existing) when existing.Seq = first.Seq && existing.Hash = first.Hash -> () // recs/index are immutable values the caller still holds — the stream is byte-identical
                | other ->
                    if dupLaw.IsNone then
                        dupLaw <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: a seen key did not converge on its first entry (got %A)"
                                    seed
                                    i
                                    other
                            )

                // ---- idempotency-before-CAS: the lost-ack retry converges under a stale head ----
                let staleHead = baseHead + "!" // guaranteed ≠ baseHead

                match OpStream.appendIdempotentIf hashFn sw key staleHead actor opD state index recs with
                | Ok(AppendOutcome.Duplicate existing) when existing.Seq = first.Seq && existing.Hash = first.Hash -> ()
                | other ->
                    if casLaw.IsNone then
                        casLaw <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: a seen key under a stale head did not converge on Duplicate (got %A)"
                                    seed
                                    i
                                    other
                            )

            // ---- fresh key through the CAS: stale head refuses; the true head ≡ append ----
            let opC, rC = gen.Op rng
            rng <- rC

            if (KeyIndex.tryFind (keyOf opC) index).IsNone then
                let staleHead = baseHead + "!"

                match OpStream.appendIdempotentIf hashFn sw (keyOf opC) staleHead actor opC state index recs with
                | Error(AppendRejection.StaleHead(expected, actual)) when expected = staleHead && actual = baseHead ->
                    ()
                | other ->
                    if casLaw.IsNone then
                        casLaw <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: a fresh key under a stale head was not StaleHead (got %A)"
                                    seed
                                    i
                                    other
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

                if not matchOk && casLaw.IsNone then
                    casLaw <- Some(sprintf "seed=%d iter=%d: appendIdempotentIf(fresh key, true head) ≢ append" seed i)

        [ { Law = "appendIdempotent with a fresh key ≡ append (chain-identical; index gains exactly the new entry)"
            Passed = freshLaw.IsNone
            Counterexample = freshLaw }
          { Law = "re-appending a seen key is Duplicate naming the entry the key first produced (stream untouched)"
            Passed = dupLaw.IsNone
            Counterexample = dupLaw }
          { Law = "KeyIndex.ofStream agrees with the incrementally-maintained index (rebuild parity)"
            Passed = parityLaw.IsNone
            Counterexample = parityLaw }
          { Law = "idempotency precedes the CAS (a seen key converges under any head; a fresh key CASes as appendIf)"
            Passed = casLaw.IsNone
            Counterexample = casLaw }
          // Phase 223 — `Guarded ["accepted"; "refused"]`, after the subject laws. A StreamGen that
          // never draws a refused fresh op leaves the verbatim-forwarding half of `fresh ≡ append`
          // and of the true-head CAS arm certified by nothing, and green.
          SampleAdequacy.reached "Conformance.idempotencyLaws" "accepted fresh op" seed [ "accepted", accepted ]
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
        // A self-contained int-op witness: the claim is about THIS library's walkers, not about a
        // host's, so there is no caller witness to take.
        let sw: StreamWitness<int, int, string> =
            { Apply = fun op state -> Ok(state + op)
              Encode = string
              Decode =
                fun s ->
                    match System.Int32.TryParse s with
                    | true, v -> Ok v
                    | false, _ -> Error("not an int: " + s) }

        let hashFn = OpStream.defaultHash
        let mutable rng = ConfRng.ofSeed seed
        let mutable unnamed = None
        let mutable roundTrip = None
        let mutable verbatim = None
        let mutable seenOp = Set.empty
        let mutable seenCapture = Set.empty

        // The reason a break carries, or None when the walk found the chain intact — which is
        // itself a defect here, since every input below is deliberately broken.
        let reasonOf (label: string) (i: int) (b: ChainBreak option) : ChainBreakReason option =
            match b with
            | Some br ->
                // Qualified since Phase 147: `DagBreakReason` declares an `Unrecognised` too, and
                // both are in scope here. The .NET compiler resolves this from the scrutinee's type;
                // FABLE does not, and reported it as an error on a tree .NET had built clean.
                (match br.Reason with
                 | ChainBreakReason.Unrecognised s when unnamed.IsNone ->
                     unnamed <-
                         Some(
                             sprintf
                                 "seed=%d iter=%d: the %s walk minted an unnamed reason %s — a walker inside this library must stay inside the named cases, or ChainBreakReason gives a consumer back the untyped string it exists to remove"
                                 seed
                                 i
                                 label
                                 s
                         )
                 | _ -> ())

                Some br.Reason
            | None ->
                if unnamed.IsNone then
                    unnamed <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: the %s walk reported NO break over a deliberately broken chain, so this family is measuring nothing"
                                seed
                                i
                                label
                        )

                None

        for i in 0 .. iterations - 1 do
            // ---- a sound op chain of four records ----
            let mutable state = 0
            let mutable recs = OpStream.empty

            for _ in 0..3 do
                let op, r' = ConfRng.intBelow 50 rng
                rng <- r'

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

                match reasonOf "op" i (OpStream.firstChainBreakWith OpStream.canonicalConfig hashFn sw renumbered) with
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

                match reasonOf "op" i (OpStream.firstChainBreakWith OpStream.canonicalConfig hashFn sw repointed) with
                | Some r -> seenOp <- Set.add (ChainBreakReason.toString r) seenOp
                | None -> ()

                // digest: tamper the OP only. Seq and PrevHash still agree, so the two cheap checks
                // pass and the hash recomputation is what fails — the only way to reach that arm.
                let tampered =
                    recs
                    |> List.mapi (fun j r -> if j = len - 1 then { r with Op = r.Op + 1000 } else r)

                match reasonOf "op" i (OpStream.firstChainBreakWith OpStream.canonicalConfig hashFn sw tampered) with
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

                match reasonOf "capture" i (OpStream.firstCaptureBreak hashFn capRenumbered) with
                | Some r -> seenCapture <- Set.add (ChainBreakReason.toString r) seenCapture
                | None -> ()

                let capRepointed =
                    caps
                    |> List.mapi (fun j c ->
                        if j = clen - 1 then
                            { c with PrevHash = c.PrevHash + "x" }
                        else
                            c)

                match reasonOf "capture" i (OpStream.firstCaptureBreak hashFn capRepointed) with
                | Some r -> seenCapture <- Set.add (ChainBreakReason.toString r) seenCapture
                | None -> ()

                let capTampered =
                    caps
                    |> List.mapi (fun j c -> if j = clen - 1 then { c with Value = c.Value + "9" } else c)

                match reasonOf "capture" i (OpStream.firstCaptureBreak hashFn capTampered) with
                | Some r -> seenCapture <- Set.add (ChainBreakReason.toString r) seenCapture
                | None -> ()

            // ---- the string pair, both directions ----
            for named in [ SequenceMismatch; PrevHashLinkBroken; HashMismatch ] do
                if
                    ChainBreakReason.ofString (ChainBreakReason.toString named) <> named
                    && roundTrip.IsNone
                then
                    roundTrip <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: ofString (toString %A) = %A — the rendering and the parse disagree, so a consumer reading a logged reason back does not recover the case that wrote it"
                                seed
                                i
                                named
                                (ChainBreakReason.ofString (ChainBreakReason.toString named))
                        )

            let alien, rA = ConfRng.intBelow 1000 rng
            rng <- rA
            let alienText = "a reason this library does not mint #" + string alien

            match ChainBreakReason.ofString alienText with
            | ChainBreakReason.Unrecognised s when s = alienText -> ()
            | other ->
                if verbatim.IsNone then
                    verbatim <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: ofString %s = %A — an unknown reason must land in Unrecognised carrying its own text, never be swept into a named case, which is a claim about which check failed that nothing established"
                                seed
                                i
                                alienText
                                other
                        )

        // The capture walk spells the digest failure differently; `toString` renders one spelling
        // for the single `HashMismatch` case, so both walks are expected to have observed the same
        // three strings.
        let expected =
            [ ChainBreakReason.toString SequenceMismatch
              ChainBreakReason.toString PrevHashLinkBroken
              ChainBreakReason.toString HashMismatch ]
            |> Set.ofList

        let missing (seen: Set<string>) =
            Set.difference expected seen |> Set.toList |> String.concat ", "

        [ { Law = "every reason the chain walkers mint is a NAMED ChainBreakReason case"
            Passed = unnamed.IsNone
            Counterexample = unnamed }
          { Law = "ChainBreakReason.ofString (toString r) = r on every named case"
            Passed = roundTrip.IsNone
            Counterexample = roundTrip }
          { Law = "ChainBreakReason.ofString carries an unknown reason into Unrecognised verbatim"
            Passed = verbatim.IsNone
            Counterexample = verbatim }
          { Law = "non-vacuity: the op walk produced every break kind it can produce"
            Passed = Set.isEmpty (Set.difference expected seenOp)
            Counterexample =
              if Set.isEmpty (Set.difference expected seenOp) then
                  None
              else
                  Some(
                      "the op walk never reported: "
                      + missing seenOp
                      + " — the laws above hold vacuously for the break kinds that were never produced"
                  ) }
          { Law = "non-vacuity: the capture walk produced every break kind it can produce"
            Passed = Set.isEmpty (Set.difference expected seenCapture)
            Counterexample =
              if Set.isEmpty (Set.difference expected seenCapture) then
                  None
              else
                  Some(
                      "the capture walk never reported: "
                      + missing seenCapture
                      + " — the laws above hold vacuously for the break kinds that were never produced"
                  ) } ]

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
        // A self-contained int-op witness: the claim is about THIS library's walker, not about a
        // host's, so there is no caller witness to take.
        let sw: StreamWitness<int, int, string> =
            { Apply = fun op state -> Ok(state + op)
              Encode = string
              Decode =
                fun s ->
                    match System.Int32.TryParse s with
                    | true, v -> Ok v
                    | false, _ -> Error("not an int: " + s) }

        let hashFn = OpStream.defaultHash
        let mutable rng = ConfRng.ofSeed seed
        let mutable unnamed = None
        let mutable roundTrip = None
        let mutable verbatim = None
        let mutable seen = Set.empty

        // The reason a break carries, or None when the walk found the DAG intact — which is itself a
        // defect here, since every input below is deliberately broken.
        let reasonOf (label: string) (i: int) (b: DagBreak option) : DagBreakReason option =
            match b with
            | Some br ->
                // Qualified: `ChainBreakReason` declares an `Unrecognised` too, and both are in
                // scope here — the sibling family below is the reason this file sees both.
                (match br.Reason with
                 | DagBreakReason.Unrecognised s when unnamed.IsNone ->
                     unnamed <-
                         Some(
                             sprintf
                                 "seed=%d iter=%d: the %s walk minted an unnamed reason %s — the walker inside this library must stay inside the named cases, or DagBreakReason gives a consumer back the untyped string it exists to remove"
                                 seed
                                 i
                                 label
                                 s
                         )
                 | _ -> ())

                Some br.Reason
            | None ->
                if unnamed.IsNone then
                    unnamed <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: the %s walk reported NO break over a deliberately broken DAG, so this family is measuring nothing"
                                seed
                                i
                                label
                        )

                None

        for i in 0 .. iterations - 1 do
            // ---- a sound DAG: genesis, two children, a merge — every node shape the walker meets ----
            let op0, r0 = ConfRng.intBelow 50 rng
            let opA, r1 = ConfRng.intBelow 50 r0
            let opB, r2 = ConfRng.intBelow 50 r1
            let opM, r3 = ConfRng.intBelow 50 r2
            rng <- r3

            let g, d1 = Dag.append hashFn sw (Human "conf") op0 "" Dag.empty
            let a, d2 = Dag.append hashFn sw (Human "conf") (opA + 1) g d1
            let b, d3 = Dag.append hashFn sw (Human "conf") (opB + 1) g d2
            let _, dag = Dag.merge hashFn sw (Human "conf") (opM + 1) a b d3

            // content id: tamper the OP and leave the map KEY exactly as it was. That is the threat
            // the content id exists to catch, and it is precisely not a rewrite.
            let tid, tnode = dag.Nodes |> Map.toList |> List.head

            let tampered =
                { Dag.T.Nodes = Map.add tid { tnode with Op = tnode.Op + 1000 } dag.Nodes }

            match reasonOf "content-id" i (Dag.firstBreak hashFn sw tampered) with
            | Some r -> seen <- Set.add (DagBreakReason.toString r) seen
            | None -> ()

            // missing parent: delete the genesis node that `a` and `b` both name. Every surviving
            // node's id still recomputes from its own fields, so the content-id check passes and the
            // parent check is the one that fires — the only way to reach that arm.
            let orphaned = { Dag.T.Nodes = Map.remove g dag.Nodes }

            match reasonOf "missing-parent" i (Dag.firstBreak hashFn sw orphaned) with
            | Some r -> seen <- Set.add (DagBreakReason.toString r) seen
            | None -> ()

            // ---- the string pair, both directions ----
            for named in [ ContentIdMismatch; MissingParent ] do
                if
                    DagBreakReason.ofString (DagBreakReason.toString named) <> named
                    && roundTrip.IsNone
                then
                    roundTrip <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: ofString (toString %A) = %A — the rendering and the parse disagree, so a consumer reading a logged reason back does not recover the case that wrote it"
                                seed
                                i
                                named
                                (DagBreakReason.ofString (DagBreakReason.toString named))
                        )

            let alien, rA = ConfRng.intBelow 1000 rng
            rng <- rA
            let alienText = "a reason this library does not mint #" + string alien

            match DagBreakReason.ofString alienText with
            | DagBreakReason.Unrecognised s when s = alienText -> ()
            | other ->
                if verbatim.IsNone then
                    verbatim <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: ofString %s = %A — an unknown reason must land in Unrecognised carrying its own text, never be swept into a named case, which is a claim about which check failed that nothing established"
                                seed
                                i
                                alienText
                                other
                        )

        let expected =
            [ DagBreakReason.toString ContentIdMismatch
              DagBreakReason.toString MissingParent ]
            |> Set.ofList

        let missing = Set.difference expected seen

        [ { Law = "every reason the DAG walker mints is a NAMED DagBreakReason case"
            Passed = unnamed.IsNone
            Counterexample = unnamed }
          { Law = "DagBreakReason.ofString (toString r) = r on every named case"
            Passed = roundTrip.IsNone
            Counterexample = roundTrip }
          { Law = "DagBreakReason.ofString carries an unknown reason into Unrecognised verbatim"
            Passed = verbatim.IsNone
            Counterexample = verbatim }
          { Law = "non-vacuity: the DAG walk produced every break kind it can produce"
            Passed = Set.isEmpty missing
            Counterexample =
              if Set.isEmpty missing then
                  None
              else
                  Some(
                      "the DAG walk never reported: "
                      + (missing |> Set.toList |> String.concat ", ")
                      + " — the laws above hold vacuously for the break kinds that were never produced"
                  ) } ]
