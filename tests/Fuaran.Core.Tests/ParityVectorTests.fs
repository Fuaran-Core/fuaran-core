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
      "sha256/empty", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
      "sha256/abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
      "sha256/two-block", "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"
      "sha256/unicode", "2c65957a04b33db60d702542c13fa9fda67c69e1d1e54c727270eb2ff685d871"
      "sha256/of-bytes", "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"
      "utf8Bytes/unicode", "636166c3a92fe697a5e69cace8aa9e2ff09f9880"
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
      "witness/render",
      @"{""kind"":""witness"",""id"":""ref-0"",""count"":3,""ratio"":0.1,""flag"":true,""tags"":[""a"",""b""],""nested"":{""z"":1,""a"":2.5,""esc"":""quote:\"" back:\\ tab:\t nl:\n sep:\u0001""}}"
      "witness/canon",
      @"{""count"":3,""flag"":true,""id"":""ref-0"",""kind"":""witness"",""nested"":{""a"":2.5,""esc"":""quote:\"" back:\\ tab:\u0009 nl:\u000a sep:\u0001"",""z"":1},""ratio"":0.1,""tags"":[""a"",""b""]}"
      "witness/render-parse-render",
      @"{""kind"":""witness"",""id"":""ref-0"",""count"":3,""ratio"":0.1,""flag"":true,""tags"":[""a"",""b""],""nested"":{""z"":1,""a"":2.5,""esc"":""quote:\"" back:\\ tab:\t nl:\n sep:\u0001""}}"
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
      "confRng/seed-1488", "1779094209-974108648-1386259377-469941146-625613426-646666080-1082870579-340445045" ]

/// The families the table must keep covering. A vector set is only as good as what it reaches, and
/// nothing about a green comparison says the list was not quietly emptied of the hard cases — the
/// same argument `SampleAdequacy` makes for a generated sample, applied to a committed one.
let private families =
    [ "fnv1a/"
      "sha256/"
      "utf8Bytes/"
      "canonicalFloat/"
      "jsonRender/"
      "witness/"
      "chain/"
      "confRng/" ]

/// The hash SWEEP (Phase 217 — the retired `tests/hash-parity-probe` corpus, absorbed): 124 rows,
/// each four digests wide. Pinned as a COUNT and a DIGEST over the rows rather than row by row — the
/// named table above carries the hand-checkable known answers, and 496 hex strings committed here
/// would be a table nobody reads. The digest is SHA-256 over the rows' `VEC` lines joined by `\n`,
/// so any row moving reddens it; the row-level tests below say which one did.
let private sweepRows = 124

let private sweepDigest =
    "86213e76c48e7e0eea259781167489b400961a086e1fbafcdef819890d95eb40"

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
                  "no two vectors share a label" ]
