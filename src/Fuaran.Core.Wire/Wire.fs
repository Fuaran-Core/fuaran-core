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

    /// The JSON kind of a value, as every decode error on the spine names it (`string`, `int`,
    /// `bool`, `float`, `array`, `object`). Public once (Phase 299): `Decode`, `RowCodec` and the
    /// columnar codec each kept a private copy of this match.
    let kindName (v: JVal) : string =
        match v with
        | JStr _ -> "string"
        | JInt _ -> "int"
        | JBool _ -> "bool"
        | JFloat _ -> "float"
        | JArr _ -> "array"
        | JObj _ -> "object"

    /// THE spelling of a non-finite float on the spine (Phase 299): `NaN`, `Infinity` or
    /// `-Infinity`, and `None` for a finite value. Every place that names one — the guarded
    /// renderers' refusals, the canonical float layout's quoted token, the columnar codec's
    /// `NonFiniteFloat` and the column layer's distinct token — reads it here, so a non-finite
    /// value has one name on every surface.
    let nonFiniteToken (f: float) : string option =
        if System.Double.IsNaN f then
            Some "NaN"
        elif System.Double.IsPositiveInfinity f then
            Some "Infinity"
        elif System.Double.IsNegativeInfinity f then
            Some "-Infinity"
        else
            None

/// The single float -> string LAYOUT, shared by `Json.render` and `Canon.canonicalFloat`, and
/// value-identical on both pipelines. .NET's round-trip specifier (`"R"`) is what the layout IS,
/// and it is also the one thing Fable will not do: `String.format` REFUSES `R` at RUNTIME, so a
/// `"{0:R}"` on a Fable-targeted surface compiles cleanly and throws in the browser. The re-lay
/// below reproduces that layout from JS's own shortest-round-trip digits, so both pipelines emit
/// the same bytes (WIRE_FORMAT §2 rule 5). Certified rather than asserted since Phase 118:
/// the Fable consumer's parity leg runs `ParityVectors` under a JS runtime and byte-compares it
/// against the .NET run (STABILITY.md "Fable cleanliness").
///
/// PUBLIC since Phase 315 (it was `internal`): a host that lays a float out anywhere but the wire —
/// an SVG coordinate, a label — used to keep its own copy of this re-lay and a note to keep it in
/// sync. `finite` and `roundTrip` are the layout; the `floatLayout/*` parity vectors pin them.
module FloatLayout =

#if FABLE_COMPILER
    [<Fable.Core.Emit("$0.toString()")>]
    let private jsNumberToString (n: float) : string = Fable.Core.Util.jsNative

    /// Re-lay JS's shortest-round-trip digits into .NET `ToString("R")` form (WIRE_FORMAT §2 rule 5)
    /// so the Fable host is byte-identical to the .NET host across the whole finite-double range.
    /// Ported verbatim from the UI host's `CanonicalJson.formatFiniteDouble`.
    let private reLay (n: float) : string =
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

    /// A FINITE double in .NET's round-trip (`"R"`) layout, on either pipeline. `-0.0` keeps its
    /// sign here — collapsing it is `Canon.canonicalFloat`'s wire rule, not the layout's.
    let finite (n: float) : string =
#if FABLE_COMPILER
        if n = 0.0 then
            // `reLay` short-circuits both zeroes to "0"; .NET's "R" spells the negative one "-0",
            // and `1.0 / n` is the only way to read the sign of a JS negative zero.
            (if 1.0 / n < 0.0 then "-0" else "0")
        else
            reLay n
#else
        n.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
#endif

    /// The full `"{0:R}"` rendering, non-finite tokens included (`NaN` / `Infinity` / `-Infinity`,
    /// the invariant-culture spellings). `Json.render`'s float case: those tokens are not valid
    /// JSON, which is exactly why `Json.tryRender` exists to name them, so the layout keeps
    /// producing them rather than quietly substituting something parseable.
    let roundTrip (f: float) : string =
        if System.Double.IsNaN f then "NaN"
        elif System.Double.IsPositiveInfinity f then "Infinity"
        elif System.Double.IsNegativeInfinity f then "-Infinity"
        else finite f


