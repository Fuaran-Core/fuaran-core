namespace Fuaran.Core

/// WHY a JSONL line was refused (Phase 296) — the closed set of faults the one line scanner
/// (`Jsonl`) and the readers built on it report. Before this type the scanner was lenient
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
    /// The line's actor names nobody (Phase 315) — the member that carried it, and why. Declared
    /// LAST so every existing case keeps its tag.
    | ActorInvalid of field: string * reason: ActorInvalid
    /// An array value's item is not followed by `,` or `]` (Phase 466: a container value is read by
    /// the grammar at every depth). The four Phase-466 cases follow every earlier one, so each
    /// existing case keeps its tag.
    | ExpectedCommaOrBracket
    /// A member key read twice in one object (Phase 466, the strict grammar's rule, DECISIONS.md
    /// D157) — at the repeated key's opening quote, once its value is read. `path` is the repeated
    /// member's, spelled as `DecodePath.render` spells one (`$["op"]["id"]`). Keys are compared
    /// after unescaping.
    | RepeatedKey of path: string
    /// A raw control character (U+0000 to U+001F) inside a string (Phase 466) — at the character.
    /// `path` is the member or item the string is in; for a key, the object that holds it.
    | RawControl of path: string
    /// A raw surrogate code unit that is not half of a high-low pair (Phase 466) — at the unit.
    /// `path` as for `RawControl`. An unpaired surrogate ESCAPE is `InvalidEscape`, as it always was.
    | LoneSurrogate of path: string
    /// A DAG checkpoint sidecar line (Phase 529; a `Refused` sentence before) whose `seq` is not `0`:
    /// a checkpoint is the snapshot at sequence zero of the history that begins at its node. The four
    /// checkpoint cases follow every earlier one, so each existing case keeps its tag.
    | CheckpointNotAtSeqZero of seq: int
    /// A DAG checkpoint sidecar line whose `prevHash` — the checkpoint's node — is empty (Phase 529).
    | CheckpointWithoutNode
    /// A DAG checkpoint sidecar line that is a chain-only snapshot (`"stateHashed":false`) (Phase 529):
    /// a checkpoint's seal always binds its state.
    | CheckpointChainOnly
    /// A DAG checkpoint sidecar line that is not a snapshot line at all (`"snapshot":true` absent)
    /// (Phase 529).
    | NotACheckpoint

/// One refused JSONL line (Phase 296): the 1-based `Line` number counted over EVERY line of the
/// text (blank lines included, so it is the number an editor shows), the scanner's own 0-based
/// `Position` within that line, and the `Reason`.
type JsonlFault =
    {
        /// 1-based line number over every line of the text, blank lines included.
        Line: int
        /// 0-based character offset within the line where the scanner refused; `0` for a fault about
        /// the line as a whole, such as a missing member.
        Position: int
        /// Why the line was refused.
        Reason: JsonlFaultReason
    }

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
        | JsonlFaultReason.ActorInvalid(field, ActorInvalid.EmptyId) -> "the actor in " + field + " has an empty id"
        | JsonlFaultReason.ExpectedCommaOrBracket -> "expected ',' or ']'"
        | JsonlFaultReason.RepeatedKey path -> "repeated key at " + path
        | JsonlFaultReason.RawControl path -> "unescaped control character in a string at " + path
        | JsonlFaultReason.LoneSurrogate path -> "unpaired surrogate in a string at " + path
        | JsonlFaultReason.CheckpointNotAtSeqZero seq ->
            sprintf "a checkpoint is the snapshot at seq 0; this line is at seq %d" seq
        | JsonlFaultReason.CheckpointWithoutNode -> "a checkpoint names its node in prevHash, and this one is empty"
        | JsonlFaultReason.CheckpointChainOnly -> "a checkpoint's seal binds its state; a chain-only line is not one"
        | JsonlFaultReason.NotACheckpoint ->
            "not a checkpoint line: a checkpoint is a snapshot line (\"snapshot\":true)"

    /// `line N: <reason> (position P)` — the `line N:` prefix every JSONL reader's `Error` string
    /// has carried since Phase 252, now 1-based over all lines.
    let toString (f: JsonlFault) : string =
        sprintf "line %d: %s (position %d)" f.Line (reasonText f.Reason) f.Position

/// One stored op a stream load read but its witness could not decode (Phase 416): where it sits and
/// what the witness said. Every other part of its line parsed, and the op's text is exactly what the
/// store holds.
type UndecodableOp =
    {
        /// The lane whose file holds the op, or `None` for a load of one text.
        Lane: string option
        /// 1-based line of that text, counted over every line, blank lines included — what an editor
        /// shows.
        Line: int
        /// The content id the store gives the op: a DAG node's id, or a linear record's stored `hash`.
        NodeId: string
        /// The witness's own `Decode` error, verbatim.
        Reason: string
    }

/// Why a stream load refused (Phase 416) — the one answer every JSONL op-stream load in
/// `Fuaran.Core.OpStream` and `Fuaran.Core.OpStream.Dag` gives, where each used to give a string.
/// `'Break` is the integrity fault of the stream the load reads: `ChainBreak` for a linear stream,
/// `DagBreak` for a DAG, `Dag.LaneBreak` for a lane store. A structural load (one that does not
/// verify) never answers `Broken`.
///
/// The cases separate the three things a host must tell apart. `Unreadable`, `DuplicateLane`,
/// `Collision` and `UnreadableCheckpoint` say the bytes are not a store. `Broken` says they are a
/// store that does not verify: damage. `Undecodable` says every line parsed, every content id and
/// parent verified (on a verifying load), and the only thing wrong is that the witness could not
/// decode some ops: the signature of a store written by a newer host, whose op vocabulary this build
/// predates. A load answers the first that holds in that order, so a decode failure in a store that
/// is also damaged is not reported beside the damage.
[<RequireQualifiedAccess>]
type StreamLoadFault<'Break> =
    /// A line the one scanner, or the record reader over it, refused: the lane whose text holds it
    /// (`None` for a load of one text) and the fault, by its line.
    | Unreadable of lane: string option * fault: JsonlFault
    /// The stream parsed and does not verify: the first break, exactly as the stream's own walker
    /// reports it.
    | Broken of 'Break
    /// The stream parsed (and, on a verifying load, verified) and the witness refused one or more ops.
    /// EVERY such op, in lane and then line order.
    | Undecodable of sites: UndecodableOp list
    /// Two of the lane texts handed to a lane load carry one lane id.
    | DuplicateLane of lane: string
    /// Two lanes hold one content id for different content (parents modulo order, actor or op): the
    /// node and the two lanes, sorted ordinally.
    | Collision of nodeId: string * lanes: string list
    /// A line of a DAG's checkpoint sidecar was refused: the fault, by its sidecar line.
    | UnreadableCheckpoint of fault: JsonlFault

