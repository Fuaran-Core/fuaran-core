module Fuaran.Core.Tests.StringParseRunTests

// Phase 366 — a string is parsed by runs, not characters. `Json.parseDetailedWithPolicy`'s
// `parseString` now walks each escape-free stretch, checks the surrogate pairing of every unit as it
// passes (Phase 299), and copies the stretch whole. Its refusals are load-bearing: the KIND, the
// MESSAGE and the POSITION of every one must be what the one-character-at-a-time reader produced.
//
// This suite pins that against `Oracle.read`, the reader as it stood before the phase, kept here
// verbatim as the reference: an exhaustive sweep over every sequence of up to four tokens drawn from
// a run-boundary alphabet (raw runs, raw high and low surrogates, every short escape, `\u` escapes of
// a BMP character and of each surrogate half, and every malformed escape), each sequence closed and
// left open; then each refusal kind named at the start, the middle and the end of a run and across a
// run/escape boundary, with its position stated; and a go-red over two plausible broken readers, so
// the sweep is shown able to fail.

open Expecto
open Fuaran.Core

/// The outcome of reading one string literal at the head of an input: the decoded string and the
/// index just past its closing quote, or the refusal as kind, message and position.
type private Read = Result<string * int, JsonErrorKind * string * int>

module private Oracle =

    exception private Refused of JsonErrorKind * string * int

    let private isHigh (u: int) = u >= 0xD800 && u <= 0xDBFF
    let private isLow (u: int) = u >= 0xDC00 && u <= 0xDFFF

    let private hexDigit (fail: JsonErrorKind -> string -> int) (c: char) : int =
        if c >= '0' && c <= '9' then int c - int '0'
        elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
        elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
        else fail BadHexDigit "bad hex digit in \\u escape"

    /// The reader before Phase 366, one unit at a time, clause for clause. `input.[0]` is the
    /// opening quote. `resetAtEscape` and `dropAtBoundary` build the two BROKEN readers the go-red
    /// uses: one forgets a pending high surrogate when an escape begins, one loses the last unit of
    /// a run that an escape ends. Both are false for the reference.
    let readWith (resetAtEscape: bool) (dropAtBoundary: bool) (input: string) : Read =
        let n = input.Length
        let mutable i = 1
        let fail kind msg : 'a = raise (Refused(kind, msg, i))
        let sb = System.Text.StringBuilder()
        let mutable fin = false
        let mutable pendingHigh = false

        let append (u: int) =
            if pendingHigh && not (isLow u) then
                fail BadEscape "ill-formed string: a high surrogate not followed by a low surrogate"
            elif not pendingHigh && isLow u then
                fail BadEscape "ill-formed string: a low surrogate with no high surrogate before it"

            pendingHigh <- isHigh u
            sb.Append(char u) |> ignore

        try
            while not fin do
                if i >= n then
                    fail UnterminatedString "unterminated string"

                let c = input.[i]
                i <- i + 1

                match c with
                | '"' ->
                    if pendingHigh then
                        fail BadEscape "ill-formed string: a high surrogate not followed by a low surrogate"

                    fin <- true
                | '\\' ->
                    if resetAtEscape then
                        pendingHigh <- false

                    if dropAtBoundary && i >= 2 && input.[i - 2] <> '"' && sb.Length > 0 then
                        sb.Length <- sb.Length - 1

                    if i >= n then
                        fail UnterminatedEscape "unterminated escape"

                    let e = input.[i]
                    i <- i + 1

                    match e with
                    | '"' -> append (int '"')
                    | '\\' -> append (int '\\')
                    | '/' -> append (int '/')
                    | 'n' -> append (int '\n')
                    | 'r' -> append (int '\r')
                    | 't' -> append (int '\t')
                    | 'b' -> append (int '\b')
                    | 'f' -> append (int '\f')
                    | 'u' ->
                        if i + 4 > n then
                            fail TruncatedUnicodeEscape "truncated \\u escape"

                        let hex = hexDigit fail

                        let code =
                            (hex input.[i] <<< 12)
                            + (hex input.[i + 1] <<< 8)
                            + (hex input.[i + 2] <<< 4)
                            + hex input.[i + 3]

                        i <- i + 4
                        append code
                    | _ -> fail BadEscape ("bad escape '\\" + string e + "'")
                | _ -> append (int c)

            Ok(sb.ToString(), i)
        with Refused(k, m, p) ->
            Error(k, m, p)

    let read = readWith false false

