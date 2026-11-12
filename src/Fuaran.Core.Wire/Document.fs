namespace Fuaran.Core

/// A JSON document somebody else defines, held losslessly (Phase 640, DECISIONS.md D252): a null,
/// a boolean, a number AS WRITTEN, a string, ordered items and ordered members. It sits beside
/// `JVal`, and the difference is stated once: `JVal` is Fuaran's own canonical wire value — no
/// null, a number read as an Int32 or a double — and `JDoc` is for JSON Fuaran does not define,
/// where a null is a value and `412000.00` is not `412000.0`.
///
/// It is read through the one grammar (`JDoc.read`, built on `Reader`), written through a guarded
/// writer (`JDoc.tryWrite`), decoded through the decode algebra (`JDocDecoder`), and converted to
/// and from `JVal` by two conversions that refuse what the other side cannot hold
/// (`JDoc.toJVal`, `JDoc.ofJVal`). Every refusal is a `DecodeError`: a code at a path.
type JDoc =
    /// The `null` literal — a value here, where `JVal` has none.
    | DNull
    /// The `true` / `false` literal.
    | DBool of bool
    /// A number as its token TEXT, digits and scale intact: `412000.00`, a thirty-digit integer,
    /// `-0`, `1E+2`. The reader only ever builds one from a token of the JSON number grammar; a
    /// constructed one is checked against that grammar by the writer, which refuses any other text.
    | DNum of string
    /// A string. The reader only ever builds a well-formed one; the writer refuses a lone surrogate.
    | DStr of string
    /// Items in order.
    | DArr of JDoc list
    /// Members in AUTHORED order. The reader refuses a repeated name (`DuplicateMember`, the
    /// scanner's rule), the writer refuses one, and every lookup takes the FIRST member of a name.
    | DObj of (string * JDoc) list

/// A decoder over the lossless document model (Phase 640): `Decoder`'s shape with `JDoc` as the
/// value carrier, refusing with the same `DecodeError`.
type JDocDecoder<'T> = JDoc -> Result<'T, DecodeError>

