namespace Fuaran.Core

/// The linear chain's families: the stream, reducer, snapshot and stream-config laws, compare-and-append,
/// idempotent append (Phase 82) and the chain break-reason fixtures — `StreamLaws` until the Phase 388
/// split along its banners.
module internal ChainStreamLaws =
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
                | Error e ->
                    jsonl.Check(
                        false,
                        fun () -> at ("the written JSONL did not read back: " + StreamLoadFault.toString e)
                    )

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

    /// The configured-stream laws (Phase 349): the `…With` stream operations — each the bare form
    /// with its `StreamConfig` (the payload pre-image and the genesis) a parameter beside its
    /// `HashFn` — held at the config and hash the caller passes, so a domain on its own chain format
    /// certifies the operations it actually calls, and a variant that ignores either parameter goes
    /// red. Run it at a config and a hash that are NOT the canonical ones: at `canonicalConfig` and
    /// `defaultHash` a variant that silently used them would agree with itself.
    ///
    ///  - **the configured chain is the chain its config and hash describe** — every record built
    ///    with `appendWith cfg hashFn` has `Seq` its index, `PrevHash` the previous `Hash` (from
    ///    `cfg.Genesis`) and `Hash = hashFn PrevHash (cfg.Payload Seq Actor (Encode Op))`;
    ///    `headWith cfg` is the last hash or the genesis; `verifyChainWith` accepts the chain and
    ///    `firstChainBreakWith` finds no break;
    ///  - **`appendManyWith` is the fold of `appendWith`** — the same state and records, byte for
    ///    byte, or the index of the first op the fold refuses with nothing chained;
    ///  - **`appendIfWith` at the configured head is `appendWith`**, and at any other head it is
    ///    `StaleHead` naming the configured head;
    ///  - **the parameters are read** — a non-empty configured chain fails `verifyChainWith` under
    ///    another genesis, another payload and another hash (each the caller's own, perturbed), and a
    ///    record whose hash is tampered fails it with `firstChainBreakWith` naming that record;
    ///  - **captures under the config** — a journal built with `captureEffectWith cfg hashFn` links
    ///    from `cfg.Genesis`, `captureHeadWith cfg` is its last hash or the genesis, it verifies
    ///    against that head with `verifyCapturesAtWith` and `firstCaptureBreakWith` finds no break;
    ///    under another genesis or another hash a non-empty journal does neither, and a deterministic
    ///    effect journals nothing.
    ///
    /// Every arm is built on every iteration (an empty chain still checks its head and its
    /// `appendIfWith`), so the family carries no guard. The perturbed parameters are DERIVED from the
    /// caller's — a suffix on the genesis, the payload and the hash — so they differ from the
    /// caller's on every input by construction rather than by a fixture's luck.
    let streamConfigLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (cfg: StreamConfig)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let described =
            LawKit.LawCell "the configured chain is the chain its config and hash describe, and verifies under them"

        let many =
            LawKit.LawCell "appendManyWith is the fold of appendWith, byte for byte, or refuses at the fold's index"

        let cas =
            LawKit.LawCell "appendIfWith at the configured head is appendWith; at another head it is StaleHead"

        let read =
            LawKit.LawCell
                "the parameters are read: another genesis, payload or hash, or a tampered record, fails verifyChainWith"

        let captures =
            LawKit.LawCell
                "captures under the config link from its genesis and verify against captureHeadWith, and under no other"

        let otherGenesis = { cfg with Genesis = cfg.Genesis + "'" }

        let otherPayload =
            { cfg with
                Payload = fun s a e -> cfg.Payload s a e + "'" }

        let otherHash: HashFn = fun prev payload -> hashFn prev payload + "'"
        let actor = Human "conf"

        LawKit.run iterations seed (fun rng _ at ->
            let ops = [ for _ in 1 .. rng.IntBelow 7 -> rng.Draw gen.Op ]

            // ---- the fold of appendWith, and the chain it builds ----
            let mutable state = gen.State0
            let mutable recs = OpStream.empty
            let mutable refusedAt = None

            ops
            |> List.iteri (fun k op ->
                if refusedAt.IsNone then
                    match OpStream.appendWith cfg hashFn sw actor op state recs with
                    | Ok(s', r') ->
                        state <- s'
                        recs <- r'
                    | Error _ -> refusedAt <- Some k)

            // The fold stops at its first refusal; the chain it built is the accepted prefix.
            let arr = List.toArray recs

            let describedOk =
                arr
                |> Array.mapi (fun k r ->
                    let prev = if k = 0 then cfg.Genesis else arr[k - 1].Hash

                    r.Seq = k
                    && r.PrevHash = prev
                    && r.Hash = hashFn prev (cfg.Payload k r.Actor (sw.Encode r.Op)))
                |> Array.forall id

            let expectedHead =
                if arr.Length = 0 then
                    cfg.Genesis
                else
                    arr[arr.Length - 1].Hash

            described.Check(
                describedOk
                && OpStream.headWith cfg recs = expectedHead
                && OpStream.verifyChainWith cfg hashFn sw recs
                && OpStream.firstChainBreakWith cfg hashFn sw recs = None,
                fun () ->
                    at (
                        sprintf
                            "records described: %b; headWith %A (expected %A); verifyChainWith %b; firstChainBreakWith %A"
                            describedOk
                            (OpStream.headWith cfg recs)
                            expectedHead
                            (OpStream.verifyChainWith cfg hashFn sw recs)
                            (OpStream.firstChainBreakWith cfg hashFn sw recs)
                    )
            )

            // ---- appendManyWith is the fold ----
            match OpStream.appendManyWith cfg hashFn sw actor ops gen.State0 OpStream.empty, refusedAt with
            | Ok(s, r), None when s = state && r = recs -> many.Saw()
            | Error(k, _), Some k' when k = k' -> many.Saw()
            | other, _ ->
                many.Check(
                    false,
                    fun () ->
                        at (
                            sprintf
                                "appendManyWith answered %s where the fold of appendWith %s"
                                (match other with
                                 | Ok(_, r) -> sprintf "Ok with %d records" (List.length r)
                                 | Error(k, _) -> sprintf "Error at %d" k)
                                (match refusedAt with
                                 | Some k -> sprintf "refused op %d" k
                                 | None -> sprintf "chained %d records" (List.length recs))
                        )
                )

            // ---- appendIfWith ----
            let probe = rng.Draw gen.Op
            let head = OpStream.headWith cfg recs

            let casOk =
                match
                    OpStream.appendIfWith cfg hashFn sw head actor probe state recs,
                    OpStream.appendWith cfg hashFn sw actor probe state recs
                with
                | Ok a, Ok b -> a = b
                | Error(AppendRejection.Domain _), Error _ -> true
                | _ -> false

            let staleOk =
                match OpStream.appendIfWith cfg hashFn sw (head + "'") actor probe state recs with
                | Error(AppendRejection.StaleHead(_, actual)) -> actual = head
                | _ -> false

            cas.Check(
                casOk && staleOk,
                fun () -> at (sprintf "appendIfWith at the head agrees: %b; at a stale head refuses: %b" casOk staleOk)
            )

            // ---- the parameters are read ----
            if arr.Length > 0 then
                let victim = rng.IntBelow arr.Length

                let tampered =
                    recs
                    |> List.mapi (fun k r -> if k = victim then { r with Hash = r.Hash + "'" } else r)

                let breakAt = OpStream.firstChainBreakWith cfg hashFn sw tampered

                read.Check(
                    not (OpStream.verifyChainWith otherGenesis hashFn sw recs)
                    && not (OpStream.verifyChainWith otherPayload hashFn sw recs)
                    && not (OpStream.verifyChainWith cfg otherHash sw recs)
                    && not (OpStream.verifyChainWith cfg hashFn sw tampered)
                    && (breakAt |> Option.map _.Index) = Some victim,
                    fun () ->
                        at (
                            sprintf
                                "a %d-record chain verified under another genesis %b, payload %b, hash %b; tampered at %d: verified %b, first break %A"
                                arr.Length
                                (OpStream.verifyChainWith otherGenesis hashFn sw recs)
                                (OpStream.verifyChainWith otherPayload hashFn sw recs)
                                (OpStream.verifyChainWith cfg otherHash sw recs)
                                victim
                                (OpStream.verifyChainWith cfg hashFn sw tampered)
                                breakAt
                        )
                )

            // ---- captures under the config ----
            let values = [ for _ in 1 .. rng.IntBelow 4 -> rng.IntBelow 1000 ]

            let journal =
                values
                |> List.indexed
                |> List.fold
                    (fun caps (k, v) ->
                        snd (
                            OpStream.captureEffectWith
                                cfg
                                hashFn
                                string
                                "random"
                                ("eff" + string k)
                                (fun () -> v)
                                caps
                        ))
                    []

            let det =
                snd (
                    OpStream.captureEffectWith cfg hashFn string OpStream.deterministicTag "det" (fun () -> 0) journal
                )

            let jarr = List.toArray journal

            let linked =
                jarr
                |> Array.mapi (fun k c -> c.Seq = k && c.PrevHash = (if k = 0 then cfg.Genesis else jarr[k - 1].Hash))
                |> Array.forall id

            let capHead = OpStream.captureHeadWith cfg journal

            let expectedCapHead =
                if jarr.Length = 0 then
                    cfg.Genesis
                else
                    jarr[jarr.Length - 1].Hash

            let refusedElsewhere =
                jarr.Length = 0
                || (not (
                        OpStream.verifyCapturesAtWith
                            otherGenesis
                            hashFn
                            (OpStream.captureHeadWith otherGenesis journal)
                            journal
                    )
                    && not (OpStream.verifyCapturesAtWith cfg otherHash capHead journal)
                    && (OpStream.firstCaptureBreakWith otherGenesis hashFn journal).IsSome
                    && (OpStream.firstCaptureBreakWith cfg otherHash journal).IsSome)

            captures.Check(
                jarr.Length = List.length values
                && linked
                && capHead = expectedCapHead
                && OpStream.verifyCapturesAtWith cfg hashFn capHead journal
                && OpStream.firstCaptureBreakWith cfg hashFn journal = None
                && refusedElsewhere
                && det = journal,
                fun () ->
                    at (
                        sprintf
                            "a %d-capture journal: linked %b, head %A (expected %A), verifies %b, first break %A, refused elsewhere %b, deterministic effect journalled %b"
                            jarr.Length
                            linked
                            capHead
                            expectedCapHead
                            (OpStream.verifyCapturesAtWith cfg hashFn capHead journal)
                            (OpStream.firstCaptureBreakWith cfg hashFn journal)
                            refusedElsewhere
                            (det <> journal)
                    )
            ))

        LawKit.results [ described; many; cas; read; captures ]

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
