namespace Fuaran.Core

/// The invocable-capability seam family (Phase 30; `SeamLaws` until the Phase 388 split along its banners).
module internal CapabilitySeamLaws =
    /// The invocable-capability laws (Phase 30) — the teeth on `Capability` / `Registry` and their
    /// Phase 27 replay wiring. Self-contained (it builds its own capabilities from the seed); over a
    /// seed-replayable sample it certifies:
    ///
    ///  - **arg-validation** — a well-typed invocation is accepted; an out-of-space value and an
    ///    arg addressing no declared hole are each a named `InvokeError`, never a throw or a silent
    ///    pass (default-deny by shape);
    ///  - **byte-identical replay** — a non-`Deterministic` invocation's realized value, journaled
    ///    via `OpStream.captureEffect` (keyed by `Capability.invocationKey` + `determinismTag`),
    ///    replays through `replayEffect` **byte-identically** even when the live source would now
    ///    produce a different value, and fully consumes the journal;
    ///  - **a capture records the whole class** (Phase 319) — the declared determinism is a SET of
    ///    factors, and the label a capture journals decodes back to exactly that set, so the effect
    ///    a capture records covers every factor the body exercised (a body reading the clock beside a
    ///    random source is journaled as both, never as its least deterministic member). Its
    ///    companion law is the converse: a body that reads a factor OUTSIDE the recorded class is
    ///    not covered, and the difference names that factor;
    ///  - **stable enumeration** — `CapabilityRegistry.enumerate` is order-stable (by id) regardless of
    ///    insertion order;
    ///  - **declaration round-trip** — `CapabilityCodec.decode (encode c) = Ok c`;
    ///  - **a slotted artifact is invocable** (Phase 229) — a capability whose signature
    ///    `Function.signature` derives from an artifact carrying tree-typed slots registers,
    ///    enumerates and DISPATCHES with a conforming slot argument (a wire document of the slot's
    ///    kind), while a constructed non-conforming one is refused by name before the body runs: a
    ///    tree of the wrong kind is `ArgOutOfSpace` carrying the slot's `SlotTree` constraint, and a
    ///    scalar is `UninvocableArg`. Both refusals are BUILT every iteration.
    ///
    /// Since Phase 210 it also certifies the seam's `Deferred` envelope, in the shape `queryLaws`
    /// carries it (Phase 198): the envelope rides out of `invoke` and `dispatch` UNCHANGED on
    /// `Ready` / `Pending`, a refusal is typed and lands BEFORE the body runs, and a body's untyped
    /// `Failed` never rides out of the seam — it becomes the enumerated `BodyFailed`, so
    /// `Ok(Failed _)` is unreachable. The three shapes MIRROR the query family's rather than being
    /// instantiated from `deferredLaws`, which takes no witness.
    ///
    /// One of `queryLaws`' three is deliberately NOT mirrored here, and the reason is worth stating
    /// so it does not read as an omission: its first envelope law is a `Deferred&lt;QueryResult&gt;`
    /// WIRE round-trip, and this seam's counterpart already exists and is already certified —
    /// `CapabilityCodec.encodeDeferred` / `decodeDeferred` are value-codec-parameterised, and
    /// `deferredLaws` round-trips all three cases at an `int` payload. Restating it here would
    /// duplicate a law rather than mirror one. What is mirrored is the part `queryLaws` could only
    /// state about ITS seam: the three outcomes, and the unreachable fourth.
    let capabilityLaws (seed: int) (iterations: int) : LawResult list =
        let validation =
            LawKit.LawCell "arg-validation accepts in-space + rejects out-of-space / unknown args"

        let replay =
            LawKit.LawCell "a non-deterministic invocation replays byte-identically via capture"

        let enumeration = LawKit.LawCell "registry enumeration is stable (id-sorted)"

        let roundtrip =
            LawKit.LawCell "capability declaration round-trips through the codec"

        let envelope =
            LawKit.LawCell "the envelope rides out of invoke / dispatch unchanged for Ready and Pending"

        let asyncAxis =
            LawKit.LawCell "dispatch settles, stays pending, or refuses typed before the body runs"

        let capturedEffect =
            LawKit.LawCell "the effect a capture records covers every determinism factor the body exercised"

        let underDeclared =
            LawKit.LawCell "a body that reads a factor outside the recorded effect is not covered, and is named"

        let typedFailure =
            LawKit.LawCell "a body failure is a typed BodyFailed, never Ok(Failed _)"

        let slotted =
            LawKit.LawCell
                "a capability over a slotted artifact is invocable; a non-conforming slot arg is refused by name"

        // value-codec for the captured realized value (an int — the stand-in for a model output).
        let encodeV (v: int) : string = string v

        let decodeV (s: string) : Result<int, string> =
            match System.Int32.TryParse s with
            | true, v -> Ok v
            | _ -> Error("not an int: " + s)

        let hashFn = OpStream.defaultHash

        // Every non-empty determinism set over the three factors, in a fixed order; iteration `i`
        // declares the (i mod 7)th, so a run reaches the single-factor and the multi-factor labels
        // without a further draw from the cursor (Phase 319).
        let allFactors = [ ClockFactor; RandomFactor; NetworkFactor ]

        let nonEmptySets =
            [ for mask in 1..7 ->
                  allFactors
                  |> List.indexed
                  |> List.filter (fun (k, _) -> (mask >>> k) &&& 1 = 1)
                  |> List.map snd
                  |> Set.ofList ]

        LawKit.run iterations seed (fun rng i at ->
            let lo = rng.IntBelow 50
            let span = rng.IntBelow 50
            let hi = lo + span + 1

            let hole: SigEntry =
                { Addr = "h0"
                  Name = "x"
                  Kind = "value"
                  Space = Some(IntRange(lo, hi))
                  Slot = None
                  Action = None
                  Required = true }

            let sg: Signature =
                { Name = "cap" + string i
                  Holes = [ hole ]
                  Effect =
                    { Host = ReadsHost
                      Determinism = List.item (i % 7) nonEmptySets } }

            let cap = Capability.create ("cap-" + string i) sg (ClientIsland Pyodide)

            // arg-validation: in-space accepts; out-of-space + unknown-arg reject.
            let inSpace = string lo

            match Capability.validateArgs cap [ "h0", inSpace ] with
            | Ok() -> validation.Saw()
            | Error e -> validation.Check(false, fun () -> at (sprintf "rejected a valid arg: %A" e))

            (match Capability.validateArgs cap [ "h0", string (hi + 1) ] with
             | Error(ArgOutOfSpace _) -> validation.Saw()
             | other -> validation.Check(false, fun () -> at (sprintf "out-of-space not rejected: %A" other)))

            (match Capability.validateArgs cap [ "nope", inSpace ] with
             | Error(UnknownArg _) -> validation.Saw()
             | other -> validation.Check(false, fun () -> at (sprintf "unknown arg not rejected: %A" other)))

            // byte-identical replay through the Phase 27 seam.
            let realized = rng.IntBelow 1000
            let args = [ "h0", inSpace ]
            let key = Capability.invocationKey cap args
            let det = Capability.determinismTag cap

            let _, caps = OpStream.captureEffect hashFn encodeV det key (fun () -> realized) []

            let liveDifferent () = realized + 1 // a divergent live source

            match OpStream.replayEffect decodeV key det liveDifferent caps with
            | Ok(v, rest) ->
                replay.Check(
                    (v = realized && List.isEmpty rest),
                    fun () -> at (sprintf "replay ≠ recorded invocation (%d vs %d)" v realized)
                )
            | Error m -> replay.Check(false, fun () -> at (sprintf "replay errored: %s" m))

            // The effect a capture records covers every factor the body exercised (Phase 319). The
            // body reads the factors it is given, noting each; the journal is decoded back through
            // the canonical label, and the set it names must equal the declared class and so cover
            // every factor read — the whole class is journaled, not the least deterministic member.
            let captureReading (readSet: Set<DeterminismFactor>) =
                let seen = ref Set.empty

                let body () =
                    for f in readSet do
                        seen.Value <- Set.add f seen.Value

                    realized

                let _, journal = OpStream.captureEffect hashFn encodeV det key body []

                let recorded =
                    match journal with
                    | [ c ] -> Effect.tryDeterminismOfTag c.Determinism
                    | _ -> None

                recorded, seen.Value

            let host = cap.Signature.Effect.Host

            let covered (recorded: Set<DeterminismFactor>) (exercised: Set<DeterminismFactor>) =
                Effect.covers { Host = host; Determinism = recorded } { Host = host; Determinism = exercised }

            let declared = cap.Determinism

            for readSet in [ declared; declared |> Set.toList |> List.truncate 1 |> Set.ofList ] do
                match captureReading readSet with
                | Some recorded, seen ->
                    capturedEffect.Check(
                        (recorded = declared && covered recorded seen),
                        fun () ->
                            at (
                                sprintf
                                    "the capture recorded %A but the body exercised %A of declared %A"
                                    recorded
                                    seen
                                    declared
                            )
                    )
                | None, _ ->
                    capturedEffect.Check(false, fun () -> at "the capture journaled no decodable determinism label")

            // A body that reads a factor OUTSIDE the class is not covered by the record, and the
            // difference names exactly that factor — the law has teeth on an under-declaration.
            match allFactors |> List.tryFind (fun f -> not (Set.contains f declared)) with
            | Some outside ->
                match captureReading (Set.add outside declared) with
                | Some recorded, seen ->
                    underDeclared.Check(
                        (not (covered recorded seen))
                        && Set.difference seen recorded = Set.singleton outside,
                        fun () -> at (sprintf "a read of %A outside the recorded %A was not named" outside recorded)
                    )
                | None, _ ->
                    underDeclared.Check(false, fun () -> at "the capture journaled no decodable determinism label")
            | None -> ()

            // stable enumeration regardless of insertion order.
            let capB = Capability.create ("cap-a" + string i) sg BuildTime

            let reg =
                CapabilityRegistry.empty
                |> CapabilityRegistry.register cap
                |> Result.bind (CapabilityRegistry.register capB)

            (match reg with
             | Ok r ->
                 let ids = CapabilityRegistry.enumerate r |> List.map (fun c -> c.Id)
                 enumeration.Check((ids = List.sort ids), fun () -> at (sprintf "enumerate not id-sorted: %A" ids))
             | Error e -> enumeration.Check(false, fun () -> at (sprintf "register failed: %A" e)))

            // declaration round-trip.
            match CapabilityCodec.decode (CapabilityCodec.encode cap) with
            | Ok c2 -> roundtrip.Check((c2 = cap), fun () -> at "capability ≠ round-trip")
            | Error m -> roundtrip.Check(false, fun () -> at (sprintf "decode failed: %s" m))

            // ---- the Deferred envelope on the seam (Phase 210) ----

            // A declaration the registry refuses is a failure of the first law the registration
            // serves, and the iteration's remaining checks are skipped — never a throw.
            match CapabilityRegistry.register cap CapabilityRegistry.empty with
            | Error e -> envelope.Fail(at (sprintf "the built declaration was refused by the registry: %A" e))
            | Ok creg ->
                // SETTLED and PENDING: the body's envelope rides out of the seam unchanged, through the
                // capability-level `invoke` and the registry-level `dispatch` alike.
                for answer in [ Ready realized; Pending ] do
                    let direct = Capability.invoke cap args (fun () -> answer)
                    let dispatched = CapabilityRegistry.dispatch creg cap.Id args (fun _ () -> answer)

                    envelope.Check(
                        (direct = Ok answer && dispatched = Ok answer),
                        fun () ->
                            at (
                                sprintf
                                    "%A did not ride out unchanged (invoke %A, dispatch %A)"
                                    answer
                                    direct
                                    dispatched
                            )
                    )

                // REFUSED: typed, and before the body runs — on a rejected arg set and on an
                // unregistered id alike.
                let ran = ref false

                (match
                    CapabilityRegistry.dispatch creg cap.Id [ "h0", string (hi + 1) ] (fun _ () ->
                        ran.Value <- true
                        Ready realized)
                 with
                 | Error(ArgOutOfSpace _) when not ran.Value -> asyncAxis.Saw()
                 | other ->
                     asyncAxis.Check(
                         false,
                         fun () -> at (sprintf "refusal not typed-before-body (%A; body ran: %b)" other ran.Value)
                     ))

                (match
                    CapabilityRegistry.dispatch creg "no-such-capability" args (fun _ () ->
                        ran.Value <- true
                        Ready realized)
                 with
                 | Error(NoSuchCapability _) when not ran.Value -> asyncAxis.Saw()
                 | other ->
                     asyncAxis.Check(
                         false,
                         fun () ->
                             at (
                                 sprintf
                                     "an unregistered id was not refused before the body (%A; body ran: %b)"
                                     other
                                     ran.Value
                             )
                     ))

                // a body's untyped failure never rides out of the seam: it becomes the enumerated
                // `BodyFailed`, so `Ok(Failed _)` is unreachable. Exhausts the body's three answers
                // rather than asserting the fourth away.
                for answer in [ Ready realized; Pending; Failed("boom-" + string i) ] do
                    match CapabilityRegistry.dispatch creg cap.Id args (fun _ () -> answer), answer with
                    | Error(BodyFailed m), Failed fm when m = fm -> typedFailure.Saw()
                    | Ok d, (Ready _ | Pending) when d = answer -> typedFailure.Saw()
                    | other, _ ->
                        typedFailure.Check(
                            false,
                            fun () -> at (sprintf "a %A body did not project to a typed outcome: %A" answer other)
                        )

                // Phase 229 — a capability over a slotted artifact is invocable. The artifact is a
                // one-node witness declaring a CONSTRAINED slot (a drawn kind), an UNCONSTRAINED slot
                // and a value hole; its signature is derived by `Function.signature`, never written by
                // hand, so the law is about what the seam is actually given.
                let kind = "k" + string (lo % 7)
                let slotAddr, anyAddr, valueAddr = "tpl/body", "tpl/any", "tpl/n"

                let slottedWitness: ArtifactWitness<unit, string> =
                    { Tree =
                        { Id = fun () -> "tpl"
                          KindTag = fun () -> "tpl"
                          Children = fun () -> []
                          ReplaceChildren = fun () _ -> () }
                      IdW =
                        { ToString = id
                          OfString = id
                          Equals = (=) }
                      Holes =
                        fun () ->
                            [ { Addr = slotAddr
                                Name = "body"
                                Kind = SlotHole(Some kind) }
                              { Addr = anyAddr
                                Name = "any"
                                Kind = SlotHole None }
                              { Addr = valueAddr
                                Name = "n"
                                Kind = ValueHole(IntRange(lo, hi)) } ]
                      Effect = fun () -> sg.Effect
                      Bind = fun _ _ () -> Ok() }

                let slottedCap =
                    Capability.create ("slotted-" + string i) (Function.signature slottedWitness "slotted" ()) Server

                let tree (k: string) =
                    Json.render (Json.kindObj k [ "n", JInt i ])

                let conforming =
                    [ slotAddr, tree kind; anyAddr, tree ("free" + string i); valueAddr, string lo ]

                let wrongKind =
                    [ slotAddr, tree (kind + "x"); anyAddr, tree kind; valueAddr, string lo ]

                let scalar = [ slotAddr, tree kind; anyAddr, string lo; valueAddr, string lo ]

                let failSlotted msg = slotted.Check(false, fun () -> at msg)

                match CapabilityRegistry.register slottedCap CapabilityRegistry.empty with
                | Error e -> failSlotted (sprintf "a slotted capability did not register: %A" e)
                | Ok sreg ->
                    slotted.Check(
                        CapabilityRegistry.enumerate sreg |> List.map (fun c -> c.Id) = [ slottedCap.Id ],
                        fun () -> at "a registered slotted capability is not enumerated"
                    )

                    let ran = ref false

                    let run a =
                        ran.Value <- false

                        CapabilityRegistry.dispatch sreg slottedCap.Id a (fun _ () ->
                            ran.Value <- true
                            Ready realized)

                    (match run conforming with
                     | Ok(Ready v) when v = realized && ran.Value -> slotted.Saw()
                     | other -> failSlotted (sprintf "a conforming slot argument did not dispatch: %A" other))

                    (match run wrongKind with
                     | Error(ArgOutOfSpace(a, SlotTree(Some c), _)) when a = slotAddr && c = kind && not ran.Value ->
                         slotted.Saw()
                     | other ->
                         failSlotted (
                             sprintf "a tree of the wrong kind was not refused by name before the body: %A" other
                         ))

                    (match run scalar with
                     | Error(UninvocableArg a) when a = anyAddr && not ran.Value -> slotted.Saw()
                     | other ->
                         failSlotted (sprintf "a scalar bound to a slot was not refused as uninvocable: %A" other)))

        LawKit.results
            [ validation
              replay
              enumeration
              roundtrip
              envelope
              asyncAxis
              typedFailure
              slotted
              capturedEffect
              underDeclared ]

    /// The capability-seam laws at a DOMAIN'S seam (Phase 246). `capabilityLaws` beside it builds
    /// its own capabilities from the seed and certifies Core's `CapabilityRegistry.dispatch`; it cannot see a
    /// domain's registry, body or host path, so a host that runs the body before the registry
    /// refuses leaves it green. This form runs the domain's own `CapabilitySeamWitness` — every call
    /// the witness's generator draws goes through the witness's `Dispatch` with its `Body`, counted —
    /// and certifies:
    ///
    ///  - **three outcomes** — every dispatch settles (`Ok(Ready _)`), stays pending (`Ok Pending`)
    ///    or is refused typed (`Error _`); `Ok(Failed _)` never escapes, and a `BodyFailed` carries the
    ///    body's own failure and nothing else;
    ///  - **a refusal precedes the body** — a typed refusal ran no body, and a settled, pending or
    ///    body-failed dispatch ran it exactly once;
    ///  - **the host is the registry's** — a call reaches the body iff its id is registered and
    ///    `Capability.validateArgs` accepts its arguments, and a refused call carries the error the
    ///    registry itself gives (`NoSuchCapability` naming the registered ids, or the validation
    ///    error), so a host can neither add a refusal nor drop one.
    ///
    /// **Vacuity.** The guard counts settled, pending and refused-before-the-body dispatches over the
    /// drawn calls; a generator that never reaches one of the three is starved, and the family says
    /// so rather than reporting green. A thrown `Body` or `Dispatch` is a failure of the first law.
    ///
    /// The capability instance of `LawKit.seamLaws` (Phase 297); `queryLawsAt` is the query one.
    /// `family` is the roster id the guard is labelled with — `Conformance.capabilityLawsAt`, or the
    /// `…With` spelling for the obsolete forward that keeps its own id for one draft.
    let capabilityLawsAt
        (family: string)
        (w: CapabilitySeamWitness<'v>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let known = CapabilityRegistry.enumerate w.Registry |> List.map (fun c -> c.Id)

        LawKit.seamLaws
            { Lookup =
                fun id ->
                    match CapabilityRegistry.tryFind id w.Registry with
                    | None -> Error(NoSuchCapability(id, known))
                    | Some c -> Ok c
              Validate = Capability.validateArgs
              Body = fun args c -> w.Body args c ()
              Dispatch = fun id args body -> w.Dispatch id args (fun c () -> body c)
              Gen = w.GenCall
              Rendering =
                { Family = family
                  Laws =
                    "capability dispatch at the domain has three outcomes (settled, pending, refused typed); Ok(Failed _) never escapes",
                    "a refusal precedes the body at the domain's host (refused: no body; dispatched: exactly one)",
                    "the domain's host agrees with its registry (reaches the body iff admitted; refuses with the registry's error)"
                  Runner = "body"
                  BodyFailedName = "BodyFailed"
                  TryBodyFailed =
                    fun e ->
                        match e with
                        | BodyFailed m -> Some m
                        | _ -> None
                  BodyFailed = BodyFailed } }
            seed
            iterations
