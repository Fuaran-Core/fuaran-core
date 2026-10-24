module Fuaran.Core.Tests.ColumnOracle

// ============================================================================
//  Phase 563 — the columnar codec's model (`proofs/WireColumn.fst`, ladder rows
//  `column-codec-differential` and `field-codec-differential`) as the exporters ask it, and the bridges
//  the `Proofs.Oracle` differentials share with them: the column types and cells, the tables with and
//  without field metadata, the refusal classes, and the unit text (`proofs/Unit.fst`). Moved from the
//  test project (`ProofOracle.Bridges.fs`, `ProofOracle.WireColumnTests.fs`), so each is defined once.
// ============================================================================

// Aliased before `Fuaran.Core` is opened: production's `Unit` module shares the model's name.
module ModelUnit = Unit

open Fuaran.Core
open Fuaran.Core.Tests.VectorOracle
open Fuaran.Core.Tests.CanonOracle

/// A unit's text as the unit model reads it, and back.
module Units =

    /// A character of a unit's text as the model's symbol; anything outside the alphabet is `Other`.
    let uchOf (c: char) : ModelUnit.uch =
        match c with
        | 'a' -> ModelUnit.La
        | 'b' -> ModelUnit.Lb
        | 'c' -> ModelUnit.Lc
        | 'd' -> ModelUnit.Ld
        | 'e' -> ModelUnit.Le
        | 'f' -> ModelUnit.Lf
        | 'g' -> ModelUnit.Lg
        | 'h' -> ModelUnit.Lh
        | 'i' -> ModelUnit.Li
        | 'j' -> ModelUnit.Lj
        | 'k' -> ModelUnit.Lk
        | 'l' -> ModelUnit.Ll
        | 'm' -> ModelUnit.Lm
        | 'n' -> ModelUnit.Ln
        | 'o' -> ModelUnit.Lo
        | 'p' -> ModelUnit.Lp
        | 'q' -> ModelUnit.Lq
        | 'r' -> ModelUnit.Lr
        | 's' -> ModelUnit.Ls
        | 't' -> ModelUnit.Lt
        | 'u' -> ModelUnit.Lu
        | 'v' -> ModelUnit.Lv
        | 'w' -> ModelUnit.Lw
        | 'x' -> ModelUnit.Lx
        | 'y' -> ModelUnit.Ly
        | 'z' -> ModelUnit.Lz
        | 'A' -> ModelUnit.UA
        | 'B' -> ModelUnit.UB
        | 'C' -> ModelUnit.UC
        | 'D' -> ModelUnit.UD
        | 'E' -> ModelUnit.UE
        | 'F' -> ModelUnit.UF
        | 'G' -> ModelUnit.UG
        | 'H' -> ModelUnit.UH
        | 'I' -> ModelUnit.UI
        | 'J' -> ModelUnit.UJ
        | 'K' -> ModelUnit.UK
        | 'L' -> ModelUnit.UL
        | 'M' -> ModelUnit.UM
        | 'N' -> ModelUnit.UN
        | 'O' -> ModelUnit.UO
        | 'P' -> ModelUnit.UP
        | 'Q' -> ModelUnit.UQ
        | 'R' -> ModelUnit.UR
        | 'S' -> ModelUnit.US
        | 'T' -> ModelUnit.UT
        | 'U' -> ModelUnit.UU
        | 'V' -> ModelUnit.UV
        | 'W' -> ModelUnit.UW
        | 'X' -> ModelUnit.UX
        | 'Y' -> ModelUnit.UY
        | 'Z' -> ModelUnit.UZ
        | '0' -> ModelUnit.D0
        | '1' -> ModelUnit.D1
        | '2' -> ModelUnit.D2
        | '3' -> ModelUnit.D3
        | '4' -> ModelUnit.D4
        | '5' -> ModelUnit.D5
        | '6' -> ModelUnit.D6
        | '7' -> ModelUnit.D7
        | '8' -> ModelUnit.D8
        | '9' -> ModelUnit.D9
        | '.' -> ModelUnit.Dot
        | '/' -> ModelUnit.Slash
        | '(' -> ModelUnit.LPar
        | ')' -> ModelUnit.RPar
        | '[' -> ModelUnit.LBr
        | ']' -> ModelUnit.RBr
        | '{' -> ModelUnit.LCur
        | '}' -> ModelUnit.RCur
        | '%' -> ModelUnit.Pct
        | '_' -> ModelUnit.Under
        | ''' -> ModelUnit.Apos
        | '-' -> ModelUnit.Minus
        | '+' -> ModelUnit.Plus
        | '*' -> ModelUnit.Star
        | '^' -> ModelUnit.Caret
        | _ -> ModelUnit.Other

    /// The model's symbol as a character; `Other` has none and is written `?`.
    let charOf (c: ModelUnit.uch) : char =
        match c with
        | ModelUnit.La -> 'a'
        | ModelUnit.Lb -> 'b'
        | ModelUnit.Lc -> 'c'
        | ModelUnit.Ld -> 'd'
        | ModelUnit.Le -> 'e'
        | ModelUnit.Lf -> 'f'
        | ModelUnit.Lg -> 'g'
        | ModelUnit.Lh -> 'h'
        | ModelUnit.Li -> 'i'
        | ModelUnit.Lj -> 'j'
        | ModelUnit.Lk -> 'k'
        | ModelUnit.Ll -> 'l'
        | ModelUnit.Lm -> 'm'
        | ModelUnit.Ln -> 'n'
        | ModelUnit.Lo -> 'o'
        | ModelUnit.Lp -> 'p'
        | ModelUnit.Lq -> 'q'
        | ModelUnit.Lr -> 'r'
        | ModelUnit.Ls -> 's'
        | ModelUnit.Lt -> 't'
        | ModelUnit.Lu -> 'u'
        | ModelUnit.Lv -> 'v'
        | ModelUnit.Lw -> 'w'
        | ModelUnit.Lx -> 'x'
        | ModelUnit.Ly -> 'y'
        | ModelUnit.Lz -> 'z'
        | ModelUnit.UA -> 'A'
        | ModelUnit.UB -> 'B'
        | ModelUnit.UC -> 'C'
        | ModelUnit.UD -> 'D'
        | ModelUnit.UE -> 'E'
        | ModelUnit.UF -> 'F'
        | ModelUnit.UG -> 'G'
        | ModelUnit.UH -> 'H'
        | ModelUnit.UI -> 'I'
        | ModelUnit.UJ -> 'J'
        | ModelUnit.UK -> 'K'
        | ModelUnit.UL -> 'L'
        | ModelUnit.UM -> 'M'
        | ModelUnit.UN -> 'N'
        | ModelUnit.UO -> 'O'
        | ModelUnit.UP -> 'P'
        | ModelUnit.UQ -> 'Q'
        | ModelUnit.UR -> 'R'
        | ModelUnit.US -> 'S'
        | ModelUnit.UT -> 'T'
        | ModelUnit.UU -> 'U'
        | ModelUnit.UV -> 'V'
        | ModelUnit.UW -> 'W'
        | ModelUnit.UX -> 'X'
        | ModelUnit.UY -> 'Y'
        | ModelUnit.UZ -> 'Z'
        | ModelUnit.D0 -> '0'
        | ModelUnit.D1 -> '1'
        | ModelUnit.D2 -> '2'
        | ModelUnit.D3 -> '3'
        | ModelUnit.D4 -> '4'
        | ModelUnit.D5 -> '5'
        | ModelUnit.D6 -> '6'
        | ModelUnit.D7 -> '7'
        | ModelUnit.D8 -> '8'
        | ModelUnit.D9 -> '9'
        | ModelUnit.Dot -> '.'
        | ModelUnit.Slash -> '/'
        | ModelUnit.LPar -> '('
        | ModelUnit.RPar -> ')'
        | ModelUnit.LBr -> '['
        | ModelUnit.RBr -> ']'
        | ModelUnit.LCur -> '{'
        | ModelUnit.RCur -> '}'
        | ModelUnit.Pct -> '%'
        | ModelUnit.Under -> '_'
        | ModelUnit.Apos -> '''
        | ModelUnit.Minus -> '-'
        | ModelUnit.Plus -> '+'
        | ModelUnit.Star -> '*'
        | ModelUnit.Caret -> '^'
        | ModelUnit.Other -> '?'

    let toText (s: string) : ModelUnit.text = s |> Seq.map uchOf |> List.ofSeq

    let ofText (t: ModelUnit.text) : string =
        t |> List.map charOf |> Array.ofList |> System.String

    /// Production's unit as the model's, through its canonical text — a refusal here is a real
    /// defect: the model declines a text production wrote as canonical.
    let toModel (u: UnitOfMeasure) : ModelUnit.uom =
        match ModelUnit.parse (toText (Unit.render u)) with
        | ModelUnit.Ok m -> m
        | ModelUnit.Refused r -> failwithf "the model refused production's canonical text %s: %A" (Unit.render u) r

    let modelRender (m: ModelUnit.uom) : string = ofText (ModelUnit.render m)

