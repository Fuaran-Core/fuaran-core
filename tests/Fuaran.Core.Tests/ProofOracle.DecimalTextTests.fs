module Fuaran.Core.Tests.ProofOracle.DecimalTextTests

// The `Proofs.Oracle` differential for `proofs/DecimalText.fst`: the extracted model
// (`proofs/oracle/DecimalText.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

// Phase 279 — the extracted DECIMAL-TEXT model, bound the same way and for the same reason:
// production's `Fuaran.Core.DecimalText` module shares the model's name once `Fuaran.Core` is
// open.
module ModelDecimal = DecimalText

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

// ---------------------------------------------------------------------------

let private decToSyms (s: string) : ModelDecimal.sym list =
    [ for c in s ->
          if c = '-' then
              ModelDecimal.Minus
          elif c = '.' then
              ModelDecimal.Dot
          elif c >= '0' && c <= '9' then
              ModelDecimal.Digit(System.Numerics.BigInteger(int c - int '0'))
          else
              ModelDecimal.Other ]

let private decFromSyms (l: ModelDecimal.sym list) : string =
    l
    |> List.map (function
        | ModelDecimal.Minus -> "-"
        | ModelDecimal.Dot -> "."
        | ModelDecimal.Digit d -> string (int d)
        | ModelDecimal.Other -> "?")
    |> String.concat ""

let private modelDecCanonical (s: string) : string option =
    match ModelDecimal.try_canonical (decToSyms s) with
    | FStar_Pervasives_Native.Some c -> Some(decFromSyms c)
    | FStar_Pervasives_Native.None -> None

let private modelDecCompare (a: string) (b: string) : int option =
    match ModelDecimal.compare (decToSyms a) (decToSyms b) with
    | FStar_Pervasives_Native.Some r -> Some(int r)
    | FStar_Pervasives_Native.None -> None

let private modelDecAdd (a: string) (b: string) : string option =
    match ModelDecimal.add (decToSyms a) (decToSyms b) with
    | FStar_Pervasives_Native.Some c -> Some(decFromSyms c)
    | FStar_Pervasives_Native.None -> None

/// A decimal-text pool: texts the grammar accepts, shaped so that the clauses the theorems are
/// about are all reached — leading and trailing zeros to trim, a sign on zero to drop, bare and
/// fractional forms, a scale and a width that differ between the two operands of a pair, and
/// magnitudes past any host number type — and texts it refuses, one per refused form D72 K4
/// names (a leading `+`, a bare point either side, an exponent, white space, a separator, a
/// second point, an empty text, a lone sign, a non-ASCII digit).
let private decTexts (seed: int) (count: int) : string list =
    let mutable rng = ConfRng.ofSeed seed

    let draw n =
        let v, r = ConfRng.intBelow n rng
        rng <- r
        v

    let digits n =
        System.String(Array.init n (fun _ -> char (int '0' + draw 10)))

    let refusedForms =
        [ "+5"
          ".5"
          "5."
          "1e3"
          " 1"
          "1 "
          "1,5"
          "1.2.3"
          ""
          "-"
          "--1"
          "-."
          "0x1"
          "\u0967" ]

    [ for _ in 1..count ->
          let shape = draw 12

          if shape = 0 then
              refusedForms[draw (List.length refusedForms)]
          else
              let sign = if draw 3 = 0 then "-" else ""
              let lead = if draw 3 = 0 then "00" else ""
              let ipLen = if shape < 4 then draw 3 else 1 + draw 25
              let ip = if ipLen = 0 then "0" else digits ipLen

              let fp =
                  if shape % 2 = 0 then
                      ""
                  else
                      "." + digits (1 + draw 9) + (if draw 3 = 0 then "00" else "")

              sign + lead + ip + fp ]

let private decSign (n: int) : int =
    if n < 0 then -1
    elif n > 0 then 1
    else 0


