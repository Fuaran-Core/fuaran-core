module Fuaran.Core.Tests.StringEscapeTests

// Phase 287 — one string escaping on the spine. `Wire.Json.escape`, `Actor.encode` and
// `Dag.toJsonl` all spell every control character as lower-case `\u00xx`, with no short form
// for `\n` / `\r` / `\t`; the conformance kit's `StringEscapeVectors` pins the three byte-for-
// byte against one table. This suite runs that family, checks the family can go red, states the
// phase's acceptance bytes directly, and exercises the chain migration a store written under
// the OLD spelling takes: `verifyChainWith legacyEscapeConfig`, then `rehash` to canonical — a
// no-op, proved rather than assumed, for a store whose actors carry no control character.

open Expecto
open Fuaran.Core

/// A string op: the payload is the string itself, so a control character inside an OP's own
/// encoding rides the chain too, not only one inside the actor.
type private NoteOp = Note of string

let private sw: StreamWitness<NoteOp, string list, string> =
    { Apply = fun (Note s) st -> Ok(s :: st)
      Encode = fun (Note s) -> Json.render (Json.kindObj "note" [ "text", JStr s ])
      Decode =
        fun json ->
            Decode.parse json
            |> Result.bind (fun el -> Decode.strField "text" el |> Result.map Note) }

let private h = OpStream.defaultHash

/// Build a chain under `cfg` from `(actor, op)` pairs.
let private buildWith (cfg: StreamConfig) (steps: (Actor * NoteOp) list) : OpRecord<NoteOp> list =
    steps
    |> List.fold
        (fun (st, recs) (actor, op) ->
            match OpStream.appendWith cfg h sw actor op st recs with
            | Ok(st', recs') -> st', recs'
            | Error e -> failtestf "append refused: %s" e)
        ([], OpStream.empty)
    |> snd

let private controlActors: (Actor * NoteOp) list =
    [ Agent("m\n", "1\t", "id\r"), Note "first"
      Human "\u0000", Note "second"
      Human "plain", Note "third" ]

let private plainActors: (Actor * NoteOp) list =
    [ Human "alice", Note "one"
      Agent("model", "4.8", "planner"), Note "two"
      Human "bob \"quoted\" \\ slashed / é", Note "three" ]

/// The committed rendering of `StringEscapeVectors.lines` (Phase 349): the file a host in another
/// language diffs its own rendering against, so the line FORMAT is pinned in the repository rather
/// than recomputed by whoever reads it. Written by `--emit-escape`, held to a fresh render below.
module EscapeCorpus =

    let familyDirName = "escape"
    let fileName = "string-escape.json"

    let private description =
        "String-escape vectors (Phase 287; committed since Phase 349). One rule spells a string inside canonical JSON: a quotation mark as backslash-quote, a backslash as two, every control character U+0000-U+001F as a lower-case backslash-u escape with no short form, and nothing else. Each entry of `lines` is one line of the table, three fields separated by one TAB (U+0009), no field carrying a TAB or a line break: for each character vector (every control character, the quotation mark, the backslash, then a plain-text control that must pass through unescaped) its name, its canonical spelling (the body of the JSON string literal, quotes excluded) and the actor encoding of a human whose id is that character; then for each named actor its name, its encoding and the linear chain pre-image at sequence 0 with an empty op. A host that reproduces this package's chain hashes renders the same lines, byte for byte and in this order."

    /// The file, rendered: a header, then one JSON string per line of the table.
    let render () : string =
        let lines = StringEscapeVectors.lines ()

        let body =
            lines
            |> List.mapi (fun i l ->
                "    \""
                + Json.escape l
                + "\""
                + (if i < List.length lines - 1 then ",\n" else "\n"))
            |> String.concat ""

        "{\n  \"family\": \"stringEscape\",\n  \"description\": \""
        + Json.escape description
        + "\",\n  \"format\": \"name<TAB>spelling<TAB>pre-image\",\n  \"lines\": [\n"
        + body
        + "  ]\n}\n"

    let path (dir: string) =
        System.IO.Path.Combine(dir, familyDirName, fileName)

    let write (dir: string) : unit =
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, familyDirName))
        |> ignore

        System.IO.File.WriteAllText(path dir, render ())

