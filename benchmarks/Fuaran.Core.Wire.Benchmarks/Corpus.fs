/// The wire benchmark's fixed corpora (Phase 364; the fifth, `floats`, Phase 367): five values, each
/// built by a deterministic generator that reads no clock, no random source and no host setting, so
/// .NET and node build the SAME value and every case renders the same bytes on both. The harness (Program.fs, run by
/// ../run.ps1) and the suite's clock leg (tests/Fuaran.Core.Tests/WireClockLeg.fs) compile this one
/// file, so the clock leg times the corpus the tables time.
///
/// THE CORPORA ARE FROZEN. Phases that change the wire layer's speed cite a "before" table measured
/// over exactly these values, so `pinned` below holds every case's output length and FNV-1a, and the
/// suite fails when a corpus moves. A change to the corpus is a new baseline: re-measure both hosts
/// and re-pin in the same commit, and say so in the results file.
module Fuaran.Core.WireBench.Corpus

open Fuaran.Core

/// A deterministic draw in [0, 65521): a multiplicative step whose product stays below 2^31 on
/// every host, so the integer arithmetic means the same thing compiled by Fable as on .NET.
let private next (x: int) : int = (x * 1103 + 12345) % 65521

/// `n` written with at least `width` digits, zero-padded. Written out rather than through a padded
/// format specifier, so nothing depends on how a host formats.
let pad (width: int) (n: int) : string =
    let s = string n

    if s.Length >= width then
        s
    else
        String.replicate (width - s.Length) "0" + s

/// Printable characters that need no escape: ASCII letters, digits and punctuation, plus three BMP
/// characters past ASCII (no surrogates, so every string is well-formed UTF-16).
let private plain =
    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_.,:;/()[]{}#@!?+=*&%$~|<>'éü中"

/// The characters `Json.escape` rewrites: the quote, the backslash and the controls (`\n`, `\r`,
/// `\t` and two others), each of which takes the `\u00xx` or two-character form.
let private escaped = "\"\\\n\r\t\u0001\u001f"

/// A string of `len` characters drawn from `seed`. `heavy` makes about one character in four an
/// escaped one; otherwise none is.
let private text (heavy: bool) (len: int) (seed: int) : string =
    let sb = System.Text.StringBuilder()
    let mutable x = seed % 65521

    for _ in 1..len do
        x <- next x

        if heavy && x % 4 = 0 then
            sb.Append(escaped[(x / 4) % escaped.Length]) |> ignore
        else
            sb.Append(plain[x % plain.Length]) |> ignore

    sb.ToString()

/// How many strings, and how long, in the two string corpora.
let stringCount = 1_000
let stringLength = 64

/// (a) Escape-free strings: `stringCount` strings of `stringLength` characters, none escaped.
let escapeFree () : JVal =
    JArr [ for i in 1..stringCount -> JStr(text false stringLength (i * 7919)) ]

/// (b) Escape-heavy strings: the same count and length, about a quarter of the characters escaped.
let escapeHeavy () : JVal =
    JArr [ for i in 1..stringCount -> JStr(text true stringLength (i * 7919)) ]

/// How many records the op-stream corpus holds.
let opCount = 500

let private opKinds = [| "setField"; "addPhase"; "markShipped"; "recordOutcome" |]

/// A 64-hex-digit digest-shaped string, from eight FNV-1a values of seeded text.
let private digestText (i: int) : string =
    [ for k in 1..8 -> Hash.fnv1a (string (i * 8 + k)) ] |> String.concat ""

/// One op-stream-shaped record: a kind tag, ids, a Lamport clock, a nested actor, a path array, a
/// short free-text value with an occasional escape, a non-whole float, a timestamp and a digest.
let private opRecord (i: int) : JVal =
    JObj
        [ "kind", JStr opKinds[i % opKinds.Length]
          "id", JStr("op-" + pad 6 i)
          "lane", JStr("lane-" + string (i % 7))
          "lamport", JInt(i * 3 + 1)
          "actor",
          JObj
              [ "name", JStr("worker-" + string (i % 11))
                "session", JStr("drv-w" + string (i % 3) + "-" + pad 3 (300 + i % 90)) ]
          "path", JArr [ JStr "phases"; JStr(string (300 + i % 90)); JStr "status" ]
          "value", JStr(text (i % 5 = 0) 24 (i * 104729))
          "weight", JFloat(float (i % 97) + 0.25)
          "at",
          JStr(
              "2026-10-04T"
              + pad 2 (i % 24)
              + ":"
              + pad 2 (i % 60)
              + ":"
              + pad 2 ((i * 7) % 60)
              + "Z"
          )
          "prev", JStr("sha256:" + digestText i) ]

