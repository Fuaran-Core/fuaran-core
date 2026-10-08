module Fuaran.Core.Tests.FloatLayoutCheckTests

// Phase 367 — the parsed-float check, measured and narrowed. `parseNumber` admits an integer token
// past 2^53 exactly when it is the canonical float layout of the double it reads as (Phase 253:
// `FloatLayout.finite v = tok`). Under Fable that comparison re-laid every such value through
// `reLay`; Phase 367 skipped the re-lay in the band [2^53, 1e17), where it is the identity, and
// Phase 373 moved that skip into `FloatLayout.finite` itself, over the wider band [1e-4, 1e17), so
// the parser asks `finite v = tok` again and the band lives in one place. On .NET the question is
// still `finite v = tok`, so this suite pins the .NET half; the node half was compared before and
// after under Fable over the same pool (benchmarks/results, the Phase 367 and 373 files), and the
// `jsonParse/*` parity vectors sit on each side of both edges for the consumer's node leg.
//
// The reference is `Reference.decideWith`, the reader's decision as it stood before the phase,
// clause for clause, with the Phase-253 question a parameter: the reference passes the pre-367
// expression verbatim, and the go-red passes two plausible wrong answers to it, so the sweep is shown
// able to fail. Every outcome is compared as the value, or the refusal's kind, message and position.

open Expecto
open Fuaran.Core
open System.Numerics

let private inv = System.Globalization.CultureInfo.InvariantCulture

module private Reference =

    exception private Refused of JsonErrorKind * string

    /// `parseNumber` before Phase 367 from the end of its scan, verbatim but for `canonical`, which
    /// is the Phase-253 question (`FloatLayout.finite v = tok` in the reference). `tok` is the whole
    /// scanned token; a refusal's position is the end of it, where the parser's `fail` reads `i`.
    let decideWith (canonical: float -> string -> bool) (tok: string) : Result<JVal, JsonErrorKind * string> =
        let fail kind msg : 'a = raise (Refused(kind, msg))
        let isFloat = tok.IndexOfAny [| '.'; 'e'; 'E' |] >= 0

        try
            if not (Json.isJsonNumber tok) then
                fail MalformedNumber ("malformed number: " + tok)

            let asFloat () =
                match System.Double.TryParse(tok, System.Globalization.NumberStyles.Float, inv) with
                | true, v when System.Double.IsNaN v || System.Double.IsInfinity v ->
                    fail
                        MalformedNumber
                        ("number outside the finite double range; it cannot round-trip on the wire: "
                         + tok)
                | true, v -> JFloat v
                | _ -> fail MalformedNumber ("malformed number: " + tok)

            if isFloat then
                Ok(asFloat ())
            else
                match Json.readInt32 tok with
                | Some v -> Ok(JInt v)
                | None ->
                    let digits = if tok.StartsWith "-" then tok.Substring 1 else tok

                    let int53Safe =
                        digits.Length < 16
                        || (digits.Length = 16
                            && System.String.CompareOrdinal(digits, "9007199254740992") <= 0)

                    if int53Safe then
                        match System.Double.TryParse(tok, System.Globalization.NumberStyles.Float, inv) with
                        | true, v -> Ok(JFloat v)
                        | _ -> fail MalformedNumber ("malformed number: " + tok)
                    else
                        match System.Double.TryParse(tok, System.Globalization.NumberStyles.Float, inv) with
                        | true, v when not (System.Double.IsNaN v || System.Double.IsInfinity v) && canonical v tok ->
                            Ok(JFloat v)
                        | _ ->
                            fail
                                MalformedNumber
                                ("integer literal outside the int53 safe range (|n| > 2^53); it cannot round-trip without precision loss: "
                                 + tok)
        with Refused(k, m) ->
            Error(k, m)

    /// The pre-367 question, verbatim.
    let reference (v: float) (tok: string) = FloatLayout.finite v = tok

    /// Wrong answer 1: no check at all — every finite read past 2^53 admitted.
    let noCheck (_: float) (_: string) = true

    /// Wrong answer 2: a digit comparison against the double's EXACT value rather than its shortest
    /// digits — the comparison this phase must not become.
    let exactDigits (v: float) (tok: string) = v.ToString("F0", inv) = tok

