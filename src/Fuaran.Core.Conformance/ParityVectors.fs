/// The cross-pipeline VALUE table — the vectors a Fable-compiled consumer runs on BOTH pipelines
/// and byte-compares (Phase 118; public since Phase 217).
///
/// One list of `label -> bytes`, computed by calling the public surfaces. Two claims rest on it and
/// neither implies the other. This repository's suite (`tests/Fuaran.Core.Tests/ParityVectorTests.fs`)
/// pins the table against committed expected bytes, so a change that moves a .NET value is a failing
/// test naming the vector. A consumer that owns a Fable toolchain compiles THIS module from the
/// package's `fable/` sources, runs `lines ()` under a JS runtime and under .NET, and diffs the two:
/// that is the claim that the transpiled code computes the same bytes. A defect that moves both
/// pipelines identically passes the diff and fails the pin; one that moves only the transpiled side
/// does the reverse.
///
/// WHY THE TABLE SHIPS AS CODE, NOT AS DATA. The transpiled side has to COMPUTE the vectors — a
/// committed table of expected bytes cannot be evaluated under Fable — and the consumer sees this
/// repository only as packages at the version it pins. Shipping the table inside the conformance kit
/// makes it version-coherent with the surfaces it measures by construction: the vectors a consumer
/// runs are always the ones for the Core it compiled.
///
/// WHY A COMPILE GATE IS NOT ENOUGH. A compile proves a construct transpiles, never that it computes
/// the same number. `Hash.fnv1a` sat divergent behind a green compile until 0.6.0, `Json.render`
/// threw under Fable for any float until Phase 118, and the `ConfRng` LCG before 0.20.0 drew zeros
/// under Fable — each with every compile green.
///
/// EVERY EMITTED VALUE IS ASCII BY CONSTRUCTION — hex digests, canonical numeric layouts, and JSON
/// whose non-ASCII content is folded through a digest rather than echoed. The two runtimes do not
/// agree about how to write a lone surrogate to a terminal, and a leg that reported a console
/// encoding difference as a value divergence would be worse than no leg. Non-ASCII INPUTS are here
/// in force; they simply leave as hex.
module Fuaran.Core.ParityVectors

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

/// A string from its UTF-16 units. An unpaired surrogate is built this way and never written as a
/// `\u` escape: the F# compiler replaces such an escape in a string literal with U+FFFD, so the
/// literal is a well-formed string and a row over it measures nothing about ill-formed input.
let private units (codes: int list) : string =
    codes |> List.map (fun c -> string (char c)) |> String.concat ""

/// The guarded UTF-8 encoder's answer as ASCII (Phase 306): `ok:<hex>`, or
/// `refused@<index>:<unit>` naming the unpaired surrogate.
let private guardedUtf8 (s: string) : string =
    match Hash.tryUtf8Bytes s with
    | Ok bytes -> "ok:" + hexOf bytes
    | Error bad ->
        "refused@"
        + string bad.Index
        + ":"
        + hexOf [| byte (bad.Unit >>> 8); byte (bad.Unit &&& 0xFF) |]

/// A guarded render's answer as ASCII: `ok:<sha256 of the text>`, or `refused` (the message
/// carries the offending string's path, whose text is not ASCII by construction).
let private guardedRender (r: Result<string, string>) : string =
    match r with
    | Ok text -> "ok:" + Hash.sha256Hex text
    | Error _ -> "refused"

/// `JInt 1` under `depth` arrays, built by a loop. The renderers were recursive until Phase 306
/// and died on a value this deep — on .NET by the stack, and a JS runtime has a stack too.
let private nested (depth: int) : JVal =
    let mutable v = JInt 1

    for _ in 1..depth do
        v <- JArr [ v ]

    v

/// A float aggregate over a float column, through the canonical layout, or the refusal's class.
let private floatAggregate (fn: AggFn) (xs: float list) : string =
    match Column.aggregate fn (Column.create "f" FloatType (xs |> List.map Float)) with
    | Ok(Float f) -> Canon.canonicalFloat f
    | Ok _ -> "<not-a-float>"
    | Error(AggregateOverflow _) -> "<overflow>"
    | Error _ -> "<refused>"

