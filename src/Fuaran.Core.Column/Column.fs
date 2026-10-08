namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Column (Phase 28) — the relational/columnar data strand, a new
//  Core substrate parallel to the tree/op-stream spine. A typed, null-aware,
//  Arrow-compatible columnar model + its canonical wire codec. It is the data
//  substrate the compute layer operates on (`Fuaran.Core.DataFrame`, produced by
//  its own repository since 0.33.0 — DECISIONS.md D66), the shape the `Query` seam
//  produces, and the shape the UI `DataSource` binding serialises (Compute Layer
//  spec §1).
//
//  It introduces no tree-witness field and no base node type — it is a separate,
//  self-contained data strand. FSharp.Core only; Fable-clean on encode and decode
//  (it reuses `Fuaran.Core.Wire`'s canonical-float / escaping / parser rules, so a
//  numeric column is byte-identical across the .NET and Fable hosts).
// ============================================================================

/// The fixed, Arrow-compatible scalar type set a column ranges over (spec §1). Closed by
/// intent — a new scalar type is an additive case, never an open extension point.
type ColumnType =
    /// 32-bit signed integers, tagged `int`. An int `Sum` outside the int32 range is refused, never
    /// wrapped.
    | IntType
    /// IEEE doubles, tagged `float`; `Int` cells widen into it. Present values must be finite to
    /// encode — the wire has no NaN or infinity.
    | FloatType
    /// Booleans, tagged `bool`; ordered `false` before `true`.
    | BoolType
    /// Free text, tagged `string`; ordered ordinally, never by culture.
    | StringType
    /// Calendar days as canonical `YYYY-MM-DD` text, tagged `date` (`TemporalText.isCanonicalDate`).
    | DateType
    /// UTC instants as canonical `YYYY-MM-DDThh:mm:ssZ` text, tagged `timestamp`; the decoder also
    /// reads an epoch number in seconds or milliseconds.
    | TimestampType
    /// An EXACT decimal (`0.33.0`): a value a `float` cannot hold without rounding, such as a sum of
    /// money. Unparameterised — it carries no precision and no scale, because the text a cell
    /// carries has exactly the digits it has. A host that maps it to a fixed-scale store type reads
    /// the scale off the data or declares it in its own model.
    | DecimalType

/// A single realized scalar cell. Null/NA is a first-class case (`Null`), never a sentinel
/// value buried in the data — the in-memory form of the wire's validity mask. `Date` /
/// `Timestamp` carry their canonical ISO-8601 string (`YYYY-MM-DD` / `YYYY-MM-DDThh:mm:ssZ`,
/// `TemporalText`) so the model needs no host `DateTime` dependency and stays Fable-clean +
/// byte-identical; `Table.validate` and the codec's decode refuse any other text (Phase 299).
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
    /// A date's canonical `YYYY-MM-DD` text; any other text is refused by `Table.validate` and decode.
    | Date of string
    /// A UTC instant's canonical `YYYY-MM-DDThh:mm:ssZ` text (whole seconds); any other text is
    /// refused by `Table.validate` and decode.
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

/// A typed, null-aware column. `Cells` co-indexes with the table's rows; a `Null` cell is the
/// validity-mask "absent" marker. `Type` is the declared column type; a present cell must be of a
/// type that WIDENS into it (`ColumnType.widens`) — the codec refuses any other at decode,
/// `Table.validate` (and so `ColumnCodec.tryEncode`) before encode, and `Column.aggregate` by name
/// (Phase 299). `Column.create` checks nothing; a column built by hand is checked where it is used.
type Column =
    {
        /// The key a table matches against its schema entry and looks the column up by; unique
        /// within a table (`Table.validate`), compared exactly.
        Name: string
        /// The declared type, which must equal the column's schema entry; a present cell must be of
        /// a type that widens into it.
        Type: ColumnType
        /// One cell per row, in row order. A linked list, so an indexed read (`Column.cell`) is O(i).
        Cells: Cell list
    }

/// A `(name, type)` ordered schema — the column order of a table follows it.
type Schema = (string * ColumnType) list

/// An embedded columnar table: its schema + the columns, every column the same length. The
/// SCHEMA is the order authority: the encoder walks it and looks each column up by name, and the
/// decoder returns the columns in schema order. The `Columns` list itself may be in any order —
/// `Table.validate` holds the two name SETS equal and does not ask for one order — so a table built
/// with its columns in another order encodes correctly and decodes back in schema order
/// (`proofs/WireColumn.fst`: the round trip is to a normal form, and this is one of its three
/// reasons).
type Table =
    {
        /// The column names and types, in the table's column order; no name may appear twice.
        Schema: Schema
        /// The columns, in any order, matched to `Schema` by name; all the same length.
        Columns: Column list
    }

/// A data source: embedded columns, or a host-resolved named `Ref` (spec §1 — the
/// `Binding.Query` by-reference precedent). The evaluator resolves a `Ref` through a caller
/// supplied resolver; the wire carries the name, never the rows — and no schema: the encoder
/// writes the empty `"schema":[]` a reader of the embedded form expects, and the decoder reads a
/// `ref` source with or without one and keeps none (Phase 299 dropped the rule that a `ref` had to
/// carry a schema the decoder then discarded).
type DataSource =
    /// The rows travel inline. `ColumnCodec.tryEncode` validates the table first; `encode` assumes
    /// it is valid.
    | Embedded of Table
    /// A name the host resolves to rows; the codec carries it uninterpreted and always encodes it.
    | Ref of string

/// THE CODEC ENVELOPE — the closed set of refusals of the columnar codec and of `Table.validate`,
/// the substrate's recoverable error discipline (GP4/GP5): every failure *names what went wrong*
/// and, where a closed set is expected, *enumerates the alternatives*. Its size is stated nowhere
/// but in this type: a new case is a breaking-source change for every exhaustive match (FS0025),
/// and is classed as one in STABILITY.md.
type ColumnError =
    /// The input was not valid JSON at all — the parser's structured failure (`Json.parseDetailed`):
    /// its classified kind, its message and its position (Phase 299; it carried the string form,
    /// already prefixed, before).
    | NotJson of error: JsonError
    /// A required field was absent from an object (`schema` / `values` / `validity` / `name` / `type`).
    | MissingField of field: string
    /// A value had the wrong JSON shape for its position (expected object/array/string where another
    /// kind appeared), or a cell's TEXT is not its type's canonical form — decimal text, or an
    /// ISO-8601 date or timestamp (`DecimalText`, `TemporalText`).
    | MalformedShape of detail: string
    /// A `type` tag was not one of the fixed scalar set; `expected` lists the valid tags.
    | UnknownType of got: string * expected: string list
    /// A present cell's type does not widen into its column's declared type — its JSON kind on
    /// decode, its `Cell` case in `Table.validate` (Phase 299) — or a column's `Type` disagrees with
    /// its schema entry.
    | TypeMismatch of column: string * expected: string * got: string
    /// A column's `values` and `validity` arrays had different lengths (they must co-index).
    | LengthMismatch of column: string * values: int * validity: int
    /// A present `Float` cell was non-finite (`NaN` / `Infinity` / `-Infinity`); the Fuaran wire has no
    /// non-finite float (the same posture as the tree wire's `Json.tryRender`, Phase 12) — `encode`
    /// would otherwise emit the JSON *string* `"NaN"`, which fails to decode back to a `FloatType` cell.
    | NonFiniteFloat of column: string * value: string
    /// The `Table` was structurally malformed (a duplicate schema or column name, or a schema/column
    /// name disagreement) — `Table.validate` names the fault.
    | Malformed of detail: string
    /// The table's columns are not one length (Phase 299): `column` has `got` rows where the first
    /// column has `expected`. Distinct from `LengthMismatch`, which is ONE column's `values` and
    /// `validity` arrays disagreeing on the wire.
    | RaggedColumns of column: string * expected: int * got: int

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
            let negative = s.[0] = '-'
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
            let d = digit a.[i] + digit b.[i] + carry
            out.[i + 1] <- d % 10
            carry <- d / 10

        out.[0] <- carry
        digitsText out

    /// `a - b` over aligned magnitudes with `a >= b`.
    let private subMagnitudes (a: string) (b: string) : string =
        let out = Array.zeroCreate<int> a.Length
        let mutable borrow = 0

        for i in a.Length - 1 .. -1 .. 0 do
            let d = digit a.[i] - digit b.[i] - borrow

            if d < 0 then
                out.[i] <- d + 10
                borrow <- 1
            else
                out.[i] <- d
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
                let c = s.[k]

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
        s.[4] = '-'
        && s.[7] = '-'
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
        && s.[10] = 'T'
        && s.[13] = ':'
        && s.[16] = ':'
        && s.[19] = 'Z'
        && (match digitsAt s 11 2, digitsAt s 14 2, digitsAt s 17 2 with
            | Some h, Some mi, Some se -> h <= 23 && mi <= 59 && se <= 59
            | _ -> false)

