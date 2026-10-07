namespace Fuaran.Core

// ============================================================================
//  The string-escape conformance vectors (Phase 287).
//
//  The spine has ONE rule for spelling a string inside canonical JSON — `"` as `\"`, `\` as
//  `\\`, every control character `U+0000`–`U+001F` as lower-case `\u00xx`, nothing else — and
//  TWO bodies that carry it: `Wire.Json.escape` (the original; `Canon.render` escapes through
//  it) and the copy in `Fuaran.Core.OpStream` (`OpStream.Jsonl.quote`), which DECISIONS.md D2
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
//  It sits beside the law kit as `WireNullTolerance` does — a vector family with a runner, not
//  a `LawResult` family — because nothing here is drawn: the alphabet is the whole of what the
//  rule escapes, enumerated. FSharp.Core only, Fable-clean.
// ============================================================================

/// The string-escape conformance vectors + their runner.
module StringEscapeVectors =

    open VectorKit

    /// One character the rule escapes (or, as a control, one it must leave alone), with the
    /// exact bytes every escaper on the spine emits for it — and the bytes the linear chain
    /// wrote for it BEFORE Phase 287, which `OpStream.legacyEscapeConfig` still reproduces.
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
        [ for c in 0..0x1F do
              let escaped = sprintf "\\u%04x" c

              let legacy =
                  match c with
                  | 0x0A -> "\\n"
                  | 0x0D -> "\\r"
                  | 0x09 -> "\\t"
                  | _ -> escaped

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

        let dag: Dag.T<unit> =
            { Nodes =
                Map.ofList
                    [ v.Input,
                      { Id = v.Input
                        Parents = [ v.Input ]
                        Actor = Human "a"
                        Op = () } ] }

        [ // the rule at its home
          expect (v.Name + " / Wire.Json.escape") "Json.escape" v.Escaped (Json.escape v.Input)
          expect (v.Name + " / Wire.Canon.render") "Canon.render" (quoted v.Escaped) (Canon.render (JStr v.Input))
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
              (v.Name + " / OpStream.canonicalConfig.Payload")
              "canonicalConfig.Payload"
              (payload human)
              (OpStream.canonicalConfig.Payload 0 (Human v.Input) "{}")
          expect
              (v.Name + " / OpStream.legacyEscapeConfig.Payload")
              "legacyEscapeConfig.Payload"
              (payload humanLegacy)
              (OpStream.legacyEscapeConfig.Payload 0 (Human v.Input) "{}")
          // the no-op claim, both ways: the two payloads agree exactly when the spellings do
          (let canonical = OpStream.canonicalConfig.Payload 0 (Human v.Input) "{}"
           let legacy = OpStream.legacyEscapeConfig.Payload 0 (Human v.Input) "{}"
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
              (name + " / OpStream.canonicalConfig.Payload")
              "canonicalConfig.Payload"
              (payload encoded)
              (OpStream.canonicalConfig.Payload 0 actor "{}") ]

    /// Run the whole family.
    let run () : Corpus.Outcome list =
        (vectors |> List.collect runVector) @ (actorVectors |> List.collect runActor)

    /// The whole family as a single verdict — `Ok` or the first failing check, named.
    let check () : Result<unit, string> =
        match run () |> List.filter (fun o -> not o.Passed) with
        | [] -> Ok()
        | first :: _ -> Error(first.Name + ": " + first.Detail)

    /// The table as lines a host in another language, or the same package under another
    /// pipeline, can diff: `name<TAB>escaped<TAB>Actor.encode (Human input)` per character vector,
    /// then `name<TAB>encoded<TAB>chain payload` per named actor. Deterministic; no seed.
    let lines () : string list =
        [ for v in vectors -> v.Name + "\t" + v.Escaped + "\t" + Actor.encode (Human v.Input)
          for name, actor, encoded in actorVectors ->
              name + "\t" + encoded + "\t" + OpStream.canonicalConfig.Payload 0 actor "{}" ]

    /// The family as `LawResult`s (Phase 349), rostered as `StringEscapeVectors.laws` on the
    /// `WireNullTolerance.laws` precedent: one law per character vector and per named actor, green
    /// exactly when every check `runVector` / `runActor` makes for it passes, and one law on the
    /// FORMAT `lines` renders — each character vector as `name<TAB>escaped<TAB>{"kind":"human",
    /// "id":"escaped"}` and each named actor as `name<TAB>encoding<TAB>` its chain pre-image, in table
    /// order, no field carrying a tab or a line break. The format law is stated from the TABLE, not
    /// by calling the escapers, so a renderer that drifted from the table is caught even where the
    /// escapers still agree with it. The corpus is fixed, so the one run is the whole sample; the
    /// committed rendering a host in another language diffs against is
    /// `conformance/escape/string-escape.json`.
    let laws () : LawResult list =
        let verdict (name: string) (outcomes: Corpus.Outcome list) : LawResult =
            let law = LawKit.LawCell("string escape: " + name)

            match outcomes |> List.tryFind (fun o -> not o.Passed) with
            | Some o -> law.Check(false, (fun () -> o.Name + ": " + o.Detail))
            | None -> law.Saw()

            law.Result

        let expected =
            [ for v in vectors ->
                  v.Name
                  + "\t"
                  + v.Escaped
                  + "\t"
                  + "{\"kind\":\"human\",\"id\":"
                  + quoted v.Escaped
                  + "}"
              for name, _, encoded in actorVectors -> name + "\t" + encoded + "\t" + payload encoded ]

        let rendered = lines ()

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

        (vectors |> List.map (fun v -> verdict v.Name (runVector v)))
        @ (actorVectors |> List.map (fun ((name, _, _) as a) -> verdict name (runActor a)))
        @ [ format.Result ]
