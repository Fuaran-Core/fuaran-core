module Fuaran.Core.Tests.ProofOracle.Utf8Tests

// The `Proofs.Oracle` cases paired to `proofs/Utf8.fst`, a proof-only model with no extracted
// oracle: `Hash.utf8Bytes` is measured against the platform's encoder instead.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

// ---- the UTF-8 encoder (Phase 306): `Hash.utf8Bytes` beside the PLATFORM's encoder ----
//
// The first INDEPENDENT-oracle differential in this family. Every other case here runs an
// extracted model beside production; `proofs/Utf8.fst` is checked and not extracted (its units
// are integers, which do not survive the extraction), so what stands beside `Hash.utf8Bytes` is
// `System.Text.Encoding.UTF8` — an implementation that shares no line with it and no author. The
// model's theorem is about the encoder the model describes; this is what says the shipped encoder
// computes those bytes.

/// The encoder tally: the strings compared, the ill-formed among them, and the well-formed strings
/// a go-red encoder lost on.
let private utf8Tally = Tally.empty [ "Strings"; "IllFormed"; "WellFormedLost" ]

let private describe (x: string) =
    x |> Seq.map (fun c -> (int c).ToString "X4") |> String.concat " "

/// The unguarded encoder against the platform's replacing encoder, and the guarded one against the
/// platform's strict encoder, on one string, into the tally.
let private utf8Probe (s: string) (t: Tally) : Tally =
    let guardedAgrees =
        match Hash.tryUtf8Bytes s, strictlyEncodable s with
        | Result.Ok bytes, true when bytes = strictUtf8.GetBytes s -> true
        | Result.Error bad, false when bad.Index < s.Length && int s[bad.Index] = bad.Unit -> true
        | _ -> false

    t
    |> Tally.bump "Strings"
    |> Tally.bumpIf (not (strictlyEncodable s)) "IllFormed"
    |> Differential.check (Hash.utf8Bytes s = System.Text.Encoding.UTF8.GetBytes s) (fun () ->
        sprintf "utf8Bytes [%s] is not the platform's bytes" (describe s))
    |> Differential.check guardedAgrees (fun () ->
        sprintf
            "tryUtf8Bytes [%s] = %A where the strict encoder %s"
            (describe s)
            (Hash.tryUtf8Bytes s)
            (if strictlyEncodable s then "encodes" else "refuses"))