/// The wire tags of the scalar types and the widening lattice — the one place both the codec and
/// schema compatibility read "is this retype safe" from.
module ColumnType =

    /// The canonical wire tag for a column type (the fixed scalar-set vocabulary).
    let tag (t: ColumnType) : string =
        match t with
        | IntType -> "int"
        | FloatType -> "float"
        | BoolType -> "bool"
        | StringType -> "string"
        | DateType -> "date"
        | TimestampType -> "timestamp"
        | DecimalType -> "decimal"

    /// The type's position in `all` — the identity `widens` compares by, as an integer (Phase 353).
    let private ordinal (t: ColumnType) : int =
        match t with
        | IntType -> 0
        | FloatType -> 1
        | BoolType -> 2
        | StringType -> 3
        | DateType -> 4
        | TimestampType -> 5
        | DecimalType -> 6

    /// The full closed set of valid type tags — the `UnknownType` enumeration (and the encode order).
    let all =
        [ IntType
          FloatType
          BoolType
          StringType
          DateType
          TimestampType
          DecimalType ]

    /// The wire tags in `all`'s order — the `expected` list an `UnknownType` refusal carries.
    let allTags = all |> List.map tag

    /// Resolve a wire tag to its type, or `None` for an unknown tag.
    let ofTag (s: string) : ColumnType option =
        all |> List.tryFind (fun t -> tag t = s)

    /// The pinned type-widening lattice (Phase 33). A `from`→`target` change is a *safe widening* iff it
    /// is the identity or the one lossless promotion the rest of the strand already pins: `Int → Float`
    /// (`ColumnCodec.decodeCell` decodes a JSON int into a `FloatType` column; the compute layer's
    /// `DataFrame` arithmetic promotes int operands to float). This is the single source of truth for "is a retype safe" — the
    /// schema-compatibility check and the codec/evaluator coercion agree by construction, not by a second
    /// rule-set.
    ///
    /// `Int → Decimal` is the second lossless promotion (`0.33.0`), and the codec agrees with it the
    /// same way: `ColumnCodec.decodeCell` decodes a JSON int into a `DecimalType` column. `Float →
    /// Decimal` is NOT a widening, in either direction: a float is an approximation and a decimal is
    /// a statement of digits, so a retype between them changes what the column claims.
    ///
    /// Read by pattern, not by `=` (Phase 353): a union's `=` is a structural-equality call under
    /// Fable, and `Column.aggregate` asks this once per cell. The identity is `ordinal`'s, whose
    /// match is exhaustive, so a new type cannot silently fail to widen into itself.
    let widens (from: ColumnType) (target: ColumnType) : bool =
        match from, target with
        | IntType, (FloatType | DecimalType) -> true
        | _ -> ordinal from = ordinal target

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

    /// The column type a present cell carries (`None` for `Null`, which is type-agnostic).
    let typeOf (c: Cell) : ColumnType option =
        match c with
        | Int _ -> Some IntType
        | Float _ -> Some FloatType
        | Bool _ -> Some BoolType
        | Str _ -> Some StringType
        | Date _ -> Some DateType
        | Timestamp _ -> Some TimestampType
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
    /// are still ordered; `Bool` false before true; `Str` / `Date` / `Timestamp` by ordinal (ISO text
    /// sorts chronologically). Anything else — a `Decimal` beside a `Float` (`widens` refuses that
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
        | Timestamp x, Timestamp y -> Some(System.String.CompareOrdinal(x, y))
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

/// A group/window aggregate function (Phase 36, lifted from the DataFrame evaluator's `GroupBy` so it
/// is a public, single-source surface). `Count` is non-null count; `Sum` keeps the source numeric type;
/// `Mean`/`Median`/`StdDev` are `float`; `Min`/`Max`/`First`/`Last` keep the source type.
type AggFn =
    /// Numeric only. An int sum is checked against int32 (overflow is named, never wrapped), a decimal
    /// sum is exact, a float sum folds left to right. With no present value the sum is `Null`, not
    /// zero.
    | Sum
    /// Numeric only, a `Float` (decimals at their nearest float); `Null` over no present value.
    | Mean
    /// The least present cell by `Cell.compare` (NaN sorts last, so it is the minimum only of an
    /// all-NaN column); `Null` when nothing is present.
    | Min
    /// The greatest present cell by `Cell.compare` — NaN where the column holds one; `Null` when
    /// nothing is present.
    | Max
    /// The number of present cells, as an `Int`; `0` for an all-null column.
    | Count
    /// Numeric only, a `Float`: the middle value, or the mean of the two middle values for an even
    /// count, with NaN counted at the top; `Null` over no present value.
    | Median
    /// Numeric only, the POPULATION standard deviation (divides by `n`) as a `Float`; one value
    /// gives `0`, and no present value gives `Null`.
    | StdDev
    /// The first row's cell — a `Null` included, not skipped; `Null` for an empty column.
    | First
    /// The last row's cell — a `Null` included, not skipped; `Null` for an empty column.
    | Last
    /// Phase 101 — the count of DISTINCT present values (nulls skipped, so `CountDistinct` over an
    /// all-null column is `0`, exactly as `Count` is). Distinctness is the SAME canonical token the
    /// compute layer's `Distinct` / `GroupBy` partition on (Phase 41), so `NaN` collapses to one value,
    /// `-0.0`/`0.0` coincide, and two cells of different types never collide — the count is
    /// host-identical, not host-comparison-dependent.
    | CountDistinct

/// Why an aggregate was refused (Phase 36) — recoverable + enumerated (GP5), never a throw (GP4). A
/// numeric aggregate (`Sum`/`Mean`/`Median`/`StdDev`) over a non-numeric column names the expected
/// types; an integer `Sum` outside the int32 band is a named overflow (the pinned no-silent-wrap posture
/// shared with the compute layer's evaluator, Phase 39).
type AggregateError =
    /// A numeric aggregate over a non-numeric column: `fn` is the aggregate's tag (`"sum"`, …),
    /// `colType` the column's type tag, and `expected` the numeric tags (`int`, `float`, `decimal`).
    | IncompatibleAggType of fn: string * colType: string * expected: string list
    /// An answer outside its type's range: an int `Sum` past int32, a float `Sum` or a float
    /// statistic that left the float range over finite input, or a decimal too large to read as a
    /// float for a float-valued aggregate. `detail` says which, with the offending value or column.
    | AggregateOverflow of detail: string
    /// A present cell outside its column's type (Phase 299): the column, its declared type, and the
    /// cell — its type's tag, or, for a `Decimal` cell, the text that is not decimal text. The
    /// aggregate used to read cells by shape and trust the column's type: a `Float` in an int
    /// column was truncated into an int `Sum`, and one in a decimal column was dropped from `Sum`
    /// and counted in `Mean`. Now it is refused, by name, before any aggregate reads it.
    | CellOutsideType of column: string * colType: string * cell: string

