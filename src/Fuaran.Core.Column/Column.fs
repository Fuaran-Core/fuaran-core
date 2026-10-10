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
//  row holds one (`Mask`). `Cell` stays the scalar
//  read type; `Column.ofCells` / `Column.toCells` are the migration bridge.
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
/// `Floats` holds floats only and `Decimals` decimal text only. `Dates` and `Timestamps` hold their
/// canonical ISO-8601 text in this phase, as the cells do; Phase 422 makes them integers.
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
    /// A `date` column's canonical `YYYY-MM-DD` texts and mask.
    | Dates of values: Vector<string> * validity: Validity
    /// A `timestamp` column's canonical `YYYY-MM-DDThh:mm:ssZ` texts and mask.
    | Timestamps of values: Vector<string> * validity: Validity
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
        | Timestamps _ -> TimestampType
        | Decimals _ -> DecimalType

    /// The validity, whatever the case.
    member this.Validity: Validity =
        match this with
        | Ints(_, v)
        | Floats(_, v)
        | Bools(_, v)
        | Strs(_, v)
        | Dates(_, v)
        | Timestamps(_, v)
        | Decimals(_, v) -> v

    /// The number of rows — the values vector's length.
    member this.Length: int =
        match this with
        | Ints(xs, _) -> xs.Length
        | Floats(xs, _) -> xs.Length
        | Bools(xs, _) -> xs.Length
        | Strs(xs, _)
        | Dates(xs, _)
        | Timestamps(xs, _)
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
            | Timestamps(xs, vx), Timestamps(ys, vy) -> ColumnStorage.presentEqual xs vx ys vy
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
            | Strs(xs, v)
            | Dates(xs, v)
            | Timestamps(xs, v)
            | Decimals(xs, v) -> ColumnStorage.presentHash xs v

        (tag * 397) ^^^ body

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
    /// reads it.
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
            | Dates(xs, _) -> Date xs[i]
            | Timestamps(xs, _) -> Timestamp xs[i]
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

    /// A `date` column over canonical `YYYY-MM-DD` texts, present where `validity` says.
    let ofDates (name: string) (values: Vector<string>) (validity: Validity) : Column =
        { Name = name
          Data = Dates(values, validity) }

    /// A `timestamp` column over canonical `YYYY-MM-DDThh:mm:ssZ` texts, present where `validity` says.
    let ofTimestamps (name: string) (values: Vector<string>) (validity: Validity) : Column =
        { Name = name
          Data = Timestamps(values, validity) }

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

    /// The texts of a `date` column, or `None` for a column of another type.
    let tryDates (c: Column) : Vector<string> option =
        match c.Data with
        | Dates(xs, _) -> Some xs
        | _ -> None

    /// The texts of a `timestamp` column, or `None` for a column of another type.
    let tryTimestamps (c: Column) : Vector<string> option =
        match c.Data with
        | Timestamps(xs, _) -> Some xs
        | _ -> None

    /// The texts of a `decimal` column, or `None` for a column of another type.
    let tryDecimals (c: Column) : Vector<string> option =
        match c.Data with
        | Decimals(xs, _) -> Some xs
        | _ -> None

    // ---- the cell-list bridge (Phase 417) ----

    /// The typed storage of `cells` for a column of type `ty`, or the first present cell whose type
    /// does not widen into `ty` (`ColumnType.widens`) as the `TypeMismatch` naming it, in row order.
    /// A widened cell is normalised (an `Int` in a float column to its float, in a decimal column to
    /// its digits); the type's zero is written at every absent row, and a list with no `Null` is
    /// `AllValid` (Phase 420). Everything else a cell can carry
    /// is held as found — a non-finite float, decimal, date or timestamp text that is not canonical —
    /// and is `Table.validate`'s to refuse at the codec, as before.
    let private storageOfCells (name: string) (ty: ColumnType) (cells: Cell list) : Result<ColumnData, ColumnError> =
        let n = List.length cells
        let mask = Array.zeroCreate<bool> n

        /// Fill `out` from the cells through `pick`, which reads the typed value of a present cell
        /// that fits, or names the type that does not.
        let fill (out: 'T[]) (pick: Cell -> Result<'T option, ColumnType>) =
            let mutable fault = None
            let mutable i = 0
            let mutable rest = cells

            while fault.IsNone && not rest.IsEmpty do
                match pick rest.Head with
                | Ok(Some v) ->
                    out[i] <- v
                    mask[i] <- true
                | Ok None -> ()
                | Error got -> fault <- Some(TypeMismatch(name, ty, ColumnType.tag got))

                i <- i + 1
                rest <- rest.Tail

            match fault with
            | Some e -> Error e
            | None -> Ok(Vector.adopt out, Validity.ofVector (Vector.adopt mask))

        let outside (c: Cell) : Result<'T option, ColumnType> =
            match Cell.typeOf c with
            | Some t -> Error t
            | None -> Ok None

        match ty with
        | IntType ->
            fill (Array.zeroCreate n) (fun c ->
                match c with
                | Int i -> Ok(Some i)
                | other -> outside other)
            |> Result.map Ints
        | FloatType ->
            fill (Array.zeroCreate n) (fun c ->
                match c with
                | Float f -> Ok(Some f)
                | Int i -> Ok(Some(float i))
                | other -> outside other)
            |> Result.map Floats
        | BoolType ->
            fill (Array.zeroCreate n) (fun c ->
                match c with
                | Bool b -> Ok(Some b)
                | other -> outside other)
            |> Result.map Bools
        | StringType ->
            fill (Array.create n "") (fun c ->
                match c with
                | Str s -> Ok(Some s)
                | other -> outside other)
            |> Result.map Strs
        | DateType ->
            fill (Array.create n "") (fun c ->
                match c with
                | Date s -> Ok(Some s)
                | other -> outside other)
            |> Result.map Dates
        | TimestampType ->
            fill (Array.create n "") (fun c ->
                match c with
                | Timestamp s -> Ok(Some s)
                | other -> outside other)
            |> Result.map Timestamps
        | DecimalType ->
            fill (Array.create n DecimalText.zero) (fun c ->
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

    /// A present cell as `aggregate` admits it (Phase 299): a `Decimal` cell passes CANONICALISED,
    /// and one whose text is not decimal text is a named `CellOutsideType`. Every other cell passes:
    /// since Phase 417 a cell read from a column is of its column's type by construction, so the
    /// type check this once made is the storage's, and `Null` is type-agnostic.
    let private admit (col: Column) (c: Cell) : Result<Cell, AggregateError> =
        match c with
        | Decimal s ->
            match DecimalText.tryCanonical s with
            | Some canonical -> Ok(Decimal canonical)
            | None -> Error(CellOutsideType(col.Name, col.Type, "decimal text '" + s + "' (not decimal)"))
        | _ -> Ok c

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
        let n = length col
        let mutable i = 0

        while outside.IsNone && i < n do
            match admit col (cell i col) with
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

            i <- i + 1

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

                for c in toCells col do
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