/// Render a `StreamLoadFault` (Phase 416). Each case renders as the string the load answered before
/// the fault was typed, where it answered one, so a host that printed the message keeps its bytes.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module StreamLoadFault =

    /// One undecodable op as `line N: <the witness's reason> (position 0)` — the bytes a JSONL reader
    /// gave a witness decode `Error` before Phase 416 — prefixed `lane L: ` when it has a lane.
    let siteText (s: UndecodableOp) : string =
        let at = sprintf "line %d: %s (position 0)" s.Line s.Reason

        match s.Lane with
        | Some lane -> "lane " + lane + ": " + at
        | None -> at

    /// The fault as text, with `renderBreak` rendering a `Broken` stream's break. A load of one text
    /// renders `Unreadable` as `JsonlFault.toString` and a single undecodable op as `siteText`, the
    /// pre-416 bytes; several undecodable ops are their `siteText`s joined by `; `.
    let toStringWith (renderBreak: 'Break -> string) (f: StreamLoadFault<'Break>) : string =
        match f with
        | StreamLoadFault.Unreadable(None, fault) -> JsonlFault.toString fault
        | StreamLoadFault.Unreadable(Some lane, fault) -> "lane " + lane + ": " + JsonlFault.toString fault
        | StreamLoadFault.Broken b -> renderBreak b
        | StreamLoadFault.Undecodable sites -> sites |> List.map siteText |> String.concat "; "
        | StreamLoadFault.DuplicateLane lane -> "lane " + lane + " is given twice"
        | StreamLoadFault.Collision(nodeId, lanes) ->
            "node "
            + nodeId
            + " is held with different content by lanes "
            + String.concat ", " lanes
        | StreamLoadFault.UnreadableCheckpoint fault -> "checkpoint sidecar: " + JsonlFault.toString fault

    /// A linear stream load's fault as text — `Jsonl.readVerified`'s message for a break,
    /// `OpStream.fromJsonlVerified: chain breaks at record N — <reason>`, byte for byte as before
    /// Phase 416 (the text keeps the reader's pre-0.39.0 name, which hosts match on).
    let toString (f: StreamLoadFault<ChainBreak>) : string =
        toStringWith
            (fun (b: ChainBreak) ->
                sprintf
                    "OpStream.fromJsonlVerified: chain breaks at record %d — %s"
                    b.Index
                    (ChainBreakReason.toString b.Reason))
            f

/// WHY a JSONL writer refused to embed a raw span (Phase 301). A writer embeds a domain encoding —
/// an op, a captured value, a snapshot's state — verbatim, and the reader hands the decoder the
/// value's exact span; so the line round-trips only when the encoding IS one JSON value that sits on
/// one line with nothing around it. Before this type the writers embedded whatever `Encode` produced,
/// and an encoding with a line break, or a space either side, read back changed — and failed
/// `verifyChain` / `verifyCaptures` — with nothing at the write to say so.
[<RequireQualifiedAccess>]
type JsonlWriteFaultReason =
    /// The encoding carries a line break (`\n` or `\r`) at `position`: the line would end inside it.
    | MultiLine of position: int
    /// Whitespace before or after the one value: the reader keeps the value's span without it, so
    /// the decoder would be handed different bytes than the chain hashed.
    | NotTrimStable
    /// The scanner does not read the encoding as one JSON value — no value, an invalid literal, an
    /// unterminated string or container, a bad escape, or a second value after the first. The
    /// scanner's own reason.
    | Unreadable of reason: JsonlFaultReason

/// One refused embedding (Phase 301): the 1-based `Line` of the output the writer would have
/// produced (the record's, capture's or node's position plus one; a snapshot line is line 1), the
/// `Member` whose span was refused (`op`, `value` or `state`), and the `Reason`.
type JsonlWriteFault =
    {
        /// 1-based line of the output the refused item would have occupied.
        Line: int
        /// The member refused: an embedded span (`op`, `value`, `state`) or, since Phase 473, a string
        /// the writer quotes itself (`actor`, `prevHash`, `hash`, `eff`, `det`, `id`, `parents`).
        Member: string
        /// What is wrong with the encoding. The writer stops at the first refused item and emits nothing.
        Reason: JsonlWriteFaultReason
    }

/// Render a `JsonlWriteFault` for a log line or an `Error` string (Phase 301).
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module JsonlWriteFault =

    /// The reason's text, without the line and member.
    let reasonText (r: JsonlWriteFaultReason) : string =
        match r with
        | JsonlWriteFaultReason.MultiLine p -> sprintf "the encoding carries a line break at position %d" p
        | JsonlWriteFaultReason.NotTrimStable -> "whitespace before or after the encoded value"
        | JsonlWriteFaultReason.Unreadable r -> "not one JSON value: " + JsonlFault.reasonText r

    /// `line N: member M: <reason>`.
    let toString (f: JsonlWriteFault) : string =
        sprintf "line %d: member %s: %s" f.Line f.Member (reasonText f.Reason)

/// The scanner's internal refusal, raised deep in the scan and caught at the one boundary that turns
/// it into a `JsonlFault` — never seen by a caller.
exception internal JsonlScanFault of position: int * reason: JsonlFaultReason

/// One step of the path a Phase-466 refusal names: a member by its decoded key, an item by its
/// zero-based index.
[<RequireQualifiedAccess>]
type internal JsonlSeg =
    | Key of string
    | Index of int

/// One open container of the structural walk (Phase 466): an object's keys so far and the member
/// being read (its key and the key's opening quote), or an array's current index. `Seen` is an
/// object's only; an array's is never read.
type internal JsonlFrame =
    { IsObject: bool
      Seen: System.Collections.Generic.HashSet<string>
      mutable Key: string
      mutable KeyAt: int
      mutable Index: int }

/// One scanned JSONL line (Phase 296): its 1-based number, its text, and its top-level members as
/// raw spans, every key once (Phase 466). Read it through the `Jsonl` accessors.
type JsonlLine =
    internal
        { Number: int
          Text: string
          Fields: (string * string * int) list }

