namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Query — the declarative, cross-domain data-acquisition seam
//  (Phase 46). The data-acquisition *sibling* to `Capability`:
//
//    Capability  — invocable compute   (typed inputs -> typed output; host body).
//    Query       — data acquisition    (a declared external fetch -> a Table).
//
//  A `Query` is *data* — a serialisable declaration of external data to fetch
//  (relational rows, retrieval hits, file/asset ingest, reference lookup),
//  typed by the `Table`/`Schema` it produces. The wire carries the declaration,
//  never the host body; the host supplies the resolver (the witness pattern
//  applied to data acquisition — the Core never sees a concrete source kind).
//
//  Cross-cutting, mirrored from `Capability` (Phase 30):
//    * default-deny registry (an unregistered id is a named error);
//    * typed param validation before any fetch (named errors, never a throw);
//    * Phase 27 capture keying (`invocationKey`) so a non-`Deterministic`
//      query replays byte-identically;
//    * Fable-clean canonical codec (`Json`/`Decode`, FSharp.Core only).
//
//  The async axis rides the shipped `Deferred<'T>` envelope (Phase 32, in
//  `Fuaran.Core.Function`): the host resolver returns `Deferred<QueryResult>`,
//  so "not yet" is expressible in the substrate's own vocabulary instead of
//  each adopter inventing one at the resolver boundary (Phase 198). The ERROR
//  axis stays typed — `invoke` returns `Result<Deferred<QueryResult>,
//  QueryError>`, and a resolver's `Failed` is projected into the enumerated
//  `ExecutionFailed`. So a dispatch has exactly three outcomes — SETTLED
//  (`Ok(Ready r)`), PENDING (`Ok Pending`) and REFUSED (`Error e`, typed) — and
//  `Ok(Failed _)` is unreachable by construction, which `queryLaws` certifies
//  rather than this comment merely asserting.
//
//  What this deliberately is NOT: an async runtime. `Deferred` is data;
//  scheduling, polling and completion stay the host's. `Pending` carries no
//  handle — the host correlates a pending fetch by `invocationKey`, which is a
//  function of the declaration and the validated args alone.
//
//  The sibling framing above covers the ASYNC axis too, since Phase 210:
//  `Capability.invoke` takes `body: unit -> Deferred<'v>` and returns
//  `Result<Deferred<'v>, InvokeError>`, projecting a body's `Failed` into the
//  enumerated `BodyFailed` exactly as this seam projects a resolver's into
//  `ExecutionFailed`. So both seams a host adopts have the same three outcomes
//  and the same unreachable fourth, and each keeps its OWN typed error — which
//  is what the sibling relation does and does not mean: one async shape, two
//  error vocabularies. (`Query` was the first to carry the envelope; Phase 198
//  recorded the asymmetry it left behind, and 210 closed it.)
// ============================================================================

/// A typed parameter a query expects at invocation — the data-acquisition analogue of a
/// `Capability` hole. Typed by a `ColumnType` (the scalar set the result columns also use), so a
/// bound value's `Cell` shape must agree with `Type`, or be `Null`.
///
/// `Required` means the parameter must be bound to a VALUE. `validateParams` refuses a required
/// parameter that is left out as `RequiredParamsUnbound`, and one that is present but bound only to
/// `Null` (a `Null` binding is absence) as `RequiredParamsNull`. An optional parameter may be left
/// out or bound to `Null`. This has held since Phase 226. Before it, `Required` checked only that
/// the NAME was present, so a required parameter bound to `Null` reached the resolver.
type QueryParam =
    {
        /// The key an argument binds under. Keep it unique within a query: `validateParams`
        /// matches an argument to the first parameter of its name.
        Name: string
        /// The declared scalar type; a bound cell must widen to it (`ColumnType.widens`) or be `Null`.
        Type: ColumnType
        /// Whether the parameter must be bound to a non-`Null` value before the resolver runs.
        Required: bool
    }

/// One typed predicate over a column of a query's result (Phase 398, DECISIONS.md D128) — a member of
/// the closed conjunction a `Query.Where` declares. Every case names a column of `ResultSchema`; a
/// case carrying a literal carries a present `Cell` of EXACTLY that column's type (no widening, so
/// one filter has one spelling), well-formed as `Table.validate` reads it. `QueryRegistry.register`,
/// `replace` and `QueryCodec`'s declaration reader refuse anything else, by name.
///
/// The meaning a resolver honours: a comparison orders by `Cell.compare`, THE cell order (Phase
/// 315: numbers numerically, NaN last, decimals exactly, strings, dates and timestamps ordinally,
/// `false` before `true`); `Contains` is an ordinal, case-sensitive substring test; a `Null` cell
/// satisfies only `IsNull`. A resolver that cannot honour a predicate refuses it with
/// `ResolveFault.PredicateUnsupported` — it never ignores one.
[<RequireQualifiedAccess>]
type ColumnPredicate =
    /// The column's cell equals `value`.
    | EqualTo of column: string * value: Cell
    /// The column's cell orders after `value` — an exclusive lower bound.
    | GreaterThan of column: string * value: Cell
    /// The column's cell orders at or after `value` — an inclusive lower bound.
    | AtLeast of column: string * value: Cell
    /// The column's cell orders before `value` — an exclusive upper bound.
    | LessThan of column: string * value: Cell
    /// The column's cell orders at or before `value` — an inclusive upper bound.
    | AtMost of column: string * value: Cell
    /// The column's string cell contains `text`, compared ordinally and case-sensitively. Applies to
    /// a `string` column only.
    | Contains of column: string * text: string
    /// The column's cell is `Null`.
    | IsNull of column: string
    /// The column's cell is present.
    | IsNotNull of column: string

/// The direction of one key of a query's declared order (Phase 398).
[<RequireQualifiedAccess>]
type SortDirection =
    /// Smallest first by `Cell.compare`, a `Null` cell before every present one.
    | Ascending
    /// Largest first by `Cell.compare`, a `Null` cell after every present one.
    | Descending

/// One key of a query's declared order (Phase 398): a column of `ResultSchema` and its direction.
/// The first key orders the rows, and each later key orders the rows the keys before it tie.
type SortKey =
    {
        /// A column of the query's `ResultSchema`; an order names each column once.
        Column: string
        /// Which end of the order comes first.
        Direction: SortDirection
    }

/// A query declaration: a named, registrable data-acquisition contract. Pure data — it carries no
/// host code. `Effect` reuses `Function`'s two-axis `EffectClass` verbatim (an empty determinism set = pure
/// relational/synthetic, re-evaluable; a set naming `Network` and/or `Clock` = captured result replayed). `Source`
/// is the `Column` `DataSource` (`Embedded` template or host-resolved `Ref`). `ResultSchema` is the
/// typed shape the query produces — so a UI can be typed against it in a schema-only (no-rows) fetch.
type Query =
    {
        /// The registry key and the prefix of every `invocationKey`; a registry refuses a
        /// second query under the same id.
        Id: string
        /// The parameters an invocation may bind, in declaration order; an argument naming none
        /// of them is refused as `UnknownParam`.
        Params: QueryParam list
        /// The columns of one result row, as (name, type) pairs in column order.
        ResultSchema: Schema
        /// Tells the caller whether to journal: a non-empty determinism set means the realized
        /// result is captured under `invocationKey`, an empty one that the query re-evaluates.
        Effect: EffectClass
        /// Where the rows come from — an `Embedded` table, or a `Ref` only the host resolver
        /// can resolve (one it cannot is answered as `SourceNotResolved`).
        Source: DataSource
        /// The fetch's time budget in milliseconds, for the host resolver to honour; the seam
        /// itself times nothing. `None` declares no budget, and the codec omits the member.
        TimeoutMs: int option
        /// The rows per page the host should return; `None` leaves paging to the source, and
        /// the codec omits the member.
        PageSize: int option
        /// The filter the host applies to the rows (Phase 398): a CONJUNCTION — a row is returned
        /// when it satisfies every predicate. Empty declares no filter, and the codec omits the
        /// member, so a declaration without one encodes as it did before the member existed.
        Where: ColumnPredicate list
        /// The order the host returns the rows in (Phase 398), most significant key first. Empty
        /// leaves the order to the source, and the codec omits the member.
        OrderBy: SortKey list
    }

