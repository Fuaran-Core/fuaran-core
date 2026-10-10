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
    match Column.aggregate fn (Column.ofFloats "f" (Vector.ofList xs) AllValid) with
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
      Decode = fun s -> Decode.parse s |> Result.bind (Decoder.describing Decoder.int) }

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
    let col = Column.ofFloats "f" (Vector.ofList [ 3.0; nan; -1.0; -0.0; nan ]) AllValid

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

// ---- Phase 276 — the exact decimal (D72) ----
// `DecimalText` is string arithmetic written to the Fable-clean subset, and these rows are what make
// that a measured claim rather than an asserted one: every refused form K4 names and every
// normalisation K3 performs, the order and the sum at each of their branches, the column codec over a
// decimal column, and `Column.aggregate` over one. Each answer is ASCII by construction: decimal text
// is ASCII, and a refusal is its class name.

/// `Space.canonical` over the whole `int` range (Phase 307): the one spelling an integer argument
/// is handed on as, or `refused`. The reader is the seam's invariant one, so a minus sign is `-`
/// and nothing else, under every culture and on both pipelines.
let private intCanon (s: string) : string =
    match Space.canonical (IntRange(System.Int32.MinValue, System.Int32.MaxValue)) s with
    | Some c -> c
    | None -> "refused"

/// `DecimalText.tryCanonical`'s answer: the canonical text, or `refused`.
let private decCanon (s: string) : string =
    match DecimalText.tryCanonical s with
    | Some c -> c
    | None -> "refused"

/// `DecimalText.compare`'s answer: `-1` / `0` / `1`, or `refused`.
let private decCompare (a: string) (b: string) : string =
    match DecimalText.compare a b with
    | Some c -> string c
    | None -> "refused"

/// `DecimalText.add`'s answer: the canonical sum, or `refused`.
let private decAdd (a: string) (b: string) : string =
    match DecimalText.add a b with
    | Some s -> s
    | None -> "refused"

/// `DecimalText.tryToFloat`'s answer through the canonical float layout, or `refused`.
let private decToFloat (s: string) : string =
    match DecimalText.tryToFloat s with
    | Some f -> Canon.canonicalFloat f
    | None -> "refused"

/// A codec refusal's class. Exhaustive, so a case added to `ColumnError` is a build error here rather
/// than a row that reads `<other>`.
let private columnErrorClass (e: ColumnError) : string =
    match e with
    | NotJson _ -> "NotJson"
    | MissingField _ -> "MissingField"
    | MalformedShape _ -> "MalformedShape"
    | UnknownType _ -> "UnknownType"
    | TypeMismatch _ -> "TypeMismatch"
    | LengthMismatch _ -> "LengthMismatch"
    | NonFiniteFloat _ -> "NonFiniteFloat"
    | Malformed _ -> "Malformed"
    | RaggedColumns _ -> "RaggedColumns"

/// `ColumnCodec.tryEncode`'s answer over a one-column decimal source: `ok:<canonical bytes>`, or
/// `refused:<class>`. A cell outside the decimal type is refused by `Column.ofCells` since Phase
/// 417, with the `TypeMismatch` `Table.validate` named for it before — the same class, so the row's
/// bytes are the ones the `Cell list` column produced.
let private decimalEncode (cells: Cell list) : string =
    let encoded =
        Column.ofCells "c" DecimalType cells
        |> Result.bind (fun col ->
            ColumnCodec.tryEncode (
                Embedded
                    { Schema = [ Field.create "c" DecimalType ]
                      Columns = [ col ] }
            ))

    match encoded with
    | Ok text -> "ok:" + text
    | Error e -> "refused:" + columnErrorClass e

/// `Json.parse` of one bare token: `i:` or `f:` and the value re-rendered, or `refused:<message>`.
let private jsonParse (token: string) : string =
    match Json.parse token with
    | Ok(JInt _ as v) -> "i:" + Json.render v
    | Ok(JFloat _ as v) -> "f:" + Json.render v
    | Ok v -> "other:" + Json.render v
    | Error e -> "refused:" + e

/// `ColumnCodec.decode` of a one-column decimal document whose `values` array is `values` (raw JSON),
/// re-encoded: `ok:<canonical bytes>`, or `refused:<class>`.
let private decimalDecode (values: string) : string =
    let validity =
        // One `true` per top-level value; the rows below never nest a comma inside a value.
        values.Split ',' |> Array.map (fun _ -> "true") |> String.concat ","

    let doc =
        "{\"schema\":[{\"name\":\"c\",\"type\":\"decimal\"}],\"columns\":{\"c\":{\"values\":["
        + values
        + "],\"validity\":["
        + validity
        + "]}}}"

    match ColumnCodec.decode doc with
    | Ok src -> "ok:" + ColumnCodec.encode src
    | Error e -> "refused:" + columnErrorClass e

