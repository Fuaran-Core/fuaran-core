module Fuaran.Core.Tests.PremiseProbeExport

// ============================================================================
//  The host-premise probes (Phase 483): one vector family per `host-premise` row of the claims ladder.
//
//  The models assume things the CLR gives this implementation for free: a string is a sequence of
//  UTF-16 code units, a double's digits are the shortest that round-trip, an integer token is judged
//  against 2^53 by its text, SHA-256 is FIPS 180-4, and a stack holds the parser's bounded recursion.
//  Another host's runtime answers each of those differently. Each premise is a `host-premise` row in
//  `proofs.json` naming its probe here by family id, and each probe is a committed file under
//  `conformance/premises/`: a host that reproduces every vector of a probe has discharged that premise
//  on its own runtime, and a host that does not has a named premise it has not answered.
//
//  Every expected answer is COMPUTED by calling the reference implementation, never typed in. Where an
//  extracted model asks the same question (the parser, `proofs/JsonParse.fst`) the export runs the model
//  too and stamps the vector `["reference", "model:JsonParse"]`, or fails naming the vector and the
//  ladder row (`VectorOracle.stampValue`). Two answers are also checked against a second, independent
//  source at export time, so the probe cannot record a digit or a digest that only this implementation
//  believes: every float vector's text reads back to its bits, and every SHA-256 digest equals the
//  platform's own `System.Security.Cryptography.SHA256`.
//
//  The family is Core-owned and self-enumerated (its own `manifest.json`), as `decode/` and `refusals/`
//  are; nothing copies it into the shared corpus.
// ============================================================================

open System
open System.IO
open Fuaran.Core

/// The family directory under `conformance/`.
let familyDirName = "premises"

let manifestFileName = "manifest.json"

/// One probe: the family a `host-premise` row names, the file it is written to, and its vectors.
type Probe =
    {
        /// The family id — the value of the ladder row's `probe`.
        Id: string
        /// The vector kind, as the manifest records it.
        Kind: string
        /// The vector file beside the manifest.
        File: string
        /// The `host-premise` row this probe discharges.
        Row: string
        /// What a vector is and what reproducing it discharges.
        Description: string
        /// The vectors, each one compact object.
        Vectors: unit -> JVal list
    }

// ---------------------------------------------------------------------------
//  Shared spellings
// ---------------------------------------------------------------------------

let private hexOf (bs: byte[]) : string =
    Convert.ToHexString(bs).ToLowerInvariant()

let private bitsOf (f: float) : string =
    (BitConverter.DoubleToInt64Bits f).ToString("x16")

/// A refusal's class, spelled as the parser model's bridge spells it (`VectorOracle.JsonModel.readStrict`):
/// a `JsonErrorKind` case name, or `LimitExceeded/<limit>`.
let private refusalClass (r: JsonRefusal) : string =
    match r with
    | JsonRefusal.Malformed e -> sprintf "%A" e.Kind
    | JsonRefusal.LimitExceeded b -> "LimitExceeded/" + JsonLimit.name b.Limit

/// A vector: its id, its input members, its expected answer, and the stamp.
let private vector (id: string) (input: (string * JVal) list) (expected: JVal) (model: JVal option) : JVal =
    let stamp =
        VectorOracle.stampValue
            id
            expected
            (model
             |> Option.map (fun m -> VectorOracle.JsonModel.Model, VectorOracle.JsonModel.LadderRow, m))

    JObj([ "id", JStr id ] @ input @ [ "expected", expected; stamp ])

/// A string the parser returned, as a host compares it: its UTF-8 bytes. The parser returns only
/// well-formed strings, so the guarded encoder cannot refuse here; if it did the export would be wrong.
let private utf8Answer (s: string) : JVal =
    match Hash.tryUtf8Bytes s with
    | Ok bytes -> JObj [ "utf8", JStr(hexOf bytes) ]
    | Error bad -> failwithf "the parser returned a string with an unpaired surrogate at unit %d" bad.Index

// ---------------------------------------------------------------------------
//  1. The string model
// ---------------------------------------------------------------------------