/// A query invocation result — a page of typed rows. `TotalRowCount`/`NextPageToken` are present
/// when the source can report them (streaming/paging); `None` when unknown.
type QueryResult =
    {
        /// The page's rows — always an embedded table; the codec refuses a result whose rows
        /// are a `Ref`.
        Rows: Table
        /// The page's ordinal. The seam imposes no base; the shipped samples and laws number
        /// from 0.
        PageNum: int
        /// The row count across every page, where the source reports it.
        TotalRowCount: int option
        /// The opaque token a host hands back to fetch the next page; `None` on the last page
        /// or from a source that does not page.
        NextPageToken: string option
    }

/// Why a typed invocation (or a registration) was refused — total, names the failure and, where a
/// closed set is expected, enumerates the alternatives (GP5). Default-deny by shape: only a
/// registered id with in-type params dispatches.
type QueryError =
    /// The id is not registered; `known` lists every registered id, in id order.
    | NoSuchQuery of id: string * known: string list
    /// A registration under an id the registry already holds; the registry is left unchanged.
    | DuplicateQuery of id: string
    /// An argument names no declared parameter; `declared` lists the parameter names in
    /// declaration order.
    | UnknownParam of name: string * declared: string list
    /// An argument's cell type does not widen to its parameter's declared type.
    | ParamTypeMismatch of name: string * expected: ColumnType * got: ColumnType
    /// The required params the args leave out entirely, in declaration order.
    | RequiredParamsUnbound of names: string list
    /// The resolver could not resolve the query's `Ref` source (`ResolveFault.SourceMissing`).
    | SourceNotResolved of ref: string
    /// The resolver ran and failed; `recoverable` names the arguments a retry may change, and
    /// is empty when the resolver answered an untyped `Deferred.Failed`. Only a resolver that ran
    /// answers it: since Phase 385 input `QueryCodec.decodeArgs` cannot read is `UnreadableArgs`.
    | ExecutionFailed of detail: string * recoverable: string list
    /// The resolver ran out of time (`ResolveFault.TimedOut`); the seam itself never times a fetch.
    | Timeout
    /// The required params the args bind only to `Null` — present, but bound to no value (Phase 226).
    /// Distinct from `RequiredParamsUnbound` (left out), so a caller can tell the two apart.
    | RequiredParamsNull of names: string list
    /// An invocation binds this parameter twice (Phase 307) — the query seam's `DuplicateArg` — or,
    /// since Phase 316, a declaration handed to `QueryRegistry.register` / `replace` names it twice.
    /// Refused at the second binding: one name takes one cell, and with two the `Map.ofList`
    /// reading and the "some binding has a value" reading of `Required` answered differently (a
    /// required `a` bound `[a = 1; a = Null]` was accepted while a resolver reading the map saw
    /// `Null`), and the capture key depended on the order of the list.
    | DuplicateParam of name: string
    /// The registry's policy DENIED the invocation (Phase 318): the gate named `policy` refused it,
    /// with `reason` and the `allowed` alternatives its guidance enumerates. Raised after the
    /// parameters validated and before the resolver: no resolver ran, and nothing was captured.
    /// Prefixed `Query…` because `InvokeError.PolicyRefused` is a case in the same namespace, and an
    /// unqualified case name shared by two unions resolves to whichever was declared last.
    | QueryPolicyRefused of policy: string * reason: string * allowed: string list
    /// The registry's policy answered `NeedsApproval` (Phase 318): the gate named `policy` will not
    /// let the query run until an approval is given. No resolver ran.
    | QueryApprovalRequired of policy: string
    /// The arguments could not be read at all (Phase 385): `QueryCodec.decodeArgs` was handed text
    /// that is not JSON, a document that is not one object keyed by parameter name, or a parameter
    /// value of a JSON kind no column type spells (an array or an object). `error` is the decode
    /// refusal: its code, the path to the value at fault within the argument document, and its
    /// sentence. No resolver ran. Before Phase 385 these were answered `ExecutionFailed`, a refusal
    /// of a fetch that never ran.
    | UnreadableArgs of error: DecodeError
    /// A `Where` predicate or an `OrderBy` key names a column the declaration's `ResultSchema` does
    /// not declare (Phase 398); `declared` lists the result's columns in schema order. Refused at
    /// registration and by the declaration reader.
    | UnknownColumn of name: string * declared: string list
    /// A `Where` predicate compares its column with a literal of another type (Phase 398): a
    /// literal's type must be the column's own, exactly. Refused at registration and by the
    /// declaration reader.
    | PredicateTypeMismatch of column: string * expected: ColumnType * got: ColumnType
    /// A `Where` predicate of kind `predicate` (its wire tag) does not apply to `column`, whose type
    /// is `columnType` (Phase 398): `contains` applies to a `string` column only.
    | PredicateNotApplicable of predicate: string * column: string * columnType: ColumnType
    /// A `Where` predicate's literal is not a value its column can carry (Phase 398): a `Null`
    /// (test absence with `isNull`), a non-finite float, or decimal, date or timestamp text that is
    /// not canonical. `reason` says which.
    | IllFormedLiteral of column: string * reason: string
    /// An `OrderBy` names `column` more than once (Phase 398); an order names each column once.
    | DuplicateSortColumn of column: string
    /// The resolver cannot apply this `Where` predicate (`ResolveFault.PredicateUnsupported`, Phase
    /// 398): the fetch was refused rather than run without it.
    | PredicateNotHonoured of predicate: ColumnPredicate
    /// The resolver cannot order the rows by this column (`ResolveFault.OrderUnsupported`, Phase
    /// 398): the fetch was refused rather than returned in another order.
    | OrderNotHonoured of column: string

/// A resolver's typed failure (Phase 295) — what `Query.invokeWithArgs`'s resolver answers when the
/// fetch cannot complete, so the refusals the resolver alone can know of reach the caller as the
/// `QueryError` that names them: a `Ref` the host could not resolve is `SourceNotResolved`, a fetch
/// that ran out of time is `Timeout`, and any other failure is `ExecutionFailed` with the arguments a
/// retry may change (`recoverable`). Before Phase 295 the resolver could answer only a string
/// (`Deferred.Failed`), so the first two cases were unreachable and `recoverable` was always empty.
[<RequireQualifiedAccess>]
type ResolveFault =
    /// The host could not resolve the `Ref` named; surfaces as `QueryError.SourceNotResolved`.
    | SourceMissing of ref: string
    /// The fetch ran out of time; surfaces as `QueryError.Timeout`.
    | TimedOut
    /// Any other failure, with the arguments a retry may change; surfaces as
    /// `QueryError.ExecutionFailed` carrying both unchanged.
    | Failed of detail: string * recoverable: string list
    /// The host cannot apply this predicate of the query's `Where` (Phase 398); surfaces as
    /// `QueryError.PredicateNotHonoured` naming it. The answer a resolver gives instead of
    /// returning rows the filter was not applied to.
    | PredicateUnsupported of predicate: ColumnPredicate
    /// The host cannot order by this column of the query's `OrderBy` (Phase 398); surfaces as
    /// `QueryError.OrderNotHonoured` naming it.
    | OrderUnsupported of column: string