/// `Column.aggregate` over a decimal column, as the result cell's token or the refusal's class. A
/// cell of another type is `<outside-type>` whether the aggregate's admission named it (before
/// Phase 417) or `Column.ofCells` refuses it at construction (since): one class, one row.
let private decimalAggregate (fn: AggFn) (cells: Cell list) : string =
    match Column.ofCells "d" DecimalType cells with
    | Error _ -> "<outside-type>"
    | Ok col ->
        match Column.aggregate fn col with
        | Ok cell -> Cell.token cell
        | Error(IncompatibleAggType _) -> "<incompatible>"
        | Error(AggregateOverflow _) -> "<overflow>"
        | Error(CellOutsideType _) -> "<outside-type>"

/// The decimal column the aggregate rows read: two spellings of one value (`1.50` built directly,
/// `1.5` canonical), a negative, an `Int` (the lossless promotion `widens` pins), and a `Null`.
let private decimalColumnCells: Cell list =
    [ Decimal "0.1"
      Decimal "0.2"
      Null
      Decimal "-0.3"
      Decimal "1.50"
      Decimal "1.5"
      Int 2 ]

/// A decimal of 400 digits: exact for `Sum`, past the float range for the float-valued aggregates.
let private pastFloat: string = "1" + String.replicate 399 "0"

/// `agrees:<n>` when `f` and `g` give one value on every corpus input, else `diverges@<index>`.
let private agreement (f: string -> string) (g: string -> string) : string =
    match exportCorpus |> List.tryFindIndex (fun s -> f s <> g s) with
    | None -> "agrees:" + string (List.length exportCorpus)
    | Some i -> "diverges@" + string i

// ---- Phase 387 — the IDL sampler (D124) ----
// `Fuaran.Core.Idl.Sample` claims "same seed, same vectors, on any host and any runtime", and the
// package is Fable-shipped. These rows are what make that a measured claim: the draws a sampler
// makes are pinned through a vocabulary whose every choice is visible in its output, and the sampled
// node sets of a vocabulary reaching every slot shape are pinned through their canonical encoding.

/// One field of a sampler vocabulary.
let private sField (name: string) (t: Fuaran.Core.Idl.IdlType) (opt: Fuaran.Core.Idl.Optionality) =
    ({ Name = name
       Type = t
       Opt = opt
       Annotations = Fuaran.Core.Idl.Annotations.Empty }
    : Fuaran.Core.Idl.IdlField)

/// One kind of a sampler vocabulary.
let private sKind (tag: string) (fields: Fuaran.Core.Idl.IdlField list) =
    ({ Tag = tag
       Category = "content"
       Fields = fields
       Annotations = Fuaran.Core.Idl.Annotations.Empty }
    : Fuaran.Core.Idl.IdlKind)

/// A vocabulary declaring only kinds and enums.
let private sVocab (kinds: Fuaran.Core.Idl.IdlKind list) (enums: Fuaran.Core.Idl.IdlEnum list) =
    ({ Kinds = kinds
       Unions = []
       Enums = enums
       Records = []
       Defaults = []
       NodeFields = []
       Ops = []
       Wire = Fuaran.Core.Idl.WireShape.Default
       Harden = Fuaran.Core.Idl.HardenPolicy.Undeclared }
    : Fuaran.Core.Idl.Idl)

/// The DRAW vocabulary: one kind whose one field is a seven-case enum, and no envelope. A sampled
/// node is exactly two choices — its id from the sampler's four-id pool, then the enum case — so the
/// encoded nodes spell the draws out, and seven (not a power of two) is a size where a draw by
/// rejection and a draw by modulo choose differently.
let private drawVocab: Fuaran.Core.Idl.Idl =
    sVocab
        [ sKind "K" [ sField "e" (Fuaran.Core.Idl.TEnum "Seven") Fuaran.Core.Idl.Required ] ]
        [ Fuaran.Core.Idl.Declare.enumOf "Seven" [ "c0"; "c1"; "c2"; "c3"; "c4"; "c5"; "c6" ] ]

/// A vocabulary whose one kind requires an enum that declares no case — the sampler's typed refusal.
let private emptyEnumVocab: Fuaran.Core.Idl.Idl =
    sVocab
        [ sKind "K" [ sField "e" (Fuaran.Core.Idl.TEnum "Nothing") Fuaran.Core.Idl.Required ] ]
        [ Fuaran.Core.Idl.Declare.enumOf "Nothing" [] ]

