namespace Fuaran.Core

/// The value domain a hole ranges over (value-space projection is the type system
/// for holes). `AnyString` and `SlotTree` are the *unbounded* spaces; only a capped `IntRange` is a
/// count space a repeat may range over (`Space.isCount`, Phase 307).
///
/// `SlotTree` (Phase 229) is the value space of a tree-typed slot at the scalar invocation seam:
/// a wire document — a `"kind"`-tagged JSON object, carried as the argument string — whose kind
/// satisfies the slot's constraint when one is declared (any kind otherwise). Core owns no node
/// type, so the space is stated over the WIRE and checked by shape; decoding the document into the
/// domain's node is the host's, per the witness pattern. `Function.signature` enters every
/// `SlotHole` with this space, which is what makes a capability over a slotted artifact invocable.
type ValueSpace =
    /// An integer between `lo` and `hi`, both inclusive, written with no `+`, white space or fraction.
    | IntRange of lo: int * hi: int
    /// A finite decimal number between `lo` and `hi`, both inclusive; an `IntRange` its bounds
    /// contain widens into it.
    | FloatRange of lo: float * hi: float
    /// A string whose length (in UTF-16 code units) is between `lo` and `hi`, both inclusive.
    | StringLen of lo: int * hi: int
    /// Exactly one of the listed members, compared ordinally; an empty list admits no value.
    | Enum of string list
    /// Any string: the top of the scalar spaces, and unbounded, so no repeat may count over it.
    | AnyString
    /// A `"kind"`-tagged wire document, of the constrained kind when one is given; unbounded, and
    /// admitted by no scalar space.
    | SlotTree of kindConstraint: string option

/// Why a value space is not well-formed (Phase 307) — the two ways a declared space can fail to
/// mean anything an argument could satisfy or a codec could write back.
[<RequireQualifiedAccess>]
type SpaceFault =
    /// The space admits no value: an `IntRange`, `FloatRange` or `StringLen` whose `lo` exceeds its
    /// `hi` (a `StringLen` whose `hi` is negative), or an `Enum` with no member.
    | Empty
    /// A `FloatRange` bound is NaN or infinite: no JSON number spells it, so no codec can write the
    /// declaration back, and an infinite bound makes the space unbounded in fact.
    | NonFinite

