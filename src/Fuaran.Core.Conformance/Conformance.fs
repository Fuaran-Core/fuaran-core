namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Conformance (Phase 243) — a property-based law kit a domain runs
//  against its own witness to certify it conforms to the Fuaran.Core op algebra
//  and op-stream. It generalises the `Core.Wire.Corpus` "methodology-is-the-asset"
//  posture from the wire codec to the op algebra: supply a generator, get a
//  verdict — instead of re-authoring a conformance suite per domain.
//
//  FSharp.Core only (a deterministic uint32 LCG, no FsCheck), Fable-clean.
//
//  ---- Out of conformance scope by design ----------------------------------
//  Certification proves **faithful carriage + integrity** — that a host's codec
//  round-trips values byte-identically, that its reducer is total and replays
//  deterministically, and that its chains/DAGs are tamper-evident. It deliberately
//  does NOT grade the following; each is a host- or domain-level concern the kit
//  leaves open on purpose, so "conformant" is a precise claim and not an implied
//  guarantee of quality:
//    - Rejection *quality* — WHETHER a domain's rejection usefully enumerates its
//      valid alternatives is an opt-in domain-supplied predicate (see `reducer`'s
//      `namesAlternatives` param), not a generic verdict the aggregate certifiers
//      compute (they cannot inspect a domain's own `'Rej` vocabulary).
//    - Attestation *policy* — WHEN to sign, key rotation, HSM/KMS choice. The kit
//      certifies the attestation *seam* (`attestationLaws`); the policy is host-side.
//    - Attribution *content* — WHETHER an `Actor` / `Session` id is truthful. The
//      chain proves attribution was not *tampered*; it never proves it was *honest*.
//    - Hash-strength *selection* — the collision-resistance of the supplied `HashFn`.
//      `hashFnLaws` certify parity + tamper-detection under ANY `HashFn`;
//      `hashFnAdversarialLaws` pin the posture, but the strong-crypto *choice* is
//      the host's (Core ships no cryptographic hash — GP3).
// ============================================================================

