namespace Fuaran.Core

/// The signature-typed function registry, the space relation it reads (Phase 295) and the content-pack
/// loading contract (Phase 57) — `SeamLaws` until the Phase 388 split along its banners.
module internal RegistrySeamLaws =
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
