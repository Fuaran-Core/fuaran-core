namespace Fuaran.Core

/// WHICH canonical rendering a content-addressed store's ids were computed under (Phase 360). A
/// store that hashes `Json.render` output — an op encoder, a node pre-image — keys its ids on the
/// rendered BYTES, so a change to those bytes is a change to every id it holds. A store names its
/// profile and renders through `Json.renderWith`, and its ids keep recomputing while the spine's own
/// rendering moves on. Closed and versioned: a later byte change to `Json.render` adds a case and
/// moves `EncodingProfile.current`; it never changes what an existing case renders.
///
/// The profiles differ in exactly one respect today, measured against the published binaries
/// (DECISIONS.md D120): how a string spells line feed, carriage return and tab. Number layout, member
/// order and whitespace are the same under both. Since Phase 520 each profile names a `Rendering`
/// (`Rendering.ofProfile`), and the cross-host form is a named rendering too (`Rendering.CrossHost`).
///
/// `Fuaran.Core.OpStream` names the same two profiles for the pre-images it builds itself as config
/// constructors (`StreamConfig.legacyEscape` is `V1`, `StreamConfig.canonical` `V2`, since Phase 516);
/// DECISIONS.md D2 keeps that package free of a reference here, so a consumer holding both maps one to
/// the other in a two-case match.
[<RequireQualifiedAccess>]
type EncodingProfile =
    /// The rendering of `0.30.0` through `0.32.0`: `\n`, `\r` and `\t` as the short escapes, every
    /// other control character as lower-case `\u00xx`, `"` and `\` escaped, nothing else.
    | V1
    /// The rendering since `0.33.0` (Phase 287): every control character `U+0000`–`U+001F` as
    /// lower-case `\u00xx`, with no short form; `"` and `\` escaped, nothing else.
    | V2

/// The `EncodingProfile` companions (Phase 360): the current default, the closed set, and the
/// canonical name a store declares its profile by.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module EncodingProfile =

    /// The profile `Json.render` renders under — `V2` since `0.33.0`. It moves only when the
    /// spine's rendering does, and a store pinned to a named profile does not move with it.
    let current: EncodingProfile = EncodingProfile.V2

    /// Every profile, oldest first.
    let all: EncodingProfile list = [ EncodingProfile.V1; EncodingProfile.V2 ]

    /// The canonical name a store declares its profile by: `v1` or `v2` — one declaration names both
    /// halves of a store's encoding, its renderer here and its op-stream config.
    let name (profile: EncodingProfile) : string =
        match profile with
        | EncodingProfile.V1 -> "v1"
        | EncodingProfile.V2 -> "v2"

    /// The profile a canonical name declares, or `None` for any other string (case-sensitive:
    /// `name >> tryParse` is `Some`, and nothing else is).
    let tryParse (name: string) : EncodingProfile option =
        match name with
        | "v1" -> Some EncodingProfile.V1
        | "v2" -> Some EncodingProfile.V2
        | _ -> None

/// How a rendering spells a string's control characters (Phase 520) — the axis the two
/// `EncodingProfile`s differ on.
[<RequireQualifiedAccess>]
type StringEscape =
    /// `\n`, `\r` and `\t` as the short escapes, every other control character as lower-case
    /// `\u00xx` — `EncodingProfile.V1`'s spelling, FROZEN at `0.30.0`.
    | ShortControls
    /// Every control character `U+0000`–`U+001F` as lower-case `\u00xx`, with no short form —
    /// the spine's one escape since `0.33.0` (`Json.escape`).
    | AllControlsHex

/// The order a rendering writes an object's members in (Phase 520).
[<RequireQualifiedAccess>]
type MemberOrder =
    /// As the value was BUILT — a codec's or an encoder's declaration order is normative, and the
    /// renderer keeps it.
    | Authored
    /// Ordinal-sorted by member name, recursively — so canonical form needs no agreed order.
    | Sorted

/// How a rendering spells a float (Phase 520).
[<RequireQualifiedAccess>]
type FloatRule =
    /// `FloatLayout.roundTrip`: the round-trip layout, the sign of `-0` kept, a non-finite float as
    /// its bare token (text `parse` refuses, which is what the guarded writers refuse first).
    | RoundTrip
    /// `FloatLayout.canonical`: the round-trip layout with `-0` collapsed to `0`, a non-finite float
    /// as WIRE_FORMAT §7's QUOTED token.
    | Canonical

/// ONE rendering contract (Phase 520; DECISIONS.md D120 as amended): every canonical byte rule the
/// spine writes is a value of this record — the three axes it varies on, each a closed union — and
/// every one of them is written by one writer (`Canonical.write`), guarded by one guard
/// (`Canonical.tryWrite`) and recognised by one predicate (`Canonical.isCanonical`). The named
/// values are `Rendering.V1`, `Rendering.V2`, `Rendering.CrossHost` and `Rendering.Declared`; a
/// new byte rule is a NEW NAME, never an edit to one of these, because a store keys its ids on the
/// bytes a named rendering writes. No rendering writes whitespace.
type Rendering =
    {
        /// How a string's control characters are spelled.
        Escape: StringEscape
        /// The order an object's members are written in.
        Order: MemberOrder
        /// How a float is spelled.
        Floats: FloatRule
    }

/// The four named renderings (Phase 520). Closed and versioned, as `EncodingProfile` is: their
/// bytes are pinned by `EncodingProfileVectors` (a column per rendering) and by the cross-host
/// corpus, and none of them moves.
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Rendering =

    /// `EncodingProfile.V1`'s bytes — `0.30.0`'s `Json.render`, FROZEN.
    let V1: Rendering =
        { Escape = StringEscape.ShortControls
          Order = MemberOrder.Authored
          Floats = FloatRule.RoundTrip }

    /// `EncodingProfile.V2`'s bytes — `Json.render` since `0.33.0`: the chain pre-images and every
    /// author-ordered store.
    let V2: Rendering =
        { Escape = StringEscape.AllControlsHex
          Order = MemberOrder.Authored
          Floats = FloatRule.RoundTrip }

    /// THE cross-host canonical form (`Canon.render` until Phase 520, unmoved since `0.30.0`):
    /// Ordinal-sorted members, the canonical float, the spine's escape. The IDL artifact, the
    /// cross-host conformance corpus and every `$type`-discriminated wire value are written in it
    /// (WIRE_FORMAT §2), so it is the bytes every host hashes.
    let CrossHost: Rendering =
        { Escape = StringEscape.AllControlsHex
          Order = MemberOrder.Sorted
          Floats = FloatRule.Canonical }

    /// The cross-host form for a vocabulary whose canonical member order is its DECLARATION order
    /// (`Canon.renderOrdered` until Phase 520; the IDL's `KeyOrder.Declared`): the encoder is the
    /// order authority and this rendering keeps the order it built.
    let Declared: Rendering =
        { Escape = StringEscape.AllControlsHex
          Order = MemberOrder.Authored
          Floats = FloatRule.Canonical }

    /// Every named rendering, in the order above.
    let all: Rendering list = [ V1; V2; CrossHost; Declared ]

    /// The rendering a content-addressed store's declared `EncodingProfile` names: `V1` or `V2`.
    let ofProfile (profile: EncodingProfile) : Rendering =
        match profile with
        | EncodingProfile.V1 -> V1
        | EncodingProfile.V2 -> V2
