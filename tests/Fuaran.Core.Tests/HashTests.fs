/// The pin suite for `Hash` — the spine's two hashing regimes.
///
/// The SHA-256 half is pinned two independent ways, because each answers a question the other
/// cannot. (1) IS IT SHA-256 — the published FIPS 180-4 known-answer vectors, including the
/// one-million-`a` vector. Passing these is the proof that the implementation is the standard
/// algorithm and not merely something self-consistent. (2) ARE OUR BYTES THE PLATFORM'S — byte
/// equality with `System.Security.Cryptography.SHA256` over a Unicode / emoji / block-boundary
/// corpus. The NIST vectors are all ASCII, so they say nothing about the hand-rolled UTF-8 encoder,
/// which is exactly where a surrogate-pair or continuation-byte defect would live.
///
/// A digest that is not itself pinned is a claim rather than a digest, which is why these vectors
/// travel with the implementation rather than living in whichever consumer happened to need them.
module Fuaran.Core.Tests.HashTests

open Expecto
open Fuaran.Core

/// The platform's own SHA-256, lowercase hex. Test-side only: `System.Security.Cryptography` does
/// not exist under Fable, which is the whole reason `Hash.sha256Hex` is a pure implementation.
let private bcl (s: string) =
    System.Security.Cryptography.SHA256.HashData(System.Text.UTF8Encoding(false).GetBytes s)
    |> Array.map (fun b -> b.ToString "x2")
    |> String.concat ""

/// Inputs chosen for the three things the ASCII vectors cannot reach: multi-byte UTF-8 (two-, three-
/// and four-byte sequences, including a ZWJ sequence of surrogate pairs), the 55/56/64-byte padding
/// boundary where a second block is forced, and a long multi-block body.
let private parityCorpus =
    [ ""
      "a"
      "core|op-stream|payload"
      "café"
      "Ω≈ç√∫˜µ≤≥÷"
      "日本語のテキスト"
      "emoji: 🔐🧾🇬🇧 and a ZWJ family 👨‍👩‍👧‍👦"
      String.replicate 40 "long-multi-block-canonical-payload-"
      System.String('x', 55) // one byte under a single-block pad
      System.String('x', 56) // the boundary that forces a second block
      System.String('x', 63)
      System.String('x', 64) // exactly one block, so the pad is a whole extra block
      System.String('x', 65) ]