/// The op stream's JSONL (Phase 332's concern file; the public module itself since Phase 516): THE line
/// scanner every chained reader reads through and its field readers, the checked line writer, the
/// carrying witness and the settling step a load of any shape reads through, and the op stream's own
/// record writer (`write` / `tryWrite`) and readers (`read`, `readWithSnapshots`, `readLegacyActor`,
/// `readVerified`). Self-contained and Fable-clean: it runs under both pipelines.
[<RequireQualifiedAccess>]
module Jsonl =

    /// JSON string spelling for every line, snapshot, capture and envelope this package emits —
    /// the ONE escaper this package carries (`JsonString.quote`, the D2 copy of `Wire.Json.escape`,
    /// every control character as `\u00xx` since Phase 287). Fable-clean.
    let internal jstr (s: string) : string = JsonString.quote s

    /// `s` spelled as a JSON string literal by this package's ONE escaper (Phase 388): `"`, `\`,
    /// and every control character `U+0000`–`U+001F` as lower-case `\u00xx`, with NO short form
    /// for `\n` / `\r` / `\t` (Phase 287) — the spelling of every line, snapshot and capture this
    /// package writes. Public so a writer outside this package (the DAG's) spells its members
    /// with the same body rather than a copy. Value-identical to `Wire.Json.escape`, held so by
    /// `StringEscapeVectors`. Fable-clean.
    let quote (s: string) : string = JsonString.quote s

    /// THE chained line (Phase 530): a record's canonical hash payload — one JSON object — with its
    /// link, `prevHash` and `hash`, spliced in as its last two members. Every chained kind writes this
    /// way — an op record, a positional or keyed capture, a strict snapshot — so the bytes a line
    /// carries are the bytes its hash was computed over, by construction rather than by two writers
    /// spelling one envelope twice.
    let internal linked (payload: string) (prev: string) (hash: string) : string =
        payload.Substring(0, payload.Length - 1)
        + ",\"prevHash\":"
        + jstr prev
        + ",\"hash\":"
        + jstr hash
        + "}"

    /// One record line around the op's embedded encoding — the one line format both writers emit: the
    /// canonical `{seq, actor, op}` payload, linked.
    let internal recordLine (r: OpRecord<'Op>) (opJson: string) : string =
        linked (StreamConfig.canonical.Payload r.Seq r.Actor opJson) r.PrevHash r.Hash

    /// One JSON object per line. The op payload is embedded as raw JSON (the domain's
    /// own `Encode` output), so a round-trip preserves it byte-for-byte.
    let write (w: StreamWitness<'Op, 'State, 'Rej>) (records: OpRecord<'Op> list) : string =
        records
        |> List.map (fun r -> recordLine r (w.Encode r.Op))
        |> String.concat "\n"


    let private fail (pos: int) (reason: JsonlFaultReason) : 'a = raise (JsonlScanFault(pos, reason))

    let private escapeText (s: string) (i: int) (len: int) = s.Substring(i, min len (s.Length - i))

    /// The line reader's placement of the text kernel's string refusal (Phase 531). The grammar is the
    /// kernel's (`JsonSyntax.readString`, the one string reader of the spine); the vocabulary and the
    /// positions are this reader's, unchanged since Phase 466 (D157), and `proofs/OpStreamLoad.fst`'s
    /// `string_tail` states them clause for clause. An escape the grammar does not have is
    /// `InvalidEscape` at its backslash — the escape as read, six characters for a `\u` one. A `\u`
    /// HIGH surrogate not followed at once by its low half is `InvalidEscape` at that escape's
    /// backslash, its twelve characters, whatever followed it (the end of the line included). An
    /// unpaired RAW surrogate is `LoneSurrogate` at the unit, once what follows a raw high one is
    /// known — the end of the line is `UnterminatedString`, a raw control its own refusal, an escape
    /// the grammar does not have that escape's. A raw U+0000 to U+001F is `RawControl` at the
    /// character. The end of the line inside the token is `UnterminatedString` at its opening quote.
    /// `path` names the member or item the string is in; it is rendered only for a refusal.
    let private stringFault (s: string) (start: int) (path: unit -> string) (r: JsonStringRefusal) : 'a =
        if r.PendingHigh >= 0 && r.PendingHighEscaped then
            fail r.PendingHigh (JsonlFaultReason.InvalidEscape(escapeText s r.PendingHigh 12))
        else
            match r.Fault with
            | JsonStringFault.Unterminated
            | JsonStringFault.UnterminatedEscape -> fail start JsonlFaultReason.UnterminatedString
            | JsonStringFault.TruncatedUnicodeEscape
            | JsonStringFault.BadHexDigit -> fail r.UnitAt (JsonlFaultReason.InvalidEscape(escapeText s r.UnitAt 6))
            | JsonStringFault.UnknownEscape e -> fail r.UnitAt (JsonlFaultReason.InvalidEscape("\\" + string e))
            | JsonStringFault.RawControl _ -> fail r.UnitAt (JsonlFaultReason.RawControl(path ()))
            | JsonStringFault.HighWithoutLow -> fail r.PendingHigh (JsonlFaultReason.LoneSurrogate(path ()))
            | JsonStringFault.LowWithoutHigh when s[r.UnitAt] = '\\' ->
                fail r.UnitAt (JsonlFaultReason.InvalidEscape(escapeText s r.UnitAt 6))
            | JsonStringFault.LowWithoutHigh -> fail r.UnitAt (JsonlFaultReason.LoneSurrogate(path ()))
            // Not reachable: the line reader declares no bound, and no string holds Int32.MaxValue code
            // points. Placed as the reader's own refusal rather than an exception, so the map is total.
            | JsonStringFault.TooLong ->
                fail r.UnitAt (JsonlFaultReason.Refused "a string past the line reader's bound")

    /// Index just past the string token whose opening quote is at `start`, held to the strict grammar
    /// (`JsonSyntax.skipString`; refusals placed by `stringFault`).
    let internal skipString (s: string) (start: int) (path: unit -> string) : int =
        match JsonSyntax.skipString s start System.Int32.MaxValue with
        | Ok stop -> stop
        | Error r -> stringFault s start path r

    /// The value of the string token whose opening quote is at `start`, and the index just past it
    /// (`JsonSyntax.readString`, one pass: an escape-free token is one `Substring`, and one with an
    /// escape is decoded into `scratch`); refused exactly as `skipString` refuses.
    let private readString
        (scratch: System.Text.StringBuilder)
        (s: string)
        (start: int)
        (path: unit -> string)
        : struct (string * int) =
        match JsonSyntax.readString s start System.Int32.MaxValue scratch with
        | Ok r -> r
        | Error r -> stringFault s start path r

    /// A bare value's token held to the literal grammar: `true`, `false`, `null`, or a JSON number
    /// (`-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?`, the text kernel's `JsonSyntax.isNumber`).
    let private isLiteral (t: string) : bool =
        t = "true" || t = "false" || t = "null" || JsonSyntax.isNumber t

    /// Index just past the bare value at `start`: the token up to `,`, `}`, `]` or whitespace,
    /// held to the literal grammar.
    let private skipLiteral (s: string) (start: int) : int =
        let n = s.Length
        let mutable i = start

        while i < n
              && not (let c = s[i] in c = ',' || c = '}' || c = ']' || JsonSyntax.isWhitespace c) do
            i <- i + 1

        let token = s.Substring(start, i - start)

        if not (isLiteral token) then
            fail start (JsonlFaultReason.InvalidLiteral token)

        i

    /// A path as `DecodePath.render` spells one (Phase 466): `$`, then `["key"]` (the key under
    /// `JsonString.quote`) or `[i]` per step — the `prefix`, then the first `count` open frames,
    /// each at its current member or item.
    let private renderPath (prefix: JsonlSeg list) (frames: ResizeArray<JsonlFrame>) (count: int) : string =
        let sb = System.Text.StringBuilder("$")

        let step (seg: JsonlSeg) =
            match seg with
            | JsonlSeg.Key k -> sb.Append('[').Append(JsonString.quote k).Append(']') |> ignore
            | JsonlSeg.Index i -> sb.Append('[').Append(string i).Append(']') |> ignore

        for seg in prefix do
            step seg

        for j in 0 .. count - 1 do
            let f = frames[j]

            step (
                if f.IsObject then
                    JsonlSeg.Key f.Key
                else
                    JsonlSeg.Index f.Index
            )

        sb.ToString()

    let private rootPath () = "$"

    /// THE structural walk: the index just past the object or array value at `start`, read by
    /// the grammar at every depth (Phase 466; until then a container value was balanced and its
    /// strings checked, and the rest of its grammar was the decoder's): each object's members and
    /// each array's items, their separators, every string and bare literal, and each object's keys
    /// pairwise distinct after unescaping — a repeated key is refused at its opening quote once its
    /// value is read, so a refusal inside the value wins (`Wire.Json`'s order, D157.2). The walk
    /// keeps an explicit stack of open containers, so no nesting depth exhausts the call stack.
    /// `proofs/OpStreamLoad.fst`'s `skip_val` / `obj_members` / `arr_items` are this walk, the stack
    /// as their recursion, and its `members_loop` is the depth-0 frame below.
    ///
    /// **The depth-0 frame (Phase 530).** The container at `start` is the walk's depth 0. Given
    /// `spans`, each of its members (or items) is recorded as `(key, raw span, position)` as its
    /// value ends — an item's key is its decimal index — so the readers of a line, of an actor
    /// object and of a string array read the spans this one walk recorded, where each used to scan
    /// its container with a loop of its own. With `line` set, the container at `start` is a line's
    /// OWN object (`membersOf`): the line ending inside that frame is `Truncated` at the end of the
    /// line, and ending inside one of its members' values is `UnterminatedContainer` at that value's
    /// opening — the two answers the line's own loop and `skipValue` gave before. Otherwise a line
    /// ending anywhere inside is `UnterminatedContainer` at `start`, the outermost opening, as the
    /// balanced walk answered.
    let private walk
        (s: string)
        (start: int)
        (prefix: JsonlSeg list)
        (line: bool)
        (spans: ResizeArray<string * string * int>)
        : int =
        let n = s.Length
        let frames = ResizeArray<JsonlFrame>()
        // The one buffer the walk decodes an escaped key into.
        let scratch = System.Text.StringBuilder()
        // The path of a value in the innermost container, and of a key (the container's own).
        let valuePath () = renderPath prefix frames frames.Count

        let keyPath () =
            renderPath prefix frames (frames.Count - 1)

        let mutable i = start
        let mutable result = -1
        let mutable first = true
        // 0: a member is due; 1: an item is due; 2: a value has just ended at `i`;
        // 3: a value is due at `i` (no whitespace before it); 4: open the container at `i`;
        // 5: the innermost container's close was consumed.
        let mutable state = 4
        // Where the depth-0 frame's current member value starts, and the opening a line ending
        // inside a container is refused at.
        let mutable valueAt = start
        let mutable openAt = start

        let skipWs () =
            while i < n && JsonSyntax.isWhitespace s[i] do
                i <- i + 1

        let ended () =
            if line && frames.Count = 1 then
                fail i JsonlFaultReason.Truncated
            else
                fail openAt JsonlFaultReason.UnterminatedContainer

        while result < 0 do
            match state with
            | 4 ->
                let isObject = s[i] = '{'

                if line && frames.Count = 1 then
                    openAt <- i

                frames.Add
                    { IsObject = isObject
                      Seen =
                        if isObject then
                            System.Collections.Generic.HashSet<string>()
                        else
                            Unchecked.defaultof<_>
                      Key = null
                      KeyAt = 0
                      Index = 0 }

                i <- i + 1
                first <- true
                state <- if isObject then 0 else 1
            | 0 ->
                skipWs ()

                if i >= n then
                    ended ()

                match s[i] with
                | '}' when first ->
                    i <- i + 1
                    state <- 5
                | '"' ->
                    let top = frames[frames.Count - 1]
                    let struct (key, e) = readString scratch s i keyPath
                    top.Key <- key
                    top.KeyAt <- i
                    i <- e
                    skipWs ()

                    if i >= n then
                        ended ()

                    if s[i] <> ':' then
                        fail i JsonlFaultReason.ExpectedColon

                    i <- i + 1
                    skipWs ()

                    if i >= n then
                        ended ()

                    state <- 3
                | _ -> fail i JsonlFaultReason.ExpectedKey
            | 1 ->
                skipWs ()

                if i >= n then
                    ended ()

                if s[i] = ']' then
                    if first then
                        i <- i + 1
                        state <- 5
                    else
                        fail i JsonlFaultReason.MissingValue
                else
                    state <- 3
            | 3 ->
                if frames.Count = 1 then
                    valueAt <- i

                match s[i] with
                | '"' ->
                    i <- skipString s i valuePath
                    state <- 2
                | '{'
                | '[' -> state <- 4
                | ','
                | '}'
                | ']' -> fail i JsonlFaultReason.MissingValue
                | _ ->
                    i <- skipLiteral s i
                    state <- 2
            | 5 ->
                frames.RemoveAt(frames.Count - 1)

                if frames.Count = 0 then result <- i else state <- 2
            | _ ->
                let top = frames[frames.Count - 1]

                if frames.Count = 1 && not (isNull spans) then
                    spans.Add(
                        (if top.IsObject then top.Key else string top.Index),
                        s.Substring(valueAt, i - valueAt),
                        valueAt
                    )

                if top.IsObject && top.Seen.Contains top.Key then
                    fail top.KeyAt (JsonlFaultReason.RepeatedKey(valuePath ()))

                skipWs ()

                if i >= n then
                    ended ()

                match s[i] with
                | ',' ->
                    if top.IsObject then
                        top.Seen.Add top.Key |> ignore
                    else
                        top.Index <- top.Index + 1

                    i <- i + 1
                    first <- false
                    state <- if top.IsObject then 0 else 1
                | '}' when top.IsObject ->
                    i <- i + 1
                    state <- 5
                | ']' when not top.IsObject ->
                    i <- i + 1
                    state <- 5
                | _ ->
                    fail
                        i
                        (if top.IsObject then
                             JsonlFaultReason.ExpectedCommaOrBrace
                         else
                             JsonlFaultReason.ExpectedCommaOrBracket)

        result

    /// Index just past the JSON value starting at `start` (no leading whitespace), read by the
    /// grammar at every depth (Phase 466). `prefix` is the value's path.
    let internal skipValue (s: string) (start: int) (prefix: JsonlSeg list) : int =
        let n = s.Length

        if start >= n then
            fail start JsonlFaultReason.Truncated

        match s[start] with
        | '"' -> skipString s start (fun () -> renderPath prefix Unchecked.defaultof<_> 0)
        | '{'
        | '[' -> walk s start prefix false null
        | ','
        | '}'
        | ']' -> fail start JsonlFaultReason.MissingValue
        | _ -> skipLiteral s start

    /// Will `raw`, embedded verbatim as a member's value, read back as exactly `raw` (Phase 301)?
    /// It must carry no line break (`\n` / `\r`), begin with no whitespace, and be ONE JSON value
    /// this scanner reads to its last character — so the member's span is `raw` byte for byte. A
    /// line or paragraph separator (U+2028 / U+2029) inside a string passes: the canonical escaper
    /// emits it raw, and this reader splits lines on `\n` alone. The check every `try…ToJsonl`
    /// writer applies, public so a writer outside this package (the DAG's) applies the same one.
    let checkRaw (raw: string) : Result<unit, JsonlWriteFaultReason> =
        let n = raw.Length
        let mutable brk = -1
        let mutable k = 0

        while brk < 0 && k < n do
            if raw[k] = '\n' || raw[k] = '\r' then
                brk <- k

            k <- k + 1

        if brk >= 0 then
            Error(JsonlWriteFaultReason.MultiLine brk)
        elif n > 0 && JsonSyntax.isWhitespace raw[0] then
            Error JsonlWriteFaultReason.NotTrimStable
        else
            try
                let e = skipValue raw 0 []

                if e = n then
                    Ok()
                else
                    let mutable allWs = true

                    for j in e .. n - 1 do
                        if not (JsonSyntax.isWhitespace raw[j]) then
                            allWs <- false

                    if allWs then
                        Error JsonlWriteFaultReason.NotTrimStable
                    else
                        Error(JsonlWriteFaultReason.Unreadable JsonlFaultReason.TrailingContent)
            with JsonlScanFault(_, r) ->
                Error(JsonlWriteFaultReason.Unreadable r)

    /// The members of the object `s` holds — `(key, raw value, value position)`, in line order:
    /// the walk's depth-0 frame over the line's own object (Phase 530; until then a loop of its own
    /// beside the walk's object arm). A key read twice is refused (`RepeatedKey`) at its opening
    /// quote once its value is read (Phase 466: until then the first won, Phase 45, and the Wire
    /// grammar now refuses it too, D157). Positions are offsets into `s` plus `offset`.
    let private membersOf (offset: int) (s: string) : (string * string * int) list =
        let n = s.Length
        let mutable i = 0

        while i < n && JsonSyntax.isWhitespace s[i] do
            i <- i + 1

        try
            if i >= n || s[i] <> '{' then
                fail i JsonlFaultReason.NotAnObject

            let spans = ResizeArray<string * string * int>()
            let mutable e = walk s i [] true spans

            while e < n && JsonSyntax.isWhitespace s[e] do
                e <- e + 1

            if e < n then
                fail e JsonlFaultReason.TrailingContent

            spans |> Seq.map (fun (k, v, p) -> k, v, offset + p) |> List.ofSeq
        with JsonlScanFault(p, r) ->
            fail (offset + p) r

    let private faultAt (line: int) (pos: int) (reason: JsonlFaultReason) : JsonlFault =
        { Line = line
          Position = pos
          Reason = reason }

    /// Scan one line into a `JsonlLine` carrying its 1-based `number`.
    let parseLine (number: int) (text: string) : Result<JsonlLine, JsonlFault> =
        try
            Ok
                { Number = number
                  Text = text
                  Fields = membersOf 0 text }
        with JsonlScanFault(p, r) ->
            Error(faultAt number p r)

    /// The members of one flat JSON object, each value as its raw span byte-for-byte (the
    /// opaque canonical payload a consumer embeds keeps its bytes); a repeated key is refused
    /// (Phase 466; until then the first won). A refusal is numbered line 1.
    let topFields (line: string) : Result<(string * string) list, JsonlFault> =
        parseLine 1 line
        |> Result.map (fun l -> l.Fields |> List.map (fun (k, v, _) -> k, v))

    /// The raw span of ONE top-level member of a flat JSON object — `None` when the object has no
    /// such member — after the whole line has been scanned (a malformed line is refused even when
    /// the member itself is intact).
    let rawSpan (field: string) (line: string) : Result<string option, JsonlFault> =
        parseLine 1 line
        |> Result.map (fun l -> l.Fields |> List.tryPick (fun (k, v, _) -> if k = field then Some v else None))

    /// Unescape a string token (surrounding quotes included). Total: a raw span that is not a
    /// well-formed JSON string is an `Error`, never a truncated read — `null` is not the string
    /// `"ul"`.
    let unquote (raw: string) : Result<string, JsonlFaultReason> =
        if raw.Length < 2 || raw[0] <> '"' then
            Error(JsonlFaultReason.ExpectedString "")
        else
            try
                match readString (System.Text.StringBuilder()) raw 0 rootPath with
                | struct (value, stop) when stop = raw.Length -> Ok value
                | _ -> Error(JsonlFaultReason.ExpectedString "")
            with JsonlScanFault(_, r) ->
                Error r

    /// The line's 1-based number.
    let lineNumber (line: JsonlLine) : int = line.Number

    /// The line's text, verbatim.
    let lineText (line: JsonlLine) : string = line.Text

    /// A reader's own refusal of a well-formed line — `JsonlFaultReason.Refused` at the line's
    /// start.
    let refuse (line: JsonlLine) (reason: string) : JsonlFault =
        faultAt line.Number 0 (JsonlFaultReason.Refused reason)

    let private memberOf (key: string) (line: JsonlLine) : (string * int) option =
        line.Fields
        |> List.tryPick (fun (k, v, p) -> if k = key then Some(v, p) else None)

    /// The raw span of a member, or `None` when the line has none.
    let tryRawField (key: string) (line: JsonlLine) : string option = memberOf key line |> Option.map fst

    /// The raw span of a required member.
    let rawField (key: string) (line: JsonlLine) : Result<string, JsonlFault> =
        match memberOf key line with
        | Some(v, _) -> Ok v
        | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))

    /// A required member that must be a JSON string, unescaped.
    let stringField (key: string) (line: JsonlLine) : Result<string, JsonlFault> =
        match memberOf key line with
        | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))
        | Some(v, p) ->
            match unquote v with
            | Ok s -> Ok s
            | Error(JsonlFaultReason.ExpectedString _) ->
                Error(faultAt line.Number p (JsonlFaultReason.ExpectedString key))
            | Error r -> Error(faultAt line.Number p r)

    /// A required member that must be an integer under the JSON grammar
    /// (`-?(0|[1-9][0-9]*)`, within the 32-bit range) — not a string, a fraction, an exponent or
    /// a hex spelling.
    let intField (key: string) (line: JsonlLine) : Result<int, JsonlFault> =
        match memberOf key line with
        | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))
        | Some(v, p) ->
            let negative = v.Length > 0 && v[0] = '-'
            let digits = if negative then v.Substring 1 else v

            let grammatical =
                digits.Length > 0
                && digits.Length <= 10
                && Seq.forall (fun c -> c >= '0' && c <= '9') digits
                && (digits = "0" || digits[0] <> '0')

            // Read from the DIGITS, never through a host number reader (Phase 306). The
            // `System.Int64.Parse v` this replaces read under the CURRENT culture: under one
            // whose negative sign is not U+002D (fa-IR, he-IL) it threw on `-5` — a token the
            // grammar test above had just accepted — out of a function that returns a `Result`.
            // At most ten digits, so the fold cannot leave int64.
            let value =
                if grammatical then
                    let magnitude =
                        digits |> Seq.fold (fun acc c -> acc * 10L + int64 (int c - int '0')) 0L

                    if negative then -magnitude else magnitude
                else
                    0L

            if
                grammatical
                && value >= int64 System.Int32.MinValue
                && value <= int64 System.Int32.MaxValue
            then
                Ok(int value)
            else
                Error(faultAt line.Number p (JsonlFaultReason.ExpectedInteger key))

    /// A required member that must be an array of JSON strings (`[]` included), unescaped.
    let stringsField (key: string) (line: JsonlLine) : Result<string list, JsonlFault> =
        match memberOf key line with
        | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))
        | Some(v, p) ->
            let bad () =
                Error(faultAt line.Number p (JsonlFaultReason.ExpectedStringArray key))

            if v.Length = 0 || v[0] <> '[' then
                bad ()
            else
                // The walk's depth-0 frame over the array records its items (Phase 530; until then a
                // third container reader, by hand). A refusal inside the span is the scanner's own,
                // at its position in the line (Phase 383), as `unquote` answers.
                let items = ResizeArray<string * string * int>()

                let walked =
                    try
                        Ok(walk v 0 [] false items)
                    with JsonlScanFault(at, r) ->
                        Error(faultAt line.Number (p + at) r)

                match walked with
                | Error f -> Error f
                | Ok e when e <> v.Length -> bad ()
                | Ok _ when items |> Seq.forall (fun (_, raw: string, _) -> raw[0] = '"') ->
                    let scratch = System.Text.StringBuilder()

                    Ok
                        [ for (_, raw, _) in items ->
                              let struct (value, _) = readString scratch raw 0 rootPath
                              value ]
                | Ok _ -> bad ()

    /// A required member holding the typed `Actor` object (Phase 320) —
    /// `{"kind":"human","id":…}` or `{"kind":"agent","model":…,"version":…,"id":…}`, every member
    /// present and a string. An absent or unknown `kind` is a refusal, never `Human`: a store
    /// written by a newer build may carry a kind this reader does not know, and reading it as a
    /// person would attribute the op to the wrong kind of actor (Phase 260).
    let actorField (key: string) (line: JsonlLine) : Result<Actor, JsonlFault> =
        match memberOf key line with
        | None -> Error(faultAt line.Number 0 (JsonlFaultReason.MissingField key))
        | Some(v, p) ->
            try
                let inner =
                    { Number = line.Number
                      Text = v
                      Fields = membersOf p v }

                let str k =
                    match stringField k inner with
                    | Ok s -> s
                    | Error f ->
                        let reason =
                            match f.Reason with
                            | JsonlFaultReason.MissingField m -> JsonlFaultReason.MissingField(key + "." + m)
                            | JsonlFaultReason.ExpectedString m -> JsonlFaultReason.ExpectedString(key + "." + m)
                            | r -> r

                        fail f.Position reason

                // Phase 315 — an actor that names nobody is refused here, as `Actor.validate`
                // refuses it at construction, so a store cannot read back an anonymous author.
                let named (a: Actor) =
                    Actor.validate a
                    |> Result.mapError (fun why -> faultAt line.Number p (JsonlFaultReason.ActorInvalid(key, why)))

                match tryRawField "kind" inner with
                | None -> Error(refuse line "the actor carries no kind")
                | Some _ ->
                    match str "kind" with
                    | "human" -> named (Human(str "id"))
                    | "agent" -> named (Agent(str "model", str "version", str "id"))
                    | kind -> Error(refuse line (sprintf "unknown actor kind \"%s\"" kind))
            with JsonlScanFault(pos, r) ->
                Error(faultAt line.Number pos r)

    /// Scan a JSONL text line by line — the one record loop every reader shares. Lines are
    /// numbered from 1 over ALL lines (blank lines counted, then skipped), `\r\n` read as `\n`;
    /// each non-blank line is scanned and handed to `decode`, and the first refusal — the
    /// scanner's or the decoder's — is the result.
    let scanRecords (decode: JsonlLine -> Result<'T, JsonlFault>) (text: string) : Result<'T list, JsonlFault> =
        let lines = text.Replace("\r\n", "\n").Split('\n')
        let acc = ResizeArray<'T>()
        let mutable fault = None
        let mutable k = 0

        while fault.IsNone && k < lines.Length do
            let text = lines[k]

            if text.Trim() <> "" then
                match parseLine (k + 1) text |> Result.bind decode with
                | Ok v -> acc.Add v
                | Error f -> fault <- Some f

            k <- k + 1

        match fault with
        | Some f -> Error f
        | None -> Ok(List.ofSeq acc)

    let inline internal bindR ([<InlineIfLambda>] f: 'a -> Result<'b, 'e>) (r: Result<'a, 'e>) = Result.bind f r

    /// A single-object reader's refusal (a snapshot line, an attribution envelope) — the reason and
    /// the position, without a line number the caller never had.
    let internal spanFault (f: JsonlFault) : string =
        sprintf "%s (position %d)" (JsonlFault.reasonText f.Reason) f.Position

    /// THE chained line, read (Phase 530): `seq`, then the kind's own members (`body`, handed the
    /// sequence), then the link, `prevHash` and `hash` — the order every chained reader refuses in, so
    /// a line missing two members names the same one whichever kind reads it.
    let internal linkedOf
        (line: JsonlLine)
        (body: int -> Result<'B, JsonlFault>)
        : Result<int * 'B * string * string, JsonlFault> =
        intField "seq" line
        |> bindR (fun seq ->
            body seq
            |> bindR (fun b ->
                stringField "prevHash" line
                |> bindR (fun prevHash -> stringField "hash" line |> Result.map (fun hash -> seq, b, prevHash, hash))))

    /// The first of `members` — `(member, text)` in line order — that `Jsonl.checkRaw` refuses, with
    /// its reason (Phase 473). A text is the member's value exactly as the line will carry it: an
    /// embedded span, or a string the writer quotes itself.
    let internal firstUnreadable (members: (string * string) list) : (string * JsonlWriteFaultReason) option =
        members
        |> List.tryPick (fun (m, text) ->
            match checkRaw text with
            | Error reason -> Some(m, reason)
            | Ok() -> None)

    /// Write one line per item, every member checked before any line is built (Phase 301, Phase
    /// 473): `members` names each member a line carries with its text as the line will carry it (an
    /// embedded span, or a string the writer quotes itself), in line order; `lineOf item text` builds
    /// the line around the text of the member named `embedded`. The first member `Jsonl.checkRaw`
    /// refuses is the `Error`, by its 1-based line and member; on `Ok` the lines joined by `\n`.
    let checkedLines
        (embedded: string)
        (membersOf: 'T -> (string * string) list)
        (lineOf: 'T -> string -> string)
        (items: 'T list)
        : Result<string, JsonlWriteFault> =
        let lines = ResizeArray<string>()

        let rec go (i: int) =
            function
            | [] -> Ok(String.concat "\n" lines)
            | (x: 'T) :: rest ->
                let members = membersOf x

                match firstUnreadable members with
                | Some(m, reason) ->
                    Error
                        { Line = i + 1
                          Member = m
                          Reason = reason }
                | None ->
                    lines.Add(lineOf x (members |> List.find (fun (m, _) -> m = embedded) |> snd))
                    go (i + 1) rest

        go 0 items

    /// `write` that refuses what the reader cannot read back (Phase 301). Every op's encoding is
    /// checked before a line is built (`Jsonl.checkRaw`): one carrying a line break, whitespace either
    /// side of the value, or anything but one JSON value would read back changed — split across lines,
    /// or trimmed — and the read-back chain would fail `verifyChain`. Since Phase 473 the strings the
    /// writer quotes itself (`actor`, `prevHash`, `hash`) are checked too, in line order, so an unpaired
    /// surrogate in one is refused here rather than by the strict reader. The first such record is the
    /// `Error`, by its 1-based line and member. On `Ok` the text is `write`'s, byte for byte,
    /// and `read` reads it back to records that `write` writes identically.
    let tryWrite (w: StreamWitness<'Op, 'State, 'Rej>) (records: OpRecord<'Op> list) : Result<string, JsonlWriteFault> =
        checkedLines
            "op"
            (fun (r: OpRecord<'Op>) ->
                [ "actor", Actor.encode r.Actor
                  "op", w.Encode r.Op
                  "prevHash", jstr r.PrevHash
                  "hash", jstr r.Hash ])
            recordLine
            records

    /// One record line, decoded — the members in line order, the `op` span handed to the witness.
    let private recordOf
        (actorOf: JsonlLine -> Result<Actor, JsonlFault>)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (line: JsonlLine)
        : Result<OpRecord<'Op>, JsonlFault> =
        linkedOf line (fun _ ->
            actorOf line
            |> bindR (fun actor ->
                rawField "op" line
                |> bindR (fun raw -> w.Decode raw |> Result.mapError (refuse line))
                |> Result.map (fun op -> actor, op)))
        |> Result.map (fun (seq, (actor, op), prevHash, hash) ->
            { Seq = seq
              Actor = actor
              Op = op
              PrevHash = prevHash
              Hash = hash })

    /// Is this line a snapshot line? One carrying `"snapshot":true` and NO `op` — a record line that
    /// happens to carry a `snapshot` member is a record (Phase 296), not a snapshot dropped on the
    /// floor.
    let private isSnapshotLine (line: JsonlLine) : bool =
        Option.isNone (tryRawField "op" line)
        && tryRawField "snapshot" line = Some "true"

    /// The records-and-snapshot reader, parameterised on how the `actor` member decodes — the
    /// canonical typed object, or the legacy bare string. One snapshot line is admitted, and only as
    /// the first line of the stream (Phase 296); a second, or one after a record, is refused. Each
    /// record comes with the 1-based line that holds it.
    let private scanJsonlWithSnapshots
        (actorOf: JsonlLine -> Result<Actor, JsonlFault>)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<(int * OpRecord<'Op>) list * string list, JsonlFault> =
        text
        |> scanRecords (fun line ->
            if isSnapshotLine line then
                Ok(Choice2Of2 line)
            else
                recordOf actorOf w line |> Result.map (fun r -> Choice1Of2(lineNumber line, r)))
        |> bindR (fun items ->
            let rec go (first: bool) recs snaps =
                function
                | [] -> Ok(List.rev recs, List.rev snaps)
                | Choice1Of2 r :: rest -> go false (r :: recs) snaps rest
                | Choice2Of2(l: JsonlLine) :: rest ->
                    if first then
                        go false recs [ lineText l ] rest
                    else
                        Error
                            { Line = lineNumber l
                              Position = 0
                              Reason = JsonlFaultReason.SnapshotNotAtHead }

            go true [] [] items)

    /// The witness a load reads through (Phase 416): it never refuses, carrying each op's stored text
    /// beside `w`'s answer, so a decode failure stops neither the read nor the verification. A decoded
    /// op encodes by `w`; an undecoded one as its stored text, the bytes the writer embedded verbatim,
    /// so a hash still recomputes over exactly what was hashed.
    let carry (w: StreamWitness<'Op, 'State, 'Rej>) : StreamWitness<string * Result<'Op, string>, unit, unit> =
        { Apply = fun _ s -> Ok s
          Encode =
            fun (raw, decoded) ->
                match decoded with
                | Ok op -> w.Encode op
                | Error _ -> raw
          Decode = fun raw -> Ok(raw, w.Decode raw) }

    /// A carried read settled: every read `decode`d, in read order — or EVERY read whose op the
    /// witness refused, each as the site `site read reason` names, in read order. Never stops at the
    /// first: a store written by a newer host is named whole (`StreamLoadFault.Undecodable`).
    let settle
        (decode: 'R -> Result<'T, string>)
        (site: 'R -> string -> UndecodableOp)
        (reads: 'R list)
        : Result<'T list, UndecodableOp list> =
        let decoded =
            reads
            |> List.map (fun r ->
                match decode r with
                | Ok v -> Ok v
                | Error why -> Error(site r why))

        match
            decoded
            |> List.choose (function
                | Error s -> Some s
                | Ok _ -> None)
        with
        | [] ->
            Ok(
                decoded
                |> List.choose (function
                    | Ok v -> Some v
                    | Error _ -> None)
            )
        | sites -> Error sites

    /// The records of a carried read, decoded — or EVERY record the witness refused, by line and
    /// stored hash: `settle` over the record's carried op.
    let private settleRecords
        (reads: (int * OpRecord<string * Result<'Op, string>>) list)
        : Result<OpRecord<'Op> list, StreamLoadFault<ChainBreak>> =
        reads
        |> settle
            (fun (_, r) ->
                snd r.Op
                |> Result.map (fun op ->
                    { Seq = r.Seq
                      Actor = r.Actor
                      Op = op
                      PrevHash = r.PrevHash
                      Hash = r.Hash }))
            (fun (line, r) why ->
                { Lane = None
                  Line = line
                  NodeId = r.Hash
                  Reason = why })
        |> Result.mapError StreamLoadFault.Undecodable

    let private readCarried
        (actorOf: JsonlLine -> Result<Actor, JsonlFault>)
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        =
        scanJsonlWithSnapshots actorOf (carry w) text
        |> Result.mapError (fun f -> StreamLoadFault.Unreadable(None, f))

    /// Parse JSONL into `(records, rawSnapshotLines)` (Phase 16) — the snapshot-aware reader. The
    /// records are decoded by the witness; the snapshot line, when there is one, is returned verbatim
    /// (its `state` member still embedded raw) so a caller can recover the base state with
    /// `Snapshots.ofJsonl` and resume via `Snapshots.replayFrom`. A non-empty
    /// snapshot list means the file was compacted — replaying the records from origin would be wrong.
    /// Fully portable (Phase 241).
    ///
    /// **Refusals (Phase 296; typed since Phase 416).** A malformed line, a member of the wrong kind
    /// (`"prevHash":null`, `"seq":0x2`), or a snapshot line anywhere but the first line is
    /// `StreamLoadFault.Unreadable` carrying the `JsonlFault`, its line 1-based over every line of the
    /// text. A witness decode `Error` does not stop the read: when every line parses and only the
    /// witness refused, the answer is `StreamLoadFault.Undecodable` naming EVERY such op by line and
    /// stored hash — the signature of a stream written by a newer host. Structural, so never
    /// `Broken`. `StreamLoadFault.toString` renders the pre-416 message. At most one snapshot line is
    /// returned. The `op` raw span is preserved byte-for-byte, so a round-trip is identical. Since
    /// Phase 320 the `actor` member is the typed object; use `readLegacyActor` for a pre-320 file.
    let readWithSnapshots
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list * string list, StreamLoadFault<ChainBreak>> =
        readCarried (actorField "actor") w text
        |> bindR (fun (reads, snaps) -> settleRecords reads |> Result.map (fun recs -> recs, snaps))

    /// Parse JSONL back into records, **dropping the snapshot line** (Phase 244) — correct for a
    /// linear, never-compacted stream. A thin wrapper over `readWithSnapshots` (the one scanner);
    /// a `compact` output (snapshot + tail) read this way loses its base state with no signal and
    /// replaying from origin is then wrong, so read a possibly-compacted file with
    /// `readWithSnapshots` instead. Refuses exactly what that reader refuses; the `op` raw span
    /// round-trips.
    let read
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list, StreamLoadFault<ChainBreak>> =
        readWithSnapshots w text |> Result.map fst

    /// Read a **pre-Phase-320** JSONL file (Phase 320 migration) — the `actor` member is still a bare
    /// JSON string, which this lifts to the typed `Human` case. The returned records carry the file's
    /// stored `PrevHash` / `Hash` (computed under the old bare-string payload), so they
    /// `verifyChain StreamConfig.legacyActor` and then `rehash StreamConfig.legacyActor StreamConfig.canonical` to the
    /// new typed form. The snapshot line is dropped. Refuses what `read` refuses.
    let readLegacyActor
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list, StreamLoadFault<ChainBreak>> =
        readCarried (fun line -> stringField "actor" line |> Result.map Actor.ofLegacyString) w text
        |> bindR (fst >> settleRecords)

    /// `read` + a chain-integrity gate (Phase 13). Parses the records, then verifies the chain under `s`
    /// them — a broken prev-link / reordered / tampered record is `StreamLoadFault.Broken` with the
    /// first `ChainBreak`, not a silent `Ok` of a corrupt stream. For a linear, uncompacted stream; a
    /// compacted file (snapshot + tail) does not start its chain at genesis, so read it with
    /// `readWithSnapshots` and verify the boundary with `Snapshots.verify` instead.
    ///
    /// **The chain is verified before any op must decode (Phase 416).** A record the witness cannot
    /// decode is hashed over its stored op text, the bytes the writer embedded, so the chain verifies
    /// without it; only then are the ops decoded. A broken chain answers `Broken` whatever else is
    /// wrong; an intact chain with ops the witness refused answers `Undecodable`, naming every one.
    /// `StreamLoadFault.toString` renders the pre-416 message, break included.
    let readVerified
        (s: Stream<'Op, 'State, 'Rej>)
        (text: string)
        : Result<OpRecord<'Op> list, StreamLoadFault<ChainBreak>> =
        readCarried (actorField "actor") s.Witness text
        |> bindR (fun (reads, _) ->
            match
                OpStream.firstChainBreak (OpStream.ofConfig s.Config s.HashFn (carry s.Witness)) (List.map snd reads)
            with
            | Some b -> Error(StreamLoadFault.Broken b)
            | None -> settleRecords reads)