/// Fable-clean encode helpers + the wire-envelope discipline + a portable
/// (FSharp.Core-only) parser. Per-kind cases stay domain-side; the core owns the
/// envelope shape, the combinators, and the parser. `render` and `parse` are inverses
/// over canonical wire JSON.
module Json =

    /// THE string escape of the spine (Phase 287; DECISIONS.md "the spine owns the string-escaping
    /// rule"). Exactly three classes are escaped and nothing else: `"` as `\"`, `\` as `\\`, and
    /// every control character `U+0000`–`U+001F` as `\u00xx` with LOWER-CASE hex — including `\n`,
    /// `\r` and `\t`, which have NO short form here. It is byte-for-byte the UI host's
    /// `CanonicalJson.appendRawString` and the TypeScript twin's escaper, so a hash pre-image that
    /// carries a string means one thing on every host. `Canon.render` escapes through this function
    /// too; the two copies the standalone layers keep (`Actor.encode` in `Fuaran.Core.OpStream`,
    /// `Dag.toJsonl` in `Fuaran.Core.OpStream.Dag` — D2 forbids them a reference here) are held
    /// value-identical to it by `StringEscapeVectors` in the conformance kit. The parser accepts
    /// both the short and the `\u` spelling, so nothing changes on read.
    let escape (s: string) : string =
        let sb = System.Text.StringBuilder()

        for ch in s do
            match ch with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | c when int c < 0x20 -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append(c) |> ignore

        sb.ToString()

    // ---- THE WRITER (Phase 306) ----
    // One iterative writer stands behind `Json.render`, `Canon.render`, `Canon.renderOrdered` and
    // both `tryRender`s. Each used to be a recursive function mapping itself over a list, so its
    // stack depth was the VALUE's nesting depth: a constructed value some 1,400 deep killed the
    // process on a 1 MB thread (a stack overflow is uncatchable on .NET), the guarded renderers
    // included, while the parse cap protected only the read side. The pending work is held on an
    // explicit stack in the heap instead, so depth costs memory and never the process.

    /// One unit of pending output: a value still to write, or the rest of an open array or object
    /// (`first` is false once a member has been written, so the next one takes a comma).
    type private Pending =
        | Value of JVal
        | Items of rest: JVal list * first: bool
        | Members of rest: (string * JVal) list * first: bool

    /// Write `v` with `floatText` as the float layout and object members in Ordinal key order
    /// (`sortKeys`) or as authored. The bytes are the ones the recursive renderers wrote.
    let internal writeWith (sortKeys: bool) (floatText: float -> string) (v: JVal) : string =
        let sb = System.Text.StringBuilder()
        let mutable stack = [ Value v ]

        let quoted (s: string) =
            sb.Append('"').Append(escape s).Append('"') |> ignore

        while not stack.IsEmpty do
            match stack with
            | [] -> ()
            | Value x :: rest ->
                stack <- rest

                match x with
                | JStr s -> quoted s
                | JInt i -> sb.Append(string i) |> ignore
                | JBool b -> sb.Append(if b then "true" else "false") |> ignore
                | JFloat f -> sb.Append(floatText f) |> ignore
                | JArr xs ->
                    sb.Append('[') |> ignore
                    stack <- Items(xs, true) :: stack
                | JObj fields ->
                    sb.Append('{') |> ignore

                    let ordered =
                        if sortKeys then
                            fields
                            |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b))
                        else
                            fields

                    stack <- Members(ordered, true) :: stack
            | Items(xs, first) :: rest ->
                match xs with
                | [] ->
                    sb.Append(']') |> ignore
                    stack <- rest
                | x :: tail ->
                    if not first then
                        sb.Append(',') |> ignore

                    stack <- Value x :: Items(tail, false) :: rest
            | Members(fields, first) :: rest ->
                match fields with
                | [] ->
                    sb.Append('}') |> ignore
                    stack <- rest
                | (k, x) :: tail ->
                    if not first then
                        sb.Append(',') |> ignore

                    quoted k
                    sb.Append(':') |> ignore
                    stack <- Value x :: Members(tail, false) :: rest

        sb.ToString()

    /// Render a `JVal` in author order with the round-trip float layout. TOTAL AGAINST THE MACHINE
    /// (Phase 306): iterative, so a value of any nesting depth renders — the text of one nested past
    /// `defaultMaxDepth` is text `parse` then refuses by name, which is the read side's cap doing
    /// its job, not a crash.
    ///
    /// ONE DIVERGENCE FROM `Canon.render` THAT A PARSE COLLAPSES: `JFloat -0.0` renders `-0` here
    /// (the layout keeps the sign; `Canon.canonicalFloat` collapses it to `0`), and `-0` is an
    /// integer token, so it parses back as `JInt 0` and renders `0` from then on. The first
    /// `render` of a negative zero is therefore not a fixed point of `parse >> render`; every later
    /// one is. The bytes are left as they are because chain pre-images are built from this renderer.
    let render (v: JVal) : string = writeWith false FloatLayout.roundTrip v

    /// One step of a path into a value, innermost first while a scan holds it.
    type private Step =
        | Index of int
        | Key of string

    /// `$` for the root, `[i]` for an array item, `["key"]` for a member (the key under `escape`).
    let private pathText (stepsRev: Step list) : string =
        let sb = System.Text.StringBuilder("$")

        for step in List.rev stepsRev do
            match step with
            | Index i -> sb.Append('[').Append(string i).Append(']') |> ignore
            | Key k -> sb.Append("[\"").Append(escape k).Append("\"]") |> ignore

        sb.ToString()

    /// A frame of the document-order scan: a value at a path, or the rest of an array or object.
    type private Frame =
        | At of Step list * JVal
        | RestItems of Step list * int * JVal list
        | RestMembers of Step list * (string * JVal) list

    /// The first hit of a document-order scan — arrays by index, members in AUTHORED order, a
    /// member's KEY before its value — as `(path, finding)`. `ofKey` reads a member key (its path
    /// is the member's); `ofValue` reads a scalar. Iterative, for the reason the writer is, and a
    /// path is only rendered for the hit.
    let private firstInDocumentOrder
        (ofKey: string -> string option)
        (ofValue: JVal -> string option)
        (v: JVal)
        : (string * string) option =
        let mutable stack = [ At([], v) ]
        let mutable found = None

        while found.IsNone && not stack.IsEmpty do
            match stack with
            | [] -> ()
            | At(path, x) :: rest ->
                stack <- rest

                match x with
                | JArr xs -> stack <- RestItems(path, 0, xs) :: stack
                | JObj fields -> stack <- RestMembers(path, fields) :: stack
                | scalar -> found <- ofValue scalar |> Option.map (fun finding -> pathText path, finding)
            | RestItems(path, i, xs) :: rest ->
                match xs with
                | [] -> stack <- rest
                | x :: tail -> stack <- At(Index i :: path, x) :: RestItems(path, i + 1, tail) :: rest
            | RestMembers(path, fields) :: rest ->
                match fields with
                | [] -> stack <- rest
                | (k, x) :: tail ->
                    match ofKey k with
                    | Some finding -> found <- Some(pathText (Key k :: path), finding)
                    | None -> stack <- At(Key k :: path, x) :: RestMembers(path, tail) :: rest

        found

    /// The FIRST non-finite `JFloat` in `v`, in document order — arrays by index, object members in
    /// AUTHORED order — as `(path, token)`: the path is `$` for the root, `[i]` for an array item and
    /// `["key"]` for a member (the key under `escape`, so the path is unambiguous for any key), and
    /// the token is `JVal.nonFiniteToken`'s. `None` where every float is finite. Public once
    /// (Phase 299): it is the scan both guarded renderers (`Json.tryRender`, `Canon.tryRender`)
    /// refuse on, which each used to carry as a private copy. Iterative since Phase 306.
    let firstNonFinite (v: JVal) : (string * string) option =
        firstInDocumentOrder
            (fun _ -> None)
            (fun x ->
                match x with
                | JFloat f -> JVal.nonFiniteToken f
                | _ -> None)
            v

    /// The index of the first UTF-16 unit of `s` that is not part of a character (Phase 306): a
    /// high surrogate (`D800`–`DBFF`) not followed at once by a low one, or a low surrogate
    /// (`DC00`–`DFFF`) with no high one before it. `None` where `s` is well-formed UTF-16 — the
    /// strings that have code points, and the only ones `parse` accepts.
    let firstIllFormedUnit (s: string) : int option =
        let n = s.Length
        let mutable k = 0
        let mutable found = None

        while found.IsNone && k < n do
            let u = int s.[k]

            if u >= 0xD800 && u <= 0xDBFF then
                if k + 1 < n && int s.[k + 1] >= 0xDC00 && int s.[k + 1] <= 0xDFFF then
                    k <- k + 2
                else
                    found <- Some k
            elif u >= 0xDC00 && u <= 0xDFFF then
                found <- Some k
            else
                k <- k + 1

        found

    /// True where `s` is well-formed UTF-16 (`firstIllFormedUnit s = None`).
    let isWellFormedUtf16 (s: string) : bool = (firstIllFormedUnit s).IsNone

    /// The FIRST string in `v` that is not well-formed UTF-16, in document order — a member's key
    /// before its value — as `(path, description)`; the description names the unit and its index
    /// and says whether the string is a member key. `None` where every string has code points.
    /// Both guarded renderers refuse on it (Phase 306).
    let firstIllFormedString (v: JVal) : (string * string) option =
        let describe (what: string) (s: string) : string option =
            firstIllFormedUnit s
            |> Option.map (fun k ->
                what
                + " holds the unpaired surrogate U+"
                + (int s.[k]).ToString("X4")
                + " at unit "
                + string k)

        firstInDocumentOrder
            (describe "a member key")
            (fun x ->
                match x with
                | JStr s -> describe "a string" s
                | _ -> None)
            v

    /// A `"kind"`-tagged object — the wire envelope every domain node/op serialises as.
    /// `tag` leads; `fields` follow in author order (camelCase keys by discipline).
    let kindObj (tag: string) (fields: (string * JVal) list) : JVal = JObj(("kind", JStr tag) :: fields)

    let encode (v: JVal) : string = render v

    /// Total, guarded render (Phase 12). `render` formats a `JFloat` with `"{0:R}"`, so a
    /// non-finite float (`NaN` / `Infinity` / `-Infinity`) emits a token that is not valid JSON
    /// and that `parse` then rejects — `render` succeeds but produces un-parseable wire, breaking
    /// `render ∘ parse = id`. The Fuaran wire model has no non-finite float (the same posture as
    /// "no null"). `tryRender` names the first non-finite `JFloat` as a typed `Error` instead.
    ///
    /// Since Phase 306 it refuses a second class for the same reason: a string (or member key)
    /// that is not well-formed UTF-16. `render` writes a lone surrogate through as it found it, and
    /// `parse` refuses the text — so that, too, was un-parseable wire from a guarded entry point.
    /// A non-finite float is looked for first, so every refusal this function already made keeps
    /// its message. Over a value with neither it is exactly `Ok (render v)`, at any nesting depth.
    let tryRender (v: JVal) : Result<string, string> =
        match firstNonFinite v with
        | Some(_, tok) -> Error("non-finite float is not representable on the Fuaran wire: " + tok)
        | None ->
            match firstIllFormedString v with
            | Some(path, what) ->
                Error(
                    "ill-formed string is not representable on the Fuaran wire: "
                    + what
                    + " at "
                    + path
                )
            | None -> Ok(render v)

    /// `tryRender` under the `encode` name — the total, guarded encode entry point.
    let tryEncode (v: JVal) : Result<string, string> = tryRender v

    /// THE integer reader of the wire (Phase 299): `Int32.TryParse` under the INVARIANT culture with
    /// `NumberStyles.AllowLeadingSign` and nothing else — no white space, no separators, no culture's
    /// own minus sign. The bare `Int32.TryParse tok` it replaces read under the CURRENT culture, so
    /// under a culture whose negative sign is not U+002D (fa-IR, he-IL) `-5` failed the Int32 read,
    /// fell to the float path and parsed as `JFloat -5.0`: one document decoded differently by
    /// server locale while its bytes and digests agreed. `NumberStyles.None` would refuse the sign
    /// itself, sending every negative integer to the float path on every host. `parseNumber` and
    /// `Versioning.Profile.tryParse` both read through this one function.
    ///
    /// THE TOKEN IS HELD TO `[+-]?[0-9]+` FIRST (Phase 306). The host reader is not that strict
    /// on its own: it has always tolerated trailing NUL characters, so `"7\u0000"` read as `7`.
    /// The parser never hands it such a token (the scanner collects digits, and the grammar check
    /// refuses a `+`), but this function is public and the profile grammar reads through it — so
    /// the shape is checked here, where every caller gets it.
    let readInt32 (tok: string) : int option =
        let digitsFrom =
            if tok.Length > 0 && (tok.[0] = '-' || tok.[0] = '+') then
                1
            else
                0

        let mutable shaped = tok.Length > digitsFrom

        for k in digitsFrom .. tok.Length - 1 do
            if tok.[k] < '0' || tok.[k] > '9' then
                shaped <- false

        if not shaped then
            None
        else
            match
                System.Int32.TryParse(
                    tok,
                    System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            with
            | true, v -> Some v
            | _ -> None

    /// True where `tok` is a number token of the JSON grammar, exactly (Phase 299; RFC 8259 §6):
    ///
    ///     -? (0 | [1-9][0-9]*) (\.[0-9]+)? ([eE][+-]?[0-9]+)?
    ///
    /// The parser holds every number token to it before reading one, so a leading zero (`01`), a
    /// point with no digit before or after it (`-.5`, `1.`, `1.e5`) and an exponent with no digit
    /// (`1e`, `1e+`) are each a `MalformedNumber` — where the scanner used to hand such a token to a
    /// host number reader that accepted some of them, and not the same ones on every host.
    let isJsonNumber (tok: string) : bool =
        let n = tok.Length

        let isDigit (k: int) =
            k < n && tok.[k] >= '0' && tok.[k] <= '9'

        let rec digitsFrom (k: int) =
            if isDigit k then digitsFrom (k + 1) else k

        // The integer part from `k`: a lone `0`, or a non-zero digit and any digits after it.
        let afterInt (k: int) =
            if k < n && tok.[k] = '0' then Some(k + 1)
            elif isDigit k then Some(digitsFrom k)
            else None

        // An optional `.` and at least one digit.
        let afterFrac (k: int) =
            if k < n && tok.[k] = '.' then
                let e = digitsFrom (k + 1)
                if e > k + 1 then Some e else None
            else
                Some k

        // An optional `e`/`E`, an optional sign, and at least one digit.
        let afterExp (k: int) =
            if k < n && (tok.[k] = 'e' || tok.[k] = 'E') then
                let s =
                    if k + 1 < n && (tok.[k + 1] = '+' || tok.[k + 1] = '-') then
                        k + 2
                    else
                        k + 1

                let e = digitsFrom s
                if e > s then Some e else None
            else
                Some k

        let start = if n > 0 && tok.[0] = '-' then 1 else 0

        match afterInt start |> Option.bind afterFrac |> Option.bind afterExp with
        | Some k -> k = n
        | None -> false

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

        let isHighSurrogate (u: int) = u >= 0xD800 && u <= 0xDBFF
        let isLowSurrogate (u: int) = u >= 0xDC00 && u <= 0xDFFF

        // A string is well-formed UTF-16 or it is REFUSED (Phase 299): every high surrogate is
        // followed at once by a low one, and every low one follows a high one — whichever spelling
        // each unit arrived in, a raw character or a `\u` escape. A lone or ill-ordered surrogate
        // has no code point, so a string carrying one is not a string of characters, and a digest
        // over it cannot mean one thing on every host (UTF-8 has no encoding for it; the platform
        // encoders each substitute their own replacement). Both spellings are refused as
        // `BadEscape`, the kind that already names "this string's content is not well-formed",
        // rather than a new kind every exhaustive match would have to learn.
        let parseString () : string =
            expect '"'
            let sb = System.Text.StringBuilder()
            let mutable fin = false
            // The last unit appended was a high surrogate still waiting for its low half.
            let mutable pendingHigh = false

            let append (u: int) =
                if pendingHigh && not (isLowSurrogate u) then
                    fail BadEscape "ill-formed string: a high surrogate not followed by a low surrogate"
                elif not pendingHigh && isLowSurrogate u then
                    fail BadEscape "ill-formed string: a low surrogate with no high surrogate before it"

                pendingHigh <- isHighSurrogate u
                sb.Append(char u) |> ignore

            while not fin do
                if i >= n then
                    fail UnterminatedString "unterminated string"

                let c = input.[i]
                i <- i + 1

                match c with
                | '"' ->
                    if pendingHigh then
                        fail BadEscape "ill-formed string: a high surrogate not followed by a low surrogate"

                    fin <- true
                | '\\' ->
                    if i >= n then
                        fail UnterminatedEscape "unterminated escape"

                    let e = input.[i]
                    i <- i + 1

                    match e with
                    | '"' -> append (int '"')
                    | '\\' -> append (int '\\')
                    | '/' -> append (int '/')
                    | 'n' -> append (int '\n')
                    | 'r' -> append (int '\r')
                    | 't' -> append (int '\t')
                    | 'b' -> append (int '\b')
                    | 'f' -> append (int '\f')
                    | 'u' ->
                        if i + 4 > n then
                            fail TruncatedUnicodeEscape "truncated \\u escape"

                        let code =
                            (hexDigit input.[i] <<< 12)
                            + (hexDigit input.[i + 1] <<< 8)
                            + (hexDigit input.[i + 2] <<< 4)
                            + hexDigit input.[i + 3]

                        i <- i + 4
                        append code
                    | _ -> fail BadEscape ("bad escape '\\" + string e + "'")
                | _ -> append (int c)

            sb.ToString()

        let isDigitAt (k: int) =
            k < n && input.[k] >= '0' && input.[k] <= '9'

        // The token is SCANNED as it always was — an optional sign, then digits, point, digits,
        // exponent, each optional — so a refusal reports the token and position it always did; it
        // is then held to the JSON number grammar (`isJsonNumber`) before anything reads it.
        let parseNumber () : JVal =
            let start = i
            let mutable isFloat = false

            if peek () = '-' then
                i <- i + 1

            while isDigitAt i do
                i <- i + 1

            if peek () = '.' then
                isFloat <- true
                i <- i + 1

                while isDigitAt i do
                    i <- i + 1

            if peek () = 'e' || peek () = 'E' then
                isFloat <- true
                i <- i + 1

                if peek () = '+' || peek () = '-' then
                    i <- i + 1

                while isDigitAt i do
                    i <- i + 1

            let tok = input.Substring(start, i - start)

            if not (isJsonNumber tok) then
                fail MalformedNumber ("malformed number: " + tok)

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
                match readInt32 tok with
                | Some v -> JInt v
                | None ->
                    // Safety is judged on the TOKEN, not on a parsed double: 2^53 + 1
                    // rounds to 2^53 as a double, so a range check on the value would
                    // wrongly accept it. An integer is int53-safe iff |value| ≤ 2^53 =
                    // 9007199254740992 (16 digits). Compare the digit string lexically —
                    // the grammar above refuses a leading zero, so for equal length that IS
                    // the numeric order. Fable-clean (string + Double.TryParse only, no Int64).
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
    ///
    /// The cap is THIS PARSER'S OWN stack guard, not the wire format's resource limits. Those
    /// (WIRE_FORMAT §21 — nesting, string and array sizes among them) belong to the wire-format
    /// hosts that decode on top of this parser, which enforce them with the format's own error;
    /// this parser enforces none of them and claims no §21 conformance (DECISIONS.md D84).
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

    // The canonical string escape (WIRE_FORMAT §2 rule 6) is `Json.escape`: only `"`, `\`, and
    // control chars (`U+0000`–`U+001F` → `\u00xx`, lower-case hex), no `\n`/`\r`/`\t` shortcuts —
    // byte-for-byte the UI host's `appendRawString`. The spine has one escaping rule (Phase 287)
    // and one writer (Phase 306, `Json.writeWith`), which every renderer here goes through.

    /// The single canonical, cross-host float → string encoder (Phase 55). Non-finite floats render to
    /// the fixed JSON-string tokens `"NaN"` / `"Infinity"` / `"-Infinity"`; `-0.0` collapses to `0`; a
    /// finite float uses `Double.ToString("R", InvariantCulture)` on .NET and the byte-identical JS
    /// shortest-round-trip re-layout (`formatFiniteDouble`) under Fable (WIRE_FORMAT §2 rule 5). Every
    /// float→wire / float→key path in the substrate routes through this one function so the bytes match
    /// across the .NET / Fable / TS / Python hosts. Pinned in `STABILITY.md`.
    let canonicalFloat (f: float) : string =
        match JVal.nonFiniteToken f with
        | Some tok -> "\"" + tok + "\""
        | None ->
            // -0 collapses to 0 (WIRE_FORMAT §2 rule 5) — the wire rule, applied before the layout.
            let v = if f = 0.0 then 0.0 else f
            FloatLayout.finite v

    /// Render a `JVal` under the canonical `$type` discipline: object keys Ordinal-sorted
    /// (recursively), the pinned float layout, canonical escaping. The encoder enforces key order;
    /// decoders stay order-tolerant (they look up by name). Iterative since Phase 306 (`Json`'s
    /// writer), so a value of any nesting depth renders; the bytes are unchanged.
    let render (v: JVal) : string = Json.writeWith true canonicalFloat v

    /// The GUARDED canonical render (Phase 165) — [[render]] with a refusal beside it, in the
    /// shape `Json.tryRender` gives `Json.render`.
    ///
    /// [[render]] spells a non-finite float as the QUOTED token `"NaN"` / `"Infinity"` /
    /// `"-Infinity"`, which is byte for byte what the STRING of those characters renders as. The
    /// wire it produces is valid, and wrong: a digest over `JFloat nan` equals the digest over
    /// `JStr "NaN"`, the value a reader decodes those bytes back to (`proofs/WireCanon.fst`,
    /// `render_aliases_nan` / `_pos_inf` / `_neg_inf`). This entry point names the FIRST non-finite
    /// `JFloat` in document order — arrays by index, object members in AUTHORED order — as a typed
    /// `Error` carrying its token and its path (`$` for the root, `[i]` for an array item,
    /// `["key"]` for a member, the key under the canonical escape so the path is unambiguous for
    /// any key). Over a value holding no non-finite float it is exactly `Ok (render v)`.
    ///
    /// The predicate is FINITENESS and nothing else — the first clause of the model's
    /// `float_canonical`. The format's documented normalisations are NOT refused: an
    /// integer-shaped finite float (`JFloat 2.0` renders `2`), the `-0` collapse, and the key
    /// sort all pass through unchanged, because each is what the format says rather than a value
    /// silently becoming another.
    ///
    /// [[render]] is untouched, and a value that aliases under it keeps aliasing under it: its
    /// bytes are pinned by the conformance corpus and by every other host. A caller that digests
    /// the canonical form and wants the refusal digests THIS function's `Ok` — there is no digest
    /// in this package to wrap (it references nothing that hashes), so the guarded digest is
    /// `tryRender v |> Result.map digest` at the caller, with the caller's own hash.
    ///
    /// A STRING THAT IS NOT WELL-FORMED UTF-16 IS REFUSED TOO (Phase 306), and for the same
    /// reason a non-finite float is: its digest is some other value's. A lone surrogate has no
    /// code point and UTF-8 has no encoding for it, so every encoder substitutes — over all
    /// 66,060,288 (high surrogate, non-low unit) pairs, 97.6% used to encode byte-identically to a
    /// well-formed astral character, and under the platform's replacement rule `"\uD800"`,
    /// `"\uDFFF"` and `"�"` are one byte string. Replacement only moves which strings collide;
    /// injectivity needs the refusal, here and at the parser, which makes it on read. The first
    /// such string in document order (a member's key before its value) is named with its path. A
    /// non-finite float is looked for first, so every refusal this function already made keeps its
    /// message. `proofs/WireCanon.fst` states what the two refusals buy: over values this function
    /// accepts, equal UTF-8 bytes of the rendering are equal normal forms.
    let tryRender (v: JVal) : Result<string, string> =
        match Json.firstNonFinite v with
        | Some(path, tok) -> Error("non-finite float has no canonical rendering of its own: " + tok + " at " + path)
        | None ->
            match Json.firstIllFormedString v with
            | Some(path, what) ->
                Error(
                    "ill-formed string has no canonical rendering of its own: "
                    + what
                    + " at "
                    + path
                )
            | None -> Ok(render v)

    /// Render a `JVal` with the SAME canonical escaping and pinned float layout as [[render]],
    /// but object keys in AUTHORED order — no sort. The declared-key-order leg (a vocabulary
    /// whose canonical form is DECLARATION order rather than Ordinal; the IDL's
    /// `WireShape.KeyOrder`): there the ENCODER is the order authority — it constructs each
    /// object's pairs in the declared order and this renderer preserves them, so canonical form
    /// stays unique without a sort. [[render]] is untouched and remains the cross-host default.
    let renderOrdered (v: JVal) : string = Json.writeWith false canonicalFloat v

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

    /// The structural fault a decode combinator meets, BEFORE any codec spells it (Phase 299). The
    /// combinators below come in two forms: the `string`-error ones every codec has always used,
    /// and a `…With` form generic over the error type, which takes the codec's own spelling of a
    /// `Fault`. A codec with a typed error envelope (the columnar codec's `ColumnError`) reuses the
    /// same traversal and keeps its own codes, rather than carrying a private copy of each
    /// combinator — which is what it did until this phase.
    type Fault =
        /// An object had no member of this name.
        | MissingProperty of name: string
        /// A value was of the wrong JSON kind: the kind expected, and the kind found (`JVal.kindName`).
        | WrongKind of expected: string * got: string

    /// A `Fault` in the words the `string`-error combinators have always used (`missing property:
    /// <name>`, `expected <kind>, got <kind>`) — byte-identical to before this type existed.
    let describe (fault: Fault) : string =
        match fault with
        | MissingProperty name -> "missing property: " + name
        | WrongKind(expected, got) -> "expected " + expected + ", got " + got

    /// The member `name` of an object — the FIRST, where a foreign document repeats a key, as every
    /// combinator here reads it — or `None` where it has none or `el` is not an object.
    let tryProp (name: string) (el: JVal) : JVal option =
        match el with
        | JObj fields -> fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd
        | _ -> None

    /// `getProp` over the caller's error type: `fault` spells a missing member or a non-object.
    let propWith (fault: Fault -> 'E) (name: string) (el: JVal) : Result<JVal, 'E> =
        match el with
        | JObj _ ->
            match tryProp name el with
            | Some v -> Ok v
            | None -> Error(fault (MissingProperty name))
        | other -> Error(fault (WrongKind("object", JVal.kindName other)))

    /// `asString` over the caller's error type.
    let stringWith (fault: Fault -> 'E) (el: JVal) : Result<string, 'E> =
        match el with
        | JStr s -> Ok s
        | other -> Error(fault (WrongKind("string", JVal.kindName other)))

    /// The items of a JSON array, over the caller's error type.
    let arrayWith (fault: Fault -> 'E) (el: JVal) : Result<JVal list, 'E> =
        match el with
        | JArr xs -> Ok xs
        | other -> Error(fault (WrongKind("array", JVal.kindName other)))

    let private kindName (v: JVal) = JVal.kindName v

    /// Parse a JSON string to a `JVal` root.
    let parse (json: string) : Result<JVal, string> = Json.parse json

    /// Parse a **foreign** JSON string that spells absent members `null` — object-member `null` is
    /// erased to absence, so every combinator below (`getProp` → `missing property: <name>`) behaves
    /// exactly as it does against the same document written without the token. The one-word swap a
    /// consumer makes to read a spec-conformant foreign document; everything downstream is unchanged.
    let parseTolerantOfNull (json: string) : Result<JVal, string> = Json.parseTolerantOfNull json

    let getProp (name: string) (el: JVal) : Result<JVal, string> = propWith describe name el

    let asString (el: JVal) : Result<string, string> = stringWith describe el

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
        arrayWith describe el
        |> Result.bind (fun xs ->
            let rec go acc =
                function
                | [] -> Ok(List.rev acc)
                | x :: rest ->
                    match d x with
                    | Ok v -> go (v :: acc) rest
                    | Error m -> Error m

            go [] xs)

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
///
/// OBSOLETE since Phase 299, removed at the next breaking draft after `0.33.0` (DECISIONS.md "the
/// parser holds to the JSON grammar, NaN sorts last, and `RowCodec` is obsoleted"). Two hazards are
/// in its bytes and cannot be fixed without changing them: a `DateTime` of `Unspecified` kind goes
/// through `ToUniversalTime()`, which reads the MACHINE's time zone, so one value encodes to
/// different seconds on two servers; and an `int64` is widened to a double, so every value past
/// 2^53 is silently a different number. And it is a boxed `Map<string, obj>` row — a UI-tier
/// representation — in the spine.
[<System.Obsolete("RowCodec is obsolete and is removed at the next breaking draft. Its bytes carry two hazards: a DateTime of Unspecified kind is encoded through ToUniversalTime(), so the Unix seconds depend on the machine's time zone; and an int64 is widened to a double, so any value past 2^53 is silently a different number. Carry rows as a Fuaran.Core.Column DataSource (ColumnCodec), or host a row codec in the UI tier.")>]
module RowCodec =

    /// The residual-opaque sentinel the rows slot carried before the typed encoding.
    [<Literal>]
    let opaqueSentinel = "<opaque>"

    let private kindName (v: JVal) = JVal.kindName v

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

        // THE PROFILE GRAMMAR (Phase 306) — a BIJECTION between the valid profiles and their
        // canonical strings:
        //
        //     profile = name "@" number "." number
        //     name    = letter (letter | digit | "." | "_" | "-")*      ; ASCII only
        //     number  = "0" | nonzero digit*                            ; at most Int32.MaxValue
        //
        // `render` writes exactly this over a valid profile, and `tryParse` accepts exactly this:
        // `tryParse (render p) = Ok p` for every valid `p`, and `render q = s` whenever
        // `tryParse s = Ok q`. Until this phase the reader took strings the writer never wrote —
        // `core@01.0`, `core@+1.0`, a version followed by NUL characters, and any name at all, the
        // empty-looking and the control-character ones included — so two strings named one
        // profile, and a `requiredProfile` could not be compared as text.

        let private isNameStart (c: char) =
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')

        let private isNameChar (c: char) =
            isNameStart c || (c >= '0' && c <= '9') || c = '.' || c = '_' || c = '-'

        /// True where `name` is a profile name of the grammar: an ASCII letter, then ASCII letters,
        /// digits, `.`, `_` and `-`.
        let isValidName (name: string) : bool =
            not (isNull (box name))
            && name.Length > 0
            && isNameStart name.[0]
            && name |> Seq.forall isNameChar

        /// True where `p` is a profile the wire can carry: a name of the grammar and two
        /// non-negative counters. The record is public, so one can be built that is not.
        let isValid (p: Profile) : bool =
            isValidName p.Name && p.Major >= 0 && p.Minor >= 0

        /// The canonical string form: `<name>@<major>.<minor>`. **Assumes a valid profile**
        /// (`isValid`) — over one, the string is the one `tryParse` reads back to it. `tryRender`
        /// is the guarded, total entry point for a profile built by hand.
        let render (p: Profile) : string =
            p.Name + "@" + string p.Major + "." + string p.Minor

        /// `render`, refusing a profile `tryParse` could not read back (Phase 306): a name outside
        /// the grammar or a negative counter. Over a valid profile it is exactly `Ok (render p)`.
        let tryRender (p: Profile) : Result<string, string> =
            if not (isValidName p.Name) then
                Error(
                    "profile name is outside the grammar (an ASCII letter, then letters, digits, '.', '_' or '-'): "
                    + (if isNull (box p.Name) then "<null>" else Json.escape p.Name)
                )
            elif p.Major < 0 || p.Minor < 0 then
                Error("profile version is negative: " + string p.Major + "." + string p.Minor)
            else
                Ok(render p)

        /// The base `core` profile — `core@1.0`.
        let coreV1: Profile = { Name = "core"; Major = 1; Minor = 0 }

        /// Parse `<name>@<major>.<minor>` — the canonical form and nothing else (the grammar
        /// above). Names a typed `Error` on any other shape — the same envelope discipline as the
        /// parser (no exceptions escape).
        let tryParse (s: string) : Result<Profile, string> =
            let at = s.LastIndexOf '@'

            if at <= 0 || at = s.Length - 1 then
                Error("malformed profile (expected '<name>@<major>.<minor>'): " + s)
            else
                let name = s.Substring(0, at)
                let ver = s.Substring(at + 1)
                let parts = ver.Split('.')

                // A canonical number: digits only, no sign, and no leading zero — so `01`, `+1`,
                // `-0` and a digit run followed by anything are refused before the wire's one
                // integer reader sees them; the reader then bounds the value to Int32.
                let parseInt (t: string) =
                    let canonical =
                        t.Length > 0
                        && t |> Seq.forall (fun c -> c >= '0' && c <= '9')
                        && (t.Length = 1 || t.[0] <> '0')

                    if canonical then Json.readInt32 t else None

                if not (isValidName name) then
                    Error(
                        "malformed profile name (expected an ASCII letter, then letters, digits, '.', '_' or '-'): "
                        + Json.escape name
                    )
                else
                    match parts with
                    | [| maj; min |] ->
                        match parseInt maj, parseInt min with
                        | Some major, Some minor ->
                            Ok
                                { Name = name
                                  Major = major
                                  Minor = minor }
                        | _ ->
                            Error(
                                "malformed profile version (expected '<major>.<minor>', each a canonical non-negative integer within Int32): "
                                + Json.escape ver
                            )
                    | _ -> Error("malformed profile version (expected '<major>.<minor>'): " + Json.escape ver)

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
    let internal profileKey = "$profile"

    [<Literal>]
    let payloadKey = "$payload"

    [<Literal>]
    let internal requiredProfileKey = "requiredProfile"

    /// A versioned wire envelope: the producer's authored `Profile` + the artifact `Payload`
    /// (a `Node` / `TreeOp` JVal). `$profile` / `$payload` keys are `$`-prefixed so they sort
    /// before any lower-case data key under `Canon.render`. The envelope is the
    /// capability-negotiation carrier — a consumer reads `$profile`, `negotiate`s, then decodes
    /// `$payload` (tolerantly when `Behind`).
    type Envelope = { Profile: Profile; Payload: JVal }

    /// Build the canonical envelope JVal.
    let encode (env: Envelope) : JVal =
        JObj [ payloadKey, env.Payload; profileKey, JStr(Profile.render env.Profile) ]

    /// Render an envelope to canonical wire bytes. **Assumes a valid profile and a payload
    /// `Canon.tryRender` accepts** — `tryRender` is the guarded entry point.
    let render (env: Envelope) : string = Canon.render (encode env)

    /// `render`, guarded (Phase 306): the profile through `Profile.tryRender`, then the whole
    /// envelope through `Canon.tryRender` — so an envelope this returns `Ok` for is one `parse`
    /// reads back to the same profile. Over a valid profile and an accepted payload it is exactly
    /// `Ok (render env)`.
    let tryRender (env: Envelope) : Result<string, string> =
        Profile.tryRender env.Profile
        |> Result.bind (fun _ -> Canon.tryRender (encode env))

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
    ///
    /// REFUSES AT THE EDGE OF THE RANGE (Phase 306): a counter is an `int`, and one already at
    /// `Int32.MaxValue` has no successor the wire can carry. `baseProfile.Minor + 1` there wrapped
    /// to a NEGATIVE minor — a profile `render` then wrote as `core@1.-2147483648` and `tryParse`
    /// refused, and one `negotiate` read as older than every consumer. The refusal names the
    /// counter; `bump` is the saturating form for a caller with no error channel.
    let tryBump (baseProfile: Profile) (ev: Evolution) : Result<Profile, string> =
        match ev with
        | Additive [] -> Ok baseProfile
        | Additive _ ->
            if baseProfile.Minor = System.Int32.MaxValue then
                Error(
                    "the minor of "
                    + Profile.render baseProfile
                    + " is at Int32.MaxValue and cannot be bumped"
                )
            else
                Ok
                    { baseProfile with
                        Minor = baseProfile.Minor + 1 }
        | Breaking _ ->
            if baseProfile.Major = System.Int32.MaxValue then
                Error(
                    "the major of "
                    + Profile.render baseProfile
                    + " is at Int32.MaxValue and cannot be bumped"
                )
            else
                Ok
                    { baseProfile with
                        Major = baseProfile.Major + 1
                        Minor = 0 }

    /// `tryBump`, SATURATING where it refuses: a counter at `Int32.MaxValue` stays there rather
    /// than wrapping negative, and the profile comes back unchanged. Everywhere below that edge it
    /// is the bump it always was. A caller that must know the bump did not happen uses `tryBump`.
    let bump (baseProfile: Profile) (ev: Evolution) : Profile =
        match tryBump baseProfile ev with
        | Ok p -> p
        | Error _ -> baseProfile

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

    let internal runCase (codec: Codec<'T>) (c: Case) : Outcome =
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

    /// The fuzz alphabet, as ATOMS a generated string is a sequence of (Phase 299): ordinary
    /// characters, the two escaped structural characters, EVERY control character U+0000–U+001F
    /// (built, never written raw — a raw NUL in the source would make git treat this file as
    /// binary), a non-ASCII BMP character, and the surrogate classes — a well-formed pair, a lone
    /// high and a lone low (a low atom drawn before a high atom is the ill-ordered class). A string
    /// holding a lone or ill-ordered surrogate is not well-formed UTF-16, and the parser refuses it.
    let private fuzzAtoms: string[] =
        Array.append
            [| "a"
               "z"
               "0"
               " "
               "\""
               "\\"
               "/"
               "\u007F"
               "é"
               "😀"
               "\uD800"
               "\uDFFF" |]
            [| for k in 0x00..0x1F -> string (char k) |]

    /// Every string and member key of `v` is well-formed UTF-16 — the values the parser can hand
    /// back (`Json.firstIllFormedString`, the scan the guarded renderers refuse on).
    let private allStringsWellFormed (v: JVal) : bool = (Json.firstIllFormedString v).IsNone

    /// Generate one random valid `JVal` from `seed`, nesting no deeper than `maxDepth`.
    let private genJVal (seed: int) (maxDepth: int) : JVal =
        let mutable st = (uint32 seed * 2654435761u) + 1u

        let next () =
            st <- (st * 1664525u) + 1013904223u
            int (st >>> 1)

        let pick (n: int) = next () % n

        let randStr () =
            let len = pick 6

            Array.init len (fun _ -> fuzzAtoms.[pick fuzzAtoms.Length]) |> String.concat ""

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

    /// Generative round-trip law: over `count` seed-replayable random `JVal`s, BOTH renderers —
    /// `Json.render` and, since Phase 299, `Canon.render` — must be idempotent under a `parse`
    /// round-trip: `parse (render v) |> Result.map render = Ok (render v)`. The string form is robust
    /// to the documented canonical normalisations (an integer-valued `JFloat` renders without a
    /// point and re-parses as `JInt`; `Canon.render` sorts keys); the rendered text still
    /// round-trips. A value holding a string that is NOT well-formed UTF-16 (a lone or ill-ordered
    /// surrogate — the alphabet draws them) has no string to round-trip to, and there the law is
    /// the refusal: `parse` must reject the rendered text as `BadEscape`, under both renderers.
    /// Returns the first counterexample's seed, renderer and offending output as an `Error`.
    let fuzzRoundTrip (seed: int) (count: int) (maxDepth: int) : Result<unit, string> =
        let check (name: string) (render: JVal -> string) (at: int) (v: JVal) : Result<unit, string> =
            let s = render v

            match Json.parseDetailed s, allStringsWellFormed v with
            | Ok v2, true when render v2 = s -> Ok()
            | Ok v2, true -> Error(sprintf "fuzz seed=%d: %s not idempotent (%s vs %s)" at name s (render v2))
            | Error e, true -> Error(sprintf "fuzz seed=%d: parse rejected %s output %s — %s" at name s e.Message)
            | Error e, false when e.Kind = BadEscape -> Ok()
            | Error e, false ->
                Error(
                    sprintf
                        "fuzz seed=%d: %s output %s carries an ill-formed string and was refused as %A, not BadEscape"
                        at
                        name
                        s
                        e.Kind
                )
            | Ok _, false ->
                Error(sprintf "fuzz seed=%d: %s output %s carries an ill-formed string and was accepted" at name s)

        let rec go i =
            if i >= count then
                Ok()
            else
                let v = genJVal (seed + i) maxDepth

                match check "Json.render" Json.render (seed + i) v with
                | Error m -> Error m
                | Ok() ->
                    match check "Canon.render" Canon.render (seed + i) v with
                    | Error m -> Error m
                    | Ok() -> go (i + 1)

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