[<Tests>]
let tests =
    testList
        "Hash"
        [

          // ---- (1) is it SHA-256 ----

          testCase "the pinned FIPS 180-4 known-answer vectors"
          <| fun _ ->
              Expect.equal
                  (Hash.sha256Hex "")
                  "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
                  "the empty string"

              Expect.equal
                  (Hash.sha256Hex "abc")
                  "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
                  "abc"

              Expect.equal
                  (Hash.sha256Hex "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq")
                  "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"
                  "the 448-bit two-block vector"

              Expect.equal
                  (Hash.sha256Hex
                      "abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu")
                  "cf5b16a778af8380036ce59e7b0492370b249b11e8f07a51afac45037afee9d1"
                  "the 896-bit multi-block vector"

          testCase "the one-million-'a' vector — the multi-block carry the Fable-safe add exists for"
          <| fun _ ->
              // Its own case because it is the vector that catches the specific failure the masked add
              // guards: working variables crossing 2^53 under float-backed numerics, which leaves
              // single-block digests correct and long ones silently wrong. A build that dropped the
              // mask passes every vector above and fails only here.
              Expect.equal
                  (Hash.sha256Hex (System.String('a', 1_000_000)))
                  "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0"
                  "one million 'a'"

          // ---- (2) are our bytes the platform's ----

          testCase "byte-for-byte equal to the platform's own SHA-256 over a Unicode corpus"
          <| fun _ ->
              for s in parityCorpus do
                  Expect.equal (Hash.sha256Hex s) (bcl s) (sprintf "matches the platform for %d chars" s.Length)

          testCase "and equal at EVERY length across the padding boundary"
          <| fun _ ->
              // A sweep rather than sampled boundaries: the pad rule has three cases (room in this
              // block, no room so a whole extra block, and the exact-fit boundary between them) and a
              // sampled test can miss whichever one the off-by-one lands in.
              for n in 0..200 do
                  let s = System.String('z', n)
                  Expect.equal (Hash.sha256Hex s) (bcl s) (sprintf "length %d" n)

          testCase "the UTF-8 encoder produces the platform's bytes, not merely the same digest"
          <| fun _ ->
              // Checked directly as well as through the digest. Two different byte strings can only
              // collide with negligible probability, so digest equality already implies this — but a
              // failure here names the encoder, whereas a digest mismatch names nothing.
              for s in parityCorpus do
                  Expect.equal
                      (Hash.utf8Bytes s)
                      (System.Text.UTF8Encoding(false).GetBytes s)
                      (sprintf "UTF-8 bytes for %d chars" s.Length)

          // ---- the byte-level form ----

          testCase "sha256Bytes is the same digest, unhexed"
          <| fun _ ->
              // The two public forms project from one compression pass, so this pins that they cannot
              // drift — a consumer chaining the raw bytes into another pre-image gets exactly what the
              // hex form describes.
              for s in parityCorpus do
                  let bytes = Hash.sha256Bytes (Hash.utf8Bytes s)
                  Expect.equal bytes.Length 32 "a SHA-256 digest is 32 bytes"

                  Expect.equal
                      (bytes |> Array.map (fun b -> b.ToString "x2") |> String.concat "")
                      (Hash.sha256Hex s)
                      (sprintf "the byte form hexes to the hex form for %d chars" s.Length)

                  Expect.equal
                      bytes
                      (System.Security.Cryptography.SHA256.HashData(System.Text.UTF8Encoding(false).GetBytes s))
                      "and equals the platform's raw digest"

          testCase "sha256HexOfBytes hashes arbitrary bytes, including ones no string encodes"
          <| fun _ ->
              // The reason a byte-level form exists at all: a caller with a digest, a nonce or a
              // length-prefixed frame has bytes that are not valid UTF-8 and must not be laundered
              // through a string to be hashed.
              let raw = [| 0uy; 1uy; 0x80uy; 0xFFuy; 0xC0uy; 0x00uy |]

              Expect.equal
                  (Hash.sha256HexOfBytes raw)
                  (System.Security.Cryptography.SHA256.HashData raw
                   |> Array.map (fun b -> b.ToString "x2")
                   |> String.concat "")
                  "raw bytes match the platform"

              Expect.equal
                  (Hash.sha256HexOfBytes (Hash.utf8Bytes "abc"))
                  "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
                  "and the string path is the byte path over UTF-8"

          // ---- the two regimes stay separate ----

          testCase "the two regimes are distinguishable by shape, so a silent fallback cannot hide"
          <| fun _ ->
              // The blunt guard. FNV-1a is eight hex characters and SHA-256 is sixty-four, so a path
              // that quietly fell back to the cache hash is caught by length alone — which is worth
              // having precisely because that fallback would otherwise be invisible.
              Expect.equal (Hash.fnv1a "anything").Length 8 "the cache fingerprint is 32-bit"
              Expect.equal (Hash.sha256Hex "anything").Length 64 "the crypto digest is 256-bit"
              Expect.notEqual (Hash.fnv1a "abc") (Hash.sha256Hex "abc") "and they are not the same function"

          testCase "fnv1a's pinned values hold — the .NET side is canonical and has never moved"
          <| fun _ ->
              // These three have now survived both a file move and a rewrite of the multiply, which
              // is the point of pinning them: content hashes across the estate fold through `fnv1a`,
              // so a value that shifted here would silently invalidate every stored one. The
              // split-half multiply was adopted precisely because it leaves this side alone.
              Expect.equal (Hash.fnv1a "") "811c9dc5" "the empty string is the FNV-1a offset basis"
              Expect.equal (Hash.fnv1a "a") "e40c292c" "a"
              Expect.equal (Hash.fnv1a "foobar") "bf9cf968" "foobar"

          testCase "fnv1a is cross-pipeline value-identical — the divergence pair pinned"
          <| fun _ ->
              // The finding this test exists for, kept as data rather than prose. Before the
              // split-half multiply, `fnv1a "a"` was `e40c292c` here and `e40c2930` under Fable: the
              // multiply overflowed the 2^53 exact-integer ceiling of JavaScript's doubles, so
              // precision was lost INSIDE the operation and the two pipelines minted different
              // chains from the same input. The .NET value is the canonical one; what moved is the
              // other side.
              Expect.equal (Hash.fnv1a "a") "e40c292c" "the canonical value, unmoved by the fix"

              Expect.notEqual
                  (Hash.fnv1a "a")
                  "e40c2930"
                  "and not the value the overflowing multiply produced under Fable"

              // An INDEPENDENT check that the split-half multiply computes true 32-bit FNV-1a
              // rather than something merely self-consistent: 64-bit arithmetic masked back to 32,
              // which cannot lose precision and shares no code with the implementation it checks.
              // Pinned vectors alone would not catch a split multiply that is wrong only for inputs
              // nobody pinned.
              let reference (s: string) =
                  let mutable h = 2166136261UL

                  for ch in s do
                      h <- (h ^^^ uint64 (uint32 ch)) &&& 0xFFFFFFFFUL
                      h <- (h * 16777619UL) &&& 0xFFFFFFFFUL

                  (uint32 h).ToString "x8"

              for s in
                  [ ""
                    "a"
                    "b"
                    "abc"
                    "foobar"
                    "message digest"
                    "" // the foldSep control byte
                    "ab"
                    "café"
                    "日本語"
                    "\U0001F600" // a surrogate pair — `fnv1a` folds UTF-16 code units
                    "￿"
                    String.replicate 80 "a"
                    String.replicate 257 "xy" ] do
                  Expect.equal
                      (Hash.fnv1a s)
                      (reference s)
                      (sprintf "the split multiply agrees with the 64-bit reference: %A" s)

              // What a .NET suite structurally CANNOT hold is the other pipeline's answer — a
              // compile gate cannot disagree about a number. That half is measured by a probe: a
              // scratch Fable build of a corpus, run under node and byte-compared against this run.
              // Re-run it if you touch the multiply; the result is recorded with the change.
              ()

          testCase "sha256Hex is deterministic and sensitive to a single bit"
          <| fun _ ->
              Expect.equal (Hash.sha256Hex "same") (Hash.sha256Hex "same") "the same input, twice"

              Expect.notEqual
                  (Hash.sha256Hex "payload")
                  (Hash.sha256Hex "payloae")
                  "one character apart, and the whole digest moves" ]
