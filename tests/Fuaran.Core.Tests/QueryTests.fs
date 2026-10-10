module Fuaran.Core.Tests.QueryTests

open Expecto
open Fuaran.Core

let private sampleQuery: Query =
    { Id = "sales-by-region"
      Params =
        [ { Name = "year"
            Type = IntType
            Required = true }
          { Name = "region"
            Type = StringType
            Required = false } ]
      ResultSchema = [ "region", StringType; "revenue", FloatType ]
      Effect =
        { Host = ReadsHost
          Determinism = Effect.network }
      Source = Ref "sales"
      TimeoutMs = Some 5000
      PageSize = Some 100
      Where = []
      OrderBy = [] }

let private sampleResult: QueryResult =
    { Rows =
        { Schema = [ "region", StringType; "revenue", FloatType ]
          Columns =
            [ Column.ofStrs "region" (Vector.ofList [ "UK"; "US" ]) (Validity.all 2)
              Column.ofFloats "revenue" (Vector.ofList [ 1234.5; 0.0 ]) (Validity.ofList [ true; false ]) ] }
      PageNum = 0
      TotalRowCount = Some 2
      NextPageToken = Some "tok-1" }

let private registered =
    QueryRegistry.register sampleQuery QueryRegistry.empty
    |> Result.toOption
    |> Option.get

