namespace Fuaran.Core

/// The content address of a wire value (Phase 382, `DECISIONS.md` D122 as amended): the typed
/// `Digest` of its canonical text under a NAMED `EncodingProfile`. This is the profile-pinned
/// constructor D122 proposed — the one place a `Digest` is minted over a JSON rendering — and it
/// hashes nothing but `Canonical`'s guarded output, so a consumer cannot digest a rendering it did
/// not pin: the profile is an argument, and the bytes are the profile's.
///
/// It lives in its own package because the renderer (`Fuaran.Core.Wire`) and the hash
/// (`Fuaran.Core.Tree`) live in two packages D2 keeps apart; this package references both, so
/// neither references the other. SHA-256 over the UTF-8 bytes of the text — `Hash.sha256Hex`'s
/// digest — so a store that keyed `sha256:` + `Hash.sha256Hex (Canonical.write p v)` reads the same
/// digest here, byte for byte.
[<RequireQualifiedAccess>]
module ContentAddress =

    open System.Runtime.CompilerServices

    /// SHA-256 over the UTF-8 bytes of `text`, through `Digest.ofSha256Bytes` — the bytes-level
    /// constructor `Fuaran.Core.Tree` shares with this package alone, through its
    /// `InternalsVisibleTo` (D122). `NoInlining` (D129): the F# optimiser would otherwise copy a body
    /// naming that internal member into a Release-built caller, which cannot reach it and fails with
    /// `MethodAccessException`. Both constructors below reach it only through here.
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let private mint (text: string) : Digest =
        Digest.ofSha256Bytes (Hash.utf8Bytes text)

    /// The digest of `v`'s canonical text under `profile`: `Canonical.tryWrite profile v`, hashed.
    /// Refused exactly where `tryWrite` refuses (a non-finite float or an ill-formed string, named
    /// with its path), because there the text would not mean `v` and two values would share it.
    let ofValue (profile: EncodingProfile) (v: JVal) : Result<Digest, string> =
        Canonical.tryWrite profile v |> Result.map mint

    /// The digest of stored text, admitted only when the text IS canonical under `profile`
    /// (`Canonical.isCanonical`): then its bytes are `ofValue profile` of the value it reads as, and
    /// the digest is the same one. Any other text — whitespace, a spelling of a control character
    /// `profile` does not write, text that does not parse — is refused, because its digest would
    /// not recompute from the value.
    let ofCanonicalText (profile: EncodingProfile) (text: string) : Result<Digest, string> =
        if isNull text then
            Error "the text is null"
        elif Canonical.isCanonical profile text then
            Ok(mint text)
        else
            Error(
                "the text is not canonical under "
                + EncodingProfile.name profile
                + ", so its digest would not recompute from the value it reads as"
            )
