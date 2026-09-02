module Fuaran.Core.Tests.ScoreDomainSpike

open System
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Third-vocabulary readiness spike — throwaway code, keep-the-findings.
//
// The second-vocabulary spike (`SecondDomainSpike.fs`) measured the IDL against
// a document-shaped tree language and left one question deliberately open: its
// two headline negatives — no demand for declarable transparent unions, no
// demand for an enum case/wire split — were findings about ONE foreign
// vocabulary, and a score-shaped vocabulary was named as the next most likely
// to answer differently, its leaf vocabularies (pitch spellings, dynamics,
// durations) being classic bare-scalar-shorthand territory. This file declares
// a slice of exactly such a vocabulary — a score-shaped (music-notation) tree
// language, deliberately not this repository's own — and certifies it against a
// corpus of sample scores written in that vocabulary's wire shape, vendored
// beside this file (`fixtures/score-domain/`) so the certification runs in any
// clone.
//
// The deliverable is the MEASUREMENT, not a shipped vocabulary. Nothing below
// is consumed by any other suite, no engine behaviour is changed by it, and the
// declared slice is not a contract anyone may depend on.
//
// WHAT THE SPIKE FOUND — the readiness report, recorded beside the code that
// produced it (each item is asserted by a test in this file, so it cannot rot
// silently into prose):
//
//  1. BLOCKER CONFIRMED (second instance), now CLOSED (Phase 108) — the
//     discriminator KEY was hard-coded to `$type` and this vocabulary, like the
//     document-shaped one, tags its nodes with a bare-string `kind`. Two
//     independent foreign vocabularies demanded the declarable key, and it is
//     now a declared slot (`Idl.Wire.Discriminator`) this file exercises
//     directly.
//
//  2. BLOCKER CONFIRMED (second instance), now CLOSED (Phase 109) — the node
//     ENVELOPE's SHAPE was hard-coded, and this vocabulary's node is FLAT (tag,
//     id and kind fields share one object), again exactly as the
//     document-shaped vocabulary's is. Two of two foreign vocabularies chose
//     the flat shape — which is what `NodeEnvelopeShape.FlatKind` now declares;
//     the shape adapter that quarantined both blockers is DELETED and the
//     certification below runs the corpus in its NATIVE shape.
//
//  3. HEADLINE NEGATIVE RESULT — transparent unions are NOT demanded by this
//     vocabulary either, and more strongly than the second spike could say it:
//     the score vocabulary declares NO value union at all. Its only
//     discriminated union is the node discriminator itself; every other closed
//     choice is an enum (a bare string from a closed set). There is no
//     union-typed field position in which a transparent case could even arise.
//     That is the SECOND consecutive pinned negative for declarable
//     transparency (D14) — the bare-scalar-shorthand intuition about score
//     vocabularies turns out to describe their ENUMS, which the type model
//     already covers, not their unions.
//
//     Nuance, priced by an authored probe: the vocabulary's one payload-carrying
//     closed set (an ornament vocabulary whose tremolo variant carries a slash
//     count) is encoded not as a tagged union but as an enum-valued field with
//     the variant's payload FLATTENED beside it. That is expressible today as
//     `TEnum` + an `Optional` int — at the cost of the case↔payload coupling
//     invariant, which the flattening cannot state (the probe shows the illegal
//     combination encodes without complaint). A validator concern above the
//     IDL, not a transparency demand: the variant is spelled by a tagged
//     STRING, never by a bare scalar standing for a whole case.
//
//  4. POSITIVE RESULT — omit-at-default is demanded HEAVILY and is already
//     expressible. This vocabulary's wire economy omits a voice of 1, a dot
//     count of 0 and false boolean flags on emit and reconstitutes them on
//     decode — and `Optionality.OmitDefault` states exactly that, including
//     inside `TRecord` positions (a duration's dot count), because encode and
//     decode run one shared field walk. Where the document-shaped vocabulary
//     surfaced an optionality GAP (explicit null), this one lands squarely on
//     the model's existing case. The vendored corpus exercises the discipline
//     in BOTH directions (populated in one sample, omitted in the ensemble
//     samples) — the second spike recorded that its corpus never exercised its
//     optionality finding, and that trap is avoided here by construction.
//
//  5. POSITIVE RESULT — non-discriminated records are demanded heavily and
//     `TRecord` covers all of them: pitch, duration, key and time signatures, a
//     staff definition (a record nesting two further records), and a form
//     section (a record carrying a NODE LIST — record-over-tree recursion).
//     All certify byte-identically through the interpreter.
//
//  6. NEGATIVE RESULT — the enum case/wire split is NOT demanded by this
//     vocabulary either. All twelve closed sets (78 wire strings, mode names
//     with accidental suffixes included) spell their wire strings as legal
//     PascalCase F# identifiers.
//
//  7. NEGATIVE RESULT — no tuple position surfaced here either. The shapes that
//     might have been tuples (a tuplet's ratio, a time signature) are named
//     fields on kinds or records, which the model already carries.
//
//  8. BY DESIGN, not a gap — the pitch record carries a REDUNDANT derived field
//     (a MIDI number computable from its letter, accidental and octave). The
//     IDL declares it as an ordinary required int and round-trips it
//     faithfully; the derivation invariant is validator policy above the
//     generated layer, exactly where the structure/policy boundary puts it. No
//     IDL position for "derived" is needed for wire fidelity.
//
//  9. BY DESIGN, not a gap — the vocabulary's DOCUMENT envelope (a version
//     stamp beside the root) has no IDL position, because the IDL is a
//     node-vocabulary tool. Same disposition as the second spike's finding (8);
//     the corpus reduces the envelope to its `root` member.
//
// The useful shape of the headline: the two blockers that stopped an adopter at
// the first byte were TWICE-confirmed here and then CLOSED as Phases 108/109 on
// exactly this evidence, while the queued transparency generalisation has two
// domains of measured counter-evidence and none in favour. Findings (4) and (5)
// are the inverse lesson — the two model features this vocabulary leans on
// hardest already existed.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// The declared slice
// ---------------------------------------------------------------------------

