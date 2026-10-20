namespace Fuaran.Core

// ============================================================================
//  Phase 379 — `Canonical` and `Codec<'T>`: one canonical writer and reader over a NAMED profile,
//  and one declaration that yields an encoder, a strict decoder and a schema (DECISIONS.md D122).
//
//  Phase 520 — ONE rendering contract (D120 as amended): `Canon.fs` folded in here. The cross-host
//  form `Canon.render` wrote is the named rendering `Rendering.CrossHost`, its author-ordered twin
//  `Canon.renderOrdered` is `Rendering.Declared`, and both are written, guarded and recognised by
//  the same three functions as the two store profiles — so a store keyed on cross-host bytes can
//  now ask whether a stored text is canonical. No rendered byte moved: `EncodingProfileVectors`
//  holds a column per rendering.
// ============================================================================

/// The canonical JSON writer and reader under a NAMED `Rendering` (Phase 379; every rendering
/// since Phase 520). A content-addressed consumer names the rendering its ids were computed under
/// and reads and writes through here; nothing about the bytes is the caller's to choose. Member
/// order, string escape and float rule are the rendering's (`Rendering`), and no rendering writes
/// whitespace. A rendering that changed any of that would be a new rendering, per `Rendering`'s
/// own rule.
///
/// The cross-host form (`Rendering.CrossHost`) is the platform-wide canonical-JSON convention
/// `Fuaran.Core` and `Fuaran.UI` share (WIRE_FORMAT §2): `$type`-discriminated objects
/// (`Canonical.typed`), Ordinal-sorted member names (recursively), control characters escaped as
/// `\u00xx` with no `\n` / `\r` / `\t` shortcut, and the pinned float layout
/// (`FloatLayout.canonical`), so a value serialised here is byte-identical to the same value
/// serialised by every other host.
[<RequireQualifiedAccess>]
module Canonical =

    /// The canonical text of `v` under `rendering`. Total, iterative (a value of any nesting depth
    /// renders), and UNGUARDED in the way `Json.render` is: a non-finite float, an ill-formed string
    /// or a repeated member renders, as text that does not mean `v`. `tryWrite` is the form whose
    /// output may be hashed.
    let write (rendering: Rendering) (v: JVal) : string = Json.writeRendering rendering v

    /// `write`, GUARDED: the canonical text of `v` under `rendering`, or the first value with no
    /// canonical rendering of its own — a non-finite float, an ill-formed string or a repeated
    /// member name, in that priority and in document order — named with its path, through THE guard
    /// (`Json.firstRenderFault`) in its one sentence (`RenderFault.describe`). Over a value carrying
    /// none it is exactly `Ok (write rendering v)`. The refusal is the reason two values would
    /// otherwise share bytes, or the text would not parse back, so a consumer that digests canonical
    /// text digests this function's `Ok`.
    ///
    /// Under `Rendering.CrossHost` the non-finite refusal matters most: the canonical float rule
    /// spells a non-finite float as the QUOTED token, which is byte for byte what the STRING of
    /// those characters renders as, so a digest over `JFloat nan` would equal the digest over
    /// `JStr "NaN"` (`proofs/WireCanon.fst`, `render_aliases_nan` / `_pos_inf` / `_neg_inf`). The
    /// format's documented normalisations are NOT refused: an integer-shaped finite float
    /// (`JFloat 2.0` renders `2`), the `-0` collapse and the member sort all pass through, because
    /// each is what the format says rather than a value silently becoming another.
    /// `proofs/WireCanon.fst` states what the refusals buy: over values this function accepts,
    /// equal UTF-8 bytes of the rendering are equal normal forms.
    let tryWrite (rendering: Rendering) (v: JVal) : Result<string, string> =
        match Json.firstRenderFault false v with
        | Some f -> Error(RenderFault.describe f)
        | None -> Ok(write rendering v)

    /// Read canonical text — or any JSON text — as a value under `ReadOptions.strict`, the refusal
    /// typed as a `DecodeError` (`Decoder.parse`). It accepts BOTH escaping spellings of a control
    /// character (`\n` and `\u000a`), so text written under any rendering reads back as the one
    /// value it encodes; the rendering is a fact about writing, never about reading.
    let read (text: string) : Result<JVal, DecodeError> = Decoder.parse ReadOptions.strict text

    /// `true` exactly when `text` is the canonical text, under `rendering`, of the value it reads
    /// as: it parses, the value has a guarded rendering, and that rendering is `text` byte for
    /// byte. It agrees with `tryWrite` on every value: `isCanonical rendering t` holds for each
    /// `Ok t` that `tryWrite rendering` answers. False for whitespace, a `-0` (it reads as the
    /// integer `0`, whose text is `0`), any spelling of a control character `rendering` does not
    /// write, and — under a sorted rendering — members out of Ordinal order. A store whose texts are
    /// all canonical under its rendering has ids that recompute from the values alone.
    let isCanonical (rendering: Rendering) (text: string) : bool =
        match Json.parse text with
        | Error _ -> false
        | Ok v ->
            match tryWrite rendering v with
            | Ok t -> t = text
            | Error _ -> false

    /// A `$type`-discriminated object — the DU-position convention of the cross-host form
    /// (`Canon.typed` until Phase 520). `$type` (0x24) sorts before every lower-case data key, so it
    /// is always the canonical first member under `Rendering.CrossHost`.
    let typed (tag: string) (fields: (string * JVal) list) : JVal = JObj(("$type", JStr tag) :: fields)
