namespace Fuaran.Core

/// A minimal JSON value model shared by the Fable-clean *encode* path (`Json.render`)
/// and the portable *decode* path (`Json.parse`). Domains build their `"kind"`-tagged,
/// camelCase wire objects from these constructors. The Fuaran wire model has no `null`
/// and no non-finite float (`tryRender` rejects NaN/±Infinity at encode; `parse`
/// rejects overflowing tokens at decode).
///
/// **Numeric normalization — `JInt` and `JFloat` are one population on the wire.**
/// JSON has a single number type, so a whole-valued `JFloat` does not survive a
/// round-trip as `JFloat`: `render (JFloat 2.0)` emits `2` (shortest round-trip form),
/// which `parse` reads back as `JInt 2`. Pattern-matching `JFloat` alone on parsed
/// wire therefore silently misses whole values — read numbers through `JVal.asFloat`
/// (the blessed numeric accessor), or match `JInt`/`JFloat` together.
type JVal =
    | JStr of string
    | JInt of int
    | JBool of bool
    | JFloat of float
    | JArr of JVal list
    | JObj of (string * JVal) list

/// The classified failure modes of the portable JSON parser (Phase 22) — so an orchestrator
/// can branch on *what* went wrong structurally instead of string-scraping `parse`'s message.
type JsonErrorKind =
    | UnexpectedChar
    | UnexpectedEndOfInput
    | ExpectedToken
    | UnterminatedString
    | UnterminatedEscape
    | TruncatedUnicodeEscape
    | BadEscape
    | BadHexDigit
    | MalformedNumber
    | NullNotRepresentable
    | MaxDepthExceeded
    | TrailingCharacters

/// A structured parse failure (Phase 22): the classified `Kind`, the human `Message` (no
/// position suffix), and the 0-based `Position` in the input. `Json.parse`'s string error is
/// `"not valid JSON: " + Message + " at position " + Position` — byte-identical to before.
type JsonError =
    { Position: int
      Message: string
      Kind: JsonErrorKind }

/// The **read-side** policy for the JSON `null` token — the position rules as data.
///
/// The Fuaran wire model itself is unchanged by this type: `JVal` gains no constructor,
/// `Json.render` / `Canon.render` never emit `null` whichever policy a read ran under, and the
/// strict policy is the pinned default (`parse` / `parseWith` / `parseDetailed` / `parseDetailedWith`
/// are byte-identical under it — same errors, same positions, same messages). The policy governs
/// exactly one thing: what the parser does when a **foreign** document spells an absent member
/// `null`, as a great many JSON producers do.
///
/// Tolerance is a *read* normalisation, never a new emission — a tolerantly-parsed document
/// re-renders in the canonical `null`-free form, so nothing downstream can tell it apart from the
/// same document spelled without the token.
type NullPolicy =
    /// Every `null` token, in every position, is a `NullNotRepresentable` rejection. The pinned
    /// default behaviour, which consumers branch on.
    | RejectNull
    /// A `null` in **object-member value position** is erased to member absence, so `{"a":null}`
    /// reads exactly as `{}` — the same "absence is structural" rule the encode side already
    /// applies to a null cell (`RowCodec.encodeCell` rule 4). Every other position has no absence
    /// to erase to and stays a named `NullNotRepresentable` rejection: a bare top-level `null` (the
    /// whole document would vanish) and a `null` array element (erasing it would silently renumber
    /// every later index). Array-position tolerance, if a consumer ever surfaces a genuine need for
    /// it, is a deliberate extension — not a thing this case quietly already does.
    | EraseMemberNull

/// Accessors over `JVal` that absorb the wire's numeric normalization (see the type doc:
/// a whole-valued `JFloat` round-trips as `JInt`, because JSON has one number type).
[<RequireQualifiedAccess>]
module JVal =

    /// The blessed numeric read path: a wire number as a `float`, whichever constructor
    /// the parser chose. `JInt` is exact in double (the parser's int53 guard bounds it);
    /// any other case is `None`. Prefer this over matching `JFloat` directly on parsed
    /// wire — `JFloat 2.0` comes back as `JInt 2`.
    let asFloat (v: JVal) : float option =
        match v with
        | JInt i -> Some(float i)
        | JFloat f -> Some f
        | _ -> None