/// The SHAPE vocabulary: every slot shape the sampler draws differently — scalars, an enum, a list
/// of nodes (recursion and the depth floor), a map of JSON, a record with an optional field, a
/// generic union applied to an argument, a hosted slot with a declared format, both presence rules,
/// and a node envelope.
let private shapeVocab: Fuaran.Core.Idl.Idl =
    let opt = Fuaran.Core.Idl.Optional
    let req = Fuaran.Core.Idl.Required

    { sVocab
          [ sKind
                "Leaf"
                [ sField "text" Fuaran.Core.Idl.TStr req
                  sField "n" Fuaran.Core.Idl.TInt req
                  sField "ratio" Fuaran.Core.Idl.TFloat req
                  sField "on" Fuaran.Core.Idl.TBool (Fuaran.Core.Idl.OmitDefault(Fuaran.Core.Idl.VBool false))
                  sField "tone" (Fuaran.Core.Idl.TEnum "Tone") req ]
            sKind
                "Panel"
                [ sField "children" (Fuaran.Core.Idl.TList Fuaran.Core.Idl.TNode) req
                  sField "meta" (Fuaran.Core.Idl.TMap Fuaran.Core.Idl.TJson) opt
                  sField "at" (Fuaran.Core.Idl.TRecord "Point") req
                  sField "held" (Fuaran.Core.Idl.TUnion("Box", [ Fuaran.Core.Idl.TFloat ])) req
                  sField
                      "when"
                      (Fuaran.Core.Idl.THosted
                          { FSharp = "System.DateOnly"
                            Encode = "encDate"
                            Decode = "decDate"
                            Wire = Some Fuaran.Core.Idl.TStr
                            Format = Some Fuaran.Core.Idl.HostedFormat.Date })
                      opt ] ]
          [ Fuaran.Core.Idl.Declare.enumOf "Tone" [ "Low"; "Mid"; "High" ] ] with
        Records =
            [ { Name = "Point"
                Fields =
                  [ sField "x" Fuaran.Core.Idl.TFloat req
                    sField "label" Fuaran.Core.Idl.TStr opt ] } ]
        Unions =
            [ { Name = "Box"
                Params = [ "T" ]
                Cases =
                  [ { Tag = "Empty"
                      Fields = []
                      Annotations = Fuaran.Core.Idl.Annotations.Empty }
                    { Tag = "Full"
                      Fields = [ sField "value" (Fuaran.Core.Idl.TVar "T") req ]
                      Annotations = Fuaran.Core.Idl.Annotations.Empty } ] } ]
        NodeFields = [ sField "hint" Fuaran.Core.Idl.TStr opt ] }

/// `Sample.trySampleNodes` then each node's canonical encoding — or the refusal, as one line.
let private sampledLines (idl: Fuaran.Core.Idl.Idl) (seed: int) (count: int) : string list =
    match Fuaran.Core.Idl.Sample.trySampleNodes idl (idl.Kinds |> List.map _.Tag) seed count with
    | Error refusal -> [ "refused:" + refusal.Describe ]
    | Ok nodes ->
        nodes
        |> List.map (fun v ->
            match Fuaran.Core.Idl.Encode.encode idl v with
            | Ok text -> text
            | Error _ -> "<unencodable>")

/// The draw vocabulary's eight nodes from `seed`, `|`-joined: ASCII by construction (the id pool
/// and the enum cases are), so the draws are readable in a divergence report.
let private sampleDraws (seed: int) : string =
    sampledLines drawVocab seed 8 |> String.concat "|"

/// The shape vocabulary's twelve nodes from `seed`, as the SHA-256 of their encodings: the string
/// pools are adversarial and not ASCII, so the set leaves as a digest.
let private sampleNodes (seed: int) : string =
    sampledLines shapeVocab seed 12 |> String.concat "\n" |> Hash.sha256Hex