/// Column reads and the pinned aggregate semantics — the single `aggregate` the compute layer's
/// grouping and pivoting call rather than copy.
module Column =

    /// The number of rows in a column.
    let length (c: Column) : int = List.length c.Cells

    /// The cell at row `i` (`Null` for an out-of-range index — total).
    ///
    /// O(i) in the row index, because `Cells` is a linked list and this walks it. That is a property
    /// of the representation, not of this function: the only way to make a single indexed read O(1)
    /// is to change what `Cells` is, which would break every consumer's construction sites for a
    /// cost nobody pays once the CALLERS stop indexing (Phase 206). A loop that wants every row
    /// reads the column list once, in order, and does not come through here at all.
    ///
    /// What did change is the constant: the bounds test was a second full `List.length` walk of the
    /// same list before the `List.item` walk, so every read cost one-and-a-half traversals where
    /// `List.tryItem` — total for a negative index as well as a too-large one — costs at most one.
    let cell (i: int) (c: Column) : Cell =
        match List.tryItem i c.Cells with
        | Some v -> v
        | None -> Null

    /// Build a typed column from a name + cell list (no validation — the codec validates the wire).
    let create (name: string) (ty: ColumnType) (cells: Cell list) : Column =
        { Name = name
          Type = ty
          Cells = cells }

    // ---- pinned aggregate semantics (Phase 36) — the single source the compute layer's GroupBy/Pivot call ----

    let private aggAsNum (c: Cell) : float option =
        match c with
        | Int i -> Some(float i)
        | Float f -> Some f
        | Decimal s -> DecimalText.tryToFloat s
        | _ -> None

    // ---- THE float order, the cell order and the cell token (Phase 299; public since Phase 315) ----
    // One normal form of a float, which the aggregate ORDER and the distinct TOKEN both read, so
    // they agree by construction. Since Phase 315 both are `Cell`'s public `compare` / `token`
    // (`Cell.compareFloat` the float order beneath them), read here rather than kept as private
    // copies — the aggregate keys `CountDistinct` on the token every consumer keys on.

    let private aggFnTag =
        function
        | Sum -> "sum"
        | Mean -> "mean"
        | Min -> "min"
        | Max -> "max"
        | Count -> "count"
        | Median -> "median"
        | StdDev -> "stddev"
        | First -> "first"
        | Last -> "last"
        | CountDistinct -> "countDistinct"

    /// A present cell as `aggregate` admits it (Phase 299): a cell of a type that widens into
    /// `col.Type` passes, a `Decimal` cell passes CANONICALISED, and anything else — a cell outside
    /// the column's type, or a `Decimal` whose text is not decimal text — is a named
    /// `CellOutsideType`. `Null` is type-agnostic and passes.
    let private admit (col: Column) (c: Cell) : Result<Cell, AggregateError> =
        match c with
        | Null -> Ok Null
        | Decimal s when
            (match col.Type with
             | DecimalType -> true
             | _ -> false)
            ->
            match DecimalText.tryCanonical s with
            | Some canonical -> Ok(Decimal canonical)
            | None ->
                Error(CellOutsideType(col.Name, ColumnType.tag col.Type, "decimal text '" + s + "' (not decimal)"))
        | _ ->
            match Cell.typeOf c with
            | Some t when ColumnType.widens t col.Type -> Ok c
            | Some t -> Error(CellOutsideType(col.Name, ColumnType.tag col.Type, ColumnType.tag t))
            | None -> Ok c

    let private checkedSumInt (r: int64) : Result<Cell, AggregateError> =
        if r >= int64 System.Int32.MinValue && r <= int64 System.Int32.MaxValue then
            Ok(Int(int r))
        else
            Error(AggregateOverflow("sum overflowed int32: " + string r))

    /// The output column type of an aggregate over a source of type `srcType` (Phase 36) — `Count` is
    /// int; `Mean`/`Median`/`StdDev` are float; the rest keep the source type.
    let aggType (fn: AggFn) (srcType: ColumnType) : ColumnType =
        match fn with
        | Count
        | CountDistinct -> IntType
        | Mean
        | Median
        | StdDev -> FloatType
        | Sum
        | Min
        | Max
        | First
        | Last -> srcType

    let private isFiniteFloat (f: float) : bool =
        not (System.Double.IsNaN f || System.Double.IsInfinity f)

    /// The mean and the POPULATION standard deviation of finite values, computed so that no
    /// intermediate leaves the float range (Phase 306) — the path `aggregate` takes only when the
    /// plain formulas overflowed on finite input. The values are scaled by a power of two (exact
    /// in binary floating point) until the largest magnitude is at most one, the two moments are
    /// accumulated by Welford's recurrence — a running mean and a running sum of squared
    /// deviations, neither of which can exceed the count once every value is within one — and the
    /// results are scaled back. Both answers are bounded by the largest input magnitude, so
    /// scaling back cannot overflow except by the last rounding, which the caller names.
    let private scaledMoments (xs: ResizeArray<float>) : float * float =
        let mutable maxAbs = 0.0

        for x in xs do
            if abs x > maxAbs then
                maxAbs <- abs x

        let mutable scale = 1.0

        while maxAbs * scale > 1.0 do
            scale <- scale * 0.5

        let mutable mean = 0.0
        let mutable m2 = 0.0
        let mutable k = 0

        for x in xs do
            k <- k + 1
            let y = x * scale
            let d = y - mean
            mean <- mean + d / float k
            m2 <- m2 + d * (y - mean)

        mean / scale, sqrt (m2 / float k) / scale

    /// What the admission pass learns for every aggregate, whatever it folds (Phase 388): the first
    /// and the last admitted cell (a `Null` included) and how many were PRESENT.
    type private Pass = { First: Cell; Last: Cell; Count: int }

    /// One aggregate as a fold (Phase 388): `Step` folds one admitted PRESENT cell, in row order, and
    /// `Finish` answers from what was folded and the pass. `admitAll` drives every one of them, so an
    /// aggregate is one walk of the column whatever it keeps.
    type private AggFold =
        { Step: Cell -> unit
          Finish: Pass -> Result<Cell, AggregateError> }

    /// The one pass (Phase 306): every cell admitted in row order (Phase 299), each present one
    /// handed to `step`. The first cell outside the column's type stops the pass and is the answer —
    /// the refusal that outranks every other.
    let private admitAll (col: Column) (step: Cell -> unit) : Result<Pass, AggregateError> =
        let mutable outside: AggregateError option = None
        let mutable seenAny = false
        let mutable first = Null
        let mutable last = Null
        let mutable count = 0
        let mutable rest = col.Cells

        while outside.IsNone && not rest.IsEmpty do
            match admit col rest.Head with
            | Error e -> outside <- Some e
            | Ok cell ->
                if not seenAny then
                    first <- cell
                    seenAny <- true

                last <- cell

                match cell with
                | Null -> ()
                | _ ->
                    count <- count + 1
                    step cell

            rest <- rest.Tail

        match outside with
        | Some e -> Error e
        | None ->
            Ok
                { First = first
                  Last = last
                  Count = count }

    /// A fold that keeps nothing: the answer is the pass's (`Count`, `First`, `Last`) or a refusal.
    let private passFold (finish: Pass -> Result<Cell, AggregateError>) : AggFold = { Step = ignore; Finish = finish }

    /// `CountDistinct` (Phase 101): the distinct present values, by the canonical token.
    let private distinctFold () : AggFold =
        let tokens = System.Collections.Generic.HashSet<string>()

        { Step = fun cell -> tokens.Add(Cell.token cell) |> ignore
          Finish = fun _ -> Ok(Int tokens.Count) }

    /// `Min` / `Max` (Phase 299): the least / greatest present cell by `Cell.compare`, an incomparable
    /// pair keeping the incumbent; `Null` over no present cell.
    let private extremeFold (isMin: bool) : AggFold =
        let best = ref Null
        let seen = ref false

        { Step =
            fun cell ->
                if not seen.Value then
                    best.Value <- cell
                    seen.Value <- true
                else
                    match Cell.compare best.Value cell with
                    | Some c ->
                        if isMin <> (c <= 0) then
                            best.Value <- cell
                    | None -> ()
          Finish = fun _ -> Ok best.Value }

    /// `Sum` over a decimal column (`0.33.0`): EXACT — never through `float`, so nothing is rounded
    /// and nothing overflows — and a `Decimal`; `Null` over no present cell.
    let private decimalSumFold () : AggFold =
        let total = ref DecimalText.zero
        let seen = ref false

        { Step =
            fun cell ->
                match Cell.asDecimal cell with
                | Some d ->
                    if seen.Value then
                        total.Value <- DecimalText.add total.Value d |> Option.defaultValue total.Value
                    else
                        total.Value <- d
                        seen.Value <- true
                | None -> ()
          Finish = fun _ -> Ok(if seen.Value then Decimal total.Value else Null) }

    /// `Sum` over an int column: folded in int64, then range-checked (Phase 39 no-silent-wrap), so the
    /// result is host-deterministic. Admitted cells of an int column are `Int`s, so nothing is
    /// truncated.
    let private intSumFold () : AggFold =
        let total = ref 0L
        let seen = ref false

        { Step =
            fun cell ->
                match cell with
                | Int i ->
                    total.Value <- total.Value + int64 i
                    seen.Value <- true
                | _ -> ()
          Finish = fun _ -> if seen.Value then checkedSumInt total.Value else Ok Null }

    /// The float fold every float-valued aggregate shares (Phase 306): the running total, left to
    /// right from zero; how many numbers; whether every one was finite; the numbers themselves where
    /// the aggregate keeps them (a sort and a second moment need them); and the first decimal past
    /// the float range, after which nothing more is folded.
    type private FloatAcc =
        { mutable Total: float
          mutable Numbers: int
          mutable AllFinite: bool
          mutable PastFloat: AggregateError option
          Kept: ResizeArray<float> }

    /// A float answer over finite input: itself where it is finite, and a named overflow where even
    /// the form that cannot overflow an intermediate left the range.
    let private finiteOr (col: Column) (what: string) (f: float) : Result<Cell, AggregateError> =
        if isFiniteFloat f then
            Ok(Float f)
        else
            Error(AggregateOverflow(col.Name + ": " + what + " overflowed the float range"))

    /// The float fold over a NUMERIC column, answered by `finish` unless a decimal past the float
    /// range was met — that refusal ranks below the admission and the non-numeric ones, which the
    /// pass and `aggregate` give first.
    let private floatFold
        (col: Column)
        (fn: AggFn)
        (keeps: bool)
        (finish: FloatAcc -> Result<Cell, AggregateError>)
        : AggFold =
        let acc =
            { Total = 0.0
              Numbers = 0
              AllFinite = true
              PastFloat = None
              Kept = ResizeArray<float>() }

        { Step =
            fun cell ->
                if acc.PastFloat.IsNone then
                    // Every admitted present cell of a numeric column is a number here; the one that
                    // is not is a decimal past the float range, which `tryToFloat` refuses rather than
                    // reading as an infinity.
                    match aggAsNum cell with
                    | Some f ->
                        acc.Total <- acc.Total + f
                        acc.Numbers <- acc.Numbers + 1

                        if not (isFiniteFloat f) then
                            acc.AllFinite <- false

                        if keeps then
                            acc.Kept.Add f
                    | None ->
                        let text =
                            match cell with
                            | Decimal s -> s
                            | other -> sprintf "%A" other

                        acc.PastFloat <-
                            Some(
                                AggregateOverflow(
                                    col.Name
                                    + ": the decimal "
                                    + text
                                    + " is past the float range, and "
                                    + aggFnTag fn
                                    + " is a float"
                                )
                            )
          Finish =
            fun _ ->
                match acc.PastFloat with
                | Some e -> Error e
                | None -> finish acc }

    /// `Sum` over a float column: the running total, which has no second form — a total that leaves
    /// the range over finite input is a named overflow.
    let private floatSum (col: Column) (acc: FloatAcc) : Result<Cell, AggregateError> =
        if acc.Numbers = 0 then Ok Null
        elif acc.AllFinite then finiteOr col "sum" acc.Total
        else Ok(Float acc.Total)

    /// `Mean`: the plain formula, recomputed by `scaledMoments` where it overflowed over finite input.
    /// That recomputation walks the column's numbers again — the one case that kept none — and every
    /// cell was admitted by the pass.
    let private floatMean (col: Column) (acc: FloatAcc) : Result<Cell, AggregateError> =
        if acc.Numbers = 0 then
            Ok Null
        else
            let mean = acc.Total / float acc.Numbers

            if isFiniteFloat mean || not acc.AllFinite then
                Ok(Float mean)
            else
                let xs = ResizeArray<float>()

                for c in col.Cells do
                    match admit col c with
                    | Ok cell ->
                        match aggAsNum cell with
                        | Some f -> xs.Add f
                        | None -> ()
                    | Error _ -> ()

                finiteOr col "mean" (fst (scaledMoments xs))

    /// `StdDev`, the POPULATION form, over the kept numbers: the plain second moment, recomputed by
    /// `scaledMoments` where it overflowed over finite input.
    let private floatStdDev (col: Column) (acc: FloatAcc) : Result<Cell, AggregateError> =
        if acc.Numbers = 0 then
            Ok Null
        else
            let n = float acc.Numbers
            let mean = acc.Total / n
            let mutable squares = 0.0

            for x in acc.Kept do
                squares <- squares + (x - mean) * (x - mean)

            let deviation = sqrt (squares / n)

            if isFiniteFloat deviation || not acc.AllFinite then
                Ok(Float deviation)
            else
                finiteOr col "stddev" (snd (scaledMoments acc.Kept))

    /// `Median` over the kept numbers in the float order; an even count halves before it adds where
    /// the plain mid-point overflowed over finite input.
    let private floatMedian (col: Column) (acc: FloatAcc) : Result<Cell, AggregateError> =
        match List.sortWith Cell.compareFloat (List.ofSeq acc.Kept) with
        | [] -> Ok Null
        | sorted ->
            let n = List.length sorted
            let mid = n / 2

            if n % 2 = 1 then
                Ok(Float(List.item mid sorted))
            else
                let a = List.item (mid - 1) sorted
                let b = List.item mid sorted
                let plain = (a + b) / 2.0

                if isFiniteFloat plain || not (isFiniteFloat a && isFiniteFloat b) then
                    Ok(Float plain)
                else
                    // Halved first: each half is exact, and their sum is within the range.
                    finiteOr col "median" (a / 2.0 + b / 2.0)

    /// Compute one aggregate over a column with the pinned null/coercion/float semantics (Phase 36) —
    /// the public surface the compute layer's `GroupBy`/`Pivot` *call* (the single source of truth, not
    /// a second copy). Null/NA is skipped; a numeric aggregate (`Sum`/`Mean`/`Median`/`StdDev`) over a
    /// non-numeric column is a named `IncompatibleAggType`; an integer `Sum` overflow is a named
    /// `AggregateOverflow` (Phase 39 no-silent-wrap). `Min`/`Max` order any same-family present cells;
    /// `First`/`Last` keep the first/last cell (a `Null` included). Int sums fold in int64 then range-
    /// check, so the result is host-deterministic. `Float` sums fold left to right from zero.
    /// `CountDistinct` (Phase 101) counts distinct PRESENT values by the canonical `Distinct` token, so
    /// it never depends on a host's float equality.
    ///
    /// A `DecimalType` column is numeric (`0.33.0`). Its `Sum` is EXACT and is a `Decimal`; its
    /// `Min`/`Max` compare exactly; its `Mean`/`Median`/`StdDev` are `float`, as `aggType` declares
    /// for every source type, and are the nearest float to each value — a value past the float
    /// range is a named `AggregateOverflow`, never an infinity.
    ///
    /// EVERY CELL IS ADMITTED FIRST (Phase 299): a present cell whose type does not widen into
    /// `col.Type` (`ColumnType.widens`), or a `Decimal` whose text is not decimal text, is a named
    /// `CellOutsideType` — never truncated, dropped or counted by shape. A `Decimal` cell is read
    /// canonicalised, so `1.50` and `1.5` are one value everywhere below. Numbers ORDER through the
    /// column layer's float order: NaN is one value and sorts last, `-0` equals `0`, so `Min`, `Max`
    /// and `Median` over a column holding a NaN answer the same on every host (`Max` is NaN, `Min`
    /// is not, and `Median` counts NaN at the top).
    ///
    /// ONE PASS (Phase 306). The column is walked once: each cell is admitted and folded into the
    /// one accumulator its aggregate needs, in row order, with no intermediate list of admitted,
    /// present or numeric cells (there were three; `Sum` over 20,000 cells cost some twenty times a
    /// direct loop). The fold order is the order those lists were folded in, so every answer that
    /// was finite is the same value to the bit. The refusals keep their precedence: a cell outside
    /// its column's type anywhere in the column first, then a non-numeric column, then the first
    /// decimal past the float range. `Median` and `StdDev` keep the column's numbers, because a
    /// sort and a second moment need them. Since Phase 388 the pass is `admitAll` and each aggregate
    /// is the one fold record it drives, so the walk is written once and an aggregate's accumulator
    /// is the only state it carries.
    ///
    /// NO FLOAT AGGREGATE ANSWERS AN INFINITY OVER FINITE INPUT (Phase 306). `Median` of
    /// `[1e308; 1e308]`, `Mean` of `[1.7e308; 1.7e308; -1.7e308]` and `StdDev` of `[1e200; -1e200]`
    /// each overflowed an intermediate — a sum, or a square — though the answer is representable.
    /// Each is computed by its plain formula first; where that leaves the float range while every
    /// input was finite, `Median` halves before it adds, and `Mean` and `StdDev` are recomputed by
    /// a scaled Welford recurrence (`scaledMoments`), whose intermediates cannot overflow. A float
    /// `Sum` has no such second form — the running total is the answer — so a total that leaves
    /// the range over finite input is a named `AggregateOverflow`, as an int `Sum` past int32
    /// always was. A column that itself HOLDS a NaN or an infinity is a different matter: the
    /// answer is whatever IEEE arithmetic gives, and that is not an overflow.
    ///
    /// `StdDev` IS THE POPULATION FORM: the square root of the mean squared deviation, dividing by
    /// the count `n`, not the sample form's `n - 1`. One value has a standard deviation of `0`.
    let aggregate (fn: AggFn) (col: Column) : Result<Cell, AggregateError> =
        // The aggregate and the column type are read by PATTERN, never by `=` (Phase 353): a union's
        // `=` is a structural-equality call under Fable, and these tests ran a dozen of them per call
        // and two more per cell — 16% of a node pivot that calls this once per (group, on-value) pair.
        let isNumeric =
            match col.Type with
            | IntType
            | FloatType
            | DecimalType -> true
            | BoolType
            | StringType
            | DateType
            | TimestampType -> false

        // The one accumulator this aggregate folds into (Phase 388). A numeric aggregate over a
        // non-numeric column folds nothing and is refused once the pass has admitted every cell.
        let fold =
            match fn with
            | Count -> passFold (fun p -> Ok(Int p.Count))
            | First -> passFold (fun p -> Ok p.First)
            | Last -> passFold (fun p -> Ok p.Last)
            | CountDistinct -> distinctFold ()
            | Min -> extremeFold true
            | Max -> extremeFold false
            | Sum
            | Mean
            | StdDev
            | Median when not isNumeric ->
                passFold (fun _ ->
                    Error(IncompatibleAggType(aggFnTag fn, ColumnType.tag col.Type, [ "int"; "float"; "decimal" ])))
            | Sum ->
                match col.Type with
                | DecimalType -> decimalSumFold ()
                | IntType -> intSumFold ()
                | _ -> floatFold col fn false (floatSum col)
            | Mean -> floatFold col fn false (floatMean col)
            | StdDev -> floatFold col fn true (floatStdDev col)
            | Median -> floatFold col fn true (floatMedian col)

        admitAll col fold.Step |> Result.bind fold.Finish

