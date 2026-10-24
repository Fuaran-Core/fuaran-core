module Fuaran.Core.Tests.ProofOracle.QueryTests

// The `Proofs.Oracle` differential for `proofs/Query.fst`: the extracted model
// (`proofs/oracle/Query.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

// Phase 177 — the extracted FUNCTION-SEAM model, bound the same way and for the same reason:
// production's `Fuaran.Core.Capability` module shares the model's name once `Fuaran.Core` is open.
module ModelCap = Capability

// Phase 187 — the extracted DATA-ACQUISITION-SEAM model, bound the same way and for the same
// reason: production's `Fuaran.Core.Query` module shares the model's name once `Fuaran.Core` is
// open.
module ModelQuery = Query

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges
open Fuaran.Core.Tests.CapabilityOracle

// ------------------------------------------------------------------------------------------
// Phase 187 — the DATA-ACQUISITION SEAM. `proofs/Query.fst` models `Fuaran.Core.Query`'s
// `validateParams` / `invoke` / `invocationKey` and `QueryRegistry.register` / `enumerate` /
// `tryFind` / `dispatch` clause for clause, over an abstract resolver and a `renderers` record;
// `proofs/oracle/Query.fs` is that model extracted. This runs it BESIDE production over the
// generator `Conformance.queryLaws` uses (`ConfRng`), WIDENED from that family's one fixed
// declaration: registries drawn from a three-id pool (so a duplicate registration arises),
// declarations of up to three params over all six column types with names drawn WITH
// replacement (so a repeated param name arises, and first-wins is compared), and argument sets
// that bind, skip, null, mistype, repeat and reverse — with an INSTRUMENTED resolver on each side
// that must have run on both or on neither, and never past a refusal that is not its own.
//
// The model's result payload is abstract, so it is instantiated at production's own
// `QueryResult` and a settled page crosses untranslated. The four renderers are production's own
// calls: `string` on an int, `FloatLayout.canonical`, `Hash.fnv1a`, and the ordinal order F#'s
// `List.sortBy fst` compares strings by. A float cell crosses as its round-trip `R` text.
// ------------------------------------------------------------------------------------------

/// The query model's unit, restated in its own module (Phase 430).
let private qUnitToModel (u: TimeUnit) : ModelQuery.time_unit =
    match u with
    | TimeUnit.Seconds -> ModelQuery.Seconds
    | TimeUnit.Milliseconds -> ModelQuery.Milliseconds
    | TimeUnit.Microseconds -> ModelQuery.Microseconds
    | TimeUnit.Nanoseconds -> ModelQuery.Nanoseconds

let private qUnitOfModel (u: ModelQuery.time_unit) : TimeUnit =
    match u with
    | ModelQuery.Seconds -> TimeUnit.Seconds
    | ModelQuery.Milliseconds -> TimeUnit.Milliseconds
    | ModelQuery.Microseconds -> TimeUnit.Microseconds
    | ModelQuery.Nanoseconds -> TimeUnit.Nanoseconds

let private qColToModel (t: ColumnType) : ModelQuery.column_type =
    match t with
    | IntType -> ModelQuery.IntType
    | FloatType -> ModelQuery.FloatType
    | BoolType -> ModelQuery.BoolType
    | StringType -> ModelQuery.StringType
    | DateType -> ModelQuery.DateType
    | TimestampType u -> ModelQuery.TimestampType(qUnitToModel u)
    | DecimalType -> ModelQuery.DecimalType

let private qColTag (t: ColumnType) : string =
    match t with
    | IntType -> "int"
    | FloatType -> "float"
    | BoolType -> "bool"
    | StringType -> "string"
    | DateType -> "date"
    | TimestampType u -> ColumnType.tag (TimestampType u)
    | DecimalType -> "decimal"

let private qModelColTag (t: ModelQuery.column_type) : string =
    match t with
    | ModelQuery.IntType -> "int"
    | ModelQuery.FloatType -> "float"
    | ModelQuery.BoolType -> "bool"
    | ModelQuery.StringType -> "string"
    | ModelQuery.DateType -> "date"
    | ModelQuery.TimestampType u -> ColumnType.tag (TimestampType(qUnitOfModel u))
    | ModelQuery.DecimalType -> "decimal"