/// The named table: `(label, value)` pairs, each value computed by calling a public surface
/// and ASCII by construction. Only the first part of what `lines` emits — the hash and
/// sanitiser sweeps follow it there — and its order is part of the comparison.
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
      // Phase 373 — the edges of the band where Fable writes JS's own `toString` with no re-lay,
      // [1e-4, 1e17): the adjacent doubles each side of each edge, one negative, and a 17-digit value
      // inside. Below 1e-4 and from 1e17 up the two layouts differ, so a band edge moved by one double
      // either way changes one of these rows under node.
      "floatLayout/band-below-1e-4", FloatLayout.finite 9.999999999999999e-5
      "floatLayout/band-at-1e-4", FloatLayout.finite 1e-4
      "floatLayout/band-above-1e-4", FloatLayout.finite 0.00010000000000000002
      "floatLayout/band-neg-at-1e-4", FloatLayout.finite -1e-4
      "floatLayout/band-neg-below-1e-4", FloatLayout.finite -9.999999999999999e-5
      "floatLayout/band-17-digits", FloatLayout.finite 0.00012345678901234567
      "floatLayout/band-below-1e17", FloatLayout.finite 99999999999999984.0
      "floatLayout/band-at-1e17", FloatLayout.finite 1e17
      "floatLayout/band-above-1e17", FloatLayout.finite 100000000000000016.0
      "floatLayout/band-neg-below-1e17", FloatLayout.finite -99999999999999984.0
      // Phase 367 / 373 — the parser's canonical-integer check (Phase 253) at both edges of the band
      // it asks it in, [2^53, 1e17): an integer token is read there exactly when it is the float
      // layout of the double it reads as. Below 2^53 the int53 guard admits it without the check; at
      // 1e17 the layout is `1E+17`, so the integer spelling is refused and the exponent one is read.
      "jsonParse/2-53-minus-1", jsonParse "9007199254740991"
      "jsonParse/2-53", jsonParse "9007199254740992"
      "jsonParse/2-53-plus-1", jsonParse "9007199254740993"
      "jsonParse/2-53-plus-2", jsonParse "9007199254740994"
      "jsonParse/neg-2-53-plus-2", jsonParse "-9007199254740994"
      "jsonParse/below-1e17", jsonParse "99999999999999980"
      "jsonParse/below-1e17-exact-digits", jsonParse "99999999999999984"
      "jsonParse/1e17-integer", jsonParse "100000000000000000"
      "jsonParse/1e17-exponent", jsonParse "1E+17"
      "jsonParse/above-1e17-integer", jsonParse "100000000000000020"
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
      |> String.concat " "

      // ---- Phase 276 — the exact decimal (D72). Appended, so every earlier row keeps its place in
      // the comparison. ----
      // `DecimalText.tryCanonical` — K4's refused forms, one row each, so a divergence names the form.
      "decimal/canonical/refused-empty", decCanon ""
      "decimal/canonical/refused-null", decCanon null
      "decimal/canonical/refused-lone-minus", decCanon "-"
      "decimal/canonical/refused-leading-plus", decCanon "+1"
      "decimal/canonical/refused-double-minus", decCanon "--1"
      "decimal/canonical/refused-bare-point-leading", decCanon ".5"
      "decimal/canonical/refused-bare-point-trailing", decCanon "5."
      "decimal/canonical/refused-minus-bare-point", decCanon "-.5"
      "decimal/canonical/refused-point-alone", decCanon "."
      "decimal/canonical/refused-two-points", decCanon "1.2.3"
      "decimal/canonical/refused-exponent", decCanon "1e3"
      "decimal/canonical/refused-exponent-upper", decCanon "1.5E-2"
      "decimal/canonical/refused-separator-comma", decCanon "1,000"
      "decimal/canonical/refused-separator-underscore", decCanon "1_000"
      "decimal/canonical/refused-decimal-comma", decCanon "1,5"
      "decimal/canonical/refused-leading-space", decCanon " 1"
      "decimal/canonical/refused-trailing-space", decCanon "1 "
      "decimal/canonical/refused-tab", decCanon "1\t"
      "decimal/canonical/refused-hex", decCanon "0x10"
      "decimal/canonical/refused-nan", decCanon "NaN"
      "decimal/canonical/refused-infinity", decCanon "Infinity"
      // A digit outside ASCII: a reader that asked "is this a digit?" of the platform would take it.
      "decimal/canonical/refused-arabic-indic-digit", decCanon "١"
      "decimal/canonical/refused-fullwidth-digit", decCanon "１"
      // K3's normalisations: leading zeros, trailing fraction zeros, a zero fraction, a signed zero.
      "decimal/canonical/already-canonical", decCanon "12.5"
      "decimal/canonical/zero", decCanon "0"
      "decimal/canonical/leading-zeros", decCanon "007"
      "decimal/canonical/trailing-zeros", decCanon "12.50"
      "decimal/canonical/zero-fraction", decCanon "3.000"
      "decimal/canonical/leading-zero-fraction", decCanon "0.500"
      "decimal/canonical/negative-zero", decCanon "-0"
      "decimal/canonical/negative-zero-fraction", decCanon "-0.000"
      "decimal/canonical/zeros-both-sides", decCanon "00.00"
      "decimal/canonical/negative-both-sides", decCanon "-012.340"
      "decimal/canonical/thirty-one-places", decCanon "0.0000000000000000000000000000001"
      "decimal/canonical/forty-digits", decCanon "1234567890123456789012345678901234567890.50"
      // `DecimalText.compare` — sign, place, equal spellings, and values one float cannot tell apart.
      "decimal/compare/negative-below-positive", decCompare "-1" "1"
      "decimal/compare/positive-above-negative", decCompare "1" "-1"
      "decimal/compare/signed-zeros-equal", decCompare "-0" "0.0"
      "decimal/compare/negatives-reversed", decCompare "-2" "-10"
      "decimal/compare/integer-width", decCompare "10" "9"
      "decimal/compare/fraction-place", decCompare "0.1" "0.09"
      "decimal/compare/negative-fraction-place", decCompare "-0.1" "-0.09"
      "decimal/compare/whole-against-fraction", decCompare "100" "99.999"
      "decimal/compare/equal-spellings", decCompare "1.50" "001.5"
      "decimal/compare/equal-spellings-negative", decCompare "-7.000" "-7"
      "decimal/compare/one-float-tenth", decCompare "0.1" "0.1000000000000000055511151231257827"
      "decimal/compare/one-float-past-2-53", decCompare "9007199254740993" "9007199254740992"
      "decimal/compare/refused-left", decCompare "1e3" "1"
      "decimal/compare/refused-right", decCompare "1" "+1"
      // `DecimalText.add` — every carry, borrow and sign branch, and scales no host decimal holds.
      "decimal/add/carry-through-point", decAdd "0.99" "0.01"
      "decimal/add/carry-into-tens", decAdd "9.95" "0.05"
      "decimal/add/widening-carry", decAdd "999" "1"
      "decimal/add/widening-carry-fraction", decAdd "99.9" "0.1"
      "decimal/add/narrowing-borrow", decAdd "1000" "-0.001"
      "decimal/add/borrow-to-fraction", decAdd "100" "-99.99"
      "decimal/add/cancel-to-unsigned-zero", decAdd "1.5" "-1.50"
      "decimal/add/cancel-negative-first", decAdd "-0.001" "0.001"
      "decimal/add/mixed-negative-larger-first", decAdd "-5" "3"
      "decimal/add/mixed-negative-larger-second", decAdd "3" "-5"
      "decimal/add/mixed-positive-larger-first", decAdd "5" "-3"
      "decimal/add/mixed-positive-larger-second", decAdd "-3" "5"
      "decimal/add/both-negative", decAdd "-1.5" "-2.75"
      "decimal/add/zero-identity", decAdd "-0" "12.50"
      "decimal/add/scale-past-host-decimal", decAdd "0.0000000000000000000000000000001" "1"
      "decimal/add/magnitude-past-host-decimal", decAdd "99999999999999999999999999999999999999" "1"
      "decimal/add/refused", decAdd "1" ".5"
      // `DecimalText.tryToFloat` — the one place the type rounds, and where it refuses to.
      "decimal/toFloat/tenth", decToFloat "0.10"
      "decimal/toFloat/past-2-53", decToFloat "9007199254740993"
      "decimal/toFloat/negative", decToFloat "-12.5"
      "decimal/toFloat/past-float-range", decToFloat pastFloat
      "decimal/toFloat/refused", decToFloat "1e3"
      // `ColumnCodec` over a decimal column: the canonical encode (an `Int` written as a decimal
      // string, a `Null` as the absent slot), the refusal of a non-canonical cell, a decode that
      // canonicalises, the integer token, and the refusals of a fractional token and of text.
      "decimalCodec/encode-canonical", decimalEncode [ Decimal "12.5"; Null; Decimal "-0.001"; Int 7 ]
      "decimalCodec/encode-refuses-non-canonical", decimalEncode [ Decimal "12.50" ]
      "decimalCodec/decode-canonicalises", decimalDecode "\"012.50\",\"-0\",\"3.000\""
      "decimalCodec/decode-integer-token", decimalDecode "42,-7,0"
      "decimalCodec/decode-integer-token-past-int32", decimalDecode "3000000000,-9007199254740992"
      "decimalCodec/decode-whole-exponent-token", decimalDecode "3e9"
      "decimalCodec/decode-refuses-fractional-token", decimalDecode "3.5"
      // Past 2^53 a whole-valued number token is refused by one of two hands: an integer token that
      // is not a canonical float layout by the parser's int53 guard, before the codec sees it, and a
      // token the parser reads as a double by the codec. Since Phase 253 the first row is the second
      // kind: `9007199254740994` is the canonical layout of a double, so the parser reads it.
      "decimalCodec/decode-refuses-integer-token-past-2-53", decimalDecode "9007199254740994"
      "decimalCodec/decode-refuses-whole-float-past-2-53", decimalDecode "1e300"
      "decimalCodec/decode-refuses-exponent-text", decimalDecode "\"1e3\""
      "decimalCodec/decode-refuses-plus-text", decimalDecode "\"+1\""
      "decimalCodec/decode-refuses-bool", decimalDecode "true"
      // `Column.aggregate` over a decimal column: the exact `Sum`, the exact order of `Min` / `Max`
      // (the winning cell as it stands), the float-valued three, `CountDistinct` over two spellings
      // of one value, and the refusals: a value past the float range, and text that is not decimal.
      "decimalAggregate/sum", decimalAggregate Sum decimalColumnCells
      "decimalAggregate/min", decimalAggregate Min decimalColumnCells
      "decimalAggregate/max", decimalAggregate Max decimalColumnCells
      "decimalAggregate/mean", decimalAggregate Mean decimalColumnCells
      "decimalAggregate/median", decimalAggregate Median decimalColumnCells
      "decimalAggregate/stddev", decimalAggregate StdDev decimalColumnCells
      "decimalAggregate/count-distinct", decimalAggregate CountDistinct decimalColumnCells
      "decimalAggregate/sum-tenths-exact", decimalAggregate Sum (List.replicate 10 (Decimal "0.1"))
      "decimalAggregate/sum-past-float", Hash.sha256Hex (decimalAggregate Sum [ Decimal pastFloat; Decimal "0.5" ])
      "decimalAggregate/mean-past-float", decimalAggregate Mean [ Decimal pastFloat; Decimal "0.5" ]
      "decimalAggregate/sum-not-decimal", decimalAggregate Sum [ Decimal "1"; Decimal "1e3" ]

      // ---- Phase 307 — the integer reader at the invocable seams (`Space.canonical`). Appended, so
      // every earlier row keeps its place. The five spellings a culture-dependent reader got wrong:
      // `-5` is read everywhere, and U+2212, a leading space, a leading `+` and an exponent nowhere.
      "space/int/ascii-minus", intCanon "-5"
      "space/int/unicode-minus", intCanon "\u22125"
      "space/int/leading-space", intCanon " 5"
      "space/int/leading-plus", intCanon "+5"
      "space/int/exponent", intCanon "1e999"
      "space/int/leading-zero", intCanon "05"

      // ---- Phase 387 — the IDL sampler (D124). Appended, so every earlier row keeps its place.
      // Until 0.36.0 the sampler was a uint64 LCG — a 64-bit multiply, the shape `ConfRng` was
      // re-based off at 0.20.0 because Fable cannot carry it — choosing by `% n`, and no row here
      // measured it. It is `ConfRng`'s xorshift32 with draws by rejection now, and these rows are
      // the cross-pipeline half of that claim: `draws/*` spell out each choice, `nodes/*` pin a
      // sampled set over every slot shape, and `refusal/*` the typed refusal's text.
      "sample/draws/seed-0", sampleDraws 0
      "sample/draws/seed-1", sampleDraws 1
      "sample/draws/seed-neg-1", sampleDraws -1
      "sample/draws/seed-1488", sampleDraws 1488
      "sample/nodes/seed-0", sampleNodes 0
      "sample/nodes/seed-1488", sampleNodes 1488
      "sample/refusal/empty-enum", sampledLines emptyEnumVocab 0 1 |> String.concat "|" ]

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
    [ Field.create (if s = "" then "c" else s) IntType
      Field.create ("n" + s) FloatType ]

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

