namespace Fuaran.Core

/// A value's codec (Phase 379): its encoder, its COLLECTING decoder and its schema, built from one
/// declaration by the `Codec` combinators so the three cannot disagree about a member's name,
/// whether it is required, or the kind of value it holds.
///
/// `ReadAll` answers every defect it finds, in the order the generator's collecting decoders use
/// (Phase 377): for an object, its undeclared members first in authored order (the strict reader
/// checks them first too), then its declared members in declaration order, each member's own
/// defects depth-first; a list's items in index order; a leaf, a wrong kind or an unknown
/// discriminator one defect. Its first defect is the one the strict reader `Codec.decoder`
/// reports, and on a clean input the two answer one value. The codes and paths are
/// `DecodeError`'s, the vocabulary every reader on the spine shares.
///
/// Writing is total (Phase 384): `Write` answers the `JVal` a value is written as, or
/// `CodecDeclarationFault.Unrecognised` at the value no `enum` or `union` case recognises. It is the
/// codec's ONE writer — there is no raising twin beside it — and, as the spine's sole
/// result-returning operations are (`ReadAll`, `Codec.read`, `Canonical.read`, the decoders), it is
/// named plainly: a `try` prefix marks the refusing half of a twin pair, and there is no pair.
type Codec<'T> =
    {
        /// The value as the `JVal` it is written as, or the declaration's refusal of a value it does
        /// not cover. Answers `Ok` over every value the declaration covers.
        Write: 'T -> Result<JVal, CodecDeclarationFault>
        /// The value a `JVal` reads as, or every defect found in it.
        ReadAll: JVal -> Result<'T, DecodeError list>
        /// The JSON Schema (2020-12 vocabulary) of what `Write` produces and `ReadAll` accepts:
        /// `type`, `items`, `properties`, `required`, `additionalProperties`, `enum`, `const`,
        /// `oneOf`. Descriptive: the decoder, not the schema, is the authority on acceptance.
        Schema: JVal
    }

/// The members of an object codec still being declared (Phase 379): `'R` is the value written,
/// `'C` what remains of its constructor. Built by `Codec.record` and `Codec.field` /
/// `Codec.optField`, closed by `Codec.build` or used as a union case by `Codec.case`.
type CodecFields<'R, 'C> =
    private
        { WriteFields: 'R -> Result<(string * JVal) list, CodecDeclarationFault>
          ReadFields: (string * JVal) list -> Result<'C, DecodeError list>
          Properties: (string * JVal) list
          Required: string list
          Names: string list }

/// One case of a discriminated codec (Phase 379), built by `Codec.case` and closed by
/// `Codec.union`.
type CodecCase<'T> =
    private
        { Tag: string
          WriteCase: 'T -> Result<(string * JVal) list, CodecDeclarationFault> option
          ReadCase: (string * JVal) list -> Result<'T, DecodeError list>
          CaseProperties: (string * JVal) list
          CaseRequired: string list
          CaseNames: string list }