let private f (name: string) (t: IdlType) (opt: Optionality) : IdlField =
    { Name = name
      Type = t
      Opt = opt
      Annotations = Annotations.Empty }

let private req name t = f name t Required
let private opt name t = f name t Optional
let private omit name t d = f name t (OmitDefault d)

let private kind tag category fields : IdlKind =
    { Tag = tag
      Category = category
      Fields = fields }

let private record name fields : IdlRecord = { Name = name; Fields = fields }

let private children = req "children" (TList TNode)

/// A slice of a score-shaped vocabulary's node set — every kind its sample
/// corpus reaches: ensemble structure (score / part-group / part / measure /
/// staff), pitched events (note / chord / grace note), spanners entering and
/// leaving (hairpins, slurs, octave shifts), point marks (dynamic, fermata,
/// ornament, rehearsal and navigation marks), a multi-measure rest, and a form
/// summary whose sections are records carrying node lists.
let scoreIdl: Idl =
    { Kinds =
        [ kind "Score" "structure" [ opt "title" TStr; opt "composer" TStr; children ]
          kind "Part" "structure" [ req "name" TStr; req "staves" (TList(TRecord "StaffDefinition")); children ]
          kind "PartGroup" "structure" [ opt "name" TStr; req "bracket" (TEnum "BracketKind"); children ]
          kind
              "Measure"
              "structure"
              [ req "number" TInt
                omit "repeatStart" TBool (VBool false)
                omit "repeatEnd" TBool (VBool false)
                opt "volta" (TList TInt)
                omit "isAnacrusis" TBool (VBool false)
                children ]
          kind "Staff" "structure" [ req "staffNumber" TInt; children ]
          kind
              "Note"
              "event"
              [ req "pitch" (TRecord "Pitch")
                req "duration" (TRecord "Duration")
                omit "voice" TInt (VInt 1)
                omit "tiedToNext" TBool (VBool false) ]
          kind
              "Chord"
              "event"
              [ req "pitches" (TList(TRecord "Pitch"))
                req "duration" (TRecord "Duration")
                omit "voice" TInt (VInt 1)
                omit "tiedToNext" TBool (VBool false) ]
          kind "GraceNote" "event" [ req "pitch" (TRecord "Pitch"); req "grace" (TEnum "GraceKind") ]
          kind "Dynamic" "mark" [ req "level" (TEnum "DynamicLevel") ]
          kind "Fermata" "mark" []
          kind "HairpinStart" "spanner" [ req "hairpin" (TEnum "HairpinKind") ]
          kind "HairpinEnd" "spanner" []
          kind "SlurStart" "spanner" []
          kind "SlurEnd" "spanner" []
          kind "OctaveShiftStart" "spanner" [ req "octaveShift" (TEnum "OctaveShiftKind") ]
          kind "OctaveShiftEnd" "spanner" []
          kind "MultiRest" "event" [ req "measureCount" TInt ]
          // Finding (3) nuance: the ornament set's tremolo variant carries a
          // payload, and the wire flattens it beside the enum-valued field
          // rather than tagging a union — declared here exactly as flattened.
          kind "Ornament" "mark" [ req "ornament" (TEnum "OrnamentName"); opt "slashCount" TInt ]
          kind "RehearsalMark" "mark" [ req "label" TStr ]
          kind "NavigationMark" "mark" [ req "navigation" (TEnum "NavigationKind") ]
          kind
              "Form"
              "structure"
              [ opt "name" TStr
                req "sections" (TList(TRecord "FormSection"))
                req "arrangement" (TList TStr) ] ]
      // Finding (3): NO value union at all — the only discriminated union on
      // this vocabulary's wire is the node discriminator itself.
      Unions = []
      Enums =
        [ Declare.enumOf "NoteLetter" [ "C"; "D"; "E"; "F"; "G"; "A"; "B" ]
          Declare.enumOf "Accidental" [ "DoubleFlat"; "Flat"; "Natural"; "Sharp"; "DoubleSharp" ]
          Declare.enumOf
              "BaseDuration"
              [ "Whole"
                "Half"
                "Quarter"
                "Eighth"
                "Sixteenth"
                "ThirtySecond"
                "SixtyFourth" ]
          Declare.enumOf
              "Mode"
              [ "Ionian"
                "Dorian"
                "Phrygian"
                "Lydian"
                "Mixolydian"
                "Aeolian"
                "Locrian"
                "MelodicMinor"
                "Dorianb2"
                "LydianAugmented"
                "LydianDominant"
                "Mixolydianb6"
                "LocrianNatural2"
                "SuperLocrian"
                "HarmonicMinor"
                "LocrianNatural6"
                "IonianAugmented"
                "DorianSharp4"
                "PhrygianDominant"
                "LydianSharp2"
                "UltraLocrian" ]
          Declare.enumOf "ClefKind" [ "Treble"; "Bass"; "Alto"; "Tenor"; "Percussion" ]
          Declare.enumOf "BracketKind" [ "Brace"; "Square"; "Line"; "None" ]
          Declare.enumOf "HairpinKind" [ "Crescendo"; "Decrescendo" ]
          Declare.enumOf "OctaveShiftKind" [ "Ottava"; "OttavaBassa"; "Quindicesima"; "QuindicesimaBassa" ]
          Declare.enumOf "GraceKind" [ "Acciaccatura"; "Appoggiatura" ]
          Declare.enumOf "OrnamentName" [ "Trill"; "Turn"; "InvertedTurn"; "Mordent"; "InvertedMordent"; "Tremolo" ]
          Declare.enumOf
              "NavigationKind"
              [ "Segno"
                "Coda"
                "DaCapo"
                "DaCapoAlFine"
                "DaCapoAlCoda"
                "DalSegno"
                "DalSegnoAlFine"
                "DalSegnoAlCoda"
                "Fine"
                "ToCoda" ]
          Declare.enumOf
              "DynamicLevel"
              [ "Pianississimo"
                "Pianissimo"
                "Piano"
                "MezzoPiano"
                "MezzoForte"
                "Forte"
                "Fortissimo"
                "Fortississimo"
                "Sforzando"
                "Forzato"
                "Rinforzando"
                "FortePiano"
                "SforzandoPiano" ] ]
      Records =
        [ record
              "Pitch"
              [ req "letter" (TEnum "NoteLetter")
                req "accidental" (TEnum "Accidental")
                req "octave" TInt
                // Finding (8): redundant derived value, declared as plain data.
                req "midi" TInt ]
          record
              "Duration"
              [ req "base" (TEnum "BaseDuration")
                // Finding (4): omit-at-default INSIDE a record position.
                omit "dots" TInt (VInt 0) ]
          record
              "KeySignature"
              [ req "tonic" (TEnum "NoteLetter")
                req "tonicAccidental" (TEnum "Accidental")
                req "mode" (TEnum "Mode") ]
          record "TimeSignature" [ req "numerator" TInt; req "denominator" TInt ]
          record
              "StaffDefinition"
              [ req "number" TInt
                req "clef" (TEnum "ClefKind")
                req "initialKey" (TRecord "KeySignature")
                req "initialTime" (TRecord "TimeSignature") ]
          // Finding (5): a record carrying a NODE LIST — record-over-tree recursion.
          record "FormSection" [ req "label" TStr; children ] ]
      Defaults = []
      NodeFields = []
      Ops = []
      // Findings (1)/(2), closed: the wire shape is DECLARED (Phases 108/109) —
      // a bare-string `kind` discriminator, and the flat node envelope.
      Wire =
        { Discriminator = "kind"
          NodeEnvelope = NodeEnvelopeShape.FlatKind
          KeyOrder = KeyOrder.Declared } }

