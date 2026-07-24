module Fuaran.Core.Tests.DecodeTests

// Phase 241 — the portable, FSharp.Core-only JSON parser (`Json.parse`) that makes
// decode symmetric across the .NET and Fable pipelines. These run under .NET because the
// parser is now always-compiled (no `#if FABLE_COMPILER` gate) — the same code Fable
// emits, so green here is green there.

open Expecto
open Fuaran.Core

let private ok =
    function
    | Ok v -> v
    | Error m -> failtestf "expected Ok, got Error %s" m

[<Tests>]
let tests =
    testList
        "Decode (portable parser)"
        [ testCase "render ∘ parse is identity over canonical wire JSON"
          <| fun _ ->
              let v =
                  Json.kindObj
                      "doc"
                      [ "id", JStr "root"
                        "n", JInt 42
                        "flag", JBool true
                        "kids", JArr [ JStr "a"; JStr "b" ] ]

              let json = Json.render v
              Expect.equal (Json.parse json |> ok) v "parse reverses render"
              Expect.equal (Json.render (Json.parse json |> ok)) json "render reverses parse (byte-identical)"

          testCase "string escapes survive a round-trip"
          <| fun _ ->
              let v = JStr "a\"b\\c\nd\tef"
              Expect.equal (Json.parse (Json.render v) |> ok) v "all escape classes restored"

          testCase "parses integers as JInt and decimals/exponents as JFloat"
          <| fun _ ->
              Expect.equal (Json.parse "0" |> ok) (JInt 0) "zero"
              Expect.equal (Json.parse "-17" |> ok) (JInt -17) "negative int"
              Expect.equal (Json.parse "3.5" |> ok) (JFloat 3.5) "decimal ⇒ float"
              Expect.equal (Json.parse "1e3" |> ok) (JFloat 1000.0) "exponent ⇒ float"

          testCase "an int32-overflowing but int53-safe integer becomes JFloat (exact)"
          <| fun _ ->
              // 9_999_999_999 > Int32.Max but ≪ 2^53, so it round-trips exactly as a double.
              match Json.parse "9999999999" |> ok with
              | JFloat _ -> ()
              | other -> failtestf "expected JFloat, got %A" other

              // The 2^53 boundary is accepted (exactly representable).
              match Json.parse "9007199254740992" |> ok with
              | JFloat _ -> ()
              | other -> failtestf "expected JFloat at the int53 boundary, got %A" other

          testCase "an integer beyond the int53 safe range is rejected, not silently corrupted"
          <| fun _ ->
              // 2^53 + 1 and a 17-digit id both lose precision as a double, so they must
              // reject by name rather than round-trip to a DIFFERENT integer.
              for tok in [ "9007199254740993"; "99999999999999999" ] do
                  match Json.parse tok with
                  | Error m -> Expect.stringContains m "int53" (sprintf "%s names the safe-range rule" tok)
                  | Ok v -> failtestf "expected reject for %s, got %A (silent precision loss)" tok v

          testCase "the negative int53 boundary mirrors the positive one exactly"
          <| fun _ ->
              // The guard strips '-' before the lexical 16-digit compare, so -2^53 is
              // accepted (exactly representable) and -(2^53 + 1) rejects by name —
              // pinning the sign-stripping path the positive-boundary test can't see.
              match Json.parse "-9007199254740992" |> ok with
              | JFloat f -> Expect.equal f -9007199254740992.0 "-2^53 exact"
              | other -> failtestf "expected JFloat at -2^53, got %A" other

              for tok in [ "-9007199254740993"; "-99999999999999999" ] do
                  match Json.parse tok with
                  | Error m -> Expect.stringContains m "int53" (sprintf "%s names the safe-range rule" tok)
                  | Ok v -> failtestf "expected reject for %s, got %A (silent precision loss)" tok v

          testCase "a float token beyond the double range is rejected, never JFloat Infinity"
          <| fun _ ->
              // "1e400" is syntactically valid JSON but TryParses to +Infinity on
              // .NET Core; the wire model has no non-finite float (tryRender enforces
              // it at encode), so the decode side must reject rather than admit an
              // un-renderable value. Both signs, and a mantissa-overflow form.
              for tok in [ "1e400"; "-1e400"; "1.8e308"; "-1.8e308" ] do
                  match Json.parse tok with
                  | Error m -> Expect.stringContains m "finite" (sprintf "%s names the finiteness rule" tok)
                  | Ok v -> failtestf "expected reject for %s, got %A (non-finite admitted)" tok v

              // Near-max finite doubles still parse — the gate is exactly finiteness.
              match Json.parse "1.7e308" |> ok with
              | JFloat f -> Expect.isTrue (System.Double.IsFinite f) "1.7e308 is finite and accepted"
              | other -> failtestf "expected JFloat for 1.7e308, got %A" other

          testCase "JVal.asFloat absorbs the JInt/JFloat wire normalization"
          <| fun _ ->
              // render (JFloat 2.0) = "2" reparses as JInt 2 (one number population on
              // the wire) — asFloat is the blessed read path that sees both.
              Expect.equal (JVal.asFloat (Json.parse "2" |> ok)) (Some 2.0) "whole value via JInt"
              Expect.equal (JVal.asFloat (Json.parse "2.5" |> ok)) (Some 2.5) "fractional via JFloat"
              Expect.equal (JVal.asFloat (Json.render (JFloat 2.0) |> Json.parse |> ok)) (Some 2.0) "round-trip"
              Expect.equal (JVal.asFloat (JStr "2")) None "non-numeric is None"

          testCase "nested objects and arrays parse structurally"
          <| fun _ ->
              let json = "{\"a\":[1,2,{\"b\":false}],\"c\":{\"d\":[]}}"
              Expect.equal (Json.render (Json.parse json |> ok)) json "deep structure preserved"

          testCase "whitespace between tokens is ignored"
          <| fun _ ->
              Expect.equal
                  (Json.parse "  {  \"k\" : 1 ,  \"j\" : 2 }  " |> ok)
                  (JObj [ "k", JInt 1; "j", JInt 2 ])
                  "ws-tolerant"

          testCase "malformed JSON is rejected, naming the failure"
          <| fun _ ->
              Expect.isError (Json.parse "{not json") "unterminated object"
              Expect.isError (Json.parse "[1,2") "unterminated array"
              Expect.isError (Json.parse "\"abc") "unterminated string"
              Expect.isError (Json.parse "{\"k\":1} trailing") "trailing characters"

          testCase "a bare null is rejected by name (the wire model has no null)"
          <| fun _ ->
              match Json.parse "null" with
              | Error m -> Expect.stringContains m "null" "names null"
              | Ok v -> failtestf "expected reject, got %A" v

          // Phase 10 — nesting cap: deep input is a named Error, never a stack-overflow crash.
          testCase "parseWith rejects nesting deeper than the cap, naming the depth"
          <| fun _ ->
              let nest d =
                  String.replicate d "[" + String.replicate d "]"
              // cap = 3: depth-3 is fine, depth-4 is refused
              Expect.isOk (Json.parseWith 3 (nest 3)) "depth at the cap parses"

              match Json.parseWith 3 (nest 4) with
              | Error m -> Expect.stringContains m "max nesting depth" "names the depth cap"
              | Ok _ -> failtest "expected a depth Error"

          testCase "default parse tolerates ordinary nesting and a deep run does not crash"
          <| fun _ ->
              // 1000-deep array is well under defaultMaxDepth (512 objects/arrays… use < cap)
              let deep = String.replicate 400 "[" + String.replicate 400 "]"
              Expect.isOk (Json.parse deep) "400-deep parses under the default cap"
              // beyond the default cap: a named Error, not an exception
              let tooDeep = String.replicate 600 "[" + String.replicate 600 "]"
              Expect.isError (Json.parse tooDeep) "600-deep exceeds the default cap"

          // Phase 12 — non-finite floats: encode-time guard, not un-parseable wire.
          testCase "tryRender rejects non-finite floats, naming the token"
          <| fun _ ->
              match Json.tryRender (JFloat(0.0 / 0.0)) with
              | Error m -> Expect.stringContains m "NaN" "names NaN"
              | Ok s -> failtestf "expected reject, got %s" s

              match Json.tryRender (JArr [ JInt 1; JFloat(1.0 / 0.0) ]) with
              | Error m -> Expect.stringContains m "Infinity" "names Infinity"
              | Ok s -> failtestf "expected reject, got %s" s

          testCase "tryRender is Ok (render v) over an all-finite value"
          <| fun _ ->
              let v = Json.kindObj "x" [ "f", JFloat 3.5; "g", JArr [ JFloat -2.0 ] ]

              match Json.tryRender v with
              | Ok s -> Expect.equal s (Json.render v) "matches render for finite floats"
              | Error m -> failtestf "expected Ok, got Error %s" m

          // Phase 22 — structured parse error: a caller can branch on Kind + Position.
          testCase "parseDetailed classifies failures by kind and position"
          <| fun _ ->
              let kindOfErr input =
                  match Json.parseDetailed input with
                  | Error e -> Some(e.Kind)
                  | Ok _ -> None

              Expect.equal (kindOfErr "null") (Some JsonErrorKind.NullNotRepresentable) "null rejected by kind"
              Expect.equal (kindOfErr "{\"k\":1} x") (Some JsonErrorKind.TrailingCharacters) "trailing chars"
              Expect.equal (kindOfErr "\"abc") (Some JsonErrorKind.UnterminatedString) "unterminated string"
              Expect.equal (kindOfErr "[1,2") (Some JsonErrorKind.ExpectedToken) "array missing ',' or ']'"
              Expect.equal (kindOfErr "") (Some JsonErrorKind.UnexpectedEndOfInput) "empty input"

              Expect.equal
                  (kindOfErr (String.replicate 4 "["))
                  (Some JsonErrorKind.UnexpectedEndOfInput)
                  "open arrays run to EOF"

              // depth cap classifies as MaxDepthExceeded
              match Json.parseDetailedWith 3 (String.replicate 4 "[" + String.replicate 4 "]") with
              | Error e -> Expect.equal e.Kind JsonErrorKind.MaxDepthExceeded "depth cap kind"
              | Ok _ -> failtest "expected a depth error"

              // a real position is carried
              match Json.parseDetailed "  @" with
              | Error e ->
                  Expect.equal e.Kind JsonErrorKind.UnexpectedChar "unexpected char kind"
                  Expect.equal e.Position 2 "position points at the '@'"
              | Ok _ -> failtest "expected an unexpected-char error"

          testCase "parse string errors are byte-identical to parseDetailed reformatted"
          <| fun _ ->
              for input in [ "null"; "\"abc"; "{\"k\":1} x"; "@" ] do
                  match Json.parse input, Json.parseDetailed input with
                  | Error s, Error e ->
                      Expect.equal
                          s
                          ("not valid JSON: " + e.Message + " at position " + string e.Position)
                          "format matches"
                  | _ -> failtestf "expected both to error on %s" input ]