// ---- Phase 291 — the IDL's sanitisation floor, under both pipelines ----
//
// The floor's defect lived where only the Fable pipeline could show it: a scan took its index from a
// lowered COPY of the string, and U+0130 lowers to one unit in .NET's `ToLowerInvariant` and to two
// in JavaScript's `toLowerCase`, so under Fable the index landed in the wrong place (a long run plus a
// short tail looped or threw). Phase 291 fixed it with a per-unit fold on the string it indexes, and
// its suite states every expected output as a literal — but that suite runs on .NET alone.
//
// Each row here carries its EXPECTED output beside its input and prints `ok` when the floor produced
// exactly it, else `diverges:` and the UTF-8 hex of what it produced. So the printed line is ASCII
// whatever the input holds, the .NET half is asserted in this repository (every row reads `ok`), and
// the transpiled half is byte-compared against it by the downstream runner. The cases are 291's own:
// U+0130 runs before each scheme (bare and inside an `href`), case folding on the original string,
// lone surrogates, the handler and element scans after a run, and the URL floor's clause table.

let private dotI = string (char 0x0130)

/// `ok` when `actual` is `expected`, else the bytes actually produced.
let private exactly (expected: string) (actual: string) : string =
    if actual = expected then
        "ok"
    else
        "diverges:" + hexOf (Hash.utf8Bytes actual)

