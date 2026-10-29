namespace Fuaran.Core

/// The `Where` / `OrderBy` shape of a declaration (Phase 398): its capture-key fields and its
/// admission (its wire form is `QuerySeamCodec`'s since Phase 551) — the checks `QueryRegistry.admissionFault` runs, so the registry and the declaration
/// reader refuse the same declarations with the same error.
module internal QueryShape =

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

    /// Why a well-typed literal is not one its column carries, from `Table.validate`'s refusal.
    let private literalReason (e: ColumnError) : string =
        match e with
        | NonFiniteFloat(_, v) -> "the float " + v + " is not finite, and the wire has no non-finite float"
        | MalformedShape detail -> detail
        | _ -> "it is not a value its column carries"

    /// The refusal of a literal of its column's type that its column cannot carry: `ofCells`'s
    /// (text a date or timestamp column cannot hold, Phase 422), else `Table.validate`'s.
    let private literalFault (c: string) (ty: ColumnType) (v: Cell) : QueryError option =
        match Column.ofCells c ty [ v ] with
        | Error e -> Some(IllFormedLiteral(c, literalReason e))
        | Ok col ->
            match
                Table.validate
                    { Schema = [ Field.create c col.Type ]
                      Columns = [ col ] }
            with
            | Ok() -> None
            | Error e -> Some(IllFormedLiteral(c, literalReason e))

    /// The first refusal a `Where` earns against `schema`, with the index of the predicate at
    /// fault: an undeclared column (`UnknownColumn`), `contains` on a column that is not a string
    /// (`PredicateNotApplicable`), a `Null` literal or one its column cannot carry
    /// (`IllFormedLiteral`), and a literal of another type (`PredicateTypeMismatch`), checked in that
    /// order per predicate, predicates in order.
    let whereFault (schema: Schema) (where: ColumnPredicate list) : (int * QueryError) option =
        let declared = schema |> List.map _.Name

        let fault (p: ColumnPredicate) : QueryError option =
            let c = column p

            match schema |> List.tryFind (fun f -> f.Name = c) with
            | None -> Some(UnknownColumn(c, declared))
            | Some f ->
                let ty = f.Type

                match p, literal p with
                | ColumnPredicate.Contains _, _ when ty <> StringType -> Some(PredicateNotApplicable(tag p, c, ty))
                | _, Some Null ->
                    Some(IllFormedLiteral(c, "a null literal compares with nothing; test for absence with isNull"))
                | _, Some v ->
                    match Cell.typeOf v with
                    // An instant's cell type is the coarsest unit that holds it (Phase 422), so a
                    // timestamp literal fits any column at least as fine; every other literal is
                    // held to its column's exact type.
                    | Some(TimestampType got) when
                        (match ty with
                         | TimestampType unit -> TimeUnit.widens got unit
                         | _ -> false)
                        ->
                        literalFault c ty v
                    | Some got when got <> ty -> Some(PredicateTypeMismatch(c, ty, got))
                    | _ -> literalFault c ty v
                | _, None -> None

        where
        |> List.indexed
        |> List.tryPick (fun (i, p) -> fault p |> Option.map (fun e -> i, e))

    /// The first refusal an `OrderBy` earns against `schema`, with the index of the key at fault:
    /// an undeclared column (`UnknownColumn`), then a column named a second time
    /// (`DuplicateSortColumn`, at its second occurrence).
    let orderFault (schema: Schema) (order: SortKey list) : (int * QueryError) option =
        let declared = schema |> List.map _.Name

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
             [ ""
               "w"
               Canonical.write Rendering.CrossHost (JArr(where |> List.map QuerySeamCodec.encodeColumnPredicate)) ])
        @ (if List.isEmpty order then
               []
           else
               [ ""
                 "o"
                 Canonical.write Rendering.CrossHost (JArr(order |> List.map QuerySeamCodec.encodeSortKey)) ])

