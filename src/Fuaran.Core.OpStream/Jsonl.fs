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
    /// An array value's item is not followed by `,` or `]` (Phase 466: a container value is read by
    /// the grammar at every depth). The four Phase-466 cases follow every earlier one, so each
    /// existing case keeps its tag.
    | ExpectedCommaOrBracket
    /// A member key read twice in one object (Phase 466, the strict grammar's rule, DECISIONS.md
    /// D157) — at the repeated key's opening quote, once its value is read. `path` is the repeated
    /// member's, spelled as `DecodePath.render` spells one (`$["op"]["id"]`). Keys are compared
    /// after unescaping.
    | RepeatedKey of path: string
    /// A raw control character (U+0000 to U+001F) inside a string (Phase 466) — at the character.
    /// `path` is the member or item the string is in; for a key, the object that holds it.
    | RawControl of path: string
    /// A raw surrogate code unit that is not half of a high-low pair (Phase 466) — at the unit.
    /// `path` as for `RawControl`. An unpaired surrogate ESCAPE is `InvalidEscape`, as it always was.
    | LoneSurrogate of path: string

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
        | JsonlFaultReason.ExpectedCommaOrBracket -> "expected ',' or ']'"
        | JsonlFaultReason.RepeatedKey path -> "repeated key at " + path
        | JsonlFaultReason.RawControl path -> "unescaped control character in a string at " + path
        | JsonlFaultReason.LoneSurrogate path -> "unpaired surrogate in a string at " + path

    /// `line N: <reason> (position P)` — the `line N:` prefix every JSONL reader's `Error` string
    /// has carried since Phase 252, now 1-based over all lines.
    let toString (f: JsonlFault) : string =
        sprintf "line %d: %s (position %d)" f.Line (reasonText f.Reason) f.Position

/// One stored op a stream load read but its witness could not decode (Phase 416): where it sits and
/// what the witness said. Every other part of its line parsed, and the op's text is exactly what the
/// store holds.
type UndecodableOp =
    {
        /// The lane whose file holds the op, or `None` for a load of one text.
        Lane: string option
        /// 1-based line of that text, counted over every line, blank lines included — what an editor
        /// shows.
        Line: int
        /// The content id the store gives the op: a DAG node's id, or a linear record's stored `hash`.
        NodeId: string
        /// The witness's own `Decode` error, verbatim.
        Reason: string
    }

/// Why a stream load refused (Phase 416) — the one answer every JSONL op-stream load in
/// `Fuaran.Core.OpStream` and `Fuaran.Core.OpStream.Dag` gives, where each used to give a string.
/// `'Break` is the integrity fault of the stream the load reads: `ChainBreak` for a linear stream,
/// `DagBreak` for a DAG, `Dag.LaneBreak` for a lane store. A structural load (one that does not
/// verify) never answers `Broken`.
///
/// The cases separate the three things a host must tell apart. `Unreadable`, `DuplicateLane`,
/// `Collision` and `UnreadableCheckpoint` say the bytes are not a store. `Broken` says they are a
/// store that does not verify: damage. `Undecodable` says every line parsed, every content id and
/// parent verified (on a verifying load), and the only thing wrong is that the witness could not
/// decode some ops: the signature of a store written by a newer host, whose op vocabulary this build
/// predates. A load answers the first that holds in that order, so a decode failure in a store that
/// is also damaged is not reported beside the damage.
[<RequireQualifiedAccess>]
type StreamLoadFault<'Break> =
    /// A line the one scanner, or the record reader over it, refused: the lane whose text holds it
    /// (`None` for a load of one text) and the fault, by its line.
    | Unreadable of lane: string option * fault: JsonlFault
    /// The stream parsed and does not verify: the first break, exactly as the stream's own walker
    /// reports it.
    | Broken of 'Break
    /// The stream parsed (and, on a verifying load, verified) and the witness refused one or more ops.
    /// EVERY such op, in lane and then line order.
    | Undecodable of sites: UndecodableOp list
    /// Two of the lane texts handed to a lane load carry one lane id.
    | DuplicateLane of lane: string
    /// Two lanes hold one content id for different content (parents modulo order, actor or op): the
    /// node and the two lanes, sorted ordinally.
    | Collision of nodeId: string * lanes: string list
    /// A line of a DAG's checkpoint sidecar was refused: the fault, by its sidecar line.
    | UnreadableCheckpoint of fault: JsonlFault