[<Tests>]
let tests =
    testList
        "Query"
        [ testCase "validateParams accepts in-type args + an absent optional param"
          <| fun _ ->
              Expect.isOk (Query.validateParams sampleQuery [ "year", Int 2026 ]) "in-type required only"

              Expect.isOk
                  (Query.validateParams sampleQuery [ "year", Int 2026; "region", Str "UK" ])
                  "in-type required + optional"

          testCase "validateParams rejects a type mismatch by name"
          <| fun _ ->
              match Query.validateParams sampleQuery [ "year", Str "2026" ] with
              | Error(ParamTypeMismatch("year", IntType, StringType)) -> ()
              | other -> failtestf "expected ParamTypeMismatch, got %A" other

          testCase "validateParams rejects an unknown param, enumerating the declared ones"
          <| fun _ ->
              match Query.validateParams sampleQuery [ "yr", Int 2026 ] with
              | Error(UnknownParam("yr", declared)) -> Expect.contains declared "year" "declared names surfaced"
              | other -> failtestf "expected UnknownParam, got %A" other

          testCase "validateParams rejects a missing required param"
          <| fun _ ->
              match Query.validateParams sampleQuery [ "region", Str "UK" ] with
              | Error(RequiredParamsUnbound names) -> Expect.contains names "year" "required name surfaced"
              | other -> failtestf "expected RequiredParamsUnbound, got %A" other

          testCase "registry is default-deny: an unregistered id is NoSuchQuery"
          <| fun _ ->
              let resolve (_: Query) = Ready Unchecked.defaultof<QueryResult>

              match QueryRegistry.dispatch QueryRegistry.empty "nope" [] resolve with
              | Error(NoSuchQuery("nope", _)) -> ()
              | other -> failtestf "expected NoSuchQuery, got %A" other

          testCase "register is additive: a duplicate id is DuplicateQuery"
          <| fun _ ->
              let r =
                  QueryRegistry.register sampleQuery QueryRegistry.empty
                  |> Result.toOption
                  |> Option.get

              match QueryRegistry.register sampleQuery r with
              | Error(DuplicateQuery "sales-by-region") -> ()
              | other -> failtestf "expected DuplicateQuery, got %A" other

          testCase "enumerate is id-stable regardless of insertion order"
          <| fun _ ->
              let a = { sampleQuery with Id = "zzz" }
              let b = { sampleQuery with Id = "aaa" }

              let r =
                  QueryRegistry.empty
                  |> QueryRegistry.register a
                  |> Result.bind (QueryRegistry.register b)
                  |> Result.toOption
                  |> Option.get

              Expect.equal (QueryRegistry.enumerate r |> List.map _.Id) [ "aaa"; "zzz" ] "id-sorted"

          testCase "dispatch validates params before running the resolver"
          <| fun _ ->
              let mutable ran = false

              let resolve (_: Query) =
                  ran <- true
                  Ready Unchecked.defaultof<QueryResult>

              let r =
                  QueryRegistry.register sampleQuery QueryRegistry.empty
                  |> Result.toOption
                  |> Option.get
              // a type-mismatched param must short-circuit before the resolver runs.
              match QueryRegistry.dispatch r "sales-by-region" [ "year", Str "x" ] resolve with
              | Error(ParamTypeMismatch _) -> Expect.isFalse ran "resolver must not run on invalid params"
              | other -> failtestf "expected ParamTypeMismatch, got %A" other

          testCase "query declaration round-trips through the codec"
          <| fun _ ->
              match QueryCodec.decode (QueryCodec.encode sampleQuery) with
              | Ok q2 -> Expect.equal q2 sampleQuery "declaration round-trip"
              | Error e -> failtestf "decode failed: %A" e

          testCase "a query with a multi-factor determinism round-trips and keys its capture by the canonical label"
          <| fun _ ->
              let q =
                  { sampleQuery with
                      Effect =
                          { Host = ReadsHost
                            Determinism = Set.ofList [ ClockFactor; NetworkFactor ] } }

              Expect.equal (Query.determinismTag q) "clock+network" "the capture label names both factors"

              match QueryCodec.decode (QueryCodec.encode q) with
              | Ok q2 -> Expect.equal q2 q "a multi-factor declaration round-trips"
              | Error e -> failtestf "decode failed: %A" e

          testCase "query result round-trips through the codec"
          <| fun _ ->
              let qr: QueryResult =
                  { Rows =
                      { Schema = [ "region", StringType; "revenue", FloatType ]
                        Columns =
                          [ Column.ofStrs "region" (Vector.ofList [ "UK"; "US" ]) (Validity.all 2)
                            Column.ofFloats "revenue" (Vector.ofList [ 1234.5; 0.0 ]) (Validity.ofList [ true; false ]) ] }
                    PageNum = 0
                    TotalRowCount = Some 2
                    NextPageToken = Some "tok-1" }

              match QueryCodec.decodeResult (QueryCodec.encodeResult qr) with
              | Ok qr2 -> Expect.equal qr2 qr "result round-trip"
              | Error e -> failtestf "decode failed: %A" e

          testCase "invocationKey is param-order-independent and arg-sensitive"
          <| fun _ ->
              let k1 = Query.invocationKey sampleQuery [ "year", Int 2026; "region", Str "UK" ]
              let k2 = Query.invocationKey sampleQuery [ "region", Str "UK"; "year", Int 2026 ]
              let k3 = Query.invocationKey sampleQuery [ "year", Int 2025; "region", Str "UK" ]
              Expect.equal k1 k2 "order-independent"
              Expect.notEqual k1 k3 "arg-sensitive"

          // ---- the Deferred envelope on the seam (Phase 198) ----
          // The seam's three outcomes, one case each, plus the invariant that makes the fourth
          // unreachable. `Ok(Failed _)` has no test because it cannot be produced — the typed-refusal
          // case below is what asserts that, and it is the assertion that would go red if `invoke`
          // ever passed a resolver's `Failed` through.

          testCase "a settled resolver settles: dispatch returns Ok(Ready result)"
          <| fun _ ->
              match
                  QueryRegistry.dispatch registered "sales-by-region" [ "year", Int 2026 ] (fun _ -> Ready sampleResult)
              with
              | Ok(Ready r) -> Expect.equal r sampleResult "the resolver's rows ride the envelope unchanged"
              | other -> failtestf "expected Ok(Ready _), got %A" other

          testCase "a pending resolver stays pending: the host expresses 'not yet' in Core's vocabulary"
          <| fun _ ->
              match QueryRegistry.dispatch registered "sales-by-region" [ "year", Int 2026 ] (fun _ -> Pending) with
              | Ok Pending -> ()
              | other -> failtestf "expected Ok Pending, got %A" other

          testCase "a resolver failure is REFUSED typed: ExecutionFailed, never Ok(Failed _)"
          <| fun _ ->
              match
                  QueryRegistry.dispatch registered "sales-by-region" [ "year", Int 2026 ] (fun _ ->
                      Failed "source unreachable")
              with
              | Error(ExecutionFailed("source unreachable", recoverable)) ->
                  Expect.isEmpty recoverable "no recoverable alternatives are invented for the host"
              | other -> failtestf "expected Error(ExecutionFailed _), got %A" other

          testCase "the query envelope round-trips the wire for Pending / Ready / Failed"
          <| fun _ ->
              for d in [ Pending; Ready sampleResult; Failed "source unreachable" ] do
                  match QueryCodec.decodeDeferredResult (QueryCodec.encodeDeferredResult d) with
                  | Ok d2 -> Expect.equal d2 d "deferred query result round-trip"
                  | Error e -> failtestf "decode failed for %A: %A" d e ]

// ---- Phase 295: the widening lattice, a typed resolver fault, decode failures as decode failures ----

let private floatQuery: Query =
    { sampleQuery with
        Id = "q-float"
        Params =
            [ { Name = "ratio"
                Type = FloatType
                Required = true } ] }

let private emptyResult: QueryResult =
    { sampleResult with
        TotalRowCount = None
        NextPageToken = None }

