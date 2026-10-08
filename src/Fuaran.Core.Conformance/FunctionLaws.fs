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
        let nested = LawKit.LawCell "apply ∘ composeAcross = the nested application"

        let associative =
            LawKit.LawCell "composeAcross is associative where typed (disjoint slots commute)"

        let hygiene =
            LawKit.LawCell "hygiene holds under re-binding (no cross-slot capture)"

        let effectJoin =
            LawKit.LawCell "composed effect = componentwise join of the parts' (Fork 3)"

        LawKit.run iterations seed (fun rng _ at ->
            let s = rng.Draw draw

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

            nested.Check(
                (viaCompose = viaApply),
                fun () -> at (sprintf "apply∘composeAcross ≠ nested application (%A vs %A)" viaCompose viaApply)
            )

            // ---- associative where typed (disjoint-slot composition is order-independent) ----
            let orderAB =
                Function.composeAcross wa wb embed s.SlotA s.ClosedInner s.Outer
                |> Result.bind (Function.composeAcross wa wb embed s.SlotB s.ClosedInner)

            let orderBA =
                Function.composeAcross wa wb embed s.SlotB s.ClosedInner s.Outer
                |> Result.bind (Function.composeAcross wa wb embed s.SlotA s.ClosedInner)

            associative.Check((orderAB = orderBA), fun () -> at "disjoint-slot composeAcross is not order-independent")

            // ---- hygiene holds under re-binding ----
            // Wire two same-named (distinct-id) inner holes into the two slots; each re-roots under
            // its slot's absolute address, so the composed function exposes two distinct copies.
            // Binding one must leave the other open (Fork 2 — no capture).
            let twoCopies =
                Function.composeAcross wa wb embed s.SlotA s.OpenInnerA s.Outer
                |> Result.bind (Function.composeAcross wa wb embed s.SlotB s.OpenInnerB)

            (match twoCopies with
             | Error e ->
                 hygiene.Check(false, fun () -> at (sprintf "composing open inners into both slots failed: %A" e))
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

                         hygiene.Check(
                             not (after.Contains a1 || not (after.Contains a2)),
                             fun () -> at (sprintf "re-binding %s captured the other copy %s" a1 a2)
                         )
                     | Error e -> hygiene.Check(false, fun () -> at (sprintf "binding one copy failed: %A" e))
                 | other ->
                     hygiene.Check(
                         false,
                         fun () -> at (sprintf "expected two distinct re-rooted copies, got %A" other)
                     ))

            // ---- effect signature joins componentwise across the boundary (Fork 3) ----
            let joined = Function.composedEffectAcross wa wb s.OpenInnerA s.Outer
            let expected = Effect.join (wa.Effect s.Outer) (wb.Effect s.OpenInnerA)

            effectJoin.Check(
                not (
                    joined <> expected
                    || not (Effect.covers joined (wa.Effect s.Outer))
                    || not (Effect.covers joined (wb.Effect s.OpenInnerA))
                ),
                fun () -> at "composed effect ≠ join of parts (Fork 3)"
            ))

        LawKit.results [ nested; associative; hygiene; effectJoin ]

    /// Apply one param-set, run the domain validator over the result, and effect-audit it — the
    /// per-case oracle shared by `verifyFunction` and `verifyFunctionSymbolic`. Returns the first
    /// defect found, else `None`. Total: `apply` is total, `Validator.runAll` walks a pure registry,
    /// and `auditEffect` returns a typed `Result` — no stage throws.
    let private verifyCase
        (w: ArtifactWitness<'Node, 'Id>)
        (reg: Validator.RuleRegistry<'Node, 'Id>)
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

    /// Property-verify an artifact-function against a domain `Validator.RuleRegistry` over a *supplied*
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
        (reg: Validator.RuleRegistry<'Node, 'Id>)
        (genParams: 'Node -> ConfRng.T -> Map<string, Arg<'Node>> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : FunctionVerifyReport<'Node, 'Id> =
        // A VERIFIER, not a law family: it stops at the first counterexample, which it returns as
        // a typed report rather than as a law. It draws through the runner's cursor all the same.
        let rng = LawKit.Draws seed
        let mutable counterexample = None
        let mutable i = 0

        while counterexample.IsNone && i < iterations do
            let pset = rng.Draw(genParams fn)
            counterexample <- verifyCase w reg fn seed i pset
            i <- i + 1

        { Verified = counterexample.IsNone
          Coverage = Sampled(i, None)
          Counterexample = counterexample }

    /// The per-hole symbolic domain (Phase 48): a finite candidate list (enumerable), or a sampler
    /// for a large / unbounded space, carrying the space size where it is bounded-but-large. The
    /// size is an `int64` (Phase 297): a full-width `IntRange` holds up to 2^32 values, which no
    /// `int` carries.
    type private HoleDomain =
        | FiniteDom of string list
        | SampledDom of sampler: (ConfRng.T -> string * ConfRng.T) * size: int64 option

    /// Project a value-space into a symbolic domain. `Enum` / a small `IntRange` enumerate; a large
    /// `IntRange` samples uniformly in-range (carrying its finite size); the genuinely-large /
    /// unbounded spaces (`FloatRange` / `StringLen` / `AnyString`) sample with no finite size. Sample
    /// strings use culture-invariant `string` / `sprintf "%f"` (Fable-clean, no `CultureInfo`).
    let private domainOf (maxCases: int) (space: ValueSpace) : HoleDomain =
        match space with
        | Enum xs -> FiniteDom xs
        | IntRange(lo, hi) ->
            // Sized in `int64`: `hi - lo + 1` wrapped for `IntRange(0, Int32.MaxValue)` and reported
            // the space as empty, so every case was a false `DidNotApply` (Phase 297).
            let n = int64 hi - int64 lo + 1L

            if n <= 0L then
                FiniteDom []
            elif n <= int64 maxCases then
                FiniteDom [ for v in lo..hi -> string v ]
            elif n <= int64 System.Int32.MaxValue then
                SampledDom(
                    (fun r ->
                        let k, r' = ConfRng.intBelow (int n) r
                        string (lo + k), r'),
                    Some n
                )
            else
                // A range wider than `intBelow` reaches (2^31 .. 2^32 values): two draws — a
                // quarter-width offset and a two-bit residue — rejected past the end, so the
                // value stays exactly uniform in range and the offset never leaves `int`.
                let quarter = int ((n + 3L) / 4L)

                SampledDom(
                    (fun r ->
                        let mutable rng = r
                        let mutable candidate = n

                        while candidate >= n do
                            let q, r1 = ConfRng.intBelow quarter rng
                            let b, r2 = ConfRng.intBelow 4 r1
                            rng <- r2
                            candidate <- 4L * int64 q + int64 b

                        string (int (int64 lo + candidate)), rng),
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
    /// when their combined finite space is small (≤ `maxCases`) and **sampled** (up to `maxCases` draws)
    /// when it is large or unbounded, with the coverage reported either way (never silently
    /// sample-and-claim-verified). Slot holes — and any value hole the caller pins in `fixedArgs` —
    /// are held constant while the remaining value holes vary; `fixedArgs` must cover every slot hole
    /// (strict `apply` demands it). Each case is applied, validated against `reg`, and effect-audited;
    /// the first failure is a reproducible counterexample and stops the run, and the coverage counts
    /// the cases evaluated up to it — an enumeration stopped short of its last case is `Sampled`,
    /// not `Exhaustive` (Phase 348). `'Node` is unconstrained.
    let verifyFunctionSymbolic
        (w: ArtifactWitness<'Node, 'Id>)
        (fn: 'Node)
        (reg: Validator.RuleRegistry<'Node, 'Id>)
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

        // The finite product (meaningful only when sizeKnown), SATURATED at `ceiling` (Phase 297):
        // multiplied in `int64`, and clamped to the ceiling the moment it is reached, so that no
        // product can wrap. Before this phase the product was an unchecked `int` — four holes of
        // 1,000 wrapped negative, passed `<= maxCases`, and materialised the full cartesian product.
        // A zero-sized hole still zeroes the product (an empty space is empty however wide the
        // rest), and the multiply is guarded by division so a saturated `acc` times a 2^32-wide
        // hole never leaves `int64`.
        let productUpTo (ceiling: int64) : int64 =
            (1L, valueHoles)
            ||> List.fold (fun acc (_, d) ->
                let size =
                    match d with
                    | FiniteDom xs -> int64 (List.length xs)
                    | SampledDom(_, Some n) -> n
                    | SampledDom(_, None) -> 1L

                if size = 0L then 0L
                elif acc > (ceiling - 1L) / size then ceiling
                else acc * size)

        // The decision product saturates at `maxCases + 1`: reaching the ceiling means "more than
        // maxCases", which is all the enumerable-vs-sampled decision reads. A negative `maxCases`
        // is clamped to zero here so the product stays non-negative and such a call falls to the
        // sampled branch with no draws, as it always did.
        let spaceSize = productUpTo (int64 (max 0 maxCases) + 1L)

        // The REPORTED size saturates one past `Int32.MaxValue`: a true product that fits an `int`
        // is reported exactly, and one that does not is reported `None` — unknown / too large for
        // the `int option` the report carries — rather than a wrapped or clamped number.
        let reportedSize: int option =
            let intCeiling = int64 System.Int32.MaxValue + 1L
            let p = productUpTo intCeiling
            if sizeKnown && p < intCeiling then Some(int p) else None

        let psetOf (valueArgs: (string * string) list) : Map<string, Arg<'Node>> =
            (fixedArgs, valueArgs) ||> List.fold (fun m (a, v) -> Map.add a (ValueArg v) m)

        if enumerable && spaceSize <= int64 maxCases then
            // exhaustive — enumerate the cartesian product of the per-hole candidate lists, LAZILY
            // (Phase 297): a sequence taken up to `maxCases`, never a materialised list.
            let lists =
                valueHoles
                |> List.map (fun (a, d) ->
                    match d with
                    | FiniteDom xs -> a, xs
                    | SampledDom _ -> a, []) // unreachable under `enumerable`

            let rec cartesian (ls: (string * string list) list) : seq<(string * string) list> =
                match ls with
                | [] -> Seq.singleton []
                | (addr, vals) :: rest ->
                    seq {
                        for v in vals do
                            for t in cartesian rest -> (addr, v) :: t
                    }

            let cx =
                cartesian lists
                |> Seq.truncate maxCases
                |> Seq.indexed
                |> Seq.tryPick (fun (i, combo) -> verifyCase w reg fn seed i (psetOf combo))

            // Under `enumerable` every hole is finite and the product is below the ceiling, so
            // `spaceSize` is the exact case count the sequence enumerates. A counterexample stops
            // the enumeration at its index (Phase 348): the report states the cases evaluated, and
            // claims `Exhaustive` only when that is every case — a stop at the last one included.
            let evaluated =
                match cx with
                | Some c -> c.Iteration + 1
                | None -> int spaceSize

            { Verified = cx.IsNone
              Coverage =
                if int64 evaluated = spaceSize then
                    Exhaustive evaluated
                else
                    Sampled(evaluated, Some(int spaceSize))
              Counterexample = cx }
        else
            // sampled — draw `maxCases` param-sets, each varying hole drawn from its domain.
            let rng = LawKit.Draws seed
            let mutable cx = None
            let mutable i = 0

            while cx.IsNone && i < maxCases do
                let valueArgs =
                    valueHoles
                    |> List.map (fun (a, d) ->
                        match d with
                        | FiniteDom [] -> a, "" // an unsatisfiable hole — apply rejects (kept total)
                        | FiniteDom xs -> a, rng.Choose xs
                        | SampledDom(sampler, _) -> a, rng.Draw sampler)

                cx <- verifyCase w reg fn seed i (psetOf valueArgs)
                i <- i + 1

            // `i` is the count drawn: `maxCases` (none for a negative one) unless a counterexample
            // stopped the run first (Phase 348 — it reported the planned `maxCases` before).
            { Verified = cx.IsNone
              Coverage = Sampled(i, reportedSize)
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

        LawKit.failAt cx.Seed cx.Iteration (sprintf "{ %s } ⇒ %s" pset defect)

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
        (reg: Validator.RuleRegistry<'Node, 'Id>)
        (genParams: 'Node -> ConfRng.T -> Map<string, Arg<'Node>> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let soundLaw =
            LawKit.LawCell "a sound artifact-function verifies clean across its param space"

        let brokenLaw =
            LawKit.LawCell "a broken artifact-function fails with a readable (param-set, defect) counterexample"

        let detLaw =
            LawKit.LawCell "verification is deterministic (same seed ⇒ identical report)"

        // Each report is one verification run over the whole sample, so each law is asserted once,
        // over that run — the evidence is the run having been made, not an iteration.
        let soundReport = verifyFunction w sound reg genParams seed iterations
        let brokenReport = verifyFunction w broken reg genParams seed iterations
        let brokenAgain = verifyFunction w broken reg genParams seed iterations

        (match soundReport.Verified, soundReport.Counterexample with
         | false, Some cx ->
             soundLaw.Check(
                 false,
                 fun () -> sprintf "seed=%d: a sound function failed verification — %s" seed (renderCounterexample w cx)
             )
         | _ -> soundLaw.Saw())

        (match brokenReport.Verified, brokenReport.Counterexample with
         | false, Some cx ->
             brokenLaw.Check(
                 not (System.String.IsNullOrWhiteSpace(renderCounterexample w cx)),
                 fun () -> sprintf "seed=%d: the broken function's counterexample was empty (not readable)" seed
             )
         | _ ->
             brokenLaw.Check(
                 false,
                 fun () -> sprintf "seed=%d: a broken function verified clean (no counterexample surfaced)" seed
             ))

        detLaw.Check(
            (brokenReport = brokenAgain),
            fun () -> sprintf "seed=%d: verification was not deterministic (same seed ⇒ different report)" seed
        )

        LawKit.results [ soundLaw; brokenLaw; detLaw ]

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
    ///    replay collapses into re-application over the same memo);
    ///  - **a value spelling the separator and the next binding misses** (Phase 290) — BUILT from the
    ///    drawn param-set: `{a = "X\u0001b=vY"}` against the cached `{a = "X"; b = "Y"}` must answer
    ///    exactly what `apply` answers and must not be a hit, so the memo key is known to be injective
    ///    in the arguments rather than a bare join a value can forge.
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
        let equalsDirect =
            LawKit.LawCell "a memoised apply equals the direct apply (a re-apply is a cache hit)"

        let paramMiss =
            LawKit.LawCell "a changed param-set misses (the original still re-hits)"

        let effectingBypassed =
            LawKit.LawCell "an effecting function is never served from cache (bypassed — soundness, Fork 3)"

        let replayParity =
            LawKit.LawCell "replay-as-re-application matches direct replay (a repeat served from cache)"

        let separatorMiss =
            LawKit.LawCell "a value spelling the separator and the next binding misses (the memo key is injective)"

        LawKit.run iterations seed (fun rng _ at ->
            let s = rng.Draw draw

            // ---- 5. (Phase 290) a value spelling the separator and the next binding MISSES ----
            // BUILT, never drawn: take the drawn param-set, and where its first two addresses in
            // key order are `a` and `b`, replace the pair with ONE binding `a = "<a's value>" +
            // U+0001 + "b=" + <b's bare tag>` — the value that spelt `{a = "X"; b = "Y"}`'s whole
            // pre-image under the bare join the key used until Phase 290 (`{a = "X\u0001b=vY"}`).
            // The cache holds the drawn set's result; the built set must not be served it. What
            // is asked is stronger than "a miss": `applyMemo` must answer exactly what `apply`
            // answers for the built set — here a refusal, since `b` is a declared hole left
            // unbound — and the hit count must not move. Only a `ValueArg` at `a` can carry the
            // spelling, so a sample whose first address is a slot reports nothing for this arm.
            (match s.Args |> Map.toList |> List.sortBy fst |> List.truncate 2 with
             | [ (a, ValueArg va); (b, bArg) ] ->
                 // `b`'s fields as the key spells them — the case tag and the payload a slot arg
                 // rides as (`Tree.encodePreimage`) — so the forged value is exactly what a bare
                 // join of the CURRENT fields would run into, not an approximation of one.
                 let bareTag =
                     match bArg with
                     | ValueArg vb -> "v" + vb
                     | SlotArg n -> "s" + Tree.encodePreimage w.Tree encode n

                 let built =
                     s.Args
                     |> Map.remove b
                     |> Map.add a (ValueArg(va + Hash.foldSep + b + "=" + bareTag))

                 match Function.applyMemo w encode s.Args s.PureFn Memo.empty with
                 | Ok(_, c1) ->
                     let direct = Function.apply w built s.PureFn
                     let memo = Function.applyMemo w encode built s.PureFn c1

                     let served =
                         match memo with
                         | Ok(t, c2) -> c2.Hits <> c1.Hits || direct <> Ok t
                         | Error e -> direct <> Error e

                     separatorMiss.Check(
                         not served,
                         fun () ->
                             at (
                                 sprintf
                                     "a value spelling U+0001 and the next binding (%s = %A) was served the cached result of the two-binding set — the memo key is not injective"
                                     a
                                     (va + Hash.foldSep + b + "=" + bareTag)
                             )
                     )
                 | Error e -> separatorMiss.Check(false, fun () -> at (sprintf "first apply errored: %A" e))
             | _ -> ())

            // ---- 1. a memoised apply equals the direct apply (pure fn); a re-apply is a hit ----
            (match Function.apply w s.Args s.PureFn, Function.applyMemo w encode s.Args s.PureFn Memo.empty with
             | Ok direct, Ok(memo1, c1) ->
                 match Function.applyMemo w encode s.Args s.PureFn c1 with
                 | Ok(memo2, c2) ->
                     equalsDirect.Check(
                         not (
                             memo1 <> direct
                             || memo2 <> direct
                             || c1.Misses <> 1
                             || c1.Hits <> 0
                             || c2.Hits <> 1
                         ),
                         fun () -> at "memoised apply ≠ direct apply / re-apply was not a hit"
                     )
                 | Error e -> equalsDirect.Check(false, fun () -> at (sprintf "a re-apply errored: %A" e))
             | other ->
                 equalsDirect.Check(false, fun () -> at (sprintf "apply / applyMemo disagreed or errored: %A" other)))

            // ---- 2. a changed param-set misses; the original still re-hits ----
            (match Function.applyMemo w encode s.Args s.PureFn Memo.empty with
             | Ok(_, c1) ->
                 match Function.applyMemo w encode s.ArgsAlt s.PureFn c1 with
                 | Ok(_, c2) ->
                     match Function.applyMemo w encode s.Args s.PureFn c2 with
                     | Ok(_, c3) ->
                         paramMiss.Check(
                             not (c2.Misses <> 2 || c2.Hits <> 0 || c3.Hits <> 1),
                             fun () ->
                                 at (
                                     sprintf
                                         "a changed param-set did not miss / the original did not re-hit (misses=%d hits=%d→%d)"
                                         c2.Misses
                                         c2.Hits
                                         c3.Hits
                                 )
                         )
                     | Error e ->
                         paramMiss.Check(false, fun () -> at (sprintf "re-apply of the original errored: %A" e))
                 | Error e ->
                     paramMiss.Check(false, fun () -> at (sprintf "apply of the alternate param-set errored: %A" e))
             | Error e -> paramMiss.Check(false, fun () -> at (sprintf "first apply errored: %A" e)))

            // ---- 3. an effecting function is never served from (or stored in) the cache ----
            (match
                Function.apply w s.EffectingArgs s.EffectingFn,
                Function.applyMemo w encode s.EffectingArgs s.EffectingFn Memo.empty
             with
             | Ok direct, Ok(m1, c1) ->
                 match Function.applyMemo w encode s.EffectingArgs s.EffectingFn c1 with
                 | Ok(m2, c2) ->
                     effectingBypassed.Check(
                         not (
                             m1 <> direct
                             || m2 <> direct
                             || not (Map.isEmpty c1.Entries)
                             || c1.Hits <> 0
                             || c1.Bypasses <> 1
                             || c2.Hits <> 0
                             || not (Map.isEmpty c2.Entries)
                         ),
                         fun () -> at "an effecting function was cached or served from cache"
                     )
                 | Error e ->
                     effectingBypassed.Check(
                         false,
                         fun () -> at (sprintf "a re-apply of the effecting fn errored: %A" e)
                     )
             | other ->
                 effectingBypassed.Check(
                     false,
                     fun () -> at (sprintf "apply / applyMemo of the effecting fn disagreed or errored: %A" other)
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
                     replayParity.Check(
                         not (mNode <> pNode || mCache.Hits < 1),
                         fun () ->
                             at (
                                 sprintf
                                     "memo replay ≠ direct replay or the repeated op was not served from cache (hits=%d)"
                                     mCache.Hits
                             )
                     )
                 | other -> replayParity.Check(false, fun () -> at (sprintf "a replay errored: %A" other))
             | Error e -> replayParity.Check(false, fun () -> at (sprintf "building the record session errored: %A" e))))

        LawKit.results [ equalsDirect; paramMiss; effectingBypassed; replayParity; separatorMiss ]

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
        let subMemo =
            LawKit.LawCell "a pure cross-witness sub-function memoises (miss then hit on re-apply)"

        let composedMemo =
            LawKit.LawCell "applyMemo over the cross-witness-composed function equals direct apply (re-apply is a hit)"

        // The cursor is seeded from `seed + 101` (the independent stream); the counterexamples
        // are stamped with the caller's `seed`, as they always were, so the runner's own stamp
        // is set aside for one built over `seed`.
        LawKit.run iterations (seed + 101) (fun rng i _ ->
            let at = LawKit.failAt seed i
            let s = rng.Draw draw

            // ---- a pure cross-witness sub-function memoises (miss then hit) ----
            (match Function.applyMemo wb encodeB Map.empty s.ClosedInner Memo.empty with
             | Ok(m1, c1) ->
                 match Function.applyMemo wb encodeB Map.empty s.ClosedInner c1 with
                 | Ok(m2, c2) ->
                     subMemo.Check(
                         not (m1 <> m2 || c1.Misses <> 1 || c1.Hits <> 0 || c2.Hits <> 1),
                         fun () -> at "a pure cross-witness sub-function did not memoise (miss then hit)"
                     )
                 | Error e -> subMemo.Check(false, fun () -> at (sprintf "sub-function re-apply errored: %A" e))
             | Error e -> subMemo.Check(false, fun () -> at (sprintf "sub-function apply errored: %A" e)))

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
                         composedMemo.Check(
                             not (mm1 <> direct || mm2 <> direct || c1.Misses <> 1 || c2.Hits <> 1),
                             fun () -> at "applyMemo over the composed function ≠ direct apply / no re-apply hit"
                         )
                     | Error e -> composedMemo.Check(false, fun () -> at (sprintf "composed re-apply errored: %A" e))
                 | other ->
                     composedMemo.Check(
                         false,
                         fun () -> at (sprintf "apply / applyMemo over the composed fn disagreed: %A" other)
                     )
             | Error e ->
                 composedMemo.Check(false, fun () -> at (sprintf "composeAcross into both slots failed: %A" e))))

        composition @ LawKit.results [ subMemo; composedMemo ]

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
    ///    effect-class-AGNOSTIC: every determinism set (`Deterministic`, each of `Clock` / `Random` /
    ///    `Network`, and every combination of them) yields the SAME verdict — all verify for the sound
    ///    function, none verify for the broken one — so "verified" certifies structure, never the
    ///    determinism class of the effect.
    ///
    /// This is the executable form of the contract clarification in the `verifyFunction` doc +
    /// `STABILITY.md`: the capability never over-claims a quality / determinism guarantee. Deterministic
    /// — the same seed reproduces the verdict.
    let verifyHonestyLaws
        (w: ArtifactWitness<'Node, 'Id>)
        (mkSound: DeterminismSource -> 'Node)
        (mkBroken: DeterminismSource -> 'Node)
        (reg: Validator.RuleRegistry<'Node, 'Id>)
        (genParams: 'Node -> ConfRng.T -> Map<string, Arg<'Node>> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        // Every determinism set over the three factors — the empty set (`Deterministic`), each factor
        // alone, and every combination — because the axis is a set since Phase 319.
        let axes: DeterminismSource list =
            [ for mask in 0..7 ->
                  [ ClockFactor; RandomFactor; NetworkFactor ]
                  |> List.indexed
                  |> List.filter (fun (k, _) -> (mask >>> k) &&& 1 = 1)
                  |> List.map snd
                  |> Set.ofList ]

        let stochasticVerifies =
            LawKit.LawCell "a stochastic-effect function verifies for structural validity across its param space"

        let effectAgnostic =
            LawKit.LawCell
                "verification makes no output-determinism/quality claim (structural verdict is effect-class-agnostic)"

        // Each law is asserted once, over a whole verification run (or a run per axis) — the
        // evidence is the run having been made, not an iteration.

        // a stochastic (Random) sound function verifies on structure across the sampled param space.
        (let rep = verifyFunction w (mkSound Effect.random) reg genParams seed iterations

         stochasticVerifies.Check(
             rep.Verified,
             fun () ->
                 let why =
                     match rep.Counterexample with
                     | Some cx -> renderCounterexample w cx
                     | None -> "(no counterexample)"

                 sprintf "seed=%d: a stochastic-effect sound function failed structural verification — %s" seed why
         ))

        // the structural verdict is effect-class-agnostic: sound verifies under every axis; broken
        // verifies under none. Verify keys on structure, not the determinism class.
        (let soundVerdicts =
            axes
            |> List.map (fun d -> (verifyFunction w (mkSound d) reg genParams seed iterations).Verified)

         let brokenVerdicts =
             axes
             |> List.map (fun d -> (verifyFunction w (mkBroken d) reg genParams seed iterations).Verified)

         effectAgnostic.Check(
             (soundVerdicts |> List.forall id) && (brokenVerdicts |> List.forall not),
             fun () ->
                 sprintf
                     "seed=%d: the structural verdict varied with the effect class (sound=%A broken=%A)"
                     seed
                     soundVerdicts
                     brokenVerdicts
         ))

        LawKit.results [ stochasticVerifies; effectAgnostic ]

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
    ///
    /// The gate distinction is a property of the fixture and is asserted once; the bypass is asserted
    /// `iterations` times, each over a fresh cache (the family draws nothing, so the count is the
    /// number of applications, not a sample size — `iterations = 0` leaves the bypass law never
    /// reached, which is what it reports).
    let memoSoundnessLaws
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (underDeclaredFn: 'Node)
        (underDeclaredArgs: Map<string, Arg<'Node>>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let gateLaw =
            LawKit.LawCell
                "applyMemo gates on the observed effect, not the declared root (an under-declared root would wrongly memoise)"

        let bypassLaw =
            LawKit.LawCell "an under-declared-impure function is bypassed (never cached, never served from cache)"

        // the gate distinction: the declared root is memoisable, the observed (walked) effect is not.
        let declaredMemoisable = Memo.isMemoisable (w.Effect underDeclaredFn)

        let observedMemoisable =
            Memo.isMemoisable (Function.observedEffect w underDeclaredFn)

        gateLaw.Check(
            declaredMemoisable && not observedMemoisable,
            fun () ->
                sprintf
                    "seed=%d: fixture is not a genuine under-declared case (declaredMemoisable=%b observedMemoisable=%b)"
                    seed
                    declaredMemoisable
                    observedMemoisable
        )

        // bypass behaviour: apply = applyMemo, nothing cached, never served on re-apply. Nothing is
        // drawn, so the counterexample carries the seed alone, as it always did.
        LawKit.run iterations seed (fun _ _ _ ->
            match
                Function.apply w underDeclaredArgs underDeclaredFn,
                Function.applyMemo w encode underDeclaredArgs underDeclaredFn Memo.empty
            with
            | Ok direct, Ok(m1, c1) ->
                match Function.applyMemo w encode underDeclaredArgs underDeclaredFn c1 with
                | Ok(m2, c2) ->
                    bypassLaw.Check(
                        not (
                            m1 <> direct
                            || m2 <> direct
                            || not (Map.isEmpty c1.Entries)
                            || not (Map.isEmpty c2.Entries)
                            || c1.Hits <> 0
                            || c2.Hits <> 0
                            || c1.Bypasses <> 1
                            || c2.Bypasses <> 2
                        ),
                        fun () ->
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
                | Error e ->
                    bypassLaw.Check(
                        false,
                        fun () -> sprintf "seed=%d: re-apply of the under-declared fn errored: %A" seed e
                    )
            | other ->
                bypassLaw.Check(
                    false,
                    fun () ->
                        sprintf
                            "seed=%d: apply / applyMemo of the under-declared fn disagreed or errored: %A"
                            seed
                            other
                ))

        LawKit.results [ gateLaw; bypassLaw ]