/// Render a `StreamLoadFault` (Phase 416). Each case renders as the string the load answered before
/// the fault was typed, where it answered one, so a host that printed the message keeps its bytes.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module StreamLoadFault =

    /// One undecodable op as `line N: <the witness's reason> (position 0)` — the bytes a JSONL reader
    /// gave a witness decode `Error` before Phase 416 — prefixed `lane L: ` when it has a lane.
    let siteText (s: UndecodableOp) : string =
        let at = sprintf "line %d: %s (position 0)" s.Line s.Reason

        match s.Lane with
        | Some lane -> "lane " + lane + ": " + at
        | None -> at

    /// The fault as text, with `renderBreak` rendering a `Broken` stream's break. A load of one text
    /// renders `Unreadable` as `JsonlFault.toString` and a single undecodable op as `siteText`, the
    /// pre-416 bytes; several undecodable ops are their `siteText`s joined by `; `.
    let toStringWith (renderBreak: 'Break -> string) (f: StreamLoadFault<'Break>) : string =
        match f with
        | StreamLoadFault.Unreadable(None, fault) -> JsonlFault.toString fault
        | StreamLoadFault.Unreadable(Some lane, fault) -> "lane " + lane + ": " + JsonlFault.toString fault
        | StreamLoadFault.Broken b -> renderBreak b
        | StreamLoadFault.Undecodable sites -> sites |> List.map siteText |> String.concat "; "
        | StreamLoadFault.DuplicateLane lane -> "lane " + lane + " is given twice"
        | StreamLoadFault.Collision(nodeId, lanes) ->
            "node "
            + nodeId
            + " is held with different content by lanes "
            + String.concat ", " lanes
        | StreamLoadFault.UnreadableCheckpoint fault -> "checkpoint sidecar: " + JsonlFault.toString fault

    /// A linear stream load's fault as text — `OpStream.fromJsonlVerified`'s message for a break,
    /// `OpStream.fromJsonlVerified: chain breaks at record N — <reason>`, byte for byte as before
    /// Phase 416.
    let toString (f: StreamLoadFault<ChainBreak>) : string =
        toStringWith
            (fun (b: ChainBreak) ->
                sprintf
                    "OpStream.fromJsonlVerified: chain breaks at record %d — %s"
                    b.Index
                    (ChainBreakReason.toString b.Reason))
            f

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
        /// The member refused: an embedded span (`op`, `value`, `state`) or, since Phase 473, a string
        /// the writer quotes itself (`actor`, `prevHash`, `hash`, `eff`, `det`, `id`, `parents`).
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

/// One step of the path a Phase-466 refusal names: a member by its decoded key, an item by its
/// zero-based index.
[<RequireQualifiedAccess>]
type internal JsonlSeg =
    | Key of string
    | Index of int

/// One open container of the structural walk (Phase 466): an object's keys so far and the member
/// being read (its key and the key's opening quote), or an array's current index. `Seen` is an
/// object's only; an array's is never read.
type internal JsonlFrame =
    { IsObject: bool
      Seen: System.Collections.Generic.HashSet<string>
      mutable Key: string
      mutable KeyAt: int
      mutable Index: int }

