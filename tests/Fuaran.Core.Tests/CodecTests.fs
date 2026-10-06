module Fuaran.Core.Tests.CodecTests

// Phase 379 — `Canonical` and `Codec<'T>`.
//
// `Canonical` is the canonical writer and reader over a NAMED encoding profile, and `Codec<'T>` is one
// declaration yielding an encoder, a collecting decoder (its first defect the strict reader's) and a
// schema. Held here: the committed bytes a declaration writes under each profile (the vectors), the
// positive control that re-rendering under the other profile moves a byte, the collecting order and
// the paths, the refusals a declaration makes when it is BUILT, the schema's agreement with the
// decoder (through an independent validator), and the stored-codec family a content-addressed
// consumer runs over its own corpus — green on a store that holds, red, naming the text, on one
// that does not.

open System
open System.Text.Json
open Expecto
open Fuaran.Core

type Level =
    | Low
    | High

type Shape =
    | Circle of radius: float
    | Rect of w: int * h: int

type Note =
    { Id: string
      Text: string
      Tags: string list
      Priority: int option
      Shape: Shape
      Level: Level }

let private level: Codec<Level> = Codec.enum [ "low", Low; "high", High ]

let private shape: Codec<Shape> =
    Codec.union
        "kind"
        [ Codec.case
              "circle"
              (fun s ->
                  match s with
                  | Circle r -> Some r
                  | _ -> None)
              Circle
              (Codec.record id |> Codec.field "radius" id Codec.float)
          Codec.case
              "rect"
              (fun s ->
                  match s with
                  | Rect(w, h) -> Some(w, h)
                  | _ -> None)
              Rect
              (Codec.record (fun w h -> w, h)
               |> Codec.field "w" fst Codec.int
               |> Codec.field "h" snd Codec.int) ]

let private note: Codec<Note> =
    Codec.record (fun i t tags p s l ->
        { Id = i
          Text = t
          Tags = tags
          Priority = p
          Shape = s
          Level = l })
    |> Codec.field "id" (fun n -> n.Id) Codec.string
    |> Codec.field "text" (fun n -> n.Text) Codec.string
    |> Codec.field "tags" (fun n -> n.Tags) (Codec.list Codec.string)
    |> Codec.optField "priority" (fun n -> n.Priority) Codec.int
    |> Codec.field "shape" (fun n -> n.Shape) shape
    |> Codec.field "level" (fun n -> n.Level) level
    |> Codec.build

let private multiLine =
    { Id = "n1"
      Text = "line one\nline two\ttab"
      Tags = [ "a"; "b" ]
      Priority = Some 2
      Shape = Circle 1.5
      Level = High }

let private plain =
    { Id = "n2"
      Text = "plain"
      Tags = []
      Priority = None
      Shape = Rect(3, 4)
      Level = Low }

/// The committed vectors: each value with the bytes each profile writes it as. Written by hand from
/// the declaration (member order is declaration order; `priority` is absent when `None`), not
/// captured from the code under test.
let private vectors: (string * Note * string * string) list =
    [ "a multi-line note",
      multiLine,
      """{"id":"n1","text":"line one\nline two\ttab","tags":["a","b"],"priority":2,"shape":{"kind":"circle","radius":1.5},"level":"high"}""",
      """{"id":"n1","text":"line one\u000aline two\u0009tab","tags":["a","b"],"priority":2,"shape":{"kind":"circle","radius":1.5},"level":"high"}"""
      "a plain note",
      plain,
      """{"id":"n2","text":"plain","tags":[],"shape":{"kind":"rect","w":3,"h":4},"level":"low"}""",
      """{"id":"n2","text":"plain","tags":[],"shape":{"kind":"rect","w":3,"h":4},"level":"low"}""" ]