/// A string from its UTF-16 units — never a `\u` escape in a literal, which the F# compiler turns into
/// U+FFFD for an unpaired surrogate.
let private ofUnits (units: int list) : string =
    units |> List.map (fun u -> string (char u)) |> String.concat ""

/// The encode cases: UTF-16 code units and the guarded encoder's answer.
let private encodeCases: (string * int list) list =
    [ "empty", []
      "ascii", [ 0x41; 0x62; 0x63 ]
      "two-byte", [ 0x63; 0xE9 ]
      "three-byte", [ 0x65E5; 0x672C ]
      "last-before-surrogates", [ 0xD7FF ]
      "first-after-surrogates", [ 0xE000 ]
      "last-bmp", [ 0xFFFF ]
      "pair-first-supplementary", [ 0xD800; 0xDC00 ]
      "pair-emoji", [ 0xD83D; 0xDE00 ]
      "pair-last-code-point", [ 0xDBFF; 0xDFFF ]
      "lone-high-alone", [ 0xD800 ]
      "lone-high-at-end", [ 0x61; 0xD83D ]
      "lone-high-before-text", [ 0xD83D; 0x61 ]
      "lone-low-alone", [ 0xDC00 ]
      "lone-low-after-text", [ 0x61; 0xDE00 ]
      "pair-reversed", [ 0xDE00; 0xD83D ]
      "high-high-low", [ 0xD83D; 0xD83D; 0xDE00 ]
      "high-low-low", [ 0xD83D; 0xDE00; 0xDE00 ]
      "pair-then-lone-high", [ 0xD83D; 0xDE00; 0xDBFF ] ]

let private encodeVector (name: string, units: int list) : JVal =
    let expected =
        match Hash.tryUtf8Bytes (ofUnits units) with
        | Ok bytes -> JObj [ "utf8", JStr(hexOf bytes) ]
        | Error bad -> JObj [ "refused", JObj [ "index", JInt bad.Index; "unit", JInt bad.Unit ] ]

    vector ("encode/" + name) [ "units", JArr(units |> List.map JInt) ] expected None

/// The parse cases: a JSON text (ASCII, escapes spelled out) whose one string the parser reads.
let private parseCases: (string * string) list =
    [ "escaped-pair", "\"\\ud83d\\ude00\""
      "escaped-pair-upper-hex", "\"\\uD83D\\uDE00\""
      "escaped-bmp", "\"\\u00e9\\u65e5\""
      "escaped-lone-high", "\"\\ud800\""
      "escaped-lone-high-then-text", "\"\\ud83dx\""
      "escaped-lone-low", "\"\\udc00\""
      "escaped-pair-reversed", "\"\\ude00\\ud83d\""
      "escaped-high-then-escaped-bmp", "\"\\ud83d\\u0041\""
      "escaped-lone-high-in-member-key", "{\"\\ud800\":1}"
      "escaped-pair-in-member-key", "{\"\\ud83d\\ude00\":1}" ]

let private parseVector (name: string, text: string) : JVal =
    let answer (v: JVal) : JVal =
        match v with
        | JStr s -> utf8Answer s
        | JObj [ k, _ ] -> JObj [ "key", utf8Answer k ]
        | other -> failwithf "parse case `%s` reads %A, which is no string" name other

    let expected =
        match Json.read ReadOptions.strict text with
        | Ok v -> answer v
        | Error r -> JObj [ "refused", JStr(refusalClass r) ]

    let model =
        match VectorOracle.JsonModel.readStrict text with
        | Ok v -> answer v
        | Error cls -> JObj [ "refused", JStr cls ]

    vector ("parse/" + name) [ "text", JStr text ] expected (Some model)

