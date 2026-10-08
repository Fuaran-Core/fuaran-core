namespace Fuaran.Core.Idl

open Fuaran.Core

/// WIRE_FORMAT §7's non-finite float spelling, read back (Phase 303) — the inverse of
/// `JVal.nonFiniteToken`, which is the one place the spine writes it. Internal: the
/// interpreter's float slot and the artifact's float value read through it, and nothing
/// else in the tier may widen a slot to the quoted token.
module internal FloatToken =
    /// The non-finite float a §7 token names, or `None` for any other string.
    let tryNonFinite (s: string) : float option =
        match s with
        | "NaN" -> Some System.Double.NaN
        | "Infinity" -> Some System.Double.PositiveInfinity
        | "-Infinity" -> Some System.Double.NegativeInfinity
        | _ -> None

    /// [[tryNonFinite]] as a pattern, for a decoder's float arm.
    let (|NonFinite|_|) (s: string) : float option = tryNonFinite s