/// Reading, writing, accessing and converting a `JDoc` (Phase 640).
[<RequireQualifiedAccess>]
module JDoc =

    /// The JSON kind of a value, as a decode refusal names it: `null`, `bool`, `number`, `string`,
    /// `array` or `object`.
    let kindName (d: JDoc) : string =
        match d with
        | DNull -> "null"
        | DBool _ -> "bool"
        | DNum _ -> "number"
        | DStr _ -> "string"
        | DArr _ -> "array"
        | DObj _ -> "object"

    // ---- reading ----

    /// The reading of one value as a `JDoc` through `Reader`'s token surface — a `null` through
    /// `Reader.readNull`, a number through `Reader.numberText`, a container through the scanner's
    /// own member and item loops, and a string or a boolean through `Reader.value` — so a host
    /// reading its own shape can take any value inside it losslessly. There is no grammar here:
    /// every token, bound and refusal is the scanner's.
    let rec reading (r: Reader) : JDoc =
        match Reader.peek r with
        | JsonToken.Null ->
            Reader.readNull r
            DNull
        | JsonToken.Number -> DNum(Reader.numberText r)
        | JsonToken.Array ->
            let acc = ResizeArray<JDoc>()
            Reader.items (fun r -> acc.Add(reading r)) r
            DArr(List.ofSeq acc)
        | JsonToken.Object ->
            let acc = ResizeArray<string * JDoc>()
            Reader.members (fun k r -> acc.Add((k, reading r))) r
            DObj(List.ofSeq acc)
        | JsonToken.String
        | JsonToken.Bool ->
            match Reader.value r with
            | JStr s -> DStr s
            | JBool b -> DBool b
            | other -> invalidOp ("JDoc.reading: the scanner read " + JVal.kindName other + " at a scalar token")

    /// Read `input` as a document under `limits` — the grammar, the bounds and the refusals of
    /// `Json.read`, with every null kept (nothing is erased) and every number read as written. A
    /// refusal is `Decoder.parse`'s: malformed text is `InvalidJson`, a passed bound `LimitExceeded`
    /// at its path. A document `Json.read` accepts under `RejectNull` is read here to the document
    /// that converts back to exactly its `JVal` (`toJVal`); the theorem is
    /// `proofs/JsonDocument.fst`'s `lossless_reader_rebuilds_the_parse`.
    let read (limits: JsonLimits) (input: string) : Result<JDoc, DecodeError> =
        Reader.read { Null = RejectNull; Limits = limits } reading input

    // ---- accessors ----

    /// True for `DNull`.
    let isNull (d: JDoc) : bool =
        match d with
        | DNull -> true
        | _ -> false

    /// The boolean of a `DBool`.
    let tryBool (d: JDoc) : bool option =
        match d with
        | DBool b -> Some b
        | _ -> None

    /// The text of a `DStr`.
    let tryString (d: JDoc) : string option =
        match d with
        | DStr s -> Some s
        | _ -> None

    /// The token text of a `DNum`, exactly as written.
    let tryNumberText (d: JDoc) : string option =
        match d with
        | DNum t -> Some t
        | _ -> None

    /// The items of a `DArr`.
    let tryItems (d: JDoc) : JDoc list option =
        match d with
        | DArr xs -> Some xs
        | _ -> None

    /// The members of a `DObj`, in authored order.
    let tryMembers (d: JDoc) : (string * JDoc) list option =
        match d with
        | DObj fields -> Some fields
        | _ -> None

    /// The member `name` of a `DObj` — the FIRST, where a constructed document repeats a name — or
    /// `None` where it has none or `d` is not an object. A linear scan; `index` builds the lookup
    /// once for an object read many times.
    let tryMember (name: string) (d: JDoc) : JDoc option =
        match d with
        | DObj fields -> fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd
        | _ -> None

    /// The INDEXED member lookup of a `DObj`: every name to its FIRST member, built once, so a
    /// caller reading many members of one wide object pays one pass rather than one per read.
    /// `None` for any other kind.
    let index (d: JDoc) : Map<string, JDoc> option =
        match d with
        | DObj fields -> Some(List.foldBack (fun (k, v) m -> Map.add k v m) fields Map.empty)
        | _ -> None

    // A number text's reading by the scanner's own rule (`Json.readNumberToken`), or `ValueNone`
    // where the text is not a number token of the grammar.
    let private numberReading (text: string) : Json.NumberReading voption =
        if Json.isJsonNumber text then
            ValueSome(Json.readNumberToken text (Json.isFloatToken text))
        else
            ValueNone

    /// The number of a `DNum` as the Int32 `Json.read` would read its token as (`JInt`): `None`
    /// for a float token, an integer past Int32, a text outside the grammar, or another kind.
    let tryInt32 (d: JDoc) : int option =
        match d with
        | DNum t ->
            match numberReading t with
            | ValueSome(Json.ReadsInt i) -> Some i
            | _ -> None
        | _ -> None

    /// The number of a `DNum` as `JVal.asFloat` reads the `JVal` `Json.read` builds for its token:
    /// `None` where `Json.read` would refuse the token (an integer past 2^53 that is not a double's
    /// canonical layout, a magnitude past the finite double range), for a text outside the grammar,
    /// and for another kind. A float token reads as the nearest double, as `Json.read` reads it.
    let tryFloat (d: JDoc) : float option =
        match d with
        | DNum t ->
            match numberReading t with
            | ValueSome(Json.ReadsInt i) -> Some(float i)
            | ValueSome(Json.ReadsFloat f) -> Some f
            | _ -> None
        | _ -> None

    // ---- the guarded writer ----

    /// One unit of pending output: a value at its path (root-first steps, held reversed), or the
    /// rest of an open array or object. Iterative, as `Json.render` is, so depth costs memory and
    /// never the process.
    type private Pending =
        | Value of PathSegment list * JDoc
        | Items of PathSegment list * int * JDoc list
        | Members of PathSegment list * (string * JDoc) list * System.Collections.Generic.HashSet<string>

    let private refusal (code: DecodeCode) (pathRev: PathSegment list) (expected: string) (detail: string) =
        DecodeError.within (List.rev pathRev) (DecodeError.make code expected detail)

    let private surrogateDetail (what: string) (s: string) (k: int) : string =
        what
        + " holds the unpaired surrogate U+"
        + (int s[k]).ToString("X4")
        + " at unit "
        + string k

    /// The document as compact JSON text — no insignificant whitespace, members in authored order,
    /// every number as its token text, every string under the spine's escape (`Json.escape`) — or
    /// the FIRST fault in document order (a member's name before its value), as a `DecodeError` at
    /// its path, with nothing written:
    ///
    ///   - a number text outside the JSON number grammar is `OutOfRange` at the number;
    ///   - a string or a member name holding a lone surrogate is `OutOfRange` at the string, or at
    ///     the member for a name (a lone surrogate has no UTF-8 encoding, and `read` refuses it);
    ///   - a repeated member name is `NotAdmitted` at the second member of the name (`read` refuses
    ///     the text as `DuplicateMember`).
    ///
    /// So a document `tryWrite` writes is text `read` reads back to the same document, and a
    /// document `read` produced from text in this writer's form (compact, strings in the spine's
    /// escape) is written back to that text byte for byte (DECISIONS.md D252 says what that covers).
    let tryWrite (d: JDoc) : Result<string, DecodeError> =
        let sb = System.Text.StringBuilder()
        let mutable stack = [ Value([], d) ]
        let mutable fault: DecodeError option = None

        let quoted (s: string) =
            sb.Append('"') |> ignore
            Json.escapeInto sb s
            sb.Append('"') |> ignore

        while fault.IsNone && not stack.IsEmpty do
            match stack with
            | [] -> ()
            | Value(path, x) :: rest ->
                stack <- rest

                match x with
                | DNull -> sb.Append("null") |> ignore
                | DBool b -> sb.Append(if b then "true" else "false") |> ignore
                | DNum t ->
                    if Json.isJsonNumber t then
                        sb.Append(t) |> ignore
                    else
                        fault <- Some(refusal DecodeCode.OutOfRange path "a JSON number" ("the text '" + t + "'"))
                | DStr s ->
                    match Json.firstIllFormedUnit s with
                    | Some k ->
                        fault <-
                            Some(
                                refusal
                                    DecodeCode.OutOfRange
                                    path
                                    "a well-formed string"
                                    (surrogateDetail "a string" s k)
                            )
                    | None -> quoted s
                | DArr xs ->
                    sb.Append('[') |> ignore
                    stack <- Items(path, 0, xs) :: stack
                | DObj fields ->
                    sb.Append('{') |> ignore

                    stack <-
                        Members(path, fields, System.Collections.Generic.HashSet<string>(HashIdentity.Structural))
                        :: stack
            | Items(path, i, xs) :: rest ->
                match xs with
                | [] ->
                    sb.Append(']') |> ignore
                    stack <- rest
                | x :: tail ->
                    if i > 0 then
                        sb.Append(',') |> ignore

                    stack <- Value(PathSegment.Index i :: path, x) :: Items(path, i + 1, tail) :: rest
            | Members(path, fields, seen) :: rest ->
                match fields with
                | [] ->
                    sb.Append('}') |> ignore
                    stack <- rest
                | (k, x) :: tail ->
                    let at = PathSegment.Key k :: path

                    match Json.firstIllFormedUnit k with
                    | Some u ->
                        fault <-
                            Some(
                                refusal
                                    DecodeCode.OutOfRange
                                    at
                                    "a well-formed member name"
                                    (surrogateDetail "a member name" k u)
                            )
                    | None when not (seen.Add k) ->
                        fault <-
                            Some(
                                refusal
                                    DecodeCode.NotAdmitted
                                    at
                                    "a member name used once in its object"
                                    ("the member name \"" + Json.escape k + "\" is repeated")
                            )
                    | None ->
                        if seen.Count > 1 then
                            sb.Append(',') |> ignore

                        quoted k
                        sb.Append(':') |> ignore
                        stack <- Value(at, x) :: Members(path, tail, seen) :: rest

        match fault with
        | Some e -> Error e
        | None -> Ok(sb.ToString())

    // ---- the two conversions ----

    /// A frame of the conversion to `JVal`: a value at its path, or an array or object being built
    /// (its items or members so far, reversed, and those still to convert).
    type private ToFrame =
        | ToValue of PathSegment list * JDoc
        | ToItems of PathSegment list * int * JVal list * JDoc list
        | ToMembers of
            PathSegment list *
            string *
            (string * JVal) list *
            (string * JDoc) list *
            System.Collections.Generic.HashSet<string>

    /// The document as the `JVal` `Json.read` builds for its text (Phase 640): every scalar but a
    /// number as itself, a number by the scanner's own reading of its token (`JInt` within Int32,
    /// `JFloat` otherwise, exactly as `Json.read` reads it), the containers in order. It refuses,
    /// at the FIRST fault in document order, exactly what `JVal` cannot hold:
    ///
    ///   - a `DNull` is `NotAdmitted` at the null (`JVal` has no null);
    ///   - a number `Json.read` refuses — an integer past 2^53 that is not a double's canonical
    ///     layout, a magnitude past the finite double range — is `OutOfRange` at the number, and so
    ///     is a constructed text outside the number grammar;
    ///   - a repeated member name (only a constructed document carries one: `read` refuses it) is
    ///     `NotAdmitted` at the repeated member, before its value is converted — so every path a
    ///     refusal names leads, through the first member of each name, to the value it means.
    ///
    /// So `toJVal` succeeds on a document `read` produced exactly when `Json.read` accepts its text
    /// under `RejectNull`, with the same value (`proofs/JsonDocument.fst`). Strings pass as they
    /// are: `JVal` holds a lone surrogate, and its guarded writers refuse it as they refuse it in
    /// any `JVal`. Iterative: a document of any depth converts.
    let toJVal (d: JDoc) : Result<JVal, DecodeError> =
        // `stack` holds the frames still open; `built` the value the last finished frame produced.
        let mutable stack = [ ToValue([], d) ]
        let mutable built: JVal option = None
        let mutable fault: DecodeError option = None

        let finish (v: JVal) = built <- Some v

        while fault.IsNone && not stack.IsEmpty do
            match stack, built with
            | ToValue(path, x) :: rest, _ ->
                stack <- rest

                match x with
                | DNull -> fault <- Some(refusal DecodeCode.NotAdmitted path "a value JVal holds" "null")
                | DBool b -> finish (JBool b)
                | DStr s -> finish (JStr s)
                | DNum t ->
                    match numberReading t with
                    | ValueSome(Json.ReadsInt i) -> finish (JInt i)
                    | ValueSome(Json.ReadsFloat f) -> finish (JFloat f)
                    | ValueSome(Json.Refused message) ->
                        fault <- Some(refusal DecodeCode.OutOfRange path "a number JVal holds" message)
                    | ValueNone ->
                        fault <- Some(refusal DecodeCode.OutOfRange path "a JSON number" ("the text '" + t + "'"))
                | DArr xs -> stack <- ToItems(path, 0, [], xs) :: stack
                | DObj fields ->
                    stack <-
                        ToMembers(
                            path,
                            "",
                            [],
                            fields,
                            System.Collections.Generic.HashSet<string>(HashIdentity.Structural)
                        )
                        :: stack
            | ToItems(path, i, doneRev, xs) :: rest, b ->
                // `b` is the item just converted, when one was.
                let doneRev, i =
                    match b with
                    | Some v ->
                        built <- None
                        v :: doneRev, i + 1
                    | None -> doneRev, i

                match xs with
                | [] ->
                    stack <- rest
                    finish (JArr(List.rev doneRev))
                | x :: tail ->
                    stack <-
                        ToValue(PathSegment.Index i :: path, x)
                        :: ToItems(path, i, doneRev, tail)
                        :: rest
            | ToMembers(path, key, doneRev, fields, seen) :: rest, b ->
                let doneRev =
                    match b with
                    | Some v ->
                        built <- None
                        (key, v) :: doneRev
                    | None -> doneRev

                match fields with
                | [] ->
                    stack <- rest
                    finish (JObj(List.rev doneRev))
                | (k, _) :: _ when not (seen.Add k) ->
                    fault <-
                        Some(
                            refusal
                                DecodeCode.NotAdmitted
                                (PathSegment.Key k :: path)
                                "a member name used once in its object"
                                ("the member name \"" + Json.escape k + "\" is repeated")
                        )
                | (k, x) :: tail ->
                    stack <-
                        ToValue(PathSegment.Key k :: path, x)
                        :: ToMembers(path, k, doneRev, tail, seen)
                        :: rest
            | [], _ -> ()

        match fault, built with
        | Some e, _ -> Error e
        | None, Some v -> Ok v
        | None, None -> invalidOp "JDoc.toJVal: the conversion finished with no value"

    /// A frame of the conversion from `JVal`, as `ToFrame` is for the conversion to it.
    type private OfFrame =
        | OfValue of PathSegment list * JVal
        | OfItems of PathSegment list * int * JDoc list * JVal list
        | OfMembers of PathSegment list * string * (string * JDoc) list * (string * JVal) list

    /// The `JVal` as a document (Phase 640): every scalar as itself, a `JInt` as its decimal text and
    /// a finite `JFloat` as the text `Json.render` writes for it (`FloatLayout.roundTrip`), so a
    /// value `Json.tryRender` writes is written by `tryWrite` to the same bytes. It refuses one
    /// thing, the one value `JVal` holds that a document cannot: a non-finite `JFloat`, which has no
    /// JSON number text, is `OutOfRange` at the float, the first in document order. A string is
    /// carried as it is (a lone surrogate is the writer's refusal, as it is `Json.tryRender`'s).
    let ofJVal (v: JVal) : Result<JDoc, DecodeError> =
        let mutable stack = [ OfValue([], v) ]
        let mutable built: JDoc option = None
        let mutable fault: DecodeError option = None

        let finish (d: JDoc) = built <- Some d

        while fault.IsNone && not stack.IsEmpty do
            match stack, built with
            | OfValue(path, x) :: rest, _ ->
                stack <- rest

                match x with
                | JBool b -> finish (DBool b)
                | JStr s -> finish (DStr s)
                | JInt i -> finish (DNum(string i))
                | JFloat f ->
                    match JVal.nonFiniteToken f with
                    | Some token -> fault <- Some(refusal DecodeCode.OutOfRange path "a finite number" token)
                    | None -> finish (DNum(FloatLayout.roundTrip f))
                | JArr xs -> stack <- OfItems(path, 0, [], xs) :: stack
                | JObj fields -> stack <- OfMembers(path, "", [], fields) :: stack
            | OfItems(path, i, doneRev, xs) :: rest, b ->
                let doneRev, i =
                    match b with
                    | Some d ->
                        built <- None
                        d :: doneRev, i + 1
                    | None -> doneRev, i

                match xs with
                | [] ->
                    stack <- rest
                    finish (DArr(List.rev doneRev))
                | x :: tail ->
                    stack <-
                        OfValue(PathSegment.Index i :: path, x)
                        :: OfItems(path, i, doneRev, tail)
                        :: rest
            | OfMembers(path, key, doneRev, fields) :: rest, b ->
                let doneRev =
                    match b with
                    | Some d ->
                        built <- None
                        (key, d) :: doneRev
                    | None -> doneRev

                match fields with
                | [] ->
                    stack <- rest
                    finish (DObj(List.rev doneRev))
                | (k, x) :: tail ->
                    stack <-
                        OfValue(PathSegment.Key k :: path, x)
                        :: OfMembers(path, k, doneRev, tail)
                        :: rest
            | [], _ -> ()

        match fault, built with
        | Some e, _ -> Error e
        | None, Some d -> Ok d
        | None, None -> invalidOp "JDoc.ofJVal: the conversion finished with no value"