/// (c) Op-stream-shaped records: an array of `opCount` records.
let opStream () : JVal =
    JArr [ for i in 1..opCount -> opRecord i ]

/// The default size of the state corpus.
let stateTokens = 20_000

/// (d) The shape of the compute repository's incremental state: a scheme tag, a LARGE array of short
/// strings (its `tokens`), and a long packed text of integers (its `ints` fields, which travel as
/// one string). `n` tokens and `n` packed integers.
let stateOf (n: int) : JVal =
    let packed =
        let sb = System.Text.StringBuilder()
        let mutable x = 17

        for i in 1..n do
            x <- next x

            if i > 1 then
                sb.Append(',') |> ignore

            sb.Append(string (x % 1000)) |> ignore

        sb.ToString()

    JObj
        [ "scheme", JStr "row-identity"
          "tokens", JArr [ for i in 1..n -> JStr("r" + pad 5 i) ]
          "rowGroups", JStr packed ]

let state () : JVal = stateOf stateTokens

/// How many numbers the float corpus holds.
let floatCount = 10_000

/// One float of the float corpus, the `i`th, from the draw `x` in [0, 65521). Eight layouts in turn:
/// short and long fractions, a negative, a small number and a large one in the `E` layout, and - one
/// in eight - a WHOLE double past 2^53, which the float layout writes as a 16- or 17-digit integer
/// token. That last shape is the only number token whose parse re-renders the value it read
/// (`parseNumber`'s Phase 253 check, `FloatLayout.finite v = tok`); every token with a `.` or an `E`
/// is read without one. No other value is whole, because a whole double below 2^53 renders as an
/// integer token and would parse back as a `JInt`.
let private floatAt (i: int) (x: int) : float =
    let h = float x + 0.5

    match i % 8 with
    | 0 -> h / 64.0
    | 1 -> h / 7.0
    | 2 -> -(h / 3.0)
    | 3 -> h * 1e-9 / 7.0
    | 4 -> float (x + 1) * 1e20 / 3.0
    | 5 -> float (x % 100) + 0.5
    | 6 ->
        if x % 2 = 0 then
            1e16 + 2.0 * float x
        else
            9.1e15 + 2.0 * float x
    | _ -> h * 1e-3

/// (e) Float-heavy (Phase 367): an array of `floatCount` floats, every one a `JFloat`, so a parse of
/// it is almost entirely number tokens.
let floats () : JVal =
    let mutable x = 31

    JArr
        [ for i in 1..floatCount do
              x <- next x
              yield JFloat(floatAt i x) ]

/// The five corpora, in table order, by name.
let corpora: (string * (unit -> JVal)) list =
    [ "escape-free", escapeFree
      "escape-heavy", escapeHeavy
      "op-stream", opStream
      "state", state
      "floats", floats ]

/// Every string the value carries, keys included, in document order: what the `escape` case escapes.
let strings (v: JVal) : string[] =
    let acc = ResizeArray<string>()

    let rec walk (v: JVal) =
        match v with
        | JStr s -> acc.Add s
        | JArr xs -> List.iter walk xs
        | JObj fields ->
            for (k, x) in fields do
                acc.Add k
                walk x
        | JInt _
        | JBool _
        | JFloat _ -> ()

    walk v
    acc.ToArray()

/// The four cases, in column order. Each takes the prepared inputs and returns something a timer can
/// keep alive; `escape` returns the total escaped length, so the case times the escapes and not a
/// concatenation of them.
type Prepared =
    { Value: JVal
      Strings: string[]
      Rendered: string }

let prepare (v: JVal) : Prepared =
    { Value = v
      Strings = strings v
      Rendered = Json.render v }

let escapeAll (p: Prepared) : int =
    let mutable n = 0

    for s in p.Strings do
        n <- n + (Json.escape s).Length

    n

let cases: (string * (Prepared -> unit -> obj)) list =
    [ "escape", (fun p () -> box (escapeAll p))
      "render", (fun p () -> box (Json.render p.Value))
      "canon", (fun p () -> box (Canon.render p.Value))
      "parse", (fun p () -> box (Json.parse p.Rendered)) ]

