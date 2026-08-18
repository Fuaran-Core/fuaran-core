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
/// gap survive. The adversarial cases in `IdlUiTests` now pin the behaviours that
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

    /// The sanitised URL, or `None` when the scheme is rejected / unknown /
    /// protocol-relative. Default-deny: an unknown scheme is refused.
    let sanitizeUrl (url: string) : string option =
        if isNull url then
            None
        else
            let trimmed = url.Trim()

            if trimmed = "" then
                Some trimmed
            elif
                (extractScheme trimmed).IsNone
                && (trimmed.StartsWith "//" || trimmed.StartsWith "/\\")
            then
                // Protocol-relative (`//host`) resolves off-origin — reject.
                None
            else
                match extractScheme trimmed with
                | None -> Some trimmed
                | Some scheme when rejectedUrlSchemes.Contains scheme -> None
                | Some scheme when allowedUrlSchemes.Contains scheme -> Some trimmed
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
                let openTag = "<" + tag
                let closeTag = "</" + tag + ">"
                let mutable keepGoing = true

                while keepGoing do
                    let i = result.IndexOf(openTag, StringComparison.OrdinalIgnoreCase)

                    if i < 0 then
                        keepGoing <- false
                    else
                        let j = result.IndexOf(closeTag, i, StringComparison.OrdinalIgnoreCase)

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
                    let lower = s.ToLowerInvariant()
                    let mutable found = -1
                    let mutable i = 0
                    let mutable insideTag = false

                    while i < lower.Length - 3 && found < 0 do
                        let ch = lower[i]

                        if ch = '<' then
                            insideTag <- true
                        elif ch = '>' then
                            insideTag <- false
                        elif
                            insideTag
                            && (ch = ' ' || ch = '\t' || ch = '\n')
                            && lower[i + 1] = 'o'
                            && lower[i + 2] = 'n'
                            && Char.IsLetter lower[i + 3]
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

                while keepGoing do
                    let i = result.ToLowerInvariant().IndexOf(proto, StringComparison.Ordinal)

                    if i < 0 then
                        keepGoing <- false
                    else
                        // `about:blank` keeps the surrounding attribute structurally valid.
                        result <- result.Substring(0, i) + "about:blank" + result.Substring(i + proto.Length)

            result