/// Fable-clean encode helpers + the wire-envelope discipline + a portable
/// (FSharp.Core-only) parser. Per-kind cases stay domain-side; the core owns the
/// envelope shape, the combinators, and the parser. `render` and `parse` are inverses
/// over canonical wire JSON.
module Json =

    let escape (s: string) : string =
        let sb = System.Text.StringBuilder()

        for ch in s do
            match ch with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when int c < 0x20 -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append(c) |> ignore

        sb.ToString()

    let rec render (v: JVal) : string =
        match v with
        | JStr s -> "\"" + escape s + "\""
        | JInt i -> string i
        | JBool b -> (if b then "true" else "false")
        | JFloat f -> System.String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R}", f)
        | JArr xs -> "[" + (xs |> List.map render |> String.concat ",") + "]"
        | JObj fields ->
            "{"
            + (fields
               |> List.map (fun (k, v) -> "\"" + escape k + "\":" + render v)
               |> String.concat ",")
            + "}"

    /// A `"kind"`-tagged object — the wire envelope every domain node/op serialises as.
    /// `tag` leads; `fields` follow in author order (camelCase keys by discipline).
    let kindObj (tag: string) (fields: (string * JVal) list) : JVal = JObj(("kind", JStr tag) :: fields)

    let encode (v: JVal) : string = render v

    /// Total, guarded render (Phase 12). `render` formats a `JFloat` with `"{0:R}"`, so a
    /// non-finite float (`NaN` / `Infinity` / `-Infinity`) emits a token that is not valid JSON
    /// and that `parse` then rejects — `render` succeeds but produces un-parseable wire, breaking
    /// `render ∘ parse = id`. The Fuaran wire model has no non-finite float (the same posture as
    /// "no null"). `tryRender` names the first non-finite `JFloat` as a typed `Error` instead;
    /// over an all-finite value it is exactly `Ok (render v)`.
    let tryRender (v: JVal) : Result<string, string> =
        let rec firstNonFinite (v: JVal) : float option =
            match v with
            | JFloat f when System.Double.IsNaN f || System.Double.IsInfinity f -> Some f
            | JArr xs -> xs |> List.tryPick firstNonFinite
            | JObj fields -> fields |> List.tryPick (fun (_, x) -> firstNonFinite x)
            | _ -> None

        match firstNonFinite v with
        | Some f ->
            let tok =
                if System.Double.IsNaN f then "NaN"
                elif f > 0.0 then "Infinity"
                else "-Infinity"

            Error("non-finite float is not representable on the Fuaran wire: " + tok)
        | None -> Ok(render v)

    /// `tryRender` under the `encode` name — the total, guarded encode entry point.
    let tryEncode (v: JVal) : Result<string, string> = tryRender v

    /// Internal signal for the recursive-descent parser; never escapes the parse entry points.
    /// Carries the classified kind, the message, and the position captured at the raise site.
    exception private JsonParseError of JsonErrorKind * string * int

    /// The default maximum object/array nesting depth for `parse`. Input nested deeper fails
    /// as a named `Error` instead of overflowing the stack — a `StackOverflowException` is
    /// uncatchable in .NET and would crash the host, breaking totality (GP4) on the one entry
    /// point built to ingest untrusted/portable wire data. `parseWith` overrides it.
    [<Literal>]
    let defaultMaxDepth = 512

    /// `parseDetailed` under an explicit nesting cap **and an explicit `NullPolicy`** — the core
    /// parser every other entry point is a wrapper over. Under `RejectNull` (the default every
    /// pre-existing entry point passes) it is the parser as it has always been, byte-for-byte.
    /// Returns a structured `JsonError` on failure; the `parseWith` family are the string-error
    /// wrappers.
    let parseDetailedWithPolicy (policy: NullPolicy) (maxDepth: int) (input: string) : Result<JVal, JsonError> =
        let tolerateMemberNull =
            match policy with
            | RejectNull -> false
            | EraseMemberNull -> true

        let n = input.Length
        let mutable i = 0

        let fail (kind: JsonErrorKind) (msg: string) : 'a = raise (JsonParseError(kind, msg, i))

        // EOI sentinel spelled as the ESCAPED literal '\000' — a raw U+0000 byte here
        // previously made this whole file "binary" to ripgrep/GitHub code search,
        // hiding the parser's source from tooling. The sentinel value itself never
        // matters: `peek ()`'s result is only compared against structural chars
        // ('-', '.', 'e', '}', …), all of which NUL fails, and every consuming loop
        // is bounds-guarded by `i < n`.
        let peek () = if i < n then input.[i] else '\000'

        let isWs c =
            c = ' ' || c = '\t' || c = '\n' || c = '\r'

        let skipWs () =
            while i < n && isWs input.[i] do
                i <- i + 1

        let expect (c: char) =
            if i < n && input.[i] = c then
                i <- i + 1
            else
                fail ExpectedToken ("expected '" + string c + "'")

        let hexDigit (c: char) : int =
            if c >= '0' && c <= '9' then int c - int '0'
            elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
            elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
            else fail BadHexDigit "bad hex digit in \\u escape"

        let parseString () : string =
            expect '"'
            let sb = System.Text.StringBuilder()
            let mutable fin = false

            while not fin do
                if i >= n then
                    fail UnterminatedString "unterminated string"

                let c = input.[i]
                i <- i + 1

                match c with
                | '"' -> fin <- true
                | '\\' ->
                    if i >= n then
                        fail UnterminatedEscape "unterminated escape"

                    let e = input.[i]
                    i <- i + 1

                    match e with
                    | '"' -> sb.Append('"') |> ignore
                    | '\\' -> sb.Append('\\') |> ignore
                    | '/' -> sb.Append('/') |> ignore
                    | 'n' -> sb.Append('\n') |> ignore
                    | 'r' -> sb.Append('\r') |> ignore
                    | 't' -> sb.Append('\t') |> ignore
                    | 'b' -> sb.Append('\b') |> ignore
                    | 'f' -> sb.Append('\f') |> ignore
                    | 'u' ->
                        if i + 4 > n then
                            fail TruncatedUnicodeEscape "truncated \\u escape"

                        let code =
                            (hexDigit input.[i] <<< 12)
                            + (hexDigit input.[i + 1] <<< 8)
                            + (hexDigit input.[i + 2] <<< 4)
                            + hexDigit input.[i + 3]

                        i <- i + 4
                        sb.Append(char code) |> ignore
                    | _ -> fail BadEscape ("bad escape '\\" + string e + "'")
                | _ -> sb.Append(c) |> ignore

            sb.ToString()

        let parseNumber () : JVal =
            let start = i
            let mutable isFloat = false

            if peek () = '-' then
                i <- i + 1

            while i < n && input.[i] >= '0' && input.[i] <= '9' do
                i <- i + 1

            if peek () = '.' then
                isFloat <- true
                i <- i + 1

                while i < n && input.[i] >= '0' && input.[i] <= '9' do
                    i <- i + 1

            if peek () = 'e' || peek () = 'E' then
                isFloat <- true
                i <- i + 1

                if peek () = '+' || peek () = '-' then
                    i <- i + 1

                while i < n && input.[i] >= '0' && input.[i] <= '9' do
                    i <- i + 1

            let tok = input.Substring(start, i - start)

            let asFloat () =
                match
                    System.Double.TryParse(
                        tok,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                with
                // Finiteness gate: a syntactically-valid float token whose magnitude
                // exceeds the double range (e.g. "1e400") TryParses to ±Infinity on
                // .NET Core — but the Fuaran wire model has no non-finite float (the
                // same posture as "no null", enforced at encode by `tryRender`).
                // Admitting it here would let an un-renderable value in through the
                // one entry point built for untrusted wire data, breaking
                // `render ∘ parse = id`. Reject it as a named MalformedNumber.
                // (NaN cannot arise from a valid JSON number token; guarded anyway.)
                | true, v when System.Double.IsNaN v || System.Double.IsInfinity v ->
                    fail
                        MalformedNumber
                        ("number outside the finite double range; it cannot round-trip on the wire: "
                         + tok)
                | true, v -> JFloat v
                | _ -> fail MalformedNumber ("malformed number: " + tok)

            if isFloat then
                asFloat ()
            else
                // Integer literal (no '.' / 'e'). Fits JInt in the Int32 range; an
                // integer beyond Int32 is representable EXACTLY as a double only within
                // the int53 safe-integer range (|n| ≤ 2^53 — the range both a .NET double
                // and a JS Number reproduce without loss). BEYOND 2^53, silent float
                // coercion drops digits AND diverges cross-host (a 19-digit id becomes a
                // different id), so reject it as a named MalformedNumber rather than
                // corrupt it. (Fable-clean: Int32.TryParse + Double.TryParse only.)
                match System.Int32.TryParse tok with
                | true, v -> JInt v
                | _ ->
                    // Safety is judged on the TOKEN, not on a parsed double: 2^53 + 1
                    // rounds to 2^53 as a double, so a range check on the value would
                    // wrongly accept it. An integer is int53-safe iff |value| ≤ 2^53 =
                    // 9007199254740992 (16 digits). Compare the digit string lexically —
                    // JSON forbids leading zeros, so for equal length that IS the numeric
                    // order. Fable-clean (string + Double.TryParse only, no Int64).
                    let digits = if tok.StartsWith "-" then tok.Substring 1 else tok

                    let int53Safe =
                        digits.Length < 16
                        || (digits.Length = 16
                            && System.String.CompareOrdinal(digits, "9007199254740992") <= 0)

                    if int53Safe then
                        match
                            System.Double.TryParse(
                                tok,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                        with
                        | true, v -> JFloat v
                        | _ -> fail MalformedNumber ("malformed number: " + tok)
                    else
                        fail
                            MalformedNumber
                            ("integer literal outside the int53 safe range (|n| > 2^53); it cannot round-trip without precision loss: "
                             + tok)

        let rec parseValue (depth: int) : JVal =
            skipWs ()

            if i >= n then
                fail UnexpectedEndOfInput "unexpected end of input"

            match input.[i] with
            | '"' -> JStr(parseString ())
            | '{' -> parseObject depth
            | '[' -> parseArray depth
            | 't' -> parseLiteral "true" (JBool true)
            | 'f' -> parseLiteral "false" (JBool false)
            | 'n' ->
                // Under `EraseMemberNull` a member-position null never reaches here — `parseObject`
                // absorbs it before calling `parseValue` — so a null arriving at this arm under the
                // tolerant policy is at a position with NO absence to erase it to (bare root, or an
                // array element). Say so: a consumer reading the tolerant path's rejection must not
                // mistake it for the strict policy's blanket refusal, since the remedy is different
                // (the strict one is fixed by choosing the tolerant policy; this one is not).
                if tolerateMemberNull then
                    fail
                        NullNotRepresentable
                        "null is not representable in the Fuaran wire JVal model, and this position has no absence to erase it to (only an object-member null is erased)"
                else
                    fail NullNotRepresentable "null is not representable in the Fuaran wire JVal model"
            | c when c = '-' || (c >= '0' && c <= '9') -> parseNumber ()
            | c -> fail UnexpectedChar ("unexpected character '" + string c + "'")

        and parseLiteral (lit: string) (v: JVal) : JVal =
            if i + lit.Length <= n && input.Substring(i, lit.Length) = lit then
                i <- i + lit.Length
                v
            else
                fail ExpectedToken ("expected '" + lit + "'")

        and parseObject (depth: int) : JVal =
            if depth >= maxDepth then
                fail MaxDepthExceeded ("max nesting depth " + string maxDepth + " exceeded")

            expect '{'
            skipWs ()
            let fields = ResizeArray<string * JVal>()

            if peek () = '}' then
                i <- i + 1
            else
                let mutable go = true

                while go do
                    skipWs ()
                    let key = parseString ()
                    skipWs ()
                    expect ':'
                    skipWs ()

                    // The one behavioural fork of `EraseMemberNull`, and the only place in the
                    // parser that can erase anything: a member whose value is exactly the `null`
                    // token is consumed and NOT added, so the object reads as though the member had
                    // been omitted. Nothing malformed is absorbed: a truncated near-miss (`nul`)
                    // fails this test and falls through to `parseValue`, which names it exactly as
                    // the strict policy does, and a trailing-garbage one (`nullish`) is caught by
                    // the ',' / '}' expectation below. Under `RejectNull` the test is never taken
                    // and the member path is the pre-existing one, unchanged.
                    let erased = tolerateMemberNull && i + 4 <= n && input.Substring(i, 4) = "null"

                    if erased then
                        i <- i + 4
                    else
                        let v = parseValue (depth + 1)
                        fields.Add((key, v))

                    skipWs ()

                    match peek () with
                    | ',' -> i <- i + 1
                    | '}' ->
                        i <- i + 1
                        go <- false
                    | _ -> fail ExpectedToken "expected ',' or '}'"

            JObj(List.ofSeq fields)

        and parseArray (depth: int) : JVal =
            if depth >= maxDepth then
                fail MaxDepthExceeded ("max nesting depth " + string maxDepth + " exceeded")

            expect '['
            skipWs ()
            let items = ResizeArray<JVal>()

            if peek () = ']' then
                i <- i + 1
            else
                let mutable go = true

                while go do
                    let v = parseValue (depth + 1)
                    items.Add v
                    skipWs ()

                    match peek () with
                    | ',' -> i <- i + 1
                    | ']' ->
                        i <- i + 1
                        go <- false
                    | _ -> fail ExpectedToken "expected ',' or ']'"

            JArr(List.ofSeq items)

        try
            let v = parseValue 0
            skipWs ()

            if i <> n then
                Error
                    { Position = i
                      Message = "trailing characters"
                      Kind = TrailingCharacters }
            else
                Ok v
        with JsonParseError(kind, msg, pos) ->
            Error
                { Position = pos
                  Message = msg
                  Kind = kind }

    /// `parseDetailed` under an explicit nesting cap (Phases 10 + 22) — the strict parser. Returns a
    /// structured `JsonError` on failure; `parseWith` / `parse` are the string-error wrappers.
    let parseDetailedWith (maxDepth: int) (input: string) : Result<JVal, JsonError> =
        parseDetailedWithPolicy RejectNull maxDepth input

    /// Render a `JsonError` as the byte-identical legacy string (`"not valid JSON: <msg> at
    /// position <pos>"`) that `parse` / `parseWith` have always returned.
    let private formatJsonError (e: JsonError) : string =
        "not valid JSON: " + e.Message + " at position " + string e.Position

    /// `parseDetailed` at the default nesting cap (Phase 22) — structured `JsonError` on failure.
    let parseDetailed (input: string) : Result<JVal, JsonError> = parseDetailedWith defaultMaxDepth input

    /// `parse` under an explicit nesting cap (Phase 10) — the string-error wrapper over
    /// `parseDetailedWith`.
    let parseWith (maxDepth: int) (input: string) : Result<JVal, string> =
        parseDetailedWith maxDepth input |> Result.mapError formatJsonError

    /// Portable, FSharp.Core-only JSON parser → the `JVal` model. Fable-clean (no
    /// System.Text.Json), so decode runs under BOTH the .NET and Fable pipelines — this
    /// is what makes `Decode` symmetric across hosts (Phase 241). `render (Result-of parse)`
    /// is the identity over canonical wire JSON (compact, author-ordered keys). A bare
    /// `null` token is rejected by name (the wire model has no null). On failure the
    /// `Error` names what was expected — the same envelope discipline as the combinators.
    /// Nesting is capped at `defaultMaxDepth` (Phase 10) so deep input is a named `Error`,
    /// not a stack-overflow crash; use `parseWith` to override the cap. For a *foreign* document
    /// that spells absent members `null`, see `parseTolerantOfNull`.
    let parse (input: string) : Result<JVal, string> = parseWith defaultMaxDepth input

    /// `parse` under an explicit `NullPolicy` — the string-error wrapper over
    /// `parseDetailedWithPolicy`. `parseWithPolicy RejectNull` is exactly `parseWith`.
    let parseWithPolicy (policy: NullPolicy) (maxDepth: int) (input: string) : Result<JVal, string> =
        parseDetailedWithPolicy policy maxDepth input |> Result.mapError formatJsonError

    /// The **null-tolerant read** at an explicit nesting cap: a `null` in object-member position is
    /// erased to member absence (`{"a":null}` reads as `{}`); a bare or array-element `null` is a
    /// named rejection, as under the strict policy. See `NullPolicy.EraseMemberNull`.
    let parseTolerantOfNullWith (maxDepth: int) (input: string) : Result<JVal, string> =
        parseWithPolicy EraseMemberNull maxDepth input

    /// The **null-tolerant read** at the default nesting cap — the entry point a consumer of a
    /// foreign, spec-conformant document reaches for when that document spells absent members
    /// `null`. Strict `parse` is untouched; this is an opt-in, read-side-only tolerance, and what it
    /// produces is an ordinary `JVal` that re-renders in the canonical `null`-free form.
    let parseTolerantOfNull (input: string) : Result<JVal, string> =
        parseTolerantOfNullWith defaultMaxDepth input

    /// `parseTolerantOfNull` with the structured `JsonError` (the tolerant path's non-member
    /// rejections keep `Kind = NullNotRepresentable`; the `Message` names the missing absence).
    let parseDetailedTolerantOfNull (input: string) : Result<JVal, JsonError> =
        parseDetailedWithPolicy EraseMemberNull defaultMaxDepth input

/// The canonical `$type` wire discipline — the single
/// platform-wide canonical-JSON convention `Fuaran.Core` and `Fuaran.UI` share, so a value
/// serialised by Core is byte-identical to the same value serialised by the UI host (and, via the
/// §11.1 gate, by the TS / Python hosts). It renders the `JVal` model under the UI host's mature
/// conventions (`fuaran/docs/WIRE_FORMAT.md` §2): `$type`-discriminated DU objects, **Ordinal-sorted
/// object keys** (recursively), control chars escaped as `\u00xx` (no `\n`/`\r`/`\t` shortcuts), and
/// the pinned float layout — `Double.ToString("R")` on .NET, the byte-identical JS re-layout under
/// Fable — so numeric columns + literals match across hosts. `Json.render` (author-ordered, `kind`-
/// tagged) stays for Core's pre-unification internal uses; `Canon.render` is the cross-host form.
module Canon =

    /// Canonical string escape (WIRE_FORMAT §2 rule 6): only `"`, `\`, and control chars
    /// (`U+0000`–`U+001F` → `\u00xx`, lower-case hex). No `\n`/`\r`/`\t` shortcuts — byte-for-byte
    /// the UI host's `appendRawString`.
    let private escape (s: string) : string =
        let sb = System.Text.StringBuilder()

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.ToString()

#if FABLE_COMPILER
    [<Fable.Core.Emit("$0.toString()")>]
    let private jsNumberToString (n: float) : string = Fable.Core.Util.jsNative

    /// Re-lay JS's shortest-round-trip digits into .NET `ToString("R")` form (WIRE_FORMAT §2 rule 5)
    /// so the Fable host is byte-identical to the .NET host across the whole finite-double range.
    /// Ported verbatim from the UI host's `CanonicalJson.formatFiniteDouble`.
    let private formatFiniteDouble (n: float) : string =
        if n = 0.0 then
            "0"
        else
            let neg = n < 0.0
            let s = jsNumberToString (abs n)
            let mutable digits = ""
            let mutable exp = 0
            let eIdx = s.IndexOf 'e'

            if eIdx >= 0 then
                let mant = s.Substring(0, eIdx)
                let mantExp = int (s.Substring(eIdx + 1))
                let dot = mant.IndexOf '.'

                if dot < 0 then
                    digits <- mant
                    exp <- mantExp + (mant.Length - 1)
                else
                    digits <- mant.Substring(0, dot) + mant.Substring(dot + 1)
                    exp <- mantExp + (dot - 1)
            else
                let dot = s.IndexOf '.'

                if dot < 0 then
                    digits <- s
                    exp <- s.Length - 1
                else
                    let intPart = s.Substring(0, dot)
                    let fracPart = s.Substring(dot + 1)

                    if intPart = "0" then
                        let trimmed = fracPart.TrimStart('0')
                        let leadingZeros = fracPart.Length - trimmed.Length
                        digits <- fracPart.Substring(leadingZeros)
                        exp <- -(leadingZeros + 1)
                    else
                        digits <- intPart + fracPart
                        exp <- intPart.Length - 1

            digits <- digits.TrimEnd('0')

            if digits = "" then
                digits <- "0"

            let out =
                if exp >= -4 && exp <= 16 then
                    if exp >= 0 then
                        if digits.Length <= exp + 1 then
                            digits + String.replicate (exp + 1 - digits.Length) "0"
                        else
                            digits.Substring(0, exp + 1) + "." + digits.Substring(exp + 1)
                    else
                        "0." + String.replicate (-exp - 1) "0" + digits
                else
                    let mantissa =
                        if digits.Length = 1 then
                            digits
                        else
                            string digits[0] + "." + digits.Substring(1)

                    let expSign = if exp >= 0 then "+" else "-"
                    let expDigits = (abs exp).ToString().PadLeft(2, '0')
                    mantissa + "E" + expSign + expDigits

            if neg then "-" + out else out
#endif

    /// The single canonical, cross-host float → string encoder (Phase 55). Non-finite floats render to
    /// the fixed JSON-string tokens `"NaN"` / `"Infinity"` / `"-Infinity"`; `-0.0` collapses to `0`; a
    /// finite float uses `Double.ToString("R", InvariantCulture)` on .NET and the byte-identical JS
    /// shortest-round-trip re-layout (`formatFiniteDouble`) under Fable (WIRE_FORMAT §2 rule 5). Every
    /// float→wire / float→key path in the substrate routes through this one function so the bytes match
    /// across the .NET / Fable / TS / Python hosts. Pinned in `STABILITY.md`.
    let canonicalFloat (f: float) : string =
        if System.Double.IsNaN f then
            "\"NaN\""
        elif System.Double.IsPositiveInfinity f then
            "\"Infinity\""
        elif System.Double.IsNegativeInfinity f then
            "\"-Infinity\""
        else
            // -0 collapses to 0 (WIRE_FORMAT §2 rule 5).
            let v = if f = 0.0 then 0.0 else f
#if FABLE_COMPILER
            formatFiniteDouble v
#else
            v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
#endif

    /// Render a `JVal` under the canonical `$type` discipline: object keys Ordinal-sorted
    /// (recursively), the pinned float layout, canonical escaping. The encoder enforces key order;
    /// decoders stay order-tolerant (they look up by name).
    let rec render (v: JVal) : string =
        match v with
        | JStr s -> "\"" + escape s + "\""
        | JInt i -> string i
        | JBool b -> (if b then "true" else "false")
        | JFloat f -> canonicalFloat f
        | JArr xs -> "[" + (xs |> List.map render |> String.concat ",") + "]"
        | JObj fields ->
            "{"
            + (fields
               |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b))
               |> List.map (fun (k, v) -> "\"" + escape k + "\":" + render v)
               |> String.concat ",")
            + "}"

    /// Build a `$type`-discriminated object — the DU-position convention. `$type` (0x24) sorts
    /// before every lower-case data key, so it is always the canonical first key after `render`.
    let typed (tag: string) (fields: (string * JVal) list) : JVal = JObj(("$type", JStr tag) :: fields)

