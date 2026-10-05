namespace Fuaran.Core

/// WHY a JSONL line was refused (Phase 296) — the closed set of faults the one line scanner
/// (`OpStream.Jsonl`) and the readers built on it report. Before this type the scanner was lenient
/// where `Wire.Json.parse` refuses: an unquoted value read as a string with its first and last
/// characters cut off (`"prevHash":null` read as `"ul"`), a non-hex `\u` digit went through
/// arithmetic, and a line ending after `:` indexed past the end. Each is now a named refusal, so a
/// stream that reads `Ok` is one the scanner actually parsed.
[<RequireQualifiedAccess>]
type JsonlFaultReason =
    /// The line's first non-blank character is not `{` — the line is not a JSON object.
    | NotAnObject
    /// The line ends before its object does — after a `:`, after a `,`, or before the closing `}`.
    | Truncated
    /// A member key is not a string token.
    | ExpectedKey
    /// A member key is not followed by `:`.
    | ExpectedColon
    /// A member has no value (`"a":}` or `"a":,`).
    | MissingValue
    /// A member value is not followed by `,` or `}`.
    | ExpectedCommaOrBrace
    /// Non-blank content follows the object's closing `}`.
    | TrailingContent
    /// A string token is not closed before the line ends.
    | UnterminatedString
    /// An array or object value is not closed before the line ends.
    | UnterminatedContainer
    /// An escape the JSON grammar does not have: an unknown escape letter, a `\u` whose four digits
    /// are not all hex, or a surrogate escape that is not half of a high-low pair. The escape as read.
    | InvalidEscape of escape: string
    /// A bare value that is not `true`, `false`, `null` or a JSON number. The token as read.
    | InvalidLiteral of token: string
    /// A member the reader requires to be a string holds another kind of value (`"prevHash":null`,
    /// `"id":12`).
    | ExpectedString of field: string
    /// A member the reader requires to be an integer holds something else — a string, a fraction, an
    /// exponent, a hex spelling (`"seq":0x2`), or a value outside the 32-bit range.
    | ExpectedInteger of field: string
    /// A member the reader requires to be an array of strings holds something else.
    | ExpectedStringArray of field: string
    /// A member the reader requires is absent.
    | MissingField of field: string
    /// A snapshot line that is not the first line of the stream — one snapshot line, at the head, is
    /// the only compacted shape `compact` writes.
    | SnapshotNotAtHead
    /// The line is well-formed JSON and its reader refused what it says — a witness decode `Error`,
    /// an actor kind this build does not know, a node colliding with one already read. The reader's
    /// reason, verbatim.
    | Refused of reason: string
    /// The line's actor names nobody (Phase 315) — the member that carried it, and why. Declared
    /// LAST so every existing case keeps its tag.
    | ActorInvalid of field: string * reason: ActorInvalid

/// One refused JSONL line (Phase 296): the 1-based `Line` number counted over EVERY line of the
/// text (blank lines included, so it is the number an editor shows), the scanner's own 0-based
/// `Position` within that line, and the `Reason`.
type JsonlFault =
    {
        /// 1-based line number over every line of the text, blank lines included.
        Line: int
        /// 0-based character offset within the line where the scanner refused; `0` for a fault about
        /// the line as a whole, such as a missing member.
        Position: int
        /// Why the line was refused.
        Reason: JsonlFaultReason
    }

