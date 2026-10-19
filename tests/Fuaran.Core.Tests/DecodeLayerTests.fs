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
//      dotnet run --project tests/Fuaran.Core.Tests -- --emit-decode [<dir>]
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

/// The decode reject family: a tiny declarative SHAPE grammar a host interprets with its own
/// combinators, the authored vectors, and their renderer.
module DecodeRejectCorpus =

    open System.IO
    open System.Text

    let familyDirName = "decode"
    let vectorsFileName = "decode-rejects.json"
    let manifestFileName = "manifest.json"
    let familyId = "decodeRejects"

    /// The reference reading of a shape — the layer's combinators, one per shape kind. A shape is
    /// `{"$type": "string" | "int" | "float" | "bool" | "any"}`, `{"$type":"list","of":S,"maxItems"?:n}`,
    /// `{"$type":"object","fields":[{"name","shape","optional"?}],"closed"?:bool}`,
    /// `{"$type":"tagged","key":k,"cases":[{"tag","shape"}],"admit"?:[tags]}` (the case's shape reads
    /// the SAME object), `{"$type":"intRange","min","max"}`, or `{"$type":"ref","name"}`, resolved
    /// against the vector's `definitions`.
    let rec shapeDecoder (defs: (string * JVal) list) (shape: JVal) : Decoder<unit> =
        let mem (name: string) = Decoder.tryMember name shape
        let unitOf (d: Decoder<'T>) : Decoder<unit> = d |> Decoder.map ignore

        match mem "$type" with
        | Some(JStr "string") -> unitOf Decoder.str
        | Some(JStr "int") -> unitOf Decoder.int
        | Some(JStr "float") -> unitOf Decoder.float
        | Some(JStr "bool") -> unitOf Decoder.bool
        | Some(JStr "any") -> unitOf Decoder.json
        | Some(JStr "list") ->
            let item = shapeDecoder defs (defaultArg (mem "of") (JObj [ "$type", JStr "any" ]))

            match mem "maxItems" with
            | Some(JInt n) -> unitOf (Decoder.boundedList n item)
            | _ -> unitOf (Decoder.list item)
        | Some(JStr "object") ->
            let fields =
                match mem "fields" with
                | Some(JArr fs) -> fs
                | _ -> []

            let names =
                fields
                |> List.choose (fun f ->
                    match Decoder.tryMember "name" f with
                    | Some(JStr n) -> Some n
                    | _ -> None)

            let reads =
                fields
                |> List.map (fun f ->
                    let name =
                        match Decoder.tryMember "name" f with
                        | Some(JStr n) -> n
                        | _ -> ""

                    let d =
                        shapeDecoder defs (defaultArg (Decoder.tryMember "shape" f) (JObj [ "$type", JStr "any" ]))

                    match Decoder.tryMember "optional" f with
                    | Some(JBool true) -> unitOf (Decoder.optField name d)
                    | _ -> Decoder.field name d)

            let body =
                fun (v: JVal) ->
                    match v with
                    | JObj _ -> unitOf (Decoder.sequence reads) v
                    | other -> Error(Decoder.wrongKind "object" other)

            match mem "closed" with
            | Some(JBool true) -> Decoder.closed names body
            | _ -> body
        | Some(JStr "tagged") ->
            let tagKey =
                match mem "key" with
                | Some(JStr k) -> k
                | _ -> "kind"

            let admit =
                match mem "admit" with
                | Some(JArr xs) ->
                    Some(
                        xs
                        |> List.choose (function
                            | JStr s -> Some s
                            | _ -> None)
                    )
                | _ -> None

            let cases =
                match mem "cases" with
                | Some(JArr cs) ->
                    cs
                    |> List.choose (fun c ->
                        match Decoder.tryMember "tag" c, Decoder.tryMember "shape" c with
                        | Some(JStr t), Some s ->
                            let read =
                                match admit with
                                | Some admitted when not (List.contains t admitted) ->
                                    fun (_: JVal) ->
                                        Error(
                                            DecodeError.under
                                                (PathSegment.Key tagKey)
                                                (DecodeError.make
                                                    DecodeCode.NotAdmitted
                                                    "an admitted tag"
                                                    ("'" + t + "' is not admitted here"))
                                        )
                                | _ -> shapeDecoder defs s

                            Some(t, read)
                        | _ -> None)
                | _ -> []

            Decoder.tagDispatch tagKey cases
        | Some(JStr "intRange") ->
            match mem "min", mem "max" with
            | Some(JInt lo), Some(JInt hi) -> unitOf (Decoder.intRange lo hi)
            | _ -> Decoder.fail RootDecodeCode.SchemaFault "an intRange with min and max" "malformed intRange shape"
        | Some(JStr "ref") ->
            let name =
                match mem "name" with
                | Some(JStr n) -> n
                | _ -> ""

            match defs |> List.tryFind (fun (n, _) -> n = name) with
            | Some(_, s) -> fun v -> shapeDecoder defs s v
            | None ->
                Decoder.fail
                    RootDecodeCode.SchemaFault
                    ("a declared shape '" + name + "'")
                    ("unknown shape '" + name + "'")
        | _ -> Decoder.fail RootDecodeCode.SchemaFault "a known shape" "unknown shape"

    /// What an authored vector is FOR.
    type Intent =
        | Refuse of code: DecodeCode * path: PathSegment list
        | Accept

    type Vector =
        {
            Name: string
            Shape: string
            Definitions: string
            Input: string
            /// The limits record the input is read under (Phase 465), or `None` for the parser's
            /// own cap and no other bound.
            Limits: JsonLimits option
            Intent: Intent
        }

    let private v name shape input intent =
        { Name = name
          Shape = shape
          Definitions = "{}"
          Input = input
          Limits = None
          Intent = intent }

    let private str = """{"$type":"string"}"""
    let private int = """{"$type":"int"}"""
    let private any = """{"$type":"any"}"""

    /// A vector read under a limits record (Phase 465), at the `any` shape: what it pins is the
    /// parse, not the shape.
    let private vl name limits input intent =
        { Name = name
          Shape = any
          Definitions = "{}"
          Input = input
          Limits = Some limits
          Intent = intent }

    /// The record every limits vector starts from: generous everywhere, so each vector tightens
    /// exactly the bound it is about and the others cannot fire.
    let private wide: JsonLimits =
        { MaxDepth = 512
          MaxStringLength = 1024
          MaxArrayLength = 1024
          MaxObjectMembers = 1024
          MaxDocumentBytes = 65536 }

    let private limited (code: DecodeCode) (path: PathSegment list) = Refuse(code, path)

    let private obj (fields: (string * string * bool) list) (closed: bool) =
        let fs =
            fields
            |> List.map (fun (n, s, optional) ->
                sprintf
                    """{"name":%s,"shape":%s%s}"""
                    (Json.render (JStr n))
                    s
                    (if optional then ",\"optional\":true" else ""))
            |> String.concat ","

        sprintf """{"$type":"object","fields":[%s]%s}""" fs (if closed then ",\"closed\":true" else "")

    let private shapes =
        """{"$type":"tagged","key":"kind","cases":[{"tag":"circle","shape":{"$type":"object","fields":[{"name":"r","shape":{"$type":"float"}}]}},{"tag":"square","shape":{"$type":"object","fields":[{"name":"side","shape":{"$type":"float"}}]}}]}"""

    let private admitCircle =
        """{"$type":"tagged","key":"kind","admit":["circle"],"cases":[{"tag":"circle","shape":{"$type":"object","fields":[{"name":"r","shape":{"$type":"float"}}]}},{"tag":"square","shape":{"$type":"object","fields":[{"name":"side","shape":{"$type":"float"}}]}}]}"""

    /// The authored vectors, at least one per code; each names the outcome it is FOR, and a test
    /// holds the computed outcome to it.
    let vectors: Vector list =
        [ v "not-json" str """{"a":""" (Refuse(DecodeCode.InvalidJson, []))
          v
              "nesting-past-the-cap"
              (sprintf """{"$type":"list","of":%s}""" int)
              (String.replicate 600 "[" + String.replicate 600 "]")
              (Refuse(DecodeCode.LimitExceeded, []))
          v
              "too-many-items"
              (sprintf """{"$type":"list","of":%s,"maxItems":2}""" int)
              "[1,2,3]"
              (Refuse(DecodeCode.LimitExceeded, []))
          v "missing-member" (obj [ "a", str, false ] false) "{}" (Refuse(DecodeCode.MissingField, [ key "a" ]))
          v
              "missing-nested-member"
              (obj [ "a", obj [ "b", int, false ] false, false ] false)
              """{"a":{}}"""
              (Refuse(DecodeCode.MissingField, [ key "a"; key "b" ]))
          v
              "wrong-kind-member"
              (obj [ "a", int, false ] false)
              """{"a":"x"}"""
              (Refuse(DecodeCode.WrongKind, [ key "a" ]))
          v
              "wrong-kind-item"
              (obj [ "xs", sprintf """{"$type":"list","of":%s}""" int, false ] false)
              """{"xs":[1,"two",3]}"""
              (Refuse(DecodeCode.WrongKind, [ key "xs"; index 1 ]))
          v "wrong-kind-root" (obj [ "a", int, false ] false) "[]" (Refuse(DecodeCode.WrongKind, []))
          v
              "member-key-needing-escape"
              (obj [ "a\"b", int, false ] false)
              """{"a\"b":"x"}"""
              (Refuse(DecodeCode.WrongKind, [ key "a\"b" ]))
          v "optional-absent" (obj [ "a", int, true ] false) "{}" Accept
          v
              "optional-present-ill-typed"
              (obj [ "a", int, true ] false)
              """{"a":"x"}"""
              (Refuse(DecodeCode.WrongKind, [ key "a" ]))
          v "unknown-tag" shapes """{"kind":"hexagon"}""" (Refuse(DecodeCode.UnknownTag, [ key "kind" ]))
          v "absent-tag" shapes """{"r":1}""" (Refuse(DecodeCode.MissingField, [ key "kind" ]))
          v "tag-not-a-string" shapes """{"kind":7}""" (Refuse(DecodeCode.WrongKind, [ key "kind" ]))
          v
              "known-tag-case-member-missing"
              shapes
              """{"kind":"circle"}"""
              (Refuse(DecodeCode.MissingField, [ key "r" ]))
          v "known-tag-accepted" shapes """{"kind":"square","side":2}""" Accept
          v
              "known-tag-not-admitted"
              admitCircle
              """{"kind":"square","side":2}"""
              (Refuse(DecodeCode.NotAdmitted, [ key "kind" ]))
          v
              "int-out-of-range"
              (obj [ "n", """{"$type":"intRange","min":0,"max":10}""", false ] false)
              """{"n":11}"""
              (Refuse(DecodeCode.OutOfRange, [ key "n" ]))
          v
              "undeclared-member"
              (obj [ "a", int, false ] true)
              """{"a":1,"b":2}"""
              (Refuse(DecodeCode.UndeclaredMember, [ key "b" ]))
          v
              "undeclared-nested-member"
              (obj [ "inner", obj [ "a", int, false ] true, false ] false)
              """{"inner":{"a":1,"z":true}}"""
              (Refuse(DecodeCode.UndeclaredMember, [ key "inner"; key "z" ]))
          v "closed-object-accepted" (obj [ "a", int, false; "b", str, true ] true) """{"a":1}""" Accept
          v "unresolved-shape" """{"$type":"ref","name":"missing"}""" "1" (Refuse(DecodeCode.SchemaFault, []))
          v
              "unresolved-nested-shape"
              (obj [ "a", """{"$type":"ref","name":"missing"}""", false ] false)
              """{"a":1}"""
              (Refuse(DecodeCode.SchemaFault, [ key "a" ]))
          { Name = "resolved-shape"
            Shape = """{"$type":"ref","name":"point"}"""
            Definitions = sprintf """{"point":%s}""" (obj [ "x", int, false; "y", int, false ] true)
            Input = """{"x":1,"y":2}"""
            Limits = None
            Intent = Accept }
          // Phase 462 — the JSON-syntax rows every wire shares, at the shape reader: a refusal of
          // the text is `InvalidJson` at the root whatever shape was asked for.
          v "repeated-member" (obj [ "a", int, false ] false) """{"a":1,"a":2}""" (Refuse(DecodeCode.InvalidJson, []))
          v "content-after-the-root" int "1 2" (Refuse(DecodeCode.InvalidJson, []))
          v "bare-nan" """{"$type":"float"}""" "NaN" (Refuse(DecodeCode.InvalidJson, []))
          v "raw-control-in-a-string" str ("\"a" + string (char 1) + "\"") (Refuse(DecodeCode.InvalidJson, []))
          v "lone-surrogate" str "\"\\uD800\"" (Refuse(DecodeCode.InvalidJson, []))
          v "number-outside-the-grammar" int "01" (Refuse(DecodeCode.InvalidJson, []))
          // Phase 465 — the limits, each declared by the vector and pinned from both sides: a
          // document exactly at the bound is read, one past it is `LimitExceeded` at the path of
          // the container the breach was raised in, never `InvalidJson`. A string's unit is the
          // Unicode code point (the astral pair), the document's is the UTF-8 byte (the CJK
          // character), and a key is a string.
          vl "limit-depth-at-max" { wide with MaxDepth = 2 } "[[1]]" Accept
          vl
              "limit-depth-past"
              { wide with MaxDepth = 2 }
              "[[[1]]]"
              (limited DecodeCode.LimitExceeded [ index 0; index 0 ])
          vl "limit-string-length-at-max" { wide with MaxStringLength = 3 } "\"abc\"" Accept
          vl
              "limit-string-length-past"
              { wide with MaxStringLength = 3 }
              "\"abcd\""
              (limited DecodeCode.LimitExceeded [])
          vl
              "limit-string-length-astral-at-max"
              { wide with MaxStringLength = 3 }
              "\"\U0001F600\U0001F600\U0001F600\""
              Accept
          vl
              "limit-string-length-astral-past"
              { wide with MaxStringLength = 3 }
              "\"\U0001F600\U0001F600\U0001F600\U0001F600\""
              (limited DecodeCode.LimitExceeded [])
          vl
              "limit-string-length-in-a-key"
              { wide with MaxStringLength = 3 }
              """{"abcd":1}"""
              (limited DecodeCode.LimitExceeded [])
          vl
              "limit-string-length-nested"
              { wide with MaxStringLength = 3 }
              """{"a":["abcd"]}"""
              (limited DecodeCode.LimitExceeded [ key "a"; index 0 ])
          vl "limit-array-length-at-max" { wide with MaxArrayLength = 2 } "[1,2]" Accept
          vl "limit-array-length-past" { wide with MaxArrayLength = 2 } "[1,2,3]" (limited DecodeCode.LimitExceeded [])
          vl
              "limit-array-length-nested"
              { wide with MaxArrayLength = 2 }
              """{"a":[1,2,3]}"""
              (limited DecodeCode.LimitExceeded [ key "a" ])
          vl "limit-object-members-at-max" { wide with MaxObjectMembers = 2 } """{"a":1,"b":2}""" Accept
          vl
              "limit-object-members-past"
              { wide with MaxObjectMembers = 2 }
              """{"a":1,"b":2,"c":3}"""
              (limited DecodeCode.LimitExceeded [])
          vl "limit-document-bytes-at-max" { wide with MaxDocumentBytes = 5 } "\"日\"" Accept
          vl
              "limit-document-bytes-past"
              { wide with MaxDocumentBytes = 4 }
              "\"日\""
              (limited DecodeCode.LimitExceeded [])
          vl
              "limit-breach-before-malformed-text"
              { wide with MaxArrayLength = 2 }
              "[1,2,3"
              (limited DecodeCode.LimitExceeded [])
          vl
              "limit-malformed-text-before-breach"
              { wide with MaxArrayLength = 2 }
              "[1,%,3,4]"
              (limited DecodeCode.InvalidJson []) ]

    /// The limits record as a vector declares it: one member per limit, by `JsonLimit.name`.
    let limitsJson (limits: JsonLimits) : JVal =
        JObj
            [ for l in
                  [ JsonLimit.Depth
                    JsonLimit.StringLength
                    JsonLimit.ArrayLength
                    JsonLimit.ObjectMembers
                    JsonLimit.DocumentBytes ] -> JsonLimit.name l, JInt(JsonLimit.declared limits l) ]

    /// The outcome of a vector, computed by calling the reference decoder: `Error (code, path)` or
    /// `Ok ()` for an acceptance.
    let outcome (vec: Vector) : Result<unit, DecodeCode * PathSegment list> =
        let defs =
            match parse vec.Definitions with
            | JObj d -> d
            | _ -> []

        (match vec.Limits with
         | Some limits -> Decoder.parseWithLimits limits vec.Input
         | None -> Decoder.parse vec.Input)
        |> Result.bind (shapeDecoder defs (parse vec.Shape))
        |> Result.mapError (fun e -> e.Code, e.Path)

    let private line (vec: Vector) : CorpusCell =
        let result =
            match outcome vec with
            | Error(code, path) ->
                [ "refusal", JObj [ "code", JStr(DecodeError.codeName code); "path", DecodePath.toJson path ] ]
            | Ok() -> [ "accepted", JBool true ]

        let defs =
            match parse vec.Definitions with
            | JObj [] -> []
            | d -> [ "definitions", d ]

        let limits =
            match vec.Limits with
            | Some l -> [ "limits", limitsJson l ]
            | None -> []

        CorpusCell.Value(
            JObj(
                [ "name", JStr vec.Name; "shape", parse vec.Shape; "input", JStr vec.Input ]
                @ limits
                @ defs
                @ result
            )
        )

    let private description =
        "Decode reject vectors (Phase 310). Each vector is a SHAPE, a JSON `input` (text, which may not parse), and the outcome computed by reading the input through the reference decoder of that shape: a `refusal` names the decode code (the closed DecodeCode set: InvalidJson, MissingField, WrongKind, UnknownTag, OutOfRange, UndeclaredMember, LimitExceeded, NotAdmitted, SchemaFault) and the path, as an array of member keys (strings) and item indices (integers) from the document root; `accepted` marks an input the shape reads. Shapes: string | int | float | bool | any; list (of, optional maxItems: more items is LimitExceeded at the array); object (fields of name, shape, optional; optional closed: an undeclared member is UndeclaredMember at that member); tagged (key, cases of tag and shape, the case shape reading the SAME object; an absent or non-string tag is refused at the key, an unknown one is UnknownTag there, and a tag outside an optional admit list is NotAdmitted there); intRange (min, max: outside is OutOfRange); ref (name, resolved against the vector's definitions: unresolved is SchemaFault). An object's fields are read in order and the first refusal is the answer; an absent optional field is accepted and a present ill-typed one refused. A MissingField path names the absent member. A vector without a `limits` member is parsed at the parser's own cap of 512 nested containers and no other bound. A vector WITH one (Phase 465) is parsed under that record — `depth` (nested containers admitted), `stringLength` (Unicode code points in one string or member key, a surrogate pair counting as one), `arrayLength` (items in one array), `objectMembers` (members in one object), `documentBytes` (UTF-8 bytes of the whole input) — each enforced on the way down, before the container, unit, item or member it guards is read: a breach is LimitExceeded at the path of the container it was raised in (the string's own path for a string value, the object's for a key, the root for documentBytes) and is never InvalidJson; malformed text is InvalidJson exactly as without the record; and the first fault in document order wins. A host certifies its decode error contract by reproducing every code and path; the sentence beside a refusal is the host's own."

    /// The vectors file, rendered: a header, then one canonical line per vector.
    let render () : string =
        CorpusFile.render
            { Head = [ "family", CorpusFile.str familyId; "description", CorpusFile.str description ]
              Items = "vectors"
              Rows = vectors |> List.map line }

    /// The family index as the one manifest schema (`CorpusManifest`, Phase 508).
    let internal manifest () : CorpusManifest =
        { Version = 1
          Description =
            "Decode reject corpus (Phase 310). SELF-ENUMERATED, as the refusals/ and apply/ families are: not indexed by the corpus root manifest.json. `adoption` is per host and is DATA: a host whose entry reads `proposed` reports the family BY NAME rather than skipping it, and flips its own entry to `adopted` in the change-set that lands its leg. This manifest is authoritative for the vector count."
          Families =
            [ { Id = familyId
                Kind = "decode-rejects"
                File = vectorsFileName
                Vectors = List.length vectors
                Adoption = RefusalVectorTests.RefusalCorpus.hostRoster |> List.map (fun h -> h, "proposed")
                Description =
                  "The wire-level decode error contract: every decode code, at the path it names, over a shape grammar each host reads with its own combinators." } ] }

    let renderManifest () : string =
        CorpusFile.render (CorpusFile.manifest (manifest ()))

    let vectorsPath (dir: string) =
        Path.Combine(dir, familyDirName, vectorsFileName)

    let manifestPath (dir: string) =
        Path.Combine(dir, familyDirName, manifestFileName)


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

                testCase
                    "tagDispatchWith words an unknown tag in the caller's sentence and changes nothing else (Phase 388)"
                <| fun _ ->
                    let cases = [ "a", Decoder.field "n" Decoder.int ]
                    let d = Decoder.tagDispatchWith "unknown thing: " "$type" cases
                    let plain = Decoder.tagDispatch "$type" cases

                    Expect.equal (d (parse """{"$type":"a","n":1}""")) (Ok 1) "a known tag dispatches"

                    let miss = parse """{"$type":"b"}"""

                    match d miss, plain miss with
                    | Error e, Error p ->
                        Expect.equal e.Message "unknown thing: b" "the miss is in the caller's sentence"

                        Expect.equal
                            (e.Code, e.Path, e.Expected)
                            (p.Code, p.Path, p.Expected)
                            "and is otherwise tagDispatch's"
                    | other -> failtestf "an unknown tag must be refused by both: %A" other

                    for doc in [ """{"n":1}"""; """{"$type":1}"""; """{"$type":"a","n":"x"}""" ] do
                        Expect.equal
                            (d (parse doc))
                            (plain (parse doc))
                            (doc + ": every other refusal is tagDispatch's")

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

                    Expect.equal (refusal (Decoder.parse "{")) (DecodeCode.InvalidJson, []) "not JSON"

                    Expect.equal
                        (refusal (Decoder.parseWith 3 "[[[[1]]]]"))
                        (DecodeCode.LimitExceeded, [])
                        "the nesting cap is a limit"

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
                                Expect.equal (e.Expected, e.Message) ("e", "m") "its phrases, untouched"

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
                        Expect.equal e.Message "argument 'n' is given more than once" "its sentence"
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

                    Expect.equal (Fuaran.Core.Decode.tryProp "s" doc) (Some(JStr "x")) "tryProp"

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
                        (Error "proposal has no 'id'")
                        "the sentence is kept"

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
                        (refusal (Artifact.ofJsonDetailed broken))
                        (DecodeCode.MissingField, [ key "kinds"; index 0; key "category" ])
                        "the first kind lacks its category"

                    Expect.equal (Artifact.ofJson broken) (Error "missing 'category'") "the sentence is kept"

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

                        Expect.equal
                            (CapabilityPipeline.decode doc)
                            (Error "unknown arg source: elsewhere")
                            "the sentence is kept"
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
                            Artifact.ofJsonDetailed
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
                    let decode = Decoder.ofString (Decoder.field "a" Decoder.int)

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
                // limit (read through `Json.parseWithLimits`, which names it) at the path the
                // vector pins — so a host whose unit is wrong (UTF-16 units, bytes) fails the
                // astral and CJK vectors by name rather than by accident.
                testCase "every limit is pinned at its bound and one past it, and a breach names its limit"
                <| fun _ ->
                    let limitVectors =
                        DecodeRejectCorpus.vectors
                        |> List.choose (fun v -> v.Limits |> Option.map (fun l -> v, l))

                    let breached =
                        [ for v, l in limitVectors do
                              match v.Intent, Json.parseWithLimits RejectNull l v.Input with
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

                    Expect.isGreaterThanOrEqual acceptedUnder 5 "a vector at each bound is accepted" ] ]
