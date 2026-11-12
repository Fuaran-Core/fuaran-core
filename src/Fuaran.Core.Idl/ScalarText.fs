namespace Fuaran.Core.Idl

/// The scalar types of the wider data universe that ride as a JSON STRING (Phase 639, DECISIONS.md
/// D251) — the closed set the lowering ([[WireForm]]) groups, so the encoder, the decoder, the
/// sampler and the validator read one definition of each.
[<RequireQualifiedAccess>]
type internal TextScalar =
    /// [[TInt64]].
    | Int64
    /// [[TDecimal]].
    | Decimal
    /// [[TBytes]].
    | Bytes
    /// [[TTimeOfDay]].
    | TimeOfDay
    /// [[TDuration]].
    | Duration

/// **The canonical spellings of the wider data universe's string-borne scalars (Phase 639,
/// DECISIONS.md D251)**, and the canonical ORDER a set's elements and a non-string-keyed map's keys
/// take. Each `is…` answers whether a string is the ONE spelling of a value of its type: a reader
/// refuses every other string, so there is nothing to normalise and no second spelling for two hosts
/// to disagree on. Written as character walks over FSharp.Core only, with no regular expression and no
/// host numeric, date or base64 type, because the same grammar is emitted for the TypeScript host
/// and the F* model and must mean one thing in all three.
[<RequireQualifiedAccess>]
module ScalarText =

    let private isDigit (c: char) : bool = c >= '0' && c <= '9'

    let private allDigits (s: string) : bool = s |> Seq.forall isDigit

    /// An unsigned integer part: digits, and no leading zero unless it is `0` itself.
    let private isNatural (s: string) : bool =
        s.Length > 0 && allDigits s && (s = "0" || s[0] <> '0')

    /// A fraction of one to `most` digits whose last digit is not zero.
    let private isFraction (most: int) (s: string) : bool =
        s.Length >= 1 && s.Length <= most && allDigits s && s[s.Length - 1] <> '0'

    /// Whether the digit string `digits` (no leading zero) is at most `bound` (no leading zero):
    /// compared by length, then by digit — which is numeric order for such strings.
    let private atMost (bound: string) (digits: string) : bool =
        digits.Length < bound.Length
        || (digits.Length = bound.Length && System.String.CompareOrdinal(digits, bound) <= 0)

    let private int64Max = "9223372036854775807"
    let private int64MinMagnitude = "9223372036854775808"

    /// `-?(0|[1-9][0-9]*)` within the signed 64-bit range, with no `-0`.
    let isInt64 (s: string) : bool =
        not (isNull s)
        && (let negative = s.StartsWith('-')
            let body = if negative then s.Substring 1 else s

            isNatural body
            && not (negative && body = "0")
            && atMost (if negative then int64MinMagnitude else int64Max) body)

    /// `DecimalText`'s canonical form: `-?(0|[1-9][0-9]*)(\.[0-9]*[1-9])?`, with no `-0`.
    let isDecimal (s: string) : bool =
        not (isNull s)
        && (let negative = s.StartsWith('-')
            let body = if negative then s.Substring 1 else s
            let dot = body.IndexOf '.'

            if dot < 0 then
                isNatural body && not (negative && body = "0")
            else
                isNatural (body.Substring(0, dot))
                && isFraction System.Int32.MaxValue (body.Substring(dot + 1)))

    let private base64Alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"

    /// RFC 4648 §4 base64 with its padding, and every unused bit of the last symbol zero.
    let isBytes (s: string) : bool =
        not (isNull s)
        && s.Length % 4 = 0
        && (let pad =
                if s.EndsWith("==", System.StringComparison.Ordinal) then 2
                elif s.EndsWith('=') then 1
                else 0

            let data = s.Length - pad

            Seq.forall (fun c -> base64Alphabet.IndexOf(c: char) >= 0) (s.Substring(0, data))
            && (pad = 0
                || (let last = base64Alphabet.IndexOf s[data - 1]
                    (pad = 2 && last % 16 = 0) || (pad = 1 && last % 4 = 0))))

    let private twoDigitsAtMost (s: string) (at: int) (most: int) : bool =
        isDigit s[at]
        && isDigit s[at + 1]
        && (int s[at] - int '0') * 10 + (int s[at + 1] - int '0') <= most

    /// `HH:MM:SS(.F)?` — 24-hour, `00:00:00` to `23:59:59.9999999`, with a fraction of one to seven
    /// digits and no trailing zero.
    let isTimeOfDay (s: string) : bool =
        not (isNull s)
        && s.Length >= 8
        && s[2] = ':'
        && s[5] = ':'
        && twoDigitsAtMost s 0 23
        && twoDigitsAtMost s 3 59
        && twoDigitsAtMost s 6 59
        && (s.Length = 8 || (s[8] = '.' && isFraction 7 (s.Substring 9)))

    /// `-?(0|[1-9][0-9]*)(\.F)?s` — seconds, a fraction of one to seven digits and no trailing zero,
    /// no `-0s`, and a magnitude in 100 ns ticks a signed 64-bit integer holds.
    let isDuration (s: string) : bool =
        not (isNull s)
        && s.EndsWith('s')
        && (let negative = s.StartsWith('-')

            let body =
                s.Substring((if negative then 1 else 0), s.Length - 1 - (if negative then 1 else 0))

            let dot = body.IndexOf '.'
            let whole = if dot < 0 then body else body.Substring(0, dot)
            let fraction = if dot < 0 then "" else body.Substring(dot + 1)

            isNatural whole
            && (dot < 0 || isFraction 7 fraction)
            && not (negative && whole = "0" && dot < 0)
            && (let ticks = (whole + fraction.PadRight(7, '0')).TrimStart '0'
                atMost (if negative then int64MinMagnitude else int64Max) ticks))

    /// Whether `s` is the canonical spelling of a value of `scalar`.
    let internal admits (scalar: TextScalar) (s: string) : bool =
        match scalar with
        | TextScalar.Int64 -> isInt64 s
        | TextScalar.Decimal -> isDecimal s
        | TextScalar.Bytes -> isBytes s
        | TextScalar.TimeOfDay -> isTimeOfDay s
        | TextScalar.Duration -> isDuration s

    /// The type's name in `idl.json` and in a refusal's expectation.
    let internal name (scalar: TextScalar) : string =
        match scalar with
        | TextScalar.Int64 -> "int64"
        | TextScalar.Decimal -> "decimal"
        | TextScalar.Bytes -> "bytes"
        | TextScalar.TimeOfDay -> "timeOfDay"
        | TextScalar.Duration -> "duration"

    /// **The canonical order** of a set's elements and a non-string-keyed map's keys: their canonical
    /// JSON texts compared by Unicode CODE POINT, which is the order of their UTF-8 bytes. A UTF-16
    /// string compares by code unit, which agrees with code point order everywhere except where one
    /// side holds a surrogate and the other a unit at or above U+E000; the remap puts the surrogates
    /// above those units, which is exactly where their code points are. Negative, zero or positive.
    let compare (a: string) (b: string) : int =
        let point (c: char) : int =
            let n = int c

            if n >= 0xD800 && n <= 0xDFFF then n + 0x2000
            elif n >= 0xE000 then n - 0x800
            else n

        let rec go (i: int) : int =
            if i >= a.Length || i >= b.Length then
                Operators.compare a.Length b.Length
            elif a[i] = b[i] then
                go (i + 1)
            else
                Operators.compare (point a[i]) (point b[i])

        go 0
