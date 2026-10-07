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

            let col = Column.create "c" ty cells
            let present = cells |> List.filter (fun c -> not (Cell.isNull c))
            let presentCol = Column.create "c" ty present

            for c in present do
                match c with
                | Int _ -> intCells <- intCells + 1
                | Float _ -> floatCells <- floatCells + 1
                | Decimal _ -> decimalCells <- decimalCells + 1
                | _ -> ()

            (match Column.aggregate Count col with
             | Ok(Int n) when n = List.length present -> nullSkip.Saw()
             | other -> nullSkip.Check(false, fun () -> at (sprintf "Count ≠ present count (%A)" other)))

            match Column.aggregate Sum col, Column.aggregate Sum presentCol with
            | Ok a, Ok b when a = b -> nullSkip.Saw()
            | a, b -> nullSkip.Check(false, fun () -> at (sprintf "Sum not null-skipping (%A vs %A)" a b)))

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

            let t: Table =
                { Schema = [ "a", aType; "s", StringType ]
                  Columns = [ Column.create "a" aType aCells; Column.create "s" StringType sCells ] }

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