let private stringModel: Probe =
    { Id = "premiseStringModel"
      Kind = "string-model"
      File = "string-model.json"
      Row = "host-string-model"
      Description =
        "The string-model premise. A string is a sequence of UTF-16 code units, and only well-formed UTF-16 is text: an unpaired surrogate (a high surrogate D800-DBFF not followed at once by a low one DC00-DFFF, or a low one with no high one before it) has no code points and no UTF-8 encoding. `encode/*` vectors give `units` (the code units, as integers) and the guarded encoder's answer: `utf8`, the lower-case hex of the UTF-8 bytes, or `refused` with the `index` and `unit` of the first unpaired surrogate. `parse/*` vectors give a JSON `text` holding one string (or one member key) and the strict parser's answer: its UTF-8 bytes as `utf8` (under `key` for a member key), or `refused` with the refusal's class. A host reproduces every vector over its own string representation; how it maps that representation onto code units is the host's, and docs/host-premises.md states it for UTF-8, byte and code-point strings."
      Vectors = fun () -> (encodeCases |> List.map encodeVector) @ (parseCases |> List.map parseVector) }

// ---------------------------------------------------------------------------
//  2. The float digits
// ---------------------------------------------------------------------------

/// SplitMix64 — a deterministic generator stated here in full, so the sample is the same on every
/// runtime this file is ever rendered on.
let private splitMix (state: uint64) : uint64 * uint64 =
    let s = state + 0x9E3779B97F4A7C15UL
    let z = (s ^^^ (s >>> 30)) * 0xBF58476D1CE4E5B9UL
    let z = (z ^^^ (z >>> 27)) * 0x94D049BB133111EBUL
    z ^^^ (z >>> 31), s

/// `count` finite doubles from `seed`, each drawn by `pick` from one 64-bit draw.
let private sample (seed: uint64) (count: int) (pick: uint64 -> float) : float list =
    let mutable state = seed

    [ for _ in 1..count do
          let r, s = splitMix state
          state <- s
          yield pick r ]

/// The four strata, each sampled: any finite bit pattern, small magnitudes with few significant
/// digits, the subnormals, and the integral range past 2^53.
let private sampled: (string * float) list =
    let anyFinite (r: uint64) =
        // A raw bit pattern with its exponent field cleared of the all-ones (non-finite) value.
        let bits = r &&& 0xFFEFFFFFFFFFFFFFUL
        BitConverter.Int64BitsToDouble(int64 bits)

    // Powers of ten up to 10^6 are exact doubles, and one IEEE multiplication or division rounds the
    // same on every runtime — a library `pow` is not held to that, so it is not used.
    let decimalShort (r: uint64) =
        let mantissa = float (r % 1000000UL)
        let scale = int ((r >>> 32) % 13UL) - 6
        let ten = float (pown 10L (abs scale))
        if scale >= 0 then mantissa * ten else mantissa / ten

    let subnormal (r: uint64) =
        BitConverter.Int64BitsToDouble(int64 (r &&& 0x000FFFFFFFFFFFFFUL ||| 1UL))

    let pastInt53 (r: uint64) =
        let m = float (r &&& 0x001FFFFFFFFFFFFFUL)
        let e = int ((r >>> 53) % 12UL)
        (9007199254740992.0 + m) * float (1L <<< e)

    [ yield! sample 1UL 64 anyFinite |> List.mapi (fun i f -> sprintf "sample/any-%02d" i, f)
      yield!
          sample 2UL 32 decimalShort
          |> List.mapi (fun i f -> sprintf "sample/short-%02d" i, f)
      yield!
          sample 3UL 16 subnormal
          |> List.mapi (fun i f -> sprintf "sample/subnormal-%02d" i, f)
      yield!
          sample 4UL 16 pastInt53
          |> List.mapi (fun i f -> sprintf "sample/past-int53-%02d" i, f) ]

