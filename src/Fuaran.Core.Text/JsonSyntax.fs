namespace Fuaran.Core

/// Why the lexical grammar refused a JSON string token (Phase 531). Each case is one rule of the
/// strict grammar (DECISIONS.md D157): the escapes the grammar has, the closing quote, no raw
/// control character, and well-formed UTF-16 whichever spelling each unit arrived in. A reader maps
/// a case to its own vocabulary — `JsonErrorKind` in `Fuaran.Core.Wire`, `JsonlFaultReason` in
/// `Fuaran.Core.OpStream` — at the positions its `JsonStringRefusal` carries.
[<RequireQualifiedAccess>]
type JsonStringFault =
    /// The input ended inside the string, before its closing quote.
    | Unterminated
    /// The input ended straight after a backslash.
    | UnterminatedEscape
    /// A `\u` escape with fewer than four characters left before the input ends.
    | TruncatedUnicodeEscape
    /// A `\u` escape whose four characters are not all hex digits (either case is a digit).
    | BadHexDigit
    /// A backslash followed by a character outside the JSON escape set — the character.
    | UnknownEscape of escape: char
    /// A raw control character, `U+0000`–`U+001F`, which the grammar requires escaped — the character.
    | RawControl of control: char
    /// A high surrogate not followed at once by a low one.
    | HighWithoutLow
    /// A low surrogate with no high one before it.
    | LowWithoutHigh
    /// The string's code points past the bound the read declared (Phase 465), at the first unit over it.
    | TooLong

/// A refused JSON string token (Phase 531): the rule it broke and where — the cursor at the refusal
/// and the start of the unit it is about — with the high surrogate still waiting for its low half, if
/// one was. Two readers place a refusal differently (the document parser at its cursor, the JSONL
/// line reader at the start of what it names), so the token's facts are carried and each reader
/// places its own; neither re-reads the token.
type JsonStringRefusal =
    {
        /// The rule the token broke.
        Fault: JsonStringFault
        /// The scan's cursor at the refusal, 0-based: AT a raw control character, just PAST a unit the
        /// scan had consumed (an escape, a surrogate, the closing quote), at the end of the input for
        /// an unterminated token, and just past the `u` for a `\u` escape it could not read.
        Cursor: int
        /// Where the unit at fault starts: its backslash for an escape, the character itself for a raw
        /// unit, the end of the input for an unterminated token.
        UnitAt: int
        /// The start of the high surrogate that was waiting for its low half when the token was
        /// refused (its backslash when it was escaped), or `-1` when none was.
        PendingHigh: int
        /// Whether that pending high surrogate was spelled as a `\u` escape.
        PendingHighEscaped: bool
    }