/// Render a `JsonlFault` for a log line or an `Error` string (Phase 296).
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module JsonlFault =

    /// The reason's text, without the line and position.
    let reasonText (r: JsonlFaultReason) : string =
        match r with
        | JsonlFaultReason.NotAnObject -> "expected a JSON object"
        | JsonlFaultReason.Truncated -> "the line ends inside the object"
        | JsonlFaultReason.ExpectedKey -> "expected a member key (a string)"
        | JsonlFaultReason.ExpectedColon -> "expected ':'"
        | JsonlFaultReason.MissingValue -> "a member has no value"
        | JsonlFaultReason.ExpectedCommaOrBrace -> "expected ',' or '}'"
        | JsonlFaultReason.TrailingContent -> "content after the closing '}'"
        | JsonlFaultReason.UnterminatedString -> "unterminated string"
        | JsonlFaultReason.UnterminatedContainer -> "unterminated container"
        | JsonlFaultReason.InvalidEscape e -> "invalid escape " + e
        | JsonlFaultReason.InvalidLiteral t -> "invalid literal '" + t + "'"
        | JsonlFaultReason.ExpectedString f -> "field " + f + " is not a string"
        | JsonlFaultReason.ExpectedInteger f -> "field " + f + " is not an integer"
        | JsonlFaultReason.ExpectedStringArray f -> "field " + f + " is not an array of strings"
        | JsonlFaultReason.MissingField f -> "missing field " + f
        | JsonlFaultReason.SnapshotNotAtHead -> "a snapshot line that is not the first line of the stream"
        | JsonlFaultReason.Refused reason -> reason
        | JsonlFaultReason.ActorInvalid(field, ActorInvalid.EmptyId) -> "the actor in " + field + " has an empty id"

    /// `line N: <reason> (position P)` — the `line N:` prefix every JSONL reader's `Error` string
    /// has carried since Phase 252, now 1-based over all lines.
    let toString (f: JsonlFault) : string =
        sprintf "line %d: %s (position %d)" f.Line (reasonText f.Reason) f.Position

/// WHY a JSONL writer refused to embed a raw span (Phase 301). A writer embeds a domain encoding —
/// an op, a captured value, a snapshot's state — verbatim, and the reader hands the decoder the
/// value's exact span; so the line round-trips only when the encoding IS one JSON value that sits on
/// one line with nothing around it. Before this type the writers embedded whatever `Encode` produced,
/// and an encoding with a line break, or a space either side, read back changed — and failed
/// `verifyChain` / `verifyCaptures` — with nothing at the write to say so.
[<RequireQualifiedAccess>]
type JsonlWriteFaultReason =
    /// The encoding carries a line break (`\n` or `\r`) at `position`: the line would end inside it.
    | MultiLine of position: int
    /// Whitespace before or after the one value: the reader keeps the value's span without it, so
    /// the decoder would be handed different bytes than the chain hashed.
    | NotTrimStable
    /// The scanner does not read the encoding as one JSON value — no value, an invalid literal, an
    /// unterminated string or container, a bad escape, or a second value after the first. The
    /// scanner's own reason.
    | Unreadable of reason: JsonlFaultReason

/// One refused embedding (Phase 301): the 1-based `Line` of the output the writer would have
/// produced (the record's, capture's or node's position plus one; a snapshot line is line 1), the
/// `Member` whose span was refused (`op`, `value` or `state`), and the `Reason`.
type JsonlWriteFault =
    {
        /// 1-based line of the output the refused item would have occupied.
        Line: int
        /// The member whose embedded span was refused: `op`, `value` or `state`.
        Member: string
        /// What is wrong with the encoding. The writer stops at the first refused item and emits nothing.
        Reason: JsonlWriteFaultReason
    }

/// Render a `JsonlWriteFault` for a log line or an `Error` string (Phase 301).
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module JsonlWriteFault =

    /// The reason's text, without the line and member.
    let reasonText (r: JsonlWriteFaultReason) : string =
        match r with
        | JsonlWriteFaultReason.MultiLine p -> sprintf "the encoding carries a line break at position %d" p
        | JsonlWriteFaultReason.NotTrimStable -> "whitespace before or after the encoded value"
        | JsonlWriteFaultReason.Unreadable r -> "not one JSON value: " + JsonlFault.reasonText r

    /// `line N: member M: <reason>`.
    let toString (f: JsonlWriteFault) : string =
        sprintf "line %d: member %s: %s" f.Line f.Member (reasonText f.Reason)

/// The scanner's internal refusal, raised deep in the scan and caught at the one boundary that turns
/// it into a `JsonlFault` — never seen by a caller.
exception internal JsonlScanFault of position: int * reason: JsonlFaultReason