/// One outcome as compared: the value rendered, or the refusal as kind, position and message.
let private shown (r: Result<JVal, JsonErrorKind * string * int>) : string =
    match r with
    | Ok v -> "ok " + Json.render v
    | Error(k, m, at) -> sprintf "err %A @%d %s" k at m

/// What production says of `tok` bare and as an array's one item, beside what `decide` says.
let private pairs (decide: string -> Result<JVal, JsonErrorKind * string>) (tok: string) : (string * string) list =
    let prod (input: string) =
        match Json.parseDetailed input with
        | Ok v -> Ok v
        | Error e -> Error(e.Kind, e.Message, e.Position)

    let expected (wrap: JVal -> JVal) (offset: int) =
        match decide tok with
        | Ok v -> Ok(wrap v)
        | Error(k, m) -> Error(k, m, offset + tok.Length)

    [ shown (prod tok), shown (expected id 0)
      shown (prod ("[" + tok + "]")), shown (expected (fun v -> JArr [ v ]) 1) ]

/// A deterministic 64-bit draw (Knuth's MMIX constants), reading no clock and no random source.
let private draws (seed: uint64) (count: int) : uint64 list =
    let mutable x = seed

    [ for _ in 1..count do
          x <- x * 6364136223846793005UL + 1442695040888963407UL
          yield x ]

/// Integers `c - r` .. `c + r` that are not negative, each also with a minus sign.
let private around (c: BigInteger) (r: int) : string list =
    [ for d in -r .. r do
          let v = c + BigInteger d

          if v.Sign >= 0 then
              yield v.ToString()
              yield "-" + v.ToString() ]

/// The pool: the oracle suite's number inputs, the int53 boundary, the band's edges, the layouts of
/// generated whole doubles below, in and above the band with their near misses, and random digit
/// strings. Every token is one the scanner reads whole, so the reference's position is its end.
let private pool: string list =
    let named =
        [ "0"
          "-0"
          "007"
          "0009007199254740992"
          "9007199254740992"
          "9007199254740993"
          "2147483647"
          "2147483648"
          "-2147483648"
          "-2147483649"
          "99999999999999999"
          "10000000000000000"
          "9007199254740994"
          "-9007199254740994"
          "18205257897171752"
          "-"
          "1.5"
          "1e3"
          "1e+3"
          "1.5e-3"
          "1e"
          "1e+"
          "1."
          "1e400"
          "-1e400"
          "99999999999999984"
          "99999999999999980" ]

    let edges =
        [ BigInteger.Pow(2I, 53), 400
          BigInteger.Pow(2I, 54), 100
          BigInteger.Pow(10I, 15), 50
          BigInteger.Pow(10I, 16), 400
          BigInteger.Pow(10I, 17), 400
          BigInteger.Parse "99999999999999984", 50
          BigInteger.Pow(10I, 19), 20 ]
        |> List.collect (fun (c, r) -> around c r)

    let layouts =
        draws 367UL 4_000
        |> List.collect (fun x ->
            let mantissa = float (x >>> 11) // 53 bits
            let scale = 2.0 ** float (int (x % 8UL) - 3) // 2^50 .. 2^57: below, in and above the band
            let s = FloatLayout.finite (System.Math.Round(mantissa * scale))

            if s.Contains "E" then
                [ s; "-" + s ]
            else
                let b = BigInteger.Parse s
                let last = int s[s.Length - 1] - int '0'

                [ s
                  "-" + s
                  (b + 1I).ToString()
                  (b - 1I).ToString()
                  (b + 2I).ToString()
                  s + "0"
                  s.Substring(0, s.Length - 1)
                  s.Substring(0, s.Length - 1) + string ((last + 5) % 10) ])

    let digitStrings =
        draws 6553UL 3_000
        |> List.map (fun x ->
            let len = int (x % 22UL) + 1

            let s =
                draws x len
                |> List.map (fun d -> string (int ((d >>> 33) % 10UL)))
                |> String.concat ""

            if x % 3UL = 0UL then "-" + s else s)

    named @ edges @ layouts @ digitStrings |> List.distinct