/// The codegen trust boundary over the `IdlValue` tree (Phase 321 task 2 + the
/// task-3 wiring): gate every `Custom` against an allowlist + content-hash, and
/// run `Sanitize.*` over declared URL / markdown fields, producing a hardened
/// value that is inert-by-construction when scaffolded to host source.
module Trust =

    /// An allowlisted `Custom` component: which module/component may resolve live,
    /// and the content-hash it must carry.
    type AllowEntry =
        { ModuleId: string
          ComponentId: string
          Hash: string }

    /// Which fields carry values that must be sanitised at codegen time. A URL
    /// field is a `Binding<string>` whose `Static` value is a URL; a markdown
    /// field is a `TextSource` whose `Literal` text is markdown. Keyed by
    /// `(kindTag, fieldName)`.
    type HardenPolicy =
        { Allowlist: AllowEntry list
          UrlFields: Set<string * string>
          MarkdownFields: Set<string * string> }

    /// The default UI-tier policy: `Link.href` / `Image.src` are URLs;
    /// `Markdown.text` is markdown. Callers pass their own allowlist.
    let uiPolicy (allowlist: AllowEntry list) : HardenPolicy =
        { Allowlist = allowlist
          UrlFields = Set.ofList [ "Link", "href"; "Image", "src" ]
          MarkdownFields = Set.ofList [ "Markdown", "text" ] }

    /// The gate decision for a `Custom` node.
    type CustomGate =
        | Allowed
        | InertPlaceholder of reason: string

    let private fieldOf (name: string) (fields: (string * IdlValue) list) : IdlValue option =
        fields |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

    /// Gate a `Custom` node's fields. Unhashed → inert; hashed-but-not-allowlisted
    /// → inert; allowlisted + hash matches → live; allowlisted + hash MISMATCH →
    /// inert under `StrictReplay` / `Enforced`, live (advisory) under
    /// `AdvisoryWarning`.
    let gateCustom (allowlist: AllowEntry list) (fields: (string * IdlValue) list) : CustomGate =
        let str name =
            match fieldOf name fields with
            | Some(VStr s) -> s
            | _ -> ""

        let moduleId = str "moduleId"
        let componentId = str "componentId"

        match fieldOf "contentHash" fields with
        | Some(VRecord hfields) ->
            let hget name =
                match fieldOf name hfields with
                | Some(VStr s) -> s
                | Some(VEnum s) -> s
                | _ -> ""

            let hash = hget "hash"
            let strictness = hget "strictness"

            match
                allowlist
                |> List.tryFind (fun e -> e.ModuleId = moduleId && e.ComponentId = componentId)
            with
            | None -> InertPlaceholder "not in codegen allowlist"
            | Some e when e.Hash = hash -> Allowed
            | Some _ when strictness = "AdvisoryWarning" -> Allowed
            | Some _ -> InertPlaceholder "content-hash mismatch"
        | _ -> InertPlaceholder "unhashed Custom"

    /// The inert labelled placeholder a gated-out `Custom` becomes — a benign
    /// `Markdown` node (renders text, never a live call), preserving the node id.
    let private inertPlaceholder (id: string) (moduleId: string) (componentId: string) (reason: string) : IdlValue =
        let label = sprintf "[inert placeholder: %s/%s — %s]" moduleId componentId reason
        VNode(id, "Markdown", [ "text", VUnion("Literal", [ "text", VStr label ]) ])

    /// Sanitise a URL field value (a `Binding<string>`): a `Static` string literal
    /// is routed through `Sanitize.sanitizeUrlOrBlank`; other binding cases (Query
    /// / State / …) carry no literal URL and pass through.
    let private sanitiseUrlValue (v: IdlValue) : IdlValue =
        match v with
        | VUnion("Static", [ ("value", VStr s) ]) -> VUnion("Static", [ "value", VStr(Sanitize.sanitizeUrlOrBlank s) ])
        | other -> other

    /// Scrub a markdown field value (a `TextSource`): a `Literal` string is routed
    /// through `Sanitize.scrubMarkdown`; other cases pass through.
    let private scrubMarkdownValue (v: IdlValue) : IdlValue =
        match v with
        | VUnion("Literal", [ ("text", VStr s) ]) -> VUnion("Literal", [ "text", VStr(Sanitize.scrubMarkdown s) ])
        | other -> other

    /// Harden an authored `IdlValue` for the codegen boundary: gate every `Custom`
    /// node to inert-by-default, and sanitise every declared URL / markdown field,
    /// recursively over the whole tree. The result scaffolds / encodes to
    /// inert-by-construction, sanitised output (Phase 321 tasks 2 + 3).
    let rec harden (policy: HardenPolicy) (v: IdlValue) : IdlValue =
        match v with
        | VNode(id, "Custom", fields) ->
            match gateCustom policy.Allowlist fields with
            | Allowed -> VNode(id, "Custom", fields |> List.map (fun (n, fv) -> n, harden policy fv))
            | InertPlaceholder reason ->
                let str name =
                    match fieldOf name fields with
                    | Some(VStr s) -> s
                    | _ -> ""

                inertPlaceholder id (str "moduleId") (str "componentId") reason
        | VNode(id, kindTag, fields) ->
            let hardenField (fieldName: string) (fv: IdlValue) : IdlValue =
                let sanitised =
                    if Set.contains (kindTag, fieldName) policy.UrlFields then
                        sanitiseUrlValue fv
                    elif Set.contains (kindTag, fieldName) policy.MarkdownFields then
                        scrubMarkdownValue fv
                    else
                        fv

                harden policy sanitised

            VNode(id, kindTag, fields |> List.map (fun (n, fv) -> n, hardenField n fv))
        // Phase 698 — an enveloped node hardens as its bare form does, plus its
        // envelope values. It DELEGATES to the arms above rather than repeating
        // them: a second copy of the Custom gate here is exactly how an enveloped
        // `Custom` node would quietly stop being gated. When the gate replaces the
        // node with the inert placeholder the envelope goes with it — the
        // placeholder is a fresh inert node, not a re-dressed version of the one
        // that was refused.
        | VNodeEnv(id, envelope, kindTag, fields) ->
            let env = envelope |> List.map (fun (n, fv) -> n, harden policy fv)

            match harden policy (VNode(id, kindTag, fields)) with
            | VNode(hid, hTag, hFields) when hTag = kindTag -> VNodeEnv(hid, env, hTag, hFields)
            | replaced -> replaced
        | VList xs -> VList(xs |> List.map (harden policy))
        | VUnion(tag, fields) -> VUnion(tag, fields |> List.map (fun (n, fv) -> n, harden policy fv))
        | VRecord fields -> VRecord(fields |> List.map (fun (n, fv) -> n, harden policy fv))
        | VMap entries -> VMap(entries |> List.map (fun (k, fv) -> k, harden policy fv))
        | other -> other

    /// Harden then scaffold an authored node to F# source (the codegen boundary
    /// end to end): the emitted source constructs an inert-by-construction,
    /// sanitised tree, prefixed with the Phase 321 provenance stamp. `wireHash` /
    /// `actor` feed the stamp.
    let scaffoldFSharp
        (policy: HardenPolicy)
        (idl: Idl)
        (wireHash: string)
        (actor: string)
        (v: IdlValue)
        : Result<string, string> =
        Gen.fsharpValue idl TNode (harden policy v)
        |> Result.map (fun body -> Gen.provenanceHeader "//" wireHash actor + "\n" + body)
