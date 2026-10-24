module Fuaran.Core.Tests.ProofOracle.JsonParseDiff

// ---------------------------------------------------------------------------
//  The PARSER differential (Phase 146) — `JsonParse.parse` beside `Json.read` at a cap and no
//  other bound (`JsonLimits.depthOnly`; until Phase 519 the unlimited `parseDetailedWithPolicy`,
//  which every reader now runs through the limited scanner instead). The model's own theorem
//  `limited_at_no_bound_is_the_parser` is the relation between the two: at no bound but the cap they
//  answer the same, the cap's `MaxDepthExceeded` lifted to the `Depth` breach at the same position —
//  so a breach and the model's cap refusal render as ONE line, and every other answer as before.
//
//  Everything above this line begins at a `JVal` that already exists, which is exactly the
//  boundary the `WireDecode` model (Phase 135) drew. What runs here is the model of the recursive-descent
//  parser itself, over the same TEXT production reads: the corpus fixtures as raw bytes, a
//  near-miss table that reaches every classified failure, and generated deep, wide and random
//  inputs.
//
//  Each probe renders both answers into ONE string carrying the outcome class, the decoded value,
//  and — on a failure — the KIND, the POSITION and the MESSAGE. A model agreeing on accept-vs-
//  refuse alone would not notice a parser that classified the wrong thing or pointed at the wrong
//  character, and the position is the half most likely to drift silently.
// ---------------------------------------------------------------------------


open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

/// The character bridge is the exporters' (`VectorOracle.JsonModel`, Phase 478).
let toCh (c: char) : JsonParse.ch = VectorOracle.JsonModel.toCh c

let toChs (s: string) : JsonParse.ch list = VectorOracle.JsonModel.toChs s

/// A string from its UTF-16 units. An unpaired surrogate is built this way and never written
/// as a `\u` escape in an F# literal, which the compiler replaces with U+FFFD.
let units (codes: int list) : string =
    System.String(codes |> List.map char |> Array.ofList)

/// The BLIND bridge — the go-red instrument for the comparison itself, and this family's
/// counterpart to the decode family's blind integer bridge. It hides the two container
/// characters, so the model is asked about a document production was not asked about and every
/// structured input must disagree. It touches nothing else, so a scalar still agrees, which is
/// what keeps the instrument narrow enough to be informative.
let toChsBlind (s: string) : JsonParse.ch list =
    s
    |> Seq.map (fun c ->
        match c with
        | '{'
        | '[' -> JsonParse.COther(string c)
        | other -> toCh other)
    |> List.ofSeq

let private inv = System.Globalization.CultureInfo.InvariantCulture

let chStr (c: JsonParse.ch) : string = VectorOracle.JsonModel.chStr c

let tokStr (t: JsonParse.ch list) : string = VectorOracle.JsonModel.tokStr t

let private modelStr (os: JsonParse.och list) : string = VectorOracle.JsonModel.modelStr os

let rec private renderProd (v: JVal) : string =
    match v with
    | JStr s -> "s:" + s
    | JInt i -> "i:" + string i
    | JBool b -> "b:" + string b
    | JFloat f -> "f:" + f.ToString("R", inv)
    | JArr xs -> "[" + (xs |> List.map renderProd |> String.concat ",") + "]"
    | JObj fs ->
        "{"
        + (fs |> List.map (fun (k, v) -> k + "=" + renderProd v) |> String.concat ",")
        + "}"

/// The model's tree in the same rendering. A number carries its TOKEN, so this is where the
/// token boundaries are checked: reading it back with the same .NET call production made means
/// a model that scanned one character more or less renders a different value.
let rec private renderModel (v: JsonParse.jval) : string =
    match v with
    | JsonParse.JStr s -> "s:" + modelStr s
    | JsonParse.JInt tok -> "i:" + string (System.Int32.Parse(tokStr tok, inv))
    | JsonParse.JBool b -> "b:" + string b
    | JsonParse.JFloat tok ->
        let d =
            System.Double.Parse(tokStr tok, System.Globalization.NumberStyles.Float, inv)

        "f:" + d.ToString("R", inv)
    | JsonParse.JArr xs -> "[" + (xs |> List.map renderModel |> String.concat ",") + "]"
    | JsonParse.JObj fs ->
        "{"
        + (fs
           |> List.map (fun (k, v) -> modelStr k + "=" + renderModel v)
           |> String.concat ",")
        + "}"

let floatRead (tok: JsonParse.ch list) : JsonParse.freadv = VectorOracle.JsonModel.floatRead tok