/// The model's host record: what a host computes about a number and about temporal text.
/// `finite` is `System.Double.IsFinite`, not production's `JVal.nonFiniteToken`, so the model
/// is not handed the function it is compared with.
let host: WireColumn.host<int, float> =
    { to_float = float
      int_text = fun i -> canonToChs (string i)
      finite = System.Double.IsFinite
      zero_int = 0
      zero_float = 0.0 }

/// The column types the list model spells: since Phase 430 every one of `ColumnType.all`, the
/// four timestamp units included (the calendar is `Temporal.fst`'s, no longer a host field).
let modelTypes: ColumnType list = ColumnType.all

/// `TimeUnit` as `Temporal.fst` spells it, and back.
let toModelUnit (u: TimeUnit) : Temporal.time_unit =
    match u with
    | TimeUnit.Seconds -> Temporal.Seconds
    | TimeUnit.Milliseconds -> Temporal.Milliseconds
    | TimeUnit.Microseconds -> Temporal.Microseconds
    | TimeUnit.Nanoseconds -> Temporal.Nanoseconds

let ofModelUnit (u: Temporal.time_unit) : TimeUnit =
    match u with
    | Temporal.Seconds -> TimeUnit.Seconds
    | Temporal.Milliseconds -> TimeUnit.Milliseconds
    | Temporal.Microseconds -> TimeUnit.Microseconds
    | Temporal.Nanoseconds -> TimeUnit.Nanoseconds

