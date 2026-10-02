module Fuaran.Core.Tests.SeamModelTests

// ---------------------------------------------------------------------------
// Phase 251 — the capability and query seams speak to a model.
//
// The seams answer in one envelope with typed refusals; what this suite pins is the model-facing
// half that used to be every adopter's glue: a refusal rendered as one sentence that names the
// alternatives, the same refusal as a wire document that decodes back, a JSON Schema for a query
// beside the one for a capability, the validated arguments handed to a body or resolver, every
// argument refusal at once, and a strict read policy under which an unknown member is refused.
//
// The first renderer cases are the six refusals a stranger's probe printed verbatim when it had
// to write these renderers itself; the sentences below are those sentences, so the probe's glue
// can be deleted with nothing a model reads changing.
// ---------------------------------------------------------------------------

open System.Text.RegularExpressions
open Microsoft.FSharp.Reflection
open Expecto
open Fuaran.Core

let private valueHole (addr: string) (space: ValueSpace) (required: bool) : SigEntry =
    { Addr = addr
      Name = addr
      Kind = "value"
      Space = Some space
      Slot = None
      Action = None
      Required = required }

let private sigOf (name: string) (holes: SigEntry list) : Signature =
    { Name = name
      Holes = holes
      Effect = Effect.pureDeterministic }

/// The probe's formatting tool: a bounded integer and a closed set of stations.
let private fmtTemp =
    Capability.create
        "fmt-temp"
        (sigOf
            "fmt-temp"
            [ valueHole "fmt-temp/celsius" (IntRange(-60, 60)) true
              valueHole "fmt-temp/station" (Enum [ "harbour"; "ridge" ]) true ])
        (ClientIsland Fable)

/// One hole per value space, so every typed argument case is reached.
let private wide =
    Capability.create
        "wide"
        (sigOf
            "wide"
            [ valueHole "n" (IntRange(0, 10)) true
              valueHole "x" (FloatRange(0.0, 1.0)) false
              valueHole "s" (StringLen(1, 5)) false
              valueHole "e" (Enum [ "a"; "b" ]) false
              valueHole "t" AnyString false
              { Addr = "tree"
                Name = "tree"
                Kind = "slot"
                Space = Some(SlotTree(Some "para"))
                Slot = Some "para"
                Action = None
                Required = false } ])
        Server

/// A capability with an action hole, so the strict check reaches an `actionEffect`.
let private withAction =
    Capability.create
        "with-action"
        { Name = "with-action"
          Holes =
            [ valueHole "n" (IntRange(0, 3)) true
              { Addr = "onDone"
                Name = "onDone"
                Kind = "action"
                Space = None
                Slot = None
                Action = Some Effect.pureDeterministic
                Required = false } ]
          Effect =
            { Host = ReadsHost
              Determinism = Effect.network } }
        Precomputed

let private toolIds =
    [ "fmt-temp"; "log-note"; "pick-sample"; "sensor-now"; "stamp" ]

let private registry =
    toolIds
    |> List.fold
        (fun r id ->
            r
            |> Result.bind (
                Registry.register (
                    if id = "fmt-temp" then
                        fmtTemp
                    else
                        { fmtTemp with Id = id }
                )
            ))
        (Ok Registry.empty)
    |> Result.toOption
    |> Option.get

let private readings: Query =
    { Id = "readings"
      Params =
        [ { Name = "station"
            Type = StringType
            Required = true }
          { Name = "limit"
            Type = IntType
            Required = false } ]
      ResultSchema = [ "station", StringType; "celsius", FloatType ]
      Effect =
        { Host = ReadsHost
          Determinism = Effect.network }
      Source = Ref "sensor-archive"
      TimeoutMs = Some 2000
      PageSize = Some 50 }

let private stations: Query =
    { readings with
        Id = "stations"
        Params = []
        ResultSchema = [ "station", StringType ] }

let private queries =
    QueryRegistry.register readings QueryRegistry.empty
    |> Result.bind (QueryRegistry.register stations)
    |> Result.toOption
    |> Option.get

/// A query over an exact decimal, as a parameter and as a result column.
let private invoices: Query =
    { Id = "invoices"
      Params =
        [ { Name = "amount"
            Type = DecimalType
            Required = true }
          { Name = "limit"
            Type = IntType
            Required = false }
          { Name = "day"
            Type = DateType
            Required = false } ]
      ResultSchema = [ "total", DecimalType; "at", TimestampType ]
      Effect = Effect.pureDeterministic
      Source = Ref "ledger"
      TimeoutMs = None
      PageSize = None }

let private emptyResult: QueryResult =
    { Rows = { Schema = []; Columns = [] }
      PageNum = 0
      TotalRowCount = None
      NextPageToken = None }

