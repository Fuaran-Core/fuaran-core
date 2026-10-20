namespace Fuaran.Core.Tests.IdlClassify

// The `idl.json` revisions the classifier is pinned over (Phase 127), and the three committed fixture
// files the `fuaran-core-idl` command legs run over. Declared here, in the fixtures library, because the
// tool writes the files (`render idl-classify-fixtures`); `IdlStabilityClassTests` is their family and
// holds each committed file to its declaration.

open System.IO
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Tests

module Fixtures =

    let ann = Annotations.Empty

    let fld name ty opt : IdlField =
        { Name = name
          Type = ty
          Opt = opt
          Aliases = []
          Presence = None
          Annotations = ann }

    let kindOf tag fields : IdlKind =
        { Tag = tag
          Category = "display"
          Fields = fields
          Annotations = ann }

    let case tag fields : IdlUnionCase =
        { Hosted = None
          Tag = tag
          Fields = fields
          Annotations = ann }

    /// The shared revision. Deliberately small and deliberately scalar-typed: the
    /// compile legs below feed `Gen.fsharpTypes`' output straight to `fsi`, so a
    /// field type that pulled in the node tree or a hosted codec would make those
    /// legs measure the reference closure rather than the shape change.
    let baseIdl: Idl =
        { Kinds = [ kindOf "Note" [ fld "body" TStr Required; fld "loudness" (TEnum "Loudness") Optional ] ]
          Unions =
            [ { TagRule = Fuaran.Core.Idl.UnionTagRule.CaseName
                Name = "Source"
                Params = []
                Cases =
                  [ case "Inline" [ fld "text" TStr Required ]
                    case "Ref" [ fld "id" TStr Required ] ]
                Annotations = Annotations.Empty } ]
          Enums =
            [ { Name = "Loudness"
                Cases = [ "Plain"; "Loud" ]
                Wires = []
                CaseAnnotations = []
                Aliases = []
                Annotations = Annotations.Empty } ]
          Records = []
          Defaults = []
          NodeFields = []
          Ops = []
          Wire = WireShape.Default
          Harden = HardenPolicy.Undeclared }

    let private withNoteFields (fields: IdlField list) : Idl =
        { baseIdl with
            Kinds = [ kindOf "Note" fields ] }

    let private noteFields = baseIdl.Kinds.Head.Fields

    /// The one the wire verdict and the F# verdict agree on.
    let requiredFieldAdded: Idl =
        withNoteFields (noteFields @ [ fld "caption" TStr Required ])

    /// The one they DISAGREE on — additive on the wire, a construction break in F#.
    let optionalFieldAdded: Idl =
        withNoteFields (noteFields @ [ fld "caption" TStr Optional ])

    let unionCaseAdded: Idl =
        { baseIdl with
            Unions =
                [ { baseIdl.Unions.Head with
                      Cases = baseIdl.Unions.Head.Cases @ [ case "Computed" [] ] } ] }

    let enumCaseAdded: Idl =
        { baseIdl with
            Enums =
                [ { baseIdl.Enums.Head with
                      Cases = baseIdl.Enums.Head.Cases @ [ "Soft" ] } ] }

    let kindRemoved: Idl = { baseIdl with Kinds = [] }

    /// A field whose optionality moves between two classes that BOTH emit a
    /// non-`option` F# member — the encoder body moves, the record shape does not.
    let omitDefaultInstead: Idl =
        withNoteFields
            [ fld "body" TStr (OmitDefault(VStr ""))
              fld "loudness" (TEnum "Loudness") Optional ]

    /// Phase 293 — an authoring default on the REQUIRED `body`: `mkNote` loses its `body`
    /// parameter, a construction break at every call site, with no record member moved.
    let defaultOnRequired: Idl =
        { baseIdl with
            Defaults =
                [ { Kind = "Note"
                    Field = "body"
                    Value = VStr "" } ] }

    /// Phase 293 — an authoring default on the OPTIONAL `loudness`: the constructor's body
    /// moves (`Some Plain` where it wrote `None`), its parameter list does not.
    let defaultOnOptional: Idl =
        { baseIdl with
            Defaults =
                [ { Kind = "Note"
                    Field = "loudness"
                    Value = VEnum "Plain" } ] }

// ---------------------------------------------------------------------------
// The committed CLI fixtures.
//
// Three artifact files the command legs at the foot of this file run
// `fuaran-core-idl` over — so the command is exercised by the repository gate
// through the suite the gate already runs, with no gate script naming it.
//
// They are GENERATED from the `Idl` values above rather than hand-written, and the
// guard below is what keeps that true: a fixture edited by hand, or left behind by
// a change to the artifact encoding, fails here naming the regeneration command
// instead of quietly certifying the command against stale bytes.
// ---------------------------------------------------------------------------

module Files =

    open System.IO

    /// `tests/Fuaran.Core.Tests/fixtures/idl-classify/<name>.json`, resolved from the
    /// repository root so the same three files serve the in-process legs and the
    /// command legs.
    let fixtureFiles: (string * Idl) list =
        [ "before", Fixtures.baseIdl
          "after-required-field", Fixtures.requiredFieldAdded
          "after-optional-field", Fixtures.optionalFieldAdded ]

    let fixtureDir = "tests/Fuaran.Core.Tests/fixtures/idl-classify"

    let fixturePath (name: string) =
        Snapshots.repoFile (fixtureDir + "/" + name + ".json")

    /// Rewrite the committed fixtures from the declarations above — the
    /// tool's `render idl-classify-fixtures`.
    let regen () =
        let dir = Path.GetDirectoryName(fixturePath "before")
        Directory.CreateDirectory dir |> ignore

        for (name, idl) in fixtureFiles do
            let p = fixturePath name
            File.WriteAllText(p, Artifact.render idl)
            printfn "regenerated %s/%s.json" fixtureDir name
