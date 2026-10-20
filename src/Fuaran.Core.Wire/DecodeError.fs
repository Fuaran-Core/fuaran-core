namespace Fuaran.Core

/// The closed code set a decode refusal carries (Phase 310) — the wire-level decode contract every
/// host mirrors (DECISIONS.md D99). The code says what KIND of fault a refusal is, so a caller, or a
/// model repairing its own emission, branches on it rather than on the sentence beside it. Closed:
/// a host adds no code of its own, and a refinement a host draws finer (the UI host tells an
/// unknown node kind from an unknown case) is an instance of one of these.
[<RequireQualifiedAccess>]
type DecodeCode =
    /// The input is not JSON text the reader can parse (malformed text; a passed bound, the nesting
    /// cap included, is `LimitExceeded`). Its path is the root.
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

/// The codes a refusal raised AT the value being decoded can carry (Phase 443): every `DecodeCode`
/// but `InvalidJson` and `MissingField`, in declaration order. A refusal at the value has the empty
/// path, and those two cannot stand there: a `MissingField` names its absent member by its path's
/// last step, so at the empty path it names nothing and resolves in no document; an `InvalidJson` is
/// the parser's code, which names only the root and stops resolving the moment a combinator carries
/// it one step down. `Decoder.fail` takes this type, and so does the refusal `Decoder.andThen`'s
/// function answers (Phase 453), so neither can raise either: an absent member is `Decoder.field`'s
/// refusal, and unparseable text the parse entry points'.
[<RequireQualifiedAccess>]
type RootDecodeCode =
    /// `DecodeCode.WrongKind`.
    | WrongKind
    /// `DecodeCode.UnknownTag`.
    | UnknownTag
    /// `DecodeCode.OutOfRange`.
    | OutOfRange
    /// `DecodeCode.UndeclaredMember`.
    | UndeclaredMember
    /// `DecodeCode.LimitExceeded`.
    | LimitExceeded
    /// `DecodeCode.NotAdmitted`.
    | NotAdmitted
    /// `DecodeCode.SchemaFault`.
    | SchemaFault