let private qCellToModel (c: Cell) : ModelQuery.cell =
    match c with
    | Cell.Int v -> ModelQuery.Int(bigint v)
    | Cell.Float v -> ModelQuery.Float(v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
    | Cell.Bool v -> ModelQuery.Bool v
    | Cell.Str v -> ModelQuery.Str v
    | Cell.Date v -> ModelQuery.Date v
    // Phase 430: a timestamp crosses with the unit `Cell.typeOf` names for it — the coarsest unit
    // holding its instant, `TemporalText.unitOf` — beside its text; the model reads the unit as the
    // cell's type and the text as its payload (its header says why).
    | Cell.Timestamp v -> ModelQuery.Timestamp(qUnitToModel (TemporalText.unitOf v), v)
    | Cell.Decimal v -> ModelQuery.Decimal v
    | Cell.Null -> ModelQuery.Null

let private qArgsToModel (args: (string * Cell) list) : ModelQuery.arguments =
    args |> List.map (fun (n, c) -> n, qCellToModel c)

let private qHostToModel (h: HostEffect) : ModelQuery.host_effect =
    match h with
    | Pure -> ModelQuery.Pure
    | ReadsHost -> ModelQuery.ReadsHost
    | WritesHost -> ModelQuery.WritesHost

let private qDetToModel (d: DeterminismSource) : ModelQuery.determinism_source =
    { ModelQuery.determinism_source.has_clock = Set.contains ClockFactor d
      ModelQuery.determinism_source.has_random = Set.contains RandomFactor d
      ModelQuery.determinism_source.has_network = Set.contains NetworkFactor d }


/// A filter predicate as the model reads it (Phase 398).
let private qPredToModel (p: ColumnPredicate) : ModelQuery.predicate =
    match p with
    | ColumnPredicate.EqualTo(c, v) -> ModelQuery.EqualTo(c, qCellToModel v)
    | ColumnPredicate.GreaterThan(c, v) -> ModelQuery.GreaterThan(c, qCellToModel v)
    | ColumnPredicate.AtLeast(c, v) -> ModelQuery.AtLeast(c, qCellToModel v)
    | ColumnPredicate.LessThan(c, v) -> ModelQuery.LessThan(c, qCellToModel v)
    | ColumnPredicate.AtMost(c, v) -> ModelQuery.AtMost(c, qCellToModel v)
    | ColumnPredicate.Contains(c, t) -> ModelQuery.Contains(c, t)
    | ColumnPredicate.IsNull c -> ModelQuery.IsNull c
    | ColumnPredicate.IsNotNull c -> ModelQuery.IsNotNull c

/// An order key as the model reads it (Phase 398).
let private qKeyToModel (k: SortKey) : ModelQuery.sort_key =
    { ModelQuery.sort_key.k_column = k.Column
      ModelQuery.sort_key.k_direction =
        match k.Direction with
        | SortDirection.Ascending -> ModelQuery.Ascending
        | SortDirection.Descending -> ModelQuery.Descending }

/// A declaration as the model reads it. `keepRequired = false` is the FORGETFUL bridge the
/// go-red uses: every param crosses as optional, so the model stops seeing step 2.
let private queryToModelWith (keepRequired: bool) (q: Query) : ModelQuery.query =
    { ModelQuery.query.q_id = q.Id
      ModelQuery.query.q_params =
        q.Params
        |> List.map (fun p ->
            { ModelQuery.query_param.p_name = p.Name
              ModelQuery.query_param.p_type = qColToModel p.Type
              ModelQuery.query_param.p_required = keepRequired && p.Required })
      ModelQuery.query.q_schema = q.ResultSchema |> List.map (fun f -> f.Name, qColToModel f.Type)
      ModelQuery.query.q_effect =
        { ModelQuery.effect_class.host = qHostToModel q.Effect.Host
          ModelQuery.effect_class.determinism = qDetToModel q.Effect.Determinism }
      ModelQuery.query.q_source = sprintf "%A" q.Source
      ModelQuery.query.q_timeout_ms = toMOpt (Option.map (fun (n: int) -> bigint n) q.TimeoutMs)
      ModelQuery.query.q_page_size = toMOpt (Option.map (fun (n: int) -> bigint n) q.PageSize)
      ModelQuery.query.q_where = q.Where |> List.map qPredToModel
      ModelQuery.query.q_order_by = q.OrderBy |> List.map qKeyToModel }

let private queryToModel (q: Query) : ModelQuery.query = queryToModelWith true q

// ---- Phase 398: the shape renderers are production's own codec ----

/// A model cell back as production's: the inverse of `qCellToModel` (a float's carrier is its
/// round-trip text).
let private qCellOfModel (c: ModelQuery.cell) : Cell =
    match c with
    | ModelQuery.Int v -> Cell.Int(int v)
    | ModelQuery.Float v -> Cell.Float(System.Double.Parse(v, System.Globalization.CultureInfo.InvariantCulture))
    | ModelQuery.Bool v -> Cell.Bool v
    | ModelQuery.Str v -> Cell.Str v
    | ModelQuery.Date v -> Cell.Date v
    | ModelQuery.Timestamp(_, v) -> Cell.Timestamp v
    | ModelQuery.Decimal v -> Cell.Decimal v
    | ModelQuery.Null -> Cell.Null

let private qPredOfModel (p: ModelQuery.predicate) : ColumnPredicate =
    match p with
    | ModelQuery.EqualTo(c, v) -> ColumnPredicate.EqualTo(c, qCellOfModel v)
    | ModelQuery.GreaterThan(c, v) -> ColumnPredicate.GreaterThan(c, qCellOfModel v)
    | ModelQuery.AtLeast(c, v) -> ColumnPredicate.AtLeast(c, qCellOfModel v)
    | ModelQuery.LessThan(c, v) -> ColumnPredicate.LessThan(c, qCellOfModel v)
    | ModelQuery.AtMost(c, v) -> ColumnPredicate.AtMost(c, qCellOfModel v)
    | ModelQuery.Contains(c, t) -> ColumnPredicate.Contains(c, t)
    | ModelQuery.IsNull c -> ColumnPredicate.IsNull c
    | ModelQuery.IsNotNull c -> ColumnPredicate.IsNotNull c

let private qKeyOfModel (k: ModelQuery.sort_key) : SortKey =
    { Column = k.k_column
      Direction =
        match k.k_direction with
        | ModelQuery.Ascending -> SortDirection.Ascending
        | ModelQuery.Descending -> SortDirection.Descending }

/// The declaration a shape renderer encodes: everything but the member it reads is fixed.
let private shapeCarrier: Query =
    { Id = "shape"
      Params = []
      ResultSchema = []
      Effect = Effect.pureDeterministic
      Source = Ref "shape"
      TimeoutMs = None
      PageSize = None
      Where = []
      OrderBy = [] }

/// The canonical text production's codec writes for one member of a declaration — the member the
/// capture key's shape fields carry (`QueryShape.keyFields` renders the same array through the same
/// `Canonical.write Rendering.CrossHost`). Read off the PUBLIC codec, so the renderer is production's own call, not a copy.
let private memberText (name: string) (q: Query) : string =
    match Json.parse (QueryCodec.encode q) with
    | Ok(JObj ms) ->
        match ms |> List.tryFind (fun (k, _) -> k = name) with
        | Some(_, v) -> Canonical.write Rendering.CrossHost v
        | None -> ""
    | _ -> ""

/// Production's own seven calls. `compare` on two F# strings is ordinal, which is what
/// `List.sortBy fst` sorts a name by; `field` is the Phase 225 canonicaliser both seams share; the
/// shape renderers (Phase 398) are the declaration codec's `where` and `orderBy` members.
let private queryRenderers: ModelQuery.renderers =
    { ModelQuery.renderers.render_int = fun (n: bigint) -> string (int n)
      ModelQuery.renderers.render_float =
        fun (s: string) ->
            FloatLayout.canonical (System.Double.Parse(s, System.Globalization.CultureInfo.InvariantCulture))
      ModelQuery.renderers.hash = Hash.fnv1a
      ModelQuery.renderers.name_le = fun (a: string) (b: string) -> System.String.CompareOrdinal(a, b) <= 0
      ModelQuery.renderers.field = Hash.canonicalField
      ModelQuery.renderers.render_where =
        fun (w: ModelQuery.predicate list) ->
            memberText
                "where"
                { shapeCarrier with
                    Where = w |> List.map qPredOfModel }
      ModelQuery.renderers.render_order =
        fun (o: ModelQuery.sort_key list) ->
            memberText
                "orderBy"
                { shapeCarrier with
                    OrderBy = o |> List.map qKeyOfModel } }

/// The ORDER-BLIND comparator the second go-red uses: everything is below everything, so the
/// model's insertion sort leaves the caller's order standing.
let private orderBlindRenderers: ModelQuery.renderers =
    { queryRenderers with
        name_le = fun (_: string) (_: string) -> true }

// ---- Phase 225: the capture key's pre-image is injective, in both seams ----

/// A DECODER for `Hash.canonicalFields`, written here from the encoding's definition rather than
/// from production: `U+0010` makes the next character literal (it may only precede `U+0010` or
/// `U+0001`), and the first unescaped `U+0001` ends a field. `decodeFields (canonicalFields l) =
/// Some l` for every `l` is the injectivity the models' `field_faithful` premise buys, measured
/// on the shipped function rather than assumed of it.
let private decodeFields (s: string) : string list option =
    let sb = System.Text.StringBuilder()

    let rec go (i: int) (acc: string list) : string list option =
        if i = s.Length then
            if sb.Length = 0 then Some(List.rev acc) else None
        else
            let c = s[i]

            if c = '\u0010' then
                if i + 1 < s.Length && (s[i + 1] = '\u0010' || s[i + 1] = '\u0001') then
                    sb.Append(s[i + 1]) |> ignore
                    go (i + 2) acc
                else
                    None
            elif c = '\u0001' then
                let f = sb.ToString()
                sb.Clear() |> ignore
                go (i + 1) (f :: acc)
            else
                sb.Append(c) |> ignore
                go (i + 1) acc

    go 0 []

/// The adversarial alphabet the round trip draws fields from: both symbols of the encoding, the
/// two characters every pre-225 join spliced on (`=` and `#`), a non-ASCII character, and the
/// empty string, so an empty field and an empty list are both drawn.
let private adversarialPieces =
    [| "a"; "b"; "="; "#"; "\u0001"; "\u0010"; "∅"; "" |]

let private genAdversarialField (r: ConfRng.T) : string * ConfRng.T =
    let n, r1 = ConfRng.intBelow 5 r
    let mutable rng = r1
    let sb = System.Text.StringBuilder()

    for _ in 1..n do
        let k, r2 = ConfRng.intBelow adversarialPieces.Length rng
        rng <- r2
        sb.Append(adversarialPieces[k]) |> ignore

    sb.ToString(), rng

let private genAdversarialFields (r: ConfRng.T) : string list * ConfRng.T =
    let n, r1 = ConfRng.intBelow 5 r
    let mutable rng = r1

    let fields =
        [ for _ in 1..n do
              let f, r2 = genAdversarialField rng
              rng <- r2
              f ]

    fields, rng

let private prodQueryErrRender (e: QueryError) : string =
    match e with
    | NoSuchQuery(id, known) -> sprintf "NoSuchQuery(%s;%s)" id (String.concat "," (List.sort known))
    | DuplicateQuery id -> sprintf "DuplicateQuery(%s)" id
    | UnknownParam(name, declared) -> sprintf "UnknownParam(%s;%s)" name (String.concat "," declared)
    | ParamTypeMismatch(name, expected, got) ->
        sprintf "ParamTypeMismatch(%s;%s;%s)" name (qColTag expected) (qColTag got)
    | RequiredParamsUnbound names -> sprintf "RequiredParamsUnbound(%s)" (String.concat "," names)
    | SourceNotResolved r -> sprintf "SourceNotResolved(%s)" r
    | ExecutionFailed(detail, recoverable) -> sprintf "ExecutionFailed(%s;%s)" detail (String.concat "," recoverable)
    | Timeout -> "Timeout"
    | RequiredParamsNull names -> sprintf "RequiredParamsNull(%s)" (String.concat "," names)
    | DuplicateParam name -> sprintf "DuplicateParam(%s)" name
    // Phase 318 — the gate's refusals; never raised by the ungated model, rendered to stay total.
    | QueryPolicyRefused(p, r, xs) -> sprintf "PolicyRefused(%s;%s;%s)" p r (String.concat "," xs)
    | QueryApprovalRequired p -> sprintf "ApprovalRequired(%s)" p
    // Phase 385 — the argument reader's refusal; no dispatch raises it, rendered to stay total.
    | UnreadableArgs e -> sprintf "UnreadableArgs(%s)" e.Message
    // Phase 398 — the shape refusals (registration and the reader) and the resolver's two; the model has
    // no Where / OrderBy and no dispatch here raises them, rendered to stay total.
    | UnknownColumn(name, declared) -> sprintf "UnknownColumn(%s;%s)" name (String.concat "," declared)
    | PredicateTypeMismatch(c, expected, got) ->
        sprintf "PredicateTypeMismatch(%s;%s;%s)" c (qColTag expected) (qColTag got)
    | PredicateNotApplicable(p, c, t) -> sprintf "PredicateNotApplicable(%s;%s;%s)" p c (qColTag t)
    | IllFormedLiteral(c, r) -> sprintf "IllFormedLiteral(%s;%s)" c r
    | DuplicateSortColumn c -> sprintf "DuplicateSortColumn(%s)" c
    | PredicateNotHonoured p -> sprintf "PredicateNotHonoured(%A)" p
    | OrderNotHonoured c -> sprintf "OrderNotHonoured(%s)" c

let private modelQueryErrRender (e: ModelQuery.query_error) : string =
    match e with
    | ModelQuery.NoSuchQuery(id, known) -> sprintf "NoSuchQuery(%s;%s)" id (String.concat "," (List.sort known))
    | ModelQuery.DuplicateQuery id -> sprintf "DuplicateQuery(%s)" id
    | ModelQuery.UnknownParam(name, declared) -> sprintf "UnknownParam(%s;%s)" name (String.concat "," declared)
    | ModelQuery.ParamTypeMismatch(name, expected, got) ->
        sprintf "ParamTypeMismatch(%s;%s;%s)" name (qModelColTag expected) (qModelColTag got)
    | ModelQuery.RequiredParamsUnbound names -> sprintf "RequiredParamsUnbound(%s)" (String.concat "," names)
    | ModelQuery.SourceNotResolved r -> sprintf "SourceNotResolved(%s)" r
    | ModelQuery.ExecutionFailed(detail, recoverable) ->
        sprintf "ExecutionFailed(%s;%s)" detail (String.concat "," recoverable)
    | ModelQuery.Timeout -> "Timeout"
    | ModelQuery.RequiredParamsNull names -> sprintf "RequiredParamsNull(%s)" (String.concat "," names)
    | ModelQuery.DuplicateParam name -> sprintf "DuplicateParam(%s)" name
    // Phase 398 — the admission's refusals of a filter or an order, rendered as production's are.
    | ModelQuery.UnknownColumn(name, declared) -> sprintf "UnknownColumn(%s;%s)" name (String.concat "," declared)
    | ModelQuery.PredicateTypeMismatch(c, expected, got) ->
        sprintf "PredicateTypeMismatch(%s;%s;%s)" c (qModelColTag expected) (qModelColTag got)
    | ModelQuery.PredicateNotApplicable(p, c, t) -> sprintf "PredicateNotApplicable(%s;%s;%s)" p c (qModelColTag t)
    | ModelQuery.IllFormedLiteral(c, r) -> sprintf "IllFormedLiteral(%s;%s)" c r
    | ModelQuery.DuplicateSortColumn c -> sprintf "DuplicateSortColumn(%s)" c

let private queryErrClass (e: QueryError) : string =
    match e with
    | NoSuchQuery _ -> "NoSuchQuery"
    | DuplicateQuery _ -> "DuplicateQuery"
    | UnknownParam _ -> "UnknownParam"
    | ParamTypeMismatch _ -> "ParamTypeMismatch"
    | RequiredParamsUnbound _ -> "RequiredParamsUnbound"
    | SourceNotResolved _ -> "SourceNotResolved"
    | ExecutionFailed _ -> "ExecutionFailed"
    | Timeout -> "Timeout"
    | RequiredParamsNull _ -> "RequiredParamsNull"
    | DuplicateParam _ -> "DuplicateParam"
    | QueryPolicyRefused _ -> "PolicyRefused"
    | QueryApprovalRequired _ -> "ApprovalRequired"
    | UnreadableArgs _ -> "UnreadableArgs"
    | UnknownColumn _ -> "UnknownColumn"
    | PredicateTypeMismatch _ -> "PredicateTypeMismatch"
    | PredicateNotApplicable _ -> "PredicateNotApplicable"
    | IllFormedLiteral _ -> "IllFormedLiteral"
    | DuplicateSortColumn _ -> "DuplicateSortColumn"
    | PredicateNotHonoured _ -> "PredicateNotHonoured"
    | OrderNotHonoured _ -> "OrderNotHonoured"

let private prodQueryDeferredRender (d: Deferred<QueryResult>) : string =
    match d with
    | Pending -> "Pending"
    | Ready r -> sprintf "Ready(page %d)" r.PageNum
    | Failed m -> sprintf "Failed(%s)" m

let private modelQueryDeferredRender (d: ModelQuery.deferred<QueryResult>) : string =
    match d with
    | ModelQuery.Pending -> "Pending"
    | ModelQuery.Ready r -> sprintf "Ready(page %d)" r.PageNum
    | ModelQuery.Failed m -> sprintf "Failed(%s)" m

/// The typed refusal's render pair.
let private queryError = Differential.pair prodQueryErrRender modelQueryErrRender

/// The async envelope's render pair.
let private queryDeferred =
    Differential.pair prodQueryDeferredRender modelQueryDeferredRender

/// The seam tally. Phase 398 added the shaped declarations registered, the shape refusals compared
/// and the keys over a shape; `RegisterClasses` is the shape refusals reached at registration,
/// `Classes` every refusal class reached at dispatch.
let private queryTally =
    Tally.empty
        [ "Registered"
          "DupRefused"
          "Settled"
          "StillPending"
          "NoSuch"
          "Validated"
          "Refused"
          "ExecFailed"
          "ResolverRan"
          "RefusedWithoutResolver"
          "Keys"
          "KeysWithNull"
          "KeysPermuted"
          "RepeatedParamDecls"
          "ShapedRegistered"
          "ShapeRefused"
          "ShapedKeys" ]
    |> Tally.sets [ "Classes"; "RegisterClasses" ]

let private queryIdPool = [ "q-a"; "q-b"; "q-c" ]
let private queryParamNamePool = [ "p0"; "p1"; "p2"; "when" ]

let private queryTypePool =
    [ IntType
      FloatType
      BoolType
      StringType
      DateType
      TimestampType TimeUnit.Seconds
      DecimalType
      TimestampType TimeUnit.Milliseconds
      TimestampType TimeUnit.Microseconds
      TimestampType TimeUnit.Nanoseconds ]

/// A present cell of the given type, from a small pool that reaches the key's edges: a negative
/// and an extreme int, a negative zero and an exponent-form float, a string that SPELLS a binding
/// (the shape `key_collision` reads off), an empty string, and the null marker as a string.
let private genCellOf (t: ColumnType) (r: ConfRng.T) : Cell * ConfRng.T =
    match t with
    | IntType ->
        let v, r1 = ConfRng.choose [ -3; 0; 42; System.Int32.MaxValue ] r
        Cell.Int v, r1
    | FloatType ->
        let v, r1 = ConfRng.choose [ 0.0; -0.0; 1.5; 0.1; 1e21; -2.5e-7 ] r
        Cell.Float v, r1
    | BoolType ->
        let v, r1 = ConfRng.choose [ true; false ] r
        Cell.Bool v, r1
    | StringType ->
        let v, r1 = ConfRng.choose [ ""; "x"; "1b=s2"; "p1=i42"; "∅" ] r
        Cell.Str v, r1
    | DateType ->
        let v, r1 = ConfRng.choose [ "2026-09-20"; "1970-01-01" ] r
        Cell.Date v, r1
    | TimestampType _ ->
        // Every unit (Phase 430): a whole second, and a fraction of three, six and nine digits —
        // so a literal is drawn finer than its column as often as not, which is the where-fault's
        // widening clause and `validateParams`' timestamp arm.
        let v, r1 =
            ConfRng.choose
                [ "2026-09-20T00:00:00Z"
                  "1970-01-01T00:00:00Z"
                  "2026-09-20T00:00:00.5Z"
                  "2026-09-20T00:00:00.000125Z"
                  "2026-09-20T00:00:00.000000007Z" ]
                r

        Cell.Timestamp v, r1
    | DecimalType ->
        // A zero, a negative, a fraction a float cannot hold, and one longer than any host decimal.
        let v, r1 =
            ConfRng.choose [ "0"; "-3.5"; "0.1"; "12345678901234567890123456789012345.000000000001" ] r

        Cell.Decimal v, r1

/// A declaration: up to three params, names drawn WITH replacement, any type, either
/// requiredness; every determinism source reached.
let private genQueryDecl (id: string) (r: ConfRng.T) : Query * ConfRng.T =
    let mutable rng = r
    let n, r1 = ConfRng.intBelow 4 rng
    rng <- r1
    let ps = System.Collections.Generic.List<QueryParam>()

    for _ in 1..n do
        let name, r2 = ConfRng.choose queryParamNamePool rng
        let ty, r3 = ConfRng.choose queryTypePool r2
        let req, r4 = ConfRng.intBelow 2 r3
        rng <- r4

        ps.Add(
            { Name = name
              Type = ty
              Required = (req = 0) }
        )

    let det, r5 = ConfRng.choose allDeterminismSets rng
    let page, r6 = ConfRng.intBelow 3 r5
    rng <- r6

    // Phase 398 — the declared filter and order, drawn AFTER every earlier draw. One declaration in
    // two carries a shape over a drawn two-column schema: each predicate a drawn kind on a drawn
    // column — one in eight an undeclared one — with, for a comparison, a literal of the column's
    // own type, or one time in eight a `Null` and one in eight a literal of ANOTHER type; each order
    // key a drawn column (the undeclared one possible, a repeat possible) and direction. Every
    // literal is one the column layer carries (the pools are finite and canonical), because the
    // model does not restate `Table.validate`'s clause (row `query-register-refuses-shape`).
    let shaped, r7 = ConfRng.intBelow 2 rng
    rng <- r7

    let schema, where, order =
        if shaped = 0 then
            [ Field.create "n" IntType ], [], []
        else
            let g = ref rng
            let t1, r8 = ConfRng.choose queryTypePool g.Value
            let t2, r9 = ConfRng.choose queryTypePool r8
            g.Value <- r9
            let schema = [ Field.create "c1" t1; Field.create "c2" t2 ]

            let column () =
                let k, r = ConfRng.intBelow 8 g.Value
                g.Value <- r

                if k = 0 then
                    "zz", StringType
                else
                    let f, r' = ConfRng.choose schema g.Value
                    g.Value <- r'
                    f.Name, f.Type

            let literal (ty: ColumnType) =
                let k, r = ConfRng.intBelow 8 g.Value
                g.Value <- r

                if k = 0 then
                    Cell.Null
                elif k = 1 then
                    let other, r' =
                        ConfRng.choose (queryTypePool |> List.filter (fun t -> t <> ty)) g.Value

                    let c, r'' = genCellOf other r'
                    g.Value <- r''
                    c
                else
                    let c, r' = genCellOf ty g.Value
                    g.Value <- r'
                    c

            let nWhere, r10 = ConfRng.intBelow 3 g.Value
            g.Value <- r10

            let where =
                [ for _ in 1..nWhere do
                      let c, ty = column ()
                      let kind, r = ConfRng.intBelow 8 g.Value
                      g.Value <- r

                      match kind with
                      | 0 -> ColumnPredicate.EqualTo(c, literal ty)
                      | 1 -> ColumnPredicate.GreaterThan(c, literal ty)
                      | 2 -> ColumnPredicate.AtLeast(c, literal ty)
                      | 3 -> ColumnPredicate.LessThan(c, literal ty)
                      | 4 -> ColumnPredicate.AtMost(c, literal ty)
                      | 5 ->
                          let text, r' = ConfRng.choose [ ""; "x"; "\u0001"; "w" ] g.Value
                          g.Value <- r'
                          ColumnPredicate.Contains(c, text)
                      | 6 -> ColumnPredicate.IsNull c
                      | _ -> ColumnPredicate.IsNotNull c ]

            let nOrder, r11 = ConfRng.intBelow 3 g.Value
            g.Value <- r11

            let order =
                [ for _ in 1..nOrder do
                      let c, _ = column ()

                      let d, r =
                          ConfRng.choose [ SortDirection.Ascending; SortDirection.Descending ] g.Value

                      g.Value <- r
                      { Column = c; Direction = d } ]

            rng <- g.Value

            schema, where, order

    { Id = id
      Params = List.ofSeq ps
      ResultSchema = schema
      Effect = { Host = ReadsHost; Determinism = det }
      Source = Ref("src-" + id)
      TimeoutMs = (if page = 0 then None else Some(1000 * page))
      PageSize = (if page = 2 then Some 50 else None)
      Where = where
      OrderBy = order },
    rng

/// An argument set against a declaration: per param, three draws in four a binding — one in six
/// of those a `Null`, one in six a cell of ANOTHER type, the rest in type — one set in five with
/// an undeclared name, one in eight with a binding REPEATED under its name, and one in two
/// reversed, so the name sort is exercised against the caller's order.
let private genQueryArgs (q: Query) (r: ConfRng.T) : (string * Cell) list * ConfRng.T =
    let mutable rng = r
    let args = System.Collections.Generic.List<string * Cell>()

    for p in q.Params do
        let roll, r1 = ConfRng.intBelow 4 rng
        rng <- r1

        if roll < 3 then
            let shape, r2 = ConfRng.intBelow 6 rng
            rng <- r2

            if shape = 0 then
                args.Add(p.Name, Cell.Null)
            elif shape = 1 then
                let other, r3 =
                    ConfRng.choose (queryTypePool |> List.filter (fun t -> t <> p.Type)) rng

                let c, r4 = genCellOf other r3
                rng <- r4
                args.Add(p.Name, c)
            else
                let c, r3 = genCellOf p.Type rng
                rng <- r3
                args.Add(p.Name, c)

    let extra, r5 = ConfRng.intBelow 5 rng
    rng <- r5

    if extra = 0 then
        args.Add("zz", Cell.Int 1)

    let repeat, r6 = ConfRng.intBelow 8 rng
    rng <- r6

    if repeat = 0 && args.Count > 0 then
        let n, _ = args[0]
        let c, r7 = genCellOf StringType rng
        rng <- r7
        args.Add(n, c)

    let flip, r8 = ConfRng.intBelow 2 rng
    rng <- r8
    let drawn = List.ofSeq args
    (if flip = 0 then List.rev drawn else drawn), rng

let private queryResultOf (page: int) : QueryResult =
    { Rows =
        { Schema = [ Field.create "n" IntType ]
          Columns = [ Column.ofInts "n" (Vector.ofList [ page ]) AllValid ] }
      PageNum = page
      TotalRowCount = None
      NextPageToken = None }

let private queryProbe
    (bridge: Query -> ModelQuery.query)
    (rn: ModelQuery.renderers)
    (seedTag: int)
    (r: ConfRng.T)
    (t0: Tally)
    : ConfRng.T * Tally =
    let mutable rng = r
    let mutable t = t0

    // build a registry on both sides, in the same order, with duplicates possible
    let n, r1 = ConfRng.intBelow 4 rng
    rng <- r1
    let mutable preg = QueryRegistry.empty
    let mutable mreg = ModelQuery.empty

    for _ in 1..n do
        let id, r2 = ConfRng.choose queryIdPool rng
        let q, r3 = genQueryDecl id r2
        rng <- r3

        t <-
            Tally.bumpIf
                ((q.Params |> List.map _.Name |> List.distinct |> List.length) < List.length q.Params)
                "RepeatedParamDecls"
                t

        match QueryRegistry.register q preg, ModelQuery.register (bridge q) mreg with
        | Ok p', ModelQuery.Ok m' ->
            preg <- p'
            mreg <- m'

            t <-
                t
                |> Tally.bump "Registered"
                |> Tally.bumpIf (not (List.isEmpty q.Where && List.isEmpty q.OrderBy)) "ShapedRegistered"
        | Error pe, ModelQuery.Error me ->
            t <- Tally.bump "DupRefused" t

            match pe with
            | UnknownColumn _
            | PredicateTypeMismatch _
            | PredicateNotApplicable _
            | IllFormedLiteral _
            | DuplicateSortColumn _ ->
                t <-
                    t
                    |> Tally.bump "ShapeRefused"
                    |> Tally.reach "RegisterClasses" (queryErrClass pe)
            | _ -> ()

            t <-
                t
                |> Differential.compareWith
                    queryError
                    (fun p m -> sprintf "seed %d: register refused differently (%s vs %s)" seedTag p m)
                    pe
                    me
        | _ -> t <- Tally.disagree (sprintf "seed %d: register verdict differs on %s" seedTag id) t

    // enumerate + tryFind agree (membership, and the entries themselves, sorted by id)
    let penum = QueryRegistry.enumerate preg |> List.map bridge
    let menum = ModelQuery.enumerate mreg |> List.sortBy _.q_id

    t <-
        t
        |> Differential.check (penum = menum) (fun () -> sprintf "seed %d: enumerate differs" seedTag)

    for id in "q-x" :: queryIdPool do
        let p = QueryRegistry.tryFind id preg |> Option.map bridge
        let m = ofMOpt (ModelQuery.try_find_query id mreg)

        t <-
            t
            |> Differential.check (p = m) (fun () -> sprintf "seed %d: tryFind %s differs" seedTag id)

    let k, r4 = ConfRng.intBelow 4 rng
    rng <- r4

    for _ in 0..k do
        let id, r5 = ConfRng.choose ("q-x" :: queryIdPool) rng
        rng <- r5

        let args, r6 =
            match QueryRegistry.tryFind id preg with
            | Some q -> genQueryArgs q rng
            | None -> [ "p0", Cell.Int 1 ], rng

        let margs = qArgsToModel args

        // the resolver answers in the envelope, so the draw reaches all three of its cases:
        // 0 fails (the typed `ExecutionFailed`), 1 is still pending, 2 and 3 settle.
        let answer, r7 = ConfRng.intBelow 4 r6
        rng <- r7
        let pRan = ref false
        let mRan = ref false

        let pResolve (_: Query) =
            pRan.Value <- true

            match answer with
            | 0 -> Failed "unreachable source"
            | 1 -> Pending
            | _ -> Ready(queryResultOf answer)

        let mResolve (_: ModelQuery.query) =
            mRan.Value <- true

            match answer with
            | 0 -> ModelQuery.Failed "unreachable source"
            | 1 -> ModelQuery.Pending
            | _ -> ModelQuery.Ready(queryResultOf answer)

        let p = QueryRegistry.dispatch preg id args pResolve
        let m = ModelQuery.dispatch mreg id margs mResolve

        t <-
            match p, m with
            | Ok pv, ModelQuery.Ok mv ->
                t
                |> Tally.bump (
                    match pv with
                    | Pending -> "StillPending"
                    | _ -> "Settled"
                )
                |> Differential.compareWith
                    queryDeferred
                    (fun pr mr ->
                        sprintf
                            "seed %d: dispatch %s accepted with different envelopes\n  prod %s\n  model %s"
                            seedTag
                            id
                            pr
                            mr)
                    pv
                    mv
                // and neither side ever hands an `Ok(Failed _)` out of the seam
                // (`invoke_never_ok_failed` / `dispatch_never_ok_failed`, sampled on the shipped one).
                |> Differential.check
                    (match pv, mv with
                     | Failed _, _
                     | _, ModelQuery.Failed _ -> false
                     | _ -> true)
                    (fun () -> sprintf "seed %d: dispatch %s accepted with an Ok(Failed _) envelope" seedTag id)
            | Error pe, ModelQuery.Error me ->
                t
                |> Tally.reach "Classes" (queryErrClass pe)
                |> Differential.compareWith
                    queryError
                    (fun pr mr ->
                        sprintf "seed %d: dispatch %s refused differently\n  prod %s\n  model %s" seedTag id pr mr)
                    pe
                    me
                |> Tally.bump (
                    match pe with
                    | NoSuchQuery _ -> "NoSuch"
                    | ExecutionFailed _ -> "ExecFailed"
                    | _ -> "Refused"
                )
            | Ok _, ModelQuery.Error me ->
                Tally.disagree
                    (sprintf
                        "seed %d: dispatch %s accepted by production, refused by the model (%s)"
                        seedTag
                        id
                        (modelQueryErrRender me))
                    t
            | Error pe, ModelQuery.Ok _ ->
                Tally.disagree
                    (sprintf
                        "seed %d: dispatch %s refused by production (%s), accepted by the model"
                        seedTag
                        id
                        (prodQueryErrRender pe))
                    t

        // the resolver ran on both sides or on neither — and never past a refusal that is not its own
        t <-
            t
            |> Differential.check (pRan.Value = mRan.Value) (fun () ->
                sprintf
                    "seed %d: dispatch %s ran the resolver on one side only (prod %b, model %b)"
                    seedTag
                    id
                    pRan.Value
                    mRan.Value)
            |> Tally.bumpIf pRan.Value "ResolverRan"

        (match p with
         | Error(ExecutionFailed _)
         | Ok _ -> ()
         | Error _ ->
             t <-
                 t
                 |> Tally.bump "RefusedWithoutResolver"
                 |> Differential.check (not pRan.Value) (fun () ->
                     sprintf "seed %d: dispatch %s was REFUSED and the resolver still ran" seedTag id))

        // validateParams, the capture key and the determinism tag on their own, on the resolved
        // declaration — the key for EVERY argument set, accepted or not: `invocationKey` is total
        // and asks nothing of validation.
        match QueryRegistry.tryFind id preg with
        | Some q ->
            let mq = bridge q

            t <-
                match Query.validateParams q args, ModelQuery.validate_params mq margs with
                | Ok(), ModelQuery.Ok() -> Tally.bump "Validated" t
                | Error pe, ModelQuery.Error me ->
                    t
                    |> Differential.compareWith
                        queryError
                        (fun pr mr ->
                            sprintf "seed %d: validateParams refused differently\n  prod %s\n  model %s" seedTag pr mr)
                        pe
                        me
                | _ -> Tally.disagree (sprintf "seed %d: validateParams verdict differs on %s" seedTag id) t

            let pKey = Query.invocationKey q args
            let mKey = ModelQuery.invocation_key rn mq margs

            t <-
                t
                |> Tally.bump "Keys"
                |> Tally.bumpIf (not (List.isEmpty q.Where && List.isEmpty q.OrderBy)) "ShapedKeys"
                |> Tally.bumpIf (args |> List.exists (fun (_, c) -> c = Cell.Null)) "KeysWithNull"
                |> Differential.check (pKey = mKey) (fun () ->
                    sprintf "seed %d: invocationKey differs on %s %A\n  prod %s\n  model %s" seedTag id args pKey mKey)

            // Phase 316 — the PAGE key, byte for byte, over a token alphabet carrying the page tag
            // and the canonical encoding's own symbols; and the first page keys as `invocationKey`.
            for token in [ None; Some ""; Some "p"; Some "\u0001"; Some "\u0010t"; Some "cursor" ] do
                let mToken =
                    match token with
                    | None -> FStar_Pervasives_Native.None
                    | Some t -> FStar_Pervasives_Native.Some t

                let pPage = Query.invocationKeyPage q args token
                let mPage = ModelQuery.invocation_key_page rn mq margs mToken

                t <-
                    t
                    |> Differential.check (pPage = mPage) (fun () ->
                        sprintf
                            "seed %d: invocationKeyPage differs on %s %A %A\n  prod %s\n  model %s"
                            seedTag
                            id
                            args
                            token
                            pPage
                            mPage)
                    |> Differential.check (not (token.IsNone && pPage <> pKey)) (fun () ->
                        sprintf "seed %d: the first page's key is not invocationKey on %s" seedTag id)

            // `invocation_key_deterministic`, sampled on the shipped seam: with distinct names the
            // caller's order does not reach the key.
            let names = args |> List.map fst

            if List.length names > 1 && List.length (List.distinct names) = List.length names then
                t <-
                    t
                    |> Tally.bump "KeysPermuted"
                    |> Differential.check (Query.invocationKey q (List.rev args) = pKey) (fun () ->
                        sprintf "seed %d: the shipped invocationKey moved under a reordering of %A" seedTag args)

            t <-
                t
                |> Differential.check (Query.determinismTag q = ModelQuery.determinism_tag_of mq) (fun () ->
                    sprintf "seed %d: determinismTag differs on %s" seedTag id)
        | None -> ()

    rng, t

let private queryDifferential
    (bridge: Query -> ModelQuery.query)
    (rn: ModelQuery.renderers)
    (seed: int)
    (trials: int)
    : Tally =
    Differential.seeded trials (ConfRng.ofSeed seed) (queryProbe bridge rn) queryTally

// ---- Phase 455 — the query codec, whole: the declaration, the result page, the async result and
//      the typed refusal, beside `QueryCodec` over its own column codec ----

/// The decode refusal's render pair — its code and its path — is the capability codec's: the two
/// seams share `DecodeError`, and one render of it is the comparison both read.
let private decodeError = CapabilityTests.decodeError

let private qColOfModel (t: ModelQuery.column_type) : ColumnType =
    match t with
    | ModelQuery.IntType -> IntType
    | ModelQuery.FloatType -> FloatType
    | ModelQuery.BoolType -> BoolType
    | ModelQuery.StringType -> StringType
    | ModelQuery.DateType -> DateType
    | ModelQuery.TimestampType u -> TimestampType(qUnitOfModel u)
    | ModelQuery.DecimalType -> DecimalType

/// A predicate literal's value as `QueryCodec` writes it — read off the PUBLIC encoder, over a
/// declaration whose only filter compares with that literal, so the writer is production's own call.
let private literalValueJson (v: Cell) : JVal =
    let q =
        { shapeCarrier with
            ResultSchema = [ Field.create "v" (Cell.typeOf v |> Option.defaultValue StringType) ]
            Where = [ ColumnPredicate.EqualTo("v", v) ] }

    match Json.parse (QueryCodec.encode q) with
    | Ok(JObj ms) ->
        match ms |> List.tryFind (fun (k, _) -> k = "where") with
        | Some(_, JArr [ JObj p ]) ->
            match p |> List.tryFind (fun (k, _) -> k = "value") with
            | Some(_, v) -> v
            | None -> JObj []
        | _ -> JObj []
    | _ -> JObj []

/// A literal read at a stated type as `QueryShape.literalOf` reads it: the one cell of a one-row
/// column of that type, through the column codec's public reader.
let private literalOfJson (column: string) (ty: ColumnType) (v: JVal) : Cell option =
    let doc =
        JObj
            [ "schema", JArr [ JObj [ "name", JStr column; "type", JStr(ColumnType.tag ty) ] ]
              "columns", JObj [ column, JObj [ "values", JArr [ v ]; "validity", JArr [ JBool true ] ] ] ]

    match ColumnCodec.decodeJson doc with
    | Ok(Embedded { Columns = [ c ] }) when Column.length c = 1 -> Some(Column.cell 0 c)
    | _ -> None

/// The model's column-codec parameter, instantiated at production's column codec: the schema entry,
/// a `Ref` source, an embedded table of rows, and a predicate literal.
let private columnCodec: ModelQuery.column_codec<Table> =
    { ModelQuery.column_codec.field_json =
        fun (n, t) -> jvalToModel (ColumnCodec.fieldJson (Field.create n (qColOfModel t)))
      ModelQuery.column_codec.field_of =
        fun j ->
            match ColumnCodec.decodeField (jvalOfModel j) with
            | Ok f -> FStar_Pervasives_Native.Some(f.Name, qColToModel f.Type)
            | Error _ -> FStar_Pervasives_Native.None
      ModelQuery.column_codec.source_json = fun s -> jvalToModel (ColumnCodec.encodeJson (Ref s))
      ModelQuery.column_codec.source_of =
        fun j ->
            match ColumnCodec.decodeJson (jvalOfModel j) with
            | Ok(Ref s) -> FStar_Pervasives_Native.Some s
            | _ -> FStar_Pervasives_Native.None
      ModelQuery.column_codec.rows_json = fun t -> jvalToModel (ColumnCodec.encodeJson (Embedded t))
      ModelQuery.column_codec.rows_of =
        fun j ->
            match ColumnCodec.decodeJson (jvalOfModel j) with
            | Ok(Embedded t) -> FStar_Pervasives_Native.Some t
            | _ -> FStar_Pervasives_Native.None
      ModelQuery.column_codec.lit_json = fun _ c -> jvalToModel (literalValueJson (qCellOfModel c))
      ModelQuery.column_codec.lit_of =
        fun c t j ->
            match literalOfJson c (qColOfModel t) (jvalOfModel j) with
            | Some cell -> FStar_Pervasives_Native.Some(qCellToModel cell)
            | None -> FStar_Pervasives_Native.None }

/// A declaration as the codec model reads it: the bridge `queryToModel` is, its source the `Ref`
/// the codec writes.
let private codecQueryToModel (q: Query) : ModelQuery.query =
    { queryToModel q with
        q_source =
            match q.Source with
            | Ref s -> s
            | other -> sprintf "%A" other }

let private rowsText (t: Table) =
    Canonical.write Rendering.CrossHost (ColumnCodec.encodeJson (Embedded t))

let private resultToModel (qr: QueryResult) : ModelQuery.query_result<Table> =
    { ModelQuery.query_result.qr_rows = qr.Rows
      ModelQuery.query_result.qr_page_num = bigint qr.PageNum
      ModelQuery.query_result.qr_total = toMOpt (Option.map (fun (n: int) -> bigint n) qr.TotalRowCount)
      ModelQuery.query_result.qr_next = toMOpt qr.NextPageToken }

let private sameResult (p: QueryResult) (m: ModelQuery.query_result<Table>) =
    rowsText p.Rows = rowsText m.qr_rows
    && bigint p.PageNum = m.qr_page_num
    && toMOpt (Option.map (fun (n: int) -> bigint n) p.TotalRowCount) = m.qr_total
    && toMOpt p.NextPageToken = m.qr_next

let private codeToModel (c: DecodeCode) : ModelCap.decode_code =
    match c with
    | DecodeCode.InvalidJson -> ModelCap.InvalidJson
    | DecodeCode.MissingField -> ModelCap.MissingField
    | DecodeCode.WrongKind -> ModelCap.WrongKind
    | DecodeCode.UnknownTag -> ModelCap.UnknownTag
    | DecodeCode.OutOfRange -> ModelCap.OutOfRange
    | DecodeCode.UndeclaredMember -> ModelCap.UndeclaredMember
    | DecodeCode.LimitExceeded -> ModelCap.LimitExceeded
    | DecodeCode.NotAdmitted -> ModelCap.NotAdmitted
    | DecodeCode.SchemaFault -> ModelCap.SchemaFault

/// Production's twenty-case `QueryError` as the codec model's `wire_query_error`; `None` for a case
/// the generator does not draw.
let private wireErrorToModel (e: QueryError) : ModelQuery.wire_query_error =
    let ty = qColToModel

    match e with
    | NoSuchQuery(id, k) -> ModelQuery.QRefused(ModelQuery.NoSuchQuery(id, k))
    | DuplicateQuery id -> ModelQuery.QRefused(ModelQuery.DuplicateQuery id)
    | UnknownParam(n, d) -> ModelQuery.QRefused(ModelQuery.UnknownParam(n, d))
    | ParamTypeMismatch(n, x, g) -> ModelQuery.QRefused(ModelQuery.ParamTypeMismatch(n, ty x, ty g))
    | RequiredParamsUnbound xs -> ModelQuery.QRefused(ModelQuery.RequiredParamsUnbound xs)
    | SourceNotResolved s -> ModelQuery.QRefused(ModelQuery.SourceNotResolved s)
    | ExecutionFailed(d, r) -> ModelQuery.QRefused(ModelQuery.ExecutionFailed(d, r))
    | Timeout -> ModelQuery.QRefused ModelQuery.Timeout
    | RequiredParamsNull xs -> ModelQuery.QRefused(ModelQuery.RequiredParamsNull xs)
    | DuplicateParam n -> ModelQuery.QRefused(ModelQuery.DuplicateParam n)
    | QueryPolicyRefused(p, r, a) -> ModelQuery.QPolicyRefused(p, r, a)
    | QueryApprovalRequired p -> ModelQuery.QApprovalRequired p
    | UnreadableArgs de ->
        ModelQuery.QUnreadableArgs
            { ModelQuery.wire_decode_error.we_code = codeToModel de.Code
              ModelQuery.wire_decode_error.we_path =
                de.Path
                |> List.map (function
                    | PathSegment.Key k -> ModelQuery.WKey k
                    | PathSegment.Index i -> ModelQuery.WIndex(bigint i))
              ModelQuery.wire_decode_error.we_expected = de.Expected
              ModelQuery.wire_decode_error.we_message = de.Message }
    | UnknownColumn(n, d) -> ModelQuery.QRefused(ModelQuery.UnknownColumn(n, d))
    | PredicateTypeMismatch(c, x, g) -> ModelQuery.QRefused(ModelQuery.PredicateTypeMismatch(c, ty x, ty g))
    | PredicateNotApplicable(p, c, t) -> ModelQuery.QRefused(ModelQuery.PredicateNotApplicable(p, c, ty t))
    | IllFormedLiteral(c, r) -> ModelQuery.QRefused(ModelQuery.IllFormedLiteral(c, r))
    | DuplicateSortColumn c -> ModelQuery.QRefused(ModelQuery.DuplicateSortColumn c)
    | PredicateNotHonoured p -> ModelQuery.QPredicateNotHonoured(qPredToModel p)
    | OrderNotHonoured c -> ModelQuery.QOrderNotHonoured c

let private genWireError (r: ConfRng.T) : QueryError * ConfRng.T =
    let k, r1 = ConfRng.intBelow 20 r
    let s, r2 = ConfRng.choose [ "a"; "b"; "when" ] r1
    let n, r3 = ConfRng.intBelow 3 r2
    let xs = List.init n (fun i -> sprintf "x%d" i)
    let t, r4 = ConfRng.choose queryTypePool r3

    let e =
        match k with
        | 0 -> NoSuchQuery(s, xs)
        | 1 -> DuplicateQuery s
        | 2 -> UnknownParam(s, xs)
        | 3 -> ParamTypeMismatch(s, t, IntType)
        | 4 -> RequiredParamsUnbound xs
        | 5 -> SourceNotResolved s
        | 6 -> ExecutionFailed("boom", xs)
        | 7 -> Timeout
        | 8 -> RequiredParamsNull xs
        | 9 -> DuplicateParam s
        | 10 -> QueryPolicyRefused(s, "no", xs)
        | 11 -> QueryApprovalRequired s
        | 12 ->
            UnreadableArgs
                { Code = DecodeCode.WrongKind
                  Path = [ PathSegment.Key s; PathSegment.Index n ]
                  Expected = "a value"
                  Message = "not one" }
        | 13 -> UnknownColumn(s, xs)
        | 14 -> PredicateTypeMismatch(s, t, StringType)
        | 15 -> PredicateNotApplicable("contains", s, t)
        | 16 -> IllFormedLiteral(s, "why")
        | 17 -> DuplicateSortColumn s
        | 18 -> PredicateNotHonoured(ColumnPredicate.EqualTo(s, Cell.Int n))
        | _ -> OrderNotHonoured s

    e, r4

/// The codec tally: the documents read, the refusals compared (and of them the unmutated
/// declarations the admission gate refused), and the refusal codes reached.
let private queryCodecTally =
    Tally.empty [ "Documents"; "AdmissionRefused"; "Refused" ]
    |> Tally.sets [ "Codes" ]

/// Runs the model's query codec beside `QueryCodec` over its own column codec: each encoding, and
/// the reading of each encoding and of mutations of it — acceptance, the value, and every typed
/// refusal's code and path — and, on production, that every typed refusal RESOLVES in the document
/// it read. `queryJson` is the model's declaration writer, a parameter for the go-red.
let private queryCodecDifferential (queryJson: ModelQuery.query -> ModelCap.jval) (seed: int) (trials: int) : Tally =
    let step (i: int) (r0: ConfRng.T) (t0: Tally) =
        let mutable rng = r0
        let mutable t = t0

        let mutations (j: JVal) =
            let mutable out = [ j ]

            for _ in 1..4 do
                let m, r = jmutate j rng
                rng <- r
                out <- m :: out

            // The document production reads is the text the mutation renders to.
            out
            |> List.map (fun m ->
                match Json.parse (Canonical.write Rendering.CrossHost m) with
                | Ok v -> m = j, v
                | Error _ -> m = j, m)

        let typed
            (what: string)
            (j: JVal)
            (p: Result<'P, DecodeError>)
            (m: ModelCap.outcome<'M, ModelCap.decode_error>)
            (same: 'P -> 'M -> bool)
            (unmutated: bool)
            (t: Tally)
            =
            let t = Tally.bump "Documents" t

            match p, m with
            | Ok a, ModelCap.Ok b when same a b -> t
            | Error pe, ModelCap.Error me when decodeError.Production pe = decodeError.Model me ->
                t
                |> Tally.bump "Refused"
                |> Tally.reach "Codes" (string pe.Code)
                |> Tally.bumpIf unmutated "AdmissionRefused"
                |> Differential.check (DecodeError.resolvesIn j pe) (fun () ->
                    sprintf
                        "seed %d: a %s refusal does not resolve: %s in %s"
                        i
                        what
                        (decodeError.Production pe)
                        (Canonical.write Rendering.CrossHost j))
            | _ ->
                Tally.disagree
                    (sprintf
                        "seed %d: the %s readers disagree on %s: %A vs %A"
                        i
                        what
                        (Canonical.write Rendering.CrossHost j)
                        p
                        m)
                    t

        // ---- the declaration, drawn by the seam differential's own generator ----
        let q, r1 = genQueryDecl (sprintf "q-%d" i) rng
        rng <- r1
        let text = QueryCodec.encode q

        match Json.parse text with
        | Error e -> t <- Tally.disagree (sprintf "seed %d: the declaration did not parse: %A" i e) t
        | Ok j0 ->
            // The encoding is compared as the canonical TEXT both write.
            t <-
                t
                |> Differential.check
                    (Canonical.write Rendering.CrossHost (jvalOfModel (queryJson (codecQueryToModel q))) = text)
                    (fun () -> sprintf "seed %d: the declaration encodes differently: %s" i text)

            for unmutated, j in mutations j0 do
                t <-
                    typed
                        "declaration"
                        j
                        (QueryCodec.decodeDetailedWith ReadPolicy.Lenient (Canonical.write Rendering.CrossHost j))
                        (ModelQuery.query_of_j columnCodec (jvalToModel j))
                        (fun a b -> codecQueryToModel a = b)
                        unmutated
                        t

        // ---- a result page, and the async envelope around it ----
        let k, r2 = ConfRng.intBelow 4 rng
        rng <- r2

        let qr =
            { queryResultOf i with
                TotalRowCount = (if k % 2 = 0 then Some(i * 3) else None)
                NextPageToken = (if k < 2 then Some(sprintf "t%d" i) else None) }

        let rtext = QueryCodec.encodeResult qr

        match Json.parse rtext with
        | Error e -> t <- Tally.disagree (sprintf "seed %d: the result did not parse: %A" i e) t
        | Ok rj ->
            t <-
                t
                |> Differential.check
                    (Canonical.write
                        Rendering.CrossHost
                        (jvalOfModel (ModelQuery.result_json columnCodec (resultToModel qr))) = rtext)
                    (fun () -> sprintf "seed %d: the result encodes differently" i)

            for _, j in mutations rj do
                t <-
                    typed
                        "result"
                        j
                        (QueryCodec.decodeResultDetailedWith ReadPolicy.Lenient (Canonical.write Rendering.CrossHost j))
                        (ModelQuery.result_of_j columnCodec (jvalToModel j))
                        sameResult
                        false
                        t

        let d =
            match k with
            | 0 -> Pending
            | 1 -> Failed "late"
            | _ -> Ready qr

        let md =
            match d with
            | Pending -> ModelCap.Pending
            | Failed m -> ModelCap.Failed m
            | Ready v -> ModelCap.Ready(resultToModel v)

        let dtext = QueryCodec.encodeDeferredResult d

        match Json.parse dtext with
        | Error e -> t <- Tally.disagree (sprintf "seed %d: the envelope did not parse: %A" i e) t
        | Ok dj ->
            t <-
                t
                |> Differential.check
                    (Canonical.write Rendering.CrossHost (jvalOfModel (ModelQuery.deferred_result_json columnCodec md)) = dtext)
                    (fun () -> sprintf "seed %d: the async result encodes differently" i)

            for _, j in mutations dj do
                t <-
                    typed
                        "async result"
                        j
                        (QueryCodec.decodeDeferredResultDetailedWith
                            ReadPolicy.Lenient
                            (Canonical.write Rendering.CrossHost j))
                        (ModelQuery.deferred_result_of_j columnCodec (jvalToModel j))
                        (fun a b ->
                            match a, b with
                            | Pending, ModelCap.Pending -> true
                            | Failed x, ModelCap.Failed y -> x = y
                            | Ready x, ModelCap.Ready y -> sameResult x y
                            | _ -> false)
                        false
                        t

        // ---- the typed refusal ----
        let e, r3 = genWireError rng
        rng <- r3
        let ej = QuerySeamCodec.encodeQueryError e

        t <-
            t
            |> Differential.check
                (jvalToModel ej = ModelQuery.query_error_json columnCodec (wireErrorToModel e))
                (fun () -> sprintf "seed %d: the query error encodes differently: %A" i e)

        for _, j in mutations ej do
            t <- Tally.bump "Documents" t

            match QuerySeamCodec.decodeQueryError j, ModelQuery.query_error_of_j columnCodec (jvalToModel j) with
            | Ok pe, ModelCap.Ok me when wireErrorToModel pe = me -> ()
            | Error _, ModelCap.Error me ->
                t <- t |> Tally.bump "Refused" |> Tally.reach "Codes" (sprintf "%A" me.d_code)
            | p, m ->
                t <-
                    Tally.disagree
                        (sprintf
                            "seed %d: the query-error readers disagree on %s: %A vs %A"
                            i
                            (Canonical.write Rendering.CrossHost j)
                            p
                            m)
                        t

        rng, t

    Differential.seeded trials (ConfRng.ofSeed seed) step queryCodecTally

/// The go-red's writer: the model's declaration writer handed the declaration with its last
/// parameter DROPPED.
let private droppingQueryJson (q: ModelQuery.query) : ModelCap.jval =
    let kept =
        match List.rev q.q_params with
        | _ :: rest -> List.rev rest
        | [] -> []

    ModelQuery.query_json columnCodec { q with q_params = kept }


[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 187: the data-acquisition seam (proofs/Query.fst) ----

          testCase
              "the query oracle agrees with QueryRegistry.register, enumerate, tryFind and dispatch, and Query.validateParams, invocationKey and determinismTag, over generated registries, declarations and argument sets"
          <| fun _ ->
              let t = queryDifferential queryToModel queryRenderers 1871 500

              Differential.expectAgrees "query" t

              // Measured at 500 registries (Phase 316: 300 until the registry began refusing a
              // declaration that names a parameter twice, which left fewer queries registered and
              // the Null-binding key floor short): registered 506, dupRefused 246 (duplicate ids
              // and repeated parameter names), repeated-name declarations 161; settled 83,
              // pending 45, noSuch 885, validated 171, refused 133, execFailed 43,
              // refusedWithoutResolver 1018; keys 304 (36 over a Null binding, 72 held still
              // under a reordering), each compared with its six page keys.
              // Phase 398 draws one declaration in two with a filter and an order over a drawn schema,
              // after every earlier draw; re-measured at the same 500: registered 394, refused at
              // registration 356 (138 of them a filter or order the schema does not admit, every one of
              // the five shape refusals reached), 96 shaped declarations registered; settled 70,
              // pending 27, noSuch 1006, validated 134, refused 101, execFailed 37,
              // refusedWithoutResolver 1107; keys 235 (37 over a Null binding, 58 held still, 60 over a
              // filter or an order). Every earlier floor still holds.
              Expect.isGreaterThan
                  (Tally.count "Registered" t)
                  200
                  (sprintf "queries were registered (registered=%d)" (Tally.count "Registered" t))

              Expect.isGreaterThan
                  (Tally.count "DupRefused" t)
                  40
                  (sprintf "duplicate registrations were refused (dupRefused=%d)" (Tally.count "DupRefused" t))

              Expect.isGreaterThan
                  (Tally.count "RepeatedParamDecls" t)
                  40
                  (sprintf
                      "declarations repeating a param name arose, so the registration refusal was compared (repeated=%d)"
                      (Tally.count "RepeatedParamDecls" t))

              Expect.isGreaterThan
                  (Tally.count "Settled" t)
                  30
                  (sprintf "dispatches settled (settled=%d)" (Tally.count "Settled" t))

              Expect.isGreaterThan
                  (Tally.count "StillPending" t)
                  10
                  (sprintf "dispatches stayed pending inside the Ok (pending=%d)" (Tally.count "StillPending" t))

              Expect.isGreaterThan
                  (Tally.count "NoSuch" t)
                  200
                  (sprintf "unregistered ids were refused (noSuch=%d)" (Tally.count "NoSuch" t))

              Expect.isGreaterThan
                  (Tally.count "Validated" t)
                  50
                  (sprintf "argument sets were accepted (validated=%d)" (Tally.count "Validated" t))

              Expect.isGreaterThan
                  (Tally.count "Refused" t)
                  50
                  (sprintf "argument sets were refused (refused=%d)" (Tally.count "Refused" t))

              Expect.isGreaterThan
                  (Tally.count "ExecFailed" t)
                  10
                  (sprintf "resolvers failed and were named (execFailed=%d)" (Tally.count "ExecFailed" t))

              Expect.equal
                  (Tally.count "ResolverRan" t)
                  ((Tally.count "Settled" t)
                   + (Tally.count "StillPending" t)
                   + (Tally.count "ExecFailed" t))
                  "the resolver ran exactly on the invocations validation passed"

              Expect.isGreaterThan
                  (Tally.count "RefusedWithoutResolver" t)
                  300
                  (sprintf
                      "refusals were checked for a resolver that did not run (refusedWithoutResolver=%d)"
                      (Tally.count "RefusedWithoutResolver" t))

              Expect.isGreaterThan
                  (Tally.count "Keys" t)
                  100
                  (sprintf "capture keys were compared (keys=%d)" (Tally.count "Keys" t))

              Expect.isGreaterThan
                  (Tally.count "KeysWithNull" t)
                  20
                  (sprintf "capture keys over a Null binding were compared (withNull=%d)" (Tally.count "KeysWithNull" t))

              Expect.isGreaterThan
                  (Tally.count "KeysPermuted" t)
                  25
                  (sprintf
                      "the shipped key was held still under a reordering of distinct names (permuted=%d)"
                      (Tally.count "KeysPermuted" t))

              // Phase 398 — the declared filter and order were drawn, admitted, refused and keyed.
              Expect.isGreaterThan
                  (Tally.count "ShapedRegistered" t)
                  40
                  (sprintf
                      "filtered or ordered declarations were registered (shaped=%d)"
                      (Tally.count "ShapedRegistered" t))

              Expect.isGreaterThan
                  (Tally.count "ShapeRefused" t)
                  50
                  (sprintf
                      "filters and orders the schema does not admit were refused (shapeRefused=%d)"
                      (Tally.count "ShapeRefused" t))

              Expect.isGreaterThan
                  (Tally.count "ShapedKeys" t)
                  25
                  (sprintf
                      "capture keys over a filter or an order were compared (shapedKeys=%d)"
                      (Tally.count "ShapedKeys" t))

              for cls in
                  [ "UnknownColumn"
                    "PredicateTypeMismatch"
                    "PredicateNotApplicable"
                    "IllFormedLiteral"
                    "DuplicateSortColumn" ] do
                  Expect.isTrue
                      (Set.contains cls (Tally.reached "RegisterClasses" t))
                      (sprintf
                          "the sample reached a %s registration refusal (reached: %A)"
                          cls
                          (Tally.reached "RegisterClasses" t))


              for cls in
                  [ "NoSuchQuery"
                    "UnknownParam"
                    "ParamTypeMismatch"
                    "RequiredParamsUnbound"
                    "RequiredParamsNull"
                    "ExecutionFailed" ] do
                  Expect.isTrue
                      (Set.contains cls (Tally.reached "Classes" t))
                      (sprintf "the sample reached a %s refusal (reached: %A)" cls (Tally.reached "Classes" t))

              Expect.equal (queryDifferential queryToModel queryRenderers 1871 500) t "same seed => identical tally"

          testCase
              "a query oracle behind a FORGETFUL bridge or an ORDER-BLIND comparator DISAGREES with Query.validateParams and Query.invocationKey — the measurement can fail"
          <| fun _ ->
              // The teeth, one set per half of the model. Behind the forgetful bridge every param
              // crosses as optional, so the model accepts what production refuses at step 2. Under
              // the order-blind comparator the model's sort leaves the caller's order standing, so
              // its key moves where production's does not. If either ever passes, that half of the
              // comparison has stopped reaching the clause and the green run above certifies
              // nothing about it.
              // 200 draws since Phase 430: the pool draws every timestamp unit, and a required param
              // refused as a type mismatch never reaches the step the forgetful bridge drops.
              let forgetful = queryDifferential (queryToModelWith false) queryRenderers 1871 200
              Differential.expectLoses "a bridge that forgets `Required`" forgetful

              Expect.isTrue
                  (Tally.disagreements forgetful
                   |> List.exists (fun d -> d.Contains "RequiredParamsUnbound"))
                  "and the disagreement is about the required-params step, which is what the bridge forgot"

              let blind = queryDifferential queryToModel orderBlindRenderers 1871 80
              Differential.expectLoses "an order-blind comparator" blind

              Expect.isTrue
                  (Tally.disagreements blind
                   |> List.forall (fun d ->
                       d.Contains "invocationKey differs" || d.Contains "invocationKeyPage differs"))
                  "and every disagreement is about the capture key — the comparator reaches nothing else"

          testCase
              "the filter and order renderers are injective on the shipped codec — a drawn Where and OrderBy round-trip QueryCodec, and the model's key reads them"
          <| fun _ ->
              // `key_premises` assumes `render_where` / `render_order` injective; the differential
              // instantiates them at the declaration codec's `where` / `orderBy` members, so the
              // premise is measured here on the shipped codec: every ADMITTED drawn declaration
              // reads back to itself (so its member texts determine its filter and order), two
              // distinct drawn filters never render one text, and the model's key — with
              // production's renderers — moves when the shape is dropped, and keys as the pre-398
              // key (`invocation_key_unshaped`) once it is.
              let mutable rng = ConfRng.ofSeed 3983
              let texts = System.Collections.Generic.Dictionary<string, ColumnPredicate list>()
              let mutable admitted = 0
              let mutable shaped = 0

              for i in 1..400 do
                  let q, r = genQueryDecl ("q-" + string i) rng
                  rng <- r

                  match QueryRegistry.register q QueryRegistry.empty with
                  | Error _ -> ()
                  | Ok _ ->
                      admitted <- admitted + 1

                      Expect.equal
                          (QueryCodec.decode (QueryCodec.encode q))
                          (Ok q)
                          "an admitted declaration round-trips"

                      if not (List.isEmpty q.Where && List.isEmpty q.OrderBy) then
                          shaped <- shaped + 1
                          let mq = queryToModel q
                          let bare = queryToModel { q with Where = []; OrderBy = [] }
                          let args = [ "p0", Cell.Int 1 ] |> qArgsToModel

                          Expect.notEqual
                              (ModelQuery.invocation_key queryRenderers mq args)
                              (ModelQuery.invocation_key queryRenderers bare args)
                              "the model's key reads the shape"

                          Expect.equal
                              (ModelQuery.invocation_key queryRenderers bare args)
                              (Query.invocationKey { q with Where = []; OrderBy = [] } [ "p0", Cell.Int 1 ])
                              "an unshaped declaration keys as production's"

                      if not (List.isEmpty q.Where) then
                          let text = queryRenderers.render_where (q.Where |> List.map qPredToModel)

                          match texts.TryGetValue text with
                          | true, seen -> Expect.equal seen q.Where (sprintf "one filter text, one filter: %s" text)
                          | _ -> texts[text] <- q.Where

              Expect.isGreaterThan admitted 100 (sprintf "declarations were admitted (admitted=%d)" admitted)
              Expect.isGreaterThan shaped 40 (sprintf "of them filtered or ordered (shaped=%d)" shaped)
              Expect.isGreaterThan texts.Count 20 (sprintf "distinct filters were rendered (filters=%d)" texts.Count)

          testCase
              "no resolver runs on a refused dispatch — `unregistered_refused` and `validate_before_resolve`, on the shipped seam"
          <| fun _ ->
              // The two theorems' statements instantiated on production: an unregistered id and a
              // rejected argument set each return the typed refusal with the resolver untouched,
              // and the result is the same under a resolver that would have failed.
              let q: Query =
                  { Id = "q-t"
                    Params =
                      [ { Name = "p0"
                          Type = IntType
                          Required = true } ]
                    ResultSchema = [ Field.create "n" IntType ]
                    Effect =
                      { Host = ReadsHost
                        Determinism = Effect.network }
                    Source = Ref "src-t"
                    TimeoutMs = None
                    PageSize = None
                    Where = []
                    OrderBy = [] }

              let reg =
                  match QueryRegistry.register q QueryRegistry.empty with
                  | Ok r -> r
                  | Error e -> failtestf "registration refused: %A" e

              let ran = ref 0

              let settling (_: Query) =
                  ran.Value <- ran.Value + 1
                  Ready(queryResultOf 1)

              let failing (_: Query) =
                  ran.Value <- ran.Value + 1
                  Failed "boom"

              let pending (_: Query) : Deferred<QueryResult> =
                  ran.Value <- ran.Value + 1
                  Pending

              // `unregistered_refused`
              for resolve in [ settling; failing; pending ] do
                  Expect.equal
                      (QueryRegistry.dispatch reg "q-u" [ "p0", Cell.Int 1 ] resolve)
                      (Error(NoSuchQuery("q-u", [ "q-t" ])))
                      "an unregistered id is NoSuchQuery, naming the id and the registry's ids, under every resolver"

              // `validate_before_resolve`, one argument set per refusal class
              for args in
                  [ [ "p0", Cell.Str "nope" ]
                    [ "zz", Cell.Int 1 ]
                    []
                    [ "p0", Cell.Int 1; "zz", Cell.Null ] ] do
                  let refusal = Query.validateParams q args
                  Expect.isError refusal (sprintf "validation rejects %A" args)

                  for resolve in [ settling; failing; pending ] do
                      Expect.equal
                          (QueryRegistry.dispatch reg "q-t" args resolve)
                          (refusal |> Result.map (fun () -> Pending))
                          (sprintf "a rejected set %A IS validation's refusal, under every resolver" args)

              Expect.equal ran.Value 0 "and no resolver ran on any of them"

              // past an accepted validation the resolver's three answers ride out as the three outcomes
              let accepted = [ "p0", Cell.Int 1 ]

              Expect.equal
                  (QueryRegistry.dispatch reg "q-t" accepted settling)
                  (Ok(Ready(queryResultOf 1)))
                  "a settling resolver settles"

              Expect.equal
                  (QueryRegistry.dispatch reg "q-t" accepted pending)
                  (Ok Pending)
                  "a pending one stays pending"

              Expect.equal
                  (QueryRegistry.dispatch reg "q-t" accepted failing)
                  (Error(ExecutionFailed("boom", [])))
                  "a failing resolver is the typed ExecutionFailed, never Ok(Failed _)"

              Expect.equal ran.Value 3 "and each of those ran its resolver once"

              // and the model says the same through the same theorems' clauses
              let mreg =
                  match ModelQuery.register (queryToModel q) ModelQuery.empty with
                  | ModelQuery.Ok r -> r
                  | ModelQuery.Error _ -> failtest "the model refused the registration"

              Expect.equal
                  (ModelQuery.dispatch mreg "q-u" (qArgsToModel accepted) (fun _ -> ModelQuery.Ready(queryResultOf 1)))
                  (ModelQuery.Error(ModelQuery.NoSuchQuery("q-u", [ "q-t" ])))
                  "the model refuses the unregistered id the same way"

              Expect.equal (ModelQuery.ids (ModelQuery.enumerate mreg)) [ "q-t" ] "and enumerates the registered one"

          testCase "the first finding is CLOSED on the shipped seam — `required_is_non_null`"
          <| fun _ ->
              // Phase 226. `all_null_accepted` was pinned here as "the finding holds": a REQUIRED
              // param bound to Null passed validation and the resolver ran. `Required` now means a
              // VALUE — a third step refuses a required param bound only to Null as the DISTINCT
              // `RequiredParamsNull`, apart from `RequiredParamsUnbound` (left out) — the model proves
              // the exact characterisation (`required_is_non_null`) and what the all-Null set gets
              // (`all_null_refusal_exact`), and this case pins the CLOSURE on production.
              // The ladder row `query-required-is-non-null` states it.
              let q: Query =
                  { Id = "q-f"
                    Params =
                      [ { Name = "a"
                          Type = StringType
                          Required = true }
                        { Name = "b"
                          Type = StringType
                          Required = false } ]
                    ResultSchema = [ Field.create "n" IntType ]
                    Effect =
                      { Host = ReadsHost
                        Determinism = Effect.network }
                    Source = Ref "src-f"
                    TimeoutMs = None
                    PageSize = None
                    Where = []
                    OrderBy = [] }

              let mq = queryToModel q

              Expect.equal
                  mq.q_params
                  ModelQuery.collision_params
                  "the declaration IS the one the model exhibits its findings on"

              // The all-Null set, BUILT from the declaration: refused as present-but-null, naming the
              // required param only.
              let nulls = [ "a", Cell.Null; "b", Cell.Null ]

              Expect.equal
                  (qArgsToModel nulls)
                  (ModelQuery.nulls_of mq.q_params)
                  "the argument set IS the model's `nulls_of`"

              Expect.equal
                  (Query.validateParams q nulls)
                  (Error(RequiredParamsNull [ "a" ]))
                  "every param bound to Null is REFUSED as RequiredParamsNull, naming the required one and only it"

              Expect.equal
                  (ModelQuery.required_names mq.q_params)
                  [ "a" ]
                  "which is the model's `required_names`, what `all_null_refusal_exact` says it names"

              Expect.equal
                  (Query.validateParams q [ "a", Cell.Null ])
                  (Error(RequiredParamsNull [ "a" ]))
                  "the required param bound to Null on its own is refused the same way"

              Expect.equal
                  (Query.validateParams q [])
                  (Error(RequiredParamsUnbound [ "a" ]))
                  "while leaving it out is still RequiredParamsUnbound — present-but-null and missing are told apart"

              Expect.equal
                  (Query.validateParams q [ "a", Str "x"; "b", Cell.Null ])
                  (Ok())
                  "an OPTIONAL param bound to Null is still accepted — `Required` is what moved"

              // Phase 307 — a name bound TWICE is refused before `Required` is asked: one name takes
              // one cell (`DuplicateParam`, `validate_params_distinct`). This list was accepted
              // here until then, while a resolver reading it as a map saw `Null`.
              Expect.equal
                  (Query.validateParams q [ "a", Cell.Null; "a", Str "x" ])
                  (Error(DuplicateParam "a"))
                  "a required name bound twice is refused as a repeat, whatever either binding says"

              let ran = ref false

              Expect.equal
                  (Query.invoke q [ "a", Cell.Null ] (fun _ ->
                      ran.Value <- true
                      Pending))
                  (Error(RequiredParamsNull [ "a" ]))
                  "so the resolver is no longer reached with its required param absent"

              Expect.isFalse ran.Value "and it did not run"

          testCase
              "the second finding is CLOSED on the shipped seam — `invocation_key_injective`, in both seams and the pipeline"
          <| fun _ ->
              // Phase 225. `key_collision` was pinned here as "the finding holds"; the pre-image now
              // goes through `Hash.canonicalFields`, the models prove it injective
              // (`invocation_key_injective` in `Query.fst` and `Capability.fst`), and this case pins
              // the CLOSURE on production: the pre-225 exhibits key apart, the model's key is still
              // production's, and the premise the proofs take of the escaper is measured.
              let q: Query =
                  { Id = "q-f"
                    Params =
                      [ { Name = "a"
                          Type = StringType
                          Required = true }
                        { Name = "b"
                          Type = StringType
                          Required = false } ]
                    ResultSchema = [ Field.create "n" IntType ]
                    Effect =
                      { Host = ReadsHost
                        Determinism = Effect.network }
                    Source = Ref "src-f"
                    TimeoutMs = None
                    PageSize = None
                    Where = []
                    OrderBy = [] }

              let mq = queryToModel q
              let one = [ "a", Cell.Str "1b=s2" ]
              let two = [ "a", Cell.Str "1"; "b", Cell.Str "2" ]
              Expect.equal (qArgsToModel one) ModelQuery.collision_one "the first set IS the model's exhibit"
              Expect.equal (qArgsToModel two) ModelQuery.collision_two "the second set IS the model's exhibit"
              Expect.equal (Query.validateParams q one) (Ok()) "the first set is accepted"
              Expect.equal (Query.validateParams q two) (Ok()) "the second set is accepted"

              Expect.notEqual
                  (ModelQuery.canonical queryRenderers ModelQuery.collision_one)
                  (ModelQuery.canonical queryRenderers ModelQuery.collision_two)
                  "the two exhibits no longer share a PRE-IMAGE"

              Expect.notEqual
                  (Query.invocationKey q one)
                  (Query.invocationKey q two)
                  "and so no longer share a capture key on production"

              Expect.equal
                  (ModelQuery.invocation_key queryRenderers mq ModelQuery.collision_one)
                  (Query.invocationKey q one)
                  "and the model's key is still production's"

              // The one place the float renderer's injectivity premise fails, pinned so it cannot
              // be forgotten: `FloatLayout.canonical` renders -0.0 and 0.0 alike, so the two share a
              // key — and production's own `Cell` equality calls them EQUAL (IEEE -0.0 = 0.0), so the
              // sharing is between argument sets production does not tell apart
              // (`query-renderers-abstract`).
              let qz: Query =
                  { q with
                      Id = "q-z"
                      Params =
                          [ { Name = "x"
                              Type = FloatType
                              Required = true } ] }

              Expect.equal
                  [ "x", Cell.Float -0.0 ]
                  [ "x", Cell.Float 0.0 ]
                  "production calls the two argument sets equal"

              Expect.equal
                  (Query.invocationKey qz [ "x", Cell.Float -0.0 ])
                  (Query.invocationKey qz [ "x", Cell.Float 0.0 ])
                  "and keys them alike"

              // The capability seam's pre-225 join spliced `addr=value` pairs on U+0001, so its
              // collision needed that byte in a value — the shard said "no separator", which was
              // true of `Query` and not of `Capability`. Both exhibits, keyed apart.
              let cap =
                  Capability.create "cap-k" (fst (genCapSignature "k" (ConfRng.ofSeed 225))) Server

              let cOne = [ "a", "1\u0001b=2" ]
              let cTwo = [ "a", "1"; "b", "2" ]
              let cThree = [ "a", "1b=2" ]

              Expect.notEqual
                  (Capability.invocationKey cap cOne)
                  (Capability.invocationKey cap cTwo)
                  "a value carrying the old join byte no longer spells the next binding"

              Expect.notEqual
                  (Capability.invocationKey cap cThree)
                  (Capability.invocationKey cap cTwo)
                  "nor does one carrying `=`"

              Expect.equal
                  (Capability.invocationKey cap [ "b", "2"; "a", "1" ])
                  (Capability.invocationKey cap cTwo)
                  "and the key is still blind to the caller's order"

              // The pipeline's node key joined `addr=L:value` on the empty string; its readable
              // prefix spliced two ids on `#`. Both collisions, closed.
              let node args =
                  Invoke("n", "cap-k", IntRange(0, 1), args)

              Expect.notEqual
                  (CapabilityPipeline.nodeInvocationKey (node [ "a", Literal "1b=L:2" ]))
                  (CapabilityPipeline.nodeInvocationKey (node [ "a", Literal "1"; "b", Literal "2" ]))
                  "a literal no longer spells the next binding"

              Expect.notEqual
                  (CapabilityPipeline.nodeInvocationKey (node [ "a", Literal "x" ]))
                  (CapabilityPipeline.nodeInvocationKey (node [ "a", FromNode "x" ]))
                  "a literal and an upstream reference with the same text key apart"

              Expect.notEqual
                  (CapabilityPipeline.nodeInvocationKey (Invoke("b#n", "cap", IntRange(0, 1), [])))
                  (CapabilityPipeline.nodeInvocationKey (Invoke("n", "cap#b", IntRange(0, 1), [])))
                  "two ids a `#` splices alike hash apart"

              Expect.notEqual
                  (CapabilityPipeline.nodeInvocationKey (Source("a#b", "c", IntRange(0, 1))))
                  (CapabilityPipeline.nodeInvocationKey (Source("a", "b#c", IntRange(0, 1))))
                  "and so do two sources"

              Expect.equal
                  (CapabilityPipeline.nodeInvocationKey (node [ "b", Literal "2"; "a", Literal "1" ]))
                  (CapabilityPipeline.nodeInvocationKey (node [ "a", Literal "1"; "b", Literal "2" ]))
                  "and the node key is still blind to the caller's order"

          testCase
              "the escaper premise holds of Hash.canonicalFields, and a separator WITHOUT an escape refutes it — the measurement can fail"
          <| fun _ ->
              // `field_faithful` (Query.fst section 8b, Capability.fst section 12) says production's
              // `canonicalField` is the modelled escaper followed by one terminator. What the
              // injectivity theorems SPEND of it is that the encoding decodes: a round trip through
              // an independently written decoder over adversarial field lists — both symbols, `=`,
              // `#`, empty fields and empty lists — is that property measured. The go-red is the
              // encoding the shard's first reading proposed, a bare U+0001 separator: it must lose.
              let mutable rng = ConfRng.ofSeed 2251
              let mutable drawn = 0
              let mutable withSymbols = 0
              let mutable bareLost = 0

              for _ in 1..2000 do
                  let fields, r = genAdversarialFields rng
                  rng <- r
                  drawn <- drawn + 1

                  if fields |> List.exists (fun f -> f.Contains "\u0001" || f.Contains "\u0010") then
                      withSymbols <- withSymbols + 1

                  Expect.equal
                      (decodeFields (Hash.canonicalFields fields))
                      (Some fields)
                      (sprintf "canonicalFields decodes back to %A" fields)

                  Expect.equal
                      (Hash.canonicalFields fields)
                      (fields |> List.map Hash.canonicalField |> String.concat "")
                      "canonicalFields is canonicalField, concatenated"

                  let bare = fields |> List.map (fun f -> f + Hash.foldSep) |> String.concat ""

                  if decodeFields bare <> Some fields then
                      bareLost <- bareLost + 1

              Expect.isGreaterThan withSymbols 200 "the draw put an encoding symbol inside a field often"
              Expect.isGreaterThan bareLost 0 "and a bare separator loses on it — the round trip can fail"

              // The capability model's key agrees with production's over adversarial invocations,
              // byte for byte, and is held still under a reversal of distinct addresses.
              let cap =
                  Capability.create "cap-k" (fst (genCapSignature "k" (ConfRng.ofSeed 225))) Server

              let mcap = capToModel cap
              let mutable compared = 0

              for _ in 1..500 do
                  let fields, r = genAdversarialFields rng
                  rng <- r
                  let args = fields |> List.mapi (fun i v -> sprintf "h%d" (i % 3), v)
                  compared <- compared + 1

                  Expect.equal
                      (ModelCap.invocation_key keyRenderers mcap args)
                      (Capability.invocationKey cap args)
                      (sprintf "the capability model's key is production's on %A" args)

              Expect.equal compared 500 "every drawn invocation was compared"

              // And the query model's key agrees with production's over string cells drawn from the
              // same alphabet. The query differential's own generator draws no encoding symbol, so
              // it cannot tell an escaping field from a bare one — measured: a `field` that only
              // appends the terminator leaves it green. These rows are what can.
              let qa: Query =
                  { Id = "q-adv"
                    Params = []
                    ResultSchema = [ Field.create "n" IntType ]
                    Effect =
                      { Host = ReadsHost
                        Determinism = Effect.network }
                    Source = Ref "src-adv"
                    TimeoutMs = None
                    PageSize = None
                    Where = []
                    OrderBy = [] }

              let mqa = queryToModel qa
              let mutable qCompared = 0

              for _ in 1..500 do
                  let fields, r = genAdversarialFields rng
                  rng <- r
                  let args = fields |> List.mapi (fun i v -> sprintf "p%d" (i % 3), Cell.Str v)
                  qCompared <- qCompared + 1

                  Expect.equal
                      (ModelQuery.invocation_key queryRenderers mqa (qArgsToModel args))
                      (Query.invocationKey qa args)
                      (sprintf "the query model's key is production's on %A" args)

              Expect.equal qCompared 500 "every drawn argument set was compared"

          // ---- Phase 455 — the query codec, whole ----

          testCase
              "the query-codec oracle agrees with QueryCodec's declaration, result, async-result and query-error codecs over generated values and mutated documents, and every typed refusal resolves"
          <| fun _ ->
              let t = queryCodecDifferential (ModelQuery.query_json columnCodec) 4552 200

              Differential.expectAgrees "query codec" t

              Expect.isGreaterThan
                  (Tally.count "Documents" t)
                  3000
                  (sprintf "documents were read (%d)" (Tally.count "Documents" t))

              Expect.isGreaterThan
                  (Tally.count "AdmissionRefused" t)
                  10
                  (sprintf
                      "an UNMUTATED declaration was refused by the admission gate on read (%d)"
                      (Tally.count "AdmissionRefused" t))

              Expect.isGreaterThan
                  (Tally.count "Refused" t)
                  300
                  (sprintf "documents were refused (%d)" (Tally.count "Refused" t))

              for c in [ "MissingField"; "WrongKind"; "UnknownTag"; "OutOfRange" ] do
                  Expect.isTrue
                      (Set.contains c (Tally.reached "Codes" t))
                      (sprintf "a %s refusal was compared (%A)" c (Tally.reached "Codes" t))

          testCase
              "a query-codec oracle whose declaration writer DROPS a parameter DISAGREES with QueryCodec — the measurement can fail"
          <| fun _ ->
              let t = queryCodecDifferential droppingQueryJson 4553 60
              Differential.expectLoses "a writer that drops a parameter" t

              Expect.isTrue
                  (Tally.disagreements t |> List.exists (fun d -> d.Contains "encodes differently"))
                  "and the disagreement is about the encoding, which is what the writer dropped" ]
