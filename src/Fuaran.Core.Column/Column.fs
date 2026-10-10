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
//
//  Since Phase 417 (the `1.0.0` slot) a column's storage is one typed `Vector` per
//  column behind the `ColumnData` union, with its `Validity` beside it, where it
//  was a `Cell list`: a consumer holds typed storage, an indexed read is O(1), and
//  a cell outside its column's type cannot be represented. Since Phase 420 a
//  null-free column carries no mask (`AllValid`); only a column with an absent
//  row holds one (`Mask`). Since Phase 422 a date column holds `int32` days and
//  a timestamp column integer epoch seconds plus a fraction at its `TimeUnit`.
//  `Cell` stays the scalar read type; `Column.ofCells` / `Column.toCells` are
//  the migration bridge.
// ============================================================================

/// Which rows of a column are PRESENT and which are the `Null` the wire's validity array marks
/// absent (Phase 417; two cases since Phase 420). A column with no null carries no mask at all —
/// `AllValid` — so the common case holds nothing beside its values; a column with a null carries a
/// `Mask`, one `bool` per row, `true` where the row is present.
///
/// `AllValid` carries no length: it reads as the all-true mask of its column's length, the values
/// vector's (`Validity.toMask` materialises it). Core's builders NORMALISE — `Validity.ofArray`,
/// `ofList` and `ofVector`, `Column.ofCells` and decode answer `AllValid` wherever no row is absent
/// — so Core never builds a `Mask` without a clear bit. A `Mask` built by hand may still be all
/// true, and a column holding one is EQUAL to the same column holding `AllValid`: column equality
/// compares the materialised masks (`ColumnData`). `=` on two `Validity` values alone compares the
/// representations, because with no length `AllValid` has no mask to compare — compare columns.
[<NoComparison>]
type Validity =
    /// Every row of the column is present; no mask is held.
    | AllValid
    /// One `bool` per row, `true` where the row is present — the in-memory form of the wire's
    /// validity array, for a column with at least one absent row.
    | Mask of present: Vector<bool>

/// Validity reads and constructors.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Validity =

    /// The validity a mask vector describes, without a copy: `AllValid` when every bit is set (an
    /// empty vector included), else a `Mask` over `present` itself.
    let ofVector (present: Vector<bool>) : Validity =
        if Vector.exists not present then Mask present else AllValid

    /// The validity of the bits of `present`: `AllValid` when every bit is set, else a `Mask` over
    /// ITS OWN COPY of `present`.
    let ofArray (present: bool[]) : Validity =
        if Array.forall id present then
            AllValid
        else
            Mask(Vector.ofArray present)

    /// The validity of the list's bits, in order: `AllValid` when every bit is set.
    let ofList (present: bool list) : Validity =
        if List.forall id present then
            AllValid
        else
            Mask(Vector.ofList present)

    /// Is row `i` present? Under a `Mask`, `false` for an index off either end — total. `AllValid`
    /// has no length, so every non-negative row is present under it: a reader bounds the index by
    /// the values first, as `Column.isPresent` and `Column.cell` do.
    let isPresent (i: int) (v: Validity) : bool =
        match v with
        | AllValid -> i >= 0
        | Mask m -> i >= 0 && i < m.Length && m[i]

    /// The mask of a column of `n` rows, materialised on request: a `Mask`'s own vector, with no
    /// copy, and for `AllValid` a fresh vector of `n` set bits — the all-true mask of the right
    /// length, which is what the wire's validity array of a null-free column holds.
    let toMask (n: int) (v: Validity) : Vector<bool> =
        match v with
        | AllValid -> Vector.init n (fun _ -> true)
        | Mask m -> m

    /// How many of a column's `n` rows are present: `n` under `AllValid`, the mask's set bits under
    /// a `Mask`.
    let presentCount (n: int) (v: Validity) : int =
        match v with
        | AllValid -> n
        | Mask m -> Vector.fold (fun k b -> if b then k + 1 else k) 0 m