[<Tests>]
let convergenceTests =
    testList
        "Query.convergence (Phase 295)"
        [ testCase "an int fills a float parameter and reaches the resolver as a float"
          <| fun _ ->
              Expect.equal (Query.validateParams floatQuery [ "ratio", Int 2 ]) (Ok()) "int widens into float"

              match Query.validateParams sampleQuery [ "year", Float 2.0 ] with
              | Error(ParamTypeMismatch("year", IntType, FloatType)) -> ()
              | other -> failtestf "a float must not narrow to an int, got %A" other

              let mutable seen = []

              let r =
                  Query.invokeWithArgs floatQuery [ "ratio", Int 2 ] (fun _ a ->
                      seen <- a
                      Ok(Ready emptyResult))

              Expect.equal r (Ok(Ready emptyResult)) "dispatched"
              Expect.equal seen [ "ratio", Float 2.0 ] "promoted to the declared type"

          testCase "a resolver's typed fault reaches the caller by name"
          <| fun _ ->
              let run (f: Result<Deferred<QueryResult>, ResolveFault>) =
                  Query.invokeWithArgs sampleQuery [ "year", Int 2026 ] (fun _ _ -> f)

              Expect.equal (run (Error(ResolveFault.SourceMissing "sales"))) (Error(SourceNotResolved "sales")) "source"
              Expect.equal (run (Error ResolveFault.TimedOut)) (Error Timeout) "timeout"

              Expect.equal
                  (run (Error(ResolveFault.Failed("busy", [ "year" ]))))
                  (Error(ExecutionFailed("busy", [ "year" ])))
                  "recoverable filled"

          testCase "a decode failure is a decode failure, with a typed form beside it"
          <| fun _ ->
              let doc = """{"$type":"query","id":3}"""

              match QueryCodec.decode doc with
              | Error m -> Expect.equal m "expected string, got int" "the refusal's sentence, not an ExecutionFailed"
              | Ok q -> failtestf "decoded %A" q

              match QueryCodec.decodeDetailedWith ReadPolicy.Lenient doc with
              | Error e -> Expect.equal e.Path [ PathSegment.Key "id" ] "the path names the member"
              | Ok q -> failtestf "decoded %A" q

              match QueryCodec.decodeDetailedWith ReadPolicy.Lenient "{" with
              | Error e -> Expect.equal e.Code DecodeCode.InvalidJson "a parse failure is refused at the root"
              | Ok q -> failtestf "decoded %A" q ]

// ---- Phase 307: a parameter bound twice ----

[<Tests>]
let duplicateParamTests =
    testList
        "Query duplicate parameters (Phase 307)"
        [ testCase "a name bound twice is DuplicateParam at validateParams, validateParamsAll and decodeArgs"
          <| fun _ ->
              let args = [ "year", Int 2024; "year", Cell.Null ]

              Expect.equal (Query.validateParams sampleQuery args) (Error(DuplicateParam "year")) "validateParams"

              Expect.equal
                  (Query.validateParamsAll sampleQuery args)
                  (Error [ DuplicateParam "year" ])
                  "validateParamsAll"

              Expect.equal
                  (QueryCodec.decodeArgs sampleQuery """{"year":2024,"year":2025}""")
                  (Error [ DuplicateParam "year" ])
                  "decodeArgs"

              Expect.equal
                  (Query.validateParams sampleQuery [ "year", Int 2024; "region", Str "UK" ])
                  (Ok())
                  "distinct names are accepted as before"

          testCase "DuplicateParam round-trips through its wire document and reads as a refusal"
          <| fun _ ->
              let e = DuplicateParam "year"
              Expect.equal (QueryCodec.decodeQueryError (QueryCodec.encodeQueryError e)) (Ok e) "round trip"
              Expect.stringStarts (QueryError.describe e) "Refused: " "described" ]

// ---- Phase 316: the registry refuses a declaration that names a parameter twice ----

[<Tests>]
let registrationDuplicateParamTests =
    testList
        "Query registration refuses a repeated parameter name (Phase 316)"
        [ testCase "register refuses a declaration naming a parameter twice, by name, and holds nothing"
          <| fun _ ->
              let twice =
                  { sampleQuery with
                      Id = "twice"
                      Params =
                          sampleQuery.Params
                          @ [ { Name = "year"
                                Type = StringType
                                Required = false } ] }

              Expect.equal
                  (QueryRegistry.register twice QueryRegistry.empty)
                  (Error(DuplicateParam "year"))
                  "the repeated name is refused at registration"

              Expect.isOk (QueryRegistry.register sampleQuery QueryRegistry.empty) "distinct names still register" ]

// ---- Phase 385: one admission gate at every reader, and the typed resolver at every dispatcher ----

/// `text` with its first `"$type"` member rewritten to `tag` (or removed, for `None`).
let private retag (from: string) (tag: string option) (text: string) : string =
    let tagMember = "\"$type\":\"" + from + "\""

    match tag with
    | Some t -> text.Replace(tagMember, "\"$type\":\"" + t + "\"")
    | None -> text.Replace(tagMember + ",", "")