/// The strict lexical grammar of JSON (Phase 531): the token rules every JSON reader in the spine
/// reads by — `Json.read` and the typed `Reader` in `Fuaran.Core.Wire`, and the op stream's JSONL line
/// reader in `Fuaran.Core.OpStream` — written once. Until Phase 531 the line reader carried its own
/// string and literal grammar, held equal to the parser's by the refusal vectors rather than by code
/// (DECISIONS.md D158). What stays with each reader is the STRUCTURE it reads: the document parser's
/// recursive descent under a depth cap, building values or typed reads, and the line reader's
/// iterative walk recording raw spans with no cap at all.
///
/// FSharp.Core only, Fable-clean, and total: a refusal is a value, never an exception.
[<RequireQualifiedAccess>]
module JsonSyntax =

    /// The JSON whitespace: space, tab, line feed and carriage return — nothing else.
    let isWhitespace (c: char) : bool =
        c = ' ' || c = '\t' || c = '\n' || c = '\r'

    let inline private isHigh (u: int) = u >= 0xD800 && u <= 0xDBFF
    let inline private isLow (u: int) = u >= 0xDC00 && u <= 0xDFFF

    let inline private hexValue (c: char) : int =
        if c >= '0' && c <= '9' then int c - int '0'
        elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
        elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
        else -1

    /// True where `tok` is a number token of the JSON grammar, exactly (RFC 8259 §6):
    ///
    ///     -? (0 | [1-9][0-9]*) (\.[0-9]+)? ([eE][+-]?[0-9]+)?
    ///
    /// The grammar only: whether the number is REPRESENTABLE (finite, an exact integer past 2^53) is
    /// the reading package's question, and the line reader does not ask it. Three steps — the
    /// integer part, the fraction, the exponent — which `proofs/JsonParse.fst`'s `is_json_number`
    /// states clause for clause (Phase 299's body, moved here from `Json.isJsonNumber`).
    let isNumber (tok: string) : bool =
        let n = tok.Length

        let isDigit (k: int) = k < n && tok[k] >= '0' && tok[k] <= '9'

        let rec digitsFrom (k: int) =
            if isDigit k then digitsFrom (k + 1) else k

        // The integer part from `k`: a lone `0`, or a non-zero digit and any digits after it.
        let afterInt (k: int) =
            if k < n && tok[k] = '0' then Some(k + 1)
            elif isDigit k then Some(digitsFrom k)
            else None

        // An optional `.` and at least one digit.
        let afterFrac (k: int) =
            if k < n && tok[k] = '.' then
                let e = digitsFrom (k + 1)
                if e > k + 1 then Some e else None
            else
                Some k

        // An optional `e`/`E`, an optional sign, and at least one digit.
        let afterExp (k: int) =
            if k < n && (tok[k] = 'e' || tok[k] = 'E') then
                let s =
                    if k + 1 < n && (tok[k + 1] = '+' || tok[k + 1] = '-') then
                        k + 2
                    else
                        k + 1

                let e = digitsFrom s
                if e > s then Some e else None
            else
                Some k

        let start = if n > 0 && tok[0] = '-' then 1 else 0

        match afterInt start |> Option.bind afterFrac |> Option.bind afterExp with
        | Some k -> k = n
        | None -> false

    let private refusalOf
        (fault: JsonStringFault)
        (cursor: int)
        (unitAt: int)
        (pendingHigh: int)
        (pendingEscaped: bool)
        : JsonStringRefusal =
        { Fault = fault
          Cursor = cursor
          UnitAt = unitAt
          PendingHigh = pendingHigh
          PendingHighEscaped = pendingEscaped }

    // The one body behind `skipString` and `readString`. With `decoded` null it validates only;
    // otherwise it copies the value into `decoded` from the token's first escape on (the run before
    // that escape included), and an escape-free token leaves `decoded` empty — every escape appends
    // one unit, so an empty `decoded` after a read is exactly a token whose value is its body.
    let private scanString
        (input: string)
        (start: int)
        (maxCodePoints: int)
        (decoded: System.Text.StringBuilder)
        : Result<int, JsonStringRefusal> =
        let n = input.Length
        let copy = not (isNull decoded)
        let mutable i = start + 1
        // Where the escape-free run not yet copied into `decoded` begins.
        let mutable runStart = i
        // The start of the high surrogate waiting for its low half, or -1; and its spelling.
        let mutable pendingHigh = -1
        let mutable pendingEscaped = false
        // The code points admitted so far (Phase 465).
        let mutable codePoints = 0
        let mutable refused = false
        let mutable refusal = Unchecked.defaultof<JsonStringRefusal>
        let mutable closed = false

        while not refused && not closed do
            // The next unit: its code, where it starts, whether it was escaped; -1 when there is none.
            let mutable unit = -1
            let mutable unitAt = i
            let mutable escaped = false

            if i >= n then
                refusal <- refusalOf JsonStringFault.Unterminated n n pendingHigh pendingEscaped
                refused <- true
            else
                let c = input[i]

                if c = '"' then
                    // An escape-free token is not copied at all: its value is its body.
                    if copy && decoded.Length > 0 && i > runStart then
                        decoded.Append(input, runStart, i - runStart) |> ignore

                    i <- i + 1

                    if pendingHigh >= 0 then
                        refusal <- refusalOf JsonStringFault.HighWithoutLow i (i - 1) pendingHigh pendingEscaped
                        refused <- true
                    else
                        closed <- true
                elif c = '\\' then
                    if copy && i > runStart then
                        decoded.Append(input, runStart, i - runStart) |> ignore

                    i <- i + 1

                    if i >= n then
                        refusal <- refusalOf JsonStringFault.UnterminatedEscape n unitAt pendingHigh pendingEscaped
                        refused <- true
                    else
                        let e = input[i]
                        i <- i + 1
                        escaped <- true

                        match e with
                        | '"' -> unit <- int '"'
                        | '\\' -> unit <- int '\\'
                        | '/' -> unit <- int '/'
                        | 'n' -> unit <- int '\n'
                        | 'r' -> unit <- int '\r'
                        | 't' -> unit <- int '\t'
                        | 'b' -> unit <- int '\b'
                        | 'f' -> unit <- int '\f'
                        | 'u' ->
                            if i + 4 > n then
                                refusal <-
                                    refusalOf JsonStringFault.TruncatedUnicodeEscape i unitAt pendingHigh pendingEscaped

                                refused <- true
                            else
                                let d0 = hexValue input[i]
                                let d1 = hexValue input[i + 1]
                                let d2 = hexValue input[i + 2]
                                let d3 = hexValue input[i + 3]

                                if d0 < 0 || d1 < 0 || d2 < 0 || d3 < 0 then
                                    refusal <- refusalOf JsonStringFault.BadHexDigit i unitAt pendingHigh pendingEscaped
                                    refused <- true
                                else
                                    i <- i + 4
                                    unit <- (d0 <<< 12) + (d1 <<< 8) + (d2 <<< 4) + d3
                        | other ->
                            refusal <-
                                refusalOf (JsonStringFault.UnknownEscape other) i unitAt pendingHigh pendingEscaped

                            refused <- true

                    runStart <- i
                elif c < ' ' then
                    refusal <- refusalOf (JsonStringFault.RawControl c) i i pendingHigh pendingEscaped
                    refused <- true
                else
                    unit <- int c
                    i <- i + 1

            // Admit the unit, which ended just before `i`.
            if unit >= 0 then
                if pendingHigh >= 0 && not (isLow unit) then
                    refusal <- refusalOf JsonStringFault.HighWithoutLow i unitAt pendingHigh pendingEscaped
                    refused <- true
                elif pendingHigh < 0 && isLow unit then
                    refusal <- refusalOf JsonStringFault.LowWithoutHigh i unitAt pendingHigh pendingEscaped
                    refused <- true
                elif pendingHigh < 0 && codePoints >= maxCodePoints then
                    refusal <- refusalOf JsonStringFault.TooLong i unitAt pendingHigh pendingEscaped
                    refused <- true
                else
                    if pendingHigh < 0 then
                        codePoints <- codePoints + 1

                    if isHigh unit then
                        pendingHigh <- unitAt
                        pendingEscaped <- escaped
                    else
                        pendingHigh <- -1
                        pendingEscaped <- false

                    if escaped && copy then
                        decoded.Append(char unit) |> ignore

        if refused then Error refusal else Ok i

    /// `readString` without the value (Phase 531): the index just past the string token whose opening
    /// quote is at `start`, or the refusal, with nothing copied — the same rules, the same refusals
    /// at the same positions.
    let skipString (input: string) (start: int) (maxCodePoints: int) : Result<int, JsonStringRefusal> =
        scanString input start maxCodePoints null

    /// THE string token reader (Phase 531; by runs since Phase 366). `start` is the index of the
    /// token's opening quote; the answer is the token's VALUE and the index just past its closing
    /// quote, or the refusal.
    /// Every escape is held to the grammar (the eight single-letter escapes, and `\uXXXX` with four
    /// hex digits); no raw control character `U+0000`–`U+001F` is admitted; and the string is
    /// well-formed UTF-16 — every high surrogate followed at once by a low one, every low one after a
    /// high one, whichever spelling each half arrived in, so a pair split between a raw unit and an
    /// escape is read. At most `maxCodePoints` code points are admitted (a low half completing a
    /// pending high is not a new one); pass `System.Int32.MaxValue` for no bound.
    ///
    /// One pass, and at most one copy: an escape-free token's value is its body, taken by one
    /// `Substring`; a token with an escape is decoded into `scratch` (cleared first, the caller's
    /// working buffer, so a reader of many strings allocates one) — each escape-free run by one ranged
    /// append, and each escape as the unit it spells.
    ///
    /// The refusal names the first rule broken in reading order, with the facts of where
    /// (`JsonStringRefusal`): a raw control is refused AT the character, before anything it would
    /// pair with is considered; a surrogate rule after the unit that breaks it.
    let readString
        (input: string)
        (start: int)
        (maxCodePoints: int)
        (scratch: System.Text.StringBuilder)
        : Result<struct (string * int), JsonStringRefusal> =
        scratch.Clear() |> ignore

        match scanString input start maxCodePoints scratch with
        | Ok stop ->
            let value =
                if scratch.Length = 0 then
                    input.Substring(start + 1, stop - start - 2)
                else
                    scratch.ToString()

            Ok(struct (value, stop))
        | Error r -> Error r