/// Table reads and `validate`, the well-formedness check the codec encodes and decodes through.
module Table =

    /// The row count of a table — the length of its first column, or 0 for a schema-only table.
    let rowCount (t: Table) : int =
        match t.Columns with
        | c :: _ -> Column.length c
        | [] -> 0

    /// The names in SCHEMA order — the table's column order, whatever order `Columns` holds; read
    /// from the schema alone, so a name with no column is still listed.
    let columnNames (t: Table) : string list = t.Schema |> List.map fst

    /// Find a column by name.
    let tryColumn (name: string) (t: Table) : Column option =
        t.Columns |> List.tryFind (fun c -> c.Name = name)

    /// The empty table (no columns, no rows).
    let empty: Table = { Schema = []; Columns = [] }

    /// The first name `names` carries twice, in order.
    let internal firstDuplicate (names: string list) : string option =
        let rec go (seen: Set<string>) =
            function
            | [] -> None
            | n :: rest -> if seen.Contains n then Some n else go (seen.Add n) rest

        go Set.empty names

    /// The first present cell of `c` the codec cannot carry as a cell of `c.Type`, as the refusal
    /// naming it (Phase 299), in row order: a cell whose type does not widen into the column's
    /// (`TypeMismatch`), a non-finite `Float` (`NonFiniteFloat` — the wire has none), and a
    /// `Decimal` / `Date` / `Timestamp` whose text is not its type's canonical form (`MalformedShape`).
    let private firstUncarriableCell (c: Column) : ColumnError option =
        c.Cells
        |> List.tryPick (fun cell ->
            match cell with
            | Null -> None
            | _ ->
                match Cell.typeOf cell with
                | Some t when not (ColumnType.widens t c.Type) ->
                    Some(TypeMismatch(c.Name, ColumnType.tag c.Type, ColumnType.tag t))
                | _ ->
                    match cell with
                    | Float f -> JVal.nonFiniteToken f |> Option.map (fun tok -> NonFiniteFloat(c.Name, tok))
                    | Decimal s when not (DecimalText.isCanonical s) ->
                        Some(
                            MalformedShape(
                                c.Name
                                + ": a decimal cell must carry canonical decimal text — an optional '-', integer digits with no leading zero, and a '.' with fraction digits only where the fraction is non-zero, with no trailing zero (build the cell with Cell.decimal)"
                            )
                        )
                    | Date s when not (TemporalText.isCanonicalDate s) ->
                        Some(
                            MalformedShape(
                                c.Name
                                + ": a date cell must carry a canonical ISO-8601 date, YYYY-MM-DD, naming a day that exists"
                            )
                        )
                    | Timestamp s when not (TemporalText.isCanonicalTimestamp s) ->
                        Some(
                            MalformedShape(
                                c.Name
                                + ": a timestamp cell must carry a canonical ISO-8601 UTC timestamp, YYYY-MM-DDThh:mm:ssZ, naming an instant that exists"
                            )
                        )
                    | _ -> None)

    /// Well-formedness — THE TABLE THE CODEC CAN CARRY (Phase 43; widened to the cells by Phase 299).
    /// `Column.create` does no validation, and `encodeJson` silently papers over a malformed table (a
    /// schema name with no column emits an empty placeholder; an extra column is dropped; ragged
    /// columns encode against the first column's length; a repeated name emits a repeated member key
    /// its readers disagree about). `validate` names the fault instead, first found in this order:
    ///   (a) no schema name and no column name appears twice (`Malformed`);
    ///   (b) every schema name has exactly one matching column and vice-versa (`Malformed`);
    ///   (c) each column's `Type` matches its schema entry (`TypeMismatch`);
    ///   (d) all columns share one length (`RaggedColumns`);
    ///   (e) every present cell, column by column in schema order and row by row, is one the codec
    ///       carries as a cell of its column's type — its type WIDENS into the column's
    ///       (`ColumnType.widens`: an `Int` in a float or decimal column is a widening, a `Bool` in an
    ///       int column or a `Float` in a decimal column is a `TypeMismatch`), a `Float` is finite
    ///       (`NonFiniteFloat`), and a `Decimal`, `Date` or `Timestamp` carries its type's canonical
    ///       text (`MalformedShape`).
    /// Over what it accepts, `ColumnCodec.tryEncode` is exactly `Ok (encode src)` — a law pins it —
    /// and `ColumnCodec.decode` ends in it, so a table that encodes is a table that decodes. It is
    /// not a data-quality check: those are the columnar validator's rules.
    let validate (t: Table) : Result<unit, ColumnError> =
        let schemaNames = t.Schema |> List.map fst
        let columnNamesList = t.Columns |> List.map (fun c -> c.Name)

        let missing =
            schemaNames |> List.filter (fun n -> not (List.contains n columnNamesList))

        let extra =
            columnNamesList |> List.filter (fun n -> not (List.contains n schemaNames))

        match firstDuplicate schemaNames, firstDuplicate columnNamesList with
        | Some n, _ -> Error(Malformed("duplicate schema name: " + n))
        | None, Some n -> Error(Malformed("duplicate column name: " + n))
        | None, None ->
            if not (List.isEmpty missing) then
                Error(Malformed("schema names with no column: " + String.concat ", " missing))
            elif not (List.isEmpty extra) then
                Error(Malformed("columns absent from the schema: " + String.concat ", " extra))
            else
                // Type agreement (schema order drives the check).
                let typeFault =
                    t.Schema
                    |> List.tryPick (fun (name, ty) ->
                        match t.Columns |> List.tryFind (fun c -> c.Name = name) with
                        | Some c when c.Type <> ty ->
                            Some(TypeMismatch(name, ColumnType.tag ty, ColumnType.tag c.Type))
                        | _ -> None)

                match typeFault with
                | Some e -> Error e
                | None ->
                    // Equal lengths across all columns.
                    let ragged =
                        match t.Columns with
                        | [] -> None
                        | first :: rest ->
                            let len0 = Column.length first

                            rest
                            |> List.tryFind (fun c -> Column.length c <> len0)
                            |> Option.map (fun c -> RaggedColumns(c.Name, len0, Column.length c))

                    match ragged with
                    | Some e -> Error e
                    | None ->
                        // The cells, schema order then row order.
                        let cellFault =
                            t.Schema
                            |> List.tryPick (fun (name, _) ->
                                t.Columns
                                |> List.tryFind (fun c -> c.Name = name)
                                |> Option.bind firstUncarriableCell)

                        match cellFault with
                        | Some e -> Error e
                        | None -> Ok()