let private nodeTags = scoreIdl.Kinds |> List.map (fun k -> k.Tag) |> Set.ofList

// ---------------------------------------------------------------------------
// The shape adapter that used to sit here is DELETED (Phases 108/109) — both
// hard-codings it quarantined are declared slots on the `Idl` now (`Wire`), so
// the corpus decodes and encodes in its native shape and there is nothing left
// to adapt. Same retirement as the second spike's.
// ---------------------------------------------------------------------------
// Corpus resolution — vendored by default, overridable; same contract as the
// second spike's resolver (the legs run, or they fail — never skip, never fall
// back silently on a bad override).
// ---------------------------------------------------------------------------

let private isCorpus (dir: string) =
    try
        let manifest = Path.Combine(dir, "manifest.json")

        File.Exists manifest
        && File.ReadAllText(manifest).Contains "\"modelRoundTrips\""
        && Directory.Exists(Path.Combine(dir, "model-roundtrips"))
    with _ ->
        false

let private vendoredCorpus () : string option =
    let rec climb (dir: string) (budget: int) : string option =
        if budget < 0 || isNull dir then
            None
        else
            let cand = Path.Combine(dir, "tests", "Fuaran.Core.Tests")

            if File.Exists(Path.Combine(cand, "UiIdl.fs")) then
                Some cand
            else
                match Directory.GetParent dir with
                | null -> None
                | parent -> climb parent.FullName (budget - 1)

    [ Directory.GetCurrentDirectory(); AppContext.BaseDirectory ]
    |> List.tryPick (fun start -> climb start 12)
    |> Option.map (fun projectDir -> Path.Combine(projectDir, "fixtures", "score-domain"))

