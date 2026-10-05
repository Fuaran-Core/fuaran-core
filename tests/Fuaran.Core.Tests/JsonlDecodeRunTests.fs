module Fuaran.Core.Tests.JsonlDecodeRunTests

// Phase 369 — an op-stream line is decoded by runs, not characters. `Jsonl.decodeString` decodes a
// string token `skipString` has already scanned and accepted. It appended one character at a time; it
// now returns an escape-free body whole (one `Substring`) and otherwise copies each clean run by one
// ranged `Append`, decoding only the escapes. `skipString` is unchanged, so every refusal, its kind and
// its position are what they were.
//
// `decodeString` is private, so production is reached through every public route that calls it:
// `Jsonl.unquote` (a whole token), `Jsonl.parseLine` / `topFields` (a member key), `Jsonl.stringField`
// (a member value) and `Jsonl.stringsField` (array items). This suite pins those against `Oracle.decode`,
// the decoder as it stood before the phase, kept here verbatim: an exhaustive sweep over every
// sequence of up to four tokens from a run-boundary alphabet; each fault kind `skipString` raises
// named at the start, the middle and the end of a run and across a run/escape boundary, with its
// position stated; and a go-red over two plausible broken run decoders, so the sweep is shown able to
// fail.

open Expecto
open Fuaran.Core

module private Oracle =

    let private isHex (c: char) =
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')

    let private hexVal (c: char) =
        if c <= '9' then int c - int '0'
        elif c >= 'a' then int c - int 'a' + 10
        else int c - int 'A' + 10

    let unicodeAt (s: string) (i: int) : int =
        if
            i + 5 < s.Length
            && s.[i + 1] = 'u'
            && isHex s.[i + 2]
            && isHex s.[i + 3]
            && isHex s.[i + 4]
            && isHex s.[i + 5]
        then
            (hexVal s.[i + 2] <<< 12)
            + (hexVal s.[i + 3] <<< 8)
            + (hexVal s.[i + 4] <<< 4)
            + hexVal s.[i + 5]
        else
            -1

    /// The decoder before Phase 369, one unit at a time, clause for clause. `token` is a token
    /// `skipString` accepted, quotes included.
    let decode (token: string) : string =
        let sb = System.Text.StringBuilder()
        let last = token.Length - 1
        let mutable i = 1

        while i < last do
            let c = token.[i]

            if c = '\\' then
                match token.[i + 1] with
                | 'u' ->
                    sb.Append(char (unicodeAt token i)) |> ignore
                    i <- i + 6
                | e ->
                    (match e with
                     | 'b' -> sb.Append('\b')
                     | 'f' -> sb.Append('\f')
                     | 'n' -> sb.Append('\n')
                     | 'r' -> sb.Append('\r')
                     | 't' -> sb.Append('\t')
                     | other -> sb.Append(other))
                    |> ignore

                    i <- i + 2
            else
                sb.Append(c) |> ignore
                i <- i + 1

        sb.ToString()

/// The run decoder with a deliberate defect at a run boundary — the go-red's two broken candidates.
/// `short` copies the run before the first escape one unit short; `dropAfter` drops every run that
/// follows an escape. With both false it is the production algorithm over a whole token.
module private Broken =

    let decodeWith (short: bool) (dropAfter: bool) (token: string) : string =
        let first = 1
        let last = token.Length - 1

        let runEnd (from: int) =
            let mutable j = from

            while j < last && token.[j] <> '\\' do
                j <- j + 1

            j

        let firstEscape = runEnd first

        if firstEscape >= last then
            token.Substring(first, last - first)
        else
            let sb = System.Text.StringBuilder()
            let head = firstEscape - first

            sb.Append(token, first, (if short && head > 0 then head - 1 else head))
            |> ignore

            let mutable i = firstEscape

            while i < last do
                match token.[i + 1] with
                | 'u' ->
                    sb.Append(char (Oracle.unicodeAt token i)) |> ignore
                    i <- i + 6
                | e ->
                    (match e with
                     | 'b' -> sb.Append('\b')
                     | 'f' -> sb.Append('\f')
                     | 'n' -> sb.Append('\n')
                     | 'r' -> sb.Append('\r')
                     | 't' -> sb.Append('\t')
                     | other -> sb.Append(other))
                    |> ignore

                    i <- i + 2

                let e = runEnd i

                if e > i && not dropAfter then
                    sb.Append(token, i, e - i) |> ignore

                i <- e

            sb.ToString()

let private bs = "\\"

