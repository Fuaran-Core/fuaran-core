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
    /// A string. Any value renders, but only a well-formed UTF-16 one survives `parse`; the guarded
    /// renderers refuse a lone or ill-ordered surrogate.
    | JStr of string
    /// A number `parse` read from an integer token within Int32. A larger integer token arrives as
    /// `JFloat`, not here (exact up to 2^53; past it, only a double's canonical layout is admitted).
    | JInt of int
    /// The `true` / `false` literal; no reader coerces a number or a string to it.
    | JBool of bool
    /// A number with a point or an exponent, or an integer past Int32. Must be finite to render as
    /// valid JSON (`tryRender` refuses NaN / ±Infinity). A whole value within Int32 renders without
    /// a point, so it reads back from `parse` as `JInt`.
    | JFloat of float
    /// Items in order. `parse` caps nesting at `Json.defaultMaxDepth`; the renderers cap nothing.
    | JArr of JVal list
    /// Members in AUTHORED order, a repeated key kept as written: `Json.render` keeps the order,
    /// `Canon.render` sorts keys Ordinal, and every reader takes the FIRST member of a repeated key.
    | JObj of (string * JVal) list

/// The classified failure modes of the portable JSON parser (Phase 22) — so an orchestrator
/// can branch on *what* went wrong structurally instead of string-scraping `parse`'s message.
type JsonErrorKind =
    /// A value position held a character no JSON value starts with.
    | UnexpectedChar
    /// The input ended where a value was due — empty or all-whitespace input included.
    | UnexpectedEndOfInput
    /// A specific token was missing: `:`, `,` or a closing bracket, the quote that opens a member
    /// key, or the rest of a `true` / `false` literal.
    | ExpectedToken
    /// The input ended inside a string, before its closing quote.
    | UnterminatedString
    /// The input ended straight after a backslash inside a string.
    | UnterminatedEscape
    /// A `\u` escape had fewer than four characters left before the input ended.
    | TruncatedUnicodeEscape
    /// A backslash followed by a character outside the JSON escape set, OR a string that is not
    /// well-formed UTF-16 (a lone or ill-ordered surrogate, raw or escaped).
    | BadEscape
    /// A `\u` escape held a character that is not a hex digit (either case is accepted).
    | BadHexDigit
    /// A number token outside the JSON grammar (`01`, `1.`, `-.5`, `1e`), outside the finite double
    /// range, or an integer past 2^53 that is not the canonical layout of a double.
    | MalformedNumber
    /// A `null` token: anywhere under `RejectNull`; only at the root or as an array item under
    /// `EraseMemberNull`.
    | NullNotRepresentable
    /// An object or array opened past the nesting cap. The one kind `DecodeError.ofJsonError` maps
    /// to `LimitExceeded` rather than `InvalidJson`.
    | MaxDepthExceeded
    /// Something other than whitespace follows one complete value.
    | TrailingCharacters

/// A structured parse failure (Phase 22): the classified `Kind`, the human `Message` (no
/// position suffix), and the 0-based `Position` in the input. `Json.parse`'s string error is
/// `"not valid JSON: " + Message + " at position " + Position` — byte-identical to before.
type JsonError =
    {
        /// The parser's cursor at the refusal, as a 0-based UTF-16 index: AT the offending character
        /// for a missing token, just PAST it where the parser had already consumed it (a bad escape).
        Position: int
        /// The refusal sentence without the position suffix. Its text is part of `parse`'s
        /// string error, so it is stable.
        Message: string
        /// The classified fault — branch on this, not on `Message`.
        Kind: JsonErrorKind
    }

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


/// WHICH canonical rendering a content-addressed store's ids were computed under (Phase 360). A
/// store that hashes `Json.render` output — an op encoder, a node pre-image — keys its ids on the
/// rendered BYTES, so a change to those bytes is a change to every id it holds. A store names its
/// profile and renders through `Json.renderWith`, and its ids keep recomputing while the spine's own
/// rendering moves on. Closed and versioned: a later byte change to `Json.render` adds a case and
/// moves `EncodingProfile.current`; it never changes what an existing case renders.
///
/// The profiles differ in exactly one respect today, measured against the published binaries
/// (DECISIONS.md D120): how a string spells line feed, carriage return and tab. Number layout, member
/// order and whitespace are the same under both. `Canon.render` is not profiled: its bytes have not
/// moved since `0.30.0`.
///
/// `Fuaran.Core.OpStream` carries the same two cases as `OpStream.EncodingProfile` for the pre-images
/// it builds itself (DECISIONS.md D2 keeps that package free of a reference here); both spell a
/// profile by the same `name`.
[<RequireQualifiedAccess>]
type EncodingProfile =
    /// The rendering of `0.30.0` through `0.32.0`: `\n`, `\r` and `\t` as the short escapes, every
    /// other control character as lower-case `\u00xx`, `"` and `\` escaped, nothing else.
    | V1
    /// The rendering since `0.33.0` (Phase 287): every control character `U+0000`–`U+001F` as
    /// lower-case `\u00xx`, with no short form; `"` and `\` escaped, nothing else.
    | V2

/// The `EncodingProfile` companions (Phase 360): the current default, the closed set, and the
/// canonical name a store declares its profile by.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module EncodingProfile =

    /// The profile `Json.render` renders under — `V2` since `0.33.0`. It moves only when the
    /// spine's rendering does, and a store pinned to a named profile does not move with it.
    let current: EncodingProfile = EncodingProfile.V2

    /// Every profile, oldest first.
    let all: EncodingProfile list = [ EncodingProfile.V1; EncodingProfile.V2 ]

    /// The canonical name a store declares its profile by: `v1` or `v2`. The same strings
    /// `OpStream.profileName` spells, so one declaration names both halves of a store's encoding.
    let name (profile: EncodingProfile) : string =
        match profile with
        | EncodingProfile.V1 -> "v1"
        | EncodingProfile.V2 -> "v2"

    /// The profile a canonical name declares, or `None` for any other string (case-sensitive:
    /// `name >> tryParse` is `Some`, and nothing else is).
    let tryParse (name: string) : EncodingProfile option =
        match name with
        | "v1" -> Some EncodingProfile.V1
        | "v2" -> Some EncodingProfile.V2
        | _ -> None


