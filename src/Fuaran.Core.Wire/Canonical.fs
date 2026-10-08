namespace Fuaran.Core

// ============================================================================
//  Phase 379 — `Canonical` and `Codec<'T>`: one canonical writer and reader over a NAMED profile,
//  and one declaration that yields an encoder, a strict decoder and a schema (DECISIONS.md D122).
//
//  Neither renders anything new. `Canonical.write` IS `Json.renderWith`, so its bytes are the
//  profile's and are held by `EncodingProfileVectors`; a `Codec<'T>` writes a `JVal`, so the bytes
//  a declaration produces are `Canonical.write`'s of that value. What the two add is the part every
//  consumer was writing for itself: a guarded write under a profile, a canonicity check, and a codec
//  whose three faces cannot drift apart because they are built from one declaration.
// ============================================================================

/// The canonical JSON writer and reader under a NAMED `EncodingProfile` (Phase 379). A
/// content-addressed consumer names the profile its ids were computed under and reads and writes
/// through here; nothing about the bytes is the caller's to choose. Key order, number layout and
/// whitespace are the profile's: under every profile that exists today the member order is the
/// order the value was BUILT in (a codec's declaration order, never a sort the caller asks for),
/// numbers take the round-trip layout and there is no whitespace. A profile that changed any of
/// that would be a new profile, per `EncodingProfile`'s own rule.
[<RequireQualifiedAccess>]
module Canonical =

    /// The canonical text of `v` under `profile` — exactly `Json.renderWith profile v`. Total, and
    /// UNGUARDED in the way `Json.render` is: a non-finite float or an ill-formed string renders,
    /// as text that does not mean `v`. `tryWrite` is the form whose output may be hashed.
    let write (profile: EncodingProfile) (v: JVal) : string = Json.renderWith profile v

    /// `write`, GUARDED: the canonical text of `v` under `profile`, or the first non-finite float
    /// or ill-formed string in document order, named with its path. Over a value carrying neither
    /// it is exactly `Ok (write profile v)`. The refusal is the reason two values would otherwise
    /// share bytes, so a consumer that digests canonical text digests this function's `Ok`.
    let tryWrite (profile: EncodingProfile) (v: JVal) : Result<string, string> =
        match Json.firstNonFinite v with
        | Some(path, tok) -> Error("non-finite float has no canonical rendering of its own: " + tok + " at " + path)
        | None ->
            match Json.firstIllFormedString v with
            | Some(path, what) ->
                Error(
                    "ill-formed string has no canonical rendering of its own: "
                    + what
                    + " at "
                    + path
                )
            | None -> Ok(write profile v)

    /// Read canonical text — or any JSON text — as a value, the refusal typed as a `DecodeError` at
    /// the root. It accepts BOTH escaping spellings of a control character (`\n` and `\u000a`), so
    /// text written under any profile reads back as the one value it encodes; the profile is a
    /// fact about writing, never about reading.
    let read (text: string) : Result<JVal, DecodeError> = Decoder.parse text

    /// `true` exactly when `text` is the canonical text, under `profile`, of the value it reads
    /// as: it parses, the value has a guarded rendering, and that rendering is `text` byte for
    /// byte. False for whitespace, a `-0` (it reads as the integer `0`, whose text is `0`), and any
    /// spelling of a control character `profile` does not write. A store whose texts are all
    /// canonical under its profile has ids that recompute from the values alone.
    let isCanonical (profile: EncodingProfile) (text: string) : bool =
        match Json.parse text with
        | Error _ -> false
        | Ok v ->
            match tryWrite profile v with
            | Ok t -> t = text
            | Error _ -> false