let private resolveCorpus () : Result<string, string> =
    match Environment.GetEnvironmentVariable "FUARAN_SCORE_SPIKE_CORPUS" with
    | ovr when not (String.IsNullOrWhiteSpace ovr) ->
        if isCorpus ovr then
            Ok ovr
        else
            Error(
                sprintf
                    "FUARAN_SCORE_SPIKE_CORPUS is set to '%s', which is not a corpus: it must hold a manifest.json naming \"modelRoundTrips\" and a model-roundtrips/ directory. Unset it to use the vendored corpus."
                    ovr
            )
    | _ ->
        match vendoredCorpus () with
        | Some dir when isCorpus dir -> Ok dir
        | Some dir -> Error(sprintf "the vendored corpus at '%s' is missing or malformed" dir)
        | None -> Error "could not locate tests/Fuaran.Core.Tests (UiIdl.fs marker) from the CWD or the test binary"

let rec private tagsIn (j: JVal) : Set<string> =
    match j with
    | JArr xs -> xs |> List.map tagsIn |> Set.unionMany
    | JObj fields ->
        let here =
            fields
            |> List.choose (function
                | "kind", JStr t -> Some t
                | _ -> None)
            |> Set.ofList

        fields |> List.map (snd >> tagsIn) |> Set.unionMany |> Set.union here
    | _ -> Set.empty