/// The encoder as it stood before Phase 290 — the go-red instrument: a high surrogate consumes the
/// NEXT unit as its low half without checking that it is one.
let private utf8ReadingAnUncheckedLowHalf (s: string) : byte[] =
    let out = ResizeArray<byte>()
    let mutable i = 0

    while i < s.Length do
        let c = int s[i]

        if c < 0x80 then
            out.Add(byte c)
        elif c < 0x800 then
            out.Add(byte (0xC0 ||| (c >>> 6)))
            out.Add(byte (0x80 ||| (c &&& 0x3F)))
        elif c >= 0xD800 && c <= 0xDBFF && i + 1 < s.Length then
            let lo = int s[i + 1]
            let cp = 0x10000 + ((c - 0xD800) <<< 10) + (lo - 0xDC00)
            out.Add(byte (0xF0 ||| (cp >>> 18)))
            out.Add(byte (0x80 ||| ((cp >>> 12) &&& 0x3F)))
            out.Add(byte (0x80 ||| ((cp >>> 6) &&& 0x3F)))
            out.Add(byte (0x80 ||| (cp &&& 0x3F)))
            i <- i + 1
        else
            out.Add(byte (0xE0 ||| (c >>> 12)))
            out.Add(byte (0x80 ||| ((c >>> 6) &&& 0x3F)))
            out.Add(byte (0x80 ||| (c &&& 0x3F)))

        i <- i + 1

    out.ToArray()

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- the UTF-8 encoder against the platform's (Phase 306) ----

          testCase
              "Hash.utf8Bytes is the platform encoder's bytes over every code unit and every surrogate boundary pair"
          <| fun _ ->
              // Every code unit, alone.
              let units = [ for u in 0..0xFFFF -> units16 [ u ] ]

              // Every high surrogate against the units that bound the low range and a non-surrogate
              // each side of it; and every low surrogate after a high one, an ordinary unit and
              // nothing — the pairs, the near-pairs and the reversed pairs.
              let pairs =
                  [ for hi in 0xD800..0xDBFF do
                        for next in [ 0xDBFF; 0xDC00; 0xDFFF; 0xE000; 0x41 ] -> units16 [ hi; next ] ]
                  @ [ for lo in 0xDC00..0xDFFF do
                          for before in [ 0xD800; 0xDBFF; 0x41 ] -> units16 [ before; lo ] ]

              // And a seeded sample of longer strings over the boundary units.
              let boundary =
                  [| 0x00
                     0x41
                     0x7F
                     0x80
                     0x7FF
                     0x800
                     0xD7FF
                     0xD800
                     0xDBFF
                     0xDC00
                     0xDFFF
                     0xE000
                     0xFFFD
                     0xFFFF |]

              // the LCG the sample has always drawn from, threaded through the harness's loop
              let sample (_: int) (st0: uint32) (t: Tally) =
                  let mutable st = st0

                  let pick (n: int) =
                      st <- (st * 1664525u) + 1013904223u
                      int (st >>> 1) % n

                  let s = units16 [ for _ in 0 .. pick 6 -> boundary[pick boundary.Length] ]
                  st, utf8Probe s t

              let t =
                  utf8Tally
                  |> Differential.over units utf8Probe
                  |> Differential.over pairs utf8Probe
                  |> Differential.seeded 20000 3062u sample

              Differential.expectAgrees "the encoder against the platform" t

              let strings = Tally.count "Strings" t
              let illFormed = Tally.count "IllFormed" t
              Expect.isGreaterThan strings 90000 (sprintf "strings compared (%d)" strings)

              Expect.isGreaterThan
                  illFormed
                  10000
                  (sprintf "… of them ill-formed, where the guard must refuse (%d)" illFormed)

          testCase "an encoder that reads an unchecked low half loses to the platform — the comparison can fail"
          <| fun _ ->
              // The go-red, and it is the encoder this repository shipped until Phase 290. It
              // agrees with the platform on every well-formed string, and loses on a high
              // surrogate followed by a unit that is not a low one — the units D801 D800 become
              // the four bytes of U+10000.
              Expect.equal
                  (utf8ReadingAnUncheckedLowHalf (units16 [ 0xD801; 0xD800 ]))
                  (System.Text.Encoding.UTF8.GetBytes(units16 [ 0xD800; 0xDC00 ]))
                  "the unchecked reading gives the ill-formed pair the bytes of a well-formed astral character"

              let t =
                  utf8Tally
                  |> Differential.over
                      [ for hi in 0xD800..0xDBFF do
                            for next in [ 0xDBFF; 0xDC00; 0xDFFF; 0xE000; 0x41 ] -> units16 [ hi; next ] ]
                      (fun s t ->
                          let agrees = utf8ReadingAnUncheckedLowHalf s = System.Text.Encoding.UTF8.GetBytes s

                          t
                          |> Tally.bump "Strings"
                          |> Tally.bumpIf (not agrees && strictlyEncodable s) "WellFormedLost"
                          |> Differential.check agrees (fun () ->
                              sprintf "the unchecked reading of [%s] is not the platform's bytes" (describe s)))

              Differential.expectLoses "an encoder that reads an unchecked low half" t

              Expect.equal
                  (List.length (Tally.disagreements t))
                  (1024 * 3)
                  "it loses on every high surrogate followed by a non-low unit"

              Expect.equal (Tally.count "WellFormedLost" t) 0 "and on no well-formed pair" ]
