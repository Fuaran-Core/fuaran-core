module FableSmoke.ParityVectors

// The cross-pipeline VALUE table (Phase 118). One list of `label -> bytes`, computed by calling the
// public surfaces, compiled into BOTH pipelines: `tests/Fuaran.Core.Tests/ParityVectorTests.fs`
// asserts it against a committed table of expected bytes, and `tests/fable-smoke/parity.ps1` runs
// this same table under `node` and byte-compares it against the .NET run. The two assertions are
// different claims and neither implies the other — the committed table says the .NET values have
// not moved, the node diff says the transpiled values agree with them.
//
// WHY THE TABLE IS COMPUTED HERE RATHER THAN IN THE TEST. A vector only the .NET suite can reach
// cannot be part of a cross-pipeline claim, and the Fable-compile gate beside this file is a
// COMPILE gate — it proves a construct transpiles, never that the transpiled code computes the same
// number. `fnv1a` sat divergent behind that gate until 0.6.0, and `Json.render`'s float case threw
// outright under Fable until Phase 118, both with the gate green.
//
// EVERY EMITTED VALUE IS ASCII BY CONSTRUCTION — hex digests, canonical numeric layouts, and JSON
// whose non-ASCII content is folded through a digest rather than echoed. The two runtimes do not
// agree about how to write a lone surrogate to a terminal, and a leg that reported a console
// encoding difference as a value divergence would be worse than no leg. Non-ASCII INPUTS are here
// in force; they simply leave as hex.

open Fuaran.Core

/// A non-ASCII input spanning the UTF-8 length classes: 2-byte (e-acute), 3-byte (CJK), and a
/// surrogate pair (U+1F600). Written as escapes rather than literal characters so the vectors do
/// not depend on this file's own encoding surviving a checkout.
let private unicodeSample = "café/日本語/\U0001F600"

/// The FIPS 180-4 two-block message (56 bytes: padding pushes it into a SECOND compression block).
/// This is the vector the masked add `Hash.(.+.)` exists for — the working variables only exceed
/// 2^53 on the second block, so removing the mask leaves every single-block digest correct and
/// turns exactly this one red under Fable. It is the go-red anchor of the whole leg.
let private twoBlock = "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"

let private hexChars = "0123456789abcdef"

let private hexOf (bs: byte[]) : string =
    let sb = System.Text.StringBuilder()

    for b in bs do
        sb.Append(hexChars[int b >>> 4]).Append(hexChars[int b &&& 0xF]) |> ignore

    sb.ToString()

/// The reference witness: one committed document exercising every `JVal` case, both key orders
/// (`Json.render` keeps author order, `Canon.render` sorts Ordinal), the escape path — including
/// the `Hash.foldSep` control byte — and a float in each renderer. ASCII in its content, so its
/// rendered form can be emitted verbatim.
let private witness: JVal =
    Json.kindObj
        "witness"
        [ "id", JStr "ref-0"
          "count", JInt 3
          "ratio", JFloat 0.1
          "flag", JBool true
          "tags", JArr [ JStr "a"; JStr "b" ]
          "nested",
          JObj
              [ "z", JInt 1
                "a", JFloat 2.5
                "esc", JStr("quote:\" back:\\ tab:\t nl:\n sep:" + Hash.foldSep) ] ]

let private streamWitness: StreamWitness<int, int, string> =
    { Apply = fun op st -> Ok(st + op)
      Encode = fun op -> Json.render (JInt op)
      Decode = fun s -> Decode.parse s |> Result.bind Decode.asInt }

/// A two-op chain under `OpStream.defaultHash`, with a `Human` and an `Agent` actor so the typed
/// attribution folded into the pre-image (Phase 320) is exercised on both shapes. Two ops, not one:
/// the second record's `PrevHash` is the first's `Hash`, so a divergence in the FIRST hash cannot
/// hide behind a matching second one.
let private chain: OpRecord<int> list =
    let step actor op (state, records) =
        match OpStream.append OpStream.defaultHash streamWitness actor op state records with
        | Ok(state', records') -> (state', records')
        | Error _ -> (state, records)

    (0, OpStream.empty)
    |> step (Human "ref") 1
    |> step (Agent("m", "v", "ag")) 2
    |> snd

let private recordAt (i: int) (project: OpRecord<int> -> string) : string =
    match List.tryItem i chain with
    | Some r -> project r
    | None -> "<no-record>"

