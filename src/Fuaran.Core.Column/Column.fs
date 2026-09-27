namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Column (Phase 28) — the relational/columnar data strand, a new
//  Core substrate parallel to the tree/op-stream spine. A typed, null-aware,
//  Arrow-compatible columnar model + its canonical wire codec. It is the data
//  substrate `Fuaran.Core.DataFrame` (Phase 29) operates on and the shape the UI
//  `DataSource` binding serialises (Compute Layer spec §1).
//
//  It introduces no tree-witness field and no base node type — it is a separate,
//  self-contained data strand. FSharp.Core only; Fable-clean on encode and decode
//  (it reuses `Fuaran.Core.Wire`'s canonical-float / escaping / parser rules, so a
//  numeric column is byte-identical across the .NET and Fable hosts).
// ============================================================================

/// The fixed, Arrow-compatible scalar type set a column ranges over (spec §1). Closed by
/// intent — a new scalar type is an additive case, never an open extension point.
type ColumnType =
    | IntType
    | FloatType
    | BoolType
    | StringType
    | DateType
    | TimestampType
    /// An EXACT decimal (`0.33.0`): a value a `float` cannot hold without rounding, such as a sum of
    /// money. Unparameterised — it carries no precision and no scale, because the text a cell
    /// carries has exactly the digits it has. A host that maps it to a fixed-scale store type reads
    /// the scale off the data or declares it in its own model.
    | DecimalType

/// A single realized scalar cell. Null/NA is a first-class case (`Null`), never a sentinel
/// value buried in the data — the in-memory form of the wire's validity mask. `Date` /
/// `Timestamp` carry their canonical ISO-8601 string (`YYYY-MM-DD` / `YYYY-MM-DDThh:mm:ssZ`)
/// so the model needs no host `DateTime` dependency and stays Fable-clean + byte-identical.
type Cell =
    | Int of int
    | Float of float
    | Bool of bool
    | Str of string
    | Date of string
    | Timestamp of string
    | Null
    /// An exact decimal, carried as its CANONICAL text (`DecimalText`): an optional `-`, the integer
    /// digits with no leading zero, and a `.` with fraction digits only where the fraction is
    /// non-zero, with no trailing zero — `0`, `12`, `-3.5`, `0.05`. No exponent, no separators.
    /// Text for the reason `Date` is text: no host decimal type is the same type on every host, and
    /// the digits are. Build one with `Cell.decimal`, which canonicalises; the codec does the same
    /// on decode. Declared after `Null` so every case that was already published keeps its tag.
    | Decimal of string

/// A typed, null-aware column. `Cells` co-indexes with the table's rows; a `Null` cell is the
/// validity-mask "absent" marker. `Type` is the declared column type; a present cell whose
/// value-shape disagrees with `Type` is a decode-time error (the codec enforces the schema).
type Column =
    { Name: string
      Type: ColumnType
      Cells: Cell list }

/// A `(name, type)` ordered schema — the column order of a table follows it.
type Schema = (string * ColumnType) list

/// An embedded columnar table: its schema + the columns (column order follows the schema,
/// every column the same length).
type Table =
    { Schema: Schema; Columns: Column list }

/// A data source: embedded columns, or a host-resolved named `Ref` (spec §1 — the
/// `Binding.Query` by-reference precedent). The evaluator resolves a `Ref` through a caller
/// supplied resolver; the wire carries the name, never the rows.
type DataSource =
    | Embedded of Table
    | Ref of string

