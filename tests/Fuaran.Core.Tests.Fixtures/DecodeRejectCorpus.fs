namespace Fuaran.Core.Tests

open Fuaran.Core
open Fuaran.Core.Idl

/// The decode reject family: a tiny declarative SHAPE grammar a host interprets with its own
/// combinators, the authored vectors, and their renderer.
module DecodeRejectCorpus =

    open System.IO
    open System.Text

    let private key (k: string) = PathSegment.Key k

    let private index (i: int) = PathSegment.Index i

    let private parse (s: string) : JVal =
        match Json.parse s with
        | Ok v -> v
        | Error e -> failwithf "fixture JSON did not parse: %s" e

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
              // Phase 519: the cap's breach at the path of the container opened at it — the 513th
              // array, item 0 of each of the 512 above it — as every other bound's (it was the root
              // while the default cap had a spelling of its own).
              (Refuse(DecodeCode.LimitExceeded, List.replicate 512 (PathSegment.Index 0)))
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
         | Some limits ->
             Decoder.parse
                 { ReadOptions.strict with
                     Limits = limits }
                 vec.Input
         | None -> Decoder.parse ReadOptions.strict vec.Input)
        |> Result.bind (shapeDecoder defs (parse vec.Shape))
        |> Result.mapError (fun e -> e.Code, e.Path)

    /// An outcome as a vector records it.
    let private outcomeMembers (o: Result<unit, DecodeCode * PathSegment list>) : (string * JVal) list =
        match o with
        | Error(code, path) ->
            [ "refusal", JObj [ "code", JStr(DecodeError.codeName code); "path", DecodePath.toJson path ] ]
        | Ok() -> [ "accepted", JBool true ]

    /// The vectors the models decline (the not-answered rule, D232): each is refused for a limit the
    /// PARSER breaches, which the parser model names by its limit and position — the decode path the
    /// reference names the breach at is not in the model. A bound the decode layer enforces itself
    /// (`maxItems`) is the decode model's, and answered.
    let breachOutOfModel: Map<string, string> =
        [ "nesting-past-the-cap"
          "limit-depth-past"
          "limit-string-length-past"
          "limit-string-length-astral-past"
          "limit-string-length-in-a-key"
          "limit-string-length-nested"
          "limit-array-length-past"
          "limit-array-length-nested"
          "limit-object-members-past"
          "limit-document-bytes-past"
          "limit-breach-before-malformed-text" ]
        |> List.map (fun n -> n, "a limit the parser breaches, whose decode path the parser model does not carry")
        |> Map.ofList

    /// The models' outcome for a vector (Phase 563): read by the parser model, decoded by the decode
    /// layer's model over the same shape.
    let private modelOutcome (vec: Vector) : (string * string * JVal) option =
        let defs =
            match parse vec.Definitions with
            | JObj d -> d
            | _ -> []

        DecodeOracle.read
            vec.Limits
            defs
            (parse vec.Shape)
            (fun o -> CorpusCell.Value(JObj(outcomeMembers o)))
            vec.Input
        |> VectorOracle.admit breachOutOfModel vec.Name
        |> Option.map (fun a ->
            match a.Cell with
            | CorpusCell.Value answer -> a.Model, a.LadderRow, answer
            | other -> failwithf "a decode vector's answer is a compact value, not %A" other)

    let private line (vec: Vector) : CorpusCell =
        let result = outcomeMembers (outcome vec)

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
                @ [ VectorOracle.stampValue vec.Name (JObj result) (modelOutcome vec) ]
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
                Adoption =
                  (RefusalCorpus.hostRoster |> List.map (fun h -> h, HostAdoption.Proposed))
                  @ [ SpineAdoption.adoption familyId ]
                Description =
                  "The wire-level decode error contract: every decode code, at the path it names, over a shape grammar each host reads with its own combinators." } ] }

    let renderManifest () : string =
        CorpusFile.render (CorpusFile.manifest (manifest ()))

    let vectorsPath (dir: string) =
        Path.Combine(dir, familyDirName, vectorsFileName)

    let manifestPath (dir: string) =
        Path.Combine(dir, familyDirName, manifestFileName)