// ---- schema compatibility (Phase 33) ----

/// A structured schema delta (Phase 33): what changed between an `old` `Schema` and a `target` one —
/// columns added (in `target`, absent from `old`), removed (in `old`, absent from `target`), retyped
/// (same name, different `ColumnType`), and whether the columns common to both appear in a different
/// relative order. A *rename* surfaces as a removed+added pair (the schema carries no rename intent; a
/// consumer that knows a rename happened reads it from that pair). Nullability is not a schema-level
/// fact in this model (it is the per-cell validity mask), so it is not part of the delta.
///
/// **`Order` makes the delta a transform (Phase 317).** `Reordered` says THAT the common columns
/// moved, not WHERE to — and an added column's position is not recorded by anything else — so
/// until this field a delta could be read but not replayed. `Order` is the target's column order,
/// left EMPTY exactly when the target's order is the one `Schema.patch` derives without it: the
/// surviving columns in their old order, then the added ones in the order `Added` lists them. So
/// `diff a a` is `Schema.identityDelta` — every list empty, `Reordered` false — and
/// `Schema.patch old (Schema.diff old target) = Ok target`. `Reordered` keeps its meaning and is
/// now implied by `Order` (a reorder of the common columns is never the derived order).
type SchemaDelta =
    {
        /// Columns only the target holds, in target order; `Schema.patch` appends them in this order
        /// and refuses one the schema already holds.
        Added: (string * ColumnType) list
        /// Columns only the old schema holds, in old order, each with its OLD type — `patch` refuses
        /// to remove a column whose type disagrees.
        Removed: (string * ColumnType) list
        /// Columns in both whose type changed, in old order, as `(name, from, to)`; `patch` checks
        /// `from` before retyping in place.
        Retyped: (string * ColumnType * ColumnType) list
        /// Whether the columns common to both schemas appear in a different relative order. A report
        /// only — `patch` never reads it; `Order` carries where they go.
        Reordered: bool
        /// The target's full column order, or empty when it is the order `patch` derives without it;
        /// a non-empty `Order` must be a permutation of the resulting columns.
        Order: string list
    }

/// Why `Schema.patch` refused a delta (Phase 317) — named and enumerated (GP5). A refusal says the
/// delta was not computed against the schema it is applied to; `patch` never repairs one.
type SchemaError =
    /// The schema names a column twice, so no delta can address its columns by name.
    | DuplicateColumn of column: string
    /// A removed or retyped column the schema does not hold.
    | AbsentColumn of column: string
    /// A removed or retyped column whose type in the schema is not the one the delta records.
    | TypeDisagrees of column: string * expected: ColumnType * got: ColumnType
    /// An added column the schema already holds.
    | AlreadyPresent of column: string
    /// An `Order` that is not a permutation of the columns the delta leaves: `expected` is that
    /// column set in the derived order, `got` the order the delta carries.
    | OrderMismatch of expected: string list * got: string list

/// A compatibility verdict for a schema change relative to the columns a consumer actually depends on
/// (Phase 33) — the data-strand analogue of `verifyChain` for the op-stream. Recoverable + enumerated
/// (GP5): `Breaking` / `Unknown` name their reasons.
[<RequireQualifiedAccess>]
type SchemaCompat =
    /// No depended-on column was removed, and every depended-on retype is a safe widening.
    | Compatible
    /// A depended-on column was removed, or retyped in a way that is NOT a safe widening.
    | Breaking of reasons: string list
    /// A depended-on column changed in a way whose safety cannot be classified. Reserved for future
    /// type relations — the current pinned lattice classifies every retype as widening-or-breaking, so
    /// `classify` does not currently produce it; it exists so the verdict surface is complete (GP5).
    | Unknown of reasons: string list

/// Schema-level operations (Phase 33): a structural `diff`, a depended-on-column compatibility verdict,
/// and a stable cross-host `fingerprint`. (`ModuleSuffix` so the module coexists with the `Schema` type
/// abbreviation, the same idiom as `Option`/`List`.)
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Schema =

    /// The structured delta from `old` to `target` (total). Order-aware: `Reordered` is set when the
    /// columns common to both schemas appear in a different relative order.
    let diff (old: Schema) (target: Schema) : SchemaDelta =
        let oldMap = Map.ofList old
        let newMap = Map.ofList target

        let added = target |> List.filter (fun (n, _) -> not (Map.containsKey n oldMap))
        let removed = old |> List.filter (fun (n, _) -> not (Map.containsKey n newMap))

        let retyped =
            old
            |> List.choose (fun (n, ot) ->
                match Map.tryFind n newMap with
                | Some nt when nt <> ot -> Some(n, ot, nt)
                | _ -> None)

        // common columns in each schema's own order — a difference is a reorder.
        let commonOld =
            old |> List.map fst |> List.filter (fun n -> Map.containsKey n newMap)

        let commonNew =
            target |> List.map fst |> List.filter (fun n -> Map.containsKey n oldMap)

        // The order `patch` derives without being told (Phase 317): the survivors in old order,
        // then the added columns in target order. `Order` is recorded only when the target differs.
        let derived = commonOld @ (added |> List.map fst)
        let targetOrder = target |> List.map fst

        { Added = added
          Removed = removed
          Retyped = retyped
          Reordered = commonOld <> commonNew
          Order = if targetOrder = derived then [] else targetOrder }

    /// The delta that changes nothing (Phase 317): every list empty, `Reordered` false. It is what
    /// `diff a a` returns for every `a`, and `patch s identityDelta = Ok s` for every schema `s`
    /// that names no column twice.
    let identityDelta: SchemaDelta =
        { Added = []
          Removed = []
          Retyped = []
          Reordered = false
          Order = [] }

    /// Apply a delta to a schema (Phase 317) — the transform `diff` is the report of, so a host that
    /// records deltas beside `fingerprint` can replay them. In order: every `Removed` column must be
    /// present with the recorded type and leaves; every `Retyped` column must be present with its
    /// recorded FROM type and takes its TO type in place; every `Added` column must be absent and is
    /// appended, in the order listed; then a non-empty `Order` must be a permutation of the columns
    /// that result, and is their order. Anything else is a named `SchemaError`, never a repair.
    ///
    /// **The law:** `patch old (diff old target) = Ok target` for every pair of schemas that each name
    /// no column twice — including reorders and added columns placed anywhere — and
    /// `diff a a = identityDelta`. Total.
    let patch (old: Schema) (delta: SchemaDelta) : Result<Schema, SchemaError> =
        let typeOf (s: Schema) (name: string) =
            s |> List.tryPick (fun (n, t) -> if n = name then Some t else None)

        let rec removeAll (s: Schema) (xs: (string * ColumnType) list) =
            match xs with
            | [] -> Ok s
            | (name, ty) :: rest ->
                match typeOf s name with
                | None -> Error(AbsentColumn name)
                | Some t when t <> ty -> Error(TypeDisagrees(name, ty, t))
                | Some _ -> removeAll (s |> List.filter (fun (n, _) -> n <> name)) rest

        let rec retypeAll (s: Schema) (xs: (string * ColumnType * ColumnType) list) =
            match xs with
            | [] -> Ok s
            | (name, fromTy, toTy) :: rest ->
                match typeOf s name with
                | None -> Error(AbsentColumn name)
                | Some t when t <> fromTy -> Error(TypeDisagrees(name, fromTy, t))
                | Some _ -> retypeAll (s |> List.map (fun (n, t) -> if n = name then (n, toTy) else (n, t))) rest

        let rec addAll (s: Schema) (xs: (string * ColumnType) list) =
            match xs with
            | [] -> Ok s
            | (name, ty) :: rest ->
                match typeOf s name with
                | Some _ -> Error(AlreadyPresent name)
                | None -> addAll (s @ [ name, ty ]) rest

        let reorder (s: Schema) =
            match delta.Order with
            | [] -> Ok s
            | order ->
                let names = s |> List.map fst

                let key (xs: string list) =
                    List.sortWith (fun a b -> System.String.CompareOrdinal(a, b)) xs

                if List.length order <> List.length names || key order <> key names then
                    Error(OrderMismatch(names, order))
                else
                    Ok(order |> List.map (fun n -> n, (typeOf s n).Value))

        match Table.firstDuplicate (old |> List.map fst) with
        | Some name -> Error(DuplicateColumn name)
        | None ->
            removeAll old delta.Removed
            |> Result.bind (fun s -> retypeAll s delta.Retyped)
            |> Result.bind (fun s -> addAll s delta.Added)
            |> Result.bind reorder

    /// Classify a delta against the set of columns a consumer depends on (Phase 33). A removed
    /// depended-on column is `Breaking`; a retyped depended-on column is safe iff the change is a
    /// widening (`ColumnType.widens`), else `Breaking`. Added columns, reorderings, and any change to a
    /// column the consumer does NOT read are all safe. Reasons are enumerated (GP5). Total.
    let classify (dependsOn: string list) (delta: SchemaDelta) : SchemaCompat =
        let deps = Set.ofList dependsOn

        let removedDep =
            delta.Removed
            |> List.filter (fun (n, _) -> deps.Contains n)
            |> List.map (fun (n, t) -> "depended-on column removed: " + n + " (" + ColumnType.tag t + ")")

        let badRetype =
            delta.Retyped
            |> List.filter (fun (n, ot, nt) -> deps.Contains n && not (ColumnType.widens ot nt))
            |> List.map (fun (n, ot, nt) ->
                "depended-on column "
                + n
                + " retyped "
                + ColumnType.tag ot
                + " → "
                + ColumnType.tag nt
                + " (not a safe widening)")

        match removedDep @ badRetype with
        | [] -> SchemaCompat.Compatible
        | reasons -> SchemaCompat.Breaking reasons

    // A DELIBERATE COPY of `Hash.fnv1a` (`Fuaran.Core.Tree`), kept because `Column` references only
    // `Wire` and taking a `Tree` dependency to reach one 8-line function would add a package edge
    // for every consumer. It must stay VALUE-IDENTICAL to the canonical one, which
    // the `hashSweep/*` rows of `ParityVectors` (Fuaran.Core.Conformance) compare over a shared corpus.
    //
    // The multiply is split into 16-bit halves — see `Hash.mul32` for why a plain `h * 16777619u`
    // is not portable (the product passes 2^53 under Fable's doubles, losing precision inside the
    // operation). .NET values are unchanged by the split. Do not "simplify" it back.
    let private fnv1a (s: string) : string =
        let mutable h = 2166136261u

        for ch in s do
            h <- h ^^^ uint32 ch
            // 16777619 = 0x01000193 = 256 * 65536 + 403, so the prime's halves are 256 and 403.
            let lo = h &&& 0xFFFFu
            let hi = h >>> 16
            let cross = ((lo * 256u) + (hi * 403u)) &&& 0xFFFFu
            h <- ((lo * 403u) + (cross * 65536u)) &&& 0xFFFFFFFFu

        h.ToString("x8")

    // A DELIBERATE COPY of `Hash.canonicalFields` (`Fuaran.Core.Tree`), for the reason `fnv1a` above
    // is one: `Column` references only `Wire`. Each field has every `fieldEsc` (U+0010) and every
    // `foldSep` (U+0001) it carries escaped by a preceding `fieldEsc`, and is then terminated by
    // `foldSep` — so the pre-image is INJECTIVE, where the bare U+0001 join it replaces (Phase 299)
    // was not: a column NAME can spell the separator, and then two different schemas shared a
    // pre-image. Both symbols are written as `\u` escapes, never as the raw control byte. It must
    // stay VALUE-IDENTICAL to the canonical encoding: the suite compares `fingerprint` against
    // `Hash.fnv1a (Hash.canonicalFields …)` over names carrying the separator and the escape, and the
    // `hashSweep/*` parity rows carry it through both pipelines.
    let private foldSep = "\u0001"
    let private fieldEsc = "\u0010"

    let private canonicalPreimage (fields: string list) : string =
        fields
        |> List.map (fun s ->
            s.Replace(fieldEsc, fieldEsc + fieldEsc).Replace(foldSep, fieldEsc + foldSep)
            + foldSep)
        |> String.concat ""

    /// A stable, cross-host content `fingerprint` of a `Schema` (Phase 33): FNV-1a over the canonical
    /// field encoding of the `name:type` list — the pre-image `Hash.canonicalFields` builds, so a
    /// name carrying the separator cannot make two schemas collide (Phase 299; the list was joined
    /// on a bare U+0001 until then, and every fingerprint moved with the pre-image). Order-sensitive
    /// — column order is part of a schema's identity — so a reorder changes the fingerprint.
    /// Byte-identical across hosts (no host hashing primitive); the schema-version stamp a
    /// consumer's provenance records to detect "same shape" cheaply.
    let fingerprint (s: Schema) : string =
        s
        |> List.map (fun (n, t) -> n + ":" + ColumnType.tag t)
        |> canonicalPreimage
        |> fnv1a