/// The canonical six-code decode envelope for the columnar codec — the substrate's recoverable
/// error discipline (GP4/GP5): every failure *names what went wrong* and, where a closed set is
/// expected, *enumerates the alternatives*. Six codes, additive only.
type ColumnError =
    /// The input was not valid JSON at all (the underlying `Json.parse` failure, verbatim).
    | NotJson of detail: string
    /// A required field was absent from an object (`schema` / `values` / `validity` / `name` / `type`).
    | MissingField of field: string
    /// A value had the wrong JSON shape for its position (expected object/array/string where another kind appeared).
    | MalformedShape of detail: string
    /// A `type` tag was not one of the fixed scalar set; `expected` lists the valid tags.
    | UnknownType of got: string * expected: string list
    /// A present cell's JSON kind disagreed with its column's declared type.
    | TypeMismatch of column: string * expected: string * got: string
    /// A column's `values` and `validity` arrays had different lengths (they must co-index).
    | LengthMismatch of column: string * values: int * validity: int
    /// A present `Float` cell was non-finite (`NaN` / `Infinity` / `-Infinity`); the Fuaran wire has no
    /// non-finite float (the same posture as the tree wire's `Json.tryRender`, Phase 12) — `encode`
    /// would otherwise emit the JSON *string* `"NaN"`, which fails to decode back to a `FloatType` cell.
    | NonFiniteFloat of column: string * value: string
    /// The `Table` was structurally malformed (schema/column name disagreement, ragged column lengths,
    /// or a column whose `Type` disagrees with its schema entry) — `Table.validate` names the fault.
    | Malformed of detail: string

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

    /// The nearest `float` to a decimal text, or `None` where it is not decimal text. This is the
    /// one place the type rounds, and it is for the aggregates whose result is a `float` by
    /// declaration (`Mean` / `Median` / `StdDev`); a value that must stay exact never comes through it.
    let tryToFloat (s: string) : float option = tryCanonical s |> Option.map float

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

    /// The full closed set of valid type tags — the `UnknownType` enumeration (and the encode order).
    let all =
        [ IntType
          FloatType
          BoolType
          StringType
          DateType
          TimestampType
          DecimalType ]

    let allTags = all |> List.map tag

    /// Resolve a wire tag to its type, or `None` for an unknown tag.
    let ofTag (s: string) : ColumnType option =
        all |> List.tryFind (fun t -> tag t = s)

    /// The pinned type-widening lattice (Phase 33). A `from`→`target` change is a *safe widening* iff it
    /// is the identity or the one lossless promotion the rest of the strand already pins: `Int → Float`
    /// (`ColumnCodec.decodeCell` decodes a JSON int into a `FloatType` column; the `DataFrame` arithmetic
    /// promotes int operands to float). This is the single source of truth for "is a retype safe" — the
    /// schema-compatibility check and the codec/evaluator coercion agree by construction, not by a second
    /// rule-set.
    ///
    /// `Int → Decimal` is the second lossless promotion (`0.33.0`), and the codec agrees with it the
    /// same way: `ColumnCodec.decodeCell` decodes a JSON int into a `DecimalType` column. `Float →
    /// Decimal` is NOT a widening, in either direction: a float is an approximation and a decimal is
    /// a statement of digits, so a retype between them changes what the column claims.
    let widens (from: ColumnType) (target: ColumnType) : bool =
        from = target
        || (from = IntType && target = FloatType)
        || (from = IntType && target = DecimalType)

module Cell =

    /// Is the cell the null/NA marker?
    let isNull (c: Cell) : bool = c = Null

    /// A `Decimal` cell holding the canonical form of `text`, or `None` where `text` is not decimal
    /// text (`DecimalText`). The way to build one: every reader of a `Decimal` cell — the capture
    /// key, the distinct token, the wire — takes the text as it finds it, so two cells are the same
    /// value exactly when they were both built canonical.
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

    /// The type default a `Null` cell encodes as on the wire (the validity mask, not this
    /// placeholder, carries nullity — the placeholder keeps the values array null-free, which the
    /// Fuaran wire model requires).
    let defaultFor (t: ColumnType) : Cell =
        match t with
        | IntType -> Int 0
        | FloatType -> Float 0.0
        | BoolType -> Bool false
        | StringType -> Str ""
        | DateType -> Date ""
        | TimestampType -> Timestamp ""
        | DecimalType -> Decimal DecimalText.zero