let toModelType (t: ColumnType) : WireColumn.column_type =
    match t with
    | IntType -> WireColumn.IntType
    | FloatType -> WireColumn.FloatType
    | BoolType -> WireColumn.BoolType
    | StringType -> WireColumn.StringType
    | DateType -> WireColumn.DateType
    | TimestampType u -> WireColumn.TimestampType(toModelUnit u)
    | DecimalType -> WireColumn.DecimalType

let ofModelType (t: WireColumn.column_type) : ColumnType =
    match t with
    | WireColumn.IntType -> IntType
    | WireColumn.FloatType -> FloatType
    | WireColumn.BoolType -> BoolType
    | WireColumn.StringType -> StringType
    | WireColumn.DateType -> DateType
    | WireColumn.TimestampType u -> TimestampType(ofModelUnit u)
    | WireColumn.DecimalType -> DecimalType

let toModelCell (c: Cell) : WireColumn.cell<int, float> =
    match c with
    | Int i -> WireColumn.Int i
    | Float f -> WireColumn.Float f
    | Bool b -> WireColumn.Bool b
    | Str s -> WireColumn.Str(canonToChs s)
    | Date s -> WireColumn.Date(canonToChs s)
    | Timestamp s -> WireColumn.Timestamp(canonToChs s)
    | Null -> WireColumn.Null
    | Decimal s -> WireColumn.Decimal(canonToChs s)

let ofModelCell (c: WireColumn.cell<int, float>) : Cell =
    match c with
    | WireColumn.Int i -> Int i
    | WireColumn.Float f -> Float f
    | WireColumn.Bool b -> Bool b
    | WireColumn.Str s -> Str(canonFromChs s)
    | WireColumn.Date s -> Date(canonFromChs s)
    | WireColumn.Timestamp s -> Timestamp(canonFromChs s)
    | WireColumn.Null -> Null
    | WireColumn.Decimal s -> Decimal(canonFromChs s)

let toModelTable (t: Table) : WireColumn.table<int, float> =
    // Phase 427: the model's schema entry is a name and a type; a field's metadata (its unit,
    // label, description and extension members) is OUTSIDE the model until Phase 428 extends it.
    { schema = t.Schema |> List.map (fun f -> canonToChs f.Name, toModelType f.Type)
      columns =
        t.Columns
        |> List.map (fun c ->
            ({ name = canonToChs c.Name
               ctype = toModelType c.Type
               cells = Column.toCells c |> List.map toModelCell }
            : WireColumn.column<int, float>)) }

/// The model's table as production's. Used for the model's NORMAL FORM of a table production
/// already validated, so every column's cells fit its type and construction cannot refuse: a
/// refusal here is a real defect, in the model's normal form or in `Column.ofCells`.
let ofModelTable (t: WireColumn.table<int, float>) : Table =
    { Schema =
        t.schema
        |> List.map (fun (n, ty) -> Field.create (canonFromChs n) (ofModelType ty))
      Columns =
        t.columns
        |> List.map (fun c ->
            let name = canonFromChs c.name

            match Column.ofCells name (ofModelType c.ctype) (c.cells |> List.map ofModelCell) with
            | Result.Ok col -> col
            | Result.Error e -> failwithf "the model's column %s did not build as production's: %A" name e) }