/// `Profile.tryParse` then `render`, or `refused`.
let private profileRoundTrip (s: string) : string =
    match Versioning.Profile.tryParse s with
    | Ok p -> Versioning.Profile.render p
    | Error _ -> "refused"

/// The first eight draws of `ConfRng.next` from a seed, hyphen-joined. EIGHT rather than one
/// because the first draw is the one a broken generator gets very nearly right: at seed 1488 the
/// LCG that stood here until 0.20.0 drew `1547650046` on .NET and `1547650048` under Fable — a
/// two-apart pair a single-draw vector would read as a rounding difference — and then `0` forever
/// after on the transpiled side.
let private drawsOf (seed: int) : string =
    let mutable r = ConfRng.ofSeed seed
    let out = ResizeArray<string>()

    for _ in 1..8 do
        let v, r' = ConfRng.next r
        r <- r'
        out.Add(string v)

    String.concat "-" out

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

/// `Column.aggregate` over a float column holding NaN, -0 and two NaNs, as `Min/Max/Median/
/// CountDistinct`, the floats through the canonical layout. F# generic comparison put NaN below
/// every value on .NET and above every value under Fable, so `Min`, `Max` and `Median` over such a
/// column answered by host until Phase 299 spelled the order out: NaN is one value and sorts LAST,
/// and -0 equals 0.
let private aggregateNanOrder: string =
    let col =
        Column.create "f" FloatType [ Float 3.0; Float nan; Float -1.0; Float -0.0; Float nan ]

    [ Min; Max; Median; CountDistinct ]
    |> List.map (fun fn ->
        match Column.aggregate fn col with
        | Ok(Float f) -> Canon.canonicalFloat f
        | Ok(Int i) -> string i
        | Ok _ -> "<not-a-number>"
        | Error _ -> "<refused>")
    |> String.concat "/"

/// The table. Order is part of the comparison, so it is a list and never a map.
/// Phase 315 — the corpus the named exports are held to what they are copies or renderings of:
/// SHA-256's padding and block boundaries (55/56/64/119/120 bytes after the join), the non-ASCII
/// length classes, and two ill-formed units, where the copied UTF-8 encoder must replace exactly as
/// the canonical one does.
let private exportCorpus: string list =
    [ ""
      "a"
      "abc"
      twoBlock
      unicodeSample
      units [ 0xD800 ]
      units [ 0xD801; 0xD800 ]
      String.replicate 46 "a"
      String.replicate 47 "a"
      String.replicate 55 "a"
      String.replicate 110 "a"
      String.replicate 111 "a"
      String.replicate 1000 "xy" ]

/// `agrees:<n>` when `f` and `g` give one value on every corpus input, else `diverges@<index>`.
let private agreement (f: string -> string) (g: string -> string) : string =
    match exportCorpus |> List.tryFindIndex (fun s -> f s <> g s) with
    | None -> "agrees:" + string (List.length exportCorpus)
    | Some i -> "diverges@" + string i