/// One scanned JSONL line (Phase 296): its 1-based number, its text, and its top-level members as
/// raw spans (first-wins on a repeated key). Read it through the `OpStream.Jsonl` accessors.
type JsonlLine =
    internal
        { Number: int
          Text: string
          Fields: (string * string * int) list }

/// The bodies of the `OpStream` JSONL members (Phase 332): the one escaper, the record writer, THE
/// line scanner (`OpStream.Jsonl`) and the record readers built on it. Internal: a consumer reaches
/// each one through its forward in `OpStream` (OpStream.fs), which carries the member's contract
/// and documentation.
module internal OpStreamJsonl =
    open Fuaran.Core.OpStreamChain

    /// JSON string spelling for every line, snapshot, capture and envelope this package emits —
    /// the ONE escaper this package carries (`JsonString.quote`, the D2 copy of `Wire.Json.escape`,
    /// every control character as `\u00xx` since Phase 287). Fable-clean.
    let jstr (s: string) : string = JsonString.quote s

    /// One record line around the op's embedded encoding — the one line format both writers emit.
    let recordLine (r: OpRecord<'Op>) (opJson: string) : string =
        "{\"seq\":"
        + string r.Seq
        + ",\"actor\":"
        + Actor.encode r.Actor
        + ",\"op\":"
        + opJson
        + ",\"prevHash\":"
        + jstr r.PrevHash
        + ",\"hash\":"
        + jstr r.Hash
        + "}"

    let toJsonl (w: StreamWitness<'Op, 'State, 'Rej>) (records: OpRecord<'Op> list) : string =
        records
        |> List.map (fun r -> recordLine r (w.Encode r.Op))
        |> String.concat "\n"

    module Jsonl =

        let inline private isWs (c: char) =
            c = ' ' || c = '\t' || c = '\n' || c = '\r'

        let private fail (pos: int) (reason: JsonlFaultReason) : 'a = raise (JsonlScanFault(pos, reason))

        let inline private isHex (c: char) =
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')

        let inline private hexVal (c: char) =
            if c <= '9' then int c - int '0'
            elif c >= 'a' then int c - int 'a' + 10
            else int c - int 'A' + 10

        /// The code unit of the `\uXXXX` escape whose backslash is at `i`, or `-1` when fewer than
        /// four hex digits follow the `u`.
        let private unicodeAt (s: string) (i: int) : int =
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

        let private escapeText (s: string) (i: int) (len: int) = s.Substring(i, min len (s.Length - i))

        /// Index just past the string token whose opening quote is at `start`, every escape held to
        /// the JSON grammar: the eight single-letter escapes, and `\uXXXX` with four hex digits, a
        /// high surrogate followed at once by an escaped low one.
        let internal skipString (s: string) (start: int) : int =
            let n = s.Length
            let mutable i = start + 1
            let mutable fin = false

            while not fin do
                if i >= n then
                    fail start JsonlFaultReason.UnterminatedString

                match s.[i] with
                | '"' ->
                    i <- i + 1
                    fin <- true
                | '\\' ->
                    if i + 1 >= n then
                        fail start JsonlFaultReason.UnterminatedString

                    match s.[i + 1] with
                    | '"'
                    | '\\'
                    | '/'
                    | 'b'
                    | 'f'
                    | 'n'
                    | 'r'
                    | 't' -> i <- i + 2
                    | 'u' ->
                        let code = unicodeAt s i

                        if code < 0 then
                            fail i (JsonlFaultReason.InvalidEscape(escapeText s i 6))
                        elif code >= 0xD800 && code <= 0xDBFF then
                            let low =
                                if i + 6 < n && s.[i + 6] = '\\' then
                                    unicodeAt s (i + 6)
                                else
                                    -1

                            if low >= 0xDC00 && low <= 0xDFFF then
                                i <- i + 12
                            else
                                fail i (JsonlFaultReason.InvalidEscape(escapeText s i 12))
                        elif code >= 0xDC00 && code <= 0xDFFF then
                            fail i (JsonlFaultReason.InvalidEscape(escapeText s i 6))
                        else
                            i <- i + 6
                    | c -> fail i (JsonlFaultReason.InvalidEscape("\\" + string c))
                | _ -> i <- i + 1

            i

        /// Decode the string token `skipString` has already accepted in `s`, from its opening quote at
        /// `start` to just past its closing quote at `stop` (Phase 369: by runs, not characters). The
        /// token is valid, so nothing here refuses — every fault and its position is `skipString`'s.
        /// A body with no backslash is returned whole, by one `Substring`; otherwise each escape-free
        /// run is copied by one ranged `Append` and only the escapes are decoded one by one, exactly as
        /// before: a `\uXXXX` appends its code unit, so a surrogate pair spelled as two escapes (or a
        /// raw half beside an escaped one) is carried through unit for unit, across runs and escapes.
        let private decodeString (s: string) (start: int) (stop: int) : string =
            let first = start + 1
            // The closing quote: the body is `s.[first .. last - 1]`.
            let last = stop - 1

            // The index of the first backslash in `s.[from .. last - 1]`, or `last` when there is none.
            // A loop, not `IndexOf(char, int, int)`: Fable maps no count argument.
            let runEnd (from: int) =
                let mutable j = from

                while j < last && s.[j] <> '\\' do
                    j <- j + 1

                j

            let firstEscape = runEnd first

            if firstEscape >= last then
                s.Substring(first, last - first)
            else
                let sb = System.Text.StringBuilder(last - first)
                sb.Append(s, first, firstEscape - first) |> ignore
                // `i` is always at a backslash, or at `last`.
                let mutable i = firstEscape

                while i < last do
                    match s.[i + 1] with
                    | 'u' ->
                        sb.Append(char (unicodeAt s i)) |> ignore
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

                    if e > i then
                        sb.Append(s, i, e - i) |> ignore

                    i <- e

                sb.ToString()

        /// A bare value's token held to the literal grammar: `true`, `false`, `null`, or a JSON number
        /// (`-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?`).
        let private isLiteral (t: string) : bool =
            if t = "true" || t = "false" || t = "null" then
                true
            else
                let n = t.Length
                let mutable i = 0
                let digit k = k < n && t.[k] >= '0' && t.[k] <= '9'

                if i < n && t.[i] = '-' then
                    i <- i + 1

                let intStart = i

                if i < n && t.[i] = '0' then
                    i <- i + 1
                else
                    while digit i do
                        i <- i + 1

                let mutable ok = i > intStart

                if ok && i < n && t.[i] = '.' then
                    i <- i + 1
                    let fracStart = i

                    while digit i do
                        i <- i + 1

                    ok <- i > fracStart

                if ok && i < n && (t.[i] = 'e' || t.[i] = 'E') then
                    i <- i + 1

                    if i < n && (t.[i] = '+' || t.[i] = '-') then
                        i <- i + 1

                    let expStart = i

                    while digit i do
                        i <- i + 1

                    ok <- i > expStart

                ok && i = n

        /// Index just past the JSON value starting at `start` (no leading whitespace).
        let internal skipValue (s: string) (start: int) : int =
            let n = s.Length

            if start >= n then
                fail start JsonlFaultReason.Truncated

            match s.[start] with
            | '"' -> skipString s start
            | '{'
            | '[' ->
                let mutable i = start + 1
                let mutable depth = 1

                while depth > 0 do
                    if i >= n then
                        fail start JsonlFaultReason.UnterminatedContainer

                    match s.[i] with
                    | '"' -> i <- skipString s i
                    | '{'
                    | '[' ->
                        depth <- depth + 1
                        i <- i + 1
                    | '}'
                    | ']' ->
                        depth <- depth - 1
                        i <- i + 1
                    | _ -> i <- i + 1

                i
            | ','
            | '}'
            | ']' -> fail start JsonlFaultReason.MissingValue
            | _ ->
                let mutable i = start

                while i < n && not (let c = s.[i] in c = ',' || c = '}' || c = ']' || isWs c) do
                    i <- i + 1

                let token = s.Substring(start, i - start)

                if not (isLiteral token) then
                    fail start (JsonlFaultReason.InvalidLiteral token)

                i

        /// Will `raw`, embedded as a member value, read back as exactly `raw` (Phase 301)? It must
        /// carry no line break, start with no whitespace, and be one JSON value the scanner reads to
        /// its last character — the conditions under which the member's span is `raw` byte for byte.
        /// A line separator (U+2028 / U+2029) is NOT a break here: the canonical escaper emits it raw
        /// inside a string, and the reader splits on `\n` alone, so it round-trips.
        let checkRaw (raw: string) : Result<unit, JsonlWriteFaultReason> =
            let n = raw.Length
            let mutable brk = -1
            let mutable k = 0

            while brk < 0 && k < n do
                if raw.[k] = '\n' || raw.[k] = '\r' then
                    brk <- k

                k <- k + 1

            if brk >= 0 then
                Error(JsonlWriteFaultReason.MultiLine brk)
            elif n > 0 && isWs raw.[0] then
                Error JsonlWriteFaultReason.NotTrimStable
            else
                try
                    let e = skipValue raw 0

                    if e = n then
                        Ok()
                    else
                        let mutable allWs = true

                        for j in e .. n - 1 do
                            if not (isWs raw.[j]) then
                                allWs <- false

                        if allWs then
                            Error JsonlWriteFaultReason.NotTrimStable
                        else
                            Error(JsonlWriteFaultReason.Unreadable JsonlFaultReason.TrailingContent)
                with JsonlScanFault(_, r) ->
                    Error(JsonlWriteFaultReason.Unreadable r)

        /// The members of the flat object `s` holds — `(key, raw value, value position)`, first-wins
        /// on a repeated key (Phase 45: the first-wins `JVal` decoders and this scanner agree on which
        /// value a repeated key resolves to). Positions are offsets into `s` plus `offset`.
        let private membersOf (offset: int) (s: string) : (string * string * int) list =
            let n = s.Length
            let mutable i = 0

            let skipWs () =
                while i < n && isWs s.[i] do
                    i <- i + 1

            let at k = offset + k
            skipWs ()

            if i >= n || s.[i] <> '{' then
                fail (at i) JsonlFaultReason.NotAnObject

            i <- i + 1
            skipWs ()
            let fields = ResizeArray<string * string * int>()

            if i >= n then
                fail (at i) JsonlFaultReason.Truncated

            if s.[i] = '}' then
                i <- i + 1
            else
                let mutable go = true

                while go do
                    skipWs ()

                    if i >= n then
                        fail (at i) JsonlFaultReason.Truncated

                    if s.[i] <> '"' then
                        fail (at i) JsonlFaultReason.ExpectedKey

                    let ks =
                        try
                            skipString s i
                        with JsonlScanFault(p, r) ->
                            fail (at p) r

                    let key = decodeString s i ks
                    i <- ks
                    skipWs ()

                    if i >= n then
                        fail (at i) JsonlFaultReason.Truncated

                    if s.[i] <> ':' then
                        fail (at i) JsonlFaultReason.ExpectedColon

                    i <- i + 1
                    skipWs ()

                    if i >= n then
                        fail (at i) JsonlFaultReason.Truncated

                    let vs =
                        try
                            skipValue s i
                        with JsonlScanFault(p, r) ->
                            fail (at p) r

                    fields.Add((key, s.Substring(i, vs - i), at i))
                    i <- vs
                    skipWs ()

                    if i >= n then
                        fail (at i) JsonlFaultReason.Truncated

                    if s.[i] = ',' then
                        i <- i + 1
                    elif s.[i] = '}' then
                        i <- i + 1
                        go <- false
                    else
                        fail (at i) JsonlFaultReason.ExpectedCommaOrBrace

            skipWs ()

            if i < n then
                fail (at i) JsonlFaultReason.TrailingContent

            let seen = System.Collections.Generic.HashSet<string>()

            [ for (k, v, p) in fields do
                  if seen.Add k then
                      yield (k, v, p) ]

        let private faultAt (line: int) (pos: int) (reason: JsonlFaultReason) : JsonlFault =
            { Line = line
              Position = pos
              Reason = reason }

        let parseLine (number: int) (text: string) : Result<JsonlLine, JsonlFault> =
            try
                Ok
                    { Number = number
                      Text = text
                      Fields = membersOf 0 text }
            with JsonlScanFault(p, r) ->
                Error(faultAt number p r)

        let topFields (line: string) : Result<(string * string) list, JsonlFault> =
            parseLine 1 line
            |> Result.map (fun l -> l.Fields |> List.map (fun (k, v, _) -> k, v))

        let rawSpan (field: string) (line: string) : Result<string option, JsonlFault> =
            parseLine 1 line
            |> Result.map (fun l -> l.Fields |> List.tryPick (fun (k, v, _) -> if k = field then Some v else None))

        let unquote (raw: string) : Result<string, JsonlFaultReason> =
            if raw.Length < 2 || raw.[0] <> '"' then
                Error(JsonlFaultReason.ExpectedString "")
            else
                try
                    if skipString raw 0 = raw.Length then
                        Ok(decodeString raw 0 raw.Length)
                    else
                        Error(JsonlFaultReason.ExpectedString "")
                with JsonlScanFault(_, r) ->
                    Error r

        let lineNumber (line: JsonlLine) : int = line.Number

        let lineText (line: JsonlLine) : string = line.Text

        let refuse (line: JsonlLine) (reason: string) : JsonlFault =
            faultAt line.Number 0 (JsonlFaultReason.Refused reason)

        let private memberOf (key: string) (line: JsonlLine) : (string * int) option =
            line.Fields
            |> List.tryPick (fun (k, v, p) -> if k = key then Some(v, p) else None)

        let tryRawField (key: string) (line: JsonlLine) : string option = memberOf key line |> Option.map fst

        let rawField (key: string) (line: JsonlLine) : Result<string, JsonlFault> =
            match memberOf key line with
            | Some(v, _) -> Ok v
            | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))

        let stringField (key: string) (line: JsonlLine) : Result<string, JsonlFault> =
            match memberOf key line with
            | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))
            | Some(v, p) ->
                match unquote v with
                | Ok s -> Ok s
                | Error(JsonlFaultReason.ExpectedString _) ->
                    Error(faultAt line.Number p (JsonlFaultReason.ExpectedString key))
                | Error r -> Error(faultAt line.Number p r)

        let intField (key: string) (line: JsonlLine) : Result<int, JsonlFault> =
            match memberOf key line with
            | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))
            | Some(v, p) ->
                let digits = if v.StartsWith "-" then v.Substring 1 else v

                let grammatical =
                    digits.Length > 0
                    && digits.Length <= 10
                    && Seq.forall (fun c -> c >= '0' && c <= '9') digits
                    && (digits = "0" || digits.[0] <> '0')

                // Read from the DIGITS, never through a host number reader (Phase 306). The
                // `System.Int64.Parse v` this replaces read under the CURRENT culture: under one
                // whose negative sign is not U+002D (fa-IR, he-IL) it threw on `-5` — a token the
                // grammar test above had just accepted — out of a function that returns a `Result`.
                // At most ten digits, so the fold cannot leave int64.
                let value =
                    if grammatical then
                        let magnitude =
                            digits |> Seq.fold (fun acc c -> acc * 10L + int64 (int c - int '0')) 0L

                        if v.StartsWith "-" then -magnitude else magnitude
                    else
                        0L

                if
                    grammatical
                    && value >= int64 System.Int32.MinValue
                    && value <= int64 System.Int32.MaxValue
                then
                    Ok(int value)
                else
                    Error(faultAt line.Number p (JsonlFaultReason.ExpectedInteger key))

        let stringsField (key: string) (line: JsonlLine) : Result<string list, JsonlFault> =
            match memberOf key line with
            | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))
            | Some(v, p) ->
                let bad () =
                    Error(faultAt line.Number p (JsonlFaultReason.ExpectedStringArray key))

                let n = v.Length

                if n < 2 || v.[0] <> '[' || v.[n - 1] <> ']' then
                    bad ()
                else
                    let items = ResizeArray<string>()
                    let mutable i = 1
                    let mutable ok = true
                    let mutable expectItem = true

                    let skipWs () =
                        while i < n - 1 && isWs v.[i] do
                            i <- i + 1

                    skipWs ()

                    if i = n - 1 then
                        Ok []
                    else
                        while ok && i < n - 1 do
                            skipWs ()

                            if expectItem then
                                if i < n - 1 && v.[i] = '"' then
                                    let e =
                                        try
                                            skipString v i
                                        with JsonlScanFault _ ->
                                            n

                                    items.Add(decodeString v i e)
                                    i <- e
                                    expectItem <- false
                                else
                                    ok <- false
                            elif v.[i] = ',' then
                                i <- i + 1
                                expectItem <- true
                            else
                                ok <- false

                            skipWs ()

                        if ok && not expectItem then
                            Ok(List.ofSeq items)
                        else
                            bad ()

        let actorField (key: string) (line: JsonlLine) : Result<Actor, JsonlFault> =
            match memberOf key line with
            | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))
            | Some(v, p) ->
                try
                    let inner =
                        { Number = line.Number
                          Text = v
                          Fields = membersOf p v }

                    let str k =
                        match stringField k inner with
                        | Ok s -> s
                        | Error f ->
                            let reason =
                                match f.Reason with
                                | JsonlFaultReason.MissingField m -> JsonlFaultReason.MissingField(key + "." + m)
                                | JsonlFaultReason.ExpectedString m -> JsonlFaultReason.ExpectedString(key + "." + m)
                                | r -> r

                            fail f.Position reason

                    // Phase 315 — an actor that names nobody is refused here, as `Actor.validate`
                    // refuses it at construction, so a store cannot read back an anonymous author.
                    let named (a: Actor) =
                        Actor.validate a
                        |> Result.mapError (fun why -> faultAt line.Number p (JsonlFaultReason.ActorInvalid(key, why)))

                    match tryRawField "kind" inner with
                    | None -> Error(refuse line "the actor carries no kind")
                    | Some _ ->
                        match str "kind" with
                        | "human" -> named (Human(str "id"))
                        | "agent" -> named (Agent(str "model", str "version", str "id"))
                        | kind -> Error(refuse line (sprintf "unknown actor kind \"%s\"" kind))
                with JsonlScanFault(pos, r) ->
                    Error(faultAt line.Number pos r)

        let scanRecords (decode: JsonlLine -> Result<'T, JsonlFault>) (text: string) : Result<'T list, JsonlFault> =
            let lines = text.Replace("\r\n", "\n").Split('\n')
            let acc = ResizeArray<'T>()
            let mutable fault = None
            let mutable k = 0

            while fault.IsNone && k < lines.Length do
                let text = lines.[k]

                if text.Trim() <> "" then
                    match parseLine (k + 1) text |> Result.bind decode with
                    | Ok v -> acc.Add v
                    | Error f -> fault <- Some f

                k <- k + 1

            match fault with
            | Some f -> Error f
            | None -> Ok(List.ofSeq acc)

    let inline bindR ([<InlineIfLambda>] f: 'a -> Result<'b, 'e>) (r: Result<'a, 'e>) = Result.bind f r

    /// A single-object reader's refusal (a snapshot line, an attribution envelope) — the reason and
    /// the position, without a line number the caller never had.
    let spanFault (f: JsonlFault) : string =
        sprintf "%s (position %d)" (JsonlFault.reasonText f.Reason) f.Position

    /// Write one line per item, each from its embedded span (Phase 301): every span is checked
    /// (`Jsonl.checkRaw`) before any line is built, and the first refused one is the result — by its
    /// 1-based line and the member that would have carried it. On `Ok` the text is the one the
    /// unchecked writer produces, byte for byte.
    let checkedLines
        (memberName: string)
        (spanOf: 'T -> string)
        (lineOf: 'T -> string -> string)
        (items: 'T list)
        : Result<string, JsonlWriteFault> =
        let lines = ResizeArray<string>()

        let rec go (i: int) =
            function
            | [] -> Ok(String.concat "\n" lines)
            | (x: 'T) :: rest ->
                let span = spanOf x

                match Jsonl.checkRaw span with
                | Error reason ->
                    Error
                        { Line = i + 1
                          Member = memberName
                          Reason = reason }
                | Ok() ->
                    lines.Add(lineOf x span)
                    go (i + 1) rest

        go 0 items

    let tryToJsonl
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : Result<string, JsonlWriteFault> =
        checkedLines "op" (fun (r: OpRecord<'Op>) -> w.Encode r.Op) recordLine records

    /// One record line, decoded — the members in line order, the `op` span handed to the witness.
    let private recordOf
        (actorOf: JsonlLine -> Result<Actor, JsonlFault>)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (line: JsonlLine)
        : Result<OpRecord<'Op>, JsonlFault> =
        Jsonl.intField "seq" line
        |> bindR (fun seq ->
            actorOf line
            |> bindR (fun actor ->
                Jsonl.rawField "op" line
                |> bindR (fun raw -> w.Decode raw |> Result.mapError (Jsonl.refuse line))
                |> bindR (fun op ->
                    Jsonl.stringField "prevHash" line
                    |> bindR (fun prevHash ->
                        Jsonl.stringField "hash" line
                        |> Result.map (fun hash ->
                            { Seq = seq
                              Actor = actor
                              Op = op
                              PrevHash = prevHash
                              Hash = hash })))))

    /// Is this line a snapshot line? One carrying `"snapshot":true` and NO `op` — a record line that
    /// happens to carry a `snapshot` member is a record (Phase 296), not a snapshot dropped on the
    /// floor.
    let private isSnapshotLine (line: JsonlLine) : bool =
        Option.isNone (Jsonl.tryRawField "op" line)
        && Jsonl.tryRawField "snapshot" line = Some "true"

    /// The records-and-snapshot reader, parameterised on how the `actor` member decodes — the
    /// canonical typed object, or the legacy bare string. One snapshot line is admitted, and only as
    /// the first line of the stream (Phase 296); a second, or one after a record, is refused.
    let private scanJsonlWithSnapshots
        (actorOf: JsonlLine -> Result<Actor, JsonlFault>)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list * string list, JsonlFault> =
        text
        |> Jsonl.scanRecords (fun line ->
            if isSnapshotLine line then
                Ok(Choice2Of2 line)
            else
                recordOf actorOf w line |> Result.map Choice1Of2)
        |> bindR (fun items ->
            let rec go (first: bool) recs snaps =
                function
                | [] -> Ok(List.rev recs, List.rev snaps)
                | Choice1Of2 r :: rest -> go false (r :: recs) snaps rest
                | Choice2Of2(l: JsonlLine) :: rest ->
                    if first then
                        go false recs [ Jsonl.lineText l ] rest
                    else
                        Error
                            { Line = Jsonl.lineNumber l
                              Position = 0
                              Reason = JsonlFaultReason.SnapshotNotAtHead }

            go true [] [] items)

    let fromJsonlWithSnapshots
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list * string list, string> =
        scanJsonlWithSnapshots (Jsonl.actorField "actor") w text
        |> Result.mapError JsonlFault.toString

    let fromJsonl (w: StreamWitness<'Op, 'State, 'Rej>) (text: string) : Result<OpRecord<'Op> list, string> =
        fromJsonlWithSnapshots w text |> Result.map fst

    let fromJsonlLegacyActor (w: StreamWitness<'Op, 'State, 'Rej>) (text: string) : Result<OpRecord<'Op> list, string> =
        scanJsonlWithSnapshots (fun line -> Jsonl.stringField "actor" line |> Result.map Actor.ofLegacyString) w text
        |> Result.map fst
        |> Result.mapError JsonlFault.toString

    let fromJsonlVerified
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list, string> =
        fromJsonl w text
        |> Result.bind (fun recs ->
            match firstChainBreak hashFn w recs with
            | None -> Ok recs
            | Some b ->
                Error(
                    sprintf
                        "OpStream.fromJsonlVerified: chain breaks at record %d — %s"
                        b.Index
                        (ChainBreakReason.toString b.Reason)
                ))