let private scrubCases: (string * string * string) list =
    [ for scheme in [ "javascript"; "vbscript" ] do
          for n in [ 1; 3; 11; 12 ] do
              let run = String.replicate n dotI

              for tailName, tail in [ "bare", ""; "tail", "alert(1)" ] do
                  yield
                      sprintf "%s-after-%02d-dotI-%s" scheme n tailName,
                      run + scheme + ":" + tail,
                      run + "about:blank" + tail

                  yield
                      sprintf "%s-after-%02d-dotI-%s-href" scheme n tailName,
                      "<a href=\"" + run + scheme + ":" + tail + "\">x</a>",
                      "<a href=\"" + run + "about:blank" + tail + "\">x</a>" ]
    @ [ "fold-upper", "JAVASCRIPT:x", "about:blankx"
        "fold-mixed", "JaVaScRiPt:x", "about:blankx"
        "fold-vbscript-mixed", "VbScRiPt:x", "about:blankx"
        "fold-several", "a javascript:1 b JAVASCRIPT:2 c", "a about:blank1 b about:blank2 c" ]
    @ [ for code in [ 0xD800; 0xDC00; 0xDBFF ] do
            let lone = units [ code ]

            for scheme in [ "javascript"; "vbscript" ] do
                yield sprintf "%s-after-lone-%04X" scheme code, lone + scheme + ":x", lone + "about:blankx"

                yield
                    sprintf "%s-after-lone-%04X-doubled" scheme code,
                    lone + lone + dotI + scheme + ":" + lone,
                    lone + lone + dotI + "about:blank" + lone ]
    @ [ for n in [ 1; 3; 12 ] do
            let run = String.replicate n dotI

            yield
                sprintf "handler-after-%02d-dotI" n,
                "<a title=\"" + run + "\" onclick=\"alert(1)\">x</a>",
                "<a title=\"" + run + "\">x</a>"

            yield sprintf "element-after-%02d-dotI" n, run + "<SCRIPT>alert(1)</SCRIPT>after", run + "after"

            yield
                sprintf "mixed-after-%02d-dotI" n,
                "<a title=\"" + run + "\" ONCLICK=\"x\" href=\"" + run + "JAVASCRIPT:y\">z</a>",
                "<a title=\"" + run + "\" href=\"" + run + "about:blanky\">z</a>" ]

