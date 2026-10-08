namespace Fuaran.Core

/// The canonical `$type` wire discipline — the single
/// platform-wide canonical-JSON convention `Fuaran.Core` and `Fuaran.UI` share, so a value
/// serialised by Core is byte-identical to the same value serialised by the UI host (and, via the
/// §11.1 gate, by the TS / Python hosts). It renders the `JVal` model under the UI host's mature
/// conventions (`fuaran/docs/WIRE_FORMAT.md` §2): `$type`-discriminated DU objects, **Ordinal-sorted
/// object keys** (recursively), control chars escaped as `\u00xx` (no `\n`/`\r`/`\t` shortcuts), and
/// the pinned float layout — `Double.ToString("R")` on .NET, the byte-identical JS re-layout under
/// Fable — so numeric columns + literals match across hosts. `Json.render` (author-ordered, `kind`-
/// tagged) stays for Core's pre-unification internal uses; `Canon.render` is the cross-host form.
module Canon =

    // The canonical string escape (WIRE_FORMAT §2 rule 6) is `Json.escape`: only `"`, `\`, and
    // control chars (`U+0000`–`U+001F` → `\u00xx`, lower-case hex), no `\n`/`\r`/`\t` shortcuts —
    // byte-for-byte the UI host's `appendRawString`. The spine has one escaping rule (Phase 287)
    // and one writer (Phase 306, `Json.writeWith`), which every renderer here goes through.

    /// The single canonical, cross-host float → string encoder (Phase 55). Non-finite floats render to
    /// the fixed JSON-string tokens `"NaN"` / `"Infinity"` / `"-Infinity"`; `-0.0` collapses to `0`; a
    /// finite float uses `FloatLayout.finite` — `Double.ToString("R", InvariantCulture)` on .NET and the
    /// byte-identical JS shortest-round-trip re-layout under Fable (WIRE_FORMAT §2 rule 5). Every
    /// float→wire / float→key path in the substrate routes through this one function so the bytes match
    /// across the .NET / Fable / TS / Python hosts. Pinned in `STABILITY.md`.
    let canonicalFloat (f: float) : string =
        match JVal.nonFiniteToken f with
        | Some tok -> "\"" + tok + "\""
        | None ->
            // -0 collapses to 0 (WIRE_FORMAT §2 rule 5) — the wire rule, applied before the layout.
            let v = if f = 0.0 then 0.0 else f
            FloatLayout.finite v

    /// Render a `JVal` under the canonical `$type` discipline: object keys Ordinal-sorted
    /// (recursively), the pinned float layout, canonical escaping. The encoder enforces key order;
    /// decoders stay order-tolerant (they look up by name). Iterative since Phase 306 (`Json`'s
    /// writer), so a value of any nesting depth renders; the bytes are unchanged.
    let render (v: JVal) : string = Json.writeWith true canonicalFloat v

    /// The GUARDED canonical render (Phase 165) — [[render]] with a refusal beside it, in the
    /// shape `Json.tryRender` gives `Json.render`.
    ///
    /// [[render]] spells a non-finite float as the QUOTED token `"NaN"` / `"Infinity"` /
    /// `"-Infinity"`, which is byte for byte what the STRING of those characters renders as. The
    /// wire it produces is valid, and wrong: a digest over `JFloat nan` equals the digest over
    /// `JStr "NaN"`, the value a reader decodes those bytes back to (`proofs/WireCanon.fst`,
    /// `render_aliases_nan` / `_pos_inf` / `_neg_inf`). This entry point names the FIRST non-finite
    /// `JFloat` in document order — arrays by index, object members in AUTHORED order — as a typed
    /// `Error` carrying its token and its path (`$` for the root, `[i]` for an array item,
    /// `["key"]` for a member, the key under the canonical escape so the path is unambiguous for
    /// any key). Over a value holding no non-finite float it is exactly `Ok (render v)`.
    ///
    /// The predicate is FINITENESS and nothing else — the first clause of the model's
    /// `float_canonical`. The format's documented normalisations are NOT refused: an
    /// integer-shaped finite float (`JFloat 2.0` renders `2`), the `-0` collapse, and the key
    /// sort all pass through unchanged, because each is what the format says rather than a value
    /// silently becoming another.
    ///
    /// [[render]] is untouched, and a value that aliases under it keeps aliasing under it: its
    /// bytes are pinned by the conformance corpus and by every other host. A caller that digests
    /// the canonical form and wants the refusal digests THIS function's `Ok` — there is no digest
    /// in this package to wrap (it references nothing that hashes), so the guarded digest is
    /// `tryRender v |> Result.map digest` at the caller, with the caller's own hash.
    ///
    /// A STRING THAT IS NOT WELL-FORMED UTF-16 IS REFUSED TOO (Phase 306), and for the same
    /// reason a non-finite float is: its digest is some other value's. A lone surrogate has no
    /// code point and UTF-8 has no encoding for it, so every encoder substitutes — over all
    /// 66,060,288 (high surrogate, non-low unit) pairs, 97.6% used to encode byte-identically to a
    /// well-formed astral character, and under the platform's replacement rule `"\uD800"`,
    /// `"\uDFFF"` and `"�"` are one byte string. Replacement only moves which strings collide;
    /// injectivity needs the refusal, here and at the parser, which makes it on read. The first
    /// such string in document order (a member's key before its value) is named with its path. A
    /// non-finite float is looked for first, so every refusal this function already made keeps its
    /// message. `proofs/WireCanon.fst` states what the two refusals buy: over values this function
    /// accepts, equal UTF-8 bytes of the rendering are equal normal forms.
    let tryRender (v: JVal) : Result<string, string> =
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
            | None -> Ok(render v)

    /// Render a `JVal` with the SAME canonical escaping and pinned float layout as [[render]],
    /// but object keys in AUTHORED order — no sort. The declared-key-order leg (a vocabulary
    /// whose canonical form is DECLARATION order rather than Ordinal; the IDL's
    /// `WireShape.KeyOrder`): there the ENCODER is the order authority — it constructs each
    /// object's pairs in the declared order and this renderer preserves them, so canonical form
    /// stays unique without a sort. [[render]] is untouched and remains the cross-host default.
    let renderOrdered (v: JVal) : string = Json.writeWith false canonicalFloat v

    /// Build a `$type`-discriminated object — the DU-position convention. `$type` (0x24) sorts
    /// before every lower-case data key, so it is always the canonical first key after `render`.
    let typed (tag: string) (fields: (string * JVal) list) : JVal = JObj(("$type", JStr tag) :: fields)