/// The run-boundary alphabet, as raw JSON text: runs (ASCII, non-ASCII, each raw surrogate half),
/// every short escape, `\u` escapes of a BMP character and of a surrogate pair, and every malformed
/// spelling `skipString` refuses (an unknown escape, a short `\u`, a lone escaped half of each kind,
/// a trailing backslash, a bare quote).
let private alphabet: (string * string) list =
    [ "run", "ab"
      "a", "a"
      "e-acute", "é"
      "hi", string (char 0xD83D)
      "lo", string (char 0xDE00)
      "\\n", bs + "n"
      "\\\"", bs + "\""
      "\\\\", bs + bs
      "\\/", bs + "/"
      "\\b", bs + "b"
      "\\f", bs + "f"
      "\\r", bs + "r"
      "\\t", bs + "t"
      "\\u0041", bs + "u0041"
      "\\u00e9", bs + "u00e9"
      "\\pair", bs + "uD83D" + bs + "ude00"
      "\\hi", bs + "ud83d"
      "\\lo", bs + "uDE00"
      "\\x", bs + "x"
      "\\u12", bs + "u12"
      "\\", bs
      "quote", "\"" ]

/// Every sequence of up to `depth` alphabet tokens, as (label, body).
let private bodies (depth: int) : (string * string) list =
    let rec go (k: int) : (string list * string) list =
        if k = 0 then
            [ [], "" ]
        else
            [ for (labels, body) in go (k - 1) do
                  for (l, t) in alphabet do
                      yield (l :: labels, t + body) ]

    [ for k in 0..depth do
          for (labels, body) in go k do
              yield String.concat " " labels, body ]

let private quote (body: string) = "\"" + body + "\""

/// The tokens production decodes for one body, in the order `production` reports them: the whole
/// token, the member key, the member value, and the two array items.
let private tokensOf (body: string) : string list =
    [ quote body; quote body; quote (body + "x"); quote body; quote ("y" + body) ]

/// Production's decodes of one body through every public route to `decodeString`, or `None` when
/// `skipString` refuses the token (the line must then be refused too).
let private production (body: string) : Result<string list option, string> =
    let line =
        "{"
        + quote body
        + ":"
        + quote (body + "x")
        + ",\"q\":["
        + quote body
        + ","
        + quote ("y" + body)
        + "]}"

    match OpStream.Jsonl.unquote (quote body), OpStream.Jsonl.parseLine 1 line with
    | Error _, Error _ -> Ok None
    | Error r, Ok _ -> Error("unquote refused (" + JsonlFault.reasonText r + ") and the line read")
    | Ok _, Error f -> Error("unquote read and the line was refused: " + JsonlFault.toString f)
    | Ok whole, Ok l ->
        match OpStream.Jsonl.topFields line with
        | Ok((key, _) :: _) ->
            match OpStream.Jsonl.stringField key l, OpStream.Jsonl.stringsField "q" l with
            | Ok value, Ok [ i0; i1 ] -> Ok(Some [ whole; key; value; i0; i1 ])
            | v, items -> Error(sprintf "member value %A, items %A" v items)
        | other -> Error(sprintf "top fields %A" other)

/// Every body whose decodes differ from `expected` over its tokens, labelled — at most `limit`.
let private mismatchesOf (decodes: string -> Result<string list option, string>) (inputs: (string * string) list) =
    inputs
    |> List.choose (fun (label, body) ->
        match decodes body with
        | Ok None -> None
        | Ok(Some got) ->
            let want = tokensOf body |> List.map Oracle.decode

            if got = want then
                None
            else
                Some(sprintf "%s | got %A, want %A" label got want)
        | Error why -> Some(label + " | " + why))

/// A candidate decoder over the bodies production accepts: the go-red's harness.
let private candidate (decode: string -> string) (body: string) : Result<string list option, string> =
    match OpStream.Jsonl.unquote (quote body) with
    | Error _ -> Ok None
    | Ok _ -> Ok(Some(tokensOf body |> List.map decode))

/// A member VALUE refused: the line, the fault's position in it and its reason. The value's opening
/// quote is at 5 and its body starts at 6.
let private valueLine (body: string) = "{\"k\":\"" + body + "\"}"