/// A decode refusal (Phase 310): its code, the path to the value at fault (see `DecodeCode` for
/// what a `MissingField` path names), what the position expected, and a sentence. Since Phase 520
/// the sentence is a PROJECTION (DECISIONS.md D99, amended): `DecodeError.sentence` writes it from
/// the code, the expectation and the detail, one form per code, and nothing re-spells it. A host
/// certifies on `Code` and `Path`; the sentence is for a reader and may be reworded between drafts.
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
        /// The sentence — `DecodeError.sentence` of the code, the expectation and the detail the
        /// refusal was built with, and what the string-error forms return. It carries no path;
        /// `DecodeError.render` adds one. Never matched on: the code and the path are the contract.
        Message: string
    }

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

    /// The ONE sentence of a refusal (Phase 520; D99 as amended): a projection of its code, what
    /// its position expected, and `detail` — what was found there (a kind name, a member name, the
    /// offending value), never a sentence of the caller's own. One form per code:
    ///
    /// - `InvalidJson`: `not valid JSON: <detail>` (the parser's account and its position);
    /// - `MissingField`: `missing property: <detail>` (the member's name);
    /// - `WrongKind`: `expected <expected>, got <detail>` (the kind found);
    /// - `UnknownTag`: `unknown <detail>; expected <expected>`;
    /// - every other code: `<code phrase>: <detail>; expected <expected>`, the detail and its colon
    ///   left out where it is empty.
    ///
    /// The path is not in it — `render` adds the path — so a refusal carried one step outward keeps
    /// its sentence.
    let sentence (code: DecodeCode) (expected: string) (detail: string) : string =
        let led (phrase: string) =
            (if detail = "" then phrase else phrase + ": " + detail)
            + "; expected "
            + expected

        match code with
        | DecodeCode.InvalidJson -> "not valid JSON: " + detail
        | DecodeCode.MissingField -> "missing property: " + detail
        | DecodeCode.WrongKind -> "expected " + expected + ", got " + detail
        | DecodeCode.UnknownTag -> "unknown " + detail + "; expected " + expected
        | DecodeCode.OutOfRange -> led "out of range"
        | DecodeCode.UndeclaredMember -> led "undeclared member"
        | DecodeCode.LimitExceeded -> led "limit exceeded"
        | DecodeCode.NotAdmitted -> led "not admitted"
        | DecodeCode.SchemaFault -> led "schema fault"

    /// A refusal at the value being decoded (an empty path — a combinator that hands the refusal
    /// out prefixes its own step): its code, what the position expected, and the detail its
    /// sentence is projected from (`sentence`).
    let make (code: DecodeCode) (expected: string) (detail: string) : DecodeError =
        { Code = code
          Path = []
          Expected = expected
          Message = sentence code expected detail }

    /// The same refusal one step further from the root — what a combinator does to a refusal
    /// leaving a member or an item.
    let under (step: PathSegment) (e: DecodeError) : DecodeError = { e with Path = step :: e.Path }

    /// The same refusal under a whole path prefix.
    let within (prefix: PathSegment list) (e: DecodeError) : DecodeError = { e with Path = prefix @ e.Path }

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

    /// `toJson`'s form read back (Phase 551; it was a private reader of the query seam's, which journals a
    /// refusal inside `QueryError.UnreadableArgs`): `code` one of `codes` by `codeName`, `path` a decode
    /// path (`DecodePath.ofJson`), `expected` and `message` strings, read in that order. The stored
    /// `message` is read back VERBATIM, never re-projected from the code (D188), so a refusal journalled
    /// before a sentence moved replays as it was written. Each refusal is the one `Decoder.field`,
    /// `Decoder.oneOf` and `Decoder.str` give, written here because this file compiles before `Decoder`.
    let decoder (el: JVal) : Result<DecodeError, DecodeError> =
        let missing (name: string) =
            under (PathSegment.Key name) (make DecodeCode.MissingField ("a member '" + name + "'") name)

        let wrongKind (expected: string) (found: JVal) =
            make DecodeCode.WrongKind expected (JVal.kindName found)

        let field (name: string) (read: JVal -> Result<'T, DecodeError>) : Result<'T, DecodeError> =
            match el with
            | JObj fields ->
                match fields |> List.tryFind (fun (k, _) -> k = name) with
                | Some(_, v) -> read v |> Result.mapError (under (PathSegment.Key name))
                | None -> Error(missing name)
            | other -> Error(wrongKind "object" other)

        let str (v: JVal) =
            match v with
            | JStr s -> Ok s
            | other -> Error(wrongKind "string" other)

        let code (v: JVal) =
            match v with
            | JStr s ->
                match codes |> List.tryFind (fun c -> codeName c = s) with
                | Some c -> Ok c
                | None ->
                    let known =
                        codes |> List.map (fun c -> "'" + codeName c + "'") |> String.concat ", "

                    Error(make DecodeCode.UnknownTag ("one of " + known) ("value '" + s + "'"))
            | other -> Error(wrongKind "string" other)

        let path (v: JVal) =
            match DecodePath.ofJson v with
            | Some p -> Ok p
            | None -> Error(make DecodeCode.WrongKind "an array of member names and item indices" "not a decode path")

        field "code" code
        |> Result.bind (fun c ->
            field "path" path
            |> Result.bind (fun p ->
                field "expected" str
                |> Result.bind (fun expected ->
                    field "message" str
                    |> Result.map (fun message ->
                        { Code = c
                          Path = p
                          Expected = expected
                          Message = message }))))

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

    /// A reader's refusal as a decode refusal (Phase 465; the one projection since Phase 519):
    /// malformed text is `InvalidJson` at the root, in the sentence `Json.parse` returns, and a
    /// breach is `LimitExceeded` AT THE BREACH'S PATH, naming the limit and its declared value and
    /// never the input — the depth cap's breach included, on every reader — so a host maps the one
    /// code to its own envelope and never reports a breach as `InvalidJson`.
    let ofJsonRefusal (r: JsonRefusal) : DecodeError =
        match r with
        | JsonRefusal.Malformed e ->
            make DecodeCode.InvalidJson "JSON text" (e.Message + " at position " + string e.Position)
        | JsonRefusal.LimitExceeded b ->
            let name = JsonLimit.name b.Limit

            within
                b.Path
                (make
                    DecodeCode.LimitExceeded
                    ("within the " + name + " bound of " + string b.Declared)
                    ("the " + name + " bound"))
