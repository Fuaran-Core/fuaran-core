/// The protocol's transport: one JSON value per line, read strictly and written canonically
/// (docs/oracle-protocol.md, "Framing"). This is the envelope's reader and writer, not a model: no
/// answer the oracle gives passes through a reading this module makes of it. It is written here,
/// over the base library alone, because the protocol needs two things the platform reader does not
/// give — a string carrying an unpaired surrogate escape, which a fuzzer must be able to send, and
/// a refusal of a repeated member name.
module Fuaran.Core.Oracle.Cli.Json

open System
open System.Text

/// A JSON value as the protocol reads it. A number keeps its token: every number the protocol
/// reads is an integer, and the token is what says whether it was written as one.
type J =
    | JNull
    | JBool of bool
    | JNum of string
    | JStr of string
    | JArr of J list
    | JObj of (string * J) list

/// Why a line is not one JSON value, and the UTF-16 index the reader stopped at.
exception ReadError of message: string * at: int

/// The deepest nesting the reader follows. The request envelope sits two levels down and a tree
/// one level per generation, so this bounds the trees a caller can send, and it is stated as a
/// protocol limit (docs/oracle-protocol.md, "Limits").
[<Literal>]
let MaxDepth = 1024

let private hexValue (c: char) =
    match c with
    | c when c >= '0' && c <= '9' -> int c - int '0'
    | c when c >= 'a' && c <= 'f' -> int c - int 'a' + 10
    | c when c >= 'A' && c <= 'F' -> int c - int 'A' + 10
    | _ -> -1

/// Reads exactly one JSON value from `text`, surrounded by optional whitespace. Strict RFC 8259
/// grammar, with two deliberate differences: a `\u` escape may name an unpaired surrogate (the
/// string then holds that unit), and an object that repeats a member name is refused.
let read (text: string) : J =
    let n = text.Length
    let mutable i = 0
    let fail message = raise (ReadError(message, i))

    let skipWs () =
        while i < n && (text[i] = ' ' || text[i] = '\t' || text[i] = '\n' || text[i] = '\r') do
            i <- i + 1

    let expectLiteral (word: string) value =
        if i + word.Length <= n && String.CompareOrdinal(text, i, word, 0, word.Length) = 0 then
            i <- i + word.Length
            value
        else
            fail $"expected '{word}'"

    let readString () =
        // at the opening quote
        i <- i + 1
        let sb = StringBuilder()
        let mutable closed = false

        while not closed do
            if i >= n then
                fail "unterminated string"

            let c = text[i]

            if c = '"' then
                i <- i + 1
                closed <- true
            elif c = '\\' then
                if i + 1 >= n then
                    fail "unterminated escape"

                let e = text[i + 1]
                i <- i + 2

                match e with
                | '"' -> sb.Append('"') |> ignore
                | '\\' -> sb.Append('\\') |> ignore
                | '/' -> sb.Append('/') |> ignore
                | 'b' -> sb.Append('\b') |> ignore
                | 'f' -> sb.Append('\f') |> ignore
                | 'n' -> sb.Append('\n') |> ignore
                | 'r' -> sb.Append('\r') |> ignore
                | 't' -> sb.Append('\t') |> ignore
                | 'u' ->
                    if i + 4 > n then
                        fail "truncated \\u escape"

                    let mutable code = 0

                    for k in 0..3 do
                        let h = hexValue text[i + k]

                        if h < 0 then
                            fail "bad hex digit in a \\u escape"

                        code <- code * 16 + h

                    i <- i + 4
                    sb.Append(char code) |> ignore
                | _ -> fail $"bad escape '\\{e}'"
            elif c < ' ' then
                fail $"unescaped control character U+{int c:X4} in a string"
            else
                sb.Append(c) |> ignore
                i <- i + 1

        sb.ToString()

    let readNumber () =
        let start = i

        if i < n && text[i] = '-' then
            i <- i + 1

        let digits () =
            let s = i

            while i < n && Char.IsAsciiDigit text[i] do
                i <- i + 1

            i - s

        if i < n && text[i] = '0' then
            i <- i + 1
        elif digits () = 0 then
            fail "malformed number"

        if i < n && text[i] = '.' then
            i <- i + 1

            if digits () = 0 then
                fail "malformed number"

        if i < n && (text[i] = 'e' || text[i] = 'E') then
            i <- i + 1

            if i < n && (text[i] = '+' || text[i] = '-') then
                i <- i + 1

            if digits () = 0 then
                fail "malformed number"

        JNum(text.Substring(start, i - start))

    let rec value (depth: int) : J =
        skipWs ()

        if i >= n then
            fail "unexpected end of input"

        match text[i] with
        | '{' ->
            if depth >= MaxDepth then
                fail $"nesting deeper than {MaxDepth}"

            i <- i + 1
            skipWs ()

            if i < n && text[i] = '}' then
                i <- i + 1
                JObj []
            else
                let members = Collections.Generic.List<string * J>()
                let seen = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
                let mutable go = true

                while go do
                    skipWs ()

                    if i >= n || text[i] <> '"' then
                        fail "expected a member name"

                    let at = i
                    let key = readString ()

                    if not (seen.Add key) then
                        raise (ReadError($"repeated member name \"{key}\"", at))

                    skipWs ()

                    if i >= n || text[i] <> ':' then
                        fail "expected ':'"

                    i <- i + 1
                    members.Add(key, value (depth + 1))
                    skipWs ()

                    if i < n && text[i] = ',' then
                        i <- i + 1
                    elif i < n && text[i] = '}' then
                        i <- i + 1
                        go <- false
                    else
                        fail "expected ',' or '}'"

                JObj(List.ofSeq members)
        | '[' ->
            if depth >= MaxDepth then
                fail $"nesting deeper than {MaxDepth}"

            i <- i + 1
            skipWs ()

            if i < n && text[i] = ']' then
                i <- i + 1
                JArr []
            else
                let items = Collections.Generic.List<J>()
                let mutable go = true

                while go do
                    items.Add(value (depth + 1))
                    skipWs ()

                    if i < n && text[i] = ',' then
                        i <- i + 1
                    elif i < n && text[i] = ']' then
                        i <- i + 1
                        go <- false
                    else
                        fail "expected ',' or ']'"

                JArr(List.ofSeq items)
        | '"' -> JStr(readString ())
        | 't' -> expectLiteral "true" (JBool true)
        | 'f' -> expectLiteral "false" (JBool false)
        | 'n' -> expectLiteral "null" JNull
        | c when c = '-' || Char.IsAsciiDigit c -> readNumber ()
        | c -> fail $"unexpected character '{c}'"

    let v = value 0
    skipWs ()

    if i < n then
        fail "trailing characters after the value"

    v