/// The OUTPUT of a case as text, for its fingerprint: the escapes concatenated, the rendered text,
/// the canonical text, and the parsed value rendered back.
let output (p: Prepared) (case: string) : string =
    match case with
    | "escape" -> p.Strings |> Array.map Json.escape |> String.concat "\n"
    | "render" -> Json.render p.Value
    | "canon" -> Canon.render p.Value
    | "parse" ->
        match Json.parse p.Rendered with
        | Ok v -> Json.render v
        | Error e -> "parse refused: " + e
    | other -> failwith ("benchmark corpus: no case named " + other)

/// A case's output fingerprint: its length in UTF-16 units and its FNV-1a, `<len>:<fnv>`. The same
/// on both hosts by construction of `Hash.fnv1a` (its parity vectors hold that).
let fingerprint (p: Prepared) (case: string) : string =
    let o = output p case
    string o.Length + ":" + Hash.fnv1a o

/// Every case's fingerprint, measured on .NET when the corpus was frozen (Phase 364). A host whose
/// fingerprint differs is computing different bytes; a corpus edit that moves one is a new baseline.
let pinned: (string * string * string) list =
    [ "escape-free", "escape", "64999:cb187309"
      "escape-free", "render", "67001:30b7cfe5"
      "escape-free", "canon", "67001:30b7cfe5"
      "escape-free", "parse", "67001:30b7cfe5"
      "escape-heavy", "escape", "126648:8fb3ccd3"
      "escape-heavy", "render", "128650:70f82119"
      "escape-heavy", "canon", "128650:70f82119"
      "escape-heavy", "parse", "128650:70f82119"
      "op-stream", "escape", "127358:daeb7431"
      "op-stream", "render", "158435:bd23bc81"
      "op-stream", "canon", "158435:cf92b045"
      "op-stream", "parse", "158435:bd23bc81"
      "state", "escape", "217865:399f0f5e"
      "state", "render", "257879:21383ec8"
      "state", "canon", "257879:a6d6a37e"
      "state", "parse", "257879:21383ec8"
      // Phase 367: the float corpus, pinned when it was added. The other corpora did not move.
      "floats", "escape", "0:811c9dc5"
      "floats", "render", "147049:0b5f9622"
      "floats", "canon", "147049:0b5f9622"
      "floats", "parse", "147049:0b5f9622" ]

/// The agreements every case must hold BEFORE it is timed, on the host that times it, short of the
/// pins: escape reads back, render round-trips, and the canonical text is a fixed point. `Error`
/// names the first that failed; the message starts `benchmark corpus:` so no caller mistakes it for
/// a host limit.
let laws (name: string) (p: Prepared) : Result<unit, string> =
    let fail (what: string) =
        Error("benchmark corpus: " + name + ": " + what)

    let escapesRead =
        p.Strings
        |> Array.forall (fun s -> Json.parse ("\"" + Json.escape s + "\"") = Ok(JStr s))

    if not escapesRead then
        fail "an escaped string does not parse back to itself"
    else
        match Json.parse p.Rendered with
        | Error e -> fail ("the rendered text does not parse: " + e)
        | Ok v when v <> p.Value -> fail "the rendered text parses to a different value"
        | Ok v ->
            let canon = Canon.render p.Value

            if Canon.render v <> canon then
                fail "the canonical text of the parsed value differs"
            else
                match Json.parse canon with
                | Ok c when Canon.render c = canon -> Ok()
                | _ -> fail "the canonical text is not a fixed point of parse then canon"

/// The cases of corpus `name` whose fingerprint is not the pinned one, as `(case, measured, pinned)`.
let unpinned (name: string) (p: Prepared) : (string * string * string) list =
    pinned
    |> List.filter (fun (c, _, _) -> c = name)
    |> List.choose (fun (_, case, fp) ->
        let got = fingerprint p case
        if got = fp then None else Some(case, got, fp))

/// `laws`, then the pins: what the harness asserts before it times a corpus.
let check (name: string) (p: Prepared) : Result<unit, string> =
    match laws name p with
    | Error e -> Error e
    | Ok() ->
        match unpinned name p with
        | [] -> Ok()
        | (case, got, fp) :: _ ->
            Error(
                "benchmark corpus: "
                + name
                + ": "
                + case
                + " fingerprint "
                + got
                + ", pinned "
                + fp
            )
