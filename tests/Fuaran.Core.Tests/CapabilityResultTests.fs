module Fuaran.Core.Tests.CapabilityResultTests

open Expecto
open Fuaran.Core

// Phase 642 — a capability declares its result (DECISIONS.md D253). Until this phase a capability
// stated no result, so a function registry entry and a pipeline node each stated one of their own and
// nothing compared them: the go-red pair below (`a pipeline call declaring an output …` and `a
// registry entry and a pipeline node …`) was written against the tree before the change and failed
// there, both of them. The capability's `Result` is now the one statement: the entry's result kind is
// derived from it, and a pipeline call's declared output is checked against it.

let private sg: Signature =
    { Name = "count"
      Holes =
        [ { Addr = "n"
            Name = "n"
            Kind = ValueHole(IntRange(0, 9))
            Required = true } ]
      Effect = Effect.pureDeterministic }

let private doc = Returns.Value(SlotTree(Some "doc"))

let private capWith (id: string) (result: Returns) = Capability.create id sg Server result

let private registryOf (caps: Capability list) =
    caps
    |> List.fold
        (fun r c -> r |> Result.bind (FunctionRegistry.register (FunctionRegistry.entry c)))
        (Ok FunctionRegistry.empty)
    |> function
        | Ok r -> r
        | Error e -> failwithf "register: %A" e

let private lookupOf (caps: Capability list) =
    CapabilityLookup.ofFunctionRegistry (registryOf caps)

let private call (declared: ValueSpace) =
    { Nodes = [ Invoke("call", "count", declared, [ "n", Literal "3" ]) ] }

let private decodeDetailed (s: string) =
    CapabilityCodec.decodeDetailedWith ReadPolicy.Lenient s

let private stripResult (doc: string) : string =
    match Json.parse doc with
    | Ok(JObj fs) -> Canonical.write Rendering.CrossHost (JObj(fs |> List.filter (fun (k, _) -> k <> "result")))
    | other -> failwithf "not a declaration: %A" other

let private withResult (doc: string) (result: JVal) : string =
    match Json.parse doc with
    | Ok(JObj fs) ->
        Canonical.write
            Rendering.CrossHost
            (JObj(fs |> List.map (fun (k, v) -> if k = "result" then k, result else k, v)))
    | other -> failwithf "not a declaration: %A" other

