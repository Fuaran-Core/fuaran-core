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
            Json.parse json
            |> Result.bind (fun el -> Decoder.describing (Decoder.field "text" Decoder.str) el |> Result.map Note) }

let private h = OpStream.defaultHash

/// Build a chain under `cfg` from `(actor, op)` pairs.
let private buildWith (cfg: StreamConfig) (steps: (Actor * NoteOp) list) : OpRecord<NoteOp> list =
    steps
    |> List.fold
        (fun (st, recs) (actor, op) ->
            match OpStream.append (OpStream.ofConfig cfg h sw) actor op st recs with
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
            DagOf.nodes (
                Map.ofList
                    [ s,
                      { Id = s
                        Parents = [ s ]
                        Actor = Human s
                        Op = () } ]
            )

        let human = "{\"kind\":\"human\",\"id\":" + q + "}"

        [ "Json.escape", body, escaped s
          "Json.escapeInto", "<" + body + ">", into
          "Json.render", q, Json.render (JStr s)
          "Canonical.write Rendering.CrossHost",
          "{" + q + ":" + q + "}",
          Canonical.write Rendering.CrossHost (JObj [ s, JStr s ])
          "Actor.encode", human, Actor.encode (Human s)
          // Phase 388 - the op-stream package's one escaper, public; `Dag.toJsonl` spells through it.
          "OpStream.Jsonl.quote", q, Jsonl.quote s
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
            let code = int s[i]

            if code < 0x20 || code = 0x22 || code = 0x5C then
                if i - start > 1 then
                    sb.Append(s, start, i - start - 1) |> ignore

                sb.Append(oracle (string s[i])) |> ignore
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

          testCase
              "the rendered conformance/escape/ file parses back to the lines (its freshness is the exporter table's)"
          <| fun _ ->
              match Json.parse (EscapeCorpus.render ()) with
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

          testCase "Json.escape and Canonical.write Rendering.CrossHost agree with each other on every C0 character"
          <| fun _ ->
              for c in 0..0x1F do
                  let s = string (char c)

                  Expect.equal
                      (Canonical.write Rendering.CrossHost (JStr s))
                      ("\"" + Json.escape s + "\"")
                      (sprintf "U+%04X: Canonical.write Rendering.CrossHost is Json.escape in quotes" c)

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
              let legacy = buildWith StreamConfig.legacyEscape controlActors

              Expect.isTrue
                  (OpStream.verifyChain (OpStream.ofConfig StreamConfig.legacyEscape h sw) legacy)
                  "verifies under the config it was written with"

              Expect.isFalse
                  (OpStream.verifyChain (OpStream.canonical h sw) legacy)
                  "the canonical payload spells the actor differently, so the old chain does not verify under it"

          testCase "rehash legacyEscapeConfig canonicalConfig cuts a control-character store over, and the head moves"
          <| fun _ ->
              let legacy = buildWith StreamConfig.legacyEscape controlActors

              match
                  OpStream.rehash (OpStream.ofConfig StreamConfig.legacyEscape h sw) (OpStream.canonical h sw) legacy
              with
              | Error e -> failtestf "rehash refused: %A" e
              | Ok migrated ->
                  Expect.isTrue
                      (OpStream.verifyChain (OpStream.canonical h sw) migrated)
                      "the migrated chain verifies under canonical"

                  Expect.equal
                      (migrated |> List.map (fun r -> r.Seq, r.Actor, r.Op))
                      (legacy |> List.map (fun r -> r.Seq, r.Actor, r.Op))
                      "rehash touches only the hash chain — the records are the source of truth"

                  Expect.notEqual
                      (OpStream.head StreamConfig.canonical migrated)
                      (OpStream.head StreamConfig.canonical legacy)
                      "the bytes moved, so the head moved"

                  Expect.equal
                      (OpStream.head StreamConfig.canonical migrated)
                      (OpStream.head StreamConfig.canonical (buildWith StreamConfig.canonical controlActors))
                      "the migrated chain is the chain a canonical writer would have produced"

          testCase
              "a store whose actors carry no control character hashes identically under both — proved by the rehash"
          <| fun _ ->
              let legacy = buildWith StreamConfig.legacyEscape plainActors

              Expect.isTrue
                  (OpStream.verifyChain (OpStream.canonical h sw) legacy)
                  "an unmigrated control-free store already verifies under canonical"

              match
                  OpStream.rehash (OpStream.ofConfig StreamConfig.legacyEscape h sw) (OpStream.canonical h sw) legacy
              with
              | Error e -> failtestf "rehash refused: %A" e
              | Ok migrated ->
                  Expect.equal migrated legacy "the rehash is a no-op: every record, every hash, byte-identical"

          testCase "rehash refuses a legacy store that was tampered"
          <| fun _ ->
              let legacy = buildWith StreamConfig.legacyEscape controlActors

              let tampered =
                  legacy
                  |> List.mapi (fun i r ->
                      if i = 1 then
                          { r with
                              Actor = Agent("m\n", "1\t", "mallory") }
                      else
                          r)

              match
                  OpStream.rehash (OpStream.ofConfig StreamConfig.legacyEscape h sw) (OpStream.canonical h sw) tampered
              with
              | Ok _ -> failtest "a re-attributed record must not migrate"
              | Error _ -> ()

          testCase "legacyActorConfig keeps the pre-320 bytes: a bare-string actor with a newline still spells \\n"
          <| fun _ ->
              // A pre-320 writer wrote the actor as a bare JSON string with the short escape; the
              // legacy config exists to reproduce THOSE bytes, so it must not adopt the new rule.
              Expect.equal
                  (StreamConfig.legacyActor.Payload 3 (Human "a\nb") "{}")
                  "{\"seq\":3,\"actor\":\"a\\nb\",\"op\":{}}"
                  "pre-320 payload bytes are unchanged by Phase 287"

          // ---- the wire, both spines ----

          testCase "the linear JSONL round-trips a control-character actor and a control-character op"
          <| fun _ ->
              let recs =
                  buildWith StreamConfig.canonical [ Agent("m\n", "1\t", "id\r"), Note "line\u0001break\n" ]

              let text = Jsonl.write sw recs

              Expect.stringContains text "\\u000a" "the line carries the canonical spelling"
              Expect.isFalse (text.Contains "\\n") "and never the short form"

              match
                  Jsonl.readVerified (OpStream.canonical h sw) text
                  |> Result.mapError StreamLoadFault.toString
              with
              | Ok back -> Expect.equal back recs "round-trip is exact and the chain verifies"
              | Error e -> failtestf "fromJsonlVerified: %s" e

          testCase "the DAG folds the canonical spelling into a node's content id, and its JSONL round-trips"
          <| fun _ ->
              let actor = Agent("m\n", "1\t", "id\r")

              let id, dag =
                  Dag.append (OpStream.canonical h sw) actor (Note "x") "" Dag.empty
                  |> Reference.built

              Expect.equal
                  id
                  (h "" (Actor.encode actor + "|" + sw.Encode(Note "x")))
                  "the content id is hashed over Actor.encode's \\u00xx bytes"

              Expect.isTrue (Dag.verifyDag (OpStream.canonical h sw) dag) "verifies"

              let text = Dag.toJsonl sw.Encode dag
              Expect.stringContains text "\\u000d" "the node line carries the canonical spelling"
              Expect.isFalse (text.Contains "\\r") "and never the short form"

              match
                  Dag.fromJsonlVerified (OpStream.canonical h sw) text
                  |> Result.mapError Dag.loadFaultToString
              with
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

              let text = Attributed.encodeEnvelope sw.Encode a
              Expect.stringContains text "\\u0009" "the envelope carries the canonical spelling"

              match Attributed.decodeEnvelope sw.Decode text with
              | Ok back -> Expect.equal back a "round-trip"
              | Error e -> failtestf "decodeEnvelope: %s" e ]

/// Phase 370 — the generated TypeScript codecs carry a FOURTH copy of the rule: the emitted `encStr`
/// (`Emit/TypeScriptCodec.fs`), which gained Phase 365's fast path — one regex test for an escapable
/// character, and the input returned whole between quotes when there is none. These cases run the
/// function the emitter actually writes, under node, against the .NET escaper.
module EmittedEncStr =

    open Fuaran.Core.Idl

    /// The fast-path line the emitter writes, as it appears in the emitted source.
    let fastPath = """  if (!/["\\\u0000-\u001f]/.test(s)) return '"' + s + '"';"""

    /// The emitted module of a one-kind vocabulary: its prelude is every generated codec's prelude.
    let emitted () : string =
        let f n t =
            { Name = n
              Type = t
              Opt = Required
              Aliases = []
              Presence = None
              Annotations = Annotations.Empty }

        let idl: Idl =
            { Kinds =
                [ { Tag = "Note"
                    Category = "leaf"
                    Annotations = Annotations.Empty
                    Fields = [ f "label" TStr ] } ]
              Unions = []
              Enums = []
              Records = []
              Defaults = []
              NodeFields = []
              Ops = []
              Wire = WireShape.Default
              Harden = HardenPolicy.Undeclared }

        match Gen.typescriptModule idl [ "Note" ] with
        | Ok src -> src
        | Error e -> failtestf "TypeScript codegen refused a one-string vocabulary: %s" (CodegenError.describe e)

    /// The emitted `encStr` declaration, exactly as written into the module.
    let encStrBlock (src: string) : string =
        let start = src.IndexOf "const encStr = (s) => {"

        if start < 0 then
            failtest "the emitted module declares no encStr"

        let stop = src.IndexOf("\n};\n", start)

        if stop < 0 then
            failtest "the emitted encStr declaration has no end"

        src.Substring(start, stop + 4 - start)

    /// The inputs: the run-boundary corpus (clean, escape-only, first/last position, every control
    /// character alone and inside runs, surrogate pairs beside escapes), the conformance family's
    /// escape alphabet, and lone surrogates, which neither host treats as escapable.
    let inputs: (string * string) list =
        RunBoundaries.inputs
        @ [ for v in StringEscapeVectors.vectors -> "vector " + v.Name, v.Input ]
        @ [ "a lone high surrogate", "a\uD800b"
            "a lone low surrogate", "\uDC00"
            "lone surrogates beside escapes", "\uDFFF\"\n\uD800"
            "a reversed surrogate pair", "\uDC00\uD800" ]

    /// Runs `encStr` and `__broken` (when `broken` is given, a mutated copy of the declaration) over
    /// every input under node. Each line out is the output's UTF-16 code units, comma-separated, so
    /// no console encoding sits between the two hosts. `None` when node is not on PATH.
    let runUnderNode (broken: string option) : (string list * string list) option =
        let src = emitted ()

        let codes (s: string) =
            "[" + (s |> Seq.map (fun c -> string (int c)) |> String.concat ",") + "]"

        let harness =
            src
            + "\n"
            + (broken |> Option.defaultValue "const __broken = encStr;\n")
            + "const __inputs = ["
            + (inputs |> List.map (snd >> codes) |> String.concat ",")
            + "].map((cs) => String.fromCharCode(...cs));\n"
            + "const __codes = (s) => Array.from({ length: s.length }, (_, i) => s.charCodeAt(i)).join(',');\n"
            + "for (const s of __inputs) console.log(__codes(encStr(s)) + '|' + __codes(__broken(s)));\n"

        let tmp =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                sprintf "fuaran-encstr-%s.mjs" (System.Guid.NewGuid().ToString "N")
            )

        System.IO.File.WriteAllText(tmp, harness)

        try
            match ChildProcess.run (ChildProcess.command "node" ("\"" + tmp + "\"")) with
            | Error _ -> None
            | Ok p ->
                let stdout = p.Stdout
                let stderr = p.Stderr

                if p.Exit <> 0 then
                    failtestf "node failed running the emitted encStr: %s" stderr

                let rows =
                    stdout.Replace("\r\n", "\n").Split('\n')
                    |> Array.filter (fun l -> l <> "")
                    |> Array.map (fun l ->
                        let decode (cs: string) =
                            if cs = "" then
                                ""
                            else
                                System.String(cs.Split(',') |> Array.map (int >> char))

                        match l.Split('|') with
                        | [| a; b |] -> decode a, decode b
                        | _ -> failtestf "an unreadable harness line: %s" l)
                    |> List.ofArray

                Some(List.map fst rows, List.map snd rows)
        finally
            try
                System.IO.File.Delete tmp
            with _ ->
                ()

    /// Every input whose emitted bytes differ from the .NET escaper's, with what each host wrote.
    let mismatches (got: string list) : string list =
        if List.length got <> List.length inputs then
            failtestf "node answered %d inputs of %d" (List.length got) (List.length inputs)

        List.zip inputs got
        |> List.choose (fun ((name, s), out) ->
            let want = Json.render (JStr s)

            if out = want then
                None
            else
                Some(sprintf "%s: .NET %A, emitted %A" name want out))

