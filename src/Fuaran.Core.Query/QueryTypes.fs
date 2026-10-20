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
        /// The columns of one result row — each a `Field` (name, type and, since Phase 427, the
        /// metadata that says what the column means) in column order.
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
    /// unqualified case name shared by two unions resolves to whichever was declared last. The prefix
    /// is carried onto the wire (Phase 497): its `$type` is `queryPolicyRefused`, so a refusal
    /// document names the seam that refused, and the capability seam's `policyRefused` is refused here.
    | QueryPolicyRefused of policy: string * reason: string * allowed: string list
    /// The registry's policy answered `NeedsApproval` (Phase 318): the gate named `policy` will not
    /// let the query run until an approval is given. No resolver ran. On the wire it is
    /// `queryApprovalRequired`, never the capability seam's `approvalRequired` (Phase 497).
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

/// The query wire's host-side readers and writers (Phase 551): the codec of `QueryError`,
/// `ColumnPredicate` and `SortKey` is DERIVED from their IDL declaration (`QuerySeamCodec`,
/// DECISIONS.md D200), and reads and writes the values the IDL does not model through these — a
/// column type by its tag, a predicate kind by its wire tag, and a comparison's literal, whose
/// `type` and `value` are one host cell read at the type its document states (a CASE-level host
/// codec, D198).
module internal QueryWire =

    /// A column type, spelled by `ColumnType.tag`.
    let columnTypeJson (t: ColumnType) : JVal = JStr(ColumnType.tag t)

    /// A column type read back from its tag.
    let columnTypeOf: Decoder<ColumnType> =
        Decoder.oneOf (ColumnType.all |> List.map (fun t -> ColumnType.tag t, t))

    /// Every predicate wire tag, in case order: what `PredicateNotApplicable` names a predicate by.
    let predicateTags =
        [ "equalTo"
          "greaterThan"
          "atLeast"
          "lessThan"
          "atMost"
          "contains"
          "isNull"
          "isNotNull" ]

    /// A predicate kind read back from its wire tag.
    let predicateTagOf: Decoder<string> =
        Decoder.oneOf (predicateTags |> List.map (fun t -> t, t))

    /// A literal's JSON value: the value the column codec writes for that cell in a column of its
    /// type (`ColumnCodec.writeCell`, Phase 514). A literal is written only after admission, which
    /// holds it to its column's exact type, so the first write succeeds. A literal its type cannot
    /// hold is written total rather than raised on: a date or timestamp as its text (Phase 422: the
    /// column holds integers, the text is what was declared), any other cell at its own type, which
    /// holds every cell of it.
    let private literalJson (ty: ColumnType) (c: Cell) : JVal =
        match ColumnCodec.writeCell "v" ty c, c with
        | Ok v, _ -> v
        | Error _, (Date s | Timestamp s) -> JStr s
        | Error _, other ->
            ColumnCodec.writeCell "v" (Cell.typeOf other |> Option.defaultValue ty) other
            |> Result.defaultValue (JStr "")

    /// A comparison's members beside its tag: `column`, the literal's `type` and its `value`.
    let comparedMembers (column: string, v: Cell) : (string * JVal) list =
        // `Null` has no type: admission refuses it, and nothing reaches here with one.
        let ty = Cell.typeOf v |> Option.defaultValue StringType

        [ "column", JStr column
          "type", JStr(ColumnType.tag ty)
          "value", literalJson ty v ]

    /// A comparison read back: its `column`, then its `type`, then its `value` through the column
    /// codec's one-cell reader (`ColumnCodec.readCell`). A value that is not one a column of the stated
    /// type carries is refused `OutOfRange` at `value`, with `IllFormedLiteral`'s reason; the type it
    /// states is held to the column's by admission, not here.
    let comparedOf (el: JVal) : Result<string * Cell, DecodeError> =
        Decoder.field "column" Decoder.str el
        |> Result.bind (fun c ->
            Decoder.field "type" columnTypeOf el
            |> Result.bind (fun ty ->
                Decoder.field
                    "value"
                    (fun v ->
                        match ColumnCodec.readCell c ty v with
                        | Ok cell -> Ok(c, cell)
                        | Error _ ->
                            Error(
                                DecodeError.make
                                    DecodeCode.OutOfRange
                                    ("a " + ColumnType.tag ty + " value")
                                    ("the filter on column '"
                                     + c
                                     + "' compares with a value a "
                                     + ColumnType.tag ty
                                     + " column cannot carry")
                            ))
                    el))