let toModelSource (src: DataSource) : WireColumn.data_source<int, float> =
    match src with
    | Embedded t -> WireColumn.Embedded(toModelTable t)
    | Ref r -> WireColumn.Ref(canonToChs r)

let ofModelSource (src: WireColumn.data_source<int, float>) : DataSource =
    match src with
    | WireColumn.Embedded t -> Embedded(ofModelTable t)
    | WireColumn.Ref r -> Ref(canonFromChs r)

/// A refusal as the CLASS the model keeps: the case, the column where the case names one, and
/// for a `Malformed` which of the five faults it is. The model drops production's prose and its
/// counts (it carries no integers), so that is what the two sides can be held to.
let prodClass (e: ColumnError) : string =
    match e with
    | NotJson _ -> "NotJson"
    | MissingField f -> "MissingField " + f
    | MalformedShape _ -> "MalformedShape"
    | UnknownType(got, _) -> "UnknownType " + got
    | TypeMismatch(col, expected, _) -> "TypeMismatch " + col + " " + ColumnType.tag expected
    | LengthMismatch(col, _, _) -> "LengthMismatch " + col
    | NonFiniteFloat(col, _) -> "NonFiniteFloat " + col
    | RaggedColumns(col, _, _) -> "RaggedColumns " + col
    | Malformed d ->
        if d.StartsWith "duplicate schema name: " then
            "Malformed " + d
        elif d.StartsWith "duplicate column name: " then
            "Malformed " + d
        elif d.StartsWith "duplicate column key in \"columns\": " then
            "Malformed " + d
        elif d.StartsWith "schema names with no column: " then
            "Malformed schema names with no column"
        elif d.StartsWith "columns absent from the schema: " then
            "Malformed columns absent from the schema"
        else
            "Malformed <unrecognised: " + d + ">"

let modelClass (e: WireColumn.column_error) : string =
    match e with
    | WireColumn.MissingField f -> "MissingField " + canonFromChs f
    | WireColumn.MissingColumn c -> "MissingField columns." + canonFromChs c
    | WireColumn.MalformedShape -> "MalformedShape"
    | WireColumn.UnknownType got -> "UnknownType " + canonFromChs got
    | WireColumn.TypeMismatch(col, expected) ->
        "TypeMismatch " + canonFromChs col + " " + ColumnType.tag (ofModelType expected)
    | WireColumn.LengthMismatch col -> "LengthMismatch " + canonFromChs col
    | WireColumn.NonFiniteFloat col -> "NonFiniteFloat " + canonFromChs col
    | WireColumn.RaggedColumns col -> "RaggedColumns " + canonFromChs col
    | WireColumn.OutOfModel -> "OutOfModel"
    | WireColumn.Malformed fault ->
        match fault with
        | WireColumn.DuplicateSchemaName n -> "Malformed duplicate schema name: " + canonFromChs n
        | WireColumn.DuplicateColumnName n -> "Malformed duplicate column name: " + canonFromChs n
        | WireColumn.DuplicateColumnKey k -> "Malformed duplicate column key in \"columns\": " + canonFromChs k
        | WireColumn.SchemaNameWithoutColumn -> "Malformed schema names with no column"
        | WireColumn.ColumnOutsideSchema -> "Malformed columns absent from the schema"

// ---- fields, and tables carrying them (`WireColumn.fst` section 10, Phase 428) ----

/// Production's field as the model's: the unit through `Units.toModel`, the extension map in
/// the key order `Map.toList` answers. `reverseExt` is the go-red's dial.
let toModelFieldWith (reverseExt: bool) (f: Field) : WireColumn.field =
    let ext = f.Ext |> Map.toList |> List.map (fun (k, v) -> canonToChs k, canonToChs v)

    { fname = canonToChs f.Name
      fty = toModelType f.Type
      funit = f.Unit |> Option.map Units.toModel |> toMOpt
      flabel = f.Label |> Option.map canonToChs |> toMOpt
      fdesc = f.Description |> Option.map canonToChs |> toMOpt
      fext = (if reverseExt then List.rev ext else ext) }

let toModelField (f: Field) : WireColumn.field = toModelFieldWith false f