/// Equality over a column's storage (Phase 417): two typed vectors with their validities hold the
/// same cells when their MATERIALISED masks agree (Phase 420: `AllValid` is the all-true mask of the
/// values' length, so it equals a `Mask` that is all true and of that length, and nothing else) and,
/// at every PRESENT row, the elements are equal under the vector's element identity. The element at
/// an absent row is not a cell and takes no part — a builder that copies writes the type's zero
/// there, and a builder that adopts leaves what it was handed.
module internal ColumnStorage =

    /// Which rows of an `n`-row column a walk reads as present (Phase 421), so a loop over a typed
    /// vector reads presence with no match per row: `Count` rows can be present — every row of an
    /// `AllValid` column, and none past a `Mask`'s end, as `Validity.isPresent` reads it — and
    /// `Bits` is the mask's backing array from `Offset`, or `null` for `AllValid`, where every one of
    /// those rows is present. `isSet` reads one row.
    [<NoComparison; NoEquality>]
    type PresentRows =
        { Count: int
          Bits: bool[]
          Offset: int }

    /// The rows of an `n`-row column under `v` (see `PresentRows`).
    let presentRows (n: int) (v: Validity) : PresentRows =
        match v with
        | AllValid -> { Count = n; Bits = null; Offset = 0 }
        | Mask m ->
            { Count = min n m.Length
              Bits = m.Items
              Offset = m.Offset }

    /// Is row `i` (below `rows.Count`) present?
    let inline isSet (rows: PresentRows) (i: int) : bool =
        isNull rows.Bits || rows.Bits[rows.Offset + i]

    /// The masks of two validities of `n`-row columns are one mask — `Validity.toMask n` of each
    /// equal, without materialising either.
    let sameMask (n: int) (vx: Validity) (vy: Validity) : bool =
        match vx, vy with
        | AllValid, AllValid -> true
        | AllValid, Mask m
        | Mask m, AllValid -> m.Length = n && not (Vector.exists not m)
        | Mask a, Mask b -> a = b

    /// A hash agreeing with `sameMask`: the materialised mask's length and its first few bits.
    let maskHash (n: int) (v: Validity) : int =
        match v with
        | AllValid ->
            let mutable h = n

            for _ in 1 .. min n VectorElements.HashedPrefix do
                h <- (h * 31) + 1

            h
        | Mask m ->
            let mutable h = m.Length

            for i in 0 .. min m.Length VectorElements.HashedPrefix - 1 do
                h <- (h * 31) + (if m[i] then 1 else 0)

            h

    /// Equal at every present row, under `VectorElements.equal`.
    let presentEqual (xs: Vector<'T>) (vx: Validity) (ys: Vector<'T>) (vy: Validity) : bool =
        xs.Length = ys.Length
        && sameMask xs.Length vx vy
        && (let mutable same = true
            let mutable i = 0

            while same && i < xs.Length do
                if Validity.isPresent i vx then
                    same <- VectorElements.equal xs[i] ys[i]

                i <- i + 1

            same)

    /// The fraction of row `i` of a timestamp column: `0` where the column holds none (`None` reads
    /// as every fraction zero, Phase 422) and for an index off the fraction vector's end.
    let fractionAt (fraction: Vector<int> option) (i: int) : int =
        match fraction with
        | Some f when i >= 0 && i < f.Length -> f[i]
        | _ -> 0

    /// Two timestamp columns' fractions agree at every present row of `vx` (the masks were already
    /// compared), a `None` reading as zeros — the materialised fractions, as `sameMask` compares the
    /// materialised masks.
    let sameFraction (n: int) (vx: Validity) (fx: Vector<int> option) (fy: Vector<int> option) : bool =
        match fx, fy with
        | None, None -> true
        | _ ->
            let mutable same = true
            let mutable i = 0

            while same && i < n do
                if Validity.isPresent i vx then
                    same <- fractionAt fx i = fractionAt fy i

                i <- i + 1

            same

    /// A hash agreeing with `presentEqual`: the length, the mask, and the first few present elements.
    let presentHash (xs: Vector<'T>) (vx: Validity) : int =
        let mutable h = xs.Length * 31 + maskHash xs.Length vx
        let mutable seen = 0
        let mutable i = 0

        while seen < VectorElements.HashedPrefix && i < xs.Length do
            if Validity.isPresent i vx then
                h <- (h * 31) + VectorElements.hashOf xs[i]
                seen <- seen + 1

            i <- i + 1

        h

/// A column's storage (Phase 417): one typed, immutable `Vector` of values and the `Validity` that
/// says which rows are present — `AllValid`, holding nothing, or a `Mask` (Phase 420) — one case per
/// `ColumnType`. The element at an absent row is a
/// placeholder, never a cell — a reader that wants the cell asks `Column.cell`, which answers
/// `Null` there. A `Mask` and its `values` have one length; `Table.validate` names a pair that does
/// not (`LengthMismatch`), and an `AllValid` column cannot disagree with its values.
///
/// A widened cell is held NORMALISED: an `Int` in a float column is the float it widens to, and in
/// a decimal column the decimal text of its digits, exactly as decode already normalised it; so
/// `Floats` holds floats only and `Decimals` decimal text only. `Dates` and `Timestamps` hold
/// integers (Phase 422, DECISIONS.md D143): a date its day count since 1970-01-01, a timestamp its
/// floor epoch second as an integer-valued float and, for a sub-second unit, the fraction of that
/// second scaled to the unit; the cell a reader is handed is the canonical text (`TemporalText`).
///
/// Equality is by the CELLS: two storages are equal when their materialised masks agree (so
/// `AllValid` equals an all-true `Mask` of the same length) and every present
/// element is equal under the vector's identity (every NaN one value, `-0.0` equal to `0.0`), so a
/// column compares the way `Cell.compare` compares its cells, on every host.
[<CustomEquality; NoComparison>]
type ColumnData =
    /// An `int` column's values and mask.
    | Ints of values: Vector<int> * validity: Validity
    /// A `float` column's values and mask; a widened `Int` is held as its float.
    | Floats of values: Vector<float> * validity: Validity
    /// A `bool` column's values and mask.
    | Bools of values: Vector<bool> * validity: Validity
    /// A `string` column's values and mask.
    | Strs of values: Vector<string> * validity: Validity
    /// A `date` column's days since 1970-01-01 (Arrow's Date32) and mask; the canonical range is
    /// `TemporalText.MinDay`..`MaxDay` (`0000-01-01`..`9999-12-31`).
    | Dates of values: Vector<int> * validity: Validity
    /// A timestamp column at `unit`: each row's floor epoch second, an integer-valued float, and for
    /// a sub-second unit the fraction of that second scaled to the unit (`0 <= f <
    /// TimeUnit.scale unit`), with its mask. `fraction = None` reads as every fraction zero — a
    /// seconds column holds none, and Core's builders answer `None` wherever no present row has a
    /// fraction; equality compares the materialised fractions, so `None` equals an all-zero `Some`.
    | Timestamps of unit: TimeUnit * seconds: Vector<float> * fraction: Vector<int> option * validity: Validity
    /// A `decimal` column's canonical decimal texts and mask; a widened `Int` is held as its digits.
    | Decimals of values: Vector<string> * validity: Validity

    /// The column type the case carries.
    member this.Type: ColumnType =
        match this with
        | Ints _ -> IntType
        | Floats _ -> FloatType
        | Bools _ -> BoolType
        | Strs _ -> StringType
        | Dates _ -> DateType
        | Timestamps(u, _, _, _) -> TimestampType u
        | Decimals _ -> DecimalType

    /// The validity, whatever the case.
    member this.Validity: Validity =
        match this with
        | Ints(_, v)
        | Floats(_, v)
        | Bools(_, v)
        | Strs(_, v)
        | Dates(_, v)
        | Timestamps(_, _, _, v)
        | Decimals(_, v) -> v

    /// The number of rows — the values vector's length.
    member this.Length: int =
        match this with
        | Ints(xs, _) -> xs.Length
        | Floats(xs, _) -> xs.Length
        | Bools(xs, _) -> xs.Length
        | Dates(xs, _) -> xs.Length
        | Timestamps(_, xs, _, _) -> xs.Length
        | Strs(xs, _)
        | Decimals(xs, _) -> xs.Length

    /// Equal by their cells (see the type).
    override this.Equals(other: obj) : bool =
        match other with
        | :? ColumnData as that ->
            match this, that with
            | Ints(xs, vx), Ints(ys, vy) -> ColumnStorage.presentEqual xs vx ys vy
            | Floats(xs, vx), Floats(ys, vy) -> ColumnStorage.presentEqual xs vx ys vy
            | Bools(xs, vx), Bools(ys, vy) -> ColumnStorage.presentEqual xs vx ys vy
            | Strs(xs, vx), Strs(ys, vy) -> ColumnStorage.presentEqual xs vx ys vy
            | Dates(xs, vx), Dates(ys, vy) -> ColumnStorage.presentEqual xs vx ys vy
            | Timestamps(ux, xs, fx, vx), Timestamps(uy, ys, fy, vy) ->
                ux = uy
                && ColumnStorage.presentEqual xs vx ys vy
                && ColumnStorage.sameFraction xs.Length vx fx fy
            | Decimals(xs, vx), Decimals(ys, vy) -> ColumnStorage.presentEqual xs vx ys vy
            | _ -> false
        | _ -> false

    /// A hash agreeing with `Equals`.
    override this.GetHashCode() : int =
        let tag =
            match this with
            | Ints _ -> 1
            | Floats _ -> 2
            | Bools _ -> 3
            | Strs _ -> 4
            | Dates _ -> 5
            | Timestamps _ -> 6
            | Decimals _ -> 7

        let body =
            match this with
            | Ints(xs, v) -> ColumnStorage.presentHash xs v
            | Floats(xs, v) -> ColumnStorage.presentHash xs v
            | Bools(xs, v) -> ColumnStorage.presentHash xs v
            | Dates(xs, v) -> ColumnStorage.presentHash xs v
            // The seconds only: two equal columns hold equal seconds whatever their fractions' form.
            | Timestamps(_, xs, _, v) -> ColumnStorage.presentHash xs v
            | Strs(xs, v)
            | Decimals(xs, v) -> ColumnStorage.presentHash xs v

        (tag * 397) ^^^ body

/// A timestamp column's storage as `Column.tryTimestamps` answers it (Phase 422): the unit, the
/// floor epoch seconds, and the fractions scaled to the unit (`None` for every fraction zero).
type TimestampVectors =
    {
        /// The resolution the column holds its instants at.
        Unit: TimeUnit
        /// Each row's floor epoch second, an integer-valued float.
        Seconds: Vector<float>
        /// Each row's fraction of its second, scaled to `Unit`; `None` where every fraction is zero.
        Fraction: Vector<int> option
    }

/// A typed, null-aware column (Phase 417: typed storage, where it was a `Cell list`). `Data` holds
/// one typed vector and a validity mask that co-index with the table's rows; the column's `Type` is
/// the case `Data` carries, so a column cannot disagree with its own storage and a present cell is
/// always of its column's type. The codec refuses a value outside the column's type at decode,
/// `Column.ofCells` refuses a cell outside it at construction; the typed builders check nothing
/// beyond what their types state, and a column built by hand is checked where it is used
/// (`Table.validate`).
type Column =
    {
        /// The key a table matches against its schema entry and looks the column up by; unique
        /// within a table (`Table.validate`), compared exactly.
        Name: string
        /// The storage: one typed vector and its validity mask, in row order.
        Data: ColumnData
    }

    /// The declared type — the case `Data` carries.
    member c.Type: ColumnType = c.Data.Type

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
    /// (`ColumnType` since Phase 391; its tag before `1.0.0`), and the cell — for a `Decimal` cell,
    /// the text that is not decimal text. Since Phase 417 a cell of another TYPE cannot sit in a
    /// column (`Column.ofCells` refuses it, and the typed storage cannot hold it), so the one cell
    /// this names is a `Decimal` whose text is not decimal: refused, by name, before any aggregate
    /// reads it. The case stays (Phase 421) because that text CAN still reach an aggregate: a
    /// decimal vector holds any string, `Column.ofDecimals` checks nothing and `Column.ofCells` holds
    /// decimal text as found, and `aggregate` reads a column without validating its table —
    /// `Table.validate` refuses the same text, as `MalformedShape`, only where a table is validated.
    | CellOutsideType of column: string * colType: ColumnType * cell: string

/// THE CODEC ENVELOPE — the closed set of refusals of the columnar codec, of `Table.validate` and of
/// `Column.ofCells`, the substrate's recoverable error discipline (GP4/GP5): every failure *names
/// what went wrong* and, where a closed set is expected, *enumerates the alternatives*. Its size is
/// stated nowhere but in this type: a new case is a breaking-source change for every exhaustive
/// match (FS0025), and is classed as one in STABILITY.md.
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
    /// A `type` tag was not one of the fixed scalar set; `expected` lists the valid types, in
    /// `ColumnType.all`'s order (`ColumnType` since Phase 391; their tags before `1.0.0`).
    | UnknownType of got: string * expected: ColumnType list
    /// A present cell's type does not widen into its column's declared type — its JSON kind on
    /// decode, its `Cell` case in `Column.ofCells` (Phase 417; `Table.validate` until then, which
    /// now cannot meet one) — or a column's `Type` disagrees with its schema entry. `expected` is the
    /// declared type (`ColumnType` since Phase 391; its tag before `1.0.0`); `got` is what was found
    /// — a JSON value's kind on decode, a `Cell`'s or a column's type tag otherwise.
    | TypeMismatch of column: string * expected: ColumnType * got: string
    /// A column's `values` and `validity` arrays had different lengths (they must co-index) — on the
    /// wire, or in a `ColumnData` built by hand (Phase 417).
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

/// Column reads, the typed builders and readers, the cell-list bridge, and the pinned aggregate
/// semantics — the single `aggregate` the compute layer's grouping and pivoting call rather than copy.
module Column =

    /// The number of rows in a column.
    let length (c: Column) : int = c.Data.Length

    /// The validity: which rows are present — `AllValid` for a null-free column, which holds no mask.
    let validity (c: Column) : Validity = c.Data.Validity

    /// The validity mask, materialised on request (Phase 420): one `bool` per row, `true` where the
    /// row is present. A `Mask` column answers the mask it holds, with no copy; an `AllValid` column
    /// a fresh all-true vector of its length.
    let mask (c: Column) : Vector<bool> =
        Validity.toMask c.Data.Length c.Data.Validity

    /// Is row `i` present (not `Null`)? `false` for an out-of-range index — total.
    let isPresent (i: int) (c: Column) : bool =
        i < c.Data.Length && Validity.isPresent i c.Data.Validity

    /// The cell at row `i` (`Null` for an absent row and for an out-of-range index — total). O(1)
    /// since Phase 417: one validity read and one vector read.
    let cell (i: int) (c: Column) : Cell =
        if not (isPresent i c) then
            Null
        else
            match c.Data with
            | Ints(xs, _) -> Int xs[i]
            | Floats(xs, _) -> Float xs[i]
            | Bools(xs, _) -> Bool xs[i]
            | Strs(xs, _) -> Str xs[i]
            | Dates(xs, _) -> Date(TemporalText.dateText xs[i])
            | Timestamps(u, xs, f, _) -> Timestamp(TemporalText.instantText u xs[i] (ColumnStorage.fractionAt f i))
            | Decimals(xs, _) -> Decimal xs[i]

    // ---- the typed builders and readers (Phase 417) ----
    // Each builder takes the storage as built — a `Vector` the caller copied into (`Vector.ofArray`)
    // or adopted (`Vector.adopt`), and `AllValid` or a `Mask` — and checks nothing: a `Mask` is
    // expected to be the values' length, and `Table.validate` names one that is not. A builder
    // does not normalise the `Validity` it is handed (an all-true `Mask` is held as given, and is
    // equal to `AllValid`); `Validity.ofArray` / `ofList` / `ofVector` are the normalising
    // constructors. Each reader answers the values vector where the column is of that type, with no
    // conversion and no copy; the validity is `Column.validity`, the materialised mask `Column.mask`.

    /// An `int` column over `values`, present where `validity` says.
    let ofInts (name: string) (values: Vector<int>) (validity: Validity) : Column =
        { Name = name
          Data = Ints(values, validity) }

    /// A `float` column over `values`, present where `validity` says.
    let ofFloats (name: string) (values: Vector<float>) (validity: Validity) : Column =
        { Name = name
          Data = Floats(values, validity) }

    /// A `bool` column over `values`, present where `validity` says.
    let ofBools (name: string) (values: Vector<bool>) (validity: Validity) : Column =
        { Name = name
          Data = Bools(values, validity) }

    /// A `string` column over `values`, present where `validity` says.
    let ofStrs (name: string) (values: Vector<string>) (validity: Validity) : Column =
        { Name = name
          Data = Strs(values, validity) }

    /// A `date` column over day counts since 1970-01-01, present where `validity` says.
    let ofDates (name: string) (values: Vector<int>) (validity: Validity) : Column =
        { Name = name
          Data = Dates(values, validity) }

    /// A timestamp column at `unit` over floor epoch seconds and, for a sub-second unit, their
    /// fractions scaled to the unit (`None` for every fraction zero), present where `validity` says.
    /// Like every typed builder it checks nothing; `Table.validate` names a second that is not whole
    /// or in range, a fraction out of range, and a fraction vector of another length.
    let ofTimestamps
        (name: string)
        (unit: TimeUnit)
        (seconds: Vector<float>)
        (fraction: Vector<int> option)
        (validity: Validity)
        : Column =
        { Name = name
          Data = Timestamps(unit, seconds, fraction, validity) }

    /// A `decimal` column over canonical decimal texts, present where `validity` says.
    let ofDecimals (name: string) (values: Vector<string>) (validity: Validity) : Column =
        { Name = name
          Data = Decimals(values, validity) }

    /// The values of an `int` column, or `None` for a column of another type.
    let tryInts (c: Column) : Vector<int> option =
        match c.Data with
        | Ints(xs, _) -> Some xs
        | _ -> None

    /// The values of a `float` column, or `None` for a column of another type.
    let tryFloats (c: Column) : Vector<float> option =
        match c.Data with
        | Floats(xs, _) -> Some xs
        | _ -> None

    /// The values of a `bool` column, or `None` for a column of another type.
    let tryBools (c: Column) : Vector<bool> option =
        match c.Data with
        | Bools(xs, _) -> Some xs
        | _ -> None

    /// The values of a `string` column, or `None` for a column of another type.
    let tryStrs (c: Column) : Vector<string> option =
        match c.Data with
        | Strs(xs, _) -> Some xs
        | _ -> None

    /// The day counts of a `date` column, or `None` for a column of another type.
    let tryDates (c: Column) : Vector<int> option =
        match c.Data with
        | Dates(xs, _) -> Some xs
        | _ -> None

    /// The unit, the epoch seconds and the fractions (`None` for every fraction zero) of a timestamp
    /// column, or `None` for a column of another type.
    let tryTimestamps (c: Column) : TimestampVectors option =
        match c.Data with
        | Timestamps(u, xs, f, _) -> Some { Unit = u; Seconds = xs; Fraction = f }
        | _ -> None

    /// The texts of a `decimal` column, or `None` for a column of another type.
    let tryDecimals (c: Column) : Vector<string> option =
        match c.Data with
        | Decimals(xs, _) -> Some xs
        | _ -> None

    // ---- the cell-list bridge (Phase 417) ----

    /// The refusal of a `Date` or `Timestamp` cell whose text is not canonical (Phase 422: the
    /// integer storage cannot hold it) — the `MalformedShape` `Table.validate` named for it before.
    let internal notCanonicalDate (name: string) : ColumnError =
        MalformedShape(
            name
            + ": a date cell must carry a canonical ISO-8601 date, YYYY-MM-DD, naming a day that exists"
        )

    /// As `notCanonicalDate`, for an instant.
    let internal notCanonicalInstant (name: string) : ColumnError =
        MalformedShape(
            name
            + ": a timestamp cell must carry a canonical ISO-8601 UTC timestamp, YYYY-MM-DDThh:mm:ssZ or YYYY-MM-DDThh:mm:ss.FZ with one to nine fraction digits and no trailing zero, naming an instant that exists"
        )

    /// The fraction vector a timestamp column at `unit` keeps for `fraction`: `None` for a seconds
    /// column and wherever no PRESENT row has a fraction (Phase 422's normal form), else the vector.
    let internal normalFraction (unit: TimeUnit) (fraction: int[]) (validity: Validity) : Vector<int> option =
        let mutable any = false
        let mutable i = 0

        while not any && i < fraction.Length do
            if fraction[i] <> 0 && Validity.isPresent i validity then
                any <- true

            i <- i + 1

        match unit with
        | TimeUnit.Seconds -> None
        | _ when not any -> None
        | _ -> Some(Vector.adopt fraction)

    /// The typed storage of `cells` for a column of type `ty`, or the first present cell it cannot
    /// hold, in row order: a cell whose type does not widen into `ty` (`ColumnType.widens`) as the
    /// `TypeMismatch` naming it, and a `Date` or `Timestamp` whose text is not canonical as a
    /// `MalformedShape` (Phase 422 — the integer storage cannot hold it). A widened cell is
    /// normalised (an `Int` in a float column to its float, in a decimal column to its digits, an
    /// instant into a finer unit's scaled fraction); the type's zero is written at every absent row,
    /// and a list with no `Null` is `AllValid` (Phase 420). Everything else a cell can carry is held
    /// as found — a non-finite float, decimal text that is not canonical — and is `Table.validate`'s
    /// to refuse at the codec, as before.
    let private storageOfCells (name: string) (ty: ColumnType) (cells: Cell list) : Result<ColumnData, ColumnError> =
        let n = List.length cells
        let mask = Array.zeroCreate<bool> n

        /// Fill `out` from the cells through `pick`, which reads the typed value of a present cell
        /// that fits, or names the refusal.
        let fill (out: 'T[]) (pick: Cell -> Result<'T option, ColumnError>) =
            let mutable fault = None
            let mutable i = 0
            let mutable rest = cells

            while fault.IsNone && not rest.IsEmpty do
                match pick rest.Head with
                | Ok(Some v) ->
                    out[i] <- v
                    mask[i] <- true
                | Ok None -> ()
                | Error e -> fault <- Some e

                i <- i + 1
                rest <- rest.Tail

            match fault with
            | Some e -> Error e
            | None -> Ok(out, Validity.ofVector (Vector.adopt mask))

        let typed (out: 'T[]) pick =
            fill out pick |> Result.map (fun (xs, v) -> Vector.adopt xs, v)

        let outside (c: Cell) : Result<'T option, ColumnError> =
            match Cell.typeOf c with
            | Some t -> Error(TypeMismatch(name, ty, ColumnType.tag t))
            | None -> Ok None

        match ty with
        | IntType ->
            typed (Array.zeroCreate n) (fun c ->
                match c with
                | Int i -> Ok(Some i)
                | other -> outside other)
            |> Result.map Ints
        | FloatType ->
            typed (Array.zeroCreate n) (fun c ->
                match c with
                | Float f -> Ok(Some f)
                | Int i -> Ok(Some(float i))
                | other -> outside other)
            |> Result.map Floats
        | BoolType ->
            typed (Array.zeroCreate n) (fun c ->
                match c with
                | Bool b -> Ok(Some b)
                | other -> outside other)
            |> Result.map Bools
        | StringType ->
            typed (Array.create n "") (fun c ->
                match c with
                | Str s -> Ok(Some s)
                | other -> outside other)
            |> Result.map Strs
        | DateType ->
            typed (Array.zeroCreate n) (fun c ->
                match c with
                | Date s ->
                    match TemporalText.tryDays s with
                    | Some d -> Ok(Some d)
                    | None -> Error(notCanonicalDate name)
                | other -> outside other)
            |> Result.map Dates
        | TimestampType unit ->
            let fraction = Array.zeroCreate<int> n

            fill (Array.create n (0.0, 0)) (fun c ->
                match c with
                | Timestamp s when not (TemporalText.isCanonicalTimestamp s) -> Error(notCanonicalInstant name)
                | Timestamp s ->
                    match TemporalText.tryInstant unit s with
                    | Some pair -> Ok(Some pair)
                    | None -> outside c
                | other -> outside other)
            |> Result.map (fun (pairs: (float * int)[], v) ->
                let seconds = Array.zeroCreate<float> n

                for i in 0 .. n - 1 do
                    let (second, f) = pairs[i]
                    seconds[i] <- second
                    fraction[i] <- f

                Timestamps(unit, Vector.adopt seconds, normalFraction unit fraction v, v))
        | DecimalType ->
            typed (Array.create n DecimalText.zero) (fun c ->
                match c with
                | Decimal s -> Ok(Some s)
                | Int i -> Ok(Some(string i))
                | other -> outside other)
            |> Result.map Decimals

    /// A column of type `ty` holding `cells`, in row order — the migration bridge from the `Cell
    /// list` column (Phase 417), and the one construction that reads cells. It REFUSES the first
    /// present cell whose type does not widen into `ty` (`ColumnType.widens`), as the `TypeMismatch`
    /// `Table.validate` used to name for it: the typed storage cannot hold a `Bool` in an int column,
    /// so no column can. A widened cell is normalised at construction, as decode normalises it: an
    /// `Int` in a float column is held as its float and read back as a `Float`, and in a decimal
    /// column as its digits and read back as a `Decimal`. What the type CAN hold is held as found
    /// and refused where it always was — a non-finite float, and decimal, date or timestamp text
    /// that is not canonical, by `Table.validate` and so by `ColumnCodec.tryEncode`; a `Decimal`
    /// whose text is not decimal by `Column.aggregate`.
    let ofCells (name: string) (ty: ColumnType) (cells: Cell list) : Result<Column, ColumnError> =
        storageOfCells name ty cells
        |> Result.map (fun data -> { Name = name; Data = data })

    /// The column's cells, in row order — a `Null` at every absent row (Phase 417). The bridge back
    /// to the `Cell list` for a reader that has not moved to the typed vectors; a fresh list each call.
    let toCells (c: Column) : Cell list =
        let mutable acc = []

        for i in length c - 1 .. -1 .. 0 do
            acc <- cell i c :: acc

        acc

#if !FABLE_COMPILER
    // ---- the .NET edge (Phase 422, DECISIONS.md D143.6) ----
    // `DateOnly` and `DateTimeOffset` are edge types, never the storage: each converts from and to
    // the integers through `DateOnly.DayNumber` and `DateTimeOffset.UtcTicks` with integer arithmetic
    // and no intermediate string. Absent under Fable, where either is a JavaScript `Date` per value.

    /// `DateOnly.DayNumber` of 1970-01-01.
    let private epochDayNumber = 719162

    /// `DateTimeOffset.UnixEpoch.UtcTicks`.
    let private epochTicks = 621355968000000000L

    /// .NET ticks (100 ns) in a second.
    let private ticksPerSecond = 10000000L

    /// The date at row `i` of a `date` column as a `DateOnly` — `None` where the row is absent or
    /// out of range, the column is of another type, or the day falls outside `DateOnly`'s years
    /// (`0001`–`9999`: the canonical form's year `0000` has no `DateOnly`).
    let tryDateOnly (i: int) (c: Column) : System.DateOnly option =
        match c.Data with
        | Dates(xs, _) when isPresent i c ->
            let dn = int64 xs[i] + int64 epochDayNumber

            if
                dn >= int64 System.DateOnly.MinValue.DayNumber
                && dn <= int64 System.DateOnly.MaxValue.DayNumber
            then
                Some(System.DateOnly.FromDayNumber(int dn))
            else
                None
        | _ -> None

    /// The instant at row `i` of a timestamp column as a UTC `DateTimeOffset` — `None` where the row
    /// is absent or out of range, the column is of another type, or the instant has no exact
    /// `DateTimeOffset`: before year `0001`, or a nanosecond fraction finer than the type's 100 ns
    /// tick (refused, not rounded).
    let tryDateTimeOffset (i: int) (c: Column) : System.DateTimeOffset option =
        match c.Data with
        | Timestamps(u, xs, f, _) when isPresent i c ->
            let scale = int64 (TimeUnit.scale u)
            let fractionTicks = int64 (ColumnStorage.fractionAt f i) * ticksPerSecond

            if fractionTicks % scale <> 0L || xs[i] <> floor xs[i] then
                None
            else
                let ticks = int64 xs[i] * ticksPerSecond + fractionTicks / scale + epochTicks

                if
                    ticks >= System.DateTimeOffset.MinValue.UtcTicks
                    && ticks <= System.DateTimeOffset.MaxValue.UtcTicks
                then
                    Some(System.DateTimeOffset(ticks, System.TimeSpan.Zero))
                else
                    None
        | _ -> None

    /// A `date` column of the days `values` name, `None` an absent row. Total: every `DateOnly` is a
    /// day the canonical form spells.
    let ofDateOnlys (name: string) (values: System.DateOnly option list) : Column =
        let days =
            values
            |> List.map (fun v ->
                match v with
                | Some d -> d.DayNumber - epochDayNumber
                | None -> 0)
            |> Array.ofList

        ofDates name (Vector.adopt days) (Validity.ofList (values |> List.map Option.isSome))

    /// A timestamp column at `unit` of the instants `values` name, `None` an absent row. An offset
    /// is read as the instant it names. REFUSES the first instant finer than `unit` holds, as the
    /// `TypeMismatch` naming the coarsest unit that holds it — as `ofCells` refuses that instant's
    /// text — rather than rounding it.
    let ofDateTimeOffsets
        (name: string)
        (unit: TimeUnit)
        (values: System.DateTimeOffset option list)
        : Result<Column, ColumnError> =
        let n = List.length values
        let seconds = Array.zeroCreate<float> n
        let fraction = Array.zeroCreate<int> n
        let scale = int64 (TimeUnit.scale unit)
        let mutable fault = None
        let mutable i = 0
        let mutable rest = values

        while fault.IsNone && not rest.IsEmpty do
            match rest.Head with
            | Some v ->
                let ticks = v.UtcTicks - epochTicks

                let second =
                    (if ticks < 0L then ticks - ticksPerSecond + 1L else ticks) / ticksPerSecond

                let remainder = ticks - second * ticksPerSecond

                if (remainder * scale) % ticksPerSecond <> 0L then
                    let finer =
                        if remainder % 10000L = 0L then TimeUnit.Milliseconds
                        elif remainder % 10L = 0L then TimeUnit.Microseconds
                        else TimeUnit.Nanoseconds

                    fault <- Some(TypeMismatch(name, TimestampType unit, ColumnType.tag (TimestampType finer)))
                else
                    seconds[i] <- float second
                    fraction[i] <- int ((remainder * scale) / ticksPerSecond)
            | None -> ()

            i <- i + 1
            rest <- rest.Tail

        match fault with
        | Some e -> Error e
        | None ->
            let validity = Validity.ofList (values |> List.map Option.isSome)
            Ok(ofTimestamps name unit (Vector.adopt seconds) (normalFraction unit fraction validity) validity)
#endif


    // ---- pinned aggregate semantics (Phase 36) — the single source the compute layer's GroupBy/Pivot call ----
    // Since Phase 421 every aggregate is a loop over the column's typed vector: a present row is read
    // off the `Validity` with no match per row (`ColumnStorage.presentRows`), and an `int`, `float`,
    // `string` or `decimal` value is folded as itself, never wrapped in a `Cell`. The answers and the
    // refusals are the cell walk's, to the bit — a differential law in TotalityTests holds the
    // pre-421 cell walk, kept there as the oracle, to it.

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

    /// A decimal column's present rows as `aggregate` admits them (Phase 299), walked in row order:
    /// each text CANONICALISED and handed to `step` with its row, so `1.50` and `1.5` are one value
    /// everywhere below; the first text that is not decimal text stops the walk as the named
    /// `CellOutsideType` it answers. This is the one admission an aggregate makes — a cell of every
    /// other column is of its column's type by construction (Phase 417) — and since it covers the
    /// whole column before any answer is given, it outranks every other refusal. The canonical texts
    /// are not kept: a column-long array of them would hold every fresh text alive at once.
    let private walkDecimals
        (col: Column)
        (xs: Vector<string>)
        (v: Validity)
        (step: int -> string -> unit)
        : AggregateError option =
        let rows = ColumnStorage.presentRows xs.Length v
        let items = xs.Items
        let offset = xs.Offset
        let mutable outside = None
        let mutable i = 0

        while outside.IsNone && i < rows.Count do
            if ColumnStorage.isSet rows i then
                let text = items[offset + i]

                match DecimalText.tryCanonical text with
                | Some c -> step i c
                | None ->
                    outside <- Some(CellOutsideType(col.Name, col.Type, "decimal text '" + text + "' (not decimal)"))

            i <- i + 1

        outside

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

    /// The number of present rows (`Count`), counted within the column's own length.
    let private presentRowCount (n: int) (v: Validity) : int =
        match v with
        | AllValid -> n
        | Mask _ ->
            let rows = ColumnStorage.presentRows n v
            let mutable count = 0

            for i in 0 .. rows.Count - 1 do
                if ColumnStorage.isSet rows i then
                    count <- count + 1

            count

    /// `Sum` over an int column: exact, then range-checked against int32 (Phase 39 no-silent-wrap),
    /// so the result is host-deterministic; `Null` over no present row. The values are added as
    /// floats in runs of at most 2^22, each run's total moved into an `int64` at its end: every
    /// partial total of a run is an integer of magnitude at most 2^22 * 2^31 = 2^53, which a float
    /// holds exactly, so the answer is the `int64` sum's — without an `int64` add per row, which
    /// Fable compiles to a `BigInt`.
    let private intSum (xs: Vector<int>) (v: Validity) : Result<Cell, AggregateError> =
        let rows = ColumnStorage.presentRows xs.Length v
        let items = xs.Items
        let offset = xs.Offset
        let mutable total = 0L
        let mutable run = 0.0
        let mutable inRun = 0
        let mutable seen = false

        for i in 0 .. rows.Count - 1 do
            if ColumnStorage.isSet rows i then
                run <- run + float items[offset + i]
                inRun <- inRun + 1

                if inRun = 4194304 then
                    total <- total + int64 run
                    run <- 0.0
                    inRun <- 0

                seen <- true

        if seen then checkedSumInt (total + int64 run) else Ok Null

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

    let inline private addNumber (acc: FloatAcc) (keeps: bool) (f: float) : unit =
        acc.Total <- acc.Total + f
        acc.Numbers <- acc.Numbers + 1

        if not (isFiniteFloat f) then
            acc.AllFinite <- false

        if keeps then
            acc.Kept.Add f

    let private emptyAcc () : FloatAcc =
        { Total = 0.0
          Numbers = 0
          AllFinite = true
          PastFloat = None
          Kept = ResizeArray<float>() }

    /// The numbers of an int or a float column folded into a fresh `FloatAcc`, in row order: an
    /// int as its float, a float as itself. A decimal column's numbers are folded as it is admitted
    /// (`aggregateDecimals`); the other columns hold no number.
    let private foldNumbers (col: Column) (keeps: bool) : FloatAcc =
        let acc = emptyAcc ()
        let rows = ColumnStorage.presentRows (length col) (validity col)

        match col.Data with
        | Ints(xs, _) ->
            let items = xs.Items
            let offset = xs.Offset

            for i in 0 .. rows.Count - 1 do
                if ColumnStorage.isSet rows i then
                    addNumber acc keeps (float items[offset + i])
        | Floats(xs, _) ->
            let items = xs.Items
            let offset = xs.Offset

            for i in 0 .. rows.Count - 1 do
                if ColumnStorage.isSet rows i then
                    addNumber acc keeps items[offset + i]
        | Decimals _
        | Bools _
        | Strs _
        | Dates _
        | Timestamps _ -> ()

        acc

    /// A float answer over finite input: itself where it is finite, and a named overflow where even
    /// the form that cannot overflow an intermediate left the range.
    let private finiteOr (col: Column) (what: string) (f: float) : Result<Cell, AggregateError> =
        if isFiniteFloat f then
            Ok(Float f)
        else
            Error(AggregateOverflow(col.Name + ": " + what + " overflowed the float range"))

    /// `Sum` over a float column: the running total, which has no second form — a total that leaves
    /// the range over finite input is a named overflow.
    let private floatSum (col: Column) (acc: FloatAcc) : Result<Cell, AggregateError> =
        if acc.Numbers = 0 then Ok Null
        elif acc.AllFinite then finiteOr col "sum" acc.Total
        else Ok(Float acc.Total)

    /// `Mean`: the plain formula, recomputed by `scaledMoments` where it overflowed over finite input.
    /// That recomputation folds the column's numbers again (`refold`), keeping them — the one case
    /// that kept none.
    let private floatMean (col: Column) (refold: unit -> FloatAcc) (acc: FloatAcc) : Result<Cell, AggregateError> =
        if acc.Numbers = 0 then
            Ok Null
        else
            let mean = acc.Total / float acc.Numbers

            if isFiniteFloat mean || not acc.AllFinite then
                Ok(Float mean)
            else
                finiteOr col "mean" (fst (scaledMoments (refold ()).Kept))

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

    /// `Median` over the kept numbers in the float order — a STABLE sort, so of two numbers the order
    /// calls equal (`-0` and `0`) the one met first stays first; an even count halves before it adds
    /// where the plain mid-point overflowed over finite input.
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

    /// `Min` / `Max` (Phase 299): the least / greatest present value by `Cell.compare`'s order — an
    /// incomparable pair keeping the incumbent, `Min` keeping the FIRST of equals and `Max` taking the
    /// LAST, which is what tells `-0` from `0` — and `Null` over no present row. An int, float or
    /// string column is compared as its values. A bool, date, timestamp or decimal column is compared
    /// as the cells it renders, whose order (chronological for an instant, Phase 422; exact for a
    /// decimal, whatever its text) is `Cell.compare`'s own, and a decimal answer is canonicalised.
    let private extreme (isMin: bool) (col: Column) : Cell =
        let rows = ColumnStorage.presentRows (length col) (validity col)
        let mutable best = -1

        // Does a present row replace the incumbent, whose order against it is `c`?
        let inline replaces (c: int) = if isMin then c > 0 else c <= 0

        match col.Data with
        | Ints(xs, _) ->
            let items = xs.Items
            let offset = xs.Offset

            for i in 0 .. rows.Count - 1 do
                if
                    ColumnStorage.isSet rows i
                    && (best < 0 || replaces (compare items[offset + best] items[offset + i]))
                then
                    best <- i

            if best < 0 then Null else Int items[offset + best]
        | Floats(xs, _) ->
            let items = xs.Items
            let offset = xs.Offset

            for i in 0 .. rows.Count - 1 do
                if
                    ColumnStorage.isSet rows i
                    && (best < 0 || replaces (Cell.compareFloat items[offset + best] items[offset + i]))
                then
                    best <- i

            if best < 0 then Null else Float items[offset + best]
        | Strs(xs, _) ->
            let items = xs.Items
            let offset = xs.Offset

            for i in 0 .. rows.Count - 1 do
                if
                    ColumnStorage.isSet rows i
                    && (best < 0
                        || replaces (System.String.CompareOrdinal(items[offset + best], items[offset + i])))
                then
                    best <- i

            if best < 0 then Null else Str items[offset + best]
        | Bools _
        | Dates _
        | Timestamps _
        | Decimals _ ->
            let mutable found = Null

            for i in 0 .. rows.Count - 1 do
                if ColumnStorage.isSet rows i then
                    let c = cell i col

                    if best < 0 then
                        found <- c
                        best <- i
                    else
                        match Cell.compare found c with
                        | Some k when replaces k -> found <- c
                        | _ -> ()

            match found with
            | Decimal s -> Decimal(DecimalText.tryCanonical s |> Option.defaultValue s)
            | other -> other

    /// `CountDistinct` (Phase 101): the distinct present values under `Cell.token`'s identity — an
    /// int and a string as themselves; a float with every NaN one value and `-0` one with `0` (the
    /// two floats `token` merges, and the only two); a bool, date, timestamp or decimal column by the
    /// token of the cell it renders, which reads a decimal canonically.
    let private distinctCount (col: Column) : int =
        let rows = ColumnStorage.presentRows (length col) (validity col)

        match col.Data with
        | Ints(xs, _) ->
            let seen = System.Collections.Generic.HashSet<int>()
            let items = xs.Items
            let offset = xs.Offset

            for i in 0 .. rows.Count - 1 do
                if ColumnStorage.isSet rows i then
                    seen.Add items[offset + i] |> ignore

            seen.Count
        | Floats(xs, _) ->
            let seen = System.Collections.Generic.HashSet<float>()
            let mutable nan = 0
            let items = xs.Items
            let offset = xs.Offset

            for i in 0 .. rows.Count - 1 do
                if ColumnStorage.isSet rows i then
                    let f = items[offset + i]

                    if System.Double.IsNaN f then nan <- 1
                    elif f = 0.0 then seen.Add 0.0 |> ignore
                    else seen.Add f |> ignore

            seen.Count + nan
        | Strs(xs, _) ->
            let seen = System.Collections.Generic.HashSet<string>()
            let items = xs.Items
            let offset = xs.Offset

            for i in 0 .. rows.Count - 1 do
                if ColumnStorage.isSet rows i then
                    let s = items[offset + i]
                    // A null string has no text of its own: it is keyed as `token` spells it after the
                    // tag, which is the host's own spelling of a null concatenated.
                    seen.Add(if isNull s then (Cell.token (Str s)).Substring 2 else s) |> ignore

            seen.Count
        | Bools _
        | Dates _
        | Timestamps _
        | Decimals _ ->
            let seen = System.Collections.Generic.HashSet<string>()

            for i in 0 .. rows.Count - 1 do
                if ColumnStorage.isSet rows i then
                    seen.Add(Cell.token (cell i col)) |> ignore

            seen.Count

    /// A float-valued aggregate's answer from its fold: the refusal of the first decimal past the
    /// float range, where one was met, and otherwise `Sum`, `Mean`, `StdDev` or `Median` of what was
    /// folded; `refold` folds the column again, keeping its numbers, for `Mean`'s second form.
    let private floatAnswer
        (col: Column)
        (fn: AggFn)
        (refold: unit -> FloatAcc)
        (acc: FloatAcc)
        : Result<Cell, AggregateError> =
        match acc.PastFloat, fn with
        | Some e, _ -> Error e
        | None, Sum -> floatSum col acc
        | None, Mean -> floatMean col refold acc
        | None, StdDev -> floatStdDev col acc
        | None, _ -> floatMedian col acc

    /// Does `fn` keep the numbers it folds? `StdDev` and `Median` do: a second moment and a sort need
    /// them.
    let private keepsNumbers (fn: AggFn) : bool =
        match fn with
        | StdDev
        | Median -> true
        | Sum
        | Mean
        | Min
        | Max
        | Count
        | First
        | Last
        | CountDistinct -> false

    /// Every aggregate over a decimal column: the admission walk (`walkDecimals`), whose refusal
    /// outranks every answer, folding `Count`, `First`, `Last`, the exact `Sum` and the float-valued
    /// aggregates as it goes; `Min`, `Max` and `CountDistinct` read the column once it is admitted.
    let private aggregateDecimals
        (fn: AggFn)
        (col: Column)
        (xs: Vector<string>)
        (v: Validity)
        : Result<Cell, AggregateError> =
        let walk = walkDecimals col xs v

        let answer (refusal: AggregateError option) (k: unit -> Result<Cell, AggregateError>) =
            match refusal with
            | Some e -> Error e
            | None -> k ()

        // The float fold over the admitted texts: each its nearest float, and the first one past the
        // float range recorded as the refusal `fn` (a float-valued aggregate) owes it, after which
        // nothing more is folded — while the walk goes on, since a later text that is not decimal
        // outranks it.
        let foldDecimals (keeps: bool) : AggregateError option * FloatAcc =
            let acc = emptyAcc ()

            let refusal =
                walk (fun _ text ->
                    if acc.PastFloat.IsNone then
                        match DecimalText.tryToFloat text with
                        | Some f -> addNumber acc keeps f
                        | None ->
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
                                ))

            refusal, acc

        match fn with
        | Count ->
            let count = ref 0
            answer (walk (fun _ _ -> count.Value <- count.Value + 1)) (fun () -> Ok(Int count.Value))
        | First
        | Last ->
            // The first or the last ROW, a `Null` included.
            let row =
                match fn with
                | First -> 0
                | _ -> xs.Length - 1

            let edge = ref Null

            answer
                (walk (fun i text ->
                    if i = row then
                        edge.Value <- Decimal text))
                (fun () -> Ok edge.Value)
        | Min -> answer (walk (fun _ _ -> ())) (fun () -> Ok(extreme true col))
        | Max -> answer (walk (fun _ _ -> ())) (fun () -> Ok(extreme false col))
        | CountDistinct -> answer (walk (fun _ _ -> ())) (fun () -> Ok(Int(distinctCount col)))
        | Sum ->
            // EXACT (`0.33.0`) — never through `float`, so nothing is rounded and nothing overflows —
            // and a `Decimal`; `Null` over no present row.
            let total: string option ref = ref None

            let add _ text =
                total.Value <-
                    match total.Value with
                    | None -> Some text
                    | Some t -> Some(DecimalText.add t text |> Option.defaultValue t)

            answer (walk add) (fun () -> Ok(total.Value |> Option.map Decimal |> Option.defaultValue Null))
        | Mean
        | StdDev
        | Median ->
            let refusal, acc = foldDecimals (keepsNumbers fn)
            answer refusal (fun () -> floatAnswer col fn (fun () -> snd (foldDecimals true)) acc)

    /// Compute one aggregate over a column with the pinned null/coercion/float semantics (Phase 36) —
    /// the public surface the compute layer's `GroupBy`/`Pivot` *call* (the single source of truth, not
    /// a second copy). Null/NA is skipped; a numeric aggregate (`Sum`/`Mean`/`Median`/`StdDev`) over a
    /// non-numeric column is a named `IncompatibleAggType`; an integer `Sum` overflow is a named
    /// `AggregateOverflow` (Phase 39 no-silent-wrap). `Min`/`Max` order any same-family present cells;
    /// `First`/`Last` keep the first/last cell (a `Null` included). Int sums are exact, then range-
    /// checked, so the result is host-deterministic. `Float` sums fold left to right from zero.
    /// `CountDistinct` (Phase 101) counts distinct PRESENT values by the canonical `Distinct` token, so
    /// it never depends on a host's float equality.
    ///
    /// A `DecimalType` column is numeric (`0.33.0`). Its `Sum` is EXACT and is a `Decimal`; its
    /// `Min`/`Max` compare exactly; its `Mean`/`Median`/`StdDev` are `float`, as `aggType` declares
    /// for every source type, and are the nearest float to each value — a value past the float
    /// range is a named `AggregateOverflow`, never an infinity.
    ///
    /// A DECIMAL COLUMN IS ADMITTED FIRST (Phase 299): a present `Decimal` whose text is not decimal
    /// text is a named `CellOutsideType` — never truncated, dropped or counted by shape — and every
    /// one is read canonicalised, so `1.50` and `1.5` are one value everywhere below. No other
    /// column can hold a cell outside its type (Phase 417). Numbers ORDER through the column layer's
    /// float order: NaN is one value and sorts last, `-0` equals `0`, so `Min`, `Max` and `Median`
    /// over a column holding a NaN answer the same on every host (`Max` is NaN, `Min` is not, and
    /// `Median` counts NaN at the top).
    ///
    /// A LOOP OVER THE TYPED VECTOR (Phase 421). Each aggregate walks the column's vector once,
    /// reading presence off the `Validity` and folding the values as themselves, in row order, into
    /// the one accumulator it needs — no `Cell` per row (`Sum` over 20,000 cells cost some twenty
    /// times a direct loop when it read three intermediate lists, Phase 306, and still built a cell
    /// per row until this phase). The fold order is the cell walk's, so every answer is the same
    /// value to the bit. The refusals keep their precedence: a decimal text that is not decimal
    /// anywhere in the column first, then a non-numeric column, then the first decimal past the
    /// float range. `Median` and `StdDev` keep the column's numbers, because a sort and a second
    /// moment need them.
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
        // The aggregate and the column are read by PATTERN, never by `=` (Phase 353): a union's `=`
        // is a structural-equality call under Fable, and a pivot calls this once per (group,
        // on-value) pair.
        let isNumeric =
            match col.Data with
            | Ints _
            | Floats _
            | Decimals _ -> true
            | Bools _
            | Strs _
            | Dates _
            | Timestamps _ -> false

        match col.Data, fn with
        | Decimals(xs, v), _ -> aggregateDecimals fn col xs v
        | _, Count -> Ok(Int(presentRowCount (length col) (validity col)))
        | _, First -> Ok(cell 0 col)
        | _, Last -> Ok(cell (length col - 1) col)
        | _, CountDistinct -> Ok(Int(distinctCount col))
        | _, Min -> Ok(extreme true col)
        | _, Max -> Ok(extreme false col)
        | _, (Sum | Mean | StdDev | Median) when not isNumeric ->
            Error(IncompatibleAggType(fn, col.Type, [ IntType; FloatType; DecimalType ]))
        | Ints(xs, v), Sum -> intSum xs v
        | _, (Sum | Mean | StdDev | Median) ->
            floatAnswer col fn (fun () -> foldNumbers col true) (foldNumbers col (keepsNumbers fn))
