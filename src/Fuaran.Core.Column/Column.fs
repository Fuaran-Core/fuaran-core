namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Column (Phase 28) — the relational/columnar data strand, a new
//  Core substrate parallel to the tree/op-stream spine. A typed, null-aware,
//  Arrow-compatible columnar model + its canonical wire codec. It is the data
//  substrate the compute layer operates on (`Fuaran.Compute.DataFrame`, produced by
//  its own repository since 0.33.0 and under that id since its 0.36.0 — DECISIONS.md
//  D66), the shape the `Query` seam produces, and the shape the UI `DataSource`
//  binding serialises (Compute Layer spec §1).
//
//  It introduces no tree-witness field and no base node type — it is a separate,
//  self-contained data strand. FSharp.Core only; Fable-clean on encode and decode
//  (it reuses `Fuaran.Core.Wire`'s canonical-float / escaping / parser rules, so a
//  numeric column is byte-identical across the .NET and Fable hosts).
// ============================================================================

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
    /// A numeric aggregate over a non-numeric column: `fn` is the aggregate, `colType` the column's
    /// type, and `expected` the numeric types (`IntType`, `FloatType`, `DecimalType`). Typed since
    /// Phase 391; their tags before `1.0.0`.
    | IncompatibleAggType of fn: AggFn * colType: ColumnType * expected: ColumnType list
    /// An answer outside its type's range: an int `Sum` past int32, a float `Sum` or a float
    /// statistic that left the float range over finite input, or a decimal too large to read as a
    /// float for a float-valued aggregate. `detail` says which, with the offending value or column.
    | AggregateOverflow of detail: string
    /// A present cell outside its column's type (Phase 299): the column, its declared type
    /// (`ColumnType` since Phase 391; its tag before `1.0.0`), and the cell — its type's tag, or, for
    /// a `Decimal` cell, the text that is not decimal text. The
    /// aggregate used to read cells by shape and trust the column's type: a `Float` in an int
    /// column was truncated into an int `Sum`, and one in a decimal column was dropped from `Sum`
    /// and counted in `Mean`. Now it is refused, by name, before any aggregate reads it.
    | CellOutsideType of column: string * colType: ColumnType * cell: string

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
            | None -> Error(CellOutsideType(col.Name, col.Type, "decimal text '" + s + "' (not decimal)"))
        | _ ->
            match Cell.typeOf c with
            | Some t when ColumnType.widens t col.Type -> Ok c
            | Some t -> Error(CellOutsideType(col.Name, col.Type, ColumnType.tag t))
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
                            | other -> Cell.token other

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
                passFold (fun _ -> Error(IncompatibleAggType(fn, col.Type, [ IntType; FloatType; DecimalType ])))
            | Sum ->
                match col.Type with
                | DecimalType -> decimalSumFold ()
                | IntType -> intSumFold ()
                | _ -> floatFold col fn false (floatSum col)
            | Mean -> floatFold col fn false (floatMean col)
            | StdDev -> floatFold col fn true (floatStdDev col)
            | Median -> floatFold col fn true (floatMedian col)

        admitAll col fold.Step |> Result.bind fold.Finish