[<Tests>]
let emittedEncStrTests =
    testList
        "StringEscape.emitted TypeScript encStr (Phase 370)"
        [ testCase "the emitted encStr carries the fast path above the unchanged escaping loop"
          <| fun _ ->
              let block = EmittedEncStr.encStrBlock (EmittedEncStr.emitted ())
              Expect.stringContains block EmittedEncStr.fastPath "the clean-string test is emitted"

              Expect.isLessThan
                  (block.IndexOf EmittedEncStr.fastPath)
                  (block.IndexOf "for (const ch of s)")
                  "and it runs before the per-character loop, which is still there"

          testCase "under node, the emitted encStr writes the .NET escaper's bytes for every input"
          <| fun _ ->
              Expect.isGreaterThan EmittedEncStr.inputs.Length 80 "the corpus is the one written"

              match EmittedEncStr.runUnderNode None with
              | None -> skiptest "node not on PATH — the emitted encStr cannot run"
              | Some(got, _) ->
                  Expect.equal (EmittedEncStr.mismatches got) [] "no input's bytes differ between the hosts"

                  for (_, s), out in List.zip EmittedEncStr.inputs got do
                      Expect.equal out ("\"" + RunBoundaries.oracle s + "\"") "and every output is the oracle's"

          testCase "the check can go red: a fast path that misses the control characters is caught"
          <| fun _ ->
              let block = EmittedEncStr.encStrBlock (EmittedEncStr.emitted ())

              let broken =
                  block.Replace("const encStr =", "const __broken =").Replace("""/["\\\u0000-\u001f]/""", """/["\\]/""")

              Expect.notEqual broken (block.Replace("const encStr =", "const __broken =")) "the mutation landed"

              match EmittedEncStr.runUnderNode (Some broken) with
              | None -> skiptest "node not on PATH — the emitted encStr cannot run"
              | Some(_, got) ->
                  let red = EmittedEncStr.mismatches got
                  Expect.isNonEmpty red "an unescaped control character is reported"

                  Expect.exists red (fun m -> m.StartsWith "vector U+000A:") "the line feed is among the inputs caught"

                  Expect.isFalse
                      (red |> List.exists (fun m -> m.StartsWith "escape-only: one quote:"))
                      "a quote still takes the escaping branch, so it is not reported" ]