let private errorsOf (r: Result<'T, DecodeError list>) : DecodeError list =
    match r with
    | Ok v -> failtestf "expected defects, read %A" v
    | Error es -> es

let private schemaAccepts (c: Codec<'T>) (wire: string) : bool =
    let schema =
        global.Json.Schema.JsonSchema.FromText(
            Json.render c.Schema,
            global.Json.Schema.BuildOptions(SchemaRegistry = global.Json.Schema.SchemaRegistry())
        )

    use doc = JsonDocument.Parse wire

    let options =
        global.Json.Schema.EvaluationOptions(OutputFormat = global.Json.Schema.OutputFormat.Flag)

    (schema.Evaluate(doc.RootElement, options)).IsValid

let private parsed (text: string) : JVal =
    match Json.parse text with
    | Ok j -> j
    | Error e -> failtestf "not JSON: %s" e

let private passed (laws: LawResult list) : bool = laws |> List.forall (fun l -> l.Passed)

let private failing (laws: LawResult list) : string list =
    laws |> List.filter (fun l -> not l.Passed) |> List.map (fun l -> l.Law)

[<Tests>]
let tests =
    testList
        "Phase 379 — Canonical and Codec<'T>"
        [ testList
              "Canonical"
              [ testCase "write is renderWith under every profile, and tryWrite agrees over a guarded value"
                <| fun () ->
                    let v = JObj [ "t", JStr "a\nb"; "n", JArr [ JInt 1; JFloat 0.5 ] ]

                    for p in EncodingProfile.all do
                        Expect.equal (Canonical.write p v) (Json.renderWith p v) (EncodingProfile.name p)
                        Expect.equal (Canonical.tryWrite p v) (Ok(Json.renderWith p v)) (EncodingProfile.name p)

                testCase "tryWrite refuses a non-finite float and an ill-formed string, naming the path"
                <| fun () ->
                    for p in EncodingProfile.all do
                        match Canonical.tryWrite p (JObj [ "x", JArr [ JFloat nan ] ]) with
                        | Error e -> Expect.stringContains e "$[\"x\"][0]" "the path"
                        | Ok t -> failtestf "a NaN rendered as %s" t

                        match Canonical.tryWrite p (JArr [ JStr(string (char 0xD800)) ]) with
                        | Error e -> Expect.stringContains e "ill-formed" "the class"
                        | Ok t -> failtestf "a lone surrogate rendered as %s" t

                testCase "read accepts both escaping spellings as one value"
                <| fun () ->
                    Expect.equal (Canonical.read "\"a\\nb\"") (Canonical.read "\"a\\u000ab\"") "one value"
                    Expect.equal (Canonical.read "\"a\\nb\"") (Ok(JStr "a\nb")) "the value"

                testCase "isCanonical holds exactly for the profile's own bytes"
                <| fun () ->
                    let v1 = "{\"t\":\"a\\nb\"}"
                    let v2 = "{\"t\":\"a\\u000ab\"}"
                    Expect.isTrue (Canonical.isCanonical EncodingProfile.V1 v1) "V1 text under V1"
                    Expect.isTrue (Canonical.isCanonical EncodingProfile.V2 v2) "V2 text under V2"
                    Expect.isFalse (Canonical.isCanonical EncodingProfile.V2 v1) "V1 text under V2"
                    Expect.isFalse (Canonical.isCanonical EncodingProfile.V1 v2) "V2 text under V1"

                    for p in EncodingProfile.all do
                        Expect.isFalse (Canonical.isCanonical p "{ \"a\":1}") "whitespace"
                        Expect.isFalse (Canonical.isCanonical p "-0") "negative zero reads as 0"
                        Expect.isFalse (Canonical.isCanonical p "{\"a\":1") "not JSON"
                        Expect.isTrue (Canonical.isCanonical p "{\"b\":1,\"a\":2}") "built order is kept, never sorted" ]

          testList
              "Codec — the committed vectors"
              [ for name, value, v1, v2 in vectors do
                    testCase name
                    <| fun () ->
                        Expect.equal (Codec.write EncodingProfile.V1 note value) v1 "V1 bytes"
                        Expect.equal (Codec.write EncodingProfile.V2 note value) v2 "V2 bytes"

                        for p, text in [ EncodingProfile.V1, v1; EncodingProfile.V2, v2 ] do
                            Expect.equal (Codec.read note text) (Ok value) "reads back"
                            Expect.isTrue (Canonical.isCanonical p text) "the committed text is canonical"
                            Expect.equal (Codec.decoder note (parsed text)) (Ok value) "strict"

                        // both columns read as one value whichever spelling they carry
                        Expect.equal (Codec.read note v1) (Codec.read note v2) "one value"

                testCase
                    "positive control: re-rendering under the other profile moves a byte exactly where a control character is"
                <| fun () ->
                    let moved =
                        vectors
                        |> List.filter (fun (_, value, _, _) ->
                            Codec.write EncodingProfile.V1 note value
                            <> Codec.write EncodingProfile.V2 note value)
                        |> List.map (fun (name, _, _, _) -> name)

                    Expect.equal moved [ "a multi-line note" ] "only the note carrying LF and TAB moves" ]
          testList
              "Codec — defects"
              [ testCase "ReadAll answers every defect, undeclared members first, then declaration order, depth-first"
                <| fun () ->
                    let doc =
                        parsed """{"zzz":1,"text":5,"tags":["a",7],"shape":{"kind":"hex"},"level":"mid"}"""

                    let errors = note.ReadAll doc |> errorsOf |> List.map (fun e -> e.Code, e.Path)

                    Expect.equal
                        errors
                        [ DecodeCode.UndeclaredMember, [ PathSegment.Key "zzz" ]
                          DecodeCode.MissingField, [ PathSegment.Key "id" ]
                          DecodeCode.WrongKind, [ PathSegment.Key "text" ]
                          DecodeCode.WrongKind, [ PathSegment.Key "tags"; PathSegment.Index 1 ]
                          DecodeCode.UnknownTag, [ PathSegment.Key "shape"; PathSegment.Key "kind" ]
                          DecodeCode.UnknownTag, [ PathSegment.Key "level" ] ]
                        "every defect, in order"

                    // the strict reader reports the first
                    Expect.equal
                        (Codec.decoder note doc)
                        (Error(List.head (errorsOf (note.ReadAll doc))))
                        "first defect"

                testCase "the strict reader's refusal of a leaf is the Decoder layer's, sentence and all"
                <| fun () ->
                    Expect.equal (Codec.decoder Codec.string (JInt 1)) (Decoder.str (JInt 1)) "string"
                    Expect.equal (Codec.decoder Codec.int (JStr "1")) (Decoder.int (JStr "1")) "int"

                    Expect.equal
                        (Codec.decoder level (JStr "mid"))
                        (Decoder.oneOf [ "low", Low; "high", High ] (JStr "mid"))
                        "enum"

                testCase "a union refuses an absent discriminator, a non-object and a member its case does not declare"
                <| fun () ->
                    let codes (text: string) =
                        shape.ReadAll(parsed text) |> errorsOf |> List.map (fun e -> e.Code, e.Path)

                    Expect.equal
                        (codes """{"radius":1}""")
                        [ DecodeCode.MissingField, [ PathSegment.Key "kind" ] ]
                        "no tag"

                    Expect.equal (codes "[1]") [ DecodeCode.WrongKind, [] ] "not an object"

                    Expect.equal
                        (codes """{"kind":"circle","radius":1,"w":2}""")
                        [ DecodeCode.UndeclaredMember, [ PathSegment.Key "w" ] ]
                        "another case's member"

                    Expect.equal
                        (shape.ReadAll(parsed """{"kind":"circle","radius":1}"""))
                        (Ok(Circle 1.0))
                        "a clean case"

                testCase "a refinement refuses as OutOfRange, and the refinement's value round-trips"
                <| fun () ->
                    let positive =
                        Codec.refine
                            "a positive int"
                            (fun i -> if i > 0 then Ok i else Error "not positive")
                            id
                            Codec.int

                    Expect.equal
                        (positive.ReadAll(JInt 0) |> errorsOf |> List.map (fun e -> e.Code, e.Expected))
                        [ DecodeCode.OutOfRange, "a positive int" ]
                        "refused"

                    Expect.equal (positive.ReadAll(positive.Write 3)) (Ok 3) "admitted"

                testCase "Codec.map carries an isomorphism: the bytes, the refusals and the schema are the base codec's"
                <| fun () ->
                    let tagged =
                        Codec.map (fun (s: string) -> "#" + s) (fun (s: string) -> s.Substring 1) Codec.string

                    Expect.equal (tagged.Write "#x") (JStr "x") "written as the base"
                    Expect.equal (tagged.ReadAll(JStr "x")) (Ok "#x") "read through there"

                    Expect.equal
                        (Codec.decoder tagged (JInt 1))
                        (Decoder.str (JInt 1) |> Result.map id)
                        "the base refusal"

                    Expect.equal tagged.Schema Codec.string.Schema "the base schema"

                testCase "a parse refusal is the one defect, at the root"
                <| fun () ->
                    let errors = Codec.read note "{" |> errorsOf

                    Expect.equal
                        (errors |> List.map (fun e -> e.Code, e.Path))
                        [ DecodeCode.InvalidJson, [] ]
                        "one defect" ]

          testList
              "Codec — declarations refused when built"
              [ testCase "a member declared twice"
                <| fun () ->
                    Expect.throwsT<ArgumentException>
                        (fun () ->
                            Codec.record (fun (a: int) (b: int) -> a + b)
                            |> Codec.field "a" id Codec.int
                            |> Codec.field "a" id Codec.int
                            |> Codec.build
                            |> ignore)
                        "refused"

                testCase "an enum spelling twice, a union tag twice, a discriminator declared as a member"
                <| fun () ->
                    Expect.throwsT<ArgumentException> (fun () -> Codec.enum [ "a", 1; "a", 2 ] |> ignore) "enum"

                    let circle =
                        Codec.case "c" (fun (x: float) -> Some x) id (Codec.record id |> Codec.field "r" id Codec.float)

                    Expect.throwsT<ArgumentException> (fun () -> Codec.union "kind" [ circle; circle ] |> ignore) "tag"

                    let clash =
                        Codec.case
                            "c"
                            (fun (x: float) -> Some x)
                            id
                            (Codec.record id |> Codec.field "kind" id Codec.float)

                    Expect.throwsT<ArgumentException> (fun () -> Codec.union "kind" [ clash ] |> ignore) "discriminator"

                testCase "a value outside the declaration is refused when written"
                <| fun () ->
                    Expect.throwsT<ArgumentException> (fun () -> (Codec.enum [ "a", 1 ]).Write 2 |> ignore) "enum" ]

          testList
              "Codec — the schema agrees with the decoder (an independent validator)"
              [ testCase "every committed vector satisfies the schema"
                <| fun () ->
                    for name, _, v1, v2 in vectors do
                        Expect.isTrue (schemaAccepts note v1) (name + " V1")
                        Expect.isTrue (schemaAccepts note v2) (name + " V2")

                testCase "what the decoder refuses, the schema refuses"
                <| fun () ->
                    for text in
                        [ """{"id":"n2","text":"plain","tags":[],"shape":{"kind":"rect","w":3,"h":4},"level":"low","zzz":1}"""
                          """{"text":"plain","tags":[],"shape":{"kind":"rect","w":3,"h":4},"level":"low"}"""
                          """{"id":"n2","text":"plain","tags":[],"shape":{"kind":"hex","w":3,"h":4},"level":"low"}"""
                          """{"id":"n2","text":"plain","tags":[1],"shape":{"kind":"rect","w":3,"h":4},"level":"low"}"""
                          """{"id":"n2","text":"plain","tags":[],"shape":{"kind":"rect","w":3,"h":4},"level":"mid"}""" ] do
                        Expect.isError (Codec.read note text) ("decoder refuses " + text)
                        Expect.isFalse (schemaAccepts note text) ("schema refuses " + text) ]

          testList
              "Codec — the corpus bridge and the stored-codec family"
              [ testCase "a declaration runs the corpus round-trip law under each profile"
                <| fun () ->
                    for p in EncodingProfile.all do
                        for _, value, _, _ in vectors do
                            Expect.equal (Corpus.roundTrip (Codec.corpus p note) value) (Ok()) (EncodingProfile.name p)

                testCase "a store written under its declared profile is green, under either profile"
                <| fun () ->
                    let v1s = vectors |> List.map (fun (_, _, v1, _) -> v1)
                    let v2s = vectors |> List.map (fun (_, _, _, v2) -> v2)
                    Expect.isTrue (passed (EncodingProfileVectors.storedCodecLaws "v1" note v1s)) "v1"
                    Expect.isTrue (passed (EncodingProfileVectors.storedCodecLaws "v2" note v2s)) "v2"
                    Expect.isTrue (passed (EncodingProfileVectors.storedCodecLaws "v2" note [])) "an empty store"

                    Expect.isTrue
                        (passed (EncodingProfileVectors.storedCodecLaws "v2" Codec.json [ "{\"a\":[1,\"x\\u000a\"]}" ]))
                        "the identity codec over canonical text"

                testCase "a store read under the wrong profile reds exactly the recompute law, naming the text"
                <| fun () ->
                    let v1s = vectors |> List.map (fun (_, _, v1, _) -> v1)
                    let laws = EncodingProfileVectors.storedCodecLaws "v2" note v1s

                    Expect.equal
                        (failing laws)
                        [ "stored codec: every stored text is the codec's canonical text of its value under the declared profile, byte for byte" ]
                        "only the recompute law"

                    let detail = laws |> List.pick (fun l -> l.Counterexample)
                    Expect.stringContains detail "stored text 0" "names the text"

                testCase "an unknown declaration, a defect and a non-canonical spelling each red their law"
                <| fun () ->
                    Expect.equal
                        (EncodingProfileVectors.storedCodecLaws "v3" note [] |> failing |> List.length)
                        4
                        "an unknown profile evaluates nothing"

                    Expect.equal
                        (EncodingProfileVectors.storedCodecLaws "v2" note [ "{\"id\":1}" ]
                         |> failing
                         |> List.head)
                        "stored codec: every stored text reads through the codec with no defect"
                        "a defect"

                    let spaced =
                        """{"id":"n2", "text":"plain","tags":[],"shape":{"kind":"rect","w":3,"h":4},"level":"low"}"""

                    Expect.equal
                        (EncodingProfileVectors.storedCodecLaws "v2" note [ spaced ] |> failing)
                        [ "stored codec: every stored text is the codec's canonical text of its value under the declared profile, byte for byte" ]
                        "whitespace" ] ]
