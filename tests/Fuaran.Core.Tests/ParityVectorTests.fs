/// The committed cross-pipeline VECTOR TABLE (Phase 118) — the .NET half of the value claim.
///
/// TWO CLAIMS, AND NEITHER IMPLIES THE OTHER. This file pins what the .NET pipeline computes, so a
/// change that moves a digest, a chain hash or a float layout is a failing test attached to the
/// line that names it. It says NOTHING about the transpiled pipeline: a defect that moves BOTH
/// sides identically passes the parity diff and is caught only here, and a defect that moves only
/// the Fable side passes here and is caught only there. `Hash.fnv1a` sat divergent behind a fully
/// green suite until `0.6.0` for exactly that reason.
///
/// The table is PUBLIC since Phase 217 — `Fuaran.Core.ParityVectors`, in the conformance kit — so
/// the transpiled half runs where the Fable compiler is: a consumer that owns a Fable toolchain
/// compiles the module from the package's `fable/` sources and diffs `ParityVectors.lines ()`
/// between the two pipelines. STABILITY.md "Fable cleanliness" names where, and the rule that ties
/// that run to every version cut. This file tests the module the consumer runs, not a copy of it.
module Fuaran.Core.Tests.ParityVectorTests

open Expecto
open Fuaran.Core

/// The expected bytes, in table order. Hand-checkable against published values where one exists:
/// `sha256/empty` and `sha256/abc` are the FIPS 180-4 known answers, `sha256/two-block` is the
/// 56-byte two-block vector, `fnv1a/empty` is the FNV offset basis, and `fnv1a/a` is the value
/// D16 records as having been `e40c2930` under Fable before the split-half multiply landed.
let private expected: (string * string) list =
    [ "fnv1a/empty", "811c9dc5"
      "fnv1a/a", "e40c292c"
      "fnv1a/foldSep-join", "32f61fef"
      "fnv1a/unicode", "a721136a"
      "fnv1a/a80", "5143e6d5"
      // Phase 225: the pre-image as UTF-8 hex. `symbols` is hand-checkable: `a` DLE SOH `b` SOH,
      // DLE DLE SOH, SOH, `=#` SOH — every carried symbol escaped, every field terminated.
      "canonicalFields/plain", "61016201"
      "canonicalFields/symbols", "6110016201101001013d2301"
      "sha256/empty", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
      "sha256/abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
      "sha256/two-block", "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"
      "sha256/unicode", "2c65957a04b33db60d702542c13fa9fda67c69e1d1e54c727270eb2ff685d871"
      "sha256/of-bytes", "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"
      "utf8Bytes/unicode", "636166c3a92fe697a5e69cace8aa9e2ff09f9880"
      // Phase 290: the platform's replacement bytes (`EF BF BD`) per ill-formed unit, taken from
      // `System.Text.UTF8Encoding` — `HashTests` asserts the same rows against it directly.
      "utf8Bytes/ill-formed-lone-high", "efbfbd"
      "utf8Bytes/ill-formed-lone-low", "efbfbd"
      "utf8Bytes/ill-formed-high-at-end", "61efbfbd"
      "utf8Bytes/ill-formed-high-then-nonlow", "efbfbdefbfbd"
      "utf8Bytes/ill-formed-high-then-ascii", "efbfbd7a"
      "utf8Bytes/ill-formed-low-then-high", "efbfbdefbfbd"
      "utf8Bytes/ill-formed-beside-a-pair", "f09f9880efbfbd"
      "sha256/ill-formed-lone-high", "83d544ccc223c057d2bf80d3f2a32982c32c3c0db8e2674820da5064783fb097"
      "canonicalFloat/zero", "0"
      "canonicalFloat/neg-zero", "0"
      "canonicalFloat/one-and-a-half", "1.5"
      "canonicalFloat/tenth", "0.1"
      "canonicalFloat/neg-third", "-0.3333333333333333"
      "canonicalFloat/e16", "10000000000000000"
      "canonicalFloat/e17", "1E+17"
      "canonicalFloat/e21", "1E+21"
      "canonicalFloat/e-7", "1E-07"
      "canonicalFloat/max", "1.7976931348623157E+308"
      "canonicalFloat/denormal-min", "5E-324"
      "canonicalFloat/nan", @"""NaN"""
      "canonicalFloat/inf", @"""Infinity"""
      "canonicalFloat/neg-inf", @"""-Infinity"""
      "jsonRender/finite-floats", "[0,-0,1.5,1E+21,1E-07,1.7976931348623157E+308]"
      "jsonRender/non-finite-floats", "[NaN,Infinity,-Infinity]"
      // Phase 287: `Json.render` spells every control character as `\u00xx` — the bytes the
      // `witness/canon` vector below already carried for the same document.
      "witness/render",
      @"{""kind"":""witness"",""id"":""ref-0"",""count"":3,""ratio"":0.1,""flag"":true,""tags"":[""a"",""b""],""nested"":{""z"":1,""a"":2.5,""esc"":""quote:\"" back:\\ tab:\u0009 nl:\u000a sep:\u0001""}}"
      "witness/canon",
      @"{""count"":3,""flag"":true,""id"":""ref-0"",""kind"":""witness"",""nested"":{""a"":2.5,""esc"":""quote:\"" back:\\ tab:\u0009 nl:\u000a sep:\u0001"",""z"":1},""ratio"":0.1,""tags"":[""a"",""b""]}"
      "witness/render-parse-render",
      @"{""kind"":""witness"",""id"":""ref-0"",""count"":3,""ratio"":0.1,""flag"":true,""tags"":[""a"",""b""],""nested"":{""z"":1,""a"":2.5,""esc"":""quote:\"" back:\\ tab:\u0009 nl:\u000a sep:\u0001""}}"
      "witness/canon-parse-canon",
      @"{""count"":3,""flag"":true,""id"":""ref-0"",""kind"":""witness"",""nested"":{""a"":2.5,""esc"":""quote:\"" back:\\ tab:\u0009 nl:\u000a sep:\u0001"",""z"":1},""ratio"":0.1,""tags"":[""a"",""b""]}"
      "witness/unicode-canon-sha256", "5b3f9741d22fae4f5d9c22e5c8eacdd263905fda2fa17574f23da9ad8c4afb33"
      "defaultHash/genesis", "31654cc6"
      "chain/hash-0", "8f05218a"
      "chain/prev-1", "8f05218a"
      "chain/hash-1", "90e9e1a4"
      // `ConfRng` is xorshift32 from 0.20.0 — these are NOT the values the LCG before it drew, and
      // the difference is the release. What the vectors pin is that the two pipelines agree; that
      // they agree on THIS stream is what makes a recorded seed reproducible at all.
      "confRng/seed-0", "12702810-1931064975-2093279515-1561498856-2122184415-1415771932-1521297884-222582007"
      "confRng/seed-1", "166402301-879050087-1794327795-1338740069-1921087622-1872842638-205863014-1114920338"
      "confRng/seed-neg-1", "2015858743-1309524423-815153517-1875012400-1982543394-218150117-1746742363-2136549504"
      "confRng/seed-1488", "1779094209-974108648-1386259377-469941146-625613426-646666080-1082870579-340445045"
      // Phase 299: Min is -1 (NaN is not below it), Max is NaN (NaN sorts last), Median of the five is
      // the third of -1, -0, 3, NaN, NaN, and the four distinct values are 3, NaN, -1 and 0.
      "aggregate/nan-order", "-1/\"NaN\"/3/4"
      // Phase 306. `fnv1a/astral-code-units` is FNV-1a over the two UTF-16 units D83D DE00; the
      // guarded rows name the unpaired unit by index and value; the two depth-10,000 renders are one
      // text, so one digest; `mean-at-the-edge` is the scaled recurrence's answer, one unit in the
      // last place below 1.7e308 / 3, and the other three edge aggregates are exact.
      "fnv1a/astral-code-units", "cb31c4b8"
      "tryUtf8Bytes/well-formed", "ok:636166c3a92fe697a5e69cace8aa9e2ff09f9880"
      "tryUtf8Bytes/lone-high", "refused@0:d800"
      "tryUtf8Bytes/high-then-high", "refused@0:d801"
      "tryUtf8Bytes/stray-low-after-a-pair", "refused@2:de00"
      "canonTryRender/well-formed", "ok:d5ccf1a3ef9786b1399f86349cc633e20f0f68d670d46ce7ecf6d3b11a540666"
      "canonTryRender/ill-formed-value", "refused"
      "canonTryRender/ill-formed-key", "refused"
      "jsonTryRender/ill-formed-value", "refused"
      "render/depth-10000-json", "bf87f541c6ec3cdd0f4de28a7264b2fe92e14f8535c0d951b5d825e1e3459d2a"
      "render/depth-10000-canon", "ok:bf87f541c6ec3cdd0f4de28a7264b2fe92e14f8535c0d951b5d825e1e3459d2a"
      "aggregate/median-at-the-edge", "1E+308"
      "aggregate/mean-at-the-edge", "5.666666666666666E+307"
      "aggregate/stddev-at-the-edge", "1E+200"
      "aggregate/sum-past-the-edge", "<overflow>"
      "aggregate/stddev-population", "2"
      "profile/canonical", "core@1.0"
      "profile/leading-zero", "refused"
      "profile/plus-sign", "refused"
      "profile/trailing-nul", "refused"
      "profile/int32-max", "core@2147483647.2147483647"
      "profile/past-int32", "refused"
      // Phase 315 — the light set. `sha256Hash/*` is also held to the platform digest in
      // `LightSetTests`, and `fnv1a32/a` is `fnv1a/a`'s `e40c292c` in decimal.
      "sha256Hash/genesis", "88ab8d7b4727fd34f90a698cd45e27a227812ca72e4d5daf0da808a11c07fdb2"
      "sha256Hash/two-block", "890f16829cee657dc9291880730df50b465ac92d2332a595fb49cffa198319f7"
      "sha256Hash/agrees-with-sha256Hex", "agrees:13"
      "fnv1a32/a", "3826002220"
      "fnv1a32/unicode", "2803962730"
      "fnv1a32/agrees-with-fnv1a", "agrees:13"
      "floatLayout/finite-neg-zero", "-0"
      "floatLayout/finite-tenth", "0.1"
      "floatLayout/finite-e21", "1E+21"
      "floatLayout/finite-e-7", "1E-07"
      "floatLayout/finite-neg-third", "-0.3333333333333333"
      "floatLayout/round-trip-non-finite", "NaN,Infinity,-Infinity"
      "cellToken/floats", "f:NaN f:Inf f:-Inf f:0 f:0 f:0.1 f:1E+21"
      "cellToken/scalars", "i:-3 b:1 s:s d:2026-10-01 n:"
      "cellToken/decimal-canonical", "m:1.5"
      "cellCompare/float-order", "f:-Inf f:0 i:0 f:1 f:Inf f:NaN"
      // Phase 276 — the exact decimal (D72). Hand-checkable: the aggregate column is 0.1, 0.2, null,
      // -0.3, 1.50, 1.5 and the int 2, so `Sum` is exactly 5, `Min` the decimal -0.3, `Max` the int
      // cell 2 as it stands, `Mean` 5/6, `Median` (0.2 + 1.5) / 2, and the two spellings of 1.5 one
      // distinct value of five. `one-float-*` are pairs one double cannot tell apart and the order
      // can. `sum-past-float` is SHA-256 over the exact sum's token, `m:1` + 399 zeros + `.5`.
      "decimal/canonical/refused-empty", "refused"
      "decimal/canonical/refused-null", "refused"
      "decimal/canonical/refused-lone-minus", "refused"
      "decimal/canonical/refused-leading-plus", "refused"
      "decimal/canonical/refused-double-minus", "refused"
      "decimal/canonical/refused-bare-point-leading", "refused"
      "decimal/canonical/refused-bare-point-trailing", "refused"
      "decimal/canonical/refused-minus-bare-point", "refused"
      "decimal/canonical/refused-point-alone", "refused"
      "decimal/canonical/refused-two-points", "refused"
      "decimal/canonical/refused-exponent", "refused"
      "decimal/canonical/refused-exponent-upper", "refused"
      "decimal/canonical/refused-separator-comma", "refused"
      "decimal/canonical/refused-separator-underscore", "refused"
      "decimal/canonical/refused-decimal-comma", "refused"
      "decimal/canonical/refused-leading-space", "refused"
      "decimal/canonical/refused-trailing-space", "refused"
      "decimal/canonical/refused-tab", "refused"
      "decimal/canonical/refused-hex", "refused"
      "decimal/canonical/refused-nan", "refused"
      "decimal/canonical/refused-infinity", "refused"
      "decimal/canonical/refused-arabic-indic-digit", "refused"
      "decimal/canonical/refused-fullwidth-digit", "refused"
      "decimal/canonical/already-canonical", "12.5"
      "decimal/canonical/zero", "0"
      "decimal/canonical/leading-zeros", "7"
      "decimal/canonical/trailing-zeros", "12.5"
      "decimal/canonical/zero-fraction", "3"
      "decimal/canonical/leading-zero-fraction", "0.5"
      "decimal/canonical/negative-zero", "0"
      "decimal/canonical/negative-zero-fraction", "0"
      "decimal/canonical/zeros-both-sides", "0"
      "decimal/canonical/negative-both-sides", "-12.34"
      "decimal/canonical/thirty-one-places", "0.0000000000000000000000000000001"
      "decimal/canonical/forty-digits", "1234567890123456789012345678901234567890.5"
      "decimal/compare/negative-below-positive", "-1"
      "decimal/compare/positive-above-negative", "1"
      "decimal/compare/signed-zeros-equal", "0"
      "decimal/compare/negatives-reversed", "1"
      "decimal/compare/integer-width", "1"
      "decimal/compare/fraction-place", "1"
      "decimal/compare/negative-fraction-place", "-1"
      "decimal/compare/whole-against-fraction", "1"
      "decimal/compare/equal-spellings", "0"
      "decimal/compare/equal-spellings-negative", "0"
      "decimal/compare/one-float-tenth", "-1"
      "decimal/compare/one-float-past-2-53", "1"
      "decimal/compare/refused-left", "refused"
      "decimal/compare/refused-right", "refused"
      "decimal/add/carry-through-point", "1"
      "decimal/add/carry-into-tens", "10"
      "decimal/add/widening-carry", "1000"
      "decimal/add/widening-carry-fraction", "100"
      "decimal/add/narrowing-borrow", "999.999"
      "decimal/add/borrow-to-fraction", "0.01"
      "decimal/add/cancel-to-unsigned-zero", "0"
      "decimal/add/cancel-negative-first", "0"
      "decimal/add/mixed-negative-larger-first", "-2"
      "decimal/add/mixed-negative-larger-second", "-2"
      "decimal/add/mixed-positive-larger-first", "2"
      "decimal/add/mixed-positive-larger-second", "2"
      "decimal/add/both-negative", "-4.25"
      "decimal/add/zero-identity", "12.5"
      "decimal/add/scale-past-host-decimal", "1.0000000000000000000000000000001"
      "decimal/add/magnitude-past-host-decimal", "100000000000000000000000000000000000000"
      "decimal/add/refused", "refused"
      "decimal/toFloat/tenth", "0.1"
      "decimal/toFloat/past-2-53", "9007199254740992"
      "decimal/toFloat/negative", "-12.5"
      "decimal/toFloat/past-float-range", "refused"
      "decimal/toFloat/refused", "refused"
      "decimalCodec/encode-canonical",
      @"ok:{""columns"":{""c"":{""validity"":[true,false,true,true],""values"":[""12.5"",""0"",""-0.001"",""7""]}},""schema"":[{""name"":""c"",""type"":""decimal""}]}"
      "decimalCodec/encode-refuses-non-canonical", "refused:MalformedShape"
      "decimalCodec/decode-canonicalises",
      @"ok:{""columns"":{""c"":{""validity"":[true,true,true],""values"":[""12.5"",""0"",""3""]}},""schema"":[{""name"":""c"",""type"":""decimal""}]}"
      "decimalCodec/decode-integer-token",
      @"ok:{""columns"":{""c"":{""validity"":[true,true,true],""values"":[""42"",""-7"",""0""]}},""schema"":[{""name"":""c"",""type"":""decimal""}]}"
      "decimalCodec/decode-integer-token-past-int32",
      @"ok:{""columns"":{""c"":{""validity"":[true,true],""values"":[""3000000000"",""-9007199254740992""]}},""schema"":[{""name"":""c"",""type"":""decimal""}]}"
      "decimalCodec/decode-whole-exponent-token",
      @"ok:{""columns"":{""c"":{""validity"":[true],""values"":[""3000000000""]}},""schema"":[{""name"":""c"",""type"":""decimal""}]}"
      "decimalCodec/decode-refuses-fractional-token", "refused:TypeMismatch"
      "decimalCodec/decode-refuses-integer-token-past-2-53", "refused:NotJson"
      "decimalCodec/decode-refuses-whole-float-past-2-53", "refused:TypeMismatch"
      "decimalCodec/decode-refuses-exponent-text", "refused:MalformedShape"
      "decimalCodec/decode-refuses-plus-text", "refused:MalformedShape"
      "decimalCodec/decode-refuses-bool", "refused:TypeMismatch"
      "decimalAggregate/sum", "m:5"
      "decimalAggregate/min", "m:-0.3"
      "decimalAggregate/max", "i:2"
      "decimalAggregate/mean", "f:0.8333333333333334"
      "decimalAggregate/median", "f:0.85"
      "decimalAggregate/stddev", "f:0.8634555897992412"
      "decimalAggregate/count-distinct", "i:5"
      "decimalAggregate/sum-tenths-exact", "m:1"
      "decimalAggregate/sum-past-float", "f71d1eb372ac8e9eb340d9d8f987246b49e50a95e06e2023170affd98d4aa403"
      "decimalAggregate/mean-past-float", "<overflow>"
      "decimalAggregate/sum-not-decimal", "<outside-type>" ]

