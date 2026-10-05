module Fuaran.Core.Tests.JsonReaderTests

// Phase 368 — `Json.Reader`, the typed reading of a document, held to `Json.parseDetailed`.
//
// The reader runs the parser's own scanner (`Json.Scanner`), so it has no grammar of its own to be
// wrong about; what it adds is the typed collection and the shape checks. Its contract is stated
// against `parseDetailed` and nothing else, so every test here computes the expected outcome by
// calling `parseDetailed` and typing its tree, never by restating what the answer ought to be:
//
//   - a document `parseDetailed` refuses is refused with `ReadError.Malformed` carrying exactly
//     `parseDetailed`'s error — kind, message AND position — whatever the reading asked for;
//   - a document it accepts is read to what typing its tree gives, or refused with
//     `ReadError.Mismatch` exactly when that typing fails.
//
// The differential runs every reading below over the parser corpus (the json vectors of the codec
// refusal corpus, Phase 299), the wire benchmark's five corpora (Phase 364), and a deterministic
// generated pool of documents with each one truncated, cut, and spliced with a grammar character.
// The comparison is shown able to fail: the same outcomes with every refusal position moved by one
// disagree on every refusal.

open Expecto
open Fuaran.Core

let private inv = System.Globalization.CultureInfo.InvariantCulture

/// An outcome as comparable text: the typed value rendered, a grammar refusal in full, or a mismatch.
let private malformed (e: JsonError) =
    sprintf "malformed %A @%d %s" e.Kind e.Position e.Message

let private readerOutcome (render: 'a -> string) (r: Result<'a, Json.ReadError>) : string =
    match r with
    | Ok a -> "ok " + render a
    | Error(Json.ReadError.Malformed e) -> malformed e
    | Error(Json.ReadError.Mismatch _) -> "mismatch"

let private specOutcome (typing: JVal -> 'a option) (render: 'a -> string) (input: string) : string =
    match Json.parseDetailed input with
    | Error e -> malformed e
    | Ok v ->
        match typing v with
        | Some a -> "ok " + render a
        | None -> "mismatch"