/// The `Where` / `OrderBy` shape of a declaration (Phase 398): its wire form, its reader, and its
/// admission — the checks `QueryRegistry.admissionFault` runs, so the registry and the declaration
/// reader refuse the same declarations with the same error.
module internal QueryShape =

    /// A string from a closed set; a miss is `UnknownTag`, in the query codec's sentence
    /// `<what><value>`.
    let tagged (what: string) (cases: (string * 'T) list) : Decoder<'T> =
        Decoder.str
        |> Decoder.andThen (fun s ->
            match cases |> List.tryFind (fun (k, _) -> k = s) with
            | Some(_, v) -> Ok v
            | None ->
                Error(
                    DecodeError.make
                        DecodeCode.UnknownTag
                        ("one of "
                         + (cases |> List.map (fun (k, _) -> "'" + k + "'") |> String.concat ", "))
                        (what + s)
                ))

    /// A column type, spelled by `ColumnType.tag`.
    let colTypeOf: Decoder<ColumnType> =
        tagged "unknown column type: " (ColumnType.all |> List.map (fun t -> ColumnType.tag t, t))

    /// A predicate's wire tag.
    let tag (p: ColumnPredicate) : string =
        match p with
        | ColumnPredicate.EqualTo _ -> "equalTo"
        | ColumnPredicate.GreaterThan _ -> "greaterThan"
        | ColumnPredicate.AtLeast _ -> "atLeast"
        | ColumnPredicate.LessThan _ -> "lessThan"
        | ColumnPredicate.AtMost _ -> "atMost"
        | ColumnPredicate.Contains _ -> "contains"
        | ColumnPredicate.IsNull _ -> "isNull"
        | ColumnPredicate.IsNotNull _ -> "isNotNull"

    /// Every predicate wire tag, in case order.
    let tags =
        [ "equalTo"
          "greaterThan"
          "atLeast"
          "lessThan"
          "atMost"
          "contains"
          "isNull"
          "isNotNull" ]

    /// The column a predicate names.
    let column (p: ColumnPredicate) : string =
        match p with
        | ColumnPredicate.EqualTo(c, _)
        | ColumnPredicate.GreaterThan(c, _)
        | ColumnPredicate.AtLeast(c, _)
        | ColumnPredicate.LessThan(c, _)
        | ColumnPredicate.AtMost(c, _)
        | ColumnPredicate.Contains(c, _)
        | ColumnPredicate.IsNull c
        | ColumnPredicate.IsNotNull c -> c

    /// The literal a comparison carries, or `None` for `contains` and the null tests.
    let literal (p: ColumnPredicate) : Cell option =
        match p with
        | ColumnPredicate.EqualTo(_, v)
        | ColumnPredicate.GreaterThan(_, v)
        | ColumnPredicate.AtLeast(_, v)
        | ColumnPredicate.LessThan(_, v)
        | ColumnPredicate.AtMost(_, v) -> Some v
        | ColumnPredicate.Contains _
        | ColumnPredicate.IsNull _
        | ColumnPredicate.IsNotNull _ -> None

    /// The one-cell table a literal is checked, written and read as — so a literal is carried by
    /// THE cell codec the column strand owns, exactly as a query argument is (`decodeArgsJson`).
    let private oneCell (column: string) (ty: ColumnType) (c: Cell) : Table =
        // Admission held the literal to its column's type, so the first build succeeds; a cell that
        // somehow does not fit is carried at its own type rather than raised on (total, Phase 417).
        let col =
            match Column.ofCells column ty [ c ] with
            | Ok col -> col
            | Error _ ->
                match Column.ofCells column (Cell.typeOf c |> Option.defaultValue ty) [ c ] with
                | Ok col -> col
                | Error _ -> Column.ofStrs column Vector.empty Vector.empty

        { Schema = [ column, col.Type ]
          Columns = [ col ] }

    /// A literal's JSON value: the value the column codec writes for that one cell. A literal is
    /// written only after admission, which holds it to its column's exact type and to what
    /// `Table.validate` accepts, so the one-cell document always has the shape matched here; the
    /// fallback (the whole document) is unreachable for an admitted declaration and is total
    /// rather than a throw.
    let private literalJson (ty: ColumnType) (c: Cell) : JVal =
        match ColumnCodec.encodeJson (Embedded(oneCell "v" ty c)) with
        | JObj [ _; "columns", JObj [ _, JObj [ "values", JArr [ v ]; _ ] ] ] -> v
        | other -> other

    /// A literal read back at the type its document states, through the column codec's one cell
    /// decoder; `None` when the value is not one a column of that type carries.
    let private literalOf (column: string) (ty: ColumnType) (v: JVal) : Cell option =
        let doc =
            JObj
                [ "schema", JArr [ JObj [ "name", JStr column; "type", JStr(ColumnType.tag ty) ] ]
                  "columns", JObj [ column, JObj [ "values", JArr [ v ]; "validity", JArr [ JBool true ] ] ] ]

        match ColumnCodec.decodeJson doc with
        | Ok(Embedded { Columns = [ c ] }) when Column.length c = 1 -> Some(Column.cell 0 c)
        | _ -> None

    /// A predicate as its wire document: `"$type"` its tag, `column`, and — for a comparison — the
    /// literal's `type` and `value`, or — for `contains` — its `text`.
    let predicateJson (p: ColumnPredicate) : JVal =
        let col = column p

        match p with
        | ColumnPredicate.Contains(_, text) -> Canon.typed (tag p) [ "column", JStr col; "text", JStr text ]
        | ColumnPredicate.IsNull _
        | ColumnPredicate.IsNotNull _ -> Canon.typed (tag p) [ "column", JStr col ]
        | _ ->
            match literal p with
            | Some v ->
                // `Null` has no type: admission refuses it, and nothing reaches here with one.
                let ty = Cell.typeOf v |> Option.defaultValue StringType

                Canon.typed
                    (tag p)
                    [ "column", JStr col
                      "type", JStr(ColumnType.tag ty)
                      "value", literalJson ty v ]
            | None -> Canon.typed (tag p) [ "column", JStr col ]

    /// The reader of `predicateJson`'s documents. A literal that is not a value of the type its
    /// document states is refused `OutOfRange` at `value`, with `IllFormedLiteral`'s reason; the
    /// type it states is held to the column's by admission, not here.
    let predicateDecoder: Decoder<ColumnPredicate> =
        let col = Decoder.field "column" Decoder.str

        let compared (make: string * Cell -> ColumnPredicate) : Decoder<ColumnPredicate> =
            col
            |> Decoder.bind (fun c ->
                Decoder.field "type" colTypeOf
                |> Decoder.bind (fun ty ->
                    Decoder.field "value" (fun v ->
                        match literalOf c ty v with
                        | Some cell -> Ok(make (c, cell))
                        | None ->
                            Error(
                                DecodeError.make
                                    DecodeCode.OutOfRange
                                    ("a " + ColumnType.tag ty + " value")
                                    ("the filter on column '"
                                     + c
                                     + "' compares with a value a "
                                     + ColumnType.tag ty
                                     + " column cannot carry")
                            ))))

        Decoder.tagDispatchWith
            "unknown predicate: "
            "$type"
            [ "equalTo", compared ColumnPredicate.EqualTo
              "greaterThan", compared ColumnPredicate.GreaterThan
              "atLeast", compared ColumnPredicate.AtLeast
              "lessThan", compared ColumnPredicate.LessThan
              "atMost", compared ColumnPredicate.AtMost
              "contains",
              col
              |> Decoder.bind (fun c ->
                  Decoder.field "text" Decoder.str
                  |> Decoder.map (fun t -> ColumnPredicate.Contains(c, t)))
              "isNull", col |> Decoder.map ColumnPredicate.IsNull
              "isNotNull", col |> Decoder.map ColumnPredicate.IsNotNull ]

    /// The members a predicate document of each tag carries, for the strict read policy.
    let predicateMembers (el: JVal) : string list =
        match el with
        | JObj ms ->
            match ms |> List.tryFind (fun (k, _) -> k = "$type") with
            | Some(_, JStr "contains") -> [ "$type"; "column"; "text" ]
            | Some(_, JStr("isNull" | "isNotNull")) -> [ "$type"; "column" ]
            | _ -> [ "$type"; "column"; "type"; "value" ]
        | _ -> []

    /// A direction's wire spelling.
    let private directionTag (d: SortDirection) : string =
        match d with
        | SortDirection.Ascending -> "ascending"
        | SortDirection.Descending -> "descending"

    /// An order key as its wire document.
    let sortKeyJson (k: SortKey) : JVal =
        JObj [ "column", JStr k.Column; "direction", JStr(directionTag k.Direction) ]

    /// The reader of `sortKeyJson`'s documents.
    let sortKeyDecoder: Decoder<SortKey> =
        Decoder.field "column" Decoder.str
        |> Decoder.bind (fun c ->
            Decoder.field
                "direction"
                (tagged
                    "unknown sort direction: "
                    [ "ascending", SortDirection.Ascending; "descending", SortDirection.Descending ])
            |> Decoder.map (fun d -> { Column = c; Direction = d }))

    /// Why a well-typed literal is not one its column carries, from `Table.validate`'s refusal.
    let private literalReason (e: ColumnError) : string =
        match e with
        | NonFiniteFloat(_, v) -> "the float " + v + " is not finite, and the wire has no non-finite float"
        | MalformedShape detail -> detail
        | _ -> "it is not a value its column carries"

    /// The first refusal a `Where` earns against `schema`, with the index of the predicate at
    /// fault: an undeclared column (`UnknownColumn`), `contains` on a column that is not a string
    /// (`PredicateNotApplicable`), a `Null` literal or one its column cannot carry
    /// (`IllFormedLiteral`), and a literal of another type (`PredicateTypeMismatch`), checked in that
    /// order per predicate, predicates in order.
    let whereFault (schema: Schema) (where: ColumnPredicate list) : (int * QueryError) option =
        let declared = schema |> List.map fst

        let fault (p: ColumnPredicate) : QueryError option =
            let c = column p

            match schema |> List.tryFind (fun (n, _) -> n = c) with
            | None -> Some(UnknownColumn(c, declared))
            | Some(_, ty) ->
                match p, literal p with
                | ColumnPredicate.Contains _, _ when ty <> StringType -> Some(PredicateNotApplicable(tag p, c, ty))
                | _, Some Null ->
                    Some(IllFormedLiteral(c, "a null literal compares with nothing; test for absence with isNull"))
                | _, Some v ->
                    match Cell.typeOf v with
                    | Some got when got <> ty -> Some(PredicateTypeMismatch(c, ty, got))
                    | _ ->
                        match Table.validate (oneCell c ty v) with
                        | Ok() -> None
                        | Error e -> Some(IllFormedLiteral(c, literalReason e))
                | _, None -> None

        where
        |> List.indexed
        |> List.tryPick (fun (i, p) -> fault p |> Option.map (fun e -> i, e))

    /// The first refusal an `OrderBy` earns against `schema`, with the index of the key at fault:
    /// an undeclared column (`UnknownColumn`), then a column named a second time
    /// (`DuplicateSortColumn`, at its second occurrence).
    let orderFault (schema: Schema) (order: SortKey list) : (int * QueryError) option =
        let declared = schema |> List.map fst

        order
        |> List.indexed
        |> List.tryPick (fun (i, k) ->
            if not (List.contains k.Column declared) then
                Some(i, UnknownColumn(k.Column, declared))
            elif order |> List.take i |> List.exists (fun x -> x.Column = k.Column) then
                Some(i, DuplicateSortColumn k.Column)
            else
                None)

    /// The capture-key fields of a declaration's shape (Phase 398): for a non-empty `Where`, an empty
    /// name, the tag `w` and the canonical text of its predicates; for a non-empty `OrderBy`, the
    /// same with the tag `o`. Neither tag is a cell's, nor the page tag `p`, so the key's pre-image
    /// stays injective; a declaration with neither adds nothing, so its key is the key it had.
    let keyFields (where: ColumnPredicate list) (order: SortKey list) : string list =
        (if List.isEmpty where then
             []
         else
             [ ""; "w"; Canon.render (JArr(where |> List.map predicateJson)) ])
        @ (if List.isEmpty order then
               []
           else
               [ ""; "o"; Canon.render (JArr(order |> List.map sortKeyJson)) ])

/// What a model reads when a dispatch is refused (Phase 251) — the `InvokeError.describe` twin. Every
/// case that refuses against a closed set names its members, and a `ParamTypeMismatch` over a type
/// a JSON value cannot spell directly (`decimal`, `date`, `timestamp`) also says how to write one.
/// The wire form of the same value is `QueryCodec.queryErrorJson`.
module QueryError =

    /// How to write a value of a type a JSON scalar does not carry as itself.
    let private howToWrite (t: ColumnType) : string =
        match t with
        | DecimalType ->
            " Write a decimal as a JSON string of decimal text, such as \"12.50\": an optional '-', digits, and an optional '.' followed by digits; never as a JSON number."
        | DateType -> " Write a date as a JSON string, YYYY-MM-DD."
        | TimestampType -> " Write a timestamp as a JSON string, YYYY-MM-DDThh:mm:ssZ."
        | _ -> ""

    /// One sentence a model can act on, naming the failure and what would be accepted.
    let describe (e: QueryError) : string =
        match e with
        | NoSuchQuery(id, []) ->
            "Refused: there is no query '"
            + id
            + "' you may run. There are no queries you may run."
        | NoSuchQuery(id, known) ->
            "Refused: there is no query '"
            + id
            + "' you may run. The queries you may run are "
            + FunctionInternals.Reads.quoteAll known
            + "."
        | DuplicateQuery id -> "Refused: the query '" + id + "' is registered twice."
        | UnknownParam(name, []) ->
            "Refused: '"
            + name
            + "' is not a parameter of this query. It takes no parameters."
        | UnknownParam(name, declared) ->
            "Refused: '"
            + name
            + "' is not a parameter of this query. Its parameters are "
            + FunctionInternals.Reads.quoteAll declared
            + "."
        | ParamTypeMismatch(name, expected, got) ->
            "Refused: parameter '"
            + name
            + "' must be "
            + ColumnType.tag expected
            + "; you sent "
            + ColumnType.tag got
            + "."
            + howToWrite expected
        | RequiredParamsUnbound names ->
            "Refused: required parameters missing: "
            + FunctionInternals.Reads.quoteAll names
            + "."
        | SourceNotResolved r -> "Refused: the data source '" + r + "' could not be resolved."
        | ExecutionFailed(detail, []) -> "Refused: the query ran and failed: " + detail + "."
        | ExecutionFailed(detail, recoverable) ->
            "Refused: the query ran and failed: "
            + detail
            + ". You may retry with "
            + FunctionInternals.Reads.quoteAll recoverable
            + "."
        | Timeout -> "Refused: the query timed out."
        | RequiredParamsNull names ->
            "Refused: required parameters bound to no value: "
            + FunctionInternals.Reads.quoteAll names
            + "."
        | DuplicateParam name ->
            "Refused: parameter '"
            + name
            + "' is given more than once. Give each parameter once."
        | QueryPolicyRefused(policy, reason, []) ->
            "Refused: the policy '" + policy + "' does not allow this: " + reason + "."
        | QueryPolicyRefused(policy, reason, allowed) ->
            "Refused: the policy '"
            + policy
            + "' does not allow this: "
            + reason
            + ". What it allows instead: "
            + FunctionInternals.Reads.quoteAll allowed
            + "."
        | QueryApprovalRequired policy ->
            "Refused: the policy '"
            + policy
            + "' requires an approval before this query runs."
        | UnreadableArgs error ->
            "Refused: the arguments could not be read at "
            + DecodePath.render error.Path
            + ": "
            + error.Message
            + ". Send one JSON object keyed by parameter name."
        | UnknownColumn(name, []) ->
            "Refused: '"
            + name
            + "' is not a column of this query's result. It returns no columns."
        | UnknownColumn(name, declared) ->
            "Refused: '"
            + name
            + "' is not a column of this query's result. Its columns are "
            + FunctionInternals.Reads.quoteAll declared
            + "."
        | PredicateTypeMismatch(column, expected, got) ->
            "Refused: the filter on column '"
            + column
            + "' compares with a "
            + ColumnType.tag got
            + " value; the column is "
            + ColumnType.tag expected
            + ", and a filter compares with a value of the column's own type."
            + howToWrite expected
        | PredicateNotApplicable(predicate, column, columnType) ->
            "Refused: the filter '"
            + predicate
            + "' does not apply to column '"
            + column
            + "', which is "
            + ColumnType.tag columnType
            + ". It applies to string columns only."
        | IllFormedLiteral(column, reason) ->
            "Refused: the filter on column '"
            + column
            + "' compares with a value the column cannot carry: "
            + reason
            + "."
        | DuplicateSortColumn column ->
            "Refused: the order names column '"
            + column
            + "' more than once. Name each column once."
        | PredicateNotHonoured predicate ->
            "Refused: the data source cannot apply the filter '"
            + QueryShape.tag predicate
            + "' on column '"
            + QueryShape.column predicate
            + "'."
        | OrderNotHonoured column -> "Refused: the data source cannot order the rows by column '" + column + "'."

    /// Every refusal, one sentence per line, in the order given — the reading of
    /// `Query.validateParamsAll`'s answer.
    let describeAll (es: QueryError list) : string =
        es |> List.map describe |> String.concat "\n"

/// The `QueryError` wire form, below the registry that journals it (Phase 385). The keyed capture
/// records a resolver's refusal as this document and its replay reads it back, so a replayed refusal
/// is the refusal that was answered live; `QueryCodec`'s public error codec is this module. Internal:
/// the published spellings are `QueryCodec.queryErrorJson` / `queryErrorOf` and their text forms.
module internal QueryErrorWire =

    // `tagged` and `colTypeOf` are `QueryShape`'s since Phase 398, which reads the shape below this
    // module; the codecs above read them from there.
    let private tagged (what: string) (cases: (string * 'T) list) : Decoder<'T> = QueryShape.tagged what cases

    let private colTypeOf: Decoder<ColumnType> = QueryShape.colTypeOf

    let private strs (xs: string list) : JVal = JArr(xs |> List.map JStr)

    /// The decode refusal an `UnreadableArgs` carries, read back from `DecodeError.toJson`'s form.
    let private decodeErrorOf: Decoder<DecodeError> =
        Decoder.field
            "code"
            (tagged "unknown decode code: " (DecodeError.codes |> List.map (fun c -> DecodeError.codeName c, c)))
        |> Decoder.bind (fun code ->
            Decoder.field "path" (fun el ->
                match DecodePath.ofJson el with
                | Some path -> Ok path
                | None ->
                    Error(
                        DecodeError.make
                            DecodeCode.WrongKind
                            "an array of member names and item indices"
                            "not a decode path"
                    ))
            |> Decoder.bind (fun path ->
                Decoder.field "expected" Decoder.str
                |> Decoder.bind (fun expected ->
                    Decoder.field "message" Decoder.str
                    |> Decoder.map (fun message ->
                        { Code = code
                          Path = path
                          Expected = expected
                          Message = message }))))

    /// A `QueryError` as its wire document: one `"$type"` per case, in camelCase.
    let toJson (e: QueryError) : JVal =
        match e with
        | NoSuchQuery(id, known) -> Canon.typed "noSuchQuery" [ "id", JStr id; "known", strs known ]
        | DuplicateQuery id -> Canon.typed "duplicateQuery" [ "id", JStr id ]
        | UnknownParam(name, declared) -> Canon.typed "unknownParam" [ "name", JStr name; "declared", strs declared ]
        | ParamTypeMismatch(name, expected, got) ->
            Canon.typed
                "paramTypeMismatch"
                [ "name", JStr name
                  "expected", JStr(ColumnType.tag expected)
                  "got", JStr(ColumnType.tag got) ]
        | RequiredParamsUnbound names -> Canon.typed "requiredParamsUnbound" [ "names", strs names ]
        | SourceNotResolved r -> Canon.typed "sourceNotResolved" [ "ref", JStr r ]
        | ExecutionFailed(detail, recoverable) ->
            Canon.typed "executionFailed" [ "detail", JStr detail; "recoverable", strs recoverable ]
        | Timeout -> Canon.typed "timeout" []
        | RequiredParamsNull names -> Canon.typed "requiredParamsNull" [ "names", strs names ]
        | DuplicateParam name -> Canon.typed "duplicateParam" [ "name", JStr name ]
        | QueryPolicyRefused(policy, reason, allowed) ->
            Canon.typed "policyRefused" [ "policy", JStr policy; "reason", JStr reason; "allowed", strs allowed ]
        | QueryApprovalRequired policy -> Canon.typed "approvalRequired" [ "policy", JStr policy ]
        | UnreadableArgs error -> Canon.typed "unreadableArgs" [ "error", DecodeError.toJson error ]
        | UnknownColumn(name, declared) -> Canon.typed "unknownColumn" [ "name", JStr name; "declared", strs declared ]
        | PredicateTypeMismatch(column, expected, got) ->
            Canon.typed
                "predicateTypeMismatch"
                [ "column", JStr column
                  "expected", JStr(ColumnType.tag expected)
                  "got", JStr(ColumnType.tag got) ]
        | PredicateNotApplicable(predicate, column, columnType) ->
            Canon.typed
                "predicateNotApplicable"
                [ "predicate", JStr predicate
                  "column", JStr column
                  "columnType", JStr(ColumnType.tag columnType) ]
        | IllFormedLiteral(column, reason) ->
            Canon.typed "illFormedLiteral" [ "column", JStr column; "reason", JStr reason ]
        | DuplicateSortColumn column -> Canon.typed "duplicateSortColumn" [ "column", JStr column ]
        | PredicateNotHonoured predicate ->
            Canon.typed "predicateNotHonoured" [ "predicate", QueryShape.predicateJson predicate ]
        | OrderNotHonoured column -> Canon.typed "orderNotHonoured" [ "column", JStr column ]

    /// The reader of `toJson`'s documents; an unknown `"$type"` keeps the codec's sentence.
    let decoder: Decoder<QueryError> =
        let str name = Decoder.field name Decoder.str

        let strList name =
            Decoder.field name (Decoder.list Decoder.str)

        let both (a: Decoder<'A>) (b: Decoder<'B>) (f: 'A -> 'B -> QueryError) : Decoder<QueryError> =
            a |> Decoder.bind (fun x -> b |> Decoder.map (f x))

        let cases =
            [ "noSuchQuery", both (str "id") (strList "known") (fun id known -> NoSuchQuery(id, known))
              "duplicateQuery", str "id" |> Decoder.map DuplicateQuery
              "unknownParam", both (str "name") (strList "declared") (fun name d -> UnknownParam(name, d))
              "paramTypeMismatch",
              str "name"
              |> Decoder.bind (fun name ->
                  both (Decoder.field "expected" colTypeOf) (Decoder.field "got" colTypeOf) (fun exp got ->
                      ParamTypeMismatch(name, exp, got)))
              "requiredParamsUnbound", strList "names" |> Decoder.map RequiredParamsUnbound
              "sourceNotResolved", str "ref" |> Decoder.map SourceNotResolved
              "executionFailed", both (str "detail") (strList "recoverable") (fun d r -> ExecutionFailed(d, r))
              "timeout", Decoder.succeed Timeout
              "requiredParamsNull", strList "names" |> Decoder.map RequiredParamsNull
              "duplicateParam", str "name" |> Decoder.map DuplicateParam
              "policyRefused",
              str "policy"
              |> Decoder.bind (fun policy ->
                  both (str "reason") (strList "allowed") (fun reason allowed ->
                      QueryPolicyRefused(policy, reason, allowed)))
              "approvalRequired", str "policy" |> Decoder.map QueryApprovalRequired
              "unreadableArgs", Decoder.field "error" decodeErrorOf |> Decoder.map UnreadableArgs
              "unknownColumn", both (str "name") (strList "declared") (fun name d -> UnknownColumn(name, d))
              "predicateTypeMismatch",
              str "column"
              |> Decoder.bind (fun column ->
                  both (Decoder.field "expected" colTypeOf) (Decoder.field "got" colTypeOf) (fun exp got ->
                      PredicateTypeMismatch(column, exp, got)))
              "predicateNotApplicable",
              Decoder.field "predicate" (tagged "unknown predicate: " (QueryShape.tags |> List.map (fun t -> t, t)))
              |> Decoder.bind (fun predicate ->
                  both (str "column") (Decoder.field "columnType" colTypeOf) (fun column ty ->
                      PredicateNotApplicable(predicate, column, ty)))
              "illFormedLiteral", both (str "column") (str "reason") (fun c r -> IllFormedLiteral(c, r))
              "duplicateSortColumn", str "column" |> Decoder.map DuplicateSortColumn
              "predicateNotHonoured",
              Decoder.field "predicate" QueryShape.predicateDecoder
              |> Decoder.map PredicateNotHonoured
              "orderNotHonoured", str "column" |> Decoder.map OrderNotHonoured ]

        // The dispatch's own miss keeps this codec's sentence.
        Decoder.tagDispatchWith "unknown query error: " "$type" cases

    /// A refusal as the text the keyed capture journals for it.
    let render (e: QueryError) : string = Canon.render (toJson e)

    /// The refusal a journalled reason records, or `None` for a reason that is not a query-error
    /// document (a journal written before Phase 385 recorded `QueryError.describe`'s sentence).
    let ofReason (reason: string) : QueryError option =
        match Decoder.parse reason |> Result.bind decoder with
        | Ok e -> Some e
        | Error _ -> None

/// The data-acquisition surface: typed registry (populate + enumerate + dispatch), the
/// param-validation contract, the Phase 27 capture keying. Additive over `Column`/`Function`;
/// FSharp.Core-only, Fable-clean.
module Query =

    /// The `ColumnType` a present (non-`Null`) cell realizes — `Cell.typeOf`, the column strand's own
    /// (Phase 295; this module re-implemented it before).
    let private cellType (c: Cell) : ColumnType option = Cell.typeOf c

    /// Does a cell of type `got` fill a parameter of type `declared`? THE widening lattice,
    /// `ColumnType.widens` (Phase 295): the identity, or a lossless promotion — an `int` fills a
    /// `float` or a `decimal` parameter. It was type equality before, so an `int` argument to a
    /// `float` parameter was refused while the column codec read the same value into a `float`
    /// column. For the types that have a value space the answer is `Space.subsumes`'s (the
    /// `spaceRelationLaws` family pins the two together).
    let private fills (declared: ColumnType) (got: ColumnType) : bool = ColumnType.widens got declared

    /// A validated cell at its parameter's declared type: an `int` that fills a `float` or `decimal`
    /// parameter is handed on as that type, so a resolver reads the type it declared.
    let private promote (declared: ColumnType) (c: Cell) : Cell =
        match declared, c with
        | FloatType, Int v -> Float(float v)
        | DecimalType, Int v -> Cell.decimal (string v) |> Option.defaultValue c
        | _ -> c

    /// The Phase 27 determinism label this query keys its captures on: `Effect.determinismTag` of its
    /// determinism set — `"deterministic"` for the empty set, else its factors joined by `+` in canonical
    /// order (`"clock"`, `"clock+random"`, …; Phase 319).
    let determinismTag (q: Query) : string =
        Effect.determinismTag q.Effect.Determinism

    /// A bound `Cell` as two fields of the capture key's pre-image: a one-letter constructor tag
    /// and the scalar rendering (a `Null` is tag `n` with an empty payload).
    let private cellFields (c: Cell) : string * string =
        match c with
        | Int v -> "i", string v
        | Float v -> "f", Canon.canonicalFloat v
        | Bool v -> "b", (if v then "1" else "0")
        | Str v -> "s", v
        | Date v -> "d", v
        | Timestamp v -> "t", v
        // `m`: `d` is a date's. The text is the payload as it stands, which is what keeps the
        // pre-image injective on cells (`cell_fields_injective` in `proofs/Query.fst`).
        | Decimal v -> "m", v
        | Null -> "n", ""

    /// The effect-identity key the Phase 27 capture seam journals a non-deterministic query under:
    /// the query id + a hash of the canonical (name-sorted) pre-image, built through
    /// `Hash.canonicalFields` — three fields per binding (name, cell tag, cell payload). Same args
    /// replay the same captured rows. The pre-image is INJECTIVE (Phase 225,
    /// `invocation_key_injective` in `proofs/Query.fst`): distinct argument sets never share one,
    /// whatever their string cells contain. That two distinct pre-images hash apart is a property
    /// of `Hash.fnv1a` and is not claimed. A consumer threads this as `OpStream.captureEffect`'s
    /// `eff` argument (the `Capability.invocationKey` pattern).
    ///
    /// Since Phase 398 the key also sees the declaration's `Where` and `OrderBy` (the paging
    /// precedent): a non-empty one adds three fields in front of the bindings — an empty name, the
    /// tag `w` (the filter) or `o` (the order), and its canonical text — so the same arguments to
    /// two declarations that filter or order differently key apart. A declaration with neither adds
    /// nothing, and its key is byte for byte the key it had before.
    let invocationKey (q: Query) (args: (string * Cell) list) : string =
        let canonical =
            QueryShape.keyFields q.Where q.OrderBy
            @ (args
               |> List.sortBy fst
               |> List.collect (fun (n, v) ->
                   let tag, payload = cellFields v
                   [ n; tag; payload ]))
            |> Hash.canonicalFields

        q.Id + "#" + Hash.fnv1a canonical

    /// The capture key of ONE PAGE of an invocation (Phase 316): `invocationKey`'s pre-image with
    /// the page token in front of it, so a paged `Network` query captured page by page replays each
    /// page from its own capture rather than page one for every page. `None` (the first page, or a
    /// query that does not page) adds nothing, so `invocationKeyPage q args None` IS
    /// `invocationKey q args`, byte for byte, and every journal keyed before paging reached the seam
    /// still replays. `Some token` adds three fields — an empty name, the tag `p` that no cell's tag
    /// is, and the token — through the same `Hash.canonicalFields`, so the pre-image stays INJECTIVE
    /// over the pair (`invocation_key_page_injective` in `proofs/Query.fst`): two (argument set,
    /// token) pairs share a pre-image only when they hold the same bindings AND the same token, so
    /// distinct tokens give distinct pre-images. That two distinct pre-images hash apart is a property
    /// of `Hash.fnv1a` and is not claimed.
    let invocationKeyPage (q: Query) (args: (string * Cell) list) (pageToken: string option) : string =
        match pageToken with
        | None -> invocationKey q args
        | Some token ->
            let canonical =
                [ ""; "p"; token ]
                @ QueryShape.keyFields q.Where q.OrderBy
                @ (args
                   |> List.sortBy fst
                   |> List.collect (fun (n, v) ->
                       let tag, payload = cellFields v
                       [ n; tag; payload ]))
                |> Hash.canonicalFields

            q.Id + "#" + Hash.fnv1a canonical

    /// Validate typed `args` (name -> bound `Cell`) against the query's declared params *before* any
    /// fetch: every arg must address a declared param, once (`DuplicateParam`, Phase 307 — so the
    /// list `invocationKey` keys has distinct names, the hypothesis of its proved determinism), and
    /// its cell type must fill the param's type —
    /// `ColumnType.widens`, Phase 295 — or be `Null`;
    /// every required param must be bound (`RequiredParamsUnbound` otherwise); and every required
    /// param must be bound to a VALUE — one whose bindings are all `Null` is refused as
    /// `RequiredParamsNull` (Phase 226, `required_is_non_null` in `proofs/Query.fst`). The steps run
    /// in that order and the first refusal is the answer. Default-deny by shape (FGP 3) — the host
    /// validates this before running any resolver.
    let validateParams (q: Query) (args: (string * Cell) list) : Result<unit, QueryError> =
        let declared = q.Params |> List.map _.Name
        let argMap = Map.ofList args

        let rec checkArgs =
            function
            | [] -> Ok()
            | (name, cell) :: rest ->
                match q.Params |> List.tryFind (fun p -> p.Name = name) with
                | None -> Error(UnknownParam(name, declared))
                | Some p ->
                    match cellType cell with
                    | None -> checkArgs rest // a Null binding — absence, type-agnostic
                    | Some t when fills p.Type t -> checkArgs rest
                    | Some t -> Error(ParamTypeMismatch(name, p.Type, t))

        // Phase 307: the first repeated name, at its second occurrence, before any cell is read.
        match Capability.repeatedAddrs args with
        | dup :: _ -> Error(DuplicateParam dup)
        | [] -> checkArgs args
        |> Result.bind (fun () ->
            let unbound =
                q.Params
                |> List.filter (fun p -> p.Required && not (Map.containsKey p.Name argMap))
                |> List.map _.Name

            if List.isEmpty unbound then
                Ok()
            else
                Error(RequiredParamsUnbound unbound))
        |> Result.bind (fun () ->
            // Some binding gives the name a non-`Null` cell. A `Null` binding is absence (step 1
            // treats it so), so a required name bound only to `Null` is bound to no value.
            let boundToValue (name: string) =
                args
                |> List.exists (fun (n, c) ->
                    n = name
                    && (match c with
                        | Null -> false
                        | _ -> true))

            let nullBound =
                q.Params
                |> List.filter (fun p -> p.Required && not (boundToValue p.Name))
                |> List.map _.Name

            if List.isEmpty nullBound then
                Ok()
            else
                Error(RequiredParamsNull nullBound))

    /// Invoke a query: validate the params, then run the host `resolve` (which performs the actual
    /// fetch per the query's source + effect/placement). The resolver answers in the shipped
    /// `Deferred` envelope, so a fetch it has not completed is `Pending` rather than a failure or a
    /// host-invented shape; a resolver failure is the named `ExecutionFailed`, never a throw and
    /// never an untyped `Failed` riding out of the seam. Deterministic queries re-evaluate freely;
    /// for a non-`Deterministic` query the caller journals the realized result (the `Ready` payload —
    /// `Pending` is not captured, replay re-issues) via `OpStream.captureEffect` keyed by
    /// `invocationKey` + `determinismTag` (Phase 27), so the query replays exactly.
    ///
    /// The three outcomes, exhaustively: `Ok(Ready r)` settled · `Ok Pending` in flight · `Error e`
    /// refused, typed. `Ok(Failed _)` cannot occur — certified by `queryLaws`.
    let invoke
        (q: Query)
        (args: (string * Cell) list)
        (resolve: Query -> Deferred<QueryResult>)
        : Result<Deferred<QueryResult>, QueryError> =
        validateParams q args
        |> Result.bind (fun () ->
            match resolve q with
            | Ready r -> Ok(Ready r)
            | Pending -> Ok Pending
            | Failed m -> Error(ExecutionFailed(m, [])))

    /// Invoke ONE PAGE of a query (Phase 316): `invoke`, with the page token handed to the resolver
    /// beside the declaration, so a host pages through the seam — default-deny and `validateParams`
    /// both still run first — instead of declaring the token as a parameter (which would leak it into
    /// the query's enumerated contract) or calling its resolver directly (which would bypass both).
    /// `None` asks for the first page; `Some token` is a `QueryResult.NextPageToken` the host handed
    /// back, opaque to the seam. The same three outcomes as `invoke`, and `invoke q args resolve` is
    /// `invokePage q args None (fun q _ -> resolve q)`. A non-deterministic page is journalled under
    /// `invocationKeyPage q args pageToken`, so each page replays from its own capture.
    let invokePage
        (q: Query)
        (args: (string * Cell) list)
        (pageToken: string option)
        (resolve: Query -> string option -> Deferred<QueryResult>)
        : Result<Deferred<QueryResult>, QueryError> =
        invoke q args (fun q -> resolve q pageToken)

    // ---- every refusal at once, the validated arguments handed on, and the schema (Phase 251) ----

    /// Validate as `validateParams` does, but answer with EVERY refusal rather than the first: one
    /// per refused argument (`UnknownParam` / `ParamTypeMismatch`), in argument order, then
    /// `RequiredParamsUnbound` naming every required parameter left out, then `RequiredParamsNull`
    /// naming every required parameter that is present and bound only to `Null`. The first-failure
    /// form is kept, and the two agree by construction of that order: the head of this list is
    /// exactly `validateParams`'s refusal, and `Ok ()` here is `Ok ()` there.
    let validateParamsAll (q: Query) (args: (string * Cell) list) : Result<unit, QueryError list> =
        let declared = q.Params |> List.map _.Name
        let argMap = Map.ofList args

        let argFault (name: string, cell: Cell) : QueryError option =
            match q.Params |> List.tryFind (fun p -> p.Name = name) with
            | None -> Some(UnknownParam(name, declared))
            | Some p ->
                match cellType cell with
                | None -> None
                | Some t when fills p.Type t -> None
                | Some t -> Some(ParamTypeMismatch(name, p.Type, t))

        // Every repeated name first (Phase 307), as `validateParams` checks it first; then one
        // refusal per argument, in order.
        let faults =
            (Capability.repeatedAddrs args |> List.map DuplicateParam)
            @ (args |> List.choose argFault)

        let unbound =
            q.Params
            |> List.filter (fun p -> p.Required && not (Map.containsKey p.Name argMap))
            |> List.map _.Name

        let boundToValue (name: string) =
            args |> List.exists (fun (n, c) -> n = name && (cellType c).IsSome)

        let nullBound =
            q.Params
            |> List.filter (fun p -> p.Required && Map.containsKey p.Name argMap && not (boundToValue p.Name))
            |> List.map _.Name

        let all =
            faults
            @ (if List.isEmpty unbound then
                   []
               else
                   [ RequiredParamsUnbound unbound ])
            @ (if List.isEmpty nullBound then
                   []
               else
                   [ RequiredParamsNull nullBound ])

        if List.isEmpty all then Ok() else Error all

    /// `invokePage` with the typed resolver (Phase 385): the resolver is handed the declaration, the
    /// page token and the validated argument list — typed, as `Cell`s, the list `validateParams`
    /// checked, each cell at its parameter's declared type (an `int` filling a `float` parameter
    /// arrives as a `float`) — and answers a TYPED failure beside the envelope (`ResolveFault`), so
    /// the refusals only it can know of reach the caller by name: `SourceMissing` is
    /// `SourceNotResolved`, `TimedOut` is `Timeout`, and `Failed(detail, recoverable)` is
    /// `ExecutionFailed(detail, recoverable)` with the arguments a retry may change; an untyped
    /// `Failed m` is `ExecutionFailed(m, [])`. The same three outcomes as `invoke`.
    ///
    /// The resolver reads the declaration's `Where` and `OrderBy` from the `Query` it is handed
    /// (Phase 398), and one it cannot honour answers `PredicateUnsupported` / `OrderUnsupported`,
    /// which reach the caller as `PredicateNotHonoured` / `OrderNotHonoured` naming the predicate or
    /// the column — a fetch that cannot apply the declared filter or order is refused, not run
    /// without it.
    let invokePageWithArgs
        (q: Query)
        (args: (string * Cell) list)
        (pageToken: string option)
        (resolve: Query -> string option -> (string * Cell) list -> Result<Deferred<QueryResult>, ResolveFault>)
        : Result<Deferred<QueryResult>, QueryError> =
        validateParams q args
        |> Result.bind (fun () ->
            let typed =
                args
                |> List.map (fun (name, cell) ->
                    match q.Params |> List.tryFind (fun p -> p.Name = name) with
                    | Some p -> name, promote p.Type cell
                    | None -> name, cell)

            match resolve q pageToken typed with
            | Ok(Ready r) -> Ok(Ready r)
            | Ok Pending -> Ok Pending
            | Ok(Failed m) -> Error(ExecutionFailed(m, []))
            | Error(ResolveFault.SourceMissing r) -> Error(SourceNotResolved r)
            | Error ResolveFault.TimedOut -> Error Timeout
            | Error(ResolveFault.Failed(detail, recoverable)) -> Error(ExecutionFailed(detail, recoverable))
            | Error(ResolveFault.PredicateUnsupported p) -> Error(PredicateNotHonoured p)
            | Error(ResolveFault.OrderUnsupported c) -> Error(OrderNotHonoured c))

    /// `invoke`, with the resolver handed the validated argument list — typed, as `Cell`s, the list
    /// `validateParams` checked, each cell at its parameter's declared type (an `int` filling a
    /// `float` parameter arrives as a `float`) — so a resolver reads its arguments instead of closing
    /// over the caller's list. The same three outcomes as `invoke`, and a resolver's `Failed m` is
    /// projected into `ExecutionFailed` exactly as there.
    ///
    /// Since Phase 295 the resolver answers a TYPED failure beside the envelope (`ResolveFault`), so
    /// the refusals only it can know of reach the caller by name: `SourceMissing` is
    /// `SourceNotResolved`, `TimedOut` is `Timeout`, and `Failed(detail, recoverable)` is
    /// `ExecutionFailed(detail, recoverable)` with the arguments a retry may change. It is
    /// `invokePageWithArgs q args None (fun q _ typed -> resolve q typed)` (Phase 385).
    let invokeWithArgs
        (q: Query)
        (args: (string * Cell) list)
        (resolve: Query -> (string * Cell) list -> Result<Deferred<QueryResult>, ResolveFault>)
        : Result<Deferred<QueryResult>, QueryError> =
        invokePageWithArgs q args None (fun q _ typed -> resolve q typed)

    /// The text an exact decimal is written as: the grammar `DecimalText` READS, which is exactly
    /// the set of strings the codec decodes into a `Decimal` cell (and canonicalises: `12.50` is
    /// `Decimal "12.5"`).
    let private decimalPattern = "^-?[0-9]+(\\.[0-9]+)?$"
    let private datePattern = "^[0-9]{4}-[0-9]{2}-[0-9]{2}$"

    let private timestampPattern =
        "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$"

    /// The JSON Schema of one value of a column type, as a model emits it and the codec reads it.
    /// A `decimal` is a STRING with the decimal-text pattern, never a number: a model told
    /// "number" emits a fractional token, and the codec refuses one, because a value that has
    /// been through a float is not a value an exact type can vouch for. A `date` / `timestamp` is
    /// a string of its canonical ISO-8601 shape; an `int` is bounded to the 32-bit range the cell
    /// holds.
    let private columnSchema (t: ColumnType) : JVal =
        match t with
        | IntType ->
            JObj
                [ "type", JStr "integer"
                  "minimum", JInt System.Int32.MinValue
                  "maximum", JInt System.Int32.MaxValue ]
        | FloatType -> JObj [ "type", JStr "number" ]
        | BoolType -> JObj [ "type", JStr "boolean" ]
        | StringType -> JObj [ "type", JStr "string" ]
        | DateType -> JObj [ "type", JStr "string"; "pattern", JStr datePattern ]
        | TimestampType -> JObj [ "type", JStr "string"; "pattern", JStr timestampPattern ]
        | DecimalType -> JObj [ "type", JStr "string"; "pattern", JStr decimalPattern ]

    /// Project a query into a STANDARD JSON Schema `object` — `Function.toJsonSchema`'s twin, over
    /// the same convention (Phase 251; the convention is written down beside `Function.toSchema`):
    /// no tag, `"title"` the query id, each parameter a property keyed by its NAME with the schema
    /// of its column type, the required ones listed, and what JSON Schema has no keyword for outside
    /// `properties` under `x-` keys — `x-effect` (the two-axis effect class, as there) and
    /// `x-result`, the schema of ONE result row keyed by column name, so a model reads what comes
    /// back without it reading as an argument to fill. A parameter's value is what
    /// `QueryCodec.decodeArgs` reads. `Canon.render` of the result is canonical and stable for a
    /// fixed query.
    let toJsonSchema (q: Query) : JVal =
        JObj
            [ "type", JStr "object"
              "title", JStr q.Id
              "x-effect", EffectCodec.toJson q.Effect
              "properties", JObj(q.Params |> List.map (fun p -> p.Name, columnSchema p.Type))
              "required", JArr(q.Params |> List.filter _.Required |> List.map (fun p -> JStr p.Name))
              "x-result",
              JObj
                  [ "type", JStr "object"
                    "properties", JObj(q.ResultSchema |> List.map (fun (n, t) -> n, columnSchema t)) ] ]
