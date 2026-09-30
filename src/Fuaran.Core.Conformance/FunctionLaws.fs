namespace Fuaran.Core

/// The function families (Phase 297 split): composition, verification, memoisation and honesty.
module internal FunctionLaws =

    /// The cross-witness composition laws (Phase 47) — the teeth on `Function.composeAcross`.
    /// A domain supplies the outer witness `wa`, the inner witness `wb`, the cross-witness slot
    /// binding `embed : 'B -> 'A`, and a `draw` of a `CompositionSample`; over a seed-replayable
    /// sample the kit certifies, across the heterogeneous boundary:
    ///
    ///  - **apply ∘ composeAcross = the nested application** — composing both inners into the
    ///    slots and THEN strict-applying the outer's value holes equals binding the value holes
    ///    first (`curry`) and then composing; composition and application commute;
    ///  - **associative where typed** — `composeAcross` into two independent slots is
    ///    order-independent (the algebraic associativity of the composition, and a direct witness
    ///    that the two embedded sub-functions don't interfere);
    ///  - **hygiene holds under re-binding** — wiring two same-named (distinct-address) inner
    ///    holes into the two slots exposes two distinct re-rooted copies; binding one leaves the
    ///    other open — no capture (Fork 2);
    ///  - **effect signature joins componentwise** — `composedEffectAcross` equals the join of
    ///    the parts' effects and covers each (Fork 3).
    ///
    /// `'A` needs equality (it compares composed trees). Opt-in like `capabilityLaws` /
    /// `queryLaws` — a domain that composes across witnesses runs it alongside its base certification.
    let compositionLaws
        (wa: ArtifactWitness<'A, 'IdA>)
        (wb: ArtifactWitness<'B, 'IdB>)
        (embed: 'B -> 'A)
        (draw: ConfRng.T -> CompositionSample<'A, 'B> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable nested = None
        let mutable associative = None
        let mutable hygiene = None
        let mutable effectJoin = None

        for i in 0 .. iterations - 1 do
            let s, r = draw rng
            rng <- r

            let argMap = s.OuterArgs |> List.map (fun (a, v) -> a, ValueArg v) |> Map.ofList

            // ---- apply ∘ composeAcross = the nested application ----
            // Compose both inners into the slots, then strict-apply the value holes; vs. bind the
            // value holes first (curry), then compose. Composition and application commute.
            let viaCompose =
                Function.composeAcross wa wb embed s.SlotA s.ClosedInner s.Outer
                |> Result.bind (Function.composeAcross wa wb embed s.SlotB s.ClosedInner)
                |> Result.bind (Function.apply wa argMap)

            let viaApply =
                Function.curry wa argMap s.Outer
                |> Result.bind (Function.composeAcross wa wb embed s.SlotA s.ClosedInner)
                |> Result.bind (Function.composeAcross wa wb embed s.SlotB s.ClosedInner)

            if viaCompose <> viaApply && nested.IsNone then
                nested <-
                    Some(
                        sprintf
                            "seed=%d iter=%d: apply∘composeAcross ≠ nested application (%A vs %A)"
                            seed
                            i
                            viaCompose
                            viaApply
                    )

            // ---- associative where typed (disjoint-slot composition is order-independent) ----
            let orderAB =
                Function.composeAcross wa wb embed s.SlotA s.ClosedInner s.Outer
                |> Result.bind (Function.composeAcross wa wb embed s.SlotB s.ClosedInner)

            let orderBA =
                Function.composeAcross wa wb embed s.SlotB s.ClosedInner s.Outer
                |> Result.bind (Function.composeAcross wa wb embed s.SlotA s.ClosedInner)

            if orderAB <> orderBA && associative.IsNone then
                associative <-
                    Some(sprintf "seed=%d iter=%d: disjoint-slot composeAcross is not order-independent" seed i)

            // ---- hygiene holds under re-binding ----
            // Wire two same-named (distinct-id) inner holes into the two slots; each re-roots under
            // its slot's absolute address, so the composed function exposes two distinct copies.
            // Binding one must leave the other open (Fork 2 — no capture).
            let twoCopies =
                Function.composeAcross wa wb embed s.SlotA s.OpenInnerA s.Outer
                |> Result.bind (Function.composeAcross wa wb embed s.SlotB s.OpenInnerB)

            (match twoCopies with
             | Error e ->
                 if hygiene.IsNone then
                     hygiene <-
                         Some(sprintf "seed=%d iter=%d: composing open inners into both slots failed: %A" seed i e)
             | Ok composed ->
                 let copies =
                     (Function.signature wa "comp" composed).Holes
                     |> List.filter (fun h -> h.Name = s.OpenHoleName)
                     |> List.map (fun h -> h.Addr)

                 match copies with
                 | [ a1; a2 ] when a1 <> a2 ->
                     match Function.curry wa (Map.ofList [ a1, ValueArg s.OpenHoleArg ]) composed with
                     | Ok bound ->
                         let after =
                             (Function.signature wa "comp" bound).Holes
                             |> List.map (fun h -> h.Addr)
                             |> Set.ofList

                         if (after.Contains a1 || not (after.Contains a2)) && hygiene.IsNone then
                             hygiene <-
                                 Some(sprintf "seed=%d iter=%d: re-binding %s captured the other copy %s" seed i a1 a2)
                     | Error e ->
                         if hygiene.IsNone then
                             hygiene <- Some(sprintf "seed=%d iter=%d: binding one copy failed: %A" seed i e)
                 | other ->
                     if hygiene.IsNone then
                         hygiene <-
                             Some(
                                 sprintf "seed=%d iter=%d: expected two distinct re-rooted copies, got %A" seed i other
                             ))

            // ---- effect signature joins componentwise across the boundary (Fork 3) ----
            let joined = Function.composedEffectAcross wa wb s.OpenInnerA s.Outer
            let expected = Effect.join (wa.Effect s.Outer) (wb.Effect s.OpenInnerA)

            if
                (joined <> expected
                 || not (Effect.covers joined (wa.Effect s.Outer))
                 || not (Effect.covers joined (wb.Effect s.OpenInnerA)))
                && effectJoin.IsNone
            then
                effectJoin <- Some(sprintf "seed=%d iter=%d: composed effect ≠ join of parts (Fork 3)" seed i)

        [ { Law = "apply ∘ composeAcross = the nested application"
            Passed = nested.IsNone
            Counterexample = nested }
          { Law = "composeAcross is associative where typed (disjoint slots commute)"
            Passed = associative.IsNone
            Counterexample = associative }
          { Law = "hygiene holds under re-binding (no cross-slot capture)"
            Passed = hygiene.IsNone
            Counterexample = hygiene }
          { Law = "composed effect = componentwise join of the parts' (Fork 3)"
            Passed = effectJoin.IsNone
            Counterexample = effectJoin } ]

    /// Apply one param-set, run the domain validator over the result, and effect-audit it — the
    /// per-case oracle shared by `verifyFunction` and `verifyFunctionSymbolic`. Returns the first
    /// defect found, else `None`. Total: `apply` is total, `Validator.runAll` walks a pure registry,
    /// and `auditEffect` returns a typed `Result` — no stage throws.
    let private verifyCase
        (w: ArtifactWitness<'Node, 'Id>)
        (reg: Validator.Registry<'Node, 'Id>)
        (fn: 'Node)
        (seed: int)
        (iter: int)
        (pset: Map<string, Arg<'Node>>)
        : VerifyCounterexample<'Node, 'Id> option =
        let mk defect =
            Some
                { ParamSet = Map.toList pset
                  Defect = defect
                  Seed = seed
                  Iteration = iter }

        match Function.apply w pset fn with
        | Error e -> mk (DidNotApply e)
        | Ok tree ->
            let defects = Validator.runAll w.Tree reg tree

            if Validator.hasErrors defects then
                mk (ValidatorRejected defects)
            else
                match Function.auditEffect w tree with
                | Error(declared, observed) -> mk (EffectObserved(declared, observed))
                | Ok() -> None

    /// Property-verify an artifact-function against a domain `Validator.Registry` over a *supplied*
    /// param-set generator (Phase 48): draw up to `iterations` valid param-sets, `apply` the
    /// function, and assert the validator passes (no `Severity.Error`) AND the result observes no
    /// undeclared effect (Fork-3) on every one. The first failure stops the run and is returned as a
    /// reproducible `(param-set, defect)` counterexample. Deterministic — the same seed reproduces
    /// the verdict. `genParams` receives the function so it can read `w.Holes` to fill the holes.
    ///
    /// **Contract honesty boundary (Phase 52) — what this DOES and does NOT certify.** `verifyFunction`
    /// certifies that the function emits a **validator-conformant tree for every binding in the
    /// sampled / symbolic param space** — i.e. *structural validity over the param space*. It does
    /// **NOT** certify that the output is *semantically good* (quality), nor that a function whose
    /// effect class is non-deterministic (`Clock` / `Random` / `Network` — a stochastic spec, e.g. an
    /// MMM / `Fuaran.Model` dialect) produces *deterministic or high-quality* output: for such a
    /// function the verdict asserts **structural validity only**, never output determinism or quality.
    /// The effect axis does not change the verdict — verify keys on structure, not on the determinism
    /// class (the `verifyHonestyLaws` guard proves this). Reading "verified" as a quality / determinism
    /// guarantee is an over-claim the statistical domains must not make. See
    /// [`../../STABILITY.md`](STABILITY.md) "verifyFunction guarantee scope".
    let verifyFunction
        (w: ArtifactWitness<'Node, 'Id>)
        (fn: 'Node)
        (reg: Validator.Registry<'Node, 'Id>)
        (genParams: 'Node -> ConfRng.T -> Map<string, Arg<'Node>> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : FunctionVerifyReport<'Node, 'Id> =
        let mutable rng = ConfRng.ofSeed seed
        let mutable counterexample = None
        let mutable i = 0

        while counterexample.IsNone && i < iterations do
            let pset, r' = genParams fn rng
            rng <- r'
            counterexample <- verifyCase w reg fn seed i pset
            i <- i + 1

        { Verified = counterexample.IsNone
          Coverage = Sampled(i, None)
          Counterexample = counterexample }

    /// The per-hole symbolic domain (Phase 48): a finite candidate list (enumerable), or a sampler
    /// for a large / unbounded space, carrying the space size where it is bounded-but-large.
    type private HoleDomain =
        | FiniteDom of string list
        | SampledDom of sampler: (ConfRng.T -> string * ConfRng.T) * size: int option

    /// Project a value-space into a symbolic domain. `Enum` / a small `IntRange` enumerate; a large
    /// `IntRange` samples uniformly in-range (carrying its finite size); the genuinely-large /
    /// unbounded spaces (`FloatRange` / `StringLen` / `AnyString`) sample with no finite size. Sample
    /// strings use culture-invariant `string` / `sprintf "%f"` (Fable-clean, no `CultureInfo`).
    let private domainOf (maxCases: int) (space: ValueSpace) : HoleDomain =
        match space with
        | Enum xs -> FiniteDom xs
        | IntRange(lo, hi) ->
            let n = hi - lo + 1

            if n <= 0 then
                FiniteDom []
            elif n <= maxCases then
                FiniteDom [ for v in lo..hi -> string v ]
            else
                SampledDom(
                    (fun r ->
                        let k, r' = ConfRng.intBelow n r
                        string (lo + k), r'),
                    Some n
                )
        | StringLen(lo, hi) ->
            let span = (max 0 (hi - lo)) + 1

            SampledDom(
                (fun r ->
                    let k, r' = ConfRng.intBelow span r
                    String.replicate (lo + k) "a", r'),
                None
            )
        | FloatRange(lo, hi) ->
            SampledDom(
                (fun r ->
                    let k, r' = ConfRng.intBelow 1001 r
                    sprintf "%f" (lo + (hi - lo) * (float k / 1000.0)), r'),
                None
            )
        | AnyString -> SampledDom((fun r -> let k, r' = ConfRng.intBelow 1000 r in "s" + string k, r'), None)
        // A tree space (Phase 229) samples wire documents of the constrained kind (a drawn kind
        // when unconstrained); it is unbounded, so it carries no finite size.
        | SlotTree c ->
            SampledDom(
                (fun r ->
                    let k, r' = ConfRng.intBelow 1000 r

                    let kind =
                        match c with
                        | Some kc -> kc
                        | None -> "k" + string k

                    Json.render (Json.kindObj kind [ "n", JInt k ]), r'),
                None
            )

    /// Property-verify an artifact-function by deriving the param space from its holes' value-spaces
    /// (Phase 48) — the symbolic / bounded mode. Value / repeat holes are enumerated **exhaustively**
    /// when their combined finite space is small (≤ `maxCases`) and **sampled** (`maxCases` draws)
    /// when it is large or unbounded, with the coverage reported either way (never silently
    /// sample-and-claim-verified). Slot holes — and any value hole the caller pins in `fixedArgs` —
    /// are held constant while the remaining value holes vary; `fixedArgs` must cover every slot hole
    /// (strict `apply` demands it). Each case is applied, validated against `reg`, and effect-audited;
    /// the first failure is a reproducible counterexample. `'Node` is unconstrained.
    let verifyFunctionSymbolic
        (w: ArtifactWitness<'Node, 'Id>)
        (fn: 'Node)
        (reg: Validator.Registry<'Node, 'Id>)
        (fixedArgs: Map<string, Arg<'Node>>)
        (maxCases: int)
        (seed: int)
        : FunctionVerifyReport<'Node, 'Id> =
        // value / repeat holes carry a value-space we can project; a hole pinned in fixedArgs (and
        // every slot) is held constant, not varied.
        let valueHoles =
            w.Holes fn
            |> List.choose (fun h ->
                match h.Kind with
                | (ValueHole space | RepeatHole space) when not (Map.containsKey h.Addr fixedArgs) ->
                    Some(h.Addr, domainOf maxCases space)
                | _ -> None)

        // enumerable ⇒ every varying hole has an explicit candidate list; sizeKnown ⇒ every varying
        // hole has a finite size (so the total space size is reportable).
        let enumerable =
            valueHoles
            |> List.forall (fun (_, d) ->
                match d with
                | FiniteDom _ -> true
                | SampledDom _ -> false)

        let sizeKnown =
            valueHoles
            |> List.forall (fun (_, d) ->
                match d with
                | SampledDom(_, None) -> false
                | _ -> true)

        // the finite product (meaningful only when sizeKnown) — drives exhaustive vs sampled + size.
        let finiteProduct =
            valueHoles
            |> List.fold
                (fun acc (_, d) ->
                    match d with
                    | FiniteDom xs -> acc * List.length xs
                    | SampledDom(_, Some n) -> acc * n
                    | SampledDom(_, None) -> acc)
                1

        let psetOf (valueArgs: (string * string) list) : Map<string, Arg<'Node>> =
            (fixedArgs, valueArgs) ||> List.fold (fun m (a, v) -> Map.add a (ValueArg v) m)

        if enumerable && finiteProduct <= maxCases then
            // exhaustive — enumerate the cartesian product of the per-hole candidate lists.
            let lists =
                valueHoles
                |> List.map (fun (a, d) ->
                    match d with
                    | FiniteDom xs -> a, xs
                    | SampledDom _ -> a, []) // unreachable under `enumerable`

            let rec cartesian =
                function
                | [] -> [ [] ]
                | (addr, vals) :: rest ->
                    let tails = cartesian rest

                    [ for v in vals do
                          for t in tails -> (addr, v) :: t ]

            let combos = List.toArray (cartesian lists)
            let mutable cx = None
            let mutable i = 0

            while cx.IsNone && i < combos.Length do
                cx <- verifyCase w reg fn seed i (psetOf combos.[i])
                i <- i + 1

            { Verified = cx.IsNone
              Coverage = Exhaustive combos.Length
              Counterexample = cx }
        else
            // sampled — draw `maxCases` param-sets, each varying hole drawn from its domain.
            let mutable rng = ConfRng.ofSeed seed
            let mutable cx = None
            let mutable i = 0

            while cx.IsNone && i < maxCases do
                let mutable r = rng

                let valueArgs =
                    valueHoles
                    |> List.map (fun (a, d) ->
                        match d with
                        | FiniteDom [] -> a, "" // an unsatisfiable hole — apply rejects (kept total)
                        | FiniteDom xs ->
                            let v, r' = ConfRng.choose xs r
                            r <- r'
                            a, v
                        | SampledDom(sampler, _) ->
                            let v, r' = sampler r
                            r <- r'
                            a, v)

                rng <- r
                cx <- verifyCase w reg fn seed i (psetOf valueArgs)
                i <- i + 1

            { Verified = cx.IsNone
              Coverage = Sampled(maxCases, (if sizeKnown then Some finiteProduct else None))
              Counterexample = cx }

    /// Render a counterexample as one readable line (Phase 48): the offending param-set
    /// (`addr=value`; a slot shown as `<slot:kind>`) and the defect, prefixed with the seed +
    /// iteration so it reproduces. The "readable counterexample" the acceptance calls for.
    let renderCounterexample (w: ArtifactWitness<'Node, 'Id>) (cx: VerifyCounterexample<'Node, 'Id>) : string =
        let renderArg =
            function
            | ValueArg s -> s
            | SlotArg n -> "<slot:" + w.Tree.KindTag n + ">"

        let pset =
            cx.ParamSet
            |> List.map (fun (a, arg) -> a + "=" + renderArg arg)
            |> String.concat ", "

        let defect =
            match cx.Defect with
            | DidNotApply e -> sprintf "apply rejected the param-set (%A)" e
            | ValidatorRejected ds ->
                ds
                |> List.map (fun d -> d.Code + ": " + d.Message)
                |> String.concat "; "
                |> sprintf "validator faulted the result — %s"
            | EffectObserved(declared, observed) -> sprintf "effect leak — declared %A, observed %A" declared observed

        sprintf "seed=%d iter=%d: { %s } ⇒ %s" cx.Seed cx.Iteration pset defect

    /// The artifact-function verification laws (Phase 48) — the teeth on `verifyFunction`. A domain
    /// supplies the witness, a `sound` function (correct-by-construction across its param space), a
    /// `broken` function (a deliberately-too-wide hole admitting a validator-rejected value), the
    /// domain validator `reg`, and a valid param-set generator; the kit certifies:
    ///
    ///  - **a sound function verifies clean** — no counterexample over the sampled param space;
    ///  - **a broken function fails with a readable counterexample** — `Verified = false` with a
    ///    non-empty `(param-set, defect)` counterexample;
    ///  - **verification is deterministic** — the same seed reproduces the identical report.
    ///
    /// `'Node` needs equality (the determinism law compares whole reports). `iterations` must be
    /// large enough that the broken function's bad sub-space is hit (seed-replayable, so a miss is
    /// reproducible, never random).
    let functionVerifyLaws
        (w: ArtifactWitness<'Node, 'Id>)
        (sound: 'Node)
        (broken: 'Node)
        (reg: Validator.Registry<'Node, 'Id>)
        (genParams: 'Node -> ConfRng.T -> Map<string, Arg<'Node>> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let soundReport = verifyFunction w sound reg genParams seed iterations
        let brokenReport = verifyFunction w broken reg genParams seed iterations
        let brokenAgain = verifyFunction w broken reg genParams seed iterations

        let soundLaw =
            match soundReport.Verified, soundReport.Counterexample with
            | false, Some cx ->
                Some(sprintf "seed=%d: a sound function failed verification — %s" seed (renderCounterexample w cx))
            | _ -> None

        let brokenLaw =
            match brokenReport.Verified, brokenReport.Counterexample with
            | false, Some cx ->
                if System.String.IsNullOrWhiteSpace(renderCounterexample w cx) then
                    Some(sprintf "seed=%d: the broken function's counterexample was empty (not readable)" seed)
                else
                    None
            | _ -> Some(sprintf "seed=%d: a broken function verified clean (no counterexample surfaced)" seed)

        let detLaw =
            if brokenReport = brokenAgain then
                None
            else
                Some(sprintf "seed=%d: verification was not deterministic (same seed ⇒ different report)" seed)

        [ { Law = "a sound artifact-function verifies clean across its param space"
            Passed = soundLaw.IsNone
            Counterexample = soundLaw }
          { Law = "a broken artifact-function fails with a readable (param-set, defect) counterexample"
            Passed = brokenLaw.IsNone
            Counterexample = brokenLaw }
          { Law = "verification is deterministic (same seed ⇒ identical report)"
            Passed = detLaw.IsNone
            Counterexample = detLaw } ]

    /// The memoised-application laws (Phase 49) — the teeth on `Function.applyMemo`. A domain supplies
    /// the witness `w`, the canonical node-encoder `encode` (the cache-key content hash), and a `draw`
    /// of a `MemoSample`; over a seed-replayable sample the kit certifies:
    ///
    ///  - **a memoised apply equals the direct apply (pure fn)** — `applyMemo` returns byte-identically
    ///    what `apply` produces on a miss, and a re-apply of the same `(function, param-set)` is a cache
    ///    HIT returning the same tree (the unchanged function served, not re-derived);
    ///  - **a changed param-set misses** — applying a *different* valid param-set keys distinctly (a
    ///    miss, not a stale hit), while the original param-set still re-hits;
    ///  - **an effecting function is never served from cache** — a non-memoisable (non-deterministic /
    ///    host-effecting) function BYPASSES the cache: it computes the correct result directly, stores
    ///    nothing, and is never served on a re-apply (the soundness guard, Fork 3);
    ///  - **replay-as-re-application matches direct replay** — folding `OpStream.replay` over a
    ///    memo-carrying state (each recorded op re-applied through `applyMemo`) reproduces the
    ///    non-memo `OpStream.replay` result exactly, with a repeated op served from the cache (op-stream
    ///    replay collapses into re-application over the same memo).
    ///
    /// `'Node` needs equality (it compares result trees + replay states). Opt-in like `compositionLaws`
    /// — a domain that memoises application runs it alongside its base certification.
    let memoLaws
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (draw: ConfRng.T -> MemoSample<'Node> * ConfRng.T)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable equalsDirect = None
        let mutable paramMiss = None
        let mutable effectingBypassed = None
        let mutable replayParity = None

        for i in 0 .. iterations - 1 do
            let s, r = draw rng
            rng <- r

            // ---- 1. a memoised apply equals the direct apply (pure fn); a re-apply is a hit ----
            (match Function.apply w s.Args s.PureFn, Function.applyMemo w encode s.Args s.PureFn Memo.empty with
             | Ok direct, Ok(memo1, c1) ->
                 match Function.applyMemo w encode s.Args s.PureFn c1 with
                 | Ok(memo2, c2) ->
                     if
                         (memo1 <> direct
                          || memo2 <> direct
                          || c1.Misses <> 1
                          || c1.Hits <> 0
                          || c2.Hits <> 1)
                         && equalsDirect.IsNone
                     then
                         equalsDirect <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: memoised apply ≠ direct apply / re-apply was not a hit"
                                     seed
                                     i
                             )
                 | Error e ->
                     if equalsDirect.IsNone then
                         equalsDirect <- Some(sprintf "seed=%d iter=%d: a re-apply errored: %A" seed i e)
             | other ->
                 if equalsDirect.IsNone then
                     equalsDirect <-
                         Some(sprintf "seed=%d iter=%d: apply / applyMemo disagreed or errored: %A" seed i other))

            // ---- 2. a changed param-set misses; the original still re-hits ----
            (match Function.applyMemo w encode s.Args s.PureFn Memo.empty with
             | Ok(_, c1) ->
                 match Function.applyMemo w encode s.ArgsAlt s.PureFn c1 with
                 | Ok(_, c2) ->
                     match Function.applyMemo w encode s.Args s.PureFn c2 with
                     | Ok(_, c3) ->
                         if (c2.Misses <> 2 || c2.Hits <> 0 || c3.Hits <> 1) && paramMiss.IsNone then
                             paramMiss <-
                                 Some(
                                     sprintf
                                         "seed=%d iter=%d: a changed param-set did not miss / the original did not re-hit (misses=%d hits=%d→%d)"
                                         seed
                                         i
                                         c2.Misses
                                         c2.Hits
                                         c3.Hits
                                 )
                     | Error e ->
                         if paramMiss.IsNone then
                             paramMiss <- Some(sprintf "seed=%d iter=%d: re-apply of the original errored: %A" seed i e)
                 | Error e ->
                     if paramMiss.IsNone then
                         paramMiss <-
                             Some(sprintf "seed=%d iter=%d: apply of the alternate param-set errored: %A" seed i e)
             | Error e ->
                 if paramMiss.IsNone then
                     paramMiss <- Some(sprintf "seed=%d iter=%d: first apply errored: %A" seed i e))

            // ---- 3. an effecting function is never served from (or stored in) the cache ----
            (match
                Function.apply w s.EffectingArgs s.EffectingFn,
                Function.applyMemo w encode s.EffectingArgs s.EffectingFn Memo.empty
             with
             | Ok direct, Ok(m1, c1) ->
                 match Function.applyMemo w encode s.EffectingArgs s.EffectingFn c1 with
                 | Ok(m2, c2) ->
                     if
                         (m1 <> direct
                          || m2 <> direct
                          || not (Map.isEmpty c1.Entries)
                          || c1.Hits <> 0
                          || c1.Bypasses <> 1
                          || c2.Hits <> 0
                          || not (Map.isEmpty c2.Entries))
                         && effectingBypassed.IsNone
                     then
                         effectingBypassed <-
                             Some(
                                 sprintf "seed=%d iter=%d: an effecting function was cached or served from cache" seed i
                             )
                 | Error e ->
                     if effectingBypassed.IsNone then
                         effectingBypassed <-
                             Some(sprintf "seed=%d iter=%d: a re-apply of the effecting fn errored: %A" seed i e)
             | other ->
                 if effectingBypassed.IsNone then
                     effectingBypassed <-
                         Some(
                             sprintf
                                 "seed=%d iter=%d: apply / applyMemo of the effecting fn disagreed or errored: %A"
                                 seed
                                 i
                                 other
                         ))

            // ---- 4. replay-as-re-application matches direct replay (a repeat served from cache) ----
            // Build a recorded "session" of re-applications [Args; Args; ArgsAlt]; replay it both with a
            // memo-carrying state (each op re-applied through `applyMemo`) and plainly (`apply`). The two
            // final states must agree, and the repeated op must have hit the cache. `OpStream.replay`
            // needs no change — its replay seam already folds the (memoised) reducer over a threaded state.
            let swPlain: StreamWitness<Map<string, Arg<'Node>>, 'Node, ApplyError> =
                { Apply = fun op _ -> Function.apply w op s.PureFn
                  Encode = fun _ -> "{}"
                  Decode = fun _ -> Ok Map.empty }

            let swMemo: StreamWitness<Map<string, Arg<'Node>>, 'Node * MemoCache<'Node>, ApplyError> =
                { Apply = fun op (_, cache) -> Function.applyMemo w encode op s.PureFn cache
                  Encode = fun _ -> "{}"
                  Decode = fun _ -> Ok Map.empty }

            let recordsResult =
                (Ok(s.PureFn, OpStream.empty), [ s.Args; s.Args; s.ArgsAlt ])
                ||> List.fold (fun acc op ->
                    acc
                    |> Result.bind (fun (st, rs) -> OpStream.append hashFn swPlain (Human "memo") op st rs))
                |> Result.map snd

            (match recordsResult with
             | Ok records ->
                 match
                     OpStream.replay swMemo (s.PureFn, Memo.empty) records, OpStream.replay swPlain s.PureFn records
                 with
                 | Ok(mNode, mCache), Ok pNode ->
                     if (mNode <> pNode || mCache.Hits < 1) && replayParity.IsNone then
                         replayParity <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: memo replay ≠ direct replay or the repeated op was not served from cache (hits=%d)"
                                     seed
                                     i
                                     mCache.Hits
                             )
                 | other ->
                     if replayParity.IsNone then
                         replayParity <- Some(sprintf "seed=%d iter=%d: a replay errored: %A" seed i other)
             | Error e ->
                 if replayParity.IsNone then
                     replayParity <- Some(sprintf "seed=%d iter=%d: building the record session errored: %A" seed i e))

        [ { Law = "a memoised apply equals the direct apply (a re-apply is a cache hit)"
            Passed = equalsDirect.IsNone
            Counterexample = equalsDirect }
          { Law = "a changed param-set misses (the original still re-hits)"
            Passed = paramMiss.IsNone
            Counterexample = paramMiss }
          { Law = "an effecting function is never served from cache (bypassed — soundness, Fork 3)"
            Passed = effectingBypassed.IsNone
            Counterexample = effectingBypassed }
          { Law = "replay-as-re-application matches direct replay (a repeat served from cache)"
            Passed = replayParity.IsNone
            Counterexample = replayParity } ]

    // ---- cross-witness composition pilot (Phase 51) ----
    // Validate the Wave-13 frontier operators (`composeAcross`, Phase 47; `applyMemo`, Phase 49)
    // against a real **heterogeneous-witness pair** — the way the in-repo reference witness validated
    // the base surface. Where `compositionLaws` is run against a single witness twice today, the pilot
    // runs `composeAcross` + `applyMemo` across TWO structurally-distinct in-repo reference witnesses,
    // certifying the cross-domain operators work generically — not just within one witness. Reuses the
    // shipped `compositionLaws` for the composeAcross half (combined-signature validity via nested
    // application, associativity, hygiene, effect-join across the boundary) and adds the memo half. No
    // new witness field — the cross-witness `embed`/`encode` ride as per-call parameters (GP2).

    /// The cross-witness composition pilot (Phase 51) — the teeth on `composeAcross` + `applyMemo`
    /// across a genuinely-distinct witness pair. `wa`/`wb` are the outer/inner witnesses, `embed : 'B ->
    /// 'A` the cross-witness lift, `encodeA`/`encodeB` the per-witness content encoders (the memo
    /// keys), and `draw` a `CompositionSample` source. Returns the four `compositionLaws` results (the
    /// composeAcross half — combined-signature validity, associativity, hygiene, effect-join across the
    /// boundary) PLUS two `applyMemo` laws across the boundary:
    ///
    ///  - **a pure cross-witness sub-function memoises** — a closed (no-hole) pure `wb` sub-function
    ///    applied through `applyMemo` MISSES on first apply and HITS on re-apply (the unchanged
    ///    sub-function served from cache, not re-derived);
    ///  - **applyMemo over the cross-witness-composed function equals direct apply** — composing the
    ///    closed inner into both slots then `applyMemo`-ing the outer's value holes is byte-identical to
    ///    the direct `apply`, and a re-apply of the unchanged composed function is a cache hit.
    ///
    /// `'A` needs equality (it compares composed trees). The pilot surfaces NO new Core seam — the
    /// frontier operators carry across the boundary on the existing per-call parameters.
    let compositionPilot
        (wa: ArtifactWitness<'A, 'IdA>)
        (wb: ArtifactWitness<'B, 'IdB>)
        (embed: 'B -> 'A)
        (encodeA: 'A -> string)
        (encodeB: 'B -> string)
        (draw: ConfRng.T -> CompositionSample<'A, 'B> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        // The composeAcross half — over the genuinely-distinct (wa, wb, embed) triple.
        let composition = compositionLaws wa wb embed draw seed iterations

        // The applyMemo half — an independent stream so the pilot stays deterministic per seed.
        let mutable rng = ConfRng.ofSeed (seed + 101)
        let mutable subMemo = None
        let mutable composedMemo = None

        for i in 0 .. iterations - 1 do
            let s, r = draw rng
            rng <- r

            // ---- a pure cross-witness sub-function memoises (miss then hit) ----
            (match Function.applyMemo wb encodeB Map.empty s.ClosedInner Memo.empty with
             | Ok(m1, c1) ->
                 match Function.applyMemo wb encodeB Map.empty s.ClosedInner c1 with
                 | Ok(m2, c2) ->
                     if (m1 <> m2 || c1.Misses <> 1 || c1.Hits <> 0 || c2.Hits <> 1) && subMemo.IsNone then
                         subMemo <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: a pure cross-witness sub-function did not memoise (miss then hit)"
                                     seed
                                     i
                             )
                 | Error e ->
                     if subMemo.IsNone then
                         subMemo <- Some(sprintf "seed=%d iter=%d: sub-function re-apply errored: %A" seed i e)
             | Error e ->
                 if subMemo.IsNone then
                     subMemo <- Some(sprintf "seed=%d iter=%d: sub-function apply errored: %A" seed i e))

            // ---- applyMemo over the cross-witness-COMPOSED function = direct apply (re-apply hits) ----
            let argMap = s.OuterArgs |> List.map (fun (a, v) -> a, ValueArg v) |> Map.ofList

            let composedR =
                Function.composeAcross wa wb embed s.SlotA s.ClosedInner s.Outer
                |> Result.bind (Function.composeAcross wa wb embed s.SlotB s.ClosedInner)

            (match composedR with
             | Ok composed ->
                 match Function.apply wa argMap composed, Function.applyMemo wa encodeA argMap composed Memo.empty with
                 | Ok direct, Ok(mm1, c1) ->
                     match Function.applyMemo wa encodeA argMap composed c1 with
                     | Ok(mm2, c2) ->
                         if
                             (mm1 <> direct || mm2 <> direct || c1.Misses <> 1 || c2.Hits <> 1)
                             && composedMemo.IsNone
                         then
                             composedMemo <-
                                 Some(
                                     sprintf
                                         "seed=%d iter=%d: applyMemo over the composed function ≠ direct apply / no re-apply hit"
                                         seed
                                         i
                                 )
                     | Error e ->
                         if composedMemo.IsNone then
                             composedMemo <- Some(sprintf "seed=%d iter=%d: composed re-apply errored: %A" seed i e)
                 | other ->
                     if composedMemo.IsNone then
                         composedMemo <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: apply / applyMemo over the composed fn disagreed: %A"
                                     seed
                                     i
                                     other
                             )
             | Error e ->
                 if composedMemo.IsNone then
                     composedMemo <- Some(sprintf "seed=%d iter=%d: composeAcross into both slots failed: %A" seed i e))

        composition
        @ [ { Law = "a pure cross-witness sub-function memoises (miss then hit on re-apply)"
              Passed = subMemo.IsNone
              Counterexample = subMemo }
            { Law = "applyMemo over the cross-witness-composed function equals direct apply (re-apply is a hit)"
              Passed = composedMemo.IsNone
              Counterexample = composedMemo } ]

    // ---- verifyFunction contract honesty boundary (Phase 52) ----
    // The teeth on the `verifyFunction` (Phase 48) contract: it certifies a function emits a
    // validator-conformant tree across its param space (STRUCTURAL validity), NOT that the output is
    // good — and for a non-deterministic-effect (stochastic) function it asserts structure only, never
    // output determinism or quality. The guard law makes the boundary executable so the capability
    // ships correctly-scoped rather than being walked back in the statistical domains (MMM / a future
    // `Fuaran.Model` dialect). No new surface — a contract/scope clarification + one conformance law.

    /// The `verifyFunction` honesty-boundary laws (Phase 52) — an **effect-class-aware** guard on the
    /// Phase-48 verification contract. A domain supplies the witness, a `mkSound : DeterminismSource ->
    /// 'Node` builder (a correct-by-construction function under a chosen effect-determinism axis), a
    /// `mkBroken` builder (a structurally-too-wide function admitting a validator-rejected binding), the
    /// domain validator `reg`, and a valid param-set generator; the kit certifies:
    ///
    ///  - **a stochastic-effect function verifies for structural validity** — `mkSound Random` (a
    ///    non-deterministic effect class) verifies clean across the sampled, *value-varying* param
    ///    space: the verdict is about the tree's structure for each binding, NOT output determinism or
    ///    quality (a structurally-valid but value-varying output still verifies);
    ///  - **verification makes no output-determinism / quality claim** — the structural verdict is
    ///    effect-class-AGNOSTIC: every determinism axis (`Deterministic` / `Clock` / `Random` /
    ///    `Network`) yields the SAME verdict — all verify for the sound function, none verify for the
    ///    broken one — so "verified" certifies structure, never the determinism class of the effect.
    ///
    /// This is the executable form of the contract clarification in the `verifyFunction` doc +
    /// `STABILITY.md`: the capability never over-claims a quality / determinism guarantee. Deterministic
    /// — the same seed reproduces the verdict.
    let verifyHonestyLaws
        (w: ArtifactWitness<'Node, 'Id>)
        (mkSound: DeterminismSource -> 'Node)
        (mkBroken: DeterminismSource -> 'Node)
        (reg: Validator.Registry<'Node, 'Id>)
        (genParams: 'Node -> ConfRng.T -> Map<string, Arg<'Node>> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let axes = [ Deterministic; Clock; Random; Network ]

        // a stochastic (Random) sound function verifies on structure across the sampled param space.
        let stochasticVerifies =
            let rep = verifyFunction w (mkSound Random) reg genParams seed iterations

            if rep.Verified then
                None
            else
                let why =
                    match rep.Counterexample with
                    | Some cx -> renderCounterexample w cx
                    | None -> "(no counterexample)"

                Some(sprintf "seed=%d: a stochastic-effect sound function failed structural verification — %s" seed why)

        // the structural verdict is effect-class-agnostic: sound verifies under every axis; broken
        // verifies under none. Verify keys on structure, not the determinism class.
        let effectAgnostic =
            let soundVerdicts =
                axes
                |> List.map (fun d -> (verifyFunction w (mkSound d) reg genParams seed iterations).Verified)

            let brokenVerdicts =
                axes
                |> List.map (fun d -> (verifyFunction w (mkBroken d) reg genParams seed iterations).Verified)

            if (soundVerdicts |> List.forall id) && (brokenVerdicts |> List.forall not) then
                None
            else
                Some(
                    sprintf
                        "seed=%d: the structural verdict varied with the effect class (sound=%A broken=%A)"
                        seed
                        soundVerdicts
                        brokenVerdicts
                )

        [ { Law = "a stochastic-effect function verifies for structural validity across its param space"
            Passed = stochasticVerifies.IsNone
            Counterexample = stochasticVerifies }
          { Law = "verification makes no output-determinism/quality claim (structural verdict is effect-class-agnostic)"
            Passed = effectAgnostic.IsNone
            Counterexample = effectAgnostic } ]

    // ---- memo soundness: the audited-effect gate (Phase 53) ----
    // The teeth on `Function.applyMemo`'s Phase-53 change — memoisation keys on the OBSERVED (walked)
    // effect (`Function.observedEffect`), not the declared root, so a function whose root under-declares
    // an impure descendant can never be cached and then served stale.

    /// The memo-soundness laws (Phase 53). A domain supplies the witness `w`, the canonical encoder
    /// `encode`, and an UNDER-DECLARED function `underDeclaredFn` (+ a valid full param-set
    /// `underDeclaredArgs`): its declared ROOT effect is pure & deterministic, but a descendant observes
    /// an impure effect (the `auditEffect` leak case). Over a seed-replayable run the kit certifies:
    ///
    ///  - **the gate keys on the observed effect, not the declared root** — the fixture's declared root
    ///    IS memoisable yet its observed (walked) effect is NOT, so the pre-Phase-53 declared-root check
    ///    would have wrongly cached it while the audited gate does not;
    ///  - **an under-declared-impure function is bypassed** — `applyMemo` computes it directly
    ///    (byte-identical to `apply`), stores nothing, and never serves it on re-apply (the soundness
    ///    guard against a stale cached result).
    let memoSoundnessLaws
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (underDeclaredFn: 'Node)
        (underDeclaredArgs: Map<string, Arg<'Node>>)
        (seed: int)
        (_iterations: int)
        : LawResult list =
        // the gate distinction: the declared root is memoisable, the observed (walked) effect is not.
        let declaredMemoisable = Memo.isMemoisable (w.Effect underDeclaredFn)

        let observedMemoisable =
            Memo.isMemoisable (Function.observedEffect w underDeclaredFn)

        let gateLaw =
            if declaredMemoisable && not observedMemoisable then
                None
            else
                Some(
                    sprintf
                        "seed=%d: fixture is not a genuine under-declared case (declaredMemoisable=%b observedMemoisable=%b)"
                        seed
                        declaredMemoisable
                        observedMemoisable
                )

        // bypass behaviour: apply = applyMemo, nothing cached, never served on re-apply.
        let bypassLaw =
            match
                Function.apply w underDeclaredArgs underDeclaredFn,
                Function.applyMemo w encode underDeclaredArgs underDeclaredFn Memo.empty
            with
            | Ok direct, Ok(m1, c1) ->
                match Function.applyMemo w encode underDeclaredArgs underDeclaredFn c1 with
                | Ok(m2, c2) ->
                    if
                        m1 <> direct
                        || m2 <> direct
                        || not (Map.isEmpty c1.Entries)
                        || not (Map.isEmpty c2.Entries)
                        || c1.Hits <> 0
                        || c2.Hits <> 0
                        || c1.Bypasses <> 1
                        || c2.Bypasses <> 2
                    then
                        Some(
                            sprintf
                                "seed=%d: under-declared fn not bypassed (entries=%d/%d hits=%d/%d bypasses=%d/%d)"
                                seed
                                (Map.count c1.Entries)
                                (Map.count c2.Entries)
                                c1.Hits
                                c2.Hits
                                c1.Bypasses
                                c2.Bypasses
                        )
                    else
                        None
                | Error e -> Some(sprintf "seed=%d: re-apply of the under-declared fn errored: %A" seed e)
            | other ->
                Some(sprintf "seed=%d: apply / applyMemo of the under-declared fn disagreed or errored: %A" seed other)

        [ { Law =
              "applyMemo gates on the observed effect, not the declared root (an under-declared root would wrongly memoise)"
            Passed = gateLaw.IsNone
            Counterexample = gateLaw }
          { Law = "an under-declared-impure function is bypassed (never cached, never served from cache)"
            Passed = bypassLaw.IsNone
            Counterexample = bypassLaw } ]
