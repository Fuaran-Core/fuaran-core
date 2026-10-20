module Fuaran.Core.Tests.ProofOracle.ColumnRefinementTests

// The `Proofs.Oracle` differential for `proofs/ColumnRefinement.fst`: the extracted model
// (`proofs/oracle/ColumnRefinement.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

// ---------------------------------------------------------------------------
//  Phase 419 — the REFINEMENT: `ColumnRefinement` beside `Column.ofCells` / `Column.toCells`, the
//  typed `Table.validate` and `ColumnCodec.encodeJson`.
// ---------------------------------------------------------------------------
//
// `proofs/ColumnRefinement.fst` models the typed-vector column Phase 417 left — `Validity`, the
// `ColumnData` union, `toCells`, `ofCells`, `Equals`, the typed `firstUncarriableCell` and the
// encoder's walks — and proves the map to `WireColumn`'s list column a bijection on validated
// columns, under which `validate` and `encode` agree. What runs here is each modelled operation
// beside the shipped one, over the typed column read the way the theorems read it: every `Vector`
// through `Vector.toArray` and nothing else. The go-red is the bridge that DROPS the widening
// normalisation: `toCells (ofCells cs)` held to `cs` itself rather than to the model's normal form.

module private RefinementDiff =

    let host = ColumnDiff.host

    /// Production's column as the model's typed column: the case, every vector through `toArray`.
    let toTyped (c: Column) : ColumnRefinement.typed_column<int, float> =
        let mask = Column.mask c |> Vector.toArray |> List.ofArray

        let texts (xs: Vector<string>) =
            Vector.toArray xs |> List.ofArray |> List.map canonToChs

        let data =
            match c.Data with
            | Ints(xs, _) -> ColumnRefinement.Ints(Vector.toArray xs |> List.ofArray, mask)
            | Floats(xs, _) -> ColumnRefinement.Floats(Vector.toArray xs |> List.ofArray, mask)
            | Bools(xs, _) -> ColumnRefinement.Bools(Vector.toArray xs |> List.ofArray, mask)
            | Strs(xs, _) -> ColumnRefinement.Strs(texts xs, mask)
            // Phase 430: the integers as the model holds them — the day counts, and per unit the
            // whole epoch seconds (an integer-valued float, read as the integer it is) with the
            // fraction vector where the column holds one.
            | Dates(xs, _) -> ColumnRefinement.Dates(Vector.toArray xs |> List.ofArray |> List.map bigint, mask)
            | Timestamps(u, xs, f, _) ->
                ColumnRefinement.Timestamps(
                    ColumnDiff.toModelUnit u,
                    Vector.toArray xs |> List.ofArray |> List.map (fun s -> bigint (int64 s)),
                    toMOpt (f |> Option.map (fun fs -> Vector.toArray fs |> List.ofArray |> List.map bigint)),
                    mask
                )
            | Decimals(xs, _) -> ColumnRefinement.Decimals(texts xs, mask)

        ({ col_name = canonToChs c.Name
           col_data = data }
        : ColumnRefinement.typed_column<int, float>)

    let toTypedTable (t: Table) : ColumnRefinement.typed_table<int, float> =
        ({ tschema = t.Schema |> List.map (fun f -> canonToChs f.Name, ColumnDiff.toModelType f.Type)
           tcolumns = t.Columns |> List.map toTyped }
        : ColumnRefinement.typed_table<int, float>)

    /// Cells as text, a float by its canonical layout — so a `Float -0.0` beside a `Float 0.0`, or
    /// a NaN beside a NaN, is not a disagreement the wire could carry.
    let showCells (cells: Cell list) : string =
        cells
        |> List.map (fun c ->
            match c with
            | Float f -> "Float " + FloatLayout.canonical f
            | other -> sprintf "%A" other)
        |> String.concat ";"

    let showModelCells (cells: WireColumn.cell<int, float> list) : string =
        cells |> List.map ColumnDiff.ofModelCell |> showCells

    let private texts = [| "1.5"; "x"; ""; "12"; "2026-02-28"; "k\"q"; "é" |]

    /// One drawn cell of `ty`, and whether it is a WIDENED one — an `Int` in a float or decimal
    /// column, which `ofCells` normalises at construction. Nulls one draw in five; a non-canonical
    /// text now and then (it builds, and is `validate`'s to refuse).
    let private cellOf (draw: int -> int) (ty: ColumnType) : Cell * bool =
        if draw 5 = 0 then
            Null, false
        else
            match ty with
            | IntType -> Int(draw 2001 - 1000), false
            | FloatType ->
                match draw 4 with
                | 0 -> Int(draw 50 - 25), true
                | 1 -> Float nan, false
                | 2 -> Float -0.0, false
                | _ -> Float(float (draw 4000 - 2000) / 8.0), false
            | BoolType -> Bool(draw 2 = 0), false
            | StringType -> Str texts[draw texts.Length], false
            // Phase 422: a date column holds days, so a `Date` whose text names no day is refused at
            // construction (`ofCells`) where the list model holds it and refuses it at `validate`;
            // the two are no longer one `ofCells`, and the pool draws canonical dates only. The
            // construction refusal is `ColumnTests`' "ofCells refuses temporal text it cannot hold".
            | DateType ->
                (if draw 7 = 0 then
                     Date "2024-02-29"
                 else
                     Date(sprintf "2026-01-%02d" (1 + draw 28))),
                false
            | TimestampType u -> Timestamp(ColumnDiff.instantFor draw u), false
            | DecimalType ->
                match draw 3 with
                | 0 -> Int(draw 50 - 25), true
                | 1 -> Decimal "1.50", false
                | _ -> Decimal(string (draw 2000 - 1000)), false

    /// A present cell `ofCells` refuses: one of a type that does NOT widen into `ty`, and for the
    /// temporal columns (Phase 430) also an instant FINER than the column's unit — refused as the
    /// `TypeMismatch` naming the finer type — and a text that is not canonical, refused as the
    /// `MalformedShape` the integer storage cannot hold.
    let private outsideOf (draw: int -> int) (ty: ColumnType) : Cell =
        match ty with
        | IntType -> Bool true
        | FloatType -> Str "x"
        | BoolType -> Int 1
        | StringType -> Bool false
        | DateType ->
            match draw 3 with
            | 0 -> Str "2026-01-01"
            | 1 -> Date "2026-02-30"
            | _ -> Date "nope"
        | TimestampType u ->
            match draw 3, ColumnDiff.finerThan u with
            | 0, Some finer -> Timestamp finer
            | 1, _ -> Timestamp "2026-01-01T00:00:00.50Z"
            | _ -> Int 5
        | DecimalType -> Float 1.5

    type Tally =
        {
            Draws: int
            Diffs: string list
            /// Draws `ofCells` built, and refused.
            Built: int
            Refused: int
            /// Built draws holding a widened `Int`; built draws holding a `Null`.
            Widened: int
            WithNull: int
            /// Built draws on which the LITERAL bridge — `toCells (ofCells cs)` held to `cs` — lost.
            LiteralLost: int
            /// The column types drawn, and the refusal classes reached.
            Types: Set<string>
            Classes: Set<string>
        }

    let empty =
        { Draws = 0
          Diffs = []
          Built = 0
          Refused = 0
          Widened = 0
          WithNull = 0
          LiteralLost = 0
          Types = Set.empty
          Classes = Set.empty }

    let private diff (label: string) (what: string) (prod: string) (model: string) (t: Tally) : Tally =
        { t with
            Diffs =
                sprintf "%s: %s\n  production: %s\n  the model:  %s" label what prod model
                :: t.Diffs }

    /// One drawn cell list, asked of production and of the model: `ofCells` (the verdict, the
    /// refusal's class, and the cells of what each built); `toCells` of the production column read
    /// through its typed vector; and the literal bridge, counted rather than asserted.
    let probe (label: string) (name: string) (ty: ColumnType) (cells: Cell list) (widened: bool) (t: Tally) : Tally =
        let t =
            { t with
                Draws = t.Draws + 1
                Types = t.Types.Add(ColumnType.tag ty) }

        let modelCells = cells |> List.map ColumnDiff.toModelCell
        let prod = Column.ofCells name ty cells

        let model =
            ColumnRefinement.of_cells host (canonToChs name) (ColumnDiff.toModelType ty) modelCells

        match prod, model with
        | Result.Ok pc, WireColumn.Good mc ->
            // what each built, read back as cells — and the model's own `to_cells` of production's
            // column read through `toArray`, which is the bridge the theorems are stated over
            let prodCells = Column.toCells pc
            let viaModel = ColumnRefinement.to_cells (toTyped pc) |> showModelCells
            let modelOwn = ColumnRefinement.to_cells mc |> showModelCells

            let t =
                if showCells prodCells = modelOwn then
                    t
                else
                    diff label "toCells (ofCells cells)" (showCells prodCells) modelOwn t

            let t =
                if showCells prodCells = viaModel then
                    t
                else
                    diff label "to_cells of the production column through toArray" (showCells prodCells) viaModel t

            // the mask, and the model's `Equals` between the two typed columns
            let prodMask = Column.mask pc |> Vector.toArray |> List.ofArray
            let modelMask = ColumnRefinement.data_mask mc.col_data

            let t =
                if prodMask = modelMask then
                    t
                else
                    diff label "the validity mask" (sprintf "%A" prodMask) (sprintf "%A" modelMask) t

            // The model's `Equals` between the two typed columns. The model's `=` on a float is its
            // carrier's, and F#'s says no NaN equals itself, where production's identity
            // (`VectorElements.equal`) reads every NaN as one value — the element identity the
            // ladder's `column-vector-is-its-contents` row names as the bridge's. So every NaN is
            // folded to one finite stand-in on BOTH sides first: what is asked of the model is its
            // mask logic (one length, one mask, equal at the present rows), which is the theorem's.
            let comparable (c: ColumnRefinement.typed_column<int, float>) =
                match c.col_data with
                | ColumnRefinement.Floats(xs, m) ->
                    ({ c with
                        col_data =
                            ColumnRefinement.Floats(
                                xs |> List.map (fun f -> if System.Double.IsNaN f then -7.0e300 else f),
                                m
                            ) }
                    : ColumnRefinement.typed_column<int, float>)
                | _ -> c

            let t =
                if ColumnRefinement.col_eq (comparable (toTyped pc)) (comparable mc) then
                    t
                else
                    diff label "ColumnData.Equals between production's column and the model's" "equal" "unequal" t

            // the literal bridge: production's `toCells` against the cells it was handed
            let lost = showCells prodCells <> showCells cells

            { t with
                Built = t.Built + 1
                Widened = t.Widened + (if widened then 1 else 0)
                WithNull = t.WithNull + (if cells |> List.exists (fun c -> c = Null) then 1 else 0)
                LiteralLost = t.LiteralLost + (if lost then 1 else 0) }
        | Result.Error pe, WireColumn.Bad me ->
            let p = ColumnDiff.prodClass pe
            let m = ColumnDiff.modelClass me

            let t = if p = m then t else diff label "ofCells refused" p m t

            { t with
                Refused = t.Refused + 1
                Classes = t.Classes.Add(p.Split(' ')[0]) }
        | Result.Ok pc, WireColumn.Bad me ->
            diff label "ofCells" ("ok " + showCells (Column.toCells pc)) ("refused " + ColumnDiff.modelClass me) t
        | Result.Error pe, WireColumn.Good mc ->
            diff
                label
                "ofCells"
                ("refused " + ColumnDiff.prodClass pe)
                ("ok " + showModelCells (ColumnRefinement.to_cells mc))
                t

    /// Drawn cell lists: every type, nulls, widened cells, and — in the wild pool — a present cell
    /// of a type the column cannot hold, which `ofCells` refuses.
    let generated (seed: int) (trials: int) (wild: bool) : Tally =
        let r = ref seed

        let draw (n: int) =
            r.Value <- nextCanonSeed r.Value
            r.Value % n

        let mutable t = empty

        for i in 1..trials do
            let ty = ColumnDiff.modelTypes[draw ColumnDiff.modelTypes.Length]
            let rows = draw 5
            let drawn = [ for _ in 1..rows -> cellOf draw ty ]
            let cells = drawn |> List.map fst
            let widened = drawn |> List.exists snd

            let cells =
                if wild && rows > 0 && draw 4 = 0 then
                    let at = draw rows
                    cells |> List.mapi (fun j c -> if j = at then outsideOf draw ty else c)
                else
                    cells

            t <- probe (sprintf "generated seed=%d iteration=%d" seed i) "c" ty cells widened t

        t

    type TableTally =
        { Tables: int
          TableDiffs: string list
          Valid: int
          Refusals: Set<string> }

    /// One drawn table (the Phase 306 pools), asked of production and of the typed model:
    /// `Table.validate` beside `validate_t`, `encodeJson` beside `encode_json_t` — both over the
    /// typed column — and `validate_t` beside the list model's `validate` of the table's image,
    /// which is `validate_agrees` evaluated on the extracted code.
    let probeTable (label: string) (table: Table) (t: TableTally) : TableTally =
        let tt = toTypedTable table
        let t = { t with Tables = t.Tables + 1 }

        let tdiff (what: string) (prod: string) (model: string) (t: TableTally) =
            { t with
                TableDiffs =
                    sprintf "%s: %s\n  production: %s\n  the model:  %s" label what prod model
                    :: t.TableDiffs }

        let prodV =
            match Table.validate table with
            | Result.Ok() -> "ok"
            | Result.Error e -> "refused " + ColumnDiff.prodClass e

        let showT (r: WireColumn.res<unit>) =
            match r with
            | WireColumn.Good _ -> "ok"
            | WireColumn.Bad e -> "refused " + ColumnDiff.modelClass e

        let modelV = showT (ColumnRefinement.validate_t host tt)
        let listV = showT (WireColumn.validate host (ColumnRefinement.to_list_table tt))

        let t =
            if prodV = modelV then
                t
            else
                tdiff "validate" prodV modelV t

        let t =
            if modelV = listV then
                t
            else
                tdiff "validate_t against the list model's validate through the map" modelV listV t

        let t =
            if prodV = "ok" then
                { t with Valid = t.Valid + 1 }
            else
                { t with
                    Refusals = t.Refusals.Add(prodV.Substring(8).Split(' ')[0]) }

        let prodJson = Json.render (ColumnCodec.encodeJson (Embedded table))

        let modelJson =
            Json.render (canonOfModel (ColumnRefinement.encode_json_t host (ColumnRefinement.TEmbedded tt)))

        if prodJson = modelJson then
            t
        else
            tdiff "encodeJson" prodJson modelJson t

    let generatedTables (seed: int) (trials: int) (wild: int) : TableTally =
        let r = ref seed

        let mutable t =
            { Tables = 0
              TableDiffs = []
              Valid = 0
              Refusals = Set.empty }

        for i in 1..trials do
            t <- probeTable (sprintf "generated seed=%d iteration=%d" seed i) (ColumnDiff.genTable r wild) t

        t

    // ---- Phase 420: the two-case validity (`ColumnRefinement` section 7) ----

    /// Production's `Validity` as the model's: `AllValid`, or a `Mask` read through `toArray`.
    let toModelValidity (v: Validity) : ColumnRefinement.validity_rep =
        match v with
        | AllValid -> ColumnRefinement.AllValid
        | Mask m -> ColumnRefinement.Mask(Vector.toArray m |> List.ofArray)

    /// A row count as the model writes one: a list of that length.
    let units (n: int) : unit list = List.replicate n ()

    type ValidityTally =
        { Draws: int
          Diffs: string list
          Normalised: int
          Masked: int
          EqualMixed: int
          UnequalMixed: int }

    /// Drawn masks and validities beside the model's: the normalising constructors against
    /// `of_mask`, `Validity.toMask` against `to_mask`, and column equality over one values vector
    /// (so only the validities can differ) against `same_mask`. `reading` is the model's equality
    /// the column equality is held to; the go-red hands it the representation's `=`.
    let generatedValidities
        (seed: int)
        (trials: int)
        (reading: unit list -> ColumnRefinement.validity_rep -> ColumnRefinement.validity_rep -> bool)
        : ValidityTally =
        let r = ref seed

        let draw (n: int) =
            r.Value <- nextCanonSeed r.Value
            r.Value % n

        let mutable t =
            { Draws = 0
              Diffs = []
              Normalised = 0
              Masked = 0
              EqualMixed = 0
              UnequalMixed = 0 }

        let diff (label: string) (what: string) (p: string) (m: string) =
            t <-
                { t with
                    Diffs = sprintf "%s: %s - production %s, model %s" label what p m :: t.Diffs }

        /// A validity of a column of `n` rows, from every corner the representation has: `AllValid`,
        /// a built (normalised) mask, a hand-built all-true `Mask` of the right length or of another,
        /// and a hand-built `Mask` with a clear bit.
        let validityOf (n: int) : Validity =
            match draw 5 with
            | 0 -> AllValid
            | 1 -> Validity.ofArray (Array.init n (fun _ -> draw 4 <> 0))
            | 2 -> Mask(Vector.ofArray (Array.create n true))
            | 3 -> Mask(Vector.ofArray (Array.create (max 0 (n + 1 - draw 3)) true))
            | _ -> Mask(Vector.ofArray (Array.init n (fun _ -> draw 3 <> 0)))

        for i in 1..trials do
            let label = sprintf "generated seed=%d iteration=%d" seed i
            let n = draw 7
            let bits = List.init n (fun _ -> draw 4 <> 0)

            // the normalising constructors, against `of_mask`
            let built = Validity.ofList bits
            let modelBuilt = ColumnRefinement.of_mask bits

            if toModelValidity built <> modelBuilt then
                diff label "Validity.ofList" (sprintf "%A" built) (sprintf "%A" modelBuilt)

            if toModelValidity (Validity.ofArray (Array.ofList bits)) <> modelBuilt then
                diff
                    label
                    "Validity.ofArray"
                    (sprintf "%A" (Validity.ofArray (Array.ofList bits)))
                    (sprintf "%A" modelBuilt)

            if toModelValidity (Validity.ofVector (Vector.ofList bits)) <> modelBuilt then
                diff label "Validity.ofVector" "a different validity" (sprintf "%A" modelBuilt)

            // the materialised mask, against `to_mask`
            let a = validityOf n
            let b = validityOf n
            let prodMask = Validity.toMask n a |> Vector.toArray |> List.ofArray
            let modelMask = ColumnRefinement.to_mask (units n) (toModelValidity a)

            if prodMask <> modelMask then
                diff label "Validity.toMask" (sprintf "%A" prodMask) (sprintf "%A" modelMask)

            // column equality over ONE values vector, against the model's reading of the validities
            let values = Vector.init n id
            let prodEqual = Column.ofInts "c" values a = Column.ofInts "c" values b
            let modelEqual = reading (units n) (toModelValidity a) (toModelValidity b)

            if prodEqual <> modelEqual then
                diff label (sprintf "equality of %A and %A" a b) (string prodEqual) (string modelEqual)

            let mixed =
                match a, b with
                | AllValid, Mask _
                | Mask _, AllValid -> true
                | _ -> false

            t <-
                { t with
                    Draws = t.Draws + 1
                    Normalised = t.Normalised + (if built = AllValid then 1 else 0)
                    Masked = t.Masked + (if built = AllValid then 0 else 1)
                    EqualMixed = t.EqualMixed + (if mixed && prodEqual then 1 else 0)
                    UnequalMixed = t.UnequalMixed + (if mixed && not prodEqual then 1 else 0) }

        t

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- the refinement: the typed column is the cell-list column it replaces (Phase 419) ----

          testCase
              "the refinement oracle agrees with Column.ofCells and Column.toCells over drawn columns of every type, with nulls and widened cells"
          <| fun _ ->
              // Two pools of drawn cell lists. The first holds only cells the column's type can
              // hold (nulls and widened `Int`s included), so every draw builds; the second also
              // plants a present cell of a type the column cannot hold, which `ofCells` refuses.
              // 2,200 draws since Phase 430: the pool draws ten column types where it drew seven,
              // and the adequacy floors below are counts, not shares.
              let fitting = onBigStack (fun () -> RefinementDiff.generated 4191 2200 false)
              let wild = onBigStack (fun () -> RefinementDiff.generated 4192 2200 true)

              for (name, t) in [ "the fitting pool", fitting; "the wild pool", wild ] do
                  Expect.isEmpty
                      t.Diffs
                      (sprintf
                          "%s: the refinement oracle disagreed with production:\n%s"
                          name
                          (renderCanonDiffs t.Diffs))

              // adequacy: every type, nulls, widened cells; and the refusal
              Expect.equal fitting.Built fitting.Draws "every draw of the fitting pool builds"

              Expect.equal
                  fitting.Types
                  (ColumnDiff.modelTypes |> List.map ColumnType.tag |> Set.ofList)
                  "every column type the list model spells was drawn"

              Expect.isGreaterThan fitting.Widened 150 (sprintf "draws holding a widened Int (%d)" fitting.Widened)
              Expect.isGreaterThan fitting.WithNull 400 (sprintf "draws holding a Null (%d)" fitting.WithNull)
              Expect.isGreaterThan wild.Refused 150 (sprintf "the wild pool reached ofCells' refusal (%d)" wild.Refused)

              Expect.equal
                  wild.Classes
                  (Set.ofList [ "TypeMismatch"; "MalformedShape" ])
                  "and the refusals are the two classes ofCells has: a cell outside the type (an instant finer than its unit included), and temporal text it cannot hold"

              Expect.isGreaterThan wild.Built 800 (sprintf "the wild pool still builds (%d)" wild.Built)

          testCase
              "the typed validate and encodeJson agree with production through the refinement, over the column pools"
          <| fun _ ->
              // The Phase 306 pools, over the TYPED column this time: `validate_t` and
              // `encode_json_t` read production's vectors through `toArray`, where `WireColumn`'s
              // differential reads its cells.
              let valid = onBigStack (fun () -> RefinementDiff.generatedTables 3063 2000 0)
              let wild = onBigStack (fun () -> RefinementDiff.generatedTables 3064 3000 1)

              for (name, t) in [ "the valid pool", valid; "the wild pool", wild ] do
                  Expect.isEmpty
                      t.TableDiffs
                      (sprintf "%s: the typed model disagreed with production:\n%s" name (renderCanonDiffs t.TableDiffs))

              Expect.equal valid.Valid valid.Tables "every table of the first pool is one validate accepts"

              for cls in
                  [ "Malformed"
                    "TypeMismatch"
                    "RaggedColumns"
                    "NonFiniteFloat"
                    "MalformedShape" ] do
                  Expect.isTrue
                      (wild.Refusals.Contains cls)
                      (sprintf "the wild pool reached a %s refusal (reached: %A)" cls wild.Refusals)

          testCase "a bridge that drops the widening normalisation loses — on exactly the widened draws"
          <| fun _ ->
              // The go-red: `toCells (ofCells cs)` held to `cs` ITSELF, the bridge that forgets
              // `ofCells` normalises a widened `Int` at construction. It loses on every built draw
              // holding an `Int` in a float or decimal column and on no other — `to_cells_of_cells`
              // says the right-hand side is `norm_cells`, which moves exactly those cells.
              let t = onBigStack (fun () -> RefinementDiff.generated 4191 2200 false)

              Expect.isEmpty t.Diffs (sprintf "the pool is the clean one above:\n%s" (renderCanonDiffs t.Diffs))
              Expect.isGreaterThan t.Widened 150 "the pool holds widened draws"
              Expect.isGreaterThan (t.Built - t.Widened) 500 "and draws with nothing to widen"
              Expect.equal t.LiteralLost t.Widened "the literal bridge lost on the widened draws, and on no other"

          testCase
              "the two-case validity oracle agrees with production: the normalising constructors, toMask, and column equality (Phase 420)"
          <| fun _ ->
              // `ColumnRefinement` section 7 beside the shipped `Validity`: `of_mask` against the
              // three normalising constructors, `to_mask` against `Validity.toMask`, and `same_mask`
              // against column equality over one values vector, so only the validities differ.
              let t = RefinementDiff.generatedValidities 4201 3000 ColumnRefinement.same_mask

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the validity oracle disagreed with production:\n%s" (String.concat "\n" (List.rev t.Diffs)))

              // adequacy: both cases built, and both answers of the mixed comparison reached
              Expect.isGreaterThan t.Normalised 300 (sprintf "draws the constructors made AllValid (%d)" t.Normalised)
              Expect.isGreaterThan t.Masked 300 (sprintf "draws they kept as a Mask (%d)" t.Masked)

              Expect.isGreaterThan
                  t.EqualMixed
                  50
                  (sprintf "AllValid beside an all-true Mask of the length, found equal (%d)" t.EqualMixed)

              Expect.isGreaterThan
                  t.UnequalMixed
                  50
                  (sprintf "AllValid beside any other Mask, found unequal (%d)" t.UnequalMixed)

          testCase "a model that compares the validities' representations loses - on the mixed equal pairs"
          <| fun _ ->
              // The go-red: column equality held to `=` on the two representations rather than on
              // the materialised masks. `same_mask_is_mask_equality` says the right-hand side is the
              // masks' equality, so the representation's `=` disagrees with production exactly where
              // `AllValid` meets an all-true `Mask` of the column's length.
              let representational
                  (_: unit list)
                  (a: ColumnRefinement.validity_rep)
                  (b: ColumnRefinement.validity_rep)
                  =
                  a = b

              let t = RefinementDiff.generatedValidities 4201 3000 representational

              Expect.isGreaterThan t.EqualMixed 50 "the pool holds the mixed equal pairs"
              Expect.equal t.Diffs.Length t.EqualMixed "the representational model lost on those, and on no other" ]