/// A group/window aggregate function (Phase 36, lifted from the DataFrame evaluator's `GroupBy` so it
/// is a public, single-source surface). `Count` is non-null count; `Sum` keeps the source numeric type;
/// `Mean`/`Median`/`StdDev` are `float`; `Min`/`Max`/`First`/`Last` keep the source type.
type AggFn =
    | Sum
    | Mean
    | Min
    | Max
    | Count
    | Median
    | StdDev
    | First
    | Last
    /// Phase 101 — the count of DISTINCT present values (nulls skipped, so `CountDistinct` over an
    /// all-null column is `0`, exactly as `Count` is). Distinctness is the SAME canonical token the
    /// DataFrame `Distinct` / `GroupBy` partition on (Phase 41), so `NaN` collapses to one value,
    /// `-0.0`/`0.0` coincide, and two cells of different types never collide — the count is
    /// host-identical, not host-comparison-dependent.
    | CountDistinct

/// Why an aggregate was refused (Phase 36) — recoverable + enumerated (GP5), never a throw (GP4). A
/// numeric aggregate (`Sum`/`Mean`/`Median`/`StdDev`) over a non-numeric column names the expected
/// types; an integer `Sum` outside the int32 band is a named overflow (the pinned no-silent-wrap posture
/// shared with the DataFrame evaluator, Phase 39).
type AggregateError =
    | IncompatibleAggType of fn: string * colType: string * expected: string list
    | AggregateOverflow of detail: string

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

    // ---- pinned aggregate semantics (Phase 36) — the single source the DataFrame GroupBy/Pivot calls ----

    let private aggAsNum (c: Cell) : float option =
        match c with
        | Int i -> Some(float i)
        | Float f -> Some f
        | Decimal s -> DecimalText.tryToFloat s
        | _ -> None

    /// A cell as exact-decimal text: a `Decimal`'s own, or an `Int`'s digits (the lossless
    /// promotion `ColumnType.widens` pins). A `Float` is not one — see `widens`.
    let private aggAsDecimal (c: Cell) : string option =
        match c with
        | Decimal s -> DecimalText.tryCanonical s
        | Int i -> Some(string i)
        | _ -> None

    /// A total comparison between two present, same-family cells (`None` ⇒ incomparable). Identical to
    /// the DataFrame evaluator's `compareCells`; kept here so `aggregate` (Min/Max) is self-contained in
    /// the Column layer (the aggregate family is the single source; comparison is shared shape, not a
    /// second aggregate implementation).
    let private aggCompare (a: Cell) (b: Cell) : int option =
        match a, b with
        | (Int _ | Float _), (Int _ | Float _) -> Some(compare (aggAsNum a) (aggAsNum b))
        // An exact comparison, never through `float`: two decimals a float cannot tell apart are
        // still ordered. A `Decimal` beside a `Float` is incomparable, as `widens` has it.
        | (Decimal _ | Int _), (Decimal _ | Int _) ->
            Option.map2 (fun x y -> DecimalText.compare x y) (aggAsDecimal a) (aggAsDecimal b)
            |> Option.flatten
        | Bool x, Bool y -> Some(compare x y)
        | Str x, Str y -> Some(System.String.CompareOrdinal(x, y))
        | Date x, Date y -> Some(System.String.CompareOrdinal(x, y))
        | Timestamp x, Timestamp y -> Some(System.String.CompareOrdinal(x, y))
        | _ -> None

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

    /// The canonical, host-deterministic identity token of a cell (Phase 101) — the same layout the
    /// DataFrame evaluator's `groupKey` uses per cell, so `CountDistinct` counts exactly the values
    /// a `Distinct` step would keep. Floats route through the pinned `Canon` layout (`-0.0` collapses
    /// to `0`) with `NaN`/±∞ named, and each token is type-tagged so an `Int 1` and a `Float 1.0`
    /// are two values, never one.
    let private distinctToken (c: Cell) : string =
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
        // `m`, the tag `Query.invocationKey` gives the same case. The text is taken as found, as a
        // date's is: a cell built by `Cell.decimal` or decoded off the wire is canonical, and for
        // those one number is one token.
        | Decimal s -> "m:" + s
        | Null -> "n:"

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

    /// Compute one aggregate over a column with the pinned null/coercion/float semantics (Phase 36) —
    /// the public surface the DataFrame `GroupBy`/`Pivot` now *calls* (the single source of truth, not a
    /// second copy). Null/NA is skipped; a numeric aggregate (`Sum`/`Mean`/`Median`/`StdDev`) over a
    /// non-numeric column is a named `IncompatibleAggType`; an integer `Sum` overflow is a named
    /// `AggregateOverflow` (Phase 39 no-silent-wrap). `Min`/`Max` order any same-family present cells;
    /// `First`/`Last` keep the first/last cell (a `Null` included). Int sums fold in int64 then range-
    /// check, so the result is host-deterministic. `Float` sums via the pinned `List.sum`.
    /// `CountDistinct` (Phase 101) counts distinct PRESENT values by the canonical `Distinct` token, so
    /// it never depends on a host's float equality.
    ///
    /// A `DecimalType` column is numeric (`0.33.0`). Its `Sum` is EXACT and is a `Decimal`; its
    /// `Min`/`Max` compare exactly; its `Mean`/`Median`/`StdDev` are `float`, as `aggType` declares
    /// for every source type, and are the nearest float to each value.
    let aggregate (fn: AggFn) (col: Column) : Result<Cell, AggregateError> =
        let cells = col.Cells
        // Each branch reads the input once, on demand: only Count/CountDistinct/Min/Max need the
        // present cells and only the numeric aggregates need the numbers, so neither is built eagerly.
        let present () =
            cells |> List.filter (fun c -> not (Cell.isNull c))

        let nums () = cells |> List.choose aggAsNum

        let isNumeric = col.Type = IntType || col.Type = FloatType || col.Type = DecimalType

        let requireNumeric (k: unit -> Result<Cell, AggregateError>) =
            if isNumeric then
                k ()
            else
                Error(IncompatibleAggType(aggFnTag fn, ColumnType.tag col.Type, [ "int"; "float"; "decimal" ]))

        match fn with
        | Count -> Ok(Int(List.length (present ())))
        | CountDistinct -> Ok(Int(present () |> List.map distinctToken |> List.distinct |> List.length))
        | First ->
            Ok(
                match cells with
                | [] -> Null
                | c :: _ -> c
            )
        | Last ->
            Ok(
                match cells with
                | [] -> Null
                | _ -> List.last cells
            )
        | Sum ->
            requireNumeric (fun () ->
                if col.Type = DecimalType then
                    // Exact: the sum of a decimal column is the decimal it is, to the last digit, and
                    // cannot overflow. It never passes through `float`.
                    match cells |> List.choose aggAsDecimal with
                    | [] -> Ok Null
                    | first :: rest ->
                        rest
                        |> List.fold (fun acc d -> DecimalText.add acc d |> Option.defaultValue acc) first
                        |> Decimal
                        |> Ok
                else
                    let ns = nums ()

                    if List.isEmpty ns then
                        Ok Null
                    elif col.Type = IntType then
                        checkedSumInt (ns |> List.sumBy int64)
                    else
                        Ok(Float(List.sum ns)))
        | Mean ->
            requireNumeric (fun () ->
                let ns = nums ()

                if List.isEmpty ns then
                    Ok Null
                else
                    Ok(Float(List.sum ns / float (List.length ns))))
        | StdDev ->
            requireNumeric (fun () ->
                let ns = nums ()

                if List.isEmpty ns then
                    Ok Null
                else
                    let n = float (List.length ns)
                    let mean = List.sum ns / n
                    let var = (ns |> List.sumBy (fun x -> (x - mean) * (x - mean))) / n
                    Ok(Float(sqrt var)))
        | Median ->
            requireNumeric (fun () ->
                let ns = nums () |> List.sort

                match ns with
                | [] -> Ok Null
                | _ ->
                    let n = List.length ns
                    let mid = n / 2

                    if n % 2 = 1 then
                        Ok(Float(List.item mid ns))
                    else
                        Ok(Float((List.item (mid - 1) ns + List.item mid ns) / 2.0)))
        | Min
        | Max ->
            match present () with
            | [] -> Ok Null
            | first :: rest ->
                let pick a b =
                    match aggCompare a b with
                    | Some c -> if (fn = Min) = (c <= 0) then a else b
                    | None -> a

                Ok(List.fold pick first rest)

