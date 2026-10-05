module Fuaran.Core.Tests.ReadInt32DigitCountTests

// Phase 372 — `Json.readInt32` refuses a token whose significant digits number more than ten before
// it asks the platform reader. Under Fable that reader throws and catches on overflow, so a decode
// paid an exception for every integer token past Int32 (whole doubles past 2^53, millisecond
// timestamps); the node figures are in benchmarks/results (the Phase 372 file). The answer must not
// move: this suite keeps the reader as it stood before the phase, verbatim, as the reference, and
// compares production with it over a pool built around both edges the pre-check could disturb — the
// ten-digit Int32 edge, and the leading zeros the reader has always accepted (`007` is 7), which a
// count of RAW digits would get wrong. Both hosts run the same path, so this suite holds the branch
// the node measurement depends on; the node half was compared before and after under Fable over the
// same shapes (the results file).
//
// Every reader is compared on the token as `readInt32` sees it, and every token that is a JSON number
// is also compared through `parseDetailed` — the value with its case, or the refusal's kind, message
// and position — so a reader that moved would show in the decode as well as in the function.

open Expecto
open Fuaran.Core
open System.Numerics

module private Reference =

    /// `Json.readInt32` before Phase 372, verbatim.
    let readInt32 (tok: string) : int option =
        let digitsFrom =
            if tok.Length > 0 && (tok.[0] = '-' || tok.[0] = '+') then
                1
            else
                0

        let mutable shaped = tok.Length > digitsFrom

        for k in digitsFrom .. tok.Length - 1 do
            if tok.[k] < '0' || tok.[k] > '9' then
                shaped <- false

        if not shaped then
            None
        else
            match
                System.Int32.TryParse(
                    tok,
                    System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            with
            | true, v -> Some v
            | _ -> None

    /// Wrong answer 1: the digit count taken over the RAW digits, leading zeros included — refuses
    /// `000000000001`, which the reader has always read as 1.
    let rawDigitCount (tok: string) : int option =
        let digitsFrom =
            if tok.Length > 0 && (tok.[0] = '-' || tok.[0] = '+') then
                1
            else
                0

        if tok.Length - digitsFrom > 10 then None else readInt32 tok

    /// Wrong answer 2: the edge one digit short — refuses every ten-digit value, in range or not.
    let nineDigits (tok: string) : int option =
        let digitsFrom =
            if tok.Length > 0 && (tok.[0] = '-' || tok.[0] = '+') then
                1
            else
                0

        let mutable first = digitsFrom

        while first < tok.Length && tok.[first] = '0' do
            first <- first + 1

        if tok.Length - first > 9 then None else readInt32 tok

/// A deterministic 64-bit draw (Knuth's MMIX constants), reading no clock and no random source.
let private draws (seed: uint64) (count: int) : uint64 list =
    let mutable x = seed

    [ for _ in 1..count do
          x <- x * 6364136223846793005UL + 1442695040888963407UL
          yield x ]

/// `c - r` .. `c + r`, not negative, each with no sign, `-` and `+`, and with 0, 1, 2 and 12 leading
/// zeros.
let private around (c: BigInteger) (r: int) : string list =
    [ for d in -r .. r do
          let v = c + BigInteger d

          if v.Sign >= 0 then
              for sign in [ ""; "-"; "+" ] do
                  for zeros in [ 0; 1; 2; 12 ] do
                      yield sign + System.String('0', zeros) + v.ToString() ]

/// The pool: the Int32 edges (and the ten-digit band around them), the int53 edge, every run of
/// zeros with and without a sign (`-0` among them), the integer tokens of the wire benchmark's
/// corpora, generated digit strings of 1 to 24 digits with signs and leading zeros, and shapes the
/// reader refuses before any count.
let private pool: string list =
    let edges =
        [ around (BigInteger 2147483647) 200
          around (BigInteger 999999999) 20
          around (BigInteger 1000000000) 20
          around (BigInteger 9999999999L) 200
          around (BigInteger 10000000000L) 20
          around (BigInteger 9007199254740992L) 50
          around (BigInteger.Pow(BigInteger 10, 17)) 10 ]
        |> List.concat

    let zeroRuns =
        [ for sign in [ ""; "-"; "+" ] do
              for zeros in 0..14 do
                  yield sign + System.String('0', zeros)
                  yield sign + System.String('0', zeros) + "0"
                  yield sign + System.String('0', zeros) + "1"
                  yield sign + System.String('0', zeros) + "2147483648" ]

    let corpusIntegers =
        [ Fuaran.Core.WireBench.Corpus.floats ()
          Fuaran.Core.WireBench.Corpus.opStream () ]
        |> List.collect (fun v ->
            System.Text.RegularExpressions.Regex.Matches(Json.render v, "-?[0-9]+(\\.[0-9]+)?([eE][+-]?[0-9]+)?")
            |> Seq.map (fun m -> m.Value)
            |> Seq.filter (fun t -> t.IndexOfAny [| '.'; 'e'; 'E' |] < 0)
            |> List.ofSeq)

    let generated =
        let ds = draws 372UL 40_000 |> Array.ofList

        [ for k in 0 .. ds.Length / 4 - 1 do
              let a, b, c, d = ds.[4 * k], ds.[4 * k + 1], ds.[4 * k + 2], ds.[4 * k + 3]
              let len = int (a % 24UL) + 1
              let digits = string (BigInteger b * BigInteger c) + string d
              let body = string (1UL + b % 9UL) + digits.Substring(0, min (len - 1) digits.Length)
              let sign = [| ""; "-"; "+" |].[int (c % 3UL)]
              let zeros = if d % 4UL = 0UL then int (a % 13UL) else 0
              yield sign + System.String('0', zeros) + body ]

    let refusedShapes =
        [ ""
          "-"
          "+"
          " 5"
          "5 "
          "1a"
          "--1"
          "+-1"
          "1.5"
          "1e3"
          "0x1F"
          "١"
          "7\u0000"
          "12345678901\u0000" ]

    edges @ zeroRuns @ corpusIntegers @ generated @ refusedShapes |> List.distinct

/// The significant digits of a token the reader would shape-accept, else -1.
let private significant (tok: string) : int =
    let digits =
        if tok.StartsWith "-" || tok.StartsWith "+" then
            tok.Substring 1
        else
            tok

    if digits.Length = 0 || not (digits |> Seq.forall System.Char.IsAsciiDigit) then
        -1
    else
        digits.TrimStart('0').Length

/// A value's shape by case, recursively: `JArr(JInt)` for `[5]`, so a whole number read as a float
/// is told apart from the same number read as an integer, which render alike.
let rec private kind (v: JVal) : string =
    match v with
    | JArr xs -> "JArr(" + (xs |> List.map kind |> String.concat ",") + ")"
    | JObj ms ->
        "JObj("
        + (ms |> List.map (fun (k, x) -> k + ":" + kind x) |> String.concat ",")
        + ")"
    | _ -> v.GetType().Name

/// One decode outcome as compared: the value with its shape, or the refusal as kind, position and
/// message.
let private shown (r: Result<JVal, JsonError>) : string =
    match r with
    | Ok v -> sprintf "ok %s %s" (kind v) (Json.render v)
    | Error e -> sprintf "err %A @%d %s" e.Kind e.Position e.Message

let private decoded (input: string) : string = shown (Json.parseDetailed input)

/// Where `candidate` and the reference disagree, as the reader answers.
let private disagreements (candidate: string -> int option) : (string * int option * int option) list =
    [ for tok in pool do
          let got, want = candidate tok, Reference.readInt32 tok

          if got <> want then
              yield tok, got, want ]

[<Tests>]
let tests =
    testList
        "ReadInt32DigitCount"
        [ test "the pool reaches both sides of the count and both sides of the ten-digit edge" {
              let count (p: string -> bool) = pool |> List.filter p |> List.length
              Expect.isGreaterThan (count (fun t -> significant t > 10)) 3_000 "refused by the count"

              Expect.isGreaterThan
                  (count (fun t -> significant t > 10 && significant t < t.TrimStart('-', '+').Length))
                  300
                  "refused by the count, with leading zeros"

              Expect.isGreaterThan
                  (count (fun t -> significant t = 10 && (Reference.readInt32 t).IsSome))
                  1_000
                  "ten digits, in range"

              Expect.isGreaterThan
                  (count (fun t -> significant t = 10 && (Reference.readInt32 t).IsNone))
                  1_000
                  "ten digits, out of range: the platform reader's to refuse"

              Expect.isGreaterThan
                  (count (fun t ->
                      significant t <= 10
                      && significant t < t.TrimStart('-', '+').Length
                      && t.Length > 11))
                  100
                  "longer than ten characters, but ten significant digits or fewer"
          }

          test "readInt32 answers as the pre-372 reader on every token" {
              let bad = disagreements Json.readInt32
              Expect.equal (List.truncate 5 bad) [] (sprintf "%d of %d tokens disagree" bad.Length pool.Length)
          }

          test "every JSON number in the pool decodes as before: value and case, or refusal kind, message and position" {
              // The decode reads through readInt32, so equal readers give equal decodes; this holds
              // the claim at the surface a consumer sees, bare, in an array and as a member value.
              let numbers = pool |> List.filter Json.isJsonNumber
              Expect.isGreaterThan numbers.Length 5_000 "the pool carries JSON numbers"

              let bad =
                  [ for tok in numbers do
                        for input in [ tok; "[" + tok + "]"; "{\"k\":" + tok + "}" ] do
                            let got = decoded input

                            let want =
                                // the Int32 branch is the only one readInt32 decides; past it the
                                // decode is the same code either side of the phase
                                match Reference.readInt32 tok with
                                | Some v ->
                                    let one = JInt v

                                    shown (
                                        Ok(
                                            if input.StartsWith "[" then JArr [ one ]
                                            elif input.StartsWith "{" then JObj [ "k", one ]
                                            else one
                                        )
                                    )
                                | None -> got

                            if got <> want then
                                yield input, got, want ]

              Expect.equal (List.truncate 5 bad) [] (sprintf "%d decodes disagree" bad.Length)

              let integers =
                  numbers |> List.filter (fun t -> (Reference.readInt32 t).IsSome) |> List.length

              Expect.isGreaterThan integers 1_000 "the comparison covers the Int32 branch"
          }

          test "named pins: the Int32 edges, leading zeros, -0, and the first token the count refuses" {
              Expect.equal (Json.readInt32 "2147483647") (Some System.Int32.MaxValue) "the high edge"
              Expect.equal (Json.readInt32 "2147483648") None "one past it: ten digits, the platform's to refuse"
              Expect.equal (Json.readInt32 "-2147483648") (Some System.Int32.MinValue) "the low edge"
              Expect.equal (Json.readInt32 "-2147483649") None "one past it"
              Expect.equal (Json.readInt32 "+2147483647") (Some System.Int32.MaxValue) "a plus sign"
              Expect.equal (Json.readInt32 "-0") (Some 0) "-0 is 0"
              Expect.equal (Json.readInt32 "-000000000000") (Some 0) "a run of zeros has no significant digit"

              Expect.equal
                  (Json.readInt32 "0000000000002147483647")
                  (Some System.Int32.MaxValue)
                  "twelve leading zeros do not count"

              Expect.equal (Json.readInt32 "-0000000000002147483648") (Some System.Int32.MinValue) "nor on the low edge"

              Expect.equal (Json.readInt32 "9999999999") None "the largest ten-digit token"
              Expect.equal (Json.readInt32 "10000000000") None "eleven significant digits: refused by the count"
              Expect.equal (Json.readInt32 "1759622400000") None "a millisecond timestamp"
              Expect.equal (Json.readInt32 "9007199254740993") None "past 2^53"
              Expect.equal (decoded "1759622400000") "ok JFloat 1759622400000" "the timestamp decodes as a float"

              Expect.equal
                  (decoded "-9007199254740994")
                  "ok JFloat -9007199254740994"
                  "a whole double past 2^53 still reaches the Phase-253 check"
          }

          test "go-red: counting raw digits, leading zeros included, is caught" {
              Expect.isNonEmpty (disagreements Reference.rawDigitCount) "a raw count refuses 000000000001"
          }

          test "go-red: an edge one digit short is caught" {
              Expect.isNonEmpty (disagreements Reference.nineDigits) "a nine-digit edge refuses 2147483647"
          } ]
