namespace Fuaran.Core

/// The canonical wire codec for a `Query` declaration + a `QueryResult`. Round-trips the typed
/// params, the result schema, the effect class, the source (via `ColumnCodec`), and the paging
/// fields. Fable-clean (`Json` / `Decode`).
module QueryCodec =

    // ---- column type ----

    // A column type is spelled by `ColumnType.tag` and read back by `ColumnType.ofTag` (Phase 295;
    // this codec carried its own copy of both before).
    let private colTypeStr = ColumnType.tag

    // Phase 310 — every member is read through the typed decode layer (`Decoder`); a refusal is
    // spelled into this codec's `QueryError` envelope at the entry points, with the sentence it
    // has always carried.

    /// A string from a closed set; a miss is `UnknownTag`, in this codec's sentence `<what><value>`.
    let private tagged (what: string) (cases: (string * 'T) list) : Decoder<'T> = QueryShape.tagged what cases

    let private colTypeOf: Decoder<ColumnType> = QueryShape.colTypeOf

    // The effect class is `EffectCodec`'s (Phase 295), the capability codec's reader and writer.

    // ---- param ----

    let private paramJson (p: QueryParam) : JVal =
        JObj
            [ "name", JStr p.Name
              "type", JStr(colTypeStr p.Type)
              "required", JBool p.Required ]

    let private paramOf (el: JVal) : Result<QueryParam, DecodeError> =
        Decoder.field "name" Decoder.str el
        |> Result.bind (fun name ->
            Decoder.field "type" colTypeOf el
            |> Result.bind (fun ty ->
                // This codec's sentence for a non-bool `required` is its own, kept.
                Decoder.field
                    "required"
                    (fun j ->
                        Decoder.bool j
                        |> Result.mapError (DecodeError.reword (fun _ -> "expected a bool")))
                    el
                |> Result.map (fun req ->
                    { Name = name
                      Type = ty
                      Required = req })))

    // ---- schema ----

    let private schemaJson (s: Schema) : JVal =
        JArr(
            s
            |> List.map (fun (n, t) -> JObj [ "name", JStr n; "type", JStr(colTypeStr t) ])
        )

    let private schemaOf: Decoder<Schema> =
        Decoder.list (fun e ->
            Decoder.field "name" Decoder.str e
            |> Result.bind (fun n -> Decoder.field "type" colTypeOf e |> Result.map (fun t -> n, t)))

    // ---- optional int ----

    let private optIntJson (name: string) (v: int option) : (string * JVal) list =
        match v with
        | Some n -> [ name, JInt n ]
        | None -> []

    // ---- query declaration ----

    let internal queryJson (q: Query) : JVal =
        JObj(
            [ "$type", JStr "query"
              "id", JStr q.Id
              "params", JArr(q.Params |> List.map paramJson)
              "resultSchema", schemaJson q.ResultSchema
              "effect", EffectCodec.toJson q.Effect
              "source", ColumnCodec.encodeJson q.Source ]
            @ optIntJson "timeoutMs" q.TimeoutMs
            @ optIntJson "pageSize" q.PageSize
            @ (if List.isEmpty q.Where then
                   []
               else
                   [ "where", JArr(q.Where |> List.map QueryShape.predicateJson) ])
            @ (if List.isEmpty q.OrderBy then
                   []
               else
                   [ "orderBy", JArr(q.OrderBy |> List.map QueryShape.sortKeyJson) ])
        )

    /// A declaration as canonical JSON text (`"$type": "query"`): the same declaration always
    /// renders to the same bytes, and `timeoutMs` / `pageSize` are omitted when `None`. Since Phase
    /// 398 a non-empty `Where` is the `where` array of predicate documents (`"$type"` the predicate:
    /// `equalTo`, `greaterThan`, `atLeast`, `lessThan`, `atMost` with the literal's `type` and
    /// `value`; `contains` with its `text`; `isNull`, `isNotNull`) and a non-empty `OrderBy` the
    /// `orderBy` array of `{column, direction}` keys; both are omitted when empty, so a declaration
    /// with neither renders the bytes it rendered before the members existed.
    let encode (q: Query) : string = Canon.render (queryJson q)

    // Phase 295 — a decode failure is a decode failure: the readers answer a typed `DecodeError` (the
    // `…Detailed` entry points, Phase 310's convention) and the string forms its sentence, as
    // `CapabilityCodec` does. They answered `ExecutionFailed("decode: …")` before, a refusal of a
    // fetch that never ran.

    /// A refusal from the column codec, carried as a decode refusal at the member it was reading.
    let private columnRefused (memberName: string) (what: string) : DecodeError =
        DecodeError.under
            (PathSegment.Key memberName)
            (DecodeError.make DecodeCode.OutOfRange what (memberName + ": not " + what + " the column codec reads"))

    /// A refusal from the registry's admission gate, carried as a decode refusal at `memberName`
    /// with the registry's own sentence (Phase 385).
    let private admissionRefused (path: PathSegment list) (e: QueryError) : DecodeError =
        List.foldBack
            DecodeError.under
            path
            (DecodeError.make DecodeCode.OutOfRange "a declaration the registry admits" (QueryError.describe e))

    // Phase 385: the readers check the document's tag (D104), and the declaration reader runs THE
    // admission gate the registry runs (`QueryRegistry.admissionFault`, D111) — so a declaration
    // that decodes is one `register` admits, and a refused one is refused with the registry's error.

    let internal queryOf (el: JVal) : Result<Query, DecodeError> =
        Decoder.field "$type" (tagged "not a query declaration: " [ "query", () ]) el
        |> Result.bind (fun () -> Decoder.field "id" Decoder.str el)
        |> Result.bind (fun id ->
            Decoder.field "params" (Decoder.list paramOf) el
            |> Result.bind (fun ps ->
                Decoder.field "resultSchema" schemaOf el
                |> Result.bind (fun sch ->
                    Decoder.field "effect" EffectCodec.decoder el
                    |> Result.bind (fun eff ->
                        Decoder.optField "timeoutMs" Decoder.int el
                        |> Result.bind (fun tmo ->
                            Decoder.optField "pageSize" Decoder.int el
                            |> Result.bind (fun pg ->
                                Decoder.optField "where" (Decoder.list QueryShape.predicateDecoder) el
                                |> Result.bind (fun wh ->
                                    Decoder.optField "orderBy" (Decoder.list QueryShape.sortKeyDecoder) el
                                    |> Result.bind (fun ob ->
                                        Decoder.field "source" Decoder.json el
                                        |> Result.bind (fun srcEl ->
                                            match ColumnCodec.decodeJson srcEl with
                                            | Error _ -> Error(columnRefused "source" "a data source")
                                            | Ok src ->
                                                let q =
                                                    { Id = id
                                                      Params = ps
                                                      ResultSchema = sch
                                                      Effect = eff
                                                      Source = src
                                                      TimeoutMs = tmo
                                                      PageSize = pg
                                                      Where = wh |> Option.defaultValue []
                                                      OrderBy = ob |> Option.defaultValue [] }

                                                match QueryRegistry.admissionFaultAt q with
                                                | Some(path, e) -> Error(admissionRefused path e)
                                                | None -> Ok q)))))))))

    // ---- query result ----

    let internal resultJson (qr: QueryResult) : JVal =
        let nextTok =
            match qr.NextPageToken with
            | Some tok -> [ "nextPageToken", JStr tok ]
            | None -> []

        JObj(
            [ "$type", JStr "queryResult"
              "rows", ColumnCodec.encodeJson (Embedded qr.Rows)
              "pageNum", JInt qr.PageNum ]
            @ optIntJson "totalRowCount" qr.TotalRowCount
            @ nextTok
        )

    /// A result page as canonical JSON text (`"$type": "queryResult"`), the rows written by the
    /// column codec as an embedded table; `totalRowCount` / `nextPageToken` are omitted when `None`.
    let encodeResult (qr: QueryResult) : string = Canon.render (resultJson qr)

    let internal resultOf (el: JVal) : Result<QueryResult, DecodeError> =
        Decoder.field "$type" (tagged "not a query result: " [ "queryResult", () ]) el
        |> Result.bind (fun () -> Decoder.field "rows" Decoder.json el)
        |> Result.bind (fun rowsEl ->
            match ColumnCodec.decodeJson rowsEl with
            | Error _ -> Error(columnRefused "rows" "a table")
            | Ok(Ref _) -> Error(columnRefused "rows" "an embedded table (a ref is not a result)")
            | Ok(Embedded t) ->
                Decoder.field "pageNum" Decoder.int el
                |> Result.bind (fun pn ->
                    Decoder.optField "totalRowCount" Decoder.int el
                    |> Result.bind (fun trc ->
                        // Phase 310: a present `nextPageToken` that is not a string is refused,
                        // where it was read as absent.
                        Decoder.optField "nextPageToken" Decoder.str el
                        |> Result.map (fun tok ->
                            { Rows = t
                              PageNum = pn
                              TotalRowCount = trc
                              NextPageToken = tok }))))

    // ---- deferred query result (Phase 198) ----
    // The seam's result type is now `Deferred<QueryResult>`, so it has to cross the wire like every
    // other Query type — a host that answers `Pending` over a transport has nothing to send
    // otherwise. This reuses the shipped envelope codec (`CapabilityCodec.deferredJson` /
    // `deferredOf`, `"$type"`-tagged `pending` / `ready` / `failed`) rather than minting a second
    // encoding of the same three cases: one envelope, one wire shape, both seams.

    let internal deferredResultJson (d: Deferred<QueryResult>) : JVal =
        CapabilityCodec.deferredJson resultJson d

    /// The seam's async envelope as canonical JSON text, in the `pending` / `ready` / `failed`
    /// shape `CapabilityCodec` uses, a `ready` payload written as `encodeResult` writes it.
    let encodeDeferredResult (d: Deferred<QueryResult>) : string = Canon.render (deferredResultJson d)

    let internal deferredResultOf (el: JVal) : Result<Deferred<QueryResult>, DecodeError> =
        CapabilityCodec.deferredOf (fun e -> resultOf e |> Result.mapError DecodeError.describe) el
        |> Result.mapError (fun m -> DecodeError.make DecodeCode.OutOfRange "a deferred query result" m)

    // ---- typed refusal (Phase 251) ----
    // `CapabilityCodec.invokeErrorJson`'s twin: one `$type` per case, in camelCase, and a column
    // type by its tag. The sentence a model reads is `QueryError.describe`. Since Phase 385 the
    // codec lives below the registry (`QueryErrorWire`), so the keyed capture journals a refusal in
    // this form and replays it back; these are its published spellings.

    /// Encode a `QueryError` to a `JVal` (`"$type"` is the case: `noSuchQuery`, `paramTypeMismatch`, …).
    let queryErrorJson (e: QueryError) : JVal = QueryErrorWire.toJson e

    /// `queryErrorJson` as canonical JSON text — the wire form; `QueryError.describe` is the
    /// sentence a model reads.
    let encodeQueryError (e: QueryError) : string = QueryErrorWire.render e

    /// Decode a `QueryError` from a `JVal`. The error side is a plain `string`: a refusal that
    /// cannot be read is not itself a refusal of the query.
    let queryErrorOf (el: JVal) : Result<QueryError, string> =
        Decoder.describing QueryErrorWire.decoder el

    /// `queryErrorOf` over text. A parse failure or an unknown `$type` is the `Error` sentence,
    /// never a `QueryError`.
    let decodeQueryError (s: string) : Result<QueryError, string> =
        Decode.parse s |> Result.bind queryErrorOf

    // ---- a model's arguments (Phase 251) ----
    // `Query.toJsonSchema` tells a model to emit one object keyed by parameter name; this reads that
    // object back into the typed argument list `validateParams` takes. Each value is read by the
    // ONE cell decoder the column codec owns — as the single cell of a one-row column of the
    // parameter's type — so an argument decodes exactly as the same value would in a result table:
    // a decimal is read from decimal text and canonicalised, a date or timestamp only in its
    // canonical form, and a fractional number token for a decimal is refused.

    /// The column type a JSON scalar spells as itself, for naming what was sent.
    let private sentType (v: JVal) : ColumnType option =
        match v with
        | JInt _ -> Some IntType
        | JFloat _ -> Some FloatType
        | JBool _ -> Some BoolType
        | JStr _ -> Some StringType
        | JArr _
        | JObj _ -> None

    let private argCell (name: string) (ty: ColumnType) (v: JVal) : Result<string * Cell, QueryError> =
        let oneCell =
            JObj
                [ "schema", JArr [ JObj [ "name", JStr name; "type", JStr(colTypeStr ty) ] ]
                  "columns", JObj [ name, JObj [ "values", JArr [ v ]; "validity", JArr [ JBool true ] ] ] ]

        let refused () =
            match sentType v with
            | Some got -> Error(ParamTypeMismatch(name, ty, got))
            | None ->
                // Phase 385: a value of a kind no column type spells is unreadable input, not a
                // fetch that ran — a decode refusal at the member, carried as `UnreadableArgs`.
                Error(
                    UnreadableArgs(
                        DecodeError.under
                            (PathSegment.Key name)
                            (DecodeError.make
                                DecodeCode.WrongKind
                                ("a " + colTypeStr ty + " value")
                                ("parameter '"
                                 + name
                                 + "' takes a "
                                 + colTypeStr ty
                                 + " value, not a JSON "
                                 + JVal.kindName v))
                    )
                )

        match ColumnCodec.decodeJson oneCell with
        | Ok(Embedded { Columns = [ c ] }) when Column.length c = 1 -> Ok(name, Column.cell 0 c)
        | _ -> refused ()

    /// Read a model's argument object — the shape `Query.toJsonSchema` describes — into the typed
    /// argument list, answering with EVERY refusal: an unknown member is `UnknownParam` naming the
    /// declared parameters (whatever the read policy: a parameter the query does not declare is never
    /// read past), and a value its parameter's type cannot read is `ParamTypeMismatch` naming the JSON
    /// type that was sent. A value of a kind no column type spells (an array, an object), and a
    /// document that is not an object at all, is `UnreadableArgs` carrying the decode refusal (Phase
    /// 385). Required-ness is not checked here; `Query.validateParams` /
    /// `validateParamsAll` take the list this returns.
    let decodeArgsJson (q: Query) (el: JVal) : Result<(string * Cell) list, QueryError list> =
        match el with
        | JObj fields ->
            let declared = q.Params |> List.map _.Name

            // A JSON object may repeat a member; a repeated parameter is `DuplicateParam` (Phase
            // 307), at its second occurrence, exactly as `validateParams` refuses it.
            let decoded =
                fields
                |> List.mapi (fun i (name, v) ->
                    if fields |> List.take i |> List.exists (fun (n, _) -> n = name) then
                        Error(DuplicateParam name)
                    else
                        match q.Params |> List.tryFind (fun p -> p.Name = name) with
                        | None -> Error(UnknownParam(name, declared))
                        | Some p -> argCell name p.Type v)

            match
                decoded
                |> List.choose (function
                    | Error e -> Some e
                    | Ok _ -> None)
            with
            | [] ->
                Ok(
                    decoded
                    |> List.choose (function
                        | Ok a -> Some a
                        | Error _ -> None)
                )
            | errors -> Error errors
        | other ->
            Error
                [ UnreadableArgs(
                      DecodeError.make
                          DecodeCode.WrongKind
                          "object"
                          ("a query's arguments are one JSON object keyed by parameter name, not a JSON "
                           + JVal.kindName other)
                  ) ]

    /// `decodeArgsJson` over text. Text that is not JSON is `UnreadableArgs` carrying the parser's
    /// refusal at the root (Phase 385).
    let decodeArgs (q: Query) (s: string) : Result<(string * Cell) list, QueryError list> =
        match Decoder.parse s with
        | Error e -> Error [ UnreadableArgs e ]
        | Ok el -> decodeArgsJson q el

    // ---- strict read policy (Phase 251) ----
    // `CapabilityCodec`'s policy, over this codec's documents: `Strict` checks that every object
    // this codec reads carries only the members it reads, then runs the ordinary decoder, so
    // `Lenient` is byte-for-byte the old behaviour. The embedded `source` and `rows` documents are
    // the column codec's, read by its own rules; the check stops at them.

    // Phase 310 — the members check is the decode layer's strict policy (`Decoder.members`); the
    // refusal keeps this codec's sentence, naming the object it is in. The helpers are the seam
    // codecs' one set (`SeamCodec`, Phase 388), shared with `CapabilityCodec`.
    open FunctionInternals.Reads

    let private strictQuery (el: JVal) =
        members
            "query"
            [ "$type"
              "id"
              "params"
              "resultSchema"
              "effect"
              "source"
              "timeoutMs"
              "pageSize"
              "where"
              "orderBy" ]
            el
        |> Result.bind (fun () -> within "params" (each (members "query parameter" [ "name"; "type"; "required" ])) el)
        |> Result.bind (fun () -> within "resultSchema" (each (members "result column" [ "name"; "type" ])) el)
        |> Result.bind (fun () -> within "effect" (members "effect" EffectCodec.members) el)
        |> Result.bind (fun () ->
            within "where" (each (fun p -> members "filter predicate" (QueryShape.predicateMembers p) p)) el)
        |> Result.bind (fun () -> within "orderBy" (each (members "order key" [ "column"; "direction" ])) el)

    let private strictResult (el: JVal) =
        members "query result" [ "$type"; "rows"; "pageNum"; "totalRowCount"; "nextPageToken" ] el

    let private strictDeferredResult (el: JVal) =
        let extra =
            match tagOf el with
            | Some "ready" -> [ "value" ]
            | Some "failed" -> [ "message" ]
            | _ -> []

        members "deferred" ("$type" :: extra) el
        |> Result.bind (fun () ->
            match tagOf el with
            | Some "ready" -> within "value" strictResult el
            | _ -> Ok())

    let private readWith
        (policy: ReadPolicy)
        (check: Decoder<unit>)
        (read: JVal -> Result<'T, DecodeError>)
        (s: string)
        : Result<'T, DecodeError> =
        Decoder.parse s
        |> Result.bind (fun el ->
            match policy with
            | ReadPolicy.Lenient -> read el
            | ReadPolicy.Strict -> check el |> Result.bind (fun () -> read el))

    /// Decode a query declaration under a read policy, answering a typed refusal (Phase 295; Phase
    /// 310's convention): its code, the path to the value at fault, and `decodeWith`'s sentence.
    /// `Strict` refuses an unknown member of the declaration, its parameters, its result columns or
    /// its effect. A parse failure is refused at the root.
    let decodeDetailedWith (policy: ReadPolicy) (s: string) : Result<Query, DecodeError> =
        readWith policy strictQuery queryOf s

    /// `decodeResult` under a read policy, answering a typed refusal (Phase 295).
    let decodeResultDetailedWith (policy: ReadPolicy) (s: string) : Result<QueryResult, DecodeError> =
        readWith policy strictResult resultOf s

    /// `decodeDeferredResult` under a read policy, answering a typed refusal (Phase 295).
    let decodeDeferredResultDetailedWith (policy: ReadPolicy) (s: string) : Result<Deferred<QueryResult>, DecodeError> =
        readWith policy strictDeferredResult deferredResultOf s

    /// Decode a query declaration — `Result`-typed with a named error, as `CapabilityCodec.decode`
    /// (Phase 295: the sentence of `decodeDetailedWith`'s refusal; it was an `ExecutionFailed` before).
    let decode (s: string) : Result<Query, string> =
        decodeDetailedWith ReadPolicy.Lenient s |> Result.mapError DecodeError.describe

    /// Decode a `QueryResult`, `Result`-typed with a named error (Phase 295).
    let decodeResult (s: string) : Result<QueryResult, string> =
        decodeResultDetailedWith ReadPolicy.Lenient s
        |> Result.mapError DecodeError.describe

    /// Decode a `Deferred<QueryResult>`, `Result`-typed with a named error (Phase 295).
    let decodeDeferredResult (s: string) : Result<Deferred<QueryResult>, string> =
        decodeDeferredResultDetailedWith ReadPolicy.Lenient s
        |> Result.mapError DecodeError.describe

    /// `decode` under a read policy: `Strict` refuses an unknown member of the declaration, its
    /// parameters, its result columns or its effect.
    let decodeWith (policy: ReadPolicy) (s: string) : Result<Query, string> =
        decodeDetailedWith policy s |> Result.mapError DecodeError.describe

    /// `decodeResult` under a read policy.
    let decodeResultWith (policy: ReadPolicy) (s: string) : Result<QueryResult, string> =
        decodeResultDetailedWith policy s |> Result.mapError DecodeError.describe

    /// `decodeDeferredResult` under a read policy: `Strict` refuses an unknown member of the
    /// envelope or of the result it carries.
    let decodeDeferredResultWith (policy: ReadPolicy) (s: string) : Result<Deferred<QueryResult>, string> =
        decodeDeferredResultDetailedWith policy s
        |> Result.mapError DecodeError.describe