/// Fable-clean encode helpers + the wire-envelope discipline + a portable
/// (FSharp.Core-only) parser. Per-kind cases stay domain-side; the core owns the
/// envelope shape, the combinators, and the parser. `render` and `parse` are inverses
/// over canonical wire JSON.
module Json =

    // ---- THE ESCAPE'S FAST PATH (Phase 365) ----
    // Most strings a document carries have nothing to escape, so the escape scans before it copies:
    // `firstEscapable` finds the first character the rule touches, and a string with none is used
    // whole. When there is one, `appendEscapedFrom` appends each clean run with ONE ranged append
    // and spells only the escaped characters, from a table rather than a format call. The bytes
    // are the rule's, unchanged; `StringEscapeVectors` and `StringEscapeTests` hold them.

    /// The `\u00xx` spelling of each control character `U+0000`–`U+001F`, lower-case hex, built
    /// once so that no escape formats a number.
    let private controlEscapes: string[] =
        let hex = "0123456789abcdef"
        Array.init 0x20 (fun c -> "\\u00" + string hex.[c >>> 4] + string hex.[c &&& 0xF])

    /// The index of the first character of `s` the rule escapes — `"`, `\` or a control character
    /// below `U+0020` — or `-1` when there is none. Under Fable it is one native regex search,
    /// which the Phase 365 harness measured faster under node than the loop on long clean strings
    /// (the `escape-free` escape case, 0.21 ms to 0.12 ms) and no slower elsewhere; the class is
    /// the same three, code unit by code unit, so the index is the loop's.
#if FABLE_COMPILER
    [<Fable.Core.Emit("$0.search(/[\"\\\\\\u0000-\\u001f]/)")>]
    let private firstEscapable (s: string) : int = Fable.Core.Util.jsNative
#else
    let private firstEscapable (s: string) : int =
        let mutable i = 0
        let mutable found = -1

        while found < 0 && i < s.Length do
            let code = int s.[i]

            if code < 0x20 || code = 0x22 || code = 0x5C then
                found <- i
            else
                i <- i + 1

        found