/// A deterministic, FSharp.Core-only RNG (uint32 LCG — same arithmetic class as the
/// portable FNV-1a hash, so it runs unchanged under Fable). Seed-replayable: the same
/// seed reproduces the same run, which is how a counterexample is reproduced.
module ConfRng =

    type T = { State: uint32 }

    let ofSeed (seed: int) : T =
        { State = (uint32 seed * 2654435761u) + 1u }

    /// A non-negative int and the advanced state.
    let next (r: T) : int * T =
        let s = (r.State * 1664525u) + 1013904223u
        int (s >>> 1), { State = s }

    /// The number of bits needed to represent `v` (0 for 0, 31 for `Int32.MaxValue`).
    let rec private bitWidth (acc: int) (v: int) : int =
        if v = 0 then acc else bitWidth (acc + 1) (v >>> 1)

    /// A value in `[0, n)` (0 when `n <= 0`).
    ///
    /// Drawn from the HIGH-ORDER bits by rejection, never `v % n`. The state is a linear
    /// congruential generator taken mod 2^32, in which bit `k` has period 2^(k+1) — bit 0
    /// alternates, bit 1 cycles every four draws — so reducing modulo a small `n` reads the
    /// weakest bits in the word. Worse than the short period itself, it is a short period
    /// *in phase*: generators drawn consecutively off one advancing stream then choose in
    /// lockstep rather than independently, which is invisible in a generator that consumes a
    /// whole word (a fresh id) and decisive in one that makes a handful of small choices per
    /// step. Taking the top `bitWidth (n - 1)` bits instead reads only full-period bits, and
    /// rejecting an out-of-range candidate leaves the result exactly uniform rather than
    /// modulo-biased. Acceptance is above one half, so fewer than two draws in expectation.
    ///
    /// Built from shifts and comparisons alone: no `uint64` (JavaScript cannot carry one
    /// exactly) and no 32-bit multiply (which does not wrap identically on both pipelines), so
    /// the kit stays value-identical under Fable. See `Hash.fs` on the same constraint.
    let intBelow (n: int) (r: T) : int * T =
        if n <= 0 then
            0, r
        elif n = 1 then
            // Still one draw, so a stream advances at the same rate whatever `n` is.
            let _, r' = next r
            0, r'
        else
            // `next` yields the top 31 bits of the state; this drops all but the top `bits`.
            let shift = 31 - bitWidth 0 (n - 1)
            let mutable rng = r
            let mutable candidate = n

            while candidate >= n do
                let v, r' = next rng
                rng <- r'
                candidate <- v >>> shift

            candidate, rng

    let choose (xs: 'a list) (r: T) : 'a * T =
        let i, r' = intBelow (List.length xs) r
        List.item i xs, r'

    /// Fisher–Yates shuffle.
    let shuffle (xs: 'a list) (r: T) : 'a list * T =
        let arr = List.toArray xs
        let mutable rng = r

        for i in (arr.Length - 1) .. -1 .. 1 do
            let j, r' = intBelow (i + 1) rng
            rng <- r'
            let tmp = arr.[i]
            arr.[i] <- arr.[j]
            arr.[j] <- tmp

        List.ofArray arr, rng

/// The domain-supplied op-algebra generator: how to build a random tree, and how to mint
/// a fresh node whose id avoids a given set (for `InsertChild`). `CanHold` (Phase 251) is
/// the container capability — `Some p` exercises the laws through `Ops.applyContained` so a
/// witness whose `ReplaceChildren` is partial on leaves certifies green without restricting
/// the generator to containers; `None` uses the plain `apply` (every node can hold children).
type OpGen<'Node, 'Id> =
    { Tree: ConfRng.T -> 'Node * ConfRng.T
      FreshNode: Set<string> -> ConfRng.T -> 'Node * ConfRng.T
      CanHold: ('Node -> bool) option }

/// The domain-supplied op-stream generator: the base state and a random op source.
type StreamGen<'Op, 'State> =
    { State0: 'State
      Op: ConfRng.T -> 'Op * ConfRng.T }

/// One law's verdict. `Counterexample` carries the seed + iteration so a failure is
/// reproducible (deterministic seed-replay).
type LawResult =
    { Law: string
      Passed: bool
      Counterexample: string option }

/// The aggregate certification report.
type ConformanceReport =
    { Results: LawResult list
      AllPassed: bool }

/// The law runners. A domain certifies its witness by supplying a generator; the kit
/// owns the laws. `'Node` / `'State` need equality (conformance compares trees / states).
module Conformance =

    /// Generate one (possibly-invalid) op against `tree` — the laws handle Ok and Error.
    let private genOp
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (tree: 'Node)
        (rng: ConfRng.T)
        : SkeletonOp<'Node, 'Id> * ConfRng.T =
        let ids = Tree.preorder nodew tree |> List.map nodew.Id
        let idKeys = ids |> List.map idw.ToString |> Set.ofList
        let kind, r1 = ConfRng.intBelow 4 rng

        match kind with
        | 0 ->
            let parent, r2 = ConfRng.choose ids r1
            let fresh, r3 = gen.FreshNode idKeys r2
            InsertChild(parent, fresh), r3
        | 1 ->
            let target, r2 = ConfRng.choose ids r1
            RemoveNode target, r2
        | 2 ->
            let target, r2 = ConfRng.choose ids r1
            let np, r3 = ConfRng.choose ids r2
            MoveNode(target, np), r3
        | _ ->
            let parent, r2 = ConfRng.choose ids r1

            match Tree.tryFind nodew idw parent tree with
            | Some p ->
                let kids = nodew.Children p |> List.map nodew.Id
                let shuffled, r3 = ConfRng.shuffle kids r2
                ReorderChildren(parent, shuffled), r3
            | None -> ReorderChildren(parent, []), r2

    /// The witness laws (Phase 253) — check the `NodeWitness`/`IdWitness` is well-formed
    /// *before* the algebra laws run, so a defect localises to the accessor that's wrong
    /// instead of surfacing as a downstream `apply ∘ invert` failure (the F1 lesson):
    /// `Children (ReplaceChildren n cs) = cs`, `Id`/`KindTag` preserved under rebuild, and
    /// the `IdWitness` round-trip + reflexivity. The `ReplaceChildren` laws are checked only
    /// on nodes that `CanHold` children (a leaf is not required to round-trip a child list).
    let witnessLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let mutable rng = ConfRng.ofSeed seed
        let mutable rcRoundTrip = None
        let mutable idPreserved = None
        let mutable kindStable = None
        let mutable idRoundTrip = None

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen.Tree rng
            rng <- r1

            match Tree.preorder nodew tree |> List.filter canHold with
            | [] -> ()
            | holders ->
                let n, r2 = ConfRng.choose holders rng
                rng <- r2
                let k, r3 = ConfRng.intBelow 3 rng
                rng <- r3
                // a candidate child list of fresh nodes (ids disjoint from the tree)
                let mutable cs = []
                let mutable seen = Tree.ids nodew tree |> List.map idw.ToString |> Set.ofList

                for _ in 1..k do
                    let fresh, r' = gen.FreshNode seen rng
                    rng <- r'
                    seen <- Set.add (idw.ToString(nodew.Id fresh)) seen
                    cs <- cs @ [ fresh ]

                let rebuilt = nodew.ReplaceChildren n cs

                if nodew.Children rebuilt <> cs && rcRoundTrip.IsNone then
                    rcRoundTrip <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: Children(ReplaceChildren n cs) ≠ cs for node %s (kind %s) — ReplaceChildren is not total"
                                seed
                                i
                                (idw.ToString(nodew.Id n))
                                (nodew.KindTag n)
                        )

                if not (idw.Equals (nodew.Id rebuilt) (nodew.Id n)) && idPreserved.IsNone then
                    idPreserved <- Some(sprintf "seed=%d iter=%d: Id changed under ReplaceChildren" seed i)

                if nodew.KindTag rebuilt <> nodew.KindTag n && kindStable.IsNone then
                    kindStable <- Some(sprintf "seed=%d iter=%d: KindTag changed under ReplaceChildren" seed i)

            for id in Tree.ids nodew tree do
                if not (idw.Equals id id) && idRoundTrip.IsNone then
                    idRoundTrip <- Some(sprintf "seed=%d iter=%d: Equals is not reflexive" seed i)
                elif not (idw.Equals (idw.OfString(idw.ToString id)) id) && idRoundTrip.IsNone then
                    idRoundTrip <-
                        Some(sprintf "seed=%d iter=%d: OfString∘ToString ≠ id for %s" seed i (idw.ToString id))

        [ { Law = "ReplaceChildren round-trip (Children(ReplaceChildren n cs) = cs)"
            Passed = rcRoundTrip.IsNone
            Counterexample = rcRoundTrip }
          { Law = "ReplaceChildren preserves Id"
            Passed = idPreserved.IsNone
            Counterexample = idPreserved }
          { Law = "ReplaceChildren preserves KindTag"
            Passed = kindStable.IsNone
            Counterexample = kindStable }
          { Law = "IdWitness round-trip + reflexivity"
            Passed = idRoundTrip.IsNone
            Counterexample = idRoundTrip } ]

    /// The op-algebra laws: apply totality (never throws), `canApply` ≡ `apply` (same
    /// accept/reject + envelope), and apply∘invert = identity on every applyable op.
    let opAlgebra
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let mutable rng = ConfRng.ofSeed seed
        let mutable totality = None
        let mutable equivalence = None
        let mutable inversion = None

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen.Tree rng
            let op, r2 = genOp nodew idw gen tree r1
            rng <- r2

            let applied =
                try
                    Some(Ops.applyContained canHold nodew idw op tree)
                with _ ->
                    None

            match applied with
            | None ->
                if totality.IsNone then
                    totality <- Some(sprintf "seed=%d iter=%d: apply threw on %A" seed i op)
            | Some res ->
                let chk = Ops.canApplyContained canHold nodew idw op tree

                let equiv =
                    match res, chk with
                    | Ok _, Ok() -> true
                    | Error e1, Error e2 -> e1 = e2
                    | _ -> false

                if not equiv && equivalence.IsNone then
                    equivalence <-
                        Some(sprintf "seed=%d iter=%d: canApply≠apply on %A (apply=%A canApply=%A)" seed i op res chk)

                match res with
                | Ok post ->
                    match Ops.invert nodew idw op tree with
                    | Error e ->
                        if inversion.IsNone then
                            inversion <-
                                Some(sprintf "seed=%d iter=%d: invert failed (%A) on an applyable %A" seed i e op)
                    | Ok inv ->
                        match Ops.applyContained canHold nodew idw inv post with
                        | Ok restored when restored = tree -> ()
                        | other ->
                            if inversion.IsNone then
                                inversion <-
                                    Some(
                                        sprintf "seed=%d iter=%d: apply∘invert≠identity on %A (got %A)" seed i op other
                                    )
                | Error _ -> ()

        [ { Law = "apply totality (never throws)"
            Passed = totality.IsNone
            Counterexample = totality }
          { Law = "canApply ≡ apply (accept/reject + envelope)"
            Passed = equivalence.IsNone
            Counterexample = equivalence }
          { Law = "apply ∘ invert = identity"
            Passed = inversion.IsNone
            Counterexample = inversion } ]

    /// The op-stream laws: `verifyChain` accepts an intact chain and rejects a tampered
    /// op; `replay` re-derives the live state from the base state.
    let streamLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable verify = None
        let mutable replay = None
        let mutable tamper = None

        for i in 0 .. iterations - 1 do
            let mutable state = gen.State0
            let mutable recs = OpStream.empty

            for _ in 0..5 do
                let op, r' = gen.Op rng
                rng <- r'

                match OpStream.append hashFn sw (Human "conf") op state recs with
                | Ok(s', recs') ->
                    state <- s'
                    recs <- recs'
                | Error _ -> () // a rejected op just doesn't extend the chain

            if not (OpStream.verifyChain hashFn sw recs) && verify.IsNone then
                verify <- Some(sprintf "seed=%d iter=%d: an intact chain failed verifyChain" seed i)

            match OpStream.replay sw gen.State0 recs with
            | Ok s when s = state -> ()
            | other ->
                if replay.IsNone then
                    replay <- Some(sprintf "seed=%d iter=%d: replay≠live state (got %A)" seed i other)

            match recs with
            | [] -> ()
            | _ ->
                let tIdx, r2 = ConfRng.intBelow (List.length recs) rng
                let newOp, r3 = gen.Op r2
                rng <- r3
                let orig = List.item tIdx recs

                // Only a genuinely-different op is a tamper the chain must detect.
                if sw.Encode orig.Op <> sw.Encode newOp then
                    let tampered =
                        recs |> List.mapi (fun j r -> if j = tIdx then { r with Op = newOp } else r)

                    if OpStream.verifyChain hashFn sw tampered && tamper.IsNone then
                        tamper <- Some(sprintf "seed=%d iter=%d: a tampered op was not detected" seed i)

        [ { Law = "verifyChain accepts an intact chain"
            Passed = verify.IsNone
            Counterexample = verify }
          { Law = "replay re-derives the live state"
            Passed = replay.IsNone
            Counterexample = replay }
          { Law = "verifyChain detects a tampered op"
            Passed = tamper.IsNone
            Counterexample = tamper } ]

    /// Domain-reducer laws (Phase 254) — certify a domain's *own* reducer
    /// `apply : 'Op -> 'State -> Result<'State, 'Rej>` (Doc's `DocOp` apply, Calc's, …), not
    /// just the tree witness. Checks **totality** (`apply` never throws — failures are `'Rej`)
    /// and **replay determinism** (re-applying the accepted ops from `State0` reproduces the
    /// threaded state). An optional `namesAlternatives : 'Rej -> bool` samples the envelope
    /// discipline ("every rejection enumerates the valid alternatives"). `'State` needs
    /// equality. The bridge from witness-level to production-apply conformance.
    let reducer
        (apply: 'Op -> 'State -> Result<'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (namesAlternatives: ('Rej -> bool) option)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable totality = None
        let mutable determinism = None
        let mutable envelope = None

        for i in 0 .. iterations - 1 do
            let mutable state = gen.State0
            let mutable accepted = []

            for _ in 0..5 do
                let op, r' = gen.Op rng
                rng <- r'

                let res =
                    try
                        Some(apply op state)
                    with _ ->
                        None

                match res with
                | None ->
                    if totality.IsNone then
                        totality <- Some(sprintf "seed=%d iter=%d: apply threw (not a typed rejection)" seed i)
                | Some(Ok st') ->
                    state <- st'
                    accepted <- accepted @ [ op ]
                | Some(Error rej) ->
                    match namesAlternatives with
                    | Some p when not (p rej) && envelope.IsNone ->
                        envelope <-
                            Some(sprintf "seed=%d iter=%d: a rejection did not enumerate its alternatives" seed i)
                    | _ -> ()

            // replay the accepted ops from State0 — must reproduce the live state
            let replayed =
                (Ok gen.State0, accepted)
                ||> List.fold (fun acc op -> acc |> Result.bind (fun s -> apply op s))

            match replayed with
            | Ok st when st = state -> ()
            | other ->
                if determinism.IsNone then
                    determinism <- Some(sprintf "seed=%d iter=%d: replay ≠ live state (got %A)" seed i other)

        [ { Law = "reducer totality (never throws)"
            Passed = totality.IsNone
            Counterexample = totality }
          { Law = "reducer replay determinism"
            Passed = determinism.IsNone
            Counterexample = determinism } ]
        @ (match namesAlternatives with
           | Some _ ->
               [ { Law = "rejection enumerates its alternatives"
                   Passed = envelope.IsNone
                   Counterexample = envelope } ]
           | None -> [])

    /// The structural-diff laws (Phase 03) — certify `Diff.toOps` against a domain's own
    /// witness. Build a random `before`, derive `after` by applying a random valid op sequence,
    /// then check: **reconstruction** (`applyAll (toOps before after) before = after`),
    /// **applyability** (`canApplyAll` accepts the emitted script — no step rejects), and
    /// **survivor preservation** (no `RemoveNode` targets an id present in both `before` and
    /// `after` — a relocated survivor diffs to `MoveNode`, never remove+insert). `'Node` needs
    /// equality. Homogeneous-tree domains fold this into `certify`; stream-only domains skip it.
    let diffLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let mutable rng = ConfRng.ofSeed seed
        let mutable reconstruction = None
        let mutable applyability = None
        let mutable survivor = None

        for i in 0 .. iterations - 1 do
            let before, r1 = gen.Tree rng
            rng <- r1

            // derive `after` by applying a few random valid ops (rejected ops are skipped)
            let mutable after = before

            for _ in 1..4 do
                let op, r' = genOp nodew idw gen after rng
                rng <- r'

                match Ops.applyContained canHold nodew idw op after with
                | Ok t' -> after <- t'
                | Error _ -> ()

            match Diff.toOps nodew idw before after with
            | Error e ->
                if reconstruction.IsNone then
                    reconstruction <-
                        Some(sprintf "seed=%d iter=%d: toOps errored on a valid before/after: %A" seed i e)
            | Ok ops ->
                match Ops.applyAll nodew idw ops before with
                | Ok rebuilt when rebuilt = after -> ()
                | other ->
                    if reconstruction.IsNone then
                        reconstruction <- Some(sprintf "seed=%d iter=%d: applyAll(toOps) ≠ after (got %A)" seed i other)

                match Ops.canApplyAll nodew idw ops before with
                | Ok() -> ()
                | Error(j, e) ->
                    if applyability.IsNone then
                        applyability <- Some(sprintf "seed=%d iter=%d: emitted op %d rejects: %A" seed i j e)

                let survivors =
                    Set.intersect
                        (Tree.ids nodew before |> List.map idw.ToString |> Set.ofList)
                        (Tree.ids nodew after |> List.map idw.ToString |> Set.ofList)

                let badRemove =
                    ops
                    |> List.tryPick (function
                        | RemoveNode t when survivors.Contains(idw.ToString t) -> Some t
                        | _ -> None)

                match badRemove with
                | Some t ->
                    if survivor.IsNone then
                        survivor <-
                            Some(sprintf "seed=%d iter=%d: RemoveNode targets a survivor %s" seed i (idw.ToString t))
                | None -> ()

        [ { Law = "diff reconstruction (applyAll(toOps before after) before = after)"
            Passed = reconstruction.IsNone
            Counterexample = reconstruction }
          { Law = "diff applyability (canApplyAll accepts the emitted script)"
            Passed = applyability.IsNone
            Counterexample = applyability }
          { Law = "diff survivor preservation (no RemoveNode on a survived id)"
            Passed = survivor.IsNone
            Counterexample = survivor } ]

    /// The op-script normalisation laws (Phase 23) — the teeth on `Ops.normalize`. Build a random
    /// *applyable* script (apply random ops, keep the accepted ones), then check: **preservation**
    /// (`applyAll (normalize ops) = applyAll ops` — the result is unchanged), **idempotence**
    /// (`normalize (normalize ops) = normalize ops`), and **non-growth** (normalisation never
    /// lengthens a script). `'Node` needs equality (it compares result trees + op scripts).
    let normalizeLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let mutable rng = ConfRng.ofSeed seed
        let mutable preservation = None
        let mutable idempotence = None
        let mutable nonGrowth = None

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen.Tree rng
            rng <- r1

            // collect an applyable script: thread random ops, keep the accepted ones
            let mutable cur = tree
            let mutable accepted = []

            for _ in 1..6 do
                let op, r' = genOp nodew idw gen cur rng
                rng <- r'

                match Ops.applyContained canHold nodew idw op cur with
                | Ok t' ->
                    cur <- t'
                    accepted <- accepted @ [ op ]
                | Error _ -> ()

            let normd = Ops.normalize nodew idw accepted

            if
                Ops.applyAll nodew idw normd tree <> Ops.applyAll nodew idw accepted tree
                && preservation.IsNone
            then
                preservation <- Some(sprintf "seed=%d iter=%d: applyAll(normalize ops) ≠ applyAll ops" seed i)

            if Ops.normalize nodew idw normd <> normd && idempotence.IsNone then
                idempotence <- Some(sprintf "seed=%d iter=%d: normalize is not idempotent" seed i)

            if List.length normd > List.length accepted && nonGrowth.IsNone then
                nonGrowth <- Some(sprintf "seed=%d iter=%d: normalize lengthened the script" seed i)

        [ { Law = "normalize preservation (applyAll(normalize ops) = applyAll ops)"
            Passed = preservation.IsNone
            Counterexample = preservation }
          { Law = "normalize idempotence (normalize ∘ normalize = normalize)"
            Passed = idempotence.IsNone
            Counterexample = idempotence }
          { Law = "normalize never lengthens a script"
            Passed = nonGrowth.IsNone
            Counterexample = nonGrowth } ]

    /// Certify a witness end-to-end: run the **witness laws first** (Phase 253), then — only
    /// if the witness is well-formed — the op-algebra laws + the diff laws (Phase 03) + the
    /// op-stream laws, and return a structured pass / counterexample report. A witness defect
    /// short-circuits the downstream laws (running them over a broken witness produces noise,
    /// not signal). Domains with only a tree (no stream) call `witnessLaws` + `opAlgebra`
    /// (+ `diffLaws`) directly.
    let certify
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (opGen: OpGen<'Node, 'Id>)
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (streamGen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : ConformanceReport =
        let witness = witnessLaws nodew idw opGen seed iterations

        let rest =
            if witness |> List.forall (fun r -> r.Passed) then
                opAlgebra nodew idw opGen (seed + 1) iterations
                @ diffLaws nodew idw opGen (seed + 3) iterations
                @ streamLaws sw streamGen hashFn (seed + 2) iterations
            else
                [] // witness defect — the downstream laws would be noise

        let results = witness @ rest

        { Results = results
          AllPassed = results |> List.forall (fun r -> r.Passed) }

    /// Certify a **reducer-only / heterogeneous-tree** domain — one with no single uniform node
    /// type for a `NodeWitness` (a layered `Model → Sheet → Region` tree, an op-stream with no
    /// addressable skeleton at all, …). This is the adoption surface for every domain whose op
    /// layer fits Core but whose tree does **not** (adoption finding F7): it runs the op-stream
    /// laws + the production-reducer laws over the *same* `StreamGen`, skipping the tree witness
    /// entirely. The reducer under test is the witness's own `Apply`, so a domain certifies its
    /// real `apply` (not a stand-in). `'State` needs equality. Domains with a homogeneous tree
    /// call `certify`; heterogeneous / stream-only domains call this.
    ///
    /// **Envelope-quality is deliberately not folded in.** The `reducer` laws below
    /// are invoked with `namesAlternatives = None`, so the "rejection enumerates its
    /// alternatives" law does not run through this aggregate. This is intentional, not
    /// an oversight: that law needs a *domain-supplied* predicate over the domain's own
    /// `'Rej` vocabulary — a semantic judgement the generic aggregate cannot make (it
    /// has no `'Rej` predicate to pass). It is the same opt-in shape as the snapshot /
    /// DAG laws below (not folded into `certify` / `certifyStream`; a domain calls them
    /// alongside its base run). A domain that wants to certify envelope quality calls
    /// `Conformance.reducer sw.Apply gen (Some myNamesAlternatives) seed iters` directly
    /// alongside `certifyStream`. See the header's "Out of conformance scope by design".
    let certifyStream
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (streamGen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : ConformanceReport =
        // Reducer laws first (they alone guard `apply` against throwing — Phase 254's totality
        // try/catch); only run the op-stream laws if the reducer is total, since `streamLaws`
        // drives the *unguarded* reducer through `append` and a non-total `apply` would crash it
        // rather than report. Same short-circuit philosophy as `certify`: foundational law first.
        let red = reducer sw.Apply streamGen None seed iterations

        let stream =
            if red |> List.forall (fun r -> r.Passed) then
                streamLaws sw streamGen hashFn (seed + 1) iterations
            else
                []

        let results = red @ stream

        { Results = results
          AllPassed = results |> List.forall (fun r -> r.Passed) }

    // ---- opt-in surfaces: snapshot / compaction + the op-DAG (Phase 07) ----
    // `streamLaws` exercises only the linear append/verify/replay path. These certify the two
    // op-stream surfaces a domain *opts into*: snapshot/compaction and the branching op-DAG.
    // They are NOT folded into `certify`/`certifyStream` (which would force a `stateEncode`
    // param and the DAG package on every adopter); a domain that uses snapshots or the DAG
    // calls them alongside its base certification.

    /// Snapshot / compaction laws under an explicit `StreamConfig` (Phase 14): **bounded replay**
    /// (`replayFrom` a compacted checkpoint equals `replay` from origin) and **verifyAcrossWith
    /// accepts an intact boundary** — the stream is built with `appendWith cfg` and the boundary
    /// verified with `verifyAcrossWith cfg`, so a domain on a legacy chain format certifies its own
    /// snapshot path. `'State` needs equality.
    let snapshotLawsWith
        (cfg: StreamConfig)
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (stateEncode: 'State -> string)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable bounded = None
        let mutable across = None

        for i in 0 .. iterations - 1 do
            let mutable state = gen.State0
            let mutable recs = OpStream.empty

            for _ in 0..5 do
                let op, r' = gen.Op rng
                rng <- r'

                match OpStream.appendWith cfg hashFn sw (Human "conf") op state recs with
                | Ok(s', recs') ->
                    state <- s'
                    recs <- recs'
                | Error _ -> ()

            let len = List.length recs

            if len > 0 then
                let atSeq, r2 = ConfRng.intBelow (len + 1) rng
                rng <- r2

                match OpStream.compact hashFn stateEncode sw gen.State0 recs atSeq with
                | Ok(snap, tail) ->
                    match OpStream.replayFrom sw snap tail, OpStream.replay sw gen.State0 recs with
                    | Ok a, Ok b when a = b -> ()
                    | other ->
                        if bounded.IsNone then
                            bounded <-
                                Some(sprintf "seed=%d iter=%d: replayFrom ≠ replay-from-origin (%A)" seed i other)

                    if
                        not (OpStream.verifyAcrossWith cfg hashFn stateEncode sw snap tail)
                        && across.IsNone
                    then
                        across <-
                            Some(sprintf "seed=%d iter=%d: verifyAcrossWith rejected an intact (snapshot, tail)" seed i)
                | Error e ->
                    if bounded.IsNone then
                        bounded <- Some(sprintf "seed=%d iter=%d: compact failed: %s" seed i e)

        [ { Law = "bounded replay (replayFrom snapshot tail = replay from origin)"
            Passed = bounded.IsNone
            Counterexample = bounded }
          { Law = "verifyAcross accepts an intact (snapshot, tail)"
            Passed = across.IsNone
            Counterexample = across } ]

    /// Snapshot / compaction laws (Phase 07): **bounded replay** (`replayFrom` a compacted
    /// checkpoint equals `replay` from origin) and **verifyAcross accepts an intact boundary**.
    /// A domain that snapshots a large `'State` runs this with its `stateEncode`. `'State` needs
    /// equality. The canonical-config wrapper over `snapshotLawsWith` (Phase 14).
    let snapshotLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (stateEncode: 'State -> string)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        snapshotLawsWith OpStream.canonicalConfig sw gen stateEncode hashFn seed iterations

    /// Op-DAG laws (Phase 07): **verifyDag accepts an intact DAG**, **replayTo is
    /// deterministic** (the total topo order ⇒ the same head replays to the same state), and
    /// **verifyDag detects a tampered node**. A domain that adopts the branching op-DAG runs
    /// this. `'State` needs equality.
    let dagLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable verify = None
        let mutable determinism = None
        let mutable tamper = None
        let mutable roundtrip = None

        for i in 0 .. iterations - 1 do
            // a fork+merge DAG: genesis g; a, b both children of g; merge m of (a, b)
            let op0, r0 = gen.Op rng
            let opA, r1 = gen.Op r0
            let opB, r2 = gen.Op r1
            let opM, r3 = gen.Op r2
            rng <- r3

            let g, d1 = Dag.append hashFn sw (Human "conf") op0 "" Dag.empty
            let a, d2 = Dag.append hashFn sw (Human "conf") opA g d1
            let b, d3 = Dag.append hashFn sw (Human "conf") opB g d2
            let m, dag = Dag.merge hashFn sw (Human "conf") opM a b d3

            if not (Dag.verifyDag hashFn sw dag) && verify.IsNone then
                verify <- Some(sprintf "seed=%d iter=%d: verifyDag rejected an intact DAG" seed i)

            // Determinism (Phase 18): build the SAME logical history with a permuted append order
            // (B before A) and confirm it converges to the same content-addressed DAG + head and
            // replays to the same state — a genuine convergence check, not the prior `f x <> f x`
            // self-comparison. (Content addressing is append-order-insensitive, so a regression
            // that leaked insertion order into a node id would diverge here.)
            let g', e1 = Dag.append hashFn sw (Human "conf") op0 "" Dag.empty
            let b', e2 = Dag.append hashFn sw (Human "conf") opB g' e1
            let a', e3 = Dag.append hashFn sw (Human "conf") opA g' e2
            let m', dag' = Dag.merge hashFn sw (Human "conf") opM a' b' e3

            if
                (dag'.Nodes <> dag.Nodes
                 || m' <> m
                 || Dag.replayTo sw gen.State0 dag' m' <> Dag.replayTo sw gen.State0 dag m)
                && determinism.IsNone
            then
                determinism <-
                    Some(sprintf "seed=%d iter=%d: a permuted-construction history diverged (nodes/head/replay)" seed i)

            // tamper one node's op with a genuinely-different op
            let newOp, r4 = gen.Op rng
            rng <- r4

            let tid, tnode = dag.Nodes |> Map.toList |> List.head

            if sw.Encode tnode.Op <> sw.Encode newOp then
                let tampered = { Dag.T.Nodes = Map.add tid { tnode with Op = newOp } dag.Nodes }

                if Dag.verifyDag hashFn sw tampered && tamper.IsNone then
                    tamper <- Some(sprintf "seed=%d iter=%d: a tampered DAG node was not detected" seed i)

            // JSONL persistence round-trip (Phase 01 is shipped)
            match Dag.fromJsonl sw (Dag.toJsonl sw.Encode dag) with
            | Ok dag' when dag'.Nodes = dag.Nodes -> ()
            | other ->
                if roundtrip.IsNone then
                    roundtrip <- Some(sprintf "seed=%d iter=%d: DAG JSONL round-trip ≠ original (%A)" seed i other)

        [ { Law = "verifyDag accepts an intact DAG"
            Passed = verify.IsNone
            Counterexample = verify }
          { Law = "replayTo is deterministic"
            Passed = determinism.IsNone
            Counterexample = determinism }
          { Law = "verifyDag detects a tampered node"
            Passed = tamper.IsNone
            Counterexample = tamper }
          { Law = "DAG JSONL round-trip preserves the DAG"
            Passed = roundtrip.IsNone
            Counterexample = roundtrip } ]

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
    ///  - **tamper-evidence** — a tampered captured value fails `verifyCaptures`.
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
        let mutable rng = ConfRng.ofSeed seed
        let mutable exact = None
        let mutable deterministic = None
        let mutable tamper = None

        for i in 0 .. iterations - 1 do
            // record a non-deterministic session: 1..5 effects, each a fresh draw, journalled.
            let k, r0 = ConfRng.intBelow 5 rng
            rng <- r0
            let mutable captures = []
            let mutable recorded = []

            for _ in 0..k do
                let v, r' = draw rng
                rng <- r'
                // the recorded "live source" draws v; captureEffect journals it under "random".
                let got, caps' =
                    OpStream.captureEffect hashFn encode "random" "eff" (fun () -> v) captures

                captures <- caps'
                recorded <- recorded @ [ got ]

            // replay with a live source that would draw DIFFERENT values — a capture hit must win.
            let mutable cursor = captures
            let mutable replayed = []
            let mutable replayOk = true

            for orig in recorded do
                // a divergent live fallback: if replay ever re-evaluated, it would diverge from orig.
                let liveDifferent () =
                    let v, r' = draw rng
                    rng <- r'
                    v

                match OpStream.replayEffect decode "eff" "random" liveDifferent cursor with
                | Ok(v, rest) ->
                    cursor <- rest
                    replayed <- replayed @ [ v ]
                | Error _ -> replayOk <- false

            if
                exact.IsNone
                && (not replayOk
                    || replayed <> recorded
                    || not (List.isEmpty cursor)
                    // byte-identity: each replayed value re-encodes to the journalled value.
                    || (List.zip replayed captures |> List.exists (fun (v, c) -> encode v <> c.Value)))
            then
                exact <- Some(sprintf "seed=%d iter=%d: replay-with-capture ≠ recorded session" seed i)

            // deterministic pass-through: no capture emitted, replay re-evaluates live, journal intact.
            let dv, r1 = draw rng
            rng <- r1

            let dGot, dCaps =
                OpStream.captureEffect hashFn encode OpStream.deterministicTag "eff" (fun () -> dv) []

            let dReplay =
                OpStream.replayEffect decode "eff" OpStream.deterministicTag (fun () -> dv) dCaps

            if
                deterministic.IsNone
                && (dGot <> dv || not (List.isEmpty dCaps) || dReplay <> Ok(dv, []))
            then
                deterministic <-
                    Some(sprintf "seed=%d iter=%d: a deterministic effect was captured or altered replay" seed i)

            // tamper: replace a captured value with a genuinely-different encoding ⇒ verifyCaptures fails.
            match captures with
            | [] -> ()
            | _ ->
                let tIdx, r2 = ConfRng.intBelow (List.length captures) rng
                let newV, r3 = draw r2
                rng <- r3
                let newValue = encode newV
                let orig = List.item tIdx captures

                if orig.Value <> newValue then
                    let tampered =
                        captures
                        |> List.mapi (fun j c -> if j = tIdx then { c with Value = newValue } else c)

                    if OpStream.verifyCaptures hashFn tampered && tamper.IsNone then
                        tamper <- Some(sprintf "seed=%d iter=%d: a tampered capture was not detected" seed i)

        // multi-identity guard (Phase 40): a journal of two distinct effect identities replays
        // correctly in record order, but a replay that requests the wrong identity at the head
        // surfaces a *named* mismatch rather than silently handing back the other effect's value.
        let multiIdentity =
            let v1, rA = draw (ConfRng.ofSeed (seed + 7))
            let v2, _ = draw rA
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

            if inOrder && misordered then
                None
            else
                Some(sprintf "seed=%d: replayEffect did not enforce effect-identity order" seed)

        [ { Law = "replay-with-capture is byte-identical to the recorded session"
            Passed = exact.IsNone
            Counterexample = exact }
          { Law = "a deterministic effect emits no capture (replay re-evaluates live)"
            Passed = deterministic.IsNone
            Counterexample = deterministic }
          { Law = "verifyCaptures detects a tampered capture"
            Passed = tamper.IsNone
            Counterexample = tamper }
          { Law = "replayEffect enforces effect-identity order (a misordered replay is a named error)"
            Passed = multiIdentity.IsNone
            Counterexample = multiIdentity } ]

    /// The dataframe-transform parity laws (Phase 29) — the teeth on a host evaluator's agreement
    /// with the `Fuaran.Core.DataFrame` reference. A domain supplies its own evaluator `under`
    /// (signature-identical to `DataFrame.evalPipeline`) and a generator of `(table, pipeline)`
    /// samples; the kit certifies, over a seed-replayable sample, that for every case the evaluator
    /// agrees with the reference **byte-for-byte**:
    ///
    ///  - both `Ok` and their result tables encode to the identical wire string (`ColumnCodec.encode`
    ///    — the cross-host parity contract: same null/coercion/order/float semantics), **or**
    ///  - both `Error` (the reference rejected the pipeline and so did the evaluator).
    ///
    /// Byte-identity (not value equality) is the contract because the parity gate compares wire
    /// output across hosts. Opt-in like `dagLaws` / `captureReplayLaws` — a domain that ships a
    /// `Transform` evaluator runs it alongside its base certification. `transformLaws` over the
    /// reference itself is the reference's own self-consistency check.
    let transformLaws
        (under: Transform list -> Table -> Result<Table, EvalError>)
        (gen: ConfRng.T -> (Table * Transform list) * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable parity = None
        let mutable totality = None

        for i in 0 .. iterations - 1 do
            let (table, pipeline), r' = gen rng
            rng <- r'

            let refResult =
                try
                    Ok(DataFrame.evalPipeline pipeline table)
                with ex ->
                    Error ex.Message

            let underResult =
                try
                    Ok(under pipeline table)
                with ex ->
                    Error ex.Message

            match refResult, underResult with
            | Error m, _
            | _, Error m ->
                // an evaluator that throws (rather than returning EvalError) breaks totality (GP4)
                if totality.IsNone then
                    totality <- Some(sprintf "seed=%d iter=%d: an evaluator threw: %s" seed i m)
            | Ok refR, Ok underR ->
                let agree =
                    match refR, underR with
                    | Ok a, Ok b -> ColumnCodec.encode (Embedded a) = ColumnCodec.encode (Embedded b)
                    | Error _, Error _ -> true
                    | _ -> false

                if not agree && parity.IsNone then
                    parity <- Some(sprintf "seed=%d iter=%d: evaluator ≠ reference (pipeline=%A)" seed i pipeline)

        [ { Law = "host evaluator is byte-identical to the DataFrame reference"
            Passed = parity.IsNone
            Counterexample = parity }
          { Law = "evaluators are total (return EvalError, never throw)"
            Passed = totality.IsNone
            Counterexample = totality } ]

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
    ///  - **stable enumeration** — `Registry.enumerate` is order-stable (by id) regardless of
    ///    insertion order;
    ///  - **declaration round-trip** — `CapabilityCodec.decode (encode c) = Ok c`.
    let capabilityLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable validation = None
        let mutable replay = None
        let mutable enumeration = None
        let mutable roundtrip = None

        // value-codec for the captured realized value (an int — the stand-in for a model output).
        let encodeV (v: int) : string = string v

        let decodeV (s: string) : Result<int, string> =
            match System.Int32.TryParse s with
            | true, v -> Ok v
            | _ -> Error("not an int: " + s)

        let hashFn = OpStream.defaultHash

        for i in 0 .. iterations - 1 do
            let lo, r1 = ConfRng.intBelow 50 rng
            let span, r2 = ConfRng.intBelow 50 r1
            let hi = lo + span + 1
            rng <- r2

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
                      Determinism = Random } }

            let cap = Capability.create ("cap-" + string i) sg (ClientIsland Pyodide)

            // arg-validation: in-space accepts; out-of-space + unknown-arg reject.
            let inSpace = string lo

            match Capability.validateArgs cap [ "h0", inSpace ] with
            | Ok() -> ()
            | Error e ->
                if validation.IsNone then
                    validation <- Some(sprintf "seed=%d iter=%d: rejected a valid arg: %A" seed i e)

            (match Capability.validateArgs cap [ "h0", string (hi + 1) ] with
             | Error(ArgOutOfSpace _) -> ()
             | other ->
                 if validation.IsNone then
                     validation <- Some(sprintf "seed=%d iter=%d: out-of-space not rejected: %A" seed i other))

            (match Capability.validateArgs cap [ "nope", inSpace ] with
             | Error(UnknownArg _) -> ()
             | other ->
                 if validation.IsNone then
                     validation <- Some(sprintf "seed=%d iter=%d: unknown arg not rejected: %A" seed i other))

            // byte-identical replay through the Phase 27 seam.
            let realized, r3 = ConfRng.intBelow 1000 rng
            rng <- r3
            let args = [ "h0", inSpace ]
            let key = Capability.invocationKey cap args
            let det = Capability.determinismTag cap

            let _, caps = OpStream.captureEffect hashFn encodeV det key (fun () -> realized) []

            let liveDifferent () = realized + 1 // a divergent live source

            match OpStream.replayEffect decodeV key det liveDifferent caps with
            | Ok(v, rest) ->
                if (v <> realized || not (List.isEmpty rest)) && replay.IsNone then
                    replay <- Some(sprintf "seed=%d iter=%d: replay ≠ recorded invocation (%d vs %d)" seed i v realized)
            | Error m ->
                if replay.IsNone then
                    replay <- Some(sprintf "seed=%d iter=%d: replay errored: %s" seed i m)

            // stable enumeration regardless of insertion order.
            let capB = Capability.create ("cap-a" + string i) sg BuildTime

            let reg =
                Registry.empty |> Registry.register cap |> Result.bind (Registry.register capB)

            (match reg with
             | Ok r ->
                 let ids = Registry.enumerate r |> List.map (fun c -> c.Id)

                 if ids <> List.sort ids && enumeration.IsNone then
                     enumeration <- Some(sprintf "seed=%d iter=%d: enumerate not id-sorted: %A" seed i ids)
             | Error e ->
                 if enumeration.IsNone then
                     enumeration <- Some(sprintf "seed=%d iter=%d: register failed: %A" seed i e))

            // declaration round-trip.
            match CapabilityCodec.decode (CapabilityCodec.encode cap) with
            | Ok c2 ->
                if c2 <> cap && roundtrip.IsNone then
                    roundtrip <- Some(sprintf "seed=%d iter=%d: capability ≠ round-trip" seed i)
            | Error m ->
                if roundtrip.IsNone then
                    roundtrip <- Some(sprintf "seed=%d iter=%d: decode failed: %s" seed i m)

        [ { Law = "arg-validation accepts in-space + rejects out-of-space / unknown args"
            Passed = validation.IsNone
            Counterexample = validation }
          { Law = "a non-deterministic invocation replays byte-identically via capture"
            Passed = replay.IsNone
            Counterexample = replay }
          { Law = "registry enumeration is stable (id-sorted)"
            Passed = enumeration.IsNone
            Counterexample = enumeration }
          { Law = "capability declaration round-trips through the codec"
            Passed = roundtrip.IsNone
            Counterexample = roundtrip } ]

    /// Certify the `Fuaran.Core.Query` data-acquisition seam (Phase 46): typed-param validation
    /// (in-type accepts; wrong-type + unknown reject), a non-deterministic query replays
    /// byte-identically through the Phase 27 capture seam, registry enumeration is id-stable, and the
    /// declaration + result round-trip the codec. Mirrors `capabilityLaws`.
    let queryLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable validation = None
        let mutable replay = None
        let mutable enumeration = None
        let mutable roundtrip = None

        // value-codec for the captured realized result (the QueryResult itself, rendered canonically).
        let encodeV (qr: QueryResult) : string = QueryCodec.encodeResult qr

        let decodeV (s: string) : Result<QueryResult, string> =
            QueryCodec.decodeResult s |> Result.mapError (fun e -> sprintf "%A" e)

        let hashFn = OpStream.defaultHash

        for i in 0 .. iterations - 1 do
            let nRows, r1 = ConfRng.intBelow 5 rng
            rng <- r1

            let q: Query =
                { Id = "q-" + string i
                  Params =
                    [ { Name = "p0"
                        Type = IntType
                        Required = true } ]
                  ResultSchema = [ "n", IntType ]
                  Effect =
                    { Host = ReadsHost
                      Determinism = Network }
                  Source = Ref("src-" + string i)
                  TimeoutMs = Some 5000
                  PageSize = None }

            // param-validation: in-type accepts; wrong-type + unknown reject.
            (match Query.validateParams q [ "p0", Int 42 ] with
             | Ok() -> ()
             | Error e ->
                 if validation.IsNone then
                     validation <- Some(sprintf "seed=%d iter=%d: rejected a valid param: %A" seed i e))

            (match Query.validateParams q [ "p0", Str "nope" ] with
             | Error(ParamTypeMismatch _) -> ()
             | other ->
                 if validation.IsNone then
                     validation <- Some(sprintf "seed=%d iter=%d: type-mismatch not rejected: %A" seed i other))

            (match Query.validateParams q [ "nope", Int 1 ] with
             | Error(UnknownParam _) -> ()
             | other ->
                 if validation.IsNone then
                     validation <- Some(sprintf "seed=%d iter=%d: unknown param not rejected: %A" seed i other))

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
                 if (v <> realized || not (List.isEmpty rest)) && replay.IsNone then
                     replay <- Some(sprintf "seed=%d iter=%d: replay ≠ recorded result" seed i)
             | Error m ->
                 if replay.IsNone then
                     replay <- Some(sprintf "seed=%d iter=%d: replay errored: %s" seed i m))

            // stable enumeration regardless of insertion order.
            let qB = { q with Id = "q-a" + string i }

            (match
                QueryRegistry.empty
                |> QueryRegistry.register q
                |> Result.bind (QueryRegistry.register qB)
             with
             | Ok r ->
                 let ids = QueryRegistry.enumerate r |> List.map (fun x -> x.Id)

                 if ids <> List.sort ids && enumeration.IsNone then
                     enumeration <- Some(sprintf "seed=%d iter=%d: enumerate not id-sorted: %A" seed i ids)
             | Error e ->
                 if enumeration.IsNone then
                     enumeration <- Some(sprintf "seed=%d iter=%d: register failed: %A" seed i e))

            // declaration + result round-trip.
            (match QueryCodec.decode (QueryCodec.encode q) with
             | Ok q2 ->
                 if q2 <> q && roundtrip.IsNone then
                     roundtrip <- Some(sprintf "seed=%d iter=%d: query ≠ round-trip" seed i)
             | Error m ->
                 if roundtrip.IsNone then
                     roundtrip <- Some(sprintf "seed=%d iter=%d: query decode failed: %A" seed i m))

            (match QueryCodec.decodeResult (QueryCodec.encodeResult realized) with
             | Ok qr2 ->
                 if qr2 <> realized && roundtrip.IsNone then
                     roundtrip <- Some(sprintf "seed=%d iter=%d: result ≠ round-trip" seed i)
             | Error m ->
                 if roundtrip.IsNone then
                     roundtrip <- Some(sprintf "seed=%d iter=%d: result decode failed: %A" seed i m))

        [ { Law = "param-validation accepts in-type + rejects type-mismatch / unknown params"
            Passed = validation.IsNone
            Counterexample = validation }
          { Law = "a non-deterministic query replays byte-identically via capture"
            Passed = replay.IsNone
            Counterexample = replay }
          { Law = "query registry enumeration is stable (id-sorted)"
            Passed = enumeration.IsNone
            Counterexample = enumeration }
          { Law = "query declaration + result round-trip through the codec"
            Passed = roundtrip.IsNone
            Counterexample = roundtrip } ]

    /// The composition sample (Phase 47) a domain supplies per draw to certify cross-witness
    /// `composeAcross`. `Outer` is an `'A`-function carrying TWO independent typed slots (`SlotA`,
    /// `SlotB`, by absolute address) plus its value-hole bindings (`OuterArgs`, addr → in-space
    /// value). `ClosedInner` is a fully-bound `'B`-function whose `embed`-lift fits either slot.
    /// `OpenInnerA` / `OpenInnerB` each carry exactly one open value hole sharing the name
    /// `OpenHoleName` but at DISTINCT ids (so the two re-rooted copies get distinct absolute
    /// addresses — the hygiene case), fillable with `OpenHoleArg`.
    type CompositionSample<'A, 'B> =
        { Outer: 'A
          SlotA: string
          SlotB: string
          OuterArgs: (string * string) list
          ClosedInner: 'B
          OpenInnerA: 'B
          OpenInnerB: 'B
          OpenHoleName: string
          OpenHoleArg: string }

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

    // ---- artifact-function property-verification (Phase 48) ----
    // Lift verification from "is this *tree* valid?" to "does this *function* produce a valid tree
    // for ALL (sampled / symbolic) valid param sets?" — property-test an artifact-function against
    // a domain `Validator.Registry` (the validity oracle the verifier *drives*, read-only). The
    // correct-by-construction property no freeform code-gen can offer: a saved typed-tree function
    // is certified valid across its whole binding space, not just one instance. The function-under-
    // test, the validator registry, and the param-set source all ride as per-call parameters (GP2);
    // no new witness field (additive over the frozen `ArtifactWitness`).

    /// Why one param-set failed verification (Phase 48) — a typed defect, never a throw (GP4).
    type VerifyDefect<'Id> =
        /// the "valid" param-set was itself rejected by `apply` (a generator producing an
        /// out-of-space / unbound set, or a non-total function).
        | DidNotApply of ApplyError
        /// `apply` produced a tree the domain validator faulted (≥1 `Severity.Error` defect).
        | ValidatorRejected of Defect<'Id> list
        /// the applied tree observes an effect its declared class does not cover (Fork-3 cross-check).
        | EffectObserved of declared: EffectClass * observed: EffectClass

    /// A reproducible counterexample: the offending param-set, the defect, and the seed/iteration
    /// (a failure is reproduced by re-running the same seed — deterministic seed-replay).
    type VerifyCounterexample<'Node, 'Id> =
        { ParamSet: (string * Arg<'Node>) list
          Defect: VerifyDefect<'Id>
          Seed: int
          Iteration: int }

    /// How the param space was covered — coverage honesty (never silently sample-and-claim-verified):
    /// the whole finite space was enumerated (`Exhaustive`), or a sample of `drawn` cases was taken
    /// from a space of `SpaceSize` (`None` = unbounded: a hole ranges over `FloatRange` / `StringLen`
    /// / `AnyString`).
    type VerifyCoverage =
        | Exhaustive of cases: int
        | Sampled of drawn: int * spaceSize: int option

    /// The verification verdict: certified across the covered param space, or a counterexample.
    type FunctionVerifyReport<'Node, 'Id> =
        { Verified: bool
          Coverage: VerifyCoverage
          Counterexample: VerifyCounterexample<'Node, 'Id> option }

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

    // ---- memoised application (Phase 49) ----
    // The teeth on `Function.applyMemo` (content-addressed application caching) + its collapse of
    // op-stream replay into re-application over the memo.

    /// The memo sample (Phase 49) a domain supplies per draw to certify `Function.applyMemo`. `PureFn`
    /// is a memoisable (fully pure & deterministic) function with a valid full param-set `Args`;
    /// `ArgsAlt` is a DIFFERENT valid full param-set (it must key distinctly — the "a param change
    /// misses" case). `EffectingFn` is a non-memoisable (non-deterministic / host-effecting) function
    /// with a valid full param-set `EffectingArgs` (the soundness-guard case — never served from cache).
    type MemoSample<'Node> =
        { PureFn: 'Node
          Args: Map<string, Arg<'Node>>
          ArgsAlt: Map<string, Arg<'Node>>
          EffectingFn: 'Node
          EffectingArgs: Map<string, Arg<'Node>> }

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
        let mutable rng = ConfRng.ofSeed seed
        let mutable findable = None
        let mutable nonMatch = None
        let mutable narrowing = None
        let mutable defaultDeny = None

        for i in 0 .. iterations - 1 do
            let lo, r1 = ConfRng.intBelow 50 rng
            let span, r2 = ConfRng.intBelow 50 r1
            let hi = lo + span + 1
            rng <- r2

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
            | Error e ->
                if findable.IsNone then
                    findable <- Some(sprintf "seed=%d iter=%d: register failed: %A" seed i e)
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

                if
                    (not (List.contains cap.Id bySub) || not (List.contains cap.Id byExact))
                    && findable.IsNone
                then
                    findable <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: entry not findable by its own result/holes (sub=%A exact=%A)"
                                seed
                                i
                                bySub
                                byExact
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

                if (not (List.isEmpty nm1) || not (List.isEmpty nm2)) && nonMatch.IsNone then
                    nonMatch <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: a non-matching query returned the entry (wrongResult=%d missingHole=%d)"
                                seed
                                i
                                (List.length nm1)
                                (List.length nm2)
                        )

                // ---- 3. a partial application narrows its signature in the index ----
                let pack =
                    FunctionRegistry.partiallyApply ("pack-" + string i) (Set.ofList [ "h0" ]) ent

                (match FunctionRegistry.register pack r with
                 | Error e ->
                     if narrowing.IsNone then
                         narrowing <- Some(sprintf "seed=%d iter=%d: registering the content pack failed: %A" seed i e)
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

                     if
                         (not (List.contains pack.Capability.Id ids)
                          || List.contains cap.Id ids
                          || packRequired <> [ "h1" ])
                         && narrowing.IsNone
                     then
                         narrowing <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: partial application did not narrow in the index (found=%A packRequired=%A)"
                                     seed
                                     i
                                     ids
                                     packRequired
                             ))

                // ---- 4. dispatch stays default-deny + arg-validated ----
                let body (_: FunctionEntry) () = Ok 1

                let unreg =
                    FunctionRegistry.dispatch r "nope" [ "h0", string lo; "h1", string lo ] body

                let okCall =
                    FunctionRegistry.dispatch r cap.Id [ "h0", string lo; "h1", string lo ] body

                let badArg =
                    FunctionRegistry.dispatch r cap.Id [ "h0", string (hi + 1); "h1", string lo ] body

                let denyOk =
                    match unreg, okCall, badArg with
                    | Error(NoSuchCapability _), Ok 1, Error(ArgOutOfSpace _) -> true
                    | _ -> false

                if not denyOk && defaultDeny.IsNone then
                    defaultDeny <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: dispatch not default-deny / arg-validated (unreg=%A ok=%A bad=%A)"
                                seed
                                i
                                unreg
                                okCall
                                badArg
                        )

        [ { Law = "a function is findable by its declared result type + required holes (subsumption + exact)"
            Passed = findable.IsNone
            Counterexample = findable }
          { Law = "a non-matching query (wrong result type / unmet hole) returns it not"
            Passed = nonMatch.IsNone
            Counterexample = nonMatch }
          { Law =
              "a partial application narrows its signature in the index (content pack findable by the smaller context)"
            Passed = narrowing.IsNone
            Counterexample = narrowing }
          { Law = "dispatch stays default-deny + arg-validated (unregistered id refused, out-of-space arg rejected)"
            Passed = defaultDeny.IsNone
            Counterexample = defaultDeny } ]

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
        let mutable rng = ConfRng.ofSeed seed
        let mutable roundTrip = None
        let mutable mismatch = None
        let mutable unknownBase = None
        let mutable shapeDerived = None

        for i in 0 .. iterations - 1 do
            let lo, r1 = ConfRng.intBelow 50 rng
            let span, r2 = ConfRng.intBelow 50 r1
            let hi = lo + span + 1
            rng <- r2

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
            | Error e ->
                if roundTrip.IsNone then
                    roundTrip <- Some(sprintf "seed=%d iter=%d: base register failed: %A" seed i e)
            | Ok reg ->
                // ---- 1. load round-trip — curry h0; the narrowed entry is findable from {h1} ----
                let pf = ContentPack.pack ("pack-" + string i) (Set.ofList [ "h0" ]) baseEntry

                let manifest =
                    { PackId = "P" + string i
                      Domain = "ref"
                      PackVersion = 1
                      Functions = [ pf ] }

                (match ContentPack.load manifest reg with
                 | Error e ->
                     if roundTrip.IsNone then
                         roundTrip <- Some(sprintf "seed=%d iter=%d: load of a valid pack failed: %A" seed i e)
                 | Ok loaded ->
                     let smallQuery =
                         { ResultType = Some resultType
                           Available = [ h1 ] }

                     let ids =
                         FunctionRegistry.findBySignature Subsumes smallQuery loaded
                         |> List.map (fun e -> e.Capability.Id)

                     if not (List.contains pf.NewId ids) && roundTrip.IsNone then
                         roundTrip <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: loaded packed function not findable under its narrowed signature (found=%A)"
                                     seed
                                     i
                                     ids
                             ))

                // ---- 2. a stale-version pack fails loudly ----
                let stale =
                    { pf with
                        BaseSignatureVersion = pf.BaseSignatureVersion + "X" }

                let staleManifest = { manifest with Functions = [ stale ] }

                (match ContentPack.load staleManifest reg with
                 | Error(SignatureVersionMismatch(_, baseId, declared, actual)) ->
                     if (baseId <> baseCap.Id || declared = actual) && mismatch.IsNone then
                         mismatch <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: mismatch error fields wrong (base=%s declared=%s actual=%s)"
                                     seed
                                     i
                                     baseId
                                     declared
                                     actual
                             )
                 | other ->
                     if mismatch.IsNone then
                         mismatch <-
                             Some(sprintf "seed=%d iter=%d: stale-version pack not refused loudly: %A" seed i other))

                // ---- 3. an unknown base is default-denied (enumerating the known ids) ----
                let ghost =
                    { NewId = "ghost-" + string i
                      BaseId = "no-such-base"
                      BaseSignatureVersion = pf.BaseSignatureVersion
                      BoundAddrs = Set.ofList [ "h0" ] }

                let ghostManifest = { manifest with Functions = [ ghost ] }

                (match ContentPack.load ghostManifest reg with
                 | Error(UnknownBaseFunction(_, "no-such-base", known)) ->
                     if not (List.contains baseCap.Id known) && unknownBase.IsNone then
                         unknownBase <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: UnknownBaseFunction did not enumerate the known ids (%A)"
                                     seed
                                     i
                                     known
                             )
                 | other ->
                     if unknownBase.IsNone then
                         unknownBase <-
                             Some(sprintf "seed=%d iter=%d: unknown base not default-denied: %A" seed i other))

                // ---- 4. the version is genuinely shape-derived ----
                let sg' =
                    { sg with
                        Holes = [ h0; h1; mkHole "h2" ] }

                if
                    ContentPack.signatureFingerprint sg = ContentPack.signatureFingerprint sg'
                    && shapeDerived.IsNone
                then
                    shapeDerived <-
                        Some(
                            sprintf "seed=%d iter=%d: a changed hole set did not shift the signature fingerprint" seed i
                        )

        [ { Law = "a content pack loads and each curried function is findable under its narrowed signature"
            Passed = roundTrip.IsNone
            Counterexample = roundTrip }
          { Law = "a pack pinned to a stale base-signature version is refused loudly (SignatureVersionMismatch)"
            Passed = mismatch.IsNone
            Counterexample = mismatch }
          { Law = "an unknown base is default-denied (UnknownBaseFunction enumerates the known ids)"
            Passed = unknownBase.IsNone
            Counterexample = unknownBase }
          { Law = "the signature version is shape-derived (a changed hole set shifts the fingerprint)"
            Passed = shapeDerived.IsNone
            Counterexample = shapeDerived } ]

    // ---- aggregate parity (Phase 36) ----
    // The teeth on `Column.aggregate` as the SINGLE source the DataFrame `GroupBy` calls: the public
    // surface must produce byte-identically what a single-group `GroupBy` produces, and skip nulls.

    /// The aggregate-parity laws (Phase 36). Self-contained — over a seed-replayable sample of random
    /// (int/float, null-bearing) columns it certifies, for every `AggFn`:
    ///
    ///  - **single-source parity** — `Column.aggregate fn col` equals the cell a single-group
    ///    `GroupBy([], [agg])` produces over the same column (the de-duplication is the point: one
    ///    implementation, byte-identical value);
    ///  - **null-skip** — `Count` equals the present-cell count, and `Sum` over a null-bearing column
    ///    equals `Sum` over its present-only projection (the pinned NA-skip semantics).
    let aggregateParityLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable parity = None
        let mutable nullSkip = None

        let fns = [ Sum; Mean; Min; Max; Count; Median; StdDev; First; Last; CountDistinct ]

        for i in 0 .. iterations - 1 do
            let isInt, r1 = ConfRng.intBelow 2 rng
            let nRows, r2 = ConfRng.intBelow 6 r1
            rng <- r2
            let ty = if isInt = 0 then IntType else FloatType
            let mutable r = rng

            let cells =
                [ for _ in 0..nRows ->
                      let k, r' = ConfRng.intBelow 4 r
                      r <- r'

                      if k = 0 then
                          Null
                      else
                          let v, r'' = ConfRng.intBelow 200 r
                          r <- r''

                          if ty = IntType then
                              Int(v - 100)
                          else
                              Float(float (v - 100) * 0.5) ]

            rng <- r
            let col = Column.create "c" ty cells

            let table =
                { Schema = [ "c", ty ]
                  Columns = [ col ] }

            for fn in fns do
                let direct = Column.aggregate fn col |> Result.mapError (fun _ -> "aggErr")

                let viaGroup =
                    match DataFrame.evalPipeline [ GroupBy([], [ { Name = "a"; Fn = fn; Of = "c" } ]) ] table with
                    | Ok t ->
                        match Table.tryColumn "a" t with
                        | Some ac -> Ok(Column.cell 0 ac)
                        | None -> Error "no agg column"
                    | Error _ -> Error "aggErr"

                if direct <> viaGroup && parity.IsNone then
                    parity <-
                        Some(
                            sprintf
                                "seed=%d iter=%d fn=%A: aggregate ≠ single-group GroupBy (%A vs %A)"
                                seed
                                i
                                fn
                                direct
                                viaGroup
                        )

            let present = cells |> List.filter (fun c -> not (Cell.isNull c))
            let presentCol = Column.create "c" ty present

            (match Column.aggregate Count col with
             | Ok(Int n) when n = List.length present -> ()
             | other ->
                 if nullSkip.IsNone then
                     nullSkip <- Some(sprintf "seed=%d iter=%d: Count ≠ present count (%A)" seed i other))

            (match Column.aggregate Sum col, Column.aggregate Sum presentCol with
             | Ok a, Ok b when a = b -> ()
             | a, b ->
                 if nullSkip.IsNone then
                     nullSkip <- Some(sprintf "seed=%d iter=%d: Sum not null-skipping (%A vs %A)" seed i a b))

        [ { Law = "Column.aggregate is byte-identical to a single-group GroupBy (single source of truth)"
            Passed = parity.IsNone
            Counterexample = parity }
          { Law = "Column.aggregate skips Null cells (Count = present count; Sum ignores nulls)"
            Passed = nullSkip.IsNone
            Counterexample = nullSkip } ]

    // ---- columnar op-algebra + op-stream (Phase 31) ----
    // The teeth on `ColumnOps`: a table-edit op DU applies totally, `canApply ≡ apply`, `apply ∘ invert =
    // id` (where invert is defined), and a table-edit stream chains + verifies + replays byte-identically
    // through the EXISTING `Fuaran.Core.OpStream` `StreamWitness` (no core change — the witness-free data
    // strand survives the op-stream adoption).

    /// The columnar op-algebra laws (Phase 31). Self-contained — over a seed-replayable sample it evolves
    /// an all-int reference `Table` by random ops and certifies: **apply totality** (never throws —
    /// failures are a typed `ColumnRejection`); **canApply ≡ apply**; **apply ∘ invert = id** (for an
    /// invertible op; `AppendRows`/`ApplyTransform` report `NotInvertible` and are skipped); and that the
    /// table-edit stream built via `OpStream.append` over the columnar `StreamWitness` **verifies** and
    /// **replays** back to the live state from the base table.
    let columnarOpLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable totality = None
        let mutable equivalence = None
        let mutable inversion = None
        let mutable verify = None
        let mutable replayLaw = None

        let hashFn = OpStream.defaultHash
        let sw = ColumnOps.streamWitness

        let baseTable: Table =
            { Schema = [ "a", IntType; "b", IntType ]
              Columns =
                [ Column.create "a" IntType [ Int 1; Int 2; Int 3 ]
                  Column.create "b" IntType [ Int 4; Int 5; Int 6 ] ] }

        // a (possibly-invalid) op generated against the current table state
        let genColOp (t: Table) (r: ConfRng.T) : ColumnOp * ConfRng.T =
            let rc = Table.rowCount t
            let names = Table.columnNames t
            let kind, r1 = ConfRng.intBelow 5 r

            match kind with
            | 0 ->
                let v, r2 = ConfRng.intBelow 100 r1

                if List.isEmpty names || rc = 0 then
                    InsertColumn(0, Column.create "a" IntType [ Int v; Int v; Int v ]), r2
                else
                    let ci, r3 = ConfRng.intBelow (List.length names) r2
                    let row, r4 = ConfRng.intBelow rc r3
                    SetCell(List.item ci names, row, Int v), r4
            | 1 ->
                let v, r2 = ConfRng.intBelow 100 r1

                if List.isEmpty names then
                    InsertColumn(0, Column.create "a" IntType []), r2
                else
                    let ci, r3 = ConfRng.intBelow (List.length names) r2
                    let nm = List.item ci names
                    SetColumn(Column.create nm IntType (List.replicate rc (Int v))), r3
            | 2 ->
                let id, r2 = ConfRng.intBelow 1000 r1
                let v, r3 = ConfRng.intBelow 100 r2
                let len = if List.isEmpty names then 3 else rc

                InsertColumn(List.length names, Column.create ("c" + string id) IntType (List.replicate len (Int v))),
                r3
            | 3 ->
                if List.isEmpty names then
                    InsertColumn(0, Column.create "a" IntType [ Int 0; Int 0; Int 0 ]), r1
                else
                    let ci, r2 = ConfRng.intBelow (List.length names) r1
                    RemoveColumn(List.item ci names), r2
            | _ ->
                let v, r2 = ConfRng.intBelow 100 r1
                AppendRows([ names |> List.map (fun n -> n, Int v) ]), r2

        for i in 0 .. iterations - 1 do
            let mutable state = baseTable
            let mutable recs = OpStream.empty

            for _ in 0..6 do
                let op, r' = genColOp state rng
                rng <- r'

                let applied =
                    try
                        Some(ColumnOps.apply op state)
                    with _ ->
                        None

                match applied with
                | None ->
                    if totality.IsNone then
                        totality <- Some(sprintf "seed=%d iter=%d: apply threw on %A" seed i op)
                | Some res ->
                    let chk = ColumnOps.canApply op state

                    let equiv =
                        match res, chk with
                        | Ok _, Ok() -> true
                        | Error e1, Error e2 -> e1 = e2
                        | _ -> false

                    if not equiv && equivalence.IsNone then
                        equivalence <- Some(sprintf "seed=%d iter=%d: canApply≠apply on %A" seed i op)

                    match res with
                    | Ok post ->
                        (match ColumnOps.invert op state with
                         | Ok inv ->
                             match ColumnOps.apply inv post with
                             | Ok restored when restored = state -> ()
                             | other ->
                                 if inversion.IsNone then
                                     inversion <-
                                         Some(sprintf "seed=%d iter=%d: apply∘invert≠id on %A (got %A)" seed i op other)
                         | Error(NotInvertible _) -> ()
                         | Error e ->
                             if inversion.IsNone then
                                 inversion <-
                                     Some(
                                         sprintf
                                             "seed=%d iter=%d: a structural op failed to invert: %A (%A)"
                                             seed
                                             i
                                             op
                                             e
                                     ))

                        match OpStream.append hashFn sw (Human "conf") op state recs with
                        | Ok(s', recs') ->
                            state <- s'
                            recs <- recs'
                        | Error _ -> ()
                    | Error _ -> ()

            if not (OpStream.verifyChain hashFn sw recs) && verify.IsNone then
                verify <- Some(sprintf "seed=%d iter=%d: verifyChain rejected an intact table-edit stream" seed i)

            match OpStream.replay sw baseTable recs with
            | Ok s when s = state -> ()
            | other ->
                if replayLaw.IsNone then
                    replayLaw <- Some(sprintf "seed=%d iter=%d: replay ≠ live state (got %A)" seed i other)

        [ { Law = "columnar apply totality (never throws)"
            Passed = totality.IsNone
            Counterexample = totality }
          { Law = "columnar canApply ≡ apply (accept/reject + rejection)"
            Passed = equivalence.IsNone
            Counterexample = equivalence }
          { Law = "columnar apply ∘ invert = identity (where invert is defined)"
            Passed = inversion.IsNone
            Counterexample = inversion }
          { Law = "verifyChain accepts an intact table-edit stream (over the columnar StreamWitness)"
            Passed = verify.IsNone
            Counterexample = verify }
          { Law = "replay re-derives the live table from the base"
            Passed = replayLaw.IsNone
            Counterexample = replayLaw } ]

    // ---- columnar validator (Phase 37) ----
    // The teeth on the `ColumnValidator` surface: stock rules over a `Table` emit located, severity-
    // tagged defects through the EXISTING defect/severity model, and the output is deterministic +
    // byte-canonical for a given table (`canonicalCodes`).

    /// The columnar-validator laws (Phase 37). Self-contained — over a seed-replayable sample of random
    /// `(a:int, s:string)` tables with injected faults (nulls + out-of-range ints) it certifies:
    /// **determinism** (`validate` and its `canonicalCodes` projection are identical on a re-run of the
    /// same table); and **soundness** (the count of `COL-NOTNULL` defects equals the number of null cells
    /// in the non-null column, and `COL-INRANGE` equals the number of out-of-range cells).
    let columnarValidatorLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable determinism = None
        let mutable soundness = None

        let reg =
            ColumnValidator.empty
            |> ColumnValidator.register (ColumnValidator.notNull "a")
            |> ColumnValidator.register (ColumnValidator.inRange "a" 0.0 100.0)
            |> ColumnValidator.register (ColumnValidator.ofType "s" StringType)
            |> ColumnValidator.register (ColumnValidator.unique [ "a" ])

        for i in 0 .. iterations - 1 do
            let nRows, r1 = ConfRng.intBelow 6 rng
            rng <- r1
            let mutable r = rng

            // column a: int straying out of [0,100], with ~1/5 nulls
            let aCells =
                [ for _ in 0..nRows ->
                      let k, r' = ConfRng.intBelow 5 r
                      r <- r'

                      if k = 0 then
                          Null
                      else
                          let v, r'' = ConfRng.intBelow 160 r
                          r <- r''
                          Int(v - 30) ]

            let sCells = aCells |> List.map (fun _ -> Str "x")
            rng <- r

            let t: Table =
                { Schema = [ "a", IntType; "s", StringType ]
                  Columns = [ Column.create "a" IntType aCells; Column.create "s" StringType sCells ] }

            let defects = ColumnValidator.validate reg t

            if
                (ColumnValidator.validate reg t <> defects
                 || Validator.canonicalCodes (ColumnValidator.validate reg t)
                    <> Validator.canonicalCodes defects)
                && determinism.IsNone
            then
                determinism <- Some(sprintf "seed=%d iter=%d: columnar validate is not deterministic" seed i)

            let nullCount = aCells |> List.filter Cell.isNull |> List.length

            let notNullDefects =
                defects |> List.filter (fun d -> d.Code = "COL-NOTNULL") |> List.length

            let outOfRange =
                aCells
                |> List.filter (fun c ->
                    match c with
                    | Int v -> v < 0 || v > 100
                    | _ -> false)
                |> List.length

            let inRangeDefects =
                defects |> List.filter (fun d -> d.Code = "COL-INRANGE") |> List.length

            if
                (notNullDefects <> nullCount || inRangeDefects <> outOfRange)
                && soundness.IsNone
            then
                soundness <-
                    Some(
                        sprintf
                            "seed=%d iter=%d: defect counts ≠ injected faults (notNull %d/%d, inRange %d/%d)"
                            seed
                            i
                            notNullDefects
                            nullCount
                            inRangeDefects
                            outOfRange
                    )

        [ { Law = "columnar validate is deterministic + byte-canonical (same table ⇒ same defects)"
            Passed = determinism.IsNone
            Counterexample = determinism }
          { Law = "columnar stock rules are sound (defect counts = injected faults)"
            Passed = soundness.IsNone
            Counterexample = soundness } ]

    // ---- incremental DataFrame evaluation (Phase 34) ----
    // The teeth on `DataFrame.evalFrom`: the incremental path is byte-identical to a full `evalPipeline`
    // over the changed source, for EVERY generated change (the reuse is a sound optimisation, not a
    // different answer). Includes a `ColumnOps.changeOf`-driven case (an edit-stream op → a `Change`).

    /// The incremental-equivalence laws (Phase 34). Self-contained — over a seed-replayable sample it
    /// builds an `(a:int, b:int, c:int)` source, a pipeline, and a change (with the matching changed
    /// source) and certifies:
    ///
    ///  - **change-driven equivalence** — `evalFrom (evalPipeline old) change pipeline new` equals
    ///    `evalPipeline pipeline new` for a directly-supplied `Change` (covering the reuse short-circuit
    ///    *and* the full-recompute path);
    ///  - **op-driven equivalence** — the same, where the `Change` is derived from a columnar op via
    ///    `ColumnOps.changeOf` (a `SetCell` edit), so an edit-stream drives incremental re-eval correctly.
    let incrementalLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable changeDriven = None
        let mutable opDriven = None

        let col name cells : Column = Column.create name IntType cells

        let mkTable (a: Cell list) (b: Cell list) (c: Cell list) : Table =
            { Schema = [ "a", IntType; "b", IntType; "c", IntType ]
              Columns = [ col "a" a; col "b" b; col "c" c ] }

        let pipelineOf k : Transform list =
            match k with
            | 0 -> [ Filter(Binary(Gt, Col "a", Lit(Int 0))) ]
            | 1 -> [ Project [ "a", "a"; "b", "b" ] ] // drops c → an irrelevant c-change can reuse
            | 2 -> [ Derive("d", Binary(Add, Col "a", Lit(Int 1))) ]
            | 3 -> [ GroupBy([ "a" ], [ { Name = "s"; Fn = Sum; Of = "b" } ]) ] // drops c
            | _ -> [ Sort [ "b", Asc ]; Project [ "a", "a" ] ] // drops b, c

        for i in 0 .. iterations - 1 do
            let nRows, r1 = ConfRng.intBelow 4 rng
            let rows = nRows + 1
            let mutable r = r1

            let draw () =
                let v, r' = ConfRng.intBelow 20 r
                r <- r'
                Int(v - 10)

            let a0 = [ for _ in 1..rows -> draw () ]
            let b0 = [ for _ in 1..rows -> draw () ]
            let c0 = [ for _ in 1..rows -> draw () ]
            let oldSrc = mkTable a0 b0 c0

            let pk, r2 = ConfRng.intBelow 5 r
            r <- r2
            let pipeline = pipelineOf pk

            // a directly-supplied change + the matching changed source
            let ck, r3 = ConfRng.intBelow 3 r
            r <- r3

            let change, newSrc =
                match ck with
                | 0 -> ColumnValuesChanged "c", mkTable a0 b0 (c0 |> List.map (fun _ -> Int 999))
                | 1 -> ColumnValuesChanged "a", mkTable (a0 |> List.map (fun _ -> Int 7)) b0 c0
                | _ -> RowsAppended, mkTable (a0 @ [ Int 5 ]) (b0 @ [ Int 6 ]) (c0 @ [ Int 7 ])

            rng <- r

            (match DataFrame.evalPipeline pipeline oldSrc with
             | Ok prior ->
                 let viaIncr = DataFrame.evalFrom prior change pipeline newSrc
                 let viaFull = DataFrame.evalPipeline pipeline newSrc

                 if viaIncr <> viaFull && changeDriven.IsNone then
                     changeDriven <-
                         Some(sprintf "seed=%d iter=%d: evalFrom ≠ evalPipeline (pk=%d change=%A)" seed i pk change)
             | Error _ -> ())

            // op-driven: a SetCell on column c → ColumnOps.changeOf → the same equivalence
            let editOp = SetCell("c", 0, Int 1234)

            (match ColumnOps.apply editOp oldSrc with
             | Ok newSrc2 ->
                 match DataFrame.evalPipeline pipeline oldSrc with
                 | Ok prior ->
                     let viaIncr = DataFrame.evalFrom prior (ColumnOps.changeOf editOp) pipeline newSrc2
                     let viaFull = DataFrame.evalPipeline pipeline newSrc2

                     if viaIncr <> viaFull && opDriven.IsNone then
                         opDriven <-
                             Some(sprintf "seed=%d iter=%d: op-driven evalFrom ≠ evalPipeline (pk=%d)" seed i pk)
                 | Error _ -> ()
             | Error _ -> ())

        [ { Law = "evalFrom is byte-identical to a full evalPipeline over the changed source (every change)"
            Passed = changeDriven.IsNone
            Counterexample = changeDriven }
          { Law = "a columnar op's changeOf drives evalFrom equivalently (edit-stream incremental re-eval)"
            Passed = opDriven.IsNone
            Counterexample = opDriven } ]

    /// The `ColExpr.Param` + evaluation-environment laws (Phase 77) — the teeth on the parameterised
    /// `DataFrame` evaluator, the substrate a UI tier binds a filter/state value into. Self-contained
    /// (it builds its own param pipelines from the seed); over a seed-replayable sample it certifies:
    ///
    ///  - **substitution equivalence** — `evalPipelineInEnv env p` ≡ `evalPipeline (Transform.substitute
    ///    env p)`: binding a param through the env is the same as replacing it with its literal;
    ///  - **unbound-param defect** — dropping a referenced param from the env is a named
    ///    `UnboundParam(name, bound)` (the missing name + the enumerated bound set), never a throw;
    ///  - **`paramsOf` completeness** — an env binding *exactly* `Transform.paramsOf p` evaluates with no
    ///    `UnboundParam` (evaluation consults no param outside `paramsOf`), `paramsOf` equals the params
    ///    the pipeline actually references, and any `paramsOf` member absent from the env yields the
    ///    defect naming exactly it;
    ///  - **codec round-trip** — a pipeline carrying `Param` steps `encode`→`decode`s back byte-stably.
    let paramLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable substEquiv = None
        let mutable unboundDefect = None
        let mutable completeness = None
        let mutable roundTrip = None

        let col name cells : Column = Column.create name IntType cells

        for i in 0 .. iterations - 1 do
            let nRows, r1 = ConfRng.intBelow 4 rng
            let rows = nRows + 1
            let mutable r = r1

            let draw () =
                let v, r' = ConfRng.intBelow 40 r
                r <- r'
                Int(v - 20)

            let aCells = [ for _ in 1..rows -> draw () ]

            let table =
                { Schema = [ "a", IntType ]
                  Columns = [ col "a" aCells ] }

            // 1..3 params p0..p(k-1), each bound to an Int in the env
            let pk, r2 = ConfRng.intBelow 3 r
            r <- r2
            let paramCount = pk + 1
            let names = [ for j in 0 .. paramCount - 1 -> "p" + string j ]

            let mutable env = Map.empty

            for nm in names do
                let v, r' = ConfRng.intBelow 40 r
                r <- r'
                env <- Map.add nm (Int(v - 20)) env

            // a pipeline referencing every param exactly once — one Derive per param over the input
            // table (rows ≥ 1), so every param is *consulted* on evaluation (a later step behind a
            // row-dropping Filter would be lazily skipped and never surface its unbound param).
            let pipeline =
                [ for j in 0 .. paramCount - 1 -> Derive("d" + string j, Binary(Add, Col "a", Param("p" + string j))) ]

            rng <- r

            // --- substitution equivalence ---
            let viaEnv = DataFrame.evalPipelineInEnv env pipeline table
            let viaSubst = DataFrame.evalPipeline (Transform.substitute env pipeline) table

            if viaEnv <> viaSubst && substEquiv.IsNone then
                substEquiv <- Some(sprintf "seed=%d iter=%d: evalPipelineInEnv ≠ evalPipeline∘substitute" seed i)

            // --- paramsOf completeness ---
            let declared = Transform.paramsOf pipeline

            let fullEnv =
                declared |> List.fold (fun m nm -> Map.add nm (Map.find nm env) m) Map.empty

            (match DataFrame.evalPipelineInEnv fullEnv pipeline table with
             | Error(UnboundParam(nm, _)) when completeness.IsNone ->
                 completeness <-
                     Some(sprintf "seed=%d iter=%d: env binding exactly paramsOf still reported unbound '%s'" seed i nm)
             | _ -> ())

            if Set.ofList declared <> Set.ofList names && completeness.IsNone then
                completeness <- Some(sprintf "seed=%d iter=%d: paramsOf %A ≠ referenced %A" seed i declared names)

            // --- unbound-param defect: drop one paramsOf member ---
            (match declared with
             | [] -> ()
             | _ ->
                 let dropIdx, r3 = ConfRng.intBelow (List.length declared) rng
                 rng <- r3
                 let dropped = List.item dropIdx declared
                 let partial = Map.remove dropped fullEnv

                 match DataFrame.evalPipelineInEnv partial pipeline table with
                 | Error(UnboundParam(nm, bound)) ->
                     let expectedBound = partial |> Map.toList |> List.map fst

                     if nm <> dropped && unboundDefect.IsNone then
                         unboundDefect <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: UnboundParam named '%s', expected dropped '%s'"
                                     seed
                                     i
                                     nm
                                     dropped
                             )
                     elif bound <> expectedBound && unboundDefect.IsNone then
                         unboundDefect <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: UnboundParam bound-set %A ≠ env keys %A"
                                     seed
                                     i
                                     bound
                                     expectedBound
                             )
                 | other ->
                     if unboundDefect.IsNone then
                         unboundDefect <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: dropping '%s' gave %A, expected UnboundParam"
                                     seed
                                     i
                                     dropped
                                     other
                             ))

            // --- codec round-trip including the param case ---
            let once = DataFrameCodec.encodePipeline pipeline

            (match DataFrameCodec.decodePipeline once with
             | Ok p2 ->
                 if (p2 <> pipeline || DataFrameCodec.encodePipeline p2 <> once) && roundTrip.IsNone then
                     roundTrip <-
                         Some(sprintf "seed=%d iter=%d: param pipeline codec not byte-stable round-trip" seed i)
             | Error e ->
                 if roundTrip.IsNone then
                     roundTrip <-
                         Some(
                             sprintf
                                 "seed=%d iter=%d: param pipeline decode failed: %s"
                                 seed
                                 i
                                 (ColumnCodec.errorString e)
                         ))

        [ { Law = "evalPipelineInEnv env ≡ evalPipeline (substitute Lit) for every binding in env"
            Passed = substEquiv.IsNone
            Counterexample = substEquiv }
          { Law = "an unbound param is UnboundParam(name, bound) — names the param, enumerates the bound set"
            Passed = unboundDefect.IsNone
            Counterexample = unboundDefect }
          { Law = "Transform.paramsOf is total + complete (evaluation consults no param outside it)"
            Passed = completeness.IsNone
            Counterexample = completeness }
          { Law = "a pipeline carrying Param steps round-trips the codec byte-stably"
            Passed = roundTrip.IsNone
            Counterexample = roundTrip } ]

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
        let mutable rng = ConfRng.ofSeed seed
        let mutable roundtrip = None
        let mutable combinators = None
        let mutable replay = None

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

        for i in 0 .. iterations - 1 do
            let v, r1 = ConfRng.intBelow 1000 rng
            rng <- r1

            let cases = [ Pending; Ready v; Failed("err" + string v) ]

            for d in cases do
                match CapabilityCodec.decodeDeferred decInt (CapabilityCodec.encodeDeferred encInt d) with
                | Ok d2 ->
                    if d2 <> d && roundtrip.IsNone then
                        roundtrip <- Some(sprintf "seed=%d iter=%d: Deferred ≠ round-trip (%A)" seed i d)
                | Error m ->
                    if roundtrip.IsNone then
                        roundtrip <- Some(sprintf "seed=%d iter=%d: Deferred decode failed: %s" seed i m)

            let mapped = Deferred.map ((+) 1) (Ready v)
            let pendingMapped = Deferred.map ((+) 1) Pending

            if
                (mapped <> Ready(v + 1)
                 || pendingMapped <> Pending
                 || Deferred.toResult (Ready v) <> Ok v
                 || Deferred.toResult (Failed "x") <> Error "x")
                && combinators.IsNone
            then
                combinators <- Some(sprintf "seed=%d iter=%d: Deferred combinators disagree" seed i)

            // a Ready value replays byte-identically through the Phase 27 seam.
            let key = "deferred#" + string i
            let _, caps = OpStream.captureEffect hashFn encV "network" key (fun () -> v) []
            let liveDifferent () = v + 1

            match OpStream.replayEffect decV key "network" liveDifferent caps with
            | Ok(rv, rest) ->
                if (rv <> v || not (List.isEmpty rest)) && replay.IsNone then
                    replay <- Some(sprintf "seed=%d iter=%d: Ready replay ≠ recorded (%d vs %d)" seed i rv v)
            | Error m ->
                if replay.IsNone then
                    replay <- Some(sprintf "seed=%d iter=%d: Ready replay errored: %s" seed i m)

        [ { Law = "Deferred round-trips the wire for Pending / Ready / Failed"
            Passed = roundtrip.IsNone
            Counterexample = roundtrip }
          { Law = "Deferred map / toResult behave (Ready lifts; Pending/Failed propagate)"
            Passed = combinators.IsNone
            Counterexample = combinators }
          { Law = "a Ready value replays byte-identically via the Phase 27 capture seam"
            Passed = replay.IsNone
            Counterexample = replay } ]

    // ---- serializable capability pipeline (Phase 35) ----
    // The teeth on `CapabilityPipeline`: type-checked composition (ill-typed edge ⇒ named error), a
    // canonical wire round-trip, and per-node byte-identical replay through the Phase-27 capture seam.

    /// The capability-pipeline laws (Phase 35). Self-contained (builds a `prod → cons` 2-node pipeline
    /// from a fixed registry); over a seed-replayable sample it certifies: **type-checked composition**
    /// (a well-typed pipeline passes; an `int`-arg fed a `string` producer is a named `EdgeTypeMismatch`);
    /// **wire round-trip** (`encode`→`decode` is identity); and **per-node replay** (a node's realized
    /// value, journalled under `nodeInvocationKey`, replays byte-identically via the Phase-27 seam).
    let capabilityPipelineLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable typecheck = None
        let mutable roundtrip = None
        let mutable replay = None

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
                  Determinism = Random } }

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
            [ { Law = "capability pipeline registry built"
                Passed = false
                Counterexample = Some(sprintf "%A" e) } ]
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

            for i in 0 .. iterations - 1 do
                let v, r1 = ConfRng.intBelow 100 rng
                rng <- r1

                (match CapabilityPipeline.typeCheck reg good, CapabilityPipeline.typeCheck reg bad with
                 | Ok(), Error(EdgeTypeMismatch _) -> ()
                 | g, b ->
                     if typecheck.IsNone then
                         typecheck <- Some(sprintf "seed=%d iter=%d: type-check disagreed (good=%A bad=%A)" seed i g b))

                (match CapabilityPipeline.decode (CapabilityPipeline.encode good) with
                 | Ok p2 ->
                     if p2 <> good && roundtrip.IsNone then
                         roundtrip <- Some(sprintf "seed=%d iter=%d: pipeline ≠ round-trip" seed i)
                 | Error m ->
                     if roundtrip.IsNone then
                         roundtrip <- Some(sprintf "seed=%d iter=%d: pipeline decode failed: %s" seed i m))

                // per-node replay byte-identity through the Phase 27 seam
                let key = CapabilityPipeline.nodeInvocationKey (List.head good.Nodes)
                let _, caps = OpStream.captureEffect hashFn encV "random" key (fun () -> v) []

                (match OpStream.replayEffect decV key "random" (fun () -> v + 1) caps with
                 | Ok(rv, rest) ->
                     if (rv <> v || not (List.isEmpty rest)) && replay.IsNone then
                         replay <- Some(sprintf "seed=%d iter=%d: node replay ≠ recorded (%d vs %d)" seed i rv v)
                 | Error m ->
                     if replay.IsNone then
                         replay <- Some(sprintf "seed=%d iter=%d: node replay errored: %s" seed i m))

            [ { Law = "pipeline type-check accepts a well-typed DAG + names an ill-typed edge (EdgeTypeMismatch)"
                Passed = typecheck.IsNone
                Counterexample = typecheck }
              { Law = "a capability pipeline round-trips the wire"
                Passed = roundtrip.IsNone
                Counterexample = roundtrip }
              { Law = "a pipeline node replays byte-identically via the Phase 27 capture seam"
                Passed = replay.IsNone
                Counterexample = replay } ]

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
        let mutable rng = ConfRng.ofSeed seed
        let mutable byteIdentical = None
        let mutable minimal = None
        let mutable honesty = None

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

        for i in 0 .. iterations - 1 do
            let s1v0, r1 = ConfRng.intBelow 1000 rng
            let s2v0, r2 = ConfRng.intBelow 1000 r1
            let s1v1, r3 = ConfRng.intBelow 1000 r2
            let s2v1, r4 = ConfRng.intBelow 1000 r3
            let ck, r5 = ConfRng.intBelow 3 r4
            rng <- r5

            let sv0 = Map.ofList [ "s1", s1v0; "s2", s2v0 ]

            // which source(s) changed → the new source values + the changed-input set
            let sv1, changed =
                match ck with
                | 0 -> Map.ofList [ "s1", s1v1; "s2", s2v0 ], Set.ofList [ "s1" ]
                | 1 -> Map.ofList [ "s1", s1v0; "s2", s2v1 ], Set.ofList [ "s2" ]
                | _ -> Map.ofList [ "s1", s1v1; "s2", s2v1 ], Set.ofList [ "s1"; "s2" ]

            match CapabilityPipeline.eval (bodyWith sv0 (ResizeArray())) pipeline with
            | Error e ->
                if byteIdentical.IsNone then
                    byteIdentical <- Some(sprintf "seed=%d iter=%d: prior eval errored: %A" seed i e)
            | Ok prior ->
                let fullInvoked = ResizeArray()
                let incrInvoked = ResizeArray()
                let viaFull = CapabilityPipeline.eval (bodyWith sv1 fullInvoked) pipeline

                let viaIncr =
                    CapabilityPipeline.evalFrom (bodyWith sv1 incrInvoked) prior changed pipeline

                // byte-identical to a full eval over the changed inputs
                if viaIncr <> viaFull && byteIdentical.IsNone then
                    byteIdentical <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: evalFrom ≠ eval (changed=%A)\n  incr=%A\n  full=%A"
                                seed
                                i
                                changed
                                viaIncr
                                viaFull
                        )

                // minimal re-invocation: evalFrom re-invokes exactly the dirty set
                let dirty = CapabilityPipeline.dirtySet changed pipeline
                let reInvoked = Set.ofSeq incrInvoked

                if reInvoked <> dirty && minimal.IsNone then
                    minimal <- Some(sprintf "seed=%d iter=%d: re-invoked=%A ≠ dirtySet=%A" seed i reInvoked dirty)

                // effect-honesty: clean nodes take their prior value & are not re-invoked; dirty nodes are.
                match viaIncr with
                | Ok result ->
                    let allIds = pipeline.Nodes |> List.map CapabilityPipeline.nodeId

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
                    | Some f when honesty.IsNone -> honesty <- Some(sprintf "seed=%d iter=%d: %s" seed i f)
                    | _ -> ()
                | Error e ->
                    if honesty.IsNone then
                        honesty <- Some(sprintf "seed=%d iter=%d: evalFrom errored: %A" seed i e)

        [ { Law = "evalFrom is byte-identical to a full eval over the changed inputs (every change-set)"
            Passed = byteIdentical.IsNone
            Counterexample = byteIdentical }
          { Law = "evalFrom re-invokes exactly the downstream-of-change nodes (minimal reuse set)"
            Passed = minimal.IsNone
            Counterexample = minimal }
          { Law = "a clean node reuses its prior value (not re-invoked); a dirty node re-invokes (effect-honesty)"
            Passed = honesty.IsNone
            Counterexample = honesty } ]

    // ---- tree-level dirty propagation (Phase 68) ----
    // The teeth on `Propagation.dirtyFromChangedIds`: over random acyclic reference graphs + a toy pull
    // evaluator, the derived dirty set is exactly the reverse-reachability closure (sound + minimal), no
    // node outside it changes value under the edit, and an incremental recompute over the dirty set is
    // byte-identical to a full recompute (the Phase-34/62 discipline at the tree level). Plus a fixed cyclic
    // fixture: a reference cycle enumerates as data (Tarjan SCC), never a divergence (GP4).

    /// The dirty-propagation laws (Phase 68). Self-contained. Each iteration builds a random DAG (node `i`
    /// reads a random subset of nodes `j < i`, so acyclic by construction) + an intrinsic base value per
    /// node; a toy pull evaluator computes `value(n) = base(n) + Σ value(reads)`. It changes one node's base
    /// and certifies:
    ///
    ///  - **sound + minimal dirty set** — `dirtyFromChangedIds` equals an independent per-node
    ///    reads-reachability oracle (the changed node ∪ every node transitively reading it, and nothing
    ///    else);
    ///  - **frontier soundness** — no node *outside* the dirty set changes value under the edit;
    ///  - **byte-identity bridge** — an incremental recompute (reuse clean, recompute dirty) is byte-identical
    ///    to a full recompute over the changed base.
    ///
    /// A final fixed law certifies **cycle-as-data**: a 3-cycle enumerates as a `Cycles` SCC and
    /// `cycleThrough` returns a path — `sort` terminates, never diverges.
    let dirtyPropagationLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable soundMinimal = None
        let mutable frontier = None
        let mutable byteIdentity = None

        // an independent oracle: the read-edges reachable from `start` (inclusive) — a per-node forward walk,
        // computed differently from `dirtyFromChangedIds`' inverted BFS, so it genuinely cross-checks it.
        let readsReachable (deps: Map<string, Set<string>>) (start: string) : Set<string> =
            let rec go (front: Set<string>) (acc: Set<string>) =
                if Set.isEmpty front then
                    acc
                else
                    let next =
                        (Set.empty, front)
                        ||> Set.fold (fun s node ->
                            match Map.tryFind node deps with
                            | Some rs -> Set.union s rs
                            | None -> s)

                    let fresh = Set.difference next acc
                    go fresh (Set.union acc fresh)

            go (Set.singleton start) (Set.singleton start)

        for i in 0 .. iterations - 1 do
            let extra, r1 = ConfRng.intBelow 6 rng
            let nNodes = extra + 2
            let mutable r = r1
            let ids = [ for k in 0 .. nNodes - 1 -> string k ]

            // node k reads a random subset of {0 .. k-1} — acyclic by construction (edges point to lower ids).
            let deps =
                [ for k in 0 .. nNodes - 1 ->
                      let reads =
                          [ for j in 0 .. k - 1 do
                                let coin, r' = ConfRng.intBelow 3 r
                                r <- r'

                                if coin = 0 then
                                    yield string j ]

                      string k, Set.ofList reads ]
                |> Map.ofList

            // an intrinsic base value per node, and a change to one node's base.
            let base0 =
                [ for k in 0 .. nNodes - 1 ->
                      let v, r' = ConfRng.intBelow 100 r
                      r <- r'
                      string k, v ]
                |> Map.ofList

            let ck, r2 = ConfRng.intBelow nNodes r
            r <- r2
            let changedId = string ck
            let base1 = Map.add changedId (Map.find changedId base0 + 1000) base0
            rng <- r

            // toy pull evaluator over the DAG, index order (reads point to lower ids ⇒ already computed).
            let evalWith (baseOf: Map<string, int>) : Map<string, int> =
                (Map.empty, ids)
                ||> List.fold (fun acc id ->
                    let v =
                        Map.find id baseOf
                        + (Map.find id deps |> Set.fold (fun s rd -> s + Map.find rd acc) 0)

                    Map.add id v acc)

            let oldVals = evalWith base0
            let newVals = evalWith base1

            let changed = Set.singleton changedId
            let dirty = Propagation.dirtyFromChangedIds deps changed

            // (1) sound + minimal: dirtyFromChangedIds == the independent reads-reachability oracle.
            let oracle =
                ids
                |> List.filter (fun n -> Set.contains changedId (readsReachable deps n))
                |> Set.ofList

            if dirty <> oracle && soundMinimal.IsNone then
                soundMinimal <- Some(sprintf "seed=%d iter=%d: dirty=%A ≠ oracle=%A (deps=%A)" seed i dirty oracle deps)

            // (2) frontier soundness: no node outside `dirty` changes value under the edit.
            let leaked =
                ids
                |> List.tryFind (fun n -> not (Set.contains n dirty) && Map.find n oldVals <> Map.find n newVals)

            match leaked with
            | Some n when frontier.IsNone ->
                frontier <- Some(sprintf "seed=%d iter=%d: clean node %s changed value (unsound frontier)" seed i n)
            | _ -> ()

            // (3) byte-identity: incremental recompute (reuse clean, recompute dirty) == full recompute.
            let incr =
                (Map.empty, ids)
                ||> List.fold (fun acc id ->
                    let v =
                        if Set.contains id dirty then
                            Map.find id base1
                            + (Map.find id deps |> Set.fold (fun s rd -> s + Map.find rd acc) 0)
                        else
                            Map.find id oldVals

                    Map.add id v acc)

            if incr <> newVals && byteIdentity.IsNone then
                byteIdentity <-
                    Some(
                        sprintf "seed=%d iter=%d: incremental recompute ≠ full recompute (changed=%s)" seed i changedId
                    )

        // (4) cycle-as-data: a fixed 3-cycle a→b→c→a enumerates as an SCC; `cycleThrough` returns a path.
        let cyclic =
            Map.ofList
                [ "a", Set.singleton "b"
                  "b", Set.singleton "c"
                  "c", Set.singleton "a"
                  "x", Set.singleton "a" ] // acyclic tail reader

        let cyclesResult = Propagation.sort cyclic
        let throughB = Propagation.cycleThrough "b" cyclic

        let cycleOk =
            (cyclesResult.Cycles
             |> List.exists (fun g -> Set.ofList g = Set.ofList [ "a"; "b"; "c" ]))
            && (match throughB with
                | Some g -> Set.ofList g = Set.ofList [ "a"; "b"; "c" ]
                | None -> false)
            && not (List.contains "a" cyclesResult.Order) // a cyclic node is not in the linear Order

        [ { Law = "dirtyFromChangedIds equals the independent reads-reachability closure (sound + minimal)"
            Passed = soundMinimal.IsNone
            Counterexample = soundMinimal }
          { Law = "no node outside the dirty set changes value under the edit (frontier soundness)"
            Passed = frontier.IsNone
            Counterexample = frontier }
          { Law = "incremental recompute over the dirty set is byte-identical to a full recompute"
            Passed = byteIdentity.IsNone
            Counterexample = byteIdentity }
          { Law = "a reference cycle enumerates as a Tarjan SCC + cycleThrough returns a path (cycle-as-data)"
            Passed = cycleOk
            Counterexample =
              (if cycleOk then
                   None
               else
                   Some(sprintf "cycles=%A through-b=%A" cyclesResult.Cycles throughB)) } ]

    // ---- tree-level incremental recompute driver (Phase 69) ----
    // The teeth on `Propagation.evalFrom`: over random acyclic DAGs + a toy pull evaluator, the incremental
    // recompute (reuse clean, re-evaluate dirty) is byte-identical to a full `eval` over the changed inputs,
    // re-evaluates exactly the dirty set (minimality, via an invoked-node recorder), and an out-of-graph
    // change is a named `EvalUnknownChange` envelope.

    /// The incremental-driver laws (Phase 69). Self-contained — each iteration builds a random DAG + a base
    /// value per node, evaluates with `eval`, changes one node's base, then re-evaluates with `evalFrom` and
    /// certifies: **byte-identity** (`evalFrom (eval old) changed` equals a full `eval` over the new bases);
    /// **minimality** (`evalFrom` invokes `evalNode` on exactly the dirty set); **unknown-change envelope**
    /// (a `changed` id absent from the graph is `EvalUnknownChange`, never a throw).
    let propagationEvalLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable byteIdentical = None
        let mutable minimal = None
        let mutable unknownChange = None

        // a toy pull evaluator over the DAG: value(n) = base(n) + Σ value(reads). Records the ids it is
        // invoked on (for the minimality assertion).
        let evalNodeWith (baseOf: Map<string, int>) (deps: Map<string, Set<string>>) (invoked: ResizeArray<string>) =
            fun (resolve: string -> int option) (id: string) ->
                invoked.Add id

                let readSum =
                    Map.find id deps
                    |> Set.fold (fun s r -> s + (resolve r |> Option.defaultValue 0)) 0

                Ok(Map.find id baseOf + readSum)

        for i in 0 .. iterations - 1 do
            let extra, r1 = ConfRng.intBelow 6 rng
            let nNodes = extra + 2
            let mutable r = r1
            let ids = [ for k in 0 .. nNodes - 1 -> string k ]

            let deps =
                [ for k in 0 .. nNodes - 1 ->
                      let reads =
                          [ for j in 0 .. k - 1 do
                                let coin, r' = ConfRng.intBelow 3 r
                                r <- r'

                                if coin = 0 then
                                    yield string j ]

                      string k, Set.ofList reads ]
                |> Map.ofList

            let base0 =
                [ for k in 0 .. nNodes - 1 ->
                      let v, r' = ConfRng.intBelow 100 r
                      r <- r'
                      string k, v ]
                |> Map.ofList

            let ck, r2 = ConfRng.intBelow nNodes r
            r <- r2
            let changedId = string ck
            let base1 = Map.add changedId (Map.find changedId base0 + 1000) base0
            rng <- r

            match Propagation.eval (evalNodeWith base0 deps (ResizeArray())) deps with
            | Error e ->
                if byteIdentical.IsNone then
                    byteIdentical <- Some(sprintf "seed=%d iter=%d: prior eval errored: %A" seed i e)
            | Ok prior ->
                let fullInvoked = ResizeArray()
                let incrInvoked = ResizeArray()
                let viaFull = Propagation.eval (evalNodeWith base1 deps fullInvoked) deps

                let viaIncr =
                    Propagation.evalFrom
                        (evalNodeWith base1 deps incrInvoked)
                        prior.Values
                        (Set.singleton changedId)
                        deps

                // (1) byte-identical to a full eval over the changed inputs
                if viaIncr <> viaFull && byteIdentical.IsNone then
                    byteIdentical <- Some(sprintf "seed=%d iter=%d: evalFrom ≠ eval (changed=%s)" seed i changedId)

                // (2) minimal: evalFrom invokes evalNode on exactly the dirty set (all nodes acyclic here)
                let dirty = Propagation.dirtyFromChangedIds deps (Set.singleton changedId)

                if Set.ofSeq incrInvoked <> dirty && minimal.IsNone then
                    minimal <-
                        Some(sprintf "seed=%d iter=%d: invoked=%A ≠ dirty=%A" seed i (Set.ofSeq incrInvoked) dirty)

                // (3) unknown-change envelope: a changed id not in the graph is a named error
                match
                    Propagation.evalFrom
                        (evalNodeWith base1 deps (ResizeArray()))
                        prior.Values
                        (Set.singleton "no-such-id")
                        deps
                with
                | Error(Propagation.EvalUnknownChange [ "no-such-id" ]) -> ()
                | other ->
                    if unknownChange.IsNone then
                        unknownChange <-
                            Some(sprintf "seed=%d iter=%d: expected EvalUnknownChange, got %A" seed i other)

        [ { Law = "evalFrom is byte-identical to a full eval over the changed inputs (every change)"
            Passed = byteIdentical.IsNone
            Counterexample = byteIdentical }
          { Law = "evalFrom re-evaluates exactly the dirty set (minimal reuse)"
            Passed = minimal.IsNone
            Counterexample = minimal }
          { Law = "a changed id absent from the dependency map is a named EvalUnknownChange (GP5)"
            Passed = unknownChange.IsNone
            Counterexample = unknownChange } ]

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

    // ---- canonical float encoder (Phase 55) ----
    // The teeth on `Wire.Canon.canonicalFloat` — the single cross-host float→string encoder every
    // float→wire / float→key path routes through, so the bytes match across the .NET / Fable / TS /
    // Python hosts.

    /// The canonical-float laws (Phase 55). Self-contained (it draws floats from the seed); over a
    /// seed-replayable sample certifies: **determinism** (the same float always renders identically),
    /// **finite round-trip** (a finite float's canonical string re-parses through the wire parser to the
    /// same numeric value — the cross-host parity contract; an integer-valued float legitimately
    /// re-parses as a `JInt` of the same value, per `WIRE_FORMAT`), and **stable non-finite tokens**
    /// (`NaN` / `±Infinity` render to fixed, distinct tokens, never host-/locale-specific text).
    let canonicalFloatLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable determinism = None
        let mutable roundtrip = None

        let numericOf (s: string) : float option =
            match Json.parse s with
            | Ok(JInt i) -> Some(float i)
            | Ok(JFloat f) -> Some f
            | _ -> None

        for i in 0 .. iterations - 1 do
            let a, r1 = ConfRng.intBelow 2000000 rng
            let b, r2 = ConfRng.intBelow 1000 r1
            rng <- r2
            // a spread of finite magnitudes, positive and negative.
            let f = float (a - 1000000) / float (b + 1)

            let s1 = Canon.canonicalFloat f
            let s2 = Canon.canonicalFloat f

            if s1 <> s2 && determinism.IsNone then
                determinism <- Some(sprintf "seed=%d iter=%d: canonicalFloat not deterministic for %g" seed i f)

            match numericOf s1 with
            | Some v when v = f -> ()
            | other ->
                if roundtrip.IsNone then
                    roundtrip <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: finite round-trip ≠ original for %g (got %A from %s)"
                                seed
                                i
                                f
                                other
                                s1
                        )

        // stable, distinct non-finite tokens (fixed text, not host-/locale-specific).
        let nonFinite =
            let nan = Canon.canonicalFloat System.Double.NaN
            let pinf = Canon.canonicalFloat System.Double.PositiveInfinity
            let ninf = Canon.canonicalFloat System.Double.NegativeInfinity

            if
                nan = "\"NaN\""
                && pinf = "\"Infinity\""
                && ninf = "\"-Infinity\""
                && nan <> pinf
                && pinf <> ninf
            then
                None
            else
                Some(
                    sprintf "seed=%d: non-finite tokens not stable/distinct (nan=%s pinf=%s ninf=%s)" seed nan pinf ninf
                )

        [ { Law = "canonicalFloat is deterministic (same float ⇒ same string)"
            Passed = determinism.IsNone
            Counterexample = determinism }
          { Law = "a finite float round-trips through the wire parser to the same numeric value"
            Passed = roundtrip.IsNone
            Counterexample = roundtrip }
          { Law = "non-finite floats render to stable, distinct tokens (NaN / ±Infinity)"
            Passed = nonFinite.IsNone
            Counterexample = nonFinite } ]

    // ---- memo encoder injectivity (Phase 56) ----
    // The teeth on `applyMemo`'s silent precondition: its content-addressed key is
    // `Tree.encodeHash w.Tree encode node`, so a non-injective `encode` (two structurally-distinct trees
    // → the same string) would make the cache serve the WRONG tree. This certifies a domain's encoder is
    // collision-free over its generator.

    /// The encoder-injectivity law (Phase 56). A domain supplies its witness `w`, the node-encoder
    /// `encode` it passes to `applyMemo`, and a tree generator `gen`; over a seed-replayable sample the
    /// kit certifies that distinct trees never share a content hash
    /// (`Tree.encodeHash a = Tree.encodeHash b ⇒ a = b`) — the precondition that makes the memo cache
    /// sound. A lossy encoder fails with a reproducible `(tree, tree)` counterexample (the two colliding
    /// trees). `'Node` needs equality. Mirrors the `Corpus.codecLaws` "certify-your-codec" posture.
    let encoderInjectivityLaws
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (gen: ConfRng.T -> 'Node * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable seen = Map.empty<string, 'Node>
        let mutable collision = None
        let mutable i = 0

        while collision.IsNone && i < iterations do
            let tree, r = gen rng
            rng <- r
            let h = Tree.encodeHash w.Tree encode tree

            match Map.tryFind h seen with
            | Some prior when prior <> tree ->
                collision <-
                    Some(sprintf "seed=%d iter=%d: distinct trees share content hash %s (%A vs %A)" seed i h prior tree)
            | Some _ -> () // the same tree re-drawn — not a collision
            | None -> seen <- Map.add h tree seen

            i <- i + 1

        [ { Law = "the node-encoder is collision-free (distinct trees ⇒ distinct content hash) — memo-key soundness"
            Passed = collision.IsNone
            Counterexample = collision } ]

    // ---- projection laws (Phase 58) ----
    // The teeth on `Fuaran.Core.Projection` — the read-token-lever seam. A domain supplies its
    // `ProjectionWitness`, its re-import (`applyOps` — the ops→tree half of the round trip), its
    // wire encoder (the compactness baseline), and a tree generator; the kit certifies the
    // projection contract over a seed-replayable sample.

    /// The projection laws (Phase 58):
    ///
    ///  - **round-trip idempotence** — `project ∘ (applyOps ∘ parseBack ∘ render) ∘ project =
    ///    project`: parsing a projection back to ops and re-importing them yields a tree whose
    ///    whole projection is identical (reads stay cheap AND writes stay trackable);
    ///  - **scoped ⊆ whole** — a `ById` / `Subtree` projection's rendered lines all appear in the
    ///    whole projection, and `ChangedSince` over an unchanged tree is empty;
    ///  - **digest stability** — projecting is deterministic, and across draws a node's content
    ///    cell (`lineText`) changes iff its content digest changes (depth/indent is presentation:
    ///    a structural move never rewrites a line);
    ///  - **compactness** — the whole projection is strictly smaller than the wire form on every
    ///    drawn tree (the token-reduction floor; a floor, not a fixed ratio).
    ///
    /// `wireEncode` and `applyOps` are per-call parameters (GP2) — the kit takes no dependency on
    /// a domain codec. Assumes the witness `Encode` is injective (certify it separately with
    /// `encoderInjectivityLaws` — a lossy encoder can alias two contents into one digest).
    let projectionLaws
        (pw: ProjectionWitness<'Node, 'Id, 'Op>)
        (applyOps: 'Op list -> Result<'Node, string>)
        (wireEncode: 'Node -> string)
        (gen: ConfRng.T -> 'Node * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable roundTrip = None
        let mutable subset = None
        let mutable digestStable = None
        let mutable compact = None
        // digest-stability witness across draws: id string -> (digest, content cell)
        let mutable seen = Map.empty<string, string * string>

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen rng
            rng <- r1
            let whole = Projection.project pw Whole tree

            // determinism: the same tree projects to the identical projection
            if Projection.project pw Whole tree <> whole && digestStable.IsNone then
                digestStable <- Some(sprintf "seed=%d iter=%d: projecting the same tree twice differs" seed i)

            // round-trip: render -> parseBack -> re-import -> project = the original projection
            match Projection.parseBack pw (Projection.render whole) with
            | Error e ->
                if roundTrip.IsNone then
                    roundTrip <- Some(sprintf "seed=%d iter=%d: parseBack rejected its own projection: %s" seed i e)
            | Ok ops ->
                match applyOps ops with
                | Error e ->
                    if roundTrip.IsNone then
                        roundTrip <- Some(sprintf "seed=%d iter=%d: re-import rejected the parsed ops: %s" seed i e)
                | Ok tree2 ->
                    if Projection.project pw Whole tree2 <> whole && roundTrip.IsNone then
                        roundTrip <- Some(sprintf "seed=%d iter=%d: re-imported tree projects differently" seed i)

            // scoped ⊆ whole, on a randomly-drawn id
            let ids = Tree.ids pw.Tree tree
            let target, r2 = ConfRng.choose ids rng
            rng <- r2

            let wholeRendered = whole.Lines |> List.map Projection.renderLine |> Set.ofList

            for scope in [ ById target; Subtree target ] do
                let scoped = Projection.project pw scope tree

                if
                    scoped.Lines
                    |> List.exists (fun l -> not (wholeRendered.Contains(Projection.renderLine l)))
                    && subset.IsNone
                then
                    subset <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: a scoped line (target %s) is not in the whole projection"
                                seed
                                i
                                (pw.IdW.ToString target)
                        )

            if
                (Projection.project pw (ChangedSince(Projection.snapshot pw tree)) tree).Lines
                <> []
                && subset.IsNone
            then
                subset <- Some(sprintf "seed=%d iter=%d: ChangedSince over an unchanged tree is non-empty" seed i)

            // digest ⇔ content cell, across draws (same id, possibly different content)
            for l in whole.Lines do
                match Map.tryFind l.IdKey seen with
                | Some(digest, cell) ->
                    if (digest = l.Digest) <> (cell = Projection.lineText l) && digestStable.IsNone then
                        digestStable <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: node %s — line changed without a digest change (or vice versa)"
                                    seed
                                    i
                                    l.IdKey
                            )
                | None -> seen <- Map.add l.IdKey (l.Digest, Projection.lineText l) seen

            // compactness: strictly smaller than the wire form
            if Projection.sizeOf whole >= String.length (wireEncode tree) && compact.IsNone then
                compact <-
                    Some(
                        sprintf
                            "seed=%d iter=%d: projection (%d chars) is not smaller than the wire form (%d chars)"
                            seed
                            i
                            (Projection.sizeOf whole)
                            (String.length (wireEncode tree))
                    )

        [ { Law = "projection round-trip (project ∘ re-import ∘ parseBack ∘ project = project)"
            Passed = roundTrip.IsNone
            Counterexample = roundTrip }
          { Law = "a scoped projection is a subset of the whole (ById / Subtree / ChangedSince)"
            Passed = subset.IsNone
            Counterexample = subset }
          { Law = "digest stability (a projection line changes iff its content digest changes)"
            Passed = digestStable.IsNone
            Counterexample = digestStable }
          { Law = "compactness (the projection is strictly smaller than the wire form)"
            Passed = compact.IsNone
            Counterexample = compact } ]

    // ---- AI-surface laws (Phase 59) ----

    /// The AI-surface laws (Phase 59) — the teeth on `AiSurfaceWitness`. A domain supplies its
    /// witness, an op generator, and a base artifact state; over a seed-replayable sample the kit
    /// certifies the four parts of the surface:
    ///
    ///  - **catalogue completeness** — every generated op's `KindOfOp` is catalogued in `OpKinds`,
    ///    and every catalogued kind is emitted at least once over the run (the generator must cover
    ///    the catalogue — an uncovered kind is a completeness failure, not a sampling accident);
    ///  - **read-tool discipline** — every read tool is total (never throws) and deterministic
    ///    (same state ⇒ the same `JVal`), and `AiSurface.runTool` refuses an unknown tool name with
    ///    guidance enumerating the available tools (default-deny by shape);
    ///  - **pattern determinism** — an intent built from a pattern's own anchor resolves, and
    ///    resolving the same intent twice yields the identical emission (no clock, no rng);
    ///  - **proposal soundness** — an `Allow`ed submit applies exactly what the domain reducer
    ///    applies (byte-equal states); a `NeedsApproval` submit parks without touching the reducer,
    ///    and approving it applies to the same state as a direct apply (or surfaces
    ///    `OpNoLongerApplies` when the reducer rejects, leaving the proposal pending); a rejected
    ///    proposal and a `Deny`ed submit never invoke the reducer (denied never mutates); an unknown
    ///    proposal id and a double-decide are named failures; and a reducer rejection renders
    ///    non-empty agent-readable guidance through `Explain`.
    ///
    /// `'State` and `'Op` need equality. The decision axis is driven by the kit (it substitutes its
    /// own `Decide` per draw to exercise all three outcomes), so the *plumbing* is certified for any
    /// policy; the domain's own `Decide` is sampled for totality alongside.
    let aiSurfaceLaws
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (genOp: ConfRng.T -> 'Op * ConfRng.T)
        (state0: 'State)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable completeness = None
        let mutable readTools = None
        let mutable patterns = None
        let mutable proposals = None

        let catalogued = w.OpKinds |> List.map (fun o -> o.Kind) |> Set.ofList
        let mutable seenKinds = Set.empty

        // ---- read-tool discipline (state-fixed — checked once, not per draw) ----

        (try
            for t in w.ReadTools do
                if t.Run state0 <> t.Run state0 && readTools.IsNone then
                    readTools <- Some(sprintf "seed=%d: read tool '%s' is not deterministic" seed t.Name)
         with ex ->
             if readTools.IsNone then
                 readTools <- Some(sprintf "seed=%d: a read tool threw: %s" seed ex.Message))

        if w.ReadTools |> List.forall (fun t -> t.Name <> "__no_such_tool__") then
            match AiSurface.runTool w "__no_such_tool__" state0 with
            | Ok _ ->
                if readTools.IsNone then
                    readTools <- Some(sprintf "seed=%d: an unknown read-tool name was not refused" seed)
            | Error msg ->
                if
                    not (w.ReadTools |> List.forall (fun t -> msg.Contains t.Name))
                    && readTools.IsNone
                then
                    readTools <-
                        Some(sprintf "seed=%d: the unknown-tool refusal does not enumerate the available tools" seed)

        // an intent text a pattern's own anchor matches: wildcard spans filled with a drawn token.
        let textOfAnchor (token: string) (anchor: string) : string =
            let sb = System.Text.StringBuilder()
            let mutable i = 0

            while i < anchor.Length do
                if anchor.[i] = '{' then
                    let close = anchor.IndexOf('}', i)
                    sb.Append token |> ignore
                    i <- (if close < 0 then anchor.Length else close + 1)
                else
                    sb.Append anchor.[i] |> ignore
                    i <- i + 1

            sb.ToString()

        for i in 0 .. iterations - 1 do
            let op, r1 = genOp rng
            rng <- r1

            // ---- catalogue completeness: emitted direction ----
            let kind = w.KindOfOp op
            seenKinds <- Set.add kind seenKinds

            if not (Set.contains kind catalogued) && completeness.IsNone then
                completeness <- Some(sprintf "seed=%d iter=%d: op kind '%s' is not in the catalogue" seed i kind)

            // ---- pattern determinism ----
            match w.Patterns with
            | [] -> ()
            | bank ->
                let card, r2 = ConfRng.choose bank rng
                rng <- r2

                match card.PromptAnchors with
                | [] ->
                    if patterns.IsNone then
                        patterns <- Some(sprintf "seed=%d iter=%d: pattern '%s' has no anchors" seed i card.Name)
                | anchors ->
                    let anchor, r3 = ConfRng.choose anchors rng
                    rng <- r3
                    let tok, r4 = ConfRng.intBelow 1000 rng
                    rng <- r4
                    let token = "v" + string tok

                    let intent =
                        { Text = "please " + textOfAnchor token anchor + " now"
                          Args = [ "value", token ] }

                    let a = PatternBank.resolve w intent

                    if a.IsNone && patterns.IsNone then
                        patterns <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: intent built from pattern '%s' anchor '%s' did not resolve"
                                    seed
                                    i
                                    card.Name
                                    anchor
                            )

                    if a <> PatternBank.resolve w intent && patterns.IsNone then
                        patterns <- Some(sprintf "seed=%d iter=%d: pattern resolution is not deterministic" seed i)

            // ---- proposal soundness ----
            // the kit drives the decision axis so all three outcomes are exercised for any policy;
            // the reducer is instrumented so "never mutates" is observable, not just typed away.
            // (a ref cell — a closure cannot capture a `let mutable` local.)
            let applyCalls = ref 0

            let wDriven (d: PolicyDecision) =
                { w with
                    Decide = fun _ _ -> d
                    Apply =
                        fun o s ->
                            applyCalls.Value <- applyCalls.Value + 1
                            w.Apply o s }

            // the domain's own Decide + Apply are total (sampled, never throw).
            let direct =
                try
                    w.Decide "conformance" op |> ignore
                    Some(w.Apply op state0)
                with ex ->
                    if proposals.IsNone then
                        proposals <- Some(sprintf "seed=%d iter=%d: Decide/Apply threw: %s" seed i ex.Message)

                    None

            match direct with
            | None -> ()
            | Some direct ->
                // a reducer rejection renders non-empty guidance through Explain.
                (match direct with
                 | Error rej ->
                     if Proposals.explainRejection w rej = "" && proposals.IsNone then
                         proposals <- Some(sprintf "seed=%d iter=%d: explainRejection rendered empty guidance" seed i)
                 | Ok _ -> ())

                let dRoll, r5 = ConfRng.intBelow 3 rng
                rng <- r5

                match dRoll with
                | 0 ->
                    // Allow: submit applies exactly what the reducer applies.
                    match
                        Proposals.submit (wDriven Allow) "author" "t0" None [ op ] Proposals.Queue.empty state0, direct
                    with
                    | Proposals.SubmitApplied s', Ok sd ->
                        if s' <> sd && proposals.IsNone then
                            proposals <- Some(sprintf "seed=%d iter=%d: an allowed submit ≠ direct apply" seed i)
                    | Proposals.SubmitOpRejected _, Error _ -> ()
                    | other, _ ->
                        if proposals.IsNone then
                            proposals <-
                                Some(
                                    sprintf
                                        "seed=%d iter=%d: allowed submit disagreed with the reducer (%A)"
                                        seed
                                        i
                                        other
                                )
                | 1 ->
                    // NeedsApproval: parks without applying; approval applies (or stays pending).
                    let wi = wDriven NeedsApproval

                    match Proposals.submit wi "author" "t0" (Some "intent") [ op ] Proposals.Queue.empty state0 with
                    | Proposals.SubmitProposed(q, id) ->
                        if applyCalls.Value <> 0 && proposals.IsNone then
                            proposals <- Some(sprintf "seed=%d iter=%d: parking a proposal invoked the reducer" seed i)

                        (match Proposals.approve wi "approver" "t1" id q state0, direct with
                         | Ok(q2, s'), Ok sd ->
                             if s' <> sd && proposals.IsNone then
                                 proposals <-
                                     Some(sprintf "seed=%d iter=%d: an approved proposal ≠ direct apply" seed i)

                             // double-decide is a named failure.
                             match Proposals.approve wi "approver" "t2" id q2 state0 with
                             | Error(Proposals.NotPending _) -> ()
                             | _ ->
                                 if proposals.IsNone then
                                     proposals <-
                                         Some(sprintf "seed=%d iter=%d: a decided proposal was re-decidable" seed i)
                         | Error(Proposals.OpNoLongerApplies _), Error _ -> ()
                         | other, _ ->
                             if proposals.IsNone then
                                 proposals <-
                                     Some(
                                         sprintf
                                             "seed=%d iter=%d: approval disagreed with the reducer (%A)"
                                             seed
                                             i
                                             other
                                     ))

                        // rejection never invokes the reducer.
                        let before = applyCalls.Value

                        (match Proposals.reject "approver" "t1" "not now" id q with
                         | Ok _ ->
                             if applyCalls.Value <> before && proposals.IsNone then
                                 proposals <-
                                     Some(sprintf "seed=%d iter=%d: rejecting a proposal invoked the reducer" seed i)
                         | Error _ ->
                             if proposals.IsNone then
                                 proposals <-
                                     Some(sprintf "seed=%d iter=%d: rejecting a pending proposal failed" seed i))

                        // an unknown id is a named failure.
                        (match Proposals.approve wi "approver" "t1" 9999 q state0 with
                         | Error(Proposals.UnknownProposal _) -> ()
                         | _ ->
                             if proposals.IsNone then
                                 proposals <-
                                     Some(sprintf "seed=%d iter=%d: an unknown proposal id was not refused" seed i))
                    | other ->
                        if proposals.IsNone then
                            proposals <-
                                Some(sprintf "seed=%d iter=%d: NeedsApproval did not park the submit (%A)" seed i other)
                | _ ->
                    // Deny: refused, and the reducer is never invoked.
                    match
                        Proposals.submit
                            (wDriven (Deny "policy says no"))
                            "author"
                            "t0"
                            None
                            [ op ]
                            Proposals.Queue.empty
                            state0
                    with
                    | Proposals.SubmitDenied _ ->
                        if applyCalls.Value <> 0 && proposals.IsNone then
                            proposals <- Some(sprintf "seed=%d iter=%d: a denied submit invoked the reducer" seed i)
                    | other ->
                        if proposals.IsNone then
                            proposals <-
                                Some(sprintf "seed=%d iter=%d: Deny did not refuse the submit (%A)" seed i other)

        // ---- catalogue completeness: emittable direction ----
        let missing = Set.difference catalogued seenKinds

        if not (Set.isEmpty missing) && completeness.IsNone then
            completeness <-
                Some(
                    sprintf
                        "seed=%d: catalogued kinds never emitted by the generator: %s"
                        seed
                        (missing |> Set.toList |> String.concat ", ")
                )

        [ { Law = "catalogue completeness (every emitted op kind is catalogued; every catalogued kind is emittable)"
            Passed = completeness.IsNone
            Counterexample = completeness }
          { Law = "read tools are total + deterministic; an unknown tool is refused naming the alternatives"
            Passed = readTools.IsNone
            Counterexample = readTools }
          { Law = "pattern resolution is deterministic (an anchor-built intent resolves, identically every time)"
            Passed = patterns.IsNone
            Counterexample = patterns }
          { Law = "proposal soundness (approved applies via the domain reducer; denied/rejected never mutates)"
            Passed = proposals.IsNone
            Counterexample = proposals } ]

    // ---- integrity & provenance conformance (Wave 17) ----
    // Rebuild a chain's hashes from its `(seq, actor, op)` pre-images under the canonical binding —
    // the "adversary rewrites history and recomputes every hash" operation. The result verifies under
    // `verifyChain` (it is internally consistent), so it is the forgery a bare hash chain re-accepts;
    // what catches it is either a moved head vs an external commitment (Phase 65) or an attestation
    // signed over the original head (Phase 60). Uses only the public `canonicalConfig` payload binding.
    let private reforgeCanonical
        (hashFn: HashFn)
        (encode: 'Op -> string)
        (records: OpRecord<'Op> list)
        : OpRecord<'Op> list =
        (([], OpStream.canonicalConfig.Genesis), records)
        ||> List.fold (fun (acc, prev) r ->
            let payload = OpStream.canonicalConfig.Payload r.Seq r.Actor (encode r.Op)
            let h = hashFn prev payload
            acc @ [ { r with PrevHash = prev; Hash = h } ], h)
        |> fst

    /// The attestation / replay-as-provenance laws (Phase 60) — the teeth on `IAttestationSink` +
    /// `attestHead` / `verifyAttestation` and the typed-`Actor`-in-hash posture (Phase 320). Over a
    /// seed-replayable sample of chains built from `gen` under a supplied `StreamWitness` + test sink,
    /// certifies the three-stage guarantee **integrity → attestation → deterministic replay**:
    ///
    ///  - **checkpoint round-trip** — a signed head verifies against the chain it was taken over
    ///    (`verifyAttestation sink (attestHead sink recs) recs`);
    ///  - **prefix attestation** — a head signed over the length-`n` prefix verifies against exactly that
    ///    prefix and NOT against a different-length one: one signature is bound to the whole prefix state
    ///    (O(commits), not O(ops) — the hash-chain already folds every prior op into the head);
    ///  - **replay-equivalence** — `replay` of the attested op log reproduces the exact live state the
    ///    signed head was taken over, so that state is provably the deterministic replay of its log;
    ///  - **falsification** — an op-tamper AND an actor-re-attribution, each *rehashed under the same
    ///    `HashFn` so `verifyChain` re-accepts the forged chain* (the plain hash-chain defence defeated),
    ///    are still caught by `verifyAttestation`: the forgery moved the head, and the signature covers
    ///    only the original one. This is exactly what attestation adds over a bare hash chain — and it
    ///    holds under a *cryptographic* `HashFn` too (run the kit with the keyed / wide stand-in), since
    ///    a re-hashed forgery cannot be re-signed without the host key.
    ///
    /// The `noAttestation` default makes every branch **vacuous** (`Sign ⇒ None ⇒` nothing to verify or
    /// falsify), so adopting the kit never forces a sink on a host — see `noAttestationVacuityLaws`.
    /// Opt-in like `snapshotLaws` / `dagLaws`. `'State` needs equality (replay-equivalence).
    let attestationLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (sink: IAttestationSink)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable roundTrip = None
        let mutable prefix = None
        let mutable replayEq = None
        let mutable opTamper = None
        let mutable actorTamper = None

        for i in 0 .. iterations - 1 do
            let mutable state = gen.State0
            let mutable recs = OpStream.empty

            for _ in 0..5 do
                let op, r' = gen.Op rng
                rng <- r'

                match OpStream.append hashFn sw (Human "conf") op state recs with
                | Ok(s', recs') ->
                    state <- s'
                    recs <- recs'
                | Error _ -> ()

            // checkpoint round-trip: a signed head verifies against its own chain (vacuous under noAttestation).
            (match OpStream.attestHead sink recs with
             | Some att ->
                 if not (OpStream.verifyAttestation sink att recs) && roundTrip.IsNone then
                     roundTrip <- Some(sprintf "seed=%d iter=%d: a signed head failed verifyAttestation" seed i)
             | None -> ())

            // prefix attestation: a head signs its whole prefix — the signature is bound to exactly that
            // prefix and not to a different-length one (whose head, folding a different op set, differs).
            let len = List.length recs
            let n, r2 = ConfRng.intBelow (len + 1) rng
            rng <- r2
            let pfx = recs |> List.truncate n

            (match OpStream.attestHead sink pfx with
             | Some attN ->
                 if not (OpStream.verifyAttestation sink attN pfx) && prefix.IsNone then
                     prefix <-
                         Some(sprintf "seed=%d iter=%d: a prefix head failed to verify against its own prefix" seed i)

                 let n2 = if n = len then max 0 (n - 1) else len
                 let pfx2 = recs |> List.truncate n2

                 if
                     n2 <> n
                     && OpStream.head pfx2 <> OpStream.head pfx
                     && OpStream.verifyAttestation sink attN pfx2
                     && prefix.IsNone
                 then
                     prefix <-
                         Some(sprintf "seed=%d iter=%d: a prefix signature covered a different-length prefix" seed i)
             | None -> ())

            // replay-equivalence: the attested op log replays to the live state the head was taken over.
            (match OpStream.replay sw gen.State0 recs with
             | Ok s when s = state -> ()
             | other ->
                 if replayEq.IsNone then
                     replayEq <-
                         Some(sprintf "seed=%d iter=%d: replay of the attested log ≠ live state (got %A)" seed i other))

            // falsification — only when the sink actually signs (vacuous under noAttestation).
            match recs, OpStream.attestHead sink recs with
            | [], _
            | _, None -> ()
            | _, Some att ->
                // op-tamper + full rehash: verifyChain re-accepts, verifyAttestation must reject.
                let tIdx, r3 = ConfRng.intBelow (List.length recs) rng
                let newOp, r4 = gen.Op r3
                rng <- r4
                let orig = List.item tIdx recs

                if sw.Encode orig.Op <> sw.Encode newOp then
                    let tampered =
                        recs |> List.mapi (fun j r -> if j = tIdx then { r with Op = newOp } else r)

                    let forged = reforgeCanonical hashFn sw.Encode tampered

                    if
                        OpStream.verifyChain hashFn sw forged
                        && OpStream.verifyAttestation sink att forged
                        && opTamper.IsNone
                    then
                        opTamper <-
                            Some(sprintf "seed=%d iter=%d: a rehashed op-forgery passed verifyAttestation" seed i)

                // actor-tamper + full rehash: re-attribution moves the head (the actor is in the hash since 320).
                let aIdx, r5 = ConfRng.intBelow (List.length recs) rng
                rng <- r5
                let origA = List.item aIdx recs

                let newActor =
                    match origA.Actor with
                    | Human _ -> Agent("m", "v", "mallory")
                    | Agent _ -> Human "mallory"

                let reattr =
                    recs
                    |> List.mapi (fun j r -> if j = aIdx then { r with Actor = newActor } else r)

                let forgedA = reforgeCanonical hashFn sw.Encode reattr

                if
                    OpStream.verifyChain hashFn sw forgedA
                    && OpStream.verifyAttestation sink att forgedA
                    && actorTamper.IsNone
                then
                    actorTamper <-
                        Some(sprintf "seed=%d iter=%d: a rehashed actor-re-attribution passed verifyAttestation" seed i)

        [ { Law = "attestation checkpoint round-trip (a signed head verifies against its chain)"
            Passed = roundTrip.IsNone
            Counterexample = roundTrip }
          { Law = "attestation is bound to its exact prefix (a head attests its whole prefix — O(commits))"
            Passed = prefix.IsNone
            Counterexample = prefix }
          { Law = "replay-equivalence (replay of the attested log = the live state the head was taken over)"
            Passed = replayEq.IsNone
            Counterexample = replayEq }
          { Law = "attestation catches a rehashed op-forgery (verifyChain re-accepts; verifyAttestation rejects)"
            Passed = opTamper.IsNone
            Counterexample = opTamper }
          { Law = "attestation catches a rehashed actor-re-attribution (attribution is inside the hash)"
            Passed = actorTamper.IsNone
            Counterexample = actorTamper } ]

    /// The `noAttestation` vacuity laws (Phase 60) — the default no-op sink issues no attestation
    /// (`attestHead noAttestation ⇒ None`) and verifies nothing (`verifyAttestation noAttestation _ ⇒
    /// false`), and — because attestation is a read-only side-band — the chain is byte-identical whether
    /// or not a host ever attests. So adopting the seam is free: no sink ⇒ exactly the pre-attestation
    /// path, and `attestationLaws OpStream.noAttestation` passes vacuously. Self-contained over a supplied
    /// witness + stream generator; `'State` is not compared.
    let noAttestationVacuityLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable noSign = None
        let mutable noVerify = None
        let mutable unchanged = None

        for i in 0 .. iterations - 1 do
            let mutable state = gen.State0
            let mutable recs = OpStream.empty

            for _ in 0..5 do
                let op, r' = gen.Op rng
                rng <- r'

                match OpStream.append hashFn sw (Human "conf") op state recs with
                | Ok(s', recs') ->
                    state <- s'
                    recs <- recs'
                | Error _ -> ()

            if OpStream.attestHead OpStream.noAttestation recs <> None && noSign.IsNone then
                noSign <- Some(sprintf "seed=%d iter=%d: noAttestation issued an attestation" seed i)

            // even a well-formed attestation over the real head is rejected by the no-op sink.
            let plausible: Attestation =
                { Head = OpStream.head recs
                  KeyId = "k"
                  Signature = "sig" }

            if
                OpStream.verifyAttestation OpStream.noAttestation plausible recs
                && noVerify.IsNone
            then
                noVerify <- Some(sprintf "seed=%d iter=%d: noAttestation verified an attestation" seed i)

            // attesting is read-only: the head is stable and the chain still verifies across an attest call.
            let before = OpStream.head recs
            OpStream.attestHead OpStream.noAttestation recs |> ignore

            if
                (OpStream.head recs <> before || not (OpStream.verifyChain hashFn sw recs))
                && unchanged.IsNone
            then
                unchanged <- Some(sprintf "seed=%d iter=%d: attesting altered the un-attested chain" seed i)

        [ { Law = "noAttestation issues no attestation (Sign ⇒ None)"
            Passed = noSign.IsNone
            Counterexample = noSign }
          { Law = "noAttestation verifies nothing (Verify ⇒ false)"
            Passed = noVerify.IsNone
            Counterexample = noVerify }
          { Law = "attesting leaves the un-attested chain unchanged (read-only side-band)"
            Passed = unchanged.IsNone
            Counterexample = unchanged } ]

    /// The pluggable-`HashFn` parity laws (Phase 65) — the teeth on the `HashFn` seam + `verifyChain`,
    /// certifying that a chain hash is a pure function of the canonical wire pre-image (the cross-host
    /// parity contract STABILITY.md states). Over a seed-replayable sample of chains built from `gen`
    /// under a supplied `HashFn`:
    ///
    ///  - **determinism** — the same op sequence produces the identical chain across independent builds
    ///    (no clock / culture leakage in the pre-image; the Phase-55 canonical-float discipline);
    ///  - **pre-image parity** — a chain built incrementally (`append`) and one rebuilt in bulk from the
    ///    same `(seq, actor, op)` pre-images agree hash-for-hash, so the hash keys on the canonical
    ///    pre-image and NOTHING about the construction path — the foundation of cross-host parity (two
    ///    hosts on the same `HashFn` + same pre-image get byte-identical chains);
    ///  - **tamper-detection** — a reorder, a dropped link, and a bit-flip are each caught by
    ///    `verifyChain` under the supplied fn.
    ///
    /// The crypto posture (a re-hashed forgery is caught under a collision-resistant fn but not under the
    /// default FNV-1a) is a separate branch — see `hashFnAdversarialLaws`. `'State` is not compared.
    let hashFnLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let chainHashes (rs: OpRecord<'Op> list) = rs |> List.map (fun r -> r.Hash)

        // build a chain from `startRng`, returning the chain + the advanced rng (deterministic).
        let build (startRng: ConfRng.T) : OpRecord<'Op> list * ConfRng.T =
            let mutable rng = startRng
            let mutable state = gen.State0
            let mutable recs = OpStream.empty

            for _ in 0..5 do
                let op, r' = gen.Op rng
                rng <- r'

                match OpStream.append hashFn sw (Human "conf") op state recs with
                | Ok(s', recs') ->
                    state <- s'
                    recs <- recs'
                | Error _ -> ()

            recs, rng

        let mutable rng = ConfRng.ofSeed seed
        let mutable determinism = None
        let mutable parity = None
        let mutable tamper = None

        for i in 0 .. iterations - 1 do
            let recs, rngA = build rng
            let recs2, _ = build rng // same start ⇒ identical chain (determinism)
            rng <- rngA

            if chainHashes recs <> chainHashes recs2 && determinism.IsNone then
                determinism <- Some(sprintf "seed=%d iter=%d: the same op sequence hashed to a different chain" seed i)

            // pre-image parity: an incremental build and a bulk reforge of the same pre-images agree.
            let reforged = reforgeCanonical hashFn sw.Encode recs

            if chainHashes recs <> chainHashes reforged && parity.IsNone then
                parity <-
                    Some(
                        sprintf
                            "seed=%d iter=%d: incremental and bulk builds disagreed (hash keys on more than the pre-image)"
                            seed
                            i
                    )

            // tamper-detection under the supplied fn: reorder (need ≥2) / drop (need ≥3) + bit-flip (need ≥1).
            let len = List.length recs

            if len >= 2 then
                let swapped =
                    recs
                    |> List.mapi (fun j r ->
                        if j = 0 then List.item 1 recs
                        elif j = 1 then List.item 0 recs
                        else r)

                if OpStream.verifyChain hashFn sw swapped && tamper.IsNone then
                    tamper <- Some(sprintf "seed=%d iter=%d: a reordered chain passed verifyChain" seed i)

            // The dropped record must be INTERIOR (needs ≥3): dropping the tail record is a
            // truncation, and a hash chain authenticates prefixes — every truncation IS a valid
            // shorter chain, undetectable by chain verification alone (by design; pinning a head
            // against truncation is the attestation seam's job, Phase 320). At len = 2 the only
            // index-1 drop is exactly that tail truncation, so this branch would assert something
            // cryptographically impossible. Surfaced by the UI adoption's rejection-heavy stream
            // generator, which legitimately builds 2-record chains (2026-07-05).
            if len >= 3 then
                let dropped =
                    recs
                    |> List.mapi (fun j r -> j, r)
                    |> List.filter (fun (j, _) -> j <> 1)
                    |> List.map snd

                if OpStream.verifyChain hashFn sw dropped && tamper.IsNone then
                    tamper <- Some(sprintf "seed=%d iter=%d: a chain with a dropped link passed verifyChain" seed i)

            if len >= 1 then
                let tIdx, r2 = ConfRng.intBelow len rng
                rng <- r2
                let victim = List.item tIdx recs

                let flippedHash =
                    if victim.Hash.Length = 0 then
                        "x"
                    else
                        let c = victim.Hash.[0]
                        let c' = if c = '0' then '1' else '0'
                        string c' + victim.Hash.Substring(1)

                if flippedHash <> victim.Hash then
                    let flipped =
                        recs
                        |> List.mapi (fun j r -> if j = tIdx then { r with Hash = flippedHash } else r)

                    if OpStream.verifyChain hashFn sw flipped && tamper.IsNone then
                        tamper <- Some(sprintf "seed=%d iter=%d: a bit-flipped hash passed verifyChain" seed i)

        [ { Law = "hash determinism (the same op sequence hashes to the same chain across builds)"
            Passed = determinism.IsNone
            Counterexample = determinism }
          { Law = "pre-image parity (chain = f(canonical pre-image) only — incremental and bulk builds agree)"
            Passed = parity.IsNone
            Counterexample = parity }
          { Law = "tamper-detection (reorder / drop / bit-flip caught by verifyChain under the supplied HashFn)"
            Passed = tamper.IsNone
            Counterexample = tamper } ]

    /// The `HashFn` crypto-posture law (Phase 65) — pins the documented *"the default FNV-1a is not
    /// cryptographic; supply a collision-resistant `HashFn` for adversarial tamper-evidence"* contract
    /// (STABILITY.md "Hash-chain integrity posture"). A *re-hashed forgery* — rewriting history and
    /// recomputing every hash — is always internally consistent, so `verifyChain` re-accepts it; the only
    /// thing between an adversary and a forged chain that still matches an externally-committed head is
    /// the hash's **collision resistance**. This law exhibits that difference directly, at the `HashFn`
    /// level, over a deterministic pre-image enumeration:
    ///
    ///  - the supplied `cryptoHf` (a collision-resistant, wide stand-in) admits **no** pre-image
    ///    collision within `budget` — a forger cannot land a chosen head, so a re-hashed forgery is
    ///    caught (its head moves);
    ///  - `OpStream.defaultHash` (32-bit FNV-1a) **does** admit a collision within the same budget —
    ///    two distinct pre-images share a chain hash, the forgery primitive the posture documents.
    ///
    /// The law PASSES when the crypto stand-in resists and the default admits (the documented posture);
    /// it FAILS only on a regression (the default silently widened, or the stand-in collided in-budget).
    /// No cryptographic hash ships in Core — `cryptoHf` is a host-side / test stand-in (GP3).
    let hashFnAdversarialLaws (cryptoHf: HashFn) (budget: int) (seed: int) : LawResult list =
        // The first two distinct pre-images sharing a hash — the re-hashed-forgery primitive.
        let firstCollision (hf: HashFn) : (string * string) option =
            let seen = System.Collections.Generic.Dictionary<string, string>()
            let mutable found = None
            let mutable k = 0

            while found.IsNone && k < budget do
                let payload = "forge-" + string (seed + k)
                let h = hf "" payload

                match seen.TryGetValue h with
                | true, prior when prior <> payload -> found <- Some(prior, payload)
                | true, _ -> ()
                | false, _ -> seen.[h] <- payload

                k <- k + 1

            found

        let resist =
            match firstCollision cryptoHf with
            | None -> None
            | Some(a, b) ->
                Some(
                    sprintf
                        "seed=%d: the crypto stand-in collided in-budget (%s / %s) — too weak for the posture"
                        seed
                        a
                        b
                )

        let admit =
            match firstCollision OpStream.defaultHash with
            | Some(a, b) when a <> b && OpStream.defaultHash "" a = OpStream.defaultHash "" b -> None
            | Some(a, b) -> Some(sprintf "seed=%d: an FNV-1a 'collision' did not check out (%s / %s)" seed a b)
            | None ->
                Some(
                    sprintf
                        "seed=%d: no FNV-1a collision within budget=%d — the default may have silently widened (posture regression)"
                        seed
                        budget
                )

        [ { Law = "a collision-resistant HashFn resists a re-hashed forgery (no in-budget pre-image collision)"
            Passed = resist.IsNone
            Counterexample = resist }
          { Law =
              "the default FNV-1a admits a re-hashed forgery (an in-budget collision — documented non-crypto posture)"
            Passed = admit.IsNone
            Counterexample = admit } ]

    /// The attributed-stream lift laws (Phase 81) — the teeth on `OpStream.Attributed.liftWitness` and
    /// the "attribution rides inside the chained op, so the hash chain covers it" claim. Over a
    /// seed-replayable sample it builds two parallel chains from the same op sequence — a bare inner
    /// chain and an attributed chain through the lifted witness (each op wrapped in a synthesized
    /// actor/session/turn/timestamp envelope) — and certifies:
    ///
    ///  - **lift preserves replay** — the attributed stream replays to exactly the state the inner ops
    ///    replay to (attribution is provenance, never state — `Apply` delegates to the inner reducer);
    ///  - **the chain covers attribution** — re-attributing a chained op (mutating its actor field)
    ///    breaks `verifyChain`, because the envelope rides inside the hashed op encoding: attribution is
    ///    tamper-evident on the same footing as op-tampering, with no new witness field (GP2);
    ///  - **envelope round-trip** — the attributed chain survives `toJsonl` → `fromJsonl` byte-for-byte
    ///    (the envelope codec is exercised through the real persistence path) and still `verifyChain`s.
    ///
    /// No new witness field — the lift is a derived value over the existing `StreamWitness`. `'State`
    /// and `'Op` need equality (replay + round-trip comparison). Opt-in like `snapshotLaws` / `dagLaws`.
    let attributedLaws
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let lifted = OpStream.Attributed.liftWitness w
        let mutable rng = ConfRng.ofSeed seed
        let mutable replayCx = None
        let mutable tamperCx = None
        let mutable roundTripCx = None

        for i in 0 .. iterations - 1 do
            let mutable innerState = gen.State0
            let mutable innerRecs = OpStream.empty
            let mutable attrState = gen.State0
            let mutable attrRecs = OpStream.empty

            for j in 0..5 do
                let op, r1 = gen.Op rng
                let ab, r2 = ConfRng.intBelow 3 r1
                let sb, r3 = ConfRng.intBelow 2 r2
                let tb, r4 = ConfRng.intBelow 4 r3
                rng <- r4

                let attr: Attributed<'Op> =
                    { Actor = "actor-" + string ab
                      Session = "sess-" + string sb
                      Turn = (if tb = 0 then None else Some tb)
                      At = "t" + string j
                      Op = op }

                // The SAME op is appended to both chains; the lifted Apply delegates to the inner Apply on
                // .Op, so both accept/reject identically and stay structurally parallel.
                match OpStream.append hashFn w (Human "conf") op innerState innerRecs with
                | Ok(s, rs) ->
                    innerState <- s
                    innerRecs <- rs
                | Error _ -> ()

                match OpStream.append hashFn lifted (Human "conf") attr attrState attrRecs with
                | Ok(s, rs) ->
                    attrState <- s
                    attrRecs <- rs
                | Error _ -> ()

            // replay parity: the attributed stream replays to the inner-op state (from origin AND live).
            match OpStream.replay lifted gen.State0 attrRecs, OpStream.replay w gen.State0 innerRecs with
            | Ok a, Ok b when a = b && a = attrState && b = innerState -> ()
            | other ->
                if replayCx.IsNone then
                    replayCx <- Some(sprintf "seed=%d iter=%d: attributed replay ≠ inner replay (%A)" seed i other)

            // chain covers attribution: mutate a chained op's actor field ⇒ verifyChain must reject.
            match attrRecs with
            | [] -> ()
            | _ ->
                let tIdx, r5 = ConfRng.intBelow (List.length attrRecs) rng
                rng <- r5

                let tampered =
                    attrRecs
                    |> List.mapi (fun k r ->
                        if k = tIdx then
                            { r with
                                Op = { r.Op with Actor = r.Op.Actor + "~" } }
                        else
                            r)

                if OpStream.verifyChain hashFn lifted tampered && tamperCx.IsNone then
                    tamperCx <- Some(sprintf "seed=%d iter=%d: re-attributing a chained op was not detected" seed i)

            // envelope round-trip: the attributed chain survives toJsonl/fromJsonl byte-for-byte + verifies.
            match OpStream.toJsonl lifted attrRecs |> OpStream.fromJsonl lifted with
            | Ok restored when restored = attrRecs && OpStream.verifyChain hashFn lifted restored -> ()
            | other ->
                if roundTripCx.IsNone then
                    roundTripCx <-
                        Some(sprintf "seed=%d iter=%d: attributed JSONL round-trip ≠ original (%A)" seed i other)

        [ { Law = "attributed lift preserves replay (a lifted stream replays to its inner-op state)"
            Passed = replayCx.IsNone
            Counterexample = replayCx }
          { Law = "the chain covers attribution (re-attributing a chained op breaks verifyChain)"
            Passed = tamperCx.IsNone
            Counterexample = tamperCx }
          { Law = "attribution envelope round-trips through JSONL (byte-identical + still verifies)"
            Passed = roundTripCx.IsNone
            Counterexample = roundTripCx } ]

    /// The op-script footprint / independence laws (Phase 78) — the teeth on `Ops.footprint` /
    /// `Ops.independent` and the "the edge is computed from the script" claim. Over a seed-replayable
    /// sample it builds two applyable scripts `a`, `b` on a shared tree (each threaded from random
    /// accepted ops) and certifies the conservativity contract:
    ///
    ///  - **soundness** — for every pair `footprint` declares **independent**, the two scripts commute
    ///    under `apply`: `applyAll a` then `applyAll b` equals `applyAll b` then `applyAll a`, compared by
    ///    the Phase-06 content hash (`Tree.encodeHash`), and neither ordering fails. `independent = true`
    ///    is a promise — a single non-commuting independent pair is a soundness break (the over-approx
    ///    leaked to under-approx);
    ///  - **monotonicity** — a sub-script's footprint ⊆ its script's, across all four address sets
    ///    (footprint is a union-fold, so a peephole or reorder that broke it would surface here);
    ///  - **determinism** — `footprint` is a pure function of the script (recomputing it agrees), the
    ///    precondition for seed-replay and for a host to cache a computed lease.
    ///
    /// `'Node` needs equality (it compares result trees). `encode` is the per-node canonical content
    /// encoder for the content-hash equality (the same one a domain feeds `Tree.encodeHash` /
    /// `Function.applyMemo`). Opt-in like `normalizeLaws` — a domain that computes leases runs it.
    let footprintLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let hashOf = Tree.encodeHash nodew encode
        let mutable rng = ConfRng.ofSeed seed
        let mutable soundness = None
        let mutable monotonicity = None
        let mutable determinism = None

        // Thread up to `n` random ops through `apply`, keeping the accepted ones — an applyable script.
        let collectScript n (tree: 'Node) (r0: ConfRng.T) =
            let mutable cur = tree
            let mutable accepted = []
            let mutable r = r0

            for _ in 1..n do
                let op, r' = genOp nodew idw gen cur r
                r <- r'

                match Ops.applyContained canHold nodew idw op cur with
                | Ok t' ->
                    cur <- t'
                    accepted <- accepted @ [ op ]
                | Error _ -> ()

            accepted, r

        let subsetFp (s: Footprint) (f: Footprint) =
            Set.isSubset s.Reads f.Reads
            && Set.isSubset s.StructureWrites f.StructureWrites
            && Set.isSubset s.ContentWrites f.ContentWrites
            && Set.isSubset s.UnknownParentWrites f.UnknownParentWrites

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen.Tree rng
            let a, r2 = collectScript 4 tree r1
            let b, r3 = collectScript 4 tree r2
            rng <- r3

            let fa = Ops.footprint nodew idw a
            let fb = Ops.footprint nodew idw b

            // determinism: footprint is a pure function of the script.
            if Ops.footprint nodew idw a <> fa && determinism.IsNone then
                determinism <- Some(sprintf "seed=%d iter=%d: footprint is not a pure function of the script" seed i)

            // monotonicity: a prefix's footprint ⊆ the full script's.
            let k, r4 = ConfRng.intBelow (List.length a + 1) rng
            rng <- r4
            let prefix = List.truncate k a

            if not (subsetFp (Ops.footprint nodew idw prefix) fa) && monotonicity.IsNone then
                monotonicity <- Some(sprintf "seed=%d iter=%d: a %d-op prefix footprint ⊄ the full footprint" seed i k)

            // soundness: an independent pair must commute under apply (content-hash equality).
            if Ops.independent fa fb then
                let applyAll ops t = Ops.applyAll nodew idw ops t

                let ab =
                    applyAll a tree |> Result.bind (fun ta -> applyAll b ta |> Result.map hashOf)

                let ba =
                    applyAll b tree |> Result.bind (fun tb -> applyAll a tb |> Result.map hashOf)

                match ab, ba with
                | Ok ha, Ok hb when ha = hb -> ()
                | _ ->
                    if soundness.IsNone then
                        soundness <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: an INDEPENDENT pair did not commute under apply (a=%A b=%A ab=%A ba=%A)"
                                    seed
                                    i
                                    a
                                    b
                                    ab
                                    ba
                            )

        [ { Law = "footprint soundness (an independent pair commutes under apply — content-hash equal)"
            Passed = soundness.IsNone
            Counterexample = soundness }
          { Law = "footprint monotonicity (a sub-script's footprint ⊆ its script's)"
            Passed = monotonicity.IsNone
            Counterexample = monotonicity }
          { Law = "footprint determinism (a pure function of the script)"
            Passed = determinism.IsNone
            Counterexample = determinism } ]

    /// The merge-conflict enumeration laws (Phase 64) — the teeth on `Dag.conflicts` and the
    /// "detection is the negation of #78 independence, decomposed by shape" claim. Over a
    /// seed-replayable sample it builds two applyable scripts `a`, `b` on a shared tree (each threaded
    /// through `apply`, exactly as `footprintLaws` does — so an op is a single-op footprint) and
    /// certifies:
    ///
    ///  - **symmetry** — `conflicts a b` and `conflicts b a` report the same collisions up to swapping
    ///    each pair's `Left`/`Right`: a merge conflict is not directional;
    ///  - **determinism** — `conflicts` is a pure function of its inputs (recomputing agrees), the
    ///    precondition for seed-replay;
    ///  - **agreement with #78 (completeness)** — a pair `(aᵢ, bⱼ)` is reported **iff** its footprints
    ///    are not `Ops.independent`. Grounded in #78's certified soundness this is exactly "two ops that
    ///    would interfere are reported; two that provably commute are not": an independent pair commutes
    ///    (`footprintLaws`) and is never reported, and a reported pair is genuinely dependent.
    ///
    /// Mirrors `footprintLaws`' signature (no `encode` — `conflicts` compares addresses, not trees).
    /// `'Node` needs equality (it compares reported op pairs). A domain that merges branches runs it.
    let mergeConflictLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let fp (op: SkeletonOp<'Node, 'Id>) = Ops.footprint nodew idw [ op ]
        let mutable rng = ConfRng.ofSeed seed
        let mutable symmetryCx = None
        let mutable determinismCx = None
        let mutable agreementCx = None

        // Thread up to `n` random ops through `apply`, keeping the accepted ones — an applyable script.
        let collectScript n (tree: 'Node) (r0: ConfRng.T) =
            let mutable cur = tree
            let mutable accepted = []
            let mutable r = r0

            for _ in 1..n do
                let op, r' = genOp nodew idw gen cur r
                r <- r'

                match Ops.applyContained canHold nodew idw op cur with
                | Ok t' ->
                    cur <- t'
                    accepted <- accepted @ [ op ]
                | Error _ -> ()

            accepted, r

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen.Tree rng
            let a, r2 = collectScript 4 tree r1
            let b, r3 = collectScript 4 tree r2
            rng <- r3

            let ab = Dag.conflicts fp a b

            // determinism: a pure function of the inputs.
            if Dag.conflicts fp a b <> ab && determinismCx.IsNone then
                determinismCx <- Some(sprintf "seed=%d iter=%d: conflicts is not a pure function of its inputs" seed i)

            // symmetry: conflicts a b ≡ conflicts b a up to each pair's Left/Right swap.
            let ba = Dag.conflicts fp b a

            let norm (swap: bool) (c: MergeConflict<SkeletonOp<'Node, 'Id>>) =
                if swap then
                    (c.Shape, c.Address, c.Right, c.Left)
                else
                    (c.Shape, c.Address, c.Left, c.Right)

            let fwd = ab |> List.map (norm false)
            let bwd = ba |> List.map (norm true)

            // Multiset equality by counting (only equality on `'Node` is demanded, not comparison).
            let sameMultiset (xs: _ list) (ys: _ list) =
                List.length xs = List.length ys
                && xs
                   |> List.forall (fun x ->
                       (xs |> List.filter ((=) x) |> List.length) = (ys |> List.filter ((=) x) |> List.length))

            if not (sameMultiset fwd bwd) && symmetryCx.IsNone then
                symmetryCx <- Some(sprintf "seed=%d iter=%d: conflicts a b ≠ conflicts b a (up to pair swap)" seed i)

            // agreement with #78: a pair is reported iff its footprints are not independent.
            for oa in a do
                for ob in b do
                    let reported = Dag.conflicts fp [ oa ] [ ob ] |> List.isEmpty |> not
                    let dependent = not (Ops.independent (fp oa) (fp ob))

                    if reported <> dependent && agreementCx.IsNone then
                        agreementCx <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: reported=%b but not-independent=%b for (%A, %A)"
                                    seed
                                    i
                                    reported
                                    dependent
                                    oa
                                    ob
                            )

        [ { Law = "conflicts is symmetric (conflicts a b ≡ conflicts b a up to Left/Right swap)"
            Passed = symmetryCx.IsNone
            Counterexample = symmetryCx }
          { Law = "conflicts is deterministic (a pure function of its inputs)"
            Passed = determinismCx.IsNone
            Counterexample = determinismCx }
          { Law = "conflicts agrees with #78 (a pair is reported iff its footprints are not independent)"
            Passed = agreementCx.IsNone
            Counterexample = agreementCx } ]

    /// The branch-reconciliation laws (Phase 83) — the teeth on `Dag.reconcile` and its "fold what
    /// commutes, hand conflicts back untouched" contract (GP6). Over a seed-replayable sample it builds
    /// a fork DAG (a common base + two independent branch deltas of accepted ops) and certifies:
    ///
    ///  - **clean-merge replay** — when `reconcile = Ok script`, the script applied to the base replays
    ///    to the SAME tree (Phase-06 content hash) as delta A then delta B AND as delta B then delta A:
    ///    a conflict-free merge folds order-independently (the pin is canonical form, not semantics);
    ///  - **footprint cross-validation (#78)** — footprint-independent deltas are always conflict-free
    ///    (`independent ⇒ reconcile = Ok`); the converse is not claimed (footprints over-approximate);
    ///  - **conflicted path is inert** — when the deltas conflict, `reconcile = Error` carrying exactly
    ///    `Dag.conflicts`' report, and nothing is applied (GP6 — no winner, no partial merge);
    ///  - **determinism / order pinning** — `reconcile` is a pure function of `(base, headA, headB)`,
    ///    and the clean script is `betweenOps base headA ++ betweenOps base headB`.
    ///
    /// `'Node` needs equality. `encode` is the per-node content encoder (as `footprintLaws`). Mirrors
    /// `footprintLaws` — a domain that reconciles branches runs it.
    let reconcileLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let hashOf = Tree.encodeHash nodew encode
        let fp (op: SkeletonOp<'Node, 'Id>) = Ops.footprint nodew idw [ op ]
        let hashFn = OpStream.defaultHash

        // A minimal StreamWitness so the branch deltas live in a REAL DAG (append needs Encode for the
        // content id; reconcile/betweenOps never call Apply or Decode). Encode is a structural
        // fingerprint of the op — enough for distinct nodes to get distinct content ids.
        let rec encOp (op: SkeletonOp<'Node, 'Id>) : string =
            match op with
            | InsertChild(p, node) ->
                "I|"
                + idw.ToString p
                + "|"
                + (Tree.preorder nodew node |> List.map encode |> String.concat ",")
            | RemoveNode t -> "R|" + idw.ToString t
            | MoveNode(t, np) -> "M|" + idw.ToString t + "|" + idw.ToString np
            | ReorderChildren(p, order) ->
                "O|"
                + idw.ToString p
                + "|"
                + (order |> List.map idw.ToString |> String.concat ",")
            | Batch inner -> "B|" + (inner |> List.map encOp |> String.concat ";")

        let sw: StreamWitness<SkeletonOp<'Node, 'Id>, 'Node, Rejection<'Id>> =
            { Apply = fun op st -> Ops.applyContained canHold nodew idw op st
              Encode = encOp
              Decode = fun _ -> Error "reconcileLaws: decode unused" }

        let applyAll (ops: SkeletonOp<'Node, 'Id> list) (t: 'Node) =
            ops
            |> List.fold (fun acc op -> acc |> Result.bind (fun s -> Ops.applyContained canHold nodew idw op s)) (Ok t)

        let mutable rng = ConfRng.ofSeed seed
        let mutable cleanCx = None
        let mutable crossCx = None
        let mutable conflictedCx = None
        let mutable determinismCx = None

        let collectScript n (tree: 'Node) (r0: ConfRng.T) =
            let mutable cur = tree
            let mutable accepted = []
            let mutable r = r0

            for _ in 1..n do
                let op, r' = genOp nodew idw gen cur r
                r <- r'

                match Ops.applyContained canHold nodew idw op cur with
                | Ok t' ->
                    cur <- t'
                    accepted <- accepted @ [ op ]
                | Error _ -> ()

            accepted, r

        // Chain a script onto `parent`, returning the new head (parent itself when the script is empty).
        let chain (ops: SkeletonOp<'Node, 'Id> list) (parent: string) (d0: Dag.T<SkeletonOp<'Node, 'Id>>) =
            let mutable head = parent
            let mutable d = d0

            for op in ops do
                let id, d' = Dag.append hashFn sw (Human "conf") op head d
                head <- id
                d <- d'

            head, d

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen.Tree rng
            let a, r2 = collectScript 4 tree r1
            let b, r3 = collectScript 4 tree r2
            rng <- r3

            // a fork DAG: a genesis base node (its op never participates — it is in the base closure,
            // which betweenOps excludes), then branch A and branch B forked off the base.
            let baseId, d1 =
                Dag.append hashFn sw (Human "conf") (RemoveNode(nodew.Id tree)) "" Dag.empty

            let headA, d2 = chain a baseId d1
            let headB, dag = chain b baseId d2

            let deltaA = Dag.betweenOps dag baseId headA
            let deltaB = Dag.betweenOps dag baseId headB
            let result = Dag.reconcile fp dag baseId headA headB

            // determinism + order pinning: a pure function of (base, headA, headB); clean script pinned.
            if Dag.reconcile fp dag baseId headA headB <> result && determinismCx.IsNone then
                determinismCx <- Some(sprintf "seed=%d iter=%d: reconcile is not a pure function of its inputs" seed i)

            match result with
            | Ok script ->
                if script <> deltaA @ deltaB && determinismCx.IsNone then
                    determinismCx <-
                        Some(sprintf "seed=%d iter=%d: clean script ≠ betweenOps A ++ betweenOps B (order pin)" seed i)

                // clean-merge replay: script ≡ A-then-B ≡ B-then-A on the base tree (content hash).
                let viaScript = applyAll script tree |> Result.map hashOf
                let ab = applyAll deltaA tree |> Result.bind (applyAll deltaB) |> Result.map hashOf
                let ba = applyAll deltaB tree |> Result.bind (applyAll deltaA) |> Result.map hashOf

                match viaScript, ab, ba with
                | Ok hs, Ok hab, Ok hba when hs = hab && hab = hba -> ()
                | _ ->
                    if cleanCx.IsNone then
                        cleanCx <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: a conflict-free merge did not fold order-independently (script=%A ab=%A ba=%A)"
                                    seed
                                    i
                                    viaScript
                                    ab
                                    ba
                            )
            | Error cs ->
                // the conflicted path returns exactly Dag.conflicts' report, nothing applied.
                if cs <> Dag.conflicts fp deltaA deltaB && conflictedCx.IsNone then
                    conflictedCx <- Some(sprintf "seed=%d iter=%d: Error payload ≠ Dag.conflicts report" seed i)

            // footprint cross-validation: footprint-independent deltas ⇒ conflict-free (Ok).
            if Ops.independent (Ops.footprint nodew idw deltaA) (Ops.footprint nodew idw deltaB) then
                match result with
                | Ok _ -> ()
                | Error _ ->
                    if crossCx.IsNone then
                        crossCx <-
                            Some(
                                sprintf "seed=%d iter=%d: footprint-independent deltas were NOT reconciled clean" seed i
                            )

        [ { Law = "reconcile clean fold replays order-independently (content-hash equal)"
            Passed = cleanCx.IsNone
            Counterexample = cleanCx }
          { Law = "reconcile is conflict-free when the deltas are footprint-independent (#78 cross-validation)"
            Passed = crossCx.IsNone
            Counterexample = crossCx }
          { Law = "reconcile hands back Dag.conflicts' report on conflict (nothing applied)"
            Passed = conflictedCx.IsNone
            Counterexample = conflictedCx }
          { Law = "reconcile is deterministic + order-pinned (pure fn of (base, headA, headB))"
            Passed = determinismCx.IsNone
            Counterexample = determinismCx } ]

    // ---- lease strand (Phase 84) ----
    // The teeth on `Fuaran.Core.Lease`: claims over a resource axis with a total apply, a conflict
    // that always names the current holder + overlap (GP5), a chainable/replayable stream, and expiry
    // as a pure function of a host-supplied "now" (time as data — no clock in Core, GP6). Self-contained
    // over the string-resource `IdWitness` instance; seed-replayable.

    /// The lease-strand laws (Phase 84) — over the string-resource instance of `Fuaran.Core.Lease`.
    /// Certifies: `apply` totality (never throws); `canApply` ≡ `apply` (same accept/reject + rejection);
    /// **conflict completeness** (an overlapping active claim by a *different* holder is always a typed
    /// `Conflict` naming the current holder and the overlapping resources); `verifyChain` accepts an
    /// intact lease stream; `replay` re-derives the live state from empty; and **expiry-as-data**
    /// (`Expire now` is a pure function of `now` — a lease survives iff `grantedAt + ttl > now`,
    /// identically on a re-run). FSharp.Core only, seed-replayable.
    let leaseLaws (seed: int) (iterations: int) : LawResult list =
        let idw: IdWitness<string> =
            { ToString = id
              OfString = id
              Equals = (=) }

        let hashFn = OpStream.defaultHash
        let sw = Lease.streamWitnessFor idw

        let holders = [ "h0"; "h1"; "h2" ]
        let resources = [ "r0"; "r1"; "r2"; "r3" ]

        let mutable rng = ConfRng.ofSeed seed
        let mutable totality = None
        let mutable equivalence = None
        let mutable conflictLaw = None
        let mutable verify = None
        let mutable replayLaw = None
        let mutable expiryLaw = None

        // a (possibly-invalid) lease op against the current state
        let genOp (r: ConfRng.T) : LeaseOp<string> * ConfRng.T =
            let kind, r1 = ConfRng.intBelow 3 r

            match kind with
            | 0 ->
                let h, r2 = ConfRng.choose holders r1
                let k, r3 = ConfRng.intBelow 3 r2
                let mutable rs = []
                let mutable rr = r3

                for _ in 0..k do
                    let res, r' = ConfRng.choose resources rr
                    rr <- r'

                    if not (List.contains res rs) then
                        rs <- res :: rs

                let g, r4 = ConfRng.intBelow 100 rr
                let t, r5 = ConfRng.intBelow 50 r4
                Claim(h, rs, int64 g, int64 (t + 1)), r5
            | 1 ->
                let h, r2 = ConfRng.choose holders r1
                Release h, r2
            | _ ->
                let n, r2 = ConfRng.intBelow 200 r1
                Expire(int64 n), r2

        for i in 0 .. iterations - 1 do
            let mutable state = Lease.emptyState<string> ()
            let mutable recs = OpStream.empty

            for _ in 0..8 do
                let op, r' = genOp rng
                rng <- r'

                let applied =
                    try
                        Some(Lease.apply idw op state)
                    with _ ->
                        None

                match applied with
                | None ->
                    if totality.IsNone then
                        totality <- Some(sprintf "seed=%d iter=%d: apply threw on %A" seed i op)
                | Some res ->
                    let chk = Lease.canApply idw op state

                    let equiv =
                        match res, chk with
                        | Ok _, Ok() -> true
                        | Error e1, Error e2 -> e1 = e2
                        | _ -> false

                    if not equiv && equivalence.IsNone then
                        equivalence <- Some(sprintf "seed=%d iter=%d: canApply≠apply on %A" seed i op)

                    match res with
                    | Ok _ ->
                        match OpStream.append hashFn sw (Human "conf") op state recs with
                        | Ok(s', recs') ->
                            state <- s'
                            recs <- recs'
                        | Error _ -> ()
                    | Error _ -> ()

            if not (OpStream.verifyChain hashFn sw recs) && verify.IsNone then
                verify <- Some(sprintf "seed=%d iter=%d: verifyChain rejected an intact lease stream" seed i)

            match OpStream.replay sw (Lease.emptyState<string> ()) recs with
            | Ok s when s = state -> ()
            | other ->
                if replayLaw.IsNone then
                    replayLaw <- Some(sprintf "seed=%d iter=%d: replay ≠ live state (got %A)" seed i other)

            // conflict completeness — grant h0 one resource, then a different holder claiming a superset
            // MUST be rejected naming h0 with that resource in the overlap.
            let resIx, rC = ConfRng.intBelow (List.length resources) rng
            rng <- rC
            let theRes = List.item resIx resources

            match Lease.apply idw (Claim("h0", [ theRes ], 0L, 10L)) (Lease.emptyState<string> ()) with
            | Ok granted ->
                match Lease.apply idw (Claim("h1", [ theRes; "rX" ], 1L, 10L)) granted with
                | Error(Conflict("h0", overlap)) when List.contains theRes overlap -> ()
                | other ->
                    if conflictLaw.IsNone then
                        conflictLaw <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: overlapping claim not rejected naming holder+overlap (got %A)"
                                    seed
                                    i
                                    other
                            )
            | Error e ->
                if conflictLaw.IsNone then
                    conflictLaw <- Some(sprintf "seed=%d iter=%d: base grant unexpectedly rejected (%A)" seed i e)

            // expiry-as-data — a lease granted at 5 with ttl 10 is live at now=14 and gone at now=15.
            match Lease.apply idw (Claim("h2", [ "r0" ], 5L, 10L)) (Lease.emptyState<string> ()) with
            | Ok g ->
                match Lease.apply idw (Expire 14L) g, Lease.apply idw (Expire 15L) g with
                | Ok live, Ok gone when Lease.isHeld "h2" live && not (Lease.isHeld "h2" gone) -> ()
                | _ ->
                    if expiryLaw.IsNone then
                        expiryLaw <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: Expire not a pure function of now (14 keeps / 15 drops)"
                                    seed
                                    i
                            )
            | Error e ->
                if expiryLaw.IsNone then
                    expiryLaw <- Some(sprintf "seed=%d iter=%d: expiry base grant rejected (%A)" seed i e)

        [ { Law = "lease apply totality (never throws)"
            Passed = totality.IsNone
            Counterexample = totality }
          { Law = "lease canApply ≡ apply (accept/reject + rejection)"
            Passed = equivalence.IsNone
            Counterexample = equivalence }
          { Law = "lease conflict completeness (an overlapping active claim is rejected naming the holder + overlap)"
            Passed = conflictLaw.IsNone
            Counterexample = conflictLaw }
          { Law = "verifyChain accepts an intact lease stream (over the lease StreamWitness)"
            Passed = verify.IsNone
            Counterexample = verify }
          { Law = "replay re-derives the live lease state from empty"
            Passed = replayLaw.IsNone
            Counterexample = replayLaw }
          { Law = "expiry-as-data (Expire now is a pure function of now — same inputs ⇒ same state)"
            Passed = expiryLaw.IsNone
            Counterexample = expiryLaw } ]

    /// The compare-and-append (CAS) laws (Phase 79) — certify `OpStream.appendIf` is a sound
    /// optimistic-concurrency primitive over a domain's `StreamWitness`. Three properties:
    /// **match ≡ append** (`appendIf` with the stream's *true* head produces exactly what `append`
    /// produces — same state, same chained records — and forwards a domain rejection as
    /// `AppendRejection.Domain`); **stale rejection** (`appendIf` with a head that does NOT match is
    /// `Error (AppendRejection.StaleHead (expected, actual))` naming the actual head, and the stream is
    /// left unchanged — no partial write); **race serialisation** (two `appendIf` calls that both
    /// captured one base head, committed in either order, yield exactly one success and one `StaleHead`
    /// — the CAS admits a single winner under any serialisation). Seed-replayable; a counterexample
    /// carries the seed + iteration. `'State` / `'Op` / `'Rej` need equality.
    let casLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable matchLaw = None
        let mutable staleLaw = None
        let mutable raceLaw = None
        let actor = Human "conf"

        for i in 0 .. iterations - 1 do
            // Build a random base chain (as streamLaws does) — the CAS is exercised against its head.
            let mutable state = gen.State0
            let mutable recs = OpStream.empty

            for _ in 0..5 do
                let op, r' = gen.Op rng
                rng <- r'

                match OpStream.append hashFn sw actor op state recs with
                | Ok(s', recs') ->
                    state <- s'
                    recs <- recs'
                | Error _ -> ()

            let baseHead = OpStream.head recs

            // ---- match ≡ append ----
            let opM, rM = gen.Op rng
            rng <- rM
            let viaAppend = OpStream.append hashFn sw actor opM state recs
            let viaCas = OpStream.appendIf hashFn sw baseHead actor opM state recs

            let matchOk =
                match viaAppend, viaCas with
                | Ok a, Ok b -> a = b
                | Error e, Error(AppendRejection.Domain e2) -> e = e2
                | _ -> false

            if not matchOk && matchLaw.IsNone then
                matchLaw <- Some(sprintf "seed=%d iter=%d: appendIf(trueHead) ≢ append" seed i)

            // ---- stale head rejects, naming the actual head; stream unchanged ----
            let opS, rS = gen.Op rng
            rng <- rS
            let staleHead = baseHead + "!" // guaranteed ≠ baseHead

            match OpStream.appendIf hashFn sw staleHead actor opS state recs with
            | Error(AppendRejection.StaleHead(expected, actual)) when expected = staleHead && actual = baseHead -> () // recs is an immutable value the caller still holds — there is no partial write
            | other ->
                if staleLaw.IsNone then
                    staleLaw <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: appendIf(staleHead) did not name the actual head (got %A)"
                                seed
                                i
                                other
                        )

            // ---- race: exactly one of two writers off one base head wins, either order ----
            let opA, rA = gen.Op rng
            let opB, rB = gen.Op rA
            rng <- rB

            // A genuine CAS race needs both ops to individually apply against the base — a domain
            // reject is not a CAS outcome, so skip the race check for that iteration.
            match OpStream.append hashFn sw actor opA state recs, OpStream.append hashFn sw actor opB state recs with
            | Ok _, Ok _ ->
                // The first writer commits against the base head → succeeds and advances the chain; the
                // second still holds baseHead as its expectation → StaleHead. Exactly one winner.
                let serialise first second =
                    match OpStream.appendIf hashFn sw baseHead actor first state recs with
                    | Ok(s1, recs1) ->
                        match OpStream.appendIf hashFn sw baseHead actor second s1 recs1 with
                        | Error(AppendRejection.StaleHead _) -> true
                        | _ -> false
                    | _ -> false

                if not (serialise opA opB && serialise opB opA) && raceLaw.IsNone then
                    raceLaw <-
                        Some(
                            sprintf "seed=%d iter=%d: two racing appendIf calls did not admit exactly one winner" seed i
                        )
            | _ -> ()

        [ { Law = "appendIf with the true head ≡ append"
            Passed = matchLaw.IsNone
            Counterexample = matchLaw }
          { Law = "appendIf with a stale head rejects, naming the actual head (stream unchanged)"
            Passed = staleLaw.IsNone
            Counterexample = staleLaw }
          { Law = "two racing appendIf calls off one base admit exactly one winner under any serialisation"
            Passed = raceLaw.IsNone
            Counterexample = raceLaw } ]

    // ---- confluence / interleaving law (Phase 80) ----
    // The coordination claim the agent-fleet substrate rests on: op-scripts `Ops.independent`
    // declares disjoint replay to the same tree under EVERY interleaving of their individual ops —
    // confluence. `footprintLaws` (Phase 78) proves the two whole-script sequential orders commute;
    // concurrent appenders produce arbitrary op-level interleavings, which is what this law samples.
    // Interleavings grow as C(m+n, m), so the law checks a bounded, deterministic, seed-replayable
    // sample (the two sequential extremes + uniform riffles) rather than enumerating.

    /// A uniform random interleaving of two lists, preserving each list's internal order: at every
    /// step the next element is drawn from either remainder with probability proportional to its
    /// length (the classic riffle — uniform over all C(m+n, m) interleavings). Deterministic in the
    /// rng, like `ConfRng.shuffle`.
    let private riffle (xs: 'a list) (ys: 'a list) (r0: ConfRng.T) : 'a list * ConfRng.T =
        let mutable a = xs
        let mutable b = ys
        let mutable acc = []
        let mutable r = r0

        while not (List.isEmpty a) || not (List.isEmpty b) do
            let na = List.length a
            let pick, r' = ConfRng.intBelow (na + List.length b) r
            r <- r'

            if pick < na then
                acc <- List.head a :: acc
                a <- List.tail a
            else
                acc <- List.head b :: acc
                b <- List.tail b

        List.rev acc, r

    /// The confluence / interleaving laws (Phase 80) with an **injectable footprint** — the teeth
    /// seam: a test injects a defective `footprintOf` (one that falsely declares dependent pairs
    /// independent) and watches the law bite. Domains call `concurrencyLaws`, which pins this to
    /// the real `Ops.footprint`.
    ///
    /// Over a seed-replayable sample it builds two applyable scripts `a`, `b` on a shared tree (the
    /// `footprintLaws` construction) and, for every pair `footprintOf` + `Ops.independent` declares
    /// **independent**, checks each sampled interleaving (the two sequential extremes `a @ b` /
    /// `b @ a` plus 8 uniform riffles — the documented bound):
    ///
    ///  - **interleaving totality** — every sampled interleaving applies cleanly (no rejection):
    ///    independence must survive any op-level schedule, not just whole-script sequencing;
    ///  - **confluence** — every sampled interleaving replays to the content-hash-equal tree
    ///    (the Phase-06 encoder hash) of the sequential `a @ b` reference order;
    ///  - **coverage** — the sample exercised at least one non-empty independent pair (a vacuity
    ///    guard: a run whose generator never yields an independent pair certifies nothing, and
    ///    says so instead of reporting a hollow green).
    ///
    /// **Honesty boundary (the Phase 52 discipline): sufficiency, not necessity.** Independence is
    /// *sufficient* for confluence, never *necessary* — a pair NOT declared independent is
    /// **skipped, not asserted** (a dependent pair may or may not commute; the law makes no claim
    /// about it). See STABILITY.md "Confluence / interleaving law".
    let concurrencyLawsWith
        (footprintOf: SkeletonOp<'Node, 'Id> list -> Footprint)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let hashOf = Tree.encodeHash nodew encode
        // The documented bound: 8 sampled riffles + the two sequential extremes per pair.
        let riffleSamples = 8
        let mutable rng = ConfRng.ofSeed seed
        let mutable totality = None
        let mutable confluence = None
        let mutable checkedPairs = 0

        // Thread up to `n` random ops through `apply`, keeping the accepted ones — an applyable script.
        let collectScript n (tree: 'Node) (r0: ConfRng.T) =
            let mutable cur = tree
            let mutable accepted = []
            let mutable r = r0

            for _ in 1..n do
                let op, r' = genOp nodew idw gen cur r
                r <- r'

                match Ops.applyContained canHold nodew idw op cur with
                | Ok t' ->
                    cur <- t'
                    accepted <- accepted @ [ op ]
                | Error _ -> ()

            accepted, r

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen.Tree rng
            let a, r2 = collectScript 4 tree r1
            let b, r3 = collectScript 4 tree r2
            rng <- r3

            if
                not (List.isEmpty a)
                && not (List.isEmpty b)
                && Ops.independent (footprintOf a) (footprintOf b)
            then
                checkedPairs <- checkedPairs + 1

                let mutable interleavings = [ a @ b; b @ a ]

                for _ in 1..riffleSamples do
                    let ops, r' = riffle a b rng
                    rng <- r'
                    interleavings <- ops :: interleavings

                match Ops.applyAll nodew idw (a @ b) tree |> Result.map hashOf with
                | Error rej ->
                    if totality.IsNone then
                        totality <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: the sequential a@b order of an INDEPENDENT pair failed to apply (%A; a=%A b=%A)"
                                    seed
                                    i
                                    rej
                                    a
                                    b
                            )
                | Ok refHash ->
                    for ops in interleavings do
                        match Ops.applyAll nodew idw ops tree with
                        | Error rej ->
                            if totality.IsNone then
                                totality <-
                                    Some(
                                        sprintf
                                            "seed=%d iter=%d: an interleaving of an INDEPENDENT pair failed to apply (%A; a=%A b=%A ops=%A)"
                                            seed
                                            i
                                            rej
                                            a
                                            b
                                            ops
                                    )
                        | Ok t ->
                            if hashOf t <> refHash && confluence.IsNone then
                                confluence <-
                                    Some(
                                        sprintf
                                            "seed=%d iter=%d: an interleaving of an INDEPENDENT pair replayed to a different tree (a=%A b=%A ops=%A)"
                                            seed
                                            i
                                            a
                                            b
                                            ops
                                    )

        [ { Law = "interleaving totality (every sampled interleaving of an independent pair applies cleanly)"
            Passed = totality.IsNone
            Counterexample = totality }
          { Law = "confluence (every sampled interleaving of an independent pair replays content-hash-equal)"
            Passed = confluence.IsNone
            Counterexample = confluence }
          { Law = "coverage (the sample exercised at least one independent pair — vacuity guard)"
            Passed = checkedPairs > 0
            Counterexample =
              if checkedPairs > 0 then
                  None
              else
                  Some(sprintf "seed=%d: %d iterations produced no non-empty independent pair" seed iterations) } ]

    /// The confluence / interleaving laws (Phase 80) pinned to the real `Ops.footprint` — the shape
    /// a domain runs. See `concurrencyLawsWith` for the law text, the sampling bound, and the
    /// sufficiency-not-necessity honesty boundary.
    let concurrencyLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        concurrencyLawsWith (Ops.footprint nodew idw) nodew idw gen encode seed iterations

    // ---- proposal arbitration (Phase 85) ----
    // The teeth on `AiSurface.arbitrate`: a deterministic, total partition of N op-script
    // proposals against one base tree — permutation-invariant, a pairwise-independent accepted
    // set, every rejection typed + actionable (GP5), and the accepted scripts confluent in
    // any order.

    /// The proposal-arbitration laws (Phase 85) — over `AiSurface.arbitrate`. Certifies:
    /// **determinism + permutation invariance** (arbitrating the same proposals shuffled yields
    /// the identical `Arbitration` — the pinned ascending-id order decides, never input order);
    /// **total partition** (every input proposal lands in exactly one of accepted / rejected —
    /// nothing dropped, nothing duplicated); **pairwise independence** (every accepted pair's
    /// footprints are `Ops.independent`); **rejection actionability** (an `Inapplicable`
    /// carries exactly the `Ops.canApplyAll` envelope against the base; a `Conflicts` cites a
    /// non-empty subset of ACCEPTED ids each of which genuinely interferes); and **any-order
    /// confluence** (the accepted scripts apply green in the pinned order, its reverse, and a
    /// random shuffle — all to the same content-hashed tree, and `MergedScript` reproduces it).
    /// The confluence law asserts the whole-script any-order claim directly; `concurrencyLaws`
    /// (Phase 80) is the stronger op-level-interleaving form of the same claim for independent
    /// pairs — a domain that arbitrates runs both. Seed-replayable; `'Node` needs equality.
    /// `encode` is the per-node canonical encoder for the content-hash comparison (as
    /// `footprintLaws`). Opt-in like `footprintLaws` — a domain that arbitrates proposals
    /// runs it.
    let arbitrationLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let hashOf = Tree.encodeHash nodew encode
        let mutable rng = ConfRng.ofSeed seed
        let mutable permutation = None
        let mutable partition = None
        let mutable independence = None
        let mutable actionability = None
        let mutable confluence = None

        // An applyable script: up to `n` random ops threaded from `tree`, keeping the accepted.
        let collectScript n (tree: 'Node) (r0: ConfRng.T) =
            let mutable cur = tree
            let mutable accepted = []
            let mutable r = r0

            for _ in 1..n do
                let op, r' = genOp nodew idw gen cur r
                r <- r'

                match Ops.applyContained canHold nodew idw op cur with
                | Ok t' ->
                    cur <- t'
                    accepted <- accepted @ [ op ]
                | Error _ -> ()

            accepted, r

        let mkProposal id ops : Proposals.Proposal<SkeletonOp<'Node, 'Id>> =
            { Id = id
              Author = sprintf "agent-%d" id
              ProposedAt = "t0"
              Intent = None
              Ops = ops
              Status = Proposals.Pending }

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen.Tree rng
            rng <- r1
            let extra, r2 = ConfRng.intBelow 4 rng
            rng <- r2
            let count = extra + 2 // 2..5 proposals

            // Proposals off one base: applyable scripts (which frequently share parents, so
            // conflicts arise naturally) + ~1-in-4 corrupted into a provably-inapplicable
            // script (an op addressing an id the base tree does not carry).
            let mutable proposals = []

            for k in 1..count do
                let script, r3 = collectScript 3 tree rng
                rng <- r3
                let corrupt, r4 = ConfRng.intBelow 4 rng
                rng <- r4

                let ops =
                    if corrupt = 0 then
                        let baseIds = Tree.ids nodew tree |> List.map idw.ToString |> Set.ofList
                        let ghost, r5 = gen.FreshNode baseIds rng
                        rng <- r5
                        script @ [ RemoveNode(nodew.Id ghost) ]
                    else
                        script

                proposals <- proposals @ [ mkProposal k ops ]

            let result = AiSurface.arbitrate nodew idw tree proposals

            // determinism + permutation invariance: shuffled input ⇒ identical Arbitration.
            let shuffled, rS = ConfRng.shuffle proposals rng
            rng <- rS

            if
                (AiSurface.arbitrate nodew idw tree shuffled <> result
                 || AiSurface.arbitrate nodew idw tree proposals <> result)
                && permutation.IsNone
            then
                permutation <-
                    Some(sprintf "seed=%d iter=%d: arbitrate is not deterministic / permutation-invariant" seed i)

            // total partition: accepted + rejected = input, each exactly once.
            let acceptedIds = result.Accepted |> List.map (fun p -> p.Id)
            let rejectedIds = result.Rejected |> List.map (fun (p, _) -> p.Id)
            let inputIds = proposals |> List.map (fun p -> p.Id) |> List.sort

            if List.sort (acceptedIds @ rejectedIds) <> inputIds && partition.IsNone then
                partition <- Some(sprintf "seed=%d iter=%d: accepted+rejected ≠ input (dropped or duplicated)" seed i)

            // pairwise independence of the accepted set.
            let acceptedFps =
                result.Accepted |> List.map (fun p -> p.Id, Ops.footprint nodew idw p.Ops)

            let pairwise =
                acceptedFps
                |> List.forall (fun (ida, fa) ->
                    acceptedFps |> List.forall (fun (idb, fb) -> ida = idb || Ops.independent fa fb))

            if not pairwise && independence.IsNone then
                independence <- Some(sprintf "seed=%d iter=%d: the accepted set is not pairwise independent" seed i)

            // rejection actionability (GP5).
            for p, reason in result.Rejected do
                match reason with
                | Inapplicable(ix, rej) ->
                    if Ops.canApplyAll nodew idw p.Ops tree <> Error(ix, rej) && actionability.IsNone then
                        actionability <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: Inapplicable ≠ the canApplyAll envelope (proposal %d)"
                                    seed
                                    i
                                    p.Id
                            )
                | Conflicts ids ->
                    let fp = Ops.footprint nodew idw p.Ops

                    let citesInterferingAccepted =
                        not (List.isEmpty ids)
                        && ids
                           |> List.forall (fun cid ->
                               match acceptedFps |> List.tryFind (fun (aid, _) -> aid = cid) with
                               | Some(_, afp) -> not (Ops.independent fp afp)
                               | None -> false)

                    if not citesInterferingAccepted && actionability.IsNone then
                        actionability <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: Conflicts cites a non-accepted or non-interfering id (proposal %d)"
                                    seed
                                    i
                                    p.Id
                            )

            // any-order confluence: pinned, reversed, and shuffled application orders all
            // succeed and agree (content hash) — and MergedScript reproduces the same tree.
            let scripts = result.Accepted |> List.map (fun p -> p.Ops)

            let applyIn (order: SkeletonOp<'Node, 'Id> list list) =
                order
                |> List.fold (fun acc s -> acc |> Result.bind (Ops.applyAll nodew idw s)) (Ok tree)
                |> Result.map hashOf

            let shuffledScripts, rO = ConfRng.shuffle scripts rng
            rng <- rO

            let viaMerged = Ops.applyAll nodew idw result.MergedScript tree |> Result.map hashOf

            match applyIn scripts, applyIn (List.rev scripts), applyIn shuffledScripts, viaMerged with
            | Ok a, Ok b, Ok c, Ok d when a = b && b = c && c = d -> ()
            | _ ->
                if confluence.IsNone then
                    confluence <- Some(sprintf "seed=%d iter=%d: the accepted scripts did not apply confluently" seed i)

        [ { Law = "arbitrate determinism + input-permutation invariance (the pinned order decides)"
            Passed = permutation.IsNone
            Counterexample = permutation }
          { Law = "arbitrate is a total partition (every proposal lands in exactly one bucket)"
            Passed = partition.IsNone
            Counterexample = partition }
          { Law = "the accepted set is pairwise independent (Ops.independent)"
            Passed = independence.IsNone
            Counterexample = independence }
          { Law =
              "every rejection is actionable (Inapplicable = the canApplyAll envelope; Conflicts cites interfering accepted ids)"
            Passed = actionability.IsNone
            Counterexample = actionability }
          { Law = "the accepted scripts apply confluently in any order (the whole-script any-order claim)"
            Passed = confluence.IsNone
            Counterexample = confluence } ]

    // ---- idempotent append (Phase 82) ----
    // The at-least-once claim the agent retry loop rests on: a re-sent invocation key converges
    // on its earlier entry instead of double-applying, the idempotency guard adds nothing to the
    // chain (a fresh-key append is byte-identical to plain `append`), and the caller-threaded
    // `KeyIndex` is honest (rebuilding from the stream agrees with incremental maintenance).

    /// The idempotent-append laws (Phase 82) — certify `OpStream.appendIdempotent` (and the
    /// combined Phase 79 composition `appendIdempotentIf`) over a domain's `StreamWitness`. Four
    /// properties: **fresh ≡ append** (a fresh-key `appendIdempotent` produces exactly what
    /// `append` produces — same state, same chained records, so the stream is chain-identical —
    /// and the returned index is the old index plus exactly the new entry; a domain rejection is
    /// forwarded verbatim); **duplicate convergence** (re-sending a seen key is
    /// `AppendOutcome.Duplicate` naming the entry the key *first* produced — `Seq` + `Hash` — and
    /// the caller's stream is untouched); **rebuild parity** (`KeyIndex.ofStream keyOf` over the
    /// resulting stream equals the incrementally-maintained index, at every prefix the loop
    /// builds); and **idempotency-before-CAS** (`appendIdempotentIf` with a seen key converges on
    /// `Duplicate` even under a stale head — the lost-ack retry terminates; a fresh key under a
    /// stale head is `StaleHead`; a fresh key under the true head ≡ `append`). `keyOf` is the
    /// domain's op → invocation-key projection (the Phase 27 `Function.invocationKey` shape) —
    /// the laws only exercise keys `keyOf` yields, so a projection that collides distinct ops
    /// treats them as retries of one invocation (the caller's contract, certified as given).
    /// Seed-replayable; a counterexample carries the seed + iteration. `'State` / `'Op` / `'Rej`
    /// need equality.
    let idempotencyLaws
        (keyOf: 'Op -> string)
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable freshLaw = None
        let mutable dupLaw = None
        let mutable parityLaw = None
        let mutable casLaw = None
        let actor = Human "conf"

        for i in 0 .. iterations - 1 do
            // Build a base chain THROUGH appendIdempotent, threading (state, records, index) — a
            // generated op whose key is already seen (or whose apply rejects) extends nothing,
            // which is itself the primitive under test. Rebuild parity is checked at every step.
            let mutable state = gen.State0
            let mutable recs = OpStream.empty
            let mutable index = KeyIndex.empty

            for _ in 0..5 do
                let op, r' = gen.Op rng
                rng <- r'

                match OpStream.appendIdempotent hashFn sw (keyOf op) actor op state index recs with
                | Ok(AppendOutcome.Appended(s', recs', idx')) ->
                    state <- s'
                    recs <- recs'
                    index <- idx'
                | Ok(AppendOutcome.Duplicate _)
                | Error _ -> ()

                if KeyIndex.ofStream keyOf recs <> index && parityLaw.IsNone then
                    parityLaw <- Some(sprintf "seed=%d iter=%d: ofStream ≠ the incrementally-maintained index" seed i)

            let baseHead = OpStream.head recs

            // ---- fresh ≡ append (chain-identity + index extended by exactly the new entry) ----
            let opF, rF = gen.Op rng
            rng <- rF

            if (KeyIndex.tryFind (keyOf opF) index).IsNone then
                let viaAppend = OpStream.append hashFn sw actor opF state recs

                let viaIdem =
                    OpStream.appendIdempotent hashFn sw (keyOf opF) actor opF state index recs

                let freshOk =
                    match viaAppend, viaIdem with
                    | Ok(sA, recsA), Ok(AppendOutcome.Appended(sI, recsI, idxI)) ->
                        let last = List.last recsA

                        sA = sI
                        && recsA = recsI
                        && idxI = KeyIndex.add (keyOf opF) { Seq = last.Seq; Hash = last.Hash } index
                    | Error e, Error e2 -> e = e2
                    | _ -> false

                if not freshOk && freshLaw.IsNone then
                    freshLaw <- Some(sprintf "seed=%d iter=%d: appendIdempotent(fresh key) ≢ append" seed i)

            // ---- duplicate convergence: a seen key names the entry it FIRST produced ----
            if not (List.isEmpty recs) then
                let pick, rP = ConfRng.intBelow (List.length recs) rng
                rng <- rP
                let key = keyOf (List.item pick recs).Op
                let first = recs |> List.find (fun r -> keyOf r.Op = key)
                let opD, rD = gen.Op rng
                rng <- rD

                match OpStream.appendIdempotent hashFn sw key actor opD state index recs with
                | Ok(AppendOutcome.Duplicate existing) when existing.Seq = first.Seq && existing.Hash = first.Hash -> () // recs/index are immutable values the caller still holds — the stream is byte-identical
                | other ->
                    if dupLaw.IsNone then
                        dupLaw <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: a seen key did not converge on its first entry (got %A)"
                                    seed
                                    i
                                    other
                            )

                // ---- idempotency-before-CAS: the lost-ack retry converges under a stale head ----
                let staleHead = baseHead + "!" // guaranteed ≠ baseHead

                match OpStream.appendIdempotentIf hashFn sw key staleHead actor opD state index recs with
                | Ok(AppendOutcome.Duplicate existing) when existing.Seq = first.Seq && existing.Hash = first.Hash -> ()
                | other ->
                    if casLaw.IsNone then
                        casLaw <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: a seen key under a stale head did not converge on Duplicate (got %A)"
                                    seed
                                    i
                                    other
                            )

            // ---- fresh key through the CAS: stale head refuses; the true head ≡ append ----
            let opC, rC = gen.Op rng
            rng <- rC

            if (KeyIndex.tryFind (keyOf opC) index).IsNone then
                let staleHead = baseHead + "!"

                match OpStream.appendIdempotentIf hashFn sw (keyOf opC) staleHead actor opC state index recs with
                | Error(AppendRejection.StaleHead(expected, actual)) when expected = staleHead && actual = baseHead ->
                    ()
                | other ->
                    if casLaw.IsNone then
                        casLaw <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: a fresh key under a stale head was not StaleHead (got %A)"
                                    seed
                                    i
                                    other
                            )

                let viaAppend = OpStream.append hashFn sw actor opC state recs

                let viaBoth =
                    OpStream.appendIdempotentIf hashFn sw (keyOf opC) baseHead actor opC state index recs

                let matchOk =
                    match viaAppend, viaBoth with
                    | Ok(sA, recsA), Ok(AppendOutcome.Appended(sI, recsI, _)) -> sA = sI && recsA = recsI
                    | Error e, Error(AppendRejection.Domain e2) -> e = e2
                    | _ -> false

                if not matchOk && casLaw.IsNone then
                    casLaw <- Some(sprintf "seed=%d iter=%d: appendIdempotentIf(fresh key, true head) ≢ append" seed i)

        [ { Law = "appendIdempotent with a fresh key ≡ append (chain-identical; index gains exactly the new entry)"
            Passed = freshLaw.IsNone
            Counterexample = freshLaw }
          { Law = "re-appending a seen key is Duplicate naming the entry the key first produced (stream untouched)"
            Passed = dupLaw.IsNone
            Counterexample = dupLaw }
          { Law = "KeyIndex.ofStream agrees with the incrementally-maintained index (rebuild parity)"
            Passed = parityLaw.IsNone
            Counterexample = parityLaw }
          { Law = "idempotency precedes the CAS (a seen key converges under any head; a fresh key CASes as appendIf)"
            Passed = casLaw.IsNone
            Counterexample = casLaw } ]
