module Fuaran.Core.Tests.VersioningTests

// Phase 319 — wire versioning + the forward/backward-compatibility contract. The defining
// laws: an older consumer decodes a newer artifact (with an unknown kind) WITHOUT crashing —
// detect + preserve + degrade — while no host can *author* an unknown kind; and an unrecognised
// kind round-trips byte-for-byte (must-ignore-but-preserve).

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// A reference codec over RNode in the canonical discipline (sorted keys via Canon.render), so
// the Known branch and the preserved-Unknown branch share one byte convention.
let rec private encodeNode (n: RNode) : JVal =
    Json.kindObj
        n.Kind
        [ "id", JStr n.Id
          "value", JStr n.Value
          "children", JArr(n.Children |> List.map encodeNode) ]

let rec private decodeNode (el: JVal) : Result<RNode, string> =
    Decode.kindOf el
    |> Result.bind (fun kind ->
        Decode.strField "id" el
        |> Result.bind (fun id ->
            Decode.strField "value" el
            |> Result.bind (fun value ->
                Decode.getProp "children" el
                |> Result.bind (Decode.mapList decodeNode)
                |> Result.map (fun kids ->
                    { Id = id
                      Kind = kind
                      Value = value
                      Hole = None
                      HoleName = ""
                      Eff = Effect.pureDeterministic
                      Children = kids }))))

// The consumer in these tests is *behind*: it understands only these two kinds. A newer
// producer authoring `hologram` / `quantumField` exercises the unknown-kind tolerance path.
let private knownTags = Set.ofList [ "doc"; "section"; "para" ]
let private isKnown (t: string) = knownTags.Contains t

/// A tolerant codec over `Decoded<RNode>`: encode re-renders canonically (the Unknown branch
/// returns its preserved payload verbatim); decode parses then routes by discriminator.
let private tolerantCodec: Corpus.Codec<Versioning.Decoded<RNode>> =
    { Encode = fun d -> Canon.render (Versioning.reencode encodeNode d)
      Decode =
        fun s ->
            Decode.parse s
            |> Result.bind (Versioning.decodeTolerant Decode.kindOf isKnown decodeNode) }

