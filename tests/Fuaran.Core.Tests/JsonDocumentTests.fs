module Fuaran.Core.Tests.JsonDocumentTests

// Phase 640 — the lossless document model beside `JVal` (DECISIONS.md D252).
//
// `JDoc` is read through the one grammar (`Reader`'s token surface, `Reader.numberText` among it),
// written by a guarded writer, decoded through the decode algebra instantiated over it
// (`JDocDecoder`), and converted to and from `JVal`. The differential below holds the reader to
// `Decoder.parse` over the reader's own pool and the same pool with nulls written in: a document
// the strict parse accepts is read to the document that converts back to its value, a document the
// lossless reader accepts and that converts is accepted by the strict parse with that value, and a
// document the lossless reader refuses is refused by the strict parse too. The extracted model's
// lossless reading is held to production over the same pool.

open Expecto
open Fuaran.Core

/// A foreign document no Core type could read and write back unchanged before Phase 640: a null, a
/// number of thirty digits, and a number whose trailing zeros are its scale.
let foreignDocument =
    "{\"id\":null,\"big\":123456789012345678901234567890,\"qty\":412000.00,\"tags\":[null,1.50,\"x\"]}"

let private unbounded = JsonLimits.depthOnly Json.defaultMaxDepth

let private readDoc (input: string) = JDoc.read unbounded input

/// The reader's pool, and the same documents with literals and strings replaced by `null`, so the
/// differential meets nulls at the root, in arrays and in member values.
let pool: string list =
    let nulled (s: string) =
        [ s.Replace("true", "null")
          s.Replace("false", "null")
          s.Replace("\"a\"", "null") ]
        |> List.filter (fun t -> t <> s)

    let basePool = JsonReaderTests.pool
    basePool @ List.collect nulled basePool @ [ foreignDocument ]

let private outcome (r: Result<'a, DecodeError>) (show: 'a -> string) : string =
    match r with
    | Ok a -> "ok " + show a
    | Error e -> DecodeError.render e

let private codeAndPath (e: DecodeError) = e.Code, e.Path