let budget (n: int) : unit list = VectorOracle.JsonModel.budget n

let private modelPolicy (p: NullPolicy) : JsonParse.policy = VectorOracle.JsonModel.policy p

/// The depth cap's one rendering, on both sides (Phase 519): production's `Depth` breach and the
/// model's `MaxDepthExceeded` at the same position are the same answer by the lift.
let depthLine (position: int) : string =
    sprintf "limit %s @%d" (JsonLimit.name JsonLimit.Depth) position

let prodAnswer (policy: NullPolicy) (maxDepth: int) (input: string) : string =
    match
        Json.read
            { Null = policy
              Limits = JsonLimits.depthOnly maxDepth }
            input
    with
    | Ok v -> "ok " + renderProd v
    | Error(JsonRefusal.Malformed e) -> sprintf "err %A @%d %s" e.Kind e.Position e.Message
    | Error(JsonRefusal.LimitExceeded b) when b.Limit = JsonLimit.Depth -> depthLine b.Position
    // No other bound is set, so none can be passed; rendered apart so a defect shows.
    | Error(JsonRefusal.LimitExceeded b) -> sprintf "limit %s @%d (not the cap)" (JsonLimit.name b.Limit) b.Position

/// The model's answer. `modelBudget` and `cap` are separate arguments only so the go-red case
/// can hand the model a cap production is not using; every real run passes `budget maxDepth`.
let modelAnswer
    (bridge: string -> JsonParse.ch list)
    (policy: NullPolicy)
    (cap: int)
    (modelBudget: unit list)
    (input: string)
    : string =
    match JsonParse.parse floatRead (string cap) (modelPolicy policy) modelBudget (bridge input) with
    | JsonParse.ROk v -> "ok " + renderModel v
    // The lift (`limited_at_no_bound_is_the_parser`): the cap's refusal is the Depth breach.
    | JsonParse.RErr(JsonParse.MaxDepthExceeded, _, at) -> depthLine (input.Length - List.length at)
    | JsonParse.RErr(k, m, at) -> sprintf "err %A @%d %s" k (input.Length - List.length at) m
    // `parse` runs at no bound and never answers this (`limited_at_no_bound_is_the_parser`);
    // the arm is for the match's totality.
    | JsonParse.RLimit(l, at) -> sprintf "limit %A @%d" l (input.Length - List.length at)

// ---- the LIMITS (Phase 465) ----

/// Production's answer under a limits record. A breach renders as `limit <name> @<position>`:
/// the model does not carry the path, which the decode vectors compare instead.
let prodAnswerLimited (policy: NullPolicy) (limits: JsonLimits) (input: string) : string =
    match Json.read { Null = policy; Limits = limits } input with
    | Ok v -> "ok " + renderProd v
    | Error(JsonRefusal.Malformed e) -> sprintf "err %A @%d %s" e.Kind e.Position e.Message
    | Error(JsonRefusal.LimitExceeded b) -> sprintf "limit %s @%d" (JsonLimit.name b.Limit) b.Position

/// The model's answer under a record. `bytesOk` is the byte verdict the model takes as a
/// parameter (its alphabet cannot count bytes); the differential computes it with the
/// platform's UTF-8 encoder, an oracle independent of production's own counter.
let modelAnswerLimited
    (bridge: string -> JsonParse.ch list)
    (policy: NullPolicy)
    (limits: JsonLimits)
    (bytesOk: bool)
    (input: string)
    : string =
    match
        JsonParse.parse_limited
            floatRead
            (string limits.MaxDepth)
            (modelPolicy policy)
            (budget limits.MaxDepth)
            (VectorOracle.JsonModel.modelLims limits)
            bytesOk
            (bridge input)
    with
    | JsonParse.ROk v -> "ok " + renderModel v
    | JsonParse.RErr(k, m, at) -> sprintf "err %A @%d %s" k (input.Length - List.length at) m
    | JsonParse.RLimit(l, at) ->
        sprintf "limit %s @%d" (VectorOracle.JsonModel.limitName l) (input.Length - List.length at)

