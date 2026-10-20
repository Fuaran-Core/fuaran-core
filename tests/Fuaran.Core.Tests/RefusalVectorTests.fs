module Fuaran.Core.Tests.RefusalVectorTests

open Expecto
open Fuaran.Core

// ============================================================================
//  Phase 299 — the codec REFUSAL corpus, `conformance/refusals/`.
//
//  The parser now holds to the JSON number grammar and refuses a lone or
//  ill-ordered surrogate; the columnar codec refuses a cell outside its column's
//  type, a repeated name, non-canonical date and timestamp text and a ragged
//  table, and reads a whole-valued number token into a decimal column. Each is a
//  statement the host codec twins must make too, so each is a VECTOR here: an
//  authored input, and the outcome computed by CALLING the codec — a refusal's
//  class, or an acceptance's canonical bytes — never by restating what it ought
//  to answer. The authored table also names the outcome each vector is FOR, and
//  a test holds the computed outcome to it, so a codec change that moves a
//  vector is a red test naming it before it is a re-emitted file.
//
//  The family is SELF-ENUMERATED (its own `manifest.json`, with per-host
//  adoption), the shape `apply/` uses and for its reason: the corpus root
//  manifest indexes the UI wire-format codec families only. It is emitted, never
//  hand-edited:  dotnet run --project tests/Fuaran.Core.Tests -- --emit-refusals [<dir>]
//  and the suite holds the committed files to a fresh render.
// ============================================================================