[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 279: the exact decimal's text arithmetic, beside production ----

          testCase
              "the decimal-text oracle agrees with DecimalText.tryCanonical, compare and add over a generated pool of texts and pairs"
          <| fun _ ->
              let texts = decTexts 2790 400
              let mutable accepted = 0
              let mutable refused = 0
              let mutable trimmed = 0

              for s in texts do
                  let model = modelDecCanonical s
                  Expect.equal (DecimalText.tryCanonical s) model (sprintf "tryCanonical %A" s)

                  match model with
                  | Some c ->
                      accepted <- accepted + 1

                      if c <> s then
                          trimmed <- trimmed + 1
                  | None -> refused <- refused + 1

              Expect.isGreaterThan accepted 250 "texts the canonicaliser read"
              Expect.isGreaterThan trimmed 60 "texts it read AND rewrote (a zero trimmed, a sign on zero dropped)"
              Expect.isGreaterThan refused 20 "and texts it refused"

              // every pair: the order and the sum, each compared verbatim — the sign of the order,
              // the text of the sum, and `None` on exactly the pairs the model refuses
              // ... plus the pairs a random draw never reaches: two spellings of one magnitude
              // with opposite signs, so the difference arm cancels to zero and the sign-on-zero
              // clause of `add` is measured rather than hoped for
              let cancelling =
                  [ "12.50", "-12.5"
                    "-0.00100", "0.001"
                    "007", "-7"
                    "-123456789012345678901234.5", "123456789012345678901234.50"
                    "0", "-0.0" ]

              let pairs = List.zip (decTexts 2791 1500) (decTexts 2792 1500) @ cancelling
              let mutable crossSign = 0
              let mutable differentScale = 0
              let mutable cancelled = 0

              for a, b in pairs do
                  Expect.equal (DecimalText.compare a b) (modelDecCompare a b) (sprintf "compare %A %A" a b)
                  Expect.equal (DecimalText.add a b) (modelDecAdd a b) (sprintf "add %A %A" a b)

                  match DecimalText.tryCanonical a, DecimalText.tryCanonical b with
                  | Some ca, Some cb ->
                      if ca.StartsWith "-" <> cb.StartsWith "-" then
                          crossSign <- crossSign + 1

                      if ca.Contains "." <> cb.Contains "." then
                          differentScale <- differentScale + 1

                      if DecimalText.add a b = Some "0" then
                          cancelled <- cancelled + 1
                  | _ -> ()

              Expect.isGreaterThan crossSign 100 "pairs of opposite sign (the difference arm)"
              Expect.isGreaterThan differentScale 100 "pairs of different scale (the alignment)"
              Expect.isGreaterThan cancelled 0 "a pair that cancels to zero (the sign-on-zero arm)"

          testCase
              "a decimal-text canonicaliser that keeps the written scale, or an ORDINAL comparator over canonical text, DISAGREES with the model — the measurement can fail"
          <| fun _ ->
              // The deliberately wrong clause, one per theorem family. A canonicaliser that trims
              // the integer part and not the fraction is the K3 sentence with one clause missing;
              // an ordinal order over canonical texts is the order a string column would give.
              let keepsScale (s: string) : string option =
                  DecimalText.tryCanonical s
                  |> Option.map (fun c ->
                      let dot = s.IndexOf '.'

                      if dot < 0 || c.Contains "." then
                          c
                      else
                          c + "." + s.Substring(dot + 1))

              let ordinal (a: string) (b: string) : int option =
                  match DecimalText.tryCanonical a, DecimalText.tryCanonical b with
                  | Some ca, Some cb -> Some(decSign (System.String.CompareOrdinal(ca, cb)))
                  | _ -> None

              let texts = decTexts 2793 400
              let pairs = List.zip (decTexts 2794 600) (decTexts 2795 600)

              let scaleLost =
                  texts
                  |> List.filter (fun s -> keepsScale s <> modelDecCanonical s)
                  |> List.length

              let ordinalLost =
                  pairs
                  |> List.filter (fun (a, b) -> ordinal a b <> modelDecCompare a b)
                  |> List.length

              Expect.isGreaterThan scaleLost 0 "the scale-keeping canonicaliser lost against the model"
              Expect.isGreaterThan ordinalLost 0 "the ordinal comparator lost against the model"

              // and the shipped clauses do NOT lose on the same pools — the go-red is the clause,
              // not the pool
              Expect.isEmpty
                  (texts
                   |> List.filter (fun s -> DecimalText.tryCanonical s <> modelDecCanonical s))
                  "production's canonicaliser agrees on the pool the wrong one lost on"

              Expect.isEmpty
                  (pairs
                   |> List.filter (fun (a, b) -> DecimalText.compare a b <> modelDecCompare a b))
                  "production's comparator agrees on the pool the wrong one lost on"

          testCase "the theorems hold on the shipped DecimalText over the pool — idempotence, the order, the sum"
          <| fun _ ->
              // What the model proves, sampled on production: `canonical_idempotent`,
              // `compare_zero_iff_canonical`, `compare_antisymmetric`, `add_canonical`,
              // `add_commutative`, `add_zero_identity`. None of this is the differential —
              // it is the theorem's statement read at the shipped code, so a reader can see what
              // each one says without the model open.
              let texts =
                  decTexts 2796 300 |> List.choose DecimalText.tryCanonical |> List.distinct

              let pairs = List.zip (decTexts 2797 800) (decTexts 2798 800)

              for c in texts do
                  Expect.equal (DecimalText.tryCanonical c) (Some c) (sprintf "idempotent at %A" c)
                  Expect.equal (DecimalText.add c "0") (Some c) (sprintf "zero is the identity at %A" c)
                  Expect.equal (DecimalText.add c "-0.00") (Some c) (sprintf "and so is any zero, at %A" c)

              for a, b in pairs do
                  match DecimalText.compare a b, DecimalText.add a b with
                  | Some r, Some sum ->
                      Expect.equal (DecimalText.compare b a) (Some(-r)) (sprintf "antisymmetric at %A %A" a b)

                      Expect.equal
                          (r = 0)
                          (DecimalText.tryCanonical a = DecimalText.tryCanonical b)
                          (sprintf "0 exactly where the canonical forms agree, at %A %A" a b)

                      Expect.isTrue (DecimalText.isCanonical sum) (sprintf "the sum is canonical at %A %A" a b)
                      Expect.equal (DecimalText.add b a) (Some sum) (sprintf "commutative at %A %A" a b)
                  | None, None -> ()
                  | _ -> failtestf "compare and add refuse the same pairs, at %A %A" a b ]