let vectors: (string * string) list =
    [
      // ---- Hash.fnv1a — the 32-bit content fingerprint (D16's split-half multiply) ----
      "fnv1a/empty", Hash.fnv1a ""
      "fnv1a/a", Hash.fnv1a "a"
      "fnv1a/foldSep-join", Hash.fnv1a ("a" + Hash.foldSep + "b")
      "fnv1a/unicode", Hash.fnv1a unicodeSample
      "fnv1a/a80", Hash.fnv1a (String.replicate 80 "a")

      // ---- Hash.canonicalFields — the capture keys' injective pre-image (Phase 225) ----
      // Printed as the UTF-8 hex of the pre-image, because the pre-image itself carries the two
      // control characters the encoding is made of and the emitted line must stay ASCII.
      "canonicalFields/plain", hexOf (Hash.utf8Bytes (Hash.canonicalFields [ "a"; "b" ]))
      "canonicalFields/symbols",
      hexOf (Hash.utf8Bytes (Hash.canonicalFields [ "a" + Hash.foldSep + "b"; Hash.fieldEsc; ""; "=#" ]))

      // ---- Hash.sha256* — the pinned pure FIPS 180-4 digest (D15) ----
      "sha256/empty", Hash.sha256Hex ""
      "sha256/abc", Hash.sha256Hex "abc"
      "sha256/two-block", Hash.sha256Hex twoBlock
      "sha256/unicode", Hash.sha256Hex unicodeSample
      "sha256/of-bytes", Hash.sha256HexOfBytes (Hash.utf8Bytes twoBlock)
      "utf8Bytes/unicode", hexOf (Hash.utf8Bytes unicodeSample)
      // Phase 290 — the ILL-FORMED rows: what the encoder does to a lone or ill-ordered surrogate
      // is the platform's answer (`EF BF BD` per unit that is not half of a pair), pinned on both
      // pipelines and asserted against `System.Text.Encoding.UTF8` on .NET. BUILT FROM UNITS
      // (`units`, Phase 306), never written as escapes: the F# compiler replaces an unpaired
      // surrogate escape in a literal with U+FFFD, so until then the .NET half of these rows
      // encoded replacement characters and never met an ill-formed unit. The expected bytes are
      // the same either way, which is how that went unseen. `high-then-nonlow` is the pair that
      // encoded as U+10000's four bytes until Phase 290; `high-then-high` and `low-then-high` are
      // two replacements each, never a pair read backwards.
      "utf8Bytes/ill-formed-lone-high", hexOf (Hash.utf8Bytes (units [ 0xD800 ]))
      "utf8Bytes/ill-formed-lone-low", hexOf (Hash.utf8Bytes (units [ 0xDFFF ]))
      "utf8Bytes/ill-formed-high-at-end", hexOf (Hash.utf8Bytes ("a" + units [ 0xD83D ]))
      "utf8Bytes/ill-formed-high-then-nonlow", hexOf (Hash.utf8Bytes (units [ 0xD801; 0xD800 ]))
      "utf8Bytes/ill-formed-high-then-ascii", hexOf (Hash.utf8Bytes (units [ 0xD83D ] + "z"))
      "utf8Bytes/ill-formed-low-then-high", hexOf (Hash.utf8Bytes (units [ 0xDE00; 0xD83D ]))
      "utf8Bytes/ill-formed-beside-a-pair", hexOf (Hash.utf8Bytes (units [ 0xD83D; 0xDE00; 0xDE00 ]))
      "sha256/ill-formed-lone-high", Hash.sha256Hex (units [ 0xD800 ])

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
      "chain/hash-1", recordAt 1 _.Hash

      // ---- ConfRng — the conformance kit's seeded draw stream ----
      // The kit's entire reproducibility claim is that a recorded seed redraws the same sample, and
      // a domain certifying in a browser runs the transpiled generator. `next` was a 32-bit LCG
      // until 0.20.0, and a 32-bit multiply is precisely the shape Fable cannot carry (`Hash.mul32`
      // documents why): the product is formed on a double and its low bits are gone before any mask
      // can recover them. Under node every draw after the first collapsed to zero, so a
      // self-contained `(seed, iterations)` family run drew a degenerate sample — a guarded family
      // reddened on its adequacy demand, an unguarded one passed vacuously green. Nothing in the
      // .NET suite could see it, and no compile gate ever could.
      // Seed 0 is here because it is the state xorshift must never reach, and -1 because it is the
      // seed whose `uint32` conversion differs most between the two runtimes.
      "confRng/seed-0", drawsOf 0
      "confRng/seed-1", drawsOf 1
      "confRng/seed-neg-1", drawsOf -1
      "confRng/seed-1488", drawsOf 1488

      // ---- Column.aggregate — the NaN-aware order (Phase 299) ----
      "aggregate/nan-order", aggregateNanOrder

      // ---- Phase 306 — totality against the machine. Appended, so every earlier row keeps its
      // place in the comparison. ----
      // FNV-1a's unit is the UTF-16 code unit: one astral character is two steps (D83D, DE00). A
      // twin that folds its UTF-8 bytes or its code point computes another value here, and the
      // same value as this one on every ASCII input.
      "fnv1a/astral-code-units", Hash.fnv1a (units [ 0xD83D; 0xDE00 ])
      // The guarded encoder: a well-formed string is its bytes; an unpaired surrogate is refused
      // with its index and unit, where the unguarded rows above answer replacement bytes.
      "tryUtf8Bytes/well-formed", guardedUtf8 unicodeSample
      "tryUtf8Bytes/lone-high", guardedUtf8 (units [ 0xD800 ])
      "tryUtf8Bytes/high-then-high", guardedUtf8 (units [ 0xD801; 0xD800 ])
      "tryUtf8Bytes/stray-low-after-a-pair", guardedUtf8 (units [ 0xD83D; 0xDE00; 0xDE00 ])
      // The guarded canonical render refuses an ill-formed string, as a value and as a member key.
      "canonTryRender/well-formed", guardedRender (Canon.tryRender (JObj [ "k", JStr unicodeSample ]))
      "canonTryRender/ill-formed-value", guardedRender (Canon.tryRender (JObj [ "k", JStr(units [ 0xD801; 0xD800 ]) ]))
      "canonTryRender/ill-formed-key", guardedRender (Canon.tryRender (JObj [ units [ 0xDC00 ], JInt 1 ]))
      "jsonTryRender/ill-formed-value", guardedRender (Json.tryRender (JArr [ JStr(units [ 0xDFFF ]) ]))
      // A value 10,000 deep renders — folded through the digest, since the text is 20,001 bytes.
      "render/depth-10000-json", Hash.sha256Hex (Json.render (nested 10000))
      "render/depth-10000-canon", guardedRender (Canon.tryRender (nested 10000))
      // The float aggregates over finite input whose plain formulas overflowed an intermediate.
      "aggregate/median-at-the-edge", floatAggregate Median [ 1e308; 1e308 ]
      "aggregate/mean-at-the-edge", floatAggregate Mean [ 1.7e308; 1.7e308; -1.7e308 ]
      "aggregate/stddev-at-the-edge", floatAggregate StdDev [ 1e200; -1e200 ]
      "aggregate/sum-past-the-edge", floatAggregate Sum [ 1e308; 1e308 ]
      "aggregate/stddev-population", floatAggregate StdDev [ 2.0; 4.0; 4.0; 4.0; 5.0; 5.0; 7.0; 9.0 ]
      // The profile grammar: the canonical string and nothing else. The NUL is built, not written.
      "profile/canonical", profileRoundTrip "core@1.0"
      "profile/leading-zero", profileRoundTrip "core@01.0"
      "profile/plus-sign", profileRoundTrip "core@+1.0"
      "profile/trailing-nul", profileRoundTrip ("core@1.0" + string (char 0))
      "profile/int32-max", profileRoundTrip "core@2147483647.2147483647"
      "profile/past-int32", profileRoundTrip "core@2147483648.0"

      // ---- Phase 315 — the light set's named exports. Appended, so every earlier row keeps its
      // place in the comparison. ----
      // `OpStream.sha256Hash` is OpStream's own copy of the digest (D2): held to `Hash.sha256Hex`
      // over the export corpus, and pinned at two values of its own.
      "sha256Hash/genesis", OpStream.sha256Hash "" "{\"seq\":0}"
      "sha256Hash/two-block", OpStream.sha256Hash "deadbeef" twoBlock
      "sha256Hash/agrees-with-sha256Hex",
      agreement (OpStream.sha256Hash "deadbeef") (fun s -> Hash.sha256Hex ("deadbeef|" + s))
      // `Hash.fnv1a32`, the raw value — printed in decimal, the one rendering both runtimes share.
      "fnv1a32/a", string (Hash.fnv1a32 "a")
      "fnv1a32/unicode", string (Hash.fnv1a32 unicodeSample)
      "fnv1a32/agrees-with-fnv1a", agreement (fun s -> (Hash.fnv1a32 s).ToString("x8")) Hash.fnv1a
      // `FloatLayout`, public: the finite layout keeps the sign of -0 (`canonicalFloat` drops it).
      "floatLayout/finite-neg-zero", FloatLayout.finite -0.0
      "floatLayout/finite-tenth", FloatLayout.finite 0.1
      "floatLayout/finite-e21", FloatLayout.finite 1e21
      "floatLayout/finite-e-7", FloatLayout.finite 1e-7
      "floatLayout/finite-neg-third", FloatLayout.finite (-1.0 / 3.0)
      "floatLayout/round-trip-non-finite",
      String.concat
          ","
          [ FloatLayout.roundTrip nan
            FloatLayout.roundTrip infinity
            FloatLayout.roundTrip -infinity ]
      // `Cell.token` and `Cell.compare` — NaN one token and last in the order, -0 and 0 one value,
      // a decimal at its canonical text.
      "cellToken/floats",
      [ Float nan
        Float infinity
        Float -infinity
        Float -0.0
        Float 0.0
        Float 0.1
        Float 1e21 ]
      |> List.map Cell.token
      |> String.concat " "
      "cellToken/scalars",
      [ Int -3; Bool true; Str "s"; Date "2026-10-01"; Null ]
      |> List.map Cell.token
      |> String.concat " "
      "cellToken/decimal-canonical", Cell.token (Decimal "1.50")
      "cellCompare/float-order",
      [ Float nan; Float infinity; Float 1.0; Float -0.0; Int 0; Float -infinity ]
      |> List.sortWith (fun a b -> Cell.compare a b |> Option.defaultValue 0)
      |> List.map Cell.token
      |> String.concat " " ]