/// The families the table must keep covering. A vector set is only as good as what it reaches, and
/// nothing about a green comparison says the list was not quietly emptied of the hard cases — the
/// same argument `SampleAdequacy` makes for a generated sample, applied to a committed one.
let private families =
    [ "fnv1a/"
      "canonicalFields/"
      "sha256/"
      "utf8Bytes/"
      "canonicalFloat/"
      "jsonRender/"
      "witness/"
      "chain/"
      "confRng/"
      "aggregate/"
      "tryUtf8Bytes/"
      "canonTryRender/"
      "render/"
      "profile/"
      "sha256Hash/"
      "fnv1a32/"
      "floatLayout/"
      "cellToken/"
      "cellCompare/"
      "decimal/"
      "decimalCodec/"
      "decimalAggregate/" ]

/// The hash SWEEP (Phase 217 — the retired `tests/hash-parity-probe` corpus, absorbed): 124 rows,
/// each four digests wide. Pinned as a COUNT and a DIGEST over the rows rather than row by row — the
/// named table above carries the hand-checkable known answers, and 496 hex strings committed here
/// would be a table nobody reads. The digest is SHA-256 over the rows' `VEC` lines joined by `\n`,
/// so any row moving reddens it; the row-level tests below say which one did.
let private sweepRows = 124