module RefusalCorpus =

    open System.IO
    open System.Text
    open Microsoft.FSharp.Reflection

    let familyDirName = "refusals"
    let vectorsFileName = "codec-refusals.json"
    let manifestFileName = "manifest.json"
    let familyId = "codecRefusals"

    /// What an authored vector is FOR: a refusal of the named class, or an acceptance.
    type Intent =
        | Refuse of cls: string
        | Accept

    type Vector =
        {
            Name: string
            /// `json` — `Json.read ReadOptions.strict`, the refusal class a `JsonErrorKind`; `column` —
            /// `ColumnCodec.decode`, the class a `ColumnError` case (`NotJson/<kind>` for a parse failure);
            /// `profile` (Phase 306) — `Versioning.Capability.tryParse`, whose one refusal class is
            /// `MalformedProfile` and whose acceptance is the profile's canonical string.
            Codec: string
            Input: string
            Intent: Intent
        }

    let private json name input intent =
        { Name = name
          Codec = "json"
          Input = input
          Intent = intent }

    let private col name input intent =
        { Name = name
          Codec = "column"
          Input = input
          Intent = intent }

    let private profile name input intent =
        { Name = name
          Codec = "profile"
          Input = input
          Intent = intent }

    /// A one-column embedded source of type `ty` whose values array is `values` (all present).
    let private one (ty: string) (values: string) =
        "{\"schema\":[{\"name\":\"c\",\"type\":\""
        + ty
        + "\"}],\"columns\":{\"c\":{\"values\":["
        + values
        + """],"validity":[true]}}}"""

    /// The authored table. Order is part of the file.
    let vectors: Vector list =
        [
          // ---- the JSON number grammar: -? (0 | [1-9][0-9]*) (\.[0-9]+)? ([eE][+-]?[0-9]+)? ----
          json "leading-zero" "01" (Refuse "MalformedNumber")
          json "leading-zero-negative" "-01" (Refuse "MalformedNumber")
          json "leading-zeros-in-array" "[00]" (Refuse "MalformedNumber")
          json "leading-zeros-int53" "0009007199254740992" (Refuse "MalformedNumber")
          json "point-without-fraction" "1." (Refuse "MalformedNumber")
          json "point-before-exponent" "1.e5" (Refuse "MalformedNumber")
          json "point-without-integer" "-.5" (Refuse "MalformedNumber")
          json "bare-minus" "-" (Refuse "MalformedNumber")
          json "exponent-without-digit" "1e" (Refuse "MalformedNumber")
          json "exponent-sign-without-digit" "1e+" (Refuse "MalformedNumber")
          json "zero" "0" Accept
          json "negative-zero" "-0" Accept
          json "zero-fraction" "0.5" Accept
          json "negative-fraction-exponent" "-0.5e-3" Accept
          json "upper-exponent" "1E+2" Accept
          json "negative-int32-min" "-2147483648" Accept
          json "past-int32" "3000000000" Accept
          // ---- well-formed UTF-16: a surrogate is half of a pair or the string is refused ----
          json "lone-high-escape" "\"\\uD800\"" (Refuse "BadEscape")
          json "lone-low-escape" "\"\\uDFFF\"" (Refuse "BadEscape")
          json "low-then-high" "\"\\uDE00\\uD83D\"" (Refuse "BadEscape")
          json "high-then-high" "\"\\uD800\\uD800\"" (Refuse "BadEscape")
          json "high-then-ascii" "\"\\uD83Dz\"" (Refuse "BadEscape")
          json "high-then-short-escape" "\"\\uD83D\\n\"" (Refuse "BadEscape")
          json "high-at-end" "\"a\\uD83D\"" (Refuse "BadEscape")
          json "lone-surrogate-in-a-key" "{\"\\uD800\":1}" (Refuse "BadEscape")
          json "escaped-pair" "\"\\uD83D\\uDE00\"" Accept
          // ---- Phase 462: the decode-determinism rows every wire's JSON layer shares ----
          // a repeated member name in one object, names compared after unescaping (row 1)
          json "repeated-member" """{"id":"a","id":"b"}""" (Refuse "DuplicateMember")
          json "repeated-member-same-value" """{"a":1,"a":1}""" (Refuse "DuplicateMember")
          json "repeated-member-not-adjacent" """{"a":1,"b":2,"a":3}""" (Refuse "DuplicateMember")
          json "repeated-member-escaped-spelling" """{"a":1,"a":2}""" (Refuse "DuplicateMember")
          json "repeated-member-nested" """[{"a":1},{"b":1,"b":2}]""" (Refuse "DuplicateMember")
          json "repeated-empty-name" """{"":1,"":2}""" (Refuse "DuplicateMember")
          json "one-name-in-sibling-objects" """[{"a":1},{"a":2}]""" Accept
          json "one-name-at-two-depths" """{"a":{"a":1}}""" Accept
          json "names-differing-in-case" """{"a":1,"A":2}""" Accept
          // content after the root value (row 2)
          json "content-after-object" "{}x" (Refuse "TrailingCharacters")
          json "second-root-value" "1 2" (Refuse "TrailingCharacters")
          json "surplus-closing-brace" "{} }" (Refuse "TrailingCharacters")
          json "whitespace-after-root" "{} \n" Accept
          // the number grammar's other arms (row 3; the arms above are Phase 299's)
          json "leading-plus" "+1" (Refuse "UnexpectedChar")
          json "no-integer-part" ".5" (Refuse "UnexpectedChar")
          // a bare non-finite literal is no JSON token; the quoted sentinel is a string (rows 4, 8)
          json "bare-nan" "NaN" (Refuse "UnexpectedChar")
          json "bare-infinity" "Infinity" (Refuse "UnexpectedChar")
          json "bare-negative-infinity" "-Infinity" (Refuse "MalformedNumber")
          json "bare-nan-in-array" "[1,NaN]" (Refuse "UnexpectedChar")
          json "sentinel-string" "\"NaN\"" Accept
          // an overflowing exponent: refused because Core's value model holds no non-finite float,
          // a value-domain refusal rather than a syntax one (DECISIONS.md D157)
          json "overflowing-exponent" "1e999" (Refuse "MalformedNumber")
          // a raw C0 control character inside a string (row 5)
          json "raw-control-u0001" ("\"a" + string (char 1) + "b\"") (Refuse "BadEscape")
          json "raw-nul" ("\"" + string (char 0) + "\"") (Refuse "BadEscape")
          json "raw-tab" ("\"a" + string (char 9) + "b\"") (Refuse "BadEscape")
          json "raw-line-feed" ("\"a" + string (char 10) + "b\"") (Refuse "BadEscape")
          json "raw-unit-separator" ("\"" + string (char 0x1F) + "\"") (Refuse "BadEscape")
          json "raw-control-in-a-name" ("{\"a" + string (char 1) + "\":1}") (Refuse "BadEscape")
          json "raw-control-after-an-escape" ("\"\\n" + string (char 1) + "\"") (Refuse "BadEscape")
          json "escaped-controls" "\"\\u0000\\u001f\\t\"" Accept
          json "raw-delete" ("\"" + string (char 0x7F) + "\"") Accept
          // ---- the columnar codec: a cell of its column's type, or a refusal naming it ----
          col "bool-in-int" (one "int" "true") (Refuse "TypeMismatch")
          col "string-in-float" (one "float" "\"1.5\"") (Refuse "TypeMismatch")
          col "fraction-in-decimal" (one "decimal" "3.5") (Refuse "TypeMismatch")
          col "whole-float-past-int53-in-decimal" (one "decimal" "1e300") (Refuse "TypeMismatch")
          col "decimal-text-not-decimal" (one "decimal" "\"1e3\"") (Refuse "MalformedShape")
          col "int-token-past-int32-in-decimal" (one "decimal" "3000000000") Accept
          col "whole-exponent-token-in-decimal" (one "decimal" "3e9") Accept
          col "negative-past-int32-in-decimal" (one "decimal" "-3000000000") Accept
          col "decimal-text-normalised" (one "decimal" "\"12.50\"") Accept
          col "date-impossible-day" (one "date" "\"2026-02-30\"") (Refuse "MalformedShape")
          col "date-not-a-leap-year" (one "date" "\"2023-02-29\"") (Refuse "MalformedShape")
          col "date-unpadded" (one "date" "\"2026-6-1\"") (Refuse "MalformedShape")
          col "date-empty" (one "date" "\"\"") (Refuse "MalformedShape")
          col "date-leap-day" (one "date" "\"2024-02-29\"") Accept
          col "timestamp-with-offset" (one "timestamp" "\"2026-06-22T17:00:00+01:00\"") (Refuse "MalformedShape")
          col "timestamp-hour-24" (one "timestamp" "\"2026-06-22T24:00:00Z\"") (Refuse "MalformedShape")
          col "timestamp-fractional-second" (one "timestamp" "\"2026-06-22T17:00:00.5Z\"") (Refuse "MalformedShape")
          col "timestamp-epoch-past-year-9999" (one "timestamp" "900000000000000") (Refuse "MalformedShape")
          col "timestamp-epoch-seconds" (one "timestamp" "1752000000") Accept
          col
              "duplicate-schema-name"
              """{"schema":[{"name":"a","type":"int"},{"name":"a","type":"int"}],"columns":{"a":[1]}}"""
              (Refuse "Malformed")
          col
              "duplicate-column-key"
              """{"schema":[{"name":"a","type":"int"}],"columns":{"a":[1],"a":[2]}}"""
              (Refuse "NotJson/DuplicateMember")
          col "ragged-columns" """{"columns":{"a":[1,2],"b":[3]}}""" (Refuse "RaggedColumns")
          col "leading-zero-in-a-column" (one "int" "01") (Refuse "NotJson/MalformedNumber")
          col "lone-surrogate-in-a-column" (one "string" "\"\\uD800\"") (Refuse "NotJson/BadEscape")
          col "ref-without-schema" """{"ref":"orders"}""" Accept
          // ---- Phase 306: surplus members are must-ignore, and the table is the schema's ----
          col
              "surplus-members-read-past"
              """{"$type":"x","schema":[{"name":"a","type":"int"}],"columns":{"a":[1],"zz":[true]},"extra":1}"""
              Accept
          // ---- Phase 306: the profile grammar — the canonical string and nothing else ----
          //   name "@" number "." number;  name = letter (letter | digit | "." | "_" | "-")*, ASCII;
          //   number = "0" | nonzero digit*, at most 2147483647
          profile "profile-canonical" "core@1.0" Accept
          profile "profile-zero-zero" "core@0.0" Accept
          profile "profile-int32-max" "core@2147483647.2147483647" Accept
          profile "profile-name-punctuation" "Fuaran-UI.v2_x@10.20" Accept
          profile "profile-leading-zero-major" "core@01.0" (Refuse "MalformedProfile")
          profile "profile-leading-zero-minor" "core@1.00" (Refuse "MalformedProfile")
          profile "profile-plus-sign" "core@+1.0" (Refuse "MalformedProfile")
          profile "profile-negative-zero" "core@-0.0" (Refuse "MalformedProfile")
          profile "profile-trailing-nul" ("core@1.0" + string (char 0)) (Refuse "MalformedProfile")
          profile "profile-trailing-space" "core@1.0 " (Refuse "MalformedProfile")
          profile "profile-past-int32" "core@2147483648.0" (Refuse "MalformedProfile")
          profile "profile-three-components" "core@1.0.0" (Refuse "MalformedProfile")
          profile "profile-missing-minor" "core@1." (Refuse "MalformedProfile")
          profile "profile-missing-name" "@1.0" (Refuse "MalformedProfile")
          profile "profile-name-starts-with-digit" "1core@1.0" (Refuse "MalformedProfile")
          profile "profile-name-with-space" "co re@1.0" (Refuse "MalformedProfile")
          profile "profile-name-with-at" "a@b@1.0" (Refuse "MalformedProfile")
          profile "profile-name-not-ascii" "café@1.0" (Refuse "MalformedProfile")
          profile "profile-non-ascii-digit" "core@1.٣" (Refuse "MalformedProfile") ]

    let private caseName (v: obj) : string =
        let case, _ = FSharpValue.GetUnionFields(v, v.GetType())
        case.Name

    /// The outcome the codec gives `v`: `Error <class>` for a refusal, `Ok <canonical bytes>` for an
    /// acceptance (`Canonical.write Rendering.CrossHost` of the parse; `ColumnCodec.encode` of the decode; `Profile.render`
    /// of the parsed profile).
    let outcome (v: Vector) : Result<string, string> =
        match v.Codec with
        | "json" ->
            match Json.read ReadOptions.strict v.Input with
            | Ok jv -> Ok(Canonical.write Rendering.CrossHost jv)
            | Error(JsonRefusal.Malformed e) -> Error(caseName (box e.Kind))
            | Error(JsonRefusal.LimitExceeded b) -> Error("LimitExceeded/" + JsonLimit.name b.Limit)
        | "profile" ->
            match Versioning.Capability.tryParse v.Input with
            | Ok p -> Ok(Versioning.Capability.render p)
            | Error _ -> Error "MalformedProfile"
        | _ ->
            match ColumnCodec.decode v.Input with
            | Ok src -> Ok(ColumnCodec.encode src)
            | Error(NotJson e) -> Error("NotJson/" + caseName (box e.Kind))
            | Error e -> Error(caseName (box e))

    let private line (v: Vector) : CorpusCell =
        let result =
            match outcome v with
            | Error cls -> [ "refusal", JStr cls ]
            | Ok canonical -> [ "canonical", JStr canonical ]

        CorpusCell.Value(JObj([ "name", JStr v.Name; "codec", JStr v.Codec; "input", JStr v.Input ] @ result))

    /// The vectors file, rendered: a header, then one canonical line per vector.
    let render () : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str familyId
                  "description",
                  CorpusFile.str
                      "Codec refusal vectors (Phase 299). Each vector is an input and the outcome computed by calling the reference codec: `json` inputs go through the JSON reader, `column` inputs through the columnar DataSource decoder, `profile` inputs (Phase 306) through the wire-profile reader. Every `json` vector binds every JSON decode entry point, not one parser (Phase 462). A `refusal` names the class the decode must refuse with (a JsonErrorKind; a ColumnError case, NotJson/<kind> for a parse failure; MalformedProfile); a `canonical` is the canonical re-encoding of what the input decodes to. A host certifies its codec twin by reproducing every outcome." ]
              Items = "vectors"
              Rows = vectors |> List.map line }

    /// The hosts adoption is recorded for — the codec twins that certify against the shared corpus.
    let hostRoster = [ "fuaran"; "fuaran-ts"; "fuaran-py"; "fuaran-go"; "fuaran-rs" ]

    /// The family index as the one manifest schema (`CorpusManifest`, Phase 508).
    let internal manifest () : CorpusManifest =
        { Version = 1
          Description =
            "Codec refusal corpus (Phase 299). SELF-ENUMERATED: not indexed by the corpus root manifest.json, which indexes the canonical wire-format codec families only (the apply/ precedent). `adoption` is per host and is DATA: a host whose entry reads `proposed` reports the family BY NAME rather than skipping it, and flips its own entry to `adopted` in the change-set that lands its leg. The shared corpus takes its copy at the hosts' next pin raise. This manifest is authoritative for the vector count."
          Families =
            [ { Id = familyId
                Kind = "codec-refusals"
                File = vectorsFileName
                Vectors = List.length vectors
                Adoption = hostRoster |> List.map (fun h -> h, "proposed")
                Description =
                  "The JSON number grammar, well-formed UTF-16 strings, the decode-determinism rows every wire's JSON layer shares (a repeated member, content after the root, a bare non-finite literal, a raw C0 control in a string; Phase 462), the columnar codec's cell-type, canonical-text, duplicate-name and ragged-table refusals, and the wire-profile grammar, beside the acceptances they are the boundary of." } ] }

    let renderManifest () : string =
        CorpusFile.render (CorpusFile.manifest (manifest ()))

    let vectorsPath (dir: string) =
        Path.Combine(dir, familyDirName, vectorsFileName)

    let manifestPath (dir: string) =
        Path.Combine(dir, familyDirName, manifestFileName)