[<Tests>]
let tests =
    testList
        "JsonDocument"
        [ test "JVal cannot hold the foreign document: Json.read refuses it, strict or tolerant (the Phase 640 go-red)" {
              Expect.isError (Json.read ReadOptions.strict foreignDocument) "the strict read refuses the null"
              // The tolerant read erases the member null and still refuses the thirty-digit integer,
              // before it reaches the array-item null it has no absence to erase to.
              match Json.read ReadOptions.tolerantOfNull foreignDocument with
              | Error(JsonRefusal.Malformed m) -> Expect.equal m.Kind MalformedNumber "the thirty-digit integer"
              | other -> failtestf "expected a refusal, got %A" other

              let noNulls = "{\"big\":123456789012345678901234567890,\"qty\":412000.00}"

              match Json.read ReadOptions.strict noNulls with
              | Error(JsonRefusal.Malformed m) -> Expect.equal m.Kind MalformedNumber "the thirty-digit integer"
              | other -> failtestf "expected a refusal, got %A" other

              let scale = "{\"qty\":412000.00}"
              let written = Json.read ReadOptions.strict scale |> Result.map Json.render
              Expect.notEqual written (Ok scale) "the scale of 412000.00 does not survive JVal"
          }

          test
              "a foreign document with a null, a thirty-digit number and trailing zeros is read and written back byte-identically" {
              let doc = readDoc foreignDocument
              Expect.equal (doc |> Result.bind JDoc.tryWrite) (Ok foreignDocument) "byte-identical"

              match doc with
              | Ok d ->
                  Expect.equal (JDoc.tryMember "id" d) (Some DNull) "the null is a value"

                  Expect.equal
                      (JDoc.tryMember "big" d |> Option.bind JDoc.tryNumberText)
                      (Some "123456789012345678901234567890")
                      "every digit"

                  Expect.equal (JDoc.tryMember "qty" d |> Option.bind JDoc.tryNumberText) (Some "412000.00") "the scale"
              | Error e -> failtestf "refused: %s" (DecodeError.render e)
          }

          test "numbers of any length and scale are held as written, and the compact form round-trips byte for byte" {
              let tokens =
                  [ "0"
                    "-0"
                    "0.000"
                    "1E+2"
                    "2.5e-3"
                    "1e400"
                    "-1e-400"
                    "9007199254740993"
                    "123456789012345678901234567890123456789012345678901234567890"
                    "1.50"
                    "412000.00" ]

              for t in tokens do
                  let text = "[" + t + "," + t + "]"
                  Expect.equal (readDoc text |> Result.bind JDoc.tryWrite) (Ok text) t
                  Expect.equal (readDoc text) (Ok(DArr [ DNum t; DNum t ])) t

              // Insignificant whitespace is not a value: a spaced document reads to the document its
              // compact form reads to, and is written in the compact form.
              let spaced = " { \"a\" : [ null , 1.50 ] , \"b\" : true } "
              Expect.equal (readDoc spaced |> Result.bind JDoc.tryWrite) (Ok "{\"a\":[null,1.50],\"b\":true}") "compact"
          }

          test
              "one scanner: over the pool the lossless reader accepts, and refuses, exactly as the strict parse with the conversion" {
              let mutable accepted, convertsNot, refused = 0, 0, 0

              let diffs =
                  [ for input in pool do
                        let strict = Decoder.parse ReadOptions.strict input
                        let lossless = readDoc input

                        match strict, lossless with
                        | Ok v, Ok d ->
                            accepted <- accepted + 1

                            if JDoc.toJVal d <> Ok v then
                                yield
                                    input
                                    + " :: converts to "
                                    + sprintf "%A" (JDoc.toJVal d)
                                    + ", parse "
                                    + sprintf "%A" v
                        | Ok _, Error e ->
                            yield input + " :: parse accepts, the lossless reader refuses " + DecodeError.render e
                        | Error _, Ok d ->
                            convertsNot <- convertsNot + 1

                            match JDoc.toJVal d with
                            | Ok v -> yield input + " :: parse refuses, the document converts to " + sprintf "%A" v
                            | Error _ -> ()
                        | Error _, Error _ -> refused <- refused + 1 ]

              Expect.isEmpty diffs "the lossless reader and the strict parse agree"
              Expect.isGreaterThan accepted 1000 "accepted documents in the pool"

              Expect.isGreaterThan
                  convertsNot
                  1000
                  "documents only the lossless reader reads (nulls, numbers past JVal)"

              Expect.isGreaterThan refused 1000 "documents both refuse"
          }

          test
              "one scanner: a refusal the lossless reader answers is the strict parse's own wherever the parse meets no null and no number past JVal first" {
              // The strict parse refuses at the FIRST null or number past JVal it meets, and the
              // lossless reader reads on; every other refusal of the strict parse is the grammar's or
              // a bound's, and the lossless reader answers it identically.
              let jvalOnly (e: DecodeError) =
                  let s = DecodeError.render e

                  s.Contains "null is not representable"
                  || s.Contains "int53 safe range"
                  || s.Contains "finite double range"

              let pairs =
                  [ for input in pool do
                        match Decoder.parse ReadOptions.strict input with
                        | Error a when not (jvalOnly a) ->
                            match readDoc input with
                            | Error b -> yield input, a, b
                            | Ok d ->
                                failtestf
                                    "%s :: parse refuses %s, the lossless reader reads %A"
                                    input
                                    (DecodeError.render a)
                                    d
                        | _ -> () ]

              Expect.isGreaterThan pairs.Length 500 "refusals compared"

              for input, a, b in pairs do
                  Expect.equal (DecodeError.render b) (DecodeError.render a) input
          }

          test "one scanner: the same limits refuse the same documents at the same breach" {
              let limits =
                  { MaxDepth = 4
                    MaxStringLength = 3
                    MaxArrayLength = 2
                    MaxObjectMembers = 2
                    MaxDocumentBytes = 40 }

              let mutable breaches = 0

              for input in pool do
                  match Decoder.parse { Null = RejectNull; Limits = limits } input, JDoc.read limits input with
                  | Error a, Error b when a.Code = DecodeCode.LimitExceeded ->
                      breaches <- breaches + 1
                      Expect.equal (DecodeError.render b) (DecodeError.render a) input
                  | Ok v, Ok d -> Expect.equal (JDoc.toJVal d) (Ok v) input
                  | _ -> ()

              Expect.isGreaterThan breaches 1000 "breaches compared"
          }

          test "a document the writer writes is read back to itself, and a value's render is the writer's bytes" {
              let mutable written = 0

              for input in pool do
                  match readDoc input with
                  | Ok d ->
                      let text = JDoc.tryWrite d
                      Expect.isOk text input
                      Expect.equal (text |> Result.bind readDoc) (Ok d) input
                      // The writer's form is a fixed point.
                      Expect.equal (text |> Result.bind readDoc |> Result.bind JDoc.tryWrite) text input
                      written <- written + 1
                  | Error _ -> ()

                  match Decoder.parse ReadOptions.strict input with
                  | Ok v ->
                      match Json.tryRender v with
                      | Ok rendered ->
                          Expect.equal (JDoc.ofJVal v |> Result.bind JDoc.tryWrite) (Ok rendered) input
                          Expect.equal (readDoc rendered |> Result.bind JDoc.tryWrite) (Ok rendered) input

                          Expect.equal
                              (JDoc.ofJVal v |> Result.bind JDoc.toJVal)
                              (Decoder.parse ReadOptions.strict rendered)
                              input
                      | Error _ -> ()
                  | Error _ -> ()

              Expect.isGreaterThan written 2000 "documents written"
          }

          test
              "the writer refuses a number text outside the grammar, a lone surrogate and a repeated member, at the path" {
              let bad = DObj [ "a", DArr [ DNum "1"; DNum "01" ] ]

              match JDoc.tryWrite bad with
              | Error e ->
                  Expect.equal
                      (codeAndPath e)
                      (DecodeCode.OutOfRange, [ PathSegment.Key "a"; PathSegment.Index 1 ])
                      "the number"
              | Ok t -> failtestf "wrote %s" t

              match JDoc.tryWrite (DArr [ DStr "ok"; DStr(string (char 0xD800)) ]) with
              | Error e -> Expect.equal (codeAndPath e) (DecodeCode.OutOfRange, [ PathSegment.Index 1 ]) "the string"
              | Ok t -> failtestf "wrote %s" t

              match JDoc.tryWrite (DObj [ "x", DNull; string (char 0xDC00), DNull ]) with
              | Error e ->
                  Expect.equal
                      (codeAndPath e)
                      (DecodeCode.OutOfRange, [ PathSegment.Key(string (char 0xDC00)) ])
                      "the name"
              | Ok t -> failtestf "wrote %s" t

              match JDoc.tryWrite (DObj [ "k", DNull; "j", DBool true; "k", DNum "1" ]) with
              | Error e -> Expect.equal (codeAndPath e) (DecodeCode.NotAdmitted, [ PathSegment.Key "k" ]) "the repeat"
              | Ok t -> failtestf "wrote %s" t

              for t in [ ""; "+1"; "1."; ".5"; "0x10"; "NaN"; "1e"; " 1" ] do
                  Expect.isError (JDoc.tryWrite (DNum t)) t
          }

          test
              "toJVal refuses a null and a number past JVal at its path, and converts everything else as Json.read reads it" {
              match readDoc foreignDocument |> Result.bind JDoc.toJVal with
              | Error e ->
                  Expect.equal (codeAndPath e) (DecodeCode.NotAdmitted, [ PathSegment.Key "id" ]) "the first null"
              | Ok v -> failtestf "converted %A" v

              match readDoc "{\"a\":[1,123456789012345678901234567890]}" |> Result.bind JDoc.toJVal with
              | Error e ->
                  Expect.equal
                      (codeAndPath e)
                      (DecodeCode.OutOfRange, [ PathSegment.Key "a"; PathSegment.Index 1 ])
                      "the number"
              | Ok v -> failtestf "converted %A" v

              match readDoc "[1e400]" |> Result.bind JDoc.toJVal with
              | Error e ->
                  Expect.equal (codeAndPath e) (DecodeCode.OutOfRange, [ PathSegment.Index 0 ]) "past the double range"
              | Ok v -> failtestf "converted %A" v

              Expect.isError (JDoc.toJVal (DNum "01")) "a text outside the grammar"

              // A repeated name (only a constructed document holds one) is refused at the repeated
              // member, before its value: a path through it would name the first member.
              match JDoc.toJVal (DObj [ "a", DObj []; "a", DObj [ "b", DNull ] ]) with
              | Error e -> Expect.equal (codeAndPath e) (DecodeCode.NotAdmitted, [ PathSegment.Key "a" ]) "the repeat"
              | Ok v -> failtestf "converted %A" v

              Expect.equal
                  (readDoc "{\"i\":7,\"f\":1.50,\"w\":9007199254740992,\"c\":10000000000000000}"
                   |> Result.bind JDoc.toJVal)
                  (Decoder.parse
                      ReadOptions.strict
                      "{\"i\":7,\"f\":1.50,\"w\":9007199254740992,\"c\":10000000000000000}")
                  "the scanner's own reading of each token"
          }

          test "ofJVal refuses a non-finite float at its path and writes every other value as Json.render does" {
              match JDoc.ofJVal (JObj [ "a", JArr [ JFloat 1.0; JFloat nan ] ]) with
              | Error e ->
                  Expect.equal
                      (codeAndPath e)
                      (DecodeCode.OutOfRange, [ PathSegment.Key "a"; PathSegment.Index 1 ])
                      "NaN"
              | Ok d -> failtestf "converted %A" d

              Expect.isError (JDoc.ofJVal (JFloat infinity)) "Infinity"
              Expect.equal (JDoc.ofJVal (JInt -5)) (Ok(DNum "-5")) "an int"
              Expect.equal (JDoc.ofJVal (JFloat 1e16)) (Ok(DNum(Json.render (JFloat 1e16)))) "the render's float text"
          }

          test "the accessors read a number by the scanner's rule, and the index takes the first member" {
              Expect.equal (JDoc.tryInt32 (DNum "-2147483648")) (Some -2147483648) "Int32 edge"
              Expect.equal (JDoc.tryInt32 (DNum "2147483648")) None "past Int32"
              Expect.equal (JDoc.tryInt32 (DNum "2.0")) None "a float token"
              Expect.equal (JDoc.tryFloat (DNum "2147483648")) (Some 2147483648.0) "int53"
              Expect.equal (JDoc.tryFloat (DNum "9007199254740993")) None "past 2^53"
              Expect.equal (JDoc.tryFloat (DNum "1.50")) (Some 1.5) "a fraction"
              Expect.equal (JDoc.tryFloat (DNum "007")) None "outside the grammar"
              Expect.equal (JDoc.tryFloat (DStr "1")) None "a string is not a number"

              let d = DObj [ "a", DNum "1"; "b", DNull; "a", DNum "2" ]
              let ix = JDoc.index d |> Option.get
              Expect.equal (Map.tryFind "a" ix) (Some(DNum "1")) "the first a"
              Expect.equal (Map.tryFind "a" ix) (JDoc.tryMember "a" d) "index agrees with tryMember"
              Expect.equal (Map.tryFind "b" ix) (Some DNull) "a null member is present"
              Expect.equal (JDoc.index (DArr [])) None "not an object"
          }

          test "Reader.numberText reads a number as written, and a later mismatch is not refused for the number" {
              let reading (r: Reader) =
                  let mutable a, b = "", [||]

                  Reader.members
                      (fun k r ->
                          if k = "a" then
                              a <- Reader.numberText r
                          else
                              b <- Reader.floats r)
                      r

                  a, b

              Expect.equal
                  (Reader.read ReadOptions.strict reading "{\"a\":123456789012345678901234567890,\"b\":[1.50]}")
                  (Ok("123456789012345678901234567890", [| 1.5 |]))
                  "the text of a number JVal cannot hold"

              match Reader.read ReadOptions.strict reading "{\"a\":123456789012345678901234567890,\"b\":\"x\"}" with
              | Error e ->
                  Expect.equal
                      (codeAndPath e)
                      (DecodeCode.WrongKind, [ PathSegment.Key "b" ])
                      "the mismatch, not the number"
              | Ok v -> failtestf "read %A" v

              match Reader.read ReadOptions.strict Reader.numberText "\"7\"" with
              | Error e -> Expect.equal e.Code DecodeCode.WrongKind "a string is not a number"
              | Ok v -> failtestf "read %s" v

              match Reader.read ReadOptions.strict Reader.numberText "01" with
              | Error e -> Expect.equal e.Code DecodeCode.InvalidJson "the grammar refuses a leading zero"
              | Ok v -> failtestf "read %s" v
          }

          test "JDocDecoder decodes a foreign document with the decode algebra's refusals at their paths" {
              let decoder =
                  JDocDecoder.pair
                      (JDocDecoder.field "id" (JDocDecoder.nullable JDocDecoder.str))
                      (JDocDecoder.pair
                          (JDocDecoder.field "qty" JDocDecoder.numberText)
                          (JDocDecoder.field "tags" (JDocDecoder.list (JDocDecoder.nullable JDocDecoder.json)))
                          (fun q t -> q, t))
                      (fun id (q, t) -> id, q, t)

              Expect.equal
                  (JDocDecoder.read unbounded decoder foreignDocument)
                  (Ok(None, "412000.00", [ None; Some(DNum "1.50"); Some(DStr "x") ]))
                  "the null, the scale, the items"

              match JDocDecoder.read unbounded (JDocDecoder.field "id" JDocDecoder.str) foreignDocument with
              | Error e ->
                  Expect.equal
                      (codeAndPath e)
                      (DecodeCode.WrongKind, [ PathSegment.Key "id" ])
                      "a null where a string belongs"

                  Expect.stringContains (DecodeError.describe e) "null" "the refusal names the null"
              | Ok v -> failtestf "decoded %A" v

              match JDocDecoder.read unbounded (JDocDecoder.field "big" JDocDecoder.int) foreignDocument with
              | Error e -> Expect.equal (codeAndPath e) (DecodeCode.OutOfRange, [ PathSegment.Key "big" ]) "past Int32"
              | Ok v -> failtestf "decoded %A" v

              match
                  JDocDecoder.read unbounded (JDocDecoder.ofDecoder (Decoder.field "qty" Decoder.float)) foreignDocument
              with
              | Error e ->
                  Expect.equal
                      (codeAndPath e)
                      (DecodeCode.NotAdmitted, [ PathSegment.Key "id" ])
                      "ofDecoder converts first"
              | Ok v -> failtestf "decoded %A" v
          }

          test "JDocDecoder is Decoder's algebra: over the pool's values the structural combinators answer alike" {
              let shapes: (string * Decoder<string> * JDocDecoder<string>) list =
                  [ ("field kind", Decoder.field "kind" Decoder.str, JDocDecoder.field "kind" JDocDecoder.str)
                    ("optField k1",
                     Decoder.optField "k1" Decoder.str |> Decoder.map (sprintf "%A"),
                     JDocDecoder.optField "k1" JDocDecoder.str |> JDocDecoder.map (sprintf "%A"))
                    ("at k0.0",
                     Decoder.at [ PathSegment.Key "k0"; PathSegment.Index 0 ] Decoder.str,
                     JDocDecoder.at [ PathSegment.Key "k0"; PathSegment.Index 0 ] JDocDecoder.str)
                    ("list str",
                     Decoder.list Decoder.str |> Decoder.map (String.concat "|"),
                     JDocDecoder.list JDocDecoder.str |> JDocDecoder.map (String.concat "|"))
                    ("boundedList 2",
                     Decoder.boundedList 2 Decoder.str |> Decoder.map (String.concat "|"),
                     JDocDecoder.boundedList 2 JDocDecoder.str |> JDocDecoder.map (String.concat "|"))
                    ("closed",
                     Decoder.closed [ "k0"; "k1" ] (Decoder.succeed "ok"),
                     JDocDecoder.closed [ "k0"; "k1" ] (JDocDecoder.succeed "ok"))
                    ("tagDispatch",
                     Decoder.tagDispatch "k1" [ "a", Decoder.succeed "A" ],
                     JDocDecoder.tagDispatch "k1" [ "a", JDocDecoder.succeed "A" ])
                    ("oneOf", Decoder.oneOf [ "a", "A"; "bcd", "B" ], JDocDecoder.oneOf [ "a", "A"; "bcd", "B" ]) ]

              let mutable compared = 0

              for input in pool do
                  match Decoder.parse ReadOptions.strict input with
                  | Ok v ->
                      let d =
                          JDoc.ofJVal v
                          |> Result.defaultWith (fun e -> failtestf "%s" (DecodeError.render e))

                      for name, onVal, onDoc in shapes do
                          compared <- compared + 1
                          let a = onVal v |> Result.mapError codeAndPath
                          let b = onDoc d |> Result.mapError codeAndPath
                          // A kind name differs where JVal names an int or a float and a document a number;
                          // the code and the path do not.
                          Expect.equal b a (name + " :: " + input)
                  | Error _ -> ()

              Expect.isGreaterThan compared 10000 "decodes compared"
          }

          test "a constructed document of any depth is written and converted without exhausting the stack" {
              let depth = 100000
              let mutable d = DNum "1"

              for _ in 1..depth do
                  d <- DArr [ d ]

              let text = JDoc.tryWrite d
              Expect.isOk text "written"
              Expect.equal (text |> Result.map String.length) (Ok(2 * depth + 1)) "every bracket"
              Expect.isOk (JDoc.toJVal d) "converted to JVal"
              let v = JDoc.toJVal d |> Result.defaultWith (fun _ -> JInt 0)
              Expect.isOk (JDoc.ofJVal v) "converted back"
          } ]