module Table =

    /// The row count of a table — the length of its first column, or 0 for a schema-only table.
    let rowCount (t: Table) : int =
        match t.Columns with
        | c :: _ -> Column.length c
        | [] -> 0

    let columnNames (t: Table) : string list = t.Schema |> List.map fst

    /// Find a column by name.
    let tryColumn (name: string) (t: Table) : Column option =
        t.Columns |> List.tryFind (fun c -> c.Name = name)

    /// The empty table (no columns, no rows).
    let empty: Table = { Schema = []; Columns = [] }

    /// Structural well-formedness (Phase 43). `Column.create` does no validation and `encodeJson`
    /// silently papers over a malformed table (a schema name with no column emits an empty placeholder;
    /// an extra column is dropped; ragged columns encode against the first column's length). `validate`
    /// names the fault instead: (a) every schema name has exactly one matching column and vice-versa,
    /// (b) all columns share one length, (c) each column's `Type` matches its schema entry. This is
    /// *structural* well-formedness — content/data-quality rules are the columnar validator's concern.
    let validate (t: Table) : Result<unit, ColumnError> =
        let schemaNames = t.Schema |> List.map fst
        let columnNamesList = t.Columns |> List.map (fun c -> c.Name)

        let missing =
            schemaNames |> List.filter (fun n -> not (List.contains n columnNamesList))

        let extra =
            columnNamesList |> List.filter (fun n -> not (List.contains n schemaNames))

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
                    | Some c when c.Type <> ty -> Some(TypeMismatch(name, ColumnType.tag ty, ColumnType.tag c.Type))
                    | _ -> None)

            match typeFault with
            | Some e -> Error e
            | None ->
                // Equal lengths across all columns.
                match t.Columns with
                | [] -> Ok()
                | first :: rest ->
                    let len0 = Column.length first

                    match rest |> List.tryFind (fun c -> Column.length c <> len0) with
                    | Some c -> Error(LengthMismatch(c.Name, len0, Column.length c))
                    | None -> Ok()