[<Tests>]
let tests =
    testList
        "Refusal vectors (Phase 299)"
        [ testCase "every authored refusal vector gets the outcome it is for"
          <| fun _ ->
              for v in RefusalCorpus.vectors do
                  match v.Intent, RefusalCorpus.outcome v with
                  | RefusalCorpus.Refuse cls, Error got -> Expect.equal got cls (sprintf "%s refuses as %s" v.Name cls)
                  | RefusalCorpus.Accept, Ok _ -> ()
                  | intent, got -> failtestf "%s: authored for %A, the codec answered %A" v.Name intent got

              let names = RefusalCorpus.vectors |> List.map _.Name
              Expect.equal (List.distinct names) names "vector names are unique"

          // Phase 462 — a row binds every ENTRY POINT that turns JSON text into a value, not one
          // parser. Since Phase 519 there are four readers — `Json.read`, `Json.parse`,
          // `Decoder.parse` and `Reader.read` — and each `json` vector is put to every one, under
          // both named read options, and each must give the answer `Json.read ReadOptions.strict`
          // gives: the same refusal in each reader's own error type, or an acceptance of the same
          // value.
          testCase "every json vector gets one answer at every decode entry point"
          <| fun _ ->
              let rec walk (r: Reader) (input: string) : unit =
                  // A structural reading through the Reader's own object and array loops.
                  match input.TrimStart() with
                  | t when t.StartsWith "{" -> Reader.members (fun _ r -> Reader.value r |> ignore) r
                  | t when t.StartsWith "[" -> Reader.items (fun r -> Reader.value r |> ignore) r
                  | _ -> Reader.value r |> ignore

              for v in RefusalCorpus.vectors |> List.filter (fun v -> v.Codec = "json") do
                  let reference = Json.read ReadOptions.strict v.Input
                  let typed = reference |> Result.mapError DecodeError.ofJsonRefusal

                  // no vector carries a null, so the tolerant policy has nothing to erase
                  Expect.equal
                      (Json.read ReadOptions.tolerantOfNull v.Input)
                      reference
                      (sprintf "%s: Json.read tolerantOfNull" v.Name)

                  match reference, Json.parse v.Input with
                  | Ok a, Ok b -> Expect.equal b a (sprintf "%s: Json.parse" v.Name)
                  | Error(JsonRefusal.Malformed e), Error m ->
                      Expect.equal
                          m
                          ("not valid JSON: " + e.Message + " at position " + string e.Position)
                          (sprintf "%s: Json.parse" v.Name)
                  | a, b -> failtestf "%s: Json.read answered %A, Json.parse %A" v.Name a b

                  for name, options in [ "strict", ReadOptions.strict; "tolerantOfNull", ReadOptions.tolerantOfNull ] do
                      Expect.equal (Decoder.parse options v.Input) typed (sprintf "%s: Decoder.parse %s" v.Name name)

                      let expected = typed |> Result.map ignore

                      Expect.equal
                          (Reader.read options (Reader.value >> ignore) v.Input)
                          expected
                          (sprintf "%s: Reader.value %s" v.Name name)

                      Expect.equal
                          (Reader.read options (fun r -> walk r v.Input) v.Input)
                          expected
                          (sprintf "%s: Reader structural %s" v.Name name)

          // Phase 462 — an erased member null is NOT an occurrence of its name (D157): the tolerant
          // read is the strict read of the document with its member nulls deleted (Phase 190), and
          // a repeated name the deletion removes was never there.
          testCase "an erased member null does not count as an occurrence of its name"
          <| fun _ ->
              let tolerant = Json.read ReadOptions.tolerantOfNull
              let strict = Json.read ReadOptions.strict

              Expect.equal (tolerant """{"a":null,"a":1}""") (strict """{"a":1}""") "erased first"

              Expect.equal (tolerant """{"a":1,"a":null}""") (strict """{"a":1}""") "erased second"

              match tolerant """{"a":1,"a":2}""" with
              | Error(JsonRefusal.Malformed e) ->
                  Expect.equal e.Kind DuplicateMember "two surviving members still repeat"
              | other -> failtestf "expected DuplicateMember, got %A" other

          testCase "a repeated member is refused at its name's opening quote"
          <| fun _ ->
              match Json.read ReadOptions.strict """{"a":1,"b":2,"a":3}""" with
              | Error(JsonRefusal.Malformed e) ->
                  Expect.equal e.Kind DuplicateMember "kind"
                  Expect.equal e.Position 13 "at the second \"a\""
              | other -> failtestf "expected DuplicateMember, got %A" other

          // The amendment's culture leg: the corpus answers the same under a culture whose negative
          // sign is not U+002D, because the parser's integer reader is the invariant one.
          testCase "every vector gets the same outcome under fa-IR and he-IL"
          <| fun _ ->
              let invariant = RefusalCorpus.vectors |> List.map RefusalCorpus.outcome

              for culture in [ "fa-IR"; "he-IL" ] do
                  let saved = System.Globalization.CultureInfo.CurrentCulture
                  System.Globalization.CultureInfo.CurrentCulture <- System.Globalization.CultureInfo culture

                  try
                      Expect.equal
                          (RefusalCorpus.vectors |> List.map RefusalCorpus.outcome)
                          invariant
                          (sprintf "the corpus under %s" culture)
                  finally
                      System.Globalization.CultureInfo.CurrentCulture <- saved ]
