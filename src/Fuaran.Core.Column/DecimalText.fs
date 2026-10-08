namespace Fuaran.Core

/// Exact-decimal text (`0.33.0`) — the carrier of a `Decimal` cell, and the only arithmetic the
/// column layer needs over it: a canonical form, an order, and a sum.
///
/// THE GRAMMAR READ is `-?[0-9]+(\.[0-9]+)?`: an optional minus, at least one integer digit, and a
/// fraction of at least one digit where a point is written. That is what a database renders a
/// fixed-scale value as (`12.50`), so leading zeros and trailing fraction zeros are READ and
/// normalised away. A leading `+`, a bare point (`.5`, `5.`), an exponent, a separator and white
/// space are refused: each has more than one reading somewhere, and the type exists to have one.
///
/// THE CANONICAL FORM WRITTEN has no leading zero on the integer part (a lone `0` stands for none),
/// no trailing zero on the fraction, no point where the fraction is zero, and no sign on zero. Two
/// texts denote one number exactly when their canonical forms are one string, so equality, grouping
/// and distinctness over canonical cells are string equality and need no arithmetic.
///
/// Arbitrary precision: the digits are strings, so nothing here overflows or rounds. Pure, total,
/// FSharp.Core only, Fable-clean — no host `decimal`, whose range and layout differ by host.
[<RequireQualifiedAccess>]
module DecimalText =

    let private isDigits (s: string) : bool =
        s.Length > 0 && s |> Seq.forall (fun c -> c >= '0' && c <= '9')

    /// `(negative, integer digits, fraction digits)` with the zeros stripped — an empty digit
    /// string is zero — or `None` for text outside the grammar.
    let private parts (s: string) : (bool * string * string) option =
        if isNull (box s) || s.Length = 0 then
            None
        else
            let negative = s[0] = '-'
            let body = if negative then s.Substring 1 else s
            let dot = body.IndexOf '.'

            let wellFormed, ip, fp =
                if dot < 0 then
                    isDigits body, body, ""
                else
                    let ip = body.Substring(0, dot)
                    let fp = body.Substring(dot + 1)
                    isDigits ip && isDigits fp, ip, fp

            if not wellFormed then
                None
            else
                let ip = ip.TrimStart '0'
                let fp = fp.TrimEnd '0'
                let isZero = ip.Length = 0 && fp.Length = 0
                Some(negative && not isZero, ip, fp)

    let private render (negative: bool, ip: string, fp: string) : string =
        (if negative then "-" else "")
        + (if ip.Length = 0 then "0" else ip)
        + (if fp.Length = 0 then "" else "." + fp)

    /// Zero, in canonical form.
    let zero: string = "0"

    /// The canonical form of `s`, or `None` where `s` is not decimal text.
    let tryCanonical (s: string) : string option = parts s |> Option.map render

    /// True where `s` is decimal text already in canonical form.
    let isCanonical (s: string) : bool = tryCanonical s = Some s

    // Magnitudes are compared and combined as digit strings aligned at the point: the fractions
    // padded to one scale on the right, the whole padded to one width on the left.
    let private aligned (ia: string, fa: string) (ib: string, fb: string) : string * string * int =
        let scale = max fa.Length fb.Length
        let a = ia + fa.PadRight(scale, '0')
        let b = ib + fb.PadRight(scale, '0')
        let width = max a.Length b.Length
        a.PadLeft(width, '0'), b.PadLeft(width, '0'), scale

    let private sign (n: int) : int =
        if n < 0 then -1
        elif n > 0 then 1
        else 0

    let private digit (c: char) : int = int c - int '0'

    // Digits are held as ints and rendered through `string`, the one conversion every host agrees on.
    let private digitsText (digits: int[]) : string =
        digits |> Array.map string |> String.concat ""

    let private addMagnitudes (a: string) (b: string) : string =
        let out = Array.zeroCreate<int> (a.Length + 1)
        let mutable carry = 0

        for i in a.Length - 1 .. -1 .. 0 do
            let d = digit a[i] + digit b[i] + carry
            out[i + 1] <- d % 10
            carry <- d / 10

        out[0] <- carry
        digitsText out

    /// `a - b` over aligned magnitudes with `a >= b`.
    let private subMagnitudes (a: string) (b: string) : string =
        let out = Array.zeroCreate<int> a.Length
        let mutable borrow = 0

        for i in a.Length - 1 .. -1 .. 0 do
            let d = digit a[i] - digit b[i] - borrow

            if d < 0 then
                out[i] <- d + 10
                borrow <- 1
            else
                out[i] <- d
                borrow <- 0

        digitsText out

    /// The numeric order of two decimal texts as `-1` / `0` / `1`, or `None` where either is not
    /// decimal text. Canonical or not: `1.50` and `1.5` compare equal.
    let compare (a: string) (b: string) : int option =
        match parts a, parts b with
        | Some(na, ia, fa), Some(nb, ib, fb) ->
            if na <> nb then
                Some(if na then -1 else 1)
            else
                let ma, mb, _ = aligned (ia, fa) (ib, fb)
                let magnitude = sign (System.String.CompareOrdinal(ma, mb))
                Some(if na then -magnitude else magnitude)
        | _ -> None

    /// The exact sum of two decimal texts, in canonical form, or `None` where either is not decimal
    /// text. Nothing is rounded and nothing overflows.
    let add (a: string) (b: string) : string option =
        match parts a, parts b with
        | Some(na, ia, fa), Some(nb, ib, fb) ->
            let ma, mb, scale = aligned (ia, fa) (ib, fb)

            let negative, magnitude =
                if na = nb then
                    na, addMagnitudes ma mb
                else
                    match sign (System.String.CompareOrdinal(ma, mb)) with
                    | 0 -> false, ""
                    | c when c > 0 -> na, subMagnitudes ma mb
                    | _ -> nb, subMagnitudes mb ma

            let magnitude = magnitude.PadLeft(scale + 1, '0')
            let ip = magnitude.Substring(0, magnitude.Length - scale)
            let fp = magnitude.Substring(magnitude.Length - scale)
            let isZero = (ip.TrimStart '0').Length = 0 && (fp.TrimEnd '0').Length = 0
            Some(render (negative && not isZero, ip.TrimStart '0', fp.TrimEnd '0'))
        | _ -> None

    /// The nearest `float` to a decimal text, or `None` where it is not decimal text OR its
    /// magnitude is past the float range (Phase 299: a text of some 309 digits read as `∞`
    /// silently until then, and an infinity is not the nearest float to any decimal). This is the
    /// one place the type rounds, and it is for the aggregates whose result is a `float` by
    /// declaration (`Mean` / `Median` / `StdDev`); a value that must stay exact never comes through it.
    let tryToFloat (s: string) : float option =
        tryCanonical s
        |> Option.map float
        |> Option.filter (fun f -> not (System.Double.IsInfinity f))
