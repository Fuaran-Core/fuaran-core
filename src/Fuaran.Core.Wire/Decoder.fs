namespace Fuaran.Core

/// A decoder over the typed refusal (Phase 310). `Decode.Decoder` is its string-error twin, kept
/// for one draft.
type Decoder<'T> = JVal -> Result<'T, DecodeError>

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

    /// `tagDispatch`, with an unknown tag refused in the caller's own sentence (Phase 388): the
    /// refusal is the same `UnknownTag` at the discriminator, its expectation still naming every known
    /// tag, and its message `what + <the tag>` — so a codec whose refusals predate this module keeps
    /// spelling them as it always has. Every other refusal is `tagDispatch`'s, unchanged.
    let tagDispatchWith (what: string) (key: string) (cases: (string * Decoder<'T>) list) : Decoder<'T> =
        fun el ->
            tagDispatch key cases el
            |> Result.mapError (fun e ->
                match e.Code, e.Path, tryMember key el with
                | DecodeCode.UnknownTag, [ PathSegment.Key k ], Some(JStr other) when k = key ->
                    { e with Message = what + other }
                | _ -> e)

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