/// The decode algebra over the lossless document model (Phase 640): `Decoder`'s combinators,
/// instantiated over `JDoc` as the value carrier (`DecodeOver`, the one algebra), so a document
/// decodes with the same refusals at the same paths as a `JVal` does, and a `null` meets a
/// decoder where it stands — refused by every kind reader as `WrongKind` naming `null`, read by
/// `nullable` as `None`. The scalar readers are the document's own: a number is read from its token
/// text by the scanner's rule, or kept as that text (`numberText`). Any `Decoder` reads a document
/// through `ofDecoder`, which converts it to `JVal` first and refuses what `JVal` cannot hold.
[<RequireQualifiedAccess>]
module JDocDecoder =

    let private carrier: DecodeCarrier<JDoc> =
        { KindName = JDoc.kindName
          Members =
            function
            | DObj fields -> ValueSome fields
            | _ -> ValueNone
          Items =
            function
            | DArr xs -> ValueSome xs
            | _ -> ValueNone
          Text =
            function
            | DStr s -> ValueSome s
            | _ -> ValueNone }

    /// A decoder that answers `v` whatever it is handed.
    let succeed (v: 'T) : JDocDecoder<'T> = fun _ -> Ok v

    /// `Decoder.fail`: a refusal at the value itself, its code a `RootDecodeCode`.
    let fail (code: RootDecodeCode) (expected: string) (detail: string) : JDocDecoder<'T> =
        fun el -> DecodeOver.fail code expected detail el

    /// `Decoder.map`.
    let map (f: 'T -> 'U) (d: JDocDecoder<'T>) : JDocDecoder<'U> = fun el -> DecodeOver.map f d el

    /// `Decoder.bind`.
    let bind (f: 'T -> JDocDecoder<'U>) (d: JDocDecoder<'T>) : JDocDecoder<'U> = fun el -> DecodeOver.bind f d el

    /// `Decoder.pair`.
    let pair (a: JDocDecoder<'A>) (b: JDocDecoder<'B>) (f: 'A -> 'B -> 'C) : JDocDecoder<'C> =
        a |> bind (fun x -> b |> map (f x))

    /// `Decoder.andThen`.
    let andThen (f: 'T -> Result<'U, RootDecodeCode * string * string>) (d: JDocDecoder<'T>) : JDocDecoder<'U> =
        fun el -> DecodeOver.andThen f d el

    // ---- the kinds ----

    /// Any value, verbatim.
    let json: JDocDecoder<JDoc> = Ok

    /// `None` for a `DNull`, else `d`'s answer: the one decoder that reads a null as a value.
    let nullable (d: JDocDecoder<'T>) : JDocDecoder<'T option> =
        fun el ->
            match el with
            | DNull -> Ok None
            | other -> d other |> Result.map Some

    /// A `DStr`; any other kind is `WrongKind`.
    let str: JDocDecoder<string> = fun el -> DecodeOver.text carrier el

    /// A `DBool`; any other kind is `WrongKind`.
    let bool: JDocDecoder<bool> =
        function
        | DBool b -> Ok b
        | other -> Error(DecodeOver.wrongKind carrier "bool" other)

    /// A number's token text, exactly as written: digits, sign, point and exponent intact. Any
    /// other kind is `WrongKind`.
    let numberText: JDocDecoder<string> =
        function
        | DNum t -> Ok t
        | other -> Error(DecodeOver.wrongKind carrier "number" other)

    /// A number `Json.read` reads as `JInt` — an integer token within Int32 (`JDoc.tryInt32`). A
    /// number it does not is `OutOfRange` at the number; any other kind is `WrongKind`.
    let int: JDocDecoder<int> =
        function
        | DNum t as d ->
            match JDoc.tryInt32 d with
            | Some i -> Ok i
            | None -> Error(DecodeError.make DecodeCode.OutOfRange "an Int32 integer" ("the number " + t))
        | other -> Error(DecodeOver.wrongKind carrier "int" other)

    /// A number as `JVal.asFloat` reads `Json.read`'s value for it (`JDoc.tryFloat`). A number
    /// `Json.read` refuses is `OutOfRange` at the number; any other kind is `WrongKind`.
    let float: JDocDecoder<float> =
        function
        | DNum t as d ->
            match JDoc.tryFloat d with
            | Some f -> Ok f
            | None -> Error(DecodeError.make DecodeCode.OutOfRange "a number JVal holds" ("the number " + t))
        | other -> Error(DecodeOver.wrongKind carrier "number" other)

    /// An array's items, undecoded.
    let items: JDocDecoder<JDoc list> = fun el -> DecodeOver.items carrier el

    /// An object's members, in authored order, undecoded.
    let obj: JDocDecoder<(string * JDoc) list> = fun el -> DecodeOver.members carrier el

    // ---- members, arrays, several decoders, tags ----

    /// `Decoder.field`: absent is `MissingField`, a non-object `WrongKind`. A member whose value is
    /// `null` is PRESENT, and `d` reads the null (`nullable` admits it).
    let field (name: string) (d: JDocDecoder<'T>) : JDocDecoder<'T> =
        fun el -> DecodeOver.field carrier name d el

    /// `Decoder.optField`: absent is `Ok None`, present-and-refused is the refusal.
    let optField (name: string) (d: JDocDecoder<'T>) : JDocDecoder<'T option> =
        fun el -> DecodeOver.optField carrier name d el

    /// `Decoder.fieldOr`.
    let fieldOr (name: string) (fallback: 'T) (d: JDocDecoder<'T>) : JDocDecoder<'T> =
        optField name d |> map (Option.defaultValue fallback)

    /// `Decoder.at`.
    let at (path: PathSegment list) (d: JDocDecoder<'T>) : JDocDecoder<'T> =
        fun el -> DecodeOver.at carrier path d el

    /// `Decoder.mapListIndexed`.
    let mapListIndexed (f: int -> JDocDecoder<'T>) : JDocDecoder<'T list> =
        fun el -> DecodeOver.mapListIndexed carrier f el

    /// `Decoder.list`.
    let list (d: JDocDecoder<'T>) : JDocDecoder<'T list> = mapListIndexed (fun _ -> d)

    /// `Decoder.boundedList`.
    let boundedList (maxItems: int) (d: JDocDecoder<'T>) : JDocDecoder<'T list> =
        fun el -> DecodeOver.boundedList carrier maxItems d el

    /// `Decoder.listAll`.
    let listAll (d: JDocDecoder<'T>) (el: JDoc) : Result<'T list, DecodeError list> = DecodeOver.listAll carrier d el

    /// `Decoder.sequence`.
    let sequence (ds: JDocDecoder<'T> list) : JDocDecoder<'T list> = fun el -> DecodeOver.sequence ds el

    /// `Decoder.all`.
    let all (ds: JDocDecoder<'T> list) (el: JDoc) : Result<'T list, DecodeError list> = DecodeOver.all ds el

    /// `Decoder.oneOf`.
    let oneOf (cases: (string * 'T) list) : JDocDecoder<'T> =
        fun el -> DecodeOver.oneOf carrier cases el

    /// `Decoder.tagDispatch`.
    let tagDispatch (key: string) (cases: (string * JDocDecoder<'T>) list) : JDocDecoder<'T> =
        fun el -> DecodeOver.tagDispatch carrier key cases el

    /// `Decoder.undeclared`.
    let undeclared (known: string list) (el: JDoc) : DecodeError list = DecodeOver.undeclared carrier known el

    /// `Decoder.members`.
    let members (known: string list) : JDocDecoder<unit> =
        fun el -> DecodeOver.membersOnly carrier known el

    /// `Decoder.closed`.
    let closed (known: string list) (d: JDocDecoder<'T>) : JDocDecoder<'T> = members known |> bind (fun () -> d)

    // ---- the bridges ----

    /// Any `Decoder` over a document: the document converted to `JVal` (`JDoc.toJVal`), then read.
    /// A value `JVal` cannot hold refuses the conversion at its path, wherever it stands, whether or
    /// not the decoder would have read it; read such a document with this module's combinators.
    let ofDecoder (d: Decoder<'T>) : JDocDecoder<'T> =
        fun el ->
            match JDoc.toJVal el with
            | Ok v -> d v
            | Error e -> Error e

    /// Read `input` under `limits` as a document (`JDoc.read`) and decode it with `d`.
    let read (limits: JsonLimits) (d: JDocDecoder<'T>) (input: string) : Result<'T, DecodeError> =
        JDoc.read limits input |> Result.bind d