[<Tests>]
let readerAdmissionTests =
    testList
        "Query readers admit what the registry admits (Phase 385)"
        [ testCase "a query document whose $type is not query is a DecodeError naming the tag"
          <| fun _ ->
              let text = QueryCodec.encode sampleQuery
              Expect.equal (QueryCodec.decode text) (Ok sampleQuery) "round trip"

              let other = retag "query" (Some "queryResult") text
              Expect.notEqual other text "the probe edited the tag"

              match QueryCodec.decodeDetailedWith ReadPolicy.Lenient other with
              | Error e ->
                  Expect.equal e.Code DecodeCode.UnknownTag "an unknown tag"
                  Expect.equal e.Path [ PathSegment.Key "$type" ] "at the tag"
                  Expect.equal e.Message "not a query declaration: queryResult" "naming the tag"
              | Ok q -> failtestf "another document type was read as a query: %A" q

              match QueryCodec.decodeDetailedWith ReadPolicy.Lenient (retag "query" None text) with
              | Error e -> Expect.equal e.Code DecodeCode.MissingField "an untagged document is refused"
              | Ok q -> failtestf "an untagged document was read as a query: %A" q

          testCase "a result document whose $type is not queryResult is a DecodeError naming the tag"
          <| fun _ ->
              let text = QueryCodec.encodeResult sampleResult
              Expect.equal (QueryCodec.decodeResult text) (Ok sampleResult) "round trip"

              Expect.equal
                  (QueryCodec.decodeResult (retag "queryResult" (Some "query") text))
                  (Error "not a query result: query")
                  "another document type"

              Expect.isError (QueryCodec.decodeResult (retag "queryResult" None text)) "an untagged document"

          testCase "a declaration naming a parameter twice is refused at the reader with the registry's DuplicateParam"
          <| fun _ ->
              let twice =
                  { sampleQuery with
                      Params =
                          sampleQuery.Params
                          @ [ { Name = "year"
                                Type = StringType
                                Required = false } ] }

              let registryRefusal =
                  match QueryRegistry.register twice QueryRegistry.empty with
                  | Error e -> e
                  | Ok _ -> failtest "the registry admitted a repeated parameter"

              Expect.equal registryRefusal (DuplicateParam "year") "the registry's refusal"

              match QueryCodec.decodeDetailedWith ReadPolicy.Lenient (QueryCodec.encode twice) with
              | Error e ->
                  Expect.equal e.Message (QueryError.describe registryRefusal) "the reader's refusal is the registry's"
                  Expect.equal e.Path [ PathSegment.Key "params" ] "at the parameters"
              | Ok q -> failtestf "the reader admitted what the registry refuses: %A" q

          testCase "decodeArgs answers unreadable input with UnreadableArgs, never ExecutionFailed"
          <| fun _ ->
              match QueryCodec.decodeArgs sampleQuery "{" with
              | Error [ UnreadableArgs e ] ->
                  Expect.equal e.Code DecodeCode.InvalidJson "a parse failure"
                  Expect.equal e.Path [] "at the root"
              | other -> failtestf "expected UnreadableArgs, got %A" other

              match QueryCodec.decodeArgs sampleQuery "[2024]" with
              | Error [ UnreadableArgs e ] ->
                  Expect.equal e.Code DecodeCode.WrongKind "not an object"
                  Expect.equal e.Path [] "at the root"
              | other -> failtestf "expected UnreadableArgs, got %A" other

              match QueryCodec.decodeArgs sampleQuery """{"year":{"y":2024},"region":"UK"}""" with
              | Error [ UnreadableArgs e ] ->
                  Expect.equal e.Code DecodeCode.WrongKind "a kind no column type spells"
                  Expect.equal e.Path [ PathSegment.Key "year" ] "at the parameter"
              | other -> failtestf "expected UnreadableArgs at year, got %A" other

              Expect.equal
                  (QueryCodec.decodeArgs sampleQuery """{"year":"2024"}""")
                  (Error [ ParamTypeMismatch("year", IntType, StringType) ])
                  "a scalar of the wrong type is still a type mismatch"

              let e =
                  UnreadableArgs(DecodeError.make DecodeCode.InvalidJson "JSON text" "unexpected end")

              Expect.equal (QueryCodec.decodeQueryError (QueryCodec.encodeQueryError e)) (Ok e) "round trip"

              Expect.equal
                  (QueryError.describe e)
                  "Refused: the arguments could not be read at $: unexpected end. Send one JSON object keyed by parameter name."
                  "described" ]

/// A resolver answering per page: the first page a result, then a timeout, a recoverable failure and
/// a missing source.
let private typedPages (_: Query) (token: string option) (args: (string * Cell) list) =
    match token with
    | None -> Ok(Ready { sampleResult with PageNum = 0 })
    | Some "p1" -> Error ResolveFault.TimedOut
    | Some "p2" -> Error(ResolveFault.Failed("busy", [ "year" ]))
    | Some "p3" -> Error(ResolveFault.SourceMissing "sales")
    | Some other -> Ok(Failed("no page " + other + " for " + string (List.length args)))

let private tokens385 = [ None; Some "p1"; Some "p2"; Some "p3" ]

