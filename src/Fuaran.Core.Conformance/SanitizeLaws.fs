namespace Fuaran.Core

open System

/// The sanitisation family (Phase 349): the floor each of the six `Fuaran.Core.Idl.Sanitize`
/// functions claims, held over generated adversarial strings and over the vectors the example
/// tests pinned, at a `SanitizeWitness` — `SanitizeWitness.core` for Core's own floor, or a host's
/// copy built from its own functions.
module internal SanitizeLaws =

    // ---- the floor, restated independently of the code it judges ----------------------------------
    //
    // Every predicate below is written from the CLAIM in `Sanitize.fs`'s doc comments, not by calling
    // into it, so a scrubber and its law cannot share a defect by sharing a helper.

    /// ASCII case fold of one UTF-16 unit — the comparison a browser makes for every token here.
    let private fold (c: char) : char =
        if c >= 'A' && c <= 'Z' then char (int c + 32) else c

    /// `needle` (lower-case ASCII) occurs in `hay` under the ASCII fold.
    let private containsFolded (hay: string) (needle: string) : bool =
        let mutable found = false
        let mutable i = 0

        while not found && i <= hay.Length - needle.Length do
            let mutable k = 0

            while k < needle.Length && fold hay[i + k] = needle[k] do
                k <- k + 1

            if k = needle.Length then found <- true else i <- i + 1

        found

    /// The element openers the markdown scrub claims to strip.
    let dangerousOpeners: string list =
        [ "<script"; "<iframe"; "<object"; "<embed"; "<form"; "<link"; "<meta" ]

    /// The scheme substrings the markdown scrub claims to neutralise.
    let dangerousSchemes: string list = [ "javascript:"; "vbscript:" ]

    /// An inline handler the scrub claims to strip: inside a start tag (after an unclosed `<`), a
    /// space, tab or line feed, then `on` under the fold, then a letter.
    let private hasTagHandler (s: string) : bool =
        let mutable inside = false
        let mutable found = false
        let mutable i = 0

        while not found && i < s.Length - 3 do
            let c = s[i]

            if c = '<' then
                inside <- true
            elif c = '>' then
                inside <- false
            elif
                inside
                && (c = ' ' || c = '\t' || c = '\n')
                && fold s[i + 1] = 'o'
                && fold s[i + 2] = 'n'
                && Char.IsLetter s[i + 3]
            then
                found <- true

            i <- i + 1

        found

    /// What the markdown floor forbids in an output, named, or `None`.
    let markdownBreach (s: string) : string option =
        match dangerousOpeners |> List.tryFind (containsFolded s) with
        | Some o -> Some(sprintf "a live %s element" o)
        | None ->
            match dangerousSchemes |> List.tryFind (containsFolded s) with
            | Some p -> Some(sprintf "a live %s scheme" p)
            | None ->
                if hasTagHandler s then
                    Some "an inline on* handler inside a tag"
                else
                    None

    /// The URL schemes the floor accepts.
    let allowedSchemes: Set<string> =
        Set.ofList [ "http"; "https"; "mailto"; "tel"; "ftp"; "sftp" ]

    /// What an ACCEPTED URL's floor forbids, named, or `None`: an edge unit at or below U+0020, a
    /// tab / line feed / carriage return anywhere, two leading units drawn from `/` and `\` with no
    /// scheme, or a scheme (the text before the first `:` with no `/ ? #` ahead of it, units at or
    /// below U+0020 removed, folded) outside the allowed set.
    let urlBreach (u: string) : string option =
        let colon = u.IndexOf ':'

        let schemeEnd =
            if colon < 0 then
                None
            else
                let before = u.Substring(0, colon)

                if before.IndexOfAny([| '/'; '?'; '#' |]) >= 0 then
                    None
                else
                    Some(
                        before
                        |> Seq.filter (fun c -> int c > 0x20)
                        |> Seq.map fold
                        |> Seq.toArray
                        |> String
                    )

        let slashish (c: char) = c = '/' || c = '\\'

        if u.Length > 0 && (u[0] <= ' ' || u[u.Length - 1] <= ' ') then
            Some "an edge unit at or below U+0020 survived"
        elif u |> Seq.exists (fun c -> c = '\t' || c = '\n' || c = '\r') then
            Some "a tab, line feed or carriage return survived"
        else
            match schemeEnd with
            | Some s when not (allowedSchemes.Contains s) -> Some(sprintf "the scheme %A is not allowed" s)
            | Some _ -> None
            | None when u.Length >= 2 && slashish u[0] && slashish u[1] -> Some "a protocol-relative URL survived"
            | None -> None

    /// The markdown scrub's length bound: removals only shrink, `javascript:` is replaced by the
    /// eleven units of `about:blank`, and `vbscript:` (nine units) by the same eleven — so the output
    /// is at most the input plus two units per nine of it.
    let markdownBound (input: string) : int = input.Length + 2 * (input.Length / 9)

    // ---- the vectors ------------------------------------------------------------------------------

    /// The fragments an adversarial string is spliced from: element and handler halves that removal
    /// can join, scheme halves that replacement or removal can join, case and control-unit
    /// obfuscations, the dotted capital I whose lower case is two units, and a lone surrogate.
    let private fragments: string list =
        [ "<"
          ">"
          "</"
          "script"
          "<script>"
          "</script>"
          "<scr"
          "ipt>"
          "<iframe>"
          "<IFRAME"
          "<form>"
          "<meta "
          "<a href=\""
          "\">"
          " on"
          " onclick=\"x\""
          "onerror="
          "=\"\""
          "\""
          "'"
          "java"
          "JaVa"
          "script:"
          "SCRIPT:"
          "javascript:"
          "vbsc"
          "vbscript:"
          ":"
          "/"
          "\\"
          "?"
          "#"
          "\t"
          "\n"
          "\r"
          " "
          "\u000b"
          "\u0000"
          "İ"
          string (char 0xD800)
          "a"
          "x"
          "data-x"
          "aria-"
          "style"
          "http"
          "https://h/p"
          "mailto:"
          "data:"
          "file:"
          "//h" ]

    /// The vectors the example tests pinned (`IdlCertificationTests`), and the two splices Phase 349's
    /// first run of this family found: removing one element can join the halves of another, and
    /// removing a handler can join the halves of an element name.
    let pinnedMarkdown: string list =
        [ "hello <script>alert(1)</script> world javascript:x"
          "Updated hourly."
          "javascjavascript:ript:alert(1)"
          "<a href=\"javascjavascript:ript:alert(1)\">x</a>"
          "vbscvbscript:ript:x"
          "<a href=\"x\" onclick=\"alert(1)\">x</a>"
          "<img src=\"x\" onerror=alert(1)>"
          "<div onload>x</div>"
          "one only once onto online"
          "JAVASCRIPT:x"
          "a javascript:1 b JAVASCRIPT:2 c"
          "İİİjavascript:x"
          "<SCRIPT>alert(1)</SCRIPT>after"
          "<scr<iframe>ipt>alert(1)</script>"
          "<scri onx=\"\"pt>alert(1)" ]

    /// URLs the floor must refuse — every one the example tests pinned, with their obfuscations.
    let refusedUrls: string list =
        [ "javascript:alert(1)"
          "JAVA\tSCRIPT:alert(1)"
          " \t java\nscript:x"
          "\u0001javascript:x"
          "vbscript:x"
          "data:text/html,x"
          "file:///etc/passwd"
          "//evil.com/x"
          "/\\evil.com"
          "\\\\evil.com"
          "\\/evil.com"
          "/\t/evil" ]

    /// URLs the floor must accept unchanged — safe and already normalised.
    let acceptedUrls: string list =
        [ "/about"
          "https://example.com"
          "mailto:a@b.com"
          "tel:+1"
          "relative/path?q=1#f"
          "\\single-backslash"
          "" ]

    /// Benign text: no `<`, no `:`, so nothing in it is a floor's business.
    let private benignFragments: string list =
        [ "a"
          "b"
          " "
          "one"
          "only"
          "on"
          "\n"
          "\t"
          "script"
          "java"
          "x=1"
          "İ"
          "é" ]

    let private splice (rng: LawKit.Draws) (alphabet: string list) (most: int) : string =
        let n = 1 + rng.IntBelow most
        String.Join("", [ for _ in 1..n -> rng.Choose alphabet ])

    /// What a splice is cut around: one of the names the markdown floor forbids.
    let private spliceTargets: string list = dangerousOpeners @ dangerousSchemes

    /// What a splice interposes: something the scrub removes or replaces, so that a scrub which
    /// stops after one pass joins the halves around it into the name it was cut from.
    let private interposers: string list =
        [ "<iframe>"
          "<form></form>"
          "<meta>"
          "<object>x</object>"
          "<embed"
          " onx=\"\""
          " onclick='x'"
          "javascript:"
          "vbscript:" ]

    /// A forbidden name cut at a drawn point with a removable piece interposed, between drawn
    /// fragments: the shape of every resurrection the scrub has been found to construct (Phase 96's
    /// spliced scheme, Phase 349's spliced element). A uniform splice of fragments reaches it about
    /// once in a hundred thousand draws, so the family draws it on purpose.
    let private spliced (rng: LawKit.Draws) : string =
        let t = rng.Choose spliceTargets
        let k = 1 + rng.IntBelow(t.Length - 1)

        splice rng fragments 3
        + t.Substring(0, k)
        + rng.Choose interposers
        + t.Substring k
        + rng.Choose [ ">"; "x>"; ">alert(1)</script>"; "(1)"; "" ]

    /// The sanitisation laws (Phase 349), over a `SanitizeWitness`:
    ///
    ///  - **an accepted URL passes the floor** — no edge unit at or below U+0020, no tab, line feed or
    ///    carriage return, no protocol-relative start, a scheme only from the allowed set — and is
    ///    never longer than its input;
    ///  - **the URL floor is idempotent** — an accepted URL is accepted again, unchanged; and
    ///    `sanitizeUrlOrBlank` is `sanitizeUrl` with `about:blank` for a refusal, idempotent itself;
    ///  - **the URL floor refuses every dangerous URL and keeps every safe one** — fixed vectors;
    ///  - **the attribute floor is exactly its claim** — a key is allowed only as a `data-` / `aria-`
    ///    key that is neither an `on*` handler nor `style`; a value is safe exactly when it carries no
    ///    C0 unit but tab and no angle bracket; `sanitizeAttributes` keeps exactly the entries both
    ///    pass, and is idempotent;
    ///  - **the scrubbed markdown passes the floor** — no live dangerous element, no live
    ///    `javascript:` / `vbscript:`, no `on*` handler inside a tag;
    ///  - **the markdown scrub is idempotent, bounded, and leaves benign text alone** — scrubbing the
    ///    output again changes nothing, the output is at most `markdownBound` units, and text with no
    ///    `<` and no `:` comes back unchanged.
    ///
    /// Every iteration draws a uniform splice of fragments and a cut-and-interpose splice of a
    /// forbidden name; the first also runs every pinned vector. Every arm is
    /// built on every iteration, so the family carries no guard.
    let sanitizeLaws (w: SanitizeWitness) (seed: int) (iterations: int) : LawResult list =
        let urlFloor =
            LawKit.LawCell "an accepted URL passes the floor and is never longer than its input"

        let urlIdem =
            LawKit.LawCell
                "the URL floor is idempotent, and sanitizeUrlOrBlank is sanitizeUrl or about:blank, idempotent itself"

        let urlFixed =
            LawKit.LawCell "the URL floor refuses every dangerous URL and keeps every safe one unchanged"

        let attrs =
            LawKit.LawCell
                "the attribute floor is exactly its claim, and sanitizeAttributes keeps exactly the entries both predicates pass"

        let mdFloor =
            LawKit.LawCell "scrubbed markdown passes the floor (no live element, scheme or tag handler)"

        let mdShape =
            LawKit.LawCell "the markdown scrub is idempotent, within its length bound, and leaves benign text unchanged"

        let url (at: string -> string) (input: string) =
            let r = w.SanitizeUrl input

            match r with
            | Some u ->
                urlFloor.Check(
                    (urlBreach u).IsNone && u.Length <= input.Length,
                    fun () ->
                        at (
                            sprintf
                                "sanitizeUrl %A accepted %A: %s"
                                input
                                u
                                (urlBreach u |> Option.defaultValue "longer than its input")
                        )
                )

                urlIdem.Check(
                    w.SanitizeUrl u = Some u,
                    fun () -> at (sprintf "sanitizeUrl %A gave %A, and again %A" input u (w.SanitizeUrl u))
                )
            | None -> urlFloor.Saw()

            let blank = w.SanitizeUrlOrBlank input
            let expected = r |> Option.defaultValue "about:blank"

            urlIdem.Check(
                blank = expected && w.SanitizeUrlOrBlank blank = blank,
                fun () ->
                    at (
                        sprintf
                            "sanitizeUrlOrBlank %A gave %A (sanitizeUrl: %A), and again %A"
                            input
                            blank
                            r
                            (w.SanitizeUrlOrBlank blank)
                    )
            )

        let markdown (at: string -> string) (input: string) =
            let out = w.ScrubMarkdown input

            mdFloor.Check(
                (markdownBreach out).IsNone,
                fun () ->
                    at (sprintf "scrubMarkdown %A gave %A: %s" input out (markdownBreach out |> Option.defaultValue ""))
            )

            let again = w.ScrubMarkdown out

            mdShape.Check(
                again = out && out.Length <= markdownBound input,
                fun () ->
                    at (
                        sprintf
                            "scrubMarkdown %A gave %A (bound %d), and again %A"
                            input
                            out
                            (markdownBound input)
                            again
                    )
            )

        let attributes (at: string -> string) (pairs: (string * string) list) =
            for k, v in pairs do
                let trimmed = k.Trim()

                let keyClaim =
                    trimmed.Length > 0
                    && (trimmed.StartsWith("data-", StringComparison.Ordinal)
                        || trimmed.StartsWith("aria-", StringComparison.Ordinal))
                    && not (trimmed.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                    && not (trimmed.Equals("style", StringComparison.OrdinalIgnoreCase))

                let valueClaim =
                    v |> Seq.forall (fun c -> (int c >= 0x20 || c = '\t') && c <> '<' && c <> '>')

                attrs.Check(
                    w.IsAllowedAttributeKey k = keyClaim && w.IsSafeAttributeValue v = valueClaim,
                    fun () ->
                        at (
                            sprintf
                                "key %A allowed %b (claim %b); value %A safe %b (claim %b)"
                                k
                                (w.IsAllowedAttributeKey k)
                                keyClaim
                                v
                                (w.IsSafeAttributeValue v)
                                valueClaim
                        )
                )

            let input = Map.ofList pairs
            let kept = w.SanitizeAttributes input

            let expected =
                input
                |> Map.filter (fun k v -> w.IsAllowedAttributeKey k && w.IsSafeAttributeValue v)

            attrs.Check(
                kept = expected && w.SanitizeAttributes kept = kept,
                fun () -> at (sprintf "sanitizeAttributes %A kept %A, expected %A" input kept expected)
            )

        LawKit.run iterations seed (fun rng i at ->
            if i = 0 then
                for u in refusedUrls do
                    urlFixed.Check(
                        w.SanitizeUrl u = None && w.SanitizeUrlOrBlank u = "about:blank",
                        fun () -> at (sprintf "the dangerous URL %A was accepted as %A" u (w.SanitizeUrl u))
                    )

                    url at u

                for u in acceptedUrls do
                    urlFixed.Check(
                        w.SanitizeUrl u = Some u,
                        fun () -> at (sprintf "the safe URL %A gave %A" u (w.SanitizeUrl u))
                    )

                    url at u

                for m in pinnedMarkdown do
                    markdown at m

                attributes
                    at
                    [ "data-test", "plain value"
                      "aria-label", "tab\there"
                      " data-pad ", "x"
                      "onclick", "x"
                      "ONLOAD", "x"
                      "style", "x"
                      "class", "x"
                      "data-angle", "a<b"
                      "data-nul", "nul\u0001here"
                      "", "x" ]

            url at (splice rng fragments 6)
            markdown at (splice rng fragments 8)
            markdown at (spliced rng)

            let benign = splice rng benignFragments 8

            mdShape.Check(
                w.ScrubMarkdown benign = benign,
                fun () -> at (sprintf "benign text %A was scrubbed to %A" benign (w.ScrubMarkdown benign))
            )

            attributes at [ for _ in 1 .. 1 + rng.IntBelow 4 -> splice rng fragments 3, splice rng fragments 3 ])

        LawKit.results [ urlFloor; urlIdem; urlFixed; attrs; mdFloor; mdShape ]
