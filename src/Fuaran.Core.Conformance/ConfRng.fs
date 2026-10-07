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

    open System.Runtime.CompilerServices

    /// An immutable generator position: every draw returns the value and the NEXT `T`, so a
    /// stream is reproduced by re-threading from the same `ofSeed`.
    type T =
        {
            /// The xorshift32 state — non-zero whenever it came from `ofSeed`. A hand-built
            /// `{ State = 0u }` is the fixed point and draws 0 forever.
            State: uint32
        }

    // The kernel — the step, the seed's warm-up, the top-31-bit draw and the high-bit rejection —
    // is `Xorshift32`'s (Phase 388; `Fuaran.Core.Idl`, through its `InternalsVisibleTo`), the one
    // body the IDL sampler draws through too. Its header says why it is shifts and XOR and never a
    // 32-bit multiply: until 0.20.0 this generator was an LCG on exactly that product, and under
    // Fable every draw after the first collapsed to zero. The cross-pipeline `confRng/*` vectors in
    // `ParityVectors` (this package) are what buy it, and they redden on a reverted multiply.
    // The three functions below that name `Xorshift32` are `NoInlining` (Phase 402, DECISIONS.md
    // D129): the kernel is internal to `Fuaran.Core.Idl`, and a body naming it that the F# optimiser
    // copied into a Release-built caller failed there with `MethodAccessException`.

    /// Seed to initial state. Non-zero by construction (see `Xorshift32.step`), and warmed by three
    /// rounds so that adjacent seeds start far apart rather than one shift-and-XOR apart —
    /// a kit that certifies over a seed SWEEP would otherwise draw near-identical samples
    /// from consecutive seeds.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let ofSeed (seed: int) : T = { State = Xorshift32.seeded seed }

    /// A non-negative int (the top 31 bits of the advanced state) and that state.
    ///
    /// Value-identical on .NET and under Fable, which is the constraint `intBelow` documents
    /// below and this function did not honour until 0.20.0 — see `Xorshift32.step`.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let next (r: T) : int * T =
        let s = Xorshift32.step r.State
        Xorshift32.value s, { State = s }

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
    /// 0.20.0, having been an LCG that did not — see `Xorshift32`, and `Hash.fs`.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let intBelow (n: int) (r: T) : int * T = Xorshift32.below next n r

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
