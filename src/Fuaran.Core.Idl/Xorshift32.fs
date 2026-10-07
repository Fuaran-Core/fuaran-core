namespace Fuaran.Core

/// The spine's one seed-replayable generator kernel (Phase 388): a uint32 xorshift32 and the draws
/// built on it. The conformance kit's public `ConfRng` threads it immutably, and the IDL sampler
/// (`Fuaran.Core.Idl.Sample`) threads it through a mutable position; both read THIS body, so the
/// sampler draws `ConfRng`'s stream by construction rather than by a copy held equal by a test. It
/// lives here, in the lower of the two packages, because `Fuaran.Core.Conformance` references this
/// one; that package reads it through `InternalsVisibleTo` (DECISIONS.md D125). Internal: the
/// surface is `ConfRng`'s.
///
/// **Shifts and XOR, never a 32-bit multiply — and that is the whole point of this kernel.** A
/// `uint32` product is the one arithmetic shape Fable cannot carry: it is formed on a double, so
/// `state * 1664525u` reaches ~7e15 and loses its low bits INSIDE the operation, before any mask could
/// recover them (`Hash.mul32` documents the same defect on the FNV multiply). Until 0.20.0 `ConfRng`
/// was an LCG built on exactly that product, and under Fable every draw after the first collapsed to
/// zero. The cross-pipeline `confRng/*` and `sample/*` vectors in `ParityVectors` are what hold it.
/// FSharp.Core only, Fable-clean.
module internal Xorshift32 =

    /// The xorshift32 step (Marsaglia 2003): three shift/XOR rounds, full period over the 2^32 - 1
    /// non-zero states.
    ///
    /// State 0 is xorshift's fixed point — it maps to itself, and every draw from it is 0 — so it
    /// must never be reached. It cannot be produced from a non-zero state (the step is a bijection on
    /// GF(2)^32), which leaves `seeded` as the only place that has to rule it out.
    let step (x: uint32) : uint32 =
        let a = x ^^^ (x <<< 13)
        let b = a ^^^ (a >>> 17)
        b ^^^ (b <<< 5)

    /// Seed to initial state. Non-zero by construction (see `step`), and warmed by three rounds so
    /// that adjacent seeds start far apart rather than one shift-and-XOR apart — a kit that certifies
    /// over a seed SWEEP would otherwise draw near-identical samples from consecutive seeds.
    let seeded (seed: int) : uint32 =
        let mixed = uint32 seed ^^^ 0x9E3779B9u
        let s0 = if mixed = 0u then 0x6D2B79F5u else mixed
        step (step (step s0))

    /// The draw a state answers: its top 31 bits, a non-negative int.
    let value (state: uint32) : int = int (state >>> 1)

    /// The number of bits needed to represent `v` (0 for 0, 31 for `Int32.MaxValue`).
    let rec private bitWidth (acc: int) (v: int) : int =
        if v = 0 then acc else bitWidth (acc + 1) (v >>> 1)

    /// A value in `[0, n)` (0 when `n <= 0`, drawing nothing), off `draw` — one draw of `value`s and
    /// the position after it, whichever way the caller threads its position.
    ///
    /// Drawn from the HIGH-ORDER bits by rejection, never `v % n`. Reducing modulo a small `n` reads
    /// the weakest end of the word: a shift-register state's low bits are the shortest XOR
    /// combinations of the bits before them, so choices drawn consecutively off one advancing stream
    /// come out in near-lockstep rather than independently. Taking the top `bitWidth (n - 1)` bits
    /// instead reads the best-mixed end, and rejecting an out-of-range candidate leaves the result
    /// exactly uniform rather than modulo-biased. Acceptance is above one half, so fewer than two
    /// draws in expectation. `n = 1` still draws once, so a stream advances at the same rate whatever
    /// `n` is.
    ///
    /// Built from shifts and comparisons alone: no `uint64` (JavaScript cannot carry one exactly) and
    /// no 32-bit multiply, so it stays value-identical under Fable.
    let below (draw: 'R -> int * 'R) (n: int) (r: 'R) : int * 'R =
        if n <= 0 then
            0, r
        elif n = 1 then
            let _, r' = draw r
            0, r'
        else
            // `value` yields the top 31 bits of the state; this drops all but the top `bits`.
            let shift = 31 - bitWidth 0 (n - 1)
            let mutable rng = r
            let mutable candidate = n

            while candidate >= n do
                let v, r' = draw rng
                rng <- r'
                candidate <- v >>> shift

            candidate, rng
