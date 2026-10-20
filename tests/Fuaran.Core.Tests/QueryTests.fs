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
      ResultSchema = [ Field.create "region" StringType; Field.create "revenue" FloatType ]
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
        { Schema = [ Field.create "region" StringType; Field.create "revenue" FloatType ]
          Columns =
            [ Column.ofStrs "region" (Vector.ofList [ "UK"; "US" ]) AllValid
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
                      { Schema = [ Field.create "region" StringType; Field.create "revenue" FloatType ]
                        Columns =
                          [ Column.ofStrs "region" (Vector.ofList [ "UK"; "US" ]) AllValid
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

              // Phase 462: on TEXT the parser refuses the repeated name before the query sees it, as
              // `UnreadableArgs` carrying the parse refusal; the two checks above answer a value.
              match QueryCodec.decodeArgs sampleQuery """{"year":2024,"year":2025}""" with
              | Error [ UnreadableArgs e ] ->
                  Expect.equal e.Code DecodeCode.InvalidJson "decodeArgs: the parser's refusal"
              | other -> failtestf "decodeArgs: expected UnreadableArgs, got %A" other

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
                  Expect.equal e.Expected "one of 'query'" "naming the one tag it reads"

                  Expect.equal
                      e.Message
                      (DecodeError.sentence DecodeCode.UnknownTag "one of 'query'" "value 'queryResult'")
                      "naming the tag, projected"
              | Ok q -> failtestf "another document type was read as a query: %A" q

              match QueryCodec.decodeDetailedWith ReadPolicy.Lenient (retag "query" None text) with
              | Error e -> Expect.equal e.Code DecodeCode.MissingField "an untagged document is refused"
              | Ok q -> failtestf "an untagged document was read as a query: %A" q

          testCase "a result document whose $type is not queryResult is a DecodeError naming the tag"
          <| fun _ ->
              let text = QueryCodec.encodeResult sampleResult
              Expect.equal (QueryCodec.decodeResult text) (Ok sampleResult) "round trip"

              match
                  QueryCodec.decodeResultDetailedWith ReadPolicy.Lenient (retag "queryResult" (Some "query") text)
              with
              | Error e ->
                  Expect.equal (e.Code, e.Path) (DecodeCode.UnknownTag, [ PathSegment.Key "$type" ]) "at the tag"

                  Expect.equal
                      e.Message
                      (DecodeError.sentence DecodeCode.UnknownTag "one of 'queryResult'" "value 'query'")
                      "naming the tag, projected"

                  Expect.equal
                      (QueryCodec.decodeResult (retag "queryResult" (Some "query") text))
                      (Error e.Message)
                      "another document type"
              | Ok r -> failtestf "another document type was read as a result: %A" r

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
                  Expect.equal e.Code DecodeCode.OutOfRange "a declaration the registry does not admit"

                  Expect.equal
                      e.Message
                      (DecodeError.sentence e.Code e.Expected (QueryError.describe registryRefusal))
                      "the reader's refusal is the registry's, projected"

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
                  ("Refused: the arguments could not be read at $: "
                   + DecodeError.sentence DecodeCode.InvalidJson "JSON text" "unexpected end"
                   + ". Send one JSON object keyed by parameter name.")
                  "described, around the refusal's projected sentence"

              // Phase 520: a refusal journalled before the sentence became a projection carries its
              // OLD sentence in `message`; it reads back verbatim — the same code, path, expectation
              // and sentence — so an append-once journal replays across the change.
              // The line is the bytes the 0.38.1-draft binary journalled for `decodeArgs sampleQuery "{"`;
              // the second is a sentence no projection writes, standing for any older spelling.
              for stored in
                  [ "not valid JSON: expected '\"' at position 1"
                    "a sentence some earlier release wrote" ] do
                  let legacy =
                      """{"$type":"unreadableArgs","error":{"code":"InvalidJson","expected":"JSON text","message":"""
                      + "\""
                      + stored.Replace("\"", "\\\"")
                      + "\""
                      + ""","path":[]}}"""

                  match QueryCodec.decodeQueryError legacy with
                  | Ok(UnreadableArgs old) ->
                      Expect.equal (old.Code, old.Path) (DecodeCode.InvalidJson, []) "the code and the path"
                      Expect.equal old.Message stored "the stored sentence, verbatim"

                      Expect.equal
                          (QueryCodec.encodeQueryError (UnreadableArgs old))
                          legacy
                          "and it re-encodes to its bytes"
                  | other -> failtestf "a pre-520 journal line did not read back: %A" other

              // and a parse failure journalled today is the same line the pre-520 binary wrote
              match QueryCodec.decodeArgs sampleQuery "{" with
              | Error [ e ] ->
                  Expect.equal
                      (QueryCodec.encodeQueryError e)
                      """{"$type":"unreadableArgs","error":{"code":"InvalidJson","expected":"JSON text","message":"not valid JSON: expected '\"' at position 1","path":[]}}"""
                      "the journalled bytes did not move"
              | other -> failtestf "expected one UnreadableArgs, got %A" other ]

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
                  KeyedCapture.captureEffectKeyed
                      StreamConfig.canonical
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
            [ Field.create "region" StringType
              Field.create "revenue" FloatType
              Field.create "day" DateType
              Field.create "amount" DecimalType
              Field.create "n" IntType
              Field.create "ok" BoolType
              Field.create "at" (TimestampType TimeUnit.Seconds) ]
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

          testCase "a literal of every column type round-trips with the bytes the column codec writes (Phase 514)"
          <| fun _ ->
              // One column of each of the ten types, an equality on each: the literal's bytes are the
              // column codec's one-cell writer's, and they read back through its one-cell reader.
              let literals =
                  [ IntType, Int -7, "-7"
                    FloatType, Float 1.25, "1.25"
                    BoolType, Bool true, "true"
                    StringType, Str "UK", "\"UK\""
                    DateType, Date "2026-01-01", "\"2026-01-01\""
                    TimestampType TimeUnit.Seconds, Timestamp "2026-01-01T00:00:01Z", "\"2026-01-01T00:00:01Z\""
                    DecimalType, Decimal "-0.05", "\"-0.05\""
                    TimestampType TimeUnit.Milliseconds,
                    Timestamp "2026-01-01T00:00:01.25Z",
                    "\"2026-01-01T00:00:01.25Z\""
                    TimestampType TimeUnit.Microseconds,
                    Timestamp "2026-01-01T00:00:01.000125Z",
                    "\"2026-01-01T00:00:01.000125Z\""
                    TimestampType TimeUnit.Nanoseconds,
                    Timestamp "2026-01-01T00:00:01.000000125Z",
                    "\"2026-01-01T00:00:01.000000125Z\"" ]


              Expect.equal
                  (literals |> List.map (fun (ty, _, _) -> ty))
                  ColumnType.all
                  "every column type, in ColumnType.all's order"

              let q =
                  { sampleQuery with
                      ResultSchema = literals |> List.mapi (fun i (ty, _, _) -> Field.create (sprintf "c%d" i) ty)
                      Where =
                          literals
                          |> List.mapi (fun i (_, cell, _) -> ColumnPredicate.EqualTo(sprintf "c%d" i, cell)) }

              let text = QueryCodec.encode q

              literals
              |> List.iteri (fun i (ty, _, bytes) ->
                  Expect.stringContains
                      text
                      (sprintf
                          "{\"$type\":\"equalTo\",\"column\":\"c%d\",\"type\":\"%s\",\"value\":%s}"
                          i
                          (ColumnType.tag ty)
                          bytes)
                      (ColumnType.tag ty))

              Expect.isOk (QueryRegistry.register q QueryRegistry.empty) "the registry admits every literal"
              Expect.equal (QueryCodec.decode text) (Ok q) "lenient"
              Expect.equal (QueryCodec.decodeWith ReadPolicy.Strict text) (Ok q) "strict"

          testCase
              "a declaration's result fields carry a unit, a label, a description and extensions through the codec and the registry (Phase 427)"
          <| fun _ ->
              let gbp =
                  match Unit.parse "[GBP]" with
                  | Ok u -> u
                  | Error e -> failtestf "[GBP] did not parse: %A" e

              let revenue =
                  Field.create "revenue" FloatType
                  |> Field.withUnit gbp
                  |> Field.withLabel "Revenue"
                  |> Field.withDescription "Net revenue in the period."
                  |> Field.withExt "vendor.format" "#,##0"

              let q =
                  { sampleQuery with
                      ResultSchema = [ Field.create "region" StringType; revenue ]
                      Where = [ ColumnPredicate.GreaterThan("revenue", Float 0.0) ] }

              let text = QueryCodec.encode q

              Expect.stringContains
                  text
                  "{\"description\":\"Net revenue in the period.\",\"ext\":{\"vendor.format\":\"#,##0\"},\"label\":\"Revenue\",\"name\":\"revenue\",\"type\":\"float\",\"unit\":\"[GBP]\"}"
                  "the result column is written as the column codec's schema entry"

              Expect.equal (QueryCodec.decode text) (Ok q) "and read back, members and all (the lenient reader)"

              Expect.equal
                  (QueryCodec.decodeDetailedWith ReadPolicy.Strict text)
                  (Ok q)
                  "the strict reader admits the four metadata members of a result column"

              Expect.isOk
                  (QueryRegistry.register q QueryRegistry.empty)
                  "the registry admits it, the literal held to the field's type"

              match
                  QueryRegistry.register
                      { q with
                          Where = [ ColumnPredicate.GreaterThan("revenue", Str "x") ] }
                      QueryRegistry.empty
              with
              | Error(PredicateTypeMismatch("revenue", FloatType, StringType)) -> ()
              | other -> failtestf "a literal of another type is refused against the field's type: %A" other

              let result =
                  { sampleResult with
                      Rows =
                          { sampleResult.Rows with
                              Schema = q.ResultSchema } }

              Expect.equal
                  (QueryCodec.decodeResult (QueryCodec.encodeResult result))
                  (Ok result)
                  "a result's rows carry the fields through the result codec"

          testCase "the registry refuses a filter or an order its result schema does not admit, by name"
          <| fun _ ->
              let declared = shaped.ResultSchema |> List.map _.Name
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

              // Phase 422: an instant's cell type is the coarsest unit that holds it, so a timestamp
              // literal fits a column at least as fine as its unit, and a finer literal is refused.
              let atMs =
                  { shaped with
                      ResultSchema =
                          shaped.ResultSchema
                          @ [ Field.create "atMs" (TimestampType TimeUnit.Milliseconds) ] }

              let register where =
                  QueryRegistry.register { atMs with Where = where } QueryRegistry.empty

              Expect.isOk
                  (register [ ColumnPredicate.AtLeast("atMs", Timestamp "2026-06-22T17:00:00Z") ])
                  "a whole-second literal in a millisecond column"

              Expect.isOk
                  (register [ ColumnPredicate.AtLeast("atMs", Timestamp "2026-06-22T17:00:00.5Z") ])
                  "a millisecond literal in a millisecond column"

              Expect.equal
                  (register [ ColumnPredicate.AtLeast("at", Timestamp "2026-06-22T17:00:00.5Z") ])
                  (Error(
                      PredicateTypeMismatch("at", TimestampType TimeUnit.Seconds, TimestampType TimeUnit.Milliseconds)
                  ))
                  "a millisecond literal in a seconds column"

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
              let declared = shaped.ResultSchema |> List.map _.Name

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

              Expect.equal retyped.Code DecodeCode.OutOfRange "a declaration the registry does not admit"

              Expect.equal
                  retyped.Message
                  (DecodeError.sentence
                      retyped.Code
                      retyped.Expected
                      (QueryError.describe (PredicateTypeMismatch("n", IntType, FloatType))))
                  "with the registry's sentence, projected"

              // A predicate naming an undeclared column.
              let stray =
                  refusedAt (
                      text.Replace("\"$type\":\"isNull\",\"column\":\"ok\"", "\"$type\":\"isNull\",\"column\":\"nope\"")
                  )

              Expect.equal stray.Path [ PathSegment.Key "where"; PathSegment.Index 6 ] "at the predicate"
              Expect.equal stray.Code DecodeCode.OutOfRange "a declaration the registry does not admit"

              Expect.equal
                  stray.Message
                  (DecodeError.sentence
                      stray.Code
                      stray.Expected
                      (QueryError.describe (UnknownColumn("nope", declared))))
                  "named, projected"

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

          testCase "the query keys' bytes are pinned: no journal moved when they came to share Hash.keyedBy"
          <| fun _ ->
              // Recorded from the build before Phase 510 routed both keys through `Hash.keyedBy`
              // (D173). A move here moves every journalled query capture: it is a breaking change to
              // the journal, not a refactor.
              let every =
                  [ "s", Str "a\u0001b\u0010c"
                    "i", Int 7
                    "f", Float 1.5
                    "b", Bool true
                    "d", Date "2026-01-01"
                    "t", Timestamp "2026-01-01T00:00:00Z"
                    "m", Decimal "12.50"
                    "n", Null ]

              Expect.equal
                  (Query.invocationKey sampleQuery [ "year", Int 2026; "region", Str "UK" ])
                  "sales-by-region#cfb323a8"
                  "unshaped"

              Expect.equal (Query.invocationKey shaped shapedArgs) "shaped#05ad7cda" "filtered and ordered"

              Expect.equal
                  (Query.invocationKeyPage shaped shapedArgs (Some "tok-1"))
                  "shaped#f968c101"
                  "a page of a shaped declaration"

              Expect.equal
                  (Query.invocationKeyPage sampleQuery every (Some "p\u0001"))
                  "sales-by-region#78d1824c"
                  "every cell kind, behind a token that spells the separator"

              Expect.equal (Query.invocationKey sampleQuery every) "sales-by-region#71c4e9e0" "every cell kind"

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

// ---------------------------------------------------------------------------
// Phase 509 — the decoder helpers the seam codecs share are Wire's: `Decoder.oneOfWith` (each codec
// wrote it as `tagged`), `Decoder.pair` and `Decoder.strings`. The refusal a codec's closed-set read
// answers is pinned field for field, so the move re-homed bodies and moved no sentence.
// ---------------------------------------------------------------------------

[<Tests>]
let decoderHelperTests =
    testList
        "Decoder helpers (Phase 509)"
        [ testCase "oneOf answers UnknownTag at the value, naming the known spellings (Phase 520: oneOfWith is gone)"
          <| fun _ ->
              let d = Decoder.oneOf [ "int", 1; "string", 2 ]
              Expect.equal (d (JStr "string")) (Ok 2) "a known value"

              Expect.equal
                  (d (JStr "blob"))
                  (Error(DecodeError.make DecodeCode.UnknownTag "one of 'int', 'string'" "value 'blob'"))
                  "a miss is UnknownTag at the value, its sentence the code's projection"

              Expect.equal
                  (d (JInt 3) |> Result.mapError _.Code)
                  (Error DecodeCode.WrongKind)
                  "a value that is not a string is WrongKind"

          testCase "pair reads both members of one object, the first refusal first; strings reads a string array"
          <| fun _ ->
              let doc = JObj [ "id", JStr "q"; "known", JArr [ JStr "a"; JStr "b" ] ]

              let d =
                  Decoder.pair (Decoder.field "id" Decoder.str) (Decoder.field "known" Decoder.strings) (fun i k ->
                      i, k)

              Expect.equal (d doc) (Ok("q", [ "a"; "b" ])) "both members"

              Expect.equal
                  (d (JObj [ "known", JInt 1 ]) |> Result.mapError _.Path)
                  (Error [ PathSegment.Key "id" ])
                  "the first decoder's refusal is answered without the second"

              Expect.equal
                  (Decoder.strings (JArr [ JStr "a"; JInt 1 ])
                   |> Result.mapError (fun e -> e.Code, e.Path))
                  (Error(DecodeCode.WrongKind, [ PathSegment.Index 1 ]))
                  "an item that is not a string, under its index" ]

// ---------------------------------------------------------------------------
// Phase 528 - the query-side seam baselines, and defect 6's tag-naming guard. As on the capability
// side (`FunctionTests.seamBaselineTests528`), every case is pinned by its exact bytes and every reader
// by the refusal it answers, so the derived codecs (fuaran-core#551) are held to what is already
// written.
// ---------------------------------------------------------------------------

/// The decode refusal `UnreadableArgs` carries in its baseline.
let private unreadable528 =
    DecodeError.under (PathSegment.Key "year") (DecodeError.make DecodeCode.WrongKind "int" "array")

/// Every `QueryError` case (and, through `predicateNotHonoured`, every `ColumnPredicate` case) with
/// the exact document `QueryCodec.encodeQueryError` writes for it.
let internal queryBaselines528: (QueryError * string) list =
    [ NoSuchQuery("x", [ "a"; "b" ]), "{\"$type\":\"noSuchQuery\",\"id\":\"x\",\"known\":[\"a\",\"b\"]}"
      DuplicateQuery "x", "{\"$type\":\"duplicateQuery\",\"id\":\"x\"}"
      UnknownParam("p", [ "a" ]), "{\"$type\":\"unknownParam\",\"declared\":[\"a\"],\"name\":\"p\"}"
      ParamTypeMismatch("p", IntType, StringType),
      "{\"$type\":\"paramTypeMismatch\",\"expected\":\"int\",\"got\":\"string\",\"name\":\"p\"}"
      RequiredParamsUnbound [ "a"; "b" ], "{\"$type\":\"requiredParamsUnbound\",\"names\":[\"a\",\"b\"]}"
      SourceNotResolved "s", "{\"$type\":\"sourceNotResolved\",\"ref\":\"s\"}"
      ExecutionFailed("d", [ "a" ]), "{\"$type\":\"executionFailed\",\"detail\":\"d\",\"recoverable\":[\"a\"]}"
      Timeout, "{\"$type\":\"timeout\"}"
      RequiredParamsNull [ "a" ], "{\"$type\":\"requiredParamsNull\",\"names\":[\"a\"]}"
      DuplicateParam "a", "{\"$type\":\"duplicateParam\",\"name\":\"a\"}"
      QueryPolicyRefused("p", "r", [ "a" ]),
      "{\"$type\":\"queryPolicyRefused\",\"allowed\":[\"a\"],\"policy\":\"p\",\"reason\":\"r\"}"
      QueryApprovalRequired "p", "{\"$type\":\"queryApprovalRequired\",\"policy\":\"p\"}"
      UnreadableArgs unreadable528,
      "{\"$type\":\"unreadableArgs\",\"error\":{\"code\":\"WrongKind\",\"expected\":\"int\",\"message\":\"expected int, got array\",\"path\":[\"year\"]}}"
      UnknownColumn("c", [ "a" ]), "{\"$type\":\"unknownColumn\",\"declared\":[\"a\"],\"name\":\"c\"}"
      PredicateTypeMismatch("c", IntType, (TimestampType TimeUnit.Milliseconds)),
      "{\"$type\":\"predicateTypeMismatch\",\"column\":\"c\",\"expected\":\"int\",\"got\":\"timestamp_ms\"}"
      PredicateNotApplicable("contains", "c", DecimalType),
      "{\"$type\":\"predicateNotApplicable\",\"column\":\"c\",\"columnType\":\"decimal\",\"predicate\":\"contains\"}"
      IllFormedLiteral("c", "why"), "{\"$type\":\"illFormedLiteral\",\"column\":\"c\",\"reason\":\"why\"}"
      DuplicateSortColumn "c", "{\"$type\":\"duplicateSortColumn\",\"column\":\"c\"}"
      PredicateNotHonoured(ColumnPredicate.EqualTo("c", Int 1)),
      "{\"$type\":\"predicateNotHonoured\",\"predicate\":{\"$type\":\"equalTo\",\"column\":\"c\",\"type\":\"int\",\"value\":1}}"
      PredicateNotHonoured(ColumnPredicate.GreaterThan("c", Float 1.5)),
      "{\"$type\":\"predicateNotHonoured\",\"predicate\":{\"$type\":\"greaterThan\",\"column\":\"c\",\"type\":\"float\",\"value\":1.5}}"
      PredicateNotHonoured(ColumnPredicate.AtLeast("c", Date "2026-10-20")),
      "{\"$type\":\"predicateNotHonoured\",\"predicate\":{\"$type\":\"atLeast\",\"column\":\"c\",\"type\":\"date\",\"value\":\"2026-10-20\"}}"
      PredicateNotHonoured(ColumnPredicate.LessThan("c", Str "m")),
      "{\"$type\":\"predicateNotHonoured\",\"predicate\":{\"$type\":\"lessThan\",\"column\":\"c\",\"type\":\"string\",\"value\":\"m\"}}"
      PredicateNotHonoured(ColumnPredicate.AtMost("c", Bool true)),
      "{\"$type\":\"predicateNotHonoured\",\"predicate\":{\"$type\":\"atMost\",\"column\":\"c\",\"type\":\"bool\",\"value\":true}}"
      PredicateNotHonoured(ColumnPredicate.Contains("c", "t")),
      "{\"$type\":\"predicateNotHonoured\",\"predicate\":{\"$type\":\"contains\",\"column\":\"c\",\"text\":\"t\"}}"
      PredicateNotHonoured(ColumnPredicate.IsNull "c"),
      "{\"$type\":\"predicateNotHonoured\",\"predicate\":{\"$type\":\"isNull\",\"column\":\"c\"}}"
      PredicateNotHonoured(ColumnPredicate.IsNotNull "c"),
      "{\"$type\":\"predicateNotHonoured\",\"predicate\":{\"$type\":\"isNotNull\",\"column\":\"c\"}}"
      OrderNotHonoured "c", "{\"$type\":\"orderNotHonoured\",\"column\":\"c\"}" ]

/// What `QueryCodec.decodeQueryError` answers for malformed documents, sentence for sentence.
let private queryRefusals528: (string * string) list =
    [ "[]", "expected object, got array"
      "{}", "missing property: $type"
      "{\"$type\":\"nope\"}",
      "unknown $type 'nope'; expected one of 'noSuchQuery', 'duplicateQuery', 'unknownParam', 'paramTypeMismatch', 'requiredParamsUnbound', 'sourceNotResolved', 'executionFailed', 'timeout', 'requiredParamsNull', 'duplicateParam', 'queryPolicyRefused', 'queryApprovalRequired', 'unreadableArgs', 'unknownColumn', 'predicateTypeMismatch', 'predicateNotApplicable', 'illFormedLiteral', 'duplicateSortColumn', 'predicateNotHonoured', 'orderNotHonoured'"
      "{\"$type\":\"policyRefused\",\"allowed\":[],\"policy\":\"p\",\"reason\":\"r\"}",
      "unknown $type 'policyRefused'; expected one of 'noSuchQuery', 'duplicateQuery', 'unknownParam', 'paramTypeMismatch', 'requiredParamsUnbound', 'sourceNotResolved', 'executionFailed', 'timeout', 'requiredParamsNull', 'duplicateParam', 'queryPolicyRefused', 'queryApprovalRequired', 'unreadableArgs', 'unknownColumn', 'predicateTypeMismatch', 'predicateNotApplicable', 'illFormedLiteral', 'duplicateSortColumn', 'predicateNotHonoured', 'orderNotHonoured'"
      "{\"$type\":\"paramTypeMismatch\",\"name\":\"p\",\"expected\":\"int\",\"got\":\"blob\"}",
      "unknown value 'blob'; expected one of 'int', 'float', 'bool', 'string', 'date', 'timestamp', 'decimal', 'timestamp_ms', 'timestamp_us', 'timestamp_ns'"
      "{\"$type\":\"predicateNotApplicable\",\"predicate\":\"like\",\"column\":\"c\",\"columnType\":\"string\"}",
      "unknown value 'like'; expected one of 'equalTo', 'greaterThan', 'atLeast', 'lessThan', 'atMost', 'contains', 'isNull', 'isNotNull'"
      "{\"$type\":\"unreadableArgs\",\"error\":{\"code\":\"Nope\",\"path\":[],\"expected\":\"e\",\"message\":\"m\"}}",
      "unknown value 'Nope'; expected one of 'InvalidJson', 'MissingField', 'WrongKind', 'UnknownTag', 'OutOfRange', 'UndeclaredMember', 'LimitExceeded', 'NotAdmitted', 'SchemaFault'"
      "{\"$type\":\"unreadableArgs\",\"error\":{\"code\":\"WrongKind\",\"path\":{},\"expected\":\"e\",\"message\":\"m\"}}",
      "expected an array of member names and item indices, got not a decode path"
      "{\"$type\":\"predicateNotHonoured\",\"predicate\":{\"$type\":\"equalTo\",\"column\":\"c\",\"type\":\"int\",\"value\":\"x\"}}",
      "out of range: the filter on column 'c' compares with a value a int column cannot carry; expected a int value"
      "{\"$type\":\"predicateNotHonoured\",\"predicate\":{\"$type\":\"between\",\"column\":\"c\"}}",
      "unknown $type 'between'; expected one of 'equalTo', 'greaterThan', 'atLeast', 'lessThan', 'atMost', 'contains', 'isNull', 'isNotNull'"
      "{\"$type\":\"executionFailed\",\"detail\":\"d\",\"recoverable\":[1]}", "expected string, got int" ]

/// A declaration carrying a `Where` (three predicate shapes) and an `OrderBy` (both directions).
let private shapeHost528 =
    "{\"$type\":\"query\",\"effect\":{\"determinism\":\"network\",\"host\":\"readsHost\"},\"id\":\"q\",\"orderBy\":[{\"column\":\"revenue\",\"direction\":\"descending\"},{\"column\":\"region\",\"direction\":\"ascending\"}],\"params\":[{\"name\":\"year\",\"required\":true,\"type\":\"int\"}],\"resultSchema\":[{\"name\":\"region\",\"type\":\"string\"},{\"name\":\"revenue\",\"type\":\"float\"}],\"source\":{\"ref\":\"s\",\"schema\":[]},\"where\":[{\"$type\":\"contains\",\"column\":\"region\",\"text\":\"U\"},{\"$type\":\"atLeast\",\"column\":\"revenue\",\"type\":\"float\",\"value\":1.5},{\"$type\":\"isNotNull\",\"column\":\"region\"}]}"

/// A replacement planted into `shapeHost528`, then the typed refusal under `Lenient` and `Strict`
/// (`DecodeError.render`: code, path, sentence; "OK" for an accepted document).
let private shapeRefusals528: (string * string * string * string) list =
    [ "\"$type\":\"contains\"",
      "\"$type\":\"like\"",
      "UnknownTag at $[\"where\"][0][\"$type\"]: unknown $type 'like'; expected one of 'equalTo', 'greaterThan', 'atLeast', 'lessThan', 'atMost', 'contains', 'isNull', 'isNotNull'",
      // Phase 551: a tag the union does not declare has no roster; the strict read answers as the lenient.
      "UnknownTag at $[\"where\"][0][\"$type\"]: unknown $type 'like'; expected one of 'equalTo', 'greaterThan', 'atLeast', 'lessThan', 'atMost', 'contains', 'isNull', 'isNotNull'"
      "\"text\":\"U\"",
      "\"text\":1",
      "WrongKind at $[\"where\"][0][\"text\"]: expected string, got int",
      "WrongKind at $[\"where\"][0][\"text\"]: expected string, got int"
      "\"value\":1.5",
      "\"value\":\"x\"",
      "OutOfRange at $[\"where\"][1][\"value\"]: out of range: the filter on column 'revenue' compares with a value a float column cannot carry; expected a float value",
      "OutOfRange at $[\"where\"][1][\"value\"]: out of range: the filter on column 'revenue' compares with a value a float column cannot carry; expected a float value"
      "\"type\":\"float\",\"value\"",
      "\"type\":\"blob\",\"value\"",
      "UnknownTag at $[\"where\"][1][\"type\"]: unknown value 'blob'; expected one of 'int', 'float', 'bool', 'string', 'date', 'timestamp', 'decimal', 'timestamp_ms', 'timestamp_us', 'timestamp_ns'",
      "UnknownTag at $[\"where\"][1][\"type\"]: unknown value 'blob'; expected one of 'int', 'float', 'bool', 'string', 'date', 'timestamp', 'decimal', 'timestamp_ms', 'timestamp_us', 'timestamp_ns'"
      "\"direction\":\"descending\"",
      "\"direction\":\"down\"",
      "UnknownTag at $[\"orderBy\"][0][\"direction\"]: unknown case 'down' of enum 'SortDirection'; expected one of 'ascending', 'descending'",
      "UnknownTag at $[\"orderBy\"][0][\"direction\"]: unknown case 'down' of enum 'SortDirection'; expected one of 'ascending', 'descending'"
      "{\"column\":\"revenue\",\"direction\":\"descending\"}",
      "{\"direction\":\"descending\"}",
      "MissingField at $[\"orderBy\"][0][\"column\"]: missing property: column",
      "MissingField at $[\"orderBy\"][0][\"column\"]: missing property: column"
      "\"$type\":\"isNotNull\"",
      "\"$type\":\"isNotNull\",\"x\":1",
      "OK",
      "UndeclaredMember at $[\"where\"][2][\"x\"]: undeclared member: 'x'; expected one of the members '$type', 'column'"
      "\"direction\":\"ascending\"",
      "\"direction\":\"ascending\",\"x\":1",
      "OK",
      "UndeclaredMember at $[\"orderBy\"][1][\"x\"]: undeclared member: 'x'; expected one of the members 'column', 'direction'" ]

/// The keyed capture journal a completion (`year = 2026`) and a resolver failure (`year = 2027`)
/// write on the query seam.
let private queryJournal528: string list =
    [ "0|q#e4e409c2|0|network|Attempted|null||71abe50a"
      "1|q#e4e409c2|0|network|Completed|{\"$type\":\"queryResult\",\"pageNum\":0,\"rows\":{\"columns\":{\"region\":{\"validity\":[true],\"values\":[\"UK\"]},\"revenue\":{\"validity\":[true],\"values\":[2.5]}},\"schema\":[{\"name\":\"region\",\"type\":\"string\"},{\"name\":\"revenue\",\"type\":\"float\"}]},\"totalRowCount\":1}|71abe50a|31ae0b02"
      "2|q#c0e1927f|0|network|Attempted|null|31ae0b02|d3353e65"
      "3|q#c0e1927f|0|network|Refused|\"{\\\"$type\\\":\\\"executionFailed\\\",\\\"detail\\\":\\\"down\\\",\\\"recoverable\\\":[]}\"|d3353e65|0b5b016b" ]

let private shapeQuery528: Query =
    { Id = "q"
      Params =
        [ { Name = "year"
            Type = IntType
            Required = true } ]
      ResultSchema = [ Field.create "region" StringType; Field.create "revenue" FloatType ]
      Effect =
        { Host = ReadsHost
          Determinism = Effect.network }
      Source = Ref "s"
      TimeoutMs = None
      PageSize = None
      Where =
        [ ColumnPredicate.Contains("region", "U")
          ColumnPredicate.AtLeast("revenue", Float 1.5)
          ColumnPredicate.IsNotNull "region" ]
      OrderBy =
        [ { Column = "revenue"
            Direction = SortDirection.Descending }
          { Column = "region"
            Direction = SortDirection.Ascending } ] }

let private journalQuery528 =
    { shapeQuery528 with
        Where = []
        OrderBy = [] }

let private journalRows528: QueryResult =
    { Rows =
        { Schema = journalQuery528.ResultSchema
          Columns =
            [ Column.ofStrs "region" (Vector.ofList [ "UK" ]) AllValid
              Column.ofFloats "revenue" (Vector.ofList [ 2.5 ]) AllValid ] }
      PageNum = 0
      TotalRowCount = Some 1
      NextPageToken = None }

// ---- defect 6: the tag-naming rule (D163), held over the refusal unions ----

/// D163's rule: a seam refusal case's `$type` is its case name with the first letter lowered, so a
/// case-name prefix given to keep two seams' cases apart is carried onto the wire.
let internal tagOf528 (caseName: string) : string =
    string (System.Char.ToLowerInvariant caseName[0]) + caseName.Substring 1

/// Reflection over a union whatever its accessibility (the planted union below is private).
let private anyAccess528 =
    System.Reflection.BindingFlags.Public
    ||| System.Reflection.BindingFlags.NonPublic

/// The case names of a union type, in declaration order.
let private caseNames528 (t: System.Type) : string list =
    Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(t, anyAccess528)
    |> Array.map _.Name
    |> List.ofArray

/// The case name of a union value.
let private caseOf528 (v: obj) : string =
    let case, _ =
        Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(v, v.GetType(), anyAccess528)

    case.Name

/// The `$type` a document leads with.
let private docTag528 (doc: string) : string =
    match Json.parse doc with
    | Ok(JObj ms) ->
        match ms |> List.tryFind (fun (k, _) -> k = "$type") with
        | Some(_, JStr t) -> t
        | _ -> ""
    | _ -> ""

/// Every pinned `(value, document)` whose `$type` is not `tagOf528` of the value's case name.
let internal namingViolations528 (rows: (obj * string) list) : (string * string) list =
    rows
    |> List.choose (fun (v, doc) ->
        let case = caseOf528 v

        if docTag528 doc = tagOf528 case then
            None
        else
            Some(case, docTag528 doc))

/// Every tag two of the named unions share, as `(tag, first union, second union)`. A union's tags
/// are DERIVED from its case names by the rule, so two seams can share a tag only by sharing a case
/// name, which D101 already keeps apart.
let internal sharedTags528 (unions: (string * System.Type) list) : (string * string * string) list =
    let tagged =
        unions |> List.map (fun (name, t) -> name, caseNames528 t |> List.map tagOf528)

    [ for i, (a, ta) in List.indexed tagged do
          for b, tb in List.skip (i + 1) tagged do
              for t in ta do
                  if List.contains t tb then
                      yield t, a, b ]

/// The seams' refusal unions: no two of them may share a `$type` tag (D163).
let private refusalUnions528: (string * System.Type) list =
    [ "InvokeError", typeof<InvokeError>
      "QueryError", typeof<QueryError>
      "DeclarationFault", typeof<DeclarationFault>
      "Rejection", typeof<Rejection<string>> ]

/// A planted seam refusal union that re-uses the capability seam's case names: the collision
/// fuaran-core#497 removed by hand, re-introduced to show the guard reds on it.
[<RequireQualifiedAccess>]
type private PlantedSeamError528 =
    | PolicyRefused of policy: string
    | ApprovalRequired of policy: string

[<Tests>]
let seamBaselineTests528 =
    testList
        "Phase 528 - query-side seam baselines and the tag-naming guard"
        [ testCase "every QueryError and ColumnPredicate case writes its pinned bytes and reads back"
          <| fun _ ->
              for e, doc in queryBaselines528 do
                  Expect.equal (QueryCodec.encodeQueryError e) doc (sprintf "%A" e)
                  Expect.equal (QueryCodec.decodeQueryError doc) (Ok e) doc

          testCase "the query-error reader answers each malformed document as pinned"
          <| fun _ ->
              for doc, refusal in queryRefusals528 do
                  Expect.equal (QueryCodec.decodeQueryError doc) (Error refusal) doc

          testCase "a declaration's Where and OrderBy write their pinned bytes and read back"
          <| fun _ ->
              Expect.equal (QueryCodec.encode shapeQuery528) shapeHost528 "bytes"
              Expect.equal (QueryCodec.decode shapeHost528) (Ok shapeQuery528) "round trip"

          testCase "a planted predicate or sort key is refused with its pinned code and path, under both policies"
          <| fun _ ->
              let render policy doc =
                  match QueryCodec.decodeDetailedWith policy doc with
                  | Ok _ -> "OK"
                  | Error e -> DecodeError.render e

              for from, into, lenient, strict in shapeRefusals528 do
                  let doc = shapeHost528.Replace(from, into)
                  Expect.notEqual doc shapeHost528 ("the plant landed: " + from)
                  Expect.equal (render ReadPolicy.Lenient doc) lenient ("lenient " + into)
                  Expect.equal (render ReadPolicy.Strict doc) strict ("strict " + into)

          testCase "Phase 551: under Strict an undeclared predicate member loses to a fault read before it"
          <| fun _ ->
              // A predicate's members are policed by its derived strict reader where it is read, after the
              // declaration's `params`; so a document wrong at both is refused at the parameter.
              let doc =
                  shapeHost528
                      .Replace("\"required\":true,\"type\":\"int\"", "\"required\":true,\"type\":\"blob\"")
                      .Replace("\"$type\":\"isNotNull\"", "\"$type\":\"isNotNull\",\"x\":1")

              Expect.notEqual doc shapeHost528 "the plants landed"

              Expect.equal
                  (QueryCodec.decodeDetailedWith ReadPolicy.Strict doc
                   |> Result.mapError DecodeError.render)
                  (Error
                      "UnknownTag at $[\"params\"][0][\"type\"]: unknown value 'blob'; expected one of 'int', 'float', 'bool', 'string', 'date', 'timestamp', 'decimal', 'timestamp_ms', 'timestamp_us', 'timestamp_ns'")
                  "the earlier-read fault wins"

          testCase "the query seam's captured journal is pinned, and a pinned journal replays to the same answers"
          <| fun _ ->
              let reg =
                  QueryRegistry.register journalQuery528 QueryRegistry.empty
                  |> Result.toOption
                  |> Option.get

              let dispatch year resolve journal =
                  (QueryRegistry.dispatchCaptured
                      OpStream.defaultHash
                      QueryCodec.encodeResult
                      reg
                      "q"
                      [ "year", Int year ]
                      resolve
                      journal)
                      .Journal

              let journal =
                  []
                  |> dispatch 2026 (fun _ -> Ready journalRows528)
                  |> dispatch 2027 (fun _ -> Failed "down")

              Expect.equal (FunctionTests.journalLines528 journal) queryJournal528 "the journal, byte for byte"

              let pinned = FunctionTests.journalOfLines528 queryJournal528

              let replay year =
                  (QueryRegistry.dispatchReplayed
                      QueryCodec.decodeResult
                      reg
                      "q"
                      [ "year", Int year ]
                      None
                      (fun _ _ -> Failed "live")
                      Map.empty
                      pinned)
                      .Outcome

              Expect.equal (replay 2026) (Ok(Ready journalRows528)) "the completion"

              Expect.equal
                  (replay 2027)
                  (Error(ReplayFailure.Refused(ExecutionFailed("down", []))))
                  "the recorded refusal"

          testCase "every refusal union's cases are pinned, and every pinned tag is its case name lowered (D163)"
          <| fun _ ->
              let pinned (rows: ('T * string) list) =
                  rows |> List.map (fun (v, doc) -> box v, doc)

              let faults =
                  FunctionTests.invokeBaselines528
                  |> List.choose (fun (e, doc) ->
                      match e, Json.parse doc with
                      | IllFormedCapability(_, f), Ok(JObj ms) ->
                          ms
                          |> List.tryPick (fun (k, v) ->
                              if k = "fault" then
                                  Some(box f, Canonical.write Rendering.CrossHost v)
                              else
                                  None)
                      | _ -> None)

              let predicates =
                  queryBaselines528
                  |> List.choose (fun (e, doc) ->
                      match e, Json.parse doc with
                      | PredicateNotHonoured p, Ok(JObj ms) ->
                          ms
                          |> List.tryPick (fun (k, v) ->
                              if k = "predicate" then
                                  Some(box p, Canonical.write Rendering.CrossHost v)
                              else
                                  None)
                      | _ -> None)

              for name, t, rows in
                  [ "InvokeError", typeof<InvokeError>, pinned FunctionTests.invokeBaselines528
                    "QueryError", typeof<QueryError>, pinned queryBaselines528
                    "DeclarationFault", typeof<DeclarationFault>, faults
                    "Rejection", typeof<Rejection<string>>, pinned FunctionTests.rejectionBaselines528
                    "ColumnPredicate", typeof<ColumnPredicate>, predicates ] do
                  Expect.equal
                      (rows |> List.map (fst >> caseOf528) |> List.distinct |> List.sort)
                      (caseNames528 t |> List.sort)
                      (name + ": a case added without a pinned document")

                  Expect.isEmpty (namingViolations528 rows) (name + ": a tag that is not its case name lowered")

          testCase "no two seam refusal unions share a $type tag"
          <| fun _ -> Expect.isEmpty (sharedTags528 refusalUnions528) "the refusal unions' tags are disjoint"

          // The guard shown red on the two plants it exists to catch, then restored: the pre-497
          // spelling (the query seam writing the capability seam's `policyRefused`) breaks the naming
          // rule, and a seam whose cases re-use the capability seam's names collides by construction.
          testCase "a planted collision and a planted mis-named tag are both refused"
          <| fun _ ->
              Expect.equal
                  (namingViolations528
                      [ box (QueryPolicyRefused("p", "r", [])),
                        "{\"$type\":\"policyRefused\",\"allowed\":[],\"policy\":\"p\",\"reason\":\"r\"}" ])
                  [ "QueryPolicyRefused", "policyRefused" ]
                  "the pre-497 tag breaks the rule"

              Expect.equal
                  (sharedTags528 (refusalUnions528 @ [ "PlantedSeamError", typeof<PlantedSeamError528> ]))
                  [ "policyRefused", "InvokeError", "PlantedSeamError"
                    "approvalRequired", "InvokeError", "PlantedSeamError" ]
                  "a planted seam re-using the capability seam's case names collides" ]
