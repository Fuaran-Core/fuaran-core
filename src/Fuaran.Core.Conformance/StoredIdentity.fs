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

    /// The chain config a profile names (Phase 516: the op stream's profiles are its config
    /// constructors) — `StreamConfig.legacyEscape` for `V1`, `StreamConfig.canonical` for `V2`.
    let private configOf (profile: EncodingProfile) : StreamConfig =
        match profile with
        | EncodingProfile.V1 -> StreamConfig.legacyEscape
        | EncodingProfile.V2 -> StreamConfig.canonical

    /// The declared profile, with the first law's verdict recorded in `cell`.
    let private parseDeclared (cell: LawKit.LawCell) (declared: string) : EncodingProfile option =
        let p = EncodingProfile.tryParse declared

        cell.Check(
            p.IsSome,
            fun () ->
                sprintf
                    "the store declares %A, which names no profile (known: %s)"
                    declared
                    (EncodingProfile.all |> List.map EncodingProfile.name |> String.concat ", ")
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

    /// What a content-addressed consumer stores (Phase 508). The three kinds were three functions
    /// holding one shape — declare, recompute, round-trip, count — and are three cases of one walk now
    /// (`storeLaws`).
    type internal Store<'Op, 'State, 'Rej> =
        /// A linear chain: `records` chained under the declared profile's config, ops encoded by the store
        /// witness; the current witness encodes them under `EncodingProfile.current`.
        | Linear of
            storeW: StreamWitness<'Op, 'State, 'Rej> *
            currentW: StreamWitness<'Op, 'State, 'Rej> *
            records: OpRecord<'Op> list
        /// A DAG: every node id minted by `Dag.nodeId` over a stream on the declared profile's config, ops
        /// encoded by the store witness; the current witness encodes them under `EncodingProfile.current`.
        | Graph of
            storeW: StreamWitness<'Op, 'State, 'Rej> *
            currentW: StreamWitness<'Op, 'State, 'Rej> *
            dag: Dag.T<'Op>
        /// A capture log chained from the `""` genesis, its effect and determinism tags spelled as the
        /// declared profile spells them.
        | Captures of captures: EffectCapture list

    /// One store kind's walk: the item it counts, its two laws' names, and the two questions asked of
    /// it under a profile — the first stored id that does not recompute, and the round trip.
    type private Walk =
        {
            Item: string
            Count: int
            Recompute: string
            RoundTrip: string
            FirstBreak: EncodingProfile -> (unit -> string) option
            /// `Error` naming the rehash that refused, or whether every stored id came home, and why not.
            Home: EncodingProfile -> Result<bool * (unit -> string), string>
        }

    let private walkOf (hashFn: HashFn) (store: Store<'Op, 'State, 'Rej>) : Walk =
        match store with
        | Linear(storeW, currentW, records) ->
            { Item = "record"
              Count = List.length records
              Recompute = "every stored record hash recomputes under the declared profile"
              RoundTrip = "the two-witness rehash to the current profile and back reproduces every stored hash"
              FirstBreak =
                fun p ->
                    OpStream.firstChainBreak (OpStream.ofConfig (configOf p) hashFn storeW) records
                    |> Option.map (fun b ->
                        fun () ->
                            sprintf
                                "record %d: %s (expected %s, stored %s)"
                                b.Index
                                (ChainBreakReason.toString b.Reason)
                                b.Expected
                                b.Got)
              Home =
                fun p ->
                    let stored = OpStream.ofConfig (configOf p) hashFn storeW
                    let current = OpStream.ofConfig (configOf EncodingProfile.current) hashFn currentW
                    let hashes (rs: OpRecord<'Op> list) = rs |> List.map (fun r -> r.Hash)

                    match OpStream.rehash stored current records with
                    | Error b -> Error(sprintf "the forward rehash refused at record %d" b.Index)
                    | Ok migrated ->
                        let there = Map.ofList (List.zip (hashes records) (hashes migrated))

                        match OpStream.rehash current stored migrated with
                        | Error b -> Error(sprintf "the rehash back refused at record %d" b.Index)
                        | Ok back ->
                            let home = Map.ofList (List.zip (hashes migrated) (hashes back))

                            Ok(
                                hashes back = hashes records && firstStray there home = None,
                                fun () ->
                                    sprintf
                                        "the round trip changed a stored hash (first stray: %A)"
                                        (firstStray there home)
                            ) }
        | Graph(storeW, currentW, dag) ->
            { Item = "node"
              Count = Map.count dag.Nodes
              Recompute = "every stored node id recomputes under the declared profile"
              RoundTrip = "the two-witness rehash to the current profile and back reproduces every stored id"
              FirstBreak =
                fun p ->
                    Dag.firstBreak (OpStream.ofConfig (configOf p) hashFn storeW) dag
                    |> Option.map (fun b ->
                        fun () -> sprintf "node %s: %A (expected %s, got %s)" b.NodeId b.Reason b.Expected b.Got)
              Home =
                fun p ->
                    let stored = OpStream.ofConfig (configOf p) hashFn storeW
                    let current = OpStream.ofConfig (configOf EncodingProfile.current) hashFn currentW

                    match Dag.rehash stored current dag with
                    | Error f -> Error(sprintf "the forward rehash refused: %A" f)
                    | Ok(migrated, there) ->
                        match Dag.rehash current stored migrated with
                        | Error f -> Error(sprintf "the rehash back refused: %A" f)
                        | Ok(back, home) ->
                            Ok(
                                Map.keys back.Nodes |> Set.ofSeq = (Map.keys dag.Nodes |> Set.ofSeq)
                                && firstStray there home = None,
                                fun () ->
                                    sprintf
                                        "the round trip changed a stored id (first stray: %A)"
                                        (firstStray there home)
                            ) }
        | Captures captures ->
            { Item = "capture"
              Count = List.length captures
              Recompute = "every stored capture hash recomputes under the declared profile"
              RoundTrip = "the rehash to the current profile and back reproduces every stored capture hash"
              FirstBreak =
                fun p ->
                    EffectCapture.firstCaptureBreak (configOf p) hashFn captures
                    |> Option.map (fun b ->
                        fun () ->
                            sprintf
                                "capture %d: %s (expected %s, stored %s)"
                                b.Index
                                (ChainBreakReason.toString b.Reason)
                                b.Expected
                                b.Got)
              Home =
                fun p ->
                    let stored = configOf p
                    let current = configOf EncodingProfile.current
                    let hashes (cs: EffectCapture list) = cs |> List.map (fun c -> c.Hash)

                    match EffectCapture.rehashCaptures stored current hashFn captures with
                    | Error b -> Error(sprintf "the forward rehash refused at capture %d" b.Index)
                    | Ok migrated ->
                        let there = Map.ofList (List.zip (hashes captures) (hashes migrated))

                        match EffectCapture.rehashCaptures current stored hashFn migrated with
                        | Error b -> Error(sprintf "the rehash back refused at capture %d" b.Index)
                        | Ok back ->
                            let home = Map.ofList (List.zip (hashes migrated) (hashes back))

                            Ok(
                                hashes back = hashes captures && firstStray there home = None,
                                fun () ->
                                    sprintf
                                        "the round trip changed a stored hash (first stray: %A)"
                                        (firstStray there home)
                            ) }

    /// A store's laws, whichever kind it is (Phase 508): the declaration names a profile this Core
    /// knows; every stored id recomputes under it; the rehash to the current profile and back
    /// reproduces every stored id; and the store holds at least one item. `declared` is the profile's
    /// canonical name, `hashFn` the hash the store's ids were minted with.
    let internal storeLaws (declared: string) (hashFn: HashFn) (store: Store<'Op, 'State, 'Rej>) : LawResult list =
        let walk = walkOf hashFn store
        let declaredLaw = declaredCell ()
        let recompute = LawKit.LawCell walk.Recompute
        let roundTrip = LawKit.LawCell walk.RoundTrip

        match parseDeclared declaredLaw declared with
        | None ->
            recompute.Fail "not attempted: the declaration names no profile"
            roundTrip.Fail "not attempted: the declaration names no profile"
        | Some p ->
            match walk.FirstBreak p with
            | Some why ->
                roundTrip.Fail "not attempted: the stored ids do not recompute under the declared profile"
                recompute.Check(false, why)
            | None ->
                recompute.Saw()

                match walk.Home p with
                | Error refused -> roundTrip.Check(false, (fun () -> refused))
                | Ok(home, why) -> roundTrip.Check(home, why)

        [ declaredLaw.Result
          recompute.Result
          roundTrip.Result
          VectorKit.storeCell "" walk.Item walk.Count ]

    /// The linear store's laws: `records` was chained with `hashFn` under the declared profile's config, its
    /// ops encoded by `storeW`. `currentW` encodes them under `EncodingProfile.current`. `storeLaws`
    /// over `Store.Linear` since Phase 508.
    let linearLaws
        (declared: string)
        (hashFn: HashFn)
        (storeW: StreamWitness<'Op, 'State, 'Rej>)
        (currentW: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : LawResult list =
        storeLaws declared hashFn (Linear(storeW, currentW, records))
        |> LawKit.labelAs "StoredIdentity.linearLaws"

    /// The DAG's laws: every node id in `dag` was minted with `hashFn` under `Dag.nodeId declared`,
    /// its ops encoded by `storeW`. `currentW` encodes them under `EncodingProfile.current`.
    /// `storeLaws` over `Store.Graph` since Phase 508.
    let dagLaws
        (declared: string)
        (hashFn: HashFn)
        (storeW: StreamWitness<'Op, 'State, 'Rej>)
        (currentW: StreamWitness<'Op, 'State, 'Rej>)
        (dag: Dag.T<'Op>)
        : LawResult list =
        storeLaws declared hashFn (Graph(storeW, currentW, dag))
        |> LawKit.labelAs "StoredIdentity.dagLaws"

    /// The capture log's laws: `captures` was chained with `hashFn` from the `""` genesis, its effect
    /// and determinism tags spelled as the declared profile spells them. `storeLaws` over
    /// `Store.Captures` since Phase 508.
    let captureLaws (declared: string) (hashFn: HashFn) (captures: EffectCapture list) : LawResult list =
        storeLaws declared hashFn (Store<unit, unit, unit>.Captures captures)
        |> LawKit.labelAs "StoredIdentity.captureLaws"