// Moved by Phase 299: every row's fourth value is `Schema.fingerprint`, whose pre-image is the
// canonical field encoding now rather than a bare U+0001 join.
let private sweepDigest =
    "4f8ceff06baed41adbfbba2bc5644c5665abf3ea138b1978c5f5e4414388782f"

[<Tests>]
let tests =
    testList
        "ParityVectors"
        [ testCase "the table is the committed set, in order"
          <| fun _ ->
              Expect.equal
                  (ParityVectors.vectors |> List.map fst)
                  (expected |> List.map fst)
                  "the vector labels, in table order — a new vector is added to BOTH lists"

          testCase "every vector computes its committed bytes"
          <| fun _ ->
              for (label, actual), (_, want) in List.zip ParityVectors.vectors expected do
                  Expect.equal actual want (sprintf "vector %s" label)

          testCase "labels are unique"
          <| fun _ ->
              let labels = ParityVectors.vectors |> List.map fst

              Expect.equal
                  (labels |> List.distinct |> List.length)
                  (List.length labels)
                  "a duplicated label makes one of the two vectors unreadable in a divergence report"

          testCase "every declared family is present"
          <| fun _ ->
              let labels = ParityVectors.vectors |> List.map fst

              for family in families do
                  Expect.isTrue
                      (labels |> List.exists (fun l -> l.StartsWith family))
                      (sprintf "the table still carries a %s vector" family)

          // The runner compares the two pipelines' STDOUT line by line. .NET and node do not agree
          // about how to write a lone surrogate or a control byte to a terminal, so a table whose
          // emitted bytes left ASCII would report a console-encoding difference as a value
          // divergence — a worse failure than none, because it is unfalsifiable from the report.
          // Non-ASCII INPUTS are fine and present; what must stay ASCII is what is PRINTED.
          testCase "every emitted label and value is printable ASCII"
          <| fun _ ->
              for label, value in ParityVectors.vectors @ ParityVectors.hashSweep do
                  for ch in label + value do
                      Expect.isTrue
                          (int ch >= 0x20 && int ch <= 0x7E)
                          (sprintf "vector %s emits only printable ASCII (found U+%04X)" label (int ch))

          // The emitted line is `VEC <label> <value>`, split on the FIRST space, so a label
          // carrying one would silently truncate the label and corrupt the value.
          testCase "no label contains a space"
          <| fun _ ->
              for label, _ in ParityVectors.vectors @ ParityVectors.hashSweep do
                  Expect.isFalse (label.Contains " ") (sprintf "label %s is one token" label)

          testCase "the hash sweep has its committed row count and digest"
          <| fun _ ->
              Expect.equal (List.length ParityVectors.hashSweep) sweepRows "the sweep's row count"

              let joined =
                  ParityVectors.hashSweep
                  |> List.map (fun (k, v) -> sprintf "VEC %s %s" k v)
                  |> String.concat "\n"

              Expect.equal
                  (Hash.sha256Hex joined)
                  sweepDigest
                  "the sweep's committed digest — a moved row is a moved .NET value"

          testCase "every sweep row carries four digests of the right widths"
          <| fun _ ->
              // A row that lost a column would still compare equal on both pipelines — the width
              // check is what says each of the four implementations is actually in the row.
              for label, value in ParityVectors.hashSweep do
                  let parts = value.Split '/'
                  Expect.equal parts.Length 4 (sprintf "%s has four digests" label)
                  Expect.equal parts[0].Length 8 (sprintf "%s: fnv1a is 32-bit hex" label)
                  Expect.equal parts[1].Length 64 (sprintf "%s: sha256 is 256-bit hex" label)

          testCase "lines () is the named table then the sweep, one VEC line each, in order"
          <| fun _ ->
              let lines = ParityVectors.lines ()

              Expect.equal
                  lines
                  (ParityVectors.vectors @ ParityVectors.hashSweep
                   |> List.map (fun (k, v) -> "VEC " + k + " " + v))
                  "the runner's comparison unit is exactly the two tables, formatted"

              Expect.equal
                  (lines |> List.distinct |> List.length)
                  (List.length (ParityVectors.vectors @ ParityVectors.hashSweep))
                  "no two vectors share a label"

          // Phase 276 — the decimal rows' committed answers held to an INDEPENDENT oracle wherever one
          // exists: `System.Decimal` holds every operand below (28 places, 96 bits), so its sum and its
          // order are what the string arithmetic must agree with. The rows past its range are the ones
          // the type exists for, and they are hand-checked in the table instead.
          testCase "the decimal add and compare rows agree with System.Decimal wherever it holds them"
          <| fun _ ->
              let inv = System.Globalization.CultureInfo.InvariantCulture
              let dec (s: string) = System.Decimal.Parse(s, inv)

              let pairs =
                  [ "0.99", "0.01"
                    "9.95", "0.05"
                    "999", "1"
                    "99.9", "0.1"
                    "1000", "-0.001"
                    "100", "-99.99"
                    "1.5", "-1.50"
                    "-0.001", "0.001"
                    "-5", "3"
                    "3", "-5"
                    "5", "-3"
                    "-3", "5"
                    "-1.5", "-2.75"
                    "-0", "12.50"
                    "-1", "1"
                    "-2", "-10"
                    "10", "9"
                    "0.1", "0.09"
                    "-0.1", "-0.09"
                    "100", "99.999"
                    "1.50", "001.5"
                    "-7.000", "-7" ]

              for a, b in pairs do
                  let viaDecimal = (dec a + dec b).ToString(inv) |> DecimalText.tryCanonical
                  Expect.equal (DecimalText.add a b) viaDecimal (sprintf "%s + %s" a b)

                  Expect.equal
                      (DecimalText.compare a b)
                      (Some(sign (System.Decimal.Compare(dec a, dec b))))
                      (sprintf "compare %s %s" a b)

          testCase "the past-float sum row is the digest of the exact sum's token"
          <| fun _ ->
              let row =
                  ParityVectors.vectors
                  |> List.find (fun (k, _) -> k = "decimalAggregate/sum-past-float")

              Expect.equal (snd row) (Hash.sha256Hex ("m:1" + String.replicate 399 "0" + ".5")) "1e399 + 0.5, exactly" ]