// ---- schema compatibility (Phase 33) ----

/// A structured schema delta (Phase 33): what changed between an `old` `Schema` and a `target` one —
/// columns added (in `target`, absent from `old`), removed (in `old`, absent from `target`), retyped
/// (same name, different `ColumnType`), and whether the columns common to both appear in a different
/// relative order. A *rename* surfaces as a removed+added pair (the schema carries no rename intent; a
/// consumer that knows a rename happened reads it from that pair). Nullability is not a schema-level
/// fact in this model (it is the per-cell validity mask), so it is not part of the delta.
type SchemaDelta =
    { Added: (string * ColumnType) list
      Removed: (string * ColumnType) list
      Retyped: (string * ColumnType * ColumnType) list
      Reordered: bool }

/// A compatibility verdict for a schema change relative to the columns a consumer actually depends on
/// (Phase 33) — the data-strand analogue of `verifyChain` for the op-stream. Recoverable + enumerated
/// (GP5): `Breaking` / `Unknown` name their reasons.
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

        { Added = added
          Removed = removed
          Retyped = retyped
          Reordered = commonOld <> commonNew }

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
        | [] -> Compatible
        | reasons -> Breaking reasons

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

    /// A stable, cross-host content `fingerprint` of a `Schema` (Phase 33): the canonical `name:type`
    /// list joined by the `U+0001` separator (which no column name contains), then hashed. Order-
    /// sensitive — column order is part of a schema's identity — so a reorder changes the fingerprint.
    /// Byte-identical across hosts (no host hashing primitive); the schema-version stamp a consumer's
    /// provenance records to detect "same shape" cheaply.
    let fingerprint (s: Schema) : string =
        s
        |> List.map (fun (n, t) -> n + ":" + ColumnType.tag t)
        |> String.concat ""
        |> fnv1a