/// Production's reading of the same input, in the oracle's shape. A string the oracle closes before
/// the end of the input is parsed on its own (the rest would be trailing characters, which are not
/// this phase's), so the comparison is of the string reader and nothing else.
let private production (oracle: Read) (input: string) : Read =
    let text =
        match oracle with
        | Ok(_, past) -> input.Substring(0, past)
        | Error _ -> input

    match Json.parseDetailedWithPolicy RejectNull Json.defaultMaxDepth text with
    | Ok(JStr s) -> Ok(s, text.Length)
    | Ok other -> Error(UnexpectedChar, sprintf "not a string: %A" other, -1)
    | Error e -> Error(e.Kind, e.Message, e.Position)

/// The run-boundary alphabet. Each token is a raw stretch, a raw surrogate half, an escape (good or
/// malformed), or a bare quote; sequences of them place every refusal at the start, the middle and
/// the end of a run, and on each side of a run/escape boundary.
let private tokens: (string * string) list =
    [ "a", "a"
      "run", "bcd"
      "bmp", "\u00E9"
      "hi", string (char 0xD83D)
      "lo", string (char 0xDE00)
      "\\n", "\\n"
      "\\\"", "\\\""
      "\\\\", "\\\\"
      "\\/", "\\/"
      "\\u0041", "\\u0041"
      "\\uD83D", "\\uD83D"
      "\\uDE00", "\\uDE00"
      "\\x", "\\x"
      "\\u12", "\\u12"
      "\\u12G4", "\\u12G4"
      "\\", "\\"
      "quote", "\"" ]

/// Every sequence of 0 to `maxLen` tokens, each opened by a quote and both closed and left open.
let private sweepInputs (maxLen: int) : (string * string) list =
    let rec seqs k : (string list * string list) list =
        if k = 0 then
            [ [], [] ]
        else
            [ for names, bodies in seqs (k - 1) do
                  for name, body in tokens do
                      yield name :: names, body :: bodies ]

    [ for k in 0..maxLen do
          for names, bodies in seqs k do
              let label = names |> List.rev |> String.concat " "
              let body = bodies |> List.rev |> String.concat ""
              yield label + " | closed", "\"" + body + "\""
              yield label + " | open", "\"" + body ]

/// The inputs whose answers differ between `read` and the oracle, labelled.
let private mismatchesOf (read: string -> Read) (inputs: (string * string) list) : string list =
    [ for label, input in inputs do
          let expected = Oracle.read input
          let actual = read input

          if actual <> expected then
              yield sprintf "%s: expected %A, got %A" label expected actual ]

let private productionRead (input: string) = production (Oracle.read input) input

/// One named input, with the refusal the reader reports for it stated outright — kind, message and
/// position — or `None` where the string is well-formed and read whole.
let private pinned: (string * string * (JsonErrorKind * string * int) option) list =
    let hi = string (char 0xD83D)
    let lo = string (char 0xDE00)
    let loneHigh = "ill-formed string: a high surrogate not followed by a low surrogate"
    let loneLow = "ill-formed string: a low surrogate with no high surrogate before it"

    [ "a lone low at the start of a run", "\"" + lo + "abc\"", Some(BadEscape, loneLow, 2)
      "a lone low in the middle of a run", "\"ab" + lo + "c\"", Some(BadEscape, loneLow, 4)
      "a lone low at the end of a run", "\"abc" + lo + "\"", Some(BadEscape, loneLow, 5)
      "a lone low after an escape, at the start of the next run", "\"\\n" + lo + "\"", Some(BadEscape, loneLow, 4)
      "a high at the end of a run, the string then closed", "\"ab" + hi + "\"", Some(BadEscape, loneHigh, 5)
      "a high at the start of a run, then a raw unit", "\"" + hi + "x\"", Some(BadEscape, loneHigh, 3)
      "a high in the middle of a run, then a raw unit", "\"a" + hi + "bc\"", Some(BadEscape, loneHigh, 4)
      "a raw high ending a run, then a short escape", "\"a" + hi + "\\n\"", Some(BadEscape, loneHigh, 5)
      "a raw high ending a run, then a \\u low: the pair is whole", "\"a" + hi + "\\uDE00\"", None
      "an escaped high, then a raw low starting the next run: whole", "\"\\uD83D" + lo + "b\"", None
      "an escaped high, then a raw non-low starting the next run", "\"\\uD83Dxy\"", Some(BadEscape, loneHigh, 8)
      "an escaped high closing the string", "\"\\uD83D\"", Some(BadEscape, loneHigh, 8)
      "a bad escape at the end of a run", "\"abc\\x\"", Some(BadEscape, "bad escape '\\x'", 6)
      "a bad hex digit after a run", "\"ab\\u12G4\"", Some(BadHexDigit, "bad hex digit in \\u escape", 5)
      "a truncated \\u escape after a run", "\"ab\\u12", Some(TruncatedUnicodeEscape, "truncated \\u escape", 5)
      "an unterminated escape after a run", "\"abc\\", Some(UnterminatedEscape, "unterminated escape", 5)
      "an unterminated string inside a run", "\"abc", Some(UnterminatedString, "unterminated string", 4)
      "an unterminated string after an escape and a run", "\"\\nab", Some(UnterminatedString, "unterminated string", 5)
      "an unterminated string, empty", "\"", Some(UnterminatedString, "unterminated string", 1) ]

