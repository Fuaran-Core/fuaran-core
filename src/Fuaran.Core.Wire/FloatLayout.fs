namespace Fuaran.Core

/// The single float -> string LAYOUT, shared by `Json.render` and `Canon.canonicalFloat`, and
/// value-identical on both pipelines. .NET's round-trip specifier (`"R"`) is what the layout IS,
/// and it is also the one thing Fable will not do: `String.format` REFUSES `R` at RUNTIME, so a
/// `"{0:R}"` on a Fable-targeted surface compiles cleanly and throws in the browser. The re-lay
/// below reproduces that layout from JS's own shortest-round-trip digits, so both pipelines emit
/// the same bytes (WIRE_FORMAT §2 rule 5). Certified rather than asserted since Phase 118:
/// the Fable consumer's parity leg runs `ParityVectors` under a JS runtime and byte-compares it
/// against the .NET run (STABILITY.md "Fable cleanliness").
///
/// PUBLIC since Phase 315 (it was `internal`): a host that lays a float out anywhere but the wire —
/// an SVG coordinate, a label — used to keep its own copy of this re-lay and a note to keep it in
/// sync. `finite` and `roundTrip` are the layout; the `floatLayout/*` parity vectors pin them.
module FloatLayout =

#if FABLE_COMPILER
    [<Fable.Core.Emit("$0.toString()")>]
    let private jsNumberToString (n: float) : string = Fable.Core.Util.jsNative

    /// Re-lay JS's shortest-round-trip digits into .NET `ToString("R")` form (WIRE_FORMAT §2 rule 5)
    /// so the Fable host is byte-identical to the .NET host across the whole finite-double range.
    /// Ported verbatim from the UI host's `CanonicalJson.formatFiniteDouble`.
    let private reLay (n: float) : string =
        if n = 0.0 then
            "0"
        else
            let neg = n < 0.0
            let s = jsNumberToString (abs n)
            let mutable digits = ""
            let mutable exp = 0
            let eIdx = s.IndexOf 'e'

            if eIdx >= 0 then
                let mant = s.Substring(0, eIdx)
                let mantExp = int (s.Substring(eIdx + 1))
                let dot = mant.IndexOf '.'

                if dot < 0 then
                    digits <- mant
                    exp <- mantExp + (mant.Length - 1)
                else
                    digits <- mant.Substring(0, dot) + mant.Substring(dot + 1)
                    exp <- mantExp + (dot - 1)
            else
                let dot = s.IndexOf '.'

                if dot < 0 then
                    digits <- s
                    exp <- s.Length - 1
                else
                    let intPart = s.Substring(0, dot)
                    let fracPart = s.Substring(dot + 1)

                    if intPart = "0" then
                        let trimmed = fracPart.TrimStart('0')
                        let leadingZeros = fracPart.Length - trimmed.Length
                        digits <- fracPart.Substring(leadingZeros)
                        exp <- -(leadingZeros + 1)
                    else
                        digits <- intPart + fracPart
                        exp <- intPart.Length - 1

            digits <- digits.TrimEnd('0')

            if digits = "" then
                digits <- "0"

            let out =
                if exp >= -4 && exp <= 16 then
                    if exp >= 0 then
                        if digits.Length <= exp + 1 then
                            digits + String.replicate (exp + 1 - digits.Length) "0"
                        else
                            digits.Substring(0, exp + 1) + "." + digits.Substring(exp + 1)
                    else
                        "0." + String.replicate (-exp - 1) "0" + digits
                else
                    let mantissa =
                        if digits.Length = 1 then
                            digits
                        else
                            string digits[0] + "." + digits.Substring(1)

                    let expSign = if exp >= 0 then "+" else "-"
                    let expDigits = (abs exp).ToString().PadLeft(2, '0')
                    mantissa + "E" + expSign + expDigits

            if neg then "-" + out else out
#endif

    /// A FINITE double in .NET's round-trip (`"R"`) layout, on either pipeline. `-0.0` keeps its
    /// sign here — collapsing it is `Canon.canonicalFloat`'s wire rule, not the layout's.
    ///
    /// Under Fable, a double of magnitude in [1e-4, 1e17) is written by JS's own `toString`, with no
    /// re-lay, because there the two layouts are the same characters (Phase 373). Write the shortest
    /// round-trip digits as d1..dk with point position p, so the value is 0.d1..dk × 10^p and dk is not
    /// zero. `reLay` recovers the same digits and sets its exponent to p - 1, then writes FIXED point
    /// exactly when p - 1 is in [-4, 16]: the digits with the point after p of them, or padded with
    /// p - k zeros when k <= p, or `0.` then -p zeros then the digits when p <= 0. ECMAScript's
    /// Number::toString writes those same three shapes for every p in [-5, 21]. So for p in [-3, 17]
    /// both write identical characters, and a negative value is `-` and the same characters on both.
    /// p >= -3 is |n| >= 1e-4, and p <= 17 is |n| < 1e17, compared as doubles: both bounds are
    /// shortest-digit values, and rounding to nearest is monotone, so a double below either bound
    /// cannot have shortest digits at or above it, and one at or above it cannot have digits below
    /// it. Outside the band the layouts differ — `1E-05` against `0.00001`, `1E+17` against
    /// `100000000000000000` — and the re-lay runs. This band contains Phase 367's [2^53, 1e17), so the
    /// parser's canonical-integer check (`Json.parseNumber`, Phase 253) skips the re-lay through this
    /// branch too. The `floatLayout/band-*` and `jsonParse/*` parity vectors sit on each side of
    /// each edge; Core's own gate never compiles Fable (D55), so those vectors, run under node by the
    /// Fable consumer's parity leg, are what sees this branch.
    let finite (n: float) : string =
#if FABLE_COMPILER
        if n = 0.0 then
            // `reLay` short-circuits both zeroes to "0"; .NET's "R" spells the negative one "-0",
            // and `1.0 / n` is the only way to read the sign of a JS negative zero.
            (if 1.0 / n < 0.0 then "-0" else "0")
        else
            let m = abs n

            if m >= 1e-4 && m < 1e17 then
                jsNumberToString n
            else
                reLay n
#else
        n.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
#endif

    /// The full `"{0:R}"` rendering, non-finite tokens included (`NaN` / `Infinity` / `-Infinity`,
    /// the invariant-culture spellings). `Json.render`'s float case: those tokens are not valid
    /// JSON, which is exactly why `Json.tryRender` exists to name them, so the layout keeps
    /// producing them rather than quietly substituting something parseable.
    let roundTrip (f: float) : string =
        if System.Double.IsNaN f then "NaN"
        elif System.Double.IsPositiveInfinity f then "Infinity"
        elif System.Double.IsNegativeInfinity f then "-Infinity"
        else finite f
