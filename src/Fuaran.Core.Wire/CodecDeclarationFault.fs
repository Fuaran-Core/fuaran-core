namespace Fuaran.Core

/// Why the `Codec` combinators refused a declaration (Phase 384) — the one vocabulary they refuse
/// in, at build and at write. A declaration that cannot be written consistently is refused when
/// it is BUILT (`Codec.enum`, `Codec.build` and `Codec.union` answer every fault they find, in
/// declaration order); a declaration that is not exhaustive over the type it is written at is
/// refused when a value it does not cover is WRITTEN, through `Codec<'T>.Write`, as
/// `Unrecognised` at the path of the value no case recognised.
[<RequireQualifiedAccess>]
type CodecDeclarationFault =
    /// `Codec.enum` declares one spelling for two cases.
    | RepeatedSpelling of spelling: string
    /// An object declares one member twice; `case` names the union case whose members they are,
    /// `None` for a record closed by `Codec.build`.
    | RepeatedMember of name: string * case: string option
    /// `Codec.union` declares one tag for two cases.
    | RepeatedTag of tag: string
    /// A case of `Codec.union` declares the union's discriminator as one of its own members.
    | DiscriminatorAsMember of tag: string * key: string
    /// A written value that no case of an `enum` or a `union` recognises — the declaration is not
    /// exhaustive over the values it was asked to write. `path` is root-first to that value,
    /// relative to the value handed to `Write`.
    | Unrecognised of path: PathSegment list

/// Rendering and paths for `CodecDeclarationFault` (Phase 384).
[<RequireQualifiedAccess>]
module CodecDeclarationFault =

    /// One sentence naming the fault.
    let describe (f: CodecDeclarationFault) : string =
        match f with
        | CodecDeclarationFault.RepeatedSpelling s -> "the spelling '" + s + "' names two cases"
        | CodecDeclarationFault.RepeatedMember(n, None) -> "the member '" + n + "' is declared twice"
        | CodecDeclarationFault.RepeatedMember(n, Some tag) ->
            "the member '" + n + "' is declared twice in case '" + tag + "'"
        | CodecDeclarationFault.RepeatedTag t -> "the tag '" + t + "' names two cases"
        | CodecDeclarationFault.DiscriminatorAsMember(tag, key) ->
            "case '" + tag + "' declares the discriminator '" + key + "' as a member"
        | CodecDeclarationFault.Unrecognised path ->
            "no case of the declaration recognises the value at " + DecodePath.render path

    /// The fault one step further from the root: an `Unrecognised` path gains `step` at its head;
    /// a build-time fault has no path and is unchanged.
    let under (step: PathSegment) (f: CodecDeclarationFault) : CodecDeclarationFault =
        match f with
        | CodecDeclarationFault.Unrecognised path -> CodecDeclarationFault.Unrecognised(step :: path)
        | other -> other