/// The hash SWEEP's inputs — absorbed from the retired `tests/hash-parity-probe` (Phase 217), so the
/// arithmetic cases that separate the two pipelines are run on every cross-pipeline check rather
/// than by hand. Empty and single characters, the multi-byte UTF-8 classes, a surrogate pair and a
/// ZWJ sequence, control bytes including the `Hash.foldSep` byte, every length 0..80 (so no carry
/// pattern is missed), and lengths straddling SHA-256's 55/56/119/120 padding boundaries and its
/// multi-block threshold. Written with escapes, so the corpus survives any checkout encoding.
let private sweepInputs: string list =
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
      // NUL, built rather than written: a raw NUL byte in the source makes git classify the file as
      // binary, which silently disables end-of-line normalisation for it.
      string (char 0)
      "\u0001"
      "a\u0001b"
      "\u007F"
      "\u0080"
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

/// A `Schema` built from a sweep input, so `Column`'s private FNV-1a copy is exercised over the same
/// inputs as the other two — including the non-ASCII ones, where a code-unit-vs-byte fold would show.
let private schemaOf (s: string) : Schema =
    [ (if s = "" then "c" else s), IntType; "n" + s, FloatType ]

/// The hash sweep: one row per input, `hashSweep/NNN` (the input's index) to FOUR values joined by
/// `/`, one per FNV-1a implementation the spine actually ships plus the digest beside them — the
/// canonical `Hash.fnv1a`, `Hash.sha256Hex`, `OpStream`'s copy (through `defaultHash`, the op-stream
/// CHAIN hash) and `Column`'s copy (through `Schema.fingerprint`). INDEXED rather than echoed, so the
/// comparison never turns on console encoding. Probing only the canonical implementation is what let
/// the chain hash stay divergent after the canonical one was fixed, which is why all three are here.
let hashSweep: (string * string) list =
    sweepInputs
    |> List.mapi (fun i s ->
        sprintf "hashSweep/%03d" i,
        String.concat
            "/"
            [ Hash.fnv1a s
              Hash.sha256Hex s
              OpStream.defaultHash "deadbeef" s
              Schema.fingerprint (schemaOf s) ])

/// Every vector as the line a runner compares: `VEC <label> <value>`, the named table first and the
/// sweep after it. Order is part of the comparison. The `VEC ` prefix is what lets a runner filter
/// out anything a runtime writes around the program; a label never contains a space, so the line
/// splits on its first one.
let lines () : string list =
    vectors @ hashSweep |> List.map (fun (k, v) -> sprintf "VEC %s %s" k v)
