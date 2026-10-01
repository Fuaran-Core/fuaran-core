namespace Fuaran.Core.Idl

open System

// ---------------------------------------------------------------------------
// Phase 321 — codegen trust boundary (tasks 2 + 3).
//
// When AI-emitted wire is codegen'd to host source you compile and run, the
// generator is a trusted computing base. Phase 321 task 1 (shipped) removed the
// template-injection class: `Gen.fsharpValue` routes every wire string through an
// escaped literal. The two risks that remain are the ones this file closes:
//
//   * task 2 — a `Custom` node resolving to arbitrary host code. A `Custom` on
//     the wire carries only data (moduleId / componentId / props / a content
//     hash), but the GENERATED code would resolve it to a registered host
//     component. So `Custom` must be **inert-by-default**: emitted live only when
//     it matches a declared allowlist AND its content-hash verifies (per the
//     `HashStrictness` mode); an unknown / unhashed / drifted `Custom` becomes an
//     inert labelled placeholder, never a live call.
//
//   * task 3 — unsanitised URLs / attributes / markdown becoming live in
//     generated code. The pure, Fable-portable `Sanitize.*` functions (the
//     render-time floor in `Fuaran.UI.Renderer.Sanitize`) are LIFTED here to a
//     Core-shared location (so non-UI hosts reuse them) and run during
//     generation, so generated code can only contain sanitised values.
//
// The boundary is expressed as a pure `IdlValue -> IdlValue` transform
// (`Trust.harden`) applied BEFORE `Gen.fsharpValue` / `Encode.encode`: gate every
// `Custom`, sanitise every declared URL / markdown field. Generated code is then
// inert-by-construction over the hardened value. FSharp.Core-only + Fable-clean.
// ---------------------------------------------------------------------------