[<Tests>]
let tests =
    testList
        "Capability result"
        [ testCase "a pipeline call declaring an output its capability does not produce is refused"
          <| fun () ->
              match CapabilityPipeline.typeCheck (lookupOf [ capWith "count" doc ]) (call (IntRange(0, 9))) with
              | Error(PipelineError.PipelineOutputContradicts("call", "count", IntRange(0, 9), r)) ->
                  Expect.equal r doc "the refusal carries the capability's declared result"
              | other -> failtestf "expected PipelineOutputContradicts, got %A" other

          testCase "a registry entry and a pipeline node cannot state two results for one capability"
          <| fun () ->
              let r = registryOf [ capWith "count" doc ]
              let entry = FunctionRegistry.tryFind "count" r |> Option.get

              Expect.equal entry.ResultType (Some "doc") "the entry's kind is the capability's result"

              Expect.isError
                  (CapabilityPipeline.typeCheck (CapabilityLookup.ofFunctionRegistry r) (call AnyString))
                  "a node stating a string where the capability answers a 'doc' tree is refused"

              Expect.equal
                  (CapabilityPipeline.typeCheck (CapabilityLookup.ofFunctionRegistry r) (call (SlotTree None)))
                  (Ok())
                  "a node stating any tree admits the capability's 'doc' tree"

          testCase "a declared output may widen the result, never narrow it"
          <| fun () ->
              let lookup = lookupOf [ capWith "count" (Returns.Value(IntRange(0, 9))) ]

              Expect.equal (CapabilityPipeline.typeCheck lookup (call (IntRange(0, 9)))) (Ok()) "the result itself"

              Expect.equal (CapabilityPipeline.typeCheck lookup (call (IntRange(-5, 50)))) (Ok()) "a wider range"

              Expect.equal (CapabilityPipeline.typeCheck lookup (call AnyString)) (Ok()) "any string admits an int"

              match CapabilityPipeline.typeCheck lookup (call (IntRange(0, 5))) with
              | Error(PipelineError.PipelineOutputContradicts("call", "count", IntRange(0, 5), _)) -> ()
              | other -> failtestf "a narrower declaration was not refused: %A" other

          testCase "a capability that answers nothing has no output a pipeline node could declare"
          <| fun () ->
              let lookup = lookupOf [ capWith "count" Returns.Nothing ]

              for declared in [ AnyString; IntRange(0, 9); SlotTree None ] do
                  match CapabilityPipeline.typeCheck lookup (call declared) with
                  | Error(PipelineError.PipelineOutputContradicts("call", "count", d, Returns.Nothing)) when
                      d = declared
                      ->
                      ()
                  | other -> failtestf "a call of a nothing-capability declaring %A: %A" declared other

          testCase "the contradiction is answered before any argument is read"
          <| fun () ->
              let p =
                  { Nodes = [ Invoke("call", "count", IntRange(0, 5), [ "nope", Literal "x" ]) ] }

              match CapabilityPipeline.typeCheck (lookupOf [ capWith "count" (Returns.Value AnyString) ]) p with
              | Error(PipelineError.PipelineOutputContradicts _) -> ()
              | other -> failtestf "expected the contradiction first, got %A" other

          testCase "evaluation refuses a contradicting pipeline as EvalIllTyped, before any body runs"
          <| fun () ->
              let ran = ref false

              let result =
                  CapabilityPipeline.eval
                      (lookupOf [ capWith "count" Returns.Nothing ])
                      ()
                      []
                      id
                      (fun _ _ ->
                          ran.Value <- true
                          Ok "x")
                      (call AnyString)

              match result with
              | Error(PipelineEvalError.EvalIllTyped(PipelineError.PipelineOutputContradicts _)) ->
                  Expect.isFalse ran.Value "no body ran"
              | other -> failtestf "expected EvalIllTyped, got %A" other

          testCase "the result index files an entry under exactly its capability's tree kind"
          <| fun () ->
              let r =
                  registryOf
                      [ capWith "a" doc
                        capWith "b" (Returns.Value(SlotTree(Some "sheet")))
                        capWith "c" (Returns.Value(IntRange(0, 9)))
                        capWith "d" Returns.Nothing
                        capWith "e" (Returns.Value(SlotTree None)) ]

              let found kind =
                  FunctionRegistry.findBySignature
                      Subsumes
                      { ResultType = kind
                        Available = sg.Holes }
                      r
                  |> List.map _.Capability.Id

              Expect.equal (found (Some "doc")) [ "a" ] "doc"
              Expect.equal (found (Some "sheet")) [ "b" ] "sheet"
              Expect.equal (found None) [ "a"; "b"; "c"; "d"; "e" ] "no kind named: every entry"

              Expect.equal
                  (FunctionRegistry.enumerate r |> List.map _.ResultType)
                  [ Some "doc"; Some "sheet"; None; None; None ]
                  "derived from each capability's result"

          testCase "narrowing a function keeps its capability's result"
          <| fun () ->
              let source = FunctionRegistry.entry (capWith "count" doc)

              match FunctionRegistry.narrow "pack" (Set.ofList [ "n" ]) source with
              | Ok pack ->
                  Expect.equal pack.Capability.Result doc "the pack answers what its source answers"
                  Expect.equal pack.ResultType (Some "doc") "and is indexed under the same kind"
              | Error e -> failtestf "narrow: %A" e

          testCase "the result round-trips the wire in both spellings, and nothing is one document"
          <| fun () ->
              for result in
                  [ Returns.Nothing
                    doc
                    Returns.Value(FloatRange(0.5, 2.0))
                    Returns.Value AnyString ] do
                  let c = capWith "count" result
                  let wire = CapabilityCodec.encode c
                  Expect.equal (CapabilityCodec.decode wire) (Ok c) (sprintf "%A round-trips" result)

              Expect.stringContains
                  (CapabilityCodec.encode (capWith "count" Returns.Nothing))
                  "\"result\":{\"$type\":\"nothing\"}"
                  "returns nothing is written as the one document"

          testCase "a declaration with no result is refused, naming the member"
          <| fun () ->
              let wire = stripResult (CapabilityCodec.encode (capWith "count" doc))

              match decodeDetailed wire with
              | Error e ->
                  Expect.equal e.Code DecodeCode.MissingField "the code"
                  Expect.equal e.Path [ PathSegment.Key "result" ] "the path names the result"
              | Ok c -> failtestf "a declaration without a result decoded: %A" c

          testCase "a string is not a spelling of nothing, and an unknown tag is refused naming nothing"
          <| fun () ->
              let wire = CapabilityCodec.encode (capWith "count" doc)

              match decodeDetailed (withResult wire (JStr "nothing")) with
              | Error e ->
                  Expect.equal e.Code DecodeCode.WrongKind "a string where a document belongs"
                  Expect.equal e.Path [ PathSegment.Key "result" ] "refused at the result"
              | Ok c -> failtestf "a string decoded: %A" c

              match decodeDetailed (withResult wire (Canonical.typed "void" [])) with
              | Error e ->
                  Expect.equal e.Code DecodeCode.UnknownTag "an unknown tag"
                  Expect.stringContains (DecodeError.describe e) "'nothing'" "the refusal names nothing"
              | Ok c -> failtestf "an unknown tag decoded: %A" c

          testCase "an ill-formed result is refused at registration, and on the wire at the result"
          <| fun () ->
              let empty = capWith "count" (Returns.Value(Enum []))
              let nonFinite = capWith "count" (Returns.Value(FloatRange(0.0, infinity)))

              Expect.equal
                  (CapabilityRegistry.register empty CapabilityRegistry.empty |> Result.map ignore)
                  (Error(IllFormedCapability("count", EmptyResult(Enum []))))
                  "an empty result space"

              Expect.equal
                  (CapabilityRegistry.register nonFinite CapabilityRegistry.empty
                   |> Result.map ignore)
                  (Error(IllFormedCapability("count", NonFiniteResult)))
                  "a non-finite bound"

              Expect.equal
                  (FunctionRegistry.register (FunctionRegistry.entry empty) FunctionRegistry.empty
                   |> Result.map ignore)
                  (Error(IllFormedCapability("count", EmptyResult(Enum []))))
                  "the function registry runs the same gate"

              let wire =
                  withResult (CapabilityCodec.encode (capWith "count" doc)) (SpaceCodec.toJson (Enum []))

              match decodeDetailed wire with
              | Error e ->
                  Expect.equal e.Code DecodeCode.OutOfRange "the code"
                  Expect.equal e.Path [ PathSegment.Key "result" ] "the path names the result"
              | Ok c -> failtestf "an empty result decoded: %A" c

          testCase "the strict reader polices the result's members"
          <| fun () ->
              let wire =
                  withResult
                      (CapabilityCodec.encode (capWith "count" doc))
                      (Canonical.typed "nothing" [ "extra", JInt 1 ])

              match CapabilityCodec.decodeDetailedWith ReadPolicy.Strict wire with
              | Error e -> Expect.equal e.Code DecodeCode.UndeclaredMember "an undeclared member"
              | Ok c -> failtestf "the strict reader admitted an undeclared member: %A" c

              Expect.isOk
                  (CapabilityCodec.decodeDetailedWith ReadPolicy.Lenient wire)
                  "the lenient reader reads past it" ]