/// The canonical wire codec for the columnar strand. Column-oriented (a `values` array + a
/// `validity` mask per column, the Arrow layout), reusing the `Fuaran.Core.Wire` canonical rules
/// so numeric columns are byte-identical across hosts. Decode is `Result`-typed with the codec
/// envelope (`ColumnError`), over the `Wire.Decode` combinators. Fable-clean (only `Json` / `Canon` /
/// `Decode`).
module ColumnCodec =

    // ---- encode (Fable-clean `JVal` construction → `Canon.render`) ----

    /// THE ABSENT SLOT — the value a `Null` cell's slot carries in the `values` array (Phase 299).
    /// The validity mask, not this value, says the cell is absent; the slot exists only because the
    /// Fuaran wire has no JSON `null`, so the array needs a value there. It is a WIRE placeholder of
    /// the column's JSON kind and never a cell: a reader skips a masked slot without decoding it, so
    /// the `""` a date or timestamp column carries is never read as a date. (Until this phase it was
    /// produced through a public `Cell.defaultFor`, which built `Date ""` — a cell no date column
    /// accepts. The bytes are unchanged.)
    let private absentSlot (ty: ColumnType) : JVal =
        match ty with
        | IntType -> JInt 0
        | FloatType -> JFloat 0.0
        | BoolType -> JBool false
        | StringType
        | DateType
        | TimestampType -> JStr ""
        | DecimalType -> JStr DecimalText.zero

    /// The JSON value of a cell in a column of type `ty`. A `Null` cell emits the absent slot (the
    /// validity mask records the nullity); an `Int` in a float or decimal column is written as that
    /// type (the lossless widenings `ColumnType.widens` pins). A cell `Table.validate` would refuse
    /// encodes as-is — `encode` assumes a validated source, and `tryEncode` checks it first.
    /// Floats use the `Wire` `{0:R}` canonical layout.
    let private cellJson (ty: ColumnType) (c: Cell) : JVal =
        match c, ty with
        | Null, _ -> absentSlot ty
        | Int i, FloatType -> JFloat(float i)
        | Int i, DecimalType -> JStr(string i)
        | Int i, _ -> JInt i
        | Float f, _ -> JFloat f
        | Bool b, _ -> JBool b
        | Str s, _ -> JStr s
        | Date s, _ -> JStr s
        | Timestamp s, _ -> JStr s
        // A STRING on the wire, never a JSON number: a number token is read through a float by
        // most parsers, and the digits are the value.
        | Decimal s, _ -> JStr s

    let private columnJson (c: Column) : JVal =
        let values = c.Cells |> List.map (cellJson c.Type)
        let validity = c.Cells |> List.map (fun cell -> JBool(not (Cell.isNull cell)))
        JObj [ "values", JArr values; "validity", JArr validity ]

    let private schemaJson (schema: Schema) : JVal =
        schema
        |> List.map (fun (name, ty) -> JObj [ "name", JStr name; "type", JStr(ColumnType.tag ty) ])
        |> JArr

    /// Encode a `DataSource` to a `JVal` — embedded columns keyed by name (type comes from the
    /// schema, so it is not repeated), or a `ref` string beside an empty `schema`. The members are
    /// built in author order; `encode` renders them under `Canon`, which sorts keys, so the BYTES
    /// are canonical whatever the order here.
    let encodeJson (src: DataSource) : JVal =
        match src with
        | Embedded t ->
            let columns =
                t.Schema
                |> List.map (fun (name, _) ->
                    let col =
                        t.Columns
                        |> List.tryFind (fun c -> c.Name = name)
                        |> Option.defaultValue (Column.create name StringType [])

                    name, columnJson col)

            JObj [ "schema", schemaJson t.Schema; "columns", JObj columns ]
        | Ref r -> JObj [ "schema", JArr []; "ref", JStr r ]

    /// The canonical wire string for a `DataSource` — rendered under the shared `$type` discipline
    /// (`Canon`): Ordinal-sorted keys + the cross-host float layout, so a columnar payload is
    /// byte-identical across the .NET, Fable, TS and Python hosts. **Assumes a source
    /// `Table.validate` accepts** — use `tryEncode` for the guarded, total entry point on
    /// untrusted/derived data.
    let encode (src: DataSource) : string = Canon.render (encodeJson src)

    /// Total, guarded encode (Phases 38 + 43 + 299): `Table.validate`, then `encode`. So over what
    /// `validate` accepts it is EXACTLY `Ok (encode src)` — a law in the suite pins it — and
    /// everything it refuses is refused by `validate`, with `validate`'s error: a structurally
    /// malformed table, a cell outside its column's type, a non-finite float, and decimal, date or
    /// timestamp text that is not canonical. A `ref` source carries no table and always encodes.
    let tryEncode (src: DataSource) : Result<string, ColumnError> =
        match src with
        | Ref _ -> Ok(encode src)
        | Embedded t -> Table.validate t |> Result.map (fun () -> encode src)

    // ---- decode (`Json.parseDetailed` → the codec envelope `ColumnError`) ----

    // The `Wire.Decode` combinators, over this codec's envelope (Phase 299; the codec kept a private
    // copy of each until then). A missing member is `MissingField`; a wrong JSON kind is
    // `MalformedShape`, prefixed with where it was met when there is a where. The messages are the
    // ones the private copies wrote.
    let private fault (ctx: string) (f: Decode.Fault) : ColumnError =
        match f with
        | Decode.MissingProperty name -> MissingField name
        | Decode.WrongKind(expected, got) ->
            MalformedShape((if ctx = "" then "" else ctx + ": ") + "expected " + expected + ", got " + got)

    let private getField (name: string) (el: JVal) : Result<JVal, ColumnError> = Decode.propWith (fault "") name el

    let private asArr (ctx: string) (el: JVal) : Result<JVal list, ColumnError> = Decode.arrayWith (fault ctx) el

    let private asStr (ctx: string) (el: JVal) : Result<string, ColumnError> = Decode.stringWith (fault ctx) el

    /// Phase 94 (lenient-ingest) — render an epoch-seconds instant as the canonical
    /// ISO-8601 UTC timestamp string. Pure integer arithmetic (civil-from-days), so it
    /// is Fable-portable and clock-free; negative epochs (pre-1970) are handled.
    let private isoOfEpochSeconds (secs: int64) : string =
        let days =
            let d = secs / 86400L
            if secs % 86400L < 0L then d - 1L else d

        let sod = secs - days * 86400L
        let z = days + 719468L
        let era = (if z >= 0L then z else z - 146096L) / 146097L
        let doe = z - era * 146097L
        let yoe = (doe - doe / 1460L + doe / 36524L - doe / 146096L) / 365L
        let doy = doe - (365L * yoe + yoe / 4L - yoe / 100L)
        let mp = (5L * doy + 2L) / 153L
        let day = doy - (153L * mp + 2L) / 5L + 1L
        let month = if mp < 10L then mp + 3L else mp - 9L
        let year = yoe + era * 400L + (if month <= 2L then 1L else 0L)
        sprintf "%04d-%02d-%02dT%02d:%02d:%02dZ" year month day (sod / 3600L) (sod % 3600L / 60L) (sod % 60L)

    /// The largest magnitude a whole-valued float token carries exactly: 2^53, the parser's int53
    /// guard. A decimal column reads a whole-valued `JFloat` up to it and no further.
    let private int53Max = 9007199254740992.0

    /// Decode one present value into a `Cell` of the declared column type, or a `TypeMismatch`.
    /// A float column accepts an integer JSON token (lossless widening); a timestamp column
    /// accepts an epoch number (Phase 94 — models emit epoch instants against their own
    /// correct `"timestamp"` schema; unit by magnitude: ≥ 1e11 ⇒ milliseconds, else seconds —
    /// epoch-seconds stay below 1e11 until year 5138). Every other type requires its exact
    /// JSON kind.
    ///
    /// A decimal column (`0.33.0`) reads a STRING of decimal text and canonicalises it, so `12.50`
    /// decodes to `Decimal "12.5"`; it reads an integer token, which is exact — whichever
    /// constructor the parser chose for it (Phase 299): a token past int32 arrives as a
    /// whole-valued `JFloat`, and within the int53 guard its value IS its digits, so `3000000000`
    /// decodes as `12` always did. It REFUSES a fractional number token, and a whole-valued one past
    /// 2^53, as a `TypeMismatch`: that value has been through a float by the time it arrives here,
    /// and a type whose purpose is exactness cannot accept a value it cannot vouch for. An emitter
    /// writes a decimal as a string.
    ///
    /// A date or timestamp column (Phase 299) reads only its canonical ISO-8601 text
    /// (`TemporalText`), and an epoch number only where the instant it names falls in the years the
    /// canonical form spells (`0000`–`9999`); anything else is a `MalformedShape`.
    let private decodeCell (colName: string) (ty: ColumnType) (v: JVal) : Result<Cell, ColumnError> =
        let mismatch () =
            Error(TypeMismatch(colName, ColumnType.tag ty, JVal.kindName v))

        let notCanonical (what: string) =
            Error(MalformedShape(colName + ": " + what))

        let temporal (isCanonical: string -> bool) (make: string -> Cell) (form: string) (s: string) =
            if isCanonical s then
                Ok(make s)
            else
                notCanonical (
                    "a "
                    + ColumnType.tag ty
                    + " value must be canonical ISO-8601 text, "
                    + form
                    + ", naming a moment that exists"
                )

        let epochToIso (i: int64) =
            let secs = if abs i >= 100_000_000_000L then i / 1000L else i
            let iso = isoOfEpochSeconds secs

            if TemporalText.isCanonicalTimestamp iso then
                Ok(Timestamp iso)
            else
                notCanonical (
                    "the epoch "
                    + string i
                    + " names an instant outside the years 0000-9999 the canonical timestamp spells"
                )

        match ty, v with
        | IntType, JInt i -> Ok(Int i)
        | FloatType, JFloat f -> Ok(Float f)
        | FloatType, JInt i -> Ok(Float(float i))
        | BoolType, JBool b -> Ok(Bool b)
        | StringType, JStr s -> Ok(Str s)
        | DateType, JStr s -> temporal TemporalText.isCanonicalDate Date "YYYY-MM-DD" s
        | TimestampType, JStr s -> temporal TemporalText.isCanonicalTimestamp Timestamp "YYYY-MM-DDThh:mm:ssZ" s
        // Epoch-seconds fit Int32 (so arrive as JInt); epoch-milliseconds overflow the
        // parser's Int32 path and arrive as a whole-valued JFloat.
        | TimestampType, JInt i -> epochToIso (int64 i)
        | TimestampType, JFloat f when f = floor f && abs f < 9e15 -> epochToIso (int64 f)
        | DecimalType, JInt i -> Ok(Decimal(string i))
        | DecimalType, JFloat f when f = floor f && abs f <= int53Max -> Ok(Decimal(string (int64 f)))
        | DecimalType, JStr s ->
            match DecimalText.tryCanonical s with
            | Some canonical -> Ok(Decimal canonical)
            | None ->
                notCanonical
                    "a decimal value must be decimal text — an optional '-', digits, and an optional '.' followed by digits, with no exponent, sign '+', separator or white space"
        | _ -> mismatch ()

    let private decodeSchemaEntry (el: JVal) : Result<string * ColumnType, ColumnError> =
        getField "name" el
        |> Result.bind (asStr "schema.name")
        |> Result.bind (fun name ->
            getField "type" el
            |> Result.bind (asStr "schema.type")
            |> Result.bind (fun tag ->
                match ColumnType.ofTag tag with
                | Some ty -> Ok(name, ty)
                | None -> Error(UnknownType(tag, ColumnType.allTags))))

    let private decodeSchema (el: JVal) : Result<Schema, ColumnError> =
        asArr "schema" el
        |> Result.bind (fun xs ->
            let rec go acc =
                function
                | [] -> Ok(List.rev acc)
                | x :: rest ->
                    match decodeSchemaEntry x with
                    | Ok e -> go (e :: acc) rest
                    | Error e -> Error e

            go [] xs)

    /// Phase 88 (lenient-ingest) — a column that rides as a BARE JSON array is
    /// the "just the data" shorthand: `values` is the array itself with an
    /// all-present validity mask. Unambiguous — the Fuaran wire has no JSON
    /// null (tree rule 4), so a bare array can only mean every cell present;
    /// absent cells require the wrapped `{values, validity}` form, which
    /// stays canonical (the encoder always emits it).
    let private columnParts (name: string) (colEl: JVal) : Result<JVal list * JVal list, ColumnError> =
        match colEl with
        | JArr xs -> Ok(xs, xs |> List.map (fun _ -> JBool true))
        | _ ->
            getField "values" colEl
            |> Result.bind (asArr (name + ".values"))
            |> Result.bind (fun values ->
                // Phase 94 (lenient-ingest, pilot-5 census) — a wrapped column object
                // carrying `values` but NO `validity` mask is the same all-present
                // statement as the Phase-88 bare array (the wire has no JSON null, so
                // omission cannot mean absent cells): models reproduce the canonical
                // object shape minus the mask. Synthesize all-present; absent cells
                // still require the full wrapped form, which stays canonical.
                match Decode.tryProp "validity" colEl with
                | None -> Ok(values, values |> List.map (fun _ -> JBool true))
                | Some validityEl ->
                    asArr (name + ".validity") validityEl
                    |> Result.map (fun validity -> values, validity))

    /// Decode a single named column against its declared type from the `columns` object.
    let private decodeColumn (columnsObj: JVal) (name: string) (ty: ColumnType) : Result<Column, ColumnError> =
        match Decode.tryProp name columnsObj with
        | None -> Error(MissingField("columns." + name))
        | Some colEl ->
            columnParts name colEl
            |> Result.bind (fun (values, validity) ->
                if List.length values <> List.length validity then
                    Error(LengthMismatch(name, List.length values, List.length validity))
                else
                    let rec go acc =
                        function
                        | [], [] -> Ok(List.rev acc)
                        | v :: vs, JBool present :: ps ->
                            if not present then
                                go (Null :: acc) (vs, ps)
                            else
                                match decodeCell name ty v with
                                | Ok c -> go (c :: acc) (vs, ps)
                                | Error e -> Error e
                        | _ :: _, p :: _ ->
                            Error(MalformedShape(name + ".validity: expected bool, got " + JVal.kindName p))
                        | _ -> Error(MalformedShape(name + ": values/validity exhausted unevenly"))

                    go [] (values, validity)
                    |> Result.map (fun cells ->
                        { Name = name
                          Type = ty
                          Cells = cells }))

    /// Phase 88 (lenient-ingest) — infer one column's `ColumnType` from its
    /// present cells. PINNED deterministic rules: all-int numerics ⇒ int, any
    /// fractional ⇒ float, all-bool ⇒ bool, all-string ⇒ string — **never**
    /// date/timestamp (temporal types require a declared schema; a date-looking
    /// string stays a string), and never decimal, on the same ground: a column of
    /// digit strings is a string column until a schema says otherwise. An empty column, or mixed kinds, is a
    /// DIDACTIC reject naming the explicit-schema remedy. (The Fuaran wire
    /// has no JSON null, so inference sees every value slot; masked-absent
    /// cells only ride the wrapped form.)
    let private inferColumnType (name: string) (values: JVal list) : Result<ColumnType, ColumnError> =
        let present = values

        let kindTag (v: JVal) =
            match v with
            | JInt _ -> "int"
            | JFloat _ -> "float"
            | JBool _ -> "bool"
            | JStr _ -> "string"
            | _ -> "other"

        match present with
        | [] ->
            Error(
                MalformedShape(
                    name
                    + ": cannot infer a column type from an empty / all-null column — declare it in an explicit \"schema\" array"
                )
            )
        | _ ->
            let tags = present |> List.map kindTag |> List.distinct

            match tags with
            | [ "int" ] -> Ok IntType
            | [ "float" ]
            | [ "int"; "float" ]
            | [ "float"; "int" ] -> Ok FloatType
            | [ "bool" ] -> Ok BoolType
            | [ "string" ] -> Ok StringType
            | mixed ->
                Error(
                    MalformedShape(
                        name
                        + ": cannot infer a single column type from mixed cell kinds ("
                        + String.concat ", " mixed
                        + ") — declare it in an explicit \"schema\" array"
                    )
                )

    /// The `columns` object, refused where it names one column twice (Phase 299). A parsed object
    /// keeps a repeated key, and the readers disagreed about which occurrence wins; the encoder
    /// never writes one (`Table.validate` refuses the duplicate name first), so a document that
    /// carries one was not written by it and has two readings.
    let private uniqueColumnKeys (columnsObj: JVal) : Result<JVal, ColumnError> =
        match columnsObj with
        | JObj fields ->
            match Table.firstDuplicate (fields |> List.map fst) with
            | Some key -> Error(Malformed("duplicate column key in \"columns\": " + key))
            | None -> Ok columnsObj
        | _ -> Ok columnsObj

    /// Decode a `DataSource` from a `JVal` root — the codec envelope on every failure.
    /// Phase 88: `schema` may be OMITTED on an EMBEDDED source (inferred per
    /// `inferColumnType`, columns in Ordinal key order). A `ref` source carries no rows, so it
    /// needs no schema and keeps none (Phase 299 dropped the rule that it carry one the decoder
    /// then discarded); a schema it does carry must still be a well-formed schema array. The
    /// canonical encoder always emits the explicit schema, so the shorthand normalises on
    /// re-encode. An embedded source ENDS in `Table.validate` (Phase 299), so what decodes is a
    /// table `tryEncode` accepts: a ragged table is a `RaggedColumns` here, not an `Ok` that encode
    /// then refuses with another cause.
    ///
    /// SURPLUS MEMBERS ARE MUST-IGNORE (stated by Phase 306; it was always so). A member of the
    /// source object other than `schema`, `columns` and `ref`, and — under an explicit schema — a
    /// member of `columns` the schema does not name, is read past, not refused: the wire's
    /// forward-compatibility rule (a reader ignores what it does not know), and what lets a host
    /// wrap the source in its own discriminator. The table that results is the schema's, checked by
    /// `validate`; a surplus column is not in it and is not re-encoded. (With NO schema every member
    /// of `columns` is a column, so nothing there is surplus.) A key REPEATED in `columns` is a
    /// different thing — two readings of one name — and is refused above. Two consequences of the
    /// same rule, stated because a reader will otherwise find them: a source carrying BOTH `ref`
    /// and `columns` is a `Ref` (the `ref` member is looked for first, and the columns are then
    /// surplus); and under an explicitly EMPTY schema no column is read, so `columns` must be
    /// present and is otherwise not looked at.
    ///
    /// WHAT COMES BACK is the table in NORMAL FORM, not always the table that was encoded: its
    /// columns in schema order, and an `Int` cell in a float or decimal column as the `Float` or
    /// `Decimal` it widens to. `decode (encode t)` is that normal form for every table `validate`
    /// accepts, and is `t` itself for every table this function returned (`proofs/WireColumn.fst`).
    let decodeJson (el: JVal) : Result<DataSource, ColumnError> =
        let schemaR =
            match Decode.tryProp "schema" el with
            | Some schemaEl -> decodeSchema schemaEl |> Result.map Some
            | None -> Ok None

        schemaR
        |> Result.bind (fun schemaOpt ->
            match Decode.tryProp "ref" el with
            | Some refEl -> asStr "ref" refEl |> Result.map Ref
            | None ->
                getField "columns" el
                |> Result.bind uniqueColumnKeys
                |> Result.bind (fun columnsObj ->
                    let schemaResolved =
                        match schemaOpt with
                        | Some schema -> Ok schema
                        | None ->
                            // Infer from the columns object, Ordinal key order.
                            match columnsObj with
                            | JObj colFields ->
                                colFields
                                |> List.map fst
                                |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
                                |> List.fold
                                    (fun acc name ->
                                        acc
                                        |> Result.bind (fun entries ->
                                            match Decode.tryProp name columnsObj with
                                            | None -> Error(MissingField("columns." + name))
                                            | Some colEl ->
                                                columnParts name colEl
                                                |> Result.bind (fun (values, _) ->
                                                    inferColumnType name values
                                                    |> Result.map (fun ty -> entries @ [ name, ty ]))))
                                    (Ok [])
                            | _ -> Error(MalformedShape "columns: expected object")

                    schemaResolved
                    |> Result.bind (fun schema ->
                        let rec go acc =
                            function
                            | [] -> Ok(List.rev acc)
                            | (name, ty) :: rest ->
                                match decodeColumn columnsObj name ty with
                                | Ok c -> go (c :: acc) rest
                                | Error e -> Error e

                        go [] schema
                        |> Result.map (fun columns -> { Schema = schema; Columns = columns })
                        |> Result.bind (fun table -> Table.validate table |> Result.map (fun () -> Embedded table)))))

    /// Decode a wire string into a `DataSource`, surfacing the codec envelope `ColumnError` (a
    /// JSON-syntax failure becomes `NotJson`, carrying the parser's structured error).
    let decode (s: string) : Result<DataSource, ColumnError> =
        match Json.parseDetailed s with
        | Error e -> Error(NotJson e)
        | Ok el -> decodeJson el

    /// Render a `ColumnError` as a stable human string — the adapter for `Corpus.Codec`'s
    /// `string`-error decode slot and for diagnostics.
    let errorString (e: ColumnError) : string =
        match e with
        | NotJson e -> "not valid JSON: " + e.Message + " at position " + string e.Position
        | MissingField f -> "missing field: " + f
        | MalformedShape d -> "malformed: " + d
        | UnknownType(got, expected) ->
            "unknown column type '"
            + got
            + "'; expected one of: "
            + String.concat ", " expected
        | TypeMismatch(col, expected, got) -> "column '" + col + "': expected " + expected + " value, got " + got
        | LengthMismatch(col, v, va) ->
            "column '"
            + col
            + "': values/validity length mismatch ("
            + string v
            + " vs "
            + string va
            + ")"
        | NonFiniteFloat(col, tok) ->
            "column '"
            + col
            + "': non-finite float is not representable on the Fuaran wire: "
            + tok
        | Malformed d -> "malformed table: " + d
        | RaggedColumns(col, expected, got) ->
            "ragged table: column '"
            + col
            + "' has "
            + string got
            + " rows where the first column has "
            + string expected

    /// The `Fuaran.Core.Wire.Corpus.Codec` over `DataSource` — encode + a `string`-error decode,
    /// so the columnar strand plugs straight into the conformance corpus tooling (`runCorpus` /
    /// `codecLaws`).
    let codec: Corpus.Codec<DataSource> =
        { Encode = encode
          Decode = fun s -> decode s |> Result.mapError errorString }

