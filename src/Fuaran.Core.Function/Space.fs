namespace Fuaran.Core

/// The value domain a hole ranges over (value-space projection is the type system
/// for holes). `AnyString` and `SlotTree` are the *unbounded* spaces.
///
/// `SlotTree` (Phase 229) is the value space of a tree-typed slot at the scalar invocation seam:
/// a wire document — a `"kind"`-tagged JSON object, carried as the argument string — whose kind
/// satisfies the slot's constraint when one is declared (any kind otherwise). Core owns no node
/// type, so the space is stated over the WIRE and checked by shape; decoding the document into the
/// domain's node is the host's, per the witness pattern. `Function.signature` enters every
/// `SlotHole` with this space, which is what makes a capability over a slotted artifact invocable.
type ValueSpace =
    | IntRange of lo: int * hi: int
    | FloatRange of lo: float * hi: float
    | StringLen of lo: int * hi: int
    | Enum of string list
    | AnyString
    | SlotTree of kindConstraint: string option

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

    /// Is a candidate value within the space?
    let validate (space: ValueSpace) (s: string) : bool =
        match space with
        | IntRange(lo, hi) ->
            match System.Int32.TryParse s with
            | true, v -> v >= lo && v <= hi
            | _ -> false
        | FloatRange(lo, hi) ->
            match
                System.Double.TryParse(
                    s,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            with
            | true, v -> v >= lo && v <= hi
            | _ -> false
        | StringLen(lo, hi) -> s.Length >= lo && s.Length <= hi
        | Enum xs -> List.contains s xs
        | AnyString -> true
        | SlotTree constraintOpt ->
            match slotKindOf s, constraintOpt with
            | None, _ -> false
            | Some _, None -> true
            | Some k, Some c -> k = c

    /// A space is bounded unless it is `AnyString` or `SlotTree` — the totality criterion for
    /// repeats (a tree space is no count space, so a repeat over one is refused as non-total).
    let internal isBounded (space: ValueSpace) : bool =
        match space with
        | AnyString
        | SlotTree _ -> false
        | _ -> true

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