/// The edges, named.
let private floatEdges: (string * float) list =
    [ "zero", 0.0
      "negative-zero", -0.0
      "one", 1.0
      "minus-one-and-a-half", -1.5
      "tenth", 0.1
      "tenth-plus-fifth", 0.1 + 0.2
      "third", 1.0 / 3.0
      "two-thirds", 2.0 / 3.0
      "four-point-three-five", 4.35
      "smallest-subnormal", BitConverter.Int64BitsToDouble 1L
      "largest-subnormal", BitConverter.Int64BitsToDouble 0x000FFFFFFFFFFFFFL
      "smallest-normal", BitConverter.Int64BitsToDouble 0x0010000000000000L
      "largest-finite", Double.MaxValue
      "smallest-finite", -Double.MaxValue
      "below-1e-4", 0.0000999
      "at-1e-4", 0.0001
      "at-1e-5", 0.00001
      "at-1e-7", 1e-7
      "at-1e15", 1e15
      "at-1e16", 1e16
      "below-1e17", 99999999999999984.0
      "at-1e17", 1e17
      "at-1e21", 1e21
      "at-1e22", 1e22
      "at-1e23", 1e23
      "two-pow-53", 9007199254740992.0
      "two-pow-53-next", 9007199254740994.0
      "two-pow-53-prev", 9007199254740991.0
      "two-pow-63", 9223372036854775808.0
      "two-pow-64", 18446744073709551616.0
      "five-e-minus-324", 5e-324
      "seventeen-digits", 0.30000000000000004
      "pi", Math.PI
      "e", Math.E
      "one-plus-epsilon", 1.0000000000000002 ]

let private floatVector (name: string, f: float) : JVal =
    let text = FloatLayout.canonical f

    // The second source: the text reads back, through the platform's own reader, to the bits written
    // — the negative zero excepted, which the wire collapses to zero before the layout runs.
    let readBack =
        Double.Parse(text, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture)

    let written = if f = 0.0 then 0.0 else f

    if bitsOf readBack <> bitsOf written then
        failwithf
            "the export refuses: float `%s` (%s) lays out as %s, which reads back as %s"
            name
            (bitsOf f)
            text
            (bitsOf readBack)

    vector name [ "bits", JStr(bitsOf f) ] (JObj [ "canonical", JStr text ]) None

let private floatDigits: Probe =
    { Id = "premiseFloatDigits"
      Kind = "float-digits"
      File = "float-digits.json"
      Row = "host-float-digits"
      Description =
        "The float-digits premise. A finite double is written with the SHORTEST decimal digits that read back to the same double, laid out by the canonical float rule (WIRE_FORMAT section 2 rule 5): fixed point when the magnitude is at least 1e-4 and below 1e17, otherwise the digits in exponent form (`1E-05`, `1.8446744073709552E+19`: a capital E, a sign, at least two exponent digits), and `-0` collapsed to `0`. Each vector gives `bits`, the IEEE 754 binary64 bit pattern as 16 lower-case hex digits, and `canonical`, the text the canonical writer gives it; the export has checked that every `canonical` reads back to `bits` (zero for the negative zero). The edges are named; `sample/*` vectors are drawn by SplitMix64 from four strata (any finite bit pattern, short decimals, subnormals, integers past 2^53). A host reproduces `canonical` from `bits` for every vector, and reads every `canonical` back to `bits`."
      Vectors = fun () -> (floatEdges @ sampled) |> List.map floatVector }

// ---------------------------------------------------------------------------
//  3. Integer width at the int53 guard
// ---------------------------------------------------------------------------

let private intTokens: string list =
    [ "0"
      "-0"
      "1"
      "-1"
      "2147483647"
      "2147483648"
      "-2147483648"
      "-2147483649"
      "4294967296"
      "9007199254740991"
      "9007199254740992"
      "-9007199254740991"
      "-9007199254740992"
      "9007199254740993"
      "-9007199254740993"
      "9007199254740994"
      "10000000000000000"
      "-10000000000000000"
      "10000000000000001"
      "18014398509481984"
      "18014398509481985"
      "99999999999999984"
      "100000000000000000"
      "123456789012345678"
      "9223372036854775807"
      "9223372036854775808"
      "18446744073709551615"
      "18446744073709551616"
      "100000000000000000000" ]

let private intVector (token: string) : JVal =
    let answer (v: JVal) : JVal =
        match v with
        | JInt n -> JObj [ "int", JInt n ]
        | JFloat f -> JObj [ "float", JStr(bitsOf f) ]
        | other -> failwithf "int53 token %s reads %A, which is no number" token other

    let expected =
        match Json.read ReadOptions.strict token with
        | Ok v -> answer v
        | Error r -> JObj [ "refused", JStr(refusalClass r) ]

    let model =
        match VectorOracle.JsonModel.readStrict token with
        | Ok v -> answer v
        | Error cls -> JObj [ "refused", JStr cls ]

    vector token [ "token", JStr token ] expected (Some model)

