namespace Fuaran.Core

// ============================================================================
//  The string-escape conformance vectors (Phase 287).
//
//  The spine has ONE rule for spelling a string inside canonical JSON — `"` as `\"`, `\` as
//  `\\`, every control character `U+0000`–`U+001F` as lower-case `\u00xx`, nothing else — and
//  TWO bodies that carry it: `Wire.Json.escape` (the original; `Canonical.write Rendering.CrossHost` escapes through
//  it) and the copy in `Fuaran.Core.OpStream` (`Jsonl.quote`), which DECISIONS.md D2
//  demands because that package takes no `Wire` reference. `Actor.encode` spells through the
//  copy, and so does `Dag.toJsonl` in `Fuaran.Core.OpStream.Dag` — which references
//  `OpStream` and so carries no copy of its own since Phase 388. A copy is safe only while it
//  is held to the original, and this family is what holds it: for every character the rule
//  escapes, and for the actors a chain pre-image carries, it pins the exact bytes each emitter
//  writes against one table.
//
//  Why the bytes matter: the actor is folded into the chain hash, so an escaper that spells
//  `\n` where another host spells `\u000a` gives the same record two chain hashes — which is
//  what the linear chain and the DAG chain did before Phase 287. A host in any language that
//  claims to reproduce this package's chain hashes satisfies these vectors or does not.
//
//  It sits beside the law kit as `WireNullTolerance` does — a VECTOR family: a runner over an
//  enumerated table with a `LawResult` projection (`laws`), because nothing here is drawn: the
//  alphabet is the whole of what the rule escapes, enumerated. FSharp.Core only, Fable-clean.
// ============================================================================

