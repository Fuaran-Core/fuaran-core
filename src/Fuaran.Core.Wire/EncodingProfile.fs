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
/// order and whitespace are the same under both. `Canon.render` is not profiled: its bytes have not
/// moved since `0.30.0`.
///
/// `Fuaran.Core.OpStream` carries the same two cases as `OpStream.EncodingProfile` for the pre-images
/// it builds itself (DECISIONS.md D2 keeps that package free of a reference here); both spell a
/// profile by the same `name`.
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

    /// The canonical name a store declares its profile by: `v1` or `v2`. The same strings
    /// `OpStream.profileName` spells, so one declaration names both halves of a store's encoding.
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