[<Tests>]
let tests =
    testList
        "Versioning"
        [
          // ---- Profile id ----
          testCase "Profile renders and parses round-trip"
          <| fun _ ->
              let p = Versioning.Profile.coreV1
              Expect.equal (Versioning.Profile.render p) "core@1.0" "canonical form"

              match Versioning.Profile.tryParse "core@2.7" with
              | Ok q ->
                  Expect.equal q.Name "core" "name"
                  Expect.equal q.Major 2 "major"
                  Expect.equal q.Minor 7 "minor"
              | Error m -> failtest m

          testCase "Profile.tryParse rejects malformed ids"
          <| fun _ ->
              Expect.isError (Versioning.Profile.tryParse "core") "missing @"
              Expect.isError (Versioning.Profile.tryParse "core@1") "missing minor"
              Expect.isError (Versioning.Profile.tryParse "core@1.x") "non-numeric"
              Expect.isError (Versioning.Profile.tryParse "@1.0") "missing name"

          // ---- Capability negotiation ----
          testCase "negotiate classifies current / behind / foreign"
          <| fun _ ->
              let consumer =
                  { Versioning.Name = "core"
                    Versioning.Major = 1
                    Versioning.Minor = 3 }

              let current = { consumer with Versioning.Minor = 2 }

              let behind = { consumer with Versioning.Minor = 5 }

              let foreignMajor = { consumer with Versioning.Major = 2 }

              let foreignName =
                  { consumer with
                      Versioning.Name = "music" }

              Expect.equal (Versioning.negotiate consumer current) Versioning.Current "<= minor ⇒ Current"
              Expect.equal (Versioning.negotiate consumer consumer) Versioning.Current "== ⇒ Current"
              Expect.equal (Versioning.negotiate consumer behind) (Versioning.Behind behind) "higher minor ⇒ Behind"

              Expect.equal
                  (Versioning.negotiate consumer foreignMajor)
                  (Versioning.Foreign foreignMajor)
                  "diff major ⇒ Foreign"

              Expect.equal
                  (Versioning.negotiate consumer foreignName)
                  (Versioning.Foreign foreignName)
                  "diff name ⇒ Foreign"

          // ---- Versioned envelope ----
          testCase "Envelope round-trips through render/parse carrying the profile"
          <| fun _ ->
              let env: Versioning.Envelope =
                  { Profile = Versioning.Profile.coreV1
                    Payload = encodeNode (sample ()) }

              let wire = Versioning.render env
              Expect.stringContains wire "\"$profile\":\"core@1.0\"" "version field on the wire"

              match Versioning.parse wire with
              | Ok back ->
                  Expect.equal back.Profile env.Profile "profile preserved"
                  // The canonical invariant is byte-stable round-trip, not in-memory JObj key
                  // order — `Canon.render` sorts keys, so compare the canonical wire forms.
                  Expect.equal (Canon.render back.Payload) (Canon.render env.Payload) "payload preserved"
                  Expect.equal (Versioning.render back) wire "envelope re-renders byte-identically"
              | Error m -> failtest m

          // ---- Transport-only Unknown: detect ----
          testCase "decodeTolerant: a known kind decodes to Known"
          <| fun _ ->
              let wire = Canon.render (encodeNode (RNode.leaf "a1" "para" "x"))

              match tolerantCodec.Decode wire with
              | Ok(Versioning.Known n) -> Expect.equal n.Kind "para" "known kind"
              | Ok(Versioning.Unknown _) -> failtest "para should be Known, not Unknown"
              | Error m -> failtest m

          testCase "decodeTolerant: an unknown kind is detected, not rejected"
          <| fun _ ->
              // A newer producer authored a `hologram` node + declared the profile it needs.
              let wire =
                  "{\"id\":\"h1\",\"kind\":\"hologram\",\"requiredProfile\":\"core@1.4\",\"shimmer\":true}"

              match tolerantCodec.Decode wire with
              | Ok(Versioning.Unknown u) ->
                  Expect.equal u.Kind "hologram" "captures the unknown discriminator"

                  Expect.equal
                      u.RequiredProfile
                      (Some(Versioning.Profile.coreV1 |> fun p -> { p with Minor = 4 }))
                      "reads requiredProfile"
              | Ok(Versioning.Known _) -> failtest "hologram is unknown to this consumer"
              | Error m -> failtest m

          // ---- Must-ignore-but-preserve: byte-for-byte ----
          testCase "an unknown kind round-trips byte-for-byte"
          <| fun _ ->
              // Canonical (Ordinal-sorted keys) so a re-render reproduces the input bytes.
              let wire = "{\"id\":\"q1\",\"kind\":\"quantumField\",\"spin\":[\"up\",\"down\"]}"

              match tolerantCodec.Decode wire with
              | Ok decoded ->
                  let reEmitted = tolerantCodec.Encode decoded
                  Expect.equal reEmitted wire "preserved verbatim — old client cannot destroy newer data"
              | Error m -> failtest m

          testCase "a nested unknown kind inside a known tree is preserved (custom driver)"
          <| fun _ ->
              // The consumer knows `doc` but `doc`'s child is an unknown `widget`. Decoding the
              // child tolerantly and re-encoding preserves its bytes inside the larger tree.
              let childWire = "{\"glyph\":\"%\",\"id\":\"w1\",\"kind\":\"widget\"}"

              match
                  Decode.parse childWire
                  |> Result.bind (Versioning.decodeTolerant Decode.kindOf isKnown decodeNode)
              with
              | Ok(Versioning.Unknown u) ->
                  Expect.equal (Canon.render u.Payload) childWire "child payload preserved byte-for-byte"
              | Ok(Versioning.Known _) -> failtest "widget is unknown"
              | Error m -> failtest m

          // ---- Authoring surface stays closed: Unknown is un-constructible on encode ----
          // (Compile-time guarantee — `Versioning.Unknown` is only reachable as a `decodeTolerant`
          //  result; there is no encode entry point that takes one. The reencode of a Known value
          //  never produces Unknown bytes.)
          testCase "reencode of a Known value emits canonical bytes (no Unknown leakage)"
          <| fun _ ->
              let n = RNode.leaf "a1" "para" "x"
              let viaTolerant = tolerantCodec.Encode(Versioning.Known n)
              let direct = Canon.render (encodeNode n)
              Expect.equal viaTolerant direct "Known re-encode == direct canonical encode"

          // ---- Evolution policy: minor vs major ----
          testCase "classify: additive change is minor, removal/rename is major"
          <| fun _ ->
              let v1 = Set.ofList [ "para"; "section" ]
              let additive = Set.ofList [ "para"; "section"; "callout" ]
              let removal = Set.ofList [ "para" ]
              let rename = Set.ofList [ "para"; "panel" ] // section → panel

              Expect.equal (Versioning.classify v1 additive) (Versioning.Additive [ "callout" ]) "added only ⇒ Additive"

              Expect.equal
                  (Versioning.classify v1 removal)
                  (Versioning.Breaking([ "section" ], []))
                  "removed ⇒ Breaking"

              match Versioning.classify v1 rename with
              | Versioning.Breaking(removed, added) ->
                  Expect.equal removed [ "section" ] "rename removes the old tag"
                  Expect.equal added [ "panel" ] "rename adds the new tag"
              | Versioning.Additive _ -> failtest "a rename is breaking"

          testCase "bump: additive bumps minor, breaking bumps major and resets minor"
          <| fun _ ->
              let p =
                  { Versioning.Name = "core"
                    Versioning.Major = 1
                    Versioning.Minor = 3 }

              Expect.equal (Versioning.bump p (Versioning.Additive [])) p "no-op additive ⇒ unchanged"

              Expect.equal
                  (Versioning.bump p (Versioning.Additive [ "callout" ]))
                  { p with Versioning.Minor = 4 }
                  "additive ⇒ minor+1"

              Expect.equal
                  (Versioning.bump p (Versioning.Breaking([ "section" ], [])))
                  { p with
                      Versioning.Major = 2
                      Versioning.Minor = 0 }
                  "breaking ⇒ major+1, minor reset"

          // ---- Corpus: the version field + an unknown-kind tolerance case ----
          testCase "corpus exercises a Known round-trip and an Unknown-tolerance round-trip"
          <| fun _ ->
              let cases =
                  [ { Corpus.Name = "known-para"
                      Corpus.Kind = Corpus.RoundTrip
                      Corpus.Json = Canon.render (encodeNode (RNode.leaf "a1" "para" "x"))
                      Corpus.Tag = "known" }
                    { Corpus.Name = "unknown-hologram"
                      Corpus.Kind = Corpus.RoundTrip
                      Corpus.Json = "{\"id\":\"h1\",\"kind\":\"hologram\",\"shimmer\":true}"
                      Corpus.Tag = "unknown-tolerance" } ]

              let outcomes = Corpus.runCorpus tolerantCodec cases
              Expect.isTrue (outcomes |> List.forall (fun o -> o.Passed)) "all corpus cases pass"
              Expect.isOk (Corpus.coverageGate [ "known"; "unknown-tolerance" ] cases) "both tags covered" ]