/// The string-escape conformance vectors + their runner.
module StringEscapeVectors =

    open VectorKit

    /// One character the rule escapes (or, as a control, one it must leave alone), with the
    /// exact bytes every escaper on the spine emits for it — and the bytes the linear chain
    /// wrote for it BEFORE Phase 287, which `StreamConfig.legacyEscape` still reproduces.
    type Vector =
        {
            /// The case label — `U+XXXX` (uppercase hex) for a control character, a word for the
            /// rest — prefixed to every outcome the runner reports for this vector.
            Name: string
            /// The one-character (or, for the plain-text control, longer) input.
            Input: string
            /// The canonical spelling: the body of the JSON string literal, quotes excluded.
            Escaped: string
            /// The pre-Phase-287 spelling: `\n` / `\r` / `\t` short, everything else as `Escaped`.
            Legacy: string
        }

    /// The escape alphabet, enumerated: every control character `U+0000`–`U+001F`, the quote
    /// and the backslash, and one plain-text control showing that a solidus, a non-ASCII
    /// character and an astral one pass through unescaped.
    let vectors: Vector list =
        [ for c, escaped, legacy in controlCharacters do
              { Name = sprintf "U+%04X" c
                Input = string (char c)
                Escaped = escaped
                Legacy = legacy }
          { Name = "quote"
            Input = "\""
            Escaped = "\\\""
            Legacy = "\\\"" }
          { Name = "backslash"
            Input = "\\"
            Escaped = "\\\\"
            Legacy = "\\\\" }
          { Name = "plain text passes through"
            Input = "a /b\u00e9\U0001F642"
            Escaped = "a /b\u00e9\U0001F642"
            Legacy = "a /b\u00e9\U0001F642" } ]

    /// The actors the phase names, with the exact `Actor.encode` bytes each must produce: an
    /// `Agent` carrying a newline, a tab and a carriage return across its three fields, and a
    /// `Human` carrying a NUL. These are the records a shared chain corpus certifies against.
    let actorVectors: (string * Actor * string) list =
        [ "agent with LF, TAB and CR",
          Agent("m\n", "1\t", "id\r"),
          "{\"kind\":\"agent\",\"model\":\"m\\u000a\",\"version\":\"1\\u0009\",\"id\":\"id\\u000d\"}"
          "human with NUL", Human "\u0000", "{\"kind\":\"human\",\"id\":\"\\u0000\"}"
          "human with no control character", Human "u", "{\"kind\":\"human\",\"id\":\"u\"}" ]

    /// The DAG node line for a node whose id and only parent are both `id`.
    let private nodeLine (id: string) : string =
        "{\"node\":true,\"id\":"
        + quoted id
        + ",\"parents\":["
        + quoted id
        + "],\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{}}"

    /// Every check one character vector makes, named.
    let runVector (v: Vector) : Corpus.Outcome list =
        let human = "{\"kind\":\"human\",\"id\":" + quoted v.Escaped + "}"

        let humanLegacy = "{\"kind\":\"human\",\"id\":" + quoted v.Legacy + "}"

        let agent =
            "{\"kind\":\"agent\",\"model\":"
            + quoted v.Escaped
            + ",\"version\":"
            + quoted v.Escaped
            + ",\"id\":"
            + quoted v.Escaped
            + "}"

        // Filed under its own id, so `Dag.ofNodes` (the constructor is private since Phase 386)
        // cannot refuse it; reaching the refusal is a defect in this vector, raised as one.
        let dag: Dag.T<unit> =
            match
                Dag.ofNodes (
                    Map.ofList
                        [ v.Input,
                          { Id = v.Input
                            Parents = [ v.Input ]
                            Actor = Human "a"
                            Op = () } ]
                )
            with
            | Ok d -> d
            | Error m -> invalidOp (sprintf "a node filed under %s carries id %s" m.Key m.NodeId)

        [ // the rule at its home
          expect (v.Name + " / Wire.Json.escape") "Json.escape" v.Escaped (Json.escape v.Input)
          expect
              (v.Name + " / Wire.Canonical.write Rendering.CrossHost")
              "Canonical.write Rendering.CrossHost"
              (quoted v.Escaped)
              (Canonical.write Rendering.CrossHost (JStr v.Input))
          expect (v.Name + " / Wire.Json.render") "Json.render" (quoted v.Escaped) (Json.render (JStr v.Input))
          // the read side accepts what the encoder writes, and what it wrote before
          (match Json.parse (quoted v.Escaped) with
           | Ok(JStr s) when s = v.Input -> pass (v.Name + " / Wire.Json.parse reads the canonical spelling")
           | other -> fail (v.Name + " / Wire.Json.parse reads the canonical spelling") (sprintf "%A" other))
          (match Json.parse (quoted v.Legacy) with
           | Ok(JStr s) when s = v.Input -> pass (v.Name + " / Wire.Json.parse reads the legacy spelling")
           | other -> fail (v.Name + " / Wire.Json.parse reads the legacy spelling") (sprintf "%A" other))
          // the OpStream copy, through the only surface it has: the actor pre-image
          expect (v.Name + " / Actor.encode Human") "Actor.encode" human (Actor.encode (Human v.Input))
          expect
              (v.Name + " / Actor.encode Agent")
              "Actor.encode"
              agent
              (Actor.encode (Agent(v.Input, v.Input, v.Input)))
          // the linear chain pre-image, canonical and legacy
          expect
              (v.Name + " / StreamConfig.canonical.Payload")
              "StreamConfig.canonical.Payload"
              (payload human)
              (StreamConfig.canonical.Payload 0 (Human v.Input) "{}")
          expect
              (v.Name + " / StreamConfig.legacyEscape.Payload")
              "StreamConfig.legacyEscape.Payload"
              (payload humanLegacy)
              (StreamConfig.legacyEscape.Payload 0 (Human v.Input) "{}")
          // the no-op claim, both ways: the two payloads agree exactly when the spellings do
          (let canonical = StreamConfig.canonical.Payload 0 (Human v.Input) "{}"
           let legacy = StreamConfig.legacyEscape.Payload 0 (Human v.Input) "{}"
           let same = canonical = legacy

           if same = (v.Escaped = v.Legacy) then
               pass (
                   v.Name
                   + " / legacy and canonical payloads differ exactly where the spellings do"
               )
           else
               fail
                   (v.Name
                    + " / legacy and canonical payloads differ exactly where the spellings do")
                   (sprintf "payloads equal: %b; spellings equal: %b" same (v.Escaped = v.Legacy)))
          // the DAG copy, through its node line
          expect (v.Name + " / Dag.toJsonl") "Dag.toJsonl" (nodeLine v.Escaped) (Dag.toJsonl (fun () -> "{}") dag) ]

    /// The named-actor checks: `Actor.encode` bytes, and the linear chain pre-image built on them.
    let runActor (name: string, actor: Actor, encoded: string) : Corpus.Outcome list =
        [ expect (name + " / Actor.encode") "Actor.encode" encoded (Actor.encode actor)
          expect
              (name + " / StreamConfig.canonical.Payload")
              "StreamConfig.canonical.Payload"
              (payload encoded)
              (StreamConfig.canonical.Payload 0 actor "{}") ]

    /// One row of the family (Phase 508): a character the rule escapes, or a named actor.
    type internal Entry =
        /// A character vector, checked by `runVector`.
        | Character of Vector
        /// A named actor with its exact `Actor.encode` bytes, checked by `runActor`.
        | NamedActor of name: string * actor: Actor * encoded: string

    let private entries (vs: Vector list) (actors: (string * Actor * string) list) : Entry list =
        List.map Character vs @ List.map NamedActor actors

    /// The family over one shape (Phase 508): the character vectors then the named actors, each line
    /// `name<TAB>escaped<TAB>Actor.encode (Human input)` for a character and
    /// `name<TAB>encoded<TAB>chain payload` for an actor.
    let internal family: VectorFamily<Entry> =
        { Label = "string escape"
          Corpus = entries vectors actorVectors
          Name =
            function
            | Character v -> v.Name
            | NamedActor(name, _, _) -> name
          Check =
            function
            | Character v -> runVector v
            | NamedActor(name, actor, encoded) -> runActor (name, actor, encoded)
          Line =
            function
            | Character v -> v.Name + "\t" + v.Escaped + "\t" + Actor.encode (Human v.Input)
            | NamedActor(name, actor, encoded) ->
                name + "\t" + encoded + "\t" + StreamConfig.canonical.Payload 0 actor "{}" }

    /// Run the whole family.
    let run () : Corpus.Outcome list = VectorFamily.run family

    /// The whole family as a single verdict — `Ok` or the first failing check, named.
    let check () : Result<unit, string> = VectorFamily.check family

    /// The table as lines a host in another language, or the same package under another
    /// pipeline, can diff: `name<TAB>escaped<TAB>Actor.encode (Human input)` per character vector,
    /// then `name<TAB>encoded<TAB>chain payload` per named actor. Deterministic; no seed.
    let lines () : string list = VectorFamily.lines family

    /// The family as `LawResult`s over a table the caller hands it (Phase 390; `laws ()` is this over
    /// the committed `vectors` and `actorVectors`): one law per character vector and per named actor,
    /// green exactly when every check `runVector` / `runActor` makes for it passes; one law on the
    /// FORMAT `lines` renders — each character vector as `name<TAB>escaped<TAB>{"kind":"human",
    /// "id":"escaped"}` and each named actor as `name<TAB>encoding<TAB>` its chain pre-image, in table
    /// order, no field carrying a tab or a line break; and the corpus law, whose evidence is one
    /// assertion per vector evaluated, so a run handed no vectors is red by name (`string escape: the
    /// corpus evaluated at least one vector`). The format law is stated from the TABLE, not by calling
    /// the escapers, so a renderer that drifted from the table is caught even where the escapers still
    /// agree with it. `VectorFamily.lawsWith` over `family` since Phase 508.
    let lawsWith (vs: Vector list) (actors: (string * Actor * string) list) : LawResult list =
        let table = VectorFamily.over (entries vs actors) family

        let expected =
            [ for v in vs ->
                  v.Name
                  + "\t"
                  + v.Escaped
                  + "\t"
                  + "{\"kind\":\"human\",\"id\":"
                  + quoted v.Escaped
                  + "}"
              for name, _, encoded in actors -> name + "\t" + encoded + "\t" + payload encoded ]

        let rendered = VectorFamily.lines table

        let format =
            LawKit.LawCell
                "lines () renders the table as name, spelling and pre-image, tab-separated, in table order, one line each"

        let wellFormed (l: string) =
            l.Split('\t').Length = 3 && not (l.Contains "\n") && not (l.Contains "\r")

        format.Check(
            rendered = expected && List.forall wellFormed rendered,
            fun () ->
                match
                    List.zip (List.truncate expected.Length rendered) (List.truncate rendered.Length expected)
                    |> List.tryFind (fun (a, b) -> a <> b)
                with
                | Some(got, want) -> sprintf "lines () rendered %A where the table says %A" got want
                | None ->
                    sprintf
                        "lines () rendered %d lines for a %d-entry table, or a line that is not three tab-separated fields"
                        rendered.Length
                        expected.Length
        )

        VectorFamily.lawsWith [ format.Result ] table
        |> LawKit.labelAs "StringEscapeVectors.lawsWith"

    /// The family as `LawResult`s (Phase 349), rostered as `StringEscapeVectors.laws` on the
    /// `WireNullTolerance.laws` precedent — `lawsWith vectors actorVectors`. The corpus is fixed, so
    /// the one run is the whole sample; the committed rendering a host in another language diffs
    /// against is `conformance/escape/string-escape.json`.
    let laws () : LawResult list =
        lawsWith vectors actorVectors |> LawKit.labelAs "StringEscapeVectors.laws"