let private int53: Probe =
    { Id = "premiseInt53"
      Kind = "int53"
      File = "int53.json"
      Row = "host-int53"
      Description =
        "The integer-width premise. An integer token is judged by its TEXT against 2^53 = 9007199254740992, never by the double it would round to: a token within the 32-bit signed range reads as an integer (`int`); one beyond it and at most 2^53 in magnitude reads as a double, exactly (`float`, its binary64 bit pattern as 16 lower-case hex digits); one past 2^53 reads as a double only when it is that double's canonical float layout, and is otherwise refused (`refused`, the refusal's class). Each vector gives the `token`, read as a whole JSON document. A host reproduces every answer: its number type holds every integer up to 2^53 exactly, and it refuses the token 2^53 + 1 rather than reading it as 2^53."
      Vectors = fun () -> intTokens |> List.map intVector }

// ---------------------------------------------------------------------------
//  4. SHA-256 known answers
// ---------------------------------------------------------------------------

/// The cases: a hex pattern and how many times it repeats.
let private shaCases: (string * string * int) list =
    let ascii (s: string) = hexOf (Text.Encoding.ASCII.GetBytes s)

    [ "empty", "", 1
      "abc", ascii "abc", 1
      "fips-two-block", ascii "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", 1
      "fips-896-bit",
      ascii
          "abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu",
      1
      "one-zero-byte", "00", 1
      "one-ff-byte", "ff", 1
      "length-55", "61", 55
      "length-56", "61", 56
      "length-57", "61", 57
      "length-63", "61", 63
      "length-64", "61", 64
      "length-65", "61", 65
      "length-119", "61", 119
      "length-120", "61", 120
      "every-byte-value", String.Join("", [ for b in 0..255 -> (byte b).ToString("x2") ]), 1
      "every-byte-value-four-times", String.Join("", [ for b in 0..255 -> (byte b).ToString("x2") ]), 4
      "fips-million-a", "61", 1000000 ]

let private shaVector (name: string, pattern: string, repeat: int) : JVal =
    let one = Convert.FromHexString pattern
    let bytes = Array.init (one.Length * repeat) (fun i -> one[i % one.Length])
    let digest = Hash.sha256HexOfBytes bytes

    // The second source: the platform's own SHA-256.
    let platform = hexOf (Security.Cryptography.SHA256.HashData bytes)

    if digest <> platform then
        failwithf "the export refuses: sha256 `%s` is %s here and %s on the platform" name digest platform

    vector name [ "input", JObj [ "hex", JStr pattern; "repeat", JInt repeat ] ] (JObj [ "sha256", JStr digest ]) None

let private sha256: Probe =
    { Id = "premiseSha256"
      Kind = "sha256"
      File = "sha256.json"
      Row = "host-sha256"
      Description =
        "The hash premise. SHA-256 is FIPS 180-4 SHA-256 on every input length. Wherever this package names SHA-256 (the crypto digest a chain or a DAG is minted under, every digest a conformance vector records as SHA-256) it is SHA-256 over bytes, and a host whose digest differs on an input reproduces none of those there. Each vector gives `input`, a byte string as `hex` (lower case) repeated `repeat` times, and `sha256`, its digest as 64 lower-case hex digits; the export has checked every digest against the platform's own SHA-256. The lengths sit on the padding boundaries (55, 56, 57, 63, 64, 65, 119 and 120 bytes), beside the FIPS 180-4 messages, every byte value, and a million `a`."
      Vectors = fun () -> shaCases |> List.map shaVector }

// ---------------------------------------------------------------------------
//  5. Recursion depth against the stack
// ---------------------------------------------------------------------------

/// The cases: the opening and closing spelling of one level, the innermost value, and the depth.
let private depthCases: (string * string * string * string * int) list =
    [ for d in [ 1; 2; 64; 256; 511; 512; 513; 1024; 100000 ] do
          yield sprintf "array-%d" d, "[", "0", "]", d
      for d in [ 511; 512; 513; 100000 ] do
          yield sprintf "object-%d" d, "{\"a\":", "0", "}", d ]

