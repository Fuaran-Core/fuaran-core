namespace Fuaran.Core

/// The resolution a timestamp column stores its instants at (Phase 422), as Arrow's
/// `Timestamp(unit)`: whole seconds, or a second and a fraction of it scaled to milliseconds,
/// microseconds or nanoseconds. A coarser unit widens into a finer one (`ColumnType.widens`). It is
/// a storage resolution, not a unit of measure.
[<RequireQualifiedAccess>]
type TimeUnit =
    /// Whole seconds — the `timestamp` tag, the only unit before Phase 422.
    | Seconds
    /// Thousandths of a second — the `timestamp_ms` tag.
    | Milliseconds
    /// Millionths of a second — the `timestamp_us` tag.
    | Microseconds
    /// Billionths of a second — the `timestamp_ns` tag.
    | Nanoseconds

/// The fraction digits and scale of each `TimeUnit`.
[<RequireQualifiedAccess>]
module TimeUnit =

    /// How many decimal fraction digits the unit holds: 0, 3, 6 or 9.
    let digits (u: TimeUnit) : int =
        match u with
        | TimeUnit.Seconds -> 0
        | TimeUnit.Milliseconds -> 3
        | TimeUnit.Microseconds -> 6
        | TimeUnit.Nanoseconds -> 9

    /// How many of the unit make one second: 1, 1000, 10^6 or 10^9. A stored fraction lies in
    /// `[0, scale)`.
    let scale (u: TimeUnit) : int =
        match u with
        | TimeUnit.Seconds -> 1
        | TimeUnit.Milliseconds -> 1000
        | TimeUnit.Microseconds -> 1000000
        | TimeUnit.Nanoseconds -> 1000000000

    /// The coarsest unit holding `d` fraction digits exactly (0 seconds, 1-3 milliseconds, 4-6
    /// microseconds, 7-9 nanoseconds; more than nine has no unit and reads as nanoseconds).
    let ofDigits (d: int) : TimeUnit =
        if d <= 0 then TimeUnit.Seconds
        elif d <= 3 then TimeUnit.Milliseconds
        elif d <= 6 then TimeUnit.Microseconds
        else TimeUnit.Nanoseconds

    /// Does an instant held in `from` lose nothing held in `target`? True when `target` is at
    /// least as fine — the timestamp arm of the widening lattice.
    let widens (from: TimeUnit) (target: TimeUnit) : bool = digits from <= digits target

/// A proleptic Gregorian civil date (Phase 422): the year, the month `1`–`12` and the day of the
/// month — what `TemporalText.civilOfDays` answers for a day count.
type CivilDate =
    {
        /// The year, `0`–`9999` over the canonical range.
        Year: int
        /// The month, `1`–`12`.
        Month: int
        /// The day of the month, `1`–`31`.
        Day: int
    }