/// The `Codec<'T>` combinators (Phase 379). Every codec they build writes members in DECLARATION
/// order and reads STRICTLY: an undeclared member is `UndeclaredMember`, an absent required one
/// `MissingField`, never a default. A declaration that cannot be written consistently — a member
/// or a case tag named twice, a discriminator that is also a member — is refused when the codec
/// is BUILT: `enum`, `build` and `union` answer `Error` with every `CodecDeclarationFault` they
/// find (Phase 384), and nothing raises. A value an `enum` or `union` does not cover is refused
/// when it is written, through `Codec<'T>.Write`.
[<RequireQualifiedAccess>]
module Codec =

    let private one (r: Result<'T, DecodeError>) : Result<'T, DecodeError list> = Result.mapError List.singleton r

    let private under (step: PathSegment) (r: Result<'T, DecodeError list>) : Result<'T, DecodeError list> =
        Result.mapError (List.map (DecodeError.under step)) r

    let private both
        (f: Result<'A -> 'B, DecodeError list>)
        (a: Result<'A, DecodeError list>)
        : Result<'B, DecodeError list> =
        match f, a with
        | Ok f, Ok a -> Ok(f a)
        | Error e, Ok _
        | Ok _, Error e -> Error e
        | Error e1, Error e2 -> Error(e1 @ e2)

    let private schemaOf (kind: string) : JVal = JObj [ "type", JStr kind ]

    let private memberOf (name: string) (members: (string * JVal) list) : JVal option =
        members |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

    /// Every name that occurs more than once, each named once, in the order it first occurs.
    let private repeats (names: string list) : string list =
        names |> List.countBy id |> List.filter (fun (_, n) -> n > 1) |> List.map fst

    /// Write each item through `write`, the first refusal stopping the walk.
    let private traverse
        (write: int -> 'A -> Result<'B, CodecDeclarationFault>)
        (xs: 'A list)
        : Result<'B list, CodecDeclarationFault> =
        let rec go i acc xs =
            match xs with
            | [] -> Ok(List.rev acc)
            | x :: rest ->
                match write i x with
                | Ok y -> go (i + 1) (y :: acc) rest
                | Error f -> Error f

        go 0 [] xs

    let private strictObject
        (names: string list)
        (j: JVal)
        (read: (string * JVal) list -> Result<'T, DecodeError list>)
        : Result<'T, DecodeError list> =
        match j with
        | JObj members ->
            match Decoder.undeclared names j, read members with
            | [], r -> r
            | u, Ok _ -> Error u
            | u, Error e -> Error(u @ e)
        | other -> Error [ Decoder.wrongKind "object" other ]

    let private objectSchema
        (extra: (string * JVal) list)
        (properties: (string * JVal) list)
        (required: string list)
        : JVal =
        JObj
            [ "type", JStr "object"
              "properties", JObj(extra @ properties)
              "required", JArr(required |> List.map JStr)
              "additionalProperties", JBool false ]

    /// A codec from its three faces, for a type the combinators do not reach. The caller owns their
    /// agreement; everything built from the combinators below has it by construction.
    let make (write: 'T -> JVal) (readAll: JVal -> Result<'T, DecodeError list>) (schema: JVal) : Codec<'T> =
        { Write = write >> Ok
          ReadAll = readAll
          Schema = schema }

    /// A codec over a short-circuiting `Decoder<'T>`, whose one refusal is its one defect.
    let ofDecoder (write: 'T -> JVal) (read: Decoder<'T>) (schema: JVal) : Codec<'T> = make write (read >> one) schema

    /// The strict reader: the codec's first defect, or its value — `Decoder`'s shape, for a caller
    /// on the short-circuiting layer.
    let decoder (c: Codec<'T>) : Decoder<'T> =
        fun j ->
            match c.ReadAll j with
            | Ok v -> Ok v
            | Error(e :: _) -> Error e
            | Error [] ->
                Error(
                    DecodeError.make
                        DecodeCode.SchemaFault
                        "a refusal"
                        "a collecting decoder answered no defect and no value"
                )

    // ---- leaves ----

    /// Any value, verbatim; its schema admits everything.
    let json: Codec<JVal> = make id Ok (JObj [])

    /// A string (`Decoder.str`).
    let string: Codec<string> = ofDecoder JStr Decoder.str (schemaOf "string")

    /// An `int` (`Decoder.int`, the strict integer read).
    let int: Codec<int> = ofDecoder JInt Decoder.int (schemaOf "integer")

    /// A `float` written as `JFloat` and read from either number constructor (`Decoder.float`). A
    /// whole-valued float writes as an integer token, per the wire's one number population.
    let float: Codec<float> = ofDecoder JFloat Decoder.float (schemaOf "number")

    /// A `bool` (`Decoder.bool`).
    let bool: Codec<bool> = ofDecoder JBool Decoder.bool (schemaOf "boolean")

    /// A closed set of values spelled as strings: written by the spelling of the FIRST case equal
    /// to the value, read by `Decoder.oneOf` (an unknown spelling is `UnknownTag`). Refused when
    /// built with a `RepeatedSpelling` per spelling that repeats; a value equal to no case is
    /// refused when written, as `Unrecognised` at the root.
    let enum (cases: (string * 'T) list) : Result<Codec<'T>, CodecDeclarationFault list> =
        match repeats (List.map fst cases) with
        | _ :: _ as repeated -> Error(repeated |> List.map CodecDeclarationFault.RepeatedSpelling)
        | [] ->
            let tryWrite (v: 'T) =
                match cases |> List.tryFind (fun (_, x) -> x = v) with
                | Some(s, _) -> Ok(JStr s)
                | None -> Error(CodecDeclarationFault.Unrecognised [])

            Ok
                { Write = tryWrite
                  ReadAll = Decoder.oneOf cases >> one
                  Schema = JObj [ "enum", JArr(cases |> List.map (fst >> JStr)) ] }

    // ---- composition ----

    /// A list, every item through `c`; every item's defects are reported, at its index.
    let list (c: Codec<'T>) : Codec<'T list> =
        let readAll (j: JVal) =
            match j with
            | JArr xs ->
                let results =
                    xs |> List.mapi (fun i x -> c.ReadAll x |> under (PathSegment.Index i))

                let errors =
                    results
                    |> List.collect (fun r ->
                        match r with
                        | Ok _ -> []
                        | Error es -> es)

                if List.isEmpty errors then
                    Ok(
                        results
                        |> List.choose (fun r ->
                            match r with
                            | Ok v -> Some v
                            | Error _ -> None)
                    )
                else
                    Error errors
            | other -> Error [ Decoder.wrongKind "array" other ]

        { Write =
            traverse (fun i x -> c.Write x |> Result.mapError (CodecDeclarationFault.under (PathSegment.Index i)))
            >> Result.map JArr
          ReadAll = readAll
          Schema = JObj [ "type", JStr "array"; "items", c.Schema ] }

    /// The codec of a type isomorphic to `'T`: `there` after reading, `back` before writing.
    let map (there: 'T -> 'U) (back: 'U -> 'T) (c: Codec<'T>) : Codec<'U> =
        { Write = back >> c.Write
          ReadAll = c.ReadAll >> Result.map there
          Schema = c.Schema }

    /// The codec of a REFINEMENT of `'T`: `check` admits a read value or refuses it with a
    /// sentence, reported as `OutOfRange` with `expected` as what the position admits. `back` is
    /// total — every refined value has a representation. The schema is the base codec's.
    let refine (expected: string) (check: 'T -> Result<'U, string>) (back: 'U -> 'T) (c: Codec<'T>) : Codec<'U> =
        let readAll (j: JVal) =
            c.ReadAll j
            |> Result.bind (fun v ->
                match check v with
                | Ok u -> Ok u
                | Error why -> Error [ DecodeError.make DecodeCode.OutOfRange expected why ])

        { Write = back >> c.Write
          ReadAll = readAll
          Schema = c.Schema }

    // ---- objects ----

    /// Begin an object declaration with its constructor, curried over the members in the order
    /// they will be declared: `Codec.record (fun a b -> { A = a; B = b })`.
    let record (ctor: 'C) : CodecFields<'R, 'C> =
        { WriteFields = fun _ -> Ok []
          ReadFields = fun _ -> Ok ctor
          Properties = []
          Required = []
          Names = [] }

    /// Declare a REQUIRED member: written from `get`, read through `c`; absent is `MissingField`.
    let field (name: string) (get: 'R -> 'A) (c: Codec<'A>) (fs: CodecFields<'R, 'A -> 'C>) : CodecFields<'R, 'C> =
        { WriteFields =
            fun r ->
                fs.WriteFields r
                |> Result.bind (fun before ->
                    c.Write(get r)
                    |> Result.mapError (CodecDeclarationFault.under (PathSegment.Key name))
                    |> Result.map (fun j -> before @ [ name, j ]))
          ReadFields =
            fun members ->
                let this =
                    match memberOf name members with
                    | Some v -> c.ReadAll v |> under (PathSegment.Key name)
                    | None -> Error [ Decoder.missing name ]

                both (fs.ReadFields members) this
          Properties = fs.Properties @ [ name, c.Schema ]
          Required = fs.Required @ [ name ]
          Names = fs.Names @ [ name ] }

    /// Declare an OPTIONAL member: written only when `get` answers `Some`, read as `None` when
    /// absent. The wire has no null, so absence is the only spelling of `None`.
    let optField
        (name: string)
        (get: 'R -> 'A option)
        (c: Codec<'A>)
        (fs: CodecFields<'R, 'A option -> 'C>)
        : CodecFields<'R, 'C> =
        { WriteFields =
            fun r ->
                match get r with
                | Some a ->
                    fs.WriteFields r
                    |> Result.bind (fun before ->
                        c.Write a
                        |> Result.mapError (CodecDeclarationFault.under (PathSegment.Key name))
                        |> Result.map (fun j -> before @ [ name, j ]))
                | None -> fs.WriteFields r
          ReadFields =
            fun members ->
                let this =
                    match memberOf name members with
                    | Some v -> c.ReadAll v |> under (PathSegment.Key name) |> Result.map Some
                    | None -> Ok None

                both (fs.ReadFields members) this
          Properties = fs.Properties @ [ name, c.Schema ]
          Required = fs.Required
          Names = fs.Names @ [ name ] }

    /// Close an object declaration: every member declared, the constructor fully applied. Read
    /// strictly — the undeclared members are reported first, then each declared member's defects.
    /// Refused when built with a `RepeatedMember` per member name that repeats.
    let build (fs: CodecFields<'R, 'R>) : Result<Codec<'R>, CodecDeclarationFault list> =
        match repeats fs.Names with
        | _ :: _ as repeated -> Error(repeated |> List.map (fun n -> CodecDeclarationFault.RepeatedMember(n, None)))
        | [] ->
            Ok
                { Write = fs.WriteFields >> Result.map JObj
                  ReadAll = fun j -> strictObject fs.Names j fs.ReadFields
                  Schema = objectSchema [] fs.Properties fs.Required }

    // ---- discriminated unions ----

    /// One case of a union: the values `project` recognises, written as the members `fs` declares
    /// under the discriminator `tag`, read back through `inject`. Total: its members are checked
    /// by the `union` that closes it, which refuses a member name repeated in the case.
    let case (tag: string) (project: 'T -> 'P option) (inject: 'P -> 'T) (fs: CodecFields<'P, 'P>) : CodecCase<'T> =
        { Tag = tag
          WriteCase = fun v -> project v |> Option.map fs.WriteFields
          ReadCase = fun members -> fs.ReadFields members |> Result.map inject
          CaseProperties = fs.Properties
          CaseRequired = fs.Required
          CaseNames = fs.Names }

    /// A discriminated union under the member `key`: written as the first case whose `project`
    /// recognises the value, its tag first; read by `Decoder.tagDispatch`'s rule (an absent or
    /// non-string discriminator, or an unknown tag, is the one defect), then the case's members
    /// strictly. Refused when built with every fault in declaration order: a `RepeatedMember` per
    /// member a case repeats, a `RepeatedTag` per tag two cases share, a `DiscriminatorAsMember`
    /// per case declaring a member named `key`. A value no case recognises is refused when
    /// written, as `Unrecognised` at that value — which an exhaustive declaration never reaches.
    let union (key: string) (cases: CodecCase<'T> list) : Result<Codec<'T>, CodecDeclarationFault list> =
        let faults =
            [ for c in cases do
                  for n in repeats c.CaseNames do
                      CodecDeclarationFault.RepeatedMember(n, Some c.Tag)
              for t in repeats (cases |> List.map _.Tag) do
                  CodecDeclarationFault.RepeatedTag t
              for c in cases do
                  if List.contains key c.CaseNames then
                      CodecDeclarationFault.DiscriminatorAsMember(c.Tag, key) ]

        match faults with
        | _ :: _ -> Error faults
        | [] ->
            let tryWrite (v: 'T) =
                let written =
                    cases
                    |> List.tryPick (fun c ->
                        c.WriteCase v
                        |> Option.map (Result.map (fun fields -> JObj((key, JStr c.Tag) :: fields))))

                match written with
                | Some r -> r
                | None -> Error(CodecDeclarationFault.Unrecognised [])

            let dispatch: Decoder<CodecCase<'T>> =
                Decoder.tagDispatch key [ for c in cases -> c.Tag, (fun _ -> Ok c) ]

            let readAll (j: JVal) =
                match dispatch j with
                | Error e -> Error [ e ]
                | Ok c -> strictObject (key :: c.CaseNames) j c.ReadCase

            let schema =
                JObj
                    [ "oneOf",
                      JArr
                          [ for c in cases ->
                                objectSchema
                                    [ key, JObj [ "const", JStr c.Tag ] ]
                                    c.CaseProperties
                                    (key :: c.CaseRequired) ] ]

            Ok
                { Write = tryWrite
                  ReadAll = readAll
                  Schema = schema }

    // ---- text ----

    /// The canonical text of `v` under `profile` — `c.Write v |> Result.map (Canonical.write profile)` —
    /// or the declaration's refusal of a value it does not cover (Phase 384: it answered a string
    /// and raised there). Unguarded as `Canonical.write` is: a non-finite float or an ill-formed
    /// string renders; `Canonical.tryWrite` over `c.Write v` is the form whose output may be hashed.
    let write (profile: EncodingProfile) (c: Codec<'T>) (v: 'T) : Result<string, CodecDeclarationFault> =
        c.Write v |> Result.map (Canonical.write profile)

    /// The value JSON `text` reads as, or every defect: a parse refusal is the one defect, at the
    /// root. Either escaping spelling reads.
    let read (c: Codec<'T>) (text: string) : Result<'T, DecodeError list> =
        match Canonical.read text with
        | Error e -> Error [ e ]
        | Ok j -> c.ReadAll j

    /// The codec as the conformance corpus's `Corpus.Codec`, writing canonical text under
    /// `profile` and reading with the strict reader's sentence — so a declaration is held to the
    /// round-trip and reject laws `Corpus` already runs. `Corpus.Codec`'s encoder is total over
    /// text, so a value the declaration refuses to write is encoded as the refusal's sentence under
    /// a fixed prefix — text that is not JSON — and the bridge's decoder answers that sentence back
    /// as its refusal, so the round-trip law goes red NAMING the write refusal rather than the bridge
    /// raising or reporting only that the text did not parse.
    let corpus (profile: EncodingProfile) (c: Codec<'T>) : Corpus.Codec<'T> =
        let refused = "the codec refused to write the value: "

        { Encode =
            fun v ->
                match write profile c v with
                | Ok text -> text
                | Error f -> refused + CodecDeclarationFault.describe f
          Decode =
            fun text ->
                if text.StartsWith(refused, System.StringComparison.Ordinal) then
                    Error text
                else
                    match read c text with
                    | Ok v -> Ok v
                    | Error(e :: _) -> Error(DecodeError.render e)
                    | Error [] -> Error "a collecting decoder answered no defect and no value" }
