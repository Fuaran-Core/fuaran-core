namespace Fuaran.Core.Tests


open Fuaran.Core

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

    // ---- the seam-refusal family (Phase 551) ----
    // The seams' refusal and declaration documents (`Fuaran.Core.SeamRefusalLaws`, held by
    // `Conformance.seamRefusalLaws`): each vector names its reader, its document and what the reader
    // answers, the canonical bytes back or a refusal's code and path (D188: a host certifies on code and
    // path; the sentence is a projection of them). Core-internal: no corpus copy carries it.

    let seamFileName = "seam-refusals.json"
    let seamFamilyId = "seamRefusals"

    let private seamLine (v: SeamRefusalLaws.Vector) : CorpusCell =
        let result =
            match SeamRefusalLaws.outcome v with
            | Ok canonical -> [ "canonical", JStr canonical ]
            | Error e -> [ "code", JStr(DecodeError.codeName e.Code); "path", DecodePath.toJson e.Path ]

        CorpusCell.Value(
            JObj(
                [ "name", JStr v.Name; "reader", JStr v.Reader; "document", JStr v.Document ]
                @ result
            )
        )

    /// The seam-refusal vectors file, rendered.
    let renderSeam () : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str seamFamilyId
                  "description",
                  CorpusFile.str
                      "Seam refusal vectors (Phase 551). The capability and query seams' refusal and declaration unions (InvokeError with DeclarationFault, Placement, the Deferred envelope, the capability pipeline, QueryError with ColumnPredicate, SortKey, the schema delta's FieldChange, and the ops Rejection envelope), each read by the codec derived from its IDL declaration. Each vector names its `reader` (a `/strict` reader refuses an undeclared member, at it), its `document`, and the outcome computed by the reference: `canonical`, the re-encoding of what it reads, or the refusal's `code` and `path`. A host certifies its reader by reproducing every outcome, on code and path." ]
              Items = "vectors"
              Rows = SeamRefusalLaws.vectors |> List.map seamLine }

    let seamPath (dir: string) =
        Path.Combine(dir, familyDirName, seamFileName)

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
                Adoption = hostRoster |> List.map (fun h -> h, HostAdoption.Proposed)
                Description =
                  "The JSON number grammar, well-formed UTF-16 strings, the decode-determinism rows every wire's JSON layer shares (a repeated member, content after the root, a bare non-finite literal, a raw C0 control in a string; Phase 462), the columnar codec's cell-type, canonical-text, duplicate-name and ragged-table refusals, and the wire-profile grammar, beside the acceptances they are the boundary of." }
              { Id = seamFamilyId
                Kind = "seam-refusals"
                File = seamFileName
                Vectors = List.length SeamRefusalLaws.vectors
                Adoption = hostRoster |> List.map (fun h -> h, HostAdoption.Proposed)
                Description =
                  "The seams' refusal and declaration documents (Phase 551): every case of every seam union read back to its own bytes, and its readers' refusals by code and path, the strict readers' undeclared members among them." } ] }

    let renderManifest () : string =
        CorpusFile.render (CorpusFile.manifest (manifest ()))

    let vectorsPath (dir: string) =
        Path.Combine(dir, familyDirName, vectorsFileName)

    let manifestPath (dir: string) =
        Path.Combine(dir, familyDirName, manifestFileName)