/// The URL floor's clauses (Phase 291, held to the UI floor): input and the sanitised URL, or `None`
/// for a refusal.
let private urlCases: (string * string * string option) list =
    [ "pair-slash-slash", "//evil.example/x", None
      "pair-slash-backslash", "/\\evil.example/x", None
      "pair-backslash-backslash", "\\\\evil.example/x", None
      "pair-backslash-slash", "\\/evil.example/x", None
      "tab-between-slashes", "/\t/evil.example", None
      "lf-between-slashes", "/\n/evil.example", None
      "cr-between-slashes", "/\r/evil.example", None
      "leading-control-before-pair", "\u0001//evil.example", None
      "leading-nul-before-pair", "\u0000//evil.example", None
      "leading-space-tab-before-pair", " \t//evil.example", None
      "mixed-pair-behind-controls", "\u001F\\\t/evil.example", None
      "trailing-controls-removed", "//evil.example\u0001\u0002", None
      "single-leading-backslash", "\\evil.example", Some "\\evil.example"
      "absolute-path", "/about", Some "/about"
      "relative-path", "about/us", Some "about/us"
      "interior-vt-kept", "/\u000B/host/x", Some "/\u000B/host/x"
      "interior-ff-kept", "/\u000C/host/x", Some "/\u000C/host/x"
      "interior-tab-removed", "/a\tb", Some "/ab"
      "edge-controls-removed", "\u0001 /about \u0002", Some "/about"
      "empty", "", Some ""
      "only-controls", "\u0001\u0002 \t", Some ""
      "javascript-behind-tab", "java\tscript:alert(1)", None
      "javascript-behind-control", "\u0001javascript:alert(1)", None
      "vbscript", "vbscript:x", None
      "file", "file:///etc/passwd", None
      "unknown-scheme", "data:text/html,x", None
      "https", "https://example.com/a?b#c", Some "https://example.com/a?b#c"
      "mailto", "mailto:a@b.example", Some "mailto:a@b.example"
      "https-behind-control", "\u0001https://example.com", Some "https://example.com" ]