/// The canonical wire codec for the columnar strand. Column-oriented (a `values` array + a
/// `validity` mask per column, the Arrow layout), reusing the `Fuaran.Core.Wire` canonical rules
/// so numeric columns are byte-identical across hosts. Decode is `Result`-typed with the six-code
/// `ColumnError` envelope. Fable-clean (only `Json` / `Decode`).
module ColumnCodec =

    // ---- encode (Fable-clean `JVal` construction → `Json.render`) ----

    /// The present-cell JSON value for a column of type `ty`. A `Null` cell emits the type
    /// default placeholder (the validity mask records the nullity); a present cell of the wrong
    /// shape for `ty` is normalised toward `ty` only where it is a lossless widening (`Int`→`Float`
    /// in a float column), otherwise it encodes as-is and the round-trip law catches a mis-built
    /// column. Floats use the `Wire` `{0:R}` canonical layout.
    let private cellJson (ty: ColumnType) (c: Cell) : JVal =
        let present =
            match c with
            | Null -> Cell.defaultFor ty
            | other -> other

        match present, ty with
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
        | Null, _ -> JStr "" // unreachable (Null replaced above); defensive

    let private columnJson (c: Column) : JVal =
        let values = c.Cells |> List.map (cellJson c.Type)
        let validity = c.Cells |> List.map (fun cell -> JBool(not (Cell.isNull cell)))
        JObj [ "values", JArr values; "validity", JArr validity ]

    let private schemaJson (schema: Schema) : JVal =
        schema
        |> List.map (fun (name, ty) -> JObj [ "name", JStr name; "type", JStr(ColumnType.tag ty) ])
        |> JArr

    /// Encode a `DataSource` to a `JVal` — embedded columns keyed by name (type comes from the
    /// schema, so it is not repeated), or a `ref` string. Author-ordered keys (`schema` first) →
    /// deterministic, byte-identical output.
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
    /// byte-identical across the .NET, Fable, TS and Python hosts. **Assumes a well-formed, all-finite
    /// source** — use `tryEncode` for the guarded, total entry point on untrusted/derived data.
    let encode (src: DataSource) : string = Canon.render (encodeJson src)

    /// The first non-finite `Float` cell in a `DataSource` as `(columnName, token)`, or `None` if all
    /// floats are finite. A non-finite float has no Fuaran wire representation (Phase 38) — the same
    /// posture as the tree wire's `Json.tryRender` (Phase 12).
    let private firstNonFinite (src: DataSource) : (string * string) option =
        match src with
        | Ref _ -> None
        | Embedded t ->
            t.Columns
            |> List.tryPick (fun c ->
                c.Cells
                |> List.tryPick (fun cell ->
                    match cell with
                    | Float f when System.Double.IsNaN f -> Some(c.Name, "NaN")
                    | Float f when System.Double.IsPositiveInfinity f -> Some(c.Name, "Infinity")
                    | Float f when System.Double.IsNegativeInfinity f -> Some(c.Name, "-Infinity")
                    | _ -> None))

    let private notCanonicalDecimal (column: string) : ColumnError =
        MalformedShape(
            column
            + ": a decimal cell must carry canonical decimal text — an optional '-', integer digits with no leading zero, and a '.' with fraction digits only where the fraction is non-zero, with no trailing zero (build the cell with Cell.decimal)"
        )

    /// The first column holding a `Decimal` cell whose text is not canonical, or `None`. Such a cell
    /// would encode to a string the decoder rewrites (`1.50` comes back as `1.5`) or refuses
    /// (`abc`), so the value that went in is not the value that comes out.
    let private firstNonCanonicalDecimal (src: DataSource) : string option =
        match src with
        | Ref _ -> None
        | Embedded t ->
            t.Columns
            |> List.tryPick (fun c ->
                c.Cells
                |> List.tryPick (fun cell ->
                    match cell with
                    | Decimal s when not (DecimalText.isCanonical s) -> Some c.Name
                    | _ -> None))

    /// Total, guarded encode (Phases 38 + 43). Rejects a structurally-malformed `Table`
    /// (`Table.validate`) and any non-finite `Float` cell with a typed `ColumnError` instead of silently
    /// emitting a `Table` that round-trips to a *different* value (extra/missing columns dropped) or
    /// un-decodable wire (`"NaN"` where a `JFloat` is expected). Over a well-formed, all-finite source it
    /// is exactly `Ok (encode src)`. A `Decimal` cell whose text is not canonical is rejected on the
    /// same ground (`0.33.0`).
    let tryEncode (src: DataSource) : Result<string, ColumnError> =
        let structural =
            match src with
            | Ref _ -> Ok()
            | Embedded t -> Table.validate t

        structural
        |> Result.bind (fun () ->
            match firstNonFinite src with
            | Some(col, tok) -> Error(NonFiniteFloat(col, tok))
            | None ->
                match firstNonCanonicalDecimal src with
                | Some col -> Error(notCanonicalDecimal col)
                | None -> Ok(encode src))

    // ---- decode (`Json.parse` → six-code `ColumnError`) ----

    let private kindName =
        function
        | JStr _ -> "string"
        | JInt _ -> "int"
        | JBool _ -> "bool"
        | JFloat _ -> "float"
        | JArr _ -> "array"
        | JObj _ -> "object"

    let private getField (name: string) (el: JVal) : Result<JVal, ColumnError> =
        match el with
        | JObj fields ->
            match fields |> List.tryFind (fun (k, _) -> k = name) with
            | Some(_, v) -> Ok v
            | None -> Error(MissingField name)
        | other -> Error(MalformedShape("expected object, got " + kindName other))

    let private tryField (name: string) (el: JVal) : JVal option =
        match el with
        | JObj fields -> fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd
        | _ -> None

    let private asArr (ctx: string) (el: JVal) : Result<JVal list, ColumnError> =
        match el with
        | JArr xs -> Ok xs
        | other -> Error(MalformedShape(ctx + ": expected array, got " + kindName other))

    let private asStr (ctx: string) (el: JVal) : Result<string, ColumnError> =
        match el with
        | JStr s -> Ok s
        | other -> Error(MalformedShape(ctx + ": expected string, got " + kindName other))

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

    /// Decode one present value into a `Cell` of the declared column type, or a `TypeMismatch`.
    /// A float column accepts an integer JSON token (lossless widening); a timestamp column
    /// accepts an epoch number (Phase 94 — models emit epoch instants against their own
    /// correct `"timestamp"` schema; unit by magnitude: ≥ 1e11 ⇒ milliseconds, else seconds —
    /// epoch-seconds stay below 1e11 until year 5138). Every other type requires its exact
    /// JSON kind.
    ///
    /// A decimal column (`0.33.0`) reads a STRING of decimal text and canonicalises it, so `12.50`
    /// decodes to `Decimal "12.5"`; it reads an integer token, which is exact; and it REFUSES a
    /// fractional number token as a `TypeMismatch`. That token has already been through a float by
    /// the time it arrives here, and a type whose purpose is exactness cannot accept a value it
    /// cannot vouch for. An emitter writes a decimal as a string.
    let private decodeCell (colName: string) (ty: ColumnType) (v: JVal) : Result<Cell, ColumnError> =
        let mismatch () =
            Error(TypeMismatch(colName, ColumnType.tag ty, kindName v))

        let epochToIso (i: int64) =
            let secs = if abs i >= 100_000_000_000L then i / 1000L else i
            Timestamp(isoOfEpochSeconds secs)

        match ty, v with
        | IntType, JInt i -> Ok(Int i)
        | FloatType, JFloat f -> Ok(Float f)
        | FloatType, JInt i -> Ok(Float(float i))
        | BoolType, JBool b -> Ok(Bool b)
        | StringType, JStr s -> Ok(Str s)
        | DateType, JStr s -> Ok(Date s)
        | TimestampType, JStr s -> Ok(Timestamp s)
        // Epoch-seconds fit Int32 (so arrive as JInt); epoch-milliseconds overflow the
        // parser's Int32 path and arrive as a whole-valued JFloat.
        | TimestampType, JInt i -> Ok(epochToIso (int64 i))
        | TimestampType, JFloat f when f = floor f && abs f < 9e15 -> Ok(epochToIso (int64 f))
        | DecimalType, JInt i -> Ok(Decimal(string i))
        | DecimalType, JStr s ->
            match DecimalText.tryCanonical s with
            | Some canonical -> Ok(Decimal canonical)
            | None ->
                Error(
                    MalformedShape(
                        colName
                        + ": a decimal value must be decimal text — an optional '-', digits, and an optional '.' followed by digits, with no exponent, sign '+', separator or white space"
                    )
                )
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
                match tryField "validity" colEl with
                | None -> Ok(values, values |> List.map (fun _ -> JBool true))
                | Some validityEl ->
                    asArr (name + ".validity") validityEl
                    |> Result.map (fun validity -> values, validity))

    /// Decode a single named column against its declared type from the `columns` object.
    let private decodeColumn (columnsObj: JVal) (name: string) (ty: ColumnType) : Result<Column, ColumnError> =
        match tryField name columnsObj with
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
                        | _ :: _, p :: _ -> Error(MalformedShape(name + ".validity: expected bool, got " + kindName p))
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

    /// Decode a `DataSource` from a `JVal` root — the six-code envelope on every failure.
    /// Phase 88: `schema` may be OMITTED on an EMBEDDED source (inferred per
    /// `inferColumnType`, columns in Ordinal key order); a `ref` source still
    /// requires it (no cells to infer from). The canonical encoder always
    /// emits the explicit schema, so the shorthand normalises on re-encode.
    let decodeJson (el: JVal) : Result<DataSource, ColumnError> =
        let schemaR =
            match tryField "schema" el with
            | Some schemaEl -> decodeSchema schemaEl |> Result.map Some
            | None -> Ok None

        schemaR
        |> Result.bind (fun schemaOpt ->
            match tryField "ref" el, schemaOpt with
            | Some refEl, Some _ -> asStr "ref" refEl |> Result.map Ref
            | Some _, None ->
                Error(
                    MalformedShape(
                        "a ref source requires an explicit \"schema\" array — there are no cells to infer column types from"
                    )
                )
            | None, _ ->
                getField "columns" el
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
                                            match tryField name columnsObj with
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
                        |> Result.map (fun columns -> Embedded { Schema = schema; Columns = columns }))))

    /// Decode a wire string into a `DataSource`, surfacing the six-code `ColumnError` envelope
    /// (a JSON-syntax failure becomes `NotJson`).
    let decode (s: string) : Result<DataSource, ColumnError> =
        match Json.parse s with
        | Error m -> Error(NotJson m)
        | Ok el -> decodeJson el

    /// Render a `ColumnError` as a stable human string — the adapter for `Corpus.Codec`'s
    /// `string`-error decode slot and for diagnostics.
    let errorString (e: ColumnError) : string =
        match e with
        | NotJson d -> "not valid JSON: " + d
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

    /// The `Fuaran.Core.Wire.Corpus.Codec` over `DataSource` — encode + a `string`-error decode,
    /// so the columnar strand plugs straight into the conformance corpus tooling (`runCorpus` /
    /// `codecLaws`).
    let codec: Corpus.Codec<DataSource> =
        { Encode = encode
          Decode = fun s -> decode s |> Result.mapError errorString }