let private writeString (sb: StringBuilder) (s: string) =
    sb.Append('"') |> ignore

    for k in 0 .. s.Length - 1 do
        let c = s[k]

        match c with
        | '"' -> sb.Append("\\\"") |> ignore
        | '\\' -> sb.Append("\\\\") |> ignore
        | c when c < ' ' -> sb.Append("\\u").Append((int c).ToString("x4")) |> ignore
        | c when Char.IsHighSurrogate c && k + 1 < s.Length && Char.IsLowSurrogate s[k + 1] -> sb.Append(c) |> ignore
        | c when Char.IsLowSurrogate c && k > 0 && Char.IsHighSurrogate s[k - 1] -> sb.Append(c) |> ignore
        // An unpaired surrogate has no UTF-8 encoding, so it is written as its escape: the line
        // stays valid UTF-8 and the caller reads back the unit the model held.
        | c when Char.IsSurrogate c -> sb.Append("\\u").Append((int c).ToString("x4")) |> ignore
        | c -> sb.Append(c) |> ignore

    sb.Append('"') |> ignore

/// The canonical text of a value: no whitespace, members sorted by ordinal comparison of their
/// names, a string escaping only `"`, `\`, the controls below U+0020 and an unpaired surrogate (as
/// lower-case `\u` escapes), and a number written as its token.
let write (v: J) : string =
    let sb = StringBuilder()

    let rec go v =
        match v with
        | JNull -> sb.Append("null") |> ignore
        | JBool true -> sb.Append("true") |> ignore
        | JBool false -> sb.Append("false") |> ignore
        | JNum token -> sb.Append(token) |> ignore
        | JStr s -> writeString sb s
        | JArr items ->
            sb.Append('[') |> ignore

            items
            |> List.iteri (fun k item ->
                if k > 0 then
                    sb.Append(',') |> ignore

                go item)

            sb.Append(']') |> ignore
        | JObj members ->
            sb.Append('{') |> ignore

            members
            |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
            |> List.iteri (fun k (key, item) ->
                if k > 0 then
                    sb.Append(',') |> ignore

                writeString sb key
                sb.Append(':') |> ignore
                go item)

            sb.Append('}') |> ignore

    go v
    sb.ToString()

/// An integer as a protocol number.
let ofInt (n: int) : J = JNum(string n)
