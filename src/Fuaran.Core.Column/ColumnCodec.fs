namespace Fuaran.Core

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

    /// One column's `values` and `validity` arrays, walked off the typed storage (Phase 417): the
    /// element at a present row as its JSON value, the absent slot at an absent one — never what
    /// the vector happens to hold there — so the bytes are those the `Cell list` column wrote. A
    /// widened cell was normalised at construction, so a float column writes floats and a decimal
    /// column its texts, as the cell encoder wrote them before.
    let private columnJson (c: Column) : JVal =
        let n = Column.length c
        let mask = Column.validity c
        let present (i: int) = Validity.isPresent i mask

        let values =
            match c.Data with
            | Ints(xs, _) -> List.init n (fun i -> if present i then JInt xs[i] else absentSlot IntType)
            | Floats(xs, _) -> List.init n (fun i -> if present i then JFloat xs[i] else absentSlot FloatType)
            | Bools(xs, _) -> List.init n (fun i -> if present i then JBool xs[i] else absentSlot BoolType)
            | Strs(xs, _)
            | Dates(xs, _)
            | Timestamps(xs, _)
            | Decimals(xs, _) -> List.init n (fun i -> if present i then JStr xs[i] else absentSlot c.Type)

        let validity = List.init n (fun i -> JBool(present i))
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
                        |> Option.defaultValue (Column.ofStrs name Vector.empty Vector.empty)

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

    /// The refusals one present value can meet, for the column `colName` of type `ty` (Phase 417;
    /// `decodeCell`'s until then, now shared by the typed readers below).
    let private valueFaults (colName: string) (ty: ColumnType) =
        let mismatch (v: JVal) =
            Error(TypeMismatch(colName, ty, JVal.kindName v))

        let notCanonical (what: string) =
            Error(MalformedShape(colName + ": " + what))

        mismatch, notCanonical

    /// A date or timestamp text, held to its canonical form.
    let private temporalText
        (notCanonical: string -> Result<string, ColumnError>)
        (ty: ColumnType)
        (isCanonical: string -> bool)
        (form: string)
        (s: string)
        : Result<string, ColumnError> =
        if isCanonical s then
            Ok s
        else
            notCanonical (
                "a "
                + ColumnType.tag ty
                + " value must be canonical ISO-8601 text, "
                + form
                + ", naming a moment that exists"
            )

    /// An epoch number as the canonical timestamp text it names (Phase 94 — models emit epoch
    /// instants against their own correct `"timestamp"` schema; unit by magnitude: ≥ 1e11 ⇒
    /// milliseconds, else seconds — epoch-seconds stay below 1e11 until year 5138), refused where the
    /// instant falls outside the years the canonical form spells (Phase 299).
    let private epochToIso
        (notCanonical: string -> Result<string, ColumnError>)
        (i: int64)
        : Result<string, ColumnError> =
        let secs = if abs i >= 100_000_000_000L then i / 1000L else i
        let iso = isoOfEpochSeconds secs

        if TemporalText.isCanonicalTimestamp iso then
            Ok iso
        else
            notCanonical (
                "the epoch "
                + string i
                + " names an instant outside the years 0000-9999 the canonical timestamp spells"
            )

    // ---- the typed readers (Phase 417; one `decodeCell` answering a `Cell` until then) ----
    // Each reads one present value as an element of its column type's storage, or refuses it: a
    // float column accepts an integer JSON token (lossless widening); a timestamp column accepts an
    // epoch number (`epochToIso`); every other type requires its exact JSON kind, as a
    // `TypeMismatch` naming the kind found.
    //
    // A decimal column (`0.33.0`) reads a STRING of decimal text and canonicalises it, so `12.50`
    // decodes to `Decimal "12.5"`; it reads an integer token, which is exact — whichever
    // constructor the parser chose for it (Phase 299): a token past int32 arrives as a whole-valued
    // `JFloat`, and within the int53 guard its value IS its digits, so `3000000000` decodes as `12`
    // always did. It REFUSES a fractional number token, and a whole-valued one past 2^53, as a
    // `TypeMismatch`: that value has been through a float by the time it arrives here, and a type
    // whose purpose is exactness cannot accept a value it cannot vouch for. An emitter writes a
    // decimal as a string.
    //
    // A date or timestamp column (Phase 299) reads only its canonical ISO-8601 text
    // (`TemporalText`), and an epoch number only where the instant it names falls in the years the
    // canonical form spells (`0000`–`9999`); anything else is a `MalformedShape`.

    let private readInt (colName: string) (v: JVal) : Result<int, ColumnError> =
        let mismatch, _ = valueFaults colName IntType

        match v with
        | JInt i -> Ok i
        | _ -> mismatch v

    let private readFloat (colName: string) (v: JVal) : Result<float, ColumnError> =
        let mismatch, _ = valueFaults colName FloatType

        match v with
        | JFloat f -> Ok f
        | JInt i -> Ok(float i)
        | _ -> mismatch v

    let private readBool (colName: string) (v: JVal) : Result<bool, ColumnError> =
        let mismatch, _ = valueFaults colName BoolType

        match v with
        | JBool b -> Ok b
        | _ -> mismatch v

    let private readStr (colName: string) (v: JVal) : Result<string, ColumnError> =
        let mismatch, _ = valueFaults colName StringType

        match v with
        | JStr s -> Ok s
        | _ -> mismatch v

    let private readDate (colName: string) (v: JVal) : Result<string, ColumnError> =
        let mismatch, notCanonical = valueFaults colName DateType

        match v with
        | JStr s -> temporalText notCanonical DateType TemporalText.isCanonicalDate "YYYY-MM-DD" s
        | _ -> mismatch v

    let private readTimestamp (colName: string) (v: JVal) : Result<string, ColumnError> =
        let mismatch, notCanonical = valueFaults colName TimestampType

        match v with
        | JStr s -> temporalText notCanonical TimestampType TemporalText.isCanonicalTimestamp "YYYY-MM-DDThh:mm:ssZ" s
        // Epoch-seconds fit Int32 (so arrive as JInt); epoch-milliseconds overflow the
        // parser's Int32 path and arrive as a whole-valued JFloat.
        | JInt i -> epochToIso notCanonical (int64 i)
        | JFloat f when f = floor f && abs f < 9e15 -> epochToIso notCanonical (int64 f)
        | _ -> mismatch v

    let private readDecimal (colName: string) (v: JVal) : Result<string, ColumnError> =
        let mismatch, notCanonical = valueFaults colName DecimalType

        match v with
        | JInt i -> Ok(string i)
        | JFloat f when f = floor f && abs f <= int53Max -> Ok(string (int64 f))
        | JStr s ->
            match DecimalText.tryCanonical s with
            | Some canonical -> Ok canonical
            | None ->
                notCanonical
                    "a decimal value must be decimal text — an optional '-', digits, and an optional '.' followed by digits, with no exponent, sign '+', separator or white space"
        | _ -> mismatch v


    let private decodeSchemaEntry (el: JVal) : Result<string * ColumnType, ColumnError> =
        getField "name" el
        |> Result.bind (asStr "schema.name")
        |> Result.bind (fun name ->
            getField "type" el
            |> Result.bind (asStr "schema.type")
            |> Result.bind (fun tag ->
                match ColumnType.ofTag tag with
                | Some ty -> Ok(name, ty)
                | None -> Error(UnknownType(tag, ColumnType.all))))

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

    /// Decode a single named column against its declared type from the `columns` object, straight
    /// into its typed storage (Phase 417): the `values` and `validity` arrays are walked once in
    /// lockstep, a present value read by the type's reader into the vector and an absent one left
    /// as the type's zero with its mask bit clear. The refusals and their order are the `Cell list`
    /// decoder's: the two lengths first (`LengthMismatch`), then row by row a validity entry that is
    /// not a bool (`MalformedShape`) before the value beside it.
    let private decodeColumn (columnsObj: JVal) (name: string) (ty: ColumnType) : Result<Column, ColumnError> =
        match Decode.tryProp name columnsObj with
        | None -> Error(MissingField("columns." + name))
        | Some colEl ->
            columnParts name colEl
            |> Result.bind (fun (values, validity) ->
                if List.length values <> List.length validity then
                    Error(LengthMismatch(name, List.length values, List.length validity))
                else
                    let n = List.length values
                    let mask = Array.zeroCreate<bool> n

                    // Fill `out` through `read`, in row order, stopping at the first refusal.
                    let fill
                        (out: 'T[])
                        (read: JVal -> Result<'T, ColumnError>)
                        : Result<Vector<'T> * Validity, ColumnError> =
                        let mutable fault = None
                        let mutable i = 0
                        let mutable vs = values
                        let mutable ps = validity

                        while fault.IsNone && not vs.IsEmpty do
                            match ps.Head with
                            | JBool present ->
                                if present then
                                    match read vs.Head with
                                    | Ok x ->
                                        out[i] <- x
                                        mask[i] <- true
                                    | Error e -> fault <- Some e
                            | p ->
                                fault <-
                                    Some(MalformedShape(name + ".validity: expected bool, got " + JVal.kindName p))

                            i <- i + 1
                            vs <- vs.Tail
                            ps <- ps.Tail

                        match fault with
                        | Some e -> Error e
                        | None -> Ok(Vector.adopt out, Vector.adopt mask)

                    match ty with
                    | IntType ->
                        fill (Array.zeroCreate n) (readInt name)
                        |> Result.map (fun (v, m) -> Column.ofInts name v m)
                    | FloatType ->
                        fill (Array.zeroCreate n) (readFloat name)
                        |> Result.map (fun (v, m) -> Column.ofFloats name v m)
                    | BoolType ->
                        fill (Array.zeroCreate n) (readBool name)
                        |> Result.map (fun (v, m) -> Column.ofBools name v m)
                    | StringType ->
                        fill (Array.create n "") (readStr name)
                        |> Result.map (fun (v, m) -> Column.ofStrs name v m)
                    | DateType ->
                        fill (Array.create n "") (readDate name)
                        |> Result.map (fun (v, m) -> Column.ofDates name v m)
                    | TimestampType ->
                        fill (Array.create n "") (readTimestamp name)
                        |> Result.map (fun (v, m) -> Column.ofTimestamps name v m)
                    | DecimalType ->
                        fill (Array.create n DecimalText.zero) (readDecimal name)
                        |> Result.map (fun (v, m) -> Column.ofDecimals name v m))

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
            + String.concat ", " (expected |> List.map ColumnType.tag)
        | TypeMismatch(col, expected, got) ->
            "column '"
            + col
            + "': expected "
            + ColumnType.tag expected
            + " value, got "
            + got
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
