namespace Fuaran.Core

/// The canonical text of a `Date` and a `Timestamp` cell (Phase 299) — the two ISO-8601 forms the
/// type docs have always named, now checked where a cell enters: `Table.validate` and the codec's
/// decode refuse any other text.
///
/// A DATE is exactly `YYYY-MM-DD`; a TIMESTAMP is exactly `YYYY-MM-DDThh:mm:ssZ` — UTC, whole
/// seconds, no offset, no fraction. The year is four digits (`0000`–`9999`), the month `01`–`12`,
/// the day within its month's length in the proleptic Gregorian calendar (29 February only in a
/// leap year), the hour `00`–`23`, the minute and the second `00`–`59` (no leap second: the epoch
/// arithmetic the codec reads instants through has none). One instant has one text, so equality,
/// grouping and ordering over valid cells are string equality and ordinal order, as they already
/// were. Pure, total, FSharp.Core only, Fable-clean — no host `DateTime`.
[<RequireQualifiedAccess>]
module TemporalText =

    let private digitsAt (s: string) (from: int) (count: int) : int option =
        let rec go (k: int) (acc: int) =
            if k = from + count then
                Some acc
            else
                let c = s[k]

                if c >= '0' && c <= '9' then
                    go (k + 1) (acc * 10 + (int c - int '0'))
                else
                    None

        go from 0

    let private isLeap (y: int) =
        y % 4 = 0 && (y % 100 <> 0 || y % 400 = 0)

    let private daysIn (y: int) (m: int) =
        match m with
        | 2 -> if isLeap y then 29 else 28
        | 4
        | 6
        | 9
        | 11 -> 30
        | _ -> 31

    // The date part of both forms, over the first ten characters of a string at least that long.
    let private datePart (s: string) : bool =
        s[4] = '-'
        && s[7] = '-'
        && (match digitsAt s 0 4, digitsAt s 5 2, digitsAt s 8 2 with
            | Some y, Some m, Some d -> m >= 1 && m <= 12 && d >= 1 && d <= daysIn y m
            | _ -> false)

    /// True where `s` is a canonical date, `YYYY-MM-DD`, naming a day that exists.
    let isCanonicalDate (s: string) : bool =
        not (isNull (box s)) && s.Length = 10 && datePart s

    /// True where `s` is a canonical timestamp, `YYYY-MM-DDThh:mm:ssZ` (UTC, whole seconds),
    /// naming an instant that exists.
    let isCanonicalTimestamp (s: string) : bool =
        not (isNull (box s))
        && s.Length = 20
        && datePart s
        && s[10] = 'T'
        && s[13] = ':'
        && s[16] = ':'
        && s[19] = 'Z'
        && (match digitsAt s 11 2, digitsAt s 14 2, digitsAt s 17 2 with
            | Some h, Some mi, Some se -> h <= 23 && mi <= 59 && se <= 59
            | _ -> false)
