namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Conformance (Phase 243) — a property-based law kit a domain runs
//  against its own witness to certify it conforms to the Fuaran.Core op algebra
//  and op-stream. It generalises the `Core.Wire.Corpus` "methodology-is-the-asset"
//  posture from the wire codec to the op algebra: supply a generator, get a
//  verdict — instead of re-authoring a conformance suite per domain.
//
//  FSharp.Core only (a deterministic uint32 xorshift, no FsCheck), Fable-clean.
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

/// A deterministic, FSharp.Core-only RNG (uint32 xorshift32 — shifts and XOR only, so it
/// draws the SAME stream under Fable as on .NET). Seed-replayable: the same seed reproduces
/// the same run, which is how a counterexample is reproduced.
module ConfRng =

    /// An immutable generator position: every draw returns the value and the NEXT `T`, so a
    /// stream is reproduced by re-threading from the same `ofSeed`.
    type T =
        {
            /// The xorshift32 state — non-zero whenever it came from `ofSeed`. A hand-built
            /// `{ State = 0u }` is the fixed point and draws 0 forever.
            State: uint32
        }

    /// The xorshift32 step (Marsaglia 2003): three shift/XOR rounds, full period over the
    /// 2^32 - 1 non-zero states.
    ///
    /// **Shifts and XOR, never a 32-bit multiply — and that is the whole point of this
    /// function existing.** A `uint32` product is the one arithmetic shape Fable cannot
    /// carry: it is formed on a double, so `state * 1664525u` reaches ~7e15 and loses its
    /// low bits INSIDE the operation, before any mask could recover them (`Hash.mul32`
    /// documents the same defect on the FNV multiply). Until 0.20.0 this generator was an
    /// LCG built on exactly that product, and under Fable every draw after the first
    /// collapsed to zero — so a domain certifying in a browser drew a degenerate sample from
    /// a seed that behaved perfectly on .NET. Nothing in a .NET suite could see it; the
    /// cross-pipeline `confRng/*` vectors in `ParityVectors` (this package) are what
    /// buys it, and they redden on a reverted multiply.
    ///
    /// State 0 is xorshift's fixed point — it maps to itself, and every draw from it is 0 —
    /// so it must never be reached. It cannot be produced from a non-zero state (the step is
    /// a bijection on GF(2)^32), which leaves `ofSeed` as the only place that has to rule it
    /// out.
    let private step (x: uint32) : uint32 =
        let a = x ^^^ (x <<< 13)
        let b = a ^^^ (a >>> 17)
        b ^^^ (b <<< 5)

    /// Seed to initial state. Non-zero by construction (see `step`), and warmed by three
    /// rounds so that adjacent seeds start far apart rather than one shift-and-XOR apart —
    /// a kit that certifies over a seed SWEEP would otherwise draw near-identical samples
    /// from consecutive seeds.
    let ofSeed (seed: int) : T =
        let mixed = uint32 seed ^^^ 0x9E3779B9u
        let s0 = if mixed = 0u then 0x6D2B79F5u else mixed
        { State = step (step (step s0)) }

    /// A non-negative int (the top 31 bits of the advanced state) and that state.
    ///
    /// Value-identical on .NET and under Fable, which is the constraint `intBelow` documents
    /// below and this function did not honour until 0.20.0 — see `step`.
    let next (r: T) : int * T =
        let s = step r.State
        int (s >>> 1), { State = s }

    /// The number of bits needed to represent `v` (0 for 0, 31 for `Int32.MaxValue`).
    let rec private bitWidth (acc: int) (v: int) : int =
        if v = 0 then acc else bitWidth (acc + 1) (v >>> 1)

    /// A value in `[0, n)` (0 when `n <= 0`).
    ///
    /// Drawn from the HIGH-ORDER bits by rejection, never `v % n`. Reducing modulo a small `n`
    /// reads the weakest end of the word: a shift-register state's low bits are the shortest
    /// XOR combinations of the bits before them, so choices drawn consecutively off one
    /// advancing stream come out in near-lockstep rather than independently — invisible in a
    /// generator that consumes a whole word (a fresh id) and decisive in one that makes a
    /// handful of small choices per step. Taking the top `bitWidth (n - 1)` bits instead reads
    /// the best-mixed end, and rejecting an out-of-range candidate leaves the result exactly
    /// uniform rather than modulo-biased. Acceptance is above one half, so fewer than two draws
    /// in expectation.
    ///
    /// Built from shifts and comparisons alone: no `uint64` (JavaScript cannot carry one
    /// exactly) and no 32-bit multiply (which does not wrap identically on both pipelines), so
    /// the kit stays value-identical under Fable. `next` honours that same constraint since
    /// 0.20.0, having been an LCG that did not — see `step` above, and `Hash.fs`.
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

    /// A uniformly chosen element of `xs` (index drawn by `intBelow`) and the advanced state.
    ///
    /// **The sanctioned programming-error throw (Phase 384).** `xs` is a GENERATOR's alphabet —
    /// a literal list in the law kit or a domain's generator, fixed when the generator is
    /// written — so an empty one is a defect of that generator, not a value a run supplies. It
    /// raises `ArgumentException` naming that, as the kit's other declaration-time refusals do;
    /// there is deliberately no `tryChoose`, because no caller has a use for drawing from a
    /// possibly-empty alphabet that `List.isEmpty` before the draw does not serve better.
    let choose (xs: 'a list) (r: T) : 'a * T =
        match xs with
        | [] -> invalidArg "xs" "ConfRng.choose: a generator's alphabet is empty, so there is no element to choose"
        | _ ->
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
