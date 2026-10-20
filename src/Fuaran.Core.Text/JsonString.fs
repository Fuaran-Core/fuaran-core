namespace Fuaran.Core

/// THE string escape of the spine (Phase 287; DECISIONS.md "the spine owns the string-escaping rule"),
/// in the one place it is written (Phase 531, DECISIONS.md D2 as amended). Exactly three classes are
/// escaped and nothing else: `"` as `\"`, `\` as `\\`, and every control character `U+0000`–`U+001F`
/// as `\u00xx` with LOWER-CASE hex — including `\n`, `\r` and `\t`, which have NO short form. It is
/// byte-for-byte the UI host's `CanonicalJson.appendRawString` and the TypeScript twin's escaper, so a
/// hash pre-image that carries a string means one thing on every host.
///
/// Every writer in the spine spells a string through this module: `Json.escape` and every named
/// `Rendering` in `Fuaran.Core.Wire`, the actor pre-image, every chained line, snapshot and capture in
/// `Fuaran.Core.OpStream` (`Jsonl.quote`), and the DAG's node lines above it. Until Phase 531 the
/// op-stream layer carried a value-identical copy, held equal to Wire's by `StringEscapeVectors`; the
/// vectors now pin this one body, on both pipelines.
///
/// `escapeLegacyInto` / `quoteLegacy` are the spelling of `0.30.0` through `0.32.0`
/// (`EncodingProfile.V1`, `StringEscape.ShortControls`): `\n`, `\r` and `\t` short, every other
/// control character `\u00xx`. FROZEN — a body of its own, never written in terms of the live escape,
/// so no later change to the live path can move a `V1` byte; `EncodingProfileVectors` pins it against
/// bytes the published `0.30.0` binary produced. Verification and migration only: a chain hashed under
/// the old bytes still verifies and rehashes.
///
/// One loop on both pipelines. Under Fable `Wire` once found the first escapable character with a
/// native regex `[<Emit>]` (Phase 365, measured faster under node on long escape-free strings); the
/// kernel takes no `Fable.Core` reference (D2 as amended: FSharp.Core only), and a second spelling of
/// the escaped class beside the loop is the drift this module exists to close, so the loop is the rule.
[<RequireQualifiedAccess>]
module JsonString =

    /// The `\u00xx` spelling of each control character `U+0000`–`U+001F`, lower-case hex, built once
    /// so that no escape formats a number.
    let private controlEscapes: string[] =
        let hex = "0123456789abcdef"
        Array.init 0x20 (fun c -> "\\u00" + string hex[c >>> 4] + string hex[c &&& 0xF])

    /// The index of the first character of `s` the rule escapes — `"`, `\` or a control character
    /// below `U+0020` — or `-1` when there is none (Phase 365).
    let private firstEscapable (s: string) : int =
        let mutable i = 0
        let mutable found = -1

        while found < 0 && i < s.Length do
            let code = int s[i]

            if code < 0x20 || code = 0x22 || code = 0x5C then
                found <- i
            else
                i <- i + 1

        found

    /// Append the escaped body of `s` to `sb`, given that `first` is the index of its first escapable
    /// character: the clean prefix and every later clean run as one ranged append each (Phase 365).
    let private appendEscapedFrom (sb: System.Text.StringBuilder) (s: string) (first: int) : unit =
        if first > 0 then
            sb.Append(s, 0, first) |> ignore

        // `start` is where the clean run not yet appended begins.
        let mutable start = first

        for i in first .. s.Length - 1 do
            let code = int s[i]

            if code < 0x20 || code = 0x22 || code = 0x5C then
                if i > start then
                    sb.Append(s, start, i - start) |> ignore

                (if code = 0x22 then sb.Append("\\\"")
                 elif code = 0x5C then sb.Append("\\\\")
                 else sb.Append(controlEscapes[code]))
                |> ignore

                start <- i + 1

        if s.Length > start then
            sb.Append(s, start, s.Length - start) |> ignore

    /// The escaped body of `s`, quotes excluded. A string with nothing to escape is returned AS IT IS
    /// (Phase 365): only a string that has an escapable character is copied, its clean runs appended
    /// whole and only the escaped characters spelled one at a time.
    let escape (s: string) : string =
        let first = firstEscapable s

        if first < 0 then
            s
        else
            let sb = System.Text.StringBuilder(s.Length + 16)
            appendEscapedFrom sb s first
            sb.ToString()

    /// `escape s` appended to `sb` (Phase 365) — the same bytes, written into the caller's own builder,
    /// so a writer that is already building a document makes no intermediate string per value.
    let escapeInto (sb: System.Text.StringBuilder) (s: string) : unit =
        let first = firstEscapable s

        if first < 0 then
            sb.Append(s) |> ignore
        else
            appendEscapedFrom sb s first

    /// `"` + `escape s` + `"` — `s` spelled as a JSON string literal, in one builder when there is
    /// anything to escape and by one concatenation when there is not.
    let quote (s: string) : string =
        let first = firstEscapable s

        if first < 0 then
            "\"" + s + "\""
        else
            let sb = System.Text.StringBuilder(s.Length + 18)
            sb.Append('"') |> ignore
            appendEscapedFrom sb s first
            sb.Append('"') |> ignore
            sb.ToString()

    /// The FROZEN `0.30.0` escape (`EncodingProfile.V1`) appended to `sb`: `\n`, `\r` and `\t` as the
    /// short escapes, every other control character as lower-case `\u00xx`, `"` and `\` escaped.
    let escapeLegacyInto (sb: System.Text.StringBuilder) (s: string) : unit =
        for ch in s do
            match ch with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when int c < 0x20 -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append(c) |> ignore

    /// `s` spelled as a JSON string literal under the frozen `0.30.0` escape (`escapeLegacyInto`).
    let quoteLegacy (s: string) : string =
        let sb = System.Text.StringBuilder()
        sb.Append('"') |> ignore
        escapeLegacyInto sb s
        sb.Append('"') |> ignore
        sb.ToString()
