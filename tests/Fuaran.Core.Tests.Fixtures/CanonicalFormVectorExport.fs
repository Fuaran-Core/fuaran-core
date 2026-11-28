namespace Fuaran.Core.Tests

// ============================================================================
//  The canonical-form law set (Phase 539, DECISIONS.md D190):
//  `laws/canonical-form-laws.json`, the vectors a host replays to certify that
//  its IDL-driven codec applies the three canonical-form declarations — a
//  member's aliases, its presence condition, and an empty-map or JSON default —
//  without running F#.
//
//  Each vector is an input document over the fixture vocabulary the file
//  carries (its `idl.json`, under `vocabulary`) and the canonical bytes the
//  reference reads it to: `Decode` then `Encode`, computed by CALLING the kit,
//  never restated. The vocabulary is authored, not drawn; so are the inputs.
// ============================================================================

module CanonicalFormVectorExport =

    open Fuaran.Core
    open Fuaran.Core.Idl

    let fileName = "canonical-form-laws.json"

    let private field (name: string) (t: IdlType) (opt: Optionality) : IdlField =
        { Name = name
          Type = t
          Opt = opt
          Aliases = []
          Keyed = false
          Presence = None
          Annotations = Annotations.Empty }

    let private kind (tag: string) (fields: IdlField list) : IdlKind =
        { Tag = tag
          Category = "Display"
          Fields = fields
          Annotations = Annotations.Empty }

    /// The fixture domain: one kind per shape, and an enum with case aliases.
    ///
    /// - `Grid.cols` is meaningful only while `template` is absent (a presence test);
    /// - `Select.value` / `Select.values` are meaningful only under `multiple = false` / `true` (an
    ///   equality test over an `OmitDefault` sibling, so its absence holds `false`), and `options`
    ///   is read under the retired spellings `source` then `suggestions`;
    /// - `Notify.args` and `Notify.payload` default to the empty map and the empty JSON object,
    ///   `heading` is read under `title`, and `tone` takes the enum's lenient spellings;
    /// - Phase 723 — `Place` holds an alias at each position a refusal can be located under one: its
    ///   `at` (read under `position`) is a `Point` record whose `x` is read under `left`, and its `mark`
    ///   is a `Mark` union whose `Pin` case reads `label` under `text` and `at` under `where`.
    let idl: Idl =
        { Kinds =
            [ kind
                  "Grid"
                  [ { field "cols" TInt Optional with
                        Presence = Some(WhenAbsent "template") }
                    field "template" TStr Optional ]
              kind
                  "Select"
                  [ field "multiple" TBool (OmitDefault(VBool false))
                    { field "value" TStr Optional with
                        Presence = Some(WhenEquals("multiple", VBool false)) }
                    { field "values" (TList TStr) Optional with
                        Presence = Some(WhenEquals("multiple", VBool true)) }
                    { field "options" (TList TStr) (OmitDefault(VList [])) with
                        Aliases = [ "source"; "suggestions" ] } ]
              kind
                  "Notify"
                  [ field "args" (TMap TStr) (OmitDefault(VMap []))
                    field "payload" TJson (OmitDefault(VJson(JObj [])))
                    { field "heading" TStr Optional with
                        Aliases = [ "title" ] }
                    field "tone" (TEnum "Tone") (OmitDefault(VEnum "Default")) ]
              kind
                  "Place"
                  [ { field "at" (TRecord("Point", [])) Optional with
                        Aliases = [ "position" ] }
                    field "mark" (TUnion("Mark", [])) Optional ] ]
          Unions =
            [ { Name = "Mark"
                Params = []
                Cases =
                  [ { Tag = "Pin"
                      Fields =
                        [ { field "label" TStr Required with
                              Aliases = [ "text" ] }
                          { field "at" (TRecord("Point", [])) Optional with
                              Aliases = [ "where" ] } ]
                      Annotations = Annotations.Empty
                      Hosted = None }
                    { Tag = "Dot"
                      Fields = []
                      Annotations = Annotations.Empty
                      Hosted = None } ]
                Annotations = Annotations.Empty
                TagRule = UnionTagRule.CaseName
                Discrimination = UnionDiscrimination.Tagged } ]
          Enums =
            [ { Declare.enumOf "Tone" [ "Default"; "Success"; "Critical" ] with
                  Aliases = [ "Success", [ "Positive" ]; "Critical", [ "Danger"; "Negative" ] ] } ]
          Records =
            [ { Name = "Point"
                Params = []
                Fields =
                  [ { field "x" TInt Required with
                        Aliases = [ "left" ] }
                    field "y" TInt Required ]
                Annotations = Annotations.Empty
                Inline = false } ]
          Defaults = []
          NodeFields = []
          Ops = []
          Wire = WireShape.Default
          Harden = HardenPolicy.Undeclared
          Contracts = [] }

    /// The fixture's artifact identity, so the file does not carry the UI vocabulary's description.
    let private identity: Artifact.Identity =
        { Name = Some "canonical-form-fixture"
          Description =
            Some
                "The Phase 539 fixture vocabulary: one kind per canonical-form declaration (a presence test, two equality tests, field aliases, an empty-map and an empty-JSON default) and an enum with case aliases." }

    /// One authored input: the vector's id, the law it exercises, the kind body as JSON text.
    let inputs: (string * string * string) list =
        [ "alias-first-spelling", "alias", """{"$type":"Select","source":["a"]}"""
          "alias-second-spelling", "alias", """{"$type":"Select","suggestions":["a"]}"""
          "alias-canonical-wins", "alias", """{"$type":"Select","options":["a"],"source":["b"]}"""
          // Phase 723 — two aliases and no canonical spelling: the first alias in declared order is read.
          "alias-declared-order", "alias", """{"$type":"Select","suggestions":["s"],"source":["a"]}"""
          "alias-field-on-optional", "alias", """{"$type":"Notify","title":"Hello"}"""
          "alias-enum-case", "alias", """{"$type":"Notify","tone":"Danger"}"""
          "alias-enum-case-second", "alias", """{"$type":"Notify","tone":"Negative"}"""
          "alias-enum-case-canonical", "alias", """{"$type":"Notify","tone":"Success"}"""
          "presence-holds", "presence", """{"$type":"Grid","cols":3}"""
          "presence-fails", "presence", """{"$type":"Grid","cols":3,"template":"1fr 2fr"}"""
          "presence-equals-default-holds", "presence", """{"$type":"Select","value":"x","values":["y"]}"""
          "presence-equals-explicit-holds",
          "presence",
          """{"$type":"Select","multiple":true,"value":"x","values":["y"]}"""
          "presence-equals-explicit-default", "presence", """{"$type":"Select","multiple":false,"value":"x"}"""
          // Phase 568 — a member whose condition fails is dropped by the DECODER, unread: its value may be
          // anything, ill-typed included. The vectors above re-encode to the same bytes whether the decoder
          // drops it or the encoder does, so a host whose decoder reads the member passes them all; each of
          // these carries an ill-typed value under a failed condition, which such a host refuses.
          "presence-fails-unread", "presence", """{"$type":"Grid","cols":"three","template":"1fr 2fr"}"""
          "presence-equals-fails-unread", "presence", """{"$type":"Select","multiple":true,"value":7,"values":["y"]}"""
          "presence-equals-default-fails-unread", "presence", """{"$type":"Select","value":"x","values":5}"""
          "default-empty-map-omitted", "default", """{"$type":"Notify","args":{}}"""
          "default-map-kept", "default", """{"$type":"Notify","args":{"k":"v"}}"""
          "default-empty-json-omitted", "default", """{"$type":"Notify","payload":{}}"""
          "default-json-kept", "default", """{"$type":"Notify","payload":{"b":[true],"a":"x"}}"""
          "default-enum-omitted", "default", """{"$type":"Notify","tone":"Default"}"""
          // Phase 723 — an alias at a member holding a record, inside the record, and inside a union case.
          "alias-record-member", "alias", """{"$type":"Place","position":{"left":1,"y":2}}"""
          "alias-union-case-field",
          "alias",
          """{"$type":"Place","mark":{"$type":"Pin","text":"a","where":{"x":1,"left":9,"y":2}}}""" ]

    /// Phase 723 (DECISIONS.md D288) — authored inputs the reference REFUSES. A defect inside a member
    /// read under an alias is located at the member AS WRITTEN, the alias, one vector per kind of
    /// position; where both spellings are present the canonical one is read, so the path names it; and a
    /// missing member's path ends at the absent key, its canonical spelling.
    let refusalInputs: (string * string * string) list =
        [ "refusal-alias-member", "alias", """{"$type":"Select","source":"not a list"}"""
          "refusal-alias-list-element", "alias", """{"$type":"Select","suggestions":["a",3]}"""
          "refusal-alias-both-spellings", "alias", """{"$type":"Select","options":5,"source":["a"]}"""
          "refusal-alias-nested-record", "alias", """{"$type":"Place","position":{"left":"one","y":2}}"""
          "refusal-alias-union-case-field", "alias", """{"$type":"Place","mark":{"$type":"Pin","text":5}}"""
          "refusal-alias-union-case-record",
          "alias",
          """{"$type":"Place","mark":{"$type":"Pin","label":"a","where":{"left":true,"y":2}}}"""
          "refusal-alias-missing-at-canonical", "alias", """{"$type":"Place","position":{"y":2}}""" ]

    /// The node document an input's kind body is carried in.
    let document (body: string) : string = """{"id":"n","kind":""" + body + "}"

    /// The canonical bytes the reference reads a document to.
    let canonical (doc: string) : Result<string, string> =
        Decode.decode idl doc |> Result.bind (Encode.encode idl)

    /// Phase 723 — the reference's refusal of a document: its code's name and its path, each segment a
    /// key as the document writes it or an index; `None` when the document decodes.
    let refusal (doc: string) : (string * PathSegment list) option =
        match Decode.decodeDetailed idl doc with
        | Ok _ -> None
        | Error e -> Some(DecodeError.codeName e.Code, e.Path)

    let private vector (id: string, law: string, body: string) : CorpusCell =
        let doc = document body

        match canonical doc with
        | Ok bytes ->
            CorpusCell.Row
                [ "id", CorpusFile.str id
                  "law", CorpusFile.str law
                  "document", CorpusFile.str doc
                  "expected", CorpusFile.str bytes
                  // No extracted model reads an IDL's aliases, presence or defaults (Phase 478).
                  VectorOracle.stamp id (CorpusFile.str bytes) None ]
        | Error e -> failwithf "the canonical-form input %s did not read: %s" id e

    let private refusalVector (id: string, law: string, body: string) : CorpusCell =
        let doc = document body

        match refusal doc with
        | Some(code, path) ->
            let refused =
                CorpusCell.Value(JObj [ "code", JStr code; "path", DecodePath.toJson path ])

            CorpusCell.Row
                [ "id", CorpusFile.str id
                  "law", CorpusFile.str law
                  "document", CorpusFile.str doc
                  "refusal", refused
                  VectorOracle.stamp id refused None ]
        | None -> failwithf "the canonical-form refusal input %s decoded" id

    let internal allVectors () : CorpusCell list =
        (inputs |> List.map vector) @ (refusalInputs |> List.map refusalVector)

    let private description =
        "The canonical-form declarations of an IDL vocabulary (Phase 539): a member's ALIASES (retired spellings a "
        + "decoder accepts and normalises to the canonical name, the canonical spelling winning when both are present, "
        + "otherwise the first alias in declared order), an enum case's aliases (lenient wire strings that decode to "
        + "the case and re-encode as its canonical string), a member's PRESENCE condition (`whenPresent` / `whenAbsent` "
        + "an Optional sibling, or `whenEquals` a sibling's scalar value, an absent OmitDefault sibling holding "
        + "its default; a member whose condition fails is absent from the canonical form, so a decoder drops it UNREAD, "
        + "whatever its value, ill-typed included, and it decodes as its optionality's absence), and an empty-map or JSON default literal (omitted at its default, "
        + "compared by canonical bytes). `vocabulary` is the fixture's idl.json; each vector reads `document` (a node "
        + "over that vocabulary) and expects `expected`, the canonical bytes the reference decodes and re-encodes it to, "
        + "or `refusal`, the decode `code` and the `path` to the fault (each segment a member's key or an index), "
        + "computed by calling the pinned kit. A path names the document AS WRITTEN (Phase 723): a fault inside a member "
        + "read under an alias is located at the alias, whether the member is a kind's, a record's or a union case's, "
        + "and at any depth beneath it; where both spellings are present the canonical one is read, so the path names "
        + "it; and a missing member's path ends at the absent key, its canonical spelling."

    /// Rendered with an explicit `kitVersion`, as its siblings in `LawVectorExport` are.
    let renderAt (stamp: string) : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "canonicalFormLaws"
                  "kitVersion", CorpusFile.str stamp
                  "description", CorpusFile.str description
                  "vocabulary", CorpusCell.Value(Artifact.jsonWith identity idl) ]
              Items = "vectors"
              Rows = allVectors () }
