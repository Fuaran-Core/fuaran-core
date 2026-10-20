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
    ///   `heading` is read under `title`, and `tone` takes the enum's lenient spellings.
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
                    field "tone" (TEnum "Tone") (OmitDefault(VEnum "Default")) ] ]
          Unions = []
          Enums =
            [ { Declare.enumOf "Tone" [ "Default"; "Success"; "Critical" ] with
                  Aliases = [ "Success", [ "Positive" ]; "Critical", [ "Danger"; "Negative" ] ] } ]
          Records = []
          Defaults = []
          NodeFields = []
          Ops = []
          Wire = WireShape.Default
          Harden = HardenPolicy.Undeclared }

    /// The fixture's artifact identity, so the file does not carry the UI vocabulary's description.
    let private identity: Artifact.Identity =
        { Name = Some "canonical-form-fixture"
          Description =
            "The Phase 539 fixture vocabulary: one kind per canonical-form declaration (a presence test, two equality tests, field aliases, an empty-map and an empty-JSON default) and an enum with case aliases." }

    /// One authored input: the vector's id, the law it exercises, the kind body as JSON text.
    let inputs: (string * string * string) list =
        [ "alias-first-spelling", "alias", """{"$type":"Select","source":["a"]}"""
          "alias-second-spelling", "alias", """{"$type":"Select","suggestions":["a"]}"""
          "alias-canonical-wins", "alias", """{"$type":"Select","options":["a"],"source":["b"]}"""
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
          "default-empty-map-omitted", "default", """{"$type":"Notify","args":{}}"""
          "default-map-kept", "default", """{"$type":"Notify","args":{"k":"v"}}"""
          "default-empty-json-omitted", "default", """{"$type":"Notify","payload":{}}"""
          "default-json-kept", "default", """{"$type":"Notify","payload":{"b":[true],"a":"x"}}"""
          "default-enum-omitted", "default", """{"$type":"Notify","tone":"Default"}""" ]

    /// The node document an input's kind body is carried in.
    let document (body: string) : string = """{"id":"n","kind":""" + body + "}"

    /// The canonical bytes the reference reads a document to.
    let canonical (doc: string) : Result<string, string> =
        Decode.decode idl doc |> Result.bind (Encode.encode idl)

    let private vector (id: string, law: string, body: string) : CorpusCell =
        let doc = document body

        match canonical doc with
        | Ok bytes ->
            CorpusCell.Row
                [ "id", CorpusFile.str id
                  "law", CorpusFile.str law
                  "document", CorpusFile.str doc
                  "expected", CorpusFile.str bytes ]
        | Error e -> failwithf "the canonical-form input %s did not read: %s" id e

    let internal allVectors () : CorpusCell list = inputs |> List.map vector

    let private description =
        "The canonical-form declarations of an IDL vocabulary (Phase 539): a member's ALIASES (retired spellings a "
        + "decoder accepts and normalises to the canonical name, the canonical spelling winning when both are present, "
        + "otherwise the first alias in declared order), an enum case's aliases (lenient wire strings that decode to "
        + "the case and re-encode as its canonical string), a member's PRESENCE condition (`whenPresent` / `whenAbsent` "
        + "an Optional sibling, or `whenEquals` a sibling's scalar value, an absent OmitDefault sibling holding "
        + "its default; a member whose condition fails is absent from the canonical form, so a decoder drops it and it "
        + "decodes as its optionality's absence), and an empty-map or JSON default literal (omitted at its default, "
        + "compared by canonical bytes). `vocabulary` is the fixture's idl.json; each vector reads `document` (a node "
        + "over that vocabulary) and expects `expected`, the canonical bytes the reference decodes and re-encodes it to, "
        + "computed by calling the pinned kit."

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