/// The canonical wire codec for a `SchemaDelta` (Phase 317) — what a host that records deltas beside
/// `Schema.fingerprint` as provenance writes, so that `Schema.patch` can replay them later. One
/// object with five members, all required: `added` and `removed` as `{name, type}` entries (the
/// columnar codec's schema entry), `retyped` as `{name, from, to}`, `reordered` a bool, and `order`
/// the column names `Order` carries. Rendered under `Canon`, so the bytes are canonical across
/// hosts; decode surfaces the columnar codec envelope (`ColumnError`). It decodes what it is handed
/// and judges nothing about the schema the delta will meet — that is `Schema.patch`'s question.
module SchemaDeltaCodec =

    let private entryJson (name: string, ty: ColumnType) : JVal =
        JObj [ "name", JStr name; "type", JStr(ColumnType.tag ty) ]

    /// Encode a delta to a `JVal`; `encode` renders it under `Canon`, which sorts the keys.
    let encodeJson (d: SchemaDelta) : JVal =
        JObj
            [ "added", JArr(d.Added |> List.map entryJson)
              "removed", JArr(d.Removed |> List.map entryJson)
              "retyped",
              JArr(
                  d.Retyped
                  |> List.map (fun (name, fromTy, toTy) ->
                      JObj
                          [ "name", JStr name
                            "from", JStr(ColumnType.tag fromTy)
                            "to", JStr(ColumnType.tag toTy) ])
              )
              "reordered", JBool d.Reordered
              "order", JArr(d.Order |> List.map JStr) ]

    /// The canonical wire string for a delta. Total: every delta encodes.
    let encode (d: SchemaDelta) : string = Canon.render (encodeJson d)

    let private fault (ctx: string) (f: Decode.Fault) : ColumnError =
        match f with
        | Decode.MissingProperty name -> MissingField name
        | Decode.WrongKind(expected, got) -> MalformedShape(ctx + ": expected " + expected + ", got " + got)

    let private field (name: string) (el: JVal) : Result<JVal, ColumnError> = Decode.propWith (fault name) name el

    let private text (ctx: string) (el: JVal) : Result<string, ColumnError> = Decode.stringWith (fault ctx) el

    let private columnType (ctx: string) (el: JVal) : Result<ColumnType, ColumnError> =
        text ctx el
        |> Result.bind (fun tag ->
            match ColumnType.ofTag tag with
            | Some ty -> Ok ty
            | None -> Error(UnknownType(tag, ColumnType.allTags)))

    let private items (name: string) (read: JVal -> Result<'a, ColumnError>) (el: JVal) : Result<'a list, ColumnError> =
        field name el
        |> Result.bind (Decode.arrayWith (fault name))
        |> Result.bind (fun xs ->
            let rec go acc =
                function
                | [] -> Ok(List.rev acc)
                | x :: rest ->
                    match read x with
                    | Ok v -> go (v :: acc) rest
                    | Error e -> Error e

            go [] xs)

    let private entry (ctx: string) (el: JVal) : Result<string * ColumnType, ColumnError> =
        field "name" el
        |> Result.bind (text (ctx + ".name"))
        |> Result.bind (fun name ->
            field "type" el
            |> Result.bind (columnType (ctx + ".type"))
            |> Result.map (fun ty -> name, ty))

    let private retypedEntry (el: JVal) : Result<string * ColumnType * ColumnType, ColumnError> =
        field "name" el
        |> Result.bind (text "retyped.name")
        |> Result.bind (fun name ->
            field "from" el
            |> Result.bind (columnType "retyped.from")
            |> Result.bind (fun fromTy ->
                field "to" el
                |> Result.bind (columnType "retyped.to")
                |> Result.map (fun toTy -> name, fromTy, toTy)))

    /// Decode a delta from a `JVal`. `decodeJson (encodeJson d) = Ok d` for every delta.
    let decodeJson (el: JVal) : Result<SchemaDelta, ColumnError> =
        items "added" (entry "added") el
        |> Result.bind (fun added ->
            items "removed" (entry "removed") el
            |> Result.bind (fun removed ->
                items "retyped" retypedEntry el
                |> Result.bind (fun retyped ->
                    field "reordered" el
                    |> Result.bind (fun r ->
                        match r with
                        | JBool b -> Ok b
                        | other -> Error(MalformedShape("reordered: expected bool, got " + JVal.kindName other)))
                    |> Result.bind (fun reordered ->
                        items "order" (text "order") el
                        |> Result.map (fun order ->
                            { Added = added
                              Removed = removed
                              Retyped = retyped
                              Reordered = reordered
                              Order = order })))))

    /// Decode a wire string into a delta (a JSON-syntax failure is `NotJson`).
    let decode (s: string) : Result<SchemaDelta, ColumnError> =
        match Json.parseDetailed s with
        | Error e -> Error(NotJson e)
        | Ok el -> decodeJson el

    /// The `Corpus.Codec` over `SchemaDelta`, for the conformance corpus tooling.
    let codec: Corpus.Codec<SchemaDelta> =
        { Encode = encode
          Decode = fun s -> decode s |> Result.mapError ColumnCodec.errorString }
