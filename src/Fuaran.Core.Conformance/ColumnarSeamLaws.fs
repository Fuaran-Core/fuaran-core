namespace Fuaran.Core

/// The columnar layer's seam families: the aggregate null-skip (Phase 36) and the columnar validator
/// (Phase 37) — `SeamLaws` until the Phase 388 split along its banners.
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

                    Column.ofStrs name Vector.empty Vector.empty

            let t: Table =
                { Schema = [ "a", aType; "s", StringType ]
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

    /// The cell-level reading `=` on columns is held to: one length, one validity mask, and at every
    /// present row `Cell.compare` answers `Some 0`.
    let private cellsEqual (a: Column) (b: Column) : bool =
        Column.length a = Column.length b
        && Column.validity a = Column.validity b
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
    ///    under an absent row. The law is stated over columns in the storage's contract (canonical
    ///    decimal text): a non-canonical decimal text is a column `Table.validate` refuses, and its
    ///    equality is by text.
    /// 2. **The bridge and the typed builders build one column.** `Column.ofCells` over the drawn
    ///    cells equals the typed builder over the drawn vector and mask, `toCells` reads the drawn
    ///    cells back, and the typed reader hands back the vector the builder was given.
    /// 3. **Every vector read agrees with the indexer.** `toList`, `toArray`, `fold`, `iter`, `iteri`,
    ///    `map`, `mapi`, `exists`, `tryFindIndex`, `tryItem`, a `slice` and a borrowed array all answer
    ///    what indexing answers, over the drawn vector.
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
                    && Validity.presentCount (Column.validity a) = (maskA |> Array.filter id |> Array.length)
                    && Column.length a = n
                    && (Column.validity (build "c" va (Validity.all n))) = Validity.ofArray (Array.create n true),
                    fun () ->
                        at (sprintf "%s: the typed reader, the mask or the length disagree with what was built" what)
                )

                va

            let floats = [| 0.0; -0.0; nan; 1.5; -2.0; 1e300; 0.1 |]
            let texts = [| ""; "a"; "b"; "é" |]
            let decimals = [| "0"; "1.5"; "-0.3"; "2"; "12.25" |]
            let dates = [| "2026-01-01"; "2026-02-28"; "1999-12-31" |]
            let instants = [| "2026-01-01T00:00:00Z"; "2026-06-22T17:00:00Z" |]

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
                        (fun s -> if s = "2026-01-01" then "2026-01-02" else "2026-01-01")
                        Column.ofDates
                        Column.tryDates
                        Date
                    |> Vector.map (fun s -> float s.Length)
                | _ ->
                    drawn
                        "timestamp"
                        (fun () -> instants[rng.IntBelow instants.Length])
                        (fun s -> if s = instants[0] then instants[1] else instants[0])
                        Column.ofTimestamps
                        Column.tryTimestamps
                        Timestamp
                    |> Vector.map (fun s -> float s.Length)

            // The three pairs every iteration builds, whatever the draw.
            let one (f: float) =
                Column.ofFloats "f" (Vector.ofList [ f ]) (Validity.all 1)

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

            reads.Check(agree, fun () -> at "a Vector read disagreed with the indexer"))

        LawKit.results [ equality; bridge; reads ]