[<Tests>]
let typedDispatchTests =
    testList
        "The typed resolver reaches paging, capture and replay (Phase 385)"
        [ testCase "invokePageWithArgs hands the resolver the token and the promoted arguments"
          <| fun _ ->
              let q =
                  { sampleQuery with
                      Params =
                          [ { Name = "w"
                              Type = FloatType
                              Required = true } ] }

              let seen = ref None

              let answer =
                  Query.invokePageWithArgs q [ "w", Int 3 ] (Some "t") (fun _ t args ->
                      seen.Value <- Some(t, args)
                      Error ResolveFault.TimedOut)

              Expect.equal answer (Error Timeout) "the typed fault, by name"
              Expect.equal seen.Value (Some(Some "t", [ "w", Float 3.0 ])) "token and promoted arguments"

              Expect.equal
                  (Query.invokeWithArgs q [ "w", Int 3 ] (fun q' a -> typedPages q' None a))
                  (Query.invokePageWithArgs q [ "w", Int 3 ] None typedPages)
                  "invokeWithArgs is the first page"

          testCase "a ResolveFault is paged, captured and replayed as the refusal answered live"
          <| fun _ ->
              let args = [ "year", Int 2026 ]

              let live =
                  tokens385
                  |> List.map (fun t -> QueryRegistry.dispatchPageWith registered "sales-by-region" args t typedPages)

              Expect.equal
                  live
                  [ Ok(Ready { sampleResult with PageNum = 0 })
                    Error Timeout
                    Error(ExecutionFailed("busy", [ "year" ]))
                    Error(SourceNotResolved "sales") ]
                  "each page's typed answer"

              let captured, journal =
                  tokens385
                  |> List.fold
                      (fun (acc, j) t ->
                          let c =
                              QueryRegistry.dispatchPageCapturedWith
                                  OpStream.defaultHash
                                  QueryCodec.encodeResult
                                  registered
                                  "sales-by-region"
                                  args
                                  t
                                  typedPages
                                  j

                          acc @ [ c.Outcome ], c.Journal)
                      ([], [])

              Expect.equal captured live "capture answers what the live dispatch answers"

              let replayed =
                  tokens385
                  |> List.fold
                      (fun (acc, cursor) t ->
                          let r =
                              QueryRegistry.dispatchReplayedWith
                                  QueryCodec.decodeResult
                                  registered
                                  "sales-by-region"
                                  args
                                  t
                                  (fun _ _ _ -> failtest "replay must not resolve a network query")
                                  cursor
                                  journal

                          match r.Outcome with
                          | Error(ReplayFailure.Unanswered f) -> failtestf "replay faulted: %A" f
                          | a -> acc @ [ a ], r.Cursor)
                      ([], Map.empty)
                  |> fst

              Expect.equal
                  replayed
                  (live |> List.map (Result.mapError ReplayFailure.Refused))
                  "replay answers each page as it was answered live"

              let unpagedCapture =
                  QueryRegistry.dispatchCapturedWith
                      OpStream.defaultHash
                      QueryCodec.encodeResult
                      registered
                      "sales-by-region"
                      args
                      (fun q a -> typedPages q (Some "p1") a)
                      []

              Expect.equal unpagedCapture.Outcome (Error Timeout) "the unpaged capture"

              let unpagedReplay =
                  QueryRegistry.dispatchReplayedWith
                      QueryCodec.decodeResult
                      registered
                      "sales-by-region"
                      args
                      None
                      typedPages
                      Map.empty
                      unpagedCapture.Journal

              Expect.equal unpagedReplay.Outcome (Error(ReplayFailure.Refused Timeout)) "replays as the timeout"

          testCase "an untyped failure replays as the ExecutionFailed answered live; a pre-0.36 reason as before"
          <| fun _ ->
              let args = [ "year", Int 2026 ]

              let capture =
                  QueryRegistry.dispatchCaptured
                      OpStream.defaultHash
                      QueryCodec.encodeResult
                      registered
                      "sales-by-region"
                      args
                      (fun _ -> Failed "down")
                      []

              let answer = capture.Outcome
              Expect.equal answer (Error(ExecutionFailed("down", []))) "live"

              let replay j =
                  QueryRegistry.dispatchReplayed
                      QueryCodec.decodeResult
                      registered
                      "sales-by-region"
                      args
                      None
                      (fun _ _ -> Ready sampleResult)
                      Map.empty
                      j

              Expect.equal
                  (replay capture.Journal).Outcome
                  (Result.mapError ReplayFailure.Refused answer)
                  "replayed exactly"

              // A journal written before 0.36.0 recorded the refusal's sentence.
              let sentence = QueryError.describe (ExecutionFailed("down", []))

              let legacy =
                  OpStream.captureEffectKeyed
                      OpStream.defaultHash
                      QueryCodec.encodeResult
                      (Query.determinismTag sampleQuery)
                      (Query.invocationKey sampleQuery args)
                      (fun () -> Some(Error sentence))
                      []

              Expect.equal
                  (replay legacy.Journal).Outcome
                  (Error(ReplayFailure.Refused(ExecutionFailed(sentence, []))))
                  "the recorded text, as it always was" ]

// ---- Phase 398: the declared filter and order ----