/// Every way the two disagree over one pool, plus how many inputs reached each outcome class —
/// a run that only ever refused has compared twelve error messages and measured no parse at
/// all, and a run that only ever accepted has measured none of the classification. EVERY
/// disagreement counts: Phase 299's grammar and surrogate refusals were carved out of this
/// comparison until Phase 306 restated the model to make them, and the carve-out is gone.
let sweep (policy: NullPolicy) (maxDepth: int) (inputs: (string * string) list) : Tally =
    Tally.empty [ "Accepted"; "Refused" ]
    |> Differential.over inputs (fun (name, input) t ->
        let p = prodAnswer policy maxDepth input
        let m = modelAnswer toChs policy maxDepth (budget maxDepth) input

        t
        |> Tally.bump (if p.StartsWith "ok " then "Accepted" else "Refused")
        |> Differential.compareWith
            Differential.text
            (fun p m -> sprintf "%s: input %s\n    production %s\n    model      %s" name input p m)
            p
            m
        // The sweep compares in the regime the differential was written for — an unbounded-
        // equivalent parser — so its cap sits above every input it is handed, and an input
        // that reaches it is a defect of the pool, not an agreement (Phase 519). The cap's own
        // boundary is the deep/wide family's, which straddles it on purpose.
        |> Differential.check (not (p.StartsWith "limit ")) (fun () ->
            sprintf "%s: input %s reached the sweep's cap %d — the pool left its regime" name input maxDepth))

let expectAgreement (label: string) (t: Tally) =
    Differential.expectAgrees label t

    Expect.isGreaterThan
        (Tally.count "Accepted" t)
        0
        (label + ": no input was ACCEPTED — the accept path measured nothing")

    Expect.isGreaterThan
        (Tally.count "Refused" t)
        0
        (label + ": no input was REFUSED — the classification measured nothing")

// ---- the pools ----

/// Every corpus fixture, as the raw TEXT production reads. The differential above this one asks
/// about the decoded value; this one asks about the bytes.
let corpusTexts (family: string) : (string * string) list =
    match SiblingCorpus.resolve family with
    | SiblingCorpus.Found root ->
        Directory.GetFiles(Path.Combine(root, family), "*.json")
        |> Array.sort
        |> Array.toList
        |> List.map (fun p -> Path.GetFileNameWithoutExtension p, File.ReadAllText p)
    | SiblingCorpus.NotAsked why -> skiptest why
    | SiblingCorpus.Absent why -> failtest why

