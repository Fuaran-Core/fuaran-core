namespace Fuaran.Core

/// A single realized scalar cell. Null/NA is a first-class case (`Null`), never a sentinel
/// value buried in the data — the in-memory form of the wire's validity mask. `Date` /
/// `Timestamp` carry their canonical ISO-8601 string (`YYYY-MM-DD` / `YYYY-MM-DDThh:mm:ss[.F]Z`,
/// `TemporalText`) so the model needs no host `DateTime` dependency and stays Fable-clean +
/// byte-identical; `Column.ofCells` and the codec's decode refuse any other text (Phase 299; a column
/// holds them as integers since Phase 422).
type Cell =
    /// A 32-bit integer. It belongs in an int column and also widens into a float or a decimal
    /// column, where the codec writes it as that type.
    | Int of int
    /// A double. `Table.validate` refuses a non-finite one, which the wire cannot carry; the order
    /// and the token treat every NaN as one value, and `-0.0` as `0`.
    | Float of float
    /// Belongs only in a bool column — no other type widens to or from it; `Cell.compare` orders
    /// `false` before `true`.
    | Bool of bool
    /// A string cell, compared ordinally. Any text is valid, including the empty string — absence
    /// is `Null`, not `""`.
    | Str of string
    /// A date's canonical `YYYY-MM-DD` text; any other text is refused by `Column.ofCells` and decode.
    | Date of string
    /// A UTC instant's canonical text: `YYYY-MM-DDThh:mm:ssZ`, or with a fraction of one to nine
    /// digits and no trailing zero, `YYYY-MM-DDThh:mm:ss.FZ` — ONE text per instant, whatever unit a
    /// column holds it at (Phase 422). Any other text is refused by `Column.ofCells` and decode.
    | Timestamp of string
    /// The absent cell, valid in a column of any type. Aggregates skip it (`First` / `Last` keep it),
    /// and on the wire it is a `false` validity bit over a placeholder slot.
    | Null
    /// An exact decimal, carried as its CANONICAL text (`DecimalText`): an optional `-`, the integer
    /// digits with no leading zero, and a `.` with fraction digits only where the fraction is
    /// non-zero, with no trailing zero — `0`, `12`, `-3.5`, `0.05`. No exponent, no separators.
    /// Text for the reason `Date` is text: no host decimal type is the same type on every host, and
    /// the digits are. Build one with `Cell.decimal`, which canonicalises; the codec does the same
    /// on decode. Declared after `Null` so every case that was already published keeps its tag.
    | Decimal of string