/// Phase 365 — the escape's fast path scans for the first escapable character and copies clean runs
/// whole. These are the inputs whose run boundaries it can get wrong, checked on every copy of the
/// rule against a one-character-at-a-time oracle written from the rule's statement.
module RunBoundaries =

    /// The rule, one character at a time: the shape the escape had before Phase 365.
    let oracle (s: string) : string =
        let sb = System.Text.StringBuilder()

        for ch in s do
            match ch with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | c when int c < 0x20 -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append(c) |> ignore

        sb.ToString()

    let private controls = [ for c in 0..0x1F -> string (char c) ]

    /// Clean strings, escape-only strings, an escape first and last, runs between escapes, every
    /// control character alone and inside runs, and surrogate pairs beside escapes.
    let inputs: (string * string) list =
        [ "empty", ""
          "one clean character", "a"
          "clean ASCII", "plain text, digits 0123456789 and / punctuation!"
          "clean non-ASCII", "é ü 中文   \u007f"
          "a surrogate pair alone", "\U0001F600"
          "surrogate pairs in clean text", "a\U0001F600b\U0001D11Ec"
          "a surrogate pair beside escapes", "\"\U0001F600\\\n\U0001F600\u0001"
          "escape-only: one quote", "\""
          "escape-only: one backslash", "\\"
          "escape-only: every escaped class", "\"\\\u0000\u001f\n\r\t"
          "escape-only: every control character", String.concat "" controls
          "an escape at the first position", "\"abc"
          "an escape at the last position", "abc\\"
          "escapes at both ends", "\nabc\t"
          "runs between escapes", "ab\"cd\\ef\ngh\u0000ij"
          "single-character runs", "a\"b\\c\nd"
          "adjacent escapes inside a run", "abc\"\"\\\\\n\ndef"
          "a long clean run", String.replicate 300 "x" + "\"" + String.replicate 300 "y" ]
        @ [ for c in 0..0x1F -> sprintf "U+%04X inside a run" c, "ab" + string (char c) + "cd" ]

    /// The checks one input makes against `escaped` — every copy of the rule, in its own wrapping.
    let checks (escaped: string -> string) (s: string) : (string * string * string) list =
        let body = oracle s
        let q = "\"" + body + "\""

        let into =
            let sb = System.Text.StringBuilder("<")
            Json.escapeInto sb s
            sb.Append(">").ToString()

        let dag: Dag.T<unit> =
            { Nodes =
                Map.ofList
                    [ s,
                      { Id = s
                        Parents = [ s ]
                        Actor = Human s
                        Op = () } ] }

        let human = "{\"kind\":\"human\",\"id\":" + q + "}"

        [ "Json.escape", body, escaped s
          "Json.escapeInto", "<" + body + ">", into
          "Json.render", q, Json.render (JStr s)
          "Canon.render", "{" + q + ":" + q + "}", Canon.render (JObj [ s, JStr s ])
          "Actor.encode", human, Actor.encode (Human s)
          "Dag.toJsonl",
          "{\"node\":true,\"id\":"
          + q
          + ",\"parents\":["
          + q
          + "],\"actor\":"
          + human
          + ",\"op\":{}}",
          Dag.toJsonl (fun () -> "{}") dag ]

    /// Every (input, copy) pair whose bytes differ from the oracle's, under `escaped` as `Json.escape`.
    let mismatches (escaped: string -> string) : string list =
        [ for name, s in inputs do
              for copy, expected, got in checks escaped s do
                  if got <> expected then
                      yield sprintf "%s / %s: expected %A, got %A" name copy expected got ]

    /// A deliberately broken run boundary: the clean run BEFORE each escape is appended one character
    /// short. Exists only to show the check above can go red.
    let brokenRunBoundary (s: string) : string =
        let sb = System.Text.StringBuilder()
        let mutable start = 0

        for i in 0 .. s.Length - 1 do
            let code = int s.[i]

            if code < 0x20 || code = 0x22 || code = 0x5C then
                if i - start > 1 then
                    sb.Append(s, start, i - start - 1) |> ignore

                sb.Append(oracle (string s.[i])) |> ignore
                start <- i + 1

        if s.Length > start then
            sb.Append(s, start, s.Length - start) |> ignore

        sb.ToString()