/// One scanned JSONL line (Phase 296): its 1-based number, its text, and its top-level members as
/// raw spans, every key once (Phase 466). Read it through the `OpStream.Jsonl` accessors.
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
                && s[i + 1] = 'u'
                && isHex s[i + 2]
                && isHex s[i + 3]
                && isHex s[i + 4]
                && isHex s[i + 5]
            then
                (hexVal s[i + 2] <<< 12)
                + (hexVal s[i + 3] <<< 8)
                + (hexVal s[i + 4] <<< 4)
                + hexVal s[i + 5]
            else
                -1

        let private escapeText (s: string) (i: int) (len: int) = s.Substring(i, min len (s.Length - i))

        let inline private isHigh (u: int) = u >= 0xD800 && u <= 0xDBFF

        let inline private isLow (u: int) = u >= 0xDC00 && u <= 0xDFFF

        let inline private isShortEscape (c: char) =
            c = '"'
            || c = '\\'
            || c = '/'
            || c = 'b'
            || c = 'f'
            || c = 'n'
            || c = 'r'
            || c = 't'

        /// Index just past the string token whose opening quote is at `start`, every escape held to
        /// the JSON grammar (the eight single-letter escapes, and `\uXXXX` with four hex digits) and
        /// the string held to well-formed UTF-16 with no raw control (Phase 466, the strict grammar's
        /// rule, D157). A high surrogate is followed at once by a low one, whichever spelling each half
        /// arrived in; a pair split between a raw unit and an escape is read. An unpaired ESCAPE is
        /// `InvalidEscape` at its backslash (its six characters, or twelve for a high one), as it always
        /// was. An unpaired RAW unit is `LoneSurrogate` at the unit, once what follows a raw high one is
        /// known — the end of the line is `UnterminatedString`, a raw control its own refusal, an escape
        /// the grammar does not have that escape's. A raw U+0000 to U+001F is `RawControl` at the
        /// character. `path` names the member or item the string is in; it is rendered only for a
        /// refusal. `proofs/OpStreamLoad.fst`'s `string_tail` is this function clause for clause.
        let internal skipString (s: string) (start: int) (path: unit -> string) : int =
            let n = s.Length
            let mutable i = start + 1
            let mutable fin = false

            while not fin do
                if i >= n then
                    fail start JsonlFaultReason.UnterminatedString

                let c = s[i]
                let u = int c

                if c = '"' then
                    i <- i + 1
                    fin <- true
                elif c = '\\' then
                    if i + 1 >= n then
                        fail start JsonlFaultReason.UnterminatedString

                    let e = s[i + 1]

                    if isShortEscape e then
                        i <- i + 2
                    elif e = 'u' then
                        let code = unicodeAt s i

                        if code < 0 then
                            fail i (JsonlFaultReason.InvalidEscape(escapeText s i 6))
                        elif isHigh code then
                            if i + 6 < n && s[i + 6] = '\\' then
                                if isLow (unicodeAt s (i + 6)) then
                                    i <- i + 12
                                else
                                    fail i (JsonlFaultReason.InvalidEscape(escapeText s i 12))
                            elif i + 6 < n && isLow (int s[i + 6]) then
                                i <- i + 7
                            else
                                fail i (JsonlFaultReason.InvalidEscape(escapeText s i 12))
                        elif isLow code then
                            fail i (JsonlFaultReason.InvalidEscape(escapeText s i 6))
                        else
                            i <- i + 6
                    else
                        fail i (JsonlFaultReason.InvalidEscape("\\" + string e))
                elif u < 0x20 then
                    fail i (JsonlFaultReason.RawControl(path ()))
                elif isHigh u then
                    if i + 1 >= n then
                        fail start JsonlFaultReason.UnterminatedString

                    let d = s[i + 1]

                    if isLow (int d) then
                        i <- i + 2
                    elif d = '\\' then
                        if i + 2 >= n then
                            fail start JsonlFaultReason.UnterminatedString

                        let e = s[i + 2]

                        if e = 'u' then
                            let code = unicodeAt s (i + 1)

                            if code < 0 then
                                fail (i + 1) (JsonlFaultReason.InvalidEscape(escapeText s (i + 1) 6))
                            elif isLow code then
                                i <- i + 7
                            else
                                fail i (JsonlFaultReason.LoneSurrogate(path ()))
                        elif isShortEscape e then
                            fail i (JsonlFaultReason.LoneSurrogate(path ()))
                        else
                            fail (i + 1) (JsonlFaultReason.InvalidEscape("\\" + string e))
                    elif int d < 0x20 then
                        fail (i + 1) (JsonlFaultReason.RawControl(path ()))
                    else
                        fail i (JsonlFaultReason.LoneSurrogate(path ()))
                elif isLow u then
                    fail i (JsonlFaultReason.LoneSurrogate(path ()))
                else
                    i <- i + 1

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
            // The closing quote: the body is `s[first .. last - 1]`.
            let last = stop - 1

            // The index of the first backslash in `s[from .. last - 1]`, or `last` when there is none.
            // A loop, not `IndexOf(char, int, int)`: Fable maps no count argument.
            let runEnd (from: int) =
                let mutable j = from

                while j < last && s[j] <> '\\' do
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
                    match s[i + 1] with
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
                let digit k = k < n && t[k] >= '0' && t[k] <= '9'

                if i < n && t[i] = '-' then
                    i <- i + 1

                let intStart = i

                if i < n && t[i] = '0' then
                    i <- i + 1
                else
                    while digit i do
                        i <- i + 1

                let mutable ok = i > intStart

                if ok && i < n && t[i] = '.' then
                    i <- i + 1
                    let fracStart = i

                    while digit i do
                        i <- i + 1

                    ok <- i > fracStart

                if ok && i < n && (t[i] = 'e' || t[i] = 'E') then
                    i <- i + 1

                    if i < n && (t[i] = '+' || t[i] = '-') then
                        i <- i + 1

                    let expStart = i

                    while digit i do
                        i <- i + 1

                    ok <- i > expStart

                ok && i = n

        /// Index just past the bare value at `start`: the token up to `,`, `}`, `]` or whitespace,
        /// held to the literal grammar.
        let private skipLiteral (s: string) (start: int) : int =
            let n = s.Length
            let mutable i = start

            while i < n && not (let c = s[i] in c = ',' || c = '}' || c = ']' || isWs c) do
                i <- i + 1

            let token = s.Substring(start, i - start)

            if not (isLiteral token) then
                fail start (JsonlFaultReason.InvalidLiteral token)

            i

        /// A path as `DecodePath.render` spells one (Phase 466): `$`, then `["key"]` (the key under
        /// `JsonString.quote`) or `[i]` per step — the `prefix`, then the first `count` open frames,
        /// each at its current member or item.
        let private renderPath (prefix: JsonlSeg list) (frames: ResizeArray<JsonlFrame>) (count: int) : string =
            let sb = System.Text.StringBuilder("$")

            let step (seg: JsonlSeg) =
                match seg with
                | JsonlSeg.Key k -> sb.Append('[').Append(JsonString.quote k).Append(']') |> ignore
                | JsonlSeg.Index i -> sb.Append('[').Append(string i).Append(']') |> ignore

            for seg in prefix do
                step seg

            for j in 0 .. count - 1 do
                let f = frames[j]

                step (
                    if f.IsObject then
                        JsonlSeg.Key f.Key
                    else
                        JsonlSeg.Index f.Index
                )

            sb.ToString()

        let private rootPath () = "$"

        /// Index just past the object or array value at `start`, read by the grammar at every depth
        /// (Phase 466; until then a container value was balanced and its strings checked, and the
        /// rest of its grammar was the decoder's): each object's members and each array's items,
        /// their separators, every string and bare literal, and each object's keys pairwise distinct
        /// after unescaping — a repeated key is refused at its opening quote once its value is read,
        /// so a refusal inside the value wins (`Wire.Json`'s order, D157.2). A line ending inside the
        /// value is `UnterminatedContainer` at `start`, the outermost opening, as the balanced walk
        /// answered. The walk keeps an explicit stack of open containers, so no nesting depth
        /// exhausts the call stack. `proofs/OpStreamLoad.fst`'s `skip_val` / `obj_members` /
        /// `arr_items` are this walk, the stack as their recursion.
        let private skipContainer (s: string) (start: int) (prefix: JsonlSeg list) : int =
            let n = s.Length
            let frames = ResizeArray<JsonlFrame>()
            // The path of a value in the innermost container, and of a key (the container's own).
            let valuePath () = renderPath prefix frames frames.Count

            let keyPath () =
                renderPath prefix frames (frames.Count - 1)

            let mutable i = start
            let mutable result = -1
            let mutable first = true
            // 0: a member is due; 1: an item is due; 2: a value has just ended at `i`;
            // 3: a value is due at `i` (no whitespace before it); 4: open the container at `i`;
            // 5: the innermost container's close was consumed.
            let mutable state = 4

            let skipWs () =
                while i < n && isWs s[i] do
                    i <- i + 1

            while result < 0 do
                match state with
                | 4 ->
                    let isObject = s[i] = '{'

                    frames.Add
                        { IsObject = isObject
                          Seen =
                            if isObject then
                                System.Collections.Generic.HashSet<string>()
                            else
                                Unchecked.defaultof<_>
                          Key = null
                          KeyAt = 0
                          Index = 0 }

                    i <- i + 1
                    first <- true
                    state <- if isObject then 0 else 1
                | 0 ->
                    skipWs ()

                    if i >= n then
                        fail start JsonlFaultReason.UnterminatedContainer

                    match s[i] with
                    | '}' when first ->
                        i <- i + 1
                        state <- 5
                    | '"' ->
                        let top = frames[frames.Count - 1]
                        let e = skipString s i keyPath
                        top.Key <- decodeString s i e
                        top.KeyAt <- i
                        i <- e
                        skipWs ()

                        if i >= n then
                            fail start JsonlFaultReason.UnterminatedContainer

                        if s[i] <> ':' then
                            fail i JsonlFaultReason.ExpectedColon

                        i <- i + 1
                        skipWs ()

                        if i >= n then
                            fail start JsonlFaultReason.UnterminatedContainer

                        state <- 3
                    | _ -> fail i JsonlFaultReason.ExpectedKey
                | 1 ->
                    skipWs ()

                    if i >= n then
                        fail start JsonlFaultReason.UnterminatedContainer

                    if s[i] = ']' then
                        if first then
                            i <- i + 1
                            state <- 5
                        else
                            fail i JsonlFaultReason.MissingValue
                    else
                        state <- 3
                | 3 ->
                    match s[i] with
                    | '"' ->
                        i <- skipString s i valuePath
                        state <- 2
                    | '{'
                    | '[' -> state <- 4
                    | ','
                    | '}'
                    | ']' -> fail i JsonlFaultReason.MissingValue
                    | _ ->
                        i <- skipLiteral s i
                        state <- 2
                | 5 ->
                    frames.RemoveAt(frames.Count - 1)

                    if frames.Count = 0 then result <- i else state <- 2
                | _ ->
                    let top = frames[frames.Count - 1]

                    if top.IsObject && top.Seen.Contains top.Key then
                        fail top.KeyAt (JsonlFaultReason.RepeatedKey(valuePath ()))

                    skipWs ()

                    if i >= n then
                        fail start JsonlFaultReason.UnterminatedContainer

                    match s[i] with
                    | ',' ->
                        if top.IsObject then
                            top.Seen.Add top.Key |> ignore
                        else
                            top.Index <- top.Index + 1

                        i <- i + 1
                        first <- false
                        state <- if top.IsObject then 0 else 1
                    | '}' when top.IsObject ->
                        i <- i + 1
                        state <- 5
                    | ']' when not top.IsObject ->
                        i <- i + 1
                        state <- 5
                    | _ ->
                        fail
                            i
                            (if top.IsObject then
                                 JsonlFaultReason.ExpectedCommaOrBrace
                             else
                                 JsonlFaultReason.ExpectedCommaOrBracket)

            result

        /// Index just past the JSON value starting at `start` (no leading whitespace), read by the
        /// grammar at every depth (Phase 466). `prefix` is the value's path.
        let internal skipValue (s: string) (start: int) (prefix: JsonlSeg list) : int =
            let n = s.Length

            if start >= n then
                fail start JsonlFaultReason.Truncated

            match s[start] with
            | '"' -> skipString s start (fun () -> renderPath prefix Unchecked.defaultof<_> 0)
            | '{'
            | '[' -> skipContainer s start prefix
            | ','
            | '}'
            | ']' -> fail start JsonlFaultReason.MissingValue
            | _ -> skipLiteral s start

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
                if raw[k] = '\n' || raw[k] = '\r' then
                    brk <- k

                k <- k + 1

            if brk >= 0 then
                Error(JsonlWriteFaultReason.MultiLine brk)
            elif n > 0 && isWs raw[0] then
                Error JsonlWriteFaultReason.NotTrimStable
            else
                try
                    let e = skipValue raw 0 []

                    if e = n then
                        Ok()
                    else
                        let mutable allWs = true

                        for j in e .. n - 1 do
                            if not (isWs raw[j]) then
                                allWs <- false

                        if allWs then
                            Error JsonlWriteFaultReason.NotTrimStable
                        else
                            Error(JsonlWriteFaultReason.Unreadable JsonlFaultReason.TrailingContent)
                with JsonlScanFault(_, r) ->
                    Error(JsonlWriteFaultReason.Unreadable r)

        /// The members of the flat object `s` holds — `(key, raw value, value position)`, in line
        /// order. A key read twice is refused (`RepeatedKey`) at its opening quote once its value is
        /// read (Phase 466: until then the first won, Phase 45, and the Wire grammar now refuses it
        /// too, D157). Positions are offsets into `s` plus `offset`.
        let private membersOf (offset: int) (s: string) : (string * string * int) list =
            let n = s.Length
            let mutable i = 0

            let skipWs () =
                while i < n && isWs s[i] do
                    i <- i + 1

            let at k = offset + k
            skipWs ()

            if i >= n || s[i] <> '{' then
                fail (at i) JsonlFaultReason.NotAnObject

            i <- i + 1
            skipWs ()
            let fields = ResizeArray<string * string * int>()
            let seen = System.Collections.Generic.HashSet<string>()

            if i >= n then
                fail (at i) JsonlFaultReason.Truncated

            if s[i] = '}' then
                i <- i + 1
            else
                let mutable go = true

                while go do
                    skipWs ()

                    if i >= n then
                        fail (at i) JsonlFaultReason.Truncated

                    if s[i] <> '"' then
                        fail (at i) JsonlFaultReason.ExpectedKey

                    let keyAt = i

                    let ks =
                        try
                            skipString s i rootPath
                        with JsonlScanFault(p, r) ->
                            fail (at p) r

                    let key = decodeString s i ks
                    i <- ks
                    skipWs ()

                    if i >= n then
                        fail (at i) JsonlFaultReason.Truncated

                    if s[i] <> ':' then
                        fail (at i) JsonlFaultReason.ExpectedColon

                    i <- i + 1
                    skipWs ()

                    if i >= n then
                        fail (at i) JsonlFaultReason.Truncated

                    let vs =
                        try
                            skipValue s i [ JsonlSeg.Key key ]
                        with JsonlScanFault(p, r) ->
                            fail (at p) r

                    if not (seen.Add key) then
                        fail
                            (at keyAt)
                            (JsonlFaultReason.RepeatedKey(renderPath [ JsonlSeg.Key key ] Unchecked.defaultof<_> 0))

                    fields.Add((key, s.Substring(i, vs - i), at i))
                    i <- vs
                    skipWs ()

                    if i >= n then
                        fail (at i) JsonlFaultReason.Truncated

                    if s[i] = ',' then
                        i <- i + 1
                    elif s[i] = '}' then
                        i <- i + 1
                        go <- false
                    else
                        fail (at i) JsonlFaultReason.ExpectedCommaOrBrace

            skipWs ()

            if i < n then
                fail (at i) JsonlFaultReason.TrailingContent

            List.ofSeq fields

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
            if raw.Length < 2 || raw[0] <> '"' then
                Error(JsonlFaultReason.ExpectedString "")
            else
                try
                    if skipString raw 0 rootPath = raw.Length then
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
                let negative = v.Length > 0 && v[0] = '-'
                let digits = if negative then v.Substring 1 else v

                let grammatical =
                    digits.Length > 0
                    && digits.Length <= 10
                    && Seq.forall (fun c -> c >= '0' && c <= '9') digits
                    && (digits = "0" || digits[0] <> '0')

                // Read from the DIGITS, never through a host number reader (Phase 306). The
                // `System.Int64.Parse v` this replaces read under the CURRENT culture: under one
                // whose negative sign is not U+002D (fa-IR, he-IL) it threw on `-5` — a token the
                // grammar test above had just accepted — out of a function that returns a `Result`.
                // At most ten digits, so the fold cannot leave int64.
                let value =
                    if grammatical then
                        let magnitude =
                            digits |> Seq.fold (fun acc c -> acc * 10L + int64 (int c - int '0')) 0L

                        if negative then -magnitude else magnitude
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

                if n < 2 || v[0] <> '[' || v[n - 1] <> ']' then
                    bad ()
                else
                    let items = ResizeArray<string>()
                    let mutable i = 1
                    let mutable ok = true
                    let mutable expectItem = true
                    // Phase 383 — an item the scanner refuses is the scanner's own fault, at its
                    // position in the line, the way `unquote` answers; never a decode of the bad span.
                    let mutable scanFault: JsonlFault option = None

                    let skipWs () =
                        while i < n - 1 && isWs v[i] do
                            i <- i + 1

                    skipWs ()

                    if i = n - 1 then
                        Ok []
                    else
                        while ok && i < n - 1 do
                            skipWs ()

                            if expectItem then
                                if i < n - 1 && v[i] = '"' then
                                    let e =
                                        try
                                            skipString v i rootPath
                                        with JsonlScanFault(at, r) ->
                                            scanFault <- Some(faultAt line.Number (p + at) r)
                                            -1

                                    if e < 0 then
                                        ok <- false
                                    else
                                        items.Add(decodeString v i e)
                                        i <- e
                                        expectItem <- false
                                else
                                    ok <- false
                            elif v[i] = ',' then
                                i <- i + 1
                                expectItem <- true
                            else
                                ok <- false

                            skipWs ()

                        match scanFault with
                        | Some f -> Error f
                        | None when ok && not expectItem -> Ok(List.ofSeq items)
                        | None -> bad ()

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
                let text = lines[k]

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

    /// The first of `members` — `(member, text)` in line order — that `Jsonl.checkRaw` refuses, with
    /// its reason (Phase 473). A text is the member's value exactly as the line will carry it: an
    /// embedded span, or a string the writer quotes itself.
    let firstUnreadable (members: (string * string) list) : (string * JsonlWriteFaultReason) option =
        members
        |> List.tryPick (fun (m, text) ->
            match Jsonl.checkRaw text with
            | Error reason -> Some(m, reason)
            | Ok() -> None)

    /// Write one line per item (Phase 301): every member each line carries — the span it embeds
    /// (`embedded`) and, since Phase 473, every string the writer quotes itself — is checked
    /// (`Jsonl.checkRaw`) in line order before any line is built, and the first refused one is the
    /// result, by its 1-based line and member. Until Phase 473 only the span was checked, so an
    /// unpaired surrogate in a quoted string was written and then refused by the strict reader
    /// (DECISIONS.md D158.6). On `Ok` the text is the one the unchecked writer produces, byte for byte.
    let checkedLines
        (embedded: string)
        (membersOf: 'T -> (string * string) list)
        (lineOf: 'T -> string -> string)
        (items: 'T list)
        : Result<string, JsonlWriteFault> =
        let lines = ResizeArray<string>()

        let rec go (i: int) =
            function
            | [] -> Ok(String.concat "\n" lines)
            | (x: 'T) :: rest ->
                let members = membersOf x

                match firstUnreadable members with
                | Some(m, reason) ->
                    Error
                        { Line = i + 1
                          Member = m
                          Reason = reason }
                | None ->
                    lines.Add(lineOf x (members |> List.find (fun (m, _) -> m = embedded) |> snd))
                    go (i + 1) rest

        go 0 items

    let tryToJsonl
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (records: OpRecord<'Op> list)
        : Result<string, JsonlWriteFault> =
        checkedLines
            "op"
            (fun (r: OpRecord<'Op>) ->
                [ "actor", Actor.encode r.Actor
                  "op", w.Encode r.Op
                  "prevHash", jstr r.PrevHash
                  "hash", jstr r.Hash ])
            recordLine
            records

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
    /// the first line of the stream (Phase 296); a second, or one after a record, is refused. Each
    /// record comes with the 1-based line that holds it.
    let private scanJsonlWithSnapshots
        (actorOf: JsonlLine -> Result<Actor, JsonlFault>)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<(int * OpRecord<'Op>) list * string list, JsonlFault> =
        text
        |> Jsonl.scanRecords (fun line ->
            if isSnapshotLine line then
                Ok(Choice2Of2 line)
            else
                recordOf actorOf w line
                |> Result.map (fun r -> Choice1Of2(Jsonl.lineNumber line, r)))
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

    /// The witness a load reads through (Phase 416): it never refuses, carrying each op's stored text
    /// beside the real witness's answer, so a decode failure stops neither the scan nor the
    /// verification. It encodes a decoded op by the real witness, as verification always has, and an
    /// undecoded one as its stored text — the bytes the checked writer embedded verbatim, so the
    /// content id still recomputes over exactly what was hashed.
    let private carry (w: StreamWitness<'Op, 'State, 'Rej>) : StreamWitness<string * Result<'Op, string>, unit, unit> =
        { Apply = fun _ s -> Ok s
          Encode =
            fun (raw, decoded) ->
                match decoded with
                | Ok op -> w.Encode op
                | Error _ -> raw
          Decode = fun raw -> Ok(raw, w.Decode raw) }

    /// The records of a carried read, decoded — or EVERY record the witness refused, by line and
    /// stored hash.
    let private settle
        (reads: (int * OpRecord<string * Result<'Op, string>>) list)
        : Result<OpRecord<'Op> list, StreamLoadFault<ChainBreak>> =
        let decoded =
            reads
            |> List.map (fun (line, r) ->
                match snd r.Op with
                | Ok op ->
                    Ok
                        { Seq = r.Seq
                          Actor = r.Actor
                          Op = op
                          PrevHash = r.PrevHash
                          Hash = r.Hash }
                | Error why ->
                    Error
                        { Lane = None
                          Line = line
                          NodeId = r.Hash
                          Reason = why })

        match
            decoded
            |> List.choose (function
                | Error site -> Some site
                | Ok _ -> None)
        with
        | [] ->
            Ok(
                decoded
                |> List.choose (function
                    | Ok r -> Some r
                    | Error _ -> None)
            )
        | sites -> Error(StreamLoadFault.Undecodable sites)

    let private readCarried
        (actorOf: JsonlLine -> Result<Actor, JsonlFault>)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        =
        scanJsonlWithSnapshots actorOf (carry w) text
        |> Result.mapError (fun f -> StreamLoadFault.Unreadable(None, f))

    let fromJsonlWithSnapshots
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list * string list, StreamLoadFault<ChainBreak>> =
        readCarried (Jsonl.actorField "actor") w text
        |> bindR (fun (reads, snaps) -> settle reads |> Result.map (fun recs -> recs, snaps))

    let fromJsonl
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list, StreamLoadFault<ChainBreak>> =
        fromJsonlWithSnapshots w text |> Result.map fst

    let fromJsonlLegacyActor
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list, StreamLoadFault<ChainBreak>> =
        readCarried (fun line -> Jsonl.stringField "actor" line |> Result.map Actor.ofLegacyString) w text
        |> bindR (fst >> settle)

    let fromJsonlVerified
        (hashFn: HashFn)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list, StreamLoadFault<ChainBreak>> =
        readCarried (Jsonl.actorField "actor") w text
        |> bindR (fun (reads, _) ->
            match firstChainBreak hashFn (carry w) (List.map snd reads) with
            | Some b -> Error(StreamLoadFault.Broken b)
            | None -> settle reads)