/// The tokens that reach the Phase-253 question: integer-shaped, grammatical, past 2^53.
let private reachesCheck (tok: string) : bool =
    let digits = if tok.StartsWith "-" then tok.Substring 1 else tok

    digits.Length > 0
    && digits |> Seq.forall System.Char.IsAsciiDigit
    && not (digits.Length > 1 && digits[0] = '0')
    && (digits.Length > 16
        || (digits.Length = 16
            && System.String.CompareOrdinal(digits, "9007199254740992") > 0))

let private disagreements (canonical: float -> string -> bool) : (string * string * string) list =
    [ for tok in pool do
          for (got, want) in pairs (Reference.decideWith canonical) tok do
              if got <> want then
                  yield tok, got, want ]

[<Tests>]
let tests =
    testList
        "FloatLayoutCheck"
        [ test "the pool reaches the check, both ways, in and out of the toString band" {
              let reaching = pool |> List.filter reachesCheck

              let admitted, refused =
                  reaching
                  |> List.partition (fun t -> Reference.decideWith Reference.reference t |> Result.isOk)

              let inBand (t: string) =
                  let m = abs (System.Double.Parse(t, inv))
                  m >= 9007199254740992.0 && m < 1e17

              Expect.isGreaterThan (List.length admitted) 3_000 "canonical layouts past 2^53 are admitted"
              Expect.isGreaterThan (List.length refused) 3_000 "near misses past 2^53 are refused"
              Expect.isGreaterThan (admitted |> List.filter inBand |> List.length) 2_000 "admitted inside the band"
              Expect.isGreaterThan (refused |> List.filter inBand |> List.length) 2_000 "refused inside the band"
              Expect.isGreaterThan (refused |> List.filter (inBand >> not) |> List.length) 500 "refused past the band"
          }

          test "every number outcome is the pre-367 reader's: value, or refusal kind, message and position" {
              let bad = disagreements Reference.reference
              Expect.equal (List.truncate 5 bad) [] (sprintf "%d of %d tokens disagree" bad.Length pool.Length)
          }

          test "the boundary is pinned by name, both sides of 2^53 and of 1e17" {
              let says (tok: string) =
                  shown (
                      match Json.parseDetailed tok with
                      | Ok v -> Ok v
                      | Error e -> Error(e.Kind, e.Message, e.Position)
                  )

              Expect.equal (says "9007199254740992") "ok 9007199254740992" "2^53 is int53-safe"
              Expect.equal (says "9007199254740994") "ok 9007199254740994" "2^53 + 2 is a layout"
              Expect.equal (says "-9007199254740994") "ok -9007199254740994" "and negated"
              Expect.equal (says "99999999999999980") "ok 99999999999999980" "the largest double below 1e17, laid out"
              Expect.equal (says "10000000000000000") "ok 10000000000000000" "1e16"

              let refusal (tok: string) =
                  sprintf
                      "err MalformedNumber @%d integer literal outside the int53 safe range (|n| > 2^53); it cannot round-trip without precision loss: %s"
                      tok.Length
                      tok

              Expect.equal (says "9007199254740993") (refusal "9007199254740993") "2^53 + 1 is refused"
              Expect.equal (says "18205257897171752") (refusal "18205257897171752") "an exact value, not the layout"
              Expect.equal (says "99999999999999984") (refusal "99999999999999984") "that double's exact value"
              Expect.equal (says "99999999999999999") (refusal "99999999999999999") "reads as 1e17, laid out 1E+17"
              Expect.equal (says "100000000000000000") (refusal "100000000000000000") "1e17 itself, spelled whole"
          }

          test "go-red: dropping the check is caught" {
              Expect.isNonEmpty (disagreements Reference.noCheck) "a reader with no check disagrees"
          }

          test "go-red: comparing the exact value's digits is caught" {
              Expect.isNonEmpty (disagreements Reference.exactDigits) "a reader comparing exact digits disagrees"
          } ]