// Phase 531 — the text kernel's own cases. `JsonString` is the one escape the emitters above spell
// through, and `JsonSyntax` the one string-token and number grammar both JSON readers read by; the
// readers' refusals, at their own positions and in their own vocabularies, are pinned by the refusal
// corpus and `JsonlDecodeRunTests`. What is held here is the kernel's contract as such: the escape's
// two faces agree with each other and with the one-character oracle, the frozen legacy spelling is
// its own table, the reader answers the value and the end, a refusal carries the facts a reader
// places it by, and the UTF-8 count is the encoder's length.
[<Tests>]
let kernelTests =
    let u (code: int) = string (char code)

    let refusalOf (input: string) =
        match JsonSyntax.skipString input 0 System.Int32.MaxValue with
        | Ok stop -> failwithf "%A was read to %d, not refused" input stop
        | Error r -> r

    testList
        "Text kernel (Phase 531)"
        [ testCase "JsonString: escape, escapeInto and quote are the one rule, and agree with the oracle"
          <| fun _ ->
              for name, s in RunBoundaries.inputs do
                  let into = System.Text.StringBuilder("<")
                  JsonString.escapeInto into s
                  Expect.equal (JsonString.escape s) (RunBoundaries.oracle s) (name + ": escape is the rule")
                  Expect.equal (into.ToString()) ("<" + JsonString.escape s) (name + ": escapeInto appends it")
                  Expect.equal (JsonString.quote s) ("\"" + JsonString.escape s + "\"") (name + ": quote quotes it")

              Expect.isTrue
                  (obj.ReferenceEquals(JsonString.escape "clean", "clean"))
                  "a string with nothing to escape is returned as it is"

          testCase "JsonString: the frozen legacy spelling keeps its three short forms and nothing else"
          <| fun _ ->
              let legacy (s: string) =
                  let sb = System.Text.StringBuilder()
                  JsonString.escapeLegacyInto sb s
                  sb.ToString()

              Expect.equal (legacy "\n\r\t") "\\n\\r\\t" "line feed, carriage return and tab are short"
              Expect.equal (legacy (u 0x01 + u 0x1F)) "\\u0001\\u001f" "every other control is lower-case \\u00xx"
              Expect.equal (legacy "a\"b\\c/é") "a\\\"b\\\\c/é" "the quote and the backslash, nothing else"
              Expect.equal (JsonString.quoteLegacy "\n") "\"\\n\"" "quoteLegacy quotes it"
              Expect.notEqual (legacy "\n") (JsonString.escape "\n") "the two rules differ exactly on the short forms"

          testCase "JsonSyntax.readString answers the value and the index past the token, escaped or not"
          <| fun _ ->
              let scratch = System.Text.StringBuilder()

              let read (input: string) (start: int) =
                  match JsonSyntax.readString input start System.Int32.MaxValue scratch with
                  | Ok(struct (value, stop)) -> Some(value, stop)
                  | Error _ -> None

              Expect.equal (read "x\"abc\"y" 1) (Some("abc", 6)) "an escape-free token is its body"
              Expect.equal (read "\"a\\nb\\u00e9\"" 0) (Some("a\nbé", 12)) "the escapes decode"
              Expect.equal (read "\"\\uD83D\\uDE00\"" 0) (Some(u 0xD83D + u 0xDE00, 14)) "a pair split over two escapes"

              Expect.equal
                  (read ("\"" + u 0xD83D + "\\uDE00\"") 0)
                  (Some(u 0xD83D + u 0xDE00, 9))
                  "a raw half and an escaped one"

              Expect.equal (read "\"\"" 0) (Some("", 2)) "the empty token"

              for input in [ "\"plain\""; "\"a\\tb\""; "\"\\\"q\\\"\"" ] do
                  Expect.equal
                      (JsonSyntax.skipString input 0 System.Int32.MaxValue)
                      (read input 0 |> Option.map snd |> Option.get |> Ok)
                      (input + ": skipString ends where readString does")

          testCase "JsonSyntax: a refusal carries the rule, the cursor, the unit and the pending high half"
          <| fun _ ->
              let r = refusalOf "\"ab"
              Expect.equal (r.Fault, r.Cursor, r.UnitAt) (JsonStringFault.Unterminated, 3, 3) "the end of the input"
              let r = refusalOf "\"a\\q\""

              Expect.equal
                  (r.Fault, r.Cursor, r.UnitAt)
                  (JsonStringFault.UnknownEscape 'q', 4, 2)
                  "past the letter, at its backslash"

              let r = refusalOf ("\"a" + u 0x01 + "\"")
              Expect.equal (r.Fault, r.Cursor, r.UnitAt) (JsonStringFault.RawControl(char 1), 2, 2) "AT the raw control"
              let r = refusalOf "\"\\uD800x\""

              Expect.equal
                  (r.Fault, r.Cursor, r.UnitAt)
                  (JsonStringFault.HighWithoutLow, 8, 7)
                  "after the unit that breaks it"

              Expect.equal (r.PendingHigh, r.PendingHighEscaped) (1, true) "the escaped high half it was waiting on"
              let r = refusalOf ("\"x" + u 0xD800 + "\"")
              Expect.equal (r.PendingHigh, r.PendingHighEscaped) (2, false) "a raw high half"
              let r = refusalOf "\"\\uDC00\""

              Expect.equal
                  (r.Fault, r.Cursor, r.UnitAt, r.PendingHigh)
                  (JsonStringFault.LowWithoutHigh, 7, 1, -1)
                  "a lone low escape"

              let r = refusalOf "\"\\u12G4\""

              Expect.equal
                  (r.Fault, r.Cursor, r.UnitAt)
                  (JsonStringFault.BadHexDigit, 3, 1)
                  "a non-hex digit, before the digits"

              match JsonSyntax.readString "\"abcd\"" 0 3 (System.Text.StringBuilder()) with
              | Error r ->
                  Expect.equal (r.Fault, r.Cursor) (JsonStringFault.TooLong, 5) "the bound, after its max+1-th unit"
              | Ok _ -> failtest "a four-code-point token is over a bound of three"

          testCase "JsonSyntax.isNumber is the RFC 8259 number grammar, exactly"
          <| fun _ ->
              for tok in [ "0"; "-0"; "12"; "1.5"; "-0.25"; "1e5"; "1E+5"; "2.5e-3" ] do
                  Expect.isTrue (JsonSyntax.isNumber tok) (tok + " is a number")

              for tok in [ ""; "-"; "01"; "1."; "-.5"; ".5"; "1e"; "1e+"; "+1"; "0x10"; "1 "; "NaN" ] do
                  Expect.isFalse (JsonSyntax.isNumber tok) (sprintf "%A is not" tok)

              Expect.isTrue (JsonSyntax.isWhitespace '\t') "tab is JSON whitespace"
              Expect.isFalse (JsonSyntax.isWhitespace (char 0xA0)) "a no-break space is not"

          testCase "Hash.utf8ByteCount is the length of Hash.utf8Bytes, lone surrogates included"
          <| fun _ ->
              for s in
                  [ ""
                    "a"
                    "é"
                    "中"
                    u 0xD83D + u 0xDE00
                    u 0xD800
                    u 0xDC00 + "x"
                    u 0xDBFF + u 0xD800
                    "mixed é 中 " + u 0xD83D + u 0xDE00 ] do
                  Expect.equal (Hash.utf8ByteCount s) (int64 (Hash.utf8Bytes s).Length) (sprintf "%A" s) ]
