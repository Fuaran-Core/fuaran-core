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
          Determinism = Network }
      Source = Ref "sales"
      TimeoutMs = Some 5000
      PageSize = Some 100 }

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
              let resolve (_: Query) = Ok Unchecked.defaultof<QueryResult>

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
                  Ok Unchecked.defaultof<QueryResult>

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
              Expect.notEqual k1 k3 "arg-sensitive" ]
