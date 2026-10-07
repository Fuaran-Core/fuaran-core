namespace Fuaran.Core

/// The effect-capture family: capture and byte-identical replay — `StreamLaws` until the Phase 388
/// split along its banners.
module internal CaptureStreamLaws =
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
