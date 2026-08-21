module HashParityProbe.Program

// The cross-pipeline VALUE probe. Prints `fnv1a` and `sha256Hex` over a fixed corpus, one line per
// input, in a form that is byte-comparable between the .NET run and the Fable-under-node run:
// `./run-parity-probe.ps1` runs it both ways and diffs. Identical output is the claim that neither
// the compile gate nor the .NET suite can make.
//
// The corpus is INDEXED rather than echoed, so the comparison never turns on console encoding — the
// two runtimes do not agree about how to write a lone surrogate or U+FFFF to a terminal, and a probe
// that reported that difference as a hash divergence would be worse than no probe. It spans the
// cases that separate the two pipelines' arithmetic: empty and single characters, the multi-byte
// UTF-8 classes, a surrogate pair, control bytes including the `foldSep` byte, every length 0..80
// (so no carry pattern is missed), and lengths straddling SHA-256's 55/56/119/120 padding
// boundaries and its multi-block threshold.

open Fuaran.Core

let corpus: string list =
    [ ""
      "a"
      "b"
      "c"
      "ab"
      "abc"
      "abcd"
      "foobar"
      "message digest"
      "The quick brown fox jumps over the lazy dog"
      "0"
      "1"
      "9"
      " "
      // NUL, built rather than written: a raw NUL byte in the source makes git classify this whole
      // file as binary, which silently disables end-of-line normalisation for it.
      string (char 0)
      "" // the foldSep control byte
      "ab"
      ""
      ""
      "ÿ"
      "café"
      "日本語"
      "😀" // U+1F600 as a surrogate pair
      "👩‍💻" // ZWJ sequence
      "�"
      "￿"
      "smoke" ]
    @ [ for n in 0..80 -> String.replicate n "a" ]
    @ [ for n in [ 1; 2; 3; 55; 56; 57; 63; 64; 65; 119; 120; 127; 128; 129; 256; 1000 ] -> String.replicate n "xy" ]

/// A `Schema` built from the corpus entry, so `Column`'s copy is exercised over the same inputs as
/// the other two — including the non-ASCII ones, which is where a code-unit-vs-byte fold would show.
let private schemaOf (s: string) : Schema =
    [ (if s = "" then "c" else s), IntType; "n" + s, FloatType ]

[<EntryPoint>]
let main _ =
    // FOUR columns per line, one per implementation the spine actually ships: the canonical
    // `Hash.fnv1a`, the pinned SHA-256 beside it, `OpStream`'s copy (reached through
    // `defaultHash`, the op-stream CHAIN hash), and `Column`'s copy (through `Schema.fingerprint`).
    // Probing only the canonical one is what let the chain hash stay divergent after it was fixed.
    corpus
    |> List.iteri (fun i s ->
        printfn
            "%03d %s %s %s %s"
            i
            (Hash.fnv1a s)
            (Hash.sha256Hex s)
            (OpStream.defaultHash "deadbeef" s)
            (Schema.fingerprint (schemaOf s)))

    0
