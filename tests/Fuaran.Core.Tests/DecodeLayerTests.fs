module Fuaran.Core.Tests.DecodeLayerTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

// ============================================================================
//  Phase 310 — the typed decode layer: a refusal is a code and a path.
//
//  Five things are held here. The combinators answer the code and the path they
//  document, the three-valued optional rule included. The string forms are
//  byte-identical forwards. The codecs that moved onto the layer answer typed
//  refusals whose paths RESOLVE in the document (`Corpus.refusalLaws`, run over
//  every codec with a typed entry point). The UI host's §4d decode codes are an
//  instance of the closed set, with no residue. And the decode REJECT family,
//  `conformance/decode/`, is the vectors host twins certify against — each one
//  an input and the code and path computed by calling the reference decoder,
//  emitted, never hand-edited:
//      dotnet run --project tools/Fuaran.Core.Tools -- render decode [<dir>]
// ============================================================================

let private key (k: string) = PathSegment.Key k

/// A law that held, or a failed test carrying its counterexample.
let private holds (r: Result<unit, string>) : unit =
    match r with
    | Ok() -> ()
    | Error m -> failtest m

let private index (i: int) = PathSegment.Index i

let private parse (s: string) : JVal =
    match Json.parse s with
    | Ok v -> v
    | Error e -> failwithf "fixture JSON did not parse: %s" e