/// Every corpus fixture, with the tags it reaches — the totality claim below is
/// that the declared slice covers ALL of them, not a filtered subset.
let private allFixtures (corpus: string) =
    Directory.GetFiles(Path.Combine(corpus, "model-roundtrips"), "*.json")
    |> Array.toList
    |> List.choose (fun path ->
        match Json.parse (File.ReadAllText path) with
        | Error _ -> None
        | Ok(JObj fields) ->
            fields
            |> List.tryPick (function
                | "root", root -> Some root
                | _ -> None)
            |> Option.map (fun root -> Path.GetFileNameWithoutExtension path, root)
        | Ok _ -> None)

/// The certification of one fixture against the IDL — identical in mechanism to
/// the second spike's: the foreign root, in its NATIVE shape and with no
/// adapter, is canonically rendered, decoded, re-encoded, and the bytes compared.
let private certify (idl: Idl) (root: JVal) : Result<unit, string> =
    let expected = Canon.renderOrdered root

    match Decode.decode idl expected with
    | Error e -> Error("decode: " + e)
    | Ok value ->
        match Encode.encode idl value with
        | Error e -> Error("encode: " + e)
        | Ok actual when actual <> expected ->
            Error(sprintf "bytes differ:\n  expected %s\n  actual   %s" expected actual)
        | Ok _ -> Ok()

/// The declaration with `Note`'s duration removed — the go-red control.
let private noteMissingDuration =
    { scoreIdl with
        Kinds =
            scoreIdl.Kinds
            |> List.map (fun k ->
                if k.Tag = "Note" then
                    { k with
                        Fields = k.Fields |> List.filter (fun fld -> fld.Name <> "duration") }
                else
                    k) }

let private pitchC4 =
    VRecord
        [ "letter", VEnum "C"
          "accidental", VEnum "Natural"
          "octave", VInt 4
          "midi", VInt 60 ]