[<Tests>]
let tests =
    testList
        "StringParseRuns"
        [ testCase "run boundaries (Phase 366): every token sequence up to four reads as the one-unit oracle reads it"
          <| fun _ ->
              let inputs = sweepInputs 4
              Expect.isGreaterThan inputs.Length 160000 "the sweep is the one written"

              let refusals =
                  inputs
                  |> List.filter (fun (_, s) -> Result.isError (Oracle.read s))
                  |> List.length

              Expect.isGreaterThan refusals 100000 "and most of it is refusals, of every kind"
              Expect.equal (mismatchesOf productionRead inputs |> List.truncate 10) [] "no input differs"

          testCase "run boundaries (Phase 366): the sweep reaches every refusal kind a string can raise"
          <| fun _ ->
              let kinds =
                  sweepInputs 3
                  |> List.choose (fun (_, s) ->
                      match Oracle.read s with
                      | Error(k, m, _) -> Some(k, m.StartsWith "ill-formed string: a low")
                      | Ok _ -> None)
                  |> List.distinct
                  |> List.sortBy (sprintf "%A")

              Expect.equal
                  kinds
                  ([ UnterminatedString, false
                     UnterminatedEscape, false
                     TruncatedUnicodeEscape, false
                     BadEscape, false
                     BadEscape, true
                     BadHexDigit, false ]
                   |> List.sortBy (sprintf "%A"))
                  "unterminated string and escape, truncated \\u, bad escape, both surrogate refusals, bad hex"

          testCase "each refusal at the start, middle and end of a run keeps its kind, message and position"
          <| fun _ ->
              for label, input, refusal in pinned do
                  let oracle = Oracle.read input

                  match refusal with
                  | Some r -> Expect.equal oracle (Error r) (label + " - the oracle states it")
                  | None -> Expect.isOk oracle (label + " - the oracle reads it whole")

                  Expect.equal (productionRead input) oracle label

          testCase "the common string, with no escape, decodes to its own text"
          <| fun _ ->
              for s in [ ""; "plain"; "\u00E9 \U0001F600 /" ] do
                  Expect.equal (Json.parseDetailed ("\"" + s + "\"")) (Ok(JStr s)) (sprintf "%A" s)

              Expect.equal
                  (Json.parseDetailed "{\"k\":\"ab\\ncd\",\"e\":\"\\u00e9x\"}")
                  (Ok(JObj [ "k", JStr "ab\ncd"; "e", JStr "\u00E9x" ]))
                  "runs on both sides of an escape are copied whole"

          testCase "run boundaries (Phase 366): two broken readers are red against the oracle"
          <| fun _ ->
              let inputs = sweepInputs 3
              let forgetful = mismatchesOf (Oracle.readWith true false) inputs
              let lossy = mismatchesOf (Oracle.readWith false true) inputs

              Expect.isNonEmpty forgetful "a reader that drops a pending high at an escape is caught"

              Expect.exists
                  forgetful
                  (fun m -> m.StartsWith "hi \\n | closed:")
                  "on a raw high ending a run before a short escape"

              Expect.isNonEmpty lossy "a reader that loses a run's last unit at an escape is caught"

              Expect.exists lossy (fun m -> m.StartsWith "run \\n | closed:") "on a run before a short escape" ]