/// Cell construction and THE cell identity and order: `decimal` canonicalises, `token` is the
/// key every consumer partitions on, and `compare` the order every consumer sorts by.
module Cell =

    /// Is the cell the null/NA marker?
    let isNull (c: Cell) : bool = c = Null

    /// A `Decimal` cell holding the canonical form of `text`, or `None` where `text` is not decimal
    /// text (`DecimalText`). The way to build one. Since Phase 299 no entry point takes a hand-built
    /// non-canonical cell at its word — `Table.validate` (and so `ColumnCodec.tryEncode`) refuses it,
    /// and `Column.aggregate` canonicalises it or refuses text that is not decimal at all — but a
    /// reader outside this package that keys on the text (a capture key) still takes it as found,
    /// so two cells are the same value there exactly when they were both built canonical.
    let decimal (text: string) : Cell option =
        DecimalText.tryCanonical text |> Option.map Decimal

    /// The column type a present cell carries (`None` for `Null`, which is type-agnostic). A
    /// `Timestamp` carries the COARSEST unit that holds its instant exactly (Phase 422): seconds with
    /// no fraction, milliseconds for one to three fraction digits, and so on — so it widens into every
    /// finer unit (`ColumnType.widens`) and a seconds column refuses a fractional instant.
    let typeOf (c: Cell) : ColumnType option =
        match c with
        | Int _ -> Some IntType
        | Float _ -> Some FloatType
        | Bool _ -> Some BoolType
        | Str _ -> Some StringType
        | Date _ -> Some DateType
        | Timestamp s -> Some(TimestampType(TemporalText.unitOf s))
        | Decimal _ -> Some DecimalType
        | Null -> None

    // ---- Phase 315: THE cell token and THE cell order, public ----
    // Four spellings of a cell's identity token had grown up (the aggregate's `CountDistinct` key,
    // `ColumnValidator.unique`'s key, the compute layer's `cellToken`, and the capture key's cell
    // fields), and the compute copy had already drifted at `Decimal`. `token` is the one spelling a
    // consumer keys on; `compare` is the one order a consumer sorts by. Phase 299's float normal form
    // is where both come from: NaN is ONE value and sorts LAST, `-0` and `0` are one value.

    /// The column layer's total order over floats: IEEE order on the non-NaN values, `-0 = 0`, and
    /// NaN one value above every other — never delegated to a host comparison, which put NaN below
    /// everything on .NET and above everything under Fable.
    let internal compareFloat (a: float) (b: float) : int =
        match System.Double.IsNaN a, System.Double.IsNaN b with
        | true, true -> 0
        | true, false -> 1
        | false, true -> -1
        | false, false ->
            if a < b then -1
            elif a > b then 1
            else 0

    let private asFloat (c: Cell) : float option =
        match c with
        | Int i -> Some(float i)
        | Float f -> Some f
        | _ -> None

    /// A cell as exact-decimal text: a `Decimal`'s canonical form, or an `Int`'s digits (the lossless
    /// promotion `ColumnType.widens` pins). A `Float` is not one.
    let internal asDecimal (c: Cell) : string option =
        match c with
        | Decimal s -> DecimalText.tryCanonical s
        | Int i -> Some(string i)
        | _ -> None

    /// THE order between two present cells of one family (Phase 315; `Column.aggregate`'s `Min` /
    /// `Max` order since Phase 299), or `None` where they are incomparable. `Int` and `Float` compare
    /// as floats through `compareFloat` — NaN last, `-0 = 0`, one answer on every host; `Decimal`
    /// and `Int` compare EXACTLY (`DecimalText.compare`), so two decimals a float cannot tell apart
    /// are still ordered; `Bool` false before true; `Str` and `Date` by ordinal (a date's fixed-width
    /// text sorts chronologically); `Timestamp` CHRONOLOGICALLY (`TemporalText.compareInstants`,
    /// Phase 422) — not ordinally, which breaks once fraction lengths vary (`…12.5Z` is below `…12Z`
    /// as a string). Anything else — a `Decimal` beside a `Float` (`widens` refuses that
    /// retype), two families, a `Null`, a `Decimal` whose text is not decimal — is `None`.
    ///
    /// An ORDER, not an identity: `Int 1` and `Float 1.0` compare equal and have two `token`s.
    let compare (a: Cell) (b: Cell) : int option =
        match a, b with
        | (Int _ | Float _), (Int _ | Float _) -> Option.map2 compareFloat (asFloat a) (asFloat b)
        | (Decimal _ | Int _), (Decimal _ | Int _) ->
            Option.map2 (fun x y -> DecimalText.compare x y) (asDecimal a) (asDecimal b)
            |> Option.flatten
        | Bool x, Bool y ->
            Some(
                if x = y then 0
                elif x then 1
                else -1
            )
        | Str x, Str y -> Some(System.String.CompareOrdinal(x, y))
        | Date x, Date y -> Some(System.String.CompareOrdinal(x, y))
        | Timestamp x, Timestamp y -> Some(TemporalText.compareInstants x y)
        | _ -> None

    /// THE canonical, host-deterministic identity token of a cell (Phase 315; the key the compute
    /// layer's `Distinct` / `GroupBy` partition on since Phase 41). Type-tagged, so `Int 1` and
    /// `Float 1.0` are two values: `i:<digits>`, `f:<float>`, `b:1` / `b:0`, `s:` / `d:` / `t:` +
    /// the text, `m:` + the CANONICAL decimal text (so `1.50` and `1.5` are one token), and `n:`.
    /// A float is `NaN`, `Inf` or `-Inf` when non-finite and `Canon.canonicalFloat` otherwise, so
    /// NaN is one token and `-0.0` shares `0`'s — two floats share a token exactly when `compare`
    /// says they are equal. A `Decimal` whose text is not decimal text keeps that text, so it never
    /// shares a token with a decimal.
    let token (c: Cell) : string =
        match c with
        | Int i -> "i:" + string i
        | Float f ->
            "f:"
            + (if System.Double.IsNaN f then "NaN"
               elif System.Double.IsPositiveInfinity f then "Inf"
               elif System.Double.IsNegativeInfinity f then "-Inf"
               else Canon.canonicalFloat f)
        | Bool b -> "b:" + (if b then "1" else "0")
        | Str s -> "s:" + s
        | Date s -> "d:" + s
        | Timestamp s -> "t:" + s
        | Decimal s -> "m:" + (DecimalText.tryCanonical s |> Option.defaultValue s)
        | Null -> "n:"
