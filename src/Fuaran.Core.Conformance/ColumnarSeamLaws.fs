namespace Fuaran.Core

/// The columnar layer's seam families: the aggregate null-skip (Phase 36) and the columnar validator
/// (Phase 37) — `SeamLaws` until the Phase 388 split along its banners — then the typed column
/// (Phase 417) and its ownership contract (Phase 418).
module internal ColumnarSeamLaws =
    // ---- aggregate null-skip (Phase 36; split by Phase 257) ----
    // The `Column.aggregate` half of what was `aggregateParityLaws`. The parity half compares the
    // aggregate against a single-group `GroupBy`, which is the dataframe layer's, so it ships from
    // `Fuaran.Core.DataFrame.Conformance` under the old name (D68), produced by the compute
    // repository since Phase 258 (D66). What stays here reads `Column`
    // alone: the pinned NA-skip semantics every consumer of the aggregate relies on.

    /// The aggregate null-skip laws (Phase 36's second law, a family of its own since Phase 257).
    /// Self-contained — over a seed-replayable sample of random (int/float/decimal, null-bearing) columns
    /// it certifies the pinned NA-skip semantics of `Column.aggregate`: `Count` equals the
    /// present-cell count, and `Sum` over a null-bearing column equals `Sum` over its present-only
    /// projection.
    let aggregateNullSkipLaws (seed: int) (iterations: int) : LawResult list =
        let nullSkip =
            LawKit.LawCell "Column.aggregate skips Null cells (Count = present count; Sum ignores nulls)"
        // Phase 276 — the present cells the laws were reached by, per numeric column type. A
        // column type the generator never draws is a `Sum` and a `Count` that were never tested
        // over it, and the exact decimal `Sum` is the one fold that shares no code with the others.
        let mutable intCells = 0
        let mutable floatCells = 0
        let mutable decimalCells = 0

        LawKit.run iterations seed (fun rng _ at ->
            // Phase 276 — the column type is drawn from all three numeric types; until then a
            // decimal column was never drawn, and the guard below says so for any run that misses one.
            let kind = rng.IntBelow 3
            let nRows = rng.IntBelow 6

            let ty =
                match kind with
                | 0 -> IntType
                | 1 -> FloatType
                | _ -> DecimalType

            let cells =
                [ for _ in 0..nRows ->
                      let k = rng.IntBelow 4

                      if k = 0 then
                          Null
                      else
                          let v = rng.IntBelow 200

                          match ty with
                          | IntType -> Int(v - 100)
                          | FloatType -> Float(float (v - 100) * 0.5)
                          | _ ->
                              // Two fraction digits, through the canonicalising constructor: `3.10`
                              // is held as `3.1`, `3.00` as `3`, as every boundary holds a decimal.
                              let frac = rng.IntBelow 100

                              let text = string (v - 100) + "." + (if frac < 10 then "0" else "") + string frac

                              Cell.decimal text |> Option.defaultValue Null ]

            let present = cells |> List.filter (fun c -> not (Cell.isNull c))

            for c in present do
                match c with
                | Int _ -> intCells <- intCells + 1
                | Float _ -> floatCells <- floatCells + 1
                | Decimal _ -> decimalCells <- decimalCells + 1
                | _ -> ()

            // Every drawn cell is of its column's type, so both builds succeed; a refusal here is a
            // defect in the kit's own draw, and the law says so rather than skipping the iteration.
            match Column.ofCells "c" ty cells, Column.ofCells "c" ty present with
            | Ok col, Ok presentCol ->
                (match Column.aggregate Count col with
                 | Ok(Int n) when n = List.length present -> nullSkip.Saw()
                 | other -> nullSkip.Check(false, fun () -> at (sprintf "Count ≠ present count (%A)" other)))

                match Column.aggregate Sum col, Column.aggregate Sum presentCol with
                | Ok a, Ok b when a = b -> nullSkip.Saw()
                | a, b -> nullSkip.Check(false, fun () -> at (sprintf "Sum not null-skipping (%A vs %A)" a b))
            | a, b ->
                nullSkip.Check(false, fun () -> at (sprintf "the drawn cells did not build a column (%A / %A)" a b)))

        LawKit.results [ nullSkip ]
        @ [ SampleAdequacy.reached
                "Conformance.aggregateNullSkipLaws"
                "column type"
                seed
                [ "int cell", intCells; "float cell", floatCells; "decimal cell", decimalCells ] ]

    // ---- columnar validator (Phase 37) ----
    // The teeth on the `ColumnValidator` surface: stock rules over a `Table` emit located, severity-
    // tagged defects through the EXISTING defect/severity model, and the output is deterministic +
    // byte-canonical for a given table (`canonicalCodes`).

    /// The columnar-validator laws (Phase 37). Self-contained — over a seed-replayable sample of random
    /// `(a:int|decimal, s:string)` tables with injected faults (nulls + out-of-range values) it certifies:
    /// **determinism** (`validate` and its `canonicalCodes` projection are identical on a re-run of the
    /// same table); and **soundness** (the count of `COL-NOTNULL` defects equals the number of null cells
    /// in the non-null column, and `COL-INRANGE` equals the number of out-of-range cells).
    let columnarValidatorLaws (seed: int) (iterations: int) : LawResult list =
        let determinism =
            LawKit.LawCell "columnar validate is deterministic + byte-canonical (same table ⇒ same defects)"

        let soundness =
            LawKit.LawCell "columnar stock rules are sound (defect counts = injected faults)"
        // Phase 223 — the fault populations the soundness law counts. A fault-free sample
        // satisfies `defects = injected faults` as 0 = 0 and certifies nothing about either rule.
        let mutable nullsInjected = 0
        let mutable outOfRangeInjected = 0
        // Phase 276 — the present cells of the ranged column, per type. `inRange` reads an `Int`
        // and a `Decimal` by different branches, and a sample of one type certifies one of them.
        let mutable intCells = 0
        let mutable decimalCells = 0

        let reg =
            // four distinct rules: `ofRules` refuses only a repeated id (Phase 298), so this is `Ok`
            match
                ColumnValidator.ofRules
                    [ ColumnValidator.notNull "a"
                      ColumnValidator.inRange "a" 0.0 100.0
                      ColumnValidator.ofType "s" StringType
                      ColumnValidator.unique [ "a" ] ]
            with
            | Ok r -> r
            | Error e -> invalidOp (sprintf "the kit's column registry repeats a rule id: %A" e)

        LawKit.run iterations seed (fun rng i at ->
            let nRows = rng.IntBelow 6

            // column a: int straying out of [0,100], with ~1/5 nulls
            let drawn =
                [ for _ in 0..nRows ->
                      let k = rng.IntBelow 5

                      if k = 0 then
                          Null
                      else
                          let v = rng.IntBelow 160
                          Int(v - 30) ]

            // Phase 223 — the roll is STRATIFIED by iteration index, so every run of three or more
            // iterations reaches both faults the soundness law counts, by construction rather than
            // by the draw: stratum 0 is a clean table (the drawn values folded into range, nulls
            // dropped), stratum 1 carries the draw plus one null, stratum 2 the draw plus one
            // out-of-range value. A shorter run can still miss them, and the guard below says so.
            //
            // Phase 276 — and by PARITY, the column's type: an odd iteration carries `a` as a decimal
            // column, each drawn int `n` read as `n.25`, so two iterations reach both of `inRange`'s
            // branches with no extra draw (an even iteration's table is the one it always was). The
            // decimal stratum 0 folds into `0..99` so `n.25` stays in range, and stratum 2's injected
            // value is `100.01` — past the bound by less than any whole step.
            let decimalColumn = i % 2 = 1

            let cellOf (n: int) : Cell =
                if decimalColumn then
                    Cell.decimal (string n + ".25") |> Option.defaultValue Null
                else
                    Int n

            let typed =
                drawn
                |> List.map (fun c ->
                    match c with
                    | Int v -> cellOf v
                    | other -> other)

            let aCells =
                match i % 3 with
                | 0 ->
                    drawn
                    |> List.choose (fun c ->
                        match c with
                        | Int v when decimalColumn -> Some(cellOf (((v % 100) + 100) % 100))
                        | Int v -> Some(Int(((v % 101) + 101) % 101))
                        | _ -> None)
                | 1 -> typed @ [ Null ]
                | _ -> typed @ [ (if decimalColumn then Decimal "100.01" else Int 150) ]

            let sCells = aCells |> List.map (fun _ -> Str "x")
            let aType = if decimalColumn then DecimalType else IntType

            // Every drawn cell is of its column's type, so each build succeeds; a refusal is a defect
            // in the kit's own draw, named through the determinism law rather than skipped.
            let built (name: string) (ty: ColumnType) (cells: Cell list) : Column =
                match Column.ofCells name ty cells with
                | Ok col -> col
                | Error e ->
                    determinism.Check(
                        false,
                        fun () -> at (sprintf "the kit's own draw did not build column %s (%A)" name e)
                    )

                    Column.ofStrs name Vector.empty AllValid

            let t: Table =
                { Schema = [ Field.create "a" aType; Field.create "s" StringType ]
                  Columns = [ built "a" aType aCells; built "s" StringType sCells ] }

            let defects = ColumnValidator.validate reg t

            determinism.Check(
                ColumnValidator.validate reg t = defects
                && Validator.canonicalCodes (ColumnValidator.validate reg t) = Validator.canonicalCodes defects,
                fun () -> at "columnar validate is not deterministic"
            )

            let nullCount = aCells |> List.filter Cell.isNull |> List.length

            let notNullDefects =
                defects |> List.filter (fun d -> d.Code = "COL-NOTNULL") |> List.length

            let outOfRange =
                aCells
                |> List.filter (fun c ->
                    match c with
                    | Int v -> v < 0 || v > 100
                    // Counted EXACTLY, by the digits — the oracle the rule's float reading is held to.
                    | Decimal s -> DecimalText.compare s "0" = Some -1 || DecimalText.compare s "100" = Some 1
                    | _ -> false)
                |> List.length

            let inRangeDefects =
                defects |> List.filter (fun d -> d.Code = "COL-INRANGE") |> List.length

            nullsInjected <- nullsInjected + nullCount
            outOfRangeInjected <- outOfRangeInjected + outOfRange

            for c in aCells do
                match c with
                | Int _ -> intCells <- intCells + 1
                | Decimal _ -> decimalCells <- decimalCells + 1
                | _ -> ()

            soundness.Check(
                (notNullDefects = nullCount && inRangeDefects = outOfRange),
                fun () ->
                    at (
                        sprintf
                            "defect counts ≠ injected faults (notNull %d/%d, inRange %d/%d)"
                            notNullDefects
                            nullCount
                            inRangeDefects
                            outOfRange
                    )
            ))

        LawKit.results [ determinism; soundness ]
        @ [
            // Phase 223 — `Guarded ["null cell"; "out-of-range cell"]`, after the subject laws. The
            // stratified roll reaches both at three iterations; a shorter run reports the guard.
            SampleAdequacy.reached
                "Conformance.columnarValidatorLaws"
                "injected null"
                seed
                [ "null cell", nullsInjected ]
            SampleAdequacy.reached
                "Conformance.columnarValidatorLaws"
                "injected out-of-range value"
                seed
                [ "out-of-range cell", outOfRangeInjected ]
            SampleAdequacy.reached
                "Conformance.columnarValidatorLaws"
                "column type"
                seed
                [ "int cell", intCells; "decimal cell", decimalCells ] ]

    // ---- the typed column (Phase 417) ----
    // The column holds one typed `Vector` per column behind `ColumnData`, and three things about
    // that representation are a consumer's to rely on across hosts: equality is the cells' equality,
    // the cell-list bridge and the typed builders build one column, and every read over a vector
    // agrees with its indexer. The kit is compiled on both pipelines, so a law here is certified
    // under Fable as on .NET — which is where array equality and NaN diverge between hosts.

    /// The cell-level reading `=` on columns is held to: one length, one MATERIALISED validity mask
    /// (so `AllValid` and an all-true `Mask` of the column's length read alike, Phase 420), and at
    /// every present row `Cell.compare` answers `Some 0`.
    let private cellsEqual (a: Column) (b: Column) : bool =
        Column.length a = Column.length b
        && Column.mask a = Column.mask b
        && (let mutable same = true
            let mutable i = 0

            while same && i < Column.length a do
                if Column.isPresent i a then
                    same <- Cell.compare (Column.cell i a) (Column.cell i b) = Some 0

                i <- i + 1

            same)

    /// The typed-column laws (Phase 417). Over a seed-replayable sample of column pairs of one type
    /// — int, float, bool, string and canonical decimal, with nulls, the drawn pair identical, moved
    /// at one row, or differing only in the element UNDER an absent row — it certifies three laws:
    ///
    /// 1. **Equality is cell equality.** `a = b` exactly when `cellsEqual a b`: one length, one
    ///    validity mask, and `Cell.compare` answering `Some 0` at every present row — so a float
    ///    column holding a NaN equals another holding a NaN there, `-0.0` equals `0.0`, the element
    ///    under an absent row takes no part, and equal columns hash equal. Three pairs are built every
    ///    iteration whatever the draw: a NaN pair, a signed-zero pair, and a pair that differs only
    ///    under an absent row. And per drawn type (Phase 420), the drawn values under `AllValid` and
    ///    under a hand-built all-true `Mask` of their length are ONE column, hashing alike, while an
    ///    all-true `Mask` one row longer is not. The law is stated over columns in the storage's contract (canonical
    ///    decimal text): a non-canonical decimal text is a column `Table.validate` refuses, and its
    ///    equality is by text.
    /// 2. **The bridge and the typed builders build one column.** `Column.ofCells` over the drawn
    ///    cells equals the typed builder over the drawn vector and mask, `toCells` reads the drawn
    ///    cells back, and the typed reader hands back the vector the builder was given. The
    ///    normalising constructors answer `AllValid` exactly when no row is absent, and the
    ///    materialised mask (`Column.mask`) is the drawn one.
    /// 3. **Every vector read agrees with the indexer.** `toList`, `toArray`, `fold`, `iter`, `iteri`,
    ///    `map`, `mapi`, `exists`, `tryFindIndex`, `tryItem`, a `slice` and a borrowed array all answer
    ///    what indexing answers, over the drawn vector — and over a drawn bool vector (Phase 431),
    ///    which under Fable is packed a byte a row: every read answers a boolean, `=` against `true`
    ///    included, and its borrow reads as the same booleans by truthiness.
    let columnVectorLaws (seed: int) (iterations: int) : LawResult list =
        let equality =
            LawKit.LawCell "column equality is cell equality under Cell.compare, and equal columns hash equal"

        let bridge =
            LawKit.LawCell "Column.ofCells and the typed builders build one column; toCells reads it back"

        let reads = LawKit.LawCell "every Vector read agrees with the indexer"

        LawKit.run iterations seed (fun rng _ at ->
            let n = 1 + rng.IntBelow 6
            let maskA = Array.init n (fun _ -> rng.IntBelow 4 <> 0)

            let maskB =
                if rng.IntBelow 3 = 0 then
                    Array.init n (fun _ -> rng.IntBelow 4 <> 0)
                else
                    Array.copy maskA

            // 0: the same values; 1: one value moved (at a present or an absent row, as the draw
            // falls); 2: a value moved under an absent row only.
            let perturb = rng.IntBelow 3

            let checkEquality (what: string) (a: Column) (b: Column) =
                let expected = cellsEqual a b

                equality.Check(
                    (a = b) = expected && (b = a) = expected,
                    fun () -> at (sprintf "%s: = answered %b where the cells say %b" what (a = b) expected)
                )

                if expected then
                    equality.Check(
                        hash a = hash b,
                        fun () -> at (sprintf "%s: equal columns with different hashes" what)
                    )

            /// Two columns of one type from one draw, through the typed builder, with the typed
            /// reader and the cell bridge held to the same storage.
            let drawn
                (what: string)
                (draw: unit -> 'T)
                (bump: 'T -> 'T)
                (build: string -> Vector<'T> -> Validity -> Column)
                (read: Column -> Vector<'T> option)
                (cellOf: 'T -> Cell)
                =
                let xs = Array.init n (fun _ -> draw ())
                let ys = Array.copy xs

                match perturb with
                | 1 ->
                    let k = rng.IntBelow n
                    ys[k] <- bump ys[k]
                | 2 ->
                    match Array.tryFindIndex not maskA with
                    | Some j -> ys[j] <- bump ys[j]
                    | None -> ()
                | _ -> ()

                let va = Vector.adopt xs
                let a = build "c" va (Validity.ofArray maskA)
                let b = build "c" (Vector.adopt ys) (Validity.ofArray maskB)
                checkEquality what a b

                // Phase 420: `AllValid` reads as the all-true mask of the column's length, so it is
                // one column with a hand-built all-true `Mask` of that length - and only that length.
                let allValid = build "c" va AllValid
                let allTrue = build "c" va (Mask(Vector.ofArray (Array.create n true)))

                equality.Check(
                    allValid = allTrue && allTrue = allValid && hash allValid = hash allTrue,
                    fun () ->
                        at (sprintf "%s: AllValid and an all-true Mask of the column's length are not one column" what)
                )

                checkEquality (what + " (AllValid, all-true Mask)") allValid allTrue

                checkEquality
                    (what + " (AllValid, a longer all-true Mask)")
                    allValid
                    (build "c" va (Mask(Vector.ofArray (Array.create (n + 1) true))))

                // The bridge: the same cells through `ofCells`, and back out through `toCells`.
                let cells = [ for i in 0 .. n - 1 -> if maskA[i] then cellOf xs[i] else Null ]

                match Column.ofCells "c" a.Type cells with
                | Ok viaCells ->
                    bridge.Check(
                        (viaCells = a),
                        fun () -> at (sprintf "%s: ofCells and the typed builder disagree" what)
                    )

                    bridge.Check(
                        List.map Cell.token (Column.toCells viaCells) = List.map Cell.token cells
                        && List.map Cell.token (Column.toCells a) = List.map Cell.token cells,
                        fun () -> at (sprintf "%s: toCells does not read the cells back" what)
                    )
                | Error e ->
                    bridge.Check(false, fun () -> at (sprintf "%s: ofCells refused the drawn cells (%A)" what e))

                bridge.Check(
                    (match read a with
                     | Some v -> obj.ReferenceEquals(v, va)
                     | None -> false)
                    && Column.validity a = Validity.ofList (List.ofArray maskA)
                    && (Column.validity a = AllValid) = Array.forall id maskA
                    && Vector.toArray (Column.mask a) = maskA
                    && Validity.presentCount n (Column.validity a) = (maskA |> Array.filter id |> Array.length)
                    && Column.length a = n
                    && Validity.ofArray (Array.create n true) = AllValid
                    && Vector.toArray (Column.mask allValid) = Array.create n true,
                    fun () ->
                        at (sprintf "%s: the typed reader, the mask or the length disagree with what was built" what)
                )

                va

            let floats = [| 0.0; -0.0; nan; 1.5; -2.0; 1e300; 0.1 |]
            let texts = [| ""; "a"; "b"; "é" |]
            let decimals = [| "0"; "1.5"; "-0.3"; "2"; "12.25" |]
            // Phase 422: dates and instants are held as integers, drawn from canonical texts.
            let dates =
                [| "2026-01-01"; "2026-02-28"; "1999-12-31" |]
                |> Array.choose TemporalText.tryDays

            let instants =
                [| "2026-01-01T00:00:00Z"; "2026-06-22T17:00:00Z" |]
                |> Array.choose (TemporalText.tryInstant TimeUnit.Seconds)
                |> Array.map fst

            let vector: Vector<float> =
                match rng.IntBelow 7 with
                | 0 ->
                    drawn "int" (fun () -> rng.IntBelow 5 - 2) ((+) 1) Column.ofInts Column.tryInts Int
                    |> Vector.map float
                | 1 ->
                    drawn
                        "float"
                        (fun () -> floats[rng.IntBelow floats.Length])
                        (fun f -> if System.Double.IsNaN f then 1.0 else f + 1.0)
                        Column.ofFloats
                        Column.tryFloats
                        Float
                | 2 ->
                    drawn "bool" (fun () -> rng.IntBelow 2 = 0) not Column.ofBools Column.tryBools Bool
                    |> Vector.map (fun b -> if b then 1.0 else 0.0)
                | 3 ->
                    drawn
                        "string"
                        (fun () -> texts[rng.IntBelow texts.Length])
                        (fun s -> s + "x")
                        Column.ofStrs
                        Column.tryStrs
                        Str
                    |> Vector.map (fun s -> float s.Length)
                | 4 ->
                    drawn
                        "decimal"
                        (fun () -> decimals[rng.IntBelow decimals.Length])
                        (fun s -> if s = "0" then "1" else s + "5")
                        Column.ofDecimals
                        Column.tryDecimals
                        Decimal
                    |> Vector.map (fun s -> float s.Length)
                | 5 ->
                    drawn
                        "date"
                        (fun () -> dates[rng.IntBelow dates.Length])
                        (fun d -> d + 1)
                        Column.ofDates
                        Column.tryDates
                        (fun d -> Date(TemporalText.dateText d))
                    |> Vector.map float
                | _ ->
                    // Whole-second instants in a column of a drawn unit, so every unit's builder,
                    // reader and cell bridge is held to one storage (`None` for the fractions).
                    let unit =
                        rng.Choose
                            [ TimeUnit.Seconds
                              TimeUnit.Milliseconds
                              TimeUnit.Microseconds
                              TimeUnit.Nanoseconds ]

                    drawn
                        ("timestamp at " + ColumnType.tag (TimestampType unit))
                        (fun () -> instants[rng.IntBelow instants.Length])
                        (fun s -> s + 1.0)
                        (fun name seconds validity -> Column.ofTimestamps name unit seconds None validity)
                        (fun c -> Column.tryTimestamps c |> Option.map _.Seconds)
                        (fun s -> Timestamp(TemporalText.instantText unit s 0))

            // The three pairs every iteration builds, whatever the draw.
            let one (f: float) =
                Column.ofFloats "f" (Vector.ofList [ f ]) AllValid

            checkEquality "NaN pair" (one nan) (one nan)
            checkEquality "signed-zero pair" (one -0.0) (one 0.0)

            checkEquality
                "absent-row pair"
                (Column.ofFloats "f" (Vector.adopt [| 1.0 |]) (Validity.ofList [ false ]))
                (Column.ofFloats "f" (Vector.adopt [| 2.0 |]) (Validity.ofList [ false ]))

            // Law 3: every read over the drawn vector (as floats) agrees with its indexer.
            let v = vector
            let m = Vector.length v
            let byIndex = [ for i in 0 .. m - 1 -> v[i] ]
            let arr = Vector.toArray v

            let iterated = ResizeArray<float>()
            Vector.iter iterated.Add v
            let indexed = ResizeArray<int * float>()
            Vector.iteri (fun i x -> indexed.Add(i, x)) v
            let start = rng.IntBelow(m + 1)
            let count = rng.IntBelow(m - start + 1)
            let sliced = Vector.slice start count v
            let borrowed = Vector.Unsafe.borrow sliced

            let bump (x: float) =
                if System.Double.IsNaN x then 0.0 else x + 1.0

            // Two float lists held equal under the cell's float identity — a drawn NaN is one value
            // here, where `=` on a `float list` would call it unequal to itself.
            let same (xs: float list) (ys: float list) =
                List.length xs = List.length ys
                && List.forall2 (fun a b -> Cell.compare (Float a) (Float b) = Some 0) xs ys

            let agree =
                m = v.Length
                && Vector.isEmpty v = (m = 0)
                && Vector.isEmpty Vector.empty<float>
                && same (Vector.toList v) byIndex
                && same (List.ofArray arr) byIndex
                && same (Vector.fold (fun acc x -> x :: acc) [] v) (List.rev byIndex)
                && same (List.ofSeq iterated) byIndex
                && (indexed |> Seq.map fst |> List.ofSeq) = [ 0 .. m - 1 ]
                && same (indexed |> Seq.map snd |> List.ofSeq) byIndex
                && same (Vector.toList (Vector.map bump v)) (List.map bump byIndex)
                && same
                    (Vector.toList (Vector.mapi (fun i x -> float i + bump x) v))
                    (List.mapi (fun i x -> float i + bump x) byIndex)
                && Vector.exists (fun x -> x > 1.0) v = List.exists (fun x -> x > 1.0) byIndex
                && Vector.tryFindIndex (fun x -> x > 1.0) v = List.tryFindIndex (fun x -> x > 1.0) byIndex
                && Vector.tryItem m v = None
                && Vector.tryItem -1 v = None
                && (m = 0
                    || (match Vector.tryItem 0 v with
                        | Some x -> same [ x ] [ Vector.item 0 v ]
                        | None -> false))
                && Vector.length sliced = count
                && same [ for i in 0 .. count - 1 -> sliced[i] ] (byIndex |> List.skip start |> List.truncate count)
                && borrowed.Length = count
                && same [ for i in 0 .. count - 1 -> borrowed.Array[borrowed.Offset + i] ] (Vector.toList sliced)
                && Vector.ofArray arr = v
                && Vector.ofSeq (Seq.ofList byIndex) = v
                && Vector.init m (fun i -> v[i]) = v

            reads.Check(agree, fun () -> at "a Vector read disagreed with the indexer")

            // Law 3 over a bool vector (Phase 431): under Fable it is PACKED, a byte a row, and every
            // read must still answer a boolean — `=` against `true` included, where a packed byte
            // would answer `false` — and a borrow lends the bytes, which read as booleans by
            // truthiness alone (DECISIONS.md D145.1). On .NET the same checks hold of a `bool[]`.
            let bits = Array.init (rng.IntBelow 7) (fun _ -> rng.IntBelow 2 = 0)
            let bv = Vector.ofArray bits
            let k = Vector.length bv
            let byBit = List.ofArray bits
            let start = rng.IntBelow(k + 1)
            let count = rng.IntBelow(k - start + 1)
            let sliced = Vector.slice start count bv
            let lent = Vector.Unsafe.borrow sliced

            // A lent element is read as a CONDITION, the one reading a packed byte answers right on
            // both hosts: `if b then true else false` would not do, the compiler reduces it to `b`.
            let bit (b: bool) = if b then 1 else 0

            let boolsAgree =
                [ for i in 0 .. k - 1 -> bv[i] = true ] = List.map (fun b -> b = true) byBit
                && Vector.toList bv = byBit
                && List.ofArray (Vector.toArray bv) = byBit
                && Vector.fold (fun acc b -> b :: acc) [] bv = List.rev byBit
                && Vector.toList (Vector.map not bv) = List.map not byBit
                && Vector.toList (Vector.mapi (fun i b -> b <> (i % 2 = 0)) bv) = List.mapi
                    (fun i b -> b <> (i % 2 = 0))
                    byBit
                && Vector.exists id bv = List.exists id byBit
                && Vector.tryFindIndex not bv = List.tryFindIndex not byBit
                && Vector.tryItem k bv = None
                && (k = 0 || Vector.tryItem 0 bv = Some(List.head byBit))
                && (let seen = ResizeArray<bool>()
                    Vector.iter seen.Add bv
                    List.ofSeq seen = byBit)
                && (let seen = ResizeArray<int * bool>()
                    Vector.iteri (fun i b -> seen.Add(i, b)) bv
                    List.ofSeq seen = List.indexed byBit)
                && Vector.toList sliced = (byBit |> List.skip start |> List.truncate count)
                && lent.Length = count
                && [ for i in 0 .. count - 1 -> bit lent.Array[lent.Offset + i] ] = List.map bit (Vector.toList sliced)
                && Vector.ofList byBit = bv
                && Vector.adopt (Array.copy bits) = bv
                && Vector.init k (fun i -> bits[i]) = bv
                && hash (Vector.ofList byBit) = hash bv
                && (k = 0 || Vector.ofList (List.map not byBit) <> bv)

            reads.Check(boolsAgree, fun () -> at "a bool Vector read did not answer the boolean the indexer holds"))

        LawKit.results [ equality; bridge; reads ]

    // ---- the ownership contract (Phase 418) ----
    // A `Vector` is immutable by contract, not by copy: two holders of one column share its storage,
    // `Vector.adopt` hands the caller's array to the vector without a copy, and `Vector.Unsafe.borrow`
    // lends the backing array to interop. No public member of `Vector` writes, so a write can reach a
    // vector's storage only through a broken promise — an adopted array written by the caller who gave
    // it away, or a borrowed one written by the borrower. The type cannot see either, so this family
    // does: it fingerprints every column it hands an operation, runs the operation, fingerprints them
    // again, and names the column whose bytes moved. The kit is compiled on both pipelines, so the
    // fingerprint is built from shifts, masks and code units only, and reads the same under Fable.

    /// Four big-endian bytes of `n`.
    let private putInt (buf: ResizeArray<byte>) (n: int) =
        buf.Add(byte ((n >>> 24) &&& 0xFF))
        buf.Add(byte ((n >>> 16) &&& 0xFF))
        buf.Add(byte ((n >>> 8) &&& 0xFF))
        buf.Add(byte (n &&& 0xFF))

    /// A string as its length and its UTF-16 code units, two bytes each — total over every string,
    /// an unpaired surrogate included, where a UTF-8 encoding would replace one and could not tell
    /// two of them apart.
    let private putString (buf: ResizeArray<byte>) (s: string) =
        putInt buf s.Length

        for ch in s do
            let u = int ch
            buf.Add(byte ((u >>> 8) &&& 0xFF))
            buf.Add(byte (u &&& 0xFF))

    /// A float by its canonical text (`Canon.canonicalFloat`, the shortest round-trip form), with the
    /// sign of zero kept: a write of `-0.0` over `0.0` is a write, though the two are one cell. Every
    /// NaN is one text, because the payload of a NaN is not portable — a JavaScript engine may
    /// canonicalise it on any store — so a write of one NaN over another is the one write this
    /// fingerprint does not see.
    let private putFloat (buf: ResizeArray<byte>) (f: float) =
        putString
            buf
            (if System.Double.IsNaN f then "NaN"
             elif System.Double.IsPositiveInfinity f then "Inf"
             elif System.Double.IsNegativeInfinity f then "-Inf"
             elif f = 0.0 then (if 1.0 / f < 0.0 then "-0" else "0")
             else Canon.canonicalFloat f)

    let private putBool (buf: ResizeArray<byte>) (b: bool) = buf.Add(if b then 1uy else 0uy)

    /// The SHA-256 of a vector's length and EVERY element in its range, read through its own
    /// indexer — an element under an absent row included, because the backing array is shared and a
    /// write under one column's absent row is a write into a value another holder may present.
    let private vectorDigest (put: ResizeArray<byte> -> 'T -> unit) (v: Vector<'T>) : string =
        let buf = ResizeArray<byte>()
        putInt buf v.Length

        for i in 0 .. v.Length - 1 do
            put buf v[i]

        Hash.sha256HexOfBytes (buf.ToArray())

    /// A column's fingerprint at one moment: the digest of its values and the digest of its mask
    /// (a fixed token for an `AllValid` column, which holds none).
    let private fingerprint (c: Column) : string * string =
        let values =
            match c.Data with
            | Ints(xs, _) -> vectorDigest putInt xs
            | Floats(xs, _) -> vectorDigest putFloat xs
            | Bools(xs, _) -> vectorDigest putBool xs
            | Dates(xs, _) -> vectorDigest putInt xs
            // Phase 422: the seconds and, where the column holds one, the fraction vector.
            | Timestamps(_, xs, f, _) ->
                vectorDigest putFloat xs
                + (match f with
                   | Some fs -> "+" + vectorDigest putInt fs
                   | None -> "")
            | Strs(xs, _)
            | Decimals(xs, _) -> vectorDigest putString xs

        // An `AllValid` column holds no mask (Phase 420), so there is nothing to write into: its
        // validity reads as one fixed token, and only a `Mask` is hashed.
        let validity =
            match Column.validity c with
            | AllValid -> "all-valid"
            | Mask m -> vectorDigest putBool m

        values, validity

    /// The ownership law over the columns `draw` builds (Phase 418): every column is fingerprinted
    /// as it is drawn, `operation` is run over the list, and every column is fingerprinted again; a
    /// column whose values or mask hash differently afterwards is named — its position, its name and
    /// its type — with the part that moved, and every other column that moved in the same run beside
    /// it. One assertion per column, so a `draw` that builds no column takes no evidence and the law
    /// reds as never reached rather than passing over nothing. An exception `operation` raises
    /// propagates: the law reads only what the operation leaves behind.
    let columnOwnershipLawsWith
        (operation: Column list -> unit)
        (draw: ConfRng.T -> Column list * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let untouched =
            LawKit.LawCell
                "the operation writes into no column it is handed: every column's values and mask hash after it as they did when it was built"

        LawKit.run iterations seed (fun rng _ at ->
            let columns = rng.Draw draw
            let before = List.map fingerprint columns
            operation columns
            let after = List.map fingerprint columns

            let moved =
                List.zip columns (List.zip before after)
                |> List.indexed
                |> List.choose (fun (i, (c, ((v0, m0), (v1, m1)))) ->
                    let parts =
                        [ if v0 <> v1 then
                              yield "values"
                          if m0 <> m1 then
                              yield "validity mask" ]

                    if List.isEmpty parts then
                        None
                    else
                        Some(
                            i,
                            sprintf
                                "column %d \"%s\" (%s): the bytes of its %s moved"
                                i
                                c.Name
                                (ColumnType.tag c.Type)
                                (String.concat " and " parts)
                        ))

            let report () =
                at (
                    "the operation wrote into storage it was handed — "
                    + (moved |> List.map snd |> String.concat "; ")
                )

            for i in 0 .. List.length columns - 1 do
                untouched.Check(not (List.exists (fun (j, _) -> j = i) moved), report))

        LawKit.results [ untouched ]

    /// The kit's own sample for the ownership law (Phase 418), every column one length: a column of
    /// each of the seven types over an adopted array, nulls drawn — `AllValid` where the draw left no
    /// row absent and an adopted `Mask` otherwise (Phase 420), so the law runs over both — and two
    /// float columns that are ADJACENT VIEWS of one backing array, their masks adjacent views of one
    /// mask array held as a `Mask` whatever the draw — so a write that runs past one column's range
    /// lands in its neighbour's, and a write through either is one the other's holder sees.
    let private ownershipSample (r: ConfRng.T) : Column list * ConfRng.T =
        let rng = LawKit.Draws 0
        rng.State <- r
        let n = 1 + rng.IntBelow 6

        let mask () =
            Validity.ofVector (Vector.adopt (Array.init n (fun _ -> rng.IntBelow 4 <> 0)))

        let pick (xs: 'T[]) = xs[rng.IntBelow xs.Length]

        let floats = [| 0.0; -0.0; nan; 1.5; -2.0; 1e300; 0.1; infinity |]
        // An unpaired surrogate is built from its code unit: a `\u` escape of one in a string literal
        // compiles as U+FFFD.
        let texts = [| ""; "a"; "é"; string (char 0xD800) |]
        let decimals = [| "0"; "1.5"; "-0.3"; "12.25" |]
        // Phase 422: days since 1970-01-01, and whole epoch seconds with a millisecond fraction.
        let dates = [| 20454; 10956 |]
        let instants = [| 1767225600.0; 1782147600.0 |]

        let shared = Vector.adopt (Array.init (2 * n) (fun _ -> pick floats))
        let sharedMask = Vector.adopt (Array.init (2 * n) (fun _ -> rng.IntBelow 4 <> 0))
        let own (xs: 'T[]) = Vector.adopt xs

        let columns =
            [ Column.ofInts "i" (own (Array.init n (fun _ -> rng.IntBelow 9 - 4))) (mask ())
              Column.ofFloats "f" (Vector.slice 0 n shared) (Mask(Vector.slice 0 n sharedMask))
              Column.ofFloats "g" (Vector.slice n n shared) (Mask(Vector.slice n n sharedMask))
              Column.ofBools "b" (own (Array.init n (fun _ -> rng.IntBelow 2 = 0))) (mask ())
              Column.ofStrs "s" (own (Array.init n (fun _ -> pick texts))) (mask ())
              Column.ofDates "d" (own (Array.init n (fun _ -> pick dates))) (mask ())
              Column.ofTimestamps
                  "t"
                  TimeUnit.Milliseconds
                  (own (Array.init n (fun _ -> pick instants)))
                  (Some(own (Array.init n (fun _ -> rng.IntBelow 1000))))
                  (mask ())
              Column.ofDecimals "m" (own (Array.init n (fun _ -> pick decimals))) (mask ()) ]

        columns, rng.State

    /// The ownership law (Phase 418) over the kit's own sample — a column of every type, nulls drawn,
    /// and two columns sharing one backing array and one mask array — with `operation` the
    /// consumer's pipeline, handed the columns as a list. `columnOwnershipLawsWith` is the same law
    /// over columns the consumer draws itself, for a pipeline that wants a schema of its own.
    let columnOwnershipLaws (operation: Column list -> unit) (seed: int) (iterations: int) : LawResult list =
        columnOwnershipLawsWith operation ownershipSample seed iterations
