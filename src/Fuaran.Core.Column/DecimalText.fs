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

    let private isDigit (c: char) : bool = c >= '0' && c <= '9'

    let private isDigits (s: string) : bool = s.Length > 0 && s |> Seq.forall isDigit

    /// True where `s` is canonical, read in place with nothing allocated: an optional minus, an
    /// integer part that is a lone `0` or starts with a non-zero digit, and, where a point is
    /// written, a fraction of at least one digit that does not end in `0` — and no minus on a
    /// lone `0`. Exactly the texts `render (parts s)` gives back unchanged.
    let private canonicalShape (s: string) : bool =
        if isNull (box s) || s.Length = 0 then
            false
        else
            let negative = s[0] = '-'
            let start = if negative then 1 else 0
            let mutable i = start

            while i < s.Length && isDigit s[i] do
                i <- i + 1

            let intLen = i - start

            if intLen = 0 || (intLen > 1 && s[start] = '0') then
                false
            elif i = s.Length then
                not (negative && intLen = 1 && s[start] = '0')
            elif s[i] <> '.' then
                false
            else
                let mutable j = i + 1

                while j < s.Length && isDigit s[j] do
                    j <- j + 1

                j = s.Length && j > i + 1 && s[j - 1] <> '0'

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

    /// The canonical form of `s` with no option around it — `s` itself where it is already
    /// canonical — or `null` where `s` is not decimal text. The admission an aggregate's walk
    /// makes per row.
    let internal canonicalOrNull (s: string) : string =
        if canonicalShape s then
            s
        else
            match parts s with
            | Some p -> render p
            | None -> null

    /// The canonical form of `s`, or `None` where `s` is not decimal text. A text already canonical
    /// is answered as itself, with no copy.
    let tryCanonical (s: string) : string option =
        match canonicalOrNull s with
        | null -> None
        | c -> Some c

    /// True where `s` is decimal text already in canonical form.
    let isCanonical (s: string) : bool = canonicalShape s

    // Canonical texts are read in place, one digit at a PLACE: `0` the units, `1` the tens, `-1`
    // the tenths. A place outside a text's digits reads `0`, which aligns two texts at the point
    // with no padded copy of either.
    let private intStart (s: string) : int = if s[0] = '-' then 1 else 0

    let private pointAt (s: string) : int =
        let dot = s.IndexOf '.'
        if dot < 0 then s.Length else dot

    let private fractionLength (s: string) (point: int) : int =
        if point < s.Length then s.Length - point - 1 else 0

    let private digitAt (s: string) (start: int) (point: int) (place: int) : int =
        let i = if place >= 0 then point - 1 - place else point - place

        if i >= start && i < s.Length then int s[i] - int '0' else 0

    let private sign (n: int) : int =
        if n < 0 then -1
        elif n > 0 then 1
        else 0

    /// The order of two canonical magnitudes, read from the highest place either holds down to
    /// the lowest, as `-1` / `0` / `1`.
    let private compareMagnitudes (a: string) (b: string) : int =
        let sa, pa = intStart a, pointAt a
        let sb, pb = intStart b, pointAt b
        let lowest = -(max (fractionLength a pa) (fractionLength b pb))
        let mutable place = max (pa - sa) (pb - sb) - 1
        let mutable order = 0

        while order = 0 && place >= lowest do
            order <- sign (digitAt a sa pa place - digitAt b sb pb place)
            place <- place - 1

        order

    /// The order of two canonical texts as `-1` / `0` / `1`. Zero is unsigned in canonical form,
    /// so a minus marks a value below every unsigned one.
    let private compareCanonical (a: string) (b: string) : int =
        let na, nb = a[0] = '-', b[0] = '-'

        if na <> nb then
            if na then -1 else 1
        else
            let magnitude = compareMagnitudes a b
            if na then -magnitude else magnitude

    /// The exact sum of two CANONICAL texts, in canonical form — the admission an aggregate has
    /// already made, or `tryCanonical`'s answer; nothing here checks it. The sum is written into one
    /// `char[]` laid out as its text — a slot for the sign, the integer places with one more for a
    /// carry, the point, the fraction places — from the lowest place up, and the one string is the
    /// span that survives the trim. Nothing is parsed, padded or split.
    let internal addCanonical (a: string) (b: string) : string =
        let na, nb = a[0] = '-', b[0] = '-'
        let sa, pa = intStart a, pointAt a
        let sb, pb = intStart b, pointAt b
        let scale = max (fractionLength a pa) (fractionLength b pb)
        let width = max (pa - sa) (pb - sb) + 1
        // where the signs differ, the larger magnitude less the smaller, with the larger's sign
        let order = if na = nb then 1 else compareMagnitudes a b

        if order = 0 then
            zero
        else
            let negative = if order > 0 then na else nb
            // the sign at 0, the units place at `width`, the point at `width + 1`
            let point = width + 1
            let text = Array.zeroCreate<char> (point + 1 + scale)
            text[point] <- '.'
            let mutable carry = 0

            for k in 0 .. width + scale - 1 do
                let place = k - scale
                let x = digitAt a sa pa place
                let y = digitAt b sb pb place

                let d =
                    if na = nb then x + y + carry
                    elif order > 0 then x - y - carry
                    else y - x - carry

                carry <- if d < 0 || d > 9 then 1 else 0

                let d =
                    if d < 0 then d + 10
                    elif d > 9 then d - 10
                    else d

                text[if place < 0 then point - place else width - place] <- char (d + int '0')

            // the first integer digit shown: the highest non-zero one, else the units' `0`
            let mutable first = 1

            while first < width && text[first] = '0' do
                first <- first + 1

            // the last character shown: the lowest non-zero fraction digit, else the units
            let mutable last = point + scale

            while last > point && text[last] = '0' do
                last <- last - 1

            let last = if last = point then width else last

            let start =
                if negative then
                    text[first - 1] <- '-'
                    first - 1
                else
                    first

            System.String(text, start, last - start + 1)

    /// A running sum's next total: `text` where `total` is `null` (no row yet), else the exact sum.
    /// Both must be canonical, as an admitted row and an earlier total are.
    let internal addToTotal (total: string) (text: string) : string =
        if isNull total then text else addCanonical total text

    /// The numeric order of two decimal texts as `-1` / `0` / `1`, or `None` where either is not
    /// decimal text. Canonical or not: `1.50` and `1.5` compare equal.
    let compare (a: string) (b: string) : int option =
        match tryCanonical a, tryCanonical b with
        | Some ca, Some cb -> Some(compareCanonical ca cb)
        | _ -> None

    /// The exact sum of two decimal texts, in canonical form, or `None` where either is not decimal
    /// text. Nothing is rounded and nothing overflows.
    let add (a: string) (b: string) : string option =
        match tryCanonical a, tryCanonical b with
        | Some ca, Some cb -> Some(addCanonical ca cb)
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
