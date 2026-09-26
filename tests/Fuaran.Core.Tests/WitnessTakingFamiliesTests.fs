module Fuaran.Core.Tests.WitnessTakingFamiliesTests

// Phase 246 — the witness-taking law families. The seam families (`capabilityLaws`, `queryLaws`,
// `capabilityPipelineLaws`) take a seed and certify Core's own fixtures, so they cannot see a
// domain's registry, body or host. Downstream consumers' measurements (Phase 246) planted two
// defects that passed them: a host that ran the body before the registry refused, and a policy that
// allowed everything. This file reproduces each planted defect at a small reference domain and
// shows the pair of results that is the phase's evidence — the witness-taking family goes RED on
// the defect, and the fixture-bound family beside it stays green, because it cannot see it.
//
// The reference witnesses here are also what `ConformanceVacuityTests` runs the new families over
// for the roster's `cases` column, which is why this file compiles before it.

open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------
//  the capability seam: three capabilities, one per outcome a body gives
// ---------------------------------------------------------------------------

let private hole (addr: string) (space: ValueSpace) : SigEntry =
    { Addr = addr
      Name = addr
      Kind = "value"
      Space = Some space
      Slot = None
      Action = None
      Required = true }

let private capability (id: string) (holes: SigEntry list) : Capability =
    Capability.create
        id
        { Name = id
          Holes = holes
          Effect = Effect.pureDeterministic }
        Server

/// `echo` settles with its argument, `later` stays pending, `flaky` fails for `flaky/k = 0`.
let capabilityRegistry: CapabilityRegistry =
    [ capability "echo" [ hole "echo/n" (IntRange(0, 10)) ]
      capability "later" [ hole "later/x" (Enum [ "a"; "b" ]) ]
      capability "flaky" [ hole "flaky/k" (IntRange(0, 3)) ] ]
    |> List.fold (fun r c -> r |> Result.bind (Registry.register c)) (Ok Registry.empty)
    |> Result.defaultWith (fun e -> failwithf "the reference registry did not build: %A" e)

let private argOf (args: (string * string) list) (addr: string) =
    args |> List.tryPick (fun (a, v) -> if a = addr then Some v else None)

let capabilityBody (args: (string * string) list) (c: Capability) () : Deferred<string> =
    match c.Id with
    | "echo" -> Ready(argOf args "echo/n" |> Option.defaultValue "?")
    | "later" -> Pending
    | _ ->
        match argOf args "flaky/k" with
        | Some "0" -> Failed "flaky refused k=0"
        | k -> Ready(sprintf "flaky %A" k)

