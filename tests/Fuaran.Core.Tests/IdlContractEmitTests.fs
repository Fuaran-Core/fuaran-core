module Fuaran.Core.Tests.IdlContractEmitTests

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests

// Phase 644 (DECISIONS.md D260.14, D260.18) — what Core emits from a contract, and the contract
// vectors. The JSON Schema of a method's arguments and the vocabulary the F* backend models a
// contract's types through; then `conformance/idl/contracts.json`, replayed from the COMMITTED bytes
// through the public readers, which is the path a port follows and a second one from the fixture's
// in-memory values the exporter read.

let private ok (r: Result<'T, CodegenError>) : 'T =
    match r with
    | Ok v -> v
    | Error e -> failtestf "refused: %s" (CodegenError.describe e)

let private schemaOf (method: string) : JVal =
    match Json.parse (Gen.methodArgumentsSchema ContractsIdl.idl method |> ok) with
    | Ok v -> v
    | Error e -> failtestf "the schema of %s is not JSON: %s" method e

let private at (path: string list) (v: JVal) : JVal option =
    (Some v, path)
    ||> List.fold (fun acc key -> acc |> Option.bind (Decoder.tryMember key))

let private keys (v: JVal option) : string list =
    match v with
    | Some(JObj members) -> members |> List.map fst
    | _ -> []

let rec private refs (v: JVal) : string list =
    match v with
    | JObj members ->
        members
        |> List.collect (fun (k, x) ->
            match k, x with
            | "$ref", JStr r -> [ r ]
            | _ -> refs x)
    | JArr xs -> xs |> List.collect refs
    | _ -> []

[<Tests>]
let emitTests =
    testList
        "IdlContractEmit"
        [ testCase "a method's arguments are one object schema, in the shape a tool definition takes" (fun _ ->
              let s = schemaOf "orders.list"
              Expect.equal (at [ "title" ] s) (Some(JStr "orders.list")) "titled with the method's name"
              Expect.equal (at [ "type" ] s) (Some(JStr "object")) "an object"

              Expect.equal
                  (keys (at [ "properties" ] s))
                  [ "paging"; "limit"; "status" ]
                  "a property an argument, in order"

              Expect.equal (at [ "required" ] s) (Some(JArr [ JStr "paging" ])) "only what a caller must supply"
              Expect.equal (at [ "additionalProperties" ] s) (Some(JBool false)) "an undeclared argument is refused"

              Expect.equal
                  (at [ "properties"; "limit" ] s)
                  (Some(
                      JObj
                          [ "type", JStr "integer"
                            "default", JInt 20
                            "minimum", JInt 1
                            "maximum", JInt 100 ]
                  ))
                  "the type, the declared default and the bound"

              Expect.equal
                  (at [ "properties"; "paging"; "description" ] s)
                  (Some(JStr "Which page."))
                  "an argument's description"

              Expect.equal
                  (at [ "description" ] (schemaOf "orders.get"))
                  (Some(JStr "One order by its id."))
                  "the method's description")

          testCase "`$defs` holds the declared types the arguments reach, and no other" (fun _ ->
              Expect.equal (keys (at [ "$defs" ] (schemaOf "orders.list"))) [ "Status"; "Paging" ] "list reaches two"
              Expect.equal (keys (at [ "$defs" ] (schemaOf "orders.place"))) [ "Order" ] "place reaches one"
              Expect.isNone (at [ "$defs" ] (schemaOf "orders.get")) "a method of scalars has none"
              Expect.isNone (at [ "$defs" ] (schemaOf "orders.export")) "nor has a method of no argument")

          testCase "LAW: every reference in every method's schema resolves in that schema" (fun _ ->
              let all = Gen.contractArgumentSchemas ContractsIdl.idl |> ok

              Expect.equal
                  (all |> List.map fst)
                  (ContractsIdl.idl.Contracts
                   |> List.collect (fun c -> c.Methods |> List.map (fun m -> c.Name + "." + m.Id)))
                  "one schema a method, in declared order"

              for (name, schemaText) in all do
                  match Json.parse schemaText with
                  | Error e -> failtestf "%s: not JSON: %s" name e
                  | Ok s ->
                      let defined =
                          keys (at [ "$defs" ] s) |> List.map (fun n -> "#/$defs/" + n) |> Set.ofList

                      for r in refs s do
                          Expect.isTrue (defined.Contains r) (sprintf "%s: %s resolves" name r))

          testCase "a float argument is a number, and a length bound refuses no admitted value" (fun _ ->
              Expect.equal
                  (at [ "properties"; "ratio" ] (schemaOf "probe.every"))
                  (Some(JObj [ "type", JStr "number" ]))
                  "no non-finite token is offered (D260.7)"

              Expect.equal
                  (at [ "properties"; "note" ] (schemaOf "orders.place"))
                  (Some(JObj [ "type", JStr "string"; "minLength", JInt 0; "maxLength", JInt 200 ]))
                  "the declared bound"

              // The IDL counts UTF-16 code units and JSON Schema characters: five units are at least
              // three characters, so `minLength` is three, and nine units are at most nine characters.
              let bounded =
                  ContractsIdl.withMethod "get" (fun m ->
                      { m with
                          Arguments =
                              [ { ContractsIdl.arg "id" TStr with
                                    Constraint = Some(ArgumentConstraint.LengthBetween(5, 9)) } ] })

              match Json.parse (Gen.methodArgumentsSchema bounded "orders.get" |> ok) with
              | Ok s ->
                  Expect.equal
                      (at [ "properties"; "id" ] s)
                      (Some(JObj [ "type", JStr "string"; "minLength", JInt 3; "maxLength", JInt 9 ]))
                      "half the minimum, rounded up"
              | Error e -> failtestf "not JSON: %s" e)

          testCase "a method the vocabulary does not declare is refused, and no contract is no schema" (fun _ ->
              Expect.isError (Gen.methodArgumentsSchema ContractsIdl.idl "orders.nope") "an unknown method"
              Expect.equal (Gen.contractArgumentSchemas (ContractsIdl.withContracts []) |> ok) [] "no contract")

          testCase
              "a vocabulary generates the same schema and the same F# whether or not it declares a contract"
              (fun _ ->
                  let bare = ContractsIdl.withContracts []
                  Expect.equal (Gen.jsonSchema ContractsIdl.idl |> ok) (Gen.jsonSchema bare |> ok) "the JSON Schema"
                  Expect.equal (Gen.fsharpTypes ContractsIdl.idl |> ok) (Gen.fsharpTypes bare |> ok) "the F# types")

          testCase "a contract's types are a vocabulary: a kind of each method's arguments and of each result" (fun _ ->
              let vocabulary, kinds = FStarTarget.contractTypes ContractsIdl.idl "orders" |> ok

              Expect.equal
                  kinds
                  [ "GetArguments"
                    "GetResult"
                    "ListArguments"
                    "ListResult"
                    "PlaceArguments"
                    "PlaceResult"
                    "CancelArguments"
                    "WatchArguments"
                    "WatchResult"
                    "ExportArguments"
                    "ExportResult" ]
                  "in declared order; a method that answers nothing has no result kind"

              Expect.isEmpty (Declare.errors vocabulary) "the vocabulary is well-formed"
              Expect.isEmpty vocabulary.Contracts "and declares no contract"

              let list = vocabulary.Kinds |> List.find (fun k -> k.Tag = "ListArguments")

              Expect.equal
                  (list.Fields |> List.map (fun f -> f.Name, f.Type, f.Opt))
                  [ "paging", TRecord "Paging", Required
                    "limit", TInt, OmitDefault(VInt 20)
                    "status", TEnum "Status", Optional ]
                  "the members are the arguments as declared"

              let result = vocabulary.Kinds |> List.find (fun k -> k.Tag = "ListResult")

              Expect.equal
                  (result.Fields |> List.map (fun f -> f.Name, f.Type, f.Opt))
                  [ "result", TList(TRecord "Order"), Required ]
                  "the result kind holds the answer")

          testCase "a contract the vocabulary does not declare, and a kind name already taken, are refused" (fun _ ->
              Expect.isError (FStarTarget.contractTypes ContractsIdl.idl "nope") "an unknown contract"

              let taken =
                  { ContractsIdl.idl with
                      Records =
                          ContractsIdl.idl.Records
                          @ [ { Name = "GetResult"
                                Fields = []
                                Annotations = Annotations.Empty
                                Inline = false } ] }

              Expect.isError (FStarTarget.contractTypes taken "orders") "a declared name is not minted twice")

          testCase "the committed F* model of the contract fixture models every kind but the one it names" (fun _ ->
              let modelled =
                  FStarTarget.partition FStarModels.contractVocabulary
                  |> List.filter _.Refusal.IsNone
                  |> List.map _.Tag

              Expect.equal modelled.Length 10 "ten of the eleven kinds"
              Expect.isFalse (List.contains "ListArguments" modelled) "an integer default is refused by name (D259)") ]

// ---- the vectors, replayed from the committed file ----

let private committed: JVal =
    let file =
        Path.Combine(FStarModels.repoRoot, "conformance", ContractsCorpus.familyDirName, ContractsCorpus.fileName)

    match Json.parse (File.ReadAllText file) with
    | Ok v -> v
    | Error e -> failwithf "conformance/idl/contracts.json is not JSON: %s" e

let private text (name: string) (v: JVal) : string =
    match Decoder.tryMember name v with
    | Some(JStr s) -> s
    | _ -> failtestf "the vector carries no string `%s`" name

let private vectors (case: string) : JVal list =
    match Decoder.tryMember "vectors" committed with
    | Some(JArr vs) -> vs |> List.filter (fun v -> Decoder.tryMember "case" v = Some(JStr case))
    | _ -> failtest "the file carries no vectors"

/// The revision a `valid` or a `refusal` vector names: its `contracts` over its own `idl`, or over the
/// file's where it carries none.
let private revisionOf (v: JVal) : string =
    let idl =
        match Decoder.tryMember "idl" v with
        | Some(JStr own) -> own
        | _ -> text "idl" committed

    ContractsCorpus.revision idl (text "contracts" v)

/// The names of the contract refusals the artifact reader gives a document, in order.
let private readerRefusals (input: string) : string list =
    match Artifact.parseDetailed input with
    | Ok _ -> []
    | Error errors ->
        errors
        |> List.choose (fun e ->
            let marker = "schema fault: "
            let i = e.Message.IndexOf marker

            if e.Code = DecodeCode.SchemaFault && i >= 0 then
                let rest = e.Message.Substring(i + marker.Length)
                Some(rest.Substring(0, rest.IndexOf ':'))
            else
                None)

[<Tests>]
let vectorTests =
    testList
        "IdlContractVectors"
        [ testCase
              "the file carries every case: valid, each refusal, each method's projection, each differ case"
              (fun _ ->
                  Expect.equal (vectors "valid").Length 3 "valid"
                  Expect.equal (vectors "refusal").Length ContractsCorpus.refusals.Length "one a refusal case"

                  let named =
                      vectors "refusal"
                      |> List.collect (fun v ->
                          match at [ "expected"; "refusals" ] v with
                          | Some(JArr rs) -> rs |> List.map (text "name")
                          | _ -> [])
                      |> Set.ofList

                  let all =
                      Reflection.FSharpType.GetUnionCases typeof<ContractFault>
                      |> Array.map (fun c -> string (System.Char.ToLowerInvariant c.Name[0]) + c.Name.Substring 1)
                      |> Set.ofArray

                  Expect.equal
                      named
                      (Set.remove "illFormedDescription" all)
                      "every named refusal the artifact can spell has a vector"

                  Expect.equal
                      (vectors "projection" |> List.map (text "method"))
                      (ContractsIdl.idl.Contracts
                       |> List.collect (fun c -> c.Methods |> List.map (fun m -> c.Name + "." + m.Id)))
                      "one a method, in declared order"

                  Expect.equal (vectors "classify").Length ContractsIdl.diffCases.Length "one a differ case")

          testCase "a valid vector is admitted by the artifact reader and earns no refusal" (fun _ ->
              for v in vectors "valid" do
                  match Artifact.parseDetailed (revisionOf v) with
                  | Ok idl -> Expect.isEmpty (Contract.faults idl) (text "name" v)
                  | Error errors -> failtestf "%s: refused: %A" (text "name" v) (errors |> List.map _.Message))

          testCase "a refusal vector is refused by the artifact reader, by the names it expects" (fun _ ->
              for v in vectors "refusal" do
                  let expected =
                      match at [ "expected"; "refusals" ] v with
                      | Some(JArr rs) -> rs |> List.map (text "name")
                      | _ -> failtestf "%s: no expected refusals" (text "name" v)

                  Expect.isNonEmpty expected (text "name" v + ": at least one refusal")
                  Expect.equal (readerRefusals (revisionOf v)) expected (text "name" v))

          testCase
              "a projection vector is the declaration the method projects to, and a capability codec reads it"
              (fun _ ->
                  let fixture =
                      ContractsCorpus.revision (text "idl" committed) (text "contracts" committed)

                  let capabilities =
                      match Artifact.parseDetailed fixture with
                      | Error errors -> failtestf "the fixture revision is refused: %A" (errors |> List.map _.Message)
                      | Ok idl ->
                          match ContractProjection.capabilities Server idl with
                          | Ok cs -> cs
                          | Error faults -> failtestf "the fixture revision did not project: %A" faults

                  for v in vectors "projection" do
                      let method = text "method" v
                      let expected = text "capability" (at [ "expected" ] v |> Option.get)
                      let projected = capabilities |> List.find (fun c -> c.Id = method)
                      Expect.equal (CapabilityCodec.encode projected) expected (method + ": the declaration")

                      match CapabilityCodec.decode expected with
                      | Ok read -> Expect.equal read projected (method + ": the codec reads it back")
                      | Error e -> failtestf "%s: the capability codec refused the declaration: %A" method e)

          testCase "a classify vector is the differ's verdict over the two revisions it names" (fun _ ->
              let idl = text "idl" committed

              for v in vectors "classify" do
                  let name = text "name" v

                  let verdictClass, rows =
                      ContractsCorpus.classify
                          (ContractsCorpus.revision idl (text "before" v))
                          (ContractsCorpus.revision idl (text "after" v))

                  let expected = at [ "expected" ] v |> Option.get
                  Expect.equal verdictClass (text "class" expected) (name + ": the class")

                  let expectedRows =
                      match Decoder.tryMember "changes" expected with
                      | Some(JArr rs) -> rs |> List.map (fun r -> text "change" r, text "class" r)
                      | _ -> failtestf "%s: no expected changes" name

                  Expect.equal rows expectedRows (name + ": the rows")

                  // And the class is the one the fixture declares for the case.
                  let declared =
                      ContractsIdl.diffCases
                      |> List.find (fun (n, _, _, _) -> "classify-" + n = name)
                      |> fun (_, _, _, c) -> c

                  Expect.equal (text "class" expected) declared (name + ": the declared class")) ]
