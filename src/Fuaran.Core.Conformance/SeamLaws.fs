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
    ///
    /// Since Phase 316 it also certifies PAGING on the input side — a paged query reached through
    /// `Query.invokePage` and `QueryRegistry.dispatchPage` receives the token it was asked for, each
    /// page keys apart under `Query.invocationKeyPage` (over tokens carrying the page tag and the
    /// encoding's own symbols), the first page keys as `invocationKey`, and a journal captured page
    /// by page replays page n from page n's capture under strict replay — and that the registry
    /// refuses a declaration naming a parameter twice, by name.
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

        let relation =
            LawKit.LawCell
                "a parameter accepts a cell exactly when ColumnType.widens says it fills, which is Space.subsumes on the numeric types"

        let typedFault =
            LawKit.LawCell
                "a resolver's typed fault reaches the caller by name (SourceNotResolved, Timeout, ExecutionFailed with its recoverable)"

        let paging =
            LawKit.LawCell
                "a paged query receives its token through the seam, keys each page apart, and replays page n from page n's capture"

        let registration =
            LawKit.LawCell "the registry refuses a declaration naming a parameter twice (DuplicateParam), by name"

        // value-codec for the captured realized result (the QueryResult itself, rendered canonically).
        let encodeV (qr: QueryResult) : string = QueryCodec.encodeResult qr

        let decodeV (s: string) : Result<QueryResult, string> = QueryCodec.decodeResult s

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

            // ---- Phase 316: paging on the input side, visible to the replay key ----
            // The token alphabet carries the canonical encoding's own symbols and the page tag, so a
            // pre-image that only concatenated would collide here.
            let pages = 1 + rng.IntBelow 4

            let tokens =
                None
                :: List.init (pages - 1) (fun n -> Some(rng.Choose [ ""; "p"; "\u0001"; "\u0010"; "t" ] + string n))

            let pageOf (n: int) : QueryResult =
                { realized with
                    PageNum = n
                    NextPageToken = List.tryItem (n + 1) tokens |> Option.flatten }

            // The resolver reads the token it was handed and answers that page.
            let resolver (_: Query) (token: string option) : Deferred<QueryResult> =
                match List.tryFindIndex ((=) token) tokens with
                | Some n -> Ready(pageOf n)
                | None -> Failed "no such page"

            let pagedArgs = [ "p0", Int 42 ]

            let reg =
                QueryRegistry.register q QueryRegistry.empty
                |> Result.defaultValue QueryRegistry.empty

            let served =
                tokens
                |> List.map (fun t ->
                    match
                        Query.invokePage q pagedArgs t resolver,
                        QueryRegistry.dispatchPage reg q.Id pagedArgs t resolver
                    with
                    | Ok(Ready r), Ok(Ready r') when r = r' -> Some r
                    | _ -> None)

            let keys = tokens |> List.map (Query.invocationKeyPage q pagedArgs)

            // Capture every page under its own key, then replay strictly: each key yields its page,
            // and the journal is consumed exactly.
            let journal =
                List.zip keys served
                |> List.fold
                    (fun caps (k, r) ->
                        match r with
                        | Some page -> OpStream.captureEffect hashFn encodeV det k (fun () -> page) caps |> snd
                        | None -> caps)
                    []

            let replayed =
                keys
                |> List.fold
                    (fun (acc, caps) k ->
                        match OpStream.replayEffectStrict decodeV k det (fun () -> pageOf 99) caps with
                        | Ok(v, rest) -> acc @ [ Some v ], rest
                        | Error _ -> acc @ [ None ], caps)
                    ([], journal)

            let pageNums = served |> List.map (Option.map (fun r -> r.PageNum))

            paging.Check(
                pageNums = List.init pages Some
                && List.distinct keys = keys
                && Query.invocationKeyPage q pagedArgs None = Query.invocationKey q pagedArgs
                && fst replayed = served
                && List.isEmpty (snd replayed)
                && Query.invokePage q pagedArgs (Some "t") resolver = Error(ExecutionFailed("no such page", []))
                && QueryRegistry.dispatchPage reg "nope" pagedArgs None resolver = Error(NoSuchQuery("nope", [ q.Id ]))
                && Query.invokePage q [ "p0", Str "x" ] None resolver = Error(
                    ParamTypeMismatch("p0", IntType, StringType)
                ),
                fun () -> at (sprintf "paging over tokens %A served pages %A under keys %A" tokens pageNums keys)
            )

            // ---- Phase 316: registration refuses a parameter named twice ----
            let repeated = rng.Choose [ "p0"; "p1" ]

            let twice =
                { qOpt with
                    Id = "twice-" + string i
                    Params =
                        qOpt.Params
                        @ [ { Name = repeated
                              Type = BoolType
                              Required = false } ] }

            registration.Check(
                QueryRegistry.register twice QueryRegistry.empty = Error(DuplicateParam repeated)
                && QueryRegistry.register qOpt QueryRegistry.empty |> Result.isOk,
                fun () -> at (sprintf "a declaration naming %s twice was registered" repeated)
            )

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

            // ---- THE widening lattice at the parameter (Phase 295) ----
            let declared = rng.Choose ColumnType.all
            let sent = rng.Choose ColumnType.all

            let cellOf (t: ColumnType) : Cell =
                match t with
                | IntType -> Int 1
                | FloatType -> Float 1.5
                | BoolType -> Bool true
                | StringType -> Str "s"
                | DateType -> Date "2026-10-02"
                | TimestampType -> Timestamp "2026-10-02T00:00:00Z"
                | DecimalType -> Decimal "1.5"

            // The numeric types are where both seams carry a number: there the widening IS the space
            // relation. A query cell is typed, so a `string` parameter takes no `int` cell, where a
            // capability's `AnyString` hole admits the string "5" — the two seams agree on numbers.
            let spaceOf (t: ColumnType) : ValueSpace option =
                match t with
                | IntType -> Some(IntRange(System.Int32.MinValue, System.Int32.MaxValue))
                | FloatType -> Some(FloatRange(System.Double.MinValue, System.Double.MaxValue))
                | _ -> None

            let qT =
                { q with
                    Params =
                        [ { Name = "p"
                            Type = declared
                            Required = true } ] }

            let accepted = Query.validateParams qT [ "p", cellOf sent ] = Ok()
            let widens = ColumnType.widens sent declared

            relation.Check(
                (accepted = widens
                 && (match spaceOf declared, spaceOf sent with
                     | Some d, Some g -> Space.subsumes d g = widens
                     | _ -> true)),
                fun () ->
                    at (
                        sprintf
                            "%s into %s: accepted=%b widens=%b subsumes=%A"
                            (ColumnType.tag sent)
                            (ColumnType.tag declared)
                            accepted
                            widens
                            (Option.map2 Space.subsumes (spaceOf declared) (spaceOf sent))
                    )
            )

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

                // a resolver's typed fault names its refusal (Phase 295).
                (match
                    QueryRegistry.dispatchWithArgs reg q.Id goodArgs (fun _ _ ->
                        Error(ResolveFault.SourceMissing("src-" + string i))),
                    QueryRegistry.dispatchWithArgs reg q.Id goodArgs (fun _ _ -> Error ResolveFault.TimedOut),
                    QueryRegistry.dispatchWithArgs reg q.Id goodArgs (fun _ _ ->
                        Error(ResolveFault.Failed("busy", [ "p0" ])))
                 with
                 | Error(SourceNotResolved r), Error Timeout, Error(ExecutionFailed("busy", [ "p0" ])) when
                     r = "src-" + string i
                     ->
                     typedFault.Saw()
                 | a, b, c -> typedFault.Check(false, fun () -> at (sprintf "typed faults: %A / %A / %A" a b c)))

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
              typedFailure
              relation
              typedFault
              paging
              registration ]

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

    // ---- THE space relation (Phase 295) ----
    // One relation, `Space.subsumes`, answers "does this space admit every value of that one" at three
    // call sites — the pipeline's edge check, the function registry's hole match, and (through
    // `ColumnType.widens`) the query seam's parameter check. Each site's family carries a cell that pins
    // the site to the relation over drawn spaces; `capabilityPipelineLaws` also certifies the relation
    // SOUND against `Space.validate`.

    /// A small value space, drawn so that related and unrelated pairs both occur: tight bounds, a
    /// shared enum alphabet, both tree constraints.
    let internal drawSpace (rng: LawKit.Draws) : ValueSpace =
        let lo = rng.IntBelow 6
        let hi = lo + rng.IntBelow 6

        match rng.IntBelow 7 with
        | 0 -> IntRange(lo, hi)
        | 1 -> FloatRange(float lo, float hi + 0.5)
        | 2 -> StringLen(lo, hi)
        | 3 ->
            Enum(
                List.init (1 + rng.IntBelow 3) (fun _ -> rng.Choose [ "a"; "bb"; "ccc" ])
                |> List.distinct
            )
        | 4 -> AnyString
        | 5 -> SlotTree None
        | _ -> SlotTree(Some(rng.Choose [ "para"; "table" ]))

    /// Candidate values for a space: some inside it, some at and past its edges, so the soundness
    /// check meets both answers.
    let internal candidates (sp: ValueSpace) : string list =
        match sp with
        | IntRange(lo, hi) -> [ string lo; string hi; string (hi + 1); "0"; "11" ]
        | FloatRange(lo, hi) -> [ Canon.canonicalFloat lo; Canon.canonicalFloat hi; "0"; "2.5"; "12" ]
        | StringLen(lo, hi) ->
            [ String.replicate lo "x"
              String.replicate hi "y"
              String.replicate (hi + 1) "z" ]
        | Enum xs -> xs @ [ "a"; "zz" ]
        | AnyString -> [ ""; "5"; "a" ]
        | SlotTree _ -> [ "{\"kind\":\"para\"}"; "{\"kind\":\"table\"}"; "5" ]


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
    ///
    /// Since Phase 316 it also certifies the LIFECYCLE of all four registries — `CapabilityRegistry`,
    /// `FunctionRegistry`, `QueryRegistry` and `Validator.RuleRegistry` — over registries drawn from
    /// a five-id pool, so collisions arise:
    ///
    ///  - **unregister undoes register** on a fresh id: `unregister id (register x r) = Ok r`;
    ///  - **an id not held is refused** by the seam's own unknown-id error (`NoSuchCapability`,
    ///    `NoSuchQuery`, `UnknownRule`) naming the held ids, by `unregister` and `replace` alike;
    ///  - **replace swaps exactly the entry under its id**, and refuses what `register` would;
    ///  - **restrict narrows**: `enumerate (restrict s r)` is exactly the entries of `r` whose id is in
    ///    `s`, so a subset of `enumerate r`;
    ///  - **union is associative and refuses a collision**: both associations of three registries are
    ///    refused together or agree, two registries join exactly when their ids are disjoint, and a
    ///    refusal names a shared id by the seam's duplicate error;
    ///  - **the function registry's index stays consistent**: after a random sequence of lifecycle
    ///    edits, `findBySignature` by result type returns exactly the enumerated entries of that type
    ///    the query matches — no phantom, no miss.
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

        let relation =
            LawKit.LawCell
                "findBySignature fills a hole exactly when the space relation says the context's space fits (Space.subsumes)"

        LawKit.run iterations seed (fun rng i at ->
            // ---- 0. the hole match IS the space relation (Phase 295) ----
            let required = drawSpace rng
            let available = drawSpace rng

            let holeOf (sp: ValueSpace) : SigEntry =
                { Addr = "h"
                  Name = "h"
                  Kind = "value"
                  Space = Some sp
                  Slot = None
                  Action = None
                  Required = true }

            (match
                FunctionRegistry.empty
                |> FunctionRegistry.register (
                    FunctionRegistry.entry
                        "doc"
                        (Capability.create
                            "rel"
                            { Name = "rel"
                              Holes = [ holeOf required ]
                              Effect = Effect.pureDeterministic }
                            BuildTime)
                )
             with
             | Error e -> relation.Check(false, fun () -> at (sprintf "register failed: %A" e))
             | Ok r ->
                 let found =
                     FunctionRegistry.findBySignature
                         Subsumes
                         { ResultType = Some "doc"
                           Available = [ holeOf available ] }
                         r
                     |> List.isEmpty
                     |> not

                 relation.Check(
                     (found = Space.subsumes required available),
                     fun () ->
                         at (
                             sprintf
                                 "the registry's hole match (%b) is not Space.subsumes (%A ⊇ %A)"
                                 found
                                 required
                                 available
                         )
                 ))

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
                (match
                    FunctionRegistry.partiallyApply ("pack-" + string i) (Set.ofList [ "h0" ]) ent
                    |> Result.bind (fun pack -> FunctionRegistry.register pack r |> Result.map (fun r2 -> pack, r2))
                 with
                 | Error e ->
                     narrowing.Check(false, fun () -> at (sprintf "registering the content pack failed: %A" e))
                 | Ok(pack, r2) ->
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

        // ---- Phase 316: the lifecycle — a registry is a lattice, not an append log ----
        let inverse =
            LawKit.LawCell "unregister undoes register on a fresh id, on all four registries"

        let unknown =
            LawKit.LawCell
                "unregister and replace of an id not held are refused by the seam's unknown-id error, naming the held ids"

        let replaces =
            LawKit.LawCell "replace swaps exactly the entry under its id, and refuses what register refuses"

        let restricts =
            LawKit.LawCell "restrict s r enumerates exactly the entries of r whose id is in s, a subset of enumerate r"

        let unions =
            LawKit.LawCell
                "union is associative, joins exactly the registries whose ids are disjoint, and refuses a shared id by the seam's duplicate error"

        let index =
            LawKit.LawCell
                "after any lifecycle edit, findBySignature by result type is exactly the enumerated entries of that type the query matches"

        let pool = [ "a"; "b"; "c"; "d"; "e" ]
        let kinds = [ "doc"; "sheet" ]

        let capOf (id: string) (hi: int) : Capability =
            Capability.create
                id
                { Name = id
                  Holes =
                    [ { Addr = "h"
                        Name = "h"
                        Kind = "value"
                        Space = Some(IntRange(0, hi))
                        Slot = None
                        Action = None
                        Required = true } ]
                  Effect = Effect.pureDeterministic }
                BuildTime

        let queryOf (id: string) (param: string) : Query =
            { Id = id
              Params =
                [ { Name = param
                    Type = IntType
                    Required = true } ]
              ResultSchema = [ "n", IntType ]
              Effect = Effect.pureDeterministic
              Source = Ref id
              TimeoutMs = None
              PageSize = None }

        let familyOf (id: string) : RuleFamily<unit, string> = { Id = id; Run = fun _ _ -> [] }

        let build (add: 'x -> 'r -> Result<'r, 'e>) (empty: 'r) (xs: 'x list) : Result<'r, 'e> =
            xs |> List.fold (fun acc x -> acc |> Result.bind (add x)) (Ok empty)

        let capIds (r: CapabilityRegistry) =
            CapabilityRegistry.enumerate r |> List.map (fun c -> c.Id)

        let qIds (r: QueryRegistry) =
            QueryRegistry.enumerate r |> List.map (fun q -> q.Id)

        // Both associations of a union agree: refused together, or equal under `same`.
        let associates (same: 'r -> 'r -> bool) (left: Result<'r, 'e>) (right: Result<'r, 'e>) : bool =
            match left, right with
            | Ok l, Ok r -> same l r
            | Error _, Error _ -> true
            | _ -> false

        LawKit.run iterations (seed + 316) (fun rng i at ->
            let subset () =
                pool |> List.filter (fun _ -> rng.IntBelow 2 = 0)

            let held = subset ()

            let fresh =
                pool
                |> List.tryFind (fun id -> not (List.contains id held))
                |> Option.defaultValue "z"

            let kindOf (id: string) = kinds.[(int id.[0] + i) % 2]

            match
                build CapabilityRegistry.register CapabilityRegistry.empty (held |> List.map (fun id -> capOf id 9)),
                build
                    FunctionRegistry.register
                    FunctionRegistry.empty
                    (held |> List.map (fun id -> FunctionRegistry.entry (kindOf id) (capOf id 9))),
                build QueryRegistry.register QueryRegistry.empty (held |> List.map (fun id -> queryOf id "p")),
                build Validator.register Validator.empty (held |> List.map familyOf)
            with
            | Ok cr, Ok fr, Ok qr, Ok vr ->
                // ---- unregister undoes register on a fresh id ----
                let capBack =
                    CapabilityRegistry.register (capOf fresh 9) cr
                    |> Result.bind (CapabilityRegistry.unregister fresh)

                let fnBack =
                    FunctionRegistry.register (FunctionRegistry.entry "doc" (capOf fresh 9)) fr
                    |> Result.bind (FunctionRegistry.unregister fresh)

                let qBack =
                    QueryRegistry.register (queryOf fresh "p") qr
                    |> Result.bind (QueryRegistry.unregister fresh)

                let vBack =
                    Validator.register (familyOf fresh) vr
                    |> Result.bind (Validator.unregister fresh)

                inverse.Check(
                    capBack = Ok cr
                    && fnBack = Ok fr
                    && qBack = Ok qr
                    && (vBack |> Result.map Validator.enumerate) = Ok(Validator.enumerate vr),
                    fun () -> at (sprintf "unregister did not undo register of %s over %A" fresh held)
                )

                // ---- an id not held is refused, naming the held ids ----
                let sorted = List.sort held

                let refusedRight =
                    CapabilityRegistry.unregister fresh cr = Error(NoSuchCapability(fresh, sorted))
                    && CapabilityRegistry.replace (capOf fresh 9) cr = Error(NoSuchCapability(fresh, sorted))
                    && FunctionRegistry.unregister fresh fr = Error(NoSuchCapability(fresh, sorted))
                    && FunctionRegistry.replace (FunctionRegistry.entry "doc" (capOf fresh 9)) fr = Error(
                        NoSuchCapability(fresh, sorted)
                    )
                    && QueryRegistry.unregister fresh qr = Error(NoSuchQuery(fresh, sorted))
                    && QueryRegistry.replace (queryOf fresh "p") qr = Error(NoSuchQuery(fresh, sorted))
                    && (match Validator.unregister fresh vr, Validator.replace (familyOf fresh) vr with
                        | Error(RegistrationError.UnknownRule(a, ka)), Error(RegistrationError.UnknownRule(b, kb)) ->
                            a = fresh && b = fresh && ka = held && kb = held
                        | _ -> false)

                unknown.Check(
                    refusedRight,
                    fun () -> at (sprintf "an unheld id %s was not refused by name over %A" fresh held)
                )

                // ---- replace swaps exactly the entry under its id ----
                (match held with
                 | [] -> ()
                 | _ ->
                     let k = rng.Choose held
                     let others (ids: string list) = ids |> List.filter (fun id -> id <> k)
                     let swapped = capOf k 3
                     let swappedEntry = FunctionRegistry.entry "sheet" swapped
                     let swappedQuery = queryOf k "p2"

                     let capOk =
                         match CapabilityRegistry.replace swapped cr with
                         | Ok r2 ->
                             CapabilityRegistry.tryFind k r2 = Some swapped
                             && capIds r2 = capIds cr
                             && others (capIds r2)
                                |> List.forall (fun id ->
                                    CapabilityRegistry.tryFind id r2 = CapabilityRegistry.tryFind id cr)
                         | Error _ -> false

                     let fnOk =
                         match FunctionRegistry.replace swappedEntry fr with
                         | Ok r2 ->
                             FunctionRegistry.tryFind k r2 = Some swappedEntry
                             && FunctionRegistry.ids r2 = FunctionRegistry.ids fr
                         | Error _ -> false

                     let qOk =
                         match QueryRegistry.replace swappedQuery qr with
                         | Ok r2 -> QueryRegistry.tryFind k r2 = Some swappedQuery && qIds r2 = qIds qr
                         | Error _ -> false

                     let vOk =
                         match Validator.replace (familyOf k) vr with
                         | Ok r2 -> Validator.enumerate r2 = Validator.enumerate vr
                         | Error _ -> false

                     // What register refuses, replace refuses: an ill-formed space, a repeated parameter.
                     let gateOk =
                         (match CapabilityRegistry.replace (capOf k (-1)) cr with
                          | Error(IllFormedCapability(id, _)) -> id = k
                          | _ -> false)
                         && (match
                                 QueryRegistry.replace
                                     { swappedQuery with
                                         Params = swappedQuery.Params @ swappedQuery.Params }
                                     qr
                             with
                             | Error(DuplicateParam "p2") -> true
                             | _ -> false)

                     replaces.Check(
                         capOk && fnOk && qOk && vOk && gateOk,
                         fun () ->
                             at (
                                 sprintf
                                     "replace of %s misbehaved (cap=%b fn=%b q=%b v=%b gate=%b)"
                                     k
                                     capOk
                                     fnOk
                                     qOk
                                     vOk
                                     gateOk
                             )
                     ))

                // ---- restrict narrows ----
                let keep = Set.ofList (subset () @ [ "zz" ])
                let within (ids: string list) = ids |> List.filter keep.Contains

                restricts.Check(
                    capIds (CapabilityRegistry.restrict keep cr) = within (capIds cr)
                    && FunctionRegistry.ids (FunctionRegistry.restrict keep fr) = within (FunctionRegistry.ids fr)
                    && qIds (QueryRegistry.restrict keep qr) = within (qIds qr)
                    && Validator.enumerate (Validator.restrict keep vr) = within (Validator.enumerate vr),
                    fun () -> at (sprintf "restrict %A over %A did not narrow exactly" keep held)
                )

                // ---- union: associative, disjoint-exactly, refused by name ----
                let a, b, c = subset (), subset (), subset ()

                let caps ids =
                    build CapabilityRegistry.register CapabilityRegistry.empty (ids |> List.map (fun id -> capOf id 9))

                let fns ids =
                    build
                        FunctionRegistry.register
                        FunctionRegistry.empty
                        (ids |> List.map (fun id -> FunctionRegistry.entry (kindOf id) (capOf id 9)))

                let qs ids =
                    build QueryRegistry.register QueryRegistry.empty (ids |> List.map (fun id -> queryOf id "p"))

                let vs ids =
                    build Validator.register Validator.empty (ids |> List.map familyOf)

                match caps a, caps b, caps c, fns a, fns b, fns c, qs a, qs b, qs c, vs a, vs b, vs c with
                | Ok ca, Ok cb, Ok cc, Ok fa, Ok fb, Ok fc, Ok qa, Ok qb, Ok qc, Ok va, Ok vb, Ok vc ->
                    let shared = Set.intersect (Set.ofList a) (Set.ofList b)

                    let assoc =
                        associates
                            (=)
                            (CapabilityRegistry.union ca cb
                             |> Result.bind (fun ab -> CapabilityRegistry.union ab cc))
                            (CapabilityRegistry.union cb cc |> Result.bind (CapabilityRegistry.union ca))
                        && associates
                            (=)
                            (FunctionRegistry.union fa fb
                             |> Result.bind (fun ab -> FunctionRegistry.union ab fc))
                            (FunctionRegistry.union fb fc |> Result.bind (FunctionRegistry.union fa))
                        && associates
                            (=)
                            (QueryRegistry.union qa qb |> Result.bind (fun ab -> QueryRegistry.union ab qc))
                            (QueryRegistry.union qb qc |> Result.bind (QueryRegistry.union qa))
                        && associates
                            (fun l r -> Validator.enumerate l = Validator.enumerate r)
                            (Validator.union va vb |> Result.bind (fun ab -> Validator.union ab vc))
                            (Validator.union vb vc |> Result.bind (Validator.union va))

                    let joins =
                        match CapabilityRegistry.union ca cb, QueryRegistry.union qa qb, Validator.union va vb with
                        | Ok cab, Ok qab, Ok vab ->
                            Set.isEmpty shared
                            && capIds cab = List.sort (a @ b)
                            && qIds qab = List.sort (a @ b)
                            && Validator.enumerate vab = a @ b
                        | Error(DuplicateCapability ci),
                          Error(DuplicateQuery qi),
                          Error(RegistrationError.DuplicateRule(vi, _)) ->
                            ci = Set.minElement shared && qi = ci && shared.Contains vi
                        | _ -> false

                    unions.Check(
                        assoc && joins,
                        fun () -> at (sprintf "union over %A / %A / %A (assoc=%b joins=%b)" a b c assoc joins)
                    )

                    // ---- the function registry's index after a sequence of edits ----
                    let step (r: FunctionRegistry) =
                        match rng.IntBelow 4 with
                        | 0 -> FunctionRegistry.unregister (rng.Choose pool) r |> Result.defaultValue r
                        | 1 ->
                            let k = rng.Choose pool

                            FunctionRegistry.replace (FunctionRegistry.entry (rng.Choose kinds) (capOf k 9)) r
                            |> Result.defaultValue r
                        | 2 -> FunctionRegistry.restrict (Set.ofList (subset ())) r
                        | _ -> FunctionRegistry.union r fc |> Result.defaultValue r

                    let edited =
                        List.init 4 id
                        |> List.fold (fun r _ -> step r) (FunctionRegistry.union fa fb |> Result.defaultValue fa)

                    let probe = [ (capOf "probe" 0).Signature.Holes.Head ]

                    for kind in kinds do
                        let found =
                            FunctionRegistry.findBySignature
                                Subsumes
                                { ResultType = Some kind
                                  Available = probe }
                                edited

                        let scanned =
                            FunctionRegistry.findBySignature Subsumes { ResultType = None; Available = probe } edited
                            |> List.filter (fun e -> e.ResultType = kind)

                        let enumerated =
                            FunctionRegistry.enumerate edited |> List.filter (fun e -> e.ResultType = kind)

                        index.Check(
                            found = scanned && found = enumerated,
                            fun () ->
                                at (
                                    sprintf
                                        "findBySignature %s found %A; the enumeration holds %A"
                                        kind
                                        (found |> List.map (fun e -> e.Capability.Id))
                                        (enumerated |> List.map (fun e -> e.Capability.Id))
                                )
                        )
                | _ -> unions.Check(false, fun () -> at "a drawn registry did not build")
            | _ -> inverse.Check(false, fun () -> at (sprintf "the drawn registries over %A did not build" held)))

        LawKit.results
            [ findable
              nonMatch
              narrowing
              defaultDeny
              relation
              inverse
              unknown
              replaces
              restricts
              unions
              index ]

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
    ///  - **unload undoes load** (Phase 316) — `ContentPack.unload m` over the registry `load m reg`
    ///    returned gives back `reg`, and a second unload is refused `PackNotLoaded` by name.
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

        let unloads =
            LawKit.LawCell
                "unload undoes load (the registry the pack loaded into comes back), and unloading a pack not loaded is refused PackNotLoaded"

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
                     )

                     // Phase 316: unload is load's inverse, and a second unload is refused by name.
                     match ContentPack.unload manifest loaded with
                     | Ok back when back = reg ->
                         match ContentPack.unload manifest back with
                         | Error(PackNotLoaded(_, newId, known)) ->
                             unloads.Check(
                                 newId = pf.NewId && known = FunctionRegistry.ids reg,
                                 fun () -> at (sprintf "the second unload named %s / %A" newId known)
                             )
                         | other ->
                             unloads.Check(false, fun () -> at (sprintf "a second unload was not refused: %A" other))
                     | other ->
                         unloads.Check(false, fun () -> at (sprintf "unload did not give back the registry: %A" other)))

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

        LawKit.results [ roundTrip; mismatch; unknownBase; shapeDerived; unloads ]

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
            // four distinct rules: `ofRules` refuses only a repeated id (Phase 298), so this is `Ok`
            match
                ColumnValidator.ofRules
                    [ ColumnValidator.notNull "a"
                      ColumnValidator.inRange "a" 0.0 100.0
                      ColumnValidator.ofType "s" StringType
                      ColumnValidator.unique [ "a" ] ]
            with
            | Ok r -> r
            | Error e -> invalidOp (sprintf "the kit's column registry repeats a rule id: %A" e)

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

        let relation =
            LawKit.LawCell
                "an edge type-checks exactly when the space relation says the producer's output fits the argument (Space.subsumes)"

        let sound =
            LawKit.LawCell "the space relation is sound: a value of the sub-space validates in the super-space"

        let ordered =
            LawKit.LawCell
                "a self-edge and a cycle are refused PipelineCycle and a forward edge PipelineForwardEdge, by name"

        let checkedFirst =
            LawKit.LawCell "an ill-typed pipeline is refused EvalIllTyped before any body runs"

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
                 | Ok(), Error(EdgeTypeMismatch _) -> typecheck.Saw()
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
                 | Error(EdgeTypeMismatch _) when not (Space.subsumes argSpace output) -> relation.Saw()
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
                 | Error(PipelineCycle("n1", [ "n1" ])),
                   Error(PipelineCycle("n1", [ "n1"; "n2" ])),
                   Error(PipelineForwardEdge("n2", "x", "n1")) -> ordered.Saw()
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
                 | Error(EvalIllTyped(EdgeTypeMismatch _)) when not ran.Value -> checkedFirst.Saw()
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
                                lookup
                                (replaceAt k (Invoke(nid, capId, outT, args @ [ strayArg, Literal "0" ])))
                        with
                        | Error(PipelineArgRefused(m, UnknownArg(a, _))) when m = nid && a = strayArg -> deny.Saw()
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
              Known = [ "inc" ] }

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
