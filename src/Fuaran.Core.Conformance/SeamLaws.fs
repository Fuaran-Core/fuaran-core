namespace Fuaran.Core

/// The seam families (Phase 297 split): capabilities, queries, registries, packs, the columnar layer, deferreds and pipelines.
module internal SeamLaws =

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
    ///  - **stable enumeration** — `Registry.enumerate` is order-stable (by id) regardless of
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
                Registry.empty |> Registry.register cap |> Result.bind (Registry.register capB)

            (match reg with
             | Ok r ->
                 let ids = Registry.enumerate r |> List.map (fun c -> c.Id)
                 enumeration.Check((ids = List.sort ids), fun () -> at (sprintf "enumerate not id-sorted: %A" ids))
             | Error e -> enumeration.Check(false, fun () -> at (sprintf "register failed: %A" e)))

            // declaration round-trip.
            match CapabilityCodec.decode (CapabilityCodec.encode cap) with
            | Ok c2 -> roundtrip.Check((c2 = cap), fun () -> at "capability ≠ round-trip")
            | Error m -> roundtrip.Check(false, fun () -> at (sprintf "decode failed: %s" m))

            // ---- the Deferred envelope on the seam (Phase 210) ----

            // A declaration the registry refuses is a failure of the first law the registration
            // serves, and the iteration's remaining checks are skipped — never a throw.
            match Registry.register cap Registry.empty with
            | Error e -> envelope.Fail(at (sprintf "the built declaration was refused by the registry: %A" e))
            | Ok creg ->
                // SETTLED and PENDING: the body's envelope rides out of the seam unchanged, through the
                // capability-level `invoke` and the registry-level `dispatch` alike.
                for answer in [ Ready realized; Pending ] do
                    let direct = Capability.invoke cap args (fun () -> answer)
                    let dispatched = Registry.dispatch creg cap.Id args (fun _ () -> answer)

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
                    Registry.dispatch creg cap.Id [ "h0", string (hi + 1) ] (fun _ () ->
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
                    Registry.dispatch creg "no-such-capability" args (fun _ () ->
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
                    match Registry.dispatch creg cap.Id args (fun _ () -> answer), answer with
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

                match Registry.register slottedCap Registry.empty with
                | Error e -> failSlotted (sprintf "a slotted capability did not register: %A" e)
                | Ok sreg ->
                    slotted.Check(
                        Registry.enumerate sreg |> List.map (fun c -> c.Id) = [ slottedCap.Id ],
                        fun () -> at "a registered slotted capability is not enumerated"
                    )

                    let ran = ref false

                    let run a =
                        ran.Value <- false

                        Registry.dispatch sreg slottedCap.Id a (fun _ () ->
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
    /// its own capabilities from the seed and certifies Core's `Registry.dispatch`; it cannot see a
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
        let known = Registry.enumerate w.Registry |> List.map (fun c -> c.Id)

        LawKit.seamLaws
            { Lookup =
                fun id ->
                    match Registry.tryFind id w.Registry with
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

    /// Certify the `Fuaran.Core.Query` data-acquisition seam (Phase 46): typed-param validation
    /// (in-type accepts; wrong-type + unknown reject; since Phase 226 the all-`Null` argument set,
    /// built from the declaration, is refused as `RequiredParamsNull` naming every required param,
    /// while an optional param bound to `Null` is still accepted), a non-deterministic query replays
    /// byte-identically through the Phase 27 capture seam, registry enumeration is id-stable, and the
    /// declaration + result round-trip the codec. Mirrors `capabilityLaws`.
    ///
    /// Since Phase 198 it also certifies the seam's `Deferred` envelope: the
    /// `Deferred&lt;QueryResult&gt;` wire round-trip, that a dispatch has exactly the three outcomes
    /// SETTLED / PENDING / typed-REFUSED, and that a resolver's untyped `Failed` never rides out of
    /// the seam — it becomes the enumerated `ExecutionFailed`, so `Ok(Failed _)` is unreachable. The
    /// three shapes MIRROR `deferredLaws` rather than delegating to it: that family takes no witness
    /// (it is self-contained at an `int` payload), so there is nothing to instantiate over a query.
    let queryLaws (seed: int) (iterations: int) : LawResult list =
        let validation =
            LawKit.LawCell
                "param-validation accepts in-type + rejects type-mismatch / unknown params / a required param bound to Null"

        let replay =
            LawKit.LawCell "a non-deterministic query replays byte-identically via capture"

        let enumeration = LawKit.LawCell "query registry enumeration is stable (id-sorted)"

        let roundtrip =
            LawKit.LawCell "query declaration + result round-trip through the codec"

        let envelope =
            LawKit.LawCell "the query envelope round-trips the wire for Pending / Ready / Failed"

        let asyncAxis =
            LawKit.LawCell "dispatch settles, stays pending, or refuses typed before the resolver runs"

        let typedFailure =
            LawKit.LawCell "a resolver failure is a typed ExecutionFailed, never Ok(Failed _)"

        // value-codec for the captured realized result (the QueryResult itself, rendered canonically).
        let encodeV (qr: QueryResult) : string = QueryCodec.encodeResult qr

        let decodeV (s: string) : Result<QueryResult, string> =
            QueryCodec.decodeResult s |> Result.mapError (fun e -> sprintf "%A" e)

        let hashFn = OpStream.defaultHash

        LawKit.run iterations seed (fun rng i at ->
            let nRows = rng.IntBelow 5

            let q: Query =
                { Id = "q-" + string i
                  Params =
                    [ { Name = "p0"
                        Type = IntType
                        Required = true } ]
                  ResultSchema = [ "n", IntType ]
                  Effect =
                    { Host = ReadsHost
                      Determinism = Effect.network }
                  Source = Ref("src-" + string i)
                  TimeoutMs = Some 5000
                  PageSize = None }

            // param-validation: in-type accepts; wrong-type + unknown reject.
            (match Query.validateParams q [ "p0", Int 42 ] with
             | Ok() -> validation.Saw()
             | Error e -> validation.Check(false, fun () -> at (sprintf "rejected a valid param: %A" e)))

            (match Query.validateParams q [ "p0", Str "nope" ] with
             | Error(ParamTypeMismatch _) -> validation.Saw()
             | other -> validation.Check(false, fun () -> at (sprintf "type-mismatch not rejected: %A" other)))

            (match Query.validateParams q [ "nope", Int 1 ] with
             | Error(UnknownParam _) -> validation.Saw()
             | other -> validation.Check(false, fun () -> at (sprintf "unknown param not rejected: %A" other)))

            // `Required` means non-null (Phase 226): the all-`Null` argument set, BUILT from the
            // declaration rather than drawn, is refused as `RequiredParamsNull` naming every required
            // param and only those; an optional param may still be bound to `Null`.
            let qOpt =
                { q with
                    Params =
                        q.Params
                        @ [ { Name = "p1"
                              Type = StringType
                              Required = false } ] }

            let allNull = qOpt.Params |> List.map (fun p -> p.Name, Null)

            let requiredNames =
                qOpt.Params |> List.filter (fun p -> p.Required) |> List.map (fun p -> p.Name)

            (match Query.validateParams qOpt allNull with
             | Error(RequiredParamsNull names) when names = requiredNames -> validation.Saw()
             | other ->
                 validation.Check(
                     false,
                     fun () -> at (sprintf "a required param bound to Null was not refused: %A" other)
                 ))

            (match Query.validateParams qOpt [ "p0", Int 42; "p1", Null ] with
             | Ok() -> validation.Saw()
             | Error e ->
                 validation.Check(false, fun () -> at (sprintf "an optional param bound to Null was refused: %A" e)))

            // byte-identical replay of the realized result through the Phase 27 seam.
            let realized: QueryResult =
                { Rows =
                    { Schema = [ "n", IntType ]
                      Columns =
                        [ { Name = "n"
                            Type = IntType
                            Cells = List.init nRows (fun k -> Int k) } ] }
                  PageNum = 0
                  TotalRowCount = Some nRows
                  NextPageToken = None }

            let key = Query.invocationKey q [ "p0", Int 42 ]
            let det = Query.determinismTag q
            let _, caps = OpStream.captureEffect hashFn encodeV det key (fun () -> realized) []

            // a divergent live source: a different page number.
            let liveDifferent () =
                { realized with
                    PageNum = realized.PageNum + 1 }

            (match OpStream.replayEffect decodeV key det liveDifferent caps with
             | Ok(v, rest) ->
                 replay.Check((v = realized && List.isEmpty rest), fun () -> at "replay ≠ recorded result")
             | Error m -> replay.Check(false, fun () -> at (sprintf "replay errored: %s" m)))

            // stable enumeration regardless of insertion order.
            let qB = { q with Id = "q-a" + string i }

            (match
                QueryRegistry.empty
                |> QueryRegistry.register q
                |> Result.bind (QueryRegistry.register qB)
             with
             | Ok r ->
                 let ids = QueryRegistry.enumerate r |> List.map (fun x -> x.Id)
                 enumeration.Check((ids = List.sort ids), fun () -> at (sprintf "enumerate not id-sorted: %A" ids))
             | Error e -> enumeration.Check(false, fun () -> at (sprintf "register failed: %A" e)))

            // declaration + result round-trip.
            (match QueryCodec.decode (QueryCodec.encode q) with
             | Ok q2 -> roundtrip.Check((q2 = q), fun () -> at "query ≠ round-trip")
             | Error m -> roundtrip.Check(false, fun () -> at (sprintf "query decode failed: %A" m)))

            (match QueryCodec.decodeResult (QueryCodec.encodeResult realized) with
             | Ok qr2 -> roundtrip.Check((qr2 = realized), fun () -> at "result ≠ round-trip")
             | Error m -> roundtrip.Check(false, fun () -> at (sprintf "result decode failed: %A" m)))

            // ---- the Deferred envelope on the seam (Phase 198) ----

            // the envelope round-trips the wire for all three cases, at a QueryResult payload.
            for d in [ Pending; Ready realized; Failed("resolver-" + string i) ] do
                match QueryCodec.decodeDeferredResult (QueryCodec.encodeDeferredResult d) with
                | Ok d2 -> envelope.Check((d2 = d), fun () -> at (sprintf "Deferred<QueryResult> ≠ round-trip (%A)" d))
                | Error e -> envelope.Check(false, fun () -> at (sprintf "Deferred<QueryResult> decode failed: %A" e))

            // a dispatch has exactly three outcomes, and the refusals stay typed and pre-resolver. A
            // declaration the registry refuses is a failure of the first law the registration serves,
            // and the iteration's remaining checks are skipped — never a throw.
            match QueryRegistry.register q QueryRegistry.empty with
            | Error e -> asyncAxis.Fail(at (sprintf "the built declaration was refused by the registry: %A" e))
            | Ok reg ->
                let goodArgs = [ "p0", Int 42 ]

                (match QueryRegistry.dispatch reg q.Id goodArgs (fun _ -> Ready realized) with
                 | Ok(Ready r) when r = realized -> asyncAxis.Saw()
                 | other ->
                     asyncAxis.Check(false, fun () -> at (sprintf "a settled resolver did not settle: %A" other)))

                (match QueryRegistry.dispatch reg q.Id goodArgs (fun _ -> Pending) with
                 | Ok Pending -> asyncAxis.Saw()
                 | other ->
                     asyncAxis.Check(false, fun () -> at (sprintf "a pending resolver did not stay pending: %A" other)))

                let ran = ref false

                (match
                    QueryRegistry.dispatch reg q.Id [ "p0", Str "nope" ] (fun _ ->
                        ran.Value <- true
                        Ready realized)
                 with
                 | Error(ParamTypeMismatch _) when not ran.Value -> asyncAxis.Saw()
                 | other ->
                     asyncAxis.Check(
                         false,
                         fun () ->
                             at (sprintf "refusal not typed-before-resolve (%A; resolver ran: %b)" other ran.Value)
                     ))

                (match QueryRegistry.dispatch reg "no-such-query" goodArgs (fun _ -> Ready realized) with
                 | Error(NoSuchQuery _) -> asyncAxis.Saw()
                 | other ->
                     asyncAxis.Check(false, fun () -> at (sprintf "an unregistered id was not refused: %A" other)))

                // a resolver's untyped failure never rides out of the seam — `Ok(Failed _)` is unreachable.
                match QueryRegistry.dispatch reg q.Id goodArgs (fun _ -> Failed("boom-" + string i)) with
                | Error(ExecutionFailed(m, _)) when m = "boom-" + string i -> typedFailure.Saw()
                | other ->
                    typedFailure.Check(
                        false,
                        fun () -> at (sprintf "a resolver failure did not become ExecutionFailed: %A" other)
                    ))

        LawKit.results
            [ validation
              replay
              enumeration
              roundtrip
              envelope
              asyncAxis
              typedFailure ]

    /// The query-seam laws at a DOMAIN'S seam (Phase 246) — `capabilityLawsWith`'s three laws, over
    /// the domain's own `QuerySeamWitness`: every drawn call goes through the witness's `Dispatch`
    /// with its `Resolver`, counted, and the family certifies that a dispatch has exactly three
    /// outcomes (`Ok(Failed _)` never escapes, and an `ExecutionFailed` carries the resolver's own
    /// failure), that **a refused dispatch runs no resolver** (and a dispatched one runs it exactly
    /// once), and that the host agrees with the registry: a call reaches the resolver iff its id is
    /// registered and `Query.validateParams` accepts its arguments, and a refused call carries the
    /// registry's own error. `queryLaws` beside it certifies Core's seam at Core's fixtures and
    /// cannot see any of this.
    ///
    /// **Vacuity.** Guarded on the three outcomes — settled, pending and refused before the
    /// resolver — each of which the domain's generator must reach.
    ///
    /// The query instance of `LawKit.seamLaws` (Phase 297); `capabilityLawsAt` is the capability one.
    /// `family` labels the guard, as there.
    let queryLawsAt (family: string) (w: QuerySeamWitness) (seed: int) (iterations: int) : LawResult list =
        let known = QueryRegistry.enumerate w.Queries |> List.map (fun q -> q.Id)

        // Phase 302 — the result law. Nothing bound a resolver's answer to the query's declared
        // `ResultSchema`: `Query.invoke` hands a `Ready` result back as the resolver built it, and the
        // seam laws compare the host with the registry, never the answer with the declaration — so a
        // resolver answering a table of a different schema, or rows `Table.validate` refuses, was
        // green. The law carries it, Core-side `invoke` is unchanged (DECISIONS: a new `QueryError`
        // case would break every exhaustive match over it).
        let bound =
            LawKit.LawCell
                "a settled query result is bound to its declaration (Rows.Schema = ResultSchema, and Table.validate accepts the rows)"

        LawKit.run iterations (seed + 7919) (fun rng _ at ->
            let id, args = rng.Draw w.GenQuery

            match QueryRegistry.tryFind id w.Queries with
            | None -> ()
            | Some q ->
                match w.Dispatch id args (fun q' -> w.Resolver args q') with
                | Ok(Ready r) ->
                    let schemaOk = r.Rows.Schema = q.ResultSchema

                    let rowsOk =
                        match Table.validate r.Rows with
                        | Ok() -> None
                        | Error e -> Some e

                    bound.Check(
                        schemaOk && rowsOk.IsNone,
                        fun () ->
                            at (
                                sprintf
                                    "query %s settled with schema %A (declared %A)%s"
                                    id
                                    r.Rows.Schema
                                    q.ResultSchema
                                    (match rowsOk with
                                     | Some e -> sprintf " and rows Table.validate refuses: %A" e
                                     | None -> "")
                            )
                    )
                | _ -> ())

        let isGuard (r: LawResult) =
            let p = SampleAdequacy.guardOpening
            r.Law.Length >= p.Length && r.Law.Substring(0, p.Length) = p

        let seam =
            LawKit.seamLaws
                { Lookup =
                    fun id ->
                        match QueryRegistry.tryFind id w.Queries with
                        | None -> Error(NoSuchQuery(id, known))
                        | Some q -> Ok q
                  Validate = Query.validateParams
                  Body = fun args q -> w.Resolver args q
                  Dispatch = w.Dispatch
                  Gen = w.GenQuery
                  Rendering =
                    { Family = family
                      Laws =
                        "query dispatch at the domain has three outcomes (settled, pending, refused typed); Ok(Failed _) never escapes",
                        "a refused dispatch runs no resolver at the domain's host (refused: none; dispatched: exactly one)",
                        "the domain's host agrees with its query registry (reaches the resolver iff admitted; refuses with the registry's error)"
                      Runner = "resolver"
                      BodyFailedName = "ExecutionFailed"
                      TryBodyFailed =
                        fun e ->
                            match e with
                            | ExecutionFailed(m, _) -> Some m
                            | _ -> None
                      BodyFailed = fun m -> ExecutionFailed(m, []) } }
                seed
                iterations

        (seam |> List.filter (isGuard >> not))
        @ LawKit.results [ bound ]
        @ (seam |> List.filter isGuard)

    // ---- signature-typed function registry (Phase 50) ----
    // The teeth on `FunctionEntry` / `FunctionRegistry` + `findBySignature`: the artifact-function
    // catalogue queried BY SIGNATURE (result type + required-hole shape), extending the Phase-30
    // `Capability` registry pattern (default-deny dispatch + arg-validated invocation carried over).

    /// The signature-typed registry laws (Phase 50) — the teeth on `FunctionEntry` / `FunctionRegistry`
    /// + `findBySignature`. Self-contained (it builds its own functions from the seed); over a
    /// seed-replayable sample it certifies:
    ///
    ///  - **findable by its declared result/holes** — an entry is returned by a query carrying its own
    ///    result type + its required holes as the available context, under BOTH structural-subsumption
    ///    AND exact matching (the registry indexes it by what it produces + requires);
    ///  - **a non-matching query returns it not** — a query with the wrong result type, or with a
    ///    context missing a required hole, does NOT return the entry (default-deny by shape on search);
    ///  - **a partial application narrows its signature in the index** — `partiallyApply` (the content-
    ///    pack formalism) yields an entry with fewer required holes that IS findable from the smaller
    ///    context that subsumes it, while the un-narrowed original is NOT (its dropped hole stays unmet);
    ///  - **dispatch stays default-deny + arg-validated** — an unregistered id is `NoSuchCapability`, a
    ///    registered id with in-space args runs the body, and an out-of-space arg is rejected
    ///    (`ArgOutOfSpace`) before the body runs (the Capability trust posture, carried over).
    let registryLaws (seed: int) (iterations: int) : LawResult list =
        let findable =
            LawKit.LawCell "a function is findable by its declared result type + required holes (subsumption + exact)"

        let nonMatch =
            LawKit.LawCell "a non-matching query (wrong result type / unmet hole) returns it not"

        let narrowing =
            LawKit.LawCell
                "a partial application narrows its signature in the index (content pack findable by the smaller context)"

        let defaultDeny =
            LawKit.LawCell
                "dispatch stays default-deny + arg-validated (unregistered id refused, out-of-space arg rejected)"

        LawKit.run iterations seed (fun rng i at ->
            let lo = rng.IntBelow 50
            let span = rng.IntBelow 50
            let hi = lo + span + 1

            let mkHole addr : SigEntry =
                { Addr = addr
                  Name = addr
                  Kind = "value"
                  Space = Some(IntRange(lo, hi))
                  Slot = None
                  Action = None
                  Required = true }

            let h0 = mkHole "h0"
            let h1 = mkHole "h1"

            let sg: Signature =
                { Name = "fn" + string i
                  Holes = [ h0; h1 ]
                  Effect = Effect.pureDeterministic }

            let resultType = "doc"
            let cap = Capability.create ("fn-" + string i) sg BuildTime
            let ent = FunctionRegistry.entry resultType cap

            match FunctionRegistry.empty |> FunctionRegistry.register ent with
            | Error e -> findable.Check(false, fun () -> at (sprintf "register failed: %A" e))
            | Ok r ->
                // ---- 1. findable by its declared result/holes — subsumption + exact ----
                let fullQuery =
                    { ResultType = Some resultType
                      Available = [ h0; h1 ] }

                let bySub =
                    FunctionRegistry.findBySignature Subsumes fullQuery r
                    |> List.map (fun e -> e.Capability.Id)

                let byExact =
                    FunctionRegistry.findBySignature Exact fullQuery r
                    |> List.map (fun e -> e.Capability.Id)

                findable.Check(
                    List.contains cap.Id bySub && List.contains cap.Id byExact,
                    fun () -> at (sprintf "entry not findable by its own result/holes (sub=%A exact=%A)" bySub byExact)
                )

                // ---- 2. a non-matching query returns it not — wrong result type; unmet required hole ----
                let wrongResult =
                    { ResultType = Some "other"
                      Available = [ h0; h1 ] }

                let missingHole =
                    { ResultType = Some resultType
                      Available = [ h0 ] } // h1 unmet

                let nm1 = FunctionRegistry.findBySignature Subsumes wrongResult r
                let nm2 = FunctionRegistry.findBySignature Subsumes missingHole r

                nonMatch.Check(
                    List.isEmpty nm1 && List.isEmpty nm2,
                    fun () ->
                        at (
                            sprintf
                                "a non-matching query returned the entry (wrongResult=%d missingHole=%d)"
                                (List.length nm1)
                                (List.length nm2)
                        )
                )

                // ---- 3. a partial application narrows its signature in the index ----
                let pack =
                    FunctionRegistry.partiallyApply ("pack-" + string i) (Set.ofList [ "h0" ]) ent

                (match FunctionRegistry.register pack r with
                 | Error e ->
                     narrowing.Check(false, fun () -> at (sprintf "registering the content pack failed: %A" e))
                 | Ok r2 ->
                     // the smaller context {h1} subsumes the pack (one required hole) but NOT the
                     // original (needs h0 + h1) — the narrowed signature is what is now in the index.
                     let smallQuery =
                         { ResultType = Some resultType
                           Available = [ h1 ] }

                     let ids =
                         FunctionRegistry.findBySignature Subsumes smallQuery r2
                         |> List.map (fun e -> e.Capability.Id)

                     let packRequired =
                         pack.Capability.Signature.Holes
                         |> List.filter (fun h -> h.Required)
                         |> List.map (fun h -> h.Addr)

                     narrowing.Check(
                         List.contains pack.Capability.Id ids
                         && not (List.contains cap.Id ids)
                         && packRequired = [ "h1" ],
                         fun () ->
                             at (
                                 sprintf
                                     "partial application did not narrow in the index (found=%A packRequired=%A)"
                                     ids
                                     packRequired
                             )
                     ))

                // ---- 4. dispatch stays default-deny + arg-validated ----
                // the body answers in the `Deferred` envelope since Phase 210; this one settles.
                let body (_: FunctionEntry) () = Ready 1

                let unreg =
                    FunctionRegistry.dispatch r "nope" [ "h0", string lo; "h1", string lo ] body

                let okCall =
                    FunctionRegistry.dispatch r cap.Id [ "h0", string lo; "h1", string lo ] body

                let badArg =
                    FunctionRegistry.dispatch r cap.Id [ "h0", string (hi + 1); "h1", string lo ] body

                let denyOk =
                    match unreg, okCall, badArg with
                    | Error(NoSuchCapability _), Ok(Ready 1), Error(ArgOutOfSpace _) -> true
                    | _ -> false

                defaultDeny.Check(
                    denyOk,
                    fun () ->
                        at (
                            sprintf
                                "dispatch not default-deny / arg-validated (unreg=%A ok=%A bad=%A)"
                                unreg
                                okCall
                                badArg
                        )
                ))

        LawKit.results [ findable; nonMatch; narrowing; defaultDeny ]

    // ---- content-pack loading contract (Phase 57) ----
    // The teeth on `PackManifest` / `ContentPack.load` + the signature-version compatibility check: a
    // content pack distributes as curried artifact-functions + a manifest and loads into the Phase-50
    // signature-typed registry through one mechanism, carrying no pack content (FGP 6).

    /// The content-pack loading-contract laws (Phase 57). Self-contained (it builds its own base
    /// functions + packs from the seed); over a seed-replayable sample it certifies:
    ///
    ///  - **load round-trip** — a pack of curried functions loads, and each packed function appears under
    ///    its NARROWED signature (findable from the smaller context the partial application now subsumes —
    ///    the content-pack formalism carried to the distribution boundary);
    ///  - **version-mismatch fails loudly** — a pack pinned to a stale base-signature fingerprint is
    ///    refused with `SignatureVersionMismatch` (naming declared + actual), never bound stale;
    ///  - **default-deny on an unknown base** — a pack naming an unregistered base is
    ///    `UnknownBaseFunction` (enumerating the known ids), never a silent skip;
    ///  - **the version is genuinely shape-derived** — changing the hole set shifts the fingerprint
    ///    (`signatureFingerprint sg ≠ signatureFingerprint sg'`), so the version check is real
    ///    change-detection, not a hand-incremented counter a host can forget to bump.
    let packLoadingLaws (seed: int) (iterations: int) : LawResult list =
        let roundTrip =
            LawKit.LawCell "a content pack loads and each curried function is findable under its narrowed signature"

        let mismatch =
            LawKit.LawCell
                "a pack pinned to a stale base-signature version is refused loudly (SignatureVersionMismatch)"

        let unknownBase =
            LawKit.LawCell "an unknown base is default-denied (UnknownBaseFunction enumerates the known ids)"

        let shapeDerived =
            LawKit.LawCell "the signature version is shape-derived (a changed hole set shifts the fingerprint)"

        LawKit.run iterations seed (fun rng i at ->
            let lo = rng.IntBelow 50
            let span = rng.IntBelow 50
            let hi = lo + span + 1

            let mkHole addr : SigEntry =
                { Addr = addr
                  Name = addr
                  Kind = "value"
                  Space = Some(IntRange(lo, hi))
                  Slot = None
                  Action = None
                  Required = true }

            let h0 = mkHole "h0"
            let h1 = mkHole "h1"

            let sg: Signature =
                { Name = "fn" + string i
                  Holes = [ h0; h1 ]
                  Effect = Effect.pureDeterministic }

            let resultType = "doc"
            let baseCap = Capability.create ("base-" + string i) sg BuildTime
            let baseEntry = FunctionRegistry.entry resultType baseCap

            match FunctionRegistry.empty |> FunctionRegistry.register baseEntry with
            | Error e -> roundTrip.Check(false, fun () -> at (sprintf "base register failed: %A" e))
            | Ok reg ->
                // ---- 1. load round-trip — curry h0; the narrowed entry is findable from {h1} ----
                let pf = ContentPack.pack ("pack-" + string i) (Set.ofList [ "h0" ]) baseEntry

                let manifest =
                    { PackId = "P" + string i
                      Domain = "ref"
                      PackVersion = 1
                      Functions = [ pf ] }

                (match ContentPack.load manifest reg with
                 | Error e -> roundTrip.Check(false, fun () -> at (sprintf "load of a valid pack failed: %A" e))
                 | Ok loaded ->
                     let smallQuery =
                         { ResultType = Some resultType
                           Available = [ h1 ] }

                     let ids =
                         FunctionRegistry.findBySignature Subsumes smallQuery loaded
                         |> List.map (fun e -> e.Capability.Id)

                     roundTrip.Check(
                         List.contains pf.NewId ids,
                         fun () ->
                             at (
                                 sprintf
                                     "loaded packed function not findable under its narrowed signature (found=%A)"
                                     ids
                             )
                     ))

                // ---- 2. a stale-version pack fails loudly ----
                let stale =
                    { pf with
                        BaseSignatureVersion = pf.BaseSignatureVersion + "X" }

                let staleManifest = { manifest with Functions = [ stale ] }

                (match ContentPack.load staleManifest reg with
                 | Error(SignatureVersionMismatch(_, baseId, declared, actual)) ->
                     mismatch.Check(
                         (baseId = baseCap.Id && declared <> actual),
                         fun () ->
                             at (
                                 sprintf
                                     "mismatch error fields wrong (base=%s declared=%s actual=%s)"
                                     baseId
                                     declared
                                     actual
                             )
                     )
                 | other ->
                     mismatch.Check(false, fun () -> at (sprintf "stale-version pack not refused loudly: %A" other)))

                // ---- 3. an unknown base is default-denied (enumerating the known ids) ----
                let ghost =
                    { NewId = "ghost-" + string i
                      BaseId = "no-such-base"
                      BaseSignatureVersion = pf.BaseSignatureVersion
                      BoundAddrs = Set.ofList [ "h0" ] }

                let ghostManifest = { manifest with Functions = [ ghost ] }

                (match ContentPack.load ghostManifest reg with
                 | Error(UnknownBaseFunction(_, "no-such-base", known)) ->
                     unknownBase.Check(
                         List.contains baseCap.Id known,
                         fun () -> at (sprintf "UnknownBaseFunction did not enumerate the known ids (%A)" known)
                     )
                 | other ->
                     unknownBase.Check(false, fun () -> at (sprintf "unknown base not default-denied: %A" other)))

                // ---- 4. the version is genuinely shape-derived ----
                let sg' =
                    { sg with
                        Holes = [ h0; h1; mkHole "h2" ] }

                shapeDerived.Check(
                    ContentPack.signatureFingerprint sg <> ContentPack.signatureFingerprint sg',
                    fun () -> at "a changed hole set did not shift the signature fingerprint"
                ))

        LawKit.results [ roundTrip; mismatch; unknownBase; shapeDerived ]

    // ---- aggregate null-skip (Phase 36; split by Phase 257) ----
    // The `Column.aggregate` half of what was `aggregateParityLaws`. The parity half compares the
    // aggregate against a single-group `GroupBy`, which is the dataframe layer's, so it ships from
    // `Fuaran.Core.DataFrame.Conformance` under the old name (D68), produced by the compute
    // repository since Phase 258 (D66). What stays here reads `Column`
    // alone: the pinned NA-skip semantics every consumer of the aggregate relies on.

    /// The aggregate null-skip laws (Phase 36's second law, a family of its own since Phase 257).
    /// Self-contained — over a seed-replayable sample of random (int/float/decimal, null-bearing) columns
    /// it certifies the pinned NA-skip semantics of `Column.aggregate`: `Count` equals the
    /// present-cell count, and `Sum` over a null-bearing column equals `Sum` over its present-only
    /// projection.
    let aggregateNullSkipLaws (seed: int) (iterations: int) : LawResult list =
        let nullSkip =
            LawKit.LawCell "Column.aggregate skips Null cells (Count = present count; Sum ignores nulls)"
        // Phase 276 — the present cells the laws were reached by, per numeric column type. A
        // column type the generator never draws is a `Sum` and a `Count` that were never tested
        // over it, and the exact decimal `Sum` is the one fold that shares no code with the others.
        let mutable intCells = 0
        let mutable floatCells = 0
        let mutable decimalCells = 0

        LawKit.run iterations seed (fun rng _ at ->
            // Phase 276 — the column type is drawn from all three numeric types; until then a
            // decimal column was never drawn, and the guard below says so for any run that misses one.
            let kind = rng.IntBelow 3
            let nRows = rng.IntBelow 6

            let ty =
                match kind with
                | 0 -> IntType
                | 1 -> FloatType
                | _ -> DecimalType

            let cells =
                [ for _ in 0..nRows ->
                      let k = rng.IntBelow 4

                      if k = 0 then
                          Null
                      else
                          let v = rng.IntBelow 200

                          match ty with
                          | IntType -> Int(v - 100)
                          | FloatType -> Float(float (v - 100) * 0.5)
                          | _ ->
                              // Two fraction digits, through the canonicalising constructor: `3.10`
                              // is held as `3.1`, `3.00` as `3`, as every boundary holds a decimal.
                              let frac = rng.IntBelow 100

                              let text = string (v - 100) + "." + (if frac < 10 then "0" else "") + string frac

                              Cell.decimal text |> Option.defaultValue Null ]

            let col = Column.create "c" ty cells
            let present = cells |> List.filter (fun c -> not (Cell.isNull c))
            let presentCol = Column.create "c" ty present

            for c in present do
                match c with
                | Int _ -> intCells <- intCells + 1
                | Float _ -> floatCells <- floatCells + 1
                | Decimal _ -> decimalCells <- decimalCells + 1
                | _ -> ()

            (match Column.aggregate Count col with
             | Ok(Int n) when n = List.length present -> nullSkip.Saw()
             | other -> nullSkip.Check(false, fun () -> at (sprintf "Count ≠ present count (%A)" other)))

            match Column.aggregate Sum col, Column.aggregate Sum presentCol with
            | Ok a, Ok b when a = b -> nullSkip.Saw()
            | a, b -> nullSkip.Check(false, fun () -> at (sprintf "Sum not null-skipping (%A vs %A)" a b)))

        LawKit.results [ nullSkip ]
        @ [ SampleAdequacy.reached
                "Conformance.aggregateNullSkipLaws"
                "column type"
                seed
                [ "int cell", intCells; "float cell", floatCells; "decimal cell", decimalCells ] ]

    // ---- columnar validator (Phase 37) ----
    // The teeth on the `ColumnValidator` surface: stock rules over a `Table` emit located, severity-
    // tagged defects through the EXISTING defect/severity model, and the output is deterministic +
    // byte-canonical for a given table (`canonicalCodes`).

    /// The columnar-validator laws (Phase 37). Self-contained — over a seed-replayable sample of random
    /// `(a:int|decimal, s:string)` tables with injected faults (nulls + out-of-range values) it certifies:
    /// **determinism** (`validate` and its `canonicalCodes` projection are identical on a re-run of the
    /// same table); and **soundness** (the count of `COL-NOTNULL` defects equals the number of null cells
    /// in the non-null column, and `COL-INRANGE` equals the number of out-of-range cells).
    let columnarValidatorLaws (seed: int) (iterations: int) : LawResult list =
        let determinism =
            LawKit.LawCell "columnar validate is deterministic + byte-canonical (same table ⇒ same defects)"

        let soundness =
            LawKit.LawCell "columnar stock rules are sound (defect counts = injected faults)"
        // Phase 223 — the fault populations the soundness law counts. A fault-free sample
        // satisfies `defects = injected faults` as 0 = 0 and certifies nothing about either rule.
        let mutable nullsInjected = 0
        let mutable outOfRangeInjected = 0
        // Phase 276 — the present cells of the ranged column, per type. `inRange` reads an `Int`
        // and a `Decimal` by different branches, and a sample of one type certifies one of them.
        let mutable intCells = 0
        let mutable decimalCells = 0

        let reg =
            ColumnValidator.empty
            |> ColumnValidator.register (ColumnValidator.notNull "a")
            |> ColumnValidator.register (ColumnValidator.inRange "a" 0.0 100.0)
            |> ColumnValidator.register (ColumnValidator.ofType "s" StringType)
            |> ColumnValidator.register (ColumnValidator.unique [ "a" ])

        LawKit.run iterations seed (fun rng i at ->
            let nRows = rng.IntBelow 6

            // column a: int straying out of [0,100], with ~1/5 nulls
            let drawn =
                [ for _ in 0..nRows ->
                      let k = rng.IntBelow 5

                      if k = 0 then
                          Null
                      else
                          let v = rng.IntBelow 160
                          Int(v - 30) ]

            // Phase 223 — the roll is STRATIFIED by iteration index, so every run of three or more
            // iterations reaches both faults the soundness law counts, by construction rather than
            // by the draw: stratum 0 is a clean table (the drawn values folded into range, nulls
            // dropped), stratum 1 carries the draw plus one null, stratum 2 the draw plus one
            // out-of-range value. A shorter run can still miss them, and the guard below says so.
            //
            // Phase 276 — and by PARITY, the column's type: an odd iteration carries `a` as a decimal
            // column, each drawn int `n` read as `n.25`, so two iterations reach both of `inRange`'s
            // branches with no extra draw (an even iteration's table is the one it always was). The
            // decimal stratum 0 folds into `0..99` so `n.25` stays in range, and stratum 2's injected
            // value is `100.01` — past the bound by less than any whole step.
            let decimalColumn = i % 2 = 1

            let cellOf (n: int) : Cell =
                if decimalColumn then
                    Cell.decimal (string n + ".25") |> Option.defaultValue Null
                else
                    Int n

            let typed =
                drawn
                |> List.map (fun c ->
                    match c with
                    | Int v -> cellOf v
                    | other -> other)

            let aCells =
                match i % 3 with
                | 0 ->
                    drawn
                    |> List.choose (fun c ->
                        match c with
                        | Int v when decimalColumn -> Some(cellOf (((v % 100) + 100) % 100))
                        | Int v -> Some(Int(((v % 101) + 101) % 101))
                        | _ -> None)
                | 1 -> typed @ [ Null ]
                | _ -> typed @ [ (if decimalColumn then Decimal "100.01" else Int 150) ]

            let sCells = aCells |> List.map (fun _ -> Str "x")
            let aType = if decimalColumn then DecimalType else IntType

            let t: Table =
                { Schema = [ "a", aType; "s", StringType ]
                  Columns = [ Column.create "a" aType aCells; Column.create "s" StringType sCells ] }

            let defects = ColumnValidator.validate reg t

            determinism.Check(
                ColumnValidator.validate reg t = defects
                && Validator.canonicalCodes (ColumnValidator.validate reg t) = Validator.canonicalCodes defects,
                fun () -> at "columnar validate is not deterministic"
            )

            let nullCount = aCells |> List.filter Cell.isNull |> List.length

            let notNullDefects =
                defects |> List.filter (fun d -> d.Code = "COL-NOTNULL") |> List.length

            let outOfRange =
                aCells
                |> List.filter (fun c ->
                    match c with
                    | Int v -> v < 0 || v > 100
                    // Counted EXACTLY, by the digits — the oracle the rule's float reading is held to.
                    | Decimal s -> DecimalText.compare s "0" = Some -1 || DecimalText.compare s "100" = Some 1
                    | _ -> false)
                |> List.length

            let inRangeDefects =
                defects |> List.filter (fun d -> d.Code = "COL-INRANGE") |> List.length

            nullsInjected <- nullsInjected + nullCount
            outOfRangeInjected <- outOfRangeInjected + outOfRange

            for c in aCells do
                match c with
                | Int _ -> intCells <- intCells + 1
                | Decimal _ -> decimalCells <- decimalCells + 1
                | _ -> ()

            soundness.Check(
                (notNullDefects = nullCount && inRangeDefects = outOfRange),
                fun () ->
                    at (
                        sprintf
                            "defect counts ≠ injected faults (notNull %d/%d, inRange %d/%d)"
                            notNullDefects
                            nullCount
                            inRangeDefects
                            outOfRange
                    )
            ))

        LawKit.results [ determinism; soundness ]
        @ [
            // Phase 223 — `Guarded ["null cell"; "out-of-range cell"]`, after the subject laws. The
            // stratified roll reaches both at three iterations; a shorter run reports the guard.
            SampleAdequacy.reached
                "Conformance.columnarValidatorLaws"
                "injected null"
                seed
                [ "null cell", nullsInjected ]
            SampleAdequacy.reached
                "Conformance.columnarValidatorLaws"
                "injected out-of-range value"
                seed
                [ "out-of-range cell", outOfRangeInjected ]
            SampleAdequacy.reached
                "Conformance.columnarValidatorLaws"
                "column type"
                seed
                [ "int cell", intCells; "decimal cell", decimalCells ] ]

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
            LawKit.LawCell "pipeline type-check accepts a well-typed DAG + names an ill-typed edge (EdgeTypeMismatch)"

        let roundtrip = LawKit.LawCell "a capability pipeline round-trips the wire"

        let replay =
            LawKit.LawCell "a pipeline node replays byte-identically via the Phase 27 capture seam"

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
            Registry.empty
            |> Registry.register (Capability.create "prod" prodSig (ClientIsland Pyodide))
            |> Result.bind (Registry.register (Capability.create "cons" consSig Server))

        match regResult with
        | Error e ->
            let built = LawKit.LawCell "capability pipeline registry built"
            built.Check(false, fun () -> sprintf "%A" e)
            LawKit.results [ built ]
        | Ok reg ->
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
                 | Ok(), Error(EdgeTypeMismatch _) -> typecheck.Saw()
                 | g, b -> typecheck.Check(false, fun () -> at (sprintf "type-check disagreed (good=%A bad=%A)" g b)))

                (match CapabilityPipeline.decode (CapabilityPipeline.encode good) with
                 | Ok p2 -> roundtrip.Check((p2 = good), fun () -> at "pipeline ≠ round-trip")
                 | Error m -> roundtrip.Check(false, fun () -> at (sprintf "pipeline decode failed: %s" m)))

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

            LawKit.results [ typecheck; roundtrip; replay ]

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
    ///    `PipelineUnknownArg`. Both are BUILT from the drawn pipeline, never drawn.
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
                if Option.isSome (Registry.tryFind s w.PipelineRegistry) then
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

            match CapabilityPipeline.typeCheck w.PipelineRegistry p with
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
                                w.PipelineRegistry
                                (replaceAt k (Invoke(nid, absentCapability, outT, args)))
                        with
                        | Error(PipelineNoSuchCapability(c, _)) when c = absentCapability -> deny.Saw()
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
                                w.PipelineRegistry
                                (replaceAt k (Invoke(nid, capId, outT, args @ [ strayArg, Literal "0" ])))
                        with
                        | Error(PipelineUnknownArg(m, a)) when m = nid && a = strayArg -> deny.Saw()
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

            match CapabilityPipeline.eval (bodyWith sv0 (ResizeArray())) pipeline with
            | Error e -> byteIdentical.Check(false, fun () -> at (sprintf "prior eval errored: %A" e))
            | Ok prior ->
                let fullInvoked = ResizeArray()
                let incrInvoked = ResizeArray()
                let viaFull = CapabilityPipeline.eval (bodyWith sv1 fullInvoked) pipeline

                let viaIncr =
                    CapabilityPipeline.evalFrom (bodyWith sv1 incrInvoked) prior changed pipeline

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