/// The near misses, hand-written so every classified failure is REACHED rather than hoped for —
/// the host's counterpart to the model's own reachability theorem. The two `null` near misses
/// are the ones Phase 135's ladder names as the reason its policy entry is an assumption:
/// `nul` falls through to the strict arm, `nullish` is caught by the following expectation.
let nearMisses: (string * string) list =
    [ "empty", ""
      "whitespace only", "   \t\r\n"
      "bare word", "%"
      "true", "true"
      "truncated true", "tru"
      "truncated false", "fals"
      "bare null", "null"
      "member null", "{\"a\":null}"
      "member nul", "{\"a\":nul}"
      "member nullish", "{\"a\":nullish}"
      "array null", "[null]"
      "unterminated string", "\"abc"
      "unterminated escape", "\"abc\\"
      "bad escape", "\"a\\q\""
      "truncated unicode", "\"\\u12\""
      "bad hex digit", "\"\\uzzzz\""
      "good unicode", "\"\\u0041\\u00e9\""
      "all short escapes", "\"\\\"\\\\\\/\\n\\r\\t\\b\\f\""
      "trailing characters", "0 0"
      "trailing brace", "{} }"
      "object missing colon", "{\"a\" 1}"
      "object trailing comma", "{\"a\":1,}"
      // Phase 462 — the decode-determinism rows the model refuses itself: a repeated member
      // name (checked when the member is accumulated; an erased null is not an occurrence) and
      // a raw control inside a string, in a value, in a key, and beside a pending surrogate.
      "repeated member", "{\"a\":1,\"a\":2}"
      "repeated member apart", "{\"a\":1,\"b\":2,\"a\":3}"
      "repeated member escaped spelling", "{\"a\":1,\"\u0061\":2}"
      "repeated member after an erased null", "{\"a\":null,\"a\":1}"
      "repeated member then null", "{\"a\":1,\"a\":null}"
      "repeated member in a nested object", "[{\"a\":1},{\"b\":1,\"b\":2}]"
      "repeated member with a malformed value", "{\"a\":1,\"a\":[}"
      "one name in sibling objects", "[{\"a\":1},{\"a\":2}]"
      "one name at two depths", "{\"a\":{\"a\":1}}"
      "raw tab in a string", "\"a	b\""
      "raw control in a string", "\"a" + string (char 1) + "b\""
      "raw control in a key", "{\"a" + string (char 1) + "\":1}"
      "raw newline after a high surrogate",
      "\""
      + string (char 0xD83D)
      + "
\""
      "escaped controls", "\"\u0000\u001f\t\""
      "array trailing comma", "[1,]"
      "array missing comma", "[1 2]"
      "unclosed object", "{\"a\":1"
      "unclosed array", "[1"
      "empty object", "{}"
      "empty array", "[]"
      "nested empties", "{\"a\":[],\"b\":{}}"
      "zero", "0"
      "negative zero", "-0"
      "leading zeros small", "007"
      "leading zeros int53", "0009007199254740992"
      "int53 boundary", "9007199254740992"
      "int53 boundary plus one", "9007199254740993"
      "int32 boundary", "2147483647"
      "int32 boundary plus one", "2147483648"
      "int32 min", "-2147483648"
      "int32 min minus one", "-2147483649"
      "seventeen nines", "99999999999999999"
      // Phase 253 — an integer token past 2^53 that IS a canonical float layout is read; one
      // that spells the same double's exact value instead is not.
      "canonical layout past int53", "10000000000000000"
      "canonical layout 2^53 plus two", "9007199254740994"
      "canonical layout negative", "-9007199254740994"
      "exact value, not the layout", "18205257897171752"
      "bare minus", "-"
      "float", "1.5"
      "float exponent", "1e3"
      "float exponent plus", "1e+3"
      "float exponent minus", "1.5e-3"
      "exponent no digits", "1e"
      "exponent sign no digits", "1e+"
      "trailing dot", "1."
      "overflowing float", "1e400"
      "negative overflowing float", "-1e400"
      "deep-ish", "[[[[[1]]]]]"
      "wide", "[1,2,3,4,5,6,7,8,9,10]"
      "whitespace everywhere", " { \"a\" : [ 1 , 2 ] , \"b\" : true } "
      "unicode key", "{\"kéy\":1}"
      "colon only", ":"
      "comma only", ","
      "close brace only", "}"
      // Phase 306 — well-formed UTF-16 or refused, in every spelling a unit can arrive in. The
      // raw units are built, never written as escapes in this file (`units`).
      "raw lone high", "\"" + units [ 0xD83D ] + "\""
      "raw lone low", "\"" + units [ 0xDE00 ] + "\""
      "raw high then ascii", "\"" + units [ 0xD83D ] + "z\""
      "raw low then high", "\"" + units [ 0xDE00; 0xD83D ] + "\""
      "raw high then high", "\"" + units [ 0xD801; 0xD800 ] + "\""
      "raw pair", "\"" + units [ 0xD83D; 0xDE00 ] + "\""
      "raw pair then stray low", "\"" + units [ 0xD83D; 0xDE00; 0xDE00 ] + "\""
      "raw high at end of input", "\"" + units [ 0xD83D ]
      "escaped lone high", "\"\\uD800\""
      "escaped lone low", "\"\\uDFFF\""
      "escaped lone low lower-case", "\"\\udfff\""
      "escaped high then high", "\"\\uD801\\uD800\""
      "escaped pair", "\"\\uD83D\\uDE00\""
      "escaped pair lower-case", "\"\\ud83d\\ude00\""
      "raw high then escaped low", "\"" + units [ 0xD83D ] + "\\uDE00\""
      "escaped high then raw low", "\"\\uD83D" + units [ 0xDE00 ] + "\""
      "escaped high then short escape", "\"\\uD83D\\n\""
      "escaped high then bad escape", "\"\\uD83D\\q\""
      "escaped high then bad hex", "\"\\uD83D\\uzzzz\""
      "lone high in a key", "{\"" + units [ 0xD800 ] + "\":1}"
      "escaped boundary below the surrogates", "\"\\uD7FF\""
      "escaped boundary above the surrogates", "\"\\uE000\""
      // The number grammar's boundary, beside the entries above that already reach it.
      "leading zero then fraction", "01.5"
      "point without integer", "-.5"
      "point before exponent", "1.e5"
      "negative leading zero", "-01"
      "zero fraction", "0.5"
      "zero exponent", "0e0" ]

/// A nesting of exactly `k` arrays around a scalar — the family the model's `depth_bound_exact`
/// is stated over, built here so the theorem's instance and the differential's input are the
/// same shape.
let nestArr (k: int) : string =
    String.replicate k "[" + "0" + String.replicate k "]"

let nestObj (k: int) : string =
    String.replicate k "{\"a\":" + "0" + String.replicate k "}"

