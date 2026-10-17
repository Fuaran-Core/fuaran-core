namespace Fuaran.Core

/// One step of a decode path (Phase 310): a member of an object by its key, or an item of an array
/// by its zero-based index. A path is the list of steps from the document's root, root first.
/// Declared beside the parser since Phase 465, because a limited parse reports a breach's path
/// (`JsonLimitBreach`); `DecodePath` renders and resolves it.
[<RequireQualifiedAccess>]
type PathSegment =
    /// A member of an object by its exact key; where a document repeats the key, the first.
    | Key of string
    /// An item of an array, zero-based.
    | Index of int

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
    /// A backslash followed by a character outside the JSON escape set, a string that is not
    /// well-formed UTF-16 (a lone or ill-ordered surrogate, raw or escaped), OR a raw C0 control
    /// character (U+0000 to U+001F) inside a string, which the grammar requires escaped (Phase 462).
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
    /// One object carries the same member name twice (Phase 462). The position is the repeated
    /// name's opening quote. A first-wins and a last-wins reader read two different documents from
    /// these bytes, so the document is refused rather than resolved either way.
    | DuplicateMember

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

/// Which bound a limit breach passed (Phase 465) — one case per field of `JsonLimits`.
[<RequireQualifiedAccess>]
type JsonLimit =
    /// `JsonLimits.MaxDepth`: an object or array opened AT the bound (the bound admits that many
    /// nested containers, the root counting as one).
    | Depth
    /// `JsonLimits.MaxStringLength`: a string, or a member key, past the bound — in Unicode code
    /// points, a surrogate pair counting as one.
    | StringLength
    /// `JsonLimits.MaxArrayLength`: an array's `MaxArrayLength + 1`-th item.
    | ArrayLength
    /// `JsonLimits.MaxObjectMembers`: an object's `MaxObjectMembers + 1`-th member (an erased
    /// member null is not a member).
    | ObjectMembers
    /// `JsonLimits.MaxDocumentBytes`: the input's UTF-8 length.
    | DocumentBytes

/// The bounds a LIMITED parse enforces on the way down (Phase 465): each is checked before the
/// recursion or the allocation it guards, so nothing past a bound is ever read or built, and a
/// breach is a `JsonLimitBreach` — never malformed JSON. The VALUES are the caller's: Core states
/// none and is not a host of any wire's limits section (DECISIONS.md D84, refined by D159); a wire
/// host passes its own specification's numbers and maps the breach to its own error envelope.
/// `JsonLimits.depthOnly` is the record every pre-existing entry point runs under.
type JsonLimits =
    {
        /// The nesting cap: the number of nested objects and arrays admitted. `Json.defaultMaxDepth`
        /// is the parser's own stack guard.
        MaxDepth: int
        /// The most Unicode code points one decoded string, or one member key, may hold.
        MaxStringLength: int
        /// The most items one array may hold.
        MaxArrayLength: int
        /// The most members one object may hold.
        MaxObjectMembers: int
        /// The most UTF-8 bytes the whole input may encode to; checked before parsing.
        MaxDocumentBytes: int
    }

/// A limit breach (Phase 465): the limit, its declared value, the decode path to the container the
/// breach was raised in, and the parser's position. It never carries the input. `Path` is the
/// container's own: an array whose item count passes the bound, an object whose member count or
/// whose key's length does, the container opened at the depth bound; a string value's breach names
/// its own position in the document, and a document-bytes breach the root. `Position` is a 0-based
/// UTF-16 index: the opening bracket for `Depth`, where the refused item or member would start for
/// `ArrayLength` / `ObjectMembers`, just past the first UTF-16 unit of the code point that passes
/// the bound for `StringLength` (the high half of a surrogate pair, before its low half is read),
/// and `0` for `DocumentBytes`.
type JsonLimitBreach =
    {
        /// The bound that was passed.
        Limit: JsonLimit
        /// That bound's value in the record the parse ran under.
        Declared: int
        /// The decode path to the container the breach was raised in (see above), root first.
        Path: PathSegment list
        /// The parser's position at the breach, a 0-based UTF-16 index into the input (see above).
        Position: int
    }

/// Why a limited parse refused a document (Phase 465): the text is not JSON — `Malformed` carries
/// EXACTLY the refusal `parseDetailed` gives it, kind, message and position — or it is JSON and a
/// bound was passed. The two are distinct cases by construction: a breach is never reported as
/// malformed, and malformed text never as a breach (DECISIONS.md D159).
[<RequireQualifiedAccess>]
type JsonRefusal =
    /// The text is not JSON: exactly the refusal `parseDetailed` gives it.
    | Malformed of JsonError
    /// The text is JSON as far as it was read, and a bound of the record was passed.
    | LimitExceeded of JsonLimitBreach