/// The canonical text of a `Date` and a `Timestamp` cell (Phase 299), and since Phase 422 the
/// integer form a column stores them in. A DATE is exactly `YYYY-MM-DD`; a TIMESTAMP is
/// `YYYY-MM-DDThh:mm:ssZ` or `YYYY-MM-DDThh:mm:ss.FZ` — UTC, no offset, `F` one to nine fraction
/// digits with no trailing zero, present only where the fraction is non-zero (the MINIMAL text, the
/// `DecimalText` rule; DECISIONS.md D143.3). The year is four digits (`0000`–`9999`), the month
/// `01`–`12`, the day within its month in the proleptic Gregorian calendar, the hour `00`–`23`, the
/// minute and the second `00`–`59` (no leap second). One instant has ONE text, whatever unit a column
/// holds it at; a whole-second instant's text is the pre-422 text.
///
/// The integer forms: a date is its day count since 1970-01-01 (`daysOfDate`, `dateOfDays`); an
/// instant is its floor epoch second as an integer-valued float and its fraction of a second scaled
/// to a unit (`tryInstant`, `instantText`). The arithmetic is days-from-civil and civil-from-days in
/// pure `int` arithmetic, so every host computes one calendar. FSharp.Core only, Fable-clean — no
/// host date type.
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

    // ---- the calendar, in integers ----

    /// Days since 1970-01-01 of the proleptic Gregorian civil date `y-m-d` (Hinnant's
    /// days-from-civil), for any `y` in `0..9999`, `m` in `1..12` and `d` in `1..31`. Pure `int`
    /// arithmetic: every division is of a non-negative dividend, so truncation and floor agree on
    /// every host.
    let daysOfCivil (y: int) (m: int) (d: int) : int =
        let y = if m <= 2 then y - 1 else y
        // Shift by 400-year eras so the dividend stays non-negative for the year -1 the shift reaches.
        let y4 = y + 400
        let era = y4 / 400
        let yoe = y4 - era * 400
        let mp = (m + 9) % 12
        let doy = (153 * mp + 2) / 5 + d - 1
        let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy
        (era - 1) * 146097 + doe - 719468

    /// The civil date of a day count since 1970-01-01 (Hinnant's civil-from-days), the inverse of
    /// `daysOfCivil` over the canonical range.
    let civilOfDays (days: int) : CivilDate =
        // Shift by one 400-year era (146097 days) so every intermediate is non-negative over the
        // canonical range; the shift is undone on the year.
        let z = days + 719468 + 146097
        let era = z / 146097
        let doe = z - era * 146097
        let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365
        let doy = doe - (365 * yoe + yoe / 4 - yoe / 100)
        let mp = (5 * doy + 2) / 153
        let d = doy - (153 * mp + 2) / 5 + 1
        let m = if mp < 10 then mp + 3 else mp - 9

        { Year = yoe + (era - 1) * 400 + (if m <= 2 then 1 else 0)
          Month = m
          Day = d }

    /// The day count of `0000-01-01`, the first day the canonical form spells.
    [<Literal>]
    let MinDay = -719528

    /// The day count of `9999-12-31`, the last day the canonical form spells.
    [<Literal>]
    let MaxDay = 2932896

    /// The epoch second of `0000-01-01T00:00:00Z`.
    let minSecond: float = float MinDay * 86400.0

    /// The epoch second of `9999-12-31T23:59:59Z`.
    let maxSecond: float = float MaxDay * 86400.0 + 86399.0

    let private pad (width: int) (n: int) : string = (string n).PadLeft(width, '0')

    // The date part of both forms, over the first ten characters of a string at least that long.
    let private datePart (s: string) : (int * int * int) option =
        if s[4] = '-' && s[7] = '-' then
            match digitsAt s 0 4, digitsAt s 5 2, digitsAt s 8 2 with
            | Some y, Some m, Some d when m >= 1 && m <= 12 && d >= 1 && d <= daysIn y m -> Some(y, m, d)
            | _ -> None
        else
            None

    // ---- dates ----

    /// The day count of a canonical date, or `None` for any other text.
    let tryDays (s: string) : int option =
        if isNull (box s) || s.Length <> 10 then
            None
        else
            datePart s |> Option.map (fun (y, m, d) -> daysOfCivil y m d)

    /// True where `s` is a canonical date, `YYYY-MM-DD`, naming a day that exists.
    let isCanonicalDate (s: string) : bool = (tryDays s).IsSome

    /// Is `days` a day the canonical form spells (`0000-01-01`..`9999-12-31`)?
    let isDayInRange (days: int) : bool = days >= MinDay && days <= MaxDay

    /// The canonical text of a day count in range (`isDayInRange`). Outside it the text is not
    /// canonical; `Table.validate` refuses such a day before anything renders it.
    let dateText (days: int) : string =
        let c = civilOfDays days
        pad 4 c.Year + "-" + pad 2 c.Month + "-" + pad 2 c.Day

    // ---- instants ----

    // The fraction of a canonical-shaped instant text: its digit count and value, the text's length
    // telling where it ends (`hh:mm:ssZ` is 20 characters; `hh:mm:ss.FZ` 22 to 30).
    let private fractionPart (s: string) : (int * int) option =
        if s.Length = 20 then
            if s[19] = 'Z' then Some(0, 0) else None
        elif s.Length >= 22 && s.Length <= 30 && s[19] = '.' && s[s.Length - 1] = 'Z' then
            let n = s.Length - 21

            match digitsAt s 20 n with
            | Some f when s[s.Length - 2] <> '0' -> Some(n, f)
            | _ -> None
        else
            None

    /// The epoch second (floor, integer-valued) of a canonical instant text and its fraction of a
    /// second as `(digits, value)` — `None` for any other text.
    let private parseInstant (s: string) : (float * int * int) option =
        if isNull (box s) || s.Length < 20 then
            None
        elif s[10] <> 'T' || s[13] <> ':' || s[16] <> ':' then
            None
        else
            match datePart s, digitsAt s 11 2, digitsAt s 14 2, digitsAt s 17 2, fractionPart s with
            | Some(y, m, d), Some h, Some mi, Some se, Some(n, f) when h <= 23 && mi <= 59 && se <= 59 ->
                let second = float (daysOfCivil y m d) * 86400.0 + float (h * 3600 + mi * 60 + se)

                Some(second, n, f)
            | _ -> None

    let private pow10 (k: int) : int =
        let mutable p = 1

        for _ in 1..k do
            p <- p * 10

        p

    /// True where `s` is a canonical timestamp (any unit), naming an instant that exists.
    let isCanonicalTimestamp (s: string) : bool = (parseInstant s).IsSome

    /// The coarsest unit holding the canonical instant `s` exactly — seconds for a text with no
    /// fraction, and for any text that is not canonical.
    let unitOf (s: string) : TimeUnit =
        match parseInstant s with
        | Some(_, n, _) -> TimeUnit.ofDigits n
        | None -> TimeUnit.Seconds

    /// The integer form of a canonical instant text in `unit`: its epoch second and its fraction
    /// scaled to the unit — `None` where the text is not canonical or its fraction is finer than the
    /// unit holds.
    let tryInstant (unit: TimeUnit) (s: string) : (float * int) option =
        match parseInstant s with
        | Some(second, n, f) when n <= TimeUnit.digits unit -> Some(second, f * pow10 (TimeUnit.digits unit - n))
        | _ -> None

    /// Is an epoch second and a fraction a valid instant in `unit`: the second a whole number the
    /// canonical form spells (`0000`..`9999`), the fraction in `[0, scale)`?
    let isInstantInRange (unit: TimeUnit) (second: float) (fraction: int) : bool =
        second = floor second
        && second >= minSecond
        && second <= maxSecond
        && fraction >= 0
        && fraction < TimeUnit.scale unit

    /// The canonical text of an instant in range (`isInstantInRange`): the minimal text, the
    /// fraction written without trailing zeros and only where it is non-zero.
    let instantText (unit: TimeUnit) (second: float) (fraction: int) : string =
        let day = floor (second / 86400.0)
        let sod = int (second - day * 86400.0)

        let frac =
            if fraction = 0 then
                ""
            else
                let digits = (pad (TimeUnit.digits unit) fraction).TrimEnd('0')
                "." + digits

        dateText (int day)
        + "T"
        + pad 2 (sod / 3600)
        + ":"
        + pad 2 (sod % 3600 / 60)
        + ":"
        + pad 2 (sod % 60)
        + frac
        + "Z"

    /// THE order of two timestamp texts (Phase 422, DECISIONS.md D143.3): chronological over
    /// canonical texts — the fixed-width `YYYY-MM-DDThh:mm:ss` prefix ordinally, then the fraction
    /// digits ordinally, which with no trailing zero is their numeric order — and a text that is not
    /// canonical after every canonical one, ordinally among its own kind. Total and transitive over
    /// any text; ordinal string order is not chronological once fraction lengths vary.
    let compareInstants (a: string) (b: string) : int =
        let sign (c: int) =
            if c < 0 then -1
            elif c > 0 then 1
            else 0

        match isCanonicalTimestamp a, isCanonicalTimestamp b with
        | true, true ->
            let head = System.String.CompareOrdinal(a.Substring(0, 19), b.Substring(0, 19))

            if head <> 0 then
                sign head
            else
                let fa = if a.Length = 20 then "" else a.Substring(20, a.Length - 21)
                let fb = if b.Length = 20 then "" else b.Substring(20, b.Length - 21)
                sign (System.String.CompareOrdinal(fa, fb))
        | true, false -> -1
        | false, true -> 1
        | false, false -> sign (System.String.CompareOrdinal(a, b))