/// The code and path of a refusal, or a failed test naming what came back instead.
let private refusal (r: Result<'T, DecodeError>) : DecodeCode * PathSegment list =
    match r with
    | Error e -> e.Code, e.Path
    | Ok v -> failtestf "expected a refusal, got %A" v


// ---- codec fixtures for the refusal law ----

let private intHole (addr: string) : SigEntry =
    { Addr = addr
      Name = addr
      Kind = ValueHole(IntRange(0, 9))
      Required = true }

let private capabilities: Capability list =
    let effect =
        { Host = Pure
          Determinism = Effect.deterministic }

    [ { Id = "sum"
        Signature =
          { Name = "sum"
            Holes = [ intHole "a"; intHole "b" ]
            Effect = effect }
        Placement = Server }
      { Id = "label"
        Signature =
          { Name = "label"
            Holes =
              [ { intHole "text" with
                    Kind = ValueHole(Enum [ "x"; "y" ]) } ]
            Effect = effect }
        Placement = ClientIsland Fable } ]

/// Every `RootDecodeCode` with its case name, in declaration order (Phase 443).
let private rootCases: (RootDecodeCode * string) list =
    Microsoft.FSharp.Reflection.FSharpType.GetUnionCases typeof<RootDecodeCode>
    |> Array.map (fun c -> Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(c, [||]) :?> RootDecodeCode, c.Name)
    |> List.ofArray

/// One value of every kind, and an object carrying the member `a` — what a refusal at the value is
/// raised over.
let private atValues: JVal list =
    [ JBool true
      JInt 1
      JStr "s"
      JArr [ JInt 2 ]
      JObj [ "a", JObj [] ]
      JObj [] ]

[<Tests>]
let tests =
    testList
        "Decode layer (Phase 310)"
        [ testList
              "combinators"
              [ testCase "field / optField / fieldOr read every kind, and the optional rule is three-valued"
                <| fun _ ->
                    let doc = parse """{"s":"x","i":3,"f":1.5,"b":true,"l":[1,2],"o":{"k":1}}"""

                    Expect.equal (Decoder.field "s" Decoder.str doc) (Ok "x") "str"
                    Expect.equal (Decoder.field "i" Decoder.int doc) (Ok 3) "int"
                    Expect.equal (Decoder.field "f" Decoder.float doc) (Ok 1.5) "float"
                    Expect.equal (Decoder.field "i" Decoder.float doc) (Ok 3.0) "a whole number reads as float"
                    Expect.equal (Decoder.field "b" Decoder.bool doc) (Ok true) "bool"
                    Expect.equal (Decoder.field "l" (Decoder.list Decoder.int) doc) (Ok [ 1; 2 ]) "list"
                    Expect.equal (Decoder.field "o" Decoder.obj doc) (Ok [ "k", JInt 1 ]) "obj"

                    // absent -> the fallback, for every kind
                    let empty = JObj []
                    Expect.equal (Decoder.fieldOr "s" "d" Decoder.str empty) (Ok "d") "str fallback"
                    Expect.equal (Decoder.fieldOr "i" 7 Decoder.int empty) (Ok 7) "int fallback"
                    Expect.equal (Decoder.fieldOr "f" 0.5 Decoder.float empty) (Ok 0.5) "float fallback"
                    Expect.equal (Decoder.fieldOr "b" false Decoder.bool empty) (Ok false) "bool fallback"
                    Expect.equal (Decoder.fieldOr "l" [] (Decoder.list Decoder.int) empty) (Ok []) "list fallback"
                    Expect.equal (Decoder.fieldOr "o" [] Decoder.obj empty) (Ok []) "obj fallback"
                    Expect.equal (Decoder.optField "s" Decoder.str empty) (Ok None) "absent is Ok None"

                    // present and ill-typed -> the refusal, never the fallback
                    for name in [ "s"; "i"; "f"; "b"; "l"; "o" ] do
                        let wrong = JObj [ name, JArr [ JStr "?" ] ]

                        let r =
                            match name with
                            | "s" -> Decoder.fieldOr name "" Decoder.str wrong |> Result.map ignore
                            | "i" -> Decoder.fieldOr name 0 Decoder.int wrong |> Result.map ignore
                            | "f" -> Decoder.fieldOr name 0.0 Decoder.float wrong |> Result.map ignore
                            | "b" -> Decoder.fieldOr name false Decoder.bool wrong |> Result.map ignore
                            | "l" -> Decoder.fieldOr name [] (Decoder.list Decoder.int) wrong |> Result.map ignore
                            | _ ->
                                Decoder.fieldOr name [] Decoder.obj (JObj [ name, JStr "?" ])
                                |> Result.map ignore

                        let code, path = refusal r
                        Expect.equal code DecodeCode.WrongKind (sprintf "%s: an ill-typed member is refused" name)
                        Expect.equal (List.head path) (key name) (sprintf "%s: the path names the member" name)

                testCase "a refusal's path grows as it leaves each member and item"
                <| fun _ ->
                    let doc = parse """{"a":{"items":[{"n":1},{"n":"two"}]}}"""

                    let d =
                        Decoder.field "a" (Decoder.field "items" (Decoder.list (Decoder.field "n" Decoder.int)))

                    let code, path = refusal (d doc)
                    Expect.equal code DecodeCode.WrongKind "the kind is wrong"
                    Expect.equal path [ key "a"; key "items"; index 1; key "n" ] "the path runs from the root"
                    Expect.equal (DecodePath.render path) "$[\"a\"][\"items\"][1][\"n\"]" "rendered"
                    Expect.equal (DecodePath.resolve path doc) (Some(JStr "two")) "and it resolves"

                    let code2, path2 =
                        refusal (Decoder.field "a" (Decoder.field "missing" Decoder.int) doc)

                    Expect.equal
                        (code2, path2)
                        (DecodeCode.MissingField, [ key "a"; key "missing" ])
                        "a missing member is named"

                testCase "a refusal's sentence is the projection of its code, expectation and detail (Phase 520)"
                <| fun _ ->
                    let cases = [ "a", Decoder.field "n" Decoder.int ]

                    match Decoder.tagDispatch "$type" cases (parse """{"$type":"b"}""") with
                    | Error e ->
                        Expect.equal
                            (e.Code, e.Path, e.Expected)
                            (DecodeCode.UnknownTag, [ key "$type" ], "one of 'a'")
                            "the code, the path and the expectation are the contract"

                        Expect.equal
                            e.Message
                            (DecodeError.sentence e.Code e.Expected "$type 'b'")
                            "the sentence is the projection"
                    | Ok v -> failtestf "an unknown tag must be refused: %A" v

                    // every refusal built through `make` carries its code's one sentence, and the
                    // sentence carries no path, so a refusal carried outward keeps it
                    for code in DecodeError.codes do
                        let e = DecodeError.make code "an x" "a y"
                        Expect.equal e.Message (DecodeError.sentence code "an x" "a y") (DecodeError.codeName code)

                        Expect.equal
                            (DecodeError.under (key "k") e).Message
                            e.Message
                            (DecodeError.codeName code + " under a step")

                    // the sentences differ by code: no two codes share a projection of one input
                    Expect.equal
                        (DecodeError.codes
                         |> List.map (fun c -> DecodeError.sentence c "an x" "a y")
                         |> List.distinct
                         |> List.length)
                        (List.length DecodeError.codes)
                        "one sentence form per code"

                testCase "at navigates and refuses where a step names nothing"
                <| fun _ ->
                    let doc = parse """{"xs":[10,20]}"""
                    Expect.equal (Decoder.at [ key "xs"; index 1 ] Decoder.int doc) (Ok 20) "found"

                    Expect.equal
                        (refusal (Decoder.at [ key "xs"; index 5 ] Decoder.int doc))
                        (DecodeCode.OutOfRange, [ key "xs" ])
                        "past the end"

                    Expect.equal
                        (refusal (Decoder.at [ key "ys" ] Decoder.int doc))
                        (DecodeCode.MissingField, [ key "ys" ])
                        "absent"

                    Expect.equal
                        (refusal (Decoder.at [ key "xs"; key "q" ] Decoder.int doc))
                        (DecodeCode.WrongKind, [ key "xs" ])
                        "wrong kind"

                testCase "kindDispatch names every known tag on a miss, at the tag"
                <| fun _ ->
                    let d = Decoder.kindDispatch [ "a", Decoder.succeed 1; "b", Decoder.succeed 2 ]
                    Expect.equal (d (parse """{"kind":"b"}""")) (Ok 2) "dispatched"

                    match d (parse """{"kind":"z"}""") with
                    | Error e ->
                        Expect.equal e.Code DecodeCode.UnknownTag "unknown"
                        Expect.equal e.Path [ key "kind" ] "at the tag"
                        Expect.stringContains e.Expected "'a', 'b'" "names the known tags"
                        Expect.stringContains e.Message "'a', 'b'" "and says so"
                    | Ok v -> failtestf "dispatched %A" v

                testCase "all answers every refusal; sequence answers the first"
                <| fun _ ->
                    let doc = parse """{"a":"x","b":"y","c":3}"""

                    let ds =
                        [ Decoder.field "a" Decoder.int
                          Decoder.field "b" Decoder.int
                          Decoder.field "c" Decoder.int ]

                    match Decoder.all ds doc with
                    | Error es -> Expect.equal (es |> List.map _.Path) [ [ key "a" ]; [ key "b" ] ] "both faults"
                    | Ok v -> failtestf "accepted %A" v

                    Expect.equal (refusal (Decoder.sequence ds doc)) (DecodeCode.WrongKind, [ key "a" ]) "the first"

                    match Decoder.listAll Decoder.int (parse """[1,"x",2,"y"]""") with
                    | Error es -> Expect.equal (es |> List.map _.Path) [ [ index 1 ]; [ index 3 ] ] "every item"
                    | Ok v -> failtestf "accepted %A" v

                testCase "the strict policy refuses an undeclared member, naming it and the declared ones"
                <| fun _ ->
                    let d = Decoder.closed [ "a"; "b" ] (Decoder.field "a" Decoder.int)
                    Expect.equal (d (parse """{"a":1,"b":2}""")) (Ok 1) "declared members pass"

                    match d (parse """{"a":1,"c":2,"d":3}""") with
                    | Error e ->
                        Expect.equal (e.Code, e.Path) (DecodeCode.UndeclaredMember, [ key "c" ]) "the first undeclared"
                        Expect.stringContains e.Expected "'a', 'b'" "names the declared members"
                    | Ok v -> failtestf "accepted %A" v

                    Expect.equal
                        (Decoder.undeclared [ "a" ] (parse """{"a":1,"c":2,"d":3}""") |> List.map _.Path)
                        [ [ key "c" ]; [ key "d" ] ]
                        "every undeclared member"

                testCase "bounds, ranges, enumerations and text"
                <| fun _ ->
                    Expect.equal
                        (refusal (Decoder.boundedList 2 Decoder.int (parse "[1,2,3]")))
                        (DecodeCode.LimitExceeded, [])
                        "items"

                    Expect.equal (refusal (Decoder.intRange 0 5 (JInt 9))) (DecodeCode.OutOfRange, []) "range"
                    Expect.equal (Decoder.oneOf [ "x", 1; "y", 2 ] (JStr "y")) (Ok 2) "oneOf"

                    Expect.equal
                        (refusal (Decoder.oneOf [ "x", 1 ] (JStr "q")))
                        (DecodeCode.UnknownTag, [])
                        "oneOf miss"

                    Expect.equal
                        (refusal (Decoder.parse ReadOptions.strict "{"))
                        (DecodeCode.InvalidJson, [])
                        "not JSON"

                    Expect.equal
                        (refusal (
                            Decoder.parse
                                { ReadOptions.strict with
                                    Limits = JsonLimits.depthOnly 3 }
                                "[[[[1]]]]"
                        ))
                        (DecodeCode.LimitExceeded, [ PathSegment.Index 0; PathSegment.Index 0; PathSegment.Index 0 ])
                        "the nesting cap is a limit, at the path of the container opened at it"

                testCase "every code has one wire name, and the names read back"
                <| fun _ ->
                    for c in DecodeError.codes do
                        Expect.equal (DecodeError.tryCodeOfName (DecodeError.codeName c)) (Some c) (sprintf "%A" c)

                    Expect.equal
                        (DecodeError.codes
                         |> List.map DecodeError.codeName
                         |> List.distinct
                         |> List.length)
                        (List.length DecodeError.codes)
                        "distinct names"

                testCase
                    "fail takes exactly the codes a refusal at the value can carry, and each resolves there (Phase 443)"
                <| fun _ ->
                    // The go-red is the type: `Decoder.fail DecodeCode.MissingField ...` no longer
                    // compiles (FS0001), and this pins the case set, so restoring either case is red.
                    let cases = rootCases

                    Expect.equal
                        (cases |> List.map snd)
                        (DecodeError.codes
                         |> List.filter (fun c -> c <> DecodeCode.InvalidJson && c <> DecodeCode.MissingField)
                         |> List.map DecodeError.codeName)
                        "every code but InvalidJson and MissingField, in declaration order"

                    for rc, name in cases do
                        for v in atValues do
                            match (Decoder.fail rc "e" "m": Decoder<unit>) v with
                            | Error e ->
                                Expect.equal (DecodeError.codeName e.Code) name "fail raises the code it is handed"
                                Expect.equal e.Path [] "at the value"
                                Expect.isTrue (DecodeError.resolvesIn v e) (sprintf "%s resolves in %A" name v)

                                match (Decoder.field "a" (Decoder.fail rc "e" "m"): Decoder<unit>) v with
                                | Error e2 when e2.Path = [ key "a" ] ->
                                    Expect.isTrue
                                        (DecodeError.resolvesIn v e2)
                                        (sprintf "%s resolves under a member" name)
                                | _ -> ()
                            | Ok() -> failtest "fail answered a value"

                testCase
                    "andThen raises its function's refusal at the value, for every root code, and it resolves (Phase 453)"
                <| fun _ ->
                    // The go-red is the type: a function answering a `DecodeError` — a path of its
                    // own, or a `MissingField` / `InvalidJson` — no longer compiles as `andThen`'s
                    // argument (FS0001); its refusal is the triple `fail` takes.
                    for rc, name in rootCases do
                        for v in atValues do
                            let refuse: Decoder<unit> =
                                Decoder.json |> Decoder.andThen (fun _ -> Error(rc, "e", "m"))

                            match refuse v with
                            | Error e ->
                                Expect.equal (DecodeError.codeName e.Code) name "andThen raises the code it is handed"
                                Expect.equal e.Path [] "at the value"

                                Expect.equal
                                    (e.Expected, e.Message)
                                    ("e", DecodeError.sentence e.Code "e" "m")
                                    "its expectation untouched, its sentence the code's projection of its detail"

                                Expect.equal
                                    (Error e)
                                    ((Decoder.fail rc "e" "m": Decoder<unit>) v)
                                    "exactly fail's refusal"

                                Expect.isTrue (DecodeError.resolvesIn v e) (sprintf "%s resolves in %A" name v)

                                match Decoder.field "a" refuse v with
                                | Error e2 when e2.Path = [ key "a" ] ->
                                    Expect.isTrue
                                        (DecodeError.resolvesIn v e2)
                                        (sprintf "%s resolves under a member" name)
                                | _ -> ()
                            | Ok() -> failtest "andThen answered a value"

                    // A refusal of the inner decoder passes through untouched, its path included.
                    Expect.equal
                        (refusal (
                            (Decoder.field "a" Decoder.int |> Decoder.andThen (fun i -> Ok(i + 1))) (
                                JObj [ "a", JStr "x" ]
                            )
                        ))
                        (DecodeCode.WrongKind, [ key "a" ])
                        "the inner refusal keeps its path"

                testCase
                    "a check that points inside the value goes through bind and at, which walk the document (Phase 453)"
                <| fun _ ->
                    // The shape `andThen` no longer admits — a function naming a path of its own — is
                    // `bind` to `at <path> (fail ...)`: the path is walked in the document, so one the
                    // document does not hold is refused where the walk stops, and still resolves.
                    let pointAt (path: PathSegment list) : Decoder<unit> =
                        Decoder.json
                        |> Decoder.bind (fun _ -> Decoder.at path (Decoder.fail RootDecodeCode.OutOfRange "e" "m"))

                    let doc = JObj [ "args", JArr [ JInt 1; JInt 2 ] ]

                    for path in
                        [ [ key "args"; index 1 ]
                          [ key "args"; index 5 ]
                          [ key "absent" ]
                          [ key "args"; key "x" ] ] do
                        match pointAt path doc with
                        | Error e -> Expect.isTrue (DecodeError.resolvesIn doc e) (sprintf "%A resolves" path)
                        | Ok() -> failtest "answered a value"

                    Expect.equal
                        (refusal (pointAt [ key "args"; index 1 ] doc))
                        (DecodeCode.OutOfRange, [ key "args"; index 1 ])
                        "a held path"

                    // Core's one caller that refused below the value, migrated to that shape: a repeated
                    // address is still refused at its second occurrence.
                    match
                        CapabilityCodec.decodeInvocationDetailedWith
                            ReadPolicy.Lenient
                            (CapabilityCodec.encodeInvocation "n" [ "n", "1"; "n", "2" ])
                    with
                    | Error e ->
                        Expect.equal (e.Code, e.Path) (DecodeCode.OutOfRange, [ key "args"; index 1 ]) "at args[1]"

                        Expect.equal
                            e.Message
                            (DecodeError.sentence e.Code e.Expected "argument 'n' is given more than once")
                            "its sentence, projected from the detail"
                    | Ok v -> failtestf "accepted %A" v

                testCase "a path round-trips its wire form"
                <| fun _ ->
                    let p = [ key "a\"b"; index 0; key "" ]
                    Expect.equal (DecodePath.ofJson (DecodePath.toJson p)) (Some p) "round-trip"
                    Expect.equal (DecodePath.ofJson (JArr [ JBool true ])) None "a non-step is refused" ]

          testList
              "the string forms are forwards"
              [ testCase "each forward answers the sentence it always answered"
                <| fun _ ->
                    let doc = parse """{"s":"x","i":1}"""

                    Expect.equal
                        (Fuaran.Core.Decoder.describing (Decoder.field "zz" Decoder.json) doc)
                        (Error "missing property: zz")
                        "getProp missing"

                    Expect.equal
                        (Fuaran.Core.Decoder.describing (Decoder.field "zz" Decoder.json) (JInt 1))
                        (Error "expected object, got int")
                        "getProp non-object"

                    Expect.equal
                        (Fuaran.Core.Decoder.describing (Decoder.field "i" Decoder.str) doc)
                        (Error "expected string, got int")
                        "strField"

                    Expect.equal
                        (Fuaran.Core.Decoder.describing (Decoder.field "s" Decoder.int) doc)
                        (Error "expected int, got string")
                        "intField"

                    Expect.equal
                        (Fuaran.Core.Decoder.describing Decoder.bool (JStr "x"))
                        (Error "expected bool, got string")
                        "asBool"

                    Expect.equal
                        (Fuaran.Core.Decoder.describing Decoder.float (JStr "x"))
                        (Error "expected number, got string")
                        "asFloat"

                    Expect.equal
                        (Fuaran.Core.Decoder.describing (Decoder.field "kind" Decoder.str) doc)
                        (Error "missing property: kind")
                        "kindOf"

                    Expect.equal (Decoder.tryMember "s" doc) (Some(JStr "x")) "tryMember"

                testCase "the IDL interpreter's string forms carry the typed refusal's sentence"
                <| fun _ ->
                    let idl = MiniIdl.miniIdl

                    for wire in [ "{"; """{"id":"n"}"""; """{"id":"n","kind":{"$type":"Nope"}}"""; "[]" ] do
                        match Decode.decode idl wire, Decode.decodeDetailed idl wire with
                        | Error m, Error e -> Expect.equal m e.Message wire
                        | a, b -> failtestf "%s: %A vs %A" wire a b ]

          testList
              "typed refusals through the codecs"
              [ testCase "the IDL interpreter reports the path through the vocabulary"
                <| fun _ ->
                    let idl = MiniIdl.miniIdl
                    let kind = idl.Kinds |> List.head

                    match Decode.decodeDetailed idl """{"id":"n","kind":{"$type":"Nope"}}""" with
                    | Error e ->
                        Expect.equal
                            (e.Code, e.Path)
                            (DecodeCode.UnknownTag, [ key "kind"; key idl.Wire.Discriminator ])
                            "unknown kind"

                        Expect.stringContains e.Expected ("'" + kind.Tag + "'") "names the known kinds"
                    | Ok v -> failtestf "decoded %A" v

                    Expect.equal
                        (refusal (Decode.decodeDetailed idl """{"kind":{}}"""))
                        (DecodeCode.MissingField, [ key "id" ])
                        "no id"

                    Expect.equal (refusal (Decode.decodeDetailed idl "[]")) (DecodeCode.WrongKind, []) "not a node"
                    Expect.equal (refusal (Decode.decodeDetailed idl "{")) (DecodeCode.InvalidJson, []) "not JSON"

                testCase "the capability codec: an undeclared member under Strict is refused at that member"
                <| fun _ ->
                    let c = List.head capabilities

                    let tampered =
                        match CapabilityCodec.encodeJson c with
                        | JObj fs ->
                            JObj(
                                fs
                                |> List.map (fun (k, v) ->
                                    match k, v with
                                    | "signature", JObj s -> k, JObj(s @ [ "actor", JStr "model" ])
                                    | _ -> k, v)
                            )
                        | other -> other

                    match CapabilityCodec.decodeJsonDetailedWith ReadPolicy.Strict tampered with
                    | Error e ->
                        Expect.equal
                            (e.Code, e.Path)
                            (DecodeCode.UndeclaredMember, [ key "signature"; key "actor" ])
                            "at the member"

                        Expect.equal
                            (CapabilityCodec.decodeJsonWith ReadPolicy.Strict tampered)
                            (Error e.Message)
                            "the string form is the same refusal's sentence"
                    | Ok v -> failtestf "decoded %A" v

                    Expect.isOk
                        (CapabilityCodec.decodeJsonDetailedWith ReadPolicy.Lenient tampered)
                        "Lenient reads past it"

                testCase "the proposal reader refuses a present, ill-typed optional member it used to read as absent"
                <| fun _ ->
                    let doc = """{"id":"p","evidence":[{"signal":"s","count":"seven"}]}"""

                    Expect.equal
                        (refusal (Proposal.parseDetailed doc))
                        (DecodeCode.WrongKind, [ key "evidence"; index 0; key "count" ])
                        "the count is not a number"

                    Expect.equal
                        (refusal (Proposal.parseDetailed """{"delta":[]}"""))
                        (DecodeCode.MissingField, [ key "id" ])
                        "no id"

                    Expect.equal
                        (Proposal.parse """{"delta":[]}""")
                        (Error(DecodeError.sentence DecodeCode.MissingField "a member 'id'" "id"))
                        "the string form is the refusal's projected sentence"

                testCase "the artifact reader reports the path into idl.json"
                <| fun _ ->
                    let text = Artifact.render MiniIdl.miniIdl

                    let broken =
                        match parse text with
                        | JObj fs ->
                            JObj(
                                fs
                                |> List.map (fun (k, v) ->
                                    match k, v with
                                    | "kinds", JArr(JObj first :: rest) ->
                                        k, JArr(JObj(first |> List.filter (fun (n, _) -> n <> "category")) :: rest)
                                    | _ -> k, v)
                            )
                        | other -> other

                    Expect.equal
                        (Artifact.ofJsonDetailed broken |> Result.mapError List.exactlyOne |> refusal)
                        (DecodeCode.MissingField, [ key "kinds"; index 0; key "category" ])
                        "the first kind lacks its category"

                    // Phase 520: the string form is the refusal rendered — code, path and sentence
                    Expect.equal
                        (Artifact.ofJson broken)
                        (Error(
                            DecodeError.render (
                                Decoder.missing "category" |> DecodeError.within [ key "kinds"; index 0 ]
                            )
                        ))
                        "the string form renders the code and the path"

                testCase "the pipeline codec reports the path to the node at fault"
                <| fun _ ->
                    let doc =
                        """{"nodes":[{"$type":"source","id":"s","dataRef":"d","outputType":{"$type":"anyString"}},{"$type":"invoke","id":"i","capabilityId":"c","outputType":{"$type":"anyString"},"args":[{"addr":"a","source":{"$type":"elsewhere"}}]}]}"""

                    match CapabilityPipeline.decodeDetailed doc with
                    | Error e ->
                        Expect.equal e.Code DecodeCode.UnknownTag "unknown arg source"

                        Expect.equal
                            e.Path
                            [ key "nodes"; index 1; key "args"; index 0; key "source"; key "$type" ]
                            "the path"

                        Expect.equal e.Expected "one of 'literal', 'fromNode'" "naming the known sources"
                        Expect.equal e.Message (DecodeError.sentence e.Code e.Expected "$type 'elsewhere'") "projected"

                        Expect.equal
                            (CapabilityPipeline.decode doc)
                            (Error e.Message)
                            "the string form is that sentence"
                    | Ok v -> failtestf "decoded %A" v ]

          testList
              "the refusal law — every refusal's path resolves in the document"
              [ testCase "the IDL interpreter, over sampled nodes of the reference vocabulary"
                <| fun _ ->
                    let idl = MiniIdl.miniIdl
                    let tags = idl.Kinds |> List.map _.Tag

                    let encode (v: IdlValue) =
                        match Encode.encode idl v with
                        | Ok w -> parse w
                        | Error e -> failwithf "encode: %s" e

                    let gen (seed: int) =
                        match Sample.trySampleNodes idl tags seed 1 with
                        | Ok(v :: _) -> v
                        | Ok [] -> failwith "the sampler drew no vector"
                        | Error r -> failwithf "the sampler refused: %s" r.Describe

                    let decode: Decoder<IdlValue> = fun j -> Decode.decodeDetailed idl (Json.render j)

                    holds (Corpus.refusalLaws encode decode gen 310 12)

                testCase "the capability codec under Strict"
                <| fun _ ->
                    let arr = List.toArray capabilities

                    holds (
                        Corpus.refusalLaws
                            CapabilityCodec.encodeJson
                            (CapabilityCodec.decodeJsonDetailedWith ReadPolicy.Strict)
                            (fun seed -> arr[abs seed % arr.Length])
                            0
                            arr.Length
                    )

                testCase "the pipeline codec"
                <| fun _ ->
                    let pipeline =
                        { Nodes =
                            [ Source("s", "data", AnyString)
                              Invoke("i", "sum", IntRange(0, 9), [ "a", Literal "1"; "b", FromNode "s" ]) ] }

                    holds (
                        Corpus.refusalLaws
                            (fun p -> parse (CapabilityPipeline.encode p))
                            (fun j -> CapabilityPipeline.decodeDetailed (Json.render j))
                            (fun _ -> pipeline)
                            0
                            1
                    )

                testCase "the artifact reader, over the reference vocabulary"
                <| fun _ ->
                    let text = Artifact.render MiniIdl.miniIdl

                    holds (
                        Corpus.refusalLaws
                            (fun (_: Idl) -> parse text)
                            (Artifact.ofJsonDetailed >> Result.mapError List.head)

                            (fun _ -> MiniIdl.miniIdl)
                            0
                            1
                    )

                testCase "the law has teeth: a refusal at a path the document does not hold is caught"
                <| fun _ ->
                    let lying: Decoder<int> =
                        fun j ->
                            match j with
                            | JInt i -> Ok i
                            | _ ->
                                Error
                                    { DecodeError.make DecodeCode.WrongKind "int" "no" with
                                        Path = [ key "nowhere" ] }

                    match Corpus.refusalLaws JInt lying id 1 1 with
                    | Error m -> Expect.stringContains m "does not resolve" "named"
                    | Ok() -> failtest "a refusal at a path the document does not hold passed the law"

                testCase "reject vectors pin code and path"
                <| fun _ ->
                    let decode =
                        Decoder.parse ReadOptions.strict >> Result.bind (Decoder.field "a" Decoder.int)

                    let outcomes =
                        Corpus.runRejects
                            decode
                            [ { Label = "missing"
                                Input = "{}"
                                RefusedAs = DecodeCode.MissingField
                                At = [ key "a" ] }
                              { Label = "wrong path claimed"
                                Input = """{"a":"x"}"""
                                RefusedAs = DecodeCode.WrongKind
                                At = [] } ]

                    Expect.equal (outcomes |> List.map _.Passed) [ true; false ] "a wrong path is a failed vector" ]

          testList
              "the UI host's decode contract is an instance of this one"
              [ testCase "every §4d code maps onto the closed set, with no residue"
                <| fun _ ->
                    // The UI host's §4d decode codes, as that host publishes them, and the code of
                    // this set each one refines. The UI host draws two distinctions finer than this
                    // set (an unknown node kind and an unknown case are both `UnknownTag`; an empty
                    // node id is an `OutOfRange` of the id's domain), which is what "an instance of"
                    // means here; it draws none coarser.
                    let uiCodes =
                        [ "INVALID_JSON", DecodeCode.InvalidJson
                          "MISSING_FIELD", DecodeCode.MissingField
                          "WRONG_TYPE", DecodeCode.WrongKind
                          "UNKNOWN_DU_CASE", DecodeCode.UnknownTag
                          "WRONG_NODE_KIND", DecodeCode.UnknownTag
                          "EMPTY_NODE_ID", DecodeCode.OutOfRange
                          "LIMIT_EXCEEDED", DecodeCode.LimitExceeded
                          "KIND_NOT_ADMITTED", DecodeCode.NotAdmitted ]

                    Expect.equal (uiCodes |> List.map fst |> List.distinct |> List.length) 8 "all eight §4d codes"

                    for ui, core in uiCodes do
                        Expect.contains
                            DecodeError.codes
                            core
                            (sprintf "%s maps onto %s" ui (DecodeError.codeName core))

                    // The UI host's path spelling (`$.a[0].b`) is a rendering of the same steps.
                    let uiPath (path: PathSegment list) =
                        "$"
                        + (path
                           |> List.map (function
                               | PathSegment.Key k -> "." + k
                               | PathSegment.Index i -> "[" + string i + "]")
                           |> String.concat "")

                    Expect.equal (uiPath [ key "children"; index 0; key "id" ]) "$.children[0].id" "the same steps" ]

          testList
              "the decode reject family (conformance/decode/)"
              [ testCase "every authored vector gets the outcome it is for"
                <| fun _ ->
                    for vec in DecodeRejectCorpus.vectors do
                        match vec.Intent, DecodeRejectCorpus.outcome vec with
                        | DecodeRejectCorpus.Refuse(code, path), Error got ->
                            Expect.equal got (code, path) (sprintf "%s refuses as authored" vec.Name)
                        | DecodeRejectCorpus.Accept, Ok() -> ()
                        | intent, got -> failtestf "%s: authored for %A, the decoder answered %A" vec.Name intent got

                    let names = DecodeRejectCorpus.vectors |> List.map _.Name
                    Expect.equal (List.distinct names) names "vector names are unique"

                testCase "the family carries every code"
                <| fun _ ->
                    let carried =
                        DecodeRejectCorpus.vectors
                        |> List.choose (fun vec ->
                            match vec.Intent with
                            | DecodeRejectCorpus.Refuse(code, _) -> Some code
                            | DecodeRejectCorpus.Accept -> None)
                        |> Set.ofList

                    for c in DecodeError.codes do
                        Expect.isTrue (carried.Contains c) (sprintf "a vector refuses with %s" (DecodeError.codeName c))

                testCase "every refusal's path resolves in its input"
                <| fun _ ->
                    for vec in DecodeRejectCorpus.vectors do
                        match DecodeRejectCorpus.outcome vec, Json.parse vec.Input with
                        | Error(code, path), Ok doc ->
                            let e = DecodeError.make code "" ""

                            Expect.isTrue
                                (DecodeError.resolvesIn doc { e with Path = path })
                                (sprintf "%s: %s resolves" vec.Name (DecodePath.render path))
                        | _ -> ()

                // Phase 465 — the limits vectors pin every limit from both sides: for each of the
                // five, a vector at the bound is accepted and one past it is the breach of THAT
                // limit (read through `Json.read` under the vector's limits, which names it) at the path the
                // vector pins — so a host whose unit is wrong (UTF-16 units, bytes) fails the
                // astral and CJK vectors by name rather than by accident.
                testCase "every limit is pinned at its bound and one past it, and a breach names its limit"
                <| fun _ ->
                    let limitVectors =
                        DecodeRejectCorpus.vectors
                        |> List.choose (fun v -> v.Limits |> Option.map (fun l -> v, l))

                    let breached =
                        [ for v, l in limitVectors do
                              match v.Intent, Json.read { ReadOptions.strict with Limits = l } v.Input with
                              | DecodeRejectCorpus.Refuse(DecodeCode.LimitExceeded, path),
                                Error(JsonRefusal.LimitExceeded b) ->
                                  Expect.equal b.Path path (sprintf "%s: the breach's path" v.Name)

                                  Expect.equal
                                      b.Declared
                                      (JsonLimit.declared l b.Limit)
                                      (sprintf "%s: the declared value" v.Name)

                                  yield b.Limit
                              | DecodeRejectCorpus.Refuse(DecodeCode.LimitExceeded, _), other ->
                                  failtestf "%s: expected a breach, the parse gave %A" v.Name other
                              | _ -> () ]
                        |> Set.ofList

                    let acceptedUnder =
                        limitVectors
                        |> List.filter (fun (v, _) -> v.Intent = DecodeRejectCorpus.Accept)
                        |> List.length

                    for l in
                        [ JsonLimit.Depth
                          JsonLimit.StringLength
                          JsonLimit.ArrayLength
                          JsonLimit.ObjectMembers
                          JsonLimit.DocumentBytes ] do
                        Expect.isTrue (breached.Contains l) (sprintf "a vector breaches %s" (JsonLimit.name l))

                    Expect.isGreaterThanOrEqual acceptedUnder 5 "a vector at each bound is accepted"

                // Phase 481 — the off-by-one go-red, kept in the suite: a host whose bound is one
                // unit early (it refuses at the bound) or one unit late (it reads one past it)
                // answers every vector exactly as the reference does under that limit shifted by
                // one, so each such host is simulated here and the family must fail it, naming the
                // vector. The at-the-bound acceptance kills the early host and the one-past refusal
                // (with its pinned path) the late one, which is why the family carries no row just
                // under a bound (DECISIONS.md D226). The parser's own nesting cap, which a vector
                // with no `limits` member is read at, is held the same way.
                testCase "a host one unit off any bound, early or late, fails the family by name"
                <| fun _ ->
                    let shift (l: JsonLimit) (delta: int) (limits: JsonLimits) : JsonLimits =
                        let at (v: int) =
                            if v = System.Int32.MaxValue then v else v + delta

                        match l with
                        | JsonLimit.Depth ->
                            { limits with
                                MaxDepth = at limits.MaxDepth }
                        | JsonLimit.StringLength ->
                            { limits with
                                MaxStringLength = at limits.MaxStringLength }
                        | JsonLimit.ArrayLength ->
                            { limits with
                                MaxArrayLength = at limits.MaxArrayLength }
                        | JsonLimit.ObjectMembers ->
                            { limits with
                                MaxObjectMembers = at limits.MaxObjectMembers }
                        | JsonLimit.DocumentBytes ->
                            { limits with
                                MaxDocumentBytes = at limits.MaxDocumentBytes }

                    let failedBy (vec: DecodeRejectCorpus.Vector) (limits: JsonLimits) =
                        match vec.Intent, DecodeRejectCorpus.outcome { vec with Limits = Some limits } with
                        | DecodeRejectCorpus.Refuse(code, path), Error got -> got <> (code, path)
                        | DecodeRejectCorpus.Accept, Ok() -> false
                        | _ -> true

                    // Each simulated host: the vectors it is run on, and the record it reads them under.
                    let mutants =
                        [ for l in
                              [ JsonLimit.Depth
                                JsonLimit.StringLength
                                JsonLimit.ArrayLength
                                JsonLimit.ObjectMembers
                                JsonLimit.DocumentBytes ] do
                              for delta in [ -1; 1 ] ->
                                  sprintf "%s %+d" (JsonLimit.name l) delta,
                                  [ for vec in DecodeRejectCorpus.vectors do
                                        match vec.Limits with
                                        | Some limits -> vec, shift l delta limits
                                        | None -> () ]
                          for delta in [ -1; 1 ] ->
                              sprintf "the nesting cap %+d" delta,
                              [ for vec in DecodeRejectCorpus.vectors do
                                    if vec.Limits.IsNone then
                                        vec, shift JsonLimit.Depth delta ReadOptions.strict.Limits ] ]

                    // The probe can fail: the unshifted reading fails no vector, so a kill below is
                    // the shift's doing and not a vector already out of step with its intent.
                    for vec in DecodeRejectCorpus.vectors do
                        Expect.isFalse
                            (failedBy vec (defaultArg vec.Limits ReadOptions.strict.Limits))
                            (sprintf "%s answers as authored unshifted" vec.Name)

                    for mutant, runs in mutants do
                        let killers =
                            runs
                            |> List.filter (fun (vec, limits) -> failedBy vec limits)
                            |> List.map (fst >> _.Name)

                        Expect.isNonEmpty killers (sprintf "a host off by one (%s) fails a vector of the family" mutant)
                        printfn "decode family, a host off by one (%s): fails %s" mutant (String.concat ", " killers) ] ]