/// A declaration over one column of every type, filtered by one predicate of every kind and ordered
/// by two keys.
let private shaped: Query =
    { sampleQuery with
        Id = "shaped"
        ResultSchema =
            [ "region", StringType
              "revenue", FloatType
              "day", DateType
              "amount", DecimalType
              "n", IntType
              "ok", BoolType
              "at", TimestampType ]
        Where =
            [ ColumnPredicate.EqualTo("region", Str "UK")
              ColumnPredicate.GreaterThan("revenue", Float 1.5)
              ColumnPredicate.AtLeast("day", Date "2026-01-01")
              ColumnPredicate.LessThan("amount", Decimal "12.5")
              ColumnPredicate.AtMost("n", Int 10)
              ColumnPredicate.Contains("region", "U")
              ColumnPredicate.IsNull "ok"
              ColumnPredicate.IsNotNull "at" ]
        OrderBy =
            [ { Column = "day"
                Direction = SortDirection.Descending }
              { Column = "region"
                Direction = SortDirection.Ascending } ] }

let private shapedArgs = [ "year", Int 2026 ]

/// The registration refusal of `shaped` with its filter and order replaced.
let private refusal (where: ColumnPredicate list) (order: SortKey list) =
    QueryRegistry.register
        { shaped with
            Where = where
            OrderBy = order }
        QueryRegistry.empty

/// The column a predicate names, read by the test rather than by the package.
let private predicateColumn (p: ColumnPredicate) : string =
    match p with
    | ColumnPredicate.EqualTo(c, _)
    | ColumnPredicate.GreaterThan(c, _)
    | ColumnPredicate.AtLeast(c, _)
    | ColumnPredicate.LessThan(c, _)
    | ColumnPredicate.AtMost(c, _)
    | ColumnPredicate.Contains(c, _)
    | ColumnPredicate.IsNull c
    | ColumnPredicate.IsNotNull c -> c