/// Calls a model could make: a declared id or an invented one, each argument in space, out of
/// space or left out, and a stray argument now and then.
let genCapabilityCall (rng: ConfRng.T) : (string * (string * string) list) * ConfRng.T =
    let id, r1 = ConfRng.choose [ "echo"; "later"; "flaky"; "no-such-tool" ] rng

    let holes =
        Registry.tryFind id capabilityRegistry
        |> Option.map (fun c -> c.Signature.Holes)
        |> Option.defaultValue []

    let args, r2 =
        holes
        |> List.fold
            (fun (acc, r) h ->
                let k, r' = ConfRng.intBelow 6 r

                match k, h.Space with
                | 0, _ -> acc, r'
                | 1, _ -> acc @ [ h.Addr, "out-of-space" ], r'
                | _, Some(Enum vs) ->
                    let v, r'' = ConfRng.choose vs r'
                    acc @ [ h.Addr, v ], r''
                | _, Some(IntRange(lo, hi)) ->
                    let v, r'' = ConfRng.intBelow (hi - lo + 1) r'
                    acc @ [ h.Addr, string (lo + v) ], r''
                | _ -> acc, r')
            ([], r1)

    let stray, r3 = ConfRng.intBelow 8 r2
    let args = if stray = 0 then args @ [ id + "/stray", "1" ] else args
    (id, args), r3

let capabilityWitness: CapabilitySeamWitness<string> =
    { Registry = capabilityRegistry
      Body = capabilityBody
      Dispatch = Registry.dispatch capabilityRegistry
      GenCall = genCapabilityCall }

/// The planted defect: a host that runs the body BEFORE the registry has refused. Every call to a
/// registered id runs it — so a refused call with a bad argument still did the work, the shape of a
/// refused write that still wrote.
let private bodyBeforeRefusal
    (id: string)
    (args: (string * string) list)
    (body: Capability -> unit -> Deferred<string>)
    : Result<Deferred<string>, InvokeError> =
    match Registry.tryFind id capabilityRegistry with
    | Some c ->
        let early = body c ()
        Registry.dispatch capabilityRegistry id args (fun _ () -> early)
    | None -> Registry.dispatch capabilityRegistry id args body

// ---------------------------------------------------------------------------
//  the query seam
// ---------------------------------------------------------------------------

let private readingsQuery: Query =
    { Id = "readings"
      Params =
        [ { Name = "station"
            Type = StringType
            Required = true }
          { Name = "limit"
            Type = IntType
            Required = false } ]
      ResultSchema = [ "v", IntType ]
      Effect =
        { Host = ReadsHost
          Determinism = Network }
      Source = Ref "readings"
      TimeoutMs = Some 5000
      PageSize = None }

let queryRegistry: QueryRegistry =
    QueryRegistry.empty
    |> QueryRegistry.register readingsQuery
    |> Result.defaultWith (fun e -> failwithf "the reference query registry did not build: %A" e)

/// `harbour` settles, `ridge` is in flight, `offline` fails.
let queryResolver (args: (string * Cell) list) (_: Query) : Deferred<QueryResult> =
    match args |> List.tryPick (fun (n, c) -> if n = "station" then Some c else None) with
    | Some(Str "ridge") -> Pending
    | Some(Str "offline") -> Failed "station offline"
    | _ ->
        Ready
            { Rows =
                { Schema = [ "v", IntType ]
                  Columns = [ Column.create "v" IntType [ Int 11 ] ] }
              PageNum = 0
              TotalRowCount = Some 1
              NextPageToken = None }

let genQueryCall (rng: ConfRng.T) : (string * (string * Cell) list) * ConfRng.T =
    let id, r1 = ConfRng.choose [ "readings"; "readings"; "raw-dump" ] rng
    let st, r2 = ConfRng.choose [ Str "harbour"; Str "ridge"; Str "offline"; Int 3 ] r1
    let lim, r3 = ConfRng.choose [ Some(Int 2); Some(Str "ten"); None ] r2
    let drop, r4 = ConfRng.intBelow 6 r3

    let args =
        (if drop = 0 then [] else [ "station", st ])
        @ (match lim with
           | Some l -> [ "limit", l ]
           | None -> [])

    let stray, r5 = ConfRng.intBelow 8 r4
    let args = if stray = 0 then args @ [ "depth", Int 1 ] else args
    (id, args), r5

let queryWitness: QuerySeamWitness =
    { Queries = queryRegistry
      Resolver = queryResolver
      Dispatch = QueryRegistry.dispatch queryRegistry
      GenQuery = genQueryCall }

let private resolverBeforeRefusal
    (id: string)
    (args: (string * Cell) list)
    (resolve: Query -> Deferred<QueryResult>)
    : Result<Deferred<QueryResult>, QueryError> =
    match QueryRegistry.tryFind id queryRegistry with
    | Some q ->
        let early = resolve q
        QueryRegistry.dispatch queryRegistry id args (fun _ -> early)
    | None -> QueryRegistry.dispatch queryRegistry id args resolve

// ---------------------------------------------------------------------------
//  the capability pipeline
// ---------------------------------------------------------------------------

/// A source fed into `echo`, then a literal-fed `echo` beside it.
let genPipeline (rng: ConfRng.T) : CapabilityPipeline * ConfRng.T =
    let v, r1 = ConfRng.intBelow 11 rng

    { Nodes =
        [ Source("s", "reading", IntRange(0, 10))
          Invoke("a", "echo", IntRange(0, 10), [ "echo/n", FromNode "s" ])
          Invoke("b", "echo", IntRange(0, 10), [ "echo/n", Literal(string v) ]) ] },
    r1

let pipelineWitness: CapabilityPipelineWitness =
    { PipelineRegistry = capabilityRegistry
      GenPipeline = genPipeline }

// ---------------------------------------------------------------------------
//  the columnar pair at a caller's generator
// ---------------------------------------------------------------------------

/// The pipelines a domain might run over the kit's reference table: a filter, a derive, a
/// projection that drops `b`, and a group-by.
let incrementalPipelines: Transform list list =
    [ [ Filter(Binary(Gt, Col "a", Lit(Int 2))) ]
      [ Derive("d", Binary(Add, Col "a", Lit(Int 1))) ]
      [ Project [ "a", "a" ] ]
      [ GroupBy([ "a" ], [ { Name = "s"; Fn = Sum; Of = "b" } ]) ] ]

let private failing (rs: LawResult list) =
    rs |> List.filter (fun r -> not r.Passed) |> List.map (fun r -> r.Law)

let private allGreen (what: string) (rs: LawResult list) =
    for r in rs do
        Expect.isTrue r.Passed (sprintf "%s — %s: %A" what r.Law r.Counterexample)

let private guardLaw (family: string) (dimension: string) =
    SampleAdequacy.lawPrefix family
    + "the sample reached every "
    + dimension
    + " the laws distinguish"

[<Tests>]
let tests =
    testList
        "Phase 246 — witness-taking law families"
        [ testCase "capabilityLawsWith certifies the reference seam green, every outcome reached"
          <| fun _ -> allGreen "capabilityLawsWith" (Conformance.capabilityLawsWith capabilityWitness 2460 300)

          testCase "a host that runs the body before refusal turns capabilityLawsWith RED; capabilityLaws stays green"
          <| fun _ ->
              let planted =
                  Conformance.capabilityLawsWith
                      { capabilityWitness with
                          Dispatch = bodyBeforeRefusal }
                      2460
                      300

              Expect.equal
                  (failing planted)
                  [ "a refusal precedes the body at the domain's host (refused: no body; dispatched: exactly one)" ]
                  "exactly the refusal-precedes-body law is red at the planted host"

              let counterexample = planted |> List.pick (fun r -> r.Counterexample)

              Expect.stringContains counterexample "after the body ran 1 time(s)" "and it says the body ran"

              // the fixture-bound family cannot see a domain's host at all — which is the finding
              allGreen "capabilityLaws" (Conformance.capabilityLaws 2460 300)

          testCase "a host that refuses what the registry admits turns the agreement law RED"
          <| fun _ ->
              let refuseAll (id: string) (_: (string * string) list) (_: Capability -> unit -> Deferred<string>) =
                  Error(NoSuchCapability(id, []))

              let rs =
                  Conformance.capabilityLawsWith
                      { capabilityWitness with
                          Dispatch = refuseAll }
                      2460
                      300

              Expect.contains
                  (failing rs)
                  "the domain's host agrees with its registry (reaches the body iff admitted; refuses with the registry's error)"
                  "the agreement law is red"

          testCase "a generator that never reaches pending starves capabilityLawsWith"
          <| fun _ ->
              let noPending (rng: ConfRng.T) =
                  let (id, args), r = genCapabilityCall rng

                  (if id = "later" then
                       ("echo", [ "echo/n", "3" ])
                   else
                       (id, args)),
                  r

              let rs =
                  Conformance.capabilityLawsWith
                      { capabilityWitness with
                          GenCall = noPending }
                      2460
                      300

              Expect.equal
                  (failing rs)
                  [ guardLaw "Conformance.capabilityLawsWith" "dispatch outcome" ]
                  "exactly the outcome guard is red"

          testCase "queryLawsWith certifies the reference seam green, every outcome reached"
          <| fun _ -> allGreen "queryLawsWith" (Conformance.queryLawsWith queryWitness 2461 300)

          testCase "a host that runs the resolver before refusal turns queryLawsWith RED; queryLaws stays green"
          <| fun _ ->
              let planted =
                  Conformance.queryLawsWith
                      { queryWitness with
                          Dispatch = resolverBeforeRefusal }
                      2461
                      300

              Expect.equal
                  (failing planted)
                  [ "a refused dispatch runs no resolver at the domain's host (refused: none; dispatched: exactly one)" ]
                  "exactly the refused-dispatch law is red at the planted host"

              allGreen "queryLaws" (Conformance.queryLaws 2461 300)

          testCase "capabilityPipelineLawsWith certifies the reference pipelines green"
          <| fun _ ->
              allGreen "capabilityPipelineLawsWith" (Conformance.capabilityPipelineLawsWith pipelineWitness 2462 200)

          testCase "a domain pipeline that does not compose at its registry turns capabilityPipelineLawsWith RED"
          <| fun _ ->
              let broken (rng: ConfRng.T) =
                  let p, r = genPipeline rng

                  { Nodes = p.Nodes @ [ Invoke("c", "echo", IntRange(0, 10), [ "echo/n", Literal "99" ]) ] }, r

              let rs =
                  Conformance.capabilityPipelineLawsWith
                      { pipelineWitness with
                          GenPipeline = broken }
                      2462
                      200

              Expect.contains
                  (failing rs)
                  "every pipeline the domain builds type-checks against its own registry"
                  "the composition law is red"

              allGreen "capabilityPipelineLaws" (Conformance.capabilityPipelineLaws 2462 200)

          testCase "a Source-only pipeline generator starves capabilityPipelineLawsWith"
          <| fun _ ->
              let sourceOnly (rng: ConfRng.T) =
                  { Nodes = [ Source("s", "reading", IntRange(0, 10)) ] }, rng

              let rs =
                  Conformance.capabilityPipelineLawsWith
                      { pipelineWitness with
                          GenPipeline = sourceOnly }
                      2462
                      50

              Expect.equal
                  (failing rs)
                  [ guardLaw "Conformance.capabilityPipelineLawsWith" "built default-deny arm" ]
                  "exactly the invoke-node guard is red"

          testCase "columnarOpLawsWith certifies the kit's reference StreamGen green"
          <| fun _ ->
              allGreen
                  "columnarOpLawsWith"
                  (Conformance.columnarOpLawsWith ColumnOps.invert Conformance.columnarOpStreamGen 2463 200)

          testCase "incrementalLawsWith certifies the reference pipelines green at the kit's StreamGen"
          <| fun _ ->
              allGreen
                  "incrementalLawsWith"
                  (Conformance.incrementalLawsWith incrementalPipelines Conformance.columnarOpStreamGen 2464 200)

          testCase "a generator that never edits a value starves incrementalLawsWith"
          <| fun _ ->
              let appendsOnly: StreamGen<ColumnOp, Table> =
                  { State0 = Conformance.columnarOpStreamGen.State0
                    Op = fun r -> AppendRows [ [ "a", Int 1; "b", Int 2 ] ], r }

              Expect.equal
                  (failing (Conformance.incrementalLawsWith incrementalPipelines appendsOnly 2464 50))
                  [ guardLaw "Conformance.incrementalLawsWith" "value edit" ]
                  "exactly the value-edit guard is red"

          testCase "the witness-taking families take the domain's witness — the roster says so"
          <| fun _ ->
              for id, witness in
                  [ "Conformance.capabilityLawsWith", [ "CapabilitySeamWitness" ]
                    "Conformance.queryLawsWith", [ "QuerySeamWitness" ]
                    "Conformance.capabilityPipelineLawsWith", [ "CapabilityPipelineWitness" ]
                    "Conformance.columnarOpLawsWith", [ "StreamGen" ]
                    "Conformance.incrementalLawsWith", [ "StreamGen" ] ] do
                  Expect.equal
                      (Families.tryFind id |> Option.map (fun f -> f.Witness))
                      (Some witness)
                      (sprintf "%s names the witness it takes" id) ]