/// The value-space operations: the seam's culture-invariant readers, the one canonical spelling of
/// a value, membership, the sub-space relation, well-formedness, and the space in words for a
/// refusal.
module Space =

    /// The kind tag of a tree argument (Phase 229): `Some kind` when the string is a well-formed
    /// wire document whose top level is a `"kind"`-tagged object, `None` otherwise — a scalar, a
    /// malformed document, or an object with no string `"kind"`. The one reader `SlotTree` reaches
    /// for; it decodes nothing below the tag.
    let slotKindOf (s: string) : string option =
        match Json.parse s with
        | Ok(JObj _ as el) ->
            match Decoder.field "kind" Decoder.str el with
            | Ok k -> Some k
            | Error _ -> None
        | _ -> None

    /// An integer argument as the seam reads it (Phase 295): an optional `-` and decimal digits,
    /// read under the INVARIANT culture (`Json.readInt32`) — no white space, no `+`, no culture's own
    /// minus sign. Leading zeros are read (`05` is 5); the canonical spelling is what `canonical`
    /// hands on. Until Phase 295 the read was `Int32.TryParse` under the CURRENT culture, which
    /// admitted `" 5"` and `"+5"` and keyed each spelling apart.
    let internal readInt (s: string) : int option =
        if s.Length > 0 && s.[0] = '+' then
            None
        else
            Json.readInt32 s

    let private isDigit (c: char) = c >= '0' && c <= '9'

    /// A number argument as the seam reads it (Phase 295): `-?digits(.digits)?([eE][+-]?digits)?`,
    /// read under the invariant culture — no white space, no `+` before the number, no `.5`, `1.`,
    /// `Infinity` or `NaN` (the float reader under `NumberStyles.Float` took all of those).
    let internal readFloat (s: string) : float option =
        let n = s.Length
        let mutable i = if n > 0 && s.[0] = '-' then 1 else 0
        let digitsFrom = i

        while i < n && isDigit s.[i] do
            i <- i + 1

        let mutable shaped = i > digitsFrom

        if shaped && i < n && s.[i] = '.' then
            i <- i + 1
            let fracFrom = i

            while i < n && isDigit s.[i] do
                i <- i + 1

            shaped <- i > fracFrom

        if shaped && i < n && (s.[i] = 'e' || s.[i] = 'E') then
            i <- i + 1

            if i < n && (s.[i] = '+' || s.[i] = '-') then
                i <- i + 1

            let expFrom = i

            while i < n && isDigit s.[i] do
                i <- i + 1

            shaped <- i > expFrom

        if not shaped || i <> n then
            None
        else
            match
                System.Double.TryParse(
                    s,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            with
            | true, v when not (System.Double.IsInfinity v) -> Some v
            | _ -> None

    /// The ONE spelling a value in the space is handed on as (Phase 295), or `None` where the
    /// string is not in the space. An integer is written in its shortest decimal form (`05` and `5`
    /// are both `5`), a number in the canonical float layout (`Canon.canonicalFloat`: `1.50` is
    /// `1.5`); every other space's value is its own spelling. So one value has one spelling, and
    /// `Capability.invocationKey` — which keys an argument by this spelling where it lies in its
    /// hole's space — gives one value one capture key.
    let canonical (space: ValueSpace) (s: string) : string option =
        match space with
        | IntRange(lo, hi) ->
            match readInt s with
            | Some v when v >= lo && v <= hi -> Some(string v)
            | _ -> None
        | FloatRange(lo, hi) ->
            match readFloat s with
            | Some v when v >= lo && v <= hi -> Some(Canon.canonicalFloat v)
            | _ -> None
        | StringLen(lo, hi) -> if s.Length >= lo && s.Length <= hi then Some s else None
        | Enum xs -> if List.contains s xs then Some s else None
        | AnyString -> Some s
        | SlotTree constraintOpt ->
            match slotKindOf s, constraintOpt with
            | None, _ -> None
            | Some _, None -> Some s
            | Some k, Some c -> if k = c then Some s else None

    /// Is a candidate value within the space? Exactly when it has a `canonical` spelling there.
    let validate (space: ValueSpace) (s: string) : bool = (canonical space s).IsSome

    /// THE space relation (Phase 295): does `required` admit every value `available` admits — is
    /// `available` a sub-space of `required`? The one answer `CapabilityPipeline.typeCheck` (does an
    /// upstream output feed an argument), `FunctionRegistry.findBySignature` (can the context fill a
    /// hole) and, through `ColumnType.widens`, `Query.validateParams` give. Sound by construction —
    /// `true` only where every value of `available` validates in `required` — and incomplete where
    /// the cross-family question would need enumerating values. The lattice, DECISIONS D104:
    ///
    ///   * numeric ranges compare by BOUNDS, and the one widening is int into float
    ///     (`FloatRange` admits an `IntRange` its bounds contain) — the widening `ColumnType.widens`
    ///     pins for `IntType` into `FloatType`; a float never narrows to an int;
    ///   * `StringLen` admits a `StringLen` it bounds, and an `Enum` whose every member it bounds;
    ///   * an `Enum` admits an `Enum` whose members are a subset of its own;
    ///   * `AnyString` is the top of the SCALAR spaces: it admits every int, number and string
    ///     space, since every argument at the seam is a string;
    ///   * the tree spaces are their own family: an unconstrained `SlotTree` admits every
    ///     `SlotTree`, a constrained one only a `SlotTree` of the same kind; no scalar space admits a
    ///     tree and no tree space a scalar.
    let subsumes (required: ValueSpace) (available: ValueSpace) : bool =
        match required, available with
        | IntRange(rl, rh), IntRange(al, ah) -> rl <= al && ah <= rh
        | FloatRange(rl, rh), FloatRange(al, ah) -> rl <= al && ah <= rh
        | FloatRange(rl, rh), IntRange(al, ah) -> rl <= float al && float ah <= rh
        | StringLen(rl, rh), StringLen(al, ah) -> rl <= al && ah <= rh
        | StringLen(rl, rh), Enum xs -> xs |> List.forall (fun x -> x.Length >= rl && x.Length <= rh)
        | Enum rs, Enum xs -> xs |> List.forall (fun x -> List.contains x rs)
        | AnyString, (IntRange _ | FloatRange _ | StringLen _ | Enum _ | AnyString) -> true
        | SlotTree None, SlotTree _ -> true
        | SlotTree(Some rk), SlotTree(Some ak) -> rk = ak
        | _ -> false

    /// The largest count a repeat hole may range over (Phase 307): the declared cap a count space
    /// must sit under. A repeat's count is how many times a host expands the repeated subtree, so a
    /// count space is a promise about the work one application can demand; one million is the
    /// number this package states (DECISIONS D111), and a space above it is refused as non-total
    /// rather than handed to a host to discover.
    [<Literal>]
    let maxRepeatCount = 1000000

    /// Is the space a COUNT space (Phase 307) — the totality criterion for repeats: an `IntRange`
    /// with `0 <= lo <= hi <= maxRepeatCount`. Every other space is refused as a repeat's count:
    /// `AnyString` and `SlotTree` are unbounded, an empty range counts nothing, and a `FloatRange`,
    /// `StringLen` or `Enum` is no count at all. Until Phase 307 the criterion was "neither
    /// `AnyString` nor `SlotTree`", under which `RepeatHole(FloatRange(0, infinity))` was total.
    let isCount (space: ValueSpace) : bool =
        match space with
        | IntRange(lo, hi) -> 0 <= lo && lo <= hi && hi <= maxRepeatCount
        | _ -> false

    /// Is the space WELL-FORMED (Phase 307): does it admit at least one value, and can every codec
    /// write it back? `Error SpaceFault.NonFinite` for a `FloatRange` with a NaN or infinite bound
    /// (looked for first, since NaN compares false against everything); `Error SpaceFault.Empty`
    /// for a range whose `lo` exceeds its `hi`, a `StringLen` whose `hi` is negative, or `Enum []`.
    /// `AnyString` and every `SlotTree` are well-formed. The one space check `Signature.validate`,
    /// the registries and the decoders run, so a declaration a seam admits has a space a value can
    /// lie in and a bound JSON can spell.
    let wellFormed (space: ValueSpace) : Result<unit, SpaceFault> =
        let finite (f: float) =
            not (System.Double.IsNaN f || System.Double.IsInfinity f)

        match space with
        | FloatRange(lo, hi) when not (finite lo && finite hi) -> Error SpaceFault.NonFinite
        | IntRange(lo, hi) when lo > hi -> Error SpaceFault.Empty
        | FloatRange(lo, hi) when lo > hi -> Error SpaceFault.Empty
        | StringLen(lo, hi) when lo > hi || hi < 0 -> Error SpaceFault.Empty
        | Enum [] -> Error SpaceFault.Empty
        | _ -> Ok()

    /// Values in single quotes, comma-separated — how a refusal names a closed set.
    let internal quoteAll (xs: string list) : string =
        xs |> List.map (fun x -> "'" + x + "'") |> String.concat ", "

    /// A value space in words, for a model to read (Phase 251): what a value must be to lie in the
    /// space, phrased so the sentence `InvokeError.describe` builds around it says what WOULD be
    /// accepted. An `Enum` names its members (the name-the-alternatives rule); a float bound is
    /// written in the canonical float layout, so the sentence is the same on every host.
    let describe (space: ValueSpace) : string =
        match space with
        | IntRange(lo, hi) -> "an integer from " + string lo + " to " + string hi
        | FloatRange(lo, hi) -> "a number from " + Canon.canonicalFloat lo + " to " + Canon.canonicalFloat hi
        | StringLen(lo, hi) -> "a string of " + string lo + " to " + string hi + " characters"
        | Enum [] -> "a member of an empty set, so no value is accepted"
        | Enum xs -> "one of " + quoteAll xs
        | AnyString -> "any string"
        | SlotTree None -> "a tree: a JSON object with a \"kind\""
        | SlotTree(Some k) -> "a tree of kind '" + k + "': a JSON object whose \"kind\" is '" + k + "'"

/// THE wire codec for a value space (Phase 295): one writer, `toJson`, and one reader, `decoder`,
/// which `CapabilityCodec`, `CapabilityPipeline` and the typed refusals all go through. A space is
/// a wire DOCUMENT in Phase 251's convention — discriminated by `"$type"` (`intRange`,
/// `floatRange`, `stringLen`, `enum`, `anyString`, `slotTree`), its bounds as `min` / `max` for
/// every range, an enum's members as `values`, a constrained slot's kind as `slotKind`.
///
/// `toSchema` is a DESCRIPTOR in the substrate's `"kind"` convention and writes the descriptor
/// spelling — `"kind"`, and a string length's bounds as `minLength` / `maxLength` — through
/// `descriptorJson`. That spelling is FROZEN, not merely kept: `ContentPack.signatureFingerprint` is
/// a hash over `toSchema`'s bytes, so moving the descriptor onto the document spelling would re-pin
/// every published pack (DECISIONS D104). The reader takes the descriptor spelling too, leniently,
/// for the 0.34.0 draft; nothing writes it into a document.
module SpaceCodec =

    /// Write a value space as a wire document (`"$type"`, `min` / `max`).
    let toJson (s: ValueSpace) : JVal =
        match s with
        | IntRange(lo, hi) -> Canon.typed "intRange" [ "min", JInt lo; "max", JInt hi ]
        | FloatRange(lo, hi) -> Canon.typed "floatRange" [ "min", JFloat lo; "max", JFloat hi ]
        | StringLen(lo, hi) -> Canon.typed "stringLen" [ "min", JInt lo; "max", JInt hi ]
        | Enum xs -> Canon.typed "enum" [ "values", JArr(xs |> List.map JStr) ]
        | AnyString -> Canon.typed "anyString" []
        | SlotTree c ->
            Canon.typed
                "slotTree"
                (match c with
                 | Some k -> [ "slotKind", JStr k ]
                 | None -> [])

    /// The frozen descriptor spelling `toSchema` writes (`"kind"`; a string length's bounds as
    /// `minLength` / `maxLength`). Never a document: `decoder` reads it only leniently.
    let descriptorJson (s: ValueSpace) : JVal =
        match s with
        | IntRange(lo, hi) -> Json.kindObj "intRange" [ "min", JInt lo; "max", JInt hi ]
        | FloatRange(lo, hi) -> Json.kindObj "floatRange" [ "min", JFloat lo; "max", JFloat hi ]
        | StringLen(lo, hi) -> Json.kindObj "stringLen" [ "minLength", JInt lo; "maxLength", JInt hi ]
        | Enum xs -> Json.kindObj "enum" [ "values", JArr(xs |> List.map JStr) ]
        | AnyString -> Json.kindObj "anyString" []
        | SlotTree c ->
            Json.kindObj
                "slotTree"
                (match c with
                 | Some k -> [ "slotKind", JStr k ]
                 | None -> [])

    let private cases (lenLo: string) (lenHi: string) : (string * Decoder<ValueSpace>) list =
        let int name = Decoder.field name Decoder.int
        let num name = Decoder.field name Decoder.float

        let pair (a: Decoder<'A>) (b: Decoder<'B>) (f: 'A -> 'B -> ValueSpace) : Decoder<ValueSpace> =
            a |> Decoder.bind (fun x -> b |> Decoder.map (f x))

        [ "intRange", pair (int "min") (int "max") (fun lo hi -> IntRange(lo, hi))
          "floatRange", pair (num "min") (num "max") (fun lo hi -> FloatRange(lo, hi))
          "stringLen", pair (int lenLo) (int lenHi) (fun lo hi -> StringLen(lo, hi))
          "enum", Decoder.field "values" (Decoder.list Decoder.str) |> Decoder.map Enum
          "anyString", Decoder.succeed AnyString
          "slotTree", Decoder.optField "slotKind" Decoder.str |> Decoder.map SlotTree ]

    /// Dispatch on `key`; an unknown tag keeps the sentence `unknown value-space kind: <tag>`.
    let private dispatchOn (key: string) (cs: (string * Decoder<ValueSpace>) list) : Decoder<ValueSpace> =
        fun el ->
            Decoder.tagDispatch key cs el
            |> Result.mapError (fun e ->
                match e.Code, e.Path, Decoder.tryMember key el with
                | DecodeCode.UnknownTag, [ PathSegment.Key k ], Some(JStr other) when k = key ->
                    { e with
                        Message = "unknown value-space kind: " + other }
                | _ -> e)

    /// Read a value space. A `"$type"` document is read as `toJson` writes it; an object with no
    /// `"$type"` and a `"kind"` is read in the descriptor spelling (lenient, for the 0.34.0 draft).
    let decoder: Decoder<ValueSpace> =
        fun el ->
            match Decoder.tryMember "$type" el, Decoder.tryMember "kind" el with
            | None, Some _ -> dispatchOn "kind" (cases "minLength" "maxLength") el
            | _ -> dispatchOn "$type" (cases "min" "max") el