[<Tests>]
let whereOrderByTests =
    testList
        "Query Where / OrderBy (Phase 398)"
        [ testCase "a declaration with neither member encodes the bytes it encoded before the members existed"
          <| fun _ ->
              // The encoder appends the two members only when non-empty, so with both empty its bytes
              // are the encoder's before Phase 398; this pins them.
              Expect.equal
                  (QueryCodec.encode sampleQuery)
                  "{\"$type\":\"query\",\"effect\":{\"determinism\":\"network\",\"host\":\"readsHost\"},\"id\":\"sales-by-region\",\"pageSize\":100,\"params\":[{\"name\":\"year\",\"required\":true,\"type\":\"int\"},{\"name\":\"region\",\"required\":false,\"type\":\"string\"}],\"resultSchema\":[{\"name\":\"region\",\"type\":\"string\"},{\"name\":\"revenue\",\"type\":\"float\"}],\"source\":{\"ref\":\"sales\",\"schema\":[]},\"timeoutMs\":5000}"
                  "byte-identical"

          testCase "every predicate kind and the order round-trip the codec, under both read policies"
          <| fun _ ->
              let text = QueryCodec.encode shaped
              Expect.stringContains text "\"where\":[" "the filter is written"
              Expect.stringContains text "\"orderBy\":[" "the order is written"

              Expect.stringContains
                  text
                  "{\"$type\":\"atLeast\",\"column\":\"day\",\"type\":\"date\",\"value\":\"2026-01-01\"}"
                  "a comparison carries its literal's type and value"

              Expect.stringContains
                  text
                  "{\"$type\":\"lessThan\",\"column\":\"amount\",\"type\":\"decimal\",\"value\":\"12.5\"}"
                  "a decimal literal is decimal text, as the column codec writes it"

              Expect.stringContains text "{\"column\":\"day\",\"direction\":\"descending\"}" "an order key"
              Expect.equal (QueryCodec.decode text) (Ok shaped) "lenient"
              Expect.equal (QueryCodec.decodeWith ReadPolicy.Strict text) (Ok shaped) "strict"

          testCase "the registry refuses a filter or an order its result schema does not admit, by name"
          <| fun _ ->
              let declared = shaped.ResultSchema |> List.map fst
              Expect.isOk (QueryRegistry.register shaped QueryRegistry.empty) "the well-formed shape registers"

              Expect.equal
                  (refusal [ ColumnPredicate.IsNull "nope" ] [])
                  (Error(UnknownColumn("nope", declared)))
                  "an undeclared predicate column"

              Expect.equal
                  (refusal
                      []
                      [ { Column = "nope"
                          Direction = SortDirection.Ascending } ])
                  (Error(UnknownColumn("nope", declared)))
                  "an undeclared order column"

              Expect.equal
                  (refusal [ ColumnPredicate.EqualTo("revenue", Int 3) ] [])
                  (Error(PredicateTypeMismatch("revenue", FloatType, IntType)))
                  "a literal of another type: no widening in a filter"

              Expect.equal
                  (refusal [ ColumnPredicate.Contains("n", "1") ] [])
                  (Error(PredicateNotApplicable("contains", "n", IntType)))
                  "contains on a column that is not a string"

              for bad, column in
                  [ ColumnPredicate.EqualTo("region", Null), "region"
                    ColumnPredicate.AtLeast("day", Date "2026-13-01"), "day"
                    ColumnPredicate.LessThan("amount", Decimal "12.50"), "amount"
                    ColumnPredicate.GreaterThan("revenue", Float nan), "revenue" ] do
                  match refusal [ bad ] [] with
                  | Error(IllFormedLiteral(c, reason)) ->
                      Expect.equal c column "names the column"
                      Expect.isNotEmpty reason "says why"
                  | other -> failtestf "%A was not refused as an ill-formed literal: %A" bad other

              Expect.equal
                  (refusal
                      []
                      [ { Column = "day"
                          Direction = SortDirection.Ascending }
                        { Column = "day"
                          Direction = SortDirection.Descending } ])
                  (Error(DuplicateSortColumn "day"))
                  "an order naming a column twice"

              // `replace` runs the same gate.
              let reg =
                  QueryRegistry.register shaped QueryRegistry.empty
                  |> Result.toOption
                  |> Option.get

              Expect.equal
                  (QueryRegistry.replace
                      { shaped with
                          Where = [ ColumnPredicate.IsNull "nope" ] }
                      reg)
                  (Error(UnknownColumn("nope", declared)))
                  "replace refuses it too"

          testCase "the declaration reader refuses what the registry refuses, at the member at fault"
          <| fun _ ->
              let text = QueryCodec.encode shaped
              let declared = shaped.ResultSchema |> List.map fst

              let refusedAt (edited: string) =
                  Expect.notEqual edited text "the probe edited the document"

                  match QueryCodec.decodeDetailedWith ReadPolicy.Lenient edited with
                  | Error e -> e
                  | Ok q -> failtestf "an inadmissible declaration was read: %A" q

              // A literal whose stated type is not the column's.
              let retyped =
                  refusedAt (
                      text.Replace(
                          "{\"$type\":\"atMost\",\"column\":\"n\",\"type\":\"int\",\"value\":10}",
                          "{\"$type\":\"atMost\",\"column\":\"n\",\"type\":\"float\",\"value\":10.5}"
                      )
                  )

              Expect.equal retyped.Path [ PathSegment.Key "where"; PathSegment.Index 4 ] "at the predicate"

              Expect.equal
                  retyped.Message
                  (QueryError.describe (PredicateTypeMismatch("n", IntType, FloatType)))
                  "with the registry's sentence"

              // A predicate naming an undeclared column.
              let stray =
                  refusedAt (
                      text.Replace("\"$type\":\"isNull\",\"column\":\"ok\"", "\"$type\":\"isNull\",\"column\":\"nope\"")
                  )

              Expect.equal stray.Path [ PathSegment.Key "where"; PathSegment.Index 6 ] "at the predicate"
              Expect.equal stray.Message (QueryError.describe (UnknownColumn("nope", declared))) "named"

              // An order naming a column twice.
              let twice =
                  refusedAt (
                      text.Replace(
                          "{\"column\":\"region\",\"direction\":\"ascending\"}",
                          "{\"column\":\"day\",\"direction\":\"ascending\"}"
                      )
                  )

              Expect.equal twice.Path [ PathSegment.Key "orderBy"; PathSegment.Index 1 ] "at the second key"

              // A literal that is not a value of the type it states.
              let malformed =
                  refusedAt (text.Replace("\"value\":\"2026-01-01\"", "\"value\":\"2026-02-30\""))

              Expect.equal malformed.Code DecodeCode.OutOfRange "out of range"

              Expect.equal
                  malformed.Path
                  [ PathSegment.Key "where"; PathSegment.Index 2; PathSegment.Key "value" ]
                  "at the value"

              // An unknown predicate kind and an unknown direction.
              let unknownKind =
                  refusedAt (text.Replace("\"$type\":\"contains\"", "\"$type\":\"matches\""))

              Expect.equal unknownKind.Code DecodeCode.UnknownTag "an unknown predicate"

              let unknownDir =
                  refusedAt (text.Replace("\"direction\":\"descending\"", "\"direction\":\"down\""))

              Expect.equal unknownDir.Code DecodeCode.UnknownTag "an unknown direction"

          testCase "the strict policy refuses an undeclared member of a predicate or an order key"
          <| fun _ ->
              let text = QueryCodec.encode shaped

              for edited in
                  [ text.Replace(
                        "\"$type\":\"isNull\",\"column\":\"ok\"",
                        "\"$type\":\"isNull\",\"column\":\"ok\",\"text\":\"x\""
                    )
                    text.Replace("{\"column\":\"day\",", "{\"column\":\"day\",\"nulls\":\"first\",") ] do
                  Expect.notEqual edited text "the probe edited the document"
                  Expect.equal (QueryCodec.decode edited) (Ok shaped) "lenient reads past it"
                  Expect.isError (QueryCodec.decodeWith ReadPolicy.Strict edited) "strict refuses it"

          testCase "the invocation key sees the filter and the order"
          <| fun _ ->
              let k = Query.invocationKey shaped shapedArgs
              let noWhere = Query.invocationKey { shaped with Where = [] } shapedArgs
              let noOrder = Query.invocationKey { shaped with OrderBy = [] } shapedArgs

              let neither =
                  Query.invocationKey { shaped with Where = []; OrderBy = [] } shapedArgs

              Expect.equal
                  (List.length (List.distinct [ k; noWhere; noOrder; neither ]))
                  4
                  "four declarations, four keys"

              Expect.notEqual
                  (Query.invocationKey
                      { shaped with
                          Where = [ ColumnPredicate.AtMost("n", Int 11) ] }
                      shapedArgs)
                  (Query.invocationKey
                      { shaped with
                          Where = [ ColumnPredicate.AtMost("n", Int 10) ] }
                      shapedArgs)
                  "the literal is keyed"

              Expect.equal (Query.invocationKeyPage shaped shapedArgs None) k "the first page keys as the invocation"

              Expect.notEqual
                  (Query.invocationKeyPage shaped shapedArgs (Some "t"))
                  (Query.invocationKeyPage { shaped with Where = [] } shapedArgs (Some "t"))
                  "a page of a filtered declaration keys apart"

          testCase "the resolver receives the filter and the order, and refuses one it cannot honour by name"
          <| fun _ ->
              let reg =
                  QueryRegistry.register shaped QueryRegistry.empty
                  |> Result.toOption
                  |> Option.get

              let seen = ref None

              let served =
                  QueryRegistry.dispatchWithArgs reg "shaped" shapedArgs (fun q _ ->
                      seen.Value <- Some(q.Where, q.OrderBy)
                      Ok(Ready sampleResult))

              Expect.equal served (Ok(Ready sampleResult)) "served"
              Expect.equal seen.Value (Some(shaped.Where, shaped.OrderBy)) "handed exactly what was declared"

              let contains = ColumnPredicate.Contains("region", "U")

              let noContains (_: Query) (_: (string * Cell) list) =
                  Error(ResolveFault.PredicateUnsupported contains)

              Expect.equal
                  (QueryRegistry.dispatchWithArgs reg "shaped" shapedArgs noContains)
                  (Error(PredicateNotHonoured contains))
                  "an unhonoured predicate is refused naming it"

              Expect.equal
                  (Query.invokePageWithArgs shaped shapedArgs (Some "p2") (fun _ _ _ ->
                      Error(ResolveFault.OrderUnsupported "day")))
                  (Error(OrderNotHonoured "day"))
                  "an unhonoured order is refused naming the column"

              // Captured and replayed as the refusal it was answered live.
              let capture =
                  QueryRegistry.dispatchCapturedWith
                      OpStream.defaultHash
                      QueryCodec.encodeResult
                      reg
                      "shaped"
                      shapedArgs
                      noContains
                      []

              let live = capture.Outcome
              Expect.equal live (Error(PredicateNotHonoured contains)) "captured live"

              let replayed =
                  QueryRegistry.dispatchReplayedWith
                      QueryCodec.decodeResult
                      reg
                      "shaped"
                      shapedArgs
                      None
                      (fun _ _ _ -> Ok(Ready sampleResult))
                      Map.empty
                      capture.Journal

              Expect.equal replayed.Outcome (Result.mapError ReplayFailure.Refused live) "replayed exactly"

          testCase "the new refusals cross the wire and read as one sentence naming what they refuse"
          <| fun _ ->
              let refusals =
                  [ UnknownColumn("nope", [ "a"; "b" ])
                    UnknownColumn("nope", [])
                    PredicateTypeMismatch("d", DateType, StringType)
                    PredicateNotApplicable("contains", "n", IntType)
                    IllFormedLiteral("d", "not a day that exists")
                    DuplicateSortColumn "d"
                    PredicateNotHonoured(ColumnPredicate.AtLeast("amount", Decimal "0.05"))
                    PredicateNotHonoured(ColumnPredicate.Contains("s", "x"))
                    PredicateNotHonoured(ColumnPredicate.IsNotNull "x")
                    OrderNotHonoured "d" ]

              for e in refusals do
                  Expect.equal
                      (QueryCodec.decodeQueryError (QueryCodec.encodeQueryError e))
                      (Ok e)
                      (sprintf "%A round-trips" e)

                  let sentence = QueryError.describe e
                  Expect.isTrue (sentence.StartsWith "Refused: ") "a refusal sentence"

                  let column =
                      match e with
                      | UnknownColumn(c, _)
                      | PredicateTypeMismatch(c, _, _)
                      | PredicateNotApplicable(_, c, _)
                      | IllFormedLiteral(c, _)
                      | DuplicateSortColumn c
                      | OrderNotHonoured c -> c
                      | PredicateNotHonoured p -> predicateColumn p
                      | other -> failtestf "not a Phase 398 refusal: %A" other

                  Expect.stringContains sentence ("'" + column + "'") "names the column" ]