/// Deterministic character soup over the parser's own alphabet — most of it malformed, which is
/// the point: it is the only pool that reaches failure positions nobody thought to write down.
let soup (seed: int) (count: int) : (string * string) list =
    // The two surrogate halves are in the alphabet since Phase 306 (built, not written): a
    // soup string holds them lone, paired and reversed, which nothing hand-written enumerates.
    let alphabet =
        "{}[]\",:0123456789-+.eE\\ \ttnrufalse\u00e9%dD8" + units [ 0xD83D; 0xDE00 ]

    let mutable state = uint64 seed * 6364136223846793005UL + 1442695040888963407UL

    let next () =
        state <- state * 6364136223846793005UL + 1442695040888963407UL
        int ((state >>> 33) &&& 0x7FFFFFFFUL)

    [ for i in 1..count ->
          let len = next () % 24

          let s = System.String(Array.init len (fun _ -> alphabet[next () % alphabet.Length]))

          sprintf "soup seed=%d #%d" seed i, s ]

// ---- the ERASE-THEN-COMPARE probe (Phase 190) ----

/// A refusal rendering with the index dropped. The bridge relates two documents of DIFFERENT
/// lengths, and the model carries a position as the input SUFFIX — the same list on both
/// sides — which this host renders as an index into an input the deletion shortened. So the
/// kind and the message are compared and the index is not; the index is what every other
/// probe in this family compares, on documents that are the same bytes on both sides.
let withoutPosition (answer: string) : string =
    if answer.StartsWith "ok " then
        answer
    else
        System.Text.RegularExpressions.Regex.Replace(answer, " @\\d+ ", " ")

/// `k` nested objects, each carrying a member null beside a member that survives — the
/// every-depth half of the theorem, whose model statement quantifies over the remaining
/// budget rather than over a nesting family.
let rec nestObjNulls (k: int) : string * string =
    if k <= 0 then
        "0", "0"
    else
        let inner, erased = nestObjNulls (k - 1)
        sprintf "{\"n\":null,\"v\":%s}" inner, sprintf "{\"v\":%s}" erased

/// Phase 190's bridge as a pool: a document carrying member nulls, beside the document with
/// exactly those members DELETED. `null_absorption_is_erasure` says the tolerant reading of
/// the first IS the strict reading of the second, so this is the theorem's own statement put
/// to production and to the extracted model rather than only to the prover.
let erasurePairs: (string * string * string) list =
    [ "leading null", "{\"a\":null,\"b\":1}", "{\"b\":1}"
      "trailing null", "{\"b\":1,\"a\":null}", "{\"b\":1}"
      "interior null", "{\"b\":1,\"a\":null,\"c\":2}", "{\"b\":1,\"c\":2}"
      "run of nulls", "{\"a\":null,\"b\":null,\"c\":3}", "{\"c\":3}"
      "sole null", "{\"a\":null}", "{}"
      "all nulls", "{\"a\":null,\"b\":null}", "{}"
      // The whitespace the model's `null_member` does not spell: it is read by `skip_ws`,
      // which is policy-independent, so this is the spelling the theorem's boundary names.
      "whitespace around the fork", "{ \"a\" : null , \"b\" : 1 }", "{ \"b\" : 1 }"
      "whitespace and sole null", "{ \"a\" : null }", "{ }"
      "nested object", "{\"a\":{\"c\":null,\"d\":2}}", "{\"a\":{\"d\":2}}"
      "null inside an array of objects", "[{\"a\":null,\"b\":1},{\"a\":null}]", "[{\"b\":1},{}]"
      "null beside an array member", "{\"a\":null,\"b\":[1,{\"c\":null,\"d\":3}]}", "{\"b\":[1,{\"d\":3}]}"
      // Keys the model's `plain_all` hypothesis does not admit — that hypothesis is what lets
      // the lemma unfold `parse_string` without a fact about escapes, and the absorption does
      // not read the key at all, so production and the oracle are asked the wider question
      // here than the prover was asked there.
      "escaped key", "{\"a\\nb\":null,\"c\":1}", "{\"c\":1}"
      "unicode key", "{\"kéy\":null,\"c\":1}", "{\"c\":1}"
      // The REFUSAL half: a tail the strict reader refuses is refused identically, by kind
      // and message, whether or not the nulls in front of it were absorbed.
      "refused tail — no value", "{\"a\":null,\"b\":}", "{\"b\":}"
      "refused tail — unclosed", "{\"a\":null,\"b\":1", "{\"b\":1"
      "refused tail — bad number", "{\"a\":null,\"b\":1e}", "{\"b\":1e}"
      "deep nesting", fst (nestObjNulls 6), snd (nestObjNulls 6) ]