#endif

    /// Append the escaped body of `s` to `sb`, given that `first` is the index of its first
    /// escapable character: the clean prefix and every later clean run as one ranged append each.
    let private appendEscapedFrom (sb: System.Text.StringBuilder) (s: string) (first: int) : unit =
        if first > 0 then
            sb.Append(s, 0, first) |> ignore

        // `start` is where the clean run not yet appended begins.
        let mutable start = first

        for i in first .. s.Length - 1 do
            let code = int s.[i]

            if code < 0x20 || code = 0x22 || code = 0x5C then
                if i > start then
                    sb.Append(s, start, i - start) |> ignore

                (if code = 0x22 then sb.Append("\\\"")
                 elif code = 0x5C then sb.Append("\\\\")
                 else sb.Append(controlEscapes.[code]))
                |> ignore

                start <- i + 1

        if s.Length > start then
            sb.Append(s, start, s.Length - start) |> ignore

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
    ///
    /// A string with nothing to escape is returned AS IT IS (Phase 365): the scan finds the first
    /// escapable character, and only a string that has one is copied — its clean runs appended
    /// whole, one ranged append each, and only the escaped characters spelled one at a time.
    let escape (s: string) : string =
        let first = firstEscapable s

        if first < 0 then
            s
        else
            let sb = System.Text.StringBuilder(s.Length + 16)
            appendEscapedFrom sb s first
            sb.ToString()

    /// `escape s` appended to `sb` (Phase 365) — the same bytes, written into the caller's own
    /// builder, so a writer that is already building a document makes no intermediate string per
    /// value. A string with nothing to escape is appended whole.
    let escapeInto (sb: System.Text.StringBuilder) (s: string) : unit =
        let first = firstEscapable s

        if first < 0 then
            sb.Append(s) |> ignore
        else
            appendEscapedFrom sb s first

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

    /// The writer, generic over how a string's body is appended (Phase 360): `writeWith` appends
    /// through the live `escape`, and `EncodingProfile.V1` through its own frozen copy, so a later
    /// rewrite of the live escape cannot move a `V1` byte.
    let private writeCore
        (appendEscaped: System.Text.StringBuilder -> string -> unit)
        (sortKeys: bool)
        (floatText: float -> string)
        (v: JVal)
        : string =
        let sb = System.Text.StringBuilder()
        let mutable stack = [ Value v ]

        let quoted (s: string) =
            sb.Append('"') |> ignore
            appendEscaped sb s
            sb.Append('"') |> ignore

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

    /// Write `v` with `floatText` as the float layout and object members in Ordinal key order
    /// (`sortKeys`) or as authored. The bytes are the ones the recursive renderers wrote. Each string
    /// is escaped straight into the writer's builder (`escapeInto`, Phase 365), never through an
    /// intermediate string.
    let internal writeWith (sortKeys: bool) (floatText: float -> string) (v: JVal) : string =
        writeCore escapeInto sortKeys floatText v

    /// `EncodingProfile.V1`'s string escape, FROZEN (Phase 360): the body `Json.escape` had in
    /// `0.30.0`, kept as its own copy rather than written in terms of the live `escape`, so no later
    /// change to the live path — a rewrite for speed, a further byte change — can move a `V1` byte.
    /// `EncodingProfileVectors` pins its output for every character it escapes against bytes the
    /// published `0.30.0` binary produced.
    let private appendEscapedV1 (sb: System.Text.StringBuilder) (s: string) : unit =
        for ch in s do
            match ch with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when int c < 0x20 -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append(c) |> ignore

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

    /// `escape` under a named profile (Phase 360): the body of the JSON string literal `s` renders
    /// as, quotes excluded, under `profile`. `escapeWith EncodingProfile.V2` is `escape`;
    /// `escapeWith EncodingProfile.V1` is the frozen `0.30.0` spelling. For a consumer that builds a
    /// hash pre-image by hand and must spell its strings as its store declares.
    let escapeWith (profile: EncodingProfile) (s: string) : string =
        match profile with
        | EncodingProfile.V1 ->
            let sb = System.Text.StringBuilder()
            appendEscapedV1 sb s
            sb.ToString()
        | EncodingProfile.V2 -> escape s

    /// `render` under a named profile (Phase 360) — the renderer a content-addressed store hashes
    /// through, so its stored ids recompute on every later release. Author member order and the
    /// round-trip float layout under every profile, exactly as `render`; the profiles differ only in
    /// the string escape (`escapeWith`).
    ///
    /// - `V1` reproduces `0.30.0`'s `Json.render` byte for byte, through a FROZEN copy of that
    ///   release's escape; `EncodingProfileVectors` pins it against bytes the published `0.30.0`
    ///   binary produced, for every escaping case and for numbers, order and nesting.
    /// - `V2` IS `render` — the live path. Its committed vector column is what holds a later rewrite
    ///   of that path to these bytes; a deliberate byte change to `render` is a new profile, never a
    ///   change to `V2`.
    let renderWith (profile: EncodingProfile) (v: JVal) : string =
        match profile with
        | EncodingProfile.V1 -> writeCore appendEscapedV1 false FloatLayout.roundTrip v
        | EncodingProfile.V2 -> render v

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

    /// `render` under the `encode` name: author key order, round-trip float layout, and UNGUARDED —
    /// a non-finite float or an ill-formed string yields text `parse` refuses. `tryEncode` refuses
    /// both instead.
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
        //
        // The string is read by RUNS (Phase 366): `scanRun` walks an escape-free stretch up to the
        // next `"` or `\`, checking the pairing of each unit as it passes and advancing `i` before
        // the check exactly as the one-unit-at-a-time reader did, so every refusal keeps its kind,
        // message and position; the stretch is then taken whole — by one `Substring` when the
        // closing quote ends the first run (the common string, with no builder at all), or by one
        // ranged `Append` into the builder an escape made necessary. The pairing state is carried
        // across runs and escapes alike, so a pair may be split between the two spellings.
        let parseString () : string =
            expect '"'
            // The last unit taken was a high surrogate still waiting for its low half.
            let mutable pendingHigh = false

            let check (u: int) =
                if pendingHigh && not (isLowSurrogate u) then
                    fail BadEscape "ill-formed string: a high surrogate not followed by a low surrogate"
                elif not pendingHigh && isLowSurrogate u then
                    fail BadEscape "ill-formed string: a low surrogate with no high surrogate before it"

                pendingHigh <- isHighSurrogate u

            // Advance `i` over an escape-free run; it stops AT the `"` or `\` that ends it, or at `n`.
            let scanRun () =
                let mutable go = true

                while go && i < n do
                    let c = input.[i]

                    if c = '"' || c = '\\' then
                        go <- false
                    else
                        i <- i + 1
                        check (int c)

            let start = i
            scanRun ()

            if i >= n then
                fail UnterminatedString "unterminated string"

            if input.[i] = '"' then
                i <- i + 1

                if pendingHigh then
                    fail BadEscape "ill-formed string: a high surrogate not followed by a low surrogate"

                input.Substring(start, i - 1 - start)
            else
                let sb = System.Text.StringBuilder()
                sb.Append(input, start, i - start) |> ignore

                let append (u: int) =
                    check u
                    sb.Append(char u) |> ignore

                let mutable fin = false

                while not fin do
                    // `i` is at the `"` or `\` that ended the last run, or at `n`.
                    if i >= n then
                        fail UnterminatedString "unterminated string"

                    let c = input.[i]
                    i <- i + 1

                    if c = '"' then
                        if pendingHigh then
                            fail BadEscape "ill-formed string: a high surrogate not followed by a low surrogate"

                        fin <- true
                    else
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

                        let s = i
                        scanRun ()

                        if i > s then
                            sb.Append(input, s, i - s) |> ignore

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
                // corrupt it — unless it is the canonical layout of a double (Phase 253,
                // below), which no reading can corrupt. (Fable-clean: Int32.TryParse +
                // Double.TryParse + the shared `FloatLayout` only.)
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
                        // Phase 253 — past 2^53 the token is admitted EXACTLY when it is the
                        // canonical float layout (`FloatLayout.finite`) of the double it reads as.
                        // The float layout writes every finite double whose base-10 exponent is 15
                        // or 16 in fixed point (WIRE_FORMAT §2 rule 5), so 1e16 is the integer-shaped
                        // `10000000000000000`; refusing it left a document Core wrote unreadable by
                        // Core. Such a token survives parse-then-render byte for byte, which is the
                        // property the refusal below is named for; every other token past 2^53 —
                        // 2^53 + 1, a 19-digit id — still fails it, and is refused as before.
                        // (DECISIONS: "an integer token past 2^53 is read when it is a canonical float".)
                        match
                            System.Double.TryParse(
                                tok,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                        with
                        | true, v when
                            not (System.Double.IsNaN v || System.Double.IsInfinity v)
                            && FloatLayout.finite v = tok
                            ->
                            JFloat v
                        | _ ->
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
    /// position <pos>"`) that `parse` / `parseWith` have always returned. Internal since Phase 310:
    /// the typed decode layer's parse refusal carries the same sentence.
    let internal formatJsonError (e: JsonError) : string =
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

// ============================================================================
//  Phase 310 — the typed decode layer: a refusal is a CODE and a PATH, not a sentence.
//
//  Until this phase `Decode` was six strict combinators returning `Result<_, string>`: no optional
//  or defaulted member, no reader for half the `JVal` kinds, no path, no tag dispatch, no
//  accumulation. Every codec on the spine and every consumer around it wrote the missing half for
//  itself, and none of them could tell a caller WHERE in a document a refusal was or WHAT KIND of
//  refusal it was without scraping the sentence. The layer below is that half, once: a closed code
//  set, a path of keys and indices, combinators that grow the path as a refusal leaves them, and a
//  sentence beside both — the SAME sentence the string forms returned, so a codec moved onto the
//  layer reads identically to every existing caller (DECISIONS.md D99).
// ============================================================================

/// One step of a decode path (Phase 310): a member of an object by its key, or an item of an array
/// by its zero-based index. A path is the list of steps from the document's root, root first.
[<RequireQualifiedAccess>]
type PathSegment =
    /// A member of an object by its exact key; where a document repeats the key, the first.
    | Key of string
    /// An item of an array, zero-based.
    | Index of int

/// The closed code set a decode refusal carries (Phase 310) — the wire-level decode contract every
/// host mirrors (DECISIONS.md D99). The code says what KIND of fault a refusal is, so a caller, or a
/// model repairing its own emission, branches on it rather than on the sentence beside it. Closed:
/// a host adds no code of its own, and a refinement a host draws finer (the UI host tells an
/// unknown node kind from an unknown case) is an instance of one of these.
[<RequireQualifiedAccess>]
type DecodeCode =
    /// The input is not JSON text the reader can parse (any parser refusal but its nesting cap). Its
    /// path is the root.
    | InvalidJson
    /// A required member is absent. The path NAMES the absent member: every step but the last
    /// resolves in the document, to an object that does not carry the last.
    | MissingField
    /// A value is of the wrong JSON kind — a string where a number belongs, an array where an
    /// object does. `Expected` names the kind the position takes.
    | WrongKind
    /// A discriminator or an enumerated value names no case the decoder knows. `Expected` lists the
    /// cases it does.
    | UnknownTag
    /// A value of the right kind outside the set its position admits: a number past its range, text
    /// not in its declared form, an index past an array's end, a value that disagrees with another
    /// the document carries.
    | OutOfRange
    /// A member the decoder does not read, refused under a strict policy. The path names the member.
    | UndeclaredMember
    /// A resource bound was passed — the parser's nesting cap, an item count.
    | LimitExceeded
    /// A value the vocabulary KNOWS that the reader's declared policy does not admit. A different
    /// fact from `UnknownTag`, with a different remedy: the spelling is right, and this reader
    /// does not take it.
    | NotAdmitted
    /// The DECODER's own declaration cannot interpret the position — a type it does not declare, an
    /// unsubstituted type variable. A defect of the vocabulary, not of the document.
    | SchemaFault

/// A decode refusal (Phase 310): its code, the path to the value at fault (see `DecodeCode` for
/// what a `MissingField` path names), what the position expected, and a sentence. `Message` is the
/// sentence the string-error forms return, so the typed and the legacy reading of one refusal are
/// the same refusal.
type DecodeError =
    {
        /// What kind of fault it is — the field to branch on, closed across hosts.
        Code: DecodeCode
        /// Root-first steps to the value at fault, relative to the value the outermost decoder was
        /// handed; empty for that value itself.
        Path: PathSegment list
        /// What the position admits, as a phrase (`object`, `one of 'a', 'b'`, `an int in [0, 9]`) —
        /// for a reader or a repairing model, not for matching on.
        Expected: string
        /// The sentence the string-error forms return for this refusal, byte for byte. It carries no
        /// path; `DecodeError.render` adds one.
        Message: string
    }

/// A decoder over the typed refusal (Phase 310). `Decode.Decoder` is its string-error twin, kept
/// for one draft.
type Decoder<'T> = JVal -> Result<'T, DecodeError>

/// Paths into a document (Phase 310): rendered, resolved, and carried on the wire.
[<RequireQualifiedAccess>]
module DecodePath =

    /// `$` for the root, `[i]` for an item, `["key"]` for a member (the key under `Json.escape`) —
    /// the spelling `Json.firstNonFinite` and `Json.firstIllFormedString` already report in.
    let render (path: PathSegment list) : string =
        let sb = System.Text.StringBuilder("$")

        for step in path do
            match step with
            | PathSegment.Index i -> sb.Append('[').Append(string i).Append(']') |> ignore
            | PathSegment.Key k -> sb.Append("[\"").Append(Json.escape k).Append("\"]") |> ignore

        sb.ToString()

    /// One step into a value — the FIRST member of the key where a foreign document repeats one, as
    /// every combinator reads it.
    let private step (s: PathSegment) (v: JVal) : JVal option =
        match s, v with
        | PathSegment.Key k, JObj fields -> fields |> List.tryFind (fun (n, _) -> n = k) |> Option.map snd
        | PathSegment.Index i, JArr items when i >= 0 -> List.tryItem i items
        | _ -> None

    /// The value at `path` in `doc`, or `None` where a step names nothing there.
    let resolve (path: PathSegment list) (doc: JVal) : JVal option =
        let rec go (p: PathSegment list) (v: JVal) =
            match p with
            | [] -> Some v
            | s :: rest -> step s v |> Option.bind (go rest)

        go path doc

    /// A path as a JSON array — a key as a string, an index as an integer. The form a conformance
    /// vector carries, so no host parses a rendered path back.
    let toJson (path: PathSegment list) : JVal =
        path
        |> List.map (function
            | PathSegment.Key k -> JStr k
            | PathSegment.Index i -> JInt i)
        |> JArr

    /// The inverse of `toJson`; `None` for anything but an array of strings and integers.
    let ofJson (v: JVal) : PathSegment list option =
        match v with
        | JArr items ->
            let steps =
                items
                |> List.map (function
                    | JStr k -> Some(PathSegment.Key k)
                    | JInt i -> Some(PathSegment.Index i)
                    | _ -> None)

            if List.forall Option.isSome steps then
                Some(List.choose id steps)
            else
                None
        | _ -> None

/// Building and reading decode refusals (Phase 310).
[<RequireQualifiedAccess>]
module DecodeError =

    /// Every code, in declaration order.
    let codes: DecodeCode list =
        [ DecodeCode.InvalidJson
          DecodeCode.MissingField
          DecodeCode.WrongKind
          DecodeCode.UnknownTag
          DecodeCode.OutOfRange
          DecodeCode.UndeclaredMember
          DecodeCode.LimitExceeded
          DecodeCode.NotAdmitted
          DecodeCode.SchemaFault ]

    /// A code's wire name — its case name, as every host spells it.
    let codeName (code: DecodeCode) : string =
        match code with
        | DecodeCode.InvalidJson -> "InvalidJson"
        | DecodeCode.MissingField -> "MissingField"
        | DecodeCode.WrongKind -> "WrongKind"
        | DecodeCode.UnknownTag -> "UnknownTag"
        | DecodeCode.OutOfRange -> "OutOfRange"
        | DecodeCode.UndeclaredMember -> "UndeclaredMember"
        | DecodeCode.LimitExceeded -> "LimitExceeded"
        | DecodeCode.NotAdmitted -> "NotAdmitted"
        | DecodeCode.SchemaFault -> "SchemaFault"

    /// The code a wire name spells, or `None`.
    let tryCodeOfName (name: string) : DecodeCode option =
        codes |> List.tryFind (fun c -> codeName c = name)

    /// A refusal at the value being decoded (an empty path — a combinator that hands the refusal
    /// out prefixes its own step).
    let make (code: DecodeCode) (expected: string) (message: string) : DecodeError =
        { Code = code
          Path = []
          Expected = expected
          Message = message }

    /// The same refusal one step further from the root — what a combinator does to a refusal
    /// leaving a member or an item.
    let under (step: PathSegment) (e: DecodeError) : DecodeError = { e with Path = step :: e.Path }

    /// The same refusal under a whole path prefix.
    let within (prefix: PathSegment list) (e: DecodeError) : DecodeError = { e with Path = prefix @ e.Path }

    /// The same refusal with its sentence rewritten — a codec prefixing its own context.
    let reword (f: string -> string) (e: DecodeError) : DecodeError = { e with Message = f e.Message }

    /// The sentence alone — what the string-error forms return.
    let describe (e: DecodeError) : string = e.Message

    /// Code, path and sentence on one line: `MissingField at $["a"]: missing property: a`.
    let render (e: DecodeError) : string =
        codeName e.Code + " at " + DecodePath.render e.Path + ": " + e.Message

    /// The refusal as a canonical wire value: `code`, `path` (`DecodePath.toJson`), `expected`,
    /// `message`.
    let toJson (e: DecodeError) : JVal =
        JObj
            [ "code", JStr(codeName e.Code)
              "path", DecodePath.toJson e.Path
              "expected", JStr e.Expected
              "message", JStr e.Message ]

    /// Whether the refusal's path RESOLVES in the document it was raised over — the law every
    /// decoder refusal answers (`Corpus.refusalLaws`): an `InvalidJson` names the root; a
    /// `MissingField` names a member of a resolving object that does not carry it; every other code
    /// names a value the document holds.
    let resolvesIn (doc: JVal) (e: DecodeError) : bool =
        match e.Code with
        | DecodeCode.InvalidJson -> List.isEmpty e.Path
        | DecodeCode.MissingField ->
            match List.rev e.Path with
            | PathSegment.Key k :: parentRev ->
                match DecodePath.resolve (List.rev parentRev) doc with
                | Some(JObj fields) -> not (fields |> List.exists (fun (n, _) -> n = k))
                | _ -> false
            | _ -> false
        | _ -> (DecodePath.resolve e.Path doc).IsSome

    /// A parser refusal as a decode refusal at the root: the nesting cap is `LimitExceeded`, every
    /// other class `InvalidJson`; the sentence is the one `Json.parse` returns.
    let ofJsonError (e: JsonError) : DecodeError =
        match e.Kind with
        | MaxDepthExceeded -> make DecodeCode.LimitExceeded "nesting within the parser's cap" (Json.formatJsonError e)
        | _ -> make DecodeCode.InvalidJson "JSON text" (Json.formatJsonError e)

/// The typed decode combinators (Phase 310). Each returns `Result<_, DecodeError>`; a refusal
/// leaving a member or an item gains that step, so the path a caller reads is the path from the
/// value the outermost decoder was handed. Fable-clean, like the rest of the package.
///
/// Optional members are three-valued and the third value is a refusal: `optField` reads an ABSENT
/// member as `Ok None` and a PRESENT member its decoder refuses as that refusal — never as absence,
/// which is how an ill-typed member went silently unread in every hand-rolled optional reader this
/// layer replaced.
[<RequireQualifiedAccess>]
module Decoder =

    /// A decoder that answers `v` whatever it is handed.
    let succeed (v: 'T) : Decoder<'T> = fun _ -> Ok v

    /// A decoder that refuses whatever it is handed, at the value itself.
    let fail (code: DecodeCode) (expected: string) (message: string) : Decoder<'T> =
        fun _ -> Error(DecodeError.make code expected message)

    /// Convert the answer; a refusal passes through untouched, its path included.
    let map (f: 'T -> 'U) (d: Decoder<'T>) : Decoder<'U> = fun el -> d el |> Result.map f

    /// Decode, then decode the SAME value with a decoder chosen by the first answer.
    let bind (f: 'T -> Decoder<'U>) (d: Decoder<'T>) : Decoder<'U> =
        fun el ->
            match d el with
            | Ok v -> f v el
            | Error e -> Error e

    /// Decode, then check or convert the answer; a refusal from `f` is raised at the value.
    let andThen (f: 'T -> Result<'U, DecodeError>) (d: Decoder<'T>) : Decoder<'U> = fun el -> d el |> Result.bind f

    /// The string-error reading of a decoder — its refusal's sentence. The bridge a codec whose
    /// published error is a `string` returns through.
    let describing (d: Decoder<'T>) : JVal -> Result<'T, string> =
        fun el -> d el |> Result.mapError DecodeError.describe

    /// The refusal of a value of the wrong kind: `expected <kind>, got <kind>`.
    let wrongKind (expected: string) (found: JVal) : DecodeError =
        DecodeError.make DecodeCode.WrongKind expected ("expected " + expected + ", got " + JVal.kindName found)

    /// The refusal of an absent required member, its path naming the member: `missing property: <name>`.
    let missing (name: string) : DecodeError =
        { Code = DecodeCode.MissingField
          Path = [ PathSegment.Key name ]
          Expected = "a member '" + name + "'"
          Message = "missing property: " + name }

    /// The member `name` of an object — the FIRST, where a foreign document repeats a key — or
    /// `None` where it has none or `el` is not an object.
    let tryMember (name: string) (el: JVal) : JVal option =
        match el with
        | JObj fields -> fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd
        | _ -> None

    let private quoteAll (xs: string list) : string =
        xs |> List.map (fun x -> "'" + x + "'") |> String.concat ", "

    // ---- the six kinds ----

    /// Any value, verbatim.
    let json: Decoder<JVal> = Ok

    /// A `JStr`; any other kind is `WrongKind` — no number or bool is turned into text.
    let str: Decoder<string> =
        function
        | JStr s -> Ok s
        | other -> Error(wrongKind "string" other)

    /// A `JInt` only — the strict integer read. A float token (`2.0`, `1e3`) and an integer past
    /// Int32 both parse as `JFloat` and are refused as `WrongKind`; `float` is the lenient read.
    let int: Decoder<int> =
        function
        | JInt i -> Ok i
        | other -> Error(wrongKind "int" other)

    /// A number, whichever constructor the parser chose (`JVal.asFloat`'s rule).
    let float: Decoder<float> =
        function
        | JFloat f -> Ok f
        | JInt i -> Ok(float i)
        | other -> Error(wrongKind "number" other)

    /// A `JBool`; `0`, `1` and `"true"` are `WrongKind`, never coerced.
    let bool: Decoder<bool> =
        function
        | JBool b -> Ok b
        | other -> Error(wrongKind "bool" other)

    /// An array's items, undecoded.
    let items: Decoder<JVal list> =
        function
        | JArr xs -> Ok xs
        | other -> Error(wrongKind "array" other)

    /// An object's members, in authored order, undecoded.
    let obj: Decoder<(string * JVal) list> =
        function
        | JObj fields -> Ok fields
        | other -> Error(wrongKind "object" other)

    // ---- members ----

    /// The member `name`, decoded with `d`; absent is `MissingField`, a non-object `WrongKind`.
    let field (name: string) (d: Decoder<'T>) : Decoder<'T> =
        fun el ->
            match el with
            | JObj _ ->
                match tryMember name el with
                | Some v -> d v |> Result.mapError (DecodeError.under (PathSegment.Key name))
                | None -> Error(missing name)
            | other -> Error(wrongKind "object" other)

    /// The member `name` if present: absent is `Ok None`, present-and-refused is the refusal.
    let optField (name: string) (d: Decoder<'T>) : Decoder<'T option> =
        fun el ->
            match el with
            | JObj _ ->
                match tryMember name el with
                | Some v ->
                    d v
                    |> Result.map Some
                    |> Result.mapError (DecodeError.under (PathSegment.Key name))
                | None -> Ok None
            | other -> Error(wrongKind "object" other)

    /// The member `name`, or `fallback` where it is ABSENT; present-and-refused is the refusal.
    let fieldOr (name: string) (fallback: 'T) (d: Decoder<'T>) : Decoder<'T> =
        optField name d |> map (Option.defaultValue fallback)

    /// Decode the value at `path` below the one handed in. A step that names nothing is refused
    /// where it fails: an absent member as `MissingField`, an item past an array's end as
    /// `OutOfRange` at the array, a step into the wrong kind as `WrongKind` at that value.
    let at (path: PathSegment list) (d: Decoder<'T>) : Decoder<'T> =
        fun el ->
            let rec go (doneRev: PathSegment list) (rest: PathSegment list) (v: JVal) =
                let here () = List.rev doneRev

                match rest with
                | [] -> d v |> Result.mapError (DecodeError.within (here ()))
                | (PathSegment.Key k as s) :: tail ->
                    match v with
                    | JObj _ ->
                        match tryMember k v with
                        | Some x -> go (s :: doneRev) tail x
                        | None -> Error(DecodeError.within (here ()) (missing k))
                    | other -> Error(DecodeError.within (here ()) (wrongKind "object" other))
                | (PathSegment.Index i as s) :: tail ->
                    match v with
                    | JArr xs ->
                        match (if i >= 0 then List.tryItem i xs else None) with
                        | Some x -> go (s :: doneRev) tail x
                        | None ->
                            let n = List.length xs

                            Error(
                                DecodeError.within
                                    (here ())
                                    (DecodeError.make
                                        DecodeCode.OutOfRange
                                        ("an index below " + string n)
                                        ("no item " + string i + " in an array of " + string n))
                            )
                    | other -> Error(DecodeError.within (here ()) (wrongKind "array" other))

            go [] path el

    // ---- arrays ----

    /// Every item of an array, each with the decoder `f` builds from its index; the first refusal,
    /// under its item's index.
    let mapListIndexed (f: int -> Decoder<'T>) : Decoder<'T list> =
        fun el ->
            match el with
            | JArr xs ->
                let rec go i acc =
                    function
                    | [] -> Ok(List.rev acc)
                    | x :: rest ->
                        match f i x with
                        | Ok v -> go (i + 1) (v :: acc) rest
                        | Error e -> Error(DecodeError.under (PathSegment.Index i) e)

                go 0 [] xs
            | other -> Error(wrongKind "array" other)

    /// Every item of an array decoded with `d`; the first refusal, under its item's index.
    let list (d: Decoder<'T>) : Decoder<'T list> = mapListIndexed (fun _ -> d)

    /// `list` with an item bound: more than `maxItems` items is `LimitExceeded` at the array,
    /// before any item is read.
    let boundedList (maxItems: int) (d: Decoder<'T>) : Decoder<'T list> =
        fun el ->
            match el with
            | JArr xs when List.length xs > maxItems ->
                Error(
                    DecodeError.make
                        DecodeCode.LimitExceeded
                        ("at most " + string maxItems + " items")
                        ("an array of "
                         + string (List.length xs)
                         + " items passes the bound of "
                         + string maxItems)
                )
            | _ -> list d el

    /// Every item of an array decoded with `d`, answering EVERY refusal rather than the first.
    let listAll (d: Decoder<'T>) (el: JVal) : Result<'T list, DecodeError list> =
        match el with
        | JArr xs ->
            let results =
                xs
                |> List.mapi (fun i x -> d x |> Result.mapError (DecodeError.under (PathSegment.Index i)))

            match
                results
                |> List.choose (function
                    | Error e -> Some e
                    | Ok _ -> None)
            with
            | [] ->
                Ok(
                    results
                    |> List.choose (function
                        | Ok v -> Some v
                        | Error _ -> None)
                )
            | errors -> Error errors
        | other -> Error [ wrongKind "array" other ]

    // ---- several decoders over one value ----

    /// Each decoder over the same value, in order; the first refusal.
    let sequence (ds: Decoder<'T> list) : Decoder<'T list> =
        fun el ->
            let rec go acc =
                function
                | [] -> Ok(List.rev acc)
                | (d: Decoder<'T>) :: rest ->
                    match d el with
                    | Ok v -> go (v :: acc) rest
                    | Error e -> Error e

            go [] ds

    /// Each decoder over the same value, answering EVERY refusal — `sequence`'s accumulating twin,
    /// for a reader that reports all of a document's independent faults at once.
    let all (ds: Decoder<'T> list) (el: JVal) : Result<'T list, DecodeError list> =
        let results = ds |> List.map (fun d -> d el)

        match
            results
            |> List.choose (function
                | Error e -> Some e
                | Ok _ -> None)
        with
        | [] ->
            Ok(
                results
                |> List.choose (function
                    | Ok v -> Some v
                    | Error _ -> None)
            )
        | errors -> Error errors

    // ---- tags and enumerations ----

    /// A string from a closed set: a miss is `UnknownTag`, naming every known spelling.
    let oneOf (cases: (string * 'T) list) : Decoder<'T> =
        fun el ->
            match el with
            | JStr s ->
                match cases |> List.tryFind (fun (k, _) -> k = s) with
                | Some(_, v) -> Ok v
                | None ->
                    let known = cases |> List.map fst

                    Error(
                        DecodeError.make
                            DecodeCode.UnknownTag
                            ("one of " + quoteAll known)
                            ("unknown value '" + s + "'; the known values are " + quoteAll known)
                    )
            | other -> Error(wrongKind "string" other)

    /// Dispatch on the string under the discriminator `key`: the case's decoder reads the SAME
    /// object. An absent or non-string discriminator is refused as such; an unknown tag is
    /// `UnknownTag` at the discriminator, naming every known tag.
    let tagDispatch (key: string) (cases: (string * Decoder<'T>) list) : Decoder<'T> =
        fun el ->
            match field key str el with
            | Error e -> Error e
            | Ok t ->
                match cases |> List.tryFind (fun (k, _) -> k = t) with
                | Some(_, d) -> d el
                | None ->
                    let known = cases |> List.map fst

                    Error(
                        DecodeError.under
                            (PathSegment.Key key)
                            (DecodeError.make
                                DecodeCode.UnknownTag
                                ("one of " + quoteAll known)
                                ("unknown " + key + " '" + t + "'; the known tags are " + quoteAll known))
                    )

    /// `tagDispatch` under the `"kind"` discriminator.
    let kindDispatch (cases: (string * Decoder<'T>) list) : Decoder<'T> = tagDispatch "kind" cases

    // ---- ranges ----

    /// An integer within `[lo, hi]`; outside it is `OutOfRange`.
    let intRange (lo: int) (hi: int) : Decoder<int> =
        fun el ->
            match int el with
            | Ok i when i >= lo && i <= hi -> Ok i
            | Ok i ->
                Error(
                    DecodeError.make
                        DecodeCode.OutOfRange
                        ("an int in [" + string lo + ", " + string hi + "]")
                        (string i + " is outside [" + string lo + ", " + string hi + "]")
                )
            | Error e -> Error e

    // ---- the strict member policy ----

    let private undeclaredError (known: string list) (k: string) : DecodeError =
        let sorted = List.sort known

        DecodeError.under
            (PathSegment.Key k)
            (DecodeError.make
                DecodeCode.UndeclaredMember
                (if List.isEmpty sorted then
                     "no members"
                 else
                     "one of the members " + quoteAll sorted)
                ("unknown member '"
                 + k
                 + "'; "
                 + (if List.isEmpty sorted then
                        "it takes no members"
                    else
                        "its members are " + quoteAll sorted)))

    /// Every member of an object outside `known`, in authored order — one `UndeclaredMember` each.
    /// A non-object has no members to refuse.
    let undeclared (known: string list) (el: JVal) : DecodeError list =
        match el with
        | JObj fields ->
            fields
            |> List.filter (fun (k, _) -> not (List.contains k known))
            |> List.map (fun (k, _) -> undeclaredError known k)
        | _ -> []

    /// The strict policy (Phase 251's `ReadPolicy.Strict`, generalised): the first member outside
    /// `known` is `UndeclaredMember`, naming the members that WOULD be read. A non-object passes —
    /// the decoder that reads it refuses its kind.
    let members (known: string list) : Decoder<unit> =
        fun el ->
            match undeclared known el with
            | [] -> Ok()
            | e :: _ -> Error e

    /// `d` under the strict policy: the members check first, then the read.
    let closed (known: string list) (d: Decoder<'T>) : Decoder<'T> = members known |> bind (fun () -> d)

    // ---- text ----

    /// Parse JSON text, a parser refusal as a decode refusal at the root (`DecodeError.ofJsonError`).
    let parseWith (maxDepth: int) (json: string) : Result<JVal, DecodeError> =
        Json.parseDetailedWith maxDepth json |> Result.mapError DecodeError.ofJsonError

    /// `parseWith` at the parser's default nesting cap.
    let parse (json: string) : Result<JVal, DecodeError> = parseWith Json.defaultMaxDepth json

    /// Parse JSON text and decode it with `d`.
    let ofString (d: Decoder<'T>) (json: string) : Result<'T, DecodeError> = parse json |> Result.bind d

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
    /// combinator here reads it — or `None` where it has none or `el` is not an object. A forward to
    /// `Decoder.tryMember` since Phase 310.
    let tryProp (name: string) (el: JVal) : JVal option = Decoder.tryMember name el

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

    // The string-error combinators below are FORWARDS onto the typed layer since Phase 310 — each is
    // `Decoder.describing` over its `Decoder` twin, and answers the sentence it always answered. They
    // are kept for one draft and removed at the next breaking draft (STABILITY.md, 0.34.0); new code
    // reads through `Decoder`, whose refusal carries a code and a path.

    /// Forward: `Decoder.field name Decoder.json`.
    let getProp (name: string) (el: JVal) : Result<JVal, string> =
        Decoder.describing (Decoder.field name Decoder.json) el

    /// Forward: `Decoder.str`.
    let asString (el: JVal) : Result<string, string> = Decoder.describing Decoder.str el

    /// Forward: `Decoder.int`.
    let asInt (el: JVal) : Result<int, string> = Decoder.describing Decoder.int el

    /// Forward: `Decoder.bool`.
    let asBool (el: JVal) : Result<bool, string> = Decoder.describing Decoder.bool el

    /// Forward: `Decoder.float`.
    let asFloat (el: JVal) : Result<float, string> = Decoder.describing Decoder.float el

    /// The discriminating `"kind"` tag of an object. Forward: `Decoder.field "kind" Decoder.str`.
    let kindOf (el: JVal) : Result<string, string> =
        Decoder.describing (Decoder.field "kind" Decoder.str) el

    /// Forward: `Decoder.field name Decoder.str`.
    let strField (name: string) (el: JVal) : Result<string, string> =
        Decoder.describing (Decoder.field name Decoder.str) el

    /// Forward: `Decoder.field name Decoder.int`.
    let intField (name: string) (el: JVal) : Result<int, string> =
        Decoder.describing (Decoder.field name Decoder.int) el

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
        {
            /// The capability namespace — a name of the grammar (`Profile.isValidName`) to cross the
            /// wire. Two profiles with different names are `Foreign` to each other.
            Name: string
            /// The incompatibility boundary: any difference makes `negotiate` answer `Foreign`.
            /// Non-negative on the wire.
            Major: int
            /// The additive counter: an artifact authored at a higher minor is `Behind`, and tolerated.
            /// Non-negative on the wire.
            Minor: int
        }

    /// The profile grammar: validation, the canonical rendering, and the parser that reads exactly
    /// the strings `render` writes.
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

    /// The envelope member that carries the artifact. `$`-prefixed, so under `Canon.render` it sorts
    /// before every lower-case data key, and before `$profile`.
    [<Literal>]
    let payloadKey = "$payload"

    [<Literal>]
    let internal requiredProfileKey = "requiredProfile"

    /// A versioned wire envelope: the producer's authored `Profile` + the artifact `Payload`
    /// (a `Node` / `TreeOp` JVal). `$profile` / `$payload` keys are `$`-prefixed so they sort
    /// before any lower-case data key under `Canon.render`. The envelope is the
    /// capability-negotiation carrier — a consumer reads `$profile`, `negotiate`s, then decodes
    /// `$payload` (tolerantly when `Behind`).
    type Envelope =
        {
            /// The profile the producer authored against — what a consumer `negotiate`s before it
            /// reads the payload.
            Profile: Profile
            /// The artifact, carried verbatim; `decode` does not interpret it.
            Payload: JVal
        }

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
        Decoder.describing (Decoder.field profileKey Decoder.str) el
        |> Result.bind Profile.tryParse
        |> Result.bind (fun p ->
            Decoder.describing (Decoder.field payloadKey Decoder.json) el
            |> Result.map (fun payload -> { Profile = p; Payload = payload }))

    /// Parse + decode an envelope from wire bytes.
    let parse (s: string) : Result<Envelope, string> = Json.parse s |> Result.bind decode

    /// A kind the consumer does not understand, captured on the decode boundary. **Transport-only**:
    /// it is reachable here and nowhere on the authoring/encode path — no host can construct one to
    /// emit. `Payload` is the *verbatim parsed object*, so re-rendering it reproduces the producer's
    /// bytes (must-ignore-but-preserve); `RequiredProfile` is the profile the artifact declared it
    /// needs (when present), so the consumer can name what it is missing in a degraded placeholder.
    type UnknownKind =
        {
            /// The unrecognised discriminator, as the decode's `tagOf` read it.
            Kind: string
            /// The whole artifact object as parsed, discriminator included — what `reencode` hands
            /// back unchanged.
            Payload: JVal
            /// The artifact's `requiredProfile` member when present and well-formed; a malformed one
            /// reads as `None`, not as a refusal.
            RequiredProfile: Profile option
        }

    /// The result of a tolerant decode: a fully-understood `'T`, or a preserved `Unknown`.
    type Decoded<'T> =
        /// A tag this consumer understands, fully decoded.
        | Known of 'T
        /// A tag this consumer does not know, preserved for re-encoding. Only a decode produces one.
        | Unknown of UnknownKind

    /// Read an optional `requiredProfile` declaration off an artifact object (the
    /// "artifact declares the profile it requires" shape). Malformed / absent ⇒ `None`.
    let private readRequiredProfile (el: JVal) : Profile option =
        match Decoder.tryMember requiredProfileKey el with
        | Some(JStr s) ->
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
        /// Tags only added, in ascending order — possibly none, which `bump` treats as no change.
        | Additive of added: string list
        /// At least one tag removed (a rename is a removal plus an addition); both lists in
        /// ascending order. Bumps the major and resets the minor.
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
        {
            /// Value to wire text. Must be total: the runners call it unguarded, so a throw aborts
            /// the whole run rather than failing one case.
            Encode: 'T -> string
            /// Wire text to value. The runners only ask whether it is `Ok` or `Error` — the sentence
            /// is copied into an `Outcome`, never checked.
            Decode: string -> Result<'T, string>
        }

    /// Which law a corpus `Case` is held to.
    type CaseKind =
        /// The JSON must decode, and the decoded value must survive encode-then-decode as an EQUAL
        /// value. The re-encoded text is not compared with the fixture, so a non-canonical fixture
        /// can pass.
        | RoundTrip
        /// The decoder must refuse the JSON. Any `Error` passes, whatever it says; `RejectVector`
        /// pins the code and the path.
        | Reject

    /// One corpus fixture: a `RoundTrip` JSON that must decode→encode→decode to an equal
    /// value, or a `Reject` JSON the decoder must refuse. `Tag` feeds the coverage gate.
    type Case =
        {
            /// The label the case's `Outcome` reports under.
            Name: string
            /// Which law `runCorpus` holds the case to.
            Kind: CaseKind
            /// The fixture text, handed to `Codec.Decode` as it stands.
            Json: string
            /// The kind or op tag the case exercises — counted by `coverageGate`, ignored by `runCorpus`.
            Tag: string
        }

    /// The verdict on one corpus `Case` or one `RejectVector`.
    type Outcome =
        {
            /// The `Case.Name` or `RejectVector.Label` it reports on.
            Name: string
            /// True when the case held its law. A failing case is reported here, never as an `Error`
            /// from the runner.
            Passed: bool
            /// `ok` or `rejected as expected` on a pass; on a failure, why — the decoder's own sentence
            /// where it refused.
            Detail: string
        }

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

    /// Hold every case to its law through `codec`, one `Outcome` per case in input order. Never
    /// short-circuits: a failing case is an `Outcome` with `Passed = false`.
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
    ///
    /// THE TWO LONE SURROGATES ARE BUILT, like the controls, and never written as `\u` escapes
    /// (Phase 306). Written as literals they were wrong on both pipelines: the F# compiler
    /// replaces an unpaired surrogate escape in a string literal with U+FFFD, so on .NET this
    /// alphabet held two replacement characters and the fuzz never drew an ill-formed string;
    /// and the Fable compiler, which keeps the unit, could not write it into its output file and
    /// failed the compile of this package outright.
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
               string (char 0xD800)
               string (char 0xDFFF) |]
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

    // ---- refusals carry a code and a path (Phase 310) ----
    // `codecLaws` above is the acceptance half of a codec's laws; these are the refusal half. A
    // reject vector pins the code and the path a refusal must carry, so host twins mirror one error
    // contract rather than one sentence; and the refusal law holds every refusal a decoder raises
    // over a mutated document to a path that resolves in that document.

    /// A reject vector: the document, and the code and path the decoder's refusal must carry.
    type RejectVector =
        {
            /// The label the vector's `Outcome` reports under.
            Label: string
            /// The document text handed to the decoder.
            Input: string
            /// The code the refusal must carry, exactly.
            RefusedAs: DecodeCode
            /// The path the refusal must carry, exactly — root first, `[]` for the root.
            At: PathSegment list
        }

    /// Run reject vectors against a typed decoder over text: each must be refused with exactly its
    /// code and its path.
    let runRejects (decode: string -> Result<'T, DecodeError>) (vectors: RejectVector list) : Outcome list =
        vectors
        |> List.map (fun v ->
            match decode v.Input with
            | Ok _ ->
                { Name = v.Label
                  Passed = false
                  Detail = "expected reject but decoded" }
            | Error e when e.Code = v.RefusedAs && e.Path = v.At ->
                { Name = v.Label
                  Passed = true
                  Detail = "rejected as expected" }
            | Error e ->
                { Name = v.Label
                  Passed = false
                  Detail =
                    "expected "
                    + DecodeError.codeName v.RefusedAs
                    + " at "
                    + DecodePath.render v.At
                    + ", got "
                    + DecodeError.render e })

    /// Structural mutations of a document — the faults the refusal law provokes: every value
    /// replaced by a value of another kind (the root included), every member of every object
    /// removed, and one undeclared member added to every object. Each mutation is the WHOLE document
    /// with one change, so a refusal's path is read against exactly what the decoder saw.
    let mutations (doc: JVal) : JVal list =
        let otherKind (v: JVal) : JVal =
            match v with
            | JStr _ -> JInt 0
            | JInt _
            | JFloat _
            | JBool _ -> JStr "?"
            | JArr _ -> JObj []
            | JObj _ -> JArr []

        let replaceAt (i: int) (x: 'a) (xs: 'a list) : 'a list =
            xs |> List.mapi (fun j y -> if j = i then x else y)

        let rec go (v: JVal) : JVal list =
            let inner =
                match v with
                | JObj fields ->
                    let removed =
                        fields
                        |> List.mapi (fun i _ ->
                            fields
                            |> List.indexed
                            |> List.filter (fun (j, _) -> j <> i)
                            |> List.map snd
                            |> JObj)

                    let added = [ JObj(fields @ [ "$undeclared", JBool true ]) ]

                    let deeper =
                        fields
                        |> List.mapi (fun i (k, x) -> go x |> List.map (fun x2 -> JObj(replaceAt i (k, x2) fields)))
                        |> List.concat

                    removed @ added @ deeper
                | JArr xs ->
                    xs
                    |> List.mapi (fun i x -> go x |> List.map (fun x2 -> JArr(replaceAt i x2 xs)))
                    |> List.concat
                | _ -> []

            otherKind v :: inner

        go doc

    /// The refusal law (Phase 310): over `count` seed-replayable values from `gen`, the decoder
    /// accepts each value's encoding, and every structural mutation of it (`mutations`) that the
    /// decoder REFUSES is refused with a path that resolves in the mutated document
    /// (`DecodeError.resolvesIn`). A refusal whose path names nothing in the document it was raised
    /// over sends a repairing caller nowhere. Returns the first counterexample as an `Error`.
    let refusalLaws
        (encode: 'T -> JVal)
        (decode: Decoder<'T>)
        (gen: int -> 'T)
        (seed: int)
        (count: int)
        : Result<unit, string> =
        let rec go i =
            if i >= count then
                Ok()
            else
                let doc = encode (gen (seed + i))

                match decode doc with
                | Error e ->
                    Error(
                        sprintf
                            "refusalLaws seed=%d: the encoding itself was refused: %s"
                            (seed + i)
                            (DecodeError.render e)
                    )
                | Ok _ ->
                    let unresolved =
                        mutations doc
                        |> List.tryPick (fun m ->
                            match decode m with
                            | Error e when not (DecodeError.resolvesIn m e) -> Some(m, e)
                            | _ -> None)

                    match unresolved with
                    | Some(m, e) ->
                        Error(
                            sprintf
                                "refusalLaws seed=%d: the refusal %s does not resolve in %s"
                                (seed + i)
                                (DecodeError.render e)
                                (Json.render m)
                        )
                    | None -> go (i + 1)

        go 0