let private depthVector (name: string, opens: string, inner: string, closes: string, depth: int) : JVal =
    let text = String.replicate depth opens + inner + String.replicate depth closes

    let answer (v: JVal) : JVal =
        let rendered = Json.render v

        if rendered <> text then
            failwithf "the export refuses: depth case `%s` renders back as other text" name

        JObj [ "accepted", JStr(Hash.sha256Hex rendered) ]

    let expected =
        match Json.read ReadOptions.strict text with
        | Ok v -> answer v
        | Error r -> JObj [ "refused", JStr(refusalClass r) ]

    let model =
        match VectorOracle.JsonModel.readStrict text with
        | Ok v -> answer v
        | Error cls -> JObj [ "refused", JStr cls ]

    vector
        name
        [ "input",
          JObj
              [ "open", JStr opens
                "inner", JStr inner
                "close", JStr closes
                "depth", JInt depth ] ]
        expected
        (Some model)

let private recursionDepth: Probe =
    { Id = "premiseRecursionDepth"
      Kind = "recursion-depth"
      File = "recursion-depth.json"
      Row = "host-recursion-depth"
      Description =
        "The stack premise. The parser descends once per nesting level and refuses an object or array opened past 512 levels by name, before it descends: a document nested 512 deep is read, one nested 513 deep or more is `refused` with `LimitExceeded/depth`, however deep it goes. Each vector gives `input`: the text is `open` repeated `depth` times, then `inner`, then `close` repeated `depth` times. An accepted document's answer is `accepted`, the SHA-256 (lower-case hex) of its canonical rendering, which is the text itself. A host reproduces every answer on the SMALLEST stack it supports, so the 512-deep documents are read without exhausting it and the deeper ones are refused rather than overflowing it."
      Vectors = fun () -> depthCases |> List.map depthVector }

// ---------------------------------------------------------------------------
//  The family
// ---------------------------------------------------------------------------

/// Every probe, in ladder order.
let probes: Probe list = [ stringModel; floatDigits; int53; sha256; recursionDepth ]

/// The family ids a `host-premise` row's `probe` may name.
let familyIds: string list = probes |> List.map _.Id

let private probePath (dir: string) (p: Probe) =
    Path.Combine(dir, familyDirName, p.File)

let manifestPath (dir: string) =
    Path.Combine(dir, familyDirName, manifestFileName)

/// One probe's file: a header, then one canonical line per vector.
let render (p: Probe) : string =
    CorpusFile.render
        { Head =
            [ "family", CorpusFile.str p.Id
              "premise", CorpusFile.str p.Row
              "description", CorpusFile.str p.Description ]
          Items = "vectors"
          Rows = p.Vectors() |> List.map CorpusCell.Value }

/// The family index as the one manifest schema (`CorpusManifest`).
let internal manifest () : CorpusManifest =
    { Version = 1
      Description =
        "Host-premise probes. SELF-ENUMERATED, as the decode/ and refusals/ families are. Each family is the probe a `host-premise` row of the claims ladder (proofs.json) names: a host that reproduces every vector of a family has discharged that premise on its own runtime. `adoption` is per host and is DATA: a host whose entry reads `proposed` reports the premise BY NAME as not yet answered. docs/host-premises.md states each premise in plain language. This manifest is authoritative for the vector counts."
      Families =
        [ for p in probes ->
              { Id = p.Id
                Kind = p.Kind
                File = p.File
                Vectors = List.length (p.Vectors())
                Adoption =
                  (RefusalCorpus.hostRoster |> List.map (fun h -> h, HostAdoption.Proposed))
                  @ [ SpineAdoption.adoption p.Id ]
                Description = sprintf "The probe of the `%s` row." p.Row } ] }

let renderManifest () : string =
    CorpusFile.render (CorpusFile.manifest (manifest ()))

/// Every file the family writes under `dir`, with its text: one per probe, then the manifest.
let files (dir: string) : (string * string) list =
    (probes |> List.map (fun p -> probePath dir p, render p))
    @ [ manifestPath dir, renderManifest () ]