/// What a model reads when a dispatch is refused (Phase 251) — the `InvokeError.describe` twin. Every
/// case that refuses against a closed set names its members, and a `ParamTypeMismatch` over a type
/// a JSON value cannot spell directly (`decimal`, `date`, `timestamp`) also says how to write one.
/// The wire form of the same value is `QuerySeamCodec.encodeQueryError`.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module QueryError =

    /// How to write a value of a type a JSON scalar does not carry as itself.
    let private howToWrite (t: ColumnType) : string =
        match t with
        | DecimalType ->
            " Write a decimal as a JSON string of decimal text, such as \"12.50\": an optional '-', digits, and an optional '.' followed by digits; never as a JSON number."
        | DateType -> " Write a date as a JSON string, YYYY-MM-DD."
        | TimestampType TimeUnit.Seconds -> " Write a timestamp as a JSON string, YYYY-MM-DDThh:mm:ssZ."
        | TimestampType u ->
            " Write a timestamp as a JSON string, YYYY-MM-DDThh:mm:ssZ, or YYYY-MM-DDThh:mm:ss.FZ with up to "
            + string (TimeUnit.digits u)
            + " fraction digits and no trailing zero."
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
/// is the refusal that was answered live. Internal: the published spellings are
/// `QuerySeamCodec.encodeQueryError` / `decodeQueryError` and `QueryCodec`'s text forms.
module internal QueryErrorWire =

    // Phase 551: the document is `QuerySeamCodec`'s, DERIVED from the union's IDL declaration
    // (DECISIONS.md D200); a column type travels by its tag, a decode refusal in `DecodeError.toJson`'s
    // form (read back by `DecodeError.decoder`), a predicate as its own document.

    /// A refusal as the text the keyed capture journals for it.
    let render (e: QueryError) : string =
        Canonical.write Rendering.CrossHost (QuerySeamCodec.encodeQueryError e)

    /// The refusal a journalled reason records, or `None` for a reason that is not a query-error
    /// document (a journal written before Phase 385 recorded `QueryError.describe`'s sentence).
    let ofReason (reason: string) : QueryError option =
        match
            Decoder.parse ReadOptions.strict reason
            |> Result.bind QuerySeamCodec.decodeQueryError
        with
        | Ok e -> Some e
        | Error _ -> None