/// Total decode combinators over the portable `Json.parse` → `JVal` model. Decode is now
/// **fully portable** — the same combinators run under .NET and Fable (the prior
/// `#if !FABLE_COMPILER` System.Text.Json path is retired, Phase 241). Each combinator
/// returns `Result<_, string>` so a failure *names what was expected* (the same envelope
/// discipline as the op algebra).
module Decode =

    /// A decoder reads a parsed `JVal`. Signature-identical across both pipelines.
    type Decoder<'T> = JVal -> Result<'T, string>

    let private kindName =
        function
        | JStr _ -> "string"
        | JInt _ -> "int"
        | JBool _ -> "bool"
        | JFloat _ -> "float"
        | JArr _ -> "array"
        | JObj _ -> "object"

    /// Parse a JSON string to a `JVal` root.
    let parse (json: string) : Result<JVal, string> = Json.parse json

    /// Parse a **foreign** JSON string that spells absent members `null` — object-member `null` is
    /// erased to absence, so every combinator below (`getProp` → `missing property: <name>`) behaves
    /// exactly as it does against the same document written without the token. The one-word swap a
    /// consumer makes to read a spec-conformant foreign document; everything downstream is unchanged.
    let parseTolerantOfNull (json: string) : Result<JVal, string> = Json.parseTolerantOfNull json

    let getProp (name: string) (el: JVal) : Result<JVal, string> =
        match el with
        | JObj fields ->
            match fields |> List.tryFind (fun (k, _) -> k = name) with
            | Some(_, v) -> Ok v
            | None -> Error("missing property: " + name)
        | other -> Error("expected object, got " + kindName other)

    let asString (el: JVal) : Result<string, string> =
        match el with
        | JStr s -> Ok s
        | other -> Error("expected string, got " + kindName other)

    let asInt (el: JVal) : Result<int, string> =
        match el with
        | JInt i -> Ok i
        | other -> Error("expected int, got " + kindName other)

    let asBool (el: JVal) : Result<bool, string> =
        match el with
        | JBool b -> Ok b
        | other -> Error("expected bool, got " + kindName other)

    let asFloat (el: JVal) : Result<float, string> =
        match el with
        | JFloat f -> Ok f
        | JInt i -> Ok(float i)
        | other -> Error("expected number, got " + kindName other)

    /// The discriminating `"kind"` tag of an object.
    let kindOf (el: JVal) : Result<string, string> =
        getProp "kind" el |> Result.bind asString

    let strField (name: string) (el: JVal) : Result<string, string> = getProp name el |> Result.bind asString

    let intField (name: string) (el: JVal) : Result<int, string> = getProp name el |> Result.bind asInt

    /// Decode every element of a JSON array with `d`. Short-circuits on the first error.
    let mapList (d: Decoder<'T>) (el: JVal) : Result<'T list, string> =
        match el with
        | JArr xs ->
            let rec go acc =
                function
                | [] -> Ok(List.rev acc)
                | x :: rest ->
                    match d x with
                    | Ok v -> go (v :: acc) rest
                    | Error m -> Error m

            go [] xs
        | other -> Error("expected array, got " + kindName other)

/// A single grid / chart / table row: an *open* name→value map (unlike a `TRecord`, whose
/// field set is fixed). Cells are boxed scalars — the shape the UI tier's decoded path and
/// its `Binding.Transform` resolution have always produced at runtime; naming it here makes
/// the rows slot wire-expressible without changing the representation (fuaran#665).
type Row = Map<string, obj>

/// Canonical codec for the typed row-source payload (fuaran#665 — rows leave the
/// residual-`"<opaque>"` boundary). Encodes a `Row seq` as a JSON array of row objects with
/// scalar cells (WIRE_FORMAT §2 rules 5/11); decode accepts the typed form **and** the legacy
/// `"<opaque>"` sentinel indefinitely (read-compat — a pre-typed emission decodes to the empty
/// feed, exactly the old behaviour). Canonicality (Ordinal key sort, float layout, escaping) is
/// inherited from `Canon.render`, never re-implemented here.
module RowCodec =

    /// The residual-opaque sentinel the rows slot carried before the typed encoding.
    [<Literal>]
    let opaqueSentinel = "<opaque>"

    let private kindName =
        function
        | JStr _ -> "string"
        | JInt _ -> "int"
        | JBool _ -> "bool"
        | JFloat _ -> "float"
        | JArr _ -> "array"
        | JObj _ -> "object"

    /// Best-effort scalar cell encode over the boxed-cell seam — the rule-11 recognised set
    /// (string / bool / int / int64 / float / float32 / DateTimeOffset / DateTime → Unix
    /// seconds), anything else the `"<opaque>"` sentinel, a `null` cell omitted (rule 4:
    /// absence is structural). The `float` test runs FIRST: under Fable every number satisfies
    /// every numeric type test (`typeof x === "number"`), so float-first routes all JS numbers
    /// through the canonical float layout — byte-identical to .NET, where the boxed types are
    /// exact and the arm order is immaterial. Integral floats render in integer form (rule 5
    /// shortest round-trip), so a .NET `box 42` (→ `JInt`) and a Fable `42` (→ `JFloat`) emit
    /// the same bytes.
    let private encodeCell (v: obj) : JVal option =
        match v with
        | null -> None
        | :? string as s -> Some(JStr s)
        | :? bool as b -> Some(JBool b)
        | :? float as f -> Some(JFloat f)
        | :? int as n -> Some(JInt n)
        | :? int64 as n -> Some(JFloat(float n))
        | :? float32 as f -> Some(JFloat(float f))
        | :? System.DateTimeOffset as t -> Some(JFloat(float (t.ToUnixTimeSeconds())))
        | :? System.DateTime as t ->
            Some(JFloat(float (System.DateTimeOffset(t.ToUniversalTime(), System.TimeSpan.Zero).ToUnixTimeSeconds())))
        | _ -> Some(JStr opaqueSentinel)

    /// Encode a row feed as a JSON array of row objects. An empty feed encodes `[]`, never
    /// `null`. No runtime test recognises a *row* (the slot is statically typed — the point
    /// of fuaran#665 design C); only the cell seam is best-effort.
    let encodeRows (rows: Row seq) : JVal =
        JArr
            [ for row in rows ->
                  JObj(
                      row
                      |> Map.toList
                      |> List.choose (fun (k, v) -> encodeCell v |> Option.map (fun jv -> k, jv))
                  ) ]

    /// A decoded cell is a boxed scalar: numbers surface as `float` (JSON has one number
    /// population — see the `JVal` numeric-normalization note), strings/bools as themselves.
    /// Nested arrays / objects are carried structurally (boxed `obj list` / `Row`) so a lenient
    /// ingest is not rejected — but they are display-opaque and re-encode as `"<opaque>"`
    /// cells (the residual boundary, narrowed to the cell seam).
    let rec private decodeCell (j: JVal) : obj =
        match j with
        | JStr s -> box s
        | JBool b -> box b
        | JInt n -> box (float n)
        | JFloat f -> box f
        | JArr xs -> box (xs |> List.map decodeCell)
        | JObj fields -> box (fields |> List.map (fun (k, v) -> k, decodeCell v) |> Map.ofList)

    /// Decode a rows payload: the typed array form, or the legacy `"<opaque>"` sentinel
    /// (→ the empty feed, read-compat with every pre-typed emission). Any other shape is a
    /// named error.
    let decodeRows (j: JVal) : Result<Row seq, string> =
        match j with
        | JStr s when s = opaqueSentinel -> Ok Seq.empty
        | JArr xs ->
            let rec go acc rest =
                match rest with
                | [] -> Ok(List.rev acc |> Seq.ofList)
                | JObj fields :: tail ->
                    let row = fields |> List.map (fun (k, v) -> k, decodeCell v) |> Map.ofList

                    go (row :: acc) tail
                | other :: _ -> Error("rows: expected a row object, got " + kindName other)

            go [] xs
        | other -> Error("rows: expected an array of row objects or \"<opaque>\", got " + kindName other)

/// Wire versioning + the forward/backward-compatibility contract (Phase 319). A versioned
/// wire format lets an *older* consumer meet a *newer* artifact and **detect → preserve →
/// degrade** instead of crashing — while the authoring/generation surface stays closed and
/// exhaustive (no host can *emit* an unknown kind). Everything here is FSharp.Core-only and
/// Fable-clean: it composes the `Json` / `Canon` / `Decode` primitives, never re-implementing
/// the canonical byte rules. The split is load-bearing:
///   • the producer authors against a closed surface and stamps the artifact with its profile;
///   • tolerance lives **only** on the decode boundary of a consumer that is `Behind` — the
///     transport-only `Unknown` is reachable on decode, un-constructible on encode.
module Versioning =

    /// A wire profile id — `<name>@<major>.<minor>` (e.g. `core@1.0`). `Name` is the capability
    /// namespace; `Major` is the `/vN/` incompatibility boundary (a removal/rename mints a new
    /// major — old consumers cannot interpret it, see `negotiate`); `Minor` is the additive
    /// capability counter (a new kind/case/field bumps the minor — an older consumer tolerates
    /// it via the must-ignore-but-preserve rule).
    type Profile =
        { Name: string; Major: int; Minor: int }

    module Profile =

        /// The canonical string form: `<name>@<major>.<minor>`.
        let render (p: Profile) : string =
            p.Name + "@" + string p.Major + "." + string p.Minor

        /// The base `core` profile — `core@1.0`.
        let coreV1: Profile = { Name = "core"; Major = 1; Minor = 0 }

        /// Parse `<name>@<major>.<minor>`. Names a typed `Error` on any malformed shape — the
        /// same envelope discipline as the parser (no exceptions escape).
        let tryParse (s: string) : Result<Profile, string> =
            let at = s.LastIndexOf '@'

            if at <= 0 || at = s.Length - 1 then
                Error("malformed profile (expected '<name>@<major>.<minor>'): " + s)
            else
                let name = s.Substring(0, at)
                let ver = s.Substring(at + 1)
                let parts = ver.Split('.')

                let parseInt (t: string) =
                    match System.Int32.TryParse t with
                    | true, v when v >= 0 -> Some v
                    | _ -> None

                match parts with
                | [| maj; min |] ->
                    match parseInt maj, parseInt min with
                    | Some major, Some minor ->
                        Ok
                            { Name = name
                              Major = major
                              Minor = minor }
                    | _ -> Error("malformed profile version (expected non-negative '<major>.<minor>'): " + ver)
                | _ -> Error("malformed profile version (expected '<major>.<minor>'): " + ver)

    /// The capability-negotiation outcome of a consumer reading an artifact's authored profile.
    type Compatibility =
        /// Authored at-or-below the consumer's profile (same name + major) — decode fully.
        | Current
        /// Authored *ahead* of the consumer (same name + major, higher minor) — the consumer
        /// may meet kinds it does not understand; it must tolerate (preserve + degrade), not crash.
        | Behind of authored: Profile
        /// A different namespace or a different major — an incompatible `/vN/` boundary the
        /// consumer cannot interpret at all (hard-refuse, never silently mis-decode).
        | Foreign of authored: Profile

    /// Negotiate a consumer's supported `Profile` against an artifact's authored `Profile`.
    /// Minor-ahead is `Behind` (tolerable); a different name or major is `Foreign` (refuse).
    let negotiate (consumer: Profile) (authored: Profile) : Compatibility =
        if authored.Name <> consumer.Name || authored.Major <> consumer.Major then
            Foreign authored
        elif authored.Minor > consumer.Minor then
            Behind authored
        else
            Current

    [<Literal>]
    let profileKey = "$profile"

    [<Literal>]
    let payloadKey = "$payload"

    [<Literal>]
    let requiredProfileKey = "requiredProfile"

    /// A versioned wire envelope: the producer's authored `Profile` + the artifact `Payload`
    /// (a `Node` / `TreeOp` JVal). `$profile` / `$payload` keys are `$`-prefixed so they sort
    /// before any lower-case data key under `Canon.render`. The envelope is the
    /// capability-negotiation carrier — a consumer reads `$profile`, `negotiate`s, then decodes
    /// `$payload` (tolerantly when `Behind`).
    type Envelope = { Profile: Profile; Payload: JVal }

    /// Build the canonical envelope JVal.
    let encode (env: Envelope) : JVal =
        JObj [ payloadKey, env.Payload; profileKey, JStr(Profile.render env.Profile) ]

    /// Render an envelope to canonical wire bytes.
    let render (env: Envelope) : string = Canon.render (encode env)

    /// Decode an envelope JVal — reads `$profile` (parsed) + the verbatim `$payload`.
    let decode (el: JVal) : Result<Envelope, string> =
        Decode.getProp profileKey el
        |> Result.bind Decode.asString
        |> Result.bind Profile.tryParse
        |> Result.bind (fun p ->
            Decode.getProp payloadKey el
            |> Result.map (fun payload -> { Profile = p; Payload = payload }))

    /// Parse + decode an envelope from wire bytes.
    let parse (s: string) : Result<Envelope, string> = Json.parse s |> Result.bind decode

    /// A kind the consumer does not understand, captured on the decode boundary. **Transport-only**:
    /// it is reachable here and nowhere on the authoring/encode path — no host can construct one to
    /// emit. `Payload` is the *verbatim parsed object*, so re-rendering it reproduces the producer's
    /// bytes (must-ignore-but-preserve); `RequiredProfile` is the profile the artifact declared it
    /// needs (when present), so the consumer can name what it is missing in a degraded placeholder.
    type UnknownKind =
        { Kind: string
          Payload: JVal
          RequiredProfile: Profile option }

    /// The result of a tolerant decode: a fully-understood `'T`, or a preserved `Unknown`.
    type Decoded<'T> =
        | Known of 'T
        | Unknown of UnknownKind

    /// Read an optional `requiredProfile` declaration off an artifact object (the
    /// "artifact declares the profile it requires" shape). Malformed / absent ⇒ `None`.
    let private readRequiredProfile (el: JVal) : Profile option =
        match Decode.getProp requiredProfileKey el with
        | Ok(JStr s) ->
            match Profile.tryParse s with
            | Ok p -> Some p
            | Error _ -> None
        | _ -> None

    /// Tolerantly decode one artifact object. `tagOf` reads its discriminator; `isKnown` reports
    /// whether this consumer understands that tag; `decodeKnown` decodes a known one. An
    /// *unrecognised* tag is NOT an error — it becomes a transport-only `Unknown` carrying the
    /// verbatim parsed `Payload` and any declared `requiredProfile`. This is the whole
    /// forward-compatibility seam: an older consumer reading a newer artifact detects the unknown
    /// kind here rather than hard-rejecting (`UNKNOWN_DU_CASE` / `WRONG_NODE_KIND`). A genuinely
    /// malformed object (no discriminator at all) still fails via `tagOf`.
    let decodeTolerant
        (tagOf: JVal -> Result<string, string>)
        (isKnown: string -> bool)
        (decodeKnown: JVal -> Result<'T, string>)
        (el: JVal)
        : Result<Decoded<'T>, string> =
        tagOf el
        |> Result.bind (fun tag ->
            if isKnown tag then
                decodeKnown el |> Result.map Known
            else
                Ok(
                    Unknown
                        { Kind = tag
                          Payload = el
                          RequiredProfile = readRequiredProfile el }
                ))

    /// Re-encode a tolerant decode back to a JVal. The `Unknown` branch returns its preserved
    /// `Payload` **verbatim** — must-ignore-but-preserve, so an old client cannot destroy data a
    /// newer producer authored. Composed with `Canon.render` (deterministic key order) the
    /// unknown artifact round-trips byte-for-byte, which is what makes preservation verifiable on
    /// the op-stream hash chain.
    let reencode (encodeKnown: 'T -> JVal) (d: Decoded<'T>) : JVal =
        match d with
        | Known v -> encodeKnown v
        | Unknown u -> u.Payload

    /// The classification of a capability change between two sets of kind tags (the IDL-diff
    /// shape the generator drives migration from). Additive-only — tags added, none removed or
    /// renamed — is a *minor* bump an older consumer tolerates via must-ignore-but-preserve. Any
    /// removal or rename (a tag present `before` and absent `after`) is *breaking* — a new `/vN/`
    /// major boundary requiring migration shims.
    type Evolution =
        | Additive of added: string list
        | Breaking of removed: string list * added: string list

    /// Classify the kind-tag delta `before` → `after`. No removals ⇒ `Additive`; any removal ⇒
    /// `Breaking`. (A *rename* surfaces as a removal + an add — correctly `Breaking`.)
    let classify (before: Set<string>) (after: Set<string>) : Evolution =
        let added = Set.difference after before |> Set.toList
        let removed = Set.difference before after |> Set.toList

        if List.isEmpty removed then
            Additive added
        else
            Breaking(removed, added)

    /// The profile a `baseProfile` bumps to under an `Evolution`: a no-op additive leaves it
    /// untouched; an additive bumps the minor (same major — older consumers stay compatible); a
    /// breaking change bumps the major and resets the minor (a `/vN/` boundary — older consumers
    /// become `Foreign`).
    let bump (baseProfile: Profile) (ev: Evolution) : Profile =
        match ev with
        | Additive [] -> baseProfile
        | Additive _ ->
            { baseProfile with
                Minor = baseProfile.Minor + 1 }
        | Breaking _ ->
            { baseProfile with
                Major = baseProfile.Major + 1
                Minor = 0 }

/// Conformance-corpus tooling — manifest + round-trip/reject runner + coverage gate,
/// parameterised by a domain's codec. The methodology (not the per-kind cases) is the
/// reusable credibility asset. It only drives the `Codec` — portable, Fable-clean.
module Corpus =

    /// A domain's encode + total decode pair.
    type Codec<'T> =
        { Encode: 'T -> string
          Decode: string -> Result<'T, string> }

    type CaseKind =
        | RoundTrip
        | Reject

    /// One corpus fixture: a `RoundTrip` JSON that must decode→encode→decode to an equal
    /// value, or a `Reject` JSON the decoder must refuse. `Tag` feeds the coverage gate.
    type Case =
        { Name: string
          Kind: CaseKind
          Json: string
          Tag: string }

    type Outcome =
        { Name: string
          Passed: bool
          Detail: string }

    /// Value-level round-trip: `encode v` must decode back to a structurally-equal value.
    let roundTrip (codec: Codec<'T>) (v: 'T) : Result<unit, string> =
        match codec.Decode(codec.Encode v) with
        | Ok v2 when v2 = v -> Ok()
        | Ok _ -> Error "round-trip produced a different value"
        | Error m -> Error("re-decode failed: " + m)

    let runCase (codec: Codec<'T>) (c: Case) : Outcome =
        match c.Kind with
        | RoundTrip ->
            match codec.Decode c.Json with
            | Error m ->
                { Name = c.Name
                  Passed = false
                  Detail = "decode failed: " + m }
            | Ok v ->
                match roundTrip codec v with
                | Ok() ->
                    { Name = c.Name
                      Passed = true
                      Detail = "ok" }
                | Error m ->
                    { Name = c.Name
                      Passed = false
                      Detail = m }
        | Reject ->
            match codec.Decode c.Json with
            | Error _ ->
                { Name = c.Name
                  Passed = true
                  Detail = "rejected as expected" }
            | Ok _ ->
                { Name = c.Name
                  Passed = false
                  Detail = "expected reject but decoded" }

    let runCorpus (codec: Codec<'T>) (cases: Case list) : Outcome list = cases |> List.map (runCase codec)

    /// Coverage gate: every required kind/op tag must be exercised by at least one case.
    /// Surfaces silent corpus gaps (the forward-coupling discipline).
    let coverageGate (required: string list) (cases: Case list) : Result<unit, string> =
        let seen = cases |> List.map (fun c -> c.Tag) |> Set.ofList
        let missing = required |> List.filter (fun t -> not (seen.Contains t))

        if List.isEmpty missing then
            Ok()
        else
            Error("corpus missing coverage for: " + String.concat ", " missing)

    // ---- generative round-trip fuzzing (Phase 18) ----
    // The fixed corpus runs hand-written fixtures; this generates a wide random sample of valid
    // `JVal` and asserts the parser and renderer stay mutually consistent — exactly the depth /
    // escaping / number-format coverage the fixtures cannot enumerate by hand. Self-contained: a
    // tiny uint32 LCG (the same arithmetic class as Conformance's `ConfRng`, inlined because
    // `Fuaran.Core.Wire` takes no dependency on `Fuaran.Core.Conformance`), seed-replayable so a
    // counterexample reproduces. Fable-clean.

    /// A small alphabet that exercises every escape class plus ordinary characters.
    let private fuzzAlphabet =
        [| 'a'; 'z'; '0'; ' '; '"'; '\\'; '/'; '\n'; '\r'; '\t'; '\b'; '\f' |]

    /// Generate one random valid `JVal` from `seed`, nesting no deeper than `maxDepth`.
    let private genJVal (seed: int) (maxDepth: int) : JVal =
        let mutable st = (uint32 seed * 2654435761u) + 1u

        let next () =
            st <- (st * 1664525u) + 1013904223u
            int (st >>> 1)

        let pick (n: int) = next () % n

        let randStr () =
            let len = pick 6

            System.String(Array.init len (fun _ -> fuzzAlphabet.[pick fuzzAlphabet.Length]))

        let rec gen (depth: int) : JVal =
            // at the depth limit only scalars are generated (no further nesting)
            match pick (if depth >= maxDepth then 4 else 6) with
            | 0 -> JStr(randStr ())
            | 1 -> JInt(pick 20000 - 10000)
            | 2 -> JBool(pick 2 = 0)
            | 3 ->
                // a finite float (Phase 12 bars non-finite); the +0.25 keeps a fractional part,
                // though the string-idempotence law below tolerates integer-valued floats too.
                (float (pick 10000) + 0.25) * (if pick 2 = 0 then 1.0 else -1.0) |> JFloat
            | 4 -> JArr [ for _ in 0 .. pick 4 -> gen (depth + 1) ]
            | _ -> JObj [ for _ in 0 .. pick 4 -> randStr (), gen (depth + 1) ]

        gen 0

    /// Generative round-trip law: over `count` seed-replayable random `JVal`s, `render` must be
    /// idempotent under a `parse` round-trip — `parse (render v) |> Result.map render = Ok (render v)`.
    /// The string form is robust to the one documented canonical normalisation (an integer-valued
    /// `JFloat` renders without a point and re-parses as `JInt`); the rendered text still round-trips.
    /// Returns the first counterexample's seed + offending output as an `Error`.
    let fuzzRoundTrip (seed: int) (count: int) (maxDepth: int) : Result<unit, string> =
        let rec go i =
            if i >= count then
                Ok()
            else
                let v = genJVal (seed + i) maxDepth
                let s = Json.render v

                match Json.parse s with
                | Ok v2 when Json.render v2 = s -> go (i + 1)
                | Ok v2 -> Error(sprintf "fuzz seed=%d: render not idempotent (%s vs %s)" (seed + i) s (Json.render v2))
                | Error m -> Error(sprintf "fuzz seed=%d: parse rejected rendered output %s — %s" (seed + i) s m)

        go 0

    /// Generative codec round-trip law (Phase 20): over `count` seed-replayable values from a
    /// domain's `gen` (seed → `'T`), `decode (encode v)` must reproduce a structurally-equal `'T`
    /// (`roundTrip`, which `'T` equality already backs). Generalises `fuzzRoundTrip` from raw
    /// `JVal` to a domain's own `Codec<'T>`, turning the hand-written corpus into property
    /// coverage and seeding the eval suite. Returns the first counterexample's seed as an `Error`.
    /// Self-contained — the generator is the caller's; no `Conformance` dependency.
    let codecLaws (codec: Codec<'T>) (gen: int -> 'T) (seed: int) (count: int) : Result<unit, string> =
        let rec go i =
            if i >= count then
                Ok()
            else
                match roundTrip codec (gen (seed + i)) with
                | Ok() -> go (i + 1)
                | Error m -> Error(sprintf "codecLaws seed=%d: %s" (seed + i) m)

        go 0
