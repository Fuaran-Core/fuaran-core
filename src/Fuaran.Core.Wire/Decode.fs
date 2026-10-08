namespace Fuaran.Core

/// Total decode combinators over the portable `Json.parse` → `JVal` model. Decode is now
/// **fully portable** — the same combinators run under .NET and Fable (the prior
/// `#if !FABLE_COMPILER` System.Text.Json path is retired, Phase 241). Each combinator
/// returns `Result<_, string>` so a failure *names what was expected* (the same envelope
/// discipline as the op algebra).
module Decode =

    /// A decoder reads a parsed `JVal`. Signature-identical across both pipelines.
    type Decoder<'T> = JVal -> Result<'T, string>

    /// The structural fault a decode combinator meets, BEFORE any codec spells it (Phase 299). The
    /// `…With` combinators below are generic over the error type and take the codec's own spelling
    /// of a `Fault`. A codec with a typed error envelope (the columnar codec's `ColumnError`) reuses
    /// the same traversal and keeps its own codes, rather than carrying a private copy of each
    /// combinator — which is what it did until this phase. (Their `string`-error twins, `getProp`
    /// to `mapList`, left at `1.0.0`: `Decoder` reads the same members with a coded refusal.)
    type Fault =
        /// An object had no member of this name.
        | MissingProperty of name: string
        /// A value was of the wrong JSON kind: the kind expected, and the kind found (`JVal.kindName`).
        | WrongKind of expected: string * got: string

    /// A `Fault` in the words the `string`-error combinators always used (`missing property:
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

    /// Parse a JSON string to a `JVal` root.
    let parse (json: string) : Result<JVal, string> = Json.parse json

    /// Parse a **foreign** JSON string that spells absent members `null` — object-member `null` is
    /// erased to absence, so every decoder (`Decoder.field` → `missing property: <name>`) behaves
    /// exactly as it does against the same document written without the token. The one-word swap a
    /// consumer makes to read a spec-conformant foreign document; everything downstream is unchanged.
    let parseTolerantOfNull (json: string) : Result<JVal, string> = Json.parseTolerantOfNull json
