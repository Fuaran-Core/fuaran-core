namespace Fuaran.Core

// ============================================================================
//  Stored identity (Phase 360) — the family a content-addressed consumer runs against ITS OWN
//  stored corpus.
//
//  Every other family in the kit draws or builds its sample. This one takes the consumer's
//  persisted store as it is and asks the question a release raise has to answer before it lands:
//  does every id this store holds still recompute, under the encoding profile the store DECLARES?
//  Phase 287 moved the rendered bytes of every string carrying a line feed, carriage return or tab,
//  and a downstream content-addressed store whose payload strings carried a newline could not verify
//  its stored ids on the next release — and learned it from its own live data. A consumer that runs
//  this family in its gate learns it from the raise instead.
//
//  The declaration is the profile's canonical NAME (`v1`, `v2`), the string a store persists beside
//  its data. The witnesses are the consumer's own: `storeW` encodes ops as the store declares (its
//  `Encode` renders through `Json.renderWith` at the declared profile), `currentW` as the current
//  profile does. Three laws per store kind:
//    - the declaration names a profile this Core knows;
//    - every stored id recomputes under it;
//    - migrating to the current profile with the two-witness rehash and back again reproduces
//      every stored id — so the migration route is open, and is not a one-way door.
// ============================================================================

/// Laws a content-addressed consumer runs against its own stored corpus (Phase 360): every stored id
/// recomputes under the profile the store declares, and the two-witness migration round-trips.
module StoredIdentity =

    let private declaredCell () =
        LawKit.LawCell "the store declares an encoding profile this Core knows"

    /// The declared profile, with the first law's verdict recorded in `cell`.
    let private parseDeclared (cell: LawKit.LawCell) (declared: string) : OpStream.EncodingProfile option =
        let p = OpStream.tryProfile declared

        cell.Check(
            p.IsSome,
            fun () ->
                sprintf
                    "the store declares %A, which names no profile (known: %s)"
                    declared
                    (OpStream.profiles |> List.map OpStream.profileName |> String.concat ", ")
        )

        p

    /// The ids a round trip must give back: every `before` id mapped forward by `there` and back by
    /// `back` to itself. `None` when it does, else the first id that does not come home.
    let private firstStray (there: Map<string, string>) (back: Map<string, string>) : string option =
        there
        |> Map.toList
        |> List.tryPick (fun (k, k') ->
            match Map.tryFind k' back with
            | Some k'' when k'' = k -> None
            | _ -> Some k)

    /// The linear store's laws: `records` was chained with `hashFn` under `configFor declared`, its
    /// ops encoded by `storeW`. `currentW` encodes them under `OpStream.currentProfile`.
    let linearLaws
        (declared: string)
        (hashFn: HashFn)
        (storeW: StreamWitness<'Op, 'State, 'Rej>)
        (currentW: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : LawResult list =
        let declaredLaw = declaredCell ()

        let recompute =
            LawKit.LawCell "every stored record hash recomputes under the declared profile"

        let roundTrip =
            LawKit.LawCell "the two-witness rehash to the current profile and back reproduces every stored hash"

        match parseDeclared declaredLaw declared with
        | None ->
            recompute.Fail "not attempted: the declaration names no profile"
            roundTrip.Fail "not attempted: the declaration names no profile"
        | Some p ->
            let cfg = OpStream.configFor p
            let current = OpStream.currentProfile
            let currentCfg = OpStream.configFor current

            match OpStream.firstChainBreakWith cfg hashFn storeW records with
            | Some b ->
                roundTrip.Fail "not attempted: the stored ids do not recompute under the declared profile"

                recompute.Check(
                    false,
                    fun () ->
                        sprintf
                            "record %d: %s (expected %s, stored %s)"
                            b.Index
                            (ChainBreakReason.toString b.Reason)
                            b.Expected
                            b.Got
                )
            | None ->
                recompute.Saw()

                match OpStream.rehashEncoding cfg storeW currentCfg currentW hashFn records with
                | Error b ->
                    roundTrip.Check(false, (fun () -> sprintf "the forward rehash refused at record %d" b.Index))
                | Ok(migrated, there) ->
                    match OpStream.rehashEncoding currentCfg currentW cfg storeW hashFn migrated with
                    | Error b ->
                        roundTrip.Check(false, (fun () -> sprintf "the rehash back refused at record %d" b.Index))
                    | Ok(back, home) ->
                        roundTrip.Check(
                            List.map (fun (r: OpRecord<'Op>) -> r.Hash) back = List.map
                                (fun (r: OpRecord<'Op>) -> r.Hash)
                                records
                            && firstStray there home = None,
                            fun () ->
                                sprintf "the round trip changed a stored hash (first stray: %A)" (firstStray there home)
                        )

        [ declaredLaw.Result; recompute.Result; roundTrip.Result ]

    /// The DAG's laws: every node id in `dag` was minted with `hashFn` under `Dag.nodeIdWith declared`,
    /// its ops encoded by `storeW`. `currentW` encodes them under `OpStream.currentProfile`.
    let dagLaws
        (declared: string)
        (hashFn: HashFn)
        (storeW: StreamWitness<'Op, 'State, 'Rej>)
        (currentW: StreamWitness<'Op, 'State, 'Rej>)
        (dag: Dag.T<'Op>)
        : LawResult list =
        let declaredLaw = declaredCell ()

        let recompute =
            LawKit.LawCell "every stored node id recomputes under the declared profile"

        let roundTrip =
            LawKit.LawCell "the two-witness rehash to the current profile and back reproduces every stored id"

        match parseDeclared declaredLaw declared with
        | None ->
            recompute.Fail "not attempted: the declaration names no profile"
            roundTrip.Fail "not attempted: the declaration names no profile"
        | Some p ->
            let current = OpStream.currentProfile

            match Dag.firstBreakWith p hashFn storeW dag with
            | Some b ->
                roundTrip.Fail "not attempted: the stored ids do not recompute under the declared profile"

                recompute.Check(
                    false,
                    fun () -> sprintf "node %s: %A (expected %s, got %s)" b.NodeId b.Reason b.Expected b.Got
                )
            | None ->
                recompute.Saw()

                match Dag.rehashEncoding p storeW current currentW hashFn dag with
                | Error f -> roundTrip.Check(false, (fun () -> sprintf "the forward rehash refused: %A" f))
                | Ok(migrated, there) ->
                    match Dag.rehashEncoding current currentW p storeW hashFn migrated with
                    | Error f -> roundTrip.Check(false, (fun () -> sprintf "the rehash back refused: %A" f))
                    | Ok(back, home) ->
                        roundTrip.Check(
                            Map.keys back.Nodes |> Set.ofSeq = (Map.keys dag.Nodes |> Set.ofSeq)
                            && firstStray there home = None,
                            fun () ->
                                sprintf "the round trip changed a stored id (first stray: %A)" (firstStray there home)
                        )

        [ declaredLaw.Result; recompute.Result; roundTrip.Result ]

    /// The capture log's laws: `captures` was chained with `hashFn` from the `""` genesis, its effect
    /// and determinism tags spelled as the declared profile spells them.
    let captureLaws (declared: string) (hashFn: HashFn) (captures: EffectCapture list) : LawResult list =
        let declaredLaw = declaredCell ()

        let recompute =
            LawKit.LawCell "every stored capture hash recomputes under the declared profile"

        let roundTrip =
            LawKit.LawCell "the rehash to the current profile and back reproduces every stored capture hash"

        match parseDeclared declaredLaw declared with
        | None ->
            recompute.Fail "not attempted: the declaration names no profile"
            roundTrip.Fail "not attempted: the declaration names no profile"
        | Some p ->
            let cfg = OpStream.configFor p
            let current = OpStream.currentProfile

            match OpStream.firstCaptureBreakEncoding p cfg hashFn captures with
            | Some b ->
                roundTrip.Fail "not attempted: the stored ids do not recompute under the declared profile"

                recompute.Check(
                    false,
                    fun () ->
                        sprintf
                            "capture %d: %s (expected %s, stored %s)"
                            b.Index
                            (ChainBreakReason.toString b.Reason)
                            b.Expected
                            b.Got
                )
            | None ->
                recompute.Saw()

                match OpStream.rehashCapturesEncoding p current cfg hashFn captures with
                | Error b ->
                    roundTrip.Check(false, (fun () -> sprintf "the forward rehash refused at capture %d" b.Index))
                | Ok(migrated, there) ->
                    match OpStream.rehashCapturesEncoding current p cfg hashFn migrated with
                    | Error b ->
                        roundTrip.Check(false, (fun () -> sprintf "the rehash back refused at capture %d" b.Index))
                    | Ok(back, home) ->
                        roundTrip.Check(
                            List.map (fun (c: EffectCapture) -> c.Hash) back = List.map
                                (fun (c: EffectCapture) -> c.Hash)
                                captures
                            && firstStray there home = None,
                            fun () ->
                                sprintf "the round trip changed a stored hash (first stray: %A)" (firstStray there home)
                        )

        [ declaredLaw.Result; recompute.Result; roundTrip.Result ]