let private renderUrl (r: string option) : string =
    match r with
    | None -> "refused"
    | Some s -> "kept:" + s

/// The sanitiser sweep: `idlSanitize/scrub/<case>` and `idlSanitize/url/<case>`, each `ok` when the
/// floor produced exactly the committed output on this pipeline.
let sanitiseSweep: (string * string) list =
    (scrubCases
     |> List.map (fun (label, input, expected) ->
         "idlSanitize/scrub/" + label, exactly expected (Fuaran.Core.Idl.Sanitize.scrubMarkdown input)))
    @ (urlCases
       |> List.map (fun (label, input, expected) ->
           "idlSanitize/url/" + label,
           exactly (renderUrl expected) (renderUrl (Fuaran.Core.Idl.Sanitize.sanitizeUrl input))))

/// `lines`, over rows the caller names — the rendering `lawsWith`'s format law holds.
let private linesOf (rows: (string * string) list) : string list =
    rows |> List.map (fun (k, v) -> sprintf "VEC %s %s" k v)

/// Every vector as the line a runner compares: `VEC <label> <value>` — the named table first, then
/// the hash sweep, then the sanitiser sweep. Order is part of the comparison. The `VEC ` prefix is
/// what lets a runner filter out anything a runtime writes around the program; a label never
/// contains a space, so the line splits on its first one.
let lines () : string list =
    linesOf (vectors @ hashSweep @ sanitiseSweep)

/// The table as `LawResult`s over rows the caller hands it (Phase 390; `laws ()` is this over the
/// named table and both sweeps, in `lines` order). The VALUES are pinned elsewhere — against
/// committed bytes by this repository's suite, and across pipelines by a consumer's diff of `lines` —
/// so these laws state what makes a row comparable at all, on whichever pipeline runs them:
///   - every label and value is printable ASCII (the module's own promise: a console-encoding
///     difference must never read as a value divergence);
///   - every label is non-empty, carries no space and is unique, so a `VEC` line splits on its first
///     space and names one row;
///   - every sanitiser-sweep row (`idlSanitize/…`) reads `ok` — the floor produced exactly the
///     committed output on THIS pipeline;
///   - `lines` renders each row as `VEC <label> <value>`, in row order;
///   - the corpus law, whose evidence is one assertion per row evaluated, so a run handed no rows is
///     red by name (`parity vectors: the corpus evaluated at least one vector`).
let lawsWith (rows: (string * string) list) : LawResult list =
    let corpus = VectorKit.corpusCell "parity vectors"

    let ascii =
        LawKit.LawCell "parity vectors: every label and value is printable ASCII"

    let labels =
        LawKit.LawCell "parity vectors: every label is non-empty, carries no space, and names one row"

    let sanitiser =
        LawKit.LawCell "parity vectors: every sanitiser-sweep row reads ok on this pipeline"

    let format =
        LawKit.LawCell "parity vectors: lines renders each row as VEC <label> <value>, in row order"

    let printable (s: string) =
        s |> Seq.forall (fun ch -> ch >= ' ' && ch <= '~')

    let mutable seen = Set.empty

    for label, value in rows do
        corpus.Saw()

        ascii.Check(
            printable label && printable value,
            fun () -> sprintf "%s emits a character outside printable ASCII" label
        )

        labels.Check(
            label <> "" && not (label.Contains " ") && not (Set.contains label seen),
            fun () -> sprintf "the label %A is empty, carries a space, or repeats" label
        )

        seen <- Set.add label seen

        sanitiser.Check(
            not (label.StartsWith "idlSanitize/") || value = "ok",
            fun () -> sprintf "%s reads %s" label value
        )

    let rendered = linesOf rows

    format.Check(
        rendered.Length = rows.Length
        && List.forall2 (fun (l: string) (k: string, v: string) -> l = "VEC " + k + " " + v) rendered rows,
        fun () ->
            sprintf "lines rendered %d lines for %d rows, or a line not of the VEC shape" rendered.Length rows.Length
    )

    LawKit.results [ ascii; labels; sanitiser; format; corpus ]

/// The whole table as `LawResult`s — `lawsWith` over the named table, the hash sweep and the
/// sanitiser sweep, the rows `lines ()` renders. A fixed table: every run evaluates every row.
let laws () : LawResult list =
    lawsWith (vectors @ hashSweep @ sanitiseSweep)
