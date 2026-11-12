namespace Fuaran.Core

/// A decoder over the typed refusal (Phase 310) — the one decode layer since Phase 519 deleted its
/// string-error twin.
type Decoder<'T> = JVal -> Result<'T, DecodeError>

/// The view the decode algebra reads a value carrier through (Phase 640): the kind a value is, and
/// its members, its items and its text where it has them. The algebra is written ONCE over it
/// (`DecodeOver`): `Decoder` is its instance over `JVal`, and `JDocDecoder` its instance over the
/// lossless document model, so a document decodes through the same combinators, with the same
/// refusals at the same paths.
[<NoEquality; NoComparison>]
type internal DecodeCarrier<'V> =
    {
        /// The JSON kind a value is, as a refusal names it.
        KindName: 'V -> string
        /// An object's members in authored order, or `ValueNone` for any other kind.
        Members: 'V -> (string * 'V) list voption
        /// An array's items, or `ValueNone` for any other kind.
        Items: 'V -> 'V list voption
        /// A string's text, or `ValueNone` for any other kind.
        Text: 'V -> string voption
    }

/// The decode algebra over a carrier (Phase 640) — every combinator of `Decoder` that is not about
/// one carrier's own scalars, written once. The doc comments are `Decoder`'s, which is its
/// instance over `JVal`.
module internal DecodeOver =

    let private rootCode (code: RootDecodeCode) : DecodeCode =
        match code with
        | RootDecodeCode.WrongKind -> DecodeCode.WrongKind
        | RootDecodeCode.UnknownTag -> DecodeCode.UnknownTag
        | RootDecodeCode.OutOfRange -> DecodeCode.OutOfRange
        | RootDecodeCode.UndeclaredMember -> DecodeCode.UndeclaredMember
        | RootDecodeCode.LimitExceeded -> DecodeCode.LimitExceeded
        | RootDecodeCode.NotAdmitted -> DecodeCode.NotAdmitted
        | RootDecodeCode.SchemaFault -> DecodeCode.SchemaFault

    let fail (code: RootDecodeCode) (expected: string) (detail: string) : 'V -> Result<'T, DecodeError> =
        fun _ -> Error(DecodeError.make (rootCode code) expected detail)

    let map (f: 'T -> 'U) (d: 'V -> Result<'T, DecodeError>) : 'V -> Result<'U, DecodeError> =
        fun el -> d el |> Result.map f

    let bind
        (f: 'T -> 'V -> Result<'U, DecodeError>)
        (d: 'V -> Result<'T, DecodeError>)
        : 'V -> Result<'U, DecodeError> =
        fun el ->
            match d el with
            | Ok v -> f v el
            | Error e -> Error e

    let andThen
        (f: 'T -> Result<'U, RootDecodeCode * string * string>)
        (d: 'V -> Result<'T, DecodeError>)
        : 'V -> Result<'U, DecodeError> =
        fun el ->
            match d el with
            | Ok v ->
                match f v with
                | Ok u -> Ok u
                | Error(code, expected, detail) -> fail code expected detail el
            | Error e -> Error e

    let wrongKind (c: DecodeCarrier<'V>) (expected: string) (found: 'V) : DecodeError =
        DecodeError.make DecodeCode.WrongKind expected (c.KindName found)

    let missing (name: string) : DecodeError =
        DecodeError.make DecodeCode.MissingField ("a member '" + name + "'") name
        |> DecodeError.under (PathSegment.Key name)

    let tryMember (c: DecodeCarrier<'V>) (name: string) (el: 'V) : 'V option =
        match c.Members el with
        | ValueSome fields -> fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd
        | ValueNone -> None

    let private quoteAll (xs: string list) : string =
        xs |> List.map (fun x -> "'" + x + "'") |> String.concat ", "

    let text (c: DecodeCarrier<'V>) : 'V -> Result<string, DecodeError> =
        fun el ->
            match c.Text el with
            | ValueSome s -> Ok s
            | ValueNone -> Error(wrongKind c "string" el)

    let items (c: DecodeCarrier<'V>) : 'V -> Result<'V list, DecodeError> =
        fun el ->
            match c.Items el with
            | ValueSome xs -> Ok xs
            | ValueNone -> Error(wrongKind c "array" el)

    let members (c: DecodeCarrier<'V>) : 'V -> Result<(string * 'V) list, DecodeError> =
        fun el ->
            match c.Members el with
            | ValueSome fields -> Ok fields
            | ValueNone -> Error(wrongKind c "object" el)

    let field (c: DecodeCarrier<'V>) (name: string) (d: 'V -> Result<'T, DecodeError>) : 'V -> Result<'T, DecodeError> =
        fun el ->
            match c.Members el with
            | ValueSome _ ->
                match tryMember c name el with
                | Some v -> d v |> Result.mapError (DecodeError.under (PathSegment.Key name))
                | None -> Error(missing name)
            | ValueNone -> Error(wrongKind c "object" el)

    let optField
        (c: DecodeCarrier<'V>)
        (name: string)
        (d: 'V -> Result<'T, DecodeError>)
        : 'V -> Result<'T option, DecodeError> =
        fun el ->
            match c.Members el with
            | ValueSome _ ->
                match tryMember c name el with
                | Some v ->
                    d v
                    |> Result.map Some
                    |> Result.mapError (DecodeError.under (PathSegment.Key name))
                | None -> Ok None
            | ValueNone -> Error(wrongKind c "object" el)

    let at
        (c: DecodeCarrier<'V>)
        (path: PathSegment list)
        (d: 'V -> Result<'T, DecodeError>)
        : 'V -> Result<'T, DecodeError> =
        fun el ->
            let rec go (doneRev: PathSegment list) (rest: PathSegment list) (v: 'V) =
                let here () = List.rev doneRev

                match rest with
                | [] -> d v |> Result.mapError (DecodeError.within (here ()))
                | (PathSegment.Key k as s) :: tail ->
                    match c.Members v with
                    | ValueSome _ ->
                        match tryMember c k v with
                        | Some x -> go (s :: doneRev) tail x
                        | None -> Error(DecodeError.within (here ()) (missing k))
                    | ValueNone -> Error(DecodeError.within (here ()) (wrongKind c "object" v))
                | (PathSegment.Index i as s) :: tail ->
                    match c.Items v with
                    | ValueSome xs ->
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
                                        ("item " + string i))
                            )
                    | ValueNone -> Error(DecodeError.within (here ()) (wrongKind c "array" v))

            go [] path el

    let mapListIndexed
        (c: DecodeCarrier<'V>)
        (f: int -> 'V -> Result<'T, DecodeError>)
        : 'V -> Result<'T list, DecodeError> =
        fun el ->
            match c.Items el with
            | ValueSome xs ->
                let rec go i acc =
                    function
                    | [] -> Ok(List.rev acc)
                    | x :: rest ->
                        match f i x with
                        | Ok v -> go (i + 1) (v :: acc) rest
                        | Error e -> Error(DecodeError.under (PathSegment.Index i) e)

                go 0 [] xs
            | ValueNone -> Error(wrongKind c "array" el)

    let boundedList
        (c: DecodeCarrier<'V>)
        (maxItems: int)
        (d: 'V -> Result<'T, DecodeError>)
        : 'V -> Result<'T list, DecodeError> =
        fun el ->
            match c.Items el with
            | ValueSome xs when List.length xs > maxItems ->
                Error(
                    DecodeError.make
                        DecodeCode.LimitExceeded
                        ("at most " + string maxItems + " items")
                        ("an array of " + string (List.length xs) + " items")
                )
            | _ -> mapListIndexed c (fun _ -> d) el

    let private errorsAndOks (results: Result<'T, DecodeError> list) : Result<'T list, DecodeError list> =
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

    let listAll (c: DecodeCarrier<'V>) (d: 'V -> Result<'T, DecodeError>) (el: 'V) : Result<'T list, DecodeError list> =
        match c.Items el with
        | ValueSome xs ->
            xs
            |> List.mapi (fun i x -> d x |> Result.mapError (DecodeError.under (PathSegment.Index i)))
            |> errorsAndOks
        | ValueNone -> Error [ wrongKind c "array" el ]

    let sequence (ds: ('V -> Result<'T, DecodeError>) list) : 'V -> Result<'T list, DecodeError> =
        fun el ->
            let rec go acc =
                function
                | [] -> Ok(List.rev acc)
                | (d: 'V -> Result<'T, DecodeError>) :: rest ->
                    match d el with
                    | Ok v -> go (v :: acc) rest
                    | Error e -> Error e

            go [] ds

    let all (ds: ('V -> Result<'T, DecodeError>) list) (el: 'V) : Result<'T list, DecodeError list> =
        ds |> List.map (fun d -> d el) |> errorsAndOks

    let oneOf (c: DecodeCarrier<'V>) (cases: (string * 'T) list) : 'V -> Result<'T, DecodeError> =
        fun el ->
            match c.Text el with
            | ValueSome s ->
                match cases |> List.tryFind (fun (k, _) -> k = s) with
                | Some(_, v) -> Ok v
                | None ->
                    let known = cases |> List.map fst

                    Error(DecodeError.make DecodeCode.UnknownTag ("one of " + quoteAll known) ("value '" + s + "'"))
            | ValueNone -> Error(wrongKind c "string" el)

    let tagDispatch
        (c: DecodeCarrier<'V>)
        (key: string)
        (cases: (string * ('V -> Result<'T, DecodeError>)) list)
        : 'V -> Result<'T, DecodeError> =
        fun el ->
            match field c key (text c) el with
            | Error e -> Error e
            | Ok t ->
                match cases |> List.tryFind (fun (k, _) -> k = t) with
                | Some(_, d) -> d el
                | None ->
                    let known = cases |> List.map fst

                    Error(
                        DecodeError.under
                            (PathSegment.Key key)
                            (DecodeError.make DecodeCode.UnknownTag ("one of " + quoteAll known) (key + " '" + t + "'"))
                    )

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
                ("'" + k + "'"))

    let undeclared (c: DecodeCarrier<'V>) (known: string list) (el: 'V) : DecodeError list =
        match c.Members el with
        | ValueSome fields ->
            fields
            |> List.filter (fun (k, _) -> not (List.contains k known))
            |> List.map (fun (k, _) -> undeclaredError known k)
        | ValueNone -> []

    let membersOnly (c: DecodeCarrier<'V>) (known: string list) : 'V -> Result<unit, DecodeError> =
        fun el ->
            match undeclared c known el with
            | [] -> Ok()
            | e :: _ -> Error e

/// The typed decode combinators (Phase 310). Each returns `Result<_, DecodeError>`; a refusal
/// leaving a member or an item gains that step, so the path a caller reads is the path from the
/// value the outermost decoder was handed. Fable-clean, like the rest of the package.
///
/// Optional members are three-valued and the third value is a refusal: `optField` reads an ABSENT
/// member as `Ok None` and a PRESENT member its decoder refuses as that refusal — never as absence,
/// which is how an ill-typed member went silently unread in every hand-rolled optional reader this
/// layer replaced.
///
/// Since Phase 640 every combinator here that is not about `JVal`'s own scalars is `DecodeOver`'s,
/// instantiated over `JVal`; `JDocDecoder` is the same algebra over the lossless document model. Each
/// is written over the value it reads (`fun el -> ...`), so its compiled method keeps the arity a
/// compiled consumer calls it with.
[<RequireQualifiedAccess>]
module Decoder =

    /// The `JVal` carrier the algebra reads through (Phase 640).
    let internal carrier: DecodeCarrier<JVal> =
        { KindName = JVal.kindName
          Members =
            function
            | JObj fields -> ValueSome fields
            | _ -> ValueNone
          Items =
            function
            | JArr xs -> ValueSome xs
            | _ -> ValueNone
          Text =
            function
            | JStr s -> ValueSome s
            | _ -> ValueNone }

    /// A decoder that answers `v` whatever it is handed.
    let succeed (v: 'T) : Decoder<'T> = fun _ -> Ok v

    /// A decoder that refuses whatever it is handed, at the value itself. Its code is a
    /// `RootDecodeCode` (Phase 443): a refusal at the value cannot be a `MissingField` (raise one
    /// through `field`, which names the member) or an `InvalidJson` (the parse entry points' code), so
    /// every refusal `fail` answers resolves in the value it was handed. `detail` is what was found
    /// there; the sentence is `DecodeError.sentence`'s (Phase 520).
    let fail (code: RootDecodeCode) (expected: string) (detail: string) : Decoder<'T> =
        fun el -> DecodeOver.fail code expected detail el

    /// Convert the answer; a refusal passes through untouched, its path included.
    let map (f: 'T -> 'U) (d: Decoder<'T>) : Decoder<'U> = fun el -> DecodeOver.map f d el

    /// Decode, then decode the SAME value with a decoder chosen by the first answer.
    let bind (f: 'T -> Decoder<'U>) (d: Decoder<'T>) : Decoder<'U> = fun el -> DecodeOver.bind f d el

    /// Two decoders over the SAME value, their answers combined by `f` (Phase 509): `a` reads first,
    /// and its refusal is answered without running `b`. The two-member read every case of a tagged
    /// union with two payloads spells.
    let pair (a: Decoder<'A>) (b: Decoder<'B>) (f: 'A -> 'B -> 'C) : Decoder<'C> = a |> bind (fun x -> b |> map (f x))

    /// Decode, then check or convert the answer. `f` refuses with the triple `fail` takes — a
    /// `RootDecodeCode`, what the position expected, and the detail — and `andThen` raises it AT the
    /// value (Phase 453), so every refusal it answers resolves in the value it was handed. A check
    /// that must point INSIDE the value (an item, a member) is `bind` to `at <path> (fail ...)`,
    /// which walks the path in the document rather than trusting it.
    let andThen (f: 'T -> Result<'U, RootDecodeCode * string * string>) (d: Decoder<'T>) : Decoder<'U> =
        fun el -> DecodeOver.andThen f d el

    /// The string-error reading of a decoder — its refusal's sentence. The bridge a codec whose
    /// published error is a `string` returns through.
    let describing (d: Decoder<'T>) : JVal -> Result<'T, string> =
        fun el -> d el |> Result.mapError DecodeError.describe

    /// The refusal of a value of the wrong kind: `expected <kind>, got <kind>`.
    let wrongKind (expected: string) (found: JVal) : DecodeError =
        DecodeOver.wrongKind carrier expected found

    /// The refusal of an absent required member, its path naming the member: `missing property: <name>`.
    let missing (name: string) : DecodeError = DecodeOver.missing name

    /// The member `name` of an object — the FIRST, where a foreign document repeats a key — or
    /// `None` where it has none or `el` is not an object.
    let tryMember (name: string) (el: JVal) : JVal option = DecodeOver.tryMember carrier name el

    // ---- the six kinds ----

    /// Any value, verbatim.
    let json: Decoder<JVal> = Ok

    /// A `JStr`; any other kind is `WrongKind` — no number or bool is turned into text.
    let str: Decoder<string> = fun el -> DecodeOver.text carrier el

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
    let items: Decoder<JVal list> = fun el -> DecodeOver.items carrier el

    /// An object's members, in authored order, undecoded.
    let obj: Decoder<(string * JVal) list> = fun el -> DecodeOver.members carrier el

    // ---- members ----

    /// The member `name`, decoded with `d`; absent is `MissingField`, a non-object `WrongKind`.
    let field (name: string) (d: Decoder<'T>) : Decoder<'T> =
        fun el -> DecodeOver.field carrier name d el

    /// The member `name` if present: absent is `Ok None`, present-and-refused is the refusal.
    let optField (name: string) (d: Decoder<'T>) : Decoder<'T option> =
        fun el -> DecodeOver.optField carrier name d el

    /// The member `name`, or `fallback` where it is ABSENT; present-and-refused is the refusal.
    let fieldOr (name: string) (fallback: 'T) (d: Decoder<'T>) : Decoder<'T> =
        optField name d |> map (Option.defaultValue fallback)

    /// Decode the value at `path` below the one handed in. A step that names nothing is refused
    /// where it fails: an absent member as `MissingField`, an item past an array's end as
    /// `OutOfRange` at the array, a step into the wrong kind as `WrongKind` at that value.
    let at (path: PathSegment list) (d: Decoder<'T>) : Decoder<'T> =
        fun el -> DecodeOver.at carrier path d el

    // ---- arrays ----

    /// Every item of an array, each with the decoder `f` builds from its index; the first refusal,
    /// under its item's index.
    let mapListIndexed (f: int -> Decoder<'T>) : Decoder<'T list> =
        fun el -> DecodeOver.mapListIndexed carrier f el

    /// Every item of an array decoded with `d`; the first refusal, under its item's index.
    let list (d: Decoder<'T>) : Decoder<'T list> = mapListIndexed (fun _ -> d)

    /// An array of strings (Phase 509) — `list str`: a non-array is `WrongKind`, and so is an item
    /// that is not a string, under its index.
    let strings: Decoder<string list> = list str

    /// `list` with an item bound: more than `maxItems` items is `LimitExceeded` at the array,
    /// before any item is read.
    let boundedList (maxItems: int) (d: Decoder<'T>) : Decoder<'T list> =
        fun el -> DecodeOver.boundedList carrier maxItems d el

    /// Every item of an array decoded with `d`, answering EVERY refusal rather than the first.
    let listAll (d: Decoder<'T>) (el: JVal) : Result<'T list, DecodeError list> = DecodeOver.listAll carrier d el

    // ---- several decoders over one value ----

    /// Each decoder over the same value, in order; the first refusal.
    let sequence (ds: Decoder<'T> list) : Decoder<'T list> = fun el -> DecodeOver.sequence ds el

    /// Each decoder over the same value, answering EVERY refusal — `sequence`'s accumulating twin,
    /// for a reader that reports all of a document's independent faults at once.
    let all (ds: Decoder<'T> list) (el: JVal) : Result<'T list, DecodeError list> = DecodeOver.all ds el

    // ---- tags and enumerations ----

    /// A string from a closed set: a miss is `UnknownTag`, naming every known spelling.
    let oneOf (cases: (string * 'T) list) : Decoder<'T> =
        fun el -> DecodeOver.oneOf carrier cases el

    /// Dispatch on the string under the discriminator `key`: the case's decoder reads the SAME
    /// object. An absent or non-string discriminator is refused as such; an unknown tag is
    /// `UnknownTag` at the discriminator, naming every known tag.
    let tagDispatch (key: string) (cases: (string * Decoder<'T>) list) : Decoder<'T> =
        fun el -> DecodeOver.tagDispatch carrier key cases el

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
                        (string i)
                )
            | Error e -> Error e

    // ---- the strict member policy ----

    /// Every member of an object outside `known`, in authored order — one `UndeclaredMember` each.
    /// A non-object has no members to refuse.
    let undeclared (known: string list) (el: JVal) : DecodeError list = DecodeOver.undeclared carrier known el

    /// The strict policy (Phase 251's `ReadPolicy.Strict`, generalised): the first member outside
    /// `known` is `UndeclaredMember`, naming the members that WOULD be read. A non-object passes —
    /// the decoder that reads it refuses its kind.
    let members (known: string list) : Decoder<unit> =
        fun el -> DecodeOver.membersOnly carrier known el

    /// `d` under the strict policy: the members check first, then the read.
    let closed (known: string list) (d: Decoder<'T>) : Decoder<'T> = members known |> bind (fun () -> d)

    // ---- text ----

    /// Parse JSON text under `options` (Phase 519: `Json.read`, its refusal through
    /// `DecodeError.ofJsonRefusal`): malformed text is `InvalidJson` at the root, and a passed
    /// bound — the nesting cap included — `LimitExceeded` at the breach's path, naming the limit and
    /// its declared value. `ReadOptions.strict` is the parser's own cap and no other bound; a wire
    /// host enforces its own specification's limits by passing them here.
    let parse (options: ReadOptions) (json: string) : Result<JVal, DecodeError> =
        Json.read options json |> Result.mapError DecodeError.ofJsonRefusal

/// A cursor over one document, handed to the reading function `Reader.read` runs (Phase 368; out of
/// `Json` at Phase 519, beside the refusal it answers). Each `Reader` function reads exactly ONE
/// value where one is due; reading none, or two, where one is due is a programming error and raises
/// `InvalidOperationException`.
[<Sealed>]
type Reader internal (scan: Json.Scanner) =
    let mutable due = true
    let mutable depth = 0
    // The positions of the tokens the reading read on its own rule — the `null` tokens
    // (`Reader.readNull`, Phase 467) and the numbers read as text (`Reader.numberText`, Phase 640) —
    // in reading order: what the re-scan after a shape mismatch admits.
    let admitted = ResizeArray<int>()
    member internal _.Scan = scan
    member internal _.Admitted = admitted

    member internal _.Due
        with get () = due
        and set (v: bool) = due <- v

    member internal _.Depth
        with get () = depth
        and set (v: int) = depth <- v

/// A reader for a large document whose consumer wants typed values, not the `JVal` tree (Phase
/// 368). `Json.read` builds a `JVal` for every value and a list cell for every element, and a
/// consumer then walks the tree a second time to type it; for a large homogeneous array that is most
/// of the decode's allocation (DECISIONS.md D121). A reading here takes such an array straight into
/// a `string[]` / `float[]` / `int[]`, and takes any other value as the `JVal` `Json.read` would
/// have built for it.
///
/// It is the SAME grammar as `Json.read`, not a second one: every token, structure, bound and
/// end-of-input rule is the scanner `Json.read` runs, under the same `ReadOptions`. The refusals
/// agree exactly (Phase 519): a document `Json.read` refuses is refused with
/// `DecodeError.ofJsonRefusal` of that refusal, whatever the reading asked for, and a document it
/// accepts is refused only with `WrongKind` AT THE PATH of the value that is not the shape asked
/// for. A reading that succeeds returns what typing `Json.read`'s tree the same way would.
///
/// Since Phase 467 it also has a TOKEN surface — `peek`, `path` and `readNull` — through which a
/// reading meets a `null` token where it stands and answers it by its own rule, the one thing a
/// `NullPolicy` cannot say; a host whose value type carries a null reads its whole tree through
/// the surface and `Json.read`'s grammar, under its own limits, with no parser of its own. Since
/// Phase 640 the surface reads a number as its token text too (`numberText`), so a host whose
/// numbers keep their digits and scale reads them through the same grammar; the lossless document
/// model (`JDoc.reading`) is that reading.
[<RequireQualifiedAccess>]
module Reader =
    /// Internal signal for a shape mismatch: the path to the value, the shape expected there, and
    /// the value's position. Never escapes `read`.
    exception private ReadMismatch of PathSegment list * string * int

    let private mismatch (s: Json.Scanner) (at: int) (expected: string) : 'a =
        raise (ReadMismatch(s.Path, expected, at))

    let private take (r: Reader) =
        if not r.Due then
            invalidOp "Reader: a value was read where none was due (a reading reads exactly one value)"

        r.Due <- false

    // A container where one is due: the value's start, which must be `opening`, else a mismatch at
    // that value (a character no value starts with is the grammar's to refuse: the mismatch defers
    // to `Json.read`, which names it).
    let private opening (r: Reader) (c: char) (expected: string) : Json.Scanner =
        take r
        let s = r.Scan

        if s.ValueStart() <> c then
            mismatch s s.Pos expected

        s

    // Run `f` where exactly one value is due, one level deeper.
    let private nested (r: Reader) (f: unit -> unit) =
        let d = r.Depth
        r.Due <- true
        r.Depth <- d + 1
        f ()

        if r.Due then
            invalidOp "Reader: a member or item was not read (a reading reads exactly one value)"

        r.Depth <- d

    // A homogeneous array, each element taken by `element` at its value start.
    let private arrayOf (r: Reader) (element: Json.Scanner -> unit) =
        let s = opening r '[' "an array"
        s.Array(r.Depth, (fun () -> element s))

    /// Any value, as `Json.read` builds it.
    let value (r: Reader) : JVal =
        take r
        r.Scan.Value r.Depth

    // ---- the token surface (Phase 467) ----
    //
    // Three functions, and a host reads a document with position-dependent null rules through
    // them and the readings above alone: `peek` says what kind of value is due, unread; `path` says
    // where it stands; `readNull` reads a `null` token on the host's own rule. A host that meets
    // `JsonToken.Null` chooses, by the path, between `readNull` (admit it here) and a refusal at
    // that path; every other token is read by the readings above, so the scalars, the arrays and
    // the objects are exactly what `Json.read` builds and the grammar stays the parser's own. The
    // read's `NullPolicy` governs the readings that build `JVal` (`value`, the typed arrays) and the
    // erasure `members` performs under `EraseMemberNull`; a token read is the host's decision and
    // consults no policy, which is the whole point of it. The canonical reading — every token read
    // back into the `JVal` `Json.read` builds — is proved to be the parse (`proofs/JsonParse.fst`,
    // `reader_rebuilds_the_parse`).

    /// The kind of the value due, from its first significant character, without reading it. Where
    /// no value is due this is a programming error (`InvalidOperationException`); where no value
    /// can start — the end of input, a character outside the grammar — the grammar's refusal is
    /// raised exactly as `value` would raise it, so a document `Json.read` refuses is refused with
    /// its refusal here too.
    let peek (r: Reader) : JsonToken =
        if not r.Due then
            invalidOp "Reader.peek: no value is due (a reading reads exactly one value)"

        r.Scan.Token()

    /// The decode path to the value due, root first — the member keys and item indices the reader is
    /// inside (Phase 467: the position a host's null rule is chosen by, and the path its refusal
    /// names). Computed on request, so a host that never asks pays nothing for it.
    let path (r: Reader) : PathSegment list = r.Scan.Path

    /// The `null` token where one is due, read on the host's rule: it is consumed and nothing is
    /// built, whatever the read's `NullPolicy`. Any other token is a mismatch at this value's path
    /// (`WrongKind`, expecting `null`), and a near miss (`nul`) the literal's own refusal.
    let readNull (r: Reader) : unit =
        take r
        let s = r.Scan

        if s.ValueStart() <> 'n' then
            mismatch s s.Pos "null"

        r.Admitted.Add s.Pos
        s.Null()

    /// The number token where one is due, as its TEXT (Phase 640): scanned and held to the number
    /// grammar by the scanner `Json.read` runs, under the read's limits, and read as no value — so a
    /// token the `JVal` reading refuses (an integer past 2^53 that is not a double's canonical
    /// layout, a magnitude past the finite double range) is read here as written, digits and scale
    /// intact. Any other token is a mismatch at this value's path (`WrongKind`, expecting a number);
    /// a token outside the grammar (`01`, `1.`) is the grammar's own refusal, as `value` names it.
    let numberText (r: Reader) : string =
        take r
        let s = r.Scan
        let c = s.ValueStart()

        if not (c = '-' || (c >= '0' && c <= '9')) then
            mismatch s s.Pos "a number"

        r.Admitted.Add s.Pos
        s.NumberText()

    /// An array of strings, into a `string[]`.
    let strings (r: Reader) : string[] =
        let acc = ResizeArray<string>()

        arrayOf r (fun s ->
            if s.ValueStart() <> '"' then
                mismatch s s.Pos "a string"

            acc.Add(s.String()))

        acc.ToArray()

    /// An array of numbers — the `JInt` and `JFloat` items of `Json.read`, read as `JVal.asFloat`
    /// reads them — into a `float[]`.
    let floats (r: Reader) : float[] =
        let acc = ResizeArray<float>()

        arrayOf r (fun s ->
            let c = s.ValueStart()

            if not (c = '-' || (c >= '0' && c <= '9')) then
                mismatch s s.Pos "a number"

            acc.Add(s.Number()))

        acc.ToArray()

    /// An array of the integers `Json.read` reads as `JInt` (an integer token within Int32), into an
    /// `int[]`. Any other number is a mismatch, as it would be a `JFloat` in the tree.
    let ints (r: Reader) : int[] =
        let acc = ResizeArray<int>()

        arrayOf r (fun s ->
            let c = s.ValueStart()
            let at = s.Pos

            if not (c = '-' || (c >= '0' && c <= '9')) then
                mismatch s at "an Int32 integer"

            s.Number() |> ignore

            if not s.NumberIsInt then
                mismatch s at "an Int32 integer"

            acc.Add s.NumberInt)

        acc.ToArray()

    /// An array, calling `item` once per element in order; `item` reads the element.
    let items (item: Reader -> unit) (r: Reader) : unit =
        let s = opening r '[' "an array"
        s.Array(r.Depth, (fun () -> nested r (fun () -> item r)))

    /// An object, calling `onMember key` once per member in AUTHORED order; `onMember` reads the
    /// member's value. A repeated member name is refused (`DuplicateMember`, Phase 462) when the
    /// scanner reaches it, as `Json.read` refuses it.
    let members (onMember: string -> Reader -> unit) (r: Reader) : unit =
        let s = opening r '{' "an object"
        s.Object(r.Depth, (fun key -> nested r (fun () -> onMember key r)))

    /// Read `input` under `options` with `reading`, which reads exactly one value (the document's),
    /// then require nothing but whitespace after it — the document `Json.read` reads, read typed.
    /// A refusal is a `DecodeError`: the document's own refusal as `Decoder.parse` reports it, or a
    /// shape mismatch as `WrongKind` at the path of the value, its `Expected` the shape asked for.
    let read (options: ReadOptions) (reading: Reader -> 'a) (input: string) : Result<'a, DecodeError> =
        match Json.documentBytesBreach options.Limits input with
        | Some breach -> Error(DecodeError.ofJsonRefusal breach)
        | None ->
            let s = Json.Scanner(options.Null, options.Limits, input)
            let r = Reader(s)

            try
                let a = reading r

                if r.Due then
                    invalidOp "Reader.read: the reading read no value"

                s.SkipWs()

                if s.Pos <> s.Length then
                    Error(
                        DecodeError.ofJsonRefusal (
                            JsonRefusal.Malformed
                                { Position = s.Pos
                                  Message = "trailing characters"
                                  Kind = TrailingCharacters }
                        )
                    )
                else
                    Ok a
            with
            | ReadMismatch(path, expected, at) ->
                // A refusal anywhere in the document outranks a shape mismatch, so a document
                // `Json.read` refuses is refused with its refusal whatever was asked — less the
                // nulls the reading read on its own rule (`readNull`), which are values to it; a
                // null the reading never reached is answered by the read's policy, as every
                // value past a mismatch is.
                match Json.readAdmitting options (r.Admitted.ToArray()) input with
                | Error refusal -> Error(DecodeError.ofJsonRefusal refusal)
                | Ok _ ->
                    let found = "the value at position " + string at
                    Error(DecodeError.within path (DecodeError.make DecodeCode.WrongKind expected found))
            | e ->
                match s.Refusal e with
                | Some refusal -> Error(DecodeError.ofJsonRefusal refusal)
                | None -> reraise ()
