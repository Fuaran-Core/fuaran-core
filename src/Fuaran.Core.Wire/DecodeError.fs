namespace Fuaran.Core

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