/// Every case of a union, by name — the coverage guard for the sample lists below.
let private caseNames<'T> () =
    FSharpType.GetUnionCases(typeof<'T>)
    |> Array.map (fun c -> c.Name)
    |> Set.ofArray

let private caseNameOf (v: 'T) =
    (fst (FSharpValue.GetUnionFields(v, typeof<'T>))).Name

let private invokeErrors: InvokeError list =
    [ NoSuchCapability("purge-station", toolIds)
      NoSuchCapability("x", [])
      DuplicateCapability "fmt-temp"
      UnknownArg("celsius", [ "fmt-temp/celsius"; "fmt-temp/station" ])
      UnknownArg("y", [])
      ArgOutOfSpace("n", IntRange(-60, 60), "hot")
      ArgOutOfSpace("x", FloatRange(-0.5, 1e21), "2")
      ArgOutOfSpace("s", StringLen(1, 5), "toolong")
      ArgOutOfSpace("e", Enum [ "a"; "b" ], "c")
      ArgOutOfSpace("e", Enum [], "c")
      ArgOutOfSpace("t", AnyString, "")
      ArgOutOfSpace("tree", SlotTree(Some "para"), "{\"kind\":\"heading\"}")
      ArgOutOfSpace("tree", SlotTree None, "3")
      RequiredArgsUnbound [ "n"; "x" ]
      UninvocableArg "onDone"
      BodyFailed "station offline: no reading in the last 24 hours"
      NonTotalCapability("expand", [ "expand/rows" ]) ]

let private queryErrors: QueryError list =
    [ NoSuchQuery("raw-dump", [ "readings"; "stations" ])
      NoSuchQuery("x", [])
      DuplicateQuery "readings"
      UnknownParam("stn", [ "station"; "limit" ])
      UnknownParam("y", [])
      RequiredParamsUnbound [ "station" ]
      SourceNotResolved "sensor-archive"
      ExecutionFailed("archive shard for station offline is unreachable", [])
      ExecutionFailed("busy", [ "limit" ])
      Timeout
      RequiredParamsNull [ "station" ] ]
    @ [ for expected in
            [ IntType
              FloatType
              BoolType
              StringType
              DateType
              TimestampType
              DecimalType ] -> ParamTypeMismatch("p", expected, StringType) ]

/// A document with one more member, `name: value`, on its top-level object.
let private withMember (name: string) (value: JVal) (doc: string) : string =
    match Json.parse doc with
    | Ok(JObj fields) -> Canon.render (JObj(fields @ [ name, value ]))
    | other -> failwithf "not an object: %A" other

/// Rewrite the first object reached at `path` (member names, array index as a string) to carry
/// one more member.
let rec private injectAt (path: string list) (name: string) (value: JVal) (el: JVal) : JVal =
    match path, el with
    | [], JObj fields -> JObj(fields @ [ name, value ])
    | p :: rest, JObj fields ->
        JObj(
            fields
            |> List.map (fun (k, v) -> if k = p then k, injectAt rest name value v else k, v)
        )
    | p :: rest, JArr xs ->
        JArr(
            xs
            |> List.mapi (fun i v -> if string i = p then injectAt rest name value v else v)
        )
    | _ -> el

let private injectInto (path: string list) (doc: string) : string =
    match Json.parse doc with
    | Ok el -> Canon.render (injectAt path "actor" (JStr "operator") el)
    | Error e -> failwithf "unparseable: %s" e

let private propOf (name: string) (el: JVal) : JVal =
    match Decode.getProp name el with
    | Ok v -> v
    | Error e -> failwithf "no member %s: %s" name e

let private patternOf (schema: JVal) : string =
    match propOf "pattern" schema with
    | JStr p -> p
    | other -> failwithf "pattern is not a string: %A" other

[<Tests>]
let tests =
    testList
        "Seams speak to a model (Phase 251)"
        [ testList
              "refusals rendered"
              [ testCase "the probe's six refused cases render as it printed them, from the real seams"
                <| fun _ ->
                    let refusalOf (r: Result<'a, 'e>) =
                        match r with
                        | Error e -> e
                        | Ok v -> failtestf "expected a refusal, got %A" v

                    let outOfSpace =
                        refusalOf (
                            Capability.validateArgs fmtTemp [ "fmt-temp/celsius", "hot"; "fmt-temp/station", "ridge" ]
                        )

                    Expect.equal
                        (InvokeError.describe outOfSpace)
                        "Refused: argument 'fmt-temp/celsius' must be an integer from -60 to 60; you sent 'hot'."
                        "ArgOutOfSpace"

                    let denied =
                        refusalOf (Registry.dispatch registry "purge-station" [] (fun _ () -> Ready "never"))

                    Expect.equal
                        (InvokeError.describe denied)
                        "Refused: there is no tool 'purge-station' you may call. The tools you may call are 'fmt-temp', 'log-note', 'pick-sample', 'sensor-now', 'stamp'."
                        "NoSuchCapability"

                    let bodyFailed =
                        refusalOf (
                            Capability.invoke
                                fmtTemp
                                [ "fmt-temp/celsius", "4"; "fmt-temp/station", "ridge" ]
                                (fun () -> Failed "station offline: no reading in the last 24 hours")
                        )

                    Expect.equal
                        (InvokeError.describe bodyFailed)
                        "Refused: the tool ran and failed: station offline: no reading in the last 24 hours."
                        "BodyFailed"

                    let mismatch =
                        refusalOf (Query.validateParams readings [ "station", Str "harbour"; "limit", Str "3" ])

                    Expect.equal
                        (QueryError.describe mismatch)
                        "Refused: parameter 'limit' must be int; you sent string."
                        "ParamTypeMismatch"

                    let noQuery =
                        refusalOf (QueryRegistry.dispatch queries "raw-dump" [] (fun _ -> Ready emptyResult))

                    Expect.equal
                        (QueryError.describe noQuery)
                        "Refused: there is no query 'raw-dump' you may run. The queries you may run are 'readings', 'stations'."
                        "NoSuchQuery"

                    let failed =
                        refusalOf (
                            QueryRegistry.dispatch queries "readings" [ "station", Str "offline" ] (fun _ ->
                                Failed "archive shard for station offline is unreachable")
                        )

                    Expect.equal
                        (QueryError.describe failed)
                        "Refused: the query ran and failed: archive shard for station offline is unreachable."
                        "ExecutionFailed"

                testCase "every case is described, and a closed set is named member by member"
                <| fun _ ->
                    Expect.equal
                        (invokeErrors |> List.map caseNameOf |> Set.ofList)
                        (caseNames<InvokeError> ())
                        "the samples reach every InvokeError case"

                    Expect.equal
                        (queryErrors |> List.map caseNameOf |> Set.ofList)
                        (caseNames<QueryError> ())
                        "the samples reach every QueryError case"

                    let alternatives (e: InvokeError) =
                        match e with
                        | NoSuchCapability(_, xs)
                        | UnknownArg(_, xs)
                        | ArgOutOfSpace(_, Enum xs, _)
                        | RequiredArgsUnbound xs
                        | NonTotalCapability(_, xs) -> xs
                        | _ -> []

                    for e in invokeErrors do
                        let text = InvokeError.describe e
                        Expect.stringStarts text "Refused: " (sprintf "%A reads as a refusal" e)

                        for x in alternatives e do
                            Expect.stringContains text ("'" + x + "'") (sprintf "%A names '%s'" e x)

                    let queryAlternatives (e: QueryError) =
                        match e with
                        | NoSuchQuery(_, xs)
                        | UnknownParam(_, xs)
                        | RequiredParamsUnbound xs
                        | RequiredParamsNull xs
                        | ExecutionFailed(_, xs) -> xs
                        | _ -> []

                    for e in queryErrors do
                        let text = QueryError.describe e
                        Expect.stringStarts text "Refused: " (sprintf "%A reads as a refusal" e)

                        for x in queryAlternatives e do
                            Expect.stringContains text ("'" + x + "'") (sprintf "%A names '%s'" e x)

                testCase "a value space is described in words a model can aim at"
                <| fun _ ->
                    Expect.equal
                        (Space.describe (FloatRange(-0.5, 2.0)))
                        "a number from -0.5 to 2"
                        "float bounds, canonical layout"

                    Expect.equal (Space.describe (StringLen(1, 5))) "a string of 1 to 5 characters" "string length"
                    Expect.equal (Space.describe (Enum [ "a"; "b" ])) "one of 'a', 'b'" "enum members"
                    Expect.stringContains (Space.describe (SlotTree(Some "para"))) "'para'" "slot kind named"

                testCase "a decimal mismatch names decimal and says how to write one"
                <| fun _ ->
                    let text = QueryError.describe (ParamTypeMismatch("amount", DecimalType, FloatType))

                    Expect.stringStarts
                        text
                        "Refused: parameter 'amount' must be decimal; you sent float."
                        "the mismatch"

                    Expect.stringContains text "JSON string of decimal text" "how to write one"
                    Expect.stringContains text "\"12.50\"" "an example"

                testCase "describeAll answers every refusal, one per line"
                <| fun _ ->
                    let es = [ UnknownArg("q", [ "n" ]); RequiredArgsUnbound [ "n" ] ]

                    Expect.equal
                        (InvokeError.describeAll es)
                        (es |> List.map InvokeError.describe |> String.concat "\n")
                        "invoke"

                    let qs = [ UnknownParam("q", [ "station" ]); RequiredParamsUnbound [ "station" ] ]

                    Expect.equal
                        (QueryError.describeAll qs)
                        (qs |> List.map QueryError.describe |> String.concat "\n")
                        "query" ]

          testList
              "refusals encoded"
              [ testCase "every InvokeError round-trips through its wire document"
                <| fun _ ->
                    for e in invokeErrors do
                        let doc = CapabilityCodec.encodeInvokeError e
                        Expect.equal (CapabilityCodec.decodeInvokeError doc) (Ok e) (sprintf "round trip of %s" doc)
                        Expect.stringStarts doc "{\"$type\":" "the codec's $type envelope"

                testCase "every QueryError round-trips through its wire document"
                <| fun _ ->
                    for e in queryErrors do
                        let doc = QueryCodec.encodeQueryError e
                        Expect.equal (QueryCodec.decodeQueryError doc) (Ok e) (sprintf "round trip of %s" doc)
                        Expect.stringStarts doc "{\"$type\":" "the codec's $type envelope"

                testCase "the out-of-space refusal carries its space in codec form"
                <| fun _ ->
                    Expect.equal
                        (CapabilityCodec.encodeInvokeError (ArgOutOfSpace("fmt-temp/celsius", IntRange(-60, 60), "hot")))
                        "{\"$type\":\"argOutOfSpace\",\"addr\":\"fmt-temp/celsius\",\"got\":\"hot\",\"space\":{\"$type\":\"intRange\",\"max\":60,\"min\":-60}}"
                        "bytes"

                    Expect.equal
                        (QueryCodec.encodeQueryError (ParamTypeMismatch("limit", IntType, StringType)))
                        "{\"$type\":\"paramTypeMismatch\",\"expected\":\"int\",\"got\":\"string\",\"name\":\"limit\"}"
                        "bytes"

                testCase "an unknown refusal tag is refused by name"
                <| fun _ ->
                    Expect.isError (CapabilityCodec.decodeInvokeError "{\"$type\":\"nope\"}") "invoke"
                    Expect.isError (QueryCodec.decodeQueryError "{\"$type\":\"nope\"}") "query" ]

          testList
              "validated arguments handed on"
              [ testCase "a body receives the validated arguments, typed by their spaces"
                <| fun _ ->
                    let tree = "{\"kind\":\"para\",\"text\":\"hi\"}"

                    let args = [ "n", "3"; "x", "0.5"; "s", "ab"; "e", "a"; "t", "free"; "tree", tree ]

                    let mutable seen = []

                    let r =
                        Capability.invokeWithArgs wide args (fun typed ->
                            seen <- typed
                            Ready "ok")

                    Expect.equal r (Ok(Ready "ok")) "settled"

                    Expect.equal
                        seen
                        [ "n", IntValue 3
                          "x", FloatValue 0.5
                          "s", TextValue "ab"
                          "e", TextValue "a"
                          "t", TextValue "free"
                          "tree", TreeValue(Json.parse tree |> Result.toOption |> Option.get) ]
                        "typed, in argument order"

                    Expect.equal (Capability.typeArgs wide args) (Ok seen) "typeArgs is the list handed on"

                testCase "a refused call never reaches the body, and answers as validateArgs does"
                <| fun _ ->
                    let mutable calls = 0

                    for args in [ [ "n", "11" ]; [ "zz", "1" ]; []; [ "n", "1"; "tree", "3" ] ] do
                        let r =
                            Capability.invokeWithArgs wide args (fun _ ->
                                calls <- calls + 1
                                Ready "no")

                        match Capability.validateArgs wide args with
                        | Error e -> Expect.equal r (Error e) (sprintf "same refusal for %A" args)
                        | Ok() -> failtestf "expected %A to be refused" args

                    Expect.equal calls 0 "the body never ran"

                testCase "a body's Failed and Pending keep their invoke meanings"
                <| fun _ ->
                    Expect.equal
                        (Capability.invokeWithArgs wide [ "n", "1" ] (fun _ -> Failed "boom"))
                        (Error(BodyFailed "boom"))
                        "Failed is BodyFailed"

                    Expect.equal
                        (Capability.invokeWithArgs wide [ "n", "1" ] (fun _ -> (Pending: Deferred<string>)))
                        (Ok Pending)
                        "Pending is in flight"

                testCase "dispatchWithArgs denies by default and hands on the resolved capability"
                <| fun _ ->
                    match Registry.dispatchWithArgs registry "purge-station" [] (fun _ _ -> Ready "never") with
                    | Error(NoSuchCapability("purge-station", known)) -> Expect.equal known toolIds "names the tools"
                    | other -> failtestf "expected NoSuchCapability, got %A" other

                    let r =
                        Registry.dispatchWithArgs
                            registry
                            "fmt-temp"
                            [ "fmt-temp/celsius", "-4"; "fmt-temp/station", "ridge" ]
                            (fun c typed -> Ready(c.Id, typed))

                    Expect.equal
                        r
                        (Ok(
                            Ready(
                                "fmt-temp",
                                [ "fmt-temp/celsius", IntValue -4; "fmt-temp/station", TextValue "ridge" ]
                            )
                        ))
                        "the capability and its typed arguments"

                testCase "a resolver receives the validated argument list"
                <| fun _ ->
                    let args = [ "station", Str "harbour"; "limit", Int 2 ]
                    let mutable seen = []
                    let mutable calls = 0

                    let resolve (q: Query) (a: (string * Cell) list) =
                        calls <- calls + 1
                        seen <- a

                        Ok(
                            Ready
                                { emptyResult with
                                    PageNum = q.Params.Length }
                        )

                    Expect.equal
                        (QueryRegistry.dispatchWithArgs queries "readings" args resolve)
                        (Ok(Ready { emptyResult with PageNum = 2 }))
                        "settled with the resolved query"

                    Expect.equal seen args "the list that was validated"

                    match QueryRegistry.dispatchWithArgs queries "raw-dump" args resolve with
                    | Error(NoSuchQuery("raw-dump", [ "readings"; "stations" ])) -> ()
                    | other -> failtestf "expected NoSuchQuery, got %A" other

                    Expect.equal
                        (Query.invokeWithArgs readings [ "limit", Int 1 ] resolve)
                        (Error(RequiredParamsUnbound [ "station" ]))
                        "refused before the resolver"

                    Expect.equal
                        (Query.invokeWithArgs readings args (fun _ _ -> Ok(Failed "down")))
                        (Error(ExecutionFailed("down", [])))
                        "Failed is ExecutionFailed"

                    Expect.equal calls 1 "the resolver ran once, for the settled call" ]

          testList
              "every violation at once"
              [ testCase "a two-argument violation is refused naming both"
                <| fun _ ->
                    match
                        Capability.validateArgsAll fmtTemp [ "fmt-temp/celsius", "hot"; "fmt-temp/station", "moon" ]
                    with
                    | Error [ ArgOutOfSpace("fmt-temp/celsius", _, "hot"); ArgOutOfSpace("fmt-temp/station", _, "moon") ] as r ->
                        let text =
                            match r with
                            | Error es -> InvokeError.describeAll es
                            | Ok() -> ""

                        Expect.stringContains text "'fmt-temp/celsius'" "names the first"
                        Expect.stringContains text "'fmt-temp/station'" "names the second"
                    | other -> failtestf "expected both refusals, got %A" other

                    match Query.validateParamsAll readings [ "stn", Str "harbour"; "limit", Str "3" ] with
                    | Error [ UnknownParam("stn", _)
                              ParamTypeMismatch("limit", IntType, StringType)
                              RequiredParamsUnbound [ "station" ] ] -> ()
                    | other -> failtestf "expected all three refusals, got %A" other

                testCase "the first-failure form is the head of the every-failure form (capability)"
                <| fun _ ->
                    let choices (addr: string) (good: string) (bad: string) = [ []; [ addr, good ]; [ addr, bad ] ]

                    let mutable cases = 0

                    for n in choices "n" "4" "11" do
                        for x in choices "x" "0.25" "2" do
                            for e in choices "e" "b" "c" do
                                for tree in choices "tree" "{\"kind\":\"para\"}" "nope" do
                                    for extra in [ []; [ "zz", "1" ] ] do
                                        for act in [ []; [ "onDone", "x" ] ] do
                                            let args = e @ extra @ n @ tree @ x @ act
                                            let cap = if act.IsEmpty then wide else withAction
                                            cases <- cases + 1

                                            let first =
                                                match Capability.validateArgsAll cap args with
                                                | Ok() -> Ok()
                                                | Error(h :: _) -> Error h
                                                | Error [] -> failtest "an empty refusal list"

                                            Expect.equal first (Capability.validateArgs cap args) (sprintf "%A" args)

                    Expect.equal cases 324 "every combination ran"

                testCase "the first-failure form is the head of the every-failure form (query)"
                <| fun _ ->
                    let q: Query =
                        { readings with
                            Params =
                                [ { Name = "year"
                                    Type = IntType
                                    Required = true }
                                  { Name = "region"
                                    Type = StringType
                                    Required = false }
                                  { Name = "amount"
                                    Type = DecimalType
                                    Required = true } ] }

                    let choices (name: string) (good: Cell) (bad: Cell) =
                        [ []; [ name, good ]; [ name, bad ]; [ name, Null ] ]

                    let mutable cases = 0

                    for year in choices "year" (Int 2026) (Str "2026") do
                        for region in choices "region" (Str "UK") (Int 1) do
                            for amount in choices "amount" (Decimal "1.5") (Float 1.5) do
                                for extra in [ []; [ "zz", Int 1 ] ] do
                                    let args = amount @ extra @ region @ year
                                    cases <- cases + 1

                                    let first =
                                        match Query.validateParamsAll q args with
                                        | Ok() -> Ok()
                                        | Error(h :: _) -> Error h
                                        | Error [] -> failtest "an empty refusal list"

                                    Expect.equal first (Query.validateParams q args) (sprintf "%A" args)

                    Expect.equal cases 128 "every combination ran"

                testCase "unbound and null-bound required parameters are both named, never one twice"
                <| fun _ ->
                    let q: Query =
                        { readings with
                            Params =
                                [ { Name = "a"
                                    Type = IntType
                                    Required = true }
                                  { Name = "b"
                                    Type = IntType
                                    Required = true } ] }

                    Expect.equal
                        (Query.validateParamsAll q [ "b", Null ])
                        (Error [ RequiredParamsUnbound [ "a" ]; RequiredParamsNull [ "b" ] ])
                        "both, each once" ]

          testList
              "strict read policy"
              [ testCase "the adversary's unknown actor beside an invocation is refused under Strict"
                <| fun _ ->
                    let doc =
                        CapabilityCodec.encodeInvocation "page-oncall" [ "page-oncall/team", "sre" ]
                        |> withMember "actor" (JStr "operator")

                    let expected = Ok("page-oncall", [ "page-oncall/team", "sre" ])
                    Expect.equal (CapabilityCodec.decodeInvocation doc) expected "the lenient default reads past it"

                    Expect.equal
                        (CapabilityCodec.decodeInvocationWith ReadPolicy.Lenient doc)
                        expected
                        "Lenient is the default"

                    match CapabilityCodec.decodeInvocationWith ReadPolicy.Strict doc with
                    | Error m ->
                        Expect.stringContains m "'actor'" "names the member"
                        Expect.stringContains m "'$type', 'args', 'capabilityId'" "names the members read"
                    | Ok v -> failtestf "Strict read past an unknown member: %A" v

                testCase "an unknown member anywhere a capability codec reads is refused under Strict"
                <| fun _ ->
                    let invocation =
                        CapabilityCodec.encodeInvocation "fmt-temp" [ "fmt-temp/celsius", "4" ]

                    Expect.isError
                        (CapabilityCodec.decodeInvocationWith ReadPolicy.Strict (injectInto [ "args"; "0" ] invocation))
                        "an invocation argument"

                    for cap in [ fmtTemp; wide; withAction ] do
                        let doc = CapabilityCodec.encode cap

                        for path in
                            [ []
                              [ "signature" ]
                              [ "signature"; "effect" ]
                              [ "signature"; "holes"; "0" ]
                              [ "signature"; "holes"; "0"; "space" ]
                              [ "placement" ] ] do
                            let tampered = injectInto path doc

                            Expect.isOk
                                (CapabilityCodec.decode tampered)
                                (sprintf "%s: Lenient reads past %A" cap.Id path)

                            match CapabilityCodec.decodeWith ReadPolicy.Strict tampered with
                            | Error m ->
                                Expect.stringContains m "'actor'" (sprintf "%s: names the member at %A" cap.Id path)
                            | Ok _ -> failtestf "%s: Strict read past a member at %A" cap.Id path

                    Expect.isError
                        (CapabilityCodec.decodeWith
                            ReadPolicy.Strict
                            (injectInto
                                [ "signature"; "holes"; "1"; "actionEffect" ]
                                (CapabilityCodec.encode withAction)))
                        "an action hole's effect"

                testCase "everything the capability codec emits reads back under Strict"
                <| fun _ ->
                    for cap in [ fmtTemp; wide; withAction ] do
                        Expect.equal
                            (CapabilityCodec.decodeWith ReadPolicy.Strict (CapabilityCodec.encode cap))
                            (CapabilityCodec.decode (CapabilityCodec.encode cap))
                            cap.Id

                    for d in [ Pending; Ready "v"; Failed "m" ] do
                        let doc = CapabilityCodec.encodeDeferred JStr d

                        Expect.equal
                            (CapabilityCodec.decodeDeferredWith ReadPolicy.Strict Decode.asString doc)
                            (Ok d)
                            doc

                        Expect.isError
                            (CapabilityCodec.decodeDeferredWith
                                ReadPolicy.Strict
                                Decode.asString
                                (withMember "actor" (JStr "x") doc))
                            (sprintf "%s with an extra member" doc)

                testCase "the query codec's documents read strictly, and an unknown member is refused"
                <| fun _ ->
                    let decl = QueryCodec.encode invoices
                    Expect.equal (QueryCodec.decodeWith ReadPolicy.Strict decl) (Ok invoices) "a declaration"

                    for path in [ []; [ "params"; "0" ]; [ "resultSchema"; "1" ]; [ "effect" ] ] do
                        let tampered = injectInto path decl

                        Expect.equal
                            (QueryCodec.decodeWith ReadPolicy.Lenient tampered)
                            (Ok invoices)
                            (sprintf "Lenient at %A" path)

                        match QueryCodec.decodeWith ReadPolicy.Strict tampered with
                        | Error m -> Expect.stringContains m "'actor'" (sprintf "names the member at %A" path)
                        | other -> failtestf "Strict at %A: %A" path other

                    let result =
                        { Rows =
                            { Schema = [ "total", DecimalType ]
                              Columns = [ Column.create "total" DecimalType [ Decimal "12.5" ] ] }
                          PageNum = 1
                          TotalRowCount = Some 1
                          NextPageToken = Some "t" }

                    let doc = QueryCodec.encodeResult result
                    Expect.equal (QueryCodec.decodeResultWith ReadPolicy.Strict doc) (Ok result) "a result"

                    Expect.isError
                        (QueryCodec.decodeResultWith ReadPolicy.Strict (injectInto [] doc))
                        "a result's member"

                    for d in [ Pending; Ready result; Failed "m" ] do
                        let doc = QueryCodec.encodeDeferredResult d
                        Expect.equal (QueryCodec.decodeDeferredResultWith ReadPolicy.Strict doc) (Ok d) doc

                        Expect.isError
                            (QueryCodec.decodeDeferredResultWith ReadPolicy.Strict (injectInto [] doc))
                            "the envelope"

                    let ready = QueryCodec.encodeDeferredResult (Ready result)

                    Expect.isOk
                        (QueryCodec.decodeDeferredResultWith ReadPolicy.Lenient (injectInto [ "value" ] ready))
                        "Lenient"

                    Expect.isError
                        (QueryCodec.decodeDeferredResultWith ReadPolicy.Strict (injectInto [ "value" ] ready))
                        "the carried result" ]

          testList
              "a query schema"
              [ testCase "Query.toJsonSchema is Function.toJsonSchema's shape, untagged"
                <| fun _ ->
                    let schema = Query.toJsonSchema readings
                    let fnSchema = Function.toJsonSchema fmtTemp.Signature

                    let keys (el: JVal) =
                        match el with
                        | JObj fields -> fields |> List.map fst |> Set.ofList
                        | _ -> Set.empty

                    let shared = set [ "type"; "title"; "x-effect"; "properties"; "required" ]
                    Expect.isTrue (Set.isSubset shared (keys schema)) "the query schema's keys"
                    Expect.isTrue (Set.isSubset shared (keys fnSchema)) "the capability schema's keys"
                    Expect.isFalse ((keys schema).Contains "$type" || (keys schema).Contains "kind") "no tag"

                    Expect.equal
                        (propOf "x-effect" schema)
                        (propOf
                            "x-effect"
                            (Function.toJsonSchema
                                { fmtTemp.Signature with
                                    Effect = readings.Effect }))
                        "one effect spelling"

                    Expect.equal
                        (Canon.render schema)
                        "{\"properties\":{\"limit\":{\"maximum\":2147483647,\"minimum\":-2147483648,\"type\":\"integer\"},\"station\":{\"type\":\"string\"}},\"required\":[\"station\"],\"title\":\"readings\",\"type\":\"object\",\"x-effect\":{\"determinism\":\"network\",\"host\":\"readsHost\"},\"x-result\":{\"properties\":{\"celsius\":{\"type\":\"number\"},\"station\":{\"type\":\"string\"}},\"type\":\"object\"}}"
                        "bytes"

                testCase "a decimal parameter and a decimal result column project as patterned strings"
                <| fun _ ->
                    let schema = Query.toJsonSchema invoices
                    let amount = schema |> propOf "properties" |> propOf "amount"
                    let total = schema |> propOf "x-result" |> propOf "properties" |> propOf "total"
                    Expect.equal (propOf "type" amount) (JStr "string") "a parameter is a string, never a number"
                    Expect.equal (propOf "type" total) (JStr "string") "a result column is a string, never a number"
                    Expect.equal (patternOf amount) (patternOf total) "one pattern"

                testCase "the round trip: projected schema, an emitted string, a Decimal cell"
                <| fun _ ->
                    let schema = Query.toJsonSchema invoices
                    let amount = schema |> propOf "properties" |> propOf "amount"
                    let pattern = Regex(patternOf amount)
                    let emitted = "12.50"
                    Expect.isTrue (pattern.IsMatch emitted) "the emission satisfies the projected schema"

                    let args =
                        QueryCodec.decodeArgs invoices ("{\"amount\":\"" + emitted + "\",\"limit\":5}")

                    Expect.equal args (Ok [ "amount", Decimal "12.5"; "limit", Int 5 ]) "decoded and canonicalised"

                    let mutable seen = []

                    let r =
                        Query.invokeWithArgs invoices (Result.defaultValue [] args) (fun _ a ->
                            seen <- a
                            Ok(Ready emptyResult))

                    Expect.equal r (Ok(Ready emptyResult)) "dispatched"
                    Expect.equal seen [ "amount", Decimal "12.5"; "limit", Int 5 ] "the resolver reads a Decimal cell"

                testCase "the decimal pattern and the decoder agree, in both directions"
                <| fun _ ->
                    let pattern =
                        Regex(
                            Query.toJsonSchema invoices
                            |> propOf "properties"
                            |> propOf "amount"
                            |> patternOf
                        )

                    let decodes (s: string) =
                        match QueryCodec.decodeArgsJson invoices (JObj [ "amount", JStr s ]) with
                        | Ok [ "amount", Decimal _ ] -> true
                        | _ -> false

                    for s in
                        [ "0"
                          "12"
                          "-3.5"
                          "0012.500"
                          "1.0"
                          "-0"
                          "123456789012345678901234567890.1" ] do
                        Expect.isTrue (pattern.IsMatch s) (sprintf "'%s' satisfies the schema" s)
                        Expect.isTrue (decodes s) (sprintf "'%s' decodes" s)

                    for s in [ "+1"; ".5"; "5."; "1e3"; "1,000"; " 1"; "1 "; ""; "-"; "1.2.3"; "0x1F" ] do
                        Expect.isFalse (pattern.IsMatch s) (sprintf "'%s' is outside the schema" s)
                        Expect.isFalse (decodes s) (sprintf "'%s' is refused" s)

                testCase "a model told number would emit a fractional token, and it is refused naming decimal"
                <| fun _ ->
                    match QueryCodec.decodeArgs invoices "{\"amount\":12.5}" with
                    | Error [ ParamTypeMismatch("amount", DecimalType, FloatType) as e ] ->
                        Expect.stringContains (QueryError.describe e) "must be decimal" "names decimal"
                        Expect.stringContains (QueryError.describe e) "\"12.50\"" "says how to write one"
                    | other -> failtestf "expected a decimal mismatch, got %A" other

                testCase "decodeArgs answers every refusal, and an undeclared member is never read past"
                <| fun _ ->
                    match
                        QueryCodec.decodeArgs
                            invoices
                            "{\"amount\":\"x\",\"actor\":\"operator\",\"day\":\"2026/09/01\",\"limit\":\"5\"}"
                    with
                    | Error [ ParamTypeMismatch("amount", DecimalType, StringType)
                              UnknownParam("actor", [ "amount"; "limit"; "day" ])
                              ParamTypeMismatch("day", DateType, StringType)
                              ParamTypeMismatch("limit", IntType, StringType) ] -> ()
                    | other -> failtestf "expected four refusals, got %A" other

                    Expect.equal
                        (QueryCodec.decodeArgs invoices "{\"amount\":7,\"day\":\"2026-09-01\"}")
                        (Ok [ "amount", Decimal "7"; "day", Date "2026-09-01" ])
                        "an integer token is an exact decimal; a canonical date reads"

                    match QueryCodec.decodeArgs invoices "[1]" with
                    | Error [ ExecutionFailed(_, []) ] -> ()
                    | other -> failtestf "expected a shape refusal, got %A" other

                    match QueryCodec.decodeArgs invoices "{\"amount\":[\"1\"]}" with
                    | Error [ ExecutionFailed(m, []) ] -> Expect.stringContains m "'amount'" "names the parameter"
                    | other -> failtestf "expected a shape refusal, got %A" other

                testCase "a date and a timestamp project their canonical shapes"
                <| fun _ ->
                    let schema = Query.toJsonSchema invoices
                    let day = schema |> propOf "properties" |> propOf "day" |> patternOf |> Regex

                    let at =
                        schema
                        |> propOf "x-result"
                        |> propOf "properties"
                        |> propOf "at"
                        |> patternOf
                        |> Regex

                    Expect.isTrue (day.IsMatch "2026-09-01") "a date"
                    Expect.isFalse (day.IsMatch "2026/09/01") "not a date"
                    Expect.isTrue (at.IsMatch "2026-09-01T10:00:00Z") "a timestamp"
                    Expect.isFalse (at.IsMatch "2026-09-01T10:00:00+01:00") "an offset is not the canonical form" ] ]
