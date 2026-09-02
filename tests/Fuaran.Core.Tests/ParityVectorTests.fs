/// The committed cross-pipeline VECTOR TABLE (Phase 118) — the .NET half of the value claim
/// `tests/fable-smoke/parity.ps1` completes.
///
/// TWO CLAIMS, AND NEITHER IMPLIES THE OTHER. This file pins what the .NET pipeline computes, so a
/// change that moves a digest, a chain hash or a float layout is a failing test attached to the
/// line that names it. It says NOTHING about the transpiled pipeline: a defect that moves BOTH
/// sides identically passes the parity diff and is caught only here, and a defect that moves only
/// the Fable side passes here and is caught only there. `Hash.fnv1a` sat divergent behind a fully
/// green suite until `0.6.0` for exactly that reason.
///
/// The table itself lives in `tests/fable-smoke/ParityVectors.fs` and is LINKED into this project,
/// not copied: the two pipelines have to be measuring the same source, or the diff certifies a
/// coincidence rather than a contract.
module Fuaran.Core.Tests.ParityVectorTests

open Expecto

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
      "chain/hash-1", "90e9e1a4" ]

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
      "chain/" ]

[<Tests>]
let tests =
    testList
        "ParityVectors"
        [ testCase "the table is the committed set, in order"
          <| fun _ ->
              Expect.equal
                  (FableSmoke.ParityVectors.vectors |> List.map fst)
                  (expected |> List.map fst)
                  "the vector labels, in table order — a new vector is added to BOTH lists"

          testCase "every vector computes its committed bytes"
          <| fun _ ->
              for (label, actual), (_, want) in List.zip FableSmoke.ParityVectors.vectors expected do
                  Expect.equal actual want (sprintf "vector %s" label)

          testCase "labels are unique"
          <| fun _ ->
              let labels = FableSmoke.ParityVectors.vectors |> List.map fst

              Expect.equal
                  (labels |> List.distinct |> List.length)
                  (List.length labels)
                  "a duplicated label makes one of the two vectors unreadable in a divergence report"

          testCase "every declared family is present"
          <| fun _ ->
              let labels = FableSmoke.ParityVectors.vectors |> List.map fst

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
              for label, value in FableSmoke.ParityVectors.vectors do
                  for ch in label + value do
                      Expect.isTrue
                          (int ch >= 0x20 && int ch <= 0x7E)
                          (sprintf "vector %s emits only printable ASCII (found U+%04X)" label (int ch))

          // The emitted line is `VEC <label> <value>`, split on the FIRST space, so a label
          // carrying one would silently truncate the label and corrupt the value.
          testCase "no label contains a space"
          <| fun _ ->
              for label, _ in FableSmoke.ParityVectors.vectors do
                  Expect.isFalse (label.Contains " ") (sprintf "label %s is one token" label) ]