[<Tests>]
let tests =
    testList
        "StringEscape"
        [ testCase "run boundaries (Phase 365): every copy of the rule agrees with the one-character oracle"
          <| fun _ ->
              Expect.isGreaterThan RunBoundaries.inputs.Length 40 "the boundary corpus is the one written"
              Expect.equal (RunBoundaries.mismatches Json.escape) [] "no input, no copy differs from the oracle"

          testCase "run boundaries (Phase 365): a deliberately broken run boundary is red"
          <| fun _ ->
              let red = RunBoundaries.mismatches RunBoundaries.brokenRunBoundary
              Expect.isNonEmpty red "a run appended one character short is caught"

              Expect.exists
                  red
                  (fun m -> m.StartsWith "runs between escapes / Json.escape:")
                  "and it is caught on the runs-between-escapes input"

              Expect.isFalse
                  (red |> List.exists (fun m -> m.StartsWith "escape-only"))
                  "an escape-only input has no run to break, and is not reported"

          testCase "a string with nothing to escape is returned as it is (Phase 365)"
          <| fun _ ->
              for s in [ ""; "plain"; "é \U0001F600   /" ] do
                  Expect.isTrue (obj.ReferenceEquals(Json.escape s, s)) (sprintf "%A is not copied" s)

              let s = "needs \"one\""
              Expect.isFalse (obj.ReferenceEquals(Json.escape s, s)) "a string with an escape is"

          testCase "Json.escapeInto appends after what the builder holds and equals Json.escape (Phase 365)"
          <| fun _ ->
              for _, s in RunBoundaries.inputs do
                  let sb = System.Text.StringBuilder("prefix|")
                  Json.escapeInto sb s
                  Expect.equal (sb.ToString()) ("prefix|" + Json.escape s) (sprintf "%A" s)

          testCase "the committed conformance/escape/ file is what this kit renders, and parses back to the lines"
          <| fun _ ->
              let file = EscapeCorpus.path (OwnedConformance.root ())

              Expect.isTrue
                  (System.IO.File.Exists file)
                  (sprintf "%s exists — run `--emit-escape` (no argument) and commit conformance/" file)

              let committed = (System.IO.File.ReadAllText file).Replace("\r\n", "\n")

              Expect.equal
                  committed
                  (EscapeCorpus.render ())
                  "the committed escape file is not what this kit renders — re-run `--emit-escape` (no argument) and commit conformance/"

              match Json.parse committed with
              | Ok(JObj members) ->
                  match members |> List.tryFind (fun (k, _) -> k = "lines") with
                  | Some(_, JArr items) ->
                      Expect.equal
                          (items
                           |> List.map (function
                               | JStr s -> s
                               | other -> failtestf "a line is not a string: %A" other))
                          (StringEscapeVectors.lines ())
                          "the file's lines read back as the table"
                  | other -> failtestf "no lines array: %A" other
              | other -> failtestf "the escape file does not parse as an object: %A" other

          testCase "the conformance family is green, and it measured something"
          <| fun _ ->
              match StringEscapeVectors.check () with
              | Ok() -> ()
              | Error e -> failtestf "StringEscapeVectors: %s" e

              let outcomes = StringEscapeVectors.run ()

              // 32 control characters + quote + backslash + plain, each through eleven checks,
              // plus two checks per named actor: the count is a floor, not a pin.
              Expect.isGreaterThan outcomes.Length 350 "the family ran every vector through every escaper"

          testCase "the family can go red: a table row claiming the short form is refused"
          <| fun _ ->
              let row: StringEscapeVectors.Vector =
                  { Name = "probe"
                    Input = "\n"
                    Escaped = "\\n"
                    Legacy = "\\n" }

              let failures =
                  StringEscapeVectors.runVector row |> List.filter (fun o -> not o.Passed)

              Expect.isNonEmpty failures "a row spelling U+000A as \\n must fail against the \\u000a rule"

              Expect.exists
                  failures
                  (fun o -> o.Name.EndsWith "Wire.Json.escape")
                  "the rule at its home is one of the checks that refused it"

              Expect.exists
                  failures
                  (fun o -> o.Name.EndsWith "Actor.encode Human")
                  "the OpStream copy is one of the checks that refused it"

              Expect.exists
                  failures
                  (fun o -> o.Name.EndsWith "Dag.toJsonl")
                  "the DAG copy is one of the checks that refused it"

          testCase "Json.escape spells every C0 character as lower-case \\u00xx and nothing else"
          <| fun _ ->
              for c in 0..0x1F do
                  Expect.equal
                      (Json.escape (string (char c)))
                      (sprintf "\\u%04x" c)
                      (sprintf "U+%04X escapes as \\u%04x" c c)

              Expect.equal (Json.escape "\"") "\\\"" "quote"
              Expect.equal (Json.escape "\\") "\\\\" "backslash"
              Expect.equal (Json.escape "/") "/" "solidus is not escaped"
              Expect.equal (Json.escape "\u007f") "\u007f" "DEL is above the control range and passes through"
              Expect.equal (Json.escape "é\u2028") "é\u2028" "non-ASCII and U+2028 pass through"

              Expect.equal
                  (Json.escape "a\nb\tc\rd")
                  "a\\u000ab\\u0009c\\u000dd"
                  "the three characters that had short forms no longer do"

          testCase "the acceptance bytes: Actor.encode (Agent(\"m\\n\",\"1\",\"id\"))"
          <| fun _ ->
              Expect.equal
                  (Actor.encode (Agent("m\n", "1", "id")))
                  "{\"kind\":\"agent\",\"model\":\"m\\u000a\",\"version\":\"1\",\"id\":\"id\"}"
                  "the phase's stated acceptance"

              Expect.equal
                  (Actor.encode (Agent("m\n", "1\t", "id\r")))
                  "{\"kind\":\"agent\",\"model\":\"m\\u000a\",\"version\":\"1\\u0009\",\"id\":\"id\\u000d\"}"
                  "the control-character agent the chain laws carry"

              Expect.equal
                  (Actor.encode (Human "\u0000"))
                  "{\"kind\":\"human\",\"id\":\"\\u0000\"}"
                  "the NUL human the chain laws carry"

          testCase "Json.escape and Canon.render agree with each other on every C0 character"
          <| fun _ ->
              for c in 0..0x1F do
                  let s = string (char c)

                  Expect.equal
                      (Canon.render (JStr s))
                      ("\"" + Json.escape s + "\"")
                      (sprintf "U+%04X: Canon.render is Json.escape in quotes" c)

          testCase "the parser reads both spellings back to the same string (nothing changes on read)"
          <| fun _ ->
              for c in 0..0x1F do
                  let s = string (char c)
                  let canonical = "\"" + Json.escape s + "\""

                  Expect.equal (Json.parse canonical) (Ok(JStr s)) (sprintf "U+%04X canonical round-trips" c)

              Expect.equal (Json.parse "\"a\\nb\\tc\\rd\"") (Ok(JStr "a\nb\tc\rd")) "the short forms still read"

              Expect.equal
                  (Json.render (Json.parse "\"a\\nb\"" |> Result.defaultValue (JStr "")))
                  "\"a\\u000ab\""
                  "a document written with a short form renders canonically after a round trip"

          // ---- the chain migration, on the Phase-255 shape ----

          testCase "a store written under the old spelling verifies under legacyEscapeConfig, not under canonical"
          <| fun _ ->
              let legacy = buildWith OpStream.legacyEscapeConfig controlActors

              Expect.isTrue
                  (OpStream.verifyChainWith OpStream.legacyEscapeConfig h sw legacy)
                  "verifies under the config it was written with"

              Expect.isFalse
                  (OpStream.verifyChain h sw legacy)
                  "the canonical payload spells the actor differently, so the old chain does not verify under it"

          testCase "rehash legacyEscapeConfig canonicalConfig cuts a control-character store over, and the head moves"
          <| fun _ ->
              let legacy = buildWith OpStream.legacyEscapeConfig controlActors

              match OpStream.rehash OpStream.legacyEscapeConfig OpStream.canonicalConfig h sw legacy with
              | Error e -> failtestf "rehash refused: %s" e
              | Ok migrated ->
                  Expect.isTrue (OpStream.verifyChain h sw migrated) "the migrated chain verifies under canonical"

                  Expect.equal
                      (migrated |> List.map (fun r -> r.Seq, r.Actor, r.Op))
                      (legacy |> List.map (fun r -> r.Seq, r.Actor, r.Op))
                      "rehash touches only the hash chain — the records are the source of truth"

                  Expect.notEqual (OpStream.head migrated) (OpStream.head legacy) "the bytes moved, so the head moved"

                  Expect.equal
                      (OpStream.head migrated)
                      (OpStream.head (buildWith OpStream.canonicalConfig controlActors))
                      "the migrated chain is the chain a canonical writer would have produced"

          testCase
              "a store whose actors carry no control character hashes identically under both — proved by the rehash"
          <| fun _ ->
              let legacy = buildWith OpStream.legacyEscapeConfig plainActors

              Expect.isTrue
                  (OpStream.verifyChain h sw legacy)
                  "an unmigrated control-free store already verifies under canonical"

              match OpStream.rehash OpStream.legacyEscapeConfig OpStream.canonicalConfig h sw legacy with
              | Error e -> failtestf "rehash refused: %s" e
              | Ok migrated ->
                  Expect.equal migrated legacy "the rehash is a no-op: every record, every hash, byte-identical"

          testCase "rehash refuses a legacy store that was tampered"
          <| fun _ ->
              let legacy = buildWith OpStream.legacyEscapeConfig controlActors

              let tampered =
                  legacy
                  |> List.mapi (fun i r ->
                      if i = 1 then
                          { r with
                              Actor = Agent("m\n", "1\t", "mallory") }
                      else
                          r)

              match OpStream.rehash OpStream.legacyEscapeConfig OpStream.canonicalConfig h sw tampered with
              | Ok _ -> failtest "a re-attributed record must not migrate"
              | Error _ -> ()

          testCase "legacyActorConfig keeps the pre-320 bytes: a bare-string actor with a newline still spells \\n"
          <| fun _ ->
              // A pre-320 writer wrote the actor as a bare JSON string with the short escape; the
              // legacy config exists to reproduce THOSE bytes, so it must not adopt the new rule.
              Expect.equal
                  (OpStream.legacyActorConfig.Payload 3 (Human "a\nb") "{}")
                  "{\"seq\":3,\"actor\":\"a\\nb\",\"op\":{}}"
                  "pre-320 payload bytes are unchanged by Phase 287"

          // ---- the wire, both spines ----

          testCase "the linear JSONL round-trips a control-character actor and a control-character op"
          <| fun _ ->
              let recs =
                  buildWith OpStream.canonicalConfig [ Agent("m\n", "1\t", "id\r"), Note "line\u0001break\n" ]

              let text = OpStream.toJsonl sw recs

              Expect.stringContains text "\\u000a" "the line carries the canonical spelling"
              Expect.isFalse (text.Contains "\\n") "and never the short form"

              match OpStream.fromJsonlVerified h sw text with
              | Ok back -> Expect.equal back recs "round-trip is exact and the chain verifies"
              | Error e -> failtestf "fromJsonlVerified: %s" e

          testCase "the DAG folds the canonical spelling into a node's content id, and its JSONL round-trips"
          <| fun _ ->
              let actor = Agent("m\n", "1\t", "id\r")
              let id, dag = Dag.append h sw actor (Note "x") "" Dag.empty |> Reference.built

              Expect.equal
                  id
                  (h "" (Actor.encode actor + "|" + sw.Encode(Note "x")))
                  "the content id is hashed over Actor.encode's \\u00xx bytes"

              Expect.isTrue (Dag.verifyDag h sw dag) "verifies"

              let text = Dag.toJsonl sw.Encode dag
              Expect.stringContains text "\\u000d" "the node line carries the canonical spelling"
              Expect.isFalse (text.Contains "\\r") "and never the short form"

              match Dag.fromJsonlVerified h sw text with
              | Ok back -> Expect.equal back dag "round-trip is exact and the DAG verifies"
              | Error e -> failtestf "Dag.fromJsonlVerified: %s" e

          testCase "the attribution envelope round-trips a session id carrying a tab"
          <| fun _ ->
              let a: Attributed<NoteOp> =
                  { Actor = "agent\u0001"
                    Session = "s\t1"
                    Turn = Some 2
                    At = "2026-09-30T00:00:00Z"
                    Op = Note "n" }

              let text = OpStream.Attributed.encodeEnvelope sw.Encode a
              Expect.stringContains text "\\u0009" "the envelope carries the canonical spelling"

              match OpStream.Attributed.decodeEnvelope sw.Decode text with
              | Ok back -> Expect.equal back a "round-trip"
              | Error e -> failtestf "decodeEnvelope: %s" e ]