/// The pure, Fable-portable sanitisation floor — lifted from
/// `Fuaran.UI.Renderer.Sanitize` to a Core-shared location so every host (UI,
/// non-UI, the codegen boundary) reuses one implementation (Phase 321 task 3).
///
/// **Parity is held by the shared corpus, not by this comment.** Phase 321 claimed
/// the semantics matched the UI module "byte-for-byte" and they did not: the lift
/// copied a subset and dropped two behaviours, both failing open (Phase 96). The
/// claim was load-bearing and unenforced, which is the combination that let the
/// gap survive. The adversarial cases in `IdlCertificationTests` now pin the behaviours that
/// diverged; a change here that is not mirrored in `Fuaran.UI.Renderer.Sanitize`
/// (and vice versa) is a defect until the two are consolidated behind one
/// implementation.
module Sanitize =

    /// URL schemes accepted verbatim.
    let private allowedUrlSchemes =
        Set.ofList [ "http"; "https"; "mailto"; "tel"; "ftp"; "sftp" ]

    /// Schemes always rejected, regardless of caller intent.
    let private rejectedUrlSchemes = Set.ofList [ "javascript"; "vbscript"; "file" ]

    let private trimAndLower (s: string) : string = s.Trim().ToLowerInvariant()

    /// ASCII case fold of ONE UTF-16 unit: `A`-`Z` to `a`-`z`, everything else unchanged.
    ///
    /// Deliberately not `Char.ToLowerInvariant` and not a lowered copy of the string
    /// (Phase 291). Under Fable a string lowercase is `toLowerCase()`, which is not
    /// length-preserving (U+0130 becomes two units), so an index taken from a lowered
    /// copy and applied to the original lands on the wrong characters, one position per
    /// such character. Every token this module scans for (an element name, `on`, a URL
    /// scheme) is ASCII and a browser compares each of them ASCII case-insensitively, so
    /// the per-unit ASCII fold is the whole of what a scan needs, and it is the same
    /// function on both pipelines: no index ever comes from a string of another length.
    let private foldAscii (c: char) : char =
        if c >= 'A' && c <= 'Z' then char (int c + 32) else c

    /// The first index at or after `start` where `needle` occurs in `hay`, comparing each
    /// unit of `hay` through `foldAscii`, or `-1`. `needle` MUST already be lower-case
    /// ASCII (every caller passes a literal). The index is into `hay` itself.
    let private indexOfFolded (hay: string) (needle: string) (start: int) : int =
        let last = hay.Length - needle.Length
        let mutable i = if start < 0 then 0 else start
        let mutable found = -1

        while found < 0 && i <= last do
            let mutable k = 0

            while k < needle.Length && foldAscii hay[i + k] = needle[k] do
                k <- k + 1

            if k = needle.Length then found <- i else i <- i + 1

        found

    /// Split a URL into `(schemeOpt, url)`. A URL with no `:` before the first
    /// `/ ? #` (relative path, fragment, empty) has no scheme. Whitespace + C0
    /// controls are stripped from the scheme candidate so `java\tscript:` etc.
    /// classify as `javascript`.
    let private extractScheme (url: string) : string option =
        if isNull url then
            None
        else
            let mutable colonIdx = -1
            let mutable slashIdx = -1
            let mutable i = 0

            while i < url.Length && colonIdx < 0 && slashIdx < 0 do
                let ch = url[i]

                if ch = ':' then
                    colonIdx <- i
                elif ch = '/' || ch = '?' || ch = '#' then
                    slashIdx <- i

                i <- i + 1

            if colonIdx < 0 || (slashIdx >= 0 && slashIdx < colonIdx) then
                None
            else
                let raw = url.Substring(0, colonIdx)
                let cleaned = raw |> Seq.filter (fun ch -> int ch > 0x20) |> Seq.toArray |> String
                Some(trimAndLower cleaned)

    /// The WHATWG URL Standard's own pre-parse normalisation, ASCII-exact, in this order:
    /// (1) remove leading and trailing C0-or-space (ALL of U+0000-U+0020, not merely the
    /// whitespace subset); (2) remove every U+0009 / U+000A / U+000D from what remains.
    ///
    /// Deliberately NOT `String.Trim()`: a native trim answers a different question on each
    /// host and removes non-ASCII whitespace the parser keeps, and the floor's purpose is
    /// that a value vetted on one host is safe on another. Step 2 is those three code
    /// points ONLY: U+000B / U+000C are removed at the edges and KEPT in the interior, so
    /// `/<VT>/host/x` is an ordinary same-origin path and stays one. Mirrors the UI
    /// module's `normalizeUrlForFloor` clause for clause (Phase 291).
    let private normalizeUrlForFloor (s: string) : string =
        if isNull s then
            ""
        else
            let mutable lo = 0
            let mutable hi = s.Length - 1

            while lo <= hi && s[lo] <= ' ' do
                lo <- lo + 1

            while hi >= lo && s[hi] <= ' ' do
                hi <- hi - 1

            s.Substring(lo, hi - lo + 1)
            |> Seq.filter (fun c -> c <> '\t' && c <> '\n' && c <> '\r')
            |> Seq.toArray
            |> String

    /// `true` when a schemeless URL starts with two units drawn from `/` and `\`, in any
    /// mix. All four spellings (`//h`, `/\h`, `\\h`, `\/h`) resolve off-origin, because a
    /// browser reads `\` as `/` and then takes what follows as an AUTHORITY. A SINGLE
    /// leading backslash reads as `/`, an ordinary same-origin path, and is allowed.
    let private isProtocolRelative (url: string) : bool =
        let slashish (c: char) = c = '/' || c = '\\'
        url.Length >= 2 && slashish url[0] && slashish url[1]

    /// The sanitised URL, or `None` when the scheme is rejected / unknown /
    /// protocol-relative. Default-deny: an unknown scheme is refused. The returned URL is
    /// the normalised one (`normalizeUrlForFloor`), as the UI floor's is.
    let sanitizeUrl (url: string) : string option =
        if isNull url then
            None
        else
            let normalised = normalizeUrlForFloor url

            if normalised = "" then
                Some normalised
            else
                match extractScheme normalised with
                | None when isProtocolRelative normalised ->
                    // Protocol-relative (`//host`, in any `/` `\` mix) resolves off-origin.
                    None
                | None -> Some normalised
                | Some scheme when rejectedUrlSchemes.Contains scheme -> None
                | Some scheme when allowedUrlSchemes.Contains scheme -> Some normalised
                | Some _ -> None

    /// The URL if accepted, else the deny sentinel `"about:blank"`.
    let sanitizeUrlOrBlank (url: string) : string =
        sanitizeUrl url |> Option.defaultValue "about:blank"

    /// `data-*` / `aria-*` attribute-key allowlist; `on*` event handlers and
    /// everything else are rejected.
    ///
    /// Named without the UI tier's `Extra` prefix deliberately: `ExtraAttributes`
    /// is UI-envelope vocabulary and this module is host-neutral. The `style`
    /// rejection below is redundant against the `data-` / `aria-` test that
    /// follows it — `style` fails that anyway — and is kept only so the rejection
    /// survives if the allowlist ever widens, matching the UI original.
    let isAllowedAttributeKey (key: string) : bool =
        if isNull key then
            false
        else
            let trimmed = key.Trim()

            if trimmed = "" then
                false
            elif trimmed.StartsWith("on", StringComparison.OrdinalIgnoreCase) then
                false
            elif trimmed.Equals("style", StringComparison.OrdinalIgnoreCase) then
                // CSS injection vector (`expression()`, `url(javascript:…)`) plus
                // content-spoofing. Out of scope for the allowlist.
                false
            else
                trimmed.StartsWith("data-", StringComparison.Ordinal)
                || trimmed.StartsWith("aria-", StringComparison.Ordinal)

    /// Reject attribute values carrying C0 control characters (tab excepted) or
    /// angle brackets. A host's attribute encoder normally escapes these, but the
    /// "render verbatim" contract some sinks offer means the floor cannot lean on
    /// it. The key allowlist above is only half the guard; this is the other half.
    let isSafeAttributeValue (value: string) : bool =
        if isNull value then
            false
        else
            let mutable ok = true

            for ch in value do
                if int ch < 0x20 && ch <> '\t' then
                    ok <- false
                elif ch = '<' || ch = '>' then
                    ok <- false

            ok

    /// Filter a candidate attribute map to the entries passing both predicates, so
    /// a host never has to trust a hand-built map. Unused by `Trust.harden` (the
    /// IDL does not model the node envelope, so no attribute map reaches it) — this
    /// is part of the reusable floor, as `isAllowedAttributeKey` already was.
    let sanitizeAttributes (attrs: Map<string, string>) : Map<string, string> =
        attrs
        |> Map.filter (fun k v -> isAllowedAttributeKey k && isSafeAttributeValue v)

    /// Defence-in-depth markdown scrub: strip dangerous element blocks
    /// (`<script>` / `<iframe>` / `<object>` / `<embed>` / `<form>` / `<link>` /
    /// `<meta>`), strip inline `on*=` event handlers from tag interiors, and
    /// neutralise `javascript:` / `vbscript:` scheme substrings to `about:blank`.
    /// Approximate substring sweep (NOT a full HTML parser) — the floor, not the
    /// ceiling; benign markdown passes through unchanged.
    ///
    /// ⚠️ PRECONDITION: this is defence in depth over output that is already
    /// escaped by construction, NOT a general-purpose HTML sanitiser. It anchors
    /// `on*=` on leading whitespace and splits attributes on the first `=` or
    /// quote, which is sound for that narrow shape and bypassable on arbitrary
    /// untrusted HTML. Do not call it as the sole sanitiser for untrusted input.
    let scrubMarkdown (md: string) : string =
        if isNull md || md = "" then
            ""
        else
            let mutable result = md

            let dangerousElements =
                [ "script"; "iframe"; "object"; "embed"; "form"; "link"; "meta" ]

            for tag in dangerousElements do
                // `tag` is lower-case ASCII, so both tags are valid `indexOfFolded` needles.
                let openTag = "<" + tag
                let closeTag = "</" + tag + ">"
                let mutable keepGoing = true

                while keepGoing do
                    let i = indexOfFolded result openTag 0

                    if i < 0 then
                        keepGoing <- false
                    else
                        let j = indexOfFolded result closeTag i

                        if j >= 0 then
                            result <- result.Remove(i, j + closeTag.Length - i)
                        else
                            let endBracket = result.IndexOf('>', i)

                            if endBracket >= 0 then
                                result <- result.Remove(i, endBracket - i + 1)
                            else
                                result <- result.Substring(0, i)
                                keepGoing <- false

            // Strip inline `on*="…"` event handlers. Scan for whitespace-then-`on`
            // occurring INSIDE a start-tag interior (between an unescaped `<` and its
            // `>`) and remove up to the value's terminator.
            //
            // The tag-interior anchor is load-bearing: without it the scan matches the
            // leading-whitespace-`on<letter>` pattern in ordinary prose — "one", "only",
            // "once", "onto", "online" — and the boolean-attribute branch below then
            // deletes the word from body text. A real handler can only appear inside a
            // tag, so restricting to tag interiors is both correct and drops the false
            // positive.
            let stripEventHandlers (input: string) : string =
                let mutable s = input
                let mutable keepGoing = true

                while keepGoing do
                    // Scanned on `s` itself, folding per unit (`foldAscii`), so every index
                    // below is an index into the string it is applied to (Phase 291).
                    let mutable found = -1
                    let mutable i = 0
                    let mutable insideTag = false

                    while i < s.Length - 3 && found < 0 do
                        let ch = s[i]

                        if ch = '<' then
                            insideTag <- true
                        elif ch = '>' then
                            insideTag <- false
                        elif
                            insideTag
                            && (ch = ' ' || ch = '\t' || ch = '\n')
                            && foldAscii s[i + 1] = 'o'
                            && foldAscii s[i + 2] = 'n'
                            && Char.IsLetter s[i + 3]
                        then
                            found <- i

                        i <- i + 1

                    if found < 0 then
                        keepGoing <- false
                    else
                        let eq = s.IndexOf('=', found)
                        let nextSpace = s.IndexOfAny([| ' '; '\t'; '\n'; '>' |], found + 1)

                        if eq < 0 || (nextSpace >= 0 && nextSpace < eq) then
                            // No `=` — a boolean attribute like `onload`. Strip the name only.
                            let stopAt = if nextSpace >= 0 then nextSpace else s.Length
                            s <- s.Remove(found, stopAt - found)
                        else
                            let mutable v = eq + 1

                            while v < s.Length && (s[v] = ' ' || s[v] = '\t') do
                                v <- v + 1

                            let stopAt =
                                if v < s.Length && (s[v] = '\'' || s[v] = '"') then
                                    let q = s[v]
                                    let close = s.IndexOf(q, v + 1)
                                    if close >= 0 then close + 1 else s.Length
                                else
                                    let candidate = s.IndexOfAny([| ' '; '\t'; '\n'; '>' |], v)
                                    if candidate >= 0 then candidate else s.Length

                            s <- s.Remove(found, stopAt - found)

                s

            result <- stripEventHandlers result

            // Neutralise dangerous scheme substrings by REPLACEMENT, rescanning from the
            // start after each hit.
            //
            // Deleting the match instead (the Phase 321 shape) is unsound: deletion
            // splices the surrounding text together and can form a fresh occurrence out
            // of the halves, and a single non-rescanning pass then emits it. That is not
            // theoretical — `javascjavascript:ript:alert(1)` came out of the old code as
            // a live `javascript:alert(1)`, the sanitiser constructing the exact payload
            // it exists to remove (Phase 96). Interposing `about:blank` cannot form the
            // pattern, so replacement both terminates and cannot resurrect.
            for proto in [ "javascript:"; "vbscript:" ] do
                let mutable keepGoing = true
                // The cursor advances past every replacement, so the loop is bounded by the
                // string's length however the input is shaped. Resuming at the cursor loses
                // nothing: `about:blank` holds neither a `j` nor a `v`, so no match can begin
                // inside it, and none can end inside it either (the only `:` in it comes
                // after `about`, which is not the tail of either scheme).
                let mutable searchFrom = 0

                while keepGoing do
                    let i = indexOfFolded result proto searchFrom

                    if i < 0 then
                        keepGoing <- false
                    else
                        // `about:blank` keeps the surrounding attribute structurally valid.
                        result <- result.Substring(0, i) + "about:blank" + result.Substring(i + proto.Length)
                        searchFrom <- i + "about:blank".Length

            result