[<Tests>]
let tests =
    testList
        "Score-vocabulary readiness spike"
        [

          test "the declared slice is well-formed" {
              Expect.isEmpty (Declare.enumWireErrors scoreIdl) "every enum's case/wire mapping is well-formed"
              Expect.isEmpty (Declare.wireShapeErrors scoreIdl) "the declared wire shape is well-formed"

              Expect.equal
                  (List.length (List.distinct (scoreIdl.Kinds |> List.map (fun k -> k.Tag))))
                  (List.length scoreIdl.Kinds)
                  "kind tags are distinct"

              Expect.equal
                  (List.length (List.distinct (scoreIdl.Records |> List.map (fun r -> r.Name))))
                  (List.length scoreIdl.Records)
                  "record names are distinct"

              let enumNames = scoreIdl.Enums |> List.map (fun e -> e.Name) |> Set.ofList
              let recordNames = scoreIdl.Records |> List.map (fun r -> r.Name) |> Set.ofList

              let rec referenced t =
                  match t with
                  | TEnum n -> [ Choice1Of2 n ]
                  | TRecord n -> [ Choice2Of2 n ]
                  | TList inner
                  | TMap inner -> referenced inner
                  | _ -> []

              let allTypes =
                  [ for k in scoreIdl.Kinds do
                        for fld in k.Fields -> fld.Type
                    for r in scoreIdl.Records do
                        for fld in r.Fields -> fld.Type ]

              for r in List.collect referenced allTypes do
                  match r with
                  | Choice1Of2 n -> Expect.isTrue (enumNames.Contains n) (sprintf "enum '%s' is declared" n)
                  | Choice2Of2 n -> Expect.isTrue (recordNames.Contains n) (sprintf "record '%s' is declared" n)
          }

          // ---- findings (1) + (2), CLOSED: the declared shape decodes the
          // ---- native wire directly, with the go-red partner pinning the slot ----

          test "the foreign envelope decodes DIRECTLY under the declared shape — findings (1)/(2) are closed" {
              let authored =
                  VNode("n1", "Note", [ "pitch", pitchC4; "duration", VRecord [ "base", VEnum "Quarter" ] ])

              let bytes =
                  match Encode.encode scoreIdl authored with
                  | Ok s -> s
                  | Error e -> failtestf "the slice must encode its own authored node (%s)" e

              Expect.isTrue (bytes.Contains "\"kind\":\"Note\"") "the declared discriminator tags the node"
              Expect.isFalse (bytes.Contains "$type") "nothing on this wire is $type-tagged"

              match Decode.decode scoreIdl bytes with
              | Ok roundTripped ->
                  match Encode.encode scoreIdl roundTripped with
                  | Ok again -> Expect.equal again bytes "the native shape round-trips byte-identically"
                  | Error e -> failtestf "re-encode: %s" e
              | Error e -> failtestf "the declared shape must decode its own wire (%s)" e

              // The go-red partner: the same wire under a DEFAULT-shape
              // declaration is refused — the declaration is doing the work.
              match
                  Decode.decode
                      { scoreIdl with
                          Wire = WireShape.Default }
                      bytes
              with
              | Ok _ -> failtest "the default-shape declaration decoded the flat kind-tagged wire"
              | Error e ->
                  Expect.stringContains
                      e
                      "node"
                      (sprintf "the failure names the node envelope rather than a field-level defect (got: %s)" e)
          }

          // ---- finding (3): the headline negative, pinned so it cannot rot ----

          test "no value union exists — transparency has no position in which to arise" {
              Expect.isEmpty scoreIdl.Unions "the score vocabulary declares no value union at all — finding (3)"

              // And structurally: no declared field anywhere in the slice is
              // union-typed, so the question a transparent case answers cannot
              // even be asked of this wire.
              let rec unionsIn t =
                  match t with
                  | TUnion(n, args) -> n :: List.collect unionsIn args
                  | TList inner
                  | TMap inner -> unionsIn inner
                  | _ -> []

              let allTypes =
                  [ for k in scoreIdl.Kinds do
                        for fld in k.Fields -> fld.Type
                    for r in scoreIdl.Records do
                        for fld in r.Fields -> fld.Type ]

              Expect.isEmpty (List.collect unionsIn allTypes) "no union-typed field position exists in the slice"
          }

          test "the ornament flattening certifies as enum plus optional payload — and cannot state the coupling" {
              // The payload-less variant, payload omitted.
              let trill = VNode("o1", "Ornament", [ "ornament", VEnum "Trill" ])

              let trillBytes =
                  match Encode.encode scoreIdl trill with
                  | Ok s -> s
                  | Error e -> failtestf "trill: %s" e

              Expect.isFalse
                  (trillBytes.Contains "slashCount")
                  "no payload field on the wire for a payload-less variant"

              // The payload-carrying variant, flattened beside the enum field.
              let tremolo =
                  VNode("o2", "Ornament", [ "ornament", VEnum "Tremolo"; "slashCount", VInt 3 ])

              match Encode.encode scoreIdl tremolo with
              | Error e -> failtestf "tremolo: %s" e
              | Ok bytes ->
                  Expect.isTrue (bytes.Contains "slashCount") "the flattened payload rides beside the enum field"

                  match Decode.decode scoreIdl bytes with
                  | Error e -> failtestf "tremolo decode: %s" e
                  | Ok v ->
                      match Encode.encode scoreIdl v with
                      | Ok again -> Expect.equal again bytes "the flattening round-trips byte-identically"
                      | Error e -> failtestf "tremolo re-encode: %s" e

              // The cost, stated as a fact rather than an opinion: the illegal
              // combination (a payload on a payload-less variant) encodes without
              // complaint — the case↔payload coupling is a validator concern
              // above the IDL, which is finding (3)'s nuance priced exactly.
              let illegal =
                  VNode("o3", "Ornament", [ "ornament", VEnum "Trill"; "slashCount", VInt 3 ])

              Expect.isTrue
                  (Result.isOk (Encode.encode scoreIdl illegal))
                  "the flattening admits the illegal combination — the coupling invariant is not statable here"
          }

          // ---- finding (4): omit-at-default, in both directions and in a record position ----

          test "omit-at-default is expressible — defaults vanish on emit and reconstitute on decode" {
              let atDefaults =
                  VNode(
                      "n1",
                      "Note",
                      [ "pitch", pitchC4
                        "duration", VRecord [ "base", VEnum "Quarter"; "dots", VInt 0 ]
                        "voice", VInt 1
                        "tiedToNext", VBool false ]
                  )

              let bytes =
                  match Encode.encode scoreIdl atDefaults with
                  | Ok s -> s
                  | Error e -> failtestf "encode: %s" e

              Expect.isFalse (bytes.Contains "\"voice\"") "a voice of 1 is omitted on the wire"
              Expect.isFalse (bytes.Contains "tiedToNext") "a false flag is omitted on the wire"
              Expect.isFalse (bytes.Contains "\"dots\"") "a dot count of 0 is omitted INSIDE the record position"

              match Decode.decode scoreIdl bytes with
              | Error e -> failtestf "decode: %s" e
              | Ok(VNode(_, _, fields)) ->
                  Expect.equal
                      (fields |> List.tryFind (fun (n, _) -> n = "voice") |> Option.map snd)
                      (Some(VInt 1))
                      "the absent voice reconstitutes to its default"

                  match fields |> List.tryFind (fun (n, _) -> n = "duration") |> Option.map snd with
                  | Some(VRecord df) ->
                      Expect.equal
                          (df |> List.tryFind (fun (n, _) -> n = "dots") |> Option.map snd)
                          (Some(VInt 0))
                          "the absent dot count reconstitutes inside the record"
                  | other -> failtestf "duration decoded as %A" other
              | Ok other -> failtestf "decoded as %A" other

              // And a NON-default value survives in both directions.
              let offDefaults =
                  VNode(
                      "n2",
                      "Note",
                      [ "pitch", pitchC4
                        "duration", VRecord [ "base", VEnum "Quarter"; "dots", VInt 1 ]
                        "voice", VInt 2 ]
                  )

              match Encode.encode scoreIdl offDefaults with
              | Error e -> failtestf "encode: %s" e
              | Ok s ->
                  Expect.isTrue (s.Contains "\"voice\"") "a non-default voice is on the wire"
                  Expect.isTrue (s.Contains "\"dots\"") "a non-zero dot count is on the wire"
          }

          // ---- finding (6): the enum case/wire split stays undemanded ----

          test "no enum needs a case/wire split in this vocabulary" {
              for e in scoreIdl.Enums do
                  Expect.isEmpty
                      e.Wires
                      (sprintf "enum '%s' spells its wire strings as its case names — finding (6)" e.Name)

                  for c in e.Cases do
                      Expect.isTrue
                          (c.Length > 0 && Char.IsUpper c[0] && c |> Seq.forall Char.IsLetterOrDigit)
                          (sprintf "enum '%s' case '%s' is a legal F# identifier" e.Name c)
          }

          // ---- the certification against the vendored corpus ----

          test "the declared slice round-trips the whole corpus byte-identically" {
              match resolveCorpus () with
              | Error e -> failtestf "corpus: %s" e
              | Ok corpus ->
                  let fixtures = allFixtures corpus

                  Expect.isGreaterThanOrEqual
                      (List.length fixtures)
                      8
                      "the corpus is large enough for the certification to mean something"

                  // Totality: every fixture is in slice — the declaration covers
                  // every tag the corpus reaches, so nothing is quietly filtered.
                  for (name, root) in fixtures do
                      Expect.isTrue
                          (Set.isSubset (tagsIn root) nodeTags)
                          (sprintf "%s: every tag it reaches is declared" name)

                  for (name, root) in fixtures do
                      match certify scoreIdl root with
                      | Ok() -> ()
                      | Error e -> failtestf "%s: %s" name e
          }

          test "the certification can go red — a field dropped from the declaration is caught" {
              match resolveCorpus () with
              | Error e -> failtestf "corpus: %s" e
              | Ok corpus ->
                  let failures =
                      allFixtures corpus
                      |> List.filter (fun (_, root) -> Result.isError (certify noteMissingDuration root))

                  Expect.isNonEmpty
                      failures
                      "removing a declared field must break the certification — otherwise it is not certifying"
          } ]