/// The data-acquisition surface: typed registry (populate + enumerate + dispatch), the
/// param-validation contract, the Phase 27 capture keying. Additive over `Column`/`Function`;
/// FSharp.Core-only, Fable-clean.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
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
        | Float v -> "f", FloatLayout.canonical v
        | Bool v -> "b", (if v then "1" else "0")
        | Str v -> "s", v
        | Date v -> "d", v
        | Timestamp v -> "t", v
        // `m`: `d` is a date's. The text is the payload as it stands, which is what keeps the
        // pre-image injective on cells (`cell_fields_injective` in `proofs/Query.fst`).
        | Decimal v -> "m", v
        | Null -> "n", ""

    /// The query grammar (Phase 225, D173): the declaration's shape fields (`QueryShape.keyFields`),
    /// then three fields per binding — name, cell tag, cell payload — name-sorted. The one body both
    /// keys read, so the paged key is the unpaged one behind a page triple.
    let private grammarFields (q: Query) (args: (string * Cell) list) : string list =
        QueryShape.keyFields q.Where q.OrderBy
        @ (args
           |> List.sortBy fst
           |> List.collect (fun (n, v) ->
               let tag, payload = cellFields v
               [ n; tag; payload ]))

    /// The effect-identity key the Phase 27 capture seam journals a non-deterministic query under:
    /// the query id + a hash of the canonical (name-sorted) pre-image, built by `Hash.keyedBy`
    /// (Phase 510) — three fields per binding (name, cell tag, cell payload). Same args
    /// replay the same captured rows. The pre-image is INJECTIVE (Phase 225,
    /// `invocation_key_injective` in `proofs/Query.fst`): distinct argument sets never share one,
    /// whatever their string cells contain. That two distinct pre-images hash apart is a property
    /// of `Hash.fnv1a` and is not claimed. A consumer threads this as `EffectCapture.captureEffect`'s
    /// `eff` argument (the `Capability.invocationKey` pattern).
    ///
    /// Since Phase 398 the key also sees the declaration's `Where` and `OrderBy` (the paging
    /// precedent): a non-empty one adds three fields in front of the bindings — an empty name, the
    /// tag `w` (the filter) or `o` (the order), and its canonical text — so the same arguments to
    /// two declarations that filter or order differently key apart. A declaration with neither adds
    /// nothing, and its key is byte for byte the key it had before.
    let invocationKey (q: Query) (args: (string * Cell) list) : string =
        Hash.keyedBy q.Id (grammarFields q args)

    /// The capture key of ONE PAGE of an invocation (Phase 316): `invocationKey`'s pre-image with
    /// the page token in front of it, so a paged `Network` query captured page by page replays each
    /// page from its own capture rather than page one for every page. `None` (the first page, or a
    /// query that does not page) adds nothing, so `invocationKeyPage q args None` IS
    /// `invocationKey q args`, byte for byte, and every journal keyed before paging reached the seam
    /// still replays. `Some token` adds three fields — an empty name, the tag `p` that no cell's tag
    /// is, and the token — through the same `Hash.keyedBy`, so the pre-image stays INJECTIVE
    /// over the pair (`invocation_key_page_injective` in `proofs/Query.fst`): two (argument set,
    /// token) pairs share a pre-image only when they hold the same bindings AND the same token, so
    /// distinct tokens give distinct pre-images. That two distinct pre-images hash apart is a property
    /// of `Hash.fnv1a` and is not claimed.
    let invocationKeyPage (q: Query) (args: (string * Cell) list) (pageToken: string option) : string =
        match pageToken with
        | None -> invocationKey q args
        | Some token -> Hash.keyedBy q.Id ([ ""; "p"; token ] @ grammarFields q args)

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
    /// `Pending` is not captured, replay re-issues) via `EffectCapture.captureEffect` keyed by
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

    /// The canonical instant text at a unit (Phase 422): whole seconds, or a fraction of up to the
    /// unit's digits with no trailing zero. The seconds pattern is the one this schema always wrote.
    let private timestampPattern (u: TimeUnit) =
        match u with
        | TimeUnit.Seconds -> "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$"
        | _ ->
            "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]{0,"
            + string (TimeUnit.digits u - 1)
            + "}[1-9])?Z$"

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
        | TimestampType u -> JObj [ "type", JStr "string"; "pattern", JStr(timestampPattern u) ]
        | DecimalType -> JObj [ "type", JStr "string"; "pattern", JStr decimalPattern ]

    /// The schema of one result column: its type's schema, then (Phase 588) the field's label as
    /// `"title"` and its description as `"description"`, each only where the field states one, so a
    /// field with neither projects exactly the bytes it always did. The members sit on the column's
    /// own schema object, so the query-level `"title"` (the query id) is a different object's
    /// member and never in tension with them.
    let private resultFieldSchema (f: Field) : JVal =
        match columnSchema f.Type with
        | JObj members ->
            JObj(
                members
                @ [ match f.Label with
                    | Some l -> "title", JStr l
                    | None -> ()
                    match f.Description with
                    | Some d -> "description", JStr d
                    | None -> () ]
            )
        | other -> other

    /// Project a query into a STANDARD JSON Schema `object` — `Function.toJsonSchema`'s twin, over
    /// the same convention (Phase 251; the convention is written down beside `Function.toSchema`):
    /// no tag, `"title"` the query id, each parameter a property keyed by its NAME with the schema
    /// of its column type, the required ones listed, and what JSON Schema has no keyword for outside
    /// `properties` under `x-` keys — `x-effect` (the two-axis effect class, as there) and
    /// `x-result`, the schema of ONE result row keyed by column name (each column carrying its label as
    /// `"title"` and its description as `"description"` where it states them, Phase 588), so a model reads what comes
    /// back without it reading as an argument to fill. A parameter's value is what
    /// `QueryCodec.decodeArgs` reads. `Canonical.write Rendering.CrossHost` of the result is canonical and stable for a
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
                    "properties", JObj(q.ResultSchema |> List.map (fun f -> f.Name, resultFieldSchema f)) ] ]
