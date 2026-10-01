namespace Fuaran.Core

/// WHY a JSONL line was refused (Phase 296) — the closed set of faults the one line scanner
/// (`OpStream.Jsonl`) and the readers built on it report. Before this type the scanner was lenient
/// where `Wire.Json.parse` refuses: an unquoted value read as a string with its first and last
/// characters cut off (`"prevHash":null` read as `"ul"`), a non-hex `\u` digit went through
/// arithmetic, and a line ending after `:` indexed past the end. Each is now a named refusal, so a
/// stream that reads `Ok` is one the scanner actually parsed.
[<RequireQualifiedAccess>]
type JsonlFaultReason =
    /// The line's first non-blank character is not `{` — the line is not a JSON object.
    | NotAnObject
    /// The line ends before its object does — after a `:`, after a `,`, or before the closing `}`.
    | Truncated
    /// A member key is not a string token.
    | ExpectedKey
    /// A member key is not followed by `:`.
    | ExpectedColon
    /// A member has no value (`"a":}` or `"a":,`).
    | MissingValue
    /// A member value is not followed by `,` or `}`.
    | ExpectedCommaOrBrace
    /// Non-blank content follows the object's closing `}`.
    | TrailingContent
    /// A string token is not closed before the line ends.
    | UnterminatedString
    /// An array or object value is not closed before the line ends.
    | UnterminatedContainer
    /// An escape the JSON grammar does not have: an unknown escape letter, a `\u` whose four digits
    /// are not all hex, or a surrogate escape that is not half of a high-low pair. The escape as read.
    | InvalidEscape of escape: string
    /// A bare value that is not `true`, `false`, `null` or a JSON number. The token as read.
    | InvalidLiteral of token: string
    /// A member the reader requires to be a string holds another kind of value (`"prevHash":null`,
    /// `"id":12`).
    | ExpectedString of field: string
    /// A member the reader requires to be an integer holds something else — a string, a fraction, an
    /// exponent, a hex spelling (`"seq":0x2`), or a value outside the 32-bit range.
    | ExpectedInteger of field: string
    /// A member the reader requires to be an array of strings holds something else.
    | ExpectedStringArray of field: string
    /// A member the reader requires is absent.
    | MissingField of field: string
    /// A snapshot line that is not the first line of the stream — one snapshot line, at the head, is
    /// the only compacted shape `compact` writes.
    | SnapshotNotAtHead
    /// The line is well-formed JSON and its reader refused what it says — a witness decode `Error`,
    /// an actor kind this build does not know, a node colliding with one already read. The reader's
    /// reason, verbatim.
    | Refused of reason: string

/// One refused JSONL line (Phase 296): the 1-based `Line` number counted over EVERY line of the
/// text (blank lines included, so it is the number an editor shows), the scanner's own 0-based
/// `Position` within that line, and the `Reason`.
type JsonlFault =
    { Line: int
      Position: int
      Reason: JsonlFaultReason }

/// Render a `JsonlFault` for a log line or an `Error` string (Phase 296).
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module JsonlFault =

    /// The reason's text, without the line and position.
    let reasonText (r: JsonlFaultReason) : string =
        match r with
        | JsonlFaultReason.NotAnObject -> "expected a JSON object"
        | JsonlFaultReason.Truncated -> "the line ends inside the object"
        | JsonlFaultReason.ExpectedKey -> "expected a member key (a string)"
        | JsonlFaultReason.ExpectedColon -> "expected ':'"
        | JsonlFaultReason.MissingValue -> "a member has no value"
        | JsonlFaultReason.ExpectedCommaOrBrace -> "expected ',' or '}'"
        | JsonlFaultReason.TrailingContent -> "content after the closing '}'"
        | JsonlFaultReason.UnterminatedString -> "unterminated string"
        | JsonlFaultReason.UnterminatedContainer -> "unterminated container"
        | JsonlFaultReason.InvalidEscape e -> "invalid escape " + e
        | JsonlFaultReason.InvalidLiteral t -> "invalid literal '" + t + "'"
        | JsonlFaultReason.ExpectedString f -> "field " + f + " is not a string"
        | JsonlFaultReason.ExpectedInteger f -> "field " + f + " is not an integer"
        | JsonlFaultReason.ExpectedStringArray f -> "field " + f + " is not an array of strings"
        | JsonlFaultReason.MissingField f -> "missing field " + f
        | JsonlFaultReason.SnapshotNotAtHead -> "a snapshot line that is not the first line of the stream"
        | JsonlFaultReason.Refused reason -> reason

    /// `line N: <reason> (position P)` — the `line N:` prefix every JSONL reader's `Error` string
    /// has carried since Phase 252, now 1-based over all lines.
    let toString (f: JsonlFault) : string =
        sprintf "line %d: %s (position %d)" f.Line (reasonText f.Reason) f.Position

/// The scanner's internal refusal, raised deep in the scan and caught at the one boundary that turns
/// it into a `JsonlFault` — never seen by a caller.
exception internal JsonlScanFault of position: int * reason: JsonlFaultReason

/// One scanned JSONL line (Phase 296): its 1-based number, its text, and its top-level members as
/// raw spans (first-wins on a repeated key). Read it through the `OpStream.Jsonl` accessors.
type JsonlLine =
    internal
        { Number: int
          Text: string
          Fields: (string * string * int) list }
