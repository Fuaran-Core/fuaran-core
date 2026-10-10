namespace Fuaran.Core

/// The query seam family (Phase 198; `SeamLaws` until the Phase 388 split along its banners).
module internal QuerySeamLaws =

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
    ///
    /// Since Phase 385 it also certifies that the TYPED resolver reaches the capture and replay
    /// dispatchers: a `ResolveFault` answered through `QueryRegistry.dispatchPageCapturedWith` /
    /// `dispatchCapturedWith` replays through `dispatchReplayedWith` as exactly the refusal
    /// `dispatchPageWith` answers live, page by page. Every iteration builds a refusal on its first
    /// page, and the law is counted per refusal page.
    ///
    /// Since Phase 398 it also certifies the declared FILTER and ORDER: a declaration carrying a
    /// `Where` predicate (each of the eight kinds in turn) and an `OrderBy` key registers, hands the
    /// resolver exactly what it declared, keys its capture apart from the same declaration without
    /// either, and round-trips the codec; a predicate naming an undeclared column is refused by
    /// `register` and by the declaration reader with one error; a declaration with neither member
    /// writes neither; and a resolver that cannot honour the predicate or the order is refused
    /// `PredicateNotHonoured` / `OrderNotHonoured` naming it, live and on replay. Counted once per
    /// iteration.
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

        let typedReach =
            LawKit.LawCell
                "a typed resolver's fault is captured, paged and replayed as the refusal it was answered live"

        let shape =
            LawKit.LawCell
                "a declared Where / OrderBy reaches the resolver, keys its capture apart, round-trips the codec and is admitted by one gate; a filter or order the resolver cannot honour is refused by name"

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
                  PageSize = None
                  Where = []
                  OrderBy = [] }

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

            let requiredNames = qOpt.Params |> List.filter _.Required |> List.map _.Name

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
                      Columns = [ Column.ofInts "n" (Vector.init nRows id) (Validity.all nRows) ] }
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

            let pageNums = served |> List.map (Option.map _.PageNum)

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
                 let ids = QueryRegistry.enumerate r |> List.map _.Id
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

                // ---- Phase 385: the typed resolver reaches capture, paging and replay ----
                // One answer per page token: the first page a typed fault (each of the three
                // `ResolveFault` cases in turn, so every iteration BUILDS a refusal), the rest drawn
                // between a result and the three faults. Captured page by page through
                // `dispatchPageCapturedWith` and replayed through `dispatchReplayedWith`, each page's
                // answer must be exactly what `dispatchPageWith` answers live — a refusal replays as
                // the refusal, never as a re-worded `ExecutionFailed` — and the unpaged
                // `dispatchCapturedWith` must capture the first page as its paged twin does.
                let faultAt (k: int) : Result<Deferred<QueryResult>, ResolveFault> =
                    match k with
                    | 0 -> Error(ResolveFault.SourceMissing("src-" + string i))
                    | 1 -> Error ResolveFault.TimedOut
                    | _ -> Error(ResolveFault.Failed("busy-" + string i, [ "p0" ]))

                let answers =
                    tokens
                    |> List.mapi (fun n _ ->
                        if n = 0 then
                            faultAt (i % 3)
                        else
                            match rng.IntBelow 4 with
                            | 3 -> Ok(Ready(pageOf n))
                            | k -> faultAt k)

                let typedResolver (_: Query) (token: string option) (_: (string * Cell) list) =
                    match List.tryFindIndex ((=) token) tokens with
                    | Some n -> List.item n answers
                    | None -> Ok(Failed "no such page")

                let live =
                    tokens
                    |> List.map (fun t -> QueryRegistry.dispatchPageWith reg q.Id goodArgs t typedResolver)

                let capturedTyped, typedJournal =
                    tokens
                    |> List.fold
                        (fun (acc, j) t ->
                            let captured =
                                QueryRegistry.dispatchPageCapturedWith
                                    hashFn
                                    encodeV
                                    reg
                                    q.Id
                                    goodArgs
                                    t
                                    typedResolver
                                    j

                            acc @ [ captured.Outcome ], captured.Journal)
                        ([], [])

                let replayFrom (journal: KeyedCapture list) (t: string option) (cursor: Map<string, int>) =
                    QueryRegistry.dispatchReplayedWith
                        decodeV
                        reg
                        q.Id
                        goodArgs
                        t
                        (fun _ _ _ -> Ok(Ready(pageOf 99)))
                        cursor
                        journal

                let replayedTyped =
                    tokens
                    |> List.fold
                        (fun (acc, cursor) t ->
                            let replayed = replayFrom typedJournal t cursor

                            match replayed.Outcome with
                            | Error(ReplayFailure.Unanswered _) -> acc @ [ None ], cursor
                            | answer -> acc @ [ Some answer ], replayed.Cursor)
                        ([], Map.empty)
                    |> fst

                let unpagedCapture =
                    QueryRegistry.dispatchCapturedWith
                        hashFn
                        encodeV
                        reg
                        q.Id
                        goodArgs
                        (fun q' a -> typedResolver q' None a)
                        []

                let unpaged = unpagedCapture.Outcome

                let unpagedReplay = (replayFrom unpagedCapture.Journal None Map.empty).Outcome

                // Counted per refusal page, where the refusal is built.
                List.zip3 live capturedTyped replayedTyped
                |> List.iteri (fun n (l, c, rp) ->
                    match l with
                    | Error _ ->
                        typedReach.Check(
                            c = l
                            && rp = Some(Result.mapError ReplayFailure.Refused l)
                            && (n > 0
                                || (unpaged = l && unpagedReplay = Result.mapError ReplayFailure.Refused l)),
                            fun () ->
                                at (
                                    sprintf
                                        "page %d (token %A): live %A, captured %A, replayed %A; unpaged %A / %A"
                                        n
                                        (List.item n tokens)
                                        l
                                        c
                                        rp
                                        unpaged
                                        unpagedReplay
                                )
                        )
                    | Ok _ -> ())

                // a resolver's untyped failure never rides out of the seam — `Ok(Failed _)` is unreachable.
                (match QueryRegistry.dispatch reg q.Id goodArgs (fun _ -> Failed("boom-" + string i)) with
                 | Error(ExecutionFailed(m, _)) when m = "boom-" + string i -> typedFailure.Saw()
                 | other ->
                     typedFailure.Check(
                         false,
                         fun () -> at (sprintf "a resolver failure did not become ExecutionFailed: %A" other)
                     ))

                // ---- Phase 398: the declared filter and order ----
                // Drawn after every earlier draw, so the laws above keep the samples they had. One
                // predicate of each kind in turn over a two-column result, one drawn order key.
                let shapeSchema = [ "n", IntType; "s", StringType ]
                let lit = rng.IntBelow 100

                let predicate =
                    match i % 8 with
                    | 0 -> ColumnPredicate.EqualTo("n", Int lit)
                    | 1 -> ColumnPredicate.GreaterThan("n", Int lit)
                    | 2 -> ColumnPredicate.AtLeast("n", Int lit)
                    | 3 -> ColumnPredicate.LessThan("n", Int lit)
                    | 4 -> ColumnPredicate.AtMost("n", Int lit)
                    | 5 -> ColumnPredicate.Contains("s", "x" + string lit)
                    | 6 -> ColumnPredicate.IsNull "s"
                    | _ -> ColumnPredicate.IsNotNull "n"

                let order =
                    [ { Column = rng.Choose [ "n"; "s" ]
                        Direction = rng.Choose [ SortDirection.Ascending; SortDirection.Descending ] } ]

                let orderColumn = (List.head order).Column

                let qS =
                    { q with
                        Id = "shaped-" + string i
                        ResultSchema = shapeSchema
                        Where = [ predicate ]
                        OrderBy = order }

                // One gate: a predicate naming an undeclared column is refused by the registry and
                // by the declaration reader with the same error, the reader at the predicate's path.
                let stray =
                    { qS with
                        Where = [ ColumnPredicate.IsNull "absent" ] }

                let strayError = UnknownColumn("absent", [ "n"; "s" ])

                let admitted =
                    QueryRegistry.register stray QueryRegistry.empty = Error strayError
                    && (match QueryCodec.decodeDetailedWith ReadPolicy.Lenient (QueryCodec.encode stray) with
                        | Error e ->
                            e.Path = [ PathSegment.Key "where"; PathSegment.Index 0 ]
                            && e.Message = QueryError.describe strayError
                        | Ok _ -> false)

                // Absent is absent: a declaration with neither member writes neither.
                let bare = QueryCodec.encode { qS with Where = []; OrderBy = [] }

                let absentOmitted =
                    not (bare.Contains "\"where\"") && not (bare.Contains "\"orderBy\"")

                let keysApart =
                    let k = Query.invocationKey qS goodArgs

                    k <> Query.invocationKey { qS with Where = [] } goodArgs
                    && k <> Query.invocationKey { qS with OrderBy = [] } goodArgs
                    && Query.invocationKeyPage qS goodArgs None = k

                match QueryRegistry.register qS QueryRegistry.empty with
                | Error e -> shape.Fail(at (sprintf "a well-formed filtered declaration was refused: %A" e))
                | Ok regS ->
                    let seen = ref None

                    let served =
                        QueryRegistry.dispatchWithArgs regS qS.Id goodArgs (fun q' _ ->
                            seen.Value <- Some(q'.Where, q'.OrderBy)
                            Ok(Ready realized))

                    let unhonoured (_: Query) (_: (string * Cell) list) =
                        Error(ResolveFault.PredicateUnsupported predicate)

                    let refusedP = QueryRegistry.dispatchWithArgs regS qS.Id goodArgs unhonoured

                    let refusedO =
                        QueryRegistry.dispatchWithArgs regS qS.Id goodArgs (fun _ _ ->
                            Error(ResolveFault.OrderUnsupported orderColumn))

                    let captureP =
                        QueryRegistry.dispatchCapturedWith hashFn encodeV regS qS.Id goodArgs unhonoured []

                    let capturedP = captureP.Outcome

                    let replayedP =
                        QueryRegistry.dispatchReplayedWith
                            decodeV
                            regS
                            qS.Id
                            goodArgs
                            None
                            (fun _ _ _ -> Ok(Ready realized))
                            Map.empty
                            captureP.Journal

                    let replayedP = replayedP.Outcome

                    shape.Check(
                        served = Ok(Ready realized)
                        && seen.Value = Some([ predicate ], order)
                        && refusedP = Error(PredicateNotHonoured predicate)
                        && refusedO = Error(OrderNotHonoured orderColumn)
                        && capturedP = refusedP
                        && replayedP = Result.mapError ReplayFailure.Refused refusedP
                        && QueryCodec.decode (QueryCodec.encode qS) = Ok qS
                        && keysApart
                        && admitted
                        && absentOmitted,
                        fun () ->
                            at (
                                sprintf
                                    "filter %A order %A: served %A seen %A refused %A / %A captured %A replayed %A keysApart %b admitted %b absentOmitted %b"
                                    predicate
                                    order
                                    served
                                    seen.Value
                                    refusedP
                                    refusedO
                                    capturedP
                                    replayedP
                                    keysApart
                                    admitted
                                    absentOmitted
                            )
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
              registration
              typedReach
              shape ]

    /// The query-seam laws at a DOMAIN'S seam (Phase 246) — `capabilityLawsAt`'s three laws, over
    /// the domain's own `QuerySeamWitness`: every drawn call goes through the witness's `Dispatch`
    /// with the domain's `resolver` (a per-call argument since Phase 391), counted, and the family certifies that a dispatch has exactly three
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
    let queryLawsAt
        (family: string)
        (w: QuerySeamWitness)
        (resolver: (string * Cell) list -> Query -> Deferred<QueryResult>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let known = QueryRegistry.enumerate w.Queries |> List.map _.Id

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
                match w.Dispatch id args (fun q' -> resolver args q') with
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
                  Body = resolver
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