/// The limits, named and read (Phase 465).
[<RequireQualifiedAccess>]
module JsonLimit =
    /// The limit's wire name — the member a limits record spells it as, and what a decode vector
    /// declares: `depth`, `stringLength`, `arrayLength`, `objectMembers`, `documentBytes`.
    let name (l: JsonLimit) : string =
        match l with
        | JsonLimit.Depth -> "depth"
        | JsonLimit.StringLength -> "stringLength"
        | JsonLimit.ArrayLength -> "arrayLength"
        | JsonLimit.ObjectMembers -> "objectMembers"
        | JsonLimit.DocumentBytes -> "documentBytes"

    /// The limit's value in a record.
    let declared (limits: JsonLimits) (l: JsonLimit) : int =
        match l with
        | JsonLimit.Depth -> limits.MaxDepth
        | JsonLimit.StringLength -> limits.MaxStringLength
        | JsonLimit.ArrayLength -> limits.MaxArrayLength
        | JsonLimit.ObjectMembers -> limits.MaxObjectMembers
        | JsonLimit.DocumentBytes -> limits.MaxDocumentBytes

/// Limits records (Phase 465).
[<RequireQualifiedAccess>]
module JsonLimits =
    /// The parser's own stack guard at `maxDepth` and no other bound — what every entry point but
    /// `Json.parseWithLimits` runs under. The three counts can never reach `Int32.MaxValue` (each
    /// is bounded by the input's length), so no breach but the depth cap's can arise under it.
    let depthOnly (maxDepth: int) : JsonLimits =
        { MaxDepth = maxDepth
          MaxStringLength = System.Int32.MaxValue
          MaxArrayLength = System.Int32.MaxValue
          MaxObjectMembers = System.Int32.MaxValue
          MaxDocumentBytes = System.Int32.MaxValue }

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
        Array.init 0x20 (fun c -> "\\u00" + string hex[c >>> 4] + string hex[c &&& 0xF])

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
            let code = int s[i]

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
            let code = int s[i]

            if code < 0x20 || code = 0x22 || code = 0x5C then
                if i > start then
                    sb.Append(s, start, i - start) |> ignore

                (if code = 0x22 then sb.Append("\\\"")
                 elif code = 0x5C then sb.Append("\\\\")
                 else sb.Append(controlEscapes[code]))
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
    /// too; the one copy the standalone layers keep (`JsonString.quote` in `Fuaran.Core.OpStream`,
    /// public as `OpStream.Jsonl.quote` — D2 forbids it a reference here; `Actor.encode` spells
    /// through it, and `Dag.toJsonl` in `Fuaran.Core.OpStream.Dag` has spelled through it rather than
    /// a copy of its own since Phase 388) is held value-identical to it by `StringEscapeVectors` in the
    /// conformance kit. Beside that copy sits its legacy twin, `JsonString.quoteLegacy`: the `V1`
    /// profile's spelling, which `escapeWith EncodingProfile.V1` writes on this side, the two held to
    /// one table by `EncodingProfileVectors`. The parser accepts
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
    /// (Phase 299): it is the scan the three guarded renderers (`Json.tryRender`, `Canon.tryRender`,
    /// `Canonical.tryWrite`) refuse on, which each used to carry as a private copy. Iterative since Phase 306.
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
            let u = int s[k]

            if u >= 0xD800 && u <= 0xDBFF then
                if k + 1 < n && int s[k + 1] >= 0xDC00 && int s[k + 1] <= 0xDFFF then
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
    /// The three guarded renderers refuse on it (Phase 306).
    let firstIllFormedString (v: JVal) : (string * string) option =
        let describe (what: string) (s: string) : string option =
            firstIllFormedUnit s
            |> Option.map (fun k ->
                what
                + " holds the unpaired surrogate U+"
                + (int s[k]).ToString("X4")
                + " at unit "
                + string k)

        firstInDocumentOrder
            (describe "a member key")
            (fun x ->
                match x with
                | JStr s -> describe "a string" s
                | _ -> None)
            v

    /// The FIRST object in `v` that carries a member name twice, in document order, as
    /// `(path, description)`; the description names the member. `None` where every object's
    /// names are distinct. The three guarded renderers refuse on it (Phase 462; `Canonical.tryWrite`
    /// since Phase 495): `render` writes such
    /// an object as it stands, and `parse` refuses the text (`DuplicateMember`), so a value with a
    /// repeated member is un-parseable wire from a guarded entry point, as an ill-formed string is.
    /// Iterative, for the reason the writer and the scan above are: a value of any depth is answered.
    let firstRepeatedMember (v: JVal) : (string * string) option =
        let mutable stack = [ At([], v) ]
        let mutable found = None

        while found.IsNone && not stack.IsEmpty do
            match stack with
            | [] -> ()
            | At(path, x) :: rest ->
                stack <- rest

                match x with
                | JArr xs -> stack <- RestItems(path, 0, xs) :: stack
                | JObj fields ->
                    let seen = System.Collections.Generic.HashSet<string>(HashIdentity.Structural)

                    match fields |> List.tryPick (fun (k, _) -> if seen.Add k then None else Some k) with
                    | Some k -> found <- Some(pathText path, "the member name \"" + escape k + "\" is repeated")
                    | None -> stack <- RestMembers(path, fields) :: stack
                | _ -> ()
            | RestItems(path, i, xs) :: rest ->
                match xs with
                | [] -> stack <- rest
                | x :: tail -> stack <- At(Index i :: path, x) :: RestItems(path, i + 1, tail) :: rest
            | RestMembers(path, fields) :: rest ->
                match fields with
                | [] -> stack <- rest
                | (k, x) :: tail -> stack <- At(Key k :: path, x) :: RestMembers(path, tail) :: rest

        found

    /// A `"kind"`-tagged object — the wire envelope every domain node/op serialises as.
    /// `tag` leads; `fields` follow in author order (camelCase keys by discipline).
    let kindObj (tag: string) (fields: (string * JVal) list) : JVal = JObj(("kind", JStr tag) :: fields)

    /// `render` under the `encode` name: author key order, round-trip float layout, and UNGUARDED —
    /// a non-finite float or an ill-formed string yields text `parse` refuses. `tryEncode` refuses
    /// both instead.
    let encode (v: JVal) : string = render v

    /// Total, guarded render (Phase 12). `render` lays a `JFloat` out with `FloatLayout.roundTrip`, so a
    /// non-finite float (`NaN` / `Infinity` / `-Infinity`) emits a token that is not valid JSON
    /// and that `parse` then rejects — `render` succeeds but produces un-parseable wire, breaking
    /// `render ∘ parse = id`. The Fuaran wire model has no non-finite float (the same posture as
    /// "no null"). `tryRender` names the first non-finite `JFloat` as a typed `Error` instead.
    ///
    /// Since Phase 306 it refuses a second class for the same reason: a string (or member key)
    /// that is not well-formed UTF-16. `render` writes a lone surrogate through as it found it, and
    /// `parse` refuses the text — so that, too, was un-parseable wire from a guarded entry point.
    /// A non-finite float is looked for first, so every refusal this function already made keeps its
    /// message. Since Phase 462 it refuses a third class, after the other two: an object carrying a
    /// member name twice, which `parse` refuses as `DuplicateMember`. Over a value with none of the
    /// three it is exactly `Ok (render v)`, at any nesting depth.
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
            | None ->
                match firstRepeatedMember v with
                | Some(path, what) ->
                    Error(
                        "repeated member is not representable on the Fuaran wire: "
                        + what
                        + " at "
                        + path
                    )
                | None -> Ok(render v)

    /// `tryRender` under the `encode` name — the total, guarded encode entry point.
    let tryEncode (v: JVal) : Result<string, string> = tryRender v

    /// The index of the first digit of `tok` from `k` on that is not `0` (or `tok.Length`): where
    /// `readInt32`'s significant digits begin (Phase 372).
    let rec private firstSignificant (tok: string) (k: int) : int =
        if k < tok.Length && tok[k] = '0' then
            firstSignificant tok (k + 1)
        else
            k

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
    ///
    /// A TOKEN TOO LONG FOR INT32 IS REFUSED BY ITS DIGIT COUNT (Phase 372), counting SIGNIFICANT
    /// digits: the run after its leading zeros, which the reader has always accepted (`007` is 7).
    /// More than ten cannot fit, because Int32's extremes are ten digits each way, so the value's
    /// magnitude is at least 10^10 and the platform reader answers `None` on every host. Asking it
    /// anyway cost an exception per token under Fable, whose `Int32.tryParse` throws and catches on
    /// overflow: a third of a node decode where whole doubles past 2^53 were one number in eight,
    /// and every millisecond timestamp paid it. Ten digits or fewer still go to the platform
    /// reader, which alone decides the ten-digit edge. Both hosts run this one path, so the suite
    /// holds the branch the node measurement depends on.
    let readInt32 (tok: string) : int option =
        let digitsFrom =
            if tok.Length > 0 && (tok[0] = '-' || tok[0] = '+') then
                1
            else
                0

        let mutable shaped = tok.Length > digitsFrom

        for k in digitsFrom .. tok.Length - 1 do
            if tok[k] < '0' || tok[k] > '9' then
                shaped <- false

        if not shaped then
            None
        // Counted only when the digit run is longer than ten, so a short token pays one comparison.
        elif
            tok.Length - digitsFrom > 10
            && tok.Length - firstSignificant tok digitsFrom > 10
        then
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

    /// Internal signal for the recursive-descent parser; never escapes the parse entry points.
    /// Carries the classified kind, the message, and the position captured at the raise site.
    exception private JsonParseError of JsonErrorKind * string * int

    /// Internal signal for a limit breach (Phase 465): the limit, its declared value and the
    /// position; the path is read off the scanner's frames at the catch. Never escapes the limited
    /// entry point, and cannot be raised under `JsonLimits.depthOnly` at all.
    exception private JsonLimitError of JsonLimit * int * int

    /// The default maximum object/array nesting depth for `parse`. Input nested deeper fails
    /// as a named `Error` instead of overflowing the stack — a `StackOverflowException` is
    /// uncatchable in .NET and would crash the host, breaking totality (GP4) on the one entry
    /// point built to ingest untrusted/portable wire data. `parseWith` overrides it.
    [<Literal>]
    let defaultMaxDepth = 512

    /// The ONE grammar (Phase 368): the scanner `parseDetailedWithPolicy` reads a document with, and
    /// the only one `Reader` reads with. Every routine below is the parser's own: the whitespace and
    /// value-start rule, the string reader (by runs, Phase 366), the number reader (the JSON number
    /// grammar, the int53 guard and the Phase 253 canonical-float admission), and the array and
    /// object loops with their depth cap and their `,` / closing expectations. `parse` builds a
    /// `JVal` through them; `Reader` reads typed values through the SAME members, so there is no
    /// second copy of any rule for the two to drift apart on, and a refusal is raised at the same
    /// position with the same kind and message whichever of them asked. One scanner per document.
    ///
    /// The scanner runs under a `JsonLimits` record (Phase 465). The depth cap is the record's
    /// `MaxDepth`, raised as `MaxDepthExceeded` exactly as it always was; the string, array and
    /// object bounds are checked on the way down — before the unit, item or member they guard is
    /// read — and raised as `JsonLimitError`, which only `DocumentLimited` catches. Under
    /// `JsonLimits.depthOnly` none of the three can fire, so every other entry point is the parser
    /// as it has always been.
    [<Sealed>]
    type internal Scanner(policy: NullPolicy, limits: JsonLimits, input: string) =
        let tolerateMemberNull =
            match policy with
            | RejectNull -> false
            | EraseMemberNull -> true

        let maxDepth = limits.MaxDepth
        let n = input.Length
        let mutable i = 0

        let fail (kind: JsonErrorKind) (msg: string) : 'a = raise (JsonParseError(kind, msg, i))

        let breach (limit: JsonLimit) : 'a =
            raise (JsonLimitError(limit, JsonLimit.declared limits limit, i))

        // The container frames open at this position, outermost first — the breach's PATH, kept
        // as two parallel lists so a member costs no allocation: `frameKeys` holds the key of the
        // member being read (null while the key itself is being read, and for an array frame),
        // `frameIdx` the index of the item being read (-1 for an object frame, and for an array
        // frame while the next item's bound is being tested). A frame whose slot is unfilled ends
        // the path: the breach is the container's own.
        let frameKeys = ResizeArray<string>()
        let frameIdx = ResizeArray<int>()

        let pathNow () : PathSegment list =
            // Outermost frame first; an unfilled slot can only be the innermost frame (every frame
            // above it is a value being read, which is a filled slot), and it ends the path.
            let rec go (k: int) (acc: PathSegment list) =
                if k >= frameKeys.Count then
                    List.rev acc
                elif frameIdx[k] >= 0 then
                    go (k + 1) (PathSegment.Index frameIdx[k] :: acc)
                elif not (isNull frameKeys[k]) then
                    go (k + 1) (PathSegment.Key frameKeys[k] :: acc)
                else
                    List.rev acc

            go 0 []

        let pushFrame () =
            frameKeys.Add null
            frameIdx.Add -1
            frameKeys.Count - 1

        let popFrame (top: int) =
            frameKeys.RemoveAt top
            frameIdx.RemoveAt top

        // EOI sentinel spelled as the ESCAPED literal '\000' — a raw U+0000 byte here
        // previously made this whole file "binary" to ripgrep/GitHub code search,
        // hiding the parser's source from tooling. The sentinel value itself never
        // matters: `peek ()`'s result is only compared against structural chars
        // ('-', '.', 'e', '}', …), all of which NUL fails, and every consuming loop
        // is bounds-guarded by `i < n`.
        let peek () = if i < n then input[i] else '\000'

        let isWs c =
            c = ' ' || c = '\t' || c = '\n' || c = '\r'

        let skipWs () =
            while i < n && isWs input[i] do
                i <- i + 1

        // Where a value is due: whitespace skipped, end of input refused, and the character a value
        // must start with returned unconsumed.
        let valueStart () : char =
            skipWs ()

            if i >= n then
                fail UnexpectedEndOfInput "unexpected end of input"

            input[i]

        let expect (c: char) =
            if i < n && input[i] = c then
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
            // The code points taken so far (Phase 465): a low half completing a pending high is
            // not a new one, so a unit is counted exactly when nothing is pending.
            let mutable codePoints = 0

            let check (u: int) =
                if pendingHigh && not (isLowSurrogate u) then
                    fail BadEscape "ill-formed string: a high surrogate not followed by a low surrogate"
                elif not pendingHigh && isLowSurrogate u then
                    fail BadEscape "ill-formed string: a low surrogate with no high surrogate before it"

                if not pendingHigh then
                    // The string bound, on the way down: a unit that would be the `max + 1`-th
                    // code point is refused here, after it and before it is appended — whichever
                    // spelling it arrived in, and before the run it sits in is copied.
                    if codePoints >= limits.MaxStringLength then
                        breach JsonLimit.StringLength

                    codePoints <- codePoints + 1

                pendingHigh <- isHighSurrogate u

            // Advance `i` over an escape-free run; it stops AT the `"` or `\` that ends it, or at `n`.
            let scanRun () =
                let mutable go = true

                while go && i < n do
                    let c = input[i]

                    if c = '"' || c = '\\' then
                        go <- false
                    elif c < ' ' then
                        // Phase 462: a raw C0 control is refused AT the character. The grammar
                        // requires it escaped, and no conformant encoder writes it raw.
                        fail
                            BadEscape
                            ("unescaped control character U+00"
                             + string ("0123456789ABCDEF"[int c >>> 4])
                             + string ("0123456789ABCDEF"[int c &&& 15])
                             + " in a string")
                    else
                        i <- i + 1
                        check (int c)

            let start = i
            scanRun ()

            if i >= n then
                fail UnterminatedString "unterminated string"

            if input[i] = '"' then
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

                    let c = input[i]
                    i <- i + 1

                    if c = '"' then
                        if pendingHigh then
                            fail BadEscape "ill-formed string: a high surrogate not followed by a low surrogate"

                        fin <- true
                    else
                        if i >= n then
                            fail UnterminatedEscape "unterminated escape"

                        let e = input[i]
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
                                (hexDigit input[i] <<< 12)
                                + (hexDigit input[i + 1] <<< 8)
                                + (hexDigit input[i + 2] <<< 4)
                                + hexDigit input[i + 3]

                            i <- i + 4
                            append code
                        | _ -> fail BadEscape ("bad escape '\\" + string e + "'")

                        let s = i
                        scanRun ()

                        if i > s then
                            sb.Append(input, s, i - s) |> ignore

                sb.ToString()

        let isDigitAt (k: int) =
            k < n && input[k] >= '0' && input[k] <= '9'

        // The token is SCANNED as it always was — an optional sign, then digits, point, digits,
        // exponent, each optional — so a refusal reports the token and position it always did; it
        // is then held to the JSON number grammar (`isJsonNumber`) before anything reads it.
        // The number reader returns the double and records, in `numIsInt` / `numInt`, whether the
        // token is the integer `parse` reads as `JInt` (Phase 368: so `Reader` takes a number into a
        // typed buffer through this same routine, with no `JVal` case built).
        let mutable numIsInt = false
        let mutable numInt = 0

        let scanNumber () : float =
            numIsInt <- false
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
                | true, v -> v
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
                | Some v ->
                    numIsInt <- true
                    numInt <- v
                    float v
                | None ->
                    // Safety is judged on the TOKEN, not on a parsed double: 2^53 + 1
                    // rounds to 2^53 as a double, so a range check on the value would
                    // wrongly accept it. An integer is int53-safe iff |value| ≤ 2^53 =
                    // 9007199254740992 (16 digits). Compare the digit string lexically —
                    // the grammar above refuses a leading zero, so for equal length that IS
                    // the numeric order. Fable-clean (string + Double.TryParse only, no Int64).
                    let digits = if tok[0] = '-' then tok.Substring 1 else tok

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
                        | true, v -> v
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
                            v
                        | _ ->
                            fail
                                MalformedNumber
                                ("integer literal outside the int53 safe range (|n| > 2^53); it cannot round-trip without precision loss: "
                                 + tok)

        let parseNumber () : JVal =
            let v = scanNumber ()
            if numIsInt then JInt numInt else JFloat v

        // The array loop: the depth cap, `[`, then `item` once per element (it reads one value from
        // the next value position), each followed by `,` or `]`. The items bound (Phase 465) is
        // tested at the loop's head, before `item` is called: an item that would be the `max + 1`-th
        // is refused where it would start, before a character of it is read, with the frame's slot
        // unfilled so the breach's path is the array's own.
        let arrayLoop (depth: int) (item: unit -> unit) : unit =
            if depth >= maxDepth then
                fail MaxDepthExceeded ("max nesting depth " + string maxDepth + " exceeded")

            expect '['
            skipWs ()

            if peek () = ']' then
                i <- i + 1
            else
                let top = pushFrame ()
                let mutable count = 0
                let mutable go = true

                while go do
                    if count >= limits.MaxArrayLength then
                        breach JsonLimit.ArrayLength

                    frameIdx[top] <- count
                    item ()
                    count <- count + 1
                    frameIdx[top] <- -1
                    skipWs ()

                    match peek () with
                    | ',' -> i <- i + 1
                    | ']' ->
                        i <- i + 1
                        go <- false
                    | _ -> fail ExpectedToken "expected ',' or ']'"

                popFrame top

        // The object loop: the depth cap, `{`, then per member its key, `:`, and `onMember key` (it
        // reads the member's value), each followed by `,` or `}`.
        //
        // The one behavioural fork of `EraseMemberNull`, and the only place in the parser that can
        // erase anything: a member whose value is exactly the `null` token is consumed and
        // `onMember` is NOT called, so the object reads as though the member had been omitted.
        // Nothing malformed is absorbed: a truncated near-miss (`nul`) fails this test and falls
        // through to the value reader, which names it exactly as the strict policy does, and a
        // trailing-garbage one (`nullish`) is caught by the ',' / '}' expectation below. Under
        // `RejectNull` the test is never taken and the member path is the pre-existing one.
        //
        // The members bound (Phase 465) is tested after the loop's `skipWs` and before the key is
        // read, at the key's opening quote: the members ACCUMULATED so far are the count — an erased
        // member null was never accumulated and is not one, which is what keeps the tolerant read
        // the strict read of the erased document (Phase 190) under a bound too — and a member that
        // would be the `max + 1`-th is refused before a character of it is read. While the key is
        // being read the frame's slot is unfilled, so a breach inside the key (its own length) is
        // the object's.
        let objectLoop (depth: int) (onMember: string -> unit) : unit =
            if depth >= maxDepth then
                fail MaxDepthExceeded ("max nesting depth " + string maxDepth + " exceeded")

            expect '{'
            skipWs ()

            if peek () = '}' then
                i <- i + 1
            else
                let top = pushFrame ()
                let mutable count = 0
                let mutable go = true
                // Phase 462: the names read so far. The first is held alone, and the set is built
                // only when a second member arrives, so a one-member object allocates nothing.
                let mutable first: string option = None
                let mutable seen: System.Collections.Generic.HashSet<string> option = None

                while go do
                    skipWs ()
                    // The slot is unfilled from here until the key is read: a breach of the members
                    // bound, or of the string bound inside the key, is the object's own.
                    frameKeys[top] <- null

                    if count >= limits.MaxObjectMembers then
                        breach JsonLimit.ObjectMembers

                    let keyAt = i
                    let key = parseString ()
                    frameKeys[top] <- key
                    skipWs ()
                    expect ':'
                    skipWs ()

                    let erased = tolerateMemberNull && i + 4 <= n && input.Substring(i, 4) = "null"

                    if erased then
                        i <- i + 4
                    else
                        onMember key
                        count <- count + 1

                        // A repeated name is refused when the member is ACCUMULATED, after its value
                        // is read, at the name's opening quote. So a refusal inside the value still
                        // wins, and under the strict policy a repeated member whose value is `null`
                        // is the null's refusal. An erased member null is NOT an occurrence: the
                        // tolerant read IS the strict read of the document with its member nulls
                        // deleted (Phase 190), and that holds only if the erased member leaves no
                        // trace here either (DECISIONS.md D157).
                        let repeated =
                            match first, seen with
                            | None, _ ->
                                first <- Some key
                                false
                            | Some f, None ->
                                if f = key then
                                    true
                                else
                                    let set = System.Collections.Generic.HashSet<string>(HashIdentity.Structural)
                                    set.Add f |> ignore
                                    set.Add key |> ignore
                                    seen <- Some set
                                    false
                            | Some _, Some set -> not (set.Add key)

                        if repeated then
                            i <- keyAt
                            fail DuplicateMember "repeated member name in one object"

                    skipWs ()

                    match peek () with
                    | ',' -> i <- i + 1
                    | '}' ->
                        i <- i + 1
                        go <- false
                    | _ -> fail ExpectedToken "expected ',' or '}'"

                popFrame top

        let rec parseValue (depth: int) : JVal =
            match valueStart () with
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
            let fields = ResizeArray<string * JVal>()
            objectLoop depth (fun key -> fields.Add((key, parseValue (depth + 1))))
            JObj(List.ofSeq fields)

        and parseArray (depth: int) : JVal =
            let items = ResizeArray<JVal>()
            arrayLoop depth (fun () -> items.Add(parseValue (depth + 1)))
            JArr(List.ofSeq items)

        // The whole document: one value, then nothing but whitespace — the grammar's refusals
        // raised, the trailing-input one constructed.
        let document () : Result<JVal, JsonError> =
            let v = parseValue 0
            skipWs ()

            if i <> n then
                Error
                    { Position = i
                      Message = "trailing characters"
                      Kind = TrailingCharacters }
            else
                Ok v

        member _.Length = n
        member _.MaxDepth = maxDepth

        member _.Pos
            with get () = i
            and set (v: int) = i <- v

        member _.SkipWs() = skipWs ()
        member _.Fail(kind: JsonErrorKind, msg: string) : 'a = fail kind msg
        member _.ValueStart() : char = valueStart ()
        member _.String() : string = parseString ()
        member _.Number() : float = scanNumber ()
        member _.NumberIsInt = numIsInt
        member _.NumberInt = numInt
        member _.Value(depth: int) : JVal = parseValue depth
        member _.Array(depth: int, item: unit -> unit) : unit = arrayLoop depth item
        member _.Object(depth: int, onMember: string -> unit) : unit = objectLoop depth onMember

        /// The whole document as `parse` reads it. Under `JsonLimits.depthOnly`, which is what every
        /// caller of this member passes, no `JsonLimitError` can arise.
        member _.Document() : Result<JVal, JsonError> =
            try
                document ()
            with JsonParseError(kind, msg, pos) ->
                Error
                    { Position = pos
                      Message = msg
                      Kind = kind }

        /// The whole document as `parseWithLimits` reads it (Phase 465): every grammar refusal as
        /// `Malformed`, exactly as `Document` reports it, and every breach as `LimitExceeded` — the
        /// depth cap's `MaxDepthExceeded` lifted to the `Depth` breach at the same position, with the
        /// path the frames still hold.
        member _.DocumentLimited() : Result<JVal, JsonRefusal> =
            try
                document () |> Result.mapError JsonRefusal.Malformed
            with
            | JsonParseError(MaxDepthExceeded, _, pos) ->
                Error(
                    JsonRefusal.LimitExceeded
                        { Limit = JsonLimit.Depth
                          Declared = maxDepth
                          Path = pathNow ()
                          Position = pos }
                )
            | JsonParseError(kind, msg, pos) ->
                Error(
                    JsonRefusal.Malformed
                        { Position = pos
                          Message = msg
                          Kind = kind }
                )
            | JsonLimitError(limit, declared, pos) ->
                Error(
                    JsonRefusal.LimitExceeded
                        { Limit = limit
                          Declared = declared
                          Path = pathNow ()
                          Position = pos }
                )

    /// `parseDetailed` under an explicit nesting cap **and an explicit `NullPolicy`** — the core
    /// parser every other entry point is a wrapper over. Under `RejectNull` (the default every
    /// pre-existing entry point passes) it is the parser as it has always been, byte-for-byte.
    /// Returns a structured `JsonError` on failure; the `parseWith` family are the string-error
    /// wrappers.
    let parseDetailedWithPolicy (policy: NullPolicy) (maxDepth: int) (input: string) : Result<JVal, JsonError> =
        Scanner(policy, JsonLimits.depthOnly maxDepth, input).Document()

    // The input's UTF-8 length, counted off its UTF-16 units — Fable-clean, and the count a host
    // handed a decoded string must make (a native length under-counts a CJK document threefold). A
    // lone surrogate counts the three bytes a replacement character takes; the parser refuses it
    // anyway.
    let private utf8Length (s: string) : int64 =
        let mutable total = 0L
        let mutable k = 0

        while k < s.Length do
            let c = int s[k]

            if c < 0x80 then
                total <- total + 1L
                k <- k + 1
            elif c < 0x800 then
                total <- total + 2L
                k <- k + 1
            elif
                c >= 0xD800
                && c <= 0xDBFF
                && k + 1 < s.Length
                && int s[k + 1] >= 0xDC00
                && int s[k + 1] <= 0xDFFF
            then
                total <- total + 4L
                k <- k + 2
            else
                total <- total + 3L
                k <- k + 1

        total

    /// The LIMITED parse (Phase 465): `parseDetailedWithPolicy` under a `JsonLimits` record, every
    /// bound enforced on the way down — the document-bytes bound before a character is read, the
    /// depth cap as it always was, and the string, array and object bounds before the unit, item or
    /// member they guard is read — and a breach reported as `JsonRefusal.LimitExceeded`, naming the
    /// limit, its declared value, the path and the position, never as malformed JSON. Everything
    /// else is exactly `parseDetailedWithPolicy policy limits.MaxDepth`: a document it accepts is
    /// accepted to the same value, and one it refuses for any reason but the depth cap is refused
    /// with `Malformed` carrying its error. The first fault in document order wins, so a breach
    /// outranks malformed text after it and malformed text outranks a breach after IT.
    ///
    /// This is the MECHANISM a wire host bounds its parse with; the values, their units and the
    /// error envelope are the host's own specification's (DECISIONS.md D84, refined by D159), and
    /// `Decoder.parseWithLimits` is the typed-decode form of the same call.
    let parseWithLimits (policy: NullPolicy) (limits: JsonLimits) (input: string) : Result<JVal, JsonRefusal> =
        // One comparison when the input cannot encode past the bound (three bytes a unit is the
        // most a unit costs; a pair is four for two), the count otherwise.
        let bytesOver =
            int64 input.Length * 3L > int64 limits.MaxDocumentBytes
            && utf8Length input > int64 limits.MaxDocumentBytes

        if bytesOver then
            Error(
                JsonRefusal.LimitExceeded
                    { Limit = JsonLimit.DocumentBytes
                      Declared = limits.MaxDocumentBytes
                      Path = []
                      Position = 0 }
            )
        else
            Scanner(policy, limits, input).DocumentLimited()

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
    /// this entry point enforces none of them and claims no §21 conformance (DECISIONS.md D84). A
    /// host enforces its own values through `parseWithLimits` (Phase 465, D159).
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

    /// Why `Reader.read` refused a document (Phase 368).
    [<RequireQualifiedAccess>]
    type ReadError =
        /// The text is not a document `parseDetailed` reads, and this is exactly the refusal
        /// `parseDetailed` gives it — kind, message and position — whatever the reading asked for.
        | Malformed of JsonError
        /// The text IS a document `parseDetailed` reads, and the value starting at `position` is
        /// not the shape the reading asked for there (`expected` names it: "an array", "a string").
        | Mismatch of position: int * expected: string

    /// Internal signal for a shape mismatch; never escapes `Reader.read`.
    exception private ReadMismatch of int * string

    /// A cursor over one document, handed to the reading function `Reader.read` runs (Phase 368).
    /// Each `Reader` function reads exactly ONE value where one is due; reading none, or two, where
    /// one is due is a programming error and raises `InvalidOperationException`.
    [<Sealed>]
    type Reader internal (scan: Scanner) =
        let mutable due = true
        let mutable depth = 0
        member internal _.Scan = scan

        member internal _.Due
            with get () = due
            and set (v: bool) = due <- v

        member internal _.Depth
            with get () = depth
            and set (v: int) = depth <- v

    /// A reader for a large document whose consumer wants typed values, not the `JVal` tree
    /// (Phase 368). `parse` builds a `JVal` for every value and a list cell for every element, and
    /// a consumer then walks the tree a second time to type it; for a large homogeneous array that
    /// is most of the decode's allocation (DECISIONS.md D121). A reading here takes such an array
    /// straight into a `string[]` / `float[]` / `int[]`, and takes any other value as the `JVal`
    /// `parse` would have built for it.
    ///
    /// It is the SAME grammar as `parse`, not a second one: every token, structure, depth and
    /// end-of-input rule is the scanner `parseDetailedWithPolicy` runs (strict `RejectNull`, the cap
    /// `defaultMaxDepth`). The refusals agree exactly: a document `parseDetailed` refuses is refused
    /// with `ReadError.Malformed` carrying `parseDetailed`'s own error, whatever the reading asked
    /// for, and a document it accepts is refused only with `ReadError.Mismatch`, when a value is not
    /// the shape asked for. A reading that succeeds returns what typing `parseDetailed`'s tree the
    /// same way would.
    [<RequireQualifiedAccess>]
    module Reader =
        let private take (r: Reader) =
            if not r.Due then
                invalidOp "Json.Reader: a value was read where none was due (a reading reads exactly one value)"

            r.Due <- false

        // A container where one is due: the value's start, which must be `opening`, else a mismatch
        // at that position (a character no value starts with is the grammar's to refuse: the
        // mismatch defers to `parseDetailed`, which names it).
        let private opening (r: Reader) (c: char) (expected: string) : Scanner =
            take r
            let s = r.Scan

            if s.ValueStart() <> c then
                raise (ReadMismatch(s.Pos, expected))

            s

        // Run `f` where exactly one value is due, one level deeper.
        let private nested (r: Reader) (f: unit -> unit) =
            let d = r.Depth
            r.Due <- true
            r.Depth <- d + 1
            f ()

            if r.Due then
                invalidOp "Json.Reader: a member or item was not read (a reading reads exactly one value)"

            r.Depth <- d

        // A homogeneous array, each element taken by `element` at its value start.
        let private arrayOf (r: Reader) (element: Scanner -> unit) =
            let s = opening r '[' "an array"
            s.Array(r.Depth, (fun () -> element s))

        /// Any value, as `parse` builds it.
        let value (r: Reader) : JVal =
            take r
            r.Scan.Value r.Depth

        /// An array of strings, into a `string[]`.
        let strings (r: Reader) : string[] =
            let acc = ResizeArray<string>()

            arrayOf r (fun s ->
                if s.ValueStart() <> '"' then
                    raise (ReadMismatch(s.Pos, "a string"))

                acc.Add(s.String()))

            acc.ToArray()

        /// An array of numbers — the `JInt` and `JFloat` items of `parse`, read as `JVal.asFloat`
        /// reads them — into a `float[]`.
        let floats (r: Reader) : float[] =
            let acc = ResizeArray<float>()

            arrayOf r (fun s ->
                let c = s.ValueStart()

                if not (c = '-' || (c >= '0' && c <= '9')) then
                    raise (ReadMismatch(s.Pos, "a number"))

                acc.Add(s.Number()))

            acc.ToArray()

        /// An array of the integers `parse` reads as `JInt` (an integer token within Int32), into an
        /// `int[]`. Any other number is a mismatch, as it would be a `JFloat` in the tree.
        let ints (r: Reader) : int[] =
            let acc = ResizeArray<int>()

            arrayOf r (fun s ->
                let c = s.ValueStart()
                let at = s.Pos

                if not (c = '-' || (c >= '0' && c <= '9')) then
                    raise (ReadMismatch(at, "an Int32 integer"))

                s.Number() |> ignore

                if not s.NumberIsInt then
                    raise (ReadMismatch(at, "an Int32 integer"))

                acc.Add s.NumberInt)

            acc.ToArray()

        /// An array, calling `item` once per element in order; `item` reads the element.
        let items (item: Reader -> unit) (r: Reader) : unit =
            let s = opening r '[' "an array"
            s.Array(r.Depth, (fun () -> nested r (fun () -> item r)))

        /// An object, calling `onMember key` once per member in AUTHORED order; `onMember` reads the
        /// member's value. A repeated member name is refused (`DuplicateMember`, Phase 462) when the
        /// scanner reaches it, as `parseDetailed` refuses it.
        let members (onMember: string -> Reader -> unit) (r: Reader) : unit =
            let s = opening r '{' "an object"
            s.Object(r.Depth, (fun key -> nested r (fun () -> onMember key r)))

        /// Read `input` with `reading`, which reads exactly one value (the document's), then require
        /// nothing but whitespace after it — the document `parseDetailed` reads, read typed.
        let read (reading: Reader -> 'a) (input: string) : Result<'a, ReadError> =
            let s = Scanner(RejectNull, JsonLimits.depthOnly defaultMaxDepth, input)
            let r = Reader(s)

            try
                let a = reading r

                if r.Due then
                    invalidOp "Json.Reader.read: the reading read no value"

                s.SkipWs()

                if s.Pos <> s.Length then
                    Error(
                        ReadError.Malformed
                            { Position = s.Pos
                              Message = "trailing characters"
                              Kind = TrailingCharacters }
                    )
                else
                    Ok a
            with
            | JsonParseError(kind, msg, pos) ->
                Error(
                    ReadError.Malformed
                        { Position = pos
                          Message = msg
                          Kind = kind }
                )
            | ReadMismatch(pos, expected) ->
                // A grammar refusal anywhere in the document outranks a shape mismatch, so a
                // document `parseDetailed` refuses is refused with its error whatever was asked.
                match parseDetailed input with
                | Error e -> Error(ReadError.Malformed e)
                | Ok _ -> Error(ReadError.Mismatch(pos, expected))
