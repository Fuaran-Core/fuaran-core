namespace Fuaran.Core.Idl

open Fuaran.Core

/// Source LITERALS for IDL-authored text (Phase 292) — the one escaper every emitter
/// splices a vocabulary's text through, with one policy per target.
///
/// **Why one module.** The generator held five escapers (`fsAttrStr`, `fsDefaultStr`,
/// `tsSourceStr`, `fsStringLit`, the F* target's `lit`), each written for the site that
/// first needed it and each a little different: the default-value one escaped neither CR nor
/// LF (so the module's own line-ending normalisation then rewrote a CR INSIDE the literal,
/// and the F# and TypeScript omit tests compared against different strings), and several
/// sites spliced text with no escaper at all — the discriminator into F# and JavaScript
/// string literals, deprecation prose and a kind's category into comments, where a line
/// break ends the comment and the rest of the text is live source. An `idl.json` is
/// UNTRUSTED input (DECISIONS D95): [[Declare.errors]] refuses a vocabulary that carries
/// such text at every loading path, and this module is the second half of the same rule —
/// a vocabulary built in code, which no loader sees, still cannot put a byte of source into
/// a generated module THROUGH THE TEXT THE IDL AUTHORS: its identifiers, wire spellings,
/// discriminator, categories, docs and deprecation prose.
///
/// **The trust boundary — what this module does NOT cover (Phase 387, DECISIONS D124).** Some
/// declared text is not IDL-authored data but HOST SOURCE, spliced verbatim by design: a
/// [[THosted]] slot's `FSharp` type and its `Encode` / `Decode` expressions, a [[TFn]] slot's
/// [[ClosureSig]] host types and placeholder, and every `support.json` entry (a doc block, a
/// splice, a kind projection, the host prelude). Escaping them would destroy them — they are
/// code — so none passes through here; [[Declare.errors]] checks a hosted slot only for its
/// declared wire form and format, and `SupportArtifact.ofJson` checks only shape. Whoever
/// supplies them supplies source to the generated module, trusted exactly as far as the project
/// that compiles it trusts its own code, so a vocabulary or support file from an untrusted
/// party must not carry them. The guarantee above is scoped to IDL-authored text and stops
/// there; `IdlCertificationTests` pins the boundary by planting a hosted body that would be
/// unsafe as data and asserting it reaches the generated module verbatim.
///
/// **The policies.** A string LITERAL (F#, an F# attribute argument, TypeScript) escapes the
/// quote and the backslash, names `\n` `\r` `\t`, and writes every other C0 control, U+0085,
/// U+2028, U+2029 and an unpaired surrogate as `\uXXXX`, so the literal's VALUE is the
/// authored string exactly and its source text holds no line break and nothing a UTF-8 file
/// cannot carry. Neither F# nor F* can spell an unpaired surrogate in a string literal (both
/// probed: the F# compiler reads `"\uD800"` as U+FFFD, and the pinned F* prover refuses it as
/// a syntax error), so in those two policies one becomes U+FFFD; TypeScript spells it. A
/// declaration that would need it is refused by [[Declare.errors]], and the F# and F*
/// backends refuse such a VALUE ([[isWellFormed]]) before it reaches here. A COMMENT (an F# `///` or `//` line, a
/// TypeScript `//` line) cannot escape anything, so the text is split at every line break an
/// author can type or an editor may break on, one comment line per authored line, and a
/// character no comment can carry (a C0 control other than tab, an unpaired surrogate,
/// U+FFFE, U+FFFF) becomes U+FFFD. A TypeScript object KEY is bare when JavaScript can spell
/// it and a string literal otherwise.
///
/// Every policy is the identity on the text the generator has always emitted — a name, a
/// plain wire string — so a vocabulary that carries none of these characters emits
/// byte-for-byte what it did.
[<RequireQualifiedAccess>]
module SourceLit =

    // Compared as code-unit values: Fable cannot write an unpaired surrogate char literal into its
    // output, and the guard in SourceLiteralTests holds every literal under src/ to that.
    let private isHigh (c: char) = int c >= 0xD800 && int c <= 0xDBFF
    let private isLow (c: char) = int c >= 0xDC00 && int c <= 0xDFFF

    let private isLineBreak (c: char) =
        c = '\n' || c = '\r' || c = '\u0085' || c = '\u2028' || c = '\u2029'

    /// `\uXXXX`, lower-case hex — the spelling the generator's literals have always used.
    let private uEscape (c: char) : string =
        let hex = "0123456789abcdef"
        let n = int c

        "\\u"
        + string hex[(n >>> 12) &&& 0xF]
        + string hex[(n >>> 8) &&& 0xF]
        + string hex[(n >>> 4) &&& 0xF]
        + string hex[n &&& 0xF]

    /// The body of a literal delimited by `quote`. `unpaired` spells an unpaired surrogate.
    let private quotedBody (quote: char) (unpaired: char -> string) (s: string) : string =
        let b = System.Text.StringBuilder()
        let mutable i = 0

        while i < s.Length do
            let c = s[i]

            if isHigh c && i + 1 < s.Length && isLow s[i + 1] then
                b.Append(c).Append(s[i + 1]) |> ignore
                i <- i + 1
            elif isHigh c || isLow c then
                b.Append(unpaired c) |> ignore
            else
                match c with
                | c when c = quote -> b.Append('\\').Append(c) |> ignore
                | '\\' -> b.Append("\\\\") |> ignore
                | '\n' -> b.Append("\\n") |> ignore
                | '\r' -> b.Append("\\r") |> ignore
                | '\t' -> b.Append("\\t") |> ignore
                | c when c < ' ' || isLineBreak c -> b.Append(uEscape c) |> ignore
                | c -> b.Append(c) |> ignore

            i <- i + 1

        b.ToString()

    /// The body of a double-quoted literal.
    let private literalBody (unpaired: char -> string) (s: string) : string = quotedBody '"' unpaired s

    /// Whether `s` is well-formed UTF-16 — no unpaired surrogate. F# and F* cannot spell an
    /// unpaired surrogate in a string literal (measured: the F# compiler reads `"\uD800"` as
    /// U+FFFD), so a caller that must reproduce a value exactly refuses one that fails this.
    let isWellFormed (s: string) : bool =
        let mutable ok = true
        let mutable i = 0

        while ok && i < s.Length do
            if isHigh s[i] && i + 1 < s.Length && isLow s[i + 1] then
                i <- i + 2
            elif isHigh s[i] || isLow s[i] then
                ok <- false
            else
                i <- i + 1

        ok

    /// An F# string literal, quotes included, whose value is `s` exactly when `s` is
    /// [[isWellFormed]]. An unpaired surrogate becomes U+FFFD, written as the escape, which
    /// is what the F# compiler would make of any spelling of it.
    let fsString (s: string) : string =
        "\"" + literalBody (fun _ -> uEscape '\uFFFD') s + "\""

    /// An F# ATTRIBUTE argument (`System.Obsolete("…")`) — an F# string literal, so the
    /// [[fsString]] policy; named for its site so an attribute splice reads as one.
    let fsAttribute (s: string) : string = fsString s

    /// A TypeScript (JavaScript) double-quoted string literal whose value is `s` exactly.
    let tsString (s: string) : string = "\"" + literalBody uEscape s + "\""

    /// The single-quoted spelling of [[tsString]] — the same policy with `'` as the escaped
    /// delimiter — for the generated runtime's own single-quoted literals, which a vocabulary
    /// whose text needs no escaping keeps byte-for-byte.
    let tsStringSingle (s: string) : string = "'" + quotedBody '\'' uEscape s + "'"

    /// An F* string literal. F* cannot spell an unpaired surrogate, which becomes U+FFFD.
    let fstarString (s: string) : string =
        "\"" + literalBody (fun _ -> uEscape '\uFFFD') s + "\""

    /// Whether JavaScript can spell `s` as a bare identifier (`$type`, `kind`).
    let tsIsIdentifier (s: string) : bool =
        s.Length > 0
        && (System.Char.IsLetter s[0] || s[0] = '_' || s[0] = '$')
        && s |> Seq.forall (fun c -> System.Char.IsLetterOrDigit c || c = '_' || c = '$')

    /// A TypeScript object-literal / interface KEY: bare when JavaScript can spell it, a
    /// [[tsString]] otherwise.
    let tsKey (s: string) : string =
        if tsIsIdentifier s then s else tsString s

    /// One comment line's text with every character no comment can carry replaced by U+FFFD.
    let private commentSafe (s: string) : string =
        let b = System.Text.StringBuilder()
        let mutable i = 0

        while i < s.Length do
            let c = s[i]

            if isHigh c && i + 1 < s.Length && isLow s[i + 1] then
                b.Append(c).Append(s[i + 1]) |> ignore
                i <- i + 1
            elif (c < ' ' && c <> '\t') || isHigh c || isLow c || c = '\uFFFE' || c = '\uFFFF' then
                b.Append('\uFFFD') |> ignore
            else
                b.Append(c) |> ignore

            i <- i + 1

        b.ToString()

    /// Text as COMMENT LINES, one per authored line: split at `\r\n`, `\r`, `\n`, U+0085,
    /// U+2028 and U+2029, trailing whitespace dropped, blank lines dropped at either end and
    /// kept inside. Text that is only whitespace yields nothing.
    let private commentLines (text: string) : string list =
        let breaks = [| '\r'; '\n'; '\u0085'; '\u2028'; '\u2029' |]

        text.Replace("\r\n", "\n").Split(breaks)
        |> Array.map (fun l -> commentSafe (l.TrimEnd()))
        |> Array.toList
        |> List.skipWhile (fun l -> l = "")
        |> List.rev
        |> List.skipWhile (fun l -> l = "")
        |> List.rev

    /// The text of F# comment lines (`///` doc lines, `//` lines) — the caller writes the
    /// marker. Whether a `///` block is XML is the caller's decision; [[fsDocXml]] is the
    /// encoding for one that is.
    let fsDocLines (text: string) : string list = commentLines text

    /// `<` and `&` encoded, for a `///` block the F# compiler reads as XML.
    let fsDocXml (line: string) : string =
        line.Replace("&", "&amp;").Replace("<", "&lt;")

    /// The text of TypeScript `//` comment lines — the caller writes the marker.
    let tsCommentLines (text: string) : string list = commentLines text