/// The model's field as production's — the unit through its canonical text, the extension
/// members through `withExt` in the list's order.
let ofModelField (f: WireColumn.field) : Field =
    let unit =
        match ofMOpt f.funit with
        | Some m ->
            match Unit.parse (Units.modelRender m) with
            | Ok u -> Some u
            | Error e -> failwithf "production refused the model's unit text %s: %A" (Units.modelRender m) e
        | None -> None

    let f0 = Field.create (canonFromChs f.fname) (ofModelType f.fty)

    let f1 =
        (match unit with
         | Some u -> Field.withUnit u f0
         | None -> f0)

    let f2 =
        (match ofMOpt f.flabel with
         | Some l -> Field.withLabel (canonFromChs l) f1
         | None -> f1)

    let f3 =
        match ofMOpt f.fdesc with
        | Some d -> Field.withDescription (canonFromChs d) f2
        | None -> f2

    f.fext
    |> List.fold (fun g (k, v) -> Field.withExt (canonFromChs k) (canonFromChs v) g) f3

/// A table with its fields' metadata, as the model's `table_f`.
let toModelTableF (t: Table) : WireColumn.table_f<int, float> =
    let m = toModelTable t

    { fschema = t.Schema |> List.map toModelField
      fcolumns = m.columns }

let ofModelTableF (m: WireColumn.table_f<int, float>) : Table =
    let stripped = ofModelTable (WireColumn.strip m)

    { stripped with
        Schema = m.fschema |> List.map ofModelField }


// ---------------------------------------------------------------------------
//  The codec vectors, as the exporters ask the model (Phase 563)
// ---------------------------------------------------------------------------

[<Literal>]
let Model = "WireColumn"

/// The ladder row holding the table codec to production.
[<Literal>]
let CodecRow = "column-codec-differential"

/// The ladder row holding the field codec to production.
[<Literal>]
let FieldRow = "field-codec-differential"

/// A codec outcome as the vectors record one: the canonical bytes of what was read or built, or the
/// refusal's class — a `ColumnError` case name, `NotJson/<kind>` for a parse refusal.
type Outcome = Result<string, string>

/// A model's refusal as the class the vectors name: the case alone, as production's is.
let private refusalClass (e: WireColumn.column_error) : string = (modelClass e).Split(' ')[0]

/// The model's canonical bytes for a source: `encode_json_f`, written by the one canonical writer.
let private canonical (src: WireColumn.data_source_f<int, float>) : string =
    Canonical.write Rendering.CrossHost (canonOfModel (WireColumn.encode_json_f host src))

let private declined (row: string) : Asked =
    Declined(
        Model,
        row,
        "the model answers OutOfModel: the input reaches a lenient-ingest arm of the decoder — a JSON number where a decimal or timestamp column holds text, a column written as a bare array or without its validity, or a schema inferred from the columns"
    )

/// `text` decoded as a `DataSource` document, as the vectors' `codecDecode` asks it: read by the parser
/// model (`JsonParse`), whose refusal is the codec's `NotJson` and the parser's answer; then decoded by
/// `decode_json_f` and re-encoded. `render` is the exporter's one renderer of an outcome.
let decode (row: string) (render: Outcome -> CorpusCell) (text: string) : Asked =
    match JsonModel.readStrict text with
    | Error kind ->
        Answered
            { Model = JsonModel.Model
              LadderRow = JsonModel.LadderRow
              Cell = render (Error("NotJson/" + kind)) }
    | Ok jv ->
        match WireColumn.decode_json_f host (canonToModel jv) with
        | WireColumn.Bad WireColumn.OutOfModel -> declined row
        | WireColumn.Bad e ->
            Answered
                { Model = Model
                  LadderRow = row
                  Cell = render (Error(refusalClass e)) }
        | WireColumn.Good src ->
            Answered
                { Model = Model
                  LadderRow = row
                  Cell = render (Ok(canonical src)) }

/// The guarded encode of `table`, as the vectors' `codecEncode` asks it: `validate_f`, then
/// `encode_json_f`.
let encode (render: Outcome -> CorpusCell) (table: Table) : Asked =
    let m = toModelTableF table

    match WireColumn.validate_f host m with
    | WireColumn.Bad WireColumn.OutOfModel -> declined CodecRow
    | WireColumn.Bad e ->
        Answered
            { Model = Model
              LadderRow = CodecRow
              Cell = render (Error(refusalClass e)) }
    | WireColumn.Good _ ->
        Answered
            { Model = Model
              LadderRow = CodecRow
              Cell = render (Ok(canonical (WireColumn.Embedded_f m))) }

/// One field's schema entry, as the vectors' `fieldEncode` asks it: `field_json`, written by the one
/// canonical writer.
let fieldEntry (f: Field) : string =
    Canonical.write Rendering.CrossHost (canonOfModel (WireColumn.field_json (toModelField f)))