let private pinned: (string * string * int * JsonlFaultReason) list =
    [ "an unknown escape at the start of a run", valueLine (bs + "xab"), 6, JsonlFaultReason.InvalidEscape(bs + "x")
      "an unknown escape in the middle of a run",
      valueLine ("ab" + bs + "xcd"),
      8,
      JsonlFaultReason.InvalidEscape(bs + "x")
      "an unknown escape at the end of a run",
      valueLine ("abcd" + bs + "x"),
      10,
      JsonlFaultReason.InvalidEscape(bs + "x")
      "an unknown escape straight after an escape",
      valueLine ("ab" + bs + "n" + bs + "x"),
      10,
      JsonlFaultReason.InvalidEscape(bs + "x")
      "an unknown escape after an escape and a run",
      valueLine (bs + "u0041cd" + bs + "x"),
      14,
      JsonlFaultReason.InvalidEscape(bs + "x")
      "a short \\u at the end of a run", valueLine ("ab" + bs + "u12"), 8, JsonlFaultReason.InvalidEscape(bs + "u12\"}")
      "a lone escaped low half in a run",
      valueLine ("ab" + bs + "ude00cd"),
      8,
      JsonlFaultReason.InvalidEscape(bs + "ude00")
      "an escaped high half followed by a run",
      valueLine ("ab" + bs + "ud83dcd"),
      8,
      JsonlFaultReason.InvalidEscape(bs + "ud83dcd\"}")
      "an escaped high half followed by a non-low escape",
      valueLine (bs + "ud83d" + bs + "u0041"),
      6,
      JsonlFaultReason.InvalidEscape(bs + "ud83d" + bs + "u0041")
      "an unterminated string inside a run", "{\"k\":\"abc", 5, JsonlFaultReason.UnterminatedString
      "an unterminated string ending on a backslash", "{\"k\":\"ab" + bs, 5, JsonlFaultReason.UnterminatedString
      "an unterminated string after an escape and a run",
      "{\"k\":\"" + bs + "nab",
      5,
      JsonlFaultReason.UnterminatedString
      "an unknown escape in the middle of a KEY's run",
      "{\"ab" + bs + "xcd\":1}",
      4,
      JsonlFaultReason.InvalidEscape(bs + "x") ]

[<Tests>]
let tests =
    testList
        "JsonlDecodeRuns"
        [ testCase
              "run boundaries (Phase 369): every token sequence up to four decodes as the one-unit oracle decodes it"
          <| fun _ ->
              let inputs = bodies 4
              Expect.isGreaterThan inputs.Length 240000 "the sweep is the one written"

              let accepted =
                  inputs
                  |> List.filter (fun (_, b) -> Result.isOk (OpStream.Jsonl.unquote (quote b)))
                  |> List.length

              Expect.isGreaterThan accepted 50000 "a large share of it is accepted and decoded"
              Expect.isLessThan accepted inputs.Length "and the rest is refused, every kind of it"
              Expect.equal (mismatchesOf production inputs |> List.truncate 10) [] "no input differs"

          testCase "each fault at the start, middle and end of a run keeps its kind and position"
          <| fun _ ->
              for label, line, pos, reason in pinned do
                  match OpStream.Jsonl.parseLine 1 line with
                  | Ok _ -> failtestf "%s: the line read" label
                  | Error f -> Expect.equal (f.Position, f.Reason) (pos, reason) label

          testCase "the common string, with no escape, decodes to its own text"
          <| fun _ ->
              for s in [ ""; "plain"; "é \U0001F600 /" ] do
                  Expect.equal (OpStream.Jsonl.unquote (quote s)) (Ok s) (sprintf "%A" s)

              let line = "{\"k\":\"ab" + bs + "ncd\",\"e\":\"" + bs + "u00e9x\"}"

              match OpStream.Jsonl.parseLine 1 line with
              | Error f -> failtest (JsonlFault.toString f)
              | Ok l ->
                  Expect.equal (OpStream.Jsonl.stringField "k" l) (Ok "ab\ncd") "runs on both sides of an escape"
                  Expect.equal (OpStream.Jsonl.stringField "e" l) (Ok "éx") "a run after a \\u escape"

          testCase "run boundaries (Phase 369): two broken run decoders are red against the oracle"
          <| fun _ ->
              let inputs = bodies 3

              Expect.equal
                  (mismatchesOf (candidate (Broken.decodeWith false false)) inputs)
                  []
                  "the harness's own copy of the run decoder, unbroken, agrees"

              let short = mismatchesOf (candidate (Broken.decodeWith true false)) inputs
              let dropped = mismatchesOf (candidate (Broken.decodeWith false true)) inputs

              Expect.isNonEmpty short "a decoder that copies the first run one unit short is caught"
              Expect.exists short (fun m -> m.StartsWith "run \\n |") "on a run before a short escape"
              Expect.isNonEmpty dropped "a decoder that drops the run after an escape is caught"
              Expect.exists dropped (fun m -> m.StartsWith "\\n run |") "on a run after a short escape" ]
