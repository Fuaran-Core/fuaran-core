namespace Fuaran.Core

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
    /// naming it (Phase 299), in row order: a non-finite `Float` (`NonFiniteFloat` — the wire has
    /// none), and a `Decimal` / `Date` / `Timestamp` whose text is not its type's canonical form
    /// (`MalformedShape`). Read off the typed storage (Phase 417): a float column is scanned for a
    /// non-finite value and a text column for a non-canonical text, at the present rows only. A
    /// cell of another type is not a case any more — the storage cannot hold one — and a mask that
    /// is not the values' length is named first (`LengthMismatch`), because a column that disagrees
    /// about its own row count has no rows to read. An `AllValid` column (Phase 420) holds no mask
    /// and so cannot disagree.
    let private firstUncarriableCell (c: Column) : ColumnError option =
        let rows = Column.validity c

        let firstPresent (xs: Vector<'T>) (bad: 'T -> bool) : 'T option =
            let mutable found = None
            let mutable i = 0

            while found.IsNone && i < xs.Length do
                if Validity.isPresent i rows && bad xs[i] then
                    found <- Some xs[i]

                i <- i + 1

            found

        let notCanonical (xs: Vector<string>) (isCanonical: string -> bool) (message: string) =
            firstPresent xs (fun s -> not (isCanonical s))
            |> Option.map (fun _ -> MalformedShape(c.Name + ": " + message))

        match rows with
        | Mask m when m.Length <> Column.length c -> Some(LengthMismatch(c.Name, Column.length c, m.Length))
        | _ ->
            match c.Data with
            | Ints _
            | Bools _
            | Strs _ -> None
            | Floats(xs, _) ->
                firstPresent xs (fun f -> System.Double.IsNaN f || System.Double.IsInfinity f)
                |> Option.bind JVal.nonFiniteToken
                |> Option.map (fun tok -> NonFiniteFloat(c.Name, tok))
            | Decimals(xs, _) ->
                notCanonical
                    xs
                    DecimalText.isCanonical
                    "a decimal cell must carry canonical decimal text — an optional '-', integer digits with no leading zero, and a '.' with fraction digits only where the fraction is non-zero, with no trailing zero (build the cell with Cell.decimal)"
            | Dates(xs, _) ->
                notCanonical
                    xs
                    TemporalText.isCanonicalDate
                    "a date cell must carry a canonical ISO-8601 date, YYYY-MM-DD, naming a day that exists"
            | Timestamps(xs, _) ->
                notCanonical
                    xs
                    TemporalText.isCanonicalTimestamp
                    "a timestamp cell must carry a canonical ISO-8601 UTC timestamp, YYYY-MM-DDThh:mm:ssZ, naming an instant that exists"

    /// Well-formedness — THE TABLE THE CODEC CAN CARRY (Phase 43; widened to the cells by Phase 299).
    /// The typed builders do no validation, and `encodeJson` silently papers over a malformed table
    /// (a schema name with no column emits an empty placeholder; an extra column is dropped; ragged
    /// columns encode against the first column's length; a repeated name emits a repeated member key
    /// its readers disagree about). `validate` names the fault instead, first found in this order:
    ///   (a) no schema name and no column name appears twice (`Malformed`);
    ///   (b) every schema name has exactly one matching column and vice-versa (`Malformed`);
    ///   (c) each column's `Type` matches its schema entry (`TypeMismatch`);
    ///   (d) all columns share one length (`RaggedColumns`);
    ///   (e) column by column in schema order: the column's values and validity mask are one length
    ///       (`LengthMismatch`), and every present cell, row by row, is one the codec carries as a
    ///       cell of its column's type — a `Float` is finite (`NonFiniteFloat`), and a `Decimal`,
    ///       `Date` or `Timestamp` carries its type's canonical text (`MalformedShape`). A cell whose
    ///       type does not widen into the column's was this clause's `TypeMismatch` until Phase 417;
    ///       the typed storage cannot hold one, and `Column.ofCells` refuses it at construction.
    /// Over what it accepts, `ColumnCodec.tryEncode` is exactly `Ok (encode src)` — a law pins it —
    /// and `ColumnCodec.decode` ends in it, so a table that encodes is a table that decodes. It is
    /// not a data-quality check: those are the columnar validator's rules.
    let validate (t: Table) : Result<unit, ColumnError> =
        let schemaNames = t.Schema |> List.map fst
        let columnNamesList = t.Columns |> List.map _.Name

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
                        | Some c when c.Type <> ty -> Some(TypeMismatch(name, ty, ColumnType.tag c.Type))
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
