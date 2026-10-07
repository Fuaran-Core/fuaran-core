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
      PageSize = Some 100 }

let private sampleResult: QueryResult =
    { Rows =
        { Schema = [ "region", StringType; "revenue", FloatType ]
          Columns =
            [ { Name = "region"
                Type = StringType
                Cells = [ Str "UK"; Str "US" ] }
              { Name = "revenue"
                Type = FloatType
                Cells = [ Float 1234.5; Null ] } ] }
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

              Expect.equal (QueryRegistry.enumerate r |> List.map (fun q -> q.Id)) [ "aaa"; "zzz" ] "id-sorted"

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
                          [ { Name = "region"
                              Type = StringType
                              Cells = [ Str "UK"; Str "US" ] }
                            { Name = "revenue"
                              Type = FloatType
                              Cells = [ Float 1234.5; Null ] } ] }
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
                          let a, _, j' =
                              QueryRegistry.dispatchPageCapturedWith
                                  OpStream.defaultHash
                                  QueryCodec.encodeResult
                                  registered
                                  "sales-by-region"
                                  args
                                  t
                                  typedPages
                                  j

                          acc @ [ a ], j')
                      ([], [])

              Expect.equal captured live "capture answers what the live dispatch answers"

              let replayed =
                  tokens385
                  |> List.fold
                      (fun (acc, cursor) t ->
                          match
                              QueryRegistry.dispatchReplayedWith
                                  QueryCodec.decodeResult
                                  registered
                                  "sales-by-region"
                                  args
                                  t
                                  (fun _ _ _ -> failtest "replay must not resolve a network query")
                                  cursor
                                  journal
                          with
                          | Ok(a, cursor') -> acc @ [ a ], cursor'
                          | Error f -> failtestf "replay faulted: %A" f)
                      ([], Map.empty)
                  |> fst

              Expect.equal replayed live "replay answers each page as it was answered live"

              let unpaged, _, j1 =
                  QueryRegistry.dispatchCapturedWith
                      OpStream.defaultHash
                      QueryCodec.encodeResult
                      registered
                      "sales-by-region"
                      args
                      (fun q a -> typedPages q (Some "p1") a)
                      []

              Expect.equal unpaged (Error Timeout) "the unpaged capture"

              Expect.equal
                  (QueryRegistry.dispatchReplayedWith
                      QueryCodec.decodeResult
                      registered
                      "sales-by-region"
                      args
                      None
                      typedPages
                      Map.empty
                      j1
                   |> Result.map fst)
                  (Ok(Error Timeout))
                  "replays as the timeout"

          testCase "an untyped failure replays as the ExecutionFailed answered live; a pre-0.36 reason as before"
          <| fun _ ->
              let args = [ "year", Int 2026 ]

              let answer, _, journal =
                  QueryRegistry.dispatchCaptured
                      OpStream.defaultHash
                      QueryCodec.encodeResult
                      registered
                      "sales-by-region"
                      args
                      (fun _ -> Failed "down")
                      []

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
                  |> Result.map fst

              Expect.equal (replay journal) (Ok answer) "replayed exactly"

              // A journal written before 0.36.0 recorded the refusal's sentence.
              let sentence = QueryError.describe (ExecutionFailed("down", []))

              let _, _, legacy =
                  OpStream.captureEffectKeyed
                      OpStream.defaultHash
                      QueryCodec.encodeResult
                      (Query.determinismTag sampleQuery)
                      (Query.invocationKey sampleQuery args)
                      (fun () -> Some(Error sentence))
                      []

              Expect.equal
                  (replay legacy)
                  (Ok(Error(ExecutionFailed(sentence, []))))
                  "the recorded text, as it always was" ]