let private allSome (xs: 'b option list) : 'b list option =
    if List.forall Option.isSome xs then
        Some(List.map Option.get xs)
    else
        None

let private asStr =
    function
    | JStr s -> Some s
    | _ -> None

let private asNum =
    function
    | JInt i -> Some(float i)
    | JFloat f -> Some f
    | _ -> None

let private asInt =
    function
    | JInt i -> Some i
    | _ -> None

let private arrayOf (item: JVal -> 'b option) =
    function
    | JArr xs -> xs |> List.map item |> allSome |> Option.map Array.ofList
    | _ -> None

let private renderStrings (a: string[]) =
    Json.render (JArr [ for s in a -> JStr s ])

let private renderFloats (a: float[]) =
    a |> Array.map (fun f -> f.ToString("R", inv)) |> String.concat ","

let private renderInts (a: int[]) =
    a |> Array.map string |> String.concat ","

/// One reading: its name, and its outcome on an input by the reader and by the specification.
type private Reading =
    { Name: string
      Reader: string -> string
      Spec: string -> string }

let private reading (name: string) (r: Json.Reader -> 'a) (typing: JVal -> 'a option) (render: 'a -> string) =
    { Name = name
      Reader = fun input -> readerOutcome render (Json.Reader.read r input)
      Spec = specOutcome typing render }

/// The `items` and `members` readings collect what their callbacks read.
let private itemsOfValues (r: Json.Reader) : JVal list =
    let acc = ResizeArray<JVal>()
    Json.Reader.items (fun r -> acc.Add(Json.Reader.value r)) r
    List.ofSeq acc

let private membersOfValues (r: Json.Reader) : (string * JVal) list =
    let acc = ResizeArray<string * JVal>()
    Json.Reader.members (fun k r -> acc.Add((k, Json.Reader.value r))) r
    List.ofSeq acc

/// The compute repository's incremental state, as its first consumer would read it: the `tokens`
/// member as a `string[]`, every other member as a value.
type private Member =
    | Tokens of string[]
    | Other of JVal

let private stateShaped (r: Json.Reader) : (string * Member) list =
    let acc = ResizeArray<string * Member>()

    Json.Reader.members
        (fun k r ->
            if k = "tokens" then
                acc.Add((k, Tokens(Json.Reader.strings r)))
            else
                acc.Add((k, Other(Json.Reader.value r))))
        r

    List.ofSeq acc

let private stateTyping (v: JVal) : (string * Member) list option =
    match v with
    | JObj fs ->
        fs
        |> List.map (fun (k, x) ->
            if k = "tokens" then
                arrayOf asStr x |> Option.map (fun a -> k, Tokens a)
            else
                Some(k, Other x))
        |> allSome
    | _ -> None

let private renderState (fs: (string * Member) list) =
    fs
    |> List.map (fun (k, m) ->
        k
        + "="
        + (match m with
           | Tokens a -> "tokens " + renderStrings a
           | Other v -> Json.render v))
    |> String.concat ";"

let private readings: Reading list =
    [ reading "value" Json.Reader.value Some Json.render
      reading "strings" Json.Reader.strings (arrayOf asStr) renderStrings
      reading "floats" Json.Reader.floats (arrayOf asNum) renderFloats
      reading "ints" Json.Reader.ints (arrayOf asInt) renderInts
      reading
          "items"
          itemsOfValues
          (function
          | JArr xs -> Some xs
          | _ -> None)
          (fun xs -> Json.render (JArr xs))
      reading
          "members"
          membersOfValues
          (function
          | JObj fs -> Some fs
          | _ -> None)
          (fun fs -> Json.render (JObj fs))
      reading "state-shaped" stateShaped stateTyping renderState
      reading
          "items-of-strings"
          (fun r ->
              let acc = ResizeArray<string[]>()
              Json.Reader.items (fun r -> acc.Add(Json.Reader.strings r)) r
              List.ofSeq acc)
          (function
          | JArr xs -> xs |> List.map (arrayOf asStr) |> allSome
          | _ -> None)
          (fun xs -> xs |> List.map renderStrings |> String.concat ";") ]

// ---- the pool ----

/// A deterministic draw in [0, 65521), the wire benchmark's step.
let private next (x: int) = (x * 1103 + 12345) % 65521

/// String bodies that exercise the string routine's runs, escapes and surrogate refusals.
let private stringBodies =
    [| "a"
       "bcd"
       "\u00E9"
       string (char 0xD83D)
       string (char 0xDE00)
       "\\n"
       "\\\""
       "\\\\"
       "\\/"
       "\\u0041"
       "\\uD83D\\uDE00"
       "\\uD83D"
       "\\uDE00"
       "\\x"
       "\\u12"
       "\\u12G4"
       "r00007"
       "" |]

/// Number tokens: the Int32 edge, int53, past 2^53 (canonical and not), fractions, exponents, and
/// the JSON grammar's refusals.
let private numberTokens =
    [| "0"
       "-0"
       "7"
       "-12"
       "2147483647"
       "2147483648"
       "-2147483648"
       "-2147483649"
       "9007199254740992"
       "9007199254740993"
       "10000000000000000"
       "12345678901234567890"
       "1.5"
       "-0.25"
       "1e5"
       "1E+2"
       "2.5e-3"
       "1e400"
       "01"
       "1."
       "-"
       ".5"
       "1e"
       "0.0" |]

let private grammarChars = "[]{},:\"\\-0123456789.eEtfn x\n"

/// A string literal from up to three bodies (some ill-formed: the parser refuses those).
let private stringLit (x: int) =
    let a = stringBodies.[x % stringBodies.Length]
    let b = stringBodies.[(x / 7) % stringBodies.Length]

    let c =
        if x % 3 = 0 then
            stringBodies.[(x / 11) % stringBodies.Length]
        else
            ""

    "\"" + a + b + c + "\""

/// A document of one of eight shapes, as text: homogeneous arrays (strings, numbers, Int32 integers),
/// mixed arrays, objects, nested arrays of strings, and the state shape.
let private document (seed: int) : string =
    let mutable x = seed % 65521

    let draw () =
        x <- next x
        x

    let many (f: unit -> string) =
        let k = draw () % 6
        "[" + String.concat "," [ for _ in 1..k -> f () ] + "]"

    let str () = stringLit (draw ())

    let num () =
        numberTokens.[draw () % numberTokens.Length]

    let int () = string (draw () - 32000)

    let scalar () =
        match draw () % 4 with
        | 0 -> str ()
        | 1 -> num ()
        | 2 -> "true"
        | _ -> "false"

    let obj (value: unit -> string) =
        let k = draw () % 4

        "{"
        + String.concat "," [ for i in 1..k -> "\"k" + string (i % 3) + "\":" + value () ]
        + "}"

    match seed % 8 with
    | 0 -> many str
    | 1 -> many num
    | 2 -> many int
    | 3 -> many scalar
    | 4 -> obj scalar
    | 5 -> many (fun () -> many str)
    | 6 ->
        "{\"scheme\":"
        + str ()
        + ",\"tokens\":"
        + many str
        + ",\"rowGroups\":"
        + str ()
        + "}"
    | _ -> obj (fun () -> many scalar)

/// Each document as written, truncated, with a character removed, and with a grammar character
/// inserted and substituted.
let private variants (seed: int) (doc: string) : string list =
    let n = doc.Length
    let mutable x = (seed * 31 + 7) % 65521

    let draw () =
        x <- next x
        x

    let at () = if n = 0 then 0 else draw () % n

    let ch () =
        string grammarChars.[draw () % grammarChars.Length]

    [ doc
      " " + doc + "\n"
      doc.Substring(0, at ())
      (let k = at () in if n = 0 then doc else doc.Remove(k, 1))
      (let k = at () in doc.Insert(k, ch ()))
      (let k = at () in if n = 0 then doc else doc.Remove(k, 1).Insert(k, ch ()))
      doc + ch () ]

let private corpusInputs: string list =
    Fuaran.Core.Tests.RefusalVectorTests.RefusalCorpus.vectors
    |> List.filter (fun v -> v.Codec = "json")
    |> List.map _.Input

let private benchInputs: string list =
    Fuaran.Core.WireBench.Corpus.corpora
    |> List.map (fun (_, build) -> Json.render (build ()))

let private generatedInputs: string list =
    [ for seed in 1..4000 do
          yield! variants seed (document seed) ]

let private pool: string list = corpusInputs @ benchInputs @ generatedInputs

/// Every disagreement between `reader` and `spec` over `inputs`, labelled, and the spec's tallies.
let private compare (name: string) (reader: string -> string) (spec: string -> string) (inputs: string list) =
    let mutable ok, mismatch, malformed = 0, 0, 0

    let diffs =
        [ for input in inputs do
              let s = spec input
              let r = reader input

              if s.StartsWith "ok " then ok <- ok + 1
              elif s = "mismatch" then mismatch <- mismatch + 1
              else malformed <- malformed + 1

              if r <> s then
                  let shown =
                      if input.Length > 120 then
                          input.Substring(0, 120) + "…"
                      else
                          input

                  yield sprintf "%s on %s\n  reader: %s\n  parse:  %s" name shown r s ]

    diffs, (ok, mismatch, malformed)

/// A reader outcome with its refusal position moved by one: the comparison must see it.
let private shifted (outcome: string) : string =
    let m =
        System.Text.RegularExpressions.Regex.Match(
            outcome,
            "^(malformed \\S+ @)(\\d+)( .*)$",
            System.Text.RegularExpressions.RegexOptions.Singleline
        )

    if m.Success then
        m.Groups.[1].Value + string (int m.Groups.[2].Value + 1) + m.Groups.[3].Value
    else
        outcome

let private mismatchAt (r: Result<'a, Json.ReadError>) =
    match r with
    | Error(Json.ReadError.Mismatch(pos, expected)) -> Some(pos, expected)
    | _ -> None

[<Tests>]
let tests =
    testList
        "JsonReader"
        [ test
              "every reading agrees with parseDetailed, refusal for refusal, over the parser corpus and the generated pool" {
              Expect.isGreaterThan corpusInputs.Length 20 "the codec refusal corpus carries its json vectors"

              for r in readings do
                  let diffs, (ok, mismatch, malformed) = compare r.Name r.Reader r.Spec pool

                  Expect.isEmpty (List.truncate 5 diffs) (sprintf "%s: %d disagreements" r.Name diffs.Length)
                  // Each reading meets all three outcomes, so no leg of the agreement is vacuous.
                  Expect.isGreaterThan ok 50 (sprintf "%s: accepted documents in the pool" r.Name)
                  Expect.isGreaterThan malformed 1000 (sprintf "%s: refused documents in the pool" r.Name)

                  if r.Name <> "value" then
                      Expect.isGreaterThan mismatch 50 (sprintf "%s: mismatched documents in the pool" r.Name)
          }

          test "the comparison can fail: refusal positions moved by one disagree on every refusal" {
              for r in readings |> List.filter (fun r -> r.Name = "value" || r.Name = "strings") do
                  let diffs, (_, _, malformed) = compare r.Name (r.Reader >> shifted) r.Spec pool
                  Expect.equal diffs.Length malformed (sprintf "%s: every refusal is seen moved" r.Name)
          }

          test "the typed readings of the benchmark corpora equal typing parse's tree" {
              let state = Json.render (Fuaran.Core.WireBench.Corpus.state ())
              let floats = Json.render (Fuaran.Core.WireBench.Corpus.floats ())

              let s = readings |> List.find (fun r -> r.Name = "state-shaped")
              let f = readings |> List.find (fun r -> r.Name = "floats")
              Expect.equal (s.Reader state) (s.Spec state) "state"
              Expect.stringStarts (s.Reader state) "ok " "the state corpus is read"
              Expect.equal (f.Reader floats) (f.Spec floats) "floats"
              Expect.stringStarts (f.Reader floats) "ok " "the float corpus is read"

              match Json.Reader.read stateShaped state with
              | Ok fs ->
                  match List.find (fun (k, _) -> k = "tokens") fs with
                  | _, Tokens a -> Expect.equal a.Length Fuaran.Core.WireBench.Corpus.stateTokens "every token read"
                  | _ -> failtest "tokens read as a value"
              | Error e -> failtestf "%A" e
          }

          test "a mismatch names the value's position and the shape asked for" {
              Expect.equal (mismatchAt (Json.Reader.read Json.Reader.strings "[\"a\",1]")) (Some(5, "a string")) "item"

              Expect.equal
                  (mismatchAt (Json.Reader.read Json.Reader.strings " [ \"a\" ,\n 1 ]"))
                  (Some(10, "a string"))
                  "after whitespace"

              Expect.equal (mismatchAt (Json.Reader.read Json.Reader.strings "{\"a\":1}")) (Some(0, "an array")) "root"

              Expect.equal
                  (mismatchAt (Json.Reader.read Json.Reader.ints "[1,2.5]"))
                  (Some(3, "an Int32 integer"))
                  "a float is not an int"

              Expect.equal
                  (mismatchAt (Json.Reader.read Json.Reader.ints "[2147483648]"))
                  (Some(1, "an Int32 integer"))
                  "past Int32 parse reads a JFloat"

              Expect.equal
                  (mismatchAt (Json.Reader.read Json.Reader.floats "[1,\"2\"]"))
                  (Some(3, "a number"))
                  "a string"

              Expect.equal
                  (mismatchAt (
                      Json.Reader.read
                          (Json.Reader.members (fun _ r -> Json.Reader.ints r |> ignore))
                          "{\"a\":[1],\"b\":{}}"
                  ))
                  (Some(13, "an array"))
                  "inside a member"
          }

          test "a grammar refusal anywhere outranks a mismatch, and is parse's own" {
              for input in [ "[\"a\", 1, tru]"; "[1, \"x\"] ]"; "[\"a\", 2, \"\\uD83D\"]"; "{\"a\":1,}" ] do
                  let expected =
                      match Json.parseDetailed input with
                      | Error e -> e
                      | Ok _ -> failtestf "%s parses" input

                  Expect.equal
                      (Json.Reader.read Json.Reader.strings input)
                      (Error(Json.ReadError.Malformed expected))
                      input
          }

          test "the depth cap is parse's, at the same position" {
              let deep = String.replicate 600 "[" + String.replicate 600 "]"
              let rec nestedItems (r: Json.Reader) = Json.Reader.items nestedItems r

              let expected =
                  match Json.parseDetailed deep with
                  | Error e -> e
                  | Ok _ -> failtest "600 levels parse"

              Expect.equal expected.Kind MaxDepthExceeded "parse's refusal is the cap"
              Expect.equal (Json.Reader.read nestedItems deep) (Error(Json.ReadError.Malformed expected)) "the reader's"

              let shallow = String.replicate 512 "[" + String.replicate 512 "]"
              Expect.equal (Json.Reader.read nestedItems shallow) (Ok()) "512 levels read, as they parse"
          }

          test "a reading that reads no value, or two where one is due, is a programming error" {
              Expect.throwsT<System.InvalidOperationException>
                  (fun () -> Json.Reader.read (fun _ -> ()) "[]" |> ignore)
                  "nothing read"

              Expect.throwsT<System.InvalidOperationException>
                  (fun () ->
                      Json.Reader.read (fun r -> Json.Reader.value r, Json.Reader.value r) "[]"
                      |> ignore)
                  "two values read"

              Expect.throwsT<System.InvalidOperationException>
                  (fun () -> Json.Reader.read (Json.Reader.members (fun _ _ -> ())) "{\"a\":1}" |> ignore)
                  "a member not read"

              Expect.throwsT<System.InvalidOperationException>
                  (fun () -> Json.Reader.read (Json.Reader.items (fun _ -> ())) "[1]" |> ignore)
                  "an item not read"

              Expect.equal
                  (Json.Reader.read (Json.Reader.items (fun _ -> ())) "[]")
                  (Ok())
                  "an empty array has nothing due"
          } ]
