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

[<Tests>]
let tests =
    testList
        "StringEscape"
        [ testCase "the conformance family is green, and it measured something"
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