/// The table. Order is part of the comparison, so it is a list and never a map.
let vectors: (string * string) list =
    [
      // ---- Hash.fnv1a — the 32-bit content fingerprint (D16's split-half multiply) ----
      "fnv1a/empty", Hash.fnv1a ""
      "fnv1a/a", Hash.fnv1a "a"
      "fnv1a/foldSep-join", Hash.fnv1a ("a" + Hash.foldSep + "b")
      "fnv1a/unicode", Hash.fnv1a unicodeSample
      "fnv1a/a80", Hash.fnv1a (String.replicate 80 "a")

      // ---- Hash.sha256* — the pinned pure FIPS 180-4 digest (D15) ----
      "sha256/empty", Hash.sha256Hex ""
      "sha256/abc", Hash.sha256Hex "abc"
      "sha256/two-block", Hash.sha256Hex twoBlock
      "sha256/unicode", Hash.sha256Hex unicodeSample
      "sha256/of-bytes", Hash.sha256HexOfBytes (Hash.utf8Bytes twoBlock)
      "utf8Bytes/unicode", hexOf (Hash.utf8Bytes unicodeSample)

      // ---- Wire.Canon.canonicalFloat — the pinned cross-host float layout (Phase 55) ----
      "canonicalFloat/zero", Canon.canonicalFloat 0.0
      "canonicalFloat/neg-zero", Canon.canonicalFloat -0.0
      "canonicalFloat/one-and-a-half", Canon.canonicalFloat 1.5
      "canonicalFloat/tenth", Canon.canonicalFloat 0.1
      "canonicalFloat/neg-third", Canon.canonicalFloat (-1.0 / 3.0)
      "canonicalFloat/e16", Canon.canonicalFloat 1e16
      "canonicalFloat/e17", Canon.canonicalFloat 1e17
      "canonicalFloat/e21", Canon.canonicalFloat 1e21
      "canonicalFloat/e-7", Canon.canonicalFloat 1e-7
      "canonicalFloat/max", Canon.canonicalFloat System.Double.MaxValue
      "canonicalFloat/denormal-min", Canon.canonicalFloat System.Double.Epsilon
      "canonicalFloat/nan", Canon.canonicalFloat nan
      "canonicalFloat/inf", Canon.canonicalFloat infinity
      "canonicalFloat/neg-inf", Canon.canonicalFloat -infinity

      // ---- Wire.Json — the author-ordered renderer's OWN float case, a separate layout from
      // `canonicalFloat`: it keeps the sign of -0 and emits the bare non-finite tokens that
      // `Json.tryRender` exists to refuse, so it needs its own vectors rather than riding along.
      "jsonRender/finite-floats",
      Json.render (
          JArr
              [ JFloat 0.0
                JFloat -0.0
                JFloat 1.5
                JFloat 1e21
                JFloat 1e-7
                JFloat System.Double.MaxValue ]
      )
      "jsonRender/non-finite-floats", Json.render (JArr [ JFloat nan; JFloat infinity; JFloat -infinity ])

      // ---- Wire.Json / Wire.Canon — encode and decode of the reference witness ----
      "witness/render", Json.render witness
      "witness/canon", Canon.render witness
      "witness/render-parse-render",
      (match Json.parse (Json.render witness) with
       | Ok back -> Json.render back
       | Error e -> "<parse-failed:" + e + ">")
      "witness/canon-parse-canon",
      (match Json.parse (Canon.render witness) with
       | Ok back -> Canon.render back
       | Error e -> "<parse-failed:" + e + ">")
      // The non-ASCII document: rendered through the escape path, then folded through the digest so
      // only hex reaches the terminal.
      "witness/unicode-canon-sha256", Hash.sha256Hex (Canon.render (JObj [ "u", JStr unicodeSample ]))

      // ---- OpStream.defaultHash — the op-stream CHAIN hash over a two-op chain ----
      "defaultHash/genesis", OpStream.defaultHash "" "{\"seq\":0}"
      "chain/hash-0", recordAt 0 _.Hash
      "chain/prev-1", recordAt 1 _.PrevHash
      "chain/hash-1", recordAt 1 _.Hash ]

/// Emit the table, one `VEC <label> <value>` line per vector — the byte-comparable form
/// `parity.ps1` diffs between the two pipelines. The `VEC ` prefix is what lets the runner filter
/// out anything a runtime writes around the program.
let emit () =
    vectors |> List.iter (fun (k, v) -> printfn "VEC %s %s" k v)
